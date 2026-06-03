using System.Text;
using System.Text.Json;
using ETD.Api.Models;
using Microsoft.AspNetCore.Hosting;

namespace ETD.Api.Utils
{
    public static class MiraSmiRoleContractStore
    {
        public const string DefaultFileName = "mira-smi-role-contract.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static MiraSmiRoleContract CreateDefault()
        {
            return new MiraSmiRoleContract
            {
                MiraPrimaryRole = "Mira is the ETDP in-app call desk and helpdesk. She owns operator-facing guidance, route support, workflow clarification, and first-line assistance inside the app.",
                MiraReviewRole = "Mira must review every Qwen-created output before Pierre treats it as usable. To review properly, Mira must understand the same ETDP workflow, qualification scope, and terminology that Qwen used.",
                MiraReviewBoundaries = "Mira may detect contradictions, route drift, missing prerequisites, unsupported claims, and other mistakes in Qwen output, but Mira may not silently fix, rewrite, or conceal those mistakes. Mira must surface them clearly to Pierre.",
                SmiPrimaryRole = "Qwen compares, compiles, parses, and may generate specialist support output for ETDP when enabled through Alpha. Qwen does not replace Mira as the in-app helpdesk persona or operator-facing voice.",
                HandoffWorkflow = "Default flow: Pierre asks ETDP, Mira handles the helpdesk path, Qwen is used when deeper compare/compile/parse or specialist draft work is needed, Mira reviews the Qwen output, Mira reports findings to Pierre, and Pierre decides whether Codex or workflow changes are required.",
                FeedbackLoggingRules = "When Mira finds an issue in Qwen output, she must log a timestamped feedback entry with the source artifact, severity, summary, detailed finding, and recommended next action so Pierre can review it before asking for code or workflow changes.",
                OperatorVisibilityRules = "Mira review feedback must remain visible in the governance UI until Pierre has reviewed it. Review notes are advisory only and must not auto-apply corrections without Pierre's instruction.",
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        public static MiraSmiRoleContract Normalize(MiraSmiRoleContract? contract)
        {
            var normalized = contract ?? new MiraSmiRoleContract();
            normalized.MiraPrimaryRole = EnsureContains(
                CleanLongText(normalized.MiraPrimaryRole, 4000),
                "Mira is the ETDP in-app call desk and helpdesk.");
            normalized.MiraReviewRole = EnsureContains(
                CleanLongText(normalized.MiraReviewRole, 5000),
                "Mira must review every Qwen-created output before Pierre treats it as usable.");
            normalized.MiraReviewBoundaries = EnsureContains(
                CleanLongText(normalized.MiraReviewBoundaries, 5000),
                "Mira may not silently fix, rewrite, or conceal those mistakes.");
            normalized.SmiPrimaryRole = EnsureContains(
                CleanLongText(normalized.SmiPrimaryRole, 4000),
                "Qwen does not replace Mira as the in-app helpdesk persona or operator-facing voice.");
            normalized.HandoffWorkflow = CleanLongText(normalized.HandoffWorkflow, 6000);
            normalized.FeedbackLoggingRules = EnsureContains(
                CleanLongText(normalized.FeedbackLoggingRules, 5000),
                "Mira must log a timestamped feedback entry");
            normalized.OperatorVisibilityRules = CleanLongText(normalized.OperatorVisibilityRules, 5000);
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

        public static MiraSmiRoleContract Load(IWebHostEnvironment? env = null)
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
                var contract = JsonSerializer.Deserialize<MiraSmiRoleContract>(json, JsonOptions);
                var normalized = Normalize(contract);
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

        public static MiraSmiRoleContract Save(MiraSmiRoleContract contract, IWebHostEnvironment? env = null)
        {
            var normalized = Normalize(contract);
            normalized.UpdatedAtUtc = DateTime.UtcNow;

            var path = ResolveRequestsFilePath(env);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(path, json, Encoding.UTF8);
            return normalized;
        }

        public static string BuildPromptBlock(MiraSmiRoleContract? contract)
        {
            var normalized = Normalize(contract);
            var sb = new StringBuilder();
            sb.AppendLine("Mira/Qwen role contract:");

            if (!string.IsNullOrWhiteSpace(normalized.MiraPrimaryRole))
            {
                sb.AppendLine($"- Mira primary role: {normalized.MiraPrimaryRole}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.MiraReviewRole))
            {
                sb.AppendLine($"- Mira review role: {normalized.MiraReviewRole}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.MiraReviewBoundaries))
            {
                sb.AppendLine($"- Mira review boundaries: {normalized.MiraReviewBoundaries}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.SmiPrimaryRole))
            {
                sb.AppendLine($"- Qwen primary role: {normalized.SmiPrimaryRole}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.HandoffWorkflow))
            {
                sb.AppendLine($"- Handoff workflow: {normalized.HandoffWorkflow}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.FeedbackLoggingRules))
            {
                sb.AppendLine($"- Feedback logging: {normalized.FeedbackLoggingRules}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.OperatorVisibilityRules))
            {
                sb.AppendLine($"- Operator visibility: {normalized.OperatorVisibilityRules}");
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
    public class MiraSmiRoleContract
    {
        public string MiraPrimaryRole { get; set; } = string.Empty;
        public string MiraReviewRole { get; set; } = string.Empty;
        public string MiraReviewBoundaries { get; set; } = string.Empty;
        public string SmiPrimaryRole { get; set; } = string.Empty;
        public string HandoffWorkflow { get; set; } = string.Empty;
        public string FeedbackLoggingRules { get; set; } = string.Empty;
        public string OperatorVisibilityRules { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class MiraSmiRoleContractRequest
    {
        public string? MiraPrimaryRole { get; set; }
        public string? MiraReviewRole { get; set; }
        public string? MiraReviewBoundaries { get; set; }
        public string? SmiPrimaryRole { get; set; }
        public string? HandoffWorkflow { get; set; }
        public string? FeedbackLoggingRules { get; set; }
        public string? OperatorVisibilityRules { get; set; }
    }
}
