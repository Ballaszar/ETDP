param(
    [Parameter(Mandatory = $true)]
    [string]$ModelPath,
    [string]$LlamaServerPath = "",
    [int]$Port = 8080,
    [int]$ContextSize = 4096,
    [int]$Threads = 8,
    [string]$ApiKey = "",
    [switch]$SetUserEnv,
    [switch]$WrapperFirst
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($LlamaServerPath)) {
    $candidate = Join-Path $PSScriptRoot "..\..\_external\paraphrase\llama.cpp\build\bin\Release\llama-server.exe"
    $LlamaServerPath = [System.IO.Path]::GetFullPath($candidate)
}

if (!(Test-Path $LlamaServerPath)) {
    throw "llama-server not found at '$LlamaServerPath'. Build llama.cpp first or pass -LlamaServerPath."
}

if (!(Test-Path $ModelPath)) {
    throw "Model file not found at '$ModelPath'."
}

$endpoint = "http://127.0.0.1:$Port/v1/chat/completions"

if ($SetUserEnv) {
    [Environment]::SetEnvironmentVariable("PARAPHRASE_WRAPPER_ENDPOINT", $endpoint, "User")
    if (![string]::IsNullOrWhiteSpace($ApiKey)) {
        [Environment]::SetEnvironmentVariable("PARAPHRASE_WRAPPER_API_KEY", $ApiKey, "User")
    }
    if ($WrapperFirst) {
        [Environment]::SetEnvironmentVariable("PARAPHRASE_WRAPPER_PRIORITY", "first", "User")
    }
    Write-Host "Saved user env vars for ETDP backend wrapper integration."
}

$args = @(
    "-m", $ModelPath,
    "--host", "127.0.0.1",
    "--port", "$Port",
    "-c", "$ContextSize",
    "-t", "$Threads"
)

if (![string]::IsNullOrWhiteSpace($ApiKey)) {
    $args += @("--api-key", $ApiKey)
}

Write-Host "Starting llama.cpp wrapper server..."
Write-Host "Endpoint: $endpoint"
Write-Host "Command: `"$LlamaServerPath`" $($args -join ' ')"

& $LlamaServerPath @args
