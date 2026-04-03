using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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

            try
            {
                var scheduleRows = ReadScheduleCsvRows(resolved.ScheduleCsvPath);
                var rolloutRows = BuildRolloutPlanRows(resolved, scheduleRows);
                var fileBytes = BuildProjectRolloutWorkbook(preview, rolloutRows);
                await System.IO.File.WriteAllBytesAsync(outputPath, fileBytes);

                var summary = BuildProjectRolloutSummaryMarkdown(resolved, preview, rolloutRows, outputPath);
                await System.IO.File.WriteAllTextAsync(summaryPath, summary, new UTF8Encoding(true));

                var downloadName = Path.GetFileName(outputPath);
                Response.Headers["X-Rollout-Output-Path"] = outputPath;
                Response.Headers["X-Rollout-Summary-Path"] = summaryPath;
                Response.Headers["X-Rollout-Schedule-Path"] = resolved.ScheduleCsvPath;
                Response.Headers["X-Rollout-Learning-Days"] = preview.LearningDays.ToString(CultureInfo.InvariantCulture);
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", downloadName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to generate rollout workbook: {ex.Message}" });
            }
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

            var projectRoot = ResolveProjectRoot();
            var templatePath = Path.Combine(projectRoot, "Exports", "ProjectTemplate", "Fitter_and_Turner_Project_Plan.xlsx");
            var resolvedTemplatePath = System.IO.File.Exists(templatePath)
                ? templatePath
                : "ETDP native rollout generator";

            var qualificationNumber = request.QualificationNumber;
            if (string.IsNullOrWhiteSpace(qualificationNumber) && request.QualificationId.HasValue && request.QualificationId.Value > 0)
            {
                qualificationNumber = await _context.Qualifications
                    .Where(q => q.Id == request.QualificationId.Value)
                    .Select(q => q.QualificationNumber)
                    .FirstOrDefaultAsync();
            }

            var scheduleCsvPath = await ResolveScheduleCsvPathAsync(
                Path.Combine(projectRoot, "Exports"),
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
                TemplatePath = resolvedTemplatePath,
                ScheduleCsvPath = scheduleCsvPath,
                QualificationId = request.QualificationId,
                QualificationNumber = qualificationNumber
            };
        }

        private string ResolveProjectRoot()
        {
            var candidates = new List<string>();

            void AddCandidate(string? candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate)) return;
                var full = Path.GetFullPath(candidate);
                if (Directory.Exists(full))
                {
                    candidates.Add(full);
                }
            }

            var fromEnv = (Environment.GetEnvironmentVariable("ETDP_WORKSPACE_ROOT") ?? string.Empty).Trim();
            AddCandidate(fromEnv);
            AddCandidate(_environment.ContentRootPath);
            AddCandidate(AppContext.BaseDirectory);

            var cursor = _environment.ContentRootPath;
            for (var depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(cursor); depth++)
            {
                AddCandidate(cursor);
                AddCandidate(Path.Combine(cursor, "ETDP"));
                cursor = Directory.GetParent(cursor)?.FullName ?? string.Empty;
            }

            cursor = AppContext.BaseDirectory;
            for (var depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(cursor); depth++)
            {
                AddCandidate(cursor);
                AddCandidate(Path.Combine(cursor, "ETDP"));
                cursor = Directory.GetParent(cursor)?.FullName ?? string.Empty;
            }

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (System.IO.File.Exists(Path.Combine(candidate, "ETDP.csproj")))
                {
                    return candidate;
                }

                if (Directory.Exists(Path.Combine(candidate, "Exports", "ProjectTemplate")))
                {
                    return candidate;
                }
            }

            return _environment.ContentRootPath;
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
            var sourceSessions = CountCsvRecords(resolved.ScheduleCsvPath);
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

        private static int CountCsvRecords(string path)
        {
            return ReadScheduleCsvRows(path).Count;
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

        private static List<RolloutScheduleSourceRow> ReadScheduleCsvRows(string path)
        {
            if (!System.IO.File.Exists(path))
            {
                throw new InvalidOperationException($"Schedule CSV not found: {path}");
            }

            var raw = System.IO.File.ReadAllText(path, Encoding.UTF8);
            var records = ParseCsvRecords(raw);
            if (records.Count == 0)
            {
                return new List<RolloutScheduleSourceRow>();
            }

            var headers = records[0].ToArray();
            var index = headers
                .Select((value, idx) => new { Name = (value ?? string.Empty).Trim(), Index = idx })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Index, StringComparer.OrdinalIgnoreCase);

            string Read(string[] fields, params string[] names)
            {
                foreach (var name in names)
                {
                    if (index.TryGetValue(name, out var i) && i >= 0 && i < fields.Length)
                    {
                        return (fields[i] ?? string.Empty).Trim();
                    }
                }

                return string.Empty;
            }

            var rows = new List<RolloutScheduleSourceRow>();
            foreach (var record in records.Skip(1))
            {
                var fields = record?.ToArray() ?? Array.Empty<string>();
                if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                rows.Add(new RolloutScheduleSourceRow
                {
                    Date = Read(fields, "Date"),
                    Day = Read(fields, "Day"),
                    Period = Read(fields, "Period"),
                    TimeStart = Read(fields, "TimeStart"),
                    TimeEnd = Read(fields, "TimeEnd"),
                    SubjectCode = Read(fields, "SubjectCode"),
                    TopicCode = Read(fields, "TopicCode"),
                    TopicDescription = Read(fields, "TopicDescription"),
                    Lpn = Read(fields, "LPN"),
                    LessonPlanDescription = Read(fields, "LessonPlanDescription"),
                    AssessmentCriteriaDescription = Read(fields, "AssessmentCriteriaDescription"),
                    LecturerActions = Read(fields, "LecturerActions"),
                    LearnerActions = Read(fields, "LearnerActions"),
                    LearningAids = Read(fields, "LearningAids")
                });
            }

            return rows;
        }

        private static List<List<string>> ParseCsvRecords(string? raw)
        {
            var records = new List<List<string>>();
            var currentRecord = new List<string>();
            var currentField = new StringBuilder();
            var text = (raw ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            var inQuotes = false;

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    currentRecord.Add(currentField.ToString());
                    currentField.Clear();
                    continue;
                }

                if (ch == '\n' && !inQuotes)
                {
                    currentRecord.Add(currentField.ToString());
                    currentField.Clear();
                    if (currentRecord.Any(value => !string.IsNullOrWhiteSpace(value)))
                    {
                        records.Add(currentRecord);
                    }
                    currentRecord = new List<string>();
                    continue;
                }

                currentField.Append(ch);
            }

            if (currentField.Length > 0 || currentRecord.Count > 0)
            {
                currentRecord.Add(currentField.ToString());
                if (currentRecord.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    records.Add(currentRecord);
                }
            }

            return records;
        }

        private static List<RolloutPlanRow> BuildRolloutPlanRows(
            ResolvedRolloutRequest resolved,
            IReadOnlyList<RolloutScheduleSourceRow> scheduleRows)
        {
            var rows = (scheduleRows ?? Array.Empty<RolloutScheduleSourceRow>()).ToList();
            if (rows.Count == 0)
            {
                return new List<RolloutPlanRow>();
            }

            var (learningPlan, _, _) = BuildSemesterPlan(
                resolved.StartDate,
                resolved.EndDate,
                resolved.Semesters,
                resolved.BreakDays,
                resolved.LearningDays);
            var sessionsPerDay = DistributeSessions(rows.Count, resolved.LearningDays);
            if (learningPlan.Count != sessionsPerDay.Count)
            {
                throw new InvalidOperationException("Unable to align rollout dates to scheduled sessions.");
            }

            var rollout = new List<RolloutPlanRow>(rows.Count);
            var sourceIndex = 0;
            for (var dayIndex = 0; dayIndex < learningPlan.Count && sourceIndex < rows.Count; dayIndex++)
            {
                var learningDay = learningPlan[dayIndex];
                var daySessionCount = sessionsPerDay[dayIndex];
                for (var sessionIndex = 0; sessionIndex < daySessionCount && sourceIndex < rows.Count; sessionIndex++, sourceIndex++)
                {
                    var row = rows[sourceIndex];
                    var period = TryParsePositiveInt(row.Period);
                    if (period <= 0)
                    {
                        period = sessionIndex + 1;
                    }

                    var generatedStart = TimeSpan.FromMinutes((period - 1) * 40)
                        .Add(new TimeSpan(8, 0, 0))
                        .ToString(@"hh\:mm", CultureInfo.InvariantCulture);
                    var generatedEnd = TimeSpan.FromMinutes(period * 40)
                        .Add(new TimeSpan(8, 0, 0))
                        .ToString(@"hh\:mm", CultureInfo.InvariantCulture);

                    rollout.Add(new RolloutPlanRow
                    {
                        Date = learningDay.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        DayName = learningDay.Day.DayOfWeek.ToString(),
                        Semester = learningDay.Semester,
                        Period = period,
                        TimeStart = string.IsNullOrWhiteSpace(row.TimeStart) ? generatedStart : row.TimeStart,
                        TimeEnd = string.IsNullOrWhiteSpace(row.TimeEnd) ? generatedEnd : row.TimeEnd,
                        SubjectCode = row.SubjectCode,
                        TopicCode = string.IsNullOrWhiteSpace(row.TopicCode) ? row.SubjectCode : row.TopicCode,
                        TopicDescription = row.TopicDescription,
                        Lpn = row.Lpn,
                        LessonPlanDescription = row.LessonPlanDescription,
                        AssessmentCriteriaDescription = row.AssessmentCriteriaDescription,
                        LecturerActions = row.LecturerActions,
                        LearnerActions = row.LearnerActions,
                        LearningAids = row.LearningAids
                    });
                }
            }

            return rollout;
        }

        private static byte[] BuildProjectRolloutWorkbook(
            ProjectRolloutPreviewResponse preview,
            IReadOnlyList<RolloutPlanRow> rolloutRows)
        {
            using var ms = new MemoryStream();
            using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                AddWorksheet(workbookPart, sheets, 1U, "1 - Overview", BuildOverviewSheet(preview, rolloutRows));
                AddWorksheet(workbookPart, sheets, 2U, "2 - Semester Plan", BuildSemesterSheet(preview));
                AddWorksheet(workbookPart, sheets, 3U, "3 - Daily Preview", BuildDailyPreviewSheet(preview));
                AddWorksheet(workbookPart, sheets, 4U, "4 - Rollout Schedule", BuildRolloutScheduleSheet(rolloutRows));

                workbookPart.Workbook.Save();
            }

            ms.Position = 0;
            return ms.ToArray();
        }

        private static void AddWorksheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            uint sheetId,
            string name,
            IEnumerable<string[]> rows)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(new SheetData());
            var data = worksheetPart.Worksheet.GetFirstChild<SheetData>()!;
            foreach (var row in rows)
            {
                AppendTextRow(data, row);
            }

            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId,
                Name = name
            });
        }

        private static IEnumerable<string[]> BuildOverviewSheet(
            ProjectRolloutPreviewResponse preview,
            IReadOnlyList<RolloutPlanRow> rolloutRows)
        {
            var rows = new List<string[]>
            {
                new[] { "Project Rollout Plan - Overview" },
                new[] { "Generator", "ETDP native rollout generator" },
                new[] { "Qualification Id", preview.QualificationId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty },
                new[] { "Qualification Number", preview.QualificationNumber ?? string.Empty },
                new[] { "Template / Mode", preview.TemplatePath ?? string.Empty },
                new[] { "Schedule CSV Source", preview.ScheduleCsvPath ?? string.Empty },
                new[] { "Start Date", preview.StartDate },
                new[] { "End Date", preview.EndDate },
                new[] { "Credits", preview.Credits.ToString(CultureInfo.InvariantCulture) },
                new[] { "Notional Hours", preview.NotionalHours.ToString(CultureInfo.InvariantCulture) },
                new[] { "Learning Days", preview.LearningDays.ToString(CultureInfo.InvariantCulture) },
                new[] { "Semesters", preview.Semesters.ToString(CultureInfo.InvariantCulture) },
                new[] { "Break Days", preview.BreakDays.ToString(CultureInfo.InvariantCulture) },
                new[] { "Source Sessions", preview.SourceSessions.ToString(CultureInfo.InvariantCulture) },
                new[] { "Rollout Sessions Planned", (rolloutRows?.Count ?? 0).ToString(CultureInfo.InvariantCulture) },
                new[] { "Sessions / Day Min", preview.SessionsPerDayMin.ToString(CultureInfo.InvariantCulture) },
                new[] { "Sessions / Day Max", preview.SessionsPerDayMax.ToString(CultureInfo.InvariantCulture) },
                new[] { "Sessions / Day Average", preview.SessionsPerDayAverage.ToString("0.00", CultureInfo.InvariantCulture) }
            };

            rows.Add(Array.Empty<string>());
            rows.Add(new[] { "Semester", "Start Date", "End Date", "Learning Days" });
            foreach (var item in preview.SemesterRanges ?? new List<SemesterRange>())
            {
                rows.Add(new[]
                {
                    item.Semester.ToString(CultureInfo.InvariantCulture),
                    item.StartDate,
                    item.EndDate,
                    item.LearningDays.ToString(CultureInfo.InvariantCulture)
                });
            }

            return rows;
        }

        private static IEnumerable<string[]> BuildSemesterSheet(ProjectRolloutPreviewResponse preview)
        {
            var rows = new List<string[]>
            {
                new[] { "Semester", "Start Date", "End Date", "Learning Days", "Break After Semester", "Break Start", "Break End", "Break Days" }
            };

            var breaksBySemester = (preview.BreakRanges ?? new List<BreakRange>())
                .ToDictionary(x => x.AfterSemester, x => x);

            foreach (var semester in preview.SemesterRanges ?? new List<SemesterRange>())
            {
                breaksBySemester.TryGetValue(semester.Semester, out var breakRange);
                rows.Add(new[]
                {
                    semester.Semester.ToString(CultureInfo.InvariantCulture),
                    semester.StartDate,
                    semester.EndDate,
                    semester.LearningDays.ToString(CultureInfo.InvariantCulture),
                    breakRange != null ? breakRange.AfterSemester.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    breakRange?.StartDate ?? string.Empty,
                    breakRange?.EndDate ?? string.Empty,
                    breakRange?.BreakDays.ToString(CultureInfo.InvariantCulture) ?? string.Empty
                });
            }

            return rows;
        }

        private static IEnumerable<string[]> BuildDailyPreviewSheet(ProjectRolloutPreviewResponse preview)
        {
            var rows = new List<string[]>
            {
                new[] { "Day Number", "Date", "Day Name", "Semester", "Session Count" }
            };

            foreach (var item in preview.DailyPreview ?? new List<DailyPreviewRow>())
            {
                rows.Add(new[]
                {
                    item.DayNumber.ToString(CultureInfo.InvariantCulture),
                    item.Date,
                    item.DayName,
                    item.Semester.ToString(CultureInfo.InvariantCulture),
                    item.SessionCount.ToString(CultureInfo.InvariantCulture)
                });
            }

            return rows;
        }

        private static IEnumerable<string[]> BuildRolloutScheduleSheet(IReadOnlyList<RolloutPlanRow> rolloutRows)
        {
            var rows = new List<string[]>
            {
                new[]
                {
                    "Date",
                    "Day",
                    "Semester",
                    "Period",
                    "Time Start",
                    "Time End",
                    "Subject Code",
                    "Topic Code",
                    "Topic Description",
                    "LPN",
                    "Lesson Plan Description",
                    "Assessment Criteria Description",
                    "Lecturer Actions",
                    "Learner Actions",
                    "Learning Aids"
                }
            };

            foreach (var item in rolloutRows ?? Array.Empty<RolloutPlanRow>())
            {
                rows.Add(new[]
                {
                    item.Date,
                    item.DayName,
                    item.Semester.ToString(CultureInfo.InvariantCulture),
                    item.Period.ToString(CultureInfo.InvariantCulture),
                    item.TimeStart,
                    item.TimeEnd,
                    item.SubjectCode,
                    item.TopicCode,
                    item.TopicDescription,
                    item.Lpn,
                    item.LessonPlanDescription,
                    item.AssessmentCriteriaDescription,
                    item.LecturerActions,
                    item.LearnerActions,
                    item.LearningAids
                });
            }

            return rows;
        }

        private static string BuildProjectRolloutSummaryMarkdown(
            ResolvedRolloutRequest resolved,
            ProjectRolloutPreviewResponse preview,
            IReadOnlyList<RolloutPlanRow> rolloutRows,
            string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Project Rollout Plan Summary");
            sb.AppendLine();
            sb.AppendLine($"- Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- Generator: ETDP native rollout generator");
            sb.AppendLine($"- Qualification Id: {resolved.QualificationId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}");
            sb.AppendLine($"- Qualification Number: {resolved.QualificationNumber ?? string.Empty}");
            sb.AppendLine($"- Start Date: {preview.StartDate}");
            sb.AppendLine($"- End Date: {preview.EndDate}");
            sb.AppendLine($"- Credits: {preview.Credits}");
            sb.AppendLine($"- Notional Hours: {preview.NotionalHours}");
            sb.AppendLine($"- Learning Days: {preview.LearningDays}");
            sb.AppendLine($"- Semesters: {preview.Semesters}");
            sb.AppendLine($"- Break Days: {preview.BreakDays}");
            sb.AppendLine($"- Source Sessions: {preview.SourceSessions}");
            sb.AppendLine($"- Planned Rollout Sessions: {rolloutRows?.Count ?? 0}");
            sb.AppendLine($"- Sessions / Day Range: {preview.SessionsPerDayMin} to {preview.SessionsPerDayMax}");
            sb.AppendLine($"- Output Workbook: {outputPath}");
            sb.AppendLine($"- Schedule CSV Source: {resolved.ScheduleCsvPath}");
            sb.AppendLine();
            sb.AppendLine("## Semester Plan");
            foreach (var semester in preview.SemesterRanges ?? new List<SemesterRange>())
            {
                sb.AppendLine($"- Semester {semester.Semester}: {semester.StartDate} to {semester.EndDate} ({semester.LearningDays} learning days)");
            }

            if (preview.BreakRanges?.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Breaks");
                foreach (var breakRange in preview.BreakRanges)
                {
                    sb.AppendLine($"- After Semester {breakRange.AfterSemester}: {breakRange.StartDate} to {breakRange.EndDate} ({breakRange.BreakDays} days)");
                }
            }

            return sb.ToString().Trim();
        }

        private static void AppendTextRow(SheetData sheetData, params string[] values)
        {
            var row = new Row();
            foreach (var value in values ?? Array.Empty<string>())
            {
                row.Append(new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(value ?? string.Empty))
                });
            }

            sheetData.Append(row);
        }

        private static int TryParsePositiveInt(string? value)
        {
            return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : 0;
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

        private sealed class RolloutScheduleSourceRow
        {
            public string Date { get; init; } = string.Empty;
            public string Day { get; init; } = string.Empty;
            public string Period { get; init; } = string.Empty;
            public string TimeStart { get; init; } = string.Empty;
            public string TimeEnd { get; init; } = string.Empty;
            public string SubjectCode { get; init; } = string.Empty;
            public string TopicCode { get; init; } = string.Empty;
            public string TopicDescription { get; init; } = string.Empty;
            public string Lpn { get; init; } = string.Empty;
            public string LessonPlanDescription { get; init; } = string.Empty;
            public string AssessmentCriteriaDescription { get; init; } = string.Empty;
            public string LecturerActions { get; init; } = string.Empty;
            public string LearnerActions { get; init; } = string.Empty;
            public string LearningAids { get; init; } = string.Empty;
        }

        private sealed class RolloutPlanRow
        {
            public string Date { get; init; } = string.Empty;
            public string DayName { get; init; } = string.Empty;
            public int Semester { get; init; }
            public int Period { get; init; }
            public string TimeStart { get; init; } = string.Empty;
            public string TimeEnd { get; init; } = string.Empty;
            public string SubjectCode { get; init; } = string.Empty;
            public string TopicCode { get; init; } = string.Empty;
            public string TopicDescription { get; init; } = string.Empty;
            public string Lpn { get; init; } = string.Empty;
            public string LessonPlanDescription { get; init; } = string.Empty;
            public string AssessmentCriteriaDescription { get; init; } = string.Empty;
            public string LecturerActions { get; init; } = string.Empty;
            public string LearnerActions { get; init; } = string.Empty;
            public string LearningAids { get; init; } = string.Empty;
        }
    }
}
