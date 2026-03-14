using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ETD.Api.Data;
using ETD.Api.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TextToVideoController : ControllerBase
    {
        private static readonly HttpClient _http = new HttpClient();
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".webm", ".mkv", ".avi" };
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<TextToVideoController> _logger;

        public TextToVideoController(ApplicationDbContext context, IWebHostEnvironment environment, ILogger<TextToVideoController> logger)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        [HttpGet("tts-options")]
        public IActionResult GetTtsOptions()
        {
            var openAiAllowed = AiRuntime.AllowOpenAi();
            var openAiKey = (Secrets.GetOpenAIKey() ?? string.Empty).Trim();
            var openAiConfigured = openAiAllowed && !string.IsNullOrWhiteSpace(openAiKey);

            var defaultOpenAiModel = FirstNonEmpty(
                Environment.GetEnvironmentVariable("OPENAI_TTS_MODEL"),
                "gpt-4o-mini-tts");
            var defaultOpenAiVoice = FirstNonEmpty(
                Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE"),
                "alloy");
            var openAiModels = ParseCsvOptions(
                Environment.GetEnvironmentVariable("OPENAI_TTS_MODELS"),
                defaultOpenAiModel,
                "tts-1-hd",
                "tts-1");
            var openAiVoices = ParseCsvOptions(
                Environment.GetEnvironmentVariable("OPENAI_TTS_VOICES"),
                defaultOpenAiVoice,
                "verse",
                "sage",
                "ash");

            return Ok(new
            {
                aiMode = AiRuntime.GetMode(),
                providers = new[]
                {
                    new
                    {
                        id = "browser",
                        label = "Browser speechSynthesis",
                        available = true,
                        reason = "Uses local system/browser voices.",
                        models = new[] { "browser-default" },
                        voices = Array.Empty<string>(),
                        defaultModel = "browser-default",
                        defaultVoice = string.Empty
                    },
                    new
                    {
                        id = "openai",
                        label = "OpenAI TTS",
                        available = openAiConfigured,
                        reason = !openAiAllowed
                            ? "OpenAI disabled by AI_MODE."
                            : (string.IsNullOrWhiteSpace(openAiKey)
                                ? "OPENAI_API_KEY is not configured."
                                : "OpenAI TTS available."),
                        models = openAiConfigured ? openAiModels : Array.Empty<string>(),
                        voices = openAiConfigured ? openAiVoices : Array.Empty<string>(),
                        defaultModel = defaultOpenAiModel,
                        defaultVoice = defaultOpenAiVoice
                    }
                },
                defaults = new
                {
                    provider = openAiConfigured ? "openai" : "browser",
                    browserLanguage = FirstNonEmpty(Environment.GetEnvironmentVariable("DEFAULT_TTS_LANG"), "en-ZA"),
                    browserPreferredVoice = FirstNonEmpty(Environment.GetEnvironmentVariable("DEFAULT_TTS_VOICE"), "Microsoft"),
                    openAiModel = defaultOpenAiModel,
                    openAiVoice = defaultOpenAiVoice,
                    format = "mp3",
                    speed = 1.0
                }
            });
        }

        [HttpPost("tts-preview")]
        public async Task<IActionResult> TtsPreview([FromBody] TtsPreviewRequest? request, CancellationToken cancellationToken)
        {
            request ??= new TtsPreviewRequest();
            var provider = (request.Provider ?? "openai").Trim().ToLowerInvariant();
            if (provider == "browser")
            {
                return BadRequest(new { error = "Browser TTS should be played directly in the client." });
            }
            if (provider != "openai")
            {
                return BadRequest(new { error = $"Unsupported TTS provider: {provider}" });
            }

            if (!AiRuntime.AllowOpenAi())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "OpenAI TTS is disabled by AI_MODE." });
            }

            var key = (Secrets.GetOpenAIKey() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "OPENAI_API_KEY is not configured." });
            }

            var text = NormalizePrompt(request.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                return BadRequest(new { error = "Text is required." });
            }

            if (text.Length > 4000)
            {
                text = text.Substring(0, 4000);
            }

            var model = string.IsNullOrWhiteSpace(request.Model)
                ? (Environment.GetEnvironmentVariable("OPENAI_TTS_MODEL") ?? "gpt-4o-mini-tts")
                : request.Model.Trim();
            var voice = string.IsNullOrWhiteSpace(request.Voice)
                ? (Environment.GetEnvironmentVariable("OPENAI_TTS_VOICE") ?? "alloy")
                : request.Voice.Trim();
            var format = NormalizeTtsFormat(request.Format);
            var speed = Math.Clamp(request.Speed.GetValueOrDefault(1.0), 0.25, 4.0);

            var payload = new
            {
                model,
                voice,
                input = text,
                format,
                speed
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                using var response = await _http.SendAsync(msg, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    return StatusCode((int)response.StatusCode, new { error = $"OpenAI TTS failed: {TrimTail(body, 600)}" });
                }

                var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (audio.Length == 0)
                {
                    return StatusCode(StatusCodes.Status502BadGateway, new { error = "OpenAI returned empty audio output." });
                }

                return File(audio, ResolveTtsContentType(format), $"tts-preview.{format}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI TTS preview failed");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = $"OpenAI TTS preview failed: {ex.Message}" });
            }
        }

        [HttpPost("generate")]
        public async Task<IActionResult> Generate([FromBody] GenerateVideoRequest? request, CancellationToken cancellationToken)
        {
            request ??= new GenerateVideoRequest();
            var prompt = NormalizePrompt(request.Prompt);
            if (string.IsNullOrWhiteSpace(prompt)) return BadRequest(new { error = "Prompt is required." });

            var conditioningPath = await ResolveConditioningPathAsync(request, cancellationToken);
            var local = await TryRunLocalLtxAsync(prompt, conditioningPath, request, cancellationToken);
            if (local.Success)
            {
                return Ok(new
                {
                    success = true,
                    provider = "ltx-local",
                    message = local.Message,
                    prompt,
                    conditioningPath,
                    local,
                    openAi = (object?)null
                });
            }

            if (request.AllowOpenAiFallback == false)
            {
                return Ok(new
                {
                    success = false,
                    provider = "ltx-local",
                    message = local.Message,
                    prompt,
                    conditioningPath,
                    local,
                    openAi = (object?)null
                });
            }

            var openAi = await TryOpenAiFallbackAsync(prompt, conditioningPath, request.OpenAiModel, cancellationToken);
            if (openAi.Success)
            {
                return Ok(new
                {
                    success = true,
                    provider = "openai-fallback",
                    message = $"Local LTX failed. {openAi.Message}",
                    prompt,
                    conditioningPath,
                    local,
                    openAi
                });
            }

            return Ok(new
            {
                success = false,
                provider = "none",
                message = $"Local LTX failed ({local.Message}). OpenAI fallback failed ({openAi.Message}).",
                prompt,
                conditioningPath,
                local,
                openAi
            });
        }

        private async Task<string> ResolveConditioningPathAsync(GenerateVideoRequest request, CancellationToken cancellationToken)
        {
            var direct = (request.ConditioningPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(direct)) return direct;

            var sourceMaterialId = request.SourceMaterialId.GetValueOrDefault(0);
            if (sourceMaterialId <= 0) return string.Empty;

            var material = await _context.SourceMaterials
                .AsNoTracking()
                .Where(s => s.Id == sourceMaterialId)
                .Select(s => new { s.FilePath, s.Url })
                .FirstOrDefaultAsync(cancellationToken);
            if (material == null) return string.Empty;

            var path = (material.FilePath ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(path) ? path : (material.Url ?? string.Empty).Trim();
        }

        private async Task<LocalExecutionResult> TryRunLocalLtxAsync(string prompt, string conditioningPath, GenerateVideoRequest request, CancellationToken cancellationToken)
        {
            var ltxRoot = ResolveLtxRoot();
            if (string.IsNullOrWhiteSpace(ltxRoot))
            {
                return LocalExecutionResult.Fail("LTX root not found. Expected C:\\ETDP\\LTX-Video or LTX_VIDEO_ROOT.", false);
            }

            var inferenceScript = Path.Combine(ltxRoot, "inference.py");
            if (!System.IO.File.Exists(inferenceScript))
            {
                return LocalExecutionResult.Fail($"inference.py not found in {ltxRoot}.", false, ltxRootPath: ltxRoot);
            }

            if (string.IsNullOrWhiteSpace(conditioningPath))
            {
                return LocalExecutionResult.Fail("Conditioning source path is empty.", false, ltxRootPath: ltxRoot);
            }

            var conditioning = conditioningPath.Trim().Trim('"');
            if (Uri.TryCreate(conditioning, UriKind.Absolute, out var u) &&
                (string.Equals(u.Scheme, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
            {
                return LocalExecutionResult.Fail("Conditioning source is a URL. Local LTX requires a local file path.", false, ltxRootPath: ltxRoot);
            }

            if (!Path.IsPathRooted(conditioning))
            {
                var candidate = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, conditioning));
                if (System.IO.File.Exists(candidate)) conditioning = candidate;
            }
            if (!System.IO.File.Exists(conditioning))
            {
                return LocalExecutionResult.Fail($"Conditioning file not found: {conditioning}", false, ltxRootPath: ltxRoot);
            }

            var pipelineConfig = string.IsNullOrWhiteSpace(request.PipelineConfig) ? "configs/ltxv-13b-0.9.8-distilled.yaml" : request.PipelineConfig.Trim();
            var pipelinePath = Path.IsPathRooted(pipelineConfig) ? pipelineConfig : Path.Combine(ltxRoot, pipelineConfig.Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(pipelinePath))
            {
                return LocalExecutionResult.Fail($"Pipeline config not found: {pipelinePath}", false, ltxRootPath: ltxRoot);
            }

            var pythonExe = ResolvePythonExe(ltxRoot);
            var width = Math.Clamp(request.Width ?? 1216, 256, 4096);
            var height = Math.Clamp(request.Height ?? 704, 256, 4096);
            var numFrames = Math.Clamp(request.NumFrames ?? 121, 8, 512);
            var seed = request.Seed ?? 171198;
            var timeoutSeconds = Math.Clamp(request.TimeoutSeconds ?? 1800, 30, 7200);
            var startedUtc = DateTime.UtcNow;

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                WorkingDirectory = ltxRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(inferenceScript);
            psi.ArgumentList.Add("--prompt");
            psi.ArgumentList.Add(prompt);
            psi.ArgumentList.Add("--conditioning_media_paths");
            psi.ArgumentList.Add(conditioning);
            psi.ArgumentList.Add("--conditioning_start_frames");
            psi.ArgumentList.Add("0");
            psi.ArgumentList.Add("--height");
            psi.ArgumentList.Add(height.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--width");
            psi.ArgumentList.Add(width.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--num_frames");
            psi.ArgumentList.Add(numFrames.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--seed");
            psi.ArgumentList.Add(seed.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--pipeline_config");
            psi.ArgumentList.Add(pipelineConfig);

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return LocalExecutionResult.Fail("Failed to start LTX process.", true, ltxRootPath: ltxRoot, pythonExe: pythonExe);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderrTask = proc.StandardError.ReadToEndAsync(timeout.Token);
                await proc.WaitForExitAsync(timeout.Token);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                var outputPath = FindLatestVideoPath(ltxRoot, startedUtc.AddSeconds(-4));

                if (proc.ExitCode != 0)
                {
                    return LocalExecutionResult.Fail($"LTX exited with code {proc.ExitCode}.", true, proc.ExitCode, stdout, stderr, ltxRoot, pythonExe, outputPath);
                }

                var msg = string.IsNullOrWhiteSpace(outputPath)
                    ? "Local LTX completed, but output video path was not auto-detected."
                    : $"Local LTX completed. Output: {outputPath}";
                return LocalExecutionResult.SuccessResult(msg, proc.ExitCode, stdout, stderr, ltxRoot, pythonExe, outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Local LTX run failed");
                return LocalExecutionResult.Fail($"Local LTX execution failed: {ex.Message}", true, ltxRootPath: ltxRoot, pythonExe: pythonExe);
            }
        }

        private async Task<OpenAiFallbackResult> TryOpenAiFallbackAsync(string prompt, string conditioningPath, string? requestedModel, CancellationToken cancellationToken)
        {
            if (!AiRuntime.AllowOpenAi()) return OpenAiFallbackResult.Fail("OpenAI fallback disabled by AI_MODE.", attempted: false);

            var key = Secrets.GetOpenAIKey();
            if (string.IsNullOrWhiteSpace(key)) return OpenAiFallbackResult.Fail("OPENAI_API_KEY is not configured.", attempted: false);

            var model = string.IsNullOrWhiteSpace(requestedModel) ? (Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini") : requestedModel.Trim();
            var payload = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = "Return practical fallback guidance for educational text-to-video when local generation failed." },
                    new
                    {
                        role = "user",
                        content = $"Local LTX failed. Create a concise fallback plan with a revised prompt, 4-6 scene outline, and next steps.\nPrompt: {prompt}\nConditioning source: {(string.IsNullOrWhiteSpace(conditioningPath) ? "(none)" : conditioningPath)}"
                    }
                },
                temperature = 0.3
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                var resp = await _http.SendAsync(msg, cancellationToken);
                var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    return OpenAiFallbackResult.Fail($"HTTP {(int)resp.StatusCode}. {TrimTail(body, 900)}", attempted: true, model: model);
                }

                var text = TryExtractChatCompletionText(body) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return OpenAiFallbackResult.Fail("OpenAI returned empty content.", attempted: true, model: model);
                }

                return OpenAiFallbackResult.SuccessResult("OpenAI fallback plan generated.", model, text.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI fallback failed");
                return OpenAiFallbackResult.Fail(ex.Message, attempted: true, model: model);
            }
        }

        private string ResolveLtxRoot()
        {
            var env = (Environment.GetEnvironmentVariable("LTX_VIDEO_ROOT") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;

            var parent = Directory.GetParent(_environment.ContentRootPath)?.FullName ?? string.Empty;
            var candidates = new[]
            {
                @"C:\ETDP\LTX-Video",
                Path.Combine(parent, "LTX-Video"),
                Path.Combine(_environment.ContentRootPath, "LTX-Video")
            };
            foreach (var c in candidates) if (Directory.Exists(c)) return c;
            return string.Empty;
        }

        private static string ResolvePythonExe(string ltxRoot)
        {
            var env = (Environment.GetEnvironmentVariable("LTX_PYTHON_EXE") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(env)) return env;

            var candidates = new[]
            {
                Path.Combine(ltxRoot, ".venv312", "Scripts", "python.exe"),
                Path.Combine(ltxRoot, ".venv", "Scripts", "python.exe"),
                Path.Combine(ltxRoot, "venv", "Scripts", "python.exe")
            };
            foreach (var c in candidates) if (System.IO.File.Exists(c)) return c;
            return "python";
        }

        private static string NormalizePrompt(string? prompt)
        {
            return string.Join(" ", (prompt ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string? FindLatestVideoPath(string ltxRoot, DateTime earliestUtc)
        {
            var dirs = new[]
            {
                Path.Combine(ltxRoot, "outputs"),
                Path.Combine(ltxRoot, "output"),
                Path.Combine(ltxRoot, "results"),
                Path.Combine(ltxRoot, "generated")
            };
            var files = new List<FileInfo>();
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                    {
                        if (!VideoExtensions.Contains(Path.GetExtension(file))) continue;
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc >= earliestUtc) files.Add(info);
                    }
                }
                catch { }
            }
            return files.OrderByDescending(x => x.LastWriteTimeUtc).Select(x => x.FullName).FirstOrDefault();
        }

        private static string? TryExtractChatCompletionText(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;
                var first = choices[0];
                if (!first.TryGetProperty("message", out var msg)) return null;
                if (!msg.TryGetProperty("content", out var content)) return null;
                return content.ValueKind switch
                {
                    JsonValueKind.String => content.GetString(),
                    _ => content.GetRawText()
                };
            }
            catch
            {
                return null;
            }
        }

        private static string TrimTail(string? input, int maxChars)
        {
            var s = (input ?? string.Empty).Trim();
            if (s.Length <= maxChars) return s;
            return s.Substring(s.Length - maxChars);
        }

        private static string NormalizeTtsFormat(string? raw)
        {
            var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "aac" => "aac",
                "wav" => "wav",
                "flac" => "flac",
                "opus" => "opus",
                _ => "mp3"
            };
        }

        private static string ResolveTtsContentType(string format)
        {
            return string.Equals(format, "wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav"
                : string.Equals(format, "flac", StringComparison.OrdinalIgnoreCase) ? "audio/flac"
                : string.Equals(format, "aac", StringComparison.OrdinalIgnoreCase) ? "audio/aac"
                : string.Equals(format, "opus", StringComparison.OrdinalIgnoreCase) ? "audio/ogg"
                : "audio/mpeg";
        }

        private static string FirstNonEmpty(params string?[] candidates)
        {
            foreach (var candidate in candidates ?? Array.Empty<string?>())
            {
                var value = (candidate ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string[] ParseCsvOptions(string? raw, params string[] fallbacks)
        {
            var values = (raw ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToList();

            if (values.Count == 0)
            {
                values.AddRange((fallbacks ?? Array.Empty<string>())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim()));
            }

            return values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public sealed class TtsPreviewRequest
        {
            public string? Provider { get; set; }
            public string? Text { get; set; }
            public string? Model { get; set; }
            public string? Voice { get; set; }
            public string? Format { get; set; }
            public double? Speed { get; set; }
        }

        public sealed class GenerateVideoRequest
        {
            public string? Prompt { get; set; }
            public int? SourceMaterialId { get; set; }
            public string? ConditioningPath { get; set; }
            public string? PipelineConfig { get; set; }
            public int? Width { get; set; }
            public int? Height { get; set; }
            public int? NumFrames { get; set; }
            public int? Seed { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? AllowOpenAiFallback { get; set; }
            public string? OpenAiModel { get; set; }
        }

        public sealed class LocalExecutionResult
        {
            public bool Attempted { get; set; }
            public bool Success { get; set; }
            public string Message { get; set; } = string.Empty;
            public int? ExitCode { get; set; }
            public string StdoutTail { get; set; } = string.Empty;
            public string StderrTail { get; set; } = string.Empty;
            public string OutputVideoPath { get; set; } = string.Empty;
            public string LtxRootPath { get; set; } = string.Empty;
            public string PythonExe { get; set; } = string.Empty;

            public static LocalExecutionResult SuccessResult(string message, int? exitCode, string? stdout, string? stderr, string? ltxRootPath, string? pythonExe, string? outputVideoPath)
            {
                return new LocalExecutionResult
                {
                    Attempted = true,
                    Success = true,
                    Message = message,
                    ExitCode = exitCode,
                    StdoutTail = TrimTail(stdout, 2000),
                    StderrTail = TrimTail(stderr, 2000),
                    OutputVideoPath = outputVideoPath ?? string.Empty,
                    LtxRootPath = ltxRootPath ?? string.Empty,
                    PythonExe = pythonExe ?? string.Empty
                };
            }

            public static LocalExecutionResult Fail(string message, bool attempted = true, int? exitCode = null, string? stdout = null, string? stderr = null, string? ltxRootPath = null, string? pythonExe = null, string? outputVideoPath = null)
            {
                return new LocalExecutionResult
                {
                    Attempted = attempted,
                    Success = false,
                    Message = message,
                    ExitCode = exitCode,
                    StdoutTail = TrimTail(stdout, 2000),
                    StderrTail = TrimTail(stderr, 2000),
                    OutputVideoPath = outputVideoPath ?? string.Empty,
                    LtxRootPath = ltxRootPath ?? string.Empty,
                    PythonExe = pythonExe ?? string.Empty
                };
            }
        }

        public sealed class OpenAiFallbackResult
        {
            public bool Attempted { get; set; }
            public bool Success { get; set; }
            public string Model { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string PlanText { get; set; } = string.Empty;

            public static OpenAiFallbackResult SuccessResult(string message, string model, string planText)
            {
                return new OpenAiFallbackResult
                {
                    Attempted = true,
                    Success = true,
                    Model = model ?? string.Empty,
                    Message = message,
                    PlanText = planText ?? string.Empty
                };
            }

            public static OpenAiFallbackResult Fail(string message, bool attempted, string? model = null)
            {
                return new OpenAiFallbackResult
                {
                    Attempted = attempted,
                    Success = false,
                    Model = model ?? string.Empty,
                    Message = message,
                    PlanText = string.Empty
                };
            }
        }
    }
}
