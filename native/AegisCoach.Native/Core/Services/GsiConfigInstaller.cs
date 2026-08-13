using System.Text;
using System.Text.RegularExpressions;

namespace AegisCoach.Core.Services;

/// <summary>
/// 生成并安装 Dota 2 Game State Integration 配置文件。
/// 配置文件必须命名为 gamestate_integration_*.cfg，放在 Dota 2 的 cfg 目录。
/// </summary>
public static class GsiConfigInstaller
{
    public const string CfgFileName = "gamestate_integration_aegiscoach.cfg";

    public static string GenerateCfg(int port, string token)
    {
        var builder = new StringBuilder();
        builder.AppendLine("\"AegisCoach\"");
        builder.AppendLine("{");
        builder.AppendLine($"    \"uri\" \"http://127.0.0.1:{port}/\"");
        builder.AppendLine("    \"timeout\" \"5.0\"");
        builder.AppendLine("    \"buffer\" \"0.1\"");
        builder.AppendLine("    \"throttle\" \"0.1\"");
        builder.AppendLine("    \"heartbeat\" \"30.0\"");
        builder.AppendLine("    \"data\"");
        builder.AppendLine("    {");
        builder.AppendLine("        \"provider\" \"1\"");
        builder.AppendLine("        \"map\" \"1\"");
        builder.AppendLine("        \"player\" \"1\"");
        builder.AppendLine("        \"hero\" \"1\"");
        builder.AppendLine("        \"abilities\" \"1\"");
        builder.AppendLine("        \"items\" \"1\"");
        builder.AppendLine("        \"buildings\" \"1\"");
        builder.AppendLine("        \"roshan\" \"1\"");
        builder.AppendLine("    }");
        builder.AppendLine("    \"auth\"");
        builder.AppendLine("    {");
        builder.AppendLine($"        \"token\" \"{token}\"");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    /// <summary>
    /// 定位 Dota 2 的 cfg 目录。优先使用用户手动指定路径，其次按常见 Steam 目录和
    /// libraryfolders.vdf 中登记的库路径扫描。找不到时返回 null。
    /// </summary>
    public static string? LocateDotaCfgDirectory(string? manualPath = null)
    {
        if (!string.IsNullOrWhiteSpace(manualPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(manualPath.Trim());
            if (Directory.Exists(expanded)) return expanded;
        }

        var libraries = new List<string>();
        foreach (var root in EnumerateSteamRoots())
        {
            libraries.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (var path in ParseLibraryPaths(vdf))
                {
                    if (Directory.Exists(path)) libraries.Add(path);
                }
            }
        }

        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var common = Path.Combine(library, "steamapps", "common", "dota 2 beta");
            if (!Directory.Exists(common)) continue;

            // Dota 2 的 GSI 配置文件读取目录是 cfg 下的 gamestate_integration 子目录：
            // 新布局 <common>/game/dota/cfg/gamestate_integration；旧布局 <common>/dota/cfg/gamestate_integration。
            var gameDota = Path.Combine(common, "game", "dota");
            if (Directory.Exists(gameDota))
            {
                var cfg = Path.Combine(gameDota, "cfg", "gamestate_integration");
                if (Directory.Exists(cfg)) return cfg;
                try
                {
                    Directory.CreateDirectory(cfg);
                    return cfg;
                }
                catch
                {
                    return null;
                }
            }

            var legacyDota = Path.Combine(common, "dota");
            if (Directory.Exists(legacyDota))
            {
                var cfg = Path.Combine(legacyDota, "cfg", "gamestate_integration");
                if (Directory.Exists(cfg)) return cfg;
                try
                {
                    Directory.CreateDirectory(cfg);
                    return cfg;
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }

    public static InstallResult Install(string? manualPath, int port, string token)
    {
        var content = GenerateCfg(port, token);
        var directory = LocateDotaCfgDirectory(manualPath);
        if (directory is null)
        {
            return new InstallResult
            {
                Success = false,
                Message = "未找到 Dota 2 的配置目录。请在高级设置中手动选择目录后重试。",
                CfgContent = content,
            };
        }

        try
        {
            var filePath = Path.Combine(directory, CfgFileName);
            AtomicFile.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return new InstallResult
            {
                Success = true,
                FilePath = filePath,
                Message = $"已写入 {filePath}",
                CfgContent = content,
            };
        }
        catch (Exception exception)
        {
            return new InstallResult
            {
                Success = false,
                Message = $"写入失败：{exception.Message}",
                CfgContent = content,
            };
        }
    }

    private static IEnumerable<string> EnumerateSteamRoots()
    {
        var roots = new List<string>();
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        roots.Add(Path.Combine(programFilesX86, "Steam"));
        roots.Add(Path.Combine(programFiles, "Steam"));

        foreach (var drive in new[] { "C", "D", "E", "F", "G", "H" })
        {
            var path = Path.Combine($@"{drive}:\", "Steam");
            if (Directory.Exists(path)) roots.Add(path);
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ParseLibraryPaths(string vdfPath)
    {
        foreach (var line in File.ReadLines(vdfPath))
        {
            var match = Regex.Match(line, "\"path\"\\s+\"([^\"]+)\"");
            if (!match.Success) continue;
            var value = match.Groups[1].Value.Replace("\\\\", "\\");
            yield return value;
        }
    }
}

public sealed class InstallResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public required string CfgContent { get; init; }
    public string? FilePath { get; init; }
}
