import { downloadTextFile, normalizeQualification } from './questionnaireDesigner';

const DEFAULT_BLOOM_DOMAIN = 'Cognitive';
const DEFAULT_BLOOM_TARGET_LEVEL = 'Understand';
const DEFAULT_REVIEW_STATUS = 'pending_smi_review';
const MINIMUM_QUESTIONS_PER_CRITERION = 2;
const MINIMUM_TOTAL_QUESTIONS = 100;
const QUALIFIER_SOURCE_DETECTED = 'detected_from_criterion';
const QUALIFIER_SOURCE_LESSON_PLAN_FALLBACK = 'lesson_plan_fallback';
const QUALIFIER_SOURCE_SMI_REQUIRED = 'smi_required';
const FOUNDATION_TOPIC_PATTERN = /^KG/i;
const INTERNAL_ASSESSMENT_TOPIC_PATTERN = /internal assessment criteria/i;
const KNOWN_MAIN_CATEGORY_LABELS = {
  'KM-01': 'Basic Engineering',
  'KM-02': 'Fitting Theory',
  'KM-03': 'Machining Theory'
};

const REMEMBER_VERBS = new Set([
  'identify',
  'recognize',
  'recognise',
  'locate',
  'list',
  'name',
  'state',
  'select',
  'match',
  'define',
  'label',
  'recall',
  'remember'
]);

const UNDERSTAND_DESCRIBE_VERBS = new Set([
  'describe',
  'outline',
  'detail',
  'summarize',
  'summarise',
  'illustrate',
  'arrange',
  'convert',
  'demonstrate'
]);

const UNDERSTAND_EXPLAIN_VERBS = new Set([
  'explain',
  'clarify',
  'interpret',
  'discuss',
  'restate',
  'translate',
  'paraphrase',
  'generalize',
  'generalise',
  'compare'
]);

export const KQ_V1_METADATA_COLUMNS = [
  'Qualification Code',
  'Phase Code',
  'Phase Description',
  'Total Subjects',
  'Total Topics',
  'Total Criteria',
  'Minimum Questions Per Criterion',
  'Questionnaire Title',
  'Bloom Domain',
  'Bloom Target Level',
  'Total Questions',
  'True/False Count',
  'Multiple Choice Count',
  'Total Marks',
  'Pass Mark',
  'Created By',
  'Reviewed By',
  'Notes'
];

export const KQ_V1_QUESTION_COLUMNS = [
  'Subject Code',
  'Subject Description',
  'Topic Code',
  'Topic Description',
  'Assessment Criteria Number',
  'Original Criterion Text',
  'Noun Focus',
  'Detected Verb',
  'Canonical Verb',
  'Bloom Domain',
  'Bloom Level',
  'Qualifier',
  'Qualifier Source',
  'Coverage Type',
  'Routing Status',
  'Lesson Plan Label',
  'Question ID',
  'Question Type',
  'Question Text',
  'Option A',
  'Option B',
  'Option C',
  'Option D',
  'Correct Answer',
  'Mark',
  'Memo / Model Answer',
  'SMI Reviewer Status'
];

const asText = (value) => String(value ?? '').trim();

const asInt = (value, fallback = 0) => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? Math.max(0, Math.trunc(parsed)) : fallback;
};

const sanitizeFilePart = (value, fallback) => {
  const cleaned = asText(value).replace(/[^A-Za-z0-9_-]+/g, '_').replace(/_+/g, '_').replace(/^_+|_+$/g, '');
  return cleaned || fallback;
};

