import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import './LecturerAssistancePage.css';

const DATA_PATH = '/data/lecturer-assistant-links.json';

const toText = (value) => String(value || '').trim();

const normalizeItem = (raw = {}) => ({
  excelRow: Number(raw.excelRow || 0),
  qualificationCode: toText(raw.qualificationCode),
  qualificationDescription: toText(raw.qualificationDescription),
  subjectCode: toText(raw.subjectCode),
  subjectDescription: toText(raw.subjectDescription),
  moduleCode: toText(raw.moduleCode),
  topicCode: toText(raw.topicCode),
  topicName: toText(raw.topicName),
  assessmentCriterionCode: toText(raw.assessmentCriterionCode),
  assessmentCriterion: toText(raw.assessmentCriterion),
  lpn: toText(raw.lpn),
  lessonPlanTitle: toText(raw.lessonPlanTitle),
  bloomLevel: toText(raw.bloomLevel),
  youtubeUrls: Array.isArray(raw.youtubeUrls) ? raw.youtubeUrls.map(toText).filter(Boolean) : [],
  openSourceUrls: Array.isArray(raw.openSourceUrls) ? raw.openSourceUrls.map(toText).filter(Boolean) : [],
  hasYoutube: Boolean(raw.hasYoutube),
  hasOpenSource: Boolean(raw.hasOpenSource)
});

const sortByCodeThenName = (a, b, codeKey, nameKey) => {
  const codeCompare = String(a[codeKey] || '').localeCompare(String(b[codeKey] || ''), undefined, { numeric: true, sensitivity: 'base' });
  if (codeCompare !== 0) return codeCompare;
  return String(a[nameKey] || '').localeCompare(String(b[nameKey] || ''), undefined, { numeric: true, sensitivity: 'base' });
};

