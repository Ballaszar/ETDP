

import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from "../context/QualificationContext";
import { logWorkflowAction, logWorkflowError } from '../utils/workflowLogger';
import { speak } from '../utils/tts';

const apiUrl = '/api/Demographics';

const initialState = {
    numberOfMales: '',
    numberOfFemales: '',
    numberAfrican: '',
    numberWhites: '',
    numberColoureds: '',
    numberAsian: '',
    numberWithDisabilities: '',
    totalNumberOfStudents: ''
};

const numericFields = [
    'numberOfMales',
    'numberOfFemales',
    'numberAfrican',
    'numberWhites',
    'numberColoureds',
    'numberAsian',
    'numberWithDisabilities'
];

const toIntOrZero = value => {
    const text = String(value ?? '').trim();
    return /^\d+$/.test(text) ? Number.parseInt(text, 10) : 0;
};

function calcTotal(d) {
    const vals = numericFields.map(field => toIntOrZero(d[field]));

    return vals.reduce((a, b) => a + b, 0) || '';
}

const toBackendPayload = (form, editingId, qualificationId) => {
    return {
        Id: editingId ? Number(editingId) : undefined,
        NumberOfMales: toIntOrZero(form.numberOfMales),
        NumberOfFemales: toIntOrZero(form.numberOfFemales),
        NumberAfrican: toIntOrZero(form.numberAfrican),
        NumberWhites: toIntOrZero(form.numberWhites),
        NumberColoureds: toIntOrZero(form.numberColoureds),
        NumberAsian: toIntOrZero(form.numberAsian),
        NumberWithDisabilities: toIntOrZero(form.numberWithDisabilities),
        TotalNumberOfStudents: toIntOrZero(form.totalNumberOfStudents),
        QualificationId: Number(qualificationId)
    };
};

