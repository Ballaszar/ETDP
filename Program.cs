using ETD.Api.Data;
using ETD.Api.Security;
using ETD.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var corsOrigins = ReadCorsOrigins(builder.Configuration);

// Add CORS policy for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy => policy.WithOrigins(corsOrigins.ToArray())
                        .AllowAnyHeader()
                        .AllowAnyMethod());
});

// Single DbContext for the entire app
var sqliteConnectionString = ResolveSqliteConnectionString(
    builder.Configuration.GetConnectionString("ETDPConnection"),
    builder.Environment.ContentRootPath,
    AppContext.BaseDirectory
);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(sqliteConnectionString));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDataProtection();
builder.Services.Configure<AppAuthorizationOptions>(builder.Configuration.GetSection("AppAuthorization"));
builder.Services.AddHostedService<AutomationJobWorker>();
builder.Services.AddScoped<KnowledgeHierarchyService>();
builder.Services.AddScoped<KnowledgeQuestionnaireV1Service>();
builder.Services.AddSingleton<SemanticStateContinuityService>();
builder.Services.AddSingleton<SemanticKernelQuestionService>();
builder.Services.AddSingleton<OcrExtractionService>();
builder.Services.AddSingleton<CurriculumKnowledgeScanService>();
builder.Services.AddSingleton<SansMetadataService>();
builder.Services.AddSingleton<CodexContinuityService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CodexContinuityService>());
builder.Services.AddSingleton<WorkspaceBackupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WorkspaceBackupService>());
var contentRoot = builder.Environment.ContentRootPath;
var baseDirectory = AppContext.BaseDirectory;
var projectRoot = FindProjectRoot(contentRoot, baseDirectory, Environment.GetEnvironmentVariable("ETDP_WORKSPACE_ROOT"));
var isProduction = builder.Environment.IsProduction();
builder.Services.PostConfigure<AppAuthorizationOptions>(options =>
{
    var fileApiKeys = ReadKeysFromCandidates(BuildKeyFileCandidates(
        "api_keys.txt",
        Environment.GetEnvironmentVariable("APP_AUTH_API_KEYS_FILE"),
        contentRoot,
        baseDirectory,
        projectRoot));
    var fileActivationKeys = ReadKeysFromCandidates(BuildKeyFileCandidates(
        "activation_keys.txt",
        Environment.GetEnvironmentVariable("APP_AUTH_ACTIVATION_KEYS_FILE"),
        contentRoot,
        baseDirectory,
        projectRoot));

    var envApiKeys = ReadKeysFromEnv("APP_AUTH_API_KEYS");
    var envActivationKeys = ReadKeysFromEnv("APP_AUTH_ACTIVATION_KEYS");
    var externalKeysProvided = fileApiKeys.Count > 0 || fileActivationKeys.Count > 0 || envApiKeys.Count > 0 || envActivationKeys.Count > 0;

    // Priority: environment variable > key files > appsettings
    if (fileApiKeys.Count > 0) options.ApiKeys = fileApiKeys;
    if (fileActivationKeys.Count > 0) options.ActivationKeys = fileActivationKeys;
    if (envApiKeys.Count > 0) options.ApiKeys = envApiKeys;
    if (envActivationKeys.Count > 0) options.ActivationKeys = envActivationKeys;

    options.ApiKeys = NormalizeKeys(options.ApiKeys);
    options.ActivationKeys = NormalizeKeys(options.ActivationKeys);
    options.BypassMachineNames = options.BypassMachineNames
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    ValidateAuthorizationOptions(options, isProduction, externalKeysProvided);
});
builder.Services.AddSingleton<ActivationTokenService>();

var app = builder.Build();
var apiMaintenanceMode = IsFlagEnabled("API_MAINTENANCE_MODE");
var enableSkeletonSeed = IsFlagEnabled("ETDP_ENABLE_SKELETON_SEED");
var frontendStaticRoot = ResolveFrontendStaticRoot(builder.Environment.ContentRootPath, AppContext.BaseDirectory);
var frontendStaticProvider = string.IsNullOrWhiteSpace(frontendStaticRoot)
    ? null
    : new PhysicalFileProvider(frontendStaticRoot);
app.Logger.LogInformation("SQLite Data Source: {DataSource}", GetSqliteDataSource(sqliteConnectionString));
app.Logger.LogInformation("Content Root: {ContentRoot} | Base Directory: {BaseDirectory}", builder.Environment.ContentRootPath, AppContext.BaseDirectory);
app.Logger.LogInformation(
    "Frontend static root: {FrontendStaticRoot}",
    string.IsNullOrWhiteSpace(frontendStaticRoot) ? "(not found)" : frontendStaticRoot);

