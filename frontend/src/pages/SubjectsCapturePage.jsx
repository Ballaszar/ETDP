

import React, { useEffect, useState } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import { useQualification } from "../context/QualificationContext";
import * as XLSX from 'xlsx';

const API_BASE = "/api";

export default function SubjectsCapturePage() {
  const navigate = useNavigate();
  const location = useLocation();
  const qualificationId = location.state?.qualificationId;
  const qualificationNumericId = location.state?.qualificationNumericId;
  const initialPhaseId = location.state?.curriculumPhaseId || "";

  const [allPhases, setAllPhases] = useState([]);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState("");
  const [uploadSuccess, setUploadSuccess] = useState("");
  const [phaseId, setPhaseId] = useState(initialPhaseId);
  const [subjectId, setSubjectId] = useState(null);
  const [saveError, setSaveError] = useState("");
  const [saveSuccess, setSaveSuccess] = useState("");
  const [subjectsInPhase, setSubjectsInPhase] = useState([]);
  const [uploadHeader, setUploadHeader] = useState([]);
  const [uploadPreviewRows, setUploadPreviewRows] = useState([]);
  const [uploadCreated, setUploadCreated] = useState(0);
  const [uploadFailed, setUploadFailed] = useState(0);
  const [duplicatePhaseId, setDuplicatePhaseId] = useState("");
  const [lastSavedSubject, setLastSavedSubject] = useState(null);
  const [form, setForm] = useState({
    purpose: "",
    code: "",
    description: "",
    credits: "",
    nqfLevel: "",
    percentage: "",
  });

  useEffect(() => {
    const loadPhases = async () => {
      const res = await fetch(`${API_BASE}/CurriculumPhase`);
      const phases = await res.json();
      setAllPhases(phases);
    };
    loadPhases();
  }, []);

  const fetchSubjectsForPhase = async (numericId, pId) => {
    if (!numericId || !pId) return;
    try {
      const res = await fetch(`${API_BASE}/Subject/byPhase?qualificationId=${Number(numericId)}&phaseId=${Number(pId)}`);
      if (!res.ok) return;
      const list = await res.json();
      setSubjectsInPhase(Array.isArray(list) ? list : []);
    } catch {}
  };

  const resolveQualificationNumericId = async (qid) => {
    if (qualificationNumericId) return Number(qualificationNumericId);
    const raw = String(qid ?? "").trim();
    const n = Number(raw || 0);
    if (!Number.isNaN(n) && Number.isFinite(n) && n > 0) {
      try {
        const probe = await fetch(`${API_BASE}/Qualification/${n}`, { cache: "no-store" });
        if (probe.ok) return n;
        if (probe.status !== 404) return n;
      } catch {
        return n;
      }
    }
    if (!raw) return null;
    try {
      const res = await fetch(`${API_BASE}/Qualification/search?text=${encodeURIComponent(raw)}`, { cache: "no-store" });
      if (!res.ok) return null;
      const list = await res.json();
      const exact = Array.isArray(list)
        ? list.find(q => String(q?.qualificationNumber ?? q?.QualificationNumber ?? "").trim() === raw)
        : null;
      const fallback = Array.isArray(list) && list.length === 1 ? list[0] : null;
      const resolved = Number(exact?.id ?? exact?.Id ?? fallback?.id ?? fallback?.Id ?? 0);
      return resolved > 0 ? resolved : null;
    } catch {
      return null;
    }
  };

  const handleChange = (field, value) => {
    setForm((prev) => ({ ...prev, [field]: value }));
  };

  const handlePhaseChange = async (value) => {
    setPhaseId(value);
    setSaveError("");
    setSaveSuccess("");
    const numericId = await resolveQualificationNumericId(qualificationId);
    if (numericId) {
      fetchSubjectsForPhase(numericId, value);
    }
  };

  const handleSave = async () => {
    if (!qualificationId || !phaseId) return;
    const numericId = await resolveQualificationNumericId(qualificationId);
    if (!numericId) return;

    const payload = {
      QualificationId: Number(numericId),
      CurriculumPhaseId: Number(phaseId),
      SubjectPurpose: form.purpose || "",
      SubjectCode: form.code,
      SubjectDescription: form.description,
      SubjectCredits: form.credits ? Number(form.credits) : null,
      SubjectNQFLevel: form.nqfLevel ? Number(form.nqfLevel) : null,
      SubjectPercentage: form.percentage ? Number(form.percentage) : null,
    };

    const method = subjectId ? "PUT" : "POST";
    const url = subjectId
      ? `${API_BASE}/Subject/${subjectId}`
      : `${API_BASE}/Subject`;

    setSaveError("");
    setSaveSuccess("");
    console.log("Saving Subject Payload:", JSON.stringify(payload, null, 2));
    const res = await fetch(url, {
      method,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    if (!res.ok) {
      const text = await res.text().catch(() => "");
      console.error("Subject save failed:", res.status, text);
      setSaveError(text || `Save failed with status ${res.status}`);
      return;
    }
    setSaveSuccess("Subject saved. You can capture another or switch phase.");
    setLastSavedSubject(payload);

    setSubjectId(null);
    setForm({
      purpose: "",
      code: "",
      description: "",
      credits: "",
      nqfLevel: "",
      percentage: "",
    });

    fetchSubjectsForPhase(numericId, phaseId);
  };

  const handleClear = () => {
    setSubjectId(null);
    setForm({
      purpose: "",
      code: "",
      description: "",
      credits: "",
      nqfLevel: "",
      percentage: "",
    });
  };

  const handleBack = () => {
    navigate("/phases", { state: { qualificationId } });
  };

  const handleNext = () => {
    if (!phaseId) return;
    navigate("/subjects-list", {
      state: { qualificationId, curriculumPhaseId: phaseId },
    });
  };

  // Excel template columns
  const normalizeHeader = (value) =>
    String(value ?? "").trim().toLowerCase().replace(/[^a-z0-9]/g, "");
  const findColumnIndex = (header, ...names) => {
    const aliases = new Set(names.map(normalizeHeader).filter(Boolean));
    for (let i = 0; i < header.length; i++) {
      if (aliases.has(normalizeHeader(header[i]))) return i;
    }
    return -1;
  };

  // Download template
  const handleDownloadTemplate = () => {
    window.open("/api/Templates/Subjects", "_blank");
  };

  // Handle Excel upload
  const handleExcelUpload = async (e) => {
    setUploadError("");
    setUploadSuccess("");
    setUploadHeader([]);
    setUploadPreviewRows([]);
    setUploadCreated(0);
    setUploadFailed(0);
    const file = e.target.files[0];
    if (!file) return;
    setUploading(true);
    try {
      const data = await file.arrayBuffer();
      const workbook = XLSX.read(data);
      const sheet = workbook.Sheets[workbook.SheetNames[0]];
      const rawRows = XLSX.utils.sheet_to_json(sheet, { header: 1, raw: false, defval: "" });
      if (!Array.isArray(rawRows) || rawRows.length <= 1) {
        throw new Error("Template file has no data rows.");
      }

      // Handle semicolon CSV files that arrive as a single-column sheet.
      const rows = (Array.isArray(rawRows[0]) && rawRows[0].length === 1 && String(rawRows[0][0] ?? "").includes(";"))
        ? rawRows.map((r) => String(r?.[0] ?? "").split(";").map((cell) => String(cell ?? "").trim()))
        : rawRows;

      const header = Array.isArray(rows[0]) ? rows[0] : [];
      const iPhase = findColumnIndex(header, "Curriculum Phase", "Learning Phases", "Phase Name");
      const iPurpose = findColumnIndex(header, "Subject Purpose", "Phases Purpose");
      const iCode = findColumnIndex(header, "Subject Code", "SubjectCode", "Phases Code", "PhasesCode");
      const iDesc = findColumnIndex(header, "Subject Description", "Subject Decription", "Phases Description");
      const iCredits = findColumnIndex(header, "Subject Credits");
      const iNqf = findColumnIndex(header, "Subject NQF Level");
      const iPct = findColumnIndex(header, "Subject Percentage");
      const iPhaseCode = findColumnIndex(header, "Phases Code", "PhasesCode");
      const iPhaseDescription = findColumnIndex(header, "Phases Description");

      const requiredMissing = [];
      if (iCode < 0) requiredMissing.push("Subject Code");
      if (iDesc < 0) requiredMissing.push("Subject Description");
      if (requiredMissing.length) throw new Error("Missing columns: " + requiredMissing.join(", "));
      if (iPhase < 0 && iPhaseCode < 0 && iPhaseDescription < 0 && !phaseId) {
        throw new Error("Missing columns: Curriculum Phase/Learning Phases/Phases Description (or select a phase in the dropdown)");
      }

      setUploadHeader(header);
      const numericId = await resolveQualificationNumericId(qualificationId);
      if (!numericId) {
        throw new Error("Qualification could not be resolved.");
      }
      let success = 0, failed = 0, preview = [];
      for (let r = 1; r < rows.length; r++) {
        const row = rows[r] || [];
        if (row.length === 0 || row.every(cell => String(cell ?? "").trim() === "")) continue;
        preview.push(row);
        const phaseFromPrimary = iPhase >= 0 ? String(row[iPhase] || "").trim() : "";
        const phaseFromDescription = iPhaseDescription >= 0 ? String(row[iPhaseDescription] || "").trim() : "";
        const phaseFromCode = iPhaseCode >= 0 ? String(row[iPhaseCode] || "").trim() : "";
        const phaseToken = phaseFromPrimary || phaseFromDescription || phaseFromCode;
        const tokenNorm = phaseToken.toLowerCase();
        const foundPhase = allPhases.find(p => {
          const name = String(p?.name ?? "").trim().toLowerCase();
          const desc = String(p?.description ?? "").trim().toLowerCase();
          return tokenNorm && (name === tokenNorm || desc === tokenNorm);
        });
        const chosenPhaseId = foundPhase ? Number(foundPhase.id) : (phaseId ? Number(phaseId) : (allPhases[0]?.id || 0));
        if (!chosenPhaseId) {
          failed++;
          continue;
        }
        const payload = {
          QualificationId: Number(numericId),
          CurriculumPhaseId: Number(chosenPhaseId),
          SubjectPurpose: iPurpose >= 0 ? (row[iPurpose] ?? "") : "",
          SubjectCode: iCode >= 0 ? (row[iCode] ?? "") : "",
          SubjectDescription: iDesc >= 0 ? (row[iDesc] ?? "") : "",
          SubjectCredits: iCredits >= 0 && row[iCredits] !== undefined && row[iCredits] !== null && row[iCredits] !== "" ? Number(row[iCredits]) : null,
          SubjectNQFLevel: iNqf >= 0 && row[iNqf] !== undefined && row[iNqf] !== null && row[iNqf] !== "" ? Number(row[iNqf]) : null,
          SubjectPercentage: iPct >= 0 && row[iPct] !== undefined && row[iPct] !== null && row[iPct] !== "" ? Number(row[iPct]) : null,
        };
        const res = await fetch(`${API_BASE}/Subject`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload),
        });
        if (res.ok) success++; else failed++;
      }
      setUploadCreated(success);
      setUploadFailed(failed);
      setUploadPreviewRows(preview.slice(0, 5));
      setUploadSuccess(`Created ${success} subjects${failed ? `, ${failed} failed` : ""}`);
      fetchSubjectsForPhase(numericId, phaseId || (allPhases[0]?.id || 0));
    } catch (err) {
      setUploadError(err.message);
    } finally {
      setUploading(false);
    }
  };

  const handleDuplicateLast = async () => {
    setSaveError("");
    setSaveSuccess("");
    if (!lastSavedSubject || !duplicatePhaseId) {
      setSaveError("Select destination phase and ensure a subject was just saved.");
      return;
    }
    const numericId = await resolveQualificationNumericId(qualificationId);
    if (!numericId) {
      setSaveError("QualificationId not resolved");
      return;
    }
    const payload = {
      QualificationId: Number(numericId),
      CurriculumPhaseId: Number(duplicatePhaseId),
      SubjectPurpose: lastSavedSubject.SubjectPurpose,
      SubjectCode: lastSavedSubject.SubjectCode,
      SubjectDescription: lastSavedSubject.SubjectDescription,
      SubjectCredits: lastSavedSubject.SubjectCredits,
      SubjectNQFLevel: lastSavedSubject.SubjectNQFLevel,
      SubjectPercentage: lastSavedSubject.SubjectPercentage,
    };
    const res = await fetch(`${API_BASE}/Subject`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
    if (!res.ok) {
      const text = await res.text().catch(() => "");
      setSaveError(text || `Duplicate failed with status ${res.status}`);
      return;
    }
    setSaveSuccess("Duplicated last subject to selected phase.");
    await fetchSubjectsForPhase(numericId, phaseId);
  };

  return (
    <div className="page-container">
      <h2>Curriculum Subjects</h2>

      {/* Excel Upload Section */}
      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginBottom: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ fontWeight: 600, fontSize: '1.1rem', color: '#185a3a', marginBottom: 8 }}>Bulk Upload Subjects (.xlsx)</div>
        <div style={{ marginBottom: 8 }}>
          <span style={{ fontWeight: 500, color: '#23395d' }}>Required Columns:</span>
          <div style={{ marginTop: 4, color: '#555' }}>
            Phase header accepted: Learning Phases / Curriculum Phase / Phases Description (or select a phase in the dropdown)
          </div>
          <ul style={{ margin: '8px 0 0 18px', color: '#111', fontSize: '1rem' }}>
            <li>Subject Code (or SubjectCode / Phases Code)</li>
            <li>Subject Description (or Phases Description)</li>
            <li>Subject Purpose (optional, accepts Phases Purpose)</li>
            <li>Subject Credits (optional)</li>
            <li>Subject NQF Level (optional)</li>
            <li>Subject Percentage (optional)</li>
          </ul>
        </div>
        <button type="button" onClick={handleDownloadTemplate} style={{ marginRight: 16 }}>Download Template</button>
        <input type="file" accept=".xlsx,.csv" onChange={handleExcelUpload} disabled={uploading} style={{ marginRight: 12 }} />
        {uploading && <span>Uploading...</span>}
        {uploadError && <span style={{ color: 'red', marginLeft: 12 }}>{uploadError}</span>}
        {uploadSuccess && <span style={{ color: 'green', marginLeft: 12 }}>{uploadSuccess}</span>}
      </div>

      <div className="form-section">
        {saveError && (
          <div style={{ background: '#ffecec', color: '#b30000', padding: '8px 10px', borderRadius: 6, marginBottom: 12 }}>
            {saveError}
          </div>
        )}
        {saveSuccess && (
          <div style={{ background: '#e9f7ef', color: '#185a3a', padding: '8px 10px', borderRadius: 6, marginBottom: 12 }}>
            {saveSuccess}
          </div>
        )}
        <label>Curriculum Phase</label>
        <select
          value={phaseId}
          onChange={(e) => handlePhaseChange(e.target.value)}
        >
          <option value="">Select a phase...</option>
          {allPhases.map((phase) => (
            <option key={phase.id} value={phase.id}>
              {phase.name}
            </option>
          ))}
        </select>

        <label>Subject Purpose</label>
        <textarea
          value={form.purpose}
          onChange={(e) => handleChange("purpose", e.target.value)}
          maxLength={180}
        />

        <label>Subject Code</label>
        <input
          type="text"
          value={form.code}
          onChange={(e) => handleChange("code", e.target.value)}
          maxLength={40}
        />

        <label>Subject Description</label>
        <input
          type="text"
          value={form.description}
          onChange={(e) => handleChange("description", e.target.value)}
          maxLength={40}
        />

        <label>Subject Credits</label>
        <input
          type="number"
          value={form.credits}
          onChange={(e) => handleChange("credits", e.target.value)}
        />

        <label>Subject NQF Level</label>
        <input
          type="number"
          value={form.nqfLevel}
          onChange={(e) => handleChange("nqfLevel", e.target.value)}
        />

        <label>Subject Percentage</label>
        <input
          type="number"
          value={form.percentage}
          onChange={(e) => handleChange("percentage", e.target.value)}
        />

        <div className="mainpage-form-actions">
          <button className="primary-save" onClick={handleSave}>Save</button>
          <button type="button" onClick={handleClear}>Clear</button>
          <button type="button" onClick={handleBack}>Back</button>
          <button className="next-step-button" type="button" onClick={handleNext}>Goto Subjects List</button>
        </div>
      </div>

      {/* Subjects in selected phase */}
      <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginTop: 24, boxShadow: '0 2px 8px #23395d11' }}>
        <div style={{ fontWeight: 600, fontSize: '1.1rem', color: '#23395d', marginBottom: 8 }}>
          Subjects in Phase {allPhases.find(p => String(p.id) === String(phaseId))?.name || ''}
        </div>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={{ textAlign: 'left', padding: '4px 6px' }}>Code</th>
              <th style={{ textAlign: 'left', padding: '4px 6px' }}>Description</th>
              <th style={{ textAlign: 'left', padding: '4px 6px' }}>Credits</th>
              <th style={{ textAlign: 'left', padding: '4px 6px' }}>NQF Level</th>
              <th style={{ textAlign: 'left', padding: '4px 6px' }}>Percentage</th>
            </tr>
          </thead>
          <tbody>
            {subjectsInPhase.map((s, idx) => (
              <tr key={idx}>
                <td style={{ padding: '4px 6px', borderBottom: '1px solid #eee' }}>{s.subjectCode}</td>
                <td style={{ padding: '4px 6px', borderBottom: '1px solid #eee' }}>{s.subjectDescription}</td>
                <td style={{ padding: '4px 6px', borderBottom: '1px solid #eee' }}>{s.subjectCredits ?? ''}</td>
                <td style={{ padding: '4px 6px', borderBottom: '1px solid #eee' }}>{s.subjectNQFLevel ?? ''}</td>
                <td style={{ padding: '4px 6px', borderBottom: '1px solid #eee' }}>{s.subjectPercentage ?? ''}</td>
              </tr>
            ))}
            {subjectsInPhase.length === 0 && (
              <tr><td colSpan={5} style={{ padding: '8px 6px', color: '#888' }}>No subjects for selected phase</td></tr>
            )}
          </tbody>
        </table>
        <div style={{ marginTop: 8, display: 'flex', gap: 16, alignItems: 'center' }}>
          <span style={{ color: '#23395d' }}>
            Count: {subjectsInPhase.length}
          </span>
          <span style={{ color: '#23395d' }}>
            Total Credits: {subjectsInPhase.reduce((sum, x) => sum + (Number(x.subjectCredits || 0)), 0)}
          </span>
          <span style={{ color: '#23395d' }}>
            Total Percentage: {subjectsInPhase.reduce((sum, x) => sum + (Number(x.subjectPercentage || 0)), 0)}
          </span>
        </div>
        <div style={{ marginTop: 12 }}>
          <div style={{ fontWeight: 600, marginBottom: 6 }}>Duplicate last subject to phase</div>
          <select value={duplicatePhaseId} onChange={e => setDuplicatePhaseId(e.target.value)} style={{ marginRight: 8 }}>
            <option value="">Select a phase...</option>
            {allPhases.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
          <button type="button" onClick={handleDuplicateLast}>Duplicate</button>
        </div>
      </div>

      {/* Upload preview */}
      {uploadHeader.length > 0 && (
        <div style={{ background: '#fff', borderRadius: 8, padding: '1.2rem 1.5rem', marginTop: 16, boxShadow: '0 2px 8px #23395d11' }}>
          <div style={{ fontWeight: 600, marginBottom: 6 }}>Upload Preview (first 5 rows)</div>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr>
                {uploadHeader.map((h, i) => (
                  <th key={i} style={{ borderBottom: '1px solid #ddd', textAlign: 'left', padding: '4px 6px' }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {uploadPreviewRows.map((row, rIdx) => (
                <tr key={rIdx}>
                  {uploadHeader.map((_, cIdx) => (
                    <td key={cIdx} style={{ borderBottom: '1px solid #eee', padding: '4px 6px' }}>{row[cIdx] ?? ''}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
          {(uploadCreated || uploadFailed) ? (
            <div style={{ marginTop: 8 }}>
              <span style={{ color: '#185a3a' }}>Created: {uploadCreated}</span>
              {uploadFailed ? <span style={{ color: '#b30000', marginLeft: 16 }}>Failed: {uploadFailed}</span> : null}
            </div>
          ) : null}
        </div>
      )}
    </div>
  );
}
