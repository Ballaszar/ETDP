import React, { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';

const API_BASE = '/api/Qualification';

const pick = (obj, keys, fallback = '—') => {
    for (const k of keys) {
        if (obj[k] !== undefined && obj[k] !== null && obj[k] !== '') return obj[k];
    }
    return fallback;
};

const normalize = (data) => {
    const merged =
        typeof data === 'object' && data
            ? { ...data, ...(data.qualification || {}), ...(data.Qualification || {}) }
            : {};

    return {
        id: pick(merged, ['id', 'Id']),
        number: pick(merged, ['qualificationNumber', 'QualificationNumber', 'code', 'Code', 'qualificationCode', 'QualificationCode']),
        description: pick(merged, ['qualificationDescription', 'QualificationDescription', 'description', 'Description']),
        cesmField: pick(merged, ['cesmField', 'CesmField']),
        nqfLevel: pick(merged, ['nqfLevel', 'NQFLevel', 'level', 'Level']),
        credits: pick(merged, ['credits', 'Credits', 'totalCredits', 'TotalCredits']),
        institution: pick(merged, ['learningInstitutionName', 'LearningInstitutionName', 'institution', 'Institution']),
        accreditation: pick(merged, ['accreditationNumber', 'AccreditationNumber', 'accreditation', 'Accreditation']),
        deanPrincipalCEO: pick(merged, ['deanPrincipalCEO', 'DeanPrincipalCEO', 'deanPrincipal', 'DeanPrincipal', 'dean', 'Dean']),
        seniorLecturer: pick(merged, ['seniorLecturer', 'SeniorLecturer']),
        logoPath: pick(merged, ['logoPath', 'LogoPath'], null),
        type: pick(merged, ['qualificationType', 'QualificationType', 'type', 'Type']),
        purpose: pick(merged, ['purpose', 'Purpose']),
        startDate: pick(merged, ['learningDateStart', 'LearningDateStart', 'startDate', 'StartDate'], null),
        endDate: pick(merged, ['learningDateEnd', 'LearningDateEnd', 'endDate', 'EndDate'], null),
    };
};

const QualificationReview = () => {
    const location = useLocation();
    const navigate = useNavigate();
    const { qualificationId } = useQualification();
    const [qualification, setQualification] = useState(location.state?.qualification || null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState('');

    useEffect(() => {
        if (!qualification && qualificationId) {
            setLoading(true);
            fetch(`${API_BASE}/${qualificationId}`)
                .then(res => {
                    if (!res.ok) {
                        res.text().then(txt => {
                            setError('Failed to load qualification: ' + txt);
                        });
                        throw new Error('Failed to load qualification');
                    }
                    return res.json();
                })
                .then(data => {
                    setQualification(normalize(data));
                    setError('');
                })
                .catch(e => {
                    setError('Failed to load qualification: ' + e);
                })
                .finally(() => setLoading(false));
        } else if (qualification && !qualification.number) {
            setQualification(normalize(qualification));
        }
    }, [qualification, qualificationId]);

    if (loading) {
        return <div className="loading-banner">Loading...</div>;
    }

    if (!qualification) {
        return (
            <div>
                <div className="info-banner">No qualification data captured yet, but you may proceed.</div>
                <div>No qualification data to review.</div>
                {error && <div className="error-banner">{error}</div>}
            </div>
        );
    }

    return (
        <div className="mainpage-root">
            <h2 className="mainpage-title">Qualification Review</h2>

            <div className="success-banner" style={{ marginBottom: 16 }}>
                Data was captured for this page.
            </div>

            <button
                type="button"
                style={{
                    marginBottom: '1rem',
                    background: '#185a3a',
                    color: '#fff',
                    border: 'none',
                    borderRadius: '6px',
                    padding: '0.7rem 1.2rem',
                    fontWeight: 'bold',
                    fontSize: '1rem',
                }}
                onClick={() => navigate('/qualifications')}
            >
                ← Back to Qualifications
            </button>

            <div style={{ marginBottom: 32, fontSize: '1.25rem', lineHeight: '2.2rem', maxWidth: 900 }}>
                <div><strong>Number:</strong> {qualification.number}</div>
                <div><strong>Description:</strong> {qualification.description}</div>
                <div><strong>CESM Field:</strong> {qualification.cesmField}</div>
                <div><strong>NQF Level:</strong> {qualification.nqfLevel}</div>
                <div><strong>Credits:</strong> {qualification.credits}</div>
                <div><strong>Institution:</strong> {qualification.institution}</div>
                <div><strong>Accreditation:</strong> {qualification.accreditation}</div>
                <div><strong>Dean/Principal/CEO:</strong> {qualification.deanPrincipalCEO}</div>
                <div><strong>Senior Lecturer:</strong> {qualification.seniorLecturer}</div>
                <div><strong>Logo:</strong> {qualification.logoPath ? <img src={qualification.logoPath} alt="Logo" style={{ maxHeight: 40 }} /> : '—'}</div>
                <div><strong>Type:</strong> {qualification.type}</div>
                <div><strong>Purpose:</strong> {qualification.purpose}</div>
                <div><strong>Start Date:</strong> {qualification.startDate ? qualification.startDate.slice(0, 10) : '—'}</div>
                <div><strong>End Date:</strong> {qualification.endDate ? qualification.endDate.slice(0, 10) : '—'}</div>
            </div>

            <div className="button-row">
                <button onClick={() => navigate(`/main?id=${qualification.id}`)} disabled={!qualification.id}>
                    Edit
                </button>
                <button onClick={() => navigate('/qualification-review')} disabled={!qualification.id}>
                    Save
                </button>
                <button
                    className="next-step-button"
                    onClick={() =>
                        navigate('/quality-council-curricula', {
                            state: { qualificationId }
                        })
                    }
                    disabled={!qualification.id}
                >
                    Goto Quality Council Curricula
                </button>
            </div>
        </div>
    );
};

export default QualificationReview;