export default function LecturerAssistancePage() {
  const navigate = useNavigate();
  const { qualificationId } = useQualification() || { qualificationId: null };

  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [generatedAtUtc, setGeneratedAtUtc] = useState('');

  const [qualificationFilter, setQualificationFilter] = useState('');
  const [subjectFilter, setSubjectFilter] = useState('');
  const [topicFilter, setTopicFilter] = useState('');
  const [query, setQuery] = useState('');
  const [onlyYoutube, setOnlyYoutube] = useState(false);
  const [onlyOpenSource, setOnlyOpenSource] = useState(false);
  const [qualificationPinned, setQualificationPinned] = useState(false);

  useEffect(() => {
    let active = true;

    const load = async () => {
      setLoading(true);
      setError('');
      try {
        const res = await fetch(DATA_PATH, { cache: 'no-cache' });
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        if (!active) return;
        const list = (Array.isArray(data?.items) ? data.items : []).map(normalizeItem);
        setItems(list);
        setGeneratedAtUtc(toText(data?.generatedAtUtc));
      } catch (e) {
        if (!active) return;
        setError(`Failed to load lecturer assistance dataset: ${e?.message || e}`);
      } finally {
        if (active) setLoading(false);
      }
    };

    load();
    return () => { active = false; };
  }, []);

  useEffect(() => {
    let active = true;

    const applyQualificationContext = async () => {
      const selectedId = Number(qualificationId || 0);
      if (!selectedId || qualificationPinned) return;
      try {
        const res = await fetch(`/api/Qualification/${selectedId}`);
        if (!res.ok) return;
        const json = await res.json();
        if (!active) return;
        const qCode = toText(json?.qualificationNumber ?? json?.QualificationNumber);
        if (qCode) {
          setQualificationFilter((prev) => prev || qCode);
        }
      } catch {
        // Ignore and leave manual filter selection.
      }
    };

    applyQualificationContext();
    return () => { active = false; };
  }, [qualificationId, qualificationPinned]);

  const qualificationOptions = useMemo(() => {
    const map = new Map();
    for (const item of items) {
      const code = item.qualificationCode;
      if (!code) continue;
      if (!map.has(code)) {
        map.set(code, {
          qualificationCode: code,
          qualificationDescription: item.qualificationDescription
        });
      }
    }
    return Array.from(map.values()).sort((a, b) => sortByCodeThenName(a, b, 'qualificationCode', 'qualificationDescription'));
  }, [items]);

  const subjectOptions = useMemo(() => {
    const map = new Map();
    for (const item of items) {
      if (qualificationFilter && item.qualificationCode !== qualificationFilter) continue;
      const key = item.subjectDescription || item.subjectCode;
      if (!key) continue;
      if (!map.has(key)) {
        map.set(key, {
          subjectCode: item.subjectDescription,
          subjectDescription: item.subjectCode
        });
      }
    }
    return Array.from(map.values()).sort((a, b) => sortByCodeThenName(a, b, 'subjectCode', 'subjectDescription'));
  }, [items, qualificationFilter]);

  const topicOptions = useMemo(() => {
    const map = new Map();
    for (const item of items) {
      if (qualificationFilter && item.qualificationCode !== qualificationFilter) continue;
      if (subjectFilter && item.subjectDescription !== subjectFilter) continue;
      const key = item.topicCode || item.topicName;
      if (!key) continue;
      if (!map.has(key)) {
        map.set(key, {
          topicCode: item.topicCode,
          topicName: item.topicName
        });
      }
    }
    return Array.from(map.values()).sort((a, b) => sortByCodeThenName(a, b, 'topicCode', 'topicName'));
  }, [items, qualificationFilter, subjectFilter]);

  const filteredItems = useMemo(() => {
    const term = query.trim().toLowerCase();
    return items
      .filter((item) => !qualificationFilter || item.qualificationCode === qualificationFilter)
      .filter((item) => !subjectFilter || item.subjectDescription === subjectFilter)
      .filter((item) => !topicFilter || item.topicCode === topicFilter)
      .filter((item) => !onlyYoutube || item.hasYoutube)
      .filter((item) => !onlyOpenSource || item.hasOpenSource)
      .filter((item) => {
        if (!term) return true;
        const haystack = [
          item.subjectDescription,
          item.subjectCode,
          item.topicCode,
          item.topicName,
          item.lpn,
          item.lessonPlanTitle,
          item.assessmentCriterionCode,
          item.assessmentCriterion,
          item.bloomLevel,
          ...item.youtubeUrls,
          ...item.openSourceUrls
        ].join(' ').toLowerCase();
        return haystack.includes(term);
      })
      .sort((a, b) => {
        const subjectCmp = a.subjectDescription.localeCompare(b.subjectDescription);
        if (subjectCmp !== 0) return subjectCmp;
        const topicCmp = a.topicCode.localeCompare(b.topicCode);
        if (topicCmp !== 0) return topicCmp;
        return a.lpn.localeCompare(b.lpn);
      });
  }, [items, qualificationFilter, subjectFilter, topicFilter, onlyYoutube, onlyOpenSource, query]);

  const summary = useMemo(() => {
    const youtube = filteredItems.reduce((sum, row) => sum + row.youtubeUrls.length, 0);
    const open = filteredItems.reduce((sum, row) => sum + row.openSourceUrls.length, 0);
    return {
      rows: filteredItems.length,
      youtube,
      open
    };
  }, [filteredItems]);

  const resetFilters = () => {
    setQualificationFilter('');
    setSubjectFilter('');
    setTopicFilter('');
    setQuery('');
    setOnlyYoutube(false);
    setOnlyOpenSource(false);
    setQualificationPinned(false);
  };

  const formatGeneratedAt = () => {
    if (!generatedAtUtc) return '-';
    const parsed = new Date(generatedAtUtc);
    if (Number.isNaN(parsed.getTime())) return generatedAtUtc;
    return parsed.toLocaleString();
  };

  return (
    <div className="page-container la-page">
      <h2>Lecturer Assistance Hub</h2>
      <p>
        This hub links your core lecturer tools and all referenced YouTube/Open Source learning URLs.
        Slide Show Creator and Text-to-Video stay on their own pages and are launched from here.
      </p>

      <section className="la-panel">
        <div className="la-quick-grid">
          <article className="la-quick-card">
            <h3>Slide Show Creator</h3>
            <p>Open PowerPoint slide generation for qualification and topic outputs.</p>
            <button type="button" onClick={() => navigate('/powerpoint-slides-export')}>Open Slide Show Creator</button>
          </article>
          <article className="la-quick-card">
            <h3>Visual Slide Assets</h3>
            <p>Use local image slides (no cloud/API). Place images in <code>Imports/SlideAssets</code> or upload to the library with topic metadata.</p>
            <button type="button" onClick={() => navigate('/powerpoint-slides-export')}>Open Visual Slide Mode</button>
          </article>
          <article className="la-quick-card">
            <h3>WAN 2.1 Studio</h3>
            <p>Open a dedicated WAN 2.1 page and pass Topic Description directly as parameter input.</p>
            <button
              type="button"
              onClick={() => navigate('/wan-2-1', {
                state: Number(qualificationId || 0) > 0 ? { qualificationId: Number(qualificationId || 0) } : undefined
              })}
            >
              Open WAN 2.1 Studio
            </button>
          </article>
        </div>
      </section>

      <section className="la-panel">
        <div className="la-toolbar-header">
          <h3>Resource Library</h3>
          <div className="la-meta">Dataset generated: {formatGeneratedAt()}</div>
        </div>

        <div className="la-filter-grid">
          <label>
            Qualification Code
            <select
              className="mainpage-input"
              value={qualificationFilter}
              onChange={(e) => {
                setQualificationFilter(e.target.value);
                setQualificationPinned(true);
                setSubjectFilter('');
                setTopicFilter('');
              }}
            >
              <option value="">All qualifications</option>
              {qualificationOptions.map((q) => (
                <option key={q.qualificationCode} value={q.qualificationCode}>
                  {q.qualificationCode} - {q.qualificationDescription || 'No description'}
                </option>
              ))}
            </select>
          </label>

          <label>
            Subject Code
            <select
              className="mainpage-input"
              value={subjectFilter}
              onChange={(e) => {
                setSubjectFilter(e.target.value);
                setTopicFilter('');
              }}
            >
              <option value="">All subjects</option>
              {subjectOptions.map((s) => (
                <option key={s.subjectCode || s.subjectDescription} value={s.subjectCode}>
                  {s.subjectCode} - {s.subjectDescription}
                </option>
              ))}
            </select>
          </label>

          <label>
            Topic Code
            <select
              className="mainpage-input"
              value={topicFilter}
              onChange={(e) => setTopicFilter(e.target.value)}
            >
              <option value="">All topics</option>
              {topicOptions.map((t) => (
                <option key={t.topicCode || t.topicName} value={t.topicCode}>
                  {t.topicCode || '-'} - {t.topicName || 'No topic name'}
                </option>
              ))}
            </select>
          </label>

          <label>
            Search
            <input
              className="mainpage-input"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search topic, LPN, lesson, criterion, URL"
            />
          </label>
        </div>

        <div className="la-toggle-row">
          <label className="la-check">
            <input type="checkbox" checked={onlyYoutube} onChange={(e) => setOnlyYoutube(e.target.checked)} />
            Only rows with YouTube URLs
          </label>
          <label className="la-check">
            <input type="checkbox" checked={onlyOpenSource} onChange={(e) => setOnlyOpenSource(e.target.checked)} />
            Only rows with Open Source URLs
          </label>
          <button type="button" onClick={resetFilters}>Reset Filters</button>
        </div>

        <div className="la-summary">
          <span><strong>Rows:</strong> {summary.rows}</span>
          <span><strong>YouTube Links:</strong> {summary.youtube}</span>
          <span><strong>Open Source Links:</strong> {summary.open}</span>
        </div>

        {loading ? <div className="la-status">Loading lecturer resource links...</div> : null}
        {error ? <div className="la-error">{error}</div> : null}

        {!loading && !error ? (
          <div className="la-table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Subject</th>
                  <th>Topic / LPN</th>
                  <th>Lesson</th>
                  <th>Bloom</th>
                  <th>YouTube</th>
                  <th>Open Source</th>
                </tr>
              </thead>
              <tbody>
                {filteredItems.map((row) => (
                  <tr key={`${row.excelRow}-${row.topicCode}-${row.lpn}`}>
                    <td>
                      <div><strong>{row.subjectDescription || '-'}</strong></div>
                      <div>{row.subjectCode || '-'}</div>
                      <div className="la-muted">Module: {row.moduleCode || '-'}</div>
                    </td>
                    <td>
                      <div><strong>{row.topicCode || '-'}</strong> - {row.topicName || '-'}</div>
                      <div>{row.lpn || '-'}</div>
                      <div className="la-muted">{row.assessmentCriterionCode || '-'} | Row {row.excelRow}</div>
                    </td>
                    <td>
                      <div>{row.lessonPlanTitle || '-'}</div>
                      <div className="la-muted">{row.assessmentCriterion || '-'}</div>
                    </td>
                    <td>{row.bloomLevel || '-'}</td>
                    <td>
                      {row.youtubeUrls.length ? (
                        <div className="la-link-list">
                          {row.youtubeUrls.map((url) => (
                            <a key={url} href={url} target="_blank" rel="noreferrer">{url}</a>
                          ))}
                        </div>
                      ) : <span className="la-muted">None</span>}
                    </td>
                    <td>
                      {row.openSourceUrls.length ? (
                        <div className="la-link-list">
                          {row.openSourceUrls.map((url) => (
                            <a key={url} href={url} target="_blank" rel="noreferrer">{url}</a>
                          ))}
                        </div>
                      ) : <span className="la-muted">None</span>}
                    </td>
                  </tr>
                ))}
                {filteredItems.length === 0 ? (
                  <tr>
                    <td colSpan={6}>No resources match the current filters.</td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        ) : null}
      </section>
    </div>
  );
}
