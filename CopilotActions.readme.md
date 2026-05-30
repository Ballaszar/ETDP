# Copilot Actions Log

This file records key actions, decisions, and context for the ETDP solution, so you can resume work or reference progress after a restart.

## Session Log
- Project was developed and built in C:\ETDP (not A:\ETDP as originally specified).
- All EF Core packages were installed and the backend builds successfully.
- Frontend and backend instructions are in development.readme.md.
- All workflow pages have validation and error handling.
- If you encounter errors after restarting, reference the error and file path here for Copilot to continue.
- 2026-03-05 (resume): recovered from interface interruption, rebuilt backend (`dotnet build`) and frontend (`npm run build`), ran `smoke_test_etdp_smi.ps1 -Mode Dev`, and confirmed ETDP + SMi health endpoints are online.
- 2026-03-14 (resume): cleared ETDP/SMi LLM runtime traces while preserving Codex continuity files. Purged SQLite AI memory tables (`SmiConversationArchives`, `SmiSemanticStateSnapshots`), emptied `runtime_logs`, `artifacts/video/logs`, `E:\ETDP\VocationalLLM\data\logs`, `E:\ETDP\VocationalLLM\data\codex_tunnel`, and SMi memory folders under `E:\ETDP\VocationalLLM\data\knowledge_taxonomy\SMI_Log`. Verified that the interrupted AI Agent model replacement was not yet committed in the main chat route: `Controllers/KnowledgeController.cs` still uses `SendLocalChatRequestAsync(...)` with `AiRuntime.GetLocalLlmModel()` / `LOCAL_LLM_MODEL`, and cloud fallback still uses `OPENAI_MODEL` defaulting to `gpt-4o-mini`.
- 2026-03-14 (model replacement): installed Ollama 0.17.7, enabled SMi Ollama autostart, pulled `llama3.1:8b` plus `nomic-embed-text`, switched SMi off `gemma3:4b`, expanded coding corpus roots to ETDP + selected local reference repos, and wired ETDP dev launch profiles to Ollama via `LOCAL_LLM_ENDPOINT=http://127.0.0.1:11434/v1/chat/completions` with `LOCAL_LLM_MODEL=llama3.1:8b`. Updated ETDP cloud fallback defaults in `KnowledgeController.cs` and `SemanticKernelQuestionService.cs` from `gpt-4o-mini` to `gpt-5-mini`. Added persisted progress snapshots in `VocationalLLM/app/coding_corpus.py` and `VocationalLLM/app/playground.py` so `/api/playground/config` stays responsive after broader repo indexing. Manually synced the SMi coding corpus: 5,312 candidates scanned, 5,288 ingested, 24 failed (mostly corrupt/non-extractable files under `Requests`). Verified SMi `/health`, authenticated `/api/playground/config`, `/api/playground/qualia/simulate`, `/api/playground/chat`, direct Ollama `/v1/chat/completions`, and a clean ETDP `dotnet build`.

## How to Use
- After each session, add a short note here about what was done or what needs to be fixed next.
- When you return, reference this file and the error/file path in your prompt to Copilot.

---
**Last action:** Ollama-backed SMi + ETDP local-agent wiring completed and verified; remaining cleanup is optional source-quality work for the 24 failed corpus files under `Requests`.
