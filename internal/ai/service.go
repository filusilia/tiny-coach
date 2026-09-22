package ai

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strings"
	"time"

	"aegis-coach/internal/dota"
)

// Service AI 战术推理与端侧/云端模型调度服务
type Service struct {
	httpClient *http.Client
}

// NewService 构造 AI 推理服务实例，初始化复用 HTTP 长连接池
func NewService() *Service {
	return &Service{
		httpClient: &http.Client{
			Timeout: 20 * time.Second,
			Transport: &http.Transport{
				MaxIdleConns:        100,
				MaxIdleConnsPerHost: 20,
				IdleConnTimeout:     90 * time.Second,
			},
		},
	}
}

// OpenAiChatRequest 兼容 OpenAI / DeepSeek / Ollama v1 协议请求实体
type OpenAiChatRequest struct {
	Model       string              `json:"model"`
	Messages    []OpenAiChatMessage `json:"messages"`
	Temperature float64             `json:"temperature"`
	MaxTokens   int                 `json:"max_tokens,omitempty"`
}

// OpenAiChatMessage 对话消息实体
type OpenAiChatMessage struct {
	Role    string `json:"role"`
	Content string `json:"content"`
}

// OpenAiChatResponse 兼容响应实体
type OpenAiChatResponse struct {
	Choices []struct {
		Message struct {
			Role    string `json:"role"`
			Content string `json:"content"`
		} `json:"message"`
	} `json:"choices"`
	Error *struct {
		Message string `json:"message"`
		Type    string `json:"type"`
	} `json:"error,omitempty"`
}

// FormatGameContext 将实时对局战况与选手战绩画像拼装为规范化 Prompt 片段
func FormatGameContext(ctx TacticalContext) string {
	timeStr := "未知"
	if ctx.ClockTime != 0 {
		min := ctx.ClockTime / 60
		sec := ctx.ClockTime % 60
		if sec < 0 {
			sec = -sec
		}
		timeStr = fmt.Sprintf("%d:%02d", min, sec)
	}

	heroName := ctx.HeroName
	if heroName == "" && ctx.HeroID > 0 {
		heroName = dota.GetHeroChineseName(ctx.HeroID)
	}
	if heroName == "" {
		heroName = "当前英雄"
	}

	level := ctx.HeroLevel
	if level <= 0 {
		level = 1
	}

	kda := ctx.KDA
	if kda == "" {
		kda = "0/0/0"
	}

	itemsStr := strings.Join(ctx.Items, ", ")
	if itemsStr == "" {
		itemsStr = "空背包"
	}

	abilitiesStr := strings.Join(ctx.Abilities, ", ")
	if abilitiesStr == "" {
		abilitiesStr = "未知"
	}

	var sb strings.Builder
	sb.WriteString("【实战对局快照】\n")
	sb.WriteString(fmt.Sprintf("- 比赛时钟: %s | 英雄: %s (Lv.%d)\n", timeStr, heroName, level))
	sb.WriteString(fmt.Sprintf("- 状态: %d%% HP | %d%% MP\n", ctx.HealthPercent, ctx.ManaPercent))
	sb.WriteString(fmt.Sprintf("- 战绩与经济: %s | 储备金币: %d\n", kda, ctx.Gold))
	sb.WriteString(fmt.Sprintf("- 现有装备: [%s]\n", itemsStr))
	sb.WriteString(fmt.Sprintf("- 技能状态: [%s]\n", abilitiesStr))

	if strings.TrimSpace(ctx.UserNotes) != "" {
		sb.WriteString(fmt.Sprintf("- 战场敌情备忘: \"%s\"\n", strings.TrimSpace(ctx.UserNotes)))
	}
	if strings.TrimSpace(ctx.PlayerProfileSnippet) != "" {
		sb.WriteString("\n" + strings.TrimSpace(ctx.PlayerProfileSnippet) + "\n")
	}

	return strings.TrimSpace(sb.String())
}

