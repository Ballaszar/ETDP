

import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';
import * as XLSX from 'xlsx';

const apiUrl = '/api/LessonPlan';

const initialState = {
  content: '',
  bibliography: '',
  assessmentCriteriaId: '', // To be linked to assessment criteria if needed
};

// Convert frontend → backend
const toBackendPayload = (form, id) => {
  const lessonPlan = {
    Id: id ? Number(id) : undefined,
    Content: form.content,
    Bibliography: form.bibliography,
    AssessmentCriteriaId: form.assessmentCriteriaId ? Number(form.assessmentCriteriaId) : null
  };
  return {
    LessonPlan: lessonPlan,
    demo: true
  };
};

const LessonPlanPage = () => {
  const [form, setForm] = useState(initialState);
  const [lessonPlans, setLessonPlans] = useState([]);
  const [editingId, setEditingId] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    fetch(apiUrl)
      .then(res => res.json())
      .then(setLessonPlans)
      .catch(e => setError('Failed to load lesson plans: ' + e.message));
  }, []);

  const handleChange = e => {
    const { name, value } = e.target;
    setForm(f => ({ ...f, [name]: value }));
    setError('');
  };

  const handleSave = async () => {
    const method = editingId ? 'PUT' : 'POST';
    const url = editingId ? `${apiUrl}/${editingId}` : apiUrl;
    const payload = toBackendPayload(form, editingId);
    console.log('Saving LessonPlan Payload:', JSON.stringify(payload, null, 2));
    await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    setForm(initialState);
    setEditingId(null);
    fetch(apiUrl).then(res => res.json()).then(setLessonPlans);
  };

  const handleEdit = lp => {
    setForm(lp);
    setEditingId(lp.id);
  };

  const handleDelete = async id => {
    await fetch(`${apiUrl}/${id}`, { method: 'DELETE' });
    fetch(apiUrl).then(res => res.json()).then(setLessonPlans);
  };

  // Excel template columns
  const excelColumns = [
    "Assessment Criteria Number (AC)",
    "Lesson Plan Number (LPN)",
    "Lesson Plan Description",
    "Lesson Plan Content",
    "Bibliography"
  ];

  // Download template
  const handleDownloadTemplate = () => {
    const ws = XLSX.utils.aoa_to_sheet([excelColumns]);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, "LessonPlanTemplate");
    XLSX.writeFile(wb, "LessonPlanTemplate.xlsx");
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
      // Auto-advance: Preview, then next page (e.g., Lecturer Toolkit)
      setTimeout(() => {
        // You can change this to the next workflow page as needed
        window.location.href = '/lesson-plan-review';
      }, 1200);
    } catch (err) {
      setUploadError(err.message);
    } finally {
      setUploading(false);
    }
  };

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Lesson Plan</h2>

      {/* Excel Upload Section */}
      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginBottom: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ fontWeight: 600, fontSize: '1.1rem', color: '#185a3a', marginBottom: 8 }}>Bulk Upload Lesson Plans (.xlsx)</div>
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
          <label>Lesson Plan Content
            <textarea name="content" maxLength={2000} className="mainpage-input" placeholder="Lesson Plan Content" value={form.content} onChange={handleChange} required />
          </label>
          <label>Bibliography (APA 7th)
            <textarea name="bibliography" maxLength={1000} className="mainpage-input" placeholder="Bibliography (APA 7th)" value={form.bibliography} onChange={handleChange} />
          </label>
          <label>Assessment Criteria Id
            <input name="assessmentCriteriaId" maxLength={8} className="mainpage-input" placeholder="Assessment Criteria Id (optional)" value={form.assessmentCriteriaId} onChange={handleChange} type="number" min="0" />
          </label>
        </div>
        <div className="mainpage-form-actions">
          <button type="submit">Save</button>
          <button type="button" onClick={() => setForm(initialState)}>Clear</button>
          {editingId && <button type="button" onClick={() => setEditingId(null)}>Cancel Edit</button>}
        </div>
      </form>
      <h3 style={{marginTop:'2rem'}}>Lesson Plans List</h3>
      <table style={{ width: '100%', marginTop: 16 }}>
        <thead>
          <tr>
            <th>Content</th><th>Bibliography</th><th>Assessment Criteria Id</th><th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {lessonPlans.map(lp => (
            <tr key={lp.id}>
              <td style={{ maxWidth: 300, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{lp.content}</td>
              <td style={{ maxWidth: 200, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{lp.bibliography}</td>
              <td>{lp.assessmentCriteriaId}</td>
              <td>
                <button onClick={() => handleEdit(lp)}>Edit</button>
                <button onClick={() => handleDelete(lp.id)}>Delete</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default LessonPlanPage;
