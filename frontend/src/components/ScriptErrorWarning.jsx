import React, { useEffect, useMemo, useState } from "react";

const MAX_ERROR_ITEMS = 20;

const toText = (value) => {
    if (value === null || value === undefined) return "";
    if (typeof value === "string") return value.trim();
    if (typeof value === "number" || typeof value === "boolean") return String(value);
    if (value instanceof Error) return value.message || value.name || "Error";
    try {
        return JSON.stringify(value);
    } catch {
        return String(value);
    }
};

const normalizeEntry = (input = {}) => {
    const message = toText(input.message) || "Unhandled script error";
    const source = toText(input.source) || "runtime";
    const fileName = toText(input.fileName || input.filename || input.file);
    const lineNo = Number(input.lineNo || input.line || 0);
    const colNo = Number(input.colNo || input.col || 0);
    const stack = toText(input.stack);
    const at = new Date().toISOString();

    return {
        source,
        message,
        fileName,
        lineNo,
        colNo,
        stack,
        at,
        signature: `${source}|${message}|${fileName}|${lineNo}|${colNo}`
    };
};

const previewStack = (stack) => {
    if (!stack) return "";
    const lines = String(stack).split(/\r?\n/).map(line => line.trim()).filter(Boolean);
    return lines.slice(0, 2).join(" | ");
};

export default function ScriptErrorWarning() {
    const [entries, setEntries] = useState([]);
    const [expanded, setExpanded] = useState(false);

    const addEntry = (entryInput) => {
        const normalized = normalizeEntry(entryInput);
        setEntries(prev => {
            if (prev.length > 0 && prev[0].signature === normalized.signature) {
                const head = prev[0];
                const merged = {
                    ...head,
                    at: normalized.at,
                    count: Number(head.count || 1) + 1
                };
                return [merged, ...prev.slice(1)];
            }

            const next = [{ ...normalized, count: 1 }, ...prev];
            return next.slice(0, MAX_ERROR_ITEMS);
        });
        setExpanded(true);
    };

    useEffect(() => {
        const onRuntimeCustom = (event) => {
            if (!event?.detail) return;
            addEntry(event.detail);
        };

        window.addEventListener("etdp:runtime-error", onRuntimeCustom);

        return () => {
            window.removeEventListener("etdp:runtime-error", onRuntimeCustom);
        };
    }, []);

    const latest = entries[0] || null;
    const total = entries.reduce((sum, item) => sum + Number(item?.count || 1), 0);
    const visibleEntries = useMemo(() => entries.slice(0, 6), [entries]);

    if (!latest) return null;

    return (
        <div
            style={{
                position: "fixed",
                left: 12,
                bottom: 12,
                zIndex: 80,
                width: "min(460px, calc(100vw - 24px))",
                border: "1px solid #d58484",
                borderRadius: 10,
                background: "#fff6f6",
                boxShadow: "0 8px 24px rgba(120, 25, 25, 0.18)",
                color: "#5a1515"
            }}
        >
            <div style={{ padding: "8px 10px", borderBottom: "1px solid #e5b3b3", display: "flex", gap: 8, alignItems: "center" }}>
                <strong style={{ flex: 1 }}>Script Errors ({total})</strong>
                <button type="button" onClick={() => setExpanded(v => !v)}>
                    {expanded ? "Hide" : "Show"}
                </button>
                <button type="button" onClick={() => setEntries([])}>
                    Clear
                </button>
            </div>

            <div style={{ padding: "8px 10px", fontSize: "0.9rem" }}>
                <div style={{ fontWeight: 600, marginBottom: expanded ? 8 : 0 }}>
                    Latest: {latest.message}
                </div>
                {!expanded ? null : (
                    <div style={{ display: "grid", gap: 8, maxHeight: 260, overflowY: "auto" }}>
                        {visibleEntries.map((entry, idx) => (
                            <div key={`${entry.signature}-${idx}`} style={{ border: "1px solid #efcdcd", borderRadius: 8, padding: "6px 8px", background: "#fff" }}>
                                <div><strong>{entry.source}</strong> {entry.count > 1 ? `(x${entry.count})` : ""}</div>
                                <div>{entry.message}</div>
                                {entry.fileName ? (
                                    <div style={{ color: "#7b3c3c" }}>
                                        {entry.fileName}
                                        {entry.lineNo > 0 ? `:${entry.lineNo}` : ""}
                                        {entry.colNo > 0 ? `:${entry.colNo}` : ""}
                                    </div>
                                ) : null}
                                {entry.stack ? (
                                    <div style={{ color: "#7b3c3c" }}>{previewStack(entry.stack)}</div>
                                ) : null}
                                <div style={{ color: "#916767" }}>{new Date(entry.at).toLocaleTimeString()}</div>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}
