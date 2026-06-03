import React, { useState, useEffect } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import { useQualification } from "../context/QualificationContext";
import { logWorkflowAction, logWorkflowError } from '../utils/workflowLogger';
import GlossaryLabel from "../components/GlossaryLabel";

const apiUrl = "/api/Qualification";
const qualificationIdOf = (q) => Number(q?.id ?? q?.Id ?? 0);
const qualificationNumberOf = (q) => String(q?.qualificationNumber ?? q?.QualificationNumber ?? "").trim();
const qualificationDescriptionOf = (q) => String(q?.qualificationDescription ?? q?.QualificationDescription ?? "").trim();

const extractErrorMessage = (text) => {
    const raw = String(text || "").trim();
    if (!raw) return "";
    try {
        const parsed = JSON.parse(raw);
        return String(parsed?.error || parsed?.message || raw).trim();
    } catch {
        return raw;
    }
};

const readApiError = async (res, fallback) => {
    const status = Number(res?.status || 0);
    const statusText = String(res?.statusText || "").trim();
    const body = await res.text().catch(() => "");
    const detail = extractErrorMessage(body);
    const prefix = status > 0 ? `${fallback} (${status}${statusText ? ` ${statusText}` : ""})` : fallback;
    return detail ? `${prefix}: ${detail}` : prefix;
};

const emptyForm = {
    name: "",
    description: "",
    code: "",
    level: "",
    credits: "",
    status: "",
    startDate: "",
    endDate: "",
    qualificationNumber: "",
    cesmField: "",
    learningInstitutionName: "",
    seniorLecturer: "",
    logoPath: ""
};

