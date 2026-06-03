import React, { forwardRef, useEffect, useImperativeHandle, useMemo, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';

const qId = (q) => Number(q?.id ?? q?.Id ?? 0);
const qNumber = (q) => String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim();
const qDescription = (q) => String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim();
const subjectIdOf = (s) => String(s?.id ?? s?.Id ?? '');
const subjectCodeOf = (s) => String(s?.subjectCode ?? s?.SubjectCode ?? s?.phasesCode ?? s?.PhasesCode ?? '').trim();
const subjectDescriptionOf = (s) => String(s?.subjectDescription ?? s?.SubjectDescription ?? '').trim();
const topicIdOf = (t) => String(t?.id ?? t?.Id ?? '');
const topicCodeOf = (t) => String(t?.topicCode ?? t?.TopicCode ?? '').trim();
const topicDescriptionOf = (t) => String(t?.topicDescription ?? t?.TopicDescription ?? '').trim();
const topicSubjectCodeOf = (t) => String(t?.subjectCode ?? t?.SubjectCode ?? '').trim();

const blankLogRow = () => ({
  subjectCode: '',
  topicCode: '',
  topicDescription: '',
  date: '',
  signature: ''
});

const asText = (value) => String(value ?? '').trim();

const composeInstitutionAddress = (learner) => {
  const streetNumber = asText(learner?.learningInstitutionStreetNumber);
  const streetName = asText(learner?.learningInstitutionStreetName);
  const cityTown = asText(learner?.learningInstitutionCityTown);
  const province = asText(learner?.learningInstitutionProvince);
  const code = asText(learner?.learningInstitutionCityTownPhysicalCode);

  const line1 = `${streetNumber} ${streetName}`.trim();
  return [line1, cityTown, province, code].filter(Boolean).join(', ');
};

const composeEmployerAddress = (learner) => {
  const streetNumber = asText(learner?.workExperienceEmployerStreetNumber);
  const streetName = asText(learner?.workExperienceEmployerStreetName);
  const cityTown = asText(learner?.workExperienceEmployerCityTown);
  const province = asText(learner?.workExperienceEmployerProvince);
  const code = asText(learner?.workExperienceEmployerCityTownCode);

  const line1 = `${streetNumber} ${streetName}`.trim();
  return [line1, cityTown, province, code].filter(Boolean).join(', ');
};

const LOGBOOK_API = '/api/WorkExperienceLogbook';

const buildInitialLogRow = (subjectList) => ({
  ...blankLogRow(),
  subjectCode: Array.isArray(subjectList) && subjectList.length > 0 ? subjectCodeOf(subjectList[0]) : ''
});

const normalizeSavedRows = (rows, subjectList) => {
  const list = Array.isArray(rows) ? rows : [];
  if (list.length === 0) {
    return [buildInitialLogRow(subjectList)];
  }

  return list.map((row) => ({
    subjectCode: asText(row?.subjectCode),
    topicCode: asText(row?.topicCode),
    topicDescription: asText(row?.topicDescription),
    date: asText(row?.date),
    signature: asText(row?.signature)
  }));
};

const hasLogRowContent = (row) => (
  asText(row?.topicCode) ||
  asText(row?.topicDescription) ||
  asText(row?.date) ||
  asText(row?.signature)
);

const formatSavedTimestamp = (value) => {
  if (!value) return '';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return '';
  return parsed.toLocaleString();
};

const WorkExperienceLogbookPage = forwardRef(function WorkExperienceLogbookPage(_props, ref) {
  const location = useLocation();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };

  const [qualifications, setQualifications] = useState([]);
  const [qualificationInfo, setQualificationInfo] = useState(null);
  const [learners, setLearners] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [topics, setTopics] = useState([]);

  const [form, setForm] = useState({
    qualificationId: String(location.state?.qualificationId || qualificationId || localStorage.getItem('qualificationId') || ''),
    learnerId: '',
    learningInstitutionName: '',
    learningInstitutionAddress: '',
    learningInstitutionContactPerson: '',
    learningInstitutionContactPhone: '',
    learningInstitutionContactEmail: '',
    employerName: '',
    employerAddress: '',
    supervisorName: '',
    supervisorPhone: '',
    supervisorEmail: ''
  });
  const [logRows, setLogRows] = useState([blankLogRow()]);
  const [logbookId, setLogbookId] = useState(null);

  const [loading, setLoading] = useState(false);
  const [loadingSaved, setLoadingSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [status, setStatus] = useState('');

  const selectedQualification = useMemo(
    () => qualifications.find((q) => String(qId(q)) === String(form.qualificationId)) || null,
    [qualifications, form.qualificationId]
  );

  const selectedLearner = useMemo(
    () => learners.find((l) => String(l?.id ?? l?.Id) === String(form.learnerId)) || null,
    [learners, form.learnerId]
  );

  useEffect(() => {
    let active = true;
    const loadQualifications = async () => {
      try {
        const res = await fetch('/api/Qualification');
        if (!res.ok) throw new Error(await res.text());
        const data = await res.json();
        if (!active) return;
        const list = Array.isArray(data) ? data : [];
        setQualifications(list);
        if (!form.qualificationId && list.length > 0) {
          setForm((prev) => ({ ...prev, qualificationId: String(qId(list[0])) }));
        }
      } catch (e) {
        if (!active) return;
        setError(`Failed to load qualification list: ${e?.message || e}`);
      }
    };
    loadQualifications();
    return () => { active = false; };
  }, []);

  useEffect(() => {
    let active = true;
    const qid = Number(form.qualificationId || 0);

    const loadByQualification = async () => {
      if (!qid) {
        setQualificationInfo(null);
        setLearners([]);
        setSubjects([]);
        setTopics([]);
        return;
      }

      setLoading(true);
      setError('');
      try {
        const [qualificationRes, learnerRes, subjectsRes, topicsRes] = await Promise.all([
          fetch(`/api/Qualification/${qid}`),
          fetch(`/api/LearnerRegistration?qualificationId=${qid}&take=500`),
          fetch(`/api/Subject/byQualification?qualificationId=${qid}`),
          fetch(`/api/Topic/byQualification?qualificationId=${qid}`)
        ]);

        if (!qualificationRes.ok) throw new Error(await qualificationRes.text());
        if (!learnerRes.ok) throw new Error(await learnerRes.text());
        if (!subjectsRes.ok) throw new Error(await subjectsRes.text());
        if (!topicsRes.ok) throw new Error(await topicsRes.text());

        const [qualificationJson, learnerJson, subjectsJson, topicsJson] = await Promise.all([
          qualificationRes.json(),
          learnerRes.json(),
          subjectsRes.json(),
          topicsRes.json()
        ]);

        if (!active) return;

        const learnerList = Array.isArray(learnerJson) ? learnerJson : [];
        const subjectList = Array.isArray(subjectsJson) ? subjectsJson : [];
        const topicList = Array.isArray(topicsJson) ? topicsJson : [];

        setQualificationInfo(qualificationJson || null);
        setLearners(learnerList);
        setSubjects(subjectList);
        setTopics(topicList);

        setForm((prev) => ({
          ...prev,
          learnerId: prev.learnerId || (learnerList.length > 0 ? String(learnerList[0]?.id ?? learnerList[0]?.Id ?? '') : '')
        }));

        setLogRows((prev) => {
          if (prev.length > 0) return prev;
          return [buildInitialLogRow(subjectList)];
        });
      } catch (e) {
        if (!active) return;
        setError(`Failed to load work-experience data: ${e?.message || e}`);
      } finally {
        if (active) setLoading(false);
      }
    };

    loadByQualification();
    if (qid > 0) setQualificationId(qid);

    return () => { active = false; };
  }, [form.qualificationId, setQualificationId]);

  useEffect(() => {
    const learner = selectedLearner;

    setForm((prev) => ({
      ...prev,
      learningInstitutionName:
        asText(learner?.learningInstitutionName) ||
        asText(qualificationInfo?.learningInstitutionName) ||
        asText(qualificationInfo?.LearningInstitutionName) ||
        prev.learningInstitutionName,
      learningInstitutionAddress: composeInstitutionAddress(learner) || prev.learningInstitutionAddress,
      learningInstitutionContactPerson: asText(learner?.learningInstitutionContactPerson) || prev.learningInstitutionContactPerson,
      learningInstitutionContactPhone: asText(learner?.learningInstitutionContactPersonPhoneNumber) || prev.learningInstitutionContactPhone,
      learningInstitutionContactEmail: asText(learner?.learningInstitutionContactPersonEmailAddress) || prev.learningInstitutionContactEmail,
      employerName: asText(learner?.workExperienceEmployerName) || prev.employerName,
      employerAddress: composeEmployerAddress(learner) || prev.employerAddress,
      supervisorName: asText(learner?.workExperienceEmployerSupervisorName) || prev.supervisorName,
      supervisorPhone: asText(learner?.workExperienceEmployerSupervisorPhoneNumber) || prev.supervisorPhone,
      supervisorEmail: asText(learner?.workExperienceEmployerSupervisorEmailAddress) || prev.supervisorEmail
    }));
  }, [selectedLearner, qualificationInfo]);

  useEffect(() => {
    let active = true;
    const qid = Number(form.qualificationId || 0);
    const learnerId = Number(form.learnerId || 0);

    const loadSavedLogbook = async () => {
      if (!qid) {
        setLogbookId(null);
        setLogRows([buildInitialLogRow(subjects)]);
        setStatus('');
        return;
      }

      if (learners.length > 0 && !learnerId) {
        setLogbookId(null);
        setLogRows([buildInitialLogRow(subjects)]);
        setStatus('');
        return;
      }

      setLoadingSaved(true);
      try {
        const params = new URLSearchParams();
        params.set('qualificationId', String(qid));
        if (learnerId > 0) {
          params.set('learnerId', String(learnerId));
        }

        const res = await fetch(`${LOGBOOK_API}?${params.toString()}`);
        if (res.status === 404) {
          if (!active) return;
          setLogbookId(null);
          setLogRows([buildInitialLogRow(subjects)]);
          setStatus('');
          return;
        }

        if (!res.ok) {
          throw new Error(await res.text());
        }

        const data = await res.json();
        if (!active) return;

        setLogbookId(Number(data?.id || 0) || null);
        setForm((prev) => ({
          ...prev,
          qualificationId: data?.qualificationId ? String(data.qualificationId) : prev.qualificationId,
          learnerId: data?.learnerId ? String(data.learnerId) : prev.learnerId,
          learningInstitutionName: asText(data?.learningInstitutionName),
          learningInstitutionAddress: asText(data?.learningInstitutionAddress),
          learningInstitutionContactPerson: asText(data?.learningInstitutionContactPerson),
          learningInstitutionContactPhone: asText(data?.learningInstitutionContactPhone),
          learningInstitutionContactEmail: asText(data?.learningInstitutionContactEmail),
          employerName: asText(data?.employerName),
          employerAddress: asText(data?.employerAddress),
          supervisorName: asText(data?.supervisorName),
          supervisorPhone: asText(data?.supervisorPhone),
          supervisorEmail: asText(data?.supervisorEmail)
        }));
        setLogRows(normalizeSavedRows(data?.logRows, subjects));
        const stamp = formatSavedTimestamp(data?.updatedAtUtc);
        setStatus(stamp ? `Loaded saved logbook from ${stamp}.` : 'Loaded saved logbook.');
      } catch (e) {
        if (!active) return;
        setError(`Failed to load saved logbook: ${e?.message || e}`);
      } finally {
        if (active) setLoadingSaved(false);
      }
    };

    loadSavedLogbook();
    return () => { active = false; };
  }, [form.qualificationId, form.learnerId, learners.length, subjects]);

  const setField = (name, value) => {
    setForm((prev) => ({ ...prev, [name]: value }));
  };

  const setLogField = (index, field, value) => {
    setLogRows((prev) => prev.map((row, i) => {
      if (i !== index) return row;

      if (field === 'subjectCode') {
        return { ...row, subjectCode: value, topicCode: '', topicDescription: '' };
      }

      if (field === 'topicCode') {
        const match = topics.find((t) => topicCodeOf(t) === value && (!row.subjectCode || topicSubjectCodeOf(t) === row.subjectCode));
        return { ...row, topicCode: value, topicDescription: match ? topicDescriptionOf(match) : row.topicDescription };
      }

      return { ...row, [field]: value };
    }));
  };

  const addLogRow = () => {
    setLogRows((prev) => [...prev, blankLogRow()]);
  };

  const removeLogRow = (index) => {
    setLogRows((prev) => (prev.length <= 1 ? prev : prev.filter((_, i) => i !== index)));
  };

  const handleSaveLogbook = async () => {
    const qid = Number(form.qualificationId || 0);
    if (qid <= 0) {
      throw new Error('Select a qualification first.');
    }

    setSaving(true);
    setError('');
    setStatus('');

    try {
      const qualificationNumber =
        qNumber(selectedQualification) ||
        asText(qualificationInfo?.qualificationNumber) ||
        asText(qualificationInfo?.QualificationNumber);

      const payload = {
        id: logbookId || null,
        qualificationId: qid,
        qualificationNumber,
        learnerId: Number(form.learnerId || 0) > 0 ? Number(form.learnerId) : null,
        learningInstitutionName: form.learningInstitutionName,
        learningInstitutionAddress: form.learningInstitutionAddress,
        learningInstitutionContactPerson: form.learningInstitutionContactPerson,
        learningInstitutionContactPhone: form.learningInstitutionContactPhone,
        learningInstitutionContactEmail: form.learningInstitutionContactEmail,
        employerName: form.employerName,
        employerAddress: form.employerAddress,
        supervisorName: form.supervisorName,
        supervisorPhone: form.supervisorPhone,
        supervisorEmail: form.supervisorEmail,
        logRows: logRows
          .filter(hasLogRowContent)
          .map((row) => ({
            subjectCode: asText(row.subjectCode),
            topicCode: asText(row.topicCode),
            topicDescription: asText(row.topicDescription),
            date: asText(row.date),
            signature: asText(row.signature)
          }))
      };

      const res = await fetch(`${LOGBOOK_API}/save`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!res.ok) {
        throw new Error(await res.text());
      }

      const data = await res.json();
      setLogbookId(Number(data?.id || 0) || null);
      setLogRows(normalizeSavedRows(data?.logRows, subjects));
      const stamp = formatSavedTimestamp(data?.updatedAtUtc);
      const message = stamp ? `Logbook saved to the database at ${stamp}.` : 'Logbook saved to the database.';
      setStatus(message);
      return message;
    } catch (e) {
      const message = e?.message || String(e);
      setError(message);
      throw e;
    } finally {
      setSaving(false);
    }
  };

  useImperativeHandle(ref, () => ({
    saveLogbook: handleSaveLogbook
  }));

  const learnerName = selectedLearner ? `${selectedLearner.learnerFirstName || ''} ${selectedLearner.learnerLastName || ''}`.trim() : '';
  const learnerIdNumber = asText(selectedLearner?.nationalId);
  const learnerContact = asText(selectedLearner?.learnerCellPhoneNumber) || asText(selectedLearner?.learnerPhoneNumber);
  const learnerStudentNumber =
    asText(selectedLearner?.learnerStudentNumber) ||
    asText(selectedLearner?.learnerAlternateId) ||
    asText(selectedLearner?.skillsProgrammeId);

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Work Experience Logbook</h2>
      <p>Capture work-experience employer and learner details, then maintain a detailed work-experience log.</p>

      {error ? <div style={{ color: '#b00020', marginBottom: 10 }}>{error}</div> : null}
      {status ? <div style={{ color: '#1b6347', marginBottom: 10 }}>{status}</div> : null}
      {loadingSaved ? <div style={{ color: '#4a6175', marginBottom: 10 }}>Loading saved logbook...</div> : null}

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Qualification Details</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(220px, 1fr))', gap: 10 }}>
          <label>
            Qualification
            <select
              className="mainpage-input"
              value={form.qualificationId}
              onChange={(e) => setField('qualificationId', e.target.value)}
              disabled={qualifications.length === 0 || loading}
            >
              {qualifications.length === 0 ? <option value="">No qualifications</option> : null}
              {qualifications.map((q) => (
                <option key={qId(q)} value={qId(q)}>
                  {qNumber(q) || qId(q)} - {qDescription(q) || 'No description'}
                </option>
              ))}
            </select>
          </label>
          <div style={{ alignSelf: 'end' }}>
            <strong>Qualification Number:</strong> {qNumber(selectedQualification) || qualificationInfo?.qualificationNumber || qualificationInfo?.QualificationNumber || '-'}
            <br />
            <strong>Qualification Description:</strong> {qDescription(selectedQualification) || qualificationInfo?.qualificationDescription || qualificationInfo?.QualificationDescription || '-'}
          </div>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Learning Institution Details</h3>
        <div className="mainpage-form-fields-vertical">
          <label>
            Learning Institution Name
            <input className="mainpage-input" value={form.learningInstitutionName} onChange={(e) => setField('learningInstitutionName', e.target.value)} />
          </label>
          <label>
            Learning Institution Address
            <input className="mainpage-input" value={form.learningInstitutionAddress} onChange={(e) => setField('learningInstitutionAddress', e.target.value)} />
          </label>
          <label>
            Contact Person Full Name
            <input className="mainpage-input" value={form.learningInstitutionContactPerson} onChange={(e) => setField('learningInstitutionContactPerson', e.target.value)} />
          </label>
          <label>
            Contact Phone Number
            <input className="mainpage-input" value={form.learningInstitutionContactPhone} onChange={(e) => setField('learningInstitutionContactPhone', e.target.value)} />
          </label>
          <label>
            Contact Email Address
            <input className="mainpage-input" value={form.learningInstitutionContactEmail} onChange={(e) => setField('learningInstitutionContactEmail', e.target.value)} />
          </label>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Work Experience Employer Details</h3>
        <div className="mainpage-form-fields-vertical">
          <label>
            Employer Name
            <input className="mainpage-input" value={form.employerName} onChange={(e) => setField('employerName', e.target.value)} />
          </label>
          <label>
            Employer Address
            <input className="mainpage-input" value={form.employerAddress} onChange={(e) => setField('employerAddress', e.target.value)} />
          </label>
          <label>
            Supervisor Full Name
            <input className="mainpage-input" value={form.supervisorName} onChange={(e) => setField('supervisorName', e.target.value)} />
          </label>
          <label>
            Supervisor Phone Number
            <input className="mainpage-input" value={form.supervisorPhone} onChange={(e) => setField('supervisorPhone', e.target.value)} />
          </label>
          <label>
            Supervisor Email Address
            <input className="mainpage-input" value={form.supervisorEmail} onChange={(e) => setField('supervisorEmail', e.target.value)} />
          </label>
        </div>
      </div>

      <div className="form-section">
        <h3 style={{ marginTop: 0 }}>Learner Details</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(260px, 1fr) minmax(260px, 1fr)', gap: 10 }}>
          <label>
            Learner Name and Surname
            <select
              className="mainpage-input"
              value={form.learnerId}
              onChange={(e) => setField('learnerId', e.target.value)}
              disabled={learners.length === 0}
            >
              {learners.length === 0 ? <option value="">No learners</option> : null}
              {learners.map((learner) => {
                const id = learner?.id ?? learner?.Id;
                const fullName = `${learner?.learnerFirstName || ''} ${learner?.learnerLastName || ''}`.trim();
                const nationalId = learner?.nationalId || learner?.NationalId || '-';
                return (
                  <option key={id} value={id}>
                    {fullName || 'Unnamed learner'} - {nationalId}
                  </option>
                );
              })}
            </select>
          </label>
          <div style={{ alignSelf: 'end' }}>
            <div><strong>Learner RSA ID Number:</strong> {learnerIdNumber || '-'}</div>
            <div><strong>Learner Contact Number:</strong> {learnerContact || '-'}</div>
            <div><strong>Learner Student Number:</strong> {learnerStudentNumber || '-'}</div>
          </div>
        </div>
        <div style={{ marginTop: 8, color: '#3d566e' }}>
          <strong>Learner:</strong> {learnerName || '-'}
        </div>
      </div>

      <div className="button-row" style={{ marginBottom: 8 }}>
        <button type="button" onClick={addLogRow}>Add Log Row</button>
        <button type="button" onClick={handleSaveLogbook} disabled={saving || loading}>
          {saving ? 'Saving...' : 'Save Log Book'}
        </button>
        <button type="button" onClick={() => window.print()}>Print Complete Log Book</button>
      </div>

      <div style={{ overflowX: 'auto' }}>
        <table className="table">
          <thead>
            <tr>
              <th>Subject Code</th>
              <th>Topic Code</th>
              <th>Topic Description</th>
              <th>Date</th>
              <th>Signature</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {logRows.map((row, index) => {
              const topicOptions = topics.filter((t) => !row.subjectCode || topicSubjectCodeOf(t) === row.subjectCode);
              return (
                <tr key={`log-row-${index}`}>
                  <td>
                    <select
                      className="mainpage-input"
                      value={row.subjectCode}
                      onChange={(e) => setLogField(index, 'subjectCode', e.target.value)}
                    >
                      <option value="">Select subject</option>
                      {subjects.map((subject) => (
                        <option key={subjectIdOf(subject)} value={subjectCodeOf(subject)}>
                          {subjectCodeOf(subject) || 'SUBJECT'} - {subjectDescriptionOf(subject) || 'No description'}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <select
                      className="mainpage-input"
                      value={row.topicCode}
                      onChange={(e) => setLogField(index, 'topicCode', e.target.value)}
                    >
                      <option value="">Select topic</option>
                      {topicOptions.map((topic) => (
                        <option key={topicIdOf(topic)} value={topicCodeOf(topic)}>
                          {topicCodeOf(topic) || 'TOPIC'}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <input
                      className="mainpage-input"
                      value={row.topicDescription}
                      onChange={(e) => setLogField(index, 'topicDescription', e.target.value)}
                      placeholder="Topic description"
                    />
                  </td>
                  <td>
                    <input
                      className="mainpage-input"
                      type="date"
                      value={row.date}
                      onChange={(e) => setLogField(index, 'date', e.target.value)}
                    />
                  </td>
                  <td>
                    <input
                      className="mainpage-input"
                      value={row.signature}
                      onChange={(e) => setLogField(index, 'signature', e.target.value)}
                      placeholder="Signature"
                    />
                  </td>
                  <td>
                    <button type="button" onClick={() => removeLogRow(index)} disabled={logRows.length <= 1}>Remove</button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
});

WorkExperienceLogbookPage.displayName = 'WorkExperienceLogbookPage';

export default WorkExperienceLogbookPage;
