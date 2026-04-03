using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using System.Xml;

namespace ETD.Api.Utils
{
    public static class DocxCoverPageOverlay
    {
        public sealed class CoverTextLine
        {
            public string Text { get; init; } = string.Empty;
            public int FontSizeHalfPt { get; init; } = 24;
            public bool Bold { get; init; }
            public int BeforeTwips { get; init; }
            public int AfterTwips { get; init; }
            public string ColorHex { get; init; } = "FFFFFF";
        }

        private const int PortraitCoverSourceWidthPx = 576;
        private const int PortraitCoverSourceHeightPx = 794;
        private const string DefaultCoverFont = "Times New Roman";

        public static bool TryAppendStandardPortraitCoverPage(
            Body body,
            MainDocumentPart main,
            string? coverPath,
            string? qualificationLine,
            string? subjectLine,
            string? institutionLine,
            uint usableWidthTwips,
            uint drawingId)
        {
            var resolvedCoverPath = (coverPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(resolvedCoverPath) || !System.IO.File.Exists(resolvedCoverPath))
            {
                return false;
            }

            var imageWidthEmu = TwipsToEmu(usableWidthTwips);
            var imageHeightEmu = ScaleHeight(imageWidthEmu, PortraitCoverSourceWidthPx, PortraitCoverSourceHeightPx);
            AppendAnchoredImage(body, main, resolvedCoverPath, imageWidthEmu, imageHeightEmu, drawingId, 0L, 0L, behindText: true);

            var imageHeightTwips = Math.Max(1L, imageHeightEmu / 635L);
            var topBeforeTwips = Math.Max(1800, (int)Math.Round(imageHeightTwips * 0.235d));
            var subjectBeforeTwips = 0;
            var institutionBeforeTwips = Math.Max(4200, (int)Math.Round(imageHeightTwips * 0.47d));
            var leftIndentTwips = Math.Max(520, (int)Math.Round(usableWidthTwips * 0.085d));

            if (!string.IsNullOrWhiteSpace(qualificationLine))
            {
                body.Append(BuildCoverParagraph(
                    qualificationLine,
                    DetermineTopBandFontSizeHalfPt(qualificationLine),
                    bold: true,
                    centered: false,
                    beforeTwips: topBeforeTwips,
                    afterTwips: 0,
                    leftIndentTwips: leftIndentTwips));
            }

            if (!string.IsNullOrWhiteSpace(subjectLine))
            {
                body.Append(BuildCoverParagraph(
                    subjectLine,
                    DetermineTopBandFontSizeHalfPt(subjectLine),
                    bold: true,
                    centered: false,
                    beforeTwips: subjectBeforeTwips,
                    afterTwips: 0,
                    leftIndentTwips: leftIndentTwips));
            }

            if (!string.IsNullOrWhiteSpace(institutionLine))
            {
                body.Append(BuildCoverParagraph(
                    institutionLine,
                    DetermineInstitutionFontSizeHalfPt(institutionLine),
                    bold: true,
                    centered: true,
                    beforeTwips: institutionBeforeTwips,
                    afterTwips: 0,
                    leftIndentTwips: 0));
            }

            return true;
        }

        public static bool TryAppendCenteredPortraitCoverPage(
            Body body,
            MainDocumentPart main,
            string? coverPath,
            IReadOnlyList<CoverTextLine>? lines,
            uint pageWidthTwips,
            uint drawingId)
        {
            var resolvedCoverPath = (coverPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(resolvedCoverPath) || !System.IO.File.Exists(resolvedCoverPath))
            {
                return false;
            }

            var imageWidthEmu = TwipsToEmu(pageWidthTwips);
            var imageHeightEmu = ScaleHeight(imageWidthEmu, PortraitCoverSourceWidthPx, PortraitCoverSourceHeightPx);
            AppendAnchoredImage(
                body,
                main,
                resolvedCoverPath,
                imageWidthEmu,
                imageHeightEmu,
                drawingId,
                0L,
                0L,
                behindText: true,
                DW.HorizontalRelativePositionValues.Page,
                DW.VerticalRelativePositionValues.Page);

            foreach (var line in lines ?? Array.Empty<CoverTextLine>())
            {
                if (string.IsNullOrWhiteSpace(line?.Text)) continue;
                body.Append(BuildCoverParagraph(
                    line.Text,
                    line.FontSizeHalfPt,
                    line.Bold,
                    centered: true,
                    beforeTwips: line.BeforeTwips,
                    afterTwips: line.AfterTwips,
                    leftIndentTwips: 0,
                    colorHex: line.ColorHex));
            }

            return true;
        }

        public static long TwipsToEmu(uint twips) => twips * 635L;

        public static long ScaleHeight(long widthEmu, int sourceWidthPx, int sourceHeightPx)
        {
            if (widthEmu <= 0 || sourceWidthPx <= 0 || sourceHeightPx <= 0)
            {
                return widthEmu;
            }

            return (long)Math.Round(widthEmu * (double)sourceHeightPx / sourceWidthPx);
        }

