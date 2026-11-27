### The conformal objects: points, lines, planes, circles and spheres as blades, with meet, join, read-back and incidence, in `Fuaran.Compute.GeometricAlgebra` (Phase 728, `DECISIONS.md` D85, D87) — additive

**What changed.** The package gains six record types, seven modules, one union of refusals and nine unions of kinds;
its baseline, `api/Fuaran.Compute.GeometricAlgebra.txt`, grows and no other file under `api/` or `api/wire/` moves. No
function that existed answers another bit. It still referenced FSharp.Core alone; Phase 729, below, gives it its
first reference.

- `Point`, `PointPair`, `Circle`, `Line`, `Sphere` and `Plane`: plain immutable records, one `float` field for each
  coefficient the type carries, in index order, named for its blade. Each has a module of its own name with
  `toMultivector` and `ofMultivector`.
- Construction, two ways each: `through` (the outer product of points, with the weight that product gives) and from
  parameters (`Point.up`, `Line.ofPointDirection`, `Circle.ofCentreRadiusNormal`, `Plane.ofNormalDistance`,
  `Sphere.ofCentreRadius`, each of weight one). The two agree in orientation.
- `Blade.join`, `Blade.meet`, `Blade.meetWithin` and `Blade.inverse` over multivectors, and the typed meets
  `Plane.meet`, `Sphere.meet`, `Sphere.meetPlane`, `Line.meetPlane`, `Line.meetSphere` and `Line.meet`.
- Read-back: `weight`, `normalise`, `centre`, `squaredRadius`, `PointPair.points`, `Line.direction` and `moment`,
  `Plane.normal` and `distance`, `nearestPoint`, `Point.coordinates`, `Circle.plane`.
- `kind` on five types: a round (real, tangent or imaginary), a flat, at infinity, the zero blade, or no object.
- Incidence: `Sphere.power`, `Circle.power` and `Plane.signedDistance` as values, and `side` on each as the test.
- `GeometryRefusal`, the one union a refusal is a case of, and `GeometricAlgebraOperations.exactness` with a `Pinned`
  row for each of the seventy new functions. `GeometricAlgebraLaws.laws` runs nine more laws, twenty-seven in all.

**The wire.** None, as before.

**The law vectors.** `conformance/laws/geometric-algebra-laws.json` grows from 167 vectors to 1,007. The 167 are
unchanged in content; the last of them gains the comma that now separates it from the next, and the document's
`description` says how the new shapes are written. Four of the new ones are over `squaredNorm` and `scalarProduct`.
The other 836 are over the seventy functions: three drawn configurations through each; each named case, each
refusal and each side of each threshold by hand; and sixty-six drawn cases chosen because a wrong port fails them. A typed operand is the list of its coefficients; an answer that can
be refused is its own cell or `{ "refused": … }`; an answer that has kinds is `{ "kind": … }` with what the case
carries. Every other file under `conformance/laws/` is byte-identical.

**What a consumer must do.** Nothing. A caller that wants the geometry should know five things before relying on it.

1. **A zero test takes its threshold on every call.** `kind`, `side`, `PointPair.points` and `Line.meet` take `tau`
   first: a finite number that is not negative. There is no default, and `0` is the exact comparison: there a
   statistic that is a quotient (a power, a signed distance, a round's squared radius) is decided from the signs of
   its numerator and its divisor, so a quotient that underflows to zero is not read as zero. The unit is the
   statistic's own, and each function's documentation states it; `kind` compares coefficient norms and, for a round,
   a squared radius, against the one `tau`.
2. **Nothing normalises.** An object built through points or by a meet has the weight its product gives, which is a
   signed number for a point and a sphere. A read-back divides by a weight only where its definition does
   (`coordinates`, `centre`, `squaredRadius`, `nearestPoint`, the statistics). `Line.direction`, `Plane.normal` and
   `Plane.distance` are read, so they are the unit ones times the weight; call `normalise` first where that matters.
3. **Nothing throws on bad data, and an `Ok` never holds a NaN.** A function that can refuse returns
   `Result<_, GeometryRefusal>`. The functions that only multiply or copy (`through`, the meets, `Blade.join`) are
   total, and `kind` says what they returned: three collinear points give a `Circle` whose kind is the flat that
   carries their line, two spheres apart a circle whose squared radius is negative and is reported, two parallel
   lines a pair at infinity.
4. **A real point pair's two points come back with the weight the formula gives,** which is `−1` for a pair built
   through two points of weight one. Read them with `Point.coordinates`.
5. **The identities hold exactly only where the arithmetic is exact.** On general inputs an answer is the fold's
   rounding, the same bits on every host, and agrees with a closed form to rounding: over the sample D87 records, a
   line and a sphere meet within 292 units in the last place of where the quadratic formula puts them. That is one
   sample's maximum and no bound on the function.

A host that ports the package certifies against the vectors. The versors are the next entry, and the specialised
products the entry after it (Phase 730); the op algebra is a later phase and is not in this package yet.
