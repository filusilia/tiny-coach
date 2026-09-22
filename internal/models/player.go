package models

// OpenDotaMatchSummary OpenDota REST API 单场比赛简报实体 DTO
type OpenDotaMatchSummary struct {
	MatchID     int64 `json:"matchId"`
	PlayerSlot  int   `json:"playerSlot"`
	RadiantWin  bool  `json:"radiantWin"`
	Duration    int   `json:"duration"`
	GameMode    int   `json:"gameMode"`
	LobbyType   int   `json:"lobbyType"`
	HeroID      int   `json:"heroId"`
	StartTime   int64 `json:"startTime"`
	Version     int   `json:"version"`
	Kills       int   `json:"kills"`
	Deaths      int   `json:"deaths"`
	Assists     int   `json:"assists"`
	AverageRank int   `json:"averageRank"`
	PartySize   int   `json:"partySize"`
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
