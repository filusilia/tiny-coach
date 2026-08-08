using System.Security;
using Microsoft.Win32;

namespace AegisCoach.Native.Services;

public sealed record NpuCapabilityReport(
    bool IsDetected,
    IReadOnlyList<string> DeviceNames,
    string Summary)
{
    public static NpuCapabilityReport NotDetected { get; } = new(
        false,
        [],
        "未检测到可识别的 NPU；规则引擎仍会正常工作。");
}

public static class NpuCapabilityDetector
{
    private static readonly string[] NpuKeywords =
    [
        "NPU",
        "AI Boost",
        "Neural Processing",
        "Ryzen AI",
        "XDNA",
        "Hexagon",
        "Neural Engine",
    ];

    public static NpuCapabilityReport Detect()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddMatchingValues(names, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        AddMatchingValues(names, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Enum\PCI", maxDepth: 3);
        AddMatchingValues(names, Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Enum\ACPI", maxDepth: 3);

        if (names.Count == 0) return NpuCapabilityReport.NotDetected;

        var devices = names.Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        return new NpuCapabilityReport(
            true,
            devices,
            $"检测到 NPU：{string.Join("、", devices)}；NPU 推理不占用 GPU，也不占用 CPU 主核。");
    }

    private static void AddMatchingValues(
        HashSet<string> names,
        RegistryKey root,
        string path,
        int maxDepth = 1)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is not null) ScanKey(names, key, 0, maxDepth);
        }
        catch (SecurityException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ScanKey(HashSet<string> names, RegistryKey key, int depth, int maxDepth)
    {
        foreach (var valueName in new[] { "DriverDesc", "DeviceDesc", "FriendlyName", "Description" })
        {
            if (key.GetValue(valueName) is not string value) continue;
            var displayName = value.Split(';').LastOrDefault()?.Trim() ?? value.Trim();
            if (NpuKeywords.Any(keyword => displayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                names.Add(displayName);
            }
        }

        if (depth >= maxDepth) return;
        foreach (var childName in key.GetSubKeyNames())
        {
            try
            {
                using var child = key.OpenSubKey(childName);
                if (child is not null) ScanKey(names, child, depth + 1, maxDepth);
            }
            catch (SecurityException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
