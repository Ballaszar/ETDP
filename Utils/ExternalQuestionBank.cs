using System.Text.RegularExpressions;
using ETD.Api.Models;

namespace ETD.Api.Utils
{
    public sealed class ExternalQuestionBankRow
    {
        public int QuestionNumber { get; init; }
        public string QualificationCode { get; init; } = string.Empty;
        public string Module { get; init; } = string.Empty;
        public string TopicCode { get; init; } = string.Empty;
        public string TopicName { get; init; } = string.Empty;
        public string AssessmentCriterion { get; init; } = string.Empty;
        public string QuestionType { get; init; } = string.Empty;
        public string Stem { get; init; } = string.Empty;
        public string OptionA { get; init; } = string.Empty;
        public string OptionB { get; init; } = string.Empty;
        public string OptionC { get; init; } = string.Empty;
        public string OptionD { get; init; } = string.Empty;
        public string OptionE { get; init; } = string.Empty;
        public string CorrectAnswer { get; init; } = string.Empty;
        public int Marks { get; init; } = 1;
        public string BloomLevel { get; init; } = string.Empty;

        public bool IsTrueFalse => ExternalQuestionBank.IsTrueFalseType(QuestionType);

        public List<string> BuildOptions()
        {
            var options = new List<string>();
            AddOption(options, OptionA);
            AddOption(options, OptionB);
            AddOption(options, OptionC);
            AddOption(options, OptionD);
            AddOption(options, OptionE);
            return options;
        }

