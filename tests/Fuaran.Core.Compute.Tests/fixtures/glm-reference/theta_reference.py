"""The fifty-digit reference for the negative binomial's theta at fixed means (Phase 634, DECISIONS.md D80).

Source: mpmath 1.4.1 (https://mpmath.org, BSD-3-Clause), run under CPython 3 at `mp.dps = 50`. Install it
into a virtual environment (`python -m venv venv`, then `venv/Scripts/python -m pip install mpmath==1.4.1`);
nothing here is installed into the system Python and the environment is never committed.

    dotnet <bench>/Fuaran.Core.Compute.Benchmarks.Node.dll 5 glm > run.txt     (or the node leg)
    python theta_reference.py < run.txt

The input is the benchmark's `glm` family (`benchmarks/Fuaran.Core.Compute.Benchmarks/GlmBench.fs`); only
the lines between `BEGIN glm-theta` and `END glm-theta` are read:

    means <case> <where> <y,...> <mu,...>                     the counts and the fixed means, as bit patterns
    theta <case> <where> <method> <hex | refused:<why>> <steps> <evaluations>

For each set of means the reference is the root of theta's score at unit weights,

    S(t) = sum_i [ psi(t + y_i) - psi(t) + ln t + 1 - ln(t + mu_i) - (y_i + t) / (mu_i + t) ],

each count and mean read as the exact value of its double. The score is positive at small t whenever a
count is positive; the root is the first sign change from + to - on the grid t = 2^(k/4), k from -80 to
240 (2^-20 to 2^60), refined by bisection at fifty digits to a relative width below 1e-40. A set of means
whose score does not change sign on the grid has no root there: its likelihood rises towards the Poisson
limit, and the right answer is a refusal. A second sign change is reported, never chosen.

Printed: per answer, its relative distance from the root; then per method, the answers, the refusals, the
largest and the median distance, the refusals where a root lies inside [2^-20, 2^20] and the answers where
no root lies on the grid.
"""
import struct
import sys

from mpmath import mp, mpf, digamma, log

mp.dps = 50


def from_hex(h):
    return struct.unpack(">d", bytes.fromhex(h))[0]


def score(t, ys, mus):
    acc = mpf(0)
    lt = log(t)
    for y, m in zip(ys, mus):
        acc += digamma(t + y) - digamma(t) + lt + 1 - log(t + m) - (y + t) / (m + t)
    return acc


def root(ys, mus):
    """The first + to - sign change of the score on the grid, bisected; (None, 0) when there is none."""
    grid = [mpf(2) ** (mpf(k) / 4) for k in range(-80, 241)]
    values = [score(t, ys, mus) for t in grid]
    changes = [i for i in range(len(grid) - 1) if values[i] > 0 and values[i + 1] <= 0]
    if not changes:
        return None, 0
    i = changes[0]
    lo, hi = grid[i], grid[i + 1]
    while (hi - lo) / hi > mpf(10) ** -40:
        mid = (lo + hi) / 2
        if score(mid, ys, mus) > 0:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2, len(changes)


def main():
    inside = False
    means = {}
    answers = []
    for line in sys.stdin:
        line = line.strip()
        if line == "BEGIN glm-theta":
            inside = True
            continue
        if line == "END glm-theta":
            inside = False
            continue
        if not inside or not line:
            continue
        parts = line.split()
        if parts[0] == "means":
            ys = [mpf(from_hex(h)) for h in parts[3].split(",")]
            mus = [mpf(from_hex(h)) for h in parts[4].split(",")]
            means[(parts[1], parts[2])] = (ys, mus)
        elif parts[0] == "theta":
            answers.append(parts[1:])

    references = {key: root(ys, mus) for key, (ys, mus) in means.items()}

    print("| case | means | method | answer | reference | relative distance | steps | evaluations |")
    print("|---|---|---|---|---|---:|---:|---:|")
    summary = {}
    for case, where, method, answer, steps, evaluations in answers:
        ref, changes = references[(case, where)]
        s = summary.setdefault(method, {"ok": 0, "refused": 0, "dist": [], "falseRefusal": [], "falseAnswer": []})
        refText = "none" if ref is None else mp.nstr(ref, 17) + ("" if changes == 1 else " (%d changes)" % changes)
        if answer.startswith("refused"):
            s["refused"] += 1
            if ref is not None and mpf(2) ** -20 <= ref <= mpf(2) ** 20:
                s["falseRefusal"].append((case, where, refText))
            dist = ""
        else:
            s["ok"] += 1
            theta = mpf(from_hex(answer))
            if ref is None:
                s["falseAnswer"].append((case, where, float(theta)))
                dist = "no root"
            else:
                d = abs(theta - ref) / ref
                s["dist"].append(d)
                dist = mp.nstr(d, 3)
            answer = repr(float(theta))
        print("| %s | %s | %s | %s | %s | %s | %s | %s |" % (case, where, method, answer, refText, dist, steps, evaluations))

    print()
    print("| method | answers | refusals | largest distance | median distance | refused with a root in [2^-20, 2^20] | answered with no root |")
    print("|---|---:|---:|---:|---:|---|---|")
    for method, s in summary.items():
        ds = sorted(s["dist"])
        largest = mp.nstr(ds[-1], 3) if ds else "-"
        median = mp.nstr(ds[len(ds) // 2], 3) if ds else "-"
        fr = "; ".join("%s/%s (%s)" % x for x in s["falseRefusal"]) or "none"
        fa = "; ".join("%s/%s (%r)" % x for x in s["falseAnswer"]) or "none"
        print("| %s | %d | %d | %s | %s | %s | %s |" % (method, s["ok"], s["refused"], largest, median, fr, fa))


if __name__ == "__main__":
    main()
