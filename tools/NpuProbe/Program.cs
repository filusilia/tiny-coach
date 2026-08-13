using System.Diagnostics;
using OpenVinoSharp;
using OpenVinoSharp.GenAI;

var defaultModelDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "AegisCoach", "models", "qwen2.5-1.5b-int4-ov");

var modelDir = args.Length > 0 ? args[0] : defaultModelDir;
var device = args.Length > 1 ? args[1] : "NPU";
var runs = args.Length > 2 ? int.Parse(args[2]) : 1;
var shortPrompt = args.Length > 3 && args[3] == "short";

if (!Directory.Exists(modelDir))
{
    Console.Error.WriteLine($"模型目录不存在：{modelDir}");
    return 2;
}

GenAI.Initialize();

// 1) 列出 OpenVINO 可见设备
try
{
    using var core = new Core();
    var devices = core.GetAvailableDevices();
    Console.WriteLine("OpenVINO 可用设备：" + string.Join(", ", devices));
}
catch (Exception ex)
{
    Console.WriteLine("设备枚举失败：" + ex.Message);
}

// 2) 用教练提示词在指定设备上生成
var prompt = shortPrompt
    ? "Dota 2教练：英雄23%生命，血量过低。用不超过35个中文字符给出可执行建议："
    : "你是Dota 2实时教练。只解释这一条规则提醒，不替代规则判断。用不超过35个中文字符，给出一个可执行动作。不要编造敌方信息。\n" +
      "规则：低血量\n提醒：你的生命值低于30%，请优先撤退或回到安全位置。\n" +
      "状态：英雄23%生命、41%魔法，时间25:30。";

Console.WriteLine($"设备 {device}，加载模型 {modelDir} …");
var load = Stopwatch.StartNew();
try
{
    using var pipe = new LLMPipeline(modelDir, device);
    load.Stop();
    Console.WriteLine($"模型加载完成（首次含编译）：{load.Elapsed.TotalSeconds:F1}s");

    using var config = new GenerationConfig()
        .SetMaxNewTokens((ulong)(shortPrompt ? 24 : 48))
        .SetTemperature(0.1f);

    for (var i = 0; i < runs; i++)
    {
        var gen = Stopwatch.StartNew();
        using var results = pipe.Generate(prompt, config);
        gen.Stop();

        var metrics = results.GetPerformanceMetrics();
        Console.WriteLine(
            $"[第{i + 1}次] 墙钟 {gen.Elapsed.TotalSeconds:F2}s | 首token {metrics.TimeToFirstToken:F2}s | " +
            $"生成 {metrics.GenerateDuration:F2}s | 吞吐 {metrics.Throughput:F1} tok/s | 输入 {metrics.NumInputTokens} tok | " +
            $"输出 {metrics.NumGenerationTokens} tok | {results.Text.Trim()}");
    }
    return 0;
}
catch (Exception ex)
{
    load.Stop();
    Console.Error.WriteLine($"设备 {device} 执行失败（{load.Elapsed.TotalSeconds:F1}s）：{ex.Message}");
    return 1;
}
