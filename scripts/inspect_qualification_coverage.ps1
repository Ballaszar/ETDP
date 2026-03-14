param(
    [string]$BackendRoot = "http://localhost:5299",
    [string]$ProjectDir = "",
    [string]$QualificationNumber = "90420",
    [int]$QualificationId = 0
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
        $available = $qualifications |
            Select-Object -First 50 |
            ForEach-Object { AsText (GetProp $_ @("qualificationNumber","QualificationNumber")) } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
        $availableText = if ($available.Count -gt 0) { ($available -join ", ") } else { "<none>" }
        throw "Qualification not found. Requested id=$QualificationId number=$QualificationNumber. Available numbers: $availableText"
    }

    $qid = AsInt (GetProp $q @("id","Id"))
    $qnum = AsText (GetProp $q @("qualificationNumber","QualificationNumber"))
    $qdesc = AsText (GetProp $q @("qualificationDescription","QualificationDescription"))
    $subjects = ToArray (Invoke-RestMethod -Uri "$api/Subject/byQualification?qualificationId=$qid" -Method Get -TimeoutSec 30)

    $rows = @()
    foreach ($s in $subjects) {
        $sid = AsInt (GetProp $s @("id","Id"))
        $topics = @()
        try {
            $topics = ToArray (Invoke-RestMethod -Uri "$api/Topic/bySubject?subjectId=$sid" -Method Get -TimeoutSec 30)
        } catch {
            $topics = @()
        }
        $rows += [pscustomobject]@{
            subjectId = $sid
            subjectCode = AsText (GetProp $s @("subjectCode","SubjectCode","phasesCode","PhasesCode"))
            subjectDescription = AsText (GetProp $s @("subjectDescription","SubjectDescription"))
            topicCount = $topics.Count
        }
    }

    $summary = [ordered]@{
        qualificationId = $qid
        qualificationNumber = $qnum
        qualificationDescription = $qdesc
        subjectCount = $subjects.Count
        subjectsWithTopics = @($rows | Where-Object { $_.topicCount -gt 0 }).Count
        subjectsWithoutTopics = @($rows | Where-Object { $_.topicCount -le 0 }).Count
        totalTopics = (($rows | Measure-Object -Property topicCount -Sum).Sum)
    }

    $output = [ordered]@{
        summary = $summary
        subjects = @($rows | Sort-Object subjectCode, subjectId)
    }

    $output | ConvertTo-Json -Depth 8
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
    }
}