const DemographicsPage = () => {
    const { qualificationId } = useQualification();
    const [form, setForm] = useState(initialState);
    const [demographics, setDemographics] = useState([]);
    const [editingId, setEditingId] = useState(null);
    const [error, setError] = useState('');
    const [saveSuccess, setSaveSuccess] = useState('');
    const navigate = useNavigate();

    const loadDemographics = async () => {
        const qid = Number(qualificationId || 0);
        if (!qid) {
            setDemographics([]);
            return;
        }
        try {
            const res = await fetch(`${apiUrl}/byQualification?qualificationId=${qid}`);
            const data = await res.json();
            if (Array.isArray(data)) {
                setDemographics(data);
                logWorkflowAction('Load Demographics', { qualificationId: qid, count: data.length });
                return;
            }
            const msg = data?.error || 'Unexpected response';
            setDemographics([]);
            setError('Failed to load demographics: ' + msg);
            logWorkflowError('Failed to load demographics', { qualificationId: qid, error: msg });
            speak('Failed to load demographics.');
        } catch (e) {
            setDemographics([]);
            setError('Failed to load demographics: ' + e.message);
            logWorkflowError('Failed to load demographics', { qualificationId: qid, error: e.message });
            speak('Failed to load demographics.');
        }
    };

    useEffect(() => {
        loadDemographics();
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [qualificationId]);

    const validate = () => {
        for (const f of numericFields) {
            const raw = String(form[f] ?? '').trim();
            if (raw && (!/^\d+$/.test(raw) || raw.length > 4)) {
                return `${f} must be numeric and max 4 chars.`;
            }
        }

        const totalRaw = String(form.totalNumberOfStudents ?? '').trim();
        if (totalRaw && (!/^\d+$/.test(totalRaw) || totalRaw.length > 8)) {
            return 'Total Number of Students must be numeric and max 8 chars.';
        }

        if (!qualificationId) {
            return 'QualificationId is missing — please save the Qualification first.';
        }

        return '';
    };

    const handleChange = e => {
        const { name, value } = e.target;

        setForm(f => {
            if (numericFields.includes(name) && !/^\d*$/.test(value)) {
                return f;
            }
            const updated = { ...f, [name]: value };
            updated.totalNumberOfStudents = calcTotal(updated);
            return updated;
        });

        setError('');
        setSaveSuccess('');
    };

    const handleSave = async () => {
        const validationError = validate();
        if (validationError) {
            setError(validationError);
            logWorkflowError('Demographics Validation Failed', { error: validationError });
            speak(validationError);
            return;
        }

        const method = editingId ? 'PUT' : 'POST';
        const url = editingId ? `${apiUrl}/${editingId}` : apiUrl;

        try {
            const payload = toBackendPayload(form, editingId, qualificationId);
            logWorkflowAction('Save Demographics', { editingId, payload });
            console.log('Saving Demographics Payload:', JSON.stringify(payload, null, 2));

            const res = await fetch(url, {
                method,
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (!res.ok) {
                const msg = await res.text();
                throw new Error(msg || 'API error');
            }

            setForm(initialState);
            setEditingId(null);

            await loadDemographics();
            logWorkflowAction('Reload Demographics', { qualificationId: Number(qualificationId || 0) });

            speak('Demographics saved successfully.');
            setSaveSuccess('Demographics saved. Use "Goto Curriculum Phases" when ready.');

        } catch (e) {
            setError('Save failed: ' + e.message);
            logWorkflowError('Save Demographics Failed', { error: e.message });
            speak('Save failed. ' + e.message);
        }
    };

    const handleEdit = d => {
        setForm({
            numberOfMales: String(d.numberOfMales ?? ''),
            numberOfFemales: String(d.numberOfFemales ?? ''),
            numberAfrican: String(d.numberAfrican ?? ''),
            numberWhites: String(d.numberWhites ?? ''),
            numberColoureds: String(d.numberColoureds ?? ''),
            numberAsian: String(d.numberAsian ?? ''),
            numberWithDisabilities: String(d.numberWithDisabilities ?? ''),
            totalNumberOfStudents: String(d.totalNumberOfStudents ?? '')
        });

        setEditingId(d.id);
        setError('');
        setSaveSuccess('');
    };

    const handleDelete = async id => {
        try {
            await fetch(`${apiUrl}/${id}`, { method: 'DELETE' });
            await loadDemographics();
        } catch (e) {
            setError('Delete failed: ' + e.message);
        }
    };

    return (
        <div className="mainpage-root">
            <h2 className="mainpage-title">Demographics of Students</h2>

            {error && (
                <div style={{ background: '#ffd2d2', color: '#a00', padding: '0.7rem', borderRadius: 6, marginBottom: 12, fontWeight: 'bold' }}>
                    {error}
                </div>
            )}

            <form className="mainpage-form" onSubmit={e => { e.preventDefault(); handleSave(); }}>
                <div className="mainpage-form-fields-vertical">
                    <label>Number of Males
                        <input name="numberOfMales" maxLength={4} className="mainpage-input" value={form.numberOfMales} onChange={handleChange} type="number" min="0" />
                    </label>

                    <label>Number of Females
                        <input name="numberOfFemales" maxLength={4} className="mainpage-input" value={form.numberOfFemales} onChange={handleChange} type="number" min="0" />
                    </label>

                    <label>Number African
                        <input name="numberAfrican" maxLength={4} className="mainpage-input" value={form.numberAfrican} onChange={handleChange} type="number" min="0" />
                    </label>

                    <label>Number Whites
                        <input name="numberWhites" maxLength={4} className="mainpage-input" value={form.numberWhites} onChange={handleChange} type="number" min="0" />
                    </label>

                    <label>Number Coloureds
                        <input name="numberColoureds" maxLength={4} className="mainpage-input" value={form.numberColoureds} onChange={handleChange} type="number" min="0" />
                    </label>

                    <label>Number Asian
                        <input name="numberAsian" maxLength={4} className="mainpage-input" value={form.numberAsian} onChange={handleChange} type="number" min="0" />
                    </label>

                    <label>Number with Disabilities
                        <input name="numberWithDisabilities" maxLength={4} className="mainpage-input" value={form.numberWithDisabilities} onChange={handleChange} type="number" min="0" />
                    </label>

                    <label>Total Number of Students (auto)
                        <input name="totalNumberOfStudents" maxLength={8} className="mainpage-input" value={form.totalNumberOfStudents} readOnly />
                    </label>
                </div>

                {saveSuccess && (
                    <div style={{ background: '#e8f6eb', color: '#185a3a', padding: '0.7rem', borderRadius: 6, marginBottom: 12, fontWeight: 'bold' }}>
                        {saveSuccess}
                    </div>
                )}

                <div className="mainpage-form-actions">
                    <button type="submit">Save</button>
                    <button type="button" onClick={() => { setForm(initialState); setError(''); setSaveSuccess(''); }}>Clear</button>
                    {editingId && <button type="button" onClick={() => { setEditingId(null); setError(''); }}>Cancel Edit</button>}
                    <button
                        className="next-step-button"
                        type="button"
                        style={{ marginLeft: 16 }}
                        onClick={() => navigate('/phases')}
                    >
                        Goto Curriculum Phases
                    </button>
                </div>
            </form>

            <h3 style={{ marginTop: '2rem' }}>Demographics Records</h3>

            <table style={{ width: '100%', marginTop: 16 }}>
                <thead>
                    <tr>
                        <th>Males</th>
                        <th>Females</th>
                        <th>African</th>
                        <th>Whites</th>
                        <th>Coloureds</th>
                        <th>Asian</th>
                        <th>With Disabilities</th>
                        <th>Total</th>
                        <th>Actions</th>
                    </tr>
                </thead>

                <tbody>
                    {Array.isArray(demographics) && demographics.length > 0 ? demographics.map(d => (
                        <tr key={d.id}>
                            <td>{d.numberOfMales}</td>
                            <td>{d.numberOfFemales}</td>
                            <td>{d.numberAfrican}</td>
                            <td>{d.numberWhites}</td>
                            <td>{d.numberColoureds}</td>
                            <td>{d.numberAsian}</td>
                            <td>{d.numberWithDisabilities}</td>
                            <td>{d.totalNumberOfStudents}</td>
                            <td>
                                <button onClick={() => handleEdit(d)}>Edit</button>
                                <button onClick={() => handleDelete(d.id)}>Delete</button>
                            </td>
                        </tr>
                    )) : (
                        <tr>
                            <td colSpan={9} style={{ textAlign: 'center', color: '#888' }}>No demographics found or failed to load.</td>
                        </tr>
                    )}
                </tbody>

            </table>
        </div>
    );
};

export default DemographicsPage;
