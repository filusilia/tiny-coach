package models

// OpenDotaMatchSummary OpenDota REST API 单场比赛简报实体 DTO
//
// 契约约束：本结构体是 OpenDota 的**反序列化输入**，其 JSON 标签必须与上游保持
// snake_case 一致。encoding/json 对标签大小写不敏感但**不会折叠下划线**，
// 一旦写成 camelCase 不会报错，只会把字段静默解码为零值——曾导致 PlayerSlot 恒为 0
// （每场都被判为天辉、胜率恒 0%）与 HeroID 恒为 0（英雄名退化为「英雄#0」）。
// 回归守卫见 internal/services/player/opendota_decode_test.go。
type OpenDotaMatchSummary struct {
	MatchID     int64 `json:"match_id"`
	PlayerSlot  int   `json:"player_slot"`
	RadiantWin  bool  `json:"radiant_win"`
	Duration    int   `json:"duration"`
	GameMode    int   `json:"game_mode"`
	LobbyType   int   `json:"lobby_type"`
	HeroID      int   `json:"hero_id"`
	StartTime   int64 `json:"start_time"`
	Version     int   `json:"version"`
	Kills       int   `json:"kills"`
	Deaths      int   `json:"deaths"`
	Assists     int   `json:"assists"`
	AverageRank int   `json:"average_rank"`
	PartySize   int   `json:"party_size"`
}

// HeroPerformance 单个英雄在统计周期内的技战术综合表现 DTO
type HeroPerformance struct {
	HeroID     int     `json:"heroId"`
	HeroName   string  `json:"heroName"`
	Matches    int     `json:"matches"`
	Wins       int     `json:"wins"`
	WinRate    int     `json:"winRate"`
	AvgKills   float64 `json:"avgKills"`
	AvgDeaths  float64 `json:"avgDeaths"`
	AvgAssists float64 `json:"avgAssists"`
}

// PlayerProfileAnalysis 玩家近期综合技战术画像 DTO
type PlayerProfileAnalysis struct {
	AccountId       string            `json:"accountId"`
	TotalMatches    int               `json:"totalMatches"`
	Wins            int               `json:"wins"`
	Losses          int               `json:"losses"`
	WinRate         int               `json:"winRate"`
	AvgKills        float64           `json:"avgKills"`
	AvgDeaths       float64           `json:"avgDeaths"`
	AvgAssists      float64           `json:"avgAssists"`
	TopHeroes       []HeroPerformance `json:"topHeroes"`
	RecentHeroes    []HeroPerformance `json:"recentHeroes"`
	TacticalSummary string            `json:"tacticalSummary"`
	LastUpdated     string            `json:"lastUpdated"`
}

// ThreatHeroSummary 本局对抗克制/高危威胁英雄摘要
type ThreatHeroSummary struct {
	HeroID      int     `json:"heroId"`
	HeroName    string  `json:"heroName"`
	GamesPlayed int     `json:"gamesPlayed"`
	WinRate     float64 `json:"winRate"`     // 我方历史对抗该英雄胜率 (0~100)
	ThreatLevel string  `json:"threatLevel"` // "extreme" (严重克制，胜率<45%) | "high" (<48%) | "normal"
	ThreatDesc  string  `json:"threatDesc"`  // 克制点描述 (如 "强控法术爆发", "物理闪避/被动核心", "高机动位移逃生")
}

// RecommendedItem 单个推荐装备条目 DTO
type RecommendedItem struct {
	ItemID     int     `json:"itemId"`
	ItemKey    string  `json:"itemKey"`    // 规范化英文标识符 (如 "item_black_king_bar" 或 "bfury")
	ItemName   string  `json:"itemName"`   // 本地化中文名或展示名
	Category   string  `json:"category"`   // "start" | "early" | "mid" | "late" | "counter"
	Popularity int     `json:"popularity"` // 购买频次权重
	Reason     string  `json:"reason"`     // 针对性出装理由
	WinRate    float64 `json:"winRate,omitempty"`
}

// HeroMatchupRecommendation 基于对局双方阵容的大数据针对性出装推荐全景
type HeroMatchupRecommendation struct {
	HeroID       int                 `json:"heroId"`
	HeroName     string              `json:"heroName"`
	ThreatHeroes []ThreatHeroSummary `json:"threatHeroes"`
	StartItems   []RecommendedItem   `json:"startItems"`
	EarlyItems   []RecommendedItem   `json:"earlyItems"`
	MidItems     []RecommendedItem   `json:"midItems"`
	LateItems    []RecommendedItem   `json:"lateItems"`
	CounterItems []RecommendedItem   `json:"counterItems"`
	Source       string              `json:"source"` // "opendota" | "cache" | "offline_rule"
	UpdatedAt    string              `json:"updatedAt"`
}
