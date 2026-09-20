package player

// OpenDotaMatchSummary OpenDota REST API 单场比赛简报实体
type OpenDotaMatchSummary struct {
	MatchID     int64   `json:"match_id"`
	PlayerSlot  int     `json:"player_slot"`
	RadiantWin  bool    `json:"radiant_win"`
	Duration    int     `json:"duration"`
	GameMode    int     `json:"game_mode"`
	LobbyType   int     `json:"lobby_type"`
	HeroID      int     `json:"hero_id"`
	StartTime   int64   `json:"start_time"`
	Version     int     `json:"version"`
	Kills       int     `json:"kills"`
	Deaths      int     `json:"deaths"`
	Assists     int     `json:"assists"`
	AverageRank int     `json:"average_rank"`
	PartySize   int     `json:"party_size"`
}

// HeroPerformance 单个英雄在统计周期内的技战术综合表现实体
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

// PlayerProfileAnalysis 玩家近期综合技战术画像实体
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
