using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AegisCoach.Native.Services;

public sealed class UpdateInfo
{
    public bool HasUpdate { get; init; }
    public string CurrentVersion { get; init; } = "1.0.0";
    public string LatestVersion { get; init; } = "1.0.0";
    public string ReleaseNotes { get; init; } = string.Empty;
    public string ReleaseUrl { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

public sealed class UpdateCheckService
{
    private static readonly Lazy<UpdateCheckService> LazyInstance =
        new(() => new UpdateCheckService());

    public static UpdateCheckService Instance => LazyInstance.Value;

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private const string RepoApiUrl = "https://api.github.com/repos/filus/aegis-coach/releases/latest";
    private const string DefaultReleasePage = "https://github.com/filus/aegis-coach/releases";

    public string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public UpdateCheckService()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AegisCoach", CurrentVersion));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion;
        try
        {
            using var response = await _http.GetAsync(RepoApiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateInfo
                {
                    CurrentVersion = current,
                    LatestVersion = current,
                    ReleaseUrl = DefaultReleasePage,
                    ErrorMessage = $"获取更新失败（HTTP {(int)response.StatusCode}），可直接访问发布页查看",
                };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubReleaseDto>(content);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateInfo
                {
                    CurrentVersion = current,
                    LatestVersion = current,
                    ReleaseUrl = DefaultReleasePage,
                };
            }

            var latestTag = release.TagName.TrimStart('v', 'V');
            var hasUpdate = IsNewerVersion(latestTag, current);

            return new UpdateInfo
            {
                HasUpdate = hasUpdate,
                CurrentVersion = current,
                LatestVersion = latestTag,
                ReleaseNotes = string.IsNullOrWhiteSpace(release.Body) ? "常规性能优化与问题修复" : release.Body.Trim(),
                ReleaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? DefaultReleasePage : release.HtmlUrl,
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateInfo { CurrentVersion = current, LatestVersion = current, ReleaseUrl = DefaultReleasePage };
        }
        catch (Exception exception)
        {
            return new UpdateInfo
            {
                CurrentVersion = current,
                LatestVersion = current,
                ReleaseUrl = DefaultReleasePage,
                ErrorMessage = $"检查更新失败（{exception.Message}），可直接前往发布页下载",
            };
        }
    }

    private static bool IsNewerVersion(string latestStr, string currentStr)
    {
        if (Version.TryParse(latestStr, out var latest) && Version.TryParse(currentStr, out var current))
        {
            return latest > current;
        }
        return string.Compare(latestStr, currentStr, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("body")] public string Body { get; set; } = string.Empty;
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = string.Empty;
    }
}
