using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ETD.Api.Utils;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;
using SysPath = System.IO.Path;

namespace ETD.Api.Services
{
    public sealed class PdfVisualExtractionService
    {
        private readonly ILogger<PdfVisualExtractionService> _logger;

        private static readonly HashSet<string> ExportableImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff"
        };

        public PdfVisualExtractionService(ILogger<PdfVisualExtractionService> logger)
        {
            _logger = logger;
        }

        public sealed class PersistOptions
        {
            public string OutputDirectory { get; set; } = string.Empty;
            public string OutputNamePrefix { get; set; } = "pdf_visual";
            public string SourceDocumentName { get; set; } = string.Empty;
            public int MaxImages { get; set; } = 12;
        }

        public sealed class PersistedVisual
        {
            public string FilePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string FileType { get; set; } = string.Empty;
            public string Caption { get; set; } = string.Empty;
            public string ContextText { get; set; } = string.Empty;
            public string PlaceholderTag { get; set; } = string.Empty;
            public int PageNumber { get; set; }
            public int Sequence { get; set; }
            public int PixelWidth { get; set; }
            public int PixelHeight { get; set; }
        }

        public sealed class PersistResult
        {
            public int CandidatesScanned { get; set; }
            public int PersistedCount { get; set; }
            public int SkippedCount { get; set; }
            public List<PersistedVisual> Visuals { get; set; } = new();
            public string SummaryText { get; set; } = string.Empty;
        }

