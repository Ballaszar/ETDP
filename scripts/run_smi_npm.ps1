param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CommandArgs
)

$workspace = "E:\ETDP\ETDP\tools\smi-knowledge-env"
if (-not (Test-Path (Join-Path $workspace "package.json"))) {
    throw "SMI Node workspace is missing."
}

if ($CommandArgs.Count -eq 1 -and $CommandArgs[0] -match "\s") {
    $CommandArgs = $CommandArgs[0] -split "\s+"
}

Push-Location $workspace
try {
    & npm @CommandArgs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