        private static void AddOption(List<string> list, string? option)
        {
            var cleaned = (option ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleaned)) return;
            list.Add(cleaned);
        }
    }

    public static class ExternalQuestionBank
    {
        private static readonly string[] DefaultRelativePaths =
        {
            Path.Combine("Requests", "Workbook-and-Memorand", "Workbook Question.csv"),
            Path.Combine("Requests", "Workbook-and-Memorand", "Workbook Qestions.csv")
        };

        public static bool TryLoad(string? contentRootPath, out List<ExternalQuestionBankRow> rows, out string sourcePath)
        {
            rows = new List<ExternalQuestionBankRow>();
            sourcePath = string.Empty;

            foreach (var candidate in BuildCandidatePaths(contentRootPath))
            {
                if (!File.Exists(candidate)) continue;

                var loaded = LoadFromCsv(candidate);
                if (loaded.Count == 0) continue;

                rows = loaded;
                sourcePath = candidate;
                return true;
            }

            return false;
        }

        public static List<ExternalQuestionBankRow> FilterForScope(
            IEnumerable<ExternalQuestionBankRow> rows,
            string? qualificationCode,
            string? subjectCode)
        {
            var all = rows
                .OrderBy(r => r.QuestionNumber <= 0 ? int.MaxValue : r.QuestionNumber)
                .ThenBy(r => NormalizeCode(r.TopicCode))
                .ThenBy(r => NormalizeCode(r.AssessmentCriterion))
                .ToList();

            if (all.Count == 0) return all;

            var normalizedQualification = NormalizeIdentifier(qualificationCode);
            if (string.IsNullOrWhiteSpace(normalizedQualification))
            {
                return new List<ExternalQuestionBankRow>();
            }

            var qualificationScoped = all
                .Where(r => string.Equals(NormalizeIdentifier(r.QualificationCode), normalizedQualification, StringComparison.Ordinal))
                .ToList();

            if (qualificationScoped.Count == 0)
            {
                return new List<ExternalQuestionBankRow>();
            }

            var normalizedSubject = NormalizeCode(subjectCode);
            var moduleMatch = Regex.Match(normalizedSubject, @"KM-\d+");
            var topicMatch = Regex.Match(normalizedSubject, @"KT\d+");

            if (!moduleMatch.Success && !topicMatch.Success)
            {
                return new List<ExternalQuestionBankRow>();
            }

            if (moduleMatch.Success && topicMatch.Success)
            {
                var module = moduleMatch.Value;
                var topicPrefix = topicMatch.Value;
                var strict = qualificationScoped
                    .Where(r =>
                        string.Equals(NormalizeCode(r.Module), module, StringComparison.Ordinal) &&
                        NormalizeCode(r.TopicCode).StartsWith(topicPrefix, StringComparison.Ordinal))
                    .ToList();
                if (strict.Count > 0) return strict;
            }

            if (topicMatch.Success)
            {
                var topicPrefix = topicMatch.Value;
                var byTopicPrefix = qualificationScoped
                    .Where(r => NormalizeCode(r.TopicCode).StartsWith(topicPrefix, StringComparison.Ordinal))
                    .ToList();
                if (byTopicPrefix.Count > 0) return byTopicPrefix;
            }

            if (moduleMatch.Success)
            {
                var module = moduleMatch.Value;
                var byModule = qualificationScoped
                    .Where(r => string.Equals(NormalizeCode(r.Module), module, StringComparison.Ordinal))
                    .ToList();
                if (byModule.Count > 0) return byModule;
            }

            return new List<ExternalQuestionBankRow>();
        }

        public static AssessmentDrivenQuestionGenerator.GeneratedQuestion ToGeneratedQuestion(
            ExternalQuestionBankRow row,
            int number,
            int? overrideMarks = null)
        {
            var options = row.BuildOptions();
            if (row.IsTrueFalse)
            {
                if (options.Count == 0) options = new List<string> { "True", "False" };
                if (options.Count == 1) options.Add(options[0].Equals("True", StringComparison.OrdinalIgnoreCase) ? "False" : "True");
            }
            else
            {
                if (options.Count == 0)
                {
                    options = new List<string>
                    {
                        "Not enough options were provided in the question bank.",
                        "Option not provided.",
                        "Option not provided.",
                        "Option not provided."
                    };
                }
            }

            var marks = overrideMarks ?? Math.Max(1, row.Marks);
            var n = number > 0 ? number : (row.QuestionNumber > 0 ? row.QuestionNumber : 1);

            return new AssessmentDrivenQuestionGenerator.GeneratedQuestion
            {
                Number = n,
                Type = row.IsTrueFalse ? "TrueFalse" : "MultipleChoice",
                Prompt = (row.Stem ?? string.Empty).Trim(),
                Options = options,
                CorrectAnswer = BuildCorrectAnswer(row, options),
                TopicCode = (row.TopicCode ?? string.Empty).Trim(),
                TopicDescription = (row.TopicName ?? string.Empty).Trim(),
                LessonPlanLabel = $"Q{row.QualificationCode} | Module {row.Module}".Trim(),
                AssessmentCriteriaDescription = (row.AssessmentCriterion ?? string.Empty).Trim(),
                Rationale = "Imported from external workbook question bank.",
                Marks = marks,
                BundleKey = $"{NormalizeCode(row.TopicCode)}:{n}"
            };
        }

        public static AssessmentDrivenQuestionGenerator.LessonEvidenceItem ToLessonEvidenceItem(ExternalQuestionBankRow row)
        {
            var stem = (row.Stem ?? string.Empty).Trim();
            var topicCode = (row.TopicCode ?? string.Empty).Trim();
            var topicName = (row.TopicName ?? string.Empty).Trim();
            var criterion = (row.AssessmentCriterion ?? string.Empty).Trim();

            return new AssessmentDrivenQuestionGenerator.LessonEvidenceItem
            {
                TopicId = 0,
                TopicCode = topicCode,
                TopicDescription = topicName,
                AssessmentCriteriaId = 0,
                AssessmentCriteriaDescription = criterion,
                LessonPlanLabel = $"Q{row.QualificationCode} | Module {row.Module}".Trim(),
                LessonPlanDescription = $"Question bank item {Math.Max(1, row.QuestionNumber)}",
                LessonPlanContent = stem,
                EvidenceText = stem,
                TopicOrder = ParseTopicOrder(topicCode),
                LessonSortOrder = Math.Max(1, row.QuestionNumber),
                BundleKey = $"{NormalizeCode(topicCode)}:{Math.Max(1, row.QuestionNumber)}"
            };
        }

        public static bool IsTrueFalseType(string? questionType)
        {
            var v = (questionType ?? string.Empty).Trim().ToUpperInvariant();
            return v == "TF" || v == "TRUEFALSE" || v == "TRUE_FALSE" || v == "TRUE/FALSE";
        }

        private static List<string> BuildCandidatePaths(string? contentRootPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();

            void AddPath(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                var full = Path.GetFullPath(path);
                if (!set.Add(full)) return;
                list.Add(full);
            }

            var roots = new List<string>();
            if (!string.IsNullOrWhiteSpace(contentRootPath)) roots.Add(contentRootPath!);
            roots.Add(Directory.GetCurrentDirectory());
            var assemblyDir = Path.GetDirectoryName(typeof(ExternalQuestionBank).Assembly.Location);
            if (!string.IsNullOrWhiteSpace(assemblyDir)) roots.Add(assemblyDir!);

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var rel in DefaultRelativePaths)
                {
                    AddPath(Path.Combine(root, rel));
                }
            }

            AddPath(EtdpPaths.CombineProject("Requests", "Workbook-and-Memorand", "Workbook Question.csv"));
            AddPath(EtdpPaths.CombineProject("Requests", "Workbook-and-Memorand", "Workbook Qestions.csv"));

            return list;
        }

        private static List<ExternalQuestionBankRow> LoadFromCsv(string path)
        {
            var rows = Csv.ReadPipeCsv(path);
            if (rows.Count == 0) return new List<ExternalQuestionBankRow>();

            var header = rows[0]
                .Select(NormalizeHeader)
                .ToList();

            var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(header[i])) continue;
                if (!indexByName.ContainsKey(header[i])) indexByName[header[i]] = i;
            }

            string GetValue(string[] row, string name)
            {
                if (!indexByName.TryGetValue(name, out var idx)) return string.Empty;
                if (idx < 0 || idx >= row.Length) return string.Empty;
                return CleanCell(row[idx]);
            }

            var result = new List<ExternalQuestionBankRow>();
            for (var i = 1; i < rows.Count; i++)
            {
                var raw = rows[i];
                if (raw.Length == 0) continue;

                var stem = GetValue(raw, "stem");
                if (string.IsNullOrWhiteSpace(stem)) continue;

                var questionNumber = ParseInt(GetValue(raw, "questionnumber"));
                var marks = ParseInt(GetValue(raw, "marks"));
                if (marks <= 0) marks = 1;

                var qualificationCode = GetFirstValue(raw, "qualificationcode", "qualificationnumber");
                if (string.IsNullOrWhiteSpace(qualificationCode))
                {
                    // Strict tenant separation: rows without qualification code are ignored.
                    continue;
                }

                result.Add(new ExternalQuestionBankRow
                {
                    QuestionNumber = questionNumber,
                    QualificationCode = qualificationCode,
                    Module = GetValue(raw, "module"),
                    TopicCode = GetValue(raw, "topiccode"),
                    TopicName = GetValue(raw, "topicname"),
                    AssessmentCriterion = GetValue(raw, "assessmentcriterion"),
                    QuestionType = GetValue(raw, "questiontype"),
                    Stem = stem,
                    OptionA = GetValue(raw, "optiona"),
                    OptionB = GetValue(raw, "optionb"),
                    OptionC = GetValue(raw, "optionc"),
                    OptionD = GetValue(raw, "optiond"),
                    OptionE = GetValue(raw, "optione"),
                    CorrectAnswer = GetValue(raw, "correctanswer"),
                    Marks = marks,
                    BloomLevel = GetValue(raw, "bloomlevel")
                });
            }

            return result;

            string GetFirstValue(string[] row, params string[] names)
            {
                foreach (var name in names)
                {
                    var value = GetValue(row, name);
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
                return string.Empty;
            }
        }

        private static string BuildCorrectAnswer(ExternalQuestionBankRow row, IReadOnlyList<string> options)
        {
            var label = ExtractOptionLabel(row.CorrectAnswer);
            if (row.IsTrueFalse && options.Count >= 2 && IsBooleanPair(options))
            {
                if (label == "B") return "A=False; B=True";
                return "A=True; B=False";
            }

            if (!string.IsNullOrWhiteSpace(label)) return label;
            return "A";
        }

        private static bool IsBooleanPair(IReadOnlyList<string> options)
        {
            if (options.Count < 2) return false;
            var a = options[0].Trim();
            var b = options[1].Trim();
            return (a.Equals("True", StringComparison.OrdinalIgnoreCase) &&
                    b.Equals("False", StringComparison.OrdinalIgnoreCase))
                   ||
                   (a.Equals("False", StringComparison.OrdinalIgnoreCase) &&
                    b.Equals("True", StringComparison.OrdinalIgnoreCase));
        }

        private static string ExtractOptionLabel(string? raw)
        {
            var text = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var c = text[0];
            if (c >= 'a' && c <= 'z') c = char.ToUpperInvariant(c);
            if (c < 'A' || c > 'Z') return string.Empty;
            return c.ToString();
        }

        private static int ParseTopicOrder(string? topicCode)
        {
            var normalized = NormalizeCode(topicCode);
            var match = Regex.Match(normalized, @"KT(\d+)");
            if (!match.Success) return int.MaxValue - 1;

            var digits = match.Groups[1].Value;
            if (digits.Length >= 2)
            {
                digits = digits.Substring(0, 2);
            }
            if (int.TryParse(digits, out var parsed)) return parsed;
            return int.MaxValue - 1;
        }

        private static int ParseInt(string? raw)
            => int.TryParse((raw ?? string.Empty).Trim(), out var v) ? v : 0;

        private static string NormalizeHeader(string? value)
        {
            var cleaned = (value ?? string.Empty).Trim().TrimStart('\uFEFF');
            return Regex.Replace(cleaned, @"[\s_\-]+", string.Empty).ToLowerInvariant();
        }

        private static string CleanCell(string? value)
            => (value ?? string.Empty).Trim().Trim('\uFEFF');

        private static string NormalizeCode(string? value)
            => Regex.Replace((value ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", string.Empty);

        private static string NormalizeIdentifier(string? value)
            => Regex.Replace((value ?? string.Empty).Trim().ToUpperInvariant(), @"[^A-Z0-9]+", string.Empty);
    }
}
