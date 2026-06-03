import React, { useEffect, useMemo, useRef, useState } from 'react';
import axios from 'axios';
import { jsPDF } from 'jspdf';
import { useLocation } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import { normalizeLearningMaterialParams, readLearningMaterialParams, writeLearningMaterialParams } from '../utils/learningMaterialParams';
import { subjectCodeOf, subjectDescriptionOf, subjectIdOf } from '../utils/learningMaterialCommon';

const API_BASE = '/api';
const A4_PORTRAIT = { width: 794, height: 1123 };
const A4_EXPORT_PORTRAIT = { width: A4_PORTRAIT.width * 2, height: A4_PORTRAIT.height * 2 };
const PHASE_PALETTE = ['#2b2d42', '#006d77', '#3a86ff', '#8338ec', '#ff006e', '#fb5607', '#2a9d8f', '#264653'];
const CATEGORY_OPTIONS = [
  { key: 'basicEngineering', label: 'Basic Engineering', aliases: ['basic engineering'], codeTokens: ['KM-01'] },
  { key: 'fittingTheory', label: 'Fitting Theory', aliases: ['fitting theory'], codeTokens: ['KM-02'] },
  { key: 'machineTheory', label: 'Machine Theory', aliases: ['machine theory', 'machining theory'], codeTokens: ['KM-03'] },
];

const qId = (q) => Number(q?.id ?? q?.Id ?? 0);
const qNumber = (q) => String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim();
const qDescription = (q) => String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim();
const learningPhasesOf = (value) => String(value?.learningPhases ?? value?.LearningPhases ?? value?.phaseName ?? value?.PhaseName ?? '').trim();
const subjectPurposeOf = (value) => String(value?.subjectPurpose ?? value?.SubjectPurpose ?? '').trim();

const createDefaultCategoryFilters = () =>
  CATEGORY_OPTIONS.reduce((acc, option) => {
    acc[option.key] = true;
    return acc;
  }, {});

const normalizeFilterText = (value) => String(value ?? '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();

const colorFromKey = (key) => {
  const text = `${key ?? ''}`;
  let sum = 0;
  for (let i = 0; i < text.length; i += 1) sum += text.charCodeAt(i);
  return PHASE_PALETTE[sum % PHASE_PALETTE.length];
};

const colorWithAlpha = (hex, alpha = 1) => {
  const value = String(hex ?? '').replace('#', '').trim();
  const full = value.length === 3
    ? value.split('').map((part) => `${part}${part}`).join('')
    : value;
  if (!/^[0-9a-f]{6}$/i.test(full)) return hex;
  const int = Number.parseInt(full, 16);
  const r = (int >> 16) & 255;
  const g = (int >> 8) & 255;
  const b = int & 255;
  return `rgba(${r}, ${g}, ${b}, ${Math.max(0, Math.min(1, alpha))})`;
};

const sanitizeFilePart = (value) => String(value ?? '')
  .trim()
  .toLowerCase()
  .replace(/[^a-z0-9]+/g, '-')
  .replace(/^-+|-+$/g, '')
  .slice(0, 48);

const clampText = (value, max = 120) => {
  const text = `${value ?? ''}`.replace(/\s+/g, ' ').trim();
  if (text.length <= max) return text;
  return `${text.slice(0, Math.max(0, max - 1))}...`;
};

const splitLines = (value, maxChars = 36, maxLines = 3) => {
  const words = `${value ?? ''}`.split(/\s+/).filter(Boolean);
  if (words.length === 0) return [];
  const lines = [];
  let line = '';
  for (const word of words) {
    const next = line.length === 0 ? word : `${line} ${word}`;
    if (next.length <= maxChars) {
      line = next;
      continue;
    }
    lines.push(line);
    line = word;
    if (lines.length >= maxLines - 1) break;
  }
  if (lines.length < maxLines && line.length > 0) lines.push(line);
  if (lines.length > maxLines) return lines.slice(0, maxLines);
  if (words.join(' ').length > lines.join(' ').length && lines.length > 0) {
    lines[lines.length - 1] = clampText(lines[lines.length - 1], maxChars);
  }
  return lines;
};

const getSvgSize = (svg) => {
  const viewBoxWidth = Number(svg.viewBox?.baseVal?.width || 0);
  const viewBoxHeight = Number(svg.viewBox?.baseVal?.height || 0);
  const widthAttr = Number(svg.getAttribute('width') || 0);
  const heightAttr = Number(svg.getAttribute('height') || 0);
  return {
    width: Math.max(1, Math.round(viewBoxWidth || widthAttr || 900)),
    height: Math.max(1, Math.round(viewBoxHeight || heightAttr || 600)),
  };
};

const serializeSvgForExport = (svg, width, height) => {
  const clone = svg.cloneNode(true);
  clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  clone.setAttribute('xmlns:xlink', 'http://www.w3.org/1999/xlink');
  clone.setAttribute('version', '1.1');
  clone.setAttribute('width', String(width));
  clone.setAttribute('height', String(height));
  if (!clone.getAttribute('viewBox')) {
    clone.setAttribute('viewBox', `0 0 ${width} ${height}`);
  }
  clone.setAttribute('preserveAspectRatio', 'xMinYMin meet');
  clone.style.transform = '';
  clone.style.transformOrigin = '';
  clone.style.width = `${width}px`;
  clone.style.height = `${height}px`;
  clone.style.background = '#ffffff';
  clone.style.overflow = 'visible';
  return `<?xml version="1.0" encoding="UTF-8"?>${new XMLSerializer().serializeToString(clone)}`;
};

const renderSvgToCanvas = (svgString, width, height, renderScale = 1) =>
  new Promise((resolve, reject) => {
    const img = new Image();
    img.decoding = 'sync';
    const svgBlob = new Blob([svgString], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(svgBlob);
    img.onload = () => {
      const ratio = Math.max(1, (window.devicePixelRatio || 1) * renderScale);
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(1, Math.round(width * ratio));
      canvas.height = Math.max(1, Math.round(height * ratio));
      canvas.style.width = `${width}px`;
      canvas.style.height = `${height}px`;
      const ctx = canvas.getContext('2d');
      ctx.imageSmoothingEnabled = true;
      ctx.imageSmoothingQuality = 'high';
      ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, 0, width, height);
      ctx.drawImage(img, 0, 0, width, height);
      URL.revokeObjectURL(url);
      resolve(canvas);
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error('Failed to render the flow diagram SVG.'));
    };
    img.src = url;
  });

const downloadJsonPayload = (payload, fileName) => {
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
};

const downloadBlob = (blob, fileName) => {
  const url = window.URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => window.URL.revokeObjectURL(url), 1500);
};

const canvasToBlob = (canvas, type = 'image/png') =>
  new Promise((resolve, reject) => {
    canvas.toBlob((blob) => {
      if (blob) {
        resolve(blob);
        return;
      }
      reject(new Error('Canvas export failed.'));
    }, type);
  });

const resolveSubjectCategoryKey = (value) => {
  const codeText = normalizeFilterText(
    value?.subjectCode ??
    value?.SubjectCode ??
    value?.code ??
    value?.Code ??
    value?.name ??
    ''
  );
  const text = normalizeFilterText([
    value?.subjectDescription ?? value?.SubjectDescription ?? value?.description ?? value?.Description ?? value?.name ?? '',
    learningPhasesOf(value),
    value?.phaseName ?? value?.PhaseName ?? '',
    subjectPurposeOf(value),
  ].join(' '));

  for (const option of CATEGORY_OPTIONS) {
    const aliasMatch = option.aliases.some((alias) => text.includes(normalizeFilterText(alias)));
    const codeMatch = option.codeTokens.some((token) => codeText.includes(normalizeFilterText(token)));
    if (aliasMatch || codeMatch) {
      return option.key;
    }
  }

  return '';
};