const escapeCsvCell = (value) => {
  const text = String(value ?? '');
  if (/[",\r\n]/.test(text)) {
    return `"${text.replace(/"/g, '""')}"`;
  }
  return text;
};

const buildCsv = (columns, rows) => {
  const header = columns.map(escapeCsvCell).join(',');
  const body = rows.map((row) => columns.map((column) => escapeCsvCell(row?.[column] ?? '')).join(','));
  return [header, ...body].join('\r\n');
};

const normalizeCoverageType = (value) => {
  const normalized = asText(value).toLowerCase();
  return normalized === 'direct' ? 'direct' : 'proxy';
};

const normalizeRoutingStatus = (value) => {
  const normalized = asText(value).toLowerCase();
  return normalized === 'kq' ? 'KQ' : 'Other Assessment';
};

const normalizeBloomLevel = (value) => {
  const normalized = asText(value).toLowerCase();
  if (normalized === 'remember') return 'Remember';
  if (normalized === 'understand') return 'Understand';
  if (normalized === 'apply') return 'Apply';
  if (normalized === 'analyze' || normalized === 'analyse') return 'Analyze';
  if (normalized === 'evaluate') return 'Evaluate';
  return '';
};

const normalizeCanonicalVerb = (value) => asText(value).toLowerCase();
const normalizeTextKey = (value) => asText(value).replace(/\s+/g, ' ').toLowerCase();
const sortByText = (left, right) => asText(left).localeCompare(asText(right), undefined, { sensitivity: 'base' });

export const deriveMainCategoryCode = (subjectCode) => {
  const parts = asText(subjectCode).split('-').filter(Boolean);
  if (parts.length >= 2) {
    return `${parts[0]}-${parts[1]}`.toUpperCase();
  }
  return asText(subjectCode).toUpperCase() || 'UNCATEGORIZED';
};

export const deriveMainCategoryLabel = (categoryCode) => {
  const normalizedCode = asText(categoryCode).toUpperCase();
  if (KNOWN_MAIN_CATEGORY_LABELS[normalizedCode]) {
    return KNOWN_MAIN_CATEGORY_LABELS[normalizedCode];
  }
  const suffix = normalizedCode.split('-')[1];
  return suffix ? `Main Category ${suffix}` : 'Main Category';
};

export const isStandingExamExcludedTopic = (topicCode, topicDescription) =>
  FOUNDATION_TOPIC_PATTERN.test(asText(topicCode)) || INTERNAL_ASSESSMENT_TOPIC_PATTERN.test(asText(topicDescription));

const compareCategoryCode = (left, right) => {
  const a = asText(left).toUpperCase();
  const b = asText(right).toUpperCase();
  const aParts = a.split('-');
  const bParts = b.split('-');
  if (aParts.length >= 2 && bParts.length >= 2 && aParts[0] === bParts[0]) {
    const aNum = Number(aParts[1]);
    const bNum = Number(bParts[1]);
    if (Number.isFinite(aNum) && Number.isFinite(bNum) && aNum !== bNum) {
      return aNum - bNum;
    }
  }
  return a.localeCompare(b, undefined, { sensitivity: 'base' });
};

export const normalizePhaseOption = (row) => ({
  id: asInt(row?.id ?? row?.Id, 0),
  qualificationPhaseId: asInt(row?.qualificationPhaseId ?? row?.QualificationPhaseId, 0),
  name: asText(row?.name ?? row?.Name),
  description: asText(row?.description ?? row?.Description),
  sequence: asInt(row?.sequence ?? row?.Sequence, 0)
});

const normalizeSubjectScope = (row) => ({
  subjectId: asInt(row?.subjectId ?? row?.SubjectId, 0),
  subjectCode: asText(row?.subjectCode ?? row?.SubjectCode),
  subjectDescription: asText(row?.subjectDescription ?? row?.SubjectDescription)
});

const normalizeTopicScope = (row) => ({
  topicId: asInt(row?.topicId ?? row?.TopicId, 0),
  subjectId: asInt(row?.subjectId ?? row?.SubjectId, 0),
  subjectCode: asText(row?.subjectCode ?? row?.SubjectCode),
  topicCode: asText(row?.topicCode ?? row?.TopicCode),
  topicDescription: asText(row?.topicDescription ?? row?.TopicDescription)
});

export const getVerbRule = (canonicalVerb, detectedBloomLevel = '') => {
  const canonical = normalizeCanonicalVerb(canonicalVerb);
  if (REMEMBER_VERBS.has(canonical)) {
    return { bloomLevel: 'Remember', coverageType: 'direct', routingStatus: 'KQ' };
  }
  if (UNDERSTAND_DESCRIBE_VERBS.has(canonical) || UNDERSTAND_EXPLAIN_VERBS.has(canonical)) {
    return { bloomLevel: 'Understand', coverageType: 'proxy', routingStatus: 'KQ' };
  }

  const fallback = normalizeBloomLevel(detectedBloomLevel);
  if (fallback === 'Remember') {
    return { bloomLevel: 'Remember', coverageType: 'direct', routingStatus: 'KQ' };
  }
  if (fallback === 'Understand') {
    return { bloomLevel: 'Understand', coverageType: 'proxy', routingStatus: 'KQ' };
  }
  return { bloomLevel: fallback, coverageType: 'proxy', routingStatus: 'Other Assessment' };
};

export const applyCanonicalVerbRule = (row, nextCanonicalVerb) => {
  const canonicalVerb = normalizeCanonicalVerb(nextCanonicalVerb);
  const rule = getVerbRule(canonicalVerb, row?.bloomLevel);
  return {
    ...row,
    canonicalVerb,
    bloomLevel: rule.bloomLevel,
    coverageType: normalizeCoverageType(rule.coverageType),
    routingStatus: normalizeRoutingStatus(rule.routingStatus)
  };
};

const normalizeMetadata = (row, draft) => {
  const defaultMinimumQuestionsPerCriterion = asInt(
    row?.minimumQuestionsPerCriterion ?? row?.MinimumQuestionsPerCriterion,
    MINIMUM_QUESTIONS_PER_CRITERION
  );
  const rawMinimumTotalQuestions = asInt(
    row?.minimumTotalQuestions ?? row?.MinimumTotalQuestions,
    0
  );
  const defaultMinimumTotalQuestions = rawMinimumTotalQuestions > 0
    ? rawMinimumTotalQuestions
    : MINIMUM_TOTAL_QUESTIONS;

  let trueFalseCount = asInt(row?.trueFalseCount ?? row?.TrueFalseCount, 0);
  let multipleChoiceCount = asInt(row?.multipleChoiceCount ?? row?.MultipleChoiceCount, 0);
  if ((trueFalseCount + multipleChoiceCount) === 0) {
    trueFalseCount = Math.floor(defaultMinimumTotalQuestions / 2);
    multipleChoiceCount = defaultMinimumTotalQuestions - trueFalseCount;
  }

  const totalQuestions = trueFalseCount + multipleChoiceCount;
  return {
    questionnaireTitle: asText(row?.questionnaireTitle ?? row?.QuestionnaireTitle)
      || ['Knowledge Questionnaire', asText(draft?.phaseCode), asText(draft?.phaseDescription)].filter(Boolean).join(' - '),
    bloomDomain: asText(row?.bloomDomain ?? row?.BloomDomain) || DEFAULT_BLOOM_DOMAIN,
    bloomTargetLevel: asText(row?.bloomTargetLevel ?? row?.BloomTargetLevel) || DEFAULT_BLOOM_TARGET_LEVEL,
    minimumQuestionsPerCriterion: defaultMinimumQuestionsPerCriterion,
    minimumTotalQuestions: defaultMinimumTotalQuestions,
    totalQuestions,
    trueFalseCount,
    multipleChoiceCount,
    totalMarks: totalQuestions,
    passMark: asText(row?.passMark ?? row?.PassMark),
    createdBy: asText(row?.createdBy ?? row?.CreatedBy),
    reviewedBy: asText(row?.reviewedBy ?? row?.ReviewedBy),
    notes: asText(row?.notes ?? row?.Notes)
  };
};

export const normalizeCriterionIntent = (row) => {
  const canonicalVerb = normalizeCanonicalVerb(row?.canonicalVerb ?? row?.CanonicalVerb);
  const rule = getVerbRule(canonicalVerb, row?.bloomLevel ?? row?.BloomLevel);
  const qualifier = asText(row?.qualifier ?? row?.Qualifier);
  const routingStatus = normalizeRoutingStatus(row?.routingStatus ?? row?.RoutingStatus ?? rule.routingStatus);
  let qualifierSource = asText(row?.qualifierSource ?? row?.QualifierSource).toLowerCase();
  if (!qualifierSource) {
    qualifierSource = routingStatus === 'KQ' ? QUALIFIER_SOURCE_LESSON_PLAN_FALLBACK : QUALIFIER_SOURCE_SMI_REQUIRED;
  } else if (!qualifier && routingStatus === 'KQ' && qualifierSource === QUALIFIER_SOURCE_SMI_REQUIRED) {
    qualifierSource = QUALIFIER_SOURCE_LESSON_PLAN_FALLBACK;
  }

  return {
    intentId: asText(row?.intentId ?? row?.IntentId),
    assessmentCriteriaId: asInt(row?.assessmentCriteriaId ?? row?.AssessmentCriteriaId, 0),
    assessmentCriteriaNumber: asText(row?.assessmentCriteriaNumber ?? row?.AssessmentCriteriaNumber),
    subjectId: asInt(row?.subjectId ?? row?.SubjectId, 0),
    subjectCode: asText(row?.subjectCode ?? row?.SubjectCode),
    subjectDescription: asText(row?.subjectDescription ?? row?.SubjectDescription),
    topicId: asInt(row?.topicId ?? row?.TopicId, 0),
    topicCode: asText(row?.topicCode ?? row?.TopicCode),
    topicDescription: asText(row?.topicDescription ?? row?.TopicDescription),
    originalCriterionText: asText(row?.originalCriterionText ?? row?.OriginalCriterionText),
    nounFocus: asText(row?.nounFocus ?? row?.NounFocus),
    detectedVerb: asText(row?.detectedVerb ?? row?.DetectedVerb),
    canonicalVerb,
    bloomDomain: asText(row?.bloomDomain ?? row?.BloomDomain) || DEFAULT_BLOOM_DOMAIN,
    bloomLevel: normalizeBloomLevel(row?.bloomLevel ?? row?.BloomLevel) || rule.bloomLevel,
    qualifier,
    qualifierSource,
    coverageType: normalizeCoverageType(row?.coverageType ?? row?.CoverageType ?? rule.coverageType),
    routingStatus
  };
};

export const normalizeGeneratedQuestion = (row) => ({
  number: asInt(row?.number ?? row?.Number, 0),
  type: asText(row?.type ?? row?.Type),
  prompt: asText(row?.prompt ?? row?.Prompt),
  options: Array.isArray(row?.options ?? row?.Options) ? (row?.options ?? row?.Options).map(asText).filter(Boolean) : [],
  correctAnswer: asText(row?.correctAnswer ?? row?.CorrectAnswer),
  subjectCode: asText(row?.subjectCode ?? row?.SubjectCode),
  subjectDescription: asText(row?.subjectDescription ?? row?.SubjectDescription),
  topicCode: asText(row?.topicCode ?? row?.TopicCode),
  topicDescription: asText(row?.topicDescription ?? row?.TopicDescription),
  assessmentCriteriaNumber: asText(row?.assessmentCriteriaNumber ?? row?.AssessmentCriteriaNumber),
  lessonPlanLabel: asText(row?.lessonPlanLabel ?? row?.LessonPlanLabel),
  assessmentCriteriaDescription: asText(row?.assessmentCriteriaDescription ?? row?.AssessmentCriteriaDescription),
  rationale: asText(row?.rationale ?? row?.Rationale),
  marks: asInt(row?.marks ?? row?.Marks, 1) || 1
});

export const normalizeCategoryMetadata = (row, fallback) => {
  const baseline = fallback ?? {};
  const minimumQuestionsPerCriterion = asInt(
    row?.minimumQuestionsPerCriterion ?? baseline.minimumQuestionsPerCriterion,
    asInt(baseline.minimumQuestionsPerCriterion, MINIMUM_QUESTIONS_PER_CRITERION)
  );
  const minimumTotalQuestions = asInt(
    row?.minimumTotalQuestions ?? baseline.minimumTotalQuestions,
    asInt(baseline.minimumTotalQuestions, 0)
  );

  let trueFalseCount = asInt(
    row?.trueFalseCount ?? baseline.trueFalseCount,
    asInt(baseline.trueFalseCount, 0)
  );
  let multipleChoiceCount = asInt(
    row?.multipleChoiceCount ?? baseline.multipleChoiceCount,
    asInt(baseline.multipleChoiceCount, 0)
  );

  if ((trueFalseCount + multipleChoiceCount) === 0 && minimumTotalQuestions > 0) {
    trueFalseCount = Math.floor(minimumTotalQuestions / 2);
    multipleChoiceCount = minimumTotalQuestions - trueFalseCount;
  }

  const totalQuestions = trueFalseCount + multipleChoiceCount;
  return {
    questionnaireTitle: asText(row?.questionnaireTitle ?? baseline.questionnaireTitle),
    bloomDomain: asText(row?.bloomDomain ?? baseline.bloomDomain) || DEFAULT_BLOOM_DOMAIN,
    bloomTargetLevel: asText(row?.bloomTargetLevel ?? baseline.bloomTargetLevel) || DEFAULT_BLOOM_TARGET_LEVEL,
    minimumQuestionsPerCriterion,
    minimumTotalQuestions,
    totalQuestions,
    trueFalseCount,
    multipleChoiceCount,
    totalMarks: totalQuestions,
    passMark: asText(row?.passMark ?? baseline.passMark),
    createdBy: asText(row?.createdBy ?? baseline.createdBy),
    reviewedBy: asText(row?.reviewedBy ?? baseline.reviewedBy),
    notes: asText(row?.notes ?? baseline.notes)
  };
};

export const buildCategoryPlans = (draft) => {
  if (!draft) return [];

  const subjectMap = new Map();
  for (const subject of Array.isArray(draft.subjects) ? draft.subjects : []) {
    subjectMap.set(subject.subjectId, subject);
  }

  const categories = new Map();
  const ensureCategory = (subjectCode) => {
    const categoryCode = deriveMainCategoryCode(subjectCode);
    if (!categories.has(categoryCode)) {
      categories.set(categoryCode, {
        key: categoryCode,
        code: categoryCode,
        label: deriveMainCategoryLabel(categoryCode),
        subjectMap: new Map(),
        allTopicMap: new Map(),
        assessableTopicMap: new Map(),
        criterionGroups: new Map()
      });
    }
    return categories.get(categoryCode);
  };

  for (const subject of Array.isArray(draft.subjects) ? draft.subjects : []) {
    const category = ensureCategory(subject.subjectCode);
    category.subjectMap.set(subject.subjectId, subject);
  }

  for (const topic of Array.isArray(draft.topics) ? draft.topics : []) {
    const category = ensureCategory(topic.subjectCode);
    const topicKey = normalizeTextKey(topic.topicCode);
    if (!category.allTopicMap.has(topicKey)) {
      category.allTopicMap.set(topicKey, topic);
    }
  }

  const criteriaRows = Array.isArray(draft.criteria) ? draft.criteria.slice().sort(compareIntentRows) : [];
  for (const row of criteriaRows) {
    const category = ensureCategory(row.subjectCode);
    const criteriaTextKey = normalizeTextKey(row.originalCriterionText);
    if (row.routingStatus !== 'KQ' || !criteriaTextKey) {
      continue;
    }
    if (isStandingExamExcludedTopic(row.topicCode, row.topicDescription)) {
      continue;
    }

    const groupKey = `${normalizeTextKey(row.subjectCode)}|${criteriaTextKey}`;
    if (!category.criterionGroups.has(groupKey)) {
      category.criterionGroups.set(groupKey, {
        key: groupKey,
        originalCriterionText: row.originalCriterionText,
        assessmentCriteriaRowsById: new Map(),
        topicMap: new Map(),
        subjectIds: new Set()
      });
    }

    const group = category.criterionGroups.get(groupKey);
    const criteriaIdKey = row.assessmentCriteriaId > 0 ? String(row.assessmentCriteriaId) : row.intentId;
    if (!group.assessmentCriteriaRowsById.has(criteriaIdKey)) {
      group.assessmentCriteriaRowsById.set(criteriaIdKey, []);
    }
    group.assessmentCriteriaRowsById.get(criteriaIdKey).push(row);
    group.subjectIds.add(row.subjectId);

    const topicKey = normalizeTextKey(row.topicCode);
    if (!group.topicMap.has(topicKey)) {
      group.topicMap.set(topicKey, {
        topicId: row.topicId,
        subjectId: row.subjectId,
        subjectCode: row.subjectCode,
        topicCode: row.topicCode,
        topicDescription: row.topicDescription
      });
    }
  }

  return Array.from(categories.values())
    .map((category) => {
      const criterionGroups = Array.from(category.criterionGroups.values())
        .map((group) => {
          const orderedAssessmentIds = Array.from(group.assessmentCriteriaRowsById.keys())
            .sort((left, right) => {
              const a = Number(left);
              const b = Number(right);
              if (Number.isFinite(a) && Number.isFinite(b) && a !== b) {
                return a - b;
              }
              return sortByText(left, right);
            });
          const representativeAssessmentCriteriaId = orderedAssessmentIds[0] ?? '';
          const criteria = (group.assessmentCriteriaRowsById.get(representativeAssessmentCriteriaId) ?? [])
            .slice()
            .sort(compareIntentRows);
          const representative = criteria[0] ?? null;
          for (const topic of group.topicMap.values()) {
            const topicKey = normalizeTextKey(topic.topicCode);
            if (!category.assessableTopicMap.has(topicKey)) {
              category.assessableTopicMap.set(topicKey, topic);
            }
          }
          return {
            key: group.key,
            representativeAssessmentCriteriaId: asInt(representative?.assessmentCriteriaId, 0),
            assessmentCriteriaNumber: asText(representative?.assessmentCriteriaNumber),
            originalCriterionText: asText(group.originalCriterionText),
            representative,
            criteria
          };
        })
        .filter((group) => group.criteria.length > 0)
        .sort((left, right) => {
          const bySubject = sortByText(left.representative?.subjectCode, right.representative?.subjectCode);
          if (bySubject !== 0) return bySubject;
          const byTopic = sortByText(left.representative?.topicCode, right.representative?.topicCode);
          if (byTopic !== 0) return byTopic;
          return sortByText(left.assessmentCriteriaNumber, right.assessmentCriteriaNumber);
        });

      const criteria = criterionGroups.flatMap((group) => group.criteria);
      const subjects = Array.from(category.subjectMap.values())
        .sort((left, right) => sortByText(left.subjectCode, right.subjectCode));
      const topics = Array.from(category.assessableTopicMap.values())
        .sort((left, right) => sortByText(left.topicCode, right.topicCode));
      const defaultMinimumQuestionsPerCriterion = MINIMUM_QUESTIONS_PER_CRITERION;
      const defaultTotalQuestions = criterionGroups.length * defaultMinimumQuestionsPerCriterion;
      const defaultMetadata = normalizeCategoryMetadata({
        questionnaireTitle: ['Knowledge Questionnaire', category.label].filter(Boolean).join(' - '),
        bloomDomain: DEFAULT_BLOOM_DOMAIN,
        bloomTargetLevel: DEFAULT_BLOOM_TARGET_LEVEL,
        minimumQuestionsPerCriterion: defaultMinimumQuestionsPerCriterion,
        minimumTotalQuestions: defaultTotalQuestions,
        trueFalseCount: Math.floor(defaultTotalQuestions / 2),
        multipleChoiceCount: defaultTotalQuestions - Math.floor(defaultTotalQuestions / 2),
        passMark: '',
        createdBy: '',
        reviewedBy: '',
        notes: ''
      });

      return {
        key: category.key,
        code: category.code,
        label: category.label,
        subjects,
        subjectIds: subjects.map((subject) => subject.subjectId).filter((id) => id > 0),
        topics,
        assessmentCriteriaIds: criterionGroups
          .map((group) => group.representativeAssessmentCriteriaId)
          .filter((id) => id > 0),
        criterionGroups,
        criteria,
        stats: {
          totalSubjects: subjects.length,
          totalTopics: topics.length,
          totalCriteria: criterionGroups.length,
          totalIntents: criteria.length,
          kqIntentCount: criteria.filter((row) => row.routingStatus === 'KQ').length,
          kqCriteriaCount: criterionGroups.length,
          otherAssessmentIntentCount: criteria.filter((row) => row.routingStatus !== 'KQ').length
        },
        defaultMetadata
      };
    })
    .filter((category) => category.stats.totalSubjects > 0)
    .sort((left, right) => compareCategoryCode(left.code, right.code));
};

export const buildPhaseExamSummary = (draft, categoryPlans) => {
  const plans = Array.isArray(categoryPlans) ? categoryPlans : [];
  const totalSubjects = new Set((Array.isArray(draft?.subjects) ? draft.subjects : []).map((row) => row.subjectId).filter((id) => id > 0)).size;
  const totalTopics = new Set((Array.isArray(draft?.topics) ? draft.topics : []).map((row) => normalizeTextKey(row.topicCode)).filter(Boolean)).size;
  return {
    totalSubjects,
    totalTopics,
    totalCriteria: plans.reduce((sum, plan) => sum + asInt(plan?.stats?.totalCriteria, 0), 0),
    totalMainCategories: plans.length,
    defaultTotalQuestions: plans.reduce((sum, plan) => sum + asInt(plan?.defaultMetadata?.totalQuestions, 0), 0)
  };
};

export const buildScopedDraft = (draft, categoryPlan, metadataOverride) => {
  if (!draft || !categoryPlan) return null;
  const metadata = normalizeCategoryMetadata(metadataOverride, categoryPlan.defaultMetadata);
  return {
    ...draft,
    subjectId: 0,
    subjectCode: categoryPlan.code,
    subjectDescription: categoryPlan.label,
    topicId: 0,
    topicCode: categoryPlan.code,
    topicDescription: categoryPlan.label,
    subjects: categoryPlan.subjects,
    topics: categoryPlan.topics,
    metadata,
    criteria: categoryPlan.criteria,
    stats: {
      ...categoryPlan.stats
    }
  };
};

export const normalizeDraft = (raw) => {
  const qualification = normalizeQualification(raw || {});
  const metadata = normalizeMetadata(raw?.metadata ?? raw?.Metadata ?? {}, raw || {});
  const criteria = Array.isArray(raw?.criteria ?? raw?.Criteria)
    ? (raw?.criteria ?? raw?.Criteria).map(normalizeCriterionIntent)
    : [];
  const subjects = Array.isArray(raw?.subjects ?? raw?.Subjects)
    ? (raw?.subjects ?? raw?.Subjects).map(normalizeSubjectScope)
    : [];
  const topics = Array.isArray(raw?.topics ?? raw?.Topics)
    ? (raw?.topics ?? raw?.Topics).map(normalizeTopicScope)
    : [];

  return {
    qualificationId: qualification.id,
    qualificationCode: asText(raw?.qualificationCode ?? raw?.QualificationCode ?? qualification.qualificationNumber),
    qualificationDescription: asText(raw?.qualificationDescription ?? raw?.QualificationDescription ?? qualification.qualificationDescription),
    phaseId: asInt(raw?.phaseId ?? raw?.PhaseId, 0),
    phaseCode: asText(raw?.phaseCode ?? raw?.PhaseCode),
    phaseDescription: asText(raw?.phaseDescription ?? raw?.PhaseDescription),
    subjectId: asInt(raw?.subjectId ?? raw?.SubjectId, 0),
    subjectCode: asText(raw?.subjectCode ?? raw?.SubjectCode),
    subjectDescription: asText(raw?.subjectDescription ?? raw?.SubjectDescription),
    topicId: asInt(raw?.topicId ?? raw?.TopicId, 0),
    topicCode: asText(raw?.topicCode ?? raw?.TopicCode),
    topicDescription: asText(raw?.topicDescription ?? raw?.TopicDescription),
    subjects,
    topics,
    metadata,
    criteria,
    stats: {
      totalSubjects: asInt(raw?.stats?.totalSubjects ?? raw?.Stats?.TotalSubjects, subjects.length),
      totalTopics: asInt(raw?.stats?.totalTopics ?? raw?.Stats?.TotalTopics, topics.length),
      totalCriteria: asInt(raw?.stats?.totalCriteria ?? raw?.Stats?.TotalCriteria, new Set(criteria.map((row) => row.assessmentCriteriaId).filter((id) => id > 0)).size),
      totalIntents: asInt(raw?.stats?.totalIntents ?? raw?.Stats?.TotalIntents, criteria.length),
      kqIntentCount: asInt(raw?.stats?.kqIntentCount ?? raw?.Stats?.KqIntentCount, criteria.filter((row) => row.routingStatus === 'KQ').length),
      kqCriteriaCount: asInt(raw?.stats?.kqCriteriaCount ?? raw?.Stats?.KqCriteriaCount, new Set(criteria.filter((row) => row.routingStatus === 'KQ').map((row) => row.assessmentCriteriaId).filter((id) => id > 0)).size),
      otherAssessmentIntentCount: asInt(raw?.stats?.otherAssessmentIntentCount ?? raw?.Stats?.OtherAssessmentIntentCount, criteria.filter((row) => row.routingStatus !== 'KQ').length)
    },
    warnings: Array.isArray(raw?.warnings ?? raw?.Warnings) ? (raw?.warnings ?? raw?.Warnings).map(asText).filter(Boolean) : []
  };
};

export const mergeDraftState = (draft, savedState) => {
  if (!draft) return null;
  if (!savedState) return draft;

  const savedMetadata = normalizeMetadata(savedState?.metadata ?? {}, draft);
  const savedCriteria = Array.isArray(savedState?.criteria) ? savedState.criteria.map(normalizeCriterionIntent) : [];
  const overrides = new Map(savedCriteria.map((row) => [row.intentId, row]));
  const hasSavedMinimumQuestionsPerCriterion = Object.prototype.hasOwnProperty.call(savedState?.metadata ?? {}, 'minimumQuestionsPerCriterion');
  const hasSavedMinimumTotalQuestions = Object.prototype.hasOwnProperty.call(savedState?.metadata ?? {}, 'minimumTotalQuestions');

  return {
    ...draft,
    metadata: {
      ...draft.metadata,
      ...savedMetadata,
      minimumQuestionsPerCriterion: hasSavedMinimumQuestionsPerCriterion
        ? savedMetadata.minimumQuestionsPerCriterion
        : (draft.metadata.minimumQuestionsPerCriterion || MINIMUM_QUESTIONS_PER_CRITERION),
      minimumTotalQuestions: hasSavedMinimumTotalQuestions
        ? savedMetadata.minimumTotalQuestions
        : (draft.metadata.minimumTotalQuestions || MINIMUM_TOTAL_QUESTIONS)
    },
    criteria: draft.criteria.map((row) => {
      const override = overrides.get(row.intentId);
      return override ? { ...row, ...override } : row;
    })
  };
};

export const buildStorageKey = (qualificationId, phaseId) =>
  `etdp:kq:v1:${asInt(qualificationId, 0) || 'none'}:${asInt(phaseId, 0) || 'none'}`;

export const buildDraftStats = (draft) => {
  const criteria = Array.isArray(draft?.criteria) ? draft.criteria : [];
  const subjects = Array.isArray(draft?.subjects) ? draft.subjects : [];
  const topics = Array.isArray(draft?.topics) ? draft.topics : [];
  const routedRows = criteria.filter((row) => row.routingStatus === 'KQ');
  return {
    totalSubjects: subjects.length,
    totalTopics: topics.length,
    totalCriteria: new Set(criteria.map((row) => row.assessmentCriteriaId).filter((id) => id > 0)).size,
    totalIntents: criteria.length,
    kqIntentCount: routedRows.length,
    kqCriteriaCount: new Set(routedRows.map((row) => row.assessmentCriteriaId).filter((id) => id > 0)).size,
    otherAssessmentIntentCount: criteria.length - routedRows.length
  };
};

export const buildValidation = (draft) => {
  const errors = [];
  const warnings = [];
  const metadata = draft?.metadata ?? {};
  const criteria = Array.isArray(draft?.criteria) ? draft.criteria : [];
  const stats = buildDraftStats(draft);
  const minimumQuestionsPerCriterion = Math.max(0, asInt(metadata.minimumQuestionsPerCriterion, MINIMUM_QUESTIONS_PER_CRITERION));
  const formulaSuggestedTotalQuestions = stats.kqCriteriaCount * minimumQuestionsPerCriterion;
  const minimumRequired = Math.max(0, asInt(metadata.minimumTotalQuestions, 0));

  if (asInt(draft?.phaseId, 0) <= 0) {
    errors.push('Select a curriculum phase before exporting CSV files or generating with SMI.');
  }
  if (metadata.trueFalseCount + metadata.multipleChoiceCount !== metadata.totalQuestions) {
    errors.push('Total questions must equal True/False count plus Multiple Choice count.');
  }
  if (metadata.totalMarks !== metadata.totalQuestions) {
    errors.push('Total marks must equal total questions because each question carries exactly 1 mark.');
  }
  if (metadata.totalQuestions < minimumRequired) {
    errors.push(`Total questions must be at least ${minimumRequired} for the selected exam scope.`);
  }
  if (metadata.totalQuestions > 0 && stats.kqCriteriaCount === 0) {
    errors.push('No criteria are routed to KQ. Set at least one criterion intent to routing status "KQ" before exporting or generating.');
  }
  if (criteria.some((row) => !asText(row.assessmentCriteriaNumber))) {
    warnings.push('One or more criterion rows are missing an assessment criteria number and should be confirmed by SMI.');
  }
  if (criteria.some((row) => row.routingStatus === 'KQ' && !asText(row.qualifier) && asText(row.qualifierSource) === QUALIFIER_SOURCE_SMI_REQUIRED)) {
    warnings.push('Some KQ-routed rows still require an SMI-added qualifier.');
  }
  if (formulaSuggestedTotalQuestions > 0 && metadata.totalQuestions < formulaSuggestedTotalQuestions) {
    warnings.push(`Manual override is below the formula suggestion of ${formulaSuggestedTotalQuestions} question(s). Coverage will be partial and SMI should confirm the final distribution.`);
  }
  if (formulaSuggestedTotalQuestions > 0 && metadata.totalQuestions > formulaSuggestedTotalQuestions) {
    warnings.push('Question allocation will cycle through criteria after the configured minimum-per-criterion coverage has been reached.');
  }

  return { errors, warnings, stats, minimumRequired, formulaSuggestedTotalQuestions, minimumQuestionsPerCriterion };
};

const compareIntentRows = (left, right) => {
  const leftRank = left?.coverageType === 'direct' ? 0 : 1;
  const rightRank = right?.coverageType === 'direct' ? 0 : 1;
  if (leftRank !== rightRank) return leftRank - rightRank;
  const byCriteria = asText(left?.assessmentCriteriaNumber).localeCompare(asText(right?.assessmentCriteriaNumber), undefined, { sensitivity: 'base' });
  if (byCriteria !== 0) return byCriteria;
  return asText(left?.canonicalVerb).localeCompare(asText(right?.canonicalVerb), undefined, { sensitivity: 'base' });
};

const buildQuestionTypePlan = (trueFalseCount, multipleChoiceCount) => {
  let remainingTrueFalse = asInt(trueFalseCount, 0);
  let remainingMultipleChoice = asInt(multipleChoiceCount, 0);
  const result = [];
  let preferTrueFalse = remainingTrueFalse >= remainingMultipleChoice;

  while (remainingTrueFalse > 0 || remainingMultipleChoice > 0) {
    if ((preferTrueFalse && remainingTrueFalse > 0) || remainingMultipleChoice === 0) {
      result.push('True/False');
      remainingTrueFalse -= 1;
    } else if (remainingMultipleChoice > 0) {
      result.push('Multiple Choice');
      remainingMultipleChoice -= 1;
    }
    preferTrueFalse = !preferTrueFalse;
  }

  return result;
};

const buildCriterionGroups = (criteria) => {
  const routed = (Array.isArray(criteria) ? criteria : [])
    .filter((row) => row.routingStatus === 'KQ')
    .sort(compareIntentRows);

  const groups = new Map();
  for (const row of routed) {
    const key = row.assessmentCriteriaId > 0 ? `ac-${row.assessmentCriteriaId}` : `intent-${row.intentId}`;
    if (!groups.has(key)) {
      groups.set(key, { key, intents: [] });
    }
    groups.get(key).intents.push(row);
  }

  return Array.from(groups.values()).map((group) => ({
    ...group,
    representative: group.intents[0]
  }));
};

const buildAssignments = (groups, trueFalseCount, multipleChoiceCount, minimumQuestionsPerCriterion) => {
  const plan = buildQuestionTypePlan(trueFalseCount, multipleChoiceCount);
  const assignments = [];
  let planIndex = 0;
  let questionNumber = 1;
  const typeOccurrences = new Map();
  const minimumPerCriterion = Math.max(0, asInt(minimumQuestionsPerCriterion, MINIMUM_QUESTIONS_PER_CRITERION));

  for (const group of groups) {
    for (let i = 0; i < minimumPerCriterion && planIndex < plan.length; i += 1) {
      const questionType = plan[planIndex];
      const occurrenceKey = `${group.key}:${questionType}`;
      const occurrence = typeOccurrences.get(occurrenceKey) || 0;
      typeOccurrences.set(occurrenceKey, occurrence + 1);
      assignments.push({ group, questionType, questionNumber, occurrence });
      planIndex += 1;
      questionNumber += 1;
    }
  }

  let roundRobinIndex = 0;
  while (planIndex < plan.length && groups.length > 0) {
    const group = groups[roundRobinIndex % groups.length];
    const questionType = plan[planIndex];
    const occurrenceKey = `${group.key}:${questionType}`;
    const occurrence = typeOccurrences.get(occurrenceKey) || 0;
    typeOccurrences.set(occurrenceKey, occurrence + 1);
    assignments.push({ group, questionType, questionNumber, occurrence });
    planIndex += 1;
    questionNumber += 1;
    roundRobinIndex += 1;
  }

  return assignments;
};

const buildQuestionScaffold = (questionType) => {
  if (questionType === 'True/False') {
    return {
      optionA: 'True',
      optionB: 'False',
      optionC: '',
      optionD: '',
      correctAnswer: ''
    };
  }
  return {
    optionA: '',
    optionB: '',
    optionC: '',
    optionD: '',
    correctAnswer: ''
  };
};

const buildQuestionId = (draft, questionNumber) => {
  const phaseCode = sanitizeFilePart(draft?.phaseCode, 'PHASE');
  const scopeCode = sanitizeFilePart(draft?.subjectCode && draft?.subjectCode !== 'MULTI' ? draft.subjectCode : '', '');
  const prefix = [phaseCode, scopeCode].filter(Boolean).join('-');
  return `${prefix || 'PHASE'}-Q${String(questionNumber).padStart(3, '0')}`;
};

const normalizeQuestionTypeLabel = (value) => {
  const normalized = asText(value).toLowerCase();
  return normalized === 'truefalse' || normalized === 'true/false' ? 'True/False' : 'Multiple Choice';
};

const buildGeneratedQuestionRow = (draft, criteriaLookup, question) => {
  const lookupKey = asText(question.assessmentCriteriaNumber).toLowerCase();
  const criterion = criteriaLookup.get(lookupKey) || null;
  const options = Array.from({ length: 4 }, (_, index) => asText(question.options[index]));
  return {
    'Subject Code': asText(question.subjectCode || criterion?.subjectCode),
    'Subject Description': asText(question.subjectDescription || criterion?.subjectDescription),
    'Topic Code': asText(question.topicCode || criterion?.topicCode),
    'Topic Description': asText(question.topicDescription || criterion?.topicDescription),
    'Assessment Criteria Number': asText(question.assessmentCriteriaNumber || criterion?.assessmentCriteriaNumber),
    'Original Criterion Text': asText(criterion?.originalCriterionText || question.assessmentCriteriaDescription),
    'Noun Focus': asText(criterion?.nounFocus),
    'Detected Verb': asText(criterion?.detectedVerb),
    'Canonical Verb': asText(criterion?.canonicalVerb),
    'Bloom Domain': asText(criterion?.bloomDomain || DEFAULT_BLOOM_DOMAIN),
    'Bloom Level': asText(criterion?.bloomLevel),
    'Qualifier': asText(criterion?.qualifier),
    'Qualifier Source': asText(criterion?.qualifierSource),
    'Coverage Type': normalizeCoverageType(criterion?.coverageType),
    'Routing Status': normalizeRoutingStatus(criterion?.routingStatus || 'KQ'),
    'Lesson Plan Label': asText(question.lessonPlanLabel),
    'Question ID': buildQuestionId(draft, question.number || 0),
    'Question Type': normalizeQuestionTypeLabel(question.type),
    'Question Text': asText(question.prompt),
    'Option A': options[0],
    'Option B': options[1],
    'Option C': options[2],
    'Option D': options[3],
    'Correct Answer': asText(question.correctAnswer),
    'Mark': asInt(question.marks, 1) || 1,
    'Memo / Model Answer': asText(question.rationale),
    'SMI Reviewer Status': DEFAULT_REVIEW_STATUS
  };
};

export const buildMetadataRows = (draft) => {
  const metadata = draft?.metadata ?? {};
  const stats = draft?.stats ?? buildDraftStats(draft);
  return [
    {
      'Qualification Code': asText(draft?.qualificationCode),
      'Phase Code': asText(draft?.phaseCode),
      'Phase Description': asText(draft?.phaseDescription),
      'Total Subjects': asInt(stats.totalSubjects, 0),
      'Total Topics': asInt(stats.totalTopics, 0),
      'Total Criteria': asInt(stats.totalCriteria, 0),
      'Minimum Questions Per Criterion': asInt(metadata.minimumQuestionsPerCriterion, MINIMUM_QUESTIONS_PER_CRITERION),
      'Questionnaire Title': asText(metadata.questionnaireTitle),
      'Bloom Domain': asText(metadata.bloomDomain) || DEFAULT_BLOOM_DOMAIN,
      'Bloom Target Level': asText(metadata.bloomTargetLevel) || DEFAULT_BLOOM_TARGET_LEVEL,
      'Total Questions': asInt(metadata.totalQuestions, 0),
      'True/False Count': asInt(metadata.trueFalseCount, 0),
      'Multiple Choice Count': asInt(metadata.multipleChoiceCount, 0),
      'Total Marks': asInt(metadata.totalMarks, 0),
      'Pass Mark': asText(metadata.passMark),
      'Created By': asText(metadata.createdBy),
      'Reviewed By': asText(metadata.reviewedBy),
      'Notes': asText(metadata.notes)
    }
  ];
};

export const buildQuestionRows = (draft, generatedQuestions = []) => {
  const normalizedQuestions = Array.isArray(generatedQuestions) ? generatedQuestions.map(normalizeGeneratedQuestion) : [];
  const criteria = Array.isArray(draft?.criteria) ? draft.criteria : [];
  const criteriaLookup = new Map(
    criteria
      .filter((row) => row.routingStatus === 'KQ')
      .map((row) => [asText(row.assessmentCriteriaNumber).toLowerCase(), row])
  );

  if (normalizedQuestions.length > 0) {
    return normalizedQuestions.map((question) => buildGeneratedQuestionRow(draft, criteriaLookup, question));
  }

  const metadata = draft?.metadata ?? {};
  const groups = buildCriterionGroups(criteria);
  if (groups.length === 0) {
    return [];
  }

  const assignments = buildAssignments(groups, metadata.trueFalseCount, metadata.multipleChoiceCount, metadata.minimumQuestionsPerCriterion);
  return assignments.map(({ group, questionType, questionNumber, occurrence }) => {
    const intent = group.intents[occurrence % group.intents.length] || group.representative;
    const scaffold = buildQuestionScaffold(questionType);
    return {
      'Subject Code': asText(intent.subjectCode),
      'Subject Description': asText(intent.subjectDescription),
      'Topic Code': asText(intent.topicCode),
      'Topic Description': asText(intent.topicDescription),
      'Assessment Criteria Number': asText(intent.assessmentCriteriaNumber),
      'Original Criterion Text': asText(intent.originalCriterionText),
      'Noun Focus': asText(intent.nounFocus),
      'Detected Verb': asText(intent.detectedVerb),
      'Canonical Verb': asText(intent.canonicalVerb),
      'Bloom Domain': asText(intent.bloomDomain) || DEFAULT_BLOOM_DOMAIN,
      'Bloom Level': asText(intent.bloomLevel),
      'Qualifier': asText(intent.qualifier),
      'Qualifier Source': asText(intent.qualifierSource),
      'Coverage Type': normalizeCoverageType(intent.coverageType),
      'Routing Status': normalizeRoutingStatus(intent.routingStatus),
      'Lesson Plan Label': '',
      'Question ID': buildQuestionId(draft, questionNumber),
      'Question Type': questionType,
      'Question Text': '',
      'Option A': scaffold.optionA,
      'Option B': scaffold.optionB,
      'Option C': scaffold.optionC,
      'Option D': scaffold.optionD,
      'Correct Answer': scaffold.correctAnswer,
      'Mark': 1,
      'Memo / Model Answer': '',
      'SMI Reviewer Status': DEFAULT_REVIEW_STATUS
    };
  });
};

export const buildMetadataCsv = (draft) => buildCsv(KQ_V1_METADATA_COLUMNS, buildMetadataRows(draft));

export const buildQuestionRowsCsv = (draft, generatedQuestions = []) => buildCsv(KQ_V1_QUESTION_COLUMNS, buildQuestionRows(draft, generatedQuestions));

export const buildFileStem = (draft) => {
  const qualificationCode = sanitizeFilePart(draft?.qualificationCode, 'Qualification');
  const phaseCode = sanitizeFilePart(draft?.phaseCode, 'Phase');
  const scopeCode = sanitizeFilePart(draft?.subjectCode && draft?.subjectCode !== 'MULTI' ? draft.subjectCode : '', '');
  return `KQ_v1_${[qualificationCode, phaseCode, scopeCode].filter(Boolean).join('_')}`;
};

export const downloadMetadataCsv = (draft) => {
  const fileName = `${buildFileStem(draft)}_Metadata.csv`;
  downloadTextFile(fileName, buildMetadataCsv(draft), 'text/csv;charset=utf-8');
  return fileName;
};

export const downloadQuestionRowsCsv = (draft, generatedQuestions = []) => {
  const suffix = Array.isArray(generatedQuestions) && generatedQuestions.length > 0 ? 'GeneratedQuestions' : 'Questions';
  const fileName = `${buildFileStem(draft)}_${suffix}.csv`;
  downloadTextFile(fileName, buildQuestionRowsCsv(draft, generatedQuestions), 'text/csv;charset=utf-8');
  return fileName;
};
