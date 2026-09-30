package ai

import (
	"strings"
	"testing"
)

func TestNormalizeBaseUrl(t *testing.T) {
	cases := []struct {
		input    string
		fallback string
		expected string
	}{
		{"https://api.deepseek.com/v1/responses", "https://api.deepseek.com/v1", "https://api.deepseek.com/v1"},
		{"http://127.0.0.1:11434/chat/completions", "http://127.0.0.1:11434/v1", "http://127.0.0.1:11434/v1"},
		{"https://api.openai.com/openapi", "https://api.openai.com/v1", "https://api.openai.com/v1"},
		{"", "http://127.0.0.1:11434/v1", "http://127.0.0.1:11434/v1"},
		{"http://192.168.1.100:11434", "http://127.0.0.1:11434/v1", "http://192.168.1.100:11434/v1"},
	}
	for _, c := range cases {
		got := normalizeBaseUrl(c.input, c.fallback)
		if !strings.EqualFold(got, c.expected) {
			t.Errorf("normalizeBaseUrl(%q, %q) = %q; 期望 %q", c.input, c.fallback, got, c.expected)
		}
	}
}
