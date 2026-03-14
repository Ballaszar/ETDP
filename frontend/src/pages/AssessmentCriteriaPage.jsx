

import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import * as XLSX from 'xlsx';

const apiUrl = '/api/AssessmentCriteria';
const apiQualification = '/api/Qualification';
const apiTopic = '/api/Topic';
const apiOutcome = '/api/Outcome';

const initialState = {
  assessmentCriteriaNumber: '',
  assessmentCriteriaDescription: '',
  subjectDescription: '',
  lessonPlanId: '',
  lessonPlanDescription: '',
  topicId: '', // To be linked to a topic if needed
  outcomeId: '',
};

// Convert frontend → backend
const toBackendPayload = (form, id) => {
  const assessmentCriteria = {
    Id: id ? Number(id) : undefined,
    AssessmentCriteriaNumber: form.assessmentCriteriaNumber,
    AssessmentCriteriaDescription: form.assessmentCriteriaDescription,
    SubjectDescription: form.subjectDescription,
    LessonPlanId: form.lessonPlanId ? Number(form.lessonPlanId) : null,
    LessonPlanDescription: form.lessonPlanDescription,
    TopicId: form.topicId ? Number(form.topicId) : null
  };
  return {
    AssessmentCriteria: assessmentCriteria,
    demo: true
  };
};

