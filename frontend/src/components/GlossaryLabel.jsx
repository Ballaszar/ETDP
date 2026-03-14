import React from "react";
import { useGlossary } from "../context/GlossaryContext";

export default function GlossaryLabel({ label, term }) {
  const { getDefinition } = useGlossary();
  const resolvedLabel = String(label || "");
  const resolvedTerm = String(term || label || "");
  const definition = getDefinition(resolvedTerm);

  if (!definition) return <>{resolvedLabel}</>;

  return (
    <span className="glossary-label-wrap">
      <span>{resolvedLabel}</span>
      <abbr
        className="glossary-tag"
        title={definition}
        aria-label={`${resolvedTerm}: ${definition}`}
      >
        i
      </abbr>
    </span>
  );
}

