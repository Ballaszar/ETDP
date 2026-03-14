param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CommandArgs
)

$pythonExe = "E:\ETDP\ETDP\tools\smi-knowledge-env\.venv\Scripts\python.exe"
if (-not (Test-Path $pythonExe)) {
    throw "SMI Python environment is not bootstrapped. Run bootstrap_smi_knowledge_env.ps1 first."
}

if ($CommandArgs.Count -eq 1 -and $CommandArgs[0] -match "\s") {
    $CommandArgs = $CommandArgs[0] -split "\s+"
}

& $pythonExe @CommandArgs
exit $LASTEXITCODE
