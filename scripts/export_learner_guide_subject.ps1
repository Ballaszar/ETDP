param(
    [string]$BackendRoot = "http://localhost:5299",
    [string]$ProjectDir = "",
    [int]$QualificationId = 51,
    [int]$SubjectId = 453,
    [bool]$Paraphrase = $false,
    [bool]$UseWorkflowCache = $false,
    [bool]$IncludeIllustrations = $false,
    [bool]$GenerateIllustrations = $false,
    [int]$MaxIllustrationsPerTopic = 2,
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProjectDir)) {
    $ProjectDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $ts = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputRoot = Join-Path $ProjectDir "Exports\90420\learner_guide_subject_$ts"
}

$api = "$($BackendRoot.TrimEnd('/'))/api"
$stdout = Join-Path $OutputRoot "backend.stdout.log"
$stderr = Join-Path $OutputRoot "backend.stderr.log"
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
if (Test-Path $stdout) { Remove-Item $stdout -Force }
if (Test-Path $stderr) { Remove-Item $stderr -Force }

$proc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--urls", $BackendRoot) -WorkingDirectory $ProjectDir -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru

try {
    $ready = $false
    for ($i = 0; $i -lt 120; $i++) {
        try {
            $null = Invoke-RestMethod -Uri "$api/Qualification" -Method Get -TimeoutSec 2
            $ready = $true
            break
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) { throw "Backend did not become ready at $BackendRoot." }

    $file = Join-Path $OutputRoot "LearnerGuide_Q${QualificationId}_S${SubjectId}.docx"
    $url = "$api/LearnerGuide/download?qualificationId=$QualificationId&subjectId=$SubjectId&paraphrase=$($Paraphrase.ToString().ToLower())&useWorkflowCache=$($UseWorkflowCache.ToString().ToLower())&includeIllustrations=$($IncludeIllustrations.ToString().ToLower())&generateIllustrations=$($GenerateIllustrations.ToString().ToLower())&maxIllustrationsPerTopic=$MaxIllustrationsPerTopic"
    Invoke-WebRequest -Uri $url -OutFile $file -TimeoutSec 300 | Out-Null

    $info = Get-Item $file
    [pscustomobject]@{
        generatedAt = (Get-Date).ToString("s")
        qualificationId = $QualificationId
        subjectId = $SubjectId
        outputPath = $info.FullName
        bytes = $info.Length
        outputRoot = $OutputRoot
    } | ConvertTo-Json -Depth 6
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
    }
}
