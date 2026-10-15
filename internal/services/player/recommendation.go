package player

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"

	"aegis-coach/internal/dota"
	"aegis-coach/internal/models"
)

// 常用物品数字 ID 至规范英文 Key 映射表
var dotaItemIdToKey = map[int]string{
	1:    "blink",
	2:    "blades_of_attack",
	3:    "broadsword",
	4:    "chainmail",
	5:    "claymore",
	6:    "helm_of_iron_will",
	7:    "javelin",
	8:    "mithril_hammer",
	9:    "platemail",
	10:   "quarterstaff",
	11:   "quelling_blade",
	12:   "ring_of_protection",
	13:   "gauntlets",
	14:   "slippers",
	15:   "mantle",
	16:   "branches",
	17:   "belt_of_strength",
	18:   "boots_of_elves",
	19:   "robe",
	20:   "circlet",
	21:   "ogre_axe",
	22:   "blade_of_alacrity",
	23:   "staff_of_wizardry",
	24:   "ultimate_orb",
	25:   "gloves",
	26:   "lifesteal",
	27:   "ring_of_regen",
	29:   "boots",
	34:   "magic_stick",
	36:   "magic_wand",
	38:   "clarity",
	41:   "bottle",
	42:   "ward_observer",
	43:   "ward_sentry",
	44:   "tango",
	46:   "tpscroll",
	48:   "travel_boots",
	50:   "phase_boots",
	56:   "ring_of_health",
	57:   "void_stone",
	63:   "power_treads",
	65:   "hand_of_midas",
	67:   "oblivion_staff",
	69:   "pers",
	73:   "bracer",
	75:   "wraith_band",
	77:   "null_talisman",
	79:   "mekansm",
	81:   "vladmir",
	86:   "buckler",
	88:   "ring_of_basilius",
	90:   "pipe",
	92:   "urn_of_shadows",
	94:   "headdress",
	96:   "sheepstick",
	98:   "orchid",
	100:  "cyclone",
	102:  "force_staff",
	104:  "dagon",
	106:  "necronomicon",
	108:  "ultimate_scepter",
	110:  "refresher",
	112:  "assault",
	114:  "heart",
	116:  "black_king_bar",
	117:  "aegis",
	119:  "shivas_guard",
	121:  "bloodstone",
	123:  "sphere",
	125:  "vanguard",
	127:  "blade_mail",
	129:  "soul_booster",
	131:  "hood_of_defiance",
	133:  "rapier",
	135:  "monkey_king_bar",
	137:  "radiance",
	139:  "butterfly",
	141:  "greater_crit",
	143:  "basher",
	145:  "bfury",
	147:  "manta",
	149:  "lesser_crit",
	151:  "armlet",
	152:  "invis_sword",
	154:  "satanic",
	156:  "mjollnir",
	158:  "skadi",
	160:  "sange",
	162:  "helm_of_the_dominator",
	164:  "maelstrom",
	166:  "desolator",
	168:  "yasha",
	170:  "mask_of_madness",
	172:  "diffusal_blade",
	174:  "ethereal_blade",
	176:  "soul_ring",
	178:  "arcane_boots",
	180:  "octarine_core",
	181:  "orb_of_venom",
	185:  "ancient_janggo",
	188:  "medallion_of_courage",
	190:  "veil_of_discord",
	196:  "diffusal_blade_2",
	206:  "rod_of_atos",
	208:  "abyssal_blade",
	210:  "heavens_halberd",
	212:  "ring_of_aquila",
	214:  "tranquil_boots",
	216:  "shadow_amulet",
	218:  "glimmer_cape",
	220:  "aeon_disk",
	223:  "meteor_hammer",
	225:  "nullifier",
	226:  "lotus_orb",
	229:  "solar_crest",
	231:  "guardian_greaves",
	232:  "aether_lens",
	235:  "dragon_lance",
	236:  "hurricane_pike",
	247:  "moon_shard",
	249:  "silver_edge",
	250:  "bloodthorn",
	252:  "echo_sabre",
	254:  "glimmer_cape",
	263:  "kaya",
	267:  "spirit_vessel",
	655:  "gleipnir",
	1021: "disperser",
	1022: "harpoon",
	1023: "phylactery",
	1024: "khanda",
	1028: "parasma",
}

