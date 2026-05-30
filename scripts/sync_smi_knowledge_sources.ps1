param(
    [string]$RootPath = "$(Split-Path $PSScriptRoot -Parent)\Imports\SMIKnowledge",
    [string]$ManifestPath = "$(Split-Path $PSScriptRoot -Parent)\Imports\SMIKnowledge\manifests\smi-knowledge-sources.json",
    [string]$ApiBase = "http://localhost:5299/api",
    [string]$QualificationDescription = "SMI External Knowledge",
    [int]$MaxFiles = 10000,
    [int]$MaxFileSizeKb = 20480,
    [switch]$ImportToEtdp,
    [switch]$SkipSync
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    param(
        [string]$WorkingDirectory,
        [string[]]$Arguments
    )

    $gitArgs = @("-C", $WorkingDirectory) + $Arguments
    $proc = Start-Process -FilePath "git.exe" -ArgumentList $gitArgs -WorkingDirectory $WorkingDirectory -NoNewWindow -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        throw ("git failed in {0} with exit code {1}" -f $WorkingDirectory, $proc.ExitCode)
    }
}

function Invoke-GitRoot {
    param(
        [string[]]$Arguments
    )

    $proc = Start-Process -FilePath "git.exe" -ArgumentList $Arguments -NoNewWindow -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        throw ("git failed with exit code {0}" -f $proc.ExitCode)
    }
}

function Test-ApiAvailable {
    param(
        [string]$BaseUrl
    )

    try {
        $null = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUrl.TrimEnd('/') + "/Content/knowledge-pools") -TimeoutSec 5
        return $true
    }
    catch {
        return $false
    }
}

function Sync-Repository {
    param(
        [pscustomobject]$Repo,
        [string]$ReposRoot
    )

    $targetPath = Join-Path $ReposRoot $Repo.name
    $branch = $Repo.branch
    if ([string]::IsNullOrWhiteSpace($branch)) {
        $branch = "main"
    }

    if (Test-Path (Join-Path $targetPath ".git")) {
        Write-Host ("Refreshing {0} ({1})" -f $Repo.name, $branch)
        Invoke-Git -WorkingDirectory $targetPath -Arguments @("fetch", "--depth", "1", "origin", $branch) | Out-Null
        Invoke-Git -WorkingDirectory $targetPath -Arguments @("checkout", $branch) | Out-Null
        Invoke-Git -WorkingDirectory $targetPath -Arguments @("pull", "--ff-only", "--depth", "1", "origin", $branch) | Out-Null
    }
    elseif (Test-Path $targetPath) {
        throw ("Target path exists but is not a git repository: {0}" -f $targetPath)
    }
    else {
        Write-Host ("Cloning {0} ({1})" -f $Repo.repoUrl, $branch)
        Invoke-GitRoot -Arguments @("clone", "--depth", "1", "--branch", $branch, $Repo.repoUrl, $targetPath) | Out-Null
    }

    $commit = (& git -C $targetPath rev-parse HEAD).Trim()
    $remote = (& git -C $targetPath remote get-url origin).Trim()
    return [pscustomobject]@{
        name = $Repo.name
        branch = $branch
        repoUrl = $remote
        localPath = $targetPath
        commit = $commit
        priority = $Repo.priority
        notes = $Repo.notes
    }
}

