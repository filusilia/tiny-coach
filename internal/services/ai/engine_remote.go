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

// RemoteEngine 面向远程云端 Response API 标准规范的引擎，遵循 /responses 协议
type RemoteEngine struct {
	mu         sync.RWMutex
	endpoint   string
	apiKey     string
	httpClient *http.Client
}

func NewRemoteEngine(endpoint, apiKey string) *RemoteEngine {
	return &RemoteEngine{
		endpoint:   normalizeBaseUrl(endpoint, "https://api.deepseek.com/v1"),
		apiKey:     strings.TrimSpace(apiKey),
		httpClient: &http.Client{Timeout: 45 * time.Second},
	}
}

func (e *RemoteEngine) Name() string {
	return "remote"
}

func (e *RemoteEngine) SetCredentials(endpoint, apiKey string) {
	e.mu.Lock()
	defer e.mu.Unlock()
	e.endpoint = normalizeBaseUrl(endpoint, "https://api.deepseek.com/v1")
	e.apiKey = strings.TrimSpace(apiKey)
}

func (e *RemoteEngine) getCredentials() (string, string) {
	e.mu.RLock()
	defer e.mu.RUnlock()
	return e.endpoint, e.apiKey
}

// Ping 测试远程连通性
func (e *RemoteEngine) Ping(ctx context.Context, model string) (int64, string, error) {
	cleanModel := strings.TrimSpace(model)
	if cleanModel == "" {
		cleanModel = "deepseek-chat"
	}

	start := time.Now()
	content, err := e.Complete(ctx, "Ping", cleanModel, 10, 0.1)
	latency := time.Since(start).Milliseconds()
	if err != nil {
		return latency, "", err
	}
	return latency, content, nil
}

// Complete 遵循标准 /chat/completions 协议执行补全
func (e *RemoteEngine) Complete(ctx context.Context, prompt, model string, maxTokens int, temperature float64) (string, error) {
	endpoint, apiKey := e.getCredentials()

	cleanModel := strings.TrimSpace(model)
	if cleanModel == "" {
		cleanModel = "deepseek-chat"
	}
	if maxTokens <= 0 {
		maxTokens = 800
	}

	if apiKey == "" {
		return "", fmt.Errorf("云端 API Key 未配置，请在系统设置中填写 API Key")
	}

	reqUrl := fmt.Sprintf("%s/chat/completions", endpoint)
	payload := map[string]any{
		"model": cleanModel,
		"messages": []map[string]string{
			{"role": "user", "content": prompt},
		},
		"max_tokens":  maxTokens,
		"temperature": temperature,
	}

	bodyBytes, err := json.Marshal(payload)
	if err != nil {
		return "", fmt.Errorf("远程请求序列化失败: %w", err)
	}

	httpReq, err := http.NewRequestWithContext(ctx, http.MethodPost, reqUrl, bytes.NewReader(bodyBytes))
	if err != nil {
		return "", fmt.Errorf("创建远程 HTTP 请求失败: %w", err)
	}

	httpReq.Header.Set("Content-Type", "application/json")
	httpReq.Header.Set("Authorization", fmt.Sprintf("Bearer %s", apiKey))

	resp, err := e.httpClient.Do(httpReq)
	if err != nil {
		return "", fmt.Errorf("连接远程端点失败: %w", err)
	}
	defer resp.Body.Close()

	limited := io.LimitReader(resp.Body, maxAiResponseBytes)
	respBody, err := io.ReadAll(limited)
	if err != nil {
		return "", fmt.Errorf("读取远程响应失败: %w", err)
	}

	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return "", fmt.Errorf("远程端点返回异常状态 HTTP %d: %s", resp.StatusCode, string(respBody))
	}

	// 解析标准 OpenAI / Chat Completions 协议，并兼容 Response API 格式
	var openAiResp struct {
		Choices []struct {
			Message struct {
				Content string `json:"content"`
			} `json:"message"`
			Text string `json:"text"`
		} `json:"choices"`
		OutputText string `json:"output_text"`
		Output     []struct {
			Text string `json:"text"`
		} `json:"output"`
		Error *struct {
			Message string `json:"message"`
		} `json:"error"`
	}

	if err := json.Unmarshal(respBody, &openAiResp); err != nil {
		return "", fmt.Errorf("解析远程 AI 响应失败: %w", err)
	}

	if openAiResp.Error != nil && openAiResp.Error.Message != "" {
		return "", fmt.Errorf("远程服务端报错: %s", openAiResp.Error.Message)
	}

	// 1. 优先提取标准 choices[0].message.content
	if len(openAiResp.Choices) > 0 {
		if content := strings.TrimSpace(openAiResp.Choices[0].Message.Content); content != "" {
			return content, nil
		}
		if text := strings.TrimSpace(openAiResp.Choices[0].Text); text != "" {
			return text, nil
		}
	}

	// 2. 兼容 output_text / output
	if text := strings.TrimSpace(openAiResp.OutputText); text != "" {
		return text, nil
	}
	if len(openAiResp.Output) > 0 {
		for _, item := range openAiResp.Output {
			if strings.TrimSpace(item.Text) != "" {
				return strings.TrimSpace(item.Text), nil
			}
		}
	}

	return "", fmt.Errorf("远程端点未返回有效文本内容")
}
