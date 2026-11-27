### `Fuaran.Compute.GeometricAlgebra` is cut: the multivector of `Cl(4,1)` and its products (Phase 727, `DECISIONS.md` D85, D86) — additive

**What changed.** A new package, `Fuaran.Compute.GeometricAlgebra`, in the namespace of its id, referencing FSharp.Core
alone. It publishes one type and three modules, and its baseline, `api/Fuaran.Compute.GeometricAlgebra.txt`, is new; no
other file under `api/` or `api/wire/` moves, and no other package's surface or answers move.

- `Multivector`: a sealed, immutable class over thirty-two coefficients in D85 item 2's blade order (the null basis
  `n0 e1 e2 e3 n∞`, by grade and within a grade lexicographically). It has no public constructor and never hands its
  storage out. Its `Equals` and `GetHashCode` are the float identity: every NaN is one value and `-0.0` equals `0.0`.
- The module `Multivector`: `zero`, `init`, `ofScalar` and `basis` construct; `coefficient`, `toArray` and `grade`
  (the grade projection) read back; `add`, `subtract` and `scale` are one rounded operation for each coefficient;
  `negate`, `reverse`, `gradeInvolution`, `dual` and `undual` are copies and exact negation; `geometricProduct`,
  `outerProduct`, `leftContraction`, `scalarProduct` and `squaredNorm` are D85 item 3's fold. There are no operators.
  Nothing refuses: a blade index outside `0 .. 31` is an argument and raises `IndexOutOfRangeException`, as an array's
  would, and a grade the algebra does not have projects to zero.
- `GeometricAlgebraOperations.exactness` classifies every one of those functions, each `Pinned`, and
  `GeometricAlgebraLaws.laws` is the law family a host runs.

**The wire.** None. The package encodes nothing, and a codec for multivector coefficients is ruled out (D85 item 10).

**The law vectors.** `conformance/laws/geometric-algebra-laws.json` is new: 167 vectors over the published functions,
every float its bit pattern. Ninety-six of them multiply each basis blade by the powers of two under each of the three
products, so every term of each table, and its sign, is one bit of one answer. The rest are inputs on which a port that
keeps another rule answers another bit (another order of summation, a fused multiply-add, a fold seeded from its first
term, a term skipped because a coefficient is zero, a negation that loses a zero's sign), and operands drawn across
eighty binary orders of magnitude with both zeros, an infinity, a NaN, subnormals and overflow among them. Every other
file under `conformance/laws/` is byte-identical.

**What a consumer must do.** Nothing: no existing package changes. A caller that wants the algebra references
`Fuaran.Compute.GeometricAlgebra`, opens the namespace and writes `Multivector.geometricProduct a b`. Three things to
know before relying on it. The identities of the algebra hold exactly only where the arithmetic is exact; elsewhere a
product is the fold's rounding, the same bits on every host, and a law that needs an identity states a bound. No
function normalises and none takes a tolerance. And `=` on two multivectors is the float identity, so it does not tell
a `-0.0` from a `0.0`; a comparison of bits reads `Multivector.toArray`. A host that ports the package certifies
against the vectors and against the three table digests D85 item 3 pins. The conformal objects are the next entry and
the versors the one after; the op algebra is a later phase and is not in this package yet.
