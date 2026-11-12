"""The fifty-digit reference for Fuaran.Compute.Elementary's special functions (Phase 631, DECISIONS.md D78).

Source: mpmath 1.4.1 (https://mpmath.org, BSD-3-Clause), run under CPython 3 at `mp.dps = 50`. Install it
into a virtual environment (`python -m venv venv`, then `venv/Scripts/python -m pip install mpmath==1.4.1`);
nothing here is installed into the system Python and the environment is never committed.

Three modes, each reading and writing plain text:

  python special_reference.py coefficients
      Prints the two kernels' Chebyshev coefficients (highest degree first) and the split of the digamma
      function's positive root, as `Special.fs` holds them.

  python special_reference.py references < cases.txt
      Each input line is `<id> <op> <hex> [<hex> ...]`, the operands as IEEE-754 binary64 bit patterns in
      sixteen lowercase hex digits, in the function's argument order. Each output line is `<id> <hex>`:
      the true value rounded once to the nearest double. This is how `SpecialVectors.references` was made.
      A case of `inverseRegularizedBeta` or `inverseRegularizedBetaComplement` carries the package's
      answer as one more operand (see below).

  python special_reference.py check < answers.txt
      Each input line is `<op> <hex> [<hex> ...] <hex>`: the operands and then the package's answer (an
      `<op>` of the form `<function>:<label>` is scored under its own heading). Prints,
      per op, the count, the largest and the 99th-percentile distance from the reference in units in the
      last place of the correctly rounded value, the largest over the inputs whose true value lies
      between `1e-10` and `1e10` in magnitude, and the three worst inputs. An input whose true value is
      below `1e-290` in magnitude, or that mpmath cannot take, is counted as unreferenced.

The functions, as mpmath spells them: `gammaLn` is `log(|gamma(x)|)`; `gamma`; `betaLn` and `beta` from
`loggamma`; `regularizedGammaP` and `regularizedGammaQ` are `gammainc(..., regularized=True)` (for `a` of
five hundred and more, the lower series summed here at ninety digits, because mpmath's own summation gives
up); `regularizedBeta` is `betainc(a, b, 0, x, regularized=True)` (for `a + b` of two thousand and more,
Abramowitz and Stegun's continued fraction 26.5.8 evaluated from its tail at eighty digits, because the
hypergeometric route can take minutes a value there);
`regularizedBetaComplement` (Phase 636, DECISIONS.md D81) is the same function of the exchanged
parameters at `1 - x`, that subtraction exact at fifty digits; `polygamma`, with `digamma` its order
zero and `trigamma` its order one. `inverseRegularizedBeta` and `inverseRegularizedBetaComplement` have
no closed reference: the answer `x` is corrected by Newton steps taken at fifty digits, which land
within `1e-25` of the root from any `x` the package answers, and that corrected value is the reference.
"""
import math
import struct
import sys

import mpmath
from mpmath import mp, mpf

mp.dps = 50

GAMMA_LN_KERNEL_DEGREE = 17
DIGAMMA_KERNEL_DEGREE = 23
TINY_RESULT = mpf(10) ** -290


def of_hex(h):
    return struct.unpack(">d", bytes.fromhex(h))[0]


def to_hex(x):
    if x != x:
        return "nan"
    return struct.pack(">d", x).hex()


def digamma_root():
    return mpmath.findroot(mpmath.digamma, mpf("1.4616321449683623"))


def coefficients():
    def gamma_ln_kernel(u):
        if u == 0:
            return mpmath.digamma(2)
        return mpmath.loggamma(2 + u) / (u * (1 + u))

    root = digamma_root()

    def digamma_kernel(c):
        x = mpf(3) / 2 + c
        if abs(x - root) < mpf(10) ** -30:
            return mpmath.polygamma(1, root)
        return mpmath.digamma(x) / (x - root)

    print("ln Gamma(2 + u) / (u (1 + u)) over u in [-1/2, 1/2], degree", GAMMA_LN_KERNEL_DEGREE)
    for c in mpmath.chebyfit(gamma_ln_kernel, [-0.5, 0.5], GAMMA_LN_KERNEL_DEGREE + 1):
        print("   ", repr(float(c)))
    print("digamma(x) / (x - root) over x - 3/2 in [-1/2, 1/2], degree", DIGAMMA_KERNEL_DEGREE)
    for c in mpmath.chebyfit(digamma_kernel, [-0.5, 0.5], DIGAMMA_KERNEL_DEGREE + 1):
        print("   ", repr(float(c)))
    high = float(root)
    print("digamma root, high and low:", repr(high), repr(float(root - mpf(high))))
    print("half ln 2 pi:", repr(float(mpmath.log(2 * mpmath.pi) / 2)))
    print("half ln 2 pi - 1/2:", repr(float(mpmath.log(2 * mpmath.pi) / 2 - mpf(1) / 2)))
    print("sqrt 2 pi:", repr(float(mpmath.sqrt(2 * mpmath.pi))))


