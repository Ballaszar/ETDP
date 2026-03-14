
import { createContext, useContext, useState, useEffect, useCallback, useMemo } from "react";

const QualificationContext = createContext();


export const QualificationProvider = ({ children }) => {
  const [qualificationId, setQualificationIdState] = useState(() => {
    // Try to restore from localStorage
    const stored = localStorage.getItem("qualificationId");
    return stored ? Number(stored) : null;
  });

  // Wrap setter to persist to localStorage
  const setQualificationId = useCallback((id) => {
    setQualificationIdState(id);
    if (id !== null && id !== undefined) {
      localStorage.setItem("qualificationId", id);
    } else {
      localStorage.removeItem("qualificationId");
    }
  }, []);

  useEffect(() => {
    // Sync state with localStorage if changed elsewhere
    const handleStorage = (e) => {
      if (e.key === "qualificationId") {
        setQualificationIdState(e.newValue ? Number(e.newValue) : null);
      }
    };
    window.addEventListener("storage", handleStorage);
    return () => window.removeEventListener("storage", handleStorage);
  }, []);

  useEffect(() => {
    const qid = Number(qualificationId || 0);
    if (!qid) return;

    const controller = new AbortController();
    (async () => {
      try {
        const res = await fetch(`/api/Qualification/${qid}`, { signal: controller.signal });
        if (res.status === 404) {
          setQualificationIdState(null);
          localStorage.removeItem("qualificationId");
        }
      } catch {
        // Ignore validation errors while offline; retain current selection.
      }
    })();

    return () => controller.abort();
  }, [qualificationId]);

  const contextValue = useMemo(() => ({
    qualificationId,
    setQualificationId
  }), [qualificationId, setQualificationId]);

  return (
    <QualificationContext.Provider value={contextValue}>
      {children}
    </QualificationContext.Provider>
  );
};

export const useQualification = () => useContext(QualificationContext);
