### The geometric product is specialised internally to nine pairs of operand types, and the law family gains one law, in `Fuaran.Compute.GeometricAlgebra` (Phase 730, `DECISIONS.md` D89)

**What changed.** No signature, no answer, no law vector and no line of `api/Fuaran.Compute.GeometricAlgebra.txt`
moved. Inside the package, the geometric product the typed functions form is specialised to nine pairs of operand
types (a rotor with a rotor, a motor with a motor, a translator with a rotor, and both products of the sandwich of a
rotor, a translator and a motor with a point): it folds only the terms of D85 item 3's table whose blades the two
types carry, in the table's order, and only where both operands are finite and hold zeros off their types; anything
else is the general product. Every answer is the general product's, bit for bit. The published
`Multivector.geometricProduct` is the general product for every input.

- **`GeometricAlgebraLaws.laws` runs one more law, thirty-seven in all:** that each specialised product is the general
  product bit for bit on every input of its operand types. It reads the package's internals (the specialised pairs and
  the specialised product, none of them published), so a host that ports the package has nothing to implement for it
  and certifies against the vectors as before. It is in the published family because that is how the node leg runs it
  under node.

**What a caller should know first.**

1. **Nothing to do.** No function answers another bit, and no call changes.
2. **Speed, as measured and no more.** On one machine, the six typed functions the benchmark measures among the
   eleven that reach a specialised product ran 6.3 to 8.7 times faster on .NET and 6.7 to 11.7 times under node, by
   the middle of three medians. A function no pair serves, `Motor.applyToSphere`, measured slower on .NET in five of
   seven runs of the final tree (9.4 to 12.0 µs against 4.3 to 5.3 µs in seven runs of the base), and the cause is not
   established. `benchmarks/results/2026-11-24-i7-8650u-phase-730.md` has every run and what was not measured.
