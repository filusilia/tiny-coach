package ai

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

// AiService AI 战术推理与双引擎分发服务（Ollama 本地引擎与 Remote 远程 Response API 引擎）
type AiService struct {
	mu         sync.RWMutex
	ollama     *OllamaEngine
	remote     *RemoteEngine
	httpClient *http.Client
	longClient *http.Client // 针对本地模型拉取等长耗时任务，复用无全局超时 client
}

var (
	aiServiceInstance *AiService
	aiServiceOnce     sync.Once
)

func GetAiService() *AiService {
	aiServiceOnce.Do(func() {
		aiServiceInstance = &AiService{
			ollama:     NewOllamaEngine("http://127.0.0.1:11434/v1"),
			remote:     NewRemoteEngine("https://api.deepseek.com/v1", ""),
			httpClient: &http.Client{Timeout: 15 * time.Second},
			longClient: &http.Client{Timeout: 0},
		}
	})
	return aiServiceInstance
}

// getEngine 依据 mode 选择对应引擎，mode 是唯一权威判定依据
func (s *AiService) getEngine(mode string) (AiEngine, error) {
	switch strings.ToLower(strings.TrimSpace(mode)) {
	case "ollama", "local_npu", "npu", "local":
		return s.ollama, nil
	case "remote", "cloud", "response_api":
		return s.remote, nil
	case "rule_only":
		return nil, nil
	default:
		return nil, fmt.Errorf("不支持的 AI 模式: %q（有效值: ollama / remote / rule_only）", mode)
	}
}

// UpdateCredentials 设置页保存后同步更新常驻引擎凭据
func (s *AiService) UpdateCredentials(mode, endpoint, apiKey string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	eng, err := s.getEngine(mode)
	if err == nil && eng != nil {
		eng.SetCredentials(endpoint, apiKey)
	}
}

// resolveExecutionEngine 解析用于单次调用的引擎（优先使用传入的临时 endpoint/apiKey 构造轻量实例）
func (s *AiService) resolveExecutionEngine(mode, endpoint, apiKey string) (AiEngine, error) {
	cleanMode := strings.ToLower(strings.TrimSpace(mode))
	switch cleanMode {
	case "ollama", "local_npu", "npu", "local":
		ep := strings.TrimSpace(endpoint)
		if ep == "" {
			s.mu.RLock()
			ep = s.ollama.getEndpoint()
			s.mu.RUnlock()
		}
		return NewOllamaEngine(ep), nil
	case "remote", "cloud", "response_api":
		ep := strings.TrimSpace(endpoint)
		key := strings.TrimSpace(apiKey)
		if ep == "" || key == "" {
			s.mu.RLock()
			savedEp, savedKey := s.remote.getCredentials()
			s.mu.RUnlock()
			if ep == "" {
				ep = savedEp
			}
			if key == "" {
				key = savedKey
			}
		}
		return NewRemoteEngine(ep, key), nil
	case "rule_only":
		return nil, nil
	default:
		return nil, fmt.Errorf("不支持的 AI 模式: %q（有效值: ollama / remote / rule_only）", mode)
	}
}

// TestAiEndpoint 测试本地 Ollama 或云端 Remote 端点连通性
func (s *AiService) TestAiEndpoint(mode, endpoint, apiKey, model string) (*models.AiEndpointTestResultDTO, error) {
	cleanMode := strings.ToLower(strings.TrimSpace(mode))
	if cleanMode == "rule_only" {
		return &models.AiEndpointTestResultDTO{
			Success:   true,
			ModelUsed: "纯规则引擎 (无模型)",
			LatencyMs: 0,
		}, nil
	}

	engine, err := s.resolveExecutionEngine(mode, endpoint, apiKey)
	if err != nil {
		return &models.AiEndpointTestResultDTO{
			Success:      false,
			ErrorMessage: err.Error(),
		}, nil
	}

	cleanModel := strings.TrimSpace(model)
	if cleanModel == "" {
		if cleanMode == "ollama" || cleanMode == "local_npu" || cleanMode == "npu" {
			cleanModel = "qwen3.5:1.5b"
		} else {
			cleanModel = "deepseek-chat"
		}
	}

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	latency, reply, pingErr := engine.Ping(ctx, cleanModel)
	if pingErr != nil {
		return &models.AiEndpointTestResultDTO{
			Success:      false,
			LatencyMs:    latency,
			ErrorMessage: pingErr.Error(),
			ModelUsed:    cleanModel,
		}, nil
	}

	return &models.AiEndpointTestResultDTO{
		Success:     true,
		LatencyMs:   latency,
		RawResponse: reply,
		ModelUsed:   cleanModel,
	}, nil
}

