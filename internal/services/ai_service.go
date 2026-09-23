package services

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"strings"
	"sync"
	"time"

	"aegis-coach/internal/models"
)

const maxAiResponseBytes int64 = 2 * 1024 * 1024

// AiService AI 战术推理与模型端点服务
type AiService struct {
	mu         sync.RWMutex
	httpClient *http.Client
}

var (
	aiServiceInstance *AiService
	aiServiceOnce     sync.Once
)

func GetAiService() *AiService {
	aiServiceOnce.Do(func() {
		aiServiceInstance = &AiService{
			httpClient: &http.Client{Timeout: 15 * time.Second},
		}
	})
	return aiServiceInstance
}

// TestAiEndpoint 测试本地或云端大模型端点连通性
func (s *AiService) TestAiEndpoint(mode, endpoint, apiKey, model string) (*models.AiEndpointTestResultDTO, error) {
	isLocal := mode == "local_npu" || mode == "npu"

	cleanEndpoint := strings.TrimSpace(endpoint)
	if cleanEndpoint == "" {
		if isLocal {
			cleanEndpoint = "http://127.0.0.1:11434/v1"
		} else {
			cleanEndpoint = "https://api.deepseek.com/v1"
		}
	}
	cleanEndpoint = strings.TrimRight(cleanEndpoint, "/")

	cleanModel := strings.TrimSpace(model)
	if cleanModel == "" {
		if isLocal {
			cleanModel = "qwen3.5:1.5b"
		} else {
			cleanModel = "deepseek-chat"
		}
	}

	if !isLocal && strings.TrimSpace(apiKey) == "" {
		return &models.AiEndpointTestResultDTO{
			Success:      false,
			ModelUsed:    cleanModel,
			ErrorMessage: "云端 API Key 未配置，请前往 [设置 → AI 配置] 填写云端 API Key",
		}, nil
	}

	reqUrl := fmt.Sprintf("%s/chat/completions", cleanEndpoint)
	payload := map[string]any{
		"model":      cleanModel,
		"messages":   []map[string]string{{"role": "user", "content": "Ping"}},
		"max_tokens": 5,
	}
	bodyBytes, err := json.Marshal(payload)
	if err != nil {
		return &models.AiEndpointTestResultDTO{
			Success:      false,
			ModelUsed:    cleanModel,
			ErrorMessage: fmt.Sprintf("请求体序列化失败: %v", err),
		}, nil
	}

	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()

	start := time.Now()
	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, reqUrl, bytes.NewReader(bodyBytes))
	if err != nil {
		return &models.AiEndpointTestResultDTO{
			Success:      false,
			ModelUsed:    cleanModel,
			ErrorMessage: fmt.Sprintf("构造请求失败，请检查端点地址 [%s]: %v", cleanEndpoint, err),
		}, nil
	}
	httpReq.Header.Set("Content-Type", "application/json")
	if strings.TrimSpace(apiKey) != "" {
		httpReq.Header.Set("Authorization", fmt.Sprintf("Bearer %s", strings.TrimSpace(apiKey)))
	}

	resp, err := s.httpClient.Do(httpReq)
	latency := time.Since(start).Milliseconds()
	if err != nil {
		var msg string
		if errors.Is(err, context.DeadlineExceeded) {
			msg = fmt.Sprintf("连接超时（8秒），请检查网络或端点响应速度 [%s]", cleanEndpoint)
		} else if isLocal {
			msg = fmt.Sprintf("无法连接本地 Ollama [%s]，请确认已运行 `ollama serve` 且端口为 11434: %v", cleanEndpoint, err)
		} else {
			msg = fmt.Sprintf("无法连接 AI 端点 [%s]，请检查端点地址和网络连接: %v", cleanEndpoint, err)
		}
		return &models.AiEndpointTestResultDTO{
			Success:      false,
			LatencyMs:    latency,
			ModelUsed:    cleanModel,
			ErrorMessage: msg,
		}, nil
	}
	defer func() { _ = resp.Body.Close() }()

	limited := io.LimitReader(resp.Body, maxAiResponseBytes)
	respBody, _ := io.ReadAll(limited)

	if resp.StatusCode != http.StatusOK {
		errPreview := string(respBody)
		if len(errPreview) > 200 {
			errPreview = errPreview[:200] + "..."
		}
		var msg string
		switch resp.StatusCode {
		case http.StatusUnauthorized, http.StatusForbidden:
			msg = fmt.Sprintf("API Key 无效或已过期（HTTP %d），请检查 [设置 → AI 配置] 中的云端 API Key。服务端返回: %s", resp.StatusCode, errPreview)
		case http.StatusNotFound:
			if isLocal {
				msg = fmt.Sprintf("本地模型 [%s] 不存在（HTTP 404），请在设置中点击'拉取模型'下载。服务端返回: %s", cleanModel, errPreview)
			} else {
				msg = fmt.Sprintf("端点路径或模型名称错误（HTTP 404）。请检查端点地址是否以 /v1 结尾，以及模型名 [%s] 是否正确。服务端返回: %s", cleanModel, errPreview)
			}
		case http.StatusTooManyRequests:
			msg = "请求过于频繁被限流（HTTP 429），请稍后重试"
		case http.StatusBadRequest:
			msg = fmt.Sprintf("请求参数错误（HTTP 400），可能是模型名 [%s] 不被支持。服务端返回: %s", cleanModel, errPreview)
		default:
			if resp.StatusCode >= 500 {
				msg = fmt.Sprintf("AI 服务端错误（HTTP %d），请稍后重试或联系服务提供方。服务端返回: %s", resp.StatusCode, errPreview)
			} else {
				msg = fmt.Sprintf("AI 端点返回异常状态（HTTP %d）。服务端返回: %s", resp.StatusCode, errPreview)
			}
		}
		return &models.AiEndpointTestResultDTO{
			Success:      false,
			LatencyMs:    latency,
			RawResponse:  string(respBody),
			ErrorMessage: msg,
			ModelUsed:    cleanModel,
		}, nil
	}

	return &models.AiEndpointTestResultDTO{
		Success:     true,
		LatencyMs:   latency,
		RawResponse: string(respBody),
		ModelUsed:   cleanModel,
	}, nil
}

