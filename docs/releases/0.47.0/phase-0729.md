### The versors: rotors, translators, motors, dilators and reflections, with the exponential, the logarithm, interpolation and the matrix, in `Fuaran.Compute.GeometricAlgebra` (Phase 729, `DECISIONS.md` D85, D88)

**What changed.** The package gains five record types, five modules of functions and one union, and **its first
reference beside FSharp.Core: `Fuaran.Compute.Elementary`**, for the pinned `sinCos` and `atan2` (which brings
`Fuaran.Compute.Solvers`, Elementary's own reference). Its baseline, `api/Fuaran.Compute.GeometricAlgebra.txt`, grows
and no other file under `api/` or `api/wire/` moves. No function that existed answers another bit.

- `Rotor`, `Translator`, `Motor`, `Dilator` and `Reflection`: plain records of the coefficients D85 item 8 gives
  each, in index order, `Scalar` first where the type carries it.
- A module of each one's name: `Rotor.ofAxisAngle`, `Translator.ofVector`, `Motor.ofRotorTranslator`,
  `Motor.ofScrew`, `Motor.ofLine`, `Dilator.ofFactor`, `Dilator.ofCentreFactor`, `Reflection.ofNormalDistance`; on
  every one `applyToPoint`, `applyToPointPair`, `applyToLine`, `applyToCircle`, `applyToPlane`, `applyToSphere`,
  `compose`, `normalise`, `toMultivector` and `ofMultivector`; `exp`, `log` and `interpolate` on `Rotor` and `Motor`;
  `toMatrix` on the four even versors; and `Motor.ofMatrix`. Seventy-one functions.
- `MatrixRefusal`, the union a matrix that is no similarity is refused with, three of whose cases carry the measure
  that failed.
- **`GeometryRefusal` gains eight cases** (`NonFiniteAngle`, `NonFiniteTranslation`, `NonFiniteFactor`,
  `NonPositiveFactor`, `NonFiniteBivector`, `NonFiniteParameter`, `NegativeSquaredNorm`, `NoRotationPlane`). A closed
  union gaining a case is breaking for a caller that matches it exhaustively; the union is new in this draft and no
  released version carries it.
- `GeometricAlgebraOperations.exactness` has a `Pinned` row for each new function, 160 in all, and
  `GeometricAlgebraLaws.laws` runs nine more laws, thirty-six in all at this entry; Phase 730 adds a thirty-seventh
  (the next entry).

**What a caller should know first.**

1. **A versor is applied by its reverse, and nothing is normalised.** `applyTo…` is `(V X) Ṽ`, the left product
   first, copied into the object's type. A versor built by a constructor is a unit because of what it computes. One
   that is not, a product of many compositions that has drifted, or one a caller scaled, scales what it is applied
   to: a rotor twice a unit takes a point's weight to four. `normalise` is the one division by `√(V ∗ Ṽ)`, and a
   caller asks for it by name.
2. **`compose b a` is the product `B A`, which applies `a` first.** `Motor.ofRotorTranslator rotor translator` is the
   rotation and then the translation.
3. **A dilation divides a point's weight by its factor.** `Point.coordinates` divides by the weight, so coordinates
   read back are right; a caller who reads `E1` directly reads the coordinate over the factor.
4. **The logarithm does not take the shorter way, and neither does interpolation.** `log` answers an angle `φ` in
   `[0, π]`, so the rotation it names lies in `[0, 2π]`, and `interpolate` follows it. A caller who wants the shorter
   way negates the second versor where the scalar coefficient of `Ṽ₁ V₂` is negative: the negated versor is the same
   motion. Where the bivector part is zero and the scalar part is not positive the logarithm is refused,
   `NoRotationPlane`.
5. **A matrix is sixteen doubles, column-major, and this package references no matrix type.** A caller holding
   `Fuaran.Compute.LinearAlgebra` writes `Matrix.init 4 4 (fun i j -> m[4 * j + i])` one way and
   `Matrix.toColumnMajor` the other. `Motor.ofMatrix tau m` answers a motor and a factor, the dilation about the
   origin first; `tau` is the threshold of the test that the three columns are of one length and orthogonal, in
   statistics that have no unit, and `0` is the exact comparison, which an integer similarity passes. A shear, a
   scale that is not uniform and a mirror are refused by name with the measure that failed. **Scale a matrix of
   very small or very large entries first.** The test sums squares of the entries and guards them with no
   threshold: a rotation is read with entries up to about `6·10¹⁵³` and refused `NonFiniteDivisor` beyond; it is
   read down to about `6·10⁻¹⁵⁷`; between about `3·10⁻¹⁵⁷` and `4·10⁻¹⁶²` the squares are subnormal and a true
   similarity is refused `NotOrthogonal` or `NotOneLength`, a name that does not describe it, with a measure that
   is rounding; and below about `2·10⁻¹⁶²` it is refused `ZeroDivisor`. The edges are one measured rotation's.
6. **An exponential keeps the sign of a zero.** `Rotor.exp (-0.0, 0.0, -0.0)` holds those three zeros as given, so
   `exp (−B)` is the reverse of `exp B` bit for bit. This is the phase's reading of a point D85's words leave open
   (D88 item 3) and can be revisited before `0.47.0` is tagged.
7. **The sine, the cosine and the arc tangent are the pinned ones**, so a rotor from an angle is the same bits on
   every host and as accurate as D84 measured `sinCos`, which is not correct rounding. The dilator is a root and two
   divisions and is exact at a power of four.
8. **An identity holds to rounding, and each stated bound is one sample's maximum.** Over the samples D88 records,
   a rotor times its reverse is within 2.5 units in the last place of one, the logarithm of a rotor's exponential
   within 3 of the bivector, and a rotation, a translation and a uniform scale applied to a point through the
   matrix within 1,534 units in the last place of the same point through the versor. The larger figures are
   coordinates near zero, and none is a bound on the function.

A host that ports the package certifies against the vectors. The specialised products are the next entry; the op
algebra is a later phase and is not in this package yet.
