namespace Fuaran.Compute.LinearAlgebra

/// The rank test of D49 item 11: read by `QR.pivoted` here and by the least-squares solve of
/// Fuaran.Compute.Regression (its own file since Phase 632, DECISIONS.md D75).
module internal Rank =

    /// `2⁻⁵²`.
    let epsilon: float = 1.0 / 4503599627370496.0

    /// `τ = max(n, p) · 2⁻⁵²`, computed as `float (max n p) * 2⁻⁵²` (D49 item 11).
    let tolerance (n: int) (p: int) : float = float (max n p) * epsilon

    /// Is the column whose diagonal entry is `d` and whose entries on and above the diagonal are
    /// `col[co .. co + len − 1]` negligible: `|d| ≤ τ · norm2 col`, the product taken first.
    let negligible (tau: float) (d: float) (col: float[]) (co: int) (len: int) : bool =
        let bound = tau * Kernels.norm2 col co len
        abs d <= bound
