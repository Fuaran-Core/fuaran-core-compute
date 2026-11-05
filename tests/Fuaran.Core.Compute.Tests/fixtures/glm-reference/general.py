"""Phase 600 reference computation (DECISIONS.md D58).

An independent, high-precision computation of the general generalised linear model the Fit verb
states -- the binomial, quasi-binomial, inverse Gaussian and negative binomial families, and the logit,
probit, complementary log-log, inverse, inverse-square and square-root links beside identity and log --
and of the pinned special functions behind them (erfc, the normal distribution function, its density
and its quantile). Written against the textbook definitions rather than against the library: Python's
standard `decimal` module at 80 significant digits; erfc by its Maclaurin series below 3 and by its
continued fraction above; pi by Machin's formula; the normal equations solved by Gauss-Jordan
elimination (the library uses a streaming Givens QR); iteratively reweighted least squares run until a
step is below 1e-50, so the answer is the maximum-likelihood estimate itself rather than a point the
library's 1e-8 stopping rule reaches; and the negative binomial's theta by Newton's method on its exact
score, the digamma and trigamma differences written as the finite sums they are at a whole count,
alternated with the coefficients until both have stopped moving.

Every input is the double the library reads: each value is written as Python's shortest repr of a
double and read here as that double's exact decimal value.

The library's answers are held to these within a relative 1e-6 (FitGeneralTests.fs); the special
functions' values are held in units in the last place, the bound DECISIONS.md D58 states.

Run:   python general.py > general.json   (no third-party package; Python 3.8 or later)
"""

import json
import math
from decimal import Decimal as D, getcontext

getcontext().prec = 80
ZERO, ONE, TWO, HALF = D(0), D(1), D(2), D("0.5")


def exact(v):
    """The exact decimal value of the double nearest `v`."""
    return D(float(v))


def machin_pi():
    def arctan_inv(n):
        x = ONE / n
        x2 = x * x
        term, total, k = x, x, 1
        while True:
            term = -term * x2
            add = term / (2 * k + 1)
            if abs(add) < D(10) ** -90:
                return total
            total += add
            k += 1

    return 16 * arctan_inv(5) - 4 * arctan_inv(239)


PI = machin_pi()
SQRT_PI = PI.sqrt()
SQRT_2 = TWO.sqrt()
SQRT_2PI = (TWO * PI).sqrt()


def erfc(x):
    if x < 0:
        return TWO - erfc(-x)
    if x < 3:
        x2 = x * x
        term, total, n = x, x, 0
        while True:
            n += 1
            term = -term * x2 / n
            add = term / (2 * n + 1)
            total += add
            if abs(add) < D(10) ** -85:
                break
        return ONE - TWO / SQRT_PI * total
    # erfc x = exp(-x^2)/sqrt(pi) * 1/(x + (1/2)/(x + 1/(x + (3/2)/(x + ...)))), evaluated backwards.
    f = x
    for k in range(4000, 0, -1):
        f = x + (D(k) / 2) / f
    return (-x * x).exp() / SQRT_PI / f


def phi_cdf(x):
    return erfc(-x / SQRT_2) / 2


def phi_density(x):
    return (-x * x / 2).exp() / SQRT_2PI


def phi_quantile(p):
    """Newton's method on the distribution function from the standard library's double estimate."""
    from statistics import NormalDist

    x = exact(NormalDist().inv_cdf(float(p)))
    for _ in range(12):
        x = x - (phi_cdf(x) - p) / phi_density(x)
    return x


# ---- links: (inverse link, d mu / d eta, link) -----------------------------------------------------


def linkinv(link, eta):
    if link == "identity":
        return eta
    if link == "log":
        return eta.exp()
    if link == "logit":
        return ONE / (ONE + (-eta).exp())
    if link == "probit":
        return phi_cdf(eta)
    if link == "cloglog":
        return ONE - (-(eta.exp())).exp()
    if link == "inverse":
        return ONE / eta
    if link == "inverseSquare":
        return ONE / eta.sqrt()
    if link == "sqrt":
        return eta * eta
    raise ValueError(link)


def mu_eta(link, eta):
    if link == "identity":
        return ONE
    if link == "log":
        return eta.exp()
    if link == "logit":
        m = linkinv(link, eta)
        return m * (ONE - m)
    if link == "probit":
        return phi_density(eta)
    if link == "cloglog":
        return (eta - eta.exp()).exp()
    if link == "inverse":
        return -ONE / (eta * eta)
    if link == "inverseSquare":
        return -ONE / (2 * eta * eta.sqrt())
    if link == "sqrt":
        return 2 * eta
    raise ValueError(link)


