"""Phase 598 reference computation (DECISIONS.md D57).

An independent, high-precision computation of the generalised-linear-model families and the
heteroskedasticity-consistent ("sandwich") standard errors the Fit verb states, written against the
textbook definitions rather than against the library: Python's standard `decimal` module at 60
significant digits, the normal equations solved by Gauss-Jordan elimination (the library uses a
streaming Givens QR), and iteratively reweighted least squares run to a step below 1e-45, so the
answer is the maximum-likelihood estimate itself rather than a point the library's 1e-8 stopping rule
reaches. The library's answers are held to these within a relative 1e-6 (FitTests.fs).

The sandwich is R's `sandwich::vcovHC` written out: bread (X'WX)^-1, scores w_i * r_i * x_i in the
working weights and working residuals, leverage h_i = w_i * x_i' (X'WX)^-1 x_i, HC1's n / (n - rank),
HC2's 1 / (1 - h_i) and HC3's 1 / (1 - h_i)^2 on each row's squared score. n counts the rows of
positive weight (D49 item 13; R's own HC1 counts every row of the model frame, which only differs
where a prior weight is zero).

Run:   python reference.py > reference.json   (no third-party package; Python 3.8 or later)
"""

import json
from decimal import Decimal as D, getcontext

getcontext().prec = 60
ZERO, ONE, TWO = D(0), D(1), D(2)


def solve(a, b):
    """Gauss-Jordan with partial pivoting over Decimal; returns (a^-1 b, a^-1)."""
    n = len(a)
    m = [row[:] + [b[i]] + [ONE if i == j else ZERO for j in range(n)] for i, row in enumerate(a)]
    for c in range(n):
        piv = max(range(c, n), key=lambda r: abs(m[r][c]))
        m[c], m[piv] = m[piv], m[c]
        pv = m[c][c]
        m[c] = [v / pv for v in m[c]]
        for r in range(n):
            if r != c and m[r][c] != 0:
                f = m[r][c]
                m[r] = [vr - f * vc for vr, vc in zip(m[r], m[c])]
    return [m[i][n] for i in range(n)], [m[i][n + 1:] for i in range(n)]


def wls(x, z, w):
    p = len(x[0])
    xtwx = [[sum(w[i] * x[i][j] * x[i][k] for i in range(len(x))) for k in range(p)] for j in range(p)]
    xtwz = [sum(w[i] * x[i][j] * z[i] for i in range(len(x))) for j in range(p)]
    return solve(xtwx, xtwz)


def variance(power, mu):
    return mu ** power if power is not None else ONE


def tweedie_unit_deviance(p, y, mu):
    if p == 1:
        return TWO * ((y * (y / mu).ln() if y != 0 else ZERO) - (y - mu))
    if p == 2:
        return TWO * (-(y / mu).ln() + (y - mu) / mu)
    y1 = y if y != 0 else ONE
    a, b = 1 - p, 2 - p
    theta = (y1 ** a - mu ** a) / a
    kappa = ((y ** b if y != 0 else ZERO) - mu ** b) / b
    return TWO * (y * theta - kappa)


def glm_log(x, y, w, power):
    """Log-link IRLS for V(mu) = mu^power; returns the estimate and the working quantities at it."""
    n, p = len(x), len(x[0])
    mu = [v if v != 0 else D("0.1") for v in y]
    eta = [m.ln() for m in mu]
    beta = None
    for _ in range(500):
        ww = [w[i] * mu[i] ** (2 - power) for i in range(n)]
        z = [eta[i] + (y[i] - mu[i]) / mu[i] for i in range(n)]
        new, inv = wls(x, z, ww)
        eta = [sum(x[i][j] * new[j] for j in range(p)) for i in range(n)]
        mu = [e.exp() for e in eta]
        if beta is not None and max(abs(a - b) for a, b in zip(new, beta)) < D("1e-45"):
            beta = new
            break
        beta = new
    ww = [w[i] * mu[i] ** (2 - power) for i in range(n)]
    _, inv = wls(x, [ZERO] * n, ww)
    resid = [(y[i] - mu[i]) / mu[i] for i in range(n)]
    return beta, mu, ww, resid, inv


def sandwich(x, ww, resid, inv, rank):
    n, p = len(x), len(x[0])
    used = [i for i in range(n) if ww[i] > 0]
    out = {}
    for variant in ("hc0", "hc1", "hc2", "hc3"):
        meat = [[ZERO] * p for _ in range(p)]
        for i in used:
            xi = x[i]
            h = ww[i] * sum(xi[j] * inv[j][k] * xi[k] for j in range(p) for k in range(p))
            s = ww[i] * resid[i]
            s2 = s * s
            if variant == "hc2":
                s2 = s2 / (1 - h)
            elif variant == "hc3":
                s2 = s2 / ((1 - h) * (1 - h))
            for j in range(p):
                for k in range(p):
                    meat[j][k] += s2 * xi[j] * xi[k]
        cov = [[sum(inv[a][j] * meat[j][k] * inv[k][b] for j in range(p) for k in range(p)) for b in range(p)] for a in range(p)]
        scale = D(len(used)) / D(len(used) - rank) if variant == "hc1" else ONE
        out[variant] = [(cov[j][j] * scale).sqrt() for j in range(p)]
    return out


