"""The fifty-digit reference for Fuaran.Compute.Elementary's trigonometric functions (Phase 725, DECISIONS.md D84).

Source: this file alone, under CPython 3.9 or later. It uses the standard library only (`fractions`, and the
language's own unbounded integers): nothing is installed. Every value is carried as an exact rational or as a
fixed-point integer of at least 620 fractional bits, so each reference is the true value to well beyond fifty
digits, and it is rounded to a double once, by Python's correctly rounded integer division.

  * pi is Machin's formula, 16 atan(1/5) - 4 atan(1/239), summed in integers.
  * sin and cos take the double as the exact rational it is, subtract the nearest multiple of pi/2 with pi held to
    the argument's exponent and 620 bits more, and sum the Taylor series of the remainder in integers.
  * atan halves its argument three times (t -> t / (1 + sqrt(1 + t^2)), the square root an integer square root) and
    sums the series; an argument above one is pi/2 less the reciprocal's; atan2 is atan of the exact quotient,
    placed in its quadrant.

Three modes, each reading and writing plain text:

  python trig_reference.py constants
      Prints every constant `Elementary.fs` holds for these functions, each a double as seventeen significant
      digits and its bit pattern: the pieces of pi/2, the digits of 2/pi in base 2^24, the table's answers, and
      the split arc tangents.

  python trig_reference.py references < conformance/laws/elementary-laws.json
      Reads the committed law document. For every vector of `sin`, `cos`, `atan` and `atan2` the routine
      computed, prints `"<id>", "<hex>"`: the true value rounded once to the nearest double, as its IEEE-754
      binary64 bit pattern. This is how the trigonometric rows of `ElementaryVectors.references` were made.

  python trig_reference.py check < answers.txt
      Each input line is `<op> <hex> [<hex>] <hex>`: the operands, then the answer (an `<op>` of the form
      `<function>:<label>` is scored under its own heading). Prints, per op, the count, the largest, the 99th
      percentile and the mean distance from the true value in units in the last place of the correctly rounded
      value, the count more than one unit away, and the three worst inputs.
"""
import json
import math
import struct
import sys
from fractions import Fraction
from math import isqrt

# Fractional bits every series is summed at, beyond what the argument's size asks for.
WORK = 620


def of_hex(h):
    return struct.unpack(">d", bytes.fromhex(h))[0]


def to_hex(x):
    if x != x:
        return "nan"
    return struct.pack(">d", x).hex()


def atan_inverse(n, bits):
    """atan(1/n) scaled by 2^bits, for an integer n of two or more."""
    total = 0
    term = (1 << bits) // n
    square = n * n
    k = 0
    while term:
        total += term // (2 * k + 1) if k % 2 == 0 else -(term // (2 * k + 1))
        term //= square
        k += 1
    return total


_pi_cache = {}


def pi_scaled(bits):
    """pi scaled by 2^bits, within one unit."""
    if bits not in _pi_cache:
        guard = bits + 64
        _pi_cache[bits] = (16 * atan_inverse(5, guard) - 4 * atan_inverse(239, guard)) >> 64
    return _pi_cache[bits]


def sin_cos_series(r, bits):
    """(sin, cos) of r / 2^bits, each scaled by 2^bits, for |r| / 2^bits at most one."""
    square = (r * r) >> bits
    s, term, k = 0, r, 1
    while term:
        s += term
        term = -((term * square) >> bits) // ((k + 1) * (k + 2))
        k += 2
    c, term, k = 0, 1 << bits, 0
    while term:
        c += term
        term = -((term * square) >> bits) // ((k + 1) * (k + 2))
        k += 2
    return s, c


def sin_cos(x):
    """(sin x, cos x) for a rational x, as rationals."""
    if x == 0:
        return Fraction(0), Fraction(1)
    if abs(x) < Fraction(1, 1 << 200):
        return x * (1 - x * x / 6), 1 - x * x / 2
    exponent = max(0, abs(x.numerator).bit_length() - x.denominator.bit_length() + 1)
    bits = WORK + 300 + exponent
    scaled = (x.numerator << bits) // x.denominator
    half_pi = pi_scaled(bits + 8) >> 9
    k = (2 * scaled + half_pi) // (2 * half_pi)
    r = scaled - k * half_pi
    s, c = sin_cos_series(r, bits)
    s, c = [(s, c), (c, -s), (-s, -c), (-c, s)][k % 4]
    return Fraction(s, 1 << bits), Fraction(c, 1 << bits)


def atan(x):
    """atan x for a rational x of zero or more, as a rational."""
    if x == 0:
        return Fraction(0)
    if x < Fraction(1, 1 << 150):
        return x * (1 - x * x / 3)
    bits = WORK + 300
    half_pi = Fraction(pi_scaled(bits), 1 << (bits + 1))
    if x > (1 << 150):
        return half_pi - (1 / x) * (1 - 1 / (3 * x * x))
    t = x if x <= 1 else 1 / x
    scaled = (t.numerator << bits) // t.denominator
    one = 1 << bits
    for _ in range(3):
        root = isqrt((one * one) + scaled * scaled)
        scaled = (scaled << bits) // (one + root)
    square = (scaled * scaled) >> bits
    total, term, k = 0, scaled, 0
    while term:
        total += term // (2 * k + 1) if k % 2 == 0 else -(term // (2 * k + 1))
        term = (term * square) >> bits
        k += 1
    value = Fraction(8 * total, 1 << bits)
    return value if x <= 1 else half_pi - value


