using System.Text;
using System.Text.Json;
using ETD.Api.Models;
using Microsoft.AspNetCore.Hosting;

namespace ETD.Api.Utils
{
    public static class LearningMaterialAuthoringRulesStore
    {
        public const string DefaultFileName = "learning-material-authoring-rules.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static LearningMaterialAuthoringRules CreateDefault()
        {
            return new LearningMaterialAuthoringRules
            {
                DisableRigidLessonTemplate = true,
                SourceMaterialPriorityRules = "Use the actual uploaded subject matter, mapped topic scope, mapped assessment criteria, and the selected source book or core text that best represents the curriculum. Keep the curriculum as the skeleton and let the subject matter mirror that skeleton with detailed explanatory content only.",
                LearnerGuideRules = "Write learner guides for learners, not for lecturers. Address the learner directly as 'you'. Never write 'the learner must' or refer to shorthand criterion codes such as KT0101, AC01, KG01, or LPN numbers. Write the full assessment criteria when criteria need to be shown. Use curriculum subject, topic, and assessment criteria only as headers or section labels, not as filler sentences inside the lesson text. Explain the subject matter in full step-by-step detail so the learner can follow the guide without additional lecturer explanation. Do not add facilitator notes, presenter scripts, or fixed lecturer-style template headings or mandatory section blocks unless the operator explicitly asks for that format. Let each topic expand naturally according to the real subject matter instead of forcing preset section quotas. Keep the table of contents to one level only.",
                AssessmentRules = "When creating assessments or assessment support material, align directly to the mapped assessment criteria, write the criteria out in full, and make the required evidence explicit without relying on shorthand codes.",
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        public static LearningMaterialAuthoringRules Normalize(LearningMaterialAuthoringRules? rules)
        {
            var normalized = rules ?? new LearningMaterialAuthoringRules();
            normalized.SourceMaterialPriorityRules = CleanLongText(normalized.SourceMaterialPriorityRules, 6000);
            normalized.LearnerGuideRules = NormalizeLearnerGuideRules(CleanLongText(normalized.LearnerGuideRules, 12000));
            normalized.AssessmentRules = CleanLongText(normalized.AssessmentRules, 12000);
            if (normalized.UpdatedAtUtc == default)
            {
                normalized.UpdatedAtUtc = DateTime.UtcNow;
            }

            return normalized;
        }

        private static string NormalizeLearnerGuideRules(string value)
        {
            var normalized = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }

            normalized = normalized.Replace(
                "Do not add facilitator notes, presenter scripts, or fixed headings such as Overview, Core Technical Understanding, Procedure and Application, Safety and Quality Checks, Common Faults, Errors, or Summary unless the operator explicitly asks for that format.",
                "Do not add facilitator notes, presenter scripts, or fixed lecturer-style template headings or mandatory section blocks unless the operator explicitly asks for that format.",
                StringComparison.OrdinalIgnoreCase);

            normalized = normalized.Replace(
                "Keep the table of contents to two levels only when headings are produced.",
                "Keep the table of contents to one level only when headings are produced.",
                StringComparison.OrdinalIgnoreCase);

            normalized = normalized.Replace(
                "Keep the table of contents to two levels only.",
                "Keep the table of contents to one level only.",
                StringComparison.OrdinalIgnoreCase);

            if (!normalized.Contains("Let each topic expand naturally according to the real subject matter instead of forcing preset section quotas.", StringComparison.OrdinalIgnoreCase))
            {
                normalized = $"{normalized.Trim()} Let each topic expand naturally according to the real subject matter instead of forcing preset section quotas.";
            }

            return normalized.Trim();
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

        public static LearningMaterialAuthoringRules Load(IWebHostEnvironment? env = null)
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
                var rules = JsonSerializer.Deserialize<LearningMaterialAuthoringRules>(json, JsonOptions);
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

        public static LearningMaterialAuthoringRules Save(LearningMaterialAuthoringRules rules, IWebHostEnvironment? env = null)
        {
            var normalized = Normalize(rules);
            normalized.UpdatedAtUtc = DateTime.UtcNow;

            var path = ResolveRequestsFilePath(env);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(path, json, Encoding.UTF8);
            return normalized;
        }

        public static string BuildPromptBlock(LearningMaterialAuthoringRules? rules)
        {
            var normalized = Normalize(rules);
            var sb = new StringBuilder();
            sb.AppendLine("Apply these operator-defined learning-material rules whenever the user asks for learner guides, lesson content, assessments, memoranda, questionnaires, or related study material.");

            if (normalized.DisableRigidLessonTemplate)
            {
                sb.AppendLine("- Do not force fixed lecturer-style lesson headings, presenter notes, or mandatory subsection blocks unless the operator explicitly asks for them.");
                sb.AppendLine("- Let each topic expand naturally according to the actual subject matter instead of preset section quotas.");
                sb.AppendLine("- Keep the table of contents to one level only.");
            }

            if (!string.IsNullOrWhiteSpace(normalized.SourceMaterialPriorityRules))
            {
                sb.AppendLine($"- Source material priority: {normalized.SourceMaterialPriorityRules}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.LearnerGuideRules))
            {
                sb.AppendLine($"- Learner guide rules: {normalized.LearnerGuideRules}");
            }

            if (!string.IsNullOrWhiteSpace(normalized.AssessmentRules))
            {
                sb.AppendLine($"- Assessment rules: {normalized.AssessmentRules}");
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
    }
}

namespace ETD.Api.Models
{
    public class LearningMaterialAuthoringRules
    {
        public bool DisableRigidLessonTemplate { get; set; } = true;
        public string SourceMaterialPriorityRules { get; set; } = string.Empty;
        public string LearnerGuideRules { get; set; } = string.Empty;
        public string AssessmentRules { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class LearningMaterialAuthoringRulesRequest
    {
        public bool? DisableRigidLessonTemplate { get; set; }
        public string? SourceMaterialPriorityRules { get; set; }
        public string? LearnerGuideRules { get; set; }
        public string? AssessmentRules { get; set; }
    }
}
