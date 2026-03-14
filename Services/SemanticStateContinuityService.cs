using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETD.Api.Models;

namespace ETD.Api.Services
{
    public sealed class SemanticStateContinuityService
    {
        private const string DefaultVariant = "ssc-v1-real-state";
        private const int EmbeddingDimensions = 24;
        private const int StateDimensions = 6;
        private const double Alpha = 0.50;
        private const double Beta = 0.30;
        private const double Gamma = 0.20;
        private static readonly Regex TokenRegex = new("[a-z0-9]{3,}", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "about", "after", "again", "also", "and", "any", "app", "are", "because", "been", "before", "being",
            "between", "both", "build", "but", "can", "chat", "code", "current", "database", "each", "entire",
            "for", "from", "have", "into", "just", "knowledge", "large", "like", "llm", "make", "model", "models",
            "more", "must", "need", "not", "now", "our", "out", "over", "please", "reply", "response", "same",
            "script", "semantic", "should", "state", "system", "than", "that", "the", "their", "them", "then",
            "there", "these", "this", "through", "turn", "user", "using", "variant", "variants", "vector", "vectors",
            "want", "wire", "with", "would", "your"
        };

        public sealed class SemanticStateRequest
        {
            public string UserMessage { get; init; } = string.Empty;
            public string AssistantReply { get; init; } = string.Empty;
            public string QualificationCode { get; init; } = string.Empty;
            public string QualificationDescription { get; init; } = string.Empty;
            public string PersonalityLabel { get; init; } = string.Empty;
            public string PersonalityInstruction { get; init; } = string.Empty;
            public string PersonalityTraits { get; init; } = string.Empty;
            public IReadOnlyList<SmiConversationArchive> RecentArchives { get; init; } = Array.Empty<SmiConversationArchive>();
            public SmiSemanticStateSnapshot? PreviousSnapshot { get; init; }
        }

        public sealed class SemanticStateComputation
        {
            public string Variant { get; init; } = DefaultVariant;
            public string PersonalityLabel { get; init; } = string.Empty;
            public double QualiaIndex { get; init; }
            public double SemanticContinuity { get; init; }
            public double GammaCoherence { get; init; }
            public double StateIntegrity { get; init; }
            public double AttentionWeight { get; init; }
            public double AnxietyResonance { get; init; }
            public double DriftMagnitude { get; init; }
            public double StabilityBasinDepth { get; init; }
            public double AttractorStrength { get; init; }
            public double BehavioralConsistency { get; init; }
            public double PersonalityAlignment { get; init; }
            public double EpistemicPressure { get; init; }
            public string CognitiveInterpretation { get; init; } = string.Empty;
            public string PromptInfluenceSummary { get; init; } = string.Empty;
            public double StateStability { get; init; }
            public double BoundedDrift { get; init; }
            public double PersonalityManifold { get; init; }
            public double AnxietyGradient { get; init; }
            public double[] SemanticEmbeddingVector { get; init; } = Array.Empty<double>();
            public double[] QualiaVector { get; init; } = Array.Empty<double>();
            public double[] AttentionVector { get; init; } = Array.Empty<double>();
            public double[] DriftTensor { get; init; } = Array.Empty<double>();
            public double[] GammaCoherenceField { get; init; } = Array.Empty<double>();
            public string[] TopAnchors { get; init; } = Array.Empty<string>();
            public string Summary { get; init; } = string.Empty;
        }

