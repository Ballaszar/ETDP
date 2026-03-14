import React, { useState, useEffect } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useQualification } from "../context/QualificationContext";
import * as XLSX from 'xlsx';
import GlossaryLabel from "../components/GlossaryLabel";

const apiUrl = '/api/Subject';
const apiQualification = '/api/Qualification';
const apiPhases = '/api/CurriculumPhase';
const apiQualificationPhase = '/api/QualificationPhase';
const apiTemplatesSubjects = '/api/Templates/Subjects';

const initialState = {
  qualificationId: '',
  qualificationNumber: '',
  learningPhases: '',
  subjectPurpose: '',
  subjectCode: '',
  subjectDescription: '',
  subjectCredits: '',
  subjectNQFLevel: '',
  subjectPercentage: '',
  curriculumPhaseId: '',
};

const toBackendPayload = (form, id) => {
  return {
    Id: id ? Number(id) : undefined,
    QualificationId: form.qualificationId ? Number(form.qualificationId) : 0,
    SubjectPurpose: form.subjectPurpose,
    PhasesCode: form.subjectCode,
    SubjectDescription: form.subjectDescription,
    SubjectCredits: form.subjectCredits ? Number(form.subjectCredits) : null,
    SubjectNQFLevel: form.subjectNQFLevel ? Number(form.subjectNQFLevel) : null,
    SubjectPercentage: form.subjectPercentage ? Number(form.subjectPercentage) : null,
    CurriculumPhaseId: form.curriculumPhaseId ? Number(form.curriculumPhaseId) : null
  };
};