def link_fn(link, mu):
    if link == "identity":
        return mu
    if link == "log":
        return mu.ln()
    if link == "logit":
        return (mu / (ONE - mu)).ln()
    if link == "probit":
        return phi_quantile(mu)
    if link == "cloglog":
        return (-((ONE - mu).ln())).ln()
    if link == "inverse":
        return ONE / mu
    if link == "inverseSquare":
        return ONE / (mu * mu)
    if link == "sqrt":
        return mu.sqrt()
    raise ValueError(link)


# ---- families --------------------------------------------------------------------------------------


def variance(family, mu, theta):
    if family == "gaussian":
        return ONE
    if family in ("poisson", "quasiPoisson"):
        return mu
    if family == "gamma":
        return mu * mu
    if family in ("binomial", "quasiBinomial"):
        return mu * (ONE - mu)
    if family == "inverseGaussian":
        return mu * mu * mu
    if family == "negativeBinomial":
        return mu + mu * mu / theta
    raise ValueError(family)


def ylogy(y, mu):
    return ZERO if y == 0 else y * (y / mu).ln()


def unit_deviance(family, y, mu, theta):
    if family == "gaussian":
        return (y - mu) ** 2
    if family in ("poisson", "quasiPoisson"):
        return TWO * (ylogy(y, mu) - (y - mu))
    if family == "gamma":
        return TWO * (-((y / mu).ln()) + (y - mu) / mu)
    if family in ("binomial", "quasiBinomial"):
        other = ZERO if y == 1 else (ONE - y) * ((ONE - y) / (ONE - mu)).ln()
        return TWO * (ylogy(y, mu) + other)
    if family == "inverseGaussian":
        return (y - mu) ** 2 / (y * mu * mu)
    if family == "negativeBinomial":
        return TWO * (ylogy(y, mu) - (y + theta) * ((y + theta) / (mu + theta)).ln())
    raise ValueError(family)


def start(family, y, w):
    if family in ("poisson", "quasiPoisson"):
        return [v + D("0.1") for v in y]
    if family in ("binomial", "quasiBinomial"):
        return [(w[i] * y[i] + HALF) / (w[i] + ONE) for i in range(len(y))]
    if family == "negativeBinomial":
        return [v if v != 0 else ONE / 6 for v in y]
    return list(y)


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


def irls(family, link, x, y, w, theta, eta=None):
    n, p = len(x), len(x[0])
    if eta is None:
        mu = start(family, y, w)
        eta = [link_fn(link, m) for m in mu]
    mu = [linkinv(link, e) for e in eta]
    beta = None
    for _ in range(2000):
        dm = [mu_eta(link, e) for e in eta]
        z = [eta[i] + (y[i] - mu[i]) / dm[i] for i in range(n)]
        ww = [w[i] * dm[i] * dm[i] / variance(family, mu[i], theta) for i in range(n)]
        new, inv = wls(x, z, ww)
        eta = [sum(x[i][j] * new[j] for j in range(p)) for i in range(n)]
        mu = [linkinv(link, e) for e in eta]
        if beta is not None and max(abs(a - b) for a, b in zip(new, beta)) < D("1e-50"):
            beta = new
            break
        beta = new
    else:
        raise RuntimeError("no convergence: %s %s" % (family, link))
    dm = [mu_eta(link, e) for e in eta]
    ww = [w[i] * dm[i] * dm[i] / variance(family, mu[i], theta) for i in range(n)]
    _, inv = wls(x, [ZERO] * n, ww)
    return beta, inv, mu, eta


def theta_score_info(y, w, mu, th):
    score, info = ZERO, ZERO
    for i in range(len(y)):
        if w[i] == 0:
            continue
        yi = int(y[i])
        s1 = sum((ONE / (th + k) for k in range(yi)), ZERO)
        s2 = sum((ONE / ((th + k) * (th + k)) for k in range(yi)), ZERO)
        mt = mu[i] + th
        score += w[i] * (s1 + th.ln() + ONE - mt.ln() - (y[i] + th) / mt)
        info += w[i] * (s2 - ONE / th + TWO / mt - (y[i] + th) / (mt * mt))
    return score, info


def theta_ml(y, w, mu, th):
    for _ in range(500):
        s, i = theta_score_info(y, w, mu, th)
        step = s / i
        th = th + step
        if abs(step) < D("1e-50"):
            return th, theta_score_info(y, w, mu, th)[1]
    raise RuntimeError("theta")