// FetchCloudModels 获取云端或兼容端点的模型列表
func (s *AiService) FetchCloudModels(baseUrl, apiKey string) ([]string, error) {
	cleanUrl := strings.TrimRight(strings.TrimSpace(baseUrl), "/")
	if cleanUrl == "" {
		cleanUrl = "https://api.deepseek.com/v1"
	}
	modelsUrl := fmt.Sprintf("%s/models", cleanUrl)

	ctx, cancel := context.WithTimeout(context.Background(), 6*time.Second)
	defer cancel()

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, modelsUrl, nil)
	if err != nil {
		return nil, err
	}
	if strings.TrimSpace(apiKey) != "" {
		req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", strings.TrimSpace(apiKey)))
	}

	resp, err := s.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer func() { _ = resp.Body.Close() }()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("HTTP %d", resp.StatusCode)
	}

	limited := io.LimitReader(resp.Body, maxAiResponseBytes)
	var result struct {
		Data []struct {
			ID string `json:"id"`
		} `json:"data"`
	}
	if err := json.NewDecoder(limited).Decode(&result); err != nil {
		return nil, err
	}

	var modelsList []string
	for _, m := range result.Data {
		if m.ID != "" {
			modelsList = append(modelsList, m.ID)
		}
	}
	return modelsList, nil
}

// AskCoach 即时战术问答（不做规则降级，AI 无效时返回详细错误）
func (s *AiService) AskCoach(
	question string,
	ctx *models.BattleContextDTO,
	mode, endpoint, apiKey, model string,
) (string, error) {
	prompt := s.buildCoachPrompt(question, ctx)
	return s.doChatCompletion(prompt, mode, endpoint, apiKey, model)
}