        private static Paragraph BuildCoverParagraph(
            string? text,
            int fontSizeHalfPt,
            bool bold,
            bool centered,
            int beforeTwips,
            int afterTwips,
            int leftIndentTwips,
            string? colorHex = "FFFFFF")
        {
            var runProperties = new RunProperties(
                new FontSize { Val = Math.Max(18, fontSizeHalfPt).ToString() },
                new RunFonts { Ascii = DefaultCoverFont, HighAnsi = DefaultCoverFont },
                new Color { Val = string.IsNullOrWhiteSpace(colorHex) ? "FFFFFF" : colorHex.Trim() });
            if (bold)
            {
                runProperties.Bold = new Bold();
            }

            var paragraphProperties = new ParagraphProperties(
                new Justification { Val = centered ? JustificationValues.Center : JustificationValues.Left },
                new SpacingBetweenLines
                {
                    Before = Math.Max(0, beforeTwips).ToString(),
                    After = Math.Max(0, afterTwips).ToString(),
                    Line = "240",
                    LineRule = LineSpacingRuleValues.Auto
                });

            if (!centered && leftIndentTwips > 0)
            {
                paragraphProperties.Indentation = new Indentation { Left = leftIndentTwips.ToString() };
            }

            return new Paragraph(
                paragraphProperties,
                new Run(
                    runProperties,
                    new Text(SanitizeXmlText(text ?? string.Empty)) { Space = SpaceProcessingModeValues.Preserve }));
        }

        private static void AppendAnchoredImage(
            Body body,
            MainDocumentPart main,
            string imagePath,
            long cx,
            long cy,
            uint drawingId,
            long offsetXEmu,
            long offsetYEmu,
            bool behindText,
            DW.HorizontalRelativePositionValues horizontalRelativeFrom = DW.HorizontalRelativePositionValues.Margin,
            DW.VerticalRelativePositionValues verticalRelativeFrom = DW.VerticalRelativePositionValues.Margin)
        {
            using var stream = System.IO.File.OpenRead(imagePath);
            var imagePart = main.AddImagePart(ResolveImagePartType(imagePath));
            imagePart.FeedData(stream);
            var relId = main.GetIdOfPart(imagePart);
            body.Append(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" }),
                new Run(BuildAnchoredImage(
                    relId,
                    cx,
                    cy,
                    drawingId,
                    Path.GetFileName(imagePath),
                    offsetXEmu,
                    offsetYEmu,
                    behindText,
                    horizontalRelativeFrom,
                    verticalRelativeFrom))));
        }

        private static Drawing BuildAnchoredImage(
            string relId,
            long cx,
            long cy,
            uint drawingId,
            string imageName,
            long offsetXEmu,
            long offsetYEmu,
            bool behindText,
            DW.HorizontalRelativePositionValues horizontalRelativeFrom,
            DW.VerticalRelativePositionValues verticalRelativeFrom)
        {
            var anchor = new DW.Anchor(
                new DW.SimplePosition() { X = 0L, Y = 0L },
                new DW.HorizontalPosition(
                    new DW.PositionOffset(offsetXEmu.ToString()))
                {
                    RelativeFrom = horizontalRelativeFrom
                },
                new DW.VerticalPosition(
                    new DW.PositionOffset(offsetYEmu.ToString()))
                {
                    RelativeFrom = verticalRelativeFrom
                },
                new DW.Extent() { Cx = cx, Cy = cy },
                new DW.EffectExtent()
                {
                    LeftEdge = 0L,
                    TopEdge = 0L,
                    RightEdge = 0L,
                    BottomEdge = 0L
                },
                new DW.WrapNone(),
                new DW.DocProperties() { Id = drawingId, Name = imageName },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks() { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties() { Id = drawingId, Name = imageName },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip() { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset() { X = 0L, Y = 0L },
                                    new A.Extents() { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                SimplePos = false,
                RelativeHeight = behindText ? 0U : 251659264U,
                BehindDoc = behindText,
                Locked = false,
                LayoutInCell = true,
                AllowOverlap = true,
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            };

            return new Drawing(anchor);
        }

        private static int DetermineTopBandFontSizeHalfPt(string? text)
        {
            var length = (text ?? string.Empty).Trim().Length;
            if (length > 120) return 20;
            if (length > 90) return 22;
            if (length > 70) return 24;
            return 26;
        }

        private static int DetermineInstitutionFontSizeHalfPt(string? text)
        {
            var length = (text ?? string.Empty).Trim().Length;
            if (length > 80) return 22;
            if (length > 60) return 24;
            return 26;
        }

        private static string SanitizeXmlText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var buffer = new char[value.Length];
            var count = 0;
            foreach (var ch in value)
            {
                if (XmlConvert.IsXmlChar(ch))
                {
                    buffer[count++] = ch;
                }
            }

            return new string(buffer, 0, count);
        }

        private static ImagePartType ResolveImagePartType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" => ImagePartType.Jpeg,
                ".jpeg" => ImagePartType.Jpeg,
                ".gif" => ImagePartType.Gif,
                ".bmp" => ImagePartType.Bmp,
                ".tif" => ImagePartType.Tiff,
                ".tiff" => ImagePartType.Tiff,
                _ => ImagePartType.Png
            };
        }
    }
}
