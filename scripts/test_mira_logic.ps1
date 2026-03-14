param(
    [string]$ApiBaseUrl = "http://127.0.0.1:5299",
    [int]$QualificationId = 1,
    [string]$QualificationCode = "",
    [string]$QualificationDescription = "",
    [string]$UserId = "mira-logic-probe",
    [string]$SessionId = "",
    [int]$TimeoutSeconds = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SessionId)) {
    $SessionId = "mira-logic-probe-" + (Get-Date -Format "yyyyMMddHHmmss")
}

$chatUrl = ($ApiBaseUrl.TrimEnd('/')) + "/api/Knowledge/chat"

function Test-EnglishLikely {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }

    $lower = $Text.ToLowerInvariant()
    $foreignHits = ([regex]::Matches($lower, "\b(que|para|nao|não|gracias|obrigado|obrigada|hola|olá|porque|usted|voc[eê]|entao|então)\b")).Count
    $words = @([regex]::Matches($lower, "[a-z]{2,}") | ForEach-Object { $_.Value })
    if ($words.Count -lt 6) { return $true }

    $stop = @("the","and","for","with","from","that","this","which","you","your","is","are","to","in","on")
    $englishHits = @($words | Where-Object { $stop -contains $_ }).Count
    $ratio = if ($words.Count -gt 0) { $englishHits / [double]$words.Count } else { 0.0 }

    if ($foreignHits -ge 2 -and $ratio -lt 0.08) { return $false }
    return ($ratio -ge 0.05 -or $foreignHits -eq 0)
}

function Test-NoiseFree {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    if ($Text.Contains([char]0xFFFD)) { return $false }
    if ($Text -match "Ã|â€") { return $false }
    if ($Text -match "([!?.,;:\-])\1{5,}") { return $false }
    return $true
}

function Test-RoleSeparation {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    $lower = $Text.ToLowerInvariant()
    if ($lower.Contains("i am the user")) { return $false }
    if ($lower.Contains("as the user i")) { return $false }
    if ($lower.Contains("you are mira")) { return $false }
    if ($lower.Contains("i am dr p.c. wepener")) { return $false }
    return $true
}

function Invoke-Chat {
    param([string]$Prompt)

    $body = @{
        message = $Prompt
        qualificationId = $QualificationId
        qualificationCode = if ([string]::IsNullOrWhiteSpace($QualificationCode)) { $null } else { $QualificationCode }
        qualificationDescription = if ([string]::IsNullOrWhiteSpace($QualificationDescription)) { $null } else { $QualificationDescription }
        userId = $UserId
        sessionId = $SessionId
    } | ConvertTo-Json -Depth 8

    $lastError = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $result = Invoke-RestMethod -Uri $chatUrl -Method Post -TimeoutSec $TimeoutSeconds -ContentType "application/json" -Body $body
            return $result
        }
        catch {
            $lastError = $_
            if ($attempt -lt 3) {
                Start-Sleep -Milliseconds (250 * $attempt)
                continue
            }
        }
    }

    if ($null -ne $lastError) {
        throw $lastError
    }

    throw "Unknown chat invocation failure."
}

$tests = @(
    [pscustomobject]@{
        Id = "role_identity"
        Prompt = "In two bullets, state who you are and who I am in this chat."
        MustContain = @("Mira", "you")
        MustNotContain = @("I am the user", "you are Mira")
    },
    [pscustomobject]@{
        Id = "syllogism"
        Prompt = "If all pumps need maintenance and this machine has a pump, what follows logically?"
        MustContain = @("maintenance", "pump")
        MustNotContain = @()
    },
    [pscustomobject]@{
        Id = "math_reasoning"
        Prompt = "Efficiency moved from 60% to 75%. Give absolute and relative increase in one short answer."
        MustContain = @("15", "25")
        MustNotContain = @()
    },
    [pscustomobject]@{
        Id = "subjective_judgement"
        Prompt = "Give your subjective view in 3 points: safety vs production speed in a training workshop."
        MustContain = @("safety")
        MustNotContain = @("knowledge base", "source file")
    },
    [pscustomobject]@{
        Id = "workflow_logic"
        Prompt = "I already uploaded specs and built queue. What should I do next in exact order?"
        MustContain = @("upload", "sync")
        MustNotContain = @("start over")
    }
)

$results = New-Object System.Collections.Generic.List[object]
$allPass = $true

foreach ($test in $tests) {
    Write-Host ""
    Write-Host "Running test: $($test.Id)"
    try {
        $response = Invoke-Chat -Prompt $test.Prompt
        $reply = ""
        if ($null -ne $response -and $null -ne $response.reply) {
            $reply = [string]$response.reply
        }

        $backend = ""
        if ($null -ne $response -and $null -ne $response.backend) {
            $backend = [string]$response.backend
        }

        $englishOk = Test-EnglishLikely -Text $reply
        $noiseOk = Test-NoiseFree -Text $reply
        $roleOk = Test-RoleSeparation -Text $reply

        $containsOk = $true
        foreach ($required in $test.MustContain) {
            if (-not $reply.ToLowerInvariant().Contains(([string]$required).ToLowerInvariant())) {
                $containsOk = $false
                break
            }
        }

        $forbiddenOk = $true
        foreach ($forbidden in $test.MustNotContain) {
            if ($reply.ToLowerInvariant().Contains(([string]$forbidden).ToLowerInvariant())) {
                $forbiddenOk = $false
                break
            }
        }

        $pass = $englishOk -and $noiseOk -and $roleOk -and $containsOk -and $forbiddenOk
        if (-not $pass) { $allPass = $false }

        $results.Add([pscustomobject]@{
            TestId = $test.Id
            Pass = $pass
            Backend = $backend
            English = $englishOk
            NoiseFree = $noiseOk
            RoleSafe = $roleOk
            RequiredTerms = $containsOk
            ForbiddenTerms = $forbiddenOk
            ReplyPreview = ($reply -replace "\s+", " ").Trim().Substring(0, [Math]::Min(180, ($reply -replace "\s+", " ").Trim().Length))
            ErrorMessage = ""
        })
    }
    catch {
        $errMsg = [string]$_.Exception.Message
        $allPass = $false
        $results.Add([pscustomobject]@{
            TestId = $test.Id
            Pass = $false
            Backend = ""
            English = $false
            NoiseFree = $false
            RoleSafe = $false
            RequiredTerms = $false
            ForbiddenTerms = $false
            ReplyPreview = "Request failed: $errMsg"
            ErrorMessage = $errMsg
        })
    }
}

Write-Host ""
Write-Host "Mira logic probe summary:"
$results | Format-Table -AutoSize

$failedWithError = @($results | Where-Object { -not $_.Pass -and -not [string]::IsNullOrWhiteSpace([string]$_.ErrorMessage) })
if ($failedWithError.Count -gt 0) {
    Write-Host ""
    Write-Host "Failure details:"
    foreach ($row in $failedWithError) {
        Write-Host ("- " + $row.TestId + ": " + $row.ErrorMessage)
    }
}

if ($allPass) {
    Write-Host ""
    Write-Host "All probe tests passed."
    exit 0
}

Write-Host ""
Write-Host "Probe tests failed. Recommended corrective path:"
Write-Host "1. Keep the new guardrail path enabled in KnowledgeController."
Write-Host "2. Re-run this script after any model/variant change."
Write-Host "3. If failures persist, switch to deterministic fallback for affected routes."
exit 1
