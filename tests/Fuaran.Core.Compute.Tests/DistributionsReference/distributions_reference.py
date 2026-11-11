"""The fifty-digit reference for Fuaran.Compute.Distributions (Phase 618, DECISIONS.md D79).

Source: mpmath 1.4.1 (https://mpmath.org, BSD-3-Clause), run under CPython 3 at `mp.dps = 50`. Install it
into a virtual environment (`python -m venv venv`, then `venv/Scripts/python -m pip install mpmath==1.4.1`);
nothing here is installed into the system Python and the environment is never committed. The incomplete
gamma and beta references are `../SpecialReference/special_reference.py`'s (Phase 631), imported, so the
two packages are measured against one reference.

Two modes, each reading and writing plain text:

  python distributions_reference.py references < cases.txt
      Each input line is `<id> <op> <hex> [<hex> ...]`, the operands as IEEE-754 binary64 bit patterns in
      sixteen lowercase hex digits, in the function's argument order; a quantile carries the package's
      answer as one more operand. Each output line is `<id> <hex>`: the true value rounded once to the
      nearest double. This is how `DistributionsVectors.references` was made.

  python distributions_reference.py check < answers.txt
      Each input line is `<op> <hex> [<hex> ...] <hex>`: the operands and then the package's answer (an
      `<op>` of the form `<function>:<candidate>` or `<function>@<range>`, or both, is scored under its
      own heading). Prints, per op, the
      count, the largest and the 99th-percentile distance from the reference in units in the last place
      of the correctly rounded value, the mean, and the three worst inputs. An input whose true value is
      below `1e-290` in magnitude, or that mpmath cannot take, is counted as unreferenced.

The functions:

  `Normal.cdf` is `ncdf`, and `Normal.quantile` the root of it nearest the answer.
  `StudentT.cdf nu x`: the tail beyond `|x|` is `I_z(nu/2, 1/2) / 2` at `z = nu / (nu + x^2)` formed at fifty
      digits, by `betainc` at every `nu` (with one parameter one half its hypergeometric route is quick and
      agrees with itself at seventy digits; integrating the density instead is five parts in `1e12` out
      in a deep tail). The lower tail is the tail and the upper its complement.
  `ChiSquare.cdf k x` is `P(k/2, x/2)`.
  `FisherF.cdf d1 d2 x` is `I_w(d1/2, d2/2)` at `w = d1 x / (d1 x + d2)` formed at fifty digits (see
      `beta_lower` for the one place it does not take the special functions' reference as it is).
  A quantile has no closed reference: the package's answer is corrected by Newton steps on the
      distribution function taken at fifty digits, which land within `1e-25` of the root from any answer
      the package gives, and that corrected value is the reference (the root nearest the answer).
"""
import math
import os
import sys

import mpmath
from mpmath import mp, mpf

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SpecialReference"))
from special_reference import beta_ln, gamma_p, gamma_q, of_hex, regularized_beta, to_hex, ulps  # noqa: E402

mp.dps = 50

TINY_RESULT = mpf(10) ** -290
HALF = mpf(1) / 2
TAIL = mpf(10) ** -9


def t_density(nu, t):
    nu, t = mpf(nu), mpf(t)
    return mpmath.exp(-(nu + 1) / 2 * mpmath.log1p(t * t / nu) - mpmath.log(nu) / 2 - beta_ln(nu / 2, HALF))


def t_tail(nu, t):
    """P(T >= t) for t >= 0, to the tail's own last places."""
    nu, t = mpf(nu), mpf(t)
    if t == 0:
        return HALF
    return mpmath.betainc(nu / 2, HALF, 0, nu / (nu + t * t), regularized=True) / 2


def t_cdf(nu, x):
    return t_tail(nu, -mpf(x)) if x < 0 else 1 - t_tail(nu, x)


def chi_cdf(k, x):
    return gamma_p(mpf(k) / 2, mpf(x) / 2)


def chi_density(k, x):
    a, x = mpf(k) / 2, mpf(x)
    return mpmath.exp((a - 1) * mpmath.log(x / 2) - x / 2 - mpmath.loggamma(a)) / 2


def beta_lower(a, b, w):
    """I_w(a, b). The special functions' reference, except in a tail where both parameters are large:
    it integrates the density there, which is accurate about the mean and parts in a million out
    forty decades down, where the hypergeometric route it avoids is quick."""
    value = regularized_beta(a, b, w)
    if a + b < 2000 or a < 10 or b < 10:
        return value
    if value < TAIL:
        return mpmath.betainc(a, b, 0, w, regularized=True)
    if 1 - value < TAIL:
        return 1 - mpmath.betainc(b, a, 0, 1 - w, regularized=True)
    return value


def f_cdf(d1, d2, x):
    d1, d2, x = mpf(d1), mpf(d2), mpf(x)
    return beta_lower(d1 / 2, d2 / 2, d1 * x / (d1 * x + d2))


