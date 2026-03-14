using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LecturerToolkitController : ControllerBase
    {
        private const int PeriodsPerDay = 13;
        private static readonly TimeSpan DayStart = new(8, 0, 0);
        private static readonly TimeSpan DayEnd = new(16, 10, 0);

        private readonly ApplicationDbContext _context;

        public LecturerToolkitController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            var items = _context.LecturerToolkitEntries
                .OrderBy(e => e.Id)
                .ToList();
            return Ok(items);
        }

        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var item = _context.LecturerToolkitEntries.Find(id);
            if (item == null) return NotFound();
            return Ok(item);
        }

        [HttpPost]
        public IActionResult Create([FromBody] LecturerToolkitEntry model)
        {
            if (string.IsNullOrWhiteSpace(model.SubjectCode) || string.IsNullOrWhiteSpace(model.SubjectDescription))
            {
                return BadRequest("Subject Code and Subject Description are required.");
            }
            if (model.QualificationsId <= 0)
            {
                return BadRequest("QualificationsId must be a positive number.");
            }
            _context.LecturerToolkitEntries.Add(model);
            _context.SaveChanges();
            return CreatedAtAction(nameof(Get), new { id = model.Id }, model);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, [FromBody] LecturerToolkitEntry updated)
        {
            var item = _context.LecturerToolkitEntries.Find(id);
            if (item == null) return NotFound();
            item.QualificationsId = updated.QualificationsId;
            item.LearningInstitutionName = updated.LearningInstitutionName;
            item.LecturerName = updated.LecturerName;
            item.SubjectCode = updated.SubjectCode;
            item.SubjectDescription = updated.SubjectDescription;
            item.AssessmentCriteriaId = updated.AssessmentCriteriaId;
            item.AssessmentCriteriaDescription = updated.AssessmentCriteriaDescription;
            item.Lpn = updated.Lpn;
            item.LessonPlanDescription = updated.LessonPlanDescription;
            item.LessonPlanContent = updated.LessonPlanContent;
            item.TimeStart = updated.TimeStart;
            item.TimeEnd = updated.TimeEnd;
            item.LecturerActions = updated.LecturerActions;
            item.LearnerActions = updated.LearnerActions;
            item.LearningAids = updated.LearningAids;
            _context.SaveChanges();
            return Ok(item);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.LecturerToolkitEntries.Find(id);
            if (item == null) return NotFound();
            _context.LecturerToolkitEntries.Remove(item);
            _context.SaveChanges();
            return NoContent();
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadExcel(
            [FromForm] IFormFile file,
            [FromQuery] int? qualificationId,
            [FromQuery] bool replaceExisting = false)
        {
            if (file == null || file.Length == 0) return BadRequest("No file uploaded.");
            if (replaceExisting && (!qualificationId.HasValue || qualificationId.Value <= 0))
            {
                return BadRequest("qualificationId is required when replaceExisting=true.");
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".csv" && ext != ".xlsx" && ext != ".xls")
            {
                return BadRequest("Unsupported file type. Upload .csv or .xlsx.");
            }
            if (ext == ".xls")
            {
                return BadRequest("Legacy .xls is not supported. Save as .xlsx or .csv and retry.");
            }

            var uploads = Path.Combine(Path.GetTempPath(), "LecturerToolkitUploads");
            Directory.CreateDirectory(uploads);
            var safeFile = $"{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploads, safeFile);

            try
            {
                await using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await file.CopyToAsync(stream);
                }

                var rows = ext == ".csv" ? ReadCsvRows(filePath) : ReadXlsxRows(filePath);
                var result = ImportRows(rows, qualificationId, replaceExisting);
                if (result.Aborted)
                {
                    return BadRequest(new
                    {
                        error = result.ErrorMessage,
                        replaced = result.Replaced,
                        created = result.Created,
                        failed = result.Failed,
                        details = result.Details
                    });
                }

                var canonicalSource = PersistUploadedLessonPlanSource(filePath, ext);

                return Ok(new
                {
                    message = replaceExisting
                        ? "File uploaded and imported successfully. Existing rows were replaced for selected qualification."
                        : "File uploaded and imported successfully.",
                    fileName = file.FileName,
                    canonicalSource = Path.GetFileName(canonicalSource),
                    replaced = result.Replaced,
                    created = result.Created,
                    failed = result.Failed,
                    details = result.Details
                });
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }
                catch
                {
                    // Best-effort temp cleanup.
                }
            }
        }

        [HttpPost("import-csv")]
        public IActionResult ImportCsv([FromQuery] int? qualificationId, [FromQuery] bool replaceExisting = false)
        {
            var path = ResolveTemplate("Lesson PLan.csv", "Lesson Plan.csv");
            if (path == null) return NotFound("Template not found: Lesson PLan.csv");
            if (replaceExisting && (!qualificationId.HasValue || qualificationId.Value <= 0))
            {
                return BadRequest("qualificationId is required when replaceExisting=true.");
            }

            var rows = ReadCsvRows(path);
            var result = ImportRows(rows, qualificationId, replaceExisting);
            if (result.Aborted)
            {
                return BadRequest(new
                {
                    error = result.ErrorMessage,
                    replaced = result.Replaced,
                    created = result.Created,
                    failed = result.Failed,
                    details = result.Details,
                    source = Path.GetFileName(path)
                });
            }
            return Ok(new
            {
                replaced = result.Replaced,
                created = result.Created,
                failed = result.Failed,
                details = result.Details,
                source = Path.GetFileName(path)
            });
        }

        [HttpPost("automate-learning-schedule")]
        public IActionResult AutomateLearningSchedule([FromQuery] int qualificationId, [FromQuery] bool replaceExisting = true)
        {
            if (qualificationId <= 0)
            {
                return BadRequest("qualificationId is required.");
            }

            var qualification = _context.Qualifications.Find(qualificationId);
            if (qualification == null)
            {
                return NotFound("Qualification not found.");
            }

            var path = ResolveTemplate("Lesson Plan.xlsx", "LessonPlan.xlsx", "Lesson PLan.csv", "Lesson Plan.csv");
            if (path == null)
            {
                return NotFound("Template not found: Lesson Plan source file.");
            }

            var rows = ReadRowsByExtension(path);
            ToolkitImportResult result;
            try
            {
                result = ImportRows(rows, qualificationId, replaceExisting);
                if (result.Aborted)
                {
                    return BadRequest(new
                    {
                        error = result.ErrorMessage,
                        replaced = result.Replaced,
                        created = result.Created,
                        failed = result.Failed,
                        details = result.Details
                    });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }

            var daysScheduled = result.Created <= 0
                ? 0
                : (int)Math.Ceiling(result.Created / (double)PeriodsPerDay);

            return Ok(new
            {
                qualificationId,
                source = Path.GetFileName(path),
                replaced = result.Replaced,
                created = result.Created,
                failed = result.Failed,
                daysScheduled,
                dailyStart = DayStart.ToString(@"hh\:mm"),
                dailyEnd = DayEnd.ToString(@"hh\:mm"),
                periodsPerDay = PeriodsPerDay
            });
        }

        private ToolkitImportResult ImportRows(List<string[]> rows, int? qualificationId, bool replaceExisting = false)
        {
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("Uploaded file is empty.");
            }

            var header = rows[0];
            int ColMulti(params string[] names) => FindColumn(header, names);

            var cQualificationsId = ColMulti("QualificationsId", "QualificationsID");
            var cQualificationCode = ColMulti("Qualification Code", "Qualification Number", "QualificationNo", "Qualification No", "Qaulification Code");
            var cInstitution = ColMulti("Name of the Learning Institution", "Learning Institution", "Learning Institution Name");
            var cLecturer = ColMulti("Name of the Lecturer", "Lecturer Name");
            var cSubjectCode = ColMulti("Subject Code", "SubjectCode");
            var cSubjectDesc = ColMulti("Subject Description", "Subject Decription");
            var cCriteriaId = ColMulti("Assessment Criteria Number", "Assessment Criteria Id", "Assessment Criteria Number (AC)", "AssessmentCriterionCode", "Assessment Criteria Code");
            var cCriteriaDesc = ColMulti("Assesment Criteria Description", "Assessment Criteria Description", "AssessmentCriterion", "Assessment Criteria", "Assessment Criterion");
            var cLpn = ColMulti("LPN", "Lesson Plan Number (LPN)");
            var cLessonDesc = ColMulti("Lesson Plan Description", "Lesson Plan Description ", "Description", "Lesson Plan", "LessonPlanTitle", "Lesson Plan Title", "Title");
            var cLessonContent = ColMulti("Lesson Plan Content");
            var cTimeStart = ColMulti("Time Start", "Start Time");
            var cTimeEnd = ColMulti("Time End", "End Time");
            var cLecturerActions = ColMulti("Lecturer Actions");
            var cLearnerActions = ColMulti("Learner Actions");
            var cLearningAids = ColMulti("Learning Aids");

            bool HasMeaningfulToolkitData(string[] row)
            {
                return !string.IsNullOrWhiteSpace(Cell(row, cCriteriaId))
                    || !string.IsNullOrWhiteSpace(Cell(row, cCriteriaDesc))
                    || !string.IsNullOrWhiteSpace(Cell(row, cLpn))
                    || !string.IsNullOrWhiteSpace(Cell(row, cLessonDesc))
                    || !string.IsNullOrWhiteSpace(Cell(row, cLessonContent))
                    || !string.IsNullOrWhiteSpace(Cell(row, cTimeStart))
                    || !string.IsNullOrWhiteSpace(Cell(row, cTimeEnd))
                    || !string.IsNullOrWhiteSpace(Cell(row, cLecturerActions))
                    || !string.IsNullOrWhiteSpace(Cell(row, cLearnerActions))
                    || !string.IsNullOrWhiteSpace(Cell(row, cLearningAids));
            }

            var created = 0;
            var failed = 0;
            var replaced = 0;
            var details = new List<object>();
            var replaceQualificationId = qualificationId.HasValue && qualificationId.Value > 0
                ? qualificationId.Value
                : (int?)null;
            var preparedEntries = new List<LecturerToolkitEntry>();
            var existingRows = replaceExisting && replaceQualificationId.HasValue
                ? _context.LecturerToolkitEntries
                    .Where(e => e.QualificationsId == replaceQualificationId.Value)
                    .AsNoTracking()
                    .ToList()
                : new List<LecturerToolkitEntry>();

            if (replaceExisting && (!replaceQualificationId.HasValue || replaceQualificationId.Value <= 0))
            {
                throw new InvalidOperationException("qualificationId is required when replaceExisting=true.");
            }

            var qualificationReferences = _context.Qualifications
                .Select(q => new QualificationImportReference
                {
                    Id = q.Id,
                    QualificationNumber = q.QualificationNumber ?? string.Empty
                })
                .ToList();
            var qualificationIds = qualificationReferences.Select(q => q.Id).ToHashSet();
            var qualificationsByNumber = qualificationReferences
                .Where(q => !string.IsNullOrWhiteSpace(q.QualificationNumber))
                .GroupBy(q => NormalizeLooseText(q.QualificationNumber))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First().Id);

            var subjectReferences = _context.Subjects
                .Select(s => new SubjectImportReference
                {
                    Id = s.Id,
                    QualificationId = s.QualificationId,
                    SubjectCode = s.SubjectCode ?? string.Empty,
                    SubjectDescription = s.SubjectDescription ?? string.Empty
                })
                .ToList();
            var subjectsByQualificationAndCode = subjectReferences
                .Where(s => !string.IsNullOrWhiteSpace(s.SubjectCode))
                .GroupBy(s => (s.QualificationId, SubjectCode: NormalizeCodeKey(s.SubjectCode)))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).ToList());
            var subjectsByQualificationAndDescription = subjectReferences
                .Where(s => !string.IsNullOrWhiteSpace(s.SubjectDescription))
                .GroupBy(s => (s.QualificationId, SubjectDescription: NormalizeLooseText(s.SubjectDescription)))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).ToList());

            var criteriaReferences = (from c in _context.AssessmentCriteria
                                      join t in _context.Topics on c.TopicId equals t.Id
                                      join s in _context.Subjects on t.SubjectId equals s.Id
                                      select new CriteriaImportReference
                                      {
                                          Id = c.Id,
                                          QualificationId = s.QualificationId,
                                          SubjectId = s.Id,
                                          SubjectCode = s.SubjectCode ?? string.Empty,
                                          Description = c.Description ?? string.Empty
                                      })
                .ToList();
            var criteriaById = criteriaReferences.ToDictionary(c => c.Id, c => c);
            var criteriaByQualificationSubjectAndDescription = criteriaReferences
                .Where(c => !string.IsNullOrWhiteSpace(c.SubjectCode) && !string.IsNullOrWhiteSpace(c.Description))
                .GroupBy(c => (
                    c.QualificationId,
                    SubjectCode: NormalizeCodeKey(c.SubjectCode),
                    Description: NormalizeLooseText(c.Description)))
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).ToList());

            int? lastQualificationId = null;
            string lastSubjectCode = string.Empty;
            string lastSubjectDescription = string.Empty;
            string lastInstitution = string.Empty;
            string lastLecturer = string.Empty;

            for (var i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Length == 0 || r.All(string.IsNullOrWhiteSpace)) continue;

                var subjCode = Cell(r, cSubjectCode);
                var subjDesc = Cell(r, cSubjectDesc);
                if (string.IsNullOrWhiteSpace(subjCode) || string.IsNullOrWhiteSpace(subjDesc))
                {
                    if (HasMeaningfulToolkitData(r) && !string.IsNullOrWhiteSpace(lastSubjectCode) && !string.IsNullOrWhiteSpace(lastSubjectDescription))
                    {
                        subjCode = lastSubjectCode;
                        subjDesc = lastSubjectDescription;
                    }
                    else
                    {
                        failed++;
                        details.Add(new { row = i, reason = "Subject Code/Description missing", subjectCode = subjCode, subjectDescription = subjDesc });
                        continue;
                    }
                }

                var idStr = Cell(r, cQualificationsId).Trim();
                var code = Cell(r, cQualificationCode).Trim();
                var criteriaDesc = NullIfWhiteSpace(Cell(r, cCriteriaDesc));
                var parsedCriteriaId = ParseCriteriaId(Cell(r, cCriteriaId));
                var institution = Cell(r, cInstitution);
                var lecturer = Cell(r, cLecturer);

                var entry = new LecturerToolkitEntry
                {
                    QualificationsId = int.TryParse(idStr, out var qid) ? qid : 0,
                    LearningInstitutionName = !string.IsNullOrWhiteSpace(institution) ? institution : lastInstitution,
                    LecturerName = !string.IsNullOrWhiteSpace(lecturer) ? lecturer : lastLecturer,
                    SubjectCode = subjCode,
                    SubjectDescription = subjDesc,
                    AssessmentCriteriaId = parsedCriteriaId,
                    AssessmentCriteriaDescription = criteriaDesc,
                    Lpn = Cell(r, cLpn),
                    LessonPlanDescription = Cell(r, cLessonDesc),
                    LessonPlanContent = Cell(r, cLessonContent),
                    TimeStart = Cell(r, cTimeStart),
                    TimeEnd = Cell(r, cTimeEnd),
                    LecturerActions = Cell(r, cLecturerActions),
                    LearnerActions = Cell(r, cLearnerActions),
                    LearningAids = Cell(r, cLearningAids),
                };

                if (entry.QualificationsId > 0)
                {
                    if (!qualificationIds.Contains(entry.QualificationsId))
                    {
                        var normalizedIdStr = NormalizeLooseText(idStr);
                        if (!string.IsNullOrWhiteSpace(normalizedIdStr)
                            && qualificationsByNumber.TryGetValue(normalizedIdStr, out var mappedQualificationId))
                        {
                            entry.QualificationsId = mappedQualificationId;
                        }
                    }
                }

                if (entry.QualificationsId <= 0 && !string.IsNullOrWhiteSpace(code))
                {
                    var normalizedQualificationCode = NormalizeLooseText(code);
                    if (!string.IsNullOrWhiteSpace(normalizedQualificationCode)
                        && qualificationsByNumber.TryGetValue(normalizedQualificationCode, out var mappedQualificationId))
                    {
                        entry.QualificationsId = mappedQualificationId;
                    }
                }

                if (replaceQualificationId.HasValue)
                {
                    if (entry.QualificationsId > 0 && entry.QualificationsId != replaceQualificationId.Value)
                    {
                        failed++;
                        details.Add(new
                        {
                            row = i,
                            reason = $"Qualification mismatch (entry={entry.QualificationsId}, selected={replaceQualificationId.Value})",
                            subjectCode = subjCode,
                            subjectDescription = subjDesc,
                            qualificationCode = code
                        });
                        continue;
                    }
                    entry.QualificationsId = replaceQualificationId.Value;
                }
                else if (entry.QualificationsId <= 0 && qualificationId.HasValue && qualificationId.Value > 0)
                {
                    entry.QualificationsId = qualificationId.Value;
                }
                if (entry.QualificationsId <= 0 && lastQualificationId.HasValue && lastQualificationId.Value > 0)
                {
                    entry.QualificationsId = lastQualificationId.Value;
                }
                if (entry.QualificationsId <= 0)
                {
                    failed++;
                    details.Add(new { row = i, reason = "QualificationsId invalid or missing", subjectCode = subjCode, subjectDescription = subjDesc, qualificationCode = code });
                    continue;
                }

                var subjectCodeKey = NormalizeCodeKey(entry.SubjectCode);
                var subjectDescriptionKey = NormalizeLooseText(entry.SubjectDescription);
                SubjectImportReference? matchedSubject = null;
                if (!string.IsNullOrWhiteSpace(subjectCodeKey)
                    && subjectsByQualificationAndCode.TryGetValue((entry.QualificationsId, subjectCodeKey), out var subjectCodeMatches)
                    && subjectCodeMatches.Count > 0)
                {
                    matchedSubject = subjectCodeMatches[0];
                }
                else if (!string.IsNullOrWhiteSpace(subjectDescriptionKey)
                    && subjectsByQualificationAndDescription.TryGetValue((entry.QualificationsId, subjectDescriptionKey), out var subjectDescriptionMatches)
                    && subjectDescriptionMatches.Count > 0)
                {
                    matchedSubject = subjectDescriptionMatches[0];
                }

                if (matchedSubject == null)
                {
                    failed++;
                    details.Add(new
                    {
                        row = i,
                        reason = $"Subject is not mapped to qualification {entry.QualificationsId}",
                        subjectCode = entry.SubjectCode,
                        subjectDescription = entry.SubjectDescription
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.SubjectDescription))
                {
                    entry.SubjectDescription = matchedSubject.SubjectDescription;
                }

                CriteriaImportReference? matchedCriteria = null;
                if (entry.AssessmentCriteriaId.HasValue && entry.AssessmentCriteriaId.Value > 0)
                {
                    if (criteriaById.TryGetValue(entry.AssessmentCriteriaId.Value, out var criteriaByDirectId)
                        && criteriaByDirectId.QualificationId == entry.QualificationsId
                        && criteriaByDirectId.SubjectId == matchedSubject.Id)
                    {
                        matchedCriteria = criteriaByDirectId;
                    }
                }

                if (matchedCriteria == null && !string.IsNullOrWhiteSpace(entry.AssessmentCriteriaDescription))
                {
                    var criteriaDescriptionKey = NormalizeLooseText(entry.AssessmentCriteriaDescription);
                    if (!string.IsNullOrWhiteSpace(criteriaDescriptionKey)
                        && criteriaByQualificationSubjectAndDescription.TryGetValue(
                            (entry.QualificationsId, NormalizeCodeKey(matchedSubject.SubjectCode), criteriaDescriptionKey),
                            out var criteriaDescriptionMatches)
                        && criteriaDescriptionMatches.Count == 1)
                    {
                        matchedCriteria = criteriaDescriptionMatches[0];
                    }
                }

                if (entry.AssessmentCriteriaId.HasValue && entry.AssessmentCriteriaId.Value > 0 && matchedCriteria == null)
                {
                    failed++;
                    details.Add(new
                    {
                        row = i,
                        reason = $"Assessment criteria is not mapped to qualification {entry.QualificationsId} subject {matchedSubject.SubjectCode}",
                        subjectCode = entry.SubjectCode,
                        assessmentCriteriaId = entry.AssessmentCriteriaId,
                        assessmentCriteriaDescription = entry.AssessmentCriteriaDescription
                    });
                    continue;
                }

                if (matchedCriteria == null && !string.IsNullOrWhiteSpace(entry.AssessmentCriteriaDescription))
                {
                    failed++;
                    details.Add(new
                    {
                        row = i,
                        reason = $"Assessment criteria description is not mapped to qualification {entry.QualificationsId} subject {matchedSubject.SubjectCode}",
                        subjectCode = entry.SubjectCode,
                        assessmentCriteriaDescription = entry.AssessmentCriteriaDescription
                    });
                    continue;
                }

                if (matchedCriteria != null)
                {
                    entry.AssessmentCriteriaId = matchedCriteria.Id;
                    entry.AssessmentCriteriaDescription = matchedCriteria.Description;
                }

                if (existingRows.Count > 0)
                {
                    var seed = FindExistingSeed(existingRows, entry);
                    if (seed != null)
                    {
                        PreserveExistingScheduleFields(entry, seed);
                    }
                }

                TrimEntry(entry);
                preparedEntries.Add(entry);
                created++;

                lastQualificationId = entry.QualificationsId;
                lastSubjectCode = entry.SubjectCode;
                lastSubjectDescription = entry.SubjectDescription;
                lastInstitution = entry.LearningInstitutionName;
                lastLecturer = entry.LecturerName;
            }

            if (replaceExisting && failed > 0)
            {
                return ToolkitImportResult.CreateAborted(
                    created: 0,
                    failed: failed,
                    replaced: 0,
                    details: details,
                    errorMessage: "Replace Existing was cancelled because one or more uploaded rows do not match the selected qualification, subject, or assessment criteria. Existing toolkit rows were left unchanged.");
            }

            if (replaceExisting && preparedEntries.Count == 0)
            {
                return ToolkitImportResult.CreateAborted(
                    created: 0,
                    failed: failed,
                    replaced: 0,
                    details: details,
                    errorMessage: "Replace Existing was cancelled because the upload did not contain any valid toolkit rows. Existing toolkit rows were left unchanged.");
            }

            using var transaction = _context.Database.BeginTransaction();
            if (replaceExisting && replaceQualificationId.HasValue)
            {
                var previousRows = _context.LecturerToolkitEntries
                    .Where(e => e.QualificationsId == replaceQualificationId.Value)
                    .ToList();
                replaced = previousRows.Count;
                if (replaced > 0)
                {
                    _context.LecturerToolkitEntries.RemoveRange(previousRows);
                }
            }

            if (preparedEntries.Count > 0)
            {
                _context.LecturerToolkitEntries.AddRange(preparedEntries);
            }

            _context.SaveChanges();
            transaction.Commit();
            return new ToolkitImportResult(created, failed, replaced, details);
        }

        private static List<string[]> ReadRowsByExtension(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".xlsx" => ReadXlsxRows(path),
                ".csv" => ReadCsvRows(path),
                _ => throw new InvalidOperationException($"Unsupported lesson plan source type: {ext}")
            };
        }

        private static List<string[]> ReadCsvRows(string path)
        {
            var firstLine = System.IO.File.ReadLines(path).FirstOrDefault() ?? string.Empty;
            var delimiter = DetectDelimiter(firstLine);
            return Csv.ReadDelimitedCsv(path, delimiter);
        }

        private static char DetectDelimiter(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return ';';
            var semicolons = line.Count(ch => ch == ';');
            var commas = line.Count(ch => ch == ',');
            var tabs = line.Count(ch => ch == '\t');
            if (tabs > semicolons && tabs > commas) return '\t';
            return semicolons >= commas ? ';' : ',';
        }

        private static List<string[]> ReadXlsxRows(string path)
        {
            using var document = SpreadsheetDocument.Open(path, false);
            var workbookPart = document.WorkbookPart;
            if (workbookPart?.Workbook?.Sheets == null) return new List<string[]>();

            var firstSheet = workbookPart.Workbook.Sheets.Elements<Sheet>().FirstOrDefault();
            if (firstSheet?.Id == null) return new List<string[]>();

            WorksheetPart? worksheetPart = workbookPart.GetPartById(firstSheet.Id!) as WorksheetPart;
            var sheetData = worksheetPart?.Worksheet?.Elements<SheetData>().FirstOrDefault();
            if (sheetData == null) return new List<string[]>();

            var captured = new List<(int RowIndex, Dictionary<int, string> Cells)>();
            var maxColumn = -1;

            foreach (var row in sheetData.Elements<Row>())
            {
                var map = new Dictionary<int, string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var columnIndex = ColumnIndexFromReference(cell.CellReference?.Value);
                    if (columnIndex < 0) continue;
                    map[columnIndex] = GetCellText(cell, workbookPart);
                    if (columnIndex > maxColumn) maxColumn = columnIndex;
                }
                captured.Add(((int)(row.RowIndex?.Value ?? 0), map));
            }

            if (maxColumn < 0) return new List<string[]>();

            var rows = new List<string[]>();
            foreach (var row in captured.OrderBy(x => x.RowIndex))
            {
                var values = Enumerable.Repeat(string.Empty, maxColumn + 1).ToArray();
                foreach (var kv in row.Cells)
                {
                    if (kv.Key >= 0 && kv.Key < values.Length)
                    {
                        values[kv.Key] = kv.Value ?? string.Empty;
                    }
                }
                rows.Add(values);
            }

            return rows;
        }

        private static string GetCellText(Cell cell, WorkbookPart workbookPart)
        {
            var raw = cell.CellValue?.InnerText ?? cell.InnerText ?? string.Empty;
            if (cell.DataType == null) return raw;

            return cell.DataType.Value switch
            {
                CellValues.SharedString => ReadSharedString(raw, workbookPart),
                CellValues.Boolean => raw == "1" ? "TRUE" : "FALSE",
                CellValues.InlineString => cell.InlineString?.Text?.Text ?? cell.InlineString?.InnerText ?? raw,
                _ => raw
            };
        }

        private static string ReadSharedString(string raw, WorkbookPart workbookPart)
        {
            if (!int.TryParse(raw, out var idx)) return raw;
            var table = workbookPart.SharedStringTablePart?.SharedStringTable;
            var item = table?.Elements<SharedStringItem>().ElementAtOrDefault(idx);
            return item?.InnerText ?? raw;
        }

        private static int ColumnIndexFromReference(string? cellReference)
        {
            if (string.IsNullOrWhiteSpace(cellReference)) return -1;

            var index = 0;
            foreach (var ch in cellReference)
            {
                if (!char.IsLetter(ch)) break;
                index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }

            return index > 0 ? index - 1 : -1;
        }

        private static (string Start, string End) BuildSlot(int periodInDay)
        {
            var totalMinutes = (DayEnd - DayStart).TotalMinutes;
            var startMinutes = Math.Round(totalMinutes * (periodInDay - 1) / PeriodsPerDay, MidpointRounding.AwayFromZero);
            var endMinutes = Math.Round(totalMinutes * periodInDay / PeriodsPerDay, MidpointRounding.AwayFromZero);

            var start = DayStart.Add(TimeSpan.FromMinutes(startMinutes));
            var end = DayStart.Add(TimeSpan.FromMinutes(endMinutes));
            return (start.ToString(@"hh\:mm"), end.ToString(@"hh\:mm"));
        }

        private static LecturerToolkitEntry? FindSeedRow(List<LecturerToolkitEntry> rows, int? criteriaId, string subjectCode, int periodWithinTopic)
        {
            var byCriteria = criteriaId.HasValue && criteriaId.Value > 0
                ? rows.Where(r => r.AssessmentCriteriaId == criteriaId.Value).ToList()
                : rows.Where(r => string.Equals(r.SubjectCode ?? string.Empty, subjectCode, StringComparison.OrdinalIgnoreCase)).ToList();

            if (byCriteria.Count == 0) return null;

            var exactLpn = byCriteria.FirstOrDefault(r => ParseLpnSort(r.Lpn) == periodWithinTopic);
            if (exactLpn != null) return exactLpn;

            return byCriteria.FirstOrDefault();
        }

        private static string ResolveLessonDescription(
            Dictionary<int, List<LessonPlan>> lessonPlansByCriteria,
            int? criteriaId,
            int periodWithinTopic,
            string? fallbackTopicDescription)
        {
            if (criteriaId.HasValue && criteriaId.Value > 0 && lessonPlansByCriteria.TryGetValue(criteriaId.Value, out var plans) && plans.Count > 0)
            {
                var exact = plans.FirstOrDefault(lp => lp.SortOrder == periodWithinTopic && !string.IsNullOrWhiteSpace(lp.Title));
                if (exact != null) return exact.Title.Trim();

                var first = plans.FirstOrDefault(lp => !string.IsNullOrWhiteSpace(lp.Title));
                if (first != null) return first.Title.Trim();
            }

            return !string.IsNullOrWhiteSpace(fallbackTopicDescription)
                ? $"Lesson activities for {fallbackTopicDescription}"
                : "Lesson activities";
        }

        private static int ResolvePeriods(double? periodsPerTopic)
        {
            if (!periodsPerTopic.HasValue || periodsPerTopic.Value <= 0) return 1;
            return Math.Max(1, (int)Math.Ceiling(periodsPerTopic.Value));
        }

        private static LecturerToolkitEntry? FindExistingSeed(List<LecturerToolkitEntry> rows, LecturerToolkitEntry entry)
        {
            if (rows == null || rows.Count == 0) return null;

            var subjectCode = NormalizeCodeKey(entry.SubjectCode);
            var lpnSort = ParseLpnSort(entry.Lpn);
            var assessmentCriteriaId = entry.AssessmentCriteriaId ?? 0;
            var assessmentCriteriaDescription = NormalizeLooseText(entry.AssessmentCriteriaDescription);

            var exact = rows.FirstOrDefault(r =>
                NormalizeCodeKey(r.SubjectCode) == subjectCode &&
                ParseLpnSort(r.Lpn) == lpnSort &&
                (r.AssessmentCriteriaId ?? 0) == assessmentCriteriaId &&
                assessmentCriteriaId > 0);
            if (exact != null) return exact;

            var byDescription = rows.FirstOrDefault(r =>
                NormalizeCodeKey(r.SubjectCode) == subjectCode &&
                ParseLpnSort(r.Lpn) == lpnSort &&
                NormalizeLooseText(r.AssessmentCriteriaDescription) == assessmentCriteriaDescription &&
                !string.IsNullOrWhiteSpace(assessmentCriteriaDescription));
            if (byDescription != null) return byDescription;

            return rows.FirstOrDefault(r =>
                NormalizeCodeKey(r.SubjectCode) == subjectCode &&
                ParseLpnSort(r.Lpn) == lpnSort);
        }

        private static void PreserveExistingScheduleFields(LecturerToolkitEntry entry, LecturerToolkitEntry existing)
        {
            if (existing == null) return;

            if (string.IsNullOrWhiteSpace(entry.LearningInstitutionName))
            {
                entry.LearningInstitutionName = existing.LearningInstitutionName ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(entry.LecturerName))
            {
                entry.LecturerName = existing.LecturerName ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(entry.LessonPlanContent))
            {
                entry.LessonPlanContent = existing.LessonPlanContent ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(entry.TimeStart))
            {
                entry.TimeStart = existing.TimeStart ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(entry.TimeEnd))
            {
                entry.TimeEnd = existing.TimeEnd ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(entry.LecturerActions))
            {
                entry.LecturerActions = existing.LecturerActions ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(entry.LearnerActions))
            {
                entry.LearnerActions = existing.LearnerActions ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(entry.LearningAids))
            {
                entry.LearningAids = existing.LearningAids ?? string.Empty;
            }
            if (LooksGenericLessonDescription(entry.LessonPlanDescription) && !string.IsNullOrWhiteSpace(existing.LessonPlanDescription))
            {
                entry.LessonPlanDescription = existing.LessonPlanDescription;
            }
        }

        private static bool LooksGenericLessonDescription(string? value)
        {
            var raw = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return true;
            return raw.Equals("Lesson activities", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("Lesson activities for ", StringComparison.OrdinalIgnoreCase);
        }

        private static string PersistUploadedLessonPlanSource(string filePath, string ext)
        {
            var root = EnsureTemplateDirectory();
            var canonicalName = string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase)
                ? "Lesson Plan.xlsx"
                : "Lesson Plan.csv";
            var canonicalPath = Path.Combine(root, canonicalName);
            System.IO.File.Copy(filePath, canonicalPath, overwrite: true);
            return canonicalPath;
        }

        private static string EnsureTemplateDirectory()
        {
            var existing = ResolveExistingTemplateDirectory();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            var fallback = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Imports", "ExcelCSVTemplates"));
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private static string? ResolveExistingTemplateDirectory()
        {
            var candidates = new List<string>();
            AddTemplateCandidate(candidates, Path.Combine(Directory.GetCurrentDirectory(), "Imports", "ExcelCSVTemplates"));
            AddTemplateCandidate(candidates, Path.Combine(AppContext.BaseDirectory, "Imports", "ExcelCSVTemplates"));
            AddTemplateCandidate(candidates, Path.Combine(AppContext.BaseDirectory, "..", "Imports", "ExcelCSVTemplates"));
            AddTemplateCandidate(candidates, Path.Combine(AppContext.BaseDirectory, "..", "..", "Imports", "ExcelCSVTemplates"));
            AddTemplateCandidate(candidates, Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Imports", "ExcelCSVTemplates"));
            AddTemplateCandidate(candidates, @"E:\ETDP\ETDP\Imports\ExcelCSVTemplates");
            AddTemplateCandidate(candidates, @"C:\ETDP\ETDP\Imports\ExcelCSVTemplates");
            return candidates.FirstOrDefault();
        }

        private static void AddTemplateCandidate(List<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath)) return;
            if (candidates.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) return;
            candidates.Add(fullPath);
        }

        private static string? ResolveTemplate(params string[] names)
        {
            var root = ResolveExistingTemplateDirectory();
            if (string.IsNullOrWhiteSpace(root)) return null;

            foreach (var name in names)
            {
                var path = Path.Combine(root, name);
                if (System.IO.File.Exists(path)) return path;
            }
            return null;
        }

        private static int FindColumn(string[] header, params string[] names)
        {
            foreach (var name in names)
            {
                var idx = Array.FindIndex(header, h => string.Equals(h?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) return idx;
            }

            var normalized = names.Select(NormalizeHeader).Where(v => v.Length > 0).ToHashSet();
            for (var i = 0; i < header.Length; i++)
            {
                if (normalized.Contains(NormalizeHeader(header[i]))) return i;
            }

            return -1;
        }

        private static string NormalizeHeader(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        private static string NormalizeCodeKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value
                .Trim()
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray();
            return new string(chars);
        }

        private static string NormalizeLooseText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }

        private static int? ParseCriteriaId(string? raw)
        {
            var s = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (int.TryParse(s, out var direct)) return direct;

            var match = Regex.Match(s, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var token)) return token;
            return null;
        }

        private static int ParseLpnSort(string? value)
        {
            var raw = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            if (int.TryParse(raw, out var direct)) return direct;

            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var parsed)) return parsed;
            return 0;
        }

        private static string Cell(string[] row, int idx)
        {
            if (idx < 0 || idx >= row.Length) return string.Empty;
            return row[idx] ?? string.Empty;
        }

        private static string? NullIfWhiteSpace(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static void TrimEntry(LecturerToolkitEntry entry)
        {
            entry.LearningInstitutionName = entry.LearningInstitutionName?.Trim() ?? string.Empty;
            entry.LecturerName = entry.LecturerName?.Trim() ?? string.Empty;
            entry.SubjectCode = entry.SubjectCode?.Trim() ?? string.Empty;
            entry.SubjectDescription = entry.SubjectDescription?.Trim() ?? string.Empty;
            entry.AssessmentCriteriaDescription = entry.AssessmentCriteriaDescription?.Trim();
            entry.Lpn = entry.Lpn?.Trim() ?? string.Empty;
            entry.LessonPlanDescription = entry.LessonPlanDescription?.Trim() ?? string.Empty;
            entry.LessonPlanContent = entry.LessonPlanContent?.Trim() ?? string.Empty;
            entry.TimeStart = entry.TimeStart?.Trim() ?? string.Empty;
            entry.TimeEnd = entry.TimeEnd?.Trim() ?? string.Empty;
            entry.LecturerActions = entry.LecturerActions?.Trim() ?? string.Empty;
            entry.LearnerActions = entry.LearnerActions?.Trim() ?? string.Empty;
            entry.LearningAids = entry.LearningAids?.Trim() ?? string.Empty;
        }

        private sealed class ToolkitImportResult
        {
            public ToolkitImportResult(int created, int failed, int replaced, List<object> details)
            {
                Created = created;
                Failed = failed;
                Replaced = replaced;
                Details = details;
            }

            public int Created { get; }
            public int Failed { get; }
            public int Replaced { get; }
            public List<object> Details { get; }
            public bool Aborted { get; private set; }
            public string ErrorMessage { get; private set; } = string.Empty;

            public static ToolkitImportResult CreateAborted(int created, int failed, int replaced, List<object> details, string errorMessage)
            {
                return new ToolkitImportResult(created, failed, replaced, details)
                {
                    Aborted = true,
                    ErrorMessage = errorMessage ?? string.Empty
                };
            }
        }

        private sealed class QualificationImportReference
        {
            public int Id { get; set; }
            public string QualificationNumber { get; set; } = string.Empty;
        }

        private sealed class SubjectImportReference
        {
            public int Id { get; set; }
            public int QualificationId { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
        }

        private sealed class CriteriaImportReference
        {
            public int Id { get; set; }
            public int QualificationId { get; set; }
            public int SubjectId { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }
    }
}
