package tts

import "context"

// TtsEngine 语音引擎统一接口（当前仅 MeloTTS 实现，保留以备未来扩展）
type TtsEngine interface {
	Name() string
	Synthesize(ctx context.Context, text string, speed float64) (string, error)
	IsReady() bool
	Status() EngineStatus
	Close() error
}

// EngineStatus 引擎状态快照，供前端展示
type EngineStatus struct {
	Engine      string `json:"engine"`
	Ready       bool   `json:"ready"`
	Downloading bool   `json:"downloading"`
	Progress    int    `json:"progress"`
	Message     string `json:"message"`
	Error       string `json:"error"`
}
