import { createContext, useContext, useEffect, useMemo, useState } from "react";

const GlossaryContext = createContext({
  loading: false,
  autoTagEnabled: true,
  setAutoTagEnabled: () => {},
  getDefinition: () => "",
});

const DEFAULT_AUTO_TAG_ENABLED = true;
const AUTO_TAG_PREF_PREFIX = "etdp:glossary:auto-tags:";

const KNOWN_ALIASES = new Map([
  ["nqf", "nqf level"],
  ["nqf levels", "nqf level"],
  ["credits", "credit"],
  ["learning program", "learning programme"],
  ["learning programmes", "learning programme"],
  ["learning programs", "learning programme"],
]);

const normalize = (value) =>
  String(value || "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .trim();

const parseRows = (rows) => (Array.isArray(rows) ? rows : []);

const buildPreferenceKey = () => {
  try {
    const activationToken = String(localStorage.getItem("activationToken") || "").trim();
    const appApiKey = String(localStorage.getItem("appApiKey") || "").trim();
    const rawIdentity = activationToken || appApiKey || "anonymous";
    const compactIdentity = rawIdentity.replace(/[^a-z0-9]/gi, "").toLowerCase();
    const suffix = compactIdentity.slice(-24) || "anonymous";
    return `${AUTO_TAG_PREF_PREFIX}${suffix}`;
  } catch {
    return `${AUTO_TAG_PREF_PREFIX}anonymous`;
  }
};

const readAutoTagPreference = (key) => {
  try {
    const raw = String(localStorage.getItem(key) || "").trim().toLowerCase();
    if (!raw) return DEFAULT_AUTO_TAG_ENABLED;
    if (raw === "1" || raw === "true" || raw === "yes" || raw === "on") return true;
    if (raw === "0" || raw === "false" || raw === "no" || raw === "off") return false;
  } catch {
    // Ignore storage read errors and use default.
  }
  return DEFAULT_AUTO_TAG_ENABLED;
};

const isHeaderRow = (cols) => {
  const joined = cols.map((c) => String(c || "").trim().toLowerCase()).join("|");
  return (
    joined === "column a|column b" ||
    joined.startsWith("index|terminology|definition")
  );
};

export const GlossaryProvider = ({ children }) => {
  const [loading, setLoading] = useState(false);
  const [termMap, setTermMap] = useState(new Map());
  const [fallbackMap, setFallbackMap] = useState(new Map());
  const [autoTagPreferenceKey, setAutoTagPreferenceKey] = useState(() => buildPreferenceKey());
  const [autoTagEnabled, setAutoTagEnabled] = useState(() => readAutoTagPreference(buildPreferenceKey()));

  useEffect(() => {
    const refreshIdentityPreference = () => {
      const nextKey = buildPreferenceKey();
      setAutoTagPreferenceKey(nextKey);
      setAutoTagEnabled(readAutoTagPreference(nextKey));
    };

    window.addEventListener("storage", refreshIdentityPreference);
    return () => {
      window.removeEventListener("storage", refreshIdentityPreference);
    };
  }, []);

  useEffect(() => {
    try {
      localStorage.setItem(autoTagPreferenceKey, autoTagEnabled ? "1" : "0");
    } catch {
      // Ignore storage write errors.
    }
  }, [autoTagPreferenceKey, autoTagEnabled]);

  useEffect(() => {
    let active = true;
    setLoading(true);

    fetch("/api/Knowledge/knowledge-pools")
      .then((r) => (r.ok ? r.json() : null))
      .then((json) => {
        if (!active || !json) return;
        const files = Array.isArray(json.files) ? json.files : [];
        const nextTerms = new Map();
        const nextFallback = new Map();

        for (const file of files) {
          const name = String(file?.name || "").toLowerCase();
          const rows = parseRows(file?.rows);

          if (name.includes("teminology") || name.includes("terminology")) {
            rows.forEach((row, idx) => {
              const cols = parseRows(row);
              if (idx === 0 && isHeaderRow(cols)) return;
              const term = String(cols[1] ?? "").trim();
              const definition = String(cols[2] ?? "").trim();
              if (!term || !definition) return;

              const keys = new Set();
              keys.add(normalize(term));
              keys.add(normalize(term.replace(/\s*\([^)]*\)\s*/g, " ").trim()));
              for (const key of keys) {
                if (key && !nextTerms.has(key)) {
                  nextTerms.set(key, definition);
                }
              }
            });
          }

          if (name.includes("level descriptor")) {
            for (let i = 0; i < rows.length; i++) {
              const cols = parseRows(rows[i]).map((c) => String(c ?? "").trim());
              if (i === 0 && isHeaderRow(cols)) continue;
              if (!cols[0] || !cols[1]) continue;
              if (cols[0].toLowerCase().startsWith("nqf level")) {
                const key = normalize(cols[0]);
                if (!nextFallback.has(key)) nextFallback.set(key, cols[1]);
                if (!nextFallback.has("nqf level")) nextFallback.set("nqf level", cols[1]);
              }
            }
          }

          if (name.includes("acronym")) {
            rows.forEach((row) => {
              const cols = parseRows(row).map((c) => String(c ?? "").trim());
              if (cols.length < 2 || !cols[0] || !cols[1]) return;
              const key = normalize(cols[0]);
              if (key && !nextFallback.has(key)) nextFallback.set(key, cols[1]);
            });
          }
        }

        if (active) {
          setTermMap(nextTerms);
          setFallbackMap(nextFallback);
        }
      })
      .catch(() => {})
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, []);

  const getDefinition = useMemo(() => {
    return (term) => {
      const original = normalize(term);
      if (!original) return "";

      const alias = KNOWN_ALIASES.get(original);
      const keys = [original, alias].filter(Boolean);

      for (const key of keys) {
        if (termMap.has(key)) return termMap.get(key) || "";
      }
      for (const key of keys) {
        if (fallbackMap.has(key)) return fallbackMap.get(key) || "";
      }

      for (const [k, v] of termMap.entries()) {
        if (k.includes(original) || original.includes(k)) return v;
      }
      for (const [k, v] of fallbackMap.entries()) {
        if (k.includes(original) || original.includes(k)) return v;
      }
      return "";
    };
  }, [termMap, fallbackMap]);

  const value = useMemo(
    () => ({
      loading,
      autoTagEnabled,
      setAutoTagEnabled,
      getDefinition,
    }),
    [loading, autoTagEnabled, getDefinition]
  );

  return <GlossaryContext.Provider value={value}>{children}</GlossaryContext.Provider>;
};

export const useGlossary = () => useContext(GlossaryContext);