// GenerateMatchReview 赛后 AI 深度复盘报告 (4 维度结构化)
func (s *AiService) GenerateMatchReview(
	matchId string,
	matchDataJson string,
	playerSlot int,
	mode, endpoint, apiKey, model string,
) (*models.MatchReviewResultDTO, error) {
	prompt := fmt.Sprintf(`你是国际邀请赛 (TI) 冠军教练。请基于以下 Dota 2 比赛数据 (视角: 玩家槽位 %d) 进行深度战术复盘。

请严格遵照以下【4个维度结构】输出纯文本内容，禁止空话套话，使用标准中文 Dota 2 术语：

### 1. 【大盘与胜负定局复盘】
分析双方阵容的强势期分布、对线期战略走势与决定胜负的宏观节奏分水岭。

### 2. 【主视角实战得失剖析】
针对主视角操纵英雄的 KDA、输出伤害与关键团战执行，指出 1~2 个导致局势恶化的核心失误。

### 3. 【装备针对性与克制推演】
结合敌方阵容，分析主视角出装的克制针对性，点出面对敌方核心威胁时本局缺失的关键针对装。

### 4. 【下局致胜核心提升指南】
总结 3 条清晰、立即可落地的对线/中后期团战执行指南（每条一句话）。

比赛数据摘要: %s`, playerSlot, matchDataJson)

	reply, err := s.doChatCompletion(prompt, mode, endpoint, apiKey, model)
	if err != nil {
		return nil, err
	}

	return s.parseReviewSections(reply, model), nil
}

// parseReviewSections 解析 4 维度结构化复盘内容
func (s *AiService) parseReviewSections(rawText string, modelUsed string) *models.MatchReviewResultDTO {
	var overview, keyTurningPoints, itemizationAnalysis string
	var nextActions []string

	findSection := func(marker, fallback string) int {
		if i := strings.Index(rawText, marker); i != -1 {
			return i
		}
		return strings.Index(rawText, fallback)
	}

	p1 := findSection("### 1.", "1.")
	p2 := findSection("### 2.", "2.")
	p3 := findSection("### 3.", "3.")
	p4 := findSection("### 4.", "4.")

	if p1 != -1 && p2 != -1 && p2 > p1 {
		overview = strings.TrimSpace(rawText[p1:p2])
	}
	if p2 != -1 && p3 != -1 && p3 > p2 {
		keyTurningPoints = strings.TrimSpace(rawText[p2:p3])
	}
	if p3 != -1 && p4 != -1 && p4 > p3 {
		itemizationAnalysis = strings.TrimSpace(rawText[p3:p4])
	}
	if p4 != -1 && len(rawText) > p4 {
		p4Content := strings.TrimSpace(rawText[p4:])
		for _, line := range strings.Split(p4Content, "\n") {
			l := strings.TrimSpace(line)
			if len(l) > 3 && (strings.HasPrefix(l, "-") || strings.HasPrefix(l, "1.") || strings.HasPrefix(l, "2.") || strings.HasPrefix(l, "3.")) {
				nextActions = append(nextActions, strings.TrimLeft(l, "-123456789. "))
			}
		}
	}

	if overview == "" {
		overview = rawText
	}
	if keyTurningPoints == "" {
		keyTurningPoints = "对线期走位与关键团战先手机制存在提升空间。"
	}
	if itemizationAnalysis == "" {
		itemizationAnalysis = "针对敌方控制与爆发，建议评估更早做出 BKB 或闪烁匕首的必要性。"
	}
	if len(nextActions) == 0 {
		nextActions = []string{
			"中前期带线时务必观察小地图敌方关键先手英雄视野，避免盲目深入单走。",
			"团战避免第一波吃到敌方集中爆发，等待后手控制技能交出后再切入战场。",
			"均势局或劣势局优先积累买活资金，杜绝带盾前或守高前无买活暴毙。",
		}
	}

	return &models.MatchReviewResultDTO{
		Overview:            overview,
		KeyTurningPoints:    keyTurningPoints,
		ItemizationAnalysis: itemizationAnalysis,
		NextMatchActions:    nextActions,
		FullText:            rawText,
		ModelUsed:           modelUsed,
	}
}

