#!/usr/bin/env node
import fs from "node:fs/promises";
import fsSync from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
import { chromium } from "playwright";

function parseArgs(argv) {
    const args = {};
    for (let i = 0; i < argv.length; i += 1) {
        const token = argv[i];
        if (!token.startsWith("--")) continue;

        const key = token.slice(2);
        if (key === "mp4" || key === "narrate" || key === "help" || key === "h") {
            args[key] = true;
            continue;
        }

        const next = argv[i + 1];
        if (!next || next.startsWith("--")) {
            args[key] = true;
            continue;
        }

        args[key] = next;
        i += 1;
    }
    return args;
}

function boolFrom(value, defaultValue = false) {
    if (value === undefined) return defaultValue;
    if (typeof value === "boolean") return value;

    const s = String(value || "").trim().toLowerCase();
    if (["1", "true", "yes", "on"].includes(s)) return true;
    if (["0", "false", "no", "off"].includes(s)) return false;
    return defaultValue;
}

function printUsage() {
    console.log("ETDP walkthrough recorder");
    console.log("");
    console.log("Usage:");
    console.log("  node scripts/video/record-walkthrough.mjs [options]");
    console.log("");
    console.log("Options:");
    console.log("  --url <http://localhost:5173>       Frontend URL to record");
    console.log("  --scenario <path>                   JSON scenario file");
    console.log("  --output <path>                     Explicit output .webm path");
    console.log("  --output-dir <path>                 Output folder (default: artifacts/video)");
    console.log("  --pace <2.0>                        Playback pace factor (2 = ~50% slower)");
    console.log("  --narrate                           Generate voice narration and mix into video");
    console.log("  --voice <name-fragment>             TTS voice match (default: Microsoft Zira)");
    console.log("  --voice-rate <-2>                   TTS rate -10..10");
    console.log("  --mp4                               Also write .mp4 (requires ffmpeg)");
    console.log("  --help                              Show this help");
}

function timestampNow() {
    const now = new Date();
    const p2 = (v) => String(v).padStart(2, "0");
    return `${now.getFullYear()}${p2(now.getMonth() + 1)}${p2(now.getDate())}-${p2(now.getHours())}${p2(now.getMinutes())}${p2(now.getSeconds())}`;
}

function toNumber(value, fallback) {
    const n = Number(value);
    return Number.isFinite(n) ? n : fallback;
}

function scaleMs(value, paceFactor) {
    const ms = Math.max(0, toNumber(value, 0));
    const pace = Math.max(0.1, toNumber(paceFactor, 1));
    return Math.round(ms * pace);
}

function resolveStepUrl(baseUrl, stepPath) {
    if (!stepPath) return baseUrl;
    try {
        return new URL(stepPath, baseUrl).toString();
    } catch {
        return baseUrl;
    }
}

function resolveStepPathname(stepPath) {
    if (!stepPath) return "/";
    try {
        return new URL(stepPath, "http://localhost").pathname || "/";
    } catch {
        return stepPath.startsWith("/") ? stepPath : "/";
    }
}

