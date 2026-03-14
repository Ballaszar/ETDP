using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Utils;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TopicController : ControllerBase
    {
        private static readonly string[] TemplateRoots =
        {
            @"C:\ETDP\ETDP\Imports\ExcelCSVTemplates"
        };

        private readonly ApplicationDbContext _context;

        public TopicController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("import-csv")]
        public IActionResult ImportCsv([FromQuery] int? qualificationId, [FromQuery] string? csvPath)
        {
            var path = ResolveCsvPath(csvPath, "Topics.csv", "TopicsV2.csv");
            if (path == null)
            {
                if (!string.IsNullOrWhiteSpace(csvPath))
                {
                    return NotFound($"CSV file not found: {csvPath}");
                }
                return NotFound("Template not found: Topics.csv or TopicsV2.csv");
            }

            var rows = ReadCsvRows(path);
            if (rows.Count == 0) return BadRequest("CSV is empty");

            var header = rows[0];

            var cQualificationId = FindColumn(header, "QualificationId");
            var cQualificationCode = FindColumn(header, "Qualification Code", "Qualification Number", "Qaulification Code");
            var cPhasesCode = FindColumn(header, "Phases Code", "PhasesCode");
            var cPhasesDescription = FindColumn(header, "Phases Description", "Phase Description");
            var cSubjectCode = FindColumn(header, "Subject Code", "SubjectCode", "PhasesCode");
            var cSubjectDescription = FindColumn(header, "Subject Description", "Subject Decription");
            var cSubjectCredits = FindColumn(header, "Subject Credits");
            var cNotionalHours = FindColumn(header, "Notional Hours", "National Hours");
            var cPeriodsPerTopic = FindColumn(header, "Periods per Topic", "PeriodsPerTopic", "Periods Per Topic");
            var cTopicPurpose = FindColumn(header, "Topic Purpose", "Phases Purpose");
            var cTopicCode = FindColumn(header, "Topic Code");
            var cTopicDescription = FindColumn(header, "Topic Description");
            var cTopicCredits = FindColumn(header, "Topic Credits");
            var cTopicPercentage = FindColumn(header, "Topic Percentage");
            var cCriteriaNumber = FindColumn(header, "Assessment Criteria Number", "Assessment Criteria Id", "Assessment Criteria Number (AC)");
            var cCriteriaDesc = FindColumn(header, "Assesment Criteria Description", "Assessment Criteria Description");
            var cLpn = FindColumn(header, "LPN", "Lesson Plan Number (LPN)");
            var cLpnPriorityKey = FindColumn(header, "LPNPriorityKey", "LPN Priority", "LPNPriority", "LPNSort", "SortOrder", "SequenceKey", "NumericKey");
            var cLessonDesc = FindColumn(header, "Lesson Plan Description", "Lesson Plan Description ", "Description", "Lesson Plan");
            var cLessonContent = FindColumn(header, "Lesson Plan Content");

            var created = 0;
            var updated = 0;
            var failed = 0;
            var details = new List<object>();
            var touchedSubjectIds = new HashSet<int>();
            var requestedQualificationId = NormalizeQualificationId(qualificationId.GetValueOrDefault());
            var lastResolvedQualificationId = requestedQualificationId;
            var lastQualificationCode = string.Empty;
            var lastPhasesCode = string.Empty;
            var lastPhasesDescription = string.Empty;
            var lastSubjectCode = string.Empty;
            var lastSubjectDescription = string.Empty;

            for (var i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Length == 0 || r.All(string.IsNullOrWhiteSpace)) continue;

                var resolvedQualificationId = requestedQualificationId;
                if (resolvedQualificationId <= 0 && lastResolvedQualificationId > 0)
                {
                    resolvedQualificationId = lastResolvedQualificationId;
                }
                if (resolvedQualificationId <= 0)
                {
                    resolvedQualificationId = NormalizeQualificationId(ParseInt(Cell(r, cQualificationId)) ?? 0);
                }
                var qualificationCode = Cell(r, cQualificationCode).Trim();
                if (string.IsNullOrWhiteSpace(qualificationCode))
                {
                    qualificationCode = lastQualificationCode;
                }
                if (resolvedQualificationId == 0 && !string.IsNullOrWhiteSpace(qualificationCode))
                {
                    var q = _context.Qualifications.FirstOrDefault(x => x.QualificationNumber == qualificationCode);
                    if (q != null) resolvedQualificationId = q.Id;
                }

                var phasesCode = Cell(r, cPhasesCode).Trim();
                if (string.IsNullOrWhiteSpace(phasesCode)) phasesCode = lastPhasesCode;
                var phasesDescription = Cell(r, cPhasesDescription).Trim();
                if (string.IsNullOrWhiteSpace(phasesDescription)) phasesDescription = lastPhasesDescription;
                var subjectCode = Cell(r, cSubjectCode).Trim();
                if (string.IsNullOrWhiteSpace(subjectCode)) subjectCode = lastSubjectCode;
                var subjectDescription = Cell(r, cSubjectDescription).Trim();
                if (string.IsNullOrWhiteSpace(subjectDescription)) subjectDescription = lastSubjectDescription;
                var lookupCode = !string.IsNullOrWhiteSpace(subjectCode) ? subjectCode : phasesCode;

                if (string.IsNullOrWhiteSpace(lookupCode) && string.IsNullOrWhiteSpace(subjectDescription))
                {
                    failed++;
                    details.Add(new { row = i, reason = "Subject Code/Description missing", qualificationCode, phasesCode });
                    continue;
                }

                Subject? subject = null;
                if (resolvedQualificationId > 0)
                {
                    if (!string.IsNullOrWhiteSpace(lookupCode))
                    {
                        subject = _context.Subjects.FirstOrDefault(s => s.QualificationId == resolvedQualificationId && s.SubjectCode == lookupCode);
                    }
                    if (subject == null && !string.IsNullOrWhiteSpace(subjectDescription))
                    {
                        subject = _context.Subjects.FirstOrDefault(s => s.QualificationId == resolvedQualificationId && s.SubjectDescription == subjectDescription);
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(lookupCode))
                    {
                        subject = _context.Subjects
                            .Where(s => s.SubjectCode == lookupCode)
                            .OrderByDescending(s => s.Id)
                            .FirstOrDefault();
                    }
                    if (subject == null && !string.IsNullOrWhiteSpace(subjectDescription))
                    {
                        subject = _context.Subjects
                            .Where(s => s.SubjectDescription == subjectDescription)
                            .OrderByDescending(s => s.Id)
                            .FirstOrDefault();
                    }
                    if (subject != null)
                    {
                        resolvedQualificationId = subject.QualificationId;
                    }
                }

                if (subject == null)
                {
                    if (resolvedQualificationId <= 0)
                    {
                        failed++;
                        details.Add(new
                        {
                            row = i,
                            reason = "Qualification not resolved and subject could not be found",
                            qualificationCode,
                            subjectCode = lookupCode
                        });
                        continue;
                    }

                    var phase = ResolveOrCreatePhase(phasesCode, phasesDescription);
                    var subjectCreditsNumber = ParseFlexibleNumber(Cell(r, cSubjectCredits));
                    var subjectCodeForCreate = !string.IsNullOrWhiteSpace(lookupCode) ? lookupCode : subjectDescription;
                    subject = new Subject
                    {
                        QualificationId = resolvedQualificationId,
                        CurriculumPhaseId = phase.Id,
                        SubjectCode = subjectCodeForCreate,
                        SubjectDescription = string.IsNullOrWhiteSpace(subjectDescription) ? lookupCode : subjectDescription,
                        SubjectPurpose = string.IsNullOrWhiteSpace(phasesDescription) ? string.Empty : phasesDescription,
                        SubjectCredits = subjectCreditsNumber.HasValue ? (int?)Math.Max(0, (int)Math.Ceiling(subjectCreditsNumber.Value)) : null
                    };
                    _context.Subjects.Add(subject);
                    _context.SaveChanges();
                }

                var topicCode = Cell(r, cTopicCode).Trim();
                var topicDescription = Cell(r, cTopicDescription).Trim();

                Topic? topic = null;
                if (!string.IsNullOrWhiteSpace(topicCode))
                {
                    topic = _context.Topics.FirstOrDefault(t => t.SubjectId == subject.Id && t.TopicCode == topicCode);
                }
                if (topic == null && !string.IsNullOrWhiteSpace(topicDescription))
                {
                    topic = _context.Topics.FirstOrDefault(t => t.SubjectId == subject.Id && t.TopicDescription == topicDescription);
                }

                var isCreate = topic == null;
                if (topic == null)
                {
                    topic = new Topic
                    {
                        SubjectId = subject.Id
                    };
                    _context.Topics.Add(topic);
                }

                topic.TopicPurpose = Cell(r, cTopicPurpose).Trim();
                topic.TopicCode = topicCode;
                topic.TopicDescription = topicDescription;
                topic.SubjectCredits = ParseFlexibleNumber(Cell(r, cSubjectCredits));
                topic.NotionalHours = ParseFlexibleNumber(Cell(r, cNotionalHours));
                var importedPeriods = ParseFlexibleNumber(Cell(r, cPeriodsPerTopic));
                topic.PeriodsPerTopic = importedPeriods;
                topic.PeriodsPerTopicManualOverride = importedPeriods.HasValue && importedPeriods.Value > 0;
                topic.TopicCredits = ParseInt(Cell(r, cTopicCredits));
                topic.TopicPercentage = ParseInt(Cell(r, cTopicPercentage));
                topic.Order = topic.Order;

                _context.SaveChanges();

                var criteriaNumber = Cell(r, cCriteriaNumber).Trim();
                var criteriaDescription = Cell(r, cCriteriaDesc).Trim();
                var resolvedCriteriaDescription = !string.IsNullOrWhiteSpace(criteriaDescription)
                    ? criteriaDescription
                    : criteriaNumber;

                AssessmentCriteria? criteria = null;
                if (!string.IsNullOrWhiteSpace(resolvedCriteriaDescription))
                {
                    criteria = _context.AssessmentCriteria
                        .FirstOrDefault(c => c.TopicId == topic.Id && c.Description == resolvedCriteriaDescription);
                    if (criteria == null)
                    {
                        criteria = new AssessmentCriteria
                        {
                            TopicId = topic.Id,
                            Description = resolvedCriteriaDescription,
                            CriteriaType = "Topic",
                            Weight = 1.0
                        };
                        _context.AssessmentCriteria.Add(criteria);
                        _context.SaveChanges();
                    }
                }

                if (criteria != null)
                {
                    var lessonDescription = Cell(r, cLessonDesc).Trim();
                    var lessonContent = Cell(r, cLessonContent).Trim();
                    var lpnSort = ParseLpnSort(Cell(r, cLpn));
                    var lpnPriorityKey = ParseInt(Cell(r, cLpnPriorityKey));
                    if (lpnPriorityKey.HasValue && lpnPriorityKey.Value > 0)
                    {
                        lpnSort = lpnPriorityKey.Value;
                    }
                    if (!string.IsNullOrWhiteSpace(lessonDescription) || !string.IsNullOrWhiteSpace(lessonContent))
                    {
                        var lesson = _context.LessonPlans
                            .FirstOrDefault(lp => lp.AssessmentCriteriaId == criteria.Id && lp.SortOrder == lpnSort);
                        if (lesson == null)
                        {
                            lesson = new LessonPlan
                            {
                                AssessmentCriteriaId = criteria.Id,
                                SortOrder = lpnSort
                            };
                            _context.LessonPlans.Add(lesson);
                        }

                        lesson.Title = string.IsNullOrWhiteSpace(lessonDescription)
                            ? criteria.Description
                            : lessonDescription;
                        lesson.Content = lessonContent;
                        lesson.Date = lesson.Date ?? DateTime.UtcNow.Date;
                        lesson.DurationMinutes = EstimateDurationMinutes(topic.PeriodsPerTopic);
                    }
                }

                _context.SaveChanges();

                if (isCreate)
                {
                    created++;
                }
                else
                {
                    updated++;
                }

                touchedSubjectIds.Add(subject.Id);

                details.Add(new
                {
                    row = i,
                    status = isCreate ? "created" : "updated",
                    topicId = topic.Id,
                    topicCode = topic.TopicCode,
                    subjectId = subject.Id
                });

                if (resolvedQualificationId > 0) lastResolvedQualificationId = resolvedQualificationId;
                if (!string.IsNullOrWhiteSpace(qualificationCode)) lastQualificationCode = qualificationCode;
                if (!string.IsNullOrWhiteSpace(phasesCode)) lastPhasesCode = phasesCode;
                if (!string.IsNullOrWhiteSpace(phasesDescription)) lastPhasesDescription = phasesDescription;
                if (!string.IsNullOrWhiteSpace(subjectCode)) lastSubjectCode = subjectCode;
                if (!string.IsNullOrWhiteSpace(subjectDescription)) lastSubjectDescription = subjectDescription;
            }

            foreach (var subjectId in touchedSubjectIds)
            {
                ApplyAutoPeriodsForSubject(subjectId);
            }

            return Ok(new { created, updated, failed, details });
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            try
            {
                var items = BuildTopicQuery().ToList();
                return Ok(items);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var dto = BuildTopicQuery().FirstOrDefault(t => t.Id == id);
            if (dto == null) return NotFound();
            return Ok(dto);
        }

        [HttpGet("bySubject")]
        public IActionResult GetBySubject([FromQuery] int subjectId)
        {
            var items = BuildTopicQuery().Where(t => t.SubjectId == subjectId).ToList();
            return Ok(items);
        }

        [HttpGet("byQualification")]
        public IActionResult GetByQualification([FromQuery] int qualificationId)
        {
            var resolvedQualificationId = NormalizeQualificationId(qualificationId);
            if (resolvedQualificationId <= 0) return Ok(Array.Empty<ETD.Api.DTOs.TopicDto>());

            var items = BuildTopicQuery().Where(t => t.QualificationId == resolvedQualificationId).ToList();
            return Ok(items);
        }

        [HttpGet("byOutcome")]
        public IActionResult GetByOutcome([FromQuery] int outcomeId)
        {
            var items = BuildTopicQuery().Where(t => t.OutcomeId == outcomeId).ToList();
            return Ok(items);
        }

        [HttpPost]
        public IActionResult Create(ETD.Api.DTOs.CreateTopicDto dto)
        {
            var subjectId = dto.SubjectId;
            if (dto.OutcomeId.HasValue)
            {
                var outcome = _context.Outcomes.Find(dto.OutcomeId.Value);
                if (outcome == null) return BadRequest("Outcome not found");
                subjectId = outcome.SubjectId;
            }

            var model = new Topic
            {
                TopicPurpose = dto.TopicPurpose,
                TopicCode = dto.TopicCode,
                TopicDescription = dto.TopicDescription,
                SubjectCredits = dto.SubjectCredits,
                NotionalHours = dto.NotionalHours,
                PeriodsPerTopic = dto.PeriodsPerTopic,
                PeriodsPerTopicManualOverride = dto.PeriodsPerTopic.HasValue && dto.PeriodsPerTopic.Value > 0,
                TopicCredits = dto.TopicCredits,
                TopicPercentage = dto.TopicPercentage,
                Order = dto.Order,
                SubjectId = subjectId,
                OutcomeId = dto.OutcomeId
            };
            _context.Topics.Add(model);
            _context.SaveChanges();

            if (!string.IsNullOrWhiteSpace(dto.AssessmentCriteriaDescription))
            {
                var criteria = new AssessmentCriteria
                {
                    TopicId = model.Id,
                    Description = dto.AssessmentCriteriaDescription,
                    CriteriaType = "Topic",
                    Weight = 1.0
                };
                _context.AssessmentCriteria.Add(criteria);
                _context.SaveChanges();
            }

            if (!dto.PeriodsPerTopic.HasValue || dto.PeriodsPerTopic.Value <= 0)
            {
                ApplyAutoPeriodsForSubject(subjectId);
            }

            var created = BuildTopicQuery().FirstOrDefault(t => t.Id == model.Id);
            return CreatedAtAction(nameof(Get), new { id = model.Id }, created);
        }

        [HttpPut("{id}")]
        public IActionResult Update(int id, ETD.Api.DTOs.UpdateTopicDto dto)
        {
            var item = _context.Topics.Find(id);
            if (item == null) return NotFound();
            var previousSubjectId = item.SubjectId;

            var subjectId = dto.SubjectId;
            if (dto.OutcomeId.HasValue)
            {
                var outcome = _context.Outcomes.Find(dto.OutcomeId.Value);
                if (outcome == null) return BadRequest("Outcome not found");
                subjectId = outcome.SubjectId;
            }

            item.TopicPurpose = dto.TopicPurpose;
            item.TopicCode = dto.TopicCode;
            item.TopicDescription = dto.TopicDescription;
            item.SubjectCredits = dto.SubjectCredits;
            item.NotionalHours = dto.NotionalHours;
            item.PeriodsPerTopic = dto.PeriodsPerTopic;
            item.PeriodsPerTopicManualOverride = dto.PeriodsPerTopic.HasValue && dto.PeriodsPerTopic.Value > 0;
            item.TopicCredits = dto.TopicCredits;
            item.TopicPercentage = dto.TopicPercentage;
            item.Order = dto.Order;
            item.SubjectId = subjectId;
            item.OutcomeId = dto.OutcomeId;
            _context.SaveChanges();

            if (!string.IsNullOrWhiteSpace(dto.AssessmentCriteriaDescription))
            {
                var criteria = _context.AssessmentCriteria.FirstOrDefault(c => c.TopicId == id);
                if (criteria == null)
                {
                    criteria = new AssessmentCriteria
                    {
                        TopicId = id,
                        Description = dto.AssessmentCriteriaDescription,
                        CriteriaType = "Topic",
                        Weight = 1.0
                    };
                    _context.AssessmentCriteria.Add(criteria);
                }
                else
                {
                    criteria.Description = dto.AssessmentCriteriaDescription;
                }

                _context.SaveChanges();
            }

            var manualPeriodsProvided = dto.PeriodsPerTopic.HasValue && dto.PeriodsPerTopic.Value > 0;
            if (!manualPeriodsProvided)
            {
                ApplyAutoPeriodsForSubject(subjectId);
                if (previousSubjectId != subjectId)
                {
                    ApplyAutoPeriodsForSubject(previousSubjectId);
                }
            }

            var updated = BuildTopicQuery().FirstOrDefault(t => t.Id == id);
            return Ok(updated);
        }

        [HttpPost("apply-periods-by-subject")]
        public IActionResult ApplyPeriodsBySubject([FromBody] ApplySubjectPeriodsRequest req)
        {
            var subjectId = req?.SubjectId ?? 0;
            var periods = req?.PeriodsPerTopic;
            if (subjectId <= 0) return BadRequest("SubjectId is required.");
            if (!periods.HasValue || periods.Value <= 0) return BadRequest("PeriodsPerTopic must be greater than 0.");

            var subject = _context.Subjects.Find(subjectId);
            if (subject == null) return NotFound("Subject not found.");

            var topics = _context.Topics.Where(t => t.SubjectId == subjectId).ToList();
            if (topics.Count == 0) return BadRequest("No topics found for this subject.");

            var applied = Math.Round(periods.Value, 2, MidpointRounding.AwayFromZero);
            foreach (var t in topics)
            {
                t.PeriodsPerTopic = applied;
                t.PeriodsPerTopicManualOverride = true;
            }
            _context.SaveChanges();

            return Ok(new
            {
                updated = topics.Count,
                subjectId,
                qualificationId = subject.QualificationId,
                periodsPerTopic = applied
            });
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var item = _context.Topics.Find(id);
            if (item == null) return NotFound();
            var subjectId = item.SubjectId;

            _context.Topics.Remove(item);
            _context.SaveChanges();
            ApplyAutoPeriodsForSubject(subjectId);
            return NoContent();
        }

        private IQueryable<ETD.Api.DTOs.TopicDto> BuildTopicQuery()
        {
            return _context.Topics
                .Select(t => new ETD.Api.DTOs.TopicDto
                {
                    Id = t.Id,
                    SubjectId = t.SubjectId,
                    OutcomeId = t.OutcomeId,
                    OutcomeCode = t.Outcome != null ? t.Outcome.OutcomeCode : null,
                    OutcomeDescription = t.Outcome != null ? t.Outcome.OutcomeDescription : null,
                    QualificationId = t.Subject != null ? t.Subject.QualificationId : 0,
                    SubjectCode = t.Subject != null ? t.Subject.SubjectCode : string.Empty,
                    PhasesCode = t.Subject != null && t.Subject.CurriculumPhase != null ? t.Subject.CurriculumPhase.Name : string.Empty,
                    SubjectDescription = t.Subject != null ? t.Subject.SubjectDescription : string.Empty,
                    SubjectCredits = t.SubjectCredits,
                    NotionalHours = t.NotionalHours,
                    PeriodsPerTopic = t.PeriodsPerTopic,
                    PeriodsPerTopicManualOverride = t.PeriodsPerTopicManualOverride,
                    TopicPurpose = t.TopicPurpose,
                    TopicCode = t.TopicCode,
                    TopicDescription = t.TopicDescription,
                    TopicCredits = t.TopicCredits,
                    TopicPercentage = t.TopicPercentage,
                    Order = t.Order,
                    AssessmentCriteriaId = _context.AssessmentCriteria.Where(c => c.TopicId == t.Id).Select(c => (int?)c.Id).FirstOrDefault(),
                    AssessmentCriteriaDescription = _context.AssessmentCriteria.Where(c => c.TopicId == t.Id).Select(c => c.Description).FirstOrDefault()
                });
        }

        private static string? ResolveTemplate(params string[] names)
        {
            foreach (var root in TemplateRoots)
            {
                foreach (var name in names)
                {
                    var path = Path.Combine(root, name);
                    if (System.IO.File.Exists(path)) return path;
                }
            }
            return null;
        }

        private static string? ResolveCsvPath(string? csvPath, params string[] templateNames)
        {
            if (!string.IsNullOrWhiteSpace(csvPath))
            {
                var fullPath = Path.GetFullPath(csvPath.Trim());
                if (System.IO.File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return ResolveTemplate(templateNames);
        }

        private int NormalizeQualificationId(int qualificationRef)
        {
            if (qualificationRef <= 0) return 0;

            var byId = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationRef);
            if (byId != null) return byId.Id;

            var key = qualificationRef.ToString();
            var byNumber = _context.Qualifications.FirstOrDefault(q => q.QualificationNumber == key);
            return byNumber?.Id ?? 0;
        }

        private CurriculumPhase ResolveOrCreatePhase(string phaseCode, string phaseDescription)
        {
            CurriculumPhase? phase = null;
            if (!string.IsNullOrWhiteSpace(phaseCode))
            {
                phase = _context.CurriculumPhases.FirstOrDefault(p => p.Name == phaseCode);
            }
            if (phase == null && !string.IsNullOrWhiteSpace(phaseDescription))
            {
                phase = _context.CurriculumPhases.FirstOrDefault(p => p.Description == phaseDescription);
            }
            if (phase == null)
            {
                phase = _context.CurriculumPhases.FirstOrDefault();
            }
            if (phase != null) return phase;

            phase = new CurriculumPhase
            {
                Name = string.IsNullOrWhiteSpace(phaseCode) ? "Default Phase" : phaseCode,
                Description = phaseDescription,
                Sequence = 1
            };
            _context.CurriculumPhases.Add(phase);
            _context.SaveChanges();
            return phase;
        }

        private static int FindColumn(string[] header, params string[] names)
        {
            foreach (var name in names)
            {
                var idx = Array.FindIndex(header, h => string.Equals(h?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) return idx;
            }

            var normalizedNames = names.Select(NormalizeHeader).Where(n => n.Length > 0).ToHashSet();
            for (var i = 0; i < header.Length; i++)
            {
                if (normalizedNames.Contains(NormalizeHeader(header[i])))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeHeader(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        private static string Cell(string[] row, int index)
        {
            if (index < 0 || index >= row.Length) return string.Empty;
            return row[index] ?? string.Empty;
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

        private static int? ParseInt(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var text = raw.Trim();
            if (int.TryParse(text, out var direct)) return direct;

            var n = ParseFlexibleNumber(text);
            if (!n.HasValue) return null;
            return (int)Math.Round(n.Value, MidpointRounding.AwayFromZero);
        }

        private static double? ParseFlexibleNumber(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var s = raw.Trim().Replace(" ", string.Empty);
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var direct))
            {
                return direct;
            }

            if (s.Contains(',') && !s.Contains('.'))
            {
                s = s.Replace(',', '.');
            }
            else if (s.Contains(',') && s.Contains('.'))
            {
                s = s.Replace(",", string.Empty);
            }

            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var normalized))
            {
                return normalized;
            }

            var match = Regex.Match(raw, @"-?\d+(?:[\.,]\d+)?");
            if (match.Success)
            {
                var token = match.Value.Replace(',', '.');
                if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out var tokenValue))
                {
                    return tokenValue;
                }
            }

            return null;
        }

        private static int ParseLpnSort(string? lpn)
        {
            var raw = (lpn ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            if (int.TryParse(raw, out var direct)) return direct;
            var token = Regex.Match(raw, @"\d+").Value;
            if (int.TryParse(token, out var parsed)) return parsed;
            return 0;
        }

        private static int EstimateDurationMinutes(double? periodsPerTopic)
        {
            var periods = periodsPerTopic.HasValue
                ? Math.Max(1, (int)Math.Ceiling(periodsPerTopic.Value))
                : 1;
            return periods * 40;
        }

        private void ApplyAutoPeriodsForSubject(int subjectId)
        {
            if (subjectId <= 0) return;

            var subject = _context.Subjects.FirstOrDefault(s => s.Id == subjectId);
            if (subject == null) return;

            var qualificationNumber = _context.Qualifications
                .Where(q => q.Id == subject.QualificationId)
                .Select(q => q.QualificationNumber)
                .FirstOrDefault();

            // Preserve imported values for qualification 90420.
            if (string.Equals(qualificationNumber, "90420", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var topics = _context.Topics.Where(t => t.SubjectId == subjectId).ToList();
            if (topics.Count == 0) return;

            var credits = ResolveSubjectCredits(subject, topics);
            if (!credits.HasValue || credits.Value <= 0) return;

            var autoPeriods = Math.Round((credits.Value * 10d) / topics.Count, 2, MidpointRounding.AwayFromZero);
            var autoManagedTopics = topics.Where(t => !t.PeriodsPerTopicManualOverride).ToList();
            if (autoManagedTopics.Count == 0) return;

            var changed = 0;
            foreach (var t in autoManagedTopics)
            {
                if (!t.PeriodsPerTopic.HasValue || Math.Abs(t.PeriodsPerTopic.Value - autoPeriods) > 0.0001d)
                {
                    t.PeriodsPerTopic = autoPeriods;
                    changed++;
                }
            }

            if (changed > 0)
            {
                _context.SaveChanges();
            }
        }

        private static double? ResolveSubjectCredits(Subject subject, List<Topic> topics)
        {
            if (subject.SubjectCredits.HasValue && subject.SubjectCredits.Value > 0)
            {
                return subject.SubjectCredits.Value;
            }

            var topicCreditCandidates = topics
                .Where(t => t.SubjectCredits.HasValue && t.SubjectCredits.Value > 0)
                .Select(t => t.SubjectCredits!.Value)
                .ToList();

            if (topicCreditCandidates.Count == 0) return null;
            return topicCreditCandidates.Max();
        }

        public class ApplySubjectPeriodsRequest
        {
            public int SubjectId { get; set; }
            public double? PeriodsPerTopic { get; set; }
        }
    }
}
