using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ETD.Api.Data;
using ETD.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace ETD.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LearnerRegistrationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private const string SourceTemplatePath = "C:\\ETDP\\ETDP\\Imports\\LearnerRegsitration\\skills-programmes-learner-enrolment-form (1).xlsx";

        private static readonly string[] CanonicalHeaders = new[]
        {
            "SDP Accreditation Number",
            "National ID",
            "LearnerAlternateID",
            "AlternateIDType",
            "LearnerLastName",
            "LearnerFirstName",
            "LearnerMiddleName",
            "LearnerTitle",
            "LearnerBirthDate",
            "EquityCode",
            "NationalityCode",
            "HomeLanguageCode",
            "GenderCode",
            "CitizenStatusCode",
            "SocioeconomicCode",
            "DisabilityCode",
            "DisabilityRating",
            "ImmigrantStatus",
            "HomeAddress1",
            "HomeAddress2",
            "HomeAddress3",
            "PostalAddress1",
            "PostalAddress2",
            "PostalAddress3",
            "LearnerHomeAddressPostalCode",
            "LearnerHomeAddressPhysicalCode",
            "LearnerPhoneNumber",
            "LearnerCellPhoneNumber",
            "LearnerFaxNumber",
            "LearnerEmailAddress",
            "ProvinceCode",
            "STATSSAAreaCode",
            "POPIActAgree",
            "POPIActDate",
            "SkillsProgramme ID",
            "EmploymentStatus",
            "LearnerEnrolledDate",
            "DATE OF FISA",
            "FINAL FISA RESULT",
            "Date Submitted to QCTO",
            "Learning Institution Name",
            "Learning Institution Province",
            "Learning Institution City/ Town",
            "Learning Institution Street Name",
            "Learning Institution Street Number",
            "Learning Institution City/ Town Physical Code",
            "Learning Institution Contact Person",
            "Learning Institution Contact Person Phone Number",
            "Learning Institution Contract Person Email Addres",
            "Work Experience Employer Name",
            "Work Experience Employer Street Number",
            "Work Experience Employer Street Name",
            "Work Experience Employer City/ Town",
            "Work Experience Employer Province",
            "Work Experience Employer City/ Town Code",
            "Work Experience Employer Supervisor Name",
            "Work Experience Employer Supervisor Phone Number",
            "Work Experience Employer Supervisor Email Address"
        };

        private static readonly string[] TemplateHeadersWithDescriptors = new[]
        {
            "SDP Accreditation Number (provider accreditation ref)",
            "National ID (13 digits, mandatory)",
            "LearnerAlternateID (passport/other if no SA ID)",
            "AlternateIDType (Passport/Refugee/Other)",
            "LearnerLastName (mandatory)",
            "LearnerFirstName (mandatory)",
            "LearnerMiddleName (optional)",
            "LearnerTitle (Mr/Ms/Dr/etc.)",
            "LearnerBirthDate (YYYY-MM-DD)",
            "EquityCode (per data spec)",
            "NationalityCode (per data spec)",
            "HomeLanguageCode (per data spec)",
            "GenderCode (per data spec)",
            "CitizenStatusCode (per data spec)",
            "SocioeconomicCode (per data spec)",
            "DisabilityCode (per data spec)",
            "DisabilityRating (if applicable)",
            "ImmigrantStatus (per data spec)",
            "HomeAddress1",
            "HomeAddress2",
            "HomeAddress3",
            "PostalAddress1",
            "PostalAddress2",
            "PostalAddress3",
            "LearnerHomeAddressPostalCode",
            "LearnerHomeAddressPhysicalCode",
            "LearnerPhoneNumber",
            "LearnerCellPhoneNumber",
            "LearnerFaxNumber",
            "LearnerEmailAddress",
            "ProvinceCode (per data spec)",
            "STATSSAAreaCode (per data spec)",
            "POPIActAgree (Yes/No)",
            "POPIActDate (YYYY-MM-DD)",
            "SkillsProgramme ID (mandatory)",
            "EmploymentStatus (per data spec)",
            "LearnerEnrolledDate (YYYY-MM-DD)",
            "DATE OF FISA (YYYY-MM-DD)",
            "FINAL FISA RESULT (Competent/NYC/etc.)",
            "Date Submitted to QCTO (YYYY-MM-DD)",
            "Learning Institution Name",
            "Learning Institution Province",
            "Learning Institution City/ Town",
            "Learning Institution Street Name",
            "Learning Institution Street Number",
            "Learning Institution City/ Town Physical Code",
            "Learning Institution Contact Person",
            "Learning Institution Contact Person Phone Number",
            "Learning Institution Contract Person Email Addres",
            "Work Experience Employer Name",
            "Work Experience Employer Street Number",
            "Work Experience Employer Street Name",
            "Work Experience Employer City/ Town",
            "Work Experience Employer Province",
            "Work Experience Employer City/ Town Code",
            "Work Experience Employer Supervisor Name",
            "Work Experience Employer Supervisor Phone Number",
            "Work Experience Employer Supervisor Email Address"
        };

        public LearnerRegistrationController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAll([FromQuery] int? qualificationId = null, [FromQuery] int take = 500)
        {
            if (take < 1) take = 1;
            if (take > 2000) take = 2000;

            var query = _context.LearnerRegistrations.AsNoTracking().AsQueryable();
            if (qualificationId.HasValue && qualificationId.Value > 0)
            {
                query = query.Where(x => x.QualificationId == qualificationId.Value);
            }

            var rows = query
                .OrderByDescending(x => x.Id)
                .Take(take)
                .Select(x => new
                {
                    x.Id,
                    x.QualificationId,
                    x.NationalId,
                    x.LearnerLastName,
                    x.LearnerFirstName,
                    x.SkillsProgrammeId,
                    x.LearnerAlternateId,
                    x.EmploymentStatus,
                    x.LearnerEnrolledDate,
                    x.FinalFisaResult,
                    x.DateSubmittedToQcto,
                    x.LearnerCellPhoneNumber,
                    x.LearnerPhoneNumber,
                    x.LearningInstitutionName,
                    x.LearningInstitutionProvince,
                    x.LearningInstitutionCityTown,
                    x.LearningInstitutionStreetName,
                    x.LearningInstitutionStreetNumber,
                    x.LearningInstitutionCityTownPhysicalCode,
                    x.LearningInstitutionContactPerson,
                    x.LearningInstitutionContactPersonPhoneNumber,
                    x.LearningInstitutionContactPersonEmailAddress,
                    x.WorkExperienceEmployerName,
                    x.WorkExperienceEmployerStreetNumber,
                    x.WorkExperienceEmployerStreetName,
                    x.WorkExperienceEmployerCityTown,
                    x.WorkExperienceEmployerProvince,
                    x.WorkExperienceEmployerCityTownCode,
                    x.WorkExperienceEmployerSupervisorName,
                    x.WorkExperienceEmployerSupervisorPhoneNumber,
                    x.WorkExperienceEmployerSupervisorEmailAddress,
                    x.CreatedAtUtc
                })
                .ToList();

            return Ok(rows);
        }

        [HttpPost("import-excel")]
        [RequestSizeLimit(50_000_000)]
        public IActionResult ImportExcel([FromForm] IFormFile? file, [FromForm] int? qualificationId)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Excel file is required.");
            if (qualificationId.HasValue && qualificationId.Value > 0)
            {
                var qExists = _context.Qualifications.Any(q => q.Id == qualificationId.Value);
                if (!qExists) return BadRequest($"QualificationId {qualificationId.Value} was not found.");
            }

            var created = 0;
            var failed = 0;
            var skippedEmpty = 0;
            var totalRows = 0;
            var details = new List<object>();

            using var stream = file.OpenReadStream();
            using var doc = SpreadsheetDocument.Open(stream, false);
            var workbookPart = doc.WorkbookPart;
            if (workbookPart == null) return BadRequest("Invalid workbook.");

            var dataSheet = ResolveDataSheet(workbookPart);
            if (dataSheet == null) return BadRequest("Could not find a learner data sheet in the workbook.");

            var wsPart = (WorksheetPart)workbookPart.GetPartById(dataSheet.Id!);
            var rows = wsPart.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToList() ?? new List<Row>();
            if (rows.Count == 0) return BadRequest("Workbook data sheet is empty.");

            var headerMap = BuildHeaderMap(rows[0], workbookPart);
            if (!headerMap.Values.Contains("National ID") || !headerMap.Values.Contains("LearnerFirstName"))
                return BadRequest("Workbook header row is not aligned with learner registration specification.");

            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var byHeader = BuildRowDictionary(row, workbookPart, headerMap);
                totalRows++;

                var nationalId = Get(byHeader, "National ID");
                var firstName = Get(byHeader, "LearnerFirstName");
                var lastName = Get(byHeader, "LearnerLastName");
                var skillsProgrammeId = Get(byHeader, "SkillsProgramme ID");
                var popiActAgree = Get(byHeader, "POPIActAgree");
                var learnerBirthDate = Get(byHeader, "LearnerBirthDate");
                var popiActDate = Get(byHeader, "POPIActDate");
                var learnerEnrolledDate = Get(byHeader, "LearnerEnrolledDate");
                var dateOfFisa = Get(byHeader, "DATE OF FISA");
                var dateSubmittedToQcto = Get(byHeader, "Date Submitted to QCTO");

                if (byHeader.Values.All(string.IsNullOrWhiteSpace))
                {
                    skippedEmpty++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(nationalId) && (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)))
                {
                    failed++;
                    details.Add(new { row = i + 1, reason = "Missing required learner identity fields (National ID or First+Last Name)." });
                    continue;
                }
                if (string.IsNullOrWhiteSpace(skillsProgrammeId))
                {
                    failed++;
                    details.Add(new { row = i + 1, reason = "SkillsProgramme ID is required." });
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(nationalId))
                {
                    var idDigitsOnly = nationalId.All(char.IsDigit);
                    if (!idDigitsOnly || nationalId.Length != 13)
                    {
                        failed++;
                        details.Add(new { row = i + 1, reason = "National ID must be 13 digits.", nationalId });
                        continue;
                    }
                }
                if (!string.IsNullOrWhiteSpace(popiActAgree))
                {
                    var normalizedPopi = popiActAgree.Trim().ToLowerInvariant();
                    if (normalizedPopi != "yes" && normalizedPopi != "no")
                    {
                        failed++;
                        details.Add(new { row = i + 1, reason = "POPIActAgree must be Yes or No.", popiActAgree });
                        continue;
                    }
                }
                var isBirthDateValid = ValidateDateLike(learnerBirthDate, out var birthDateError);
                var isPopiDateValid = ValidateDateLike(popiActDate, out var popiDateError);
                var isEnrolledDateValid = ValidateDateLike(learnerEnrolledDate, out var enrolledDateError);
                var isFisaDateValid = ValidateDateLike(dateOfFisa, out var fisaDateError);
                var isSubmittedDateValid = ValidateDateLike(dateSubmittedToQcto, out var submittedDateError);

                if (!isBirthDateValid || !isPopiDateValid || !isEnrolledDateValid || !isFisaDateValid || !isSubmittedDateValid)
                {
                    failed++;
                    details.Add(new
                    {
                        row = i + 1,
                        reason = "One or more date fields are invalid. Use YYYY-MM-DD.",
                        learnerBirthDateError = birthDateError,
                        popiActDateError = popiDateError,
                        learnerEnrolledDateError = enrolledDateError,
                        dateOfFisaError = fisaDateError,
                        dateSubmittedToQctoError = submittedDateError
                    });
                    continue;
                }

                var duplicate = _context.LearnerRegistrations.Any(x =>
                    x.QualificationId == qualificationId &&
                    x.NationalId == nationalId &&
                    x.SkillsProgrammeId == skillsProgrammeId);
                if (duplicate)
                {
                    failed++;
                    details.Add(new { row = i + 1, reason = "Duplicate learner row for qualification+NationalID+SkillsProgrammeID.", nationalId, skillsProgrammeId });
                    continue;
                }

                var entity = new LearnerRegistration
                {
                    QualificationId = qualificationId,
                    SdpAccreditationNumber = Get(byHeader, "SDP Accreditation Number"),
                    NationalId = nationalId,
                    LearnerAlternateId = Get(byHeader, "LearnerAlternateID"),
                    AlternateIdType = Get(byHeader, "AlternateIDType"),
                    LearnerLastName = lastName,
                    LearnerFirstName = firstName,
                    LearnerMiddleName = Get(byHeader, "LearnerMiddleName"),
                    LearnerTitle = Get(byHeader, "LearnerTitle"),
                    LearnerBirthDate = Get(byHeader, "LearnerBirthDate"),
                    EquityCode = Get(byHeader, "EquityCode"),
                    NationalityCode = Get(byHeader, "NationalityCode"),
                    HomeLanguageCode = Get(byHeader, "HomeLanguageCode"),
                    GenderCode = Get(byHeader, "GenderCode"),
                    CitizenStatusCode = Get(byHeader, "CitizenStatusCode"),
                    SocioeconomicCode = Get(byHeader, "SocioeconomicCode"),
                    DisabilityCode = Get(byHeader, "DisabilityCode"),
                    DisabilityRating = Get(byHeader, "DisabilityRating"),
                    ImmigrantStatus = Get(byHeader, "ImmigrantStatus"),
                    HomeAddress1 = Get(byHeader, "HomeAddress1"),
                    HomeAddress2 = Get(byHeader, "HomeAddress2"),
                    HomeAddress3 = Get(byHeader, "HomeAddress3"),
                    PostalAddress1 = Get(byHeader, "PostalAddress1"),
                    PostalAddress2 = Get(byHeader, "PostalAddress2"),
                    PostalAddress3 = Get(byHeader, "PostalAddress3"),
                    LearnerHomeAddressPostalCode = Get(byHeader, "LearnerHomeAddressPostalCode"),
                    LearnerHomeAddressPhysicalCode = Get(byHeader, "LearnerHomeAddressPhysicalCode"),
                    LearnerPhoneNumber = Get(byHeader, "LearnerPhoneNumber"),
                    LearnerCellPhoneNumber = Get(byHeader, "LearnerCellPhoneNumber"),
                    LearnerFaxNumber = Get(byHeader, "LearnerFaxNumber"),
                    LearnerEmailAddress = Get(byHeader, "LearnerEmailAddress"),
                    ProvinceCode = Get(byHeader, "ProvinceCode"),
                    StatssaAreaCode = Get(byHeader, "STATSSAAreaCode"),
                    PopiActAgree = Get(byHeader, "POPIActAgree"),
                    PopiActDate = Get(byHeader, "POPIActDate"),
                    SkillsProgrammeId = skillsProgrammeId,
                    EmploymentStatus = Get(byHeader, "EmploymentStatus"),
                    LearnerEnrolledDate = Get(byHeader, "LearnerEnrolledDate"),
                    DateOfFisa = Get(byHeader, "DATE OF FISA"),
                    FinalFisaResult = Get(byHeader, "FINAL FISA RESULT"),
                    DateSubmittedToQcto = Get(byHeader, "Date Submitted to QCTO"),
                    LearningInstitutionName = Get(byHeader, "Learning Institution Name"),
                    LearningInstitutionProvince = Get(byHeader, "Learning Institution Province"),
                    LearningInstitutionCityTown = Get(byHeader, "Learning Institution City/ Town"),
                    LearningInstitutionStreetName = Get(byHeader, "Learning Institution Street Name"),
                    LearningInstitutionStreetNumber = Get(byHeader, "Learning Institution Street Number"),
                    LearningInstitutionCityTownPhysicalCode = Get(byHeader, "Learning Institution City/ Town Physical Code"),
                    LearningInstitutionContactPerson = Get(byHeader, "Learning Institution Contact Person"),
                    LearningInstitutionContactPersonPhoneNumber = Get(byHeader, "Learning Institution Contact Person Phone Number"),
                    LearningInstitutionContactPersonEmailAddress = GetAny(
                        byHeader,
                        "Learning Institution Contract Person Email Addres",
                        "Learning Institution Contact Person Email Address"),
                    WorkExperienceEmployerName = Get(byHeader, "Work Experience Employer Name"),
                    WorkExperienceEmployerStreetNumber = Get(byHeader, "Work Experience Employer Street Number"),
                    WorkExperienceEmployerStreetName = Get(byHeader, "Work Experience Employer Street Name"),
                    WorkExperienceEmployerCityTown = Get(byHeader, "Work Experience Employer City/ Town"),
                    WorkExperienceEmployerProvince = Get(byHeader, "Work Experience Employer Province"),
                    WorkExperienceEmployerCityTownCode = Get(byHeader, "Work Experience Employer City/ Town Code"),
                    WorkExperienceEmployerSupervisorName = Get(byHeader, "Work Experience Employer Supervisor Name"),
                    WorkExperienceEmployerSupervisorPhoneNumber = Get(byHeader, "Work Experience Employer Supervisor Phone Number"),
                    WorkExperienceEmployerSupervisorEmailAddress = Get(byHeader, "Work Experience Employer Supervisor Email Address")
                };

                _context.LearnerRegistrations.Add(entity);
                created++;
            }

            _context.SaveChanges();
            return Ok(new { totalRows, skippedEmpty, created, failed, details });
        }

        [HttpGet("template/download")]
        public IActionResult DownloadTemplate()
        {
            var bytes = BuildFriendlyTemplate();
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "LearnerRegistrationTemplate_Enhanced.xlsx");
        }

        [HttpGet("template/original")]
        public IActionResult DownloadOriginalTemplate()
        {
            if (!System.IO.File.Exists(SourceTemplatePath))
                return NotFound("Original learner registration template file not found.");
            var bytes = System.IO.File.ReadAllBytes(SourceTemplatePath);
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "skills-programmes-learner-enrolment-form.xlsx");
        }

        private static string Get(Dictionary<string, string> dict, string key) =>
            dict.TryGetValue(key, out var value) ? value : string.Empty;

        private static string GetAny(Dictionary<string, string> dict, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (dict.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return string.Empty;
        }

        private static Sheet? ResolveDataSheet(WorkbookPart workbookPart)
        {
            var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToList() ?? new List<Sheet>();
            if (sheets.Count == 0) return null;

            var byName = sheets.FirstOrDefault(s =>
                (s.Name?.Value ?? "").Contains("Learner", StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;

            foreach (var s in sheets)
            {
                var part = (WorksheetPart)workbookPart.GetPartById(s.Id!);
                var first = part.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>().FirstOrDefault();
                if (first == null) continue;
                var map = BuildHeaderMap(first, workbookPart);
                if (map.Values.Contains("National ID")) return s;
            }

            return sheets.First();
        }

        private static Dictionary<int, string> BuildHeaderMap(Row headerRow, WorkbookPart workbookPart)
        {
            var normalizedCanonical = CanonicalHeaders.ToDictionary(h => NormalizeHeader(h), h => h);
            var map = new Dictionary<int, string>();
            var sequentialIndex = 0;
            foreach (var cell in headerRow.Elements<Cell>())
            {
                sequentialIndex++;
                var colIdx = string.IsNullOrWhiteSpace(cell.CellReference?.Value)
                    ? sequentialIndex
                    : GetColumnIndex(cell.CellReference?.Value);
                var text = GetCellValue(cell, workbookPart);
                var normalized = NormalizeHeader(text);
                if (normalizedCanonical.TryGetValue(normalized, out var canonical))
                {
                    map[colIdx] = canonical;
                }
            }
            return map;
        }

        private static Dictionary<string, string> BuildRowDictionary(Row row, WorkbookPart workbookPart, Dictionary<int, string> headerMap)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sequentialIndex = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                sequentialIndex++;
                var colIdx = string.IsNullOrWhiteSpace(cell.CellReference?.Value)
                    ? sequentialIndex
                    : GetColumnIndex(cell.CellReference?.Value);
                if (!headerMap.TryGetValue(colIdx, out var header)) continue;
                result[header] = GetCellValue(cell, workbookPart).Trim();
            }
            foreach (var h in CanonicalHeaders)
            {
                if (!result.ContainsKey(h)) result[h] = string.Empty;
            }
            return result;
        }

        private static int GetColumnIndex(string? cellRef)
        {
            if (string.IsNullOrWhiteSpace(cellRef)) return 0;
            var col = new string(cellRef.Where(char.IsLetter).ToArray()).ToUpperInvariant();
            var sum = 0;
            foreach (var ch in col)
            {
                sum *= 26;
                sum += (ch - 'A' + 1);
            }
            return sum;
        }

        private static string GetCellValue(Cell cell, WorkbookPart workbookPart)
        {
            if (cell.InlineString?.Text?.Text != null)
                return cell.InlineString.Text.Text;

            var value = cell.CellValue?.Text ?? string.Empty;
            if (cell.DataType == null) return value;

            return cell.DataType.Value switch
            {
                CellValues.SharedString => GetSharedString(value, workbookPart),
                CellValues.Boolean => value == "1" ? "TRUE" : "FALSE",
                _ => value
            };
        }

        private static string GetSharedString(string indexText, WorkbookPart workbookPart)
        {
            if (!int.TryParse(indexText, out var index)) return string.Empty;
            var sst = workbookPart.SharedStringTablePart?.SharedStringTable;
            if (sst == null) return string.Empty;
            var item = sst.Elements<SharedStringItem>().ElementAtOrDefault(index);
            return item?.InnerText ?? string.Empty;
        }

        private static string NormalizeHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return string.Empty;
            var left = header.Split('(')[0].Trim();
            var chars = left.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToLowerInvariant();
        }

        private static byte[] BuildFriendlyTemplate()
        {
            using var ms = new MemoryStream();
            using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook, true))
            {
                var workbookPart = doc.AddWorkbookPart();
                workbookPart.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                var instructionsPart = workbookPart.AddNewPart<WorksheetPart>();
                instructionsPart.Worksheet = new Worksheet(new SheetData());
                var insData = instructionsPart.Worksheet.GetFirstChild<SheetData>()!;

                AppendTextRow(insData, "Learner Registration Upload Template - Instructions");
                AppendTextRow(insData, "1) Complete learner records in the second sheet: 'Learner Data Upload'.");
                AppendTextRow(insData, "2) Keep the header row unchanged. Do not rename or reorder columns.");
                AppendTextRow(insData, "3) Mandatory practical fields: National ID, LearnerLastName, LearnerFirstName, SkillsProgramme ID.");
                AppendTextRow(insData, "4) Date format recommendation: YYYY-MM-DD.");
                AppendTextRow(insData, "5) Code fields (e.g., EquityCode, ProvinceCode, GenderCode) must follow the official data-load specification PDF.");
                AppendTextRow(insData, "6) POPIActAgree should be Yes/No and POPIActDate should be captured when consent is confirmed.");
                AppendTextRow(insData, "7) Save as .xlsx and upload from the Learner Registration page.");
                AppendTextRow(insData, "8) Learning Institution and Work Experience Employer columns are required for extended reporting forms.");
                AppendTextRow(insData, "9) Duplicate rows by Qualification + National ID + SkillsProgramme ID will be skipped.");

                var dataPart = workbookPart.AddNewPart<WorksheetPart>();
                dataPart.Worksheet = new Worksheet(new SheetData());
                var dataSheet = dataPart.Worksheet.GetFirstChild<SheetData>()!;
                AppendTextRow(dataSheet, TemplateHeadersWithDescriptors);

                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(instructionsPart),
                    SheetId = 1U,
                    Name = "1 - Instructions"
                });
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(dataPart),
                    SheetId = 2U,
                    Name = "2 - Learner Data Upload"
                });

                workbookPart.Workbook.Save();
            }
            ms.Position = 0;
            return ms.ToArray();
        }

        private static void AppendTextRow(SheetData sheetData, params string[] values)
        {
            var row = new Row();
            foreach (var v in values)
            {
                row.Append(new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(v ?? string.Empty))
                });
            }
            sheetData.Append(row);
        }

        private static bool ValidateDateLike(string value, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(value)) return true;

            if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return true;

            error = $"Invalid date '{value}'";
            return false;
        }
    }
}