def lower_gamma(a, x):
    """P(a, x) at ninety digits by the lower series, for the large `a` mpmath's summation refuses.
    The caller holds `mp.dps` at ninety."""
    big_a, big_x = mpf(a), mpf(x)
    term = mpf(1)
    total = mpf(1)
    n = 0
    while True:
        n += 1
        term = term * big_x / (big_a + n)
        total += term
        if term < total * mpf(10) ** -85:
            break
    return mpmath.exp(big_a * mpmath.log(big_x) - big_x - mpmath.loggamma(big_a + 1)) * total


def gamma_p(a, x):
    if a >= 500:
        mp.dps = 90
        try:
            p = lower_gamma(a, x)
        finally:
            mp.dps = 50
        return +p
    return mpmath.gammainc(mpf(a), 0, mpf(x), regularized=True)


def gamma_q(a, x):
    if a >= 500:
        mp.dps = 90
        try:
            q = 1 - lower_gamma(a, x)
        finally:
            mp.dps = 50
        if q < mpf(10) ** -40:
            # the complement of a ninety-digit `P` has no digits left this far down: mpmath's own
            # upper function, which raises (and the input is counted unreferenced) where it gives up
            return mpmath.gammainc(mpf(a), mpf(x), mpmath.inf, regularized=True)
        return +q
    return mpmath.gammainc(mpf(a), mpf(x), mpmath.inf, regularized=True)


def beta_ln(a, b):
    return mpmath.loggamma(mpf(a)) + mpmath.loggamma(mpf(b)) - mpmath.loggamma(mpf(a) + mpf(b))


def beta_fraction(a, b, x):
    """`I_x(a, b)` for `x` at or below `(a + 1) / (a + b + 2)`: Abramowitz and Stegun 26.5.8 evaluated
    from its tail at eighty digits, the count of partial quotients doubled until two evaluations agree
    to `1e-45` of the value."""
    with mp.workdps(mp.dps + 30):
        a, b, x = mpf(a), mpf(b), mpf(x)
        prefix = mpmath.exp(a * mpmath.log(x) + b * mpmath.log1p(-x) - beta_ln(a, b)) / a

        def evaluate(count):
            tail = mpf(0)
            for m in range(count, 0, -1):
                even = m * (b - m) * x / ((a + 2 * m - 1) * (a + 2 * m))
                odd = -(a + m) * (a + b + m) * x / ((a + 2 * m) * (a + 2 * m + 1))
                tail = even / (1 + odd / (1 + tail))
            return prefix / (1 - (a + b) * x / (a + 1) / (1 + tail))

        count = 64
        value = evaluate(count)
        while True:
            count *= 2
            again = evaluate(count)
            if abs(again - value) <= abs(again) * mpf(10) ** -45:
                return +again
            if count > 1 << 22:
                raise ValueError("the reference fraction did not settle")
            value = again


def regularized_beta(a, b, x):
    a, b, x = mpf(a), mpf(b), mpf(x)
    if a + b < 2000:
        return mpmath.betainc(a, b, 0, x, regularized=True)
    # Beyond that mpmath's hypergeometric route can take minutes a value. The continued fraction is
    # evaluated instead, on its own side of the switch. (Until Phase 636 this was, for both parameters
    # at least ten, a quadrature of the density cut at the standard deviation's scale, which is out by
    # up to `1e-4` of the value some twenty standard deviations into a tail, and `betainc` for a
    # parameter below ten. The fraction agrees with `betainc` to forty digits and more wherever both
    # were run: Phase 636's sample of the corner, and every vector's reference re-derives unchanged.)
    if x <= (a + 1) / (a + b + 2):
        return beta_fraction(a, b, x)
    return 1 - beta_fraction(b, a, 1 - x)


def inverse_reference(a, b, p, answer):
    """The root nearest the package's answer: Newton steps from it at fifty digits."""
    x = mpf(answer)
    for _ in range(3):
        density = mpmath.exp((mpf(a) - 1) * mpmath.log(x) + (mpf(b) - 1) * mpmath.log(1 - x) - beta_ln(a, b))
        x = x - (regularized_beta(a, b, x) - mpf(p)) / density
    return x


def regularized_beta_complement(a, b, x):
    """`1 - I_x(a, b)`: `I_(1 - x)(b, a)` with the subtraction taken at fifty digits, where it is exact
    (an `x` of `1e-30` and more); below that, one minus the function, which is not near one there
    unless `a` is far below anything sampled."""
    x = mpf(x)
    if x >= mpf(10) ** -30:
        return regularized_beta(b, a, 1 - x)
    return 1 - regularized_beta(a, b, x)


