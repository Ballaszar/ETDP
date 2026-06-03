using System;
using System.Linq;
using ETD.Api.Services;
using ETD.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETD.Api.Tools
{
    public static class BackfillPdfImages
    {
        public static void Run(IServiceProvider services, string qualificationCode = null, bool dryRun = true, int limit = 24)
        {
            var extractor = services.GetRequiredService<PdfVisualExtractionService>();
            var storage = services.GetRequiredService<VisualStorageService>();
            var repo = services.GetRequiredService<SourceMaterialsRepository>();

            var uploadsRoot = System.IO.Path.Combine(AppContext.BaseDirectory, "uploads");
            var searchRoot = string.IsNullOrWhiteSpace(qualificationCode)
                ? uploadsRoot
                : System.IO.Path.Combine(uploadsRoot, qualificationCode);

            if (!System.IO.Directory.Exists(searchRoot))
            {
                Console.WriteLine("No uploaded PDFs found at: " + searchRoot);
                return;
            }

            var pdfs = System.IO.Directory.EnumerateFiles(searchRoot, "*.pdf", System.IO.SearchOption.AllDirectories).Take(limit).ToList();
            Console.WriteLine($"Found {pdfs.Count} PDFs to scan (dryRun={dryRun})");

            foreach (var pdf in pdfs)
            {
                Console.WriteLine("Scanning: " + pdf);
                var res = extractor.ExtractAndPersist(pdf, new PdfVisualExtractionService.PersistOptions
                {
                    OutputDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "temp_visuals"),
                    OutputNamePrefix = System.IO.Path.GetFileNameWithoutExtension(pdf),
                    SourceDocumentName = System.IO.Path.GetFileName(pdf),
                    MaxImages = limit
                });

                foreach (var visual in res.Visuals)
                {
                    var stored = storage.StoreFile(visual.FilePath);
                    Console.WriteLine($" -> {visual.FileName} -> {stored.Url}");

                    if (!dryRun)
                    {
                        var model = new ETD.Api.Models.SourceMaterial
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

                        if (!repo.ExistsByFilePath(model.FilePath))
                        {
                            repo.AddAsync(model).GetAwaiter().GetResult();
                        }
                    }
                }
            }
        }
    }
}