// 常见物品数字 ID 至标准中文译名
var dotaItemIdToChinese = map[int]string{
	1:    "闪烁匕首 (跳刀)",
	11:   "压制之刃 (补刀斧)",
	16:   "铁树枝干",
	29:   "速度之靴 (草鞋)",
	36:   "魔杖 (大魔棒)",
	44:   "树之祭祀 (吃树)",
	48:   "远行鞋 (飞鞋)",
	50:   "相位鞋",
	63:   "动力鞋 (假腿)",
	73:   "护腕",
	75:   "怨灵系带",
	77:   "空灵挂件",
	96:   "邪恶镰刀 (羊刀)",
	98:   "紫怨",
	100:  "神圣法杖 / 吹风杖 (风杖)",
	102:  "原力法杖 (推推棒)",
	108:  "阿哈利姆神杖 (A杖)",
	114:  "恐鳌之心 (龙心)",
	116:  "黑皇杖 (BKB)",
	119:  "希瓦的守护",
	123:  "林肯法球",
	127:  "刃甲",
	135:  "金箍棒 (MKB)",
	137:  "辉耀",
	139:  "蝴蝶",
	141:  "代达罗斯之殇 (大炮)",
	143:  "碎颅锤 (小晕锤)",
	145:  "狂战斧",
	147:  "幻影斧 (分身斧)",
	154:  "撒旦之邪力",
	156:  "雷神之锤 (大雷锤)",
	158:  "斯嘉蒂之眼 (冰眼)",
	164:  "漩涡 (小电锤)",
	166:  "黯灭",
	170:  "疯狂面具",
	172:  "净魂之刃 (散失)",
	208:  "深渊之刃 (大晕锤)",
	210:  "天堂之戟",
	220:  "永恒之盘 (盘子)",
	225:  "否决挂饰",
	226:  "莲花宝珠",
	249:  "白银之锋 (大隐刀)",
	250:  "血棘",
	252:  "回音战刃 (连击刀)",
	267:  "魂之灵龛 (大灵龛)",
	655:  "缚灵索",
	1021: "散灵刀",
	1022: "鱼叉",
	1024: "坎达",
	1028: "帕拉席玛",
}

func getItemKeyById(id int) string {
	if k, ok := dotaItemIdToKey[id]; ok {
		return k
	}
	return fmt.Sprintf("item_%d", id)
}

func getItemChineseNameById(id int) string {
	if n, ok := dotaItemIdToChinese[id]; ok {
		return n
	}
	return getItemKeyById(id)
}

// OpenDota 接口响应模型
type openDotaItemPopularityResp struct {
	StartGameItems map[string]int `json:"start_game_items"`
	EarlyGameItems map[string]int `json:"early_game_items"`
	MidGameItems   map[string]int `json:"mid_game_items"`
	LateGameItems  map[string]int `json:"late_game_items"`
}

type openDotaMatchupItem struct {
	HeroID      int `json:"hero_id"`
	GamesPlayed int `json:"games_played"`
	Wins        int `json:"wins"`
}