function Write-SeedIndex {
    param(
        [pscustomobject]$Manifest,
        [System.Collections.IEnumerable]$SyncedRepos,
        [string]$OutputPath
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# SMI Knowledge Seed")
    $lines.Add("")
    $lines.Add("This file gives SMI a top-level map of the external knowledge sources synced into ETDP.")
    $lines.Add("")
    $lines.Add("## Repositories")
    $lines.Add("")
    foreach ($repo in $SyncedRepos) {
        $lines.Add(("- {0}" -f $repo.name))
        $lines.Add(("  - priority: {0}" -f $repo.priority))
        $lines.Add(("  - branch: {0}" -f $repo.branch))
        $lines.Add(("  - commit: {0}" -f $repo.commit))
        $lines.Add(("  - localPath: {0}" -f $repo.localPath))
        $lines.Add(("  - repoUrl: {0}" -f $repo.repoUrl))
        if (-not [string]::IsNullOrWhiteSpace($repo.notes)) {
            $lines.Add(("  - notes: {0}" -f $repo.notes))
        }
        $lines.Add("")
    }

    $lines.Add("## Package References")
    $lines.Add("")
    foreach ($pkg in $Manifest.packageReferences) {
        $lines.Add(("- {0} ({1})" -f $pkg.name, $pkg.ecosystem))
        $lines.Add(("  - install: {0}" -f $pkg.install))
        if ($pkg.notes) {
            $lines.Add(("  - notes: {0}" -f $pkg.notes))
        }
        $lines.Add("")
    }

    $lines.Add("## Remote Docs")
    $lines.Add("")
    foreach ($doc in $Manifest.remoteDocs) {
        $lines.Add(("- {0}" -f $doc.name))
        $lines.Add(("  - url: {0}" -f $doc.url))
        if ($doc.notes) {
            $lines.Add(("  - notes: {0}" -f $doc.notes))
        }
        $lines.Add("")
    }

    $lines.Add("## Ingestion Method")
    $lines.Add("")
    $lines.Add("- Clone or pull repositories into the local repos folder.")
    $lines.Add("- Import the entire root folder into ETDP with includeCodeFiles enabled.")
    $lines.Add("- Search through local paragraphs first using knowledgePool `local_folder` or `local_any`.")
    $lines.Add("- Use the seed summary for high-level repo discovery before diving into raw source files.")
    $lines.Add("")
    $lines.Add("## Priority Files")
    $lines.Add("")
    foreach ($item in $Manifest.priorityFiles) {
        $lines.Add(("- {0}" -f $item))
    }

    Set-Content -Path $OutputPath -Value $lines -Encoding UTF8
}

function Import-KnowledgeFolder {
    param(
        [string]$BaseUrl,
        [string]$FolderRoot,
        [string]$PoolName,
        [string]$QualificationDescription,
        [int]$MaxFiles,
        [int]$MaxFileSizeKb
    )

    $body = @{
        rootPath = $FolderRoot
        knowledgePool = $PoolName
        qualificationDescription = $QualificationDescription
        maxFiles = $MaxFiles
        maxFileSizeKb = $MaxFileSizeKb
        includeCodeFiles = $true
    } | ConvertTo-Json -Depth 5

    return Invoke-RestMethod -Method Post -Uri ($BaseUrl.TrimEnd('/') + "/Content/import-local-folder") -ContentType "application/json" -Body $body
}

if (-not (Test-Path $ManifestPath)) {
    throw ("Manifest not found: {0}" -f $ManifestPath)
}

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$reposRoot = Join-Path $RootPath "repos"
$seedPath = Join-Path $RootPath "manifests\SMI_KNOWLEDGE_SEED.md"
$reportPath = Join-Path $RootPath "manifests\smi-knowledge-sync-report.json"

New-Item -ItemType Directory -Force -Path $reposRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $seedPath -Parent) | Out-Null

$synced = @()
if (-not $SkipSync) {
    foreach ($repo in $manifest.repos) {
        $synced += Sync-Repository -Repo $repo -ReposRoot $reposRoot
    }
}
else {
    foreach ($repo in $manifest.repos) {
        $targetPath = Join-Path $reposRoot $repo.name
        if (Test-Path (Join-Path $targetPath ".git")) {
            $synced += [pscustomobject]@{
                name = $repo.name
                branch = (& git -C $targetPath rev-parse --abbrev-ref HEAD).Trim()
                repoUrl = (& git -C $targetPath remote get-url origin).Trim()
                localPath = $targetPath
                commit = (& git -C $targetPath rev-parse HEAD).Trim()
                priority = $repo.priority
                notes = $repo.notes
            }
        }
    }
}

Write-SeedIndex -Manifest $manifest -SyncedRepos $synced -OutputPath $seedPath

$report = [pscustomobject]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    rootPath = $RootPath
    repoCount = @($synced).Count
    repos = $synced
    importAttempted = [bool]$ImportToEtdp
    importResult = $null
}

if ($ImportToEtdp) {
    if (Test-ApiAvailable -BaseUrl $ApiBase) {
        $report.importResult = Import-KnowledgeFolder -BaseUrl $ApiBase -FolderRoot $RootPath -PoolName $manifest.knowledgePool -QualificationDescription $QualificationDescription -MaxFiles $MaxFiles -MaxFileSizeKb $MaxFileSizeKb
    }
    else {
        $report.importResult = [pscustomobject]@{
            status = "skipped"
            reason = "backend_unavailable"
            apiBase = $ApiBase
        }
    }
}

$report | ConvertTo-Json -Depth 8 | Set-Content -Path $reportPath -Encoding UTF8
$report | ConvertTo-Json -Depth 8
