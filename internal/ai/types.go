package ai

// TacticalContext 战术提问实战上下文快照实体
type TacticalContext struct {
	HeroID               int      `json:"heroId,omitempty"`
	HeroName             string   `json:"heroName,omitempty"`
	HeroLevel            int      `json:"heroLevel,omitempty"`
	HealthPercent        int      `json:"healthPercent,omitempty"`
	ManaPercent          int      `json:"manaPercent,omitempty"`
	Gold                 int      `json:"gold,omitempty"`
	ClockTime            int      `json:"clockTime,omitempty"`
	KDA                  string   `json:"kda,omitempty"`
	Items                []string `json:"items,omitempty"`
	Abilities            []string `json:"abilities,omitempty"`
	UserNotes            string   `json:"userNotes,omitempty"`
	PlayerProfileSnippet string   `json:"playerProfileSnippet,omitempty"`
}

// CoachReply 即时战术回答响应实体
type CoachReply struct {
	Answer    string `json:"answer"`
	ModelUsed string `json:"modelUsed"`
	IsNpu     bool   `json:"isNpu"`
}

// MatchReviewResult 赛后对局深度复盘分析报告实体 (参考 BrightGir 决策层级)
type MatchReviewResult struct {
	Overview            string   `json:"overview"`
	KeyTurningPoints    string   `json:"keyTurningPoints"`
	ItemizationAnalysis string   `json:"itemizationAnalysis"`
	NextMatchActions    []string `json:"nextMatchActions"`
	FullText            string   `json:"fullText"`
	ModelUsed           string   `json:"modelUsed"`
}

// AiEndpointTestResult 端点连通性测试结果实体
type AiEndpointTestResult struct {
	Success         bool   `json:"success"`
	LatencyMs       int64  `json:"latencyMs"`
	StatusCode      int    `json:"statusCode,omitempty"`
	ModelUsed       string `json:"modelUsed"`
	ResponsePreview string `json:"responsePreview,omitempty"`
	ErrorMessage    string `json:"errorMessage,omitempty"`
	TestedAt        string `json:"testedAt"`
}
