
import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';

const apiUrl = '/api/Qualification';

const extractErrorMessage = (text) => {
    const raw = String(text || '').trim();
    if (!raw) return '';
    try {
        const parsed = JSON.parse(raw);
        return String(parsed?.error || parsed?.message || raw).trim();
    } catch {
        return raw;
    }
};

const readApiError = async (res, fallback) => {
    const status = Number(res?.status || 0);
    const statusText = String(res?.statusText || '').trim();
    const body = await res.text().catch(() => '');
    const detail = extractErrorMessage(body);
    const prefix = status > 0 ? `${fallback} (${status}${statusText ? ` ${statusText}` : ''})` : fallback;
    return detail ? `${prefix}: ${detail}` : prefix;
};

const QualificationsPage = () => {
    const [qualifications, setQualifications] = useState([]);
    const [error, setError] = useState('');
    const navigate = useNavigate();

    const { setQualificationId } = useQualification();
    const getId = (q) => Number(q?.id ?? q?.Id ?? 0);
    const v = (q, camel, pascal) => q?.[camel] ?? q?.[pascal] ?? '';
    const toQualificationList = (payload) => {
        if (Array.isArray(payload)) return payload;
        if (Array.isArray(payload?.items)) return payload.items;
        if (Array.isArray(payload?.data)) return payload.data;
        if (Array.isArray(payload?.value)) return payload.value;
        if (Array.isArray(payload?.results)) return payload.results;
        return [];
    };

    useEffect(() => {
        const loadQualifications = async () => {
            try {
                const res = await fetch(apiUrl);
                const payload = await res.json().catch(() => null);
                const list = toQualificationList(payload);

                if (!res.ok) {
                    setError(await readApiError(res, 'Failed to load qualifications'));
                    setQualifications([]);
                    return;
                }

                if (list.length === 0 && payload && !Array.isArray(payload)) {
                    setError(payload?.error || payload?.message || 'Qualifications response was not a list.');
                } else {
                    setError('');
                }

                setQualifications(list);
            } catch (e) {
                setError(e?.message || 'Failed to load qualifications');
                setQualifications([]);
            }
        };

        loadQualifications();
    }, []);

    const handleSelect = (id) => {
        setQualificationId(id);
        navigate('/main');
    };

    const handleCreate = () => {
        setQualificationId(null);        // new qualification
        navigate('/main');
    };

    const handleDelete = async (id) => {
        if (!window.confirm('Are you sure you want to delete this qualification?')) return;
        try {
            const res = await fetch(`${apiUrl}/${id}`, { method: 'DELETE' });
            if (!res.ok) throw new Error(await readApiError(res, 'Delete failed'));
            setQualifications((qs) => Array.isArray(qs) ? qs.filter(q => getId(q) !== id) : []);
        } catch (e) {
            setError(e?.message || 'Delete failed');
        }
    };

    return (
        <div className="qualifications-root">
            <h2>Qualifications</h2>

            {error && (
                <div style={{
                    background: '#ffd2d2',
                    color: '#a00',
                    padding: '0.7rem',
                    borderRadius: 6,
                    marginBottom: 12,
                    fontWeight: 'bold',
                    maxWidth: 600
                }}>
                    {error}
                </div>
            )}

            <button
                onClick={handleCreate}
                style={{
                    marginBottom: '1rem',
                    background: '#185a3a',
                    color: '#fff',
                    border: 'none',
                    borderRadius: '6px',
                    padding: '0.7rem 1.2rem',
                    fontWeight: 'bold',
                    fontSize: '1rem'
                }}
            >
                + Create New Qualification
            </button>

            <table className="mainpage-table">
                <thead>
                    <tr>
                        <th>Name</th><th>Description</th><th>Code</th><th>CESM Field</th><th>Level</th><th>Status</th><th>Institution</th><th>Credits</th><th>Start</th><th>End</th><th>Actions</th>
                    </tr>
                </thead>
                <tbody>
                    {qualifications.length === 0 ? (
                        <tr>
                            <td colSpan={11} style={{ textAlign: 'center', color: '#888' }}>
                                No qualifications found.
                            </td>
                        </tr>
                    ) : qualifications.map(q => (
                        <tr key={getId(q)}>
                            <td>{v(q, 'qualificationDescription', 'QualificationDescription')}</td>
                            <td>{v(q, 'purpose', 'Purpose')}</td>
                            <td>{v(q, 'qualificationNumber', 'QualificationNumber')}</td>
                            <td>{v(q, 'cesmField', 'CesmField')}</td>
                            <td>{v(q, 'nqfLevel', 'NqfLevel')}</td>
                            <td>{v(q, 'qualificationType', 'QualificationType')}</td>
                            <td>{v(q, 'learningInstitutionName', 'LearningInstitutionName')}</td>
                            <td>{v(q, 'credits', 'Credits')}</td>
                            <td>{v(q, 'learningDateStart', 'LearningDateStart') ? String(v(q, 'learningDateStart', 'LearningDateStart')).slice(0, 10) : ''}</td>
                            <td>{v(q, 'learningDateEnd', 'LearningDateEnd') ? String(v(q, 'learningDateEnd', 'LearningDateEnd')).slice(0, 10) : ''}</td>
                            <td>
                                <button onClick={() => {
                                    const id = getId(q);
                                    if (!id) return;
                                    setQualificationId(id);
                                    navigate(`/main?id=${id}`);
                                }}>Edit</button>
                                <button
                                    onClick={() => handleDelete(getId(q))}
                                    style={{
                                        marginLeft: '0.5rem',
                                        background: '#a00',
                                        color: '#fff',
                                        border: 'none',
                                        borderRadius: '4px',
                                        padding: '0.3rem 0.7rem'
                                    }}
                                >
                                    Delete
                                </button>
                            </td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
};

export default QualificationsPage;
