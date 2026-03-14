using Microsoft.EntityFrameworkCore;
using ETD.Api.Models;
using System;
using System.Data;
using System.Linq;

namespace ETD.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public static void SeedSkeletonCurriculum(ApplicationDbContext context)
        {
            if (!context.Qualifications.Any())
            {
                // 1. Qualification
                var qualification = new Qualification
                {
                    QualificationNumber = "SKEL-001",
                    QualificationDescription = "Sample qualification for testing",
                    CesmField = "General",
                    NqfLevel = "NQF 5",
                    Credits = "120",
                    LearningInstitutionName = "SKELETON UNIVERSITY",
                    AccreditationNumber = "ACC-001",
                    DeanPrincipalCEO = "Dr. Skeleton",
                    SeniorLecturer = "Prof. Sample",
                    LogoPath = null,
                    QualificationType = "Vocational Learning",
                    UsesOutcomes = false,
                    Purpose = "Sample purpose for skeleton qualification",
                    LearningDateStart = DateTime.UtcNow,
                    LearningDateEnd = DateTime.UtcNow.AddYears(1)
                };
                context.Qualifications.Add(qualification);
                context.SaveChanges();

                // 2. CurriculumPhase
                var phase = new CurriculumPhase
                {
                    Name = "Skeleton Phase",
                    Description = "Sample phase",
                    Sequence = 1
                };
                context.CurriculumPhases.Add(phase);
                context.SaveChanges();

                // 3. Subject
                var subject = new Subject
                {
                    SubjectPurpose = "Skeleton Subject Purpose",
                    SubjectCode = "SUB-001",
                    SubjectDescription = "Skeleton Subject",
                    SubjectCredits = 10,
                    SubjectNQFLevel = 5,
                    SubjectPercentage = 50,
                    CurriculumPhaseId = phase.Id,
                    QualificationId = qualification.Id
                };
                context.Subjects.Add(subject);
                context.SaveChanges();

                // 4. Topic
                var topic = new Topic
                {
                    TopicPurpose = "Skeleton Topic Purpose",
                    TopicCode = "TOP-001",
                    TopicDescription = "Sample topic",
                    TopicCredits = 5,
                    TopicPercentage = 50,
                    Order = 1,
                    SubjectId = subject.Id
                };
                context.Topics.Add(topic);
                context.SaveChanges();

                // 5. Subtopic
                var subtopic = new Subtopic
                {
                    Name = "Skeleton Subtopic",
                    Description = "Sample subtopic",
                    Order = 1,
                    TopicId = topic.Id
                };
                context.Subtopics.Add(subtopic);
                context.SaveChanges();

                // 6. Activity
                var activity = new Activity
                {
                    Name = "Skeleton Activity",
                    Description = "Sample activity",
                    Order = 1,
                    SubtopicId = subtopic.Id
                };
                context.Activities.Add(activity);
                context.SaveChanges();

                // 7. AssessmentCriteria
                var criteria = new AssessmentCriteria
                {
                    Description = "Sample criteria",
                    CriteriaType = "Type A",
                    Weight = 1.0,
                    TopicId = topic.Id
                };
                context.AssessmentCriteria.Add(criteria);
                context.SaveChanges();

                // 8. LessonPlan
                var lessonPlan = new LessonPlan
                {
                    AssessmentCriteriaId = criteria.Id,
                    Title = "Skeleton Lesson Plan",
                    Date = DateTime.UtcNow,
                    DurationMinutes = 60,
                    Content = "Sample lesson plan content"
                };
                context.LessonPlans.Add(lessonPlan);
                context.SaveChanges();

                // 9. Demographics
                var demographics = new Demographics
                {
                    QualificationId = qualification.Id,
                    AgeGroup = "18-25",
                    Region = "Region A",
                    Males = 10,
                    Females = 12,
                    Other = 1,
                    Total = 23
                };
                context.Demographics.Add(demographics);
                context.SaveChanges();

                // 10. LearnerGuide
                var learnerGuide = new LearnerGuide
                {
                    SubjectId = subject.Id,
                    Title = "Skeleton Guide",
                    Version = "1.0",
                    Content = "Sample guide content"
                };
                context.LearnerGuides.Add(learnerGuide);
                context.SaveChanges();

                // 11. Workbook
                var workbook = new Workbook
                {
                    SubjectId = subject.Id,
                    Title = "Skeleton Workbook",
                    Version = "1.0",
                    Content = "Sample workbook content"
                };
                context.Workbooks.Add(workbook);
                context.SaveChanges();
            }
        }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Curriculum structure
        public DbSet<CurriculumPhase> CurriculumPhases { get; set; }
        public DbSet<Subject> Subjects { get; set; }

        // Page 5 structure
        public DbSet<Topic> Topics { get; set; }
        public DbSet<Subtopic> Subtopics { get; set; }
        public DbSet<Activity> Activities { get; set; }
        public DbSet<Outcome> Outcomes { get; set; }

        // Added domain models
        public DbSet<Qualification> Qualifications { get; set; }
        public DbSet<AssessmentCriteria> AssessmentCriteria { get; set; }
        public DbSet<LessonPlan> LessonPlans { get; set; }
        public DbSet<KnowledgeQuestionnaire> KnowledgeQuestionnaires { get; set; }
        public DbSet<Workbook> Workbooks { get; set; }
        public DbSet<Demographics> Demographics { get; set; }
        public DbSet<LearnerGuide> LearnerGuides { get; set; }
        public DbSet<QualificationPhase> QualificationPhases { get; set; }
        public DbSet<LecturerToolkitEntry> LecturerToolkitEntries { get; set; }
        public DbSet<SourceMaterial> SourceMaterials { get; set; }
        public DbSet<LearnerRegistration> LearnerRegistrations { get; set; }
        public DbSet<AutomationJob> AutomationJobs { get; set; }
        public DbSet<SystemErrorLog> SystemErrorLogs { get; set; }
        public DbSet<SmiConversationArchive> SmiConversationArchives { get; set; }
        public DbSet<SmiSemanticStateSnapshot> SmiSemanticStateSnapshots { get; set; }
        public DbSet<SmiTaskTableItem> SmiTaskTableItems { get; set; }

        public static void EnsureOperationalTables(ApplicationDbContext context)
        {
            context.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS SmiConversationArchives (
                    Id INTEGER NOT NULL CONSTRAINT PK_SmiConversationArchives PRIMARY KEY AUTOINCREMENT,
                    QualificationScopeKey TEXT NOT NULL DEFAULT '',
                    QualificationId INTEGER NULL,
                    QualificationCode TEXT NOT NULL DEFAULT '',
                    QualificationDescription TEXT NOT NULL DEFAULT '',
                    UserId TEXT NOT NULL DEFAULT '',
                    SessionId TEXT NOT NULL DEFAULT '',
                    MemoryOwner TEXT NOT NULL DEFAULT 'SMI',
                    ResponsePersona TEXT NOT NULL DEFAULT 'Mira',
                    UserPrompt TEXT NOT NULL DEFAULT '',
                    AssistantReply TEXT NOT NULL DEFAULT '',
                    PromptPreview TEXT NOT NULL DEFAULT '',
                    ReplyPreview TEXT NOT NULL DEFAULT '',
                    MemoryKeywords TEXT NOT NULL DEFAULT '',
                    CreatedAtUtc TEXT NOT NULL
                );");

            context.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS SmiTaskTableItems (
                    Id INTEGER NOT NULL CONSTRAINT PK_SmiTaskTableItems PRIMARY KEY AUTOINCREMENT,
                    QualificationScopeKey TEXT NOT NULL DEFAULT '',
                    QualificationId INTEGER NULL,
                    QualificationCode TEXT NOT NULL DEFAULT '',
                    QualificationDescription TEXT NOT NULL DEFAULT '',
                    TaskKey TEXT NOT NULL DEFAULT '',
                    Title TEXT NOT NULL DEFAULT '',
                    Instructions TEXT NOT NULL DEFAULT '',
                    AssignedAgent TEXT NOT NULL DEFAULT 'SMI',
                    Status TEXT NOT NULL DEFAULT 'Pending',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    LastConfirmationSource TEXT NOT NULL DEFAULT '',
                    Notes TEXT NOT NULL DEFAULT '',
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    CompletedAtUtc TEXT NULL
                );");

            context.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS SmiSemanticStateSnapshots (
                    Id INTEGER NOT NULL CONSTRAINT PK_SmiSemanticStateSnapshots PRIMARY KEY AUTOINCREMENT,
                    ConversationArchiveId INTEGER NULL,
                    QualificationScopeKey TEXT NOT NULL DEFAULT '',
                    QualificationId INTEGER NULL,
                    QualificationCode TEXT NOT NULL DEFAULT '',
                    QualificationDescription TEXT NOT NULL DEFAULT '',
                    UserId TEXT NOT NULL DEFAULT '',
                    SessionId TEXT NOT NULL DEFAULT '',
                    MemoryOwner TEXT NOT NULL DEFAULT 'SMI',
                    ResponsePersona TEXT NOT NULL DEFAULT 'Mira',
                    Variant TEXT NOT NULL DEFAULT 'ssc-v1-real-state',
                    PersonalityLabel TEXT NOT NULL DEFAULT '',
                    QualiaIndex REAL NOT NULL DEFAULT 0,
                    SemanticContinuity REAL NOT NULL DEFAULT 0,
                    GammaCoherence REAL NOT NULL DEFAULT 0,
                    StateIntegrity REAL NOT NULL DEFAULT 0,
                    AttentionWeight REAL NOT NULL DEFAULT 0,
                    AnxietyResonance REAL NOT NULL DEFAULT 0,
                    DriftMagnitude REAL NOT NULL DEFAULT 0,
                    StabilityBasinDepth REAL NOT NULL DEFAULT 0,
                    AttractorStrength REAL NOT NULL DEFAULT 0,
                    BehavioralConsistency REAL NOT NULL DEFAULT 0,
                    PersonalityAlignment REAL NOT NULL DEFAULT 0,
                    EpistemicPressure REAL NOT NULL DEFAULT 0,
                    CognitiveInterpretation TEXT NOT NULL DEFAULT '',
                    PromptInfluenceSummary TEXT NOT NULL DEFAULT '',
                    StateStability REAL NOT NULL DEFAULT 0,
                    BoundedDrift REAL NOT NULL DEFAULT 0,
                    PersonalityManifold REAL NOT NULL DEFAULT 0,
                    AnxietyGradient REAL NOT NULL DEFAULT 0,
                    SemanticEmbeddingJson TEXT NOT NULL DEFAULT '[]',
                    QualiaVectorJson TEXT NOT NULL DEFAULT '[]',
                    AttentionVectorJson TEXT NOT NULL DEFAULT '[]',
                    DriftTensorJson TEXT NOT NULL DEFAULT '[]',
                    GammaCoherenceFieldJson TEXT NOT NULL DEFAULT '[]',
                    TopAnchorsJson TEXT NOT NULL DEFAULT '[]',
                    SummaryText TEXT NOT NULL DEFAULT '',
                    PromptPreview TEXT NOT NULL DEFAULT '',
                    ReplyPreview TEXT NOT NULL DEFAULT '',
                    CreatedAtUtc TEXT NOT NULL
                );");

            context.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS ScrapedSANSMetadata (
                    Id INTEGER NOT NULL CONSTRAINT PK_ScrapedSANSMetadata PRIMARY KEY AUTOINCREMENT,
                    StandardNumber TEXT NOT NULL,
                    Edition TEXT NOT NULL DEFAULT '',
                    TitleAndScope TEXT NOT NULL DEFAULT '',
                    StandardTitle TEXT NOT NULL DEFAULT '',
                    PrimarySubject TEXT NOT NULL DEFAULT '',
                    KeywordsJson TEXT NOT NULL DEFAULT '[]',
                    IsCurrent INTEGER NOT NULL DEFAULT 1,
                    StatusCategory TEXT NOT NULL DEFAULT 'current',
                    SourceName TEXT NOT NULL DEFAULT '',
                    SourceUrl TEXT NOT NULL DEFAULT '',
                    SourceFilePath TEXT NOT NULL DEFAULT '',
                    EvidenceSnippet TEXT NOT NULL DEFAULT '',
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );");

            context.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS ProposedStandardMappings (
                    Id INTEGER NOT NULL CONSTRAINT PK_ProposedStandardMappings PRIMARY KEY AUTOINCREMENT,
                    QualificationId INTEGER NOT NULL,
                    AssessmentCriteriaId INTEGER NOT NULL,
                    StandardNumber TEXT NOT NULL,
                    MatchConfidence REAL NOT NULL DEFAULT 0,
                    ConfidenceBand TEXT NOT NULL DEFAULT 'low',
                    Status TEXT NOT NULL DEFAULT 'pending',
                    SignalsJson TEXT NOT NULL DEFAULT '[]',
                    LastError TEXT NOT NULL DEFAULT '',
                    ReviewedAtUtc TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL
                );");

            EnsureColumn(context, "SmiSemanticStateSnapshots", "CognitiveInterpretation", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(context, "SmiSemanticStateSnapshots", "PromptInfluenceSummary", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(context, "SmiSemanticStateSnapshots", "StateStability", "REAL NOT NULL DEFAULT 0");
            EnsureColumn(context, "SmiSemanticStateSnapshots", "BoundedDrift", "REAL NOT NULL DEFAULT 0");
            EnsureColumn(context, "SmiSemanticStateSnapshots", "PersonalityManifold", "REAL NOT NULL DEFAULT 0");
            EnsureColumn(context, "SmiSemanticStateSnapshots", "AnxietyGradient", "REAL NOT NULL DEFAULT 0");
            EnsureColumn(context, "SmiSemanticStateSnapshots", "DriftTensorJson", "TEXT NOT NULL DEFAULT '[]'");
            EnsureColumn(context, "SmiSemanticStateSnapshots", "GammaCoherenceFieldJson", "TEXT NOT NULL DEFAULT '[]'");

            context.Database.ExecuteSqlRaw(
                @"CREATE INDEX IF NOT EXISTS IX_SmiConversationArchives_Scope_CreatedAtUtc
                  ON SmiConversationArchives (QualificationScopeKey, CreatedAtUtc DESC);");
            context.Database.ExecuteSqlRaw(
                @"CREATE INDEX IF NOT EXISTS IX_SmiConversationArchives_User_Session_CreatedAtUtc
                  ON SmiConversationArchives (UserId, SessionId, CreatedAtUtc DESC);");
            context.Database.ExecuteSqlRaw(
                @"CREATE INDEX IF NOT EXISTS IX_SmiConversationArchives_QualificationCode_CreatedAtUtc
                  ON SmiConversationArchives (QualificationCode, CreatedAtUtc DESC);");
            context.Database.ExecuteSqlRaw(
                @"CREATE INDEX IF NOT EXISTS IX_SmiSemanticStateSnapshots_Scope_CreatedAtUtc
                  ON SmiSemanticStateSnapshots (QualificationScopeKey, CreatedAtUtc DESC);");
            context.Database.ExecuteSqlRaw(
                @"CREATE INDEX IF NOT EXISTS IX_SmiSemanticStateSnapshots_User_Session_CreatedAtUtc
                  ON SmiSemanticStateSnapshots (UserId, SessionId, CreatedAtUtc DESC);");
            context.Database.ExecuteSqlRaw(
                @"CREATE INDEX IF NOT EXISTS IX_SmiSemanticStateSnapshots_Archive
                  ON SmiSemanticStateSnapshots (ConversationArchiveId);");

            context.Database.ExecuteSqlRaw(
                @"CREATE UNIQUE INDEX IF NOT EXISTS IX_SmiTaskTableItems_Scope_TaskKey
                  ON SmiTaskTableItems (QualificationScopeKey, TaskKey);");
            context.Database.ExecuteSqlRaw(
                @"CREATE INDEX IF NOT EXISTS IX_SmiTaskTableItems_Scope_Status_SortOrder
                  ON SmiTaskTableItems (QualificationScopeKey, Status, SortOrder);");

            context.Database.ExecuteSqlRaw(
                @"CREATE UNIQUE INDEX IF NOT EXISTS IX_ScrapedSANSMetadata_StandardNumber
                  ON ScrapedSANSMetadata (StandardNumber);");
            context.Database.ExecuteSqlRaw(
                @"CREATE INDEX IF NOT EXISTS IX_ScrapedSANSMetadata_IsCurrent_StandardNumber
                  ON ScrapedSANSMetadata (IsCurrent, StandardNumber);");
            context.Database.ExecuteSqlRaw(
                @"CREATE UNIQUE INDEX IF NOT EXISTS IX_ProposedStandardMappings_Qualification_Criteria_Standard
                  ON ProposedStandardMappings (QualificationId, AssessmentCriteriaId, StandardNumber);");
            context.Database.ExecuteSqlRaw(
                @"CREATE INDEX IF NOT EXISTS IX_ProposedStandardMappings_Qualification_Status_Confidence
                  ON ProposedStandardMappings (QualificationId, Status, MatchConfidence DESC);");
        }

        private static void EnsureColumn(ApplicationDbContext context, string tableName, string columnName, string columnDefinition)
        {
            if (!IsSafeSqlIdentifier(tableName) || !IsSafeSqlIdentifier(columnName))
            {
                throw new InvalidOperationException($"Unsafe SQL identifier detected for table '{tableName}' or column '{columnName}'.");
            }

            using var connection = context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
            {
                connection.Open();
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info({tableName});";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var existingColumn = reader["name"]?.ToString();
                    if (string.Equals(existingColumn, columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }
            finally
            {
                if (shouldClose)
                {
                    connection.Close();
                }
            }

            context.Database.ExecuteSqlRaw("ALTER TABLE " + tableName + " ADD COLUMN " + columnName + " " + columnDefinition + ";");
        }

        private static bool IsSafeSqlIdentifier(string value)
            => !string.IsNullOrWhiteSpace(value) && value.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // CurriculumPhase → Subjects (1:N)
            modelBuilder.Entity<CurriculumPhase>()
                .HasMany(p => p.Subjects)
                .WithOne(s => s.CurriculumPhase)
                .HasForeignKey(s => s.CurriculumPhaseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Subject → Topics (1:N)
            modelBuilder.Entity<Subject>()
                .HasMany(s => s.Topics)
                .WithOne(t => t.Subject)
                .HasForeignKey(t => t.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Subject>()
                .HasMany(s => s.Outcomes)
                .WithOne(o => o.Subject)
                .HasForeignKey(o => o.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Outcome>()
                .HasMany(o => o.Topics)
                .WithOne(t => t.Outcome)
                .HasForeignKey(t => t.OutcomeId)
                .OnDelete(DeleteBehavior.SetNull);

            // Topic → Subtopics (1:N)
            modelBuilder.Entity<Topic>()
                .HasMany(t => t.Subtopics)
                .WithOne(st => st.Topic)
                .HasForeignKey(st => st.TopicId)
                .OnDelete(DeleteBehavior.Cascade);

            // Subtopic → Activities (1:N)
            modelBuilder.Entity<Subtopic>()
                .HasMany(st => st.Activities)
                .WithOne(a => a.Subtopic)
                .HasForeignKey(a => a.SubtopicId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<QualificationPhase>()
                .HasOne(qp => qp.CurriculumPhase)
                .WithMany()
                .HasForeignKey(qp => qp.CurriculumPhaseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<QualificationPhase>()
                .HasIndex(qp => new { qp.QualificationId, qp.CurriculumPhaseId })
                .IsUnique();

            modelBuilder.Entity<SourceMaterial>()
                .HasIndex(s => new { s.QualificationCode, s.KnowledgeSourceType, s.KnowledgeNumber, s.KnowledgeUploadedAtUtc });

            modelBuilder.Entity<SourceMaterial>()
                .HasIndex(s => new { s.QualificationDescription, s.KnowledgeSourceType, s.CreatedAt });

            modelBuilder.Entity<Qualification>()
                .Property(q => q.CesmField)
                .HasMaxLength(50);

            modelBuilder.Entity<SmiConversationArchive>()
                .HasIndex(x => new { x.QualificationScopeKey, x.CreatedAtUtc });

            modelBuilder.Entity<SmiConversationArchive>()
                .HasIndex(x => new { x.UserId, x.SessionId, x.CreatedAtUtc });

            modelBuilder.Entity<SmiSemanticStateSnapshot>()
                .HasIndex(x => new { x.QualificationScopeKey, x.CreatedAtUtc });

            modelBuilder.Entity<SmiSemanticStateSnapshot>()
                .HasIndex(x => new { x.UserId, x.SessionId, x.CreatedAtUtc });

            modelBuilder.Entity<SmiTaskTableItem>()
                .HasIndex(x => new { x.QualificationScopeKey, x.Status, x.SortOrder });

            modelBuilder.Entity<SmiTaskTableItem>()
                .HasIndex(x => new { x.QualificationScopeKey, x.TaskKey })
                .IsUnique();
        }
    }
}
