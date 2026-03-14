import React, { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useQualification } from "../context/QualificationContext";

const API_BASE = "/api/Demographics";

export default function DemographicsReview() {
    const navigate = useNavigate();
    const { qualificationId } = useQualification();
    const [records, setRecords] = useState([]);
    const [error, setError] = useState("");
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        if (!qualificationId) {
            setError("No Qualification selected.");
            setRecords([]);
            return;
        }

        setLoading(true);
        setError("");
        setRecords([]);

        const load = async () => {
            try {
                const res = await fetch(
                    `${API_BASE}/byQualification?qualificationId=${qualificationId}`
                );
                if (!res.ok) {
                    const msg = await res.text();
                    throw new Error(msg || "Failed to load demographics");
                }
                const data = await res.json();
                setRecords(Array.isArray(data) ? data : []);
            } catch (e) {
                setError(e.message);
            } finally {
                setLoading(false);
            }
        };

        load();
    }, [qualificationId]);

    return (
        <div className="page-container">
            <h2>Demographics Review</h2>

            {error && <div className="error-banner">{error}</div>}
            {loading && <div className="loading-banner">Loading...</div>}

            {(!loading && records.length > 0) && (
                <div className="success-banner">Data was captured for this page.</div>
            )}

            {(!loading && records.length === 0 && !error) && (
                <div className="info-banner">No demographics captured yet, but you may proceed.</div>
            )}

            <table className="table">
                <thead>
                    <tr>
                        <th>Males</th>
                        <th>Females</th>
                        <th>African</th>
                        <th>Whites</th>
                        <th>Coloureds</th>
                        <th>Asian</th>
                        <th>With Disabilities</th>
                        <th>Total</th>
                        <th>Age Group</th>
                        <th>Region</th>
                    </tr>
                </thead>
                <tbody>
                    {loading ? (
                        <tr><td colSpan={10}>Loading...</td></tr>
                    ) : records.length === 0 ? (
                        <tr>
                            <td colSpan={10}>No demographics captured yet.</td>
                        </tr>
                    ) : (
                        records.map((d) => (
                            <tr key={d.id}>
                                <td>{d.numberOfMales ?? ''}</td>
                                <td>{d.numberOfFemales ?? ''}</td>
                                <td>{d.numberAfrican ?? ''}</td>
                                <td>{d.numberWhites ?? ''}</td>
                                <td>{d.numberColoureds ?? ''}</td>
                                <td>{d.numberAsian ?? ''}</td>
                                <td>{d.numberWithDisabilities ?? ''}</td>
                                <td>{d.totalNumberOfStudents ?? ''}</td>
                                <td>{d.ageGroup ?? ''}</td>
                                <td>{d.region ?? ''}</td>
                            </tr>
                        ))
                    )}
                </tbody>
            </table>

            <div className="button-row">
                <button onClick={() => navigate("/demographics")}>Back</button>

                {/* ⭐ FIXED: qualificationId is now passed into /phases */}
                <button
                    className="next-step-button"
                    onClick={() =>
                        navigate("/phases", {
                            state: { qualificationId }
                        })
                    }
                >
                    Goto Curriculum Phases
                </button>
            </div>
        </div>
    );
}
