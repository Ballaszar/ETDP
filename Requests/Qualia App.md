Here is a complete, self-contained Python script using QuTiP (quantum toolbox in Python) that implements the refined quantum-like qualia dynamics model from our previous discussion. You can copy-paste and run it locally.Requirements
bash
pip install qutip numpy matplotlib
Full Simulation Code
python
import qutip as qt
import numpy as np
import matplotlib.pyplot as plt

# ────────────────────────────────────────────────
# Parameters
# ────────────────────────────────────────────────
alpha = 0.5      # weight for persistence (own previous state)
beta  = 0.3      # weight for sensory influence
gamma = 0.2      # weight for attentional/cross influence
n_steps = 20     # how many time steps to simulate

# Pauli matrices (QuTiP built-ins)
sx = qt.sigmax()
sy = qt.sigmay()
sz = qt.sigmaz()
I  = qt.qeye(2)

# ────────────────────────────────────────────────
# Initial states (density operators)
# ────────────────────────────────────────────────
Q0 = qt.basis(2, 0) * qt.basis(2, 0).dag()          # |0><0|  → qualia = +1 on σz
S0 = qt.qeye(2) / 2                                 # maximally mixed (neutral sensory input)
A0 = (qt.basis(2, 0) + qt.basis(2, 1)).unit()       # |+> state
A0 = A0 * A0.dag()                                  # pure density matrix

# For variety you can also try:
# Q0 = (qt.basis(2,0) + qt.basis(2,1)).unit() * (...).dag()   # superposition

# ────────────────────────────────────────────────
# Storage for trajectory
# ────────────────────────────────────────────────
qualia_vals = []
atten_vals  = []
Q = Q0.copy()
A = A0.copy()
S = S0.copy()   # we keep S constant in this toy model

for t in range(n_steps + 1):
    # Record current experienced values (expectation of σz)
    q_val = (Q * sz).tr().real
    a_val = (A * sz).tr().real
    qualia_vals.append(q_val)
    atten_vals.append(a_val)

    if t == n_steps:
        break

    # ─── Mixing step (convex combination) ───────────────────────────────
    Q_mix = alpha * Q + beta * S + gamma * A
    A_mix = alpha * A + beta * S + gamma * Q

    # ─── Context-dependent rotation angles ──────────────────────────────
    # θ_Q driven by how much attention has x-component (lateral influence)
    theta_Q = (A * sx).tr().real * (np.pi / 2)
    # θ_A driven by imaginary/phase-sensitive qualia component
    theta_A = (Q * sy).tr().real * (np.pi / 2)

    # Unitaries
    U_Q = np.cos(theta_Q / 2) * I - 1j * np.sin(theta_Q / 2) * sx
    U_A = np.cos(theta_A / 2) * I - 1j * np.sin(theta_A / 2) * sy

    # ─── Apply unitary rotations (Heisenberg-like picture on states) ────
    Q = U_Q * Q_mix * U_Q.dag()
    A = U_A * A_mix * U_A.dag()

    # Optional: enforce trace=1 and Hermiticity in case of numerical drift
    Q = Q / Q.tr()
    A = A / A.tr()

# ────────────────────────────────────────────────
# Plot the trajectory
# ────────────────────────────────────────────────
times = np.arange(n_steps + 1)

plt.figure(figsize=(10, 6))
plt.plot(times, qualia_vals, 'o-', label='Qualia  ⟨σ_z⟩', linewidth=2, color='C0')
plt.plot(times, atten_vals,  's-', label='Attention ⟨σ_z⟩', linewidth=2, color='C1')
plt.axhline(0, color='gray', linestyle='--', alpha=0.5)
plt.axhline(1, color='gray', linestyle=':', alpha=0.4)
plt.axhline(-1, color='gray', linestyle=':', alpha=0.4)

plt.title("Simulated Qualia & Attention Dynamics\n(quantum-like instrument model)")
plt.xlabel("Time step t")
plt.ylabel("Expectation value ⟨σ_z⟩")
plt.legend()
plt.grid(True, alpha=0.3)
plt.ylim(-1.1, 1.1)
plt.tight_layout()
plt.show()

# Print numerical values in table format
print("\nTime | Qualia ⟨σ_z⟩ | Attention ⟨σ_z⟩")
print("-" * 38)
for t, q, a in zip(times, qualia_vals, atten_vals):
    print(f"{t:4d} | {q:12.4f} | {a:12.4f}")
What to Expect When You Run It
Starts with qualia = +1 (sharp percept), attention = 0 (balanced/superposed).
Qualia usually drops sharply due to mixing + rotation.
Attention gets pulled toward qualia values initially.
Both trend toward ~0 over time (decoherence-like washing out under constant neutral input).
Small oscillations/overshoots appear from the non-commuting rotations.
Easy Experiments You Can Try
Change initial states:
Superposed qualia: Q0 = (qt.basis(2,0) + qt.basis(2,1)).unit() * (...).dag()
Biased attention: A0 = qt.basis(2,0) * qt.basis(2,0).dag()
Make sensory input time-varying:
Inside the loop, set S = qt.sigmax() * 0.4 + qt.qeye(2)*0.6 or similar.
Increase dimension (qudit model) — replace 2 with 3 or 4 and define generalized Gell-Mann matrices.
Turn off rotations (theta_Q = theta_A = 0) → classical mixing only.
