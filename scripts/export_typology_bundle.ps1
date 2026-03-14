param(
    [string]$BackendRoot = "http://localhost:5299",
    [string]$ProjectDir = "",
    [int]$QualificationId = 51,
    [string]$QualificationNumber = "90420",
    [int]$SubjectId = 0,
    [string]$SubjectCode = "",
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

function ToArray {
    param([object]$Value)
    if ($null -eq $Value) { return @() }
    if ($Value -is [System.Array]) { return $Value }
    return @($Value)
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

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $ts = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputRoot = Join-Path $ProjectDir "Exports\90420\typology_bundle_$ts"
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$stdout = Join-Path $OutputRoot "backend.stdout.log"
$stderr = Join-Path $OutputRoot "backend.stderr.log"
if (Test-Path $stdout) { Remove-Item $stdout -Force }
if (Test-Path $stderr) { Remove-Item $stderr -Force }

$api = "$($BackendRoot.TrimEnd('/'))/api"
$proc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--no-build", "--urls", $BackendRoot) -WorkingDirectory $ProjectDir -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru

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

    $qualifications = ToArray (Invoke-RestMethod -Uri "$api/Qualification" -Method Get -TimeoutSec 30)
    if ($qualifications.Count -eq 0) { throw "No qualifications returned by API." }

    $q = $null
    if ($QualificationId -gt 0) {
        $q = $qualifications | Where-Object { (AsInt (GetProp $_ @("id","Id"))) -eq $QualificationId } | Select-Object -First 1
    }
    if (-not $q -and -not [string]::IsNullOrWhiteSpace($QualificationNumber)) {
        $q = $qualifications | Where-Object {
            (AsText (GetProp $_ @("qualificationNumber","QualificationNumber"))) -eq $QualificationNumber
        } | Select-Object -First 1
    }
    if (-not $q) { $q = $qualifications | Select-Object -First 1 }

    $qid = AsInt (GetProp $q @("id","Id"))
    $qnum = AsText (GetProp $q @("qualificationNumber","QualificationNumber"))
    if ($qid -le 0) { throw "Resolved qualification id is invalid." }

    $subjects = ToArray (Invoke-RestMethod -Uri "$api/Subject/byQualification?qualificationId=$qid" -Method Get -TimeoutSec 30)
    if ($subjects.Count -eq 0) { throw "No subjects found for qualification id $qid." }

    $subject = $null
    if ($SubjectId -gt 0) {
        $subject = $subjects | Where-Object { (AsInt (GetProp $_ @("id","Id"))) -eq $SubjectId } | Select-Object -First 1
    }
    if (-not $subject -and -not [string]::IsNullOrWhiteSpace($SubjectCode)) {
        $subject = $subjects | Where-Object {
            ((AsText (GetProp $_ @("subjectCode","SubjectCode"))) -eq $SubjectCode) -or
            ((AsText (GetProp $_ @("phasesCode","PhasesCode"))) -eq $SubjectCode)
        } | Select-Object -First 1
    }
    if (-not $subject) { $subject = $subjects | Select-Object -First 1 }

    $sid = AsInt (GetProp $subject @("id","Id"))
    $scode = AsText (GetProp $subject @("subjectCode","SubjectCode"))
    if ([string]::IsNullOrWhiteSpace($scode)) { $scode = AsText (GetProp $subject @("phasesCode","PhasesCode")) }
    if ([string]::IsNullOrWhiteSpace($scode)) { $scode = "SUBJECT_$sid" }
    $safeSubject = ($scode -replace '[^A-Za-z0-9_-]', '_')

    $lgPath = Join-Path $OutputRoot "LearnerGuide_${qnum}_${safeSubject}_S${sid}.docx"
    $kqPath = Join-Path $OutputRoot "KnowledgeQuestionnaire_${qnum}_${safeSubject}_S${sid}.docx"
    $kqMemoPath = Join-Path $OutputRoot "KnowledgeQuestionnaire_Memorandum_${qnum}_${safeSubject}_S${sid}.docx"
    $wbPath = Join-Path $OutputRoot "Workbook_${qnum}_${safeSubject}_S${sid}.docx"
    $wbMemoPath = Join-Path $OutputRoot "Workbook_Memorandum_${qnum}_${safeSubject}_S${sid}.docx"

    $lgUrl = "$api/LearnerGuide/download?qualificationId=$qid&subjectId=$sid&paraphrase=false&useWorkflowCache=false"
    $kqUrl = "$api/KnowledgeQuestionnaire/download?qualificationId=$qid&subjectId=$sid&mcqDistractors=$McqDistractors"
    $kqMemoUrl = "$api/KnowledgeQuestionnaire/download-memorandum?qualificationId=$qid&subjectId=$sid&mcqDistractors=$McqDistractors"
    $wbUrl = "$api/Workbook/download?qualificationId=$qid&subjectId=$sid&maxActivities=30"
    $wbMemoUrl = "$api/Workbook/download-memorandum?qualificationId=$qid&subjectId=$sid&maxActivities=30"

    Invoke-WebRequest -Uri $lgUrl -OutFile $lgPath -TimeoutSec 300 | Out-Null
    Invoke-WebRequest -Uri $kqUrl -OutFile $kqPath -TimeoutSec 300 | Out-Null
    Invoke-WebRequest -Uri $kqMemoUrl -OutFile $kqMemoPath -TimeoutSec 300 | Out-Null
    Invoke-WebRequest -Uri $wbUrl -OutFile $wbPath -TimeoutSec 300 | Out-Null
    Invoke-WebRequest -Uri $wbMemoUrl -OutFile $wbMemoPath -TimeoutSec 300 | Out-Null

    $meta = [ordered]@{
        generatedAt = (Get-Date).ToString("s")
        backendRoot = $BackendRoot
        qualificationId = $qid
        qualificationNumber = $qnum
        subjectId = $sid
        subjectCode = $scode
        outputRoot = $OutputRoot
        files = [ordered]@{
            learnerGuide = [ordered]@{ path = $lgPath; bytes = (Get-Item $lgPath).Length }
            knowledgeQuestionnaire = [ordered]@{ path = $kqPath; bytes = (Get-Item $kqPath).Length }
            knowledgeQuestionnaireMemorandum = [ordered]@{ path = $kqMemoPath; bytes = (Get-Item $kqMemoPath).Length }
            workbook = [ordered]@{ path = $wbPath; bytes = (Get-Item $wbPath).Length }
            workbookMemorandum = [ordered]@{ path = $wbMemoPath; bytes = (Get-Item $wbMemoPath).Length }
        }
    }

    $metaPath = Join-Path $OutputRoot "typology-export-meta.json"
    $meta | ConvertTo-Json -Depth 20 | Set-Content -Path $metaPath -Encoding UTF8
    $meta | ConvertTo-Json -Depth 20
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
    }
}
