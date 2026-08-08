using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AegisCoach.Core.Models;

namespace AegisCoach.Native.Services;

/// <summary>
/// 云端 OpenAI 兼容接口后端：本地零性能开销，只发送脱敏后的 evidence 提示词。
/// 密钥只用于请求鉴权，不写入日志；请求超时或校验失败时返回 null，由调用方回退规则原文。
/// </summary>
public sealed class CloudCoachService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim _generationGate = new(1, 1);

    /// <summary>通用文本生成（赛后分析等长文本场景）。失败返回 null。</summary>
    public async Task<string?> GenerateAsync(
        string prompt,
        string systemPrompt,
        string endpoint,
        string model,
        string apiKey,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(prompt)) return null;
        if (!await _generationGate.WaitAsync(TimeSpan.FromMilliseconds(20), cancellationToken)) return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                NormalizeEndpoint(endpoint) + "/chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model,
                    temperature = 0.3,
                    max_tokens = maxTokens,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = prompt },
                    },
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(timeout.Token);
            var text = result?.Choices?.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
        finally
        {
            _generationGate.Release();
        }
    }

    public async Task<string?> ExplainAsync(
        CoachAdvice advice,
        MatchSnapshot snapshot,
        string endpoint,
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        if (!await _generationGate.WaitAsync(TimeSpan.FromMilliseconds(20), cancellationToken)) return null;

        try
        {
            var prompt = CoachAiPrompts.BuildPrompt(advice, snapshot);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                NormalizeEndpoint(endpoint) + "/chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model,
                    temperature = 0.1,
                    max_tokens = 48,
                    messages = new[]
                    {
                        new
                        {
                            role = "system",
                            content = "你是Dota 2实时教练。只解释规则引擎触发的一条提醒，不替代规则判断，不编造敌方信息。",
                        },
                        new { role = "user", content = prompt },
                    },
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(timeout.Token);
            var text = result?.Choices?.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        finally
        {
            _generationGate.Release();
        }
    }

    /// <summary>轻量探测：用 GET /models 验证接口与密钥是否可用（800ms 上限）。</summary>
    public async Task<bool> ProbeAsync(string endpoint, string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        using var request = new HttpRequestMessage(HttpMethod.Get, NormalizeEndpoint(endpoint) + "/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeEndpoint(string endpoint) => endpoint.TrimEnd('/');

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public Message? Message { get; set; }
    }

    private sealed class Message
    {
        public string? Content { get; set; }
    }
}