def atan2(y, x):
    """atan2(y, x) for rationals, neither zero, as a rational."""
    a = atan(abs(y) / abs(x))
    if x < 0:
        a = Fraction(pi_scaled(WORK + 300), 1 << (WORK + 300)) - a
    return a if y > 0 else -a


def nearest(v):
    """A rational rounded once to the nearest double."""
    return v.numerator / v.denominator


def exact(h):
    return Fraction(of_hex(h))


def true_value(op, operands):
    """The true value of one function at operands given as bit patterns; `sinCos` answers a pair."""
    if op == "sin":
        return sin_cos(exact(operands[0]))[0]
    if op == "cos":
        return sin_cos(exact(operands[0]))[1]
    if op == "atan":
        x = exact(operands[0])
        return atan(x) if x >= 0 else -atan(-x)
    if op == "atan2":
        return atan2(exact(operands[0]), exact(operands[1]))
    raise ValueError(op)


def split(v, bits):
    """The leading `bits` bits of a positive rational, as a rational."""
    e = v.numerator.bit_length() - v.denominator.bit_length()
    if Fraction(2) ** e > v:
        e -= 1
    scale = Fraction(2) ** (bits - 1 - e)
    return Fraction(math.floor(v * scale)) / scale


def show(name, value):
    print(f"{name} = {value!r}  ({to_hex(value)})")


def constants():
    bits = 2000
    pi = Fraction(pi_scaled(bits), 1 << bits)
    half_pi = pi / 2
    show("pi", nearest(pi))
    show("pi, the remainder", nearest(pi - Fraction(nearest(pi))))
    show("pi / 2", nearest(half_pi))
    show("pi / 2, the remainder", nearest(half_pi - Fraction(nearest(half_pi))))
    show("pi / 4", nearest(pi / 4))
    show("3 pi / 4", nearest(3 * pi / 4))
    show("2 / pi", nearest(2 / pi))
    rest = half_pi
    for i in (1, 2, 3):
        piece = split(rest, 33)
        show(f"pi / 2, piece {i} (33 bits)", nearest(piece))
        rest -= piece
    show("pi / 2, the tail past three pieces", nearest(rest))
    two_over_pi = (1 << (2 * bits + 1)) // pi_scaled(bits)
    digits = [(two_over_pi >> (bits - 24 * (i + 1))) & 0xFFFFFF for i in range(50)]
    print("2 / pi in base 2^24, fifty digits:")
    for i in range(0, 50, 5):
        print("    " + "; ".join(f"{d}.0" for d in digits[i : i + 5]))
    print("    as hex: " + " ".join(f"{d:06X}" for d in digits[:8]) + " ...")
    for name, x in (("atan 1/2", Fraction(1, 2)), ("atan 1", Fraction(1)), ("atan 3/2", Fraction(3, 2))):
        for label, value in ((name, atan(x)), ("pi - " + name, pi - atan(x))):
            hi = nearest(value)
            show(label, hi)
            show(label + ", the remainder", nearest(value - Fraction(hi)))


def references():
    document = json.load(sys.stdin)
    for vector in document["vectors"]:
        op = vector.get("op")
        if op not in ("sin", "cos", "atan", "atan2") or vector["expected"].get("table") != "compute":
            continue
        operands = [vector["input"][name] for name in (("y", "x") if op == "atan2" else ("x",))]
        print(f'"{vector["id"]}", "{to_hex(nearest(true_value(op, operands)))}"')


def check():
    scores = {}
    for line in sys.stdin:
        parts = line.split()
        if len(parts) < 3:
            continue
        label, operands, answer = parts[0], parts[1:-1], parts[-1]
        op = label.split(":")[0]
        truth = true_value(op, operands)
        reference = nearest(truth)
        if answer == "nan" or math.isinf(of_hex(answer)):
            distance = math.inf
        else:
            distance = abs(float((exact(answer) - truth) / Fraction(math.ulp(reference))))
        scores.setdefault(label, []).append((distance, operands, answer, to_hex(reference)))
    print("| function | scored | max | 99th percentile | mean | more than one unit away |")
    print("|---|---:|---:|---:|---:|---:|")
    for label, rows in scores.items():
        rows.sort(key=lambda row: row[0])
        distances = [row[0] for row in rows]
        p99 = distances[min(len(distances) - 1, (len(distances) * 99) // 100)]
        mean = sum(distances) / len(distances)
        over = sum(1 for d in distances if d > 1.0)
        print(f"| {label} | {len(rows)} | {distances[-1]:.3f} | {p99:.3f} | {mean:.3f} | {over} |")
    print()
    for label, rows in scores.items():
        for distance, operands, answer, reference in rows[-3:][::-1]:
            print(f"worst {label}: {distance:.3f} at {' '.join(operands)} answered {answer}, reference {reference}")


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    if mode == "constants":
        constants()
    elif mode == "references":
        references()
    elif mode == "check":
        check()
    else:
        sys.exit(__doc__)
