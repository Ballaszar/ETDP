using System.Text.RegularExpressions;
using ETD.Api.Data;
using ETD.Api.Models;

namespace ETD.Api.Utils
{
    public static class AssessmentDrivenQuestionGenerator
    {
        private static readonly Regex SentenceSplit = new(@"(?<=[\.\!\?])\s+", RegexOptions.Compiled);
        private static readonly Regex MultiSpace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex AdministrativeReference = new(@"\b(?:assessment\s+criteria?|assessment\s+criterion|topic\s*code|lpn\s*[-:]?\s*\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AdministrativeCode = new(@"\b(?:AC|KG)\s*[-:]?\s*\d+[A-Za-z]?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CurriculumEcho = new(@"\b(?:lesson\s+plan\s+content|topic\s+content|curriculum(?:\s+content|\s+map)?|assessment\s+alignment)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EvidenceNoiseHeading = new(@"^(?:overview|key\s+concepts|core\s+technical\s+understanding|detailed\s+technical\s+content|procedure\s+and\s+application|safety\s+and\s+quality\s+checks|common\s+faults?(?:\s*/\s*errors)?|worked\s+example|summary|citations?|bibliography|references)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FilePathLike = new(@"(?:[A-Za-z]:\\|\.pdf\b|\.pptx\b|\.docx\b|\.xlsx\b|##\s*Page\b|https?://)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ConversationalLeadIn = new(@"^\s*do\s+you\s+think\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","and","for","with","from","that","this","into","your","you","are","was","were","have","has","had",
            "not","yet","all","any","can","will","would","should","use","using","topic","lesson","plan","criteria",
            "assessment","subject","about","what","when","where","how","why","which","than","then","their","there"
        };
        private static readonly string[] CommonColourValues =
        {
            "blue", "red", "yellow", "green", "orange", "white", "black"
        };

        public sealed class LessonEvidenceItem
        {
            public int TopicId { get; init; }
            public string TopicCode { get; init; } = string.Empty;
            public string TopicDescription { get; init; } = string.Empty;
            public int AssessmentCriteriaId { get; init; }
            public string AssessmentCriteriaDescription { get; init; } = string.Empty;
            public string LessonPlanLabel { get; init; } = string.Empty;
            public string LessonPlanDescription { get; init; } = string.Empty;
            public string LessonPlanContent { get; init; } = string.Empty;
            public string EvidenceText { get; init; } = string.Empty;
            public int TopicOrder { get; init; }
            public int LessonSortOrder { get; init; }
            public string BundleKey { get; init; } = string.Empty;
        }

        public sealed class GeneratedQuestion
        {
            public int Number { get; init; }
            public string Type { get; init; } = string.Empty; // TrueFalse | MultipleChoice
            public string Prompt { get; init; } = string.Empty;
            public List<string> Options { get; init; } = new();
            public string CorrectAnswer { get; init; } = string.Empty;
            public string TopicCode { get; init; } = string.Empty;
            public string TopicDescription { get; init; } = string.Empty;
            public string LessonPlanLabel { get; init; } = string.Empty;
            public string AssessmentCriteriaDescription { get; init; } = string.Empty;
            public string Rationale { get; init; } = string.Empty;
            public int Marks { get; init; }
            public string BundleKey { get; init; } = string.Empty;
        }

        private sealed class LessonPlanFact
        {
            public string Subject { get; init; } = string.Empty;
            public string Verb { get; init; } = "is";
            public string Value { get; init; } = string.Empty;
            public string Statement { get; init; } = string.Empty;
        }

        public static bool ContainsQuestionAdministrativeReference(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return false;
            return AdministrativeReference.IsMatch(text) || AdministrativeCode.IsMatch(text) || CurriculumEcho.IsMatch(text);
        }

        public static string SanitizeQuestionText(string? value)
        {
            var text = (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            text = MultiSpace.Replace(text, " ").Trim();
            if (text.Length == 0) return string.Empty;

            text = AdministrativeCode.Replace(text, string.Empty);
            text = Regex.Replace(text, @"\bassessment\s+criteria\b", "learning requirements", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bassessment\s+criterion\b", "learning requirement", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\btopic\s*code\b", "topic", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bLPN\s*[-:]?\s*\d+\b", "lesson segment", RegexOptions.IgnoreCase);
            text = MultiSpace.Replace(text, " ").Trim();
            text = Regex.Replace(text, @"\s+([,;\.\!\?])", "$1");
            text = text.Trim(' ', ',', ';', ':', '-');
            return MultiSpace.Replace(text, " ").Trim();
        }

        public static string NormalizeQuestionStem(string? value, string fallback)
        {
            var stem = SanitizeQuestionText(value);
            if (string.IsNullOrWhiteSpace(stem) || ConversationalLeadIn.IsMatch(stem))
            {
                stem = SanitizeQuestionText(fallback);
            }
            return string.IsNullOrWhiteSpace(stem) ? fallback : stem;
        }

        public static string NormalizeQuestionStatement(string? value, string fallback = "Follow safe and accurate practice.")
        {
            var statement = SanitizeQuestionText(value);
            if (string.IsNullOrWhiteSpace(statement))
            {
                statement = SanitizeQuestionText(fallback);
            }
            if (string.IsNullOrWhiteSpace(statement))
            {
                statement = "Follow safe and accurate practice.";
            }
            if (!Regex.IsMatch(statement, @"[\.!\?]$"))
            {
                statement += ".";
            }
            return statement;
        }

        public static string BuildLearnerContextLabel(LessonEvidenceItem item)
        {
            foreach (var raw in new[]
            {
                item.TopicDescription,
                item.LessonPlanContent,
                item.EvidenceText
            })
            {
                var cleaned = SanitizeQuestionText(raw);
                if (string.IsNullOrWhiteSpace(cleaned)) continue;
                cleaned = cleaned.Trim().TrimEnd('.', '!', '?');
                if (cleaned.Length < 6) continue;
                if (ContainsQuestionAdministrativeReference(cleaned)) continue;
                return cleaned;
            }
            return "this lesson";
        }

        public static string BuildTopicQuestionLabel(LessonEvidenceItem item)
        {
            var topic = SanitizeQuestionText(item.TopicDescription);
            if (!string.IsNullOrWhiteSpace(topic))
            {
                return topic.Trim().TrimEnd('.', '!', '?');
            }

            var context = BuildLearnerContextLabel(item);
            return string.IsNullOrWhiteSpace(context) ? "this topic" : context.Trim().TrimEnd('.', '!', '?');
        }

        public static List<LessonEvidenceItem> BuildOrderedLessonEvidence(ApplicationDbContext context, int subjectId)
        {
            var subject = context.Subjects.FirstOrDefault(s => s.Id == subjectId);
            if (subject == null) return new List<LessonEvidenceItem>();

            var qualificationCode = context.Qualifications
                .Where(q => q.Id == subject.QualificationId)
                .Select(q => q.QualificationNumber)
                .FirstOrDefault();

            var normalizedQualificationCode = (qualificationCode ?? string.Empty).Trim();

            var topics = context.Topics
                .Where(t => t.SubjectId == subjectId)
                .OrderBy(t => t.Order ?? int.MaxValue)
                .ThenBy(t => t.TopicCode)
                .ThenBy(t => t.Id)
                .ToList();

            if (topics.Count == 0) return new List<LessonEvidenceItem>();

            var topicIds = topics.Select(t => t.Id).ToList();
            var criteria = context.AssessmentCriteria
                .Where(c => topicIds.Contains(c.TopicId))
                .OrderBy(c => c.TopicId)
                .ThenBy(c => c.Id)
                .ToList();
            var criteriaByTopic = criteria
                .GroupBy(c => c.TopicId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var criteriaIds = criteria.Select(c => c.Id).ToList();
            var lessonPlansByCriteria = context.LessonPlans
                .Where(lp => criteriaIds.Contains(lp.AssessmentCriteriaId))
                .OrderBy(lp => lp.SortOrder)
                .ThenBy(lp => lp.Id)
                .ToList()
                .GroupBy(lp => lp.AssessmentCriteriaId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var toolkitEntries = context.LecturerToolkitEntries
                .Where(e => e.AssessmentCriteriaId.HasValue && criteriaIds.Contains(e.AssessmentCriteriaId.Value))
                .ToList();
            var toolkitByCriteria = toolkitEntries
                .GroupBy(e => e.AssessmentCriteriaId ?? 0)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderBy(e => ParseSortNumber(e.Lpn))
                        .ThenBy(e => e.Lpn)
                        .ThenBy(e => e.Id)
                        .ToList());

            var materialsQuery = context.SourceMaterials.AsQueryable();
            if (!string.IsNullOrWhiteSpace(normalizedQualificationCode))
            {
                materialsQuery = materialsQuery.Where(s => (s.QualificationCode ?? string.Empty) == normalizedQualificationCode);
            }
            else
            {
                // Strict isolation: if subject qualification code is unknown, do not pull cross-curriculum evidence.
                materialsQuery = materialsQuery.Where(s => false);
            }

            var materials = materialsQuery
                .OrderByDescending(s => s.CreatedAt)
                .Take(400)
                .ToList();

            var result = new List<LessonEvidenceItem>();
            foreach (var topic in topics)
            {
                if (!criteriaByTopic.TryGetValue(topic.Id, out var topicCriteria) || topicCriteria.Count == 0)
                {
                    var fallbackEvidence = BuildEvidenceText(
                        materials,
                        subject.SubjectDescription,
                        topic.TopicDescription ?? string.Empty,
                        string.Empty,
                        string.Empty);

                    result.Add(new LessonEvidenceItem
                    {
                        TopicId = topic.Id,
                        TopicCode = topic.TopicCode ?? string.Empty,
                        TopicDescription = topic.TopicDescription ?? string.Empty,
                        AssessmentCriteriaId = 0,
                        AssessmentCriteriaDescription = "Apply the lesson content to demonstrate understanding of this topic.",
                        LessonPlanLabel = "LPN 1",
                        LessonPlanDescription = $"Learning activities for {topic.TopicDescription}",
                        LessonPlanContent = string.Empty,
                        EvidenceText = fallbackEvidence,
                        TopicOrder = topic.Order ?? int.MaxValue,
                        LessonSortOrder = 1,
                        BundleKey = $"{topic.Id}:0:LPN1"
                    });
                    continue;
                }

                foreach (var criterion in topicCriteria)
                {
                    var added = false;

                    if (toolkitByCriteria.TryGetValue(criterion.Id, out var teList) && teList.Count > 0)
                    {
                        foreach (var te in teList)
                        {
                            var lessonLabel = NormalizeLessonLabel(te.Lpn);
                            var lessonDesc = string.IsNullOrWhiteSpace(te.LessonPlanDescription)
                                ? $"Lesson for {topic.TopicDescription}"
                                : te.LessonPlanDescription.Trim();
                            var lessonContent = SanitizeLessonEvidenceText(te.LessonPlanContent);
                            var evidence = BuildEvidenceText(
                                materials,
                                subject.SubjectDescription,
                                topic.TopicDescription ?? string.Empty,
                                criterion.Description ?? string.Empty,
                                lessonContent);

                            result.Add(new LessonEvidenceItem
                            {
                                TopicId = topic.Id,
                                TopicCode = topic.TopicCode ?? string.Empty,
                                TopicDescription = topic.TopicDescription ?? string.Empty,
                                AssessmentCriteriaId = criterion.Id,
                                AssessmentCriteriaDescription = criterion.Description ?? string.Empty,
                                LessonPlanLabel = lessonLabel,
                                LessonPlanDescription = lessonDesc,
                                LessonPlanContent = lessonContent,
                                EvidenceText = evidence,
                                TopicOrder = topic.Order ?? int.MaxValue,
                                LessonSortOrder = ParseSortNumber(te.Lpn),
                                BundleKey = $"{topic.Id}:{criterion.Id}:{lessonLabel}"
                            });
                        }
                        added = true;
                    }

                    if (!added && lessonPlansByCriteria.TryGetValue(criterion.Id, out var lpList) && lpList.Count > 0)
                    {
                        foreach (var lp in lpList)
                        {
                            var lessonLabel = $"LPN {Math.Max(1, lp.SortOrder)}";
                            var lessonDesc = string.IsNullOrWhiteSpace(lp.Title)
                                ? $"Lesson for {topic.TopicDescription}"
                                : lp.Title.Trim();
                            var lessonContent = SanitizeLessonEvidenceText(lp.Content);
                            var evidence = BuildEvidenceText(
                                materials,
                                subject.SubjectDescription,
                                topic.TopicDescription ?? string.Empty,
                                criterion.Description ?? string.Empty,
                                lessonContent);

                            result.Add(new LessonEvidenceItem
                            {
                                TopicId = topic.Id,
                                TopicCode = topic.TopicCode ?? string.Empty,
                                TopicDescription = topic.TopicDescription ?? string.Empty,
                                AssessmentCriteriaId = criterion.Id,
                                AssessmentCriteriaDescription = criterion.Description ?? string.Empty,
                                LessonPlanLabel = lessonLabel,
                                LessonPlanDescription = lessonDesc,
                                LessonPlanContent = lessonContent,
                                EvidenceText = evidence,
                                TopicOrder = topic.Order ?? int.MaxValue,
                                LessonSortOrder = Math.Max(1, lp.SortOrder),
                                BundleKey = $"{topic.Id}:{criterion.Id}:{lessonLabel}"
                            });
                        }
                        added = true;
                    }

                    if (!added)
                    {
                        var evidence = BuildEvidenceText(
                            materials,
                            subject.SubjectDescription,
                            topic.TopicDescription ?? string.Empty,
                            criterion.Description ?? string.Empty,
                            string.Empty);
                        result.Add(new LessonEvidenceItem
                        {
                            TopicId = topic.Id,
                            TopicCode = topic.TopicCode ?? string.Empty,
                            TopicDescription = topic.TopicDescription ?? string.Empty,
                            AssessmentCriteriaId = criterion.Id,
                            AssessmentCriteriaDescription = criterion.Description ?? string.Empty,
                            LessonPlanLabel = "LPN 1",
                            LessonPlanDescription = $"Lesson for {topic.TopicDescription}",
                            LessonPlanContent = string.Empty,
                            EvidenceText = evidence,
                            TopicOrder = topic.Order ?? int.MaxValue,
                            LessonSortOrder = 1,
                            BundleKey = $"{topic.Id}:{criterion.Id}:LPN1"
                        });
                    }
                }
            }

            return result
                .OrderBy(x => x.TopicOrder)
                .ThenBy(x => x.TopicCode)
                .ThenBy(x => x.AssessmentCriteriaId)
                .ThenBy(x => x.LessonSortOrder)
                .ThenBy(x => x.LessonPlanLabel)
                .ToList();
        }

        public static GeneratedQuestion BuildTrueFalseQuestion(LessonEvidenceItem item, int number, int marks = 2)
        {
            var facts = ExtractLessonPlanFacts(item);
            var statements = BuildLessonPlanContentStatements(item);
            var primaryFact = facts.FirstOrDefault();
            var correct = primaryFact != null
                ? primaryFact.Statement
                : statements[0];
            var distractors = primaryFact != null
                ? BuildFactBasedDistractors(item, primaryFact, facts, correct)
                : BuildDistractors(item, correct, statements.Skip(1).ToList());
            var optionCount = 4;
            var correctIndex = Math.Abs(number) % optionCount;
            var options = new List<string>(optionCount);
            for (var i = 0; i < optionCount; i++)
            {
                if (i == correctIndex)
                {
                    options.Add(correct);
                }
                else
                {
                    options.Add(distractors[i < correctIndex ? i : i - 1]);
                }
            }
            var correctLabel = OptionLabel(correctIndex);
            var falseKeys = string.Join("; ", Enumerable.Range(0, optionCount)
                .Where(i => i != correctIndex)
                .Select(i => $"{OptionLabel(i)}=False"));
            var topicLabel = BuildTopicQuestionLabel(item);

            return new GeneratedQuestion
            {
                Number = number,
                Type = "TrueFalse",
                Prompt = NormalizeQuestionStem(
                    $"Which statements are True or False relating to {topicLabel}? Only one statement is True.",
                    "Read each statement and mark it as True or False. Only one statement is True."),
                Options = options,
                CorrectAnswer = $"{correctLabel}=True; {falseKeys}",
                TopicCode = item.TopicCode,
                TopicDescription = item.TopicDescription,
                LessonPlanLabel = item.LessonPlanLabel,
                AssessmentCriteriaDescription = item.AssessmentCriteriaDescription,
                Rationale = $"One statement matches {topicLabel}; the remaining statements contradict that same content.",
                Marks = marks,
                BundleKey = item.BundleKey
            };
        }

        public static GeneratedQuestion BuildBinaryTrueFalseQuestion(LessonEvidenceItem item, int number, int marks = 1)
        {
            var facts = ExtractLessonPlanFacts(item);
            var statements = BuildLessonPlanContentStatements(item);
            var questionSeed = Math.Max(1, Math.Abs(number));
            var factIndex = facts.Count > 0
                ? (questionSeed - 1) % facts.Count
                : 0;
            var statementIndex = statements.Count > 0
                ? (questionSeed - 1) % statements.Count
                : 0;
            var primaryFact = facts.Count > 0
                ? facts[factIndex]
                : null;
            var factualStatement = primaryFact != null
                ? primaryFact.Statement
                : statements[statementIndex];
            var shouldBeTrue = questionSeed % 2 == 1;
            var falseAlternativeIndex = facts.Count > 1
                ? (questionSeed - 1) % (facts.Count - 1)
                : 0;
            var renderedStatement = shouldBeTrue
                ? factualStatement
                : primaryFact != null
                    ? BuildFalseFactStatement(primaryFact, facts, falseAlternativeIndex)
                    : BuildBinaryFalseStatement(
                        item,
                        factualStatement,
                        statements
                            .Where((_, index) => index != statementIndex)
                            .ToList());
            var context = BuildLearnerContextLabel(item);
            var topicLabel = BuildTopicQuestionLabel(item);
            var fallbackStatement = NormalizeQuestionStatement(
                $"Safe and accurate practice is required during {context}.",
                "Safe and accurate practice is required during this lesson.");

            return new GeneratedQuestion
            {
                Number = number,
                Type = "TrueFalse",
                Prompt = NormalizeQuestionStatement(renderedStatement, fallbackStatement),
                Options = new List<string> { "True", "False" },
                CorrectAnswer = shouldBeTrue ? "True" : "False",
                TopicCode = item.TopicCode,
                TopicDescription = item.TopicDescription,
                LessonPlanLabel = item.LessonPlanLabel,
                AssessmentCriteriaDescription = item.AssessmentCriteriaDescription,
                Rationale = shouldBeTrue
                    ? $"The statement is taken directly from {topicLabel}."
                    : $"The statement was deliberately altered so that it conflicts with {topicLabel}.",
                Marks = Math.Max(1, marks),
                BundleKey = item.BundleKey
            };
        }

        public static GeneratedQuestion BuildMultipleChoiceQuestion(LessonEvidenceItem item, int number, int marks = 2)
        {
            var facts = ExtractLessonPlanFacts(item);
            var statements = BuildLessonPlanContentStatements(item);
            var primaryFact = facts.FirstOrDefault();
            var correctStatement = primaryFact != null
                ? primaryFact.Statement
                : statements[0];
            var distractors = primaryFact != null
                ? BuildFactBasedDistractors(item, primaryFact, facts, correctStatement)
                : BuildDistractors(item, correctStatement, statements.Skip(1).ToList());

            var correctIndex = Math.Abs(number) % 4;
            var options = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                if (i == correctIndex)
                {
                    options.Add(correctStatement);
                }
                else
                {
                    options.Add(distractors[i < correctIndex ? i : i - 1]);
                }
            }

            var label = ((char)('A' + correctIndex)).ToString();
            var topicLabel = BuildTopicQuestionLabel(item);
            return new GeneratedQuestion
            {
                Number = number,
                Type = "MultipleChoice",
                Prompt = NormalizeQuestionStem(
                    $"Which option best reflects correct practice for {topicLabel}?",
                    "Which option best reflects correct practice for this lesson?"),
                Options = options,
                CorrectAnswer = label,
                TopicCode = item.TopicCode,
                TopicDescription = item.TopicDescription,
                LessonPlanLabel = item.LessonPlanLabel,
                AssessmentCriteriaDescription = item.AssessmentCriteriaDescription,
                Rationale = $"One statement matches {topicLabel} and the remaining statements contradict that same content.",
                Marks = marks,
                BundleKey = item.BundleKey
            };
        }

        private static string NormalizeLessonLabel(string? lpn)
        {
            var raw = (lpn ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return "LPN 1";
            if (raw.StartsWith("LPN", StringComparison.OrdinalIgnoreCase)) return raw.ToUpperInvariant();
            return $"LPN {raw}";
        }

        private static int ParseSortNumber(string? raw)
        {
            var v = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(v)) return int.MaxValue - 1;
            if (int.TryParse(v, out var direct)) return direct;
            var digits = new string(v.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed)) return parsed;
            return int.MaxValue - 1;
        }

        private static List<string> BuildCandidateStatements(LessonEvidenceItem item)
        {
            var block = string.Join(" ",
                item.LessonPlanDescription,
                item.LessonPlanContent,
                item.EvidenceText,
                item.AssessmentCriteriaDescription,
                item.TopicDescription);

            var keywords = ExtractKeywords($"{item.TopicDescription} {item.AssessmentCriteriaDescription}");
            var sentences = ExtractSentences(block)
                .Select(s => new { Text = s, Score = ScoreSentence(s, keywords) })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Text.Length)
                .Select(x => NormalizeQuestionStatement(x.Text))
                .Where(x => !ContainsQuestionAdministrativeReference(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            var context = BuildLearnerContextLabel(item);
            if (sentences.Count == 0)
            {
                sentences.Add(NormalizeQuestionStatement($"Learners apply safe and accurate practice during {context}."));
            }
            if (sentences.Count == 1)
            {
                sentences.Add(NormalizeQuestionStatement($"Quality checks are completed before finalising work in {context}."));
            }
            return sentences;
        }

        private static List<string> BuildLessonPlanContentStatements(LessonEvidenceItem item)
        {
            var statements = new List<string>();

            void AddStatements(string? raw, int maxStatements)
            {
                if (statements.Count >= maxStatements) return;

                var text = (raw ?? string.Empty)
                    .Replace('\r', '\n');
                if (string.IsNullOrWhiteSpace(text)) return;

                var parts = Regex.Split(text, @"(?:[\n;]+)|(?<=[\.\!\?])\s+");
                foreach (var part in parts)
                {
                    var cleaned = part.Trim();
                    cleaned = Regex.Replace(cleaned, @"^[\-\*\d\.\)\(]+", string.Empty).Trim();
                    if (cleaned.Length < 18) continue;
                    if (!Regex.IsMatch(cleaned, "[A-Za-z]")) continue;

                    var statement = NormalizeQuestionStatement(cleaned);
                    if (ContainsQuestionAdministrativeReference(statement)) continue;
                    if (statements.Any(existing => string.Equals(existing, statement, StringComparison.OrdinalIgnoreCase))) continue;

                    statements.Add(statement);
                    if (statements.Count >= maxStatements) break;
                }
            }

            AddStatements(item.LessonPlanContent, maxStatements: 8);
            if (statements.Count == 0)
            {
                var content = SanitizeQuestionText(item.LessonPlanContent);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    statements.Add(NormalizeQuestionStatement(content, content));
                }
            }
            if (statements.Count == 0)
            {
                foreach (var fallback in new[]
                {
                    item.LessonPlanDescription,
                    item.TopicDescription,
                    item.AssessmentCriteriaDescription,
                    item.EvidenceText
                })
                {
                    var cleaned = SanitizeQuestionText(fallback);
                    if (string.IsNullOrWhiteSpace(cleaned)) continue;
                    if (ContainsQuestionAdministrativeReference(cleaned)) continue;
                    statements.Add(NormalizeQuestionStatement(cleaned));
                    break;
                }
            }
            if (statements.Count == 0)
            {
                statements.Add(NormalizeQuestionStatement(
                    $"Learners apply safe and accurate practice during {BuildTopicQuestionLabel(item)}.",
                    "Learners apply safe and accurate practice during this lesson."));
            }

            return statements;
        }

        private static List<LessonPlanFact> ExtractLessonPlanFacts(LessonEvidenceItem item)
        {
            var facts = new List<LessonPlanFact>();

            void AddFact(string subjectRaw, string valueRaw, string? verbRaw = null)
            {
                var subject = NormalizeFactFragment(subjectRaw);
                var value = NormalizeOptionValue(valueRaw);
                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(value)) return;

                var verb = string.IsNullOrWhiteSpace(verbRaw)
                    ? InferFactVerb(subject)
                    : verbRaw.Trim().ToLowerInvariant();
                if (verb != "is" && verb != "are") verb = InferFactVerb(subject);

                var statement = NormalizeQuestionStatement($"{subject} {verb} {value}");
                if (ContainsQuestionAdministrativeReference(statement)) return;
                if (facts.Any(f =>
                        string.Equals(f.Subject, subject, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(f.Value, value, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(f.Verb, verb, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                facts.Add(new LessonPlanFact
                {
                    Subject = subject,
                    Verb = verb,
                    Value = value,
                    Statement = statement
                });
            }

            void AddFactsFromSource(string? raw)
            {
                var text = (raw ?? string.Empty).Replace('\r', '\n');
                if (string.IsNullOrWhiteSpace(text)) return;

                var parts = Regex.Split(text, @"(?:[\n;]+)|(?<=[\.\!\?])\s+");
                foreach (var part in parts)
                {
                    var cleaned = part.Trim();
                    cleaned = Regex.Replace(cleaned, @"^[\-\*\d\.\)\(]+", string.Empty).Trim();
                    if (cleaned.Length < 8) continue;

                    var parentheticalSource = cleaned;
                    var colon = parentheticalSource.LastIndexOf(':');
                    if (colon >= 0 && colon < parentheticalSource.Length - 1)
                    {
                        parentheticalSource = parentheticalSource[(colon + 1)..];
                    }

                    foreach (Match match in Regex.Matches(
                        parentheticalSource,
                        @"(?:^|,|;|\band\b)\s*(?<subject>[A-Za-z][A-Za-z0-9/\-\s]{1,80}?)\s*\((?<value>[^()]{1,40})\)",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    {
                        AddFact(match.Groups["subject"].Value, match.Groups["value"].Value, "are");
                    }

                    var verbMatch = Regex.Match(
                        cleaned,
                        @"^(?<subject>[A-Za-z][A-Za-z0-9/\-\s\(\)]{2,90}?)\s+(?<verb>is|are)\s+(?<value>[^,;:\.\!\?]{1,80})[\.\!\?]?$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (verbMatch.Success)
                    {
                        var value = NormalizeOptionValue(verbMatch.Groups["value"].Value);
                        var wordCount = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                        if (wordCount <= 8)
                        {
                            AddFact(
                                verbMatch.Groups["subject"].Value,
                                value,
                                verbMatch.Groups["verb"].Value);
                        }
                    }
                }
            }

            AddFactsFromSource(item.LessonPlanContent);

            return facts;
        }

        private static List<string> BuildFactBasedDistractors(LessonEvidenceItem item, LessonPlanFact fact, List<LessonPlanFact> facts, string correctStatement)
        {
            var distractors = new List<string>();

            foreach (var alternativeValue in BuildFactAlternativeValues(fact, facts))
            {
                AddDistinctOption(distractors, NormalizeQuestionStatement($"{fact.Subject} {fact.Verb} {alternativeValue}"));
                if (distractors.Count >= 3) break;
            }

            if (distractors.Count < 3)
            {
                AddDistinctOption(distractors, BuildFalseFactStatement(fact, facts));
            }

            if (distractors.Count < 3)
            {
                foreach (var fallback in BuildDistractors(item, correctStatement, BuildLessonPlanContentStatements(item).Skip(1).ToList()))
                {
                    AddDistinctOption(distractors, fallback);
                    if (distractors.Count >= 3) break;
                }
            }

            while (distractors.Count < 3)
            {
                foreach (var candidate in BuildContentBoundFalseStatements(correctStatement))
                {
                    AddDistinctOption(distractors, candidate);
                    if (distractors.Count >= 3) break;
                }

                if (distractors.Count >= 3) break;
                AddDistinctOption(distractors, BuildContextualNearMissStatement(item, correctStatement));
                if (distractors.Count >= 3) break;
                break;
            }

            return distractors;
        }

        private static string BuildFalseFactStatement(LessonPlanFact fact, List<LessonPlanFact> facts, int alternativeIndex = 0)
        {
            var alternativeValues = BuildFactAlternativeValues(fact, facts);
            if (alternativeIndex >= 0 && alternativeIndex < alternativeValues.Count)
            {
                return NormalizeQuestionStatement($"{fact.Subject} {fact.Verb} {alternativeValues[alternativeIndex]}");
            }

            var mutated = NormalizeQuestionStatement(MakeFalseStatement(fact.Statement));
            if (!string.Equals(mutated, fact.Statement, StringComparison.OrdinalIgnoreCase))
            {
                return mutated;
            }

            return NormalizeQuestionStatement($"A common shortcut is acceptable when working with {TrimForSentence(fact.Subject)}.");
        }

        private static List<string> BuildFactAlternativeValues(LessonPlanFact fact, List<LessonPlanFact> facts)
        {
            var values = facts
                .Select(f => NormalizeOptionValue(f.Value))
                .Where(value => !string.Equals(value, fact.Value, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (LooksLikeColour(fact.Value))
            {
                foreach (var colour in CommonColourValues)
                {
                    if (string.Equals(colour, fact.Value, StringComparison.OrdinalIgnoreCase)) continue;
                    if (values.Any(value => string.Equals(value, colour, StringComparison.OrdinalIgnoreCase))) continue;
                    values.Add(colour);
                }
            }

            foreach (var alternative in BuildCommonAlternativeValues(fact.Value))
            {
                if (string.Equals(alternative, fact.Value, StringComparison.OrdinalIgnoreCase)) continue;
                if (values.Any(value => string.Equals(value, alternative, StringComparison.OrdinalIgnoreCase))) continue;
                values.Add(alternative);
            }

            return values;
        }

        private static List<string> BuildFactMcqDistractors(LessonPlanFact fact, List<LessonPlanFact> facts)
        {
            var distractors = new List<string>();
            foreach (var value in BuildFactAlternativeValues(fact, facts))
            {
                var option = NormalizeOptionValue(value);
                if (string.IsNullOrWhiteSpace(option)) continue;
                if (distractors.Any(existing => string.Equals(existing, option, StringComparison.OrdinalIgnoreCase))) continue;
                distractors.Add(option);
                if (distractors.Count >= 3) break;
            }

            while (distractors.Count < 3)
            {
                var fallback = BuildCommonAlternativeValues(fact.Value)
                    .FirstOrDefault(value =>
                        !string.Equals(value, fact.Value, StringComparison.OrdinalIgnoreCase) &&
                        !distractors.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)));

                if (string.IsNullOrWhiteSpace(fallback))
                {
                    fallback = $"Alternative {distractors.Count + 1}";
                }

                distractors.Add(fallback);
            }

            return distractors;
        }

        private static List<string> BuildCommonAlternativeValues(string? value)
        {
            var cleaned = NormalizeOptionValue(value);
            if (string.IsNullOrWhiteSpace(cleaned)) return new List<string>();

            if (LooksLikeColour(cleaned))
            {
                return CommonColourValues
                    .Where(colour => !string.Equals(colour, cleaned, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return cleaned.ToLowerInvariant() switch
            {
                "mandatory" => new List<string> { "prohibition", "warning", "emergency information" },
                "prohibition" => new List<string> { "mandatory", "warning", "emergency information" },
                "warning" => new List<string> { "mandatory", "prohibition", "emergency information" },
                "emergency" => new List<string> { "warning", "mandatory", "prohibition" },
                "emergency information" => new List<string> { "warning", "mandatory", "prohibition" },
                _ => new List<string>()
            };
        }

        private static string BuildFactMcqPrompt(LessonPlanFact fact)
        {
            if (LooksLikeColour(fact.Value))
            {
                return $"What colour are {fact.Subject}?";
            }

            return string.Equals(fact.Verb, "is", StringComparison.OrdinalIgnoreCase)
                ? $"Complete the statement: {fact.Subject} is ____."
                : $"Complete the statement: {fact.Subject} are ____.";
        }

        private static string NormalizeFactFragment(string? value)
        {
            var cleaned = SanitizeQuestionText(value);
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;

            cleaned = Regex.Replace(cleaned, @"^(?:and|or)\s+", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            cleaned = cleaned.Trim(' ', ',', ';', ':', '.', '?', '!');
            return cleaned;
        }

        private static string NormalizeOptionValue(string? value, string fallback = "")
        {
            var cleaned = SanitizeQuestionText(value);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = SanitizeQuestionText(fallback);
            }

            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            return cleaned.Trim(' ', '.', '?', '!', ';', ':');
        }

        private static string InferFactVerb(string subject)
        {
            var cleaned = (subject ?? string.Empty).Trim();
            if (cleaned.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
                cleaned.Contains(" and ", StringComparison.OrdinalIgnoreCase))
            {
                return "are";
            }

            return "is";
        }

        private static bool LooksLikeColour(string? value)
        {
            var cleaned = (value ?? string.Empty).Trim();
            return CommonColourValues.Any(colour => string.Equals(colour, cleaned, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildBinaryFalseStatement(LessonEvidenceItem item, string factualStatement, List<string> alternateCandidates)
        {
            var mutated = NormalizeQuestionStatement(MakeFalseStatement(factualStatement));
            if (!string.IsNullOrWhiteSpace(mutated) &&
                !string.Equals(mutated, factualStatement, StringComparison.OrdinalIgnoreCase) &&
                !ContainsQuestionAdministrativeReference(mutated))
            {
                return mutated;
            }

            var distractor = BuildDistractors(item, factualStatement, alternateCandidates)
                .FirstOrDefault(option =>
                    !string.IsNullOrWhiteSpace(option) &&
                    !string.Equals(option, factualStatement, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(distractor))
            {
                return NormalizeQuestionStatement(distractor);
            }

            return BuildContextualNearMissStatement(item, factualStatement);
        }

        private static List<string> BuildDistractors(LessonEvidenceItem item, string correct, List<string> candidates)
        {
            var list = new List<string>();
            foreach (var c in candidates)
            {
                foreach (var d in BuildContentBoundFalseStatements(c))
                {
                    if (!string.Equals(d, correct, StringComparison.OrdinalIgnoreCase))
                    {
                        AddDistinctOption(list, d);
                    }
                    if (list.Count >= 3) break;
                }

                if (list.Count >= 3) break;
            }

            if (list.Count < 3)
            {
                foreach (var d in BuildContentBoundFalseStatements(correct))
                {
                    AddDistinctOption(list, d);
                    if (list.Count >= 3) break;
                }
            }

            while (list.Count < 3)
            {
                AddDistinctOption(list, BuildContextualNearMissStatement(item, correct));
                if (list.Count >= 3) break;

                foreach (var statement in BuildLessonPlanContentStatements(item))
                {
                    foreach (var d in BuildContentBoundFalseStatements(statement))
                    {
                        AddDistinctOption(list, d);
                        if (list.Count >= 3) break;
                    }

                    if (list.Count >= 3) break;
                }

                if (list.Count >= 3) break;
                break;
            }
            return list;
        }

        private static List<string> BuildContentBoundFalseStatements(string? input)
        {
            var source = NormalizeQuestionStatement(input);
            var options = new List<string>();
            if (string.IsNullOrWhiteSpace(source)) return options;

            var mutated = NormalizeQuestionStatement(MakeFalseStatement(source));
            if (!string.IsNullOrWhiteSpace(mutated) &&
                !string.Equals(mutated, source, StringComparison.OrdinalIgnoreCase) &&
                !ContainsQuestionAdministrativeReference(mutated))
            {
                options.Add(mutated);
            }

            return options;
        }

        private static string BuildContextualNearMissStatement(LessonEvidenceItem item, string factualStatement)
        {
            var mutated = NormalizeQuestionStatement(MakeFalseStatement(factualStatement));
            if (!string.IsNullOrWhiteSpace(mutated) &&
                !string.Equals(mutated, factualStatement, StringComparison.OrdinalIgnoreCase) &&
                !ContainsQuestionAdministrativeReference(mutated))
            {
                return mutated;
            }

            var context = BuildTopicQuestionLabel(item);
            var source = TrimForSentence(factualStatement);
            if (!string.IsNullOrWhiteSpace(context) &&
                !string.Equals(context, "this topic", StringComparison.OrdinalIgnoreCase) &&
                !source.Contains(context, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeQuestionStatement($"During {context}, a common shortcut is acceptable when it saves time.");
            }

            return NormalizeQuestionStatement($"A shortcut is acceptable when completing {source}.");
        }

        private static string BuildEvidenceText(
            List<SourceMaterial> materials,
            string subjectDescription,
            string topicDescription,
            string criteriaDescription,
            string lessonContent)
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(lessonContent))
            {
                bits.Add(SanitizeLessonEvidenceText(lessonContent));
            }

            var keywords = ExtractKeywords($"{subjectDescription} {topicDescription} {criteriaDescription}");
            var scored = new List<(SourceMaterial material, int score)>();
            foreach (var m in materials)
            {
                var score = 0;
                if (!string.IsNullOrWhiteSpace(m.SubjectDescription) &&
                    m.SubjectDescription.Contains(subjectDescription ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    score += 6;
                }
                if (!string.IsNullOrWhiteSpace(m.TopicDescription) &&
                    m.TopicDescription.Contains(topicDescription ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    score += 8;
                }
                if (!string.IsNullOrWhiteSpace(m.AssessmentCriteriaDescription) &&
                    m.AssessmentCriteriaDescription.Contains(criteriaDescription ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    score += 8;
                }

                var text = m.ExtractedText ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var lower = text.ToLowerInvariant();
                    score += keywords.Count(k => lower.Contains(k, StringComparison.Ordinal)) * 2;
                }

                if (score > 0)
                {
                    scored.Add((m, score));
                }
            }

            foreach (var s in scored.OrderByDescending(x => x.score).Take(2))
            {
                var snippet = string.Join(" ", ExtractSentences(s.material.ExtractedText).Take(4));
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    bits.Add(snippet);
                }
            }

            return string.Join(" ", bits.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static List<string> ExtractSentences(string? text)
        {
            var cleaned = SanitizeLessonEvidenceText(text);
            if (cleaned.Length == 0) return new List<string>();

            var raw = SentenceSplit.Split(cleaned);
            var sentences = new List<string>();
            foreach (var part in raw)
            {
                var s = part.Trim();
                s = Regex.Replace(s, @"^[\-\*\d\.\)\(]+", "").Trim();
                if (s.Length < 30 || s.Length > 260) continue;
                if (!Regex.IsMatch(s, "[A-Za-z]")) continue;
                if (EvidenceNoiseHeading.IsMatch(s)) continue;
                if (FilePathLike.IsMatch(s)) continue;
                if (!Regex.IsMatch(s, @"[\.\!\?]$")) s += ".";
                sentences.Add(s);
            }
            return sentences;
        }

        private static string SanitizeLessonEvidenceText(string? value)
        {
            var text = (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var lines = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => !EvidenceNoiseHeading.IsMatch(line))
                .Where(line => !Regex.IsMatch(line, @"^\[\d+\]\s*"))
                .Where(line => !FilePathLike.IsMatch(line))
                .ToList();

            var cleaned = string.Join(" ", lines);
            cleaned = MultiSpace.Replace(cleaned, " ").Trim();
            return cleaned;
        }

        private static List<string> ExtractKeywords(string text)
        {
            return Regex.Split(text ?? string.Empty, @"[^A-Za-z0-9]+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => x.Length > 3 && !StopWords.Contains(x))
                .Distinct()
                .Take(16)
                .ToList();
        }

        private static int ScoreSentence(string sentence, List<string> keywords)
        {
            var lower = sentence.ToLowerInvariant();
            var score = 0;
            foreach (var k in keywords)
            {
                if (lower.Contains(k, StringComparison.Ordinal)) score += 3;
            }
            if (lower.Contains("must ", StringComparison.Ordinal)) score += 2;
            if (lower.Contains("should ", StringComparison.Ordinal)) score += 1;
            if (lower.Contains("demonstrate", StringComparison.Ordinal)) score += 2;
            return score;
        }

        private static string MakeFalseStatement(string input)
        {
            var s = SanitizeQuestionText(input);
            if (string.IsNullOrWhiteSpace(s))
            {
                return string.Empty;
            }

            var simpleReplacements = new (string Pattern, string Replacement)[]
            {
                (@"\bblue\b", "red"),
                (@"\bred\b", "blue"),
                (@"\byellow\b", "green"),
                (@"\bgreen\b", "yellow"),
                (@"\bmandatory\b", "prohibition"),
                (@"\bprohibition\b", "mandatory"),
                (@"\bwarning\b", "emergency"),
                (@"\bemergency information\b", "warning"),
                (@"\bemergency\b", "warning")
            };

            foreach (var (pattern, replacement) in simpleReplacements)
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (regex.IsMatch(s))
                {
                    var mutated = regex.Replace(s, replacement, 1);
                    if (!string.Equals(mutated, s, StringComparison.OrdinalIgnoreCase))
                    {
                        return MultiSpace.Replace(mutated, " ").Trim();
                    }
                }
            }

            var phraseReplacements = new (string Pattern, string Replacement)[]
            {
                (@"\bmust\s+be\b", "does not need to be"),
                (@"\bmust\s+have\b", "does not need to have"),
                (@"\bmust\s+use\b", "must not use"),
                (@"\bmust\b", "must not"),
                (@"\bshould\s+be\b", "should not be"),
                (@"\bshould\b", "should not"),
                (@"\bis\s+required\b", "is optional"),
                (@"\bare\s+required\b", "are optional"),
                (@"\bmandatory\b", "optional"),
                (@"\boptional\b", "mandatory"),
                (@"\bbefore\b", "after"),
                (@"\bafter\b", "before"),
                (@"\bincrease\b", "decrease"),
                (@"\bdecrease\b", "increase"),
                (@"\btighten\b", "loosen"),
                (@"\bloosen\b", "tighten"),
                (@"\bclockwise\b", "counterclockwise"),
                (@"\bcounterclockwise\b", "clockwise"),
                (@"\balways\b", "never"),
                (@"\bnever\b", "always"),
                (@"\bcorrect\b", "incorrect"),
                (@"\baccurate\b", "inaccurate"),
                (@"\bsafe\b", "unsafe"),
                (@"\bacceptable\b", "unacceptable"),
                (@"\buse\b", "do not use"),
                (@"\bis\b", "is not"),
                (@"\bare\b", "are not"),
                (@"\bcan\b", "cannot"),
                (@"\brequired\b", "optional")
            };

            foreach (var (pattern, replacement) in phraseReplacements)
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (regex.IsMatch(s))
                {
                    var mutated = regex.Replace(s, replacement, 1);
                    if (!string.Equals(mutated, s, StringComparison.OrdinalIgnoreCase))
                    {
                        return MultiSpace.Replace(mutated, " ").Trim();
                    }
                }
            }

            var numericMatch = Regex.Match(s, @"\b\d+\b", RegexOptions.CultureInvariant);
            if (numericMatch.Success && int.TryParse(numericMatch.Value, out var numericValue))
            {
                var replacement = (numericValue == 0 ? 1 : numericValue + 1).ToString();
                var mutated = s[..numericMatch.Index] + replacement + s[(numericMatch.Index + numericMatch.Length)..];
                if (!string.Equals(mutated, s, StringComparison.OrdinalIgnoreCase))
                {
                    return MultiSpace.Replace(mutated, " ").Trim();
                }
            }

            return s;
        }

        private static string TrimForSentence(string value)
        {
            var v = SanitizeQuestionText(value);
            if (string.IsNullOrWhiteSpace(v)) return "the lesson requirement";
            return v.EndsWith(".") ? v.TrimEnd('.') : v;
        }

        private static void AddDistinctOption(List<string> list, string value)
        {
            var cleaned = NormalizeQuestionStatement(value);
            if (string.IsNullOrWhiteSpace(cleaned)) return;
            if (list.Any(x => string.Equals(x, cleaned, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(cleaned);
        }

        private static string OptionLabel(int index)
        {
            if (index < 0) return "A";
            return ((char)('A' + index)).ToString();
        }
    }
}