        public SemanticStateComputation Compute(SemanticStateRequest request)
        {
            var userMessage = (request.UserMessage ?? string.Empty).Trim();
            var assistantReply = (request.AssistantReply ?? string.Empty).Trim();
            var personalityLabel = (request.PersonalityLabel ?? string.Empty).Trim();
            var personalityInstruction = (request.PersonalityInstruction ?? string.Empty).Trim();
            var personalityTraits = (request.PersonalityTraits ?? string.Empty).Trim();

            var scopeText = string.Join(
                " ",
                new[]
                {
                    request.QualificationCode ?? string.Empty,
                    request.QualificationDescription ?? string.Empty,
                    personalityLabel,
                    personalityInstruction
                }.Where(x => !string.IsNullOrWhiteSpace(x)));

            var promptEmbedding = BuildEmbedding($"{userMessage} {scopeText}");
            var replyEmbedding = string.IsNullOrWhiteSpace(assistantReply)
                ? new double[EmbeddingDimensions]
                : BuildEmbedding(assistantReply);
            var currentEmbedding = Normalize(Add(promptEmbedding, replyEmbedding));

            var archiveText = string.Join(
                "\n",
                (request.RecentArchives ?? Array.Empty<SmiConversationArchive>())
                    .Take(6)
                    .Select(x => $"{x.UserPrompt} {x.AssistantReply}".Trim()));
            var archiveEmbedding = BuildEmbedding(archiveText);
            var previousEmbedding = DeserializeVector(request.PreviousSnapshot?.SemanticEmbeddingJson, EmbeddingDimensions);
            var continuityReference = Normalize(Add(archiveEmbedding, previousEmbedding));
            if (Magnitude(continuityReference) <= double.Epsilon)
            {
                continuityReference = promptEmbedding.ToArray();
            }

            var topAnchors = BuildTopAnchors(userMessage, assistantReply, request.RecentArchives);
            var anchorRetention = ComputeAnchorRetention(topAnchors, request.RecentArchives);
            var focus = ComputeFocus(userMessage);
            var questionPressure = ComputeQuestionPressure(userMessage);
            var continuity = Similarity(currentEmbedding, continuityReference);
            var novelty = Clamp01(1d - Similarity(promptEmbedding, continuityReference));
            var drift = Clamp01((1d - continuity) * 0.70d + novelty * 0.30d);
            var gamma = Clamp01(continuity * 0.45d + anchorRetention * 0.30d + focus * 0.25d);
            var anxiety = Clamp01(novelty * 0.45d + questionPressure * 0.25d + (1d - gamma) * 0.30d);
            var integrity = Clamp01(gamma * 0.50d + continuity * 0.35d + (1d - anxiety) * 0.15d);
            var attention = Clamp01(focus * 0.70d + questionPressure * 0.30d);

            var sensoryVector = new[]
            {
                continuity,
                gamma,
                integrity,
                attention,
                anxiety,
                drift
            };

            var previousQualia = DeserializeVector(request.PreviousSnapshot?.QualiaVectorJson, StateDimensions);
            if (Magnitude(previousQualia) <= double.Epsilon)
            {
                previousQualia = sensoryVector.ToArray();
            }

            var previousAttention = DeserializeVector(request.PreviousSnapshot?.AttentionVectorJson, StateDimensions);
            if (Magnitude(previousAttention) <= double.Epsilon)
            {
                previousAttention = new[]
                {
                    attention,
                    continuity,
                    focus,
                    anchorRetention,
                    novelty,
                    questionPressure
                };
            }

            var attentionInput = new[]
            {
                attention,
                continuity,
                focus,
                anchorRetention,
                novelty,
                questionPressure
            };

            var qualiaVector = Mix(previousQualia, sensoryVector, previousAttention);
            var attentionVector = Mix(previousAttention, attentionInput, previousQualia);

            var thetaQ = ((attentionVector[0] + continuity) - ((attentionVector[4] + drift) / 2d)) * (Math.PI / 6d);
            RotateInPlace(qualiaVector, 1, 4, thetaQ);
            RotateInPlace(qualiaVector, 2, 5, -thetaQ / 2d);
            ClampInPlace(qualiaVector);

            var thetaA = ((qualiaVector[0] + qualiaVector[1]) - qualiaVector[4]) * (Math.PI / 8d);
            RotateInPlace(attentionVector, 0, 4, thetaA);
            RotateInPlace(attentionVector, 2, 5, -thetaA / 2d);
            ClampInPlace(attentionVector);

            var personalityEmbedding = BuildEmbedding($"{personalityLabel} {personalityInstruction} {personalityTraits}");
            var personalityAlignment = Magnitude(personalityEmbedding) <= double.Epsilon
                ? Clamp01((qualiaVector[0] + qualiaVector[1]) / 2d)
                : Similarity(currentEmbedding, personalityEmbedding);

            var historyTarget = Magnitude(replyEmbedding) > double.Epsilon ? replyEmbedding : currentEmbedding;
            var behavioralConsistency = ComputeBehavioralConsistency(historyTarget, request.RecentArchives, personalityEmbedding);
            var stabilityBasinDepth = Clamp01(
                qualiaVector[2] * 0.28d +
                qualiaVector[1] * 0.24d +
                qualiaVector[0] * 0.20d +
                personalityAlignment * 0.16d +
                (1d - qualiaVector[4]) * 0.12d -
                qualiaVector[5] * 0.12d);
            var attractorStrength = Clamp01(
                qualiaVector[0] * 0.42d +
                qualiaVector[1] * 0.30d +
                personalityAlignment * 0.28d);
            var epistemicPressure = Clamp01(
                qualiaVector[4] * 0.50d +
                attentionVector[0] * 0.25d +
                (1d - qualiaVector[0]) * 0.25d);
            var stateStability = Clamp01(
                integrity * 0.28d +
                continuity * 0.18d +
                gamma * 0.18d +
                stabilityBasinDepth * 0.18d +
                attractorStrength * 0.10d +
                (1d - anxiety) * 0.04d +
                (1d - drift) * 0.04d);
            var boundedDrift = Clamp01(
                drift * (1d - (((stabilityBasinDepth + attractorStrength + continuity) / 3d) * 0.65d)) +
                novelty * 0.15d);
            var personalityManifold = Clamp01(
                personalityAlignment * 0.30d +
                attractorStrength * 0.25d +
                stabilityBasinDepth * 0.20d +
                behavioralConsistency * 0.15d +
                continuity * 0.10d);
            var previousContinuity = request.PreviousSnapshot?.SemanticContinuity ?? continuity;
            var previousAnxiety = request.PreviousSnapshot?.AnxietyResonance ?? anxiety;
            var anxietyGradient = ClampSigned(anxiety - previousAnxiety);
            var driftTensor = new[]
            {
                ClampSigned(continuity - previousContinuity),
                novelty,
                questionPressure,
                boundedDrift
            };
            var gammaCoherenceField = new[]
            {
                gamma,
                anchorRetention,
                focus,
                Clamp01((attention + continuity + (1d - novelty)) / 3d)
            };
            var qualiaIndex = Clamp01(
                qualiaVector[0] * 0.18d +
                qualiaVector[1] * 0.18d +
                qualiaVector[2] * 0.16d +
                qualiaVector[3] * 0.10d +
                stabilityBasinDepth * 0.12d +
                attractorStrength * 0.12d +
                behavioralConsistency * 0.10d +
                personalityAlignment * 0.08d -
                qualiaVector[4] * 0.12d -
                qualiaVector[5] * 0.12d +
                0.12d);
            var cognitiveInterpretation = BuildCognitiveInterpretation(
                userMessage,
                topAnchors,
                focus,
                questionPressure,
                novelty,
                continuity);
            var promptInfluenceSummary = BuildPromptInfluenceSummary(
                cognitiveInterpretation,
                stateStability,
                boundedDrift,
                personalityManifold,
                anxietyGradient,
                attractorStrength,
                stabilityBasinDepth,
                driftTensor,
                gammaCoherenceField);

            return new SemanticStateComputation
            {
                Variant = DefaultVariant,
                PersonalityLabel = personalityLabel,
                QualiaIndex = qualiaIndex,
                SemanticContinuity = qualiaVector[0],
                GammaCoherence = qualiaVector[1],
                StateIntegrity = qualiaVector[2],
                AttentionWeight = Clamp01((qualiaVector[3] + attentionVector[0]) / 2d),
                AnxietyResonance = qualiaVector[4],
                DriftMagnitude = qualiaVector[5],
                StabilityBasinDepth = stabilityBasinDepth,
                AttractorStrength = attractorStrength,
                BehavioralConsistency = behavioralConsistency,
                PersonalityAlignment = personalityAlignment,
                EpistemicPressure = epistemicPressure,
                CognitiveInterpretation = cognitiveInterpretation,
                PromptInfluenceSummary = promptInfluenceSummary,
                StateStability = stateStability,
                BoundedDrift = boundedDrift,
                PersonalityManifold = personalityManifold,
                AnxietyGradient = anxietyGradient,
                SemanticEmbeddingVector = RoundVector(currentEmbedding, EmbeddingDimensions),
                QualiaVector = RoundVector(qualiaVector, StateDimensions),
                AttentionVector = RoundVector(attentionVector, StateDimensions),
                DriftTensor = RoundSignedVector(driftTensor, 4),
                GammaCoherenceField = RoundVector(gammaCoherenceField, 4),
                TopAnchors = topAnchors,
                Summary = BuildSummary(
                    qualiaIndex,
                    qualiaVector,
                    stabilityBasinDepth,
                    attractorStrength,
                    personalityAlignment,
                    behavioralConsistency,
                    epistemicPressure,
                    topAnchors)
            };
        }