const SubjectsPage = () => {
  const [form, setForm] = useState(initialState);
  const [subjects, setSubjects] = useState([]);
  const [editingId, setEditingId] = useState(null);
  const [error, setError] = useState('');
  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId: contextId, setQualificationId } = useQualification();
  const stateId = location.state?.qualificationId;
  const activeQualificationId = String(stateId || contextId || '').trim();
  const [phases, setPhases] = useState([]);
  const [phaseId, setPhaseId] = useState(() => {
    const routed = Number(location.state?.curriculumPhaseId || 0);
    return routed > 0 ? String(routed) : '';
  });
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState("");
  const [uploadSuccess, setUploadSuccess] = useState("");
  const [uploadHeader, setUploadHeader] = useState([]);
  const [uploadPreviewRows, setUploadPreviewRows] = useState([]);
  const [uploadCreated, setUploadCreated] = useState(0);
  const [uploadFailed, setUploadFailed] = useState(0);
  const [uploadDetails, setUploadDetails] = useState([]);

  const loadSubjects = async () => {
    try {
      const qid = Number(activeQualificationId || 0);
      const url = qid > 0
        ? `${apiUrl}/byQualification?qualificationId=${qid}`
        : apiUrl;
      const res = await fetch(url);
      if (!res.ok) throw new Error(await res.text());
      const list = await res.json();
      setSubjects(Array.isArray(list) ? list : []);
    } catch (e) {
      setSubjects([]);
      setError('Failed to load subjects: ' + (e?.message || e));
    }
  };

  useEffect(() => {
    loadSubjects();
  }, [activeQualificationId]);

  useEffect(() => {
    let active = true;
    const loadPhaseOptions = async () => {
      try {
        const phasesRes = await fetch(apiPhases);
        const allPhases = phasesRes.ok ? await phasesRes.json() : [];
        let options = Array.isArray(allPhases) ? allPhases : [];

        const resolvedQid = await resolveQualificationNumericId(activeQualificationId || stateId || contextId || 0);
        if (resolvedQid) {
          const qLinksRes = await fetch(`${apiQualificationPhase}/${resolvedQid}`);
          const qPhaseLinks = qLinksRes.ok ? await qLinksRes.json() : [];
          if (Array.isArray(qPhaseLinks) && qPhaseLinks.length > 0) {
            const byId = new Map(options.map(p => [Number(p.id), p]));
            const linkedOrdered = [];
            for (const link of qPhaseLinks) {
              const phase = byId.get(Number(link.curriculumPhaseId));
              if (!phase) continue;
              if (linkedOrdered.some(p => Number(p.id) === Number(phase.id))) continue;
              linkedOrdered.push(phase);
            }
            if (linkedOrdered.length > 0) {
              options = linkedOrdered;
            }
          }
        }

        if (!active) return;
        setPhases(options);
        const routePhaseId = Number(location.state?.curriculumPhaseId || 0);
        setPhaseId(prev => {
          const prevNum = Number(prev || 0);
          const hasRoute = routePhaseId > 0 && options.some(p => Number(p.id) === routePhaseId);
          if (hasRoute) return String(routePhaseId);
          const hasPrev = prevNum > 0 && options.some(p => Number(p.id) === prevNum);
          if (hasPrev) return prev;
          return options.length > 0 ? String(options[0].id) : '';
        });
      } catch {
        if (!active) return;
        setPhases([]);
        setPhaseId('');
      }
    };
    loadPhaseOptions();
    return () => { active = false; };
  }, [activeQualificationId, stateId, contextId, location.state?.curriculumPhaseId]);

  useEffect(() => {
    let active = true;
    const loadContext = async () => {
      const raw = stateId || contextId || null;
      if (!raw) return;
      const resolved = await resolveQualificationNumericId(raw);
      const idToUse = Number(resolved || raw || 0);
      if (!idToUse || !active) return;

      if (Number(contextId || 0) !== idToUse) {
        setQualificationId(idToUse);
      }

      setForm(f => ({ ...f, qualificationId: String(idToUse) }));
      fetch(`${apiQualification}/${idToUse}`)
        .then(res => res.ok ? res.json() : null)
        .then(data => {
          if (data && active) {
            setForm(f => ({ ...f, qualificationNumber: data.qualificationNumber || '' }));
          }
        })
        .catch(() => {});

      Promise.all([
        fetch(apiPhases).then(res => res.ok ? res.json() : []),
        fetch(`${apiQualificationPhase}/${idToUse}`).then(res => res.ok ? res.json() : [])
      ])
      .then(([phasesList, qPhaseLinks]) => {
        if (!active) return;
        const byId = new Map(Array.isArray(phasesList) ? phasesList.map(p => [Number(p.id), p]) : []);
        const names = (Array.isArray(qPhaseLinks) ? qPhaseLinks : [])
          .map(link => byId.get(Number(link.curriculumPhaseId))?.name)
          .filter(Boolean)
          .map(n => String(n));
        const uniqueOrdered = [];
        for (const n of names) {
          if (!uniqueOrdered.includes(n)) uniqueOrdered.push(n);
        }
        setForm(f => ({ ...f, learningPhases: uniqueOrdered.join('; ') }));
      })
      .catch(() => {});
    };

    loadContext();
    return () => { active = false; };
  }, [stateId, contextId, setQualificationId]);

  const handleChange = e => {
    const { name, value } = e.target;
    setForm(f => ({ ...f, [name]: value }));
    setError('');
  };

  const excelColumns = [
    "Qualification Code",
    "Phases Code",
    "Phases Description",
    "Phases Purpose",
    "SubjectCode",
    "Subject Description",
    "Subject Credits",
    "Subject NQF Level",
    "Subject Percentage"
  ];
  const resolveQualificationNumericId = async (qid) => {
    const raw = String(qid ?? '').trim();
    const n = Number(raw || 0);
    if (!Number.isNaN(n) && Number.isFinite(n) && n > 0) {
      try {
        const probe = await fetch(`/api/Qualification/${n}`, { cache: 'no-store' });
        if (probe.ok) return n;
      } catch {
        // Continue with code-based resolution.
      }
    }
    if (!raw) return null;
    try {
      const res = await fetch(`/api/Qualification/search?text=${encodeURIComponent(raw)}`, { cache: 'no-store' });
      if (res.ok) {
        const list = await res.json();
        const exact = Array.isArray(list)
          ? list.find(q => String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim() === raw)
          : null;
        const fallback = Array.isArray(list) && list.length === 1 ? list[0] : null;
        const resolved = Number(exact?.id ?? exact?.Id ?? fallback?.id ?? fallback?.Id ?? 0);
        if (resolved > 0) return resolved;
      }
    } catch {
      // Continue to all-qualifications fallback.
    }

    try {
      const allRes = await fetch('/api/Qualification', { cache: 'no-store' });
      if (allRes.ok) {
        const all = await allRes.json();
        const exactAll = Array.isArray(all)
          ? all.find(q => String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim() === raw)
          : null;
        const resolvedAll = Number(exactAll?.id ?? exactAll?.Id ?? 0);
        return resolvedAll > 0 ? resolvedAll : null;
      }
    } catch {
      // no-op
    }
    return null;
  };
  const handleDownloadTemplate = () => {
    window.open(apiTemplatesSubjects, "_blank");
  };
  const handleExcelUpload = async (e) => {
    setUploadError("");
    setUploadSuccess("");
    const file = e.target.files[0];
    if (!file) return;
    setUploading(true);
    try {
      const data = await file.arrayBuffer();
      const workbook = XLSX.read(data);
      const sheet = workbook.Sheets[workbook.SheetNames[0]];
      const rows = XLSX.utils.sheet_to_json(sheet, { header: 1 });
      const header = rows[0] || [];
      const idxAny = (...names) => {
        for (const n of names) {
          const i = header.indexOf(n);
          if (i >= 0) return i;
        }
        return -1;
      };
      const iPurpose = idxAny("Subject Purpose", "Phases Purpose");
      const iCode = idxAny("SubjectCode", "Subject Code", "PhasesCode", "Phases Code");
      const iDesc = idxAny("Subject Description");
      const iCredits = idxAny("Subject Credits");
      const iNqf = idxAny("Subject NQF Level");
      const iPct = idxAny("Subject Percentage");
      const iPhaseName = idxAny("Learning Phases", "Curriculum Phase");
      const missing = [
        { label: "SubjectCode", idx: iCode },
        { label: "Subject Description", idx: iDesc }
      ].filter(c => c.idx < 0).map(c => c.label);
      if (missing.length) throw new Error("Missing columns: " + missing.join(", "));
      setUploadHeader(header);
      let created = 0, failed = 0;
      let preview = [];
      let details = [];
      const qidNum = await resolveQualificationNumericId(form.qualificationId || contextId || 0);
      if (!qidNum) throw new Error("QualificationId not set");
      const byName = new Map(phases.map(p => [String(p.name || "").trim().toLowerCase(), p]));
      const parseNumber = (v) => {
        const raw = String(v ?? "").trim();
        if (!raw) return null;
        const n = Number(raw.replace(/\s+/g, "").replace(",", "."));
        return Number.isFinite(n) ? n : null;
      };
      for (let r = 1; r < rows.length; r++) {
        const row = rows[r] || [];
        if (row.length === 0) continue;
        preview.push(row);
        const pNameRaw = iPhaseName >= 0 ? (row[iPhaseName] ?? "") : "";
        const pNorm = String(pNameRaw).trim().toLowerCase();
        let phaseToUse = null;
        if (pNorm) {
          phaseToUse = byName.get(pNorm) || null;
        }
        if (!phaseToUse && phaseId) {
          phaseToUse = phases.find(p => String(p.id) === String(phaseId)) || null;
        }
        if (!phaseToUse) {
          failed++;
          details.push({
            row: r,
            reason: "Phase not found",
            provided: String(pNameRaw)
          });
          continue;
        }
        const payload = {
          QualificationId: qidNum,
          SubjectPurpose: iPurpose >= 0 ? (row[iPurpose] ?? "") : "",
          PhasesCode: iCode >= 0 ? (row[iCode] ?? "") : "",
          SubjectDescription: iDesc >= 0 ? (row[iDesc] ?? "") : "",
          SubjectCredits: iCredits >= 0 ? parseNumber(row[iCredits]) : null,
          SubjectNQFLevel: iNqf >= 0 ? parseNumber(row[iNqf]) : null,
          SubjectPercentage: iPct >= 0 ? parseNumber(row[iPct]) : null,
          CurriculumPhaseId: Number(phaseToUse.id)
        };
        const res = await fetch(apiUrl, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload),
        });
        if (res.ok) {
          created++;
        } else {
          failed++;
          let body = "";
          try { body = await res.text(); } catch {}
          details.push({
            row: r,
            reason: "API error",
            status: res.status,
            body: body?.slice(0, 200) || ""
          });
        }
      }
      setUploadCreated(created);
      setUploadFailed(failed);
      setUploadPreviewRows(preview.slice(0, 5));
      setUploadSuccess(`Created ${created} subjects${failed ? `, ${failed} failed` : ''}`);
      setUploadDetails(details);
      loadSubjects();
    } catch (err) {
      setUploadError(err.message);
    } finally {
      setUploading(false);
    }
  };

  const handleSave = async () => {
    const method = editingId ? 'PUT' : 'POST';
    const url = editingId ? `${apiUrl}/${editingId}` : apiUrl;
    const applyQid = String(form.qualificationId || contextId || '').trim();
    const resolvedQid = await resolveQualificationNumericId(applyQid);
    if (!resolvedQid) {
      setError('QualificationId could not be resolved. Please select a valid qualification.');
      return;
    }
    const payload = toBackendPayload({ ...form, qualificationId: String(resolvedQid) }, editingId);
    console.log('Saving Subject Payload:', JSON.stringify(payload, null, 2));
    await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    setForm(initialState);
    setEditingId(null);
    loadSubjects();
  };

  const handleEdit = s => {
    const next = {
      qualificationId: String(s.qualificationId ?? form.qualificationId ?? ''),
      qualificationNumber: s.qualificationCode ?? s.qualificationNumber ?? form.qualificationNumber ?? '',
      learningPhases: s.learningPhases ?? form.learningPhases ?? '',
      subjectPurpose: s.subjectPurpose ?? s.SubjectPurpose ?? '',
      subjectCode: s.phasesCode ?? s.PhasesCode ?? s.subjectCode ?? s.SubjectCode ?? '',
      subjectDescription: s.subjectDescription ?? s.SubjectDescription ?? '',
      subjectCredits: (s.subjectCredits ?? s.SubjectCredits ?? '') === '' ? '' : Number(s.subjectCredits ?? s.SubjectCredits),
      subjectNQFLevel: (s.subjectNQFLevel ?? s.SubjectNQFLevel ?? '') === '' ? '' : Number(s.subjectNQFLevel ?? s.SubjectNQFLevel),
      subjectPercentage: (s.subjectPercentage ?? s.SubjectPercentage ?? '') === '' ? '' : Number(s.subjectPercentage ?? s.SubjectPercentage),
      curriculumPhaseId: String(s.curriculumPhaseId ?? s.CurriculumPhaseId ?? '')
    };
    setForm(next);
    setEditingId(s.id);
  };

  const handleDelete = async id => {
    await fetch(`${apiUrl}/${id}`, { method: 'DELETE' });
    loadSubjects();
  };

  const gotoSubjectsCapture = () => {
    const qid = Number(activeQualificationId || 0);
    if (qid > 0) {
      setQualificationId(qid);
    }
    navigate('/subjects/capture', {
      state: {
        qualificationId: qid > 0 ? qid : activeQualificationId || contextId || null,
        curriculumPhaseId: phaseId ? Number(phaseId) : undefined,
      }
    });
  };

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Curriculum Subjects</h2>
      {(form.qualificationId || contextId) && (
        <div style={{ margin: '8px 0', padding: '8px', background: '#f7f9fc', border: '1px solid #e5e9f2', borderRadius: 8 }}>
          <strong><GlossaryLabel label="Qualification" term="Qualification" />:</strong> {(form.qualificationNumber ? `${form.qualificationNumber}` : `#${String(form.qualificationId || contextId)}`)}
        </div>
      )}
      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginBottom: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ fontWeight: 600, fontSize: '1.1rem', color: '#185a3a', marginBottom: 8 }}>Bulk Upload Subjects (.xlsx)</div>
        <div style={{ marginBottom: 8 }}>
          <label style={{ display: 'block', marginBottom: 6 }}><GlossaryLabel label="Curriculum Phase" term="Curriculum" /></label>
          <select value={phaseId} onChange={e => setPhaseId(e.target.value)} required>
            <option value="">Select a phase...</option>
            {phases.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
          {!phaseId && <div style={{ color: '#b30000', marginTop: 6 }}>Phase is required</div>}
        </div>
        <div style={{ marginBottom: 8 }}>
          <span style={{ fontWeight: 500, color: '#23395d' }}>Required Columns:</span>
          <ul style={{ margin: '8px 0 0 18px', color: '#111', fontSize: '1rem' }}>
            {excelColumns.map(col => <li key={col}>{col}</li>)}
          </ul>
          <div style={{ color: '#555', marginTop: 4 }}>(Optional: you may also include “Learning Phases” or “Curriculum Phase”)</div>
        </div>
        <button type="button" onClick={handleDownloadTemplate} style={{ marginRight: 16 }}>Download Template</button>
        <input type="file" accept=".xlsx" onChange={handleExcelUpload} disabled={uploading || !phaseId} style={{ marginRight: 12 }} />
        <button type="button" onClick={async () => {
          setUploading(true);
          setUploadError("");
          setUploadSuccess("");
          try {
            const qidNum = await resolveQualificationNumericId(form.qualificationId || activeQualificationId || contextId || 0);
            if (!qidNum) throw new Error("QualificationId not set");
            const res = await fetch(`/api/Subject/import-csv?qualificationId=${qidNum}`, { method: 'POST' });
            if (!res.ok) {
              const body = await res.text();
              setUploadError(`Server error ${res.status}: ${String(body).slice(0,200)}`);
            } else {
              const data = await res.json();
              const created = Number(data?.created ?? 0);
              const failed = Number(data?.failed ?? 0);
              const details = Array.isArray(data?.details) ? data.details : [];
              setUploadCreated(created);
              setUploadFailed(failed);
              setUploadDetails(details.filter(d => d?.reason));
              setUploadSuccess(`Imported template: created ${created}${failed ? `, ${failed} failed` : ''}`);
              loadSubjects();
            }
          } catch (e) {
            setUploadError(String(e.message || e));
          } finally {
            setUploading(false);
          }
        }} style={{ marginLeft: 12 }} disabled={!phaseId}>Import from Template</button>
        {uploading && <span>Uploading...</span>}
        {uploadError && <span style={{ color: 'red', marginLeft: 12 }}>{uploadError}</span>}
        {uploadSuccess && <span style={{ color: 'green', marginLeft: 12 }}>{uploadSuccess}</span>}
        {(form.qualificationId || contextId) && (
          <div style={{ marginTop: 8, color: '#23395d' }}>Resolved QualificationId: {String(form.qualificationId || contextId)}</div>
        )}
        {uploadHeader.length > 0 && (
          <div style={{ marginTop: 12 }}>
            <div style={{ fontWeight: 600, marginBottom: 6 }}>Upload Preview (first 5 rows)</div>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr>
                  {uploadHeader.map((h, i) => (
                    <th key={i} style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {uploadPreviewRows.map((row, rIdx) => (
                  <tr key={rIdx}>
                    {uploadHeader.map((_, cIdx) => (
                      <td key={cIdx} style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{row[cIdx] ?? ''}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
            {(uploadCreated || uploadFailed) ? (
              <div style={{ marginTop: 8 }}>
                <span style={{ color: '#185a3a' }}>Created: {uploadCreated}</span>
                {uploadFailed ? <span style={{ color: '#b30000', marginLeft: 16 }}>Failed: {uploadFailed}</span> : null}
              </div>
            ) : null}
            {uploadDetails.length > 0 ? (
              <div style={{ marginTop: 12 }}>
                <div style={{ fontWeight: 600, marginBottom: 6, color: '#b30000' }}>Errors (first 20)</div>
                <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                  <thead>
                    <tr>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Row</th>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Reason</th>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Status</th>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Body</th>
                    </tr>
                  </thead>
                  <tbody>
                    {uploadDetails.slice(0, 20).map((d, i) => (
                      <tr key={i}>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.row}</td>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.reason}</td>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.status ?? ''}</td>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px', maxWidth: 400, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{d.body ?? ''}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : null}
          </div>
        )}
      </div>
      <form className="mainpage-form" onSubmit={e => { e.preventDefault(); handleSave(); }}>
        <div className="mainpage-form-fields-vertical">
          <label><GlossaryLabel label="QualificationId" term="Qualification" />
            <input name="qualificationId" className="mainpage-input" placeholder="QualificationId" value={form.qualificationId} onChange={handleChange} type="number" min="1" />
          </label>
          <label><GlossaryLabel label="Qualification Code" term="Qualification" />
            <input name="qualificationNumber" className="mainpage-input" placeholder="Qualification Code" value={form.qualificationNumber} onChange={handleChange} />
          </label>
          <label><GlossaryLabel label="Learning Phases" term="Learning Programme" />
            <input name="learningPhases" className="mainpage-input" placeholder="Learning Phases" value={form.learningPhases} onChange={handleChange} />
          </label>
          <label>Subject Purpose
            <input name="subjectPurpose" maxLength={180} className="mainpage-input" placeholder="Subject Purpose" value={form.subjectPurpose} onChange={handleChange} />
          </label>
          <label>Subject Code
            <input name="subjectCode" maxLength={40} className="mainpage-input" placeholder="Subject Code" value={form.subjectCode} onChange={handleChange} />
          </label>
          <label>Subject Description
            <input name="subjectDescription" maxLength={40} className="mainpage-input" placeholder="Subject Description" value={form.subjectDescription} onChange={handleChange} />
          </label>
          <label><GlossaryLabel label="Subject Credits" term="Credit" />
            <input name="subjectCredits" maxLength={6} className="mainpage-input" placeholder="Subject Credits" value={form.subjectCredits} onChange={handleChange} type="number" min="0" />
          </label>
          <label><GlossaryLabel label="Subject NQF Level" term="NQF Level" />
            <input name="subjectNQFLevel" maxLength={4} className="mainpage-input" placeholder="Subject NQF Level" value={form.subjectNQFLevel} onChange={handleChange} type="number" min="0" />
          </label>
          <label>Subject Percentage
            <input name="subjectPercentage" maxLength={4} className="mainpage-input" placeholder="Subject Percentage" value={form.subjectPercentage} onChange={handleChange} type="number" min="0" />
          </label>
          <label><GlossaryLabel label="Curriculum Phase Id" term="Curriculum" />
            <input name="curriculumPhaseId" maxLength={8} className="mainpage-input" placeholder="Curriculum Phase Id" value={form.curriculumPhaseId} onChange={handleChange} type="number" min="1" />
          </label>
        </div>
        <div className="mainpage-form-actions">
          <button type="submit">Save</button>
          <button type="button" onClick={() => setForm(initialState)}>Clear</button>
          {editingId && <button type="button" onClick={() => setEditingId(null)}>Cancel Edit</button>}
          <button className="next-step-button" type="button" style={{ marginLeft: 16 }} onClick={gotoSubjectsCapture}>Goto Subjects</button>
          <button
            className="next-step-button"
            type="button"
            style={{ marginLeft: 16 }}
            onClick={() => {
              const qid = Number(form.qualificationId || activeQualificationId || contextId || 0);
              if (qid > 0) {
                setQualificationId(qid);
              }
              navigate('/topics', {
                state: {
                  qualificationId: qid > 0 ? qid : undefined,
                  curriculumPhaseId: phaseId ? Number(phaseId) : undefined
                }
              });
            }}
          >
            Goto Topics
          </button>
        </div>
      </form>
      <h3 style={{marginTop:'2rem'}}>Subjects List</h3>
      <div className="button-row">
        <button type="button" onClick={async () => {
          setUploading(true);
          setUploadError("");
          setUploadSuccess("");
          try {
            const qidNum = await resolveQualificationNumericId(form.qualificationId || activeQualificationId || contextId || 0);
            if (!qidNum) throw new Error("QualificationId not set");
            const res = await fetch(`/api/Subject/import-csv?qualificationId=${qidNum}`, { method: 'POST' });
            if (!res.ok) {
              const body = await res.text();
              setUploadError(`Server error ${res.status}: ${String(body).slice(0,200)}`);
            } else {
              const data = await res.json();
              const created = Number(data?.created ?? 0);
              const failed = Number(data?.failed ?? 0);
              const details = Array.isArray(data?.details) ? data.details : [];
              setUploadCreated(created);
              setUploadFailed(failed);
              setUploadDetails(details.filter(d => d?.reason));
              setUploadSuccess(`Imported template: created ${created}${failed ? `, ${failed} failed` : ''}`);
              loadSubjects();
            }
          } catch (e) {
            setUploadError(String(e.message || e));
          } finally {
            setUploading(false);
          }
        }} disabled={!phaseId}>Import from Template</button>
      </div>
      <table style={{ width: '100%', marginTop: 16 }}>
        <thead>
          <tr>
            <th>QualificationId</th><th>Qualification Code</th><th>Learning Phases</th><th>Purpose</th><th>Subject Code</th><th>Description</th><th>Credits</th><th>NQF Level</th><th>Percentage</th><th>Curriculum Phase Id</th><th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {subjects.map(s => (
            <tr key={s.id}>
              <td>{s.qualificationId}</td>
              <td>{s.qualificationCode || s.qualificationNumber || ''}</td>
              <td>{s.learningPhases || ''}</td>
              <td>{s.subjectPurpose}</td>
              <td>{s.subjectCode ?? s.phasesCode}</td>
              <td>{s.subjectDescription}</td>
              <td>{s.subjectCredits}</td>
              <td>{s.subjectNQFLevel}</td>
              <td>{s.subjectPercentage}</td>
              <td>{s.curriculumPhaseId}</td>
              <td>
                <button onClick={() => handleEdit(s)}>Edit</button>
                <button onClick={() => handleDelete(s.id)}>Delete</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default SubjectsPage;
