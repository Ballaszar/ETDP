using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Services
{
    public sealed class ContinuousLearningIngestionWorker : BackgroundService
    {
        private const string GithubSourceType = "continuous_github_dataset";
        private const string HuggingFaceSourceType = "continuous_hf_dataset";
        private const int MaxStoredTextChars = 240_000;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(90)
        };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ContinuousLearningIngestionWorker> _logger;
        private readonly SemaphoreSlim _runGate = new(1, 1);
        private readonly string _rootPath;
        private readonly string _configPath;
        private readonly string _statePath;
        private readonly string _githubRoot;
        private readonly string _cacheRoot;
        private ContinuousLearningStatus _status = new();
        private bool _runRequested;
        private bool _started;

        public ContinuousLearningIngestionWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<ContinuousLearningIngestionWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _rootPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ETDP",
                "ContinuousLearning");
            _configPath = Path.Combine(_rootPath, "continuous-learning-sources.json");
            _statePath = Path.Combine(_rootPath, "continuous-learning-state.json");
            _githubRoot = Path.Combine(_rootPath, "github");
            _cacheRoot = Path.Combine(_rootPath, "cache");
        }

        public ContinuousLearningStatus GetStatus()
        {
            HydrateStatusFromDisk();
            lock (_status)
            {
                return _status with
                {
                    RecentMessages = _status.RecentMessages.ToList(),
                    Pipelines = _status.Pipelines
                        .Select(x => new ContinuousLearningPipelineStatus
                        {
                            Key = x.Key,
                            Name = x.Name,
                            SourceType = x.SourceType,
                            State = x.State,
                            Processed = x.Processed,
                            Total = x.Total,
                            Created = x.Created,
                            Percentage = x.Percentage,
                            Message = x.Message,
                            LastSyncedAtUtc = x.LastSyncedAtUtc,
                            UpdatedAtUtc = x.UpdatedAtUtc
                        })
                        .ToList()
                };
            }
        }

        public async Task<ContinuousLearningConfig> GetConfigAsync(CancellationToken ct = default)
        {
            EnsureDirectories();
            if (!File.Exists(_configPath))
            {
                var defaults = ContinuousLearningConfig.CreateDefault();
                await SaveConfigAsync(defaults, ct);
                return defaults;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_configPath, ct);
                return JsonSerializer.Deserialize<ContinuousLearningConfig>(json, JsonOptions) ?? ContinuousLearningConfig.CreateDefault();
            }
            catch
            {
                return ContinuousLearningConfig.CreateDefault();
            }
        }

        public async Task SaveConfigAsync(ContinuousLearningConfig config, CancellationToken ct = default)
        {
            EnsureDirectories();
            config.UpdatedAtUtc = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(_configPath, json, ct);
        }

        public Task RequestRunAsync()
        {
            _runRequested = true;
            lock (_status)
            {
                _status.RunRequested = true;
                _status.WorkerOnline = _started;
                _status.ProgressPercentage = 0;
            }
            SetStatusMessage("Manual continuous learning run requested.");
            return Task.CompletedTask;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            EnsureDirectories();
            await GetConfigAsync(stoppingToken);
            _started = true;
            lock (_status)
            {
                _status.WorkerOnline = true;
            }
            SetStatusMessage("Continuous learning worker is online.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var config = await GetConfigAsync(stoppingToken);
                    var enabledByEnv = !string.Equals(
                        Environment.GetEnvironmentVariable("ETDP_CONTINUOUS_LEARNING_ENABLED"),
                        "false",
                        StringComparison.OrdinalIgnoreCase);

                    var shouldRunScheduled = config.Enabled && enabledByEnv && ShouldRun(config);
                    var shouldRunManual = _runRequested;
                    if (shouldRunScheduled || shouldRunManual)
                    {
                        _runRequested = false;
                        lock (_status)
                        {
                            _status.RunRequested = false;
                        }
                        await RunOnceAsync(config, stoppingToken);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(_runRequested ? 2 : 5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Continuous learning worker loop error.");
                    SetStatusFailure(ex.Message);
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        public async Task<ContinuousLearningStatus> RunOnceAsync(ContinuousLearningConfig config, CancellationToken ct = default)
        {
            if (!await _runGate.WaitAsync(0, ct))
            {
                SetStatusMessage("Continuous learning run is already active.");
                return GetStatus();
            }

            try
            {
                var state = await LoadStateAsync(ct);
                var started = DateTime.UtcNow;
                SetRunning(true, started);
                var enabledGithubSources = config.GitHubSources.Where(s => s.Enabled).ToList();
                var enabledHuggingFaceSources = config.HuggingFaceSources.Where(s => s.Enabled).ToList();
                var totalSources = enabledGithubSources.Count + enabledHuggingFaceSources.Count;
                SetRunProgress(totalSources, 0, string.Empty, string.Empty, 0, 0, 0);
                foreach (var source in enabledGithubSources)
                {
                    SetPipelineStatus(source.Url, source.Name, "GitHub", "queued", 0, 0, 0, 0, string.Empty);
                }
                foreach (var source in enabledHuggingFaceSources)
                {
                    SetPipelineStatus(source.Key, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", "queued", 0, 0, 0, 0, string.Empty);
                }

                var githubCreated = 0;
                var hfCreated = 0;
                var completedSources = 0;
                foreach (var source in enabledGithubSources)
                {
                    ct.ThrowIfCancellationRequested();
                    SetRunProgress(totalSources, completedSources, source.Name, "GitHub", 0, 0, 0);
                    SetPipelineStatus(source.Url, source.Name, "GitHub", "running", 0, 0, 0, 0, string.Empty);
                    githubCreated += await IngestGitHubSourceAsync(source, config, state, ct);
                    completedSources++;
                    SetRunProgress(totalSources, completedSources, source.Name, "GitHub", 0, 0, 0);
                    await SaveStateAsync(state, ct);
                }

                foreach (var source in enabledHuggingFaceSources)
                {
                    ct.ThrowIfCancellationRequested();
                    SetRunProgress(totalSources, completedSources, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", 0, 0, 0);
                    SetPipelineStatus(source.Key, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", "running", 0, 0, 0, 0, string.Empty);
                    hfCreated += await IngestHuggingFaceSourceAsync(source, config, state, ct);
                    completedSources++;
                    SetRunProgress(totalSources, completedSources, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", 0, 0, 0);
                    await SaveStateAsync(state, ct);
                }

                state.LastRunStartedAtUtc = started;
                state.LastRunCompletedAtUtc = DateTime.UtcNow;
                state.TotalCreated += githubCreated + hfCreated;
                await SaveStateAsync(state, ct);

                SetCompleted(githubCreated + hfCreated, githubCreated, hfCreated);
                return GetStatus();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Continuous learning run failed.");
                SetStatusFailure(ex.Message);
                return GetStatus();
            }
            finally
            {
                SetRunning(false, null);
                _runGate.Release();
            }
        }

        private async Task<int> IngestGitHubSourceAsync(
            ContinuousLearningGitHubSource source,
            ContinuousLearningConfig config,
            ContinuousLearningState state,
            CancellationToken ct)
        {
            var repoKey = SafeKey(source.Url);
            var repoPath = Path.Combine(_githubRoot, repoKey);
            SetStatusMessage($"Syncing GitHub dataset {source.Url}");
            var sync = Directory.Exists(Path.Combine(repoPath, ".git"))
                ? await RunProcessAsync("git", $"-C \"{repoPath}\" pull --ff-only", TimeSpan.FromMinutes(3), ct)
                : await RunProcessAsync("git", $"clone --depth 1 \"{source.Url}\" \"{repoPath}\"", TimeSpan.FromMinutes(8), ct);

            if (sync.ExitCode != 0)
            {
                SetStatusMessage($"GitHub sync failed for {source.Url}: {Trim(sync.StdErr, 500)}");
                SetPipelineStatus(source.Url, source.Name, "GitHub", "failed", 0, 0, 0, 0, Trim(sync.StdErr, 500));
                return 0;
            }

            var included = new HashSet<string>(config.FileExtensions.Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()));
            var files = Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => included.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(config.MaxGitHubFilesPerSourcePerRun)
                .ToList();
            SetRunProgress(null, null, source.Name, "GitHub", 0, files.Count, 0);
            SetPipelineStatus(source.Url, source.Name, "GitHub", "running", 0, files.Count, 0, 0, string.Empty);

            var created = 0;
            var processed = 0;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existingUrls = await db.SourceMaterials
                .AsNoTracking()
                .Where(x => x.KnowledgeSourceType == GithubSourceType && x.Url.StartsWith(source.Url))
                .Select(x => x.Url)
                .ToListAsync(ct);
            var existing = new HashSet<string>(existingUrls, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                processed++;
                var relPath = Path.GetRelativePath(repoPath, file).Replace('\\', '/');
                var fileHash = await ComputeFileHashAsync(file, ct);
                var materialUrl = $"{source.Url}::blob::{relPath}::sha256:{fileHash}";
                if (existing.Contains(materialUrl))
                {
                    SetPipelineStatus(source.Url, source.Name, "GitHub", "running", processed, files.Count, created, Percent(processed, files.Count), string.Empty);
                    continue;
                }

                var text = await ExtractFileTextAsync(file, ct);
                if (IsTooThin(text))
                {
                    SetPipelineStatus(source.Url, source.Name, "GitHub", "running", processed, files.Count, created, Percent(processed, files.Count), "Skipped a file with too little extractable text.");
                    continue;
                }

                db.SourceMaterials.Add(new SourceMaterial
                {
                    Title = $"Continuous GitHub Dataset - {source.Name} - {relPath}",
                    FileName = Path.GetFileName(file),
                    FilePath = file,
                    FileType = Path.GetExtension(file).TrimStart('.').ToLowerInvariant(),
                    Url = materialUrl,
                    QualificationCode = source.QualificationCode,
                    QualificationDescription = source.QualificationDescription,
                    SubjectDescription = source.SubjectDescription,
                    TopicDescription = string.IsNullOrWhiteSpace(source.TopicDescription) ? $"GitHub:{source.Name}" : source.TopicDescription,
                    AssessmentCriteriaDescription = $"GitHubPath:{relPath}",
                    KnowledgeSourceType = GithubSourceType,
                    KnowledgeLabel = $"ContinuousLearning::{source.Name}",
                    KnowledgeRootPath = _rootPath,
                    KnowledgeUploadedAtUtc = DateTime.UtcNow,
                    ExtractedText = Trim(text, config.MaxTextCharsPerMaterial),
                    CreatedAt = DateTime.UtcNow
                });
                existing.Add(materialUrl);
                created++;

                if (created % 25 == 0)
                {
                    await db.SaveChangesAsync(ct);
                    SetStatusMessage($"GitHub {source.Name}: ingested {created} new files.");
                }

                SetRunProgress(null, null, source.Name, "GitHub", processed, files.Count, created);
                SetPipelineStatus(source.Url, source.Name, "GitHub", "running", processed, files.Count, created, Percent(processed, files.Count), string.Empty);
            }

            await db.SaveChangesAsync(ct);
            state.GitHub[source.Url] = new ContinuousLearningGitHubState
            {
                LastSyncAtUtc = DateTime.UtcNow,
                LastKnownPath = files.LastOrDefault() ?? string.Empty
            };
            SetStatusMessage($"GitHub {source.Name}: created {created} new knowledge records.");
            SetRunProgress(null, null, source.Name, "GitHub", files.Count, files.Count, created);
            SetPipelineStatus(source.Url, source.Name, "GitHub", "completed", files.Count, files.Count, created, 100, string.Empty);
            return created;
        }

        private async Task<int> IngestHuggingFaceSourceAsync(
            ContinuousLearningHuggingFaceSource source,
            ContinuousLearningConfig config,
            ContinuousLearningState state,
            CancellationToken ct)
        {
            var sourceKey = source.Key;
            state.HuggingFace.TryGetValue(sourceKey, out var sourceState);
            sourceState ??= new ContinuousLearningHuggingFaceState();

            var split = string.IsNullOrWhiteSpace(source.Split) ? "train" : source.Split.Trim();
            var offset = Math.Max(0, sourceState.Offset);
            var length = Math.Clamp(source.RowsPerRun ?? config.MaxHuggingFaceRowsPerSourcePerRun, 1, 100);
            var url = BuildHuggingFaceRowsUrl(source, split, offset, length);
            SetStatusMessage($"Reading Hugging Face dataset {source.Dataset}/{source.ConfigName} at offset {offset}.");

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var hfToken = ResolveHuggingFaceToken();
            if (!string.IsNullOrWhiteSpace(hfToken))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", hfToken.Trim());
            }

            using var response = await Http.SendAsync(req, ct);
            if (!response.IsSuccessStatusCode)
            {
                SetStatusMessage($"Hugging Face read skipped for {source.Dataset}: {(int)response.StatusCode} {response.ReasonPhrase}");
                SetPipelineStatus(sourceKey, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", "failed", 0, length, 0, 0, $"{(int)response.StatusCode} {response.ReasonPhrase}");
                return 0;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                SetStatusMessage($"Hugging Face {source.Dataset}: no rows returned.");
                SetPipelineStatus(sourceKey, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", "skipped", 0, 0, 0, 0, "No rows returned.");
                return 0;
            }

            var created = 0;
            var processed = 0;
            var totalRows = rows.GetArrayLength();
            SetRunProgress(null, null, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", 0, totalRows, 0);
            SetPipelineStatus(sourceKey, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", "running", 0, totalRows, 0, 0, string.Empty);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            foreach (var item in rows.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                processed++;
                var rowIndex = item.TryGetProperty("row_idx", out var rowIdxElement) && rowIdxElement.TryGetInt64(out var rowIdx)
                    ? rowIdx
                    : offset + created;
                var row = item.TryGetProperty("row", out var rowElement) ? rowElement : item;
                var materialUrl = $"hf://datasets/{source.Dataset}/{source.ConfigName}/{split}/row/{rowIndex}";
                var exists = await db.SourceMaterials.AnyAsync(x => x.Url == materialUrl && x.KnowledgeSourceType == HuggingFaceSourceType, ct);
                if (exists)
                {
                    SetPipelineStatus(sourceKey, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", "running", processed, totalRows, created, Percent(processed, totalRows), string.Empty);
                    continue;
                }

                var text = BuildTextFromDatasetRow(row, source);
                if (IsTooThin(text))
                {
                    SetPipelineStatus(sourceKey, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", "running", processed, totalRows, created, Percent(processed, totalRows), "Skipped a row with too little extractable text.");
                    continue;
                }

                db.SourceMaterials.Add(new SourceMaterial
                {
                    Title = $"Continuous HF Dataset - {source.Dataset} - {source.ConfigName} - row {rowIndex}",
                    FileName = $"{SafeKey(source.Dataset)}-{SafeKey(source.ConfigName)}-{split}-{rowIndex}.json",
                    FilePath = Path.Combine(_cacheRoot, "hf", SafeKey(source.Dataset), SafeKey(source.ConfigName), split),
                    FileType = "json",
                    Url = materialUrl,
                    QualificationCode = source.QualificationCode,
                    QualificationDescription = source.QualificationDescription,
                    SubjectDescription = source.SubjectDescription,
                    TopicDescription = string.IsNullOrWhiteSpace(source.TopicDescription) ? $"Dataset:{source.Dataset}/{source.ConfigName}" : source.TopicDescription,
                    AssessmentCriteriaDescription = $"HuggingFaceRow:{rowIndex}",
                    KnowledgeSourceType = HuggingFaceSourceType,
                    KnowledgeLabel = $"ContinuousLearning::{source.Dataset}",
                    KnowledgeRootPath = _rootPath,
                    KnowledgeUploadedAtUtc = DateTime.UtcNow,
                    ExtractedText = Trim(text, config.MaxTextCharsPerMaterial),
                    CreatedAt = DateTime.UtcNow
                });
                created++;
                SetRunProgress(null, null, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", processed, totalRows, created);
                SetPipelineStatus(sourceKey, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", "running", processed, totalRows, created, Percent(processed, totalRows), string.Empty);
            }

            await db.SaveChangesAsync(ct);
            sourceState.Offset = offset + length;
            sourceState.LastSyncAtUtc = DateTime.UtcNow;
            state.HuggingFace[sourceKey] = sourceState;
            SetStatusMessage($"Hugging Face {source.Dataset}/{source.ConfigName}: created {created} new rows.");
            SetRunProgress(null, null, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", totalRows, totalRows, created);
            SetPipelineStatus(sourceKey, $"{source.Dataset}/{source.ConfigName}", "Hugging Face", "completed", totalRows, totalRows, created, 100, string.Empty);
            return created;
        }

        private static string BuildHuggingFaceRowsUrl(ContinuousLearningHuggingFaceSource source, string split, int offset, int length)
        {
            var dataset = Uri.EscapeDataString(source.Dataset);
            var config = Uri.EscapeDataString(source.ConfigName);
            var splitValue = Uri.EscapeDataString(split);
            return $"https://datasets-server.huggingface.co/rows?dataset={dataset}&config={config}&split={splitValue}&offset={offset}&length={length}";
        }

        private static string ResolveHuggingFaceToken()
        {
            var envToken = Environment.GetEnvironmentVariable("HF_TOKEN")
                ?? Environment.GetEnvironmentVariable("HUGGINGFACE_TOKEN");
            if (!string.IsNullOrWhiteSpace(envToken))
            {
                return envToken.Trim();
            }

            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("HF_TOKEN_FILE"),
                Environment.GetEnvironmentVariable("HUGGINGFACE_TOKEN_FILE"),
                @"D:\ETDP\hugingfacetoken.md",
                @"D:\ETDP\huggingfacetoken.md",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETDP", "huggingface.token")
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                try
                {
                    var path = Path.GetFullPath(candidate.Trim().Trim('"'));
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var token = File.ReadLines(path)
                        .Select(line => line.Trim())
                        .FirstOrDefault(line =>
                            !string.IsNullOrWhiteSpace(line) &&
                            !line.StartsWith("#", StringComparison.Ordinal) &&
                            !line.StartsWith("```", StringComparison.Ordinal));
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        return token.Trim();
                    }
                }
                catch
                {
                    // Token file fallback is best-effort only.
                }
            }

            return string.Empty;
        }

        private static string BuildTextFromDatasetRow(JsonElement row, ContinuousLearningHuggingFaceSource source)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Dataset: {source.Dataset}");
            sb.AppendLine($"Config: {source.ConfigName}");
            AppendPreferredFields(sb, row);

            var raw = row.GetRawText();
            if (sb.Length < 500)
            {
                sb.AppendLine();
                sb.AppendLine("Raw structured row:");
                sb.AppendLine(raw);
            }

            return CleanText(sb.ToString());
        }

        private static void AppendPreferredFields(StringBuilder sb, JsonElement row)
        {
            var names = new[]
            {
                "title", "text", "content", "document", "passage", "context",
                "instruction", "input", "question", "answer", "answers",
                "output", "response", "chosen", "rejected", "rationale",
                "explanation", "solution", "conversations", "messages"
            };

            foreach (var name in names)
            {
                if (!row.TryGetProperty(name, out var value))
                {
                    continue;
                }

                var text = JsonValueToText(value);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                sb.AppendLine();
                sb.AppendLine(name.ToUpperInvariant() + ":");
                sb.AppendLine(text);
            }
        }

        private static string JsonValueToText(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
                JsonValueKind.Array => string.Join(Environment.NewLine, value.EnumerateArray().Select(JsonValueToText).Where(x => !string.IsNullOrWhiteSpace(x))),
                JsonValueKind.Object => string.Join(Environment.NewLine, value.EnumerateObject().Select(p => $"{p.Name}: {JsonValueToText(p.Value)}").Where(x => !string.IsNullOrWhiteSpace(x))),
                _ => string.Empty
            };
        }

        private async Task<string> ExtractFileTextAsync(string path, CancellationToken ct)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".pdf")
            {
                return CleanText(ExtractPdfText(path));
            }

            if (ext == ".docx")
            {
                return CleanText(ExtractDocxText(path));
            }

            if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".tif" or ".tiff")
            {
                return string.Empty;
            }

            var info = new FileInfo(path);
            if (info.Length > 20_000_000)
            {
                return string.Empty;
            }

            return CleanText(await File.ReadAllTextAsync(path, ct));
        }

        private static string ExtractPdfText(string path)
        {
            try
            {
                using var reader = new PdfReader(path);
                using var pdf = new PdfDocument(reader);
                var sb = new StringBuilder();
                var pages = pdf.GetNumberOfPages();
                for (var i = 1; i <= pages; i++)
                {
                    sb.AppendLine($"[Page {i}]");
                    sb.AppendLine(PdfTextExtractor.GetTextFromPage(pdf.GetPage(i)));
                    sb.AppendLine();
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractDocxText(string path)
        {
            try
            {
                using var doc = WordprocessingDocument.Open(path, false);
                return doc.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static async Task<string> ComputeFileHashAsync(string path, CancellationToken ct)
        {
            await using var stream = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
            string fileName,
            string arguments,
            TimeSpan timeout,
            CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stdErrTask = process.StandardError.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutCts.Token);
            return (process.ExitCode, await stdOutTask, await stdErrTask);
        }

        private bool ShouldRun(ContinuousLearningConfig config)
        {
            var status = GetStatus();
            if (status.LastCompletedAtUtc == null)
            {
                return true;
            }

            var hours = Math.Clamp(config.IntervalHours, 1, 168);
            return DateTime.UtcNow - status.LastCompletedAtUtc.Value >= TimeSpan.FromHours(hours);
        }

        private async Task<ContinuousLearningState> LoadStateAsync(CancellationToken ct)
        {
            if (!File.Exists(_statePath))
            {
                return new ContinuousLearningState();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_statePath, ct);
                return JsonSerializer.Deserialize<ContinuousLearningState>(json, JsonOptions) ?? new ContinuousLearningState();
            }
            catch
            {
                return new ContinuousLearningState();
            }
        }

        private async Task SaveStateAsync(ContinuousLearningState state, CancellationToken ct)
        {
            EnsureDirectories();
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(_statePath, json, ct);
        }

        private void HydrateStatusFromDisk()
        {
            if (_status.IsRunning)
            {
                return;
            }

            ContinuousLearningConfig? config = null;
            ContinuousLearningState? state = null;

            try
            {
                if (File.Exists(_configPath))
                {
                    var configJson = File.ReadAllText(_configPath);
                    config = JsonSerializer.Deserialize<ContinuousLearningConfig>(configJson, JsonOptions);
                }
            }
            catch
            {
                config = null;
            }

            try
            {
                if (File.Exists(_statePath))
                {
                    var stateJson = File.ReadAllText(_statePath);
                    state = JsonSerializer.Deserialize<ContinuousLearningState>(stateJson, JsonOptions);
                }
            }
            catch
            {
                state = null;
            }

            config ??= ContinuousLearningConfig.CreateDefault();
            state ??= new ContinuousLearningState();
            var now = DateTime.UtcNow;

            lock (_status)
            {
                if (_status.IsRunning)
                {
                    return;
                }

                _status.WorkerOnline = _started;
                _status.LastStartedAtUtc ??= state.LastRunStartedAtUtc;
                _status.LastCompletedAtUtc ??= state.LastRunCompletedAtUtc;
                _status.TotalSources = config.GitHubSources
                    .Where(x => x.Enabled)
                    .Select(x => x.Url)
                    .Concat(config.HuggingFaceSources.Where(x => x.Enabled).Select(x => x.Key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                _status.CompletedSources = state.LastRunCompletedAtUtc.HasValue ? _status.TotalSources : 0;
                _status.ProgressPercentage = _status.TotalSources > 0 && _status.CompletedSources == _status.TotalSources ? 100 : _status.ProgressPercentage;

                foreach (var source in config.GitHubSources.Where(x => x.Enabled))
                {
                    var key = source.Url;
                    var existing = _status.Pipelines.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        existing = new ContinuousLearningPipelineStatus { Key = key };
                        _status.Pipelines.Add(existing);
                    }

                    state.GitHub.TryGetValue(key, out var sourceState);
                    existing.Name = source.Name;
                    existing.SourceType = "GitHub";
                    existing.State = sourceState?.LastSyncAtUtc == null ? "not started" : "last batch completed";
                    existing.Processed = sourceState?.LastSyncAtUtc == null ? 0 : Math.Max(1, config.MaxGitHubFilesPerSourcePerRun);
                    existing.Total = Math.Max(1, config.MaxGitHubFilesPerSourcePerRun);
                    existing.Percentage = sourceState?.LastSyncAtUtc == null ? 0 : 100;
                    existing.Message = sourceState?.LastSyncAtUtc == null
                        ? $"Configured. Up to {config.MaxGitHubFilesPerSourcePerRun} files will be inspected per run."
                        : $"Last synced {sourceState.LastSyncAtUtc:u}. Per-run cap: {config.MaxGitHubFilesPerSourcePerRun} files.";
                    existing.LastSyncedAtUtc = sourceState?.LastSyncAtUtc;
                    existing.UpdatedAtUtc = sourceState?.LastSyncAtUtc ?? now;
                }

                foreach (var source in config.HuggingFaceSources.Where(x => x.Enabled))
                {
                    var key = source.Key;
                    var existing = _status.Pipelines.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        existing = new ContinuousLearningPipelineStatus { Key = key };
                        _status.Pipelines.Add(existing);
                    }

                    state.HuggingFace.TryGetValue(key, out var sourceState);
                    var batchSize = Math.Max(1, source.RowsPerRun ?? config.MaxHuggingFaceRowsPerSourcePerRun);
                    existing.Name = $"{source.Dataset}/{source.ConfigName}";
                    existing.SourceType = "Hugging Face";
                    existing.State = sourceState?.LastSyncAtUtc == null ? "not started" : "last batch completed";
                    existing.Processed = sourceState?.Offset ?? 0;
                    existing.Total = Math.Max(batchSize, sourceState?.Offset ?? 0);
                    existing.Percentage = sourceState?.LastSyncAtUtc == null ? 0 : 100;
                    existing.Message = sourceState?.LastSyncAtUtc == null
                        ? $"Configured. Next run will fetch {batchSize} rows from {source.Split}."
                        : $"Cumulative rows fetched: {sourceState.Offset}. Last batch size: {batchSize}. Last synced {sourceState.LastSyncAtUtc:u}.";
                    existing.LastSyncedAtUtc = sourceState?.LastSyncAtUtc;
                    existing.UpdatedAtUtc = sourceState?.LastSyncAtUtc ?? now;
                }
            }
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(_rootPath);
            Directory.CreateDirectory(_githubRoot);
            Directory.CreateDirectory(_cacheRoot);
        }

        private void SetRunning(bool running, DateTime? startedAtUtc)
        {
            lock (_status)
            {
                _status.IsRunning = running;
                _status.WorkerOnline = _started;
                if (startedAtUtc.HasValue)
                {
                    _status.LastStartedAtUtc = startedAtUtc.Value;
                    _status.LastError = string.Empty;
                    _status.RunRequested = false;
                }
            }
        }

        private void SetCompleted(int totalCreated, int githubCreated, int hfCreated)
        {
            lock (_status)
            {
                _status.LastCompletedAtUtc = DateTime.UtcNow;
                _status.LastCreated = totalCreated;
                _status.LastGitHubCreated = githubCreated;
                _status.LastHuggingFaceCreated = hfCreated;
                _status.ProgressPercentage = 100;
                _status.CurrentSourceName = string.Empty;
                _status.CurrentSourceType = string.Empty;
                _status.CurrentSourceProcessed = 0;
                _status.CurrentSourceTotal = 0;
                _status.CurrentSourceCreated = 0;
                _status.LastError = string.Empty;
            }
            SetStatusMessage($"Continuous learning completed. New records: {totalCreated}.");
        }

        private void SetRunProgress(
            int? totalSources,
            int? completedSources,
            string currentSourceName,
            string currentSourceType,
            int currentProcessed,
            int currentTotal,
            int currentCreated)
        {
            lock (_status)
            {
                if (totalSources.HasValue)
                {
                    _status.TotalSources = totalSources.Value;
                }

                if (completedSources.HasValue)
                {
                    _status.CompletedSources = completedSources.Value;
                }

                _status.CurrentSourceName = currentSourceName ?? string.Empty;
                _status.CurrentSourceType = currentSourceType ?? string.Empty;
                _status.CurrentSourceProcessed = currentProcessed;
                _status.CurrentSourceTotal = currentTotal;
                _status.CurrentSourceCreated = currentCreated;

                var sourcePercent = _status.TotalSources <= 0
                    ? 0
                    : _status.CompletedSources * 100.0 / _status.TotalSources;
                var currentSourceShare = _status.TotalSources <= 0
                    ? 0
                    : 100.0 / _status.TotalSources;
                var innerPercent = currentTotal <= 0 ? 0 : Math.Clamp(currentProcessed * 1.0 / currentTotal, 0, 1) * currentSourceShare;
                _status.ProgressPercentage = Math.Clamp((int)Math.Round(sourcePercent + innerPercent), 0, 100);
            }
        }

        private void SetPipelineStatus(
            string key,
            string name,
            string sourceType,
            string state,
            int processed,
            int total,
            int created,
            int percentage,
            string message)
        {
            key = string.IsNullOrWhiteSpace(key) ? $"{sourceType}:{name}" : key.Trim();
            lock (_status)
            {
                var existing = _status.Pipelines.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new ContinuousLearningPipelineStatus { Key = key };
                    _status.Pipelines.Add(existing);
                }

                existing.Name = name ?? string.Empty;
                existing.SourceType = sourceType ?? string.Empty;
                existing.State = state ?? string.Empty;
                existing.Processed = processed;
                existing.Total = total;
                existing.Created = created;
                existing.Percentage = Math.Clamp(percentage, 0, 100);
                existing.Message = message ?? string.Empty;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        private void SetStatusFailure(string error)
        {
            lock (_status)
            {
                _status.LastError = Trim(error, 1000);
                _status.IsRunning = false;
            }
            SetStatusMessage($"Continuous learning error: {Trim(error, 500)}");
        }

        private void SetStatusMessage(string message)
        {
            lock (_status)
            {
                _status.RecentMessages.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                if (_status.RecentMessages.Count > 30)
                {
                    _status.RecentMessages.RemoveRange(30, _status.RecentMessages.Count - 30);
                }
            }
            _logger.LogInformation("{Message}", message);
        }

        private static bool IsTooThin(string text)
            => string.IsNullOrWhiteSpace(text) || CleanText(text).Length < 120;

        private static int Percent(int value, int total)
            => total <= 0 ? 0 : Math.Clamp((int)Math.Round(value * 100.0 / total), 0, 100);

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

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var capped = Math.Clamp(max, 1, MaxStoredTextChars);
            return value.Length <= capped ? value : value[..capped];
        }

        private static string SafeKey(string value)
        {
            var key = Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9._-]+", "-");
            return key.Trim('-').Length == 0 ? "source" : key.Trim('-');
        }
    }

    public sealed record ContinuousLearningStatus
    {
        public bool IsRunning { get; set; }
        public bool WorkerOnline { get; set; }
        public bool RunRequested { get; set; }
        public DateTime? LastStartedAtUtc { get; set; }
        public DateTime? LastCompletedAtUtc { get; set; }
        public int LastCreated { get; set; }
        public int LastGitHubCreated { get; set; }
        public int LastHuggingFaceCreated { get; set; }
        public int TotalSources { get; set; }
        public int CompletedSources { get; set; }
        public int ProgressPercentage { get; set; }
        public string CurrentSourceName { get; set; } = string.Empty;
        public string CurrentSourceType { get; set; } = string.Empty;
        public int CurrentSourceProcessed { get; set; }
        public int CurrentSourceTotal { get; set; }
        public int CurrentSourceCreated { get; set; }
        public List<ContinuousLearningPipelineStatus> Pipelines { get; set; } = new();
        public string LastError { get; set; } = string.Empty;
        public List<string> RecentMessages { get; set; } = new();
    }

    public sealed class ContinuousLearningPipelineStatus
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public int Processed { get; set; }
        public int Total { get; set; }
        public int Created { get; set; }
        public int Percentage { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime? LastSyncedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public sealed class ContinuousLearningConfig
    {
        public bool Enabled { get; set; } = true;
        public int IntervalHours { get; set; } = 6;
        public int MaxGitHubFilesPerSourcePerRun { get; set; } = 120;
        public int MaxHuggingFaceRowsPerSourcePerRun { get; set; } = 25;
        public int MaxTextCharsPerMaterial { get; set; } = 120_000;
        public List<string> FileExtensions { get; set; } = new()
        {
            ".txt", ".md", ".pdf", ".docx", ".csv", ".json", ".jsonl", ".xml", ".yml", ".yaml", ".html", ".htm"
        };
        public List<ContinuousLearningGitHubSource> GitHubSources { get; set; } = new();
        public List<ContinuousLearningHuggingFaceSource> HuggingFaceSources { get; set; } = new();
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public static ContinuousLearningConfig CreateDefault()
        {
            return new ContinuousLearningConfig
            {
                GitHubSources = new List<ContinuousLearningGitHubSource>
                {
                    new("OUTCOME-BASED-EDUCATION-OBE-AUTOMATION", "https://github.com/galvinguy2002/OUTCOME-BASED-EDUCATION-OBE-AUTOMATION.git"),
                    new("awesome-instruction-learning", "https://github.com/RenzeLou/awesome-instruction-learning.git"),
                    new("OBE", "https://github.com/whomping-willow/OBE.git"),
                    new("awesome-curriculum-learning", "https://github.com/Openning07/awesome-curriculum-learning.git"),
                    new("easy-dataset", "https://github.com/ConardLi/easy-dataset.git"),
                    new("ednet", "https://github.com/riiid/ednet.git"),
                    new("EduData", "https://github.com/bigdata-ustc/EduData.git")
                },
                HuggingFaceSources = new List<ContinuousLearningHuggingFaceSource>
                {
                    new("HuggingFaceFW/fineweb-edu", "default") { RowsPerRun = 10 },
                    new("HuggingFaceFW/fineweb-edu", "CC-MAIN-2013-20") { RowsPerRun = 10 },
                    new("HuggingFaceFW/fineweb-edu", "CC-MAIN-2013-48") { RowsPerRun = 10 },
                    new("AI-MO/NuminaMath-CoT", "default"),
                    new("garage-bAInd/Open-Platypus", "default"),
                    new("davanstrien/reasoning-required", "default"),
                    new("tau/commonsense_qa", "default"),
                    new("rajpurkar/squad", "plain_text"),
                    new("stanfordnlp/coqa", "default"),
                    new("Aeala/ShareGPT_Vicuna_unfiltered", "default") { RowsPerRun = 10 }
                }
            };
        }
    }

    public sealed record ContinuousLearningGitHubSource(string Name, string Url)
    {
        public bool Enabled { get; set; } = true;
        public string? QualificationCode { get; set; }
        public string? QualificationDescription { get; set; }
        public string? SubjectDescription { get; set; }
        public string? TopicDescription { get; set; }
    }

    public sealed record ContinuousLearningHuggingFaceSource(string Dataset, string ConfigName)
    {
        public bool Enabled { get; set; } = true;
        public string Split { get; set; } = "train";
        public int? RowsPerRun { get; set; }
        public string? QualificationCode { get; set; }
        public string? QualificationDescription { get; set; }
        public string? SubjectDescription { get; set; }
        public string? TopicDescription { get; set; }
        [JsonIgnore]
        public string Key => $"{Dataset}/{ConfigName}/{Split}";
    }

    public sealed class ContinuousLearningState
    {
        public DateTime? LastRunStartedAtUtc { get; set; }
        public DateTime? LastRunCompletedAtUtc { get; set; }
        public int TotalCreated { get; set; }
        public Dictionary<string, ContinuousLearningGitHubState> GitHub { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ContinuousLearningHuggingFaceState> HuggingFace { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ContinuousLearningGitHubState
    {
        public DateTime? LastSyncAtUtc { get; set; }
        public string LastKnownPath { get; set; } = string.Empty;
    }

    public sealed class ContinuousLearningHuggingFaceState
    {
        public DateTime? LastSyncAtUtc { get; set; }
        public int Offset { get; set; }
    }
}
