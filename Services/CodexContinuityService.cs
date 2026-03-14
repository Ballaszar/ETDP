using System.Reflection;
using System.Text;
using System.Text.Json;
using ETD.Api.Controllers;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.DataProtection;

namespace ETD.Api.Services
{
    public sealed class CodexContinuityService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<CodexContinuityService> _logger;
        private readonly IDataProtector _protector;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private readonly object _statusLock = new();

        private ContinuityStatus _status = new();

        public sealed class ContinuityStatus
        {
            public bool Enabled { get; set; } = true;
            public string RootPath { get; set; } = string.Empty;
            public string LatestMarkdownPath { get; set; } = string.Empty;
            public string LatestJsonPath { get; set; } = string.Empty;
            public string LatestEncryptedPath { get; set; } = string.Empty;
            public string LedgerPath { get; set; } = string.Empty;
            public int RefreshIntervalMinutes { get; set; } = 360;
            public DateTime? LastRunAtUtc { get; set; }
            public DateTime? LastSuccessAtUtc { get; set; }
            public string LastReason { get; set; } = string.Empty;
            public string LastError { get; set; } = string.Empty;
            public DateTime? LastErrorAtUtc { get; set; }
            public int LastControllersCount { get; set; }
            public int LastServicesCount { get; set; }
            public int LastModelsCount { get; set; }
            public int LastEndpointCount { get; set; }
            public DateTime CheckedAtUtc { get; set; }
        }

        private sealed class ContinuitySnapshot
        {
            public DateTime GeneratedAtUtc { get; set; }
            public string Reason { get; set; } = string.Empty;
            public string ApplicationRoot { get; set; } = string.Empty;
            public string WorkspaceRoot { get; set; } = string.Empty;
            public Dictionary<string, object> Runtime { get; set; } = new();
            public Dictionary<string, int> DataOverview { get; set; } = new();
            public List<object> Controllers { get; set; } = new();
            public List<object> Services { get; set; } = new();
            public List<string> DbSets { get; set; } = new();
            public List<object> KeyKnowledgeFiles { get; set; } = new();
            public List<object> RecentCoreFileUpdates { get; set; } = new();
        }

        private sealed class ContinuityLedgerEntry
        {
            public DateTime GeneratedAtUtc { get; set; }
            public string Reason { get; set; } = string.Empty;
            public string MarkdownPath { get; set; } = string.Empty;
            public string JsonPath { get; set; } = string.Empty;
            public string EncryptedPath { get; set; } = string.Empty;
            public int Controllers { get; set; }
            public int Services { get; set; }
            public int Models { get; set; }
            public int Endpoints { get; set; }
            public string Hash { get; set; } = string.Empty;
        }

        private sealed class EndpointSummary
        {
            public string Method { get; set; } = string.Empty;
            public string Route { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
        }

        public CodexContinuityService(
            IServiceScopeFactory scopeFactory,
            IWebHostEnvironment environment,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<CodexContinuityService> logger)
        {
            _scopeFactory = scopeFactory;
            _environment = environment;
            _logger = logger;
            _protector = dataProtectionProvider.CreateProtector("ETDP.CodexContinuity.v1");
        }

        public ContinuityStatus GetStatus()
        {
            lock (_statusLock)
            {
                return new ContinuityStatus
                {
                    Enabled = _status.Enabled,
                    RootPath = _status.RootPath,
                    LatestMarkdownPath = _status.LatestMarkdownPath,
                    LatestJsonPath = _status.LatestJsonPath,
                    LatestEncryptedPath = _status.LatestEncryptedPath,
                    LedgerPath = _status.LedgerPath,
                    RefreshIntervalMinutes = _status.RefreshIntervalMinutes,
                    LastRunAtUtc = _status.LastRunAtUtc,
                    LastSuccessAtUtc = _status.LastSuccessAtUtc,
                    LastReason = _status.LastReason,
                    LastError = _status.LastError,
                    LastErrorAtUtc = _status.LastErrorAtUtc,
                    LastControllersCount = _status.LastControllersCount,
                    LastServicesCount = _status.LastServicesCount,
                    LastModelsCount = _status.LastModelsCount,
                    LastEndpointCount = _status.LastEndpointCount,
                    CheckedAtUtc = DateTime.UtcNow
                };
            }
        }