// Use CORS for frontend
app.UseCors("AllowFrontend");
app.UseMiddleware<RequestCorrelationMiddleware>();
app.UseMiddleware<ExceptionLoggingMiddleware>();
app.Use(async (context, next) =>
{
    if (!apiMaintenanceMode || !context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    context.Response.ContentType = "application/json";
    context.Response.Headers["Retry-After"] = "120";
    await context.Response.WriteAsJsonAsync(new
    {
        error = "API is temporarily unavailable for maintenance."
    });
});

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var startupInitTimer = Stopwatch.StartNew();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var knowledgeHierarchy = scope.ServiceProvider.GetRequiredService<KnowledgeHierarchyService>();
    app.Logger.LogInformation("Startup init: applying database migrations");
    db.Database.Migrate();
    app.Logger.LogInformation("Startup init: ensuring SMi operational SQLite tables");
    ApplicationDbContext.EnsureOperationalTables(db);
    if (enableSkeletonSeed)
    {
        app.Logger.LogInformation("Startup init: seeding skeleton curriculum");
        ApplicationDbContext.SeedSkeletonCurriculum(db);
    }
    else
    {
        app.Logger.LogInformation("Startup init: skeleton curriculum seeding disabled (set ETDP_ENABLE_SKELETON_SEED=true to enable)");
    }
    app.Logger.LogInformation("Startup init: ensuring upload readme");
    knowledgeHierarchy.EnsureUploadReadme();
    app.Logger.LogInformation("Startup init: ensuring qualification structures");
    knowledgeHierarchy.EnsureStructuresForKnownQualifications();
    app.Logger.LogInformation("Startup init: consolidating legacy qualification folders");
    knowledgeHierarchy.ConsolidateLegacyQualificationFolders(new KnowledgeHierarchyService.ConsolidationOptions
    {
        RebuildUploadReadme = false,
        RemoveEmptyLegacyFolders = true
    });
    app.Logger.LogInformation(
        "Startup init complete in {ElapsedMs} ms. Codex continuity refresh runs in hosted background service.",
        startupInitTimer.ElapsedMilliseconds);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}
if (frontendStaticProvider is not null)
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = frontendStaticProvider
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = frontendStaticProvider,
        OnPrepareResponse = context =>
        {
            var path = context.Context.Request.Path.Value ?? string.Empty;
            var isHtml = path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(Path.GetExtension(path));
            context.Context.Response.Headers["Cache-Control"] = isHtml
                ? "no-store, no-cache, max-age=0"
                : "no-cache, max-age=0, must-revalidate";
            context.Context.Response.Headers["Pragma"] = "no-cache";
            context.Context.Response.Headers["Expires"] = "0";
        }
    });
}
app.UseMiddleware<AppAuthorizationMiddleware>();
app.UseAuthorization();
app.MapControllers();
if (frontendStaticProvider is not null)
{
    var frontendIndexPath = Path.Combine(frontendStaticRoot!, "index.html");
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!File.Exists(frontendIndexPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
        await context.Response.SendFileAsync(frontendIndexPath);
    });
}
app.Run();

static List<string> ReadKeysFromFile(string path)
{
    if (!File.Exists(path)) return new List<string>();
    return File.ReadAllLines(path)
        .Select(x => x.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Where(x => !x.StartsWith("#"))
        .Distinct(StringComparer.Ordinal)
        .ToList();
}

static List<string> ReadKeysFromCandidates(IEnumerable<string> candidates)
{
    foreach (var candidate in candidates)
    {
        if (string.IsNullOrWhiteSpace(candidate)) continue;
        var keys = ReadKeysFromFile(candidate);
        if (keys.Count > 0)
        {
            return keys;
        }
    }

    return new List<string>();
}

static List<string> ReadKeysFromEnv(string envName)
{
    var raw = Environment.GetEnvironmentVariable(envName);
    if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
    return raw
        .Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.Ordinal)
        .ToList();
}

static List<string> ReadCorsOrigins(IConfiguration configuration)
{
    var fromConfig = configuration
        .GetSection("Cors:AllowedOrigins")
        .GetChildren()
        .Select(x => x.Value ?? string.Empty)
        .Where(x => !string.IsNullOrWhiteSpace(x));

    var fromEnv = (Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? string.Empty)
        .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x));

    var merged = fromEnv
        .Concat(fromConfig)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (merged.Count > 0) return merged;

    return new List<string>
    {
        "http://localhost:5173",
        "https://localhost:5173"
    };
}

