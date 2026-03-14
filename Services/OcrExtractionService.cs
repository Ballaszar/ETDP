using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETD.Api.Utils;
using Microsoft.Extensions.Logging;

namespace ETD.Api.Services
{
    public sealed class OcrExtractionService
    {
        private readonly ILogger<OcrExtractionService> _logger;
        private static readonly HttpClient _http = new HttpClient();
        private readonly object _telemetryLock = new();
        private long _attemptCount;
        private long _successCount;
        private long _fallbackSuccessCount;
        private long _failureCount;
        private long _skippedCount;
        private string _lastEngineUsed = "none";
        private string _lastOutcome = "not_run";
        private string _lastError = string.Empty;
        private DateTime? _lastErrorAtUtc;
        private DateTime? _lastAttemptAtUtc;
        private DateTime? _lastSuccessAtUtc;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".svg"
        };

        public sealed class OcrHealthSnapshot
        {
            public bool Enabled { get; set; }
            public string EngineMode { get; set; } = "auto";
            public string EffectiveEngineOrder { get; set; } = "tesseract";
            public string PdfMode { get; set; } = "auto";
            public long Attempts { get; set; }
            public long Successes { get; set; }
            public long FallbackSuccesses { get; set; }
            public long Failures { get; set; }
            public long Skipped { get; set; }
            public string LastEngineUsed { get; set; } = "none";
            public string LastOutcome { get; set; } = "not_run";
            public DateTime? LastAttemptAtUtc { get; set; }
            public DateTime? LastSuccessAtUtc { get; set; }
            public string LastError { get; set; } = string.Empty;
            public DateTime? LastErrorAtUtc { get; set; }
            public DateTime CheckedAtUtc { get; set; }
        }

        private sealed class OcrRunResult
        {
            public string Text { get; set; } = string.Empty;
            public string EngineUsed { get; set; } = "none";
            public bool UsedFallback { get; set; }
            public string? LastError { get; set; }
        }

        private sealed class EngineAttemptResult
        {
            public string Engine { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public string? Error { get; set; }
        }

        public OcrExtractionService(ILogger<OcrExtractionService> logger)
        {
            _logger = logger;
        }

        public bool IsImageExtension(string? ext)
        {
            return ImageExtensions.Contains(NormalizeExt(ext));
        }

        public bool IsSupportedOcrExtension(string? ext)
        {
            var normalized = NormalizeExt(ext);
            return normalized == ".pdf" || ImageExtensions.Contains(normalized);
        }

        public OcrHealthSnapshot GetHealthSnapshot()
        {
            lock (_telemetryLock)
            {
                var enabled = GetBoolEnv("OCR_ENABLED", true);
                var engineMode = (Environment.GetEnvironmentVariable("OCR_ENGINE") ?? "tesseract").Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(engineMode) || engineMode == "auto" || engineMode == "azure")
                {
                    engineMode = "tesseract";
                }
                var order = ResolveEngineOrder();
                var pdfMode = (Environment.GetEnvironmentVariable("OCR_PDF_MODE") ?? "auto").Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(pdfMode))
                {
                    pdfMode = "auto";
                }

