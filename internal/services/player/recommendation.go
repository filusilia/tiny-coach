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
	37:   "ghost",
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
	37:   "幽魂权杖 (绿杖)",
	44:   "树之祭祀 (吃树)",
	48:   "远行鞋 (飞鞋)",
	50:   "相位鞋",
	63:   "动力鞋 (假腿)",
	73:   "护腕",
	75:   "怨灵系带",
	77:   "空灵挂件",
	79:   "梅肯斯姆",
	90:   "洞察烟斗 (笛子)",
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
	206:  "阿托斯之棍",
	208:  "深渊之刃 (大晕锤)",
	210:  "天堂之戟",
	220:  "永恒之盘 (盘子)",
	225:  "否决挂饰",
	226:  "莲花宝珠",
	229:  "太阳纹章",
	231:  "卫士胫甲 (大鞋)",
	249:  "白银之锋 (大隐刀)",
	250:  "血棘",
	252:  "回音战刃 (连击刀)",
	254:  "微光披风",
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

	isSupport := isSupportHero(myHeroId)

	if isSupport {
		// ══════════════════════════════════════════════════════════════
		// 辅助分路针对装 (Support Counter Matrix)
		// 严禁推荐任何金箍棒、大隐刀、冰眼、否决、血棘等大哥输出装
		// ══════════════════════════════════════════════════════════════

		// 1. 强控 / 法爆 / 范围伤害 -> 微光披风 (254) / 洞察烟斗 (90) / 永恒之盘 (220)
		if hasStunOrBurst {
			counterMap[254] = models.RecommendedItem{
				ItemID:     254,
				ItemKey:    "glimmer_cape",
				ItemName:   getItemChineseNameById(254),
				Category:   "counter",
				Popularity: 98,
				Reason:     "敌方高法术爆发/强控制，提供隐身与300点魔法护盾，救人与防秒核心装",
				Role:       "support",
			}
			counterMap[90] = models.RecommendedItem{
				ItemID:     90,
				ItemKey:    "pipe",
				ItemName:   getItemChineseNameById(90),
				Category:   "counter",
				Popularity: 90,
				Reason:     "全队抵御法术 AOE 伤害护盾，团战辅助克制法系秒杀必备",
				Role:       "support",
			}
			counterMap[220] = models.RecommendedItem{
				ItemID:     220,
				ItemKey:    "aeon_disk",
				ItemName:   getItemChineseNameById(220),
				Category:   "counter",
				Popularity: 88,
				Reason:     "生命告急时强驱散并伤害免疫2.5秒，防军团决斗/虚空大/刺客突脸强秒",
				Role:       "support",
			}
		}

		// 2. 闪避 / 强被动 / 物理突脸核心 -> 天堂之戟 (210) / 幽魂权杖 (37) / 原力法杖 (102)
		if hasEvasionOrPassive {
			counterMap[210] = models.RecommendedItem{
				ItemID:     210,
				ItemKey:    "heavens_halberd",
				ItemName:   getItemChineseNameById(210),
				Category:   "counter",
				Popularity: 95,
				Reason:     "缴械敌方物理核心（如PA/拍拍/巨魔）使其无法普攻，无法被驱散保核神装",
				Role:       "support",
			}
			counterMap[37] = models.RecommendedItem{
				ItemID:     37,
				ItemKey:    "ghost",
				ItemName:   getItemChineseNameById(37),
				Category:   "counter",
				Popularity: 92,
				Reason:     "物理免疫4秒，辅助防敌方刺客物理平A突脸秒杀第一神器",
				Role:       "support",
			}
			counterMap[102] = models.RecommendedItem{
				ItemID:     102,
				ItemKey:    "force_staff",
				ItemName:   getItemChineseNameById(102),
				Category:   "counter",
				Popularity: 89,
				Reason:     "位移救被切队友或自保拉开距离，克制近战粘人与控制框",
				Role:       "support",
			}
		}

		// 3. 高回复 / 肉盾 -> 魂之灵龛 / 大骨灰 (267)
		if hasRegenOrTank {
			counterMap[267] = models.RecommendedItem{
				ItemID:     267,
				ItemKey:    "spirit_vessel",
				ItemName:   getItemChineseNameById(267),
				Category:   "counter",
				Popularity: 96,
				Reason:     "辅助克制高回复与肉核核心神器，百分比扣血并削减45%生命恢复与吸血",
				Role:       "support",
			}
		}

		// 4. 高机动位移 / 逃生 -> 阿托斯之棍 (206) / 吹风 (100)
		if hasHighMobility {
			counterMap[206] = models.RecommendedItem{
				ItemID:     206,
				ItemKey:    "rod_of_atos",
				ItemName:   getItemChineseNameById(206),
				Category:   "counter",
				Popularity: 90,
				Reason:     "超远距离2秒缠绕，锁死敌方蓝猫/帕克/敌法等位移，配合队友集火",
				Role:       "support",
			}
			counterMap[100] = models.RecommendedItem{
				ItemID:     100,
				ItemKey:    "cyclone",
				ItemName:   getItemChineseNameById(100),
				Category:   "counter",
				Popularity: 86,
				Reason:     "留人吹起打断，亦可自吹规避集火与驱散负面状态",
				Role:       "support",
			}
		}

		// 若未命中任何特定极端类型，至少给出通用保核救人装 (微光 254、推推 102)
		if len(counterMap) == 0 {
			counterMap[254] = models.RecommendedItem{
				ItemID:     254,
				ItemKey:    "glimmer_cape",
				ItemName:   getItemChineseNameById(254),
				Category:   "counter",
				Popularity: 85,
				Reason:     "对线与中期必备保人防法伤道具，隐身护盾兼备",
				Role:       "support",
			}
			counterMap[102] = models.RecommendedItem{
				ItemID:     102,
				ItemKey:    "force_staff",
				ItemName:   getItemChineseNameById(102),
				Category:   "counter",
				Popularity: 82,
				Reason:     "全期通用救队友或自保位移防近身",
				Role:       "support",
			}
		}
	} else {
		// ══════════════════════════════════════════════════════════════
		// 核心大哥分路针对装 (Core/Carry Counter Matrix)
		// ══════════════════════════════════════════════════════════════

		// 1. 强控 / 法爆 -> 黑皇杖 (116)
		if hasStunOrBurst {
			counterMap[116] = models.RecommendedItem{
				ItemID:     116,
				ItemKey:    "black_king_bar",
				ItemName:   getItemChineseNameById(116),
				Category:   "counter",
				Popularity: 98,
				Reason:     "敌方存在强力控制/高法术爆发，中期核心团战防秒必出",
				Role:       "core",
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
				Role:       "core",
			}
			counterMap[249] = models.RecommendedItem{
				ItemID:     249,
				ItemKey:    "silver_edge",
				ItemName:   getItemChineseNameById(249),
				Category:   "counter",
				Popularity: 85,
				Reason:     "破除敌方强力被动技能 (如钢背兽/幽鬼/小狗)，隐身偷人质变",
				Role:       "core",
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
				Role:       "core",
			}
			counterMap[225] = models.RecommendedItem{
				ItemID:     225,
				ItemKey:    "nullifier",
				ItemName:   getItemChineseNameById(225),
				Category:   "counter",
				Popularity: 82,
				Reason:     "持续驱散敌方盘子/绿杖/吹风/微光等保命救人道具",
				Role:       "core",
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
				Role:       "core",
			}
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

// supportHeroIds 包含官方角色定位中以辅助为主的英雄数字 ID 集合 (37+位典型辅助英雄)
var supportHeroIds = map[int]bool{
	3:   true, // 祸乱之源 Bane
	5:   true, // 水晶室女 Crystal Maiden
	7:   true, // 撼地者 Earthshaker
	16:  true, // 沙王 Sand King
	20:  true, // 复仇之魂 Vengeful Spirit
	26:  true, // 莱恩 Lion
	27:  true, // 暗影萨满 Shadow Shaman
	30:  true, // 巫医 Witch Doctor
	31:  true, // 巫妖 Lich
	37:  true, // 术士 Warlock
	40:  true, // 剧毒术士 Venomancer
	50:  true, // 戴泽 Dazzle
	57:  true, // 全能骑士 Omniknight
	58:  true, // 魅惑魔女 Enchantress
	64:  true, // 杰奇洛 Jakiro
	66:  true, // 陈 Chen
	68:  true, // 远古冰魄 Ancient Apparition
	79:  true, // 暗影恶魔 Shadow Demon
	83:  true, // 树精卫士 Treant Protector
	84:  true, // 食人魔魔法师 Ogre Magi
	85:  true, // 不朽尸王 Undying
	86:  true, // 拉比克 Rubick
	87:  true, // 干扰者 Disruptor
	90:  true, // 光之守卫 Keeper of the Light
	91:  true, // 艾欧 Io
	92:  true, // 维萨吉 Visage
	101: true, // 天怒法师 Skywrath Mage
	103: true, // 上古巨神 Elder Titan
	105: true, // 工程师 Techies
	110: true, // 凤凰 Phoenix
	111: true, // 神谕者 Oracle
	112: true, // 寒冬飞龙 Winter Wyvern
	119: true, // 邪影芳灵 Dark Willow
	121: true, // 天涯墨客 Grimstroke
	123: true, // 森海飞霞 Hoodwink
	128: true, // 电炎绝手 Snapfire
	131: true, // 百戏大王 Ringmaster
	155: true, // 朗戈 Largo
}

func isSupportHero(heroId int) bool {
	return supportHeroIds[heroId]
}