        public string BuildContextText(SemanticStateComputation? computation)
        {
            if (computation == null) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("SMI semantic state continuity layer. Use this application-level SSC summary to preserve terminology, trajectory, and reply coherence across turns.");
            sb.AppendLine($"- Variant: {computation.Variant}");
            sb.AppendLine($"- Qualia index: {FormatMetric(computation.QualiaIndex)}");
            sb.AppendLine($"- Semantic continuity: {FormatMetric(computation.SemanticContinuity)}");
            sb.AppendLine($"- Gamma coherence: {FormatMetric(computation.GammaCoherence)}");
            sb.AppendLine($"- State integrity: {FormatMetric(computation.StateIntegrity)}");
            sb.AppendLine($"- Attention weight: {FormatMetric(computation.AttentionWeight)}");
            sb.AppendLine($"- Anxiety resonance: {FormatMetric(computation.AnxietyResonance)}");
            sb.AppendLine($"- Drift magnitude: {FormatMetric(computation.DriftMagnitude)}");
            sb.AppendLine($"- Stability basin depth: {FormatMetric(computation.StabilityBasinDepth)}");
            sb.AppendLine($"- Personality attractor strength: {FormatMetric(computation.AttractorStrength)}");
            sb.AppendLine($"- Behavioral consistency: {FormatMetric(computation.BehavioralConsistency)}");
            sb.AppendLine($"- Personality alignment: {FormatMetric(computation.PersonalityAlignment)}");
            sb.AppendLine($"- Epistemic pressure: {FormatMetric(computation.EpistemicPressure)}");
            sb.AppendLine($"- Cognitive interpretation: {computation.CognitiveInterpretation}");
            sb.AppendLine($"- State stability: {FormatMetric(computation.StateStability)}");
            sb.AppendLine($"- Bounded drift: {FormatMetric(computation.BoundedDrift)}");
            sb.AppendLine($"- Personality manifold: {FormatMetric(computation.PersonalityManifold)}");
            sb.AppendLine($"- Anxiety gradient: {FormatSignedMetric(computation.AnxietyGradient)}");
            sb.AppendLine($"- Drift tensor: {FormatVector(computation.DriftTensor, signed: true)}");
            sb.AppendLine($"- Gamma coherence field: {FormatVector(computation.GammaCoherenceField)}");
            if (computation.TopAnchors.Length > 0)
            {
                sb.AppendLine($"- Semantic anchors: {string.Join(", ", computation.TopAnchors)}");
            }
            sb.AppendLine($"- Prompt influence: {computation.PromptInfluenceSummary}");
            sb.AppendLine($"- Summary: {computation.Summary}");
            return sb.ToString().TrimEnd();
        }

