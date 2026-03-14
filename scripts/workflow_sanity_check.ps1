param(
  [string]$PreferredQualificationNumber = '90231',
  [string]$BaseUrl = 'http://localhost:5299',
  [string]$OutputPath = 'artifacts/workflow-sanity-report.json'
)

$ErrorActionPreference = 'Stop'

$backendOut = 'tmp_workflow_backend_stdout.log'
$backendErr = 'tmp_workflow_backend_stderr.log'

if (Test-Path $backendOut) { Remove-Item $backendOut -Force }
if (Test-Path $backendErr) { Remove-Item $backendErr -Force }

$proc = Start-Process -FilePath dotnet -ArgumentList @('run', '--urls', $BaseUrl) -WorkingDirectory 'c:\ETDP\ETDP' -RedirectStandardOutput $backendOut -RedirectStandardError $backendErr -PassThru

function To-Array($value) {
  if ($null -eq $value) { return @() }
  if ($value -is [System.Array]) { return $value }
  return @($value)
}

function Get-Json($url) {
  $res = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 30
  return $res
}

function Get-Array($url) {
  try {
    return To-Array (Get-Json $url)
  } catch {
    return @()
  }
}

function Get-PropString($obj, [string[]]$names) {
  foreach ($n in $names) {
    if ($obj.PSObject.Properties.Name -contains $n) {
      return [string]($obj.$n)
    }
  }
  return ''
}

function Get-PropInt($obj, [string[]]$names) {
  foreach ($n in $names) {
    if ($obj.PSObject.Properties.Name -contains $n) {
      $v = $obj.$n
      if ($null -eq $v) { continue }
      $tmp = 0
      if ([int]::TryParse([string]$v, [ref]$tmp)) { return $tmp }
    }
  }
  return 0
}