static string? ResolveFrontendStaticRoot(string contentRootPath, string baseDirectory)
{
    var overridePath = Environment.GetEnvironmentVariable("ETDP_FRONTEND_DIST");
    if (!string.IsNullOrWhiteSpace(overridePath))
    {
        var resolvedOverride = Path.GetFullPath(overridePath.Trim());
        if (Directory.Exists(resolvedOverride) && File.Exists(Path.Combine(resolvedOverride, "index.html")))
        {
            return resolvedOverride;
        }
    }

    var projectRoot = FindProjectRoot(contentRootPath, baseDirectory);
    var candidates = new List<string>();

    AddCandidateDir(candidates, Path.Combine(contentRootPath, "wwwroot"));
    AddCandidateDir(candidates, Path.Combine(contentRootPath, "frontend", "dist"));
    AddCandidateDir(candidates, Path.Combine(baseDirectory, "wwwroot"));
    AddCandidateDir(candidates, Path.Combine(baseDirectory, "frontend", "dist"));
    AddCandidateDir(candidates, Path.Combine(baseDirectory, "..", "wwwroot"));
    AddCandidateDir(candidates, Path.Combine(baseDirectory, "..", "frontend", "dist"));
    if (!string.IsNullOrWhiteSpace(projectRoot))
    {
        AddCandidateDir(candidates, Path.Combine(projectRoot, "wwwroot"));
        AddCandidateDir(candidates, Path.Combine(projectRoot, "frontend", "dist"));
    }

    return candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "index.html")));
}

static IEnumerable<string> BuildKeyFileCandidates(
    string fileName,
    string? explicitPath,
    string contentRootPath,
    string baseDirectory,
    string? projectRoot)
{
    var candidates = new List<string>();

    AddCandidatePath(candidates, explicitPath);
    AddCandidatePath(candidates, Path.Combine(contentRootPath, "Security", "keys", fileName));
    AddCandidatePath(candidates, Path.Combine(baseDirectory, "Security", "keys", fileName));
    AddCandidatePath(candidates, Path.Combine(baseDirectory, "..", "Security", "keys", fileName));
    AddCandidatePath(candidates, Path.Combine(baseDirectory, "..", "..", "Security", "keys", fileName));
    if (!string.IsNullOrWhiteSpace(projectRoot))
    {
        AddCandidatePath(candidates, Path.Combine(projectRoot, "Security", "keys", fileName));
    }

    var workspaceRoot = Environment.GetEnvironmentVariable("ETDP_WORKSPACE_ROOT");
    if (!string.IsNullOrWhiteSpace(workspaceRoot))
    {
        AddCandidatePath(candidates, Path.Combine(workspaceRoot, "ETDP", "Security", "keys", fileName));
        AddCandidatePath(candidates, Path.Combine(workspaceRoot, "Security", "keys", fileName));
    }

    return candidates;
}

static void AddCandidateDir(List<string> paths, string? rawPath)
{
    if (string.IsNullOrWhiteSpace(rawPath)) return;

    var full = Path.GetFullPath(rawPath);
    if (paths.Contains(full, StringComparer.OrdinalIgnoreCase)) return;
    paths.Add(full);
}

static List<string> NormalizeKeys(List<string> keys)
{
    return (keys ?? new List<string>())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim())
        .Distinct(StringComparer.Ordinal)
        .ToList();
}

static void ValidateAuthorizationOptions(AppAuthorizationOptions options, bool isProduction, bool externalKeysProvided)
{
    var apiKeyRequired = options.RequireApiKey;
    var activationRequired = options.RequireActivation;
    var anyAuthRequired = apiKeyRequired || activationRequired;

    if (apiKeyRequired && options.ApiKeys.Count == 0)
    {
        throw new InvalidOperationException("Authorization misconfiguration: RequireApiKey=true but no API keys are configured.");
    }

    if (activationRequired && options.ActivationKeys.Count == 0)
    {
        throw new InvalidOperationException("Authorization misconfiguration: RequireActivation=true but no activation keys are configured.");
    }

    if (!isProduction) return;

    if (anyAuthRequired && !externalKeysProvided)
    {
        throw new InvalidOperationException("Production misconfiguration: keys must be provided via environment variables or Security/keys files.");
    }

    if (anyAuthRequired && options.BypassInDevelopment)
    {
        throw new InvalidOperationException("Production misconfiguration: BypassInDevelopment must be false.");
    }

    if (anyAuthRequired && options.BypassMachineNames.Count > 0)
    {
        throw new InvalidOperationException("Production misconfiguration: BypassMachineNames must be empty.");
    }

    static bool IsWeakOrPlaceholder(string key) =>
        string.IsNullOrWhiteSpace(key) ||
        key.Length < 16 ||
        key.Contains("CHANGE-ME", StringComparison.OrdinalIgnoreCase);

    if (apiKeyRequired && options.ApiKeys.Any(IsWeakOrPlaceholder))
    {
        throw new InvalidOperationException("Production misconfiguration: One or more API keys are weak or placeholder values.");
    }

    if (activationRequired && options.ActivationKeys.Any(IsWeakOrPlaceholder))
    {
        throw new InvalidOperationException("Production misconfiguration: One or more activation keys are weak or placeholder values.");
    }
}