// GetHeroMatchupRecommendations 获取基于本局英雄与敌方阵容的大数据针对性出装建议
func (s *PlayerService) GetHeroMatchupRecommendations(heroId int, enemyHeroIds []int) (*models.HeroMatchupRecommendation, error) {
	if heroId <= 0 {
		return nil, fmt.Errorf("英雄 ID 必须大于 0")
	}

	enemyKey := makeEnemyKey(enemyHeroIds)
	cacheKey := fmt.Sprintf("hero_rec_%d_%s", heroId, enemyKey)

	val, err := s.sfGroup.Do(cacheKey, func() (any, error) {
		// 1. 尝试从本地持久化磁盘缓存加载 (TTL 7天)
		cacheDir := filepath.Join(s.storageManager.GetCacheDirectory(), "opendota", "recommendations")
		_ = os.MkdirAll(cacheDir, 0755)
		cacheFile := filepath.Join(cacheDir, fmt.Sprintf("rec_%d_%s.json", heroId, enemyKey))

		if info, statErr := os.Stat(cacheFile); statErr == nil && time.Since(info.ModTime()) < 7*24*time.Hour {
			if data, readErr := os.ReadFile(cacheFile); readErr == nil {
				var cached models.HeroMatchupRecommendation
				if json.Unmarshal(data, &cached) == nil {
					cached.Source = "cache"
					return &cached, nil
				}
			}
		}

		// 2. 在线拉取 OpenDota itemPopularity 与 matchups
		popData, popErr := s.fetchItemPopularity(heroId)
		matchupList, matchupErr := s.fetchMatchups(heroId)

		rec := &models.HeroMatchupRecommendation{
			HeroID:       heroId,
			HeroName:     dota.GetHeroChineseName(heroId),
			ThreatHeroes: make([]models.ThreatHeroSummary, 0),
			StartItems:   make([]models.RecommendedItem, 0),
			EarlyItems:   make([]models.RecommendedItem, 0),
			MidItems:     make([]models.RecommendedItem, 0),
			LateItems:    make([]models.RecommendedItem, 0),
			CounterItems: make([]models.RecommendedItem, 0),
			Source:       "opendota",
			UpdatedAt:    time.Now().Format("2006-01-02 15:04:05"),
		}

		// 若在线拉取失败，尝试降级读取任何历史真实缓存；若无任何缓存则直接报错，严禁伪造任何离线假出装
		if popErr != nil && matchupErr != nil {
			if data, readErr := os.ReadFile(cacheFile); readErr == nil {
				var cached models.HeroMatchupRecommendation
				if json.Unmarshal(data, &cached) == nil {
					cached.Source = "cache"
					return &cached, nil
				}
			}
			return nil, fmt.Errorf("无法连接 OpenDota 大数据接口且无历史真实缓存: %v", popErr)
		}

		// 3. 处理对抗克制关系 (Matchups)
		if len(matchupList) > 0 {
			rec.ThreatHeroes = s.calculateThreatHeroes(heroId, enemyHeroIds, matchupList)
		}

		// 4. 处理各阶段装备热度 (Popularity)
		if popData != nil {
			rec.StartItems = parsePopularityStage(popData.StartGameItems, "start", 4)
			rec.EarlyItems = parsePopularityStage(popData.EarlyGameItems, "early", 4)
			rec.MidItems = parsePopularityStage(popData.MidGameItems, "mid", 4)
			rec.LateItems = parsePopularityStage(popData.LateGameItems, "late", 4)
		}

		// 5. 动态计算本局针对性出装加权 (Counter-Item Matrix)
		rec.CounterItems = s.generateCounterItems(heroId, enemyHeroIds, rec.ThreatHeroes)

		// 6. 成功落盘
		if recJson, mErr := json.MarshalIndent(rec, "", "  "); mErr == nil {
			_ = os.WriteFile(cacheFile, recJson, 0644)
		}

		return rec, nil
	})

	if err != nil {
		return nil, err
	}
	return val.(*models.HeroMatchupRecommendation), nil
}

// 辅助方法：在线拉取 itemPopularity
func (s *PlayerService) fetchItemPopularity(heroId int) (*openDotaItemPopularityResp, error) {
	apiUrl := fmt.Sprintf("https://api.opendota.com/api/heroes/%d/itemPopularity", heroId)
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, apiUrl, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")

	resp, err := s.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("itemPopularity 接口响应异常: HTTP %d", resp.StatusCode)
	}

	body, err := io.ReadAll(io.LimitReader(resp.Body, maxPlayerApiBytes))
	if err != nil {
		return nil, err
	}

	var data openDotaItemPopularityResp
	if err := json.Unmarshal(body, &data); err != nil {
		return nil, err
	}
	return &data, nil
}

// 辅助方法：在线拉取 matchups
func (s *PlayerService) fetchMatchups(heroId int) ([]openDotaMatchupItem, error) {
	apiUrl := fmt.Sprintf("https://api.opendota.com/api/heroes/%d/matchups", heroId)
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()

	req, err := http.NewRequestWithContext(ctx, http.MethodGet, apiUrl, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")

	resp, err := s.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("matchups 接口响应异常: HTTP %d", resp.StatusCode)
	}

	body, err := io.ReadAll(io.LimitReader(resp.Body, maxPlayerApiBytes))
	if err != nil {
		return nil, err
	}

	var list []openDotaMatchupItem
	if err := json.Unmarshal(body, &list); err != nil {
		return nil, err
	}
	return list, nil
}