def fmt(v):
    return format(v, ".20e")


def glm_case(name, source, x, y, w, power, family):
    beta, mu, ww, resid, inv = glm_log(x, y, w, power)
    n, p = len(x), len(x[0])
    used = [i for i in range(n) if w[i] > 0]
    df = len(used) - p
    pearson = sum(w[i] * (y[i] - mu[i]) ** 2 / mu[i] ** power for i in used)
    phi = ONE if family == "poisson" else pearson / df
    dev = sum(w[i] * tweedie_unit_deviance(power, y[i], mu[i]) for i in used)
    se = [(phi * inv[j][j]).sqrt() for j in range(p)]
    hc = sandwich(x, ww, resid, inv, p)
    return {
        "name": name,
        "source": source,
        "family": family,
        "power": str(power),
        "coefficients": [fmt(b) for b in beta],
        "dispersion": fmt(phi),
        "deviance": fmt(dev),
        "stdErrors": [fmt(s) for s in se],
        **{k: [fmt(s) for s in v] for k, v in hc.items()},
    }


def ols_case(name, source, x, y, w):
    n, p = len(x), len(x[0])
    beta, inv = wls(x, y, w)
    resid = [y[i] - sum(x[i][j] * beta[j] for j in range(p)) for i in range(n)]
    used = [i for i in range(n) if w[i] > 0]
    phi = sum(w[i] * resid[i] ** 2 for i in used) / (len(used) - p)
    se = [(phi * inv[j][j]).sqrt() for j in range(p)]
    hc = sandwich(x, w, resid, inv, p)
    return {
        "name": name,
        "source": source,
        "family": "gaussian",
        "coefficients": [fmt(b) for b in beta],
        "stdErrors": [fmt(s) for s in se],
        **{k: [fmt(s) for s in v] for k, v in hc.items()},
    }


def dobson():
    counts = [18, 17, 15, 20, 10, 20, 25, 13, 12]
    x = []
    for i in range(9):
        o, t = i % 3 + 1, i // 3 + 1
        x.append([ONE, D(int(o == 2)), D(int(o == 3)), D(int(t == 2)), D(int(t == 3))])
    return x, [D(c) for c in counts], [ONE] * 9


def zeros():
    ys = ["0", "1.2", "0", "3.4", "2.2", "0", "5.1", "4.4", "0", "7.9", "6.3", "9.0"]
    x = [[ONE, D(i + 1)] for i in range(12)]
    return x, [D(v) for v in ys], [ONE] * 12


def fit_table(weighted):
    # FitTests' heteroskedastic table: x = i + 1, y = 2 + x/2 + ((37 i mod 11) - 5)/10 * (1 + i/3).
    x, y, w = [], [], []
    for i in range(10):
        xi = D(i + 1)
        x.append([ONE, xi])
        y.append(D(2) + xi / 2 + D((37 * i) % 11 - 5) / 10 * (1 + D(i) / 3))
        w.append((D(0) if i == 6 else ONE + D(i % 4) / 2) if weighted else ONE)
    return x, y, w


def main():
    dx, dy, dw = dobson()
    zx, zy, zw = zeros()
    cases = [
        glm_case("dobson-poisson", "Dobson (1990), R ?glm: counts ~ outcome + treatment", dx, dy, dw, 1, "poisson"),
        glm_case("dobson-quasipoisson", "the same, family quasi-Poisson", dx, dy, dw, 1, "quasiPoisson"),
        glm_case("dobson-tweedie-1.5", "the same, Tweedie p = 1.5, log link", dx, dy, dw, D("1.5"), "tweedie"),
        glm_case("zeros-tweedie-1.5", "twelve rows with four zeros, y ~ x, Tweedie p = 1.5", zx, zy, zw, D("1.5"), "tweedie"),
        glm_case("zeros-tweedie-1.3", "the same, Tweedie p = 1.3", zx, zy, zw, D("1.3"), "tweedie"),
        ols_case("hetero-ols", "y ~ x over ten heteroskedastic rows", *fit_table(False)),
        ols_case("hetero-wls", "the same with prior weights, one of them zero", *fit_table(True)),
    ]
    print(json.dumps({"phase": "598", "precision": 60, "cases": cases}, indent=2))


if __name__ == "__main__":
    main()
