import React, { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useQualification } from "../context/QualificationContext";

const API_BASE = "/api";

export default function SubjectsListPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId: contextId } = useQualification();
  const qualificationId = location.state?.qualificationId || contextId || null;
  const initialPhaseId = location.state?.curriculumPhaseId || "";

  const [allPhases, setAllPhases] = useState([]);
  const [phaseId, setPhaseId] = useState(initialPhaseId);
  const [subjects, setSubjects] = useState([]);

  useEffect(() => {
    const loadPhases = async () => {
      const res = await fetch(`${API_BASE}/CurriculumPhase`);
      const phases = await res.json();
      setAllPhases(phases);
      if (!initialPhaseId && phases?.length) {
        setPhaseId(String(phases[0].id));
      }
    };
    loadPhases();
  }, []);

  useEffect(() => {
    if (!qualificationId || !phaseId) {
      setSubjects([]);
      return;
    }

    const loadSubjects = async () => {
      const res = await fetch(
        `${API_BASE}/Subject/byPhase?qualificationId=${qualificationId}&phaseId=${phaseId}`
      );
      if (!res.ok) {
        setSubjects([]);
        return;
      }
      const data = await res.json();
      setSubjects(data);
    };

    loadSubjects();
  }, [qualificationId, phaseId]);

  const handleDelete = async (id) => {
    const res = await fetch(`${API_BASE}/Subject/${id}`, {
      method: "DELETE",
    });
    if (!res.ok) return;
    setSubjects((prev) => prev.filter((s) => s.id !== id));
  };

  const handleEdit = (subject) => {
    navigate("/subjects/capture", {
      state: {
        qualificationId,
        curriculumPhaseId: phaseId,
        subjectId: subject.id,
      },
    });
  };

  const handleBack = () => {
    navigate("/subjects/capture", {
      state: { qualificationId, curriculumPhaseId: phaseId },
    });
  };

  const handleNext = () => {
    navigate("/topics", { state: { qualificationId } });
  };

  const getPhaseName = (id) =>
    allPhases.find((p) => p.id === id)?.name || id;

  return (
    <div className="page-container">
      <h2>Curriculum Subjects List</h2>

      <div className="form-section">
        <label>Curriculum Phase</label>
        <select
          value={phaseId}
          onChange={(e) => setPhaseId(e.target.value)}
        >
          <option value="">Select a phase...</option>
          {allPhases.map((phase) => (
            <option key={phase.id} value={phase.id}>
              {phase.name}
            </option>
          ))}
        </select>
      </div>

      <table className="table">
        <thead>
          <tr>
            <th>Purpose</th>
            <th>Code</th>
            <th>Description</th>
            <th>Credits</th>
            <th>NQF Level</th>
            <th>Percentage</th>
            <th>Phase</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {subjects.map((s) => (
            <tr key={s.id}>
              <td>{s.purpose}</td>
              <td>{s.code}</td>
              <td>{s.description}</td>
              <td>{s.credits}</td>
              <td>{s.nqfLevel}</td>
              <td>{s.percentage}</td>
              <td>{getPhaseName(s.curriculumPhaseId)}</td>
              <td>
                <button onClick={() => handleEdit(s)}>Edit</button>
                <button onClick={() => handleDelete(s.id)}>Delete</button>
              </td>
            </tr>
          ))}
          {!subjects.length && (
            <tr>
              <td colSpan={8}>No subjects for this phase yet.</td>
            </tr>
          )}
        </tbody>
      </table>

      <div className="button-row">
        <button onClick={handleBack}>Back</button>
        <button className="next-step-button" onClick={handleNext}>Goto Topics</button>
      </div>
    </div>
  );
}

