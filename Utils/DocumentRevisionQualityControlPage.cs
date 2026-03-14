using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using ETD.Api.Models;

namespace ETD.Api.Utils
{
    public sealed class DocumentRevisionQualityControlPageOptions
    {
        public string DocumentTitle { get; set; } = "Document";
        public string DocumentType { get; set; } = "Learning Material";
        public string Phase { get; set; } = string.Empty;
        public string Version { get; set; } = $"{DateTime.Now:yyyy}/V1";
        public string RegulatoryReference { get; set; } = string.Empty;
        public string Seta { get; set; } = string.Empty;
        public string DocumentDeveloper { get; set; } = string.Empty;
        public string Moderator { get; set; } = string.Empty;
    }

    public static class DocumentRevisionQualityControlPage
    {
        public static void Append(
            Body body,
            Qualification? qualification,
            DocumentRevisionQualityControlPageOptions? options = null)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));

            options ??= new DocumentRevisionQualityControlPageOptions();

            var qualificationNumber = (qualification?.QualificationNumber ?? string.Empty).Trim();
            var qualificationDescription = (qualification?.QualificationDescription ?? string.Empty).Trim();
            var learningProvider = (qualification?.LearningInstitutionName ?? string.Empty).Trim();
            var nqfLevel = (qualification?.NqfLevel ?? string.Empty).Trim();
            var credits = (qualification?.Credits ?? string.Empty).Trim();
            var documentDeveloper = string.IsNullOrWhiteSpace(options.DocumentDeveloper)
                ? (qualification?.SeniorLecturer ?? string.Empty).Trim()
                : options.DocumentDeveloper.Trim();

            body.Append(BuildParagraph(
                "DOCUMENT REVISION AND QUALITY CONTROL",
                "28",
                bold: true,
                justification: JustificationValues.Center,
                before: "120",
                after: "140"));

            body.Append(BuildParagraph(
                "This document is a draft proposal. It is only valid for use once approved and signed off by the applicable Quality Council or Sector Education and Training Authority.",
                "22",
                before: "0",
                after: "180"));

            body.Append(BuildMetadataTable(
                options.DocumentTitle,
                options.Version,
                qualificationNumber,
                qualificationDescription,
                options.RegulatoryReference,
                learningProvider,
                nqfLevel,
                credits,
                options.DocumentType,
                options.Phase,
                documentDeveloper,
                options.Moderator,
                options.Seta));

            body.Append(BuildSpacer());
            body.Append(BuildApprovalTable("PROVIDER APPROVAL"));
            body.Append(BuildSpacer());
            body.Append(BuildApprovalTable("QUALITY COUNCIL / SETA APPROVAL"));
            body.Append(BuildSpacer());

            body.Append(BuildParagraph(
                "DR PC. WEPENER PROVIDES THE SOFTWARE \"AS IS\" AND WITHOUT WARRANTY OF ANY KIND. ALL EXPRESS OR IMPLIED WARRANTIES, INCLUDING MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, PERFORMANCE, ACCURACY, RELIABILITY, AND NON-INFRINGEMENT, ARE DISCLAIMED.",
                "18",
                bold: true,
                before: "120",
                after: "80"));

            body.Append(BuildParagraph(
                "AI-assisted drafting notice: This document may have been drafted or revised in part using software tools that incorporate OpenAI technology. AI-assisted content must be reviewed, corrected, moderated, and approved by the Learning Provider, Moderator, and the applicable Quality Council or SETA before use.",
                "18",
                before: "0",
                after: "80"));

            body.Append(BuildParagraph(
                "Use of OpenAI technology does not constitute approval, moderation, endorsement, certification, or warranty of this document.",
                "18",
                before: "0",
                after: "80"));

            body.Append(BuildParagraph(
                "OpenAI policy references:",
                "18",
                bold: true,
                before: "0",
                after: "20"));
            body.Append(BuildParagraph(
                "https://openai.com/policies/terms-of-use/",
                "18",
                before: "0",
                after: "20"));
            body.Append(BuildParagraph(
                "https://openai.com/policies/sharing-publication-policy/",
                "18",
                before: "0",
                after: "0"));
        }

        private static Table BuildMetadataTable(
            string documentTitle,
            string version,
            string qualificationNumber,
            string qualificationDescription,
            string regulatoryReference,
            string learningProvider,
            string nqfLevel,
            string credits,
            string documentType,
            string phase,
            string documentDeveloper,
            string moderator,
            string seta)
        {
            var table = new Table(
                new TableProperties(
                    new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                    new TableLayout { Type = TableLayoutValues.Fixed },
                    BuildTableBorders(),
                    new TableCellMarginDefault(
                        new TopMargin { Width = "70", Type = TableWidthUnitValues.Dxa },
                        new BottomMargin { Width = "70", Type = TableWidthUnitValues.Dxa },
                        new TableCellLeftMargin { Width = 90, Type = TableWidthValues.Dxa },
                        new TableCellRightMargin { Width = 90, Type = TableWidthValues.Dxa })),
                new TableGrid(
                    new GridColumn { Width = "2500" },
                    new GridColumn { Width = "2400" },
                    new GridColumn { Width = "2500" },
                    new GridColumn { Width = "2400" }));

            table.Append(MetadataRow("Document Title", documentTitle, "Version", version));
            table.Append(MetadataRow("SAQA Qualification Number", qualificationNumber, "Qualification", qualificationDescription));
            table.Append(MetadataRow("QCTO / SETA Reference", regulatoryReference, "Learning Provider", learningProvider));
            table.Append(MetadataRow("NQF Level", nqfLevel, "Credits", credits));
            table.Append(MetadataRow("Document Type", documentType, "Phase", phase));
            table.Append(MetadataRow("Document Developer", documentDeveloper, "Moderator", moderator));
            table.Append(MetadataRow("SETA", seta, string.Empty, string.Empty));
            return table;
        }

        private static TableRow MetadataRow(string label1, string value1, string label2, string value2)
        {
            return new TableRow(
                MetadataCell(label1, shaded: true, bold: true),
                MetadataCell(value1),
                MetadataCell(label2, shaded: !string.IsNullOrWhiteSpace(label2), bold: !string.IsNullOrWhiteSpace(label2)),
                MetadataCell(value2));
        }

        private static TableCell MetadataCell(string text, bool shaded = false, bool bold = false)
        {
            return new TableCell(
                new TableCellProperties(
                    new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "2400" },
                    new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = shaded ? "F2F2F2" : "FFFFFF" },
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                BuildParagraph(text, "20", bold: bold));
        }

        private static Table BuildApprovalTable(string heading)
        {
            var table = new Table(
                new TableProperties(
                    new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                    new TableLayout { Type = TableLayoutValues.Fixed },
                    BuildTableBorders(),
                    new TableCellMarginDefault(
                        new TopMargin { Width = "70", Type = TableWidthUnitValues.Dxa },
                        new BottomMargin { Width = "70", Type = TableWidthUnitValues.Dxa },
                        new TableCellLeftMargin { Width = 90, Type = TableWidthValues.Dxa },
                        new TableCellRightMargin { Width = 90, Type = TableWidthValues.Dxa })),
                new TableGrid(
                    new GridColumn { Width = "2400" },
                    new GridColumn { Width = "2600" },
                    new GridColumn { Width = "2200" },
                    new GridColumn { Width = "2600" }));

            table.Append(new TableRow(
                new TableCell(
                    new TableCellProperties(
                        new GridSpan { Val = 4 },
                        new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F2F2F2" },
                        new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                    BuildParagraph(heading, "20", bold: true, justification: JustificationValues.Center))));

            table.Append(ApprovalRow("Document Approved by:", string.Empty, "Name and Surname:", string.Empty));
            table.Append(ApprovalRow("Appointment:", string.Empty, "Date Approved:", "dd/mm/yyyy"));
            table.Append(ApprovalRow("Signature:", string.Empty, string.Empty, string.Empty));
            table.Append(new TableRow(
                ApprovalCell("Remarks:", shaded: true, bold: true),
                new TableCell(
                    new TableCellProperties(
                        new GridSpan { Val = 3 },
                        new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }),
                    new Paragraph(
                        new ParagraphProperties(new SpacingBetweenLines { Before = "40", After = "40", Line = "480", LineRule = LineSpacingRuleValues.Auto }),
                        new Run(new Text(string.Empty))))));

            return table;
        }

        private static TableRow ApprovalRow(string label1, string value1, string label2, string value2)
        {
            return new TableRow(
                ApprovalCell(label1, shaded: true, bold: true),
                ApprovalCell(value1),
                ApprovalCell(label2, shaded: !string.IsNullOrWhiteSpace(label2), bold: !string.IsNullOrWhiteSpace(label2)),
                ApprovalCell(value2));
        }

        private static TableCell ApprovalCell(string text, bool shaded = false, bool bold = false)
        {
            return new TableCell(
                new TableCellProperties(
                    new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "2400" },
                    new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = shaded ? "F2F2F2" : "FFFFFF" },
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                BuildParagraph(text, "20", bold: bold));
        }

        private static TableBorders BuildTableBorders()
        {
            return new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 8 },
                new LeftBorder { Val = BorderValues.Single, Size = 8 },
                new BottomBorder { Val = BorderValues.Single, Size = 8 },
                new RightBorder { Val = BorderValues.Single, Size = 8 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 8 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 8 });
        }

        private static Paragraph BuildParagraph(
            string text,
            string fontSizeHalfPoints,
            bool bold = false,
            JustificationValues justification = JustificationValues.Left,
            string before = "0",
            string after = "40")
        {
            return new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = justification },
                    new SpacingBetweenLines { Before = before, After = after }),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = "Arial", HighAnsi = "Arial" },
                        new FontSize { Val = fontSizeHalfPoints },
                        new FontSizeComplexScript { Val = fontSizeHalfPoints },
                        bold ? new Bold() : new Bold { Val = false }),
                    new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }));
        }

        private static Paragraph BuildSpacer()
        {
            return new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Before = "60", After = "60" }),
                new Run(new Text(string.Empty)));
        }
    }
}