// AskCoach 执行实战战术即时问答 (支持 Solution Hierarchy 与离线保底)
func (s *Service) AskCoach(
	question string,
	ctx TacticalContext,
	mode string,
	endpoint string,
	apiKey string,
	model string,
	timeoutSec int,
) (*CoachReply, error) {
	if mode == "rule_only" {
		fallback := GenerateDeterministicAdvice(question, ctx)
		return &CoachReply{
			Answer:    fallback,
			ModelUsed: "确定性规则引擎 (离线纯规则模式)",
			IsNpu:     false,
		}, nil
	}

	isNpu := mode == "local_npu" || mode == "npu"
	targetEndpoint := strings.TrimRight(endpoint, "/")
	if targetEndpoint == "" {
		if isNpu {
			targetEndpoint = "http://127.0.0.1:11434/v1"
		} else {
			targetEndpoint = "https://api.deepseek.com/v1"
		}
	}

	targetModel := strings.TrimSpace(model)
	if targetModel == "" {
		if isNpu {
			targetModel = "qwen3.5:1.5b"
		} else {
			targetModel = "deepseek-chat"
		}
	}

	if timeoutSec <= 0 {
		timeoutSec = 8
	}

	systemPrompt := `你是一名 Dota 2 顶尖电竞教练。
你的职责是基于当前选手的实战对局快照与战场敌情，给出精确、果断、立竿见影的实战决策。

【核心决策优先级（Solution Hierarchy 必须严格执行）】
1. 最高优先级：优先检查身上已就绪的技能或装备（[就绪]），零经济成本化解眼前危机。
2. 次优先级：检查核心保命或关键控制技能的冷却状态（[CD]），若即将就绪，指导拉扯拖延等待 CD。
3. 创造性利用现有战场资源（身位拉扯、走位掩体、队友掩护）。
4. 最低优先级（严禁盲目劝买）：只有在身上技能与现有装备均无法应对时，才推荐合成新装备，且推荐必须契合英雄主属性、输出类型（物理/法术）与团队定位。

【选手战绩与对阵风格融合】
- 若快照包含【玩家近期战绩画像】，必须结合选手历史英雄熟练度与风格弱点（如场均阵亡偏高需侧重自保出装与防游走视野，高熟练度英雄需强化质变装节奏），生成契合其水平的针对性战术。

【输出要求】
- 必须使用纯正 Dota 2 游戏术语（严禁使用任何 LOL 术语）。
- 严格限制在 2~3 句话以内，拒绝空话套话（不要说“注意走位”、“认真补刀”等废话）。
- 必须明确“立即对谁用什么”或“留钱合成哪件部件”。`

	userPrompt := fmt.Sprintf("%s\n\n【选手战术提问】\n\"%s\"\n\n请按照优先级给出精准可执行的 2~3 句行动建议：",
		FormatGameContext(ctx), strings.TrimSpace(question))

	reqBody := OpenAiChatRequest{
		Model: targetModel,
		Messages: []OpenAiChatMessage{
			{Role: "system", Content: systemPrompt},
			{Role: "user", Content: userPrompt},
		},
		Temperature: 0.3,
		MaxTokens:   300,
	}

	jsonBytes, err := json.Marshal(reqBody)
	if err != nil {
		return s.fallbackWithRule(question, ctx, isNpu, "请求数据编码失败"), nil
	}

	httpCtx, cancel := context.WithTimeout(context.Background(), time.Duration(timeoutSec)*time.Second)
	defer cancel()

	apiUrl := targetEndpoint + "/chat/completions"
	httpReq, err := http.NewRequestWithContext(httpCtx, http.MethodPost, apiUrl, bytes.NewReader(jsonBytes))
	if err != nil {
		return s.fallbackWithRule(question, ctx, isNpu, "创建 HTTP 请求失败"), nil
	}

	httpReq.Header.Set("Content-Type", "application/json")
	httpReq.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")
	if !isNpu && strings.TrimSpace(apiKey) != "" {
		httpReq.Header.Set("Authorization", "Bearer "+strings.TrimSpace(apiKey))
	}

	resp, err := s.httpClient.Do(httpReq)
	if err != nil {
		return s.fallbackWithRule(question, ctx, isNpu, fmt.Sprintf("连接模型超时或失败: %v", err)), nil
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		errBytes, _ := io.ReadAll(resp.Body)
		return s.fallbackWithRule(question, ctx, isNpu, fmt.Sprintf("HTTP %d: %s", resp.StatusCode, string(errBytes))), nil
	}

	var chatResp OpenAiChatResponse
	if err := json.NewDecoder(resp.Body).Decode(&chatResp); err != nil || len(chatResp.Choices) == 0 {
		return s.fallbackWithRule(question, ctx, isNpu, "解析模型响应异常"), nil
	}

	replyContent := strings.TrimSpace(chatResp.Choices[0].Message.Content)
	if replyContent == "" {
		return s.fallbackWithRule(question, ctx, isNpu, "模型返回空内容"), nil
	}

	modelTag := targetModel
	if isNpu {
		modelTag = "[NPU/Local] " + targetModel
	} else {
		modelTag = "[Cloud] " + targetModel
	}

	return &CoachReply{
		Answer:    replyContent,
		ModelUsed: modelTag,
		IsNpu:     isNpu,
	}, nil
}

