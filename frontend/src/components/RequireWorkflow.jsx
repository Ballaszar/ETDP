import React, { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import { WORKFLOW_STEP_META, getMissingPrerequisites } from '../utils/workflowPrerequisites';

const toArray = (value) => {
  if (!value) return [];
  return Array.isArray(value) ? value : [value];
};

const fetchList = async (url) => {
  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) throw new Error(`${url} (${res.status})`);
  const json = await res.json();
  return toArray(json);
};

const safeFetchList = async (url) => {
  try {
    return await fetchList(url);
  } catch {
    return [];
  }
};

const safeFetchListWithMeta = async (url) => {
  try {
    const items = await fetchList(url);
    return { items, error: null };
  } catch (e) {
    return { items: [], error: e?.message || 'request failed' };
  }
};

const getQualificationIdFromItem = (item) => {
  return Number(item?.qualificationId ?? item?.QualificationId ?? item?.qualificationsId ?? item?.QualificationsId ?? 0);
};

const getCountForQualification = (items, qualificationId) => {
  const qid = Number(qualificationId || 0);
  if (!qid) return 0;
  return toArray(items).filter((x) => getQualificationIdFromItem(x) === qid).length;
};

const getToolkitCountForQualification = (items, qualificationId) => {
  return getCountForQualification(items, qualificationId);
};

const currentPageLabel = (pageKey) => WORKFLOW_STEP_META[pageKey]?.label || pageKey;

const normalizeQualificationId = async (rawValue) => {
  const raw = String(rawValue ?? '').trim();
  const numeric = Number(raw || 0);

  if (numeric > 0) {
    const probe = await fetch(`/api/Qualification/${numeric}`, { cache: 'no-store' });
    if (probe.ok) return numeric;
    if (probe.status !== 404) return numeric;
  }

  if (!raw) return 0;

  const search = await fetch(`/api/Qualification/search?text=${encodeURIComponent(raw)}`, { cache: 'no-store' });
  if (!search.ok) {
    if (numeric > 0) {
      try {
        const allRes = await fetch('/api/Qualification', { cache: 'no-store' });
        if (allRes.ok) {
          const all = toArray(await allRes.json());
          const exactAll = all.find((q) => String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim() === raw);
          const resolvedAll = Number(exactAll?.id ?? exactAll?.Id ?? 0);
          if (resolvedAll > 0) return resolvedAll;
        }
      } catch {
        // fallback below
      }
    }
    return 0;
  }

  const list = toArray(await search.json());
  const exact = list.find((q) => String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim() === raw);
  const fallback = list.length === 1 ? list[0] : null;
  const resolved = Number(exact?.id ?? exact?.Id ?? fallback?.id ?? fallback?.Id ?? 0);
  if (resolved > 0) return resolved;

  if (numeric > 0) {
    try {
      const allRes = await fetch('/api/Qualification', { cache: 'no-store' });
      if (allRes.ok) {
        const all = toArray(await allRes.json());
        const exactAll = all.find((q) => String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim() === raw);
        const resolvedAll = Number(exactAll?.id ?? exactAll?.Id ?? 0);
        if (resolvedAll > 0) return resolvedAll;
      }
    } catch {
      // fallback below
    }
  }

  return 0;
};

