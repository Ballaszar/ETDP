using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ETD.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ElectricBookExportController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ElectricBookExportController> _logger;

        public ElectricBookExportController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            ILogger<ElectricBookExportController> logger)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        [HttpPost("map")]
        public async Task<IActionResult> Map([FromBody] ElectricBookMapRequest? request, CancellationToken cancellationToken)
        {
            request ??= new ElectricBookMapRequest();
            if (request.QualificationId <= 0) return BadRequest(new { error = "qualificationId is required." });

            var qualification = await _context.Qualifications
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == request.QualificationId, cancellationToken);
            if (qualification == null) return NotFound(new { error = $"Qualification not found: {request.QualificationId}" });

            var subjects = await _context.Subjects
                .AsNoTracking()
                .Where(s => s.QualificationId == request.QualificationId)
                .OrderBy(s => s.CurriculumPhaseId)
                .ThenBy(s => s.SubjectCode)
                .Select(s => new SubjectMapRow
                {
                    Id = s.Id,
                    SubjectCode = s.SubjectCode ?? string.Empty,
                    SubjectDescription = s.SubjectDescription ?? string.Empty,
                    SubjectPurpose = s.SubjectPurpose ?? string.Empty
                })
                .ToListAsync(cancellationToken);
            if (subjects.Count == 0) return BadRequest(new { error = "No subjects found for this qualification." });

            var subjectIds = subjects.Select(s => s.Id).ToList();
            var topics = await _context.Topics
                .AsNoTracking()
                .Where(t => subjectIds.Contains(t.SubjectId))
                .OrderBy(t => t.SubjectId)
                .ThenBy(t => t.Order ?? int.MaxValue)
                .ThenBy(t => t.TopicCode)
                .Select(t => new TopicMapRow
                {
                    SubjectId = t.SubjectId,
                    TopicCode = t.TopicCode ?? string.Empty,
                    TopicDescription = t.TopicDescription ?? string.Empty,
                    TopicPurpose = t.TopicPurpose ?? string.Empty,
                    NotionalHours = t.NotionalHours,
                    PeriodsPerTopic = t.PeriodsPerTopic
                })
                .ToListAsync(cancellationToken);

            var topicsBySubject = topics
                .GroupBy(t => t.SubjectId)
                .ToDictionary(g => g.Key, g => g.ToList());
            foreach (var subject in subjects)
            {
                subject.Topics = topicsBySubject.TryGetValue(subject.Id, out var list)
                    ? list
                    : new List<TopicMapRow>();
            }

            var workspaceRoot = ResolveWorkspaceRoot();
            var electricBookPath = ResolveElectricBookPath(workspaceRoot);
            if (!Directory.Exists(electricBookPath))
            {
                return NotFound(new { error = $"electric-book repo not found: {electricBookPath}" });
            }

            var templateBookPath = Path.Combine(electricBookPath, "book");
            if (!Directory.Exists(templateBookPath))
            {
                return NotFound(new { error = $"Template book folder not found: {templateBookPath}" });
            }

            var bookSlug = ResolveBookSlug(request.BookSlug, qualification.QualificationNumber, qualification.QualificationDescription);
            var bookPath = Path.Combine(electricBookPath, bookSlug);
            var worksPath = Path.Combine(electricBookPath, "_data", "works", bookSlug);
            var worksDefaultPath = Path.Combine(worksPath, "default.yml");

            var chapterWidth = subjects.Count >= 100 ? 3 : 2;
            var chapterFiles = new List<string>();
            var writes = new List<(string path, string content)>();

            writes.Add((Path.Combine(bookPath, "index.md"), "---\nstyle: cover-page\n---\n\n{% include cover %}\n"));
            writes.Add((Path.Combine(bookPath, "0-0-cover.md"), "---\nstyle: cover-page\n---\n\n{% include cover %}\n"));
            writes.Add((Path.Combine(bookPath, "0-1-titlepage.md"), "---\ntitle: Title page\nstyle: title-page\n---\n\n{% include title-page %}\n"));
            writes.Add((Path.Combine(bookPath, "0-2-copyright.md"), "---\ntitle: Copyright\nstyle: copyright-page\n---\n\n{% include copyright-page %}\n"));
            writes.Add((Path.Combine(bookPath, "0-3-contents.md"), "---\ntitle: Contents\nstyle: contents-page\n---\n\n{% include contents %}\n"));

            for (var i = 0; i < subjects.Count; i++)
            {
                var chapterNumber = i + 1;
                var subject = subjects[i];
                var file = $"{chapterNumber.ToString(new string('0', chapterWidth))}-{Slugify($"{subject.SubjectCode}-{subject.SubjectDescription}")}.md";
                var fileKey = Path.GetFileNameWithoutExtension(file);
                chapterFiles.Add(fileKey);
                writes.Add((Path.Combine(bookPath, file), BuildChapterMarkdown(
                    chapterNumber,
                    subject,
                    request.IncludeSubjectPurpose != false,
                    request.IncludeTopicPurpose != false)));
            }

            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var worksYaml = BuildWorksDefaultYaml(
                qualification.QualificationNumber,
                qualification.QualificationDescription,
                bookSlug,
                date,
                chapterFiles);
            writes.Add((worksDefaultPath, worksYaml));

            if (request.DryRun == true)
            {
                return Ok(new
                {
                    success = true,
                    dryRun = true,
                    bookSlug,
                    electricBookPath,
                    bookPath,
                    worksPath,
                    subjectCount = subjects.Count,
                    topicCount = topics.Count,
                    filesToWrite = writes.Select(w => w.path).ToList()
                });
            }

            Directory.CreateDirectory(bookPath);
            Directory.CreateDirectory(worksPath);
            CopyAssetDirectory(templateBookPath, bookPath, "styles", keepExisting: request.KeepExistingAssets != false);
            CopyAssetDirectory(templateBookPath, bookPath, "images", keepExisting: request.KeepExistingAssets != false);
            CopyAssetFile(templateBookPath, bookPath, "package.opf", keepExisting: request.KeepExistingAssets != false);
            CopyAssetFile(templateBookPath, bookPath, "toc.ncx", keepExisting: request.KeepExistingAssets != false);

            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in writes)
            {
                var dirName = Path.GetDirectoryName(file.path) ?? string.Empty;
                if (!string.Equals(Path.GetFullPath(dirName), Path.GetFullPath(bookPath), StringComparison.OrdinalIgnoreCase)) continue;
                reserved.Add(Path.GetFileName(file.path));
            }
            foreach (var existing in Directory.GetFiles(bookPath, "*.md"))
            {
                var fileName = Path.GetFileName(existing);
                if (reserved.Contains(fileName)) continue;
                System.IO.File.Delete(existing);
            }

            foreach (var write in writes)
            {
                var parent = Path.GetDirectoryName(write.path);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                System.IO.File.WriteAllText(write.path, write.content.Replace("\n", Environment.NewLine), Encoding.UTF8);
            }

            return Ok(new
            {
                success = true,
                dryRun = false,
                message = "Electric Book mapping completed.",
                bookSlug,
                electricBookPath,
                bookPath,
                worksPath,
                subjectCount = subjects.Count,
                topicCount = topics.Count,
                mappedFiles = writes.Select(w => w.path).ToList()
            });
        }

        [HttpPost("trigger")]
        public async Task<IActionResult> Trigger([FromBody] ElectricBookTriggerRequest? request, CancellationToken cancellationToken)
        {
            request ??= new ElectricBookTriggerRequest();
            if (request.QualificationId <= 0) return BadRequest(new { error = "qualificationId is required." });

            var qualification = await _context.Qualifications
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == request.QualificationId, cancellationToken);
            if (qualification == null) return NotFound(new { error = $"Qualification not found: {request.QualificationId}" });

            var workspaceRoot = ResolveWorkspaceRoot();
            var electricBookPath = ResolveElectricBookPath(workspaceRoot);
            if (!Directory.Exists(electricBookPath))
            {
                return NotFound(new { error = $"electric-book repo not found: {electricBookPath}" });
            }

            var bookSlug = ResolveBookSlug(request.BookSlug, qualification.QualificationNumber, qualification.QualificationDescription);
            var bookPath = Path.Combine(electricBookPath, bookSlug);
            if (!Directory.Exists(bookPath))
            {
                return NotFound(new { error = $"Mapped book folder not found: {bookPath}. Run /api/ElectricBookExport/map first." });
            }

            var operation = (request.Operation ?? "output").Trim().ToLowerInvariant();
            if (operation != "output" && operation != "export" && operation != "check" && operation != "list-commands")
            {
                return BadRequest(new { error = "operation must be output, export, check, or list-commands." });
            }

            var npmExe = ResolveNpmExe(request.NpmExecutable);
            var args = new List<string> { "run", "eb" };
            if (operation == "check")
            {
                args.Add("--");
                args.Add("check");
            }
            else if (operation == "output" || operation == "export")
            {
                args.Add("--");
                args.Add(operation);
                args.Add("--book");
                args.Add(bookSlug);
                if (!string.IsNullOrWhiteSpace(request.Format))
                {
                    args.Add("--format");
                    args.Add(request.Format!.Trim());
                }
                if (!string.IsNullOrWhiteSpace(request.Language))
                {
                    args.Add("--language");
                    args.Add(request.Language!.Trim());
                }
                if (request.Incremental == true) args.Add("--incremental");
                if (request.MathJax.HasValue) { args.Add("--mathjax"); args.Add(request.MathJax.Value ? "true" : "false"); }
                if (request.DebugJs.HasValue) { args.Add("--debugjs"); args.Add(request.DebugJs.Value ? "true" : "false"); }
                if (request.SkipWebpack.HasValue) { args.Add("--skipwebpack"); args.Add(request.SkipWebpack.Value ? "true" : "false"); }
            }

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);
            var command = BuildCommandString(npmExe, args);

            if (request.DryRun == true)
            {
                return Ok(new { success = true, dryRun = true, command, workingDirectory = electricBookPath });
            }

            var result = await RunProcessAsync(npmExe, args, electricBookPath, request.TimeoutSeconds, cancellationToken);
            return Ok(new
            {
                success = result.Success,
                message = result.Message,
                command = result.Command,
                exitCode = result.ExitCode,
                stdout = result.StdOut,
                stderr = result.StdErr,
                workingDirectory = electricBookPath
            });
        }

        private static string BuildChapterMarkdown(int chapterNumber, SubjectMapRow subject, bool includeSubjectPurpose, bool includeTopicPurpose)
        {
            var title = $"{subject.SubjectCode} - {subject.SubjectDescription}".Trim(' ', '-');
            if (string.IsNullOrWhiteSpace(title)) title = $"Subject {chapterNumber}";

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"title: \"{EscapeDoubleQuotes($"{chapterNumber}. {title}")}\"");
            sb.AppendLine($"style: default-page page-{chapterNumber}");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"# **{chapterNumber}** {title}");
            sb.AppendLine();

            if (includeSubjectPurpose && !string.IsNullOrWhiteSpace(subject.SubjectPurpose))
            {
                sb.AppendLine("## Subject Purpose");
                sb.AppendLine();
                sb.AppendLine(subject.SubjectPurpose.Trim());
                sb.AppendLine();
            }

            if (subject.Topics.Count == 0)
            {
                sb.AppendLine("_No topics captured yet for this subject._");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine("## Topics");
            sb.AppendLine();
            for (var i = 0; i < subject.Topics.Count; i++)
            {
                var topic = subject.Topics[i];
                var heading = BuildTopicHeading(topic, i + 1);
                sb.AppendLine($"### {heading}");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(topic.TopicDescription))
                {
                    sb.AppendLine(topic.TopicDescription.Trim());
                    sb.AppendLine();
                }
                var stats = new List<string>();
                if (topic.NotionalHours.HasValue) stats.Add($"Notional Hours: {topic.NotionalHours.Value:0.##}");
                if (topic.PeriodsPerTopic.HasValue) stats.Add($"Periods/Topic: {topic.PeriodsPerTopic.Value:0.##}");
                if (stats.Count > 0)
                {
                    sb.AppendLine($"- {string.Join(" | ", stats)}");
                    sb.AppendLine();
                }
                if (includeTopicPurpose && !string.IsNullOrWhiteSpace(topic.TopicPurpose))
                {
                    sb.AppendLine("#### Topic Purpose");
                    sb.AppendLine();
                    sb.AppendLine(topic.TopicPurpose.Trim());
                    sb.AppendLine();
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildTopicHeading(TopicMapRow topic, int ordinal)
        {
            var code = (topic.TopicCode ?? string.Empty).Trim();
            var desc = (topic.TopicDescription ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(desc)) return $"{code}: {desc}";
            if (!string.IsNullOrWhiteSpace(code)) return code;
            if (!string.IsNullOrWhiteSpace(desc)) return desc;
            return $"Topic {ordinal}";
        }

        private static string BuildWorksDefaultYaml(
            string qualificationNumber,
            string qualificationDescription,
            string bookSlug,
            string date,
            List<string> chapterFiles)
        {
            var files = new List<string> { "0-0-cover", "0-1-titlepage", "0-2-copyright", "0-3-contents" };
            files.AddRange(chapterFiles);
            var title = string.IsNullOrWhiteSpace(qualificationDescription) ? $"Qualification {qualificationNumber}" : qualificationDescription.Trim();
            var subtitle = string.IsNullOrWhiteSpace(qualificationNumber) ? "ETDP Electric Book Export" : $"Qualification {qualificationNumber}";

            var sb = new StringBuilder();
            sb.AppendLine("# Auto-generated by ETDP");
            sb.AppendLine($"title: \"{EscapeDoubleQuotes(title)}\"");
            sb.AppendLine($"subtitle: \"{EscapeDoubleQuotes(subtitle)}\"");
            sb.AppendLine("creator: \"ETDP\"");
            sb.AppendLine("contributor: \"\"");
            sb.AppendLine("description: |");
            sb.AppendLine("  Auto-generated from ETDP subject/topic mapping.");
            sb.AppendLine($"  Qualification Number: {qualificationNumber}");
            sb.AppendLine($"  Qualification Description: {qualificationDescription}");
            sb.AppendLine("image: \"cover.jpg\"");
            sb.AppendLine("publisher: \"ETDP\"");
            sb.AppendLine("publisher-url: \"\"");
            sb.AppendLine("rightsholder: \"\"");
            sb.AppendLine("rights: |");
            sb.AppendLine("  Internal ETDP export.");
            sb.AppendLine("language: \"en\"");
            sb.AppendLine($"date: \"{date}\"");
            sb.AppendLine($"modified: \"{date}\"");
            sb.AppendLine("type: \"Training Guide\"");
            sb.AppendLine("subject: \"Education and Training\"");
            sb.AppendLine($"identifier: \"{EscapeDoubleQuotes(bookSlug)}\"");
            sb.AppendLine("keywords: \"ETDP, curriculum\"");
            sb.AppendLine("products:");
            sb.AppendLine("  web:");
            sb.AppendLine("    format: \"Online\"");
            sb.AppendLine("    files:");
            foreach (var file in files) sb.AppendLine($"      - \"{EscapeDoubleQuotes(file)}\"");
            sb.AppendLine("  print-pdf:");
            sb.AppendLine("    format: \"Print\"");
            sb.AppendLine("    files:");
            foreach (var file in files) sb.AppendLine($"      - \"{EscapeDoubleQuotes(file)}\"");
            sb.AppendLine("  screen-pdf:");
            sb.AppendLine("    format: \"PDF\"");
            sb.AppendLine("    files:");
            foreach (var file in files) sb.AppendLine($"      - \"{EscapeDoubleQuotes(file)}\"");
            sb.AppendLine("  epub:");
            sb.AppendLine("    format: \"Ebook\"");
            sb.AppendLine("    contents-page: \"0-3-contents\"");
            sb.AppendLine("    files:");
            foreach (var file in files) sb.AppendLine($"      - \"{EscapeDoubleQuotes(file)}\"");
            return sb.ToString().TrimEnd();
        }

        private static void CopyAssetDirectory(string templateBookPath, string targetBookPath, string relativeDir, bool keepExisting)
        {
            var sourceDir = Path.Combine(templateBookPath, relativeDir);
            if (!Directory.Exists(sourceDir)) return;
            var targetDir = Path.Combine(targetBookPath, relativeDir);
            Directory.CreateDirectory(targetDir);

            foreach (var sourceFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, sourceFile);
                var targetFile = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile) ?? targetDir);
                if (keepExisting && System.IO.File.Exists(targetFile)) continue;
                System.IO.File.Copy(sourceFile, targetFile, overwrite: true);
            }
        }

        private static void CopyAssetFile(string templateBookPath, string targetBookPath, string fileName, bool keepExisting)
        {
            var sourceFile = Path.Combine(templateBookPath, fileName);
            if (!System.IO.File.Exists(sourceFile)) return;
            var targetFile = Path.Combine(targetBookPath, fileName);
            if (keepExisting && System.IO.File.Exists(targetFile)) return;
            System.IO.File.Copy(sourceFile, targetFile, overwrite: true);
        }

        private string ResolveWorkspaceRoot()
        {
            var fromEnv = (Environment.GetEnvironmentVariable("ETDP_WORKSPACE_ROOT") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv)) return fromEnv;

            var parent = Directory.GetParent(_environment.ContentRootPath)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)) return parent;
            return _environment.ContentRootPath;
        }

        private string ResolveElectricBookPath(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(workspaceRoot, "electric-book"),
                Path.Combine(workspaceRoot, "_external", "electric-book"),
                Path.Combine(_environment.ContentRootPath, "_external", "electric-book"),
                Path.Combine(workspaceRoot, "ETDP", "_external", "electric-book")
            };
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate)) return candidate;
            }
            return candidates[0];
        }

        private static string ResolveBookSlug(string? requested, string qualificationNumber, string qualificationDescription)
        {
            var raw = (requested ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = !string.IsNullOrWhiteSpace(qualificationNumber) ? qualificationNumber : qualificationDescription;
                raw = $"etdp-{raw}";
            }
            return Slugify(raw);
        }

        private static string Slugify(string? value)
        {
            var raw = (value ?? string.Empty).Trim().ToLowerInvariant();
            raw = Regex.Replace(raw, @"[^a-z0-9]+", "-");
            raw = Regex.Replace(raw, @"-+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(raw)) raw = "etdp-export";
            if (raw.Length > 64) raw = raw.Substring(0, 64).Trim('-');
            return raw;
        }

        private static string EscapeDoubleQuotes(string? value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ResolveNpmExe(string? explicitPath)
        {
            var direct = (explicitPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(direct)) return direct;
            return OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
        }

        private static List<string> SplitCommandLine(string? commandLine)
        {
            var raw = (commandLine ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

            var result = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < raw.Length; i++)
            {
                var ch = raw[i];
                if (ch == '"') { inQuotes = !inQuotes; continue; }
                if (!inQuotes && char.IsWhiteSpace(ch))
                {
                    if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                    continue;
                }
                current.Append(ch);
            }
            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }

        private static string BuildCommandString(string fileName, List<string> args)
        {
            static string Q(string v)
            {
                if (string.IsNullOrWhiteSpace(v)) return "\"\"";
                return v.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
                    ? $"\"{v.Replace("\"", "\\\"")}\""
                    : v;
            }
            return string.Join(" ", new[] { Q(fileName) }.Concat(args.Select(Q)));
        }

        private async Task<ProcessResult> RunProcessAsync(string fileName, List<string> args, string workingDirectory, int? timeoutSeconds, CancellationToken cancellationToken)
        {
            var timeout = Math.Clamp(timeoutSeconds ?? 1800, 10, 7200);
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);

            try
            {
                using var process = Process.Start(psi);
                if (process == null) return ProcessResult.Fail("Failed to start process.", BuildCommandString(fileName, args));

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var stdOutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
                var stdErrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
                await process.WaitForExitAsync(linkedCts.Token);

                var stdout = await stdOutTask;
                var stderr = await stdErrTask;
                return new ProcessResult
                {
                    Success = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    Command = BuildCommandString(fileName, args),
                    Message = process.ExitCode == 0 ? "Process completed successfully." : $"Process exited with code {process.ExitCode}.",
                    StdOut = TrimOutput(stdout),
                    StdErr = TrimOutput(stderr)
                };
            }
            catch (OperationCanceledException)
            {
                return ProcessResult.Fail($"Process timed out after {timeout} seconds.", BuildCommandString(fileName, args));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Electric Book trigger process failed");
                return ProcessResult.Fail(ex.Message, BuildCommandString(fileName, args));
            }
        }

        private static string TrimOutput(string? text, int maxChars = 16000)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Length <= maxChars) return value;
            return value.Substring(value.Length - maxChars);
        }

        public sealed class ElectricBookMapRequest
        {
            public int QualificationId { get; set; }
            public string? BookSlug { get; set; }
            public bool? IncludeSubjectPurpose { get; set; } = true;
            public bool? IncludeTopicPurpose { get; set; } = true;
            public bool? KeepExistingAssets { get; set; } = true;
            public bool? DryRun { get; set; } = false;
        }

        public sealed class ElectricBookTriggerRequest
        {
            public int QualificationId { get; set; }
            public string? BookSlug { get; set; }
            public string? Operation { get; set; } = "output";
            public string? Format { get; set; } = "web";
            public string? Language { get; set; }
            public bool? Incremental { get; set; }
            public bool? MathJax { get; set; }
            public bool? DebugJs { get; set; }
            public bool? SkipWebpack { get; set; }
            public string? ExtraArgs { get; set; }
            public string? NpmExecutable { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; } = false;
        }

        private sealed class SubjectMapRow
        {
            public int Id { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public string SubjectPurpose { get; set; } = string.Empty;
            public List<TopicMapRow> Topics { get; set; } = new();
        }

        private sealed class TopicMapRow
        {
            public int SubjectId { get; set; }
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string TopicPurpose { get; set; } = string.Empty;
            public double? NotionalHours { get; set; }
            public double? PeriodsPerTopic { get; set; }
        }

        private sealed class ProcessResult
        {
            public bool Success { get; set; }
            public int? ExitCode { get; set; }
            public string Command { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string StdOut { get; set; } = string.Empty;
            public string StdErr { get; set; } = string.Empty;

            public static ProcessResult Fail(string message, string command)
            {
                return new ProcessResult
                {
                    Success = false,
                    ExitCode = null,
                    Command = command,
                    Message = message,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                };
            }
        }
    }
}
