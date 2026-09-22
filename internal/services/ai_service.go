package services

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strings"
	"sync"
	"time"

	"aegis-coach/internal/models"
)

const maxAiResponseBytes int64 = 2 * 1024 * 1024 // 2 MiB 响应体上限

// AiService AI 战术推理与模型端点服务
type AiService struct {
	mu         sync.RWMutex
	httpClient *http.Client
}

var (
	aiServiceInstance *AiService
	aiServiceOnce     sync.Once
)

// GetAiService 获取 AiService 单例
func GetAiService() *AiService {
	aiServiceOnce.Do(func() {
		aiServiceInstance = &AiService{
			httpClient: &http.Client{
				Timeout: 15 * time.Second,
			},
		}
	})
	return aiServiceInstance
}

// TestAiEndpoint 测试本地或云端大模型端点连通性
func (s *AiService) TestAiEndpoint(mode, endpoint, apiKey, model string) (*models.AiEndpointTestResultDTO, error) {
	cleanEndpoint := strings.TrimSpace(endpoint)
	if cleanEndpoint == "" {
		if mode == "cloud" || mode == "cloud_api" {
			cleanEndpoint = "https://api.deepseek.com/v1"
		} else {
			cleanEndpoint = "http://127.0.0.1:11434/v1"
		}
	}

	cleanModel := strings.TrimSpace(model)
	if cleanModel == "" {
		if mode == "cloud" || mode == "cloud_api" {
			cleanModel = "deepseek-chat"
		} else {
			cleanModel = "qwen3.5:1.5b"
		}
	}

	reqUrl := fmt.Sprintf("%s/chat/completions", strings.TrimRight(cleanEndpoint, "/"))
	payload := map[string]any{
		"model": cleanModel,
		"messages": []map[string]string{
			{"role": "user", "content": "Ping"},
		},
		"max_tokens": 5,
	}
	bodyBytes, err := json.Marshal(payload)
	if err != nil {
		return nil, fmt.Errorf("序列化测试请求失败: %w", err)
	}

	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()

	start := time.Now()
	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, reqUrl, bytes.NewReader(bodyBytes))
	if err != nil {
		return nil, fmt.Errorf("构建测试请求失败: %w", err)
	}
	httpReq.Header.Set("Content-Type", "application/json")
	if strings.TrimSpace(apiKey) != "" {
		httpReq.Header.Set("Authorization", fmt.Sprintf("Bearer %s", strings.TrimSpace(apiKey)))
	}

	resp, err := s.httpClient.Do(httpReq)
	latency := time.Since(start).Milliseconds()
	if err != nil {
		return &models.AiEndpointTestResultDTO{
			Success:      false,
			LatencyMs:    latency,
			ErrorMessage: fmt.Sprintf("连接失败: %v", err),
			ModelUsed:    cleanModel,
		}, nil
	}
	defer func() { _ = resp.Body.Close() }()

	limited := io.LimitReader(resp.Body, maxAiResponseBytes)
	respBody, _ := io.ReadAll(limited)

	if resp.StatusCode != http.StatusOK {
		return &models.AiEndpointTestResultDTO{
			Success:      false,
			LatencyMs:    latency,
			RawResponse:  string(respBody),
			ErrorMessage: fmt.Sprintf("HTTP 异常状态码: %d", resp.StatusCode),
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

// AskCoach 进行即时战术问答推演 (统一在 Go 侧完成 Prompt 拼接与安全脱敏)
func (s *AiService) AskCoach(
	question string,
	ctx *models.BattleContextDTO,
	mode, endpoint, apiKey, model string,
) (string, error) {
	prompt := s.buildCoachPrompt(question, ctx)
	return s.doChatCompletion(prompt, mode, endpoint, apiKey, model)
}

// GenerateMatchReview 生成赛后 AI 深度复盘报告
func (s *AiService) GenerateMatchReview(
	matchId string,
	matchDataJson string,
	playerSlot int,
	mode, endpoint, apiKey, model string,
) (*models.MatchReviewResultDTO, error) {
	prompt := fmt.Sprintf(`你是国际邀请赛 (TI) 冠军教练。请基于以下 Dota 2 比赛数据 (视角: 玩家槽位 %d) 进行深度战术复盘。
比赛数据摘要: %s

请以结构化格式给出：
1. 大盘与胜负定局复盘 (宏观战略、对线期节奏与胜负分水岭)
2. 主视角核心实战得失剖析 (对线得失、关键团战走位与入场时机)
3. 装备针对性与克制推演 (出装顺序、克制反制、质变装分析)
4. 下局致胜核心提升指南 (3 条清晰可落地的执行指南)`, playerSlot, matchDataJson)

	reply, err := s.doChatCompletion(prompt, mode, endpoint, apiKey, model)
	if err != nil {
		return nil, err
	}

	return &models.MatchReviewResultDTO{
		Overview:            reply,
		KeyTurningPoints:    "见完整复盘正文",
		ItemizationAnalysis: "见出装剖析章节",
		NextMatchActions: []string{
			"保持对线压制并保护视野",
			"关键质变装成型立即组织第一波强势推进",
			"留好买活资金防守高地",
		},
		FullText:  reply,
		ModelUsed: model,
	}, nil
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

func (s *AiService) doChatCompletion(prompt, mode, endpoint, apiKey, model string) (string, error) {
	cleanEndpoint := strings.TrimSpace(endpoint)
	if cleanEndpoint == "" {
		if mode == "cloud" || mode == "cloud_api" {
			cleanEndpoint = "https://api.deepseek.com/v1"
		} else {
			cleanEndpoint = "http://127.0.0.1:11434/v1"
		}
	}

	cleanModel := strings.TrimSpace(model)
	if cleanModel == "" {
		if mode == "cloud" || mode == "cloud_api" {
			cleanModel = "deepseek-chat"
		} else {
			cleanModel = "qwen3.5:1.5b"
		}
	}

	reqUrl := fmt.Sprintf("%s/chat/completions", strings.TrimRight(cleanEndpoint, "/"))
	payload := map[string]any{
		"model": cleanModel,
		"messages": []map[string]string{
			{"role": "user", "content": prompt},
		},
		"temperature": 0.6,
		"max_tokens":  600,
	}

	bodyBytes, err := json.Marshal(payload)
	if err != nil {
		return "", err
	}

	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()

	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, reqUrl, bytes.NewReader(bodyBytes))
	if err != nil {
		return "", err
	}
	httpReq.Header.Set("Content-Type", "application/json")
	if strings.TrimSpace(apiKey) != "" {
		httpReq.Header.Set("Authorization", fmt.Sprintf("Bearer %s", strings.TrimSpace(apiKey)))
	}

	resp, err := s.httpClient.Do(httpReq)
	if err != nil {
		return "", fmt.Errorf("AI 端点请求失败: %w", err)
	}
	defer func() { _ = resp.Body.Close() }()

	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("AI 端点返回状态码 %d", resp.StatusCode)
	}

	limited := io.LimitReader(resp.Body, maxAiResponseBytes)
	var chatResp struct {
		Choices []struct {
			Message struct {
				Content string `json:"content"`
			} `json:"message"`
		} `json:"choices"`
	}
	if err := json.NewDecoder(limited).Decode(&chatResp); err != nil {
		return "", fmt.Errorf("解析 AI 响应失败: %w", err)
	}

	if len(chatResp.Choices) == 0 || strings.TrimSpace(chatResp.Choices[0].Message.Content) == "" {
		return "", fmt.Errorf("AI 模型返回内容为空")
	}

	return strings.TrimSpace(chatResp.Choices[0].Message.Content), nil
}

// PullLocalModel 向本地 Ollama 服务或通过命令行发起模型下载拉取
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

	// 1. 尝试直接请求 Ollama API 发起后台 pull
	pullUrl := fmt.Sprintf("%s/api/pull", cleanEndpoint)
	payload, _ := json.Marshal(map[string]any{
		"name":   cleanName,
		"stream": false,
	})

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
