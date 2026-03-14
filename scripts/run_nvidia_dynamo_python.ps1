param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CommandArgs
)

$pythonExe = "E:\ETDP\ETDP\tools\nvidia-dynamo-env\.venv\Scripts\python.exe"
if (-not (Test-Path $pythonExe)) {
    throw "NVIDIA Dynamo Python environment is not bootstrapped."
}

if ($CommandArgs.Count -eq 1 -and $CommandArgs[0] -match "\s") {
    $CommandArgs = $CommandArgs[0] -split "\s+"
}

& $pythonExe @CommandArgs
exit $LASTEXITCODE