// fallbackWithRule 战术提问异常时秒级触发确定性规则兜底
func (s *Service) fallbackWithRule(question string, ctx TacticalContext, isNpu bool, reason string) *CoachReply {
	fallbackText := GenerateDeterministicAdvice(question, ctx)
	modelTag := "[云端 规则降级]"
	if isNpu {
		modelTag = "[NPU 规则降级]"
	}
	return &CoachReply{
		Answer:    fmt.Sprintf("%s (AI服务异常已自动降级至战术规则库: %s)", fallbackText, reason),
		ModelUsed: modelTag,
		IsNpu:     isNpu,
	}
}

// GenerateDeterministicAdvice 确定性规则保底战术生成器
func GenerateDeterministicAdvice(question string, ctx TacticalContext) string {
	q := strings.ToLower(question)
	gold := ctx.Gold

	if strings.Contains(q, "装备") || strings.Contains(q, "出什么") || strings.Contains(q, "装") {
		if gold >= 2250 {
			return "当前金钱充裕，核心位优先做出黑皇杖(BKB)或闪烁匕首(跳刀)，把握第一波核心大件成型的强势进攻期。"
		}
		return "优先补齐基础属性装与消耗品，积攒金钱直奔核心大件关键散件，避免频繁买零碎配件拖慢大件节奏。"
	}
	if strings.Contains(q, "肉山") || strings.Contains(q, "盾") || strings.Contains(q, "roshan") {
		return "提前排布肉山高坡视野，检查全队大招与 BKB 冷却，开雾抱团逼近巢穴控盾。"
	}
	if strings.Contains(q, "团") || strings.Contains(q, "进场") || strings.Contains(q, "开") {
		return "后手切入敌方后排酱油，避免第一波被敌方先手集火，等待关键控制技能互换后再入场收割。"
	}
	if strings.Contains(q, "线") || strings.Contains(q, "刷") || strings.Contains(q, "发育") {
		return "观察小地图敌方露头人数，大优控盾推二塔压缩野区，劣势避战收安全线等待对方冒进失误。"
	}

	return "保持良好身位，核心大件合成前避免盲目无视野接团，优先与团队关键控制位抱团行动。"
}

