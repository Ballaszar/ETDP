param(
    [string]$BackendBase = "http://localhost:5299/api",
    [int]$QualificationId = 28,
    [switch]$RunImports,
    [switch]$RunSeedWrite,
    [int]$MaxSubjects = 0
)

$ErrorActionPreference = "Stop"
$BackendBase = ($BackendBase ?? "").TrimEnd('/')
$BackendRoot = if ($BackendBase.ToLower().EndsWith("/api")) { $BackendBase.Substring(0, $BackendBase.Length - 4) } else { $BackendBase }
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Get-CountSnapshot {
    param([string]$Base, [int]$Qid)

    $subjects = Invoke-RestMethod -Uri "$Base/Subject/byQualification?qualificationId=$Qid" -Method Get
    $outcomes = Invoke-RestMethod -Uri "$Base/Outcome/byQualification?qualificationId=$Qid" -Method Get
    $topics = Invoke-RestMethod -Uri "$Base/Topic/byQualification?qualificationId=$Qid" -Method Get
    $criteria = Invoke-RestMethod -Uri "$Base/AssessmentCriteria/byQualification?qualificationId=$Qid" -Method Get
    $plans = Invoke-RestMethod -Uri "$Base/LessonPlan/byQualification?qualificationId=$Qid" -Method Get
    $toolkitAll = Invoke-RestMethod -Uri "$Base/LecturerToolkit" -Method Get
    $toolkit = @($toolkitAll | Where-Object { $_.qualificationsId -eq $Qid })

    [pscustomobject]@{
        subjects = @($subjects).Count
        outcomes = @($outcomes).Count
        topics = @($topics).Count
        assessmentCriteria = @($criteria).Count
        lessonPlans = @($plans).Count
        toolkitRows = @($toolkit).Count
    }
}

function Invoke-OptionalRest {
    param(
        [string]$Name,
        [string]$Uri,
        [string]$Method = "Post"
    )

    try {
        return Invoke-RestMethod -Uri $Uri -Method $Method
    } catch {
        $msg = $_.Exception.Message
        if ($msg -match "404") {
            return [pscustomobject]@{
                skipped = $true
                reason = "$Name endpoint not available"
                error = $msg
                savedCount = 0
                skippedCount = 0
            }
        }
        throw
    }
}

$pre = Get-CountSnapshot -Base $BackendBase -Qid $QualificationId

$subjectImport = $null
$outcomeImport = $null
$topicImport = $null
$toolkitImport = $null

if ($RunImports) {
    $subjectImport = Invoke-RestMethod -Uri "$BackendBase/Subject/import-csv" -Method Post
    $outcomeImport = Invoke-RestMethod -Uri "$BackendBase/Outcome/import-csv" -Method Post
    $topicImport = Invoke-RestMethod -Uri "$BackendBase/Topic/import-csv" -Method Post
    $toolkitImport = Invoke-RestMethod -Uri "$BackendBase/LecturerToolkit/import-csv?qualificationId=$QualificationId" -Method Post
}

$seedBody = @{
    QualificationId = $QualificationId
    OnlyMissing = $true
    DryRun = (-not $RunSeedWrite)
} | ConvertTo-Json
$seed = Invoke-RestMethod -Uri "$BackendBase/Admin/seed-skeleton" -Method Post -Body $seedBody -ContentType "application/json"

$ts = Get-Date -Format "yyyyMMdd_HHmmss"
$outRoot = Join-Path $ProjectRoot "Exports\SmokeTest_$($QualificationId)_$ts"
New-Item -ItemType Directory -Force -Path $outRoot, "$outRoot\Schedule", "$outRoot\LearnerGuide", "$outRoot\Workbook", "$outRoot\Questionnaire", "$outRoot\Compliance" | Out-Null