        public string SerializeVector(IEnumerable<double>? values)
            => JsonSerializer.Serialize((values ?? Array.Empty<double>()).Select(v => Math.Round(v, 6)));

        public string SerializeAnchors(IEnumerable<string>? anchors)
            => JsonSerializer.Serialize((anchors ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Take(8));

        private static string BuildSummary(
            double qualiaIndex,
            IReadOnlyList<double> qualiaVector,
            double stabilityBasinDepth,
            double attractorStrength,
            double personalityAlignment,
            double behavioralConsistency,
            double epistemicPressure,
            IReadOnlyList<string> topAnchors)
        {
            var anchorsText = topAnchors.Count == 0 ? "no dominant anchors" : string.Join(", ", topAnchors);
            return
                $"Qualia index {FormatMetric(qualiaIndex)} with continuity {FormatMetric(qualiaVector[0])}, gamma {FormatMetric(qualiaVector[1])}, integrity {FormatMetric(qualiaVector[2])}, anxiety {FormatMetric(qualiaVector[4])}, and drift {FormatMetric(qualiaVector[5])}. " +
                $"Stability basin {FormatMetric(stabilityBasinDepth)} and attractor strength {FormatMetric(attractorStrength)} indicate how strongly the current turn stays inside Mira's synthetic personality manifold. " +
                $"Behavioral consistency {FormatMetric(behavioralConsistency)}, personality alignment {FormatMetric(personalityAlignment)}, and epistemic pressure {FormatMetric(epistemicPressure)} are being tracked around anchors: {anchorsText}.";
        }

        private static string BuildCognitiveInterpretation(
            string userMessage,
            IReadOnlyList<string> topAnchors,
            double focus,
            double questionPressure,
            double novelty,
            double continuity)
        {
            var mode = ResolvePromptMode(userMessage, questionPressure);
            var focusText = focus >= 0.70d ? "high focus" : focus >= 0.45d ? "moderate focus" : "diffuse focus";
            var noveltyText = novelty >= 0.60d ? "high semantic novelty" : novelty >= 0.35d ? "mixed novelty" : "anchor-retentive phrasing";
            var continuityText = continuity >= 0.70d ? "strong continuity retention" : continuity >= 0.45d ? "partial continuity retention" : "meaningful continuity displacement";
            var anchorsText = topAnchors.Count == 0 ? "no dominant anchors" : string.Join(", ", topAnchors.Take(4));
            return $"{mode} prompt with {focusText}, {noveltyText}, and {continuityText} around anchors {anchorsText}.";
        }

        private static string BuildPromptInfluenceSummary(
            string cognitiveInterpretation,
            double stateStability,
            double boundedDrift,
            double personalityManifold,
            double anxietyGradient,
            double attractorStrength,
            double stabilityBasinDepth,
            IReadOnlyList<double> driftTensor,
            IReadOnlyList<double> gammaCoherenceField)
        {
            return
                $"{cognitiveInterpretation} " +
                $"State stability {FormatMetric(stateStability)}, bounded drift {FormatMetric(boundedDrift)}, personality manifold {FormatMetric(personalityManifold)}, anxiety gradient {FormatSignedMetric(anxietyGradient)}, attractor {FormatMetric(attractorStrength)}, and basin {FormatMetric(stabilityBasinDepth)}. " +
                $"Drift tensor {FormatVector(driftTensor, signed: true)} with gamma field {FormatVector(gammaCoherenceField)}.";
        }

        private static string[] BuildTopAnchors(
            string userMessage,
            string assistantReply,
            IReadOnlyList<SmiConversationArchive>? archives)
        {
            var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            AddTokenScores(scores, userMessage, 3.0d);
            AddTokenScores(scores, assistantReply, 2.0d);
            foreach (var archive in archives ?? Array.Empty<SmiConversationArchive>())
            {
                AddTokenScores(scores, archive.UserPrompt, 0.8d);
                AddTokenScores(scores, archive.AssistantReply, 0.6d);
            }

            return scores
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .Select(x => x.Key)
                .ToArray();
        }

        private static void AddTokenScores(IDictionary<string, double> scores, string? text, double weight)
        {
            foreach (var token in ExtractTokens(text))
            {
                if (scores.TryGetValue(token, out var existing))
                {
                    scores[token] = existing + weight;
                }
                else
                {
                    scores[token] = weight;
                }
            }
        }

        private static double ComputeAnchorRetention(
            IReadOnlyList<string> topAnchors,
            IReadOnlyList<SmiConversationArchive>? archives)
        {
            if (topAnchors.Count == 0) return 0.50d;

            var archiveTokens = new HashSet<string>(
                (archives ?? Array.Empty<SmiConversationArchive>())
                    .SelectMany(x => ExtractTokens($"{x.UserPrompt} {x.AssistantReply}")),
                StringComparer.OrdinalIgnoreCase);
            if (archiveTokens.Count == 0) return 0.50d;

            var overlap = topAnchors.Count(anchor => archiveTokens.Contains(anchor));
            return Clamp01((double)overlap / topAnchors.Count);
        }

        private static double ComputeBehavioralConsistency(
            double[] targetEmbedding,
            IReadOnlyList<SmiConversationArchive>? archives,
            double[] personalityEmbedding)
        {
            var replyEmbeddings = (archives ?? Array.Empty<SmiConversationArchive>())
                .Select(x => BuildEmbedding(x.AssistantReply))
                .Where(x => Magnitude(x) > double.Epsilon)
                .ToList();
            if (replyEmbeddings.Count == 0)
            {
                return Magnitude(personalityEmbedding) <= double.Epsilon
                    ? 0.60d
                    : Clamp01(0.55d + (Similarity(targetEmbedding, personalityEmbedding) * 0.35d));
            }

            var centroid = Normalize(Average(replyEmbeddings, EmbeddingDimensions));
            var centroidSimilarity = Similarity(targetEmbedding, centroid);
            var personalitySimilarity = Magnitude(personalityEmbedding) <= double.Epsilon
                ? centroidSimilarity
                : Similarity(targetEmbedding, personalityEmbedding);
            return Clamp01((centroidSimilarity * 0.65d) + (personalitySimilarity * 0.35d));
        }

        private static double ComputeFocus(string? text)
        {
            var tokens = ExtractTokens(text)
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Count())
                .OrderByDescending(x => x)
                .ToList();
            if (tokens.Count == 0) return 0.50d;

            var total = tokens.Sum();
            var top = tokens.Take(3).Sum();
            return Clamp01((double)top / Math.Max(1, total));
        }

