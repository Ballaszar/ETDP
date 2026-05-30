using ETD.Api.Data;
using ETD.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace ETD.Api.Services
{
    public sealed class KnowledgeQuestionnaireV1Service
    {
        private const string CognitiveDomain = "Cognitive";
        private const string BloomUnderstandLevel = "Understand";
        private const string CoverageProxy = "proxy";
        private const string RoutingKq = "KQ";
        private const string RoutingOtherAssessment = "Other Assessment";
        private const string QualifierSourceDetectedFromCriterion = "detected_from_criterion";
        private const string QualifierSourceLessonPlanFallback = "lesson_plan_fallback";
        private const string QualifierSourceSmiRequired = "smi_required";

        private static readonly string[] BloomCsvCandidates =
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "OneDrive",
                "Documents",
                "Documents",
                "Bloom Cognitive.csv"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents",
                "Bloom Cognitive.csv"),
            Path.Combine(AppContext.BaseDirectory, "Bloom Cognitive.csv")
        };

        private static readonly string[] QualifierMarkers =
        {
            "with respect to",
            "with emphasis on",
            "in accordance with",
            "according to",
            "focus specifically on",
            "focus specific on",
            "focus on",
            "specific to",
            "pertaining to",
            "must be included",
            "must include",
            "including",
            "such as",
            "used by"
        };

        private static readonly HashSet<string> RememberPreferredCanonicals = new(StringComparer.OrdinalIgnoreCase)
        {
            "identify",
            "recognize",
            "recognise",
            "locate",
            "list",
            "name",
            "state",
            "select",
            "match",
            "define",
            "label",
            "recall",
            "remember"
        };

        private static readonly HashSet<string> ExplainPreferredCanonicals = new(StringComparer.OrdinalIgnoreCase)
        {
            "explain",
            "clarify",
            "interpret",
            "discuss",
            "restate",
            "translate",
            "paraphrase",
            "generalize",
            "generalise",
            "compare"
        };

        private static readonly HashSet<string> DescribePreferredCanonicals = new(StringComparer.OrdinalIgnoreCase)
        {
            "describe",
            "outline",
            "detail",
            "summarize",
            "summarise",
            "illustrate",
            "arrange",
            "convert",
            "demonstrate"
        };

        private static readonly Lazy<IReadOnlyList<BloomVerbFamily>> BloomFamilies = new(LoadBloomFamilies);

        private readonly ApplicationDbContext _context;

        public KnowledgeQuestionnaireV1Service(ApplicationDbContext context)
        {
            _context = context;
        }

        public KnowledgeQuestionnaireV1Draft BuildDraft(int qualificationId, int topicId)
        {
            if (topicId <= 0)
            {
                throw new InvalidOperationException("topicId is required.");
            }

            var topic = _context.Topics
                .AsNoTracking()
                .Include(t => t.Subject)
                .ThenInclude(s => s!.Qualification)
                .Include(t => t.Subject)
                .ThenInclude(s => s!.CurriculumPhase)
                .FirstOrDefault(t => t.Id == topicId);

            if (topic == null)
            {
                throw new InvalidOperationException("Topic not found.");
            }

            var subject = topic.Subject ?? throw new InvalidOperationException("Topic is not linked to a subject.");
            var qualification = subject.Qualification ?? throw new InvalidOperationException("Subject is not linked to a qualification.");

            if (qualificationId > 0 && qualification.Id != qualificationId)
            {
                throw new InvalidOperationException("Selected topic does not belong to the requested qualification.");
            }

            var criteria = _context.AssessmentCriteria
                .AsNoTracking()
                .Where(c => c.TopicId == topic.Id)
                .OrderBy(c => c.Id)
                .ToList();

            var intents = new List<KnowledgeQuestionnaireV1CriterionIntentDraft>();
            foreach (var criterion in criteria)
            {
                var criterionIntents = BuildCriterionIntents(criterion);
                StampIntentScope(criterionIntents, subject, topic);
                intents.AddRange(criterionIntents);
            }

            var kqIntents = intents
                .Where(i => string.Equals(i.RoutingStatus, "KQ", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var kqCriteriaCount = kqIntents
                .Select(i => i.AssessmentCriteriaId)
                .Where(id => id > 0)
                .Distinct()
                .Count();
            var minimumQuestionsPerCriterion = 2;
            var minimumTotalQuestions = Math.Max(100, Math.Max(1, kqCriteriaCount) * minimumQuestionsPerCriterion);
            var trueFalseCount = minimumTotalQuestions / 2;
            var multipleChoiceCount = minimumTotalQuestions - trueFalseCount;

            var warnings = BuildDraftWarnings(intents);

            return new KnowledgeQuestionnaireV1Draft
            {
                QualificationId = qualification.Id,
                QualificationCode = (qualification.QualificationNumber ?? string.Empty).Trim(),
                QualificationDescription = (qualification.QualificationDescription ?? string.Empty).Trim(),
                PhaseId = subject.CurriculumPhaseId,
                PhaseCode = (subject.CurriculumPhase?.Name ?? string.Empty).Trim(),
                PhaseDescription = (subject.CurriculumPhase?.Description ?? string.Empty).Trim(),
                SubjectId = subject.Id,
                SubjectCode = (subject.SubjectCode ?? string.Empty).Trim(),
                SubjectDescription = (subject.SubjectDescription ?? string.Empty).Trim(),
                TopicId = topic.Id,
                TopicCode = (topic.TopicCode ?? string.Empty).Trim(),
                TopicDescription = (topic.TopicDescription ?? string.Empty).Trim(),
                Subjects = new List<KnowledgeQuestionnaireV1SubjectScope>
                {
                    new()
                    {
                        SubjectId = subject.Id,
                        SubjectCode = (subject.SubjectCode ?? string.Empty).Trim(),
                        SubjectDescription = (subject.SubjectDescription ?? string.Empty).Trim()
                    }
                },
                Topics = new List<KnowledgeQuestionnaireV1TopicScope>
                {
                    new()
                    {
                        TopicId = topic.Id,
                        SubjectId = subject.Id,
                        SubjectCode = (subject.SubjectCode ?? string.Empty).Trim(),
                        TopicCode = (topic.TopicCode ?? string.Empty).Trim(),
                        TopicDescription = (topic.TopicDescription ?? string.Empty).Trim()
                    }
                },
                Metadata = new KnowledgeQuestionnaireV1MetadataDraft
                {
                    QuestionnaireTitle = BuildQuestionnaireTitle(topic),
                    BloomDomain = string.Empty,
                    BloomTargetLevel = string.Empty,
                    MinimumQuestionsPerCriterion = minimumQuestionsPerCriterion,
                    MinimumTotalQuestions = minimumTotalQuestions,
                    TotalQuestions = minimumTotalQuestions,
                    TrueFalseCount = trueFalseCount,
                    MultipleChoiceCount = multipleChoiceCount,
                    TotalMarks = minimumTotalQuestions,
                    PassMark = string.Empty,
                    CreatedBy = string.Empty,
                    ReviewedBy = string.Empty,
                    Notes = string.Empty
                },
                Criteria = intents,
                Stats = new KnowledgeQuestionnaireV1DraftStats
                {
                    TotalSubjects = 1,
                    TotalTopics = 1,
                    TotalCriteria = criteria.Count,
                    TotalIntents = intents.Count,
                    KqIntentCount = kqIntents.Count,
                    KqCriteriaCount = kqCriteriaCount,
                    OtherAssessmentIntentCount = intents.Count(i => !string.Equals(i.RoutingStatus, "KQ", StringComparison.OrdinalIgnoreCase))
                },
                Warnings = warnings
            };
        }

        public KnowledgeQuestionnaireV1Draft BuildPhaseDraft(int qualificationId, int phaseId)
        {
            if (phaseId <= 0)
            {
                throw new InvalidOperationException("phaseId is required.");
            }

            var phase = _context.CurriculumPhases
                .AsNoTracking()
                .FirstOrDefault(p => p.Id == phaseId);
            if (phase == null)
            {
                throw new InvalidOperationException("Curriculum phase not found.");
            }

            var recoveryWarnings = new List<string>();
            var subjects = ResolvePhaseSubjects(qualificationId, phase, recoveryWarnings);

            if (subjects.Count == 0)
            {
                throw new InvalidOperationException("No subjects were found for the selected curriculum phase.");
            }

            var qualification = subjects
                .Select(s => s.Qualification)
                .FirstOrDefault(q => q != null)
                ?? throw new InvalidOperationException("No qualification is linked to the selected curriculum phase.");

            if (qualificationId > 0 && qualification.Id != qualificationId)
            {
                throw new InvalidOperationException("Selected curriculum phase does not belong to the requested qualification.");
            }

            var subjectIds = subjects.Select(s => s.Id).ToList();
            var topics = CollapseDuplicatePhaseTopics(
                _context.Topics
                .AsNoTracking()
                .Where(t => subjectIds.Contains(t.SubjectId))
                .OrderBy(t => t.SubjectId)
                .ThenBy(t => t.Order ?? int.MaxValue)
                .ThenBy(t => t.TopicCode)
                .ThenBy(t => t.Id)
                .ToList(),
                subjects.ToDictionary(s => s.Id),
                recoveryWarnings);

            var topicIds = topics.Select(t => t.Id).ToList();
            var topicsById = topics.ToDictionary(t => t.Id);
            var criteria = CollapseDuplicatePhaseCriteria(
                _context.AssessmentCriteria
                .AsNoTracking()
                .Where(c => topicIds.Contains(c.TopicId))
                .OrderBy(c => c.TopicId)
                .ThenBy(c => c.Id)
                .ToList(),
                topicsById,
                subjects.ToDictionary(s => s.Id),
                recoveryWarnings);

            var subjectsById = subjects.ToDictionary(s => s.Id);
            var intents = new List<KnowledgeQuestionnaireV1CriterionIntentDraft>();

            foreach (var criterion in criteria)
            {
                if (!topicsById.TryGetValue(criterion.TopicId, out var topic)) continue;
                if (!subjectsById.TryGetValue(topic.SubjectId, out var subject)) continue;

                var criterionIntents = BuildCriterionIntents(criterion);
                StampIntentScope(criterionIntents, subject, topic);
                intents.AddRange(criterionIntents);
            }

            var kqIntents = intents
                .Where(i => string.Equals(i.RoutingStatus, "KQ", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var kqCriteriaCount = kqIntents
                .Select(i => i.AssessmentCriteriaId)
                .Where(id => id > 0)
                .Distinct()
                .Count();
            var minimumQuestionsPerCriterion = 2;
            var minimumTotalQuestions = Math.Max(100, Math.Max(1, kqCriteriaCount) * minimumQuestionsPerCriterion);
            var trueFalseCount = minimumTotalQuestions / 2;
            var multipleChoiceCount = minimumTotalQuestions - trueFalseCount;

            var warnings = BuildDraftWarnings(intents);
            warnings.AddRange(recoveryWarnings);
            warnings = warnings
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new KnowledgeQuestionnaireV1Draft
            {
                QualificationId = qualification.Id,
                QualificationCode = (qualification.QualificationNumber ?? string.Empty).Trim(),
                QualificationDescription = (qualification.QualificationDescription ?? string.Empty).Trim(),
                PhaseId = phase.Id,
                PhaseCode = (phase.Name ?? string.Empty).Trim(),
                PhaseDescription = (phase.Description ?? string.Empty).Trim(),
                SubjectId = 0,
                SubjectCode = "MULTI",
                SubjectDescription = $"{subjects.Count} subjects in phase",
                TopicId = 0,
                TopicCode = "PHASE",
                TopicDescription = $"{topics.Count} topics in phase",
                Subjects = subjects
                    .Select(subject => new KnowledgeQuestionnaireV1SubjectScope
                    {
                        SubjectId = subject.Id,
                        SubjectCode = (subject.SubjectCode ?? string.Empty).Trim(),
                        SubjectDescription = (subject.SubjectDescription ?? string.Empty).Trim()
                    })
                    .ToList(),
                Topics = topics
                    .Select(topic => new KnowledgeQuestionnaireV1TopicScope
                    {
                        TopicId = topic.Id,
                        SubjectId = topic.SubjectId,
                        SubjectCode = subjectsById.TryGetValue(topic.SubjectId, out var subject) ? (subject.SubjectCode ?? string.Empty).Trim() : string.Empty,
                        TopicCode = (topic.TopicCode ?? string.Empty).Trim(),
                        TopicDescription = (topic.TopicDescription ?? string.Empty).Trim()
                    })
                    .ToList(),
                Metadata = new KnowledgeQuestionnaireV1MetadataDraft
                {
                    QuestionnaireTitle = BuildPhaseQuestionnaireTitle(phase),
                    BloomDomain = string.Empty,
                    BloomTargetLevel = string.Empty,
                    MinimumQuestionsPerCriterion = minimumQuestionsPerCriterion,
                    MinimumTotalQuestions = minimumTotalQuestions,
                    TotalQuestions = minimumTotalQuestions,
                    TrueFalseCount = trueFalseCount,
                    MultipleChoiceCount = multipleChoiceCount,
                    TotalMarks = minimumTotalQuestions,
                    PassMark = string.Empty,
                    CreatedBy = string.Empty,
                    ReviewedBy = string.Empty,
                    Notes = string.Empty
                },
                Criteria = intents,
                Stats = new KnowledgeQuestionnaireV1DraftStats
                {
                    TotalSubjects = subjects.Count,
                    TotalTopics = topics.Count,
                    TotalCriteria = criteria.Count,
                    TotalIntents = intents.Count,
                    KqIntentCount = kqIntents.Count,
                    KqCriteriaCount = kqCriteriaCount,
                    OtherAssessmentIntentCount = intents.Count(i => !string.Equals(i.RoutingStatus, "KQ", StringComparison.OrdinalIgnoreCase))
                },
                Warnings = warnings
            };
        }

        private List<Subject> ResolvePhaseSubjects(
            int qualificationId,
            CurriculumPhase phase,
            List<string> warnings)
        {
            var direct = _context.Subjects
                .AsNoTracking()
                .Include(s => s.Qualification)
                .Where(s => s.QualificationId == qualificationId && s.CurriculumPhaseId == phase.Id)
                .OrderBy(s => s.SubjectCode)
                .ThenBy(s => s.Id)
                .ToList();

            if (direct.Count > 0)
            {
                return CollapseDuplicatePhaseSubjects(direct, warnings);
            }

            var phaseName = (phase.Name ?? string.Empty).Trim();
            var moduleToken = ExtractPhaseModuleToken(phase);
            if (!string.IsNullOrWhiteSpace(moduleToken))
            {
                var recovered = _context.Subjects
                    .AsNoTracking()
                    .Include(s => s.Qualification)
                    .Where(s =>
                        s.QualificationId == qualificationId &&
                        EF.Functions.Like(s.SubjectCode ?? string.Empty, $"%{moduleToken}-%"))
                    .OrderBy(s => s.SubjectCode)
                    .ThenBy(s => s.Id)
                    .ToList();

                if (recovered.Count > 0)
                {
                    warnings.Add($"Selected curriculum phase had no directly linked subjects. Recovered scope from subject codes matching {moduleToken}.");
                    return CollapseDuplicatePhaseSubjects(recovered, warnings);
                }
            }

            if (phaseName.Contains("knowledge", StringComparison.OrdinalIgnoreCase))
            {
                var recovered = _context.Subjects
                    .AsNoTracking()
                    .Include(s => s.Qualification)
                    .Where(s =>
                        s.QualificationId == qualificationId &&
                        (EF.Functions.Like(s.SubjectCode ?? string.Empty, "KM-%") ||
                         EF.Functions.Like(s.SubjectCode ?? string.Empty, "%-KM-%")))
                    .OrderBy(s => s.SubjectCode)
                    .ThenBy(s => s.Id)
                    .ToList();

                if (recovered.Count > 0)
                {
                    warnings.Add("Selected Knowledge Learning phase had no directly linked subjects. Recovered scope from subject codes starting with KM-.");
                    return CollapseDuplicatePhaseSubjects(recovered, warnings);
                }
            }

            return direct;
        }

        private static string ExtractPhaseModuleToken(CurriculumPhase phase)
        {
            var source = string.Join(
                " ",
                (phase.Name ?? string.Empty).Trim(),
                (phase.Description ?? string.Empty).Trim());
            var match = Regex.Match(source, @"(?i)(?:^|[^A-Z0-9])(?:\d{4,9}-)?(?<token>(?:KM|PM|WM)-\d{2})(?:[^A-Z0-9]|$)");
            return match.Success ? match.Groups["token"].Value.ToUpperInvariant() : string.Empty;
        }

        private static List<Subject> CollapseDuplicatePhaseSubjects(
            List<Subject> subjects,
            List<string> warnings)
        {
            if (subjects.Count <= 1) return subjects;

            var collapsed = subjects
                .GroupBy(BuildSubjectScopeKey)
                .Select(group => group
                    .OrderBy(subject => subject.Id)
                    .First())
                .OrderBy(subject => subject.SubjectCode)
                .ThenBy(subject => subject.Id)
                .ToList();

            var duplicatesRemoved = subjects.Count - collapsed.Count;
            if (duplicatesRemoved > 0)
            {
                warnings.Add($"Collapsed {duplicatesRemoved} duplicate subject row(s) in the selected curriculum phase.");
            }

            return collapsed;
        }

        private static List<Topic> CollapseDuplicatePhaseTopics(
            List<Topic> topics,
            IReadOnlyDictionary<int, Subject> subjectsById,
            List<string> warnings)
        {
            if (topics.Count <= 1) return topics;

            var collapsed = topics
                .GroupBy(topic => BuildTopicScopeKey(
                    topic,
                    subjectsById.TryGetValue(topic.SubjectId, out var subject) ? subject : null))
                .Select(group => group
                    .OrderBy(topic => topic.Order ?? int.MaxValue)
                    .ThenBy(topic => topic.Id)
                    .First())
                .OrderBy(topic => topic.SubjectId)
                .ThenBy(topic => topic.Order ?? int.MaxValue)
                .ThenBy(topic => topic.TopicCode)
                .ThenBy(topic => topic.Id)
                .ToList();

            var duplicatesRemoved = topics.Count - collapsed.Count;
            if (duplicatesRemoved > 0)
            {
                warnings.Add($"Collapsed {duplicatesRemoved} duplicate topic row(s) in the selected curriculum phase.");
            }

            return collapsed;
        }

        private static List<AssessmentCriteria> CollapseDuplicatePhaseCriteria(
            List<AssessmentCriteria> criteria,
            IReadOnlyDictionary<int, Topic> topicsById,
            IReadOnlyDictionary<int, Subject> subjectsById,
            List<string> warnings)
        {
            if (criteria.Count <= 1) return criteria;

            var collapsed = criteria
                .GroupBy(criteriaRow =>
                {
                    topicsById.TryGetValue(criteriaRow.TopicId, out var topic);
                    var subject = topic != null && subjectsById.TryGetValue(topic.SubjectId, out var subjectRow)
                        ? subjectRow
                        : null;
                    return BuildCriterionScopeKey(criteriaRow, topic, subject);
                })
                .Select(group => group
                    .OrderBy(criteriaRow => criteriaRow.Id)
                    .First())
                .OrderBy(criteriaRow => criteriaRow.TopicId)
                .ThenBy(criteriaRow => criteriaRow.Id)
                .ToList();

            var duplicatesRemoved = criteria.Count - collapsed.Count;
            if (duplicatesRemoved > 0)
            {
                warnings.Add($"Collapsed {duplicatesRemoved} duplicate assessment criteria row(s) in the selected curriculum phase.");
            }

            return collapsed;
        }

        private static string BuildSubjectScopeKey(Subject subject)
        {
            var key = BuildScopeKey(subject.SubjectCode, subject.SubjectDescription);
            return string.IsNullOrWhiteSpace(key) ? $"SUBJECTID:{subject.Id}" : key;
        }

        private static string BuildTopicScopeKey(Topic topic, Subject? subject)
        {
            var key = BuildScopeKey(subject?.SubjectCode ?? string.Empty, topic.TopicCode, topic.TopicDescription);
            return string.IsNullOrWhiteSpace(key) ? $"TOPICID:{topic.Id}" : key;
        }

        private static string BuildCriterionScopeKey(AssessmentCriteria criteria, Topic? topic, Subject? subject)
        {
            var key = BuildScopeKey(subject?.SubjectCode ?? string.Empty, topic?.TopicCode ?? string.Empty, criteria.Description);
            return string.IsNullOrWhiteSpace(key) ? $"CRITERIAID:{criteria.Id}" : key;
        }

        private static string BuildScopeKey(params string?[] parts)
        {
            return string.Join("|", parts
                .Select(part => NormalizeSpaces(part ?? string.Empty).ToUpperInvariant())
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static string BuildQuestionnaireTitle(Topic topic)
        {
            var topicCode = (topic.TopicCode ?? string.Empty).Trim();
            var topicDescription = (topic.TopicDescription ?? string.Empty).Trim();
            return string.Join(" - ", new[] { "Knowledge Questionnaire", topicCode, topicDescription }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        private static string BuildPhaseQuestionnaireTitle(CurriculumPhase phase)
        {
            var phaseCode = (phase.Name ?? string.Empty).Trim();
            var phaseDescription = (phase.Description ?? string.Empty).Trim();
            return string.Join(" - ", new[] { "Knowledge Questionnaire", phaseCode, phaseDescription }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        private static void StampIntentScope(
            IEnumerable<KnowledgeQuestionnaireV1CriterionIntentDraft> intents,
            Subject subject,
            Topic topic)
        {
            foreach (var intent in intents)
            {
                intent.SubjectId = subject.Id;
                intent.SubjectCode = (subject.SubjectCode ?? string.Empty).Trim();
                intent.SubjectDescription = (subject.SubjectDescription ?? string.Empty).Trim();
                intent.TopicId = topic.Id;
                intent.TopicCode = (topic.TopicCode ?? string.Empty).Trim();
                intent.TopicDescription = (topic.TopicDescription ?? string.Empty).Trim();
            }
        }

        private static IReadOnlyList<BloomVerbFamily> LoadBloomFamilies()
        {
            foreach (var path in BloomCsvCandidates)
            {
                if (!File.Exists(path)) continue;

                var parsed = TryParseBloomFamilies(path);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }

            return BuildFallbackBloomFamilies();
        }

        private static List<BloomVerbFamily> TryParseBloomFamilies(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length <= 1) return new List<BloomVerbFamily>();

            var families = new List<BloomVerbFamily>();
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(';');
                if (parts.Length < 3) continue;

                var level = (parts[0] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(level)) continue;

                var definition = (parts[1] ?? string.Empty).Trim();
                var verbs = SplitVerbList(parts[2]);
                if (verbs.Count == 0) continue;

                families.Add(new BloomVerbFamily
                {
                    BloomLevel = level,
                    Definition = definition,
                    RelatedVerbs = verbs
                });
            }

            return families;
        }

        private static IReadOnlyList<BloomVerbFamily> BuildFallbackBloomFamilies()
        {
            return new List<BloomVerbFamily>
            {
                new()
                {
                    BloomLevel = "Remember",
                    Definition = "Recall specific bits of information",
                    RelatedVerbs = new List<string>
                    {
                        "tell", "list", "describe", "name", "repeat", "remember", "recall", "identify",
                        "state", "select", "match", "know", "locate", "report", "recognize", "define", "label"
                    }
                },
                new()
                {
                    BloomLevel = "Understand",
                    Definition = "Construct meaning from information",
                    RelatedVerbs = new List<string>
                    {
                        "explain", "restate", "find", "describe", "review", "relate", "clarify", "illustrate",
                        "outline", "summarize", "interpret", "paraphrase", "transform", "compare", "translate"
                    }
                },
                new()
                {
                    BloomLevel = "Apply",
                    Definition = "Use methods, concepts, principles, and theories in new situations",
                    RelatedVerbs = new List<string> { "apply", "use", "demonstrate", "solve", "practice" }
                },
                new()
                {
                    BloomLevel = "Analyze",
                    Definition = "Identify how parts relate to one another or to a larger structure",
                    RelatedVerbs = new List<string> { "analyze", "compare", "contrast", "differentiate", "dissect" }
                },
                new()
                {
                    BloomLevel = "Evaluate",
                    Definition = "Judge the value of something based on criteria",
                    RelatedVerbs = new List<string> { "evaluate", "justify", "recommend", "assess", "criticize" }
                }
            };
        }

        private static List<string> SplitVerbList(string raw)
        {
            return (raw ?? string.Empty)
                .Split(',')
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<KnowledgeQuestionnaireV1CriterionIntentDraft> BuildCriterionIntents(AssessmentCriteria criterion)
        {
            var text = NormalizeSpaces(criterion.Description);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<KnowledgeQuestionnaireV1CriterionIntentDraft>();
            }

            return new List<KnowledgeQuestionnaireV1CriterionIntentDraft>
            {
                new()
                {
                    IntentId = $"kqv1-{criterion.Id}-1",
                    AssessmentCriteriaId = criterion.Id,
                    AssessmentCriteriaNumber = $"AC-{criterion.Id}",
                    OriginalCriterionText = text,
                    NounFocus = text,
                    DetectedVerb = string.Empty,
                    CanonicalVerb = string.Empty,
                    BloomDomain = string.Empty,
                    BloomLevel = string.Empty,
                    Qualifier = string.Empty,
                    QualifierSource = string.Empty,
                    CoverageType = "direct",
                    RoutingStatus = RoutingKq
                }
            };
        }

        private static List<DetectedVerbMatch> DetectVerbMatches(string text)
        {
            var matches = new List<DetectedVerbMatch>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var family in BloomFamilies.Value)
            {
                foreach (var alias in family.RelatedVerbs)
                {
                    foreach (var variant in ExpandVerbVariants(alias))
                    {
                        foreach (Match match in Regex.Matches(text, $@"\b{Regex.Escape(variant)}\b", RegexOptions.IgnoreCase))
                        {
                            if (!match.Success) continue;
                            var key = $"{match.Index}|{match.Length}|{family.BloomLevel}|{variant}";
                            if (!seen.Add(key)) continue;
                            matches.Add(new DetectedVerbMatch
                            {
                                Index = match.Index,
                                MatchLength = match.Length,
                                DetectedVerb = text.Substring(match.Index, match.Length),
                                NormalizedVerb = NormalizeVerb(match.Value),
                                BloomLevel = family.BloomLevel
                            });
                        }
                    }
                }
            }

            return matches
                .GroupBy(m => $"{m.Index}|{m.MatchLength}|{m.DetectedVerb}".ToLowerInvariant())
                .Select(g => g.First())
                .OrderBy(m => m.Index)
                .ThenByDescending(m => m.MatchLength)
                .ToList();
        }

        private static IEnumerable<string> ExpandVerbVariants(string alias)
        {
            var normalized = NormalizeVerb(alias);
            if (string.IsNullOrWhiteSpace(normalized)) yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in ExpandVerbVariantsInternal(normalized))
            {
                var cleaned = NormalizeSpaces(value);
                if (!string.IsNullOrWhiteSpace(cleaned) && seen.Add(cleaned))
                {
                    yield return cleaned;
                }
            }
        }

        private static IEnumerable<string> ExpandVerbVariantsInternal(string normalized)
        {
            yield return normalized;
            if (normalized.Contains(' ')) yield break;

            if (normalized.EndsWith("e", StringComparison.OrdinalIgnoreCase))
            {
                yield return normalized[..^1] + "ing";
                yield return normalized + "d";
                yield return normalized + "s";
            }
            else
            {
                yield return normalized + "ing";
                yield return normalized + "ed";
                yield return normalized + "s";
                yield return normalized + "es";
            }

            if (normalized.EndsWith("y", StringComparison.OrdinalIgnoreCase) && normalized.Length > 1)
            {
                yield return normalized[..^1] + "ies";
                yield return normalized[..^1] + "ied";
            }
        }

        private static string NormalizeVerb(string value)
        {
            var cleaned = NormalizeSpaces(Regex.Replace(value ?? string.Empty, @"[^A-Za-z\s]", " ")).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            if (cleaned.Contains(' ')) return cleaned;

            return cleaned switch
            {
                "desctribe" or "desctribes" or "desctribed" or "desctribing" => "describe",
                "described" or "describes" or "describing" => "describe",
                "demonstrated" or "demonstrates" or "demonstrating" => "demonstrate",
                "explained" or "explains" or "explaining" => "explain",
                "identified" or "identifies" or "identifying" => "identify",
                "outlined" or "outlines" or "outlining" => "outline",
                "detailed" or "details" or "detailing" => "detail",
                "clarified" or "clarifies" or "clarifying" => "clarify",
                "interpreted" or "interprets" or "interpreting" => "interpret",
                "discussed" or "discusses" or "discussing" => "discuss",
                "translated" or "translates" or "translating" => "translate",
                "summarised" or "summarises" or "summarising" => "summarise",
                "summarized" or "summarizes" or "summarizing" => "summarize",
                "listed" or "lists" or "listing" => "list",
                "recognized" or "recognizes" or "recognizing" => "recognize",
                "recognised" or "recognises" or "recognising" => "recognise",
                "located" or "locates" or "locating" => "locate",
                "selected" or "selects" or "selecting" => "select",
                "matched" or "matches" or "matching" => "match",
                "defined" or "defines" or "defining" => "define",
                "labelled" or "labelling" => "label",
                _ => cleaned
            };
        }

        private static CriterionDecomposition DecomposeCriterionText(string text, IReadOnlyList<DetectedVerbMatch> matches)
        {
            var firstMatch = matches.OrderBy(m => m.Index).First();
            var passiveMatch = Regex.Match(text, @"\b(is|are|was|were|be|been|being)\b", RegexOptions.IgnoreCase);
            var passiveDetected = passiveMatch.Success && passiveMatch.Index < firstMatch.Index;

            string nounFocus;
            string trailing;

            if (passiveDetected)
            {
                nounFocus = text[..passiveMatch.Index].Trim(' ', ',', ';', ':', '.');
                var chainEnd = FindVerbChainEnd(text, matches);
                trailing = chainEnd < text.Length ? text[chainEnd..].Trim(' ', ',', ';', ':', '.') : string.Empty;
            }
            else if (firstMatch.Index <= 18)
            {
                var chainEnd = FindVerbChainEnd(text, matches);
                trailing = chainEnd < text.Length ? text[chainEnd..].Trim(' ', ',', ';', ':', '.') : string.Empty;
                nounFocus = trailing;
            }
            else
            {
                nounFocus = text;
                trailing = string.Empty;
            }

            var qualifier = string.Empty;
            var sourceForQualifier = passiveDetected ? trailing : nounFocus;
            var marker = FindQualifierMarker(sourceForQualifier);
            if (marker != null)
            {
                if (passiveDetected)
                {
                    qualifier = sourceForQualifier[marker.Value.Index..].Trim(' ', ',', ';', ':', '.');
                }
                else
                {
                    qualifier = sourceForQualifier[marker.Value.Index..].Trim(' ', ',', ';', ':', '.');
                    nounFocus = sourceForQualifier[..marker.Value.Index].Trim(' ', ',', ';', ':', '.');
                }
            }

            if (string.IsNullOrWhiteSpace(nounFocus))
            {
                nounFocus = text;
            }

            return new CriterionDecomposition
            {
                NounFocus = NormalizeSpaces(nounFocus),
                Qualifier = NormalizeSpaces(qualifier)
            };
        }

        private static int FindVerbChainEnd(string text, IReadOnlyList<DetectedVerbMatch> matches)
        {
            var ordered = matches.OrderBy(m => m.Index).ToList();
            if (ordered.Count == 0) return 0;

            var chainEnd = ordered[0].Index + ordered[0].MatchLength;
            for (var i = 1; i < ordered.Count; i++)
            {
                var current = ordered[i];
                if (current.Index < chainEnd) continue;

                var between = text[chainEnd..current.Index];
                if (Regex.IsMatch(between, @"^\s*(,|and|or|is|are|was|were|to|the|a|an|clearly|accurately|properly|correctly|\-)?\s*$", RegexOptions.IgnoreCase))
                {
                    chainEnd = current.Index + current.MatchLength;
                    continue;
                }

                break;
            }

            return chainEnd;
        }

        private static (int Index, string Marker)? FindQualifierMarker(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var lowered = text.ToLowerInvariant();
            (int Index, string Marker)? best = null;
            foreach (var marker in QualifierMarkers)
            {
                var idx = lowered.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0) continue;
                if (best == null || idx < best.Value.Index)
                {
                    best = (idx, marker);
                }
            }

            return best;
        }

        private static string ResolveCanonicalVerb(string normalizedVerb, string bloomLevel)
        {
            if (RememberPreferredCanonicals.Contains(normalizedVerb))
            {
                return "identify";
            }

            if (DescribePreferredCanonicals.Contains(normalizedVerb))
            {
                return "describe";
            }

            if (ExplainPreferredCanonicals.Contains(normalizedVerb))
            {
                return "explain";
            }

            if (string.Equals(bloomLevel, "Remember", StringComparison.OrdinalIgnoreCase))
            {
                return "identify";
            }

            return normalizedVerb;
        }

        private static VerbClassification ClassifyCanonicalVerb(string canonicalVerb, string detectedBloomLevel)
        {
            var canonical = NormalizeVerb(canonicalVerb);
            if (string.Equals(canonical, "identify", StringComparison.OrdinalIgnoreCase))
            {
                return new VerbClassification("Remember", "direct", RoutingKq);
            }

            if (string.Equals(canonical, "describe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(canonical, "explain", StringComparison.OrdinalIgnoreCase))
            {
                return new VerbClassification(BloomUnderstandLevel, CoverageProxy, RoutingKq);
            }

            if (string.Equals(detectedBloomLevel, "Remember", StringComparison.OrdinalIgnoreCase))
            {
                return new VerbClassification("Remember", "direct", RoutingKq);
            }

            if (string.Equals(detectedBloomLevel, BloomUnderstandLevel, StringComparison.OrdinalIgnoreCase))
            {
                return new VerbClassification(BloomUnderstandLevel, CoverageProxy, RoutingKq);
            }

            return new VerbClassification(
                string.IsNullOrWhiteSpace(detectedBloomLevel) ? BloomUnderstandLevel : detectedBloomLevel,
                CoverageProxy,
                RoutingKq);
        }

        private static string ResolveQualifierSource(VerbClassification classification, string qualifier)
        {
            if (!string.IsNullOrWhiteSpace(qualifier))
            {
                return QualifierSourceDetectedFromCriterion;
            }

            return string.Equals(classification.RoutingStatus, RoutingKq, StringComparison.OrdinalIgnoreCase)
                ? QualifierSourceLessonPlanFallback
                : QualifierSourceSmiRequired;
        }

        private static List<string> BuildDraftWarnings(IEnumerable<KnowledgeQuestionnaireV1CriterionIntentDraft> intents)
        {
            _ = intents;
            return new List<string>();
        }

        private static string NormalizeSpaces(string value)
        {
            return Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        }

        public sealed class KnowledgeQuestionnaireV1Draft
        {
            public int QualificationId { get; set; }
            public string QualificationCode { get; set; } = string.Empty;
            public string QualificationDescription { get; set; } = string.Empty;
            public int PhaseId { get; set; }
            public string PhaseCode { get; set; } = string.Empty;
            public string PhaseDescription { get; set; } = string.Empty;
            public int SubjectId { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public int TopicId { get; set; }
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public List<KnowledgeQuestionnaireV1SubjectScope> Subjects { get; set; } = new();
            public List<KnowledgeQuestionnaireV1TopicScope> Topics { get; set; } = new();
            public KnowledgeQuestionnaireV1MetadataDraft Metadata { get; set; } = new();
            public List<KnowledgeQuestionnaireV1CriterionIntentDraft> Criteria { get; set; } = new();
            public KnowledgeQuestionnaireV1DraftStats Stats { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
        }

        public sealed class KnowledgeQuestionnaireV1MetadataDraft
        {
            public string QuestionnaireTitle { get; set; } = string.Empty;
            public string BloomDomain { get; set; } = CognitiveDomain;
            public string BloomTargetLevel { get; set; } = "Understand";
            public int MinimumQuestionsPerCriterion { get; set; } = 2;
            public int MinimumTotalQuestions { get; set; }
            public int TotalQuestions { get; set; }
            public int TrueFalseCount { get; set; }
            public int MultipleChoiceCount { get; set; }
            public int TotalMarks { get; set; }
            public string PassMark { get; set; } = string.Empty;
            public string CreatedBy { get; set; } = string.Empty;
            public string ReviewedBy { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;
        }

        public sealed class KnowledgeQuestionnaireV1CriterionIntentDraft
        {
            public string IntentId { get; set; } = string.Empty;
            public int AssessmentCriteriaId { get; set; }
            public string AssessmentCriteriaNumber { get; set; } = string.Empty;
            public int SubjectId { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public int TopicId { get; set; }
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string OriginalCriterionText { get; set; } = string.Empty;
            public string NounFocus { get; set; } = string.Empty;
            public string DetectedVerb { get; set; } = string.Empty;
            public string CanonicalVerb { get; set; } = string.Empty;
            public string BloomDomain { get; set; } = CognitiveDomain;
            public string BloomLevel { get; set; } = string.Empty;
            public string Qualifier { get; set; } = string.Empty;
            public string QualifierSource { get; set; } = string.Empty;
            public string CoverageType { get; set; } = "proxy";
            public string RoutingStatus { get; set; } = "Other Assessment";
        }

        public sealed class KnowledgeQuestionnaireV1DraftStats
        {
            public int TotalSubjects { get; set; }
            public int TotalTopics { get; set; }
            public int TotalCriteria { get; set; }
            public int TotalIntents { get; set; }
            public int KqIntentCount { get; set; }
            public int KqCriteriaCount { get; set; }
            public int OtherAssessmentIntentCount { get; set; }
        }

        public sealed class KnowledgeQuestionnaireV1SubjectScope
        {
            public int SubjectId { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
        }

        public sealed class KnowledgeQuestionnaireV1TopicScope
        {
            public int TopicId { get; set; }
            public int SubjectId { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
        }

        private sealed class BloomVerbFamily
        {
            public string BloomLevel { get; set; } = string.Empty;
            public string Definition { get; set; } = string.Empty;
            public List<string> RelatedVerbs { get; set; } = new();
        }

        private sealed class DetectedVerbMatch
        {
            public int Index { get; set; }
            public int MatchLength { get; set; }
            public string DetectedVerb { get; set; } = string.Empty;
            public string NormalizedVerb { get; set; } = string.Empty;
            public string BloomLevel { get; set; } = string.Empty;
        }

        private sealed class CriterionDecomposition
        {
            public string NounFocus { get; set; } = string.Empty;
            public string Qualifier { get; set; } = string.Empty;
        }

        private readonly record struct VerbClassification(string BloomLevel, string CoverageType, string RoutingStatus);
    }
}