def inverse_complement_reference(a, b, q, answer):
    """The root of `1 - I_x(a, b) = q` nearest the package's answer, by the same Newton steps. The
    residual is read on the side that does not cancel: `1 - I_x - q` for a `q` below one half, and
    `I_x - (1 - q)` otherwise, `1 - q` being exact at fifty digits."""
    x = mpf(answer)
    for _ in range(3):
        density = mpmath.exp((mpf(a) - 1) * mpmath.log(x) + (mpf(b) - 1) * mpmath.log(1 - x) - beta_ln(a, b))
        if q < 0.5:
            x = x + (regularized_beta_complement(a, b, x) - mpf(q)) / density
        else:
            x = x - (regularized_beta(a, b, x) - (1 - mpf(q))) / density
    return x


REFERENCE = {
    "gammaLn": lambda x: mpmath.log(abs(mpmath.gamma(mpf(x)))) if x < 0 else mpmath.loggamma(mpf(x)),
    "gamma": lambda x: mpmath.gamma(mpf(x)),
    "betaLn": beta_ln,
    "beta": lambda a, b: mpmath.exp(beta_ln(a, b)),
    "regularizedGammaP": gamma_p,
    "regularizedGammaQ": gamma_q,
    "regularizedBeta": regularized_beta,
    "regularizedBetaComplement": regularized_beta_complement,
    "polygamma": lambda n, x: mpmath.polygamma(int(n), mpf(x)),
    "digamma": lambda x: mpmath.digamma(mpf(x)),
    "trigamma": lambda x: mpmath.polygamma(1, mpf(x)),
}


def reference(op, operands, answer=None):
    if op == "inverseRegularizedBeta":
        if answer is None or not (0.0 < answer < 1.0):
            raise ValueError("the inverse's reference is taken from the package's answer inside (0, 1)")
        return inverse_reference(*operands, answer)
    if op == "inverseRegularizedBetaComplement":
        if answer is None or not (0.0 < answer < 1.0):
            raise ValueError("the inverse's reference is taken from the package's answer inside (0, 1)")
        return inverse_complement_reference(*operands, answer)
    return REFERENCE[op](*operands)


def ulps(got, want):
    """The distance of the double `got` from the true value `want`, in ulps of `want` rounded."""
    rounded = float(want)
    if got != got or math.isinf(got) or rounded == 0.0 or math.isinf(rounded):
        return 0.0 if got == rounded else math.inf
    return float(abs(mpf(got) - want) / mpf(math.ulp(rounded)))


def references():
    for line in sys.stdin:
        parts = line.split()
        if not parts:
            continue
        identity, op, operands = parts[0], parts[1], [of_hex(h) for h in parts[2:]]
        if op in ("inverseRegularizedBeta", "inverseRegularizedBetaComplement"):
            want = reference(op, operands[:-1], operands[-1])
        else:
            want = reference(op, operands)
        print(identity, to_hex(float(want)))


def check():
    by_op = {}
    for line in sys.stdin:
        parts = line.split()
        if not parts:
            continue
        # `<function>:<label>` scores the function under its own heading (the reflections, say).
        op, values = parts[0], [of_hex(h) if h != "nan" else math.nan for h in parts[1:]]
        operands, answer = values[:-1], values[-1]
        try:
            want = reference(op.split(":")[0], operands, answer)
        except Exception as error:  # a reference mpmath cannot take is counted, never dropped quietly
            by_op.setdefault(op, []).append((math.nan, operands, str(error)))
            continue
        if want != 0 and abs(want) < TINY_RESULT:
            # a result this small is rounded on the subnormal grid, where a unit in the last place is
            # not a relative measure; counted with the unreferenced
            by_op.setdefault(op, []).append((math.nan, operands, "below 1e-290"))
            continue
        moderate = mpf(10) ** -10 <= abs(want) <= mpf(10) ** 10
        by_op.setdefault(op, []).append((ulps(answer, want), operands, "moderate" if moderate else ""))
    for op, rows in by_op.items():
        unreferenced = [r for r in rows if r[0] != r[0]]
        scored = sorted((r for r in rows if r[0] == r[0]), key=lambda r: -r[0])
        if not scored:
            print(f"{op}: no reference for any of {len(rows)} inputs")
            continue
        finite = [r[0] for r in scored if r[0] != math.inf]
        moderate = [r[0] for r in scored if r[2] == "moderate"]
        print(
            f"{op}: n={len(scored)} max={scored[0][0]:.4g} p99={scored[len(scored) // 100][0]:.4g} "
            f"mean={sum(finite) / max(1, len(finite)):.4g} "
            f"max-where-1e-10<=|value|<=1e10={max(moderate, default=0.0):.4g} (n={len(moderate)}) "
            f"unreferenced={len(unreferenced)} "
            f"worst={[tuple(r[1]) for r in scored[:3]]}"
        )


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    if mode == "coefficients":
        coefficients()
    elif mode == "references":
        references()
    elif mode == "check":
        check()
    else:
        sys.exit(__doc__)