        public string GetLatestMarkdown()
        {
            var path = GetStatus().LatestMarkdownPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return string.Empty;
            }
            return File.ReadAllText(path);
        }

        public async Task<ContinuityStatus> RefreshNowAsync(string reason, CancellationToken cancellationToken = default)
        {
            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                var rootPath = ResolveContinuityRootPath();
                Directory.CreateDirectory(rootPath);

                var snapshot = await BuildSnapshotAsync(reason, cancellationToken);
                snapshot.GeneratedAtUtc = now;

                var jsonPath = Path.Combine(rootPath, "codex-continuity-latest.json");
                var markdownPath = Path.Combine(rootPath, "codex-continuity-latest.md");
                var encryptedPath = Path.Combine(rootPath, "codex-continuity-latest.protected.txt");
                var ledgerPath = Path.Combine(rootPath, "codex-continuity-ledger.jsonl");

                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                var markdown = BuildMarkdown(snapshot);
                var encrypted = _protector.Protect(json);

                File.WriteAllText(jsonPath, json, Encoding.UTF8);
                File.WriteAllText(markdownPath, markdown, Encoding.UTF8);
                File.WriteAllText(encryptedPath, encrypted, Encoding.UTF8);

                var ledgerEntry = new ContinuityLedgerEntry
                {
                    GeneratedAtUtc = now,
                    Reason = reason,
                    MarkdownPath = markdownPath,
                    JsonPath = jsonPath,
                    EncryptedPath = encryptedPath,
                    Controllers = snapshot.Controllers.Count,
                    Services = snapshot.Services.Count,
                    Models = snapshot.DataOverview.TryGetValue("Models", out var modelCount) ? modelCount : 0,
                    Endpoints = snapshot.DataOverview.TryGetValue("Endpoints", out var endpointCount) ? endpointCount : 0,
                    Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant()
                };
                File.AppendAllText(ledgerPath, JsonSerializer.Serialize(ledgerEntry) + Environment.NewLine, Encoding.UTF8);

                lock (_statusLock)
                {
                    _status.RootPath = rootPath;
                    _status.LatestMarkdownPath = markdownPath;
                    _status.LatestJsonPath = jsonPath;
                    _status.LatestEncryptedPath = encryptedPath;
                    _status.LedgerPath = ledgerPath;
                    _status.LastRunAtUtc = now;
                    _status.LastSuccessAtUtc = now;
                    _status.LastReason = reason;
                    _status.LastError = string.Empty;
                    _status.LastControllersCount = snapshot.Controllers.Count;
                    _status.LastServicesCount = snapshot.Services.Count;
                    _status.LastModelsCount = snapshot.DataOverview.TryGetValue("Models", out var m) ? m : 0;
                    _status.LastEndpointCount = snapshot.DataOverview.TryGetValue("Endpoints", out var e) ? e : 0;
                }

                return GetStatus();
            }
            catch (Exception ex)
            {
                lock (_statusLock)
                {
                    _status.LastRunAtUtc = DateTime.UtcNow;
                    _status.LastError = ex.Message;
                    _status.LastErrorAtUtc = DateTime.UtcNow;
                    _status.LastReason = reason;
                }
                _logger.LogWarning(ex, "Failed to refresh Codex continuity log.");
                return GetStatus();
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var intervalMinutes = GetIntEnv("CODEX_CONTINUITY_INTERVAL_MINUTES", 360, 15, 1440);
            lock (_statusLock)
            {
                _status.Enabled = GetBoolEnv("CODEX_CONTINUITY_ENABLED", true);
                _status.RefreshIntervalMinutes = intervalMinutes;
                _status.RootPath = ResolveContinuityRootPath();
                _status.LatestMarkdownPath = Path.Combine(_status.RootPath, "codex-continuity-latest.md");
                _status.LatestJsonPath = Path.Combine(_status.RootPath, "codex-continuity-latest.json");
                _status.LatestEncryptedPath = Path.Combine(_status.RootPath, "codex-continuity-latest.protected.txt");
                _status.LedgerPath = Path.Combine(_status.RootPath, "codex-continuity-ledger.jsonl");
            }

            if (!GetBoolEnv("CODEX_CONTINUITY_ENABLED", true))
            {
                return;
            }

            await RefreshNowAsync("startup", stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
                    await RefreshNowAsync("scheduled", stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Scheduled Codex continuity refresh failed.");
                }
            }
        }

