"""Phase 593 — the reference check behind DECISIONS.md D52.

Reads the `BEGIN elementary-samples` … `END` block the benchmark harness prints
(`node Program.js <runs> elementary`, or the same program on .NET) from one or two capture files,
computes the correctly rounded double of each input with Python's `decimal` at 80 digits, and reports,
per function and per candidate, the distance of each answer from it in units in the last place (the
difference of ordered bit patterns): the worst, the mean, and a histogram. With two captures it also
compares them line for line, which is the byte-identity claim.

    python reference-check.py <dotnet-capture> [<node-capture>]

The reference is independent of both candidates: `decimal`'s `exp`, `ln` and `log10` at 80 digits
rounded once to the nearest double, and `pow` as `exp(y · ln x)` at the same precision. A result the
harness answered as `nan`, `0`, an infinity or a subnormal is compared under the same ordered-bit
distance, with the reference rounded at the subnormal precision by `float()` itself.
"""

import struct
import sys
from collections import Counter
from decimal import Decimal, getcontext, Overflow, Underflow

getcontext().prec = 80
# Below the smallest subnormal `float()` rounds to zero on its own; let the context not raise.
getcontext().traps[Overflow] = False
getcontext().traps[Underflow] = False


def of_hex(h):
    return struct.unpack(">d", bytes.fromhex(h))[0]


def ordered(x):
    b = struct.unpack(">q", struct.pack(">d", x))[0]
    return (-(2**63)) - b if b < 0 else b


def ulps(a, b):
    if a != a and b != b:
        return 0
    if a != a or b != b:
        return None
    return abs(ordered(a) - ordered(b))


def reference(fn, x, y=None):
    if fn == "exp":
        return float(Decimal(x).exp())
    if fn == "ln":
        return float(Decimal(x).ln())
    if fn == "log10":
        return float(Decimal(x).log10())
    if fn == "pow":
        if x < 0:
            # an integer exponent on a negative base: the sign by parity, the magnitude as below
            n = int(y)
            assert n == y
            mag = float((Decimal(-x).ln() * Decimal(y)).exp())
            return -mag if n % 2 else mag
        return float((Decimal(x).ln() * Decimal(y)).exp())
    raise ValueError(fn)


def read(path):
    rows = []
    inside = False
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\r\n")
            if line == "BEGIN elementary-samples":
                inside = True
                continue
            if line == "END elementary-samples":
                break
            if inside and line:
                rows.append(line.split(" "))
    return rows


def value(h):
    return float("nan") if h == "nan" else of_hex(h)


def report(rows, label):
    print(f"\n## {label}: {len(rows)} samples")
    print()
    print("| function | n | candidate | worst ulps | mean ulps | 0 | 1 | 2 | >2 | NaN mismatch |")
    print("|---|---:|---|---:|---:|---:|---:|---:|---:|---:|")
    by_fn = {}
    for row in rows:
        by_fn.setdefault(row[0], []).append(row)
    for fn in ("exp", "ln", "log10", "pow"):
        fn_rows = by_fn.get(fn, [])
        if not fn_rows:
            continue
        for candidate, index in (("faithful-pinned", -2), ("correctly rounded", -1)):
            hist = Counter()
            total = 0
            worst = 0
            nan_mismatch = 0
            for row in fn_rows:
                x = value(row[1])
                y = value(row[2]) if fn == "pow" else None
                ref = reference(fn, x, y)
                got = value(row[index])
                d = ulps(got, ref)
                if d is None:
                    nan_mismatch += 1
                    continue
                total += d
                worst = max(worst, d)
                hist[min(d, 3)] += 1
            n = len(fn_rows)
            print(
                f"| {fn} | {n} | {candidate} | {worst} | {total / n:.4f} | {hist[0]} | {hist[1]} | {hist[2]} | {hist[3]} | {nan_mismatch} |"
            )


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    first = read(argv[1])
    report(first, argv[1])
    if len(argv) > 2:
        second = read(argv[2])
        report(second, argv[2])
        print()
        if len(first) != len(second):
            print(f"BYTE IDENTITY: the captures hold {len(first)} and {len(second)} lines — not comparable")
            return 1
        differ = [(a, b) for a, b in zip(first, second) if a != b]
        pinned_differ = [(a, b) for a, b in zip(first, second) if a[:-1] != b[:-1]]
        print(
            f"BYTE IDENTITY: faithful-pinned answers differ on {len(pinned_differ)} of {len(first)} lines; "
            f"with the correctly rounded candidate's column, {len(differ)} lines differ"
        )
        for a, b in pinned_differ[:10]:
            print("  ", " ".join(a), "|", " ".join(b))
        return 1 if pinned_differ else 0
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