// GenerateMatchReview 针对单场比赛生成赛后 AI 深度复盘报告 (吸收 BrightGir 4 维度层级)
func (s *Service) GenerateMatchReview(
	matchJson string,
	targetPlayerSlot int,
	targetAccountId string,
	mode string,
	endpoint string,
	apiKey string,
	model string,
) (*MatchReviewResult, error) {
	var match map[string]interface{}
	if err := json.Unmarshal([]byte(matchJson), &match); err != nil {
		return nil, fmt.Errorf("比赛数据 JSON 解析失败: %w", err)
	}

	// 提取比赛基础数据
	matchID, _ := match["match_id"].(float64)
	duration, _ := match["duration"].(float64)
	radiantWin, _ := match["radiant_win"].(bool)
	radiantScore, _ := match["radiant_score"].(float64)
	direScore, _ := match["dire_score"].(float64)

	rawPlayers, _ := match["players"].([]interface{})
	var targetPlayer map[string]interface{}
	var radiantHeroes, direHeroes []string

	for _, p := range rawPlayers {
		pm, ok := p.(map[string]interface{})
		if !ok {
			continue
		}
		slot, _ := pm["player_slot"].(float64)
		hid, _ := pm["hero_id"].(float64)
		hName := dota.GetHeroChineseName(int(hid))

		isRadiant := slot < 128
		if isRadiant {
			radiantHeroes = append(radiantHeroes, hName)
		} else {
			direHeroes = append(direHeroes, hName)
		}

		// 匹配主视角分析玩家
		if targetPlayerSlot >= 0 && int(slot) == targetPlayerSlot {
			targetPlayer = pm
		} else if targetPlayer == nil && targetAccountId != "" {
			accId, _ := pm["account_id"].(float64)
			if fmt.Sprintf("%.0f", accId) == targetAccountId {
				targetPlayer = pm
			}
		}
	}

	if targetPlayer == nil && len(rawPlayers) > 0 {
		targetPlayer, _ = rawPlayers[0].(map[string]interface{})
	}
	if targetPlayer == nil {
		targetPlayer = make(map[string]interface{})
	}

	targetHeroId, _ := targetPlayer["hero_id"].(float64)
	targetHeroName := dota.GetHeroChineseName(int(targetHeroId))
	targetSlot, _ := targetPlayer["player_slot"].(float64)
	isTargetRadiant := targetSlot < 128
	isWin := (isTargetRadiant && radiantWin) || (!isTargetRadiant && !radiantWin)

	// 离线确定性保底模式
	if mode == "rule_only" {
		return s.generateDeterministicReview(match, targetPlayer, isWin, targetHeroName, radiantHeroes, direHeroes, "确定性规则引擎 (离线保底)"), nil
	}

	isNpu := mode == "local_npu" || mode == "npu"
	targetEndpoint := strings.TrimRight(endpoint, "/")
	if targetEndpoint == "" {
		if isNpu {
			targetEndpoint = "http://127.0.0.1:11434/v1"
		} else {
			targetEndpoint = "https://api.deepseek.com/v1"
		}
	}

	targetModel := strings.TrimSpace(model)
	if targetModel == "" {
		if isNpu {
			targetModel = "qwen3.5:1.5b"
		} else {
			targetModel = "deepseek-chat"
		}
	}

	durationMin := int(duration) / 60
	durationSec := int(duration) % 60
	winText := "失利"
	if isWin {
		winText = "胜利"
	}

	kills, _ := targetPlayer["kills"].(float64)
	deaths, _ := targetPlayer["deaths"].(float64)
	assists, _ := targetPlayer["assists"].(float64)
	gpm, _ := targetPlayer["gold_per_min"].(float64)
	xpm, _ := targetPlayer["xp_per_min"].(float64)
	damage, _ := targetPlayer["hero_damage"].(float64)
	towerDamage, _ := targetPlayer["tower_damage"].(float64)
	lastHits, _ := targetPlayer["last_hits"].(float64)
	denies, _ := targetPlayer["denies"].(float64)
	pName, _ := targetPlayer["personaname"].(string)
	if pName == "" {
		pName = "选手"
	}

	userPrompt := fmt.Sprintf(`【Dota 2 对局核心数据快照】
- 比赛编号: #%.0f
- 对局时长: %d分%02d秒
- 胜负判定: 本场%s (天辉 %.0f : 夜魇 %.0f)
- 天辉阵容: %s
- 夜魇阵容: %s
- 主分析视角玩家: %s (%s)
- 操纵英雄: %s
- 个人战绩: %.0f 击杀 / %.0f 阵亡 / %.0f 助攻
- 经济与经验: GPM %.0f / XPM %.0f
- 英雄伤害: %.0f | 建筑伤害: %.0f
- 正/反补刀: %.0f / %.0f

请依据上述客观事实，给出高水平专业战术复盘：`,
		matchID, durationMin, durationSec, winText, radiantScore, direScore,
		strings.Join(radiantHeroes, "、"), strings.Join(direHeroes, "、"),
		pName, map[bool]string{true: "天辉方", false: "夜魇方"}[isTargetRadiant],
		targetHeroName, kills, deaths, assists, gpm, xpm, damage, towerDamage, lastHits, denies)

	enemyHeroes := direHeroes
	if !isTargetRadiant {
		enemyHeroes = radiantHeroes
	}

	systemPrompt := fmt.Sprintf(`你是一名 Dota 2 职业战队高级分析师与专业电竞教练。
你的职责是基于客观战况数据与双方阵容克制关系，给出深度、见解独到、直击痛点的赛后复盘。

请严格遵照以下【4个维度结构】输出纯文本内容，禁止空话套话，使用标准中文 Dota 2 术语：
### 1. 【大盘与胜负定局复盘】
分析双方阵容的强势期分布、对线期战略走势与决定胜负的宏观节奏分水岭（如控盾推塔时机、兵线压力、上高阵型）。
### 2. 【主视角实战得失剖析】
针对操纵英雄 %s 的 KDA（%.0f/%.0f/%.0f）与输出伤害（%.0f），肯定其对线或团战贡献，指出 1~2 个导致局势恶化或阵亡过多的核心执行失误（如站位靠前、拉扯脱节、入场时机偏差）。
### 3. 【装备针对性与克制推演】
结合敌方阵容（%s），分析主视角出装的克制针对性，点出面对敌方核心威胁时本局缺失的关键针对装（例如：面对多控制是否必须提早做黑皇杖(BKB)、面对闪避是否需金箍棒、面对肉核是否需减甲/冰眼等）。
### 4. 【下局致胜核心提升指南】
总结 3 条清晰、立即可落地的对线/中后期团战执行指南（每条一句话），帮助选手在下局立刻形成战术肌肉记忆。`,
		targetHeroName, kills, deaths, assists, damage, strings.Join(enemyHeroes, "、"))

	reqBody := OpenAiChatRequest{
		Model: targetModel,
		Messages: []OpenAiChatMessage{
			{Role: "system", Content: systemPrompt},
			{Role: "user", Content: userPrompt},
		},
		Temperature: 0.4,
		MaxTokens:   800,
	}

	jsonBytes, _ := json.Marshal(reqBody)
	httpCtx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()

	apiUrl := targetEndpoint + "/chat/completions"
	httpReq, err := http.NewRequestWithContext(httpCtx, http.MethodPost, apiUrl, bytes.NewReader(jsonBytes))
	if err != nil {
		return s.generateDeterministicReview(match, targetPlayer, isWin, targetHeroName, radiantHeroes, direHeroes, "规则降级: 创建请求失败"), nil
	}

	httpReq.Header.Set("Content-Type", "application/json")
	httpReq.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")
	if !isNpu && strings.TrimSpace(apiKey) != "" {
		httpReq.Header.Set("Authorization", "Bearer "+strings.TrimSpace(apiKey))
	}

	resp, err := s.httpClient.Do(httpReq)
	if err != nil {
		return s.generateDeterministicReview(match, targetPlayer, isWin, targetHeroName, radiantHeroes, direHeroes, fmt.Sprintf("规则降级: 请求超时或失败 (%v)", err)), nil
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return s.generateDeterministicReview(match, targetPlayer, isWin, targetHeroName, radiantHeroes, direHeroes, fmt.Sprintf("规则降级: HTTP %d", resp.StatusCode)), nil
	}

	var chatResp OpenAiChatResponse
	if err := json.NewDecoder(resp.Body).Decode(&chatResp); err != nil || len(chatResp.Choices) == 0 {
		return s.generateDeterministicReview(match, targetPlayer, isWin, targetHeroName, radiantHeroes, direHeroes, "规则降级: 解析响应异常"), nil
	}

	rawText := strings.TrimSpace(chatResp.Choices[0].Message.Content)
	if rawText == "" {
		return s.generateDeterministicReview(match, targetPlayer, isWin, targetHeroName, radiantHeroes, direHeroes, "规则降级: 模型返回空"), nil
	}

	modelTag := "[Cloud] " + targetModel
	if isNpu {
		modelTag = "[NPU/Local] " + targetModel
	}

	return s.parseReviewSections(rawText, modelTag), nil
}