// 辅助方法：计算威胁英雄
func (s *PlayerService) calculateThreatHeroes(myHeroId int, enemyHeroIds []int, matchups []openDotaMatchupItem) []models.ThreatHeroSummary {
	matchupMap := make(map[int]openDotaMatchupItem, len(matchups))
	for _, m := range matchups {
		matchupMap[m.HeroID] = m
	}

	results := make([]models.ThreatHeroSummary, 0)

	// 若提供了具体敌方 5 名英雄，则严格针对这 5 名英雄筛选
	targetIds := enemyHeroIds
	if len(targetIds) == 0 {
		// 未选定时，找出全局最克制我的 TOP 3 英雄
		type sortableMatchup struct {
			heroId  int
			winRate float64
			games   int
		}
		var list []sortableMatchup
		for _, m := range matchups {
			if m.GamesPlayed >= 30 {
				wr := float64(m.Wins) / float64(m.GamesPlayed) * 100.0
				list = append(list, sortableMatchup{heroId: m.HeroID, winRate: wr, games: m.GamesPlayed})
			}
		}
		sort.Slice(list, func(i, j int) bool {
			return list[i].winRate < list[j].winRate
		})
		for i := 0; i < len(list) && i < 3; i++ {
			targetIds = append(targetIds, list[i].heroId)
		}
	}

	for _, eId := range targetIds {
		if eId <= 0 || eId == myHeroId {
			continue
		}
		name := dota.GetHeroChineseName(eId)
		m, ok := matchupMap[eId]
		winRate := 50.0
		games := 0
		if ok && m.GamesPlayed > 0 {
			games = m.GamesPlayed
			winRate = float64(m.Wins) / float64(m.GamesPlayed) * 100.0
		}

		level := "normal"
		if winRate < 45.0 {
			level = "extreme"
		} else if winRate < 48.5 {
			level = "high"
		}

		desc := getHeroThreatFeatureDesc(eId)

		results = append(results, models.ThreatHeroSummary{
			HeroID:      eId,
			HeroName:    name,
			GamesPlayed: games,
			WinRate:     float64(int(winRate*10)) / 10.0,
			ThreatLevel: level,
			ThreatDesc:  desc,
		})
	}

	sort.Slice(results, func(i, j int) bool {
		return results[i].WinRate < results[j].WinRate
	})

	return results
}

// 辅助方法：生成对抗质变出装
func (s *PlayerService) generateCounterItems(myHeroId int, enemyHeroIds []int, threats []models.ThreatHeroSummary) []models.RecommendedItem {
	counterMap := make(map[int]models.RecommendedItem)

	hasStunOrBurst := false
	hasEvasionOrPassive := false
	hasRegenOrTank := false
	hasHighMobility := false

	// 分析敌方阵容或高危威胁英雄特征
	targetSet := make(map[int]bool)
	for _, id := range enemyHeroIds {
		if id > 0 && id != myHeroId {
			targetSet[id] = true
		}
	}
	for _, t := range threats {
		if t.HeroID > 0 && t.HeroID != myHeroId {
			targetSet[t.HeroID] = true
		}
	}

	for eId := range targetSet {
		if isBurstOrStunHero(eId) {
			hasStunOrBurst = true
		}
		if isEvasionOrPassiveHero(eId) {
			hasEvasionOrPassive = true
		}
		if isRegenOrTankHero(eId) {
			hasRegenOrTank = true
		}
		if isMobilityHero(eId) {
			hasHighMobility = true
		}
	}

	// 1. 强控 / 法爆 -> 黑皇杖 (116)
	if hasStunOrBurst {
		counterMap[116] = models.RecommendedItem{
			ItemID:     116,
			ItemKey:    "black_king_bar",
			ItemName:   getItemChineseNameById(116),
			Category:   "counter",
			Popularity: 98,
			Reason:     "敌方存在强力控制/高法术爆发，中期核心团战防秒必出",
		}
	}

	// 2. 闪避 / 强被动 -> 金箍棒 (135) 或 白银之锋 (249)
	if hasEvasionOrPassive {
		counterMap[135] = models.RecommendedItem{
			ItemID:     135,
			ItemKey:    "monkey_king_bar",
			ItemName:   getItemChineseNameById(135),
			Category:   "counter",
			Popularity: 92,
			Reason:     "敌方存在幻影刺客/风行者等物理闪避，克制 MISS 必出克制装",
		}
		counterMap[249] = models.RecommendedItem{
			ItemID:     249,
			ItemKey:    "silver_edge",
			ItemName:   getItemChineseNameById(249),
			Category:   "counter",
			Popularity: 85,
			Reason:     "破除敌方强力被动技能 (如钢背兽/幽鬼/小狗)，隐身偷人质变",
		}
	}

	// 3. 高回复 / 肉盾 -> 斯嘉蒂之眼 (158/160) 或 否决 (225)
	if hasRegenOrTank {
		counterMap[158] = models.RecommendedItem{
			ItemID:     158,
			ItemKey:    "skadi",
			ItemName:   getItemChineseNameById(158),
			Category:   "counter",
			Popularity: 88,
			Reason:     "大幅削弱敌方生命恢复与吸血效果，提供高额三维与减速缠绕",
		}
		counterMap[225] = models.RecommendedItem{
			ItemID:     225,
			ItemKey:    "nullifier",
			ItemName:   getItemChineseNameById(225),
			Category:   "counter",
			Popularity: 82,
			Reason:     "持续驱散敌方盘子/绿杖/吹风/微光等保命救人道具",
		}
	}

	// 4. 高机动位移 / 逃生 -> 紫怨/血棘 (250) 或 缚灵索 (655)
	if hasHighMobility {
		counterMap[250] = models.RecommendedItem{
			ItemID:     250,
			ItemKey:    "bloodthorn",
			ItemName:   getItemChineseNameById(250),
			Category:   "counter",
			Popularity: 86,
			Reason:     "针对蓝猫/帕克/敌法师等高机动英雄，提供瞬间沉默禁魔与必中暴击",
		}
	}

	results := make([]models.RecommendedItem, 0, len(counterMap))
	for _, it := range counterMap {
		results = append(results, it)
	}
	sort.Slice(results, func(i, j int) bool {
		return results[i].Popularity > results[j].Popularity
	})
	return results
}

