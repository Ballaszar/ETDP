using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ETD.Api.Data;
using ETD.Api.Models;
using ETD.Api.Services;
using ETD.Api.Utils;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.AspNetCore.Mvc;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QualityCouncilCurriculaController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly OcrExtractionService _ocrExtractionService;
        private readonly CurriculumKnowledgeScanService _curriculumKnowledgeScanService;
        private readonly CurriculumPipelineService _curriculumPipelineService;
        private readonly KnowledgeHierarchyService _knowledgeHierarchyService;
        private readonly SansMetadataService _sansMetadataService;
        private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".txt", ".md" };
        private static readonly Regex PhaseCodeRegex = new(@"^\d{6,9}-(?:KM|PM|WM)-\d{2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SubjectCodeRegex = new(@"^(?:KM-\d{2}-KT\d{2}|PM-\d{2}-PS\d{2}|WM-\d{2}-WE\d{2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TopicCodeRegex = new(@"^[A-Z]{2}\d{4,6}[A-Z]?$", RegexOptions.Compiled);
        private static readonly Regex SharedQctoCodeRegex = new(@"^QCTO_(?<code>\d{4,})_", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public QualityCouncilCurriculaController(
            ApplicationDbContext context,
            OcrExtractionService ocrExtractionService,
            CurriculumKnowledgeScanService curriculumKnowledgeScanService,
            CurriculumPipelineService curriculumPipelineService,
            KnowledgeHierarchyService knowledgeHierarchyService,
            SansMetadataService sansMetadataService)
        {
            _context = context;
            _ocrExtractionService = ocrExtractionService;
            _curriculumKnowledgeScanService = curriculumKnowledgeScanService;
            _curriculumPipelineService = curriculumPipelineService;
            _knowledgeHierarchyService = knowledgeHierarchyService;
            _sansMetadataService = sansMetadataService;
        }

        [HttpGet("tree")]
        public IActionResult Tree()
        {
            var baseDir = ResolveImportsBaseDir();
            var qualifications = _context.Qualifications
                .OrderBy(q => q.QualificationNumber)
                .ThenBy(q => q.Id)
                .ToList();

            var nodes = qualifications.Select(q =>
            {
                var dir = ResolveQualificationFolder(baseDir, q, ensureExists: false);
                var files = Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "QC_*.*")
                        .Where(f => AllowedExt.Contains(Path.GetExtension(f)))
                        .Select(f => Path.GetFileName(f) ?? string.Empty)
                        .Where(n => n.Length > 0)
                        .OrderBy(n => n)
                        .ToList()
                    : new List<string>();

                var curriculum = files.FirstOrDefault(f => IsCurriculumFileName(f));
                var assessment = files.FirstOrDefault(f => IsAssessmentFileName(f));

                return new
                {
                    qualificationId = q.Id,
                    qualificationNumber = q.QualificationNumber,
                    qualificationDescription = q.QualificationDescription,
                    folderPath = dir,
                    hasCurriculumSpecification = !string.IsNullOrWhiteSpace(curriculum),
                    curriculumSpecificationFile = curriculum,
                    hasAssessmentSpecification = !string.IsNullOrWhiteSpace(assessment),
                    assessmentSpecificationFile = assessment,
                    files
                };
            }).ToList();

            return Ok(new { baseDir, nodes });
        }

        [HttpGet("status")]
        public IActionResult Status([FromQuery] int qualificationId)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required.");
            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var baseDir = ResolveImportsBaseDir();
            var dir = ResolveQualificationFolder(baseDir, qualification, ensureExists: false);
            var files = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "QC_*.*")
                    .Where(f => AllowedExt.Contains(Path.GetExtension(f)))
                    .ToList()
                : new List<string>();

            var curriculum = files.FirstOrDefault(f => IsCurriculumFileName(Path.GetFileName(f)));
            var assessment = files.FirstOrDefault(f => IsAssessmentFileName(Path.GetFileName(f)));

            return Ok(new
            {
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                qualificationDescription = qualification.QualificationDescription,
                folderPath = dir,
                hasCurriculumSpecification = curriculum != null,
                curriculumSpecificationFile = curriculum != null ? Path.GetFileName(curriculum) : null,
                hasAssessmentSpecification = assessment != null,
                assessmentSpecificationFile = assessment != null ? Path.GetFileName(assessment) : null,
                automationReady = curriculum != null && assessment != null,
                warning = "Cognitive scraping can fail on some PDF builds. Always verify extracted text before relying on automation."
            });
        }

        [HttpPost("upload")]
        [RequestSizeLimit(500_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 500_000_000)]
        public async Task<IActionResult> Upload([FromForm] UploadRequest req)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");
            if (req.File == null || req.File.Length == 0) return BadRequest("A document file is required.");
            var docType = (req.DocType ?? "").Trim().ToLowerInvariant();
            if (docType != "curriculum" && docType != "assessment")
                return BadRequest("DocType must be 'curriculum' or 'assessment'.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var ext = Path.GetExtension(req.File.FileName).ToLowerInvariant();
            if (!AllowedExt.Contains(ext))
                return BadRequest("Unsupported file type. Allowed: .pdf, .docx, .txt, .md");

            var baseDir = ResolveImportsBaseDir();
            var dir = ResolveQualificationFolder(baseDir, qualification, ensureExists: true);
            var prefix = docType == "curriculum" ? "QC_CurriculumSpecification" : "QC_AssessmentSpecification";

            foreach (var existing in Directory.GetFiles(dir, $"{prefix}.*"))
            {
                System.IO.File.Delete(existing);
            }

            var path = Path.Combine(dir, $"{prefix}{ext}");
            try
            {
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                await req.File.CopyToAsync(fs);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to save upload: {ex.Message}", path });
            }            var structure = EnsureQualificationLibraryStructure(qualification);
            var curriculumLibraryDocumentPath = MirrorSpecificationToCurriculumLibrary(structure, path, docType);

            object? pipelineJob = null;
            if (req.AutoStartPipeline)
            {
                try
                {
                    pipelineJob = await _curriculumPipelineService.QueueQualificationAsync(
                        qualification.Id,
                        req.StartPage,
                        forceRestart: false);
                }
                catch (Exception ex)
                {
                    return Ok(new
                    {
                        uploaded = true,
                        qualificationId = qualification.Id,
                        qualificationNumber = qualification.QualificationNumber,
                        qualificationDescription = qualification.QualificationDescription,
                        docType,
                        path,
                        qualificationLibraryRootPath = structure.QualificationRootPath,
                        curriculumLibraryPath = structure.CurriculumLibraryPath,
                        curriculumLibraryDocumentPath,
                        pipelineQueued = false,
                        pipelineQueueError = ex.Message
                    });
                }
            }

            return Ok(new
            {
                uploaded = true,
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                qualificationDescription = qualification.QualificationDescription,
                docType,
                path,
                qualificationLibraryRootPath = structure.QualificationRootPath,
                curriculumLibraryPath = structure.CurriculumLibraryPath,
                curriculumLibraryDocumentPath,
                pipelineQueued = pipelineJob != null,
                pipelineJob
            });
        }

        [HttpGet("library")]
        public IActionResult SharedLibrary(
            [FromQuery] int? qualificationId = null,
            [FromQuery] string? qualificationCode = null,
            [FromQuery] string? qualificationDescription = null)
        {
            var qualification = ResolveQualification(qualificationId, qualificationCode, qualificationDescription);
            var libraryRootPath = ResolveSharedQctoLibraryRootPath();
            var matches = BuildSharedQctoLibraryCatalog(
                qualification?.Id,
                qualification?.QualificationNumber ?? qualificationCode,
                qualification?.QualificationDescription ?? qualificationDescription);

            return Ok(new
            {
                libraryRootPath,
                qualificationId = qualification?.Id,
                qualificationCode = qualification?.QualificationNumber ?? qualificationCode ?? string.Empty,
                qualificationDescription = qualification?.QualificationDescription ?? qualificationDescription ?? string.Empty,
                matches
            });
        }

        [HttpPost("import-from-library")]
        public async Task<IActionResult> ImportFromSharedLibrary([FromBody] ImportFromLibraryRequest req)
        {
            if (req == null) return BadRequest("Import payload is required.");
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");

            var docType = (req.DocType ?? string.Empty).Trim().ToLowerInvariant();
            if (docType != "curriculum" && docType != "assessment")
            {
                return BadRequest("DocType must be 'curriculum' or 'assessment'.");
            }

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var sourcePath = (req.SourcePath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return BadRequest("SourcePath is required.");
            }

            var libraryRootPath = ResolveSharedQctoLibraryRootPath();
            var sourceFullPath = Path.GetFullPath(sourcePath);
            var libraryRootFullPath = Path.GetFullPath(libraryRootPath);
            if (!sourceFullPath.StartsWith(libraryRootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Selected source path is outside the shared QCTO library.");
            }

            if (!System.IO.File.Exists(sourceFullPath))
            {
                return NotFound($"Library file not found: {sourceFullPath}");
            }

            var ext = Path.GetExtension(sourceFullPath).ToLowerInvariant();
            if (!AllowedExt.Contains(ext))
            {
                return BadRequest("Unsupported library file type. Allowed: .pdf, .docx, .txt, .md");
            }

            var baseDir = ResolveImportsBaseDir();
            var dir = ResolveQualificationFolder(baseDir, qualification, ensureExists: true);
            var prefix = docType == "curriculum" ? "QC_CurriculumSpecification" : "QC_AssessmentSpecification";

            foreach (var existing in Directory.GetFiles(dir, $"{prefix}.*"))
            {
                System.IO.File.Delete(existing);
            }

            var destinationPath = Path.Combine(dir, $"{prefix}{ext}");
            var resolvedGitLfsPointer = await GitLfsPointerResolver.CopyResolvedContentAsync(sourceFullPath, destinationPath);

            var structure = EnsureQualificationLibraryStructure(qualification);
            var curriculumLibraryDocumentPath = MirrorSpecificationToCurriculumLibrary(structure, destinationPath, docType);

            return Ok(new
            {
                imported = true,
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                qualificationDescription = qualification.QualificationDescription,
                docType,
                sourcePath = sourceFullPath,
                destinationPath,
                resolvedGitLfsPointer,
                libraryRootPath,
                qualificationLibraryRootPath = structure.QualificationRootPath,
                curriculumLibraryPath = structure.CurriculumLibraryPath,
                curriculumLibraryDocumentPath
            });
        }

        [HttpPost("run-scrape")]
        public async Task<IActionResult> RunScrape([FromBody] QualificationRequest req)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");
            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var baseDir = ResolveImportsBaseDir();
            var dir = ResolveQualificationFolder(baseDir, qualification, ensureExists: false);
            if (!Directory.Exists(dir)) return NotFound($"Qualification folder not found: {dir}");

            var allQcFiles = Directory.GetFiles(dir, "QC_*.*")
                .Where(f => AllowedExt.Contains(Path.GetExtension(f)))
                .ToList();
            var curriculum = allQcFiles.FirstOrDefault(f => IsCurriculumFileName(Path.GetFileName(f)));
            var assessment = allQcFiles.FirstOrDefault(f => IsAssessmentFileName(Path.GetFileName(f)));
            if (curriculum == null || assessment == null)
            {
                return BadRequest("Both compulsory documents are required before running automation: Curriculum Specification and Assessment Specification.");
            }

            var created = 0;
            var skipped = 0;
            var failed = 0;
            var details = new List<object>();

            foreach (var path in allQcFiles)
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var fileName = Path.GetFileName(path);
                if (_context.SourceMaterials.Any(s => s.FilePath == path))
                {
                    skipped++;
                    details.Add(new { file = fileName, action = "skipped", reason = "Already imported" });
                    continue;
                }

                string text;
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    text = await ExtractTextFromFileStreamAsync(stream, ext);
                    text = await _ocrExtractionService.EnhanceExtractedTextAsync(path, ext, text);
                }
                catch (Exception ex)
                {
                    failed++;
                    details.Add(new { file = fileName, action = "failed", reason = ex.Message });
                    continue;
                }

                _context.SourceMaterials.Add(new SourceMaterial
                {
                    Title = fileName,
                    FileName = fileName,
                    FilePath = path,
                    FileType = ext.TrimStart('.'),
                    Url = string.Empty,
                    QualificationDescription = qualification.QualificationDescription,
                    ExtractedText = text ?? string.Empty
                });
                created++;
                details.Add(new { file = fileName, action = "created", extractedChars = (text ?? string.Empty).Length });
            }

            _context.SaveChanges();
            return Ok(new
            {
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                created,
                skipped,
                failed,
                details,
                warning = "Scraper output may vary by PDF build method. OCR enrichment is active for scanned PDFs/images when local Tesseract OCR is available. Validate extracted text quality before automation."
            });
        }

        [HttpPost("run-cognitive-scan")]
        public async Task<IActionResult> RunCognitiveScan([FromBody] CognitiveScanRequest req)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");
            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var baseDir = ResolveImportsBaseDir();
            var dir = ResolveQualificationFolder(baseDir, qualification, ensureExists: false);
            if (!Directory.Exists(dir)) return NotFound($"Qualification folder not found: {dir}");

            var curriculum = Directory.GetFiles(dir, "QC_*.*")
                .Where(f => AllowedExt.Contains(Path.GetExtension(f)))
                .FirstOrDefault(f => IsCurriculumFileName(Path.GetFileName(f)));
            if (curriculum == null)
            {
                return BadRequest("Curriculum Specification document is required before cognitive scan.");
            }

            var ext = Path.GetExtension(curriculum).ToLowerInvariant();
            var requestedStartPage = req.StartPage.GetValueOrDefault(10);
            if (requestedStartPage < 1) requestedStartPage = 1;

            string extractedText;
            try
            {
                extractedText = await ExtractTextForCognitiveScanAsync(curriculum, ext, requestedStartPage);
                extractedText = await _ocrExtractionService.EnhanceExtractedTextAsync(curriculum, ext, extractedText);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = $"Cognitive extraction failed: {ex.Message}",
                    curriculum
                });
            }

            var outputDir = ResolveCognitiveOutputFolder(
                dir,
                qualification.QualificationNumber,
                qualification.QualificationDescription);

            CognitiveScanArtifacts artifacts;
            try
            {
                artifacts = _curriculumKnowledgeScanService.GenerateArtifacts(
                    curriculum,
                    ext,
                    extractedText,
                    qualification.QualificationNumber,
                    outputDir,
                    ext == ".pdf" ? requestedStartPage : null);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = $"Cognitive parsing failed: {ex.Message}",
                    curriculum,
                    outputDir
                });
            }

            return Ok(new
            {
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                qualificationDescription = qualification.QualificationDescription,
                curriculumPath = curriculum,
                outputDir,
                startPageUsed = ext == ".pdf" ? requestedStartPage : (int?)null,
                moduleCount = artifacts.ModuleCount,
                curriculumPhaseCount = artifacts.CurriculumPhaseCount,
                knowledgeSubjectCount = artifacts.KnowledgeSubjectCount,
                topicCount = artifacts.TopicCount,
                warnings = artifacts.Warnings,
                outputs = new
                {
                    artifacts.ExtractTextPath,
                    artifacts.PhasesCsvPath,
                    artifacts.SubjectCsvPath,
                    artifacts.TopicCsvPath,
                    artifacts.ReportJsonPath
                }
            });
        }

        [HttpPost("build-mapping-review-queue")]
        public async Task<IActionResult> BuildMappingReviewQueue([FromBody] BuildMappingReviewQueueRequest req)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            CognitiveScanExecution? scan;
            try
            {
                scan = await ExecuteCognitiveScanAsync(qualification, req.StartPage);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = $"Cognitive scan failed: {ex.Message}"
                });
            }

            var queue = BuildMappingReviewQueueDocument(qualification, scan);
            var queuePath = ResolveMappingReviewQueuePath(scan.OutputDir);
            SaveMappingReviewQueue(queuePath, queue);

            return Ok(new
            {
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                qualificationDescription = qualification.QualificationDescription,
                scan = new
                {
                    curriculumPath = scan.CurriculumPath,
                    outputDir = scan.OutputDir,
                    startPageUsed = scan.StartPageUsed,
                    moduleCount = scan.Artifacts.ModuleCount,
                    curriculumPhaseCount = scan.Artifacts.CurriculumPhaseCount,
                    knowledgeSubjectCount = scan.Artifacts.KnowledgeSubjectCount,
                    topicCount = scan.Artifacts.TopicCount,
                    warnings = scan.Artifacts.Warnings,
                    outputs = new
                    {
                        scan.Artifacts.ExtractTextPath,
                        scan.Artifacts.PhasesCsvPath,
                        scan.Artifacts.SubjectCsvPath,
                        scan.Artifacts.TopicCsvPath,
                        scan.Artifacts.ReportJsonPath
                    }
                },
                reviewQueue = new
                {
                    queuePath,
                    summary = queue.Summary,
                    items = queue.Items
                }
            });
        }

        [HttpGet("mapping-review-queue")]
        public IActionResult GetMappingReviewQueue([FromQuery] int qualificationId)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var baseDir = ResolveImportsBaseDir();
            var qualificationFolder = ResolveQualificationFolder(baseDir, qualification, ensureExists: false);
            var queuePath = ResolveExistingMappingReviewQueuePath(qualificationFolder, qualification.QualificationNumber, qualification.QualificationDescription);
            if (string.IsNullOrWhiteSpace(queuePath) || !System.IO.File.Exists(queuePath))
            {
                return NotFound("No mapping review queue found. Run cognitive scan and build queue first.");
            }

            var queue = LoadMappingReviewQueue(queuePath);
            queue.Summary = BuildQueueSummary(queue.Items);
            SaveMappingReviewQueue(queuePath, queue);

            return Ok(new
            {
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                qualificationDescription = qualification.QualificationDescription,
                queuePath,
                scan = queue.Scan,
                summary = queue.Summary,
                items = queue.Items
            });
        }

        [HttpPost("apply-mapping-review")]
        public IActionResult ApplyMappingReview([FromBody] ApplyMappingReviewRequest req)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var baseDir = ResolveImportsBaseDir();
            var qualificationFolder = ResolveQualificationFolder(baseDir, qualification, ensureExists: false);
            var queuePath = ResolveExistingMappingReviewQueuePath(qualificationFolder, qualification.QualificationNumber, qualification.QualificationDescription);
            if (string.IsNullOrWhiteSpace(queuePath) || !System.IO.File.Exists(queuePath))
            {
                return NotFound("No mapping review queue found. Run cognitive scan and build queue first.");
            }

            var queue = LoadMappingReviewQueue(queuePath);
            var pendingOnly = req.PendingOnly;
            var requestedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(req.ItemId)) requestedIds.Add(req.ItemId.Trim());
            if (req.ItemIds != null)
            {
                foreach (var id in req.ItemIds.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    requestedIds.Add(id.Trim());
                }
            }

            var minConfidence = req.MinConfidence.GetValueOrDefault();
            if (req.MinConfidence.HasValue)
            {
                minConfidence = Math.Max(0, Math.Min(100, minConfidence));
            }

            var candidates = queue.Items
                .Where(item => requestedIds.Count == 0 || requestedIds.Contains(item.Id))
                .Where(item => !pendingOnly || string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase))
                .Where(item => !req.MinConfidence.HasValue || item.ConfidenceScore >= minConfidence)
                .ToList();

            if (candidates.Count == 0)
            {
                return BadRequest("No matching queue items to apply. Adjust filters or rebuild queue.");
            }

            var details = new List<object>();
            var applied = 0;
            var failed = 0;
            var skipped = 0;

            foreach (var item in candidates)
            {
                try
                {
                    var applyResult = ApplyMappingReviewItem(qualification, item);
                    item.Status = "applied";
                    item.LastError = null;
                    item.ReviewedAtUtc = DateTime.UtcNow;
                    applied++;
                    details.Add(new
                    {
                        item.Id,
                        item.EntityType,
                        status = "applied",
                        result = applyResult
                    });
                }
                catch (InvalidOperationException ex)
                {
                    item.Status = "failed";
                    item.LastError = ex.Message;
                    item.ReviewedAtUtc = DateTime.UtcNow;
                    failed++;
                    details.Add(new
                    {
                        item.Id,
                        item.EntityType,
                        status = "failed",
                        error = ex.Message
                    });
                }
                catch (Exception ex)
                {
                    item.Status = "failed";
                    item.LastError = ex.Message;
                    item.ReviewedAtUtc = DateTime.UtcNow;
                    failed++;
                    details.Add(new
                    {
                        item.Id,
                        item.EntityType,
                        status = "failed",
                        error = ex.Message
                    });
                }
            }

            skipped = candidates.Count - applied - failed;
            queue.UpdatedAtUtc = DateTime.UtcNow;
            queue.Summary = BuildQueueSummary(queue.Items);
            SaveMappingReviewQueue(queuePath, queue);

            return Ok(new
            {
                qualificationId = qualification.Id,
                queuePath,
                processed = candidates.Count,
                applied,
                failed,
                skipped,
                summary = queue.Summary,
                details
            });
        }

        [HttpGet("cognitive-exports")]
        public IActionResult CognitiveExports([FromQuery] int qualificationId)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var baseDir = ResolveImportsBaseDir();
            var qualificationFolder = ResolveQualificationFolder(baseDir, qualification, ensureExists: false);
            var queuePath = ResolveExistingMappingReviewQueuePath(qualificationFolder, qualification.QualificationNumber, qualification.QualificationDescription);
            if (string.IsNullOrWhiteSpace(queuePath) || !System.IO.File.Exists(queuePath))
            {
                return NotFound("No cognitive exports available yet. Run cognitive scan first.");
            }

            var queue = LoadMappingReviewQueue(queuePath);
            var exportItems = BuildCognitiveExportItems(queue, qualification.Id);

            return Ok(new
            {
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                qualificationDescription = qualification.QualificationDescription,
                queuePath,
                outputDir = queue.OutputDir,
                exports = exportItems,
                warning = "Changes to the templates are at their own risk."
            });
        }

        [HttpGet("cognitive-export-file")]
        public IActionResult CognitiveExportFile([FromQuery] int qualificationId, [FromQuery] string kind)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required.");
            if (string.IsNullOrWhiteSpace(kind)) return BadRequest("kind is required.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var baseDir = ResolveImportsBaseDir();
            var qualificationFolder = ResolveQualificationFolder(baseDir, qualification, ensureExists: false);
            var queuePath = ResolveExistingMappingReviewQueuePath(qualificationFolder, qualification.QualificationNumber, qualification.QualificationDescription);
            if (string.IsNullOrWhiteSpace(queuePath) || !System.IO.File.Exists(queuePath))
            {
                return NotFound("No cognitive export queue found. Run cognitive scan first.");
            }

            var queue = LoadMappingReviewQueue(queuePath);
            var exportPath = ResolveExportPathByKind(queue, kind);
            if (string.IsNullOrWhiteSpace(exportPath) || !System.IO.File.Exists(exportPath))
            {
                return NotFound($"Export file not found for kind '{kind}'.");
            }

            var fileName = Path.GetFileName(exportPath);
            var ext = Path.GetExtension(exportPath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".csv" => "text/csv",
                ".json" => "application/json",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };

            var stream = new FileStream(exportPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return File(stream, contentType, fileName);
        }

        [HttpPost("upload-manual-csv")]
        public IActionResult UploadManualCsv([FromForm] ManualCsvUploadRequest req)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");
            if (req.File == null || req.File.Length == 0) return BadRequest("CSV file is required.");

            var entityType = (req.EntityType ?? string.Empty).Trim().ToLowerInvariant();
            if (entityType != "phases" && entityType != "subjects" && entityType != "topics")
            {
                return BadRequest("EntityType must be one of: phases, subjects, topics.");
            }

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var ext = Path.GetExtension(req.File.FileName).ToLowerInvariant();
            if (ext != ".csv") return BadRequest("Only .csv files are supported for manual upload.");

            var baseDir = ResolveImportsBaseDir();
            var qualificationFolder = ResolveQualificationFolder(baseDir, qualification, ensureExists: true);
            var scanDir = ResolveCognitiveOutputFolder(qualificationFolder, qualification.QualificationNumber, qualification.QualificationDescription, ensureExists: true);
            var manualDir = Path.Combine(scanDir, "ManualOverrides");
            Directory.CreateDirectory(manualDir);

            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var fileName = $"{entityType}_manual_{stamp}.csv";
            var savedPath = Path.Combine(manualDir, fileName);

            using (var fs = new FileStream(savedPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                req.File.CopyTo(fs);
            }

            IActionResult importResult = entityType switch
            {
                "phases" => new CurriculumPhaseController(_context).ImportCsv(req.QualificationId, savedPath),
                "subjects" => new SubjectController(_context).ImportCsv(req.QualificationId, savedPath),
                "topics" => new TopicController(_context).ImportCsv(req.QualificationId, savedPath),
                _ => BadRequest("Unsupported entity type.")
            };

            var importStatusCode = importResult switch
            {
                ObjectResult objectResult when objectResult.StatusCode.HasValue => objectResult.StatusCode.Value,
                StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
                _ => 200
            };
            var importPayload = importResult switch
            {
                ObjectResult objectResult => objectResult.Value,
                _ => null
            };

            return StatusCode(importStatusCode, new
            {
                uploaded = importStatusCode >= 200 && importStatusCode < 300,
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                entityType,
                csvPath = savedPath,
                warning = "Changes to the templates are at their own risk.",
                import = importPayload
            });
        }

        [HttpPost("queue-automation")]
        public IActionResult QueueAutomation([FromBody] QueueAutomationRequest req)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");

            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var baseDir = ResolveImportsBaseDir();
            var dir = ResolveQualificationFolder(baseDir, qualification, ensureExists: false);
            var files = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "QC_*.*")
                    .Where(f => AllowedExt.Contains(Path.GetExtension(f)))
                    .Select(f => Path.GetFileName(f) ?? string.Empty)
                    .Where(n => n.Length > 0)
                    .ToList()
                : new List<string>();
            var hasCurriculum = files.Any(IsCurriculumFileName);
            var hasAssessment = files.Any(IsAssessmentFileName);
            if (!hasCurriculum || !hasAssessment)
            {
                return BadRequest("Automation is blocked. Upload both compulsory documents first: Curriculum Specification and Assessment Specification.");
            }

            var requiresApproval = req.RequiresApproval || req.RunSeedWrite;
            var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}".TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://localhost:5299";
            }
            var cfg = new
            {
                RunImports = true,
                req.RunSeedWrite,
                BackendBase = $"{baseUrl}/api",
                ScriptPath = @"C:\ETDP\ETDP\AzureAgent\smoke-test-agent.ps1",
                PowerShellPath = "powershell.exe",
                Trigger = "QualityCouncilCurricula"
            };

            var job = new AutomationJob
            {
                JobType = "build_qualification",
                QualificationId = req.QualificationId,
                QualificationNumber = qualification.QualificationNumber,
                Status = requiresApproval ? "PendingApproval" : "Queued",
                RequiresApproval = requiresApproval,
                RequestedBy = string.IsNullOrWhiteSpace(req.RequestedBy) ? "quality-council-page" : req.RequestedBy.Trim(),
                ConfigJson = JsonSerializer.Serialize(cfg)
            };

            _context.AutomationJobs.Add(job);
            _context.SaveChanges();

            return Ok(new
            {
                job.Id,
                job.Status,
                job.RequiresApproval,
                message = requiresApproval
                    ? "Automation queued and waiting for approval."
                    : "Automation queued."
            });
        }

        [HttpPost("reset")]
        public IActionResult Reset([FromBody] QualificationRequest req)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");
            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var baseDir = ResolveImportsBaseDir();
            var dir = ResolveQualificationFolder(baseDir, qualification, ensureExists: false);
            if (!Directory.Exists(dir))
            {
                return Ok(new
                {
                    deletedFiles = 0,
                    deletedSourceMaterials = 0,
                    folderPath = dir,
                    message = "Nothing to reset."
                });
            }

            var files = Directory.GetFiles(dir, "QC_*.*")
                .Where(f => AllowedExt.Contains(Path.GetExtension(f)))
                .ToList();
            var deletedFiles = 0;
            foreach (var file in files)
            {
                if (System.IO.File.Exists(file))
                {
                    System.IO.File.Delete(file);
                    deletedFiles++;
                }
            }

            var toDelete = _context.SourceMaterials.Where(s => files.Contains(s.FilePath)).ToList();
            _context.SourceMaterials.RemoveRange(toDelete);
            _context.SaveChanges();

            return Ok(new
            {
                deletedFiles,
                deletedSourceMaterials = toDelete.Count,
                folderPath = dir,
                message = "Quality Council files removed. You can re-upload and run automation again from a clean state."
            });
        }

        [HttpPost("run-sans-metadata-scan")]
        public async Task<IActionResult> RunSansMetadataScan([FromForm] SansMetadataScanRequest req, CancellationToken cancellationToken)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");
            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var baseDir = ResolveImportsBaseDir();
            var qualificationFolder = ResolveQualificationFolder(baseDir, qualification, ensureExists: true);
            var workingDirectory = ResolveSansOutputFolder(qualificationFolder, ensureExists: true);

            var filePaths = new List<string>();
            foreach (var file in req.Files ?? new List<IFormFile>())
            {
                if (file == null || file.Length <= 0) continue;
                var safeName = MakeSafeFileName(file.FileName, "sans-source.pdf");
                var destination = Path.Combine(workingDirectory, $"{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{safeName}");
                await using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                await file.CopyToAsync(stream, cancellationToken);
                filePaths.Add(destination);
            }

            var sourceUrls = SplitSourceUrls(req.SourceUrls);
            if (filePaths.Count == 0 && sourceUrls.Count == 0)
            {
                return BadRequest("Provide at least one SANS source URL or upload at least one Gazette/catalogue file.");
            }

            var scan = await _sansMetadataService.ScanSourcesAsync(
                _context,
                filePaths,
                sourceUrls,
                workingDirectory,
                cancellationToken);

            return Ok(new
            {
                workingDirectory,
                scan
            });
        }

        [HttpGet("sans-metadata-index")]
        public async Task<IActionResult> GetSansMetadataIndex([FromQuery] bool currentOnly = true, CancellationToken cancellationToken = default)
        {
            var index = await _sansMetadataService.GetMetadataIndexAsync(_context, currentOnly, cancellationToken);
            return Ok(index);
        }

        [HttpGet("sans-code-name-export")]
        public async Task<IActionResult> ExportSansCodeName([FromQuery] string? format = "csv", [FromQuery] bool currentOnly = true, CancellationToken cancellationToken = default)
        {
            var items = await _sansMetadataService.GetCodeNameIndexAsync(_context, currentOnly, cancellationToken);
            var normalizedFormat = (format ?? "csv").Trim().ToLowerInvariant();
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);

            if (normalizedFormat == "json")
            {
                var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
                return File(Encoding.UTF8.GetBytes(json), "application/json", $"SANS_Code_Name_{timestamp}.json");
            }

            var csv = new StringBuilder();
            csv.AppendLine("Code,Name");
            foreach (var item in items)
            {
                csv.Append(EscapeCsv(item.StandardNumber));
                csv.Append(',');
                csv.AppendLine(EscapeCsv(item.StandardName));
            }

            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", $"SANS_Code_Name_{timestamp}.csv");
        }

        [HttpPost("build-sans-mapping-review")]
        public async Task<IActionResult> BuildSansMappingReview([FromBody] QualificationRequest req, CancellationToken cancellationToken)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");
            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var reviewQueue = await _sansMetadataService.BuildMappingQueueAsync(_context, qualification.Id, cancellationToken);
            return Ok(new
            {
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                reviewQueue
            });
        }

        [HttpGet("sans-mapping-review-queue")]
        public async Task<IActionResult> GetSansMappingReviewQueue([FromQuery] int qualificationId, CancellationToken cancellationToken)
        {
            if (qualificationId <= 0) return BadRequest("qualificationId is required.");
            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var reviewQueue = await _sansMetadataService.GetMappingQueueAsync(_context, qualification.Id, cancellationToken);
            return Ok(new
            {
                qualificationId = qualification.Id,
                qualificationNumber = qualification.QualificationNumber,
                reviewQueue
            });
        }

        [HttpPost("apply-sans-mapping-review")]
        public async Task<IActionResult> ApplySansMappingReview([FromBody] ApplySansMappingReviewRequest req, CancellationToken cancellationToken)
        {
            if (req.QualificationId <= 0) return BadRequest("QualificationId is required.");
            var qualification = _context.Qualifications.FirstOrDefault(q => q.Id == req.QualificationId);
            if (qualification == null) return NotFound("Qualification not found.");

            var result = await _sansMetadataService.ApplyMappingReviewAsync(
                _context,
                qualification.Id,
                req.ItemId,
                req.ItemIds,
                req.MinConfidence,
                req.PendingOnly,
                cancellationToken);

            var queue = await _sansMetadataService.GetMappingQueueAsync(_context, qualification.Id, cancellationToken);
            return Ok(new
            {
                qualificationId = qualification.Id,
                applied = result.Applied,
                failed = result.Failed,
                processed = result.Processed,
                reviewQueue = queue
            });
        }

        private async Task<CognitiveScanExecution> ExecuteCognitiveScanAsync(Qualification qualification, int? startPage)
        {
            var baseDir = ResolveImportsBaseDir();
            var dir = ResolveQualificationFolder(baseDir, qualification, ensureExists: false);
            if (!Directory.Exists(dir))
            {
                throw new InvalidOperationException($"Qualification folder not found: {dir}");
            }

            var curriculum = Directory.GetFiles(dir, "QC_*.*")
                .Where(f => AllowedExt.Contains(Path.GetExtension(f)))
                .FirstOrDefault(f => IsCurriculumFileName(Path.GetFileName(f)));
            if (curriculum == null)
            {
                throw new InvalidOperationException("Curriculum Specification document is required before cognitive scan.");
            }

            var ext = Path.GetExtension(curriculum).ToLowerInvariant();
            var requestedStartPage = startPage.GetValueOrDefault(10);
            if (requestedStartPage < 1) requestedStartPage = 1;

            string extractedText;
            try
            {
                extractedText = await ExtractTextForCognitiveScanAsync(curriculum, ext, requestedStartPage);
                extractedText = await _ocrExtractionService.EnhanceExtractedTextAsync(curriculum, ext, extractedText);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Cognitive extraction failed: {ex.Message}");
            }

            var outputDir = ResolveCognitiveOutputFolder(
                dir,
                qualification.QualificationNumber,
                qualification.QualificationDescription);

            CognitiveScanArtifacts artifacts;
            try
            {
                artifacts = _curriculumKnowledgeScanService.GenerateArtifacts(
                    curriculum,
                    ext,
                    extractedText,
                    qualification.QualificationNumber,
                    outputDir,
                    ext == ".pdf" ? requestedStartPage : null);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Cognitive parsing failed: {ex.Message}");
            }

            return new CognitiveScanExecution
            {
                CurriculumPath = curriculum,
                OutputDir = outputDir,
                StartPageUsed = ext == ".pdf" ? requestedStartPage : null,
                Artifacts = artifacts
            };
        }

        private MappingReviewQueueDocument BuildMappingReviewQueueDocument(Qualification qualification, CognitiveScanExecution scan)
        {
            var phaseRows = ReadCsvRows(scan.Artifacts.PhasesCsvPath);
            var subjectRows = ReadCsvRows(scan.Artifacts.SubjectCsvPath);
            var topicRows = ReadCsvRows(scan.Artifacts.TopicCsvPath);

            var items = new List<MappingReviewItem>();
            var idCounter = 1;

            var phaseSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in phaseRows)
            {
                var qualificationCode = CsvField(row, "Qualification Code", "Qualification Number", "Qaulification Code");
                var learningPhases = CsvField(row, "Learning Phases", "Phase Name", "Name");
                var phasesCode = CsvField(row, "Phases Code", "Phase Code");
                var phasesDescription = CsvField(row, "Phases Description", "Description");
                var phasesPurpose = CsvField(row, "Phases Purpose");
                var sequence = ParseNullableInt(CsvField(row, "Sequence", "Order"));

                var phaseName = HasText(learningPhases) ? learningPhases : phasesCode;
                if (!HasText(phaseName)) continue;

                var dedupeKey = $"{qualification.Id}|{phaseName.Trim().ToUpperInvariant()}";
                if (!phaseSeen.Add(dedupeKey)) continue;

                var existingPhase = _context.CurriculumPhases.FirstOrDefault(p => p.Name == phaseName);
                var linked = existingPhase != null && _context.QualificationPhases.Any(qp =>
                    qp.QualificationId == qualification.Id &&
                    qp.CurriculumPhaseId == existingPhase.Id);

                var suggestedAction = "create";
                if (existingPhase != null)
                {
                    var changed =
                        !EqualsNormalized(existingPhase.Description, phasesDescription) ||
                        (sequence.HasValue && sequence.Value > 0 && existingPhase.Sequence != sequence.Value) ||
                        !linked;
                    suggestedAction = changed ? "update" : "noop";
                }

                var signals = new List<string>();
                var score = ScorePhaseReviewItem(
                    qualification.QualificationNumber,
                    qualificationCode,
                    phaseName,
                    phasesCode,
                    phasesDescription,
                    phasesPurpose,
                    sequence,
                    existingPhase != null,
                    linked,
                    signals);

                items.Add(new MappingReviewItem
                {
                    Id = $"MRI-{idCounter++:D5}",
                    EntityType = "phase",
                    Status = "pending",
                    SuggestedAction = suggestedAction,
                    ExistsInDatabase = existingPhase != null,
                    ExistingEntityId = existingPhase?.Id,
                    QualificationCode = qualificationCode,
                    LearningPhases = phaseName,
                    PhasesCode = HasText(phasesCode) ? phasesCode : phaseName,
                    PhasesDescription = phasesDescription,
                    PhasesPurpose = phasesPurpose,
                    Sequence = sequence,
                    ConfidenceScore = score,
                    ConfidenceBand = GetConfidenceBand(score),
                    Signals = signals
                });
            }

            var subjectSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in subjectRows)
            {
                var qualificationCode = CsvField(row, "Qualification Code", "Qualification Number", "Qaulification Code");
                var phasesCode = CsvField(row, "Phases Code", "Phase Code", "Learning Phases");
                var phasesDescription = CsvField(row, "Phases Description", "Description");
                var phasesPurpose = CsvField(row, "Phases Purpose", "Subject Purpose");
                var subjectCode = CsvField(row, "SubjectCode", "Subject Code", "PhasesCode");
                var subjectDescription = CsvField(row, "Subject Description");
                var subjectCredits = CsvField(row, "Subject Credits");
                var subjectNqfLevel = CsvField(row, "Subject NQF Level");
                var subjectPercentage = CsvField(row, "Subject Percentage");

                if (!HasText(subjectCode)) continue;

                var dedupeKey = $"{qualification.Id}|{subjectCode.Trim().ToUpperInvariant()}";
                if (!subjectSeen.Add(dedupeKey)) continue;

                var existingSubject = _context.Subjects.FirstOrDefault(s =>
                    s.QualificationId == qualification.Id &&
                    s.SubjectCode == subjectCode);
                if (existingSubject == null && HasText(subjectDescription))
                {
                    existingSubject = _context.Subjects.FirstOrDefault(s =>
                        s.QualificationId == qualification.Id &&
                        s.SubjectDescription == subjectDescription);
                }

                var targetCredits = ParseRoundedInt(subjectCredits);
                var targetNqf = ParseRoundedInt(subjectNqfLevel);
                var targetPct = ParseRoundedInt(subjectPercentage);

                var suggestedAction = "create";
                if (existingSubject != null)
                {
                    var changed =
                        !EqualsNormalized(existingSubject.SubjectDescription, subjectDescription) ||
                        !EqualsNormalized(existingSubject.SubjectPurpose, phasesPurpose) ||
                        existingSubject.SubjectCredits != targetCredits ||
                        existingSubject.SubjectNQFLevel != targetNqf ||
                        existingSubject.SubjectPercentage != targetPct;
                    suggestedAction = changed ? "update" : "noop";
                }

                var signals = new List<string>();
                var score = ScoreSubjectReviewItem(
                    qualification.QualificationNumber,
                    qualificationCode,
                    phasesCode,
                    subjectCode,
                    subjectDescription,
                    subjectCredits,
                    subjectNqfLevel,
                    subjectPercentage,
                    existingSubject != null,
                    signals);

                items.Add(new MappingReviewItem
                {
                    Id = $"MRI-{idCounter++:D5}",
                    EntityType = "subject",
                    Status = "pending",
                    SuggestedAction = suggestedAction,
                    ExistsInDatabase = existingSubject != null,
                    ExistingEntityId = existingSubject?.Id,
                    QualificationCode = qualificationCode,
                    LearningPhases = phasesCode,
                    PhasesCode = phasesCode,
                    PhasesDescription = phasesDescription,
                    PhasesPurpose = phasesPurpose,
                    SubjectCode = subjectCode,
                    SubjectDescription = subjectDescription,
                    SubjectCredits = subjectCredits,
                    SubjectNqfLevel = subjectNqfLevel,
                    SubjectPercentage = subjectPercentage,
                    ConfidenceScore = score,
                    ConfidenceBand = GetConfidenceBand(score),
                    Signals = signals
                });
            }

            var topicSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in topicRows)
            {
                var qualificationCode = CsvField(row, "Qualification Code", "Qualification Number", "Qaulification Code");
                var phasesCode = CsvField(row, "Phases Code", "Phase Code", "Learning Phases");
                var phasesDescription = CsvField(row, "Phases Description", "Phase Description");
                var subjectCode = CsvField(row, "Subject Code", "SubjectCode", "PhasesCode");
                var subjectDescription = CsvField(row, "Subject Description", "Subject Decription");
                var subjectCredits = CsvField(row, "Subject Credits");
                var notionalHours = CsvField(row, "Notional Hours", "National Hours");
                var periodsPerTopic = CsvField(row, "Periods per Topic", "PeriodsPerTopic", "Periods Per Topic");
                var topicCode = CsvField(row, "Topic Code");
                var topicDescription = CsvField(row, "Topic Description");
                var assessmentCriteriaNumber = CsvField(row, "Assessment Criteria Number", "Assessment Criteria Id", "Assessment Criteria Number (AC)");
                var assessmentCriteriaDescription = CsvField(row, "Assesment Criteria Description", "Assessment Criteria Description");

                if (!HasText(subjectCode) || (!HasText(topicCode) && !HasText(topicDescription))) continue;

                var dedupeKey = $"{qualification.Id}|{subjectCode.Trim().ToUpperInvariant()}|{topicCode.Trim().ToUpperInvariant()}|{NormalizeHeader(topicDescription)}";
                if (!topicSeen.Add(dedupeKey)) continue;

                Topic? existingTopic = null;
                if (HasText(topicCode))
                {
                    existingTopic = _context.Topics.FirstOrDefault(t =>
                        t.Subject != null &&
                        t.Subject.QualificationId == qualification.Id &&
                        t.Subject.SubjectCode == subjectCode &&
                        t.TopicCode == topicCode);
                }
                if (existingTopic == null && HasText(topicDescription))
                {
                    existingTopic = _context.Topics.FirstOrDefault(t =>
                        t.Subject != null &&
                        t.Subject.QualificationId == qualification.Id &&
                        t.Subject.SubjectCode == subjectCode &&
                        t.TopicDescription == topicDescription);
                }

                var existingCriteria = existingTopic == null
                    ? string.Empty
                    : _context.AssessmentCriteria
                        .Where(c => c.TopicId == existingTopic.Id)
                        .Select(c => c.Description)
                        .FirstOrDefault() ?? string.Empty;

                var suggestedAction = "create";
                if (existingTopic != null)
                {
                    var changed =
                        !EqualsNormalized(existingTopic.TopicDescription, topicDescription) ||
                        !EqualsNullableDouble(existingTopic.SubjectCredits, ParseFlexibleNumber(subjectCredits)) ||
                        !EqualsNullableDouble(existingTopic.NotionalHours, ParseFlexibleNumber(notionalHours)) ||
                        !EqualsNullableDouble(existingTopic.PeriodsPerTopic, ParseFlexibleNumber(periodsPerTopic)) ||
                        (!string.IsNullOrWhiteSpace(assessmentCriteriaDescription) &&
                         !EqualsNormalized(existingCriteria, assessmentCriteriaDescription));
                    suggestedAction = changed ? "update" : "noop";
                }

                var signals = new List<string>();
                var score = ScoreTopicReviewItem(
                    qualification.QualificationNumber,
                    qualificationCode,
                    subjectCode,
                    topicCode,
                    topicDescription,
                    assessmentCriteriaDescription,
                    notionalHours,
                    periodsPerTopic,
                    existingTopic != null,
                    signals);

                items.Add(new MappingReviewItem
                {
                    Id = $"MRI-{idCounter++:D5}",
                    EntityType = "topic",
                    Status = "pending",
                    SuggestedAction = suggestedAction,
                    ExistsInDatabase = existingTopic != null,
                    ExistingEntityId = existingTopic?.Id,
                    QualificationCode = qualificationCode,
                    LearningPhases = phasesCode,
                    PhasesCode = phasesCode,
                    PhasesDescription = phasesDescription,
                    SubjectCode = subjectCode,
                    SubjectDescription = subjectDescription,
                    SubjectCredits = subjectCredits,
                    NotionalHours = notionalHours,
                    PeriodsPerTopic = periodsPerTopic,
                    TopicCode = topicCode,
                    TopicDescription = topicDescription,
                    AssessmentCriteriaNumber = assessmentCriteriaNumber,
                    AssessmentCriteriaDescription = assessmentCriteriaDescription,
                    ConfidenceScore = score,
                    ConfidenceBand = GetConfidenceBand(score),
                    Signals = signals
                });
            }

            var sortedItems = items
                .OrderByDescending(i => i.ConfidenceScore)
                .ThenBy(i => i.EntityType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var queue = new MappingReviewQueueDocument
            {
                QualificationId = qualification.Id,
                QualificationNumber = qualification.QualificationNumber,
                QualificationDescription = qualification.QualificationDescription,
                CurriculumPath = scan.CurriculumPath,
                OutputDir = scan.OutputDir,
                StartPageUsed = scan.StartPageUsed,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Scan = new MappingReviewScanSnapshot
                {
                    ModuleCount = scan.Artifacts.ModuleCount,
                    CurriculumPhaseCount = scan.Artifacts.CurriculumPhaseCount,
                    KnowledgeSubjectCount = scan.Artifacts.KnowledgeSubjectCount,
                    TopicCount = scan.Artifacts.TopicCount,
                    Warnings = scan.Artifacts.Warnings,
                    ExtractTextPath = scan.Artifacts.ExtractTextPath,
                    PhasesCsvPath = scan.Artifacts.PhasesCsvPath,
                    SubjectCsvPath = scan.Artifacts.SubjectCsvPath,
                    TopicCsvPath = scan.Artifacts.TopicCsvPath,
                    ReportJsonPath = scan.Artifacts.ReportJsonPath
                },
                Items = sortedItems
            };

            queue.Summary = BuildQueueSummary(queue.Items);
            return queue;
        }

        private static List<Dictionary<string, string>> ReadCsvRows(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                return new List<Dictionary<string, string>>();
            }

            var rows = Csv.ReadSemicolonCsv(path);
            if (rows.Count <= 1) return new List<Dictionary<string, string>>();

            var header = rows[0];
            var keys = header.Select(NormalizeHeader).ToArray();
            var output = new List<Dictionary<string, string>>();

            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Length == 0 || row.All(string.IsNullOrWhiteSpace)) continue;

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < keys.Length; c++)
                {
                    var key = keys[c];
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    var value = c < row.Length ? (row[c] ?? string.Empty).Trim() : string.Empty;
                    map[key] = value;
                }
                output.Add(map);
            }

            return output;
        }

        private static string CsvField(Dictionary<string, string> row, params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var key = NormalizeHeader(alias);
                if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            return string.Empty;
        }

        private object ApplyMappingReviewItem(Qualification qualification, MappingReviewItem item)
        {
            var entityType = (item.EntityType ?? string.Empty).Trim().ToLowerInvariant();
            return entityType switch
            {
                "phase" => ApplyPhaseReviewItem(qualification, item),
                "subject" => ApplySubjectReviewItem(qualification, item),
                "topic" => ApplyTopicReviewItem(qualification, item),
                _ => throw new InvalidOperationException($"Unsupported review item entity type '{item.EntityType}'.")
            };
        }

        private object ApplyPhaseReviewItem(Qualification qualification, MappingReviewItem item)
        {
            var phase = EnsurePhaseFromReviewItem(qualification, item, out var phaseAction, out var linkAction);
            return new
            {
                phaseId = phase.Id,
                phaseName = phase.Name,
                phaseAction,
                linkAction
            };
        }

        private object ApplySubjectReviewItem(Qualification qualification, MappingReviewItem item)
        {
            var subject = EnsureSubjectFromReviewItem(qualification, item, out var subjectAction, out var phaseAction, out var linkAction);
            return new
            {
                subjectId = subject.Id,
                subjectCode = subject.SubjectCode,
                subjectAction,
                phaseAction,
                linkAction
            };
        }

        private object ApplyTopicReviewItem(Qualification qualification, MappingReviewItem item)
        {
            var subject = EnsureSubjectFromReviewItem(qualification, item, out var subjectAction, out var phaseAction, out var linkAction);

            var topicCode = item.TopicCode?.Trim() ?? string.Empty;
            var topicDescription = item.TopicDescription?.Trim() ?? string.Empty;
            if (!HasText(topicCode) && !HasText(topicDescription))
            {
                throw new InvalidOperationException("Topic code/description is missing.");
            }

            Topic? topic = null;
            if (HasText(topicCode))
            {
                topic = _context.Topics.FirstOrDefault(t => t.SubjectId == subject.Id && t.TopicCode == topicCode);
            }
            if (topic == null && HasText(topicDescription))
            {
                topic = _context.Topics.FirstOrDefault(t => t.SubjectId == subject.Id && t.TopicDescription == topicDescription);
            }

            var topicAction = "noop";
            var isCreate = topic == null;
            if (topic == null)
            {
                topic = new Topic
                {
                    SubjectId = subject.Id
                };
                _context.Topics.Add(topic);
                topicAction = "created";
            }

            var targetSubjectCredits = ParseFlexibleNumber(item.SubjectCredits);
            var targetNotionalHours = ParseFlexibleNumber(item.NotionalHours);
            var targetPeriods = ParseFlexibleNumber(item.PeriodsPerTopic);
            var targetPurpose = item.PhasesDescription?.Trim() ?? string.Empty;

            var changed = false;
            if (!EqualsNormalized(topic.TopicPurpose, targetPurpose) && HasText(targetPurpose))
            {
                topic.TopicPurpose = targetPurpose;
                changed = true;
            }
            if (!EqualsNormalized(topic.TopicCode, topicCode) && HasText(topicCode))
            {
                topic.TopicCode = topicCode;
                changed = true;
            }
            if (!EqualsNormalized(topic.TopicDescription, topicDescription) && HasText(topicDescription))
            {
                topic.TopicDescription = topicDescription;
                changed = true;
            }
            if (!EqualsNullableDouble(topic.SubjectCredits, targetSubjectCredits))
            {
                topic.SubjectCredits = targetSubjectCredits;
                changed = true;
            }
            if (!EqualsNullableDouble(topic.NotionalHours, targetNotionalHours))
            {
                topic.NotionalHours = targetNotionalHours;
                changed = true;
            }
            if (!EqualsNullableDouble(topic.PeriodsPerTopic, targetPeriods))
            {
                topic.PeriodsPerTopic = targetPeriods;
                topic.PeriodsPerTopicManualOverride = targetPeriods.HasValue && targetPeriods.Value > 0;
                changed = true;
            }

            if (isCreate || changed)
            {
                if (!isCreate && topicAction == "noop") topicAction = "updated";
                _context.SaveChanges();
            }

            var criteriaDescription = HasText(item.AssessmentCriteriaDescription)
                ? item.AssessmentCriteriaDescription.Trim()
                : item.AssessmentCriteriaNumber?.Trim() ?? string.Empty;

            var criteriaAction = "none";
            int? criteriaId = null;
            if (HasText(criteriaDescription))
            {
                var criteria = _context.AssessmentCriteria
                    .FirstOrDefault(c => c.TopicId == topic.Id && c.Description == criteriaDescription);
                if (criteria == null)
                {
                    criteria = new AssessmentCriteria
                    {
                        TopicId = topic.Id,
                        Description = criteriaDescription,
                        CriteriaType = "Topic",
                        Weight = 1.0
                    };
                    _context.AssessmentCriteria.Add(criteria);
                    _context.SaveChanges();
                    criteriaAction = "created";
                }
                else
                {
                    criteriaAction = "noop";
                }
                criteriaId = criteria.Id;
            }

            return new
            {
                topicId = topic.Id,
                topicCode = topic.TopicCode,
                topicAction,
                subjectId = subject.Id,
                subjectCode = subject.SubjectCode,
                subjectAction,
                phaseAction,
                linkAction,
                criteriaId,
                criteriaAction
            };
        }

        private CurriculumPhase EnsurePhaseFromReviewItem(Qualification qualification, MappingReviewItem item, out string phaseAction, out string linkAction)
        {
            var phaseName = HasText(item.LearningPhases) ? item.LearningPhases.Trim() : item.PhasesCode?.Trim() ?? string.Empty;
            if (!HasText(phaseName))
            {
                throw new InvalidOperationException("Phase code/name is missing.");
            }

            var phaseDescription = item.PhasesDescription?.Trim() ?? string.Empty;
            var sequence = item.Sequence.GetValueOrDefault();
            if (sequence <= 0) sequence = 1;

            var phase = _context.CurriculumPhases.FirstOrDefault(p => p.Name == phaseName);
            phaseAction = "noop";

            if (phase == null)
            {
                phase = new CurriculumPhase
                {
                    Name = phaseName,
                    Description = phaseDescription,
                    Sequence = sequence
                };
                _context.CurriculumPhases.Add(phase);
                _context.SaveChanges();
                phaseAction = "created";
            }
            else
            {
                var changed = false;
                if (HasText(phaseDescription) && !EqualsNormalized(phase.Description, phaseDescription))
                {
                    phase.Description = phaseDescription;
                    changed = true;
                }
                if (item.Sequence.HasValue && item.Sequence.Value > 0 && phase.Sequence != item.Sequence.Value)
                {
                    phase.Sequence = item.Sequence.Value;
                    changed = true;
                }

                if (changed)
                {
                    _context.SaveChanges();
                    phaseAction = "updated";
                }
            }

            var linkExists = _context.QualificationPhases.Any(qp =>
                qp.QualificationId == qualification.Id &&
                qp.CurriculumPhaseId == phase.Id);
            linkAction = "noop";
            if (!linkExists)
            {
                _context.QualificationPhases.Add(new QualificationPhase
                {
                    QualificationId = qualification.Id,
                    CurriculumPhaseId = phase.Id
                });
                _context.SaveChanges();
                linkAction = "linked";
            }

            return phase;
        }

        private Subject EnsureSubjectFromReviewItem(
            Qualification qualification,
            MappingReviewItem item,
            out string subjectAction,
            out string phaseAction,
            out string linkAction)
        {
            var subjectCode = item.SubjectCode?.Trim() ?? string.Empty;
            if (!HasText(subjectCode))
            {
                throw new InvalidOperationException("Subject code is missing.");
            }

            var phase = EnsurePhaseFromReviewItem(qualification, item, out phaseAction, out linkAction);

            var subjectDescription = item.SubjectDescription?.Trim() ?? subjectCode;
            var subjectPurpose = item.PhasesPurpose?.Trim() ?? string.Empty;
            var targetCredits = ParseRoundedInt(item.SubjectCredits);
            var targetNqf = ParseRoundedInt(item.SubjectNqfLevel);
            var targetPct = ParseRoundedInt(item.SubjectPercentage);

            var subject = _context.Subjects.FirstOrDefault(s =>
                s.QualificationId == qualification.Id &&
                s.SubjectCode == subjectCode);
            if (subject == null && HasText(subjectDescription))
            {
                subject = _context.Subjects.FirstOrDefault(s =>
                    s.QualificationId == qualification.Id &&
                    s.SubjectDescription == subjectDescription);
            }

            subjectAction = "noop";
            if (subject == null)
            {
                subject = new Subject
                {
                    QualificationId = qualification.Id,
                    CurriculumPhaseId = phase.Id,
                    SubjectCode = subjectCode,
                    SubjectDescription = subjectDescription,
                    SubjectPurpose = subjectPurpose,
                    SubjectCredits = targetCredits,
                    SubjectNQFLevel = targetNqf,
                    SubjectPercentage = targetPct
                };
                _context.Subjects.Add(subject);
                _context.SaveChanges();
                subjectAction = "created";
            }
            else
            {
                var changed = false;
                if (!EqualsNormalized(subject.SubjectDescription, subjectDescription) && HasText(subjectDescription))
                {
                    subject.SubjectDescription = subjectDescription;
                    changed = true;
                }
                if (!EqualsNormalized(subject.SubjectPurpose, subjectPurpose) && HasText(subjectPurpose))
                {
                    subject.SubjectPurpose = subjectPurpose;
                    changed = true;
                }
                if (subject.SubjectCredits != targetCredits)
                {
                    subject.SubjectCredits = targetCredits;
                    changed = true;
                }
                if (subject.SubjectNQFLevel != targetNqf)
                {
                    subject.SubjectNQFLevel = targetNqf;
                    changed = true;
                }
                if (subject.SubjectPercentage != targetPct)
                {
                    subject.SubjectPercentage = targetPct;
                    changed = true;
                }
                if (subject.CurriculumPhaseId != phase.Id)
                {
                    subject.CurriculumPhaseId = phase.Id;
                    changed = true;
                }

                if (changed)
                {
                    _context.SaveChanges();
                    subjectAction = "updated";
                }
            }

            return subject;
        }

        private static MappingReviewQueueSummary BuildQueueSummary(List<MappingReviewItem> items)
        {
            var list = items ?? new List<MappingReviewItem>();
            return new MappingReviewQueueSummary
            {
                Total = list.Count,
                Pending = list.Count(i => string.Equals(i.Status, "pending", StringComparison.OrdinalIgnoreCase)),
                Applied = list.Count(i => string.Equals(i.Status, "applied", StringComparison.OrdinalIgnoreCase)),
                Failed = list.Count(i => string.Equals(i.Status, "failed", StringComparison.OrdinalIgnoreCase)),
                HighConfidence = list.Count(i => string.Equals(i.ConfidenceBand, "high", StringComparison.OrdinalIgnoreCase)),
                MediumConfidence = list.Count(i => string.Equals(i.ConfidenceBand, "medium", StringComparison.OrdinalIgnoreCase)),
                LowConfidence = list.Count(i => string.Equals(i.ConfidenceBand, "low", StringComparison.OrdinalIgnoreCase)),
                PhaseCount = list.Count(i => string.Equals(i.EntityType, "phase", StringComparison.OrdinalIgnoreCase)),
                SubjectCount = list.Count(i => string.Equals(i.EntityType, "subject", StringComparison.OrdinalIgnoreCase)),
                TopicCount = list.Count(i => string.Equals(i.EntityType, "topic", StringComparison.OrdinalIgnoreCase))
            };
        }

        private static double ScorePhaseReviewItem(
            string expectedQualificationCode,
            string qualificationCode,
            string phaseName,
            string phasesCode,
            string phasesDescription,
            string phasesPurpose,
            int? sequence,
            bool existsInDb,
            bool linkedToQualification,
            List<string> signals)
        {
            var score = 35d;

            if (!HasText(qualificationCode) || string.Equals(qualificationCode, expectedQualificationCode, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
                signals.Add("Qualification code aligned.");
            }
            else
            {
                score -= 6;
                signals.Add("Qualification code differs from selected qualification.");
            }

            var phaseToken = HasText(phasesCode) ? phasesCode : phaseName;
            if (PhaseCodeRegex.IsMatch(phaseToken ?? string.Empty))
            {
                score += 18;
                signals.Add("Phase code matches expected curriculum module pattern.");
            }
            else if (Regex.IsMatch(phaseToken ?? string.Empty, @"\b(?:KM|PM|WM)-\d{2}\b", RegexOptions.IgnoreCase))
            {
                score += 10;
                signals.Add("Phase code contains a recognized curriculum module token.");
            }
            else
            {
                score -= 6;
                signals.Add("Phase code pattern is weak.");
            }

            var descLen = (phasesDescription ?? string.Empty).Trim().Length;
            if (descLen >= 20) score += 14;
            else if (descLen >= 8) score += 7;
            else score -= 8;

            if ((phasesPurpose ?? string.Empty).Trim().Length >= 20) score += 7;
            if (sequence.HasValue && sequence.Value > 0) score += 4;

            if (existsInDb)
            {
                score += 6;
                signals.Add("Phase already exists and can be updated safely.");
            }

            if (linkedToQualification)
            {
                score += 3;
            }

            return ClampScore(score);
        }

        private static double ScoreSubjectReviewItem(
            string expectedQualificationCode,
            string qualificationCode,
            string phasesCode,
            string subjectCode,
            string subjectDescription,
            string subjectCredits,
            string subjectNqfLevel,
            string subjectPercentage,
            bool existsInDb,
            List<string> signals)
        {
            var score = 30d;

            if (!HasText(qualificationCode) || string.Equals(qualificationCode, expectedQualificationCode, StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }
            else
            {
                score -= 6;
                signals.Add("Qualification code differs from selected qualification.");
            }

            if (SubjectCodeRegex.IsMatch(subjectCode ?? string.Empty))
            {
                score += 22;
                signals.Add("Subject code matches a recognized curriculum subject pattern.");
            }
            else if (HasText(subjectCode))
            {
                score += 8;
                signals.Add("Subject code present but pattern is non-standard.");
            }
            else
            {
                score -= 12;
            }

            var descLen = (subjectDescription ?? string.Empty).Trim().Length;
            if (descLen >= 20) score += 12;
            else if (descLen >= 8) score += 6;
            else score -= 8;

            var credits = ParseFlexibleNumber(subjectCredits);
            if (credits.HasValue && credits.Value > 0) score += 8;
            else score -= 4;

            var nqf = ParseFlexibleNumber(subjectNqfLevel);
            if (nqf.HasValue && nqf.Value >= 1 && nqf.Value <= 10) score += 5;

            var pct = ParseFlexibleNumber(subjectPercentage);
            if (pct.HasValue && pct.Value > 0 && pct.Value <= 100) score += 8;

            if (HasText(phasesCode)) score += 6;

            if (existsInDb)
            {
                score += 5;
                signals.Add("Subject already exists and can be updated.");
            }

            return ClampScore(score);
        }

        private static double ScoreTopicReviewItem(
            string expectedQualificationCode,
            string qualificationCode,
            string subjectCode,
            string topicCode,
            string topicDescription,
            string assessmentCriteriaDescription,
            string notionalHours,
            string periodsPerTopic,
            bool existsInDb,
            List<string> signals)
        {
            var score = 28d;

            if (!HasText(qualificationCode) || string.Equals(qualificationCode, expectedQualificationCode, StringComparison.OrdinalIgnoreCase))
            {
                score += 8;
            }
            else
            {
                score -= 6;
                signals.Add("Qualification code differs from selected qualification.");
            }

            if (SubjectCodeRegex.IsMatch(subjectCode ?? string.Empty))
            {
                score += 8;
            }
            else if (HasText(subjectCode))
            {
                score += 3;
            }
            else
            {
                score -= 10;
            }

            if (TopicCodeRegex.IsMatch(topicCode ?? string.Empty))
            {
                score += 22;
                signals.Add("Topic code matches expected pattern.");
            }
            else if (HasText(topicCode))
            {
                score += 8;
                signals.Add("Topic code present but non-standard.");
            }
            else
            {
                score -= 8;
            }

            var descLen = (topicDescription ?? string.Empty).Trim().Length;
            if (descLen >= 20) score += 12;
            else if (descLen >= 8) score += 6;
            else score -= 10;

            var criteriaLen = (assessmentCriteriaDescription ?? string.Empty).Trim().Length;
            if (criteriaLen >= 12) score += 10;
            else if (criteriaLen >= 4) score += 4;
            else score -= 4;

            var hours = ParseFlexibleNumber(notionalHours);
            if (hours.HasValue && hours.Value > 0) score += 5;

            var periods = ParseFlexibleNumber(periodsPerTopic);
            if (periods.HasValue && periods.Value > 0) score += 4;

            if (existsInDb)
            {
                score += 4;
                signals.Add("Topic already exists and can be updated.");
            }

            return ClampScore(score);
        }

        private static string ResolveMappingReviewQueuePath(string outputDir)
            => Path.Combine(outputDir, "MappingReviewQueue.json");

        private List<object> BuildCognitiveExportItems(MappingReviewQueueDocument queue, int qualificationId)
        {
            var items = new List<object>();

            void Add(string kind, string path)
            {
                var exists = !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);
                items.Add(new
                {
                    kind,
                    exists,
                    path,
                    fileName = exists ? Path.GetFileName(path) : string.Empty,
                    downloadUrl = exists
                        ? $"/api/QualityCouncilCurricula/cognitive-export-file?qualificationId={qualificationId}&kind={Uri.EscapeDataString(kind)}"
                        : string.Empty
                });
            }

            Add("phases", queue.Scan.PhasesCsvPath);
            Add("subjects", queue.Scan.SubjectCsvPath);
            Add("topics", queue.Scan.TopicCsvPath);
            Add("extract", queue.Scan.ExtractTextPath);
            Add("report", queue.Scan.ReportJsonPath);
            Add("queue", ResolveMappingReviewQueuePath(queue.OutputDir));

            return items;
        }

        private static string ResolveExportPathByKind(MappingReviewQueueDocument queue, string kindRaw)
        {
            var kind = (kindRaw ?? string.Empty).Trim().ToLowerInvariant();
            return kind switch
            {
                "phases" => queue.Scan.PhasesCsvPath,
                "subjects" => queue.Scan.SubjectCsvPath,
                "topics" => queue.Scan.TopicCsvPath,
                "extract" => queue.Scan.ExtractTextPath,
                "report" => queue.Scan.ReportJsonPath,
                "queue" => ResolveMappingReviewQueuePath(queue.OutputDir),
                _ => string.Empty
            };
        }

        private static string? ResolveExistingMappingReviewQueuePath(string qualificationFolder, string qualificationNumber, string qualificationDescription)
        {
            var preferredDir = ResolveCognitiveOutputFolder(qualificationFolder, qualificationNumber, qualificationDescription, ensureExists: false);
            var preferred = Path.Combine(preferredDir, "MappingReviewQueue.json");
            if (System.IO.File.Exists(preferred)) return preferred;

            var scanRoot = Path.Combine(qualificationFolder, "CognitiveScan");
            if (!Directory.Exists(scanRoot)) return null;

            return Directory.GetFiles(scanRoot, "MappingReviewQueue.json", SearchOption.AllDirectories)
                .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static MappingReviewQueueDocument LoadMappingReviewQueue(string path)
        {
            var json = System.IO.File.ReadAllText(path);
            var queue = JsonSerializer.Deserialize<MappingReviewQueueDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (queue == null)
            {
                throw new InvalidOperationException($"Unable to parse mapping review queue at {path}.");
            }
            queue.Items ??= new List<MappingReviewItem>();
            queue.Scan ??= new MappingReviewScanSnapshot();
            queue.Summary ??= new MappingReviewQueueSummary();
            return queue;
        }

        private static void SaveMappingReviewQueue(string path, MappingReviewQueueDocument queue)
        {
            queue.Items ??= new List<MappingReviewItem>();
            queue.Scan ??= new MappingReviewScanSnapshot();
            queue.Summary ??= BuildQueueSummary(queue.Items);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(queue, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            System.IO.File.WriteAllText(path, json);
        }

        private static double ClampScore(double score)
        {
            if (score < 0) return 0;
            if (score > 100) return 100;
            return Math.Round(score, 2, MidpointRounding.AwayFromZero);
        }

        private static string GetConfidenceBand(double score)
        {
            if (score >= 85) return "high";
            if (score >= 70) return "medium";
            return "low";
        }

        private static bool HasText(string? value)
            => !string.IsNullOrWhiteSpace(value);

        private static bool EqualsNormalized(string? left, string? right)
        {
            var a = Regex.Replace(left ?? string.Empty, @"\s+", " ").Trim();
            var b = Regex.Replace(right ?? string.Empty, @"\s+", " ").Trim();
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EqualsNullableDouble(double? left, double? right)
        {
            if (!left.HasValue && !right.HasValue) return true;
            if (!left.HasValue || !right.HasValue) return false;
            return Math.Abs(left.Value - right.Value) < 0.0001;
        }

        private static int? ParseNullableInt(string? raw)
        {
            var number = ParseFlexibleNumber(raw);
            if (!number.HasValue) return null;
            return (int)Math.Round(number.Value, MidpointRounding.AwayFromZero);
        }

        private static int? ParseRoundedInt(string? raw)
        {
            var number = ParseFlexibleNumber(raw);
            if (!number.HasValue) return null;
            return (int)Math.Round(number.Value, MidpointRounding.AwayFromZero);
        }

        private static double? ParseFlexibleNumber(string? raw)
        {
            var txt = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(txt)) return null;

            txt = txt.Replace("%", string.Empty).Trim();
            txt = txt.Replace(" ", string.Empty);

            if (double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out var direct))
            {
                return direct;
            }

            if (double.TryParse(txt.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var dotted))
            {
                return dotted;
            }

            if (double.TryParse(txt.Replace(".", ","), NumberStyles.Float, CultureInfo.GetCultureInfo("fr-FR"), out var comma))
            {
                return comma;
            }

            return null;
        }

        private static string NormalizeHeader(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        private Qualification? ResolveQualification(int? qualificationId, string? qualificationCode, string? qualificationDescription)
        {
            Qualification? qualification = null;
            if (qualificationId.HasValue && qualificationId.Value > 0)
            {
                qualification = _context.Qualifications.FirstOrDefault(q => q.Id == qualificationId.Value);
            }

            var code = (qualificationCode ?? string.Empty).Trim();
            if (qualification == null && !string.IsNullOrWhiteSpace(code))
            {
                qualification = _context.Qualifications.FirstOrDefault(q => q.QualificationNumber == code);
            }

            var description = (qualificationDescription ?? string.Empty).Trim();
            if (qualification == null && !string.IsNullOrWhiteSpace(description))
            {
                qualification = _context.Qualifications.FirstOrDefault(q => q.QualificationDescription == description);
            }

            return qualification;
        }

        private KnowledgeHierarchyService.StructureInfo EnsureQualificationLibraryStructure(Qualification qualification)
        {
            var qualificationCode = string.IsNullOrWhiteSpace(qualification.QualificationNumber)
                ? qualification.Id.ToString(CultureInfo.InvariantCulture)
                : qualification.QualificationNumber.Trim();
            var qualificationDescription = string.IsNullOrWhiteSpace(qualification.QualificationDescription)
                ? qualificationCode
                : qualification.QualificationDescription.Trim();
            return _knowledgeHierarchyService.EnsureQualificationStructure(qualificationCode, qualificationDescription);
        }

                        private static string MirrorSpecificationToCurriculumLibrary(
            KnowledgeHierarchyService.StructureInfo structure,
            string sourcePath,
            string docType)
        {
            var ext = Path.GetExtension(sourcePath);
            var prefix = string.Equals(docType, "assessment", StringComparison.OrdinalIgnoreCase)
                ? "QC_AssessmentSpecification"
                : "QC_CurriculumSpecification";
            var destinationPath = Path.Combine(structure.CurriculumLibraryPath, $"{prefix}{ext}");

            var fullSource = Path.GetFullPath(sourcePath);
            var fullDest = Path.GetFullPath(destinationPath);

            // If source and destination are the same file, we have already written it.
            if (string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
            {
                return destinationPath;
            }

            // If destination folder is same as source folder, we must be careful not to delete the source.
            var sourceDir = Path.GetDirectoryName(fullSource);
            var destDir = Path.GetDirectoryName(fullDest);

            if (Directory.Exists(structure.CurriculumLibraryPath))
            {
                foreach (var existing in Directory.GetFiles(structure.CurriculumLibraryPath, $"{prefix}.*"))
                {
                    var fullExisting = Path.GetFullPath(existing);
                    if (!string.Equals(fullExisting, fullSource, StringComparison.OrdinalIgnoreCase))
                    {
                        System.IO.File.Delete(existing);
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(structure.CurriculumLibraryPath);
            }

            if (!string.Equals(fullSource, fullDest, StringComparison.OrdinalIgnoreCase))
            {
                GitLfsPointerResolver.CopyResolvedContent(sourcePath, destinationPath);
            }

            return destinationPath;
        }

        private string ResolveSharedQctoLibraryRootPath()
        {
            return ResolveImportsBaseDir();
        }

        private object BuildSharedQctoLibraryCatalog(int? qualificationId, string? qualificationCode, string? qualificationDescription)
        {
            var libraryRoot = ResolveSharedQctoLibraryRootPath();
            var normalizedCode = (qualificationCode ?? string.Empty).Trim();
            var normalizedDescription = (qualificationDescription ?? string.Empty).Trim();
            var qualificationByCode = _context.Qualifications
                .ToList()
                .Where(q => !string.IsNullOrWhiteSpace(q.QualificationNumber))
                .GroupBy(q => q.QualificationNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var rows = new List<(string qualificationCode, string qualificationDescription, string docType, string fileName, string sourcePath, string relativePath, string sourceArea, DateTime lastWriteTimeUtc)>();
            if (Directory.Exists(libraryRoot))
            {
                foreach (var qualificationFolder in Directory.EnumerateDirectories(libraryRoot, "*", SearchOption.TopDirectoryOnly))
                {
                    var code = Path.GetFileName(qualificationFolder) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(normalizedCode) &&
                        !string.Equals(code, normalizedCode, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    qualificationByCode.TryGetValue(code, out var qualification);
                    var description = qualification?.QualificationDescription ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(description) &&
                        !string.IsNullOrWhiteSpace(normalizedDescription) &&
                        (string.IsNullOrWhiteSpace(normalizedCode) || string.Equals(code, normalizedCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        description = normalizedDescription;
                    }

                    if (!string.IsNullOrWhiteSpace(normalizedDescription) &&
                        !string.IsNullOrWhiteSpace(description) &&
                        !string.Equals(description, normalizedDescription, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (var path in Directory.EnumerateFiles(qualificationFolder, "QC_*.*", SearchOption.TopDirectoryOnly))
                    {
                        var ext = Path.GetExtension(path);
                        if (!AllowedExt.Contains(ext))
                        {
                            continue;
                        }

                        var fileName = Path.GetFileName(path) ?? string.Empty;
                        var docType = IsAssessmentFileName(fileName)
                            ? "assessment"
                            : IsCurriculumFileName(fileName)
                                ? "curriculum"
                                : string.Empty;
                        if (string.IsNullOrWhiteSpace(docType))
                        {
                            continue;
                        }

                        rows.Add((
                            qualificationCode: code,
                            qualificationDescription: description,
                            docType,
                            fileName,
                            sourcePath: path,
                            relativePath: Path.GetRelativePath(libraryRoot, path),
                            sourceArea: "qualification-folder",
                            lastWriteTimeUtc: System.IO.File.GetLastWriteTimeUtc(path)));
                    }
                }

                foreach (var path in Directory.EnumerateFiles(libraryRoot, "QCTO_*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(path);
                    if (!AllowedExt.Contains(ext))
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(path) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        continue;
                    }

                    var match = SharedQctoCodeRegex.Match(fileName);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var code = match.Groups["code"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(normalizedCode) &&
                        !string.Equals(code, normalizedCode, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    qualificationByCode.TryGetValue(code, out var qualification);
                    var description = qualification?.QualificationDescription ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(description) &&
                        !string.IsNullOrWhiteSpace(normalizedDescription) &&
                        (string.IsNullOrWhiteSpace(normalizedCode) || string.Equals(code, normalizedCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        description = normalizedDescription;
                    }

                    if (!string.IsNullOrWhiteSpace(normalizedDescription) &&
                        !string.IsNullOrWhiteSpace(description) &&
                        !string.Equals(description, normalizedDescription, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var lowerName = fileName.ToLowerInvariant();
                    var docType = lowerName.Contains("assessment", StringComparison.Ordinal)
                        ? "assessment"
                        : (lowerName.Contains("curric", StringComparison.Ordinal) ? "curriculum" : "other");
                    var relativePath = Path.GetRelativePath(libraryRoot, path);
                    var sourceArea = relativePath.Replace('/', '\\').Contains(@"\archive\", StringComparison.OrdinalIgnoreCase)
                        ? "archive"
                        : "shared-library";

                    rows.Add((
                        qualificationCode: code,
                        qualificationDescription: description,
                        docType,
                        fileName,
                        sourcePath: path,
                        relativePath,
                        sourceArea,
                        lastWriteTimeUtc: System.IO.File.GetLastWriteTimeUtc(path)));
                }
            }

            var grouped = rows
                .GroupBy(x => x.qualificationCode, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    qualificationByCode.TryGetValue(g.Key, out var qualification);
                    var effectiveQualificationId = qualification?.Id ?? qualificationId;
                    var effectiveDescription = g.Select(x => x.qualificationDescription).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                        ?? qualification?.QualificationDescription
                        ?? string.Empty;

                    return new
                    {
                        qualificationId = effectiveQualificationId,
                        qualificationCode = g.Key,
                        qualificationDescription = effectiveDescription,
                        totalFiles = g.Count(),
                        curriculumCandidates = g.Count(x => x.docType == "curriculum"),
                        assessmentCandidates = g.Count(x => x.docType == "assessment"),
                        otherCandidates = g.Count(x => x.docType == "other"),
                        entries = g
                            .OrderBy(x => x.docType == "curriculum" ? 0 : x.docType == "assessment" ? 1 : 2)
                            .ThenByDescending(x => x.lastWriteTimeUtc)
                            .Select(x => new
                            {
                                x.docType,
                                x.fileName,
                                x.sourcePath,
                                x.relativePath,
                                x.sourceArea,
                                lastWriteTimeUtc = x.lastWriteTimeUtc
                            })
                            .ToList()
                    };
                })
                .ToList();

            return grouped;
        }

        private static string ResolveImportsBaseDir()
        {
            return EtdpPaths.GetImportsRoot();
        }

                                private static string ResolveQualificationFolder(string baseDir, Qualification qualification, bool ensureExists)
        {
            var code = string.IsNullOrWhiteSpace(qualification.QualificationNumber)
                ? $"Qualification_{qualification.Id}"
                : qualification.QualificationNumber.Trim();
            var safeFolder = Regex.Replace(code, @"[^\w\- ]+", "").Trim().Replace(" ", "_");
            var dir = Path.Combine(baseDir, safeFolder);
            if (ensureExists) Directory.CreateDirectory(dir);
            return dir;
        }

        private static bool IsCurriculumFileName(string? fileName)
            => string.Equals(Path.GetFileNameWithoutExtension(fileName ?? string.Empty), "QC_CurriculumSpecification", StringComparison.OrdinalIgnoreCase);

        private static bool IsAssessmentFileName(string? fileName)
            => string.Equals(Path.GetFileNameWithoutExtension(fileName ?? string.Empty), "QC_AssessmentSpecification", StringComparison.OrdinalIgnoreCase);

        private static string ResolveCognitiveOutputFolder(string qualificationFolder, string qualificationNumber, string qualificationDescription, bool ensureExists = true)
        {
            var safeNumber = Regex.Replace(qualificationNumber ?? string.Empty, @"[^\w\- ]+", "").Trim().Replace(" ", "_");
            var safeDescription = Regex.Replace(qualificationDescription ?? string.Empty, @"[^\w\- ]+", "").Trim().Replace(" ", "_");
            if (string.IsNullOrWhiteSpace(safeDescription))
            {
                safeDescription = "Curriculum";
            }

            var tag = $"{safeNumber}_{safeDescription}".Trim('_');
            var outputDir = Path.Combine(qualificationFolder, "CognitiveScan", tag);
            if (ensureExists) Directory.CreateDirectory(outputDir);
            return outputDir;
        }

        private static string ResolveSansOutputFolder(string qualificationFolder, bool ensureExists)
        {
            var outputDir = Path.Combine(qualificationFolder, "StandardsCompliance", "Sources");
            if (ensureExists) Directory.CreateDirectory(outputDir);
            return outputDir;
        }

        private static List<string> SplitSourceUrls(string? raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string EscapeCsv(string? value)
        {
            var text = value ?? string.Empty;
            if (text.Contains('"'))
            {
                text = text.Replace("\"", "\"\"");
            }

            if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
            {
                return $"\"{text}\"";
            }

            return text;
        }

        private static string MakeSafeFileName(string? rawName, string fallback)
        {
            var name = string.IsNullOrWhiteSpace(rawName) ? fallback : rawName.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            if (string.IsNullOrWhiteSpace(Path.GetExtension(name))) name += Path.GetExtension(fallback);
            return name;
        }

        private static async Task<string> ExtractTextForCognitiveScanAsync(string path, string ext, int startPage)
        {
            var readablePath = await GitLfsPointerResolver.ResolveReadablePathAsync(path);

            if (ext == ".txt" || ext == ".md")
            {
                return CleanExtractedText(await System.IO.File.ReadAllTextAsync(readablePath));
            }

            if (ext == ".docx")
            {
                await using var stream = new FileStream(readablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return await ExtractTextFromDocxStreamAsync(stream);
            }

            if (ext == ".pdf")
            {
                var start = Math.Max(1, startPage);
                var text = ExtractTextFromPdf(readablePath, start);
                if (string.IsNullOrWhiteSpace(text) && start > 1)
                {
                    text = ExtractTextFromPdf(readablePath, 1);
                }
                return CleanExtractedText(text);
            }

            throw new InvalidOperationException($"Unsupported curriculum source extension: {ext}");
        }

        private static List<(int Number, string Text)> ReadPdfPages(string path)
        {
            using var reader = new PdfReader(path);
            using var doc = new PdfDocument(reader);
            var pages = new List<(int Number, string Text)>();
            var totalPages = doc.GetNumberOfPages();
            for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
            {
                var text = PdfTextExtractor.GetTextFromPage(doc.GetPage(pageNumber)) ?? string.Empty;
                pages.Add((pageNumber, text));
            }
            return pages;
        }

        private static List<(int Number, string Text)> ReadPdfPages(Stream stream)
        {
            if (stream.CanSeek) stream.Position = 0;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;

            using var reader = new PdfReader(ms);
            using var doc = new PdfDocument(reader);
            var pages = new List<(int Number, string Text)>();
            var totalPages = doc.GetNumberOfPages();
            for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
            {
                var text = PdfTextExtractor.GetTextFromPage(doc.GetPage(pageNumber)) ?? string.Empty;
                pages.Add((pageNumber, text));
            }
            return pages;
        }

        private static string ExtractTextFromPdf(string path, int startPage)
        {
            var pdfPages = ReadPdfPages(path);
            var pages = new List<(int Number, List<string> Lines)>();
            var sb = new StringBuilder();
            foreach (var page in pdfPages)
            {
                if (page.Number < startPage) continue;
                var pageText = page.Text;
                pageText = DocumentTextCleaner.CleanPdfPageText(pageText ?? string.Empty);
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                var lines = pageText
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Regex.Replace(x ?? string.Empty, @"\s+", " ").Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Where(x => !DocumentTextCleaner.IsNoiseLine(x))
                    .ToList();
                if (lines.Count == 0) continue;

                pages.Add((page.Number, lines));
            }

            var repeatedBoundaryKeys = DocumentTextCleaner.DetectRepeatedBoundaryLineKeys(
                pages.Select(x => (IReadOnlyList<string>)x.Lines).ToList());

            foreach (var page in pages)
            {
                var filteredLines = page.Lines
                    .Where(line => !repeatedBoundaryKeys.Contains(DocumentTextCleaner.NormalizeLineKey(line)))
                    .ToList();
                if (filteredLines.Count == 0) continue;

                var normalizedPageText = DocumentTextCleaner.CleanPdfPageText(string.Join("\n", filteredLines));
                if (string.IsNullOrWhiteSpace(normalizedPageText)) continue;
                if (DocumentTextCleaner.IsLikelyBoilerplateParagraph(normalizedPageText) &&
                    DocumentTextCleaner.WordCount(normalizedPageText) < 220)
                {
                    continue;
                }

                sb.AppendLine(normalizedPageText);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static async Task<string> ExtractTextFromFileStreamAsync(Stream stream, string ext)
        {
            if (stream.CanSeek) stream.Position = 0;
            if (ext == ".txt" || ext == ".md")
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
                return CleanExtractedText(await reader.ReadToEndAsync());
            }
            if (ext == ".docx")
            {
                return await ExtractTextFromDocxStreamAsync(stream);
            }
            if (ext == ".pdf")
            {
                return ExtractTextFromPdfStream(stream);
            }
            return string.Empty;
        }

        private static async Task<string> ExtractTextFromDocxStreamAsync(Stream stream)
        {
            if (stream.CanSeek) stream.Position = 0;
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var para in body.Descendants<Paragraph>())
            {
                var line = string.Join("", para
                    .Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>()
                    .Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine(line.Trim());
                }
            }
            return CleanExtractedText(sb.ToString());
        }

        private static string ExtractTextFromPdfStream(Stream stream)
        {
            var pdfPages = ReadPdfPages(stream);
            var pages = new List<(int Number, List<string> Lines)>();
            var sb = new StringBuilder();
            foreach (var page in pdfPages)
            {
                var pageText = page.Text;
                pageText = DocumentTextCleaner.CleanPdfPageText(pageText ?? string.Empty);
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                var lines = pageText
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => Regex.Replace(x ?? string.Empty, @"\s+", " ").Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Where(x => !DocumentTextCleaner.IsNoiseLine(x))
                    .ToList();
                if (lines.Count == 0) continue;

                pages.Add((page.Number, lines));
            }

            var repeatedBoundaryKeys = DocumentTextCleaner.DetectRepeatedBoundaryLineKeys(
                pages.Select(x => (IReadOnlyList<string>)x.Lines).ToList());

            foreach (var page in pages)
            {
                var filteredLines = page.Lines
                    .Where(line => !repeatedBoundaryKeys.Contains(DocumentTextCleaner.NormalizeLineKey(line)))
                    .ToList();
                if (filteredLines.Count == 0) continue;

                var normalizedPageText = DocumentTextCleaner.CleanPdfPageText(string.Join("\n", filteredLines));
                if (string.IsNullOrWhiteSpace(normalizedPageText)) continue;
                if (DocumentTextCleaner.IsLikelyBoilerplateParagraph(normalizedPageText) &&
                    DocumentTextCleaner.WordCount(normalizedPageText) < 220)
                {
                    continue;
                }

                sb.AppendLine($"[Page {page.Number}]");
                sb.AppendLine(normalizedPageText);
                sb.AppendLine();
            }
            return CleanExtractedText(sb.ToString());
        }

        private static string CleanExtractedText(string text)
        {
            return DocumentTextCleaner.Clean(text, preservePdfPageMarkers: true);
        }

        private sealed class CognitiveScanExecution
        {
            public string CurriculumPath { get; set; } = string.Empty;
            public string OutputDir { get; set; } = string.Empty;
            public int? StartPageUsed { get; set; }
            public CognitiveScanArtifacts Artifacts { get; set; } = new();
        }

        private sealed class MappingReviewQueueDocument
        {
            public int QualificationId { get; set; }
            public string QualificationNumber { get; set; } = string.Empty;
            public string QualificationDescription { get; set; } = string.Empty;
            public string CurriculumPath { get; set; } = string.Empty;
            public string OutputDir { get; set; } = string.Empty;
            public int? StartPageUsed { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public DateTime UpdatedAtUtc { get; set; }
            public MappingReviewScanSnapshot Scan { get; set; } = new();
            public MappingReviewQueueSummary Summary { get; set; } = new();
            public List<MappingReviewItem> Items { get; set; } = new();
        }

        private sealed class MappingReviewScanSnapshot
        {
            public int ModuleCount { get; set; }
            public int CurriculumPhaseCount { get; set; }
            public int KnowledgeSubjectCount { get; set; }
            public int TopicCount { get; set; }
            public List<string> Warnings { get; set; } = new();
            public string ExtractTextPath { get; set; } = string.Empty;
            public string PhasesCsvPath { get; set; } = string.Empty;
            public string SubjectCsvPath { get; set; } = string.Empty;
            public string TopicCsvPath { get; set; } = string.Empty;
            public string ReportJsonPath { get; set; } = string.Empty;
        }

        private sealed class MappingReviewQueueSummary
        {
            public int Total { get; set; }
            public int Pending { get; set; }
            public int Applied { get; set; }
            public int Failed { get; set; }
            public int HighConfidence { get; set; }
            public int MediumConfidence { get; set; }
            public int LowConfidence { get; set; }
            public int PhaseCount { get; set; }
            public int SubjectCount { get; set; }
            public int TopicCount { get; set; }
        }

        private sealed class MappingReviewItem
        {
            public string Id { get; set; } = string.Empty;
            public string EntityType { get; set; } = string.Empty;
            public string Status { get; set; } = "pending";
            public string SuggestedAction { get; set; } = "create";
            public bool ExistsInDatabase { get; set; }
            public int? ExistingEntityId { get; set; }
            public string QualificationCode { get; set; } = string.Empty;
            public string LearningPhases { get; set; } = string.Empty;
            public string PhasesCode { get; set; } = string.Empty;
            public string PhasesDescription { get; set; } = string.Empty;
            public string PhasesPurpose { get; set; } = string.Empty;
            public int? Sequence { get; set; }
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
            public string SubjectCredits { get; set; } = string.Empty;
            public string SubjectNqfLevel { get; set; } = string.Empty;
            public string SubjectPercentage { get; set; } = string.Empty;
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string NotionalHours { get; set; } = string.Empty;
            public string PeriodsPerTopic { get; set; } = string.Empty;
            public string AssessmentCriteriaNumber { get; set; } = string.Empty;
            public string AssessmentCriteriaDescription { get; set; } = string.Empty;
            public double ConfidenceScore { get; set; }
            public string ConfidenceBand { get; set; } = "low";
            public List<string> Signals { get; set; } = new();
            public string? LastError { get; set; }
            public DateTime? ReviewedAtUtc { get; set; }
        }

        public class UploadRequest
        {
            public int QualificationId { get; set; }
            public string DocType { get; set; } = string.Empty; // curriculum | assessment
            public IFormFile? File { get; set; }
            public bool AutoStartPipeline { get; set; } = true;
            public int? StartPage { get; set; }
        }

        public class ImportFromLibraryRequest
        {
            public int QualificationId { get; set; }
            public string DocType { get; set; } = string.Empty; // curriculum | assessment
            public string SourcePath { get; set; } = string.Empty;
        }

        public class QualificationRequest
        {
            public int QualificationId { get; set; }
        }

        public class CognitiveScanRequest
        {
            public int QualificationId { get; set; }
            public int? StartPage { get; set; }
        }

        public class BuildMappingReviewQueueRequest
        {
            public int QualificationId { get; set; }
            public int? StartPage { get; set; }
        }

        public class SansMetadataScanRequest
        {
            public int QualificationId { get; set; }
            public string? SourceUrls { get; set; }
            public List<IFormFile>? Files { get; set; }
        }

        public class ApplyMappingReviewRequest
        {
            public int QualificationId { get; set; }
            public string? ItemId { get; set; }
            public List<string>? ItemIds { get; set; }
            public double? MinConfidence { get; set; }
            public bool PendingOnly { get; set; } = true;
        }

        public class ApplySansMappingReviewRequest
        {
            public int QualificationId { get; set; }
            public string? ItemId { get; set; }
            public List<string>? ItemIds { get; set; }
            public double? MinConfidence { get; set; }
            public bool PendingOnly { get; set; } = true;
        }

        public class ManualCsvUploadRequest
        {
            public int QualificationId { get; set; }
            public string EntityType { get; set; } = string.Empty; // phases | subjects | topics
            public IFormFile? File { get; set; }
            public bool AutoStartPipeline { get; set; } = true;
            public int? StartPage { get; set; }
        }

        public class QueueAutomationRequest
        {
            public int QualificationId { get; set; }
            public bool RunSeedWrite { get; set; }
            public bool RequiresApproval { get; set; } = true;
            public string? RequestedBy { get; set; }
        }
    }
}