// parseReviewSections 解析 4 维度结构化复盘内容
func (s *Service) parseReviewSections(rawText string, modelUsed string) *MatchReviewResult {
	var overview, keyTurningPoints, itemizationAnalysis string
	var nextActions []string

	p1 := strings.Index(rawText, "### 1.")
	if p1 == -1 {
		p1 = strings.Index(rawText, "1.")
	}
	p2 := strings.Index(rawText, "### 2.")
	if p2 == -1 {
		p2 = strings.Index(rawText, "2.")
	}
	p3 := strings.Index(rawText, "### 3.")
	if p3 == -1 {
		p3 = strings.Index(rawText, "3.")
	}
	p4 := strings.Index(rawText, "### 4.")
	if p4 == -1 {
		p4 = strings.Index(rawText, "4.")
	}

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

	return &MatchReviewResult{
		Overview:            overview,
		KeyTurningPoints:    keyTurningPoints,
		ItemizationAnalysis: itemizationAnalysis,
		NextMatchActions:    nextActions,
		FullText:            rawText,
		ModelUsed:           modelUsed,
	}
}

// generateDeterministicReview 确定性离线复盘保底生成器
func (s *Service) generateDeterministicReview(
	match map[string]interface{},
	target map[string]interface{},
	isWin bool,
	targetHeroName string,
	radiantHeroes []string,
	direHeroes []string,
	modelUsed string,
) *MatchReviewResult {
	kills, _ := target["kills"].(float64)
	deaths, _ := target["deaths"].(float64)
	assists, _ := target["assists"].(float64)
	damage, _ := target["hero_damage"].(float64)
	gpm, _ := target["gold_per_min"].(float64)

	var overview string
	if isWin {
		overview = fmt.Sprintf("本场比赛最终取得胜利。天辉阵容 (%s) 与夜魇阵容 (%s) 在多轮阵地战中交锋，己方在团战执行与兵线推进上建立了稳定的经济雪球，并在关键控盾与上高节奏中果断终结比赛。",
			strings.Join(radiantHeroes, "、"), strings.Join(direHeroes, "、"))
	} else {
		overview = fmt.Sprintf("本场比赛遗憾告负。双方对抗中，己方在面对敌方多点开花与强先手机制时容错率偏低，中后期关键阵亡导致防御塔与野区视野失守，终被敌方抓住买活时间差破路翻盘。")
	}

	var keyTurningPoints string
	if deaths <= 3 {
		keyTurningPoints = fmt.Sprintf("主操纵英雄【%s】交出了 %.0f/%.0f/%.0f 的稳健数据，场均输出 %.0f。对线期与中期抓人效率突出，但在 20~30 分钟的关键推塔波次中，队伍整体站位稍显脱节，后续应加强抱团开雾第一时间的集火统一性。",
			targetHeroName, kills, deaths, assists, damage)
	} else {
		keyTurningPoints = fmt.Sprintf("主操纵英雄【%s】个人数据为 %.0f/%.0f/%.0f，阵亡次数偏高。实战中多次在缺乏保护视野的情况下收线过深，被敌方游走抓单；团战入场过早承担了过多第一波不明伤害，导致核心输出未能完整倾泻。",
			targetHeroName, kills, deaths, assists)
	}

	var itemizationAnalysis string
	if gpm >= 550 {
		itemizationAnalysis = fmt.Sprintf("经济刷取效率维持在较高水准 (GPM %.0f)。出装整体兼顾了刷钱与打架节奏，但面对敌方控制链时，应评估更早做出闪烁匕首或黑皇杖 (BKB) 的必要性，以建立更强的先手容错率。", gpm)
	} else {
		itemizationAnalysis = fmt.Sprintf("经济发育速度相对受限 (GPM %.0f)。散件过度投资在一定程度上延缓了质变核心大件的成型。建议优化打钱路线，减少无效支援跑路，直奔关键保命装与第一波输出质变装。", gpm)
	}

	nextMatchActions := []string{
		"对线期强化正反补兵线控制，5 分钟前优先确保消耗品充足与控符视野。",
		"中后期开雾推进前，务必由酱油先手探草排眼，核心切忌走在第一身位吃控。",
		"核心大件成型第一波果断发起集结进攻，推塔后立即扫荡敌方三角区野怪扩大经济差。",
	}

	fullText := fmt.Sprintf("### 1. 【大盘与胜负定局复盘】\n%s\n\n### 2. 【主视角实战得失剖析】\n%s\n\n### 3. 【装备针对性与克制推演】\n%s\n\n### 4. 【下局致胜核心提升指南】\n1. %s\n2. %s\n3. %s",
		overview, keyTurningPoints, itemizationAnalysis, nextMatchActions[0], nextMatchActions[1], nextMatchActions[2])

	return &MatchReviewResult{
		Overview:            overview,
		KeyTurningPoints:    keyTurningPoints,
		ItemizationAnalysis: itemizationAnalysis,
		NextMatchActions:    nextMatchActions,
		FullText:            fullText,
		ModelUsed:           modelUsed,
	}
}