// 辅助排序并格式化阶段物品
func parsePopularityStage(raw map[string]int, category string, limit int) []models.RecommendedItem {
	type itemFreq struct {
		id    int
		count int
	}
	var list []itemFreq
	for k, v := range raw {
		if id, err := strconv.Atoi(k); err == nil && v > 0 {
			// 过滤掉卷轴与眼位消耗品
			if id == 42 || id == 43 || id == 46 {
				continue
			}
			list = append(list, itemFreq{id: id, count: v})
		}
	}
	sort.Slice(list, func(i, j int) bool {
		return list[i].count > list[j].count
	})

	results := make([]models.RecommendedItem, 0, limit)
	for i := 0; i < len(list) && i < limit; i++ {
		id := list[i].id
		results = append(results, models.RecommendedItem{
			ItemID:     id,
			ItemKey:    getItemKeyById(id),
			ItemName:   getItemChineseNameById(id),
			Category:   category,
			Popularity: list[i].count,
			Reason:     fmt.Sprintf("大数据同英雄出装热度指数: %d", list[i].count),
		})
	}
	return results
}

func makeEnemyKey(ids []int) string {
	if len(ids) == 0 {
		return "none"
	}
	sort.Ints(ids)
	var sb strings.Builder
	for i, id := range ids {
		if i > 0 {
			sb.WriteString("_")
		}
		sb.WriteString(strconv.Itoa(id))
	}
	return sb.String()
}

func isBurstOrStunHero(id int) bool {
	// 蓝猫(17), 莱恩(26), 天怒(101), 莉娜(25), 宙斯(22), 军团(104), 拉比克(86), 萨满(27), 火女(25), 巫妖(31)
	bursts := map[int]bool{17: true, 22: true, 25: true, 26: true, 27: true, 31: true, 74: true, 86: true, 101: true, 104: true}
	return bursts[id]
}

func isEvasionOrPassiveHero(id int) bool {
	// 风行(21), PA(44), 钢背(99), 幽鬼(67), 熊战士(70), 小狗(54)
	evasions := map[int]bool{21: true, 44: true, 54: true, 67: true, 70: true, 99: true}
	return evasions[id]
}

func isRegenOrTankHero(id int) bool {
	// 屠夫(14), NEC(36), 炼金(73), 伐木机(98), 钢背(99), 哈斯卡(59)
	tanks := map[int]bool{14: true, 36: true, 59: true, 73: true, 98: true, 99: true}
	return tanks[id]
}

func isMobilityHero(id int) bool {
	// 敌法(1), 帕克(13), 蓝猫(17), 女王(39), 蚂蚁(63), 火猫(106), 虚无之灵(126)
	mobiles := map[int]bool{1: true, 13: true, 17: true, 39: true, 63: true, 106: true, 126: true}
	return mobiles[id]
}

func getHeroThreatFeatureDesc(id int) string {
	if isEvasionOrPassiveHero(id) {
		return "高物理闪避 / 强力被动核心"
	}
	if isBurstOrStunHero(id) {
		return "高法术爆发 / 强控制链"
	}
	if isRegenOrTankHero(id) {
		return "极高生命回复 / 肉盾抗伤"
	}
	if isMobilityHero(id) {
		return "高机动性位移 / 瞬间逃生"
	}
	return "常规对位战力"
}
