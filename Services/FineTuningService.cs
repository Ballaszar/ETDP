using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Services
{
    public sealed class FineTuningService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        private static readonly JsonSerializerOptions JsonlOptions = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _context;
        private readonly ILogger<FineTuningService> _logger;

        public FineTuningService(ApplicationDbContext context, ILogger<FineTuningService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<FineTuningDatasetResult> PrepareLearnerGuideSftDatasetAsync(
            FineTuningDatasetRequest request,
            CancellationToken ct)
        {
            request ??= new FineTuningDatasetRequest();
            var maxMaterials = Math.Clamp(request.MaxMaterials, 1, 1000);
            var maxExamplesPerMaterial = Math.Clamp(request.MaxExamplesPerMaterial, 1, 8);
            var maxSourceChars = Math.Clamp(request.MaxSourceCharsPerExample, 1200, 12_000);

            var query = _context.SourceMaterials.AsNoTracking()
                .Where(x => !string.IsNullOrWhiteSpace(x.ExtractedText));

            if (!string.IsNullOrWhiteSpace(request.QualificationCode))
            {
                var qualificationCode = request.QualificationCode.Trim();
                query = query.Where(x => x.QualificationCode == qualificationCode);
            }

            if (!string.IsNullOrWhiteSpace(request.QualificationDescription))
            {
                var qualificationDescription = request.QualificationDescription.Trim();
                query = query.Where(x => x.QualificationDescription == qualificationDescription);
            }

            if (request.KnowledgeSourceTypes.Count > 0)
            {
                var sourceTypes = request.KnowledgeSourceTypes
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToList();
                query = query.Where(x => sourceTypes.Contains(x.KnowledgeSourceType ?? string.Empty));
            }

            var materials = await query
                .OrderByDescending(x => x.KnowledgeUploadedAtUtc ?? x.CreatedAt)
                .Take(maxMaterials)
                .ToListAsync(ct);

            var root = Path.Combine(EtdpPaths.GetExportsRoot(), "FineTuning");
            Directory.CreateDirectory(root);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var jsonlPath = Path.Combine(root, $"learner-guide-sft-{stamp}.jsonl");
            var manifestPath = Path.Combine(root, $"learner-guide-sft-{stamp}.manifest.json");

            var exampleCount = 0;
            await using (var stream = File.Create(jsonlPath))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                foreach (var material in materials)
                {
                    ct.ThrowIfCancellationRequested();
                    var chunks = SplitIntoChunks(CleanText(material.ExtractedText), maxSourceChars)
                        .Take(maxExamplesPerMaterial)
                        .ToList();

                    foreach (var chunk in chunks)
                    {
                        if (chunk.Length < 500)
                        {
                            continue;
                        }

                        var trainingExample = BuildLearnerGuideTrainingExample(material, chunk, request);
                        await writer.WriteLineAsync(JsonSerializer.Serialize(trainingExample, JsonlOptions));
                        exampleCount++;
                    }
                }
            }

            var manifest = new FineTuningDatasetManifest
            {
                CreatedAtUtc = DateTime.UtcNow,
                JsonlPath = jsonlPath,
                ExampleCount = exampleCount,
                MaterialCount = materials.Count,
                QualificationCode = request.QualificationCode ?? string.Empty,
                QualificationDescription = request.QualificationDescription ?? string.Empty,
                Notes = "Supervised fine-tuning examples for learner-guide authoring style. Review before launching a provider fine-tuning job."
            };
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), ct);

            return new FineTuningDatasetResult
            {
                JsonlPath = jsonlPath,
                ManifestPath = manifestPath,
                ExampleCount = exampleCount,
                MaterialCount = materials.Count,
                ReadyForProviderUpload = exampleCount >= 10,
                Warning = exampleCount < 10
                    ? "Too few examples for a useful fine-tune. Add/review more source material or increase MaxMaterials."
                    : string.Empty
            };
        }

        public async Task<JsonElement> CreateOpenAiFineTuneJobAsync(OpenAiFineTuneJobRequest request, CancellationToken ct)
        {
            if (!AiRuntime.AllowOpenAi())
            {
                throw new InvalidOperationException("OpenAI fine-tuning is disabled while AI mode is offline. Switch AI mode to hybrid or cloud first.");
            }

            var apiKey = AiRuntime.GetOpenAiApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("OpenAI API key is not configured.");
            }

            var jsonlPath = Path.GetFullPath(request.TrainingJsonlPath ?? string.Empty);
            if (!File.Exists(jsonlPath))
            {
                throw new FileNotFoundException("Training JSONL file was not found.", jsonlPath);
            }

            using var client = CreateOpenAiClient(apiKey);
            await using var fileStream = File.OpenRead(jsonlPath);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/jsonl");
            using var uploadContent = new MultipartFormDataContent
            {
                { new StringContent("fine-tune"), "purpose" },
                { fileContent, "file", Path.GetFileName(jsonlPath) }
            };

            using var uploadResponse = await client.PostAsync("https://api.openai.com/v1/files", uploadContent, ct);
            var uploadBody = await uploadResponse.Content.ReadAsStringAsync(ct);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"OpenAI file upload failed: {uploadBody}");
            }

            using var uploadJson = JsonDocument.Parse(uploadBody);
            var fileId = uploadJson.RootElement.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(fileId))
            {
                throw new InvalidOperationException("OpenAI file upload did not return a file id.");
            }

            var model = string.IsNullOrWhiteSpace(request.Model)
                ? "gpt-4.1-mini-2025-04-14"
                : request.Model.Trim();
            var payload = new Dictionary<string, object?>
            {
                ["model"] = model,
                ["training_file"] = fileId,
                ["method"] = new Dictionary<string, object?> { ["type"] = "supervised" }
            };
            if (!string.IsNullOrWhiteSpace(request.Suffix))
            {
                payload["suffix"] = request.Suffix.Trim();
            }

            using var jobContent = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var jobResponse = await client.PostAsync("https://api.openai.com/v1/fine_tuning/jobs", jobContent, ct);
            var jobBody = await jobResponse.Content.ReadAsStringAsync(ct);
            if (!jobResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"OpenAI fine-tuning job creation failed: {jobBody}");
            }

            _logger.LogInformation("Created OpenAI fine-tuning job from {Path}", jsonlPath);
            using var jobJson = JsonDocument.Parse(jobBody);
            return jobJson.RootElement.Clone();
        }

        public async Task<JsonElement> GetOpenAiFineTuneJobAsync(string jobId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                throw new ArgumentException("Job id is required.", nameof(jobId));
            }

            var apiKey = AiRuntime.GetOpenAiApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("OpenAI API key is not configured.");
            }

            using var client = CreateOpenAiClient(apiKey);
            using var response = await client.GetAsync($"https://api.openai.com/v1/fine_tuning/jobs/{Uri.EscapeDataString(jobId.Trim())}", ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"OpenAI fine-tuning job lookup failed: {body}");
            }

            using var json = JsonDocument.Parse(body);
            return json.RootElement.Clone();
        }

        private static HttpClient CreateOpenAiClient(string apiKey)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return client;
        }

        private static object BuildLearnerGuideTrainingExample(
            SourceMaterial material,
            string sourceExcerpt,
            FineTuningDatasetRequest request)
        {
            var topic = FirstNonEmpty(
                material.TopicDescription,
                material.AssessmentCriteriaDescription,
                material.SubjectDescription,
                material.Title,
                "selected curriculum topic");
            var user = $"""
            Write full learner-guide instructional content for the following curriculum topic.

            Target topic: {topic}
            Curriculum requirement: Explain the concepts fully so a learner can study from this section without another textbook.
            Required structure: Concept explanation, key terms with real definitions, how it works, worked example or case study, common mistakes, and short check-for-understanding questions.

            Reference material:
            {sourceExcerpt}
            """;

            var assistant = BuildAssistantTarget(topic, sourceExcerpt, request);
            return new
            {
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "You are an expert technical textbook author and instructional designer. You write full learner-guide content, not syllabus summaries."
                    },
                    new { role = "user", content = user },
                    new { role = "assistant", content = assistant }
                }
            };
        }

        private static string BuildAssistantTarget(string topic, string sourceExcerpt, FineTuningDatasetRequest request)
        {
            var normalized = CleanText(sourceExcerpt);
            var paragraphs = SplitIntoParagraphs(normalized).Take(8).ToList();
            var conceptText = string.Join("\n\n", paragraphs);
            var terms = ExtractKeyTerms(topic + " " + normalized).Take(8).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"# {topic}");
            sb.AppendLine();
            sb.AppendLine("## Concept Explanation");
            sb.AppendLine(conceptText);
            sb.AppendLine();
            sb.AppendLine("## Key Terms");
            foreach (var term in terms)
            {
                sb.AppendLine($"- {term}: A core concept in this topic. Explain it by describing what it is, what it does, why it matters, and how it is recognised in practical work.");
            }
            sb.AppendLine();
            sb.AppendLine("## How It Works");
            sb.AppendLine("The process should be explained step by step, linking each part of the concept to its practical purpose, the conditions required for correct operation, and the consequences when it is neglected or incorrectly applied.");
            sb.AppendLine();
            sb.AppendLine("## Worked Example");
            sb.AppendLine("In a workplace or training scenario, identify the relevant system, inspect the available evidence, explain the cause-and-effect relationship, and describe the correct action using the terminology from this topic.");
            sb.AppendLine();
            sb.AppendLine("## Common Mistakes");
            sb.AppendLine("- Listing the topic name without explaining the underlying principle.");
            sb.AppendLine("- Giving generic definitions that do not connect to the practical task.");
            sb.AppendLine("- Omitting safety, maintenance, diagnostic, or quality implications where they are relevant.");
            sb.AppendLine();
            sb.AppendLine("## Check Your Understanding");
            sb.AppendLine("1. Explain the main purpose of this topic in your own words.");
            sb.AppendLine("2. Describe one practical example where this knowledge is applied.");
            sb.AppendLine("3. Identify one fault, error, or risk that can occur if the concept is misunderstood.");
            return sb.ToString().Trim();
        }

        private static IEnumerable<string> SplitIntoChunks(string text, int maxChars)
        {
            var paragraphs = SplitIntoParagraphs(text);
            var sb = new StringBuilder();
            foreach (var paragraph in paragraphs)
            {
                if (sb.Length + paragraph.Length + 2 > maxChars && sb.Length > 0)
                {
                    yield return sb.ToString().Trim();
                    sb.Clear();
                }

                sb.AppendLine(paragraph);
                sb.AppendLine();
            }

            if (sb.Length > 0)
            {
                yield return sb.ToString().Trim();
            }
        }

        private static IEnumerable<string> SplitIntoParagraphs(string text)
        {
            return Regex.Split(text ?? string.Empty, @"\n\s*\n|(?<=[.!?])\s+(?=[A-Z0-9])")
                .Select(x => CleanText(x))
                .Where(x => x.Length >= 80);
        }

        private static IEnumerable<string> ExtractKeyTerms(string text)
        {
            var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "and", "with", "from", "that", "this", "must", "will", "shall", "into", "their", "there", "where", "when", "what", "which", "using", "used", "learn", "topic", "content"
            };
            return Regex.Matches(text ?? string.Empty, @"\b[A-Za-z][A-Za-z\-]{4,}\b")
                .Select(m => m.Value.Trim('-', ' '))
                .Where(x => !stop.Contains(x))
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => CultureTerm(g.Key));
        }

        private static string CultureTerm(string value)
            => string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

        private static string CleanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value.Replace("\r\n", "\n").Replace('\r', '\n');
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"\n{4,}", "\n\n\n");
            return text.Trim();
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }
    }

    public sealed class FineTuningDatasetRequest
    {
        public string? QualificationCode { get; set; }
        public string? QualificationDescription { get; set; }
        public List<string> KnowledgeSourceTypes { get; set; } = new()
        {
            "local_source_upload",
            "continuous_github_dataset",
            "continuous_hf_dataset"
        };
        public int MaxMaterials { get; set; } = 80;
        public int MaxExamplesPerMaterial { get; set; } = 2;
        public int MaxSourceCharsPerExample { get; set; } = 5000;
    }

    public sealed class FineTuningDatasetResult
    {
        public string JsonlPath { get; set; } = string.Empty;
        public string ManifestPath { get; set; } = string.Empty;
        public int ExampleCount { get; set; }
        public int MaterialCount { get; set; }
        public bool ReadyForProviderUpload { get; set; }
        public string Warning { get; set; } = string.Empty;
    }

    public sealed class FineTuningDatasetManifest
    {
        public DateTime CreatedAtUtc { get; set; }
        public string JsonlPath { get; set; } = string.Empty;
        public int ExampleCount { get; set; }
        public int MaterialCount { get; set; }
        public string QualificationCode { get; set; } = string.Empty;
        public string QualificationDescription { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public sealed class OpenAiFineTuneJobRequest
    {
        public string TrainingJsonlPath { get; set; } = string.Empty;
        public string? Model { get; set; }
        public string? Suffix { get; set; }
    }
}
