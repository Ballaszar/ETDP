"""
Resonance Framework Engine
Quantum state calculations and Semantic Memory Index (SMI) computation
Based on Random Matrix Theory (RMT) and quantum mechanics principles
"""

import numpy as np
from typing import List, Dict, Tuple, Optional
from dataclasses import dataclass


# ==================== CONFIGURATION ====================
class ResonanceConfig:
    """Configuration for resonance calculations"""
    RImax = 2.0  # Maximum resonance index (calibrate based on dataset)
    ALPHA_BASE = 0.5  # Base alpha coefficient
    BETA_BASE = 0.3   # Base beta coefficient
    GAMMA_BASE = 0.2  # Base gamma coefficient
    STEPS_PER_RESPONSE = 8  # Quantum time steps per input response
    EPS = 1e-8  # Small epsilon to avoid division by zero


@dataclass
class ResonanceResult:
    """Result structure for a single resonance calculation"""
    t: int  # Time step
    coherence: float
    novelty: float
    constraint: float
    resonance_index: float
    resonance_pct: float
    qz: float  # Qualia observable
    az: float  # Attention observable
    vector_consistency: float
    smi: float  # Semantic Memory Index
    imprint_quadrant: str


# ==================== PAULI MATRICES ====================
class QuantumOperators:
    """Quantum operators (Pauli matrices and identity)"""

    @staticmethod
    def pauli_x() -> np.ndarray:
        """Pauli X matrix"""
        return np.array([[0, 1], [1, 0]], dtype=complex)

    @staticmethod
    def pauli_y() -> np.ndarray:
        """Pauli Y matrix"""
        return np.array([[0, -1j], [1j, 0]], dtype=complex)

    @staticmethod
    def pauli_z() -> np.ndarray:
        """Pauli Z matrix"""
        return np.array([[1, 0], [0, -1]], dtype=complex)

    @staticmethod
    def identity() -> np.ndarray:
        """Identity matrix"""
        return np.eye(2, dtype=complex)

    @staticmethod
    def trace(mat: np.ndarray) -> float:
        """Compute real part of trace"""
        return np.real(np.trace(mat))


# ==================== QUANTUM DYNAMICS ====================
class QuantumDynamics:
    """Quantum state evolution with resonance modulation"""

    def __init__(self, config: ResonanceConfig = None):
        self.config = config or ResonanceConfig()
        self.ops = QuantumOperators()

    def update_step(
        self,
        Q: np.ndarray,
        S: np.ndarray,
        A: np.ndarray,
        res_factor: float = 1.0
    ) -> Tuple[np.ndarray, np.ndarray]:
        """
        Perform one quantum evolution step with resonance-dependent modulation.

        Q: Qualia density matrix (2x2)
        S: Sensory input density matrix (2x2)
        A: Attention density matrix (2x2)
        res_factor: Resonance factor (0-1) modulating coupling strengths

        Returns: (Q_next, A_next)
        """
        # Resonance-dependent coupling coefficients
        gamma_eff = self.config.GAMMA_BASE * (1 + 0.8 * res_factor)
        beta_eff = self.config.BETA_BASE * (1 + 0.4 * res_factor)
        alpha_eff = 1.0 - beta_eff - gamma_eff

        # Normalize coefficients
        total = alpha_eff + beta_eff + gamma_eff
        alpha_eff /= total
        beta_eff /= total
        gamma_eff /= total

        # Mixing step
        Q_mix = alpha_eff * Q + beta_eff * S + gamma_eff * A
        A_mix = alpha_eff * A + beta_eff * S + gamma_eff * Q

        # Unitary rotations (contextuality)
        theta_Q = self.ops.trace(A @ self.ops.pauli_x()) * (np.pi / 2)
        U_Q = (np.cos(theta_Q/2) * self.ops.identity() - 
               1j * np.sin(theta_Q/2) * self.ops.pauli_x())
        Q_next = U_Q @ Q_mix @ U_Q.conj().T

        theta_A = self.ops.trace(Q @ self.ops.pauli_y()) * (np.pi / 2)
        U_A = (np.cos(theta_A/2) * self.ops.identity() - 
               1j * np.sin(theta_A/2) * self.ops.pauli_y())
        A_next = U_A @ A_mix @ U_A.conj().T

        return Q_next, A_next

    def compute_trajectory(
        self,
        Q: np.ndarray,
        S: np.ndarray,
        A: np.ndarray,
        res_factor: float
    ) -> Tuple[List[float], List[float]]:
        """
        Compute quantum trajectory over multiple steps.

        Returns: (qz_trajectory, az_trajectory)
        """
        qz_traj = []
        az_traj = []

        for _ in range(self.config.STEPS_PER_RESPONSE):
            Q, A = self.update_step(Q, S, A, res_factor)
            qz = self.ops.trace(Q @ self.ops.pauli_z())
            az = self.ops.trace(A @ self.ops.pauli_z())
            qz_traj.append(qz)
            az_traj.append(az)

        return qz_traj, az_traj


