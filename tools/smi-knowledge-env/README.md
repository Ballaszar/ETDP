# SMI Knowledge Environment

This folder holds the isolated runtime for SMI knowledge tooling.

## Purpose

- Keep Python and Node package installs local to ETDP.
- Avoid polluting the global machine environment.
- Generate package/version summaries that can be imported into the ETDP knowledge system.

## Layout

- `.venv/`
  - Local Python 3.12 virtual environment.
- `package.json`
  - Local Node workspace for the requested JS packages.
- `python-requirements.txt`
  - Local Python package list for SMI tooling.

## Bootstrap

Use [bootstrap_smi_knowledge_env.ps1](E:/ETDP/ETDP/scripts/bootstrap_smi_knowledge_env.ps1).

That script will:

1. Create a Python 3.12 virtual environment.
2. Install the Python packages into the local venv.
3. Install the Node packages into the local workspace.
4. Write environment reports back into `Imports/SMIKnowledge/manifests`.

## Helper Wrappers

- [run_smi_python.ps1](E:/ETDP/ETDP/scripts/run_smi_python.ps1)
- [run_smi_npm.ps1](E:/ETDP/ETDP/scripts/run_smi_npm.ps1)
