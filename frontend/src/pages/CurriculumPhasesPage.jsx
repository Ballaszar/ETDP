import React, { useEffect, useMemo, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import * as XLSX from "xlsx";
import { useQualification } from "../context/QualificationContext";
import GlossaryLabel from "../components/GlossaryLabel";

const API_BASE = "/api";
const API_TEMPLATE_PHASES = "/api/Templates/Phases";
const DEFAULT_PHASES = [
    { id: 1, name: "Fundamental Learning", description: "", sequence: 1 },
    { id: 2, name: "Knowledge Learning", description: "", sequence: 2 },
    { id: 3, name: "Practical Learning", description: "", sequence: 3 },
    { id: 4, name: "Workplace Experience", description: "", sequence: 4 }
];

const normalizePhaseName = (value) => String(value ?? "").trim().toLowerCase();
const normalizeHeader = (value) => String(value ?? "").trim().toLowerCase().replace(/[^a-z0-9]/g, "");
const TEMPLATE_MISMATCH_PREFIX = "Template Qualification Code mismatch:";

const parseSequenceValue = (value, fallback = 1) => {
    const raw = String(value ?? "").trim();
    if (!raw) return Math.max(1, Number(fallback) || 1);

    const normalized = raw.includes(",") && !raw.includes(".") ? raw.replace(",", ".") : raw;
    const num = Number(normalized);
    if (!Number.isFinite(num)) return Math.max(1, Number(fallback) || 1);

    return Math.max(1, Math.round(num));
};

const findColumnIndex = (header, ...names) => {
    const normalizedNames = new Set(names.map(normalizeHeader).filter(Boolean));
    for (let i = 0; i < header.length; i++) {
        if (normalizedNames.has(normalizeHeader(header[i]))) return i;
    }
    return -1;
};

const readCell = (row, index) => {
    if (index < 0) return "";
    return String(row?.[index] ?? "").trim();
};

const PHASE_TEMPLATE_COLUMN_META = [
    {
        key: "qualificationCode",
        label: "Qualification Code",
        aliases: ["Qualification Code", "Qaulification Code", "Qualification Number"],
        required: false
    },
    {
        key: "learningPhases",
        label: "Learning Phases",
        aliases: ["Learning Phases", "Phase Name", "Name"],
        required: false
    },
    {
        key: "phasesCode",
        label: "Phases Code",
        aliases: ["Phases Code", "Phase Code"],
        required: false
    },
    {
        key: "phasesDescription",
        label: "Phases Description",
        aliases: ["Phases Description", "Description"],
        required: false
    },
    {
        key: "sequence",
        label: "Sequence",
        aliases: ["Sequence", "Order"],
        required: false
    }
];

const buildPhaseTemplateDiagnostics = (rows, metadata = {}) => {
    const safeRows = Array.isArray(rows) ? rows : [];
    const header = Array.isArray(safeRows[0]) ? safeRows[0] : [];

    const cQualificationCode = findColumnIndex(header, ...PHASE_TEMPLATE_COLUMN_META[0].aliases);
    const cLearningPhases = findColumnIndex(header, ...PHASE_TEMPLATE_COLUMN_META[1].aliases);
    const cPhasesCode = findColumnIndex(header, ...PHASE_TEMPLATE_COLUMN_META[2].aliases);
    const cPhasesDescription = findColumnIndex(header, ...PHASE_TEMPLATE_COLUMN_META[3].aliases);
    const cSequence = findColumnIndex(header, ...PHASE_TEMPLATE_COLUMN_META[4].aliases);

    const headerMappings = [
        {
            ...PHASE_TEMPLATE_COLUMN_META[0],
            index: cQualificationCode,
            matchedHeader: cQualificationCode >= 0 ? readCell(header, cQualificationCode) : ""
        },
        {
            ...PHASE_TEMPLATE_COLUMN_META[1],
            index: cLearningPhases,
            matchedHeader: cLearningPhases >= 0 ? readCell(header, cLearningPhases) : ""
        },
        {
            ...PHASE_TEMPLATE_COLUMN_META[2],
            index: cPhasesCode,
            matchedHeader: cPhasesCode >= 0 ? readCell(header, cPhasesCode) : ""
        },
        {
            ...PHASE_TEMPLATE_COLUMN_META[3],
            index: cPhasesDescription,
            matchedHeader: cPhasesDescription >= 0 ? readCell(header, cPhasesDescription) : ""
        },
        {
            ...PHASE_TEMPLATE_COLUMN_META[4],
            index: cSequence,
            matchedHeader: cSequence >= 0 ? readCell(header, cSequence) : ""
        }
    ];

    const missingRequired = [];
    if (cLearningPhases < 0 && cPhasesCode < 0) {
        missingRequired.push("Learning Phases OR Phases Code");
    }

    const extractedRows = [];
    for (let r = 1; r < safeRows.length; r++) {
        const row = Array.isArray(safeRows[r]) ? safeRows[r] : [];
        if (row.length === 0 || row.every(cell => String(cell ?? "").trim() === "")) continue;

        const qualificationCode = readCell(row, cQualificationCode);
        const learningPhase = readCell(row, cLearningPhases);
        const phaseCode = readCell(row, cPhasesCode);
        const phaseName = learningPhase || phaseCode;
        const phaseDescription = readCell(row, cPhasesDescription);
        const phaseSequence = parseSequenceValue(readCell(row, cSequence), r);
        const reason = phaseName ? "" : "Learning Phases/Phases Code missing";

        extractedRows.push({
            row: r,
            qualificationCode,
            learningPhase,
            phaseCode,
            phaseName,
            phaseDescription,
            phaseSequence,
            isValid: Boolean(phaseName),
            reason
        });
    }

    const validRowCount = extractedRows.filter(row => row.isValid).length;

    return {
        fileName: String(metadata.fileName || ""),
        sheetName: String(metadata.sheetName || ""),
        header: header.map(cell => String(cell ?? "").trim()),
        headerMappings,
        missingRequired,
        dataRowCount: extractedRows.length,
        validRowCount,
        invalidRowCount: Math.max(0, extractedRows.length - validRowCount),
        extractedRows
    };
};

export default function CurriculumPhasesPage() {
    const navigate = useNavigate();
    const location = useLocation();
    const { qualificationId: contextId, setQualificationId } = useQualification();

    const qualificationId = location.state?.qualificationId || contextId || null;

    const [allPhases, setAllPhases] = useState([]);
    const [qualificationPhases, setQualificationPhases] = useState([]);
    const [selectedPhaseName, setSelectedPhaseName] = useState("");
    const [manualPhaseName, setManualPhaseName] = useState("");
    const [manualPhaseDescription, setManualPhaseDescription] = useState("");
    const [manualPhaseSequence, setManualPhaseSequence] = useState("");
    const [editingPhaseId, setEditingPhaseId] = useState(null);
    const [editingPhaseLinkId, setEditingPhaseLinkId] = useState(null);
    const [errors, setErrors] = useState([]);
    const [saveMessage, setSaveMessage] = useState("");
    const [importing, setImporting] = useState(false);
    const [resolvedNumericId, setResolvedNumericId] = useState(null);
    const [qualificationInfo, setQualificationInfo] = useState(null);
    const [templateFile, setTemplateFile] = useState(null);
    const [importSummary, setImportSummary] = useState(null);
    const [importPreviewRows, setImportPreviewRows] = useState([]);
    const [templateDiagnostics, setTemplateDiagnostics] = useState(null);

    const pushError = (msg) => {
        if (!msg) return;
        setErrors(prev => (prev.includes(msg) ? prev : [...prev, msg]));
    };

    const clearTemplateMismatchErrors = () => {
        setErrors(prev => prev.filter(msg => !String(msg || "").startsWith(TEMPLATE_MISMATCH_PREFIX)));
    };

    const extractTemplateQualificationCodes = (diagnostics) => {
        const codes = new Set();
        for (const row of diagnostics?.extractedRows || []) {
            const code = String(row?.qualificationCode || "").trim();
            if (code) codes.add(code);
        }
        return [...codes];
    };

    const readTemplateWorkbook = async (file) => {
        const data = await file.arrayBuffer();
        const workbook = XLSX.read(data, { type: "array" });
        const firstSheet = workbook?.SheetNames?.[0];
        if (!firstSheet) {
            throw new Error("Template has no worksheet.");
        }

        const sheet = workbook.Sheets[firstSheet];
        const rows = XLSX.utils.sheet_to_json(sheet, { header: 1, raw: false, defval: "" });
        if (!Array.isArray(rows) || rows.length === 0) {
            throw new Error("Template has no header row.");
        }

        return { rows, firstSheet };
    };

    const handleTemplateFileChange = async (event) => {
        const file = event.target.files?.[0] || null;
        clearTemplateMismatchErrors();
        setTemplateFile(file);
        setImportSummary(null);
        setImportPreviewRows([]);
        setSaveMessage("");

        if (!file) {
            setTemplateDiagnostics(null);
            return;
        }

        try {
            const { rows, firstSheet } = await readTemplateWorkbook(file);
            const diagnostics = buildPhaseTemplateDiagnostics(rows, {
                fileName: file.name,
                sheetName: firstSheet
            });
            setTemplateDiagnostics(diagnostics);

            if (diagnostics.missingRequired.length > 0) {
                pushError(`Template header warning: missing ${diagnostics.missingRequired.join(", ")}.`);
            }
            if (diagnostics.dataRowCount === 0) {
                pushError("Template header loaded but there are no data rows to import.");
            }
            if (diagnostics.dataRowCount > 0 && diagnostics.missingRequired.length === 0) {
                setSaveMessage(`Template "${file.name}" loaded. Click "Import Selected File" to persist and link phases.`);
            }

            const selectedQualificationCode = String(qualificationInfo?.qualificationNumber || "").trim();
            const templateQualificationCodes = extractTemplateQualificationCodes(diagnostics);
            if (
                selectedQualificationCode &&
                templateQualificationCodes.length > 0 &&
                !templateQualificationCodes.includes(selectedQualificationCode)
            ) {
                setSaveMessage(
                    `${TEMPLATE_MISMATCH_PREFIX} selected qualification is "${selectedQualificationCode}", file contains ${templateQualificationCodes.join(", ")}. Import will continue and link to the selected qualification.`
                );
            }
        } catch (e) {
            setTemplateDiagnostics(null);
            pushError(`Failed to read selected template: ${String(e?.message || e)}`);
        }
    };

    const phaseOptions = useMemo(() => {
        const merged = [];
        const seen = new Set();
        for (const source of [allPhases, DEFAULT_PHASES]) {
            for (const phase of source) {
                const key = normalizePhaseName(phase?.name);
                if (!key || seen.has(key)) continue;
                seen.add(key);
                merged.push({
                    id: phase?.id,
                    name: String(phase?.name ?? ""),
                    description: String(phase?.description ?? ""),
                    sequence: Number(phase?.sequence ?? 0)
                });
            }
        }
        return merged.sort((a, b) => Number(a.sequence || 9999) - Number(b.sequence || 9999));
    }, [allPhases]);

    const resolveQualificationNumericId = async (qid) => {
        const n = Number(qid);
        const raw = String(qid ?? "").trim();

        if (!Number.isNaN(n) && Number.isFinite(n) && n > 0) {
            try {
                const probe = await fetch(`${API_BASE}/Qualification/${n}`, { cache: "no-store" });
                if (probe.ok) return n;
            } catch {
                // Continue with code-based resolution.
            }
        }

        if (!raw) return null;

        try {
            const res = await fetch(`${API_BASE}/Qualification/search?text=${encodeURIComponent(raw)}`, { cache: "no-store" });
            if (res.ok) {
                const list = await res.json();
                const exact = Array.isArray(list)
                    ? list.find(q =>
                        String(q?.qualificationNumber ?? q?.QualificationNumber ?? "").trim() === raw ||
                        Number(q?.id ?? q?.Id ?? 0) === n
                    )
                    : null;
                const fallback = Array.isArray(list) && list.length === 1 ? list[0] : null;
                const resolved = Number(exact?.id ?? exact?.Id ?? fallback?.id ?? fallback?.Id ?? 0);
                if (resolved > 0) return resolved;
            }
        } catch (e) {
            pushError(`Qualification search exception: ${String(e?.message || e)}`);
        }

        try {
            const allRes = await fetch(`${API_BASE}/Qualification`, { cache: "no-store" });
            if (allRes.ok) {
                const all = await allRes.json();
                const exactAll = Array.isArray(all)
                    ? all.find(q => String(q?.qualificationNumber ?? q?.QualificationNumber ?? "").trim() === raw)
                    : null;
                const resolvedAll = Number(exactAll?.id ?? exactAll?.Id ?? 0);
                if (resolvedAll > 0) return resolvedAll;
            }
        } catch {
            // Best effort final fallback below.
        }

        pushError(`Qualification not found for "${raw}". Open Main Menu and reselect qualification.`);
        return null;
    };

    const refreshPhaseLists = async (numericId) => {
        try {
            const phasesRes = await fetch(`${API_BASE}/CurriculumPhase`);
            if (!phasesRes.ok) throw new Error("Failed to load phases");
            const phases = await phasesRes.json();
            setAllPhases(Array.isArray(phases) ? phases : []);
        } catch (e) {
            pushError(`Failed to load phases: ${String(e)}`);
            setAllPhases([]);
        }

        if (!numericId) {
            setQualificationPhases([]);
            return;
        }

        try {
            const qPhasesRes = await fetch(`${API_BASE}/QualificationPhase/${numericId}`);
            if (!qPhasesRes.ok) {
                const txt = await qPhasesRes.text().catch(() => "");
                pushError(`Failed to load qualification phases (${qPhasesRes.status}): ${txt || "No details"}`);
                setQualificationPhases([]);
                return;
            }
            const qPhases = await qPhasesRes.json();
            setQualificationPhases(Array.isArray(qPhases) ? qPhases : []);
        } catch (e) {
            pushError(`Failed to load qualification phases: ${String(e)}`);
            setQualificationPhases([]);
        }
    };

    const ensurePhaseExists = async ({ name, description, sequence }, phaseCache = null) => {
        const phaseName = String(name ?? "").trim();
        if (!phaseName) throw new Error("Phase name is required.");

        const key = normalizePhaseName(phaseName);
        const createPhase = async (createName, createDescription, createSequence) => {
            const createRes = await fetch(`${API_BASE}/CurriculumPhase`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    name: createName,
                    description: String(createDescription ?? "").trim(),
                    sequence: parseSequenceValue(createSequence, allPhases.length + 1)
                })
            });
            if (!createRes.ok) {
                const txt = await createRes.text().catch(() => "");
                throw new Error(`Create phase failed (${createRes.status}): ${txt || "No details"}`);
            }
            return createRes.json();
        };

        let phase = phaseCache?.get(key) || allPhases.find(p => normalizePhaseName(p.name) === key) || null;

        if (!phase || Number(phase?.id || 0) <= 0) {
            const created = await createPhase(phaseName, description, sequence);
            phase = created;
            if (phaseCache) phaseCache.set(key, created);
            setAllPhases(prev => {
                if (prev.some(p => Number(p.id) === Number(created.id))) return prev;
                return [...prev, created];
            });
            return { phase, created: true, updated: false };
        }

        const nextDescription = String(description ?? phase.description ?? "").trim();
        const nextSequence = parseSequenceValue(sequence, phase.sequence || 1);
        const currentDescription = String(phase.description ?? "");
        const currentSequence = Number(phase.sequence ?? 0);
        const needsUpdate = currentDescription !== nextDescription || currentSequence !== nextSequence;

        if (needsUpdate) {
            const updateRes = await fetch(`${API_BASE}/CurriculumPhase/${phase.id}`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    id: phase.id,
                    name: phase.name,
                    description: nextDescription,
                    sequence: nextSequence
                })
            });
            if (!updateRes.ok) {
                if (updateRes.status === 404) {
                    const recreated = await createPhase(phaseName, nextDescription, nextSequence);
                    phase = recreated;
                    if (phaseCache) phaseCache.set(key, recreated);
                    setAllPhases(prev => {
                        const withoutSameName = prev.filter(p => normalizePhaseName(p?.name) !== key);
                        if (withoutSameName.some(p => Number(p.id) === Number(recreated.id))) return withoutSameName;
                        return [...withoutSameName, recreated];
                    });
                    return { phase, created: true, updated: false };
                }
                const txt = await updateRes.text().catch(() => "");
                throw new Error(`Update phase failed (${updateRes.status}): ${txt || "No details"}`);
            }

            const updatedFromApi = await updateRes.json().catch(() => null);
            phase = updatedFromApi && typeof updatedFromApi === "object"
                ? { ...phase, ...updatedFromApi }
                : { ...phase, description: nextDescription, sequence: nextSequence };

            if (phaseCache) phaseCache.set(key, phase);
            setAllPhases(prev => prev.map(p => (Number(p.id) === Number(phase.id) ? { ...p, ...phase } : p)));
        }

        return { phase, created: false, updated: needsUpdate };
    };

    const ensureQualificationPhaseLink = async (qualificationNumericId, phaseId, linkedPhaseIds = null) => {
        const pid = Number(phaseId || 0);
        if (!pid) throw new Error("Phase id is missing for qualification link.");

        const alreadyLinked = linkedPhaseIds
            ? linkedPhaseIds.has(pid)
            : qualificationPhases.some(qp => Number(qp.curriculumPhaseId) === pid);

        if (alreadyLinked) return { linked: false };

        const res = await fetch(`${API_BASE}/QualificationPhase`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                qualificationId: Number(qualificationNumericId),
                curriculumPhaseId: pid
            })
        });

        if (!res.ok) {
            const txt = await res.text().catch(() => "");
            throw new Error(`Link save failed (${res.status}): ${txt || "No details"}`);
        }

        const createdLink = await res.json().catch(() => ({
            qualificationId: Number(qualificationNumericId),
            curriculumPhaseId: pid
        }));

        if (linkedPhaseIds) linkedPhaseIds.add(pid);
        setQualificationPhases(prev => {
            if (prev.some(qp => Number(qp.curriculumPhaseId) === pid)) return prev;
            return [...prev, createdLink];
        });

        return { linked: true };
    };

    useEffect(() => {
        if (!qualificationId) {
            pushError("No qualificationId available in Phases page.");
            return;
        }

        const loadData = async () => {
            await refreshPhaseLists(null);

            const numericId = await resolveQualificationNumericId(qualificationId);
            setResolvedNumericId(numericId);
            if (!numericId) {
                setQualificationPhases([]);
                setQualificationInfo(null);
                return;
            }
            if (Number(contextId || 0) !== Number(numericId)) {
                setQualificationId(numericId);
            }

            try {
                const qRes = await fetch(`${API_BASE}/Qualification/${numericId}`, { cache: "no-store" });
                if (qRes.ok) {
                    const qData = await qRes.json();
                    setQualificationInfo(qData);
                } else {
                    setQualificationInfo(null);
                }
            } catch {
                setQualificationInfo(null);
            }

            await refreshPhaseLists(numericId);
        };

        loadData();
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [qualificationId]);

    const handlePhaseSelectChange = (name) => {
        setSelectedPhaseName(name);
        setEditingPhaseId(null);
        setEditingPhaseLinkId(null);
        const selected = phaseOptions.find(p => p.name === name);
        if (!selected) return;
        setManualPhaseName(selected.name || "");
        setManualPhaseDescription(selected.description || "");
        setManualPhaseSequence(selected.sequence ? String(selected.sequence) : "");
    };

    const beginEditPhase = (phase, qualificationPhaseLinkId = null) => {
        if (!phase || !Number(phase?.id || 0)) return;
        setSelectedPhaseName(String(phase?.name || ""));
        setManualPhaseName(String(phase?.name || ""));
        setManualPhaseDescription(String(phase?.description || ""));
        setManualPhaseSequence(phase?.sequence ? String(phase.sequence) : "");
        setEditingPhaseId(Number(phase.id));
        setEditingPhaseLinkId(
            Number(qualificationPhaseLinkId || 0) > 0 ? Number(qualificationPhaseLinkId) : null
        );
        setSaveMessage(`Editing phase "${String(phase?.name || "").trim()}". Update fields, then click Save.`);
    };

    const handleEditLinkedPhase = (qp) => {
        const phase = allPhases.find(p => Number(p?.id || 0) === Number(qp?.curriculumPhaseId || 0)) || null;
        if (!phase) {
            pushError("Could not find the selected phase to edit.");
            return;
        }
        beginEditPhase(phase, Number(qp?.id || 0));
    };

    const handleCancelEdit = () => {
        setEditingPhaseId(null);
        setEditingPhaseLinkId(null);
        setSelectedPhaseName("");
        setManualPhaseName("");
        setManualPhaseDescription("");
        setManualPhaseSequence("");
        setSaveMessage("");
    };

    useEffect(() => {
        const routedPhaseId = Number(location.state?.curriculumPhaseId || location.state?.phaseId || 0);
        if (!routedPhaseId || !Array.isArray(allPhases) || allPhases.length === 0) return;
        if (editingPhaseId && Number(editingPhaseId) === routedPhaseId) return;

        const phase = allPhases.find(p => Number(p?.id || 0) === routedPhaseId);
        if (!phase) return;
        beginEditPhase(phase, Number(location.state?.qualificationPhaseId || 0));
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [allPhases, location.state?.curriculumPhaseId, location.state?.phaseId, location.state?.qualificationPhaseId]);

    const handleSave = async () => {
        setSaveMessage("");
        setImportSummary(null);
        setImportPreviewRows([]);

        if (!qualificationId) {
            pushError("Missing qualificationId; cannot save phase link.");
            return;
        }

        const selected = phaseOptions.find(p => p.name === selectedPhaseName) || null;
        const phaseName = String(manualPhaseName || selected?.name || "").trim();
        const phaseDescription = String(manualPhaseDescription || selected?.description || "").trim();
        const phaseSequence = parseSequenceValue(manualPhaseSequence, selected?.sequence || phaseOptions.length + 1);

        if (!phaseName) {
            pushError("Provide a phase name or choose a curriculum phase before saving.");
            return;
        }

        const numericId = await resolveQualificationNumericId(qualificationId);
        if (!numericId) {
            pushError("Cannot resolve qualification numeric Id for save.");
            return;
        }

        try {
            let phaseResult;
            if (Number(editingPhaseId || 0) > 0) {
                const updateRes = await fetch(`${API_BASE}/CurriculumPhase/${Number(editingPhaseId)}`, {
                    method: "PUT",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({
                        id: Number(editingPhaseId),
                        name: phaseName,
                        description: phaseDescription,
                        sequence: phaseSequence
                    })
                });
                if (!updateRes.ok) {
                    const txt = await updateRes.text().catch(() => "");
                    throw new Error(`Update phase failed (${updateRes.status}): ${txt || "No details"}`);
                }
                const updatedPhase = await updateRes.json().catch(() => ({
                    id: Number(editingPhaseId),
                    name: phaseName,
                    description: phaseDescription,
                    sequence: phaseSequence
                }));
                setAllPhases(prev => prev.map(p => (Number(p.id) === Number(updatedPhase.id) ? { ...p, ...updatedPhase } : p)));
                phaseResult = { phase: updatedPhase, created: false, updated: true };
            } else {
                phaseResult = await ensurePhaseExists({
                    name: phaseName,
                    description: phaseDescription,
                    sequence: phaseSequence
                });
            }
            const linkResult = await ensureQualificationPhaseLink(numericId, phaseResult.phase.id);

            const saveParts = [];
            if (phaseResult.created) saveParts.push("Phase created");
            else if (phaseResult.updated) saveParts.push("Phase updated");
            else saveParts.push("Phase matched existing record");

            if (linkResult.linked) saveParts.push("linked to qualification");
            else saveParts.push("already linked to qualification");

            setResolvedNumericId(numericId);
            setSaveMessage(`${saveParts.join(", ")}. Use Goto Subjects to continue.`);
            setManualPhaseName("");
            setManualPhaseDescription("");
            setManualPhaseSequence("");
            setSelectedPhaseName("");
            setEditingPhaseId(null);
            setEditingPhaseLinkId(null);
        } catch (e) {
            pushError(`Save exception: ${String(e?.message || e)}`);
        }
    };

    const handleDelete = async (id) => {
        try {
            const res = await fetch(`${API_BASE}/QualificationPhase/${id}`, { method: "DELETE" });
            if (!res.ok) {
                const txt = await res.text().catch(() => "");
                pushError(`Delete failed (${res.status}): ${txt || "No details"}`);
                return;
            }
            if (Number(editingPhaseLinkId || 0) === Number(id)) {
                handleCancelEdit();
            }
            setQualificationPhases(prev => prev.filter(p => p.id !== id));
        } catch (e) {
            pushError(`Delete exception: ${String(e)}`);
        }
    };

    const handleNext = async () => {
        const nextQualificationId = Number(
            resolvedNumericId || (await resolveQualificationNumericId(qualificationId)) || qualificationId || 0
        );
        if (nextQualificationId <= 0) {
            pushError("Cannot resolve qualification id to continue to Subjects.");
            return;
        }

        let latestLinks = Array.isArray(qualificationPhases) ? [...qualificationPhases] : [];
        if (!latestLinks.length) {
            try {
                const linksRes = await fetch(`${API_BASE}/QualificationPhase/${nextQualificationId}`, { cache: "no-store" });
                if (linksRes.ok) {
                    latestLinks = await linksRes.json();
                    setQualificationPhases(Array.isArray(latestLinks) ? latestLinks : []);
                }
            } catch {
                // Best effort fetch before auto-link fallback.
            }
        }

        if (!latestLinks.length && templateFile && !importing) {
            try {
                const importResult = await handleImportTemplate({ silent: true });
                if (importResult?.ok) {
                    const linksRes = await fetch(`${API_BASE}/QualificationPhase/${nextQualificationId}`, { cache: "no-store" });
                    latestLinks = linksRes.ok ? await linksRes.json() : [];
                    setQualificationPhases(Array.isArray(latestLinks) ? latestLinks : []);

                    if (Array.isArray(latestLinks) && latestLinks.length > 0) {
                        setSaveMessage(
                            `Auto-imported "${templateFile.name}" and linked ${Number(importResult?.linked || 0)} phase(s). Continuing to Subjects.`
                        );
                    }
                }
            } catch (e) {
                pushError(`Failed to auto-import selected file before next step: ${String(e?.message || e)}`);
                return;
            }
        }

        if (!latestLinks.length) {
            const selected = phaseOptions.find(p => p.name === selectedPhaseName) || null;
            const phaseName = String(manualPhaseName || selected?.name || "").trim();
            const phaseDescription = String(manualPhaseDescription || selected?.description || "").trim();
            const phaseSequence = parseSequenceValue(manualPhaseSequence, selected?.sequence || phaseOptions.length + 1);

            if (phaseName) {
                try {
                    const phaseResult = await ensurePhaseExists({
                        name: phaseName,
                        description: phaseDescription,
                        sequence: phaseSequence
                    });
                    await ensureQualificationPhaseLink(nextQualificationId, phaseResult.phase.id);
                    setSaveMessage(`Phase "${phaseResult.phase.name}" was auto-linked. Continuing to Subjects.`);
                    await refreshPhaseLists(nextQualificationId);

                    const linksRes = await fetch(`${API_BASE}/QualificationPhase/${nextQualificationId}`, { cache: "no-store" });
                    latestLinks = linksRes.ok ? await linksRes.json() : [];
                    setQualificationPhases(Array.isArray(latestLinks) ? latestLinks : []);
                } catch (e) {
                    pushError(`Failed to auto-link selected phase before next step: ${String(e?.message || e)}`);
                    return;
                }
            }
        }

        if (!Array.isArray(latestLinks) || latestLinks.length === 0) {
            pushError("No phase is linked to this qualification yet. Click \"Import Selected File\" (or Save a manual phase), then try again.");
            return;
        }

        const sortedLinks = [...latestLinks].sort((a, b) => Number(a?.id || 0) - Number(b?.id || 0));
        const last = sortedLinks[sortedLinks.length - 1];

        setQualificationId(nextQualificationId);
        navigate("/subjects", {
            state: {
                qualificationId: nextQualificationId,
                curriculumPhaseId: Number(last?.curriculumPhaseId || 0) || undefined
            }
        });
    };

    const handleDownloadTemplate = () => {
        window.open(API_TEMPLATE_PHASES, "_blank");
    };

    const handleImportServerTemplate = async () => {
        setSaveMessage("");
        setImportSummary(null);
        setImportPreviewRows([]);

        if (!qualificationId) {
            pushError("Missing qualificationId; cannot import phases.");
            return;
        }

        const numericId = await resolveQualificationNumericId(qualificationId);
        if (!numericId) {
            pushError("Cannot resolve qualification numeric Id for server import.");
            return;
        }

        setImporting(true);
        try {
            const res = await fetch(`${API_BASE}/CurriculumPhase/import-csv?qualificationId=${numericId}`, { method: "POST" });
            const data = await res.json().catch(() => ({}));
            if (!res.ok) {
                const msg = data?.error || data?.message || `Import failed (${res.status})`;
                pushError(msg);
                return;
            }

            await refreshPhaseLists(numericId);
            setResolvedNumericId(numericId);
            const created = Number(data?.created || 0);
            const linked = Number(data?.linked || 0);
            const updated = Number(data?.updated || 0);
            const failed = Number(data?.failed || 0);
            const preview = Array.isArray(data?.details)
                ? data.details.slice(0, 50).map((d) => ({
                    row: d?.row ?? "",
                    phaseName: d?.phaseName ?? "",
                    description: d?.phaseDescription ?? "",
                    sequence: d?.sequence ?? "",
                    status: d?.status ?? (d?.reason ? "failed" : ""),
                    reason: d?.reason ?? ""
                }))
                : [];

            setImportSummary({
                source: "server",
                created,
                updated,
                linked,
                failed,
                details: Array.isArray(data?.details) ? data.details.filter(d => d?.reason).slice(0, 20) : []
            });
            setImportPreviewRows(preview);
            setSaveMessage(
                `Imported built-in template: created ${created}, linked ${linked}` +
                `${updated ? `, updated ${updated}` : ""}` +
                `${failed ? `, failed ${failed}` : ""}.`
            );
        } catch (e) {
            pushError(`Import exception: ${String(e)}`);
        } finally {
            setImporting(false);
        }
    };

    const handleImportTemplate = async (options = {}) => {
        const silent = Boolean(options?.silent);
        clearTemplateMismatchErrors();
        if (!silent) {
            setSaveMessage("");
            setImportSummary(null);
            setImportPreviewRows([]);
        }

        if (!templateFile) {
            if (!silent) pushError("Select a template file first (.xlsx, .xls, or .csv).");
            return { ok: false, reason: "no-template-file" };
        }
        if (!qualificationId) {
            pushError("Missing qualificationId; cannot import phases.");
            return { ok: false, reason: "missing-qualification-id" };
        }

        const numericId = await resolveQualificationNumericId(qualificationId);
        if (!numericId) {
            pushError("Cannot resolve qualification numeric Id for import.");
            return { ok: false, reason: "cannot-resolve-qualification-id" };
        }

        setImporting(true);
        try {
            const { rows, firstSheet } = await readTemplateWorkbook(templateFile);
            const diagnostics = buildPhaseTemplateDiagnostics(rows, {
                fileName: templateFile.name,
                sheetName: firstSheet
            });
            setTemplateDiagnostics(diagnostics);

            if (diagnostics.dataRowCount <= 0) {
                pushError("Template file has no data rows.");
                return { ok: false, reason: "no-data-rows" };
            }
            if (diagnostics.missingRequired.length > 0) {
                pushError(`Missing columns: ${diagnostics.missingRequired.join(", ")}.`);
                return { ok: false, reason: "missing-required-columns" };
            }

            const selectedQualificationCode = String(qualificationInfo?.qualificationNumber || "").trim();
            const templateQualificationCodes = extractTemplateQualificationCodes(diagnostics);
            let mismatchWarning = "";
            if (
                selectedQualificationCode &&
                templateQualificationCodes.length > 0 &&
                !templateQualificationCodes.includes(selectedQualificationCode)
            ) {
                mismatchWarning = `${TEMPLATE_MISMATCH_PREFIX} selected qualification is "${selectedQualificationCode}", file contains ${templateQualificationCodes.join(", ")}. Imported rows were linked to "${selectedQualificationCode}".`;
            }

            let created = 0;
            let updated = 0;
            let linked = 0;
            let failed = 0;
            const details = [];
            const previewRows = [];

            const phaseCache = new Map();
            (allPhases || []).forEach(p => {
                const key = normalizePhaseName(p?.name);
                if (key) phaseCache.set(key, p);
            });
            const linkedPhaseIds = new Set((qualificationPhases || []).map(qp => Number(qp.curriculumPhaseId)));

            for (const rowData of diagnostics.extractedRows) {
                const rowNumber = Number(rowData?.row || 0);
                const phaseName = String(rowData?.phaseName || "").trim();
                const phaseDescription = String(rowData?.phaseDescription || "").trim();
                const phaseSequence = parseSequenceValue(rowData?.phaseSequence, rowNumber || 1);

                if (!rowData?.isValid || !phaseName) {
                    failed++;
                    const reason = String(rowData?.reason || "Learning Phases/Phases Code missing");
                    details.push({ row: rowNumber, reason });
                    previewRows.push({
                        row: rowNumber,
                        phaseName: "",
                        description: phaseDescription,
                        sequence: phaseSequence,
                        status: "failed",
                        reason
                    });
                    continue;
                }

                try {
                    const phaseResult = await ensurePhaseExists(
                        { name: phaseName, description: phaseDescription, sequence: phaseSequence },
                        phaseCache
                    );
                    if (phaseResult.created) created++;
                    if (phaseResult.updated) updated++;

                    const linkResult = await ensureQualificationPhaseLink(
                        numericId,
                        phaseResult.phase.id,
                        linkedPhaseIds
                    );
                    if (linkResult.linked) linked++;

                    const statusParts = [];
                    if (phaseResult.created) statusParts.push("created");
                    else if (phaseResult.updated) statusParts.push("updated");
                    else statusParts.push("matched");
                    statusParts.push(linkResult.linked ? "linked" : "already-linked");

                    previewRows.push({
                        row: rowNumber,
                        phaseName: phaseResult.phase?.name || phaseName,
                        description: String(phaseResult.phase?.description ?? phaseDescription ?? ""),
                        sequence: Number(phaseResult.phase?.sequence ?? phaseSequence),
                        status: statusParts.join(", "),
                        reason: ""
                    });
                } catch (e) {
                    failed++;
                    const reason = String(e?.message || e);
                    details.push({ row: rowNumber, reason });
                    previewRows.push({
                        row: rowNumber,
                        phaseName,
                        description: phaseDescription,
                        sequence: phaseSequence,
                        status: "failed",
                        reason
                    });
                }
            }

            await refreshPhaseLists(numericId);
            setResolvedNumericId(numericId);
            setImportSummary({
                source: "file",
                created,
                updated,
                linked,
                failed,
                details: details.slice(0, 20)
            });
            setImportPreviewRows(previewRows.slice(0, 50));
            setSaveMessage(
                `Imported "${templateFile.name}": created ${created}, linked ${linked}` +
                `${updated ? `, updated ${updated}` : ""}` +
                `${failed ? `, failed ${failed}` : ""}.` +
                `${mismatchWarning ? ` ${mismatchWarning}` : ""}`
            );
            return { ok: true, created, updated, linked, failed };
        } catch (e) {
            pushError(`Import exception: ${String(e?.message || e)}`);
            return { ok: false, reason: "import-exception", error: String(e?.message || e) };
        } finally {
            setImporting(false);
        }
    };

    const handleBack = () => {
        navigate("/demographics", { state: { qualificationId } });
    };

    const handlePreview = () => {
        const nextQualificationId = Number(resolvedNumericId || qualificationId || 0);
        navigate("/phases-review", {
            state: {
                qualificationId: nextQualificationId > 0 ? nextQualificationId : qualificationId
            }
        });
    };

    const getPhaseById = (id) => allPhases.find(p => Number(p.id) === Number(id)) || null;
    const getPhaseName = (id) => getPhaseById(id)?.name || id;

    if (!qualificationId) {
        return (
            <div className="page-container">
                <h2>Curriculum Phases</h2>
                {errors.length > 0 && (
                    <div style={{ background: "#ffd2d2", color: "#a00", padding: "0.7rem", borderRadius: 6, marginBottom: 12 }}>
                        <strong>Errors</strong>
                        <ul style={{ margin: "0.5rem 0 0 1rem" }}>
                            {errors.map((e, i) => <li key={i}>{e}</li>)}
                        </ul>
                        <button style={{ marginTop: "0.5rem" }} onClick={() => setErrors([])}>Clear</button>
                    </div>
                )}
                <div className="error-banner">
                    No Qualification selected. Please go back and select or create one.
                </div>
                <button onClick={() => navigate("/qualifications")}>Back</button>
            </div>
        );
    }

    return (
        <div className="page-container">
            <h2><GlossaryLabel label="Curriculum Phases" term="Curriculum" /></h2>
            {errors.length > 0 && (
                <div style={{ background: "#ffd2d2", color: "#a00", padding: "0.7rem", borderRadius: 6, marginBottom: 12 }}>
                    <strong>Errors</strong>
                    <ul style={{ margin: "0.5rem 0 0 1rem" }}>
                        {errors.map((e, i) => <li key={i}>{e}</li>)}
                    </ul>
                    <button style={{ marginTop: "0.5rem" }} onClick={() => setErrors([])}>Clear</button>
                </div>
            )}

            <div style={{ background: "#eef6ff", border: "1px solid #cfe2ff", color: "#0b3d91", padding: "0.7rem", borderRadius: 6, marginBottom: 12 }}>
                <div><strong><GlossaryLabel label="Qualification Code" term="Qualification" /></strong>: {String(qualificationId || "-")}</div>
                <div><strong>Resolved Numeric Id</strong>: {resolvedNumericId ?? "-"}</div>
                {qualificationInfo && (
                    <>
                        <div><strong><GlossaryLabel label="Qualification Number" term="Qualification" /></strong>: {qualificationInfo.qualificationNumber || "-"}</div>
                        <div style={{ marginTop: 4 }}><strong><GlossaryLabel label="Curriculum Title/Description" term="Curriculum" /></strong>:</div>
                        <div style={{ whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
                            {qualificationInfo.qualificationDescription || "-"}
                        </div>
                        <div style={{ marginTop: 4 }}><strong>Institution</strong>: {qualificationInfo.learningInstitutionName || "-"}</div>
                    </>
                )}
            </div>

            <div className="form-section">
                <div style={{ marginBottom: 12, padding: "10px 12px", background: "#f7f9fc", border: "1px solid #dbe3f5", borderRadius: 8 }}>
                    <div style={{ fontWeight: 600, marginBottom: 6 }}>Phases Template</div>
                    <div style={{ display: "flex", gap: 8, flexWrap: "wrap", alignItems: "center" }}>
                        <button type="button" onClick={handleDownloadTemplate}>Download Template</button>
                        <input
                            type="file"
                            accept=".xlsx,.xls,.csv"
                            onChange={handleTemplateFileChange}
                            disabled={importing}
                        />
                        <button type="button" onClick={handleImportTemplate} disabled={importing || !qualificationId || !templateFile}>
                            {importing ? "Importing..." : "Import Selected File"}
                        </button>
                        <button type="button" onClick={handleImportServerTemplate} disabled={importing || !qualificationId}>
                            Import Built-In Template
                        </button>
                    </div>
                    {templateFile ? (
                        <div style={{ marginTop: 6, color: "#23395d" }}>
                            Selected file: <strong>{templateFile.name}</strong>
                        </div>
                    ) : null}
                    {templateFile && !importSummary ? (
                        <div style={{ marginTop: 6, color: "#7a5400", background: "#fff6df", border: "1px solid #eac875", borderRadius: 6, padding: "6px 8px" }}>
                            Template headers/data are loaded for diagnostics only. Click <strong>Import Selected File</strong> to persist and link phases.
                        </div>
                    ) : null}
                    {importSummary ? (
                        <div style={{ marginTop: 8 }}>
                            <div style={{ color: "#185a3a", fontWeight: 600 }}>
                                Import Summary ({importSummary.source}): created {importSummary.created}, linked {importSummary.linked}
                                {importSummary.updated ? `, updated ${importSummary.updated}` : ""}
                                {importSummary.failed ? `, failed ${importSummary.failed}` : ""}
                            </div>
                            {Array.isArray(importSummary.details) && importSummary.details.length > 0 ? (
                                <div style={{ marginTop: 6, color: "#8a1f11" }}>
                                    <strong>First import errors:</strong>
                                    <ul style={{ margin: "4px 0 0 18px" }}>
                                        {importSummary.details.map((d, i) => (
                                            <li key={i}>Row {d.row}: {d.reason}</li>
                                        ))}
                                    </ul>
                                </div>
                            ) : null}
                        </div>
                    ) : null}
                    {importPreviewRows.length > 0 ? (
                        <div style={{ marginTop: 10 }}>
                            <div style={{ fontWeight: 600, marginBottom: 6 }}>
                                Imported Data Preview (first {importPreviewRows.length} rows)
                            </div>
                            <div style={{ overflowX: "auto" }}>
                                <table className="table" style={{ minWidth: 680 }}>
                                    <thead>
                                        <tr>
                                            <th>Row</th>
                                            <th>Phase Name</th>
                                            <th>Description</th>
                                            <th>Sequence</th>
                                            <th>Status</th>
                                            <th>Reason</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {importPreviewRows.map((item, idx) => (
                                            <tr key={`${item.row}-${idx}`}>
                                                <td>{item.row}</td>
                                                <td>{item.phaseName || "-"}</td>
                                                <td>{item.description || "-"}</td>
                                                <td>{item.sequence || "-"}</td>
                                                <td>{item.status || "-"}</td>
                                                <td>{item.reason || "-"}</td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    ) : null}
                </div>

                <div className="mainpage-form-fields-vertical">
                    <label><GlossaryLabel label="Curriculum Phase" term="Curriculum" />
                        <select
                            value={selectedPhaseName}
                            onChange={(e) => handlePhaseSelectChange(e.target.value)}
                        >
                            <option value="">Select an existing phase...</option>
                            {phaseOptions.map((phase) => (
                                <option key={`${phase.id ?? phase.name}-${phase.name}`} value={phase.name}>
                                    {phase.name}
                                </option>
                            ))}
                        </select>
                    </label>

                    <label>Phase Name (manual capture)
                        <input
                            className="mainpage-input"
                            value={manualPhaseName}
                            onChange={(e) => setManualPhaseName(e.target.value)}
                            placeholder="Enter a phase name"
                        />
                    </label>

                    <label>Phase Description (manual capture)
                        <input
                            className="mainpage-input"
                            value={manualPhaseDescription}
                            onChange={(e) => setManualPhaseDescription(e.target.value)}
                            placeholder="Enter a phase description"
                        />
                    </label>

                    <label>Phase Sequence (manual capture)
                        <input
                            className="mainpage-input"
                            value={manualPhaseSequence}
                            onChange={(e) => setManualPhaseSequence(e.target.value)}
                            type="number"
                            min="1"
                            placeholder="1"
                        />
                    </label>
                </div>

                <div className="button-row">
                    <button onClick={handleSave}>Save</button>
                    <button onClick={handleBack}>Back</button>
                    <button onClick={handlePreview}>Preview Uploaded Phases</button>
                    <button className="next-step-button" onClick={handleNext} disabled={importing}>Goto Subjects</button>
                    {editingPhaseId ? (
                        <button onClick={handleCancelEdit}>Cancel Edit</button>
                    ) : null}
                </div>
                {saveMessage ? (
                    <div style={{ marginTop: 10, color: "#185a3a", fontWeight: 600 }}>{saveMessage}</div>
                ) : null}
            </div>

            <h3>Phases for this Qualification</h3>
            <table className="table">
                <thead>
                    <tr>
                        <th>Phase</th>
                        <th>Description</th>
                        <th>Sequence</th>
                        <th>Actions</th>
                    </tr>
                </thead>
                <tbody>
                    {qualificationPhases.map((qp) => {
                        const phase = getPhaseById(qp.curriculumPhaseId);
                        return (
                            <tr key={qp.id}>
                                <td>{getPhaseName(qp.curriculumPhaseId)}</td>
                                <td>{phase?.description || ""}</td>
                                <td>{phase?.sequence ?? ""}</td>
                                <td>
                                    <button onClick={() => handleEditLinkedPhase(qp)}>Edit Parameters</button>
                                    <button onClick={() => handleDelete(qp.id)}>Delete</button>
                                </td>
                            </tr>
                        );
                    })}
                    {!qualificationPhases.length && (
                        <tr>
                            <td colSpan={4}>No phases linked yet.</td>
                        </tr>
                    )}
                </tbody>
            </table>

            {(templateDiagnostics || importSummary || importPreviewRows.length > 0) ? (
                <div style={{ marginTop: 14, padding: "12px", background: "#f8fbff", border: "1px solid #d6e3f3", borderRadius: 8 }}>
                    <h3 style={{ margin: "0 0 8px 0" }}>Template Header and Import Diagnostics</h3>

                    {templateDiagnostics ? (
                        <>
                            <div style={{ color: "#224466", marginBottom: 8 }}>
                                <strong>File:</strong> {templateDiagnostics.fileName || "-"}{" "}
                                <strong style={{ marginLeft: 12 }}>Sheet:</strong> {templateDiagnostics.sheetName || "-"}{" "}
                                <strong style={{ marginLeft: 12 }}>Header Columns:</strong> {templateDiagnostics.header.length}{" "}
                                <strong style={{ marginLeft: 12 }}>Data Rows:</strong> {templateDiagnostics.dataRowCount}{" "}
                                <strong style={{ marginLeft: 12 }}>Valid:</strong> {templateDiagnostics.validRowCount}{" "}
                                <strong style={{ marginLeft: 12 }}>Invalid:</strong> {templateDiagnostics.invalidRowCount}
                            </div>

                            {templateDiagnostics.missingRequired.length > 0 ? (
                                <div style={{ background: "#fff2d8", border: "1px solid #eac875", color: "#7a5400", borderRadius: 6, padding: "8px 10px", marginBottom: 10 }}>
                                    Required column mapping issue: {templateDiagnostics.missingRequired.join(", ")}.
                                </div>
                            ) : (
                                <div style={{ background: "#ecf9ef", border: "1px solid #a9d7b1", color: "#1b5e2b", borderRadius: 6, padding: "8px 10px", marginBottom: 10 }}>
                                    Required template headers were detected.
                                </div>
                            )}

                            <div style={{ overflowX: "auto", marginBottom: 10 }}>
                                <table className="table" style={{ minWidth: 740 }}>
                                    <thead>
                                        <tr>
                                            <th>Expected Column</th>
                                            <th>Accepted Header Names</th>
                                            <th>Matched Header</th>
                                            <th>Index</th>
                                            <th>Status</th>
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {templateDiagnostics.headerMappings.map((mapping) => (
                                            <tr key={mapping.key}>
                                                <td>{mapping.label}</td>
                                                <td>{mapping.aliases.join(" | ")}</td>
                                                <td>{mapping.matchedHeader || "-"}</td>
                                                <td>{mapping.index >= 0 ? mapping.index : "-"}</td>
                                                <td>{mapping.index >= 0 ? "mapped" : "not-found"}</td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>

                            {templateDiagnostics.extractedRows.length > 0 ? (
                                <div style={{ overflowX: "auto" }}>
                                    <div style={{ fontWeight: 600, marginBottom: 6 }}>
                                        Parsed Template Rows (first {Math.min(25, templateDiagnostics.extractedRows.length)})
                                    </div>
                                    <table className="table" style={{ minWidth: 920 }}>
                                        <thead>
                                            <tr>
                                                <th>Row</th>
                                                <th>Qualification Code</th>
                                                <th>Learning Phases</th>
                                                <th>Phases Code</th>
                                                <th>Parsed Phase Name</th>
                                                <th>Description</th>
                                                <th>Sequence</th>
                                                <th>Validation</th>
                                            </tr>
                                        </thead>
                                        <tbody>
                                            {templateDiagnostics.extractedRows.slice(0, 25).map((row, idx) => (
                                                <tr key={`${row.row}-${idx}`}>
                                                    <td>{row.row}</td>
                                                    <td>{row.qualificationCode || "-"}</td>
                                                    <td>{row.learningPhase || "-"}</td>
                                                    <td>{row.phaseCode || "-"}</td>
                                                    <td>{row.phaseName || "-"}</td>
                                                    <td>{row.phaseDescription || "-"}</td>
                                                    <td>{row.phaseSequence || "-"}</td>
                                                    <td>{row.isValid ? "ok" : (row.reason || "invalid")}</td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            ) : null}
                        </>
                    ) : (
                        <div style={{ color: "#355" }}>
                            No file-level header diagnostics available yet. Select a template file to preview header mapping.
                        </div>
                    )}

                    {importSummary ? (
                        <div style={{ marginTop: 10, color: "#185a3a", fontWeight: 600 }}>
                            Import Summary ({importSummary.source}): created {importSummary.created}, linked {importSummary.linked}
                            {importSummary.updated ? `, updated ${importSummary.updated}` : ""}
                            {importSummary.failed ? `, failed ${importSummary.failed}` : ""}
                        </div>
                    ) : null}

                    {importPreviewRows.length > 0 ? (
                        <div style={{ marginTop: 10, overflowX: "auto" }}>
                            <div style={{ fontWeight: 600, marginBottom: 6 }}>
                                Import Execution Preview (first {importPreviewRows.length} rows)
                            </div>
                            <table className="table" style={{ minWidth: 700 }}>
                                <thead>
                                    <tr>
                                        <th>Row</th>
                                        <th>Phase Name</th>
                                        <th>Description</th>
                                        <th>Sequence</th>
                                        <th>Status</th>
                                        <th>Reason</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {importPreviewRows.map((item, idx) => (
                                        <tr key={`${item.row}-exec-${idx}`}>
                                            <td>{item.row}</td>
                                            <td>{item.phaseName || "-"}</td>
                                            <td>{item.description || "-"}</td>
                                            <td>{item.sequence || "-"}</td>
                                            <td>{item.status || "-"}</td>
                                            <td>{item.reason || "-"}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    ) : null}
                </div>
            ) : null}
        </div>
    );
}