function getDefaultHighlightSelector(stepPath) {
    const pathname = resolveStepPathname(stepPath);
    const escaped = pathname.replace(/"/g, '\\"');
    return `.dashboard-menu a[href="${escaped}"]`;
}

function buildCaptionText(step, fallbackTitle) {
    const explicit = String(step.caption || "").trim();
    if (explicit) return explicit;
    const note = String(step.note || "").trim();
    if (note) return note;
    return String(fallbackTitle || "").trim();
}

function buildNarrationText(step, fallbackTitle) {
    const explicit = String(step.narration || "").trim();
    if (explicit) return explicit;

    const title = String(fallbackTitle || "").trim();
    const note = String(step.note || "").trim();
    if (title && note) return `${title}. ${note}`;
    return title || note;
}

function toSrtTime(ms) {
    const t = Math.max(0, Math.round(ms));
    const h = Math.floor(t / 3600000);
    const m = Math.floor((t % 3600000) / 60000);
    const s = Math.floor((t % 60000) / 1000);
    const msPart = t % 1000;
    const p2 = (v) => String(v).padStart(2, "0");
    const p3 = (v) => String(v).padStart(3, "0");
    return `${p2(h)}:${p2(m)}:${p2(s)},${p3(msPart)}`;
}

async function writeSrt(timeline, srtPath) {
    const lines = [];
    let index = 1;

    for (const item of timeline) {
        const text = String(item.captionText || "").trim();
        if (!text) continue;

        const start = Math.max(0, Math.round(item.startMs || 0));
        const endRaw = Math.max(start + 800, Math.round(item.endMs || start + 1500));
        const end = Math.max(start + 800, endRaw);

        lines.push(String(index));
        lines.push(`${toSrtTime(start)} --> ${toSrtTime(end)}`);
        lines.push(text);
        lines.push("");
        index += 1;
    }

    await fs.writeFile(srtPath, lines.join("\n"), "utf8");
}

async function safeGoto(page, url) {
    await page.goto(url, { waitUntil: "domcontentloaded", timeout: 120000 });
    await page.waitForTimeout(350);
}

async function showOverlay(page, {
    title,
    note,
    caption,
    step,
    total,
    highlightSelector
}) {
    await page.evaluate((payload) => {
        const overlayId = "__etdp_walkthrough_overlay__";
        const captionId = "__etdp_walkthrough_caption__";
        const guideId = "__etdp_walkthrough_guide__";
        const styleId = "__etdp_walkthrough_style__";

        const oldOverlay = document.getElementById(overlayId);
        if (oldOverlay) oldOverlay.remove();

        const oldCaption = document.getElementById(captionId);
        if (oldCaption) oldCaption.remove();

        const oldGuide = document.getElementById(guideId);
        if (oldGuide) oldGuide.remove();

        if (!document.getElementById(styleId)) {
            const style = document.createElement("style");
            style.id = styleId;
            style.textContent = `
                @keyframes etdpPulseBorder {
                    0% { transform: scale(1); box-shadow: 0 0 0 0 rgba(255, 211, 77, 0.45); }
                    70% { transform: scale(1.01); box-shadow: 0 0 0 12px rgba(255, 211, 77, 0); }
                    100% { transform: scale(1); box-shadow: 0 0 0 0 rgba(255, 211, 77, 0); }
                }
                @keyframes etdpArrowPulse {
                    0%, 100% { transform: translateX(0); opacity: 0.95; }
                    50% { transform: translateX(4px); opacity: 1; }
                }
            `;
            document.head.appendChild(style);
        }

        const host = document.createElement("div");
        host.id = overlayId;
        host.style.position = "fixed";
        host.style.top = "18px";
        host.style.right = "18px";
        host.style.maxWidth = "680px";
        host.style.padding = "16px 18px";
        host.style.borderRadius = "14px";
        host.style.boxShadow = "0 12px 28px rgba(0, 0, 0, 0.25)";
        host.style.background = "rgba(10, 46, 33, 0.93)";
        host.style.color = "#f7faf9";
        host.style.fontFamily = "Segoe UI, Arial, sans-serif";
        host.style.zIndex = "2147483647";
        host.style.pointerEvents = "none";

        const progress = document.createElement("div");
        progress.textContent = `Step ${payload.stepNo} of ${payload.totalSteps}`;
        progress.style.fontSize = "14px";
        progress.style.opacity = "0.88";
        progress.style.marginBottom = "6px";

        const heading = document.createElement("div");
        heading.textContent = payload.titleText || "Walkthrough";
        heading.style.fontSize = "24px";
        heading.style.fontWeight = "700";
        heading.style.lineHeight = "1.2";

        const desc = document.createElement("div");
        desc.textContent = payload.noteText || "";
        desc.style.marginTop = "7px";
        desc.style.fontSize = "17px";
        desc.style.lineHeight = "1.35";
        desc.style.opacity = "0.98";

        host.appendChild(progress);
        host.appendChild(heading);
        host.appendChild(desc);
        document.body.appendChild(host);

        if (payload.captionText) {
            const captionEl = document.createElement("div");
            captionEl.id = captionId;
            captionEl.textContent = payload.captionText;
            captionEl.style.position = "fixed";
            captionEl.style.left = "50%";
            captionEl.style.bottom = "22px";
            captionEl.style.transform = "translateX(-50%)";
            captionEl.style.maxWidth = "min(1220px, calc(100vw - 56px))";
            captionEl.style.padding = "14px 18px";
            captionEl.style.borderRadius = "12px";
            captionEl.style.boxShadow = "0 8px 22px rgba(0, 0, 0, 0.22)";
            captionEl.style.background = "rgba(0, 0, 0, 0.75)";
            captionEl.style.color = "#ffffff";
            captionEl.style.fontFamily = "Segoe UI, Arial, sans-serif";
            captionEl.style.fontSize = "28px";
            captionEl.style.fontWeight = "700";
            captionEl.style.lineHeight = "1.25";
            captionEl.style.textAlign = "center";
            captionEl.style.zIndex = "2147483646";
            captionEl.style.pointerEvents = "none";
            document.body.appendChild(captionEl);
        }

        const selector = String(payload.highlightSelector || "").trim();
        if (!selector) return;

        const target = document.querySelector(selector);
        if (!target) return;

        target.scrollIntoView({ block: "center", inline: "nearest", behavior: "instant" });
        const rect = target.getBoundingClientRect();
        if (!rect || rect.width <= 0 || rect.height <= 0) return;

        const guide = document.createElement("div");
        guide.id = guideId;
        guide.style.position = "fixed";
        guide.style.inset = "0";
        guide.style.pointerEvents = "none";
        guide.style.zIndex = "2147483645";

        const ring = document.createElement("div");
        ring.style.position = "fixed";
        ring.style.left = `${Math.max(4, rect.left - 7)}px`;
        ring.style.top = `${Math.max(4, rect.top - 7)}px`;
        ring.style.width = `${Math.max(12, rect.width + 14)}px`;
        ring.style.height = `${Math.max(12, rect.height + 14)}px`;
        ring.style.border = "4px solid #ffd34d";
        ring.style.borderRadius = "12px";
        ring.style.boxShadow = "0 0 0 4px rgba(0, 0, 0, 0.16)";
        ring.style.animation = "etdpPulseBorder 1.25s ease-in-out infinite";

        const arrow = document.createElement("div");
        arrow.textContent = "➤";
        arrow.style.position = "fixed";
        arrow.style.left = `${Math.max(8, rect.left - 34)}px`;
        arrow.style.top = `${Math.max(8, rect.top + rect.height / 2 - 20)}px`;
        arrow.style.fontSize = "40px";
        arrow.style.color = "#ffd34d";
        arrow.style.textShadow = "0 0 10px rgba(0, 0, 0, 0.45)";
        arrow.style.animation = "etdpArrowPulse 1.1s ease-in-out infinite";

        guide.appendChild(ring);
        guide.appendChild(arrow);
        document.body.appendChild(guide);
    }, {
        titleText: String(title || ""),
        noteText: String(note || ""),
        captionText: String(caption || ""),
        stepNo: Number(step || 1),
        totalSteps: Number(total || 1),
        highlightSelector: String(highlightSelector || "")
    });
}

async function performAction(page, action, paceFactor) {
    const actionWaitMs = scaleMs(700, paceFactor);
    const readName = () => {
        if (typeof action === "string") return action;
        if (action && typeof action === "object") return String(action.type || "");
        return "";
    };

    const name = readName().trim().toLowerCase();
    if (!name) return;

    if (name === "scroll-menu-down") {
        await page.evaluate(() => {
            const nav = document.querySelector(".dashboard-menu nav");
            if (nav) nav.scrollTo({ top: nav.scrollHeight, behavior: "smooth" });
        });
        await page.waitForTimeout(actionWaitMs);
        return;
    }

    if (name === "scroll-menu-top") {
        await page.evaluate(() => {
            const nav = document.querySelector(".dashboard-menu nav");
            if (nav) nav.scrollTo({ top: 0, behavior: "smooth" });
        });
        await page.waitForTimeout(actionWaitMs);
        return;
    }

    if (name === "scroll-main-down") {
        await page.mouse.wheel(0, 2000);
        await page.waitForTimeout(actionWaitMs);
        return;
    }

    if (name === "scroll-main-top") {
        await page.evaluate(() => window.scrollTo({ top: 0, behavior: "smooth" }));
        await page.waitForTimeout(actionWaitMs);
        return;
    }

    if (name === "hover-selector" && action?.selector) {
        await page.locator(String(action.selector)).first().hover({ timeout: 8000 }).catch(() => {});
        await page.waitForTimeout(actionWaitMs);
        return;
    }

    if (name === "click-selector" && action?.selector) {
        await page.locator(String(action.selector)).first().click({ timeout: 8000 }).catch(() => {});
        await page.waitForTimeout(actionWaitMs);
    }
}

function executableInPath(command) {
    const result = spawnSync(command, ["-version"], { stdio: "ignore", shell: process.platform === "win32" });
    return result.status === 0;
}

function localFfmpegBinary(repoRoot) {
    const binaryName = process.platform === "win32" ? "ffmpeg.exe" : "ffmpeg";
    const candidate = path.join(repoRoot, "tools", "ffmpeg", "bin", binaryName);
    return fsSync.existsSync(candidate) ? candidate : "";
}

function runPowershell(args, cwd) {
    const shell = process.platform === "win32" ? "powershell" : "pwsh";
    return spawnSync(shell, ["-ExecutionPolicy", "Bypass", ...args], {
        cwd,
        encoding: "utf8"
    });
}

function resolveFfmpegBinary(repoRoot, autoInstallLocal = false) {
    if (executableInPath("ffmpeg")) return "ffmpeg";

    const local = localFfmpegBinary(repoRoot);
    if (local) return local;

    if (!autoInstallLocal) return "";
    if (process.platform !== "win32") return "";

    const ensureScript = path.join(repoRoot, "scripts", "video", "ensure-ffmpeg.ps1");
    if (!fsSync.existsSync(ensureScript)) return "";

    const result = runPowershell(["-File", ensureScript], repoRoot);
    if (result.status !== 0) return "";

    const stdout = String(result.stdout || "").trim();
    const reportedPath = stdout ? stdout.split(/\r?\n/).map((line) => line.trim()).filter(Boolean).pop() : "";
    if (reportedPath && fsSync.existsSync(reportedPath)) return reportedPath;

    return localFfmpegBinary(repoRoot);
}

function convertToMp4(ffmpegBinary, sourcePath, targetPath) {
    const result = spawnSync(
        ffmpegBinary,
        [
            "-y",
            "-i",
            sourcePath,
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-movflags",
            "+faststart",
            "-c:a",
            "aac",
            "-b:a",
            "192k",
            targetPath
        ],
        { stdio: "inherit", shell: false }
    );

    if (result.status !== 0) {
        throw new Error(`ffmpeg conversion failed with code ${result.status}`);
    }
}

function synthesizeClip(scriptPath, text, output, voice, voiceRate, cwd) {
    const psArgs = [
        "-File", scriptPath,
        "-Text", text,
        "-Output", output,
        "-Voice", voice,
        "-Rate", String(voiceRate)
    ];
    const result = runPowershell(psArgs, cwd);
    if (result.status !== 0) {
        const err = String(result.stderr || "").trim() || String(result.stdout || "").trim() || "Unknown TTS error";
        throw new Error(`Narration synthesis failed: ${err}`);
    }
}

function createNarrationClips(timeline, scriptPath, outputDir, voice, voiceRate, cwd) {
    const clips = [];
    let seq = 1;

    for (const item of timeline) {
        const text = String(item.narrationText || "").trim();
        if (!text) continue;

        const fileName = `narration-${String(seq).padStart(3, "0")}.wav`;
        const clipPath = path.join(outputDir, fileName);
        synthesizeClip(scriptPath, text, clipPath, voice, voiceRate, cwd);

        clips.push({
            path: clipPath,
            delayMs: Math.max(0, Math.round(item.startMs || 0))
        });
        seq += 1;
    }

    return clips;
}

function mergeNarrationIntoVideo(ffmpegBinary, inputVideoPath, clips, outputNarratedPath) {
    if (!clips.length) return;

    const args = ["-y", "-i", inputVideoPath];
    for (const clip of clips) {
        args.push("-i", clip.path);
    }

    const filters = [];
    const mixRefs = [];
    for (let i = 0; i < clips.length; i += 1) {
        const inputIndex = i + 1;
        const delay = Math.max(0, Math.round(clips[i].delayMs || 0));
        const tag = `a${inputIndex}`;
        filters.push(`[${inputIndex}:a]adelay=${delay}|${delay},volume=1.0[${tag}]`);
        mixRefs.push(`[${tag}]`);
    }
    filters.push(`${mixRefs.join("")}amix=inputs=${clips.length}:normalize=0:dropout_transition=0[aout]`);

    args.push(
        "-filter_complex", filters.join(";"),
        "-map", "0:v:0",
        "-map", "[aout]",
        "-c:v", "copy",
        "-c:a", "libopus",
        "-shortest",
        outputNarratedPath
    );

    const result = spawnSync(ffmpegBinary, args, { stdio: "inherit", shell: false });
    if (result.status !== 0) {
        throw new Error(`Narration merge failed with code ${result.status}`);
    }
}

function withSuffix(filePath, suffix, extOverride = "") {
    const parsed = path.parse(filePath);
    const ext = extOverride || parsed.ext;
    return path.join(parsed.dir, `${parsed.name}${suffix}${ext}`);
}

async function loadScenario(scenarioPath) {
    const raw = await fs.readFile(scenarioPath, "utf8");
    const parsed = JSON.parse(raw);
    const steps = Array.isArray(parsed?.steps) ? parsed.steps : [];
    if (steps.length === 0) {
        throw new Error("Scenario has no steps.");
    }
    return {
        name: String(parsed?.name || "ETDP Walkthrough"),
        initialDelayMs: Number(parsed?.initialDelayMs || 1000),
        paceFactor: toNumber(parsed?.paceFactor, 1),
        steps
    };
}

async function main() {
    const args = parseArgs(process.argv.slice(2));
    if (args.help || args.h) {
        printUsage();
        return;
    }

    const thisFile = fileURLToPath(import.meta.url);
    const videoDir = path.dirname(thisFile);
    const repoRoot = path.resolve(videoDir, "..", "..");
    const baseUrl = String(args.url || "http://localhost:5173").trim().replace(/\/+$/, "");
    const scenarioPath = path.resolve(String(args.scenario || path.join(videoDir, "walkthrough.default.json")));
    const outputDir = path.resolve(String(args["output-dir"] || path.join(repoRoot, "artifacts", "video")));
    const outputFile = args.output
        ? path.resolve(String(args.output))
        : path.join(outputDir, `etdp-walkthrough-${timestampNow()}.webm`);

    const shouldConvertMp4 = boolFrom(args.mp4, false);
    const shouldNarrate = boolFrom(args.narrate, false);
    const voice = String(args.voice || "Microsoft Zira").trim();
    const voiceRate = Math.max(-10, Math.min(10, Math.round(toNumber(args["voice-rate"], -2))));

    const videoSize = { width: 1920, height: 1080 };

    await fs.mkdir(outputDir, { recursive: true });
    const scenario = await loadScenario(scenarioPath);
    const paceFactor = Math.max(0.1, toNumber(args.pace, scenario.paceFactor || (shouldNarrate ? 2 : 1)));

    const tmpRecordDir = path.join(outputDir, ".tmp-playwright-video");
    await fs.mkdir(tmpRecordDir, { recursive: true });

    let browser;
    let context;
    let page;
    let pageVideo;
    let narratedOutputPath = "";
    const timeline = [];

    try {
        browser = await chromium.launch({ headless: true });
        context = await browser.newContext({
            viewport: videoSize,
            recordVideo: {
                dir: tmpRecordDir,
                size: videoSize
            }
        });

        page = await context.newPage();
        pageVideo = page.video();

        console.log(`Recording walkthrough: ${scenario.name}`);
        console.log(`Base URL: ${baseUrl}`);
        console.log(`Scenario: ${scenarioPath}`);
        console.log(`Pace factor: ${paceFactor.toFixed(2)}x`);
        if (shouldNarrate) {
            console.log(`Narration: enabled (voice="${voice}", rate=${voiceRate})`);
        }

        const recordingStartMs = Date.now();

        await safeGoto(page, baseUrl);
        const initialDelayMs = scaleMs(scenario.initialDelayMs, paceFactor);
        if (initialDelayMs > 0) {
            await page.waitForTimeout(initialDelayMs);
        }

        for (let i = 0; i < scenario.steps.length; i += 1) {
            const step = scenario.steps[i] || {};
            const title = String(step.title || `Step ${i + 1}`);
            const note = String(step.note || "");
            const stepWaitMs = scaleMs(step.waitMs || 1600, paceFactor);
            const stepPath = String(step.path || "/");
            const targetUrl = resolveStepUrl(baseUrl, stepPath);
            const actions = Array.isArray(step.actions) ? step.actions : [];
            const highlightSelector = String(step.highlightSelector || getDefaultHighlightSelector(stepPath)).trim();
            const captionText = buildCaptionText(step, title);
            const narrationText = buildNarrationText(step, title);

            await safeGoto(page, targetUrl);
            const stepStartMs = Date.now() - recordingStartMs;

            await showOverlay(page, {
                title,
                note,
                caption: captionText,
                step: i + 1,
                total: scenario.steps.length,
                highlightSelector
            });

            for (const action of actions) {
                await performAction(page, action, paceFactor);
            }

            if (stepWaitMs > 0) {
                await page.waitForTimeout(stepWaitMs);
            }

            const stepEndMs = Date.now() - recordingStartMs;
            timeline.push({
                stepNumber: i + 1,
                startMs: stepStartMs,
                endMs: stepEndMs,
                title,
                note,
                captionText,
                narrationText
            });
        }

        await page.waitForTimeout(scaleMs(300, paceFactor));
        await context.close();
        await browser.close();

        const recordedPath = await pageVideo.path();
        await fs.copyFile(recordedPath, outputFile);
        if (path.resolve(recordedPath) !== path.resolve(outputFile)) {
            await fs.unlink(recordedPath).catch(() => {});
        }
        console.log(`Walkthrough saved: ${outputFile}`);

        const srtPath = withSuffix(outputFile, "", ".srt");
        await writeSrt(timeline, srtPath);
        console.log(`Captions saved: ${srtPath}`);

        if (shouldNarrate) {
            const ffmpegBinary = resolveFfmpegBinary(repoRoot, true);
            if (!ffmpegBinary) {
                console.warn("ffmpeg not available. Narration mix skipped.");
            } else {
                const ttsScript = path.join(repoRoot, "scripts", "video", "synthesize-step-audio.ps1");
                const tmpNarrationDir = path.join(outputDir, `.tmp-narration-${timestampNow()}`);
                await fs.mkdir(tmpNarrationDir, { recursive: true });

                try {
                    const clips = createNarrationClips(
                        timeline,
                        ttsScript,
                        tmpNarrationDir,
                        voice,
                        voiceRate,
                        repoRoot
                    );

                    if (!clips.length) {
                        console.warn("No narration clips were generated.");
                    } else {
                        narratedOutputPath = withSuffix(outputFile, "-narrated", ".webm");
                        mergeNarrationIntoVideo(ffmpegBinary, outputFile, clips, narratedOutputPath);
                        console.log(`Narrated video saved: ${narratedOutputPath}`);
                    }
                } finally {
                    await fs.rm(tmpNarrationDir, { recursive: true, force: true }).catch(() => {});
                }
            }
        }

        if (shouldConvertMp4) {
            const ffmpegBinary = resolveFfmpegBinary(repoRoot, true);
            if (!ffmpegBinary) {
                console.warn("ffmpeg not available. Skipping mp4 conversion.");
            } else {
                const sourceForMp4 = narratedOutputPath || outputFile;
                const mp4Path = withSuffix(sourceForMp4, "", ".mp4");
                try {
                    convertToMp4(ffmpegBinary, sourceForMp4, mp4Path);
                    console.log(`MP4 saved: ${mp4Path}`);
                } catch (err) {
                    console.warn(`MP4 conversion failed: ${err?.message || String(err)}`);
                    console.warn("Keeping WEBM output.");
                }
            }
        }
    } catch (error) {
        if (context) {
            try { await context.close(); } catch { }
        }
        if (browser) {
            try { await browser.close(); } catch { }
        }

        const msg = error?.message || String(error);
        if (msg.includes("Executable doesn't exist")) {
            console.error("Playwright browser is missing. Run: npm run video:setup");
        }
        throw error;
    } finally {
        await fs.rm(tmpRecordDir, { recursive: true, force: true }).catch(() => {});
    }
}

main().catch((error) => {
    console.error(error?.stack || error?.message || String(error));
    process.exit(1);
});
