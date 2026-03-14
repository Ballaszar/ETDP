param(
    [string]$BackendBase = 'http://localhost:5299/api',
    [string]$ProjectPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $projectRoot 'ETDP.csproj'
}

$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path $projectRoot "Exports\90420\run_$ts"
$docsRoot = "C:\Users\pierr\Documents\90420\run_$ts"
New-Item -ItemType Directory -Force -Path $outRoot, $docsRoot | Out-Null

function Scalar {
    param([object]$Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Array]) {
        if ($Value.Count -gt 0) { return $Value[0] }
        return $null
    }
    return $Value
}

function S {
    param([object]$Value)
    $v = Scalar $Value
    if ($null -eq $v) { return '' }
    return [string]$v
}

function I {
    param([object]$Value)
    $v = S $Value
    if ([string]::IsNullOrWhiteSpace($v)) { return $null }
    return [int]$v
}

function Invoke-Api {
    param(
        [Parameter(Mandatory=$true)][string]$Method,
        [Parameter(Mandatory=$true)][string]$Path,
        [object]$Body = $null
    )

    $uri = "$BackendBase/$Path"
    if ($Method -eq 'GET') {
        return Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 300
    }

    if ($Body -ne $null) {
        $json = $Body | ConvertTo-Json -Depth 25
        return Invoke-RestMethod -Uri $uri -Method $Method -ContentType 'application/json' -Body $json -TimeoutSec 300
    }

    return Invoke-RestMethod -Uri $uri -Method $Method -TimeoutSec 300
}

function Try-WebDownload {
    param(
        [string]$Name,
        [string]$Url,
        [string]$OutFile
    )
    try {
        Invoke-WebRequest -Uri $Url -OutFile $OutFile -TimeoutSec 300 | Out-Null
        return [pscustomobject]@{ name=$Name; ok=$true; file=$OutFile; size=(Get-Item $OutFile).Length; error='' }
    } catch {
        return [pscustomobject]@{ name=$Name; ok=$false; file=$OutFile; size=0; error=$_.Exception.Message }
    }
}

$job = Start-Job -ScriptBlock {
    param($Proj)
    dotnet run --project $Proj --urls http://localhost:5299
} -ArgumentList $ProjectPath

$summary = [ordered]@{}