        private async Task<ContinuitySnapshot> BuildSnapshotAsync(string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var assembly = typeof(KnowledgeController).Assembly;
            var controllerTypes = assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var serviceTypes = assembly
                .GetTypes()
                .Where(t => !t.IsAbstract &&
                            t.IsClass &&
                            t.Namespace == "ETD.Api.Services")
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var modelTypes = assembly
                .GetTypes()
                .Where(t => !t.IsAbstract &&
                            t.IsClass &&
                            t.Namespace == "ETD.Api.Models")
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var controllers = new List<object>();
            var endpointCount = 0;
            foreach (var type in controllerTypes)
            {
                var routeBase = type.GetCustomAttribute<RouteAttribute>()?.Template ?? "api/[controller]";
                var constructor = type.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault();
                var dependencies = constructor == null
                    ? new List<string>()
                    : constructor.GetParameters()
                        .Select(p => p.ParameterType.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                var endpoints = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .SelectMany(method =>
                    {
                        var attrs = method.GetCustomAttributes()
                            .OfType<HttpMethodAttribute>()
                            .ToList();
                        if (attrs.Count == 0) return Enumerable.Empty<EndpointSummary>();
                        return attrs.Select(attr => new EndpointSummary
                        {
                            Method = string.Join(",", attr.HttpMethods ?? new List<string> { "GET" }),
                            Route = string.IsNullOrWhiteSpace(attr.Template) ? routeBase : $"{routeBase}/{attr.Template}",
                            Action = method.Name
                        });
                    })
                    .OrderBy(x => x.Route, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Method, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                endpointCount += endpoints.Count;
                controllers.Add(new
                {
                    Name = type.Name,
                    RouteBase = routeBase,
                    Dependencies = dependencies,
                    Endpoints = endpoints
                });
            }

            var services = serviceTypes
                .Select(type =>
                {
                    var constructor = type.GetConstructors()
                        .OrderByDescending(c => c.GetParameters().Length)
                        .FirstOrDefault();
                    var dependencies = constructor == null
                        ? new List<string>()
                        : constructor.GetParameters()
                            .Select(p => p.ParameterType.Name)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    return new
                    {
                        Name = type.Name,
                        Dependencies = dependencies
                    };
                })
                .Cast<object>()
                .ToList();

            var dbSets = typeof(ApplicationDbContext)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType.IsGenericType &&
                            p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .Select(p => p.Name)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var dataOverview = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Controllers"] = controllers.Count,
                ["Services"] = services.Count,
                ["Models"] = modelTypes.Count,
                ["Endpoints"] = endpointCount,
                ["DbSets"] = dbSets.Count
            };

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                dataOverview["Qualifications"] = await db.Qualifications.CountAsync(cancellationToken);
                dataOverview["Subjects"] = await db.Subjects.CountAsync(cancellationToken);
                dataOverview["Topics"] = await db.Topics.CountAsync(cancellationToken);
                dataOverview["SourceMaterials"] = await db.SourceMaterials.CountAsync(cancellationToken);
                dataOverview["KnowledgeDeveloper"] = await db.SourceMaterials.CountAsync(
                    x => (x.KnowledgeSourceType ?? string.Empty) == "developer_knowledge_base",
                    cancellationToken);
            }

            var keyFiles = BuildKeyKnowledgeFiles();
            var recentFiles = BuildRecentCoreFileUpdates();

            return new ContinuitySnapshot
            {
                Reason = reason,
                ApplicationRoot = _environment.ContentRootPath,
                WorkspaceRoot = ResolveWorkspaceRoot(_environment.ContentRootPath),
                Runtime = new Dictionary<string, object>
                {
                    ["Environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                    ["MachineName"] = Environment.MachineName,
                    ["ProcessId"] = Environment.ProcessId,
                    ["AiMode"] = AiRuntime.GetMode(),
                    ["LocalLibraryPath"] = AiRuntime.GetLocalLibraryPath()
                },
                DataOverview = dataOverview,
                Controllers = controllers,
                Services = services,
                DbSets = dbSets,
                KeyKnowledgeFiles = keyFiles,
                RecentCoreFileUpdates = recentFiles
            };
        }

        private List<object> BuildKeyKnowledgeFiles()
        {
            var files = new List<string>
            {
                Path.Combine(_environment.ContentRootPath, "development.readme.md"),
                Path.Combine(AiRuntime.GetLocalLibraryPath(), "KnowledgeHierarchy", "upload.readme.md"),
                Path.Combine(_environment.ContentRootPath, "AzureAgent", "MODERATOR4_BOOTSTRAP_PROTOCOL.md"),
                Path.Combine(_environment.ContentRootPath, "BACKEND_README.md")
            };

            return files
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => new
                {
                    Path = path,
                    Exists = File.Exists(path),
                    LastWriteUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null,
                    SizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0
                })
                .Cast<object>()
                .ToList();
        }