func (s *AiService) buildCoachPrompt(question string, ctx *models.BattleContextDTO) string {
	var sb strings.Builder
	sb.WriteString("你是一位顶级 Dota 2 实时电竞战术教练。请基于当前局势，用 2~3 句精准、直接、可落地的战术指挥回答玩家。\n\n")

	if ctx != nil {
		sb.WriteString(fmt.Sprintf("【即时局势】\n英雄: %s (Lv.%d), 血量: %d%%, 蓝量: %d%%, 金钱: %d, 游戏时间: %s, KDA: %s\n",
			ctx.HeroName, ctx.HeroLevel, ctx.HealthPercent, ctx.ManaPercent, ctx.Gold, ctx.ClockTime, ctx.KDA))
		if len(ctx.Items) > 0 {
			sb.WriteString(fmt.Sprintf("当前装备: %s\n", strings.Join(ctx.Items, ", ")))
		}
		if len(ctx.Abilities) > 0 {
			sb.WriteString(fmt.Sprintf("核心技能: %s\n", strings.Join(ctx.Abilities, ", ")))
		}
		if strings.TrimSpace(ctx.PlayerProfileSnippet) != "" {
			sb.WriteString(fmt.Sprintf("玩家战绩特征: %s\n", strings.TrimSpace(ctx.PlayerProfileSnippet)))
		}
		if strings.TrimSpace(ctx.UserNotes) != "" {
			sb.WriteString(fmt.Sprintf("敌情备忘: %s\n", strings.TrimSpace(ctx.UserNotes)))
		}
	}

	sb.WriteString(fmt.Sprintf("\n【选手提问】%s\n【教练战术指导】", question))
	return sb.String()
}

// doChatCompletion 统一调用 OpenAI 兼容端点，按错误类型给出可操作提示
func (s *AiService) doChatCompletion(prompt, mode, endpoint, apiKey, model string) (string, error) {
	isLocal := mode == "local_npu" || mode == "npu"

	cleanEndpoint := strings.TrimSpace(endpoint)
	if cleanEndpoint == "" {
		if isLocal {
			cleanEndpoint = "http://127.0.0.1:11434/v1"
		} else {
			cleanEndpoint = "https://api.deepseek.com/v1"
		}
	}
	cleanEndpoint = strings.TrimRight(cleanEndpoint, "/")

	cleanModel := strings.TrimSpace(model)
	if cleanModel == "" {
		if isLocal {
			cleanModel = "qwen3.5:1.5b"
		} else {
			cleanModel = "deepseek-chat"
		}
	}

	// 预检查：云端模式必须配置 API Key
	if !isLocal && strings.TrimSpace(apiKey) == "" {
		return "", fmt.Errorf("云端 API Key 未配置，请前往 [设置 → AI 配置] 填写云端 API Key")
	}

	reqUrl := fmt.Sprintf("%s/chat/completions", cleanEndpoint)
	payload := map[string]any{
		"model": cleanModel,
		"messages": []map[string]string{
			{"role": "user", "content": prompt},
		},
		"temperature": 0.6,
		"max_tokens":  800,
	}

	bodyBytes, err := json.Marshal(payload)
	if err != nil {
		return "", fmt.Errorf("请求体序列化失败: %w", err)
	}

	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()

	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, reqUrl, bytes.NewReader(bodyBytes))
	if err != nil {
		return "", fmt.Errorf("构造请求失败，请检查端点地址 [%s]: %w", cleanEndpoint, err)
	}
	httpReq.Header.Set("Content-Type", "application/json")
	if strings.TrimSpace(apiKey) != "" {
		httpReq.Header.Set("Authorization", fmt.Sprintf("Bearer %s", strings.TrimSpace(apiKey)))
	}

	resp, err := s.httpClient.Do(httpReq)
	if err != nil {
		if errors.Is(err, context.DeadlineExceeded) {
			return "", fmt.Errorf("AI 端点连接超时（15秒），请检查网络或端点响应速度 [%s]", cleanEndpoint)
		}
		if isLocal {
			return "", fmt.Errorf("无法连接本地 Ollama [%s]，请确认已运行 `ollama serve` 且端口为 11434: %v", cleanEndpoint, err)
		}
		return "", fmt.Errorf("无法连接 AI 端点 [%s]，请检查端点地址和网络连接: %v", cleanEndpoint, err)
	}
	defer func() { _ = resp.Body.Close() }()

	limited := io.LimitReader(resp.Body, maxAiResponseBytes)
	respBody, _ := io.ReadAll(limited)

	if resp.StatusCode != http.StatusOK {
		errPreview := string(respBody)
		if len(errPreview) > 200 {
			errPreview = errPreview[:200] + "..."
		}

		switch resp.StatusCode {
		case http.StatusUnauthorized, http.StatusForbidden:
			return "", fmt.Errorf("API Key 无效或已过期（HTTP %d），请检查 [设置 → AI 配置] 中的云端 API Key。服务端返回: %s", resp.StatusCode, errPreview)
		case http.StatusNotFound:
			if isLocal {
				return "", fmt.Errorf("本地模型 [%s] 不存在（HTTP 404），请在设置中点击'拉取模型'下载。服务端返回: %s", cleanModel, errPreview)
			}
			return "", fmt.Errorf("端点路径或模型名称错误（HTTP 404）。请检查端点地址是否以 /v1 结尾，以及模型名 [%s] 是否正确。服务端返回: %s", cleanModel, errPreview)
		case http.StatusTooManyRequests:
			return "", fmt.Errorf("请求过于频繁被限流（HTTP 429），请稍后重试")
		case http.StatusBadRequest:
			return "", fmt.Errorf("请求参数错误（HTTP 400），可能是模型名 [%s] 不被支持。服务端返回: %s", cleanModel, errPreview)
		default:
			if resp.StatusCode >= 500 {
				return "", fmt.Errorf("AI 服务端错误（HTTP %d），请稍后重试或联系服务提供方。服务端返回: %s", resp.StatusCode, errPreview)
			}
			return "", fmt.Errorf("AI 端点返回异常状态（HTTP %d）。服务端返回: %s", resp.StatusCode, errPreview)
		}
	}

	var chatResp struct {
		Choices []struct {
			Message struct {
				Content string `json:"content"`
			} `json:"message"`
		} `json:"choices"`
		Error *struct {
			Message string `json:"message"`
			Type    string `json:"type"`
		} `json:"error,omitempty"`
	}
	if err := json.Unmarshal(respBody, &chatResp); err != nil {
		return "", fmt.Errorf("端点返回不是 OpenAI 兼容格式，请确认 [%s] 是否为 /v1/chat/completions 规范端点: %v", cleanEndpoint, err)
	}

	if chatResp.Error != nil && chatResp.Error.Message != "" {
		return "", fmt.Errorf("AI 端点返回错误: %s", chatResp.Error.Message)
	}

	if len(chatResp.Choices) == 0 {
		return "", fmt.Errorf("AI 端点返回结果不含 choices 字段，可能端点不是 OpenAI 兼容格式")
	}

	content := strings.TrimSpace(chatResp.Choices[0].Message.Content)
	if content == "" {
		return "", fmt.Errorf("AI 模型返回内容为空，可能是模型 [%s] 未能正常生成回复，请检查模型状态", cleanModel)
	}

	return content, nil
}