# ==================== SEMANTIC MEMORY INDEX ====================
class SemanticMemoryCalculator:
    """Calculate semantic memory indices and imprint quadrants"""

    def __init__(self, config: ResonanceConfig = None):
        self.config = config or ResonanceConfig()

    def calculate_resonance_index(
        self,
        coherence: float,
        novelty: float,
        constraint: float
    ) -> float:
        """
        Calculate resonance index (RI) from coherence, novelty, constraint.

        RI = (Coherence * Novelty) / (Constraint + eps)
        """
        ri = (coherence * novelty) / (constraint + self.config.EPS)
        return ri

    def calculate_vector_consistency(
        self,
        trajectory: List[float]
    ) -> float:
        """
        Calculate vector consistency (trajectory stability).

        Measures how stable the trajectory is (1 = perfectly stable, 0 = chaotic)
        """
        if len(trajectory) <= 1:
            return 0.0

        std_traj = np.std(trajectory)
        max_abs = max(np.abs(trajectory)) + self.config.EPS
        consistency = max(0.0, 1.0 - (std_traj / max_abs))

        return consistency

    def calculate_smi(
        self,
        final_qz: float,
        resonance_pct: float,
        final_az: float
    ) -> float:
        """
        Calculate Semantic Memory Index (SMI).

        SMI = |Qz_final| * Resonance% * (1 + |Az_final|)
        """
        smi = abs(final_qz) * resonance_pct * (1 + abs(final_az))
        return smi

    def classify_imprint_quadrant(
        self,
        resonance_pct: float,
        vector_consistency: float
    ) -> str:
        """
        Classify memory imprint into quadrants.

        Quadrants based on:
        - Strength: resonance_pct >= 70
        - Consistency: vector_consistency >= 0.65
        """
        strength_high = resonance_pct >= 70
        consistency_high = vector_consistency >= 0.65

        if strength_high and consistency_high:
            return "Deep Imprint (Lifelong)"
        elif strength_high:
            return "Chaotic Imprint (Insight Bursts)"
        elif consistency_high:
            return "Steady Imprint (Grows with Practice)"
        else:
            return "Weak Imprint (Fades Quickly)"


