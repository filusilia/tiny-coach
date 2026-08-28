using System;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

var cacheDir = @"E:\documents\AegisCoach\cache";
var files = Directory.GetFiles(cacheDir, "hero-*-zh-*.json");

var unparsedExamples = new List<string>();
var zeroStatExamples = new List<string>();
var englishStatExamples = new List<string>();

var paramRegex = new Regex(@"(?i)\+?%?\{s:([a-zA-Z0-9_$]+)\}%?|%([a-zA-Z0-9_$]+)%|\{\{([a-zA-Z0-9_$]+)\}\}|\{([a-zA-Z0-9_$]+)\}");

foreach (var file in files)
{
    var doc = JsonDocument.Parse(File.ReadAllText(file));
    if (doc.RootElement.TryGetProperty("abilities", out var abilities))
    {
        foreach (var ab in abilities.EnumerateArray())
        {
            var aname = ab.TryGetProperty("name_loc", out var nl) ? nl.GetString() ?? "" : "";
            var desc = ab.TryGetProperty("desc_loc", out var dl) ? dl.GetString() ?? "" : "";
            var svals = ab.TryGetProperty("special_values", out var sv) ? sv.EnumerateArray().ToList() : new List<JsonElement>();
            
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in svals)
            {
                var vn = v.GetProperty("name").GetString() ?? "";
                var vf = v.TryGetProperty("values_float", out var vfloats) ? vfloats.EnumerateArray().Select(x => x.GetDouble()).ToList() : new List<double>();
                if (vf.Count > 0)
                {
                    dict[vn] = string.Join(" / ", vf.Select(x => x % 1 == 0 ? x.ToString("0") : x.ToString("0.##")));
                    if (vn.StartsWith("bonus_")) dict[vn[6..]] = dict[vn];
                    else dict["bonus_" + vn] = dict[vn];
                    if (vn.StartsWith("tooltip_")) dict[vn[8..]] = dict[vn];
                    else dict["tooltip_" + vn] = dict[vn];
                }
            }
            
            // Check remaining placeholders after resolve
            var resolved = paramRegex.Replace(desc, m =>
            {
                var k = "";
                for (int i = 1; i < m.Groups.Count; i++) if (m.Groups[i].Success) { k = m.Groups[i].Value; break; }
                return dict.TryGetValue(k, out var val) ? val : "";
            }).Replace("%%", "%").Trim();
            
            var leftover = Regex.Matches(resolved, @"%[a-zA-Z0-9_$]+%|\{s:[a-zA-Z0-9_$]+\}");
            if (leftover.Count > 0)
            {
                unparsedExamples.Add($"{aname}: {string.Join(", ", leftover.Select(x => x.Value))}");
            }
        }
    }
}

Console.WriteLine($"Unparsed placeholder count: {unparsedExamples.Count}");
foreach (var e in unparsedExamples.Take(15)) Console.WriteLine("  " + e);