        public PersistResult ExtractAndPersist(string pdfPath, PersistOptions? options = null)
        {
            var result = new PersistResult();
            if (!IsEnabled()) return result;

            options ??= new PersistOptions();
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath)) return result;
            if (string.IsNullOrWhiteSpace(options.OutputDirectory)) return result;

            var maxImages = options.MaxImages > 0
                ? Math.Clamp(options.MaxImages, 1, 48)
                : GetIntEnv("PDF_VISUAL_MAX_IMAGES", 12, 1, 48);
            var sourceDocumentName = string.IsNullOrWhiteSpace(options.SourceDocumentName)
                ? SysPath.GetFileName(pdfPath)
                : options.SourceDocumentName.Trim();
            var outputPrefix = MakeSafeFilePart(options.OutputNamePrefix, "pdf_visual");
            Directory.CreateDirectory(options.OutputDirectory);

            try
            {
                using var reader = new PdfReader(pdfPath);
                using var document = new PdfDocument(reader);

                var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var pageNumber = 1; pageNumber <= document.GetNumberOfPages(); pageNumber++)
                {
                    if (result.Visuals.Count >= maxImages) break;

                    var page = document.GetPage(pageNumber);
                    var pageCapture = CapturePage(page);
                    var pageSequence = 0;

                    foreach (var candidate in pageCapture.Images
                        .OrderByDescending(x => ScoreImageCandidate(x))
                        .ThenByDescending(x => x.PixelArea))
                    {
                        result.CandidatesScanned++;

                        if (!IsCandidateUseful(candidate))
                        {
                            result.SkippedCount++;
                            continue;
                        }

                        var ext = NormalizeImageExtension(candidate.FileExtension);
                        if (!ExportableImageExtensions.Contains(ext))
                        {
                            result.SkippedCount++;
                            continue;
                        }

                        var hash = ComputeSha256Hex(candidate.ImageBytes);
                        if (!seenHashes.Add(hash))
                        {
                            result.SkippedCount++;
                            continue;
                        }

                        pageSequence++;
                        var contextText = BuildContextText(candidate, pageCapture.TextFragments, pageCapture.FallbackPageText);
                        var placeholderTag = $"[[PDF_IMAGE_P{pageNumber:000}_{pageSequence:00}]]";
                        var caption = BuildCaption(sourceDocumentName, pageNumber, contextText);
                        var safeCaption = MakeSafeFilePart(caption, $"page_{pageNumber:000}_{pageSequence:00}");
                        var fileName = $"{outputPrefix}_p{pageNumber:000}_i{pageSequence:00}_{safeCaption}{ext}";
                        var filePath = EnsureUniquePath(SysPath.Combine(options.OutputDirectory, fileName));

                        File.WriteAllBytes(filePath, candidate.ImageBytes);
                        var sidecarPath = SysPath.Combine(
                            SysPath.GetDirectoryName(filePath) ?? options.OutputDirectory,
                            $"{SysPath.GetFileNameWithoutExtension(filePath)}.caption.md");
                        File.WriteAllText(
                            sidecarPath,
                            BuildSidecarContent(sourceDocumentName, pageNumber, placeholderTag, caption, contextText),
                            Encoding.UTF8);

                        result.Visuals.Add(new PersistedVisual
                        {
                            FilePath = filePath,
                            FileName = SysPath.GetFileName(filePath),
                            FileType = ext.TrimStart('.'),
                            Caption = caption,
                            ContextText = contextText,
                            PlaceholderTag = placeholderTag,
                            PageNumber = pageNumber,
                            Sequence = pageSequence,
                            PixelWidth = candidate.PixelWidth,
                            PixelHeight = candidate.PixelHeight
                        });
                        result.PersistedCount = result.Visuals.Count;

                        if (result.Visuals.Count >= maxImages)
                        {
                            break;
                        }
                    }
                }

                result.SummaryText = BuildSummaryText(sourceDocumentName, result.Visuals);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PDF visual extraction failed for '{PdfPath}'", pdfPath);
            }

            return result;
        }

        private static bool IsEnabled()
        {
            var raw = (Environment.GetEnvironmentVariable("PDF_VISUAL_EXTRACTION_ENABLED") ?? "true").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return true;
            return raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetIntEnv(string name, int fallback, int min, int max)
        {
            var raw = (Environment.GetEnvironmentVariable(name) ?? string.Empty).Trim();
            if (!int.TryParse(raw, out var value))
            {
                return fallback;
            }

            return Math.Clamp(value, min, max);
        }

        private static PageCapture CapturePage(PdfPage page)
        {
            var listener = new PageVisualListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);

            string fallbackPageText;
            try
            {
                fallbackPageText = PdfTextExtractor.GetTextFromPage(page) ?? string.Empty;
            }
            catch
            {
                fallbackPageText = string.Empty;
            }

            fallbackPageText = DocumentTextCleaner.CleanPdfPageText(fallbackPageText);
            fallbackPageText = Regex.Replace(fallbackPageText, @"\s+", " ").Trim();

            return new PageCapture
            {
                TextFragments = listener.TextFragments,
                Images = listener.Images,
                FallbackPageText = LimitLength(fallbackPageText, 900)
            };
        }

        private static double ScoreImageCandidate(ImageCandidate candidate)
        {
            return candidate.PixelArea + candidate.RenderArea;
        }

        private static bool IsCandidateUseful(ImageCandidate candidate)
        {
            var minWidth = GetIntEnv("PDF_VISUAL_MIN_WIDTH", 180, 48, 2400);
            var minHeight = GetIntEnv("PDF_VISUAL_MIN_HEIGHT", 120, 48, 2400);
            var minPixelArea = GetIntEnv("PDF_VISUAL_MIN_PIXEL_AREA", 32000, 4096, 8000000);
            var minRenderArea = GetIntEnv("PDF_VISUAL_MIN_RENDER_AREA", 9000, 1200, 1200000);

            if (candidate.ImageBytes == null || candidate.ImageBytes.Length < 2048) return false;
            if (candidate.PixelWidth < minWidth || candidate.PixelHeight < minHeight) return false;
            if (candidate.PixelArea < minPixelArea) return false;
            if (candidate.RenderArea < minRenderArea) return false;

            var aspect = candidate.PixelHeight <= 0 ? 0d : (double)candidate.PixelWidth / candidate.PixelHeight;
            if (aspect is < 0.12d or > 8.5d) return false;

            return true;
        }

        private static string BuildContextText(
            ImageCandidate candidate,
            IReadOnlyList<TextFragment> textFragments,
            string fallbackPageText)
        {
            var contextRadius = GetIntEnv("PDF_VISUAL_CONTEXT_RADIUS", 120, 40, 600);
            var selected = textFragments
                .Select(fragment => new
                {
                    fragment.Text,
                    Score = ScoreTextFragment(candidate.Bounds, fragment.Bounds, contextRadius)
                })
                .Where(x => x.Score > 0d && !string.IsNullOrWhiteSpace(x.Text))
                .OrderByDescending(x => x.Score)
                .Select(x => x.Text.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();

            var context = Regex.Replace(string.Join(" ", selected), @"\s+", " ").Trim();
            if (CountWords(context) < 6)
            {
                context = string.IsNullOrWhiteSpace(fallbackPageText) ? context : fallbackPageText;
            }

            return LimitLength(context, 700);
        }

        private static double ScoreTextFragment(Rectangle imageBounds, Rectangle textBounds, int contextRadius)
        {
            var verticalGap = Math.Min(
                Math.Abs(textBounds.GetBottom() - imageBounds.GetTop()),
                Math.Abs(textBounds.GetTop() - imageBounds.GetBottom()));
            var horizontalGap = DistanceBetweenIntervals(
                imageBounds.GetLeft(),
                imageBounds.GetRight(),
                textBounds.GetLeft(),
                textBounds.GetRight());
            var horizontalOverlap = OverlapBetweenIntervals(
                imageBounds.GetLeft(),
                imageBounds.GetRight(),
                textBounds.GetLeft(),
                textBounds.GetRight());

            if (verticalGap > contextRadius && horizontalOverlap <= 0f)
            {
                return -1d;
            }

            var score = Math.Max(0d, (contextRadius + 20d) - verticalGap);
            if (horizontalOverlap > 0f)
            {
                score += 35d + Math.Min(horizontalOverlap, 120f) * 0.15d;
            }
            else
            {
                score += Math.Max(0d, 24d - (horizontalGap * 0.12d));
            }

            return score;
        }

        private static float DistanceBetweenIntervals(float a1, float a2, float b1, float b2)
        {
            if (a2 < b1) return b1 - a2;
            if (b2 < a1) return a1 - b2;
            return 0f;
        }

        private static float OverlapBetweenIntervals(float a1, float a2, float b1, float b2)
        {
            return Math.Max(0f, Math.Min(a2, b2) - Math.Max(a1, b1));
        }

        private static string BuildCaption(string sourceDocumentName, int pageNumber, string contextText)
        {
            var cleaned = Regex.Replace(contextText ?? string.Empty, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = Regex.Replace(cleaned, @"^(figure|fig\.?|diagram|image|photo|illustration)\s*\d*\s*[:\-]?\s*", string.Empty, RegexOptions.IgnoreCase);
                var sentence = Regex.Split(cleaned, @"(?<=[\.\!\?])\s+")
                    .Select(x => x.Trim())
                    .FirstOrDefault(x => CountWords(x) >= 4);
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    sentence = cleaned;
                }

                sentence = LimitWords(sentence, 16).Trim(' ', '.', ',', ';', ':', '-');
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    return LimitLength(sentence, 160);
                }
            }

            var sourceStem = SysPath.GetFileNameWithoutExtension(sourceDocumentName ?? string.Empty);
            sourceStem = Regex.Replace((sourceStem ?? string.Empty).Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(sourceStem)
                ? $"Technical visual from page {pageNumber}"
                : $"Visual from {sourceStem} page {pageNumber}";
        }

        private static string BuildSidecarContent(
            string sourceDocumentName,
            int pageNumber,
            string placeholderTag,
            string caption,
            string contextText)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {caption}");
            sb.AppendLine();
            sb.AppendLine($"- Source PDF: {sourceDocumentName}");
            sb.AppendLine($"- Page: {pageNumber}");
            sb.AppendLine($"- Placeholder: {placeholderTag}");
            sb.AppendLine();
            sb.AppendLine("## Context");
            sb.AppendLine(string.IsNullOrWhiteSpace(contextText) ? "No nearby context captured." : contextText);
            return sb.ToString().Trim();
        }

        private static string BuildSummaryText(string sourceDocumentName, IReadOnlyList<PersistedVisual> visuals)
        {
            if (visuals == null || visuals.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"PDF visual extraction for {sourceDocumentName}: {visuals.Count} visual(s) captured.");
            foreach (var visual in visuals.Take(12))
            {
                sb.AppendLine($"{visual.PlaceholderTag} Page {visual.PageNumber}: {visual.Caption}");
                if (!string.IsNullOrWhiteSpace(visual.ContextText))
                {
                    sb.AppendLine($"Context: {LimitLength(visual.ContextText, 220)}");
                }
            }

            return sb.ToString().Trim();
        }

        private static string NormalizeImageExtension(string? extension)
        {
            var normalized = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;
            return normalized switch
            {
                "jpg" => ".jpg",
                "jpeg" => ".jpeg",
                "png" => ".png",
                "gif" => ".gif",
                "bmp" => ".bmp",
                "tif" => ".tif",
                "tiff" => ".tiff",
                _ => $".{normalized}"
            };
        }

        private static string MakeSafeFilePart(string? value, string fallback)
        {
            var cleaned = Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9]+", "_").Trim('_');
            cleaned = Regex.Replace(cleaned, @"_+", "_");
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : LimitLength(cleaned, 64);
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            var directory = SysPath.GetDirectoryName(path) ?? string.Empty;
            var stem = SysPath.GetFileNameWithoutExtension(path);
            var ext = SysPath.GetExtension(path);
            var counter = 2;
            string candidate;
            do
            {
                candidate = SysPath.Combine(directory, $"{stem}_{counter}{ext}");
                counter++;
            }
            while (File.Exists(candidate));

            return candidate;
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes ?? Array.Empty<byte>());
            return Convert.ToHexString(hash);
        }

        private static string LimitLength(string? value, int maxLen)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length <= maxLen) return text;
            return text.Substring(0, maxLen).Trim();
        }

        private static string LimitWords(string? value, int maxWords)
        {
            var words = Regex.Split((value ?? string.Empty).Trim(), @"\s+")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(maxWords)
                .ToList();
            return string.Join(" ", words);
        }

        private static int CountWords(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            return Regex.Split(value.Trim(), @"\s+").Count(x => !string.IsNullOrWhiteSpace(x));
        }

        private sealed class PageCapture
        {
            public List<TextFragment> TextFragments { get; init; } = new();
            public List<ImageCandidate> Images { get; init; } = new();
            public string FallbackPageText { get; init; } = string.Empty;
        }

        private sealed class TextFragment
        {
            public string Text { get; init; } = string.Empty;
            public Rectangle Bounds { get; init; } = new Rectangle(0, 0, 0, 0);
        }

        private sealed class ImageCandidate
        {
            public byte[] ImageBytes { get; init; } = Array.Empty<byte>();
            public string FileExtension { get; init; } = string.Empty;
            public Rectangle Bounds { get; init; } = new Rectangle(0, 0, 0, 0);
            public int PixelWidth { get; init; }
            public int PixelHeight { get; init; }
            public int PixelArea => PixelWidth * PixelHeight;
            public float RenderArea => Math.Abs(Bounds.GetWidth() * Bounds.GetHeight());
        }

        private sealed class PageVisualListener : IEventListener
        {
            public List<TextFragment> TextFragments { get; } = new();
            public List<ImageCandidate> Images { get; } = new();

            public void EventOccurred(IEventData data, EventType type)
            {
                if (type == EventType.RENDER_TEXT && data is TextRenderInfo textInfo)
                {
                    var rawText = Regex.Replace(textInfo.GetText() ?? string.Empty, @"\s+", " ").Trim();
                    if (string.IsNullOrWhiteSpace(rawText)) return;

                    var ascent = textInfo.GetAscentLine()?.GetBoundingRectangle();
                    var descent = textInfo.GetDescentLine()?.GetBoundingRectangle();
                    var bounds = ascent != null && descent != null
                        ? Rectangle.GetCommonRectangle(new[] { ascent, descent })
                        : ascent ?? descent;
                    if (bounds == null) return;

                    TextFragments.Add(new TextFragment
                    {
                        Text = rawText,
                        Bounds = bounds
                    });
                    return;
                }

                if (type == EventType.RENDER_IMAGE && data is ImageRenderInfo imageInfo)
                {
                    try
                    {
                        var image = imageInfo.GetImage();
                        if (image == null || image.IsMask() || image.IsSoftMask()) return;

                        var bytes = image.GetImageBytes();
                        if (bytes == null || bytes.Length == 0) return;

                        Images.Add(new ImageCandidate
                        {
                            ImageBytes = bytes,
                            FileExtension = NormalizeImageExtension(image.IdentifyImageFileExtension()),
                            Bounds = GetBounds(imageInfo.GetImageCtm()),
                            PixelWidth = (int)Math.Round(image.GetWidth()),
                            PixelHeight = (int)Math.Round(image.GetHeight())
                        });
                    }
                    catch
                    {
                        // Skip unreadable inline or masked images without failing the page.
                    }
                }
            }

            public ICollection<EventType> GetSupportedEvents()
            {
                return new HashSet<EventType>
                {
                    EventType.RENDER_TEXT,
                    EventType.RENDER_IMAGE
                };
            }
        }

        private static Rectangle GetBounds(Matrix ctm)
        {
            var points = new[]
            {
                new Vector(0, 0, 1).Cross(ctm),
                new Vector(1, 0, 1).Cross(ctm),
                new Vector(0, 1, 1).Cross(ctm),
                new Vector(1, 1, 1).Cross(ctm)
            };

            var minX = points.Min(p => p.Get(0));
            var maxX = points.Max(p => p.Get(0));
            var minY = points.Min(p => p.Get(1));
            var maxY = points.Max(p => p.Get(1));
            return new Rectangle(minX, minY, Math.Max(0.1f, maxX - minX), Math.Max(0.1f, maxY - minY));
        }
    }
}