def fit(case):
    family, link = case["family"], case["link"]
    x = [[exact(v) for v in row] for row in case["x"]]
    y = [exact(v) for v in case["y"]]
    w = [exact(v) for v in case["weights"]]
    n, p = len(x), len(x[0])
    theta = exact(case["theta"]) if case.get("theta") is not None else None
    estimated = family == "negativeBinomial" and theta is None
    se_theta = None
    if estimated:
        beta, inv, mu, eta = irls("poisson", link, x, y, w, None)
        th = sum(w) / sum(w[i] * (y[i] / mu[i] - 1) ** 2 for i in range(n))
        th, _ = theta_ml(y, w, mu, th)
        for _ in range(500):
            beta, inv, mu, eta = irls(family, link, x, y, w, th, eta)
            th_new, info = theta_ml(y, w, mu, th)
            moved = abs(th_new - th)
            th = th_new
            if moved < D("1e-45"):
                beta, inv, mu, eta = irls(family, link, x, y, w, th, eta)
                break
        theta = th
        _, info = theta_score_info(y, w, mu, th)
        se_theta = (ONE / info).sqrt()
    else:
        beta, inv, mu, eta = irls(family, link, x, y, w, theta)
    dev = sum((w[i] * unit_deviance(family, y[i], mu[i], theta) for i in range(n) if w[i] > 0), ZERO)
    df = sum(1 for v in w if v > 0) - p
    if family in ("binomial", "poisson", "negativeBinomial"):
        phi = ONE
    elif family == "gaussian":
        phi = dev / df
    else:
        phi = sum((w[i] * (y[i] - mu[i]) ** 2 / variance(family, mu[i], theta) for i in range(n) if w[i] > 0), ZERO) / df
    se = [(phi * inv[j][j]).sqrt() for j in range(p)]
    out = dict(case)
    out["coefficients"] = ["%.30e" % b for b in beta]
    out["stdErrors"] = ["%.30e" % s for s in se]
    out["deviance"] = "%.30e" % dev
    out["dispersion"] = "%.30e" % phi
    if estimated:
        out["thetaEstimate"] = "%.30e" % theta
        out["thetaStdError"] = "%.30e" % se_theta
    return out


def r(v):
    return repr(float(v))


# ---- the fixtures ----------------------------------------------------------------------------------

# Bliss (1935): beetles killed by carbon disulphide, as in McCullagh & Nelder (1989) and R's examples.
DOSE = [1.6907, 1.7242, 1.7552, 1.7842, 1.8113, 1.8369, 1.8610, 1.8839]
KILLED = [6, 13, 18, 28, 52, 53, 61, 60]
TRIALS = [59, 60, 62, 56, 63, 59, 62, 60]
BEETLE_X = [[1.0, d] for d in DOSE]
BEETLE_Y = [r(k / t) for k, t in zip(KILLED, TRIALS)]
BEETLE_W = [float(t) for t in TRIALS]

# A 0/1 outcome over two covariates, its classes overlapping.
BIN_Y = [0, 0, 1, 0, 0, 0, 1, 0, 1, 0, 0, 1, 1, 0, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1]
BIN_X = [[1.0, r(-2.3 + 0.2 * i), r(((i * 5) % 7) / 3.0 - 1.0)] for i in range(24)]

# Dobson (1990): counts ~ outcome + treatment, as in R's ?glm.
COUNTS = [18, 17, 15, 20, 10, 20, 25, 13, 12]
DOBSON_X = [[1.0, 1.0 if i % 3 == 1 else 0.0, 1.0 if i % 3 == 2 else 0.0] for i in range(9)]

# McCullagh & Nelder (1989), clotting times of blood (lot 1) against log(u), as in R's ?glm.
U = [5, 10, 15, 20, 30, 40, 60, 80, 100]
LOT1 = [118, 58, 42, 35, 27, 25, 21, 19, 18]
CLOT_X = [[1.0, r(math.log(u))] for u in U]

# Overdispersed counts against one covariate.
NB_Y = [0, 1, 0, 2, 1, 0, 3, 1, 2, 5, 0, 2, 4, 1, 6, 3, 2, 8, 4, 1, 7, 5, 3, 10, 6, 2, 12, 4, 9, 15]
NB_X = [[1.0, r(i / 10.0)] for i in range(30)]
# The same counts with one added to each, so the identity link's mean is positive along the whole design.
NB_Y1 = [v + 1 for v in NB_Y]


def case(name, family, link, x, y, w, theta=None, source=""):
    return {
        "name": name,
        "source": source,
        "family": family,
        "link": link,
        "theta": theta,
        "x": [[r(v) for v in row] for row in x],
        "y": [r(v) for v in y],
        "weights": [r(v) for v in w],
    }


