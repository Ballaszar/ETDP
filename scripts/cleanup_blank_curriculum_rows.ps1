param(
    [string]$BackendRoot = "http://localhost:5299",
    [string]$ProjectDir = "",
    [string]$QualificationNumber = "94020",
    [int]$QualificationId = 0,
    [bool]$DeleteBlankTopics = $true,
    [bool]$Apply = $false
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

function ToArray {
    param([object]$Value)
    if ($null -eq $Value) { return @() }
    if ($Value -is [System.Array]) { return $Value }
    return @($Value)
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

function IsBlank {
    param([string]$Value)
    return [string]::IsNullOrWhiteSpace(($Value ?? "").Trim())
}

$api = "$($BackendRoot.TrimEnd('/'))/api"
$proc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--no-build", "--urls", $BackendRoot) -WorkingDirectory $ProjectDir -PassThru

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
    if (-not $q) {
        throw "Qualification not found. id=$QualificationId number=$QualificationNumber"
    }

    $qid = AsInt (GetProp $q @("id","Id"))
    $qnum = AsText (GetProp $q @("qualificationNumber","QualificationNumber"))

    $subjects = ToArray (Invoke-RestMethod -Uri "$api/Subject/byQualification?qualificationId=$qid" -Method Get -TimeoutSec 30)
    $blankSubjects = @()
    $blankTopics = @()

    foreach ($s in $subjects) {
        $sid = AsInt (GetProp $s @("id","Id"))
        $scode = AsText (GetProp $s @("subjectCode","SubjectCode","phasesCode","PhasesCode"))
        $sdesc = AsText (GetProp $s @("subjectDescription","SubjectDescription"))
        $topics = @()
        try {
            $topics = ToArray (Invoke-RestMethod -Uri "$api/Topic/bySubject?subjectId=$sid" -Method Get -TimeoutSec 30)
        } catch {
            $topics = @()
        }

        if ((IsBlank $scode) -and (IsBlank $sdesc)) {
            $blankSubjects += [pscustomobject]@{
                subjectId = $sid
                subjectCode = $scode
                subjectDescription = $sdesc
                topicCount = $topics.Count
            }
        }

        foreach ($t in $topics) {
            $tid = AsInt (GetProp $t @("id","Id"))
            $tcode = AsText (GetProp $t @("topicCode","TopicCode"))
            $tdesc = AsText (GetProp $t @("topicDescription","TopicDescription"))
            if ((IsBlank $tcode) -and (IsBlank $tdesc)) {
                $blankTopics += [pscustomobject]@{
                    topicId = $tid
                    subjectId = $sid
                    subjectCode = $scode
                    topicCode = $tcode
                    topicDescription = $tdesc
                }
            }
        }
    }

    $deletedSubjectIds = New-Object System.Collections.Generic.List[int]
    $deletedTopicIds = New-Object System.Collections.Generic.List[int]
    $deleteErrors = New-Object System.Collections.Generic.List[string]

    if ($Apply) {
        if ($DeleteBlankTopics) {
            foreach ($bt in $blankTopics) {
                $sid = [int]$bt.subjectId
                if ($blankSubjects | Where-Object { [int]$_.subjectId -eq $sid }) {
                    continue
                }
                try {
                    Invoke-RestMethod -Uri "$api/Topic/$($bt.topicId)" -Method Delete -TimeoutSec 30 | Out-Null
                    $deletedTopicIds.Add([int]$bt.topicId) | Out-Null
                } catch {
                    $deleteErrors.Add("Topic $($bt.topicId): $($_.Exception.Message)") | Out-Null
                }
            }
        }

        foreach ($bs in $blankSubjects) {
            try {
                Invoke-RestMethod -Uri "$api/Subject/$($bs.subjectId)" -Method Delete -TimeoutSec 30 | Out-Null
                $deletedSubjectIds.Add([int]$bs.subjectId) | Out-Null
            } catch {
                $deleteErrors.Add("Subject $($bs.subjectId): $($_.Exception.Message)") | Out-Null
            }
        }
    }

    $subjectsAfter = ToArray (Invoke-RestMethod -Uri "$api/Subject/byQualification?qualificationId=$qid" -Method Get -TimeoutSec 30)
    $blankSubjectsAfter = @($subjectsAfter | Where-Object {
        $scode = AsText (GetProp $_ @("subjectCode","SubjectCode","phasesCode","PhasesCode"))
        $sdesc = AsText (GetProp $_ @("subjectDescription","SubjectDescription"))
        (IsBlank $scode) -and (IsBlank $sdesc)
    })

    [ordered]@{
        qualificationId = $qid
        qualificationNumber = $qnum
        apply = $Apply
        deleteBlankTopics = $DeleteBlankTopics
        before = [ordered]@{
            subjectCount = $subjects.Count
            blankSubjectCount = $blankSubjects.Count
            blankTopicCount = $blankTopics.Count
            blankSubjects = $blankSubjects
            blankTopics = $blankTopics
        }
        actions = [ordered]@{
            deletedSubjectIds = @($deletedSubjectIds)
            deletedTopicIds = @($deletedTopicIds)
            errors = @($deleteErrors)
        }
        after = [ordered]@{
            subjectCount = $subjectsAfter.Count
            blankSubjectCount = $blankSubjectsAfter.Count
        }
    } | ConvertTo-Json -Depth 10
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
    }
}
