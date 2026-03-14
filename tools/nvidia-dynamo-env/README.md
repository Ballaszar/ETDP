# NVIDIA Dynamo Env

This folder holds an isolated Python environment for NVIDIA Dynamo and TRT-LLM related experiments.

## Current State

- Environment path: `E:\ETDP\ETDP\tools\nvidia-dynamo-env\.venv`
- Python: `3.12.10`
- GPU detected: `NVIDIA GeForce RTX 4050 Laptop GPU`
- Driver / CUDA reported by `nvidia-smi`: `595.71 / 13.2`

## Attempted Install

```powershell
& E:\ETDP\ETDP\tools\nvidia-dynamo-env\.venv\Scripts\python.exe -m pip install --pre --extra-index-url https://pypi.nvidia.com "ai-dynamo[trtllm]"
```

## Result

Install failed on this machine because `ai-dynamo-runtime` has no matching distribution for the current environment.

Confirmed with:

```powershell
& E:\ETDP\ETDP\tools\nvidia-dynamo-env\.venv\Scripts\python.exe -m pip index versions ai-dynamo-runtime --extra-index-url https://pypi.nvidia.com
```

Output:

```text
ERROR: No matching distribution found for ai-dynamo-runtime
```

## Practical Meaning

This is not an OpenAI key problem.

This is not a missing GPU problem.

This is an environment compatibility problem, most likely because the required NVIDIA runtime wheel is not published for this Windows setup. In practice, these TRT-LLM stacks are commonly Linux-first.

## Safe Next Step

If you want a real working Dynamo / TRT-LLM install, the likely path is:

1. Ubuntu or WSL2 Linux
2. NVIDIA drivers and CUDA aligned to the package support matrix
3. A fresh Python environment there
4. Re-run the same `pip install --pre --extra-index-url https://pypi.nvidia.com "ai-dynamo[trtllm]"`

## Wrapper

Use this PowerShell wrapper to run Python inside this env:

```powershell
powershell -ExecutionPolicy Bypass -File E:\ETDP\ETDP\scripts\run_nvidia_dynamo_python.ps1 --version
```
