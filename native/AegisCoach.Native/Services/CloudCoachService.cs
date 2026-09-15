using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AegisCoach.Core.Models;

namespace AegisCoach.Native.Services;

/// <summary>
/// 云端 OpenAI 兼容接口后端：本地零性能开销，支持 Chat Completions 与 Responses API，支持动态拉取远程模型。
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
        CancellationToken cancellationToken,
        string protocol = "chat")
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(prompt)) return null;
        if (!IsLocalEndpoint(endpoint) && string.IsNullOrWhiteSpace(apiKey)) return null;
        if (!await _generationGate.WaitAsync(TimeSpan.FromMilliseconds(20), cancellationToken)) return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            if (protocol.Equals("responses", StringComparison.OrdinalIgnoreCase))
            {
                var result = await TryCallResponsesApiAsync(
                    endpoint, model, apiKey, prompt, systemPrompt, maxTokens, 0.3, timeout.Token);
                if (!string.IsNullOrWhiteSpace(result)) return result;
            }

            return await CallChatCompletionsApiAsync(
                endpoint, model, apiKey, prompt, systemPrompt, maxTokens, 0.3, timeout.Token);
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

    /// <summary>实时规则事件解释。失败返回 null。</summary>
    public async Task<string?> ExplainAsync(
        CoachAdvice advice,
        MatchSnapshot snapshot,
        string endpoint,
        string model,
        string apiKey,
        CancellationToken cancellationToken,
        string protocol = "chat",
        string? keywords = null)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        if (!IsLocalEndpoint(endpoint) && string.IsNullOrWhiteSpace(apiKey)) return null;
        if (!await _generationGate.WaitAsync(TimeSpan.FromMilliseconds(20), cancellationToken)) return null;

        try
        {
            var prompt = CoachAiPrompts.BuildPrompt(advice, snapshot, keywords);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));

            var systemPrompt = string.IsNullOrWhiteSpace(keywords)
                ? "你是Dota 2实时教练。只解释规则引擎触发的一条提醒，不替代规则判断，不编造敌方信息。"
                : $"你是Dota 2实时教练。只解释规则引擎触发的一条提醒，不替代规则判断。要点：{keywords.Trim()}。不编造敌方信息。";

            if (protocol.Equals("responses", StringComparison.OrdinalIgnoreCase))
            {
                var result = await TryCallResponsesApiAsync(
                    endpoint, model, apiKey, prompt, systemPrompt, 48, 0.1, timeout.Token);
                if (!string.IsNullOrWhiteSpace(result)) return result;
            }

            return await CallChatCompletionsApiAsync(
                endpoint, model, apiKey, prompt, systemPrompt, 48, 0.1, timeout.Token);
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

    /// <summary>向远程接口拉取可用模型列表（支持 OpenAI / DeepSeek / Ollama / LocalAI 等）。</summary>
    public async Task<List<string>> FetchModelsAsync(string endpoint, string apiKey = "", CancellationToken cancellationToken = default)
    {
        var rawEndpoint = (endpoint ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(rawEndpoint)) return new List<string>();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        // 优先：若是 Ollama 服务，尝试请求原生 /api/tags
        if (rawEndpoint.Contains("11434") || rawEndpoint.Contains("ollama", StringComparison.OrdinalIgnoreCase))
        {
            var baseHost = rawEndpoint.Replace("/v1", "").TrimEnd('/');
            try
            {
                using var ollamaReq = new HttpRequestMessage(HttpMethod.Get, baseHost + "/api/tags");
                using var ollamaResp = await _httpClient.SendAsync(ollamaReq, timeout.Token);
                if (ollamaResp.IsSuccessStatusCode)
                {
                    var json = await ollamaResp.Content.ReadAsStringAsync(timeout.Token);
                    var tags = ParseModelsJson(json);
                    if (tags.Count > 0) return tags;
                }
            }
            catch
            {
                // 继续尝试标准 /v1/models
            }
        }

        var baseUri = NormalizeEndpoint(rawEndpoint);
        var url = baseUri.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? baseUri
            : baseUri + "/models";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(timeout.Token);
                var models = ParseModelsJson(content);
                if (models.Count > 0) return models;
            }

            // 若 /models 返回非 200 或空，尝试本地服务的 /api/tags
            if (IsLocalEndpoint(rawEndpoint))
            {
                var fallbackUrl = rawEndpoint.Replace("/v1", "").TrimEnd('/') + "/api/tags";
                try
                {
                    using var fallbackReq = new HttpRequestMessage(HttpMethod.Get, fallbackUrl);
                    using var fallbackResp = await _httpClient.SendAsync(fallbackReq, timeout.Token);
                    if (fallbackResp.IsSuccessStatusCode)
                    {
                        var fallbackContent = await fallbackResp.Content.ReadAsStringAsync(timeout.Token);
                        return ParseModelsJson(fallbackContent);
                    }
                }
                catch { }
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"服务端返回 HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }
            return new List<string>();
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("拉取模型超时，请检查接口地址与网络连接。");
        }
    }

    /// <summary>轻量探测：用 GET /models 验证接口与密钥是否可用（1000ms 上限）。</summary>
    public async Task<bool> ProbeAsync(string endpoint, string apiKey, CancellationToken cancellationToken)
    {
        if (!IsLocalEndpoint(endpoint) && string.IsNullOrWhiteSpace(apiKey)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(1000));
        var baseUri = NormalizeEndpoint(endpoint);
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUri + "/models");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        try
        {
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode) return true;
            if (IsLocalEndpoint(endpoint))
            {
                var tagsUri = baseUri.Replace("/v1", "").TrimEnd('/') + "/api/tags";
                using var tagsReq = new HttpRequestMessage(HttpMethod.Get, tagsUri);
                using var tagsResp = await _httpClient.SendAsync(tagsReq, timeout.Token);
                return tagsResp.IsSuccessStatusCode;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> TryCallResponsesApiAsync(
        string endpoint,
        string model,
        string apiKey,
        string prompt,
        string systemPrompt,
        int maxTokens,
        double temperature,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                NormalizeEndpoint(endpoint) + "/responses")
            {
                Content = JsonContent.Create(new
                {
                    model,
                    instructions = systemPrompt,
                    input = prompt,
                    temperature,
                    max_output_tokens = maxTokens,
                }),
            };
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("output_text", out var textEl) && !string.IsNullOrWhiteSpace(textEl.GetString()))
            {
                return textEl.GetString()?.Trim();
            }

            if (root.TryGetProperty("output", out var outputEl) && outputEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in outputEl.EnumerateArray())
                {
                    if (item.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in contentEl.EnumerateArray())
                        {
                            if (c.TryGetProperty("text", out var tEl))
                            {
                                var text = tEl.GetString();
                                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                            }
                        }
                    }
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> CallChatCompletionsApiAsync(
        string endpoint,
        string model,
        string apiKey,
        string prompt,
        string systemPrompt,
        int maxTokens,
        double temperature,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                NormalizeEndpoint(endpoint) + "/chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model,
                    temperature,
                    max_tokens = maxTokens,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = prompt },
                    },
                }),
            };
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken);
            var text = result?.Choices?.FirstOrDefault()?.Message?.Content;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static List<string> ParseModelsJson(string json)
    {
        var list = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataEl.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idEl))
                    {
                        var id = idEl.GetString();
                        if (!string.IsNullOrWhiteSpace(id)) list.Add(id);
                    }
                }
            }
            else if (root.TryGetProperty("models", out var modelsEl) && modelsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in modelsEl.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameEl))
                    {
                        var name = nameEl.GetString();
                        if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
                    }
                    else if (item.TryGetProperty("model", out var modelEl))
                    {
                        var m = modelEl.GetString();
                        if (!string.IsNullOrWhiteSpace(m)) list.Add(m);
                    }
                }
            }
        }
        catch
        {
            // 忽略 JSON 结构差异
        }
        return list.Distinct().OrderBy(x => x).ToList();
    }

    public static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = (endpoint ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;
        if (!trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Contains("/v1/") &&
            !trimmed.Contains("/api"))
        {
            trimmed += "/v1";
        }
        return trimmed;
    }

    public static bool IsLocalEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return false;
        return endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
               endpoint.Contains("127.0.0.1") ||
               endpoint.Contains("::1") ||
               endpoint.Contains("11434");
    }

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
