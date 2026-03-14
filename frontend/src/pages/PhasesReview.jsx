import React, { useEffect, useMemo, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useQualification } from "../context/QualificationContext";

const API_BASE = "/api";

const toArray = (value) => {
  if (!value) return [];
  return Array.isArray(value) ? value : [value];
};

const fetchList = async (url) => {
  const res = await fetch(url, { cache: "no-store" });
  if (!res.ok) throw new Error(`${url} (${res.status})`);
  return toArray(await res.json());
};

const resolveQualificationNumericId = async (rawValue) => {
  const raw = String(rawValue ?? "").trim();
  const numeric = Number(raw || 0);

  if (numeric > 0) {
    const probe = await fetch(`${API_BASE}/Qualification/${numeric}`, { cache: "no-store" });
    if (probe.ok) return numeric;
    if (probe.status !== 404) return numeric;
  }

  if (!raw) return 0;

  const search = await fetch(`${API_BASE}/Qualification/search?text=${encodeURIComponent(raw)}`, { cache: "no-store" });
  if (!search.ok) return numeric > 0 ? numeric : 0;
  const list = toArray(await search.json());
  const exact = list.find((q) => String(q?.qualificationNumber ?? q?.QualificationNumber ?? "").trim() === raw);
  const fallback = list.length === 1 ? list[0] : null;
  const resolved = Number(exact?.id ?? exact?.Id ?? fallback?.id ?? fallback?.Id ?? 0);
  return resolved > 0 ? resolved : (numeric > 0 ? numeric : 0);
};

export default function PhasesReview() {
  const navigate = useNavigate();
  const location = useLocation();
  const { qualificationId, setQualificationId } = useQualification() || { qualificationId: null, setQualificationId: () => {} };
  const qualificationRef = location.state?.qualificationId ?? qualificationId ?? localStorage.getItem("qualificationId") ?? null;

  const [resolvedNumericId, setResolvedNumericId] = useState(0);
  const [phaseRows, setPhaseRows] = useState([]);
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;
    const load = async () => {
      setError("");
      try {
        const qid = await resolveQualificationNumericId(qualificationRef);
        if (!active) return;
        setResolvedNumericId(Number(qid || 0));
        if (Number(qid || 0) > 0) setQualificationId(Number(qid));
        if (!qid) {
          setPhaseRows([]);
          return;
        }

        const [links, phases] = await Promise.all([
          fetchList(`${API_BASE}/QualificationPhase/${qid}`),
          fetchList(`${API_BASE}/CurriculumPhase`)
        ]);

        if (!active) return;

        const byPhaseId = new Map(phases.map((p) => [Number(p?.id ?? 0), p]));
        const mapped = links.map((link) => {
          const curriculumPhaseId = Number(link?.curriculumPhaseId ?? 0);
          const phase = byPhaseId.get(curriculumPhaseId) || null;
          return {
            qualificationPhaseId: Number(link?.id ?? 0),
            curriculumPhaseId,
            name: phase?.name || `Phase #${curriculumPhaseId}`,
            description: phase?.description || "",
            sequence: phase?.sequence ?? ""
          };
        });
        setPhaseRows(mapped);
      } catch (e) {
        if (!active) return;
        setError(e?.message || "Failed to load phases.");
      }
    };

    load();
    return () => {
      active = false;
    };
  }, [qualificationRef, setQualificationId]);

  const hasData = useMemo(() => Array.isArray(phaseRows) && phaseRows.length > 0, [phaseRows]);

  const handleDelete = async (qualificationPhaseId) => {
    try {
      const res = await fetch(`${API_BASE}/QualificationPhase/${qualificationPhaseId}`, {
        method: "DELETE"
      });
      if (!res.ok) throw new Error(`Delete failed (${res.status})`);
      setPhaseRows((prev) => prev.filter((p) => Number(p.qualificationPhaseId) !== Number(qualificationPhaseId)));
    } catch (e) {
      setError(e?.message || "Delete failed.");
    }
  };

  const handleEdit = (phase) => {
    navigate("/phases", {
      state: {
        qualificationId: resolvedNumericId || qualificationRef,
        qualificationPhaseId: phase.qualificationPhaseId,
        curriculumPhaseId: phase.curriculumPhaseId
      }
    });
  };

  const gotoSubjects = () => {
    navigate("/subjects", {
      state: {
        qualificationId: resolvedNumericId || qualificationRef
      }
    });
  };

  return (
    <div className="page-container">
      <h2>Curriculum Phases Preview</h2>

      <div style={{ background: "#eef6ff", border: "1px solid #cfe2ff", color: "#0b3d91", padding: "0.7rem", borderRadius: 6, marginBottom: 12 }}>
        <strong>Qualification Id:</strong> {resolvedNumericId || "-"}
      </div>

      {error ? <div className="error-banner">{error}</div> : null}
      {hasData && !error ? <div className="success-banner">Phases uploaded and linked for this qualification.</div> : null}
      {!hasData && !error ? <div className="info-banner">No linked phases found yet for this qualification.</div> : null}

      <table className="table">
        <thead>
          <tr>
            <th>Phase Name</th>
            <th>Description</th>
            <th>Sequence</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {phaseRows.map((p) => (
            <tr key={p.qualificationPhaseId}>
              <td>{p.name}</td>
              <td>{p.description}</td>
              <td>{p.sequence}</td>
              <td>
                <button type="button" onClick={() => handleEdit(p)}>Edit Parameters</button>
                <button type="button" onClick={() => handleDelete(p.qualificationPhaseId)}>Delete</button>
              </td>
            </tr>
          ))}
          {!phaseRows.length ? (
            <tr>
              <td colSpan={4}>No phases captured yet.</td>
            </tr>
          ) : null}
        </tbody>
      </table>

      <div className="button-row">
        <button type="button" onClick={() => navigate("/phases", { state: { qualificationId: resolvedNumericId || qualificationRef } })}>Back</button>
        <button className="next-step-button" type="button" onClick={gotoSubjects}>Goto Subjects</button>
      </div>
    </div>
  );
}
