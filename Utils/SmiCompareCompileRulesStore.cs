using System.Text;
using System.Text.Json;
using ETD.Api.Models;
using Microsoft.AspNetCore.Hosting;

namespace ETD.Api.Utils
{
    public static class SmiCompareCompileRulesStore
    {
        public const string DefaultFileName = "smi-compare-compile-rules.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static SmiCompareCompileRules CreateDefault()
        {
            return new SmiCompareCompileRules
            {
                Purpose = "Qwen compares, compiles, parses, and may generate specialist support output for ETDP when enabled through Alpha. Every Qwen-created output must return to Mira for review before Pierre treats it as ready.",
                CompareRules = "Compare Mira responses or Qwen draft output against the active qualification context, ETDP workflow order, saved operator rules, mapped curriculum structure, and known route/page names. Flag contradictions, missing prerequisites, unsupported claims, route drift, and missing subject-matter detail before accepting an output as usable.",
                CompileRules = "Compile review outcomes into a clean English summary with: verdict first, discrepancies second, corrected route third, and recommended next ETDP action last. Keep compiled output concise, structured, and operator-ready so Mira can review and relay it clearly.",
                ParseRules = "Parse the user request, Mira reply, qualification scope, workflow tracker state, and any supporting context as separate layers before drawing conclusions. Preserve exact ETDP route names, qualification codes, and workflow terminology. Do not merge unrelated contexts.",
                Guardrails = "Do not overrule Codex on engineering decisions. Do not rewrite Mira's identity. Do not bypass Mira review. Do not invent missing qualifications, files, routes, or evidence. When assessment criteria are mentioned, write them out in full and never rely on shorthand codes. If context is missing, state that clearly and mark the review as incomplete rather than guessing.",
                OutputFormatRules = "Return English-only plain text. Start with a decision line, then list key review findings, then provide the compiled correction path or next action. Keep wording clear enough to paste into ETDP review notes.",
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        public static SmiCompareCompileRules Normalize(SmiCompareCompileRules? rules)
        {
            var normalized = rules ?? new SmiCompareCompileRules();
            normalized.Purpose = EnsureContains(
                CleanLongText(normalized.Purpose, 4000),
                "Every Qwen-created output must return to Mira for review before Pierre treats it as ready.");
            normalized.CompareRules = CleanLongText(normalized.CompareRules, 12000);
            normalized.CompileRules = CleanLongText(normalized.CompileRules, 12000);
            normalized.ParseRules = CleanLongText(normalized.ParseRules, 12000);
            normalized.Guardrails = EnsureContains(
                CleanLongText(normalized.Guardrails, 12000),
                "Do not bypass Mira review.");
            normalized.OutputFormatRules = CleanLongText(normalized.OutputFormatRules, 8000);
            if (normalized.UpdatedAtUtc == default)
            {
                normalized.UpdatedAtUtc = DateTime.UtcNow;
            }

            return normalized;
        }

        public static string ResolveRequestsFilePath(IWebHostEnvironment? env = null)
        {
            var candidates = new List<string>();
            var contentRoot = (env?.ContentRootPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(contentRoot))
            {
                candidates.Add(Path.Combine(contentRoot, "Requests", DefaultFileName));
            }

            var baseDir = AppContext.BaseDirectory;
            var current = new DirectoryInfo(baseDir);
            for (var i = 0; current != null && i < 8; i += 1)
            {
                candidates.Add(Path.Combine(current.FullName, "Requests", DefaultFileName));
                current = current.Parent;
            }

            candidates.Add(Path.Combine("E:\\ETDP\\ETDP", "Requests", DefaultFileName));
            candidates.Add(Path.Combine("C:\\ETDP\\ETDP", "Requests", DefaultFileName));
            candidates.Add(Path.Combine("F:\\ETDP\\ETDP", "Requests", DefaultFileName));

            foreach (var candidate in candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .First();
        }

        public static SmiCompareCompileRules Load(IWebHostEnvironment? env = null)
        {
            var path = ResolveRequestsFilePath(env);
            try
            {
                if (!File.Exists(path))
                {
                    var baseline = Normalize(CreateDefault());
                    Save(baseline, env);
                    return baseline;
                }

                var json = File.ReadAllText(path);
                var rules = JsonSerializer.Deserialize<SmiCompareCompileRules>(json, JsonOptions);
                var normalized = Normalize(rules);
                var normalizedJson = JsonSerializer.Serialize(normalized, JsonOptions);
                if (!string.Equals(json.Trim(), normalizedJson.Trim(), StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                    File.WriteAllText(path, normalizedJson, Encoding.UTF8);
                }

                return normalized;
            }
            catch
            {
                return Normalize(CreateDefault());
            }
        }

        public static SmiCompareCompileRules Save(SmiCompareCompileRules rules, IWebHostEnvironment? env = null)
        {
            var normalized = Normalize(rules);
            normalized.UpdatedAtUtc = DateTime.UtcNow;

            var path = ResolveRequestsFilePath(env);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(path, json, Encoding.UTF8);
            return normalized;
        }

        public static string BuildPromptBlock(SmiCompareCompileRules? rules)
        {
            var normalized = Normalize(rules);
            var sb = new StringBuilder();
            sb.AppendLine("Qwen role contract: compare, compile, parse, and optionally generate specialist ETDP support output without replacing Mira as the outward assistant or bypassing Mira review.");

            if (!string.IsNullOrWhiteSpace(normalized.Purpose))
            {
                sb.AppendLine($"- Purpose: {normalized.Purpose}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.CompareRules))
            {
                sb.AppendLine($"- Compare rules: {normalized.CompareRules}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.CompileRules))
            {
                sb.AppendLine($"- Compile rules: {normalized.CompileRules}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.ParseRules))
            {
                sb.AppendLine($"- Parse rules: {normalized.ParseRules}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.Guardrails))
            {
                sb.AppendLine($"- Guardrails: {normalized.Guardrails}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.OutputFormatRules))
            {
                sb.AppendLine($"- Output format: {normalized.OutputFormatRules}");
            }

            return sb.ToString().Trim();
        }

        private static string CleanLongText(string? value, int maxLength)
        {
            var normalized = (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            return normalized[..maxLength].TrimEnd();
        }

        private static string EnsureContains(string value, string requiredText)
        {
            var normalized = value.Trim();
            if (string.IsNullOrWhiteSpace(requiredText))
            {
                return normalized;
            }

            return normalized.IndexOf(requiredText, StringComparison.Ordinal) >= 0
                ? normalized
                : (string.IsNullOrWhiteSpace(normalized) ? requiredText : normalized + " " + requiredText);
        }
    }
}

namespace ETD.Api.Models
{
    public class SmiCompareCompileRules
    {
        public string Purpose { get; set; } = string.Empty;
        public string CompareRules { get; set; } = string.Empty;
        public string CompileRules { get; set; } = string.Empty;
        public string ParseRules { get; set; } = string.Empty;
        public string Guardrails { get; set; } = string.Empty;
        public string OutputFormatRules { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class SmiCompareCompileRulesRequest
    {
        public string? Purpose { get; set; }
        public string? CompareRules { get; set; }
        public string? CompileRules { get; set; }
        public string? ParseRules { get; set; }
        public string? Guardrails { get; set; }
        public string? OutputFormatRules { get; set; }
    }
}
