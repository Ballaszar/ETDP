using System.Globalization;
using System.Text.RegularExpressions;

namespace ETD.Api.Services
{
    internal static class CurriculumWorkExperienceParser
    {
        private enum WorkCaptureMode
        {
            None,
            Activities,
            Evidence
        }

        private static readonly Regex ModuleHeaderRegex = new(
            @"^(?:\d+(?:\.\d+){0,5}\.?\s+)?(?:[•]\s*)?(?<fullCode>(?<qual>\d{6,9}(?:-\d{3})?(?:-\d{2})?)\s*,?\s*-?\s*(?<wm>WM-?\d{2}))\s*[:,-]?\s*(?<desc>.+?)(?=(?:,\s*NQF\s*Level\b)|$)(?:,\s*NQF\s*Level\s*(?<nqf>\d+)(?:\s*,\s*Credits?\s*(?<credits>[\d.,]+)))?.*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SubjectHeaderRegex = new(
            @"^(?:\d+(?:\.\d+){0,6}\.?\s+)?(?:[•]\s*)?(?<code>WM-\d{2}-WE\d{2})\s*[:,-]?\s*(?<desc>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ActivityRegex = new(
            @"^(?:[•]\s*)?(?<code>WA\d{4,6}[A-Z]?)\s+(?<desc>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex EvidenceRegex = new(
            @"^(?:[•]\s*)?(?<code>SE\d{4,6}[A-Z]?)\s+(?<desc>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex WmTokenRegex = new(@"^(?<wm>WM-?\d{2})-WE\d{2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MinimumHoursRegex = new(@"minimum of\s+(?<hours>[\d.,]+)\s+hours", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static KnowledgeParseResult Parse(string cleanedText, string qualificationCodeFallback)
        {
            var lines = CurriculumKnowledgeParser.NormalizeLines(cleanedText);
            var warnings = new List<string>();
            var modules = new List<KnowledgeModule>();
            var moduleByToken = new Dictionary<string, KnowledgeModule>(StringComparer.OrdinalIgnoreCase);

            var inSection = false;
            var collectingPurpose = false;
            var captureMode = WorkCaptureMode.None;

            KnowledgeModule? currentModule = null;
            KnowledgeSubject? currentSubject = null;
            string? globalPurpose = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var upper = line.ToUpperInvariant();

                if (IsWorkSectionStart(upper))
                {
                    inSection = true;
                    collectingPurpose = false;
                    captureMode = WorkCaptureMode.None;
                    currentModule = null;
                    currentSubject = null;
                    continue;
                }

                if (inSection && IsWorkSectionEnd(upper))
                {
                    break;
                }

                if (!inSection) continue;

                if (IsWorkSectionNoiseHeader(upper))
                {
                    collectingPurpose = false;
                    captureMode = WorkCaptureMode.None;
                    currentSubject = null;
                    continue;
                }

                if (upper.Contains("PURPOSE OF THE WORK EXPERIENCE MODULE", StringComparison.Ordinal))
                {
                    collectingPurpose = true;
                    captureMode = WorkCaptureMode.None;
                    currentSubject = null;
                    continue;
                }

                if (collectingPurpose)
                {
                    if (upper.Contains("THE LEARNER WILL BE REQUIRED TO", StringComparison.Ordinal))
                    {
                        collectingPurpose = false;
                        captureMode = WorkCaptureMode.None;
                        currentSubject = null;
                        continue;
                    }

                    if (!ModuleHeaderRegex.IsMatch(line) && !SubjectHeaderRegex.IsMatch(line) &&
                        !upper.Contains("GUIDELINES FOR WORK EXPERIENCE", StringComparison.Ordinal))
                    {
                        if (currentModule != null)
                        {
                            currentModule.PhasesPurpose = CurriculumKnowledgeParser.AppendSentence(currentModule.PhasesPurpose, line);
                        }
                        else
                        {
                            globalPurpose = CurriculumKnowledgeParser.AppendSentence(globalPurpose, line);
                        }
                        continue;
                    }

                    collectingPurpose = false;
                }

                if (upper.Contains("GUIDELINES FOR WORK EXPERIENCE", StringComparison.Ordinal))
                {
                    captureMode = WorkCaptureMode.None;
                    currentSubject = null;
                    continue;
                }

                var moduleMatch = ModuleHeaderRegex.Match(line);
                if (moduleMatch.Success)
                {
                    captureMode = WorkCaptureMode.None;
                    currentSubject = null;

                    var fullCode = NormalizeModuleHeaderCode(moduleMatch.Groups["fullCode"].Value);
                    var wmToken = ExtractWmTokenFromModuleCode(fullCode);
                    var module = GetOrCreateModule(moduleByToken, modules, wmToken, fullCode, qualificationCodeFallback);
                    module.PhasesDescription = CurriculumKnowledgeParser.CleanLabel(moduleMatch.Groups["desc"].Value);
                    module.SubjectNqfLevel = moduleMatch.Groups["nqf"].Success
                        ? moduleMatch.Groups["nqf"].Value.Trim()
                        : module.SubjectNqfLevel;
                    module.ModuleCredits = moduleMatch.Groups["credits"].Success
                        ? CurriculumKnowledgeParser.ParseDecimalString(moduleMatch.Groups["credits"].Value)
                        : module.ModuleCredits;
                    if (!string.IsNullOrWhiteSpace(globalPurpose) && string.IsNullOrWhiteSpace(module.PhasesPurpose))
                    {
                        module.PhasesPurpose = globalPurpose!;
                    }

                    currentModule = module;
                    continue;
                }

                var subjectMatch = SubjectHeaderRegex.Match(line);
                if (subjectMatch.Success)
                {
                    captureMode = WorkCaptureMode.None;
                    var subjectCode = subjectMatch.Groups["code"].Value.Trim().ToUpperInvariant();
                    var subjectDescription = CurriculumKnowledgeParser.CleanLabel(subjectMatch.Groups["desc"].Value);
                    var subjectHours = ExtractMinimumHours(subjectDescription);

                    var wmToken = ExtractWmTokenFromSubjectCode(subjectCode);
                    var module = currentModule != null && string.Equals(currentModule.KmToken, wmToken, StringComparison.OrdinalIgnoreCase)
                        ? currentModule
                        : GetOrCreateModule(moduleByToken, modules, wmToken, BuildModuleCode(qualificationCodeFallback, wmToken), qualificationCodeFallback);
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
                            SubjectCredits = subjectHours.HasValue ? (subjectHours.Value / 10d).ToString("0.##", CultureInfo.InvariantCulture) : null,
                            NotionalHours = subjectHours.HasValue ? subjectHours.Value.ToString("0.##", CultureInfo.InvariantCulture) : null
                        };
                        module.SubjectsByCode[subjectCode] = subject;
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(subjectDescription))
                        {
                            subject.SubjectDescription = subjectDescription;
                        }
                        if (subjectHours.HasValue)
                        {
                            subject.SubjectCredits = (subjectHours.Value / 10d).ToString("0.##", CultureInfo.InvariantCulture);
                            subject.NotionalHours = subjectHours.Value.ToString("0.##", CultureInfo.InvariantCulture);
                        }
                    }

                    currentModule = module;
                    currentSubject = subject;
                    continue;
                }

                if (upper.Contains("SCOPE OF WORK EXPERIENCE", StringComparison.Ordinal))
                {
                    captureMode = WorkCaptureMode.Activities;
                    continue;
                }

                if (upper.Contains("SUPPORTING EVIDENCE", StringComparison.Ordinal))
                {
                    captureMode = WorkCaptureMode.Evidence;
                    continue;
                }

                if (currentSubject == null) continue;

                if (captureMode == WorkCaptureMode.Activities)
                {
                    var activityMatch = ActivityRegex.Match(line);
                    if (activityMatch.Success)
                    {
                        var topicCode = activityMatch.Groups["code"].Value.Trim().ToUpperInvariant();
                        var topicDescription = CurriculumKnowledgeParser.CleanLabel(activityMatch.Groups["desc"].Value);
                        if (!currentSubject.Topics.Any(t => string.Equals(t.TopicCode, topicCode, StringComparison.OrdinalIgnoreCase)))
                        {
                            currentSubject.Topics.Add(new KnowledgeTopicElement
                            {
                                TopicCode = topicCode,
                                TopicDescription = topicDescription
                            });
                        }
                        continue;
                    }
                }

                if (captureMode == WorkCaptureMode.Evidence)
                {
                    var evidenceMatch = EvidenceRegex.Match(line);
                    if (evidenceMatch.Success)
                    {
                        var evidence = $"{evidenceMatch.Groups["code"].Value.Trim().ToUpperInvariant()} {CurriculumKnowledgeParser.CleanLabel(evidenceMatch.Groups["desc"].Value)}";
                        if (!currentSubject.AssessmentCriteria.Any(c => string.Equals(c, evidence, StringComparison.OrdinalIgnoreCase)))
                        {
                            currentSubject.AssessmentCriteria.Add(evidence);
                        }
                    }
                }
            }