// FetchCloudModels 获取远程端点的模型列表（仅供 remote 模式使用）
func (s *AiService) FetchCloudModels(baseUrl, apiKey string) ([]string, error) {
	cleanUrl := normalizeBaseUrl(baseUrl, "https://api.deepseek.com/v1")
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

// AskCoach 即时战术问答
func (s *AiService) AskCoach(
	question string,
	ctx *models.BattleContextDTO,
	mode, endpoint, apiKey, model string,
) (string, error) {
	cleanMode := strings.ToLower(strings.TrimSpace(mode))
	if cleanMode == "rule_only" {
		return "【纯规则引擎提示】当前处于离线免模型模式，请密切关注局内状态栏高光战术建议。", nil
	}

	engine, err := s.resolveExecutionEngine(mode, endpoint, apiKey)
	if err != nil {
		return "", err
	}

	prompt := s.buildCoachPrompt(question, ctx)
	reqCtx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()

	return engine.Complete(reqCtx, prompt, model, 800, 0.6)
}

type matchReviewTargetPlayer struct {
	Slot          int      `json:"slot"`
	Hero          string   `json:"hero"`
	Kda           string   `json:"kda"`
	Gpm           int      `json:"gpm"`
	Xpm           int      `json:"xpm"`
	HeroDamage    int      `json:"hero_damage"`
	TowerDamage   int      `json:"tower_damage"`
	LastHits      int      `json:"last_hits"`
	Denies        int      `json:"denies"`
	Items         []string `json:"items"`
	NeutralItem   string   `json:"neutral_item"`
	HasAghs       bool     `json:"has_aghs"`
	HasShard      bool     `json:"has_shard"`
	KpPercent     string   `json:"kp_percent"`
	DamagePercent string   `json:"damage_percent"`
}

type matchReviewPayload struct {
	MatchId         any                     `json:"match_id"`
	DurationMinutes int                     `json:"duration_minutes"`
	IsWin           bool                    `json:"is_win"`
	RadiantScore    int                     `json:"radiant_score"`
	DireScore       int                     `json:"dire_score"`
	RadiantHeroes   string                  `json:"radiant_heroes"`
	DireHeroes      string                  `json:"dire_heroes"`
	TargetPlayer    matchReviewTargetPlayer `json:"target_player"`
	EnemyCores      string                  `json:"enemy_cores"`
	UserFocus       string                  `json:"user_focus"`
}

// GenerateMatchReview 赛后 AI 深度复盘报告 (4 维度结构化)
func (s *AiService) GenerateMatchReview(
	matchId string,
	matchDataJson string,
	playerSlot int,
	mode, endpoint, apiKey, model string,
) (*models.MatchReviewResultDTO, error) {
	var payload matchReviewPayload
	_ = json.Unmarshal([]byte(matchDataJson), &payload)

	cleanMode := strings.ToLower(strings.TrimSpace(mode))
	if cleanMode == "rule_only" {
		return s.generateDeterministicReview(&payload, "确定性战术分析引擎 (纯规则模式)"), nil
	}

	engine, err := s.resolveExecutionEngine(mode, endpoint, apiKey)
	if err != nil {
		// 引擎无法初始化时降级至确定性规则分析，保证用户随时获得专业复盘
		return s.generateDeterministicReview(&payload, fmt.Sprintf("确定性战术分析降级: %v", err)), nil
	}

	heroName := payload.TargetPlayer.Hero
	if heroName == "" {
		heroName = fmt.Sprintf("玩家槽位 %d", playerSlot)
	}

	prompt := fmt.Sprintf(`你是一名顶级 Dota 2 国际邀请赛 (TI) 冠军主教练兼高阶战术数据分析师。
请基于以下客观对局快照数据（包含双方阵容阵营、主操纵英雄战绩、经济转化率、正反补、核心出装与敌方核心威胁），给出极度专业、详尽深入、富有实战细节与因果机制推演的赛后复盘报告。

请严格遵照以下【4个维度结构】输出纯文本内容，严禁泛泛空谈与敷衍套话，必须使用标准专业 Dota 2 战术术语，每部分须结合双方具体英雄名、关键技能互动机制与具体装备展开不少于 150 字的深入剖析：

### 1. 【大盘与胜负定局复盘】
深入剖析双方阵容强弱期交错曲线（0~10分钟对线期线权分布、15~25分钟控肉山盾推进节奏、30+分钟后期买活与上高博弈），明确指出决定本场胜负走势的 2 个决定性宏观节奏分水岭。

### 2. 【主视角实战得失剖析】
深度剖析主视角【%s】的操纵表现：结合其正反补发育速度、参战率 (%s%%)、伤害占比 (%s%%) 与 KDA (%s)，详细推演其在团战拉扯身位、入场第一目标选择、保命容错或带线节奏上的关键得失与伏笔。

### 3. 【装备针对性与克制推演】
结合主视角当前配装与敌方核心威胁（%s），深度推演装备路线的成型窗口与优劣，逐一指出针对敌方控制、爆发或被动恢复的克制装（如黑皇杖BKB、否决、闪烁匕首、强袭装甲、大隐刀、金箍棒等）的出装时机与反制得失。

### 4. 【下局致胜核心提升指南】
提炼 3~4 条极度精准、针针见血、具有明确操作执行指令的对线与团战肌肉记忆规范（每条以序号开头，独立成段）。

【实战快照数据】:
%s`, heroName, payload.TargetPlayer.KpPercent, payload.TargetPlayer.DamagePercent, payload.TargetPlayer.Kda, payload.EnemyCores, matchDataJson)

	reqCtx, cancel := context.WithTimeout(context.Background(), 45*time.Second)
	defer cancel()

	reply, err := engine.Complete(reqCtx, prompt, model, 2500, 0.4)
	if err != nil {
		// 模型生成超时或网络故障时自动降级
		return s.generateDeterministicReview(&payload, fmt.Sprintf("%s 降级: %v", model, err)), nil
	}

	result := s.parseReviewSections(reply, model)
	return result, nil
}

// generateDeterministicReview 提供高质量、多维度详尽的离线确定性专业复盘报告
func (s *AiService) generateDeterministicReview(p *matchReviewPayload, modelUsed string) *models.MatchReviewResultDTO {
	heroName := p.TargetPlayer.Hero
	if heroName == "" {
		heroName = "本方核心"
	}
	durationMin := p.DurationMinutes
	if durationMin <= 0 {
		durationMin = 30
	}

	// 1. 大盘分析
	var overviewSb strings.Builder
	if p.IsWin {
		overviewSb.WriteString(fmt.Sprintf("【胜利奠基复盘】本局对决历经 %d 分钟，天辉与夜魇终局比分锁定为 %d : %d。己方阵容在整体节奏推进与关键团战处理上占据主动。对线期（前 10 分钟）稳住核心经济走廊，中期（15~25 分钟）敏锐抓住控肉山盾与外塔推压的时间窗口，形成了持续向敌方野区施压的滚雪球优势。后期上高波次利用买活与防御塔阵地拉扯稳扎稳打，最终锁定胜局。",
			durationMin, p.RadiantScore, p.DireScore))
	} else {
		overviewSb.WriteString(fmt.Sprintf("【失利症结复盘】整场比赛历时 %d 分钟（比分 %d : %d）。双方阵容前中期对抗激烈，但在 20 分钟前后的关键控盾与二塔攻防节奏分水岭上，己方在缺乏深位防守视野的情况下贸然开战或落单被抓，丢失了野区主控权。后期因阵型脱节被对手逐个击破，上高防御波次买活时间差拉大，导致局面不可逆转。",
			durationMin, p.RadiantScore, p.DireScore))
	}
	if p.RadiantHeroes != "" && p.DireHeroes != "" {
		overviewSb.WriteString(" 从阵容博弈维度来看，对决双方在阵地控制与团战爆发上互有长短，决定胜负的核心往往在于第一波先手技能能否秒杀敌方关键拆火点，以及肉山巢穴附近的视野占领深度。")
	}

	// 2. 主视角得失剖析
	var pointsSb strings.Builder
	pointsSb.WriteString(fmt.Sprintf("主视角操控【%s】录得战绩 %s，GPM %d / XPM %d，输出全队占比 %s%%，团战参战率 %s%%。\n",
		heroName, p.TargetPlayer.Kda, p.TargetPlayer.Gpm, p.TargetPlayer.Xpm, p.TargetPlayer.DamagePercent, p.TargetPlayer.KpPercent))

	csPerMin := float64(p.TargetPlayer.LastHits) / float64(durationMin)
	if csPerMin >= 7.5 {
		pointsSb.WriteString(fmt.Sprintf("发育端表现优异：正反补达到 %d / %d (平均 %.1f 刀/分)，刷线与清野路线规划清晰，有效将野区资源快速转化为净资产领先。", p.TargetPlayer.LastHits, p.TargetPlayer.Denies, csPerMin))
	} else if csPerMin >= 4.5 {
		pointsSb.WriteString(fmt.Sprintf("发育端中规中矩：录得 %d 正补与 %d 反补 (%.1f 刀/分)，对线期补刀基本达标，但在中盘转线与防抓路线选择上略显迟疑，发育节奏数次被小规模交战打断。", p.TargetPlayer.LastHits, p.TargetPlayer.Denies, csPerMin))
	} else {
		pointsSb.WriteString(fmt.Sprintf("发育端存在明显提升空间：全场仅 %d 正补 (%.1f 刀/分)，对线期兵线重置与塔下补刀功底需强化，过早陷入被动抗压或无意义游走，推迟了第一个核心大件的成型期。", p.TargetPlayer.LastHits, csPerMin))
	}

	// 3. 装备与针对性克制
	var itemSb strings.Builder
	itemsStr := strings.Join(p.TargetPlayer.Items, "、")
	if itemsStr == "" {
		itemsStr = "基础散件"
	}
	itemSb.WriteString(fmt.Sprintf("终局六格配装为 [%s]", itemsStr))
	if p.TargetPlayer.NeutralItem != "" {
		itemSb.WriteString(fmt.Sprintf("，中立物品佩戴【%s】", p.TargetPlayer.NeutralItem))
	}
	itemSb.WriteString("。\n")

	hasBkb := false
	hasBlink := false
	for _, it := range p.TargetPlayer.Items {
		if strings.Contains(it, "黑皇杖") || strings.Contains(strings.ToLower(it), "bkb") {
			hasBkb = true
		}
		if strings.Contains(it, "闪烁匕首") || strings.Contains(it, "跳刀") {
			hasBlink = true
		}
	}

	if hasBkb {
		itemSb.WriteString("本局果断做出【黑皇杖 (BKB)】，在阵地战中确立了关键的免控输出环境，极大改善了生存容错。")
	} else {
		itemSb.WriteString("终局出装未包含【黑皇杖 (BKB)】。在面对敌方多点硬控与高额魔法爆发威胁时，缺乏强魔免的庇护极易在入场瞬间被连招强秒，限制了输出潜力。")
	}

	if hasBlink {
		itemSb.WriteString(" 配备【闪烁匕首】带来了极强的先手开团与切后排机动性。")
	}

	if p.TargetPlayer.HasAghs {
		itemSb.WriteString(" 已解锁【阿哈利姆神杖】，核心技能机制实现质变。")
	}
	if p.TargetPlayer.HasShard {
		itemSb.WriteString(" 准时出装【阿哈利姆魔晶】，补足了小技能的战略功能性。")
	}

	if p.EnemyCores != "" {
		itemSb.WriteString(fmt.Sprintf(" 对抗敌方核心（%s）时，后续对局可针对性补充如金箍棒（破闪避）、大隐刀（破被动）或否决（封禁绿杖吹风盘子）以达到绝对压制。", p.EnemyCores))
	}

	// 4. 下局提升建议
	actions := []string{
		fmt.Sprintf("强化视野防抓意识：带线至河道外或敌方二塔前必须确认敌方核心头像在小地图出现，若全图MISS严禁盲目单走收野。"),
		fmt.Sprintf("技能与切入时序优化：团战切忌第一波脸探草丛吃满第一轮AOE爆发，等待敌方关键硬控技能交出后，从侧翼或后排精准切入目标。"),
		fmt.Sprintf("推进与买活纪律：25 分钟后身上常备买活资金，团战获胜后第一时间转拆防御塔或控肉山，杜绝赢团后分散清野给对手喘息窗口。"),
	}

	return &models.MatchReviewResultDTO{
		Overview:            overviewSb.String(),
		KeyTurningPoints:    pointsSb.String(),
		ItemizationAnalysis: itemSb.String(),
		NextMatchActions:    actions,
		FullText: fmt.Sprintf("### 1. 【大盘与胜负定局复盘】\n%s\n\n### 2. 【主视角实战得失剖析】\n%s\n\n### 3. 【装备针对性与克制推演】\n%s\n\n### 4. 【下局致胜核心提升指南】\n1. %s\n2. %s\n3. %s",
			overviewSb.String(), pointsSb.String(), itemSb.String(), actions[0], actions[1], actions[2]),
		ModelUsed: modelUsed,
	}
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
			if len(l) > 3 && (strings.HasPrefix(l, "-") || strings.HasPrefix(l, "1.") || strings.HasPrefix(l, "2.") || strings.HasPrefix(l, "3.") || strings.HasPrefix(l, "4.")) {
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
	sb.WriteString("你是一位国际顶级 Dota 2 实时电竞战术教练。请基于当前即时战局，用 1~2 句精准、直接、可立即落地的战术指挥回答玩家。\n\n")
	sb.WriteString("【战术思考与决策原则 (必须严格遵循)】\n")
	sb.WriteString("1. 方案优先级金字塔 (Solution Hierarchy)：\n")
	sb.WriteString("   - 最高优先级：立即运用英雄当前已就绪的核心技能与背包现有装备；\n")
	sb.WriteString("   - 次高优先级：等待数秒内即将冷却完毕的关键技能或装备；\n")
	sb.WriteString("   - 禁止行为：在确认玩家当前已有技能与背包装备无法化解危机前，严禁空谈或盲目建议后续购买新装备！\n")
	sb.WriteString("2. 因果链推演：严谨遵循 Dota 2 真实机制因果链（如：清莲宝珠是反弹指向技能而非阻挡晕眩，刃甲是反弹全额伤害而非免伤）；\n")
	sb.WriteString("3. 极速时效：语言极度精炼电竞风，直接指挥走位、切入时机、逃生路线或输出目标。\n\n")

	if ctx != nil {
		sb.WriteString(fmt.Sprintf("【即时局势】\n英雄: %s (Lv.%d), 血量: %d%%, 蓝量: %d%%, 金钱: %d, 游戏时间: %s, KDA: %s\n",
			ctx.HeroName, ctx.HeroLevel, ctx.HealthPercent, ctx.ManaPercent, ctx.Gold, ctx.ClockTime, ctx.KDA))
		if len(ctx.Items) > 0 {
			sb.WriteString(fmt.Sprintf("当前背包装备: %s\n", strings.Join(ctx.Items, ", ")))
		}
		if len(ctx.Abilities) > 0 {
			sb.WriteString(fmt.Sprintf("当前英雄技能: %s\n", strings.Join(ctx.Abilities, ", ")))
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

// PullLocalModel 向本地 Ollama 服务发起模型下载拉取（仅供 ollama 模式使用）
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

	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Minute)
	defer cancel()

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, pullUrl, bytes.NewReader(payload))
	if err != nil {
		return "", fmt.Errorf("创建下载请求失败: %w", err)
	}
	req.Header.Set("Content-Type", "application/json")

	resp, reqErr := s.longClient.Do(req)
	if reqErr != nil {
		if errors.Is(reqErr, context.DeadlineExceeded) {
			return "", fmt.Errorf("拉取本地模型 [%s] 超时（超过 15 分钟），请检查网络连接或在终端手动执行: ollama pull %s", cleanName, cleanName)
		}
		return "", fmt.Errorf("无法连接本地 Ollama 服务 (端点: %s): %v。请确认已在终端执行 `ollama serve`，或手动执行: ollama run %s", cleanEndpoint, reqErr, cleanName)
	}
	defer resp.Body.Close()

	if resp.StatusCode == http.StatusOK {
		return "success", nil
	}

	errBody, _ := io.ReadAll(io.LimitReader(resp.Body, 1024))
	return "", fmt.Errorf("本地 Ollama 拉取模型返回 HTTP %d: %s", resp.StatusCode, strings.TrimSpace(string(errBody)))
}