// PullLocalModel 向本地 Ollama 服务发起模型下载拉取
func (s *AiService) PullLocalModel(modelName, endpoint string) (string, error) {
	cleanName := strings.TrimSpace(modelName)
	if cleanName == "" {
		return "", fmt.Errorf("模型名称不能为空")
	}

	cleanEndpoint := strings.TrimSpace(endpoint)
	if cleanEndpoint == "" {
		cleanEndpoint = "http://127.0.0.1:11434"
	}
	cleanEndpoint = strings.TrimRight(cleanEndpoint, "/")
	cleanEndpoint = strings.TrimSuffix(cleanEndpoint, "/v1")

	pullUrl := fmt.Sprintf("%s/api/pull", cleanEndpoint)
	payload, _ := json.Marshal(map[string]any{"name": cleanName, "stream": false})

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Minute)
	defer cancel()

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, pullUrl, bytes.NewReader(payload))
	if err == nil {
		req.Header.Set("Content-Type", "application/json")
		resp, reqErr := s.httpClient.Do(req)
		if reqErr == nil {
			defer resp.Body.Close()
			if resp.StatusCode == http.StatusOK {
				return "success", nil
			}
		}
	}

	return "", fmt.Errorf("未检测到本地已运行的 Ollama 服务 (端点: %s)。请确认 Ollama 已启动，或在终端手动执行: ollama run %s", cleanEndpoint, cleanName)
}
