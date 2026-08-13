namespace AegisCoach.Core.Services;

/// <summary>应用产生的用户文件统一保存在“文档\AegisCoach”。</summary>
public static class AegisCoachPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "AegisCoach");

    public static string Cache => Path.Combine(Root, "cache");
    public static string Analysis => Path.Combine(Root, "analysis");
    public static string Habits => Path.Combine(Root, "habits");
    public static string Snapshots => Path.Combine(Root, "gsi");
    public static string Models => Path.Combine(Root, "models");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string ExportedGsiConfig => Path.Combine(Root, GsiConfigInstaller.CfgFileName);
}