try {
  $ready = $false
  for ($i = 0; $i -lt 120; $i++) {
    try {
      $null = Invoke-RestMethod -Uri "$BaseUrl/api/Qualification" -Method Get -TimeoutSec 2
      $ready = $true
      break
    } catch {
      Start-Sleep -Milliseconds 500
    }
  }
  if (-not $ready) { throw "Backend did not start in time at $BaseUrl." }

  $quals = Get-Array "$BaseUrl/api/Qualification"
  if ($quals.Count -eq 0) { throw 'No qualifications returned by API.' }

  $selected = $quals | Where-Object {
    (Get-PropString $_ @('qualificationNumber', 'QualificationNumber')).Trim() -eq $PreferredQualificationNumber
  } | Select-Object -First 1

  if (-not $selected) {
    $selected = $quals | Select-Object -First 1
  }

  $qid = Get-PropInt $selected @('id', 'Id')
  if ($qid -le 0) { throw 'Resolved qualification id is invalid.' }

  $qualificationNumber = (Get-PropString $selected @('qualificationNumber', 'QualificationNumber')).Trim()
  $qualificationDescription = (Get-PropString $selected @('qualificationDescription', 'QualificationDescription')).Trim()

  $demographics = Get-Array "$BaseUrl/api/Demographics/byQualification?qualificationId=$qid"
  $phaseLinks = Get-Array "$BaseUrl/api/QualificationPhase/$qid"
  $phases = Get-Array "$BaseUrl/api/CurriculumPhase"
  $subjects = Get-Array "$BaseUrl/api/Subject/byQualification?qualificationId=$qid"
  $outcomes = Get-Array "$BaseUrl/api/Outcome/byQualification?qualificationId=$qid"
  $topics = Get-Array "$BaseUrl/api/Topic/byQualification?qualificationId=$qid"
  $criteria = Get-Array "$BaseUrl/api/AssessmentCriteria/byQualification?qualificationId=$qid"
  $toolkitAll = Get-Array "$BaseUrl/api/LecturerToolkit"
  $toolkit = @(
    $toolkitAll | Where-Object {
      (Get-PropInt $_ @('qualificationsId', 'QualificationsId')) -eq $qid
    }
  )

  $phaseNameById = @{}
  foreach ($p in $phases) {
    $phaseRowId = Get-PropInt $p @('id', 'Id')
    if ($phaseRowId -le 0) { continue }
    $pname = (Get-PropString $p @('name', 'Name')).Trim()
    $phaseNameById[$phaseRowId] = $pname
  }

  $subjectById = @{}
  $subjectCodeById = @{}
  foreach ($s in $subjects) {
    $sid = Get-PropInt $s @('id', 'Id')
    if ($sid -le 0) { continue }
    $scode = (Get-PropString $s @('subjectCode', 'SubjectCode')).Trim()
    $subjectById[$sid] = $s
    $subjectCodeById[$sid] = $scode
  }

  # Ensure there is at least one toolkit/LPN row for this qualification.
  $createdToolkitEntryId = 0
  if ($toolkit.Count -eq 0 -and $criteria.Count -gt 0 -and $subjects.Count -gt 0) {
    $firstCriteria = $criteria | Select-Object -First 1
    $criteriaId = Get-PropInt $firstCriteria @('id', 'Id')
    $criteriaDesc = (Get-PropString $firstCriteria @('description', 'Description')).Trim()
    $criteriaTopicId = Get-PropInt $firstCriteria @('topicId', 'TopicId')

    $targetSubject = $subjects | Select-Object -First 1
    if ($criteriaTopicId -gt 0) {
      $topicForCriteria = $topics | Where-Object { (Get-PropInt $_ @('id', 'Id')) -eq $criteriaTopicId } | Select-Object -First 1
      if ($topicForCriteria) {
        $subjectIdForTopic = Get-PropInt $topicForCriteria @('subjectId', 'SubjectId')
        if ($subjectIdForTopic -gt 0) {
          $fromTopicSubject = $subjects | Where-Object { (Get-PropInt $_ @('id', 'Id')) -eq $subjectIdForTopic } | Select-Object -First 1
          if ($fromTopicSubject) { $targetSubject = $fromTopicSubject }
        }
      }
    }

    $subjectCode = (Get-PropString $targetSubject @('subjectCode', 'SubjectCode')).Trim()
    $subjectDescription = (Get-PropString $targetSubject @('subjectDescription', 'SubjectDescription')).Trim()

    if (-not [string]::IsNullOrWhiteSpace($subjectCode) -and -not [string]::IsNullOrWhiteSpace($subjectDescription)) {
      $payload = @{
        QualificationsId = $qid
        SubjectCode = $subjectCode
        SubjectDescription = $subjectDescription
        AssessmentCriteriaId = if ($criteriaId -gt 0) { $criteriaId } else { $null }
        AssessmentCriteriaDescription = $criteriaDesc
        Lpn = '1'
        LessonPlanDescription = 'Auto-seeded LPN for sanity workflow check'
        LessonPlanContent = 'Seeded for workflow sanity check.'
      }

      $created = Invoke-RestMethod -Uri "$BaseUrl/api/LecturerToolkit" -Method Post -TimeoutSec 30 -ContentType 'application/json' -Body ($payload | ConvertTo-Json -Depth 6)
      $createdToolkitEntryId = Get-PropInt $created @('id', 'Id')
    }
  }

  # Reload toolkit after optional seed create
  $toolkitAll = Get-Array "$BaseUrl/api/LecturerToolkit"
  $toolkit = @(
    $toolkitAll | Where-Object {
      (Get-PropInt $_ @('qualificationsId', 'QualificationsId')) -eq $qid
    }
  )

  $topicById = @{}
  foreach ($t in $topics) {
    $tid = Get-PropInt $t @('id', 'Id')
    if ($tid -gt 0) { $topicById[$tid] = $t }
  }

  $criteriaById = @{}
  foreach ($c in $criteria) {
    $cid = Get-PropInt $c @('id', 'Id')
    if ($cid -gt 0) { $criteriaById[$cid] = $c }
  }

  $anomalies = New-Object System.Collections.Generic.List[string]

  # Phase mapping check in TopicController response.
  foreach ($t in $topics) {
    $tid = Get-PropInt $t @('id', 'Id')
    $sid = Get-PropInt $t @('subjectId', 'SubjectId')
    $actualPhaseCode = (Get-PropString $t @('phasesCode', 'PhasesCode')).Trim()
    if (-not $subjectById.ContainsKey($sid)) {
      $anomalies.Add("Topic $tid references missing SubjectId $sid")
      continue
    }
    $subject = $subjectById[$sid]
    $phaseId = Get-PropInt $subject @('curriculumPhaseId', 'CurriculumPhaseId')
    $expectedPhaseCode = ''
    if ($phaseNameById.ContainsKey($phaseId)) { $expectedPhaseCode = [string]$phaseNameById[$phaseId] }

    if (-not [string]::IsNullOrWhiteSpace($expectedPhaseCode) -and $actualPhaseCode -ne $expectedPhaseCode) {
      $anomalies.Add("Topic $tid PhasesCode mismatch: expected '$expectedPhaseCode' from phaseId=$phaseId, got '$actualPhaseCode'")
    }
  }

  # Subject/topic code consistency.
  foreach ($t in $topics) {
    $tid = Get-PropInt $t @('id', 'Id')
    $sid = Get-PropInt $t @('subjectId', 'SubjectId')
    $topicSubjectCode = (Get-PropString $t @('subjectCode', 'SubjectCode')).Trim()
    if ($subjectCodeById.ContainsKey($sid)) {
      $expectedSubjectCode = [string]$subjectCodeById[$sid]
      if (-not [string]::IsNullOrWhiteSpace($topicSubjectCode) -and -not [string]::IsNullOrWhiteSpace($expectedSubjectCode) -and $topicSubjectCode -ne $expectedSubjectCode) {
        $anomalies.Add("Topic $tid subject code mismatch: expected '$expectedSubjectCode', got '$topicSubjectCode'")
      }
    }
  }

  # Criteria references must resolve to topic.
  foreach ($c in $criteria) {
    $cid = Get-PropInt $c @('id', 'Id')
    $topicId = Get-PropInt $c @('topicId', 'TopicId')
    if ($topicId -gt 0 -and -not $topicById.ContainsKey($topicId)) {
      $anomalies.Add("AssessmentCriteria $cid references missing TopicId $topicId")
    }
  }

  # Toolkit consistency for Content Builder cascade.
  foreach ($k in $toolkit) {
    $kid = Get-PropInt $k @('id', 'Id')
    $scode = (Get-PropString $k @('subjectCode', 'SubjectCode')).Trim()
    $cid = Get-PropInt $k @('assessmentCriteriaId', 'AssessmentCriteriaId')
    $hasSubject = $false
    foreach ($s in $subjects) {
      if ((Get-PropString $s @('subjectCode', 'SubjectCode')).Trim() -eq $scode) {
        $hasSubject = $true
        break
      }
    }
    if (-not $hasSubject) {
      $anomalies.Add("Toolkit entry $kid references unknown SubjectCode '$scode'")
    }

    if ($cid -gt 0) {
      if (-not $criteriaById.ContainsKey($cid)) {
        $anomalies.Add("Toolkit entry $kid references unknown AssessmentCriteriaId $cid")
      } else {
        $crit = $criteriaById[$cid]
        $topicId = Get-PropInt $crit @('topicId', 'TopicId')
        if ($topicById.ContainsKey($topicId)) {
          $topic = $topicById[$topicId]
          $topicSubjectCode = (Get-PropString $topic @('subjectCode', 'SubjectCode')).Trim()
          if (-not [string]::IsNullOrWhiteSpace($topicSubjectCode) -and -not [string]::IsNullOrWhiteSpace($scode) -and $topicSubjectCode -ne $scode) {
            $anomalies.Add("Toolkit entry $kid mismatch: toolkit subject '$scode' but criteria topic subject '$topicSubjectCode'")
          }
        }
      }
    }
  }

  # Content Builder chain check for at least one toolkit entry.
  $contentBuilderReady = $false
  $contentBuilderCheck = [ordered]@{
    toolkitEntryId = 0
    subjectCode = ''
    criteriaId = 0
    topicId = 0
    subjectFound = $false
    criteriaFound = $false
    topicFound = $false
    subjectCodeMatchesTopic = $false
  }

  $entry = $toolkit | Select-Object -First 1
  if ($entry) {
    $entryId = Get-PropInt $entry @('id', 'Id')
    $entryCode = (Get-PropString $entry @('subjectCode', 'SubjectCode')).Trim()
    $entryCriteriaId = Get-PropInt $entry @('assessmentCriteriaId', 'AssessmentCriteriaId')
    $contentBuilderCheck.toolkitEntryId = $entryId
    $contentBuilderCheck.subjectCode = $entryCode
    $contentBuilderCheck.criteriaId = $entryCriteriaId

    $subjectFound = $subjects | Where-Object { (Get-PropString $_ @('subjectCode', 'SubjectCode')).Trim() -eq $entryCode } | Select-Object -First 1
    if ($subjectFound) { $contentBuilderCheck.subjectFound = $true }

    if ($entryCriteriaId -gt 0 -and $criteriaById.ContainsKey($entryCriteriaId)) {
      $contentBuilderCheck.criteriaFound = $true
      $criteriaRow = $criteriaById[$entryCriteriaId]
      $topicId = Get-PropInt $criteriaRow @('topicId', 'TopicId')
      $contentBuilderCheck.topicId = $topicId
      if ($topicById.ContainsKey($topicId)) {
        $contentBuilderCheck.topicFound = $true
        $topicRow = $topicById[$topicId]
        $topicCode = (Get-PropString $topicRow @('subjectCode', 'SubjectCode')).Trim()
        if ($topicCode -eq $entryCode) {
          $contentBuilderCheck.subjectCodeMatchesTopic = $true
        }
      }
    }

    $contentBuilderReady = $contentBuilderCheck.subjectFound -and $contentBuilderCheck.criteriaFound -and $contentBuilderCheck.topicFound -and $contentBuilderCheck.subjectCodeMatchesTopic
  }

  $lpnValues = @(
    $toolkit | ForEach-Object {
      (Get-PropString $_ @('lpn', 'Lpn', 'LPN')).Trim()
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
  )
  $parsedLpn = @(
    $lpnValues | ForEach-Object {
      $n = 0.0
      $ok = [double]::TryParse(([string]$_ -replace '[^0-9\.-]', ''), [ref]$n)
      if ($ok) {
        [PSCustomObject]@{ raw = $_; n = $n }
      }
    }
  )
  $lpnAscending = $true
  if ($parsedLpn.Count -gt 1) {
    for ($i = 1; $i -lt $parsedLpn.Count; $i++) {
      if ($parsedLpn[$i].n -lt $parsedLpn[$i - 1].n) { $lpnAscending = $false; break }
    }
  }

  $report = [ordered]@{
    timestampUtc = (Get-Date).ToUniversalTime().ToString('o')
    requestedQualificationNumber = $PreferredQualificationNumber
    requestedQualificationFound = ($qualificationNumber -eq $PreferredQualificationNumber)
    qualification = [ordered]@{
      id = $qid
      number = $qualificationNumber
      description = $qualificationDescription
    }
    toolkitSeed = [ordered]@{
      createdToolkitEntryId = $createdToolkitEntryId
      toolkitCount = $toolkit.Count
    }
    counts = [ordered]@{
      demographics = $demographics.Count
      phaseLinks = $phaseLinks.Count
      subjects = $subjects.Count
      outcomes = $outcomes.Count
      topics = $topics.Count
      criteria = $criteria.Count
      toolkit = $toolkit.Count
    }
    contentBuilderCheck = [ordered]@{
      ready = $contentBuilderReady
      details = $contentBuilderCheck
    }
    lpnOrder = [ordered]@{
      ascendingInToolkitList = $lpnAscending
      firstTen = @($lpnValues | Select-Object -First 10)
    }
    anomalyCount = $anomalies.Count
    anomalies = @($anomalies)
  }

  $json = $report | ConvertTo-Json -Depth 8
  $outDir = Split-Path -Path $OutputPath -Parent
  if (-not [string]::IsNullOrWhiteSpace($outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
  }
  Set-Content -Path $OutputPath -Value $json -Encoding UTF8
  Write-Output $json
}
finally {
  if ($proc -and -not $proc.HasExited) {
    Stop-Process -Id $proc.Id -Force
  }
}