// TestEndpoint 测试目标端点网络连通性与模型响应
func (s *Service) TestEndpoint(mode string, endpoint string, apiKey string, model string) (*AiEndpointTestResult, error) {
	testedAt := time.Now().Format("15:04:05")

	if mode == "rule_only" {
		return &AiEndpointTestResult{
			Success:         true,
			LatencyMs:       0,
			StatusCode:      200,
			ModelUsed:       "确定性规则引擎 (离线纯规则模式)",
			ResponsePreview: "纯确定性规则引擎就绪：0 延迟、0 费用、0 崩溃风险，依靠静态规则秒级裁定。",
			TestedAt:        testedAt,
		}, nil
	}

	isLocal := mode == "local_npu" || mode == "npu"
	cleanEndpoint := strings.TrimRight(endpoint, "/")
	if cleanEndpoint == "" {
		if isLocal {
			cleanEndpoint = "http://127.0.0.1:11434/v1"
		} else {
			cleanEndpoint = "https://api.deepseek.com/v1"
		}
	}

	testModel := strings.TrimSpace(model)
	if testModel == "" {
		if isLocal {
			testModel = "qwen3.5:1.5b"
		} else {
			testModel = "deepseek-chat"
		}
	}

	reqBody := OpenAiChatRequest{
		Model: testModel,
		Messages: []OpenAiChatMessage{
			{Role: "user", Content: "Ping: 请回复一句简短的“Dota 2 教练在线”以验证连通性。"},
		},
		Temperature: 0.1,
		MaxTokens:   30,
	}

	jsonBytes, err := json.Marshal(reqBody)
	if err != nil {
		return &AiEndpointTestResult{
			Success:      false,
			ModelUsed:    testModel,
			ErrorMessage: fmt.Sprintf("编码请求数据失败: %v", err),
			TestedAt:     testedAt,
		}, nil
	}

	start := time.Now()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, cleanEndpoint+"/chat/completions", bytes.NewReader(jsonBytes))
	if err != nil {
		return &AiEndpointTestResult{
			Success:      false,
			ModelUsed:    testModel,
			ErrorMessage: fmt.Sprintf("创建请求失败: %v", err),
			TestedAt:     testedAt,
		}, nil
	}

	httpReq.Header.Set("Content-Type", "application/json")
	httpReq.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")
	if !isLocal && strings.TrimSpace(apiKey) != "" {
		httpReq.Header.Set("Authorization", "Bearer "+strings.TrimSpace(apiKey))
	}

	resp, err := s.httpClient.Do(httpReq)
	latencyMs := time.Since(start).Milliseconds()

	if err != nil {
		return &AiEndpointTestResult{
			Success:      false,
			LatencyMs:    latencyMs,
			ModelUsed:    testModel,
			ErrorMessage: fmt.Sprintf("连接超时或服务未启动: %v", err),
			TestedAt:     testedAt,
		}, nil
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		errBytes, _ := io.ReadAll(resp.Body)
		return &AiEndpointTestResult{
			Success:      false,
			LatencyMs:    latencyMs,
			StatusCode:   resp.StatusCode,
			ModelUsed:    testModel,
			ErrorMessage: fmt.Sprintf("HTTP %d: %s", resp.StatusCode, string(errBytes)),
			TestedAt:     testedAt,
		}, nil
	}

	var chatResp OpenAiChatResponse
	if err := json.NewDecoder(resp.Body).Decode(&chatResp); err != nil || len(chatResp.Choices) == 0 {
		return &AiEndpointTestResult{
			Success:      false,
			LatencyMs:    latencyMs,
			StatusCode:   resp.StatusCode,
			ModelUsed:    testModel,
			ErrorMessage: "解析模型回复格式失败",
			TestedAt:     testedAt,
		}, nil
	}

	return &AiEndpointTestResult{
		Success:         true,
		LatencyMs:       latencyMs,
		StatusCode:      resp.StatusCode,
		ModelUsed:       testModel,
		ResponsePreview: strings.TrimSpace(chatResp.Choices[0].Message.Content),
		TestedAt:        testedAt,
	}, nil
}

