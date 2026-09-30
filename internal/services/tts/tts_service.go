package tts

import (
	"context"
	"fmt"
	"strings"
	"sync"
)

// TtsService 语音合成服务（当前仅持有 MeloTTS 引擎，保留接口以备未来扩展）
type TtsService struct {
	mu     sync.RWMutex
	engine TtsEngine
}

var (
	ttsServiceInstance *TtsService
	ttsServiceOnce     sync.Once
)

func GetTtsService() *TtsService {
	ttsServiceOnce.Do(func() {
		ttsServiceInstance = &TtsService{
			engine: NewMeloTtsEngine(),
		}
		// 不再在构造时无条件触发下载，改为懒加载
	})
	return ttsServiceInstance
}

// SynthesizeSpeech 合成入口
func (t *TtsService) SynthesizeSpeech(text string, speed float64) (string, error) {
	t.mu.RLock()
	eng := t.engine
	t.mu.RUnlock()

	if eng == nil {
		return "", fmt.Errorf("TTS 引擎未初始化")
	}
	return eng.Synthesize(context.Background(), text, speed)
}

// GetGoSpeechStatus 兼容旧接口：返回是否就绪
func (t *TtsService) GetGoSpeechStatus() (bool, error) {
	t.mu.RLock()
	defer t.mu.RUnlock()
	if t.engine == nil {
		return false, nil
	}
	return t.engine.IsReady(), nil
}

// GetTtsStatus 返回引擎完整状态，供设置页展示进度
func (t *TtsService) GetTtsStatus() EngineStatus {
	t.mu.RLock()
	defer t.mu.RUnlock()
	if t.engine == nil {
		return EngineStatus{Engine: "none", Message: "未初始化"}
	}
	return t.engine.Status()
}

// TestGoSpeech 合成并返回首段试听音频 (如果未提供文本则使用默认测试语句)
func (t *TtsService) TestGoSpeech(text string, speed float64) (string, error) {
	cleanText := strings.TrimSpace(text)
	if cleanText == "" {
		cleanText = "神盾教练语音播报已连接，战术状态就绪。"
	}
	return t.SynthesizeSpeech(cleanText, speed)
}

// Close 释放引擎资源
func (t *TtsService) Close() error {
	t.mu.Lock()
	defer t.mu.Unlock()
	if t.engine != nil {
		return t.engine.Close()
	}
	return nil
}
