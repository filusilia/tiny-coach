package ai

import (
	"context"
	"strings"
)

// AiEngine AI 战术推理引擎统一接口
type AiEngine interface {
	Name() string
	Ping(ctx context.Context, model string) (latencyMs int64, outputText string, err error)
	Complete(ctx context.Context, prompt, model string, maxTokens int, temperature float64) (string, error)
	SetCredentials(endpoint, apiKey string)
}

// normalizeBaseUrl 统一格式化 AI 端点基准 URL
func normalizeBaseUrl(endpoint, fallback string) string {
	ep := strings.TrimSpace(endpoint)
	if ep == "" {
		return fallback
	}
	ep = strings.TrimRight(ep, "/")

	// 剔除调用方误带的端点后缀
	for _, suffix := range []string{"/chat/completions", "/responses", "/completions"} {
		if strings.HasSuffix(strings.ToLower(ep), suffix) {
			ep = ep[:len(ep)-len(suffix)]
			break
		}
	}

	// 规范化 /openapi 为 /v1
	if strings.HasSuffix(strings.ToLower(ep), "/openapi") {
		ep = ep[:len(ep)-len("/openapi")] + "/v1"
	}

	// 若既无 /v1 也无其他子路径，但指定了 host:port（如 http://127.0.0.1:11434），补全 /v1
	if !strings.HasSuffix(ep, "/v1") && !strings.Contains(ep, "/v") {
		ep = ep + "/v1"
	}

	return ep
}
