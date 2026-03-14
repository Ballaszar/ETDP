import React, { useEffect, useMemo, useRef, useState } from 'react';
import axios from 'axios';

const API_BASE = '/api';
const A4_PORTRAIT = { width: 794, height: 1123 };
const PHASE_PALETTE = ['#2b2d42', '#006d77', '#3a86ff', '#8338ec', '#ff006e', '#fb5607', '#2a9d8f', '#264653'];

const colorFromKey = (key) => {
  const text = `${key ?? ''}`;
  let sum = 0;
  for (let i = 0; i < text.length; i += 1) sum += text.charCodeAt(i);
  return PHASE_PALETTE[sum % PHASE_PALETTE.length];
};

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

const serializeSvgForExport = (svg) => {
  const clone = svg.cloneNode(true);
  clone.style.transform = '';
  clone.style.transformOrigin = '';
  clone.style.width = '';
  clone.style.height = '';
  return new XMLSerializer().serializeToString(clone);
};

const renderSvgToCanvas = (svgString, width, height) =>
  new Promise((resolve, reject) => {
    const img = new Image();
    const svgBlob = new Blob([svgString], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(svgBlob);
    img.onload = () => {
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const ctx = canvas.getContext('2d');
      ctx.fillStyle = '#ffffff';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(img, 0, 0, width, height);
      URL.revokeObjectURL(url);
      resolve(canvas);
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error('Failed to render SVG image'));
    };
    img.src = url;
  });

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