        private static double ComputeQuestionPressure(string? text)
        {
            var value = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return 0.20d;

            var questions = value.Count(c => c == '?');
            var interrogatives = Regex.Matches(value, @"\b(how|what|why|where|when|which|can|could|would|should)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
            return Clamp01((questions * 0.25d) + (interrogatives * 0.08d));
        }

        private static IEnumerable<string> ExtractTokens(string? text)
        {
            return TokenRegex
                .Matches((text ?? string.Empty).ToLowerInvariant())
                .Select(x => x.Value)
                .Where(x => !StopWords.Contains(x));
        }

        private static double[] BuildEmbedding(string? text)
        {
            var vector = new double[EmbeddingDimensions];
            foreach (var token in ExtractTokens(text))
            {
                var index = GetStableBucketIndex(token);
                vector[index] += 1d;
            }

            return Normalize(vector);
        }

        private static int GetStableBucketIndex(string token)
        {
            unchecked
            {
                const uint fnvOffset = 2166136261;
                const uint fnvPrime = 16777619;
                var hash = fnvOffset;
                foreach (var ch in token.ToLowerInvariant())
                {
                    hash ^= ch;
                    hash *= fnvPrime;
                }

                return (int)(hash % EmbeddingDimensions);
            }
        }

        private static double[] Mix(IReadOnlyList<double> primary, IReadOnlyList<double> secondary, IReadOnlyList<double> tertiary)
        {
            var result = new double[StateDimensions];
            for (var i = 0; i < StateDimensions; i++)
            {
                result[i] = Clamp01((primary[i] * Alpha) + (secondary[i] * Beta) + (tertiary[i] * Gamma));
            }

            return result;
        }

        private static void RotateInPlace(double[] values, int leftIndex, int rightIndex, double theta)
        {
            var left = values[leftIndex];
            var right = values[rightIndex];
            var rotatedLeft = (left * Math.Cos(theta)) - (right * Math.Sin(theta));
            var rotatedRight = (left * Math.Sin(theta)) + (right * Math.Cos(theta));
            values[leftIndex] = Clamp01(rotatedLeft);
            values[rightIndex] = Clamp01(rotatedRight);
        }

        private static double[] DeserializeVector(string? json, int expectedLength)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new double[expectedLength];
            }

