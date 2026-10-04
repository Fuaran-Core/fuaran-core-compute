# Phase 345 evidence - Python leg: math.fsum over the same bytes; the bit pattern is the cross-host oracle.
import math, struct, sys, os, time
d = sys.argv[1]
out = []
print(sys.version.split()[0])
for kind in ["quarters", "uniform", "wide"]:
    for n in [10000, 100000, 1000000]:
        with open(os.path.join(d, f"{kind}-{n}.f64"), "rb") as f:
            xs = list(struct.unpack(f"<{n}d", f.read()))
        t0 = time.perf_counter(); v = math.fsum(xs); t1 = time.perf_counter()
        fold = 0.0
        for x in xs: fold += x
        b = struct.pack("<d", v)[::-1].hex()
        fb = struct.pack("<d", fold)[::-1].hex()
        print(kind, n, "fsum", b, "fold", fb, f"fsum {1000*(t1-t0):.3f} ms")
        out.append(f"{kind} {n} fsum={b} fold={fb}")
with open(os.path.join(d, "python-results.txt"), "w") as f:
    f.write("\n".join(out) + "\n")
