using System.Diagnostics;
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
        private readonly ILogger<AutomationJobWorker> _logger;

        public AutomationJobWorker(IServiceScopeFactory scopeFactory, ILogger<AutomationJobWorker> logger)
        {
            _scopeFactory = scopeFactory;
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

        private static async Task<(int ExitCode, string StdOut, string StdErr, string? OutputPath)> ExecuteJobAsync(AutomationJob job, CancellationToken ct)
        {
            if (!string.Equals(job.JobType, "build_qualification", StringComparison.OrdinalIgnoreCase))
            {
                return (1, "", $"Unsupported job type: {job.JobType}", null);
            }

            var cfg = ParseConfig(job.ConfigJson);
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
            public string? ScriptPath { get; set; }
            public string? PowerShellPath { get; set; }
        }
    }
}
