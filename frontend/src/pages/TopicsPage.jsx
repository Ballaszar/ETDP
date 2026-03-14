
import React, { useState, useEffect, useMemo } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import * as XLSX from 'xlsx';
import GlossaryLabel from "../components/GlossaryLabel";

const apiUrl = '/api/Topic';
const apiSubjects = '/api/Subject';
const apiPhases = '/api/CurriculumPhase';
const apiQualification = '/api/Qualification';
const apiOutcomes = '/api/Outcome';

const initialState = {
  qualificationId: '',
  phasesCode: '',
  subjectCode: '',
  subjectDescription: '',
  subjectCredits: '',
  notionalHours: '',
  periodsPerTopic: '',
  topicPurpose: '',
  topicCode: '',
  topicDescription: '',
  topicCredits: '',
  topicPercentage: '',
  assessmentCriteriaId: '',
  assessmentCriteriaDescription: '',
  subjectId: '',
  outcomeId: '',
};

const parseNullableNumber = (value) => {
  const raw = String(value ?? '').trim();
  if (!raw) return null;
  const normalized = raw.replace(/\s+/g, '').replace(',', '.');
  const n = Number(normalized);
  return Number.isFinite(n) ? n : null;
};

// Convert frontend → backend
const toBackendPayload = (form, id) => ({
  TopicPurpose: form.topicPurpose,
  TopicCode: form.topicCode,
  TopicDescription: form.topicDescription,
  SubjectCredits: parseNullableNumber(form.subjectCredits),
  NotionalHours: parseNullableNumber(form.notionalHours),
  PeriodsPerTopic: parseNullableNumber(form.periodsPerTopic),
  TopicCredits: form.topicCredits ? Number(form.topicCredits) : null,
  TopicPercentage: form.topicPercentage ? Number(form.topicPercentage) : null,
  SubjectId: form.subjectId ? Number(form.subjectId) : 0,
  OutcomeId: form.outcomeId ? Number(form.outcomeId) : null,
  AssessmentCriteriaDescription: form.assessmentCriteriaDescription || null
});

