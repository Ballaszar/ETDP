import React, { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useQualification } from "../context/QualificationContext";

const API_BASE = "/api";

export default function AssessmentCriteriaReview() {
  const navigate = useNavigate();
  const { qualificationId } = useQualification();
  const [criteria, setCriteria] = useState([]);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!qualificationId) return;

    const load = async () => {
      try {
        const res = await fetch(
          `${API_BASE}/AssessmentCriteria/byQualification?qualificationId=${qualificationId}`
        );
        if (!res.ok) throw new Error("Failed to load assessment criteria");
        const data = await res.json();
        const normalized = (Array.isArray(data) ? data : []).map((c) => ({
          id: Number(c.id ?? c.Id ?? 0),
          outcome: String(
            c.outcomeDescription ??
            c.OutcomeDescription ??
            c.topicDescription ??
            c.TopicDescription ??
            (c.topicId ?? c.TopicId ? `Topic #${c.topicId ?? c.TopicId}` : "")
          ),
          criterion: String(
            c.criterionDescription ??
            c.CriterionDescription ??
            c.description ??
            c.Description ??
            ""
          ),
          weight: c.weight ?? c.Weight ?? ""
        })).filter(c => c.id > 0);
        setCriteria(normalized);
      } catch (e) {
        setError(e.message);
      }
    };

    load();
  }, [qualificationId]);

  const handleDelete = async (id) => {
    try {
      const res = await fetch(`${API_BASE}/AssessmentCriteria/${id}`, {
        method: "DELETE",
      });
      if (!res.ok) throw new Error("Delete failed");
      setCriteria((prev) => prev.filter((c) => c.id !== id));
    } catch (e) {
      setError(e.message);
    }
  };

  const handleEdit = (c) => {
    navigate("/assessment-criteria", {
      state: { qualificationId, assessmentCriteriaId: c.id },
    });
  };

  return (
    <div className="page-container">
      <h2>Assessment Criteria Review</h2>

      {error && <div className="error-banner">{error}</div>}

      {/* Data captured indicator */}
      {criteria.length > 0 && !error && (
        <div className="success-banner">Data was captured for this page.</div>
      )}
      {/* Fallback if no records */}
      {criteria.length === 0 && !error && (
        <div className="info-banner">No assessment criteria captured yet, but you may proceed.</div>
      )}

      <table className="table">
        <thead>
          <tr>
            <th>Outcome</th>
            <th>Criterion</th>
            <th>Weight</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {criteria.map((c) => (
            <tr key={c.id}>
              <td>{c.outcome || "-"}</td>
              <td>{c.criterion || "-"}</td>
              <td>{c.weight}</td>
              <td>
                <button onClick={() => handleEdit(c)}>Edit</button>
                <button onClick={() => handleDelete(c.id)}>Delete</button>
              </td>
            </tr>
          ))}
          {!criteria.length && (
            <tr>
              <td colSpan={4}>No assessment criteria captured yet.</td>
            </tr>
          )}
        </tbody>
      </table>

      <div className="button-row">
        <button onClick={() => navigate("/topics-review")}>Back</button>
            <button onClick={() => navigate("/lecturer-toolkit")}>Save</button>
            <button className="next-step-button" onClick={() => navigate("/lecturer-toolkit")}>Goto Lesson Plan Content</button>
      </div>
    </div>
  );
}