// FetchCloudModels 原生拉取云端可用模型列表 (避开浏览器 CORS 跨域)
func (s *Service) FetchCloudModels(baseUrl string, apiKey string) ([]string, error) {
	cleanBase := strings.TrimRight(baseUrl, "/")
	if cleanBase == "" {
		cleanBase = "https://api.deepseek.com/v1"
	}

	ctx, cancel := context.WithTimeout(context.Background(), 6*time.Second)
	defer cancel()

	httpReq, err := http.NewRequestWithContext(ctx, http.MethodGet, cleanBase+"/models", nil)
	if err != nil {
		return nil, err
	}
	httpReq.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")
	if strings.TrimSpace(apiKey) != "" {
		httpReq.Header.Set("Authorization", "Bearer "+strings.TrimSpace(apiKey))
	}

	resp, err := s.httpClient.Do(httpReq)
	if err != nil {
		return nil, fmt.Errorf("请求云端模型列表失败: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("云端返回异常状态 HTTP %d", resp.StatusCode)
	}

	var result struct {
		Data []struct {
			ID string `json:"id"`
		} `json:"data"`
	}

	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, fmt.Errorf("解析模型数据失败: %w", err)
	}

	var models []string
	for _, m := range result.Data {
		if strings.TrimSpace(m.ID) != "" {
			models = append(models, strings.TrimSpace(m.ID))
		}
	}
	return models, nil
}

