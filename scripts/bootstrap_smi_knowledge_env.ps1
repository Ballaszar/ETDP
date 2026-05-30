param(
    [string]$EnvRoot = "$(Split-Path $PSScriptRoot -Parent)\tools\smi-knowledge-env",
    [string]$KnowledgeRoot = "$(Split-Path $PSScriptRoot -Parent)\Imports\SMIKnowledge",
    [string]$PythonSelector = "-3.12",
    [switch]$ImportToEtdp,
    [string]$ApiBase = "http://localhost:5299/api"
)

$ErrorActionPreference = "Stop"

function New-OrUpdatePythonEnv {
    param(
        [string]$Root,
        [string]$Selector
    )

    $venvPath = Join-Path $Root ".venv"
    $requirementsPath = Join-Path $Root "python-requirements.txt"
    if (-not (Test-Path $venvPath)) {
        & py $Selector -m venv $venvPath
        if ($LASTEXITCODE -ne 0) {
            throw ("Failed to create Python virtual environment with selector {0}" -f $Selector)
        }
    }

    $pythonExe = Join-Path $venvPath "Scripts\python.exe"
    if (-not (Test-Path $pythonExe)) {
        throw ("Python executable not found in venv: {0}" -f $pythonExe)
    }

    & $pythonExe -m pip install --upgrade pip setuptools wheel | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upgrade pip/setuptools/wheel in SMI venv."
    }

    & $pythonExe -m pip install -r $requirementsPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install Python requirements for SMI environment."
    }

    return $pythonExe
}

function New-OrUpdateNodeEnv {
    param(
        [string]$Root
    )

    Push-Location $Root
    try {
        npm install
        if ($LASTEXITCODE -ne 0) {
            throw "npm install failed for SMI environment."
        }
    }
    finally {
        Pop-Location
    }
}

function Get-PythonPackageReport {
    param(
        [string]$PythonExe
    )

    $raw = & $PythonExe -m pip list --format json
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to collect Python package report."
    }
    return $raw | ConvertFrom-Json
}

function Get-NodePackageReport {
    param(
        [string]$Root
    )

    Push-Location $Root
    try {
        $raw = npm ls --depth=0 --json
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to collect Node package report."
        }
        return $raw | ConvertFrom-Json
    }
    finally {
        Pop-Location
    }
}

function Write-EnvironmentArtifacts {
    param(
        [string]$EnvRootPath,
        [string]$KnowledgeRootPath,
        [string]$PythonExe,
        [object[]]$PythonPackages,
        [object]$NodePackages
    )

    $manifestsPath = Join-Path $KnowledgeRootPath "manifests"
    New-Item -ItemType Directory -Force -Path $manifestsPath | Out-Null

    $reportPath = Join-Path $manifestsPath "smi-environment-report.json"
    $indexPath = Join-Path $manifestsPath "SMI_ENVIRONMENT_INDEX.md"

    $dependencies = @()
    if ($NodePackages -and $NodePackages.dependencies) {
        $NodePackages.dependencies.PSObject.Properties | ForEach-Object {
            $dependencies += [pscustomobject]@{
                name = $_.Name
                version = $_.Value.version
            }
        }
    }

    $report = [pscustomobject]@{
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        envRoot = $EnvRootPath
        knowledgeRoot = $KnowledgeRootPath
        python = [pscustomobject]@{
            executable = $PythonExe
            version = (& $PythonExe --version) -join ""
            packages = @($PythonPackages | Sort-Object name)
        }
        node = [pscustomobject]@{
            nodeVersion = (node -v)
            npmVersion = (npm -v)
            workspace = $EnvRootPath
            packages = @($dependencies | Sort-Object name)
        }
    }

    $report | ConvertTo-Json -Depth 8 | Set-Content -Path $reportPath -Encoding UTF8

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# SMI Environment Index")
    $lines.Add("")
    $lines.Add("This file summarizes the isolated Python and Node runtime prepared for SMI.")
    $lines.Add("")
    $lines.Add("## Python")
    $lines.Add("")
    $lines.Add(("- executable: {0}" -f $PythonExe))
    $lines.Add(("- version: {0}" -f ((& $PythonExe --version) -join "")))
    $lines.Add("")
    $lines.Add("### Installed Python Packages")
    $lines.Add("")
    foreach ($pkg in ($PythonPackages | Sort-Object name)) {
        $lines.Add(("- {0} {1}" -f $pkg.name, $pkg.version))
    }
    $lines.Add("")
    $lines.Add("## Node")
    $lines.Add("")
    $lines.Add(("- node: {0}" -f (node -v)))
    $lines.Add(("- npm: {0}" -f (npm -v)))
    $lines.Add(("- workspace: {0}" -f $EnvRootPath))
    $lines.Add("")
    $lines.Add("### Installed Node Packages")
    $lines.Add("")
    foreach ($pkg in ($dependencies | Sort-Object name)) {
        $lines.Add(("- {0} {1}" -f $pkg.name, $pkg.version))
    }
    $lines.Add("")
    $lines.Add("## Wrappers")
    $lines.Add("")
    $lines.Add(("- PowerShell Python wrapper: $(Join-Path $PSScriptRoot "run_smi_python.ps1")"))
    $lines.Add(("- PowerShell npm wrapper: $(Join-Path $PSScriptRoot "run_smi_npm.ps1")"))
    $lines.Add("")
    $lines.Add("## Ingestion Use")
    $lines.Add("")
    $lines.Add("- Keep the binary environment under tools so ETDP does not ingest it directly.")
    $lines.Add("- Import the reports and repo sources from Imports/SMIKnowledge into ETDP search.")
    $lines.Add("- Use the repo sources for detailed knowledge and this environment index for runtime context.")

    Set-Content -Path $indexPath -Value $lines -Encoding UTF8
}

function Invoke-OptionalImport {
    param(
        [string]$ApiBaseUrl
    )

    $syncScript = Join-Path $PSScriptRoot "sync_smi_knowledge_sources.ps1"
    & powershell -ExecutionPolicy Bypass -File $syncScript -SkipSync -ImportToEtdp -ApiBase $ApiBaseUrl
    if ($LASTEXITCODE -ne 0) {
        throw "SMI knowledge import failed after environment bootstrap."
    }
}

if (-not (Test-Path $EnvRoot)) {
    New-Item -ItemType Directory -Force -Path $EnvRoot | Out-Null
}

$pythonExe = New-OrUpdatePythonEnv -Root $EnvRoot -Selector $PythonSelector
New-OrUpdateNodeEnv -Root $EnvRoot
$pythonPackages = Get-PythonPackageReport -PythonExe $pythonExe
$nodePackages = Get-NodePackageReport -Root $EnvRoot
Write-EnvironmentArtifacts -EnvRootPath $EnvRoot -KnowledgeRootPath $KnowledgeRoot -PythonExe $pythonExe -PythonPackages $pythonPackages -NodePackages $nodePackages

if ($ImportToEtdp) {
    Invoke-OptionalImport -ApiBaseUrl $ApiBase
}