const TopicsPage = () => {
  const [form, setForm] = useState(initialState);
  const [topics, setTopics] = useState([]);
  const [editingId, setEditingId] = useState(null);
  const [error, setError] = useState('');
  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };
  const [qualifications, setQualifications] = useState([]);
  const [resolvedQualificationId, setResolvedQualificationId] = useState('');
  const [phases, setPhases] = useState([]);
  const [phaseId, setPhaseId] = useState('');
  const [subjectsPhase, setSubjectsPhase] = useState([]);
  const [subjectsByQualification, setSubjectsByQualification] = useState([]);
  const [usesOutcomes, setUsesOutcomes] = useState(false);
  const [outcomes, setOutcomes] = useState([]);
  const [subjectPeriodsOverride, setSubjectPeriodsOverride] = useState('');
  const [periodOverrideStatus, setPeriodOverrideStatus] = useState('');
  const [applyingSubjectPeriods, setApplyingSubjectPeriods] = useState(false);

  useEffect(() => {
    fetch(apiPhases)
      .then(res => res.json())
      .then(list => {
        const allowed = new Set([
          "Knowledge Learning",
          "Practical Learning",
          "Workplace Experience",
          "Fundamental Learning"
        ]);
        const filtered = (Array.isArray(list) ? list : [])
          .filter(p => p && typeof p.name === "string" && allowed.has(p.name) && !p.name.toLowerCase().includes("skeleton"));
        setPhases(filtered);
        if (!phaseId && filtered.length) {
          setPhaseId(String(filtered[0].id));
        }
      })
      .catch(() => {});
    fetch(apiQualification)
      .then(res => (res.ok ? res.json() : []))
      .then(list => {
        const normalized = (Array.isArray(list) ? list : [])
          .map((q) => ({
            id: Number(q?.id ?? q?.Id ?? 0),
            qualificationNumber: String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim(),
            qualificationDescription: String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim()
          }))
          .filter((q) => q.id > 0)
          .sort((a, b) => {
            const aKey = `${a.qualificationNumber} ${a.qualificationDescription}`.trim().toLowerCase();
            const bKey = `${b.qualificationNumber} ${b.qualificationDescription}`.trim().toLowerCase();
            return aKey.localeCompare(bKey);
          });
        setQualifications(normalized);
      })
      .catch(() => setQualifications([]));
    const stateId = location.state?.qualificationId;
    const idToUse = qualificationId || stateId || null;
    if (idToUse) {
      setResolvedQualificationId(String(idToUse));
      setForm(f => ({ ...f, qualificationId: String(idToUse) }));
    }
  }, []);

  const handleQualificationSelect = (qidRaw) => {
    const qid = String(qidRaw || '').trim();
    setResolvedQualificationId(qid);
    setForm((f) => ({
      ...f,
      qualificationId: qid,
      subjectId: '',
      subjectCode: '',
      subjectDescription: '',
      outcomeId: '',
      phasesCode: phaseNameById.get(Number(phaseId || 0)) || ''
    }));
    setPeriodOverrideStatus('');
    setError('');
    const qidNum = Number(qid || 0);
    if (qidNum > 0) {
      try { setQualificationId(qidNum); } catch (_) {}
    }
  };

  useEffect(() => {
    const qid = Number(resolvedQualificationId || 0);
    const url = qid > 0
      ? `${apiUrl}/byQualification?qualificationId=${qid}`
      : apiUrl;
    fetch(url)
      .then(res => res.ok ? res.json() : [])
      .then(list => setTopics(Array.isArray(list) ? list : []))
      .catch(e => setError('Failed to load topics: ' + e.message));
  }, [resolvedQualificationId]);

  useEffect(() => {
    const qid = Number(resolvedQualificationId || 0);
    if (!qid) {
      setUsesOutcomes(false);
      return;
    }
    fetch(`${apiQualification}/${qid}`)
      .then(res => (res.ok ? res.json() : null))
      .then(q => setUsesOutcomes(Boolean(q?.usesOutcomes ?? q?.UsesOutcomes ?? false)))
      .catch(() => setUsesOutcomes(false));
  }, [resolvedQualificationId]);

  useEffect(() => {
    const qid = Number(resolvedQualificationId || 0);
    const pid = Number(phaseId || 0);
    if (!qid || !pid) { setSubjectsPhase([]); return; }
    fetch(`${apiSubjects}/byPhase?qualificationId=${qid}&phaseId=${pid}`)
      .then(res => res.ok ? res.json() : [])
      .then(list => setSubjectsPhase(Array.isArray(list) ? list : []))
      .catch(() => setSubjectsPhase([]));
  }, [resolvedQualificationId, phaseId]);

  useEffect(() => {
    const qid = Number(resolvedQualificationId || 0);
    if (!qid) {
      setSubjectsByQualification([]);
      return;
    }
    fetch(`${apiSubjects}/byQualification?qualificationId=${qid}`)
      .then(res => res.ok ? res.json() : [])
      .then(list => setSubjectsByQualification(Array.isArray(list) ? list : []))
      .catch(() => setSubjectsByQualification([]));
  }, [resolvedQualificationId]);

  useEffect(() => {
    if (!usesOutcomes) {
      setOutcomes([]);
      return;
    }
    const subjectId = form.subjectId ? Number(form.subjectId) : 0;
    if (!subjectId) {
      setOutcomes([]);
      return;
    }
    fetch(`${apiOutcomes}/bySubject?subjectId=${subjectId}`)
      .then(res => (res.ok ? res.json() : []))
      .then(list => setOutcomes(Array.isArray(list) ? list : []))
      .catch(() => setOutcomes([]));
  }, [usesOutcomes, form.subjectId]);

  const reloadTopics = () => {
    const qid = Number(resolvedQualificationId || 0);
    const url = qid > 0
      ? `${apiUrl}/byQualification?qualificationId=${qid}`
      : apiUrl;
    fetch(url)
      .then(res => res.ok ? res.json() : [])
      .then(list => setTopics(Array.isArray(list) ? list : []))
      .catch(() => setTopics([]));
  };

  const applySubjectPeriodsOverride = async () => {
    setError('');
    setPeriodOverrideStatus('');

    const subjectId = Number(form.subjectId || 0);
    const periods = parseNullableNumber(subjectPeriodsOverride);
    if (!subjectId) {
      setError('Select a subject before applying periods override.');
      return;
    }
    if (!periods || periods <= 0) {
      setError('Enter a valid Periods per Topic value greater than 0.');
      return;
    }

    setApplyingSubjectPeriods(true);
    try {
      const res = await fetch(`${apiUrl}/apply-periods-by-subject`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          subjectId,
          periodsPerTopic: periods
        })
      });
      const txt = await res.text();
      if (!res.ok) {
        setError(`Apply periods failed: ${txt || res.status}`);
        return;
      }
      const json = txt ? JSON.parse(txt) : {};
      reloadTopics();
      setPeriodOverrideStatus(`Applied periods per topic (${periods}) to ${Number(json?.updated || 0)} topic(s). Rebuild the learning schedule from Lecturer Toolkit when you are ready.`);
    } catch (e) {
      setError(`Apply periods failed: ${String(e?.message || e)}`);
    } finally {
      setApplyingSubjectPeriods(false);
    }
  };

  const handleChange = e => {
    const { name, value } = e.target;
    if (name === 'subjectId') {
      const selected = subjectsPhase.find(s => String(s.id) === String(value)) || null;
      setForm(f => ({
        ...f,
        subjectId: value,
        subjectCode: selected?.subjectCode ?? selected?.phasesCode ?? '',
        subjectDescription: selected?.subjectDescription ?? '',
        subjectCredits: selected?.subjectCredits != null ? String(selected.subjectCredits) : f.subjectCredits,
        phasesCode: phaseNameById.get(Number(selected?.curriculumPhaseId || phaseId || 0)) || '',
        outcomeId: ''
      }));
      setError('');
      return;
    }
    setForm(f => ({ ...f, [name]: value, ...(name === 'phasesCode' ? { outcomeId: '' } : {}) }));
    setError('');
  };

  const handleSave = async () => {
    const method = editingId ? 'PUT' : 'POST';
    const url = editingId ? `${apiUrl}/${editingId}` : apiUrl;
    setPeriodOverrideStatus('');
    let subjectId = form.subjectId ? Number(form.subjectId) : 0;
    if (!subjectId) {
      const norm = v => String(v ?? '').trim().toLowerCase();
      const lookup = norm(form.subjectCode);
      const match = subjectsPhase.find(s => norm(s.subjectCode ?? s.phasesCode) === lookup);
      subjectId = match ? Number(match.id) : 0;
    }
    if (!subjectId) {
      setError('Select a subject for the selected phase.');
      return;
    }
    const payload = { ...toBackendPayload(form, editingId), SubjectId: subjectId };
    if (!usesOutcomes) payload.OutcomeId = null;
    console.log('Saving Topic Payload:', JSON.stringify(payload, null, 2));
    await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    // Stay on Topics; Assessment Criteria UI was removed
    setPeriodOverrideStatus('Topic saved. Rebuild the learning schedule from Lecturer Toolkit when you are ready.');
    setForm(initialState);
    setEditingId(null);
    reloadTopics();
  };

  const handleEdit = t => {
    const subjectIdNum = Number(t.subjectId || 0);
    const matchedSubject = subjectsByQualification.find(s => Number(s.id) === subjectIdNum) || null;
    if (matchedSubject?.curriculumPhaseId) {
      setPhaseId(String(matchedSubject.curriculumPhaseId));
    }
    setForm({
      ...initialState,
      qualificationId: String(t.qualificationId ?? resolvedQualificationId ?? ''),
      phasesCode: String(t.phasesCode ?? ''),
      subjectCode: String(t.subjectCode ?? ''),
      subjectDescription: String(t.subjectDescription ?? ''),
      subjectCredits: (t.subjectCredits ?? '') === '' ? '' : String(t.subjectCredits),
      notionalHours: (() => {
        const value = t.notionalHours ?? t.NotionalHours ?? t.nationalHours ?? t.NationalHours ?? '';
        return value === '' ? '' : String(value);
      })(),
      periodsPerTopic: (t.periodsPerTopic ?? '') === '' ? '' : String(t.periodsPerTopic),
      topicPurpose: String(t.topicPurpose ?? ''),
      topicCode: String(t.topicCode ?? ''),
      topicDescription: String(t.topicDescription ?? ''),
      topicCredits: (t.topicCredits ?? '') === '' ? '' : String(t.topicCredits),
      topicPercentage: (t.topicPercentage ?? '') === '' ? '' : String(t.topicPercentage),
      assessmentCriteriaId: (t.assessmentCriteriaId ?? '') === '' ? '' : String(t.assessmentCriteriaId),
      assessmentCriteriaDescription: String(t.assessmentCriteriaDescription ?? ''),
      subjectId: (t.subjectId ?? '') === '' ? '' : String(t.subjectId),
      outcomeId: (t.outcomeId ?? '') === '' ? '' : String(t.outcomeId)
    });
    setEditingId(t.id);
  };

  const handleDelete = async id => {
    await fetch(`${apiUrl}/${id}`, { method: 'DELETE' });
    setPeriodOverrideStatus('Topic deleted. Rebuild the learning schedule from Lecturer Toolkit when you are ready.');
    reloadTopics();
  };

  // Excel template columns
  const excelColumns = [
    "Qualification Code",
    "Phases Code",
    "Phases Description",
    "Subject Code",
    "Subject Credits",
    "Notional Hours",
    "Periods per Topic",
    "Subject Decription",
    "Topic Code",
    "Topic Description",
    "Assessment Criteria Number",
    "Assesment Criteria Description",
    "LPN",
    "Lesson Plan Description"
  ];

  // Download template
  const handleDownloadTemplate = () => {
    window.open("/api/Templates/Topics", "_blank");
  };

  // Handle Excel upload
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState("");
  const [uploadSuccess, setUploadSuccess] = useState("");
  const [uploadHeader, setUploadHeader] = useState([]);
  const [uploadPreviewRows, setUploadPreviewRows] = useState([]);
  const [uploadCreated, setUploadCreated] = useState(0);
  const [uploadFailed, setUploadFailed] = useState(0);
  const [uploadDetails, setUploadDetails] = useState([]);
  const resolveQualificationNumericId = async (qid) => {
    const n = Number(qid);
    if (!Number.isNaN(n) && Number.isFinite(n) && n > 0) return n;
    try {
      const res = await fetch(`/api/Qualification/search?text=${encodeURIComponent(String(qid))}`);
      if (!res.ok) return null;
      const list = await res.json();
      const exact = Array.isArray(list) ? list.find(q => q.qualificationNumber === String(qid)) : null;
      return exact?.id ? Number(exact.id) : null;
    } catch {
      return null;
    }
  };
  const handleExcelUpload = async (e) => {
    setUploadError("");
    setUploadSuccess("");
    const file = e.target.files[0];
    if (!file) return;
    setUploading(true);
    try {
      // For demo: parse and validate columns client-side
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
      const required = [
        { label: "Phases Code", idx: idxAny("Phases Code", "PhasesCode") },
        { label: "Subject Code", idx: idxAny("Subject Code", "SubjectCode") },
        { label: "Subject Description", idx: idxAny("Subject Decription", "Subject Description") },
        { label: "Topic Code", idx: idxAny("Topic Code") },
        { label: "Topic Description", idx: idxAny("Topic Description") }
      ];
      const missing = required.filter(c => c.idx < 0).map(c => c.label);
      if (missing.length) throw new Error("Missing columns: " + missing.join(", "));
      setUploadHeader(header);
      const iPhasesCode = idxAny("Phases Code", "PhasesCode");
      const iPhasesDescription = idxAny("Phases Description", "Phase Description");
      const iSubjectCode = idxAny("Subject Code", "SubjectCode");
      const iSubjectDesc = idxAny("Subject Decription", "Subject Description");
      const iSubjectCredits = idxAny("Subject Credits");
      const iNotionalHours = idxAny("Notional Hours", "National Hours");
      const iPeriodsPerTopic = idxAny("Periods per Topic", "PeriodsPerTopic");
      const iCode = idxAny("Topic Code");
      const iDesc = idxAny("Topic Description");
      const iCriteriaNum = idxAny("Assessment Criteria Number", "Assessment Criteria Id");
      const iACDesc = idxAny("Assesment Criteria Description", "Assessment Criteria Description");
      let created = 0, failed = 0;
      let preview = [];
      let details = [];
      const norm = v => String(v ?? "").trim().toLowerCase().replace(/[:]+$/g, "");
      const qidNum = await resolveQualificationNumericId(form.qualificationId || qualificationId || 0);
      if (!qidNum) throw new Error("QualificationId not set");
      const phaseNum = Number(phaseId || 0);
      if (!phaseNum) throw new Error("Curriculum Phase not selected");
      let subjects = [];
      // Prefer subjects for the selected phase; if none, fallback to all subjects for qualification
      try {
        const resPhase = await fetch(`${apiSubjects}/byPhase?qualificationId=${qidNum}&phaseId=${phaseNum}`);
        subjects = resPhase.ok ? await resPhase.json() : [];
      } catch {}
      if (!Array.isArray(subjects) || subjects.length === 0) {
        try {
          const resAll = await fetch(`${apiSubjects}/byQualification?qualificationId=${qidNum}`);
          subjects = resAll.ok ? await resAll.json() : [];
        } catch {}
      }
      const byCode = new Map(subjects.map(s => [norm((s.subjectCode ?? s.phasesCode) || ''), s]));
      const byDesc = new Map(subjects.map(s => [norm(s.subjectDescription || ''), s]));
      let lastPhaseCodeRaw = "";
      let lastSubjectCodeRaw = "";
      let lastSubjectDescRaw = "";
      for (let r = 1; r < rows.length; r++) {
        const row = rows[r] || [];
        if (row.length === 0) continue;
        preview.push(row);
        const phaseCodeCell = iPhasesCode >= 0 ? row[iPhasesCode] ?? '' : '';
        const subjectCodeCell = iSubjectCode >= 0 ? row[iSubjectCode] ?? '' : '';
        const subjectDescCell = iSubjectDesc >= 0 ? row[iSubjectDesc] ?? '' : '';
        const phaseCodeRaw = String(phaseCodeCell ?? "").trim() || lastPhaseCodeRaw;
        const subjectCodeRaw = String(subjectCodeCell ?? "").trim() || lastSubjectCodeRaw;
        const subjectDescRaw = String(subjectDescCell ?? "").trim() || lastSubjectDescRaw;
        if (String(phaseCodeCell ?? "").trim()) lastPhaseCodeRaw = String(phaseCodeCell ?? "").trim();
        if (String(subjectCodeCell ?? "").trim()) lastSubjectCodeRaw = String(subjectCodeCell ?? "").trim();
        if (String(subjectDescCell ?? "").trim()) lastSubjectDescRaw = String(subjectDescCell ?? "").trim();
        const scRaw = subjectCodeRaw || phaseCodeRaw;
        const sc = norm(scRaw);
        const sd = norm(subjectDescRaw);
        const variants = (() => {
          const v = new Set();
          v.add(sc);
          v.add(sc.replace(/^\d+\s*-\s*/, ''));
          v.add(sc.replace(/\s+/g, ''));
          const parts = sc.split('-');
          if (parts.length >= 2) v.add([parts[0], parts[1]].join('-'));
          return Array.from(v).filter(Boolean);
        })();
        let match = null;
        for (const key of variants) {
          if (!match && byCode.get(key)) { match = byCode.get(key); break; }
        }
        if (!match) {
          match = subjects.find(s => {
            const code = norm((s.subjectCode ?? s.phasesCode) || '');
            return variants.some(v => code.startsWith(v));
          }) || null;
        }
        if (!match && sd) {
          match = byDesc.get(sd) || null;
        }
        const subjectId = match ? Number(match.id) : 0;
        const payload = {
          TopicPurpose: iPhasesDescription >= 0 ? (row[iPhasesDescription] ?? "") : "",
          TopicCode: iCode >= 0 ? (row[iCode] ?? "") : "",
          TopicDescription: iDesc >= 0 ? (row[iDesc] ?? "") : "",
          SubjectCredits: iSubjectCredits >= 0 ? parseNullableNumber(row[iSubjectCredits]) : null,
          NotionalHours: iNotionalHours >= 0 ? parseNullableNumber(row[iNotionalHours]) : null,
          PeriodsPerTopic: iPeriodsPerTopic >= 0 ? parseNullableNumber(row[iPeriodsPerTopic]) : null,
          TopicCredits: null,
          TopicPercentage: null,
          SubjectId: subjectId,
          AssessmentCriteriaDescription: iACDesc >= 0
            ? (row[iACDesc] ?? null)
            : (iCriteriaNum >= 0 ? (row[iCriteriaNum] ?? null) : null)
        };
        if (!subjectId) {
          failed++;
          details.push({
            row: r,
            reason: "Subject not found",
            phasesCode: String(phaseCodeRaw),
            subjectDescription: String(subjectDescRaw)
          });
          continue;
        }
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
            subjectId,
            body: body?.slice(0, 200) || ""
          });
        }
      }
      setUploadCreated(created);
      setUploadFailed(failed);
      setUploadPreviewRows(preview.slice(0, 5));
      setUploadSuccess(`Created ${created} topics${failed ? `, ${failed} failed` : ''}`);
      setUploadDetails(details);
      reloadTopics();
    } catch (err) {
      setUploadError(err.message);
    } finally {
      setUploading(false);
    }
  };

  const phaseNameById = useMemo(
    () => new Map(phases.map(p => [Number(p.id), String(p.name || '')])),
    [phases]
  );
  const phaseNameBySubjectId = useMemo(() => {
    const map = new Map();
    subjectsByQualification.forEach((s) => {
      map.set(Number(s.id), phaseNameById.get(Number(s.curriculumPhaseId)) || '');
    });
    return map;
  }, [subjectsByQualification, phaseNameById]);
  const subjectCodeBySubjectId = useMemo(() => {
    const map = new Map();
    subjectsByQualification.forEach((s) => {
      map.set(Number(s.id), String(s.subjectCode ?? s.phasesCode ?? ''));
    });
    return map;
  }, [subjectsByQualification]);

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Curriculum Topics</h2>
      {resolvedQualificationId && (
        <div style={{ margin: '8px 0', padding: '8px', background: '#f7f9fc', border: '1px solid #e5e9f2', borderRadius: 8 }}>
          <strong><GlossaryLabel label="Qualification" term="Qualification" />:</strong> #{resolvedQualificationId}
        </div>
      )}
      {periodOverrideStatus ? (
        <div style={{ margin: '8px 0', padding: '8px', background: '#edf9f0', border: '1px solid #b7e3c2', borderRadius: 8, color: '#185a3a' }}>
          {periodOverrideStatus}
        </div>
      ) : null}

      {/* Excel Upload Section */}
      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginBottom: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ fontWeight: 600, fontSize: '1.1rem', color: '#185a3a', marginBottom: 8 }}>Bulk Upload Topics (.xlsx)</div>
        <div style={{ marginBottom: 8 }}>
          <label style={{ display: 'block', marginBottom: 6 }}><GlossaryLabel label="Qualification" term="Qualification" /></label>
          <select value={resolvedQualificationId || ''} onChange={(e) => handleQualificationSelect(e.target.value)} required>
            <option value="">Select qualification...</option>
            {qualifications.map((q) => (
              <option key={q.id} value={String(q.id)}>
                {q.qualificationNumber
                  ? `${q.qualificationNumber} - ${q.qualificationDescription || `Qualification #${q.id}`}`
                  : (q.qualificationDescription || `Qualification #${q.id}`)}
              </option>
            ))}
          </select>
          {!resolvedQualificationId && <div style={{ color: '#b30000', marginTop: 6 }}>Qualification is required</div>}
        </div>
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
        </div>
        <button type="button" onClick={handleDownloadTemplate} style={{ marginRight: 16 }}>Download Template</button>
        <input type="file" accept=".xlsx" onChange={handleExcelUpload} disabled={uploading || !phaseId || !resolvedQualificationId} style={{ marginRight: 12 }} />
        <button type="button" onClick={async () => {
          setUploading(true);
          setUploadError("");
          setUploadSuccess("");
          try {
            const qidForImport = Number(resolvedQualificationId || form.qualificationId || qualificationId || 0);
            if (!qidForImport) {
              setUploadError('QualificationId not set');
              return;
            }
            const res = await fetch(`/api/Topic/import-csv?qualificationId=${qidForImport}`, { method: 'POST' });
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
              reloadTopics();
            }
          } catch (e) {
            setUploadError(String(e.message || e));
          } finally {
            setUploading(false);
          }
        }} style={{ marginLeft: 12 }} disabled={!phaseId || !resolvedQualificationId}>Import from Template</button>
        {uploading && <span>Uploading...</span>}
        {uploadError && <span style={{ color: 'red', marginLeft: 12 }}>{uploadError}</span>}
        {uploadSuccess && <span style={{ color: 'green', marginLeft: 12 }}>{uploadSuccess}</span>}
        {resolvedQualificationId && (
          <div style={{ marginTop: 8, color: '#23395d' }}>Resolved QualificationId: {resolvedQualificationId}</div>
        )}
        {Array.isArray(topics) && topics.length > 0 && (
          <details style={{ marginTop: 12 }}>
            <summary style={{ cursor: 'pointer', fontWeight: 600 }}>
              Captured Data Diagnostics (optional)
            </summary>
            <div style={{ marginTop: 8 }}>
              {(() => {
                const total = topics.length;
                const count = (k) => topics.reduce((acc, t) => acc + (String(t?.[k] ?? '').trim() ? 1 : 0), 0);
                const unique = (k) => {
                  const set = new Set(topics.map(t => String(t?.[k] ?? '').trim()).filter(Boolean));
                  return set.size;
                };
                return (
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 8 }}>
                    <div><strong>Total Topics:</strong> {total}</div>
                    <div><strong>Subject Id rows:</strong> {count('subjectId')} (unique {unique('subjectId')})</div>
                    <div><strong>Subject Credits rows:</strong> {count('subjectCredits')}</div>
                    <div><strong>Notional Hours rows:</strong> {count('notionalHours')}</div>
                    <div><strong>Periods per Topic rows:</strong> {count('periodsPerTopic')}</div>
                    <div><strong>Topic Code rows:</strong> {count('topicCode')}</div>
                    <div><strong>Topic Description rows:</strong> {count('topicDescription')}</div>
                    <div style={{ gridColumn: '1 / -1' }}><strong>Assessment Criteria rows:</strong> {count('assessmentCriteriaDescription')}</div>
                  </div>
                );
              })()}
            </div>
          </details>
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
                <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>PhasesCode</th>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Subject Description</th>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Status</th>
                      <th style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>Body</th>
                    </tr>
                  </thead>
                  <tbody>
                    {uploadDetails.slice(0, 20).map((d, i) => (
                      <tr key={i}>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.row}</td>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.reason}</td>
                  <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.phasesCode ?? d.subjectCode ?? ''}</td>
                        <td style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{d.subjectDescription ?? ''}</td>
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
          <label><GlossaryLabel label="Qualification Id" term="Qualification" />
            <select
              name="qualificationId"
              className="mainpage-input"
              value={form.qualificationId || resolvedQualificationId || ''}
              onChange={(e) => handleQualificationSelect(e.target.value)}
              required
            >
              <option value="">Select qualification...</option>
              {qualifications.map((q) => (
                <option key={q.id} value={String(q.id)}>
                  {q.qualificationNumber
                    ? `${q.qualificationNumber} - ${q.qualificationDescription || `Qualification #${q.id}`}`
                    : (q.qualificationDescription || `Qualification #${q.id}`)}
                </option>
              ))}
            </select>
          </label>
          <label>Phases Code
            <input className="mainpage-input" value={phaseNameById.get(Number(phaseId || 0)) || ''} readOnly />
          </label>
          <label>Subject
            <select name="subjectId" className="mainpage-input" value={form.subjectId} onChange={handleChange}>
              <option value="">Select Subject…</option>
              {subjectsPhase.map(s => (
                <option key={s.id} value={String(s.id)}>
                  {(s.subjectCode ?? s.phasesCode ?? '') + ' — ' + (s.subjectDescription ?? '')}
                </option>
              ))}
            </select>
          </label>
          <label>Subject Code
            <input name="subjectCode" className="mainpage-input" placeholder="Subject Code" value={form.subjectCode} onChange={handleChange} />
          </label>
          <label>Subject Description
            <input name="subjectDescription" className="mainpage-input" placeholder="Subject Description" value={form.subjectDescription} onChange={handleChange} />
          </label>
          <label><GlossaryLabel label="Subject Credits" term="Credit" />
            <input name="subjectCredits" className="mainpage-input" placeholder="Subject Credits" value={form.subjectCredits} onChange={handleChange} />
          </label>
          <label>Notional Hours
            <input name="notionalHours" className="mainpage-input" placeholder="Notional Hours" value={form.notionalHours} onChange={handleChange} />
          </label>
          <label>Periods per Topic
            <input name="periodsPerTopic" className="mainpage-input" placeholder="Periods per Topic" value={form.periodsPerTopic} onChange={handleChange} />
          </label>
          <label>Topic Purpose
            <input name="topicPurpose" maxLength={180} className="mainpage-input" placeholder="Topic Purpose" value={form.topicPurpose} onChange={handleChange} />
          </label>
          <label>Topic Code
            <input name="topicCode" maxLength={40} className="mainpage-input" placeholder="Topic Code" value={form.topicCode} onChange={handleChange} />
          </label>
          <label>Topic Description
            <input name="topicDescription" maxLength={40} className="mainpage-input" placeholder="Topic Description" value={form.topicDescription} onChange={handleChange} />
          </label>
          <label>Topic Credits
            <input name="topicCredits" maxLength={6} className="mainpage-input" placeholder="Topic Credits" value={form.topicCredits} onChange={handleChange} type="number" min="0" />
          </label>
          <label>Topic Percentage
            <input name="topicPercentage" maxLength={4} className="mainpage-input" placeholder="Topic Percentage" value={form.topicPercentage} onChange={handleChange} type="number" min="0" />
          </label>
          <label>Assessment Criteria Description
            <input name="assessmentCriteriaDescription" className="mainpage-input" placeholder="Assessment Criteria Description" value={form.assessmentCriteriaDescription} onChange={handleChange} />
          </label>
          {usesOutcomes && (
            <label>Outcome
              <select name="outcomeId" className="mainpage-input" value={form.outcomeId} onChange={handleChange}>
                <option value="">Select Outcome…</option>
                {outcomes.map(o => (
                  <option key={o.id} value={String(o.id)}>
                    {(o.outcomeCode || o.OutcomeCode || '') + ' — ' + (o.outcomeDescription || o.OutcomeDescription || '')}
                  </option>
                ))}
              </select>
            </label>
          )}
        </div>
        <div style={{ marginTop: 12, padding: 10, border: '1px solid #d8e1ef', borderRadius: 8, background: '#f8fbff' }}>
          <div style={{ fontWeight: 600, marginBottom: 6 }}>Manual Period Override (Selected Subject)</div>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
            <input
              className="mainpage-input"
              placeholder="Periods per Topic"
              value={subjectPeriodsOverride}
              onChange={e => setSubjectPeriodsOverride(e.target.value)}
              style={{ maxWidth: 220 }}
            />
            <button type="button" onClick={applySubjectPeriodsOverride} disabled={applyingSubjectPeriods || !form.subjectId}>
              {applyingSubjectPeriods ? 'Applying...' : 'Apply to Subject Topics'}
            </button>
          </div>
          <div style={{ color: '#334', marginTop: 6, fontSize: '0.92rem' }}>
            Applies one Periods-per-Topic value to all topics in the selected subject and refreshes the learning schedule.
          </div>
        </div>
        <div className="mainpage-form-actions">
          <button type="submit" className="primary-save">Save</button>
          <button type="button" onClick={() => setForm(initialState)}>Clear</button>
          {editingId && <button type="button" onClick={() => setEditingId(null)}>Cancel Edit</button>}
          <button type="button" style={{ marginLeft: 16 }} onClick={() => navigate('/subjects')}>Back</button>
          <button className="next-step-button" type="button" style={{ marginLeft: 16 }} onClick={() => navigate('/lecturer-toolkit')}>Goto Lecturer Toolkit</button>
        </div>
      </form>
      <h3 style={{marginTop:'2rem'}}>Topics List</h3>
      <table className="mainpage-table" style={{ width: '100%', marginTop: 16 }}>
        <thead>
          <tr>
            <th>Topic Code</th>
            <th>Topic Description</th>
            <th>Assessment Criteria Description</th>
            <th>Subject Id</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {topics.map(t => (
            <tr key={t.id}>
              <td>{t.topicCode}</td>
              <td>{t.topicDescription}</td>
              <td>{t.assessmentCriteriaDescription || ''}</td>
              <td>{t.subjectId}</td>
              <td>
                <button onClick={() => handleEdit(t)}>Edit</button>
                <button onClick={() => handleDelete(t.id)}>Delete</button>
              </td>
            </tr>
          ))}
          {topics.length === 0 && (
            <tr>
              <td colSpan="5">No topics found.</td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
};

export default TopicsPage;
