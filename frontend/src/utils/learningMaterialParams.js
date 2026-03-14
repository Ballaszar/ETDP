const STORAGE_KEY = 'learningMaterial.parameters.v1';

const toText = (value) => String(value ?? '');

export const readLearningMaterialParams = () => {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
};

export const writeLearningMaterialParams = (params) => {
  const next = params && typeof params === 'object' ? params : {};
  localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  return next;
};

export const mergeLearningMaterialParams = (partial) => {
  const current = readLearningMaterialParams();
  const next = { ...current, ...(partial || {}) };
  writeLearningMaterialParams(next);
  return next;
};

export const normalizeLearningMaterialParams = (params) => {
  const p = params && typeof params === 'object' ? params : {};
  return {
    qualificationId: toText(p.qualificationId),
    qualificationLabel: toText(p.qualificationLabel),
    dateFrom: toText(p.dateFrom),
    dateTo: toText(p.dateTo),
    subjectFromId: toText(p.subjectFromId),
    subjectToId: toText(p.subjectToId),
    subjectFromCode: toText(p.subjectFromCode),
    subjectToCode: toText(p.subjectToCode),
    topicId: toText(p.topicId),
    topicCode: toText(p.topicCode),
    maxActivities: Number(p.maxActivities || 30)
  };
};