static bool IsFlagEnabled(string envName)
{
    var raw = Environment.GetEnvironmentVariable(envName);
    if (string.IsNullOrWhiteSpace(raw)) return false;

    var normalized = raw.Trim().ToLowerInvariant();
    return normalized == "1" || normalized == "true" || normalized == "yes" || normalized == "on";
}

static string ResolveSqliteConnectionString(string? configuredConnectionString, string contentRootPath, string baseDirectory)
{
    var fallback = "Data Source=etdp.db";
    var raw = string.IsNullOrWhiteSpace(configuredConnectionString) ? fallback : configuredConnectionString;

    try
    {
        var builder = new SqliteConnectionStringBuilder(raw);
        var dataSource = string.IsNullOrWhiteSpace(builder.DataSource) ? "etdp.db" : builder.DataSource.Trim();

        var overridePath = Environment.GetEnvironmentVariable("ETDP_SQLITE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            builder.DataSource = Path.GetFullPath(overridePath.Trim());
            return builder.ToString();
        }

        if (Path.IsPathRooted(dataSource))
        {
            builder.DataSource = Path.GetFullPath(dataSource);
            return builder.ToString();
        }

        var projectRoot = FindProjectRoot(contentRootPath, baseDirectory) ?? Path.GetFullPath(contentRootPath);
        var preferredPath = Path.GetFullPath(Path.Combine(projectRoot, dataSource));

        var candidates = new List<string>();
        AddCandidatePath(candidates, preferredPath);
        AddCandidatePath(candidates, Path.Combine(contentRootPath, dataSource));
        AddCandidatePath(candidates, Path.Combine(contentRootPath, "ETDP", dataSource));
        AddCandidatePath(candidates, Path.Combine(baseDirectory, dataSource));
        AddCandidatePath(candidates, Path.Combine(baseDirectory, "..", "..", "..", dataSource));

        builder.DataSource = SelectBestExistingCandidate(candidates, preferredPath) ?? preferredPath;
        return builder.ToString();
    }
    catch
    {
        // If parsing fails, keep previous behavior to avoid startup regression.
        return raw;
    }
}

static void AddCandidatePath(List<string> paths, string? rawPath)
{
    if (string.IsNullOrWhiteSpace(rawPath)) return;
    var full = Path.GetFullPath(rawPath);
    if (paths.Contains(full, StringComparer.OrdinalIgnoreCase)) return;
    paths.Add(full);
}

static string? SelectBestExistingCandidate(IEnumerable<string> candidates, string preferredPath)
{
    var preferredFull = Path.GetFullPath(preferredPath);
    var existing = candidates
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Where(System.IO.File.Exists)
        .ToList();

    if (existing.Count == 0) return null;
    if (existing.Any(path => string.Equals(path, preferredFull, StringComparison.OrdinalIgnoreCase)))
    {
        return preferredFull;
    }

    return existing
        .Select(path => new System.IO.FileInfo(path))
        .OrderByDescending(file => file.LastWriteTimeUtc)
        .ThenByDescending(file => file.Length)
        .Select(file => file.FullName)
        .FirstOrDefault();
}

static string? FindProjectRoot(params string[] probes)
{
    foreach (var probe in probes)
    {
        if (string.IsNullOrWhiteSpace(probe)) continue;
        var start = Path.GetFullPath(probe);

        var nestedProject = Path.Combine(start, "ETDP", "ETDP.csproj");
        if (System.IO.File.Exists(nestedProject))
        {
            return Path.GetDirectoryName(nestedProject);
        }

        var baseDir = Directory.Exists(start) ? start : Path.GetDirectoryName(start);
        if (string.IsNullOrWhiteSpace(baseDir)) continue;

        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            var csproj = Path.Combine(current.FullName, "ETDP.csproj");
            if (System.IO.File.Exists(csproj))
            {
                return current.FullName;
            }

            current = current.Parent;
        }
    }

    return null;
}

static string GetSqliteDataSource(string connectionString)
{
    try
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        return builder.DataSource ?? string.Empty;
    }
    catch
    {
        return connectionString;
    }
}