const matchesSubjectCategoryFilters = (value, categoryFilters) => {
  const selectedKeys = CATEGORY_OPTIONS.filter((option) => categoryFilters?.[option.key]).map((option) => option.key);
  if (selectedKeys.length === CATEGORY_OPTIONS.length) return true;
  if (selectedKeys.length === 0) return false;
  const resolvedKey = resolveSubjectCategoryKey(value);
  if (!resolvedKey) return false;
  return selectedKeys.includes(resolvedKey);
};

const normalizeFlowData = (data) => {
  if (!data || !Array.isArray(data.modules)) return data;
  const modules = data.modules.map((module) => {
    const topicMap = new Map();
    (module.topics || []).forEach((topic) => {
      topicMap.set(topic.topicId, {
        topicId: topic.topicId,
        topicCode: topic.topicCode || 'TOPIC',
        topicDescription: topic.topicDescription || 'Topic',
        lessonPlans: (topic.lessonPlans || []).map((lp, idx) => ({
          lessonPlanId: lp.lessonPlanId,
          title: lp.title || 'Lesson Plan',
          durationMinutes: lp.durationMinutes || 0,
          sortOrder: lp.sortOrder ?? idx + 1,
        })),
      });
    });

    if (topicMap.size === 0) {
      (module.sessions || []).forEach((session, idx) => {
        const key = session.topicId ?? `fallback-${idx}`;
        if (!topicMap.has(key)) {
          topicMap.set(key, {
            topicId: session.topicId,
            topicCode: session.topicCode || 'TOPIC',
            topicDescription: session.topicDescription || 'Topic',
            lessonPlans: [],
          });
        }
        topicMap.get(key).lessonPlans.push({
          lessonPlanId: session.lessonPlanId ?? idx + 1,
          title: session.title || 'Lesson Plan',
          durationMinutes: session.durationMinutes || 0,
          sortOrder: idx + 1,
        });
      });
    }

    const topics = [...topicMap.values()].map((topic) => ({
      ...topic,
      lessonPlans: [...(topic.lessonPlans || [])].sort((a, b) => (a.sortOrder || 0) - (b.sortOrder || 0)),
    }));

    const sessions = topics.flatMap((topic) =>
      topic.lessonPlans.map((lesson) => ({
        lessonPlanId: lesson.lessonPlanId,
        title: lesson.title,
        durationMinutes: lesson.durationMinutes,
        topicId: topic.topicId,
        topicCode: topic.topicCode,
        topicDescription: topic.topicDescription,
      }))
    );

    const totalDurationMinutes = sessions.reduce((sum, s) => sum + (s.durationMinutes || 0), 0);
    const lessonCards = (module.lessonCards || []).map((card, idx) => ({
      index: card.index ?? idx + 1,
      lpn: card.lpn || `LPN ${idx + 1}`,
      lessonPlanDescription: card.lessonPlanDescription || card.title || 'Lesson Plan',
      topic: card.topic || '',
      timeStart: card.timeStart || '',
      timeEnd: card.timeEnd || '',
    }));
    return { ...module, topics, sessions, lessonCards, totalDurationMinutes };
  });
  return { ...data, modules };
};

const recomputeModule = (module) => {
  const topics = (module.topics || []).map((topic) => ({
    ...topic,
    lessonPlans: (topic.lessonPlans || []).map((lesson, idx) => ({
      ...lesson,
      sortOrder: idx + 1,
    })),
  }));
  const sessions = topics.flatMap((topic) =>
    topic.lessonPlans.map((lesson) => ({
      lessonPlanId: lesson.lessonPlanId,
      title: lesson.title,
      durationMinutes: lesson.durationMinutes,
      topicId: topic.topicId,
      topicCode: topic.topicCode,
      topicDescription: topic.topicDescription,
    }))
  );
  const totalDurationMinutes = sessions.reduce((sum, s) => sum + (s.durationMinutes || 0), 0);
  return { ...module, topics, sessions, totalDurationMinutes };
};

const buildA4Pages = (canvas, pageSize = A4_PORTRAIT) => {
  const pageWidth = pageSize.width;
  const pageHeight = pageSize.height;
  const scale = pageWidth / canvas.width;
  const scaledHeight = Math.max(1, Math.ceil(canvas.height * scale));
  const scaledCanvas = document.createElement('canvas');
  scaledCanvas.width = pageWidth;
  scaledCanvas.height = scaledHeight;
  const scaledCtx = scaledCanvas.getContext('2d');
  scaledCtx.imageSmoothingEnabled = true;
  scaledCtx.imageSmoothingQuality = 'high';
  scaledCtx.fillStyle = '#ffffff';
  scaledCtx.fillRect(0, 0, scaledCanvas.width, scaledCanvas.height);
  scaledCtx.drawImage(canvas, 0, 0, pageWidth, scaledHeight);

  const pages = [];
  for (let y = 0; y < scaledHeight; y += pageHeight) {
    const pageCanvas = document.createElement('canvas');
    pageCanvas.width = pageWidth;
    pageCanvas.height = pageHeight;
    const pageCtx = pageCanvas.getContext('2d');
    pageCtx.imageSmoothingEnabled = true;
    pageCtx.imageSmoothingQuality = 'high';
    pageCtx.fillStyle = '#ffffff';
    pageCtx.fillRect(0, 0, pageCanvas.width, pageCanvas.height);
    const copyHeight = Math.min(pageHeight, scaledHeight - y);
    pageCtx.drawImage(scaledCanvas, 0, y, pageWidth, copyHeight, 0, 0, pageWidth, copyHeight);
    pages.push(pageCanvas.toDataURL('image/png'));
  }

  return { pages, pageWidth, pageHeight };
};

