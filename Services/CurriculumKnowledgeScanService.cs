using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ETD.Api.Services
{
    public sealed class CurriculumKnowledgeScanService
    {
        public CognitiveScanArtifacts GenerateArtifacts(
            string sourcePath,
            string sourceExt,
            string extractedText,
            string qualificationCode,
            string outputDir,
            int? startPage = null)
        {
            var resolvedQualification = qualificationCode?.Trim() ?? string.Empty;
            var sourceBaseName = Path.GetFileNameWithoutExtension(sourcePath) ?? "curriculum";
            Directory.CreateDirectory(outputDir);

            var extractTextPath = Path.Combine(outputDir, $"{sourceBaseName}_knowledge_extract.txt");
            var phasesCsvPath = Path.Combine(outputDir, $"{sourceBaseName}_CurriculumPhases.csv");
            var subjectCsvPath = Path.Combine(outputDir, $"{sourceBaseName}_KnowledgeSubjects.csv");
            var topicCsvPath = Path.Combine(outputDir, $"{sourceBaseName}_KnowledgeTopics.csv");
            var reportJsonPath = Path.Combine(outputDir, $"{sourceBaseName}_KnowledgeScanReport.json");

            var cleanedText = CleanText(extractedText);
            File.WriteAllText(extractTextPath, cleanedText);

            var parseResult = CurriculumKnowledgeParser.Parse(cleanedText, resolvedQualification);
            var phaseRows = BuildPhaseRows(parseResult.Modules, parseResult.QualificationCode);

            WritePhasesCsv(phasesCsvPath, phaseRows);
            WriteSubjectsCsv(subjectCsvPath, parseResult.SubjectRows);
            WriteTopicsCsv(topicCsvPath, parseResult.TopicRows);

            var report = new
            {
                sourcePath,
                sourceExt,
                startPage,
                qualificationCode = parseResult.QualificationCode,
                scannedAtUtc = DateTime.UtcNow,
                moduleCount = parseResult.Modules.Count,
                curriculumPhaseCount = phaseRows.Count,
                knowledgeSubjectCount = parseResult.SubjectRows.Count,
                topicCount = parseResult.TopicRows.Count,
                warnings = parseResult.Warnings,
                outputs = new
                {
                    extractTextPath,
                    phasesCsvPath,
                    subjectCsvPath,
                    topicCsvPath
                }
            };
            File.WriteAllText(reportJsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

            return new CognitiveScanArtifacts
            {
                QualificationCode = parseResult.QualificationCode,
                ModuleCount = parseResult.Modules.Count,
                CurriculumPhaseCount = phaseRows.Count,
                KnowledgeSubjectCount = parseResult.SubjectRows.Count,
                TopicCount = parseResult.TopicRows.Count,
                Warnings = parseResult.Warnings,
                ExtractTextPath = extractTextPath,
                PhasesCsvPath = phasesCsvPath,
                SubjectCsvPath = subjectCsvPath,
                TopicCsvPath = topicCsvPath,
                ReportJsonPath = reportJsonPath
            };
        }

        private static string CleanText(string text)
        {
            var t = text ?? string.Empty;
            t = t.Replace("\r\n", "\n").Replace('\r', '\n');
            t = t.Replace("\u00A0", " ");
            t = Regex.Replace(t, @"[ \t]+", " ");
            t = Regex.Replace(t, @"\n{3,}", "\n\n");
            return t.Trim();
        }

        private static List<CurriculumPhaseCsvRow> BuildPhaseRows(IReadOnlyList<KnowledgeModule> modules, string qualificationCode)
        {
            var rows = new List<CurriculumPhaseCsvRow>();
            var ordered = modules
                .OrderBy(m => m.PhasesCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var module = ordered[i];
                var phaseCode = module.PhasesCode;
                if (string.IsNullOrWhiteSpace(phaseCode) && !string.IsNullOrWhiteSpace(module.KmToken))
                {
                    phaseCode = string.IsNullOrWhiteSpace(qualificationCode)
                        ? module.KmToken
                        : $"{qualificationCode}-{module.KmToken}";
                }

                rows.Add(new CurriculumPhaseCsvRow
                {
                    QualificationCode = string.IsNullOrWhiteSpace(qualificationCode)
                        ? module.QualificationCode
                        : qualificationCode,
                    LearningPhases = phaseCode,
                    PhasesCode = phaseCode,
                    PhasesDescription = module.PhasesDescription,
                    PhasesPurpose = module.PhasesPurpose,
                    Sequence = i + 1
                });
            }

            return rows;
        }

        private static void WritePhasesCsv(string path, IReadOnlyList<CurriculumPhaseCsvRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Qualification Code;Learning Phases;Phases Code;Phases Description;Phases Purpose;Sequence");
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(";", new[]
                {
                    Csv(row.QualificationCode),
                    Csv(row.LearningPhases),
                    Csv(row.PhasesCode),
                    Csv(row.PhasesDescription),
                    Csv(row.PhasesPurpose),
                    Csv(row.Sequence.ToString(CultureInfo.InvariantCulture))
                }));
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteSubjectsCsv(string path, IReadOnlyList<KnowledgeSubjectRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Qualification Code;Phases Code;Phases Description;Phases Purpose;SubjectCode;Subject Description;Subject Credits;Subject NQF Level;Subject Percentage");
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(";", new[]
                {
                    Csv(row.QualificationCode),
                    Csv(row.PhasesCode),
                    Csv(row.PhasesDescription),
                    Csv(row.PhasesPurpose),
                    Csv(row.SubjectCode),
                    Csv(row.SubjectDescription),
                    Csv(row.SubjectCredits),
                    Csv(row.SubjectNqfLevel),
                    Csv(row.SubjectPercentage)
                }));
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static void WriteTopicsCsv(string path, IReadOnlyList<KnowledgeTopicRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Qualification Code;Phases Code;Phases Description;Subject Code;Subject Credits;Notional Hours;Periods per Topic;Subject Decription;Topic Code;Topic Description;Assessment Criteria Number;Assesment Criteria Description;LPN;Lesson Plan Description ");
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(";", new[]
                {
                    Csv(row.QualificationCode),
                    Csv(row.PhasesCode),
                    Csv(row.PhasesDescription),
                    Csv(row.SubjectCode),
                    Csv(row.SubjectCredits),
                    Csv(row.NotionalHours),
                    Csv(row.PeriodsPerTopic),
                    Csv(row.SubjectDescription),
                    Csv(row.TopicCode),
                    Csv(row.TopicDescription),
                    Csv(row.AssessmentCriteriaNumber),
                    Csv(row.AssessmentCriteriaDescription),
                    Csv(row.Lpn),
                    Csv(row.LessonPlanDescription)
                }));
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static string Csv(string? value)
        {
            var v = value ?? string.Empty;
            if (!v.Contains(';') && !v.Contains('"') && !v.Contains('\n') && !v.Contains('\r')) return v;
            return $"\"{v.Replace("\"", "\"\"")}\"";
        }
    }

    public sealed class CognitiveScanArtifacts
    {
        public string QualificationCode { get; set; } = string.Empty;
        public int ModuleCount { get; set; }
        public int CurriculumPhaseCount { get; set; }
        public int KnowledgeSubjectCount { get; set; }
        public int TopicCount { get; set; }
        public List<string> Warnings { get; set; } = new();
        public string ExtractTextPath { get; set; } = string.Empty;
        public string PhasesCsvPath { get; set; } = string.Empty;
        public string SubjectCsvPath { get; set; } = string.Empty;
        public string TopicCsvPath { get; set; } = string.Empty;
        public string ReportJsonPath { get; set; } = string.Empty;
    }

    public sealed class CurriculumPhaseCsvRow
    {
        public string QualificationCode { get; set; } = string.Empty;
        public string LearningPhases { get; set; } = string.Empty;
        public string PhasesCode { get; set; } = string.Empty;
        public string PhasesDescription { get; set; } = string.Empty;
        public string PhasesPurpose { get; set; } = string.Empty;
        public int Sequence { get; set; }
    }
