import React, { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useQualification } from "../context/QualificationContext";

const API_BASE = "/api";

export default function SubjectsReview() {
  const navigate = useNavigate();
  const { qualificationId } = useQualification();
  const [subjects, setSubjects] = useState([]);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!qualificationId) return;

    const load = async () => {
      try {
        const res = await fetch(
          `${API_BASE}/Subject/byQualification?qualificationId=${qualificationId}`
        );
        if (!res.ok) throw new Error("Failed to load subjects");
        const data = await res.json();
        setSubjects(data);
      } catch (e) {
        setError(e.message);
      }
    };

    load();
  }, [qualificationId]);

  const handleDelete = async (id) => {
    try {
      const res = await fetch(`${API_BASE}/Subject/${id}`, {
        method: "DELETE",
      });
      if (!res.ok) throw new Error("Delete failed");
      setSubjects((prev) => prev.filter((s) => s.id !== id));
    } catch (e) {
      setError(e.message);
    }
  };

  const handleEdit = (subject) => {
    navigate("/subjects", {
      state: { qualificationId, subjectId: subject.id },
    });
  };

  return (
    <div className="page-container">
      <h2>Subjects Review</h2>

      {error && <div className="error-banner">{error}</div>}

      {/* Data captured indicator */}
      {subjects.length > 0 && !error && (
        <div className="success-banner">Data was captured for this page.</div>
      )}
      {/* Fallback if no records */}
      {subjects.length === 0 && !error && (
        <div className="info-banner">No subjects captured yet, but you may proceed.</div>
      )}

      <table className="table">
        <thead>
          <tr>
            <th>Purpose</th>
            <th>Code</th>
            <th>Description</th>
            <th>Credits</th>
            <th>NQF Level</th>
            <th>Percentage</th>
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
              <td>
                <button onClick={() => handleEdit(s)}>Edit</button>
                <button onClick={() => handleDelete(s.id)}>Delete</button>
              </td>
            </tr>
          ))}
          {!subjects.length && (
            <tr>
              <td colSpan={7}>No subjects captured yet.</td>
            </tr>
          )}
        </tbody>
      </table>

      <div className="button-row">
        <button onClick={() => navigate("/phases-review")}>Back</button>
        <button onClick={() => navigate("/topics")}>Save</button>
        <button className="next-step-button" onClick={() => navigate("/topics")}>Goto Topics</button>
      </div>
    </div>
  );
}
