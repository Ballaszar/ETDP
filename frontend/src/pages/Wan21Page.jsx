import React, { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useQualification } from '../context/QualificationContext';

const API_ROOT = '/api';

const asInt = (value, fallback = 0) => {
  const n = Number(value);
  return Number.isFinite(n) ? Math.trunc(n) : fallback;
};

const normalizeQualification = (row) => ({
  id: asInt(row?.id ?? row?.Id, 0),
  code: String(row?.qualificationNumber ?? row?.QualificationNumber ?? '').trim(),
  description: String(row?.qualificationDescription ?? row?.QualificationDescription ?? '').trim()
});

const normalizeTopic = (row) => ({
  id: asInt(row?.id ?? row?.Id, 0),
  code: String(row?.topicCode ?? row?.TopicCode ?? '').trim(),
  description: String(row?.topicDescription ?? row?.TopicDescription ?? '').trim(),
  subjectCode: String(row?.subjectCode ?? row?.SubjectCode ?? '').trim(),
  order: asInt(row?.order ?? row?.Order, 0)
});

export default function Wan21Page() {
  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId } = useQualification() || { qualificationId: null };

  const stateQualificationId = asInt(location?.state?.qualificationId, 0);
  const initialQualificationId = String(stateQualificationId || qualificationId || localStorage.getItem('qualificationId') || '');

  const [qualifications, setQualifications] = useState([]);
  const [topics, setTopics] = useState([]);
  const [selectedQualificationId, setSelectedQualificationId] = useState(initialQualificationId);
  const [selectedTopicId, setSelectedTopicId] = useState('');
  const [parameterInput, setParameterInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const selectedTopic = useMemo(
    () => topics.find((topic) => String(topic.id) === String(selectedTopicId)) || null,
    [topics, selectedTopicId]
  );

  useEffect(() => {
    let active = true;
    const loadQualifications = async () => {
      setError('');
      try {
        const res = await fetch(`${API_ROOT}/Qualification`);
        if (!res.ok) throw new Error(await res.text());
        const json = await res.json();
        if (!active) return;
        const list = (Array.isArray(json) ? json : [])
          .map(normalizeQualification)
          .filter((q) => q.id > 0);
        setQualifications(list);
        if (!selectedQualificationId && list.length > 0) {
          setSelectedQualificationId(String(list[0].id));
        }
      } catch (e) {
        if (!active) return;
        setError(`Failed to load qualifications: ${e?.message || e}`);
      }
    };
    loadQualifications();
    return () => { active = false; };
  }, []);

  useEffect(() => {
    let active = true;
    const qid = asInt(selectedQualificationId, 0);
    if (!qid) {
      setTopics([]);
      setSelectedTopicId('');
      return () => { active = false; };
    }

    const loadTopics = async () => {
      setLoading(true);
      setError('');
      try {
        const res = await fetch(`${API_ROOT}/Topic/byQualification?qualificationId=${qid}`);
        if (!res.ok) throw new Error(await res.text());
        const json = await res.json();
        if (!active) return;
        const list = (Array.isArray(json) ? json : [])
          .map(normalizeTopic)
          .filter((topic) => topic.id > 0)
          .sort((a, b) => {
            const subjectCmp = String(a.subjectCode || '').localeCompare(String(b.subjectCode || ''), undefined, { sensitivity: 'base', numeric: true });
            if (subjectCmp !== 0) return subjectCmp;
            const orderCmp = Number(a.order || 0) - Number(b.order || 0);
            if (orderCmp !== 0) return orderCmp;
            return String(a.code || '').localeCompare(String(b.code || ''), undefined, { sensitivity: 'base', numeric: true });
          });
        setTopics(list);
        setSelectedTopicId((prev) => (list.some((topic) => String(topic.id) === String(prev)) ? prev : (list[0] ? String(list[0].id) : '')));
      } catch (e) {
        if (!active) return;
        setError(`Failed to load topics: ${e?.message || e}`);
        setTopics([]);
      } finally {
        if (active) setLoading(false);
      }
    };
    loadTopics();
    return () => { active = false; };
  }, [selectedQualificationId]);

  useEffect(() => {
    if (!selectedTopic) return;
    setParameterInput(String(selectedTopic.description || '').trim());
  }, [selectedTopicId, selectedTopic]);

  const openWanEditor = () => {
    const qid = asInt(selectedQualificationId, 0);
    const tid = asInt(selectedTopicId, 0);
    const source = String(parameterInput || selectedTopic?.description || '').trim();
    if (!qid) {
      setError('Select a qualification first.');
      return;
    }
    if (!tid) {
      setError('Select a topic first.');
      return;
    }
    if (!source) {
      setError('Topic parameter input cannot be empty.');
      return;
    }
    navigate('/text-to-video-editor', {
      state: {
        qualificationId: qid,
        topicId: tid,
        topicDescription: source,
        sourceText: source
      }
    });
  };

  return (
    <div className="page-container">
      <h2>WAN 2.1 Studio</h2>
      <p>Use Topic Description as parameter input, then open WAN 2.1 on a dedicated full page.</p>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 10 }}>
        <label>
          Qualification
          <select
            className="mainpage-input"
            value={selectedQualificationId}
            onChange={(e) => {
              setSelectedQualificationId(e.target.value);
              setSelectedTopicId('');
            }}
          >
            <option value="">Select qualification</option>
            {qualifications.map((q) => (
              <option key={q.id} value={q.id}>
                {q.code || `Q-${q.id}`} - {q.description || 'No description'}
              </option>
            ))}
          </select>
        </label>

        <label>
          Topic
          <select
            className="mainpage-input"
            value={selectedTopicId}
            onChange={(e) => setSelectedTopicId(e.target.value)}
            disabled={loading || topics.length === 0}
          >
            <option value="">{loading ? 'Loading topics...' : 'Select topic'}</option>
            {topics.map((topic) => (
              <option key={topic.id} value={topic.id}>
                {topic.subjectCode ? `${topic.subjectCode} | ` : ''}{topic.code || `TOP-${topic.id}`} - {topic.description || 'No topic description'}
              </option>
            ))}
          </select>
        </label>
      </div>

      <label style={{ display: 'block', marginTop: 12 }}>
        WAN 2.1 Parameter Input (Topic Description)
        <textarea
          className="mainpage-input"
          rows={6}
          value={parameterInput}
          onChange={(e) => setParameterInput(e.target.value)}
          placeholder="Topic description is loaded here. Edit before opening WAN 2.1."
        />
      </label>

      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 12 }}>
        <button type="button" onClick={openWanEditor}>Open WAN 2.1 Editor</button>
        <button type="button" onClick={() => navigate('/lecturer-assistance')}>Back to Lecturer Assistance Hub</button>
      </div>

      {error ? <div style={{ marginTop: 10, color: '#b00020', fontWeight: 600 }}>{error}</div> : null}
    </div>
  );
}

