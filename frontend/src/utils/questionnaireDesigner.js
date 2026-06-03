export const OPTION_KEYS = ['A', 'B', 'C', 'D', 'E'];

export const DEFAULT_SHARED_STORAGE_KEY = 'etdp:questionnaire:designer:shared:v1';

const asInt = (value, fallback = 0) => {
  const n = Number(value);
  return Number.isFinite(n) ? Math.trunc(n) : fallback;
};

const asText = (value) => String(value ?? '').trim();

export const makeId = (prefix = 'qid') =>
  `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 9)}`;

export const formatAcNumber = (value) => {
  const raw = asText(value);
  if (!raw) return '';
  if (/^ac-\d+$/i.test(raw)) return raw.toUpperCase();
  const numeric = Number(raw);
  if (Number.isFinite(numeric) && numeric > 0) return `AC-${Math.trunc(numeric)}`;
  return raw;
};

export const normalizeQualification = (row) => ({
  id: asInt(row?.id ?? row?.Id, 0),
  qualificationNumber: asText(row?.qualificationNumber ?? row?.QualificationNumber),
  qualificationDescription: asText(row?.qualificationDescription ?? row?.QualificationDescription),
  learningInstitutionName: asText(row?.learningInstitutionName ?? row?.LearningInstitutionName),
  nqfLevel: asText(row?.nqfLevel ?? row?.NqfLevel),
  credits: asText(row?.credits ?? row?.Credits)
});

export const normalizeTopic = (row) => {
  const acNumberRaw = row?.assessmentCriteriaNumber
    ?? row?.AssessmentCriteriaNumber
    ?? row?.assessmentCriteriaId
    ?? row?.AssessmentCriteriaId
    ?? '';

  return {
    id: asInt(row?.id ?? row?.Id, 0),
    subjectId: asInt(row?.subjectId ?? row?.SubjectId, 0),
    subjectCode: asText(row?.subjectCode ?? row?.SubjectCode),
    topicCode: asText(row?.topicCode ?? row?.TopicCode),
    topicDescription: asText(row?.topicDescription ?? row?.TopicDescription),
    assessmentCriteriaNumber: formatAcNumber(acNumberRaw),
    assessmentCriteriaDescription: asText(row?.assessmentCriteriaDescription ?? row?.AssessmentCriteriaDescription)
  };
};

export const normalizeAssessmentCriteria = (row) => ({
  id: asInt(row?.id ?? row?.Id, 0),
  topicId: asInt(row?.topicId ?? row?.TopicId, 0),
  description: asText(row?.description ?? row?.Description),
  criteriaType: asText(row?.criteriaType ?? row?.CriteriaType),
  weight: Number(row?.weight ?? row?.Weight ?? 0) || 0
});

export const normalizeLessonPlan = (row) => ({
  id: asInt(row?.id ?? row?.Id, 0),
  assessmentCriteriaId: asInt(row?.assessmentCriteriaId ?? row?.AssessmentCriteriaId, 0),
  title: asText(row?.title ?? row?.Title),
  sortOrder: asInt(row?.sortOrder ?? row?.SortOrder, 0),
  date: row?.date ?? row?.Date ?? null,
  durationMinutes: asInt(row?.durationMinutes ?? row?.DurationMinutes, 0),
  content: String(row?.content ?? row?.Content ?? '')
});

export const normalizeToolkitEntry = (row) => ({
  id: asInt(row?.id ?? row?.Id, 0),
  qualificationsId: asInt(row?.qualificationsId ?? row?.QualificationsId, 0),
  subjectCode: asText(row?.subjectCode ?? row?.SubjectCode),
  assessmentCriteriaId: asInt(row?.assessmentCriteriaId ?? row?.AssessmentCriteriaId, 0),
  assessmentCriteriaDescription: asText(row?.assessmentCriteriaDescription ?? row?.AssessmentCriteriaDescription),
  lpn: asText(row?.lpn ?? row?.Lpn),
  lessonPlanDescription: asText(row?.lessonPlanDescription ?? row?.LessonPlanDescription),
  lessonPlanContent: String(row?.lessonPlanContent ?? row?.LessonPlanContent ?? '')
});

