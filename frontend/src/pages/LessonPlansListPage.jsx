
import React, { useState, useEffect } from 'react';
import { useQualification } from '../context/QualificationContext';

const apiUrl = '/api/LessonPlan';

const LessonPlansListPage = () => {
  const [lessonPlans, setLessonPlans] = useState([]);
  const [editingId, setEditingId] = useState(null);
  const [form, setForm] = useState({ content: '', bibliography: '' });
  const { qualificationId } = useQualification() || { qualificationId: null };
  const activeQualificationId = Number(qualificationId || localStorage.getItem('qualificationId') || 0);

  const loadLessonPlans = async () => {
    if (activeQualificationId <= 0) {
      setLessonPlans([]);
      return;
    }
    const res = await fetch(`${apiUrl}/byQualification?qualificationId=${activeQualificationId}`);
    const body = res.ok ? await res.json().catch(() => []) : [];
    setLessonPlans(Array.isArray(body) ? body : []);
  };

  useEffect(() => {
    loadLessonPlans();
  }, [activeQualificationId]);

  const handleEdit = lp => {
    setForm(lp);
    setEditingId(lp.id);
  };

  const handleDelete = async id => {
    await fetch(`${apiUrl}/${id}`, { method: 'DELETE' });
    loadLessonPlans();
  };

  const handleChange = e => {
    const { name, value } = e.target;
    setForm(f => ({ ...f, [name]: value }));
  };

  const handleSave = async () => {
    if (!editingId) return;
    await fetch(`${apiUrl}/${editingId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ...form, id: editingId }),
    });
    setEditingId(null);
    setForm({ content: '', bibliography: '' });
    loadLessonPlans();
  };

  return (
    <div>
      <h2>Lesson Plans List</h2>
      {activeQualificationId <= 0 && (
        <div style={{ background: '#fff6d8', border: '1px solid #e5c966', borderRadius: 8, padding: 12, color: '#694b00', marginBottom: 16 }}>
          Select a qualification first. This list now stays qualification-scoped to prevent another qualification&apos;s lesson plans from appearing here.
        </div>
      )}
      <table style={{ width: '100%', marginTop: 16 }}>
        <thead>
          <tr>
            <th>Content</th><th>Bibliography</th><th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {lessonPlans.map(lp => (
            <tr key={lp.id}>
              <td style={{ maxWidth: 300, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{lp.content}</td>
              <td style={{ maxWidth: 200, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{lp.bibliography}</td>
              <td>
                <button onClick={() => handleEdit(lp)}>Edit</button>
                <button onClick={() => handleDelete(lp.id)}>Delete</button>
                <button>Goto Next Step</button>
                <button>Back</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {editingId && (
        <div style={{ marginTop: 24, background: '#23395d', padding: 16, borderRadius: 8 }}>
          <h4>Edit Lesson Plan</h4>
          <textarea name="content" maxLength={2000} placeholder="Lesson Plan Content" value={form.content} onChange={handleChange} required />
          <textarea name="bibliography" maxLength={1000} placeholder="Bibliography (APA 7th)" value={form.bibliography} onChange={handleChange} />
          <div style={{ marginTop: 12 }}>
            <button onClick={handleSave}>Save</button>
            <button onClick={() => setEditingId(null)}>Cancel</button>
          </div>
        </div>
      )}
      <div style={{ marginTop: 24 }}>
        <button>Save</button>
        <button>Edit</button>
        <button>Delete</button>
        <button>Goto Next Step</button>
        <button>Back</button>
      </div>
    </div>
  );
};

export default LessonPlansListPage;
