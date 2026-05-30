using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using ETD.Api.Services;
using ETD.Api.Data;
using ETD.Api.Models;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/admin/visuals")]
    public class VisualBackfillController : ControllerBase
    {
        private readonly PdfVisualExtractionService _extractor;
        private readonly VisualStorageService _storage;
        private readonly SourceMaterialsRepository _repo;

        public VisualBackfillController(
            PdfVisualExtractionService extractor,
            VisualStorageService storage,
            SourceMaterialsRepository repo)
        {
            _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        public sealed class BackfillRequest
        {
            public string? QualificationCode { get; set; }
            public bool DryRun { get; set; } = true;
            public int Limit { get; set; } = 24;
        }

        [HttpPost("backfill")]
        public ActionResult Backfill([FromBody] BackfillRequest req)
        {
            // Locate uploaded PDFs for the requested qualification scope
            // For now: look in ContentRootPath/uploads/{qualificationCode} or uploads root
            var uploadsRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "uploads");
            var searchRoot = string.IsNullOrWhiteSpace(req.QualificationCode)
                ? uploadsRoot
                : System.IO.Path.Combine(uploadsRoot, req.QualificationCode);

            if (!System.IO.Directory.Exists(searchRoot))
            {
                return NotFound(new { error = "No uploaded PDFs found for the selected qualification scope.", searchRoot });
            }

            var pdfs = System.IO.Directory.EnumerateFiles(searchRoot, "*.pdf", System.IO.SearchOption.AllDirectories)
                .Take(Math.Max(1, req.Limit))
                .ToList();

            var results = new List<object>();
            foreach (var pdf in pdfs)
            {
                var res = _extractor.ExtractAndPersist(pdf, new PdfVisualExtractionService.PersistOptions
                {
                    OutputDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "temp_visuals"),
                    OutputNamePrefix = System.IO.Path.GetFileNameWithoutExtension(pdf),
                    SourceDocumentName = System.IO.Path.GetFileName(pdf),
                    MaxImages = req.Limit
                });

                foreach (var visual in res.Visuals)
                {
                    var stored = _storage.StoreFile(visual.FilePath);

                    var model = new SourceMaterial
                    {
                        Title = visual.Caption,
                        FileName = stored.FileName,
                        FilePath = stored.FilePath,
                        FileType = stored.FileType,
                        Url = stored.Url,
                        ExtractedText = visual.ContextText ?? string.Empty,
                        CreatedAt = DateTime.UtcNow,
                        KnowledgeSourceType = "uploaded_pdf",
                        KnowledgeLabel = visual.PlaceholderTag
                    };

                    if (!req.DryRun)
                    {
                        if (!_repo.ExistsByFilePath(model.FilePath))
                        {
                            _repo.AddAsync(model).GetAwaiter().GetResult();
                        }
                    }

                    results.Add(new
                    {
                        pdf = System.IO.Path.GetFileName(pdf),
                        visual = visual.FileName,
                        caption = visual.Caption,
                        page = visual.PageNumber,
                        url = stored.Url,
                        persisted = !req.DryRun
                    });
                }
            }

            return Ok(new { scanned = pdfs.Count, items = results });
        }

        [HttpGet("review")]
        public ActionResult Review(int limit = 100)
        {
            // Simple read via ApplicationDbContext through repository - repository doesn't implement listing so use context directly for review
            var db = HttpContext.RequestServices.GetService(typeof(ApplicationDbContext)) as ApplicationDbContext;
            if (db == null) return StatusCode(500, new { error = "Database context not available" });

            var list = db.SourceMaterials.OrderByDescending(x => x.CreatedAt).Take(limit).Select(x => new
            {
                x.Id,
                x.Title,
                x.FileName,
                x.FilePath,
                x.Url,
                x.KnowledgeSourceType,
                x.KnowledgeLabel,
                x.CreatedAt
            }).ToList();

            return Ok(new { count = list.Count, items = list });
        }
    }
}