const RequireWorkflow = ({ pageKey, children }) => {
  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };
  const routeStateQualification = location.state?.qualificationId ?? null;
  const rawQualificationRef = routeStateQualification ?? qualificationId ?? localStorage.getItem('qualificationId') ?? null;
  const hasRawQualificationRef = rawQualificationRef !== null && rawQualificationRef !== undefined && String(rawQualificationRef).trim() !== '';
  const qid = Number(rawQualificationRef || 0);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [status, setStatus] = useState(null);
  const [reloadNonce, setReloadNonce] = useState(0);

  useEffect(() => {
    let active = true;

    const loadStatus = async () => {
      if (!hasRawQualificationRef) {
        if (!active) return;
        setStatus(null);
        setLoading(false);
        return;
      }

      setLoading(true);
      setError('');
      try {
        const canonicalQid = await normalizeQualificationId(rawQualificationRef);
        if (!active) return;
        if (!canonicalQid) {
          setStatus(null);
          setLoading(false);
          return;
        }
        if (Number(canonicalQid) !== Number(qid)) {
          setQualificationId(canonicalQid);
        }

        const [
          demographics,
          phaseLinksPrimary,
          phaseLinksFallback,
          subjectsByQualification,
          subjectsAll,
          topicsByQualification,
          topicsAll,
          criteriaByQualification,
          criteriaAll,
          toolkitAllMeta
        ] = await Promise.all([
          safeFetchList(`/api/Demographics/byQualification?qualificationId=${canonicalQid}`),
          safeFetchListWithMeta(`/api/QualificationPhase/${canonicalQid}`),
          safeFetchListWithMeta(`/api/CurriculumPhase/byQualification?qualificationId=${canonicalQid}`),
          safeFetchListWithMeta(`/api/Subject/byQualification?qualificationId=${canonicalQid}`),
          safeFetchListWithMeta('/api/Subject'),
          safeFetchListWithMeta(`/api/Topic/byQualification?qualificationId=${canonicalQid}`),
          safeFetchListWithMeta('/api/Topic'),
          safeFetchListWithMeta(`/api/AssessmentCriteria/byQualification?qualificationId=${canonicalQid}`),
          safeFetchListWithMeta('/api/AssessmentCriteria'),
          safeFetchListWithMeta('/api/LecturerToolkit')
        ]);

        if (!active) return;
        const phaseLinksCount = Math.max(
          Number(phaseLinksPrimary?.items?.length || 0),
          Number(phaseLinksFallback?.items?.length || 0)
        );

        const buildCountResolution = (label, byQualification, allItems) => {
          const byQualificationCount = Number(byQualification?.items?.length || 0);
          const allFilteredCount = getCountForQualification(allItems?.items, canonicalQid);
          const parts = [
            `${label}ByQualification=${byQualificationCount}`,
            `${label}AllFiltered=${allFilteredCount}`
          ];
          if (byQualification?.error) parts.push(`${label}ByQualificationError=${byQualification.error}`);
          if (allItems?.error) parts.push(`${label}AllError=${allItems.error}`);
          return {
            count: Math.max(byQualificationCount, allFilteredCount),
            text: parts.join(' | ')
          };
        };

        const subjectResolution = buildCountResolution('Subject', subjectsByQualification, subjectsAll);
        const topicResolution = buildCountResolution('Topic', topicsByQualification, topicsAll);
        const criteriaResolution = buildCountResolution('Criteria', criteriaByQualification, criteriaAll);

        const phaseDebug = [
          `QualificationPhase=${Number(phaseLinksPrimary?.items?.length || 0)}`,
          `CurriculumPhaseByQualification=${Number(phaseLinksFallback?.items?.length || 0)}`
        ];
        if (phaseLinksPrimary?.error) phaseDebug.push(`QualificationPhaseError=${phaseLinksPrimary.error}`);
        if (phaseLinksFallback?.error) phaseDebug.push(`CurriculumPhaseByQualificationError=${phaseLinksFallback.error}`);

        setStatus({
          qualificationId: canonicalQid,
          diagnostics: {
            phaseResolution: phaseDebug.join(' | '),
            subjectResolution: subjectResolution.text,
            topicResolution: topicResolution.text,
            criteriaResolution: criteriaResolution.text
          },
          counts: {
            demographics: demographics.length,
            phaseLinks: phaseLinksCount,
            subjects: subjectResolution.count,
            topics: topicResolution.count,
            criteria: criteriaResolution.count,
            toolkit: getToolkitCountForQualification(toolkitAllMeta?.items, canonicalQid)
          }
        });
      } catch (e) {
        if (!active) return;
        setError(e?.message || 'Failed to validate workflow prerequisites.');
      } finally {
        if (active) setLoading(false);
      }
    };

    loadStatus();
    return () => { active = false; };
  }, [qid, rawQualificationRef, hasRawQualificationRef, setQualificationId, reloadNonce]);

  const missing = useMemo(() => {
    if (!status) return [];
    return getMissingPrerequisites(pageKey, status);
  }, [pageKey, status]);

  if (!hasRawQualificationRef) {
    return (
      <div className="mainpage-root">
        <h2 className="mainpage-title">Workflow Prerequisite Required</h2>
        <div style={{ background: '#fff6d8', border: '1px solid #e5c966', borderRadius: 8, padding: 12, color: '#694b00' }}>
          You must first complete "Qualification" before opening "{currentPageLabel(pageKey)}".
        </div>
        <div style={{ marginTop: 12 }}>
          <button className="next-step-button" type="button" onClick={() => navigate('/main')}>Goto Qualification</button>
        </div>
      </div>
    );
  }

  if (loading) {
    return (
      <div style={{ padding: 12, color: '#355' }}>
        Checking workflow prerequisites...
      </div>
    );
  }

  if (error) {
    return (
      <div className="mainpage-root">
        <h2 className="mainpage-title">Workflow Validation Error</h2>
        <div style={{ background: '#ffe6e6', border: '1px solid #f5b2b2', borderRadius: 8, padding: 12, color: '#8a1f1f' }}>
          Could not validate prerequisite workflow pages: {error}
        </div>
        <div style={{ marginTop: 12 }}>
          <button type="button" onClick={() => setReloadNonce((n) => n + 1)}>Recheck Workflow</button>
        </div>
      </div>
    );
  }

  if (missing.length > 0) {
    const first = missing[0];
    const activeQid = Number(status?.qualificationId || qid || 0);
    const counts = status?.counts || {};
    const phaseResolution = String(status?.diagnostics?.phaseResolution || '');
    const subjectResolution = String(status?.diagnostics?.subjectResolution || '');
    const topicResolution = String(status?.diagnostics?.topicResolution || '');
    const criteriaResolution = String(status?.diagnostics?.criteriaResolution || '');
    return (
      <div className="mainpage-root">
        <h2 className="mainpage-title">Workflow Prerequisite Required</h2>
        <div style={{ background: '#fff6d8', border: '1px solid #e5c966', borderRadius: 8, padding: 12, color: '#694b00' }}>
          You must first complete "{first.label}" before "{currentPageLabel(pageKey)}".
        </div>
        <div style={{ marginTop: 8, color: '#355' }}>
          Active Qualification Id: {activeQid || '-'}
        </div>
        <div style={{ marginTop: 6, color: '#355' }}>
          Counts: Demographics {Number(counts.demographics || 0)} | Phase Links {Number(counts.phaseLinks || 0)} | Subjects {Number(counts.subjects || 0)} | Topics {Number(counts.topics || 0)}
        </div>
        {phaseResolution ? (
          <div style={{ marginTop: 6, color: '#355' }}>
            Phase Count Source: {phaseResolution}
          </div>
        ) : null}
        {subjectResolution ? (
          <div style={{ marginTop: 6, color: '#355' }}>
            Subject Count Source: {subjectResolution}
          </div>
        ) : null}
        {topicResolution ? (
          <div style={{ marginTop: 6, color: '#355' }}>
            Topic Count Source: {topicResolution}
          </div>
        ) : null}
        {criteriaResolution ? (
          <div style={{ marginTop: 6, color: '#355' }}>
            Criteria Count Source: {criteriaResolution}
          </div>
        ) : null}
        <div style={{ marginTop: 12, display: 'flex', gap: 10, flexWrap: 'wrap' }}>
          <button type="button" onClick={() => setReloadNonce((n) => n + 1)}>
            Recheck Workflow
          </button>
          {missing.map((step) => (
            <button
              key={step.key}
              className="next-step-button"
              type="button"
              onClick={() => navigate(step.path, {
                state: activeQid > 0 ? { qualificationId: activeQid } : undefined
              })}
            >
              Goto {step.label}
            </button>
          ))}
        </div>
      </div>
    );
  }

  return children;
};

export default RequireWorkflow;
