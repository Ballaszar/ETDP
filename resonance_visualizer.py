"""
Resonance Framework Visualization Module
Generates publication-quality plots for resonance analysis
"""

import numpy as np
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
from typing import Dict, List, Tuple, Optional
import io
import base64
from datetime import datetime


class ResonanceVisualizer:
    """Visualizes resonance framework results"""

    def __init__(self, figsize: Tuple[int, int] = (12, 8), dpi: int = 100):
        self.figsize = figsize
        self.dpi = dpi
        self.colors = {
            'qz': '#1f77b4',      # Blue
            'az': '#ff7f0e',      # Orange
            'deep': '#2ca02c',    # Green
            'chaotic': '#d62728', # Red
            'steady': '#9467bd',  # Purple
            'weak': '#8c564b'     # Brown
        }

    def plot_trajectories(
        self,
        qz_history: List[float],
        az_history: List[float],
        response_boundaries: Optional[List[int]] = None,
        title: str = "Quantum State Trajectories (Qz and Az over Time)"
    ) -> plt.Figure:
        """
        Plot Qz and Az trajectories over time.

        Args:
            qz_history: Qualia observable values over time
            az_history: Attention observable values over time
            response_boundaries: Indices marking response boundaries
            title: Plot title

        Returns:
            matplotlib Figure object
        """
        fig, ax = plt.subplots(figsize=self.figsize, dpi=self.dpi)

        time_steps = np.arange(len(qz_history))

        # Plot trajectories
        ax.plot(time_steps, qz_history, color=self.colors['qz'], linewidth=2, 
                label='Qz (Qualia)', alpha=0.8)
        ax.plot(time_steps, az_history, color=self.colors['az'], linewidth=2, 
                label='Az (Attention)', alpha=0.8)

        # Add response boundaries
        if response_boundaries:
            for boundary in response_boundaries[1:]:
                ax.axvline(boundary, color='gray', linestyle='--', alpha=0.5, linewidth=1)

        ax.set_xlabel('Time Steps', fontsize=12)
        ax.set_ylabel('Observable Value', fontsize=12)
        ax.set_title(title, fontsize=14, fontweight='bold')
        ax.legend(loc='best', fontsize=11)
        ax.grid(True, alpha=0.3)

        return fig

    def plot_imprint_quadrants(
        self,
        results: List[Dict],
        title: str = "Imprint Quadrant Distribution"
    ) -> plt.Figure:
        """
        Create scatter plot of responses in quadrant space.

        X-axis: Resonance%
        Y-axis: Vector Consistency
        Size/color: SMI value

        Args:
            results: List of resonance result dictionaries
            title: Plot title

        Returns:
            matplotlib Figure object
        """
        fig, ax = plt.subplots(figsize=self.figsize, dpi=self.dpi)

        # Extract data
        resonance_pcts = [r['resonance_pct'] for r in results]
        consistencies = [r['vector_consistency'] for r in results]
        smi_values = [r['SMI'] for r in results]
        imprints = [r['imprint_quadrant'] for r in results]
        time_indices = [r['t'] for r in results]

        # Normalize SMI for size
        smi_norm = np.array(smi_values)
        if smi_norm.max() > 0:
            smi_sizes = (smi_norm / smi_norm.max()) * 500 + 50
        else:
            smi_sizes = 100

        # Quadrant boundaries
        ax.axvline(70, color='gray', linestyle='-', alpha=0.3, linewidth=2)
        ax.axhline(0.65, color='gray', linestyle='-', alpha=0.3, linewidth=2)

        # Plot points colored by imprint type
        quadrant_colors = {
            "Deep Imprint (Lifelong)": self.colors['deep'],
            "Chaotic Imprint (Insight Bursts)": self.colors['chaotic'],
            "Steady Imprint (Grows with Practice)": self.colors['steady'],
            "Weak Imprint (Fades Quickly)": self.colors['weak']
        }

        for i, (res_pct, cons, imprint) in enumerate(zip(resonance_pcts, consistencies, imprints)):
            color = quadrant_colors.get(imprint, 'gray')
            ax.scatter(res_pct, cons, s=smi_sizes[i], color=color, alpha=0.7, 
                      edgecolors='black', linewidth=1)
            # Add text labels
            ax.text(res_pct, cons, str(time_indices[i]), ha='center', va='center', 
                   fontsize=8, fontweight='bold')

        # Add quadrant labels
        ax.text(85, 0.8, "Deep Imprint\n(Lifelong)", ha='center', va='top', 
               fontsize=10, style='italic', alpha=0.7)
        ax.text(85, 0.4, "Chaotic Imprint\n(Insight Bursts)", ha='center', va='top', 
               fontsize=10, style='italic', alpha=0.7)
        ax.text(35, 0.8, "Steady Imprint\n(Grows with Practice)", ha='center', va='top', 
               fontsize=10, style='italic', alpha=0.7)
        ax.text(35, 0.4, "Weak Imprint\n(Fades Quickly)", ha='center', va='top', 
               fontsize=10, style='italic', alpha=0.7)

        ax.set_xlim(0, 105)
        ax.set_ylim(-0.05, 1.05)
        ax.set_xlabel('Resonance %', fontsize=12)
        ax.set_ylabel('Vector Consistency', fontsize=12)
        ax.set_title(title, fontsize=14, fontweight='bold')
        ax.grid(True, alpha=0.2)

        # Create custom legend
        legend_elements = [
            mpatches.Patch(facecolor=self.colors['deep'], label='Deep Imprint'),
            mpatches.Patch(facecolor=self.colors['chaotic'], label='Chaotic Imprint'),
            mpatches.Patch(facecolor=self.colors['steady'], label='Steady Imprint'),
            mpatches.Patch(facecolor=self.colors['weak'], label='Weak Imprint')
        ]
        ax.legend(handles=legend_elements, loc='lower right', fontsize=10)

        return fig

    def plot_smi_curve(
        self,
        results: List[Dict],
        use_cumulative: bool = True,
        title: str = "Semantic Memory Index (SMI) Growth"
    ) -> plt.Figure:
        """
        Plot SMI growth over time.

        Args:
            results: List of resonance result dictionaries
            use_cumulative: If True, show cumulative SMI; else individual values
            title: Plot title

        Returns:
            matplotlib Figure object
        """
        fig, ax = plt.subplots(figsize=self.figsize, dpi=self.dpi)

        time_indices = [r['t'] for r in results]
        smi_values = [r['SMI'] for r in results]

        if use_cumulative:
            smi_data = np.cumsum(smi_values)
            ylabel = 'Cumulative SMI'
        else:
            smi_data = smi_values
            ylabel = 'SMI per Response'

        # Plot line and points
        ax.plot(time_indices, smi_data, color=self.colors['qz'], linewidth=2.5, 
               marker='o', markersize=8, label='SMI Trajectory', alpha=0.8)

        # Fill area under curve
        ax.fill_between(time_indices, smi_data, alpha=0.2, color=self.colors['qz'])

        ax.set_xlabel('Response Index', fontsize=12)
        ax.set_ylabel(ylabel, fontsize=12)
        ax.set_title(title, fontsize=14, fontweight='bold')
        ax.grid(True, alpha=0.3)
        ax.legend(fontsize=11)

        # Set integer x-axis
        ax.set_xticks(time_indices)

        return fig

    def plot_phase_space(
        self,
        results: List[Dict],
        title: str = "Phase Space: Qz vs Az"
    ) -> plt.Figure:
        """
        Plot phase space trajectory (Qz vs Az).

        Args:
            results: List of resonance result dictionaries
            title: Plot title

        Returns:
            matplotlib Figure object
        """
        fig, ax = plt.subplots(figsize=self.figsize, dpi=self.dpi)

        qz_values = [r['Qz'] for r in results]
        az_values = [r['Az'] for r in results]
        time_indices = [r['t'] for r in results]

        # Plot trajectory
        ax.plot(qz_values, az_values, color=self.colors['qz'], linewidth=2, 
               alpha=0.7, label='Trajectory')

        # Mark points with indices
        for i, (qz, az, t_idx) in enumerate(zip(qz_values, az_values, time_indices)):
            ax.scatter(qz, az, s=200, color=self.colors['qz'], alpha=0.8, 
                      edgecolors='black', linewidth=1.5)
            ax.text(qz, az, str(t_idx), ha='center', va='center', 
                   fontsize=9, fontweight='bold', color='white')

        # Add origin
        ax.scatter(0, 0, s=300, marker='x', color='red', linewidth=2, label='Origin (0,0)')

        ax.set_xlabel('Qz (Qualia)', fontsize=12)
        ax.set_ylabel('Az (Attention)', fontsize=12)
        ax.set_title(title, fontsize=14, fontweight='bold')
        ax.grid(True, alpha=0.3)
        ax.legend(fontsize=11)
        ax.axhline(y=0, color='k', linewidth=0.5)
        ax.axvline(x=0, color='k', linewidth=0.5)

        return fig

    def plot_resonance_index_heatmap(
        self,
        results: List[Dict],
        title: str = "Resonance Index Evolution"
    ) -> plt.Figure:
        """
        Bar chart showing resonance index evolution.

        Args:
            results: List of resonance result dictionaries
            title: Plot title

        Returns:
            matplotlib Figure object
        """
        fig, ax = plt.subplots(figsize=self.figsize, dpi=self.dpi)

        time_indices = [r['t'] for r in results]
        ri_values = [r['RI'] for r in results]
        imprints = [r['imprint_quadrant'] for r in results]

        # Color bars by imprint type
        quadrant_colors = {
            "Deep Imprint (Lifelong)": self.colors['deep'],
            "Chaotic Imprint (Insight Bursts)": self.colors['chaotic'],
            "Steady Imprint (Grows with Practice)": self.colors['steady'],
            "Weak Imprint (Fades Quickly)": self.colors['weak']
        }

        colors = [quadrant_colors.get(imprint, 'gray') for imprint in imprints]

        bars = ax.bar(time_indices, ri_values, color=colors, edgecolor='black', linewidth=1.5, alpha=0.8)

        ax.set_xlabel('Response Index', fontsize=12)
        ax.set_ylabel('Resonance Index', fontsize=12)
        ax.set_title(title, fontsize=14, fontweight='bold')
        ax.grid(True, alpha=0.3, axis='y')
        ax.set_xticks(time_indices)

        # Custom legend
        legend_elements = [
            mpatches.Patch(facecolor=self.colors['deep'], label='Deep Imprint'),
            mpatches.Patch(facecolor=self.colors['chaotic'], label='Chaotic Imprint'),
            mpatches.Patch(facecolor=self.colors['steady'], label='Steady Imprint'),
            mpatches.Patch(facecolor=self.colors['weak'], label='Weak Imprint')
        ]
        ax.legend(handles=legend_elements, loc='upper left', fontsize=10)

        return fig

    @staticmethod
    def fig_to_base64(fig: plt.Figure) -> str:
        """Convert matplotlib figure to base64 string for web display"""
        buffer = io.BytesIO()
        fig.savefig(buffer, format='png', bbox_inches='tight', dpi=100)
        buffer.seek(0)
        image_base64 = base64.b64encode(buffer.read()).decode()
        plt.close(fig)
        return image_base64

    def generate_all_plots(
        self,
        engine_output: Dict,
        results_dicts: List[Dict]
    ) -> Dict[str, str]:
        """
        Generate all visualization plots as base64-encoded images.

        Args:
            engine_output: Output from ResonanceEngine.process_sequence
            results_dicts: List of result dictionaries from engine

        Returns:
            Dictionary mapping plot names to base64-encoded PNG images
        """
        # Calculate response boundaries for trajectory plot
        response_boundaries = []
        cumulative = 0
        for i, _ in enumerate(results_dicts):
            response_boundaries.append(cumulative)
            cumulative += 8  # STEPS_PER_RESPONSE

        plots = {}

        # Trajectories
        fig = self.plot_trajectories(
            engine_output['qz_history'],
            engine_output['az_history'],
            response_boundaries
        )
        plots['trajectories'] = self.fig_to_base64(fig)

        # Imprint Quadrants
        fig = self.plot_imprint_quadrants(results_dicts)
        plots['imprint_quadrants'] = self.fig_to_base64(fig)

        # SMI Curve
        fig = self.plot_smi_curve(results_dicts, use_cumulative=True)
        plots['smi_cumulative'] = self.fig_to_base64(fig)

        fig = self.plot_smi_curve(results_dicts, use_cumulative=False)
        plots['smi_individual'] = self.fig_to_base64(fig)

        # Phase Space
        fig = self.plot_phase_space(results_dicts)
        plots['phase_space'] = self.fig_to_base64(fig)

        # Resonance Index
        fig = self.plot_resonance_index_heatmap(results_dicts)
        plots['resonance_index'] = self.fig_to_base64(fig)

        return plots