// FetchLocalModels 原生探测本地 Ollama 已安装模型
func (s *Service) FetchLocalModels(endpoint string) ([]string, error) {
	cleanEndpoint := strings.TrimRight(endpoint, "/")
	if cleanEndpoint == "" {
		cleanEndpoint = "http://127.0.0.1:11434"
	}
	cleanEndpoint = strings.TrimSuffix(cleanEndpoint, "/v1")

	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()

	httpReq, err := http.NewRequestWithContext(ctx, http.MethodGet, cleanEndpoint+"/api/tags", nil)
	if err != nil {
		return nil, err
	}
	httpReq.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")

	resp, err := s.httpClient.Do(httpReq)
	if err != nil {
		return nil, fmt.Errorf("连接本地 Ollama 服务失败: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("本地服务返回异常 HTTP %d", resp.StatusCode)
	}

	var result struct {
		Models []struct {
			Name string `json:"name"`
		} `json:"models"`
	}

	if err := json.NewDecoder(resp.Body).Decode(&result); err != nil {
		return nil, fmt.Errorf("解析本地模型标签失败: %w", err)
	}

	var models []string
	for _, m := range result.Models {
		if strings.TrimSpace(m.Name) != "" {
			models = append(models, strings.TrimSpace(m.Name))
		}
	}
	return models, nil
}