const AssessmentCriteriaPage = () => {
  const { qualificationId } = useQualification() || { qualificationId: null };
  const [form, setForm] = useState(initialState);
  const [criteria, setCriteria] = useState([]);
  const [outcomes, setOutcomes] = useState([]);
  const [topics, setTopics] = useState([]);
  const [usesOutcomes, setUsesOutcomes] = useState(false);
  const [editingId, setEditingId] = useState(null);
  const [error, setError] = useState('');
  const navigate = useNavigate();

  useEffect(() => {
    fetch(apiUrl)
      .then(res => res.json())
      .then(setCriteria)
      .catch(e => setError('Failed to load assessment criteria: ' + e.message));
  }, []);

  useEffect(() => {
    const qid = Number(qualificationId || 0);
    if (!qid) {
      setUsesOutcomes(false);
      setOutcomes([]);
      setTopics([]);
      return;
    }
    fetch(`${apiQualification}/${qid}`)
      .then(res => (res.ok ? res.json() : null))
      .then(q => {
        const enabled = Boolean(q?.usesOutcomes ?? q?.UsesOutcomes ?? false);
        setUsesOutcomes(enabled);
        if (enabled) {
          fetch(`${apiOutcome}/byQualification?qualificationId=${qid}`)
            .then(res => (res.ok ? res.json() : []))
            .then((list) => {
              const arr = Array.isArray(list) ? list : [];
              setOutcomes(arr.map(o => ({
                id: Number(o.id ?? o.Id ?? 0),
                description: `${String(o.outcomeCode ?? o.OutcomeCode ?? '').trim()} - ${String(o.outcomeDescription ?? o.OutcomeDescription ?? '').trim()}`
              })).filter(o => o.id > 0));
            })
            .catch(() => setOutcomes([]));
          setTopics([]);
        } else {
          fetch(`${apiTopic}/byQualification?qualificationId=${qid}`)
            .then(res => (res.ok ? res.json() : []))
            .then((list) => {
              const arr = Array.isArray(list) ? list : [];
              setTopics(arr.map(t => ({
                id: Number(t.id ?? t.Id ?? 0),
                description: String(t.topicDescription ?? t.TopicDescription ?? '').trim()
              })).filter(t => t.id > 0));
            })
            .catch(() => setTopics([]));
          setOutcomes([]);
        }
      })
      .catch(() => {
        setUsesOutcomes(false);
        setOutcomes([]);
        setTopics([]);
      });
  }, [qualificationId]);

  useEffect(() => {
    if (!usesOutcomes) return;
    const outcomeId = Number(form.outcomeId || 0);
    if (!outcomeId) {
      setTopics([]);
      return;
    }
    fetch(`${apiTopic}/byOutcome?outcomeId=${outcomeId}`)
      .then(res => (res.ok ? res.json() : []))
      .then((list) => {
        const arr = Array.isArray(list) ? list : [];
        setTopics(arr.map(t => ({
          id: Number(t.id ?? t.Id ?? 0),
          description: String(t.topicDescription ?? t.TopicDescription ?? '').trim()
        })).filter(t => t.id > 0));
      })
      .catch(() => setTopics([]));
  }, [usesOutcomes, form.outcomeId]);

  const handleChange = e => {
    const { name, value } = e.target;
    setForm(f => ({
      ...f,
      [name]: value,
      ...(name === 'outcomeId' ? { topicId: '' } : {})
    }));
    setError('');
  };

  const handleSave = async () => {
    const method = editingId ? 'PUT' : 'POST';
    const url = editingId ? `${apiUrl}/${editingId}` : apiUrl;
    const payload = toBackendPayload(form, editingId);
    console.log('Saving AssessmentCriteria Payload:', JSON.stringify(payload, null, 2));
    await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    // Automatically advance to Lesson Plan page after save
    navigate('/lesson-plan');
    setForm(initialState);
    setEditingId(null);
    fetch(apiUrl).then(res => res.json()).then(setCriteria);
  };

  const handleEdit = c => {
    setForm(c);
    setEditingId(c.id);
  };

  const handleDelete = async id => {
    await fetch(`${apiUrl}/${id}`, { method: 'DELETE' });
    fetch(apiUrl).then(res => res.json()).then(setCriteria);
  };

  // Excel template columns
  const excelColumns = [
    "Qualification Number",
    "Qualification Description",
    "Topic Code",
    "Topic Description",
    "Assessment Criteria Number (AC)",
    "Assessment Criteria Description",
    "Subject Description",
    "Lesson Plan Id",
    "Lesson Plan Description"
  ];

  // Download template
  const handleDownloadTemplate = () => {
    const ws = XLSX.utils.aoa_to_sheet([excelColumns]);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, "AssessmentCriteriaTemplate");
    XLSX.writeFile(wb, "AssessmentCriteriaTemplate.xlsx");
  };

  // Handle Excel upload
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState("");
  const [uploadSuccess, setUploadSuccess] = useState("");
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
      const missing = excelColumns.filter(col => !header.includes(col));
      if (missing.length) throw new Error("Missing columns: " + missing.join(", "));
      // TODO: send to backend for real import
      setUploadSuccess("Upload successful! (Demo: file validated)");
      // Auto-advance: Preview, then Lesson Plan
      navigate("/assessment-criteria-review");
      setTimeout(() => {
        navigate("/lesson-plan");
      }, 1200);
    } catch (err) {
      setUploadError(err.message);
    } finally {
      setUploading(false);
    }
  };

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Assessment Criteria</h2>

      {/* Excel Upload Section */}
      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginBottom: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ fontWeight: 600, fontSize: '1.1rem', color: '#185a3a', marginBottom: 8 }}>Bulk Upload Assessment Criteria (.xlsx)</div>
        <div style={{ marginBottom: 8 }}>
          <span style={{ fontWeight: 500, color: '#23395d' }}>Required Columns:</span>
          <ul style={{ margin: '8px 0 0 18px', color: '#111', fontSize: '1rem' }}>
            {excelColumns.map(col => <li key={col}>{col}</li>)}
          </ul>
        </div>
        <button type="button" onClick={handleDownloadTemplate} style={{ marginRight: 16 }}>Download Template</button>
        <input type="file" accept=".xlsx" onChange={handleExcelUpload} disabled={uploading} style={{ marginRight: 12 }} />
        {uploading && <span>Uploading...</span>}
        {uploadError && <span style={{ color: 'red', marginLeft: 12 }}>{uploadError}</span>}
        {uploadSuccess && <span style={{ color: 'green', marginLeft: 12 }}>{uploadSuccess}</span>}
      </div>

      <form className="mainpage-form" onSubmit={e => { e.preventDefault(); handleSave(); }}>
        <div className="mainpage-form-fields-vertical">
          <label>Criteria Number (AC)
            <input name="assessmentCriteriaNumber" maxLength={4} className="mainpage-input" placeholder="Criteria Number (AC)" value={form.assessmentCriteriaNumber} onChange={handleChange} required />
          </label>
          <label>Criteria Description
            <input name="assessmentCriteriaDescription" maxLength={500} className="mainpage-input" placeholder="Criteria Description" value={form.assessmentCriteriaDescription} onChange={handleChange} required />
          </label>
          <label>Subject Description
            <input name="subjectDescription" maxLength={40} className="mainpage-input" placeholder="Subject Description" value={form.subjectDescription} onChange={handleChange} required />
          </label>
          <label>Lesson Plan Id
            <input name="lessonPlanId" maxLength={6} className="mainpage-input" placeholder="Lesson Plan Id (optional)" value={form.lessonPlanId} onChange={handleChange} type="number" min="0" />
          </label>
          <label>Lesson Plan Description
            <input name="lessonPlanDescription" maxLength={240} className="mainpage-input" placeholder="Lesson Plan Description" value={form.lessonPlanDescription} onChange={handleChange} />
          </label>
          {usesOutcomes && (
            <label>Outcome
              <select name="outcomeId" className="mainpage-input" value={form.outcomeId} onChange={handleChange}>
                <option value="">Select Outcome...</option>
                {outcomes.map(o => (
                  <option key={o.id} value={String(o.id)}>{o.description || `Outcome #${o.id}`}</option>
                ))}
              </select>
            </label>
          )}
          <label>Topic
            <select name="topicId" className="mainpage-input" value={form.topicId} onChange={handleChange}>
              <option value="">Select Topic...</option>
              {topics.map(t => (
                <option key={t.id} value={String(t.id)}>{t.description || `Topic #${t.id}`}</option>
              ))}
            </select>
          </label>
        </div>
        <div className="mainpage-form-actions">
          <button type="submit">Save</button>
          <button type="button" onClick={() => setForm(initialState)}>Clear</button>
          {editingId && <button type="button" onClick={() => setEditingId(null)}>Cancel Edit</button>}
          <button className="next-step-button" type="button" style={{ marginLeft: 16 }} onClick={() => navigate('/lesson-plan')}>Goto Lesson Plan</button>
        </div>
      </form>
      <h3 style={{marginTop:'2rem'}}>Assessment Criteria List</h3>
      <table style={{ width: '100%', marginTop: 16 }}>
        <thead>
          <tr>
            <th>Number</th><th>Description</th><th>Subject</th><th>Lesson Plan Id</th><th>Lesson Plan Description</th><th>Topic Id</th><th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {criteria.map(c => (
            <tr key={c.id}>
              <td>{c.assessmentCriteriaNumber}</td>
              <td>{c.assessmentCriteriaDescription}</td>
              <td>{c.subjectDescription}</td>
              <td>{c.lessonPlanId}</td>
              <td>{c.lessonPlanDescription}</td>
              <td>{c.topicId}</td>
              <td>
                <button onClick={() => handleEdit(c)}>Edit</button>
                <button onClick={() => handleDelete(c.id)}>Delete</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default AssessmentCriteriaPage;
