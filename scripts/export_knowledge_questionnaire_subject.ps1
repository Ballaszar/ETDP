param(
    [string]$BackendRoot = "http://localhost:5299",
    [string]$ProjectDir = "",
    [string]$QualificationNumber = "90420",
    [int]$QualificationId = 0,
    [string]$SubjectCode = "",
    [int]$SubjectId = 0,
    [int]$McqDistractors = 4,
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProjectDir)) {
    $ProjectDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Scalar {
    param([object]$Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Array]) {
        if ($Value.Count -gt 0) { return $Value[0] }
        return $null
    }
    return $Value
}

function AsInt {
    param([object]$Value)
    $v = Scalar $Value
    if ($null -eq $v) { return 0 }
    return [int]$v
}

function AsText {
    param([object]$Value)
    $v = Scalar $Value
    if ($null -eq $v) { return "" }
    return [string]$v
}

function GetProp {
    param(
        [object]$Obj,
        [string[]]$Names
    )
    foreach ($n in $Names) {
        if ($Obj.PSObject.Properties.Name -contains $n) {
            return $Obj.$n
        }
    }
    return $null
}

function ToArray {
    param([object]$Value)
    if ($null -eq $Value) { return @() }
    if ($Value -is [System.Array]) { return $Value }
    return @($Value)
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $ts = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputRoot = Join-Path $ProjectDir "Exports\90420\questionnaire_subject_$ts"
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

    $quals = ToArray (Invoke-RestMethod -Uri "$api/Qualification" -Method Get -TimeoutSec 30)
    if ($quals.Count -eq 0) { throw "No qualifications returned." }

    $q = $null
    if ($QualificationId -gt 0) {
        $q = $quals | Where-Object { (AsInt (GetProp $_ @("id","Id"))) -eq $QualificationId } | Select-Object -First 1
    }
    if (-not $q) {
        $q = $quals | Where-Object {
            (AsText (GetProp $_ @("qualificationNumber","QualificationNumber"))) -eq $QualificationNumber
        } | Select-Object -First 1
    }
    if (-not $q) { $q = $quals | Select-Object -First 1 }
    $qid = AsInt (GetProp $q @("id","Id"))
    if ($qid -le 0) { throw "Could not resolve a valid qualification id." }

    $subjects = ToArray (Invoke-RestMethod -Uri "$api/Subject/byQualification?qualificationId=$qid" -Method Get -TimeoutSec 30)
    if ($subjects.Count -eq 0) { throw "No subjects for qualification id $qid." }

    $s = $null
    if ($SubjectId -gt 0) {
        $s = $subjects | Where-Object { (AsInt (GetProp $_ @("id","Id"))) -eq $SubjectId } | Select-Object -First 1
    }
    if (-not [string]::IsNullOrWhiteSpace($SubjectCode)) {
        $s = $subjects | Where-Object {
            ((AsText (GetProp $_ @("subjectCode","SubjectCode"))) -eq $SubjectCode) -or
            ((AsText (GetProp $_ @("phasesCode","PhasesCode"))) -eq $SubjectCode)
        } | Select-Object -First 1
    }
    if (-not $s) { $s = $subjects | Select-Object -First 1 }
    $sid = AsInt (GetProp $s @("id","Id"))
    $scode = AsText (GetProp $s @("subjectCode","SubjectCode"))
    if ([string]::IsNullOrWhiteSpace($scode)) { $scode = AsText (GetProp $s @("phasesCode","PhasesCode")) }
    if ([string]::IsNullOrWhiteSpace($scode)) { $scode = "SUBJECT_$sid" }
    $safeSubject = ($scode -replace '[^A-Za-z0-9_-]', '_')

    $paperPath = Join-Path $OutputRoot "KnowledgeQuestionnaire_${safeSubject}_S${sid}.docx"
    $memoPath = Join-Path $OutputRoot "KnowledgeQuestionnaire_Memorandum_${safeSubject}_S${sid}.docx"
    $metaPath = Join-Path $OutputRoot "export-meta.json"

    $qUrl = "$api/KnowledgeQuestionnaire/download?qualificationId=$qid&subjectId=$sid&mcqDistractors=$McqDistractors"
    $mUrl = "$api/KnowledgeQuestionnaire/download-memorandum?qualificationId=$qid&subjectId=$sid&mcqDistractors=$McqDistractors"

    Invoke-WebRequest -Uri $qUrl -OutFile $paperPath -TimeoutSec 240 | Out-Null
    Invoke-WebRequest -Uri $mUrl -OutFile $memoPath -TimeoutSec 240 | Out-Null

    $meta = [ordered]@{
        generatedAt = (Get-Date).ToString("s")
        backendRoot = $BackendRoot
        qualificationId = $qid
        qualificationNumber = AsText (GetProp $q @("qualificationNumber","QualificationNumber"))
        subjectId = $sid
        subjectCode = $scode
        mcqDistractors = $McqDistractors
        questionnairePath = $paperPath
        memorandumPath = $memoPath
        questionnaireBytes = (Get-Item $paperPath).Length
        memorandumBytes = (Get-Item $memoPath).Length
    }
    $meta | ConvertTo-Json -Depth 10 | Set-Content -Path $metaPath -Encoding UTF8
    $meta | ConvertTo-Json -Depth 10
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
    }
}
