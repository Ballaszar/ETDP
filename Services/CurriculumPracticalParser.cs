using System.Globalization;
using System.Text.RegularExpressions;

namespace ETD.Api.Services
{
    internal static class CurriculumPracticalParser
    {
        private enum PracticalCaptureMode
        {
            None,
            Scope,
            AppliedKnowledge,
            Criteria
        }

        private static readonly Regex ModuleHeaderRegex = new(
            @"^(?:\d+(?:\.\d+){0,5}\.?\s+)?(?:[•]\s*)?(?<fullCode>(?<qual>\d{6,9}(?:-\d{3})?(?:-\d{2})?)\s*,?\s*-?\s*(?<pm>PM-?\d{2}))\s*[:,-]?\s*(?<desc>.+?)(?=(?:,\s*NQF\s*Level\b)|$)(?:,\s*NQF\s*Level\s*(?<nqf>\d+)(?:\s*,\s*Credits?\s*(?<credits>[\d.,]+)))?.*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SubjectHeaderRegex = new(
            @"^(?:\d+(?:\.\d+){0,6}\.?\s+)?(?:[•]\s*)?(?<code>PM-\d{2}-PS\d{2})\s*[:,-]?\s*(?<desc>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TopicElementRegex = new(
            @"^(?:[•]\s*)?(?<code>(?:PA|AK)\d{4,6}[A-Z]?)\s+(?<desc>.+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex PmTokenRegex = new(@"^(?<pm>PM-?\d{2})-PS\d{2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static KnowledgeParseResult Parse(string cleanedText, string qualificationCodeFallback)
        {
            var lines = CurriculumKnowledgeParser.NormalizeLines(cleanedText);
            var warnings = new List<string>();
            var modules = new List<KnowledgeModule>();
            var moduleByToken = new Dictionary<string, KnowledgeModule>(StringComparer.OrdinalIgnoreCase);

            var inSection = false;
            var collectingPurpose = false;
            var captureMode = PracticalCaptureMode.None;

            KnowledgeModule? currentModule = null;
            KnowledgeSubject? currentSubject = null;
            string? globalPurpose = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var upper = line.ToUpperInvariant();

                if (IsPracticalSectionStart(upper))
                {
                    inSection = true;
                    collectingPurpose = false;
                    captureMode = PracticalCaptureMode.None;
                    currentModule = null;
                    currentSubject = null;
                    continue;
                }

                if (inSection && IsPracticalSectionEnd(upper))
                {
                    break;
                }

                if (!inSection) continue;

                if (IsPracticalSectionNoiseHeader(upper))
                {
                    collectingPurpose = false;
                    captureMode = PracticalCaptureMode.None;
                    currentSubject = null;
                    continue;
                }

                if (upper.Contains("PURPOSE OF THE PRACTICAL SKILL MODULE", StringComparison.Ordinal))
                {
                    collectingPurpose = true;
                    captureMode = PracticalCaptureMode.None;
                    currentSubject = null;
                    continue;
                }

                if (collectingPurpose)
                {
                    if (upper.Contains("THE LEARNER WILL BE REQUIRED TO", StringComparison.Ordinal))
                    {
                        collectingPurpose = false;
                        captureMode = PracticalCaptureMode.None;
                        currentSubject = null;
                        continue;
                    }

                    if (!ModuleHeaderRegex.IsMatch(line) && !SubjectHeaderRegex.IsMatch(line) &&
                        !upper.Contains("GUIDELINES FOR PRACTICAL SKILLS", StringComparison.Ordinal))
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

                if (upper.Contains("GUIDELINES FOR PRACTICAL SKILLS", StringComparison.Ordinal))
                {
                    captureMode = PracticalCaptureMode.None;
                    currentSubject = null;
                    continue;
                }

                var moduleMatch = ModuleHeaderRegex.Match(line);
                if (moduleMatch.Success)
                {
                    captureMode = PracticalCaptureMode.None;
                    currentSubject = null;

                    var fullCode = NormalizeModuleHeaderCode(moduleMatch.Groups["fullCode"].Value);
                    var pmToken = ExtractPmTokenFromModuleCode(fullCode);
                    var module = GetOrCreateModule(moduleByToken, modules, pmToken, fullCode, qualificationCodeFallback);
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
                    captureMode = PracticalCaptureMode.None;
                    var subjectCode = subjectMatch.Groups["code"].Value.Trim().ToUpperInvariant();
                    var subjectDescription = CurriculumKnowledgeParser.CleanLabel(subjectMatch.Groups["desc"].Value);

                    var pmToken = ExtractPmTokenFromSubjectCode(subjectCode);
                    var module = currentModule != null && string.Equals(currentModule.KmToken, pmToken, StringComparison.OrdinalIgnoreCase)
                        ? currentModule
                        : GetOrCreateModule(moduleByToken, modules, pmToken, BuildModuleCode(qualificationCodeFallback, pmToken), qualificationCodeFallback);
                    if (!string.IsNullOrWhiteSpace(globalPurpose) && string.IsNullOrWhiteSpace(module.PhasesPurpose))
                    {
                        module.PhasesPurpose = globalPurpose!;
                    }

                    if (!module.SubjectsByCode.TryGetValue(subjectCode, out var subject))
                    {
                        subject = new KnowledgeSubject
                        {
                            SubjectCode = subjectCode,
                            SubjectDescription = subjectDescription
                        };
                        module.SubjectsByCode[subjectCode] = subject;
                    }
                    else if (!string.IsNullOrWhiteSpace(subjectDescription))
                    {
                        subject.SubjectDescription = subjectDescription;
                    }

                    currentModule = module;
                    currentSubject = subject;
                    continue;
                }

                if (upper.Contains("SCOPE OF PRACTICAL SKILL", StringComparison.Ordinal))
                {
                    captureMode = PracticalCaptureMode.Scope;
                    continue;
                }

                if (upper.Contains("APPLIED KNOWLEDGE", StringComparison.Ordinal))
                {
                    captureMode = PracticalCaptureMode.AppliedKnowledge;
                    continue;
                }

                if (upper.Contains("INTERNAL ASSESSMENT CRITERIA", StringComparison.Ordinal))
                {
                    captureMode = PracticalCaptureMode.Criteria;
                    continue;
                }

                if (currentSubject == null) continue;

                if (captureMode is PracticalCaptureMode.Scope or PracticalCaptureMode.AppliedKnowledge)
                {
                    var topicMatch = TopicElementRegex.Match(line);
                    if (topicMatch.Success)
                    {
                        var topicCode = topicMatch.Groups["code"].Value.Trim().ToUpperInvariant();
                        var topicDescription = CurriculumKnowledgeParser.CleanLabel(topicMatch.Groups["desc"].Value);
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

                if (captureMode == PracticalCaptureMode.Criteria &&
                    !currentSubject.AssessmentCriteria.Any(c => string.Equals(c, line, StringComparison.OrdinalIgnoreCase)))
                {
                    currentSubject.AssessmentCriteria.Add(line);
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
                        warnings.Add($"No practical topic elements found for {subject.SubjectCode}.");
                        continue;
                    }

                    var criteriaForTopic = CurriculumKnowledgeParser.BuildCriteriaMapping(subject.Topics.Count, subject.AssessmentCriteria);
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
                            AssessmentCriteriaDescription = criteriaForTopic[i],
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

        private static bool IsPracticalSectionStart(string upperLine)
        {
            return upperLine.Contains("SECTION 3B", StringComparison.Ordinal) ||
                   upperLine.Contains("PRACTICAL SKILL MODULE SPECIFICATIONS", StringComparison.Ordinal);
        }

        private static bool IsPracticalSectionEnd(string upperLine)
        {
            return upperLine.Contains("SECTION 3C", StringComparison.Ordinal) ||
                   upperLine.Contains("WORK EXPERIENCE MODULE SPECIFICATIONS", StringComparison.Ordinal);
        }

        private static bool IsPracticalSectionNoiseHeader(string upperLine)
        {
            return upperLine.Contains("PROVIDER PROGRAMME ACCREDITATION CRITERIA", StringComparison.Ordinal) ||
                   upperLine.Contains("PHYSICAL REQUIREMENTS", StringComparison.Ordinal) ||
                   upperLine.Contains("HUMAN RESOURCE REQUIREMENTS", StringComparison.Ordinal) ||
                   upperLine.Contains("LEGAL REQUIREMENTS", StringComparison.Ordinal) ||
                   upperLine.Contains("EXEMPTIONS", StringComparison.Ordinal);
        }

        private static KnowledgeModule GetOrCreateModule(
            Dictionary<string, KnowledgeModule> moduleByToken,
            List<KnowledgeModule> modules,
            string pmToken,
            string fullCode,
            string qualificationCodeFallback)
        {
            if (!moduleByToken.TryGetValue(pmToken, out var module))
            {
                module = new KnowledgeModule
                {
                    QualificationCode = ResolveQualificationCodeFromModule(fullCode, qualificationCodeFallback),
                    PhasesCode = fullCode,
                    KmToken = pmToken
                };
                moduleByToken[pmToken] = module;
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
                module.PhasesCode = BuildModuleCode(module.QualificationCode, pmToken);
            }

            return module;
        }

        private static string ResolveQualificationCodeFromModule(string fullCode, string fallback)
        {
            var normalized = NormalizeModuleHeaderCode(fullCode);
            var m = Regex.Match(normalized ?? string.Empty, @"^(?<q>\d{6,9})(?:-\d{3})?(?:-\d{2})?-PM-?\d{2}$", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups["q"].Value;
            return fallback ?? string.Empty;
        }

        private static string ExtractPmTokenFromModuleCode(string moduleCode)
        {
            var m = Regex.Match(moduleCode ?? string.Empty, @"(?<pm>PM-?\d{2})$", RegexOptions.IgnoreCase);
            return m.Success ? NormalizePmToken(m.Groups["pm"].Value) : "PM-00";
        }

        private static string ExtractPmTokenFromSubjectCode(string subjectCode)
        {
            var m = PmTokenRegex.Match(subjectCode ?? string.Empty);
            return m.Success ? NormalizePmToken(m.Groups["pm"].Value) : "PM-00";
        }

        private static string BuildModuleCode(string qualificationCode, string pmToken)
        {
            var normalizedPmToken = NormalizePmToken(pmToken);
            if (string.IsNullOrWhiteSpace(qualificationCode)) return normalizedPmToken;
            return $"{qualificationCode}-{normalizedPmToken}";
        }

        private static string NormalizeModuleHeaderCode(string? raw)
        {
            var compact = Regex.Replace(raw ?? string.Empty, @"\s+", string.Empty)
                .Replace(",", string.Empty)
                .ToUpperInvariant();
            var m = Regex.Match(compact, @"^(?<qual>\d{6,9}(?:-\d{3})?(?:-\d{2})?)-?(?<pm>PM-?\d{2})$", RegexOptions.IgnoreCase);
            if (!m.Success) return compact;
            return $"{m.Groups["qual"].Value}-{NormalizePmToken(m.Groups["pm"].Value)}";
        }

        private static string NormalizePmToken(string? raw)
        {
            var m = Regex.Match(raw ?? string.Empty, @"PM-?(?<num>\d{2})", RegexOptions.IgnoreCase);
            return m.Success ? $"PM-{m.Groups["num"].Value}" : (raw ?? string.Empty).Trim().ToUpperInvariant();
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

        private static string? DeriveEqualSubjectCredits(string? moduleCredits, int subjectCount)
        {
            if (subjectCount <= 0) return null;
            var credits = CurriculumKnowledgeParser.ParseFlexibleNumber(moduleCredits);
            if (!credits.HasValue || credits.Value <= 0) return null;
            return (credits.Value / subjectCount).ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
