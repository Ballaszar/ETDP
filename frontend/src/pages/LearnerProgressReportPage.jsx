import React, { useEffect, useMemo, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';

const qId = (q) => Number(q?.id ?? q?.Id ?? 0);
const qNumber = (q) => String(q?.qualificationNumber ?? q?.QualificationNumber ?? '').trim();
const qDescription = (q) => String(q?.qualificationDescription ?? q?.QualificationDescription ?? '').trim();

const topicIdOf = (t) => Number(t?.id ?? t?.Id ?? 0);
const topicSubjectCode = (t) => String(t?.subjectCode ?? t?.SubjectCode ?? '').trim();
const topicSubjectDescription = (t) => String(t?.subjectDescription ?? t?.SubjectDescription ?? '').trim();
const topicPurpose = (t) => String(t?.topicPurpose ?? t?.TopicPurpose ?? '').trim();
const topicCode = (t) => String(t?.topicCode ?? t?.TopicCode ?? '').trim();
const topicDescription = (t) => String(t?.topicDescription ?? t?.TopicDescription ?? '').trim();
const topicCredits = (t) => t?.topicCredits ?? t?.TopicCredits ?? '';
const topicPercentage = (t) => t?.topicPercentage ?? t?.TopicPercentage ?? '';

export default function LearnerProgressReportPage() {
  const location = useLocation();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };

  const [qualifications, setQualifications] = useState([]);
  const [qualificationInfo, setQualificationInfo] = useState(null);
  const [learners, setLearners] = useState([]);
  const [topics, setTopics] = useState([]);
  const [criteria, setCriteria] = useState([]);
  const [rows, setRows] = useState([]);

  const [form, setForm] = useState({
    qualificationId: String(location.state?.qualificationId || qualificationId || localStorage.getItem('qualificationId') || ''),
    learnerId: '',
    assessor: '',
    moderator: '',
    dateFrom: String(location.state?.dateFrom || ''),
    dateTo: String(location.state?.dateTo || '')
  });

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

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
        setTopics([]);
        setCriteria([]);
        setRows([]);
        return;
      }

      setLoading(true);
      setError('');
      try {
        const [qRes, learnersRes, topicsRes, criteriaRes] = await Promise.all([
          fetch(`/api/Qualification/${qid}`),
          fetch(`/api/LearnerRegistration?qualificationId=${qid}&take=500`),
          fetch(`/api/Topic/byQualification?qualificationId=${qid}`),
          fetch(`/api/AssessmentCriteria/byQualification?qualificationId=${qid}`)
        ]);

        if (!qRes.ok) throw new Error(await qRes.text());
        if (!learnersRes.ok) throw new Error(await learnersRes.text());
        if (!topicsRes.ok) throw new Error(await topicsRes.text());
        if (!criteriaRes.ok) throw new Error(await criteriaRes.text());

        const [qJson, learnersJson, topicsJson, criteriaJson] = await Promise.all([
          qRes.json(),
          learnersRes.json(),
          topicsRes.json(),
          criteriaRes.json()
        ]);

        if (!active) return;

        const learnerList = Array.isArray(learnersJson) ? learnersJson : [];
        const topicList = Array.isArray(topicsJson) ? topicsJson : [];
        const criteriaList = Array.isArray(criteriaJson) ? criteriaJson : [];

        setQualificationInfo(qJson || null);
        setLearners(learnerList);
        setTopics(topicList);
        setCriteria(criteriaList);

        setForm((prev) => ({
          ...prev,
          learnerId: prev.learnerId || (learnerList.length > 0 ? String(learnerList[0]?.id ?? learnerList[0]?.Id ?? '') : '')
        }));
      } catch (e) {
        if (!active) return;
        setError(`Failed to load progress-report data: ${e?.message || e}`);
        setQualificationInfo(null);
        setLearners([]);
        setTopics([]);
        setCriteria([]);
        setRows([]);
      } finally {
        if (active) setLoading(false);
      }
    };

    loadByQualification();
    if (qid > 0) setQualificationId(qid);

    return () => { active = false; };
  }, [form.qualificationId, setQualificationId]);

  useEffect(() => {
    const criteriaByTopic = criteria.reduce((acc, c) => {
      const topicId = Number(c?.topicId ?? c?.TopicId ?? 0);
      if (!topicId) return acc;
      const existing = acc.get(topicId) || [];
      existing.push(c);
      acc.set(topicId, existing);
      return acc;
    }, new Map());

    const orderedTopics = [...topics].sort((a, b) => {
      const subjectDiff = topicSubjectCode(a).localeCompare(topicSubjectCode(b));
      if (subjectDiff !== 0) return subjectDiff;
      const codeDiff = topicCode(a).localeCompare(topicCode(b));
      if (codeDiff !== 0) return codeDiff;
      return topicIdOf(a) - topicIdOf(b);
    });

    setRows((prevRows) => {
      const prevMap = new Map(prevRows.map((r) => [r.key, r]));
      const nextRows = [];

      orderedTopics.forEach((topic) => {
        const tId = topicIdOf(topic);
        const topicCriteria = criteriaByTopic.get(tId) || [];

        if (topicCriteria.length === 0) {
          const key = `topic-${tId}-criteria-none`;
          const prev = prevMap.get(key);
          nextRows.push({
            key,
            subjectCode: topicSubjectCode(topic),
            subjectDescription: topicSubjectDescription(topic),
            topicPurpose: topicPurpose(topic),
            topicCode: topicCode(topic),
            topicDescription: topicDescription(topic),
            credits: topicCredits(topic),
            percentage: topicPercentage(topic),
            assessmentCriteria: String(topic?.assessmentCriteriaDescription ?? topic?.AssessmentCriteriaDescription ?? '').trim(),
            likert: prev?.likert || '',
            assessorDecision: prev?.assessorDecision || '',
            moderatorDecision: prev?.moderatorDecision || ''
          });
          return;
        }

        topicCriteria.forEach((criterion) => {
          const cId = Number(criterion?.id ?? criterion?.Id ?? 0);
          const key = `topic-${tId}-criteria-${cId || 'x'}`;
          const prev = prevMap.get(key);
          nextRows.push({
            key,
            subjectCode: topicSubjectCode(topic),
            subjectDescription: topicSubjectDescription(topic),
            topicPurpose: topicPurpose(topic),
            topicCode: topicCode(topic),
            topicDescription: topicDescription(topic),
            credits: topicCredits(topic),
            percentage: topicPercentage(topic),
            assessmentCriteria: String(criterion?.description ?? criterion?.Description ?? '').trim(),
            likert: prev?.likert || '',
            assessorDecision: prev?.assessorDecision || '',
            moderatorDecision: prev?.moderatorDecision || ''
          });
        });
      });

      return nextRows;
    });
  }, [topics, criteria]);

  const setField = (name, value) => {
    setForm((prev) => ({ ...prev, [name]: value }));
  };

  const updateRow = (key, patch) => {
    setRows((prev) => prev.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  };

  const clearRatings = () => {
    setRows((prev) => prev.map((row) => ({ ...row, likert: '', assessorDecision: '', moderatorDecision: '' })));
  };

  const learnerFullName = selectedLearner
    ? `${selectedLearner.learnerFirstName || ''} ${selectedLearner.learnerLastName || ''}`.trim()
    : '';

  const qualificationCode =
    qualificationInfo?.qualificationNumber ||
    qualificationInfo?.QualificationNumber ||
    qNumber(selectedQualification) ||
    '-';

  const qualificationName =
    qualificationInfo?.qualificationDescription ||
    qualificationInfo?.QualificationDescription ||
    qDescription(selectedQualification) ||
    '-';

  const deanValue = qualificationInfo?.deanPrincipalCEO || qualificationInfo?.DeanPrincipalCEO || '-';
  const seniorLecturerValue = qualificationInfo?.seniorLecturer || qualificationInfo?.SeniorLecturer || '-';

  return (
    <div className="mainpage-root">
      <h2 className="mainpage-title">Learner Progress Report</h2>
      <p>Capture assessor/moderator learner progress ratings and print the complete progress report.</p>

      <div className="form-section">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(220px, 1fr))', gap: 10 }}>
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
          <label>
            Date From
            <input
              className="mainpage-input"
              type="date"
              value={form.dateFrom}
              onChange={(e) => setField('dateFrom', e.target.value)}
            />
          </label>
          <label>
            Date To
            <input
              className="mainpage-input"
              type="date"
              value={form.dateTo}
              onChange={(e) => setField('dateTo', e.target.value)}
            />
          </label>
          <label>
            Learner
            <select
              className="mainpage-input"
              value={form.learnerId}
              onChange={(e) => setField('learnerId', e.target.value)}
              disabled={learners.length === 0}
            >
              {learners.length === 0 ? <option value="">No learner registrations</option> : null}
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
          <label>
            Assessor
            <input
              className="mainpage-input"
              value={form.assessor}
              onChange={(e) => setField('assessor', e.target.value)}
              placeholder="Assessor full name"
            />
          </label>
          <label>
            Moderator
            <input
              className="mainpage-input"
              value={form.moderator}
              onChange={(e) => setField('moderator', e.target.value)}
              placeholder="Moderator full name"
            />
          </label>
        </div>
      </div>

      {error ? <div style={{ color: '#b00020', marginBottom: 10 }}>{error}</div> : null}

      <div className="form-section">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(260px, 1fr))', gap: 8 }}>
          <div><strong>Qualification Code:</strong> {qualificationCode}</div>
          <div><strong>Dean of the College:</strong> {deanValue}</div>
          <div><strong>Qualification Description:</strong> {qualificationName}</div>
          <div><strong>Senior Lecturer:</strong> {seniorLecturerValue}</div>
          <div><strong>Learner Full Name:</strong> {learnerFullName || '-'}</div>
          <div><strong>Assessor:</strong> {form.assessor || '-'}</div>
          <div><strong>Learner RSA ID No:</strong> {selectedLearner?.nationalId || '-'}</div>
          <div><strong>Moderator:</strong> {form.moderator || '-'}</div>
          <div><strong>Learner Contact No:</strong> {selectedLearner?.learnerCellPhoneNumber || selectedLearner?.learnerPhoneNumber || '-'}</div>
          <div><strong>Date Window:</strong> {form.dateFrom || '-'} to {form.dateTo || '-'}</div>
        </div>
      </div>

      <div className="button-row" style={{ marginBottom: 8 }}>
        <button type="button" onClick={() => window.print()} disabled={rows.length === 0}>Print Complete Progress Report</button>
        <button type="button" onClick={clearRatings} disabled={rows.length === 0}>Clear Ratings</button>
      </div>

      <div style={{ overflowX: 'auto' }}>
        <table className="table">
          <thead>
            <tr>
              <th>Subject Code</th>
              <th>Subject Description</th>
              <th>Topic Purpose</th>
              <th>Topic Code</th>
              <th>Topic Description</th>
              <th>Credits</th>
              <th>%</th>
              <th>Assessment Criteria</th>
              <th>Likert Scale (1-5)</th>
              <th>Assessor Decision</th>
              <th>Moderator Decision</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.key}>
                <td>{row.subjectCode || '-'}</td>
                <td>{row.subjectDescription || '-'}</td>
                <td>{row.topicPurpose || '-'}</td>
                <td>{row.topicCode || '-'}</td>
                <td>{row.topicDescription || '-'}</td>
                <td>{row.credits || '-'}</td>
                <td>{row.percentage || '-'}</td>
                <td>{row.assessmentCriteria || '-'}</td>
                <td>
                  <select
                    className="mainpage-input"
                    value={row.likert}
                    onChange={(e) => updateRow(row.key, { likert: e.target.value })}
                  >
                    <option value="">-</option>
                    <option value="1">1</option>
                    <option value="2">2</option>
                    <option value="3">3</option>
                    <option value="4">4</option>
                    <option value="5">5</option>
                  </select>
                </td>
                <td>
                  <input
                    className="mainpage-input"
                    value={row.assessorDecision}
                    onChange={(e) => updateRow(row.key, { assessorDecision: e.target.value })}
                    placeholder="Assessor decision"
                  />
                </td>
                <td>
                  <input
                    className="mainpage-input"
                    value={row.moderatorDecision}
                    onChange={(e) => updateRow(row.key, { moderatorDecision: e.target.value })}
                    placeholder="Moderator decision"
                  />
                </td>
              </tr>
            ))}
            {rows.length === 0 ? (
              <tr>
                <td colSpan={11}>{loading ? 'Loading report rows...' : 'No topic/assessment rows found for this qualification.'}</td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </div>
  );
}
