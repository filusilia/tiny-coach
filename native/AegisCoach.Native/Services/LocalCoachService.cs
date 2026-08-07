using System.Net.Http.Json;
using System.Text.Json;
using AegisCoach.Core.Models;

namespace AegisCoach.Native.Services;

public sealed class LocalCoachService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    public async Task<string?> ExplainAsync(
        CoachAdvice advice,
        MatchSnapshot snapshot,
        string endpoint,
        string model,
        CancellationToken cancellationToken)
    {
        var prompt = "你是Dota 2实时教练。只解释这一条规则提醒，不替代规则判断。用不超过35个中文字符，给出一个可执行动作。不要编造敌方信息。\n" +
            "规则：" + advice.Title + "\n提醒：" + advice.Message +
            "\n状态：英雄" + snapshot.Hero.HealthPercent.ToString("0") + "%生命、" +
            snapshot.Hero.ManaPercent.ToString("0") + "%魔法，时间" + snapshot.GameTimeText + "。";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        using var response = await _httpClient.PostAsJsonAsync(
            NormalizeEndpoint(endpoint) + "/api/generate",
            new { model, prompt, stream = false, options = new { temperature = 0.1, num_predict = 48 } },
            timeout.Token);
        if (!response.IsSuccessStatusCode) return null;
        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(timeout.Token);
        return string.IsNullOrWhiteSpace(result?.Response) ? null : result.Response.Trim();
    }

    public async Task<string?> ProbeAsync(string endpoint, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(800));
        using var response = await _httpClient.GetAsync(NormalizeEndpoint(endpoint) + "/api/tags", timeout.Token);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync(timeout.Token);
    }

    public async Task<(bool Success, string Message)> PullAsync(
        string endpoint,
        string model,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, NormalizeEndpoint(endpoint) + "/api/pull")
        {
            Content = JsonContent.Create(new { name = model, stream = true }),
        };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return (false, "Ollama 返回 " + (int)response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var json = JsonDocument.Parse(line);
                var status = json.RootElement.TryGetProperty("status", out var value) ? value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(status)) progress?.Report(status);
                if (json.RootElement.TryGetProperty("error", out var error))
                    return (false, error.GetString() ?? "模型下载失败");
            }
            catch (JsonException)
            {
                progress?.Report(line);
            }
        }

        return (true, "已请求下载 " + model);
    }

    private static string NormalizeEndpoint(string endpoint) => endpoint.TrimEnd('/');

    private sealed class OllamaGenerateResponse
    {
        public string? Response { get; set; }
    }
}