            if (modules.Count == 0)
            {
                return new KnowledgeParseResult
                {
                    QualificationCode = qualificationCodeFallback ?? string.Empty,
                    Modules = modules,
                    SubjectRows = new List<KnowledgeSubjectRow>(),
                    TopicRows = new List<KnowledgeTopicRow>(),
                    Warnings = warnings
                };
            }

            if (!string.IsNullOrWhiteSpace(qualificationCodeFallback))
            {
                foreach (var module in modules)
                {
                    if (!string.IsNullOrWhiteSpace(module.QualificationCode) &&
                        !AreEquivalentQualificationCodes(module.QualificationCode, qualificationCodeFallback))
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

            foreach (var module in modules.OrderBy(m => m.PhasesCode, StringComparer.OrdinalIgnoreCase))
            {
                var orderedSubjects = module.SubjectsByCode.Values
                    .OrderBy(s => s.SubjectCode, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var defaultCredits = DeriveEqualSubjectCredits(module.ModuleCredits, orderedSubjects.Count);

                foreach (var subject in orderedSubjects)
                {
                    var subjectCredits = subject.SubjectCredits ?? defaultCredits;
                    var notionalHours = subject.NotionalHours ?? CurriculumKnowledgeParser.DeriveNotionalHours(subjectCredits);

                    subjectRows.Add(new KnowledgeSubjectRow
                    {
                        QualificationCode = module.QualificationCode,
                        PhasesCode = module.PhasesCode,
                        PhasesDescription = module.PhasesDescription,
                        PhasesPurpose = module.PhasesPurpose,
                        SubjectCode = subject.SubjectCode,
                        SubjectDescription = subject.SubjectDescription,
                        SubjectCredits = subjectCredits ?? string.Empty,
                        SubjectNqfLevel = subject.SubjectNqfLevel ?? module.SubjectNqfLevel ?? string.Empty,
                        SubjectPercentage = subject.SubjectPercentage ?? string.Empty
                    });

                    if (subject.Topics.Count == 0)
                    {
                        warnings.Add($"No work activity elements found for {subject.SubjectCode}.");
                        continue;
                    }

                    var evidenceForTopic = CurriculumKnowledgeParser.BuildCriteriaMapping(subject.Topics.Count, subject.AssessmentCriteria);
                    for (var i = 0; i < subject.Topics.Count; i++)
                    {
                        var topic = subject.Topics[i];
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
                            AssessmentCriteriaDescription = evidenceForTopic[i],
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

        private static bool IsWorkSectionStart(string upperLine)
        {
            return upperLine.Contains("SECTION 3C", StringComparison.Ordinal) ||
                   upperLine.Contains("WORK EXPERIENCE MODULE SPECIFICATIONS", StringComparison.Ordinal);
        }

        private static bool IsWorkSectionEnd(string upperLine)
        {
            return upperLine.StartsWith("SECTION 4", StringComparison.Ordinal) ||
                   upperLine.Contains("EXTERNAL ASSESSMENT SPECIFICATIONS", StringComparison.Ordinal) ||
                   upperLine.Contains("ASSESSMENT SPECIFICATIONS", StringComparison.Ordinal);
        }

        private static bool IsWorkSectionNoiseHeader(string upperLine)
        {
            return upperLine.Contains("CONTEXTUALISED WORKPLACE KNOWLEDGE", StringComparison.Ordinal) ||
                   upperLine.Contains("CRITERIA FOR WORKPLACE APPROVAL", StringComparison.Ordinal) ||
                   upperLine.Contains("PHYSICAL REQUIREMENTS", StringComparison.Ordinal) ||
                   upperLine.Contains("HUMAN RESOURCE REQUIREMENTS", StringComparison.Ordinal) ||
                   upperLine.Contains("LEGAL REQUIREMENTS", StringComparison.Ordinal) ||
                   upperLine.Contains("ADDITIONAL ASSIGNMENTS TO BE ASSESSED EXTERNALLY", StringComparison.Ordinal);
        }

        private static KnowledgeModule GetOrCreateModule(
            Dictionary<string, KnowledgeModule> moduleByToken,
            List<KnowledgeModule> modules,
            string wmToken,
            string fullCode,
            string qualificationCodeFallback)
        {
            if (!moduleByToken.TryGetValue(wmToken, out var module))
            {
                module = new KnowledgeModule
                {
                    QualificationCode = ResolveQualificationCodeFromModule(fullCode, qualificationCodeFallback),
                    PhasesCode = fullCode,
                    KmToken = wmToken
                };
                moduleByToken[wmToken] = module;
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
                module.PhasesCode = BuildModuleCode(module.QualificationCode, wmToken);
            }

            return module;
        }

        private static string ResolveQualificationCodeFromModule(string fullCode, string fallback)
        {
            var normalized = NormalizeModuleHeaderCode(fullCode);
            var m = Regex.Match(normalized ?? string.Empty, @"^(?<q>\d{6,9})(?:-\d{3})?(?:-\d{2})?-WM-?\d{2}$", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups["q"].Value;
            return fallback ?? string.Empty;
        }

        private static string ExtractWmTokenFromModuleCode(string moduleCode)
        {
            var m = Regex.Match(moduleCode ?? string.Empty, @"(?<wm>WM-?\d{2})$", RegexOptions.IgnoreCase);
            return m.Success ? NormalizeWmToken(m.Groups["wm"].Value) : "WM-00";
        }

        private static string ExtractWmTokenFromSubjectCode(string subjectCode)
        {
            var m = WmTokenRegex.Match(subjectCode ?? string.Empty);
            return m.Success ? NormalizeWmToken(m.Groups["wm"].Value) : "WM-00";
        }

        private static string BuildModuleCode(string qualificationCode, string wmToken)
        {
            var normalizedWmToken = NormalizeWmToken(wmToken);
            if (string.IsNullOrWhiteSpace(qualificationCode)) return normalizedWmToken;
            return $"{qualificationCode}-{normalizedWmToken}";
        }

        private static string NormalizeModuleHeaderCode(string? raw)
        {
            var compact = Regex.Replace(raw ?? string.Empty, @"\s+", string.Empty)
                .Replace(",", string.Empty)
                .ToUpperInvariant();
            var m = Regex.Match(compact, @"^(?<qual>\d{6,9}(?:-\d{3})?(?:-\d{2})?)-?(?<wm>WM-?\d{2})$", RegexOptions.IgnoreCase);
            if (!m.Success) return compact;
            return $"{m.Groups["qual"].Value}-{NormalizeWmToken(m.Groups["wm"].Value)}";
        }

        private static string NormalizeWmToken(string? raw)
        {
            var m = Regex.Match(raw ?? string.Empty, @"WM-?(?<num>\d{2})", RegexOptions.IgnoreCase);
            return m.Success ? $"WM-{m.Groups["num"].Value}" : (raw ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool AreEquivalentQualificationCodes(string left, string right)
        {
            var a = Regex.Replace(left ?? string.Empty, @"\D", string.Empty);
            var b = Regex.Replace(right ?? string.Empty, @"\D", string.Empty);
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
                   a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
                   b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        private static double? ExtractMinimumHours(string description)
        {
            var m = MinimumHoursRegex.Match(description ?? string.Empty);
            if (!m.Success) return null;
            return CurriculumKnowledgeParser.ParseFlexibleNumber(m.Groups["hours"].Value);
        }

        private static string? DeriveEqualSubjectCredits(string? moduleCredits, int subjectCount)
        {
            if (subjectCount <= 0) return null;
            var credits = CurriculumKnowledgeParser.ParseFlexibleNumber(moduleCredits);
            if (!credits.HasValue || credits.Value <= 0) return null;
            return (credits.Value / subjectCount).ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