const MainPage = () => {
    const { setQualificationId } = useQualification();
    const [form, setForm] = useState(emptyForm);
    const [editingId, setEditingId] = useState(null);
    const [error, setError] = useState("");
    const [qualificationOptions, setQualificationOptions] = useState([]);

    const navigate = useNavigate();
    const location = useLocation();

    // Map backend DTO -> form shape
    const fromDto = (data) => ({
        name: data?.qualificationDescription ?? data?.name ?? "",
        description: data?.purpose ?? data?.description ?? "",
        code: "",
        level: data?.nqfLevel ?? data?.level ?? "",
        credits: data?.credits ?? "",
        status: data?.qualificationType ?? data?.status ?? "",
        startDate: data?.learningDateStart ? String(data.learningDateStart).slice(0, 10) : "",
        endDate: data?.learningDateEnd ? String(data.learningDateEnd).slice(0, 10) : "",
        qualificationNumber: data?.qualificationNumber ?? "",
        cesmField: data?.cesmField ?? data?.CesmField ?? "",
        learningInstitutionName: data?.learningInstitutionName ?? "",
        seniorLecturer: data?.seniorLecturer ?? "",
        logoPath: data?.logoPath ?? data?.LogoPath ?? ""
    });

    // Map form -> backend DTO
    const toDto = (form) => ({
        QualificationNumber: form.qualificationNumber || "",
        QualificationDescription: form.name || "",
        CesmField: String(form.cesmField || "").trim(),
        NqfLevel: form.level || "",
        Credits: form.credits || "",
        LearningInstitutionName: form.learningInstitutionName || "",
        AccreditationNumber: "",
        DeanPrincipalCEO: null,
        SeniorLecturer: form.seniorLecturer || "",
        LogoPath: form.logoPath || null,
        QualificationType: form.status || "",
        Purpose: form.description || "",
        LearningDateStart: form.startDate || null,
        LearningDateEnd: form.endDate || null
    });

    // Load existing qualification if editing
    useEffect(() => {
        const params = new URLSearchParams(location.search);
        const id = params.get("id");

        if (!id) return;
        let active = true;

        (async () => {
            try {
                const res = await fetch(`${apiUrl}/${id}`);
                if (!res.ok) {
                    throw new Error(await readApiError(res, "Failed to load qualification"));
                }
                const data = await res.json().catch(() => null);
                if (!active || !data || typeof data !== "object") return;
                setForm(fromDto(data));
                setEditingId(id);
                logWorkflowAction('Load Qualification', { id });
            } catch (err) {
                if (!active) return;
                const msg = err?.message || "Failed to load qualification";
                setError(msg);
                logWorkflowError('Failed to load qualification', { error: msg });
            }
        })();

        return () => { active = false; };
    }, [location.search]);

    const handleChange = (e) => {
        const { name, value } = e.target;
        setForm((prev) => ({ ...prev, [name]: value }));
        setError("");
    };

    const [saveSuccess, setSaveSuccess] = useState("");
    const [saveErrorMsg, setSaveErrorMsg] = useState("");
    const [savedId, setSavedId] = useState(null);
    const [logoPreviewUrl, setLogoPreviewUrl] = useState("");
    const [logoUploadStatus, setLogoUploadStatus] = useState("");
    const [logoUploadError, setLogoUploadError] = useState("");
    const [uploadingLogo, setUploadingLogo] = useState(false);
  const [loadQualificationSelectionId, setLoadQualificationSelectionId] = useState("");
  const loadExisting = async () => {
    try {
      const selectedId = Number(loadQualificationSelectionId || 0);
      if (!selectedId) {
        setSaveErrorMsg("Select a qualification to load.");
        return;
      }
      const res = await fetch(`${apiUrl}/${selectedId}`);
      if (!res.ok) {
        setSaveErrorMsg(await readApiError(res, "Load failed"));
        return;
      }
      const item = await res.json();
      if (item && typeof item === "object") {
        setForm(fromDto(item));
        setEditingId(String(selectedId));
        setQualificationId(selectedId);
        setSaveSuccess("");
        setSaveErrorMsg("");
      } else {
        setSaveErrorMsg("Selected qualification could not be loaded.");
      }
    } catch (e) {
      setSaveErrorMsg(e?.message || "Load failed");
    }
  };

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const res = await fetch(apiUrl);
                if (!res.ok) return;
                const data = await res.json().catch(() => []);
                const list = Array.isArray(data) ? data : [];
                if (!active) return;
                setQualificationOptions(list);
                if (!loadQualificationSelectionId && list.length > 0) {
                    setLoadQualificationSelectionId(String(qualificationIdOf(list[0])));
                }
            } catch {
                // Keep page usable even if lookup list fails.
            }
        })();
        return () => { active = false; };
    }, []);

    useEffect(() => {
        return () => {
            if (String(logoPreviewUrl || "").startsWith("blob:")) {
                URL.revokeObjectURL(logoPreviewUrl);
            }
        };
    }, [logoPreviewUrl]);

    const handleLogoUpload = async (e) => {
        const file = e.target.files?.[0];
        if (!file) return;

        setLogoUploadStatus("");
        setLogoUploadError("");
        setUploadingLogo(true);

        const preview = URL.createObjectURL(file);
        setLogoPreviewUrl((prev) => {
            if (String(prev || "").startsWith("blob:")) {
                URL.revokeObjectURL(prev);
            }
            return preview;
        });

        try {
            const fd = new FormData();
            fd.append("file", file);
            const res = await fetch("/api/Qualification/upload-logo", { method: "POST", body: fd });
            if (!res.ok) {
                throw new Error(await readApiError(res, "Logo upload failed"));
            }
            const data = await res.json().catch(() => ({}));
            const storedPath = String(data?.path || "").trim();
            if (storedPath) {
                setForm((prev) => ({ ...prev, logoPath: storedPath }));
            }
            setLogoUploadStatus("Upload Successful");
        } catch (err) {
            setLogoUploadError(err?.message || "Logo upload failed");
        } finally {
            setUploadingLogo(false);
        }
    };

    const handleSave = async () => {
        const cesmField = String(form.cesmField || "").trim();
        if (cesmField.length > 50) {
            setSaveErrorMsg("CESM Field must be 50 characters or fewer.");
            setSaveSuccess("");
            return;
        }

        const method = editingId ? "PUT" : "POST";
        const url = editingId ? `${apiUrl}/${editingId}` : apiUrl;

        const payload = toDto(form);

        try {
            const res = await fetch(url, {
                method,
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            if (!res.ok) {
                const msg = await readApiError(res, "Save failed");
                setSaveErrorMsg(msg);
                setSaveSuccess("");
                return;
            }

            const saved = await res.json();

            if (saved?.id) {
                setQualificationId(saved.id);
                setSavedId(saved.id);
                setSaveSuccess("Saved successfully");
                setSaveErrorMsg("");
                try {
                    const refreshed = await fetch(`${apiUrl}/${saved.id}`).then(r => r.json());
                    if (refreshed && typeof refreshed === 'object') {
                        setForm(fromDto(refreshed));
                        setEditingId(String(saved.id));
                    }
                } catch {}
            } else {
                setSaveErrorMsg("Save failed: No qualification ID returned.");
                setSaveSuccess("");
            }
        } catch (err) {
            setSaveErrorMsg(err?.message || "Save failed");
            setSaveSuccess("");
        }
    };

    return (
        <div className="page-container mainpage-root">
            <h2 className="mainpage-title">Qualification Details</h2>
            {editingId && (
                <div style={{ position: "absolute", right: 24, top: 96, background: "#0b2447", color: "#fff", padding: "6px 10px", borderRadius: 8, fontWeight: 700 }}>
                    {editingId}
                </div>
            )}

            {error && (
                <div
                    style={{
                        background: "#ffd2d2",
                        color: "#a00",
                        padding: "0.7rem",
                        borderRadius: 6,
                        marginBottom: 12,
                        fontWeight: "bold",
                        maxWidth: 600
                    }}
                >
                    {error}
                </div>
            )}

            <button
                type="button"
                style={{
                    marginBottom: "1rem",
                    background: "#185a3a",
                    color: "#fff",
                    border: "none",
                    borderRadius: "6px",
                    padding: "0.7rem 1.2rem",
                    fontWeight: "bold",
                    fontSize: "1rem"
                }}
                onClick={() => navigate("/qualifications")}
            >
                Back to Qualifications
            </button>

            <form
                className="mainpage-form"
                onSubmit={(e) => {
                    e.preventDefault();
                    handleSave();
                }}
            >
                <button
                    type="button"
                    className="mainpage-create-btn"
                    onClick={() => {
                        setEditingId(null);
                        setForm(emptyForm);
                        setError("");
                    }}
                >
                    Create New
                </button>

            <div style={{ marginTop: "12px", background: "#fff", borderRadius: 8, padding: "12px", boxShadow: "0 2px 8px #23395d11", maxWidth: 600 }}>
              <div style={{ fontWeight: 600, marginBottom: 6 }}>Load Existing Qualification</div>
              <div style={{ display: "flex", gap: 8 }}>
                <select
                  className="mainpage-input"
                  value={loadQualificationSelectionId}
                  onChange={e => setLoadQualificationSelectionId(e.target.value)}
                >
                  {qualificationOptions.length === 0 ? (
                    <option value="">No qualifications available</option>
                  ) : null}
                  {qualificationOptions.map((q) => {
                    const id = qualificationIdOf(q);
                    return (
                      <option key={id} value={id}>
                        {(qualificationNumberOf(q) || id) + " - " + (qualificationDescriptionOf(q) || "No description")}
                      </option>
                    );
                  })}
                </select>
                <button type="button" onClick={loadExisting} disabled={!loadQualificationSelectionId}>Load</button>
              </div>
              {saveErrorMsg && <div style={{ marginTop: 6, color: "#a00" }}>{saveErrorMsg}</div>}
            </div>

                <div className="mainpage-form-fields-vertical">
                    <label>
                        Name
                        <input
                            className="mainpage-input"
                            name="name"
                            value={form.name}
                            onChange={handleChange}
                            type="text"
                        />
                    </label>
                    <label>
                        <GlossaryLabel label="Qualification Purpose" term="Qualification" />
                        <input
                            className="mainpage-input"
                            name="description"
                            value={form.description}
                            onChange={handleChange}
                            type="text"
                        />
                    </label>
                    <label>
                        Name of the Learning Institution
                        <input
                            className="mainpage-input"
                            name="learningInstitutionName"
                            value={form.learningInstitutionName}
                            onChange={handleChange}
                            type="text"
                        />
                    </label>
                    <label>
                        Senior Lecturer
                        <input
                            className="mainpage-input"
                            name="seniorLecturer"
                            value={form.seniorLecturer}
                            onChange={handleChange}
                            type="text"
                        />
                    </label>
                    <label>
                        Institution Logo Upload
                        <input
                            className="mainpage-input"
                            type="file"
                            accept=".png,.jpg,.jpeg,.bmp,.gif,.webp"
                            onChange={handleLogoUpload}
                        />
                        {uploadingLogo ? <div style={{ color: "#355", marginTop: 6 }}>Uploading logo...</div> : null}
                        {logoUploadStatus ? <div style={{ color: "#0b7a0b", marginTop: 6 }}>{logoUploadStatus}</div> : null}
                        {logoUploadError ? <div style={{ color: "#a00", marginTop: 6 }}>{logoUploadError}</div> : null}
                        {form.logoPath ? (
                            <div style={{ marginTop: 6, fontSize: 12, color: "#355" }}>
                                Saved Logo Path: {form.logoPath}
                            </div>
                        ) : null}
                        {logoPreviewUrl ? (
                            <img
                                src={logoPreviewUrl}
                                alt="Logo preview"
                                style={{ marginTop: 8, maxHeight: 80, border: "1px solid #d8dfeb", borderRadius: 4 }}
                            />
                        ) : null}
                    </label>
                    <label>
                        Logo Path
                        <input
                            className="mainpage-input"
                            name="logoPath"
                            value={form.logoPath || ""}
                            onChange={handleChange}
                            type="text"
                            placeholder="C:\\ETDP\\ETDP\\Imports\\Logos\\your-logo.png"
                        />
                    </label>
                    
                    <label>
                        <GlossaryLabel label="NQF Level" term="NQF Level" />
                        <input
                            className="mainpage-input"
                            name="level"
                            value={form.level}
                            onChange={handleChange}
                            type="text"
                        />
                    </label>
                    <label>
                        <GlossaryLabel label="Credits" term="Credit" />
                        <input
                            className="mainpage-input"
                            name="credits"
                            value={form.credits}
                            onChange={handleChange}
                            type="text"
                        />
                    </label>
                    <label>
                        Status
                        <input
                            className="mainpage-input"
                            name="status"
                            value={form.status}
                            onChange={handleChange}
                            type="text"
                        />
                    </label>
                    <label>
                        Start Date
                        <input
                            className="mainpage-input"
                            name="startDate"
                            value={form.startDate}
                            onChange={handleChange}
                            type="date"
                        />
                    </label>
                    <label>
                        End Date
                        <input
                            className="mainpage-input"
                            name="endDate"
                            value={form.endDate}
                            onChange={handleChange}
                            type="date"
                        />
                    </label>
                    <label>
                        <GlossaryLabel label="Qualification Number" term="Qualification" />
                        <input
                            className="mainpage-input"
                            name="qualificationNumber"
                            value={form.qualificationNumber}
                            onChange={handleChange}
                            required
                        />
                    </label>
                    <label>
                        CESM Field
                        <input
                            className="mainpage-input"
                            name="cesmField"
                            value={form.cesmField}
                            onChange={handleChange}
                            maxLength={50}
                            placeholder="CESM field (max 50 chars)"
                        />
                    </label>
                </div>

                <div className="mainpage-form-actions">
                    <button type="submit">Save</button>
                    <button
                        type="button"
                        onClick={() => {
                            setForm(emptyForm);
                            setError("");
                        }}
                    >
                        Clear
                    </button>
                    {editingId && (
                        <button
                            type="button"
                            onClick={() => {
                                setEditingId(null);
                                setError("");
                            }}
                        >
                            Cancel Edit
                        </button>
                    )}
                    {saveErrorMsg && (
                        <span style={{ color: "#a00", marginLeft: 12 }}>{saveErrorMsg}</span>
                    )}
                    {saveSuccess && savedId && (
                        <button
                            className="next-step-button"
                            type="button"
                            onClick={() => navigate("/qualification-review")}
                            style={{
                                marginLeft: "12px"
                            }}
                        >
                            Goto Qualification Review
                        </button>
                    )}
                </div>
            </form>

            {(saveSuccess || editingId) && (
                <div style={{ marginTop: 16, background: "#fff", borderRadius: 8, padding: "12px", boxShadow: "0 2px 8px #23395d11" }}>
                    <div style={{ fontWeight: 600, marginBottom: 6 }}>Captured Data Summary</div>
                    <div style={{ display: "grid", gridTemplateColumns: "repeat(2, minmax(0, 1fr))", gap: 8 }}>
                        <div><strong>Name:</strong> {form.name || "—"}</div>
                        <div><strong><GlossaryLabel label="Qualification Number" term="Qualification" />:</strong> {form.qualificationNumber || "—"}</div>
                        <div><strong>CESM Field:</strong> {form.cesmField || "—"}</div>
                        <div><strong><GlossaryLabel label="NQF Level" term="NQF Level" />:</strong> {form.level || "—"}</div>
                        <div><strong><GlossaryLabel label="Credits" term="Credit" />:</strong> {form.credits || "—"}</div>
                        <div><strong>Status:</strong> {form.status || "—"}</div>
                        <div><strong>Start Date:</strong> {form.startDate || "—"}</div>
                        <div><strong>End Date:</strong> {form.endDate || "—"}</div>
                        <div style={{ gridColumn: "1 / -1" }}><strong>Qualification Purpose:</strong> {form.description || "—"}</div>
                        <div><strong>Institution:</strong> {form.learningInstitutionName || "—"}</div>
                        <div><strong>Senior Lecturer:</strong> {form.seniorLecturer || "—"}</div>
                        <div style={{ gridColumn: "1 / -1" }}><strong>Logo Path:</strong> {form.logoPath || "—"}</div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default MainPage;