def f_density(d1, d2, x):
    d1, d2, x = mpf(d1), mpf(d2), mpf(x)
    a, b = d1 / 2, d2 / 2
    return mpmath.exp(
        a * mpmath.log(d1 / d2) + (a - 1) * mpmath.log(x) - (a + b) * mpmath.log1p(d1 * x / d2) - beta_ln(a, b)
    )


def newton(distribution, density, p, answer, upper=None):
    """The root of `distribution x = p` nearest `answer`. `upper x`, where given, is `1 - distribution x`
    computed directly, and is what the residual reads above one half."""
    x, p = mpf(answer), mpf(p)
    for _ in range(4):
        if upper is not None and p > HALF:
            residual = (1 - p) - upper(x)
        else:
            residual = distribution(x) - p
        x = x - residual / density(x)
    return x


def t_quantile(nu, p, answer):
    # the lower tail is the function computed directly: above one half, the mirrored root
    if answer < 0:
        return newton(lambda x: t_tail(nu, -x), lambda x: t_density(nu, x), p, answer)
    return -newton(lambda x: t_tail(nu, -x), lambda x: t_density(nu, x), 1 - mpf(p), -answer)


def chi_quantile(k, p, answer):
    return newton(
        lambda x: chi_cdf(k, x), lambda x: chi_density(k, x), p, answer, lambda x: gamma_q(mpf(k) / 2, x / 2)
    )


def f_quantile(d1, d2, p, answer):
    # above one half, the root of the mirrored distribution at the reciprocal
    if p > 0.5:
        return 1 / newton(lambda x: f_cdf(d2, d1, x), lambda x: f_density(d2, d1, x), 1 - mpf(p), 1 / mpf(answer))
    return newton(lambda x: f_cdf(d1, d2, x), lambda x: f_density(d1, d2, x), p, answer)


def normal_quantile(p, answer):
    return newton(mpmath.ncdf, mpmath.npdf, p, answer, lambda x: mpmath.ncdf(-x))


CDF = {
    "Normal.cdf": lambda x: mpmath.ncdf(mpf(x)),
    "StudentT.cdf": t_cdf,
    "ChiSquare.cdf": chi_cdf,
    "FisherF.cdf": f_cdf,
}

QUANTILE = {
    "Normal.quantile": normal_quantile,
    "StudentT.quantile": t_quantile,
    "ChiSquare.quantile": chi_quantile,
    "FisherF.quantile": f_quantile,
}


def reference(op, operands, answer):
    if op in CDF:
        return CDF[op](*operands)
    if answer != answer or math.isinf(answer) or answer == 0.0:
        raise ValueError("a quantile's reference is taken from a finite, non-zero answer")
    return QUANTILE[op](*operands, answer)


def references():
    for line in sys.stdin:
        parts = line.split()
        if not parts:
            continue
        identity, op, values = parts[0], parts[1], [of_hex(h) for h in parts[2:]]
        if op in CDF:
            want = reference(op, values, None)
        else:
            want = reference(op, values[:-1], values[-1])
        print(identity, to_hex(float(want)))


def check():
    by_op = {}
    for line in sys.stdin:
        parts = line.split()
        if not parts:
            continue
        # `<function>:<label>` scores the function under its own heading (a range, or a candidate).
        op, values = parts[0], [of_hex(h) if h != "nan" else math.nan for h in parts[1:]]
        operands, answer = values[:-1], values[-1]
        try:
            want = reference(op.split("@")[0].split(":")[0], operands, answer)
        except Exception as error:  # a reference mpmath cannot take is counted, never dropped quietly
            by_op.setdefault(op, []).append((math.nan, operands, str(error)))
            continue
        if want != 0 and abs(want) < TINY_RESULT:
            # rounded on the subnormal grid, where a unit in the last place is not a relative measure
            by_op.setdefault(op, []).append((math.nan, operands, "below 1e-290"))
            continue
        by_op.setdefault(op, []).append((ulps(answer, want), operands, ""))
    for op, rows in by_op.items():
        unreferenced = [r for r in rows if r[0] != r[0]]
        scored = sorted((r for r in rows if r[0] == r[0]), key=lambda r: -r[0])
        if not scored:
            print(f"{op}: no reference for any of {len(rows)} inputs ({unreferenced[0][2]})")
            continue
        finite = [r[0] for r in scored if r[0] != math.inf]
        print(
            f"{op}: n={len(scored)} max={scored[0][0]:.4g} p99={scored[len(scored) // 100][0]:.4g} "
            f"mean={sum(finite) / max(1, len(finite)):.4g} unreferenced={len(unreferenced)} "
            f"worst={[tuple(r[1]) for r in scored[:3]]}"
        )


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    if mode == "references":
        references()
    elif mode == "check":
        check()
    else:
        sys.exit(__doc__)
