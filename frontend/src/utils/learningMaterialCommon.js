import { normalizeLearningMaterialParams, readLearningMaterialParams } from './learningMaterialParams';

export const API = '/api';

const AUDIT_STORAGE_KEY = 'learningMaterial.auditedSubjects.v1';
const SUBJECT_FETCH_CACHE = new Map();

const readLearningMaterialAudit = () => {
  try {
    const raw = localStorage.getItem(AUDIT_STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
};

const writeLearningMaterialAudit = (value) => {
  try {
    localStorage.setItem(AUDIT_STORAGE_KEY, JSON.stringify(value || {}));
  } catch {
    // ignore storage errors
  }
};

export const subjectIdOf = (subject) => String(subject?.id ?? subject?.Id ?? '').trim();
export const subjectCodeOf = (subject) =>
  String(subject?.subjectCode ?? subject?.SubjectCode ?? subject?.code ?? subject?.Code ?? '').trim();
export const subjectDescriptionOf = (subject) =>
  String(subject?.subjectDescription ?? subject?.SubjectDescription ?? '').trim();

const normalizeSubjectList = (subjects) => {
  if (!Array.isArray(subjects)) return [];
  return [...subjects].sort((a, b) => {
    const codeA = (subjectCodeOf(a) || '').toLowerCase();
    const codeB = (subjectCodeOf(b) || '').toLowerCase();
    if (codeA !== codeB) return codeA.localeCompare(codeB, undefined, { sensitivity: 'base', numeric: true });
    const descA = (subjectDescriptionOf(a) || '').toLowerCase();
    const descB = (subjectDescriptionOf(b) || '').toLowerCase();
    return descA.localeCompare(descB, undefined, { sensitivity: 'base', numeric: true });
  });
};

export const fetchQualificationSubjects = async (qualificationId) => {
  const qid = Number(qualificationId || 0);
  if (qid <= 0) return [];
  if (SUBJECT_FETCH_CACHE.has(qid)) {
    return SUBJECT_FETCH_CACHE.get(qid);
  }
  const res = await fetch(`${API}/Subject/byQualification?qualificationId=${qid}`, {
    cache: 'no-store'
  });
  if (!res.ok) {
    throw new Error(await res.text() || `Failed to load subjects (${res.status})`);
  }
  const data = await res.json();
  const subjects = normalizeSubjectList(Array.isArray(data) ? data : []);
  SUBJECT_FETCH_CACHE.set(qid, subjects);
  return subjects;
};

export const getSubjectRangeIds = (subjects, fromId, toId) => {
  if (!Array.isArray(subjects) || subjects.length === 0) return [];
  const normalizedSubjects = normalizeSubjectList(subjects);
  const from = String(fromId || '').trim();
  const to = String(toId || '').trim();
  let fromIndex = 0;
  let toIndex = normalizedSubjects.length - 1;
  if (from) {
    const idx = normalizedSubjects.findIndex((s) => subjectIdOf(s) === from);
    if (idx >= 0) fromIndex = idx;
  }
  if (to) {
    const idx = normalizedSubjects.findIndex((s) => subjectIdOf(s) === to);
    if (idx >= 0) toIndex = idx;
  }
  if (fromIndex > toIndex) [fromIndex, toIndex] = [toIndex, fromIndex];
  return normalizedSubjects.slice(fromIndex, toIndex + 1).map((s) => subjectIdOf(s)).filter(Boolean);
};

export const getSubjectRangeSubjects = (subjects, fromId, toId) => {
  const ids = getSubjectRangeIds(subjects, fromId, toId);
  if (ids.length === 0) return [];
  const normalizedSubjects = normalizeSubjectList(subjects);
  const map = new Map(normalizedSubjects.map((s) => [subjectIdOf(s), s]));
  return ids.map((id) => map.get(id)).filter(Boolean);
};

export const getAuditedSubjectIds = (qualificationId) => {
  const qid = String(Number(qualificationId || 0));
  if (!qid || qid === '0') return new Set();
  const state = readLearningMaterialAudit();
  const list = Array.isArray(state[qid]) ? state[qid] : [];
  return new Set(list.filter((item) => typeof item === 'string' && item.trim().length > 0));
};

const setAuditedSubjectIds = (qualificationId, ids) => {
  const qid = String(Number(qualificationId || 0));
  if (!qid || qid === '0') return;
  const state = readLearningMaterialAudit();
  state[qid] = Array.from(new Set((ids || []).map((id) => String(id || '').trim()).filter(Boolean)));
  writeLearningMaterialAudit(state);
};

export const toggleAuditedSubject = (qualificationId, subjectId, reviewed) => {
  const current = Array.from(getAuditedSubjectIds(qualificationId));
  const set = new Set(current);
  const normalized = String(subjectId || '').trim();
  if (!normalized) return Array.from(set);
  if (reviewed) {
    set.add(normalized);
  } else {
    set.delete(normalized);
  }
  setAuditedSubjectIds(qualificationId, Array.from(set));
  return Array.from(set);
};

export const markAuditedSubjects = (qualificationId, subjectIds, reviewed = true) => {
  let set = getAuditedSubjectIds(qualificationId);
  const normalized = (subjectIds || []).map((id) => String(id || '').trim()).filter(Boolean);
  if (reviewed) {
    normalized.forEach((id) => set.add(id));
  } else {
    normalized.forEach((id) => set.delete(id));
  }
  setAuditedSubjectIds(qualificationId, Array.from(set));
  return Array.from(set);
};

export const areSubjectIdsAudited = (qualificationId, subjectIds) => {
  if (!subjectIds || subjectIds.length === 0) return false;
  const set = getAuditedSubjectIds(qualificationId);
  return subjectIds.every((id) => set.has(String(id || '').trim()));
};

export const ensureSubjectRangeAudited = async ({ qualificationId, subjectFromId, subjectToId }) => {
  const qid = Number(qualificationId || 0);
  if (qid <= 0) throw new Error('Select a qualification before exporting learning materials.');
  const subjects = await fetchQualificationSubjects(qid);
  const rangeIds = getSubjectRangeIds(subjects, subjectFromId, subjectToId);
  if (rangeIds.length === 0) throw new Error('Select a subject range before exporting learning materials.');
  if (!areSubjectIdsAudited(qid, rangeIds)) {
    throw new Error('Please confirm each selected subject is audited on the dashboard before saving exports.');
  }
  return rangeIds;
};

export const getLearningMaterialParamsWithFallback = (qualificationId) => {
  const stored = normalizeLearningMaterialParams(readLearningMaterialParams());
  const fallbackQid = Number(qualificationId || localStorage.getItem('qualificationId') || 0);
  return {
    ...stored,
    qualificationId: String(stored.qualificationId || (fallbackQid > 0 ? fallbackQid : ''))
  };
};

export const ensureQualificationId = (params, qualificationId) => {
  const fromParams = Number(params?.qualificationId || 0);
  const fromContext = Number(qualificationId || 0);
  const fromStorage = Number(localStorage.getItem('qualificationId') || 0);
  return fromParams > 0 ? fromParams : (fromContext > 0 ? fromContext : fromStorage);
};

export const openUrl = (url) => {
  window.open(url, '_blank');
};

export const downloadBlobResponse = async (res, fallbackName) => {
  const blob = await res.blob();
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  const dispo = String(res.headers.get('Content-Disposition') || '');
  const match = dispo.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i);
  link.download = match?.[1] ? decodeURIComponent(match[1].replace(/\"/g, '').trim()) : fallbackName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
};

export const buildSubjectRange = (params) => {
  const fromId = String(params?.subjectFromId || '').trim();
  const toId = String(params?.subjectToId || params?.subjectFromId || '').trim();
  if (!fromId || !toId) return null;
  return { fromId, toId };
};
