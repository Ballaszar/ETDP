using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ETD.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RepoIntegrationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<RepoIntegrationController> _logger;

        public RepoIntegrationController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            ILogger<RepoIntegrationController> logger)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        [HttpGet("catalog")]
        public IActionResult Catalog()
        {
            var workspaceRoot = ResolveWorkspaceRoot();
            var ltxPath = Path.Combine(workspaceRoot, "LTX-Video");
            var hunyuanPath = Path.Combine(workspaceRoot, "HunyuanVideo");
            var slideDeckPath = Path.Combine(workspaceRoot, "slide-deck-ai");
            var paper2SlidesPath = Path.Combine(workspaceRoot, "paper2slides");
            var awesomePath = Path.Combine(workspaceRoot, "Awesome-Text-to-Video-Generation");
            var electricBookPath = ResolveElectricBookPath(workspaceRoot);
            var kjvPath = ResolveKjvBiblePath(workspaceRoot);
            var openSoraPath = ResolveOpenSoraPath(workspaceRoot);
            var viMaxPath = ResolveViMaxPath(workspaceRoot);
            var swiftPath = ResolveSwiftPath(workspaceRoot);
            var langchainPath = ResolveLangchainPath(workspaceRoot);
            var mem0Path = ResolveMem0Path(workspaceRoot);

            return Ok(new
            {
                workspaceRoot,
                repos = new[]
                {
                    RepoStatus("LTX-Video", ltxPath, "High-quality local text/video conditioning generation"),
                    RepoStatus("HunyuanVideo", hunyuanPath, "Tencent Hunyuan text-to-video generation"),
                    RepoStatus("slide-deck-ai", slideDeckPath, "LLM-powered PowerPoint generator"),
                    RepoStatus("paper2slides", paper2SlidesPath, "Generate Beamer slides from arXiv papers"),
                    RepoStatus("Awesome-Text-to-Video-Generation", awesomePath, "Research/paper index for T2V"),
                    RepoStatus("electric-book", electricBookPath, "Multi-format publishing pipeline (web/pdf/epub/Word) for book outputs"),
                    RepoStatus("King-James-Bible-pdf-with-bookmarks", kjvPath, "KJV Bible PDF/HTML source with bookmarks and reading plan assets"),
                    RepoStatus("Open-Sora", openSoraPath, "Open-source text/image-to-video training and inference"),
                    RepoStatus("ViMax", viMaxPath, "Agentic idea/script-to-video orchestration framework"),
                    RepoStatus("ms-swift", swiftPath, "Large model training/fine-tuning/inference framework"),
                    RepoStatus("langchain", langchainPath, "Agent framework and memory orchestration components"),
                    RepoStatus("mem0", mem0Path, "Long-term memory layer and REST API server")
                },
                slideDeckModels = ReadSlideDeckModelKeys(slideDeckPath),
                slideDeckTemplates = ReadSlideDeckTemplateNames(slideDeckPath),
                toolHelp = new Dictionary<string, string[]>
                {
                    ["ltx"] = new[]
                    {
                        "Core: prompt, conditioningPath, pipelineConfig, width, height, numFrames, seed",
                        "Optional: frameRate, negativePrompt, inputMediaPath, outputPath, offloadToCpu",
                        "Advanced: conditioningStrengthsCsv, conditioningStartFramesCsv, imageCondNoiseScale, extraArgs"
                    },
                    ["hunyuan"] = new[]
                    {
                        "Core: prompt, videoHeight, videoWidth, videoLength, inferSteps, savePath",
                        "Optional: modelBase, modelResolution, seed, negPrompt, cfgScale, embeddedCfgScale",
                        "Memory/parallel: flowReverse, useCpuOffload, ulyssesDegree, ringDegree"
                    },
                    ["slideDeckAi"] = new[]
                    {
                        "Operation: list-models or generate",
                        "Generate params: model, topic, apiKey, templateId, outputPath, extraArgs"
                    },
                    ["paper2slides"] = new[]
                    {
                        "Command: all / generate / compile",
                        "Core: query, useLinter, usePdfcrop, noOpen, model, apiKey, verbose, extraArgs"
                    },
                    ["awesome"] = new[]
                    {
                        "Search papers and links from README by keyword",
                        "Params: query, limit"
                    },
                    ["electricBook"] = new[]
                    {
                        "Operation: list-commands, setup, update-modules, check, output, export",
                        "Core options: format, book, language",
                        "Boolean options: incremental, mathjax, debugjs, skipwebpack"
                    },
                    ["swift"] = new[]
                    {
                        "Operation: version, pip-install, sft, pt, rlhf, infer, eval, export, deploy, sample, app, web-ui, run-script",
                        "Use extraArgs for stage/task flags based on your LLaMA Factory workflow notes",
                        "Core options: pythonExe, scriptPath (run-script), timeoutSeconds"
                    },
                    ["openSora"] = new[]
                    {
                        "Operation: version, pip-install, infer, train, run-script",
                        "Infer defaults to scripts/diffusion/inference.py with config t2i2v_256px.py",
                        "Train defaults to scripts/diffusion/train.py with config stage1.py"
                    },
                    ["viMax"] = new[]
                    {
                        "Operation: version, uv-sync, pip-install, idea2video, script2video, run-script",
                        "idea2video runs main_idea2video.py; script2video runs main_script2video.py",
                        "Set API/model keys in configs/idea2video.yaml and configs/script2video.yaml"
                    },
                    ["langchain"] = new[]
                    {
                        "Operation: version, memory-smoke, pip-install, run-script",
                        "Core options: pythonExe, scriptPath (run-script), timeoutSeconds",
                        "Use memory-smoke to validate chat-history memory primitives"
                    },
                    ["mem0"] = new[]
                    {
                        "Operation: version, install-server-deps, serve, run-script",
                        "Core options: pythonExe, host, port, timeoutSeconds",
                        "Use serve to start FastAPI memory server (mem0/server/main.py via uvicorn)"
                    }
                }
            });
        }

        [HttpGet("awesome/search")]
        public IActionResult SearchAwesome([FromQuery] string? query, [FromQuery] int? limit)
        {
            var workspaceRoot = ResolveWorkspaceRoot();
            var readmePath = Path.Combine(workspaceRoot, "Awesome-Text-to-Video-Generation", "README.md");
            if (!System.IO.File.Exists(readmePath))
            {
                return NotFound(new { error = $"Awesome repo README not found: {readmePath}" });
            }

            var q = (query ?? string.Empty).Trim();
            var maxRows = Math.Clamp(limit ?? 50, 1, 300);
            var lines = System.IO.File.ReadAllLines(readmePath);
            var linkRegex = new Regex(@"\[(?<title>[^\]]+)\]\((?<url>https?://[^)\s]+[^)]*)\)", RegexOptions.Compiled);

            var results = new List<object>();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!string.IsNullOrWhiteSpace(q) &&
                    line.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var matches = linkRegex.Matches(line);
                foreach (Match match in matches)
                {
                    var title = (match.Groups["title"].Value ?? string.Empty).Trim();
                    var url = (match.Groups["url"].Value ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(url)) continue;

                    results.Add(new
                    {
                        lineNumber = i + 1,
                        title,
                        url,
                        context = line.Trim()
                    });

                    if (results.Count >= maxRows)
                    {
                        return Ok(new { query = q, count = results.Count, items = results });
                    }
                }
            }

            return Ok(new { query = q, count = results.Count, items = results });
        }

        [HttpPost("run/ltx")]
        public async Task<IActionResult> RunLtx([FromBody] LtxRunRequest? request, CancellationToken cancellationToken)
        {
            request ??= new LtxRunRequest();
            var workspaceRoot = ResolveWorkspaceRoot();
            var repoPath = Path.Combine(workspaceRoot, "LTX-Video");
            if (!Directory.Exists(repoPath)) return NotFound(new { error = $"LTX repo not found: {repoPath}" });

            var prompt = NormalizePrompt(request.Prompt);
            if (string.IsNullOrWhiteSpace(prompt)) return BadRequest(new { error = "Prompt is required." });

            var conditioningPath = await ResolveConditioningPathAsync(request.ConditioningPath, request.SourceMaterialId, cancellationToken);

            var pythonExe = ResolvePythonExe(repoPath, request.PythonExe);
            var scriptPath = Path.Combine(repoPath, "inference.py");
            if (!System.IO.File.Exists(scriptPath)) return NotFound(new { error = $"inference.py not found: {scriptPath}" });

            var args = new List<string>
            {
                scriptPath,
                "--prompt", prompt,
                "--pipeline_config", string.IsNullOrWhiteSpace(request.PipelineConfig) ? "configs/ltxv-13b-0.9.8-distilled.yaml" : request.PipelineConfig!.Trim(),
                "--seed", (request.Seed ?? 171198).ToString(),
                "--height", Math.Clamp(request.Height ?? 704, 256, 4096).ToString(),
                "--width", Math.Clamp(request.Width ?? 1216, 256, 4096).ToString(),
                "--num_frames", Math.Clamp(request.NumFrames ?? 121, 8, 512).ToString(),
                "--frame_rate", Math.Clamp(request.FrameRate ?? 30, 1, 120).ToString()
            };

            if (!string.IsNullOrWhiteSpace(request.OutputPath))
            {
                args.Add("--output_path");
                args.Add(request.OutputPath.Trim());
            }

            if (!string.IsNullOrWhiteSpace(conditioningPath))
            {
                args.Add("--conditioning_media_paths");
                args.Add(conditioningPath);
            }

            var conditioningStrengths = ParseCsv(request.ConditioningStrengthsCsv);
            if (conditioningStrengths.Count > 0)
            {
                args.Add("--conditioning_strengths");
                args.AddRange(conditioningStrengths);
            }

            var conditioningStartFrames = ParseCsv(request.ConditioningStartFramesCsv);
            if (conditioningStartFrames.Count > 0)
            {
                args.Add("--conditioning_start_frames");
                args.AddRange(conditioningStartFrames);
            }

            if (!string.IsNullOrWhiteSpace(request.NegativePrompt))
            {
                args.Add("--negative_prompt");
                args.Add(request.NegativePrompt.Trim());
            }

            if (!string.IsNullOrWhiteSpace(request.InputMediaPath))
            {
                args.Add("--input_media_path");
                args.Add(request.InputMediaPath.Trim());
            }

            if (request.ImageCondNoiseScale.HasValue)
            {
                args.Add("--image_cond_noise_scale");
                args.Add(request.ImageCondNoiseScale.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (request.OffloadToCpu == true)
            {
                args.Add("--offload_to_cpu");
            }

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);

            return await ExecuteToolAsync(
                toolKey: "ltx",
                displayName: "LTX-Video",
                workingDirectory: repoPath,
                fileName: pythonExe,
                args: args,
                timeoutSeconds: request.TimeoutSeconds,
                dryRun: request.DryRun,
                cancellationToken: cancellationToken);
        }

        [HttpPost("run/hunyuan")]
        public async Task<IActionResult> RunHunyuan([FromBody] HunyuanRunRequest? request, CancellationToken cancellationToken)
        {
            request ??= new HunyuanRunRequest();
            var workspaceRoot = ResolveWorkspaceRoot();
            var repoPath = Path.Combine(workspaceRoot, "HunyuanVideo");
            if (!Directory.Exists(repoPath)) return NotFound(new { error = $"HunyuanVideo repo not found: {repoPath}" });

            var prompt = NormalizePrompt(request.Prompt);
            if (string.IsNullOrWhiteSpace(prompt)) return BadRequest(new { error = "Prompt is required." });

            var pythonExe = ResolvePythonExe(repoPath, request.PythonExe);
            var scriptPath = Path.Combine(repoPath, "sample_video.py");
            if (!System.IO.File.Exists(scriptPath)) return NotFound(new { error = $"sample_video.py not found: {scriptPath}" });

            var videoHeight = Math.Clamp(request.VideoHeight ?? 720, 128, 4096);
            var videoWidth = Math.Clamp(request.VideoWidth ?? 1280, 128, 4096);
            var args = new List<string>
            {
                scriptPath,
                "--prompt", prompt,
                "--video-size", videoHeight.ToString(), videoWidth.ToString(),
                "--video-length", Math.Clamp(request.VideoLength ?? 129, 5, 4096).ToString(),
                "--infer-steps", Math.Clamp(request.InferSteps ?? 50, 1, 1000).ToString(),
                "--save-path", string.IsNullOrWhiteSpace(request.SavePath) ? "./results" : request.SavePath!.Trim(),
                "--model-base", string.IsNullOrWhiteSpace(request.ModelBase) ? "ckpts" : request.ModelBase!.Trim(),
                "--model-resolution", string.IsNullOrWhiteSpace(request.ModelResolution) ? "720p" : request.ModelResolution!.Trim()
            };

            if (request.Seed.HasValue)
            {
                args.Add("--seed");
                args.Add(request.Seed.Value.ToString());
            }

            if (!string.IsNullOrWhiteSpace(request.NegPrompt))
            {
                args.Add("--neg-prompt");
                args.Add(request.NegPrompt.Trim());
            }

            if (request.CfgScale.HasValue)
            {
                args.Add("--cfg-scale");
                args.Add(request.CfgScale.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (request.EmbeddedCfgScale.HasValue)
            {
                args.Add("--embedded-cfg-scale");
                args.Add(request.EmbeddedCfgScale.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (request.NumVideos.HasValue && request.NumVideos.Value > 0)
            {
                args.Add("--num-videos");
                args.Add(request.NumVideos.Value.ToString());
            }

            if (request.UlyssesDegree.HasValue && request.UlyssesDegree.Value > 0)
            {
                args.Add("--ulysses-degree");
                args.Add(request.UlyssesDegree.Value.ToString());
            }

            if (request.RingDegree.HasValue && request.RingDegree.Value > 0)
            {
                args.Add("--ring-degree");
                args.Add(request.RingDegree.Value.ToString());
            }

            if (!string.IsNullOrWhiteSpace(request.Model))
            {
                args.Add("--model");
                args.Add(request.Model.Trim());
            }

            if (request.FlowReverse == true) args.Add("--flow-reverse");
            if (request.UseCpuOffload == true) args.Add("--use-cpu-offload");

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);

            return await ExecuteToolAsync(
                toolKey: "hunyuan",
                displayName: "HunyuanVideo",
                workingDirectory: repoPath,
                fileName: pythonExe,
                args: args,
                timeoutSeconds: request.TimeoutSeconds,
                dryRun: request.DryRun,
                cancellationToken: cancellationToken);
        }

        [HttpPost("run/slide-deck-ai")]
        public async Task<IActionResult> RunSlideDeckAi([FromBody] SlideDeckAiRunRequest? request, CancellationToken cancellationToken)
        {
            request ??= new SlideDeckAiRunRequest();
            var workspaceRoot = ResolveWorkspaceRoot();
            var repoPath = Path.Combine(workspaceRoot, "slide-deck-ai");
            if (!Directory.Exists(repoPath)) return NotFound(new { error = $"slide-deck-ai repo not found: {repoPath}" });

            var operation = (request.Operation ?? "generate").Trim().ToLowerInvariant();
            if (operation != "generate" && operation != "list-models")
            {
                return BadRequest(new { error = "Operation must be 'generate' or 'list-models'." });
            }

            var pythonExe = ResolvePythonExe(repoPath, request.PythonExe);
            var args = new List<string> { "-m", "slidedeckai.cli" };

            if (operation == "list-models")
            {
                args.Add("--list-models");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Model)) return BadRequest(new { error = "Model is required for generate operation." });
                if (string.IsNullOrWhiteSpace(request.Topic)) return BadRequest(new { error = "Topic is required for generate operation." });

                args.Add("generate");
                args.Add("--model");
                args.Add(request.Model!.Trim());
                args.Add("--topic");
                args.Add(request.Topic!.Trim());

                if (!string.IsNullOrWhiteSpace(request.ApiKey))
                {
                    args.Add("--api-key");
                    args.Add(request.ApiKey.Trim());
                }

                if (request.TemplateId.HasValue)
                {
                    args.Add("--template-id");
                    args.Add(Math.Max(0, request.TemplateId.Value).ToString());
                }

                if (!string.IsNullOrWhiteSpace(request.OutputPath))
                {
                    args.Add("--output-path");
                    args.Add(request.OutputPath.Trim());
                }
            }

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);

            var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PYTHONPATH"] = ComposePythonPath(Path.Combine(repoPath, "src"))
            };

            return await ExecuteToolAsync(
                toolKey: "slide-deck-ai",
                displayName: "slide-deck-ai",
                workingDirectory: repoPath,
                fileName: pythonExe,
                args: args,
                timeoutSeconds: request.TimeoutSeconds,
                dryRun: request.DryRun,
                environmentVariables: env,
                cancellationToken: cancellationToken);
        }

        [HttpPost("run/paper2slides")]
        public async Task<IActionResult> RunPaper2Slides([FromBody] Paper2SlidesRunRequest? request, CancellationToken cancellationToken)
        {
            request ??= new Paper2SlidesRunRequest();
            var workspaceRoot = ResolveWorkspaceRoot();
            var repoPath = Path.Combine(workspaceRoot, "paper2slides");
            if (!Directory.Exists(repoPath)) return NotFound(new { error = $"paper2slides repo not found: {repoPath}" });

            var command = (request.Command ?? "all").Trim().ToLowerInvariant();
            if (command != "all" && command != "generate" && command != "compile")
            {
                return BadRequest(new { error = "Command must be all, generate, or compile." });
            }

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new { error = "Query is required (arXiv id or search query)." });
            }

            var pythonExe = ResolvePythonExe(repoPath, request.PythonExe);
            var scriptPath = Path.Combine(repoPath, "paper2slides.py");
            if (!System.IO.File.Exists(scriptPath)) return NotFound(new { error = $"paper2slides.py not found: {scriptPath}" });

            var args = new List<string> { scriptPath };
            if (request.Verbose == true) args.Add("-v");
            args.Add(command);
            args.Add(request.Query!.Trim());

            if (command != "compile")
            {
                if (request.UseLinter == true) args.Add("--use_linter");
                if (request.UsePdfcrop == true) args.Add("--use_pdfcrop");
                if (!string.IsNullOrWhiteSpace(request.ApiKey))
                {
                    args.Add("--api_key");
                    args.Add(request.ApiKey.Trim());
                }
                if (!string.IsNullOrWhiteSpace(request.Model))
                {
                    args.Add("--model");
                    args.Add(request.Model.Trim());
                }
            }

            if (command == "all" && request.NoOpen == true)
            {
                args.Add("--no-open");
            }

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);

            return await ExecuteToolAsync(
                toolKey: "paper2slides",
                displayName: "paper2slides",
                workingDirectory: repoPath,
                fileName: pythonExe,
                args: args,
                timeoutSeconds: request.TimeoutSeconds,
                dryRun: request.DryRun,
                cancellationToken: cancellationToken);
        }

        [HttpPost("run/electric-book")]
        public async Task<IActionResult> RunElectricBook([FromBody] ElectricBookRunRequest? request, CancellationToken cancellationToken)
        {
            request ??= new ElectricBookRunRequest();
            var workspaceRoot = ResolveWorkspaceRoot();
            var repoPath = ResolveElectricBookPath(workspaceRoot);
            if (!Directory.Exists(repoPath)) return NotFound(new { error = $"electric-book repo not found: {repoPath}" });

            var operation = (request.Operation ?? "list-commands").Trim().ToLowerInvariant();
            var npmExe = ResolveNpmExe(request.NpmExecutable);
            var args = new List<string>();

            switch (operation)
            {
                case "list-commands":
                    args.Add("run");
                    args.Add("eb");
                    break;
                case "setup":
                    args.Add("run");
                    args.Add("setup");
                    break;
                case "update-modules":
                    args.Add("run");
                    args.Add("update-modules");
                    break;
                case "check":
                    args.Add("run");
                    args.Add("eb");
                    args.Add("--");
                    args.Add("check");
                    break;
                case "output":
                case "export":
                    args.Add("run");
                    args.Add("eb");
                    args.Add("--");
                    args.Add(operation);

                    if (!string.IsNullOrWhiteSpace(request.Format))
                    {
                        args.Add("--format");
                        args.Add(request.Format.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(request.Book))
                    {
                        args.Add("--book");
                        args.Add(request.Book.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(request.Language))
                    {
                        args.Add("--language");
                        args.Add(request.Language.Trim());
                    }

                    if (request.Incremental == true)
                    {
                        args.Add("--incremental");
                    }

                    if (request.MathJax.HasValue)
                    {
                        args.Add("--mathjax");
                        args.Add(request.MathJax.Value ? "true" : "false");
                    }

                    if (request.DebugJs.HasValue)
                    {
                        args.Add("--debugjs");
                        args.Add(request.DebugJs.Value ? "true" : "false");
                    }

                    if (request.SkipWebpack.HasValue)
                    {
                        args.Add("--skipwebpack");
                        args.Add(request.SkipWebpack.Value ? "true" : "false");
                    }
                    break;
                default:
                    return BadRequest(new { error = "Operation must be list-commands, setup, update-modules, check, output, or export." });
            }

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);

            return await ExecuteToolAsync(
                toolKey: "electric-book",
                displayName: "electric-book",
                workingDirectory: repoPath,
                fileName: npmExe,
                args: args,
                timeoutSeconds: request.TimeoutSeconds,
                dryRun: request.DryRun,
                cancellationToken: cancellationToken);
        }

        [HttpPost("run/langchain")]
        public async Task<IActionResult> RunLangchain([FromBody] LangchainRunRequest? request, CancellationToken cancellationToken)
        {
            request ??= new LangchainRunRequest();
            var workspaceRoot = ResolveWorkspaceRoot();
            var repoPath = ResolveLangchainPath(workspaceRoot);
            if (!Directory.Exists(repoPath)) return NotFound(new { error = $"langchain repo not found: {repoPath}" });

            var operation = (request.Operation ?? "version").Trim().ToLowerInvariant();
            var pythonExe = ResolvePythonExe(repoPath, request.PythonExe);
            var args = new List<string>();

            switch (operation)
            {
                case "version":
                    args.Add("-c");
                    args.Add("import json, importlib.util as u, importlib.metadata as m; out={}; out['langchain']='not-installed' if u.find_spec('langchain') is None else m.version('langchain'); out['langchain_core']='not-installed' if u.find_spec('langchain_core') is None else m.version('langchain-core'); print(json.dumps(out))");
                    break;
                case "memory-smoke":
                    args.Add("-c");
                    args.Add("import json; from langchain_core.chat_history import InMemoryChatMessageHistory as H; h=H(); h.add_user_message('Remember that I prefer evening classes.'); h.add_ai_message('Saved.'); h.add_user_message('Also remind me to bring PPE.'); print(json.dumps({'message_count': len(h.messages), 'last_message': h.messages[-1].content if h.messages else ''}))");
                    break;
                case "pip-install":
                    args.Add("-m");
                    args.Add("pip");
                    args.Add("install");
                    args.Add("-e");
                    args.Add(".");
                    break;
                case "run-script":
                    if (string.IsNullOrWhiteSpace(request.ScriptPath))
                    {
                        return BadRequest(new { error = "scriptPath is required for run-script operation." });
                    }
                    var scriptPath = ResolvePathFromRoot(repoPath, request.ScriptPath!);
                    if (!System.IO.File.Exists(scriptPath))
                    {
                        return NotFound(new { error = $"Script file not found: {scriptPath}" });
                    }
                    args.Add(scriptPath);
                    break;
                default:
                    return BadRequest(new { error = "Operation must be version, memory-smoke, pip-install, or run-script." });
            }

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);

            return await ExecuteToolAsync(
                toolKey: "langchain",
                displayName: "langchain",
                workingDirectory: repoPath,
                fileName: pythonExe,
                args: args,
                timeoutSeconds: request.TimeoutSeconds,
                dryRun: request.DryRun,
                cancellationToken: cancellationToken);
        }

        [HttpPost("run/mem0")]
        public async Task<IActionResult> RunMem0([FromBody] Mem0RunRequest? request, CancellationToken cancellationToken)
        {
            request ??= new Mem0RunRequest();
            var workspaceRoot = ResolveWorkspaceRoot();
            var repoPath = ResolveMem0Path(workspaceRoot);
            if (!Directory.Exists(repoPath)) return NotFound(new { error = $"mem0 repo not found: {repoPath}" });

            var operation = (request.Operation ?? "version").Trim().ToLowerInvariant();
            var pythonExe = ResolvePythonExe(repoPath, request.PythonExe);
            var args = new List<string>();
            var workingDirectory = repoPath;

            switch (operation)
            {
                case "version":
                    args.Add("-c");
                    args.Add("import json, importlib.util as u, importlib.metadata as m; out={}; out['mem0']='not-installed' if u.find_spec('mem0') is None else m.version('mem0ai'); print(json.dumps(out))");
                    break;
                case "install-server-deps":
                    workingDirectory = Path.Combine(repoPath, "server");
                    if (!Directory.Exists(workingDirectory))
                    {
                        return NotFound(new { error = $"mem0 server directory not found: {workingDirectory}" });
                    }
                    var reqPath = Path.Combine(workingDirectory, "requirements.txt");
                    if (!System.IO.File.Exists(reqPath))
                    {
                        return NotFound(new { error = $"requirements.txt not found: {reqPath}" });
                    }
                    args.Add("-m");
                    args.Add("pip");
                    args.Add("install");
                    args.Add("-r");
                    args.Add("requirements.txt");
                    break;
                case "serve":
                    workingDirectory = Path.Combine(repoPath, "server");
                    if (!Directory.Exists(workingDirectory))
                    {
                        return NotFound(new { error = $"mem0 server directory not found: {workingDirectory}" });
                    }
                    args.Add("-m");
                    args.Add("uvicorn");
                    args.Add("main:app");
                    args.Add("--host");
                    args.Add(string.IsNullOrWhiteSpace(request.Host) ? "127.0.0.1" : request.Host!.Trim());
                    args.Add("--port");
                    args.Add(Math.Clamp(request.Port ?? 8000, 1, 65535).ToString());
                    break;
                case "run-script":
                    if (string.IsNullOrWhiteSpace(request.ScriptPath))
                    {
                        return BadRequest(new { error = "scriptPath is required for run-script operation." });
                    }
                    var scriptPath = ResolvePathFromRoot(repoPath, request.ScriptPath!);
                    if (!System.IO.File.Exists(scriptPath))
                    {
                        return NotFound(new { error = $"Script file not found: {scriptPath}" });
                    }
                    args.Add(scriptPath);
                    break;
                default:
                    return BadRequest(new { error = "Operation must be version, install-server-deps, serve, or run-script." });
            }

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);

            return await ExecuteToolAsync(
                toolKey: "mem0",
                displayName: "mem0",
                workingDirectory: workingDirectory,
                fileName: pythonExe,
                args: args,
                timeoutSeconds: request.TimeoutSeconds,
                dryRun: request.DryRun,
                cancellationToken: cancellationToken);
        }

        [HttpPost("run/swift")]
        public async Task<IActionResult> RunSwift([FromBody] SwiftRunRequest? request, CancellationToken cancellationToken)
        {
            request ??= new SwiftRunRequest();
            var workspaceRoot = ResolveWorkspaceRoot();
            var repoPath = ResolveSwiftPath(workspaceRoot);
            if (!Directory.Exists(repoPath)) return NotFound(new { error = $"ms-swift repo not found: {repoPath}" });

            var operation = (request.Operation ?? "version").Trim().ToLowerInvariant();
            var pythonExe = ResolvePythonExe(repoPath, request.PythonExe);
            var args = new List<string>();

            switch (operation)
            {
                case "version":
                    args.Add("-c");
                    args.Add(@"import json, importlib.util as u, importlib.metadata as m
out = {'swift_module': u.find_spec('swift') is not None}
try:
    out['ms_swift_version'] = m.version('ms-swift')
except Exception:
    out['ms_swift_version'] = 'not-installed'
print(json.dumps(out))");
                    break;
                case "pip-install":
                    args.Add("-m");
                    args.Add("pip");
                    args.Add("install");
                    args.Add("-e");
                    args.Add(".");
                    break;
                case "sft":
                case "pt":
                case "rlhf":
                case "infer":
                case "eval":
                case "export":
                case "deploy":
                case "sample":
                case "app":
                case "web-ui":
                    args.Add("-m");
                    args.Add("swift.cli.main");
                    args.Add(operation);
                    break;
                case "run-script":
                    if (string.IsNullOrWhiteSpace(request.ScriptPath))
                    {
                        return BadRequest(new { error = "scriptPath is required for run-script operation." });
                    }
                    var scriptPath = ResolvePathFromRoot(repoPath, request.ScriptPath!);
                    if (!System.IO.File.Exists(scriptPath))
                    {
                        return NotFound(new { error = $"Script file not found: {scriptPath}" });
                    }
                    args.Add(scriptPath);
                    break;
                default:
                    return BadRequest(new { error = "Operation must be version, pip-install, sft, pt, rlhf, infer, eval, export, deploy, sample, app, web-ui, or run-script." });
            }

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);

            return await ExecuteToolAsync(
                toolKey: "swift",
                displayName: "ms-swift",
                workingDirectory: repoPath,
                fileName: pythonExe,
                args: args,
                timeoutSeconds: request.TimeoutSeconds,
                dryRun: request.DryRun,
                cancellationToken: cancellationToken);
        }

        [HttpPost("run/opensora")]
        public async Task<IActionResult> RunOpenSora([FromBody] OpenSoraRunRequest? request, CancellationToken cancellationToken)
        {
            request ??= new OpenSoraRunRequest();
            var workspaceRoot = ResolveWorkspaceRoot();
            var repoPath = ResolveOpenSoraPath(workspaceRoot);
            if (!Directory.Exists(repoPath)) return NotFound(new { error = $"Open-Sora repo not found: {repoPath}" });

            var operation = (request.Operation ?? "version").Trim().ToLowerInvariant();
            var pythonExe = ResolvePythonExe(repoPath, request.PythonExe);
            var fileName = pythonExe;
            var args = new List<string>();

            switch (operation)
            {
                case "version":
                    args.Add("-c");
                    args.Add(@"import json, importlib.util as u
out = {
  'opensora_module': u.find_spec('opensora') is not None,
  'train_script_exists': __import__('os').path.exists('scripts/diffusion/train.py'),
  'infer_script_exists': __import__('os').path.exists('scripts/diffusion/inference.py')
}
print(json.dumps(out))");
                    break;
                case "pip-install":
                    args.Add("-m");
                    args.Add("pip");
                    args.Add("install");
                    args.Add("-v");
                    args.Add("-e");
                    args.Add(".");
                    break;
                case "infer":
                {
                    var scriptPath = Path.Combine(repoPath, "scripts", "diffusion", "inference.py");
                    if (!System.IO.File.Exists(scriptPath)) return NotFound(new { error = $"Open-Sora inference script not found: {scriptPath}" });

                    var configRaw = string.IsNullOrWhiteSpace(request.ConfigPath)
                        ? "configs/diffusion/inference/t2i2v_256px.py"
                        : request.ConfigPath!.Trim();
                    var configPath = ResolvePathFromRoot(repoPath, configRaw);
                    if (!System.IO.File.Exists(configPath)) return NotFound(new { error = $"Open-Sora config not found: {configPath}" });

                    fileName = string.IsNullOrWhiteSpace(request.TorchrunExecutable) ? "torchrun" : request.TorchrunExecutable!.Trim();
                    args.Add("--nproc_per_node");
                    args.Add(Math.Clamp(request.NprocPerNode ?? 1, 1, 128).ToString());
                    args.Add("--standalone");
                    args.Add(scriptPath);
                    args.Add(configPath);
                    if (!string.IsNullOrWhiteSpace(request.SaveDir))
                    {
                        args.Add("--save-dir");
                        args.Add(request.SaveDir!.Trim());
                    }
                    if (!string.IsNullOrWhiteSpace(request.Prompt))
                    {
                        args.Add("--prompt");
                        args.Add(request.Prompt!.Trim());
                    }
                    break;
                }
                case "train":
                {
                    var scriptPath = Path.Combine(repoPath, "scripts", "diffusion", "train.py");
                    if (!System.IO.File.Exists(scriptPath)) return NotFound(new { error = $"Open-Sora train script not found: {scriptPath}" });

                    var configRaw = string.IsNullOrWhiteSpace(request.ConfigPath)
                        ? "configs/diffusion/train/stage1.py"
                        : request.ConfigPath!.Trim();
                    var configPath = ResolvePathFromRoot(repoPath, configRaw);
                    if (!System.IO.File.Exists(configPath)) return NotFound(new { error = $"Open-Sora config not found: {configPath}" });

                    fileName = string.IsNullOrWhiteSpace(request.TorchrunExecutable) ? "torchrun" : request.TorchrunExecutable!.Trim();
                    args.Add("--nproc_per_node");
                    args.Add(Math.Clamp(request.NprocPerNode ?? 1, 1, 128).ToString());
                    args.Add(scriptPath);
                    args.Add(configPath);
                    if (!string.IsNullOrWhiteSpace(request.DatasetPath))
                    {
                        args.Add("--dataset.data-path");
                        args.Add(request.DatasetPath!.Trim());
                    }
                    break;
                }
                case "run-script":
                {
                    if (string.IsNullOrWhiteSpace(request.ScriptPath))
                    {
                        return BadRequest(new { error = "scriptPath is required for run-script operation." });
                    }
                    var scriptPath = ResolvePathFromRoot(repoPath, request.ScriptPath!);
                    if (!System.IO.File.Exists(scriptPath))
                    {
                        return NotFound(new { error = $"Script file not found: {scriptPath}" });
                    }
                    args.Add(scriptPath);
                    break;
                }
                default:
                    return BadRequest(new { error = "Operation must be version, pip-install, infer, train, or run-script." });
            }

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);

            return await ExecuteToolAsync(
                toolKey: "opensora",
                displayName: "Open-Sora",
                workingDirectory: repoPath,
                fileName: fileName,
                args: args,
                timeoutSeconds: request.TimeoutSeconds,
                dryRun: request.DryRun,
                cancellationToken: cancellationToken);
        }

        [HttpPost("run/vimax")]
        public async Task<IActionResult> RunViMax([FromBody] ViMaxRunRequest? request, CancellationToken cancellationToken)
        {
            request ??= new ViMaxRunRequest();
            var workspaceRoot = ResolveWorkspaceRoot();
            var repoPath = ResolveViMaxPath(workspaceRoot);
            if (!Directory.Exists(repoPath)) return NotFound(new { error = $"ViMax repo not found: {repoPath}" });

            var operation = (request.Operation ?? "version").Trim().ToLowerInvariant();
            var pythonExe = ResolvePythonExe(repoPath, request.PythonExe);
            var fileName = pythonExe;
            var args = new List<string>();

            switch (operation)
            {
                case "version":
                    args.Add("-c");
                    args.Add(@"import json, os
out = {
  'main_idea2video_exists': os.path.exists('main_idea2video.py'),
  'main_script2video_exists': os.path.exists('main_script2video.py'),
  'idea_config_exists': os.path.exists('configs/idea2video.yaml'),
  'script_config_exists': os.path.exists('configs/script2video.yaml')
}
print(json.dumps(out))");
                    break;
                case "uv-sync":
                    fileName = ResolveUvExe(request.UvExecutable);
                    args.Add("sync");
                    break;
                case "pip-install":
                    args.Add("-m");
                    args.Add("pip");
                    args.Add("install");
                    args.Add("-e");
                    args.Add(".");
                    break;
                case "idea2video":
                    args.Add("main_idea2video.py");
                    break;
                case "script2video":
                    args.Add("main_script2video.py");
                    break;
                case "run-script":
                {
                    if (string.IsNullOrWhiteSpace(request.ScriptPath))
                    {
                        return BadRequest(new { error = "scriptPath is required for run-script operation." });
                    }
                    var scriptPath = ResolvePathFromRoot(repoPath, request.ScriptPath!);
                    if (!System.IO.File.Exists(scriptPath))
                    {
                        return NotFound(new { error = $"Script file not found: {scriptPath}" });
                    }
                    args.Add(scriptPath);
                    break;
                }
                default:
                    return BadRequest(new { error = "Operation must be version, uv-sync, pip-install, idea2video, script2video, or run-script." });
            }

            var extraArgs = SplitCommandLine(request.ExtraArgs);
            if (extraArgs.Count > 0) args.AddRange(extraArgs);

            return await ExecuteToolAsync(
                toolKey: "vimax",
                displayName: "ViMax",
                workingDirectory: repoPath,
                fileName: fileName,
                args: args,
                timeoutSeconds: request.TimeoutSeconds,
                dryRun: request.DryRun,
                cancellationToken: cancellationToken);
        }

        [HttpGet("help/{tool}")]
        public async Task<IActionResult> Help([FromRoute] string tool, [FromQuery] int? timeoutSeconds, CancellationToken cancellationToken)
        {
            var workspaceRoot = ResolveWorkspaceRoot();
            var key = (tool ?? string.Empty).Trim().ToLowerInvariant();

            string? repoPath = null;
            string? fileName = null;
            List<string>? args = null;
            Dictionary<string, string?>? env = null;

            switch (key)
            {
                case "ltx":
                    repoPath = Path.Combine(workspaceRoot, "LTX-Video");
                    fileName = ResolvePythonExe(repoPath, null);
                    args = new List<string> { Path.Combine(repoPath, "inference.py"), "--help" };
                    break;
                case "hunyuan":
                    repoPath = Path.Combine(workspaceRoot, "HunyuanVideo");
                    fileName = ResolvePythonExe(repoPath, null);
                    args = new List<string> { Path.Combine(repoPath, "sample_video.py"), "--help" };
                    break;
                case "slide-deck-ai":
                    repoPath = Path.Combine(workspaceRoot, "slide-deck-ai");
                    fileName = ResolvePythonExe(repoPath, null);
                    args = new List<string> { "-m", "slidedeckai.cli", "--help" };
                    env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PYTHONPATH"] = ComposePythonPath(Path.Combine(repoPath, "src"))
                    };
                    break;
                case "paper2slides":
                    repoPath = Path.Combine(workspaceRoot, "paper2slides");
                    fileName = ResolvePythonExe(repoPath, null);
                    args = new List<string> { Path.Combine(repoPath, "paper2slides.py"), "--help" };
                    break;
                case "electric-book":
                    repoPath = ResolveElectricBookPath(workspaceRoot);
                    fileName = ResolveNpmExe(null);
                    args = new List<string> { "run", "eb" };
                    break;
                case "langchain":
                    repoPath = ResolveLangchainPath(workspaceRoot);
                    fileName = ResolvePythonExe(repoPath, null);
                    args = new List<string>
                    {
                        "-c",
                        "import json, importlib.util as u, importlib.metadata as m; out={}; out['langchain']='not-installed' if u.find_spec('langchain') is None else m.version('langchain'); out['langchain_core']='not-installed' if u.find_spec('langchain_core') is None else m.version('langchain-core'); print(json.dumps(out))"
                    };
                    break;
                case "mem0":
                    repoPath = ResolveMem0Path(workspaceRoot);
                    fileName = ResolvePythonExe(repoPath, null);
                    args = new List<string>
                    {
                        "-c",
                        "import json, importlib.util as u, importlib.metadata as m; out={}; out['mem0']='not-installed' if u.find_spec('mem0') is None else m.version('mem0ai'); print(json.dumps(out))"
                    };
                    break;
                case "swift":
                    repoPath = ResolveSwiftPath(workspaceRoot);
                    fileName = ResolvePythonExe(repoPath, null);
                    args = new List<string> { "-m", "swift.cli.main", "--help" };
                    break;
                case "opensora":
                    repoPath = ResolveOpenSoraPath(workspaceRoot);
                    fileName = ResolvePythonExe(repoPath, null);
                    args = new List<string> { Path.Combine(repoPath, "scripts", "diffusion", "inference.py"), "--help" };
                    break;
                case "vimax":
                    repoPath = ResolveViMaxPath(workspaceRoot);
                    fileName = ResolvePythonExe(repoPath, null);
                    args = new List<string>
                    {
                        "-c",
                        "print('ViMax quick help: run `uv sync`, then `python main_idea2video.py` or `python main_script2video.py` from repo root.')"
                    };
                    break;
                default:
                    return BadRequest(new { error = "Unknown tool. Use ltx, hunyuan, slide-deck-ai, paper2slides, electric-book, swift, opensora, vimax, langchain, or mem0." });
            }

            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            {
                return NotFound(new { error = $"Repo path not found for tool '{tool}'." });
            }

            var result = await RunProcessAsync(
                fileName!,
                args!,
                repoPath,
                timeoutSeconds: timeoutSeconds,
                environmentVariables: env,
                cancellationToken: cancellationToken);

            return Ok(new
            {
                tool = key,
                success = result.Success,
                command = result.Command,
                exitCode = result.ExitCode,
                stdout = result.StdOut,
                stderr = result.StdErr,
                message = result.Message
            });
        }

        private static object RepoStatus(string name, string path, string purpose)
        {
            return new
            {
                name,
                path,
                exists = Directory.Exists(path),
                purpose
            };
        }

        private async Task<IActionResult> ExecuteToolAsync(
            string toolKey,
            string displayName,
            string workingDirectory,
            string fileName,
            List<string> args,
            int? timeoutSeconds,
            bool? dryRun,
            CancellationToken cancellationToken,
            Dictionary<string, string?>? environmentVariables = null)
        {
            var commandString = BuildCommandString(fileName, args);
            if (dryRun == true)
            {
                return Ok(new
                {
                    tool = toolKey,
                    repo = displayName,
                    success = true,
                    dryRun = true,
                    workingDirectory,
                    command = commandString,
                    message = "Dry run only. Command was not executed."
                });
            }

            var result = await RunProcessAsync(
                fileName,
                args,
                workingDirectory,
                timeoutSeconds,
                environmentVariables,
                cancellationToken);

            return Ok(new
            {
                tool = toolKey,
                repo = displayName,
                success = result.Success,
                dryRun = false,
                message = result.Message,
                workingDirectory,
                command = result.Command,
                exitCode = result.ExitCode,
                stdout = result.StdOut,
                stderr = result.StdErr
            });
        }

        private async Task<ProcessResult> RunProcessAsync(
            string fileName,
            List<string> args,
            string workingDirectory,
            int? timeoutSeconds,
            Dictionary<string, string?>? environmentVariables,
            CancellationToken cancellationToken)
        {
            var timeout = Math.Clamp(timeoutSeconds ?? 1800, 10, 7200);
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args) psi.ArgumentList.Add(arg);
            if (environmentVariables != null)
            {
                foreach (var kv in environmentVariables)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                    psi.Environment[kv.Key] = kv.Value ?? string.Empty;
                }
            }

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return ProcessResult.Fail("Failed to start process.", BuildCommandString(fileName, args));
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeout));

                var stdOutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
                var stdErrTask = proc.StandardError.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token);

                var stdout = await stdOutTask;
                var stderr = await stdErrTask;
                var success = proc.ExitCode == 0;

                return new ProcessResult
                {
                    Success = success,
                    ExitCode = proc.ExitCode,
                    Command = BuildCommandString(fileName, args),
                    StdOut = TrimOutput(stdout),
                    StdErr = TrimOutput(stderr),
                    Message = success ? "Process completed successfully." : $"Process exited with code {proc.ExitCode}."
                };
            }
            catch (OperationCanceledException)
            {
                return ProcessResult.Fail($"Process timed out after {timeout} seconds.", BuildCommandString(fileName, args));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Repo tool process execution failed");
                return ProcessResult.Fail(ex.Message, BuildCommandString(fileName, args));
            }
        }

        private string ResolveWorkspaceRoot()
        {
            var fromEnv = (Environment.GetEnvironmentVariable("ETDP_WORKSPACE_ROOT") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            {
                return fromEnv;
            }

            var parent = Directory.GetParent(_environment.ContentRootPath)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                return parent;
            }

            return _environment.ContentRootPath;
        }

        private string ResolveElectricBookPath(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(workspaceRoot, "electric-book"),
                Path.Combine(workspaceRoot, "_external", "electric-book"),
                Path.Combine(_environment.ContentRootPath, "_external", "electric-book"),
                Path.Combine(workspaceRoot, "ETDP", "_external", "electric-book")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        private string ResolveKjvBiblePath(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(workspaceRoot, "King-James-Bible-pdf-with-bookmarks"),
                Path.Combine(workspaceRoot, "_external", "King-James-Bible-pdf-with-bookmarks"),
                Path.Combine(_environment.ContentRootPath, "_external", "King-James-Bible-pdf-with-bookmarks"),
                Path.Combine(workspaceRoot, "ETDP", "_external", "King-James-Bible-pdf-with-bookmarks")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        private string ResolveSwiftPath(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(workspaceRoot, "ms-swift"),
                Path.Combine(workspaceRoot, "_external", "ms-swift"),
                Path.Combine(_environment.ContentRootPath, "_external", "ms-swift"),
                Path.Combine(workspaceRoot, "ETDP", "_external", "ms-swift")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        private string ResolveOpenSoraPath(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(workspaceRoot, "Open-Sora"),
                Path.Combine(workspaceRoot, "_external", "Open-Sora"),
                Path.Combine(_environment.ContentRootPath, "_external", "Open-Sora"),
                Path.Combine(workspaceRoot, "ETDP", "_external", "Open-Sora")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        private string ResolveViMaxPath(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(workspaceRoot, "ViMax"),
                Path.Combine(workspaceRoot, "_external", "ViMax"),
                Path.Combine(_environment.ContentRootPath, "_external", "ViMax"),
                Path.Combine(workspaceRoot, "ETDP", "_external", "ViMax")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        private string ResolveLangchainPath(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(workspaceRoot, "langchain"),
                Path.Combine(workspaceRoot, "_external", "langchain"),
                Path.Combine(_environment.ContentRootPath, "_external", "langchain"),
                Path.Combine(workspaceRoot, "ETDP", "_external", "langchain")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        private string ResolveMem0Path(string workspaceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(workspaceRoot, "mem0"),
                Path.Combine(workspaceRoot, "_external", "mem0"),
                Path.Combine(_environment.ContentRootPath, "_external", "mem0"),
                Path.Combine(workspaceRoot, "ETDP", "_external", "mem0")
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        private static string ResolveNpmExe(string? explicitPath)
        {
            var direct = (explicitPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npm.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npm.exe")
            };

            foreach (var candidate in candidates)
            {
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
        }

        private static string ResolveUvExe(string? explicitPath)
        {
            var direct = (explicitPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            return OperatingSystem.IsWindows() ? "uv.exe" : "uv";
        }

        private static string ResolvePythonExe(string repoPath, string? explicitPath)
        {
            var fromRequest = (explicitPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(fromRequest))
            {
                return fromRequest;
            }

            var envPython = (Environment.GetEnvironmentVariable("REPO_TOOL_PYTHON_EXE") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(envPython))
            {
                return envPython;
            }

            var candidates = new[]
            {
                Path.Combine(repoPath, "server", ".venv312", "Scripts", "python.exe"),
                Path.Combine(repoPath, "server", ".venv", "Scripts", "python.exe"),
                Path.Combine(repoPath, "server", "venv", "Scripts", "python.exe"),
                Path.Combine(repoPath, ".venv312", "Scripts", "python.exe"),
                Path.Combine(repoPath, ".venv", "Scripts", "python.exe"),
                Path.Combine(repoPath, "venv", "Scripts", "python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312", "python.exe"),
                @"C:\Python312\python.exe",
                @"C:\Python314\python.exe"
            };

            foreach (var candidate in candidates)
            {
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "python";
        }

        private static string NormalizePrompt(string? prompt)
        {
            return string.Join(
                " ",
                (prompt ?? string.Empty)
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static List<string> ParseCsv(string? csv)
        {
            return (csv ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static string BuildCommandString(string fileName, List<string> args)
        {
            static string Q(string v)
            {
                if (string.IsNullOrWhiteSpace(v)) return "\"\"";
                return v.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
                    ? $"\"{v.Replace("\"", "\\\"")}\""
                    : v;
            }

            return string.Join(" ", new[] { Q(fileName) }.Concat(args.Select(Q)));
        }

        private static string TrimOutput(string? text, int maxChars = 12000)
        {
            var value = (text ?? string.Empty).Trim();
            if (value.Length <= maxChars) return value;
            return value.Substring(value.Length - maxChars);
        }

        private static List<string> SplitCommandLine(string? commandLine)
        {
            var raw = (commandLine ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

            var result = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < raw.Length; i++)
            {
                var ch = raw[i];
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(ch))
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }

            return result;
        }

        private async Task<string> ResolveConditioningPathAsync(string? requestPath, int? sourceMaterialId, CancellationToken cancellationToken)
        {
            var direct = (requestPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            var id = sourceMaterialId.GetValueOrDefault(0);
            if (id <= 0) return string.Empty;

            var material = await _context.SourceMaterials
                .AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.FilePath, s.Url })
                .FirstOrDefaultAsync(cancellationToken);

            if (material == null) return string.Empty;
            var filePath = (material.FilePath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(filePath)) return filePath;
            return (material.Url ?? string.Empty).Trim();
        }

        private static string ComposePythonPath(string preferred)
        {
            var existing = Environment.GetEnvironmentVariable("PYTHONPATH");
            if (string.IsNullOrWhiteSpace(existing))
            {
                return preferred;
            }

            var sep = Path.PathSeparator.ToString();
            if (existing.Split(Path.PathSeparator).Any(x => string.Equals(x.Trim(), preferred, StringComparison.OrdinalIgnoreCase)))
            {
                return existing;
            }

            return preferred + sep + existing;
        }

        private static string ResolvePathFromRoot(string root, string input)
        {
            var raw = (input ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            if (Path.IsPathRooted(raw)) return raw;
            return Path.GetFullPath(Path.Combine(root, raw));
        }

        private static List<string> ReadSlideDeckModelKeys(string slideDeckRepoPath)
        {
            var configPath = Path.Combine(slideDeckRepoPath, "src", "slidedeckai", "global_config.py");
            if (!System.IO.File.Exists(configPath)) return new List<string>();

            var text = System.IO.File.ReadAllText(configPath);
            var matches = Regex.Matches(text, @"'(\[[^\]]+\][^']+)'\s*:\s*\{");
            return matches
                .Select(m => (m.Groups[1].Value ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> ReadSlideDeckTemplateNames(string slideDeckRepoPath)
        {
            var configPath = Path.Combine(slideDeckRepoPath, "src", "slidedeckai", "global_config.py");
            if (!System.IO.File.Exists(configPath)) return new List<string>();

            var text = System.IO.File.ReadAllText(configPath);
            var matches = Regex.Matches(text, @"'([^']+)'\s*:\s*\{\s*[\r\n]+\s*'file'\s*:");
            return matches
                .Select(m => (m.Groups[1].Value ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("[", StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public sealed class LtxRunRequest
        {
            public string? Prompt { get; set; }
            public int? SourceMaterialId { get; set; }
            public string? ConditioningPath { get; set; }
            public string? PipelineConfig { get; set; }
            public int? Width { get; set; }
            public int? Height { get; set; }
            public int? NumFrames { get; set; }
            public int? FrameRate { get; set; }
            public int? Seed { get; set; }
            public bool? OffloadToCpu { get; set; }
            public string? NegativePrompt { get; set; }
            public string? InputMediaPath { get; set; }
            public double? ImageCondNoiseScale { get; set; }
            public string? ConditioningStrengthsCsv { get; set; }
            public string? ConditioningStartFramesCsv { get; set; }
            public string? OutputPath { get; set; }
            public string? ExtraArgs { get; set; }
            public string? PythonExe { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; }
        }

        public sealed class HunyuanRunRequest
        {
            public string? Prompt { get; set; }
            public string? Model { get; set; }
            public string? ModelBase { get; set; }
            public string? ModelResolution { get; set; }
            public int? VideoHeight { get; set; }
            public int? VideoWidth { get; set; }
            public int? VideoLength { get; set; }
            public int? InferSteps { get; set; }
            public int? Seed { get; set; }
            public string? NegPrompt { get; set; }
            public double? CfgScale { get; set; }
            public double? EmbeddedCfgScale { get; set; }
            public int? NumVideos { get; set; }
            public bool? FlowReverse { get; set; }
            public bool? UseCpuOffload { get; set; }
            public int? UlyssesDegree { get; set; }
            public int? RingDegree { get; set; }
            public string? SavePath { get; set; }
            public string? ExtraArgs { get; set; }
            public string? PythonExe { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; }
        }

        public sealed class SlideDeckAiRunRequest
        {
            public string? Operation { get; set; }
            public string? Model { get; set; }
            public string? Topic { get; set; }
            public string? ApiKey { get; set; }
            public int? TemplateId { get; set; }
            public string? OutputPath { get; set; }
            public string? ExtraArgs { get; set; }
            public string? PythonExe { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; }
        }

        public sealed class Paper2SlidesRunRequest
        {
            public string? Command { get; set; }
            public string? Query { get; set; }
            public bool? UseLinter { get; set; }
            public bool? UsePdfcrop { get; set; }
            public bool? NoOpen { get; set; }
            public string? ApiKey { get; set; }
            public string? Model { get; set; }
            public bool? Verbose { get; set; }
            public string? ExtraArgs { get; set; }
            public string? PythonExe { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; }
        }

        public sealed class ElectricBookRunRequest
        {
            public string? Operation { get; set; }
            public string? Format { get; set; }
            public string? Book { get; set; }
            public string? Language { get; set; }
            public bool? Incremental { get; set; }
            public bool? MathJax { get; set; }
            public bool? DebugJs { get; set; }
            public bool? SkipWebpack { get; set; }
            public string? ExtraArgs { get; set; }
            public string? NpmExecutable { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; }
        }

        public sealed class SwiftRunRequest
        {
            public string? Operation { get; set; }
            public string? ScriptPath { get; set; }
            public string? ExtraArgs { get; set; }
            public string? PythonExe { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; }
        }

        public sealed class OpenSoraRunRequest
        {
            public string? Operation { get; set; }
            public string? ConfigPath { get; set; }
            public string? DatasetPath { get; set; }
            public string? Prompt { get; set; }
            public string? SaveDir { get; set; }
            public int? NprocPerNode { get; set; }
            public string? TorchrunExecutable { get; set; }
            public string? ScriptPath { get; set; }
            public string? ExtraArgs { get; set; }
            public string? PythonExe { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; }
        }

        public sealed class ViMaxRunRequest
        {
            public string? Operation { get; set; }
            public string? ScriptPath { get; set; }
            public string? ExtraArgs { get; set; }
            public string? PythonExe { get; set; }
            public string? UvExecutable { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; }
        }

        public sealed class LangchainRunRequest
        {
            public string? Operation { get; set; }
            public string? ScriptPath { get; set; }
            public string? ExtraArgs { get; set; }
            public string? PythonExe { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; }
        }

        public sealed class Mem0RunRequest
        {
            public string? Operation { get; set; }
            public string? Host { get; set; }
            public int? Port { get; set; }
            public string? ScriptPath { get; set; }
            public string? ExtraArgs { get; set; }
            public string? PythonExe { get; set; }
            public int? TimeoutSeconds { get; set; }
            public bool? DryRun { get; set; }
        }

        private sealed class ProcessResult
        {
            public bool Success { get; set; }
            public int? ExitCode { get; set; }
            public string Command { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string StdOut { get; set; } = string.Empty;
            public string StdErr { get; set; } = string.Empty;

            public static ProcessResult Fail(string message, string command)
            {
                return new ProcessResult
                {
                    Success = false,
                    ExitCode = null,
                    Command = command,
                    Message = message,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                };
            }
        }
    }
}
