import React, { useEffect, useMemo, useState } from "react";
import {
  downloadEditedDocxFromText,
  downloadEditedRtfFromText,
  htmlToEditableText
} from "../utils/docxEditExport";

export default function DocxPreviewModal({
  open,
  title,
  loading,
  error,
  html,
  warnings,
  editableDocx = false,
  editedDocxFileName = "EditedDocument.docx",
  zoom = 1,
  onZoomIn,
  onZoomOut,
  onZoomReset,
  onClose
}) {
  const [editMode, setEditMode] = useState(false);
  const [editedText, setEditedText] = useState("");
  const [editorStatus, setEditorStatus] = useState("");
  const [editorError, setEditorError] = useState("");
  const [downloadingEdited, setDownloadingEdited] = useState(false);
  const [legacyMode, setLegacyMode] = useState(true);
  const [fullScreen, setFullScreen] = useState(false);

  const plainTextFromHtml = useMemo(() => htmlToEditableText(html), [html]);
  const draftStorageKey = useMemo(
    () => `etdp_doc_draft:${String(editedDocxFileName || "doc").trim()}:${String(title || "").trim()}`,
    [editedDocxFileName, title]
  );
  const rtfFileName = useMemo(() => {
    const base = String(editedDocxFileName || "EditedDocument.docx").trim();
    if (!base) return "EditedDocument.rtf";
    return base.toLowerCase().endsWith(".docx")
      ? `${base.slice(0, -5)}.rtf`
      : `${base}.rtf`;
  }, [editedDocxFileName]);
  const hasLocalStorage = typeof window !== "undefined" && typeof window.localStorage !== "undefined";

  useEffect(() => {
    if (!open) return undefined;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    const handleKeyDown = (event) => {
      if (event.key === "Escape") onClose?.();
    };
    window.addEventListener("keydown", handleKeyDown);

    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [open, onClose]);

  useEffect(() => {
    if (!open) return;
    setEditMode(false);
    setEditedText(plainTextFromHtml);
    setEditorStatus("");
    setEditorError("");
    setDownloadingEdited(false);
    setLegacyMode(true);
    setFullScreen(false);
  }, [open, plainTextFromHtml]);

  if (!open) return null;

  const safeZoom = Math.max(0.5, Math.min(2.5, Number(zoom || 1)));
  const zoomPct = Math.round(safeZoom * 100);

  const downloadEdited = async () => {
    setEditorStatus("");
    setEditorError("");
    setDownloadingEdited(true);
    try {
      await downloadEditedDocxFromText({
        text: editedText,
        fileName: editedDocxFileName,
        title: title || "Edited Document",
        legacyMode
      });
      setEditorStatus(legacyMode ? "Edited DOCX (legacy mode) downloaded." : "Edited DOCX downloaded.");
    } catch (e) {
      setEditorError(`Failed to download edited DOCX: ${String(e?.message || e)}`);
    } finally {
      setDownloadingEdited(false);
    }
  };

  const downloadEditedRtf = async () => {
    setEditorStatus("");
    setEditorError("");
    setDownloadingEdited(true);
    try {
      await downloadEditedRtfFromText({
        text: editedText,
        fileName: rtfFileName,
        title: title || "Edited Document"
      });
      setEditorStatus("Edited RTF downloaded for legacy Word compatibility.");
    } catch (e) {
      setEditorError(`Failed to download edited RTF: ${String(e?.message || e)}`);
    } finally {
      setDownloadingEdited(false);
    }
  };

  const saveDraft = () => {
    setEditorStatus("");
    setEditorError("");
    if (!hasLocalStorage) {
      setEditorError("Local draft storage is not available in this browser.");
      return;
    }
    try {
      const payload = {
        text: editedText,
        legacyMode,
        savedAtUtc: new Date().toISOString()
      };
      window.localStorage.setItem(draftStorageKey, JSON.stringify(payload));
      setEditorStatus("Draft saved locally on this device.");
    } catch (e) {
      setEditorError(`Failed to save draft: ${String(e?.message || e)}`);
    }
  };

  const restoreDraft = () => {
    setEditorStatus("");
    setEditorError("");
    if (!hasLocalStorage) {
      setEditorError("Local draft storage is not available in this browser.");
      return;
    }
    try {
      const raw = window.localStorage.getItem(draftStorageKey);
      if (!raw) {
        setEditorError("No saved draft found for this document.");
        return;
      }
      const parsed = JSON.parse(raw);
      setEditedText(String(parsed?.text || ""));
      setLegacyMode(Boolean(parsed?.legacyMode));
      setEditorStatus("Draft restored.");
    } catch (e) {
      setEditorError(`Failed to restore draft: ${String(e?.message || e)}`);
    }
  };

  const clearDraft = () => {
    setEditorStatus("");
    setEditorError("");
    if (!hasLocalStorage) {
      setEditorError("Local draft storage is not available in this browser.");
      return;
    }
    try {
      window.localStorage.removeItem(draftStorageKey);
      setEditorStatus("Draft cleared.");
    } catch (e) {
      setEditorError(`Failed to clear draft: ${String(e?.message || e)}`);
    }
  };

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        zIndex: 2000,
        background: "rgba(10, 20, 35, 0.45)",
        display: "flex",
        alignItems: fullScreen ? "stretch" : "center",
        justifyContent: fullScreen ? "stretch" : "center",
        padding: fullScreen ? 0 : 16
      }}
      onClick={() => onClose?.()}
    >
      <div
        style={{
          width: fullScreen ? "100vw" : "min(1240px, 98vw)",
          height: fullScreen ? "100vh" : "min(92vh, 920px)",
          background: "#f5f7fb",
          border: fullScreen ? "none" : "1px solid #cbd6ea",
          borderRadius: fullScreen ? 0 : 10,
          display: "grid",
          gridTemplateRows: "auto auto 1fr",
          boxShadow: fullScreen ? "none" : "0 20px 60px rgba(0,0,0,0.22)"
        }}
        onClick={(event) => event.stopPropagation()}
      >
        <div
          style={{
            display: "flex",
            gap: 10,
            alignItems: "center",
            justifyContent: "space-between",
            padding: "10px 12px",
            borderBottom: "1px solid #d8dfeb",
            background: "#eaf1ff",
            borderTopLeftRadius: fullScreen ? 0 : 10,
            borderTopRightRadius: fullScreen ? 0 : 10
          }}
        >
          <div style={{ fontWeight: 700, color: "#173a6a" }}>{title || "Document Preview"}</div>
          <button type="button" onClick={() => onClose?.()}>Close</button>
        </div>

        <div
          style={{
            display: "flex",
            gap: 8,
            alignItems: "center",
            padding: "8px 12px",
            borderBottom: "1px solid #d8dfeb",
            background: "#f8fbff"
          }}
        >
          <button type="button" onClick={onZoomOut} disabled={loading}>Zoom Out</button>
          <button type="button" onClick={onZoomIn} disabled={loading}>Zoom In</button>
          <button type="button" onClick={onZoomReset} disabled={loading}>Reset</button>
          <button type="button" onClick={() => setFullScreen((value) => !value)} disabled={loading}>
            {fullScreen ? "Windowed" : "Full Screen"}
          </button>
          {editableDocx ? (
            <button
              type="button"
              onClick={() => setEditMode((v) => !v)}
              disabled={loading}
            >
              {editMode ? "View Mode" : "Edit Text"}
            </button>
          ) : null}
          {editableDocx && editMode ? (
            <>
              <label style={{ display: "inline-flex", alignItems: "center", gap: 4, marginLeft: 4 }}>
                <input
                  type="checkbox"
                  checked={legacyMode}
                  onChange={(event) => setLegacyMode(event.target.checked)}
                  disabled={loading || downloadingEdited}
                />
                Legacy Mode
              </label>
              <button
                type="button"
                onClick={() => setEditedText(plainTextFromHtml)}
                disabled={loading || downloadingEdited}
              >
                Reset Text
              </button>
              <button
                type="button"
                onClick={saveDraft}
                disabled={loading || downloadingEdited}
              >
                Save Draft
              </button>
              <button
                type="button"
                onClick={restoreDraft}
                disabled={loading || downloadingEdited}
              >
                Restore Draft
              </button>
              <button
                type="button"
                onClick={clearDraft}
                disabled={loading || downloadingEdited}
              >
                Clear Draft
              </button>
              <button
                type="button"
                onClick={downloadEdited}
                disabled={loading || downloadingEdited || !String(editedText || "").trim()}
              >
                {downloadingEdited ? "Preparing DOCX..." : (legacyMode ? "Download Edited .docx (Legacy)" : "Download Edited .docx")}
              </button>
              <button
                type="button"
                onClick={downloadEditedRtf}
                disabled={loading || downloadingEdited || !String(editedText || "").trim()}
              >
                {downloadingEdited ? "Preparing RTF..." : "Download .rtf (Older Word)"}
              </button>
            </>
          ) : null}
          <span style={{ color: "#304e76", fontWeight: 600 }}>{zoomPct}%</span>
          {Array.isArray(warnings) && warnings.length > 0 ? (
            <span style={{ color: "#7a5400", marginLeft: 8 }}>
              Converted with {warnings.length} warning(s).
            </span>
          ) : null}
        </div>

        <div style={{ overflow: "auto", padding: 14 }}>
          {editorStatus ? (
            <div style={{ color: "#0b7a0b", marginBottom: 8 }}>{editorStatus}</div>
          ) : null}
          {editorError ? (
            <div style={{ color: "#b00020", marginBottom: 8 }}>{editorError}</div>
          ) : null}
          {editableDocx && editMode ? (
            <div style={{ color: "#3d566e", marginBottom: 8, fontSize: 13 }}>
              Legacy Mode keeps formatting simpler for older Microsoft Word versions. Use RTF if DOCX still opens poorly.
            </div>
          ) : null}
          {loading ? (
            <div style={{ color: "#3d566e" }}>Generating preview...</div>
          ) : error ? (
            <div style={{ color: "#b00020" }}>{error}</div>
          ) : !String(html || "").trim() ? (
            <div style={{ color: "#3d566e" }}>No preview content was generated.</div>
          ) : editableDocx && editMode ? (
            <textarea
              value={editedText}
              onChange={(event) => setEditedText(event.target.value)}
              rows={30}
              style={{
                width: "100%",
                minHeight: "68vh",
                border: "1px solid #d8dfeb",
                borderRadius: 6,
                padding: 12,
                resize: "vertical",
                lineHeight: 1.45,
                fontFamily: "Segoe UI, Arial, sans-serif"
              }}
            />
          ) : (
            <div
              style={{
                width: `${100 / safeZoom}%`,
                transform: `scale(${safeZoom})`,
                transformOrigin: "top left",
                background: "#ffffff",
                border: "1px solid #d8dfeb",
                borderRadius: 6,
                padding: 24,
                color: "#111827",
                lineHeight: 1.55
              }}
              dangerouslySetInnerHTML={{ __html: html }}
            />
          )}
        </div>
      </div>
    </div>
  );
}