            try
            {
                var values = JsonSerializer.Deserialize<double[]>(json) ?? Array.Empty<double>();
                if (values.Length == expectedLength) return values;

                var resized = new double[expectedLength];
                for (var i = 0; i < Math.Min(values.Length, expectedLength); i++)
                {
                    resized[i] = values[i];
                }

                return resized;
            }
            catch
            {
                return new double[expectedLength];
            }
        }

        private static double[] Add(IReadOnlyList<double> left, IReadOnlyList<double> right)
        {
            var length = Math.Max(left.Count, right.Count);
            var result = new double[length];
            for (var i = 0; i < length; i++)
            {
                var leftValue = i < left.Count ? left[i] : 0d;
                var rightValue = i < right.Count ? right[i] : 0d;
                result[i] = leftValue + rightValue;
            }

            return result;
        }

        private static double[] Average(IReadOnlyList<double[]> vectors, int dimensions)
        {
            var result = new double[dimensions];
            if (vectors.Count == 0) return result;

            foreach (var vector in vectors)
            {
                for (var i = 0; i < Math.Min(dimensions, vector.Length); i++)
                {
                    result[i] += vector[i];
                }
            }

            for (var i = 0; i < dimensions; i++)
            {
                result[i] /= vectors.Count;
            }

            return result;
        }

