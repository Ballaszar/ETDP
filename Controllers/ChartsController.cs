using Microsoft.AspNetCore.Mvc;
using ETD.Api.Data;
using ETD.Api.Models;
using System.Linq;
using System.IO;
using System.Text.Json;
using System;
using System.Collections.Generic;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChartsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ChartsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("flow-program")]
        public IActionResult FlowProgram(
            [FromQuery] int? qualificationId = null,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null,
            [FromQuery] bool archive = false)
        {
            var qualification = qualificationId.HasValue
                ? _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId.Value)
                : _context.Qualifications.FirstOrDefault();
            if (qualification == null) return BadRequest("No qualification available");

            var subjects = ResolveSubjectRange(qualification.Id, subjectFromId, subjectToId);
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");
            var phasesById = _context.CurriculumPhases.ToDictionary(p => p.Id, p => p);
            var subjectIds = subjects.Select(s => s.Id).ToList();

            var topics = _context.Topics
                .Where(t => subjectIds.Contains(t.SubjectId))
                .ToList();
            var topicsById = topics.ToDictionary(t => t.Id, t => t);
            var topicsBySubjectId = topics.GroupBy(t => t.SubjectId).ToDictionary(g => g.Key, g => g.ToList());
            var topicIds = topics.Select(t => t.Id).ToList();

            var criteria = _context.AssessmentCriteria
                .Where(c => topicIds.Contains(c.TopicId))
                .ToList();
            var criteriaByTopicId = criteria.GroupBy(c => c.TopicId).ToDictionary(g => g.Key, g => g.ToList());
            var criteriaById = criteria.ToDictionary(c => c.Id, c => c);
            var criteriaIds = criteria.Select(c => c.Id).ToList();

            var plans = _context.LessonPlans
                .Where(lp => criteriaIds.Contains(lp.AssessmentCriteriaId))
                .ToList();
            var plansByCriteriaId = plans.GroupBy(lp => lp.AssessmentCriteriaId).ToDictionary(g => g.Key, g => g.ToList());
            var toolkit = _context.LecturerToolkitEntries
                .Where(e => e.QualificationsId == qualification.Id)
                .ToList();

            var modules = subjects
                .OrderBy(s => s.CurriculumPhaseId)
                .ThenBy(s => s.SubjectCode)
                .Select(s =>
            {
                var topicNodes = new List<(int TopicId, string? TopicCode, string? TopicDescription, List<(int LessonPlanId, string? Title, int DurationMinutes)> LessonPlans)>();
                var totalDuration = 0;

                if (topicsBySubjectId.TryGetValue(s.Id, out var subjectTopics))
                {
                    foreach (var topic in subjectTopics.OrderBy(t => t.Order ?? int.MaxValue).ThenBy(t => t.TopicCode))
                    {
                        var lessonNodes = new List<(int LessonPlanId, string? Title, int DurationMinutes)>();
                        if (criteriaByTopicId.TryGetValue(topic.Id, out var topicCriteria))
                        {
                            foreach (var criterion in topicCriteria)
                            {
                                if (!plansByCriteriaId.TryGetValue(criterion.Id, out var criterionPlans))
                                {
                                    continue;
                                }

                                foreach (var plan in criterionPlans.OrderBy(p => p.SortOrder).ThenBy(p => p.Title))
                                {
                                    var duration = plan.DurationMinutes ?? 0;
                                    totalDuration += duration;
                                    lessonNodes.Add((plan.Id, plan.Title, duration));
                                }
                            }
                        }

                        topicNodes.Add((topic.Id, topic.TopicCode, topic.TopicDescription, lessonNodes));
                    }
                }

                var sessions = topicNodes
                    .SelectMany(topicNode => topicNode.LessonPlans.Select(lesson => new
                    {
                        lessonPlanId = lesson.LessonPlanId,
                        title = lesson.Title,
                        durationMinutes = lesson.DurationMinutes,
                        topicId = topicNode.TopicId,
                        topicCode = topicNode.TopicCode,
                        topicDescription = topicNode.TopicDescription
                    }))
                    .ToList();

                var subjectToolkitEntries = toolkit
                    .Where(e =>
                        string.Equals((e.SubjectCode ?? "").Trim(), (s.SubjectCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals((e.SubjectDescription ?? "").Trim(), (s.SubjectDescription ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => ParseLpnSequence(e.Lpn))
                    .ThenBy(e => e.Lpn)
                    .ToList();

                var lessonCards = subjectToolkitEntries.Select((e, idx) =>
                {
                    var topicLabel = e.AssessmentCriteriaDescription ?? "";
                    if (e.AssessmentCriteriaId.HasValue && criteriaById.TryGetValue(e.AssessmentCriteriaId.Value, out var crit) && topicsById.TryGetValue(crit.TopicId, out var topic))
                    {
                        topicLabel = $"{topic.TopicCode} - {topic.TopicDescription}";
                    }

                    return new
                    {
                        index = idx + 1,
                        lpn = string.IsNullOrWhiteSpace(e.Lpn) ? $"LPN {idx + 1}" : e.Lpn,
                        lessonPlanDescription = string.IsNullOrWhiteSpace(e.LessonPlanDescription) ? (e.LessonPlanContent ?? "") : e.LessonPlanDescription,
                        topic = topicLabel,
                        timeStart = e.TimeStart,
                        timeEnd = e.TimeEnd
                    };
                }).ToList();

                if (lessonCards.Count == 0)
                {
                    lessonCards = sessions.Select((sess, idx) => new
                    {
                        index = idx + 1,
                        lpn = $"LPN {idx + 1}",
                        lessonPlanDescription = sess.title ?? "Lesson Plan",
                        topic = $"{sess.topicCode} - {sess.topicDescription}",
                        timeStart = "",
                        timeEnd = ""
                    }).ToList();
                }

                var phaseName = phasesById.TryGetValue(s.CurriculumPhaseId, out var phase)
                    ? $"{phase.Name} (#{phase.Sequence})"
                    : $"Phase {s.CurriculumPhaseId}";

                var topicsOut = topicNodes.Select(topicNode => new
                {
                    topicId = topicNode.TopicId,
                    topicCode = topicNode.TopicCode,
                    topicDescription = topicNode.TopicDescription,
                    lessonPlans = topicNode.LessonPlans.Select(lesson => new
                    {
                        lessonPlanId = lesson.LessonPlanId,
                        title = lesson.Title,
                        durationMinutes = lesson.DurationMinutes
                    }).ToList()
                }).ToList();

                return new
                {
                    phaseId = s.CurriculumPhaseId,
                    phaseName,
                    subjectId = s.Id,
                    subjectCode = s.SubjectCode,
                    subjectDescription = s.SubjectDescription,
                    totalDurationMinutes = totalDuration,
                    lessonCards,
                    topics = topicsOut,
                    sessions
                };
            }).ToList();

            var data = new { qualification = new { id = qualification.Id, number = qualification.QualificationNumber, name = qualification.QualificationDescription }, modules };

            string? path = null;
            if (archive)
            {
                var exportDir = Path.Combine(Directory.GetCurrentDirectory(), "Exports", "Charts");
                Directory.CreateDirectory(exportDir);
                path = Path.Combine(exportDir, $"flow_program_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                var json = JsonSerializer.Serialize(data);
                System.IO.File.WriteAllText(path, json);
            }

            return Ok(new { data, archivedPath = path });
        }

        [HttpGet("sunburst")]
        public IActionResult Sunburst(
            [FromQuery] int? qualificationId = null,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null,
            [FromQuery] bool archive = false)
        {
            var qualification = qualificationId.HasValue
                ? _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId.Value)
                : _context.Qualifications.FirstOrDefault();
            if (qualification == null) return BadRequest("No qualification available");

            var phasesById = _context.CurriculumPhases.ToDictionary(p => p.Id, p => p);
            var subjects = ResolveSubjectRange(qualification.Id, subjectFromId, subjectToId);
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");
            var toolkit = _context.LecturerToolkitEntries
                .Where(e => e.QualificationsId == qualification.Id)
                .ToList();

            var phaseNodes = subjects
                .GroupBy(s => s.CurriculumPhaseId)
                .OrderBy(g => phasesById.TryGetValue(g.Key, out var ph) ? ph.Sequence : int.MaxValue)
                .Select(g =>
            {
                var phaseName = phasesById.TryGetValue(g.Key, out var phase)
                    ? $"{phase.Name} (#{phase.Sequence})"
                    : $"Phase {g.Key}";

                var subjectNodes = g
                    .OrderBy(s => s.SubjectCode)
                    .Select(s =>
                {
                    var lpnCount = toolkit.Count(e =>
                        string.Equals((e.SubjectCode ?? "").Trim(), (s.SubjectCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals((e.SubjectDescription ?? "").Trim(), (s.SubjectDescription ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

                    return new
                    {
                        name = $"{s.SubjectCode} - {s.SubjectDescription}",
                        value = lpnCount
                    };
                })
                    .ToList();

                return new
                {
                    name = phaseName,
                    children = subjectNodes
                };
            }).ToList();

            var root = new
            {
                name = $"{qualification.QualificationNumber} - {qualification.QualificationDescription}",
                children = phaseNodes
            };

            string? path = null;
            if (archive)
            {
                var exportDir = Path.Combine(Directory.GetCurrentDirectory(), "Exports", "Charts");
                Directory.CreateDirectory(exportDir);
                path = Path.Combine(exportDir, $"sunburst_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                var json = JsonSerializer.Serialize(root);
                System.IO.File.WriteAllText(path, json);
            }

            return Ok(new { data = root, archivedPath = path });
        }

        private static int ParseLpnSequence(string? lpn)
        {
            if (string.IsNullOrWhiteSpace(lpn)) return int.MaxValue;
            var digits = new string(lpn.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? n : int.MaxValue;
        }

        [HttpPost("export-docx")]
        public IActionResult ExportDocx([FromBody] ChartsDocxRequest req)
        {
            const int emuPerPixel = 9525;
            const int emuPerTwip = 635;
            const int a4WidthTwips = 11906;
            const int a4HeightTwips = 16838;
            const int marginTwips = 720;

            var pages = new List<byte[]>();
            if (req.Base64PngPages != null && req.Base64PngPages.Count > 0)
            {
                foreach (var page in req.Base64PngPages.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    pages.Add(Convert.FromBase64String(page.Split(',').Last()));
                }
            }

            if (pages.Count == 0 && !string.IsNullOrWhiteSpace(req.Base64Png))
            {
                pages.Add(Convert.FromBase64String(req.Base64Png.Split(',').Last()));
            }

            if (pages.Count == 0) return BadRequest("Missing PNG");

            using var ms = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body ?? (main.Document.Body = new Body());
                var sourceCx = (long)Math.Max(1, req.WidthPx) * emuPerPixel;
                var sourceCy = (long)Math.Max(1, req.HeightPx) * emuPerPixel;
                var maxCx = (long)(a4WidthTwips - (marginTwips * 2)) * emuPerTwip;
                var maxCy = (long)(a4HeightTwips - (marginTwips * 2)) * emuPerTwip;
                var scale = Math.Min((double)maxCx / sourceCx, (double)maxCy / sourceCy);
                var targetCx = (long)Math.Round(sourceCx * scale);
                var targetCy = (long)Math.Round(sourceCy * scale);

                for (var i = 0; i < pages.Count; i++)
                {
                    var pngBytes = pages[i];
                    var imgPart = main.AddImagePart(ImagePartType.Png);
                    using (var imgStream = new MemoryStream(pngBytes))
                    {
                        imgPart.FeedData(imgStream);
                    }
                    var relId = main.GetIdOfPart(imgPart);
                    var drawing = BuildInlineImage(relId, targetCx, targetCy, (uint)(i + 1), $"ChartPage_{i + 1}.png");
                    body.Append(new Paragraph(new Run(drawing)));

                    if (i < pages.Count - 1)
                    {
                        body.Append(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));
                    }
                }

                body.Append(new SectionProperties(
                    new PageSize
                    {
                        Width = (UInt32Value)(uint)a4WidthTwips,
                        Height = (UInt32Value)(uint)a4HeightTwips,
                        Orient = PageOrientationValues.Portrait
                    },
                    new PageMargin
                    {
                        Top = marginTwips,
                        Bottom = marginTwips,
                        Left = (uint)marginTwips,
                        Right = (uint)marginTwips,
                        Header = 450U,
                        Footer = 450U,
                        Gutter = 0U
                    }
                ));

                main.Document.Save();
            }
            ms.Position = 0;
            var fileName = $"Chart_{DateTime.Now:yyyyMMdd_HHmmss}.docx";
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
        }

        [HttpGet("workflow-rows")]
        public IActionResult WorkflowRows(
            [FromQuery] int? qualificationId = null,
            [FromQuery] int? subjectFromId = null,
            [FromQuery] int? subjectToId = null,
            [FromQuery] bool archive = false)
        {
            var qualification = qualificationId.HasValue
                ? _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId.Value)
                : _context.Qualifications.FirstOrDefault();
            if (qualification == null) return BadRequest("No qualification available");

            var subjects = ResolveSubjectRange(qualification.Id, subjectFromId, subjectToId);
            if (subjects.Count == 0) return BadRequest("No subjects were resolved for the selected range.");
            var subjectIds = subjects.Select(s => s.Id).ToList();
            var phasesById = _context.CurriculumPhases.ToDictionary(p => p.Id, p => p);

            var topics = _context.Topics
                .Where(t => subjectIds.Contains(t.SubjectId))
                .ToList();
            var topicsBySubjectId = topics.GroupBy(t => t.SubjectId).ToDictionary(g => g.Key, g => g.ToList());
            var topicIds = topics.Select(t => t.Id).ToList();

            var criteria = _context.AssessmentCriteria
                .Where(c => topicIds.Contains(c.TopicId))
                .ToList();
            var criteriaByTopicId = criteria.GroupBy(c => c.TopicId).ToDictionary(g => g.Key, g => g.ToList());
            var criteriaIds = criteria.Select(c => c.Id).ToList();

            var plans = _context.LessonPlans
                .Where(lp => criteriaIds.Contains(lp.AssessmentCriteriaId))
                .ToList();
            var plansByCriteriaId = plans.GroupBy(lp => lp.AssessmentCriteriaId).ToDictionary(g => g.Key, g => g.ToList());

            var rows = new List<object>();

            foreach (var subject in subjects.OrderBy(s => s.CurriculumPhaseId).ThenBy(s => s.SubjectCode))
            {
                var phaseName = phasesById.TryGetValue(subject.CurriculumPhaseId, out var phase)
                    ? $"{phase.Name} (#{phase.Sequence})"
                    : $"Phase {subject.CurriculumPhaseId}";

                if (!topicsBySubjectId.TryGetValue(subject.Id, out var subjectTopics) || subjectTopics.Count == 0)
                {
                    rows.Add(new
                    {
                        phaseId = subject.CurriculumPhaseId,
                        phase = phaseName,
                        subjectId = subject.Id,
                        subject = $"{subject.SubjectCode} - {subject.SubjectDescription}",
                        topicId = (int?)null,
                        topic = (string?)null,
                        lessonPlanId = (int?)null,
                        lessonPlan = (string?)null,
                        durationMinutes = (int?)null
                    });
                    continue;
                }

                foreach (var topic in subjectTopics.OrderBy(t => t.Order ?? int.MaxValue).ThenBy(t => t.TopicCode))
                {
                    if (!criteriaByTopicId.TryGetValue(topic.Id, out var topicCriteria) || topicCriteria.Count == 0)
                    {
                        rows.Add(new
                        {
                            phaseId = subject.CurriculumPhaseId,
                            phase = phaseName,
                            subjectId = subject.Id,
                            subject = $"{subject.SubjectCode} - {subject.SubjectDescription}",
                            topicId = topic.Id,
                            topic = $"{topic.TopicCode} - {topic.TopicDescription}",
                            lessonPlanId = (int?)null,
                            lessonPlan = (string?)null,
                            durationMinutes = (int?)null
                        });
                        continue;
                    }

                    var hasPlans = false;
                    foreach (var criterion in topicCriteria)
                    {
                        if (!plansByCriteriaId.TryGetValue(criterion.Id, out var criterionPlans) || criterionPlans.Count == 0)
                        {
                            continue;
                        }

                        foreach (var plan in criterionPlans.OrderBy(p => p.SortOrder).ThenBy(p => p.Title))
                        {
                            hasPlans = true;
                            rows.Add(new
                            {
                                phaseId = subject.CurriculumPhaseId,
                                phase = phaseName,
                                subjectId = subject.Id,
                                subject = $"{subject.SubjectCode} - {subject.SubjectDescription}",
                                topicId = topic.Id,
                                topic = $"{topic.TopicCode} - {topic.TopicDescription}",
                                lessonPlanId = plan.Id,
                                lessonPlan = plan.Title,
                                durationMinutes = plan.DurationMinutes
                            });
                        }
                    }

                    if (!hasPlans)
                    {
                        rows.Add(new
                        {
                            phaseId = subject.CurriculumPhaseId,
                            phase = phaseName,
                            subjectId = subject.Id,
                            subject = $"{subject.SubjectCode} - {subject.SubjectDescription}",
                            topicId = topic.Id,
                            topic = $"{topic.TopicCode} - {topic.TopicDescription}",
                            lessonPlanId = (int?)null,
                            lessonPlan = (string?)null,
                            durationMinutes = (int?)null
                        });
                    }
                }
            }

            var data = new
            {
                qualification = new
                {
                    id = qualification.Id,
                    number = qualification.QualificationNumber,
                    name = qualification.QualificationDescription
                },
                rows
            };

            string? path = null;
            if (archive)
            {
                var exportDir = Path.Combine(Directory.GetCurrentDirectory(), "Exports", "Charts");
                Directory.CreateDirectory(exportDir);
                path = Path.Combine(exportDir, $"workflow_rows_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                var json = JsonSerializer.Serialize(data);
                System.IO.File.WriteAllText(path, json);
            }

            return Ok(new { data, archivedPath = path });
        }

        private static Drawing BuildInlineImage(string relId, long cx, long cy, uint drawingId, string imageName)
        {
            var inline = new DW.Inline(
                new DW.Extent() { Cx = cx, Cy = cy },
                new DW.DocProperties() { Id = drawingId, Name = imageName },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks() { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties() { Id = drawingId, Name = imageName },
                                new PIC.NonVisualPictureDrawingProperties()
                            ),
                            new PIC.BlipFill(
                                new A.Blip() { Embed = relId },
                                new A.Stretch(new A.FillRectangle())
                            ),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset() { X = 0, Y = 0 },
                                    new A.Extents() { Cx = cx, Cy = cy }
                                ),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }
                            )
                        )
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                )
            );
            return new Drawing(inline);
        }

        private List<Subject> ResolveSubjectRange(int qualificationId, int? subjectFromId, int? subjectToId)
        {
            var subjects = _context.Subjects
                .Where(s => s.QualificationId == qualificationId)
                .OrderBy(s => s.SubjectCode)
                .ThenBy(s => s.SubjectDescription)
                .ToList();
            if (subjects.Count == 0) return new List<Subject>();

            var fromIndex = 0;
            var toIndex = subjects.Count - 1;

            if (subjectFromId.HasValue && subjectFromId.Value > 0)
            {
                fromIndex = subjects.FindIndex(s => s.Id == subjectFromId.Value);
                if (fromIndex < 0) return new List<Subject>();
            }

            if (subjectToId.HasValue && subjectToId.Value > 0)
            {
                toIndex = subjects.FindIndex(s => s.Id == subjectToId.Value);
                if (toIndex < 0) return new List<Subject>();
            }

            if (fromIndex > toIndex)
            {
                (fromIndex, toIndex) = (toIndex, fromIndex);
            }

            return subjects
                .Skip(fromIndex)
                .Take((toIndex - fromIndex) + 1)
                .ToList();
        }

        public class ChartsDocxRequest
        {
            public string Base64Png { get; set; } = string.Empty;
            public List<string> Base64PngPages { get; set; } = new();
            public int WidthPx { get; set; } = 900;
            public int HeightPx { get; set; } = 600;
        }
    }
}