                return new OcrHealthSnapshot
                {
                    Enabled = enabled,
                    EngineMode = engineMode,
                    EffectiveEngineOrder = string.Join(" -> ", order),
                    PdfMode = pdfMode,
                    Attempts = _attemptCount,
                    Successes = _successCount,
                    FallbackSuccesses = _fallbackSuccessCount,
                    Failures = _failureCount,
                    Skipped = _skippedCount,
                    LastEngineUsed = _lastEngineUsed,
                    LastOutcome = _lastOutcome,
                    LastAttemptAtUtc = _lastAttemptAtUtc,
                    LastSuccessAtUtc = _lastSuccessAtUtc,
                    LastError = _lastError,
                    LastErrorAtUtc = _lastErrorAtUtc,
                    CheckedAtUtc = DateTime.UtcNow
                };
            }
        }

        public string EnhanceExtractedText(string filePath, string? extension, string? existingText)
        {
            try
            {
                return EnhanceExtractedTextAsync(filePath, extension, existingText).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OCR enhancement failed synchronously for '{FilePath}'", filePath);
                return Clean(existingText);
            }
        }

        public async Task<string> EnhanceExtractedTextAsync(
            string filePath,
            string? extension,
            string? existingText,
            CancellationToken cancellationToken = default)
        {
            var baseline = Clean(existingText);
            if (!GetBoolEnv("OCR_ENABLED", true))
            {
                RecordSkip("disabled");
                return baseline;
            }

            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            {
                RecordSkip("missing_file");
                return baseline;
            }

            var ext = NormalizeExt(extension);
            if (!IsSupportedOcrExtension(ext))
            {
                RecordSkip("unsupported_extension");
                return baseline;
            }

            try
            {
                if (IsImageExtension(ext))
                {
                    var run = await RunImageOcrAsync(filePath, cancellationToken);
                    if (string.IsNullOrWhiteSpace(run.Text))
                    {
                        RecordFailure(run.EngineUsed, run.LastError ?? "OCR returned no text.");
                        return baseline;
                    }

                    var merged = MergeExtractedText(baseline, run.Text, "OCR_IMAGE");
                    RecordSuccess(run.EngineUsed, run.UsedFallback);
                    return merged;
                }

                if (ext == ".pdf")
                {
                    var pdfMode = (Environment.GetEnvironmentVariable("OCR_PDF_MODE") ?? "auto").Trim().ToLowerInvariant();
                    if (pdfMode == "off")
                    {
                        RecordSkip("pdf_mode_off");
                        return baseline;
                    }

                    if (pdfMode == "auto" && HasSufficientDigitalPdfText(baseline))
                    {
                        RecordSkip("pdf_digital_text_sufficient");
                        return baseline;
                    }

                    var run = await RunPdfOcrAsync(filePath, cancellationToken);
                    if (string.IsNullOrWhiteSpace(run.Text))
                    {
                        RecordFailure(run.EngineUsed, run.LastError ?? "OCR returned no text.");
                        return baseline;
                    }

                    var merged = MergeExtractedText(baseline, run.Text, "OCR_PDF_LAYOUT");
                    RecordSuccess(run.EngineUsed, run.UsedFallback);
                    return merged;
                }
            }
            catch (Exception ex)
            {
                RecordFailure("exception", ex.Message);
                _logger.LogWarning(ex, "OCR enhancement failed for '{FilePath}'", filePath);
            }

            return baseline;
        }

        private async Task<OcrRunResult> RunImageOcrAsync(string filePath, CancellationToken cancellationToken)
        {
            var engines = ResolveEngineOrder();
            RecordAttemptStart();
            string? lastError = null;
            var lastEngineUsed = "none";
            for (var index = 0; index < engines.Length; index++)
            {
                var engine = engines[index];
                lastEngineUsed = engine;
                if (engine == "azure")
                {
                    var azure = await TryAzureDocumentIntelligenceAsync(filePath, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(azure.Text))
                    {
                        return new OcrRunResult
                        {
                            Text = azure.Text,
                            EngineUsed = azure.Engine,
                            UsedFallback = index > 0
                        };
                    }
                    if (!string.IsNullOrWhiteSpace(azure.Error)) lastError = azure.Error;
                    continue;
                }

                if (engine == "tesseract")
                {
                    var tess = await TryTesseractAsync(filePath, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(tess.Text))
                    {
                        return new OcrRunResult
                        {
                            Text = tess.Text,
                            EngineUsed = tess.Engine,
                            UsedFallback = index > 0
                        };
                    }
                    if (!string.IsNullOrWhiteSpace(tess.Error)) lastError = tess.Error;
                }
            }

            return new OcrRunResult
            {
                Text = string.Empty,
                EngineUsed = lastEngineUsed,
                UsedFallback = false,
                LastError = string.IsNullOrWhiteSpace(lastError) ? "No OCR text produced." : lastError
            };
        }

        private async Task<OcrRunResult> RunPdfOcrAsync(string filePath, CancellationToken cancellationToken)
        {
            var engines = ResolveEngineOrder();
            RecordAttemptStart();
            string? lastError = null;
            var lastEngineUsed = "none";
            for (var index = 0; index < engines.Length; index++)
            {
                var engine = engines[index];
                lastEngineUsed = engine;
                if (engine == "azure")
                {
                    var azure = await TryAzureDocumentIntelligenceAsync(filePath, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(azure.Text))
                    {
                        return new OcrRunResult
                        {
                            Text = azure.Text,
                            EngineUsed = azure.Engine,
                            UsedFallback = index > 0
                        };
                    }
                    if (!string.IsNullOrWhiteSpace(azure.Error)) lastError = azure.Error;
                    continue;
                }

                if (engine == "tesseract")
                {
                    var tess = await TryTesseractAsync(filePath, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(tess.Text))
                    {
                        return new OcrRunResult
                        {
                            Text = tess.Text,
                            EngineUsed = tess.Engine,
                            UsedFallback = index > 0
                        };
                    }
                    if (!string.IsNullOrWhiteSpace(tess.Error)) lastError = tess.Error;
                }
            }

            return new OcrRunResult
            {
                Text = string.Empty,
                EngineUsed = lastEngineUsed,
                UsedFallback = false,
                LastError = string.IsNullOrWhiteSpace(lastError) ? "No OCR text produced." : lastError
            };
        }

        private string[] ResolveEngineOrder()
        {
            var mode = (Environment.GetEnvironmentVariable("OCR_ENGINE") ?? "tesseract").Trim().ToLowerInvariant();
            return mode switch
            {
                "tesseract" => new[] { "tesseract" },
                "auto" => new[] { "tesseract" },
                "azure" => new[] { "tesseract" },
                _ => new[] { "tesseract" }
            };
        }

        private async Task<EngineAttemptResult> TryAzureDocumentIntelligenceAsync(string filePath, CancellationToken cancellationToken)
        {
            var result = new EngineAttemptResult { Engine = "azure" };
            if (!AiRuntime.AllowFoundry())
            {
                result.Error = "Azure Document Intelligence OCR is disabled by policy.";
                return result;
            }

            var endpoint = (Environment.GetEnvironmentVariable("AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT") ?? string.Empty).Trim();
            var key = (Environment.GetEnvironmentVariable("AZURE_DOCUMENT_INTELLIGENCE_KEY") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            {
                result.Error = "Azure Document Intelligence is not configured.";
                return result;
            }

            var apiVersion = (Environment.GetEnvironmentVariable("AZURE_DOCUMENT_INTELLIGENCE_API_VERSION") ?? "2024-11-30").Trim();
            var model = (Environment.GetEnvironmentVariable("AZURE_DOCUMENT_INTELLIGENCE_MODEL") ?? "prebuilt-layout").Trim();
            if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = "https://" + endpoint;
            }

            var analyzeUrl = $"{endpoint.TrimEnd('/')}/documentintelligence/documentModels/{Uri.EscapeDataString(model)}:analyze?api-version={Uri.EscapeDataString(apiVersion)}";

            using var content = new ByteArrayContent(await System.IO.File.ReadAllBytesAsync(filePath, cancellationToken));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            using var request = new HttpRequestMessage(HttpMethod.Post, analyzeUrl)
            {
                Content = content
            };
            request.Headers.Add("Ocp-Apim-Subscription-Key", key);

            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                result.Text = ExtractAzureAnalyzeText(body);
                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    result.Error = "Azure OCR completed but returned empty text.";
                }
                return result;
            }

            if (response.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                var failBody = await response.Content.ReadAsStringAsync(cancellationToken);
                result.Error = $"Azure OCR analyze request failed with HTTP {(int)response.StatusCode}.";
                _logger.LogDebug("Azure Document Intelligence analyze failed for '{FilePath}': {Status} {Body}",
                    filePath, (int)response.StatusCode, TrimForLog(failBody, 500));
                return result;
            }

            if (!response.Headers.TryGetValues("operation-location", out var values))
            {
                result.Error = "Azure OCR response missing operation-location header.";
                return result;
            }

            var operationLocation = values.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(operationLocation))
            {
                result.Error = "Azure OCR response returned empty operation location.";
                return result;
            }

            var timeoutSeconds = GetIntEnv("OCR_AZURE_TIMEOUT_SECONDS", 120, 15, 600);
            var pollMs = GetIntEnv("OCR_AZURE_POLL_MS", 1000, 300, 5000);
            var started = DateTime.UtcNow;

            while (DateTime.UtcNow - started < TimeSpan.FromSeconds(timeoutSeconds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(pollMs, cancellationToken);

                using var pollRequest = new HttpRequestMessage(HttpMethod.Get, operationLocation);
                pollRequest.Headers.Add("Ocp-Apim-Subscription-Key", key);
                using var pollResponse = await _http.SendAsync(pollRequest, cancellationToken);
                var pollBody = await pollResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!pollResponse.IsSuccessStatusCode)
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(pollBody);
                var root = doc.RootElement;
                var status = root.TryGetProperty("status", out var statusProp)
                    ? (statusProp.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                    : string.Empty;
                if (status == "succeeded")
                {
                    result.Text = ExtractAzureAnalyzeText(pollBody);
                    if (string.IsNullOrWhiteSpace(result.Text))
                    {
                        result.Error = "Azure OCR succeeded but yielded no text.";
                    }
                    return result;
                }

                if (status == "failed" || status == "canceled")
                {
                    result.Error = $"Azure OCR job status was '{status}'.";
                    _logger.LogDebug("Azure Document Intelligence OCR status '{Status}' for '{FilePath}'", status, filePath);
                    return result;
                }
            }

            result.Error = $"Azure OCR timed out after {timeoutSeconds} seconds.";
            return result;
        }

        private async Task<EngineAttemptResult> TryTesseractAsync(string filePath, CancellationToken cancellationToken)
        {
            var result = new EngineAttemptResult { Engine = "tesseract" };
            var tesseractPath = ResolveTesseractExecutable();
            var lang = (Environment.GetEnvironmentVariable("TESSERACT_LANG") ?? "eng").Trim();
            var timeoutSeconds = GetIntEnv("OCR_TESSERACT_TIMEOUT_SECONDS", 90, 10, 600);
            var psm = (Environment.GetEnvironmentVariable("TESSERACT_PSM") ?? "6").Trim();

            var args = $"{Quote(filePath)} stdout -l {lang} --dpi 300 --psm {psm}";
            var psi = new ProcessStartInfo
            {
                FileName = tesseractPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                {
                    result.Error = "Failed to start tesseract process.";
                    return result;
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cts.Token);

                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                if (process.ExitCode != 0)
                {
                    result.Error = $"Tesseract exited with code {process.ExitCode}.";
                    _logger.LogDebug("Tesseract OCR exit code {ExitCode} for '{FilePath}' with stderr: {Stderr}",
                        process.ExitCode, filePath, TrimForLog(stderr, 400));
                    return result;
                }

                result.Text = Clean(stdout);
                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    result.Error = !string.IsNullOrWhiteSpace(stderr)
                        ? $"Tesseract produced no OCR text. {TrimForLog(stderr, 250)}"
                        : "Tesseract produced no OCR text.";
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        _logger.LogDebug("Tesseract produced no OCR text for '{FilePath}'. stderr: {Stderr}",
                            filePath, TrimForLog(stderr, 400));
                    }
                }
                return result;
            }
            catch (OperationCanceledException)
            {
                result.Error = $"Tesseract OCR timed out after {timeoutSeconds} seconds.";
                _logger.LogDebug("Tesseract OCR timed out for '{FilePath}'", filePath);
                return result;
            }
            catch (Exception ex)
            {
                result.Error = $"Tesseract OCR failed: {ex.Message}";
                _logger.LogDebug(ex, "Tesseract OCR failed for '{FilePath}'", filePath);
                return result;
            }
        }

        private static string ResolveTesseractExecutable()
        {
            var configured = (Environment.GetEnvironmentVariable("TESSERACT_PATH") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (System.IO.File.Exists(configured))
                {
                    return configured;
                }

                if (Directory.Exists(configured))
                {
                    var nested = Path.Combine(configured, "tesseract.exe");
                    if (System.IO.File.Exists(nested))
                    {
                        return nested;
                    }
                }

                if (!configured.Contains(Path.DirectorySeparatorChar) && !configured.Contains(Path.AltDirectorySeparatorChar))
                {
                    return configured;
                }
            }

            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tesseract-OCR", "tesseract.exe")
            };

            foreach (var candidate in candidates)
            {
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.IsNullOrWhiteSpace(configured) ? "tesseract" : configured;
        }

        private void RecordAttemptStart()
        {
            lock (_telemetryLock)
            {
                _attemptCount++;
                _lastAttemptAtUtc = DateTime.UtcNow;
                _lastOutcome = "running";
            }
        }

        private void RecordSuccess(string engineUsed, bool usedFallback)
        {
            lock (_telemetryLock)
            {
                _successCount++;
                if (usedFallback)
                {
                    _fallbackSuccessCount++;
                    _lastOutcome = "fallback_success";
                }
                else
                {
                    _lastOutcome = "success";
                }

                _lastEngineUsed = string.IsNullOrWhiteSpace(engineUsed) ? "none" : engineUsed;
                _lastSuccessAtUtc = DateTime.UtcNow;
            }
        }

        private void RecordFailure(string engineUsed, string error)
        {
            lock (_telemetryLock)
            {
                _failureCount++;
                _lastEngineUsed = string.IsNullOrWhiteSpace(engineUsed) ? "none" : engineUsed;
                _lastOutcome = "failure";
                _lastError = CleanError(error);
                _lastErrorAtUtc = DateTime.UtcNow;
            }
        }

        private void RecordSkip(string reason)
        {
            lock (_telemetryLock)
            {
                _skippedCount++;
                _lastOutcome = string.IsNullOrWhiteSpace(reason) ? "skipped" : $"skipped:{reason}";
            }
        }

        private static string CleanError(string? value)
        {
            var text = Regex.Replace((value ?? string.Empty).Trim(), @"\s+", " ");
            if (text.Length <= 500) return text;
            return text.Substring(0, 500).TrimEnd() + "...";
        }

        private static string ExtractAzureAnalyzeText(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var analyze = root.TryGetProperty("analyzeResult", out var analyzeResult)
                ? analyzeResult
                : root;

            var sb = new StringBuilder();

            if (analyze.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
            {
                var content = contentProp.GetString();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine(content);
                }
            }

            if (analyze.TryGetProperty("paragraphs", out var paragraphs) && paragraphs.ValueKind == JsonValueKind.Array)
            {
                var added = 0;
                foreach (var paragraph in paragraphs.EnumerateArray())
                {
                    if (added >= 500) break;
                    if (!paragraph.TryGetProperty("content", out var pTextProp) || pTextProp.ValueKind != JsonValueKind.String) continue;
                    var pText = (pTextProp.GetString() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(pText)) continue;
                    var role = paragraph.TryGetProperty("role", out var roleProp) && roleProp.ValueKind == JsonValueKind.String
                        ? (roleProp.GetString() ?? string.Empty).Trim()
                        : string.Empty;
                    if (!string.IsNullOrWhiteSpace(role))
                    {
                        sb.AppendLine($"[{role}] {pText}");
                    }
                    else
                    {
                        sb.AppendLine(pText);
                    }
                    added++;
                }
            }

            if (analyze.TryGetProperty("tables", out var tables) && tables.ValueKind == JsonValueKind.Array)
            {
                var tableIndex = 0;
                foreach (var table in tables.EnumerateArray())
                {
                    tableIndex++;
                    if (tableIndex > 40) break;
                    if (!table.TryGetProperty("cells", out var cellsProp) || cellsProp.ValueKind != JsonValueKind.Array) continue;

                    var cells = cellsProp.EnumerateArray()
                        .Select(c =>
                        {
                            var row = c.TryGetProperty("rowIndex", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0;
                            var col = c.TryGetProperty("columnIndex", out var cl) && cl.ValueKind == JsonValueKind.Number ? cl.GetInt32() : 0;
                            var txt = c.TryGetProperty("content", out var t) && t.ValueKind == JsonValueKind.String ? (t.GetString() ?? string.Empty) : string.Empty;
                            return new { row, col, txt = txt.Trim() };
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x.txt))
                        .OrderBy(x => x.row)
                        .ThenBy(x => x.col)
                        .ToList();

                    if (cells.Count == 0) continue;
                    sb.AppendLine($"[Table {tableIndex}]");
                    foreach (var group in cells.GroupBy(x => x.row).OrderBy(g => g.Key))
                    {
                        var rowText = string.Join(" | ", group.OrderBy(x => x.col).Select(x => x.txt));
                        if (!string.IsNullOrWhiteSpace(rowText))
                        {
                            sb.AppendLine(rowText);
                        }
                    }
                }
            }

            return Clean(sb.ToString());
        }

        private static bool HasSufficientDigitalPdfText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var words = Regex.Matches(text, @"\b[\p{L}\p{N}][\p{L}\p{N}\-']*\b").Count;
            var minWords = GetIntEnv("OCR_PDF_MIN_WORDS", 120, 20, 2000);
            return words >= minWords;
        }

        private static string MergeExtractedText(string existingText, string ocrText, string label)
        {
            var existing = Clean(existingText);
            var ocr = Clean(ocrText);
            if (string.IsNullOrWhiteSpace(ocr)) return existing;
            if (string.IsNullOrWhiteSpace(existing)) return Limit(ocr, 320000);

            var nExisting = NormalizeLoose(existing);
            var nOcr = NormalizeLoose(ocr);
            if (!string.IsNullOrWhiteSpace(nExisting) && !string.IsNullOrWhiteSpace(nOcr))
            {
                if (nExisting.Contains(nOcr, StringComparison.Ordinal)) return existing;
                if (nOcr.Contains(nExisting, StringComparison.Ordinal)) return Limit(ocr, 320000);
            }

            var merged = $"{existing}\n\n[{label}]\n{ocr}";
            return Limit(merged, 320000);
        }

        private static string NormalizeLoose(string value)
        {
            return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"\s+", " ").Trim();
        }

        private static string Clean(string? value)
        {
            return DocumentTextCleaner.Clean(value, preservePdfPageMarkers: true);
        }

        private static string NormalizeExt(string? ext)
        {
            var value = (ext ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;
            if (!value.StartsWith(".")) value = "." + value;
            return value.ToLowerInvariant();
        }

        private static bool GetBoolEnv(string key, bool defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
            if (bool.TryParse(raw.Trim(), out var parsed)) return parsed;
            return defaultValue;
        }

        private static int GetIntEnv(string key, int defaultValue, int min, int max)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
            if (!int.TryParse(raw.Trim(), out var parsed)) return defaultValue;
            if (parsed < min) return min;
            if (parsed > max) return max;
            return parsed;
        }

        private static string Quote(string value)
        {
            return $"\"{(value ?? string.Empty).Replace("\"", "\\\"")}\"";
        }

        private static string Limit(string value, int maxChars)
        {
            var text = value ?? string.Empty;
            if (text.Length <= maxChars) return text;
            return text.Substring(0, maxChars).TrimEnd() + "...";
        }

        private static string TrimForLog(string? value, int max)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length <= max) return text;
            return text.Substring(0, max).TrimEnd() + "...";
        }
    }
}
