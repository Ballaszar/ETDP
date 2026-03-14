import React from "react";

export default function ErrorFallback({ error, resetErrorBoundary }) {
    return (
        <div style={{
            padding: "2rem",
            background: "#ffe6e6",
            border: "1px solid #ffb3b3",
            borderRadius: "8px",
            color: "#b00020",
            margin: "2rem auto",
            maxWidth: "800px"
        }}>
            <h2>⚠️ Something went wrong on this page</h2>

            <p><strong>Error:</strong> {String(error)}</p>

            <pre style={{ whiteSpace: "pre-wrap", marginTop: "1rem" }}>
                {error?.stack}
            </pre>


            <button
                onClick={resetErrorBoundary}
                style={{
                    marginTop: "1rem",
                    padding: "0.6rem 1.2rem",
                    background: "#b00020",
                    color: "#fff",
                    border: "none",
                    borderRadius: "6px",
                    cursor: "pointer"
                }}
            >
                Reload Page
            </button>
        </div>
    );
}
