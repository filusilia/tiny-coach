package ai

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strings"
	"sync"
	"time"
)

// OllamaEngine 面向本地 Ollama 服务的 AI 引擎，严格遵循 /chat/completions 标准协议
type OllamaEngine struct {
	mu         sync.RWMutex
	endpoint   string
	httpClient *http.Client
}

func NewOllamaEngine(endpoint string) *OllamaEngine {
	return &OllamaEngine{
		endpoint:   normalizeBaseUrl(endpoint, "http://127.0.0.1:11434/v1"),
		httpClient: &http.Client{Timeout: 45 * time.Second},
	}
}

func (e *OllamaEngine) Name() string {
	return "ollama"
}

func (e *OllamaEngine) SetCredentials(endpoint, _ string) {
	e.mu.Lock()
	defer e.mu.Unlock()
	e.endpoint = normalizeBaseUrl(endpoint, "http://127.0.0.1:11434/v1")
}

func (e *OllamaEngine) getEndpoint() string {
	e.mu.RLock()
	defer e.mu.RUnlock()
	return e.endpoint
}

// Ping 测试连通性
func (e *OllamaEngine) Ping(ctx context.Context, model string) (int64, string, error) {
	cleanModel := strings.TrimSpace(model)
	if cleanModel == "" {
		cleanModel = "qwen3.5:1.5b"
	}

	start := time.Now()
	content, err := e.Complete(ctx, "Ping", cleanModel, 10, 0.1)
	latency := time.Since(start).Milliseconds()
	if err != nil {
		return latency, "", err
	}
	return latency, content, nil
}

// Complete 执行聊天补全 (/chat/completions)
func (e *OllamaEngine) Complete(ctx context.Context, prompt, model string, maxTokens int, temperature float64) (string, error) {
	cleanModel := strings.TrimSpace(model)
	if cleanModel == "" {
		cleanModel = "qwen3.5:1.5b"
	}
	if maxTokens <= 0 {
		maxTokens = 800
	}

	reqUrl := fmt.Sprintf("%s/chat/completions", e.getEndpoint())
	payload := map[string]any{
		"model": cleanModel,
		"messages": []map[string]string{
			{"role": "user", "content": prompt},
		},
		"temperature": temperature,
		"max_tokens":  maxTokens,
	}

	bodyBytes, err := json.Marshal(payload)
	if err != nil {
		return "", fmt.Errorf("Ollama 请求序列化失败: %w", err)
	}

	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, reqUrl, bytes.NewReader(bodyBytes))
	if err != nil {
		return "", fmt.Errorf("创建 Ollama HTTP 请求失败: %w", err)
	}
	httpReq.Header.Set("Content-Type", "application/json")

	resp, err := e.httpClient.Do(httpReq)
	if err != nil {
		return "", fmt.Errorf("连接 Ollama 失败: %w", err)
	}
	defer resp.Body.Close()

	limited := io.LimitReader(resp.Body, maxAiResponseBytes)
	respBody, err := io.ReadAll(limited)
	if err != nil {
		return "", fmt.Errorf("读取 Ollama 响应失败: %w", err)
	}

	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return "", fmt.Errorf("Ollama 返回异常状态 HTTP %d: %s", resp.StatusCode, string(respBody))
	}

	var chatResp struct {
		Choices []struct {
			Message struct {
				Content string `json:"content"`
			} `json:"message"`
			Text string `json:"text"`
		} `json:"choices"`
		Response string `json:"response"`
		Error    *struct {
			Message string `json:"message"`
		} `json:"error"`
	}

	if err := json.Unmarshal(respBody, &chatResp); err != nil {
		return "", fmt.Errorf("解析 Ollama 响应失败: %w", err)
	}

	if chatResp.Error != nil && chatResp.Error.Message != "" {
		return "", fmt.Errorf("Ollama 服务端报错: %s", chatResp.Error.Message)
	}

	if len(chatResp.Choices) > 0 {
		if content := strings.TrimSpace(chatResp.Choices[0].Message.Content); content != "" {
			return content, nil
		}
		if text := strings.TrimSpace(chatResp.Choices[0].Text); text != "" {
			return text, nil
		}
	}

	if chatResp.Response != "" {
		return strings.TrimSpace(chatResp.Response), nil
	}

	return "", fmt.Errorf("Ollama 未返回有效内容")
}
