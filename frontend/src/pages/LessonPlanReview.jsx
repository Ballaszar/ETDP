import React, { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';

const API = '/api';

const toNumber = (value) => {
  const n = Number(value ?? 0);
  return Number.isFinite(n) ? n : 0;
};

const readToolkitQualificationId = (row) =>
  toNumber(row?.qualificationsId ?? row?.QualificationsId ?? 0);

const readToolkitId = (row) => toNumber(row?.id ?? row?.Id ?? 0);
const readLpn = (row) => String(row?.lpn ?? row?.Lpn ?? '').trim();
const readSubjectCode = (row) => String(row?.subjectCode ?? row?.SubjectCode ?? '').trim();
const readSubjectDescription = (row) => String(row?.subjectDescription ?? row?.SubjectDescription ?? '').trim();
const readCriteriaDescription = (row) =>
  String(row?.assessmentCriteriaDescription ?? row?.AssessmentCriteriaDescription ?? '').trim();
const readLessonDescription = (row) =>
  String(row?.lessonPlanDescription ?? row?.LessonPlanDescription ?? '').trim();
const readLessonContent = (row) =>
  String(row?.lessonPlanContent ?? row?.LessonPlanContent ?? '').trim();

const lpnSortValue = (value) => {
  const raw = String(value ?? '').trim();
  if (!raw) return Number.POSITIVE_INFINITY;
  const digits = String(raw).replace(/[^\d]/g, '');
  const n = Number(digits);
  return Number.isFinite(n) && n > 0 ? n : Number.POSITIVE_INFINITY;
};

const sortToolkitRows = (rows) => {
  const list = Array.isArray(rows) ? [...rows] : [];
  list.sort((a, b) => {
    const bySubject = readSubjectCode(a).localeCompare(readSubjectCode(b), undefined, { sensitivity: 'base' });
    if (bySubject !== 0) return bySubject;

    const byLpn = lpnSortValue(readLpn(a)) - lpnSortValue(readLpn(b));
    if (byLpn !== 0) return byLpn;

    return readToolkitId(a) - readToolkitId(b);
  });
  return list;
};

export default function LessonPlanReview() {
  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId } = useQualification() || { qualificationId: null };

  const routeQualificationId = toNumber(location.state?.qualificationId ?? 0);
  const contextQualificationId = toNumber(qualificationId ?? 0);
  const storageQualificationId = toNumber(localStorage.getItem('qualificationId') ?? 0);
  const activeQualificationId = routeQualificationId || contextQualificationId || storageQualificationId;

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [toolkitRows, setToolkitRows] = useState([]);
  const [lessonPlans, setLessonPlans] = useState([]);

  useEffect(() => {
    if (!activeQualificationId) return;

    const abort = new AbortController();

    const load = async () => {
      setLoading(true);
      setError('');
      try {
        const [toolkitRes, lessonPlanRes] = await Promise.all([
          fetch(`${API}/LecturerToolkit`, { signal: abort.signal }),
          fetch(`${API}/LessonPlan/byQualification?qualificationId=${activeQualificationId}`, { signal: abort.signal })
        ]);

        if (!toolkitRes.ok) {
          throw new Error(`Could not load lecturer toolkit entries (${toolkitRes.status}).`);
        }

        const toolkitJson = await toolkitRes.json().catch(() => []);
        const toolkitByQualification = (Array.isArray(toolkitJson) ? toolkitJson : [])
          .filter((row) => readToolkitQualificationId(row) === activeQualificationId);

        setToolkitRows(sortToolkitRows(toolkitByQualification));

        if (lessonPlanRes.ok) {
          const lessonPlanJson = await lessonPlanRes.json().catch(() => []);
          setLessonPlans(Array.isArray(lessonPlanJson) ? lessonPlanJson : []);
        } else {
          setLessonPlans([]);
        }
      } catch (e) {
        if (abort.signal.aborted) return;
        setError(String(e?.message || e));
        setToolkitRows([]);
        setLessonPlans([]);
      } finally {
        if (!abort.signal.aborted) setLoading(false);
      }
    };

    load();

    return () => abort.abort();
  }, [activeQualificationId]);

  const completeCount = useMemo(
    () => toolkitRows.filter((row) => readLessonContent(row).length > 0).length,
    [toolkitRows]
  );

  const completionPercent = useMemo(() => {
    if (toolkitRows.length === 0) return 0;
    return Math.round((completeCount / toolkitRows.length) * 100);
  }, [toolkitRows, completeCount]);

  const workflowState = activeQualificationId > 0 ? { qualificationId: activeQualificationId } : undefined;

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Lesson Plan Review</h2>
      <p>Review imported and authored lesson plan rows before continuing to the print and export stage.</p>

      {!activeQualificationId ? (
        <div style={{ background: '#fff6d8', border: '1px solid #e5c966', borderRadius: 8, padding: 12, color: '#694b00' }}>
          No active qualification is selected. Open Main Menu and select a qualification first.
        </div>
      ) : null}

      {activeQualificationId > 0 ? (
        <div style={{ marginBottom: 10, color: '#355' }}>
          Active Qualification Id: {activeQualificationId}
        </div>
      ) : null}

      {loading ? (
        <div style={{ color: '#355' }}>Loading review data...</div>
      ) : null}

      {error ? (
        <div style={{ background: '#ffe6e6', border: '1px solid #f5b2b2', borderRadius: 8, padding: 12, color: '#8a1f1f', marginBottom: 12 }}>
          {error}
        </div>
      ) : null}

      {!loading && !error && activeQualificationId > 0 ? (
        <div className="form-section">
          {toolkitRows.length > 0 ? (
            <div style={{ color: '#0b7a0b', marginBottom: 10 }}>
              {toolkitRows.length} toolkit lesson row(s) found. {completeCount} row(s) have lesson content ({completionPercent}%).
            </div>
          ) : (
            <div style={{ color: '#694b00', marginBottom: 10 }}>
              No lecturer toolkit rows found for this qualification. You can return to Lecturer Toolkit to import/create rows.
            </div>
          )}

          <div style={{ color: '#355', marginBottom: 10 }}>
            LessonPlan records linked via Assessment Criteria: {lessonPlans.length}
          </div>

          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Entry</th>
                  <th>LPN</th>
                  <th>Subject</th>
                  <th>Assessment Criteria</th>
                  <th>Lesson Plan</th>
                  <th>Content Status</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {toolkitRows.map((row) => {
                  const entryId = readToolkitId(row);
                  const content = readLessonContent(row);
                  const status = content ? `Ready (${content.length} chars)` : 'Needs content';
                  return (
                    <tr key={entryId || `${readSubjectCode(row)}-${readLpn(row)}`}>
                      <td>{entryId || '-'}</td>
                      <td>{readLpn(row) || '-'}</td>
                      <td>{readSubjectCode(row) || '-'}{readSubjectDescription(row) ? ` - ${readSubjectDescription(row)}` : ''}</td>
                      <td>{readCriteriaDescription(row) || '-'}</td>
                      <td>{readLessonDescription(row) || '-'}</td>
                      <td>{status}</td>
                      <td>
                        <button
                          type="button"
                          onClick={() => navigate(`/content-builder/${entryId}`, {
                            state: {
                              ...workflowState,
                              autoLoadExistingContent: true,
                              toolkitEntryId: entryId
                            }
                          })}
                          disabled={!entryId}
                        >
                          Open in Engine
                        </button>
                      </td>
                    </tr>
                  );
                })}
                {toolkitRows.length === 0 ? (
                  <tr>
                    <td colSpan={7}>No rows to review.</td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Next Step</h3>
        <p style={{ marginTop: 0 }}>
          After review, continue to Learning Material Dashboard to generate Learner Guide, Workbook, Summative Assessment, Slides, and other exports.
        </p>
        <div className="button-row">
          <button type="button" onClick={() => navigate('/lecturer-toolkit', { state: workflowState })}>Back to Lecturer Toolkit</button>
          <button type="button" onClick={() => navigate('/learning-material', { state: workflowState })}>Open Learning Material Dashboard</button>
          <button type="button" onClick={() => navigate('/learner-guide-export', { state: workflowState })}>Open Learner Guide Export</button>
          <button type="button" onClick={() => navigate('/main-menu', { state: workflowState })}>Return to Main Menu</button>
        </div>
      </div>
    </div>
  );
}