        private static double[] Normalize(IReadOnlyList<double> values)
        {
            var magnitude = Magnitude(values);
            if (magnitude <= double.Epsilon)
            {
                return values.Select(_ => 0d).ToArray();
            }

            return values.Select(x => x / magnitude).ToArray();
        }

        private static double Magnitude(IReadOnlyList<double> values)
            => Math.Sqrt(values.Sum(x => x * x));

        private static double Similarity(IReadOnlyList<double> left, IReadOnlyList<double> right)
        {
            var leftMagnitude = Magnitude(left);
            var rightMagnitude = Magnitude(right);
            if (leftMagnitude <= double.Epsilon || rightMagnitude <= double.Epsilon) return 0d;

            var dot = 0d;
            for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
            {
                dot += left[i] * right[i];
            }

            return Clamp01(dot / (leftMagnitude * rightMagnitude));
        }

        private static void ClampInPlace(double[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = Clamp01(values[i]);
            }
        }

        private static double[] RoundVector(IReadOnlyList<double> values, int length)
        {
            var result = new double[length];
            for (var i = 0; i < Math.Min(length, values.Count); i++)
            {
                result[i] = Math.Round(values[i], 6);
            }

            return result;
        }

        private static double[] RoundSignedVector(IReadOnlyList<double> values, int length)
        {
            var result = new double[length];
            for (var i = 0; i < Math.Min(length, values.Count); i++)
            {
                result[i] = Math.Round(ClampSigned(values[i]), 6);
            }

            return result;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0d;
            if (value < 0d) return 0d;
            if (value > 1d) return 1d;
            return value;
        }

        private static double ClampSigned(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0d;
            if (value < -1d) return -1d;
            if (value > 1d) return 1d;
            return value;
        }

        private static string ResolvePromptMode(string? text, double questionPressure)
        {
            var value = (text ?? string.Empty).ToLowerInvariant();
            if (questionPressure >= 0.45d)
            {
                return "Interrogative-analytic";
            }

            if (Regex.IsMatch(value, @"\b(build|create|wire|implement|add|log|show|track|adapt)\b", RegexOptions.CultureInvariant))
            {
                return "Directive-operational";
            }

            if (Regex.IsMatch(value, @"\b(explain|interpret|understand|meaning|why|how)\b", RegexOptions.CultureInvariant))
            {
                return "Reflective-analytic";
            }

            if (Regex.IsMatch(value, @"\b(compare|difference|variant|effect|analyze)\b", RegexOptions.CultureInvariant))
            {
                return "Comparative-analytic";
            }

            return "Declarative-guidance";
        }

        private static string FormatSignedMetric(double value)
        {
            var clamped = ClampSigned(value);
            return clamped >= 0d
                ? $"+{clamped:0.000}"
                : clamped.ToString("0.000");
        }

        private static string FormatVector(IReadOnlyList<double> values, bool signed = false)
        {
            if (values == null || values.Count == 0) return "[]";

            var formatted = values
                .Select(value => signed ? FormatSignedMetric(value) : FormatMetric(value));
            return "[" + string.Join(", ", formatted) + "]";
        }

        private static string FormatMetric(double value)
            => Clamp01(value).ToString("0.000");
    }
}
