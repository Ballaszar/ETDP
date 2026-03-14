import { useEffect } from "react";
import { useGlossary } from "../context/GlossaryContext";

const CANDIDATE_SELECTOR = "label, th, h2, h3, strong";

const TERM_RULES = [
  { term: "notional hours", pattern: /\bnotional\s+hours?\b/i },
  { term: "assessment criteria", pattern: /assessment\s+criteria/i },
  { term: "learning programme", pattern: /learning\s+(programme|program|phases?)/i },
  { term: "nqf level", pattern: /\bnqf(\s+level)?\b/i },
  { term: "qualification", pattern: /qualification/i },
  { term: "curriculum", pattern: /curriculum/i },
  { term: "credit", pattern: /\bcredits?\b/i },
  { term: "subject", pattern: /\bsubjects?\b/i },
  { term: "topic", pattern: /\btopics?\b/i },
  { term: "outcome", pattern: /\boutcomes?\b/i },
  { term: "lesson plan", pattern: /lesson\s+plan/i },
];

function normalizeText(value) {
  return String(value || "")
    .replace(/\s+/g, " ")
    .replace(/[|]/g, " ")
    .trim();
}

function resolveDefinition(text, getDefinition) {
  const source = normalizeText(text);
  if (!source) return null;

  for (const rule of TERM_RULES) {
    if (!rule.pattern.test(source)) continue;
    const definition = String(getDefinition(rule.term) || "").trim();
    if (definition) return { term: rule.term, definition };
  }

  const fallback = String(getDefinition(source) || "").trim();
  if (fallback) return { term: source, definition: fallback };

  return null;
}

function extractElementLabel(el) {
  const text = normalizeText(el?.textContent || "");
  if (!text) return "";
  const first = text.split(":")[0];
  return normalizeText(first);
}

function clearAutoTags(root) {
  const tagged = root.querySelectorAll(".glossary-auto-tagged");
  tagged.forEach((el) => {
    if (!(el instanceof HTMLElement)) return;
    el.classList.remove("glossary-auto-tagged");
    el.removeAttribute("data-glossary-term");
    el.removeAttribute("data-glossary-definition");
    if (el.getAttribute("data-glossary-added-title") === "1") {
      el.removeAttribute("title");
      el.removeAttribute("data-glossary-added-title");
    }
  });
}

export default function GlossaryAutoTagger() {
  const { getDefinition, autoTagEnabled } = useGlossary();

  useEffect(() => {
    let raf = 0;
    const observedRoot = document.body;
    if (!observedRoot) return undefined;
    if (!autoTagEnabled) {
      clearAutoTags(observedRoot);
      return undefined;
    }

    const annotate = () => {
      const nodes = observedRoot.querySelectorAll(CANDIDATE_SELECTOR);
      nodes.forEach((el) => {
        if (!(el instanceof HTMLElement)) return;
        if (el.closest("[data-glossary-skip='1']")) return;
        if (el.querySelector(".glossary-label-wrap")) return;

        const label = extractElementLabel(el);
        if (!label || label.length > 120) return;

        const match = resolveDefinition(label, getDefinition);
        if (!match) {
          if (el.classList.contains("glossary-auto-tagged")) {
            el.classList.remove("glossary-auto-tagged");
            el.removeAttribute("data-glossary-term");
            el.removeAttribute("data-glossary-definition");
            if (el.getAttribute("data-glossary-added-title") === "1") {
              el.removeAttribute("title");
              el.removeAttribute("data-glossary-added-title");
            }
          }
          return;
        }

        el.classList.add("glossary-auto-tagged");
        el.setAttribute("data-glossary-term", match.term);
        el.setAttribute("data-glossary-definition", match.definition);

        const hasTitle = String(el.getAttribute("title") || "").trim().length > 0;
        if (!hasTitle || el.getAttribute("data-glossary-added-title") === "1") {
          el.setAttribute("title", match.definition);
          el.setAttribute("data-glossary-added-title", "1");
        }
      });
    };

    const scheduleAnnotate = () => {
      if (raf) cancelAnimationFrame(raf);
      raf = requestAnimationFrame(() => {
        raf = 0;
        annotate();
      });
    };

    scheduleAnnotate();

    const observer = new MutationObserver(() => {
      scheduleAnnotate();
    });

    observer.observe(observedRoot, {
      subtree: true,
      childList: true,
      characterData: true,
    });

    return () => {
      if (raf) cancelAnimationFrame(raf);
      observer.disconnect();
    };
  }, [getDefinition, autoTagEnabled]);

  return null;
}
