using System.Diagnostics;
using System.Globalization;
using System.Text;
using ETD.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProjectRolloutController : ControllerBase
    {
        private static readonly DateOnly DefaultStartDate = new(2026, 2, 22);
        private static readonly DateOnly DefaultEndDate = new(2027, 12, 17);

        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public ProjectRolloutController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        [HttpPost("preview")]
        public async Task<IActionResult> Preview([FromBody] ProjectRolloutRequest? request)
        {
            try
            {
                var resolved = await ResolveRequestAsync(request);
                var preview = BuildPreview(resolved);
                return Ok(preview);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("export")]
        public async Task<IActionResult> Export([FromBody] ProjectRolloutRequest? request)
        {
            ResolvedRolloutRequest resolved;
            ProjectRolloutPreviewResponse preview;
            try
            {
                resolved = await ResolveRequestAsync(request);
                preview = BuildPreview(resolved);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }

            var scriptPath = Path.Combine(_environment.ContentRootPath, "Exports", "ProjectTemplate", "generate_project_rollout_plan.py");
            if (!System.IO.File.Exists(scriptPath))
            {
                return NotFound(new { error = $"Rollout generator script not found: {scriptPath}" });
            }

            string outputDirectory;
            try
            {
                outputDirectory = await ResolveProjectRolloutOutputDirectoryAsync(resolved);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var outputPath = Path.Combine(outputDirectory, $"Project_Plan_Rollout_{stamp}.xlsx");
            var summaryPath = Path.Combine(outputDirectory, $"ProjectPlan_Rollout_Summary_{stamp}.md");

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                WorkingDirectory = _environment.ContentRootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("--template");
            psi.ArgumentList.Add(resolved.TemplatePath);
            psi.ArgumentList.Add("--schedule-csv");
            psi.ArgumentList.Add(resolved.ScheduleCsvPath);
            psi.ArgumentList.Add("--output");
            psi.ArgumentList.Add(outputPath);
            psi.ArgumentList.Add("--summary");
            psi.ArgumentList.Add(summaryPath);
            psi.ArgumentList.Add("--start-date");
            psi.ArgumentList.Add(resolved.StartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--end-date");
            psi.ArgumentList.Add(resolved.EndDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--credits");
            psi.ArgumentList.Add(resolved.Credits.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--learning-days");
            psi.ArgumentList.Add(resolved.LearningDays.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--semesters");
            psi.ArgumentList.Add(resolved.Semesters.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--break-days");
            psi.ArgumentList.Add(resolved.BreakDays.ToString(CultureInfo.InvariantCulture));

            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                {
                    return StatusCode(500, new { error = "Failed to start Python process." });
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var stdOutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var stdErrTask = process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);
                var stdOut = await stdOutTask;
                var stdErr = await stdErrTask;

                if (process.ExitCode != 0)
                {
                    return StatusCode(500, new
                    {
                        error = "Rollout export generation failed.",
                        exitCode = process.ExitCode,
                        stderr = stdErr,
                        stdout = stdOut
                    });
                }
            }
            catch (OperationCanceledException)
            {
                return StatusCode(504, new { error = "Rollout export generation timed out." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to run rollout generator: {ex.Message}" });
            }

            if (!System.IO.File.Exists(outputPath))
            {
                return StatusCode(500, new { error = $"Generator completed but output file was not created: {outputPath}" });
            }

            var fileBytes = await System.IO.File.ReadAllBytesAsync(outputPath);
            var downloadName = Path.GetFileName(outputPath);
            Response.Headers["X-Rollout-Output-Path"] = outputPath;
            Response.Headers["X-Rollout-Summary-Path"] = summaryPath;
            Response.Headers["X-Rollout-Schedule-Path"] = resolved.ScheduleCsvPath;
            Response.Headers["X-Rollout-Learning-Days"] = preview.LearningDays.ToString(CultureInfo.InvariantCulture);
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", downloadName);
        }

        private async Task<string> ResolveProjectRolloutOutputDirectoryAsync(ResolvedRolloutRequest resolved)
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documentsPath))
            {
                throw new InvalidOperationException("Unable to resolve My Documents path.");
            }

            var qualificationFolder = await ResolveQualificationFolderNameAsync(resolved);
            var rootPath = string.IsNullOrWhiteSpace(qualificationFolder)
                ? documentsPath
                : Path.Combine(documentsPath, qualificationFolder);

            var outputDirectory = Path.Combine(rootPath, "Project Roll Out Plan");
            Directory.CreateDirectory(outputDirectory);
            return outputDirectory;
        }

        private async Task<string> ResolveQualificationFolderNameAsync(ResolvedRolloutRequest resolved)
        {
            var qualificationId = resolved.QualificationId.GetValueOrDefault();
            if (qualificationId > 0)
            {
                var qualification = await _context.Qualifications
                    .Where(q => q.Id == qualificationId)
                    .Select(q => new { q.QualificationNumber, q.QualificationDescription, q.LearningInstitutionName })
                    .FirstOrDefaultAsync();

                if (qualification != null)
                {
                    return BuildRootDirectoryName(
                        qualification.QualificationNumber,
                        qualification.QualificationDescription,
                        qualification.LearningInstitutionName,
                        qualificationId);
                }

                return $"Qualification_{qualificationId}";
            }

            return MakeSafePathPart(resolved.QualificationNumber);
        }

        private static string BuildRootDirectoryName(string? qualificationNumber, string? qualificationDescription, string? learningInstitutionName, int qualificationId)
        {
            var safeNumber = MakeSafePathPart(qualificationNumber);
            var safeDescription = MakeSafePathPart(qualificationDescription);
            var safeInstitution = MakeSafePathPart(learningInstitutionName);

            if (safeNumber.Length == 0 && safeDescription.Length == 0 && safeInstitution.Length == 0)
            {
                return $"Qualification_{qualificationId}";
            }

            if (safeDescription.Length == 0 && safeInstitution.Length == 0)
            {
                return safeNumber;
            }

            if (safeNumber.Length == 0 && safeInstitution.Length == 0)
            {
                return safeDescription;
            }

            var parts = new List<string>();
            if (safeNumber.Length > 0) parts.Add(safeNumber);
            if (safeDescription.Length > 0) parts.Add(safeDescription);
            if (safeInstitution.Length > 0) parts.Add(safeInstitution);
            return string.Join(" - ", parts);
        }

        private static string MakeSafePathPart(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            var cleaned = new string(chars);
            while (cleaned.Contains("  ", StringComparison.Ordinal))
            {
                cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
            }

            return cleaned.Trim();
        }

        private async Task<ResolvedRolloutRequest> ResolveRequestAsync(ProjectRolloutRequest? request)
        {
            request ??= new ProjectRolloutRequest();

            var startDate = ParseDateOrDefault(request.StartDate, DefaultStartDate);
            var endDate = ParseDateOrDefault(request.EndDate, DefaultEndDate);
            var credits = request.Credits.GetValueOrDefault(548);
            var learningDays = request.LearningDays.GetValueOrDefault(228);
            var semesters = request.Semesters.GetValueOrDefault(4);
            var breakDays = request.BreakDays.GetValueOrDefault(4);

            if (credits <= 0) throw new InvalidOperationException("Credits must be greater than 0.");
            if (learningDays <= 0) throw new InvalidOperationException("LearningDays must be greater than 0.");
            if (semesters <= 0) throw new InvalidOperationException("Semesters must be greater than 0.");
            if (breakDays < 0) throw new InvalidOperationException("BreakDays cannot be negative.");
            if (endDate <= startDate) throw new InvalidOperationException("End date must be after start date.");

            var templatePath = Path.Combine(_environment.ContentRootPath, "Exports", "ProjectTemplate", "Fitter_and_Turner_Project_Plan.xlsx");
            if (!System.IO.File.Exists(templatePath))
            {
                throw new InvalidOperationException($"Rollout template not found: {templatePath}");
            }

            var qualificationNumber = request.QualificationNumber;
            if (string.IsNullOrWhiteSpace(qualificationNumber) && request.QualificationId.HasValue && request.QualificationId.Value > 0)
            {
                qualificationNumber = await _context.Qualifications
                    .Where(q => q.Id == request.QualificationId.Value)
                    .Select(q => q.QualificationNumber)
                    .FirstOrDefaultAsync();
            }

            var scheduleCsvPath = await ResolveScheduleCsvPathAsync(
                Path.Combine(_environment.ContentRootPath, "Exports"),
                request.QualificationId,
                qualificationNumber);

            return new ResolvedRolloutRequest
            {
                StartDate = startDate,
                EndDate = endDate,
                Credits = credits,
                LearningDays = learningDays,
                Semesters = semesters,
                BreakDays = breakDays,
                TemplatePath = templatePath,
                ScheduleCsvPath = scheduleCsvPath,
                QualificationId = request.QualificationId,
                QualificationNumber = qualificationNumber
            };
        }

        private static string ResolveScheduleCsvPath(string exportsRoot, string? qualificationNumber)
        {
            if (!Directory.Exists(exportsRoot))
            {
                throw new InvalidOperationException($"Exports folder not found: {exportsRoot}");
            }

            var candidates = new List<FileInfo>();

            if (!string.IsNullOrWhiteSpace(qualificationNumber))
            {
                var qn = qualificationNumber.Trim();
                var matchingBuildDirs = Directory.GetDirectories(exportsRoot, $"Build_{qn}_*", SearchOption.TopDirectoryOnly);
                foreach (var dir in matchingBuildDirs)
                {
                    var path = Path.Combine(dir, "Schedule", "learning_schedule.csv");
                    if (System.IO.File.Exists(path))
                    {
                        candidates.Add(new FileInfo(path));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                var allBuildDirs = Directory.GetDirectories(exportsRoot, "Build_*", SearchOption.TopDirectoryOnly);
                foreach (var dir in allBuildDirs)
                {
                    var path = Path.Combine(dir, "Schedule", "learning_schedule.csv");
                    if (System.IO.File.Exists(path))
                    {
                        candidates.Add(new FileInfo(path));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("No schedule source found. Expected Build_*/Schedule/learning_schedule.csv under Exports.");
            }

            return candidates
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .First()
                .FullName;
        }

        private async Task<string> ResolveScheduleCsvPathAsync(string exportsRoot, int? qualificationId, string? qualificationNumber)
        {
            if (qualificationId.GetValueOrDefault() > 0)
            {
                try
                {
                    return await BuildScheduleCsvFromToolkitAsync(exportsRoot, qualificationId, qualificationNumber);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("No schedule source found", StringComparison.OrdinalIgnoreCase))
                {
                    // Fall through to legacy file-based lookup.
                }
            }

            try
            {
                return ResolveScheduleCsvPath(exportsRoot, qualificationNumber);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("No schedule source found", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("Exports folder not found", StringComparison.OrdinalIgnoreCase))
            {
                return await BuildScheduleCsvFromToolkitAsync(exportsRoot, qualificationId, qualificationNumber);
            }
        }

        private async Task<string> BuildScheduleCsvFromToolkitAsync(string exportsRoot, int? qualificationId, string? qualificationNumber)
        {
            Directory.CreateDirectory(exportsRoot);

            var resolvedQualificationId = qualificationId.GetValueOrDefault();
            var resolvedQualificationNumber = (qualificationNumber ?? string.Empty).Trim();
            var resolvedInstitutionName = string.Empty;

            if (resolvedQualificationId <= 0 && !string.IsNullOrWhiteSpace(qualificationNumber))
            {
                resolvedQualificationId = await _context.Qualifications
                    .Where(q => q.QualificationNumber == qualificationNumber)
                    .Select(q => q.Id)
                    .FirstOrDefaultAsync();
            }

            if (resolvedQualificationId > 0)
            {
                var q = await _context.Qualifications
                    .Where(x => x.Id == resolvedQualificationId)
                    .Select(x => new { x.QualificationNumber, x.LearningInstitutionName })
                    .FirstOrDefaultAsync();
                if (q != null)
                {
                    if (string.IsNullOrWhiteSpace(resolvedQualificationNumber))
                    {
                        resolvedQualificationNumber = (q.QualificationNumber ?? string.Empty).Trim();
                    }
                    resolvedInstitutionName = (q.LearningInstitutionName ?? string.Empty).Trim();
                }
            }

            var query = _context.LecturerToolkitEntries.AsNoTracking().AsQueryable();
            if (resolvedQualificationId > 0)
            {
                query = query.Where(x => x.QualificationsId == resolvedQualificationId);
            }

            var rows = await query
                .OrderBy(x => x.Id)
                .Select(x => new
                {
                    x.SubjectCode,
                    x.SubjectDescription,
                    x.Lpn,
                    x.LessonPlanDescription,
                    x.AssessmentCriteriaDescription,
                    x.LecturerActions,
                    x.LearnerActions,
                    x.LearningAids,
                    x.TimeStart,
                    x.TimeEnd
                })
                .ToListAsync();

            if (rows.Count == 0)
            {
                var qLabel = resolvedQualificationId > 0
                    ? $"qualification id {resolvedQualificationId}"
                    : (!string.IsNullOrWhiteSpace(resolvedQualificationNumber) ? $"qualification number {resolvedQualificationNumber}" : "the selected qualification");
                throw new InvalidOperationException(
                    $"No learning schedule data found for {qLabel}. " +
                    "Complete Lesson Plan / Lecturer Toolkit data first so the schedule can be auto-generated.");
            }

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var safeNumber = MakeSafePathPart(resolvedQualificationNumber);
            if (string.IsNullOrWhiteSpace(safeNumber))
            {
                safeNumber = resolvedQualificationId > 0 ? resolvedQualificationId.ToString(CultureInfo.InvariantCulture) : "AUTO";
            }
            var safeInstitution = MakeSafePathPart(resolvedInstitutionName);
            if (string.IsNullOrWhiteSpace(safeInstitution))
            {
                safeInstitution = "Institution";
            }

            var buildPrefix = resolvedQualificationId > 0
                ? $"Build_{safeNumber}_{safeInstitution}_QID{resolvedQualificationId}_{stamp}"
                : $"Build_{safeNumber}_{safeInstitution}_{stamp}";
            var scheduleDir = Path.Combine(exportsRoot, buildPrefix, "Schedule");
            Directory.CreateDirectory(scheduleDir);
            var schedulePath = Path.Combine(scheduleDir, "learning_schedule.csv");

            using (var writer = new StreamWriter(schedulePath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("Date,Day,Period,TimeStart,TimeEnd,SubjectCode,TopicCode,TopicDescription,LPN,LessonPlanDescription,AssessmentCriteriaDescription,LecturerActions,LearnerActions,LearningAids");
                for (var i = 0; i < rows.Count; i++)
                {
                    var period = (i % 13) + 1;
                    var generatedStart = TimeSpan.FromMinutes((period - 1) * 40).Add(new TimeSpan(8, 0, 0)).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
                    var generatedEnd = TimeSpan.FromMinutes(period * 40).Add(new TimeSpan(8, 0, 0)).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
                    var timeStart = string.IsNullOrWhiteSpace(rows[i].TimeStart) ? generatedStart : rows[i].TimeStart.Trim();
                    var timeEnd = string.IsNullOrWhiteSpace(rows[i].TimeEnd) ? generatedEnd : rows[i].TimeEnd.Trim();
                    var topicCode = (rows[i].SubjectCode ?? string.Empty).Trim();
                    var topicDescription = (rows[i].SubjectDescription ?? string.Empty).Trim();

                    var csv = string.Join(",",
                        EscapeCsv(string.Empty),
                        EscapeCsv(string.Empty),
                        EscapeCsv(period.ToString(CultureInfo.InvariantCulture)),
                        EscapeCsv(timeStart),
                        EscapeCsv(timeEnd),
                        EscapeCsv(topicCode),
                        EscapeCsv(topicCode),
                        EscapeCsv(topicDescription),
                        EscapeCsv((rows[i].Lpn ?? string.Empty).Trim()),
                        EscapeCsv((rows[i].LessonPlanDescription ?? string.Empty).Trim()),
                        EscapeCsv((rows[i].AssessmentCriteriaDescription ?? string.Empty).Trim()),
                        EscapeCsv((rows[i].LecturerActions ?? string.Empty).Trim()),
                        EscapeCsv((rows[i].LearnerActions ?? string.Empty).Trim()),
                        EscapeCsv((rows[i].LearningAids ?? string.Empty).Trim()));
                    writer.WriteLine(csv);
                }
            }

            return schedulePath;
        }

        private static string EscapeCsv(string? value)
        {
            var text = value ?? string.Empty;
            if (text.Contains(",") || text.Contains("\"") || text.Contains("\n") || text.Contains("\r"))
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            return text;
        }

        private static ProjectRolloutPreviewResponse BuildPreview(ResolvedRolloutRequest resolved)
        {
            var sourceSessions = CountCsvRows(resolved.ScheduleCsvPath);
            if (sourceSessions <= 0)
            {
                throw new InvalidOperationException($"Schedule CSV has no data rows: {resolved.ScheduleCsvPath}");
            }

            var (learningPlan, semesterRanges, breakRanges) = BuildSemesterPlan(
                resolved.StartDate,
                resolved.EndDate,
                resolved.Semesters,
                resolved.BreakDays,
                resolved.LearningDays);

            var sessionsPerDay = DistributeSessions(sourceSessions, resolved.LearningDays);
            if (sessionsPerDay.Count != learningPlan.Count)
            {
                throw new InvalidOperationException("Session distribution mismatch.");
            }

            var dailyPreview = learningPlan
                .Select((item, idx) => new DailyPreviewRow
                {
                    DayNumber = idx + 1,
                    Date = item.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DayName = item.Day.DayOfWeek.ToString(),
                    Semester = item.Semester,
                    SessionCount = sessionsPerDay[idx]
                })
                .Take(30)
                .ToList();

            return new ProjectRolloutPreviewResponse
            {
                QualificationId = resolved.QualificationId,
                QualificationNumber = resolved.QualificationNumber,
                TemplatePath = resolved.TemplatePath,
                ScheduleCsvPath = resolved.ScheduleCsvPath,
                StartDate = resolved.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = resolved.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Credits = resolved.Credits,
                NotionalHours = resolved.Credits * 10,
                LearningDays = resolved.LearningDays,
                Semesters = resolved.Semesters,
                BreakDays = resolved.BreakDays,
                SourceSessions = sourceSessions,
                SessionsPerDayMin = sessionsPerDay.Min(),
                SessionsPerDayMax = sessionsPerDay.Max(),
                SessionsPerDayAverage = Math.Round((double)sourceSessions / resolved.LearningDays, 2),
                SemesterRanges = semesterRanges,
                BreakRanges = breakRanges,
                DailyPreview = dailyPreview
            };
        }

        private static int CountCsvRows(string path)
        {
            using var reader = new StreamReader(path, Encoding.UTF8, true);
            var count = 0;
            var isFirst = true;
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (isFirst)
                {
                    isFirst = false;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(line)) continue;
                count++;
            }
            return count;
        }

        private static DateOnly ParseDateOrDefault(string? value, DateOnly fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            var raw = value.Trim();

            if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            var formats = new[] { "dd/MM/yyyy", "yyyy-MM-dd", "dd-MM-yyyy" };
            if (DateOnly.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed;
            }

            throw new InvalidOperationException($"Unsupported date format: {value}");
        }

        private static DateOnly NextWeekday(DateOnly day)
        {
            var d = day;
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                d = d.AddDays(1);
            }
            return d;
        }

        private static List<DateOnly> NextNWeekdays(DateOnly startDay, int count)
        {
            var result = new List<DateOnly>();
            var d = NextWeekday(startDay);
            while (result.Count < count)
            {
                if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    result.Add(d);
                }
                d = d.AddDays(1);
            }
            return result;
        }

        private static (List<LearningDay> learning, List<SemesterRange> semesters, List<BreakRange> breaks) BuildSemesterPlan(
            DateOnly startDate,
            DateOnly endDate,
            int semesters,
            int breakDays,
            int learningDays)
        {
            if (endDate <= startDate) throw new InvalidOperationException("End date must be after start date.");
            if (semesters <= 0) throw new InvalidOperationException("Semesters must be positive.");
            if (learningDays <= 0) throw new InvalidOperationException("LearningDays must be positive.");

            var baseDays = learningDays / semesters;
            var extra = learningDays % semesters;
            var daysPerSemester = Enumerable.Range(0, semesters)
                .Select(i => baseDays + (i < extra ? 1 : 0))
                .ToArray();

            var span = endDate.DayNumber - startDate.DayNumber;
            var anchors = Enumerable.Range(0, semesters)
                .Select(i => startDate.AddDays((int)Math.Round(span * (double)i / semesters)))
                .ToArray();

            var allLearning = new List<LearningDay>();
            var semesterRanges = new List<SemesterRange>();
            var breakRanges = new List<BreakRange>();
            var cursor = startDate;

            for (var i = 0; i < semesters; i++)
            {
                var semNo = i + 1;
                var semStart = NextWeekday(anchors[i] > cursor ? anchors[i] : cursor);
                var semDays = NextNWeekdays(semStart, daysPerSemester[i]);
                var semEnd = semDays[^1];

                if (semEnd > endDate)
                {
                    throw new InvalidOperationException($"Semester {semNo} end ({semEnd:yyyy-MM-dd}) exceeds project end date ({endDate:yyyy-MM-dd}).");
                }

                semesterRanges.Add(new SemesterRange
                {
                    Semester = semNo,
                    StartDate = semDays[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    EndDate = semEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    LearningDays = semDays.Count
                });

                allLearning.AddRange(semDays.Select(d => new LearningDay { Semester = semNo, Day = d }));

                if (semNo < semesters && breakDays > 0)
                {
                    var breakDates = NextNWeekdays(semEnd.AddDays(1), breakDays);
                    var breakEnd = breakDates[^1];
                    if (breakEnd > endDate)
                    {
                        throw new InvalidOperationException($"Break after semester {semNo} exceeds project end date ({endDate:yyyy-MM-dd}).");
                    }

                    breakRanges.Add(new BreakRange
                    {
                        AfterSemester = semNo,
                        StartDate = breakDates[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        EndDate = breakEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        BreakDays = breakDates.Count
                    });

                    cursor = breakEnd.AddDays(1);
                }
                else
                {
                    cursor = semEnd.AddDays(1);
                }
            }

            return (allLearning, semesterRanges, breakRanges);
        }

        private static List<int> DistributeSessions(int totalSessions, int learningDays)
        {
            if (learningDays <= 0) throw new InvalidOperationException("LearningDays must be positive.");
            var baseCount = totalSessions / learningDays;
            var extra = totalSessions % learningDays;
            var list = new List<int>(learningDays);
            for (var i = 0; i < learningDays; i++)
            {
                list.Add(baseCount + (i < extra ? 1 : 0));
            }
            return list;
        }

        public sealed class ProjectRolloutRequest
        {
            public string? StartDate { get; set; }
            public string? EndDate { get; set; }
            public int? Credits { get; set; }
            public int? LearningDays { get; set; }
            public int? Semesters { get; set; }
            public int? BreakDays { get; set; }
            public int? QualificationId { get; set; }
            public string? QualificationNumber { get; set; }
        }

        private sealed class ResolvedRolloutRequest
        {
            public DateOnly StartDate { get; init; }
            public DateOnly EndDate { get; init; }
            public int Credits { get; init; }
            public int LearningDays { get; init; }
            public int Semesters { get; init; }
            public int BreakDays { get; init; }
            public int? QualificationId { get; init; }
            public string? QualificationNumber { get; init; }
            public string TemplatePath { get; init; } = string.Empty;
            public string ScheduleCsvPath { get; init; } = string.Empty;
        }

        private sealed class LearningDay
        {
            public int Semester { get; init; }
            public DateOnly Day { get; init; }
        }

        public sealed class ProjectRolloutPreviewResponse
        {
            public int? QualificationId { get; set; }
            public string? QualificationNumber { get; set; }
            public string TemplatePath { get; set; } = string.Empty;
            public string ScheduleCsvPath { get; set; } = string.Empty;
            public string StartDate { get; set; } = string.Empty;
            public string EndDate { get; set; } = string.Empty;
            public int Credits { get; set; }
            public int NotionalHours { get; set; }
            public int LearningDays { get; set; }
            public int Semesters { get; set; }
            public int BreakDays { get; set; }
            public int SourceSessions { get; set; }
            public int SessionsPerDayMin { get; set; }
            public int SessionsPerDayMax { get; set; }
            public double SessionsPerDayAverage { get; set; }
            public List<SemesterRange> SemesterRanges { get; set; } = new();
            public List<BreakRange> BreakRanges { get; set; } = new();
            public List<DailyPreviewRow> DailyPreview { get; set; } = new();
        }

        public sealed class SemesterRange
        {
            public int Semester { get; set; }
            public string StartDate { get; set; } = string.Empty;
            public string EndDate { get; set; } = string.Empty;
            public int LearningDays { get; set; }
        }

        public sealed class BreakRange
        {
            public int AfterSemester { get; set; }
            public string StartDate { get; set; } = string.Empty;
            public string EndDate { get; set; } = string.Empty;
            public int BreakDays { get; set; }
        }

        public sealed class DailyPreviewRow
        {
            public int DayNumber { get; set; }
            public string Date { get; set; } = string.Empty;
            public string DayName { get; set; } = string.Empty;
            public int Semester { get; set; }
            public int SessionCount { get; set; }
        }
    }
}
