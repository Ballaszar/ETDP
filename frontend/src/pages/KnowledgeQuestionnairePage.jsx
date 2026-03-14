import React, { useEffect, useMemo, useState } from 'react';
import DocxPreviewModal from '../components/DocxPreviewModal';
import { useQualification } from '../context/QualificationContext';
import { fetchDocxPreview } from '../utils/docxPreview';
import { downloadBlobFile, normalizeQualification } from '../utils/questionnaireDesigner';
import {
  applyCanonicalVerbRule,
  buildCategoryPlans,
  buildFileStem,
  buildPhaseExamSummary,
  buildScopedDraft,
  buildStorageKey,
  buildValidation,
  downloadMetadataCsv,
  downloadQuestionRowsCsv,
  mergeDraftState,
  normalizeDraft,
  normalizeCategoryMetadata,
  normalizeGeneratedQuestion,
  normalizePhaseOption
} from '../utils/knowledgeQuestionnaireV1';

const API = '/api';
const DEFAULT_VERB_OPTIONS = ['identify', 'describe', 'explain'];

const asText = (value) => String(value ?? '').trim();

const asInt = (value, fallback = 0) => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? Math.max(0, Math.trunc(parsed)) : fallback;
};

const loadStoredState = (storageKey) => {
  try {
    const raw = localStorage.getItem(storageKey);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
};

const saveStoredState = (storageKey, payload) => {
  try {
    localStorage.setItem(storageKey, JSON.stringify(payload));
  } catch {
    // Ignore storage failures.
  }
};

const normalizeStoredGeneratedByCategory = (value) => {
  if (!value || typeof value !== 'object') return {};

  const next = {};
  for (const [key, entry] of Object.entries(value)) {
    if (!asText(key)) continue;
    next[key] = {
      questions: Array.isArray(entry?.questions)
        ? entry.questions.map(normalizeGeneratedQuestion).filter((row) => asText(row.prompt))
        : [],
      smiSource: asText(entry?.smiSource),
      smiResources: Array.isArray(entry?.smiResources) ? entry.smiResources.map(asText).filter(Boolean) : []
    };
  }

  return next;
};

const parseErrorText = async (response) => {
  const text = await response.text();
  return asText(text) || `Request failed with status ${response.status}.`;
};

const parseDownloadFileName = (response, fallbackName) => {
  const header = response.headers.get('content-disposition') || '';
  const utfMatch = header.match(/filename\*=UTF-8''([^;]+)/i);
  if (utfMatch?.[1]) {
    try {
      return decodeURIComponent(utfMatch[1]);
    } catch {
      return utfMatch[1];
    }
  }
  const plainMatch = header.match(/filename=\"?([^\";]+)\"?/i);
  return asText(plainMatch?.[1]) || fallbackName;
};

const normalizePromptKey = (value) => asText(value).toLowerCase().replace(/\s+/g, ' ');

const buildGeneratedQuestionReview = (rows, minimumQuestionsPerCriterion) => {
  const list = Array.isArray(rows) ? rows : [];
  const minRequired = asInt(minimumQuestionsPerCriterion, 0);
  const groups = new Map();

  for (const row of list) {
    const key = [
      asText(row.assessmentCriteriaNumber) || 'UNNUMBERED',
      asText(row.subjectCode),
      asText(row.topicCode)
    ].join('|');

    if (!groups.has(key)) {
      groups.set(key, {
        key,
        assessmentCriteriaNumber: asText(row.assessmentCriteriaNumber) || 'Unnumbered criterion',
        rows: [],
        prompts: []
      });
    }

    const entry = groups.get(key);
    entry.rows.push(row);
    entry.prompts.push(normalizePromptKey(row.prompt));
  }

  const warnings = [];
  for (const entry of groups.values()) {
    const promptSet = new Set(entry.prompts.filter(Boolean));
    if (minRequired > 0 && entry.rows.length < minRequired) {
      warnings.push(`${entry.assessmentCriteriaNumber} only generated ${entry.rows.length} question(s); expected at least ${minRequired}.`);
    }
    if (entry.rows.length > 1 && promptSet.size < entry.rows.length) {
      warnings.push(`${entry.assessmentCriteriaNumber} contains repeated prompt text. The question pair is not yet distinct enough.`);
    }
  }

  return {
    warnings
  };
};

const buildCategoryConfigState = (plans, savedConfigs) => {
  const next = {};
  for (const plan of Array.isArray(plans) ? plans : []) {
    next[plan.key] = normalizeCategoryMetadata(savedConfigs?.[plan.key] ?? {}, plan.defaultMetadata);
  }
  return next;
};

export default function KnowledgeQuestionnairePage() {
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };
  const [qualifications, setQualifications] = useState([]);
  const [phases, setPhases] = useState([]);
  const [selectedQualificationId, setSelectedQualificationId] = useState(String(qualificationId || localStorage.getItem('qualificationId') || ''));
  const [selectedPhaseId, setSelectedPhaseId] = useState('');
  const [draft, setDraft] = useState(null);
  const [selectedCategoryKey, setSelectedCategoryKey] = useState('');
  const [categoryConfigs, setCategoryConfigs] = useState({});
  const [generatedByCategory, setGeneratedByCategory] = useState({});
  const [generatedQuestions, setGeneratedQuestions] = useState([]);
  const [loadingQualifications, setLoadingQualifications] = useState(false);
  const [loadingPhases, setLoadingPhases] = useState(false);
  const [loadingDraft, setLoadingDraft] = useState(false);
  const [smiBusy, setSmiBusy] = useState(false);
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');
  const [smiSource, setSmiSource] = useState('');
  const [smiResources, setSmiResources] = useState([]);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState('');
  const [previewHtml, setPreviewHtml] = useState('');
  const [previewWarnings, setPreviewWarnings] = useState([]);
  const [previewZoom, setPreviewZoom] = useState(1);
  const [previewTitle, setPreviewTitle] = useState('Knowledge Questionnaire Preview');
  const [previewFileName, setPreviewFileName] = useState('KnowledgeQuestionnaire.docx');

  const qid = asInt(selectedQualificationId, 0);
  const phaseId = asInt(selectedPhaseId, 0);

  const selectedQualification = useMemo(
    () => qualifications.find((row) => row.id === qid) || null,
    [qualifications, qid]
  );

  const selectedPhase = useMemo(
    () => phases.find((row) => row.id === phaseId) || null,
    [phases, phaseId]
  );

  const storageKey = useMemo(
    () => buildStorageKey(qid, phaseId),
    [qid, phaseId]
  );

  const categoryPlans = useMemo(
    () => buildCategoryPlans(draft),
    [draft]
  );

  const selectedCategoryPlan = useMemo(
    () => categoryPlans.find((row) => row.key === selectedCategoryKey) || categoryPlans[0] || null,
    [categoryPlans, selectedCategoryKey]
  );

  const selectedCategoryConfig = useMemo(
    () => (selectedCategoryPlan ? (categoryConfigs[selectedCategoryPlan.key] || selectedCategoryPlan.defaultMetadata) : null),
    [categoryConfigs, selectedCategoryPlan]
  );

  const scopedDraft = useMemo(
    () => buildScopedDraft(draft, selectedCategoryPlan, selectedCategoryConfig),
    [draft, selectedCategoryPlan, selectedCategoryConfig]
  );

  const phaseExamSummary = useMemo(
    () => buildPhaseExamSummary(draft, categoryPlans),
    [draft, categoryPlans]
  );

  const validation = useMemo(
    () => buildValidation(scopedDraft),
    [scopedDraft]
  );

  const combinedWarnings = useMemo(() => {
    const serverWarnings = Array.isArray(draft?.warnings)
      ? draft.warnings.filter((message) => /recovered scope/i.test(message))
      : [];
    return Array.from(new Set([...serverWarnings, ...validation.warnings].filter(Boolean)));
  }, [draft, validation.warnings]);

  const verbOptions = useMemo(() => {
    const dynamic = Array.isArray(scopedDraft?.criteria)
      ? scopedDraft.criteria.map((row) => asText(row.canonicalVerb)).filter(Boolean)
      : [];
    return Array.from(new Set([...DEFAULT_VERB_OPTIONS, ...dynamic])).sort((left, right) =>
      left.localeCompare(right, undefined, { sensitivity: 'base' })
    );
  }, [scopedDraft]);

  const restoreGeneratedForCategory = (categoryKey, sourceMap = generatedByCategory) => {
    const saved = sourceMap?.[categoryKey];
    setGeneratedQuestions(Array.isArray(saved?.questions) ? saved.questions : []);
    setSmiSource(asText(saved?.smiSource));
    setSmiResources(Array.isArray(saved?.smiResources) ? saved.smiResources : []);
    setPreviewOpen(false);
  };

  useEffect(() => {
    let active = true;
    const loadQualifications = async () => {
      setLoadingQualifications(true);
      setError('');
      try {
        const response = await fetch(`${API}/Qualification`);
        if (!response.ok) throw new Error(await parseErrorText(response));
        const data = await response.json();
        if (!active) return;
        const nextQualifications = (Array.isArray(data) ? data : [])
          .map(normalizeQualification)
          .filter((row) => row.id > 0)
          .sort((left, right) => {
            const a = `${left.qualificationNumber} ${left.qualificationDescription}`.toLowerCase();
            const b = `${right.qualificationNumber} ${right.qualificationDescription}`.toLowerCase();
            return a.localeCompare(b);
          });
        setQualifications(nextQualifications);
        if (qid <= 0 && nextQualifications.length > 0) {
          const firstId = String(nextQualifications[0].id);
          setSelectedQualificationId(firstId);
          try { setQualificationId(Number(firstId)); } catch { /* ignore */ }
        }
      } catch (e) {
        if (!active) return;
        setError(`Failed to load qualifications: ${e?.message || e}`);
      } finally {
        if (active) setLoadingQualifications(false);
      }
    };

    loadQualifications();
    return () => { active = false; };
  }, []);

  useEffect(() => {
    let active = true;
    const loadPhases = async () => {
      if (qid <= 0) {
        setPhases([]);
        setSelectedPhaseId('');
        setDraft(null);
        setSelectedCategoryKey('');
        setCategoryConfigs({});
        setGeneratedByCategory({});
        setGeneratedQuestions([]);
        return;
      }

      setLoadingPhases(true);
      setError('');
      try {
        const response = await fetch(`${API}/CurriculumPhase/byQualification?qualificationId=${qid}`);
        if (!response.ok) throw new Error(await parseErrorText(response));
        const data = await response.json();
        if (!active) return;
        const nextPhases = (Array.isArray(data) ? data : [])
          .map(normalizePhaseOption)
          .filter((row) => row.id > 0)
          .sort((left, right) => {
            if (left.sequence !== right.sequence) return left.sequence - right.sequence;
            return `${left.name} ${left.description}`.localeCompare(`${right.name} ${right.description}`, undefined, { sensitivity: 'base' });
          });
        setPhases(nextPhases);
        setSelectedPhaseId((previous) => {
          if (previous && nextPhases.some((row) => String(row.id) === String(previous))) return previous;
          return nextPhases.length > 0 ? String(nextPhases[0].id) : '';
        });
      } catch (e) {
        if (!active) return;
        setPhases([]);
        setSelectedPhaseId('');
        setDraft(null);
        setSelectedCategoryKey('');
        setCategoryConfigs({});
        setGeneratedByCategory({});
        setGeneratedQuestions([]);
        setError(`Failed to load curriculum phases: ${e?.message || e}`);
      } finally {
        if (active) setLoadingPhases(false);
      }
    };

    loadPhases();
    setGeneratedByCategory({});
    setGeneratedQuestions([]);
    setSmiSource('');
    setSmiResources([]);
    if (qid > 0) {
      try { setQualificationId(qid); } catch { /* ignore */ }
      localStorage.setItem('qualificationId', String(qid));
    }
    return () => { active = false; };
  }, [qid, setQualificationId]);

  useEffect(() => {
    let active = true;
    const loadDraft = async () => {
      if (qid <= 0 || phaseId <= 0) {
        setDraft(null);
        setGeneratedByCategory({});
        setGeneratedQuestions([]);
        return;
      }

      setLoadingDraft(true);
      setError('');
      setStatus('');
      try {
        const response = await fetch(`${API}/KnowledgeQuestionnaire/v1-phase-draft?qualificationId=${qid}&phaseId=${phaseId}`);
        if (!response.ok) throw new Error(await parseErrorText(response));
        const raw = await response.json();
        if (!active) return;
        const normalized = normalizeDraft(raw);
        const saved = loadStoredState(buildStorageKey(qid, phaseId));
        const merged = mergeDraftState(normalized, saved);
        const plans = buildCategoryPlans(merged);
        const savedGeneratedByCategory = normalizeStoredGeneratedByCategory(saved?.generatedByCategory);
        const resolvedCategoryKey = (() => {
          if (selectedCategoryKey && plans.some((plan) => plan.key === selectedCategoryKey)) {
            return selectedCategoryKey;
          }
          if (saved?.selectedCategoryKey && plans.some((plan) => plan.key === saved.selectedCategoryKey)) {
            return saved.selectedCategoryKey;
          }
          if (plans[0]?.key) {
            return plans[0].key;
          }
          return '';
        })();
        setDraft(merged);
        setCategoryConfigs(buildCategoryConfigState(plans, saved?.categoryConfigs));
        setGeneratedByCategory(savedGeneratedByCategory);
        setSelectedCategoryKey(resolvedCategoryKey);
        restoreGeneratedForCategory(resolvedCategoryKey, savedGeneratedByCategory);
      } catch (e) {
        if (!active) return;
        setDraft(null);
        setSelectedCategoryKey('');
        setCategoryConfigs({});
        setGeneratedByCategory({});
        setGeneratedQuestions([]);
        setError(`Failed to load consolidated Knowledge Questionnaire draft: ${e?.message || e}`);
      } finally {
        if (active) setLoadingDraft(false);
      }
    };

    loadDraft();
    setGeneratedQuestions([]);
    setSmiSource('');
    setSmiResources([]);
    return () => { active = false; };
  }, [qid, phaseId]);

  useEffect(() => {
    if (!draft) return;
    saveStoredState(storageKey, {
      categoryConfigs,
      selectedCategoryKey,
      criteria: draft.criteria,
      generatedByCategory
    });
  }, [storageKey, draft, categoryConfigs, selectedCategoryKey, generatedByCategory]);

  useEffect(() => {
    restoreGeneratedForCategory(selectedCategoryKey);
    setStatus('');
    setError('');
  }, [selectedCategoryKey]);

  useEffect(() => {
    if (categoryPlans.length === 0) {
      if (selectedCategoryKey) setSelectedCategoryKey('');
      return;
    }
    if (!categoryPlans.some((plan) => plan.key === selectedCategoryKey)) {
      setSelectedCategoryKey(categoryPlans[0].key);
    }
    setCategoryConfigs((current) => buildCategoryConfigState(categoryPlans, current));
  }, [categoryPlans, selectedCategoryKey]);
  const updateMetadata = (field, value) => {
    if (!selectedCategoryPlan) return;
    setCategoryConfigs((current) => {
      const base = current[selectedCategoryPlan.key] || selectedCategoryPlan.defaultMetadata;
      const nextMetadata = { ...base };
      if (field === 'trueFalseCount' || field === 'multipleChoiceCount') {
        nextMetadata[field] = asInt(value, 0);
        nextMetadata.totalQuestions = nextMetadata.trueFalseCount + nextMetadata.multipleChoiceCount;
        nextMetadata.totalMarks = nextMetadata.totalQuestions;
      } else if (field === 'minimumQuestionsPerCriterion') {
        nextMetadata[field] = asInt(value, 0);
        const suggestedTotal = Math.max(0, asInt(selectedCategoryPlan?.stats?.totalCriteria, 0) * nextMetadata.minimumQuestionsPerCriterion);
        nextMetadata.minimumTotalQuestions = suggestedTotal;
        if (nextMetadata.totalQuestions < suggestedTotal) {
          nextMetadata.trueFalseCount = Math.floor(suggestedTotal / 2);
          nextMetadata.multipleChoiceCount = suggestedTotal - nextMetadata.trueFalseCount;
          nextMetadata.totalQuestions = suggestedTotal;
          nextMetadata.totalMarks = suggestedTotal;
        }
      } else if (field === 'minimumTotalQuestions') {
        nextMetadata[field] = asInt(value, 0);
      } else {
        nextMetadata[field] = value;
      }
      return {
        ...current,
        [selectedCategoryPlan.key]: nextMetadata
      };
    });
    setGeneratedQuestions([]);
    setSmiSource('');
    setSmiResources([]);
    setStatus('');
    setError('');
  };

  const updateCriterion = (intentId, field, value) => {
    setDraft((current) => {
      if (!current) return current;
      const nextCriteria = current.criteria.map((row) => {
        if (row.intentId !== intentId) return row;
        if (field === 'canonicalVerb') {
          return applyCanonicalVerbRule(row, value);
        }
        if (field === 'coverageType') {
          return { ...row, coverageType: value === 'direct' ? 'direct' : 'proxy' };
        }
        if (field === 'routingStatus') {
          return { ...row, routingStatus: value === 'KQ' ? 'KQ' : 'Other Assessment' };
        }
        return { ...row, [field]: value };
      });
      return { ...current, criteria: nextCriteria };
    });
    setGeneratedQuestions([]);
    setSmiSource('');
    setSmiResources([]);
    setStatus('');
    setError('');
  };

  const reloadCurrentDraft = async () => {
    if (qid <= 0 || phaseId <= 0) return false;
    setLoadingDraft(true);
    setError('');
    setStatus('');
    try {
      const response = await fetch(`${API}/KnowledgeQuestionnaire/v1-phase-draft?qualificationId=${qid}&phaseId=${phaseId}`);
      if (!response.ok) throw new Error(await parseErrorText(response));
      const raw = await response.json();
      const normalized = normalizeDraft(raw);
      const saved = loadStoredState(buildStorageKey(qid, phaseId));
      const merged = mergeDraftState(normalized, saved);
      const plans = buildCategoryPlans(merged);
      const savedGeneratedByCategory = normalizeStoredGeneratedByCategory(saved?.generatedByCategory);
      const resolvedCategoryKey = (() => {
        if (selectedCategoryKey && plans.some((plan) => plan.key === selectedCategoryKey)) {
          return selectedCategoryKey;
        }
        if (saved?.selectedCategoryKey && plans.some((plan) => plan.key === saved.selectedCategoryKey)) {
          return saved.selectedCategoryKey;
        }
        return plans[0]?.key || '';
      })();
      setDraft(merged);
      setCategoryConfigs(buildCategoryConfigState(plans, saved?.categoryConfigs));
      setGeneratedByCategory(savedGeneratedByCategory);
      setSelectedCategoryKey(resolvedCategoryKey);
      restoreGeneratedForCategory(resolvedCategoryKey, savedGeneratedByCategory);
      return true;
    } catch (e) {
      setError(`Failed to reload draft: ${e?.message || e}`);
      return false;
    } finally {
      setLoadingDraft(false);
    }
  };

  const resetLocalOverrides = () => {
    try {
      localStorage.removeItem(storageKey);
    } catch {
      // Ignore storage failures.
    }
    setGeneratedByCategory({});
    setGeneratedQuestions([]);
    setSmiSource('');
    setSmiResources([]);
    setPreviewOpen(false);
    setError('');
    void (async () => {
      const ok = await reloadCurrentDraft();
      if (ok) {
        setStatus('Removed local overrides for the selected phase and reloaded draft defaults.');
      }
    })();
  };

  const generateWithSmi = async () => {
    if (!scopedDraft || !selectedCategoryPlan) {
      setError('Load the selected main-category draft before generating with SMI.');
      return;
    }
    if (validation.errors.length > 0) {
      setError(validation.errors.join(' '));
      return;
    }

    setSmiBusy(true);
    setError('');
    setStatus('');
    setSmiSource('');
    setSmiResources([]);

    try {
      const payload = {
        qualificationId: qid,
        phaseId,
        subjectIds: selectedCategoryPlan.subjectIds,
        assessmentCriteriaIds: selectedCategoryPlan.assessmentCriteriaIds,
        trueFalseCount: asInt(scopedDraft.metadata.trueFalseCount, 0),
        multipleChoiceCount: asInt(scopedDraft.metadata.multipleChoiceCount, 0),
        minimumQuestionsPerCriterion: asInt(scopedDraft.metadata.minimumQuestionsPerCriterion, 0),
        minimumTotalQuestions: asInt(scopedDraft.metadata.minimumTotalQuestions, 0),
        mcqDistractors: 3
      };

      const response = await fetch(`${API}/KnowledgeQuestionnaire/v1-phase-smi-draft`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!response.ok) throw new Error(await parseErrorText(response));
      const data = await response.json();
      const rows = Array.isArray(data?.questions) ? data.questions.map(normalizeGeneratedQuestion) : [];
      if (rows.length === 0) {
        setError('SMI returned no questionnaire rows for the selected knowledge-learning phase.');
        return;
      }

      const nextSource = asText(data?.questionSource) || `Generated with the ${selectedCategoryPlan.label} main-category workflow.`;
      const nextResources = Array.isArray(data?.learningResourceSuggestions) ? data.learningResourceSuggestions.map(asText).filter(Boolean) : [];
      setGeneratedQuestions(rows);
      setGeneratedByCategory((current) => ({
        ...current,
        [selectedCategoryPlan.key]: {
          questions: rows,
          smiSource: nextSource,
          smiResources: nextResources
        }
      }));
      setSmiSource(nextSource);
      setSmiResources(nextResources);
      setPreviewOpen(false);
      setStatus(`SMI generated ${rows.length} questionnaire row(s) for ${selectedCategoryPlan.label}.`);
    } catch (e) {
      setError(`SMI generation failed: ${e?.message || e}`);
    } finally {
      setSmiBusy(false);
    }
  };

  const clearGeneratedQuestions = () => {
    setGeneratedQuestions([]);
    setGeneratedByCategory((current) => {
      const next = { ...current };
      if (selectedCategoryPlan?.key) {
        delete next[selectedCategoryPlan.key];
      }
      return next;
    });
    setSmiSource('');
    setSmiResources([]);
    setPreviewOpen(false);
    setStatus(`Cleared generated questionnaire rows for ${selectedCategoryPlan?.label || 'the selected category'}.`);
    setError('');
  };

  const saveCurrentState = () => {
    if (!draft) {
      setError('Load a Knowledge Questionnaire draft before saving local state.');
      return;
    }

    saveStoredState(storageKey, {
      categoryConfigs,
      selectedCategoryKey,
      criteria: draft.criteria,
      generatedByCategory
    });
    setError('');
    setStatus(`Saved local Knowledge Questionnaire state for ${selectedCategoryPlan?.label || 'the selected category'}.`);
  };

  const handleDownload = (kind) => {
    if (!scopedDraft) {
      setError('Load a main-category draft before exporting CSV files.');
      return;
    }
    if (validation.errors.length > 0) {
      setError(validation.errors.join(' '));
      setStatus('');
      return;
    }

    setError('');
    if (kind === 'metadata') {
      const fileName = downloadMetadataCsv(scopedDraft);
      setStatus(`Downloaded ${fileName}.`);
      return;
    }
    if (kind === 'questions') {
      const fileName = downloadQuestionRowsCsv(scopedDraft, generatedQuestions);
      setStatus(`Downloaded ${fileName}.`);
      return;
    }

    const metadataFileName = downloadMetadataCsv(scopedDraft);
    const questionFileName = downloadQuestionRowsCsv(scopedDraft, generatedQuestions);
    setStatus(`Downloaded ${metadataFileName} and ${questionFileName}.`);
  };

  const handleDownloadAllCategories = () => {
    if (!draft || categoryPlans.length === 0) {
      setError('Load a knowledge-phase draft before downloading all main-category CSVs.');
      return;
    }

    const fileNames = [];
    for (const plan of categoryPlans) {
      const categoryDraft = buildScopedDraft(draft, plan, categoryConfigs[plan.key] || plan.defaultMetadata);
      if (!categoryDraft) continue;
      const savedQuestions = generatedByCategory[plan.key]?.questions || [];
      fileNames.push(downloadMetadataCsv(categoryDraft));
      fileNames.push(downloadQuestionRowsCsv(categoryDraft, savedQuestions));
    }

    if (fileNames.length === 0) {
      setError('No main-category exports were available.');
      return;
    }

    setError('');
    setStatus(`Downloaded ${fileNames.length} category CSV file(s).`);
  };

  const buildSelectedCategoryDocxPayload = (operationLabel) => {
    if (!scopedDraft || !selectedCategoryPlan) {
      throw new Error(`Load the selected main-category draft before ${operationLabel}.`);
    }
    if (generatedQuestions.length === 0) {
      throw new Error(`Generate the selected category with SMI before ${operationLabel}.`);
    }
    if (validation.errors.length > 0) {
      throw new Error(validation.errors.join(' '));
    }

    return {
      qualificationId: qid,
      phaseId,
      phaseName: asText(selectedPhase?.name),
      phaseDescription: asText(selectedPhase?.description),
      mainCategoryCode: asText(selectedCategoryPlan.code),
      mainCategoryLabel: asText(selectedCategoryPlan.label),
      questionnaireTitle: asText(scopedDraft.metadata.questionnaireTitle),
      passMark: asText(scopedDraft.metadata.passMark),
      createdBy: asText(scopedDraft.metadata.createdBy),
      reviewedBy: asText(scopedDraft.metadata.reviewedBy),
      trueFalseCount: asInt(scopedDraft.metadata.trueFalseCount, 0),
      multipleChoiceCount: asInt(scopedDraft.metadata.multipleChoiceCount, 0),
      totalQuestions: asInt(scopedDraft.metadata.totalQuestions, 0),
      totalMarks: asInt(scopedDraft.metadata.totalMarks, 0),
      questions: generatedQuestions
    };
  };

  const handlePreviewSelectedCategoryDocx = async () => {
    let payload;
    let fallbackName = 'KnowledgeQuestionnaire.docx';

    try {
      payload = buildSelectedCategoryDocxPayload('previewing DOCX');
      fallbackName = `${buildFileStem(scopedDraft)}.docx`;
    } catch (e) {
      setError(e?.message || String(e));
      setStatus('');
      return;
    }

    setError('');
    setStatus('');
    setPreviewOpen(true);
    setPreviewLoading(true);
    setPreviewError('');
    setPreviewHtml('');
    setPreviewWarnings([]);
    setPreviewZoom(1);
    setPreviewTitle('Knowledge Questionnaire Preview');
    setPreviewFileName(fallbackName);

    try {
      const preview = await fetchDocxPreview(
        `${API}/KnowledgeQuestionnaire/v1-phase-export-docx`,
        fallbackName,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        }
      );
      setPreviewHtml(preview?.html || '');
      setPreviewWarnings(Array.isArray(preview?.warnings) ? preview.warnings : []);
      if (preview?.fileName) {
        setPreviewFileName(preview.fileName);
        setPreviewTitle(`Knowledge Questionnaire Preview - ${preview.fileName}`);
      }
    } catch (e) {
      setPreviewError(`Failed to generate preview: ${e?.message || e}`);
    } finally {
      setPreviewLoading(false);
    }
  };

  const handleDownloadSelectedCategoryDocx = async () => {
    let payload;
    let fallbackName = 'KnowledgeQuestionnaire.docx';

    try {
      payload = buildSelectedCategoryDocxPayload('exporting DOCX');
      fallbackName = `${buildFileStem(scopedDraft)}.docx`;
    } catch (e) {
      setError(e?.message || String(e));
      setStatus('');
      return;
    }

    setError('');
    setStatus('');

    try {
      const response = await fetch(`${API}/KnowledgeQuestionnaire/v1-phase-export-docx`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!response.ok) throw new Error(await parseErrorText(response));

      const blob = await response.blob();
      const fileName = parseDownloadFileName(response, fallbackName);
      downloadBlobFile(fileName, blob);
      setStatus(`Downloaded ${fileName}.`);
    } catch (e) {
      setError(`DOCX export failed: ${e?.message || e}`);
    }
  };

  const subjectPreview = useMemo(() => {
    const rows = Array.isArray(scopedDraft?.subjects) ? scopedDraft.subjects : [];
    return rows.slice(0, 6);
  }, [scopedDraft]);

  const topicPreview = useMemo(() => {
    const rows = Array.isArray(scopedDraft?.topics) ? scopedDraft.topics : [];
    return rows.slice(0, 8);
  }, [scopedDraft]);

  const generatedQuestionReview = useMemo(
    () => buildGeneratedQuestionReview(generatedQuestions, scopedDraft?.metadata?.minimumQuestionsPerCriterion),
    [generatedQuestions, scopedDraft]
  );

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Knowledge Questionnaire v1</h2>
      <p style={{ marginTop: 4, color: '#4b6075', maxWidth: 1100 }}>
        Standing rule: one Knowledge Questionnaire exam is written after each main category inside the selected
        Knowledge Learning Phase. Defaults stay locked to two questions per assessable criterion, with True/False and
        four-option Multiple Choice only. Lecturers may override the parameters, but doing so can destabilize the exam
        weight ratio.
      </p>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(280px, 1fr))', gap: 12, maxWidth: 1100, marginBottom: 16 }}>
        <label>
          Qualification
          <select
            className="mainpage-input"
            value={selectedQualificationId}
            onChange={(event) => setSelectedQualificationId(event.target.value)}
            disabled={loadingQualifications}
          >
            {qualifications.length === 0 ? <option value="">No qualifications available</option> : null}
            {qualifications.map((row) => (
              <option key={row.id} value={row.id}>
                {[row.qualificationNumber, row.qualificationDescription].filter(Boolean).join(' - ')}
              </option>
            ))}
          </select>
        </label>

        <label>
          Curriculum Phase
          <select
            className="mainpage-input"
            value={selectedPhaseId}
            onChange={(event) => setSelectedPhaseId(event.target.value)}
            disabled={loadingPhases || phases.length === 0}
          >
            {phases.length === 0 ? <option value="">No curriculum phases available</option> : null}
            {phases.map((row) => (
              <option key={row.id} value={row.id}>
                {[row.name, row.description].filter(Boolean).join(' - ')}
              </option>
            ))}
          </select>
        </label>
      </div>
      {selectedQualification ? (
        <div style={{ marginBottom: 12, color: '#355' }}>
          <strong>Qualification:</strong> {[selectedQualification.qualificationNumber, selectedQualification.qualificationDescription].filter(Boolean).join(' - ')}
        </div>
      ) : null}

      {selectedPhase ? (
        <div style={{ marginBottom: 20, color: '#355' }}>
          <strong>Phase Scope:</strong> {[selectedPhase.name, selectedPhase.description].filter(Boolean).join(' / ')}
        </div>
      ) : null}

      {loadingDraft ? <div style={{ marginBottom: 12, color: '#355' }}>Loading knowledge-phase draft...</div> : null}
      {error ? <div style={{ marginBottom: 12, color: '#b00020' }}>{error}</div> : null}
      {status ? <div style={{ marginBottom: 12, color: '#1b6347' }}>{status}</div> : null}

      {draft && validation.errors.length > 0 ? (
        <div style={{ marginBottom: 16, border: '1px solid #f2c0c0', borderRadius: 8, background: '#fff6f6', padding: 12, maxWidth: 1100 }}>
          <div style={{ fontWeight: 700, marginBottom: 6 }}>Generation blockers</div>
          <ul style={{ margin: 0, paddingLeft: 20 }}>
            {validation.errors.map((message) => <li key={message}>{message}</li>)}
          </ul>
        </div>
      ) : null}

      {draft && combinedWarnings.length > 0 ? (
        <div style={{ marginBottom: 16, border: '1px solid #ecd497', borderRadius: 8, background: '#fff9e7', padding: 12, maxWidth: 1100 }}>
          <div style={{ fontWeight: 700, marginBottom: 6 }}>Review warnings</div>
          <ul style={{ margin: 0, paddingLeft: 20 }}>
            {combinedWarnings.map((message) => <li key={message}>{message}</li>)}
          </ul>
        </div>
      ) : null}

      {draft ? (
        <>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(6, minmax(150px, 1fr))', gap: 12, maxWidth: 1100, marginBottom: 20 }}>
            <div style={{ border: '1px solid #d8dfeb', borderRadius: 8, padding: 12, background: '#f8fbff' }}>
              <div style={{ fontSize: 12, color: '#5b6f86', textTransform: 'uppercase' }}>File Stem</div>
              <div style={{ fontWeight: 700 }}>{scopedDraft ? buildFileStem(scopedDraft) : buildFileStem(draft)}</div>
            </div>
            <div style={{ border: '1px solid #d8dfeb', borderRadius: 8, padding: 12, background: '#f8fbff' }}>
              <div style={{ fontSize: 12, color: '#5b6f86', textTransform: 'uppercase' }}>Subjects</div>
              <div style={{ fontWeight: 700 }}>{phaseExamSummary.totalSubjects}</div>
            </div>
            <div style={{ border: '1px solid #d8dfeb', borderRadius: 8, padding: 12, background: '#f8fbff' }}>
              <div style={{ fontSize: 12, color: '#5b6f86', textTransform: 'uppercase' }}>Topics</div>
              <div style={{ fontWeight: 700 }}>{phaseExamSummary.totalTopics}</div>
            </div>
            <div style={{ border: '1px solid #d8dfeb', borderRadius: 8, padding: 12, background: '#f8fbff' }}>
              <div style={{ fontSize: 12, color: '#5b6f86', textTransform: 'uppercase' }}>Assessable Criteria</div>
              <div style={{ fontWeight: 700 }}>{phaseExamSummary.totalCriteria}</div>
            </div>
            <div style={{ border: '1px solid #d8dfeb', borderRadius: 8, padding: 12, background: '#f8fbff' }}>
              <div style={{ fontSize: 12, color: '#5b6f86', textTransform: 'uppercase' }}>Main Categories</div>
              <div style={{ fontWeight: 700 }}>{phaseExamSummary.totalMainCategories}</div>
            </div>
            <div style={{ border: '1px solid #d8dfeb', borderRadius: 8, padding: 12, background: '#f8fbff' }}>
              <div style={{ fontSize: 12, color: '#5b6f86', textTransform: 'uppercase' }}>Default Question Load</div>
              <div style={{ fontWeight: 700 }}>{phaseExamSummary.defaultTotalQuestions}</div>
            </div>
          </div>

          <div className="form-section" style={{ maxWidth: 1100 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap', marginBottom: 12 }}>
              <h3 style={{ margin: 0 }}>Metadata and Export</h3>
              <div className="button-row">
                <button type="button" onClick={reloadCurrentDraft} disabled={loadingDraft || smiBusy}>Reload Draft</button>
                <button type="button" onClick={saveCurrentState} disabled={loadingDraft}>Save Local State</button>
                <button type="button" onClick={resetLocalOverrides} disabled={loadingDraft || smiBusy}>Reset Local Overrides</button>
                <button type="button" onClick={generateWithSmi} disabled={loadingDraft || smiBusy || !selectedCategoryPlan}>Generate Selected Category With SMI</button>
                <button type="button" onClick={clearGeneratedQuestions} disabled={generatedQuestions.length === 0 || smiBusy}>Clear Generated</button>
                <button type="button" onClick={handlePreviewSelectedCategoryDocx} disabled={loadingDraft || smiBusy || generatedQuestions.length === 0 || !selectedCategoryPlan}>Preview Selected Category DOCX</button>
                <button type="button" onClick={handleDownloadSelectedCategoryDocx} disabled={loadingDraft || smiBusy || generatedQuestions.length === 0 || !selectedCategoryPlan}>Save Selected Category (.docx)</button>
                <button type="button" onClick={() => handleDownload('metadata')} disabled={loadingDraft || smiBusy}>Download Metadata CSV</button>
                <button type="button" onClick={() => handleDownload('questions')} disabled={loadingDraft || smiBusy}>Download Question Rows CSV</button>
                <button type="button" onClick={() => handleDownload('both')} disabled={loadingDraft || smiBusy}>Download Both CSVs</button>
                <button type="button" onClick={handleDownloadAllCategories} disabled={loadingDraft || smiBusy || categoryPlans.length === 0}>Download All Category CSVs</button>
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(280px, 1fr))', gap: 12, marginBottom: 12 }}>
              <label>
                Main Category Exam
                <select
                  className="mainpage-input"
                  value={selectedCategoryPlan?.key || ''}
                  onChange={(event) => setSelectedCategoryKey(event.target.value)}
                  disabled={categoryPlans.length === 0}
                >
                  {categoryPlans.length === 0 ? <option value="">No main categories available</option> : null}
                  {categoryPlans.map((plan) => (
                    <option key={plan.key} value={plan.key}>
                      {[plan.code, plan.label, `${plan.stats.totalCriteria} assessable criteria`].filter(Boolean).join(' - ')}
                    </option>
                  ))}
                </select>
              </label>
              <div style={{ border: '1px solid #d8dfeb', borderRadius: 8, padding: 12, background: '#f8fbff' }}>
                <div style={{ fontSize: 12, color: '#5b6f86', textTransform: 'uppercase' }}>Selected Category Default</div>
                <div style={{ fontWeight: 700 }}>
                  {selectedCategoryPlan ? `${selectedCategoryPlan.label}: ${selectedCategoryPlan.defaultMetadata.totalQuestions} questions / ${selectedCategoryPlan.defaultMetadata.totalMarks} marks` : 'No category selected'}
                </div>
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(220px, 1fr))', gap: 12 }}>
              <label>
                Questionnaire Title
                <input
                  className="mainpage-input"
                  value={scopedDraft?.metadata.questionnaireTitle || ''}
                  onChange={(event) => updateMetadata('questionnaireTitle', event.target.value)}
                />
              </label>

              <label>
                Bloom Domain
                <input className="mainpage-input" value={scopedDraft?.metadata.bloomDomain || ''} readOnly />
              </label>

              <label>
                Bloom Target Level
                <input className="mainpage-input" value={scopedDraft?.metadata.bloomTargetLevel || ''} readOnly />
              </label>

              <label>
                Minimum Questions Per Criterion
                <input
                  className="mainpage-input"
                  type="number"
                  min="0"
                  value={scopedDraft?.metadata.minimumQuestionsPerCriterion || 0}
                  onChange={(event) => updateMetadata('minimumQuestionsPerCriterion', event.target.value)}
                />
              </label>

              <label>
                Minimum Total Questions
                <input
                  className="mainpage-input"
                  type="number"
                  min="0"
                  value={scopedDraft?.metadata.minimumTotalQuestions || 0}
                  onChange={(event) => updateMetadata('minimumTotalQuestions', event.target.value)}
                />
              </label>

              <label>
                Total Questions
                <input className="mainpage-input" value={scopedDraft?.metadata.totalQuestions || 0} readOnly />
              </label>

              <label>
                True/False Count
                <input
                  className="mainpage-input"
                  type="number"
                  min="0"
                  value={scopedDraft?.metadata.trueFalseCount || 0}
                  onChange={(event) => updateMetadata('trueFalseCount', event.target.value)}
                />
              </label>

              <label>
                Multiple Choice Count
                <input
                  className="mainpage-input"
                  type="number"
                  min="0"
                  value={scopedDraft?.metadata.multipleChoiceCount || 0}
                  onChange={(event) => updateMetadata('multipleChoiceCount', event.target.value)}
                />
              </label>

              <label>
                Total Marks
                <input className="mainpage-input" value={scopedDraft?.metadata.totalMarks || 0} readOnly />
              </label>

              <label>
                Pass Mark
                <input
                  className="mainpage-input"
                  value={scopedDraft?.metadata.passMark || ''}
                  onChange={(event) => updateMetadata('passMark', event.target.value)}
                  placeholder="e.g. 50% or 50/100"
                />
              </label>

              <label>
                Created By
                <input
                  className="mainpage-input"
                  value={scopedDraft?.metadata.createdBy || ''}
                  onChange={(event) => updateMetadata('createdBy', event.target.value)}
                />
              </label>

              <label>
                Reviewed By
                <input
                  className="mainpage-input"
                  value={scopedDraft?.metadata.reviewedBy || ''}
                  onChange={(event) => updateMetadata('reviewedBy', event.target.value)}
                />
              </label>

              <label style={{ gridColumn: '1 / -1' }}>
                Notes
                <textarea
                  className="mainpage-input"
                  value={scopedDraft?.metadata.notes || ''}
                  onChange={(event) => updateMetadata('notes', event.target.value)}
                  rows={3}
                />
              </label>
            </div>

            <div style={{ marginTop: 10, color: '#35536b', fontSize: 14 }}>
              Standing default for {selectedCategoryPlan?.label || 'the selected main category'}: {validation.stats.kqCriteriaCount} assessable criteria x {validation.minimumQuestionsPerCriterion} default questions per criterion = {validation.formulaSuggestedTotalQuestions} question(s).
              Minimum Total Questions is the manual override enforced during export and SMI generation.
            </div>
            <div style={{ marginTop: 6, color: '#7a5b17', fontSize: 13 }}>
              Changing the default parameters can destabilize the exam weight ratio. The system allows the override, but the risk remains with the lecturer.
            </div>
          </div>
          <div className="form-section" style={{ maxWidth: 1100 }}>
            <h3 style={{ marginTop: 0 }}>Main Category Scope</h3>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(280px, 1fr))', gap: 16 }}>
              <div>
                <div style={{ fontWeight: 700, marginBottom: 8 }}>Subjects</div>
                <ul style={{ margin: 0, paddingLeft: 20 }}>
                  {subjectPreview.map((row) => (
                    <li key={row.subjectId}>{[row.subjectCode, row.subjectDescription].filter(Boolean).join(' - ')}</li>
                  ))}
                </ul>
                {scopedDraft && scopedDraft.subjects.length > subjectPreview.length ? (
                  <div style={{ marginTop: 8, color: '#5b6f86' }}>+ {scopedDraft.subjects.length - subjectPreview.length} more subject(s)</div>
                ) : null}
              </div>
              <div>
                <div style={{ fontWeight: 700, marginBottom: 8 }}>Assessable Topics</div>
                <ul style={{ margin: 0, paddingLeft: 20 }}>
                  {topicPreview.map((row) => (
                    <li key={row.topicId}>{[row.subjectCode, row.topicCode, row.topicDescription].filter(Boolean).join(' - ')}</li>
                  ))}
                </ul>
                {scopedDraft && scopedDraft.topics.length > topicPreview.length ? (
                  <div style={{ marginTop: 8, color: '#5b6f86' }}>+ {scopedDraft.topics.length - topicPreview.length} more topic(s)</div>
                ) : null}
              </div>
            </div>
          </div>

          <div className="form-section" style={{ maxWidth: 1100 }}>
            <h3 style={{ marginTop: 0 }}>Criterion Decomposition</h3>
            <p style={{ marginTop: 0, color: '#4b6075' }}>
              SMI confirms or corrects noun focus, canonical verb, qualifier, coverage type, and routing status before generation.
              Rows marked <strong>Other Assessment</strong> stay visible for review but are excluded from the selected main-category exam.
            </p>

            <div style={{ overflowX: 'auto', border: '1px solid #d8dfeb', borderRadius: 8 }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: 1640 }}>
                <thead>
                  <tr style={{ background: '#f3f7fc', textAlign: 'left' }}>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Subject</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Topic</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>AC Number</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Original Criterion</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Noun Focus</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Detected Verb</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Canonical Verb</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Bloom Level</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Qualifier</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Qualifier Source</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Coverage</th>
                    <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Routing</th>
                  </tr>
                </thead>
                <tbody>
                  {(scopedDraft?.criteria ?? []).map((row) => (
                    <tr
                      key={row.intentId}
                      style={{
                        background: row.routingStatus === 'KQ' ? '#ffffff' : '#f8f3f3',
                        borderBottom: '1px solid #e4ebf3'
                      }}
                    >
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 180 }}>
                        {[row.subjectCode, row.subjectDescription].filter(Boolean).join(' - ')}
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 220 }}>
                        {[row.topicCode, row.topicDescription].filter(Boolean).join(' - ')}
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top' }}>
                        <input
                          className="mainpage-input"
                          value={row.assessmentCriteriaNumber}
                          onChange={(event) => updateCriterion(row.intentId, 'assessmentCriteriaNumber', event.target.value)}
                        />
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 280 }}>
                        <div style={{ whiteSpace: 'pre-wrap', lineHeight: 1.4 }}>{row.originalCriterionText || 'No criterion text'}</div>
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 220 }}>
                        <textarea
                          className="mainpage-input"
                          value={row.nounFocus}
                          onChange={(event) => updateCriterion(row.intentId, 'nounFocus', event.target.value)}
                          rows={3}
                        />
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 120 }}>
                        {row.detectedVerb || <span style={{ color: '#8c99a8' }}>Not detected</span>}
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 180 }}>
                        <input
                          className="mainpage-input"
                          list="kq-v1-verb-options"
                          value={row.canonicalVerb}
                          onChange={(event) => updateCriterion(row.intentId, 'canonicalVerb', event.target.value)}
                        />
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 120 }}>
                        {row.bloomLevel || <span style={{ color: '#8c99a8' }}>Unclassified</span>}
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 240 }}>
                        <textarea
                          className="mainpage-input"
                          value={row.qualifier}
                          onChange={(event) => updateCriterion(row.intentId, 'qualifier', event.target.value)}
                          placeholder={row.qualifierSource === 'lesson_plan_fallback'
                            ? 'Lesson plan content defines the question scope. Manual qualifier entry is optional.'
                            : ''}
                          rows={3}
                        />
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 170 }}>
                        <select
                          className="mainpage-input"
                          value={row.qualifierSource}
                          onChange={(event) => updateCriterion(row.intentId, 'qualifierSource', event.target.value)}
                        >
                          <option value="detected_from_criterion">detected_from_criterion</option>
                          <option value="lesson_plan_fallback">lesson_plan_fallback</option>
                          <option value="smi_required">smi_required</option>
                          <option value="smi_override">smi_override</option>
                        </select>
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 140 }}>
                        <select
                          className="mainpage-input"
                          value={row.coverageType}
                          onChange={(event) => updateCriterion(row.intentId, 'coverageType', event.target.value)}
                        >
                          <option value="direct">direct</option>
                          <option value="proxy">proxy</option>
                        </select>
                      </td>
                      <td style={{ padding: 10, verticalAlign: 'top', minWidth: 180 }}>
                        <select
                          className="mainpage-input"
                          value={row.routingStatus}
                          onChange={(event) => updateCriterion(row.intentId, 'routingStatus', event.target.value)}
                        >
                          <option value="KQ">KQ</option>
                          <option value="Other Assessment">Other Assessment</option>
                        </select>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <datalist id="kq-v1-verb-options">
              {verbOptions.map((verb) => <option key={verb} value={verb} />)}
            </datalist>
          </div>
          <div className="form-section" style={{ maxWidth: 1100 }}>
            <h3 style={{ marginTop: 0 }}>SMI Output</h3>
            <p style={{ marginTop: 0, color: '#4b6075' }}>
              The official KQ workflow now generates one questionnaire for the selected main category inside the Knowledge Learning Phase.
              Downloaded question CSVs will use these generated rows if they are present; otherwise the scaffold rows are exported.
              Word export uses the generated rows directly and does not require Excel, CSV import, or mail merge.
            </p>

            {smiSource ? <div style={{ marginBottom: 10, color: '#355' }}><strong>Source:</strong> {smiSource}</div> : null}
            {generatedQuestions.length > 0 ? <div style={{ marginBottom: 10, color: '#355' }}><strong>Generated Rows:</strong> {generatedQuestions.length}</div> : null}
            {generatedQuestionReview.warnings.length > 0 ? (
              <div style={{ marginBottom: 12, border: '1px solid #ecd497', borderRadius: 8, background: '#fff9e7', padding: 12 }}>
                <div style={{ fontWeight: 700, marginBottom: 6 }}>Generated Question Review</div>
                <ul style={{ margin: 0, paddingLeft: 20 }}>
                  {generatedQuestionReview.warnings.map((message) => <li key={message}>{message}</li>)}
                </ul>
              </div>
            ) : null}
            {smiResources.length > 0 ? (
              <div style={{ marginBottom: 12 }}>
                <div style={{ fontWeight: 700, marginBottom: 6 }}>Learning Resource Suggestions</div>
                <ul style={{ margin: 0, paddingLeft: 20 }}>
                  {smiResources.map((resource) => <li key={resource}>{resource}</li>)}
                </ul>
              </div>
            ) : null}

            {generatedQuestions.length === 0 ? (
              <div style={{ color: '#5b6f86' }}>No generated questions yet. Use <strong>Generate Selected Category With SMI</strong> after reviewing the decomposition table.</div>
            ) : (
              <div style={{ overflowX: 'auto', border: '1px solid #d8dfeb', borderRadius: 8 }}>
                <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: 1500 }}>
                  <thead>
                    <tr style={{ background: '#f3f7fc', textAlign: 'left' }}>
                      <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>#</th>
                      <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Type</th>
                      <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Subject</th>
                      <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Topic</th>
                      <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>AC Number</th>
                      <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Prompt</th>
                      <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Options</th>
                      <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Correct Answer</th>
                      <th style={{ padding: 10, borderBottom: '1px solid #d8dfeb' }}>Rationale</th>
                    </tr>
                  </thead>
                  <tbody>
                    {generatedQuestions.map((row) => (
                      <tr key={`${row.number}-${row.assessmentCriteriaNumber}-${row.type}`} style={{ borderBottom: '1px solid #e4ebf3' }}>
                        <td style={{ padding: 10, verticalAlign: 'top' }}>{row.number}</td>
                        <td style={{ padding: 10, verticalAlign: 'top' }}>{row.type === 'TrueFalse' ? 'True/False' : 'Multiple Choice'}</td>
                        <td style={{ padding: 10, verticalAlign: 'top', minWidth: 180 }}>{[row.subjectCode, row.subjectDescription].filter(Boolean).join(' - ')}</td>
                        <td style={{ padding: 10, verticalAlign: 'top', minWidth: 220 }}>{[row.topicCode, row.topicDescription].filter(Boolean).join(' - ')}</td>
                        <td style={{ padding: 10, verticalAlign: 'top' }}>{row.assessmentCriteriaNumber}</td>
                        <td style={{ padding: 10, verticalAlign: 'top', minWidth: 300, whiteSpace: 'pre-wrap', lineHeight: 1.4 }}>{row.prompt}</td>
                        <td style={{ padding: 10, verticalAlign: 'top', minWidth: 260 }}>
                          <ol style={{ margin: 0, paddingLeft: 20 }}>
                            {row.options.slice(0, 4).map((option, index) => <li key={`${row.number}-${index}`}>{option}</li>)}
                          </ol>
                        </td>
                        <td style={{ padding: 10, verticalAlign: 'top', minWidth: 160 }}>{row.correctAnswer}</td>
                        <td style={{ padding: 10, verticalAlign: 'top', minWidth: 260, whiteSpace: 'pre-wrap', lineHeight: 1.4 }}>{row.rationale}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

          </div>
        </>
      ) : null}

      <DocxPreviewModal
        open={previewOpen}
        title={previewTitle}
        loading={previewLoading}
        error={previewError}
        html={previewHtml}
        warnings={previewWarnings}
        editableDocx
        editedDocxFileName={previewFileName}
        zoom={previewZoom}
        onZoomIn={() => setPreviewZoom((value) => Math.min(2.5, Number(value || 1) + 0.1))}
        onZoomOut={() => setPreviewZoom((value) => Math.max(0.5, Number(value || 1) - 0.1))}
        onZoomReset={() => setPreviewZoom(1)}
        onClose={() => setPreviewOpen(false)}
      />
    </div>
  );
}