export const buildLessonContentCandidates = ({
  qualificationId,
  criteriaRows,
  lessonPlans,
  toolkitEntries
}) => {
  const qid = asInt(qualificationId, 0);
  const criteria = Array.isArray(criteriaRows) ? criteriaRows.map(normalizeAssessmentCriteria) : [];
  const criteriaById = new Map(criteria.filter((c) => c.id > 0).map((c) => [c.id, c]));
  const criteriaIds = new Set(criteriaById.keys());

  const lessonPlanRows = Array.isArray(lessonPlans) ? lessonPlans.map(normalizeLessonPlan) : [];
  const toolkitRows = Array.isArray(toolkitEntries) ? toolkitEntries.map(normalizeToolkitEntry) : [];
  const results = [];

  for (const row of toolkitRows) {
    if (qid > 0 && row.qualificationsId !== qid) continue;
    if (!criteriaIds.has(row.assessmentCriteriaId)) continue;
    const criterion = criteriaById.get(row.assessmentCriteriaId);
    results.push({
      refId: `toolkit-${row.id}`,
      source: 'lecturer-toolkit',
      sourceId: row.id,
      assessmentCriteriaId: row.assessmentCriteriaId,
      assessmentCriteriaDescription: row.assessmentCriteriaDescription || criterion?.description || '',
      label: [row.lpn || 'LPN', row.lessonPlanDescription || 'Lesson Plan'].join(' - '),
      content: String(row.lessonPlanContent || ''),
      hasContent: String(row.lessonPlanContent || '').trim().length > 0
    });
  }

  for (const row of lessonPlanRows) {
    if (!criteriaIds.has(row.assessmentCriteriaId)) continue;
    const criterion = criteriaById.get(row.assessmentCriteriaId);
    results.push({
      refId: `lessonplan-${row.id}`,
      source: 'lesson-plan',
      sourceId: row.id,
      assessmentCriteriaId: row.assessmentCriteriaId,
      assessmentCriteriaDescription: criterion?.description || '',
      label: `LPN ${row.sortOrder || row.id} - ${row.title || 'Lesson Plan'}`,
      content: String(row.content || ''),
      hasContent: String(row.content || '').trim().length > 0
    });
  }

  const dedup = new Map();
  for (const row of results) {
    const key = `${row.source}:${row.sourceId}`;
    if (!dedup.has(key)) dedup.set(key, row);
  }

  return Array.from(dedup.values()).sort((a, b) => {
    const contentDelta = Number(b.hasContent) - Number(a.hasContent);
    if (contentDelta !== 0) return contentDelta;
    const sourceRankA = a.source === 'lecturer-toolkit' ? 0 : 1;
    const sourceRankB = b.source === 'lecturer-toolkit' ? 0 : 1;
    if (sourceRankA !== sourceRankB) return sourceRankA - sourceRankB;
    return String(a.label).localeCompare(String(b.label), undefined, { sensitivity: 'base' });
  });
};

export const createLessonBinding = (candidate = null, existingContent = '') => {
  if (!candidate) {
    return {
      lessonPlanRefId: '',
      lessonPlanSource: '',
      lessonPlanId: 0,
      lessonPlanLabel: '',
      lessonPlanContent: String(existingContent ?? ''),
      lessonPlanAssessmentCriteriaId: 0
    };
  }

  return {
    lessonPlanRefId: asText(candidate.refId),
    lessonPlanSource: asText(candidate.source),
    lessonPlanId: asInt(candidate.sourceId, 0),
    lessonPlanLabel: asText(candidate.label),
    lessonPlanContent: String(candidate.content ?? existingContent ?? ''),
    lessonPlanAssessmentCriteriaId: asInt(candidate.assessmentCriteriaId, 0)
  };
};

export const mergeLessonBindingIntoQuestion = (question, candidate) => {
  const current = question || {};
  const binding = createLessonBinding(candidate, current.lessonPlanContent);
  return {
    ...current,
    ...binding,
    assessmentCriteriaDescription: asText(current.assessmentCriteriaDescription) || asText(candidate?.assessmentCriteriaDescription)
  };
};

export const normalizeOptionList = (options) => {
  const source = Array.isArray(options) ? options : [];
  return OPTION_KEYS.map((key, index) => {
    const row = source[index] || {};
    return {
      key,
      text: asText(row?.text),
      imageUrl: asText(row?.imageUrl)
    };
  });
};