internal static class CurriculumKnowledgeParser
{
    private static readonly Regex ModuleHeaderRegex = new(
        @"^(?:\d+(?:\.\d+){0,5}\.?\s+)?(?<fullCode>\d{6,9}-KM-\d{2})\s*[:,-]?\s*(?<desc>.+?)(?=(?:,\s*NQF\s*Level\b)|$)(?:,\s*NQF\s*Level\s*(?<nqf>\d+)(?:\s*\((?<creditsPar>[\d.,]+)\)|\s*,\s*Credits?\s*(?<creditsCsv>[\d.,]+)))?.*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex KnowledgeSubjectHeaderRegex = new(
        @"^(?:\d+(?:\.\d+){0,6}\.?\s+)?(?<code>KM-\d{2}-KT\d{2})\s*[:,-]?\s*(?<desc>.+?)(?:\s*\((?<pct>[\d.,]+)\s*%\))?(?:\s+Topic elements to be covered include:)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TopicElementRegex = new(
        @"^(?:[•]\s*)?(?<code>[A-Z]{2}\d{4,6}[A-Z]?)\s+(?<desc>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex KmTokenRegex = new(@"^(?<km>KM-\d{2})-KT\d{2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static KnowledgeParseResult Parse(string cleanedText, string qualificationCodeFallback)
    {
        var lines = NormalizeLines(cleanedText);
        var warnings = new List<string>();
        var modules = new List<KnowledgeModule>();
        var moduleByToken = new Dictionary<string, KnowledgeModule>(StringComparer.OrdinalIgnoreCase);
        var orderBySubjectCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var inKnowledgeSection = false;
        var collectingPurpose = false;
        var collectingCriteria = false;

        KnowledgeModule? currentModule = null;
        KnowledgeSubject? currentSubject = null;
        string? globalPurpose = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var upper = line.ToUpperInvariant();

            if (upper.Contains("SECTION 3A") || upper.Contains("KNOWLEDGE SUBJECT SPECIFICATIONS") || upper.Contains("KNOWLEDGE LEARNING"))
            {
                inKnowledgeSection = true;
                collectingPurpose = false;
                collectingCriteria = false;
                continue;
            }

            if (!inKnowledgeSection && ModuleHeaderRegex.IsMatch(line))
            {
                inKnowledgeSection = true;
            }

            if (inKnowledgeSection && (upper.Contains("SECTION 3B") || upper.Contains("PRACTICAL SKILL MODULE")))
            {
                break;
            }

            if (IsKnowledgeSectionNoiseHeader(upper))
            {
                collectingPurpose = false;
                collectingCriteria = false;
                currentSubject = null;
                continue;
            }

            if (!inKnowledgeSection) continue;

            if (upper.Contains("PURPOSE OF THE KNOWLEDGE SUBJECT", StringComparison.Ordinal) ||
                upper.Contains("PURPOSE OF THE KNOWLEDGE MODULE", StringComparison.Ordinal))
            {
                collectingPurpose = true;
                collectingCriteria = false;
                currentSubject = null;
                continue;
            }

            var moduleMatch = ModuleHeaderRegex.Match(line);
            if (moduleMatch.Success)
            {
                collectingPurpose = false;
                collectingCriteria = false;
                currentSubject = null;

                var fullCode = moduleMatch.Groups["fullCode"].Value.Trim();
                var kmToken = ExtractKmTokenFromModuleCode(fullCode);
                var module = GetOrCreateModule(moduleByToken, modules, kmToken, fullCode, qualificationCodeFallback);
                module.PhasesDescription = CleanLabel(moduleMatch.Groups["desc"].Value);
                module.SubjectNqfLevel = moduleMatch.Groups["nqf"].Success
                    ? moduleMatch.Groups["nqf"].Value.Trim()
                    : module.SubjectNqfLevel;
                var creditsRaw = moduleMatch.Groups["creditsPar"].Success
                    ? moduleMatch.Groups["creditsPar"].Value
                    : moduleMatch.Groups["creditsCsv"].Value;
                module.ModuleCredits = !string.IsNullOrWhiteSpace(creditsRaw)
                    ? ParseDecimalString(creditsRaw)
                    : module.ModuleCredits;
                if (!string.IsNullOrWhiteSpace(globalPurpose) && string.IsNullOrWhiteSpace(module.PhasesPurpose))
                {
                    module.PhasesPurpose = globalPurpose!;
                }

                currentModule = module;
                continue;
            }

            if (collectingPurpose)
            {
                if (KnowledgeSubjectHeaderRegex.IsMatch(line) || line.Contains("Topic elements to be covered include", StringComparison.OrdinalIgnoreCase))
                {
                    collectingPurpose = false;
                }
                else
                {
                    if (currentModule != null)
                    {
                        currentModule.PhasesPurpose = AppendSentence(currentModule.PhasesPurpose, line);
                    }
                    else
                    {
                        globalPurpose = AppendSentence(globalPurpose, line);
                    }
                    continue;
                }
            }

            var subjectMatch = KnowledgeSubjectHeaderRegex.Match(line);
            if (subjectMatch.Success)
            {
                collectingCriteria = false;
                var subjectCode = subjectMatch.Groups["code"].Value.Trim().ToUpperInvariant();
                var subjectDescription = CleanLabel(subjectMatch.Groups["desc"].Value);
                var subjectPercentage = subjectMatch.Groups["pct"].Success
                    ? ParseDecimalString(subjectMatch.Groups["pct"].Value)
                    : null;

                var kmToken = ExtractKmTokenFromSubjectCode(subjectCode);
                var module = GetOrCreateModule(moduleByToken, modules, kmToken, BuildModuleCode(qualificationCodeFallback, kmToken), qualificationCodeFallback);
                if (!string.IsNullOrWhiteSpace(globalPurpose) && string.IsNullOrWhiteSpace(module.PhasesPurpose))
                {
                    module.PhasesPurpose = globalPurpose!;
                }

                if (!module.SubjectsByCode.TryGetValue(subjectCode, out var subject))
                {
                    subject = new KnowledgeSubject
                    {
                        SubjectCode = subjectCode,
                        SubjectDescription = subjectDescription,
                        SubjectPercentage = subjectPercentage
                    };
                    module.SubjectsByCode[subjectCode] = subject;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(subjectDescription))
                    {
                        subject.SubjectDescription = subjectDescription;
                    }
                    if (!string.IsNullOrWhiteSpace(subjectPercentage))
                    {
                        subject.SubjectPercentage = subjectPercentage;
                    }
                }

                currentModule = module;
                currentSubject = subject;
                continue;
            }

            if (line.Contains("Internal Assessment Criteria", StringComparison.OrdinalIgnoreCase))
            {
                collectingCriteria = currentSubject != null;
                continue;
            }

            if (line.Contains("Topic elements to be covered include", StringComparison.OrdinalIgnoreCase))
            {
                collectingCriteria = false;
                continue;
            }

            if (currentSubject != null)
            {
                var topicMatch = TopicElementRegex.Match(line);
                if (topicMatch.Success)
                {
                    var topicCode = topicMatch.Groups["code"].Value.Trim().ToUpperInvariant();
                    if (!topicCode.StartsWith("KM-", StringComparison.OrdinalIgnoreCase) &&
                        !topicCode.StartsWith("AK", StringComparison.OrdinalIgnoreCase) &&
                        !topicCode.StartsWith("PA", StringComparison.OrdinalIgnoreCase))
                    {
                        var topicDescription = CleanLabel(topicMatch.Groups["desc"].Value);
                        if (!currentSubject.Topics.Any(t => string.Equals(t.TopicCode, topicCode, StringComparison.OrdinalIgnoreCase)))
                        {
                            currentSubject.Topics.Add(new KnowledgeTopicElement
                            {
                                TopicCode = topicCode,
                                TopicDescription = topicDescription
                            });
                        }
                        collectingCriteria = false;
                        continue;
                    }
                }
            }

            if (collectingCriteria && currentSubject != null)
            {
                if (!currentSubject.AssessmentCriteria.Any(c => string.Equals(c, line, StringComparison.OrdinalIgnoreCase)))
                {
                    currentSubject.AssessmentCriteria.Add(line);
                }
            }
        }

        if (modules.Count == 0)
        {
            warnings.Add("No Knowledge Learning modules were parsed. Verify source formatting or OCR quality.");
        }

        if (!string.IsNullOrWhiteSpace(qualificationCodeFallback))
        {
            foreach (var module in modules)
            {
                if (!string.IsNullOrWhiteSpace(module.QualificationCode) &&
                    !string.Equals(module.QualificationCode, qualificationCodeFallback, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Qualification code mismatch in source ({module.QualificationCode}) for {module.KmToken}. Using override {qualificationCodeFallback}.");
                }
                module.QualificationCode = qualificationCodeFallback;
                module.PhasesCode = BuildModuleCode(qualificationCodeFallback, module.KmToken);
            }
        }

        var subjectRows = new List<KnowledgeSubjectRow>();
        var topicRows = new List<KnowledgeTopicRow>();
        var acCounter = 1;

        foreach (var module in modules)
        {
            foreach (var subject in module.SubjectsByCode.Values.OrderBy(s => s.SubjectCode, StringComparer.OrdinalIgnoreCase))
            {
                if (!orderBySubjectCode.ContainsKey(subject.SubjectCode))
                {
                    orderBySubjectCode[subject.SubjectCode] = orderBySubjectCode.Count + 1;
                }

                var subjectCredits = DeriveSubjectCredits(module.ModuleCredits, subject.SubjectPercentage);
                var notionalHours = DeriveNotionalHours(subjectCredits);

                subjectRows.Add(new KnowledgeSubjectRow
                {
                    QualificationCode = module.QualificationCode,
                    PhasesCode = module.PhasesCode,
                    PhasesDescription = module.PhasesDescription,
                    PhasesPurpose = module.PhasesPurpose,
                    SubjectCode = subject.SubjectCode,
                    SubjectDescription = subject.SubjectDescription,
                    SubjectCredits = subjectCredits ?? string.Empty,
                    SubjectNqfLevel = module.SubjectNqfLevel ?? string.Empty,
                    SubjectPercentage = subject.SubjectPercentage ?? string.Empty
                });

                if (subject.Topics.Count == 0)
                {
                    warnings.Add($"No topic elements found for {subject.SubjectCode}.");
                    continue;
                }

                var criteriaForTopic = BuildCriteriaMapping(subject.Topics.Count, subject.AssessmentCriteria);
                for (var i = 0; i < subject.Topics.Count; i++)
                {
                    var topic = subject.Topics[i];
                    var criteria = criteriaForTopic[i];

                    topicRows.Add(new KnowledgeTopicRow
                    {
                        QualificationCode = module.QualificationCode,
                        PhasesCode = module.PhasesCode,
                        PhasesDescription = module.PhasesDescription,
                        SubjectCode = subject.SubjectCode,
                        SubjectCredits = subjectCredits ?? string.Empty,
                        NotionalHours = notionalHours ?? string.Empty,
                        PeriodsPerTopic = string.Empty,
                        SubjectDescription = subject.SubjectDescription,
                        TopicCode = topic.TopicCode,
                        TopicDescription = topic.TopicDescription,
                        AssessmentCriteriaNumber = $"AC {acCounter++}",
                        AssessmentCriteriaDescription = criteria,
                        Lpn = string.Empty,
                        LessonPlanDescription = topic.TopicDescription
                    });
                }
            }
        }

        var resolvedQualificationCode = !string.IsNullOrWhiteSpace(qualificationCodeFallback)
            ? qualificationCodeFallback
            : modules.Select(m => m.QualificationCode).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

        return new KnowledgeParseResult
        {
            QualificationCode = resolvedQualificationCode,
            Modules = modules,
            SubjectRows = subjectRows,
            TopicRows = topicRows,
            Warnings = warnings
        };
    }

    private static List<string> NormalizeLines(string text)
    {
        var normalized = (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\u00A0", " ");
        var rawLines = normalized.Split('\n')
            .Select(SanitizeRawLine)
            .Where(l => !string.IsNullOrWhiteSpace(l) && !ShouldDiscardLine(l))
            .ToList();

        var merged = new List<string>();
        foreach (var line in rawLines)
        {
            if (merged.Count == 0)
            {
                merged.Add(line);
                continue;
            }

            if (string.Equals(merged[^1], line, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var prev = merged[^1];
            if (ShouldMergeLines(prev, line))
            {
                merged[^1] = $"{prev} {line}".Trim();
            }
            else
            {
                merged.Add(line);
            }
        }
        return merged;
    }

    private static bool ShouldMergeLines(string prev, string current)
    {
        if (string.IsNullOrWhiteSpace(prev) || string.IsNullOrWhiteSpace(current)) return false;
        if (ModuleHeaderRegex.IsMatch(current)) return false;
        if (KnowledgeSubjectHeaderRegex.IsMatch(current)) return false;
        if (TopicElementRegex.IsMatch(current)) return false;
        if (current.Contains("Internal Assessment Criteria", StringComparison.OrdinalIgnoreCase)) return false;
        if (current.Contains("SECTION ", StringComparison.OrdinalIgnoreCase)) return false;
        if (current.Contains("Topic elements to be covered include", StringComparison.OrdinalIgnoreCase)) return false;
        if (IsKnowledgeSectionNoiseHeader(current.ToUpperInvariant())) return false;
        if (prev.EndsWith(":", StringComparison.Ordinal)) return false;
        if (Regex.IsMatch(current, @"^[a-z(]")) return true;
        if (prev.EndsWith(",", StringComparison.Ordinal) || prev.EndsWith("-", StringComparison.Ordinal)) return true;
        return false;
    }

    private static string SanitizeRawLine(string? input)
    {
        var line = input ?? string.Empty;
        line = line.Replace("\u00A0", " ").Trim();
        line = Regex.Replace(line, @"\s+", " ");
        line = Regex.Replace(line, @"^[•\u2022\u25CF\u25E6]+\s*", string.Empty);
        return line.Trim();
    }

    private static bool ShouldDiscardLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        if (Regex.IsMatch(line, @"^\d{1,3}$")) return true; // likely page number
        if (line.Contains("TABLE OF CONTENTS", StringComparison.OrdinalIgnoreCase)) return true;
        if (Regex.IsMatch(line, @"\.{4,}\s*\d+\s*$")) return true; // TOC dotted leader
        return false;
    }

    private static bool IsKnowledgeSectionNoiseHeader(string upperLine)
    {
        return upperLine.Contains("PROVIDER ACCREDITATION REQUIREMENTS", StringComparison.Ordinal) ||
               upperLine.Contains("PHYSICAL REQUIREMENTS", StringComparison.Ordinal) ||
               upperLine.Contains("HUMAN RESOURCE REQUIREMENTS", StringComparison.Ordinal) ||
               upperLine.Contains("LEGAL REQUIREMENTS", StringComparison.Ordinal) ||
               upperLine.Contains("CRITICAL TOPICS TO BE ASSESSED EXTERNALLY", StringComparison.Ordinal) ||
               upperLine.Contains("EXEMPTIONS", StringComparison.Ordinal);
    }

    private static KnowledgeModule GetOrCreateModule(
        Dictionary<string, KnowledgeModule> moduleByToken,
        List<KnowledgeModule> modules,
        string kmToken,
        string fullCode,
        string qualificationCodeFallback)
    {
        if (!moduleByToken.TryGetValue(kmToken, out var module))
        {
            module = new KnowledgeModule
            {
                QualificationCode = ResolveQualificationCodeFromModule(fullCode, qualificationCodeFallback),
                PhasesCode = fullCode,
                KmToken = kmToken
            };
            moduleByToken[kmToken] = module;
            modules.Add(module);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(module.PhasesCode))
            {
                module.PhasesCode = fullCode;
            }
            if (string.IsNullOrWhiteSpace(module.QualificationCode))
            {
                module.QualificationCode = ResolveQualificationCodeFromModule(fullCode, qualificationCodeFallback);
            }
        }

        if (string.IsNullOrWhiteSpace(module.PhasesCode))
        {
            module.PhasesCode = BuildModuleCode(module.QualificationCode, kmToken);
        }

        return module;
    }

    private static string ResolveQualificationCodeFromModule(string fullCode, string fallback)
    {
        var m = Regex.Match(fullCode ?? string.Empty, @"^(?<q>\d{6,9})-KM-\d{2}$", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups["q"].Value;
        return fallback ?? string.Empty;
    }

    private static string ExtractKmTokenFromModuleCode(string moduleCode)
    {
        var m = Regex.Match(moduleCode ?? string.Empty, @"(?<km>KM-\d{2})$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["km"].Value.ToUpperInvariant() : "KM-00";
    }

    private static string ExtractKmTokenFromSubjectCode(string subjectCode)
    {
        var m = KmTokenRegex.Match(subjectCode ?? string.Empty);
        return m.Success ? m.Groups["km"].Value.ToUpperInvariant() : "KM-00";
    }

    private static string BuildModuleCode(string qualificationCode, string kmToken)
    {
        if (string.IsNullOrWhiteSpace(qualificationCode)) return kmToken;
        return $"{qualificationCode}-{kmToken}";
    }

    private static string CleanLabel(string value)
    {
        var v = (value ?? string.Empty).Trim();
        v = Regex.Replace(v, @"\s+", " ");
        v = Regex.Replace(v, @"\s*Topic elements to be covered include:\s*$", string.Empty, RegexOptions.IgnoreCase);
        return v.Trim();
    }

    private static string AppendSentence(string? current, string next)
    {
        var c = string.IsNullOrWhiteSpace(current) ? string.Empty : current.Trim();
        var n = string.IsNullOrWhiteSpace(next) ? string.Empty : next.Trim();
        if (string.IsNullOrWhiteSpace(n)) return c;
        if (string.IsNullOrWhiteSpace(c)) return n;
        if (c.Contains(n, StringComparison.OrdinalIgnoreCase)) return c;
        if (c.EndsWith(".", StringComparison.Ordinal) || c.EndsWith(":", StringComparison.Ordinal))
        {
            return $"{c} {n}".Trim();
        }
        return $"{c} {n}".Trim();
    }

    private static string? ParseDecimalString(string raw)
    {
        var value = ParseFlexibleNumber(raw);
        if (!value.HasValue) return null;
        return value.Value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static double? ParseFlexibleNumber(string? raw)
    {
        var txt = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(txt)) return null;

        txt = txt.Replace("%", string.Empty).Trim();
        txt = txt.Replace(" ", string.Empty);

        if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var direct))
        {
            return direct;
        }

        if (double.TryParse(txt.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var dotted))
        {
            return dotted;
        }

        if (double.TryParse(txt.Replace(".", ","), NumberStyles.Float, CultureInfo.GetCultureInfo("fr-FR"), out var comma))
        {
            return comma;
        }

        return null;
    }

    private static string? DeriveSubjectCredits(string? moduleCredits, string? subjectPct)
    {
        var mc = ParseFlexibleNumber(moduleCredits);
        var pct = ParseFlexibleNumber(subjectPct);
        if (!mc.HasValue || !pct.HasValue) return null;
        var credits = mc.Value * (pct.Value / 100d);
        return credits.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string? DeriveNotionalHours(string? subjectCredits)
    {
        var sc = ParseFlexibleNumber(subjectCredits);
        if (!sc.HasValue) return null;
        var hours = sc.Value * 10d;
        return hours.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static List<string> BuildCriteriaMapping(int topicCount, List<string> criteria)
    {
        var outList = new List<string>();
        var items = criteria
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Count == 0)
        {
            for (var i = 0; i < topicCount; i++) outList.Add(string.Empty);
            return outList;
        }

        if (items.Count == 1)
        {
            for (var i = 0; i < topicCount; i++) outList.Add(items[0]);
            return outList;
        }

        if (items.Count == topicCount)
        {
            return items;
        }

        var joined = string.Join(" | ", items);
        for (var i = 0; i < topicCount; i++) outList.Add(joined);
        return outList;
    }
}

internal sealed class KnowledgeModule
{
    public string QualificationCode { get; set; } = string.Empty;
    public string KmToken { get; set; } = string.Empty;
    public string PhasesCode { get; set; } = string.Empty;
    public string PhasesDescription { get; set; } = string.Empty;
    public string PhasesPurpose { get; set; } = string.Empty;
    public string? SubjectNqfLevel { get; set; }
    public string? ModuleCredits { get; set; }
    public Dictionary<string, KnowledgeSubject> SubjectsByCode { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class KnowledgeSubject
{
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectDescription { get; set; } = string.Empty;
    public string? SubjectPercentage { get; set; }
    public List<KnowledgeTopicElement> Topics { get; } = new();
    public List<string> AssessmentCriteria { get; } = new();
}

internal sealed class KnowledgeTopicElement
{
    public string TopicCode { get; set; } = string.Empty;
    public string TopicDescription { get; set; } = string.Empty;
}

internal sealed class KnowledgeParseResult
{
    public string QualificationCode { get; set; } = string.Empty;
    public List<KnowledgeModule> Modules { get; set; } = new();
    public List<KnowledgeSubjectRow> SubjectRows { get; set; } = new();
    public List<KnowledgeTopicRow> TopicRows { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

internal sealed class KnowledgeSubjectRow
{
    public string QualificationCode { get; set; } = string.Empty;
    public string PhasesCode { get; set; } = string.Empty;
    public string PhasesDescription { get; set; } = string.Empty;
    public string PhasesPurpose { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectDescription { get; set; } = string.Empty;
    public string SubjectCredits { get; set; } = string.Empty;
    public string SubjectNqfLevel { get; set; } = string.Empty;
    public string SubjectPercentage { get; set; } = string.Empty;
}

internal sealed class KnowledgeTopicRow
{
    public string QualificationCode { get; set; } = string.Empty;
    public string PhasesCode { get; set; } = string.Empty;
    public string PhasesDescription { get; set; } = string.Empty;
    public string SubjectCode { get; set; } = string.Empty;
    public string SubjectCredits { get; set; } = string.Empty;
    public string NotionalHours { get; set; } = string.Empty;
    public string PeriodsPerTopic { get; set; } = string.Empty;
    public string SubjectDescription { get; set; } = string.Empty;
    public string TopicCode { get; set; } = string.Empty;
    public string TopicDescription { get; set; } = string.Empty;
    public string AssessmentCriteriaNumber { get; set; } = string.Empty;
    public string AssessmentCriteriaDescription { get; set; } = string.Empty;
    public string Lpn { get; set; } = string.Empty;
    public string LessonPlanDescription { get; set; } = string.Empty;
}

}
