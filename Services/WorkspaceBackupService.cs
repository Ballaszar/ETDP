using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace ETD.Api.Services
{
    public sealed class WorkspaceBackupService : BackgroundService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<WorkspaceBackupService> _logger;
        private readonly SemaphoreSlim _runLock = new(1, 1);
        private readonly object _statusLock = new();

        private BackupStatus _status = new();

        public sealed class BackupStatus
        {
            public bool Enabled { get; set; } = true;
            public string SourcePath { get; set; } = string.Empty;
            public string DestinationPath { get; set; } = string.Empty;
            public string MirrorPath { get; set; } = string.Empty;
            public string SnapshotsPath { get; set; } = string.Empty;
            public string LastSnapshotPath { get; set; } = string.Empty;
            public int IntervalMinutes { get; set; } = 120;
            public int SnapshotIntervalMinutes { get; set; } = 360;
            public int SnapshotRetention { get; set; } = 28;
            public DateTime? LastRunAtUtc { get; set; }
            public DateTime? LastSuccessAtUtc { get; set; }
            public DateTime? LastSnapshotAtUtc { get; set; }
            public long? LastDurationMs { get; set; }
            public int? LastExitCode { get; set; }
            public string LastReason { get; set; } = string.Empty;
            public string LastSummary { get; set; } = string.Empty;
            public string LastError { get; set; } = string.Empty;
            public DateTime? LastErrorAtUtc { get; set; }
            public string HistoryLogPath { get; set; } = string.Empty;
            public DateTime CheckedAtUtc { get; set; }
        }

        private sealed class BackupHistoryEntry
        {
            public DateTime RunAtUtc { get; set; }
            public string Reason { get; set; } = string.Empty;
            public string SourcePath { get; set; } = string.Empty;
            public string DestinationPath { get; set; } = string.Empty;
            public string MirrorPath { get; set; } = string.Empty;
            public string SnapshotPath { get; set; } = string.Empty;
            public int? MirrorExitCode { get; set; }
            public int? SnapshotExitCode { get; set; }
            public long DurationMs { get; set; }
            public string Summary { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }

        public WorkspaceBackupService(IWebHostEnvironment environment, ILogger<WorkspaceBackupService> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public BackupStatus GetStatus()
        {
            lock (_statusLock)
            {
                return new BackupStatus
                {
                    Enabled = _status.Enabled,
                    SourcePath = _status.SourcePath,
                    DestinationPath = _status.DestinationPath,
                    MirrorPath = _status.MirrorPath,
                    SnapshotsPath = _status.SnapshotsPath,
                    LastSnapshotPath = _status.LastSnapshotPath,
                    IntervalMinutes = _status.IntervalMinutes,
                    SnapshotIntervalMinutes = _status.SnapshotIntervalMinutes,
                    SnapshotRetention = _status.SnapshotRetention,
                    LastRunAtUtc = _status.LastRunAtUtc,
                    LastSuccessAtUtc = _status.LastSuccessAtUtc,
                    LastSnapshotAtUtc = _status.LastSnapshotAtUtc,
                    LastDurationMs = _status.LastDurationMs,
                    LastExitCode = _status.LastExitCode,
                    LastReason = _status.LastReason,
                    LastSummary = _status.LastSummary,
                    LastError = _status.LastError,
                    LastErrorAtUtc = _status.LastErrorAtUtc,
                    HistoryLogPath = _status.HistoryLogPath,
                    CheckedAtUtc = DateTime.UtcNow
                };
            }
        }

        public async Task<BackupStatus> RunBackupNowAsync(string reason, CancellationToken cancellationToken = default)
        {
            await _runLock.WaitAsync(cancellationToken);
            try
            {
                var sourcePath = ResolveSourcePath();
                var destinationPath = ResolveDestinationPath();
                var mirrorPath = ResolveMirrorPath(destinationPath);
                var snapshotsPath = ResolveSnapshotsPath(destinationPath);
                var historyPath = ResolveHistoryPath(destinationPath);
                Directory.CreateDirectory(destinationPath);
                Directory.CreateDirectory(mirrorPath);
                Directory.CreateDirectory(snapshotsPath);
                var historyDirectory = Path.GetDirectoryName(historyPath);
                if (!string.IsNullOrWhiteSpace(historyDirectory))
                {
                    Directory.CreateDirectory(historyDirectory);
                }

                var started = DateTime.UtcNow;
                var stopwatch = Stopwatch.StartNew();
                var snapshotIntervalMinutes = GetIntEnv("ETDP_BACKUP_SNAPSHOT_INTERVAL_MINUTES", 360, 30, 10080);
                var snapshotRetention = GetIntEnv("ETDP_BACKUP_SNAPSHOT_RETENTION", 28, 3, 120);
                var shouldCreateSnapshot = ShouldCreateSnapshot(reason, snapshotIntervalMinutes);

                var mirrorResult = await RunRoboCopyAsync(sourcePath, mirrorPath, mirror: true, cancellationToken);
                var snapshotPath = string.Empty;
                (int? exitCode, string summary, string error) snapshotResult = (0, "snapshot_skipped", string.Empty);
                if (shouldCreateSnapshot)
                {
                    snapshotPath = Path.Combine(snapshotsPath, started.ToString("yyyyMMdd_HHmmss"));
                    snapshotResult = await RunRoboCopyAsync(sourcePath, snapshotPath, mirror: false, cancellationToken);
                    PruneSnapshots(snapshotsPath, snapshotRetention);
                }

                stopwatch.Stop();
                var duration = stopwatch.ElapsedMilliseconds;
                var success = string.IsNullOrWhiteSpace(mirrorResult.error) && string.IsNullOrWhiteSpace(snapshotResult.error);
                var summary = shouldCreateSnapshot
                    ? (success ? "mirror_and_snapshot_completed" : "mirror_and_snapshot_warning")
                    : (string.IsNullOrWhiteSpace(mirrorResult.error) ? "mirror_completed" : "mirror_warning");
                var error = string.Join(
                    " | ",
                    new[] { mirrorResult.error, snapshotResult.error }.Where(x => !string.IsNullOrWhiteSpace(x)));

                var history = new BackupHistoryEntry
                {
                    RunAtUtc = started,
                    Reason = reason,
                    SourcePath = sourcePath,
                    DestinationPath = destinationPath,
                    MirrorPath = mirrorPath,
                    SnapshotPath = snapshotPath,
                    MirrorExitCode = mirrorResult.exitCode,
                    SnapshotExitCode = shouldCreateSnapshot ? snapshotResult.exitCode : null,
                    DurationMs = duration,
                    Summary = summary,
                    Error = error
                };
                if (!string.IsNullOrWhiteSpace(historyDirectory))
                {
                    Directory.CreateDirectory(historyDirectory);
                }
                File.AppendAllText(historyPath, JsonSerializer.Serialize(history) + Environment.NewLine, Encoding.UTF8);

                lock (_statusLock)
                {
                    _status.SourcePath = sourcePath;
                    _status.DestinationPath = destinationPath;
                    _status.MirrorPath = mirrorPath;
                    _status.SnapshotsPath = snapshotsPath;
                    _status.SnapshotIntervalMinutes = snapshotIntervalMinutes;
                    _status.SnapshotRetention = snapshotRetention;
                    _status.HistoryLogPath = historyPath;
                    _status.LastRunAtUtc = started;
                    _status.LastDurationMs = duration;
                    _status.LastExitCode = mirrorResult.exitCode;
                    _status.LastSummary = summary;
                    _status.LastReason = reason;
                    if (shouldCreateSnapshot && string.IsNullOrWhiteSpace(snapshotResult.error))
                    {
                        _status.LastSnapshotPath = snapshotPath;
                        _status.LastSnapshotAtUtc = started;
                    }
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        _status.LastSuccessAtUtc = DateTime.UtcNow;
                        _status.LastError = string.Empty;
                    }
                    else
                    {
                        _status.LastError = error;
                        _status.LastErrorAtUtc = DateTime.UtcNow;
                    }
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    _logger.LogWarning("Workspace backup reported warnings/errors: {Error}", error);
                }

                return GetStatus();
            }
            catch (Exception ex)
            {
                lock (_statusLock)
                {
                    _status.LastRunAtUtc = DateTime.UtcNow;
                    _status.LastReason = reason;
                    _status.LastError = ex.Message;
                    _status.LastErrorAtUtc = DateTime.UtcNow;
                }
                _logger.LogWarning(ex, "Workspace backup run failed.");
                return GetStatus();
            }
            finally
            {
                _runLock.Release();
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var intervalMinutes = GetIntEnv("ETDP_BACKUP_INTERVAL_MINUTES", 120, 15, 1440);
            var snapshotIntervalMinutes = GetIntEnv("ETDP_BACKUP_SNAPSHOT_INTERVAL_MINUTES", 360, 30, 10080);
            var snapshotRetention = GetIntEnv("ETDP_BACKUP_SNAPSHOT_RETENTION", 28, 3, 120);
            var enabled = GetBoolEnv("ETDP_BACKUP_ENABLED", true);
            var sourcePath = ResolveSourcePath();
            var destinationPath = ResolveDestinationPath();
            var mirrorPath = ResolveMirrorPath(destinationPath);
            var snapshotsPath = ResolveSnapshotsPath(destinationPath);
            var historyPath = ResolveHistoryPath(destinationPath);

            lock (_statusLock)
            {
                _status.Enabled = enabled;
                _status.IntervalMinutes = intervalMinutes;
                _status.SnapshotIntervalMinutes = snapshotIntervalMinutes;
                _status.SnapshotRetention = snapshotRetention;
                _status.SourcePath = sourcePath;
                _status.DestinationPath = destinationPath;
                _status.MirrorPath = mirrorPath;
                _status.SnapshotsPath = snapshotsPath;
                _status.HistoryLogPath = historyPath;
            }

            try
            {
                Directory.CreateDirectory(destinationPath);
                Directory.CreateDirectory(mirrorPath);
                Directory.CreateDirectory(snapshotsPath);
                var historyDirectory = Path.GetDirectoryName(historyPath);
                if (!string.IsNullOrWhiteSpace(historyDirectory))
                {
                    Directory.CreateDirectory(historyDirectory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create backup destination path '{BackupPath}'", destinationPath);
            }

            if (!enabled)
            {
                return;
            }

            await RunBackupNowAsync("startup", stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
                    await RunBackupNowAsync("scheduled", stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Scheduled workspace backup failed.");
                }
            }
        }

        private async Task<(int? exitCode, string summary, string error)> RunRoboCopyAsync(
            string sourcePath,
            string destinationPath,
            bool mirror,
            CancellationToken cancellationToken)
        {
            if (!Directory.Exists(sourcePath))
            {
                return (null, "backup_skipped", $"Source path does not exist: {sourcePath}");
            }

            Directory.CreateDirectory(destinationPath);

            var sourceFullPath = NormalizePath(sourcePath);
            var destinationFullPath = NormalizePath(destinationPath);
            if (PathEquals(sourceFullPath, destinationFullPath))
            {
                return (null, "backup_failed", $"Backup source and destination are the same path: {sourceFullPath}");
            }

            var sourceInsideDestination = IsSubPathOf(sourceFullPath, destinationFullPath);
            if (sourceInsideDestination)
            {
                return (
                    null,
                    "backup_failed",
                    $"Unsafe backup paths: source '{sourceFullPath}' is inside destination '{destinationFullPath}'. Configure ETDP_BACKUP_DEST_PATH outside the source tree.");
            }

            var excludedDirs = new[]
            {
                "node_modules",
                "bin",
                "obj",
                "dist",
                ".vs"
            };

            var excludedPaths = new List<string>();
            foreach (var dir in excludedDirs)
            {
                excludedPaths.Add(Path.Combine(sourceFullPath, dir));
            }

            var destinationInsideSource = IsSubPathOf(destinationFullPath, sourceFullPath);
            if (destinationInsideSource)
            {
                excludedPaths.Add(destinationFullPath);
                _logger.LogWarning(
                    "Backup destination '{DestinationPath}' is inside source '{SourcePath}'. Destination is being excluded to prevent recursive mirroring.",
                    destinationFullPath,
                    sourceFullPath);
            }

            var args = new StringBuilder();
            args.Append('"').Append(sourceFullPath).Append('"');
            args.Append(' ');
            args.Append('"').Append(destinationFullPath).Append('"');
            args.Append(mirror ? " /MIR" : " /E");
            args.Append(" /R:1 /W:1 /FFT /NP /NFL /NDL /NJH /NJS");
            if (excludedPaths.Count > 0)
            {
                args.Append(" /XD");
                foreach (var path in excludedPaths)
                {
                    args.Append(' ').Append('"').Append(path).Append('"');
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = "robocopy",
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                {
                    return (null, "backup_failed", "Could not start robocopy process.");
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                var code = process.ExitCode;

                var success = code <= 7;
                var summary = success
                    ? (mirror ? "mirror_completed" : "snapshot_completed")
                    : (mirror ? "mirror_failed" : "snapshot_failed");
                var error = success ? string.Empty : BuildErrorMessage(stderr, stdout, code);
                return (code, summary, error);
            }
            catch (Exception ex)
            {
                return (null, "backup_failed", ex.Message);
            }
        }

        private static string BuildErrorMessage(string stderr, string stdout, int exitCode)
        {
            var stderrClean = (stderr ?? string.Empty).Trim();
            var stdoutClean = (stdout ?? string.Empty).Trim();
            var detail = string.IsNullOrWhiteSpace(stderrClean) ? stdoutClean : stderrClean;
            if (detail.Length > 600)
            {
                detail = detail.Substring(0, 600).TrimEnd() + "...";
            }
            return $"Robocopy exit code {exitCode}. {detail}".Trim();
        }

        private string ResolveSourcePath()
        {
            var configured = (Environment.GetEnvironmentVariable("ETDP_BACKUP_SOURCE_PATH") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            try
            {
                var parent = Directory.GetParent(_environment.ContentRootPath);
                return parent?.FullName ?? _environment.ContentRootPath;
            }
            catch
            {
                return _environment.ContentRootPath;
            }
        }

        private string ResolveDestinationPath()
        {
            var configured = (Environment.GetEnvironmentVariable("ETDP_BACKUP_DEST_PATH") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            const string preferredExternalPath = @"F:\ETDP";
            try
            {
                var driveRoot = Path.GetPathRoot(preferredExternalPath);
                if (!string.IsNullOrWhiteSpace(driveRoot) && Directory.Exists(driveRoot))
                {
                    return preferredExternalPath;
                }
            }
            catch
            {
                // Ignore and use local fallback below.
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(localAppData, "ETDP", "Backups");
            }

            return Path.Combine(Path.GetTempPath(), "ETDP", "Backups");
        }

        private static string ResolveMirrorPath(string destinationPath)
        {
            return Path.Combine(destinationPath, "WorkspaceMirror");
        }

        private static string ResolveSnapshotsPath(string destinationPath)
        {
            return Path.Combine(destinationPath, "Snapshots");
        }

        private string ResolveHistoryPath(string destinationPath)
        {
            var configured = (Environment.GetEnvironmentVariable("ETDP_BACKUP_HISTORY_PATH") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return Path.Combine(destinationPath, "_backup", "backup-history.jsonl");
        }

        private bool ShouldCreateSnapshot(string reason, int snapshotIntervalMinutes)
        {
            if (string.IsNullOrWhiteSpace(reason)) return true;
            if (!string.Equals(reason, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            lock (_statusLock)
            {
                if (!_status.LastSnapshotAtUtc.HasValue)
                {
                    return true;
                }

                return DateTime.UtcNow - _status.LastSnapshotAtUtc.Value >= TimeSpan.FromMinutes(snapshotIntervalMinutes);
            }
        }

        private void PruneSnapshots(string snapshotsPath, int snapshotRetention)
        {
            try
            {
                if (!Directory.Exists(snapshotsPath))
                {
                    return;
                }

                var directories = new DirectoryInfo(snapshotsPath)
                    .GetDirectories()
                    .OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var directory in directories.Skip(Math.Max(1, snapshotRetention)))
                {
                    directory.Delete(recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Snapshot retention pruning failed for '{SnapshotsPath}'.", snapshotsPath);
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static bool PathEquals(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSubPathOf(string candidatePath, string basePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(basePath))
            {
                return false;
            }

            var baseWithSeparator = basePath + Path.DirectorySeparatorChar;
            var altWithSeparator = basePath + Path.AltDirectorySeparatorChar;
            return candidatePath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase)
                || candidatePath.StartsWith(altWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private static bool GetBoolEnv(string key, bool defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
            if (bool.TryParse(raw.Trim(), out var parsed)) return parsed;
            return defaultValue;
        }

        private static int GetIntEnv(string key, int defaultValue, int min, int max)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
            if (!int.TryParse(raw.Trim(), out var parsed)) return defaultValue;
            if (parsed < min) return min;
            if (parsed > max) return max;
            return parsed;
        }
    }
}