const buildA4Pages = (canvas) => {
  const pageWidth = A4_PORTRAIT.width;
  const pageHeight = A4_PORTRAIT.height;
  const scale = pageWidth / canvas.width;
  const scaledHeight = Math.max(1, Math.ceil(canvas.height * scale));
  const scaledCanvas = document.createElement('canvas');
  scaledCanvas.width = pageWidth;
  scaledCanvas.height = scaledHeight;
  const scaledCtx = scaledCanvas.getContext('2d');
  scaledCtx.fillStyle = '#ffffff';
  scaledCtx.fillRect(0, 0, scaledCanvas.width, scaledCanvas.height);
  scaledCtx.drawImage(canvas, 0, 0, pageWidth, scaledHeight);

  const pages = [];
  for (let y = 0; y < scaledHeight; y += pageHeight) {
    const pageCanvas = document.createElement('canvas');
    pageCanvas.width = pageWidth;
    pageCanvas.height = pageHeight;
    const pageCtx = pageCanvas.getContext('2d');
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
  const [view, setView] = useState('flow');
  const [flowData, setFlowData] = useState(null);
  const [sunburstData, setSunburstData] = useState(null);
  const [scale, setScale] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [drag, setDrag] = useState({ active: false, startX: 0, startY: 0, origX: 0, origY: 0 });
  const [previewPages, setPreviewPages] = useState([]);
  const [previewPageIndex, setPreviewPageIndex] = useState(0);
  const [previewZoom, setPreviewZoom] = useState(1);
  const [previewBusy, setPreviewBusy] = useState(false);
  const [draggingLesson, setDraggingLesson] = useState(null);
  const [reorderMessage, setReorderMessage] = useState('');

  useEffect(() => {
    const load = async () => {
      try {
        const f = await axios.get(`${API_BASE}/Charts/flow-program`);
        setFlowData(normalizeFlowData(f.data.data));
        const s = await axios.get(`${API_BASE}/Charts/sunburst`);
        setSunburstData(s.data.data);
      } catch {
        setReorderMessage('Failed to load graph data.');
      }
    };
    load();
  }, []);

  const previewImage = useMemo(() => previewPages[previewPageIndex] || null, [previewPages, previewPageIndex]);

  const handleZoomIn = () => setScale((prev) => Math.min(prev + 0.2, 4));
  const handleZoomOut = () => setScale((prev) => Math.max(prev - 0.2, 0.4));
  const handlePrint = () => window.print();

  const handleArchive = async () => {
    try {
      const res = view === 'sunburst'
        ? await axios.get(`${API_BASE}/Charts/sunburst?archive=true`)
        : await axios.get(`${API_BASE}/Charts/flow-program?archive=true`);
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

  const buildExportCanvas = async () => {
    const svg = graphRef.current?.querySelector('svg');
    if (!svg) return null;
    const { width, height } = getSvgSize(svg);
    const svgStr = serializeSvgForExport(svg);
    return renderSvgToCanvas(svgStr, width, height);
  };

  const exportPng = async () => {
    try {
      const canvas = await buildExportCanvas();
      if (!canvas) return;
      const pngUrl = canvas.toDataURL('image/png');
      const a = document.createElement('a');
      a.href = pngUrl;
      a.download = `chart_${view}_${Date.now()}.png`;
      a.click();
    } catch {
      alert('PNG export failed');
    }
  };

  const generatePreviewPages = async () => {
    try {
      setPreviewBusy(true);
      const canvas = await buildExportCanvas();
      if (!canvas) return;
      const { pages } = buildA4Pages(canvas);
      setPreviewPages(pages);
      setPreviewPageIndex(0);
      setPreviewZoom(1);
    } catch {
      alert('Preview generation failed');
    } finally {
      setPreviewBusy(false);
    }
  };

  const exportDocx = async () => {
    try {
      setPreviewBusy(true);
      const canvas = await buildExportCanvas();
      if (!canvas) return;
      const { pages, pageWidth, pageHeight } = buildA4Pages(canvas);
      setPreviewPages(pages);
      setPreviewPageIndex(0);
      const res = await axios.post(
        `${API_BASE}/Charts/export-docx`,
        { Base64PngPages: pages, WidthPx: pageWidth, HeightPx: pageHeight },
        { responseType: 'blob' }
      );
      const blob = new Blob([res.data], { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' });
      const link = document.createElement('a');
      const blobUrl = URL.createObjectURL(blob);
      link.href = blobUrl;
      link.download = `chart_${view}_${Date.now()}.docx`;
      link.click();
      URL.revokeObjectURL(blobUrl);
    } catch {
      alert('DOCX export failed');
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
          orderedIds = items.map((x) => x.lessonPlanId).filter((id) => Number.isInteger(id));
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
    if (!flowData) return null;
    const modules = flowData.modules || [];
    const cards = [];
    modules.forEach((module) => {
      const sourceCards = (module.lessonCards && module.lessonCards.length > 0)
        ? module.lessonCards
        : (module.sessions || []).map((s, idx) => ({
          index: idx + 1,
          lpn: `LPN ${idx + 1}`,
          lessonPlanDescription: s.title || 'Lesson Plan',
          topic: `${s.topicCode || ''} ${s.topicDescription || ''}`.trim(),
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
          <text x={40} y={120} fill="#5b6b8a" fontSize="16">No lesson cards to display for this qualification.</text>
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
              {descLines.map((line, i) => (
                <text key={`desc-${node.idx}-${i}`} x={node.x + 12} y={node.y + 54 + i * 16} fill="#2d3d5e" fontSize="11.5">
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
    if (!flowData) return null;
    const modules = flowData.modules || [];
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
          <text x={40} y={110} fill="#5b6b8a" fontSize="16">No topic cards to display for this qualification.</text>
        </svg>
      );
    }

    const cols = Math.min(4, Math.max(2, Math.ceil(Math.sqrt(topicCards.length))));
    const cardWidth = 260;
    const cardHeight = 110;
    const gapX = 26;
    const gapY = 22;
    const marginX = 34;
    const marginY = 28;
    const rows = Math.ceil(topicCards.length / cols);
    const width = marginX * 2 + cols * cardWidth + (cols - 1) * gapX;
    const height = marginY * 2 + rows * cardHeight + Math.max(0, rows - 1) * gapY;

    return (
      <svg width={width} height={height} style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${scale})`, transformOrigin: '0 0' }}>
        {topicCards.map((card, idx) => {
          const row = Math.floor(idx / cols);
          const col = idx % cols;
          const x = marginX + col * (cardWidth + gapX);
          const y = marginY + row * (cardHeight + gapY);
          const title = `${card.topicCode} - ${clampText(card.topicDescription, 30)}`;
          const subject = `${card.subjectCode} - ${clampText(card.subjectDescription, 26)}`;
          return (
            <g key={card.key}>
              <rect x={x} y={y} width={cardWidth} height={cardHeight} rx="10" fill="#f5f8ff" stroke="#ccdaf0" />
              <rect x={x} y={y} width={cardWidth} height={28} rx="10" fill="#2d5d9b" />
              <text x={x + 10} y={y + 18} fill="#fff" fontSize="11.5" fontWeight="700">{title}</text>
              <text x={x + 10} y={y + 54} fill="#2f4262" fontSize="11">{subject}</text>
              <text x={x + 10} y={y + 78} fill="#5f7294" fontSize="10.5">Lesson Plans: {card.lessonPlans}</text>
            </g>
          );
        })}
      </svg>
    );
  };

  const renderCurriculumChain = () => {
    if (!flowData) return null;
    const qualification = flowData.qualification || {};
    const modules = flowData.modules || [];
    if (modules.length === 0) {
      return (
        <svg width={980} height={220}>
          <text x={40} y={110} fill="#5b6b8a" fontSize="16">No curriculum workflow nodes to display.</text>
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
          const lessonCount = topics.reduce((sum, t) => sum + ((t.lessonPlans || []).length || 0), 0);
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
    if (!sunburstData) return null;
    const width = 1100;
    const phases = sunburstData.children || [];
    const phaseWidth = 220;
    const phaseGap = 36;
    const subjectGap = 10;
    const subjectHeight = 34;
    const rootY = 40;
    const phaseY = 150;
    const totalWidth = phases.length * phaseWidth + Math.max(0, phases.length - 1) * phaseGap;
    const startX = Math.max(24, (width - totalWidth) / 2);
    let maxY = phaseY + 120;
    phases.forEach((p) => {
      const count = (p.children || []).length;
      const bottom = phaseY + 58 + count * (subjectHeight + subjectGap);
      if (bottom > maxY) maxY = bottom;
    });
    const height = maxY + 70;
    const cx = width / 2;
    const root = sunburstData;
    return (
      <svg width={width} height={height} style={{ transform: `translate(${pan.x}px, ${pan.y}px) scale(${scale})`, transformOrigin: '0 0' }}>
        <rect x={cx - 240} y={rootY} width={480} height={44} rx="12" fill="#2b2d42" />
        <text x={cx} y={rootY + 28} textAnchor="middle" fill="#fff" fontSize="13" fontWeight="700">{root.name}</text>
        {phases.map((phase, pi) => {
          const phaseX = startX + pi * (phaseWidth + phaseGap);
          const phaseColor = colorFromKey(phase.name);
          const subjects = phase.children || [];
          return (
            <g key={`phase-${pi}`}>
              <line x1={cx} y1={rootY + 44} x2={phaseX + phaseWidth / 2} y2={phaseY} stroke="#4f5d7a" strokeWidth="1.5" />
              <rect x={phaseX} y={phaseY} width={phaseWidth} height={44} rx="10" fill={phaseColor} />
              <text x={phaseX + phaseWidth / 2} y={phaseY + 27} textAnchor="middle" fill="#fff" fontSize="12" fontWeight="700">
                {phase.name}
              </text>
              {subjects.map((subject, si) => {
                const sy = phaseY + 58 + si * (subjectHeight + subjectGap);
                return (
                  <g key={`subject-${pi}-${si}`}>
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
    if (view !== 'flow' || !flowData) return null;
    const modules = flowData.modules || [];
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

  return (
    <div>
      <h2>Graphs & Flow Diagrams</h2>
      <div style={{ marginBottom: 16 }}>
        <select value={view} onChange={(e) => setView(e.target.value)} style={{ marginRight: 12 }}>
          <option value="flow">Lesson Plan Flow</option>
          <option value="topics">Topics Flow</option>
          <option value="full">Curriculum Full Flow</option>
          <option value="sunburst">Phase to Subject Map</option>
        </select>
        <button onClick={handleZoomIn}>Zoom In</button>
        <button onClick={handleZoomOut}>Zoom Out</button>
        <button onClick={handlePrint}>Print</button>
        <button onClick={handleArchive}>Archive JSON</button>
        <button onClick={exportPng}>Export PNG</button>
        <button onClick={generatePreviewPages} disabled={previewBusy}>Preview A4 Pages</button>
        <button onClick={exportDocx} disabled={previewBusy}>Export DOCX</button>
      </div>
      <div
        ref={graphRef}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseUp}
        style={{ border: '1px solid #23395d', minHeight: 300, background: '#f4f8ff', marginBottom: 16, overflow: 'auto', cursor: drag.active ? 'grabbing' : 'grab' }}
      >
        {view === 'sunburst' ? renderSunburst() : view === 'topics' ? renderTopicsFlow() : view === 'full' ? renderCurriculumChain() : renderFlow()}
      </div>

      {renderLessonPlanTable()}

      <div style={{ marginTop: 16, border: '1px solid #c8d7f0', background: '#fff', borderRadius: 8, padding: 12 }}>
        <h3 style={{ marginTop: 0 }}>A4 Portrait Preview</h3>
        <div style={{ marginBottom: 10 }}>
          <button onClick={() => setPreviewZoom((z) => Math.max(0.4, z - 0.1))} disabled={!previewImage}>Preview Zoom Out</button>
          <button onClick={() => setPreviewZoom((z) => Math.min(2.5, z + 0.1))} disabled={!previewImage}>Preview Zoom In</button>
          <button onClick={() => setPreviewPageIndex((i) => Math.max(0, i - 1))} disabled={!previewImage || previewPageIndex === 0}>Prev Page</button>
          <button onClick={() => setPreviewPageIndex((i) => Math.min(previewPages.length - 1, i + 1))} disabled={!previewImage || previewPageIndex >= previewPages.length - 1}>Next Page</button>
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
