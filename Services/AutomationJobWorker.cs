using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Services
{
    public class AutomationJobWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly CurriculumPipelineService _curriculumPipelineService;
        private readonly ILogger<AutomationJobWorker> _logger;

        public AutomationJobWorker(
            IServiceScopeFactory scopeFactory,
            CurriculumPipelineService curriculumPipelineService,
            ILogger<AutomationJobWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _curriculumPipelineService = curriculumPipelineService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var job = await db.AutomationJobs
                        .Where(x => x.Status == "Queued")
                        .OrderBy(x => x.RequestedAtUtc)
                        .FirstOrDefaultAsync(stoppingToken);

                    if (job == null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(4), stoppingToken);
                        continue;
                    }

                    job.Status = "Running";
                    job.StartedAtUtc = DateTime.UtcNow;
                    job.Error = null;
                    await db.SaveChangesAsync(stoppingToken);

                    var result = await ExecuteJobAsync(job, stoppingToken);

                    job.Status = result.ExitCode == 0 ? "Completed" : "Failed";
                    job.CompletedAtUtc = DateTime.UtcNow;
                    job.Log = Truncate(result.StdOut, 25000);
                    job.Error = string.IsNullOrWhiteSpace(result.StdErr) ? null : Truncate(result.StdErr, 12000);
                    job.OutputPath = result.OutputPath;
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Automation worker loop error.");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= max ? value : value[..max];
        }

        private async Task<(int ExitCode, string StdOut, string StdErr, string? OutputPath)> ExecuteJobAsync(AutomationJob job, CancellationToken ct)
        {
            if (!string.Equals(job.JobType, "build_qualification", StringComparison.OrdinalIgnoreCase))
            {
                return (1, "", $"Unsupported job type: {job.JobType}", null);
            }

            var cfg = ParseConfig(job.ConfigJson);
            var executionMode = string.IsNullOrWhiteSpace(cfg.ExecutionMode)
                ? "internal_pipeline"
                : cfg.ExecutionMode.Trim().ToLowerInvariant();

            if (!string.Equals(executionMode, "powershell", StringComparison.OrdinalIgnoreCase))
            {
                return await ExecuteInternalPipelineJobAsync(job, cfg, ct);
            }

            return await ExecutePowerShellJobAsync(job, cfg, ct);
        }

        private async Task<(int ExitCode, string StdOut, string StdErr, string? OutputPath)> ExecuteInternalPipelineJobAsync(
            AutomationJob job,
            BuildJobConfig cfg,
            CancellationToken ct)
        {
            var log = new StringBuilder();

            void AppendLog(string message)
            {
                log.Append('[')
                    .Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                    .Append("] ")
                    .AppendLine(message);
            }

            AppendLog($"Launching ETDP internal curriculum pipeline for qualification {job.QualificationId}.");
            if (cfg.RunImports || cfg.RunSeedWrite)
            {
                AppendLog("Legacy import/seed flags were supplied. The internal pipeline now imports linked subject matter and seeds lesson-plan draft LPN rows automatically.");
            }
            else
            {
                AppendLog("ETDP will import linked subject matter, map topic evidence, and generate lesson-plan draft LPN rows automatically.");
            }

            CurriculumPipelineService.CurriculumPipelineJob pipelineJob;
            try
            {
                pipelineJob = await _curriculumPipelineService.QueueQualificationAsync(
                    job.QualificationId,
                    cfg.StartPage,
                    cfg.ForceRestart,
                    ct);
            }
            catch (Exception ex)
            {
                return (1, log.ToString(), $"Failed to queue internal curriculum pipeline: {ex.Message}", null);
            }

            AppendLog($"Internal pipeline job id: {pipelineJob.Id}");

            var lastStageKey = string.Empty;
            var lastProgress = -1;

            while (!ct.IsCancellationRequested)
            {
                var current = await _curriculumPipelineService.GetJobAsync(pipelineJob.Id, ct);
                if (current == null)
                {
                    return (1, log.ToString(), $"Internal curriculum pipeline job disappeared: {pipelineJob.Id}", pipelineJob.JobFolder);
                }

                var currentStageKey = current.CurrentStage ?? string.Empty;
                if (!string.Equals(currentStageKey, lastStageKey, StringComparison.OrdinalIgnoreCase) ||
                    current.ProgressPercent != lastProgress)
                {
                    lastStageKey = currentStageKey;
                    lastProgress = current.ProgressPercent;
                    var currentStage = current.Stages.FirstOrDefault(stage =>
                        string.Equals(stage.Key, currentStageKey, StringComparison.OrdinalIgnoreCase));
                    var detail = string.IsNullOrWhiteSpace(currentStage?.Detail)
                        ? currentStageKey
                        : currentStage!.Detail;
                    var stageLabel = string.IsNullOrWhiteSpace(currentStageKey)
                        ? "Queued"
                        : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(currentStageKey.Replace("-", " "));
                    AppendLog($"{stageLabel} | {current.ProgressPercent}% | {detail}");
                }

                var status = (current.Status ?? string.Empty).Trim().ToLowerInvariant();
                if (status is "completed" or "failed")
                {
                    var delivery = current.Artifacts?.DeliveryPilot;
                    if (delivery != null)
                    {
                        AppendLog($"Topic evidence map: {delivery.TopicsMappedCount}/{delivery.TopicCount} topics and {delivery.CriteriaMappedCount}/{delivery.CriteriaCount} criteria.");
                        AppendLog($"Lesson-plan draft LPN rows: created {delivery.LessonPlanDraftsCreated}, updated {delivery.LessonPlanDraftsUpdated}, skipped {delivery.LessonPlanDraftsSkipped}.");
                        if (!string.IsNullOrWhiteSpace(delivery.LessonPlanDraftsPath))
                        {
                            AppendLog($"Draft artifact: {delivery.LessonPlanDraftsPath}");
                        }
                    }

                    if (status == "completed")
                    {
                        AppendLog("Internal curriculum pipeline completed successfully.");
                        return (0, log.ToString(), string.Empty, current.JobFolder);
                    }

                    var failedStage = current.Stages.LastOrDefault(stage =>
                        string.Equals(stage.Status, "failed", StringComparison.OrdinalIgnoreCase));
                    var error = string.IsNullOrWhiteSpace(current.Error)
                        ? (failedStage?.Detail ?? "Internal curriculum pipeline failed.")
                        : current.Error;
                    return (1, log.ToString(), error, current.JobFolder);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }

            return (1, log.ToString(), "Automation job was cancelled before the internal curriculum pipeline finished.", pipelineJob.JobFolder);
        }

        private static async Task<(int ExitCode, string StdOut, string StdErr, string? OutputPath)> ExecutePowerShellJobAsync(
            AutomationJob job,
            BuildJobConfig cfg,
            CancellationToken ct)
        {
            var backendBase = string.IsNullOrWhiteSpace(cfg.BackendBase) ? "http://localhost:5299/api" : cfg.BackendBase.Trim().TrimEnd('/');
            var defaultScriptPath = EtdpPaths.CombineProject("AzureAgent", "smoke-test-agent.ps1");
            var scriptPath = string.IsNullOrWhiteSpace(cfg.ScriptPath) ? defaultScriptPath : cfg.ScriptPath.Trim();
            var psPath = string.IsNullOrWhiteSpace(cfg.PowerShellPath) ? "powershell.exe" : cfg.PowerShellPath.Trim();

            if (!File.Exists(scriptPath))
            {
                return (1, "", $"Script not found: {scriptPath}", null);
            }

            var args = new StringBuilder();
            args.Append("-NoProfile -ExecutionPolicy Bypass -File ");
            args.Append('"').Append(scriptPath).Append('"');
            args.Append(" -BackendBase ").Append('"').Append(backendBase).Append('"');
            args.Append(" -QualificationId ").Append(job.QualificationId.ToString());
            if (cfg.RunImports) args.Append(" -RunImports");
            if (cfg.RunSeedWrite) args.Append(" -RunSeedWrite");

            var psi = new ProcessStartInfo
            {
                FileName = psPath,
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stdErrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdOut = await stdOutTask;
            var stdErr = await stdErrTask;
            var outputPath = TryExtractOutputPath(stdOut);
            return (process.ExitCode, stdOut, stdErr, outputPath);
        }

        private static BuildJobConfig ParseConfig(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new BuildJobConfig();
            try
            {
                return JsonSerializer.Deserialize<BuildJobConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new BuildJobConfig();
            }
            catch
            {
                return new BuildJobConfig();
            }
        }

        private static string? TryExtractOutputPath(string stdOut)
        {
            if (string.IsNullOrWhiteSpace(stdOut)) return null;
            try
            {
                using var doc = JsonDocument.Parse(stdOut);
                if (doc.RootElement.TryGetProperty("outputRoot", out var outputRoot))
                {
                    return outputRoot.GetString();
                }
            }
            catch
            {
                // Ignore parse errors; output may be plain text.
            }
            return null;
        }

        private sealed class BuildJobConfig
        {
            public bool RunImports { get; set; }
            public bool RunSeedWrite { get; set; }
            public string? BackendBase { get; set; }
            public int? StartPage { get; set; }
            public bool ForceRestart { get; set; }
            public string? ExecutionMode { get; set; }
            public string? ScriptPath { get; set; }
            public string? PowerShellPath { get; set; }
        }
    }
}