# ==================== HELPER FUNCTIONS ====================
def generate_summary_report(
    results_dicts: List[Dict],
    statistics: Dict,
    engine_output: Dict
) -> str:
    """
    Generate a markdown summary report of resonance analysis.

    Args:
        results_dicts: List of result dictionaries
        statistics: Statistics dictionary from engine
        engine_output: Full engine output

    Returns:
        Markdown-formatted report string
    """
    report = f"""
# Resonance Framework Analysis Report
Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}

## Summary Statistics
- **Total Responses Processed**: {statistics['num_responses']}
- **Total Semantic Memory Index (SMI)**: {statistics['total_smi']}
- **Average SMI per Response**: {statistics['avg_smi']}
- **Peak SMI**: {statistics['max_smi']}
- **Average Resonance Index**: {statistics['avg_resonance_index']}
- **Average Vector Consistency**: {statistics['avg_consistency']}

## Quantum Observable Statistics
### Qz (Qualia Observable)
- **Mean**: {statistics['qz_mean']}
- **Std Dev**: {statistics['qz_std']}

### Az (Attention Observable)
- **Mean**: {statistics['az_mean']}
- **Std Dev**: {statistics['az_std']}

## Imprint Quadrant Distribution
"""
    for quad, count in statistics['quadrant_counts'].items():
        report += f"- **{quad}**: {count}\n"

    report += """
## Detailed Results

| Response | Coherence | Novelty | Constraint | RI | Resonance% | Qz | Az | Consistency | SMI | Imprint |
|----------|-----------|---------|------------|-----|------------|-----|-----|-------------|-----|---------|
"""

    for r in results_dicts:
        report += (f"| {r['t']} | {r['coherence']:.3f} | {r['novelty']:.3f} | {r['constraint']:.3f} | "
                   f"{r['RI']:.4f} | {r['resonance_pct']:.1f} | {r['Qz']:.4f} | {r['Az']:.4f} | "
                   f"{r['vector_consistency']:.3f} | {r['SMI']:.2f} | {r['imprint_quadrant']} |\n")

    return report
