import React, { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useQualification } from "../context/QualificationContext";

const API_BASE = "/api";

export default function TopicsReview() {
  const navigate = useNavigate();
  const { qualificationId } = useQualification();
  const [topics, setTopics] = useState([]);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!qualificationId) return;

    const load = async () => {
      try {
        const res = await fetch(
          `${API_BASE}/Topic/byQualification?qualificationId=${qualificationId}`
        );
        if (!res.ok) throw new Error("Failed to load topics");
        const data = await res.json();
        setTopics(data);
      } catch (e) {
        setError(e.message);
      }
    };

    load();
  }, [qualificationId]);

  const handleDelete = async (id) => {
    try {
      const res = await fetch(`${API_BASE}/Topic/${id}`, {
        method: "DELETE",
      });
      if (!res.ok) throw new Error("Delete failed");
      setTopics((prev) => prev.filter((t) => t.id !== id));
    } catch (e) {
      setError(e.message);
    }
  };

  const handleEdit = (topic) => {
    navigate("/topics", {
      state: { qualificationId, topicId: topic.id },
    });
  };

  return (
    <div className="page-container">
      <h2>Topics Review</h2>

      {error && <div className="error-banner">{error}</div>}

      {/* Data captured indicator */}
      {topics.length > 0 && !error && (
        <div className="success-banner">Data was captured for this page.</div>
      )}
      {/* Fallback if no records */}
      {topics.length === 0 && !error && (
        <div className="info-banner">No topics captured yet, but you may proceed.</div>
      )}

      <table className="table">
        <thead>
          <tr>
            <th>Code</th>
            <th>Description</th>
            <th>Credits</th>
            <th>NQF Level</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {topics.map((t) => (
            <tr key={t.id}>
              <td>{t.code}</td>
              <td>{t.description}</td>
              <td>{t.credits}</td>
              <td>{t.nqfLevel}</td>
              <td>
                <button onClick={() => handleEdit(t)}>Edit</button>
                <button onClick={() => handleDelete(t.id)}>Delete</button>
              </td>
            </tr>
          ))}
          {!topics.length && (
            <tr>
              <td colSpan={5}>No topics captured yet.</td>
            </tr>
          )}
        </tbody>
      </table>

      <div className="button-row">
        <button onClick={() => navigate("/subjects-review")}>Back</button>
        <button onClick={() => navigate("/lecturer-toolkit")}>Save</button>
        <button className="next-step-button" onClick={() => navigate("/lecturer-toolkit")}>Goto Lecturer Toolkit</button>
      </div>
    </div>
  );
}
