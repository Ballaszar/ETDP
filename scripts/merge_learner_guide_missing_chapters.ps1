param(
    [Parameter(Mandatory = $true)]
    [string]$CorrectedSampleDocx,

    [Parameter(Mandatory = $true)]
    [string]$GeneratedFullDocx,

    [Parameter(Mandatory = $true)]
    [string]$OutputDocx
)

$ErrorActionPreference = "Stop"

$script:ChapterRegex = [regex]'(?i)^\s*chapter\s*(\d+)\s*$'
$script:SummaryRegexA = [regex]'(?i)^\s*chapter\s+\d+\s+summary\s*$'
$script:SummaryRegexB = [regex]'(?i)^\s*summary\s+of\s+chapter\s+\d+\s*$'
$script:SubjectCodeRegex = [regex]'KM-\d{2}-KT\d{2}'
$script:TopicRegex = [regex]'(?i)^\s*(?:topic\s+)?([a-z]{2}\d{4})\s*:'

function Expand-Docx {
    param(
        [string]$DocxPath,
        [string]$FolderPath
    )
    if (Test-Path -LiteralPath $FolderPath) {
        Remove-Item -LiteralPath $FolderPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $FolderPath | Out-Null
    Expand-Archive -LiteralPath $DocxPath -DestinationPath $FolderPath -Force
}

function Save-XmlUtf8NoBom {
    param(
        [xml]$XmlDoc,
        [string]$Path
    )
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.Indent = $false
    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $XmlDoc.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

function Normalize-Text {
    param([string]$Text)
    if ($null -eq $Text) { return "" }
    return (($Text -replace '\s+', ' ').Trim())
}

function Get-ParagraphRows {
    param(
        [xml]$XmlDoc,
        [System.Xml.XmlNamespaceManager]$NsMgr
    )
    $paras = $XmlDoc.SelectNodes('//w:body/w:p', $NsMgr)
    $rows = @()
    for ($i = 0; $i -lt $paras.Count; $i++) {
        $p = $paras[$i]
        $raw = ($p.SelectNodes('.//w:t', $NsMgr) | ForEach-Object { $_.'#text' }) -join ''
        $rows += [pscustomobject]@{
            index = $i
            text = (Normalize-Text $raw)
            node = $p
        }
    }
    return $rows
}

function Get-ChapterBlocks {
    param([object[]]$Rows)

    $markerIndices = @()
    foreach ($r in $Rows) {
        if ($script:ChapterRegex.IsMatch($r.text)) {
            $markerIndices += $r.index
        }
    }
    if ($markerIndices.Count -eq 0) { return @() }

    $blocks = @()
    for ($i = 0; $i -lt $markerIndices.Count; $i++) {
        $start = [int]$markerIndices[$i]
        $end = if ($i -lt $markerIndices.Count - 1) { [int]$markerIndices[$i + 1] } else { $Rows.Count }
        $subjectCode = ""
        for ($j = $start + 1; $j -lt [Math]::Min($end, $start + 40); $j++) {
            $m = $script:SubjectCodeRegex.Match($Rows[$j].text)
            if ($m.Success) {
                $subjectCode = $m.Value.ToUpperInvariant()
                break
            }
        }
        $blocks += [pscustomobject]@{
            start = $start
            end = $end
            markerText = $Rows[$start].text
            subjectCode = $subjectCode
        }
    }

    return $blocks
}

function Find-CanonicalChapterStart {
    param([object[]]$ChapterBlocks)
    if ($ChapterBlocks.Count -eq 0) { return -1 }

    foreach ($b in $ChapterBlocks) {
        $m = $script:ChapterRegex.Match($b.markerText)
        if ($m.Success -and [int]$m.Groups[1].Value -eq 1 -and $b.subjectCode -eq "KM-01-KT01") {
            return [int]$b.start
        }
    }
    foreach ($b in $ChapterBlocks) {
        if ($b.subjectCode -eq "KM-01-KT01") {
            return [int]$b.start
        }
    }
    return [int]$ChapterBlocks[0].start
}

function Remove-BodyRangeByParagraphIndices {
    param(
        [System.Xml.XmlNode]$Body,
        [object[]]$Rows,
        [int]$StartParagraphIndex,
        [int]$EndParagraphIndex
    )

    if ($StartParagraphIndex -lt 0 -or $EndParagraphIndex -lt $StartParagraphIndex -or $EndParagraphIndex -ge $Rows.Count) {
        return 0
    }

    $startNode = $Rows[$StartParagraphIndex].node
    $endNode = $Rows[$EndParagraphIndex].node
    $children = @($Body.ChildNodes)
    $startChildIndex = -1
    $endChildIndex = -1
    for ($i = 0; $i -lt $children.Count; $i++) {
        if ($startChildIndex -lt 0 -and $children[$i] -eq $startNode) {
            $startChildIndex = $i
        }
        if ($children[$i] -eq $endNode) {
            $endChildIndex = $i
        }
    }
    if ($startChildIndex -lt 0 -or $endChildIndex -lt $startChildIndex) {
        return 0
    }

    $removed = 0
    for ($i = $endChildIndex; $i -ge $startChildIndex; $i--) {
        [void]$Body.RemoveChild($children[$i])
        $removed++
    }
    return $removed
}

function Find-ChapterSummaryIndex {
    param(
        [object[]]$Rows,
        [int]$ChapterStart,
        [int]$ChapterEnd
    )
    for ($i = $ChapterStart + 1; $i -lt $ChapterEnd; $i++) {
        $t = $Rows[$i].text
        if ($script:SummaryRegexA.IsMatch($t) -or $script:SummaryRegexB.IsMatch($t)) {
            return $i
        }
    }
    return -1
}

function Get-TopicBlocks {
    param(
        [object[]]$Rows,
        [int]$ChapterStart,
        [int]$ChapterEnd
    )
    $summaryIndex = Find-ChapterSummaryIndex -Rows $Rows -ChapterStart $ChapterStart -ChapterEnd $ChapterEnd
    $scanEnd = if ($summaryIndex -ge 0) { $summaryIndex } else { $ChapterEnd }

    $topicStarts = @()
    for ($i = $ChapterStart + 1; $i -lt $scanEnd; $i++) {
        $m = $script:TopicRegex.Match($Rows[$i].text)
        if ($m.Success) {
            $topicStarts += [pscustomobject]@{
                start = $i
                code = $m.Groups[1].Value.ToUpperInvariant()
            }
        }
    }

    $blocks = @()
    for ($i = 0; $i -lt $topicStarts.Count; $i++) {
        $start = $topicStarts[$i].start
        $end = if ($i -lt $topicStarts.Count - 1) { $topicStarts[$i + 1].start } else { $scanEnd }
        $blocks += [pscustomobject]@{
            start = $start
            end = $end
            code = $topicStarts[$i].code
        }
    }
    return $blocks
}

function Import-ParagraphRange {
    param(
        [xml]$TargetXml,
        [System.Xml.XmlNode]$TargetBody,
        [object[]]$SourceRows,
        [int]$StartIndex,
        [int]$EndIndex,
        [System.Xml.XmlNode]$InsertBeforeNode
    )

    $count = 0
    $effectiveInsertBefore = $InsertBeforeNode
    if ($null -eq $effectiveInsertBefore) {
        $effectiveInsertBefore = @($TargetBody.ChildNodes) |
            Where-Object {
                $_.LocalName -eq "sectPr" -and
                $_.NamespaceURI -eq "http://schemas.openxmlformats.org/wordprocessingml/2006/main"
            } |
            Select-Object -First 1
    }

    for ($i = $StartIndex; $i -lt $EndIndex; $i++) {
        $imported = $TargetXml.ImportNode($SourceRows[$i].node, $true)
        if ($null -ne $effectiveInsertBefore) {
            [void]$TargetBody.InsertBefore($imported, $effectiveInsertBefore)
        }
        else {
            [void]$TargetBody.AppendChild($imported)
        }
        $count++
    }
    return $count
}

function Set-ParagraphText {
    param(
        [xml]$XmlDoc,
        [System.Xml.XmlNamespaceManager]$NsMgr,
        [System.Xml.XmlNode]$ParagraphNode,
        [string]$NewText
    )
    $textNodes = $ParagraphNode.SelectNodes('.//w:t', $NsMgr)
    if ($textNodes.Count -gt 0) {
        $textNodes[0].InnerText = $NewText
        for ($i = 1; $i -lt $textNodes.Count; $i++) {
            $textNodes[$i].InnerText = ""
        }
        return
    }

    $wNs = $NsMgr.LookupNamespace("w")
    $run = $XmlDoc.CreateElement("w", "r", $wNs)
    $text = $XmlDoc.CreateElement("w", "t", $wNs)
    $text.InnerText = $NewText
    [void]$run.AppendChild($text)
    [void]$ParagraphNode.AppendChild($run)
}

function Ensure-UpdateFieldsOnOpen {
    param([string]$OutDir)

    $settingsPath = Join-Path $OutDir "word\settings.xml"
    if (-not (Test-Path -LiteralPath $settingsPath)) {
        return
    }

    [xml]$settingsXml = Get-Content -LiteralPath $settingsPath -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($settingsXml.NameTable)
    $ns.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
    $root = $settingsXml.SelectSingleNode('/w:settings', $ns)
    if ($null -eq $root) {
        return
    }

    $node = $settingsXml.SelectSingleNode('/w:settings/w:updateFields', $ns)
    if ($null -eq $node) {
        $node = $settingsXml.CreateElement("w", "updateFields", $ns.LookupNamespace("w"))
        [void]$root.AppendChild($node)
    }
    $node.SetAttribute("val", $ns.LookupNamespace("w"), "true")
    Save-XmlUtf8NoBom -XmlDoc $settingsXml -Path $settingsPath
}

if (-not (Test-Path -LiteralPath $CorrectedSampleDocx)) {
    throw "Corrected sample file not found: $CorrectedSampleDocx"
}
if (-not (Test-Path -LiteralPath $GeneratedFullDocx)) {
    throw "Generated full file not found: $GeneratedFullDocx"
}

$outputDir = Split-Path -Parent $OutputDocx
if (-not [string]::IsNullOrWhiteSpace($outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("lg_merge_" + [guid]::NewGuid().ToString("N"))
$sampleDir = Join-Path $tmpRoot "sample"
$fullDir = Join-Path $tmpRoot "full"
$outDir = Join-Path $tmpRoot "out"

$removedLegacyNodes = 0
$insertedMissingTopicNodes = 0
$insertedSummaryNodes = 0
$appendedChapterNodes = 0
$chaptersRenumbered = 0

try {
    Expand-Docx -DocxPath $CorrectedSampleDocx -FolderPath $sampleDir
    Expand-Docx -DocxPath $GeneratedFullDocx -FolderPath $fullDir

    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    Copy-Item -Path (Join-Path $sampleDir "*") -Destination $outDir -Recurse -Force

    $sampleXmlPath = Join-Path $outDir "word\document.xml"
    $fullXmlPath = Join-Path $fullDir "word\document.xml"
    if (-not (Test-Path -LiteralPath $sampleXmlPath)) { throw "Missing word/document.xml in corrected sample." }
    if (-not (Test-Path -LiteralPath $fullXmlPath)) { throw "Missing word/document.xml in generated full doc." }

    [xml]$sampleXml = Get-Content -LiteralPath $sampleXmlPath -Raw
    [xml]$fullXml = Get-Content -LiteralPath $fullXmlPath -Raw

    $sampleNs = New-Object System.Xml.XmlNamespaceManager($sampleXml.NameTable)
    $sampleNs.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")
    $fullNs = New-Object System.Xml.XmlNamespaceManager($fullXml.NameTable)
    $fullNs.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")

    $sampleBody = $sampleXml.SelectSingleNode('//w:body', $sampleNs)
    $fullBody = $fullXml.SelectSingleNode('//w:body', $fullNs)
    if ($null -eq $sampleBody -or $null -eq $fullBody) {
        throw "Invalid DOCX XML: missing body."
    }

    $sampleRows = Get-ParagraphRows -XmlDoc $sampleXml -NsMgr $sampleNs
    $sampleBlocks = Get-ChapterBlocks -Rows $sampleRows
    $canonicalStart = Find-CanonicalChapterStart -ChapterBlocks $sampleBlocks
    if ($canonicalStart -lt 0) {
        throw "Could not locate canonical chapter sequence in corrected sample."
    }

    $legacyBlock = $sampleBlocks |
        Where-Object { $_.start -lt $canonicalStart } |
        Sort-Object start |
        Select-Object -First 1
    if ($legacyBlock) {
        $removedLegacyNodes = Remove-BodyRangeByParagraphIndices `
            -Body $sampleBody `
            -Rows $sampleRows `
            -StartParagraphIndex ([int]$legacyBlock.start) `
            -EndParagraphIndex ($canonicalStart - 1)
    }

    $fullRows = Get-ParagraphRows -XmlDoc $fullXml -NsMgr $fullNs
    $fullBlocks = Get-ChapterBlocks -Rows $fullRows
    $fullBySubject = @{}
    foreach ($fb in $fullBlocks) {
        if ([string]::IsNullOrWhiteSpace($fb.subjectCode)) { continue }
        if (-not $fullBySubject.ContainsKey($fb.subjectCode)) {
            $fullBySubject[$fb.subjectCode] = $fb
        }
    }

    while ($true) {
        $sampleRows = Get-ParagraphRows -XmlDoc $sampleXml -NsMgr $sampleNs
        $sampleBlocks = Get-ChapterBlocks -Rows $sampleRows
        $canonicalStart = Find-CanonicalChapterStart -ChapterBlocks $sampleBlocks
        $canonicalBlocks = $sampleBlocks | Where-Object { $_.start -ge $canonicalStart } | Sort-Object start

        $changed = $false
        foreach ($sb in $canonicalBlocks) {
            if ([string]::IsNullOrWhiteSpace($sb.subjectCode)) { continue }
            if (-not $fullBySubject.ContainsKey($sb.subjectCode)) { continue }
            $fb = $fullBySubject[$sb.subjectCode]

            $sampleTopicBlocks = Get-TopicBlocks -Rows $sampleRows -ChapterStart $sb.start -ChapterEnd $sb.end
            $fullTopicBlocks = Get-TopicBlocks -Rows $fullRows -ChapterStart $fb.start -ChapterEnd $fb.end

            $sampleTopicSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
            foreach ($tb in $sampleTopicBlocks) { [void]$sampleTopicSet.Add($tb.code) }

            $missingTopicBlocks = @($fullTopicBlocks | Where-Object { -not $sampleTopicSet.Contains($_.code) })
            $sampleSummaryIndex = Find-ChapterSummaryIndex -Rows $sampleRows -ChapterStart $sb.start -ChapterEnd $sb.end
            $fullSummaryIndex = Find-ChapterSummaryIndex -Rows $fullRows -ChapterStart $fb.start -ChapterEnd $fb.end

            if ($missingTopicBlocks.Count -eq 0 -and -not ($sampleSummaryIndex -lt 0 -and $fullSummaryIndex -ge 0)) {
                continue
            }

            $insertBeforeParagraphIndex = if ($sampleSummaryIndex -ge 0) { $sampleSummaryIndex } else { $sb.end }
            $insertBeforeNode = if ($insertBeforeParagraphIndex -ge 0 -and $insertBeforeParagraphIndex -lt $sampleRows.Count) {
                $sampleRows[$insertBeforeParagraphIndex].node
            }
            else {
                $null
            }

            foreach ($tb in $missingTopicBlocks) {
                $insertedMissingTopicNodes += Import-ParagraphRange `
                    -TargetXml $sampleXml `
                    -TargetBody $sampleBody `
                    -SourceRows $fullRows `
                    -StartIndex $tb.start `
                    -EndIndex $tb.end `
                    -InsertBeforeNode $insertBeforeNode
            }

            if ($sampleSummaryIndex -lt 0 -and $fullSummaryIndex -ge 0) {
                $insertedSummaryNodes += Import-ParagraphRange `
                    -TargetXml $sampleXml `
                    -TargetBody $sampleBody `
                    -SourceRows $fullRows `
                    -StartIndex $fullSummaryIndex `
                    -EndIndex $fb.end `
                    -InsertBeforeNode $insertBeforeNode
            }

            $changed = $true
            break
        }

        if (-not $changed) { break }
    }

    $sampleRows = Get-ParagraphRows -XmlDoc $sampleXml -NsMgr $sampleNs
    $sampleBlocks = Get-ChapterBlocks -Rows $sampleRows
    $canonicalStart = Find-CanonicalChapterStart -ChapterBlocks $sampleBlocks
    $canonicalBlocks = $sampleBlocks | Where-Object { $_.start -ge $canonicalStart } | Sort-Object start

    $existingSubjectSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($b in $canonicalBlocks) {
        if (-not [string]::IsNullOrWhiteSpace($b.subjectCode)) {
            [void]$existingSubjectSet.Add($b.subjectCode)
        }
    }

    $firstMissingFullBlock = $null
    foreach ($fb in $fullBlocks | Sort-Object start) {
        if ([string]::IsNullOrWhiteSpace($fb.subjectCode)) { continue }
        if (-not $existingSubjectSet.Contains($fb.subjectCode)) {
            $firstMissingFullBlock = $fb
            break
        }
    }

    if ($firstMissingFullBlock) {
        $sampleSectPr = $sampleBody.SelectSingleNode('./w:sectPr', $sampleNs)
        for ($i = $firstMissingFullBlock.start; $i -lt $fullRows.Count; $i++) {
            $imported = $sampleXml.ImportNode($fullRows[$i].node, $true)
            if ($null -ne $sampleSectPr) {
                [void]$sampleBody.InsertBefore($imported, $sampleSectPr)
            }
            else {
                [void]$sampleBody.AppendChild($imported)
            }
            $appendedChapterNodes++
        }
    }

    $sampleRows = Get-ParagraphRows -XmlDoc $sampleXml -NsMgr $sampleNs
    $sampleBlocks = Get-ChapterBlocks -Rows $sampleRows
    $canonicalStart = Find-CanonicalChapterStart -ChapterBlocks $sampleBlocks
    $canonicalBlocks = $sampleBlocks | Where-Object { $_.start -ge $canonicalStart } | Sort-Object start

    $chapterNumber = 1
    foreach ($b in $canonicalBlocks) {
        Set-ParagraphText -XmlDoc $sampleXml -NsMgr $sampleNs -ParagraphNode $sampleRows[$b.start].node -NewText ("CHAPTER {0}" -f $chapterNumber)
        $summaryIndex = Find-ChapterSummaryIndex -Rows $sampleRows -ChapterStart $b.start -ChapterEnd $b.end
        if ($summaryIndex -ge 0) {
            $currentSummary = $sampleRows[$summaryIndex].text
            if ($script:SummaryRegexB.IsMatch($currentSummary)) {
                Set-ParagraphText -XmlDoc $sampleXml -NsMgr $sampleNs -ParagraphNode $sampleRows[$summaryIndex].node -NewText ("Summary of Chapter {0}" -f $chapterNumber)
            }
            else {
                Set-ParagraphText -XmlDoc $sampleXml -NsMgr $sampleNs -ParagraphNode $sampleRows[$summaryIndex].node -NewText ("Chapter {0} Summary" -f $chapterNumber)
            }
        }
        $chapterNumber++
        $chaptersRenumbered++
    }

    Save-XmlUtf8NoBom -XmlDoc $sampleXml -Path $sampleXmlPath
    Ensure-UpdateFieldsOnOpen -OutDir $outDir

    if (Test-Path -LiteralPath $OutputDocx) {
        Remove-Item -LiteralPath $OutputDocx -Force
    }
    $zipPath = [System.IO.Path]::ChangeExtension($OutputDocx, ".zip")
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath -Force
    Move-Item -LiteralPath $zipPath -Destination $OutputDocx -Force

    [pscustomobject]@{
        correctedSample = (Resolve-Path -LiteralPath $CorrectedSampleDocx).Path
        generatedFull = (Resolve-Path -LiteralPath $GeneratedFullDocx).Path
        output = (Resolve-Path -LiteralPath $OutputDocx).Path
        removedLegacyNodes = $removedLegacyNodes
        insertedMissingTopicNodes = $insertedMissingTopicNodes
        insertedSummaryNodes = $insertedSummaryNodes
        appendedChapterNodes = $appendedChapterNodes
        chaptersRenumbered = $chaptersRenumbered
    } | ConvertTo-Json -Depth 6
}
finally {
    if (Test-Path -LiteralPath $tmpRoot) {
        Remove-Item -LiteralPath $tmpRoot -Recurse -Force
    }
}
