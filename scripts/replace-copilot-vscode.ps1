param(
    [Parameter(Mandatory = $true)]
    [string]$OpenAIApiKey,

    [string]$Model = "gpt-4.1",

    [switch]$KeepCopilotDisabledOnly
)

$ErrorActionPreference = "Stop"

function Require-Command($name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "Required command '$name' not found. Install VS Code and ensure 'code' is on PATH."
    }
}

Require-Command "code"

Write-Host "Uninstalling GitHub Copilot extensions (if installed)..."
$copilotExtensions = @(
    "GitHub.copilot",
    "GitHub.copilot-chat"
)

foreach ($ext in $copilotExtensions) {
    try {
        code --uninstall-extension $ext | Out-Null
        Write-Host "Removed: $ext"
    }
    catch {
        Write-Host "Not present or could not remove: $ext"
    }
}

if ($KeepCopilotDisabledOnly) {
    Write-Host "Done. Copilot removed/disabled only."
    exit 0
}

Write-Host "Installing Continue extension..."
code --install-extension continue.continue --force | Out-Null

$continueDir = Join-Path $env:USERPROFILE ".continue"
if (-not (Test-Path $continueDir)) {
    New-Item -ItemType Directory -Path $continueDir | Out-Null
}

$configPath = Join-Path $continueDir "config.json"

$config = @{
    models = @(
        @{
            title = "OpenAI"
            provider = "openai"
            model = $Model
            apiKey = $OpenAIApiKey
        }
    )
    tabAutocompleteModel = @{
        title = "OpenAI"
        provider = "openai"
        model = $Model
        apiKey = $OpenAIApiKey
    }
} | ConvertTo-Json -Depth 10

$config | Set-Content -Path $configPath -Encoding UTF8

Write-Host "Done. Continue is configured at: $configPath"
Write-Host "Restart VS Code and open Continue chat/autocomplete."