const GraphsPage = () => {
  const graphRef = useRef();
  const location = useLocation();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };
  const routeParams = useMemo(
    () => normalizeLearningMaterialParams(location.state?.learningMaterialParams || {}),
    [location.state]
  );
  const storedParams = useMemo(() => normalizeLearningMaterialParams(readLearningMaterialParams()), []);
  const initialQualificationId = String(
    routeParams.qualificationId ||
    location.state?.qualificationId ||
    qualificationId ||
    storedParams.qualificationId ||
    localStorage.getItem('qualificationId') ||
    ''
  );

  const [view, setView] = useState('topics');
  const [qualifications, setQualifications] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [filters, setFilters] = useState({
    qualificationId: initialQualificationId,
    subjectFromId: String(routeParams.subjectFromId || storedParams.subjectFromId || ''),
    subjectToId: String(routeParams.subjectToId || storedParams.subjectToId || ''),
    categories: createDefaultCategoryFilters(),
  });
  const [flowData, setFlowData] = useState(null);
  const [sunburstData, setSunburstData] = useState(null);
  const [scale, setScale] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [drag, setDrag] = useState({ active: false, startX: 0, startY: 0, origX: 0, origY: 0 });
  const [previewPages, setPreviewPages] = useState([]);
  const [previewPageIndex, setPreviewPageIndex] = useState(0);
  const [previewZoom, setPreviewZoom] = useState(1);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [filterBusy, setFilterBusy] = useState(false);
  const [graphBusy, setGraphBusy] = useState(false);
  const [filterError, setFilterError] = useState('');
  const [graphError, setGraphError] = useState('');
  const [draggingLesson, setDraggingLesson] = useState(null);
  const [reorderMessage, setReorderMessage] = useState('');

  const qid = Number(filters.qualificationId || 0);
  const selectedQualification = useMemo(
    () => qualifications.find((q) => String(qId(q)) === String(filters.qualificationId)) || null,
    [qualifications, filters.qualificationId]
  );
  const selectedSubjectFrom = useMemo(
    () => subjects.find((subject) => subjectIdOf(subject) === String(filters.subjectFromId)) || null,
    [subjects, filters.subjectFromId]
  );
  const selectedSubjectTo = useMemo(
    () => subjects.find((subject) => subjectIdOf(subject) === String(filters.subjectToId)) || null,
    [subjects, filters.subjectToId]
  );
  const selectedCategoryCount = CATEGORY_OPTIONS.filter((option) => filters.categories?.[option.key]).length;
  const categoryCounts = useMemo(
    () => CATEGORY_OPTIONS.map((option) => ({
      ...option,
      count: subjects.filter((subject) => resolveSubjectCategoryKey(subject) === option.key).length,
    })),
    [subjects]
  );

  const chartQueryParams = useMemo(() => {
    const params = {};
    if (qid > 0) params.qualificationId = qid;
    if (Number(filters.subjectFromId || 0) > 0) params.subjectFromId = Number(filters.subjectFromId);
    if (Number(filters.subjectToId || 0) > 0) params.subjectToId = Number(filters.subjectToId);
    return params;
  }, [qid, filters.subjectFromId, filters.subjectToId]);

  const visibleFlowData = useMemo(() => {
    if (!flowData) return null;
    const modules = (flowData.modules || []).filter((module) => matchesSubjectCategoryFilters(module, filters.categories));
    return { ...flowData, modules };
  }, [flowData, filters.categories]);

  const visibleSunburstData = useMemo(() => {
    if (!sunburstData) return null;
    const children = (sunburstData.children || [])
      .map((phase) => ({
        ...phase,
        children: (phase.children || []).filter((subject) => matchesSubjectCategoryFilters({
          subjectCode: subject.name,
          subjectDescription: subject.name,
          phaseName: phase.name,
        }, filters.categories)),
      }))
      .filter((phase) => (phase.children || []).length > 0);
    return { ...sunburstData, children };
  }, [sunburstData, filters.categories]);

  const previewImage = useMemo(() => previewPages[previewPageIndex] || null, [previewPages, previewPageIndex]);
  const totalRangeSubjects = (flowData?.modules || []).length;
  const visibleRangeSubjects = (visibleFlowData?.modules || []).length;
  const hasVisibleGraphData = view === 'sunburst'
    ? (visibleSunburstData?.children || []).length > 0
    : (visibleFlowData?.modules || []).length > 0;
  const exportFileStem = useMemo(() => {
    const rangeParts = [];
    if (selectedSubjectFrom) rangeParts.push(subjectCodeOf(selectedSubjectFrom));
    if (selectedSubjectTo && subjectIdOf(selectedSubjectTo) !== subjectIdOf(selectedSubjectFrom)) {
      rangeParts.push(subjectCodeOf(selectedSubjectTo));
    }
    const parts = ['flow-diagram', view, ...rangeParts]
      .map(sanitizeFilePart)
      .filter(Boolean);
    return parts.join('_') || 'flow-diagram';
  }, [view, selectedSubjectFrom, selectedSubjectTo]);

  useEffect(() => {
    let active = true;

    const loadQualifications = async () => {
      setFilterBusy(true);
      setFilterError('');
      try {
        const res = await fetch(`${API_BASE}/Qualification`);
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        if (!active) return;
        const list = Array.isArray(data) ? data : [];
        setQualifications(list);
        if (!filters.qualificationId && list.length > 0) {
          const firstId = String(qId(list[0]));
          setFilters((prev) => ({ ...prev, qualificationId: firstId }));
        }
      } catch (error) {
        if (!active) return;
        setFilterError(`Failed to load qualifications: ${error?.message || error}`);
      } finally {
        if (active) setFilterBusy(false);
      }
    };

    loadQualifications();
    return () => { active = false; };
  }, []);

  useEffect(() => {
    const subjectFrom = subjects.find((subject) => subjectIdOf(subject) === String(filters.subjectFromId)) || null;
    const subjectTo = subjects.find((subject) => subjectIdOf(subject) === String(filters.subjectToId)) || null;
    const current = normalizeLearningMaterialParams(readLearningMaterialParams());
    const qualificationLabel = selectedQualification
      ? `${qNumber(selectedQualification) || '#'} - ${qDescription(selectedQualification) || 'No description'}`
      : current.qualificationLabel;

    writeLearningMaterialParams({
      ...current,
      qualificationId: String(filters.qualificationId || ''),
      qualificationLabel,
      subjectFromId: String(filters.subjectFromId || ''),
      subjectToId: String(filters.subjectToId || ''),
      subjectFromCode: subjectFrom ? subjectCodeOf(subjectFrom) : '',
      subjectToCode: subjectTo ? subjectCodeOf(subjectTo) : '',
    });

    if (qid > 0) {
      setQualificationId(qid);
    }
  }, [filters.qualificationId, filters.subjectFromId, filters.subjectToId, qid, selectedQualification, subjects, setQualificationId]);

  useEffect(() => {
    let active = true;

    const loadSubjects = async () => {
      if (qid <= 0) {
        setSubjects([]);
        setFlowData(null);
        setSunburstData(null);
        return;
      }

      setFilterBusy(true);
      setFilterError('');
      try {
        const res = await fetch(`${API_BASE}/Subject/byQualification?qualificationId=${qid}`);
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        if (!active) return;
        const list = Array.isArray(data) ? data : [];
        setSubjects(list);
        setFilters((prev) => {
          let next = prev;
          const hasFrom = list.some((subject) => subjectIdOf(subject) === String(prev.subjectFromId));
          const hasTo = list.some((subject) => subjectIdOf(subject) === String(prev.subjectToId));
          if (!hasFrom && list.length > 0) {
            next = { ...next, subjectFromId: subjectIdOf(list[0]) };
          }
          if (!hasTo && list.length > 0) {
            next = { ...next, subjectToId: subjectIdOf(list[Math.max(list.length - 1, 0)]) };
          }
          return next;
        });
      } catch (error) {
        if (!active) return;
        setFilterError(`Failed to load subjects for flow diagram filters: ${error?.message || error}`);
        setSubjects([]);
      } finally {
        if (active) setFilterBusy(false);
      }
    };

    loadSubjects();
    return () => { active = false; };
  }, [qid]);

  useEffect(() => {
    let active = true;

    const loadGraphs = async () => {
      if (qid <= 0) {
        setFlowData(null);
        setSunburstData(null);
        return;
      }

      setGraphBusy(true);
      setGraphError('');
      setPreviewPages([]);
      setPreviewPageIndex(0);
      setPreviewZoom(1);
      setScale(1);
      setPan({ x: 0, y: 0 });
      setReorderMessage('');

      try {
        const [flowRes, sunburstRes] = await Promise.all([
          axios.get(`${API_BASE}/Charts/flow-program`, { params: chartQueryParams }),
          axios.get(`${API_BASE}/Charts/sunburst`, { params: chartQueryParams }),
        ]);
        if (!active) return;
        setFlowData(normalizeFlowData(flowRes.data?.data));
        setSunburstData(sunburstRes.data?.data || null);
      } catch (error) {
        if (!active) return;
        const message = error?.response?.data || error?.message || 'Failed to load graph data.';
        setGraphError(typeof message === 'string' ? message : 'Failed to load graph data.');
        setFlowData(null);
        setSunburstData(null);
      } finally {
        if (active) setGraphBusy(false);
      }
    };

    loadGraphs();
    return () => { active = false; };
  }, [qid, chartQueryParams]);

  const updateFilterField = (name, value) => {
    setFilterError('');
    setGraphError('');
    setPreviewPages([]);
    setFilters((prev) => {
      const next = { ...prev, [name]: value };
      if (name === 'qualificationId') {
        next.subjectFromId = '';
        next.subjectToId = '';
      }
      return next;
    });
  };

  const updateCategoryFilter = (key, checked) => {
    setPreviewPages([]);
    setGraphError('');
    setFilters((prev) => ({
      ...prev,
      categories: {
        ...prev.categories,
        [key]: checked,
      },
    }));
  };

  const handleZoomIn = () => setScale((prev) => Math.min(prev + 0.2, 4));
  const handleZoomOut = () => setScale((prev) => Math.max(prev - 0.2, 0.4));
  const handlePrint = async () => {
    try {
      setPreviewBusy(true);
      const { pages, pageWidth, pageHeight } = await buildPagedExportAssets({
        syncPreview: true,
        exportPageSize: A4_EXPORT_PORTRAIT,
        previewPageSize: A4_PORTRAIT,
        renderScale: 2,
      });
      const popup = window.open('', '_blank', 'noopener,noreferrer,width=900,height=1100');
      if (!popup) throw new Error('Pop-up blocked by the browser.');
      popup.document.write(`<!doctype html>
<html>
  <head>
    <title>Flow Diagram Print</title>
    <style>
      body { margin: 0; padding: 24px; background: #eef3fb; font-family: "Segoe UI", Arial, sans-serif; }
      .page { width: ${pageWidth}px; min-height: ${pageHeight}px; margin: 0 auto 24px; background: #fff; box-shadow: 0 10px 28px rgba(27, 42, 73, 0.12); }
      img { display: block; width: ${pageWidth}px; height: ${pageHeight}px; }
      @media print {
        body { margin: 0; padding: 0; background: #fff; }
        .page { margin: 0; box-shadow: none; page-break-after: always; }
      }
    </style>
  </head>
  <body>
    ${pages.map((page, index) => `<div class="page"><img src="${page}" alt="Flow Diagram Page ${index + 1}" /></div>`).join('')}
    <script>
      window.onload = function () {
        setTimeout(function () {
          window.print();
        }, 150);
      };
    <\/script>
  </body>
</html>`);
      popup.document.close();
    } catch (error) {
      alert(`Print failed: ${error?.message || error}`);
    } finally {
      setPreviewBusy(false);
    }
  };

  const handleArchive = async () => {
    const payload = view === 'sunburst' ? visibleSunburstData : visibleFlowData;
    if (!payload) return;

    if (selectedCategoryCount !== CATEGORY_OPTIONS.length) {
      downloadJsonPayload(payload, `chart_${view}_filtered_${Date.now()}.json`);
      alert('Filtered JSON downloaded.');
      return;
    }

    try {
      const res = view === 'sunburst'
        ? await axios.get(`${API_BASE}/Charts/sunburst`, { params: { ...chartQueryParams, archive: true } })
        : await axios.get(`${API_BASE}/Charts/flow-program`, { params: { ...chartQueryParams, archive: true } });
      alert(`Archived to: ${res.data.archivedPath || 'N/A'}`);
    } catch {
      alert('Archive failed');
    }
  };

  const handleMouseDown = (e) => {
    const rect = graphRef.current.getBoundingClientRect();
    setDrag({ active: true, startX: e.clientX - rect.left, startY: e.clientY - rect.top, origX: pan.x, origY: pan.y });
  };

  const handleMouseMove = (e) => {
    if (!drag.active) return;
    const rect = graphRef.current.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    setPan({ x: drag.origX + (x - drag.startX), y: drag.origY + (y - drag.startY) });
  };

  const handleMouseUp = () => setDrag({ active: false, startX: 0, startY: 0, origX: 0, origY: 0 });

  const buildExportCanvas = async (renderScale = 1) => {
    const svg = graphRef.current?.querySelector('svg');
    if (!svg) {
      throw new Error('The flow diagram is not ready to export yet.');
    }
    const { width, height } = getSvgSize(svg);
    const svgStr = serializeSvgForExport(svg, width, height);
    return renderSvgToCanvas(svgStr, width, height, renderScale);
  };

  const buildPagedExportAssets = async ({
    syncPreview = false,
    exportPageSize = A4_PORTRAIT,
    previewPageSize = A4_PORTRAIT,
    renderScale = 1,
  } = {}) => {
    const canvas = await buildExportCanvas(renderScale);
    const paged = buildA4Pages(canvas, exportPageSize);
    if (syncPreview) {
      const previewPaged = (previewPageSize.width === exportPageSize.width && previewPageSize.height === exportPageSize.height)
        ? paged
        : buildA4Pages(canvas, previewPageSize);
      setPreviewPages(previewPaged.pages);
      setPreviewPageIndex(0);
      setPreviewZoom(1);
    }
    return { canvas, ...paged };
  };

  const exportPng = async () => {
    try {
      const canvas = await buildExportCanvas();
      const blob = await canvasToBlob(canvas, 'image/png');
      downloadBlob(blob, `${exportFileStem}_${Date.now()}.png`);
    } catch (error) {
      alert(`PNG export failed: ${error?.message || error}`);
    }
  };

  const generatePreviewPages = async () => {
    try {
      setPreviewBusy(true);
      await buildPagedExportAssets({ syncPreview: true });
    } catch (error) {
      alert(`Preview generation failed: ${error?.message || error}`);
    } finally {
      setPreviewBusy(false);
    }
  };

  const exportDocx = async () => {
    try {
      setPreviewBusy(true);
      const { pages, pageWidth, pageHeight } = await buildPagedExportAssets({
        syncPreview: true,
        exportPageSize: A4_EXPORT_PORTRAIT,
        previewPageSize: A4_PORTRAIT,
        renderScale: 2,
      });
      const res = await axios.post(
        `${API_BASE}/Charts/export-docx`,
        { Base64PngPages: pages, WidthPx: pageWidth, HeightPx: pageHeight },
        { responseType: 'blob' }
      );
      const blob = new Blob([res.data], { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' });
      downloadBlob(blob, `${exportFileStem}_${Date.now()}.docx`);
    } catch (error) {
      alert(`DOCX export failed: ${error?.message || error}`);
    } finally {
      setPreviewBusy(false);
    }
  };

  const exportPdf = async () => {
    try {
      setPreviewBusy(true);
      const { pages, pageWidth, pageHeight } = await buildPagedExportAssets({
        syncPreview: true,
        exportPageSize: A4_EXPORT_PORTRAIT,
        previewPageSize: A4_PORTRAIT,
        renderScale: 2,
      });
      const pdf = new jsPDF({
        orientation: 'portrait',
        unit: 'px',
        format: [pageWidth, pageHeight],
        compress: true,
        hotfixes: ['px_scaling'],
      });
      pages.forEach((page, index) => {
        if (index > 0) {
          pdf.addPage([pageWidth, pageHeight], 'portrait');
        }
        pdf.addImage(page, 'PNG', 0, 0, pageWidth, pageHeight, undefined, 'NONE');
      });
      pdf.save(`${exportFileStem}_${Date.now()}.pdf`);
    } catch (error) {
      alert(`PDF export failed: ${error?.message || error}`);
    } finally {
      setPreviewBusy(false);
    }
  };

  const persistTopicOrder = async (lessonPlanIds) => {
    if (!lessonPlanIds || lessonPlanIds.length === 0) return;
    try {
      await axios.post(`${API_BASE}/LessonPlan/reorder`, { lessonPlanIds });
      setReorderMessage('Lesson order saved.');
    } catch {
      setReorderMessage('Lesson order updated in view, but save failed.');
    }
  };

  const moveLessonWithinTopic = (subjectId, topicId, fromIndex, toIndex) => {
    if (fromIndex === toIndex) return;
    let orderedIds = [];
    setFlowData((prev) => {
      if (!prev) return prev;
      const modules = (prev.modules || []).map((module) => {
        if (module.subjectId !== subjectId) return module;
        const topics = (module.topics || []).map((topic) => {
          if (topic.topicId !== topicId) return topic;
          const items = [...(topic.lessonPlans || [])];
          const [moved] = items.splice(fromIndex, 1);
          items.splice(toIndex, 0, moved);
          orderedIds = items.map((item) => item.lessonPlanId).filter((id) => Number.isInteger(id));
          return { ...topic, lessonPlans: items };
        });
        return recomputeModule({ ...module, topics });
      });
      return { ...prev, modules };
    });
    if (orderedIds.length > 0) {
      persistTopicOrder(orderedIds);
    }
  };

  const renderFlow = () => {
    if (!visibleFlowData) return null;
    const modules = visibleFlowData.modules || [];
    const cards = [];
    modules.forEach((module) => {
      const sourceCards = (module.lessonCards && module.lessonCards.length > 0)
        ? module.lessonCards
        : (module.sessions || []).map((session, idx) => ({
          index: idx + 1,
          lpn: `LPN ${idx + 1}`,
          lessonPlanDescription: session.title || 'Lesson Plan',
          topic: `${session.topicCode || ''} ${session.topicDescription || ''}`.trim(),
          timeStart: '',
          timeEnd: '',
        }));

      sourceCards.forEach((card) => {
        cards.push({
          ...card,
          phaseId: module.phaseId,
          phaseName: module.phaseName || 'Phase',
          subjectCode: module.subjectCode || 'SUB',
          subjectDescription: module.subjectDescription || 'Subject',
        });
      });
    });

    if (cards.length === 0) {
      return (
        <svg width={1000} height={240}>
          <text x={40} y={120} fill="#5b6b8a" fontSize="16">No lesson cards to display for the current filter selection.</text>
        </svg>
      );
    }

    const columns = Math.min(4, Math.max(2, Math.ceil(Math.sqrt(cards.length))));
    const cardWidth = 250;
    const cardHeight = 140;
    const gapX = 44;
    const gapY = 52;
    const marginX = 56;
    const marginY = 56;
    const rows = Math.ceil(cards.length / columns);
    const width = marginX * 2 + columns * cardWidth + (columns - 1) * gapX;
    const height = marginY * 2 + rows * cardHeight + Math.max(0, rows - 1) * gapY + 30;

    const positioned = cards.map((card, idx) => {
      const row = Math.floor(idx / columns);
      const offset = idx % columns;
      const col = row % 2 === 0 ? offset : (columns - 1 - offset);
      const x = marginX + col * (cardWidth + gapX);
      const y = marginY + row * (cardHeight + gapY);
      return { ...card, idx, row, col, x, y };
    });

    return (
      <svg width={width} height={height} style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${scale})`, transformOrigin: '0 0' }}>
        <defs>
          <marker id="flowArrow" markerWidth="10" markerHeight="10" refX="8" refY="3.5" orient="auto">
            <polygon points="0 0, 8 3.5, 0 7" fill="#3f4f72" />
          </marker>
        </defs>

        {positioned.map((node, idx) => {
          if (idx === 0) return null;
          const prev = positioned[idx - 1];
          const fromX = prev.x + (prev.col < node.col ? cardWidth : 0);
          const fromY = prev.y + cardHeight / 2;
          const toX = node.x + (prev.col < node.col ? 0 : cardWidth);
          const toY = node.y + cardHeight / 2;
          const midX = (fromX + toX) / 2;
          return (
            <path
              key={`line-${idx}`}
              d={`M ${fromX} ${fromY} C ${midX} ${fromY}, ${midX} ${toY}, ${toX} ${toY}`}
              stroke="#3f4f72"
              strokeWidth="1.8"
              fill="none"
              markerEnd="url(#flowArrow)"
              opacity="0.85"
            />
          );
        })}

        {positioned.map((node) => {
          const phaseColor = colorFromKey(node.phaseName || node.phaseId || node.subjectCode);
          const descLines = splitLines(node.lessonPlanDescription, 28, 2);
          const footer = `${node.subjectCode}${node.topic ? ` | ${clampText(node.topic, 20)}` : ''}`;
          return (
            <g key={`card-${node.idx}`}>
              <rect x={node.x} y={node.y} width={cardWidth} height={cardHeight} rx="12" fill="#f0f2f7" stroke="#d5d9e4" />
              <rect x={node.x} y={node.y} width={cardWidth} height={30} rx="12" fill={phaseColor} />
              <text x={node.x + 14} y={node.y + 20} fill="#ffffff" fontSize="12" fontWeight="700">
                {node.lpn || `LPN ${node.idx + 1}`}
              </text>
              <circle cx={node.x + cardWidth / 2} cy={node.y - 6} r="12" fill="#ef6a50" />
              <text x={node.x + cardWidth / 2} y={node.y - 2} fill="#fff" fontSize="11" textAnchor="middle" fontWeight="700">
                {node.idx + 1}
              </text>
              {descLines.map((line, idx) => (
                <text key={`desc-${node.idx}-${idx}`} x={node.x + 12} y={node.y + 54 + idx * 16} fill="#2d3d5e" fontSize="11.5">
                  {line.toUpperCase()}
                </text>
              ))}
              <text x={node.x + 12} y={node.y + cardHeight - 16} fill="#68789d" fontSize="10">
                {footer}
              </text>
              <text x={node.x + cardWidth - 12} y={node.y + cardHeight - 16} fill="#68789d" fontSize="10" textAnchor="end">
                {node.timeStart && node.timeEnd ? `${node.timeStart}-${node.timeEnd}` : node.phaseName}
              </text>
            </g>
          );
        })}
      </svg>
    );
  };

  const renderTopicsFlow = () => {
    if (!visibleFlowData) return null;
    const modules = visibleFlowData.modules || [];
    const topicCards = modules.flatMap((module) =>
      (module.topics || []).map((topic, idx) => ({
        key: `${module.subjectId}-${topic.topicId}-${idx}`,
        subjectCode: module.subjectCode || 'SUB',
        subjectDescription: module.subjectDescription || 'Subject',
        topicCode: topic.topicCode || 'TOPIC',
        topicDescription: topic.topicDescription || 'Topic',
        lessonPlans: Array.isArray(topic.lessonPlans) ? topic.lessonPlans.length : 0,
      }))
    );

    if (topicCards.length === 0) {
      return (
        <svg width={980} height={220}>
          <text x={40} y={110} fill="#5b6b8a" fontSize="16">No topic cards to display for the current filter selection.</text>
        </svg>
      );
    }

    const cols = Math.min(4, Math.max(2, Math.ceil(Math.sqrt(topicCards.length))));
    const cardWidth = 260;
    const cardHeight = 118;
    const gapX = 26;
    const gapY = 34;
    const marginX = 34;
    const marginY = 34;
    const rows = Math.ceil(topicCards.length / cols);
    const width = marginX * 2 + cols * cardWidth + (cols - 1) * gapX;
    const height = marginY * 2 + rows * cardHeight + Math.max(0, rows - 1) * gapY + 8;
    const positioned = topicCards.map((card, idx) => {
      const row = Math.floor(idx / cols);
      const col = idx % cols;
      const x = marginX + col * (cardWidth + gapX);
      const y = marginY + row * (cardHeight + gapY);
      const accent = colorFromKey(`${card.subjectCode}-${card.topicCode}-${idx}`);
      return { ...card, idx, row, col, x, y, accent };
    });

    return (
      <svg width={width} height={height} style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${scale})`, transformOrigin: '0 0' }}>
        <defs>
          <marker id="topicsFlowArrow" markerWidth="10" markerHeight="10" refX="8" refY="3.5" orient="auto">
            <polygon points="0 0, 8 3.5, 0 7" fill="#38558a" />
          </marker>
        </defs>
        {positioned.map((card, idx) => {
          if (idx === 0) return null;
          const prev = positioned[idx - 1];
          const sameRow = prev.row === card.row;
          const startX = sameRow ? prev.x + cardWidth : prev.x + (cardWidth / 2);
          const startY = sameRow ? prev.y + (cardHeight / 2) : prev.y + cardHeight;
          const endX = sameRow ? card.x : card.x + (cardWidth / 2);
          const endY = sameRow ? card.y + (cardHeight / 2) : card.y;
          const path = sameRow
            ? `M ${startX} ${startY} C ${(startX + endX) / 2} ${startY}, ${(startX + endX) / 2} ${endY}, ${endX} ${endY}`
            : `M ${startX} ${startY} C ${startX} ${(startY + endY) / 2}, ${endX} ${(startY + endY) / 2}, ${endX} ${endY}`;
          return (
            <path
              key={`topic-line-${card.key}`}
              d={path}
              stroke={colorWithAlpha(card.accent, 0.85)}
              strokeWidth="2.2"
              strokeLinecap="round"
              strokeLinejoin="round"
              fill="none"
              markerEnd="url(#topicsFlowArrow)"
            />
          );
        })}
        {positioned.map((card) => {
          const titleLines = splitLines(`${card.topicCode} - ${card.topicDescription}`, 30, 2);
          const subject = `${card.subjectCode} - ${clampText(card.subjectDescription, 28)}`;
          return (
            <g key={card.key}>
              <rect
                x={card.x}
                y={card.y}
                width={cardWidth}
                height={cardHeight}
                rx="14"
                fill={colorWithAlpha(card.accent, 0.08)}
                stroke={colorWithAlpha(card.accent, 0.32)}
              />
              <rect x={card.x} y={card.y} width={cardWidth} height={32} rx="14" fill={card.accent} />
              {titleLines.map((line, lineIndex) => (
                <text
                  key={`${card.key}-title-${lineIndex}`}
                  x={card.x + 12}
                  y={card.y + 20 + (lineIndex * 12)}
                  fill="#fff"
                  fontSize="11.5"
                  fontWeight="700"
                >
                  {line}
                </text>
              ))}
              <rect
                x={card.x + 12}
                y={card.y + 44}
                width={cardWidth - 24}
                height={26}
                rx="7"
                fill="#ffffff"
                stroke={colorWithAlpha(card.accent, 0.16)}
              />
              <text x={card.x + 20} y={card.y + 61} fill="#304567" fontSize="10.8">{subject}</text>
              <text x={card.x + 12} y={card.y + 87} fill="#5c6f92" fontSize="10.8">
                Lesson Plans: {card.lessonPlans}
              </text>
              <text x={card.x + cardWidth - 12} y={card.y + 87} fill={card.accent} fontSize="10.4" textAnchor="end" fontWeight="700">
                Topic Flow
              </text>
            </g>
          );
        })}
      </svg>
    );
  };

  const renderCurriculumChain = () => {
    if (!visibleFlowData) return null;
    const qualification = visibleFlowData.qualification || {};
    const modules = visibleFlowData.modules || [];
    if (modules.length === 0) {
      return (
        <svg width={980} height={220}>
          <text x={40} y={110} fill="#5b6b8a" fontSize="16">No curriculum workflow nodes to display for the current filter selection.</text>
        </svg>
      );
    }

    const cols = Math.min(3, Math.max(1, Math.ceil(Math.sqrt(modules.length))));
    const subjectWidth = 300;
    const subjectHeight = 116;
    const gapX = 30;
    const gapY = 28;
    const marginX = 38;
    const startY = 148;
    const rows = Math.ceil(modules.length / cols);
    const width = marginX * 2 + cols * subjectWidth + (cols - 1) * gapX;
    const height = startY + rows * subjectHeight + Math.max(0, rows - 1) * gapY + 48;
    const qTitle = `${qualification?.number || 'Curriculum'} - ${qualification?.name || 'Qualification'}`;

    return (
      <svg width={width} height={height} style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${scale})`, transformOrigin: '0 0' }}>
        <rect x={width / 2 - 280} y={30} width={560} height={54} rx="12" fill="#1d4f80" />
        <text x={width / 2} y={62} textAnchor="middle" fill="#fff" fontSize="13" fontWeight="700">{clampText(qTitle, 70)}</text>
        {modules.map((module, idx) => {
          const row = Math.floor(idx / cols);
          const col = idx % cols;
          const x = marginX + col * (subjectWidth + gapX);
          const y = startY + row * (subjectHeight + gapY);
          const cx = x + subjectWidth / 2;
          const topics = module.topics || [];
          const topicCount = topics.length;
          const lessonCount = topics.reduce((sum, topic) => sum + ((topic.lessonPlans || []).length || 0), 0);
          const firstTopic = topicCount > 0 ? `${topics[0].topicCode || ''} ${topics[0].topicDescription || ''}`.trim() : 'No topic mapped';
          return (
            <g key={`full-${module.subjectId}-${idx}`}>
              <line x1={width / 2} y1={84} x2={cx} y2={y} stroke="#5d7399" strokeWidth="1.4" />
              <rect x={x} y={y} width={subjectWidth} height={subjectHeight} rx="11" fill="#f5f8ff" stroke="#cbd9f0" />
              <text x={x + 12} y={y + 28} fill="#223d61" fontSize="12" fontWeight="700">
                {clampText(`${module.subjectCode} - ${module.subjectDescription}`, 42)}
              </text>
              <text x={x + 12} y={y + 52} fill="#4b5f82" fontSize="11">Topics: {topicCount}</text>
              <text x={x + 122} y={y + 52} fill="#4b5f82" fontSize="11">Lesson Plans: {lessonCount}</text>
              <text x={x + 12} y={y + 78} fill="#6b7da2" fontSize="10.5">Topic Chain: {clampText(firstTopic, 36)}</text>
              <text x={x + 12} y={y + 98} fill="#6b7da2" fontSize="10.5">
                Phase: {clampText(module.phaseName || `Phase ${module.phaseId || ''}`, 30)}
              </text>
            </g>
          );
        })}
      </svg>
    );
  };

  const renderSunburst = () => {
    if (!visibleSunburstData) return null;
    const width = 1100;
    const phases = visibleSunburstData.children || [];
    const phaseWidth = 220;
    const phaseGap = 36;
    const subjectGap = 10;
    const subjectHeight = 34;
    const rootY = 40;
    const phaseY = 150;
    const totalWidth = phases.length * phaseWidth + Math.max(0, phases.length - 1) * phaseGap;
    const startX = Math.max(24, (width - totalWidth) / 2);
    let maxY = phaseY + 120;
    phases.forEach((phase) => {
      const count = (phase.children || []).length;
      const bottom = phaseY + 58 + count * (subjectHeight + subjectGap);
      if (bottom > maxY) maxY = bottom;
    });
    const height = maxY + 70;
    const cx = width / 2;
    const root = visibleSunburstData;

    if (phases.length === 0) {
      return (
        <svg width={width} height={220}>
          <text x={40} y={110} fill="#5b6b8a" fontSize="16">No phase-to-subject map to display for the current filter selection.</text>
        </svg>
      );
    }

    return (
      <svg width={width} height={height} style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${scale})`, transformOrigin: '0 0' }}>
        <rect x={cx - 240} y={rootY} width={480} height={44} rx="12" fill="#2b2d42" />
        <text x={cx} y={rootY + 28} textAnchor="middle" fill="#fff" fontSize="13" fontWeight="700">{root.name}</text>
        {phases.map((phase, pi) => {
          const phaseX = startX + pi * (phaseWidth + phaseGap);
          const phaseColor = colorFromKey(phase.name);
          const subjectsForPhase = phase.children || [];
          return (
            <g key={`phase-${pi}`}>
              <line x1={cx} y1={rootY + 44} x2={phaseX + phaseWidth / 2} y2={phaseY} stroke="#4f5d7a" strokeWidth="1.5" />
              <rect x={phaseX} y={phaseY} width={phaseWidth} height={44} rx="10" fill={phaseColor} />
              <text x={phaseX + phaseWidth / 2} y={phaseY + 27} textAnchor="middle" fill="#fff" fontSize="12" fontWeight="700">
                {phase.name}
              </text>
              {subjectsForPhase.map((subject, idx) => {
                const sy = phaseY + 58 + idx * (subjectHeight + subjectGap);
                return (
                  <g key={`subject-${pi}-${idx}`}>
                    <line x1={phaseX + phaseWidth / 2} y1={phaseY + 44} x2={phaseX + phaseWidth / 2} y2={sy} stroke="#8a94ad" strokeWidth="1.2" />
                    <rect x={phaseX + 8} y={sy} width={phaseWidth - 16} height={subjectHeight} rx="8" fill="#edf2fb" stroke="#d5dff0" />
                    <text x={phaseX + 16} y={sy + 22} fill="#2a3b5f" fontSize="11">
                      {clampText(subject.name, 30)}
                    </text>
                    <text x={phaseX + phaseWidth - 16} y={sy + 22} fill="#6a7a9b" fontSize="10" textAnchor="end">
                      {subject.value || 0} LPN
                    </text>
                  </g>
                );
              })}
            </g>
          );
        })}
      </svg>
    );
  };

  const renderLessonPlanTable = () => {
    if (view !== 'flow' || !visibleFlowData) return null;
    const modules = visibleFlowData.modules || [];
    return (
      <div style={{ marginTop: 16, border: '1px solid #c8d7f0', background: '#fff', borderRadius: 8, padding: 12 }}>
        <h3 style={{ marginTop: 0 }}>Lesson Plans (Drag To Reorder)</h3>
        <p style={{ marginTop: 0, color: '#516a9e' }}>
          Drag rows inside the same topic. Flow structure and DOCX exports update using this order.
        </p>
        {reorderMessage ? <div style={{ marginBottom: 10, color: '#1b4f9d' }}>{reorderMessage}</div> : null}
        {modules.map((module) => (
          <div key={`table-${module.subjectId}`} style={{ marginBottom: 16 }}>
            <div style={{ fontWeight: 700, marginBottom: 8 }}>
              {module.subjectCode} - {module.subjectDescription}
            </div>
            {(module.topics || []).map((topic) => (
              <div key={`table-${module.subjectId}-${topic.topicId}`} style={{ marginBottom: 10 }}>
                <div style={{ fontWeight: 600, color: '#26416b', marginBottom: 6 }}>
                  {topic.topicCode} - {topic.topicDescription}
                </div>
                <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                  <thead>
                    <tr style={{ background: '#f2f6ff' }}>
                      <th style={{ textAlign: 'left', border: '1px solid #dbe6fa', padding: '6px 8px', width: 60 }}>Order</th>
                      <th style={{ textAlign: 'left', border: '1px solid #dbe6fa', padding: '6px 8px' }}>Title</th>
                      <th style={{ textAlign: 'left', border: '1px solid #dbe6fa', padding: '6px 8px', width: 120 }}>Duration</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(topic.lessonPlans || []).map((lesson, idx) => (
                      <tr
                        key={`row-${lesson.lessonPlanId}`}
                        draggable
                        onDragStart={() => setDraggingLesson({ subjectId: module.subjectId, topicId: topic.topicId, index: idx })}
                        onDragOver={(e) => e.preventDefault()}
                        onDrop={() => {
                          if (!draggingLesson) return;
                          if (draggingLesson.subjectId !== module.subjectId || draggingLesson.topicId !== topic.topicId) return;
                          moveLessonWithinTopic(module.subjectId, topic.topicId, draggingLesson.index, idx);
                          setDraggingLesson(null);
                        }}
                        onDragEnd={() => setDraggingLesson(null)}
                        style={{ cursor: 'move' }}
                      >
                        <td style={{ border: '1px solid #dbe6fa', padding: '6px 8px' }}>{idx + 1}</td>
                        <td style={{ border: '1px solid #dbe6fa', padding: '6px 8px' }}>{lesson.title}</td>
                        <td style={{ border: '1px solid #dbe6fa', padding: '6px 8px' }}>{lesson.durationMinutes || 0} min</td>
                      </tr>
                    ))}
                    {(!topic.lessonPlans || topic.lessonPlans.length === 0) ? (
                      <tr>
                        <td colSpan={3} style={{ border: '1px solid #dbe6fa', padding: '6px 8px', color: '#8b97b7' }}>
                          No lesson plans in this topic.
                        </td>
                      </tr>
                    ) : null}
                  </tbody>
                </table>
              </div>
            ))}
          </div>
        ))}
      </div>
    );
  };

  const qualificationLabel = selectedQualification
    ? `${qNumber(selectedQualification) || '#'} - ${qDescription(selectedQualification) || 'No description'}`
    : 'Not selected';

  return (
    <div>
      <h2>Graphs &amp; Flow Diagrams</h2>

      <div style={{ marginBottom: 16, border: '1px solid #c8d7f0', background: '#fff', borderRadius: 8, padding: 12 }}>
        <h3 style={{ marginTop: 0 }}>Graph Filters</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(220px, 1fr))', gap: 10 }}>
          <label>
            Qualification
            <select
              className="mainpage-input"
              value={filters.qualificationId}
              onChange={(e) => updateFilterField('qualificationId', e.target.value)}
              disabled={filterBusy || qualifications.length === 0}
            >
              {qualifications.length === 0 ? <option value="">No qualifications found</option> : null}
              {qualifications.map((qualification) => {
                const id = qId(qualification);
                return (
                  <option key={id} value={id}>
                    {qNumber(qualification) || id} - {qDescription(qualification) || 'No description'}
                  </option>
                );
              })}
            </select>
          </label>
          <label>
            From Subject
            <select
              className="mainpage-input"
              value={filters.subjectFromId}
              onChange={(e) => updateFilterField('subjectFromId', e.target.value)}
              disabled={subjects.length === 0}
            >
              {subjects.length === 0 ? <option value="">No subjects</option> : null}
              {subjects.map((subject) => (
                <option key={subjectIdOf(subject)} value={subjectIdOf(subject)}>
                  {subjectCodeOf(subject) || 'SUBJECT'} - {subjectDescriptionOf(subject) || 'No description'}
                </option>
              ))}
            </select>
          </label>
          <label>
            To Subject
            <select
              className="mainpage-input"
              value={filters.subjectToId}
              onChange={(e) => updateFilterField('subjectToId', e.target.value)}
              disabled={subjects.length === 0}
            >
              {subjects.length === 0 ? <option value="">No subjects</option> : null}
              {subjects.map((subject) => (
                <option key={subjectIdOf(subject)} value={subjectIdOf(subject)}>
                  {subjectCodeOf(subject) || 'SUBJECT'} - {subjectDescriptionOf(subject) || 'No description'}
                </option>
              ))}
            </select>
          </label>
        </div>

        <div style={{ marginTop: 12 }}>
          <strong>Main Sub Categories</strong>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, marginTop: 8 }}>
            {categoryCounts.map((option) => (
              <label
                key={option.key}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 6,
                  padding: '6px 10px',
                  border: '1px solid #d8e1ea',
                  borderRadius: 8,
                  background: filters.categories?.[option.key] ? '#f6fbff' : '#fafafa',
                }}
              >
                <input
                  type="checkbox"
                  checked={Boolean(filters.categories?.[option.key])}
                  onChange={(e) => updateCategoryFilter(option.key, e.target.checked)}
                />
                <span>{option.label} ({option.count})</span>
              </label>
            ))}
          </div>
        </div>

        <div style={{ marginTop: 10, color: '#355' }}>
          <strong>Lookup Parameters:</strong> {qualificationLabel}
        </div>
        <div style={{ marginTop: 6, color: '#355' }}>
          <strong>Active Range:</strong> {selectedSubjectFrom ? subjectCodeOf(selectedSubjectFrom) : '-'} to {selectedSubjectTo ? subjectCodeOf(selectedSubjectTo) : '-'}
          {' | '}
          <strong>Visible Subjects:</strong> {visibleRangeSubjects} of {totalRangeSubjects}
        </div>
        <div style={{ marginTop: 6, color: '#516a9e', fontSize: 13 }}>
          Topics Flow is the default view. Set From Subject and To Subject to the same chapter when you want a single-subject PNG or DOCX figure.
        </div>
        {selectedCategoryCount === 0 ? (
          <div style={{ marginTop: 8, color: '#8b1e1e' }}>
            Select at least one sub category to render the diagram.
          </div>
        ) : null}
        {filterBusy ? <div style={{ marginTop: 8, color: '#355' }}>Loading filter options...</div> : null}
        {filterError ? <div style={{ marginTop: 8, color: '#b00020' }}>{filterError}</div> : null}
        {graphError ? <div style={{ marginTop: 8, color: '#b00020' }}>{graphError}</div> : null}
      </div>

      <div style={{ marginBottom: 16 }}>
        <select value={view} onChange={(e) => setView(e.target.value)} style={{ marginRight: 12 }}>
          <option value="topics">Topics Flow</option>
          <option value="flow">Lesson Plan Flow</option>
          <option value="full">Curriculum Full Flow</option>
          <option value="sunburst">Phase to Subject Map</option>
        </select>
        <button onClick={handleZoomIn}>Zoom In</button>
        <button onClick={handleZoomOut}>Zoom Out</button>
        <button onClick={handlePrint}>Print</button>
        <button onClick={handleArchive} disabled={!hasVisibleGraphData || graphBusy}>Archive JSON</button>
        <button onClick={exportPng} disabled={!hasVisibleGraphData || graphBusy}>Export PNG</button>
        <button onClick={exportPdf} disabled={previewBusy || graphBusy || !hasVisibleGraphData}>Export PDF</button>
        <button onClick={generatePreviewPages} disabled={previewBusy || graphBusy || !hasVisibleGraphData}>Preview A4 Pages</button>
        <button onClick={exportDocx} disabled={previewBusy || graphBusy || !hasVisibleGraphData}>Export DOCX</button>
      </div>

      <div
        ref={graphRef}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        style={{ border: '1px solid #23395d', minHeight: 300, background: '#f4f8ff', marginBottom: 16, overflow: 'auto', cursor: drag.active ? 'grabbing' : 'grab' }}
      >
        {graphBusy ? (
          <div style={{ padding: 24, color: '#355' }}>Loading graph data...</div>
        ) : (
          view === 'sunburst'
            ? renderSunburst()
            : view === 'topics'
              ? renderTopicsFlow()
              : view === 'full'
                ? renderCurriculumChain()
                : renderFlow()
        )}
      </div>

      {renderLessonPlanTable()}

      <div style={{ marginTop: 16, border: '1px solid #c8d7f0', background: '#fff', borderRadius: 8, padding: 12 }}>
        <h3 style={{ marginTop: 0 }}>A4 Portrait Preview</h3>
        <div style={{ marginBottom: 10 }}>
          <button onClick={() => setPreviewZoom((zoom) => Math.max(0.4, zoom - 0.1))} disabled={!previewImage}>Preview Zoom Out</button>
          <button onClick={() => setPreviewZoom((zoom) => Math.min(2.5, zoom + 0.1))} disabled={!previewImage}>Preview Zoom In</button>
          <button onClick={() => setPreviewPageIndex((index) => Math.max(0, index - 1))} disabled={!previewImage || previewPageIndex === 0}>Prev Page</button>
          <button onClick={() => setPreviewPageIndex((index) => Math.min(previewPages.length - 1, index + 1))} disabled={!previewImage || previewPageIndex >= previewPages.length - 1}>Next Page</button>
          <span style={{ marginLeft: 8, color: '#38558a' }}>
            {previewImage ? `Page ${previewPageIndex + 1} of ${previewPages.length}` : 'No preview generated yet.'}
          </span>
        </div>
        <div style={{ border: '1px solid #dbe6fa', background: '#f8fbff', minHeight: 260, overflow: 'auto', padding: 12 }}>
          {previewImage ? (
            <img
              src={previewImage}
              alt={`Preview Page ${previewPageIndex + 1}`}
              style={{
                width: `${Math.round(A4_PORTRAIT.width * previewZoom)}px`,
                height: `${Math.round(A4_PORTRAIT.height * previewZoom)}px`,
                display: 'block',
                margin: '0 auto',
                border: '1px solid #cfdbf5',
                background: '#fff',
              }}
            />
          ) : (
            <div style={{ color: '#8b97b7' }}>Click `Preview A4 Pages` to generate page previews.</div>
          )}
        </div>
      </div>
    </div>
  );
};

export default GraphsPage;