try {
    $ready = $false
    for ($i=0; $i -lt 180; $i++) {
        try {
            Invoke-RestMethod -Uri "$BackendBase/Qualification" -Method Get -TimeoutSec 20 | Out-Null
            $ready = $true
            break
        } catch {
            Start-Sleep -Seconds 1
        }
    }
    if (-not $ready) { throw 'Backend did not become ready on localhost:5299.' }

    # Ensure qualification 90420 exists
    $quals = @(Invoke-Api -Method GET -Path 'Qualification')
    $q = $quals | Where-Object { (S $_.QualificationNumber) -eq '90420' } | Select-Object -First 1
    if (-not $q) {
        $q = Invoke-Api -Method POST -Path 'Qualification' -Body @{
            qualificationNumber = '90420'
            qualificationDescription = 'A Fitter and Turner fabricates metal parts, fits, assembles, maintains and repairs mechanical components, sub-assemblies and machines.'
            nqfLevel = '4'
            credits = '540'
            learningInstitutionName = 'ETDP'
            accreditationNumber = ''
            deanPrincipalCEO = ''
            seniorLecturer = ''
            logoPath = ''
            qualificationType = 'Occupational Certificate'
            usesOutcomes = $false
            purpose = 'End-to-end automated qualification build for 90420'
            learningDateStart = (Get-Date).ToString('yyyy-MM-dd')
            learningDateEnd = (Get-Date).AddMonths(12).ToString('yyyy-MM-dd')
        }
    }
    $qid = I $q.Id
    $qDesc = S $q.QualificationDescription
    $summary.qualification = [ordered]@{ id=$qid; number='90420'; description=$qDesc }

    # Prepare QC files in Imports\90420
    $importsDir = Join-Path $projectRoot 'Imports\90420'
    New-Item -ItemType Directory -Force -Path $importsDir | Out-Null

    $currCandidates = @(
        (Join-Path $projectRoot 'Requests\Re-configured 652302000-Curriculum Document Fitter and Turner.docx'),
        (Join-Path $projectRoot 'Imports\94020\QC_CurriculumSpecification.docx'),
        (Join-Path $projectRoot 'Imports\94020\QC_CurriculumSpecification.pdf'),
        (Join-Path $projectRoot 'Requests\652302000-CurriculumDocumentFitterandTurner.pdf')
    )
    $curriculumSource = $currCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $curriculumSource) { throw 'No curriculum source file found for 90420 setup.' }
    $currExt = [System.IO.Path]::GetExtension($curriculumSource)
    $qcCurrPath = Join-Path $importsDir ("QC_CurriculumSpecification" + $currExt)
    $qcAssessPath = Join-Path $importsDir ("QC_AssessmentSpecification" + $currExt)
    Copy-Item -Path $curriculumSource -Destination $qcCurrPath -Force
    Copy-Item -Path $curriculumSource -Destination $qcAssessPath -Force
    $summary.input = [ordered]@{ curriculumSource=$curriculumSource; qcCurriculum=$qcCurrPath; qcAssessment=$qcAssessPath }

    # Build/Apply mapping queue
    $queueBuild = Invoke-Api -Method POST -Path 'QualityCouncilCurricula/build-mapping-review-queue' -Body @{ QualificationId=$qid; StartPage=10 }
    $applyHigh = Invoke-Api -Method POST -Path 'QualityCouncilCurricula/apply-mapping-review' -Body @{ QualificationId=$qid; PendingOnly=$true; MinConfidence=85 }
    $applyAll = Invoke-Api -Method POST -Path 'QualityCouncilCurricula/apply-mapping-review' -Body @{ QualificationId=$qid; PendingOnly=$true }

    # Seed and schedule
    $seed = Invoke-Api -Method POST -Path 'Admin/seed-skeleton' -Body @{ QualificationId=$qid; OnlyMissing=$true; DryRun=$false }
    $toolkitSchedule = Invoke-Api -Method POST -Path ("LecturerToolkit/automate-learning-schedule?qualificationId=$qid&replaceExisting=true")

    # Index known 90420 hierarchy files
    $null = Invoke-Api -Method POST -Path 'Content/scaffold-knowledge-hierarchy' -Body @{ QualificationId=$qid }
    $devRoot = Join-Path $projectRoot 'Imports\KnowledgeHierarchy\90420_Fitter_and_Turner\developer_knowledge_base'
    $localRoot = Join-Path $projectRoot 'Imports\KnowledgeHierarchy\90420_Fitter_and_Turner\local_source_upload'
    $idxDev = $null
    $idxLocal = $null

    if (Test-Path $devRoot) {
        $idxDev = Invoke-Api -Method POST -Path 'Content/index-qualification-knowledge' -Body @{
            RootPath = $devRoot
            QualificationId = $qid
            QualificationCode = '90420'
            QualificationDescription = $qDesc
            SourceType = 'developer_knowledge_base'
            Recursive = $true
            MaxFiles = 5000
        }
    }

    if (Test-Path $localRoot) {
        $idxLocal = Invoke-Api -Method POST -Path 'Content/index-qualification-knowledge' -Body @{
            RootPath = $localRoot
            QualificationId = $qid
            QualificationCode = '90420'
            QualificationDescription = $qDesc
            SourceType = 'local_source_upload'
            Recursive = $true
            MaxFiles = 5000
        }
    }

    # Build lookup graph
    $subjects = @(Invoke-Api -Method GET -Path ("Subject/byQualification?qualificationId=$qid"))
    $topics = @(Invoke-Api -Method GET -Path ("Topic/byQualification?qualificationId=$qid"))
    $criteria = @(Invoke-Api -Method GET -Path ("AssessmentCriteria/byQualification?qualificationId=$qid"))
    $toolkitAll = @(Invoke-Api -Method GET -Path 'LecturerToolkit')
    $toolkit = @($toolkitAll | Where-Object { (I $_.QualificationsId) -eq $qid })

    $subjectById = @{}
    foreach ($s in $subjects) { $sid = I $s.Id; if ($sid) { $subjectById[$sid] = $s } }

    $topicById = @{}
    foreach ($t in $topics) { $tid = I $t.Id; if ($tid) { $topicById[$tid] = $t } }

    $criteriaById = @{}
    foreach ($c in $criteria) { $cid = I $c.Id; if ($cid) { $criteriaById[$cid] = $c } }

    # Auto-map groups by criteria, then propagate content to all LPN rows in each group
    $groups = @($toolkit | Group-Object {
        $cid = I $_.AssessmentCriteriaId
        if ($cid) { "criteria:$cid" } else { "entry:$((I $_.Id))" }
    })

    $mapDetails = New-Object System.Collections.Generic.List[object]
    $mappedGroups = 0
    $failedGroups = 0
    $propagatedRows = 0

    foreach ($g in $groups) {
        $ordered = @($g.Group | Sort-Object @{Expression={
            $raw = S $_.Lpn
            $m = [regex]::Match($raw, '\\d+')
            if ($m.Success) { [int]$m.Value } else { 999999 }
        }}, @{Expression={ I $_.Id }})

        if ($ordered.Count -eq 0) { continue }

        $seedEntry = $ordered[0]
        $seedId = I $seedEntry.Id
        $criteriaId = I $seedEntry.AssessmentCriteriaId

        $criteriaObj = if ($criteriaId -and $criteriaById.ContainsKey($criteriaId)) { $criteriaById[$criteriaId] } else { $null }
        $topicObj = $null
        if ($criteriaObj) {
            $topicId = I $criteriaObj.TopicId
            if ($topicId -and $topicById.ContainsKey($topicId)) { $topicObj = $topicById[$topicId] }
        }
        $subjectObj = $null
        if ($topicObj) {
            $subjectId = I $topicObj.SubjectId
            if ($subjectId -and $subjectById.ContainsKey($subjectId)) { $subjectObj = $subjectById[$subjectId] }
        }

        $subjectCode = if ($subjectObj) { S $subjectObj.SubjectCode } else { S $seedEntry.SubjectCode }
        $subjectDescription = if ($subjectObj) { S $subjectObj.SubjectDescription } else { S $seedEntry.SubjectDescription }
        $topicDescription = if ($topicObj) { S $topicObj.TopicDescription } else { '' }
        $criteriaDescription = if ($criteriaObj) { S $criteriaObj.Description } else { S $seedEntry.AssessmentCriteriaDescription }
        $lessonDescription = S $seedEntry.LessonPlanDescription

        $backendUsed = 'moderator'
        $groupError = ''

        try {
            $null = Invoke-Api -Method POST -Path 'Content/moderator-insert-best-context' -Body @{
                LecturerToolkitEntryId = $seedId
                Query = "90420 $subjectCode $subjectDescription $topicDescription $criteriaDescription $lessonDescription"
                QualificationCode = '90420'
                QualificationDescription = $qDesc
                SubjectDescription = $subjectDescription
                SubjectCode = $subjectCode
                TopicDescription = $topicDescription
                AssessmentCriteriaDescription = $criteriaDescription
                LessonPlanDescription = $lessonDescription
                Cite = $false
                CandidateLimit = 8
                SnippetLength = 1600
                DryRun = $false
            }
        }
        catch {
            $groupError = $_.Exception.Message
        }

        $seedUpdated = Invoke-Api -Method GET -Path ("LecturerToolkit/$seedId")
        $finalContent = (S $seedUpdated.LessonPlanContent).Trim()

        if ([string]::IsNullOrWhiteSpace($finalContent)) {
            try {
                $backendUsed = 'draft_fallback'
                $draft = Invoke-Api -Method POST -Path 'Content/draft' -Body @{
                    SubjectName = $subjectCode
                    SubjectDescription = $subjectDescription
                    TopicDescription = $topicDescription
                    TopicPurpose = ''
                    LessonPlanDescription = $lessonDescription
                    AssessmentCriteriaDescription = $criteriaDescription
                    LecturerActions = S $seedEntry.LecturerActions
                    LearnerActions = S $seedEntry.LearnerActions
                    Sources = @()
                    Length = '600-900 words'
                    Level = 'TVET NQF 4'
                }
                $draftText = (S $draft.content).Trim()
                if (-not [string]::IsNullOrWhiteSpace($draftText)) {
                    $null = Invoke-Api -Method POST -Path 'Content/assemble' -Body @{ LecturerToolkitEntryId = $seedId; Content = $draftText }
                    $seedUpdated = Invoke-Api -Method GET -Path ("LecturerToolkit/$seedId")
                    $finalContent = (S $seedUpdated.LessonPlanContent).Trim()
                }
            }
            catch {
                if ([string]::IsNullOrWhiteSpace($groupError)) {
                    $groupError = $_.Exception.Message
                }
                else {
                    $groupError = "$groupError | fallback: $($_.Exception.Message)"
                }
            }
        }

        if ([string]::IsNullOrWhiteSpace($finalContent)) {
            $failedGroups++
            $mapDetails.Add([pscustomobject]@{ group=$g.Name; seedEntryId=$seedId; status='failed'; backend=$backendUsed; error=$groupError; propagated=0 }) | Out-Null
            continue
        }

        $mappedGroups++
        $groupProp = 0

        foreach ($e in $ordered) {
            $eid = I $e.Id
            if ($eid -eq $seedId) { continue }
            $existing = (S $e.LessonPlanContent).Trim()
            if (-not [string]::IsNullOrWhiteSpace($existing)) { continue }

            $payload = @{
                QualificationsId = I $e.QualificationsId
                LearningInstitutionName = S $e.LearningInstitutionName
                LecturerName = S $e.LecturerName
                SubjectCode = S $e.SubjectCode
                SubjectDescription = S $e.SubjectDescription
                AssessmentCriteriaId = I $e.AssessmentCriteriaId
                AssessmentCriteriaDescription = S $e.AssessmentCriteriaDescription
                Lpn = S $e.Lpn
                LessonPlanDescription = S $e.LessonPlanDescription
                LessonPlanContent = $finalContent
                TimeStart = S $e.TimeStart
                TimeEnd = S $e.TimeEnd
                LecturerActions = S $e.LecturerActions
                LearnerActions = S $e.LearnerActions
                LearningAids = S $e.LearningAids
            }

            try {
                $null = Invoke-Api -Method PUT -Path ("LecturerToolkit/$eid") -Body $payload
                $groupProp++
                $propagatedRows++
            }
            catch {
                # continue
            }
        }

        $mapDetails.Add([pscustomobject]@{ group=$g.Name; seedEntryId=$seedId; status='mapped'; backend=$backendUsed; error=$groupError; propagated=$groupProp }) | Out-Null
    }

    # Refresh counts
    $subjectsAfter = @(Invoke-Api -Method GET -Path ("Subject/byQualification?qualificationId=$qid"))
    $topicsAfter = @(Invoke-Api -Method GET -Path ("Topic/byQualification?qualificationId=$qid"))
    $criteriaAfter = @(Invoke-Api -Method GET -Path ("AssessmentCriteria/byQualification?qualificationId=$qid"))
    $lessonPlansAfter = @(Invoke-Api -Method GET -Path ("LessonPlan/byQualification?qualificationId=$qid"))
    $toolkitAfterAll = @(Invoke-Api -Method GET -Path 'LecturerToolkit')
    $toolkitAfter = @($toolkitAfterAll | Where-Object { (I $_.QualificationsId) -eq $qid })
    $toolkitNonEmpty = @($toolkitAfter | Where-Object { -not [string]::IsNullOrWhiteSpace((S $_.LessonPlanContent).Trim()) }).Count

    # Exports
    $exportsDir = Join-Path $outRoot 'Exports'
    New-Item -ItemType Directory -Force -Path $exportsDir, (Join-Path $exportsDir 'Schedule'), (Join-Path $exportsDir 'LearnerGuide'), (Join-Path $exportsDir 'Workbook'), (Join-Path $exportsDir 'Questionnaire') | Out-Null

    $exportResults = New-Object System.Collections.Generic.List[object]
    $exportResults.Add((Try-WebDownload -Name 'LearningSchedule CSV' -Url "$BackendBase/LearningSchedule/download?qualificationId=$qid" -OutFile (Join-Path $exportsDir 'Schedule\learning_schedule.csv'))) | Out-Null
    $exportResults.Add((Try-WebDownload -Name 'LearningSchedule DOCX' -Url "$BackendBase/LearningSchedule/download-docx?qualificationId=$qid" -OutFile (Join-Path $exportsDir 'Schedule\learning_schedule.docx'))) | Out-Null
    $exportResults.Add((Try-WebDownload -Name 'LearnerGuide DOCX' -Url "$BackendBase/LearnerGuide/download?qualificationId=$qid" -OutFile (Join-Path $exportsDir 'LearnerGuide\LearnerGuide.docx'))) | Out-Null

    foreach ($s in $subjectsAfter) {
        $sid = I $s.Id
        $scode = S $s.SubjectCode
        if ([string]::IsNullOrWhiteSpace($scode)) { $scode = "Subject_$sid" }
        $scode = ($scode -replace '[^A-Za-z0-9_-]', '_')

        $exportResults.Add((Try-WebDownload -Name "Workbook DOCX S$sid" -Url "$BackendBase/Workbook/download?qualificationId=$qid&subjectId=$sid" -OutFile (Join-Path $exportsDir "Workbook\Workbook_${scode}_S${sid}.docx"))) | Out-Null
        $exportResults.Add((Try-WebDownload -Name "Questionnaire DOCX S$sid" -Url "$BackendBase/KnowledgeQuestionnaire/download?qualificationId=$qid&subjectId=$sid" -OutFile (Join-Path $exportsDir "Questionnaire\Questionnaire_${scode}_S${sid}.docx"))) | Out-Null
        $exportResults.Add((Try-WebDownload -Name "Memorandum DOCX S$sid" -Url "$BackendBase/KnowledgeQuestionnaire/download-memorandum?qualificationId=$qid&subjectId=$sid" -OutFile (Join-Path $exportsDir "Questionnaire\Memorandum_${scode}_S${sid}.docx"))) | Out-Null
    }

    $slidesResult = $null
    try {
        $slidesResult = Invoke-Api -Method POST -Path ("Content/export-slides-by-lpn?qualificationId=$qid")
    }
    catch {
        $slidesResult = [pscustomobject]@{ error = $_.Exception.Message }
    }

    $summary.mappingQueue = [ordered]@{
        queuePath = S $queueBuild.reviewQueue.queuePath
        total = I $queueBuild.reviewQueue.summary.total
        highConfidence = I $queueBuild.reviewQueue.summary.highConfidence
        mediumConfidence = I $queueBuild.reviewQueue.summary.mediumConfidence
        lowConfidence = I $queueBuild.reviewQueue.summary.lowConfidence
        applyHigh = [ordered]@{ processed=I $applyHigh.processed; applied=I $applyHigh.applied; failed=I $applyHigh.failed; skipped=I $applyHigh.skipped }
        applyRemaining = [ordered]@{ processed=I $applyAll.processed; applied=I $applyAll.applied; failed=I $applyAll.failed; skipped=I $applyAll.skipped }
        finalSummary = $applyAll.summary
    }

    $summary.build = [ordered]@{
        seed = $seed
        toolkitSchedule = $toolkitSchedule
        indexDeveloper = $idxDev
        indexLocalUpload = $idxLocal
    }

    $summary.counts = [ordered]@{
        subjects = $subjectsAfter.Count
        topics = $topicsAfter.Count
        assessmentCriteria = $criteriaAfter.Count
        lessonPlans = $lessonPlansAfter.Count
        toolkitRows = $toolkitAfter.Count
        toolkitRowsWithContent = $toolkitNonEmpty
    }

    $summary.autoMapLessonPlans = [ordered]@{
        groupsTotal = $groups.Count
        groupsMapped = $mappedGroups
        groupsFailed = $failedGroups
        rowsPropagated = $propagatedRows
    }

    $summary.exports = [ordered]@{
        outputDir = $exportsDir
        passed = @($exportResults | Where-Object { $_.ok }).Count
        failed = @($exportResults | Where-Object { -not $_.ok }).Count
        results = $exportResults
        slides = $slidesResult
    }

    $summary.paths = [ordered]@{ outRoot = $outRoot; docsRoot = $docsRoot }

    $summaryPath = Join-Path $outRoot '90420_end_to_end_summary.json'
    $mapPath = Join-Path $outRoot '90420_automap_group_results.json'

    $summary | ConvertTo-Json -Depth 25 | Set-Content -Path $summaryPath -Encoding UTF8
    $mapArray = @()
    foreach ($m in $mapDetails) { $mapArray += $m }
    if ($mapArray.Count -eq 0)
    {
        '[]' | Set-Content -Path $mapPath -Encoding UTF8
    }
    else
    {
        $mapArray | ConvertTo-Json -Depth 8 | Set-Content -Path $mapPath -Encoding UTF8
    }

    Copy-Item -Path $summaryPath -Destination (Join-Path $docsRoot (Split-Path $summaryPath -Leaf)) -Force
    Copy-Item -Path $mapPath -Destination (Join-Path $docsRoot (Split-Path $mapPath -Leaf)) -Force
    Copy-Item -Path $exportsDir -Destination (Join-Path $docsRoot 'Exports') -Recurse -Force

    $summary | ConvertTo-Json -Depth 12
}
finally {
    try { Stop-Job -Job $job -Force -ErrorAction SilentlyContinue | Out-Null } catch {}
    try { Receive-Job -Job $job -Keep -ErrorAction SilentlyContinue | Out-Null } catch {}
    try { Remove-Job -Job $job -Force -ErrorAction SilentlyContinue | Out-Null } catch {}
}