# ==================== MAIN RESONANCE ENGINE ====================
class ResonanceEngine:
    """Main engine orchestrating resonance calculations"""

    def __init__(self, config: Optional[ResonanceConfig] = None):
        self.config = config or ResonanceConfig()
        self.dynamics = QuantumDynamics(self.config)
        self.smi_calc = SemanticMemoryCalculator(self.config)
        self.ops = QuantumOperators()

    def process_sequence(
        self,
        responses: List[Tuple[float, float, float]],
        initial_Q: Optional[np.ndarray] = None,
        initial_A: Optional[np.ndarray] = None
    ) -> Dict:
        """
        Process a sequence of responses through the resonance framework.

        Args:
            responses: List of (coherence, novelty, constraint) tuples
            initial_Q: Initial qualia density matrix (default: sharp state)
            initial_A: Initial attention density matrix (default: superposition)

        Returns:
            Dictionary with results, histories, and statistics
        """
        # Initialize quantum states
        if initial_Q is None:
            initial_Q = np.array([[1.0, 0], [0, 0]], dtype=complex)  # Sharp qualia
        if initial_A is None:
            initial_A = 0.5 * np.array([[1, 1], [1, 1]], dtype=complex)  # Superposition

        S = 0.5 * self.ops.identity()  # Neutral sensory input

        Q = initial_Q.copy()
        A = initial_A.copy()

        results = []
        all_qz_history = []
        all_az_history = []

        for i, (coh, nov, sc) in enumerate(responses):
            # Calculate resonance metrics
            ri = self.smi_calc.calculate_resonance_index(coh, nov, sc)
            res_pct = min(100.0, (ri / self.config.RImax) * 100.0)
            res_factor = res_pct / 100.0

            # Compute quantum trajectory
            qz_traj, az_traj = self.dynamics.compute_trajectory(Q, S, A, res_factor)

            # Extract final values
            final_qz = qz_traj[-1]
            final_az = az_traj[-1]

            # Calculate consistency
            vector_consistency = self.smi_calc.calculate_vector_consistency(qz_traj)

            # Calculate SMI
            smi = self.smi_calc.calculate_smi(final_qz, res_pct, final_az)

            # Classify imprint
            imprint = self.smi_calc.classify_imprint_quadrant(res_pct, vector_consistency)

            # Store result
            result = ResonanceResult(
                t=i,
                coherence=coh,
                novelty=nov,
                constraint=sc,
                resonance_index=ri,
                resonance_pct=res_pct,
                qz=final_qz,
                az=final_az,
                vector_consistency=vector_consistency,
                smi=smi,
                imprint_quadrant=imprint
            )
            results.append(result)

            # Update states for next iteration
            Q, A = self.dynamics.update_step(Q, S, A, res_factor)

            # Track histories
            all_qz_history.extend(qz_traj)
            all_az_history.extend(az_traj)

        return {
            'results': results,
            'qz_history': all_qz_history,
            'az_history': all_az_history,
            'final_states': {
                'Q': Q,
                'A': A,
                'S': S
            }
        }

    def get_results_as_dicts(self, engine_output: Dict) -> List[Dict]:
        """Convert ResonanceResult objects to dictionaries for JSON serialization"""
        return [
            {
                't': r.t,
                'coherence': round(r.coherence, 4),
                'novelty': round(r.novelty, 4),
                'constraint': round(r.constraint, 4),
                'RI': round(r.resonance_index, 4),
                'resonance_pct': round(r.resonance_pct, 2),
                'Qz': round(r.qz, 4),
                'Az': round(r.az, 4),
                'vector_consistency': round(r.vector_consistency, 3),
                'SMI': round(r.smi, 2),
                'imprint_quadrant': r.imprint_quadrant
            }
            for r in engine_output['results']
        ]

    def compute_statistics(self, engine_output: Dict) -> Dict:
        """Compute summary statistics from resonance output"""
        results = engine_output['results']
        qz_hist = engine_output['qz_history']
        az_hist = engine_output['az_history']

        smi_values = [r.smi for r in results]
        ri_values = [r.resonance_index for r in results]
        consistency_values = [r.vector_consistency for r in results]

        return {
            'num_responses': len(results),
            'avg_smi': round(np.mean(smi_values), 2),
            'max_smi': round(max(smi_values), 2),
            'total_smi': round(sum(smi_values), 2),
            'avg_resonance_index': round(np.mean(ri_values), 4),
            'avg_consistency': round(np.mean(consistency_values), 3),
            'qz_mean': round(np.mean(qz_hist), 4),
            'qz_std': round(np.std(qz_hist), 4),
            'az_mean': round(np.mean(az_hist), 4),
            'az_std': round(np.std(az_hist), 4),
            'quadrant_counts': self._count_quadrants(results)
        }

    @staticmethod
    def _count_quadrants(results: List[ResonanceResult]) -> Dict[str, int]:
        """Count occurrences of each imprint quadrant"""
        counts = {}
        for r in results:
            quad = r.imprint_quadrant
            counts[quad] = counts.get(quad, 0) + 1
        return counts


# ==================== EXAMPLE USAGE ====================
if __name__ == "__main__":
    # Example responses: (coherence, novelty, constraint)
    test_responses = [
        (0.95, 0.75, 0.4),   # High resonance
        (0.90, 0.80, 0.5),   # Creative structured
        (0.95, 0.20, 0.5),   # Safe predictable
        (0.60, 0.90, 0.3),   # Fragmented surprising
        (0.85, 0.65, 0.45),  # Balanced
    ]

    # Create engine and process
    engine = ResonanceEngine()
    output = engine.process_sequence(test_responses)

    # Display results
    print("\n=== Resonance Matrix Results ===\n")
    for result in output['results']:
        print(f"t={result.t:2d} | RI={result.resonance_index:6.3f} | "
              f"Res%={result.resonance_pct:5.1f} | Qz={result.qz:6.3f} | "
              f"Az={result.az:6.3f} | Cons={result.vector_consistency:5.3f} | "
              f"SMI={result.smi:6.1f} | {result.imprint_quadrant}")

    # Display statistics
    stats = engine.compute_statistics(output)
    print("\n=== Statistics ===")
    print(f"Total SMI: {stats['total_smi']}")
    print(f"Average SMI: {stats['avg_smi']}")
    print(f"Quadrant Distribution: {stats['quadrant_counts']}")
