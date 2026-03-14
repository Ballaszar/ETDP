# Moderator 4 Resource And Search Protocol

## 1) Objective
Create one controlled entry point for knowledge search and troubleshooting, using Blob-first ingestion and strict retrieval order.

## 2) Resource Access Paths

### Core API Base
- `ETDP_BACKEND_BASE` example: `http://localhost:5299/api`

### Knowledge Ingestion
- Blob upload + local indexing:
  - `POST /api/Content/upload-material-to-blob`
  - `POST /api/Content/upload-curriculum-to-blob`
- Local/private folder pool ingest:
  - `POST /api/Content/import-local-folder`
- GitHub pool ingest (optional):
  - `POST /api/Content/import-github-repo`
- OpenAIP/OAI ingest:
  - `POST /api/Content/import-oai-pmh`
- Engineering seed ingest:
  - `POST /api/Content/import-engineering-seed`

### Knowledge Discovery / Retrieval
- Pool inventory:
  - `GET /api/Content/knowledge-pools`
- Deterministic paragraph retrieval:
  - `POST /api/Content/search-paragraphs`
- Unified retrieval orchestration:
  - `POST /api/Content/search-azure`
- Moderator/Foundry chat route:
  - `POST /api/Knowledge/chat`
- Flat prioritized export:
  - `POST /api/Content/export-knowledge-flat`

### Diagnostics / Code Navigation
- Recent errors:
  - `GET /api/Diagnostics/recent`
- Error export:
  - `GET /api/Diagnostics/download`
- Code search:
  - `GET /api/Code/search`
- Code file read:
  - `GET /api/Code/read`
- Code file list:
  - `GET /api/Code/list`

## 3) Required Environment Keys

### Blob
- `AZURE_BLOB_CONTAINER_SAS_URL`

### Azure AI Search
- `AZURE_AI_SEARCH_ENDPOINT`
- `AZURE_AI_SEARCH_INDEX`
- `AZURE_AI_SEARCH_KEY`

### Bing
- `BING_SEARCH_KEY`
- `BING_CUSTOM_CONFIG_ID` (optional for custom configuration path)

### OpenAIP/Figshare
- `OPENAIP_TOKEN` (optional for authenticated access)
- `OPENAIP_API_BASE` (default `https://api.figshare.com/v2`)
- `OPENAIP_SEARCH_ENABLED=true|false`

### Foundry/APIM
- `FOUNDRY_APIM_ENDPOINT`
- `FOUNDRY_APIM_SUBSCRIPTION_KEY`
- Optional auth:
  - `AZURE_TENANT_ID`
  - `AZURE_CLIENT_ID`
  - `AZURE_CLIENT_SECRET`
  - `FOUNDRY_BEARER_TOKEN`

### Frontend/API route integrity
- `VITE_API_BASE`
- `CORS_ALLOWED_ORIGINS`

## 4) Strict Search Sequence (Must Follow)

### Step 0: Context Gate
Before any search, require:
- qualification or domain
- subject
- topic or failure mode
- expected output type (lesson plan, RCA insight, code fix, reference)

If missing, stop and request exact fields.

### Step 1: Pool Discovery
- Call `GET /api/Content/knowledge-pools`.
- Confirm pool counts and freshness.
- If pool is empty, trigger ingest before search.

### Step 2: Paragraph-First Local Retrieval
- Call `POST /api/Content/search-paragraphs` with:
  - `knowledgePool` targeted first, then `local_any`.
  - `removeBoilerplate=true`.
- Prefer results with:
  - higher score
  - explicit context match (topic/criteria/failure mode)
  - non-boilerplate snippets.

### Step 3: Unified Retrieval Fallback
If local paragraph hits are weak/insufficient:
- Call `POST /api/Content/search-azure`.
- Use merged providers in this order:
  1. local paragraph-ranked results
  2. Azure AI Search index
  3. Bing Grounding (if your Foundry tool is enabled)
  4. Bing custom/web (if configured)
  5. OpenAIP/Figshare (if enabled)

### Step 4: Provider-Specific Escalation
If still weak:
- Target provider directly (`openaip`, `wikipedia`, `google`) through existing search path.
- Ask user to refine scope rather than broadening blindly.

### Step 5: Evidence Assembly
- Return paragraph-level evidence with source + URL/material ID.
- Never return uncited conclusions for technical claims.

## 5) Protocol Character (Intervention Rules)
- Enforce exact workflow sequence; do not skip Step 0.
- Intervene when user query is too broad or mixes multiple goals.
- Reject destructive operations unless user explicitly confirms.
- Block operations that proceed without qualification/topic context for workflow-critical runs.
- If user asks to bypass sequence, require explicit override phrase:
  - `OVERRIDE_PROTOCOL_AND_PROCEED`

## 6) Autonomous Technical Error Resolution Loop

### Detection
1. Pull recent errors:
   - `GET /api/Diagnostics/recent`
2. Group by:
   - endpoint, status code, stack signature.

### Isolation
3. Locate code references:
   - `GET /api/Code/search?text=<signature>`
4. Read exact files:
   - `GET /api/Code/read?path=<file>`

### Fix
5. Draft patch with smallest safe change.
6. Validate locally:
   - backend: `dotnet build C:\ETDP\ETDP\ETDP.csproj -nologo`
   - frontend: `npm run build` in `C:\ETDP\ETDP\frontend`
7. Re-check failing endpoint.

### Closure
8. Capture result record:
   - issue, root cause, patch summary, validation commands, outcome.

## 7) .NET / React / Vite Baseline Commands
- Backend build: `dotnet build C:\ETDP\ETDP\ETDP.csproj -nologo`
- Backend run: `dotnet run --launch-profile http` (in `C:\ETDP\ETDP`)
- Frontend install: `npm install` (in `C:\ETDP\ETDP\frontend`)
- Frontend dev: `npm run dev`
- Frontend prod check: `npm run build`

## 8) Recommended Operating Mode
1. Ingest all sources to ETDP storage/index first.
2. Use `search-paragraphs` as default retrieval.
3. Use `search-azure` only as fallback/expansion.
4. Export `jsonl` knowledge layer periodically via `export-knowledge-flat`.
5. Run diagnostics loop daily for autonomous hardening.