Invoke-WebRequest -Uri "$BackendBase/LearningSchedule/download?qualificationId=$QualificationId" -OutFile "$outRoot\Schedule\learning_schedule.csv"
Invoke-WebRequest -Uri "$BackendBase/LearningSchedule/download-docx?qualificationId=$QualificationId" -OutFile "$outRoot\Schedule\learning_schedule.docx"
$lessonPlanExport = Invoke-OptionalRest -Name "LearningSchedule lesson-plan export" -Uri "$BackendBase/LearningSchedule/export-lesson-plans-by-lpn?qualificationId=$QualificationId" -Method Post
$slidesExport = Invoke-OptionalRest -Name "Content slides export" -Uri "$BackendBase/Content/export-slides-by-lpn?qualificationId=$QualificationId" -Method Post
Invoke-WebRequest -Uri "$BackendBase/LearnerGuide/download?qualificationId=$QualificationId" -OutFile "$outRoot\LearnerGuide\LearnerGuide.docx"

$subjects = Invoke-RestMethod -Uri "$BackendBase/Subject/byQualification?qualificationId=$QualificationId" -Method Get
$workbookFiles = 0
$questionnaireFiles = 0
$memorandumFiles = 0
$subjectIndex = 0
foreach ($s in $subjects) {
    $subjectIndex++
    if ($MaxSubjects -gt 0 -and $subjectIndex -gt $MaxSubjects) { break }
    $sid = $s.id
    $sourceCode = if ($s.subjectCode) { $s.subjectCode } else { $s.phasesCode }
    $scode = ($sourceCode -replace '[^A-Za-z0-9_-]', '_')
    if ([string]::IsNullOrWhiteSpace($scode)) { $scode = "Subject_$sid" }

    try {
        Invoke-WebRequest -Uri "$BackendBase/Workbook/download?subjectId=$sid" -OutFile "$outRoot\Workbook\Workbook_${scode}_S${sid}.docx"
        $workbookFiles++
    } catch {}

    try {
        Invoke-WebRequest -Uri "$BackendBase/KnowledgeQuestionnaire/download?subjectId=$sid" -OutFile "$outRoot\Questionnaire\Questionnaire_${scode}_S${sid}.docx"
        $questionnaireFiles++
    } catch {}

    try {
        Invoke-WebRequest -Uri "$BackendBase/KnowledgeQuestionnaire/download-memorandum?subjectId=$sid" -OutFile "$outRoot\Questionnaire\Memorandum_${scode}_S${sid}.docx"
        $memorandumFiles++
    } catch {}
}

$rubric = Invoke-RestMethod -Uri "$BackendBase/AssessmentCompliance/rubric/generate?qualificationId=$QualificationId" -Method Post
if ($rubric.downloadUrl) {
    $rubricDownloadUrl = if ($rubric.downloadUrl.StartsWith("http")) { $rubric.downloadUrl } else { "$BackendRoot$($rubric.downloadUrl)" }
    Invoke-WebRequest -Uri $rubricDownloadUrl -OutFile "$outRoot\Compliance\generated_rubric.csv"
}

$post = Get-CountSnapshot -Base $BackendBase -Qid $QualificationId

$report = [pscustomobject]@{
    qualificationId = $QualificationId
    preCounts = $pre
    postCounts = $post
    imports = @{
        executed = [bool]$RunImports
        subjects = $subjectImport
        outcomes = $outcomeImport
        topics = $topicImport
        toolkit = $toolkitImport
        seed = $seed
        seedWriteEnabled = [bool]$RunSeedWrite
    }
    exports = @{
        lessonPlansSaved = $lessonPlanExport.savedCount
        lessonPlansSkipped = $lessonPlanExport.skippedCount
        slidesSaved = $slidesExport.savedCount
        slidesSkipped = $slidesExport.skippedCount
        workbooks = $workbookFiles
        questionnaires = $questionnaireFiles
        memorandums = $memorandumFiles
    }
    outputRoot = $outRoot
    timestamp = (Get-Date).ToString("s")
}

$reportPath = "$outRoot\smoke-test-report.json"
$report | ConvertTo-Json -Depth 20 | Set-Content -Path $reportPath -Encoding UTF8
$report | ConvertTo-Json -Depth 20
