# ETDP Local Run Guide

## 1. Start backend API

```powershell
cd E:\ETDP\ETDP
dotnet run --launch-profile http
```

Expected API base: `http://localhost:5299`

The automation worker and smoke-test script are now aligned to this same base (`http://localhost:5299/api`).

## 2. Start frontend

```powershell
cd E:\ETDP\ETDP\frontend
npm run dev
```

Expected frontend URL: `http://localhost:5173`

## 3. Verify routing

- Open `http://localhost:5173/`
- Landing page should be **Dashboard** (not auto-redirect to `/main`).
- Qualification capture page is at `http://localhost:5173/main`.
- Open **System Diagnostics** and confirm `Backend Endpoint` shows `http://localhost:5299` as API base.

## 4. If UI still opens old qualification route/state

In browser DevTools console:

```js
localStorage.removeItem('qualificationId');
location.href = '/';
```

## 5. Dev API settings (frontend/.env.development)

```dotenv
VITE_DISABLE_API=false
VITE_API_BASE=/api
VITE_DEV_API_BASE=http://localhost:5299
```

## 6. Optional long-term chat memory (Mem0)

Start Mem0 REST server:

```powershell
cd E:\ETDP\mem0\server
python -m pip install -r requirements.txt
python -m uvicorn main:app --host 127.0.0.1 --port 8000
```

Enable ETDP memory integration (set before starting backend):

```powershell
$env:ETDP_MEMORY_ENABLED="true"
$env:ETDP_MEMORY_BASE_URL="http://127.0.0.1:8000"
$env:ETDP_MEMORY_TOP_K="5"
$env:ETDP_MEMORY_TIMEOUT_SECONDS="3"
```

ETDP `POST /api/Knowledge/chat` will then retrieve memory snippets from Mem0 (`/search`) and persist new turns (`/memories`) per `userId` + `sessionId`.

## 7. Electric Book Export Workflow

- Open `http://localhost:5173/electric-book-export`
- Choose qualification
- Run **Map ETDP -> Electric Book**
- Then run **Trigger Electric Book** (`output`/`export`) to execute `npm run eb` for the mapped book folder

## 8. MS-SWIFT Integration Workflow

- Repo path used by ETDP: `E:\ETDP\ms-swift`
- Open `http://localhost:5173/repo-integration-hub`
- In **ms-swift** panel:
  - Run `version` (or `pip-install`) first
  - Use `sft`, `pt`, `rlhf`, `infer`, etc. with **Extra Args** for your command options
- Local stage notes reference: `E:\ETDP\LlamaFactoryUserguide\LLaMA Factory.MD`

## 9. Open-Sora + ViMax Integration Workflow

- Repo paths used by ETDP:
  - `E:\ETDP\Open-Sora`
  - `E:\ETDP\ViMax`
- Open `http://localhost:5173/repo-integration-hub`
- In **Open-Sora** panel:
  - Run `version` (or `pip-install`) first
  - Use `infer`/`train` with config paths and `extraArgs`
- In **ViMax** panel:
  - Run `version`
  - Run `uv-sync` (or `pip-install`) to prepare dependencies
  - Configure keys in `configs/idea2video.yaml` and `configs/script2video.yaml`
  - Run `idea2video` or `script2video`