ones = lambda n: [1.0] * n
BEETLE = "Bliss (1935) beetle mortality, R ?glm / McCullagh & Nelder 1989"
CASES = [
    case("beetle-logit", "binomial", "logit", BEETLE_X, BEETLE_Y, BEETLE_W, source=BEETLE),
    case("beetle-probit", "binomial", "probit", BEETLE_X, BEETLE_Y, BEETLE_W, source=BEETLE),
    case("beetle-cloglog", "binomial", "cloglog", BEETLE_X, BEETLE_Y, BEETLE_W, source=BEETLE),
    case("beetle-quasibinomial", "quasiBinomial", "logit", BEETLE_X, BEETLE_Y, BEETLE_W, source=BEETLE),
    case("binary-logit", "binomial", "logit", BIN_X, BIN_Y, ones(24), source="synthetic 0/1 outcome"),
    case("binary-probit", "binomial", "probit", BIN_X, BIN_Y, ones(24), source="synthetic 0/1 outcome"),
    case("binary-cloglog", "binomial", "cloglog", BIN_X, BIN_Y, ones(24), source="synthetic 0/1 outcome"),
    case("dobson-poisson-identity", "poisson", "identity", DOBSON_X, COUNTS, ones(9), source="Dobson (1990)"),
    case("dobson-poisson-sqrt", "poisson", "sqrt", DOBSON_X, COUNTS, ones(9), source="Dobson (1990)"),
    case("dobson-quasipoisson-identity", "quasiPoisson", "identity", DOBSON_X, COUNTS, ones(9), source="Dobson (1990)"),
    case("clotting-gamma-inverse", "gamma", "inverse", CLOT_X, LOT1, ones(9), source="McCullagh & Nelder (1989) clotting, R ?glm"),
    case("clotting-gamma-identity", "gamma", "identity", CLOT_X, LOT1, ones(9), source="McCullagh & Nelder (1989) clotting"),
    case("clotting-ig-inversesquare", "inverseGaussian", "inverseSquare", CLOT_X, LOT1, ones(9), source="McCullagh & Nelder (1989) clotting"),
    case("clotting-ig-inverse", "inverseGaussian", "inverse", CLOT_X, LOT1, ones(9), source="McCullagh & Nelder (1989) clotting"),
    case("clotting-ig-identity", "inverseGaussian", "identity", CLOT_X, LOT1, ones(9), source="McCullagh & Nelder (1989) clotting"),
    case("clotting-ig-log", "inverseGaussian", "log", CLOT_X, LOT1, ones(9), source="McCullagh & Nelder (1989) clotting"),
    case("clotting-gaussian-log", "gaussian", "log", CLOT_X, LOT1, ones(9), source="McCullagh & Nelder (1989) clotting"),
    case("clotting-gaussian-inverse", "gaussian", "inverse", CLOT_X, LOT1, ones(9), source="McCullagh & Nelder (1989) clotting"),
    case("counts-negbin-log", "negativeBinomial", "log", NB_X, NB_Y, ones(30), source="synthetic overdispersed counts"),
    case("counts-negbin-log-fixed", "negativeBinomial", "log", NB_X, NB_Y, ones(30), theta=2.0, source="synthetic overdispersed counts"),
    case("counts-negbin-sqrt", "negativeBinomial", "sqrt", NB_X, NB_Y, ones(30), source="synthetic overdispersed counts"),
    case("counts-negbin-identity-fixed", "negativeBinomial", "identity", NB_X, NB_Y1, ones(30), theta=3.0, source="synthetic overdispersed counts"),
]


def grid():
    erfc_x = [-6.0 + 0.137 * k for k in range(80)] + [5.0 + 0.29 * k for k in range(80)] + [0.84375, 1.25, 2.857142857142857, 27.9]
    cdf_x = [-37.5 + 0.5 * k for k in range(60)] + [-7.95 + 0.21 * k for k in range(80)]
    dens_x = [-38.0 + 0.47 * k for k in range(162)]
    quant_p = [1e-300, 1e-200, 1e-100, 1e-50, 1e-20, 1e-12, 1e-8, 1e-4, 0.01, 0.0749, 0.0751, 0.2, 0.5, 0.6, 0.9, 0.925, 0.99, 0.999999]
    return {
        "erfc": [[repr(v), "%.30e" % erfc(exact(v))] for v in erfc_x],
        "normalCdf": [[repr(v), "%.30e" % phi_cdf(exact(v))] for v in cdf_x],
        "normalDensity": [[repr(v), "%.30e" % phi_density(exact(v))] for v in dens_x],
        "normalQuantile": [[repr(v), "%.30e" % phi_quantile(exact(v))] for v in quant_p],
    }


if __name__ == "__main__":
    print(json.dumps({"phase": "600", "precision": 80, "special": grid(), "cases": [fit(c) for c in CASES]}, indent=2))
