using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ETD.Api.Data;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ETD.Api.Services
{
    public sealed class SansMetadataService
    {
        private static readonly Regex SansEntryRegex = new(
            @"(?<number>SANS\s\d{1,5}(?:-\d+)*(?::\d{4})?)\s*(?<edition>Ed(?:ition)?\s*\d+(?:\.\d+)?)?\s*(?<title>.*?)(?=(?:\bSANS\s\d{1,5})|(?:\bSCHEDULE\b)|(?:\bNOTICE\b)|(?:\bDEPARTMENT OF\b)|\z)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex PdfHrefRegex = new(
            @"href\s*=\s*[""'](?<url>[^""'#>]+\.pdf(?:\?[^""'#>]*)?)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HtmlTableRowRegex = new(
            @"<tr\b[^>]*>(?<row>.*?)</tr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex HtmlTableCellRegex = new(
            @"<t[dh]\b[^>]*>(?<cell>.*?)</t[dh]>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex HtmlAttributeRegex = new(
            @"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*[""'](?<value>[^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "about","accordance","act","against","all","amended","and","applicable","assessment","code","codes",
            "compliance","current","department","edition","ed","engineering","for","gazette","general","government",
            "guideline","guidelines","industry","latest","list","matters","mechanical","new","notice","part","parts",
            "premises","published","purport","reference","references","regulation","regulations","requirements","sabs",
            "sans","schedule","scope","series","specification","standard","standards","technical","terms","the","their",
            "this","title","withdrawal","withdrawn","with","wiring","your"
        };

        private readonly ILogger<SansMetadataService> _logger;

        public SansMetadataService(ILogger<SansMetadataService> logger)
        {
            _logger = logger;
        }

        public async Task<SansMetadataScanResult> ScanSourcesAsync(
            ApplicationDbContext context,
            IEnumerable<string>? localFilePaths,
            IEnumerable<string>? sourceUrls,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(workingDirectory);
            var result = new SansMetadataScanResult();
            var documents = new List<ParsedSourceDocument>();
            var warnings = new List<string>();

            foreach (var path in localFilePaths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                try
                {
                    var doc = await ReadLocalDocumentAsync(path, cancellationToken);
                    if (doc != null) documents.Add(doc);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Local standards source failed: {path} | {ex.Message}");
                }
            }

            foreach (var rawUrl in sourceUrls ?? Enumerable.Empty<string>())
            {
                var url = (rawUrl ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(url)) continue;
                try
                {
                    documents.AddRange(await ReadRemoteDocumentsAsync(url, workingDirectory, cancellationToken));
                }
                catch (Exception ex)
                {
                    warnings.Add($"Remote standards source failed: {url} | {ex.Message}");
                }
            }

            result.SourceCount = documents.Count;
            result.ProcessedDocuments = documents.Count;

            var rows = documents.SelectMany(ParseEntries).ToList();
            result.ExtractedEntries = rows.Count;

            await using var connection = new SqliteConnection(context.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync(cancellationToken);
            foreach (var row in rows)
            {
                try
                {
                    var action = await UpsertMetadataAsync(connection, row, cancellationToken);
                    if (action == SansUpsertAction.Inserted) result.Inserted++;
                    if (action == SansUpsertAction.Updated) result.Updated++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to save SANS {row.StandardNumber}: {ex.Message}");
                }
            }

            result.Warnings = warnings;
            result.Metadata = rows.OrderBy(x => x.StandardNumber, StringComparer.OrdinalIgnoreCase).Select(ToScanItem).ToList();
            result.CurrentCount = result.Metadata.Count(x => x.IsCurrent);
            result.WithdrawnCount = result.Metadata.Count(x => !x.IsCurrent);
            return result;
        }

        public async Task<SansMetadataIndexResult> GetMetadataIndexAsync(
            ApplicationDbContext context,
            bool currentOnly,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqliteConnection(context.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var rows = await LoadMetadataAsync(connection, currentOnly, cancellationToken);
            return new SansMetadataIndexResult
            {
                TotalCount = rows.Count,
                CurrentCount = rows.Count(x => x.IsCurrent),
                WithdrawnCount = rows.Count(x => !x.IsCurrent),
                Metadata = rows.Select(ToScanItem).ToList()
            };
        }

        public async Task<List<SansCodeNameItem>> GetCodeNameIndexAsync(
            ApplicationDbContext context,
            bool currentOnly,
            CancellationToken cancellationToken = default)
        {
            var index = await GetMetadataIndexAsync(context, currentOnly, cancellationToken);
            return index.Metadata
                .Select(item => new SansCodeNameItem
                {
                    StandardNumber = item.StandardNumber,
                    StandardName = !string.IsNullOrWhiteSpace(item.StandardTitle)
                        ? item.StandardTitle
                        : item.TitleAndScope,
                    Edition = item.Edition,
                    IsCurrent = item.IsCurrent
                })
                .OrderBy(item => item.StandardNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<SansMappingQueueResult> BuildMappingQueueAsync(
            ApplicationDbContext context,
            int qualificationId,
            CancellationToken cancellationToken = default)
        {
            var criteriaRows = await (
                from ac in context.AssessmentCriteria.AsNoTracking()
                join topic in context.Topics.AsNoTracking() on ac.TopicId equals topic.Id
                join subject in context.Subjects.AsNoTracking() on topic.SubjectId equals subject.Id
                where subject.QualificationId == qualificationId
                select new AssessmentCriteriaProjection
                {
                    AssessmentCriteriaId = ac.Id,
                    AssessmentCriteriaDescription = ac.Description,
                    TopicCode = topic.TopicCode,
                    TopicDescription = topic.TopicDescription ?? string.Empty,
                    TopicPurpose = topic.TopicPurpose,
                    SubjectCode = subject.SubjectCode,
                    SubjectDescription = subject.SubjectDescription ?? string.Empty
                }).ToListAsync(cancellationToken);

            await using var connection = new SqliteConnection(context.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var standards = await LoadMetadataAsync(connection, currentOnly: true, cancellationToken);
            if (standards.Count == 0)
            {
                return new SansMappingQueueResult
                {
                    Summary = new SansMappingQueueSummary(),
                    Items = new List<SansMappingQueueItem>()
                };
            }

            await DeletePendingMappingsAsync(connection, qualificationId, cancellationToken);

            foreach (var criteria in criteriaRows)
            {
                var searchable = NormalizeText(string.Join(" ",
                    criteria.AssessmentCriteriaDescription,
                    criteria.TopicDescription,
                    criteria.TopicPurpose,
                    criteria.SubjectDescription,
                    criteria.SubjectCode,
                    criteria.TopicCode));

                foreach (var standard in standards)
                {
                    var evaluation = EvaluateMapping(searchable, criteria, standard);
                    if (evaluation.Score < 60d) continue;

                    await UpsertMappingAsync(
                        connection,
                        qualificationId,
                        criteria.AssessmentCriteriaId,
                        standard.StandardNumber,
                        evaluation.Score,
                        evaluation.ConfidenceBand,
                        evaluation.Signals,
                        cancellationToken);
                }
            }

            return await GetMappingQueueAsync(connection, qualificationId, cancellationToken);
        }

        public async Task<SansMappingQueueResult> GetMappingQueueAsync(
            ApplicationDbContext context,
            int qualificationId,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqliteConnection(context.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return await GetMappingQueueAsync(connection, qualificationId, cancellationToken);
        }

        public async Task<SansApplyResult> ApplyMappingReviewAsync(
            ApplicationDbContext context,
            int qualificationId,
            string? itemId,
            IEnumerable<string>? itemIds,
            double? minConfidence,
            bool pendingOnly,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqliteConnection(context.Database.GetDbConnection().ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var candidateIds = new HashSet<long>();
            if (long.TryParse(itemId, out var parsedId)) candidateIds.Add(parsedId);
            foreach (var raw in itemIds ?? Enumerable.Empty<string>())
            {
                if (long.TryParse(raw, out var parsed)) candidateIds.Add(parsed);
            }

            var result = new SansApplyResult();
            if (candidateIds.Count == 0)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id
                    FROM ProposedStandardMappings
                    WHERE QualificationId = $qualificationId
                      AND ($pendingOnly = 0 OR lower(Status) = 'pending')
                      AND ($minConfidence IS NULL OR MatchConfidence >= $minConfidence)
                    ORDER BY MatchConfidence DESC, Id ASC;";
                command.Parameters.AddWithValue("$qualificationId", qualificationId);
                command.Parameters.AddWithValue("$pendingOnly", pendingOnly ? 1 : 0);
                command.Parameters.AddWithValue("$minConfidence", minConfidence.HasValue ? minConfidence.Value : DBNull.Value);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    candidateIds.Add(reader.GetInt64(0));
                }
            }

            result.Processed = candidateIds.Count;
            var now = DateTime.UtcNow.ToString("O");
            foreach (var id in candidateIds)
            {
                try
                {
                    await using var update = connection.CreateCommand();
                    update.CommandText = @"
                        UPDATE ProposedStandardMappings
                        SET Status = 'applied',
                            LastError = '',
                            ReviewedAtUtc = $reviewedAtUtc,
                            UpdatedAtUtc = $updatedAtUtc
                        WHERE Id = $id
                          AND QualificationId = $qualificationId
                          AND ($pendingOnly = 0 OR lower(Status) = 'pending');";
                    update.Parameters.AddWithValue("$reviewedAtUtc", now);
                    update.Parameters.AddWithValue("$updatedAtUtc", now);
                    update.Parameters.AddWithValue("$id", id);
                    update.Parameters.AddWithValue("$qualificationId", qualificationId);
                    update.Parameters.AddWithValue("$pendingOnly", pendingOnly ? 1 : 0);

                    if (await update.ExecuteNonQueryAsync(cancellationToken) > 0) result.Applied++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    await MarkMappingFailedAsync(connection, id, qualificationId, ex.Message, cancellationToken);
                }
            }

            return result;
        }

        private static SansMappingEvaluation EvaluateMapping(string searchable, AssessmentCriteriaProjection criteria, SansMetadataRow standard)
        {
            var matchedKeywords = new List<string>();
            foreach (var keyword in standard.Keywords)
            {
                if (keyword.Length < 4) continue;
                if (searchable.Contains($" {keyword} ", StringComparison.Ordinal)) matchedKeywords.Add(keyword);
            }

            var score = 22d + (matchedKeywords.Count * 19d);
            var signals = new List<string>();
            if (matchedKeywords.Count > 0) signals.Add($"Matched keywords: {string.Join(", ", matchedKeywords.Take(5))}");

            if (!string.IsNullOrWhiteSpace(standard.PrimarySubject) &&
                searchable.Contains($" {standard.PrimarySubject} ", StringComparison.Ordinal))
            {
                score += 12d;
                signals.Add($"Primary subject aligned: {standard.PrimarySubject}");
            }

            var topicText = NormalizeText($"{criteria.TopicDescription} {criteria.TopicPurpose}");
            foreach (var token in ExtractKeywords(standard.StandardTitle))
            {
                if (topicText.Contains($" {token} ", StringComparison.Ordinal))
                {
                    score += 6d;
                }
            }

            if (matchedKeywords.Count >= 3) score += 10d;
            var confidence = Clamp(score);
            return new SansMappingEvaluation
            {
                Score = confidence,
                ConfidenceBand = confidence >= 85d ? "high" : confidence >= 70d ? "medium" : "low",
                Signals = signals
            };
        }

        private static async Task DeletePendingMappingsAsync(SqliteConnection connection, int qualificationId, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM ProposedStandardMappings
                WHERE QualificationId = $qualificationId
                  AND lower(Status) = 'pending';";
            command.Parameters.AddWithValue("$qualificationId", qualificationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task UpsertMappingAsync(
            SqliteConnection connection,
            int qualificationId,
            int assessmentCriteriaId,
            string standardNumber,
            double matchConfidence,
            string confidenceBand,
            List<string> signals,
            CancellationToken cancellationToken)
        {
            await using var existing = connection.CreateCommand();
            existing.CommandText = @"
                SELECT Id
                FROM ProposedStandardMappings
                WHERE QualificationId = $qualificationId
                  AND AssessmentCriteriaId = $assessmentCriteriaId
                  AND StandardNumber = $standardNumber
                LIMIT 1;";
            existing.Parameters.AddWithValue("$qualificationId", qualificationId);
            existing.Parameters.AddWithValue("$assessmentCriteriaId", assessmentCriteriaId);
            existing.Parameters.AddWithValue("$standardNumber", standardNumber);

            long? existingId = null;
            await using (var reader = await existing.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken)) existingId = reader.GetInt64(0);
            }

            var now = DateTime.UtcNow.ToString("O");
            var signalsJson = JsonSerializer.Serialize(signals ?? new List<string>());
            if (existingId.HasValue)
            {
                await using var update = connection.CreateCommand();
                update.CommandText = @"
                    UPDATE ProposedStandardMappings
                    SET MatchConfidence = $matchConfidence,
                        ConfidenceBand = $confidenceBand,
                        SignalsJson = $signalsJson,
                        Status = CASE WHEN lower(Status) = 'applied' THEN Status ELSE 'pending' END,
                        UpdatedAtUtc = $updatedAtUtc
                    WHERE Id = $id;";
                update.Parameters.AddWithValue("$matchConfidence", matchConfidence);
                update.Parameters.AddWithValue("$confidenceBand", confidenceBand);
                update.Parameters.AddWithValue("$signalsJson", signalsJson);
                update.Parameters.AddWithValue("$updatedAtUtc", now);
                update.Parameters.AddWithValue("$id", existingId.Value);
                await update.ExecuteNonQueryAsync(cancellationToken);
                return;
            }

            await using var insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO ProposedStandardMappings
                (
                    QualificationId,
                    AssessmentCriteriaId,
                    StandardNumber,
                    MatchConfidence,
                    ConfidenceBand,
                    Status,
                    SignalsJson,
                    LastError,
                    CreatedAtUtc,
                    UpdatedAtUtc
                )
                VALUES
                (
                    $qualificationId,
                    $assessmentCriteriaId,
                    $standardNumber,
                    $matchConfidence,
                    $confidenceBand,
                    'pending',
                    $signalsJson,
                    '',
                    $createdAtUtc,
                    $updatedAtUtc
                );";
            insert.Parameters.AddWithValue("$qualificationId", qualificationId);
            insert.Parameters.AddWithValue("$assessmentCriteriaId", assessmentCriteriaId);
            insert.Parameters.AddWithValue("$standardNumber", standardNumber);
            insert.Parameters.AddWithValue("$matchConfidence", matchConfidence);
            insert.Parameters.AddWithValue("$confidenceBand", confidenceBand);
            insert.Parameters.AddWithValue("$signalsJson", signalsJson);
            insert.Parameters.AddWithValue("$createdAtUtc", now);
            insert.Parameters.AddWithValue("$updatedAtUtc", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task MarkMappingFailedAsync(SqliteConnection connection, long id, int qualificationId, string error, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE ProposedStandardMappings
                SET Status = 'failed',
                    LastError = $lastError,
                    UpdatedAtUtc = $updatedAtUtc
                WHERE Id = $id
                  AND QualificationId = $qualificationId;";
            command.Parameters.AddWithValue("$lastError", error ?? string.Empty);
            command.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$qualificationId", qualificationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<SansMappingQueueResult> GetMappingQueueAsync(SqliteConnection connection, int qualificationId, CancellationToken cancellationToken)
        {
            var items = new List<SansMappingQueueItem>();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    P.Id,
                    P.AssessmentCriteriaId,
                    COALESCE(AC.Description, ''),
                    COALESCE(T.TopicCode, ''),
                    COALESCE(T.TopicDescription, ''),
                    COALESCE(S.SubjectCode, ''),
                    COALESCE(S.SubjectDescription, ''),
                    P.StandardNumber,
                    COALESCE(M.StandardTitle, ''),
                    COALESCE(M.TitleAndScope, ''),
                    P.MatchConfidence,
                    COALESCE(P.ConfidenceBand, 'low'),
                    COALESCE(P.Status, 'pending'),
                    COALESCE(P.SignalsJson, '[]'),
                    COALESCE(P.LastError, ''),
                    P.ReviewedAtUtc
                FROM ProposedStandardMappings P
                JOIN AssessmentCriteria AC ON AC.Id = P.AssessmentCriteriaId
                LEFT JOIN Topics T ON T.Id = AC.TopicId
                LEFT JOIN Subjects S ON S.Id = T.SubjectId
                LEFT JOIN ScrapedSANSMetadata M ON M.StandardNumber = P.StandardNumber
                WHERE P.QualificationId = $qualificationId
                ORDER BY CASE lower(P.Status)
                    WHEN 'pending' THEN 0
                    WHEN 'applied' THEN 1
                    WHEN 'failed' THEN 2
                    ELSE 3
                END, P.MatchConfidence DESC, P.Id DESC;";
            command.Parameters.AddWithValue("$qualificationId", qualificationId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new SansMappingQueueItem
                {
                    Id = reader.GetInt64(0).ToString(),
                    AssessmentCriteriaId = reader.GetInt32(1),
                    AssessmentCriteriaDescription = reader.GetString(2),
                    TopicCode = reader.GetString(3),
                    TopicDescription = reader.GetString(4),
                    SubjectCode = reader.GetString(5),
                    SubjectDescription = reader.GetString(6),
                    StandardNumber = reader.GetString(7),
                    StandardTitle = reader.GetString(8),
                    TitleAndScope = reader.GetString(9),
                    MatchConfidence = reader.GetDouble(10),
                    ConfidenceBand = reader.GetString(11),
                    Status = reader.GetString(12),
                    Signals = DeserializeList(reader.GetString(13)),
                    LastError = reader.GetString(14),
                    ReviewedAtUtc = reader.IsDBNull(15)
                        ? null
                        : DateTime.TryParse(reader.GetString(15), out var reviewedAtUtc) ? reviewedAtUtc : null
                });
            }

            return new SansMappingQueueResult
            {
                Summary = new SansMappingQueueSummary
                {
                    Total = items.Count,
                    Pending = items.Count(x => string.Equals(x.Status, "pending", StringComparison.OrdinalIgnoreCase)),
                    Applied = items.Count(x => string.Equals(x.Status, "applied", StringComparison.OrdinalIgnoreCase)),
                    Failed = items.Count(x => string.Equals(x.Status, "failed", StringComparison.OrdinalIgnoreCase)),
                    HighConfidence = items.Count(x => string.Equals(x.ConfidenceBand, "high", StringComparison.OrdinalIgnoreCase)),
                    MediumConfidence = items.Count(x => string.Equals(x.ConfidenceBand, "medium", StringComparison.OrdinalIgnoreCase)),
                    LowConfidence = items.Count(x => string.Equals(x.ConfidenceBand, "low", StringComparison.OrdinalIgnoreCase))
                },
                Items = items
            };
        }

        private static async Task<List<SansMetadataRow>> LoadMetadataAsync(
            SqliteConnection connection,
            bool currentOnly,
            CancellationToken cancellationToken)
        {
            var rows = new List<SansMetadataRow>();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    StandardNumber,
                    Edition,
                    TitleAndScope,
                    StandardTitle,
                    PrimarySubject,
                    KeywordsJson,
                    IsCurrent,
                    StatusCategory,
                    SourceName,
                    SourceUrl,
                    SourceFilePath,
                    EvidenceSnippet
                FROM ScrapedSANSMetadata
                WHERE ($currentOnly = 0 OR IsCurrent = 1)
                ORDER BY StandardNumber ASC;";
            command.Parameters.AddWithValue("$currentOnly", currentOnly ? 1 : 0);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new SansMetadataRow
                {
                    StandardNumber = reader.GetString(0),
                    Edition = reader.GetString(1),
                    TitleAndScope = reader.GetString(2),
                    StandardTitle = reader.GetString(3),
                    PrimarySubject = reader.GetString(4),
                    Keywords = DeserializeList(reader.GetString(5)),
                    IsCurrent = reader.GetInt64(6) == 1,
                    StatusCategory = reader.GetString(7),
                    SourceName = reader.GetString(8),
                    SourceUrl = reader.GetString(9),
                    SourceFilePath = reader.GetString(10),
                    EvidenceSnippet = reader.GetString(11)
                });
            }

            return rows;
        }

        private static async Task<SansUpsertAction> UpsertMetadataAsync(SqliteConnection connection, SansMetadataRow row, CancellationToken cancellationToken)
        {
            await using var check = connection.CreateCommand();
            check.CommandText = @"
                SELECT Id
                FROM ScrapedSANSMetadata
                WHERE StandardNumber = $standardNumber
                LIMIT 1;";
            check.Parameters.AddWithValue("$standardNumber", row.StandardNumber);

            long? id = null;
            await using (var reader = await check.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken)) id = reader.GetInt64(0);
            }

            var now = DateTime.UtcNow.ToString("O");
            var keywordsJson = JsonSerializer.Serialize(row.Keywords ?? new List<string>());
            if (id.HasValue)
            {
                await using var update = connection.CreateCommand();
                update.CommandText = @"
                    UPDATE ScrapedSANSMetadata
                    SET Edition = $edition,
                        TitleAndScope = $titleAndScope,
                        StandardTitle = $standardTitle,
                        PrimarySubject = $primarySubject,
                        KeywordsJson = $keywordsJson,
                        IsCurrent = $isCurrent,
                        StatusCategory = $statusCategory,
                        SourceName = $sourceName,
                        SourceUrl = $sourceUrl,
                        SourceFilePath = $sourceFilePath,
                        EvidenceSnippet = $evidenceSnippet,
                        UpdatedAtUtc = $updatedAtUtc
                    WHERE Id = $id;";
                AddMetadataParameters(update, row, keywordsJson, now);
                update.Parameters.AddWithValue("$id", id.Value);
                await update.ExecuteNonQueryAsync(cancellationToken);
                return SansUpsertAction.Updated;
            }

            await using var insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO ScrapedSANSMetadata
                (
                    StandardNumber,
                    Edition,
                    TitleAndScope,
                    StandardTitle,
                    PrimarySubject,
                    KeywordsJson,
                    IsCurrent,
                    StatusCategory,
                    SourceName,
                    SourceUrl,
                    SourceFilePath,
                    EvidenceSnippet,
                    CreatedAtUtc,
                    UpdatedAtUtc
                )
                VALUES
                (
                    $standardNumber,
                    $edition,
                    $titleAndScope,
                    $standardTitle,
                    $primarySubject,
                    $keywordsJson,
                    $isCurrent,
                    $statusCategory,
                    $sourceName,
                    $sourceUrl,
                    $sourceFilePath,
                    $evidenceSnippet,
                    $createdAtUtc,
                    $updatedAtUtc
                );";
            AddMetadataParameters(insert, row, keywordsJson, now);
            insert.Parameters.AddWithValue("$createdAtUtc", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return SansUpsertAction.Inserted;
        }

        private static void AddMetadataParameters(SqliteCommand command, SansMetadataRow row, string keywordsJson, string now)
        {
            command.Parameters.AddWithValue("$standardNumber", row.StandardNumber);
            command.Parameters.AddWithValue("$edition", row.Edition ?? string.Empty);
            command.Parameters.AddWithValue("$titleAndScope", row.TitleAndScope ?? string.Empty);
            command.Parameters.AddWithValue("$standardTitle", row.StandardTitle ?? string.Empty);
            command.Parameters.AddWithValue("$primarySubject", row.PrimarySubject ?? string.Empty);
            command.Parameters.AddWithValue("$keywordsJson", keywordsJson);
            command.Parameters.AddWithValue("$isCurrent", row.IsCurrent ? 1 : 0);
            command.Parameters.AddWithValue("$statusCategory", row.StatusCategory ?? string.Empty);
            command.Parameters.AddWithValue("$sourceName", row.SourceName ?? string.Empty);
            command.Parameters.AddWithValue("$sourceUrl", row.SourceUrl ?? string.Empty);
            command.Parameters.AddWithValue("$sourceFilePath", row.SourceFilePath ?? string.Empty);
            command.Parameters.AddWithValue("$evidenceSnippet", row.EvidenceSnippet ?? string.Empty);
            command.Parameters.AddWithValue("$updatedAtUtc", now);
        }

        private static SansMetadataScanItem ToScanItem(SansMetadataRow row) => new()
        {
            StandardNumber = row.StandardNumber,
            Edition = row.Edition,
            TitleAndScope = row.TitleAndScope,
            StandardTitle = row.StandardTitle,
            PrimarySubject = row.PrimarySubject,
            Keywords = row.Keywords ?? new List<string>(),
            IsCurrent = row.IsCurrent,
            StatusCategory = row.StatusCategory,
            SourceName = row.SourceName,
            SourceUrl = row.SourceUrl,
            SourceFilePath = row.SourceFilePath
        };

        private static List<SansMetadataRow> ParseEntries(ParsedSourceDocument document)
        {
            var structuredEntries = ParseDssHtmlEntries(document);
            if (structuredEntries.Count > 0)
            {
                return structuredEntries;
            }

            var rawText = CollapseWhitespace(document.Text);
            var entries = new List<SansMetadataRow>();
            if (string.IsNullOrWhiteSpace(rawText)) return entries;

            foreach (Match match in SansEntryRegex.Matches(rawText))
            {
                var standardNumber = CollapseWhitespace(match.Groups["number"].Value).ToUpperInvariant();
                var titleAndScope = CollapseWhitespace(match.Groups["title"].Value);
                if (string.IsNullOrWhiteSpace(standardNumber) || string.IsNullOrWhiteSpace(titleAndScope) || titleAndScope.Length < 8)
                {
                    continue;
                }

                var status = InferStatusCategory(rawText, match.Index);
                var standardTitle = BuildStandardTitle(titleAndScope);
                var keywords = ExtractKeywords($"{standardNumber} {standardTitle} {titleAndScope}");
                entries.Add(new SansMetadataRow
                {
                    StandardNumber = standardNumber,
                    Edition = CollapseWhitespace(match.Groups["edition"].Value),
                    TitleAndScope = titleAndScope,
                    StandardTitle = standardTitle,
                    PrimarySubject = keywords.FirstOrDefault() ?? string.Empty,
                    Keywords = keywords,
                    IsCurrent = !string.Equals(status, "withdrawn", StringComparison.OrdinalIgnoreCase),
                    StatusCategory = status,
                    SourceName = document.SourceName,
                    SourceUrl = document.SourceUrl,
                    SourceFilePath = document.SourceFilePath,
                    EvidenceSnippet = BuildEvidenceSnippet(rawText, match.Index, match.Length)
                });
            }

            return entries.GroupBy(x => x.StandardNumber, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
        }

        private async Task<List<ParsedSourceDocument>> ReadRemoteDocumentsAsync(string url, string workingDirectory, CancellationToken cancellationToken)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ETDP-SANS-Scraper/1.0");

            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var uri = response.RequestMessage?.RequestUri?.ToString() ?? url;
            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
            if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) || uri.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pdfBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var path = SaveRemoteBinary(uri, workingDirectory, pdfBytes);
                return new List<ParsedSourceDocument>
                {
                    new() { SourceName = Path.GetFileName(path), SourceUrl = uri, SourceFilePath = path, Text = ExtractPdfText(pdfBytes) }
                };
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var documents = new List<ParsedSourceDocument>
            {
                new() { SourceName = uri, SourceUrl = uri, SourceFilePath = string.Empty, Text = HtmlToText(html), HtmlContent = html }
            };

            var pdfLinks = PdfHrefRegex.Matches(html)
                .Select(m => m.Groups["url"].Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(link => new Uri(new Uri(uri), link).ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            foreach (var pdfUrl in pdfLinks)
            {
                try
                {
                    var pdfBytes = await client.GetByteArrayAsync(pdfUrl, cancellationToken);
                    var path = SaveRemoteBinary(pdfUrl, workingDirectory, pdfBytes);
                    documents.Add(new ParsedSourceDocument
                    {
                        SourceName = Path.GetFileName(path),
                        SourceUrl = pdfUrl,
                        SourceFilePath = path,
                        Text = ExtractPdfText(pdfBytes)
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download linked SANS PDF {PdfUrl}", pdfUrl);
                }
            }

            return documents;
        }

        private static async Task<ParsedSourceDocument?> ReadLocalDocumentAsync(string path, CancellationToken cancellationToken)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var text = ext switch
            {
                ".pdf" => ExtractPdfText(await File.ReadAllBytesAsync(path, cancellationToken)),
                ".txt" => await File.ReadAllTextAsync(path, cancellationToken),
                ".md" => await File.ReadAllTextAsync(path, cancellationToken),
                ".docx" => ExtractDocxText(path),
                ".html" => HtmlToText(await File.ReadAllTextAsync(path, cancellationToken)),
                ".htm" => HtmlToText(await File.ReadAllTextAsync(path, cancellationToken)),
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(text)) return null;
            var htmlContent = (ext == ".html" || ext == ".htm")
                ? await File.ReadAllTextAsync(path, cancellationToken)
                : string.Empty;
            return new ParsedSourceDocument
            {
                SourceName = Path.GetFileName(path),
                SourceUrl = string.Empty,
                SourceFilePath = path,
                Text = text,
                HtmlContent = htmlContent
            };
        }

        private static string SaveRemoteBinary(string url, string workingDirectory, byte[] bytes)
        {
            var fileName = MakeSafeFilename(Path.GetFileName(new Uri(url).AbsolutePath), "remote-source.pdf");
            var path = Path.Combine(workingDirectory, fileName);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static string ExtractDocxText(string path)
        {
            using var document = WordprocessingDocument.Open(path, false);
            var texts = document.MainDocumentPart?.Document?.Descendants<Text>()
                .Select(x => x.Text)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList() ?? new List<string>();
            return string.Join(Environment.NewLine, texts);
        }

        private static string ExtractPdfText(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new PdfReader(stream);
            using var pdf = new PdfDocument(reader);
            var pages = new List<string>();
            for (var i = 1; i <= pdf.GetNumberOfPages(); i++)
            {
                pages.Add(PdfTextExtractor.GetTextFromPage(pdf.GetPage(i)));
            }

            return string.Join(Environment.NewLine, pages);
        }

        private static string HtmlToText(string html)
        {
            var withoutTags = HtmlTagRegex.Replace(html ?? string.Empty, " ");
            return CollapseWhitespace(System.Net.WebUtility.HtmlDecode(withoutTags));
        }

        private static List<SansMetadataRow> ParseDssHtmlEntries(ParsedSourceDocument document)
        {
            var html = document.HtmlContent ?? string.Empty;
            if (string.IsNullOrWhiteSpace(html)) return new List<SansMetadataRow>();
            if (!html.Contains("standardsTable", StringComparison.OrdinalIgnoreCase)
                && !html.Contains("download-btn", StringComparison.OrdinalIgnoreCase)
                && !html.Contains("data-standard-number", StringComparison.OrdinalIgnoreCase))
            {
                return new List<SansMetadataRow>();
            }

            var rows = new List<SansMetadataRow>();
            foreach (Match rowMatch in HtmlTableRowRegex.Matches(html))
            {
                var rowHtml = rowMatch.Groups["row"].Value;
                if (string.IsNullOrWhiteSpace(rowHtml)) continue;
                if (!rowHtml.Contains("SANS", StringComparison.OrdinalIgnoreCase)) continue;

                var cells = HtmlTableCellRegex.Matches(rowHtml)
                    .Select(match => HtmlToText(match.Groups["cell"].Value))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
                if (cells.Count < 2) continue;

                var rawDesignation = GetHtmlAttributeValue(rowHtml, "data-standard-number");
                if (string.IsNullOrWhiteSpace(rawDesignation))
                {
                    rawDesignation = cells[0];
                }

                var designation = SplitStandardDesignation(rawDesignation);
                if (string.IsNullOrWhiteSpace(designation.StandardNumber)
                    || !designation.StandardNumber.StartsWith("SANS ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var standardTitle = cells.ElementAtOrDefault(1) ?? string.Empty;
                var scope = cells.ElementAtOrDefault(2) ?? string.Empty;
                var titleAndScope = string.Join(" ", new[] { standardTitle, scope }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
                if (string.IsNullOrWhiteSpace(titleAndScope)) continue;

                var downloadUrl = GetHtmlAttributeValue(rowHtml, "data-comment-link");
                var status = InferStructuredStatus(standardTitle, scope);
                var keywords = ExtractKeywords($"{designation.StandardNumber} {standardTitle} {scope}");

                rows.Add(new SansMetadataRow
                {
                    StandardNumber = designation.StandardNumber,
                    Edition = designation.Edition,
                    TitleAndScope = titleAndScope,
                    StandardTitle = CollapseWhitespace(standardTitle),
                    PrimarySubject = keywords.FirstOrDefault() ?? string.Empty,
                    Keywords = keywords,
                    IsCurrent = !string.Equals(status, "withdrawn", StringComparison.OrdinalIgnoreCase),
                    StatusCategory = status,
                    SourceName = document.SourceName,
                    SourceUrl = !string.IsNullOrWhiteSpace(downloadUrl) ? downloadUrl : document.SourceUrl,
                    SourceFilePath = document.SourceFilePath,
                    EvidenceSnippet = CollapseWhitespace($"{rawDesignation} | {standardTitle} | {scope}")
                });
            }

            return rows
                .GroupBy(x => x.StandardNumber, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => item.TitleAndScope.Length)
                    .ThenByDescending(item => item.StandardTitle.Length)
                    .First())
                .ToList();
        }

        private static StandardDesignationParts SplitStandardDesignation(string rawDesignation)
        {
            var cleaned = CollapseWhitespace(rawDesignation);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return new StandardDesignationParts();
            }

            var match = Regex.Match(
                cleaned,
                @"^(?<number>SANS\s\d{1,5}(?:-\d+)*(?::\d{4})?)(?:\s+(?<edition>.*))?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                return new StandardDesignationParts
                {
                    StandardNumber = cleaned.ToUpperInvariant(),
                    Edition = string.Empty
                };
            }

            return new StandardDesignationParts
            {
                StandardNumber = CollapseWhitespace(match.Groups["number"].Value).ToUpperInvariant(),
                Edition = CollapseWhitespace(match.Groups["edition"].Value)
            };
        }

        private static string InferStructuredStatus(string title, string scope)
        {
            var combined = $"{title} {scope}".ToLowerInvariant();
            if (combined.Contains("withdrawn", StringComparison.Ordinal)) return "withdrawn";
            if (combined.Contains("amended", StringComparison.Ordinal)) return "amended";
            if (combined.Contains("new standard", StringComparison.Ordinal)) return "new";
            return "current";
        }

        private static string GetHtmlAttributeValue(string html, string attributeName)
        {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(attributeName)) return string.Empty;

            foreach (Match match in HtmlAttributeRegex.Matches(html))
            {
                var name = match.Groups["name"].Value;
                if (!string.Equals(name, attributeName, StringComparison.OrdinalIgnoreCase)) continue;
                return System.Net.WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
            }

            return string.Empty;
        }

        private static string BuildStandardTitle(string titleAndScope)
        {
            foreach (var separator in new[] { ". ", "; ", "  " })
            {
                var index = titleAndScope.IndexOf(separator, StringComparison.Ordinal);
                if (index > 0) return titleAndScope[..index].Trim();
            }

            return titleAndScope.Trim();
        }

        private static List<string> ExtractKeywords(string raw)
        {
            return Regex.Matches((raw ?? string.Empty).ToLowerInvariant(), @"[a-z][a-z0-9\-]{2,}")
                .Select(match => match.Value.Trim('-'))
                .Where(token => token.Length >= 4)
                .Where(token => !StopWords.Contains(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
        }

        private static string InferStatusCategory(string rawText, int matchIndex)
        {
            var start = Math.Max(0, matchIndex - 300);
            var length = Math.Min(rawText.Length - start, 700);
            var window = rawText.Substring(start, length).ToLowerInvariant();
            if (window.Contains("schedule a.2", StringComparison.Ordinal) || window.Contains("withdrawn", StringComparison.Ordinal)) return "withdrawn";
            if (window.Contains("schedule b.2", StringComparison.Ordinal) || window.Contains("amended", StringComparison.Ordinal)) return "amended";
            if (window.Contains("schedule b.1", StringComparison.Ordinal) || window.Contains("new standard", StringComparison.Ordinal)) return "new";
            return "current";
        }

        private static string BuildEvidenceSnippet(string rawText, int startIndex, int length)
        {
            var snippetStart = Math.Max(0, startIndex - 80);
            var snippetLength = Math.Min(rawText.Length - snippetStart, length + 160);
            return CollapseWhitespace(rawText.Substring(snippetStart, snippetLength));
        }

        private static string CollapseWhitespace(string value) => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();

        private static string NormalizeText(string value) => $" {CollapseWhitespace(value).ToLowerInvariant()} ";

        private static List<string> DeserializeList(string rawJson)
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(rawJson ?? "[]") ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static string MakeSafeFilename(string? rawName, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(rawName) ? fallback : rawName.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            if (string.IsNullOrWhiteSpace(Path.GetExtension(value))) value += Path.GetExtension(fallback);
            return value;
        }

        private static double Clamp(double value) => Math.Round(Math.Max(0d, Math.Min(100d, value)), 2, MidpointRounding.AwayFromZero);

        private sealed class ParsedSourceDocument
        {
            public string SourceName { get; set; } = string.Empty;
            public string SourceUrl { get; set; } = string.Empty;
            public string SourceFilePath { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
            public string HtmlContent { get; set; } = string.Empty;
        }

        private sealed class StandardDesignationParts
        {
            public string StandardNumber { get; set; } = string.Empty;
            public string Edition { get; set; } = string.Empty;
        }

        private sealed class AssessmentCriteriaProjection
        {
            public int AssessmentCriteriaId { get; set; }
            public string AssessmentCriteriaDescription { get; set; } = string.Empty;
            public string TopicCode { get; set; } = string.Empty;
            public string TopicDescription { get; set; } = string.Empty;
            public string TopicPurpose { get; set; } = string.Empty;
            public string SubjectCode { get; set; } = string.Empty;
            public string SubjectDescription { get; set; } = string.Empty;
        }

        private sealed class SansMetadataRow
        {
            public string StandardNumber { get; set; } = string.Empty;
            public string Edition { get; set; } = string.Empty;
            public string TitleAndScope { get; set; } = string.Empty;
            public string StandardTitle { get; set; } = string.Empty;
            public string PrimarySubject { get; set; } = string.Empty;
            public List<string> Keywords { get; set; } = new();
            public bool IsCurrent { get; set; } = true;
            public string StatusCategory { get; set; } = "current";
            public string SourceName { get; set; } = string.Empty;
            public string SourceUrl { get; set; } = string.Empty;
            public string SourceFilePath { get; set; } = string.Empty;
            public string EvidenceSnippet { get; set; } = string.Empty;
        }

        private sealed class SansMappingEvaluation
        {
            public double Score { get; set; }
            public string ConfidenceBand { get; set; } = "low";
            public List<string> Signals { get; set; } = new();
        }

        private enum SansUpsertAction
        {
            None = 0,
            Inserted = 1,
            Updated = 2
        }
    }

    public sealed class SansMetadataScanResult
    {
        public int SourceCount { get; set; }
        public int ProcessedDocuments { get; set; }
        public int ExtractedEntries { get; set; }
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int CurrentCount { get; set; }
        public int WithdrawnCount { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<SansMetadataScanItem> Metadata { get; set; } = new();
    }

    public sealed class SansMetadataScanItem
    {
        public string StandardNumber { get; set; } = string.Empty;
        public string Edition { get; set; } = string.Empty;
        public string TitleAndScope { get; set; } = string.Empty;
        public string StandardTitle { get; set; } = string.Empty;
        public string PrimarySubject { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public bool IsCurrent { get; set; }
        public string StatusCategory { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string SourceFilePath { get; set; } = string.Empty;
    }

    public sealed class SansMetadataIndexResult
    {
        public int TotalCount { get; set; }
        public int CurrentCount { get; set; }
        public int WithdrawnCount { get; set; }
        public List<SansMetadataScanItem> Metadata { get; set; } = new();
    }

    public sealed class SansCodeNameItem
    {
        public string StandardNumber { get; set; } = string.Empty;
        public string StandardName { get; set; } = string.Empty;
        public string Edition { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
    }

    public sealed class SansMappingQueueResult
    {
        public SansMappingQueueSummary Summary { get; set; } = new();
        public List<SansMappingQueueItem> Items { get; set; } = new();
    }

    public sealed class SansMappingQueueSummary
    {
        public int Total { get; set; }
        public int Pending { get; set; }
        public int Applied { get; set; }
        public int Failed { get; set; }
        public int HighConfidence { get; set; }
        public int MediumConfidence { get; set; }
        public int LowConfidence { get; set; }
    }

    public sealed class SansMappingQueueItem
    {
        public string Id { get; set; } = string.Empty;
        public int AssessmentCriteriaId { get; set; }
        public string AssessmentCriteriaDescription { get; set; } = string.Empty;
        public string TopicCode { get; set; } = string.Empty;
        public string TopicDescription { get; set; } = string.Empty;
        public string SubjectCode { get; set; } = string.Empty;
        public string SubjectDescription { get; set; } = string.Empty;
        public string StandardNumber { get; set; } = string.Empty;
        public string StandardTitle { get; set; } = string.Empty;
        public string TitleAndScope { get; set; } = string.Empty;
        public double MatchConfidence { get; set; }
        public string ConfidenceBand { get; set; } = "low";
        public string Status { get; set; } = "pending";
        public List<string> Signals { get; set; } = new();
        public string? LastError { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
    }

    public sealed class SansApplyResult
    {
        public int Processed { get; set; }
        public int Applied { get; set; }
        public int Failed { get; set; }
    }
}
