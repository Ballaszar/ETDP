param(
    [Parameter(Mandatory = $true)][string]$Text,
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$Voice = "",
    [int]$Rate = -2
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Speech

$dir = Split-Path -Parent $Output
if ($dir) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    if ($Voice) {
        $match = $synth.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Name -like "*$Voice*" } | Select-Object -First 1
        if ($match) {
            $synth.SelectVoice($match.VoiceInfo.Name)
        }
    }

    if ($Rate -lt -10) { $Rate = -10 }
    if ($Rate -gt 10) { $Rate = 10 }
    $synth.Rate = $Rate

    if ([string]::IsNullOrWhiteSpace($Text)) {
        $safeText = " "
    } else {
        $safeText = $Text
    }
    $synth.SetOutputToWaveFile($Output)
    $synth.Speak($safeText)
}
finally {
    $synth.Dispose()
}

Write-Output $Output
