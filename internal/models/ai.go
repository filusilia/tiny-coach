package models

// BattleContextDTO 战局即时上下文 DTO
type BattleContextDTO struct {
	HeroID               int      `json:"heroId"`
	HeroName             string   `json:"heroName"`
	HeroLevel            int      `json:"heroLevel"`
	HealthPercent        int      `json:"healthPercent"`
	ManaPercent          int      `json:"manaPercent"`
	Gold                 int      `json:"gold"`
	ClockTime            string   `json:"clockTime"`
	KDA                  string   `json:"kda"`
	Items                []string `json:"items"`
	Abilities            []string `json:"abilities"`
	UserNotes            string   `json:"userNotes,omitempty"`
	PlayerProfileSnippet string   `json:"playerProfileSnippet,omitempty"`
}

// MatchReviewResultDTO 赛后 AI 深度战术复盘报告 DTO
type MatchReviewResultDTO struct {
	Overview            string   `json:"overview"`
	KeyTurningPoints    string   `json:"keyTurningPoints"`
	ItemizationAnalysis string   `json:"itemizationAnalysis"`
	NextMatchActions    []string `json:"nextMatchActions"`
	FullText            string   `json:"fullText"`
	ModelUsed           string   `json:"modelUsed"`
}

// AiEndpointTestResultDTO AI 端点连通性测试报告 DTO
type AiEndpointTestResultDTO struct {
	Success      bool   `json:"success"`
	LatencyMs    int64  `json:"latencyMs"`
	RawResponse  string `json:"rawResponse,omitempty"`
	ErrorMessage string `json:"errorMessage,omitempty"`
	ModelUsed    string `json:"modelUsed,omitempty"`
}