export const createQuestionSeed = (topic = null, source = 'manual') => ({
  id: makeId('question'),
  source,
  topicId: asInt(topic?.id, 0),
  subjectCode: asText(topic?.subjectCode),
  topicCode: asText(topic?.topicCode),
  topicDescription: asText(topic?.topicDescription),
  assessmentCriteriaNumber: asText(topic?.assessmentCriteriaNumber),
  assessmentCriteriaDescription: asText(topic?.assessmentCriteriaDescription),
  lessonPlanRefId: '',
  lessonPlanSource: '',
  lessonPlanId: 0,
  lessonPlanLabel: '',
  lessonPlanContent: '',
  lessonPlanAssessmentCriteriaId: 0,
  prompt: '',
  imageUrl: '',
  options: normalizeOptionList([]),
  correctOption: 'A',
  explanation: ''
});

export const shuffleList = (items) => {
  const arr = Array.isArray(items) ? [...items] : [];
  for (let i = arr.length - 1; i > 0; i -= 1) {
    const j = Math.floor(Math.random() * (i + 1));
    const temp = arr[i];
    arr[i] = arr[j];
    arr[j] = temp;
  }
  return arr;
};

export const parseFreezletText = (rawText) => {
  const lines = String(rawText || '').split(/\r?\n/);
  const rows = [];
  for (const line of lines) {
    const cleaned = line.trim();
    if (!cleaned || cleaned.startsWith('#')) continue;
    const separator = cleaned.indexOf('|');
    if (separator <= 0) continue;
    const question = cleaned.slice(0, separator).trim();
    const answer = cleaned.slice(separator + 1).trim();
    if (!question || !answer) continue;
    rows.push({ question, answer });
  }
  return rows;
};

export const toFreezletText = (questions) => {
  const rows = Array.isArray(questions) ? questions : [];
  return rows
    .map((q) => {
      const option = Array.isArray(q?.options)
        ? q.options.find((o) => String(o?.key || '').toUpperCase() === String(q?.correctOption || '').toUpperCase())
        : null;
      const answer = asText(option?.text);
      const prompt = asText(q?.prompt);
      return prompt && answer ? `${prompt} | ${answer}` : '';
    })
    .filter(Boolean)
    .join('\n');
};

export const downloadTextFile = (filename, content, mimeType = 'text/plain;charset=utf-8') => {
  const blob = new Blob([String(content ?? '')], { type: mimeType });
  downloadBlobFile(filename, blob);
};

export const downloadBlobFile = (filename, blob) => {
  if (!blob) return;

  if (window.navigator && typeof window.navigator.msSaveOrOpenBlob === 'function') {
    window.navigator.msSaveOrOpenBlob(blob, filename);
    return;
  }

  const href = window.URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = href;
  anchor.download = filename;
  anchor.rel = 'noopener';
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();

  window.setTimeout(() => {
    anchor.remove();
    window.URL.revokeObjectURL(href);
  }, 60_000);
};

export const buildFreezletChoiceOptions = (answer, allAnswers) => {
  const correct = asText(answer);
  const pool = Array.from(
    new Set(
      (Array.isArray(allAnswers) ? allAnswers : [])
        .map((item) => asText(item))
        .filter(Boolean)
        .filter((item) => item.toLowerCase() !== correct.toLowerCase())
    )
  );
  const distractors = shuffleList(pool).slice(0, 4);
  while (distractors.length < 4) {
    distractors.push(`Distractor ${distractors.length + 1}`);
  }
  const combined = shuffleList([correct, ...distractors]).slice(0, 5);
  const options = OPTION_KEYS.map((key, idx) => ({ key, text: combined[idx] || '', imageUrl: '' }));
  const correctOption = options.find((opt) => asText(opt.text).toLowerCase() === correct.toLowerCase())?.key || 'A';
  return { options, correctOption };
};

export const loadJsonFromStorage = (storageKey, fallback) => {
  try {
    const raw = localStorage.getItem(String(storageKey || ''));
    if (!raw) return fallback;
    return JSON.parse(raw);
  } catch {
    return fallback;
  }
};

export const saveJsonToStorage = (storageKey, payload) => {
  try {
    localStorage.setItem(String(storageKey || ''), JSON.stringify(payload));
  } catch {
    // ignore localStorage quota or serialization failures
  }
};

export const buildQualificationLabel = (qualification) => {
  if (!qualification) return '';
  const code = asText(qualification.qualificationNumber);
  const description = asText(qualification.qualificationDescription);
  return [code, description].filter(Boolean).join(' - ');
};