        private List<object> BuildRecentCoreFileUpdates()
        {
            var roots = new[]
            {
                Path.Combine(_environment.ContentRootPath, "Controllers"),
                Path.Combine(_environment.ContentRootPath, "Services"),
                Path.Combine(_environment.ContentRootPath, "Models"),
                Path.Combine(_environment.ContentRootPath, "frontend", "src")
            };

            var files = new List<FileInfo>();
            foreach (var root in roots.Where(Directory.Exists))
            {
                files.AddRange(Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(path =>
                    {
                        var ext = Path.GetExtension(path).ToLowerInvariant();
                        return ext == ".cs" || ext == ".jsx" || ext == ".js" || ext == ".ts" || ext == ".tsx" || ext == ".md";
                    })
                    .Select(path => new FileInfo(path)));
            }

            return files
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(120)
                .Select(f => new
                {
                    Path = f.FullName,
                    LastWriteUtc = f.LastWriteTimeUtc,
                    SizeBytes = f.Length
                })
                .Cast<object>()
                .ToList();
        }

        private static string BuildMarkdown(ContinuitySnapshot snapshot)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Codex Continuity Log");
            sb.AppendLine();
            sb.AppendLine($"Generated (UTC): {snapshot.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Reason: {snapshot.Reason}");
            sb.AppendLine($"Application Root: {snapshot.ApplicationRoot}");
            sb.AppendLine($"Workspace Root: {snapshot.WorkspaceRoot}");
            sb.AppendLine();

            sb.AppendLine("## Runtime");
            sb.AppendLine();
            foreach (var kv in snapshot.Runtime.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {kv.Key}: {kv.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("## Data Overview");
            sb.AppendLine();
            foreach (var kv in snapshot.DataOverview.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {kv.Key}: {kv.Value}");
            }
            sb.AppendLine();

            sb.AppendLine("## Controllers and Routes");
            sb.AppendLine();
            foreach (var controller in snapshot.Controllers.Take(80))
            {
                var json = JsonSerializer.Serialize(controller);
                sb.AppendLine($"- {json}");
            }
            sb.AppendLine();

            sb.AppendLine("## Services");
            sb.AppendLine();
            foreach (var service in snapshot.Services.Take(120))
            {
                var json = JsonSerializer.Serialize(service);
                sb.AppendLine($"- {json}");
            }
            sb.AppendLine();

            sb.AppendLine("## DbSets");
            sb.AppendLine();
            foreach (var dbset in snapshot.DbSets)
            {
                sb.AppendLine($"- {dbset}");
            }
            sb.AppendLine();

            sb.AppendLine("## Key Knowledge Files");
            sb.AppendLine();
            foreach (var file in snapshot.KeyKnowledgeFiles)
            {
                var json = JsonSerializer.Serialize(file);
                sb.AppendLine($"- {json}");
            }
            sb.AppendLine();

            sb.AppendLine("## Recent Core File Updates");
            sb.AppendLine();
            foreach (var file in snapshot.RecentCoreFileUpdates)
            {
                var json = JsonSerializer.Serialize(file);
                sb.AppendLine($"- {json}");
            }

            return sb.ToString().TrimEnd();
        }

        private string ResolveContinuityRootPath()
        {
            var configured = (Environment.GetEnvironmentVariable("CODEX_CONTINUITY_PATH") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }
            return Path.Combine(_environment.ContentRootPath, "SystemData", "CodexContinuity");
        }

        private static string ResolveWorkspaceRoot(string contentRoot)
        {
            try
            {
                var parent = Directory.GetParent(contentRoot);
                if (parent == null) return contentRoot;
                return parent.FullName;
            }
            catch
            {
                return contentRoot;
            }
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
