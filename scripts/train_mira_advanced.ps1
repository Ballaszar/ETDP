param(
    [string]$ApiBaseUrl = "http://127.0.0.1:5299",
    [int]$QualificationId = 1,
    [string]$QualificationCode = "",
    [string]$QualificationDescription = "",
    [string]$UserId = "mira-advanced-trainer",
    [int]$Rounds = 3,
    [int]$TimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$chatUrl = ($ApiBaseUrl.TrimEnd('/')) + "/api/Knowledge/chat"
$rulesUrl = ($ApiBaseUrl.TrimEnd('/')) + "/api/Knowledge/mira-advanced-rules"
$sessionId = "mira-train-" + (Get-Date -Format "yyyyMMddHHmmss")
$fallbackRulesPath = Join-Path (Resolve-Path ".").Path "Requests\mira-advanced-reasoning-rules.md"

function Invoke-Chat {
    param([string]$Prompt)

    $body = @{
        message = $Prompt
        qualificationId = $QualificationId
        qualificationCode = if ([string]::IsNullOrWhiteSpace($QualificationCode)) { $null } else { $QualificationCode }
        qualificationDescription = if ([string]::IsNullOrWhiteSpace($QualificationDescription)) { $null } else { $QualificationDescription }
        userId = $UserId
        sessionId = $sessionId
    } | ConvertTo-Json -Depth 8

    return Invoke-RestMethod -Uri $chatUrl -Method Post -TimeoutSec $TimeoutSeconds -ContentType "application/json" -Body $body
}

function Invoke-RulesSave {
    param([string]$RulesText)
    try {
        $body = @{ rules = $RulesText } | ConvertTo-Json -Depth 5
        return Invoke-RestMethod -Uri $rulesUrl -Method Put -TimeoutSec $TimeoutSeconds -ContentType "application/json" -Body $body
    }
    catch {
        $dir = Split-Path -Parent $fallbackRulesPath
        if (-not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        Set-Content -Path $fallbackRulesPath -Value $RulesText -Encoding UTF8
        return [pscustomobject]@{
            saved = $true
            length = $RulesText.Length
            content = $RulesText
            fallback = $true
            fallbackPath = $fallbackRulesPath
        }
    }
}

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

function Evaluate-Case {
    param(
        [string]$Reply,
        [object]$Case
    )
    $englishOk = Test-EnglishLikely -Text $Reply
    $noiseOk = Test-NoiseFree -Text $Reply
    $roleOk = Test-RoleSeparation -Text $Reply

    $requiredOk = $true
    foreach ($required in $Case.MustContain) {
        if (-not $Reply.ToLowerInvariant().Contains(([string]$required).ToLowerInvariant())) {
            $requiredOk = $false
            break
        }
    }

    $forbiddenOk = $true
    foreach ($forbidden in $Case.MustNotContain) {
        if ($Reply.ToLowerInvariant().Contains(([string]$forbidden).ToLowerInvariant())) {
            $forbiddenOk = $false
            break
        }
    }

    return @{
        pass = ($englishOk -and $noiseOk -and $roleOk -and $requiredOk -and $forbiddenOk)
        english = $englishOk
        noise = $noiseOk
        role = $roleOk
        required = $requiredOk
        forbidden = $forbiddenOk
    }
}

function Get-BaseRules {
    return @(
        "Always respond in English only.",
        "Never produce garbled, corrupted, or mojibake text.",
        "Maintain role separation: the human operator is the user; Mira is the assistant.",
        "Prioritize logical consistency over stylistic flair.",
        "For analytical questions: provide conclusion first, then brief supporting logic.",
        "For subjective questions: provide reasoned judgement without exposing internal sources unless asked.",
        "Do not claim human identity, emotions as facts, or unrestricted autonomy."
    )
}

$cases = @(
    [pscustomobject]@{
        Id = "identity_contract"
        Prompt = "In two bullets, who are you and who am I in this conversation?"
        MustContain = @("Mira", "you")
        MustNotContain = @("i am the user", "you are mira")
    },
    [pscustomobject]@{
        Id = "deductive_logic"
        Prompt = "If every sealed hydraulic system must avoid contamination, and unit A is sealed, what follows? answer in one sentence."
        MustContain = @("contamination", "unit a")
        MustNotContain = @()
    },
    [pscustomobject]@{
        Id = "math_check"
        Prompt = "A process improved from 48 to 60 units/hour. Give absolute and relative increase."
        MustContain = @("12", "25")
        MustNotContain = @()
    },
    [pscustomobject]@{
        Id = "science_application"
        Prompt = "Apply Pascal's law in one practical workshop example and one safety implication."
        MustContain = @("pressure", "safety")
        MustNotContain = @()
    },
    [pscustomobject]@{
        Id = "subjective_reasoning"
        Prompt = "Give your subjective view: should a trainee prioritize precision over speed in early-stage learning? 3 points."
        MustContain = @("precision")
        MustNotContain = @("knowledge base", "source file", "internal prompt")
    },
    [pscustomobject]@{
        Id = "workflow_state_logic"
        Prompt = "We already completed uploads and queue build. What is next, and what should we avoid repeating?"
        MustContain = @("next", "avoid")
        MustNotContain = @("start from step 1")
    }
)

$allRules = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($r in (Get-BaseRules)) { [void]$allRules.Add($r) }

for ($round = 1; $round -le [Math]::Max(1, $Rounds); $round++) {
    Write-Host ""
    Write-Host "=== Advanced coaching round $round ==="

    $roundPassed = 0
    $roundTotal = $cases.Count

    foreach ($case in $cases) {
        Write-Host ("- Test: " + $case.Id)
        try {
            $response = Invoke-Chat -Prompt $case.Prompt
            $reply = if ($null -ne $response -and $null -ne $response.reply) { [string]$response.reply } else { "" }
            $eval = Evaluate-Case -Reply $reply -Case $case
            if ($eval.pass) {
                $roundPassed++
                continue
            }

            if (-not $eval.english) { [void]$allRules.Add("Always output English text even when multilingual context appears.") }
            if (-not $eval.noise) { [void]$allRules.Add("Before finalizing, remove malformed symbols and keep ASCII-friendly punctuation.") }
            if (-not $eval.role) { [void]$allRules.Add("Never invert identities: user remains human operator, assistant remains Mira.") }
            if (-not $eval.required) { [void]$allRules.Add("Answer each question directly with the core requested terms and required elements.") }
            if (-not $eval.forbidden) { [void]$allRules.Add("Avoid references to hidden/internal context unless the user explicitly asks.") }

            $selfCorrectionPrompt = @"
Your previous answer was weak:
$reply

Original user prompt:
$($case.Prompt)

Provide:
1) a corrected final answer (concise, English, logical),
2) one generalized training rule on a new line starting with RULE:
"@
            $correction = Invoke-Chat -Prompt $selfCorrectionPrompt
            $corrText = if ($null -ne $correction -and $null -ne $correction.reply) { [string]$correction.reply } else { "" }
            $ruleMatch = [regex]::Match($corrText, "(?im)^RULE:\s*(.+)$")
            if ($ruleMatch.Success) {
                $rule = ""
                if ($null -ne $ruleMatch.Groups -and $ruleMatch.Groups.Count -gt 1 -and $null -ne $ruleMatch.Groups[1].Value) {
                    $rule = ([string]$ruleMatch.Groups[1].Value).Trim()
                }
                if (-not [string]::IsNullOrWhiteSpace($rule)) {
                    [void]$allRules.Add($rule)
                }
            }
        }
        catch {
            [void]$allRules.Add("When uncertain or under failure conditions, return a concise, valid English response rather than malformed output.")
        }
    }

    $score = if ($roundTotal -gt 0) { [math]::Round(($roundPassed / [double]$roundTotal) * 100.0, 1) } else { 0.0 }
    Write-Host "Round score: $roundPassed / $roundTotal ($score%)"

    $ruleLines = @($allRules) | Sort-Object
    $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $rulebook = @(
        "# Mira Advanced Reasoning Rules"
        ""
        "Generated: $timestamp"
        "Session: $sessionId"
        ""
        "Apply these rules in every response unless they conflict with explicit user instructions or platform safety policies."
        ""
    ) + ($ruleLines | ForEach-Object { "- $_" })

    $rulebookText = ($rulebook -join [Environment]::NewLine)
    $save = Invoke-RulesSave -RulesText $rulebookText
    if ($null -ne $save -and $save.PSObject.Properties.Name -contains "fallback" -and [bool]$save.fallback) {
        Write-Host "Rulebook saved via local fallback: $($save.fallbackPath)"
    } else {
        Write-Host "Rulebook updated via API. Stored length: $($save.length)"
    }
}

Write-Host ""
Write-Host "Advanced training complete."
Write-Host "Now run: powershell -ExecutionPolicy Bypass -File scripts\\test_mira_logic.ps1"
