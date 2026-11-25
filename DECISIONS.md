# Fuaran.Core.Compute — decisions (newest first)

## 2026-11-24 — D90: geometric transforms ride the op stream as a parametric op algebra in `Fuaran.Compute.GeometricAlgebraOps`, written to D85 item 9 and choosing nothing D85 fixed — a state of named objects ordered by the caller's comparison; D85 item 9's seven ops with its parameters and no coefficient; replay through the algebra's published constructors and `applyTo…`, computing nothing of its own; every refusal named and the state left the same value, an object that would hold a NaN refused; a codec that writes every float as its bit pattern, D51 item 7's rule; the `Fuaran.Core.OpStream` witness; an inverse for six ops, exact only for `Add`, and answered only where the state after the op accepts it; the compaction of a run of translations, or of rotations and screws about one axis, which nothing else calls; 244 vectors; twenty-three wrong ports held against nine functions; seven bounded laws, each pinning one stated sample; and three points D85 leaves open, recorded with their options

**Context.** Phase 731. D85 item 9 ruled this package's id, its references, its state ("a set of named objects,
generic in the name: the caller supplies the identity and its encoding"), its first op list and its refusals; item 10
keeps coefficients off a wire; items 4 to 7 fix the constructions, the sandwich, that nothing normalises, that no
threshold is a constant, and the form of a bound. This entry records what D85 left to this phase: the names, the
order of the state, the codec's spelling, each inverse and the compaction with the figures pinned on their samples,
and how each is held. `Fuaran.Compute.ColumnOps` is the shape followed: an op union, a total `apply`, `canApply` as `apply` with its
answer discarded, an `invert` guarded by `canApply` and partial, a canonical codec and a stream witness.

**Premise check** (on `84532a5`, the tree this phase was cut from). Phases 727 to 730 are on it, and the algebra
publishes the objects (`Point.up`, `PointPair.through`, `Line.ofPointDirection`, `Circle.ofCentreRadiusNormal`,
`Plane.ofNormalDistance`, `Sphere.ofCentreRadius`), the versors (`Motor.ofScrew`, `Translator.ofVector`,
`Dilator.ofCentreFactor`, `Reflection.ofNormalDistance`) and an `applyTo…` of each versor for each of the six objects.
This file opened with D89, D88, D87, D86 and D85. `Fuaran.Core.OpStream` and `Fuaran.Core.Wire` were central package
versions already. The version is `0.47.0`, a draft: no `v0.47.0` tag exists, and it stays `0.47.0`. One premise was
sharpened: the repository writes a float two ways (item 6), and only one gives the round trip this phase owes.

**Ruled.**

1. **The package and its boundary.** `Fuaran.Compute.GeometricAlgebraOps`, in the namespace of its id, references
   the algebra package, `Fuaran.Core.OpStream`, `Fuaran.Core.Wire` and FSharp.Core, and is the only geometric package
   that references the op-stream (D85 item 9). The suite holds that by project file and by built assembly
   (`ComputeBoundaryTests`), beside the algebra's own rule, which already refuses the op-stream, the wire and this
   package.

2. **The names D85 left.** `Naming<'N>` (`Compare`, `Encode`, `Decode`): the caller's comparison and spelling of a
   name. `Form`: D85 item 4's parametric forms, by case `Point`, `PointPair` (two points), `Line` (a point and a
   direction), `Circle` (a centre, a radius and a normal), `Plane` (a normal and a distance) and `Sphere` (a centre
   and a radius). `GeometricObject`: the algebra's six typed blades, one case each. `GeometricOp<'N>`: `Add`, `Remove`,
   `Rotate`, `Translate`, `Scale`, `Reflect` and `Screw`, each with item 9's parameters in item 9's order.
   `GeometricOpRefusal<'N>`: `UnknownName`, `NameAlreadyPresent`, `Geometry` (the algebra's `GeometryRefusal`, carried
   as its constructor gave it), `NotANumber` (item 5), `NotInvertible` and `InverseRefused` (item 8).
   `GeometricState<'N>` and its module (`empty`, `toList`, `tryFind`, `count`); the module `GeometricOps` (`apply`, `canApply`, `applyAll`, `invert`, `encode`, `decode`,
   `streamWitness`, `compact`); `GeometricAlgebraOpsOperations` and `GeometricAlgebraOpsLaws`, the names D85 item 7
   gave. Every union is `RequireQualifiedAccess`, so opening both namespaces brings no case named `Point` beside the
   algebra's type.

3. **The state and its order.** A sealed, immutable class over its `Naming` and its entries, with no public
   constructor. Its order is one rule: ascending under the caller's `Compare`, a new name placed before the first
   entry that compares above it. It is the same on .NET and under Fable whenever the caller's comparison is (the
   suite's names are strings under `String.CompareOrdinal`); the package compares nothing itself. Two names are one
   name when `Compare` answers zero.

4. **Replay, and what it computes.** `Add` builds its object by the form's constructor (two points are
   `PointPair.through (Point.up p) (Point.up q)`). A transform derives its versor by the algebra's constructor and
   applies it by the algebra's `applyTo…` for the object's kind: `Translate` by `Translator.ofVector`, `Scale` by
   `Dilator.ofCentreFactor`, `Reflect` by `Reflection.ofNormalDistance`, `Screw` by `Motor.ofScrew`, and `Rotate` by
   `Motor.ofScrew point direction angle 0.0`, the screw of no distance. Each of the five transforms applies to each of
   the six kinds (a case holds the thirty pairs). Nothing is normalised (D85 item 5), and the package forms no
   product, no trigonometric value and no norm. *The reading this rests on:* D85 item 4 writes the rotation about the
   axis through `p` as `(T_p R) T̃_p` inside the screw and publishes no constructor of that rotation alone; the screw of
   distance zero is that product times the translator by zero, and the suite holds `Motor.ofScrew p û θ 0.0` equal to
   `(T_p R) T̃_p` formed by the published geometric product bit for bit on four authored axes, the angles where the
   pinned sine and cosine are not correctly rounded among them (a test, on those inputs: a fold from `+0.0` adds a
   zero term without moving a bit, D89 item 3's argument). *Rejected:* composing `T_p`, the rotor and `T̃_p` with
   `Motor.compose` in this package, which would be a product this package forms.

5. **`apply`, `canApply`, `applyAll` and the refusals.** `apply` is total. It reads the name first (an op on a name
   the state does not hold is `UnknownName`; an `Add` under a name it holds is `NameAlreadyPresent`) and then the
   parameters through the constructor, whose checks run in the order the op lists them, so an op with two faults is
   refused for the first: the suite holds that on vectors whose name and parameters, point and direction, direction
   and angle, angle and distance, and centre and factor are each both at fault. A constructor's refusal is carried as
   `Geometry` with the algebra's own case, and never re-derived: a non-finite parameter (`NonFiniteCoordinate`,
   `NonFiniteAngle`, `NonFiniteDistance`, `NonFiniteTranslation`), a zero-length direction (`ZeroDirection`), a
   factor that is not positive (`NonPositiveFactor`, `−0.0` included), a negative radius (`NegativeRadius`), and
   `NotANumber`, which `Motor.ofScrew` answers for a rotation or a screw whose motor holds a NaN (its products
   overflowed: an axis's point of an overflowing scale, say). Where a motor overflows the constructor refuses rather
   than answering an infinity, and this package carries what it answers; D85 does not rule on which a constructor
   should do. **One refusal is this package's own, an addition to D85 item 9's list:** an op whose object would hold
   a NaN in any coefficient is refused, `NotANumber` with the object's name, the state unchanged. Every op but
   `Remove` can reach it: an object at an overflowing scale holds an infinity, which is a value (a point at
   `(1e200, 0, 0)` holds `+∞` at `n∞`, and its `Add` is accepted), and a product that meets it can form
   `∞ − ∞` or `0 × ∞`; the vectors hold one case of each of the six ops that reaches it, under `apply`,
   `canApply` and `applyAll`. It follows the algebra's own record (D87, D88), that an `Ok` holds no NaN and an
   infinity is a value. It is a test of the result, `Double.IsNaN` over the object's coefficients, and no
   arithmetic, and `canApply` agrees because it is `apply` discarded. *Rejected:* an `Ok` that holds a NaN, which
   would let a history carry a NaN silently into every later op and leave a state no comparison can hold, since a
   NaN equals nothing. A law holds that no `Ok` state holds a NaN over drawn histories whose parameters overflow
   and underflow, on .NET and under node. A refused op answers its refusal and the caller keeps the state it had:
   `applyAll` hands back the same value, held by a law over every refusal its drawn histories meet. `canApply` is `apply` with its state discarded, so the two agree
   both ways by construction and by law. `applyAll` applies each op in turn, in the order given, and a refused op
   leaves the state as it was and is noted with its index; the next op is applied after it. *Rejected:* halting at the
   first refusal, as `ColumnOps.applyAll` does, which would make one bad op of a script cost every op after it. A
   recorded stream holds no refused op: `OpStream.append` chains no record for one, and `OpStream.replay` halts at
   one, so `applyAll` continuing past a refusal never touches a recorded history.

6. **The codec, and the float rule.** An op is canonical JSON under `Rendering.CrossHost`: `$type` (`add`, `remove`,
   `rotate`, `translate`, `scale`, `reflect`, `screw`; a form's `point`, `pointPair`, `line`, `circle`, `plane`,
   `sphere`), `name` as the caller's `Encode` spells it, and one member for each parameter, named for it (`point`,
   `first`, `second`, `direction`, `centre`, `radius`, `normal`, `distance`, `angle`, `vector`, `factor`). **A float
   is its IEEE-754 binary64 bit pattern in sixteen lowercase hex digits, a triple's three concatenated, and a NaN is
   `7ff8000000000000`:** D51 item 7's rule, which `LinAlgWire` (`writeBits`, `floatsOf` in
   `src/Fuaran.Compute.LinearAlgebra/Wire.fs`) and `RegressionWire` follow. *Rejected:* the `JFloat` spelling
   `ColumnOps` writes, which collapses `-0` to `0` and spells a non-finite float as a quoted token
   (`Fuaran.Core.Wire`'s `Canonical.tryWrite` says so), so no round trip of the bits; D51 rejected the shortest
   round-trip decimal for the same reasons. The writer is this package's own: `LinAlgWire`'s is internal to a package
   this one may not reference. **A non-finite parameter travels as its pattern** (an infinity as itself, a NaN as the
   one pattern) and `apply` refuses it: the codec carries what was written and decides nothing. `decode (encode op)` is
   `op` bit for bit, `-0.0` included, on every op free of a NaN, and a NaN reads back as `Double.NaN`. Malformed text
   is refused by name: `not json: …`, `expected an object`, `$type: …`, `unknown op: …`, `unknown form: …`,
   `members: expected …, found …` (the member set is closed), a member that is not its count of lowercase hex digits,
   and `name: …` with the caller's reason. The wire-surface baseline shows parametric members only, and a case reads
   every encoded op and form for that: each member is one of the names above, and each float member is sixteen or
   forty-eight digits, never a coefficient list (D85 item 10). What the bit pattern costs, and that the rule is an
   open point for the owner, is G3 below.

7. **The stream witness, and undo.** `GeometricOps.streamWitness naming` is `{ Apply = apply; Encode = encode naming;
   Decode = decode naming }`, so `OpStream.append`, `verifyChain` and `replay` chain, verify and replay a history, and
   the chain hashes each op's canonical text. A recorded stream, written to text and read back, replays to the same
   bits and verifies (a law); undo is truncate and replay, and a stream truncated to `k` records replays to exactly
   the state its first `k` appends produced (a law, every `k` of every drawn history).

8. **The inverse of each op.** `invert op s` is guarded twice. By `canApply op s`, as `ColumnOps.invert` is since
   Phase 181: an op `s` refuses has no inverse, and its refusal is the answer. And by the state after the op, as the
   compaction is guarded (item 9): an inverse that state would refuse is not answered, and the answer is
   `InverseRefused` carrying the refusal `apply` would give it. Every transform can reach the second guard. A
   `Scale` by a positive factor whose reciprocal overflows (below about `5.56e-309`: `1e-310` about the origin,
   applied to the origin's point, is accepted, and its inverse would be the scale by `+∞`) is answered
   `InverseRefused (Geometry NonFiniteFactor)`. And any transform whose state, undone, would hold a NaN is answered
   `InverseRefused (NotANumber …)`: from the point `(1, 2, 3)`, a translation by `(1e300, 0, 0)`, a rotation about
   an axis through `(1e300, 0, 0)`, a reflection in a plane at `1e300`, a screw `1e300` along its axis, and a
   scale of `(1, 0, 0)` by `1e-308`, each accepted, each with an inverse the state after it refuses. The vectors
   hold one of each (`invert-refused-inverse-…`), a wrong port with the guard removed is red on them, and a law
   holds over drawn histories with parameters of an overflowing scale and subnormal ones that an inverse `invert`
   answers is accepted by the state after the op, and that one it refuses is refused there as it says. The search
   that found these is the suite's wide draw (item 12) and a larger one run outside the suite and not committed;
   neither is a proof that no other input reaches the guard, and none needs to be: the guard reads the answer.

   | op | inverse | exact? |
   |---|---|---|
   | `Add (n, _)` | `Remove n` | yes: the remove deletes the one entry the add placed, and every other entry and its place are untouched. Held bit for bit by a law over drawn histories; the argument is the proof, the law a search that fails to break it |
   | `Remove n` | none: `NotInvertible "Remove"`, answered before the guard | — |
   | `Rotate (n, p, û, θ)` | `Rotate (n, p, û, −θ)` | no |
   | `Translate (n, t)` | `Translate (n, −t)` | not in general, and exact in value where every product and partial sum of the two sandwiches is a double (below) |
   | `Scale (n, c, s)` | `Scale (n, c, 1.0 / s)`, one rounded division | no |
   | `Reflect (n, n̂, δ)` | itself | no: the op is its own inverse, and the state does not come back bit for bit (105.5 units in the last place of the object's largest coefficient on the stated sample) |
   | `Screw (n, p, û, θ, d)` | `Screw (n, p, û, −θ, −d)` | no |

   *A translation.* A translation undone is the object it was in value WHERE every product and partial sum of the
   two sandwiches is a double: then each is exact, and the inverse of an exact operation on exact values is exact.
   Being quarters is not enough; their size matters. For quarters in `[−8, 8]` (a point, a point pair or a sphere of
   them, translated by a vector of them) every such product and sum has few enough significant bits, and a law
   holds the round trip under the float identity on drawn objects in that range and no other. It is not bit for
   bit: a coordinate of `-0.0` comes back `+0.0`, since `-0.0 + t − t` is `+0.0`
   (`applyAll-a-translation-undone-on-a-point-of-quarters`). Quarters near `1e5` are not enough: a point pair of
   them, translated and back, is not the pair it was in value
   (`applyAll-a-translation-undone-on-a-pair-of-large-quarters`): somewhere in its sandwiches a product or a partial
   sum is no longer a double. Outside quarters it is not exact either: the point
   `(0.1, 0, 0)` translated by `(0.7, 0, 0)` and back has `x` `0.09999999999999998` (`3fb9999999999998`) for
   `0.1` (`3fb999999999999a`) and the `n∞` coefficient `0.0049999999999999906` for `0.005000000000000001`
   (`applyAll-a-translation-undone-on-a-point-of-tenths`).

   *Why `Remove` has none.* Its inverse needs the object, and the object a state holds after transforms is
   coefficients, which no op carries (D85 item 10); an `Add` from parameters read back from it would be a computed,
   rounded approximation, and a read-back is a computation this package does not make. Undo of a `Remove` is truncate
   and replay, which is exact. *Rejected:* negating the direction of a rotation or a screw in place of the angle and
   the distance (the same motion in exact arithmetic; the angle is the parameter the op names as its amount);
   carrying the removed object in the inverse (a coefficient on an op). Each pinned figure is stated in the API
   documentation of `invert`, as a pin and not a bound, and in item 10.

9. **The compaction.** `GeometricOps.compact ops s` answers a list of ops in which each run of adjacent rigid ops of
   one object is one op of the list. Two translations merge into the translation by the sum of their vectors; two
   rotations or screws about one axis, given by the same point and the same direction bit for bit, merge into the
   screw by the sum of their angles and of their distances, a rotation when neither has a distance. Each sum is one
   rounded addition, the earlier op's parameter first. A scale and a reflection end a run, as do an `Add`, a `Remove`,
   an op on another object, a refused op (kept as given), and a rigid op that does not merge; a merged op the run's
   starting state would refuse (a sum that overflowed) is not formed, and the run ends there. **A run that mixes axes
   is not compacted** (point G1 below). It is a function the caller calls: nothing in `apply`, the codec or the
   witness calls it. The compaction is the last section of the source, after the witness, so nothing above it can
   name it, and a source-reading case holds that and that none of `apply`, `canApply`, `applyAll`, `encode`,
   `decode` or `streamWitness` mentions it.

10. **The pinned figures, and what each rests on.** Each bounded law is an identity of exact arithmetic that binary64
    holds only to rounding, and PINS the largest error over ONE stated sample, which is a measurement of that sample
    and no bound on the function, since other samples exceed it (the second table): seed 731, 1,200 objects (two
    hundred of each kind, coordinates multiples of a quarter in `[−8, 8]` with zeros of both signs, directions of small integers,
    angles of full precision in `(−3, 3)`, factors in `(0.5, 4.5)`), with no margin, because the answers are pinned
    bits and every host measures the same ones. Three figures, all held: **(i)** in units in the last place of the
    reference coefficient, D85 item 7's form; **(ii)** absolute, where the reference coefficient is zero; **(iii)** in
    units in the last place of the largest magnitude among the reference object's coefficients. Figure (i) is
    dominated wherever a reference coefficient is a rounding residue of a zero, and is then no statement of accuracy;
    (iii) says how far the object moved at its own scale (point G2).

    | law (sample of 1,200 at seed 731) | (i) coefficient ulps | (ii) absolute | (iii) object ulps |
    |---|---:|---:|---:|
    | a rotation undone by its inverse | 31525197391593470 | 7.216449660063518e-15 | 65.5 |
    | a translation undone by its inverse | 13510798882111488 | 4.440892098500626e-16 | 58.0625 |
    | a scale undone by its inverse | 640 | 2.842170943040401e-14 | 50 |
    | a reflection undone by itself | 4503599627370496 | 7.105427357601002e-15 | 105.5 |
    | a screw undone by its inverse | 1280 | 1.2434497875801753e-14 | 135.5 |
    | a run of 2 to 4 translations, compacted | 6755399441055744 | 0 | 32 |
    | a run of 2 to 4 rotations and screws about one axis, compacted | 7808 | 0 | 71 |

    The eight other seeds (1, 2, 3, 590, 727, 729, 1000, 4242), figure (iii), in that order, with the range of (i) and
    of (ii) across them; the suite holds every figure of every seed as recorded:

    | law | (iii) at the eight seeds | (i) range | (ii) range |
    |---|---|---|---|
    | rotation | 100, 154, 98, 122, 284, 94, 176, 114 | 768 .. 22517998136852480 | 3.18e-15 .. 2.49e-14 |
    | translation | 26.5, 36, 19.75, 89, 21, 16, 29, 20 | 1024 .. 49539595901075460 | 2.22e-16 .. 1.07e-14 |
    | scale | 26, 36, 29, 44, 40, 53.5, 59, 44 | 10240 .. 1.125899906842624e17 | 2.66e-15 .. 4.22e-14 |
    | reflection | 51, 49, 64, 80, 96, 48, 63, 71 | 512 .. 29273397577908224 | 1.78e-15 .. 1.42e-14 |
    | screw | 134, 64, 64, 121, 120, 181, 602.5, 76 | 1340 .. 27021597764222976 | 4.00e-15 .. 2.49e-14 |
    | translations compacted | 60, 24, 47, 108, 48, 61, 28, 16 | 2251799813685248 .. 22517998136852480 | 0 .. 8.88e-16 |
    | rotations about one axis compacted | 72, 141, 64, 149, 76, 51, 345, 164 | 4096 .. 29243826285682932 | 0 .. 2.22e-16 |

    So a seed-731 figure is no bound on the function: a screw's sample at seed 1000 reaches 602.5 object ulps against
    135.5; the package's documentation and `STABILITY.md` call each figure a pin of its sample, not a bound on the
    function.
    What each claim rests on: each figure is a measurement of its sample; `Add`'s exact inverse rests on the
    argument in item 8 and a law; the exact laws (item 11) are tested on drawn inputs and not proved.

11. **The law family.** `GeometricAlgebraOpsLaws.laws` runs seventeen laws: ten exact (a recorded stream replays to
    the same bytes; a truncated stream replays to its prefix's state; `canApply` answers `Ok` exactly when `apply`
    does, with the same refusal; a refused op leaves the same value; each refusal is the one its op names, in order;
    the codec's round trip, and malformed text refused by name; an `Add` undone by its inverse; an `Ok` state holds
    no NaN; an inverse is answered only where the state after the op accepts it; a translation of quarters in
    `[−8, 8]` undone, under the float identity) and the seven bounded laws of item 10, which draw nothing from the seed they are run
    at. The NaN law and the inverse law draw parameters of an overflowing scale (`1e150`, `1e200`, `−1e300`) and
    subnormal ones (`5e-324`, `1e-310`), and factors whose reciprocal overflows. The suite runs them at seed 590 and
    at eight other seeds (1, 2, 3, 727, 729, 731, 1000, 4242), 200 iterations each, and the node leg at seed 590; before landing
    they were run green at the nine seeds of the record's list with 2,000 iterations each, in one process outside the
    suite, and that run was not captured to a committed file.

12. **The rules record and the wrong ports.** Every function is written once over `Rules<'N>`, an internal record of
    eighteen members, one rule each: `Fold` (how `applyAll` runs a list), `Refused` (what a refusal answers), `Find`
    (where a name's object is), `Place` (where a new object goes), `After` (what follows a transform: nothing),
    `Angle` (radians, as given), the five versors' constructors, `Check` (`canApply`), `Inverse`, `Merge`
    (the compaction's merge), `FloatText` (the codec's float), `ChainText` (the bytes the chain hashes), `Holds`
    (that an object holds no NaN) and `Undoes` (that the state after an op accepts its inverse). A public function
    passes `Rules.shipped`; each go-red witness is the shipped function with ONE member changed, never a
    second implementation. Twenty-three wrong ports, against the nine functions that compute (`apply`, `canApply`,
    `applyAll`, `invert`, `compact`, `encode`, `decode`, `streamWitness`, `GeometricState.tryFind`): (a) a replay in
    reverse order; (b) a refused op that empties the state, and a replay that halts at the first refusal; (c) the
    versor applied to the first object in order; (d) a normalisation after each transform; (e) a run of rigid ops of
    one object composed into one motor and applied once; (f) a rotation about the origin; (g) a screw that translates
    first and rotates after; (h) a scale about the origin; (i) four inverses that negate or invert the wrong parameter
    (a rotation's direction for its angle, a screw's angle without its distance, a scale by its factor, a translation
    with one component kept); (j) a float written by the shortest round-trip decimal formatter; (k) the state in the
    order of addition; (l) the chain hashed over the declared-order rendering; (m) an angle read as degrees; (n) a
    `canApply` that reads the name and no parameter; the correctly rounded sine and cosine in a rotation, and in a
    screw; a compaction that merges rotations about different axes; a NaN admitted to a state; and an inverse
    answered without asking whether the state after the op accepts it. The table is computed: for every port and
    every function, a committed vector of the function is red under the port, or the pair is listed unable with its
    reason, and a drawn search of 9,200 cases finds no case the port answers otherwise: 400 indices drawn narrow and
    400 wide, one case of each function at each, and 400 deep, five cases each (the deep search, below). The narrow
    draws hold zeros of both signs, coordinates whose squares underflow to a zero, coordinates whose squares are
    subnormal, coordinates of full precision and non-finite ones; the wide draws add coordinates of an overflowing
    scale and factors and radii that are subnormal or whose reciprocal overflows. A port that changes a drawn answer and fails no committed vector of that function fails
    the case; three such were found while the vectors were written:
    the two drawn cases that showed (f) and (m) changing `compact` are committed (`drawn-2-compact`,
    `drawn-6-compact`), and an authored vector was added for (k) on `apply`
    (`apply-add-a-name-that-orders-first`). Five more were found by the wide draws once an object that would hold a
    NaN was refused, and are committed: (c) changing `canApply`, `invert` and `compact` (`drawn-wide-13-…`), and
    the NaN port changing `compact` and `streamWitness` (`drawn-wide-9-compact`, `drawn-wide-0-streamWitness`).

    *The deep search.* A port that changes an object's bits changes whether a LATER op is refused `NotANumber`, and so
    what `compact`, `applyAll`, `streamWitness`, and `canApply` and `invert` over a history answer. The narrow and wide
    draws, one op over a fresh object, did not reach that, and the listing said (h) could not move `compact` because a
    scale is never merged; that was false (a scale is kept as given, but the object it leaves decides whether the next
    translations are refused, and a refused op ends a run). The deep draws reach it: at each of 400 indices, one object
    with coordinates up to about `4e160`, a first transform drawn at that scale (a scale by `10^k` with `k` in
    `[−30, 30]`, a rotation, a translation, a reflection or a screw), then two or three small translations and two
    rotations about one axis; `compact`, `applyAll` and `streamWitness` over that list, and `canApply` and `invert` of
    the second op over the state the first reaches through the functions under test (the case's `history`). All
    twenty-three ports were run over all five deep functions. What moved, by deep cases changed of 400: (a) `applyAll`
    290; (b) the emptying port `compact`, `applyAll` and `streamWitness` 317 each, `canApply` and `invert` 230 each;
    (b) the halting port `applyAll` 316; (d) `applyAll` and `streamWitness` 173 each, `compact`, `canApply` and
    `invert` 1 each; (e) `applyAll` 225; (f) `applyAll` 102, `streamWitness` 77, `compact` 64, `canApply` and `invert`
    5 each; (h) `applyAll` and `streamWitness` 39 each, `compact` 10, `canApply` and `invert` 9 each; (i) the
    translation's inverse `invert` 170; (j) `streamWitness` 253; (l) `streamWitness` 234; (m) `applyAll` 208,
    `streamWitness` 187, `compact` 40, `canApply` and `invert` 4 each; (n) `canApply` 174; the correctly rounded
    rotation `applyAll` and `streamWitness` 89 each, and screw 11 each; the NaN port `compact` 118, `applyAll` and
    `streamWitness` 137 each, `canApply` and `invert` 101 each; and nothing for (c), (g), the other three (i) ports,
    (k), the compaction's merge and the inverse guard. Three of those were pairs the listing still called unable, and
    each now has a committed deep vector and left the listing: (h) on `compact` (`drawn-deep-38-compact`) and (d) on
    `canApply` and `compact` (`drawn-deep-220-canApply`, `drawn-deep-220-compact`).

    The reasons are of three kinds, and the listing says which: **the function does not read the member** (an
    argument from the source: the member is not on its path); **the function answers a verdict or the ops it was
    given, never an object, and the port moves no verdict** (an argument, held for (k) alone: every name is found
    wherever it is placed, and the object is the one placed); and **a search, not an argument** (for (g) and the two
    correctly rounded ports on `canApply`, `invert` and `compact`: since a NaN verdict reads the object a transform
    builds, a port that changes that object could move a verdict, and no drawn case, narrow, wide or deep, does).
    None is a proof, and the listing never says "cannot".

    *The correctly rounded port.* `apply`, `applyAll` and `streamWitness` answer coefficients a rotation's or a
    screw's sine and cosine reach, and each has vectors at twice the arguments where Phase 725's law file records the
    pinned `sinCos` a unit from the correctly rounded value; each is red under the port. `canApply`, `invert` and
    `compact` answer a verdict or ops, a sine and a cosine make no constructor refuse, and no drawn case moves a NaN
    verdict (a search); `encode`, `decode` and `tryFind` read no versor (an argument). Those six are listed unable,
    with those reasons.

13. **No float literal in a comparison.** The package validates by the algebra's constructors and names no threshold.
    The census of float literals in `ConformalTests.fs` reads the algebra package's modules only, so the same rule is
    held over this package's operation modules by a case of its own: they hold three float literals, `0.0` (the
    rotation's distance), `1.0` (the inverse's reciprocal) and `NaN` (the value a decoded NaN pattern reads as), and
    none stands in a comparison. The law family compares against literals, as a law must, and is outside the census
    as the algebra's is. No host mathematics function is called, and a source-reading case holds that.

14. **Exactness.** `GeometricAlgebraOpsOperations.exactness` has a `Pinned` row for each of the twelve published
    functions, held to the surface both ways. Nothing is `HostRounded`.

**The choices this entry makes that D85 left open, each with its alternative.** The names (item 2); the state's order
by the caller's comparison (against the order of addition, which a state rebuilt from another history would not
share); `Rotate` as the screw of no distance (against composing three versors here); a constructor's refusal carried
inside one case (against a case for each of the algebra's); `applyAll` that applies past a refusal (against halting);
the bit-pattern float (against `JFloat`); a non-finite parameter carried on the wire and refused by `apply` (against a
codec that refuses it); the member names; an object that would hold a NaN refused (against an `Ok` that holds one:
item 5); an inverse guarded by `canApply` and by the state after the op (against an unguarded one, and against
one guarded by `canApply` alone, which answered the scale by `+∞`); the angle and the distance negated (against the direction); `1.0 / factor` (no parametric alternative exists); `Remove` with no
inverse (against an inverse carrying the object); the runs that compact and the bit-equality that makes two axes one
(against a geometric test of coincidence, which would need a threshold); the third figure beside each pinned one (point
G2); the sample and its seed.

**What D85 does not settle, and what this phase did.**
- **G1 — the screw parameters of a composed motor.** Compacting a run that mixes axes needs the point, direction,
  angle and distance of the composed motor's screw. D85 item 4 defines the motor's logarithm and no such read-back,
  and its closing paragraph does not list it among the derived quantities a later phase chooses. Every route
  computes here (norms, divisions, a cross product), which item 9 rules out for replay. Options: (a) the algebra
  package publishes the read-back under an amendment to D85, its expression chosen by measurement, and the
  compaction calls it; (b) this package computes it; (c) mixed runs are not compacted. **Shipped: (c).**
- **G2 — the unit of a bound on an object.** D85 item 7's "units in the last place of the reference value", read
  coefficient by coefficient, is dominated by rounding residues of zero (item 10). Options: that figure alone; the
  object's scale alone; both. **Shipped: both, beside the absolute figure.**
- **G3 — which float rule "the repository's other codecs" means, and what it costs.** D85 item 9's sentence matches two
  rules in this tree: the `JFloat` decimal spelling `ColumnOps` and the dataframe codecs write, and the bit pattern
  `LinAlgWire` and `RegressionWire` write (D51 item 7). Item 6 takes the bit pattern, because it is the only rule in
  the tree that carries a `-0.0` parameter to the same bits, and a point stores a `-0.0` coordinate as given, so a
  decimal `-0` read as `0` would replay to another state. **What it costs:** the package exists so that a history
  of transforms can be written and read, by a person or a model as well as by a program, and an encoded op is not
  something anyone writes by hand: the rotation by `0.75` about the axis `(0, 1, 1)` through `(1, −2, 0.5)` is
  `{"$type":"rotate","angle":"3fe8000000000000","direction":"00000000000000003ff00000000000003ff0000000000000","name":"b","point":"3ff0000000000000c0000000000000003fe0000000000000"}`.
  **What a readable rule would have to guarantee** to replace it: every double to one text on every host, and that
  text back to the same double, the shortest round-trip spelling being correct on every host's parser; `-0.0`
  written and read as itself; and a NaN and both infinities carried, as distinct tokens no string can alias.
  Options: (a) the bit pattern (shipped); (b) a readable rule meeting those three, which no codec of this
  repository has yet; (c) both, a readable text beside the bits. **Shipped: (a), fixed with this package's first
  wire baseline unless the owner revisits it before a `v0.47.0` tag.**

**Ladder.** `proofs.json` gains `geometric-algebra-ops-pinned-by-recorded-rules` at `tested`, listing this family's
cases, each held by `GeometricAlgebraOpsTests.fs`; the algebra's and the objects' rows, which said the op package did
not exist, now name this row. Nothing is proved.

**Registration.** The solution, `pack.ps1`, the publish workflow (whose `Fuaran.Compute.*` policy covers the id), the
README's roster row, `proofs/coverage-exclusions.json` (`pinned-order-law-tested`), both API baselines, the wire-surface
roster, the boundary rule, the node leg, which compiles the vectors and the laws under Fable and holds the rendered
document to the committed file byte for byte, the law export and `gate-reads.json`'s account of the node leg.

**What a consumer does.** Nothing: no existing package changes. `STABILITY.md` records the new package under the
`0.47.0` draft, with the counts of laws and vectors no baseline shows. The version stays `0.47.0`.

**Tidy.** Reviewed every file this change touches: the three sources, the package's project file, the two suites, the
test project file, `WireSurfaceTests.fs`, `ComputeBoundaryTests.fs`, `LawVectorExport.fs`, the node leg's program and
project, `LinearAlgebraTests.fs`, `proofs/coverage-exclusions.json`, the solution, `pack.ps1`, the publish workflow,
`gate-reads.json`, `proofs.json`, `README.md`, `STABILITY.md` and this file. *Made true:* two `proofs.json` claims that
said the op package did not exist. *Reused instead of minted:* the algebra suite's cell writers and its two
off-by-one arguments, the conformal suite's source census and cell text, and the elementary vectors' correctly rounded
references. *Considered and kept, each a copy this change could not avoid:* the bit-pattern writer and reader (the
original is internal to `LinAlgWire`, in a package this one may not reference); the Park and Miller draw in the law
family and in the vectors (the algebra's is private to its law module, and a published package cannot read test
code); the list of malformed texts in the law family and in the vectors (one is a law the package runs, the other the
vectors' inputs, and the vectors compile under Fable without the package's internals); the names of the algebra's
refusal cases in the vectors (the algebra suite's mapping is private to a file this phase does not own). *In the
second round:* the boundary case's `contains` checks became one exact set, so nothing it named twice survives;
`invert` reuses `apply` and the `Check` member for its second guard rather than a second test of the inverse's
parameters; the vectors' drawn generator gained a wide mode rather than a second generator, its narrow stream
unchanged, so no committed drawn vector moved. *In the third round:* the deep search is a third entry over the same
generator (two members added to it, its other streams unchanged), and a case's `history` is applied by the function
record's own `apply`, so no second replay was written; the seven bounded laws are renamed "within the pinned
figures of its sample", their failure "past the pinned figures of its sample", which moves the law names the suite
records beside the other seeds' figures and no committed vector.

## 2026-11-24 — D89: the geometric product is specialised to nine pairs of operand types where a measurement showed the gain — a rotor with a rotor, a motor with a motor, a translator with a rotor, and both products of the sandwich of a rotor, a translator and a motor with a point; each a filter of the one table in its order, taken only on finite operands that hold zeros off their types, and so the general product's bits on every input; every one kept by the margin of two and a half stated before measuring, on its designated runs, at 4.55 to 13.64 times the general product on .NET and 5.42 to 21.14 under node, with one pair below the margin on .NET when every committed run is pooled; one float literal in a comparison D85 item 6 does not list, admitted by name; the law family gains one law that reads internals; no signature, vector or API baseline line moved

**Context.** Phase 730. The general product of `Fuaran.Compute.GeometricAlgebra` (D86) folds all 1,024 signed terms of
D85 item 3's table whatever its operands hold; the objects (D87) and the versors (D88) fill few coefficients. This
entry asks whether a product over only the coefficients two operand types carry is worth having, and holds any it
keeps to the general product's bits. It moves nothing D85 fixed but one addition, named in item 11: the guard's
comparison of a coefficient with `0.0`, which D85 item 6's list of such comparisons does not hold. Every fold still
starts at `+0.0`, visits its terms in the table's order, enters each as a rounded product and a rounded sum, and
skips no term on a coefficient's value.

**Premise check** (on `f8a358f`, the tree this phase was cut from). `main` holds Phases 727, 728 and 729: the general
product and its 1,542 vectors, the six object types and the five versor types. This file opened with D88, then D87,
D86 and D85. Every typed function reaches the general product through the `Geometric` member of `Arithmetic`, which
`Arithmetic.shipped` bound to `Multivector.geometricProduct`, on operands it widened from their records. The
benchmarks directory holds the node leg's timing program, through which every Compute kernel since Phase 593 has been
measured on both hosts, and committed results. The tree declares `0.47.0` and no `v0.47.0` tag exists. All five held.
One was sharpened: the wrong ports of the objects' and the versors' suites replace the four products of the arithmetic
wholesale, so a specialisation behind `Arithmetic.shipped.Geometric` leaves their tables as they are (item 6).

**The margin, stated before any specialisation was measured.** The general product was measured first and committed
(`benchmarks/results/2026-11-24-i7-8650u-phase-730.md`, three runs on each host). Across those runs one case's median
moved by a factor of up to 2.34 on .NET and 2.09 under node, on a machine that was not quiet. So a specialised
product is kept only where, on BOTH hosts, the GREATEST of its three run medians is below the LEAST of the general
product's three run medians on the same operands divided by **2.5**: a pessimistic speed-up of at least two and a half
times, which the general product's own spread in the baseline did not reach (the final tree's own spread did, on
.NET: item 8). A product that does not clear it on either host is deleted in the change that measured it, and named
below with its numbers.

**The route, chosen before any specialisation was measured: a table filtered from the general one when the module is
first read.** A specialisation is a pair of operand types, each a mask of the blades it carries, and the terms of
`Basis.geometric` whose left blade the left type carries and whose right blade the right type carries. A filter keeps
order, so the specialised fold visits its terms in the general order by construction, not by review.
*Rejected:* hand-written products (some hundred terms each, the order and every sign a reviewer's to check against a
table that already exists, and a second copy of it); products generated at build time (straight-line code with no
table, possibly faster, but a generator and a generated source the build and the Fable leg would both have to carry,
for a gain this phase has not measured); and inlining (an inlined function still loops over its table, and this
phase knows of no compiler on either host that turns a loop over a filtered array into straight-line code; it did
not try one).

**What was edited after measuring.** The two paragraphs above were committed in `42bb282`, before any specialisation
was measured. After it, `42ff4d3` edited three passages in them: the words "in the baseline" inserted into the margin
paragraph's clause on the general product's own spread, the parenthetical on the final tree's own spread after them,
and the wording of the rejected inlining route. The rule's own sentence, from "a specialised product is kept only
where" to "divided by **2.5**", is as `42bb282` committed it.

**Ruled.**

1. **Which products, and which pairs.** The geometric product alone, on nine pairs of types, because those are the
   products the typed functions form most and the ones the measurement covered: a rotor with a rotor (`Rotor.compose`,
   `Rotor.interpolate`), a motor with a motor (`Motor.compose`, `Motor.interpolate`), a translator with a rotor
   (`Motor.ofRotorTranslator`; `Motor.ofScrew`, `Motor.ofLine` and `Motor.ofMatrix` build their motors through these
   pairs), and both products of the sandwich `(V X) Ṽ` of a rotor, a translator and a motor with a point
   (`applyToPoint`). The second product of a sandwich is specialised to the type the first one's answer has, the
   blades its terms reach, with the versor's type, which is its reverse's. *Measured and not written:* the products of
   a point with a sphere. No typed function forms their geometric product; the power against a sphere forms their
   scalar product, which folds 32 terms and costs about a twentieth of the geometric product already (96 to 108 ns on
   .NET, 4.1 to 4.7 µs under node), and the join forms their outer product, 243 terms. A specialisation of either
   would serve no measured cost worth the code, so none was written. *Not measured, and so not specialised:* the
   sandwiches of a dilator and a reflection, the sandwiches of a versor with a point pair, a line, a circle, a plane
   or a sphere, and the outer product and the left contraction anywhere. Each is a pair a later phase can measure and
   add to the list by the same rule.
2. **How a typed operand says its type.** `Multivector` carries, internally, the mask of the blades its type carries:
   `Conformal.widen` gives a value widened from a record its type's mask, set in the loop that writes it; the reverse,
   the grade involution and negation keep it, so a typed value's reverse has its type; a specialised product's answer
   has the mask of the blades its terms reach; and every other value (`init`, `ofScalar`, a sum, a scale, a grade
   projection, a dual or an undual, the general product's answer) carries every blade, which is always safe. Equality
   and the hash do not read it. The typed functions' geometric product, `Specialised.geometric`, is
   `Arithmetic.shipped.Geometric`: it finds the operands' pair in the list, and folds the pair's terms; a pair not in
   the list, or a value of no type, is the general product. The published `Multivector.geometricProduct` stays the
   general product for every input.
3. **Why dropping a term moves no bit on finite operands.** A term the specialised fold drops has a factor on a blade
   its operand's type does not carry, and that coefficient is a zero of either sign. With the other factor finite, the
   term's product is a zero of either sign. A fold from `+0.0` never holds `−0.0`: under round-to-nearest, `r + p` and
   `r − p` are `−0.0` only when `r` is `−0.0` and `p` is a zero of the right sign, and an exact cancellation is `+0.0`,
   so by induction from the start no accumulator is ever `−0.0`. And `r ± 0` is `r` for `r` a positive zero, a finite
   number that is not zero, an infinity or a NaN. So each output's accumulator passes the dropped terms unchanged, and
   the specialised fold, which visits the kept terms in the general order, reaches the general fold's value at every
   output. Where it is NOT safe, and what is done:
   - (a) *A non-finite operand.* `0 × ∞` and `0 × NaN` are NaN, which the general fold adds and the specialised one
     would drop. So the specialised fold is taken only where every coefficient of both operands on a blade its type
     carries is finite (`not (IsNaN x || IsInfinity x)`); anything else is the general product. A vector shows it
     (`Rotor.applyToPoint-of-an-infinite-coefficient`), and so does the law, which draws an infinity and a NaN on each
     carried blade.
   - (b) *A typed operand holding −0.0 or a number off its type's blades.* From the code it cannot reach a specialised
     product: a mask is set only by `widen`, which writes `+0.0` off the type's blades; by a specialised product, whose
     unreached outputs are the fold's `+0.0` start; and by the reverse, the grade involution and negation, which may
     negate those zeros to `−0.0`, still a zero. A `−0.0` there drops a zero term like a `+0.0`. The guard checks it
     anyway: an operand that holds anything but a zero of either sign off its type's blades takes the general path, so
     the bits do not rest on that bookkeeping, and a wrong mask could cost speed but never a bit. The law draws such an
     operand.
   - (c) *An output every term of which is dropped.* The general fold answers `+0.0` there, a sum of zero terms from
     `+0.0`; the specialised one never writes it and answers its start, `+0.0`. A case holds it.
4. **The equality law.** `geometricAlgebraLaws` gains one law, the thirty-seventh, run on .NET and under node: for each
   of the nine pairs, operands of its two types in eight regimes — drawn doubles; zeros of both signs half the time;
   doubles so small that a product of two underflows to a zero of either sign; subnormals; the largest finite doubles
   of either sign; an infinity on one carried blade; a NaN on one; and a number off the type's blades — and the
   specialised product equal to `Multivector.geometricProduct` in all sixty-four bits of every coefficient, zero signs
   included. It also holds that the specialised fold answered, with its answer's type, in each of the five finite
   regimes, so that the law is never met by the general product answering for it. The suite runs it at five more
   seeds, 300 iterations each, on .NET.
5. **The go-red witnesses**, each the shipped specialised product with one thing changed, through
   `Kernel.specialisedUnder`, and each red on that law: a fold seeded from `−0.0`, where a dropped `+0.0` term that the
   general fold adds would have moved the sign of a zero (and a case shows the `−0.0` it leaves on two rotors of zeros);
   the terms in reverse order; the guard removed; and a fused multiply-add.
6. **The functions it serves, and the wrong-port tables.** A specialised product that answers a NaN at every
   coefficient moves exactly eleven typed functions on their committed vectors: `Rotor.compose`, `Rotor.interpolate`,
   `Rotor.applyToPoint`, `Translator.applyToPoint`, `Motor.compose`, `Motor.interpolate`, `Motor.applyToPoint`,
   `Motor.ofRotorTranslator`, `Motor.ofScrew`, `Motor.ofLine` and `Motor.ofMatrix`; the suite holds that list. Under the
   other-order witness and the fused witness each of the eleven is red on a committed vector. The seeded witness is red
   on four of them (`Motor.ofMatrix`, `Motor.ofRotorTranslator`, `Rotor.applyToPoint`, `Rotor.interpolate`) and the
   guard's absence on one (`Rotor.applyToPoint`): a negative zero left by a seeded fold survives only where every kept
   term of a coefficient is a zero, and the guard matters only on a non-finite operand, which only that one served
   function's vectors hold. Both are red on the equality law, which draws exactly those operands, and the suite pins
   the four sets. No vector was added for them: the law file changes only for a published product (below). The
   tables of `ConformalTests.fs` and `VersorTests.fs` hold unchanged, counts included: each wrong port there replaces
   the four products of the arithmetic with the general fold under a wrong rule, so under it no function reaches a
   specialised product, which is still the shipped arithmetic with one member changed; and under every other port the
   specialised product answers the general product's bits.
7. **Not published, and what moved.** The typed functions call the specialised product internally and answer the
   bits they answered before: no signature moved, no law vector moved, no row of `GeometricAlgebraOperations` was
   added, and the API baseline did not move. One published thing did move: `GeometricAlgebraLaws.laws`, which is
   public, answers thirty-seven laws where it answered thirty-six. The thirty-seventh reads the package's internals
   (the specialised pairs, their masks and the specialised product, none of them published), so a host that ports
   the package has nothing to implement for it and certifies against the vectors as before. It is in the published
   family because that is how the node leg runs it: the leg runs `GeometricAlgebraLaws.laws` under node, and a law
   outside the family would run on .NET alone. `STABILITY.md` records the change under the `0.47.0` draft.
   Publishing the specialised product itself would have meant a function taking two masks or nine named functions,
   law vectors of its own and a surface to keep, for a caller who can already have the same bits from
   `Multivector.geometricProduct` or from a typed function. *Rejected:* publishing.
8. **The measurement after, and the verdict.** Taken on the final tree (`73f765d`), three runs on each host, by the
   procedure and commands of the baseline. Every figure here and in the results file is computed from the committed
   captures by `benchmarks/results/2026-11-24-i7-8650u-phase-730/figures.py`, except the script-host figures of item 9
   (4.5 to 4.6 µs, 2.0 to 2.4 µs and 4.6 µs), which are reported from runs that were not captured to files and cannot
   be recomputed, and item 9's figures of 2026-11-25, which are read from the captures that item names.
   - *The rule, as `42bb282` stated it before measuring, on its designated runs* (the three final runs on each host,
     the general product's medians from the same runs, on the same operands): the least general median over the
     greatest specialised one.

     | pair | .NET general (ns) | .NET specialised (ns) | ratio | node general (ns) | node specialised (ns) | ratio |
     |---|---:|---:|---:|---:|---:|---:|
     | `rotor * rotor` | 2422, 4494, 4644 | 178, 167, 166 | 13.64 | 103000, 105992, 103008 | 5125, 5125, 5064 | 20.10 |
     | `motor * motor` | 2189, 4493, 7471 | 261, 229, 248 | 8.39 | 103000, 105992, 103000 | 8499, 8251, 8249 | 12.12 |
     | `translator * rotor` | 2276, 4513, 4458 | 171, 164, 166 | 13.29 | 108992, 107000, 194992 | 5062, 5062, 4938 | 21.14 |
     | `rotor * point` | 2253, 4409, 4532 | 189, 178, 174 | 11.89 | 103000, 106000, 106016 | 5564, 5500, 5312 | 18.51 |
     | `(rotor point) * reverse rotor` | 2160, 6326, 4490 | 264, 474, 229 | 4.55 | 104000, 106000, 103000 | 8499, 8252, 8251 | 12.12 |
     | `translator * point` | 2142, 7032, 4525 | 181, 337, 177 | 6.35 | 104000, 104000, 103984 | 5562, 5375, 5376 | 18.69 |
     | `(translator point) * reverse translator` | 2407, 4461, 5100 | 219, 210, 236 | 10.18 | 104000, 103000, 103000 | 7125, 6875, 6876 | 14.46 |
     | `motor * point` | 2407, 4608, 5030 | 242, 220, 248 | 9.69 | 104000, 103000, 110000 | 7626, 8375, 7752 | 12.30 |
     | `(motor point) * reverse motor` | 2207, 4430, 5049 | 436, 412, 442 | 4.99 | 136000, 104000, 103008 | 19000, 16500, 16498 | 5.42 |

     **The verdict:** every pair clears 2.5 on both hosts on the designated runs, the least being 4.55 on .NET
     (`(rotor point) * reverse rotor`) and 5.42 under node (`(motor point) * reverse motor`), so by the rule as stated
     every pair is kept and none is deleted. The rule is not changed after the fact.
   - *Against the baseline runs' general medians instead*, the least ratio is 3.85 on .NET (`(rotor point) * reverse
     rotor`, 1826 over 474) and 5.32 under node. The baseline did not measure the two translator-sandwich pairs; their
     general figures come only from the final tree's own runs.
   - *Pooled over every committed default-configuration run of the final tree* (on .NET the three designated runs and
     the four `pgo-final` runs, seven in all; under node the three designated runs): eight pairs clear 2.5 on both
     hosts, and **one does not: `(motor point) * reverse motor` on .NET, 2.08** (the least general median 2207 over
     the greatest specialised median 1059, in `pgo-final-1`). Under node it pools at 5.42.
   - *In one process*, the general median over the specialised median of the same run, least over those runs: 4.35 on
     .NET (`(motor point) * reverse motor`, `pgo-final-1`) and 6.24 under node (the same pair, `specialised-node-3`);
     every other pair's least is 8.17 or more on .NET and 12.12 or more under node.
   - *The specialised fold has a slow mode of its own on .NET.* `(motor point) * reverse motor` ran at 412 to 466 ns
     in six of the seven pooled runs and at 1059 ns in `pgo-final-1`, and at 814 ns in one run of the intermediate tree
     (`interim-cfe5cdb-dotnet-3`); `(rotor point) * reverse rotor` at 228 to 264 ns in five and 474 and 484 ns in two;
     `translator * point` at 177 to 189 ns in six and 337 ns in one. Each slow run is about twice its pair's other runs,
   the proportion item 9 establishes for the general product's two speeds, and these were not measured separately.
   - *The general product's own spread on the final tree exceeds the margin.* On .NET its median for `motor * motor`
     moved from 2189 to 7471 ns across the three designated runs, a factor of 3.41, and pooled over seven runs another
     pair's moved by 3.53; under node the largest factor is 1.82. So a ratio taken ACROSS runs, as the rule takes it,
     can move by more than the margin with the machine alone on .NET: the rule's ratio is pessimistic where the general
     side ran fast and generous where it ran slow, and the same-process ratios above are the figures that do not mix
     two processes.
   - *Bits.* In every pooled run on both hosts each specialised arm's checksum is its general arm's, and every row's
     checksum is the baseline's.
   - *The typed functions*, by the middle of each three medians, base over final: the six served functions the
     benchmark measures are 6.33 to 8.73 times faster on .NET (`Motor.applyToPoint` from 5656 to 894 ns) and 6.69 to
     11.72 times under node (`Motor.applyToPoint` from 251008 to 37504 ns). `Sphere.power`, which no pair serves, ran
     at 279, 265 and 453 ns on the final tree's designated runs against 266 to 289 on the base's, and at 266 to 283 in
     the four `pgo-final` runs; the 453 is one run, not measured separately (item 9 gives the likely cause).
9. **What the .NET measurements found beside the verdict, and what was done.** Three things, in the order they were
   found; the captures of each intermediate tree are kept beside the results file, named for their commits.
   - *An inlined second copy of the general fold.* The first build of the dispatch inlined the general fold into it as
     its fallback (`030f016`). `Motor.applyToSphere`, which no pair serves, then ran at 9488 ns in one run of three
     where the base ran it at 5053 to 5236. The fallback and the published `Multivector.geometricProduct` were made
     one compiled function, `Kernel.general` (`cfe5cdb`); that tree ran it at 5110 to 5250 ns in three runs of three.
   - *Bookkeeping on paths no pair serves.* `Sphere.power`, which no pair serves, ran at 308 to 329 ns on the second
     intermediate tree against the base's 266 to 289: each widening folded its indices into a mask in a second pass,
     and each signed permutation walked thirty-two blades to move one. The widening now sets the mask in the loop that
     writes the coefficients, and the dual and the undual answer a value of no type (`73f765d`). The final runs are of
     that tree.
   - *A function no pair serves measured slower on the final tree on .NET; the cause, established afterwards, is the
     machine and not the tree.* In this
     harness, `Motor.applyToSphere` ran at 4338 to 5323 ns in all seven committed runs of the base tree, and at 9388
     to 12045 ns in five of the seven committed runs of the final tree (4664 and 4880 in the other two). The general
     product on `point * sphere` ran at 1754 to 2586 ns in all seven base runs and at 4361 to 4747 ns in six of the
     seven final runs (2537 in the other). With dynamic profile-guided optimisation off (`DOTNET_TieredPGO=0`, two
     runs) or tiered compilation off (`DOTNET_TieredCompilation=0`, one run), the final tree ran the general product
     at 2043 to 2240 ns and `Motor.applyToSphere` at 4403 to 4626 ns; the base tree was not measured in those
     configurations. Marking the general fold `AggressiveOptimization` left it at 4374 to 7190 ns in three runs and
     was not kept. Outside the harness, in a script host timing the general product alone, the base tree ran it at 4.5
     to 4.6 µs in three processes of three and this phase's trees at 2.0 to 2.4 µs in two of three (4.6 in the third);
     those runs were not captured to files. So in this harness the base ran it at its faster cost in seven runs of
     seven and the final tree at the slower in five of seven.
   - *The cause, measured on 2026-11-25* (`benchmarks/results/2026-11-25-i7-8650u-phase-730-two-speeds.md` and the
     four captures beside it). The two speeds are the product's thread running alone on its physical core, or
     sharing it with another busy thread (this processor has two hyper-threads per core). Three findings, in order:
     (1) the JIT compiles `Kernel.general` to the same 293 bytes, through the same tiers, in a slow process (4729 ns)
     as in fast ones, so the difference is no code-generation decision; (2) a process changes speed when a blocking
     collection lets the scheduler move its thread, after its code is final (2054 → 4688 → 4687 → 4741 → 2132 ns in
     one process); (3) with the probe pinned to two processors on different cores, busy threads on their two sibling
     hyper-threads put every process at the slow speed, on BOTH trees (final 3872 to 7257 ns, base 4807 to 5138),
     while with the siblings idle both ran at the fast one (final 1866 to 2327 ns, base 1925 to 2785). So the base's
     seven fast runs and the final tree's five slow runs in the harness were taken under different background load
     on a machine that was not quiet. The general product such a function calls runs at the same two speeds on both
     trees; `Motor.applyToSphere` itself was not run under pinning. The same effect accounts
     for the pooled 2.08 of item 8, which pairs a run at one speed with a run at the other; within one process the
     least ratio is 4.35. The PGO-off and tiering-off runs above are three runs taken at one time and show no property
     of those settings: of thirty later processes with `DOTNET_TieredPGO=0`, one ran at the slow speed (4469 ns).
   - *What a comparison across processes on this machine needs from now on.* Either the benchmark process pinned to
     one logical processor per physical core, with each sibling idle and the background load recorded beside the
     capture, or only same-process ratios, which this effect cannot skew.
   - *Withdrawn:* the first version of this entry said every pair clears the margin against the general product's
     faster speed; `pgo-final-1` refutes it for `(motor point) * reverse motor`: 1059 ns against that pair's general
     medians in the base tree's seven runs, 1760 to 2667 ns, is 1.66 to 2.52.
10. **What was NOT measured.** One machine (an Intel Core i7-8650U laptop, Windows 11, not quiet); one .NET runtime
    (10.0.15) and one node (v22.20.0, Fable 5.0.0); time per call only, not allocation, which the specialised fold does
    not change (one array of thirty-two for the answer); warm code only, not a cold start or the cost of building the
    nine tables when the module is first read; no other host the package's answers are transcribed to; the base tree
    with profile-guided optimisation or tiering off; whether the specialised fold's own slow mode (item 8) has the cause
    item 9 establishes for the general product's (its proportion matches; it was not run under pinning); and the
    general product's own cost
    under node, about fifty times .NET's, which is not this phase's to explain and is recorded as measured.
11. **The one float literal this entry adds to a comparison.** The specialised product's guard, `Kernel.admits` in
    `Multivector.fs`, compares each coefficient its operand's type does not carry with zero, `x = 0.0`, and sends an
    operand for which it fails to the general product. D85 item 6 lists every comparison in which `0.0` may be named,
    and its list does not hold this one. It is admitted here, as written, for three reasons: it is exact (true for
    `+0.0` and `−0.0` and nothing else); it is no threshold and no tolerance, since it compares a coefficient with zero
    and with nothing a caller passes; and it decides no answer, since it chooses between two paths that answer the same
    bits (item 3), so a wrong verdict could cost time and never a bit. The census of float literals counts it, and its
    account cites this item. D85 is not reworded.

**The choices this entry makes, each with the alternative.** The margin (against a smaller one the general product's
own spread could have produced); the route (against hand-written, generated and inlined products); the
geometric product on nine pairs (against every product on every pair, unmeasured); a mask on the multivector (against
a new member of `Arithmetic`, which would have moved every wrong-port table, and against telling a type from the
values, which is skipping a term on a coefficient's value); the guard on every operand (against trusting the masks),
and its one comparison with `0.0` (item 11); and not publishing (item 7).

**Ladder.** `proofs.json` gains `specialised-products-are-the-general-product` at `tested`, listing this family's
cases, each held by `SpecialisedProductTests.fs`; the objects' row now names it where it said the specialised products
did not exist. Nothing is proved: item 3's argument is an argument, and the law tests it on drawn operands.

**What a consumer does.** Nothing: no signature, answer, vector or API baseline line moves. The one published change
is that `GeometricAlgebraLaws.laws` answers thirty-seven laws, the new one reading internals that a port has nothing
to implement for (item 7); `STABILITY.md` records it under the `0.47.0` draft. The version stays `0.47.0`.

**Tidy.** Reviewed every file this change touches: `Multivector.fs`, `Conformal.fs`, `Motion.fs`,
`GeometricAlgebraLaws.fs`, the package's project file, `ConformalTests.fs`, `VersorTests.fs`,
`GeometricAlgebraTests.fs`, `SpecialisedProductTests.fs`, the test project file, `proofs.json`, the bench and the node
leg's project and program, `README.md`, `STABILITY.md`, the results file with its captures and the script that
computes its figures (`figures.py`, added so that no captured figure here is read by eye), and this file. *Moved, not copied:*
the five versors' index sets left `Motion` for `Indices`, where the objects' already were, so that the specialised
pairs read the one copy; `Motion` names them from there. *Made one:* the published geometric product and the fallback
are one function (item 9) rather than two inlined copies. *Reused instead of minted:* the versors' suite's dispatch of
a vector to its function, made `internal` (two words) rather than copied into the new suite. *Considered and kept:*
`Multivector.zero` and `grade` now build through `Kernel.adopt`, which canonicalises a NaN their inputs never hold —
kept, so that one function builds every untyped value; three earlier notes about the Elementary sources concern files
this change does not touch.

## 2026-11-24 — D88: rotors, translators, motors, dilators and reflections ship as versors of `Fuaran.Compute.GeometricAlgebra`, written to D85 and choosing nothing D85 fixed — five typed wrappers and a module of each one's name, a union of refusals for a matrix read back, and eight more cases of `GeometryRefusal`; every function written once over Phase 728's arithmetic, which gains a member for each new rule, so every go-red witness is the shipped function with one member changed; the five expressions D85 left to this phase each chosen by measurement against an exact or high-precision reference; 533 more vectors, each replayed bit for bit by a second implementation; forty-nine wrong ports held against fifty-eight functions, by a red vector, by a function that does not read the member, or by a drawn search with a stated reason; five bounded laws, each pinned to one stated sample whose maximum is its bound; and the package's first reference, to `Fuaran.Compute.Elementary`

**Context.** Phase 729. D85 recorded how a versor is built, applied, composed, exponentiated and read from a matrix;
D86 shipped the products and D87 the objects a versor is applied to. This entry ships the versors. It chooses no sign,
scale, order, orientation, branch, normalisation or threshold: each is D85's, and where a line below looks like a
choice the item that makes it is cited. What D85 left to this entry, by its own closing section, is the names, the
cases of each refusal, each bound with its sample, and the expression for five derived quantities it defines without
one. Each of those is listed under *Choices* at the end, with the alternative.

**Premise check** (on `c40c94f`, the tree this phase was cut from). `main` holds Phases 727 and 728: the multivector,
the products, the six object types, the meet, the join and incidence. `Elementary.sinCos` and `Elementary.atan2` are
in `api/Fuaran.Compute.Elementary.txt`. D85 item 4 fixes each versor's coefficients, the sandwich, composition, the
exponential, the logarithm, interpolation and the matrix; item 5 the versor's normalisation. This file opened with
D87, then D86, then D85. The tree declares `0.47.0` and no `v0.47.0` tag exists. All five held.

**Ruled.**

1. **The surface, and the names.** Five record types, five modules and one union, in the namespace of the package id.
   Each record holds one `float` field for each coefficient D85 item 8's table gives its type, in index order, named
   for its blade as D87's are, and `Scalar` for the coefficient of `1`: `Motor`, `Dilator`, `Rotor`, `Translator`,
   `Reflection`. They are declared widest first, as D87 declared a circle before a line, so a record expression that
   names only a rotor's four fields reads as a rotor.

   | module | functions | D85 |
   |---|---|---|
   | `Rotor` | `ofAxisAngle axis angle`, `exp bivector`, `log`, `interpolate t first second`, `compose b a`, `toMatrix` | item 4 |
   | `Translator` | `ofVector t`, `compose b a`, `toMatrix` | item 4 |
   | `Motor` | `ofRotorTranslator rotor translator` (`T R`), `ofRotor`, `ofTranslator` (the widenings), `ofScrew point direction angle distance`, `ofLine line angle distance`, `exp bivector vector`, `log`, `interpolate t first second`, `compose b a`, `toMatrix`, `ofMatrix tau matrix` | item 4 |
   | `Dilator` | `ofFactor factor`, `ofCentreFactor centre factor`, `compose b a`, `toMatrix` | item 4 |
   | `Reflection` | `ofNormalDistance normal distance`, `compose b a`, which answers a `Motor` | item 4 |

   Each module also has `applyToPoint`, `applyToPointPair`, `applyToLine`, `applyToCircle`, `applyToPlane` and
   `applyToSphere`; `normalise` (item 5); and `toMultivector` and `ofMultivector`. Seventy-one functions. A Euclidean
   bivector is the triple of its coefficients on `e1∧e2`, `e1∧e3` and `e2∧e3`, in index order, as D87 made a point a
   triple; a motor's bivector `B + v ∧ n∞` is that triple and the triple of `v`. A matrix is a `float[]` of sixteen,
   column-major, a fresh array each way (D85 item 9), and `Motor.ofMatrix` answers a motor and a factor.
   *Rejected:* one `apply` over a union of the five versors (D85 item 8 puts a type's functions in a module of its
   name); an `apply` over a `Multivector` (it is two calls of `Multivector.geometricProduct`, and "any other mixture
   is taken on the multivector" already says so); `Reflection.toMatrix` (D85 item 4 names four types that convert; a
   reflection reverses orientation and is what `ofMatrix` refuses); a dilator widened to a motor (a motor does not
   carry index 9); `Reflection.ofPlane` (the op list of D85 item 9 is parametric, and no reader has asked).

2. **What is refused.** `GeometryRefusal` gains eight cases, because a caller binds a line's read-back to
   `Motor.ofLine` in one `Result`: `NonFiniteAngle`, `NonFiniteTranslation`, `NonFiniteFactor`, `NonPositiveFactor`
   (a factor that is zero, of either sign, or negative), `NonFiniteBivector` (an exponential's argument, or one whose
   norm overflows, as a direction whose norm overflows is `NonFiniteDirection`), `NonFiniteParameter` (an
   interpolation's `t`), `NegativeSquaredNorm` (item 5: a versor's divisor that is not positive; only a dilator can
   reach it) and `NoRotationPlane` (the logarithm where the bivector part is zero and the scalar part is not
   positive). A point a screw passes through and a dilation's centre are `NonFiniteCoordinate`; a screw's distance
   and a reflection's are `NonFiniteDistance`; an axis and a normal are D87's two direction cases. **Every parameter
   is checked in the order it is passed, before anything is computed** (D87 item 2), and a vector of each
   constructor holds it: a screw through a point of the largest doubles with a distance that is no number is
   refused for its distance. `Motor.ofLine` checks that the line's weight is a divisor, then the angle, then the
   distance, and only then reads the line's nearest point, whose own refusals it passes on. **An `Ok` never holds a
   NaN.** A matrix read back has its own union, `MatrixRefusal`, because D85 item 4 asks for the measure that failed
   and D87 ruled that a case of `GeometryRefusal` carries none: `InvalidThreshold`, `NotSixteenEntries`,
   `NonFiniteEntry`, `NotAffine`, `ZeroDivisor`, `NonFiniteDivisor`, `NotOneLength of float`,
   `NotOrthogonal of float`, `ReversesOrientation of float` and `NotANumber`, tested in that order.

3. **Where D85 was read, and what was read.** The second of these is the sign of a zero, and is recorded as a
   reading that can be revisited. None of the others is a sign, a scale or an order.
   - *A logarithm at a scalar part of zero is refused.* D85 item 4 answers `B′` itself "where `|B′| = 0` and
     `c > 0`" and refuses "at `B′ = 0` with `c < 0`". At `c = 0`, of either sign, the rotor is the zero rotor, no
     plane is determined either, and the answer is the same refusal: the test is `c > 0`, which item 6 lists.
   - **The sign of a zero in an exponential: a reading of a point D85's words leave open, and recorded as one.**
     D85 item 4 writes `exp B = cos φ + (sin φ / φ) B`, `1 + B` at `φ = 0`, and `1 + v ∧ n∞`. *One reading* takes
     item 3's "a sum … is one rounded operation for each coefficient" onto those formulas: each coefficient is a sum
     with a zero, so a `-0.0` of `B` or of `v` comes back `+0.0`. *The other,* which ships, PLACES each coefficient
     on its blade: the scalar and the bivector part lie on different blades, no coefficient of the answer is a sum of
     two terms, and a `-0.0` stays. So `Rotor.exp` of zeros keeps the sign of each, and a motor's exponential at
     `φ = 0` holds the three coefficients of `v` as they are given (and `+0.0` on the three bivector blades, which
     `1 + v ∧ n∞` does not carry). Four grounds, each tested before it was written here. (a) D85's own coefficient
     forms place and do not add a zero: a rotor's `s × â2` at index 11 and a translator's `−(0.5 × t_i)` stand
     beside the formulas `cos ½θ − sin ½θ (â I₃)` and `1 − ½ t ∧ n∞`. (b) Placed, `exp (−B)` is the reverse of
     `exp B` bit for bit, zeros included; summed, it is not. (c) Placed, the logarithm of `exp (v ∧ n∞)` returns `v`
     bit for bit, a negative zero included; summed, it does not. (d) Placed, the zeros of `Rotor.exp` of
     `−½θ â I₃` about a coordinate axis have the signs `Rotor.ofAxisAngle` gives; summed, they are all positive. A
     case holds (b), (c) and (d) over a drawn sample with zeros of both signs, and shows each fail under the port
     that sums. **This is the phase's reading and no ruling of D85's**, whose words are not changed. It can be
     revisited before the version is tagged, at the cost of the five vectors the summed form fails, which a case
     holds to be these and no others: `Rotor.exp-of-zeros-keeps-each-sign`,
     `Rotor.exp-of-a-bivector-with-a-zero-of-each-sign`, `Rotor.exp-of-that-bivector-negated-is-the-reverse`,
     `Rotor.exp-at-a-cosine-a-unit-from-correctly-rounded` (its indices 10 and 13 hold `-0.0`: a negative quotient
     times a coefficient that is `+0.0`) and `Motor.exp-of-a-vector-alone-is-one-and-the-vector`.
   - *A motor's logarithm at `|B′| = 0`, `c > 0` answers the three `e_i ∧ n∞` coefficients themselves*, as a rotor's
     answers `B′` itself: nothing is divided by the scalar part, so a translator that is no unit answers its vector
     part as stored (item 5). **For a motor that is no unit the answer is therefore discontinuous there**: this
     branch scales with the motor, and the general branch, whose quotients divide the scale out, does not. Dividing
     by the scalar part in this branch alone would be a normalisation nobody asked for (item 5); a caller normalises
     first.
   - *A value D85 says is used as it is given is used once.* A screw's `R` is the rotor about `û` as it is, not
     divided by its norm a second time, and `p` is not moved along the axis; a line's direction over its weight, an
     axis and a reflection's normal are divided once. Each is a member (item 4), because a second normalisation
     moves a unit in the last place and is a port a vector must fail. A dilator's centre enters only through
     `−(0.5 × c_i)`, an exact halving: there is no norm and no division for a port to repeat, and none is added.
   - *A versor's matrix is read as a point is.* The image of `n0` under the sandwich has a weight, its index 1; the
     twelve entries are the images of `e1`, `e2`, `e3` and `n0`, their indices 2 to 4, each divided by that weight
     (item 4: "coordinates are read by dividing by the weight"). So a versor that is no unit names the same
     similarity as its unit multiple, and a versor whose image of the origin has weight zero is refused.
   - *What a versor is applied by is one rule.* The sandwich, the matrix and `Ṽ₁` in an interpolation all read the
     member `VersorReverse`, so the port that takes the inverse fails a vector of each.
   - *An exponential refuses what a constructor refuses.* It takes parameters, so it answers `Result`.

4. **Every function is written once, and its arithmetic is an argument** (D87 item 6). `Motion` holds each function
   that computes, taking Phase 728's `Arithmetic` first, and a published function is that function under
   `Arithmetic.shipped`. The record gains twenty-one members, each one rule: `SinCos` and `Atan2` (the pinned pair
   and the pinned arc tangent); `VersorReverse`; `Compose` (`B A`); `Screw` (the order of a screw's four factors);
   `ByQuotient` (a quotient formed once and then multiplied in: `sin φ / φ` and `φ / |B′|`); `IsZeroAngle` and
   `IsZeroBivector` (the two exact tests of item 6); `DilatorParts`; `Between` (the versor an interpolation takes
   the logarithm of, as formed); `SplitAlong` and `MotorAcross` (items 6 and 7 below); `MatrixIndex` (column-major);
   `Dot` and `Orientation` (a column's dot product and the triple product, as folds); `RotorOfRotation` and
   `Canonical` (item 8 below); `UnitAsGiven` and `PointAsGiven` (a direction already of length one, and a screw's
   point, each used as it is); `Placed` (a coefficient of an exponential, placed and never added to a zero); and
   `NormaliseOnce` (a normalisation's division of every coefficient by one scalar, applied once).
   Fifty-eight functions compute and have such a door. Thirteen have none: the ten
   widenings and copies back, `Motor.ofRotor` and `Motor.ofTranslator`, and `Translator.ofVector`, which is one
   exact halving and a negation for each component and has no rule a port could change. The expressions measured
   and not chosen stay in the package, internal, beside the chosen ones (`Forms`), because each is a wrong port the
   suite shows red.

5. **The constructors are D85 item 4's coefficients.** A rotor asks `Elementary.sinCos (0.5 × θ)` once and holds `c`,
   `−(s × â3)`, `s × â2`, `−(s × â1)`; a translator holds `1` and `−(0.5 × t_i)`; a screw is
   `T_(d û) ((T_p R) T̃_p)`, formed in that order, its second translator from the three products `d × û_i`; a
   dilator about the origin is `r = √s`, `q = 2 × r`, `(s + 1) / q` and `(s − 1) / q`, and about a centre
   `(T_c D₀) T̃_c`. `Elementary.exp`, `ln` and `pow` are called nowhere, and a source-reading case holds that the
   package names `Elementary.sinCos` and `Elementary.atan2` once each and nothing else of that package.

6. **A motor's exponential: how `v` is split, chosen by measurement.** D85 gives
   `(1 + v∥ ∧ n∞)(cos φ + (sin φ / φ)(B + v⊥ ∧ n∞))` and leaves the two parts of `v`. All three candidates project on
   the unit bivector `Û = B / φ`, one division for each coefficient, with its reverse in place of its inverse.
   *Rejected before measuring:* the inverse of `B` itself, whose divisor `B ∗ B̃` underflows: for a bivector of norm
   `5·10⁻¹⁷⁰` it is refused as a zero divisor, and the exponential of a small angle would be refused with it. *The
   sample:* seed 729, 2,000 draws; three coefficients of `B` in `(−1, 1)` times one power of two from `2⁻³⁰` to `1`,
   three of `v` in `(−1, 1)` times one from `2⁻¹⁰` to `2¹⁰`. *The reference:* the same formula in exact rationals, with
   a root to 400 binary places and a sine and cosine to about 470, written from their series over integers.

   | form | largest error of a coefficient, in units in the last place of its reference | mean of each draw's largest |
   |---|---:|---:|
   | `v∥ = (v ∧ Û) Ũ`, `v⊥ = v − v∥` (**ships**) | 792.8 | 4.40 |
   | `v⊥ = (v ⌋ Û) Ũ`, `v∥ = v − v⊥` | 1,467 | 5.31 |
   | each by its own projection | 4,889 | 10.21 |

   At eight other seeds (1, 2, 3, 590, 727, 728, 1000, 4242) the third form's mean is larger than the first's at
   every one; the first two are within a factor of six of each other in their largest error and change places
   (2,422 against 1,548 at seed 1; 700 against 4,049 at seed 728). The choice is the stated sample's, by both
   columns, and no claim that the first form is better at every input. The large figures are coefficients near zero
   beside larger ones.

7. **A motor's logarithm, chosen by measurement.** `φ = atan2 |B′| c` from the rotation's part, and `B` the
   coefficients of `B′` times `φ / |B′|`, as a rotor's (D85). For `v`: the motor's coefficient at index 30 is
   `(sin φ / φ)(v∥ ∧ B)`, so `v∥ ∧ n∞` is that one coefficient times `Ũ′`, over `|B′|`, with `Û′ = B′ / |B′|`; and the
   three `e_i ∧ n∞` coefficients `m` are `(sin φ / φ) v⊥ + c v∥`, so `v⊥` is the same quotient `φ / |B′|`, formed
   once with the bivector's, times what is left. *The sample:* the one above. *The reference:* the bivector and the
   vector themselves; the motor is their exponential in the high-precision arithmetic above, rounded correctly.

   | what is left of `m` | largest error, units in the last place | mean of each draw's largest |
   |---|---:|---:|
   | `m_i − c × v∥_i` (**ships**) | 2,048 | 4.83 |
   | the projection `(m ⌋ Û′) Ũ′` | 2,048 | 6.17 |

   At the eight other seeds the first form's mean is the smaller at every one (2.0 to 3.1 against 3.5 to 7.1) and its
   largest error is never the larger (128 to 2,048 against 128 to 4,096).

8. **The matrix, both ways, chosen by measurement.**
   - *The sixteen entries of a versor.* Item 3 above: the frame over the origin's weight. *The sample:* seed 729,
     2,000 screws through a point of coordinates up to `2¹⁰` and 2,000 dilators about such a centre with a factor
     from `2⁻⁶` to `2⁶`, built by the shipped constructors. *The reference:* the same expression in exact rationals on
     the same coefficients.

     | form | screws: largest, mean | dilators: largest, mean |
     |---|---:|---:|
     | the images of `e1`, `e2`, `e3`, `n0` over the weight (**ships**) | 5,188; 10.8 | 42.3; 3.75 |
     | the images of four points, read as points, and their differences | 5,598,000; 5,923 | 30,410; 556 |

     The same order at each of the eight other seeds (393 to 2,663 against 625,000 to 4,708,000 for screws).
   - *A rotor read from a rotation.* The nine entries are each divided by the factor, once. The four sums
     `1 ± r₀₀ ± r₁₁ ± r₂₂`, each from `1.0` left to right, are `4w²`, `4x²`, `4y²`, `4z²` of the rotor
     `(w, x, y, z) = (cos ½θ, sin ½θ â)`; the component of the **largest** sum, the first in a tie, is half its root,
     and each other is a sum or a difference of two entries over twice that root. *The reference:* an integer
     quaternion's rotation in exact rationals, rounded to doubles, against the quaternion over its norm.

     | divided by | rotations at large: largest error | within `10⁻⁶` of a half turn |
     |---|---:|---:|
     | the component of the largest sum (**ships**) | 13.3 units in the last place; `1.8·10⁻¹⁶` absolute | `1.8·10⁻¹⁶` absolute |
     | the scalar's sum always | `0.87` absolute, 19 of 2,000 no number | `0.98` absolute, 206 of 2,000 no number |

     The rotor's sign is then D85 item 4's: negated when its scalar part is negative, and at a scalar part of zero
     when its axis component of largest magnitude, the first in a tie, is negative. A half turn's matrix is
     symmetric, so its scalar part is the quotient of an exact zero and is zero exactly.
   - *The two statistics of the test,* each against `τ` by D87's one function, so at `τ = 0` each is decided from
     the signs of its numerator and its divisor. *One length:* for the second and the third column,
     `(c_j · c_j − c_0 · c_0) / (c_0 · c_0)`, about twice the relative difference of the lengths. *Orthogonal:* for the
     pairs `(0,1)`, `(0,2)`, `(1,2)`, `(c_i · c_j) / (|c_i| × |c_j|)`, the cosine, D49's norms in the divisor. Neither
     has a unit, so one `τ` means the same at any size of entry: over 400 rotations (seed 729) with every entry
     times `2⁻⁴⁰⁰`, `10⁻¹⁰⁰`, `10⁻³`, `1`, `10³`, `10¹⁰⁰` and `2⁴⁰⁰`, the largest length statistic is between `6.3·10⁻¹⁶` and
     `7.3·10⁻¹⁶` and the largest cosine between `2.2·10⁻¹⁶` and `3.2·10⁻¹⁶`, where the dot product unscaled runs from
     `3·10⁻²⁵⁷` to `1.5·10²²⁵`. And at `τ = 0` they are exact wherever the products are: of 2,000 integer similarities
     (an integer quaternion's rotation times its squared norm times an integer, seed 729) all 2,000 are accepted,
     where columns divided by the factor first accept 169 and lengths compared through their norms 1,282. The
     squared lengths are the fold's, plain sums of squares, and **no threshold is added to guard them** (D85 item 6
     allows none). What that costs was measured on a rotation about `(1, 2, 2)` by one radian with every entry
     times a power of two, at `τ = 2⁻⁴⁰`. *Large:* read up to `2⁵¹¹` (about `6.7·10¹⁵³`); from `2⁵¹²` a square
     overflows and the answer is `NonFiniteDivisor`. *Small, three bands:* read down to `2⁻⁵¹⁹` (about
     `5.8·10⁻¹⁵⁷`); from `2⁻⁵²⁰` to `2⁻⁵³⁶` (about `2.9·10⁻¹⁵⁷` to `4.5·10⁻¹⁶²`) the squares are subnormal, their
     rounding is the statistic, and the matrix is refused `NotOrthogonal` or `NotOneLength`, **a name that does
     not describe it**, with a measure that is rounding and no geometry, `−2⁻¹⁴` at `2⁻⁵³⁰` (and at two scales of the
     seventeen, `2⁻⁵²²` and `2⁻⁵³⁵`, it is accepted by the accident of that rounding); from `2⁻⁵³⁷` (about
     `2.2·10⁻¹⁶²`) a square is zero and the answer is `ZeroDivisor`. The edges are this rotation's and move by a
     binary order or two with another. An earlier form of this item said entries were read "from about `10⁻¹⁵⁴`"
     and "refused by name beyond"; the small side of that was wrong, and five vectors now hold one scale in each
     band. A caller whose entries are that small scales the matrix first, and the function's documentation says so.
     A length statistic cannot underflow to a zero (a difference that is not zero is at least a unit in the last
     place of the divisor); a cosine can, and two vectors hold each sign of it refused at `τ = 0` and accepted at
     the least threshold above it. The orientation is the sign of `(c_0 × c_1) · c_2`, each component of the cross
     product two rounded products and their difference.

9. **The laws.** `GeometricAlgebraLaws.laws` gains nine, thirty-six in all, on .NET and under node.
   - *Bit for bit, on exact inputs:* a translator composed with its opposite is `1` and three `+0.0`; the
     reflections in `x1 = 0` then `x2 = 0` are `−1.0` at index 10 and `+0.0` at the seven other indices of a motor,
     and likewise `e3 e2` at 13 and `e1 e3` (`+1.0`) at 11; a dilation by `4^k`, `k` from `−3` to `3`, about a centre
     of multiples of a quarter takes `up x` to `up (c + s (x − c))` with each coefficient divided by `s`.
   - *Under the float identity:* those three products against the rotor's coefficient formulas at `c = 0`, `s = 1`,
     which hold a negative zero where the product holds none; both counts are asserted.
   - *Within a bound,* item 10.

10. **Each bound is one sample's maximum.** D85 item 7 fixes the form: units in the last place of the reference,
    absolute where the reference is zero, the largest over a stated, seeded sample, any margin stated. **Each
    figure below is the stated sample's, seed 729, 2,000 draws, with no margin, and is no bound on the function.**
    A bounded law is pinned to that sample and draws nothing from the seed the family is run at (D87 item 11), so no
    law goes red at another seed; a case holds every row below, and all thirty-six laws were run at each of these
    seeds with 2,000 iterations and held.

    | seed | `R R̃` against `1` | `log (exp B)`, rotor | `log (exp (B, v))`, motor | rotor interpolation at 0 and 1 | motor interpolation at 0 and 1 | matrix against versor, on points |
    |---:|---:|---:|---:|---:|---:|---:|
    | 729 (stated) | 2.5; `0.25 × 2⁻⁵²` at zero | 3 | 512 | 39,755 | 49,763 | 1,534 |
    | 1 | 3 | 3 | 256 | 52,884 | 900,352 | 5,213 |
    | 2 | 3 | 3 | 4,096 | 16,782 | 278,847 | 1,758 |
    | 3 | 2.5 | 3 | 4,096 | 17,132 | 25,831 | 5,101 |
    | 590 | 2 | 3 | 512 | 17,650 | 1,916,928 | 18,433 |
    | 727 | 2.5 | 3 | 1,024 | 338,947 | 62,672 | 6,375 |
    | 728 | 2.5 | 3 | 256 | 29,901 | 194,923 | 40,052 |
    | 1000 | 2.5 | 3 | 1,024 | 22,046 | 51,828 | 75,202 |
    | 4242 | 2.5 | 3 | 512 | 10,014 | 65,536 | 6,783 |

    *The samples.* A rotor: an axis with components in `(−1, 1)` and an angle in `(−8, 8)`; its product with its
    reverse against `1` at index 0 and zero at the thirty-one others (the absolute figure is `0.25 × 2⁻⁵²` at every
    seed). A bivector: as in item 6; its vector likewise. An interpolation's ends: two such rotors, or two screws
    through points of coordinates in `(−8, 8)` by angles and distances in `(−8, 8)`; at `t = 0` against the first
    and at `t = 1` against the second. The matrix: a rotor, a translator by components in `(−8, 8)` and a dilator
    about such a centre by a factor in `(1, 8)`, each applied to one point through the sandwich and through its
    sixteen doubles as a `Matrix` of `Fuaran.Compute.LinearAlgebra` (`Matrix.init 4 4 (fun i j -> m[4 * j + i])`,
    which the suite alone references) times the column `(x, 1)`; the versor's coordinate is the reference. *Why the
    interpolations' figures are large:* a coefficient near zero beside others near one carries an absolute error of
    a unit in the last place of one, which is many of its own; the same is true of a coordinate near zero in the
    last column. The reference of the first five is exact by construction: `1`, the bivector drawn, the versor given.
    **The two interpolation figures are pins of one sample and no statement of accuracy**: each is measured at
    `t = 1` against coefficients near zero, and at another seed it reaches tens of millions of units in the last
    place (one measurement outside this record's nine seeds, at seed 2026, gave 42,743,482).

11. **The vectors, and what a wrong port is held to.** The law file holds 1,542: the 1,007 of Phases 727 and 728,
    whose lines are unchanged (the last of them gains the comma that separates it from the next), 533 over the
    seventy-one functions, and two of Phase 728's `PointPair.normalise` and `Line.normalise`, appended last (below).
    Of the 533, 234 are three drawn configurations through every function, a versor an
    operation takes being its coefficients times a drawn weight, so that it is no unit; 270 are authored: each
    refusal and the order of refusal, each side of each boundary, the laws on exact inputs, the half turns, a
    statistic at, below and above its threshold, each sign of a cosine that underflows, and the arguments at which
    Phase 725's law file records the pinned sine, cosine and arc tangent a unit from the correctly rounded double (a
    rotor's angle is twice one; an interpolation is steered to one), a product that underflows to a negative zero,
    and a rotation at five scales of entry; and 29 are cases a search found, kept because
    a wrong port fails them where it failed no earlier vector of that function.

    **What the suite holds of the wrong ports is computed** (D87 item 10), over forty-nine ports and the fifty-eight
    functions that compute, 2,842 pairs. The ports are Phase 728's twenty and these twenty-nine, each
    `Arithmetic.shipped` with one member changed:

    | a port that | member |
    |---|---|
    | answers the correctly rounded sine or cosine where the pinned one is a unit from it; a sine a unit up; a cosine a unit up | `SinCos` |
    | answers the correctly rounded arc tangent where the pinned one is a unit from it; an arc tangent a unit up; takes the angle through an arc cosine | `Atan2` |
    | applies a versor by its inverse | `VersorReverse` |
    | composes in the other order | `Compose` |
    | forms a screw left to right | `Screw` |
    | forms a quotient for each coefficient | `ByQuotient` |
    | does not test a zero angle; does not test a zero bivector part | `IsZeroAngle`, `IsZeroBivector` |
    | builds a dilator through the pinned exponential and logarithm; by its root and the root's reciprocal | `DilatorParts` |
    | interpolates the shorter way | `Between` |
    | splits `v` by the contraction; by two projections | `SplitAlong` |
    | forms the logarithm's vector by a projection | `MotorAcross` |
    | writes a matrix row-major | `MatrixIndex` |
    | fuses a dot product; sums it from the last | `Dot` |
    | fuses the cross product | `Orientation` |
    | reads a rotor from the scalar's sum always | `RotorOfRotation` |
    | has no rule at a half turn; takes the last in a tie | `Canonical` |
    | divides a direction by its norm a second time | `UnitAsGiven` |
    | moves a screw's point to the point of its axis nearest the origin | `PointAsGiven` |
    | sums an exponential's parts with a zero | `Placed` |
    | applies a normalisation twice | `NormaliseOnce` |

    For each pair, one of three things, and the case holds which: **a committed vector of that function is red
    under the port (340 pairs); or the function does not read the member the port changes (2,438), by a table of
    the members each function reads; or the function reads it and the port is listed unable, with the reason (64).**
    In the second and the third the port answers the function as the shipped arithmetic does on every element of a
    drawn sample, and a pair for which the table is wrong, or a port that changes a drawn answer and fails no
    committed vector, fails the case. *The sample* is 450 configurations: ninety drawn at three scales of coordinate,
    the even ones with two coefficients in three of each versor a zero of a drawn sign; sixty in which every
    coefficient of every versor and of every object it is applied to is a zero of a drawn sign, or every one but
    one; and three hundred in which a third of those coefficients are such zeros and a third are powers of two
    between `2⁻⁶⁵⁰` and `2⁻⁴⁵⁰` of a drawn sign, so that a product of two of them underflows to a zero of either sign
    where the square of one need not. Each matrix is asked again at every threshold its own statistics name. 32,001
    elements. **It is a search and no proof.** The sixty-four listed pairs, and what each reason is. Fifty-three
    are the fold seeded from a negative zero, which answers otherwise only where every signed term of a coefficient
    is a negative zero, and no element of the sample has such a coefficient; **that is the weakest entry here and is
    not proved**: no argument is given that no typed operand has one. Two are the port that sums an exponential's
    parts, on the two interpolations: argued, since the exponential enters one more product and the sign of a zero
    factor cannot move a bit of a fold (D85 item 3). Six are argued from the terms a translator's functions and a
    motor's logarithm read: a squared norm with one term that is not a zero, or two; two translators' product; and
    the order of the one product a motor's logarithm reads. **Three are a search and say so**: the other order of
    summation, a fused product and the other grouping, on `Translator.toMatrix`, where the terms of each entry
    were not counted, a term that underflows can leave a negative zero in a fused fold, and nothing is argued
    about whether one survives to an entry. A case holds that no function of Phase 728 reads a member this phase
    added.

    **This listing was wrong once, and it is recorded.** Its first form listed the fused product unable on
    `Motor.log`, "one product fused into a zero is that product rounded". That is false where the one term
    underflows to a negative zero: the fold answers `+0.0 + (−0.0) = +0.0` and the fused product `-0.0`. Such an
    input is now the vector `Motor.log-whose-one-product-underflows-to-a-negative-zero`, red
    under the fused port; and the sample, which drew no operand whose products underflow, now draws three hundred
    configurations of them, and a case holds that it reaches that very difference in a drawn logarithm. Run over
    every pair still listed, the widened search moved none; every reason was read again, and each now either
    argues or says it is a search. A port that divides a screw's axis by its norm a second time likewise failed
    no vector of `Motor.ofScrew` or `Motor.ofLine` in the first form: that is now a member, a port and a vector
    of each, and the same question was asked of a screw's point, a reflection's normal and a line's direction
    (item 3).

    **A normalisation divides once** (D85 item 5), and the port that applies one twice is held against every
    `normalise` the package publishes, eleven of them. *The five versors',* in the table above: each is red on a
    vector. `Rotor.normalise` had none until `of-a-rotor-a-second-division-would-move`, each of whose coefficients
    a second division ends one unit higher. `Translator.normalise` has a second divisor of exactly one wherever the
    square of its scalar part is formed exactly, since the root of that square is then the scalar part's
    magnitude; it is red where the square is subnormal and loses its last place
    (`of-a-scalar-part-whose-square-is-subnormal`). *Phase 728's six,* in a case of this family:
    `Circle.normalise` and `Plane.normalise` were red on vectors that existed; `PointPair.normalise` and
    `Line.normalise` were red on none, and one vector of each is appended, the only vectors of the objects this
    phase adds; `Point.normalise` and `Sphere.normalise` are immune by an argument: their weight is one of the
    coefficients divided, so the first division leaves it exactly one (a finite double that is not zero, over
    itself, is one) and a second, by one, is exact at every coefficient, and Phase 728's drawn sample holds it. The
    port is not added to Phase 728's own table of twenty, whose counts D87 states: the case runs it over that
    phase's vectors and its drawn sample and holds that no other function of the objects moves, as it holds that
    no function of the objects reads any other member this phase added.

    **Each function, the vectors it has, and up to four wrong ports with a vector of that function each fails**
    (every published function that computes has a vector a wrong port fails). A name is the vector's id
    after the function's. The short names of this phase's ports, in the order of the table above: rounded-sine,
    sine-up, cosine-up; rounded-angle, angle-up, arc-cosine; inverse; other-order; screw-order; quotient-each;
    zero-angle, zero-bivector; exp-ln, root-reciprocal; shorter; split-contraction, split-both; log-projection;
    row-major; fused-dot, dot-order; fused-cross; scalar-sum; no-half-turn-rule, last-in-tie; unit-twice;
    point-moved; summed; twice-normalised. Phase 728's ports keep
    D87's short names. A function that copies has no rule to change, and its vectors hold each index and the sign
    of each zero.

    | function | vectors | wrong ports, each with a vector of this function it fails |
    |---|---:|---|
    | `Rotor.applyToCircle` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Rotor.applyToLine` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Rotor.applyToPlane` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Rotor.applyToPoint` | 7 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Rotor.applyToPointPair` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Rotor.applyToSphere` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Rotor.compose` | 6 | other-order `general-1`; fused `general-2`; order `general-1` |
    | `Rotor.exp` | 18 | summed `of-zeros-keeps-each-sign`; rounded-sine `at-a-sine-a-unit-from-correctly-rounded`; sine-up `general-1`; quotient-each `general-1`; and 7 more |
    | `Rotor.interpolate` | 17 | inverse `general-1`; rounded-sine `at-a-sine-a-unit-from-correctly-rounded`; rounded-angle `at-an-arc-tangent-a-unit-from-correctly-rounded`; sine-up `general-3`; and 14 more |
    | `Rotor.log` | 23 | rounded-angle `at-an-arc-tangent-a-unit-from-correctly-rounded`; angle-up `general-1`; arc-cosine `general-1`; quotient-each `general-1`; and 6 more |
    | `Rotor.normalise` | 11 | twice-normalised `of-a-rotor-a-second-division-would-move`; fused `searched-1017`; order `general-1`; reciprocal `general-1`; and 1 more |
    | `Rotor.ofAxisAngle` | 20 | unit-twice `general-2`; rounded-sine `at-a-sine-a-unit-from-correctly-rounded`; sine-up `general-1`; reciprocal `general-2`; and 6 more |
    | `Rotor.toMatrix` | 8 | inverse `general-1`; row-major `general-1`; grouping `general-1`; fused `general-1`; and 3 more |
    | `Rotor.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |
    | `Rotor.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |
    | `Translator.applyToCircle` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Translator.applyToLine` | 3 | inverse `general-1`; grouping `general-1`; fused `general-2`; order `general-1` |
    | `Translator.applyToPlane` | 4 | inverse `general-1`; grouping `general-1`; fused `searched-1002`; order `general-1` |
    | `Translator.applyToPoint` | 5 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Translator.applyToPointPair` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Translator.applyToSphere` | 3 | inverse `general-1`; grouping `general-2`; fused `general-1`; order `general-1` |
    | `Translator.compose` | 5 | fused `general-1` |
    | `Translator.normalise` | 6 | twice-normalised `of-a-scalar-part-whose-square-is-subnormal`; reciprocal `general-1`; divisor `of-a-zero-scalar-part` |
    | `Translator.toMatrix` | 6 | inverse `general-1`; row-major `general-1`; reciprocal `general-1`; divisor `searched-1000` |
    | `Translator.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |
    | `Translator.ofVector` | 8 | a wrong index, or a zero whose sign is lost: `halves-and-negates` |
    | `Translator.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |
    | `Motor.applyToCircle` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Motor.applyToLine` | 4 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Motor.applyToPlane` | 3 | inverse `general-1`; grouping `general-2`; fused `general-1`; order `general-1` |
    | `Motor.applyToPoint` | 4 | inverse `general-1`; grouping `general-2`; fused `general-1`; order `general-1` |
    | `Motor.applyToPointPair` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Motor.applyToSphere` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Motor.compose` | 4 | other-order `general-1`; fused `general-1`; order `general-1` |
    | `Motor.exp` | 17 | summed `of-a-vector-alone-is-one-and-the-vector`; rounded-sine `at-a-sine-a-unit-from-correctly-rounded`; sine-up `general-1`; quotient-each `general-1`; and 11 more |
    | `Motor.interpolate` | 15 | inverse `general-1`; rounded-sine `at-a-sine-a-unit-from-correctly-rounded`; rounded-angle `at-an-arc-tangent-a-unit-from-correctly-rounded`; sine-up `general-1`; and 17 more |
    | `Motor.log` | 22 | rounded-angle `at-an-arc-tangent-a-unit-from-correctly-rounded`; angle-up `general-1`; arc-cosine `of-a-screw-about-a-line-parallel-to-e3`; quotient-each `general-1`; and 8 more |
    | `Motor.normalise` | 7 | twice-normalised `general-3`; fused `searched-1033`; order `searched-1003`; reciprocal `general-1`; and 1 more |
    | `Motor.ofLine` | 20 | unit-twice `searched-of-a-weighted-line-1000`; point-moved `general-of-a-weighted-line-3`; screw-order `general-1`; rounded-sine `at-a-sine-a-unit-from-correctly-rounded`; and 10 more |
    | `Motor.ofMatrix` | 77 | other-order `general-1`; row-major `general-1`; scalar-sum `general-1`; no-half-turn-rule `of-a-half-turn-whose-largest-component-comes-out-negative`; and 18 more |
    | `Motor.ofRotorTranslator` | 4 | other-order `general-1`; fused `general-1`; order `general-1` |
    | `Motor.ofScrew` | 19 | unit-twice `searched-1000`; point-moved `general-1`; screw-order `general-1`; rounded-sine `at-a-sine-a-unit-from-correctly-rounded`; and 10 more |
    | `Motor.toMatrix` | 5 | inverse `general-1`; row-major `general-1`; grouping `general-1`; fused `general-1`; and 3 more |
    | `Motor.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |
    | `Motor.ofRotor` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |
    | `Motor.ofTranslator` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |
    | `Motor.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |
    | `Dilator.applyToCircle` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Dilator.applyToLine` | 3 | inverse `general-1`; grouping `general-2`; fused `general-1`; order `general-2` |
    | `Dilator.applyToPlane` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-2` |
    | `Dilator.applyToPoint` | 4 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Dilator.applyToPointPair` | 3 | inverse `general-1`; grouping `general-1`; fused `general-1`; order `general-1` |
    | `Dilator.applyToSphere` | 4 | inverse `general-1`; grouping `general-2`; fused `general-1`; order `general-1` |
    | `Dilator.compose` | 4 | other-order `general-1`; fused `general-1`; order `general-1` |
    | `Dilator.normalise` | 7 | twice-normalised `general-2`; fused `searched-1007`; reciprocal `general-2`; divisor `of-a-zero-squared-norm` |
    | `Dilator.ofCentreFactor` | 9 | exp-ln `general-1`; root-reciprocal `general-1`; grouping `general-1`; fused `general-1`; and 2 more |
    | `Dilator.ofFactor` | 16 | exp-ln `general-1`; root-reciprocal `general-1`; reciprocal `general-1` |
    | `Dilator.toMatrix` | 5 | inverse `general-1`; row-major `general-1`; grouping `general-1`; fused `general-1`; and 3 more |
    | `Dilator.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |
    | `Dilator.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |
    | `Reflection.applyToCircle` | 3 | grouping `general-1`; fused `general-1`; order `general-1` |
    | `Reflection.applyToLine` | 4 | grouping `general-1`; fused `general-1`; order `general-1` |
    | `Reflection.applyToPlane` | 3 | grouping `general-3`; fused `general-1`; order `general-1` |
    | `Reflection.applyToPoint` | 4 | grouping `general-1`; fused `general-1`; order `general-1` |
    | `Reflection.applyToPointPair` | 3 | grouping `general-1`; fused `general-1`; order `general-1` |
    | `Reflection.applyToSphere` | 3 | grouping `general-1`; fused `general-1`; order `general-1` |
    | `Reflection.compose` | 7 | other-order `general-1`; fused `general-1`; order `general-1` |
    | `Reflection.normalise` | 7 | twice-normalised `general-2`; fused `searched-1037`; order `searched-1017`; reciprocal `general-2`; and 1 more |
    | `Reflection.ofNormalDistance` | 9 | unit-twice `general-1`; reciprocal `general-2`; direction `of-a-zero-normal`; norm `general-2`; and 3 more |
    | `Reflection.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |
    | `Reflection.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |

    **The sine a unit up is a port and no host.** The correctly rounded port answers Phase 725's recorded reference
    at three arguments and the pinned value elsewhere, so it can only fail a vector at those arguments, and every
    function that asks the sine or the arc tangent has one. The ports a unit up move every answer and are what the
    drawn sample is searched with. No case reads a host's sine.

    **A normalisation nobody asked for** is the published function before or after the published `normalise`, and
    fails a vector of `Rotor.applyToPoint`, `Motor.applyToLine`, `Dilator.applyToSphere` and `Rotor.compose` at each
    of the three configurations; a rotor twice a unit takes a point's weight to exactly four, and that vector is
    worked by hand.

12. **How the family was cross-checked.** By a route that shares no code with the package: Phase 727's and Phase
    728's scratch route for the algebra and the objects (the orthonormal basis, blades as bitmaps, the table derived
    and held to D85's digests), and every function of this phase written again from D85 item 4 and this entry, once,
    over a number type. *In binary64* it replays all 533 vectors, refusals and measures included, **bit for bit,
    with no mismatch**; the pinned sine, cosine and arc tangent it is given are `Fuaran.Compute.Elementary`'s own,
    which is Phase 725's package and has its own law file. *In exact rationals* fourteen vectors answer another
    case, each authored to overflow or underflow. It is also the program that measured items 6, 7 and 8. The
    distance of each function's pinned answers from their exact values is **not** tabulated here as D87 tabulated
    it: the high-precision sine reduces its argument with 480 bits of `π`, which is not enough at the largest
    angles the vectors ask, and a table with those rows wrong would be worse than none.

13. **Where the suite and the registrations moved.** `VersorTests.fs` is new, the family "Versors". Three suite
    files are edited where a roster reads the surface. `GeometricAlgebraTests.fs`: the law count, the modules the
    operation census reads, the package's top-level names. `ConformalTests.fs`: its vectors are those of the object
    modules as Phase 728 committed them, and no longer every vector with a dot in its `op`; the helpers `VersorTests` reuses are no longer
    private; the census reads the three new source files and accounts for each new literal with D85's phrase (the
    one `1.0` in a comparison is the fourth entry of a matrix's last row); and the host-call case counts five roots
    and reads a function named `exp` as a definition. `ComputeBoundaryTests.fs`: the algebra is held to Elementary
    and the solvers below it, by project and by assembly, and the case is renamed for what it now holds.
    `GeometricAlgebraOperations.exactness` has 160 rows. The package's project file references
    `Fuaran.Compute.Elementary`; `api/Fuaran.Compute.GeometricAlgebra.txt` is regenerated and no other baseline
    moves; every other law file is byte-identical.

**The choices this entry makes, each with the alternative.** The names of item 1 (against `apply` on a union or on a
multivector). Eight cases on `GeometryRefusal` and a second union for the matrix (against one union with payloads, or
a third for the versors). The refusal at a scalar part of zero (against answering a zero bivector). The sign of a zero in an exponential,
placed and kept (against summed with a zero and lost; item 3, a reading that can be revisited before the tag). The
matrix read
through the origin's weight (against refusing a versor that is no unit). The unit bivector in the split (against the
bivector's inverse). The three measured forms that ship in items 6 to 8, and the two statistics (against statistics
of the normalised columns, which are not exact on integer matrices). The order in which a matrix is tested (length,
then angle, then orientation). `Translator.ofVector` counted with the copies. Typed application named
`applyTo<Type>` (against six functions on each object module, which cannot be reopened).

**Ladder.** `proofs.json` gains `versors-pinned-by-recorded-rules` at `tested`, listing this family's cases, each held
to a test of that name. It stands over the premise `trigonometry-pinned-by-transcription` for the operations that
ask the sine, the cosine or the arc tangent, says that the bounds are one sample's measurements, and says what is
held of a wrong port in the words of the case that holds it. `ProofsLadderTests` is not edited.

**What a consumer does.** Nothing: no existing function's answer moves. The package now references
`Fuaran.Compute.Elementary`, so a consumer restores that package with it. The version stays `0.47.0`, a draft this
addition rides. `STABILITY.md` says what a caller should know first.

**Tidy.** Reviewed every file this change touches. *Removed:* nothing was made dead. *Reused instead of minted:*
the arithmetic record, the norm, the refusals, the direction's normalisation, the zero test of a quotient and the
suite's wrong ports, corpus reader and cell renderer are Phase 728's, opened to the new module and the new suite
file and not written again; the suite's second set of object helpers in the vector file serves the versors too.
*Considered and kept:* the three forms measured and not shipped, because each is a wrong port; the drawn-sample
machinery written a second time in `VersorTests.fs` beside Phase 728's, because that one is bound to its own table
of reasons and its own thresholds; the five pairs of functions that turn a versor into its list of coefficients and
back, in the package and again in the vector file, for D87's reason; and the float census's account as one list in
`ConformalTests.fs`, where the case that reads it is.

## 2026-11-24 — D87: points, lines, planes, circles and spheres ship as blades of `Fuaran.Compute.GeometricAlgebra`, written to D85 and choosing nothing D85 fixed — six typed wrappers and a module of each one's name, the blade operations in `Blade`, one union of refusals and a union of kinds for each type; every function Phase 727's general product, written once with its arithmetic as an argument, so every go-red witness is the shipped function with one member changed; a zero test that takes its threshold on every call, and at a threshold of zero reads signs and forms no quotient; 840 more vectors, each replayed bit for bit by a second implementation; a wrong port held against every function, by a red vector or by a drawn search that finds it unable; the closed-form bound measured on one stated sample, 292 units in the last place over 1,948 lines and spheres; and two things Phase 727 was asked to add for the squared norm that no vector can show

**Context.** Phase 728. D85 recorded how the objects of three-dimensional Euclidean space are held in `Cl(4,1)`, and D86
shipped the multivector and the products they are written in. This entry ships the objects: the embedding, the six
types of D85 item 8 with both constructions of each, the join and the meet, the read-backs, the two normalisations of
this phase, the kinds an answer has, and incidence. It chooses no sign, scale, order, orientation, branch,
normalisation or threshold: each is D85's, and where a line below looks like a choice the item that makes it is cited.
What D85 left to this entry is the names, the cases of each refusal and each kind, and each bound with its sample.

**Premise check** (on `21fbc29`, the tree this phase was cut from). `src/Fuaran.Compute.GeometricAlgebra/` holds `Basis.fs`,
`Multivector.fs`, the operations table and the law family, and this file opens with D86, then D85. The law file holds 167
vectors. D85 items 1, 4, 5 and 6 fix the null basis, each object's construction, the read-backs and the zero test. The
package references FSharp.Core alone and the boundary suite holds that. The tree declares `0.47.0` and no `v0.47.0`
tag exists. All five held. Three things were sharpened. D49 item 4's norm, as LinearAlgebra writes it, calls `abs`; the
copy here may call nothing but the root, so it finds the largest magnitude with two comparisons (item 5). Phase 727's
suite holds four rosters this phase's surface moves, so that file is edited (item 13). And of the three squared-norm
vectors Phase 727 carried forward, two ask for something no vector can show (item 8).

**Ruled.**

1. **The surface, and the names.** Six record types, seven modules, one union of refusals and nine unions of kinds, in the
   namespace of the package id. Each record holds one `float` field for each coefficient its type carries, in index
   order, named for its blade (`N0`, `E1`, `NInf`, `N0E1`, `E1E2NInf`, …): `Point`, `PointPair`, `Circle`, `Line`,
   `Sphere`, `Plane`. They are declared in that order because a line's blades are six of a circle's and a plane's four
   of a sphere's, so a record expression naming only the shared fields reads as the smaller type. A Euclidean point, a
   direction and a normal are a triple of floats.

   | module | functions | D85 |
   |---|---|---|
   | `Blade` | `join a b` (`A ∧ B`), `meet a b` (`B* ⌋ A`), `inverse x` (`X̃ / (X ∗ X̃)`), `meetWithin j a b` (`(B ⌋ J⁻¹) ⌋ A`), over multivectors | item 4 |
   | `Point` | `up`, `coordinates`, `weight`, `normalise` | items 1, 4, 5 |
   | `PointPair` | `through p q`, `weight`, `normalise`, `centre`, `squaredRadius`, `points tau`, `kind tau` | items 4, 5, 6 |
   | `Line` | `through p q`, `ofPointDirection p d`, `weight`, `normalise`, `direction`, `moment`, `nearestPoint`, `kind tau`, `meet tau first second`, `meetPlane`, `meetSphere` | items 4, 5, 6 |
   | `Circle` | `through p q r`, `ofCentreRadiusNormal c r n`, `weight`, `normalise`, `centre`, `squaredRadius`, `kind tau`, `plane`, `power p`, `side tau p` | items 4, 5, 6 |
   | `Plane` | `through p q r`, `ofNormalDistance n δ`, `weight`, `normalise`, `normal`, `distance`, `nearestPoint`, `kind tau`, `meet`, `signedDistance p`, `side tau p` | items 4, 5, 6 |
   | `Sphere` | `through p q r s`, `ofCentreRadius c r`, `weight`, `normalise`, `centre`, `squaredRadius`, `kind tau`, `meet`, `meetPlane`, `power p`, `side tau p` | items 4, 5, 6 |

   Each object module also has `toMultivector` and `ofMultivector`, the widening and the copy back of item 4. Seventy
   functions. `up` is the record's word and is kept. A function is named for what it takes (`ofCentreRadius`), so the two
   constructions of a type cannot be confused, and `through` is every type's outer product of points. *Rejected:*
   `Multivector.meet` and `Multivector.join`: a module cannot be reopened in a later file, the meet needs the refusal
   declared after `Multivector`, and these are operations on blades, which `Blade.inverse` says in its name where
   `Multivector.inverse` would promise the inverse D85 item 10 keeps out. *Rejected:* a record for a Euclidean triple
   (a third name for three floats, and one more type every caller converts through); `ofCoordinates` for `up`; typed
   meets for every pair of types (fifteen functions where `Blade.meet` and a copy say the same; the six that ship are
   the ones D85 item 4 or 6 names: two planes, two spheres, a sphere and a plane, a line with a plane, with a sphere
   and with a line); a typed join other than `through` (no reader yet).

2. **What is refused, and one rule for every `Result`.** One union, `GeometryRefusal`, with a case for each thing D85
   says is refused by name: `NonFiniteCoordinate`, `ZeroDirection`, `NonFiniteDirection`, `NegativeRadius`,
   `NonFiniteRadius`, `NonFiniteDistance` (construction from parameters, item 4), `ZeroDivisor`, `NonFiniteDivisor`
   (a weight, `X ∗ X̃`, `W ∗ W̃`, `J ∗ J̃`, or the product of two weights; items 4 and 5), `InvalidThreshold` (a `τ` that
   is negative or not finite, item 6) and `NotANumber`. **Every parameter is checked in the order it is passed, before anything is computed**: a centre and a radius whose
   arithmetic would overflow into a NaN are not reached when the normal passed after them is zero. A
   non-finite radius is `NonFiniteRadius` whatever its sign. A direction whose norm overflows is `NonFiniteDirection`:
   dividing by an infinite norm would answer a zero direction and say nothing. **An `Ok` never holds a NaN:** where an
   answer would hold one, because an operand did or because finite arithmetic overflowed into one, the function
   answers `NotANumber`. D85 item 4 says a blade's inverse is refused when its divisor is zero; a divisor that is not
   finite is refused beside it, as item 5 refuses it for a normalisation, because the quotient would otherwise be a
   NaN or a silent zero. An infinity is a value and is answered. *Rejected:* a union for each function (a caller binds a
   construction to a read-back in one `Result`, as it does with `LinAlgError`); a payload on a case (the record says
   "by name", and no caller has asked what the divisor was).

   The functions that only multiply or copy return no `Result` and are total: `through`, the meets, `Blade.join`,
   `Circle.plane`, the reads. What they return on degenerate or non-finite operands is a blade like any other, and
   `kind` says what it is.

3. **The kinds.** `Reality` is `Real`, `Tangent` or `Imaginary`. `PointPairKind`, `CircleKind` and `SphereKind` are
   `Round of Reality`, a flat, `AtInfinity`, `Zero` and `NoObject`; the flat of a circle carries its `Line` and of a
   sphere its `Plane`, and a flat point pair, `P ∧ n∞`, is `FlatPoint` with the point's coordinates. `FlatKind`, for a
   line and a plane, is `Flat`, `AtInfinity`, `Zero` or `NoObject`. `PairPoints` is `Real` of two points, `Tangent` of
   the centre, or `Imaginary` of the centre and the squared radius. `LineMeet` is `Within` of a point pair, `SameLine`,
   or `Skew` of a point. `Side` is `Inside`, `On`, `Outside`; `PlaneSide` is `Negative`, `On`, `Positive`. The test of a
   kind is D85 item 6's, in its order: round when the norm on the blades that hold `n0` and not `n∞` is above `τ`;
   else flat, at infinity or zero as the other groups are above `τ` or within it; else no object. **A blade that holds
   a NaN is `NoObject`**, whichever group holds it: item 6 says anything else is no object, and a flat or a round
   reported around a NaN would be a NaN returned as geometry. A round's reality is its squared radius against the same
   `τ`, which is in another unit: item 6 states both, and `kind` documents it.

4. **Where D85 was read, and what was read.** None of these is a sign, a scale or an order.
   - *The centre is coordinates.* D85 item 5 lists "a centre" among the read-backs that divide by a weight, so
     `centre` answers the vector `⟨(X n∞) X⟩₁` read as a point is: its indices 2 to 4 over its index 1. That weight is
     negative for a point pair, so a centre coordinate that is zero is `-0.0` there. The vectors pin it.
   - *A real pair's two points are the vectors D85 writes,* `⟨(X ∓ s) W⁻¹⟩₁`, copied into `Point` and not normalised
     (item 5). For `up p ∧ up q` they are `−up p` and `−up q`: the points, in order, of weight `−1`.
     `Point.coordinates` reads them. A tangent and an imaginary pair answer the centre, which is coordinates. D85 item 4
     says of a real pair "for `P ∧ Q` they are `P` then `Q`": that names the points and their order, and its formula,
     which governs, gives each with weight `−1`. An independent derivation gives the same weight.
   - *A plane's normal and distance are exact reads of its vector* (item 4: "are its vector's"), so they are `n̂` and
     `δ` times the weight, as a line's direction is read and not computed. `normalise` first, for the unit ones.
   - *A flat point is answered by the nearest-point formula.* Item 6 says a flat of grade two "is answered as the
     point", and item 4 gives the point of any flat nearest the origin, which for `P ∧ n∞` is the point.
   - *`n̂ · c`, for a circle from parameters, is the scalar product* of the two vectors, item 3's fold, the normal
     first.
   - *Two lines.* `Line.meet` first takes their meet in the whole space. Where that is not the zero blade the lines
     span the space, and the answer is `Skew` carrying it, a multiple of `n∞`. Otherwise it is item 4's rule as
     written: the span is the join of the first line with `up` of the second's point nearest the origin, or, when
     that join is the zero blade, with `up` of those coordinates plus the second line's direction as stored; when that
     is the zero blade too, `SameLine`; else `Within` carrying the meet within the span, which `PointPair.kind` reads
     as a flat point or as at infinity. Each "is the zero blade" is the test of a kind on the typed copy, with the
     caller's `τ`. Testing for skew first is this entry's: item 4 gives the span "for those" whose meet in the whole
     space is zero, and a span through a point of a skew line contains only one of the two. The rule goes through
     `up`, which refuses a coordinate that is not finite, **and so `Line.meet` can answer `NonFiniteCoordinate`.** The
     line with direction `(0, −1, 0.07918293826789391)` and moment `(−M, M, 0)`, `M` the largest double, has
     finite coefficients and a nearest point whose first coordinate is past the largest double. `Line.nearestPoint`
     answers `(∞, 0, 0)`, as `Ok`: an infinity is a value and is answered (item 2). `Line.meet`, asked to
     join the first line with `up` of those coordinates, answers `NonFiniteCoordinate`: that is D85 item 4's own
     refusal by name, of the one thing the rule passes to `up`, and no other case of `LineMeet` is true of two lines
     whose span cannot be formed. Two vectors hold the two answers. An earlier form of this item said the overflow
     could not be reached, because the inverse would leave the doubles first; that was an argument and a search over
     48,000 pairs, and it was false.
   - *A typed operation is the general one and a copy* (item 4). The products are `Multivector`'s. Nothing is
     specialised, and no speed is claimed.

5. **The norm is D49 item 4's, transcribed.** The package may not reference LinearAlgebra (D85 item 9), so
   `Norm.scaled` is that routine again: `m = max |x_i|`, a NaN when any element is one, `+∞` when `m` is, `0` when `m`
   is, else `m × √(Σ (x_i / m)²)` folded in index order. It finds `m` with `v > m` and `−v > m`, which is the same `m`
   and calls nothing. The suite, which references both packages, holds the two to one answer bit for bit over 4,000
   drawn vectors of one and three elements across six hundred binary orders of magnitude and ten authored ones (an
   empty vector, zeros of both signs, a NaN, an infinity, squares that overflow and underflow, the least subnormal).
   `sqrt` is the one host function the object modules call, twice: here, and for a real pair's `√(X ∗ X)`. That root
   is of a positive number: for a point pair each term of `X ∗ X` is the negation of a term of `X ∗ X̃`, so one is
   the other negated wherever either is not zero (the suite holds it on every point pair of the law file), and
   `W ∗ W̃` is a sum of squares, so a squared radius above a threshold that is not negative has a positive numerator.

6. **Every function is written once, and its arithmetic is an argument.** `Conformal` holds each function that
   computes, taking an `Arithmetic` first. It has seventeen members, **and each is one rule**: the four products; how
   a product of more than two factors is grouped, two at a time from the left (`Chain`); how a quotient is formed
   (`Divide`); how a statistic's is, the product of the divisor's two factors and then one division (`OverBoth`);
   the norm's scaling (`Scaled`) and the sum of its squared quotients, folded in index order (`SumOfSquares`), which
   are D49 item 4's two rules; the two halved squares (`0.5 × ((x1·x1 + x2·x2) + x3·x3)` and `0.5 × (r × r)`); the
   `−2.0 ×` of a power; whether a divisor is zero; whether a direction's norm is zero; what "above `τ`" is; what
   "within `τ`" is; and whether a threshold is the exact comparison, at which signs decide. A published function is
   that function under `Arithmetic.shipped`, in one line, and passes nothing else. D86 item 4 ruled the same for the
   fold and for the same reason: a go-red witness must be the shipped code. A wrong port is `Arithmetic.shipped` with
   one member changed, and the suite calls the same function under it. Fifty-two functions compute and have such a
   door. Eighteen are copies and exact reads (`toMultivector`, `ofMultivector`, a point's and a sphere's weight, a
   line's direction and moment, a plane's normal and distance) and have none: there is no rule of theirs to change.
   All of it is internal. **Two things are not members and are said so.** Reading the signs of a numerator and a
   divisor at `τ = 0` (a zero numerator is within; equal signs are above) is written once, in `Conformal.against`,
   and no witness changes it. And "below `−τ`" is whatever is neither above nor within. *Rejected:* a witness
   written as a second implementation in the suite (it can agree with itself); a mutable arithmetic the suite swaps
   (a pinned library with a global, and a suite that runs in parallel); one member for every zero test (a witness
   for a zero divisor then also moved the branch taken at `τ = 0`, and showed nothing of either); one member for
   the whole norm (its order of summation could then be changed by no witness).

7. **A zero test takes `τ` on every call, and at `τ = 0` it forms no quotient.** Ten functions test a zero (`kind` on
   five types, `side` on three, `PointPair.points`, `Line.meet`), each takes `tau` first, and none has a default, an
   optional argument or a named tolerance. `τ = 0` is the exact comparison. Each statistic is published twice, as D85
   item 6 says: `Sphere.power`, `Circle.power` and `Plane.signedDistance` answer the value, formed in item 6's order
   (the scalar product, `−2.0` times it for a power, the product of the divisor's two factors, one division), and
   `side` answers the kind. **At `τ = 0` every statistic that is a quotient is decided from the signs of its numerator
   and its divisor, and the quotient is not formed.** That is `side`, and it is also a round's reality: D85 item 4
   says a point pair's points are "decided by item 6's test of the squared radius", and the squared radius
   `−(X ∗ X̃) / (W ∗ W̃)` is that statistic. So `PointPair.kind`, `Circle.kind`, `Sphere.kind` and
   `PointPair.points` read the two signs at `τ = 0`, through the one function `side` uses. A power of `3·2^-1080` is
   `Outside` where its quotient is `+0.0`; and a point pair of weight `2^500` whose squared radius has the numerator
   `−2^-573` is `Imaginary` where its quotient is `-0.0`. **What an answer carries is unchanged:** an imaginary pair
   carries its squared radius, D85 item 4's quotient, so that pair is `Imaginary` carrying `-0.0`, and
   `PointPair.squaredRadius` answers `-0.0` for it. D85 item 4 says an imaginary pair's squared radius "is negative";
   at `τ = 0` it can be this `-0.0`, negative in sign and zero in magnitude, where the quotient has underflowed. That is D85's two rules composed, the kind by item 6 and the value
   by item 4, and no new convention. Its mirror, with the numerator positive, is `Real`, and its two points are formed
   by the root as any real pair's are. Above `τ = 0` nothing changes: at `τ = 5e-324` both are `Tangent`, because there
   the quotient is the statistic and a zero is within any threshold. Twelve vectors hold this, on each of the four
   functions and on `PointPair.squaredRadius`, and the port that forms the quotient at `τ = 0` fails each of the
   eight that are asked at zero. A power that is zero is `-0.0` over a positive divisor; a circle's divisor,
   `w_P × (Y ∗ Ỹ)`, is negative for a point of positive weight, so a point on a circle answers `+0.0`.

8. **Carried from Phase 727, and what could not be done.** Phase 727 left the squared norm without a vector that a
   separately written fold fails under a fused multiply-add, under the right operand's index outermost, or under a
   fold seeded from its first term; and the scalar product with one vector for the second of those. Four vectors are
   added, each with its witness through Phase 727's doors. `squaredNorm-fma-witness` answers `+0.0`, and `2^-60`
   fused. `squaredNorm-absorbed-then-cancelled` answers `1`, and `0` with the terms descending.
   `scalar-right-index-outermost` answers `2`, and `1` with the right index outermost. **Two of the three things asked
   of the squared norm cannot be shown by any vector, and the suite now holds why.**
   - *The right operand's index outermost is not another order for a squared norm.* The scalar coefficient pairs each
     blade with one partner, of its own grade, and the two terms of a pair carry one sign. Against its own reverse,
     the term at a blade and the term at its partner are one product, so both orders visit one sequence of values.
     The suite holds the pairing and holds every squared-norm vector to the same bits in both orders.
   - *A fold seeded from its first term answers the squared norm's bits on every input.* The first term is `a0 × a0`,
     a square, and never a negative zero. `squaredNorm-every-term-that-can-be-a-negative-zero` is the nearest vector
     there is: twenty-four of its thirty-two signed terms are `-0.0`, the eight that are a blade against itself are
     `+0.0`, and it answers `+0.0` under any summation. It is in the file as the pinned answer and is no witness.

   **D86 item 7 is reworded.** It said the package's sources name `0.0` in no comparison but the hash's. That is true
   of the operation modules and not of the law family, which compares bits against float literals as a law must. The
   sentence now names its scope, and this phase's source-reading case states the same one.

9. **The laws.** `GeometricAlgebraLaws.laws` gains nine, the ones of D85 item 7 that concern these objects, each
   named for its comparison; twenty-seven in all, on .NET and under node, at seed 590 and 200 iterations. The objects'
   inputs are integers of magnitude up to four (up to eight on the in-circle grid), so that a product of five points'
   coefficients and each partial sum is a double; the two laws over any multivector draw Phase 727's exact inputs.
   - *Bit for bit:* `up x ∗ up x = 0`, `up x ∗ up y = −½ |x − y|²` and `up x ∗ n∞ = −1`; a point on the circle and on
     the sphere built through it (`up p ∧ X` is `+0.0` at every index); the meet of two planes in both (its meet with
     each is `+0.0` at every index); the meet within the whole space against the meet.
   - *Under the float identity:* the meet against `B* ∧ A*` undualled; and `up x ∗ up x` against `−½ |x − x|²`, whose
     right side is asserted to be `-0.0`, which is why it is not the law above.
   - *In sign:* the power against a sphere and the signed distance from a plane, by value and by `side` at `τ = 0`,
     against `|x − c|² − r²` and the triple product in integers, for points and spheres of each sign of weight; and
     **the in-circle statistic against the determinant predicate** on the integer grid of a plane, by value and by
     test, through the orientation of the three points, since the power is negative inside whatever their order.
     Three collinear points are refused by both, as `ZeroDivisor`.
   - *Within a bound:* item 11.

10. **The vectors, and what a wrong port is held to.** The law file holds 1,007: Phase 727's 167, whose content is
    unchanged (the last of them gains the comma that separates it from the next); the four of item 8; and 836 over
    the seventy functions. Of those, 321 are three drawn configurations (five weighted points and the parameters of
    two spheres, two planes, a line and a circle, at three scales of coordinate) through every function; 449 are
    authored: each named case, each refusal, each side of each threshold, the rounds of item 7, a parameter
    refused before a constructor computes, a round whose divisor underflows, a join whose every term is a negative zero,
    and the line of item 4 whose nearest point overflows; and 66 are cases a search found, chosen because a wrong
    port fails them where it failed no earlier vector of that function. A typed operand is the list of its
    coefficients, so a port reads operands and reproduces only answers. An answer that can be refused is its cell or
    `{ refused }`, and one that has kinds is `{ kind }` with what the case carries.

    The wrong ports, each `Arithmetic.shipped` with one member changed, the short name the table below uses, and how
    many of the fifty-two computing functions have a committed vector the port fails:

    | short | a port that | functions with a red vector |
    |---|---|---:|
    | order | sums the terms of each product descending | 28 |
    | fused | fuses a multiply-add in a product | 34 |
    | seeded | starts each fold at `-0.0`, which is a fold seeded from its first term | 4 |
    | grouping | forms a product of three factors from the right, `A (B C)` | 6 |
    | reciprocal | forms a quotient as a product with a reciprocal | 31 |
    | twice | divides a statistic by each factor of its divisor in turn, `(n / a) / b` | 6 |
    | norm | takes `√(Σ x²)` unscaled | 19 |
    | norm-order | sums the norm's squared quotients from the last to the first | 19 |
    | fused-norm | fuses each square into the norm's sum | 19 |
    | rounded-norm | rounds the norm's sum of squares correctly, once | 19 |
    | half-square | sums a point's three squares from the right | 3 |
    | radius | halves a radius before squaring it | 2 |
    | zero-sign | forms a power as `0 − 2s`, so a zero power has the other sign | 2 |
    | divisor | does not test a divisor against zero | 28 |
    | direction | does not test a direction's norm against zero | 3 |
    | above | finds a statistic above its threshold where it equals it (`≥`) | 10 |
    | strict | finds a statistic within its threshold only where it is below it (`<`) | 10 |
    | never | never finds a statistic within its threshold | 10 |
    | always | always finds a statistic within its threshold | 7 |
    | quotient | forms the quotient at `τ = 0` | 7 |

    Twenty ports, and 267 pairs of a port and a function with a red vector.

    **What the suite holds of them is computed, and it is this and no more.** For every wrong port and every function
    that computes, one of two things: a committed vector of that function is red under the port; or the port is listed
    as unable to change that function, with the reason, and it answers that function as the shipped arithmetic does on
    every element of a drawn sample. A port that changes an answer in the sample and fails no committed vector of that
    function fails the case. *The sample* is drawn at 165 numbers, and at each: a configuration, fifty-five at each
    of three scales of coordinate (the generator's seeds 9000 to 9164), every function over it; the same
    configuration with two coefficients in three of every operand replaced by a zero of a drawn sign (seeds 7000 to
    7164); and the four blade operations over untyped multivectors that are zeros of both signs, alone, around one
    coefficient, and around a third of them (seeds 8000 to 8164). Each function that takes a threshold is asked again
    at every threshold its own statistics name there: the norms of its blade's groups, the magnitude of its squared
    radius or of its statistic, and for two lines the norms the two-line rule tests. A wrong port that moves a
    statistic moves the answer at exactly such a threshold, which is why the sample holds them. It is 30,855 cases as
    drawn and 11,659 more at thresholds. **It is a search and not a proof**: an entry listed unable says that no
    element of this sample differs, and gives the reason it is believed that none would. The 1,040 pairs of a port
    and a function divide into 267 with a red vector and 773 listed unable. Of the unable, most are a function that
    does not use the member at all (it calls no product, forms no quotient, reads no norm, takes no threshold). The
    rest use it and are not moved by it: a product each of whose coefficients is a sum of at most two terms of its
    table, which round alike in either order (counted on the table, for each such function), or of one term, which
    a fusion does not change, or, for a sphere's centre, of two equal terms, which fused or not round to twice one; a
    product of two factors, which has one grouping, or of three one of which is `n∞`, which only copies and negates,
    so that either grouping forms the same products; a fold from
    `-0.0`, which differs only where every term of a coefficient is a negative zero; a point's `n∞` coefficient,
    which the outer product with `n∞` annihilates before it reaches a line; the sign of a zero power, which a test
    does not read; and a line's and a plane's kind under a threshold always met, whose only unsettled norms are of
    groups those types do not carry.

    **This claim has been wrong twice, and both are recorded.** Its first form said a port was shown red "on a
    committed vector of every function it could change". It was not true: a quotient formed with a reciprocal in a
    point pair's squared radius, a statistic divided twice, a fusion inside the norm and others changed bits on drawn
    inputs and passed every vector of the function. Its second form was the computed one, over typed operands only,
    and listed `Blade.join` unable under the seeded fold "because the coefficients a typed operand does not carry are
    `+0.0`". `Blade.join` takes untyped multivectors: the outer product of `e1` with a multivector that is `-0.0`
    at every index but `e1`'s is `+0.0` at every index from `+0.0`, and `-0.0` at eight of them from `-0.0`. That
    vector is in the file, and the sample now draws untyped operands and zeros of both signs; drawn so, the seeded
    fold also moved `Circle.ofCentreRadiusNormal`, and the norm summed from the right and the centre grouped from the
    right, which had no member, each needed vectors. The sixty-six chosen cases are what the searches found.

    **A normalisation nobody asked for** is the last wrong port and is not a member: it is the published function
    followed by the published `normalise`, and it fails a vector of each of the nine functions that build an object
    through points or by a meet and can be normalised, and of `Circle.plane` (`Line.meetPlane` answers a flat point,
    whose normalisation as a round is refused); normalising the operands of a statistic first fails a vector of each
    of the three statistics. Thresholds, radii and non-finite values are refused by comparisons that are not members;
    they are held by a vector on each side (`-5e-324` refused and `-0.0` accepted as a radius; `-1` refused and `-0.0`
    accepted as a threshold; a direction of norm zero refused and the least subnormal accepted; a norm that overflows
    refused).

    Each function, the vectors it has, up to four wrong ports with a vector of that function each fails (a name is
    the vector's id after the function's), and a vector whose pinned answer is **not** the correctly rounded value of
    its own formula, found by the exact reference of item 12. That column is empty where a function copies or answers
    a case, and for a point's `coordinates` and the `normalise` of a point and of a sphere, which are one division for
    each coefficient and so are correctly rounded (D85 item 7).

    | function | vectors | wrong ports, each with a vector of this function it fails | not correctly rounded at |
    |---|---:|---|---|
    | `Blade.join` | 6 | order `general-1`; fused `general-1`; seeded `whose-every-term-is-a-negative-zero` | `general-1` |
    | `Blade.meet` | 9 | order `general-1`; fused `general-1`; seeded `of-a-sphere-and-a-plane` | `general-1` |
    | `Blade.inverse` | 13 | reciprocal `general-of-a-sphere-1`; order `general-of-a-sphere-1`; fused `general-of-a-sphere-1`; divisor `of-a-null-vector` | `general-of-a-line-1` |
    | `Blade.meetWithin` | 12 | reciprocal `general-a-plane-1001`; order `general-a-plane-1`; fused `general-a-plane-1`; divisor `a-null-span`; and 1 more | `general-the-whole-space-1` |
    | `Point.up` | 11 | half-square `sums-left-to-right` | `general-1` |
    | `Point.coordinates` | 11 | reciprocal `divides-and-never-multiplies-by-a-reciprocal`; divisor `of-a-zero-weight` |  |
    | `Point.weight` | 5 | a wrong index, or a zero whose sign is lost: `is-signed` |  |
    | `Point.normalise` | 10 | reciprocal `divides-and-never-multiplies-by-a-reciprocal`; divisor `of-a-zero-weight` |  |
    | `Point.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |  |
    | `Point.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |  |
    | `PointPair.through` | 5 | fused `general-1` | `general-2` |
    | `PointPair.weight` | 12 | norm `whose-squares-overflow`; norm-order `general-1002`; fused-norm `general-1026`; rounded-norm `general-1012` | `general-2` |
    | `PointPair.normalise` | 10 | reciprocal `three-four-five`; norm `general-2`; norm-order `general-1002`; fused-norm `general-1026`; and 2 more | `general-1` |
    | `PointPair.centre` | 11 | reciprocal `general-of-a-meet-1`; order `general-of-a-meet-1`; grouping `general-1`; fused `general-1`; and 1 more | `general-of-a-meet-1` |
    | `PointPair.squaredRadius` | 14 | reciprocal `general-1002`; order `general-of-a-meet-1`; fused `general-of-a-meet-2`; divisor `of-a-flat-point` | `general-of-a-meet-1` |
    | `PointPair.points` | 25 | reciprocal `general-of-a-meet-1`; order `of-a-weighted-pair`; grouping `general-1000-at-a-further-threshold`; fused `of-a-weighted-pair`; and 6 more | `general-1` |
    | `PointPair.kind` | 35 | reciprocal `general-of-a-line-and-a-plane-1`; order `general-1001-at-a-threshold-it-names`; fused `general-1001-at-a-threshold-it-names`; norm `of-a-round-whose-divisor-underflows`; and 9 more | `general-of-a-line-and-a-plane-1` |
    | `PointPair.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |  |
    | `PointPair.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |  |
    | `Line.through` | 5 | fused `general-1` | `general-2` |
    | `Line.ofPointDirection` | 17 | reciprocal `divides-and-never-multiplies-by-a-reciprocal`; fused `general-1`; norm `divides-and-never-multiplies-by-a-reciprocal`; norm-order `general-2`; and 3 more | `general-1` |
    | `Line.weight` | 9 | norm `whose-squares-overflow`; norm-order `general-1002`; fused-norm `general-1026`; rounded-norm `general-1012` | `general-2` |
    | `Line.normalise` | 9 | reciprocal `three-four-five`; norm `general-2`; norm-order `general-1002`; fused-norm `general-1026`; and 2 more | `general-1` |
    | `Line.direction` | 4 | a wrong index, or a zero whose sign is lost: `is-read` |  |
    | `Line.moment` | 4 | a wrong index, or a zero whose sign is lost: `is-read` |  |
    | `Line.nearestPoint` | 13 | reciprocal `general-of-a-unit-line-1`; order `general-1001`; fused `general-3`; divisor `of-the-zero-blade` | `general-1` |
    | `Line.kind` | 17 | norm `general-1001-at-a-threshold-it-names`; norm-order `general-1014-at-a-threshold-it-names`; fused-norm `general-1026-at-a-threshold-it-names`; rounded-norm `general-1014-at-a-threshold-it-names`; and 3 more |  |
    | `Line.meet` | 28 | reciprocal `general-within-a-threshold-2`; order `general-within-a-threshold-1`; fused `general-at-tau-zero-1`; norm `general-at-tau-zero-1000-at-a-threshold-it-names`; and 7 more | `general-at-tau-zero-1` |
    | `Line.meetPlane` | 6 | order `general-2`; fused `general-2` | `general-1` |
    | `Line.meetSphere` | 9 | order `general-1`; fused `general-1` | `general-1` |
    | `Line.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |  |
    | `Line.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |  |
    | `Circle.through` | 6 | order `general-1`; grouping `general-1`; fused `general-1` | `general-1` |
    | `Circle.ofCentreRadiusNormal` | 22 | reciprocal `general-2`; order `general-3`; fused `general-1`; norm `general-3`; and 7 more | `general-1` |
    | `Circle.weight` | 13 | norm `general-3`; norm-order `general-1`; fused-norm `general-1033`; rounded-norm `general-1` | `general-from-parameters-1` |
    | `Circle.normalise` | 13 | reciprocal `general-1`; norm `general-3`; norm-order `general-1`; fused-norm `general-1033`; and 2 more | `general-from-parameters-1` |
    | `Circle.centre` | 12 | reciprocal `general-from-parameters-1`; order `general-1`; grouping `general-1`; fused `general-from-parameters-1`; and 1 more | `general-1` |
    | `Circle.squaredRadius` | 12 | reciprocal `general-2`; order `general-1`; fused `general-1`; divisor `of-three-collinear-points` | `general-1` |
    | `Circle.kind` | 33 | reciprocal `general-from-parameters-1008-at-a-threshold-it-names`; order `general-from-parameters-1000-at-a-threshold-it-names`; fused `general-from-parameters-1006-at-a-threshold-it-names`; norm `of-a-round-whose-divisor-underflows`; and 9 more |  |
    | `Circle.plane` | 12 | a normalised answer `general-1`; a wrong index or sign `of-the-unit-circle` |  |
    | `Circle.power` | 19 | reciprocal `general-2`; twice `general-1005`; order `general-1`; fused `general-1`; and 2 more | `general-1` |
    | `Circle.side` | 28 | reciprocal `general-from-parameters-1000-at-a-threshold-it-names`; twice `general-1007-at-a-threshold-it-names`; order `general-1001-at-a-threshold-it-names`; fused `general-1001-at-a-threshold-it-names`; and 6 more |  |
    | `Circle.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |  |
    | `Circle.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |  |
    | `Plane.through` | 5 | order `general-1`; grouping `general-1`; fused `general-1` | `general-1` |
    | `Plane.ofNormalDistance` | 14 | reciprocal `divides-and-never-multiplies-by-a-reciprocal`; norm `divides-and-never-multiplies-by-a-reciprocal`; norm-order `general-1010`; fused-norm `general-1006`; and 2 more | `general-2` |
    | `Plane.weight` | 9 | norm `general-3`; norm-order `general-1`; fused-norm `general-1033`; rounded-norm `general-1` | `general-from-parameters-1` |
    | `Plane.normalise` | 10 | reciprocal `three-four-five`; norm `general-3`; norm-order `general-1`; fused-norm `general-1033`; and 2 more | `general-from-parameters-1` |
    | `Plane.normal` | 7 | a wrong index, or a zero whose sign is lost: `is-read` |  |
    | `Plane.distance` | 7 | a wrong index, or a zero whose sign is lost: `is-read` |  |
    | `Plane.nearestPoint` | 11 | reciprocal `general-from-parameters-1`; order `general-1`; fused `general-1000`; divisor `of-a-plane-at-infinity` | `general-1` |
    | `Plane.kind` | 19 | norm `general-1003-at-a-threshold-it-names`; norm-order `general-1033-at-a-threshold-it-names`; fused-norm `general-1033-at-a-threshold-it-names`; rounded-norm `general-1033-at-a-threshold-it-names`; and 3 more |  |
    | `Plane.signedDistance` | 19 | reciprocal `general-from-parameters-1002`; twice `general-1`; order `general-3`; fused `general-from-parameters-1`; and 5 more | `general-1` |
    | `Plane.side` | 29 | reciprocal `general-1006-at-a-threshold-it-names`; twice `general-1000-at-a-threshold-it-names`; order `general-1001-at-a-threshold-it-names`; fused `general-1001-at-a-threshold-it-names`; and 10 more |  |
    | `Plane.meet` | 9 | fused `general-1` | `general-1` |
    | `Plane.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |  |
    | `Plane.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |  |
    | `Sphere.through` | 6 | order `general-2`; grouping `general-1`; fused `general-1` | `general-1` |
    | `Sphere.ofCentreRadius` | 18 | half-square `general-2`; radius `squares-the-radius-first` | `general-1` |
    | `Sphere.weight` | 11 | a wrong index, or a zero whose sign is lost: `of-the-unit-sphere` |  |
    | `Sphere.normalise` | 13 | reciprocal `divides-and-never-multiplies-by-a-reciprocal`; divisor `of-four-coplanar-points` |  |
    | `Sphere.centre` | 11 | reciprocal `general-2`; divisor `of-four-coplanar-points` | `general-1` |
    | `Sphere.squaredRadius` | 12 | reciprocal `general-3`; order `general-1`; fused `general-1`; divisor `of-four-coplanar-points` | `general-from-parameters-2` |
    | `Sphere.kind` | 33 | reciprocal `general-1148-at-a-threshold-it-names`; order `general-from-parameters-1000-at-a-threshold-it-names`; fused `general-from-parameters-1000-at-a-threshold-it-names`; norm `of-a-round-whose-divisor-underflows`; and 9 more |  |
    | `Sphere.power` | 19 | reciprocal `general-from-parameters-2`; twice `general-1002`; order `general-1`; fused `general-from-parameters-1`; and 2 more | `general-1` |
    | `Sphere.side` | 27 | reciprocal `general-from-parameters-1014-at-a-threshold-it-names`; twice `general-1012-at-a-threshold-it-names`; order `general-1001-at-a-threshold-it-names`; fused `general-1001-at-a-threshold-it-names`; and 6 more |  |
    | `Sphere.meet` | 10 | fused `general-1` | `general-1` |
    | `Sphere.meetPlane` | 9 | fused `general-through-points-1` | `general-1` |
    | `Sphere.toMultivector` | 4 | a wrong index, or a zero whose sign is lost: `keeps-each-zero` |  |
    | `Sphere.ofMultivector` | 4 | a wrong index, or a zero whose sign is lost: `of-the-powers-of-two` |  |

    **One go-red for each degenerate result.** Seventy-seven committed vectors, each a named case, a refusal or a
    side of a threshold, are shown red under the one member that names it: a tangent pair and an imaginary one at
    their thresholds (`never`, `always`); the flat point, the pair at infinity and the zero blade; three collinear
    points and their line; four coplanar points and four of one circle; a line and a plane at infinity; two lines
    that cross, that are parallel, that are one line, and the skew pair at its threshold; each threshold from within
    (`strict`) and from above (`above`); each zero divisor (`divisor`) and each zero direction (`direction`); a sign
    decided where the quotient underflows, in a test and in a round's reality (`quotient`); a zero power
    (`zero-sign`); a join whose every term is a negative zero (`seeded`); a centre grouped from the right, in a
    pair's points (`grouping`); a norm summed from the right (`norm-order`). A tangent and an imaginary round at
    `τ = 0` are decided by the signs, which is not a member, so their witnesses are the same rounds at a threshold
    above zero. A separate case answers each degenerate configuration through the published functions and holds the
    named case, with what it carries.

11. **The closed form, and the bound, which is one sample's maximum.** D85 item 7 fixes the form of a bound and no
    number, and allows the largest error over a stated sample. *The sample:* seed 728, 2,000 configurations drawn by
    the law family's generator; in each, two lattice points of the sphere of radius 3, 7 or 9 about the origin, a
    centre with integer coordinates up to twenty, a line through the two points given by another of its points and an
    integer multiple of their difference; 1,948 of them have two different points. *The reference:* the two points,
    which are exact by construction; item 12's script solves each line against each sphere by the quadratic formula
    in rationals and holds that the roots are those points, in the line's order. *The route:*
    `Line.ofPointDirection`, `Sphere.ofCentreRadius`, `Line.meetSphere`, `PointPair.points` at `τ = 0`,
    `Point.coordinates`. *The measurement:* every configuration answers two real points in the line's order; the
    largest error of a coordinate is **292 units in the last place of its reference**, and where the reference is
    zero **`104 × 2^-52`, absolute, in length**. *The margin:* none, and so **the figure is this sample's and no
    other's.** It is not a bound on the function. The same sample drawn at other seeds measures:

    | seed | largest error, units in the last place | largest absolute error at a zero reference, `× 2^-52` |
    |---:|---:|---:|
    | 728 (the stated sample) | 292 | 104 |
    | 1 | 162 | 152 |
    | 2 | 192 | 192 |
    | 3 | 224 | 136 |
    | 590 | 264 | 136 |
    | 727 | 160 | 160 |
    | 729 | 192 | 115.5 |
    | 1000 | 196 | 152 |
    | 4242 | 256 | 256 |

    The relative figure stays within the stated sample's at these eight; the absolute one does not, and at seed 4242
    it is more than twice it. A case holds all nine rows. **So the law does not take its sample from the seed it is
    run at**: `GeometricAlgebraLaws.laws seed n` runs the closed-form law on the stated sample whatever `seed` is,
    and no law of the family goes red at another seed. All twenty-seven were run at seeds 1, 2, 3, 590, 727, 729, 1000
    and 4242 with 2,000 iterations each and held. The answers are pinned bits, so every host measures these numbers.
    The error is rounding in a coordinate that is small beside the others it is computed from: 292 units in the last
    place of a coordinate near `1` beside a centre near `20` is about `6·10^-14`. This is the part of "an answer beside
    its closed form" that this phase ships.

12. **How each family was cross-checked.** By a route that shares no code with the package. A script that is scratch
    work and not part of this change takes Phase 727's route for the algebra (the orthonormal basis, blades as
    bitmaps, exact rationals, the table derived from it and held to D85's three digests) and writes each of the
    seventy functions again from D85 items 4 to 6, once, over a number type. *In binary64,* in the record's order, it
    replays the four vectors of item 8 and all 836 others, refusals and kinds included, **bit for bit, with no
    mismatch**. *In exact rationals* (a root to 400 binary places by integer square root) it evaluates the same
    formula on the same operands: forty-six vectors answer another case there, each one authored to overflow or
    underflow or asked at a threshold a rounded statistic names, and the rest give each function's largest distance from its exact value and the last column of the
    table. It replicates the sample of item 11 and measures the same two numbers. It does not prove an identity.

13. **Where the suite and the registrations moved.** `ConformalTests.fs` is new, the family "Conformal objects".
    Phase 727's `GeometricAlgebraTests.fs` is edited where its rosters read the surface: the law count, the modules
    the operation census reads, the package's top-level names, and how a vector's `op` names its function; and it
    gains the witnesses of item 8. `GeometricAlgebraOperations.exactness` has a `Pinned` row for each of the seventy
    functions, eighty-nine in all, held to the surface both ways and each function to a vector. A source-reading
    case holds the **operation modules** (everything but the law family) to a census: every float literal token they
    write and every float they name, comments stripped and strings emptied, equal to a stated account of each, how
    often it is written and what it is there for. `Multivector.fs` writes `0.0` nine times and `1.0` once;
    `Conformal.fs` writes `0.0` nineteen times, eight of them in a comparison D85 item 6 states (a divisor, a
    direction's norm and a threshold against zero, `τ ≥ 0`, a radius below zero, a zero numerator and the two signs
    at `τ = 0`), four inside the norm, and seven as a coefficient a vector does not hold; and `0.5` twice, `2.0` once
    and `1.0` once, none in a comparison. The floats that are named: `Double.NaN` once in each of those two modules,
    `Double.PositiveInfinity` once in the norm, and two names that are not floats there and are counted because the
    census cannot tell: `infinity`, the integer position of `n∞` (six times in `Basis.fs`, four in `Conformal.fs`),
    and `nan`, a boolean inside the norm (three times). No other module writes one. **The census is lexical.** It
    reads a literal with a decimal point, an exponent, underscores, a suffix (`1.0f`, `1e-9M`) or as a hexadecimal
    double, and the names `infinity`, `nan`, `NaN`, `PositiveInfinity`, `NegativeInfinity`, `MaxValue` and
    `MinValue`; and it refuses `defaultArg`, `Option.defaultValue`, `defaultWith`, `ValueOption.default…`, `Epsilon`,
    an optional parameter, a `float` conversion of an integer or a string literal, and two tokens outright, since no
    operation module has a use for either: `double`, the other name of that conversion, and any name that holds
    `Parse` (`Double.Parse`, `TryParse`), whatever it is given. It is shown red on twenty-four shapes, among them a
    default argument, a constant with a neutral name, the literal `1.`, an operator section, a comparison split over
    lines, a match on `0.0`, `0x…LF`, `1_000.0`, `Double.Epsilon`, `float "1e-9"`, `float 1 / float 1_000_000_000`,
    `double 1 / double 1_000_000_000`, `Double.Parse "1e-9"`, `System.Double.TryParse text` and `infinity`. _(The two
    tokens and their three shapes were added in Phase 729: this list first said a `float` conversion was refused and
    did not say that `double` and a parsed string literal were not.)_ **What it cannot see:** a float that reaches
    these modules without a token of its own in them. A constant defined in another assembly, a value built from bits
    (`BitConverter.Int64BitsToDouble`), one computed from accounted literals alone (`0.5 * 0.5 * …` raises the count
    of `0.5` and is seen; a loop that halves `1.0` is not), a `float` conversion of an integer held in a variable
    (`float n`), and a conversion that is neither of the two names nor a parse (`Convert.ToDouble`, an `unbox`) are
    all outside it. It is a regular expression and not the compiler's lexer.
    Another holds the object modules to no host call but the root. `api/Fuaran.Compute.GeometricAlgebra.txt`
    is regenerated and no other baseline moves; every other law file is byte-identical; `gate-reads.json` does not
    move.

14. **Not here.** The versor normalisation, the third of D85 item 5, ships with the versors (Phase 729). No
    specialised product and no timing (Phase 730). `Point.kind` (no caller reads a vector's kind yet; `Line.meet`
    reads it inside). The right contraction, a general inverse, and everything D85 item 10 keeps out.

**Ladder.** `proofs.json` gains `conformal-objects-pinned-by-recorded-rules` at `tested`, listing this family's cases,
each held to a test of that name. It says what guarantees the bits, what the suite holds of a wrong port in the
words of the case that holds it (a red vector, or a drawn search that finds the port unable, and not that a port
fails every function it could change), that the identities are tested on exact inputs and
not proved, and that the closed-form agreement is a measurement and no theorem. Phase 727's row loses the sentence
that said the conformal objects did not exist. `ProofsLadderTests` is not edited.

**What a consumer does.** Nothing: no existing function's answer moves, and the version stays `0.47.0`, a draft this
addition rides. `STABILITY.md` says what a caller should know first.

**Tidy.** Reviewed every file this change touches. *Removed:* the suite's listing of red vectors by port, which no
case read once the claim was computed, and the pattern hunt for comparisons, which the census replaces. *Reused instead of minted:* the
suite's go-red helpers stay in Phase 727's file, and the carried witnesses were added there and not beside a copy of
them. *Considered and kept:* the six pairs of functions that turn a typed object into its list of coefficients and
back, which exist in the package (internal) and again in the suite's vector file, because that file compiles under
node from the published surface alone and writes the record's field order a second time on purpose; the suite's two
lookup tables from a vector's `op` to a function, one over the published functions and one over the doors, for the
same reason; the three private Park and Miller generators D86 kept; and `Blade.join` as a named function for one outer
product, because D85 item 4 names the join as an operation that ships.

## 2026-11-24 — D86: the multivector and its products ship as `Fuaran.Compute.GeometricAlgebra`, written to D85 and choosing nothing D85 fixed — the names D85 left; the table built by D85 item 3's rule and held to its three digests; one fold kernel that takes its rule as an argument, so every go-red witness is the shipped fold with one rule changed; a law family that says which comparison each law holds under; 167 vectors, ninety-six of which spell out every signed term; the ladder's row moves from rules recorded to tested

**Context.** Phase 727. D85 recorded every convention of the geometric algebra before any kernel. This is the first
kernel: the multivector over `Cl(4,1)` and the products every later phase is written in. It implements D85 items 2, 3,
7 and 8 and the part of item 4 that is the products, the involutions and the dual. It chooses no sign, scale, order,
orientation or zero rule: where a choice below looks like one, the item of D85 that makes it is cited. What D85 left to
this entry is names, the layout of the modules, and how the suite shows that a wrong port fails.

**Premise check** (on `dfbdd30`, the tree this phase was cut from). `DECISIONS.md` opens with D85 and `src/` holds no
geometric algebra directory. D85 item 9 records the package id; items 2 and 3 fix the basis, the blade order and the
fold. `proofs.json` holds `geometric-algebra-pinned-by-recorded-rules` at `policy` and `ProofsLadderTests` reads it
against the project file. The tree declares `0.47.0` and no `v0.47.0` tag exists, so the slot is a draft and this
addition rides it. Phase 623 (method cards) and Phase 666 (step vectors) have not shipped, so this phase adds neither a
card nor a step vector: those phases' rosters gain these functions. All five held. One thing the phase's own list of
registrations did not name was found: the suite holds two more rosters a new packable package must be on, the coverage
declaration and the wire roster, and the node leg's list of documents is in the linear-algebra suite. Each gained its
one entry (item 11).

**Ruled.**

1. **The surface, and the names.** One type and one module of functions, in the namespace of the package id (D85 item
   9). `Multivector` is the sealed class of D85 item 8. The module `Multivector` holds:

   | function | what it is | D85 |
   |---|---|---|
   | `zero` | the zero multivector, a value | item 2 |
   | `init f` | the coefficient at index `i` is `f i`, called once for each index, ascending | item 2 |
   | `ofScalar s`, `basis i` | `s` at index 0; `1.0` at index `i`; `+0.0` elsewhere | item 2 |
   | `coefficient i a`, `toArray a` | one coefficient; all thirty-two, as a fresh array | item 8 |
   | `grade g a` | the grade projection: a copy of one grade's run, `+0.0` elsewhere | item 4 |
   | `add`, `subtract`, `scale s a` | one rounded `+`, `−` or `×` for each coefficient, the scalar first | item 3 |
   | `negate`, `reverse`, `gradeInvolution` | exact negation of every blade, of grades 2 and 3, of the odd grades | items 2, 4 |
   | `dual`, `undual` | the signed permutation of item 2's last column, and its inverse | item 4 |
   | `geometricProduct`, `outerProduct`, `leftContraction` | item 3's fold over the table, and over its two selections | items 3, 4 |
   | `scalarProduct`, `squaredNorm` | the scalar coefficient of `A B`; `A ∗ Ã` | item 4 |

   A product is named in full. *Rejected:* `geometric`, `outer`, `inner` and `dot` (D85 item 4 publishes one inner
   product and names it the left contraction, and a function called `inner` or `dot` would invite the reading it
   rules out); `wedge` and `lc` (a twin author reads the record's words, not a library's abbreviations); operators
   (item 8). The sum, the difference, the scalar multiple and negation are in this phase although the phase's own
   list of what ships does not name them: D85 item 7 classifies them among the published operations, distributivity
   and the signature laws cannot be stated without them, and an algebra with no addition is not one. No named values
   for `n0`, `e1`, `e2`, `e3`, `n∞` or the pseudoscalar are published: `basis 1` to `basis 5` and `basis 31` are
   those, the index is the record's, and a second name for each is surface with no reader yet. No function publishes
   a blade's name, grade or signs: the order is D85 item 2's table, which a port transcribes, and the law file's
   head carries the thirty-two names in order.

2. **Nothing refuses, and one thing raises.** No function of this phase returns `Result`. Construction is `init`,
   which is total: there is no array whose length could be wrong. *Rejected:* `ofArray : float[] -> Result<_, _>`,
   a refusal and a union case for a mistake `init` makes impossible; `Matrix.init` is the precedent. A blade index is
   an argument and not data, so `basis` and `coefficient` raise `IndexOutOfRangeException` outside `0 .. 31`, as an
   array's read would: D50 item 8 ruled the same for a matrix's shape and its reads. *Rejected:* `Result<float, _>`
   from a coefficient read, which makes every loop over a multivector a match on a case that cannot occur. A grade
   is an argument too, but its projection is total: a grade the algebra does not have holds no blade, so `grade g`
   below zero or above five answers the zero multivector, which is what `⟨A⟩_g` is. So the names of refusal cases
   that D85 leaves to the phases begin in Phase 728, which has the first refusal.

3. **The table is built, not written.** `Basis` enumerates the blades by D85 item 2's rule (the subsets of
   `n0 e1 e2 e3 n∞`, by size and then lexicographically) and builds the 1,024 terms by item 3's rule: each blade's
   Euclidean factor and null factor, `λ`, `κ`, the parity of the sort, and the four-by-four table of the null
   factors, written once as sixteen cases. No table of terms, signs or indices is a literal anywhere in the package.
   The suite renders the three tables as item 3 says (a line feed after each line whatever the platform) and holds
   their term counts, byte counts and SHA-256 to the record's. The outer product's and the contraction's terms are
   selected from the geometric product's by the grades, in the order they have there, and the scalar product's are
   those whose output is the scalar. **The dual's permutation is read off the table:** a blade's one term against the
   pseudoscalar, with the opposite sign because `I⁻¹ = −I`; item 4 says the dual is that product. It is not a second
   transcription. The suite holds it, the blades, the grades and the two involutions' signs to item 2's table,
   transcribed once in the suite as the record writes it, and holds the scalar coefficient's thirty-two terms to the
   line item 3 writes out. *Rejected:* a literal table generated by a script (a second artefact to keep equal to the
   rule, and nothing a reader can check against the record but its digest); deriving the dual from the contraction's
   terms (the same thing through a selection that keeps exactly those terms).

4. **One kernel, and its rule is an argument.** Every product is `Kernel.foldWith start enter terms a b`: an array
   of thirty-two accumulators created at `start`, then one loop over `terms` in order, each term entering its
   output's accumulator through `enter`. The shipped products pass `+0.0`, `Kernel.twoRoundings` (`p = x × y`, then
   `r + p` or `r − p`) and one of the four term arrays. The involutions, negation and the duals are
   `Kernel.signedPermutationWith negate target negative a`, and pass `Kernel.exactNegation` (`−x`). Nothing is
   specialised: the geometric product visits all 1,024 terms on every call, and that loop is the reference Phase 730
   holds a specialised product to. **The rule is an argument for one reason: a go-red witness must be this code.**
   The suite calls the same kernels with one argument changed (item 9). A witness written as a second fold in the
   suite can agree with itself and omit a step the shipped path applies; one that calls the shipped kernel cannot.
   The kernels are `inline` so that a product pays no closure call for it: the reference a later measurement is taken
   against is the plain loop. An inline member cannot be used from another assembly when it reads internal ones, so
   `Kernel.productUnder`, `scalarUnder` and `permutedUnder` are the same kernels compiled in the package with the
   rule left open, internal, and the suite reaches them through `InternalsVisibleTo`. Nothing of this is public.
   *Rejected:* a kernel with a closure argument and no `inline` (a virtual call for each of 1,024 terms in the
   reference); a test-only copy of the fold (the defect the rule above exists to prevent); compile-time switches.

5. **No fused multiply-add: what was read.** `twoRoundings` binds the product before it adds. On .NET the JIT does
   not contract the two (D49 item 2). Under Fable 5.0.0 the emitted JavaScript for the geometric product is a `for`
   loop from index 0 over the term array, whose body stores `Kernel_twoRoundings(term.Negative, r[term.Out],
   a[term.Left], b[term.Right])` through Fable's checked array reads, and that function is `const p = x * y; if (negative) return r - p; else return
   r + p;`. JavaScript has no contraction, the loop is the table's order, and the accumulators are a `Float64Array`
   filled with `0`. The node leg renders the law file byte for byte (item 9).

6. **A NaN is canonical where a value leaves.** `Kernel.adopt` rewrites each NaN of a fresh array to `Double.NaN`
   and wraps it; every function that computes or negates ends in it, and the two that return a float end in
   `Kernel.canonical`. `init` ends in it too, so storage never holds another NaN, and the functions that only copy
   (`grade`, `coefficient`, `toArray`) need no pass. It is applied once, on the way out, never inside the fold
   (D85 item 3).

7. **`Equals` and the hash are D85 item 8's, and item 6 gains one phrase.** `Equals` walks the thirty-two pairs and
   holds when each is equal as numbers or both are NaN. The hash is item 8's as written, so it tests a coefficient
   against zero: `x = 0.0`. D85 item 6 lists every comparison that may name `0.0`, and Phase 728's suite reads the
   sources against that list; the list omitted this one, which item 8 requires. **This phase adds the phrase "the
   zero test inside `Multivector`'s hash (item 8)" to item 6's list**, and changes nothing else in D85. The package's
   operation modules name `0.0` in no other comparison; the law family is not one of them, and compares bits against
   float literals, as a law must (the scope was named in Phase 728, D87 item 8: the sentence said "sources"). Under Fable the class's `Equals` is called by `=` and its
   `GetHashCode` by `hash`; the law family holds both on each host (item 8 here).

8. **The law family, and which comparison each law holds under.** `GeometricAlgebraLaws.laws seed count` runs
   eighteen laws on whichever host calls it; the suite runs them on .NET and the node leg under JavaScript, at seed
   590 and 200 iterations. Each law's name says its comparison (D85 item 7).

   - *Bit for bit, on exact inputs* (multiples of a quarter no larger than eight, a zero of either sign among them):
     the signature (`e_i² = 1`, `n0² = n∞² = 0`, `n0 ∗ n∞ = −1`, `½ n∞ − n0` and `½ n∞ + n0` squaring to `+1` and
     `−1`, `I² = −1`); associativity; distributivity on both sides; `a ∧ a = 0` for a vector;
     `(A ∧ B) ∗ C = A ∗ (B ⌋ C)`.
   - *Under the float identity, on exact inputs:* `rev (A B) = rev B rev A`, with `rev (e1 e2)` holding nineteen
     negative zeros and `rev e2 rev e1` none; `(A ∧ B)* = A ⌋ B*`, with sixteen against none at `e1`, `e2`; the dual
     against `A ⌋ I⁻¹` written as a fold, and against `A I⁻¹` and `I⁻¹ A`; the undual against `A ⌋ I`. The counts
     are the ones D85 item 7 gives, and they are asserted: they are why these laws are not bit for bit.
   - *Bit for bit, on general inputs, because exact by construction:* the undual of a dual and the dual of an undual
     (infinities and the least subnormals among the coefficients); each involution applied twice; a grade projection
     as a copy; a sum, a difference and a scalar multiple as one rounded operation each; the scalar product as the
     scalar coefficient of the geometric product and the squared norm as `A ∗ Ã`; and, for `A` of one grade and `B`
     of one grade, `A ∧ B` and `A ⌋ B` as the grade parts of `A B`.
   - *Structural:* the table's shape (1,024 terms, thirty-two for each output, 128 pairs with two, 243 and 243 and
     32 selected); no product returns a negative zero, over operands that are mostly zeros of both signs; a NaN
     leaves as `Double.NaN`'s bits, whether it was given, made by a zero against an infinity, or had its sign moved
     by a negation; and equality is the float identity with equal multivectors hashing alike.

   The laws of D85 item 7 that are not here concern operations that do not exist yet: `up`, the objects, the meet
   and the versors (Phases 728 and 729). **The identities are tested and not proved.** On general inputs they hold
   only to rounding and this phase states no bound, because it ships no operation whose contract is one: what is
   pinned on general inputs is the bits, by the vectors.

9. **The vectors, and the wrong port each exists to catch.** `conformance/laws/geometric-algebra-laws.json` holds 167
   vectors over the published functions, rendered through the one corpus writer, each float its bit pattern. Every
   expected value is the shipped .NET path's answer. Three kinds.

   - **Ninety-six table rows** (`table-geometric-NN`, `table-outer-NN`, `table-contraction-NN`): the basis blade of
     index `NN` times the multivector whose coefficient at index `j` is `2^j`. An output coefficient is then the
     signed sum of the `2^j` whose term reaches it, at most two of them, exactly. So the thirty-two rows of a product
     are its whole table: each term and its sign is one bit of one answer.
   - **Twenty-six hard inputs**, each authored for one rule.
   - **Forty-five general inputs**, drawn by a pinned generator (Park and Miller's, every step exact) and written
     into the file, so a port reads the operands and reproduces only the answers: dense operands at one scale and
     across sixteen, forty and eighty binary orders of magnitude; operands that are two-thirds zeros of both signs;
     one with a NaN, one with an infinity; products that underflow into the subnormals and past them; products that
     overflow, where an infinity of each sign in one sum is a NaN in this order; and the copies over one operand
     holding both zeros, an infinity and a NaN.

   `init` and `toArray` have no vector of their own: every vector is read through them.

   Each wrong port, the vector that catches it, and how the suite shows it. Every witness runs the shipped kernel
   with exactly the one argument named, renders it as the shipped answer is rendered, and asserts two things on the
   committed file's line for that vector: the shipped rule's answer is on it, and the witness's is not.

   | a port that | is caught by | the witness: the shipped kernel with |
   |---|---|---|
   | (a) sums in another order | `geometric-absorbed-then-cancelled` (the scalar is `((2^60 + 1) − 2^60) + 1 = 1`; with the right index outermost it is `2`, and descending `0`), `scalar-absorbed-then-cancelled`, `geometric-general-3`, `geometric-general-4`, `scalar-general-2` | the term array sorted by `(j, i, k)`, and reversed |
   | (b) fuses a multiply-add | `geometric-fma-witness` and `scalar-fma-witness` (`−1 + (1 + 2^-30)(1 − 2^-30)`: `+0.0` in two roundings, `−2^-60` fused), `geometric-general-1`, `outer-general-1`, `contraction-general-1` | `enter` replaced by the host's fused multiply-add |
   | (c) seeds a fold from its first term | `geometric-every-scalar-term-a-negative-zero`, `scalar-every-term-a-negative-zero` (every one of the thirty-two signed terms is `−0.0`; the fold answers `+0.0`) | `start` at `−0.0`, which is a fold seeded from its first term exactly wherever an output has a term: `−0.0 + p = p` and `−0.0 − p = −p` for every `p` |
   | (d) skips a zero's term | `geometric-`, `outer-`, `contraction-` and `scalar-zero-against-infinity` (a NaN wherever a zero meets the infinity) | `enter` returning the accumulator when a factor is zero |
   | (e) has one wrong table sign | the table row of the term's left blade | the term array with that one sign flipped: all 1,510 terms of the three tables, one at a time; and with each term left out; and, for the two selections, with each of the 781 terms they do not keep, kept |
   | (f) returns `−0.0` where the fold gives `+0.0` | the vectors of (c) | as (c) |
   | (f) returns `+0.0` where an involution gives `−0.0` | `negate-zeros`, `reverse-zeros`, `reverse-positive-zeros`, `gradeInvolution-zeros`, `dual-zeros`, `dual-positive-zeros`, `undual-zeros` | `negate` replaced by `0.0 − x`, which is `−x` everywhere but at `+0.0` |

   Two things the same tests hold the other way, because D85 item 3 states them and a later phase stands on them.
   Gathering one output at a time, the term array sorted by `(k, i, j)`, answers every product vector's bits: it is
   another loop and not another order. And over finite operands a fold that skips a zero's term answers the same
   bits, on every finite product vector of the file: that is the statement a specialised product relies on.

   **How each family was cross-checked.** By a route that shares no code with the package: the algebra over the
   orthonormal basis `e1 e2 e3 e+ e−`, blades as bitmaps, in exact rational arithmetic, in a script that is scratch
   work and not part of this change. It builds the thirty-two stored blades as outer products of `n0 = ½ (e− − e+)`
   and `n∞ = e− + e+`, expresses every product of two of them back in stored blades by solving exactly, and so
   derives the table without item 3's rule. *The table:* 1,024 terms, every coefficient `±1`, and the three
   renderings hash to the three digests of D85 item 3. *The selections:* each equals the grade part of the product
   taken in the orthonormal basis, pair by pair. *The dual:* `A I⁻¹` and `I⁻¹ A` agree and equal item 2's last
   column. *Every vector:* replayed in binary64 with that table in item 3's fold, all 167 answer the committed bits.
   *The exact answers:* for the 123 product vectors with finite operands, the exact rational product through the
   orthonormal basis: the ninety-six table rows equal it exactly, and the largest distance of any other answer from
   it is 4.98 units of `2^-53` of the sum of the magnitudes of that output's terms (`geometric-general-2`), with the
   two underflowing vectors inside thirty-two half least subnormals more. The script fails when one hex digit of one
   expected value is changed. The node leg renders the same 180,861 bytes as .NET.

10. **The operation table and the source rule.** `GeometricAlgebraOperations.exactness` has a `Pinned` row for each
    of the nineteen functions of item 1 (`zero` is a value), and the suite holds it equal to the module's public
    functions both ways, and holds every one of them but `init` and `toArray` to a vector. A source-reading test
    holds the four source files to no host mathematics call, and is shown to read a fused multiply-add, a root, a
    power and an absolute value where there is one. The package publishes no operator, and no top-level name
    another Compute package declares (D74 item 2).

11. **Where the package is registered.** The solution, `pack.ps1`, the publish workflow's header (it packs the
    solution), the README's derived roster, `STABILITY.md` under the `0.47.0` draft, and
    `api/Fuaran.Compute.GeometricAlgebra.txt`. The boundary suite holds it to FSharp.Core alone, by project and by
    built assembly, through the rule D79 item 1 added (`below`, with an empty set): a `Fuaran.Core.*` package, and
    LinearAlgebra, the dataframe, the op package or, for now, Elementary as a project, are each shown to be a
    violation. Phase 729 states Elementary there when it takes `sinCos` and `atan2` (D85 item 9). The coverage
    declaration gains the package as `pinned-order-law-tested`, as the other pinned packages are, and the wire
    roster as a package that encodes nothing (D85 item 10). `gate-reads.json` does not move: the solution builds the
    project. No other baseline under `api/` moves, and every other law file is byte-identical.

12. **Not here.** The conformal objects, `up`, the meet and the join, a blade's inverse and the three normalisations
    (Phase 728); the versors, the exponential and the matrix (Phase 729); any specialised product, and any timing
    (Phase 730): no speed is claimed for the general product, which is written to be read; the op algebra (Phase
    731).

**Ladder.** `geometric-algebra-pinned-by-recorded-rules` moves from `policy` to `tested` in the change that adds the
project file, with no edit to `ProofsLadderTests`. It says that the pin guarantees the bits, that the identities are
tested on exact inputs and not proved, and that it asserts nothing of the packages and phases that do not exist yet.
It lists the cases that test it, and the suite holds each to a test of that name.

**What a consumer does.** Nothing, unless it wants the algebra: no existing package's surface or answers move, and
the version stays `0.47.0`, a draft this addition rides. `STABILITY.md` says what a caller should know first.

**Tidy.** Reviewed every file this change touches. Removed nothing: the change adds a package and one entry to each
roster, and makes no existing code dead, duplicate or obsolete. Considered and kept: the three private Park and
Miller generators that now exist in this package's law family, in the suite's vector file and in each sibling
package's law family, because a law family must run with FSharp.Core alone and the packages share no code by D85 item
9, and the vector file's draws are written into the document it renders; `ComputeBoundaryTests`' older `below` case,
whose message names the distributions, because the algebra has a case of its own beside it; and the sentence of
`gate-reads.json` that lists the node leg's packages, because that file moves only when what the gate builds does.

## 2026-11-23 — D85: the geometric algebra rules, recorded before any kernel lands — `Cl(4,1)` with `n0 · n∞ = −1`; thirty-two coefficients held over the null basis `n0 e1 e2 e3 n∞`, grade then lexicographic, every blade an ascending outer product; every product one fold from `+0.0` over one table of 1,024 signed terms, with no fused multiply-add and no term skipped on a coefficient's value; the dual is `A ⌋ I⁻¹`; an object is a direct blade and a versor is applied by its reverse; nothing is normalised unasked; a zero test is a threshold the caller passes; the package ids `Fuaran.Compute.GeometricAlgebra` and `Fuaran.Compute.GeometricAlgebraOps` are recorded as ruled

**Context.** Phase 726. The geometric algebra library is built as packages of this repository and held to its
pinned-numerics contract (operator ruling 2026-11-23). Its algebra is the conformal model of three-dimensional
Euclidean space, `Cl(4,1)`. A pinned library's conventions are part of its answers: the blade order decides the order
of every sum, the sign of the dual decides the sign of every `meet`, and the first law vector that is committed makes
each of them permanent. So they are written before the first kernel, as D49 wrote the linear-algebra rules before
Phase 590, and Phases 727 to 731 are held to "D85 item n" rather than argued from first principles. A twin
author porting the library reads items 1 to 7 as its contract. No source file moves and no public surface moves.

**Premise check** (on `cc3ca8f`, the tree this phase was cut from). No geometric algebra source exists: `src/` holds
`ColumnOps`, `Conformance`, `DataFrame`, `Distributions`, `Elementary`, `LinearAlgebra`, `PipelineQuery`,
`PipelineTree`, `Regression` and `Solvers`, and no tracked file names a multivector. The newest entry is D84
(Phase 725), so this one is D85. `Elementary.sin`, `cos`, `sinCos`, `atan` and `atan2` are in the `0.47.0` draft's
baseline (`api/Fuaran.Compute.Elementary.txt`). `proofs.json` and `ProofsLadderTests.fs` exist, and the test holds
every row to the closed forms and three premises by id. All four held. One reading of the phase's own text is
sharpened: it describes the algebra over `e1 e2 e3 e+ e−`, which item 1 keeps as the definition, and item 2 holds the
coefficients over the null basis instead and says why.

**The texts cited.** *DFM*: Dorst, Fontijne and Mann, *Geometric Algebra for Computer Science* (Morgan Kaufmann,
2007). *DL*: Doran and Lasenby, *Geometric Algebra for Physicists* (Cambridge University Press, 2003). *P*: Perwass,
*Geometric Algebra with Applications in Engineering* (Springer, 2009). *LHR*: Li, Hestenes and Rockwood,
"Generalized homogeneous coordinates for computational geometry", in *Geometric Computing with Clifford Algebras*
(Springer, 2001). *HS*: Hestenes and Sobczyk, *Clifford Algebra to Geometric Calculus* (Reidel, 1984).

**How the rules below were checked.** Every formula in items 1 to 6 that states a value was evaluated before it was
written here: in exact rational arithmetic, and the motor's exponential against its own series in binary64. The
product table of item 3 was built three times, by three routes that share no code (through the orthonormal basis;
by the rule of item 3 in a script; by the same rule in F#), to one digest. The programs are scratch work and are
not part of this change, which is this record, a ladder row and its test: what they establish is restated here so
that Phase 727 can re-derive each fact, and the digests let it show that it did.

**Ruled.**

1. **The algebra, the null pair and its normalisation.** The algebra is `Cl(4,1)`: orthonormal vectors `e1`, `e2`,
   `e3` and `e+` of square `+1`, and `e−` of square `−1`. The null pair is

   `n∞ = e− + e+` and `n0 = ½ (e− − e+)`, so `n0² = n∞² = 0` and **`n0 · n∞ = −1`**,

   and inversely `e+ = ½ n∞ − n0`, `e− = ½ n∞ + n0`. A Euclidean point `x` is the null vector
   `up x = n0 + x + ½ x² n∞`, with `up x · n∞ = −1` and `up x · up y = −½ |x − y|²`. The pseudoscalar is
   `I = n0 ∧ e1 ∧ e2 ∧ e3 ∧ n∞`, which is `e1 e2 e3 e+ e−`: `I² = −1`, `I⁻¹ = −I`, its reverse is itself, and it
   commutes with every element. `E = n0 ∧ n∞` squares to `+1`.

   This is DFM's convention (chapter 13: `o · ∞ = −1`, the point `o + p + ½ p² ∞`), LHR's, and P's, who writes the
   pair exactly as above. *Rejected:* DL's (chapter 10), `n = e + ē`, `n̄ = e − ē`, `n · n̄ = 2`, a point
   `x² n + 2x − n̄` with `X · n = −2` and `X · Y = −2 |x − y|²`. It differs from DFM's in the scale of the origin
   vector, by two, and in its sign. It loses on three counts. A point's Euclidean part is twice its coordinates, so
   every read-back halves. The geometric product of the null pair is `n0 n∞ = −1 + n0 ∧ n∞` here and `2 + n ∧ n̄`
   there, so the table of item 3 would carry entries of `±2` where it carries only `±1`, and a product of small
   integers would no longer be a sum of their products alone. And DFM's tables of rounds and flats, which items 4
   and 6 follow, are written in the convention chosen, so a reader checks a formula here against a printed one sign
   for sign.

2. **Storage, the blade order and the sign each blade carries.** A multivector is thirty-two binary64 coefficients
   over the outer products of **`n0`, `e1`, `e2`, `e3`, `n∞`**, in that order of the basis vectors: by grade, and
   within a grade lexicographically. **Every basis blade is the outer product of its vectors in ascending order and
   carries the sign `+1`**: `e1 ∧ e3`, never `e3 ∧ e1`. The index of a blade is its row below; it is the position
   of its coefficient in every array, vector and document. `rev` is the sign of the reverse,
   `(−1)^(g(g−1)/2)`; `inv` the sign of the grade involution, `(−1)^g`; `dual` is where item 4's dual sends the
   blade, with its sign.

   | index | blade | grade | rev | inv | dual |
   |---:|---|---:|:-:|:-:|---|
   | 0 | `1` | 0 | + | + | −`n0∧e1∧e2∧e3∧n∞` (31) |
   | 1 | `n0` | 1 | + | − | +`n0∧e1∧e2∧e3` (26) |
   | 2 | `e1` | 1 | + | − | +`n0∧e2∧e3∧n∞` (29) |
   | 3 | `e2` | 1 | + | − | −`n0∧e1∧e3∧n∞` (28) |
   | 4 | `e3` | 1 | + | − | +`n0∧e1∧e2∧n∞` (27) |
   | 5 | `n∞` | 1 | + | − | +`e1∧e2∧e3∧n∞` (30) |
   | 6 | `n0∧e1` | 2 | − | + | +`n0∧e2∧e3` (19) |
   | 7 | `n0∧e2` | 2 | − | + | −`n0∧e1∧e3` (17) |
   | 8 | `n0∧e3` | 2 | − | + | +`n0∧e1∧e2` (16) |
   | 9 | `n0∧n∞` | 2 | − | + | +`e1∧e2∧e3` (22) |
   | 10 | `e1∧e2` | 2 | − | + | +`n0∧e3∧n∞` (21) |
   | 11 | `e1∧e3` | 2 | − | + | −`n0∧e2∧n∞` (20) |
   | 12 | `e1∧n∞` | 2 | − | + | +`e2∧e3∧n∞` (25) |
   | 13 | `e2∧e3` | 2 | − | + | +`n0∧e1∧n∞` (18) |
   | 14 | `e2∧n∞` | 2 | − | + | −`e1∧e3∧n∞` (24) |
   | 15 | `e3∧n∞` | 2 | − | + | +`e1∧e2∧n∞` (23) |
   | 16 | `n0∧e1∧e2` | 3 | − | − | −`n0∧e3` (8) |
   | 17 | `n0∧e1∧e3` | 3 | − | − | +`n0∧e2` (7) |
   | 18 | `n0∧e1∧n∞` | 3 | − | − | −`e2∧e3` (13) |
   | 19 | `n0∧e2∧e3` | 3 | − | − | −`n0∧e1` (6) |
   | 20 | `n0∧e2∧n∞` | 3 | − | − | +`e1∧e3` (11) |
   | 21 | `n0∧e3∧n∞` | 3 | − | − | −`e1∧e2` (10) |
   | 22 | `e1∧e2∧e3` | 3 | − | − | −`n0∧n∞` (9) |
   | 23 | `e1∧e2∧n∞` | 3 | − | − | −`e3∧n∞` (15) |
   | 24 | `e1∧e3∧n∞` | 3 | − | − | +`e2∧n∞` (14) |
   | 25 | `e2∧e3∧n∞` | 3 | − | − | −`e1∧n∞` (12) |
   | 26 | `n0∧e1∧e2∧e3` | 4 | + | + | −`n0` (1) |
   | 27 | `n0∧e1∧e2∧n∞` | 4 | + | + | −`e3` (4) |
   | 28 | `n0∧e1∧e3∧n∞` | 4 | + | + | +`e2` (3) |
   | 29 | `n0∧e2∧e3∧n∞` | 4 | + | + | −`e1` (2) |
   | 30 | `e1∧e2∧e3∧n∞` | 4 | + | + | −`n∞` (5) |
   | 31 | `n0∧e1∧e2∧e3∧n∞` | 5 | + | − | +`1` (0) |

   So a point is `(1, x1, x2, x3, ½ x²)` at indices 1 to 5, a line is its direction and its moment at six indices,
   and a motor is eight coefficients.

   *Rejected: coefficients over `e1 e2 e3 e+ e−`.* Its product table is the simplest there is (one blade for each
   pair, found by an exclusive-or), and that is its whole merit. Its cost is in the answers. There `n∞` is null
   only by cancellation, and a point holds `½ x² − ½` and `½ x² + ½`, so `up x · up y` forms two products of size
   `¼ x² y²` and subtracts them to reach `−½ |x − y|²`: the error is of the order of `x² y²` units in the last
   place where the null basis, which forms `x · y`, `½ x²` and `½ y²`, has `x²`. Measured, both written as item 3's
   fold in binary64 against the exact value, over 20,000 pairs of points with every coordinate of the first within
   the stated size and the second within one of it in each coordinate (seed 726), the largest absolute error of
   `up x · up y`:

   | coordinates up to | null basis | `e+ e−` basis |
   |---:|---:|---:|
   | 1 | 8.3e-16 | 1.5e-15 |
   | 10 | 4.5e-14 | 4.3e-12 |
   | 100 | 4.6e-12 | 3.5e-8 |
   | 1,000 | 4.7e-10 | 3.1e-4 |
   | 10,000 | 5.1e-8 | 4.7 |

   At coordinates of a hundred the orthonormal store has given up four digits the null basis keeps, and at ten
   thousand its answer for two points a unit apart is wrong in its first digit. The figures are one sample's; the
   argument above is why they fall as they do. The null basis keeps what the
   orthonormal one had: every table entry is `±1`, every output coefficient is a sum of thirty-two terms, and the
   dual is still a signed permutation of the coefficients (the last column above), so it is exact.

   *Rejected: bitmap order* (the blade whose vectors are the set bits of its index), which needs no table to find a
   blade. Grade order keeps each grade a contiguous run (0, 1–5, 6–15, 16–25, 26–30, 31), which is how a law vector
   is read and how a typed object is laid out; the table above is short enough to transcribe once. *Rejected: `n0`
   last but one* (`e1 e2 e3 n0 n∞`): the order chosen is the order in which DFM writes the basis, `o`, `e1`, `e2`,
   `e3`, `∞` (chapter 13), so a point reads here from left to right as its formula does, `n0 + x + ½ x² n∞`. *Rejected: cyclic
   bivectors* (`e2∧e3`, `e3∧e1`, `e1∧e2`): a sign per blade is a second table every port must carry.

3. **One summation order for every product, and what a zero does.**

   *The table.* Write a basis blade as `U N`: `U` its Euclidean factor, a product of `e1 e2 e3` ascending, and `N`
   its null factor, one of `1`, `n0`, `n∞`, `E`. The stored blade `n0 ∧ U ∧ n∞` is the geometric product `λ U N`,
   with `λ = −1` when `N` is `n0` or `E` and `U` has odd grade, and `+1` otherwise. Then

   `(λ₁ U N₁)(λ₂ V N₂) = λ₁ λ₂ κ (U V)(N₁ N₂)`,

   where `κ = −1` when `N₁` is `n0` or `n∞` and `V` has odd grade (a null vector anticommutes with each Euclidean
   vector; `E` commutes); `U V = ±W`, `W` the vectors in exactly one of the two and the sign the parity of the
   transpositions that sort them (each `e_i² = +1`); and `N₁ N₂` is the row `N₁`, column `N₂` of

   | | `1` | `n0` | `n∞` | `E` |
   |---|---|---|---|---|
   | **`1`** | `1` | `n0` | `n∞` | `E` |
   | **`n0`** | `n0` | `0` | `−1 + E` | `n0` |
   | **`n∞`** | `n∞` | `−1 − E` | `0` | `−n∞` |
   | **`E`** | `E` | `−n0` | `n∞` | `1` |

   Each `W N` that results is put back as a stored blade by its own `λ`. A *term* is `(i, j, k, s)`: the blade of
   index `i` times the blade of index `j` contributes `s` times the blade of index `k`. There are **1,024** terms,
   every `s` is `+1` or `−1`, and each of the thirty-two output coefficients receives exactly thirty-two; 128 pairs
   `(i, j)` give two terms and 128 give none. The table is pinned: one line `i j k s` for each term, `s` written `+`
   or `-`, single spaces, a line feed after each line, ASCII, the terms ascending in `(i, j, k)`, is 10,304 bytes
   whose SHA-256 is `29b9805b10126723149f83a56a8d75f1e0755ea4992a690c828fb987383d43d8`.

   *The fold.* The geometric product `A B` visits the terms **ascending in `(i, j, k)`**: the left operand's blades
   in index order, under each the right operand's in index order. Each output coefficient `r_k` starts at `+0.0`;
   each term forms `p = a_i × b_j`, one rounded product with the left coefficient first, and then `r_k + p` or
   `r_k − p`, one rounded sum, as it is met. No fused multiply-add on any host (D49 item 2, with its obligations on
   a twin). Negation is exact and rounding to nearest is symmetric, so applying a term's sign as a subtraction, as a
   negated product or as a negated operand is one answer, and a port may write any of them. Whether the loop
   scatters into thirty-two accumulators or gathers one output at a time is not observable: for one output the terms
   arrive in the same order either way.

   *One output written out.* The scalar coefficient of `A B`, in order:

   `+a0·b0 −a1·b5 +a2·b2 +a3·b3 +a4·b4 −a5·b1 −a6·b12 −a7·b14 −a8·b15 +a9·b9 −a10·b10 −a11·b11 −a12·b6 −a13·b13`
   `−a14·b7 −a15·b8 +a16·b23 +a17·b24 +a18·b18 +a19·b25 +a20·b20 +a21·b21 −a22·b22 +a23·b16 +a24·b17 +a25·b19`
   `+a26·b30 −a27·b27 −a28·b28 −a29·b29 +a30·b26 −a31·b31`

   that is, `((((+0.0 + a0·b0) − a1·b5) + a2·b2) + …) − a31·b31`. A left blade can meet two right blades for one
   output: the coefficient of `n0` begins `+a0·b1 +a1·b0 +a1·b9 −a2·b6 …`, the right index ascending.

   *The other products are the same terms, selected.* The outer product keeps the terms with
   `grade k = grade i + grade j` (243 of them; the same rendering is 2,358 bytes, SHA-256
   `794d5f82a81b29af115be4ef5ddb3f97c2444d61ac11ac7de9fc363896432af2`). The left contraction keeps those with
   `grade k = grade j − grade i` (243; 2,358 bytes;
   `c564f7bc6b5a0bae494c2ceaada6d2ba1a87b474eb4eb615c2ebea2e75276453`). The scalar product keeps those with
   `k = 0`, so it is the scalar coefficient of the geometric product bit for bit. Each is folded in the order its
   terms have in the full table. A sum, a difference and a scalar multiple are one rounded operation for each
   coefficient. A product of three factors is two products, **left to right**: `A B C` is `(A B) C`.

   *What a zero does.* **No term is skipped on the value of a coefficient.** A term is absent only when the table
   has none for its pair, which is a fact about the blades and never about the data. Two things follow, and both
   are relied on.

   - *No coefficient a product returns is `−0.0`.* An accumulator that starts at `+0.0` can only reach `−0.0` by
     adding `−0.0` to `−0.0`, and it never holds one. A product whose every term is a negative zero answers `+0.0`.
     Starting each fold from its first term instead was rejected for exactly this: `(−0.0) + (−0.0)` is `−0.0`, the
     sign of a zero result would then depend on which zero terms were formed, and nothing could ever be dropped.
   - *Over finite operands, leaving out a term one of whose factors is a zero cannot move a bit.* Such a term is
     `±0.0`, and adding either zero to an accumulator that is not `−0.0` returns the accumulator. So a product
     written for the coefficients its operand types carry (Phase 730), visiting its terms in the order above,
     answers the general product's bits, the sign of every zero included, and what would break that is a fold
     seeded from its first term.

   A non-finite coefficient is IEEE's: a zero against an infinity is a NaN and the general product says so. A
   specialised product must answer the general product's bits on every input, so where an operand holds a
   non-finite coefficient it takes the general path; it finds that out with one test per coefficient it carries,
   before any term. The involutions, the dual and negation flip signs exactly, a zero's included, so `−0.0` can
   stand in a multivector that one of them, or a caller, made; the two statements above are about what a product
   returns. Every float leaves a public function with `Double.NaN`'s bits when it is a NaN (D16, D49 item 5),
   applied on the way out and never per step.

   *Rejected:* a pairwise or compensated sum for each coefficient (more accurate, and a second order every port must
   reproduce; D9 ruled the fold for `Sum` and D49 item 1 for every reduction since); skipping a term whose
   coefficient is zero, as a sparse product would (the answer would then depend on the test, a zero against an
   infinity would be silent, and it buys nothing the statement above does not already give a specialised product).

4. **The products and operations that ship, and how each is defined.**

   - **Geometric product**, item 3. **Outer product** `A ∧ B` and **left contraction** `A ⌋ B`, by selection.
   - **The inner product is the left contraction**, and nothing else is published under that name. It is DFM's
     (chapter 3): the adjoint of the outer product, `(A ∧ B) ∗ C = A ∗ (B ⌋ C)`, so it needs no exception for a
     scalar operand, and duality reads `(A ∧ B)* = A ⌋ B*` at every grade. *Rejected:* HS's inner product, which is
     zero whenever an operand is a scalar and so carries an exception into every identity that uses it; the
     symmetric "fat dot", which is not the adjoint of anything; the right contraction as a second published
     product, which is `(B̃ ⌋ Ã)` reversed and waits for a reader.
   - **Scalar product** `A ∗ B`, the scalar coefficient of `A B`. **Squared norm** `A ∗ Ã`: a signed number, not
     a length, negative on a blade of negative signature, and named for what it is.
   - **Reverse**, **grade involution** and **grade projection**: the signs of item 2's table applied by exact
     negation, and a copy of one grade's run with `+0.0` elsewhere. The Clifford conjugate is their composition
     and is not published separately.
   - **Dual** and its inverse, the **undual**. In meaning the dual is `A* = A ⌋ I⁻¹` and the undual `A ⌋ I`, with
     `I` of item 1; because `I` is the pseudoscalar each is also the geometric product with `I⁻¹` or `I`, and
     because `I` is central it does not matter on which side. **What ships is the signed permutation in item 2's
     last column** (the undual its inverse permutation), applied by copying and exact negation: no arithmetic, so
     `undual (dual A)` is `A` bit for bit on every input free of a NaN. The contraction and the product, written
     out as folds, give the same multivector under the float identity of item 8 and not bit for bit: the
     permutation's negation turns a `+0.0` into a `−0.0`, which no fold returns. This is DFM's dual. *Rejected:*
     `A I`, multiplication by the pseudoscalar itself (DL's usage), which is the same map with the other sign, and
     here the undual; one of the two had to be the dual, and DFM's tables are written with this one. *Rejected as
     the shipping form:* the contraction as a fold, which is thirty-two products and sums to do what a copy does.
   - **Join** `A ∧ B`, one outer product. **Meet** `meet A B = B* ⌋ A`: the dual of the second operand, by the
     permutation, contracted onto the first, one fold (DFM chapter 5). It is the blade whose dual is `B* ∧ A*`, and
     with the operands exchanged it is the same blade up to `(−1)^((5−a)(5−b))`. *Rejected as the shipping form:*
     `B* ∧ A*` undualled, which is two duals, an outer product and an undual where this is one dual and one
     contraction, and whose last step can return a `−0.0`, which item 3 lets no product do. The two forms agree
     under the float identity and not bit for bit. Both the join and the meet are taken in the whole space. A zero
     blade is an answer and is named: the join of two operands that share a factor (two flats always share `n∞`),
     the meet of two that do not span the space (two coplanar lines; two skew lines do span it, and their meet is a
     multiple of `n∞`). For those the **meet within a span** ships beside it, `(B ⌋ J⁻¹) ⌋ A` for a blade `J` the
     caller supplies that contains both, with `J⁻¹ = J̃ / (J ∗ J̃)` and each coefficient divided once; with `J = I`
     it is the meet bit for bit. For two lines the span is the join of the first with a point of the second: `up` of
     the coordinates of its point nearest the origin, or, when that join is the zero blade, `up` of those
     coordinates plus the second line's direction as it is stored, not divided by its weight, one addition for
     each. Both cases go through coordinates, so the point joined has weight one and is null by construction; if that
     join is the zero blade too, the lines are one line. "Is the zero blade" is item 6's test of a kind, with the
     caller's `τ`, so at `τ = 0` it is exact. *Rejected:* a general join that factors its operands, which needs a
     threshold at every step and is no single product.
   - **A blade's inverse** is `X̃ / (X ∗ X̃)`, refused when the divisor is zero. No inverse of a general
     multivector ships.

   **What a typed operation is.** An operation on typed operands (item 8) is the operation above on their
   thirty-two coefficients, the coefficients a type does not carry being `+0.0`, followed by a copy of the result
   type's coefficients. The copy discards the rest without a test: in exact arithmetic they are zero, and in
   binary64 they are rounding residue.

   **The objects are direct blades**: an object is the outer product of points on it, so `x` lies on `X` when
   `up x ∧ X = 0`, and its orientation is the order of its points.

   | object | grade | through points | from parameters |
   |---|---:|---|---|
   | point | 1 | | `up x`: `x` at indices 2–4, `1` at 1, `½ ((x1·x1 + x2·x2) + x3·x3)` at 5 |
   | point pair | 2 | `P ∧ Q` | the two points |
   | line | 3 | `P ∧ Q ∧ n∞` | `up p ∧ d̂ ∧ n∞`: a point and a direction |
   | circle | 3 | `P ∧ Q ∧ R` | `meet (sphere c r) (plane n̂ (n̂ · c))`: a centre, a radius and a normal |
   | plane | 4 | `P ∧ Q ∧ R ∧ n∞` | `(n̂ + δ n∞)*`: a normal and the distance `δ` of the plane from the origin along it |
   | sphere | 4 | `P ∧ Q ∧ R ∧ S` | `(up c − ½ r² n∞)*`: a centre and a radius; `½ r²` is `0.5 × (r × r)`, the square first, subtracted from the coefficient of `up c` at index 5 |

   A direction parameter (a line's direction, a normal, an axis) is divided by its norm, D49 item 4's, one
   division for each component, before it is used; a zero or non-finite one is refused by name, as is a negative or
   non-finite radius and a non-finite coordinate. The two constructions agree in orientation: the line through `p`
   and `q` has direction `q − p`; the plane through three points has the normal `(q − p) × (r − p)` and the weight
   of that vector's length; a circle built from parameters runs counter-clockwise about its normal; the weight of a
   sphere through four points is `det [q − p, r − p, s − p]`. The **vector** of a plane or a sphere is its undual,
   `n̂ + δ n∞` or `up c − ½ r² n∞` times its weight: an exact read, since the dual is a permutation.

   **Read-back.** Write `W = n∞ ⌋ X`. The **weight** of a point is its coefficient at index 1, and of a sphere its
   coefficient at index 26: one signed number each, and not a norm. The weight of a point pair or a circle is the
   norm (D49 item 4) of its coefficients on the blades that hold `n0` and not `n∞`, and the weight of a flat the
   norm of its coefficients on the blades that hold both (a line's direction, a plane's normal); those are never
   negative. A point's coordinates are `P[2..4]`, each divided by `P[1]`. The **centre** of a round is the point
   `⟨(X n∞) X⟩₁`, read as a point is; its **squared radius** is `−(X ∗ X̃) / (W ∗ W̃)`, which is negative for the
   meet of two spheres that do not touch and is reported, never hidden. **A point pair's two points** are asked for
   with a `τ`, and the answer has three cases, decided by item 6's test of the squared radius before any root is
   taken. A *real* pair answers `⟨(X − s) W⁻¹⟩₁` and `⟨(X + s) W⁻¹⟩₁` with `s = +√(X ∗ X)`, in that order: for
   `P ∧ Q` they are `P` then `Q`, and for the meet of a line with a sphere built from its parameters they run along
   the line's direction. A *tangent* pair answers its one point, the centre. An *imaginary* pair answers its centre
   and its squared radius, which is negative. No root of a negative number is taken, and a NaN is never how a
   caller learns that two objects do not meet. A line's direction is its
   coefficients at indices 18, 20 and 21, read and not computed, and its moment `p ∧ d` is at 23, 24 and 25; a
   plane's normal and distance are its vector's. The point of a flat nearest the origin has the coordinates of the
   vector `⟨(n0 ⌋ X) X⁻¹⟩₁`, its indices 2 to 4 each divided by its index 1. That vector is not itself the point:
   its `n∞` coefficient is `x²` and not `½ x²`. Wherever the point is wanted as a multivector it is `up` of those
   coordinates.

   **The versors.** Each is a unit versor, `V Ṽ = 1`, built from parameters; angles are in radians.

   - **Rotor**, by `θ` about the axis `â` through the origin, counter-clockwise seen from the axis's tip:
     `R = cos ½θ − sin ½θ (â I₃)` with `I₃ = e1 e2 e3`, so with `(s, c) = Elementary.sinCos (0.5 × θ)` its
     coefficients are `c` at index 0, `−(s × â3)` at 10, `s × â2` at 11 and `−(s × â1)` at 13. About `e3` it takes
     `e1` towards `e2`.
   - **Translator** by `t`: `T = 1 − ½ t ∧ n∞`, coefficients `1` at 0 and `−(0.5 × t_i)` at 12, 14 and 15.
     `T (up x) T̃ = up (x + t)`.
   - **Motor**: `T R`, the rotation first and then the translation. The screw about the axis through the point `p`
     with direction `û`, by `θ` and a distance `d` along it, is `T_(d û) ((T_p R) T̃_p)`, `R` the rotor about `û`
     and the translator by `d û` built from the three products `d × û_i`; `p` is used as it is given. Read from a
     `Line`, `û` is the line's direction divided by its weight and `p` its point nearest the origin.
   - **Dilator** by the factor `s > 0` about the origin: `D₀ = (s + 1)/(2√s) + ((s − 1)/(2√s)) n0 ∧ n∞`, formed as
     `r = √s`, `q = 2 × r`, then `(s + 1) / q` at index 0 and `(s − 1) / q` at index 9. About a centre `c` it is
     `(T_c D₀) T̃_c`. It is the exponential of `½ ln s · n0 ∧ n∞` written without the logarithm: a correctly
     rounded root and two divisions, exact at a power of four, where `cosh` and `sinh` of half a pinned logarithm
     would be a measured approximation. **A dilation divides a point's weight by `s`**; coordinates are read by
     dividing by the weight, so they are right, and nothing renormalises.
   - **Reflection** in the plane with normal `n̂` at distance `δ`: the vector `π = n̂ + δ n∞`, whose square is `1`.

   **Applying one** is the sandwich with the **reverse**, never the inverse: `(V X) Ṽ` for the four even versors and
   `(π X̂) π` for a reflection, `X̂` the grade involution, the left product first, the result copied into the
   object's own type. A versor that is not a unit therefore scales what it is applied to, and that is visible
   rather than repaired (item 5). **Composing two**: the product `B A` applies `A` first. The product of two
   versors of one type is that type; of two reflections, a motor; a rotor or a translator becomes a motor by
   widening; any other mixture is taken on the multivector.

   **Exponential and logarithm** are the functions themselves, with no factor folded in: the rotor about `â` by `θ`
   is `exp (−½θ â I₃)`. For a Euclidean bivector `B` with `φ = |B|` (D49's norm of its three coefficients),
   `exp B = cos φ + (sin φ / φ) B`, the quotient formed once, and `exp B = 1 + B` at `φ = 0`. For a motor's
   bivector `B + v ∧ n∞`, split `v` into its part along the axis of `B` and the rest:
   `exp = (1 + v∥ ∧ n∞)(cos φ + (sin φ / φ)(B + v⊥ ∧ n∞))`, and `1 + v ∧ n∞` at `φ = 0`. The **logarithm** takes
   `φ = Elementary.atan2 |B′| c` from a rotor's scalar `c` and bivector part `B′`, and answers `(φ / |B′|) B′`, the
   quotient formed once, and `B′` itself where `|B′| = 0` and `c > 0`; a motor's takes the same `φ` from its
   rotation's part. So **`φ` lies in `[0, π]`** and the rotation it names lies in `[0, 2π]`. It never replaces a versor by its negative to take the shorter way: a
   caller who wants that negates a rotor of negative scalar part first. At `B′ = 0` with `c < 0` no plane is
   determined, and that is a named refusal. **Interpolation** is `V₁ exp (t log (Ṽ₁ V₂))`, formed in that order, so
   `t = 0` returns `V₁`'s coefficients, a negative zero among them coming back positive (item 3). It follows the
   logarithm's branch, so it can go the long way round. A caller who wants the shorter way passes `−V₂` in place of
   `V₂` when the scalar coefficient of `Ṽ₁ V₂` is negative: the negated versor is the same motion.

   **The matrix.** A similarity `x ↦ s R x + t` is sixteen doubles in column-major order acting on column vectors:
   columns 0 to 2 hold `s R`, column 3 holds `t`, and the last row is `0 0 0 1`. A rotor, a translator, a motor
   and a dilator each convert to it. Back, a matrix converts to a motor and a factor, the dilation about the origin
   first and then the motor; the factor is the norm of its first column; the rotor returned has a scalar part that
   is not negative, and at a half turn, where that part is zero, the axis component of largest magnitude is
   positive, the first such in a tie. The last row is compared exactly. That the three columns are orthogonal and
   of one length is a zero test of item 6, and a matrix that fails it, or reverses orientation, is refused by name
   with the measure that failed.

5. **Normalisation is never implicit.** No product, involution, dual, meet, join, sandwich, composition,
   exponential, logarithm, interpolation, conversion or replay normalises an operand or a result. A constructor
   from parameters returns a unit versor or a weight-one object because of what it computes, and that is the only
   sense in which a value arrives normalised. Three routines normalise, each named, each called by the caller,
   each dividing every coefficient the type carries by one scalar, once:
   - a **point** by its weight `P[1]`;
   - any other **object** by its weight (item 4); a sphere's is signed, so one of negative weight changes
     orientation when it is normalised;
   - a **versor** by `√(V ∗ Ṽ)`.
   Each refuses by name a divisor that is zero, not finite or, for a versor, not positive; those are exact
   comparisons and take no threshold. A read-back that divides by a weight (a point's coordinates, a centre, a
   squared radius) does so because its definition in item 4 says so, and returns the object it was given
   unchanged. *Rejected:* normalising inside the sandwich or after each composition, as libraries that hold
   single-precision rotors do. It hides drift, it makes `apply` a different function from the product it is
   written as, and it would make a replayed history depend on how often something was read.

6. **Incidence, and what a zero test is.** **A zero test is the comparison `|statistic| ≤ τ` with `τ` passed by the
   caller on every call**: a finite `τ ≥ 0`, anything else refused by name. `τ = 0` is the exact comparison, so
   there is one kind of test and not two. Each function that takes a `τ` documents its statistic and the unit it is
   in. **No tolerance is a constant in either package**: no default argument, no named epsilon, and
   no float literal in a comparison but the two this entry itself requires. `0.0` may be named only in a comparison
   stated here: a divisor against zero (a weight, `V ∗ Ṽ`, `J ∗ J̃`, `X ∗ X̃`, `W ∗ W̃`); the norm of a direction
   parameter against zero; `V ∗ Ṽ > 0`; a factor `s > 0`; a radius or a `τ` that is not negative; the
   exponential's `φ = 0` and the logarithm's `|B′| = 0`; the sign of a scalar part (the logarithm's `c`, and the
   rotor read from a matrix, with the sign of its largest axis component at a half turn); the sign of whatever
   says that a matrix reverses orientation; each of the first three entries of a matrix's last row; the zero test
   inside `Multivector`'s hash (item 8); and the signs
   of a numerator and a denominator at `τ = 0`. `1.0` may be named in exactly one comparison: the fourth entry of a
   matrix's last row. A test for a NaN or an infinity names no tolerance and is outside the rule, and so are the
   two tests inside the norm itself, `m = 0` and `m = +∞` (D49 item 4). The suite of Phase 728 reads the sources to
   hold that. At `τ = 0` a statistic that is a
   quotient is decided from the signs of its numerator and denominator and no quotient is formed, so a test on
   integer coordinates is exact wherever its products are.

   The statistics, each a geometric quantity with the weights divided out in its own definition (`w` a weight,
   `σ` and `π` the vectors of item 4):
   - a point against a **sphere**: the power `(−2.0 × (P ∗ σ)) / (w_P × w_σ)`, which is `|x − c|² − r²`, in squared
     length. Negative is inside, within `τ` is on, positive is outside.
   - a point against a **plane**: the signed distance `(P ∗ π) / (w_P × w_π)`, positive on the side the normal
     points to, in length.
   - a point against a **circle**, for a point in the circle's plane: the power
     `(−2.0 × ((P ∧ X) ∗ Ỹ)) / (w_P × (Y ∗ Ỹ))` with `Y = X ∧ n∞` the circle's plane; the same reading as the sphere's.
     On integer coordinates its numerator is formed exactly, and it is negative for a point inside whatever the
     order of the circle's three points, where the in-circle determinant changes sign with their orientation.
     Whether the point is in the plane is the plane test, and the caller's to make.
   - **Each statistic is published twice**: as a function that returns its value, and as the test that returns
     the kind. The value is formed in one order: the scalar product by item 3's fold; for a power, `−2.0` times
     it, which is exact; the product of the two factors of the divisor; then one division. So a power that is zero
     is `−0.0` over a positive divisor and `+0.0` over a negative one, and a signed distance that is zero is `+0.0`
     over a positive divisor. The kind compares a magnitude with `τ` and does not read the sign of a zero.
   - **What kind of blade an answer is**: by the norm (D49 item 4) of a group of its coefficients, in coefficient
     units. A *round* has coefficients of norm above `τ` on the blades that hold `n0` and not `n∞`; it is *real*, *tangent* or
     *imaginary* as its squared radius is above `τ`, within it or below `−τ`. A *flat* has none there, none on the
     blades that hold neither, and coefficients of norm above `τ` on the blades that hold both. A blade with coefficients only on the
     blades that hold `n∞` and not `n0` is *at infinity*: the meet of two parallel planes, and of two parallel
     lines within their plane. The zero blade is a kind, and so is anything else, which is no object. Three
     collinear points define no circle: their outer product is a flat, and the answer is that case carrying the
     line. A flat of grade two is a point `P ∧ n∞`, which is what a line and a plane meet in, and is answered as
     the point.

   Every answer is total: each kind is a case of the result, carrying what the algebra still knows, never an
   exception and never a NaN returned as if it were geometry. *Rejected:* a library tolerance (`1e-9`, a multiple
   of the unit roundoff, a tolerance scaled by the operands' norms). Each is right at one scale of coordinates and
   silently wrong at another, and the caller is the only party that knows the scale; D49 item 11 could fix its rank
   tolerance because a factorization states its own scale, and an incidence test has none to state.

7. **Exactness.** **Every published operation of both packages is `Pinned`** in D43's vocabulary, as D50 item 2
   and D74 item 2 apply it: the package declares its own `Exactness = Pinned | HostRounded`, nested in the module
   `GeometricAlgebraOperations`, whose `exactness` table has a row for every published function, and the suite
   holds the table equal to the surface both ways. The module is not called `Operations`, which would shadow
   LinearAlgebra's member by member (D74 item 2). The op package's table is `GeometricAlgebraOpsOperations`, with
   its own nested `Exactness`, and its law family is `GeometricAlgebraOpsLaws`.
   Nothing is `HostRounded`: no host mathematics function is called
   on any path, and a source-reading test holds that as it does for `Elementary`. What stands behind the class
   differs, and the table's documentation says which:

   | operations | built from | what the answer is |
   |---|---|---|
   | reverse, grade involution, grade projection, dual, undual, negation, widening to a multivector and the copy back, a coefficient read | copies and exact negation | exact on every input |
   | sum, difference, scalar multiple; the three normalisations | one `+`, `−`, `×` or `÷` for each coefficient | correctly rounded, coefficient by coefficient |
   | geometric, outer, contraction and scalar products, squared norm, join, meet, the sandwich, composition, `up`, the incidence statistics, the read-backs that take no root | item 3's folds, and `÷` where item 4 or 6 writes one | exact whenever every product and every partial sum is representable; otherwise the fold's rounding, the same bits on every host |
   | a norm and a weight; normalising a direction parameter or a versor; a dilator; a point pair's two points; a matrix read as a motor and a factor | the above and `√` | the same, `√` being correctly rounded (D49's premise) |
   | a rotor or a screw from an angle; `exp`; interpolation | the above and `Elementary.sinCos`, once | the same bits on every host; as accurate as D84 measured `sinCos`, which is not correct rounding |
   | `log`; a rotor's angle; interpolation | the above and `Elementary.atan2`, once | the same, with D84's measurement of `atan2` |

   `Elementary.exp`, `ln` and `pow` are called nowhere in the first surface: the dilator is item 4's roots. Phases
   727 and 728 therefore reference nothing but FSharp.Core, and Phase 729 adds `Fuaran.Compute.Elementary`.

   **Laws on inputs whose arithmetic is exact** (integers and dyadic fractions small enough that every product and
   partial sum is a double, and free of a NaN). Each holds under one of two comparisons, and a law says which. *Bit
   for bit* compares the sixty-four bits of each coefficient, which tells the two zeros apart. *Under the float
   identity* is item 8's comparison, in which the two zeros are one value. The second is needed wherever one side
   of a law ends in a negation and the other in a fold: a negation turns `+0.0` into `−0.0`, and a fold never
   returns one (item 3), so the two sides are the same multivector with different zeros.

   - **Bit for bit:** the signature (`e_i² = 1`, `n0² = n∞² = 0`, `n0 ∗ n∞ = −1`, and `½ n∞ − n0` and `½ n∞ + n0`
     square to `+1` and `−1`); associativity and distributivity of the geometric product; `a ∧ a = 0`; `I² = −1`;
     `(A ∧ B) ∗ C = A ∗ (B ⌋ C)`; `up x ∗ up x = 0`, and `up x ∗ up y = −½ |x − y|²` for two different points; a
     point on the circle or sphere built through it; the meet of two planes in both; the meet within the whole
     space against the meet; a translator composed with its opposite, against the identity; the reflection in the plane
     `x1 = 0` and then the one in `x2 = 0`,
     the product `e2 e1`, against the motor that holds `−1.0` at index 10 and `+0.0` at each of its seven other
     indices (and likewise `e3 e2` against `−1.0` at index 13, and `e1 e3` against `+1.0` at index 11); a dilation by a
     power of four,
     against the point it should give. The undual of a dual is its input bit for bit on every input without a NaN,
     exact or not, because a coefficient is negated twice or not at all.
   - **Under the float identity, and not bit for bit:** the reverse as an anti-automorphism,
     `rev (A B) = rev B rev A` (the left side ends in the reverse's negations: `rev (e1 e2)` holds nineteen negative
     zeros and `rev e2 rev e1` none); `(A ∧ B)* = A ⌋ B*` (the left side ends in the dual's: sixteen against none at
     `A = e1`, `B = e2`); the dual against the contraction `A ⌋ I⁻¹` written as a fold; the meet against `B* ∧ A*`
     undualled; `up x ∗ up x` against `−½ |x − x|²`, whose right side is `−0.0`; and those products of two
     reflections against the rotor's coefficient formulas of item 4 at `c = 0`, `s = 1`, which hold
     `−(1 × 0) = −0.0` where a product holds `+0.0`. The half turn is written out in the law above because it
     cannot be built: no double is `π`, so the rotor's constructor never returns one.
   - **The sign of every incidence statistic** is the sign exact arithmetic gives, a comparison of signs and not of
     bits.

   Each was evaluated in binary64 under the comparison named, on exact inputs, before it was listed.

   **Everywhere else an identity holds only to rounding**, and a
   law that states it states a bound: a rotor times its reverse, the logarithm of an exponential, an inverse
   transform, a compaction, an answer beside its closed form. **This entry fixes the form of every such bound and
   no number.** A bound is in units in the last place of the reference value, or absolute and in the statistic's
   own unit where the reference is zero. It is the largest error over a stated, seeded sample, and any margin
   added to that is stated beside it. It is recorded, with the sample, in the entry of the phase that ships the
   operation, as D84 records its own. A number written here, before the code it bounds exists, would be a guess
   with a decision's authority.

8. **The type shape.** **One `Multivector` type and typed wrappers that hold only their own coefficients.**
   `Multivector` is a sealed class with an internal constructor over thirty-two doubles, never handing its array
   out: the shape `Matrix` has. Each object and each versor is an immutable record of named `float` fields, one
   for each coefficient it carries, in index order:

   | type | indices carried |
   |---|---|
   | `Point` | 1–5 |
   | `PointPair` | 6–15 |
   | `Line` | 18, 20, 21, 23, 24, 25 |
   | `Circle` | 16–25 |
   | `Plane` | 27–30 |
   | `Sphere` | 26–30 |
   | `Rotor` | 0, 10, 11, 13 |
   | `Translator` | 0, 12, 14, 15 |
   | `Motor` | 0, 10–15, 30 |
   | `Dilator` | 0, 9, 12, 14, 15 |
   | `Reflection` | 2–5 |

   Each widens to a `Multivector` and is copied back from one (item 4), and its functions live in a module of its
   own name. A function that can refuse returns `Result` with a union of named cases, and one whose answer has
   kinds returns a union of them (D49 item 6): nothing throws on bad data. **There are no operators**: a product is
   a named function, because `*` between two multivectors would have to be one of four products.

   **Equality.** `Multivector` is a class, so its `Equals` and
   `GetHashCode` are written, and they are **the float identity `Matrix` compares under**. Two multivectors are
   equal when at every index the two coefficients are both NaN or are equal as numbers: every NaN is one value and
   `−0.0` equals `0.0`. The hash starts at `32` and takes each coefficient in index order as `h × 31 + k`, where
   `k` is `0x7FC00000` for a NaN, `0` for either zero and F#'s `hash` of the double otherwise, so equal
   multivectors hash alike and `x.Equals x` is true whatever `x` holds. The hash is not a pinned number: it is one
   value for equal multivectors on a host and may differ between hosts. A caller's `=` on two multivectors answers
   that identity. The wrappers are records and keep F#'s own equality, which is another thing: `=` on two records
   is false where a field is a NaN on both sides and true across the two zeros, while the `Equals` F# writes for a
   record counts a NaN equal to itself on .NET. **Neither is what a law compares with.** A law compares bits, the
   sixty-four of each coefficient (`BitConverter.DoubleToInt64Bits`, or the corpus writer's bytes), or it says that
   it compares under the float identity (item 7); it never uses `=`. *Rejected:* an `Equals` under which a NaN
   equals nothing, which breaks the rule that a value equals itself, and with it every hashed collection that
   holds one. No type is generic in its scalar, its grade or its signature, and
   nothing is `inline` for the sake of a constraint.

   *What was checked.* A scratch project holding these shapes (the sealed class over a `float[]`, a record of five
   floats, the same as a struct record, a record of eight, two unions) and the general product of item 3, table
   built by item 3's rule, compiled without a warning on .NET and with the Fable the tool manifest pins (5.0.0),
   and run under node 22. The table's digest on .NET is item 3's. Forty products of full multivectors drawn with
   both zeros among the coefficients, the product whose only terms are negative zeros, and each equality answered
   the same bytes on the two hosts. The probe's class compared its coefficients with `=`; the identity ruled above
   is `Matrix`'s, whose class runs on both hosts in every gate. A `float[]` is a `Float64Array` under Fable (D10's premise check found the
   same); a struct record compiles there to an object like any other, so `[<Struct>]` would buy nothing on one host
   and change copying on the other, and the wrappers are plain records.

   *Rejected:* typed wrappers that each hold a full `Multivector` (a point is then thirty-two doubles, every typed
   operation is the 1,024-term product until it is specialised, and Phase 730 has nothing to measure against but a
   store it must first shrink); a type for each grade, or a multivector indexed by the grades present (a rotor and
   a motor are not single grades, the types would be written against members F# resolves by `inline` constraint,
   and that is the surface Fable compiles least plainly); one sparse multivector holding index and value pairs (the
   order of a fold would then be a property of each value).

9. **The package boundary and the ids.**

   **Ruled by the operator, 2026-11-23, recorded here and not reopened:** the algebra package is
   **`Fuaran.Compute.GeometricAlgebra`**, and the op package is **`Fuaran.Compute.GeometricAlgebraOps`**. The
   second replaces the first working id, `Fuaran.Compute.GeometricOps`, which read as geometric operations in
   general.

   **`Fuaran.Compute.GeometricAlgebra`** has the namespace of its id. It references FSharp.Core alone in Phases
   727 and 728, and from Phase 729 `Fuaran.Compute.Elementary` beside it, for `sinCos` and `atan2` (which brings
   `Fuaran.Compute.Solvers`, Elementary's own reference). It never references a `Fuaran.Core.*` package, so no tree,
   op-stream, ops, validator, column or wire package, and never LinearAlgebra, DataFrame or the op package; a
   boundary rule in the suite holds that, as D77 item 10's holds the solvers. It holds the multivector, the
   products, the objects, the versors, `GeometricAlgebraOperations` and the law family `GeometricAlgebraLaws`. It
   knows nothing of names, histories, units or tables.

   **The conversion with `Matrix` is the algebra package's, written against sixteen doubles; neither package
   references the other, and LinearAlgebra gains nothing.** The algebra package gives and takes item 4's matrix as
   a `float[]` of sixteen in column-major order, a fresh array each way, which is the layout `Matrix.toColumnMajor`
   answers. A caller holding both packages writes one line each way: `Matrix.init 4 4 (fun i j -> m[4 * j + i])`
   to make the `Matrix`, and `Matrix.toColumnMajor` to hand one back. The suite, which references both, holds the
   round trip and the agreement of the two routes on points. *Rejected:* a `Matrix.ofColumnMajor` in LinearAlgebra
   (`Matrix.init` already builds a matrix from any layout, and a wrong array length is not the two shapes
   `ShapeMismatch` carries, so it would be new surface and a new refusal for one line); the algebra package
   referencing LinearAlgebra (it would take `Fuaran.Core.Column` and `Fuaran.Core.Wire` for the sake of a four by
   four, and move with every raise of the substrate); LinearAlgebra referencing the algebra package (D75: it holds
   matrix operations only and depends on nothing in Compute, and every regression consumer would carry a geometry
   library); a third package for the conversion (an id and a release for one reader).

   **`Fuaran.Compute.GeometricAlgebraOps`** references the algebra package, `Fuaran.Core.OpStream`,
   `Fuaran.Core.Wire` and FSharp.Core, and is the only geometric package that references `Fuaran.Core.OpStream`,
   as `Fuaran.Compute.ColumnOps` stands beside `Fuaran.Compute.DataFrame`. Its state is a set of named objects,
   generic in the name: the caller supplies the identity and its encoding, and the package invents neither. **Its
   first op list:**

   | op | parameters |
   |---|---|
   | `Add` | a name and an object in item 4's parametric form: a point; two points; a point and a direction; a centre, a radius and a normal; a normal and a distance; a centre and a radius |
   | `Remove` | a name |
   | `Rotate` | a name, a point on the axis, the axis's direction, an angle |
   | `Translate` | a name, a vector |
   | `Scale` | a name, a centre, a factor |
   | `Reflect` | a name, a plane's normal and distance |
   | `Screw` | a name, a point on the axis, the axis's direction, an angle and a distance along it: the general rigid motion |

   `Add` and `Remove` are in the list. A history that cannot create what it transforms needs its first state from
   somewhere else, and that state would need an encoding, which is the coefficient codec item 10 keeps out;
   `ColumnOps` carries a column's insertion and removal for the same reason. An op that builds one object from
   others (the circle through three named points) is not in it. **Every op is parametric**: no multivector
   coefficient is a member of any op or of the wire, angles are radians, and replay derives each object and each
   versor through item 4's constructors, applies it by item 4's sandwich and normalises nothing (item 5). `apply`
   and `canApply` are total: an unknown name, a name already present, a non-finite parameter, a zero-length
   direction, a factor that is not positive and a negative radius are refusals carried as data, with the state
   unchanged. The codec writes a float as the repository's other codecs do.

10. **What stays out, and the stance on each.**
    - **Units of measure on a multivector.** Out. A point holds a pure number, a length and a squared length in one
      value, so no one unit types it, and a unit for each coefficient would be a second algebra. A unit belongs to
      a parameter or a read-back, at a boundary above these packages, and neither references `Fuaran.Core.Unit`.
    - **A bit-exact codec for multivector values.** Out. A coefficient on the wire would freeze item 2's basis,
      order and signs into every stream that carried one. What is persisted is parametric (item 9). The law
      vectors carry coefficients through the corpus writer: that is the conformance corpus of these packages and
      no one's wire contract.
    - **Spacetime algebra.** Out: another signature, another table and other objects. Nothing here is generic in
      the signature, so it would be a package of its own under a ruling of its own.
    - **The quadric extensions** (the double conformal and quadric conformal algebras, of dimension ten and
      fifteen). Out. A cylinder, a cone and a general quadric are not blades of `Cl(4,1)`, and they stay whatever a
      caller already holds them as.
    - **Freeform surfaces.** Out, for the same reason and more so.
    - **A scalar type other than the pinned double.** Out: no single precision, no decimal, no interval, no dual
      number. Every rule above is a statement about binary64, and a second scalar is a second library.
    - Also not here, each waiting for a reader: a general multivector inverse, the right contraction, the
      exponential of a bivector with a dilation in it, and non-uniform scale, which stays a matrix.

**The boundary this entry restates and does not move.** Geometric algebra stays below every domain's language tier: a
motor or a blade is never a node kind and never a member of a domain's wire contract.

**What the later phases still record, and what they do not.** Phases 727 to 731 choose no sign, scale, order,
orientation, branch, normalisation or threshold: each is above. They record, in their own entries, the names this
entry leaves (the cases of each refusal and each kind, and a function's name where none is given here); each bound a
law states, with the sample behind it; and, where this entry defines a derived quantity and not the expression that
computes it (the sixteen entries of a versor's matrix; a rotor read from a rotation; a motor's
logarithm; how a motor's exponential forms the two parts of `v`, along the axis and across it, which several routes
give equally in exact arithmetic and differently in binary64; and the two statistics of the matrix test, how far the
columns are from orthogonal and from one length, which are chosen with the cases of that refusal so that one `τ`
means the same at any size of entry), the
expression, written in item 4's products in item 3's order, chosen by measurement as D84 chose its forms and pinned
by a vector. Where an expression is written above, it is the one that ships; and where a formula and
coefficients are both given (a point, a rotor, a translator, a dilator), the coefficients are the expression and the
formula is what they mean.

**Ladder.** `proofs.json` gains `geometric-algebra-pinned-by-recorded-rules` at `policy`, citing this record: the
rules are stated and nothing is tested, because nothing exists to test, and a policy row asserts no coverage.
`ProofsLadderTests` holds it to the tree both ways. While the algebra package's project file,
`src/Fuaran.Compute.GeometricAlgebra/Fuaran.Compute.GeometricAlgebra.fsproj`, is absent, the row is there, at `policy`,
citing `DECISIONS.md`, and this entry's heading is in that file. Once that file exists a row still at `policy` is a
false statement and the case goes red, so Phase 727 moves the row in the change that adds the package, with no edit to
the test. The test reads the project file and not the directory, so a build's leftover `bin` or `obj` in an older
checkout is not a package.

**What a consumer does.** Nothing: no surface moves, so `STABILITY.md` has no entry and the version stays `0.47.0`.

**Tidy.** Reviewed `DECISIONS.md`, `proofs.json` and `ProofsLadderTests.fs`. Nothing in them is made dead or obsolete
by this entry. The test's row type gained the one evidence member the new case reads, and its doc comment the one
line that names the case.

## 2026-11-23 — D84: sine, cosine and the two arc tangents are pinned in `Elementary` — `sin`, `cos`, `sinCos`, `atan` and `atan2`, each behind a table published as data; the argument is reduced exactly at every finite double, by three pieces of `π/2` up to `2²⁰` and by the digits of `2/π` beyond, so no bound ships; the kernels are the ones `Special`'s reflections were built on, moved, so no bit of `Special` moves; each form chosen by measurement against a fifty-digit reference, and every function is within one unit in the last place over the sample

**Context.** Phase 725. `Fuaran.Compute.Elementary` pinned `exp`, `ln`, `log10`, `pow`, `erfc` and the special
functions (D52, D58, D78) and published no trigonometric function. Its `Special` module held a sine and a cosine kernel
for `|y| ≤ π/4`, private to the reflection formulas. A rotation is built from the sine and cosine of half its angle and
its angle is read back through `atan2`, so a geometric algebra over this package needs all three pinned, or every
rotation it computes is the host's answer and not the library's. The functions have other readers (a seasonal term, a
great-circle distance), so they belong here and not in a geometry package.

**Premises checked first.** On `main` at `79716de`: `api/Fuaran.Compute.Elementary.txt` published no `sin`, `cos`,
`atan` or `atan2`; `Special.fs` held the private `sinTaylor`, `cosTaylor`, `sinKernel` and `cosKernel` and one
`(sin πx, cos πx)` pair over them; `v0.46.0` was tagged at that commit, so the slot was released and this phase opens
`0.47.0`. All three held. Phase 623 (method cards) and Phase 666 (step vectors) had not shipped, so this phase adds
neither a card nor a step vector: those phases' rosters gain these five functions.

**Decision.**

1. **The surface.** `Elementary.sin`, `Elementary.cos`, `Elementary.sinCos` (a struct pair from one reduction, `sin x`
   and `cos x` bit for bit), `Elementary.atan` and `Elementary.atan2 y x`. Each is answered first by a table published
   as data, as D43 and D52 did for the first four: `sinTable`, `cosTable`, `atanTable`, `atan2Table`. All nine have a
   `Pinned` row in `ElementaryOperations.exactness`. Every path is `+ − × ÷` and comparisons over literal constants,
   in one written order, with no fused multiply-add and no host mathematics call.

2. **The tables.** A NaN operand answers NaN everywhere. `sin` and `cos` of an infinity are outside the reals
   (`Undefined`; the functions answer NaN). `sin ±0` is `±0`; `cos` has no zero row, because the routine answers
   exactly `1` there. `atan ±0` is `±0` and `atan ±∞` is `±π/2`. `atan2`'s table is IEEE 754's in full, every answer
   taking the sign of `y`, a zero's included: a zero `y` answers a zero against a positive or `+0` `x` and `π` against
   a negative or `−0` one; a zero `x` answers `π/2`; an infinite `y` answers `π/4`, `3π/4` or `π/2` as `x` is `+∞`,
   `−∞` or finite; a finite `y` answers a zero against `+∞` and `π` against `−∞`. Each multiple of `π/4` is the double
   nearest it. Unlike `pow`, whose table D43 made stricter than C99, `atan2` has no `Undefined` row: the angle of a
   zero vector under each pair of signs is what IEEE 754 states, and a caller composing rotations reads it.

3. **The reduction, which this phase decides by measurement.** Three forms were measured.
   - *The cheaper one*: `π/2` in two pieces, eighty-six bits, with the remainder rounded to one double. Inside `2²⁰` it
     is a third of a unit worse on an ordinary argument (sample maxima of 1.3 to 1.4 against 0.8 to 0.9). At the
     doubles nearest multiples of `π/2` the remainder is the difference of two nearly equal numbers and has no digits
     left: `6.6e8`
     units. Beyond `2²⁰` its multiple of the first piece is no longer exact and the answer is noise. It is no quicker
     than what ships (26.9 ns against 26.6).
   - *The digits of `2/π` for every argument*: correct everywhere, and ten times dearer on an ordinary argument (260 ns
     against 26 on .NET, 850 against 69 under node).
   - *What ships*: both, each exact. Up to `π/4` the argument stands. Up to `2²⁰`, `π/2` is read in three pieces of
     thirty-three bits and the double nearest what they leave, 152 bits in all; a multiple below `2²⁰` of each piece
     is exact, each subtraction's rounding error is kept (Knuth's two-sum), and the remainder is a double and its
     tail. Beyond `2²⁰` the argument is an integer below `2⁵³` times a power of two; the integer, in three limbs of
     twenty-four bits, is multiplied by nine digits of `2/π` in base `2²⁴` (a table of fifty, 1200 bits), the first of
     them the first whose products are not all multiples of eight. Every product and carry is an integer below `2⁵⁰`,
     exact in a double on every host, and the product is known to `2⁻¹³⁷`; its fraction, taken to the nearer integer,
     is multiplied by `π/2` as a double and its remainder.

   So **no bound ships, and there is nothing the functions answer "beyond" one**: the remainder is exact to its tail at
   every finite double. The double of all nearest a multiple of `π/2` (`6381956970095103 · 2⁷⁹⁷`, whose cosine is
   `−4.687e-19`) and the largest double are vectors, and both answer their correctly rounded bits. `2²⁰` is where the
   hand-over falls because a multiple of a thirty-three-bit piece is exact only below it. The pieces and the digits are
   the ones fdlibm publishes (`pio2_1`, `pio2_2`, `pio2_3`, `pio2_3t`, and the head of its table of `2/π`); the
   reference script generates them from `π` and they agree bit for bit.

4. **The kernels: one, not two.** `Special`'s own Taylor polynomials (to `y¹⁹` and `y¹⁸`, the coefficients the doubles
   nearest `±1/n!`, by Horner's rule in `y²`) moved to `Elementary` as the internal `sinKernel` and `cosKernel`, and
   `Special`'s reflections read them. The candidate was fdlibm's minimax kernels (`k_sin.c`, `k_cos.c`). Over the
   sample's 3,000 arguments within `π/4` the worst sine is the same (0.658 units) and the worst cosines are 0.788 and
   0.826: nothing to choose on accuracy.
   The minimax kernels are quicker (six terms against nine). The Taylor kernels ship because they are the ones the
   special functions' released answers were computed with, and a pinned function's bits are its contract.

   Each kernel takes what the reduction leaves. `sinKernel y tail` joins the tail's first-order term before its one
   last addition. `cosKernel y correction` adds its polynomial to one, then adds back the exact rounding error of that
   addition (Dekker's, the first term being the larger) less the correction. Under a zero tail and a zero correction
   both are, bit for bit, the expressions `Special` evaluated: a zero tail adds a zero, and the cosine's last sum is
   then the first addition's true value, which rounds to the first addition's own answer. That is why no bit of
   `Special` moves at ANY argument, not only at its vectors. `cos` passes the tail's term and what the rounding error
   of `y²` moves the cosine by, which the kernel's own square cannot see. Without that second term the sample's worst
   cosines were 1.05 to 1.12 units, beside `π/4`; with it, 0.83 to 0.90.

5. **The reproduction is held three ways.** The 309 special-function vectors of `0.46.0` in
   `conformance/laws/elementary-laws.json` are byte-identical, and the suite pins the SHA-256 of their `<id> <value>`
   lines as the `v0.46.0` document holds them. `ElementaryLaws.laws` gains a law that runs on every host: twenty-four
   reflection answers (`gammaLn`, `gamma` and `digamma` below zero, on both sides of the reflection's quarter and at
   both parities) against the bits the sources at the tag `v0.46.0` answer. Three of them, at `−0.5`, are vectors of
   that tag's document; all twenty-four were reproduced by loading the tag's `Elementary.fs` and `Special.fs` in
   `dotnet fsi`, twenty-four of twenty-four equal. And the suite reads `Special.fs` and finds nothing declared there
   that is named for a sine or a cosine.

6. **`atan`** is fdlibm's `s_atan.c` with its published coefficients: five arms (the argument as it stands below
   `7/16`; moved against `atan ½`, `atan 1` and `atan 3/2` up to `11/16`, `19/16` and `39/16`; against `π/2` beyond),
   one odd polynomial with its even and odd halves summed apart, and each constant a double and its remainder joined
   before the last addition. The candidate was three arms with the constant added after the polynomial is rounded: a
   sample maximum of 1.43 units against 0.74.

7. **`atan2`** is the arc tangent of `|y| / |x|` with three changes to fdlibm's `e_atan2.c`, each measured. Where
   the quotient lies between `7/16` and `39/16`, the arms against `atan ½`, `atan 1` and `atan 3/2` are formed from
   the two operands and not from their quotient: `(2y − x) / (2x + y)`, `(y − x) / (y + x)` and
   `(y − 3x/2) / (x + 3y/2)`, the first two numerators exact and the third rounded once, over operands scaled by one
   power of two where they lie outside `2^±400`. Below `7/16` and from `39/16` the quotient's remainder is carried
   (Dekker's two-product) and joins the arm as a tail, so the division's rounding does not reach the answer. And where
   `x` is negative each arm reads `π` less its constant, as a double and its remainder, so the answer is rounded once
   and not twice. A quotient below `2⁻²⁹` is its own arc tangent and one from `2⁶⁶` answers `π/2`. With `x = 1`
   every arm is the expression `atan` evaluates, so `atan2 y 1` is `atan y` bit for bit, which a law holds; `atan`
   itself answers the bits it did (no bit moved over 20,000,000 arguments, nor at any `atan` vector).

   *How the middle arms were chosen.* The form first written carried the quotient's remainder into every arm, as
   `((q − 1) + tail) / (q + 1)` against `atan 1`. Over the sample it was at most 0.78 units from the true value where
   fdlibm's form was 1.2. The search of item 9 then found it MORE than one unit away where `x` is positive and the
   quotient is in the sixteenth above `11/16`: 384 of 300,000,000 inputs asked between `11/16` and `0.72`, the worst
   1.020 units. There the numerator's sum, the divisor's sum and the quotient each round a value near 0.19 while the
   answer lies in `[½, 1)`. An answer more than one unit away is not one of the two doubles beside the true value,
   so that form does not ship. Three candidates were measured beside it by the screen of item 9, on the same seeded
   inputs (2,000,000 to each sixteenth of the quotient from `1/4` to `4` on each side of `x = 0`, and 50,000,000
   between `11/16` and `0.72`), largest error against `atan ½`, `atan 1` and `atan 3/2` with `x` positive, and how many
   of the 50,000,000 were a unit away or more:

   | form | `atan ½` | `atan 1` | `atan 3/2` | a unit or more |
   |---|---:|---:|---:|---:|
   | the quotient and its tail, as first written | 0.950 | 1.014 | 0.813 | 58 |
   | the tail carried into the divisor too | 0.950 | 1.014 | 0.813 | 58 |
   | the arm against `atan 1` from the operands | 0.950 | 0.922 | 0.813 | 0 |
   | all three middle arms from the operands | 0.864 | 0.922 | 0.793 | 0 |

   Carrying the tail into the divisor changes nothing: the tail is below half a unit in the last place of the divisor,
   so the sum rounds back to the divisor. Forming the arm from the operands removes the numerator's rounding and the
   divisor's missing tail together, and nothing was a unit away. All three arms take it, and that is what ships: it is
   the better form in each (the arm against `atan ½` stood at 0.95 under the quotient's form), and it makes
   `atan2 y 1 = atan y` hold by construction. With `x` negative no form was above 0.81, before or after.

8. **The reference.** `tests/Fuaran.Core.Compute.Tests/SpecialReference/trig_reference.py`: Python's standard library
   only, exact rational and fixed-point integer arithmetic at 620 fractional bits and more, `π` by Machin's formula,
   each value rounded once to the nearest double. It prints the constants `Elementary.fs` holds, computes the
   references the vectors carry from the committed document, and scores a sample. Nothing is installed to run it. The
   special functions' reference (D78) is mpmath; these three functions need no more than a series and an exact
   subtraction, and a reference with no dependency can be re-run anywhere the suite can.

9. **Accuracy, which is a measurement and not a theorem.** Two measurements were taken, and neither is a bound.

   *The sample.* 55,998 inputs drawn at seed 725 by the benchmark harness
   (`benchmarks/results/2026-11-23-i7-8650u-phase-725.md` states the draw), each scored in units in the last place of
   the correctly rounded value. Each figure is the largest over the sample's inputs in that range, and says nothing of
   an input the sample did not draw:

   | function | range | sample maximum | 99th percentile |
   |---|---|---:|---:|
   | sin | within `π/4` | 0.66 | 0.52 |
   | sin | to `2²⁰` | 0.80 | 0.58 |
   | sin | `2²⁰` to the largest double | 0.82 | 0.58 |
   | cos | within `π/4` | 0.83 | 0.59 |
   | cos | to `2²⁰` | 0.90 | 0.57 |
   | cos | `2²⁰` to the largest double | 0.76 | 0.56 |
   | sin, cos | the doubles nearest multiples of `π/2` | 0.50 | 0.49 |
   | atan | `2⁻⁴⁰` to `2⁷⁰` | 0.74 | 0.64 |
   | atan2 | every quadrant, any exponent | 0.74 | 0.58 |

   No answer in the sample is a whole unit from the true value.

   *The search.* Three thousand inputs to a range do not find a function's worst, so each function was then searched
   where its sample errors were largest: a double-double screen over hundreds of millions of inputs, its worst finds
   scored by the exact reference (both programs are kept beside the results file, which gives every sixteenth of the
   arc tangents' range). The largest error found by any means:

   | function | largest error found | at | asked |
   |---|---:|---|---|
   | sin | 0.953 | `x = 1312.3961111785018` | 300,000,000 arguments within 0.09 of the first 2,000 odd multiples of `π/4` |
   | cos | 0.952 | `x = 16.491372400987107` | the same 300,000,000 |
   | atan | 0.854 | `x = 0.6923583728807784` | 600,000,000 arguments from `1/4` to `4`, and 60,000,000 below and above |
   | atan2, `x` positive | 0.924 | `(y, x) = (0.8169514258312074, 1.1859452743949115)` | 600,000,000 quotients from `1/4` to `4`, 300,000,000 from `11/16` to `0.72`, and 60,000,000 below and above |
   | atan2, `x` negative | 0.796 | `(y, x) = (4.199258022477436, -1.7602745130473163)` | 600,000,000 quotients from `1/4` to `4`, and 60,000,000 below and above |

   No function was found a whole unit from the true value, in any arm of the arc tangents, on either side of `x = 0`,
   from a quotient of `2⁻⁴⁰` to one of `2⁷⁰`. For `atan2` that is the result of this search and not of the sample: the
   form first written passed the sample and was 1.020 units away at its worst input (item 7). The 395 inputs the
   search found a unit or more away under that form are kept beside the results file; asked again, each is within
   0.020 units, and five of them are law vectors. It remains a measurement and not a bound. None of the 139
   referenced vectors is more than one double from its reference, and a large argument is answered as accurately as a
   small one.

10. **Cost.** About twice the host's own function on an ordinary argument (26 ns against 13 for `sin` and `cos` up to
    `2²⁰` on .NET, 69 against 34 under node); `atan` at the host's cost and `atan2` a half more; ten times more beyond
    `2²⁰`, where the digits reduce. `sinCos` is cheaper than the two calls it replaces (40 ns against 54).

11. **The law vectors.** `elementary-laws.json` gains 190 vectors after `pow`'s: the tables' rows, `atan2`'s four
    quadrants and both axes, arguments at and beside each reduction boundary (`π/4`, `2²⁰`, `2⁷⁹` where the digits'
    first index moves, the edges of the arc tangent's arms, `2⁻²⁹` and `2⁶⁶`), the doubles nearest multiples of `π/2`,
    large arguments to the largest double, quotients that underflow and overflow, and eight inputs at which the middle
    arms formed from the operands answer another bit than the same arms formed from the quotient (item 7): six
    against `atan 1`, five of them inputs the quotient's arm answered more than one unit away, and one each against
    `atan ½` and `atan 3/2` (`atan2-operands-arm-*`), beside four that ask those two arms near the largest double and
    among the subnormals, where the operands are scaled. With the quotient's arms put back into `Elementary.fs` those
    eight vectors and `atan2-five-six` fail on .NET, and nothing else does. Four are go-red cases
    (`*-reordered-polynomial-witness`). At `sin 0.512`, `cos 0.5104` and `atan 0.36`
    the same coefficients summed in another order INSIDE the shipped path (everything else as shipped: the square, the
    cosine's correction, the last additions) answer another bit; and `sinCos 0.5104` asks the pair where both its
    halves move. The suite shows each two ways: the path written out under the pinned order is the package bit for
    bit, and under the other order it is one unit away. Each was also confirmed with the other order put into
    `Elementary.fs` in place of the pinned one and the document's trigonometric vectors evaluated on .NET: the
    sine's order turns `sin-` and `sincos-reordered-polynomial-witness` red, the cosine's `cos-` and `sincos-`, the
    arc tangent's `atan-`, and nothing else. The cosine's witness has to be sought with the correction of item 4 in
    place: at `cos 0.114` the uncorrected `1 + y² · p` answers the same bit in either order, a bit other than the
    shipped path's only because the correction is missing, and the shipped path answers one bit in both orders. With
    the correction in place the order decides the last bit at about one argument in twenty within `π/4` (512,990 of
    10,000,000 asked). As first shipped, every `sin` vector with a reference was correctly rounded, so the document
    could not tell the pinned sine from a correctly rounded one, and the test that stood for it asked the host's own
    library, which differed on one host and not on another. Eight vectors (`*-not-correctly-rounded-*`), found by
    measurement and confirmed against the reference, give each of `sin`, `cos`, `atan` and `atan2` at least three at
    which the pinned answer is one double from the correctly rounded one (`ulps` 1), and `sinCos` one where its sine
    is, so a correctly rounded port fails a vector of each; the suite holds that from the committed references alone.
    D52's vectors had the same gap, held by the same kind of host-library test: no `log10` vector was off the
    correctly rounded double, and `exp` and `ln` had one each. Seven vectors join those functions' own
    (`exp-`, `ln-` and `log10-not-correctly-rounded-*`, scored by D52's eighty-digit `decimal` reference), so `exp`,
    `ln`, `log10` and `pow` each have three, and the older test is rewritten the same way; this note sits here and
    not in D52, whose record of Phase 593 stays as written.

12. **What the pin guarantees, and what it does not** (`proofs.json`, `trigonometry-pinned-by-transcription`). A host
    that transcribes the tables, the two reductions, the kernels and the arc tangent exactly answers the same bits:
    the law vectors and 111,996 sample lines were byte-identical on .NET and under node. It does not say the answers
    are correctly rounded: the accuracy above is measured, by a sample and by a search, and neither found an answer a
    whole unit from the true value.

**Not here.** `tan`, `asin`, `acos` and the hyperbolic functions wait for a reader. The evaluator's scalar set
(`ColExpr`) gains no `Sin` or `Cos`: that is a change to the pipeline wire specification, filed when a pipeline asks.

**Version.** `v0.46.0` is tagged, so this addition advances the slot to `0.47.0`. Additive: no existing surface, wire
byte or vector moves.

## 2026-11-16 — D83: a pipeline's bytes and its hash are a published specification — `Pipeline.hash` mints `compute-pipeline-sha256-v1` over the codec's canonical text, the specification's corpus is a build input the suite fails without, and a new `Transform` or `ColExpr` case lands in the codec, the text, the schema, the corpus and the manifest in one change-set

**Context.** Phase 604 (Layer 1 of the model specification format plan). A pipeline's canonical bytes were written down
only in this repository's codec and its generated `api/wire/` baselines, and "the pipeline hash" other formats cite was
a contract with no definition. The phase cuts the pipeline wire specification as its own public repository,
[`fuaran-pipeline-specification`](https://github.com/Fuaran-Core/fuaran-pipeline-specification), states the minting
rule there, and implements it here.

**Premises read at take** (item 1 of the shard). On `main` at `f947c23`: `Fit` (Phase 592), `Predict` (597), `Define`
and `invoke` with `Interpolate` (596), `KMeans` and `Bootstrap` (621) have shipped and are specified; the sequence and
host-function kinds (Phase 599), the term decomposition (602) and the tree form (603) have not, so the text names them
as planned and absent, the corpus reserves the family `tree`, and each phase adds its own cases under item 4. One
premise was partly false: "there is no pipeline-hash function". `IncrementalCodec.pipelineHash` existed — the bare hex
SHA-256 of `encodePipeline`, the key a saved incremental state is matched by. It was unspecified, unregistered and
unguarded (it digests whatever the unguarded writer writes, a non-finite float's quoted token included), so it was not
the contract the shard asks for; it was the right digest, and item 1 keeps it.

**Decision.**

1. **`Pipeline.hash`** (`src/Fuaran.Compute.DataFrame/Pipeline.fs`): `sha256:` and the lowercase hex SHA-256 of the
   UTF-8 bytes of the pipeline's canonical text (`Pipeline.canonicalText`, which is `encodePipeline`'s text written
   through the GUARDED writer), and `Pipeline.hashAlgorithm` = `compute-pipeline-sha256-v1`. A pipeline the guarded
   writer refuses — a non-finite float, an ill-formed string, a repeated member — has no hash: `Result.Error` names
   why. Over every mintable pipeline the hash is `"sha256:" + IncrementalCodec.pipelineHash p`, which the suite holds:
   one digest over one set of bytes, written as a content address where it is an identity and bare where it is a
   stored state's key. The incremental key stays as it is: it is part of a stored state's encoding, and moving it
   would invalidate every saved state for no gain. The file compiles after `Incremental.fs` to reuse the package's one
   SHA-256 (`StateWire.sha256Hex`, Fable-clean) rather than mint a second copy of it.
2. **The identifier names the format major** (the plan's ruling F6). A change that moves an existing document's bytes
   mints under `-v2`, and a `-v1` hash stays valid and distinguishable. The text (§7.4) and the specification's own
   `DECISIONS.md` D3 carry the rule; this repository changes the identifier only in a change that moves the bytes.
3. **The specification's corpus is a build input.** `tests/Fuaran.Core.Compute.Tests/PipelineSpecCorpus.fs` resolves
   it from `FUARAN_PIPELINE_SPEC` (the specification's checkout) or, unset, from `Fuaran-Pipeline-Specification` beside
   this repository's MAIN working tree (`git rev-parse --git-common-dir`, the anchor `SiblingCorpus` uses, so a linked
   worktree finds it too). The directory name is the interface (the specification's D2). An absent corpus FAILS the
   suite, naming the paths tried and the clone command; there is no skip. `run.ps1` checks for it before the build;
   CI checks it out. `PipelineSpecTests` certifies every vector (round trip, recorded digest, each reject refused with
   the refusal its class names, each accept read as its canonical document), asserts the count run is the count the
   manifest enumerates, and proves a mutated document goes red.
4. **Forward coupling.** From this phase on, a new `Transform` or `ColExpr` case, a new registry token, or any change
   to a member, ordering, encoding or refusal lands in ONE change-set across this repository's codec, the
   specification's text, its schema, its corpus and its `manifest.json` (the Program and App precedent). The corpus is
   GENERATED here — the wire-surface harness's per-case documents, realised to values a reader accepts, plus the
   declared pipelines of `PipelineHashSample` and the authored reject and accept vectors — by
   `dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-pipeline-corpus <specification root>`, and a test
   holds the specification's committed corpus equal to that emission, so a case added here without regenerating the
   corpus reds this suite. The specification's `DECISIONS.md` D4 records the same rule from its side.
5. **Fable parity.** `PipelineHashSample` (Fable-clean) declares pipelines reaching every step and expression kind the
   harness reaches (a test holds that), the float layout's boundaries, a seed past `2⁵³`, text outside ASCII and the
   three refusals. The node leg (`Node/PooledLawNode.fsproj`) prints each one's hash or refusal and the suite holds the
   lines to .NET's; the corpus's `pipeline` family carries the same pipelines, so the hashes node reproduces are the
   corpus's. The corpus's hash vectors are the minting rule's law vectors.
6. **A `lit` date or instant is read only in its canonical text** (`TemporalText.isCanonicalDate`,
   `isCanonicalTimestamp`), as a column's cells already were. A decimal's non-canonical text is still read as the
   number it names; `15/01/2026` names no day, and two spellings of one day would be two hashes. No emitter writes
   another text, so no committed document moved.
7. **Exactness.** The text states that every shipped step and expression kind is `Pinned` over pinned inputs, that
   `HostRounded` enters only through an input column so declared, and that the planned host-function kind is the first
   that is not pinned (`Exactness`, D43, D54).

**Consequences.** `Fuaran.Compute.DataFrame` gains `Pipeline` (additive; `api/Fuaran.Compute.DataFrame.txt`); no wire
byte moves (`api/wire/` unchanged). `proofs/coverage-exclusions.json` records the two operations as measured
elsewhere, standing in `PipelineSpecTests.fs`. A machine running this suite needs the specification's checkout beside
the main working tree or named by `FUARAN_PIPELINE_SPEC`.
## 2026-11-16 — D82: a pipeline is a substrate tree edited by the skeleton ops, in a package of its own, `Fuaran.Compute.PipelineTree`; the pipeline is the root and each step one node; a lifted step's id is its position and a digest of the step; an edit is refused when it adds a column read the static walk shows absent; the footprint is read at the base; the pipeline hash stays the list's and excludes the ids

**Context.** Phase 603. A pipeline is an ordered `Transform list` over a source the caller holds, so a new version of
one is a new list, and its canonical hash (`IncrementalCodec.pipelineHash`) says that two versions differ and never
how. Every tree domain built on the substrate edits its artefact by the substrate's skeleton ops, with the engine, the
op-stream, the footprint and the diff that come with them. The phase brings a pipeline into that model. The shard
was read against the tree at `f947c23` and the substrate at `0.41.0` before anything was built. Four of its
premises did not hold as written, and items 1, 6 and 8 record what was built instead.

**Decision.**

1. **Node granularity.** The root is the pipeline (`PipelineNodeContent.Pipeline`, id `pipeline` in every tree, since
   `Diff` derives a script only between two trees whose roots share an id). Each step is one node holding one
   `Transform` case (`PipelineNodeContent.Step`), and its expressions and parameters are its content, rewritten by an
   `UpdateNode` of the step. Expression-level nodes are a possible later extension and are not built.
   - *Premise checked: the source is not part of the pipeline.* The shard put the source in the root. A pipeline does not
     carry its source (`DataFrameCodec.encodePipeline` and the pipeline hash read the list alone; the evaluator takes
     the input table as an argument), and an embedded source would put rows on the op-stream. The source is therefore
     the caller's, as it is to the evaluator, and an edit is checked against it as a `PipelineContext`: the input's
     `SchemaKnowledge` and the declared schemas of the named sources.
   - *Premise checked: no source is a pipeline.* The shard made a `Join` or `Union` source that is itself a pipeline a
     child subtree. A `DataSource` is `Embedded` rows or a `Ref` name, never a pipeline, so the tree is one level deep and
     there is no subtree to build. When a source can be a pipeline, its subtree is the extension this item names.
   - The node is a record (`Id`, `Content`, `Children`) rather than a union with a childless case, so the witness's
     `ReplaceChildren` is lawful on every node. Only the pipeline may hold children (`OpScope.contained`), and the
     containment grammar (`Policy.grammar`: a pipeline holds steps, a step holds nothing) refuses a pipeline placed
     anywhere in a tree. No node has a keyed position (the keyed declaration is `None`).

2. **Ids.** A step keeps its id across every edit. `lift` mints `s<index>-` and the first twelve hex digits of the SHA-256
   of the step's canonical encoding, so the same list lifts to the same ids on every host. The digest is the shard's
   "deterministically from position" made honest for item 8: two lifts share an id only where they hold the same step at
   the same position, which is the one alignment that is not a guess. A step an edit introduces takes
   `PipelineTree.freshId`, the substrate's `FreshIds.sequential` (`step-1`, `step-2`, …, the first the tree does not hold).

3. **Order.** `InsertChild` appends, so a step is placed mid-pipeline by `PipelineTree.insertAfter tree after node`:
   the `Batch` of the insert and the `ReorderChildren` that places it after `after` (first, for `None`). The order is
   stated against the tree the caller holds, so an `after` it does not hold makes the reorder, and with it the batch,
   refuse.

4. **Identity.** Two values, for two purposes. The **pipeline hash** (`PipelineTree.pipelineHash`) is
   `IncrementalCodec.pipelineHash` of the lowered list, so it is unchanged from Phase 592's for the same list and the same
   pipeline reached by two edit histories has one identity: ids name places in one history and say nothing about what
   the pipeline computes. The **content address** (`PipelineTree.contentAddress`) is the SHA-256 of the tree's canonical
   wire, ids included: the identity of a tree on an op-stream, where an id is what an op addresses.

5. **Edits are validated, and the check is "no new unknown read".** `PipelineTree.apply context op tree` refuses a tree
   that is not a pipeline tree (`IllShaped`), then runs `Ops.apply` under the grammar and the contained scope
   (`Rejected` carries the engine's refusal), then the schema check: the edited pipeline is walked by `SchemaWalk` from
   the context's input, and a step that names a column the walk shows absent at that step is refused as
   `UnknownColumn(step id, column, available)`. Only closed knowledge refutes a name, as `SchemaKnowledge` states. The
   refusal is for a read the tree before the edit did not already have, so a pipeline that was already broken can be
   repaired one edit at a time; on a sound pipeline this is the shard's "still passes". A `Batch` is all-or-nothing and
   checked once, on the tree it lands on, so a script may pass through an intermediate state that reads a column it
   restores later. Every refusal leaves the tree unchanged.
   - The walk carries no refusal verdict (D26's walk answers the input rather than refuse), so the columns a step names
     are read by a census in this package, one arm per `Transform` case (a new case does not compile until it is
     classified, FS0025 being an error here). The planner's `reads` is the nearest existing function, but it is private,
     answers "the whole row" for nine verbs, and lives in `DataFrame.fs`. The census is held to the evaluator for every
     class of read: the suite builds one step per class naming an absent column and requires the evaluator to refuse each
     as `UnknownColumn` of that name, so a census entry the evaluator does not share fails rather than refusing an edit
     the evaluator would run. One public census shared with the planner is the follow-up recorded with this phase.

6. **The footprint is read at the base.** `PipelineTree.footprint tree ops` is `Ops.footprintIn` (Phase 500) at the tree
   the script applies to. *Premise checked:* the shard expected `Ops.footprint` to make two edits to different steps
   independent. Without a base, the substrate records an `UpdateNode`'s parent as an unknown-parent write, against which
   every structural edit and every other update is dependent, so two rewrites of different steps are reported
   dependent; a test pins that. At the base the parent is resolved, and two rewrites of different steps, or a rewrite of
   one and the removal of another, are independent. Two edits that both change the step list (an insert, a removal, a
   move, a reorder) write the root and are not. Footprint independence is a statement about the tree; whether the merged
   pipeline still passes the schema check is `apply`'s answer when the merge is applied.

7. **The op-stream.** `PipelineTree.streamWitness context` is the substrate's `StreamWitness` with `apply` as its reducer
   and the op codec as its encoding, so `OpStream.append` refuses an edit `apply` refuses and `OpStream.replay` from
   `PipelineTree.empty` reproduces the tree, and therefore its pipeline hash. A pipeline never edited starts its stream
   with `PipelineTree.script tree` (the substrate's `Ops.lower`), one insert per step.

8. **Derivation between versions.** `PipelineTree.derive before after` is the substrate's content-aware `Diff.toOpsWith`
   over the canonical node encoding. Ids align the two versions: a step present in both is kept, reordered or
   rewritten, and a step in one only is inserted or removed. Two lifts of unrelated lists share no id, by item 2, so they
   derive the worst case, every step removed and every step inserted. *Premise checked:* with ids minted from the
   position alone, two unrelated lists would share `s0`, `s1`, … and derive as rewrites of each step in place, which is
   the guessed alignment the shard refuses. Where the edit that made a version was applied and recorded, that script is
   the record, and `derive` is for versions that carry none.

9. **The wire.** A node is `{"$type":"pipeline"|"step","id":…,"children":[…]}`, a step carrying its `Transform` as `"step"`
   in the dataframe codec's own encoding; an op is `insertChild` (`parent`, `node`), `removeNode` (`target`), `moveNode`
   (`target`, `newParent`), `reorderChildren` (`parent`, `order`), `batch` (`ops`) or `updateNode` (`node`); a script is a
   JSON array of ops. All are rendered by `Canonical.write Rendering.CrossHost`. `PipelineTreeWire.decode` reads a node
   and refuses one that is not a pipeline tree. The substrate publishes no op codec, so this is the first spelling of the
   skeleton ops on a Compute wire; it uses the op names as the substrate's union spells them.

10. **The package and its boundary.** `Fuaran.Compute.PipelineTree` stands above `Fuaran.Compute.DataFrame` and takes
    `Fuaran.Core.Tree` and `Fuaran.Core.Ops` besides the substrate packages the strand stands on. That is the boundary's
    second designed widening, held per project as Phase 281's is: no other project may take either. The substrate's
    `Validator` is not taken: the containment grammar is the engine's, and the schema check is this repository's own.

**What it does not change.** The evaluator, the `Transform` union, the dataframe codec and the pipeline hash are
untouched, and so is every other package's public and wire surface. No other law file moves.

**Consequences.** Additive: a new package, which rides the `0.45.0` draft (`STABILITY.md`). Its law vectors are
`conformance/laws/pipeline-tree-laws.json` (lift, lower, the build script, each op kind accepted and refused, five
ill-typed edits refused by name, `derive` and the footprint verdict), held byte for byte on .NET and under node. The
suite also runs the substrate's witness, containment, diff, footprint and stream laws over the pipeline witness; the
lowering laws are not run, because their adequacy guard needs a filled container below the root, which a pipeline tree
cannot hold.

## 2026-11-12 — D81: the incomplete beta function keeps its accuracy at a large parameter — from a parameter sum of `128` its continued fraction is read in the contracted form with its one cancelling difference formed exactly, and a parameter below one is answered by its own series; `Special` publishes `regularizedBetaComplement` and `inverseRegularizedBetaComplement`; the t and the F read them directly; each form chosen by measurement against a fifty-digit reference, whose own quadrature was wrong in a deep tail and is replaced

**Context.** Phase 636. Phase 618 measured `Special.regularizedBeta` (D78) losing accuracy where one parameter is large
and the other small, read within a few `1 / a` of one, and wrote the t's and the F's forms round a complement `Special`
did not publish (D79 items 3, 4, 5 and 10). The phase asks for the corner repaired, the complement and its inverse
published, and the distributions re-read where a measurement says a direct read is the more accurate. The shard was
read against `main` at `b324aa6` (Phases 630, 632, 621, 633, 631, 618 and 634 landed). Its premise was re-run before
anything changed (item 1), and what the measurement changed of its plan is item 11.

**Decision.**

1. **The premise holds, and its cause is one subtraction.** On the tree as it stood, `I_x(a, ½)` at
   `x = 2a / (2a + 3.84)` (the t's tail at a `t²` of 3.84) is 35 units in the last place from the fifty-digit value at
   `a = 50`, 247 at `500`, 3,660 at `5,000`, 1,420 at `20,000`, 11,900 at `50,000` and 176,900 at `500,000` (after
   this decision: 35, where the parameters sum below its threshold, then 5, 25, 3, 7 and 20). The
   function there is Abramowitz and Stegun's continued fraction 26.5.8, whose first denominator is
   `1 − (a + b) x / (a + 1)`. On the fraction's side of the switch that denominator is `λ / (a + 1)` with
   `λ = a + 1 − (a + b) x ≥ 2x`, so near the switch it is as small as `2 / (a + b + 2)`: a difference of two numbers
   that agree in all but their last `log₂ ((a + b) / 2)` bits, formed in one rounded subtraction. The relative error is
   about `(a + 1) / λ` units, which is the growth measured. The same subtraction is read from the other end
   (`I_x(b, a)` at a small `x`, the mirrored call the function makes beyond its switch), and where both parameters are
   large and `x` is within a standard deviation of the mean, where D78's sample measured hundreds of units and not
   thousands because `λ` there is of order `√a` and not of order one.

2. **From `a + b = 128` the fraction is read in its contracted form, with `λ` formed exactly.** The even part of the
   same fraction (DiDonato and Morris 1992, equation 9) is `I_x(a, b) = prefix / f`,
   `f = β₀ + α₁ / (β₁ + α₂ / (β₂ + …))`, with `β₀ = a λ / (a + 1)`,
   `αₘ = (a + m − 1)(a + b + m − 1) m (b − m) x² / (a + 2m − 1)²` and
   `βₘ = m + m (b − m) x / (a + 2m − 1) + (a + m)(λ + m (2 − x)) / (a + 2m + 1)`. It is the fraction D78 reads, two
   partial quotients at a time, written so that the one difference that cancels is `λ` and no other term subtracts
   two numbers near one. `λ` is formed once (`betaLambda`): the sum `a + b`, the product `x (a + b)` and `a + 1` each
   with their rounding errors (`twoSum`, `twoProd`, which `Elementary` already holds), so
   `λ = ((t − ph) + (tl − pl)) − x e − xLow s` keeps its relative accuracy. The fraction is evaluated by the modified
   Lentz method under the rule every series of the module stops on (D78 item 9): relative change below `2⁻⁵²`, a cap
   of 65536, NaN at the cap. `m − 1` and `2m − 1` are formed before they are added to `a`, because a parameter far
   below one must not pass through a sum with one on its way to being read alone (written `(a + m) − 1` the first
   numerator lost ten bits at `a = 0.0014`, 600 units of the answer).

3. **Why `128`, and why a threshold at all.** Below a sum of `128` the first denominator is at least `2 / 130`, so
   the subtraction has lost at most six bits, and the function below that sum answers the bits it answered: every
   vector there is the line it was. The threshold was chosen on a sample drawn by the sum, a power of two a range,
   half with one parameter small and half with both of a size, `x` within six standard deviations of the mean
   (240 a range; largest, 99th percentile and mean, in units in the last place):

   | `a + b` | the fraction as D78 left it | the contracted fraction |
   |---|---:|---:|
   | `[16, 32)` | 109 / 44 / 7.9 | 198 / 125 / 7.9 |
   | `[32, 64)` | 76 / 50 / 7.5 | 72 / 53 / 6.9 |
   | `[64, 128)` | 163 / 125 / 9.4 | 163 / 79 / 6.4 |
   | `[128, 256)` | 131 / 95 / 11.2 | 73 / 36 / 5.3 |
   | `[256, 512)` | 377 / 247 / 16.3 | 58 / 51 / 5.3 |
   | `[512, 1024)` | 559 / 318 / 25.5 | 72 / 53 / 5.2 |
   | `[1024, 2048)` | 1,097 / 824 / 49 | 92 / 49 / 6.4 |

   (The second column is the contracted fraction with nothing else exchanged, read at every sum in a working copy;
   from `128` it is the function as shipped.) Below `64` the contracted form is not the better one: it is the worse
   at the 99th percentile in `[16, 32)`, where the errors are the tails' and not the fraction's. In `[64, 128)` it
   is ahead at the 99th percentile and level at worst. From `128` the old form's 99th percentile is 2.6 to 17 times
   the new one's, and its mean two to eight times. A threshold on the sum is one comparison a host transcribes, it
   does not depend on `x` (so the function changes form nowhere along its argument), and it leaves the pinned bits
   of every small parameter where they were. Replacing the fraction at every sum is the simpler function and was
   not taken: the measurement does not ask for it below `64`, and it would move every incomplete beta vector and
   every t and F vector.

4. **What `1 − x` rounded away, from `128`.** The function beyond its switch is the mirrored fraction at
   `y = 1 − x`, which a double holds exactly only from `x = ½`. D78 restores the rounding to first order through the
   density (item 7 there). From `128` it is not added: `λ` takes `yLow = (1 − y) − x` itself, and the prefix is
   already a function of `x` and not of the rounded `y`, so the density's term would count the prefix's share
   twice. Measured on 400 inputs with a parameter in `[0.5, 500)` against one in `[1e3, 1e5)`, `x` below one half
   and up to eight standard deviations beyond the switch: the upper value is 38,920 units out at worst as D78 left
   it, 25,080 with the contracted fraction at the rounded `y` and the density's correction, and 273 with `λ` taking
   `yLow` and no correction (99th percentiles 24,850, 10,840 and 102).

5. **A parameter below one, from `128`, is answered by its own series where its side is the small one.** The pair
   `(I_x, 1 − I_x)` is the fraction's value and its complement. Where the fraction's own parameter is below one, its
   value can be within `10⁻⁶` of one at the switch (most of the mass is at that end), and the complement is then a
   difference of two near ones: D78 reads Gauss's series there, up to an argument of `0.99`, and beyond it states
   the function as "accurate to a unit in the last place of one and not of itself". From a sum of `128` that
   argument is always beyond `0.99` (the switch is within `2 / 130` of the end), so the small one is computed
   instead (`betaUpperSmall`), wherever the fraction's parameter is below one and its value above one half:
   `1 − I_x(a, b) = −expm1 L − exp L · a · Σ_{n ≥ 1} (1 − b)ₙ xⁿ / ((a + n) n!)` with
   `L = a ln (x b) + ((a + b − ½) ln (1 + a / b) − a) + (tail (a + b) − tail b) − ln Γ(1 + a)`. It is the function's
   own series `xᵃ / (a B(a, b)) · ₂F₁(a, 1 − b; a + 1; x)` taken from one, the form D78's `upperSmall` gives the
   incomplete gamma function, with `b`'s falling products where that has powers. There `b x < 2`, so the terms
   alternate and fall from the second. Every part of `L` is formed relative to `a`: the product `x b` with its
   rounding error, the difference of the two Stirling tails as three terms factored through
   `1 / (a + b) − 1 / b`, and `ln Γ(1 + a)` by the kernel (D78 item 3). So the answer keeps its relative accuracy as
   `a` falls to zero, where it is `a` times a number of order one. It stops on the module's rule. Gauss's series is
   not read from a sum of `128` up. Without this arm the corner with the small parameter in `[0.05, 1)` measured 373
   units at worst (a value of `0.015` read as one minus `0.985`), and the loss grows as the parameter falls, being
   the reciprocal of the value.

6. **The complement.** `Special.regularizedBetaComplement a b x` is `1 − I_x(a, b)`: `I_{1 − x}(b, a)` with `1 − x`
   taken in full and not as the double nearest it. It is the second member of the pair the function already
   computed (`regularizedBetaBoth`), so it is computed and not subtracted wherever it is the small one: beyond the
   switch it is the mirrored fraction with what `1 − x` rounded away restored; before the switch it is Gauss's
   series (below `128`) or `betaUpperSmall` (from it) where the fraction's parameter is below one. Elsewhere before
   the switch the function itself is the small one of the pair and the complement is one minus it, which is exact
   to a unit in the last place of one and is what a complement near one can be. `1` at `x = 0`, `0` at `x = 1`, NaN
   wherever `regularizedBeta` is. A caller that holds `y = 1 − x` and not `x` reads `regularizedBeta b a y`; the two
   agree within the function's own error. The names follow D74's rule: the module qualifies them once
   (`Special.regularizedBetaComplement`), and no qualifier is doubled.

7. **The complement's inverse.** `Special.inverseRegularizedBetaComplement a b q` is the `x` with
   `1 − I_x(a, b) = q`. D78's inverse (item 8) is refactored into one solve over the pair `(p, q)` as the caller
   holds them (`inverseBoth`): whichever is at most one half is exact, and it is the one every residual reads.
   `inverseRegularizedBeta a b p` is that solve at `(p, 1 − p)`, the arithmetic it was; the complement's inverse is it
   at `(1 − q, q)`. So a small upper probability is read as it stands, where `inverseRegularizedBeta a b (1 − q)`
   reads a `1 − q` rounded to the last place of one (at `q = 10⁻²⁰` that is one, and the plain inverse can only
   answer one). From `q = ½` up, `1 − q` is exact and the two inverses are one function, bit for bit: a law holds
   it. `1` at `q = 0`, `0` at `q = 1`, NaN wherever the inverse is. A caller that wants `1 − x` reads
   `inverseRegularizedBeta b a q`, which solves for it directly. Each of the two has a `Pinned` row in
   `ElementaryOperations.exactness`.

8. **The distributions read them directly.** Each t and F form D79 wrote round the missing complement was measured
   against the form that reads it, on the repaired function, over D79's own sample and 1,200 more F inputs with a
   degree of freedom up to a million (item 9's second table). The direct read is kept for all four:
   - `betaNearOne a b y` (the function at `fl (1 − y)`, corrected through the density) is
     `Special.regularizedBetaComplement b a y`.
   - `StudentT.cdf`: the tail is `½ (1 − I_w(½, ν/2))` by the complement at `w = t² / (ν + t²)` wherever D79 read
     the centre or `betaNearOne` (`t² (ν + 2) ≤ 3ν` or `2t² < ν`), `½ I_z(ν/2, ½)` with `z` formed directly
     otherwise, and the leading term from `t = 2³⁰ √ν`. Three arms where there were four. In the centre the
     complement is the subtraction D79 wrote, the same bits, except where the function now computes the small
     member.
   - `StudentT.quantile`, below `p = ¼`: the root `z` of `I_z(ν/2, ½) = 2p` while it is at most one half; beyond,
     the same root from its other end, `w = 1 − z` by `Special.inverseRegularizedBetaComplement ½ (ν/2) (2p)`, which
     takes the exact `2p`. D79's one Newton step on the distribution function, its `1/16` and `2⁻²⁶` thresholds and
     the root read at a rounded `1 − 2p` are gone. From `p = ¼` the form is unchanged (`betaOdds ½ (ν/2) (1 − 2p)`).
   - `betaOdds a b p`, the odds at the root of `I_x(a, b) = p`: the root while it is at most one half; beyond, the
     same root from its other end, `Special.inverseRegularizedBetaComplement b a p`. The choice between two
     inverses by the mirrored density, and the argument saying whether `1 − p` was exact, are gone.
   - `FisherF.cdf`, beyond `d1 x = d2`: `Special.regularizedBetaComplement (d2/2) (d1/2) z` at
     `z = d2 / (d1 x + d2)`, at every `z`; the `2⁻²⁶` threshold is gone. `FisherF.quantile` reads the new `betaOdds`.

   `Kernel` loses `betaDensity`, `betaNearOne` and `nearOneFrom`; `StudentT` loses its density. The package still
   writes no loop and no special function, and its public surface is the eight functions it was.

9. **What was measured** (`benchmarks/results/2026-11-12-ryzen9-9950x-phase-636.md`; the reference is mpmath 1.4.1
   at fifty digits, by `tests/Fuaran.Core.Compute.Tests/SpecialReference/special_reference.py` and the distributions'
   script that imports it). .NET and node answered the same bits on every line of the three samples: 17,960 of the
   new family `beta-corner`, 35,480 of `distributions` and 29,563 of `special`. Largest error in units in the last
   place, with the 99th percentile and the mean, before and after, over a seeded draw of 300 a row
   (`corner` is `(a, b, 1 − c / a)` with `a` the large parameter and `c` log-uniform over `[1e-3, 30)`; `mirror` is
   the same point read from the other end, `(b, a, c / a)`):

   | `regularizedBeta` | before | after |
   |---|---:|---:|
   | corner, `a` in `[128, 2e4)`, `b` in `[0.05, 1)` | 18,790 / 2,860 / 222 | 43 / 27 / 3.4 |
   | corner, `a` in `[128, 2e4)`, `b` in `[1, 10)` | 4,859 / 3,435 / 132 | 128 / 64 / 5.1 |
   | corner, `a` in `[128, 1e6)`, `b` in `[1e-6, 0.05)` | 18,190,000 / 5,826,000 / 314,000 | 59 / 35 / 3.8 |
   | corner, `a` in `[2e4, 1e6)`, `b` in `[0.05, 1)` | 432,900 / 189,800 / 12,200 | 32 / 29 / 3.7 |
   | corner, `a` in `[2e4, 1e6)`, `b` in `[1, 10)` | 99,410 / 59,210 / 2,570 | 180 / 109 / 9.5 |
   | mirror, the five ranges in that order | 200, 557, 309, 14,060, 16,940 | 18, 139, 22, 26, 328 |
   | both parameters in `[1e3, 1e6)`, within six standard deviations of the mean (120) | 9,139 / 4,445 / 290 | 50 / 33 / 4.7 |

   So: with the small parameter below one the function is within 64 units of the reference at a large parameter up
   to a million, at every distance from one the sample drew; with the small parameter in `[1, 10)`, within 512 (the
   largest are tails of `10⁻²⁰` and below; where the value is at least `1/16` the largest is 189). The complement
   over the same rows: 14, 179, 23, 27 and 252 at the corner and 45, 67, 46, 29 and 158 mirrored (before, as the
   function's unpublished upper value: up to 15,280,000), 32 with both large, and 819 over D78's own draw of triples,
   in a deep tail (78 where the value is at least `1/16`). The inverse and the complement's inverse are
   within two units where the root is near one, and within 420, 61, 4,826, 485 and 101 (303, 50, 1,215, 234 and 65 for
   the complement's) where the root is the small one, which is D78's conditioning, about `1 / a` for a small root
   (before: up to 8,920,000). D78's own sample, re-run: `regularizedBeta` 405 at worst (402), 104 at the 99th
   percentile (130) and 6.8 on average (8.3), 182 where the value is between `10⁻¹⁰` and `10¹⁰` (as before);
   `inverseRegularizedBeta` 477 (477), mean 7.1 (7.7); every other function of the module answers the bits and the
   figures it did. D78's stated range for the incomplete beta function (parameters to `2 × 10⁴`) is `10⁶` now for
   one large and one small parameter and for both large within six standard deviations of the mean.

   The distributions, the same statistics, by D79's ranges and one more:

   | function | range | Phase 618 | D79's forms on the repaired function | the direct forms (shipped) |
   |---|---|---:|---:|---:|
   | `StudentT.cdf` | `ν < 100` | 560 / 81 / 5.3 | 560 / 81 / 5.3 | 560 / 81 / 5.4 |
   | | `100 ≤ ν < 4e4` | 6,095 / 1,969 / 104 | 133 / 73 / 6.4 | 140 / 73 / 5.9 |
   | | `4e4 ≤ ν < 1e6` | 137,400 / 66,190 / 3,580 | 445 / 64 / 7.6 | 124 / 61 / 5.4 |
   | `StudentT.quantile` | `ν < 100` | 314 / 92 / 8.1 | 314 / 92 / 8.1 | 314 / 92 / 8.1 |
   | | `100 ≤ ν < 4e4` | 2,509 / 634 / 27 | 21 / 15 / 2.6 | 16 / 9.7 / 1.8 |
   | | `4e4 ≤ ν < 1e6` | 24,590 / 18,700 / 796 | 21 / 16 / 3.0 | 16 / 9.4 / 1.9 |
   | `FisherF.cdf` | `d1, d2 < 1e3` | 1,198 / 364 / 20 | 1,198 / 364 / 18.5 | 1,198 / 364 / 18.3 |
   | | a parameter in `[1e3, 4e4)` | 15,800 / 2,817 / 127 | 1,299 / 251 / 15.1 | 1,271 / 218 / 15.0 |
   | | a parameter in `[4e4, 1e6)` | not drawn | 790 / 133 / 11.9 | 841 / 213 / 13.2 |
   | `FisherF.quantile` | `d1, d2 < 1e3` | 320 / 163 / 16 | 320 / 131 / 13.6 | 320 / 131 / 13.4 |
   | | a parameter in `[1e3, 4e4)` | 3,143 / 1,004 / 63 | 492 / 204 / 16 | 289 / 131 / 11.1 |
   | | a parameter in `[4e4, 1e6)` | not drawn | 10,010 / 2,559 / 109 | 387 / 90 / 13.3 |

   The repair is most of it: a t tail at a million degrees of freedom was good to eleven digits and is good to
   fourteen, as it is at a hundred. The direct forms are then at least as accurate on every row but two, and
   cheaper: the t's distribution function at `100 ≤ ν < 4e4` (140 against 133 at worst, lower at the 99th percentile
   and on average) and the F's with a parameter beyond `4e4` (841 against 790, and 213 against 133 at the 99th
   percentile). On that last row the two forms differ on 136 of 535 inputs, the direct read the nearer on 74 and
   the farther on 62, and the largest differences are probabilities below `10⁻²⁵`, where the prefix's exponent
   carries the error (D78 item 10) and the two forms round it differently. It is kept for the F too: it is one read where D79's was a
   function value, a density and a correction, and where they differ the difference is not the form's. What still
   sets the largest figures is unchanged from D79 item 9: a tail far out carries about twice its exponent, and a
   quantile where the mass is at zero carries the function's error times `2 / d`.

   Cost. Over the corner's rows on .NET, the lowest of three interleaved captures of each build, nanoseconds a
   value: with the small parameter below one the function costs 179 to 240 where it cost 147 to 152 (the series of
   item 5, read on about half those inputs); with it in `[1, 10)`, 120 to 145 where it cost 126 to 185; with both
   parameters large, 347 where it cost 481 (the contracted fraction takes one division an iteration where the old
   one took two); from a sum of `128` on the by-sum sample 205 to 269 where it cost 235 to 263. The complement costs
   what the function does (119 to 302 over the same rows), and its inverse what the inverse does (1,164 to 1,967
   against 1,250 to 2,108). The distributions, one capture a host on a machine that was not quiet, .NET and then
   node: `StudentT.cdf` 172 and 434 where D79's form over the same function costs 295 and 586; `FisherF.cdf` 217 and
   449 against 398 and 572; `StudentT.quantile` 2,533 and 5,751 against 2,656 and 4,436; `FisherF.quantile` 3,353 and
   6,830 against 3,724 and 6,076. The distribution functions are cheaper by the density D79's correction paid for;
   the quantiles are two inverses either way, and their captures differ by less than the captures of one build do.

10. **The moved vectors.** Every file under `conformance/laws/` had its SHA-256 recorded before the first edit.
    `dataframe-parity.json`, `linear-algebra-laws.json`, `regression-laws.json`, `solvers-laws.json` and
    `transform-laws.json` are byte-identical. That last one is a fact about its rows and not about `Predict`: its
    interval rows read `StudentT.quantile` at a handful of degrees of freedom and levels, and each of those
    quantiles happens to be the same bits under the direct form. Over degrees of freedom of 1 to 130 and eight
    levels from one half to `0.999`, 580 of 1,040 quantiles answer other bits than Phase 618's, by 34 units in the
    last place at most (8 at most to thirty degrees of freedom, 1 on 244 of them).
    - `elementary-laws.json`: 58 vectors appended (17 of `regularizedBeta` and `inverseRegularizedBeta` in the
      repaired region and either side of its threshold, 25 of the complement, 16 of its inverse) and one head
      member, `complements`. Five vectors that were there moved, each a `regularizedBeta` vector with a parameter sum
      of `128` or more, each because the fraction is the contracted one there (distance from the reference before
      and after): `beta-inc-large-at-mean` 14 to 2, `beta-inc-large-below-mean` 14 to 2,
      `beta-inc-very-large-at-mean` 48 to 8, `beta-inc-very-large-skewed` 71 to 3, `beta-inc-small-and-large` 3 to 7.
      Every other vector is the line it was, but for the comma that now follows the last of them.
    - `distributions-laws.json`: four vectors appended (the F at a degree of freedom of a million). 34 moved. By
      the repaired function alone, under D79's forms or the direct ones: `t-cdf-thousand-2.5-percent` 403 to 8,
      `t-cdf-thousand-three` 70 to 6, `t-cdf-thousand-tail-1e-10` 23 to 7, `t-cdf-ten-thousand-2.5-percent` 210 to 19,
      `t-cdf-ten-thousand-tail-1e-10` 602 to 15, `t-cdf-forty-thousand-three` 1,428 to 1,
      `t-cdf-forty-thousand-tail-1e-10` 1,085 to 33, `t-cdf-million-three` 43,078 to 2,
      `t-cdf-million-tail-1e-10` 17,739 to 5, `t-cdf-million-upper` 9,251 to 0,
      `t-quantile-thousand-2.5-percent` 25 to 2, `t-quantile-thousand-99.95-percent` 4 to 1,
      `t-quantile-thousand-1e-10` 0 to 2, `t-quantile-thousand-40-percent` 2 to 4,
      `t-quantile-ten-thousand-2.5-percent` 201 to 1, `t-quantile-ten-thousand-1e-10` 10 to 0,
      `t-quantile-forty-thousand-0.1-percent` 329 to 0, `t-quantile-forty-thousand-near-half` 6 to 9,
      `t-quantile-million-2.5-percent` 52,406 to 1, `t-quantile-million-1e-10` 91 to 0,
      `t-quantile-million-upper-1e-10` 744 to 1, `f-cdf-thousand-two-small` 0 to 2,
      `f-cdf-three-ten-thousand` 141 to 2, `f-cdf-ten-thousand-both-median` 27 to 1,
      `f-cdf-ten-thousand-both-upper` 1 to 0, `f-quantile-thousand-one-1e-10` 10 to 1,
      `f-quantile-three-ten-thousand-95-percent` 580 to 3, `f-quantile-ten-thousand-both-median` 8 to 0. By the direct
      form alone, at parameters the repair does not reach: `t-cdf-cauchy-minus-one` 2 to 1,
      `t-quantile-thirty-95-percent` 6 to 5, `t-quantile-thirty-five-97.5-percent` 1 to 0,
      `t-quantile-hundred-twenty-99.5-percent` 1 to 2, `f-cdf-hundred-two` 0 to 5,
      `f-quantile-two-tenth-30-percent` 60 to 34. Twenty-eight of the 34 are nearer their reference than they were
      and six are farther, by five units at most (`t-quantile-thousand-1e-10`, `t-quantile-thousand-40-percent`,
      `t-quantile-forty-thousand-near-half`, `t-quantile-hundred-twenty-99.5-percent`, `f-cdf-thousand-two-small`,
      `f-cdf-hundred-two`), as is `beta-inc-small-and-large` in the other file, by four: a form is chosen on the
      sample of item 9, and a single input can land a few units either way. The 34 distances summed to 128,544 and
      sum to 173.
    - The suite's recorded bounds follow (`SpecialVectors.referenceUlps`, `DistributionsVectors.referenceUlps`, each
      the power of two at or above its worst vector): `regularizedBeta` 64 (128), the complement 64, its inverse 8;
      `StudentT.cdf` 64 to forty thousand degrees of freedom and 8 beyond (2,048 and 65,536), `StudentT.quantile` 128
      and 1 (512 and 65,536), `FisherF.cdf` 16 and 2 (256, none beyond), `FisherF.quantile` 256 and 4 (1,024, none
      beyond).

11. **What the tree and the measurement changed of the shard's plan.**
    - **The repair is not an asymptotic expansion.** The shard names Temme's uniform form and a better-conditioned
      fraction as candidates. The loss was one cancelling subtraction in a fraction that otherwise converges in
      thirty to seventy iterations at a parameter of a million, so the better-conditioned fraction is the whole
      repair, and it reuses the prefix, the switch, the Lentz evaluation and the stopping rule D78 chose. Four forms
      of it were measured on a prototype before the package was touched (the fraction as it was; with only its
      first denominator formed from `λ`; contracted with `λ` in plain doubles; contracted with `λ` exact): at `a`
      up to a million and `b` below one they are 224,000, 253,000, 72,700 and 28 units out at worst. An expansion in
      the large parameter (DiDonato and Morris's for a small second parameter, or Temme's) would be a second
      algorithm with a boundary of its own and was not built.
    - **"Computed directly, never as `1 − regularizedBeta`" is true where the complement is the small one.** Where
      the function itself is below one half on its own side of the switch, its complement is one minus it, which
      loses nothing a complement near one has. What the shard's sentence rules out, and item 5 and item 6 do not
      do, is a small complement formed by subtraction. Below a sum of `128` D78's own limit stands for both
      functions (item 12).
    - **The series arm of item 5 was not in the shard.** The measurement asked for it: the corner with a small
      parameter below one is where the t lives (`b = ½`), and without it the function there is a complement.
    - **The reference was wrong in a deep tail.** `special_reference.py` integrated the density where both
      parameters are at least ten and sum to two thousand or more, the interval cut at the standard deviation's
      scale. Against `betainc` at seventy digits that quadrature is out by `1.2e-4` of the value 23 standard
      deviations into a tail, `1.2e-5` at 28 and `1e-13` at thirteen; the first complement measured with both parameters large was
      "4.6e9 units" from it and correct. It is replaced by Abramowitz and Stegun's fraction evaluated from its tail
      at eighty digits, the count of partial quotients doubled until two evaluations agree to `1e-45`, which agrees
      with `betainc` at ninety digits to `6.6e-52` over 30 such inputs out to thirty standard deviations and runs in
      under a tenth of a second at parameters of a million. The same fraction now serves every sum of two thousand
      or more (it agrees with `betainc` to `2.3e-51` over 1,241 inputs with one parameter below ten), where
      `betainc` can take minutes an input: the F's sample with a degree of freedom up to a million did not finish
      in half an hour of fourteen processes. Every reference the vectors carried re-derives to the same bits under
      it: 182 of `SpecialVectors` and 143 of `DistributionsVectors`. D78 item 10's figures were taken against the
      quadrature; re-run against the fraction they are the figures of item 9, and none moved by more than the
      function did. The distributions' script had worked round the quadrature in its own tails
      (`beta_lower`); that work-round is removed.
    - **The method card waits for Phase 623**, as for D78 and D79.

12. **Not changed here.**
    - Below a parameter sum of `128`, D78's statement stands: where `b < 1` and `x` is beyond `0.99`, the function
      is a complement, accurate to a unit in the last place of one and not of itself, and the complement has the
      mirrored limit (`a < 1`, wherever Gauss's series is not read: `x` below `0.01`, or a value not below `x`).
      Measured at a large parameter in
      `[64, 128)` with the small one drawn down to `10⁻⁶`: 151,300 units at worst. `betaUpperSmall` would answer it,
      with more terms of its Stirling difference than the three a parameter of `127` needs; extending it below
      `128` moves vectors there and is a decision of its own.
    - The prefix. With the small parameter in `[1, 10)` and the large one beyond ten the prefix is
      `exp (a ln x + b ln (1 − x) − ln B(a, b))`, three terms of size `b ln a` that cancel to order one. That is the
      likeliest source of the 100 to 330 units of item 9's `[1, 10)` rows (the mean is 5 to 32): the fraction is the
      one that measures 30 with the parameter below one, and the prefix is what differs. It was not isolated by a
      measurement. D78's form for both parameters from ten, with the small parameter's Stirling tail computed
      exactly, is the candidate; it was neither built nor measured.
    - The inverse's start, bracket and rule (D78 item 8), the incomplete gamma functions, and every other function
      of the module.

**Falsifiers.** Each is a way this decision would be wrong, and what would show it.

- A host answers other bits: the 58 new vectors and the five moved ones of `elementary-laws.json`, and the 38 of
  `distributions-laws.json`, are rendered on .NET and under node and compared byte for byte, each with its distance
  from the reference; `ElementaryLaws.laws` gains a law over the complements, run on both hosts.
- The corner is worse than stated: the suite holds every vector of the repaired region
  (`beta-inc-corner-*`, `beta-comp-corner-*`, and both parameters at a million) to 32 units, and the functions to
  their recorded bounds. The figures are a measurement over the stated ranges, never a theorem: a large parameter
  beyond a million, or both large and more than six standard deviations from the mean, is computed by the same
  rules and not covered.
- The threshold is in the wrong place: `beta-inc-below-contracted` and `beta-inc-at-contracted` are the same
  function a quarter either side of a sum of `128`, 46 and 5 units from their references, and a test holds the
  function continuous across it.
- The complement is a subtraction somewhere it should not be: a test reads four vectors where it is small and
  finds one minus the function hundreds of units from the reference the complement meets within 64.
- The direct forms are worse than D79's somewhere the sample did not draw: `DistributionsBench.fs` keeps D79's
  forms as candidates (`d79`) over the public functions, scored over the same inputs, so the comparison of item 9
  is one run.
- The reference is wrong again: its fraction is checked against `betainc` wherever both can be run, and the two
  figures above are the check.

**Alternatives considered.** The contracted fraction at every parameter sum, and an asymptotic expansion: item 3 and
item 11. `1 − regularizedBeta` behind the new name: it is what the phase exists to end. A complement that takes
`1 − x` as its argument: that is `regularizedBeta b a y`, which `Special` already publishes, and a caller that holds
`x` would have to round `1 − x` to call it. Publishing the pair as one function returning both: no reader, and a
struct return is not the shape of the module. Keeping D79's Newton step as a polish after the direct root: measured
as part of `d79`, and the direct root is already the nearer.

## 2026-11-11 — D80: `Glm` calls the solvers — its IRLS is `Damped.iterate` and θ's Newton is `Root.bracketedNewton` over `[2⁻²⁰, 2²⁰]`, an answer on the bracket's end refused; θ moves by measurement, not by reproduction, and no law vector moves; the reproduction law becomes a record of the bits `Glm.fit` answered before

**Context.** Phase 634. Phase 633 shipped the solvers (D77) and a reproduction law showing that `Damped.iterate` and
`Root.bracketedNewton`, given `Glm`'s own step, test, objective and halving, answer `Glm.fit`'s bits on the reference
corpus. It also found that θ's iteration is NOT a bracketed Newton: `thetaMl` was MASS's `theta.ml`, an unbracketed
Newton that reflects a negative θ (`θ ← |θ| + score/info`), refusing a θ above `2⁵⁰`. So the shard's "no bit moves"
could not hold for θ as written, and the operator left the choice to a measurement: (a) Solvers gains θ's actual
method as a pinned arm of its own, so no θ bit moves; or (b) θ moves to `Root.bracketedNewton`, and what moves is
listed. The shard was read against `main` at `1a80f11` (Phases 630, 632, 621, 633, 631 and 618 landed).

**Decision.**

1. **The IRLS loop is one call of `Damped.iterate`** (D77 item 7). The state is the coefficients, the linear
   predictor and means they give, the first row out of range, and the working weights and solve that proposed them.
   The step is the weighted solve (`observe` is called inside it, once per iteration, as before); a state is
   inadmissible where `firstBad` names a row, and the refusal is `outOfRange`'s; the objective is the deviance, never
   read at an inadmissible state; the midpoint halves every kept coefficient towards the standing state's, an aliased
   column staying aliased; ten halvings; the start is a seed. The rule is `RelativeChange (Tol, 0.1)` under the cap
   `MaxIter`. The refusals map with their payloads unchanged: `StepRefused e` and `HalvingExhausted (_, Some e)` are
   `e` (`Separation`, `MeanOutOfRange`, the solve's own); `HalvingExhausted (k, None)` is `NotConverged k`; the cap is
   `NotConverged k` after the binomials' separation rule is read at the last state, as it is read at a converged one.
   The Gaussian identity fit stays one solve: it is no iteration. The hand-written `iterate` and `halve` are deleted.

2. **θ moves to `Root.bracketedNewton` (option b), by measurement.** `f` is the score and `f′` minus the observed
   information, so a Newton step is `θ + score/info` exactly as before; the bracket is `[2⁻²⁰, 2²⁰]`; `shrink = 1`, so
   a Newton point is replaced by the bracket's midpoint only where it leaves the bracket; the rule is
   `AbsoluteStep 2⁻¹³` (MASS's `.Machine$double.eps^0.25`) under the cap `MaxIter − 1`, the old loop's; the start is
   `Σw / Σ w·(y/μ − 1)²`. The information at the answer is read from the evaluation that found it, not computed again.
   Every refusal of the solver is `ThetaNotConverged alternation`, as every refusal of the loop was. **An answer on
   an end of the bracket is refused** (item 4). The constant `2⁵⁰` and the loop are deleted.

3. **The measurement** (`benchmarks/Fuaran.Core.Compute.Benchmarks/GlmBench.fs`, `node Program.js 5 glm` or the same
   program on .NET; the reference is `tests/Fuaran.Core.Compute.Tests/fixtures/glm-reference/theta_reference.py`,
   mpmath 1.4.1 at fifty digits, θ's maximum-likelihood root at the same fixed means, the first sign change of the
   score from `2⁻²⁰` to `2⁶⁰` bisected to `1e-40`). Each method solved θ at fixed means — the Poisson fit's, where the
   first alternation starts, and the negative binomial fit's own — over the reference corpus's counts, over seeded
   negative binomial data at θ = 0.05, 0.5, 8, 200 and 5000 and Poisson data (five seeds each, 30 to 400 rows), and over
   a sweep of 3,995 small seeded designs (10 to 40 rows) at θ log-uniform over `[1e-2, 1e6]`, one in eight at
   Poisson's limit. AMD Ryzen 9 9950X, Windows 11; .NET 10 Release x64, and node v22.23.4 over Fable 5.0.0, whose
   reference lines agree with .NET's byte for byte (1,113 lines).
   - **Where both answer, they answer the same bits** on every corpus and seeded case (52 sets of means: 43 both
     answer, 9 both refuse) and on 1,832 of the sweep's designs, with the same Newton steps; 1,862 designs both refuse. The two differ only where the
     unbracketed Newton left the bracket or went below zero, which no corpus case and no law vector reaches: 94
     designs where both answer different bits (the bracketed answer is the nearer to the reference in 50 and the
     reflected in 44; the bracketed one is within a relative `1.2e-5` of the root in all 94, the reflected one in all
     but one, which has no correct digit), and 207 where one answers and the other refuses.
   - **The reflection fails where θ is small.** On 205 designs the reflected loop reached its cap (24 Newton steps,
     reflecting and overshooting again) where a root lies inside `[2⁻²⁰, 2²⁰]` — at θ from 0.005 to 0.4, ordinary
     overdispersed counts — and the bracketed Newton found it. On 2 designs the bracketed Newton refused (its start
     fell outside the bracket) where the reflected loop answered, and both of those answers are wrong in every digit
     (`9.9e-7` for a root at `1.5e-3`, `1.9e-9` for `3.6e-5`). Over the answers the reference was run on, the
     reflected loop answered 3 values with no correct digit and the bracketed Newton 1 (`1.5e-4` for a root at
     `1.4e-2`: the absolute step rule stops a bisection whose width is below `2⁻¹³`, item 6). The bracketed Newton
     refused 2 designs whose root lies inside its bracket, against the reflected loop's 205.
   - **Accuracy.** Where both answer the same bits the accuracy is one figure: over the 43 corpus and seeded sets of means
     (the reference was run on those and on the sweep's designs where the two differ), a median relative distance from the fifty-digit root of `5.4e-11`, a 90th percentile of
     `1.7e-8` and a largest of `7.1e-6`. Over all 342 bracketed answers it was run on: a median of `3.5e-8`, a 90th
     percentile of `7.1e-6` and a 99th of `9.3e-5`, larger at small θ, where the step rule's absolute `2⁻¹³` is a
     relative one of `1e-2`. On the corpus's two estimated fits θ is the same bits as before,
     `8.441166827096124` and `8.139668123835209`, within a relative `1e-12` of the 80-digit maximum-likelihood
     estimate in `general.json`.
   - **The bracket's upper end.** The same iteration over `[2⁻²⁰, 2⁵⁰]` (`Glm`'s old bound) refused 35 of the 52
     corpus and seeded sets whose root lies inside that bracket: at `2⁵⁰` the score is far below its own rounding, its
     sign is noise, and the bracket check reads it. Over `[2⁻²⁰, 2²⁰]` every answer the reference was run on has its
     root inside the bracket, so no noise at `2²⁰` produced an answer, except where the means reproduce the counts
     (item 4). The old loop answered no θ above `2²⁰` on any design measured.
   - **Iterations and cost.** Where both answer, the bracketed Newton takes the same Newton steps and two more
     evaluations of the score (its two ends), and reads the information at the answer from its last evaluation, where
     the old loop evaluated it once more: a θ solve costs 1.32 times the old loop's on .NET and 1.27 times under node
     (summed over the 43 sets where both answer), and 0.08 times where both refuse, the bracket check refusing after two
     evaluations where the loop ran to its cap. Over whole fits, interleaved runs of each build (medians of three on .NET and six under node; this
     machine's run-to-run spread is about ten per cent a case): IRLS fits (Poisson log and sqrt, binomial logit, Gamma
     inverse, 30 rows) cost 1.02 times what they did on .NET and 0.98 under node, the indirection through
     `Damped.iterate` within the spread; negative binomial fits that answer 1.04 times on .NET and 1.09 under node; fits
     refused at θ 0.76 times on .NET and 0.81 under node. The θ solve's extra evaluations are the method's cost, not the
     indirection's, and are surfaced here rather than hidden: they buy the bracket that item 3 measures.
   - **Option (a) was not taken.** It would add a solver arm — an unbracketed Newton with a caller's reflection —
     whose one reader is θ, to reproduce an iteration the measurement shows refusing ordinary data, and it would
     carry the `2⁵⁰` bound whose upper range the score cannot resolve. Under this side's rule that an abstraction
     needs a reader and the doctrine's preference for an existing seam, the measurement settles it.

4. **An answer on the bracket's end is refused.** The solver answers an end at once when the score there is exactly
   zero. Where the means reproduce the counts (a Poisson fit that is exact: every count equal, the case
   `transform-179-fit-negbin-theta-refused` pins), the score at `2²⁰` rounds to zero, and the solver would answer
   `θ = 2²⁰`; the old loop refused those with a start that is not finite. The sweep met 39 such designs before the
   guard. A θ on an end is no estimate, so it is `ThetaNotConverged`, held by a test.

5. **What moved, and what did not.** No law vector moved: every file under `conformance/laws/` is byte-identical
   (their SHA-256 recorded before the first edit and after the last), including `regression-laws.json` (which holds
   no GLM vector) and the three negative binomial `Fit` rows of `transform-laws.json` (`transform-173-fit-negbin-fixed`,
   `transform-174-fit-negbin-summary`, `transform-179-fit-negbin-theta-refused`), on .NET and under node. `Glm.fit`
   answers the bits it answered before on every case of the reference corpus and the authored cases, under three
   controls: 87 lines, 33 of them refusals, recorded by the pre-swap build. What moved is θ where the old loop
   reflected or overshot: it is estimated on data the loop refused, refused on the two designs above, and answered
   differently in its last bits where the two paths differ. That is a breaking entry in STABILITY.

6. **Not changed here.** θ's stopping rule stays the absolute `2⁻¹³` MASS states, so a small θ is good to a relative
   `1e-2` at worst and a bisection narrower than the tolerance stops where it is (one sweep design). A relative rule
   would move bits the corpus pins; it is a decision of its own. The alternation between the coefficients and θ (glm.nb's
   outer loop) stays as it is: it is not an iteration the solvers offer, and it has one reader.

7. **The reproduction law becomes a record.** D77 item 9's law transcribed what `Glm`'s loops called and drove the
   solvers with it. With the loops gone the transcription would repeat `Glm`'s own calls, so it is deleted
   (`SolversReproductionTests.fs`), and what it established is kept as data: `fixtures/glm-reference/glm-bits.txt`,
   every compared cell's bits, iteration count and refusal of `Glm.fit` over the 29 cases under the three controls,
   written by the build before the swap and compared line for line (`GlmSolversTests.fs`), with a probe that a tighter
   rule moves a recorded line. `proofs.json`'s solvers row says the regression package now rests on the solvers
   rather than being reproduced by them; it carries no GLM row of its own (the shard assumed one).

8. **The version.** `Fuaran.Compute.Regression` references `Fuaran.Compute.Solvers` directly (it did transitively
   through Elementary); no public surface moves (`api/Fuaran.Compute.Regression.txt` and `api/Fuaran.Compute.Solvers.txt`
   unchanged). The θ change is behavioural and breaking, on a draft that already carries breaking moves: it rides
   `0.45.0`.

**Consequences.** A host transcribing `Glm` transcribes two solver calls and their arguments instead of two loops, and
the solvers' own vectors pin the decisions. θ is estimated on small-θ data MASS's iteration gives up on, and a θ the
score cannot resolve is a refusal at the bracket rather than an answer near `2⁵⁰`. A change to θ's stopping rule, or a
relative rule for it, is its own decision and moves the corpus.

## 2026-11-11 — D79: the normal, t, chi-square and F distributions are pinned in a package of their own, `Fuaran.Compute.Distributions`, over Elementary's special functions and the solvers; the normal pair is Elementary's, read under the distribution's name; the t and the F read the incomplete beta function and its inverse and iterate nothing, the chi-square quantile is `Root.bracketedNewton`; each form chosen by measurement against a fifty-digit reference; `Predict` reads `StudentT.quantile` and the t table is gone

**Context.** Phase 618. A confidence interval, a significance test and a model's p-value end in a distribution
function, and this repository had one: the two-sided t quantiles `Predict` read from a table in `DataFrame.fs` (D56). The
phase asks for the cumulative distribution function and the quantile of the standard normal, Student's t, the
chi-square and Fisher's F distributions, pinned as D52 pinned `exp`, and for `Predict` to read the function. The
shard was written before Phases 630, 631, 633, 632 and 621 landed and was read against the tree as they left it:
`Fuaran.Compute.Elementary` exists (D74) and holds `Special` (D78) and the normal pair (D58); the solvers are a package
below it (D77); `Glm` is in `Fuaran.Compute.Regression` (D75). Three of the shard's statements did not hold of that
tree, and item 10 says what each changed.

**Decision.**

1. **Where they live and how they are called.** `Fuaran.Compute.Distributions` is a new package between Elementary
   and the packages above it. It references `Fuaran.Compute.Solvers` and `Fuaran.Compute.Elementary` and nothing
   else of this repository or of the substrate; a boundary test holds its project and its built assembly to that
   set, with a go-red case. It holds one module a family: `Normal`, `StudentT`, `ChiSquare`, `FisherF`. A caller
   opens the namespace and writes `StudentT.quantile`. The operations table is `DistributionsOperations`, named for
   the package as D74 item 2 requires, with its `Exactness` type nested; each of the eight functions has a `Pinned`
   row. No other package declares a module or type of any of the six names: a test reads every source file under
   `src/` for one (which is how it sees an `internal` module, as the dataframe's t table was) and every other
   package's exported types. `Fuaran.Compute.DataFrame` references the package; `Regression` does not yet.

2. **The normal distribution is not written twice.** `Normal.cdf` and `Normal.quantile` ARE `Elementary.normalCdf`
   and `Elementary.normalQuantile` (Phase 600, D58): each is one line that calls the other. The implementation
   stays in Elementary, where `Glm`'s probit link and `Special`'s inverse start read it, so no vector of
   `elementary-laws.json` or `regression-laws.json` moves. A law holds the two spellings bit for bit on both hosts,
   and the suite holds them over a grid of 18,200 points. It is the standard normal only; a location and a scale
   are Phase 629's common surface.

3. **What every function is built from.** `+ − × ÷`, comparisons, the correctly rounded square root, and
   Elementary's pinned `exp`, `ln`, `pow`, `normalQuantile`, `gammaLn`, `beta`, `betaLn`, `regularizedGammaP`,
   `regularizedGammaQ`, `regularizedBeta` and `inverseRegularizedBeta`. No special function is written here, no
   fused multiply-add and no host mathematics call, and the package's source has no loop: a test reads it for one.
   Two private helpers serve the t and the F:
   - `betaNearOne a b y` is `I_{1−y}(a, b)` for a caller that holds `y` and not `1 − y`. The incomplete beta
     function takes its argument as one double, and `1 − y` is not one. It is read at `x = fl(1 − y)`; `u = 1 − x`
     and `d = y − u` are exact; the answer is `I_x − d · xᵃ⁻¹ uᵇ⁻¹ / B(a, b)`, the first-order term through the
     density. This is the correction D78 item 7 makes inside the function for an argument below one half, made
     for the other end. It is used for `y` from `2⁻²⁶`, where the second-order term is below `2⁻⁵⁶` of the answer's
     scale.
   - `betaOdds a b p q qExact` is `x / (1 − x)` at the root of `I_x(a, b) = p`. `inverseRegularizedBeta a b p` is
     read first; a root at or below one half is used as it is. Above one half the function has answered `1 − y`
     for a small `y`, and the odds divide by a `y` rounded to the last place of one. Then the mirrored problem
     `I_y(b, a) = q` is read, whose root is `y` itself, when `q = 1 − p` is exact; when `q` is rounded, only where
     the mirrored density at `y` is above one, which is where the rounding of `q` moves `y` by less than the
     rounding of `1 − y` did.

4. **Student's t.**
   - `cdf ν x`: the tail beyond `|x|` is computed once and is the answer for `x < 0`; for `x ≥ 0` the answer is one
     minus it. So a lower tail keeps its relative accuracy, and `cdf ν (−x) + cdf ν x` is one within a rounding.
     The tail is half of `I_z(ν/2, ½)` at `z = ν / (ν + x²)`, in four arms by `w = x² / (ν + x²) = 1 − z`:
     from `|x| = 2³⁰ √ν`, where `z` is below `2⁻⁶⁰`, the leading term `(√ν / |x|)^ν / (ν B(ν/2, ½))` by the pinned
     `pow` (nothing overflows before the tail does, and the answer does not carry the error of an exponential of a
     large exponent); where `x² (ν + 2) ≤ 3ν` or `w < 2⁻²⁶`, the centre `½ − ½ I_w(½, ν/2)`; where `2x² ≥ ν`,
     `½ I_z(ν/2, ½)` with `z` formed directly; otherwise `½ betaNearOne (ν/2) ½ w`. The centre arm is a matter
     of cost: `betaNearOne` measures the same error there, because the incomplete beta function already reads
     its fraction at the small argument, and pays an exponential, two logarithms and a `betaLn` for it (204
     against 228 ns a value over the sample on .NET, 398 against 469 under node).
   - `quantile ν p`: `0` at one half; above one half the negation of the quantile at `1 − p`, a subtraction that
     is exact, so the function is odd about one half bit for bit. Below one half `t² / ν` is the odds of a root
     of the incomplete beta function, and the two ways of writing it take `2p` and `1 − 2p`. From `p = ¼` both
     are exact and it is `betaOdds ½ (ν/2) (1 − 2p) (2p)`. Below `¼` only `2p` is exact:
     `I_z(ν/2, ½) = 2p` is read, and `t = −√(ν (1 − z) / z)` stands while `z ≤ ½` or `1 − z ≥ 1/16`. For
     `2⁻²⁶ ≤ 1 − z < 1/16` ONE Newton step on the distribution function follows, its residual the tail above and
     its derivative the density `exp (½ (ν + 1) ln z − ½ ln ν − ln B(ν/2, ½))`; below `2⁻²⁶` the other root is
     read. An answer at or beyond `−2³⁰ √ν` is replaced by the leading term solved for `t`,
     `−√ν / (p ν B(ν/2, ½))^(1/ν)`. The Newton step is a fixed correction, not an iteration: it has no stopping
     test for a host to take differently.

5. **Chi-square and F.**
   - `ChiSquare.cdf k x` is `regularizedGammaP (k/2) (x/2)`.
   - `ChiSquare.quantile k p` is twice the root `u` of `P(k/2, u) = p` by `Root.bracketedNewton`. The residual is
     `P − p`, read as `(1 − p) − Q` where `p > ½`; the derivative is the density
     `exp ((a − 1) ln u − u − ln Γ(a))`. The bracket is `[0, a + √(2aL) + L]` with `L = ln 2` where `p ≤ ½` and
     `−ln (1 − p)` above: the gamma distribution's tail beyond `a + √(2at) + t` is at most `e⁻ᵗ`, so the residual at
     the upper end is not negative. The start is the leading term at zero solved for `u`,
     `exp ((ln p + ln Γ(a + 1)) / a)`, where `a < −0.62 ln p` (Best and Roberts' threshold, AS 91); otherwise
     Wilson and Hilferty's `a (1 − 1/(9a) + z √(1/(9a)))³` over the pinned normal quantile; half the bracket where
     that lands outside it. The shrink fraction is one. The rule is `RelativeChange (2⁻⁴⁰, 2⁻¹⁰²²)` with a cap of
     one hundred: stop when a step moved the point by less than `2⁻⁴⁰` of itself. A refusal of the solver is NaN.
     A start at zero that underflows answers `0`: the quantile is below the smallest double.
   - `FisherF.cdf d1 d2 x` is `I_w(d1/2, d2/2)` at `w = d1 x / (d1 x + d2)`: read at `w` where `d1 x ≤ d2`, and
     beyond through `betaNearOne` at `z = d2 / (d1 x + d2)` (as `1 − I_z(d2/2, d1/2)` below `2⁻²⁶`).
   - `FisherF.quantile d1 d2 p` is `(d2 / d1) · betaOdds (d1/2) (d2/2) p (1 − p)` up to one half, and
     `d2 / (d1 · betaOdds (d2/2) (d1/2) (1 − p) p)` above, where `1 − p` is exact.

6. **`Predict` reads the function.** The `StudentT` table, its 366 lines and its interpolation in `1/ν` are
   deleted from `DataFrame.fs`. An interval's quantile for a level `L` is `−StudentT.quantile ν ((1 − L) / 2)` at
   the fit's residual degrees of freedom, and `−Normal.quantile ((1 − L) / 2)` where the family fixes its
   dispersion. The lower tail is read because `(1 − L) / 2` is exact from `L = ½` and `(1 + L) / 2` is not: the
   tail is the level's own. A level is admissible when it is strictly between zero and one; anything else is
   refused as `FitError` naming it (at zero the interval is a point, at one the whole line). The refusal of a
   level the table did not carry is gone. No residual degrees of freedom is still no interval.

7. **The special-value table.** A NaN argument answers NaN everywhere. A parameter (`ν`, `k`, `d1`, `d2`) that is
   not above zero and finite answers NaN. A probability outside `[0, 1]` answers NaN. A quantile at `p = 0` and
   `p = 1` answers the distribution's bounds: `−∞` and `+∞` for the normal and the t, `0` and `+∞` for the
   chi-square and the F. A distribution function at `−∞` answers `0` and at `+∞` answers `1`; the chi-square's and
   the F's answer `0` for every `x ≤ 0`. The shard's "a non-finite argument answers NaN" is read as the
   parameters: an infinite `x` has a limit, `Elementary.normalCdf` and `Special.regularizedGammaP` already answer
   it, and `Normal.cdf` could not answer otherwise without being a second function. The table is code, stated on
   each function, and a law holds every row on both hosts; it is not published as data, for D78 item 9's reason.

8. **The stopping rule, in bits.** One iteration in the package, the chi-square quantile's, and its rule is item
   5's: `|current − previous| / (|current| + 2⁻¹⁰²²) < 2⁻⁴⁰` through `StoppingRule.stops`, a cap of one hundred, a
   function value of exactly zero first. The t's and the F's quantiles are the special function's own roots
   (`inverseRegularizedBeta`, D78 item 8, the same rule) read once or twice, and the t's Newton step is one step.

9. **What was measured** (`benchmarks/results/2026-11-11-ryzen9-9950x-phase-618.md`; the reference is mpmath 1.4.1
   at fifty digits, by `tests/Fuaran.Core.Compute.Tests/DistributionsReference/distributions_reference.py`, which
   imports Phase 631's incomplete gamma and beta references). Over a seeded sample of 11,640 inputs, the same on
   every host, .NET and node answered the same bits on every one, and on the candidates' 13,120 more. Largest
   error in units in the last place by range, and nanoseconds a value:

   | function | small parameters | to `4e4` (the t), `2e5` (χ²) | the t to `1e6` | .NET ns | node ns |
   |---|---:|---:|---:|---:|---:|
   | `Normal.cdf` | 1.9 | | | 24 | 64 |
   | `Normal.quantile` | 3.5 | | | 13 | 28 |
   | `StudentT.cdf` | 560 (`ν < 100`) | 6,095 | 137,400 | 204 | 398 |
   | `StudentT.quantile` | 314 (`ν < 100`) | 2,509 | 24,590 | 2,054 | 3,375 |
   | `ChiSquare.cdf` | 656 (`k < 2e3`) | 63 | | 194 | 268 |
   | `ChiSquare.quantile` | 2,380 (`k < 2e3`) | 0.6 | | 1,666 | 2,259 |
   | `FisherF.cdf` | 1,198 (`d1, d2 < 1e3`) | 15,800 | | 216 | 476 |
   | `FisherF.quantile` | 320 (`d1, d2 < 1e3`) | 3,143 | | 2,536 | 4,407 |

   The 99th percentiles are 81, 92, 208, 348, 364 and 163 in the first column. The bound is not uniform, and three
   things set it, each the special functions' own. The incomplete beta function at one large and one small
   parameter, read within a few `1 / a` of one, is out by an amount that grows with `a` (a relative error of 260
   units of `2⁻⁵²` at `a = 500`, 6,400 at `20,000`, 200,000 at `500,000`): that is the t from `ν = 100` and the F
   with one large degree of freedom, and it leaves a tail at `ν = 1,000` good to fourteen digits and at a million
   to eleven. A tail far out carries about twice its exponent. And a quantile where the mass is at zero carries
   the function's error times `2 / k`. The chi-square quantile's iteration took 3.5 steps on average and 27 at
   most; with Wilson and Hilferty's start alone, or from the middle of the bracket, 147 of the 1,573 runs reached
   the cap.

10. **What the tree and the measurement changed of the shard's plan.**
    - **The t table was not "transcribed at four or five significant figures".** Its entries were the nearest
      doubles to the exact quantiles, and the function answers every one of its 280 within 24 units in the last
      place, 45 of them to the bit (a test holds the distance at 32, with the table kept as its data). So at a tabled `ν` and level an interval's
      bounds move in their last digit or two, by the function's own error against a correctly rounded entry, and
      that is not a gain. What the function gains is everything the table could not say: a `ν` between its rows,
      where it read linearly in `1/ν` and was `5.2e-5` above the quantile at `ν = 35`, a
      `ν` that is not an integer, and any level.
    - **The interval-bearing transform vectors moved by units in the last place, not in the fourth figure.** Every
      one of them has a tabled `ν` and level. Three rows' bounds moved (`transform-142-predict-poisson-response`,
      `transform-143-predict-gamma-link`, `transform-175-predict-logit-response`), by at most seven units; the two
      gaussian interval rows did not move a bit. `transform-145-predict-level-refused` asked for a level of `0.97`,
      which is admissible now: it asks for a level of one, and is still refused as `FitError`. The file's head
      `description` says what the quantile is. Every other row is the line it was.
    - **The normal distribution was already pinned** (D58), so it is read, not built (item 2). The package's
      first families are four modules and six new functions.
    - **`Special` publishes no complement.** The t's lower tail and a small F probability need
      `I_{1−y}(a, b)` for a small `y` the caller holds, and a quantile needs the inverse's `1 − x`. The function
      computes both inside itself and publishes neither, so the package restores what the public argument rounds
      away (`betaNearOne`), chooses between two inverses by the density (`betaOdds`), and takes one Newton step
      where neither inverse is good (the t at a large `ν` in its tail). Each is measured against the form it
      replaced (the candidates of the measurement): reading the function at `ν / (ν + x²)` directly is `1e8` to
      `1e10` units out, splitting at the centre without the correction `1e15`, one inverse alone `1e5` to `1e15`,
      two inverses without the step `1e7` to `1e9`.
    - **The far tail is the leading term.** The shard did not ask for it; the vectors did. `ν / x²` underflows
      long before a Cauchy tail does, and the function's own prefix is two hundred units out at `z = 1e-301`.
    - **The dependency is Elementary and Solvers**, as the shard's Goal says and its Acceptance line does not.

**Falsifiers.** Each is a way this decision would be wrong, and what would show it.

- A host answers other bits: the 215 vectors of `conformance/laws/distributions-laws.json` are rendered on .NET
  and under node and compared byte for byte, and each of the 143 the routines computed carries its distance from
  the reference, which a host must reproduce too.
- A stated bound is exceeded: the suite holds every referenced vector to its function's bound
  (`DistributionsVectors.referenceUlps`, one figure for parameters to forty thousand and one beyond, each the
  power of two at or above its worst vector), and `DistributionsLaws.laws` holds, on both hosts and for parameters
  from one quarter to 512: the distribution function of each quantile is `p` within `2⁻⁴⁴`;
  `FisherF.cdf 1 ν (t²) = 2 StudentT.cdf ν |t| − 1` within `2⁻⁴⁴`; `StudentT.cdf ν (−t) + StudentT.cdf ν t = 1`
  within `2⁻⁵²`; the t's quantile above one half is the negation of the one below, bit for bit; and no distribution
  function falls as its argument rises beyond `2⁻⁴⁴` of itself.
- The normal pair drifts from Elementary's: the law and the grid test of item 2.
- The accuracy is worse somewhere the sample did not reach: it is a measurement over the stated ranges, never a
  theorem. A `ν`, `d1` or `d2` above a million, or a `k` above two hundred thousand, is computed by the same rules
  and is not covered by the figures; where a special function's series does not settle (a `k` beyond about `10⁸`)
  the answer is NaN, never an unconverged value.
- `Special.regularizedBeta` changes where it reads its fraction: the centre arm of the t and the choice in
  `betaOdds` were measured against the function as D78 left it. The referenced vectors on either side of each
  arm's boundary would move, and the bound test would say so.
- `Special` publishes the complement and its inverse: then `betaNearOne`, the density test of `betaOdds` and the
  t's Newton step are each a longer way round than a direct read, and the figures of item 9 from `ν = 100` are
  what a direct read would be measured against.

**Alternatives considered.** Moving the normal pair into this package and leaving Elementary's names as readers:
rejected, because Elementary cannot reference a package above it and `Special` reads the quantile. Keeping the
table's rows as a fast path at the tabled `ν`: rejected, because two sources for one quantile is what the phase
removes, and the difference is the function's last digit. A Cornish–Fisher expansion for the t at a large `ν`:
not built; it is a second algorithm with a boundary of its own, and the measured loss there is the special
function's, which is where it would be repaired. Adding the complement to `Special` in this phase: out of the
phase's region, and it would move `elementary-laws.json`, which this phase was told to leave byte-identical.
Publishing the densities: Phase 629's.

## 2026-11-11 — D78: the special functions are pinned in `Special`, a module of `Fuaran.Compute.Elementary` of its own — `gammaLn`, `gamma`, `betaLn`, `beta`, `regularizedGammaP`, `regularizedGammaQ`, `regularizedBeta`, `inverseRegularizedBeta`, `polygamma`, `digamma` and `trigamma`; each algorithm, kernel, threshold and stopping rule chosen by measurement against a fifty-digit reference; every series stops on the solvers' `StoppingRule` and the inverse is `Root.bracketedNewton`; the accuracy is a measurement, stated by range

**Context.** Phase 631. The distributions, likelihoods, tests and intervals above this package reduce to a small set of
special functions, and none was pinned. The phase asks for them as a public module beside `Elementary`'s `exp`, `ln`
and `pow`, measured first as D52 measured those. The shard was written before Phases 630, 621 and 633 landed and was
read against the tree as they left it: the package exists (D74), its table module is `ElementaryOperations`, and the
solvers are below it (D77). Nothing in the repository declared any of these functions. What the measurement changed
of the shard's plan is item 11.

**Decision.**

1. **Where they live and how they are called.** `Special` is a module of `Fuaran.Compute.Elementary`, in its own file
   after `Elementary.fs`. A caller opens the namespace and writes `Special.gammaLn`. No other package declares a
   module or type of that name (a test reads every source file for one), so the name cannot be shadowed the way D74
   item 2 found `Operations` could. Each function has a `Pinned` row in `ElementaryOperations.exactness`. The next
   special function (the error function's inverse, the inverse incomplete gamma) lands in this module.

2. **What every function is built from.** `+ − × ÷`, comparisons, the correctly rounded square root, and this
   package's own `exp`, `ln`, `pow` and `normalQuantile`; no fused multiply-add and no host mathematics call. Four
   helpers are fixed-length and private: `ln (1 + m)` and `m − ln (1 + m)` within `[−½, ½]` as eighteen terms of
   `atanh`'s series in `m / (2 + m)`; `exp y − 1` within `[−½, ½]` as thirteen Taylor terms; and `sin πx` and
   `cos πx` by splitting `x` exactly into its nearest integer and a remainder, then a Taylor polynomial to `y¹⁹` at
   `πr` or at `π(½ − |r|)`, so a value near a zero keeps its relative accuracy. Two of `Elementary`'s private
   helpers (`twoSum`, `twoProd`) and its integer tests are `internal` now, so `Special` reads them and no copy exists.

3. **`gammaLn` and `gamma`: one kernel, Stirling's series from `10`.**
   - The kernel is a degree-17 Chebyshev fit of `ln Γ(2 + u) / (u (1 + u))` over `u` in `[−½, ½]`, evaluated by
     Horner's rule and multiplied by `u (1 + u)`. The two zeros of `ln Γ`, at one and two, are those factors, and the
     reduced argument `u` is formed exactly (`x − 2`, `x − 1` or `x` itself), so the answer keeps its relative
     accuracy at both zeros.
   - `gammaLn`, `x > 0`: from `2⁵⁴`, `x (ln x − 1)`; from `10`, `(x − ½)(ln x − 1) + (½ ln 2π − ½ + tail)` with the
     tail eight terms of Stirling's series in `1 / x`; in `(2.5, 10)`, the kernel at `x − k` plus `ln` of the product
     `(x − 1) … (x − k)`, `k = ⌊x − 1.5⌋`; in `[1.5, 2.5]`, the kernel; in `[0.5, 1.5)`, the kernel at `x − 1` minus
     `ln x`; below `0.5`, the kernel at `x` minus `ln (x (1 + x))`. Below zero: the last form down to `−½`
     (`ln (−x (1 + x))`), and the reflection `ln (π / |x sin πx|) − ln Γ(−x)` beyond. The shard's "reflection below
     ½" is not used on the positive axis: the recurrence is more accurate there and needs no sine.
   - `gamma`: an integer from `1` to `23` is the exact product; from `10`, `√(2π) · (v e⁻ˣ) · v · exp (tail)` with
     `v = pow x (x/2 − ¼)`, the power in two halves so it does not overflow before the function does; below `10`,
     `exp` of the kernel times the same product, or divided by `x` or `x (1 + x)`; below `−½`, the reflection
     `−π / (x sin πx) / Γ(−x)`.

4. **`digamma` and `polygamma`.**
   - `digamma`, `x > 0`: from `10`, `ln x − 1/(2x)` minus seven Bernoulli terms. In `[1, 2)`, a kernel: a degree-23
     Chebyshev fit of `ψ(x) / (x − x₀)` over `x − 1.5`, times `x − x₀` with the root `x₀ ≈ 1.4616` held as a double
     and its remainder, so the answer keeps its relative accuracy at the root. In `[2, 10)`, the kernel at
     `x − k` plus `Σ 1 / (x − j)`; below one, the kernel at `x + 1` (its argument formed exactly) minus `1 / x`. Below
     zero, the reflection `ψ(1 − x) − π cos πx / sin πx`.
   - `polygamma n x`, `n ≥ 1`, `x > 0`: the recurrence shifts `x` up by ones until it is at least `10 + n`, adding
     `1 / (x + i)ⁿ⁺¹` (each power `n` products, left to right); there the asymptotic series
     `(n − 1)! / xⁿ · (1 + n / (2x) + Σ_{k=1..10} B₂ₖ C(2k + n − 1, 2k) / x²ᵏ)`, the binomial a running product; the
     sign is `(−1)ⁿ⁺¹`. `n = 0` is `digamma`, and `trigamma` is `polygamma 1`. The order is a float, because a
     negative or non-integer order is a row of the table.

5. **`betaLn` and `beta`.** The arguments are ordered first, so both are symmetric bit for bit. `betaLn`: both at
   least `10`, `−½ ln b + ½ ln 2π + (tail a + (tail b − tail (a + b))) + (a − ½) ln r + b ln1p (−r)` with
   `r = a / (a + b)`; one at least `10`, `ln Γ(a) + ((tail b − tail (a + b)) + (a − a ln (a + b)) + (b − ½) ln1p (−r))`;
   both below, `ln Γ(a) + ln Γ(b) − ln Γ(a + b)`. `beta`: `Γ(a) / Γ(a + b) · Γ(b)` where `a + b ≤ 170`, `exp (betaLn)`
   beyond. In both small arms the sum `a + b` is taken with its rounding error `e` (`twoSum`) and the gamma function
   of the sum corrected to first order through `digamma` (`ln Γ` gains `e ψ(s)`, `Γ` the factor `1 + e ψ(s)`): without
   it the rounding of the sum alone costs up to a hundred units.

6. **`regularizedGammaP` and `regularizedGammaQ`.** The prefix `xᵃ e⁻ˣ / Γ(a)` is `pow x a · exp (−x) / Γ(a)` below
   `a = 10` and `√(a / 2π) exp (−a (μ − ln (1 + μ)) − tail a)` with `μ = (x − a) / a` from it. The boundary is
   `x = a + 1`, or `x = 1` where `a < 1`. Below it `P` is the series `Σ xⁿ / (a (a + 1) … (a + n))` times the prefix;
   from it `Q` is Legendre's continued fraction by the modified Lentz method (denominators held to `10⁻³⁰⁰`) times the
   prefix. The other of the pair is the complement, except `Q` for `a < 1` below the boundary, which is its own
   series, `−expm1 L − exp L · a · Σ_{n ≥ 1} (−x)ⁿ / ((a + n) n!)` with `L = a ln x − ln Γ(1 + a)`: `1 − P` there loses
   everything as `a` falls to zero.

7. **`regularizedBeta`.** The prefix `xᵃ (1 − x)ᵇ / B(a, b)` is, for both parameters at least `10`,
   `√(ab / (2π (a + b))) exp (−a φ(n / a) − b φ(−n / b) − tails)` with `φ(m) = m − ln (1 + m)` and
   `n = x (a + b) − a` formed in double-double arithmetic; otherwise `exp (a ln x + b ln (1 − x) − betaLn a b)`, each
   logarithm of the smaller of `x` and `1 − x` taken directly and of the larger through `ln1p`. The function is the
   continued fraction (Abramowitz and Stegun 26.5.8, modified Lentz, two partial quotients an iteration) at `x` where
   `x ≤ (a + 1) / (a + b + 2)`, and `1 − I_{1−x}(b, a)` beyond. Two corrections the measurement asked for:
   - `1 − x` is exact only from `x = ½`. What the subtraction rounded away is restored to first order through the
     density, `(1 − (1 − x) − x) · prefix / (x (1 − x))`; without it a `b` of ten thousand multiplied that rounding
     into thousands of units.
   - where `b < 1` the function is small until `x` is very near one, and the complement of a value near one has no
     relative accuracy. There (the complement below `1 − x`, and `x ≤ 0.99`) it is Gauss's series
     `Σ (a + b)ₙ / (a + 1)ₙ xⁿ` times `prefix / a`, every term positive. The series loses about `3 / (1 − x)` units
     to its own length, which is why it is not used wherever the complement is larger than `1 − x`, and why beyond
     `0.99` the function stays the complement, accurate to a unit in the last place of one.

8. **`inverseRegularizedBeta`.** The residual `I_x(a, b) − p` (read as `(1 − p) − (1 − I_x)` where `p > ½`) is taken at
   `x = ½`. Not negative, the root is in the lower half; negative, the mirrored problem `I_y(b, a) = 1 − p` is solved
   for `y = 1 − x`. Either way the solver works on the small one of `x` and `1 − x`. The start: for both parameters at
   least one, Abramowitz and Stegun 26.5.22 over the pinned `normalQuantile`; otherwise, and where that lands outside
   `(0, 1)`, the leading term of the function at the nearer end solved for `x` by `pow`; a start at or above `½` is
   replaced by `¼`. Then `Root.bracketedNewton` over `[0, 1]` with the density for a derivative and a shrink fraction
   of one. The bracket is the whole interval: over `[0, ½]` a root within rounding of `½` sent a Newton step past the
   end, and the bisection that replaced it stopped the rule sixty units short.

9. **The stopping rules, in bits, and the special-value table.**
   - Every series and continued fraction (five loops) stops on `StoppingTest.RelativeChange (2⁻⁵², 2⁻¹⁰²²)` read
     through `StoppingRule.stops`: `|current − previous| / (|current| + 2⁻¹⁰²²) < 2⁻⁵²`, `current` the running sum or
     fraction after the term. The cap is `65536`; a loop that reaches it answers NaN. Inside the measured range the
     longest is the lower gamma series at `x ≈ a`, about `8.6 √a` terms (7,600 at `a = 10⁶`), so the cap is reached
     from about `a = 5 × 10⁷`.
   - The inverse's rule is `RelativeChange (2⁻⁴⁰, 2⁻¹⁰²²)` with a cap of one hundred; a refusal of the solver (the
     cap, or a fraction that answered NaN) is NaN.
   - The fixed-length helpers of item 2, the polynomial kernels, Stirling's and the asymptotic series, and the
     polygamma shift (`⌈10 + n − x⌉` steps) take no rule: their length is a constant or a function of the arguments
     alone, so no host can stop them elsewhere.
   - The table: a NaN argument answers NaN everywhere. `gammaLn`: `+∞` at `±∞`, at zero and at the negative integers.
     `gamma`: `+∞` at `+∞` and above `171.62437695630272`; NaN at `−∞`, zero and the negative integers. `betaLn`,
     `beta`: NaN unless both arguments are above zero and finite. `P`, `Q`: NaN unless `a` is above zero and finite
     and `x ≥ 0`; `P(a, 0) = 0`, `P(a, +∞) = 1`, `Q` their complements. `regularizedBeta`: NaN unless the parameters
     are above zero and finite and `x` is in `[0, 1]`; `I_0 = 0`, `I_1 = 1`. The inverse: the same refusals over `p`;
     `0` at `p = 0`, `1` at `p = 1`, and `0` or `1` where the start itself underflows to that end. `digamma`: `+∞` at
     `+∞`; NaN at `−∞`, zero and the negative integers. `polygamma`: NaN for an order that is negative, not an
     integer or above `170` (where `n!` is not a finite double), and for `n ≥ 1` at any `x ≤ 0`; `0` for `n ≥ 1` at
     `+∞`. The tables are code and are stated here and on each function; they are not published as data the way
     `expTable` is, because nothing reads them yet (the phase that makes these column functions publishes them).

10. **What was measured** (`benchmarks/results/2026-11-11-ryzen9-9950x-phase-631.md`; the reference is mpmath 1.4.1
    at fifty digits, by `tests/Fuaran.Core.Compute.Tests/SpecialReference/special_reference.py`, which also generates
    the two kernels' coefficients). Over a seeded sample of 29,563 inputs, the same on every host, .NET and node
    answered the same bits on every one. Largest error in units in the last place, and nanoseconds a value on .NET
    and under node:

    | function | largest error | where `1e-10 ≤ |value| ≤ 1e10` | .NET ns | node ns |
    |---|---:|---:|---:|---:|
    | `gammaLn`, `x > 0` | 4.2 | 4.2 | 26 | 80 |
    | `gamma` | 5.0 | 3.4 | 84 | 185 |
    | `betaLn` | 5.2 at the 99th percentile | (see below) | 77 | 244 |
    | `beta` | 666 | 34 | 123 | 331 |
    | `regularizedGammaP` | 1135 | 93 | 238 | 373 |
    | `regularizedGammaQ` | 1544 | 53 | 185 | 377 |
    | `regularizedBeta` | 402 | 182 | 229 | 353 |
    | `inverseRegularizedBeta` | 477 | 117 | 1633 | 2760 |
    | `digamma`, `x > 0` | 2.2 | 2.2 | 26 | 70 |
    | `trigamma` | 2.7 | 2.7 | 16 | 42 |
    | `polygamma`, orders 2 to 20 | 16 | 8.9 | 31 | 61 |

    The bound is not uniform, and four things set it. A function that crosses zero by cancellation has no bound in
    units of its own last place there: `gammaLn` and `digamma` below zero (195 and 2,492 in the sample) and `betaLn`
    where `B` is one (2,024); their absolute error is a few units in the last place of the cancelling terms. An
    answer that is `exp` of an exponent of magnitude `E` carries about `2E` units: `beta` beyond `a + b = 170` and the
    incomplete functions' deep tails. A long series carries the rounding of its recurrence: `P` at `x ≈ a` is 40 units
    out at `a = 10⁵` and 182 at `a = 10⁶`. And the inverse inherits the function's error divided by its conditioning,
    about `1 / a` for a small root. The polygamma orders above `20` follow the same rule and were not measured.

11. **What the measurement changed of the shard's plan.**
    - `digamma` is not "one algorithm for every order": shifting up to the asymptotic series, as the higher orders
      do, measured 111 units away from the root and has no bound at it. The kernel measures 2.
    - `gamma` is not `exp (gammaLn x)`: that measured 1,160 units. Stirling's formula over the pinned `pow` and the
      kernel's product measure 5.
    - `gammaLn` takes no reflection below `½`, only below zero (item 3).
    - `regularizedBeta` gained the two corrections of item 7 and the inverse its two halves (item 8); the incomplete
      gamma prefix is not `exp (a ln x − x − gammaLn a)`, which measured a million units at `a` near `10⁵`.
    - Lanczos' approximation was a candidate for `gammaLn` and loses both zeros (`10¹⁶` units beside them).

**Falsifiers.** Each is a way this decision would be wrong, and what would show it.

- A host answers other bits: the vectors of `conformance/laws/elementary-laws.json` (251 of them for these functions,
  appended after the vectors that were there, which are unchanged) are rendered on .NET and under node and compared
  byte for byte, and each carries the function's distance from its reference, which a host must reproduce too.
- A stated bound is exceeded: the suite holds every referenced vector to its function's bound
  (`SpecialVectors.referenceUlps`), and `ElementaryLaws.laws` holds the identities on both hosts, each within a bound
  written in units of `2⁻⁵³`: `Γ(x + 1) = x Γ(x)`; `P + Q = 1`; `I_x(a, b) + I_{1−x}(b, a) = 1`; `I` of its inverse is
  `p` within `2⁻⁴⁰`; `P` and `I_x` do not fall as `x` rises; `ψ(x + 1) = ψ(x) + 1 / x`; `ψ(1) = −γ` and
  `ψ′(1) = π² / 6` within one unit; a central difference of `gammaLn` against `digamma` within `2⁻²⁶`. `B`, `ln B`, and
  `digamma` and `trigamma` against `polygamma`, are held bit for bit.
- The accuracy is worse somewhere the sample did not reach: it is a measurement over the stated ranges, never a
  theorem. An input outside them (an order above `20`, an `a` above `3 × 10⁵`, parameters of the incomplete beta
  above `2 × 10⁴`) is computed by the same rules and is not covered by the figures.
- A cap is reached inside a range a consumer needs: the function answers NaN there, never an unconverged value, so
  the failure is visible. Temme's uniform expansion is what would remove the long series at large `a`; it is not
  shipped, and the cost recorded for `a = 10⁶` (17 µs a value) is why it has not been needed.

**Alternatives considered.** Publishing each function's special-value table as data, as `expTable` is: deferred to the
phase that reads them (item 9). A double-double exponent for the deep tails: not built, because it is a cost on every
call for the sake of values below `10⁻²⁰⁰`; the four-figure maxima are what it would remove. Kahan summation for the
long gamma series: measured, and it does not help, because the error is in the term recurrence and not in the sum.

## 2026-11-11 — D77: pinned solvers are a package of their own, `Fuaran.Compute.Solvers`, at the bottom of Compute's graph, generic over the caller's functions; one `StoppingRule` every method takes, each test one written comparison; the bracketed Newton's fallback, the damped iteration's rise test and each method's order and refusals; Brent's method is not shipped; the solvers reproduce `Glm`'s loops bit for bit, and θ's Newton is not a bracketed Newton

**Context.** Phase 633. Each iterative method in Compute carries its own loop and its own stopping decision, and in a
pinned library the stopping decision is part of the answer: two hosts that stop one iteration apart differ in every bit
after. The phase asks for one library those methods call. The shard was written before Phases 630, 632 and 621 landed;
it was read against the tree as they left it (`Glm` in `Fuaran.Compute.Regression`), and what did not hold is item 9.

**Decision.**

1. **The package and where it sits.** `Fuaran.Compute.Solvers` references FSharp.Core and nothing else: no substrate
   package and no package of this repository. A method takes the caller's function, its derivative where it uses one,
   and for a multivariate iteration the caller's whole step, so no method names a matrix. `Fuaran.Compute.Elementary`
   references it (the special functions' inverses will call it); nothing in Elementary reads it yet. A solver's own
   arithmetic is `+ − × ÷` and comparisons in one written order, with no fused multiply-add (D48) and no host
   mathematics call: an absolute value is a comparison and a negation, and finiteness is `v − v = 0`.

2. **`StoppingRule` is a cap and one test from a closed set, and each test is one comparison.**
   - `AbsoluteStep tol`: stop when `|step| ≤ tol`. Not strict.
   - `RelativeChange (tol, floor)`: stop when `|current − previous| / (|current| + floor) < tol`. Strict. `Glm`'s rule is
     this arm with the floor `0.1`.
   - `BracketUlps ulps`: stop when `hi − lo ≤ ulps × 2⁻⁵² × max(|lo|, |hi|)`, the product formed left to right. A unit is
     measured at the top of the larger end's binade, so the test reads no bit pattern and needs no host function.
   - `ExactZero`: stop only on a function value of exactly zero.

   A rule is valid when its cap is at least one and its tolerance, floor and ulp count are finite and not negative, the
   floor above zero; anything else is `RuleInvalid`. Each method says what `previous`, `current` and `step` are. An
   answer names the arm that stopped it (`StoppingArm`) and the iteration. A run that reaches its cap is the refusal
   `NotConverged`, carrying the last state with the arm `Cap`: it is never returned as an answer. Every method tests
   convergence BEFORE the cap, so a run that converges at its cap converges.

3. **`Root.bisect`.** `f lo` is read before `f hi`. Checked in this order: the rule, the ends (finite, `lo < hi`), the
   two values (finite), a zero at `lo` then at `hi` (answered with zero iterations), a sign change (`BracketInvalid`
   otherwise). Iteration `k` evaluates `mid = lo + 0.5 × (hi − lo)` and replaces the end whose value shares its sign. It
   stops on `f mid = 0` (`ExactZero`, whatever the rule), then the rule's test with `previous` the last midpoint (`lo`
   before the first), `current = mid` and `step = mid − previous`, then the cap. A bracket with no float strictly inside
   stops as `Bracket` without evaluating. A value that is not finite is `NotFinite mid`.

4. **`Root.bracketedNewton`, and its fallback rule.** The caller passes one function answering `(f x, f′ x)`, a start
   strictly inside the bracket, and a fraction `shrink` in `(0, 1]`. The ends are checked as bisection's, then the
   bracket is narrowed by the start. Iteration `k`, with `w = hi − lo`: the proposal is Newton's `x − f/f′` when Newton
   is allowed and the point lies inside the bracket, ends included; otherwise the midpoint. It is evaluated, the end
   sharing its sign is replaced, and Newton is allowed in the next iteration when the narrowed bracket is at most
   `shrink × w` wide. The rule reads `step` as `f/f′` for a Newton proposal and `x − midpoint` for a bisection.
   - The ends are included because a converged Newton step leaves `x` where it is, and `x` is by then an end. Excluding
     them replaced the last Newton step of a converged run with a bisection.
   - The fraction is the caller's because the two readers differ. A quantile wants a guaranteed rate (`0.5`: a bracket
     that did not halve is bisected next). A caller reproducing an existing Newton iteration states `1`, at which every
     narrowing passes and the only fallback is a point outside the bracket.
   - A step-size safeguard (bisect when a step is more than half the last) was considered and not taken: Newton from
     far below a root takes growing steps, and such a rule would bisect a run that was converging.

5. **`Root.integerSearch`.** The smallest integer `k` in `[lo, hi]` with `g k ≥ target`, for a `g` that does not fall.
   `g hi` is read first and `g lo` second; the bracket is then halved at `lo + (hi − lo) / 2`, a value that is not
   `≥ target` (a NaN included) moving `lo`. It takes no rule: it ends when `hi − lo = 1`, within thirty-one halvings.
   `lo > hi`, a width beyond a 32-bit integer and `g hi ≥ target` not holding are `BracketInvalid`.

6. **`Minimise.goldenSection`.** The interior points are `lo + 0.3819660112501051 × (hi − lo)` and
   `lo + 0.6180339887498949 × (hi − lo)`, the constants literals (`0x3FD8722191A02D60`, `0x3FE3C6EF372FE950`), so no host
   takes a square root. The left value is read first. `f c < f d`, strictly, keeps the left part; a tie keeps the
   right. One evaluation an iteration. The estimate is the interior point with the smaller value, the left on a tie. It
   stops when a new point is not strictly between its neighbours (`Bracket`), then on a value of zero under
   `ExactZero`, then the rule's test with `step` the bracket's width, then the cap.

7. **`Damped.iterate`.** The caller gives a step (which may refuse: `StepRefused` with the caller's reason), an
   admissibility test answering the caller's reason, an objective, a midpoint and a count of halvings. A candidate
   fails when it is inadmissible, when its objective is not finite, or when its objective rose. A RISE is the rule's
   own test over the signed change (`StoppingRule.rises`): under `RelativeChange`,
   `not ((current − previous) / (|current| + floor) < tol)`. That is `Glm`'s rule, and its reason holds generally: a
   rise smaller than the stopping test can see is rounding at the optimum, which no halving removes. A failing
   candidate is replaced by the midpoint towards the standing state, at most the stated number of times, then
   `HalvingExhausted`, carrying the caller's reason when the last candidate was inadmissible. A problem may declare
   its start a SEED: the first proposal is then not halved and its objective is not held to the seed's, and an
   inadmissible first proposal is refused at once. `Glm` starts from a seed: `μ₀` is the start, not a point of the model.
   `BracketUlps` reads no objective and is `RuleInvalid` here.

8. **Brent's method is not shipped.** The shard left it to a measurement. The measurement is a census of readers, not
   a timing: every root the filed phases solve has a derivative to hand (a quantile's is its density, the inverse
   incomplete beta's is the beta density, θ's score has its information), and the one-dimensional minimisation is
   golden section's. No reader lacks a derivative. Under this side's rule that an abstraction needs a reader, Brent
   waits for the first caller that has none; `SolversOperations.exactnessOf "Root.brent"` is `None`, held by a test.

9. **The reproduction law, and what the premise check found.**
   - `Glm`'s IRLS is the shape the shard names. Its stopping test is `|dev − dev_prev| / (|dev| + 0.1) < Tol`, strict,
     read before the cap `iteration ≥ MaxIter`; it halves at most ten times on a mean out of range, a deviance that is
     not finite or a rise as item 7 states; the first iteration is not halved. `Damped.iterate` given those answers
     `Glm.fit`'s coefficients, means, linear predictor, deviance and iteration count bit for bit, and its refusals, on
     all 22 cases of the reference corpus (`fixtures/glm-reference/general.json`) under three controls and on authored
     cases that halve and that are refused.
   - **θ's Newton is NOT a bracketed Newton.** `thetaMl` is an unbracketed Newton that reflects a negative θ
     (`θ ← |θ| + score/info`), stops on `|step| ≤ 2⁻¹³` and takes at most `MaxIter − 1` steps. `Root.bracketedNewton`
     with `f` the score, `f′` minus the information, `shrink = 1` and the rule `AbsoluteStep 2⁻¹³` under a cap of
     `MaxIter − 1` computes the same step (`f/f′` is `score/info` negated exactly) and answers the same θ, standard
     error and alternation count bit for bit on every estimated case, while θ stays inside the bracket. Where the
     hand-written loop would reflect a negative θ the solver bisects instead, so the two differ there. No corpus case
     reaches that. Phase 634 decides whether that difference is admitted when it makes the call.
   - The law lives in the test project and transcribes what `Glm`'s private loops call, so no Regression file moved.
     It compares at the level of `Glm.fit`. The corpus's `Fit` vectors reach `Glm.fit` through the dataframe bridge and
     are not driven through the solvers here; they are pinned by `transform-laws.json` itself, which goes red if
     Phase 634 moves a bit of them.
   - The halving refusal (`HalvingExhausted`) is held by the solver's own vectors and laws. No authored fit reaches
     `Glm`'s exhausted halving, so that one path is not compared against `Glm`.

10. **The roster and the names.** The package is registered in the solution, `pack.ps1`, the publish workflow,
    `gate-reads.json`, the boundary, wire and coverage rosters and its own API baseline. A boundary rule holds the bottom:
    a project or assembly reference from the solvers to any `Fuaran.*` package fails the suite. The exactness table is
    the module `SolversOperations` in `SolversOperations.fs` (the shard said `Operations.fs`; two opened modules named
    `Operations` shadow each other, D74 item 2), and `Exactness` is nested in it. Callers open the namespace and write
    `Root.bisect`, `Damped.iterate`, `StoppingRule.create`. A test reads every other Compute package's top-level names
    and finds none that the solvers' names would shadow.

11. **The version.** Additive, on a draft that already carries breaking moves: it rides `0.45.0`.

**Consequences.** A twin transcribes each solver once and a reviewer checks each stopping rule once. A method added
here needs a reader, a vector for each decision it makes and a row in the exactness table. The solvers pin decisions,
not convergence: how fast a method converges on a caller's function, and how close it ends, are the caller's to measure
(`proofs.json`, `solvers-decisions-pinned-by-transcription`). Method cards for the solvers wait on Phase 623.

## 2026-11-11 — D76: a pinned SplitMix64 generator, `Rng`, in the Elementary package, computed in 16-bit limbs; `KMeans` and `Bootstrap` are pipeline steps seeded from it, every order pinned; a seed is required and travels as digits; the steps are planner barriers, re-evaluated whole by the incremental seam and held by tests rather than by the model

**Context.** Phase 621. A segmentation is a decision, and nothing here could replay one: a clustering or a resample
drew from a host generator, so one spec over one table answered differently on another day. The phase asks for one
pinned generator and two steps seeded from it, so that a pipeline, a seed and the data reproduce the answer bit for bit
on any host. The shard was written before Phases 630 and 632; it was read against the tree as they left it, and the
premises that did not hold are recorded in item 14.

**Decision.**

1. **The generator is SplitMix64** (Steele, Lea and Flood 2014): the state advances by `0x9E3779B97F4A7C15`, and a draw
   is the state through two xor-shift-multiply rounds (`0xBF58476D1CE4E5B9`, `0x94D049BB133111EB`) and a final
   xor-shift. It is `Rng` in `Fuaran.Compute.Elementary` (`Rng.fs`), the bottom of the graph, so the distributions'
   sampling and these steps read one generator. `Rng` is a module; its one type, the stream, is nested in it
   (`Rng.Stream`), so `open Fuaran.Compute.Elementary` brings in the module's name and nothing else. No other Compute
   package declares `Rng` (checked against `src/` and the `api/` baselines).
2. **Every step is computed over four 16-bit limbs.** The addition carries between limbs in `int`s. A limb product is
   below `2³²` and a column of them below `2³⁵`; both are formed in floats, where every integer below `2⁵³` is exact, and
   the carry out of a column is the quotient by `2¹⁶`, truncated. The shifts and exclusive-ors act on limbs. A draw
   therefore forms no 64-bit integer and no 32-bit product on any host.
3. **The measurement the shard asked for.** Fuaran.Core's Phase 495 measured a `uint32` multiply collapsing under Fable
   (1579 distinct draws on .NET against 6 under node). The `uint64` product does NOT collapse: Fable carries a 64-bit
   integer as an arbitrary-precision integer wrapped to 64 bits, and `ElementaryLaws.laws` holds the limb product equal
   to the host's own `uint64` product over drawn operand pairs, and eight draws from a drawn state equal to the
   recurrence written in the host's `uint64`, on .NET and under node, at every iteration. The probe in 495's shape is a
   vector member: `distinct`, the number of distinct draws among a stream's first two thousand, is 2000 on both hosts at
   all three seeds. The limbs are kept all the same, for two reasons that do not depend on the measurement: a draw
   allocates no arbitrary-precision integer under JavaScript (D73 records what those cost per row), and a host with no
   64-bit integer at all transcribes the routine as written. `Rng.next` alone assembles a `uint64`, for a caller that
   wants the draw whole.
4. **A seed's streams are named by an index.** `Rng.ofSeed seed stream` starts at the state
   `mix(seed + (stream + 1) · γ)`, the `(stream + 1)`-th draw of the generator whose state is `seed`, by one limb
   product. Every stream of a seed therefore starts from a mixed state, and stream `k` costs what stream `0` does. The
   streams of one seed share the increment `γ`, so they are offsets into one cycle of length `2⁶⁴`; two of them overlap
   only if their starting states lie within the number of draws taken, which for `d` draws over `s` streams has
   probability about `s² · d / 2⁶⁴`. A negative stream index is refused.
5. **`uniform53` is the draw's top 53 bits times `2⁻⁵³`.** **`below n` is by rejection:** the top 32 bits `x` of a draw
   are redrawn while `x < 2³² mod n`, and the answer is `x mod n`, so every value in `0 .. n − 1` is equally likely.
   Both are float operations on integers below `2³³`. `n` is an `int`, so at most `2³¹ − 1`.
6. **Fuaran.Core's `Xorshift32` is not this kernel, and the two coexist.** That one is internal to `Fuaran.Core.Text`
   and seeds the conformance fuzzer; its 32 bits of state are too few to resample a million rows over a thousand
   replicates. This one is a published operation whose bytes a decision record cites.
7. **Both steps are `Transform` cases in `Fit`'s class**: a per-group step whose spec is data and whose table is chosen
   by an `Output` case, wire `$type` `kmeans` and `bootstrap`. Group `g`, in first-appearance order, draws stream `g`
   of the step's seed. Their types stand BEFORE `FitSpec` in the source, so a record written or read by a field name
   they share with it (`GroupBy`, `Output`) still infers as the fit's, as it did before they existed.
8. **A seed is required and is a `Slot<int64>`.** There is no default, so a run is replayable by construction. `Cell`
   has no 64-bit integer (`Int` is 32 bits), so a PARAM binds an int cell, read as its 64-bit value, and a LITERAL
   carries all 64 bits. On the wire a literal seed is its decimal digits in a JSON string (`"seed":"42"`): a JSON number
   past `2⁵³` is not the same integer on a host whose numbers are doubles. The form is canonical: an optional `-`, no
   leading zero, no `-0`, inside the signed 64-bit range; anything else is refused at decode. The digits are read one by
   one, so no host's number parser or culture is consulted.
9. **`KMeans`, in pinned order.** Each feature is a named `ColExpr` of int, float or decimal type, read as its float; a
   null or non-finite feature is refused by name, naming the row. With `Standardise`, each feature is centred and scaled
   within its group, `(x − mean) / sd` with `sd` over `n − 1`, and a constant feature answers zero. Seeding is
   k-means++: the first centre is row `below n`; each later one is the first row whose running sum of squared distances
   to the nearest chosen centre exceeds `uniform53 · total`, sums left to right in row order. Where every row coincides
   with a centre (`total = 0`) the next seed is the lowest row that is not yet one, with no draw; where the product
   rounds up to the total itself it is the last row off a centre. Lloyd's passes follow: every row to its nearest centre
   by squared Euclidean distance in `+ − ×`, a tie to the lowest cluster; a cluster left empty takes the row farthest
   from its own centre among the clusters that keep another row, a tie to the lowest row, in cluster order; a pass that
   leaves every row where the last pass left it has CONVERGED; otherwise each centre becomes its rows' mean, summed in
   row order, and the passes stop unconverged at `MaxIterations`. `iterations` counts assignment passes, the confirming
   one included. `Restarts` seedings run in order from the group's one stream; the lowest total within-cluster sum of
   squares is answered, a tie to the earliest. Clusters are numbered from ONE in the order they were seeded, never by
   size, so a replay's labels are stable. A centre is stated in the space the clustering ran in (standardised scores
   where `Standardise` is set), and carries its feature's unit only where it is not. The silhouette is opt-in
   (`Silhouette`, a column of `Summary`), because it reads every pair of a group's rows: the mean over rows of
   `(b − a) / max(a, b)` on Euclidean distances, zero for a row alone in its cluster. Refusals, checked in this order
   and each naming what it refuses: `k` below two, a restart or pass bound below one, the seed, no feature, a feature
   named twice, an unknown key, an output name that collides, a feature of a type no clustering reads (`TypeError`), a
   null or non-finite feature, and a group with fewer rows than `k`.
10. **`Bootstrap`, in pinned order.** Per group and replicate, `below n` once per row of the group, in replicate order;
    with `Cluster`, once per distinct cluster value (first-appearance order), each drawn cluster contributing all its
    rows. Each aggregate is then `Column.aggregate` over the drawn rows' cells in draw order: `GroupBy`'s vocabulary and
    its refusals, so an aggregate the group cannot evaluate is the aggregate's own named error. Replicates are numbered
    from one. `Replicates` answers one row per group and replicate, each aggregate typed and unit-stated as a `GroupBy`
    states it. `Summary` answers one row per group and aggregate, as floats: `estimate`, the aggregate over the group's
    own rows; `std_error`, the `StdDev` aggregate over the replicate column, so a `GroupBy` over the `Replicates` table
    reproduces it bit for bit; and, where `Level` is stated, `lower` and `upper` by ONE quantile rule: the replicates
    sorted ascending (a negative zero read as zero first, so no host orders two zeros), position `h = (B − 1) · q`, the
    value at `⌊h⌋` plus `h − ⌊h⌋` of the way to the next (Hyndman and Fan's type 7), at `q = (1 − Level) / 2` and
    `1 − q`. A summary refuses an aggregate of a non-numeric type, and a replicate whose aggregate is null or not
    finite, naming the group, the replicate and the aggregate. Other refusals: no replicate, a level outside `(0, 1)`, no
    aggregate, an unknown column, a null in the cluster column, an output name stated twice.
11. **Both steps are planner barriers.** Their draws are counted against the rows they are handed, so no filter is moved
    ahead of one and no column pruned across one; `Plan.isTotal` answers `false`, because both refuse by row and by
    group. The static walk states each table's names, types and units, every column `Pinned`.
12. **The incremental seam re-evaluates a seeded step whole** (`StepNotRowLocal`, as it does a `Predict`), which is
    byte-equal to a full evaluation by construction. A per-group memo was considered and not built. A group's stream is
    its ordinal among the groups, so a delta that creates or removes an earlier group renumbers every later group's
    stream; a memo would have to prove the ordinals stable before it reused anything, and no consumer's refresh cost has
    been measured to size that against. A measured one is the reason to revisit.
13. **Held by tests, not by the model.** `proofs/Pipeline.fst` carries no regression and no seeded step: `Fit` and
    `Predict` are recorded exclusions of class `measured-elsewhere`, and these two steps join them
    (`proofs/coverage-exclusions.json`, stand-in `SeededStepsTests.fs`). What the pin guarantees is the premise row
    `rng-splitmix64-pinned-by-limbs` in `proofs.json`: byte identity given the limb arithmetic's transcription and the
    basic-operations premise (Phase 589) for the steps' sums, means, distances and one square root.
14. **Where the shard and the tree differed.** (a) The shard has `Pipeline.fst` and its oracle "learn both steps' schema
    effects"; the model has no case for `Fit`-class steps, so neither file moved (item 13). (b) It names a consumer's
    profile table "from the regression corpus" as a vector fixture; this repository holds no such table, and a
    consumer's data does not enter a public repository's corpus. The vectors carry an authored profile shape instead: a
    decimal and an int feature, standardised, four seedings. (c) Phases 604 (the pipeline wire specification), 617
    (weighted aggregates) and 623 (method cards) have not landed, so there is no specification document to add the two
    members to, the weighted aggregates are not in `Agg`'s vocabulary yet (a `Bootstrap` takes whatever `AggFn` holds
    when they are), and the generator's and the steps' method cards wait on 623's roster law.
15. **The law vectors are additions.** `elementary-laws.json` gains three `rng` rows and a `generator` head member;
    `transform-laws.json` gains twenty rows (iterations 181 to 200), a `seeded` head member, and its `iterations` count
    moves from 181 to 201. Every row that was there answers the bytes it did.

## 2026-11-11 — D75: regression modelling is a package of its own, `Fuaran.Compute.Regression`, above LinearAlgebra and Elementary; LinearAlgebra holds matrix operations only and depends on nothing in Compute; Regression reads LinearAlgebra's internals by `InternalsVisibleTo`; the move changes no bit

**Context.** Phase 632, on the operator's rulings of 2026-11-10 and 2026-11-11: functions specific to regression
modelling leave the module of a specific operation, such as the QR decomposition, for a package of their own. After
Phase 630 (D74) `Fuaran.Compute.LinearAlgebra`'s `QR.fs` still held two things, the decomposition (`QR.householder`,
`QR.pivoted`, `PivotedQR`, the `Rank` rule) and least squares (`QRAccumulator`, `LeastSquaresFit`, `LeastSquares`,
`RobustVariant`); `Glm.fs` was all regression; and `Wire.fs` carried the accumulator's codec beside the matrix's.

**Decision.**

1. **The package graph.** After this phase and its neighbours:

   ```
   Fuaran.Compute.Elementary    (scalar pinned maths; Phases 630, 631, 621)
   Fuaran.Compute.Distributions → Elementary                (Phases 618, 629)
   Fuaran.Compute.LinearAlgebra                              (matrix operations only: Matrix, Blas, Triangular, QR, Rank; SVD and SymEig with Phase 622)
   Fuaran.Compute.Regression    → LinearAlgebra, Elementary  (and Distributions once a reader needs it: Phases 619, 629)
   Fuaran.Compute.DataFrame     → Regression                 (Fit, Predict)
   ```

   LinearAlgebra references nothing in Compute. The streaming accumulator goes with regression, not with the
   factorisations: its state is a design's `R` beside `Qᵀy` and a residual sum of squares, and its fold takes a response
   and a weight per row, so it is least squares. The Givens rotation it folds with (`Givens`) and the batch `QR` stay.
   The rank rule moves to its own `Rank.fs` in LinearAlgebra, because the pivoted QR, the least-squares solve and Phase
   622's SVD all read it.

2. **The internals decision: `InternalsVisibleTo`, not a public kernel surface.** The moved bodies read LinearAlgebra's
   internals: the kernels `Kernels.adopt`, `canonical`, `lend`, `norm2Pair` and `rotateNative`, the `Rank` module, the
   internal constructors and members (`Matrix(…)`, `.Storage`, `UpperTriangular(…)`), and, for the accumulator's codec,
   the bit-pattern spelling and shape checks of `LinAlgWire` (`bitsText`, `floatsOf`, `bitsField`, `nonNegativeInt`,
   `typeIs`, `bounded`, private until now and `internal` since). LinearAlgebra grants `InternalsVisibleTo` to
   `Fuaran.Compute.Regression`, the precedent this repository already holds (DataFrame grants ColumnOps and
   Conformance). A public kernel surface was considered and declined: it would publish, and freeze behind a baseline, a
   loan of a vector's backing array (`lend`) and a raw rotation over arrays (`rotateNative`) that no consumer outside
   Compute should call, to serve one sibling package that ships at the same version from the same repository. The cost of
   the grant is the one the precedent already carries: the two packages are versioned together (one `<Version>`), and a
   consumer mixing versions of them is outside the contract.

   **One mechanical consequence, recorded because it touches a file the move does not otherwise touch.** F# refuses to
   inline an `internal` value marked `inline` into another assembly, even one granted `InternalsVisibleTo` (FS1113,
   FS1118). `Kernels.canonical` and `Kernels.lend` were `inline`, so they lose the keyword in `Matrix.fs`; their bodies do
   not change. Within LinearAlgebra the optimizer still inlines both (each is one comparison or one field pair), and no
   bit can move, because the operations performed are the same operations in the same order.
   **Measured (Phase 637, `benchmarks/results/2026-11-12-ryzen9-9950x-phase-637.md`):** the same tree with both kernels
   inlined at Regression's 14 call sites, against the tree as shipped, over `LeastSquares.solve`, `Glm.fit` and the
   DataFrame's `Fit` and `Predict` at 100, 10,000 and 100,000 rows, seven processes per configuration on each host: every
   difference is below the run-to-run spread (.NET geometric mean of the shipped-over-inlined ratios 0.996; node 1.023, a
   direction at a millisecond clock and inside each configuration's range), so the boundary stays as it is and nothing
   under `src/` changed.

3. **No bit moves.** This is a move: the bodies are unchanged beyond the qualified names it forces (the namespace line,
   an `open Fuaran.Compute.LinearAlgebra`, the codec helpers spelled `LinAlgWire.bitsText` and so on from
   `RegressionWire`). Checked: the 20 least-squares vectors move from `linear-algebra-laws.json` to the new
   `regression-laws.json` row for row and the 54 that stay are byte-identical (the multiset of rows across the two files
   is exactly the old file's, none lost, added or duplicated, each file in the old order); the accumulator's two wire
   specimens hash as before; `transform-laws.json`, `elementary-laws.json` and `dataframe-parity.json` are byte-identical.
   The 27 laws that moved to `RegressionLaws.laws` keep their bodies and their names, and each draws from its own
   generator over the seed, so a law answers over exactly the numbers it answered over before. `RegressionLaws` carries a
   copy of the Park-Miller generator, as `ElementaryLaws` does (D74 item 3), because sharing it would need a
   cross-package public type. One law, `every QR refusal is answered as stated`, moved whole although two of its cases
   are the batch factorizations', because the rest of it is the accumulator's and LinearAlgebra may not read
   Regression; the batch factorizations' refusals stay pinned in `linear-algebra-laws.json`.

4. **Naming (the operator's ruling on Phase 630, applied first time).** A caller adds `open Fuaran.Compute.Regression`
   and keeps writing `Glm.fit`, `LeastSquares.solve`, `QRAccumulator.empty`, `RobustVariant.HC3`. Nothing the package
   declares at namespace level collides with a name another Compute package declares there: its exactness table is
   `RegressionOperations`, not `Operations` (which would shadow LinearAlgebra's member by member, D74 item 2), with its
   `Exactness` union nested inside it; the codec is `RegressionWire`, not a second `LinAlgWire`. Checked with a
   `dotnet fsi` probe that opens `Fuaran.Compute.Elementary`, `Fuaran.Compute.LinearAlgebra` and
   `Fuaran.Compute.Regression` in both orders: in each, `Operations.exactnessOf "Blas.gemv"`,
   `ElementaryOperations.exactnessOf "Elementary.exp"` and `RegressionOperations.exactnessOf "Glm.fit"` all answer
   `Some Pinned`, `Operations.exactnessOf "Glm.fit"` answers `None`, `Exactness.Pinned` is LinearAlgebra's type, and
   `Glm.fit`, `LeastSquares.solve`, `QRAccumulator.empty`, `RobustVariant.HC3` and `RegressionWire.encodeAccumulator`
   resolve. No doubled qualifier (`Regression.Regression.`) occurs anywhere in the repository.

5. **Two of the shard's premises did not hold, and neither changed the work.** `linear-algebra-laws.json` carried no
   GLM vectors (the GLM's committed answers are the transform vectors' `Fit` shapes, which do not move); and
   `proofs.json` carries no rows for Phases 591, 595, 598 or 600. The record that named the moved code is
   `proofs/coverage-exclusions.json`: LinearAlgebra's `pinned-order-law-tested` entry loses its accumulator clauses, a
   Regression entry holds them, and the `Fit` row names the regression package's least squares.

6. **Version.** Breaking, the class of Phase 630's move, so it rides the untagged `0.45.0` draft.

**Consequences.** Phases 619 and 629 land in Regression and Phase 622 beside the QR in LinearAlgebra. A statistical
method reads distributions and matrix operations without either carrying the other.

## 2026-11-11 — D74: the pinned elementary functions are a package of their own, `Fuaran.Compute.Elementary`, at the bottom of Compute's graph; LinearAlgebra references it until regression leaves; the move changes no bit

**Context.** `Fuaran.Compute.LinearAlgebra` held three layers in one package: the scalar pinned mathematics
(`Elementary`, Phase 593, D52; `erfc` and the normal functions, Phase 600, D58), the matrix algebra (`Matrix`, `Blas`,
`Triangular`, `QR`, `Wire`), and the models built on both (`Glm`). A distributions library (Phase 629) needs the scalar
maths and should serve the models, so with `Elementary` inside LinearAlgebra it would have had to sit above them. The
operator's rulings of 2026-11-10 and 11 set the graph this decision records:

```
Fuaran.Compute.Solvers        (nothing)                                  Phase 633
Fuaran.Compute.Elementary     → Solvers once 631's inverse reads it       Phase 630; 631, 621
Fuaran.Compute.Distributions  → Elementary, Solvers                       Phases 618, 629
Fuaran.Compute.LinearAlgebra  (nothing, once 632 has moved regression out)
Fuaran.Compute.Regression     → LinearAlgebra, Distributions, Solvers     Phases 632, 634
Fuaran.Compute.DataFrame      → Regression …
```

Moving first, while 618 and 621 were unstarted, costs one breaking move rather than two.

**Premise check** (on `06f2c02`). `Elementary.fs` was the one scalar-maths module; it compiled after `Matrix`, `Blas`,
`Triangular`, `QR` and `Wire`, none of which reads it. Its readers were `Glm`, `Operations` (eleven exactness rows),
`LinearAlgebraLaws` (four laws) and `DataFrame` (the evaluator's `Pow`, `Exp`, `Ln` and `Log10`), and in the suite the
linear-algebra vectors, tests, the proof-oracle host, and the node benchmark's `ElementaryBench`. Neither 618 nor 621 had
started: no `Distributions` project and no `Rng` existed.

**Ruled.**

1. **The package.** `Fuaran.Compute.Elementary` (namespace `Fuaran.Compute.Elementary`) holds `Elementary.fs`, moved by
   `git mv` with its body unchanged, the `SpecialValue` union with it, an `ElementaryOperations.exactness` table of its eleven
   operations, and `ElementaryLaws.laws` (the four laws that left `LinearAlgebraLaws`). It takes FSharp.Core alone and
   nothing of the substrate or of Compute, under LinearAlgebra's Fable rules. The module keeps its name, so a call site
   that adds `open Fuaran.Compute.Elementary` still writes `Elementary.exp`; the break is the namespace and the package a
   consumer references.
2. **Its own `Exactness`, nested in `ElementaryOperations`.** The package declares `Exactness = Pinned | HostRounded`
   rather than reading LinearAlgebra's, which would invert the graph, or DataFrame's, which sits above both. This is D49
   item 2's precedent: the type is per package, one vocabulary (D43's), each package's table classifying its own
   surface. It is declared INSIDE the table's module (`ElementaryOperations.Exactness`), not at namespace level, so that
   `open Fuaran.Compute.Elementary` is safe for every caller: an opened type of the same name would shadow the caller's
   own, and `DataFrame.fs` sits in `Fuaran.Compute` beside `Fuaran.Compute.Exactness`. The table's module is named for
   the package, as `ElementaryLaws` is, rather than `Operations`, because a probe showed that two opened modules of one
   name shadow member by member: with LinearAlgebra and Elementary both opened, `Operations.exactnessOf "Blas.gemv"`
   answered `None` from whichever was opened last, silently. So nothing the package declares at namespace level
   (`SpecialValue`, and the modules `Elementary`, `ElementaryOperations`, `ElementaryLaws`) shares a name with anything
   another Compute package declares, and every caller opens the namespace and writes `Elementary.exp` and
   `SpecialValue.Compute`, exactly as before the move. The first cut of this
   phase declared the type at namespace level and had callers qualify the module through its namespace; the operator's review
   ruled that out, and the nesting replaced it before the phase landed. (Operator ruling 2026-11-11.)
3. **The generator is copied, not shared.** `ElementaryLaws` carries Park and Miller's float generator and the `law`
   runner beside `LinearAlgebraLaws`'s, line for line. Sharing it would need a public type in one package that the other
   reads, and once 632 has moved `Glm` out, LinearAlgebra depends on nothing in Compute. The copy is what keeps a seed
   drawing the same numbers it drew before the move: `ElementaryLaws.laws 590 200` runs the four laws over exactly the
   draws `LinearAlgebraLaws.laws 590 200` gave them.
4. **LinearAlgebra references Elementary until Phase 632.** `Glm` is its only reader; the matrix modules compile without
   it. `DataFrame` receives the package through LinearAlgebra and needs no reference of its own until 632 re-points it.
5. **No bit moves.** The 110 elementary vectors leave `linear-algebra-laws.json` for `elementary-laws.json` (family
   `elementary`), each row byte for byte; the 74 that stay are byte-identical; each file's head is its own. The node leg
   (`LinearAlgebraNode`) runs both law families and prints both documents, and the suite compares each with its committed
   file. `transform-laws.json` moves only in its head: the `kitVersion` stamp that every version cut re-emits, and the
   clause of its description that named `Fuaran.Compute.LinearAlgebra.Elementary` and `linear-algebra-laws.json`, which
   would otherwise publish a namespace and a file that no longer exist. No transform vector moves.
6. **The version.** `0.44.0` is tagged, so the breaking move advances the slot to `0.45.0`.
7. **Publication.** The nuget.org Trusted Publishing policy for this repository names the `Fuaran.Compute.*` pattern
   (operator, 2026-11-11), so the new id needs no registration; the workflow comment that said each new id must be named
   is corrected.
8. **Consumers outside the repository.** `fuaran-dotnet`'s `tests/core-fable/Program.fs` names LinearAlgebra's
   `Elementary`; it adapts in the next cohort raise of `fuaran-dotnet` onto `0.45.0`. No twin outside this repository
   was found reading the elementary rows of `linear-algebra-laws.json` by path.

## 2026-11-10 — D73: a range's bound is computed in floats, and every range loop reads its bound from a local

**Context.** D70 wrote a join's pair emission as loops over ranges of left rows, `for i in lbound j .. lbound (j + 1)
- 1`, with `lbound` computed in `int64` against overflow. Fable compiles `int64` to an emulated integer and compiles
that loop to a JavaScript loop whose condition evaluates `lbound (j + 1)` on every iteration, so under JavaScript
every joined row paid several emulated multiplications and divisions. The portable member runs the same code over
one range, so every join in a browser paid it. .NET evaluates a `for` loop's upper bound once, and every measurement
of D65 to D72 was on .NET, which is how it shipped.

**Found** by profiling a report workload compiled by Fable and run under node: three joins over 22,596 rows took
most of a pipeline that ran four times its .NET time, and node's CPU profile put the emulated integer's conversions
among the top self-times of a join.

**Decided.**

1. `Ranges.bound n parts j`, in `Frame.fs`, is every range's bound: `int (float n * float j / float parts)`. `n * j`
   is exact in a double for any row count a frame holds, and the correctly rounded quotient of two exact integers
   with a divisor of at most 64 truncates to the integer quotient, so the bounds are the `int64` ones. Every bound
   D65 to D72 computed (the coding, the hash partitions, the aggregates' ranges and slices, the join's ranges, the
   gather, the first rows, the key column) is it.
2. Every loop over a range reads its upper bound from a local, so the condition JavaScript evaluates per iteration
   is a variable.

**Evidence.** A law holds `Ranges.bound` equal to the `int64` bound over row counts to a billion and every partition
count from 1 to 64; a bound that rounds instead of truncating goes red on its first case. verify.ps1 green, the
Fable leg included. Under node, one join of 22,596 rows repeated 400 times took 1,836 to 1,853 ms at D69, 2,557 to
2,634 ms after D70 and 1,848 to 1,886 ms with this; the three-join pipeline 16.3 to 16.8, 21.8 to 22.3 and 16.8 to
17.5 ms. The bounds are the same integers, so .NET's answers and paths are unchanged.

**Not decided.** A browser join still runs about 230 ns a row against about 30 on .NET. Its largest remaining cost
is Fable's checked array access in the join's emission loops (a fifth of its profile), where `Raw.get` with a range
proof compiles to a bare index. Two `int64` bounds remain outside any per-row loop (the sort's range array and a
window's partition test). Nothing times a join under node in the regular benchmarks; the node leg times its corpus.

## 2026-11-10 — D72: a join's slot listing and its right-only rows across threads, measured and not kept

**Context.** After D70, what a combining join does on one thread besides its right table's boundary is listing each
slot's right rows (a count per slot, a running sum, a fill over the right side) and, for `Right` and `Outer`, two passes
over the right rows for the ones no left row matched.

**Tried.** The listing across ranges of right rows, in three paths: where every slot holds one right row (a right side
holding each key once, recognised by counting the rows that hold a slot), slot `s`'s row scattered to `members[s]`;
where the slots were few beside the rows, a count per range per slot and each range filling its places in range order;
otherwise the one pass. The right-only rows counted by ranges and written from each range's place. All exact: a law
over right sides of the three shapes held every join kind on every member to the portable member, and three mutants
went red (repeated keys taken as one row a slot, ranges placed last first in a slot, every range's right-only rows
written from the first place).

**Measured.** One sitting, a million-row join on distinct string keys: inner 48.5 ms before and 48.0 after with all
three paths; outer 52.4 and 48.3 in that sitting, then, with the listing taken back and only the right-only pass kept,
52.1 and 52.1, while the inner join, whose path the second form left as it was, moved from 49.5 to 52.4. Neither part
reads above the spread.

**Decided.** Neither ships; the join code carries a note at the listing. The law stays, renamed for what it holds: a
join over the three right-side shapes answers on every member what the sequential join answers, which reaches D69's
partitioned build and D70's parallel pairs and gather over shapes the frame-level law does not draw.

## 2026-11-10 — D71: a grouping's first rows are found by a running maximum, and its key column written by ranges, where its groups are many

**Context.** With a group a row, what follows the coding and the aggregates in a grouping is three passes on one thread:
the scan for each slot's first row, which with every key distinct reads every row; the map from those rows to physical
rows; and the key column read from them (`keyColumn`). A profile put the key column at 0.6 ms for a million int keys and
1.9 ms for a million strings, of a 13 and a 22 ms step.

**Decided.**

1. **The first rows** (`firstRowsAcross`). With codes numbered in first-appearance order, a row is its code's first
   exactly when its code is above every code before it. Each range takes its largest code; then each range, from the
   largest of the ranges before it, writes the rows whose code rises above the largest so far. Only where the codes are
   many: with few, the scan in order stops within the first rows.
2. **The physical rows and the key column** (`keySlots`, `keyColumn`) are written by ranges of slots, each range writing
   only its own; `keyColumn` keeps its rule, a missing key left at the carrier's default, and a decimal key column is
   `Vec.gatherRanges`.
3. All three take at most 16 ranges from four times the kernel set's threshold, D66's rule. `slotsWith` answers its
   first rows as an array.

**Evidence.** The row-hash law holds `slotsWith`'s first rows on every member to the portable member's, and the
cross-member grouping law groups by many keys under a selection. Three mutants go red: the running maximum ignoring the
ranges before (the row-hash law), a missing key written (the row-hash law), first rows not mapped to physical rows (the
cross-member law, whose filtered frames make logical and physical rows differ). verify.ps1 green. BenchmarkDotNet: a
grouping by a million distinct string keys fell from 26.2 to 23.0 ms on the native member; the cases the paths do not
reach read within their spread. A same-sitting profile could not separate the two forms, its int case swinging 10 to
15 ms between alternations; the instrument that could was BenchmarkDotNet, on the one suite that reaches all three paths.

## 2026-11-10 — D70: a join's pairs are written by ranges of its left rows, and its output columns by ranges of their rows

**Context.** After D69 a million-row join on distinct string keys spent 28 of its 52 ms coding keys, 8.3 gathering its
four output columns, and about 16 listing each slot's right rows, matching the left side and writing the pairs, all of
it after the coding on one thread.

**Decided.**

1. **The pairs.** The left rows are taken in ranges. Each counts the rows it emits (a matched row emits its slot's right
   rows, a padded one one row) and the padded ones among them, and marks its slots probed; a running sum of the counts is
   where each range writes; then each writes its pairs there. The rows land in the order one pass writes them. Two
   ranges marking one slot write the same `true`. One path serves every member: the portable member runs its one range
   on the caller's thread.
2. **The columns.** `Vec.gatherRanges` allocates every carrier whole and fills it by ranges of output rows, each reading
   the rows `gather` reads and writing the pads `gatherOrNull` writes, so the vector is theirs.
3. **The cap.** Both split into at most 16 ranges, from four times the rows the kernel set partitions from (131,072
   natively), D66's rule for the coding: 32 ranges of a 100,000-row join cost an int join 15 per cent in overhead.

**Evidence.** A law holds `gatherRanges` to `gather` and `gatherOrNull`, carriers and masks included, over every
carrier, at one, three and sixteen ranges, padded and not. The frame-level law runs every join kind on every member,
and the row-hash law holds every join kind to the nested-loop oracle. Four mutants go red: every range writing from the
start (the cross-member law), row 0 read as a pad (the gather law), padded rows not counted (the portable member
errs), and slots not marked probed. That last breaks both members alike, so the cross-member law cannot see it; the
oracle law does. verify.ps1 green. A million-row join on distinct string keys fell from 55.4 to 44.2 ms and on one key
per ten rows from 19.0 to 14.7 ms in BenchmarkDotNet; the keyed joins at 100,000 rows read level
(`benchmarks/results/2026-11-10-x64-join-gather.md`).

**Not decided.** Listing each slot's right rows (a count, a running sum and a fill over the right side), the right-only
rows of a `Right` or `Outer` join, and the right table's boundary stay on one thread.

## 2026-11-10 — D69: a join's build side of many distinct keys is coded by partitions of its values, and its probe side looked up by the same partitions

**Context.** D66 codes a grouping's key column by partitions of its values and left the join out: the probe side looks
its codes up in the build side's coder, and partitions hold no one coder.

**Decided.**

1. **The split is shared** (`partitionByHash`): a hasher in the join's relation (`cellEq`, where `Int 1` keys as
   `Float 1.0` and a null has no code; or the token relation for an int key against an int key) partitions the rows of
   either side, so equal values on the two sides fall in one partition.
2. **The build side** (`RowHash.buildAcross`) takes D66's path in either relation, from the same probe and threshold,
   and keeps each partition's coder and the map from its codes to the column's (`Coded.ByPartitions`); a null keeps `-1`.
3. **The probe side** (`RowHash.lookUp`) is split by the same hash and each partition looked up in its partition's
   coder, opening nothing, its codes mapped to the column's. Held by one coder (`Coded.ByCoder`), ranges of rows are
   looked up as D65 had it, now each coded straight into the output rather than into a copy.
4. `codesAcross` and `probeAcross`, which existed for the join, are gone; the D65 law reads `buildAcross` and `lookUp`.

**Evidence.** The D65 law, now over `buildAcross` and `lookUp`, holds the build side's codes, their count and the probe
side's codes to the sequential coder on every member, in both relations, over every row-hash kind and pools of
thousands, which `nativeFrom 2` partitions. Four mutants go red: the probe side partitioned in the other relation, its
codes left as the partition's, a build-side null given a code, and the probe opening codes. verify.ps1 green. A join of a
million rows on a million distinct string keys fell from about 79 to 68.5 ms in one sitting, and from 73.3 to 59.2 ms in
BenchmarkDotNet (`benchmarks/results/2026-11-10-x64-hash-join.md`). The harness gains `Partitioned.StringJoin`.

**Costs measured.** A join whose build side is below the threshold read 19.1 ms before and 21 to 23 ms after in one
sitting; alternating its two probe forms (with copies and without) in a later one read no difference, inside a spread in
which the unchanged portable join moved from 97 to 61 ms. Recorded as unresolved, not as a cost.

**Not decided.** The gain is smaller than the grouping's (14 to 19 per cent against D66's half): the probe side is hashed,
scattered and looked up again, and gathering a million matched rows' columns, which this does not reach, is much of the
join.

**Numbering.** The parallel aggregates landed as D67 beside the authoring helpers' D67 from a concurrent session, which
landed first; they are D68, and their commit's subject still says D67.

## 2026-11-10 — D68: a grouping's aggregates are fed by row ranges where they merge exactly, and emitted by group ranges

**Context.** After D65 and D66 a grouping's key is coded across threads and its aggregates are not. A profile of the step
over a million rows put the feed (two aggregates) at 1.5 to 1.9 ms beside 1.3 to 3.8 ms of coding, and the emit at 5.1 ms
where there is a group a row. D8's rule stands: no fold is split where a split moves a byte.

**Decided.**

1. **What merges.** A stream's state is per slot. Slots fed by ranges of rows, each range into a stream of its own and
   merged in range order (`Stream.MergeFrom`), answer what one feed in row order does for `Count` and an int `Sum` (they
   add exactly), `First` (the earlier range's row), `Last` (the later range's), `Min` and `Max` over an int or float
   carrier (the earlier range's best against the later one's under the stream's own comparison and tie rule, which is
   the comparison the later rows would have made), and an int `Mean` below 2^22 rows (integers added in floats are
   exact below 2^53). A deferral in any range defers the slot. `Stream.Mergeable` says which. A float or decimal `Sum`,
   a float `Mean` and a boxed `Min` or `Max` stay on one thread: added by ranges, a float total rounds at other places.
2. **`GroupAgg.feedAcross`** feeds a mergeable stream by at most 16 ranges on the pool, the first into the stream itself,
   then merges the rest into it in range order, split by group ranges across the pool. Where a slot per range for every
   range would outnumber the rows, the merge costs what the feed does, and the stream is fed alone. `FeedAll` is
   `FeedRange` over every row.
3. **The emit.** A stream whose `Emit` writes only a number or a null of the output's carrier (`Stream.EmitsTyped`: a
   count, a sum, a mean, and a row-keeping aggregate over an int or float carrier) writes only its own row of the output,
   so where the kernel set partitions the groups those streams emit every slot across the pool first. The loop in group
   order then reads which of their slots deferred and emits every other stream and every deferral as before, so the
   first error is the one it always reported. Any other stream may write a cell that unpacks the output, so it emits in
   the loop.

**Evidence.** Two laws. Over frames thousands of rows long, every streamed aggregate over int, float, string, date,
timestamp and decimal columns, grouped by few keys and by many, one aggregate a pipeline, on every member, equals the
portable member's answer byte for byte; a column of small ints keeps sums from overflowing and one of doubles makes a sum
added by ranges round differently, and most comparisons must be of answers, not of refusals. A first version carried
every aggregate in one pipeline, and an int overflow made every comparison an error against the same error: three merge
mutants passed it. At the stream, over a boxed vector holding a cell of another type at random rows, `feedAcross` equals
`FeedAll` slot by slot: a typed column holds no such cell, so through the evaluator a deferral reaches every row or none.
Six mutants go red: `First` taking the later range, `Min`/`Max` comparing the later best with the earlier, counts not
merged, deferrals not merged, typed emits all read as written, and a float `Mean` merged. verify.ps1 green. Figures in
`benchmarks/results/2026-11-10-x64-parallel-aggregates.md`: an int grouping by ten keys with `Sum`, `Count` and `Max`
fell from 6.46 to 2.59 ms at a million rows on the native member and from 646 to 352 µs at 100,000; a float `Sum` and a
`Count` from 3.70 to 3.37 ms; a grouping by a million distinct strings from 31.0 to 26.3 ms (the emit). The harness gains
`Partitioned.GroupByInt`.

**Costs measured.** At one key per ten rows (100,000 groups, the feed alone and the emit split) the native member read
6 per cent slower at a million rows in BenchmarkDotNet and level in a same-sitting profile. The portable member moved
within 5 per cent both ways across the suites on unchanged machine code.

**Not decided.** A float sum by ranges would need a summation that does not depend on the order, which `Column.aggregate`
does not do and the answers would then move; a float `Mean` and `Sum` stay sequential. With many groups the feed stays on
one thread: partitioning rows by group range, D8's attempt, read every row from every thread.

## 2026-11-10 — D67: hand-written `ColExpr` gets opt-in authoring helpers, `Fuaran.Compute.Syntax`, that build exactly the constructors' nodes; `And` and `Or` are functions, not operators; a builder over pipeline steps is rejected

**Context.** Hand-written `ColExpr` is the verbose part of a pipeline written in F#: `Filter(Binary(Gt, Col "salary",
Lit(Int 95)))`. A survey of this repository's suite and two downstream suites (2026-11-10, regex counts, an order of
magnitude) found about 860 hand-written pipelines averaging about 1.6 steps, against about 580 `Binary(…)` and 530
`Lit(…)` constructions. The steps are already short union constructors; the expressions carry the noise, and suites had
started to mint local shorthands one file at a time.

**Decided.**

1. **The module.** `Fuaran.Compute.Syntax`, in namespace `Fuaran.Compute`, in the `Fuaran.Compute.DataFrame` package
   (`src/Fuaran.Compute.DataFrame/Syntax.fs`). It is an ordinary module, opt-in by `open Fuaran.Compute.Syntax`; nothing
   opens it automatically. FSharp.Core only, Fable-clean. Every helper returns exactly the node its constructor builds:
   no new node, no new semantics, no evaluation, so a consumer that does not open it observes nothing.
2. **The helpers.** Leaves: `col`, `param`, `lit` (any `Cell`), one per `Cell` case (`litInt`, `litFloat`, `litBool`,
   `litStr`, `litDate`, `litTimestamp`, `litDecimal`, `litNull`), and `now`. One spelling per `BinOp` case: the
   arithmetic and comparison cases are the F# operator of the same meaning behind a leading `.` (`.+ .- .* ./ .% .= .<>
   .< .<= .> .>=`); `And` and `Or` are `and'` and `or'` (item 4); the ordinal predicates are `contains`, `startsWith` and
   `endsWith`, taking the subject first, as `Binary` does. Then `not'`, `isNull'`, `coalesce`, `inList`, `inParam` and
   `cast`. `litDecimal` takes the text as given, as the constructor does; `Cell.decimal` canonicalises where a caller
   needs that.
3. **The naming rule.** A helper takes the plain name of what it builds, lower-cased. Where that name is an FSharp.Core
   function or an F# keyword (`not`, `isNull`, `and`, `or`), it takes a trailing prime, so an `open` never captures a
   name FSharp.Core binds. A literal names the `Cell` case it wraps, because `int`, `float` and `string` are FSharp.Core
   conversions. The suite holds the rule: with the module open, `not`, `isNull`, `int`, `float` and `string` still answer
   as FSharp.Core's.
4. **`And` and `Or` are functions, because the operator form binds wrongly.** The phase's shard assumed F# ignores a
   leading `.` when it decides precedence, so `.&&` would bind as `&&`. It does for the arithmetic and comparison
   operators, and does not for these two: F# reads `.&&` as an `&` operator and `.||` as a `|` operator, and places both
   at the COMPARISON level, left-associative. Measured with `dotnet fsi` before any code was written:
   `a .> b .&& c .< d` builds `((a > b) && c) < d`. Every operand is a `ColExpr`, so the wrong tree type-checks. No
   operator class sits between comparison and the built-in `&&`, so no `.`-prefixed spelling is safe, and redefining
   `&&` itself would break item 3. A two-argument function has no precedence to get wrong.
5. **Totality.** The suite maps every `BinOp` case to its helper by a closed match. FS0025 is an error in this
   repository (D2), so a case the algebra gains stops the build until its spelling is decided.
6. **The identity law.** For every helper, the node it builds equals the hand-written constructor structurally and
   renders to the same wire bytes through `DataFrameCodec.encodeExpr`. A deliberately wrong mapping (a swapped `Lt`/`Gt`
   pair, a wrong literal) is reported by name. A roster check reads the module's public members by reflection and
   requires the law to hold every one, so a helper added without a law is red.
7. **Rejected: a builder over pipeline steps.** A computation expression or LINQ-style builder over `Transform` steps
   saves nothing at about 1.6 steps per pipeline. The law, proof-oracle and conformance suites exist to pin raw nodes, and
   a builder would put a layer in front of them. An `IQueryable` or expression-tree surface is refused outright, for the
   reasons the downstream dataset model records against a second query language: an open expression form cannot be
   checked default-deny, fails at translation rather than by a named refusal, and is not wire data.
8. **The version.** `0.43.0` is tagged, so this additive surface advances the slot to `0.44.0`, a draft.

**Evidence.** The `Phase 626` family in `SyntaxTests.fs`: the identity law over every `BinOp` case (read off the union by
reflection) and every other helper, the go-red, the roster check, precedence pins (`.*` over `.+`, `.-` left-associative,
arithmetic over comparison) and the no-capture check. `DataFrameTests.fs`, this repository's densest file, reads through
the helpers with its suite unchanged; its local column builder `col` was renamed `column` so the helper's name is free.
`api/Fuaran.Compute.DataFrame.txt` moves by additions only.

**Not decided.** No other file migrates in this phase; each adopts as it is touched. The remaining `ColExpr` cases
(`Case`, `ApplyFn`, `Quotient`, `Rounded`, `Convert`, `Call`, `Interpolate`, `Arg`) have no helper: their constructors
already name what they build, and a helper earns its place by a measured saving.

## 2026-11-10 — D66: a group-by key column of many distinct keys is coded by partitions of its values

**Context.** D65 codes a key column across threads by row ranges merged in row order, which pays only where the column
repeats its values; a column of many distinct keys fell back to one thread, where a million distinct strings cost 43 ms
to code and the grouping step about 70.

**Decided.**

1. **A hasher.** A coder gains `Hashing`: it opens nothing, and for a value it has not met answers the hash of the key
   it would open (`keyHash`, `EqualityComparer<'k>.Default`'s hash, which agrees with the dictionaries' equality), and a fixed number for a special. A value is
   one key in one dictionary, so equal values hash alike. Its dictionaries stay empty, so threads share one.
2. **`RowHash.codesByHash`**, token relation. Each row's partition is its hash, Fibonacci-mixed, modulo the partitions;
   each partition's rows are listed in row order (counted and scattered by range on the pool); each partition is coded on
   the pool by a coder of its own, in place over its list; a value's code is the number of values whose first row comes
   before its own, from the partitions' first rows marked and counted by range. Those are first-appearance codes.
3. **When.** `across` (the shared driver behind `codesAcross` and `slotsWith`) codes a probe of the first range's first
   4,096 rows. A column whose probe opened more than half its rows, coded through dictionaries (no direct table for its
   carrier), from four times the kernel set's partition threshold (131,072 rows natively), is coded by at most 16
   partitions; any other goes on as D65 had it. Only `GroupBy` and `Window` (`slotsWith`) take it: a join's build side
   needs one coder holding every code for its probe side, so `codesAcross` never does.
4. **The probe sizes its dictionary for the column** (D64) unless the column may be partitioned; then for itself, and the
   rest of the first range for the column.

**Two findings on the way, both in the code's comments.**

- **A call in tail position cost int keys five times their coding.** The hasher's branch first ended `codeIn`, which is
  inlined into `intCode`, `directCode` and every carrier's coding; F# marks a call there `.tail`, and the JIT compiled
  those methods without tiering or inlining them: `codesOf` over a million int keys went from 1.8 to 9 ms on a path that
  never hashes. `DOTNET_JitDisasmSummary` showed it ("Tier-0 switched to FullOpts", four bytes more IL). The branch now
  adds the hasher's count, which is 0, so the call is not last.
- **Thirty-two partitions on a 32-thread machine** ran no faster than 16 at a million rows and seven times slower than
  sequential at 100,000 rows, a partition holding 3,000 rows; six arrays of a row each were large-object allocations on
  every call. The cap is 16, the threshold four times the partition threshold, and the arrays three (the partition array
  is reused for the ranks and then the codes, the row list for the partition codes).

**Evidence.** The law of D65 runs the partitioned path through `nativeFrom 2` (its probe on small frames opens most of
its rows): `slotsWith` on every member equals the portable member's slots and first rows, one key and two, every row-hash
kind, under a selection. Three mutants go red: rows partitioned by position, ranks not offset by earlier ranges, ranges
scattered last first. A hasher answering one hash for every value stays green, as it should: one partition is correct,
only not parallel. verify.ps1 green, the Fable leg included (`Array.Clear` is not in Fable; `Array.fill` is). Figures in
`benchmarks/results/2026-11-10-x64-hash-coding.md`: a grouping step over a million distinct string keys fell from about
71 to 34.5 ms on the native member, one sitting, and from 57.1 to 31.3 ms in BenchmarkDotNet; at 100,000 rows, at one
key per ten rows, and on the portable member it is level. The harness gains `Partitioned.StringGroupBy`.

**Not decided.** A join's build side over many distinct keys still codes on one thread, and needs its probe side looked up
in the partition's coder by the same hash. At one key per ten rows the coding gains and the step barely moves: the
aggregates and output over 100,000 groups are the rest of it. BenchmarkDotNet's two legs an hour apart read the portable
member's string cases 30 to 42 per cent apart on unchanged code; cases that collect over a million strings need one
sitting, alternated, to compare.

## 2026-11-08 — D65: a key column is coded across threads where it repeats its values, and a join's probe side is looked up across threads

**Context.** D8 (item 2) measured a grouping partitioned by key and did not keep it: each partition read its rows scattered
across the vector, and at 100,000 rows it was 1.5 to 2.1 times slower. Since D61 to D64 a grouping first codes its key
column into dense codes, and a profile of the step put most of it there: at a million rows, 2 to 4 ms of an int grouping
and 9 to 41 ms of a string grouping, against 1 to 2 ms for the aggregates. The coding ran on one thread for every verb.

**Decided.**

1. **`RowHash.codesAcross`** codes a column through the kernel set's partitions. The first coder chooses its direct table
   over the whole column, as before, and codes the first of `parts` ranges on the caller's thread. Where that range opened
   more than a quarter of `n / parts` codes, merging would cost what coding does, so that coder codes the rest itself.
   Otherwise each other range is coded on the pool by a coder of its own, with no direct table. Then, range by range in row
   order, the first coder codes each range's values at their first rows, opening the ones it has not met in the order the
   range met them, which is the order the sequential coder opens them in; each range's codes are rewritten through that
   map on the pool. Every value is coded by `codesOf`, so the relation and the numbering are the sequential coder's.
2. **`RowHash.probeAcross`** looks a join's probe side up across ranges. A lookup opens no code, builds no table and
   sizes no dictionary, so the ranges only read the coder.
3. **Where it runs.** `GroupBy` and `Window` pass the kernel set to `keySlots`; the join builds through `codesAcross` and
   probes through `probeAcross`. `Distinct`, `Pivot` and the design matrix take `Kernels.oneThread`, unchanged. The
   portable member answers one partition, so Fable and `oneThread` run the sequential path.
4. **No range copies anything.** `codesInto` codes rows `lo .. hi - 1` into the same indexes of one output array, so the
   parallel path allocates no array per range: a first version copied each range's rows and codes, eight arrays of 125,000
   ints at a million rows, past the large-object threshold, and gained little on int keys. Its loops index both arrays by
   the loop counter: an offset between them kept the .NET bounds checks, which cost 11 to 13 per cent of an int grouping on
   the sequential path. The first range sizes its dictionary (D64) for the whole column; sized for its own rows, the
   fallback grew it a second time and a timestamp grouping over distinct keys read 12 per cent slower.

The harness gains `Partitioned.GroupBy`, the grouping through both members, beside D8's `GroupByKey` prototype.

**Evidence.** A law holds `slotsWith`, `codesAcross` and `probeAcross` on every parallel member to the sequential coder,
codes and first rows, in both relations, one key column and two, under a selection, over every row-hash kind and over pools
of thousands. Four mutants go red: the ranges merged last first, a range's codes not rewritten, a range's first rows read
from row 0, a probe range written at row 0. The frame-level law ("over frames thousands of rows long ...") already ran
`GroupBy`, `Window` and every join kind on every member. verify.ps1 green. BenchmarkDotNet, before and after in one
sitting (`benchmarks/results/2026-11-08-x64-keyed.md`, D65's section): a grouping by ten int keys on the native member
fell from 400 to 348 µs at 100,000 rows and from 4.01 to 3.53 ms at a million; a string join at 100,000 rows from 6.17 to
4.51 ms and a timestamp join from 5.01 to 4.01 ms. The profile put a grouping by ten string keys at a million rows at 9.7
ms before and 2.9 ms after.

**Costs measured.** The fallback (distinct keys) is within 1 per cent for int, date, decimal and timestamp groupings. A
string grouping over 100,000 distinct keys read flat, 4 and 7 per cent slower in three sittings, with the same bytes. The
portable member's grouping by ten keys at a million rows read 4 and 6 per cent slower in two sittings, where a scratch
profile in one sitting read it level; its code is `codesOf` over `codesInto`, the same loops.

**Not decided.** A column of many distinct keys still codes on one thread: a merge in row order costs what coding does
there. Partitioning by the key's hash, each thread owning its keys and the first rows renumbered after, is the way to it,
and needs a hash every carrier's coder agrees on under both relations. The aggregates (`FeedAll`) and the output are still
sequential; an int `Sum` or a `Count` could be summed by ranges exactly, a float sum could not without moving its bytes.

## 2026-11-08 — D64: a key column's dictionary is sized from its first 1,024 rows

**Context.** After D63 a string key cost 3.7 times the int key at 100,000 distinct keys and a timestamp key 3.2 to 3.5
times (`benchmarks/results/2026-11-08-x64-keyed.md`). Their codes open in a dictionary that starts empty and doubles about
seventeen times on the way to 100,000 keys, copying every entry at each doubling; at that size its arrays are past the
large-object threshold, so the doublings are gen2 garbage too.

**Decided.** `RowHash.codesOf`, on a side that opens codes and over at least 4,096 rows, looks at the dictionary its
carrier opens keys in once the first 1,024 rows are coded (`sizeFor`): where more than half of those rows opened a key, it
reserves room for the column's rows at that rate. A column whose sample repeats its keys reserves nothing, so a group-by
over a few groups allocates what it did. The carriers sized are the ones whose keys can be many: a timestamp's
microsecond keys, a decimal's value keys beside its direct table, and a text column's string, day or microsecond keys.
An int and a date in range code through their direct tables and need no dictionary. Under JavaScript the slot table gains
`Reserve`, one rehash to the power of two that holds the reservation at most half full.

Sizing changes no code: a dictionary's numbering is the order keys are added in, whatever its capacity.

**Evidence.** The slot-table law against `Dictionary` reserves partway through its run; a `Reserve` that moved the
capacity without rehashing errors it. verify.ps1 green. At 100,000 rows a string group-by fell from 4.4 to 3.2 ms and its
bytes from 12.98 to 7.92 MB, a timestamp group-by from 4.0 to 2.8 ms, a string join from 7.5 to 6.4 ms and a timestamp
join from 5.9 to 5.1 ms. At 10,000 rows, where the decimal key has no direct table, a decimal group-by fell from 427 to
283 µs.

**Not decided.** A string key is still 2.8 to 3.5 times the int key. .NET keeps each entry's hash, so a doubling never
rehashed a string; what is left is hashing and comparing the strings themselves, once per row on each side.

## 2026-11-08 — D63: a decimal column is read at the boundary in one scan of each text

**Context.** A decimal column crossing into the frame cost 1.5 ms per 100,000 rows against 0.13 ms for an int column
(`benchmarks/results/2026-11-08-x64-keyed.md`, D62's section), on every evaluation. `ofDecimalTexts` called
`ScaledDecimal.scaleOf`, which ran `shape` over every text, then `fill`, whose `tryScaled` ran `shape` again before reading
the digits: about four scans of each text.

**Decided.** `ScaledDecimal.read` scans each present text once and writes its value at the scale read so far; a row with
more fraction digits multiplies the rows before it by the difference, at most `Width` times over a column. Each value is an
integer no larger than its final magnitude, so where the column fits the width every step is exact, and where it does not
the values are discarded and the column takes the text path, as before. No per-row scale is kept, so nothing is allocated
beside the carrier: an array of a byte or an int per row is past the large-object threshold at 100,000 rows, the
allocation `2026-11-07-x64-join.md` traced the dated pipeline's tail to. `ofDecimalTexts` and `packAt`'s decimal arm read
through it.

`scaleOf` and `fill` stay: they are the host functions `proofs/Frame.fst` models the boundary by, and the proof oracle
plugs them into the model. The oracle compares the model's frame against production's, now built by `read`, and a law
holds `read` to `scaleOf` and `fill` bit for bit over 2,000 drawn columns (raised scales, both zeroes, leading and trailing
zeros, negatives, texts outside the grammar, columns past the width, absent rows holding garbage).

**Evidence.** Three mutants go red: the earlier rows not rescaled and the sign dropped (31 tests each), and trailing
fraction zeros kept in the value (13). verify.ps1 green. At 100,000 rows the boundary fell from 1,535 to 686 µs, a decimal
join from 5.4 to 3.8 ms and a decimal group-by from 3.0 to 2.2 ms.

**Not decided.** The column is still parsed on every evaluation. Holding the carrier beside the Core column (a cache keyed
by the text array, or Core carrying the scaled integers) would remove the rest, 0.56 ms per 100,000 rows; the first needs a
weak table Fable does not offer, and the second is a Core change and a release.

## 2026-11-08 — D62: a timestamp and a decimal key take one 64-bit key, a decimal key a direct table, and a decimal group key is gathered

**Context.** After D61 a timestamp key cost 4.0 to 4.2 times the int key at 100,000 distinct keys and a decimal key 5.1 to
7.1 times. The timestamp coded through a dictionary over a (second, nanosecond) pair and the decimal through one of sixteen
per-scale dictionaries over floats, with a floating remainder per trailing-zero test.

**Decided.**

1. **An instant that is a whole microsecond is one `int64` key**, its epoch microsecond (`Micros`): the canonical range is
   under 3.2e17 microseconds either side of the epoch. One with a sub-microsecond digit keeps its pair key (`Instants`).
2. **A decimal off the table is one `int64` key**, its unscaled value with trailing zeros removed by integer division, times
   16, plus the scale left (`DecVals`, one dictionary in place of sixteen). The magnitude is below 10^15 and the scale below
   16, so the key is one value's alone.
3. **A decimal key has a direct table** (`ScaledDirect`), built at the scale of the first decimal vector a coder meets before
   it has coded a decimal, when its range is under four times the rows. A value from any column goes through it exactly
   when it is exact at that scale (a coarser value scaled up; a finer one only where the division leaves a whole number)
   and in range, so the route is a function of the value and one value has one code.
4. **`keyColumn` gathers a decimal key column** (`Vec.gather`), as every other verb carries one. It fell to the boxed arm,
   which made a cell per group and parsed every text again to pack it: most of the decimal group-by's cost.

**Evidence.** The row-hash value kinds gain nanosecond instants, decimals at scale 3 (some exact at scale 2, some not), a
far-apart decimal column, and a close scale-2 column that builds a table coarser values are scaled into. Five mutants go
red: the scale-down exactness test dropped, the scale-up ignored, the sub-microsecond test removed, the scale left out of
the 64-bit key, and the decimal key column gathered in reverse (four suites). verify.ps1 green.

**Not decided.** A decimal column is parsed to its scaled carrier at the boundary on every evaluation: 1.6 ms per 100,000
rows, now most of what a decimal join costs over an int join. The other `keyColumn` arms copy by their own loop, which
writes a zero carrier at an absent row where `Vec.gather` copies the row's; they were left as they are, since what an
absent slot holds reaches the adopted column. The timestamp and string dictionaries still grow from empty.

## 2026-11-08 — D61: a date, a timestamp and a decimal key are coded by value

**Context.** `RowHash.codesOf` rendered every date and timestamp row to its canonical text and every decimal row to
`Cell.token` before a dictionary probe, so a join or group-by keyed by any of them paid one string per row on each side.
At 100,000 distinct keys a date join took 35.4 ms and 47.7 MB against 1.8 ms and 6.2 MB for the same values as ints, and
a timestamp join 56.7 ms and 104.6 MB (`benchmarks/results/2026-11-08-x64-keyed.md`).

**Decided.** Each is coded by the value its canonical text spells, and only where the text is that value's canonical text:

1. **A date by its day count**, through a direct table of its own (`Direct`, now shared with the int table) built from
   the first date vector a coder meets before it has coded a day. A day outside the canonical range keeps its text code.
2. **An instant by its epoch second and nanosecond**, so one instant in two units is one key, as its trimmed canonical
   text already was. An instant outside `isInstantInRange` keeps its text code.
3. **A decimal by its unscaled integer at its own scale**, trailing fraction zeros removed (`1.50` at scale 2 and `1.5`
   at scale 1 are one key, `-0` and `0` one key), in one table per scale. The scaled carrier is read; the texts are not.

A boxed or string-carried text is read back to the same key (`tryDays`; `tryInstant` in nanoseconds with the text
required to equal its re-rendering; `ScaledDecimal.tryScaled` at the text's own scale within the width) and otherwise
keeps its text code. No text that is not canonical is any value's canonical text, so it still equals only itself, and
the relation each verb partitions by is unchanged.

**Evidence.** The row-hash laws (join kinds against the nested loop over `cellEq`; the set verbs, group-by and pivot
against the token oracle) gain a second draw over value-coded kinds: canonical dates beside a text that is not one,
timestamps in milliseconds and microseconds, decimals at scales 0 and 2 with both zeroes, and decimals past the width.
Three mutants go red: the decimal normalisation removed (three suites), the unit's nanosecond factor dropped (three
suites), and a boxed canonical date coded by text while a typed one is coded by day (the join law).

**Not decided.** A timestamp or decimal key still codes through a dictionary that grows from empty, which leaves it at 4
to 7 times the int key at 100,000 rows; presizing it meets D60's open point. A float key is unchanged.

## 2026-11-07 — D60: a join pays for a second table and growing lists only where it needs them

**Context.** An int join of 100,000 by 100,000 took 3.7 ms and 11.0 MB under BenchmarkDotNet, about 2.4 times a
hand-written hash join (`benchmarks/results/2026-11-07-x64-join.md`, which also corrects an earlier figure of 9 times).

**Decided.** `evalJoin` changes in four ways, and no answer changes.

1. **One key column's codes are its slots.** A left row has a code only where a right row opened it, and a struck null
   is `-1` on both sides, so `RowHash.Index` is built only for a tuple of columns.
2. **The filtering joins build no slot lists.** `Semi` and `Anti` read only whether a slot was found, so the lists move
   into the combining branch.
3. **The output is counted, then written once.** A count per slot replaces the per-right-row `matched` flags, and it
   sizes the two index vectors, so no list grows and none is copied.
4. **A side with no padded row uses `gather`.** `gatherOrNull`'s null handling is paid only where a `-1` was written.

**Evidence.** The join's nested-loop laws hold. Three mutants go red in them: a right row counted as matched whatever
was probed, the padded counts crossed, and a left null key left unstruck. A fourth, a right null key left unstruck,
survives because it changes nothing: no left row can find that code, and `Right` and `Outer` still emit the row as
unmatched.

**Not decided.** That right-side strike stays: it is redundant only by the argument above, and it keeps null rows out of
the slot lists. The coder's dictionary still grows from empty, because `codesOf` is shared with verbs whose rows say
little about their cardinality.

## 2026-11-07 — D59: a date comparison reads day counts where that is the text's order

**Context.** A date column has been day counts since Phase 423, but comparing one with a date still rendered every
row's day to text: `filterBits` had no date leaf, and the compiled path's ordinal comparison read `asStr`. A range
filter was 93% of the dated pipeline's allocation (`benchmarks/results/2026-11-07-x64-date-comparison.md`).

**Decided.**

1. **The reference's order stays the text's.** `compareCells` orders dates ordinally, and that is not the order of the
   day counts once a year leaves four digits: `10000-01-01` sorts before `2026-01-01`. `Column.ofDates` does not check
   its range, so such a day can reach a comparison. Day counts decide only where both days spell a four-digit year
   (`fourDigitDay`), and nothing here changes an answer.
2. **The filter kernel takes a date leaf under two conditions**: the constant is canonical (`tryDays` reads it), and
   every value in the vector is in the range. Otherwise it declines, and the compiled path answers. The scan reads
   values under absent rows too, which can only decline more often. It costs one pass per leaf, small beside the
   comparison: the range filter is about 1.3 to 1.6 times the int filter's time.
3. **The compiled path compares day counts per row** where both operands are date columns or a canonical constant, for
   what the kernel never sees: a derive, two date columns, and a date leaf beside a predicate the kernel declines. A
   row outside the range compares the rendered text, as it did.
4. **The model moved with the code.** `Frame.fst`'s `leaf` gains the date case and `filter_kernel_is_step` covers it.
   `kernel_prims` gains two premises, both instantiated at production's functions by the differential: two dates whose
   days fit compare as their day counts, and a date text with a day count is that day's canonical text and fits. The
   differential now draws date constants and days past the range.

**Evidence.** F* refutes `leaf_eval` when the range guard is removed from `leaf`. With `fourDigitDay` forced true, five
of `DateCompareTests`' seven cases and the frame differential go red.

**Not decided.** A date constant that is not canonical still goes through text. `Timestamp` columns are unchanged: they
have no single integer to compare at a fractional unit, and no measurement yet asks for it.

## 2026-11-05 — D58: `Fit` estimates the general GLM — the binomial, quasi-binomial, inverse Gaussian and negative binomial families beside Phase 595's and 598's, under the whole link set (identity, log, logit, probit, cloglog, inverse, inverse square, square root), every `g`, `g⁻¹` and `dμ/dη` built from pinned operations; a pinned `erfc` and normal distribution, density and quantile enter `Elementary`; the binomial links hold their means to `[2⁻⁵², 1 − 2⁻⁵²]`; separation is refused by a pinned rule; the negative binomial estimates θ by MASS's alternation; the wire refuses a pair outside the admitted table when it decodes; `Predict` maps the response scale through each link, its standard error by the delta method

**Context.** The operator's request of 2026-11-03 ("I want it capable of estimating Generalized Linear Models"),
answering the accepted plan's open question 4 with "in v1, and the full link set", and the operator's ruling of
the same day that the negative binomial *estimates* θ rather than only fixing it. **The premise check at take**,
against `main` at `3226b00`: 595's `Glm.fit` with its pinned IRLS rules (D49 items 14 to 19, D55), 598's Tweedie
and quasi-Poisson (D57) and 597's `Predict` (D56) are on main; `Elementary` carries `exp`, `ln` and `pow` (D52);
`FitFamily` / `FitLink` (D53) carried Gaussian, Poisson, Gamma, Tweedie and quasi-Poisson under Identity and Log,
with every other pairing refused as `UnsupportedFit` *when the step ran* — the wire decoded any pairing. Two load-bearing
premises of the shard were checked and found to need correction. (a) 597's response scale did not "handle `Scale =
Response`" by mapping standard errors: it stated `se_fit` on the link scale whatever the prediction's scale (D56),
which item 7 changes. (b) The IRLS arithmetic was *not* already link-generic in the code: 595 and 598 spelled the
log link's working response and weight directly (`(y − μ)/μ`, `w·μ`, `w`, `w·μ^(2−p)`), so the general form is new
here, and the old spellings are kept for the old pairs so their bits do not move.

**Decided.**

1. **The admitted table** (`Glm.admitted`, one table read by the estimator, the evaluator and the codec): R's
   `family` objects restricted to this link set. Gaussian under identity, log and inverse; binomial and
   quasi-binomial under logit, probit and cloglog; Poisson and quasi-Poisson under log, identity and square root;
   Gamma under inverse, identity and log; inverse Gaussian under inverse square, inverse, identity and log; the
   negative binomial under log, square root and identity (MASS's `negative.binomial`); Tweedie under log only
   (D57). Twenty-nine pairs counting the negative binomial's two forms. R's `cauchit` and the binomial's `log` link
   are left out: neither was asked for, and the binomial log link needs a constrained step (`μ ≤ 1`) this loop
   does not have. `Glm.defaultLink` names each family's canonical link where the set has it (Gaussian identity,
   binomial logit, Poisson log, Gamma inverse, inverse Gaussian inverse square) and log for the negative binomial
   and Tweedie.
2. **The links, pinned** (`Glm.linkFunction`, `Glm.inverseLink`, `Glm.meanDerivative`): `g`, `g⁻¹` and `dμ/dη` as
   the `GlmLink` documentation spells them, through `Elementary` only. The binomial links' means are held to
   `[ε, 1 − ε]` and their derivatives at or above `ε`, **`ε = 2⁻⁵²`** (R's `.Machine$double.eps`, the clamp R's
   `make.link` uses); the cloglog link caps `η` at `700` before `exp η` (where `1 − exp(−exp η)` is already one), and
   forms `1 − exp(−t)` by its alternating series below `t = ½` (eighteen terms, Horner from the highest) so a
   small mean keeps its relative precision without an `expm1`. Alternatives considered: R's own logit clamp (`η`
   to `±30`, which bounds the mean at about `9·10⁻¹⁴` rather than `ε`) — rejected for one rule across the three
   links; a pinned `expm1` transcribed from fdlibm — rejected as a second long routine where a series covers the
   only range that needs it.
3. **A mean out of range is refused by name.** `LinAlgError` gains, appended, `MeanOutOfRange of row`: after the
   step-halving of D49 item 16, a mean not positive where the family's must be, or a linear predictor not positive
   under the inverse-square or square-root link. Under the log link the same event keeps D55's name, `Separation`
   (the overflow of Phase 306), and under the binomials it is `Separation` too. The verb reports `MeanOutOfRange` as
   a `FitError` naming the input row, `Separation` as D55's `OverflowError`.
4. **The responses each pair admits**, refused before any iteration as `InvalidResponse` naming the first row: the
   binomials `[0, 1]` (a 0/1 outcome, or a proportion with the trials as prior weights); inverse Gaussian positive;
   the negative binomial not negative, and **a whole count where θ is estimated**, because the θ score is the count
   likelihood's (item 8); Gaussian positive under the log link and not zero under the inverse link, so the start
   `g(y)` exists. Integrality is required nowhere else: a non-integer Poisson or binomial response is a
   quasi-likelihood fit, which R admits with a warning, and a fixed-θ negative binomial's deviance needs no count.
   Alternative considered: refusing non-integer successes `w·y` under the binomial — rejected, because it would
   refuse every fractionally weighted (survey-weighted) logistic regression.
5. **Separation is refused by a pinned rule.** Wherever the iteration ends — at convergence or at the iteration
   bound — a binomial or quasi-binomial fit whose fitted probability at a row of positive weight lies within
   **`τ = 2⁻²⁰`** of zero or one is refused as `Separation` naming that row. Complete separation drives every
   separated row's probability to about the deviance's size, far below `τ`, before the deviance stopping rule
   stops; quasi-complete separation does so for the separated rows while the deviance of the overlap is below
   about `100`. Alternatives considered: the linear-programming test of Konis (2007, R's `detectseparation`),
   which is exact but needs a simplex solver with its own pinned pivoting in every twin — left for a later
   phase if a consumer needs it; R's `10ε` warning threshold, which complete separation reaches only by the clamp
   and quasi-complete separation rarely; a coefficient-divergence test, which needs iterations beyond the stopping
   rule. **The stated limit:** a quasi-complete separation over a large overlap can converge with its separated
   rows' probabilities above `τ` and is fitted, with large finite coefficients, as R fits it.
6. **The starts** (`μ₀`, then `η₀ = g(μ₀)`): Poisson and quasi-Poisson `y + 0.1` and Tweedie `y` with `0.1` for a
   zero (unchanged); the binomials R's `(w·y + 0.5)/(w + 1)`; the negative binomial MASS's `y` with `1/6` for a
   zero; every other family `y`. The working response is `z = (η − offset) + (y − μ)/(dμ/dη)` and the working weight
   `w·(dμ/dη)²/V(μ)` — except that under the log link `z` divides by `μ` itself and the old pairs keep their D55 and
   D57 weight spellings (`w·μ`, `w`, `w·μ^(2−p)`), so every Phase 595 and 598 vector is byte-identical. Gaussian
   under a link other than the identity iterates; under the identity it is still the one least-squares solve.
   Dispersions: one for binomial, Poisson and negative binomial; Pearson's `Σ w·(y − μ)²/V(μ)` over `n − rank`
   for quasi-binomial (`V = μ(1 − μ)`), inverse Gaussian (`μ³`) and Gamma under a non-log link (`μ²`; under the log
   link D55's spelling stays); the deviance over `n − rank` for Gaussian under any link. The unit deviances: the
   binomials `2 (y·ln(y/μ) + (1 − y)·ln((1 − y)/(1 − μ)))`, inverse Gaussian `(y − μ)²/(y·μ²)`, the negative binomial
   `2 (y·ln(y/μ) − (y + θ)·ln(1 + (y − μ)/(μ + θ)))` with `ln(1 + u)` by Goldberg's correction (`ln w · u/(w − 1)`,
   `w = 1 + u`), so the deviance keeps its precision as θ grows and the stopping rule does not see rounding as a
   rise — the plain `ln((y + θ)/(μ + θ))` made a negative binomial at `θ = 10¹⁰` fail to converge.
7. **`Predict` on every link** (supersedes D56 item 5's "the standard errors are on the link scale whatever the
   prediction's"). On the response scale the prediction is `g⁻¹(η)`; `se_fit` (and Gaussian's `se_pred`) is the delta
   method's `|dμ/dη| · se(η)`, in the response's unit — which is `1 · se` under the identity link, so least squares
   moves no bit; an interval's ends are the link scale's `η ± q·se` each mapped through `g⁻¹` (`Glm.responseInterval`),
   not a symmetric interval about the mean, ordered (the inverse and inverse-square links decrease), and held
   inside the mean's range: where the link meets the range's boundary at `η = 0` (identity under a positive
   family, square root, inverse square, and inverse on the side of zero the prediction lies), an end beyond zero is
   held at zero, and an end the link maps to no finite mean is null — unbounded on that side. A response-scale
   prediction whose own `η` has no mean under the pair (`Glm.validPredictor`: a negative `η` under Poisson's identity
   link, say) is refused as a `FitError` naming the row, on both model sources. A prediction interval stays
   Gaussian's alone and is now the identity link's alone too. An interval reads the normal row wherever the family
   fixes its dispersion (Poisson, binomial, negative binomial), the t at the residual df otherwise. The one vector
   this moves is `transform-142-predict-poisson-response`, whose `se_fit` is now the delta method's.
8. **The negative binomial's θ** (the operator's ruling of 2026-11-03), after MASS's `glm.nb` and `theta.ml`
   (Venables and Ripley, 2002): Poisson under the same link from its own start; θ by Newton's method at its means —
   from `θ₀ = Σw / Σ w·(y/μ − 1)²`, `θ ← |θ| + score/information`, the digamma and trigamma differences
   `ψ(θ + y) − ψ(θ)` and `ψ′(θ) − ψ′(θ + y)` written as the finite sums they are at a whole count (`Σ_{k<y} 1/(θ + k)`,
   `Σ_{k<y} 1/(θ + k)²`, `k` ascending), until a step is at most **`2⁻¹³`** (MASS's `eps^¼`), at most `MaxIter − 1`
   steps; then alternations, each an IRLS at the current θ from the previous fit's linear predictor and a fresh θ
   at the new means, while `|ℓ₀ − ℓ|/d₁ + |Δθ|/d₂ > tol` with `d₁ = √(2·max(1, df))`, `d₂ = 1` and `ℓ` the
   log-likelihood less its `Σ w·ln Γ(y + 1)`, which no comparison reads, at most `MaxIter` alternations. The
   coefficients, deviance and iterations are the last alternation's IRLS, fitted at the θ before its re-estimate;
   `Theta` is the re-estimate (MASS's convention), `ThetaStdError` is `1/√I` at it and `Alternations` the count. A
   caller may still fix θ (`NegativeBinomial (Some θ)`, finite and positive or `InvalidTheta`). **Refused by name**
   (`ThetaNotConverged alternation`): a start or step that is not finite, a θ not positive, a θ above **`2⁵⁰`** (data
   no more dispersed than Poisson's, whose likelihood rises towards the Poisson limit — MASS returns a huge θ with a
   warning), a last Newton step above the tolerance, or the alternation bound reached. Differences from MASS,
   chosen: θ is re-estimated at the *new* means (MASS reads the previous ones, an artefact of its loop order, at the
   same fixed point); refusals where MASS warns; the likelihood without its constant. The null deviance of an
   estimated negative binomial is stated at the fit's θ (`nullDeviance` refuses `NegativeBinomial None` as
   `InvalidTheta NaN`). The `Summary` table of a negative binomial fit gains `theta` and `theta_std_error` (in the
   response's unit) and `alternations`, after `converged` — a closed schema the spec decides.
   Alternatives considered: θ fixed only (the operator ruled it out); θ by golden-section search on the profile
   likelihood — rejected, Newton is MASS's and converges in a handful of steps.
9. **Units.** The linear predictor is in the response's unit under the identity link, a pure number under the
   log, logit, probit and cloglog links, `unit(y)⁻¹` under the inverse link and `unit(y)⁻²` under the inverse
   square; under the square-root link it is `unit(y)^½`, which the unit algebra cannot carry, so a fit of a
   response with a unit states no coefficient unit under that link. A deviance is in its unit deviance's unit
   (Gaussian `unit(y)²`; Poisson, quasi-Poisson and negative binomial `unit(y)`; inverse Gaussian `unit(y)⁻¹`;
   Tweedie as D57; none for Gamma and the binomials); `sigma` and the dispersion state units for Gaussian only, and
   only least squares states a decimal response's deviances in decimal.
10. **The wire.** The family tags `binomial`, `quasiBinomial`, `inverseGaussian` and `negativeBinomial` (with an
    optional float member `theta`: present for a fixed θ, absent for an estimated one), the link tags `logit`,
    `probit`, `cloglog`, `inverse`, `inverseSquare` and `sqrt`. **A family and link outside the admitted table is
    refused when the pipeline decodes**, by name (`fit: the binomial family is not fitted under the log link`), so a
    pipeline that reaches a step names a pair it can evaluate; one built in memory is still refused at the step as
    `UnsupportedFit`. So no transform vector can carry an unsupported pair: the slot Phase 592 gave
    `fit-unsupported-refused` (iteration 128, a Poisson identity fit, now admitted) is re-used for
    `fit-binomial-response-refused`. The wire baselines gain one document per new case; the reflective witness
    pairs a case with the other union's first case, so some witnesses name pairs the decoder refuses — the baseline
    pins what the encoder emits, which is a superset of what decodes (as with a decode alias).
11. **The special functions** (`Elementary`, under D52's measurement rule): `erfc` is fdlibm's `s_erf.c` transcribed
    with its published coefficients (each checked against its published bit pattern), its exponentials through the
    pinned `exp` and fdlibm's `SET_LOW_WORD` done as an exact scale, floor and quotient; `normalCdf` is
    `erfc(−x/√2)/2` with the argument carried as a double and its rounding error and the error folded back by a
    first-order term; `normalDensity` folds `x²`'s exact error back the same way; `normalQuantile` is Wichura's AS 241
    (`PPND16`) with its published coefficients, the tails through the pinned `ln`. **Measured** against an 80-digit
    reference (`tests/Fuaran.Core.Compute.Tests/fixtures/glm-reference/general.py`): `erfc` within 1 ulp over 164
    points on `[−6, 28)` and its branch points; `normalCdf` within 1 ulp over 140 points on `[−37.5, 8.7]`;
    `normalDensity` within 2 ulps over 162 points on `[−38, 38]`; `normalQuantile` within 2 ulps over 18 points from
    `10⁻³⁰⁰` to `1 − 10⁻⁶`. The suite holds the bounds at 1, 2, 2 and 4.
12. **The reference and the laws.** Every admitted family and link pair is held, within a relative `10⁻⁶`, to an
    independent 80-digit computation (`general.py` / `general.json`: Gauss–Jordan normal equations, IRLS to a step
    below `10⁻⁵⁰`, the negative binomial's θ by Newton on its exact score alternated to `10⁻⁴⁵`), on Bliss's beetle
    mortality (logit, probit, cloglog, quasi-binomial), a synthetic 0/1 outcome, Dobson's counts (Poisson and
    quasi-Poisson identity, Poisson square root), McCullagh and Nelder's clotting times (Gamma, inverse Gaussian and
    Gaussian under their non-canonical links) and synthetic overdispersed counts (negative binomial, θ fixed and
    estimated); R's `glm` and `MASS::glm.nb` are a cross-check only. Six laws join `LinearAlgebraLaws.laws`: under
    each canonical pair the observed information equals the expected at convergence, and under probit it does
    not; binomial logit with an intercept fits `Σwμ̂ = Σwy`; the negative binomial at `θ = 10¹⁰` is Poisson's within
    `10⁻⁶`; logit coefficients are `1.45` to `1.95` times probit's where the probit estimate is twice its error;
    every refusal of this entry answers as stated; and each link's `g`, `g⁻¹` and `dμ/dη` agree. Gaussian under
    identity stays byte-equal to the least-squares fold (D55's law, unchanged).

**Rejected.** Shipping the families under their canonical links only, which the operator's "the full link set"
rules out; a host-rounded `erfc` (D43's tier), which D54 retired.

**Consequences.** The public estimator is a general GLM: classification, proportion and binary-outcome models are
data on the wire and reproduce byte for byte on every host. A language twin certifying the transform laws ports
items 1 to 8 and 10 to 11; the eighteen new vectors, the renamed vector 128 and the moved vector 142 are red on it
until it does.

## 2026-11-04 — D57: `Fit` fits Tweedie(p) and quasi-Poisson under the log link and states heteroskedasticity-consistent standard errors HC0 to HC3 — Tweedie from R's `statmod` start with `w·μ^(2−p)` working weights through the pinned `pow`, quasi-Poisson as Poisson's iterations with Pearson's dispersion; the sandwich is one pass over the rows in a pinned order in the working weights and residuals; a caller asking for robust errors without naming a variant gets HC3, resolved when the spec is made

**Context.** The families half of the plan's S5 (`fuaran-compute-linear-algebra-and-glm.md`), operator rulings D9
and D10 of 2026-11-01 (sandwich errors HC0 to HC3 ship here, as an option on `Fit`'s standard errors) and the
ruling of 2026-11-03 on the plan's §7 Q2 (the default variant is HC3). **The premise check at take**, against
`main` at `63dd667`: 595's IRLS (`Glm.fit`, D55) and 597's scoring (`Predict`, D56) are on main, and
`Elementary.pow` (D52) is the pinned float-exponent power — exact at `y = 1`, `−1`, `2` and `½`, `exp(y · ln x)` in
double-double otherwise — so Tweedie's working weight `μ^(2−p)` takes it and nothing else. The `FitFamily` cases
`Tweedie of power` and `QuasiPoisson` were already typed (D53) and refused as `UnsupportedFit`, with a wire
spelling; this entry evaluates them.

**Decided.**

1. **The two families, in `LinearAlgebra.Glm`.** `GlmFamily` gains, appended, `Tweedie of power: float` and
   `QuasiPoisson`; both are fitted under `GlmLink.Log` only, every other pairing `UnsupportedModel`.
   *Quasi-Poisson* is Poisson's mean model and Poisson's iteration exactly — the start `μ₀ = y + 0.1`, the working
   weight `w·μ`, the unit deviance — so its coefficients, means, deviance and iteration count are Poisson's bit for
   bit; only the dispersion differs: Pearson's `Σ w·(y − μ)²/μ` over the rows of positive weight, in row order,
   divided by `n − rank` (null where that is zero), and the classical standard errors are read under it.
   *Tweedie(p)* has `V(μ) = μᵖ`: the start is R's `statmod::tweedie` — `μ₀ = y`, with `0.1` in place of a zero;
   the working weight `w·μ^(2−p)` through `Elementary.pow`, so at `p = 1` it is `w·μ` and at `p = 2` exactly `w`
   (the exact cases of D52); the unit deviance is Poisson's at `p = 1`, Gamma's at `p = 2`, and otherwise
   `statmod`'s `2 (y·(y₁^(1−p) − μ^(1−p))/(1 − p) − (y^(2−p) − μ^(2−p))/(2 − p))` with `y₁ = y`, or `1` at `y = 0`,
   every power through `Elementary.pow`; the dispersion is Pearson's `Σ w·(y − μ)²/μᵖ` over `n − rank`. Every other
   rule of the loop — the first iteration, the halving, the stopping rule, the null deviance — is D55's, unchanged.
2. **The power and the response.** A power strictly between zero and one, a NaN or an infinity is refused before
   any row is read: no Tweedie distribution has such a power. `LinAlgError` gains, appended, `InvalidPower of
   power`; the verb refuses it at resolution as a `FitError`. A power of zero or below is admitted — it names a
   Tweedie distribution — and so is every power from one up. The response must be finite and, under the log link,
   not negative (the start needs `μ₀ > 0`); from `p = 2` up it must be positive, since `y^(2−p)` is infinite at a
   zero once `p > 2` and Gamma's deviance needs `y > 0` at `p = 2`. Quasi-Poisson admits Poisson's responses.
   Outside the range is `InvalidResponse` naming the first row, a `FitError` at the verb (D55 item 7).
3. **Tweedie at `p = 1` agrees with quasi-Poisson; it is not the same bits.** The variance function, working
   weights, deviance and dispersion formula coincide (`pow μ 1` is `μ`), but the starts differ — `statmod`'s
   `y + 0.1·[y = 0]` against R's quasi-Poisson `y + 0.1` — so the two converge to one estimate by different paths
   and agree within the stopping rule's order. That is the law. Tweedie at `p = 2` *is* Gamma's iteration bit for
   bit — the start, the weights and the deviance all coincide — which is a second law; its dispersion differs in
   the last place (`d·d/μ²` against Gamma's `((y − μ)/μ)²`).
4. **The sandwich.** `LeastSquares.robustStandardErrors variant x weights residuals fit` states, per design
   column, `√diag((RᵀR)⁻¹ · Σᵢ ψᵢψᵢᵀ · (RᵀR)⁻¹)` with the score `ψᵢ = wᵢ·rᵢ·xᵢ` over the kept columns and `R` the
   fit's, read as R's `sandwich::vcovHC`. One pass over the rows of positive weight in row order (a zero-weight row
   is not counted, D49 item 13), each a fixed sequence: `u = R⁻ᵀxᵢ` (`k` ascending to `t`, from `+0`), the leverage
   `hᵢ = wᵢ·Σₜ u[t]²`, the multiplier `s = wᵢ·rᵢ` — divided by `√(1 − hᵢ)` under HC2 and by `1 − hᵢ` under HC3 —
   then `v[t] = s · Σₖ R⁻¹[t,k]·u[k]` (`k` ascending from `t`), which is `(RᵀR)⁻¹ψᵢ`, and `v[t]²` added to the
   `t`-th sum. HC1 multiplies each sum by `n / (n − rank)`, `n` the rows of positive weight. Only the diagonal is
   formed, row by row, so the cost is one O(p²) step per row and no p × p meat. `RobustVariant` = `HC0 | HC1 | HC2
   | HC3` is the package's type. For least squares the residuals are `y − ŷ` and the weights the prior weights;
   for a generalised linear model, `Glm.robustStandardErrors`, the working weights the final iteration's solve was
   folded under and the working residuals `(y − μ)/μ` at the final mean — R's `weights(fit, "working")` and
   `residuals(fit, "working")`, so the answer is free of the dispersion, as `vcovHC` on a `glm` is. `GlmFit` gains
   `WorkingWeights` and `WorkingResiduals` to carry them.
5. **Where the sandwich is undefined.** Under HC1 with `n ≤ rank` (no residual degree of freedom), and under HC2
   and HC3 when any row's leverage is one or more — its adjusted score is `0/0`, which R answers as `NaN` in every
   column — every error is null, as a classical error is null without a residual degree of freedom (D53 item 8).
   HC0 is defined there. Refused: a shape mismatch, a negative or non-finite weight, a non-finite design cell or
   residual at a row of positive weight.
6. **`n` counts the rows of positive weight, and R's HC1 does not always.** `sandwich::meatHC` takes `n` as the
   model matrix's rows, zero-weight rows included, while the bread takes the rows of positive weight; with a zero
   prior weight R's errors are scaled by the ratio of the two. This package counts the rows the fit counts (D49
   item 13), so its errors are the textbook estimator's; where no weight is zero the two agree.
7. **The default variant is HC3, resolved when the spec is made.** `FitStdErrors` = `Classical | HC0 | HC1 | HC2
   | HC3` is a member of `FitSpec` (`StdErrors`), classical in `FitSpec.ols`. A caller asking for robust errors
   without naming a variant writes `FitStdErrors.Robust`, which IS `HC3`, or the wire spelling `robust`, which
   decodes to `hc3`. The spec never stores "robust": a stored pipeline means one estimator whatever a later version
   says the default is, and its wire bytes say which. The wire member `stdErrors` (`classical`, `hc0` … `hc3`,
   `robust` on decode) is emitted only when it is not classical, so every existing pipeline's wire bytes, and
   every wire baseline document, are unchanged.
8. **What the option moves.** The `Coefficients` table's `std_error` and `t` — nothing else: the estimates, the
   units, `Summary`, `Fitted` and `Residuals` are the classical fit's, byte for byte. A positive ridge penalty with
   robust errors is a `FitError` (the sandwich of a penalised estimator is not an estimator this version states; a
   zero penalty folds nothing and is admitted). `Predict` refuses a spec whose errors are robust, under either
   model form, as a `FitError`: its `se_fit` and `se_pred` read the classical covariance off `R` (D56), and a
   prediction answering classical errors for a model that asked for robust ones would be read wrongly.
9. **Units** (D53 item 10, D55 item 8). Under the log link a coefficient is in `1 / unit(x)` for both families. A
   unit deviance is in `unit(y)^(2 − p)`: quasi-Poisson's in `unit(y)`, Tweedie's stated where `2 − p` is an
   integer other than zero the algebra carries and none otherwise (`p = 1.5` states none). The dispersion is a pure
   number.
10. **Incremental.** Neither family keeps state, as D55 item 9 has it for every GLM. A robust least-squares fit is
    still maintained: `R` is the stored accumulator's, byte-equal to a fresh fold, and the sandwich's pass reads
    the group's rows at each evaluation, as the fitted values already do, so an append refresh is byte-equal to a
    full evaluation and the state codec is unchanged.
11. **Laws, vectors and the reference.** `LinearAlgebraLaws` gains four laws: quasi-Poisson is Poisson's iteration
    byte for byte with Pearson's dispersion; Tweedie at `p = 1` agrees with quasi-Poisson within the tolerance's
    order and at `p = 2` is Gamma's iteration byte for byte; HC0 on a thousand homoskedastic rows is within a
    quarter of the classical errors, HC1 is HC0 scaled by `√(n/(n − rank))`, and HC0 ≤ HC2 ≤ HC3 per column; every
    new refusal and undefined answer fires. `transform-laws.json` gains thirteen vectors (iterations 150 to 162):
    a grouped quasi-Poisson summary, Tweedie at `p = 1.5` over coefficients and a weighted summary, least squares
    under each of HC0 to HC3, Poisson and Tweedie coefficients with robust errors, and four refusals by class
    (`FitError`: a power, a response, a ridge with robust errors, a prediction from a robust model); the pooled
    node leg renders them under Fable line for line. The reference computation is
    `tests/Fuaran.Core.Compute.Tests/fixtures/glm-reference/reference.py` — Python's standard `decimal` at sixty
    digits, the normal equations by Gauss–Jordan elimination, IRLS run to the maximum-likelihood estimate — and its
    output `reference.json` beside it: Dobson's trial (R's `?glm`) under Poisson, quasi-Poisson and Tweedie(1.5), a
    response with zeros under Tweedie(1.5) and Tweedie(1.3), and heteroskedastic least squares unweighted and
    weighted with a zero weight, each with its estimates, dispersion, deviance, classical errors and HC0 to HC3.
    `FitFamiliesTests` holds the verb to it within a relative `1e-7` under a tight control, which measures the
    estimator rather than the default stopping point (the GLM sandwich reads the final iteration's working weights,
    as R does, so under the default control it differs from the estimate's own by the stopping rule's order).

**Rejected.** *A `Robust` case resolved at evaluation* — item 7: a stored spec would change meaning with the
default. *Tweedie at `p = 1` taking quasi-Poisson's start*, to make the two the same bits — item 3: the family
would then not be `statmod`'s on the one power where the two meet. *Refusing powers of zero and below* — they are
Tweedie distributions; the log link's start bounds the response instead (item 2). *Forming the p × p meat and two
matrix products* — the diagonal row by row is the same estimator with one pinned order and no intermediate matrix.
*R's `n` for HC1 with zero weights* — item 6. *A sandwich for a ridge fit, and robust errors for `Predict`* —
item 8; each is its own phase when asked for.

**What a consumer does.** Nothing to adopt: a pipeline that named Tweedie or quasi-Poisson under `Log` and met
`UnsupportedFit` now gets the fit, and robust errors are opt-in. A full-literal `FitSpec` construction gains the
`StdErrors` field (`FitSpec` is new in this draft). A language twin certifying the transform laws ports items 1 to
4 and 7; the thirteen new vectors are red on it until it does.

**Tidy.** Reviewed every key file. `fitGlmGroupOf` and the least-squares group now build the group's design through
one helper (`fitGroupMatrix`), which the sandwich reads too; Gamma's Pearson loop became the one `pearson` fold all
three estimated-dispersion families share, its bits unchanged. Nothing else in the files touched is made dead or
obsolete.

## 2026-11-04 — D56: a fitted model scores rows with one `Predict` step — the model is the `Fit` spec over its training rows, refitted bit for bit, or a coefficient table for point estimates; a row is coded as the fit coded its training rows; `se_fit` and `se_pred` read the fit's `R` by one triangular solve and a sum of squares; an interval's quantile is a row of a Student t table carried as data and read through `Interpolate` in `1/df`, the normal row at infinity

**Context.** The scoring half of the plan's S5 (`fuaran-compute-linear-algebra-and-glm.md`), amended by ruling F9
of 2026-11-01 (the forecasts plan §4.2): `Predict` returns prediction intervals, and the t quantile is a pinned
data table evaluated through `Interpolate` (option (a)), not computed from `Elementary` (option (b)). **The premise
check at take**, against `main` at `0b1329a`: 596's `Interpolate` is on main (D48), and 592's `Fit` (D53, with 595's
GLMs, D55) is on main — but its OUTPUT does not carry everything scoring needs. `Coefficients` carries the estimates
in pinned term order with their standard errors, and `Summary` carries `σ̂` and the residual df, but no table
carries `R⁻¹` (only its diagonal, through `std_error`), and none records a categorical term's reference level: a
scoring level absent from the coefficient rows is either the reference or a level the fit never saw, and the table
cannot say which. Item 1 is the corrected model.

**Decided.**

1. **The model.** `Predict of PredictSpec`, `PredictSpec = { Fit: FitSpec; Model: PredictModel; Scale:
   PredictScale; Interval: PredictInterval option }`. `PredictModel.Training source` fits the spec over the
   training rows exactly as a `Fit` step over them does (`fitResolve`, `fitDesign`, then one group fit per group
   by the same `fitOneGroup` the `Fit` evaluator reads), so the estimates, `R`, the dispersion and the df are the
   fit's own bits; the spec's `Output` is not read. `PredictModel.Coefficients source` reads a table of the
   `Coefficients` schema — the spec's group keys, `term` and `estimate` — for point estimates over the intercept
   and numeric terms only: a categorical term is refused by name (the table records no reference level), every
   term of the spec must have a row per group and every row's term must be one of the spec's (each refused by
   name), an estimate that is null (an aliased column) contributes nothing, as in the fit, and an interval is
   refused (the table carries no `R`).
2. **Coding a row.** Each term is evaluated over the step's input as the fit evaluated it over the training rows
   (the derive's typing and unit check), then coded by the training design's coding, which `FitDesign` now
   records (`TermCoding`): a numeric term is its value, and must be numeric in the same unit as the training term
   (a term in another unit is refused naming both — a metre beside a kilometre would otherwise be mis-scored); a
   categorical term is one indicator per coded level against the training rows' levels, so the reference level
   scores no indicator, exactly D53 item 4's treatment coding, and a level the training rows did not hold is
   refused naming the term and the level. A row with a null term or offset answers null in every appended
   column (listwise, as the fit deletes). Each row is scored by its own group's fit, found by the token `GroupBy`
   groups on; a group the model has none for is refused naming its keys. The offset is the scoring row's.
3. **The prediction.** `η = Σⱼ xⱼ·βⱼ + offset`, the columns ascending from `+0`, an aliased column contributing
   nothing, the offset added last — the order a `Fit`'s fitted value and `Glm`'s linear predictor are formed in,
   so `Predict` on the training rows reproduces `Fitted` byte for byte under every family. `Scale = Link` answers
   `η`; `Response` answers `μ`, which is `η` under `Identity` and `Elementary.exp η` under `Log`. `predicted` is
   a float column whatever the response's type: a decimal response's `Fitted` is the exact decimal of this float
   (`DecimalArith.ofFloat`, D53 item 10).
4. **The standard errors** are on the link scale. With `x₀` restricted to the fit's kept columns, `z` solves
   `Rᵀz = x₀` by `UpperTriangular.solveUpperT` and `q = Σ zᵢ²` left to right, so `q = x₀ᵀ(RᵀR)⁻¹x₀` from one
   pinned triangular solve and a sum of squares — no inverse is formed. `se_fit = √(φ·q)` and, under the gaussian
   family, `se_pred = √(φ·(1 + q))`, with `φ = σ̂²` the dispersion the fit's `Summary` reports; written with `φ`
   rather than `σ̂·√q` so that the one correctly rounded square root is the last operation, as
   `LeastSquares.standardErrors` writes a coefficient's. `R` is the factorization the coefficients' `std_error`
   reads — a ridge fit's penalised `R`, a GLM's last weighted solve — so `se_fit` at a unit row is that
   coefficient's error up to rounding. `se_pred` is null outside the gaussian family: a GLM's predictive
   distribution is not `η ± σ̂√(1 + q)` on the link scale. With no residual df there is no dispersion, and the
   errors and bounds are null.
5. **The interval.** `PredictInterval = { Kind: Confidence | Prediction; Level: Slot<float> }`; the bounds are
   `η ∓ t·se_fit` (confidence) or `η ∓ t·se_pred` (prediction, the gaussian family only, refused by name
   otherwise), mapped through the inverse link on the response scale — so a log-link interval is the link-scale
   interval transformed by `Elementary.exp`, and is pinned (594 and 595 are on main; no host-rounded mark
   arises). `t` is the quantile at the fit's residual df where the family estimates its dispersion (gaussian,
   gamma), and the normal row where the family fixes it (Poisson), as R's `summary.glm` reads its tests.
6. **The t table** (`StudentT`, internal to the DataFrame package). **Levels:** two-sided coverage `0.5`, `0.8`,
   `0.9`, `0.95`, `0.98`, `0.99`, `0.999`; a level not tabled is refused naming the tabled ones. **Grid:** `ν` = 1
   to 30, 40, 50, 60, 80, 100, 120, 200, 500, 1000 and the normal row `ν = ∞`. **Values:** the `(1 + L)/2`
   quantile of Student's t, each the double nearest the exact value, computed once in 70-digit decimal arithmetic
   from Abramowitz and Stegun's closed forms 26.7.3 (odd `ν`) and 26.7.4 (even `ν`) for `P(|T| < t)` — the error
   function for the normal row — solved by bisection to `10⁻⁵⁰`, and checked by quadrature of the density (spot
   values: `t(0.975, 10) = 2.228138851986275`, `t(0.975, 1) = 12.706204736174705 = tan(0.475π)`,
   `z(0.975) = 1.9599639845400543`; the last two are one ulp above what common statistics libraries print, whose
   own routines are faithful rather than correctly rounded). **Read:** one `Interpolate` curve per level,
   `InterpKind.Linear` over the knots `x = 1/ν`, with the normal row at `x = 0`; a tabled `ν` meets its own knot and
   answers its row exactly (the curve's knot rule), a `ν` between rows reads linearly in `1/ν` (within `2·10⁻⁴` of
   the exact quantile at `ν = 35`, level `0.95`), and a `ν` above 1000 reads between the 1000 row and the normal
   row — the shard's "df beyond the table clamps to the normal row" is met by the limit rather than by a step,
   which is the more accurate reading, and is stated here rather than silent. No transcendental function is on the
   path.
7. **Units.** On the link scale `predicted` and the bounds are in the linear predictor's unit — `unit(y)` under
   `Identity`, a pure number under `Log` (`fitStatUnits`' `Eta`); on the response scale `unit(y)`; `se_fit` and
   `se_pred` are on the link scale, so `unit(y)` under `Identity`. A coefficient table states no response unit, so
   its `predicted` carries none.
8. **Where it stands.** Every refusal is `EvalError.FitError` with a `predict:` text, or the evaluator's own
   `UnknownColumn` / `TypeError`; no `EvalError` case is added. The static walk appends the closed float columns,
   `Pinned` where the training rows, the terms and the keys are. The step is a fall-back for the incremental seam
   (`StepNotRowLocal "predict"`): its model source can change between evaluations, and refitting it is the whole
   of its cost. `PipelineQuery` reads its params and its model source. Wire:
   `{"$type":"predict","fit":{<the fit step's members>},"model":{"from":"training"|"coefficients","source":…},
   "scale":"link"|"response","interval":{"kind":"confidence"|"prediction","level":<slot>}?}`.
9. **Laws.** `PredictTests` holds: a training row's prediction is its fitted value bit for bit (gaussian with
   groups, levels and an offset; Poisson); `se_fit` is the textbook `σ̂·√(1/n + (x₀ − x̄)²/Sxx)` and
   `se_pred² = se_fit² + σ̂²` within `1e-9`; an interval at a tabled df uses the tabled quantile exactly; every
   table row is exact at its knot. Eleven transform law vectors (iterations 139 to 149) pin the answers, the node
   leg holds them under Fable line for line, and five name their refusal class.

**Rejected.** *Widening `Coefficients` with the covariance or `R⁻¹`* — a closed per-term schema cannot carry a
`p × p` matrix without a column per term (a schema the data decides), and a new `FitOutput` case for it would still
leave the reference level unrecorded; refitting the training rows is pinned and costs one factorization. *Computing
the quantile from `Elementary`* — ruling F9 chose the table. *Interpolating in `ν` and clamping above the table* —
it answers `t(1000)` for every larger `ν` and is coarser between rows than `1/ν`. *A delta-method standard error
on the response scale* (`μ·se_η`) — it is not what the interval is built from, and two scales in one column would be
read wrongly; the errors stay on the link scale. *A decimal `predicted` for a decimal response* — the interval and
the errors are floats, and the decimal is one conversion away. *A new `EvalError` case* — `FitError` already names
the model's refusals. *Maintaining a prediction incrementally* — item 8.

**What a consumer does.** Add the `Predict` arm to any exhaustive `match` over `Transform`. A language twin that
certifies the transform laws ports items 2 to 6 (the t table is data to copy), and the eleven new vectors are red on
it until it does.

**Tidy.** Reviewed every key file. The design's categorical coding was computed inside the column builder; it is
now the recorded `TermCoding` the builder reads, so the fit and the prediction share one definition. The per-group
fit the `Fit` evaluator inlined is `fitOneGroup`, read by both. The fit step's wire members are one `fitFields`,
written by both steps. Nothing else in the files touched is made dead or obsolete.

## 2026-11-04 — D55: a log-link GLM fits by IRLS in `LinearAlgebra.Glm` — Poisson and Gamma under `Log`, every step a pinned operation, so the coefficients, the iteration count and the convergence decision are the same bits on every host; the `Fit` verb's refusal lifts for the two pairings; the first iteration is not compared with the saturated start, and a rise smaller than the tolerance is not a rise; the null model with an intercept and an offset is the intercept-only fit with that offset

**Context.** The plan's S4 (`fuaran-compute-linear-algebra-and-glm.md` §2.5; operator rulings D5, D6 = B, D7 and
D9 of 2026-11-01). IRLS is a loop of weighted least-squares solves, and weighted least squares is 591's streaming
Givens QR, so a GLM is the same `Fit` verb (D53 item 1) over the same kernel; 593 pinned `exp` and `ln` (D52) so
that the loop is built from pinned operations alone. The premise check at take found 592's `Fit` (D53) and 593's
`Elementary` (D52) on `main`, and the IRLS rules recorded as D49 items 14 to 19 (the shard cites them as D48;
596 took D48). Two of those rules do not survive contact with the loop, and the null-deviance rule D53 item 8
attributes to R does not match R when an offset sits beside an intercept; items 4 to 6 below correct each.

**Decided.**

1. **`Glm` in `Fuaran.Compute.LinearAlgebra`.** `Glm.fit : GlmFamily → GlmLink → GlmControl → Matrix → y →
   weights → offset → Result<GlmFit, LinAlgError>`, every `exp` and `ln` through `Elementary`. `GlmFamily` =
   `Gaussian | Poisson | Gamma`, `GlmLink` = `Identity | Log`, both appendable (598 adds Tweedie and
   quasi-Poisson, 600 the logit link). `GlmControl` is opaque: `GlmControl.create maxIter tol` answers `None`
   unless `maxIter ≥ 1` and `tol` is finite and positive, so a fit never meets a control out of range;
   `GlmControl.defaults` is R's `glm.control` (25, `1e-8`). `GlmFit` carries the coefficients (`None` at a column
   the final solve aliased), the standard errors, the mean and the linear predictor at every row, the deviance,
   the dispersion, the iterations and the final iteration's `LeastSquaresFit`. `Glm.nullDeviance` answers the
   null model's deviance (item 6). `LeastSquares.standardErrors phi fit` is the classical standard error D53
   item 8 states, now in one place that the evaluator's least squares and `Glm` both read. `LinAlgError` gains,
   appended, `InvalidResponse of row`, `Separation of row` and `UnsupportedModel of family * link`.
2. **The pairings fitted.** `Gaussian` under `Identity` — one weighted least-squares solve of `y − offset` under
   the prior weights, which is the fixed point (the working response and weight do not depend on `μ`), reported as
   one iteration and byte-equal to `QRAccumulator.addRows` and `LeastSquares.ofAccumulator` over the same rows;
   `Poisson` under `Log` (`V = μ`, canonical, working weight `w·μ`, dispersion one); `Gamma` under `Log`
   (`V = μ²`, working weight exactly the prior weight `w`, the dispersion Pearson's `Σ w·((y − μ)/μ)²` over the
   rows of positive weight divided by `n − rank`, null where that is zero). Every other pairing is
   `UnsupportedModel` — `Gaussian` under `Log` included, although D49 records its start: no phase asks for it,
   and it lands with its own laws when one does. The standard errors are `√(φ · diag((RᵀR)⁻¹))` over the final
   iteration's `R`, the expected information, as R's `summary.glm` reads them; for Gamma under the log link the
   working weights are the prior weights, so that `R` is the prior-weighted design's.
3. **The iteration, in order.** From D49 item 14's start (`μ₀ = y + 0.1` for Poisson, `y` for Gamma;
   `η₀ = ln μ₀`; the previous deviance the deviance of `μ₀`): the working response `z = (η − offset) + (y − μ)/μ`
   and weight are formed row by row; one fresh accumulator folds the rows (`addRows`, row order); the solve gives
   `β`; `η = Σⱼ xᵢⱼβⱼ` over the kept columns `j` ascending from `+0`, then `+ offset` (the order a least-squares
   fitted value is formed in); `μ = exp η`; the deviance is D49 item 18's fold over the rows of positive weight
   (a zero-weight row is not counted, and leaving it out is the same bits as adding its `+0` wherever its unit
   deviance is finite — it is what keeps `0 · ∞` out). A mean is OUT OF RANGE at a row when it is not finite or
   not positive, or when the row has positive weight and its unit deviance is not finite; a scaled row that
   overflows the accumulator is out of range too. Then item 4's halving, then D49 item 17's stopping rule.
4. **Two corrections to D49 items 14 and 16.** (a) *The first iteration has no previous coefficients*, so
   nothing to halve towards: a mean out of range there is refused at once as `Separation`, and its deviance is
   not compared with `μ₀`'s. `μ₀` is the saturated start, not a point of the model — its deviance is at or near
   the minimum any model can reach, so the first iteration's deviance is almost always above it, and D49 item 16
   read literally would halve every fit's first step towards coefficients that do not exist. `μ₀`'s deviance
   still serves as the first stopping test's previous value, as in R. (b) *A rise is one the stopping rule would
   see*: a step is halved when `(dev − dev_prev) / (|dev| + 0.1) ≥ tol` (or when a mean is out of range), the
   test of R's `glm2`, not when `dev > dev_prev`. Near the optimum the deviance moves by a few units in the last
   place either way; a rise of that size is rounding, no halving removes it, and D49 item 16 read literally
   refused converged fits as `NotConverged` — the drawn Poisson law met it at its 116th iteration. A rise below
   the tolerance is exactly what the stopping rule accepts, so the two tests now share one scale. The halving
   itself is D49's: `β ← 0.5·β_old + 0.5·β_new` element by element, at most ten times; a column the new solve
   aliased stays aliased, and one the old solve aliased reads its old value as zero. After the tenth, a mean
   still out of range is `Separation` naming its row, a rise still standing `NotConverged` naming the iteration.
5. **The refusals before any iteration**, in this order, each naming the first offending row: a shape mismatch;
   the pairing; a negative or non-finite weight (`InvalidWeight`); a response outside the family — not finite,
   negative under Poisson, not positive under Gamma (`InvalidResponse`); a non-finite design cell
   (`NotFinite (row, column)`); a non-finite offset (`NotFinite (row, cols)`). `y·ln(y/μ)` is `0` at `y = 0`
   without evaluating the logarithm (D49 item 18).
6. **The null deviance (R's `glm.fit`), and a correction to D53 item 8.** Without an intercept the null mean is
   `g⁻¹(offset)`. With one it is the intercept-only model WITH the offset: under the identity link its closed
   form `μᵢ = m + offsetᵢ`, `m = Σw(y − offset)/Σw`; under the log link with every offset zero the weighted mean
   `Σwy/Σw` (the intercept-only maximum-likelihood mean for both families); under the log link with an offset,
   that model fitted by `Glm.fit` itself, whose refusals it shares. D53 item 8 took `Σwy/Σw` with an intercept
   whatever the offset, which is R's rule only when there is none — R refits the intercept with the offset. The
   evaluator's least squares now uses the identity-link form; without an offset it is the same bits as before
   (`y − 0` is `y`), so no vector moves.
7. **The verb.** `fitResolve` admits `Poisson` and `Gamma` under `Log` beside `Gaussian` under `Identity`; every
   other pairing keeps `UnsupportedFit`. A positive ridge penalty under any family but `Gaussian` is a `FitError`
   (a penalised IRLS is not a fit this version states; a zero penalty folds nothing and is admitted). Per group,
   the bridge's design (D53 items 3 to 6, unchanged) is handed to `Glm.fit` and `Glm.nullDeviance` with the
   control's slots; `InvalidResponse` and `NotConverged` are `FitError`s naming the step's input row or the
   iteration, and `Separation` is an `OverflowError` naming the row — the overflow family Phase 306 started, a
   value that left the float range over finite input. The outputs are D53 item 9's tables: `fitted` is the mean
   `μ`, `residual` the response residual `y − μ`, `iterations` the loop's count, `converged` true on every row
   answered, `sigma` `√φ`, `r2` `1 − deviance / null_deviance`.
8. **Units and types under the log link** (D53 item 10). The linear predictor is the logarithm of a mean, a pure
   number, so a numeric term's coefficient is stated in `1 / unit(x)` and the intercept's and a level's in none;
   `sigma` and the dispersion are pure numbers; a Poisson deviance (`y·ln(y/μ) − (y − μ)`) is in `unit(y)` and a
   Gamma one is a pure number. For a decimal response `fitted` is the mean's exact decimal and `residual` exact
   decimal arithmetic, as D53 item 11 has it, but the deviances are floats: a logarithm is never an exact
   decimal. `fitStatUnits` states all of this once, read by the evaluator and the static walk alike.
9. **Incremental (D7, D49 item 22).** A GLM keeps no state: `Incremental.plan` maintains a `Fit` as before
   (`MaintainFits`), and every refresh fits every group from the pinned start, reported as every group refit; the
   state codec emits no `fit` member for it. Byte-equality with a full evaluation is by construction. A warm start
   is not offered. Re-fitting only the groups a delta touched would need the outputs of every untouched group in
   the state, and on a decoded state the counts would differ from the in-memory ones; that saving is left for a
   phase that measures it.
10. **Laws and vectors.** `LinearAlgebraLaws` gains five laws: `Gaussian` under `Identity` through `Glm.fit` is the
    least-squares fold byte for byte (coefficients, fitted values, deviance, standard errors, one iteration);
    a Poisson fit meets its score equations `Xᵀ W (y − μ) = 0` at convergence within `1e-5` of the terms' scale
    (with an intercept, `Σ w μ̂ = Σ w y`); an offset of `c` on every row is the intercept shifted by `c`; Gamma's
    working weights are the prior weights, exactly, on every iteration (read through an internal observer seam);
    and every refusal fires. The node leg runs them under Fable. `transform-laws.json` gains nine vectors over the
    fit table (iterations 130 to 138): Poisson coefficients, a grouped Poisson summary carrying the iteration
    count, Poisson means under an `ln` offset, Gamma coefficients over a categorical term, a weighted Gamma
    summary, and the four refusals by class (`FitError` twice for a response outside each family, `OverflowError`
    for separation, `FitError` for no convergence); vector 128, which named Poisson under `Log` as
    `UnsupportedFit`, now names Poisson under `Identity` (`fit-unsupported-refused`). The pooled node leg renders
    every fit vector under Fable and the suite holds it equal line by line, so the coefficients and the iteration
    counts are compared byte for byte across the two hosts. `FitTests` holds the evaluator to R's Dobson example
    (`?glm`: the estimates and standard errors within `1e-6`, the deviances, four iterations).

**Rejected.** *A conditioning-based tolerance between hosts* — D49 item 19 stands: every step is pinned, so the
law vectors compare bits. *`Gaussian` under `Log` now* — item 2. *Deviance residuals as `residual`* — R's
default for a GLM, but D53's `residual` is `y − fitted` and a table whose meaning changed with the family would
be read wrongly; a deviance-residual output is its own `FitOutput` case when asked for. *Halving on any rise*
(D49 item 16 as written) — item 4(b). *A penalised IRLS* — item 7. *Per-group state for a GLM* — item 9.

**Corrects.** D49 item 16's rise test and its silence on the first iteration (item 4); D53 item 8's null mean
with an intercept and an offset (item 6); D53 item 1's "the one pairing this version evaluates" (item 7).

**What a consumer does.** A pipeline that named `Poisson` or `Gamma` under `Log` and met `UnsupportedFit` now gets
the fit. A language twin that certifies the transform laws ports `Glm` (items 2 to 6) over its `Elementary`
transcription; the nine new vectors are red on it until it does.

**Tidy.** Reviewed every key file. The evaluator's own standard-error loop is gone, replaced by
`LeastSquares.standardErrors`, which `Glm` reads too; the `t` statistic's loop is one helper read by both the
least-squares and the GLM group. Nothing else in the files touched is made dead or obsolete.

## 2026-11-04 — D54: the evaluator's `Exp`, `Ln`, `Log10` and float-exponent `Pow` run on `Elementary`'s pinned routines behind its published special-value tables, so every column they compute is `Pinned` and byte-identical on every host; D43's host-rounded tier is superseded for the four functions; the `HostRounded` mark, the decision report and the vector tolerance stay as the seam a value declared host-rounded enters by, and no scalar function produces one

**Context.** D43 (Phase 557) made a float-exponent `Pow`, `Exp`, `Ln` and `Log10` host-rounded: the special-value
table first, then the host's own maths library, deterministic within one host and within two ulps across hosts,
every column reading one marked `HostRounded`, every discrete decision such a value reaches reported, and the
transform law vectors comparing those columns in ulps (D44). D52 (Phase 593) built the pinned replacement —
`Fuaran.Compute.LinearAlgebra.Elementary`'s `exp`, `ln`, `log10` and `pow`, one reduction and one polynomial each
over the correctly rounded basic operations, within one ulp of the true value and byte-identical on .NET and node —
and published D43's table as data (`SpecialValue`, `expTable`, `lnTable`, `powTable`), but left the evaluator on
the host. This entry moves it (Phase 594; operator ruling D6 = B, 2026-11-01: pin the functions before the GLM).

**Premise check at take, against `main` at `ae3707c`.** `Elementary` is on `main` with the four routines and the
three tables, and `Fuaran.Compute.DataFrame` already references `Fuaran.Compute.LinearAlgebra` (Phase 592). What read
`HostRounded` or the host functions: the evaluator's `applyScalar` arms for the four functions, through D43's own
copy of the table in `DataFrame.fs` (`PowSpecial`, `powSpecial`, `powFloat`, `expHost`, `logHost` — the copy D52
said this phase deletes); the static walk's `hostRoundedOperand`, which named the four functions as sources of the
mark (`integerExponent` deciding a power), and through it `exprExactness`, every verb's carried mark and
`hostRoundedDecisions`; the four transform vectors carrying `expected.tolerance` (`power-special-values` at ten
columns, `power-float-exponent`, `power-exp`, `power-ln-log10`); `proofs/Pipeline.fst` section 14 and the oracle
differential, which compared the extracted tables to the `DataFrame.fs` copy; the tests asserting those marks. No
native kernel computes any of the four (`Kernels.fs` has none), and the compiled derive and the incremental seam
reach them only through `applyScalar`. The shard's "`SchemaWalk.exactnessOf` reports these columns `Exact`" names
a case that does not exist: the pinned case is `Exactness.Pinned`, and that is what they report.

**Decision.**

1. **The evaluator reads `Elementary`, table first.** A float-exponent `Pow` answers from `Elementary.powTable`,
   `Exp` from `expTable`, `Ln` and `Log10` from `lnTable`; a NaN is D16's one NaN, the table's value is that value,
   `Undefined` is `Null` (as `Div` by zero is), and every input the table leaves (`Compute`) is
   `Elementary.pow` / `exp` / `ln` / `log10`. One helper (`DataFrame.ofTable`) reads all three tables, and
   `applyScalar` stays the one implementation the row evaluator, the compiled derive and the incremental seam share.
   D43's copy of the table in `DataFrame.fs` is deleted: there is one table, the published one. The integer-exponent
   routine (D43 item 2) is unchanged.
2. **The four functions are `Pinned`.** The static walk no longer names them as sources of the mark, and
   `integerExponent` (which existed only to decide a power's mark) is removed; a column computed by any of them over
   pinned inputs is `Pinned`, and a pipeline over pinned input knowledge reports no host-rounded decision. Every
   other rule of D43 items 5 and (c) is unchanged — the lineage rules, which verbs carry the mark, what counts as a
   decision.
3. **What remains `HostRounded`.** No scalar function of the evaluator. The mark enters only where a caller
   declares it: an input column marked so in the knowledge handed to `SchemaWalk.ofPipelineFrom` /
   `hostRoundedDecisionsFrom` (`ColumnKnowledge.withExactness`), and a call the walk cannot expand (Phase 596's
   conservative reading, whose pipeline the evaluator refuses anyway). That is the seam Phase 599's registered host
   functions stand on: a host function declares its own exactness, and one declared `HostRounded` reaches the walk,
   the decision report and a vector's tolerance through exactly the machinery D43 built. So the `Exactness` type, the
   decision report, `LawVectorExport.hostRoundedUlps`, the tolerance member and `VectorTolerance` (D44) all stay; the
   suite now proves them over a declared mark rather than over a transcendental.
4. **The corpus.** The four power vectors lose `expected.tolerance` and are compared byte for byte; their recorded
   tables are unchanged, because the pinned routines answer the same bits .NET's library did at every drawn input
   (the regenerated file differs from the committed one only in the four tolerance members and the description). No
   vector of `transform-laws.json` carries a tolerance; the member, and its specification in the file's description,
   stay for the vector whose walk marks a declared host-rounded column. The file's `iterations` and every other
   vector are unchanged.
5. **The proof model.** `proofs/Pipeline.fst` section 14 keeps its shape: the tables concretely over a classification
   of the float, and the computed arm a PARAMETER. What the parameter stands for changes — the pinned routine, not
   the host's library — so the section's header and the ladder row `pipeline-power-tables-before-host` say so, and
   three lemmas are added (`pow_float_pinned`, `exp_float_pinned`, `log_float_pinned`): two hosts whose routines
   agree at an input answer the same cell there. Their premise is the ladder row 593 recorded,
   `elementary-functions-pinned-by-transcription`, so cross-host byte identity now reaches every input — the table's
   by `*_table_before_host`, the rest by the transcription premise — and the extraction is unchanged (lemmas are
   erased). The oracle differential instantiates the parameter at `Elementary` and holds the model's cell to the
   evaluator's (`DataFrame.powFloat`, `expCell`, `logCell`) bit for bit over the special-value grid.
6. **The twins follow.** `fuaran-ts` and every other host that certifies the transform laws must replace its host
   `Math.pow` / `exp` / `log` / `log10` behind the table with a transcription of `Elementary` (its own vectors in
   `linear-algebra-laws.json` certify the transcription) and adopt the rewritten corpus, where the four vectors are
   now byte vectors. The follow-up is a language-side phase (the precedent is fuaran#2400); a Compute pin raise in a host re-emits the Compute-produced corpus files in the same change-set.
7. **Classification.** BREAKING, `behaviour`: no public surface moves (every changed function is internal; the
   regenerated baselines are byte-identical), but what a consumer receives does — a column the walk marked
   `HostRounded` is now `Pinned`, `hostRoundedDecisions` reports nothing for a pipeline it used to report on, the
   four vectors lose a member, and a value off the special-value table may differ in its last bit from the host
   library's on some input. It rides the `0.43.0` draft, which already carries breaking moves.

**Rejected.** *Removing the `HostRounded` mark, the decision report and the tolerance now that nothing produces
one* — the smaller tree today, but Phase 599 reintroduces a producer (a registered host function declaring its
exactness) with a class below this one (`Opaque`), and deleting the machinery a phase before it is needed again
would be churn in a published surface twice; the mark is now proved over a declared input, so it is live code, not
dead. *Keeping a host-rounded fallback behind a flag* (the host's library for speed, the pinned routine on demand) —
two answers for one expression is exactly what D52 was built to end, and the measured cost of the pinned routines
(D52) does not justify it. *Calling `Elementary.exp` and friends directly, without reading the table in the
evaluator* — they answer NaN where the table says `Undefined`, and the evaluator's answer there is `Null`; reading
the table first keeps the `Null` rule in one visible place, and `Compute` is the only arm that calls a routine.

**Supersedes** D43's host-rounded tier for `Exp`, `Ln`, `Log10` and a float-exponent `Pow` — item 3's "then the
host's" arms, ruling (a)'s 2 ulp bound as it applies to them, item 5's list of functions that mark a column, item 6's
tolerance on the power vectors, item 7's reading of the parameter as the host's function, item 8's measurement as
the evaluator's guarantee, and item 9's host-scoped digests for these columns. D43's ruling (b), ruling (c), items 1,
2 and 4 and the mark's lineage rules stand. D43 carries the status line.

## 2026-11-04 — D53: a pipeline fits a regression with one `Fit` step — `FitSpec` names the response, the terms as expressions, the groups, the weights, an offset, a ridge penalty, the family and link and the table it answers; the frame-to-matrix bridge, units, the schema walk, the wire tag and the incremental state live in `DataFrame` while the algebra stays in `LinearAlgebra`; ordinary least squares is the one pairing evaluated, every other refused by name; a decimal response is estimated through a recorded cast and every output stated against it is decimal; an append to a group continues its stored factorization byte for byte

**Context.** The plan's S3 (`fuaran-compute-linear-algebra-and-glm.md` §2.4, operator rulings D4, D5 and D7 of
2026-11-01): a design matrix could be assembled with `Derive` and `Pivot`, but nothing in a pipeline could fit a
model, so every regression left the algebra — not data, not on the wire, not incremental, not law-checked. Phase
591 shipped the kernel (`QRAccumulator`, `LeastSquares`, `LinAlgWire`, D51). The premise check at take found all
three on `main` as D51 states, and the streamed `GroupBy` slots (Phases 323 / 344) as the plan assumed — one
pass over the rows into per-slot state in first-appearance order, with a deferral to the member lists where a
stream cannot answer; the fit takes the same grouping (`keySlots`) and keeps member lists outright, because a
factorization folds its rows in order and has no stream form cheaper than its own fold.

**Decided.**

1. **One verb, the family and link as data (plan D5).** `Transform.Fit of FitSpec`, appended last to the union.
   `FitFamily` = `Gaussian | Poisson | Gamma | Tweedie of power | QuasiPoisson`; `FitLink` = `Identity | Log`.
   `Gaussian` under `Identity` is ordinary least squares and is the one pairing this version evaluates; every
   other is typed, carried on the wire and refused when the step resolves — before any row is read — as the new
   `EvalError.UnsupportedFit (family, link)`, both named by their wire spellings. Phase 595 lifts the refusal for
   the log-link families; a pairing no later phase evaluates keeps it, which is why the case names the pair
   rather than "not yet".
2. **The slots.** `Ridge : Slot<float> option` and `Control = { MaxIter : Slot<int>; Tol : Slot<float> }`
   resolve against the evaluation env as every slot does (a float slot takes a float or an int cell). They are
   checked for every family, OLS included, so a pipeline is refused for an out-of-range control whatever it
   names: the penalty finite and `≥ 0`, `MaxIter ≥ 1`, `Tol` finite and `> 0`. The default control is D49 item
   13's: 25 iterations, `1e-8`. Ordinary least squares solves in one pass: its summary reports `iterations = 1`
   and `converged = true`. Every slot's param is reported by `Transform.paramsOf` and `slotParamsOf`, and bound
   by `Transform.substitute`.
3. **The bridge (D49 item 13).** The response is a column, int, float or decimal; each predictor a named
   `ColExpr` evaluated into a column by the derive's own typing and unit check under the label `fit term '<name>'`
   (so an interaction is a `Mul`, a polynomial an integer `Pow`, and both are exact). A row is in the design
   exactly when none of the response, a term, the offset or the weight is null: listwise deletion. A row in the
   design whose response, term or offset is not finite, or whose weight is negative or not finite, is refused as
   `FitError`, naming the step's input row and the column. A numeric term (int, float, decimal) is one design
   column, read as the cast to float reads it; a string or boolean term is categorical; any other type is a
   `TypeError`.
4. **Categorical coding.** A categorical term's levels are its distinct values over the rows in the design —
   across all groups, so every group carries the same terms — in the cell order (strings ordinal, `false` before
   `true`). The first level is the reference and is dropped (treatment coding); each other level is one 0/1
   column named `term[level]`. Without an intercept the FIRST categorical term keeps every level, as R's
   treatment coding does, so the model spans the same space either way. A level absent from a group is a column
   of zeros there and is aliased (item 6).
5. **The coefficients' order and names.** `(Intercept)` first (R's spelling), then the terms in the order
   written, each categorical term expanded in level order. A name repeated — two terms, a term named
   `(Intercept)`, or a coded name colliding with a written one — is refused as `FitError`; so is a design with no
   column.
6. **The kernel (plan D4).** Per group, the group's rows in frame order are folded into a fresh
   `QRAccumulator` by `addRows` (byte-equal to `addRow` row by row, D51), with the response minus the offset as
   `y` and the prior weight as the weight (D49 item 9: scaled by `√w`, a zero weight in the design and not
   counted). `LeastSquares.ofAccumulator` solves it with D49 item 11's rank rule: an aliased column keeps its row
   in the coefficient table — named, never dropped — with null `estimate`, `std_error` and `t`.
7. **Ridge.** A penalty `λ > 0` folds one row per non-intercept column, `√λ` at that column and zero elsewhere,
   response zero, weight one, in column order, AFTER the data rows. The intercept is not penalised: a penalised
   intercept makes the fit depend on where the response's zero is, which no reader of a ridge regression expects
   (glmnet and scikit-learn agree); the plan's "p rows of √λ·I" is read as one row per penalised column. A zero
   penalty folds nothing, so it is byte-equal to no ridge (a row of zeros would count towards `n` and the rank
   tolerance). The data accumulator is kept without the penalty rows, so an append continues it (item 12).
8. **The statistics, each a pinned left-to-right fold in member order.** `fitted = Σⱼ xⱼ·βⱼ` over the kept
   columns `j` ascending, then `+ offset`; `residual = y − fitted`; `deviance = Σ w·(r·r)`; the null model's mean
   is R's `glm.fit` rule — with an intercept `Σwy / Σw`, without one the offset — and `null_deviance = Σ w·(y −
   μ₀)²`. `n` is the accumulator's count (rows of positive weight), `rank` the kept columns, `df = max(0, n −
   rank)`; `dispersion = deviance / df` and `sigma = √dispersion`, null where `df = 0`; `std_errorⱼ =
   √(dispersion · Σₖ R⁻¹[t,k]²)` over the kept column's row `t` of `R⁻¹`, `k` ascending; `t = estimate /
   std_error`, null where the error is zero or null; `r2 = 1 − deviance / null_deviance`, null where the null
   deviance or the weight total is zero. Under a ridge penalty the deviance is the data rows' alone, not the
   penalised objective.
9. **The outputs, closed schemas.** `Coefficients`: the group keys, `term`, `estimate`, `std_error`, `t`, `unit`
   — one row per group and term, groups in first-appearance order (the `GroupBy` rule), terms in coefficient
   order. `Summary`: the keys, `n`, `df`, `sigma`, `r2`, `rank`, `deviance`, `null_deviance`, `dispersion`,
   `iterations`, `converged` — one row per group. `Fitted` / `Residuals`: the step's input rows in order with
   `fitted` / `residual` appended (null at a row not in the design; a zero-weight row has both); an input already
   carrying that name is refused as `FitError`. Groups are formed over the rows in the design, so a frame with
   none answers no group. Every schema is decided before any row is read, which is what lets
   `PipelineQueryRegistry.register` admit a registered regression query; the static walk states each, with the
   units of item 10, and marks every column `Pinned` where the response, keys, weights and terms are — the fit
   adds no host-rounded operation (`+ − × ÷ √` only, D49).
10. **Units (D29).** A coefficient's unit is `unit(y) / unit(xⱼ)` through Core's algebra, the intercept's and a
    categorical column's `unit(y)`. One column of a tidy table cannot carry one unit per row, so the coefficient
    table carries it as data: `unit`, the canonical text, null where none is stated. `sigma`, `fitted` and
    `residual` carry `unit(y)` as field metadata; `deviance`, `null_deviance` and `dispersion` its square.
11. **A decimal response (plan §7 Q3, operator ruling 2026-11-03).** The estimation reads the response through
    `FitSpec.estimationResponse spec = Cast(FloatType, Col response)` — the evaluator's own cast, the recorded
    `Cast` the pipeline and its replay show, for every response type (identity on a float, exact on an int).
    Every output computed AGAINST the response then uses the decimal column: **`fitted`** is the float estimate's
    exact decimal (the float's own digits, the one place an approximation enters a decimal),
    **`residual`** = `y − fitted` in exact decimal arithmetic, **`deviance`** and **`null_deviance`** are exact
    decimal sums of `w · r²` (the weight and the null mean entering as their exact decimals). The statistics
    that are not amounts in the response unit — `estimate`, `std_error`, `t`, `sigma`, `dispersion` and `r2`
    (the last two read off the decimal deviances, converted once) — stay floats. So a money figure is never
    reported through a float: what is stated in the response's unit is decimal, and what is a statistic says so
    by its type.
12. **Incremental (plan D7).** `Incremental.plan` classifies a `Fit` as `MaintainFits groupBy` (a new case,
    appended) when it is the LAST step, after no aggregating step and no window; otherwise it declines by name —
    `AggregateStepNotLast "fit"` (produced again, for a fit only), `AggregateStepRepeated "fit"` after a
    `GroupBy`, and the new `FitInputNotMaintained "window"` after a window, whose column moves for a row whose
    neighbour moved. The strategy is `RowLocalThenGroups`, the maintained-grouping strategy, so a refresh
    reports `GroupsRecomputed (rows, groupsRefit)`. The state keeps, per group token, the members' row tokens in
    order and the DATA accumulator. A group whose prior members are a prefix of its members now — each one
    stable (not named by the delta) and in the prior order — continues from its accumulator by the appended rows
    alone: **an append refresh is byte-equal to a full evaluation**, because the fold is sequential and
    `addRows` onto the prior state is the fold of the whole. Any other group — an edit, a delete, a reorder — is
    refolded from its first row (downdating is unstable), the rule float `Sum` follows; a design whose layout
    moved (a new level, say) refolds every group. Warm starts are never certified (D7). The terms are
    re-evaluated over the whole input on every refresh and the footprint charges them, as the reference does —
    what the state saves is the factorization. The state codec carries it as a `fit` member — the layout, and
    per group its token, its members as indices into the state's row tokens, and its accumulator through
    `LinAlgWire` (IEEE-754 bit patterns), emitted only where a fit left state, so every other state's bytes are
    unchanged and a decoded fit continues to the same bits.
13. **Wire.** `{"$type":"fit","response":…,"predictors":[{"name":…,"expr":…},…],"intercept":…,"groupBy":[…],
    "family":"gaussian"|"poisson"|"gamma"|"tweedie"|"quasiPoisson","power":…?,"link":"identity"|"log",
    "control":{"maxIter":<slot>,"tol":<slot>},"output":"coefficients"|"summary"|"fitted"|"residuals",
    "weights":…?,"offset":…?,"ridge":<slot>?}` — `power` for a Tweedie family only; the three optional members
    only where the spec carries them.
14. **Laws and vectors.** Thirteen transform vectors over a fixed ten-row table (the `fit-` shapes): ordinary
    least squares, a grouped summary, a categorical term, an interaction and a polynomial, an aliased column, two
    terms collinear to `1e-7`, weights with a zero beside a ridge penalty, an offset without an intercept, fitted
    values over a listwise deletion, a decimal response's residuals and summary, and two refusals named by class
    (`UnsupportedFit`, `FitError`). They are pinned bytes, held on .NET by the vector tests and under node by the
    pooled node leg, which renders the same vectors' results through Fable and compares them line for line.

**Rejected.** *A `Predict` verb in this phase* — the plan pairs it with the fit (§2.4) but no task here names it;
it is a step of its own over a fitted table. *Levels per group* — each group would carry its own terms and
reference level, the coefficient tables of two groups would not line up, and a level seen first in one group
would change another's coding; global levels cost an incremental refit of every group when a new level appears,
which is the honest price. *A `unit` per column of the coefficient table, a wide table, one column per term* —
the term set is data (a categorical term's levels), so the schema would not be closed and registration would
refuse it. *Penalising the intercept* — item 7. *A float deviance for a decimal response* — the operator's
ruling. *Maintaining a fit after a sort or a limit by row values* — a stable row is unchanged and the member
comparison is in order, so a sort ahead of a fit is maintained already; only a window moves values of a stable
row, and storing every row's design to compare would make the state O(n·p).

**Supersedes nothing.** `AggregateStepNotLast`'s "no longer produced" (Phase 202) is narrowed: it is produced
again, for a `Fit` only.

## 2026-11-04 — D52: `exp`, `ln`, `log10` and the float-exponent `pow` are pinned in `Fuaran.Compute.LinearAlgebra.Elementary` — one reduction and one polynomial each, `+ − × ÷` and comparisons over a table of the powers of two, no fused multiply-add and no host mathematics call, behind D43's special-value table published as data; the faithful-pinned build is chosen over a correctly rounded one on the measurement, which puts the pinned routines within one ulp of the true value and byte-identical on .NET and node, and the correctly rounded candidate at five to twenty-five times the cost with a multiprecision library in every twin

**Context.** D43 (Phase 557) made the transcendentals host-rounded: deterministic within one host, within two ulps
across hosts, every column that reads one marked `HostRounded`. The plan for the linear-algebra chain
(`fuaran-core-compute`'s S6; operator ruling D6 = B, 2026-11-01) says that is not enough for a generalised linear
model: an IRLS loop passes the differences through every iteration, the condition number amplifies them, and the
convergence test is a discrete decision over them, so two hosts can stop at different iterations. Byte identity
needs a *pinned* algorithm, not necessarily a *correctly rounded* one, and the plan's §7 question 1 asked for a
measurement before the build: the faithful-pinned candidate against the correctly rounded one, in cost per value
on .NET and under node, in error against a high-precision reference, and in lines per twin. This entry records
the measurement, the choice it makes, and the build. The premise check at take found D43's table in
`DataFrame.fs` as the entry states, Phase 559's tolerance vectors present (`e`, `l`, `l10`, `p` and the table's
rows, each at two ulps), and Phases 590 and 591 on `main`.

**Measured, on 2026-11-04 (Intel Core i7-8650U, Windows 11; .NET 10 Release, x64 native; node v22.20.0 over
Fable 5.0.0).** The harness is the node timing program's `elementary` family
(`benchmarks/Fuaran.Core.Compute.Benchmarks/ElementaryBench.fs`), the same program on both hosts; the write-up
is `benchmarks/results/2026-11-04-i7-8650u-phase-593.md` with the reference checker beside it. The sample is a
seeded draw at 593 through the pinned Park–Miller generator (the same values on every host): `exp` over 3,320
inputs (uniform across the computed range, with clusters round zero, the half-ln2 reduction boundary and both
edges), `ln` and `log10` over 3,107 (log-uniform across the positive doubles, a cluster round one, the subnormals
and the extremes), `pow` over 3,847 pairs (bases across two hundred binary decades and round one, exponents to
`±10¹⁶`, negative bases under integer exponents, every pair with a finite non-zero answer) — 13,381 values in all.
The reference is Python's `decimal` at eighty digits (`exp`, `ln`, `log10`, and `pow` as `exp(y · ln x)`) rounded
once to the nearest double; the distance is the difference of ordered bit patterns.

1. **The faithful-pinned candidate** (the build below): the worst distance from the reference is **one ulp on
   every function**, with the mean at 0.093 (`exp`), 0.010 (`ln`), 0.076 (`log10`) and 0.103 (`pow`) — off by
   one on 310, 31, 237 and 397 of the samples respectively, never by two. Cost per value, .NET: **18, 66, 91
   and 204 ns**; node: **51, 246, 423 and 487 ns**. The .NET and node captures agree on **all 13,381 lines,
   byte for byte.** Size: `Elementary.fs` is 436 lines with its comments, of which the routines are about
   forty-five (`exp`), fifty (`ln`), fifteen (`log10`) and a hundred and ten (`pow`, sixty of them the
   double-double helpers), plus the two-thousand-entry table of the powers of two a twin builds in a loop; the
   constants are fdlibm's twenty-one published coefficients and six split constants.
2. **The correctly rounded candidate** (a Ziv two-phase prototype kept in the harness and nowhere else: the same
   reductions carried in double-double arithmetic to about `2⁻⁹⁵`, a rounding test on the result, and a
   320-bit fixed-point `BigInteger` slow path where the test cannot decide or the double-double scaling cannot
   reach). Cost per value, .NET: 544 (`ln`), 477 (`log10`) and 1,135 ns (`pow`) on the fast path alone — 8, 5
   and 6 times the pinned build — and 4,745 ns for `exp`, because 651 of its 3,320 samples lie where the fast
   path withdraws (a subnormal or near-overflow result) and pay the slow path; node: 9,339, 5,633, 5,230 and
   18,195 ns. The slow path alone costs **22 to 68 µs** per value on .NET and 34 to 65 µs under node; the
   rounding test sent 3 `exp`, 11 `ln` and no `log10` or `pow` samples to it. Accuracy: `exp` is at zero ulps on
   every sample, but `ln`, `log10` and `pow` are off by one on 1, 9 and 84 — the prototype as written is NOT
   yet correctly rounded (the sloppy double-double sum and the `2⁻⁹⁵` bound it assumes are the likely cause),
   and making it so is more of exactly the work that distinguishes the candidates. Size: about three hundred
   lines beyond the pinned build, plus a multiprecision integer in every twin — JavaScript, Python and .NET have
   one; C, Rust, Go, Swift and Kotlin take a dependency or write one.

**The falsifiers, named.** A sample at which the pinned routine sits two or more ulps from the reference; a line
on which the two hosts' captures differ. The instrument sees both: the correctly rounded column differs from the
pinned one on hundreds of lines (every off-by-one), the first run of the prototype was caught at 256 ulps on
`ln` just below one (a cancellation its `ln` did not fold away, fixed before the figures above), and the first
run of the harness measured unoptimised .NET code and a BigInt checksum under node, each corrected and each named
in the harness so the next reader does not repeat it. The sample and the checker are reproducible from the seed
and the recipe in the results file.

**Decision.**

1. **The faithful-pinned build ships, and the correctly rounded one is declined on the measurement.** The
   plan's rule was that the choice is the measurement's unless the correctly rounded cost proves out of
   proportion, in which case it is surfaced to the operator; it has, and the operator's standing instruction
   for this chain of phases is to take the long-term option and record it here. The pinned build answers the
   question D6 asked — the same bits on every host — within one ulp of the truth, at the cost of the host's own
   libm; the correctly rounded one would add agreement with other correctly rounded libraries, which nothing
   downstream reads, at five to twenty-five times the cost, a slow path of tens of microseconds, a prototype that
   is not yet correct, and a multiprecision dependency in five twins. The door stays open as D43's did: a
   correctly rounded routine is pinned by definition, and a host that ships one changes nothing here but the
   `ulps` cells.
2. **The module `Elementary`, in `Fuaran.Compute.LinearAlgebra`** (D49 item 20's boundary: FSharp.Core and the
   substrate's column; Fable-clean): `exp`, `ln`, `log10`, `pow`, and the table functions `expTable`, `lnTable`
   and `powTable`, each `Pinned` in `Operations.exactness`. The arithmetic is `+ − × ÷` and comparisons; the
   one further thing read is the binary64 format itself, as a **table of `2ᵏ` for `k` in `−1074 .. 1023` built
   by exact doubling and halving** at module load: a float's exponent is found by binary search over it and
   its mantissa by one exact product, and `y · 2ᵏ` is one exact product into the normal range, or two where the
   answer is subnormal or beyond `2¹⁰²³` (fdlibm's `scalbn` route, the second product rounding once). No bit
   pattern is read or written; a twin transcribes a loop, not a layout. The square root `pow(x, ½)` takes is
   the F# operator over the correctly rounded root the linear-algebra premise names. A source-reading test holds
   `Elementary.fs` to no `Math.*` call and no `**`.
3. **The algorithms.** `exp` is fdlibm's `e_exp.c`: `k` the nearest integer to `x / ln 2` (rounding half away
   from zero), `hi = x − k · ln2Hi` exact (the high part carries twenty-one trailing zeros), `lo = k · ln2Lo`,
   the degree-five polynomial for `x − expm1(x)` over `x = hi − lo`, the compensated assembly
   `1 − ((lo − x·c / (2 − c)) − hi)`, then `2ᵏ`; `|x| < 2⁻²⁸` answers `1 + x`. Two simplifications of fdlibm,
   both deliberate: `k` is formed by the one formula at every magnitude (fdlibm hand-sets `k = ±1` in the band
   below `1.5 · ln 2`, which the formula answers the same except at the band's edge, where either reduction is
   valid), and `ln` takes the `hfsq` assembly throughout (fdlibm's narrow band without it is not reproduced).
   `ln` is `e_log.c`: `x = 2ᵏ · (1 + f)` with `1 + f` folded into `[√2/2, √2)`, `s = f / (2 + f)`, the two
   even polynomials over `s²`, and `k · ln2Hi − ((hfsq − (s · (hfsq + R) + k · ln2Lo)) − f)`, with the
   `|f| < 2⁻²⁰` short form. `log10` is `e_log10.c` over the same `ln` core: the mantissa in `[1, 2)` or, under
   a negative exponent, `[½, 1)` with the exponent raised by one so the two terms carry one sign, then
   `(n · log10(2)_lo + ln(m) / ln 10) + n · log10(2)_hi`. `pow` is `exp(y · ln x)` with **`ln x` and the
   product in double-double** (Dekker's error-free transformations — `twoSum`, Veltkamp's split, `twoProd`):
   `ln(1 + f) = 2s · (1 + z · (⅓ + z · (⅕ + T)))` with `s` and `z = s²` double-double and the tail
   `T = z/7 + … + z¹⁰/25` plain, where it weighs below `2⁻⁶⁶` of the whole; `k · ln 2` from the split
   constant; then `y · ln x` as `twoProd` plus `y · ln_lo`, so the `exp` core receives the part a plain product
   would have rounded away — which is why its one-ulp bound carries over to `pow`, and why `pow` near one under
   an exponent of `10¹⁶` is within one ulp. The split needs `|y| < 2⁹⁹⁶`; a plain estimate of `y · ln x`
   answers the overflow and underflow first, so the split is never reached beyond it. Before the core, IEEE-754's
   rules for an infinite or zero operand and for a negative base under an integer exponent (the sign by the
   exponent's parity, the parity found with `+` and `−` through the `2⁵²` rounding trick, never a `floor`),
   and **four exact cases**, `y = 1`, `−1`, `2` and `½` — the operand, its reciprocal, its square and its root,
   each one correctly rounded operation and so the same bits as the spelling a consumer would write with `Mul`,
   `Div` or `Sqrt`, and as D43's integer routine answers `x¹` and `x²`.
4. **D43's special-value table is kept exactly and published as data.** `SpecialValue` is
   `NaN | Value of float | Undefined | Compute`, and `expTable`, `lnTable` and `powTable` answer it clause for
   clause as `DataFrame.powSpecial`, `expHost` and `logHost` do today, including the two edges of `exp`
   (`709.782712893384` and `−745.1332191019412` — D43's literals; the second is one ulp above fdlibm's own
   underflow threshold and is kept as D43 wrote it, since the table is D43's), `pow(1, NaN)` and `pow(NaN, 0)`
   as NaN, and a zero base under a negative exponent and a negative base under a non-integer exponent as
   `Undefined`. The functions answer **NaN where the table says `Undefined`** — `ln 0` is NaN here, not
   `−Inf` — because the table, not the function, is the contract for those inputs: the evaluator reads the
   table first and answers `Null`, as it does today, and a consumer wanting IEEE's `−Inf` is asking a question
   the table has already answered otherwise. The evaluator's move (Phase 594) is the one that deletes the
   `DataFrame.fs` copies and reads these.
5. **The vectors carry the truth beside the answer.** The elementary vectors of
   `conformance/laws/linear-algebra-laws.json` answer `{ verdict: ok, value, table }`, and every vector the
   routine computed also carries `reference` — the correctly rounded bits of the true value, authored from the
   eighty-digit computation — and `ulps`, the package's distance from them, which is at most one on every such
   vector and is itself part of the document a host must reproduce byte for byte. A host that ports the
   routines therefore checks two things from one file: that its bits are ours, and how far ours are from the
   truth. A vector the table answers carries no reference. The suite also holds, as a go-red, that the host's
   own libm answers other bits on at least one vector, so the byte comparison is known to see a port that calls
   the host instead of transcribing.
6. **Laws.** `LinearAlgebraLaws.laws` gains four: every table row and IEEE edge answered as stated (the canonical
   NaN included); `pow`'s four exact cases and the sign rule byte for byte; `ln (exp x)` within `4u · (|x| + 1)`
   of `x` and `exp (ln y)` within `4u · y · (|ln y| + 1)` of `y`; and the functional equations
   (`exp(a + b)` against the product, `ln(xy)` against the sum, `pow(x, y) · pow(x, −y)` against one, `log10`
   against `ln / ln 10`) within the bounds a one-ulp routine admits. The byte law across hosts is the vector
   file under the node leg, as for every other operation of the package.
7. **The claims ladder** gains the assumed premise `elementary-functions-pinned-by-transcription`: byte identity
   rests on the linear-algebra premise and on exact transcription, never on a host's own `exp` or `log`; the
   accuracy claim is the measurement above, a faithful bound and never a theorem. `ProofsLadderTests` requires
   it beside the two arithmetic premises.
8. **What Phase 594 does**, stated here so the two halves read as one: the evaluator's `Exp`, `Ln`, `Log10` and
   float-exponent `Pow` move onto `Elementary` behind the published table, the `HostRounded` mark and D43's
   two-ulp tolerance come off those columns, and the transform vectors that carry `expected.tolerance` lose
   it — the corpus change every twin must follow. The integer-exponent routine (D43 item 2) is untouched.

**Rejected.** *A port of fdlibm's `e_pow.c`* — three hundred lines of word-splitting over the mantissa's bit
pattern, whose accuracy argument is tied to that splitting; the double-double route reuses the `exp` core,
rests on Dekker's lemmas rather than on a layout, and measured within one ulp. *A table of logarithms at 128
centres* (musl's shape) — fewer operations per `pow`, at 384 further constants in every twin, and the band round
one would still need the table-free arm; declined while `pow` costs 0.2 µs. *Reading the exponent from the bit
pattern* — exact and cheap, but it makes the claim "`+ − × ÷` and a table" false and asks every twin to carry a
layout as well as a loop; the binary search costs eleven comparisons. *Answering `−Inf` for `ln 0`* — item 4.
*Correct rounding* — item 1, on the measurement.

**Supersedes nothing yet.** D43 stands in full until Phase 594 moves the evaluator; this entry adds the routines
and the table's data form beside it.

## 2026-11-04 — D51: least squares is a streaming Givens QR over an immutable `QRAccumulator` whose state is `R` beside `Qᵀy`, row by row; a weight is refused by its own case, `InvalidWeight`; an overflowing state is refused when it is read, not as it is built; an aliased column is removed and the rest re-triangularised in column order; the batch Householder factorizations are pinned in their own order and held to the streaming one by tolerance; the accumulator and a matrix travel by IEEE-754 bit patterns through `Fuaran.Core.Wire`

**Context.** Phase 591, stage S2 of the linear-algebra plan: the accumulator, the batch and pivoted
factorizations, the solve, the wire codec the incremental state needs, the native rotation and the benchmarks.
D49 items 8 to 12 fix the arithmetic; this entry rules the calls the shard left to the phase and the ones
implementing D49 surfaced. The operator's standing instruction of 2026-11-04 (work unsupervised, take the
long-term option, record the alternatives) applies to every item.

**Premise check** (on `7da5005`). 590's `Matrix`, `Givens.make` / `apply`, the checked `UpperTriangular` and both
triangular solves are on main as D50 describes. The shard cites "D48's Givens convention and rank tolerance";
those are D49 items 8 and 11 (589 took D49, 596 D48), and the code agrees with item 8 exactly: the identity when
`b = 0` of either sign, else `r = norm2 [a; b]`, `c = a / r`, `s = b / r`. No rank test existed in 590 (it is a
factorization's, D50 item 5), so item 11 is implemented here for the first time, as written. The package took
Column alone; D50 item 3 named Wire as the dependency a codec would add, under D49 item 20's bound.

**Ruled.**

1. **The accumulator's shape.** `QRAccumulator` is opaque and immutable: `p` rows of `p + 1` floats, row `j` of
   `R` beside `(Qᵀy)[j]`, so rotating a design row into row `j` and the response into `Qᵀy` is ONE contiguous
   pass of pairs — which is what lets the native member be a SIMD loop at all, since `R` stored column-major
   would put a row's pairs `p` apart. `addRow x y weight` and `addRows x y weights` answer a new state (the
   batch copies the state once, not per row); `r` (a plain `Matrix`), `qty`, `rss`, `count` and `cols` read it.
   The residual sum of squares is the fold, in row order from `+0.0`, of the square of the residual each row
   leaves in the response slot after its `p` rotations — so σ̂² needs no second pass. Alternatives considered:
   a mutable accumulator with an explicit copy (rejected: the package's public surface is immutable, D49 item
   20); a separate unweighted `addRow` (rejected: a weight of `1` scales by exactly `1`, so one entry point with
   the weight explicit answers the same bits and halves the surface).
2. **The diagonal is replaced by the radius, and the zeroed cell is set to `+0.0`** — D49 item 8 read
   literally, not `apply` run on the pivot pair (whose first output is `r` only to within rounding). The radius
   is computed by an allocation-free `norm2Pair`, held byte-equal to `norm2 [| a; b |]` by a law, and
   `Givens.make` now calls it too.
3. **A weight has its own refusal.** `LinAlgError` gains `InvalidWeight of row`, for a negative, NaN or
   infinite prior weight (D49 item 9). `NotFinite` was considered and rejected: a negative weight is finite, and
   its column would be a fiction. A row is checked before it is rotated, in this order: the weight, then the
   design cells ascending (`NotFinite (row, column)`), then the response as column `p`. `addRows` names the
   row's index in `x` and folds nothing; `addRow` names row `0`. A weight of zero, of either sign, is accepted,
   rotates nothing and is not counted. The case is appended last, so no existing tag moves; the package is new
   in this draft, so no released consumer matches on it.
4. **An overflowing state is refused when it is read.** Finite rows can still leave an infinity in `R` (a
   column norm past `Double.MaxValue`). Checking every touched entry per row would cost a pass as long as the
   rotation's; instead `LeastSquares.ofAccumulator` refuses `NotFinite` naming the first non-finite entry of
   `[R | Qᵀy]` in column-major order, and `r` is a plain matrix that `UpperTriangular.ofMatrix` checks. `rss`
   may be `+∞` honestly (a sum of squares past `1.3e154`).
5. **Aliasing in the streaming solve** (D49 item 11 made concrete). In column order, a column whose
   `|R[j,j]| ≤ τ · norm2 R[0..j, j]` is removed; rows `c` and `c + 1`, `c` ascending from the removed position,
   are rotated with the item-8 construction across every pair to the right of the diagonal and `Qᵀy`; the
   `Qᵀy` entry left alone in the last row is squared into the residual sum of squares (in removal order); and
   the test resumes at the SAME position against the re-triangularised column. The coefficient of a removed
   column is `None`. A column of zeros is always removed (`0 ≤ τ · 0`), so a kept diagonal is never zero and
   `Singular` does not arise from `ofAccumulator`; `requireFullRank` turns any aliasing into
   `RankDeficient rank`. `R⁻¹` is `solveUpper` of each unit vector, so its order is the solve's. Alternative
   considered: refusing rank deficiency outright (rejected: R's `lm` convention, which the plan's `Fit` follows,
   reports aliased columns; the strict reading is one call away).
6. **The batch factorizations' order.** `QR.householder` and `QR.pivoted` share one kernel: at step `k`, with
   pivoting the remaining column of largest scaled norm over rows `k..` (recomputed every step, never
   downdated; the lowest index on a tie) moves to position `k`; `α = norm2 a[k.., k]`; `σ = −1` when
   `a[k,k] < 0` else `1`; `v₀ = a[k,k] + σα`; each later column is updated by `f = (2·(v·a_j)) / (v·v)` and
   `a_ij ← a_ij − f·v_i`; a row of `R` with a negative diagonal is then negated, so both kernels' diagonals are
   non-negative. Rows past `n` are zero. The two are NOT byte-equal to the streaming kernel (D49 item 10) and
   are held to it by a law within `256 · (n + p) · u · ‖a_j‖` on well-conditioned designs. The pivoted rank is
   D49 item 11's test at each position; its aliased columns are reported by original index, ascending.
7. **The codec.** `LinAlgWire` writes a matrix as `{"$type":"matrix","bits":…,"cols":c,"rows":r}` and an
   accumulator as `{"$type":"qrAccumulator","cols":p,"count":n,"qty":…,"r":…,"rss":…}`, canonical JSON
   (`Rendering.CrossHost`) with closed member sets, each float its binary64 bit pattern in sixteen lowercase hex
   digits, a member's floats concatenated, `r` the upper triangle row by row. A NaN is written
   `7ff8000000000000` on every host and read as the host's NaN, so a state encodes to the same bytes on every
   host (D50 item 10's observation, that the canonical NaN is one value but not one bit pattern across hosts,
   applied to a codec). A decoder refuses a diagonal of `R` or a residual sum of squares with the sign bit set
   (no accumulator builds one) and any shape whose element count would pass `2³¹ / 16`. Alternatives
   considered: the shortest round-trip decimal the incremental state codec writes (rejected: it reads back the
   same value only through every host's parser being correct, needs tokens for `-0` and the non-finite values,
   and is longer for full-precision floats — the bits ARE the claim); a JSON array of one hex string per float
   (rejected: three bytes of punctuation per float and nothing gained). The package now references
   `Fuaran.Core.Wire`, registers a wire root (`api/wire/Fuaran.Compute.LinearAlgebra.txt`) and leaves
   `WireSurfaceTests.notWire`.
8. **The native rotation.** `Kernels.rotateNative` runs a SIMD lane per pair on .NET — four rounded products
   and two rounded sums per lane, no fused multiply-add — and is the portable member under Fable; a law holds
   the two byte-equal over every length to 37 and over whole accumulators to 21 columns, and a probe shows a
   fused rotation answering otherwise. Both the accumulator and the aliasing re-triangularisation call it.
9. **The laws' bounds** (D49 item 10 asked 591 to state them). With `u = 2⁻⁵³`: `|RᵀR − XᵀWX|ᵢⱼ ≤ 16 (n + p
   + 2) u ‖xᵢ‖ ‖xⱼ‖`; `|Xᵀ(y − Xβ)|ⱼ ≤ 64 (n + p) u ‖xⱼ‖ (‖y‖ + Σₖ |βₖ| ‖xₖ‖)` on a well-conditioned design
   (the identity's multiple added to the top rows, `n ≥ 3p + 2`); Householder against streaming as in item 6.
   The byte laws are exact: `addRows` is `addRow` row by row, refusals included; an append is a fresh fold; a
   state through the codec continues, and fits, to the same bits; the accumulator is its own spelling through
   the public `Givens.make` / `apply`.
10. **The benchmarks** (`benchmarks/results/2026-11-04-i7-8650u-phase-591.md`) time the fold at the plan's
    sizes with rows generated as they are folded, portable against native on .NET and the one member under
    node, and state the gap to a vendor BLAS as an estimate, because no vendor BLAS is installed on the
    machine that measured. The first node run showed the rotation loop paying for Fable's shared
    bounds-checked array helper (0.09 GFLOP/s at 10⁶ × 64); the loop now indexes through unchecked accessors
    under JavaScript (`Kernels.get` / `set`, the plain index on .NET), the dataframe package's Phase 326
    pattern, which cut node's cost 9.5 times with no bit moved. The accessors are a second copy of that
    package's internal `Raw.get` / `set` rather than a shared seam: the dataframe package sits above this one,
    and two internal one-line emits do not meet D49 item 20's bar for a new substrate dependency.

**What a consumer does.** Nothing changes for an existing consumer: new types and modules, one new
`LinAlgError` case, a new wire baseline, all within the `0.43.0` draft in which the package itself is new
(`STABILITY.md`).

**Tidy.** Reviewed every key file the phase touched. `Givens.make` allocated a two-element array per rotation
to call `norm2`; it now calls the allocation-free `norm2Pair`, which the accumulator needed anyway. The census in
`LinearAlgebraTests` and `Operations.exactness` were extended rather than duplicated. Nothing was made dead.

## 2026-11-04 — D50: `Fuaran.Compute.LinearAlgebra` ships in its own namespace with modules `Matrix`, `Blas`, `UpperTriangular` and `Givens`; it takes `Fuaran.Core.Column` alone; exactness is D43's `Pinned | HostRounded`; the two triangular solves' fold orders are pinned; its laws live in the package and its hard-input vectors render through the one corpus writer, with a NaN written as a token

**Context.** Phase 590, stage S1 of the linear-algebra plan: the first kernels under D49. The shard left five
calls to the phase — the level-1 module's name, the error cases' names, where the law family lives, how the
vectors are written, and the package's exact dependency set — and implementing D49 surfaced three more (the
solves' fold orders, the identity rotation, the coverage reason). Each is ruled here so 591 onward and a twin
author read one record. The operator's standing instruction of 2026-11-04 (work unsupervised, take the
long-term option, record the alternatives) applies to every item.

**Premise check** (on `dd61528`). D49 had landed (as D49, not the shard's D48). The substrate's
`Vector<float>` (Column 0.40.2) still ships `Vector.adopt` and `Vector.Unsafe.borrow`, with `slice` a
zero-copy view. The roster, surface, wire, boundary and coverage checks all derive their package set from
`src/`, so a new packable project is picked up automatically — and is refused until it is registered in each:
the README table, `api/`, `WireSurfaceTests.notWire`, `ComputeBoundaryTests.compute` and
`proofs/coverage-exclusions.json`. That list is longer than the shard's "the roster test and `api/`
baselines", which is why the phase touched three test files and a proofs file outside its declared region.

**Ruled.**

1. **Namespace and modules.** The package's namespace is `Fuaran.Compute.LinearAlgebra`, not the
   `Fuaran.Compute` the other four share: `DataFrame` already publishes `Fuaran.Compute.Exactness` and keeps an
   internal `Vec`, and two assemblies contributing one name to one namespace resolve by reference order. Inside
   it: `Matrix` (storage — `init`, `identity`, `ofColumns`, `column`, `columns`, `transpose`, the reads),
   `Blas` (levels 1 to 3 — `dot`, `axpy`, `scale`, `add`, `sub`, `norm2`, `gemv`, `gemm`, `gram`),
   `UpperTriangular` (the checked factor and its solves), `Givens`, `Operations` (the exactness table) and
   `LinearAlgebraLaws`. Alternatives considered: `Vec` for level 1 (rejected: the shard's own collision with
   `DataFrame`'s internal module); `Vector` (rejected: the substrate's module for the very type these operate
   on); `Level1` / `Level2` / `Level3` (rejected: a call site would name a level rather than the operation, and
   `gemv` beside `gemm` is one contract split three ways). `Blas` names the operations' conventional levels; it
   is not the reference BLAS calling convention and carries no `alpha` / `beta`.
2. **Exactness is D43's vocabulary.** The shard said `Exact`; the evaluator's word since D43 is `Pinned`, and a
   schema walk that reads through this table later should read one vocabulary, so the type is
   `Exactness = Pinned | HostRounded`, in this namespace, with `Operations.exactness` classifying every public
   operation. The suite holds the table equal to the published functions of the four operation modules, both
   ways, so a function published without a class is red at the commit that adds it.
3. **Column only.** D49 item 20 bounds the package to `Fuaran.Core.Column`, `Fuaran.Core.Wire` and FSharp.Core.
   Nothing here encodes, so the project references Column alone: a package reference without a use is a
   dependency a consumer pays for and a raise must move for nothing. A matrix codec, when one is wanted,
   takes Wire under the same bound.
4. **The errors.** `LinAlgError` (qualified access) is `ShapeMismatch of operation * left: Dims * right: Dims`,
   `Singular of index`, `RankDeficient of rank`, `NotFinite of row * col`, `NotTriangular of row * col` and
   `NotConverged of iterations`. `NotTriangular` is the one case D49 item 6 did not list: the checked
   `UpperTriangular.ofMatrix` needs a refusal for a nonzero entry below the diagonal, and naming it as a shape
   mismatch would hide which entry. `ofMatrix` checks square, then finite, then triangular, and names the first
   offender in column-major order; a `-0.0` below the diagonal is a zero. A kernel does not refuse a non-finite
   operand — it propagates it, and the NaN leaves canonical (D49 item 5); refusing non-finite input is the
   checked constructors' and the frame bridge's (D49 item 13), where the row and column are known.
5. **The solves' orders** (D49 item 1 says the order is the contract; this item states it). `solveUpper` is
   back substitution by columns: for `j` from `n − 1` down, `x_j = b'_j / R[j,j]`, then
   `b'_i ← b'_i − x_j · R[i,j]` for `i < j` — so each `x_i` subtracts its terms in DESCENDING `j`, a lane per
   row as in `gemv`. `solveUpperT` is forward substitution down column `i` of `R`, ASCENDING `j`. A diagonal
   entry exactly zero is refused as `Singular`, the lowest such index, before any arithmetic. The rank
   tolerance (D49 item 11) is the factorization's test, not the solve's.
6. **`gemm` is `gemv` a column at a time.** Element `(i, j)` accumulates `A[i,k] · B[k,j]` in `k` order from
   `+0.0` either way, so the column form is the per-element dot byte for byte, with `gemv`'s SIMD row lanes;
   a law holds the two spellings equal. `gram` computes each pair once and mirrors it, so it is exactly
   symmetric, and a law holds it equal to `gemm (transpose A) A`.
7. **The identity rotation is a flag.** `Givens.make a b` with `b = 0` (either sign) is the identity and says
   so (`IsIdentity`); applying it returns the pair bit for bit. Testing `c = 1 && s = 0` instead would be
   wrong: a pair `(huge, subnormal)` makes a real rotation whose `s` underflows to zero, and `c·x + s·y` is not
   `x` when `y` is infinite or `x` is `-0.0`.
8. **Storage.** A `Matrix` is rows, columns and one adopted `Vector<float>` in column-major order, NaNs made
   canonical as it is built, so `Matrix.column` is a zero-copy `Vector.slice` that is already canonical.
   Equality is the substrate vector's (every NaN one value, `-0.0 = 0.0`); a byte comparison reads
   `Matrix.toColumnMajor`. A shape is an argument, not data, so `init` raises on a negative dimension and the
   reads raise outside the shape, as the substrate's indexer does; a mismatch between operands is data and
   returns `ShapeMismatch`.
9. **The laws live in the package.** `LinearAlgebraLaws.laws seed count` answers each law by name with a
   `Result<unit, string>` — the verdict shape of the substrate's `Corpus.codecLaws`. They are not a family in
   `Fuaran.Compute.Conformance`: that package sits above `DataFrame`, and the kit's `LawResult` and roster are
   `Fuaran.Core.Conformance`'s, which item 3 and D49 item 20 keep out of this package; a law family the
   package cannot run itself would also leave a consumer (or a host compiled from the Fable sources) unable to
   check a native member without the dataframe layer. The draw is Park and Miller's generator run in float
   arithmetic, every step exact, so a seed draws the same matrices on every host.
10. **The vectors.** `conformance/laws/linear-algebra-laws.json` holds the package's answers over authored hard
    inputs, rendered through `CorpusFile` (Phase 543's route) and written by `--emit-laws`. A float is its bit
    pattern as sixteen lowercase hex digits, except a NaN, which is the token `nan`: .NET's `Double.NaN` sets
    the sign bit and JavaScript's NaN does not, so "the canonical NaN" is one value per host but not one bit
    pattern across hosts, and the file compares only what is pinned. The file carries no version stamp, so it
    moves only when an answer does. The suite holds it fresh on .NET and holds the document the node leg
    renders under Fable equal to it, so the vectors pass on both hosts. **The shared corpus copy is a release
    act:** like the transform family's (D6 (4)), the corpus grades a copy against a release tag, and no tag
    holds this file yet; the `v0.43.0` release adds the `copies.json` record and the copy.
11. **The coverage reason.** The package is excluded from `Proofs.Coverage` under a new term,
    `pinned-order-law-tested`: its contract is an order of IEEE operations, which the byte laws and the vectors
    check directly, over the premise `linear-algebra-basic-ops-host-independent`. The substrate's
    `law-tested-by-design` was considered and rejected: its text requires the named family to sit in the kit's
    roster, which item 9 rules out. `model-scheduled` was rejected because no model is filed, and an F* model of
    a fixed float order would restate the code.

**What a consumer does.** Nothing changes for an existing consumer: a new package, additive (`STABILITY.md`,
0.43.0). Before the `v0.43.0` tag, the nuget.org Trusted Publishing policy must name the new id
(`.github/workflows/publish-packages.yml` says so).

**Tidy.** Reviewed every file the phase touched. `pack.ps1`'s header and `LawVectorExport.emitted`'s comment
counted the packages and vector sets and were corrected with the addition; nothing was made dead or duplicate.

## 2026-11-04 — D49: the linear-algebra rules, recorded before any kernel lands — every reduction a pinned left-to-right fold, no fused multiply-add on any host, one Givens rotation with a non-negative diagonal, one rank tolerance, listwise deletion at the frame bridge, the IRLS start, step-halving and stopping rules, and the package boundary; D48 item 7's "a JavaScript host does not promise a correctly rounded square root" is corrected

**Context.** Phase 589, stage S0 of the accepted linear-algebra plan: a pinned matrix library
(`Fuaran.Compute.LinearAlgebra`), a streaming Givens QR, a `Fit` verb in `DataFrame` and log-link GLMs by IRLS.
Every one of those inherits the rules below, so they are written before the first kernel (Phase 590), and a
review of 590 onward cites "D49 item n" rather than arguing from first principles. A twin author porting the
library reads this entry as its contract. The operator's rulings of 2026-11-01 on the plan are numbered D1–D15
in the plan itself; to keep them apart from this record's numbers they are cited here as **ruling L1 … L15**
(L3: pinned and byte-identical, no fast host-rounded path; L4: the streaming Givens QR is the kernel; L5: one
`Fit` verb; L6 = B: pinned elementary functions before the GLM; L7: warm start never certified; L8: no
tall-skinny QR; L9 and L15: the scope boundary; L10: sandwich errors in). Nothing in the evaluator moves; no
public surface moves.

**Premise check** (on `de96fe9`, the tree this phase was cut from). The shard named D47 as the newest entry;
Phase 596 took D48 the same day, so this entry is D49. `proofs.json` still carries
`pipeline-ieee-multiply-is-host-independent`. No linear-algebra code exists: `src/` holds `ColumnOps`,
`Conformance`, `DataFrame` and `PipelineQuery` only, and no source calls a fused multiply-add. One premise
did not hold as written. D48 item 7 says a square root is "the one operation here whose correct rounding a
JavaScript host does not promise". That was true of ECMA-262 up to its 2024 edition, where `Math.sqrt` was
implementation-approximated. tc39/ecma262#3345 ("Normative: fully define Math.sqrt", merged 2024-08) made it
the correctly rounded `𝔽(√ℝ(n))` from ECMAScript 2025, which matches what every engine already did (each
compiles it to the hardware square root, which IEEE 754 requires to be correctly rounded). .NET's
`Math.Sqrt` is the same instruction. So `√` joins `+ − × ÷` as an operation this library may use and
still be pinned, which the plan's scaled norm and Givens construction need. D48's choice of the square
region over Fritsch and Carlson's circle stands, because it needs no square root at all; only its stated
reason is corrected.

**Ruled — the determinism rules (L3; this record's D3 and D9 applied to linear algebra).**

1. **Every reduction is a pinned left-to-right fold.** A dot product sums indices `0 .. n−1` in order,
   starting from `+0.0` and adding each product as it is formed. In a matrix product, `C[i,j]` is the dot of
   row `i` of `A` and column `j` of `B` in `k` order. Blocking over `i` and `j` is allowed; splitting,
   reordering or unrolling-with-separate-accumulators over `k` is forbidden, as D9 forbids it for float `Sum`.
   A matrix-vector product `y = A·x` is computed column by column (`y += x_j · A[:,j]`, `j` ascending), so
   each `y_i` accumulates in `j` order. Every other sum in the library — a Gram entry, a residual sum of
   squares, a deviance, a Pearson statistic, a column norm — is a fold in its stated index order, and the
   order is part of the operation's contract.
2. **No fused multiply-add, ever, on any host.** `a·b + c` is a rounded product followed by a rounded sum.
   No member calls `Math.FusedMultiplyAdd`, `MultiplyAddEstimate` or a vector FMA intrinsic, native member
   included. Neither .NET's JIT nor a JavaScript engine contracts one unasked, but some twin languages do: Go's
   specification lets a compiler fuse `x*y + z` (an explicit `float64(x*y)` conversion forbids it), and C or
   C++ needs `-ffp-contract=off`. A twin carries whatever its language needs to keep the product rounded, and
   says so beside its kernel.
3. **SIMD only across independent outputs.** A vector lane computes a different output (a different `y_i`, a
   different element of a rotated row), never a part of one output's sum. The column-major `gemv` above and a
   Givens rotation of two rows are both elementwise in this sense. Where a kernel has a portable member and a
   native one, as `Kernels.fs` does, a law holds them byte-equal on the same inputs.
4. **One 2-norm, scaled, never `hypot`.** `norm2 x` is: `m = max |x_i|` (exact, and independent of order);
   `0` when `m = 0`; NaN when any element is NaN; `+∞` when `m = +∞`; otherwise `m · √(Σ (x_i / m)²)` with the
   sum folded in index order. The host's `hypot` is host-rounded and is never called. A Givens rotation's
   radius is `norm2 [a; b]` by the same rule.
5. **Canonical NaN on the way out** (D16). Every float the library returns carries `Double.NaN`'s bits when it
   is a NaN, applied where the answer leaves a public function, never per arithmetic step.
6. **Typed errors, not exceptions.** A public operation returns `Result<_, LinAlgError>` where it can fail:
   a shape mismatch, a singular triangular solve (naming the diagonal index), rank deficiency (naming the
   rank), a non-finite input (naming the row and column) and IRLS non-convergence (naming the iterations).
   The case names are 590's to fix in the package; the classes are fixed here. Nothing throws on bad data.
7. **Every operation is `Exact` (pinned) in the D43 / D48 sense.** Under L6 = B the transcendentals the GLM
   needs are pinned in an `Elementary` module (Phase 593) before the GLM lands, so `exp`, `ln` and float
   `pow` stop being host-rounded inside this library. The classification is kept on each operation so
   `SchemaWalk` reads through it, and so a registered host function (L14) can be marked otherwise.

**Ruled — the factorization rules (L4, L8).**

8. **The one Givens construction and its sign convention.** Given `(a, b)`: when `b = 0` the rotation is the
   identity `(c, s, r) = (1, 0, a)` and applying it changes nothing; otherwise `r = norm2 [a; b]` (item 4),
   `c = a / r`, `s = b / r`, so `r > 0`. Applying it to a pair `(x, y)` gives `x' = c·x + s·y` and
   `y' = c·y − s·x`, each as two rounded products and one rounded sum or difference, in that operand order
   (item 2). Because a diagonal entry of the streaming `R` starts at zero and is only ever replaced by a
   radius, **every diagonal entry of `R` is non-negative** — the sign convention that makes `R` unique, and the
   one a batch factorization is normalised to before it is compared with the streaming one.
9. **A weighted row enters scaled by `√w`.** `addRow` with prior weight `w` multiplies every element of the
   row and its response by `√w` (correctly rounded, item 4's premise) before rotating; `w = 0` is accepted,
   rotates nothing and is not counted; a negative or non-finite weight is refused before any rotation.
   Rows are rotated into `R` in row order, one at a time, columns ascending.
10. **The streaming Givens QR is the `Fit` kernel; batch Householder is secondary and not byte-equal to it**
    (L4). The two agree within a tolerance law (590/591 state its bound), never byte for byte; a fit that the
    law vectors pin is always the streaming one.
11. **The rank tolerance.** A column `j` of `R` is negligible — aliased — when
    `|R[j,j]| ≤ τ · norm2 R[0..j, j]` with `τ = max(n, p) · 2⁻⁵²`, `n` the counted rows and `p` the columns,
    `τ` computed as `float (max n p) * 2⁻⁵²` and the comparison taken after it. The column's own norm is read
    off `R` (it equals the weighted design column's norm in exact arithmetic), so the streaming accumulator
    needs no second running sum. `QR.pivoted` pivots on the largest remaining column norm, the lowest index on
    a tie, and states its rank by the same test. An aliased column is dropped, the remaining columns are
    re-triangularised with the item-8 rotation in column order, its coefficient is reported null (the `NA`
    convention of R's `lm`), and the rank is `p` less the aliased count. Alternatives considered: R's `lm`
    tolerance (`1e-7`, relative, with limited pivoting) — rejected because a fixed constant is too loose for a
    well-scaled design and drops genuine columns of large-`n` fits; a tolerance relative to `|R[0,0]|` alone
    — rejected because without pivoting the first column need not be the largest, so the test would depend on
    column order.
12. **Parallelism across groups only; no tall-skinny QR** (L8). One group's fit is one sequence of rotations
    on one thread; splitting it changes the last bits and needs its own ruling, as D9 was for `Sum`. Groups
    are independent and may run alongside one another.
13. **The frame bridge deletes listwise.** A row with a null in the response, any predictor term, the weight
    or the offset is left out of the fit before the matrix is built, and the count left out is reported. A
    non-finite value in a present cell (NaN or an infinity) is not a null: it is refused by name (row and
    column), never silently deleted. Rows of weight zero are kept in the frame and fitted values are produced
    for them, but they are not counted in `n` or the degrees of freedom.

**Ruled — the IRLS rules (plan §2.5; L6 = B).**

14. **Starting values.** `μ₀ = y + 0.1` for Poisson and Tweedie (`y = 0` is allowed, and `ln 0` is never
    taken); `μ₀ = y` for Gamma and for Gaussian under the log link. `η₀ = ln μ₀` through the pinned `Elementary`
    `ln`. The deviance of `μ₀` is the "previous" deviance of the first iteration.
15. **The refusals before any iteration.** Poisson and Tweedie (`1 < p < 2`) need `y ≥ 0`; Gamma needs
    `y > 0`; Gaussian under the log link needs `y > 0`; every weight is `≥ 0` and finite. Each is refused by
    name with the first offending row in row order.
16. **Step-halving, a fixed count.** When an iteration's new `μ` has a non-finite element, or its deviance is
    greater than the previous iteration's, the step is halved — `β ← 0.5·β_old + 0.5·β_new`, element by
    element (each product by `0.5` exact, one rounding for the sum, no overflow) — and `η`, `μ` and the
    deviance recomputed, at most **10** times. If the tenth halving still leaves a non-finite `μ`, the fit is
    refused naming the first row whose `μ` is non-finite (the separation refusal); if it still leaves the
    deviance above the previous one, it is refused as non-converged at that iteration. A fit never returns a
    non-monotone path. Ten bounds the work of one iteration to eleven deviance evaluations, and a step cut to
    `2⁻¹⁰` of itself is below anything a useful iteration moves.
17. **The stopping rule.** Stop when `|dev − dev_prev| / (|dev| + 0.1) < tol`, computed in that order, the
    comparison strict; or at `MaxIter`, where the fit is the named refusal `NotConverged` carrying the
    iteration count. The defaults are R's `glm.control`: `tol = 1e-8`, `MaxIter = 25`.
18. **The deviance.** A fold in row order of `prior weight · unit deviance`. Poisson: `2 (y·ln(y/μ) − (y − μ))`;
    Gamma: `2 (−ln(y/μ) + (y − μ)/μ)`; and `y·ln(y/μ)` is pinned to `0` at `y = 0` without evaluating the
    logarithm. Tweedie's unit deviance is 595's to state under the same rule.
19. **No conditioning-based tolerance** (L6 = B). Because every IRLS step is built from pinned operations, the
    coefficients, the iteration count and the convergence decision are identical on every host and the law
    vectors compare them byte for byte; the GLM waits for Phase 593's `Elementary` and does not ship ahead of
    it. A link or family added later (the logit link, Phase 600) records its starting value and its unit
    deviance in its own entry under items 14–18.

**Ruled — the boundaries.**

20. **The package** (L1, L2). `Fuaran.Compute.LinearAlgebra` is a new packable cohort member, ahead of
    `DataFrame` in the build order, depending on `Fuaran.Core.Column`, `Fuaran.Core.Wire` and FSharp.Core
    only, and Fable-clean. Its vectors are Core's `Vector<float>`, not a new type, so a frame's float column
    becomes a regression input without a copy. Matrices are dense and column-major; the public surface is
    immutable and mutation stays internal, following Core's `adopt` / `borrow` pattern. It knows nothing of
    tables, nulls or units: those are `DataFrame`'s, at the bridge (item 13).
21. **One `Fit` verb with `Family` and `Link`** (L5); `Gaussian` with `Identity` is ordinary least squares and
    runs no iteration.
22. **Incremental behaviour** (L7, plan §2.6). Least squares over rows appended last in group order rotates
    the new rows onto the stored `R` and is certified byte-equal to a full evaluation; a deleted or edited row
    re-fits its group (downdating is not stable), certified as float `Sum` is; a GLM re-fits an affected group
    from the pinned start, certified. A warm start from the previous `β` converges to different bits and is
    **never certified**: if it exists at all, it is an opt-in mode outside the certified evaluator.
23. **Scope** (L9, L15). This repository ships generic estimation — ordinary, weighted and ridge least
    squares, IRLS over the standard families, classical standard errors and the heteroskedasticity-robust
    sandwich estimators HC0–HC3 (L10, Phase 598) — and generic transforms — named functions, interpolation,
    convolution, recurrence and scan. Domain-specific methods built on top of these (the endogeneity
    corrections, bootstrap decompositions and named response-curve families of a particular modelling
    discipline) are out of scope here; a consumer expresses them over the generic steps.

**Ladder.** `proofs.json` gains `linear-algebra-basic-ops-host-independent`, an `assumed` premise beside
`pipeline-ieee-multiply-is-host-independent`: `+ − × ÷ √` are correctly rounded binary64 operations on every
host this library runs on, and no host contracts a product and a sum into one rounding. The byte-identity
claims of 590 onward stand on it; the law vectors' byte comparison on every certifying host is what holds it,
not a theorem. `ProofsLadderTests` gains a case naming both premises, so neither row can be dropped while
the rules above cite it.

**What a consumer does.** Nothing: no surface moves, so `STABILITY.md` has no entry. A twin porting the
library reads items 1–19 as its contract.

**Tidy.** Reviewed `DECISIONS.md`, `proofs.json` and `ProofsLadderTests.fs`. Nothing in them is made dead or
obsolete by this entry; D48's square-root sentence is corrected here rather than rewritten in place, as the
record corrects an earlier entry's sentence elsewhere (D41 on D38 item 1).

## 2026-11-04 — D48: a pipeline carries its own named functions as `Define` steps, every `Call` is expanded inline before evaluation under §21.8's 512-node bound with recursion refused, and `Interpolate` reads an operand through knots as data — linear, step, or a monotone cubic under a pinned Fritsch–Carlson slope rule — exact at every knot and byte-identical on every host

**Context.** Phase 596, stage S8 of the accepted linear-algebra plan (its rulings D11 and D12, and its §7 Q7,
answered "copies in each pipeline"). A regressor is rarely a raw column: a saturation curve, a custom index or an
empirical response curve prepares it before a fit sees it, and the founding rule here is that a pipeline is data, not
code. Two tiers meet both: named functions written in `ColExpr`, and curves given as knots. Phase 597 builds on both
(its t-quantile table is an `Interpolate`), so the wire forms below are meant to be final.

**Premise check.** Read on the tree (`74a34f3`) before anything moved. The `ColExpr` union is as the plan describes
(sixteen cases, `Convert` last); the compiled expression form (Phase 266) compiles the resolved form `ResolvedExpr`,
so a new node needs an arm in the resolver, the reference evaluator and the compiler; the schema walk reads
exactness per output column (`ColumnKnowledge.Exactness`, D43) through `hostRoundedOperand`. All three held. One
plan detail did not survive contact: the plan's `FnDef` gives each parameter a `ColumnType`. Item 2 says why it
has none.

**Ruled.**

1. **Where definitions travel: a `Transform.Define of FnDef` step, scoped to the whole pipeline.** A pipeline is a
   `Transform list` and its wire is a JSON array of steps, so a definition is a step: `{"$type":"define","name":…,
   "params":[{"name":…,"default":<cell or {"$param":…}>?},…],"body":…}`. A definition is visible to every step and
   every other definition's body wherever it stands, so no ordering rule can be broken by a rewrite and the call
   graph, not the step order, is what is checked. A call's wire tag is `invoke` (`{"$type":"invoke","name":…,
   "args":[…]}`), because `call` has been a lenient spelling of `apply` since Phase 93 and a decoder cannot tell
   the two apart by tag. Alternatives considered: a pipeline record `{functions; steps}`
   (breaks the wire's top-level shape and every `Transform list` signature); a definition carried on each call
   (`Call of FnDef * args`, a copy per call site, and "unknown function" would not exist to refuse); a prelude rule
   (definitions must lead) — rejected because the planner may insert a `Project` at the head of a pipeline it
   prunes, and a rule a rewrite can break is a rule the evaluator would have to re-check.
2. **A parameter is a name and an optional default; it carries no declared type.** A declared type is either
   unchecked — a comment the wire carries that can lie — or enforced by a cast around each argument, which changes
   the body's typing and breaks the ruling that a call means exactly its body. Typing reads through the expansion
   instead. A default is a `Slot<Cell>`: a literal cell or a named parameter of the pipeline, so a Hill curve's `a`
   and `k` bind per evaluation, are reported by `Transform.paramsOf`, and bind under `Transform.substitute`.
   Arguments bind parameters in order; a parameter left without one takes its default, and one with none is an
   `ArityError` naming the count the call needed, as is a call passing more arguments than there are parameters.
3. **Expansion, not evaluation.** `FnDef.expand` registers the functions and replaces every `Call` with its
   callee's body, each `Arg` replaced by the argument (or the default), and drops the `Define` steps. Every
   evaluating entry point expands before its first step (`evalPreparedCountedFolding`, the asynchronous fold), the
   planner plans the expanded pipeline (`Plan.explain`, so the incremental seam classifies the expansion and a call
   is exactly as incremental as its body), and the schema walk walks it (`SchemaWalk.ofPipelineFrom`), so a call's
   type, unit and exactness mark are its body's — a body over a float `Pow` is `HostRounded` and the walk says so,
   until 594 moves the transcendentals onto pinned functions. A `Call` or an `Arg` that reaches the evaluator
   unexpanded (a caller of `evalExprInRow` that never expanded) is refused by name, as an unpinned `Now` is.
   `hostRoundedDecisions` reports each decision at the index of the WRITTEN step it came from; the planner's report
   counts the expanded pipeline's steps, and says so. Expansion is the pipeline's meaning, not an optimisation, so
   no `RewriteClass` names it. A pipeline's functions belong to the whole pipeline, so a host that splits one at a
   step (the Phase 415 source seam) carries every `Define` into each half; the seam law in the suite does so.
4. **The refusals, in order, at registration:** a name defined twice; a definition's own text — a parameter named
   twice, a body that reads a column, an `Arg` naming no parameter (`FunctionError`, naming the function); a call
   naming no definition (`FunctionError`, enumerating the defined names) or passing the wrong count (`ArityError`);
   a cycle in the call graph (`RecursiveFunction`, the cycle in call order, first name repeated last); a definition
   whose body alone expands past the bound; then, step by step, an `Arg` outside any body, a call refused as above,
   or an expansion past the bound (`ExpansionTooLarge`, naming the step or the function and the bound).
5. **The expansion bound is §21.8's 512 nodes per expression** — the limit the proof model states as
   `Limits.max_expr_nodes`, which D44 item 7 records as enforced nowhere. A function never builds an expression the
   specification refuses. The size is computed from the definitions, never by building the expansion: each
   definition's expanded size is a linear form `constant + Σ coefficient(p) · size(argument p)` over its own
   parameters (an argument is substituted whole wherever its parameter is read), filled callee first, every number
   saturating one past the bound, so a nest that doubles sixty times is refused in time linear in its text. The
   bound binds only an expression that contains a call: a hand-written expression past 512 nodes is unchanged, and
   enforcing §21.8 everywhere stays the open decision D44 item 7 names. Alternatives considered: a larger,
   function-only bound (4,096 or 10,000) — rejected because the evaluator's measured headroom (D44 item 6) is about
   190 levels of a left-deep chain in a Debug build, and a bound beyond the specification's would let a call build
   what the specification refuses; a depth bound — rejected because size bounds depth and work together, and depth
   bounds neither the work nor the expansion's memory.
6. **`Interpolate of ColExpr * knots * InterpKind * InterpOutside`.** Knots are `(x, y)` float pairs, literal: at
   least two, every coordinate finite, `x` strictly increasing, and every gap, rise and secant finite, so no
   arithmetic meets an infinity the knots did not state (`InvalidKnots`, raised with the step's other static
   refusals in the unit walk, before any row, so an empty frame is refused as a full one; the planner's totality
   verdict reads the same rule). The
   operand is an `Int` (promoted) or a `Float`; a `Decimal` is refused naming the `Cast`; a null propagates; a NaN
   answers NaN. Outside `[x0, xn]`, `Clamp` answers the end knot's `y` and `Null` answers `Null`. Inside, the
   segment is the largest `k` with `x_k <= x`, and a knot's own `x` answers its own `y` exactly, whatever the kind.
   `Linear` is `y0 + (y1 - y0) * ((x - x0) / (x1 - x0))`, in that order. `Step` holds `y_k` on `[x_k, x_{k+1})`.
   The answer is a `Float` (`Typing.interpolate`), states no unit (the curve's `y` are numbers the curve gives,
   whatever the operand's unit), and is marked as its operand is. Wire:
   `{"$type":"interpolate","expr":…,"knots":[[x,y],…],"kind":"linear"|"step"|"monotoneCubic","outside":"clamp"|"null"}`.
7. **The monotone-cubic slope rule (pinned).** Secants `d_k = (y_{k+1} - y_k) / (x_{k+1} - x_k)`. Tangents: `m_0 =
   d_0`, `m_n = d_{n-1}`, and at an interior knot `(d_{k-1} + d_k) / 2` where the two secants share a strict sign,
   else `0`. Then, segment by segment from the left: a flat segment (`d_k = 0`) sets `m_k = m_{k+1} = 0`; otherwise
   a tangent more than three times its segment's secant (`m_k / d_k > 3`, `m_{k+1} / d_k > 3`) is set to `3 · d_k`.
   That is Fritsch and Carlson's monotonicity region restricted to the square `[0, 3]²`, which needs no square root
   (their circle `α² + β² <= 9` does, and a square root is the one operation here whose correct rounding a
   JavaScript host does not promise); lowering a tangent only shrinks it toward the region, so a later segment's
   adjustment never undoes an earlier one. Evaluation: `h = x1 - x0`, `t = (x - x0) / h`, `c2 = 3d - 2m0 - m1`,
   `c3 = m0 + m1 - 2d`, `y = y0 + h · (t · (m0 + t · (c2 + t · c3)))`, then clamped into `[min(y0, y1), max(y0,
   y1)]`. The cubic lies in that interval on a monotone segment, so the clamp removes only a rounding overshoot; a
   flat segment answers `y0` exactly, because every increment is a product with zero.
8. **Exactness and decisions.** `Interpolate` is `Pinned`: every operation is an IEEE addition, subtraction,
   multiplication, division or comparison in a written order — no square root, no host library function, no fused
   multiply-add (neither .NET's JIT nor a JavaScript engine contracts one unasked). A curve that JUMPS — a `Step`,
   or `Null` outside — decides which side of a knot its operand fell, so over a host-rounded operand it is reported
   as a decision (`interpolate`); a continuous one (linear or cubic, clamped) is not.
9. **The proof model.** `Call`, `Arg` and `Define` need no model case: they are gone before evaluation, and what
   the model's evaluator sees is the expansion. `Interpolate` is declared out of the model, not modelled: floats
   are opaque carriers in `proofs/Pipeline.fst`, so a modelled curve could state only that some host function is
   called, which the law vectors already pin byte for byte. The oracle's translation refuses all four by name
   (`ProofOracleTests`), and its generators never build them. `FnDef.expand` and `Transform.Define` enter the
   operation census as `measured-elsewhere` (`proofs/coverage-exclusions.json`), standing in on
   `FunctionsAndCurvesTests`.
10. **The law vectors.** Thirteen shapes join `transform-laws.json` after the union shapes: `function-call`
    derives one column through a call, one through its body spelled out and a third comparing them, so a host
    reads the expansion law off the vector; the four registration refusals and the two curve refusals NAME their class (as
    D45 and D46 name theirs — a host evaluating named functions mirrors `RecursiveFunction`, `FunctionError`,
    `ArityError`, `ExpansionTooLarge`, `InvalidKnots` and `TypeError`); the curve shapes run every kind under both
    policies over the float column and over fixed probes below, between, on and above the knots, and exactly at
    the knots. Every vector is pinned
    bytes — the Hill curve's exponent is an int, so its powers run the exact routine.

**What a consumer does.** An exhaustive `match` over `ColExpr` gains `Arg`, `Call` and `Interpolate`; over
`Transform`, `Define`; over `EvalError`, `FunctionError`, `RecursiveFunction`, `ExpansionTooLarge` and
`InvalidKnots`. A host that evaluates pipelines itself either expands calls (the registration order above is the
contract) or reports the new vectors as unsupported; a host that hands pipelines to this evaluator changes nothing.

**Tidy.** Reviewed every key file this phase touched for dead code and duplication made obsolete by the change;
the new walks reuse the existing ones (expansion's sub-expression walk is one `children` function, not a fourth
copy of the match), and nothing the change made obsolete remains.

## 2026-10-29 — D47: a cell edit through `applyPrepared` records the rows it wrote, and a refresh of a state evaluated over the edited version reads that record as an in-place delta, keying the written rows and no others; the edited version's table shares every column the edit did not write; the shard's "the cost moved with the column identity" is half right, and the counted half is the record

**Context.** fuaran#1891 drove grid edits through `ColumnOps.applyPrepared`, read the delta off the op
(`deltaOfPrepared`, O(1)) and refreshed with `Incremental.refreshPrepared`. On a derive-only pipeline that is the
chunked path (Phase 268) and one edit costs one chunk. On a filter it is the row-local walk, and the op-derived
refresh measured no better than a diffed one (about 2.7 ms against 2.1 ms on 10,000 rows). The shard read the cause as
column identity: `Prepared.ofChunks` builds the version's table with a fresh column per column, so nothing keyed over
the prior version's columns carries across.

**Premise check.** Read on the tree (`0b6f287`) before anything moved, and reproduced as a counted test
(`PreparedEditRefreshTests`: 10,000 rows, one `SetCell`, a filter and a derive-only pipeline, op-derived against
diffed against full).

1. *Where the row-local refresh over a prepared version reads its key:* the TABLE. `refreshPrepared` hands
   `Prepared.table` to `refreshWith`, and `run` mints a token for every row (`tokensOf`), because the op-derived delta
   carries neither the keys a diff attaches (`KeyedIndexes.carried`) nor its in-place record
   (`KeyedIndexes.inPlaceOf`, Phase 323). A diffed delta carries both, so its refresh mints nothing and pays for the
   changed row; the diff itself keys the new table, which is where the diffed tick's linear term lives.
2. *`Source` builds fresh columns for untouched ones:* confirmed. `ofChunks` concatenates every rope
   (`Chunked.toColumn`) on first read, so an `applyPrepared` version shares no column with its prior version's table.
3. *The cost moves with column identity:* HALF TRUE. The counted linear term is the key minting, and column identity
   does not reach it: the walk's frame tests a column's length in O(1) whether or not the column is the prior's
   (`frameOf`), and nothing in the refresh holds an index over the prior's columns. The falsifier is a go-red in the
   suite: the edited version with its columns shared and its record dropped still keys every row. What column
   identity does cost is the concatenation of every untouched column the first time the version's table is read,
   which is memory traffic linear in the table and no key work.

**Ruled.**

1. **The edit is the record — both mechanisms, for different halves.** `Prepared` gains two internal fields: `Stamp`,
   a fresh object per version compared by reference and holding nothing, and `Edit`, the record a cell edit leaves
   (`PreparedEdit`: the parent's stamp and the rows written). `ColumnOps.applyPrepared`'s `SetCell` builds its version
   through `Prepared.ofEdit` with the edited row as the record. `refreshWith`, given a prepared version whose record
   names the stamp of the version this state was evaluated over (the state's source being that version's table),
   checks each written row's key against the state's token for that row, one key per row, and where every one is
   unmoved reads the edit exactly as it reads a diff's in-place record. `run` then takes the prior tokens as the
   source's, which the record asserts, and mints nothing. A written row whose key moved, a state evaluated over some
   other version, or a version with no record takes the general walk, as before.
2. **The version's table shares the parent's columns.** `Prepared.ofEdit` gives the new version's table the parent
   table's own column wherever the edit left the parent's rope in place (the same `Chunked`, under the same field, the
   parent's column `count` rows long), decided at edit time in time linear in the schema and only where the parent's
   table is already built. `SetCell`, `SetColumn`, `InsertColumn` and `RemoveColumn` build through it; `AppendRows`
   moves every rope and keeps `ofChunks`.
3. **Laziness and the persistent-version guarantee.** `Source` stays lazy: nothing is built at edit time but the
   schema-long sharing decision. The closure holds the shared columns, which the new table holds anyway, and nothing
   else of the parent: no frame, no rope, no table, and the parent is named only by its stamp, so a chain of versions
   never keeps its ancestors alive. A column is immutable, so sharing one changes neither version's table; the prior
   version still reads its own cell. `toTable (applyPrepared op (prepare t)) = apply op t` holds as before and is
   asserted over the shared table.
4. **What the bound is.** A one-cell edit's op-derived tick keys a constant number of rows (two to derive the delta,
   one to confirm it in place), evaluates one row on a filter, and does both identically at 1,000 and at 10,000 rows;
   the diffed tick keys at least the new table. The measure is counted, because a clock comparison of two sub-
   millisecond paths does not discriminate (Phase 207). The refresh still walks an array of row flags as long as the
   table (`runIncremental`'s `priorOf`/`stable`), which is the same pass the diffed in-place refresh makes and no key
   work; it is not claimed away.
5. **Refresh equals full evaluation, unchanged.** Every test of the phase compares the refresh with
   `DataFrame.evalPipeline` over the version's table, including a chain of five edits, a key-moving edit, a skipped
   version and a filter feeding a group step; the Phase 433 / 464 theorems' differential runs unchanged and green.
6. **The version.** `0.43.0` is an untagged draft carrying an additive change (D46); this change moves no public
   surface (the new fields and `ofEdit` are internal), so it rides the draft.

**Rejected.** *Keying the row-local refresh incrementally from the delta's rows alone:* the delta names keys, not
positions, so reading "the row with this key is at its prior index" off it needs either an index over the new table,
which is the work being removed, or trust in the delta beyond the contract (a delta must describe the change, not
where rows sit). The op knows the position, so the op records it. *Column sharing alone:* removes the concatenation
and no key work (premise 3). *A strong reference to the parent version in the record:* it would keep every ancestor,
and its table, alive for as long as the newest version lives. *A record for `SetColumn`:* its changed rows are read
off the cells, which is the diff's work; a whole-column write reaches the chunked path's chunk sharing already, and a
row-local record for it is left until a consumer measures one.

## 2026-10-29 — D46: the transform law vectors pin an unknown sort, partition or order key as `UnknownColumn`, a union pair with no type join as `TypeError` and a union whose sides' names differ as `JoinError`, each beside an answered neighbour; D45 item 3's "every other refusal stays unnamed" is narrowed by exactly these three classes

**Context.** The reference refuses a `Sort` key that names no column (Phase 412, D21), on the fused `Sort` + `Limit`
path too, and a `Window` partition or order key that names no column (Phase 441, D26), each as
`UnknownColumn(name, available)` before any row is read; and it types a union column by the column-type join,
refusing a pair no column type holds together (`unionField`, Phase 469, D36). On 2026-10-28 the four host twins were
read: the Python, Go and TypeScript twins sort by an absent key and return the input order, and the Python union keeps
the left side's types whatever the right side holds. All were certified green, because no transform law vector
reached any of these refusals.

**Premise check.** Confirmed on the tree before any vector was written. `knownSortKeys` (through `unknownSortKey` and
`unknownKey`) refuses an unknown key on the `Sort` step and on the fused top-n arm of `foldHead`, which
`evalPipeline` takes, since it plans; `windowRefusal` refuses an unknown partition key, then an unknown order key,
through the same `unknownKey`; `unionField` refuses an unjoinable pair with `typesBesideEachOther`, a `TypeError`,
and `evalUnion` refuses differing column names as `JoinError` before it pairs any column. Refusals were encoded as
D45 left them: `"verdict": "error"`, with `expected.error` on the overflow vectors alone, so no existing vector pinned
any class here. One premise of the shard was false: the file DID carry `Union` steps (`decimal-union`, `unit-union`,
and `unit-union-refused`, a unit mismatch), but every one paired two columns of one type, so none reached the type
join. No production code moves in this phase.

**Ruled.**

1. **The vectors.** Thirteen `evalPipeline` shapes are appended after the overflow shapes (iterations 91 to 103), so
   every earlier vector is byte-identical. The ordering family: a sort by the absent `revenue` alone, after a `Pivot`
   (the reported shape) and fused with a `Limit`; a `Window` partitioned by the absent `dept` and one ordered by the
   absent `revenue`; and an in-schema neighbour for each verb that must be answered. The union family: an int beside
   an int, an int beside a float (answered as a float, the join), an int beside a string (refused) and two sides whose
   second column's names differ (refused). They live in the shared sample for the reason D45 item 1 gives.

2. **An unknown ordering key is pinned as `UnknownColumn`.** It is the class every other verb answers for a column
   that is not there, and D21 and D26 made the ordering keys answer it on purpose: a key is a column reference, and a
   host that refused it as `TypeError` would have a different rule for name resolution in three verbs. The twins
   already carry the class (the base `unknown-column` vector's refusal is one), so the name costs a host nothing it
   does not have.

3. **An unjoinable union pair is pinned as `TypeError`, and differing names as `JoinError`.** `TypeError` is the
   class the reference gives every pair no column type holds together, a derived column's and a union's alike
   (`typesBesideEachOther`); `JoinError` is the class it gives a set verb whose sides do not line up by name. Both
   were read in the Python, Go and TypeScript twins before they were pinned: the twins spell the name mismatch
   `JoinError` today, and carry `TypeError` as a class.

4. **Every other refusal stays unnamed.** D45 item 3 stands for every refusal not named here: a decimal `Div`, a unit
   mismatch and a repeated name remain unmeasured vocabularies. The suite now asks the named families by shape
   (`LawVectorExport.refusalClassOf`), and holds every other vector to no class.

5. **The in-process law is unchanged**, for D45 item 4's reason.

6. **The version.** `0.42.0` is tagged, so the change advances the slot to `0.43.0`, as D45 item 5 did for the same
   class of change. No package's public surface moves.

**Rejected.** *Leaving the classes unnamed:* the ordering vectors would still turn a silently-sorting host red, but
a host could then refuse an unknown key as anything, and the rule D21 and D26 fixed is name resolution, which has a
class. *A case kind of its own:* D45 item 1. *Fixing the twins here:* they are other repositories, and follow in
fuaran#2485.

## 2026-10-24 — D45: the transform law vectors pin int32 overflow on `Add`, `Sub`, `Mul` and integer `Pow`, and a refused overflow vector names its class, `OverflowError`; every other refusal stays unnamed, and the in-process parity law still compares refusal with refusal

**Context.** The reference has range-checked every integer result against the int32 band since Phase 39:
`checkedInt` refuses an out-of-band `add`, `sub`, `mul` or `mod` as `OverflowError`, and since Phase 557
`powIntExact` refuses an integer power the same way, every product and every base square checked. No transform
law vector reached the band. On 2026-10-24 the four host twins were measured computing `add`, `sub` and `mul`
over a wider integer and answering unchecked, and every one was certified green, because nothing in the file
could see it. Separately, the twins' new `pow` refusal reports `TypeError` where this reference reports
`OverflowError`, and the file could not see that either: an `error` vector carried `"verdict": "error"` and
nothing else.

**Premise check.** Confirmed against the tree before any vector was written: `checkedInt` is the one check
behind `Add`, `Sub` and `Mul` on two integers (and the Fable arm's float carrier recomputes in int64 before the
same check, Phase 326); integer `Pow` with a non-negative exponent runs `powIntExact`; a negative exponent is a
float reciprocal and cannot overflow the band. Neither the vectors nor `DataFrameConformance.transformLaws`
compared the refusal's class: the law's `Error _, Error _` arm agrees on any two refusals, and the file's
`description` told hosts not to gate on a class. No production code moves in this phase.

**Ruled.**

1. **The vectors.** Twelve `evalPipeline` shapes are appended to the drawn sample after the power shapes
   (iterations 79 to 90), so every earlier vector is byte-identical and every reader that follows
   `iterations` follows them. For each operator, one vector lies past the upper edge of the band, one past
   the lower edge, and one in-band neighbour lands on or beside the edges: `Int32.MaxValue` and
   `Int32.MinValue` are reached by `add`, `sub` and `mul`, and `MinValue` by `(-2) ^ 31`. The upper edge of
   `mul` is `MinValue * -1`, the negation no int32 holds; the lower edge of `pow` is `(-1291) ^ 3`, refused by
   its final product. Each operand is derived into a column first, so the operation runs over a column as a
   host's kernel would, never over two literals a host might fold. They live in the shared sample
   (`TransformVectorSample`) rather than in a block of their own because they are evaluator answers like
   every other, and a case kind of their own would be skipped by a host that ignores kinds it does not know.

2. **The class is pinned for overflow, by name.** A refused overflow vector carries
   `"error": "OverflowError"`. The token is the `EvalError` case as it is named, which is the vocabulary every
   host's evaluator already mirrors, and it follows the member the substrate's decimal laws already use for a
   named refusal (`"verdict": "reject", "error": "MalformedShape"`). The overflow class is pinned because it
   is the class the reference has named on purpose since Phase 39, and because a host that answers an
   overflow with some other refusal has implemented a different rule, not spelled the same one differently:
   `TypeError` says the operands were wrong, where `OverflowError` says the operands were right and the
   result left the band. The twins' `pow` refusal is therefore a divergence these vectors make red, and
   fuaran#2444 is where the twins move.

3. **Every other refusal stays unnamed.** The file's other refusals (an unknown column, a decimal under
   `Div`, a unit mismatch, a repeated name) are not given classes here. Each would be a separate claim about
   a cross-host vocabulary nobody has measured, and this phase measured only overflow. The `description`
   now says a refusal is unnamed unless the vector carries `expected.error`.

4. **The in-process law is unchanged.** `DataFrameConformance.transformLaws` still agrees on any two
   refusals. Its subjects are .NET evaluators that return this repository's own `EvalError`, so the class
   comparison would add a behaviour change to a published kit for evaluators that cannot answer a different
   case without being a different evaluator. The cross-host contract is the vector file, and that is where
   the class is pinned. This suite checks it there: a vector's named class must be the reference's
   (`LawVectorTests`), with a go-red that perturbs the recorded class.

5. **The version.** `0.41.0` is tagged, so the change advances the slot to `0.42.0`. No package's public
   surface moves, but the vectors are part of what a version publishes and the corpus copy is re-emitted
   from a released version (D6 (4)), so a released `0.41.0` cannot be the version that carries them.

## 2026-10-22 — D44: a transform law vector is compared under the tolerance it carries, on every host and in this suite — a host-rounded column cell by cell within its bound, a zero, a sign and a non-finite value exact, everything else byte for byte — by one comparison the conformance kit ships; the committed file's freshness is asked the same way; the over-limit oracle expression is balanced, and the evaluator's measured stack headroom is recorded

**Context.** `main` was red in CI from `38ae3d8` (Phase 557's landing). On the Linux runners, in Debug and in
Release, `transform-77-power-ln-log10` answers `0.5118833609788744` in row 4 of `l10` where the vector,
emitted on a Windows x64 host, records `0.5118833609788743`: one ulp, which D43's bound admits. D43 item 6
had put the bound in the data (`expected.tolerance`), but nothing in this repository read it. Three legs
compared the recorded table byte for byte: the oracle question of the committed-vectors case
(`LawVectorTests`), the prepared-result law over the vectors (Phase 342) and the kernel pair over the
vectors (both `DataFrameTests`). They were green where the file was emitted and red everywhere else.
Separately, on Windows `verify.ps1` died with `0xC00000FD` inside the `Proofs.Oracle` case "the finding
holds on the shipped evaluator".

**Ruled.**

1. **The comparison.** An `ok` vector with no `tolerance` member is compared byte for byte, as before. One
   with the member is compared column by column. Every column it names must be present in both tables,
   under one type and one length, and null in the same rows. Each pair of present cells must be equal,
   except that two nonzero finite floats of one sign agree when they lie within the column's bound. The
   distance is the measure D43 item 8 was taken in: the difference of the two doubles' IEEE-754 binary64
   bit patterns, read as ordered integers. Then the answer, with every named column replaced by the
   recorded one, must encode to `expected.table` byte for byte, so the schema, the column order and every
   column the member does not name are compared exactly as before. The bound is the vector's own, never a
   constant on the host side; today every vector carries D43's 2. The rule is
   `Fuaran.Compute.Conformance.VectorTolerance` (`ofExpected`, `tableAgrees`, `floatsAgree`, `ulpDistance`).
   It is the kit a .NET host certifies with, and the three legs above call it. The file's `description`
   states the same rule for a host in another language, which is the re-emit: one line, and every vector
   is byte-identical. It is a module of its own because it is the comparison the families' vectors are
   read under, not a family, and so has no pre-split spelling for `Forwards.fs` to carry.
2. **A zero, a sign and a non-finite value are exact.** D43 item 6 says the special-value vector is pinned
   because "no wrong special value lies within two ulps of the right one", and gives `0` against a
   subnormal as an instance. Under the bare measure that is false: `0` and the least subnormal are one
   apart, and `expUnder` (`exp(-746)`, decided by the table) records `0`. So a zero of either sign is
   compared bit for bit, and so is a NaN or an infinity (the wire carries neither). Two floats of opposite
   signs never agree. This makes D43's sentence true as written. It is strictly narrower than D43's bound,
   and admits nothing D43 refused.
3. **Freshness is the same question.** The committed file and this host's render are the same file when
   they are byte-identical. They are also the same when they have the same lines and each line that differs
   is one vector carrying the member, identical in every member but its recorded table, whose tables agree
   under item 1. A header line that moved is staleness, and so is any other vector line. The within-host
   laws stay byte-exact: the render byte-stable across renders, the kept result against `evalPrepared`, the
   portable kernel member against the native one.
4. **The corpus copy is not re-synced here.** `copies.json` grades it `against: release`. It holds the
   `v0.40.0` release's file (67 iterations, `kitVersion` 0.40.0). The `CORPUS COPY STALE` banner is the
   standing, non-fatal report every run has printed since: the green run on `5d0435f` prints it at 89 lines
   against 93. Moving the copy now would publish twelve draft vectors no certifying host evaluates yet,
   ahead of the release they belong to. It moves with `0.41.0`, by that release's own re-emit.
5. **The stack overflow, at its cause.** Reproduced from this tree in Debug on Windows x64: 255 repeats of
   `resolveUnitsIn` / `go` / `both`, three frames for each `Binary` level. On `5d0435f` the same case passes.
   The case's 513-node expression was a left-deep chain 256 deep. The finding it pins is about the node
   COUNT (§21.8's limit is enforced nowhere, `pipeline-limit-unenforced`). The depth was incidental, and the
   evaluator recurses as deep as an expression is in several walks: unit resolution, typing, compilation.
   The expression is now a balanced tree of the same nodes (one `Col`, 256 `Add`, 256 `Lit 1`), ten deep,
   and the case asserts the depth. No thread's stack is raised and no production code moves.
6. **Measured, on 2026-10-22 (.NET 10, Windows x64).** A probe evaluates a left-deep `Binary(Add, _, Lit 1)`
   chain through `evalPipeline` on a thread of a stated stack, and halves the interval to the deepest chain
   answered. Debug, 1 MB: about 162 levels at `38ae3d8` and 190 at `5d0435f`. Debug, the default thread:
   about 250 and 292. Release, 1 MB: about 772 at `38ae3d8`, the limit there being `compileExpr`. The
   256-deep case therefore passed by about 36 levels before Phase 557 grew `resolveUnitsIn`'s frame with the
   `Pow` arm, and failed by about 6 after. The falsifier is a chain the probe reports answered at a depth it
   also reports overflowing. The instrument was checked both ways: the same chain passes at 150 and
   overflows at 200 on a 1 MB Debug thread.
7. **Recorded, and left where it is decided.** Nothing bounds an expression's depth. `decodePipeline`
   accepts a chain 300 deep, and the node limit is unenforced. Enforcing the node limit, the decision
   `pipeline-limit-unenforced` records as open, bounds the depth at 511. That is inside a Release build's
   measured 1 MB headroom and outside a Debug build's. This entry adds the measurement to that open
   decision, so whoever takes it starts from the number; it does not take it.

**Premises checked against the tree.** The shard asked that the format say which answers are
tolerance-compared and by how much. The format already did, since 557 (`expected.tolerance`). What it did
not say was the measure, or what else in a named column is exact, and the description now states both. The
CI's corpus banner is not one of the three failures, and it predates 557 (item 4). The overflow does not
predate 557 on this case, but it is a margin 557 consumed rather than a defect it introduced (item 6).

**Rejected.** *A host-side constant bound in the comparison:* the shard rules it out, and it would let a
vector's tolerance and a host's disagree silently. *Comparing the freshness of the committed file only on
the emitting host:* nothing records which host that was, and a CI on two operating systems is the case the
check exists for. *Raising the test thread's stack, or running the case on a thread of its own:* that hides
the depth instead of removing an incidental one. *Shrinking `resolveUnitsIn`'s frame:* it moves the first
walk to overflow without bounding any of them, and depth is item 7's question. *Re-syncing the corpus copy
now:* item 4.

## 2026-10-21 — D43: `Pow`, `Exp`, `Ln` and `Log10` enter the scalar set — an integer power runs one pinned routine, exact on every host; the transcendentals are host-rounded within 2 ulp behind a special-value table; the static walk marks every host-rounded column and reports each discrete decision such a value reaches; the Pow/Log line of Fuaran.Core's D13 is superseded

**Status (2026-11-04).** The host-rounded tier is SUPERSEDED for `Exp`, `Ln`, `Log10` and a float-exponent `Pow` by
D54 (Phase 594): the evaluator runs `Elementary`'s pinned routines (D52) behind the published table, so their columns
are `Pinned` and byte-identical on every host. Rulings (b) and (c), items 1, 2 and 4 and the mark's lineage rules
stand; the mark, the decision report and the tolerance remain for a value its caller declares host-rounded.

**Context.** Fuaran.Core's D13 (Phase 101, recorded in that repository's `DECISIONS.md` before the compute
strand was cut from it; this repository's own D13 is unrelated) declined `Pow` and `Log`: IEEE-754 does not
require a transcendental function to be correctly rounded, so two conformant hosts may differ in the last
ulp, and one ulp is a different byte in the canonical float layout — a parity failure. The reasoning
holds. What changes is the conclusion, on the operator's instruction (2026-10-21): "we should solve the
pow / log problem, or at least deal with it to acceptable tolerance per the host languages — a project in
one host should at least agree with other formulae in that host". Instead of refusing the functions, each
states the guarantee it carries, and the laws enforce that guarantee. The precedent is D13's own "no clock"
line, which Phase 125 reversed by pinning the clock through a witness rather than banning it.

**The three choices were ruled by the operator on 2026-10-21, at the phase's planning**, and are recorded
as rulings, not proposals: **(a)** the cross-host bound is **2 ulp**, measured against a correctly rounded
reference; **(b)** out-of-domain input yields **`Null`**, never a NaN, matching how the evaluator already
answers an invalid input (`Div` by zero, a negative `Sqrt`), and NaN input propagates as D16's one NaN;
**(c)** a `HostRounded` value that reaches a discrete decision — a `Filter` comparison, a `GroupBy` /
`Join` / `Distinct` key, a `Sort` key, `Round` / `Floor` / `Ceil`, a `Cast` to an int or a decimal — is
**admitted and reported by the schema walk**, never refused; the cross-host law promises its tolerance only
on a column no such decision reaches.

**Decision.**

1. **Four `ScalarFn` cases, `Pow`, `Exp`, `Ln`, `Log10`**, wire tags `pow` / `exp` / `ln` / `log10`.
   Typing: the transcendentals answer a float; `Pow` over an `Int` base under a LITERAL exponent at or
   above zero answers an `Int` (`Typing.powLit`), under a negative literal a float, under an int column
   the cells decide (an `Int` for a non-negative cell, a float for a negative one), and every other
   numeric pair a float. A `Decimal` operand is refused by name, as `Sqrt` refuses one
   (`pow of a decimal is not exact: write Pow(Cast(float, x), n)`). `Plan.isTotal` admits the
   transcendentals over any numeric operand and `Pow` over every numeric pair except an int base under
   an int exponent, which can overflow. One implementation, `applyScalar`, serves the reference row
   evaluator, the compiled derive (its boxed arm) and the incremental seam, so the three paths cannot
   disagree; it is Fable-clean, and the node measurement below ran it under node.
2. **An integer exponent — an `Int` cell — runs one pinned routine, `DataFrame.powInt`**: binary
   exponentiation over the host's IEEE-754 multiply, the accumulator empty until the first set bit, so a
   power takes exactly the multiplies its bits name — `x^0` is `1.0` with no multiply, `x^1` is `x`
   itself, `x^2` is `x * x`, `x^3` is `x * (x * x)`, `x^4` is `(x * x) * (x * x)`. Every conformant host
   rounds a single multiply the same way, so the product tree and not the host decides the bits, and the
   result is byte-identical on .NET, under Fable and in any twin that runs the routine. **Up to the cube
   the routine's cells are the repeated-multiplication spelling's** (`x * x * x` as a consumer writes it
   with `Mul`), a single multiply being commutative; from the fourth power the squaring tree is a
   DIFFERENT tree from the left fold's and no equality is claimed — the shard's "equals the repeated-
   multiplication spelling bit for bit" was checked and holds exactly that far, which is what the law
   asserts. A negative exponent is one reciprocal of the positive power, and **`Null` where that power is
   zero** — a zero base, or a square that underflowed to zero (`1e-300` to the power `-2`) — because the
   consumer's spelling `1 / (x * x)` meets `Div` by zero there and answers `Null`; the two spellings
   therefore answer one cell on every input. An `Int` base under a non-negative exponent runs the same
   tree over `int64` and refuses every product that leaves the int32 band by name
   (`OverflowError`, `pow overflowed int32: …`), a base square that leaves the band with exponent bits
   still to take included, since every value it would still multiply into is at least as large.
3. **A float exponent, `Exp`, `Ln` and `Log10` are host-rounded behind a special-value table** that is
   evaluated BEFORE the host is called, so the host's behaviour on those inputs is never reached:
   - `pow`: a NaN operand answers NaN (`pow(1, NaN)` and `pow(NaN, 0)` included — C99 says `1`,
     JavaScript says NaN for the first, and D16's one NaN is the answer); a zero exponent and a base of
     one answer `1`; a base of minus one under an infinite exponent answers `1` (C99 `1`, JavaScript
     NaN); a zero base under a negative exponent is `Null`; a finite negative base under a finite
     non-integer exponent is `Null`; everything else is the host's `Math.Pow`.
   - `exp`: a NaN propagates; above `709.782712893384` (the natural log of the largest double) the
     answer is `Inf` and below `-745.1332191019412` (the natural log of half the smallest subnormal)
     it is `0` — the two edges where a host's last-ulp rounding decides between a finite value and the
     limit; between them the host's `Math.Exp`.
   - `ln` / `log10`: a NaN propagates; zero and below is `Null`; `1` is exactly `0`; `Inf` is `Inf`;
     otherwise the host's `Math.Log` / `Math.Log10`.
   Within one host the answer is deterministic and agrees with every other formula evaluated in that
   host — the operator's bar. Across hosts it agrees within ruling (a)'s bound, measured below.
4. **Units** (D29 extended). `Pow` under a literal integer exponent composes through Core's `Unit.pow`
   (`m` squared is `m2`, `s` to `-1` is `/s`, to `0` unconstrained); an exponent product the algebra
   cannot carry — `Unit.pow` raises past `±2147483647` — is refused as `UnitMismatch` against the text
   `a power the algebra can carry` (the node in the error carries the exponent), never thrown. `Pow` under any other exponent, `Exp`, `Ln`
   and `Log10` take a DIMENSIONLESS operand: unconstrained, or stating `1` (a ratio of one unit, `t / t`,
   renders `1` and is admitted); a dimensioned operand or exponent is refused against `1`.
   `docs/units.md` states the rule; `proofs/Pipeline.fst` section 13 models it with `u_pow` a fourth
   member of the algebra parameter, and `refused_is_rejected` now also admits a power the algebra
   cannot raise as a rejection.
5. **The exactness mark.** `ColumnKnowledge` gains `Exactness` — `Pinned` or `HostRounded` — and the
   walk computes it per output column: an input field is pinned; a derive is host-rounded exactly where
   its expression's lineage passes through `Exp`, `Ln`, `Log10`, a `Pow` whose exponent the typer cannot
   call an int (a literal int, an int column or a cast to int is an integer exponent; anything else may
   be a float cell), or a column already so marked — a boolean over one included, since two hosts can
   decide it differently; a project, a filter, a sort, a join keep the mark; an aggregate over a
   host-rounded column is host-rounded, `Count` excepted (presence is decided by the table before any
   host is called) and `CountDistinct` not excepted (a decision over the values); a window column is
   its source's mark joined with its partition and order keys' (a rank over a host-rounded key is a
   decision); a union, an unpivot's `value` join their inputs' marks. `SchemaWalk.exactnessOf` reads
   one; `SchemaWalk.hostRoundedDecisions` lists every discrete decision of ruling (c) in pipeline
   order — the step, the verb (`filter`, `sort key`, `group-by key`, `join key`, `distinct`,
   `intersect`, `except`, `pivot key`, `window key`, `count-distinct`, `round`, `floor`, `ceil`,
   `rounded`, `cast int`, `cast decimal`) and the host-rounded operand (the column, or the function's
   tag where it is computed inside the deciding expression). Nothing is refused.
6. **Laws.** Within a host, the byte laws are unchanged: a host-rounded pipeline is byte-identical across
   repeated, reordered-batch and incremental evaluation, because the three paths share `applyScalar`
   and D16's one exit. Across hosts, the transform law vectors gain a per-column tolerance: a vector
   whose accepted output the walk marks host-rounded carries `expected.tolerance`, an object naming each
   such column with the bound in ulps (`LawVectorExport.hostRoundedUlps`, 2); a certifying host compares
   those columns within it and every other column byte for byte; a pinned column is never named, so
   every earlier vector is the bytes it was. Eight `power-` shapes follow the repeat shapes over the
   decimal table: `power-int-exponent` and `power-int-base` (byte-exact), `power-cube-is-repeated-multiply`
   (the routine beside the `Mul` spelling and their equality, byte-exact), `power-special-values` (every
   finite row of the table — the wire carries no `Inf` or NaN, so those rows are held by the suite alone;
   its columns read host-rounded functions and so carry the bound, which pins them all the same, since no
   wrong special value lies within two ulps of the right one), `power-float-exponent`, `power-exp` and
   `power-ln-log10` (host-rounded, with the bound), and `power-decimal-refused`. A host that does not
   implement the functions reports the vectors as unsupported and never skips them silently (Phase 356's
   rule, restated in the file's description).
7. **Proofs.** `proofs/Pipeline.fst` section 14 models `powInt` clause for clause over an abstract
   multiply (`fp`): `pow_int_zero` to `pow_int_four` state the product tree per exponent, `pow_int_neg`
   the reciprocal, and `pow_int_is_fold_to_three` the equality with the left fold up to the cube,
   **conditional on the multiply commuting** (IEEE-754's does). The tables are modelled concretely over a
   classification of the float (`pow_table`, `exp_table`, `log_table`), and the host functions are
   PARAMETERS reached only where a table does not answer: `pow_table_before_host`,
   `exp_table_before_host` and `log_table_before_host` prove that two hosts whose functions differ
   arbitrarily answer the same cell on every input a table decides. **What is conditional, named:** every
   theorem of section 14 is over the abstract arithmetic — that a single multiply and divide round the
   same way on every host is the premise `pipeline-ieee-multiply-is-host-independent` (`proofs.json`),
   and the 2 ulp bound is a measurement, never a theorem. Typing (`typing_apply_fn`, `typing_pow_lit`),
   the totality admission (`admit_fn`) and the unit rule are mirrored, and `verdict_sound` carries the
   one extra fact the literal-exponent typing needs (`pow_lit_admitted`: where the verdict admits a power
   under a literal int, the literal rule and the general rule name one typing, because the pair they
   decide differently is the pair the verdict refuses). The oracle is re-extracted and the differential
   extended: the generator draws the four functions, and `powInt`, `powSpecial`, `expHost` and `logHost`
   are compared to the extracted `pow_int`, `pow_table`, `exp_table` and `log_table` instantiated at the
   host's arithmetic.
8. **Measured, on 2026-10-21 (node v24.16.0; .NET 10 on this machine's Arm64 host and on its x64 host
   under emulation — the x64 libm's code, which is what the measurement is about).** The probe evaluates
   each function THROUGH the evaluator (a `Derive` over a float column) over a seeded draw at seed 557 —
   `exp` over 561 inputs spanning `-745` to `709.7` with clusters round zero and the edges; `ln` and
   `log10` over 509 inputs log-uniform across the positive doubles with a cluster round `1`, the
   smallest subnormal and the largest double included; `pow` over 508 (base, non-integer exponent)
   pairs across thirty decades — and reads the canonical result wire; the reference is Python's
   `decimal` at 60 digits rounded to the nearest double, and the distance is the difference of ordered
   bit patterns. The worst observed distance from the correctly rounded value:

   | host | `exp` | `ln` | `log10` | `pow` |
   |---|---|---|---|---|
   | .NET 10, Arm64 | 0 ulp | 0 ulp | 0 ulp | 0 ulp |
   | .NET 10, x64 | 1 ulp | 0 ulp | 0 ulp | 0 ulp |
   | node v24.16.0 (Fable) | 1 ulp | 0 ulp | 1 ulp | 0 ulp |

   No host exceeds the bound; none is a finding. The instrument was checked both ways: the two .NET
   hosts DISAGREE with each other on `exp` at one drawn input (index 46), so a difference is something
   it sees, and the Arm64 zeros are against an independent reference, not against the host. The
   falsifier is a draw at which a host sits more than two ulps from the reference; the probe and its
   draw are reproducible from the seed and the recipe above.
9. **Content hashes and cached results are host-scoped where a `HostRounded` column is in play**
   (`STABILITY.md`): a digest over the output bytes of such a pipeline names the host that produced
   them, so any cache keyed on output bytes must carry the host identity, and a consumer addressing
   results across hosts keys on the pinned columns or on the input.

**Premises checked against the tree.** The README carries no omissions list (the shard's "the README's
omissions list" names a paragraph that was only ever in `DataFrame.fs`); the verb table gained the four
and a guarantee paragraph. The "bit for bit the repeated-multiplication spelling" law was checked
and bounded (item 2). `Unit.pow` raises on an exponent past its range, so the unit rule carries a refusal
the shard did not name (item 4). The wire carries no non-finite float, so the special-value vectors hold
the finite rows and the suite holds `Inf` (item 6).

**Rejected.** *A correctly rounded implementation shipped with every host* (a port of a correctly
rounded libm into .NET, JavaScript and each twin) would make the four functions byte-exact and need no
mark — at the cost of carrying and certifying a maths library in every host, for a difference the
measurement puts at one ulp. Declined for now; the mark and the bound are the honest claim, and the
door is open: a host that becomes correctly rounded measures zero and changes nothing. *Comparing
NaNs equal in the laws* — D16's answer stands: the evaluator emits one NaN. *Refusing a decision over a
host-rounded value* — ruled out by (c): a consumer who filters on `exp(x) > 1` has an ordinary
pipeline, and the walk's report is where the cross-host caveat is read. *Marking `Count` host-rounded*
— presence is pinned by construction (every `Null` the functions answer is decided by the table), so a
count is exact and the mark would be false.

**Supersedes** the Pow/Log line of Fuaran.Core's D13, whose entry carries the status line; the rest of
that entry stands.

## 2026-10-21 — D42: the per-row caches of an incremental state are held in chunks below the large-object threshold, so an append shares the prior chunks; the refresh-against-full case draws each call's source from a pool; the 20,000-row append is timed, and D27's 448.C reach is closed

**Context.** D27 held the refresh alone against the full evaluation at every size for an edit, but for
an append only at 1,000 and 100,000 rows. At 20,000 rows the append's refresh on `byRegion`,
`filter > groupBy` and `filter > groupBy > filter` read 0.8 to 2.5 times a full evaluation of 0.24 to
0.45 ms, and Phase 448 named two causes: the large-object growth of two per-row arrays, and an
instrument that re-refreshed one (state, delta, source) triple. Phase 451 was filed for both (448's
deferred task 448.t3).

**Both premises were checked on this tree before anything was built.** A probe counted the runtime's
large-object allocation events over 500 in-place appends at 20,000 rows. It found exactly one 160 KB
`string[]` (the row tokens) and one 160 KB `Cell[]` (a step's cells) per refresh on the three nodes,
and nothing else on the large-object heap. On `byRegion` the `Cell[]` is the `Derive` step's cells,
which are also the walk's frame column, so chunking the cache alone would not have reached it. A third
per-row cache, the rows' groups (`int[]`), was copied whole on every in-place refresh too, and on an
edit the copy was then discarded. At 20,000 rows it is 80 KB, just under the threshold, and it crosses
the threshold at 21,250 rows. It is the same class of array and is taken in the same change. The
instrument premise held as well; the A/B is below.

**Decision.**

1. **The three per-row caches are `RowChunks`**: chunks of 8,192 entries, 64 KB of references, each
   owned by the cache that allocated or copied it. `RowChunks.grow` shares every chunk of the prior
   cache that keeps its length, copies the one it extends, and allocates the new ones. A shared chunk
   is copied before its first write. So a prior state's caches never move, and two refreshes of one
   state see only their own writes. A state built by a full walk, a prime included, is chunked too, so
   the first refresh after a prime shares as every later one does. No array a state keeps per row is
   ever a large object.
2. **A `Derive`'s column in the walk's frame is the step's chunks** (`WalkFrame.Cached`). A step that
   reads only the rows a refresh walks reads them off the chunks (`cellAt`, `cellAtSlot`,
   `cellsAtSlots`, the patched result column). A step that reads the column whole (a sort, a window, a
   group streamed in full) concatenates it once (`column`).
3. **The wire form is the flat arrays'.** The encoder flattens each cache (`RowChunks.toArray`) and the
   decoder chunks it again, so no encoded byte, corpus vector or result value moves.
4. **The refresh-against-full case draws each call's source from a pool** of prepared (source, delta)
   pairs (`ScalingTests.refreshPool`: 128 at 1,000 and 20,000 rows, 16 at 100,000), as a consumer's
   tick has a source of its own. The full evaluation it is held against still re-evaluates one source,
   so the bound is not loosened. The case prints the generation-2 collections each refresh timing drew.
   `refreshSizes` is one list, and the 20,000-row append is timed on every node the seam accepts.

**The A/B** (Arm64, Release, the case narrowed to the append at 1,000 then 20,000 rows, two runs per
arm; the figures are in `docs/performance.md`, "A 20,000-row append refresh is held against the full
evaluation"). The heap budget was moved two ways: the generation-0 budget raised to 256 MB, and the
large-object threshold raised past the caches. On the pre-451 library (`5cd9ecc`) with one source, the
three nodes read 0.55 to 1.88 times the full, with 1 to 23 generation-2 collections per cell, and went
red on a counted attempt in both default runs. With the pool they drew no generation-2 collection, but
`filter > groupBy` still read 0.11 to 0.17 ms across the three budgets. On this phase's library with
the pool, it read 0.086 to 0.095 ms on all three. One source on this phase's library held the three
grouped nodes steady too, but not `lines`, whose refresh still allocates its result's 160 KB column:
the pool stays.

**448.C is closed** (448.t3). One clock leg of the case on this tree read the 20,000-row append at
0.09 to 0.43 of the full evaluation on every node the seam accepts. Across every size and both ticks
no cell read above 0.85. A main-suite case pins the representation: over every node the seam accepts,
a prime and two chained appends at 20,000 rows leave no per-row array of 85,000 bytes or more, and
each append shares every chunk of the prior caches it does not write. It went red under each of two
perturbations, run one at a time: `grow` returning one flat array, and `grow` copying the chunks it
shares.

**Considered and rejected.**

- **A shared buffer with spare capacity**, written past the prior's length by the next append. Two
  refreshes of one state, which the seam allows and the suite does, would both write the same spare
  slots, and one state would read the other's rows.
- **Raising the large-object threshold** (`GCLOHThreshold`). It is a process-wide runtime setting.
  A library cannot own it for its consumers, and it only moves the cliff.
- **Flat caches after a full walk, chunked from the first refresh.** It saves a prime the chunking,
  but the first refresh after every prime then re-chunks the whole state. That is the refresh the case
  times, and the one a consumer pays first.
- **Timing the full evaluation over the pool too.** It is the symmetric choice, but it can only make
  the full evaluation dearer and the bound easier, so it is not taken.

**Not taken here, and why.** The walk's own per-slot scratch (`Prior`, `Stable`, a filter's order) is
allocated per refresh and dropped. It crosses the threshold at about 21,000 rows for an `int[]` and
85,000 for a `bool[]`. It is not a cache and is outside this phase's acceptance. A refreshed result's
computed column is the consumer's answer, and a 20,000-row column of floats is 160 KB whatever the
seam does. The group order (`GroupOrder`) has one entry per group, not per row.

**Consequences.** No public signature, answer or wire byte moves (`STABILITY.md`, class `none`,
`performance`), and the `0.41.0` draft is ridden. The clock leg's case count is unchanged. The main
suite gains one case.

## 2026-10-16 — D41: the relational model's union refuses a pair no column type holds together through the cells' typing's own definitions, and the relational differential excuses nothing — its D18 arm was dead and is gone; the frame reads a packed cell through `Pipeline.fst`'s `pack_cell`, tied by a proved equality; D38 item 1's twin sentence is corrected

**Context.** Phase 470 (D38) left two things out of its scope. The relational differential in
`ProofOracleTests` kept an excuse arm — a production `TypeError` "which no column type holds together"
against a model `Ok` was tallied as outside the model's domain — and `Frame.fst`'s per-cell packing was
a twin of `Pipeline.fst`'s (D38 item 1). Phase 476 was filed to close both.

**The first premise was false, and was checked before anything was built.** The shard said
`Relational.fst`'s `Union` "still lacks the D18 refusal". It did not: Phase 469 (D36) modelled the
union's per-column rule (`union_ty`, F#: `unionField`), and `union_types` has refused a pair no column
type holds together, with the D18 text, since then; the pinned `423-union-refuses-unrelated` shape
compared that refusal on both sides. Production's D18 refusal of a union reads the two SCHEMAS
(`unionField` over the two fields' types), never the cells, so there was no cell-level refusal left for
the schema model to lack. MEASURED: with the arm deleted and nothing else changed, the relational
differential is green and its `rowRefused` tally is 0 — the arm fired on no step, generated or named.
What WAS true is narrower: the model restated what it should have reused. `types_beside_each_other`
was a byte-identical twin of `Pipeline.types_beside`, `type_tag` a twin of `Pipeline.type_tag`, and
`join_column_type` re-derived the join from `widens` both ways instead of reading the cells' typing.

**Decision.**

1. **The union's column typing reads the cells' typing by name.** `join_column_type a b` is
   `if disjoint_types a b then None else Some (widen_type a b)` — `Pipeline.disjoint_types` is
   `typeFromCells`' test of a pair no column type holds together and `Pipeline.widen_type` its join, so
   a union column and a derived column refuse one pair by one rule. It equals the old definition by case
   analysis (`widens` relates equal types and an int to a float or a decimal, nothing else). The refusal
   is `Pipeline.types_beside`; `types_beside_each_other` and `type_tag` are deleted. `union_ty` keeps its
   shape (`unionField`'s equal-types arm first). Every relational theorem is re-proved with its statement
   unchanged — `union_sound`, `union_walk_widens`, `walk_sound` and the rest — and none needed a proof
   edit.

2. **The excuse arm is deleted, and the comparison is shown to reach the refusal.** The differential now
   runs every ordered pair of the generator's seven column types as a one-column union (`rUnionPairs`,
   49 shapes, a null row each) beside the named shapes, and counts a union refused on D18 by BOTH sides
   with the same text (`RBeside`); the case asserts at least 39 — the 38 pairs no widening relates and
   the 423 shape — and measures exactly 39. **Go-red, one perturbation, two runs** (on the extracted
   oracle, so no prover run was spent): `union_types` answering the left type where it refuses. With the
   arm restored as it stood, and the new count's floor relaxed to 0 as the case stood before, the
   differential is GREEN and the 39 refusals are excused (`rowRefused=39`); without the arm the same
   perturbation is RED on all 39 (`production refused TypeError … model evaluated to …`). So the arm
   would have hidden exactly the defect it named, had the model ever had it.

3. **The frame reads a packed cell through Pipeline's definition, tied by a proved equality.** One
   definition is not possible within this phase's files: `Pipeline.pack_cell` reads its two carriers
   (`float_of_int`, `int_text`) off `prims`, `Frame.fst` off `host`, and the records share exactly those
   two fields under the same names; making them one definition means narrowing what `Pipeline.pack_cell`
   takes, a `Pipeline.fst` change and a re-proof of every model that opens it. So: inside `Frame.fst`
   the int's widening has ONE definition — `norm_cell`, the typed carrier's reading, now re-renders the
   temporal texts and reads every other cell through `widen_cell` — and two theorems tie it to Pipeline's
   by name: `widen_cell_is_pack_cell` (the boxed cell IS `Pipeline.pack_cell pr (erase ty)` wherever
   `pr` and `h` agree on the two carriers) and `pack_cell_is_pipeline` (so `Vec.pack`'s read-back is
   Pipeline's packing at every non-temporal type, typed or boxed). A temporal column differs only where
   the typed carrier re-renders a text that was not canonical, which is what the premise
   `pipeline-temporal-cells-canonical` already names. `pack_reads` and every frame theorem keep their
   statements. The premise is checked where it is instantiated: a `Proofs.Oracle` case holds
   `ModelFrame.pack_cell fHost` and `widen_cell fHost` to `ModelPipe.pack_cell pPrims` over drawn
   columns at the five non-temporal types.

4. **D38 item 1's twin sentence is corrected in place.** It gave the wrong reason the two could not be
   one definition (`Frame.fst` opening `Pipeline` is what lets the frame READ Pipeline's definition);
   the reason is the carrier record, and the two are now tied.

**Evidence and cost.** The proof leg is green with every model checked and extracted (`check.ps1 -Extract`, one cold run, a contended pass at x0.89): `Relational` 54s against its 120s budget (recorded 57s; 62s on the authoring run), `Frame` 69s against 130s (recorded 64s; 69s on the authoring run too), `IncrementalMaintained`, which opens `Relational`, 208s against 480s. Both moved modules stay inside their budgets and no cost finding was raised, so `modules.json` is not re-seeded: a 5s rise on a contended pass is not a re-seed obligation by the file's own rule, and the figures are recorded here instead. No production code moves and `STABILITY.md` is untouched: production
already refused what the model now refuses through Pipeline's definitions.

**Consequences.** No proof differential in the repository excuses a production refusal the reference
does not model. `Relational.fst` re-models nothing `Pipeline.fst` models. The frame and the list model
read a packed cell through two definitions tied by a proved equality; one definition remains available,
at the price of narrowing `Pipeline.pack_cell`'s record, and nothing here needs it.

## 2026-10-16 — D40: an int under a float or a decimal column reads as that type's value whatever the carrier — `Vec.packAt` widens it on the boxed form as on the typed one, so a cell's reading is a function of the cell and the column's type alone; the pack verdict leaves the walk's key, `packs_as_is` leaves the reorder theorem, and the models pack cell by cell; Core already read so, and nothing there moves; D38 item 5 is closed

**Context.** D38 item 5 named the faithful alternative to tracking `Vec.packAt`'s VERDICT — typed, or boxed
with every cell as it was — through the incremental walk: widen every int under a float or a decimal
column on the boxed form too, so that the verdict vanishes, the pack key is the type alone, and the
premise `packs_as_is` Phase 470 had to give `derive_then_filter` and `reorder_in_context` is unnecessary.
The operator chose it on 2026-10-15 (Phase 472), under the 2026-10-12 ruling that a behaviour change in a
published package is not a reason to keep a defect. The defect: under the verdict, one unrelated row (a
decimal text past the scaled carrier's width, arriving or leaving) changed how every stable int in the
column read, with no delta naming those rows — the shape Phases 464 and 470 each found production
diverging on, and each fixed by tracking more state.

**Decision.**

1. **`Vec.packAt` widens on the boxed form.** The boxed fall-back — taken on a present cell the type does
   not carry, and for a decimal column on texts `ScaledDecimal.scaleOf` cannot carry at one scale — reads
   an `Int` under a `FloatType` column as `Float (float v)` and under a `DecimalType` column as
   `Decimal (string v)` (`widened`), exactly as the typed carrier reads it back through `Vec.cellAt` and
   as Core's `Column.ofCells` holds it. Every other cell is as it was; no other type is touched. `Cell.Int`
   is a 32-bit int (D39 item 1), exact in a float and in a decimal, so the widening loses nothing. A
   `behaviour` change on the open `0.41.0` draft (`STABILITY.md`): what a consumer observes is a `Float`
   or a `Decimal` cell where a boxed column answered an `Int`, and a refusal where arithmetic between an
   int and a float once answered over a boxed decimal column.

2. **The walk's verdict tracking is deleted, not maintained.** `Incremental.fs`'s `typedPack` (D38's
   mirror of the verdict) is gone; `widenCells` widens an int under a float or a decimal column
   unconditionally again, as D37 first wrote it; `retypedSince` compares the type alone, as D37 wrote it,
   and no longer takes the step's cells. The two production divergences 464 and 470 closed stay closed by
   construction: there is no verdict left to disagree about.

3. **The Core half's premise is false, and nothing in Core moves.** The shard asked for `Column.ofCells`
   to widen on "the boxed path, as on the typed one". Core has no boxed path: `ColumnData` is the seven
   typed cases and nothing else, `storageOfCells` holds an `Int` under a `FloatType` column as `float i`
   and under a `DecimalType` column as `string i`, and REFUSES (`TypeMismatch`, the first offending cell
   named) any present cell the type does not carry. A Core column already reads an int under a float
   column as its float on its only path, pinned by Core's own suite (`ColumnTests`: "ofCells normalises a
   widened Int: Float in a float column, Decimal in a decimal column"). The two halves are therefore
   independent: this repository widens in `Vec.packAt` before any cell reaches Core, and a boxed vector
   handed to `Column.ofCells` (through `Vec.toColumn`'s boxed arm) was already widened there. No pin is
   raised: Core `0.38.0` is an untagged draft and this repository pins the public `0.37.0`, which this
   change does not need. Core's `DECISIONS.md` D161 records the same finding from its side.

4. **The models pack cell by cell.** `Pipeline.fst`'s `pack_cells pr ty` is `map_cells (pack_cell pr ty)`;
   `pack_admits`, `all_admitted`, `dec_texts`, `dec_mask`, `typed_pack`, `pack_one`, `pack_with`,
   `packs_as_is`, `pack_with_as_is` and `pack_cells_as_is` are gone, and `prims` loses `scale_of` (the
   packing consults no scale). `derive_then_filter` and `reorder_in_context` are proved with their
   pre-470 statements — Phase 269's three premises, no `packs_as_is` — because a per-cell packing commutes
   with the filter's selection: the row lemma `filter_ext` is stated over the per-cell map `g` the derived
   column went through (`zip_ext k rows (map_cells g ws)`), and its proof is the old one with `g w` for
   `w`. `kept_cells_has_type` and `derive_cells_fit` are gone with the premise; `planner_reorder_sound`
   keeps Phase 338's statement standing on `derive_total` alone. `Incremental.fst` records the type per
   derive (`entry.e_pack : option column_type`; `pack_key`, `pack_key_of` and `cells_of_pack_cache` are
   gone), `pack_cache pr ty` maps `pack_cell`, and `walk_reuse` switches on `ty0 = ty`;
   `IncrementalMaintained.fst` the same. `Frame.fst`'s twin `pack` (the frame model, held to `Vec.pack`
   by the frame differential) widens on its boxed arm too (`pack_boxed`), and `pack_cell` there reads
   the boxed cell through `widen_cell`. Every other theorem over the derive is re-proved with its
   statement unchanged (`prime_is_full`, `refresh_is_prime`, `refresh_agrees`, `mprime_is_full`,
   `mrefresh_is_prime`, `mrefresh_agrees`, `eval_total`, `budget_monotone`, `work_bounded`,
   `verdict_sound`, `planner_reorder_sound`, `pack_reads`). The temporal-text premise
   (`pipeline-temporal-cells-canonical`) stands, worded over `pack_cell`.

5. **Evidence.** The go-red probe (`ProofOracleTests`, "an int under a float or a decimal column reads as
   that type's value on the boxed form too …"): `Vec.pack FloatType [| Int 1; Str "x"; Null |]` is boxed
   and reads `Float 1.0` at row 0, `Vec.pack DecimalType [| Int 1; wide; Int -30 |]` is boxed and reads
   `Decimal "1"` and `Decimal "-30"` — red before this phase; the model packs the same cells the same
   way; and a model that keeps Phase 470's verdict (`pVerdictDerive`, kept once) answers the first boxed
   shape (`pBoxedShapes`: an int beside a decimal past the width, read by an `Add` of a float) where
   production and the corrected reference refuse `arithmetic between a decimal and a float`. The two
   boxed shapes still hold the refresh to the full evaluation, and `pCornerAgrees` holds the three models
   to production on every retype and boxed shape. The module budgets are re-measured in `modules.json`.

**Consequences.** D38 item 5 is closed; D38 item 4's premise is retired with its reason. A column's
reading no longer depends on rows other than the one read, in the full evaluation, the incremental
refresh and the planner's reorders. `Vec.packAt`'s boxed form is no longer a reading of its own: a kernel's
reference arm over a boxed float or decimal column never meets an `Int`.

## 2026-10-15 — D39: the maintained `Sort` is modelled and proved — the merge of the moved rows into the prior produced order by binary insertion, under `arrivedAsBefore`, answers the full stable sort, in a module of its own under `IncrementalMaintained.fst`; the comparator is modelled concretely and proved a total preorder within a typed column, so the incomparable-pair lead of D37 item 1 does not arise and no production code moves; D37 item 1 is closed

**Context.** D37 declared `Sort` out of the maintained walk's model by name: production's refresh of a `Sort`
(`Incremental.fs`, `MergeOrder`) merges the rows the delta moved into the prior PRODUCED order by binary
insertion (`mergeOrders`, Phase 274), reusing that order only while the stable rows arrived in the same
relative order as before (`arrivedAsBefore`, Phase 215), and proving that equal to the full stable sort
needed two things Phase 464 did not start — the uniqueness of a sorted permutation under the comparator
read as a strict total order with the arrival position as the tie-break, and a comparator premise, since
across cell types `compareCells` answers `None`, which `compareResolved` reads as `0`, and a tie that is
not transitive makes a sort depend on its input order. Phase 471 was cut to do both: the premise check
first, against production, then the model composed with 464's walk and 470's packing reference, the
agreement and state theorems, a differential with a go-red, and the census moved.

**Decision.**

1. **The premise check: a column reaching a `Sort` holds one family, and within one family the
   comparator is a total preorder — the lead does not arise, and nothing in production moves.** The
   shard asked whether a column reaching `Sort` can hold cells of more than one type, and the driver
   named the lead: after D38 a BOXED column under a float or decimal type (the typed pass abandoned)
   holds `Int` cells beside `Float` / `Decimal` ones. Established against `DataFrame.compareCells`
   (`DataFrame.fs`) and Core's `Cell.compare`: an int against a float compares in the float carrier
   (`Kernels.compareFloat` over `numOf`), an int against a decimal EXACTLY (`Cell.compare`:
   `DecimalText.compare` over `string i` and the canonical text), an int against an int in the float
   carrier — and **`Cell.Int` carries a 32-bit int, exact in a float and in a decimal alike**, so the
   three readings of a mixed pair agree with the exact numeric order and the boxed column is one total
   preorder whichever cells stayed raw. `compareCells` answers `None` on exactly three shapes: two
   families (a string beside an int), a float beside a decimal, or a `Decimal` whose text is not decimal
   text — and no frame a `Sort` reaches holds any of them: `typeFromCells` refuses two families and a
   float beside a decimal (D18, D38 item 3), `Vec.packAt` carries an int under a float or decimal column
   as that type or raw, every decimal primitive and `castCell` refuse a text outside the grammar, and a
   temporal cell's text is canonical (`pipeline-temporal-cells-canonical`). So the cross-type case does
   not arise from the ints Phase 472 will widen, and it does not arise any other way: there is no
   production defect to fix here, `Incremental.fs`, `DataFrame.fs`, `STABILITY.md` and `api/` are
   untouched, and the typing fact is stated as the premise the two theorems compose through (item 3).
   Pinned on production directly (`ProofOracleTests`): a boxed decimal column holding `Int32.MaxValue`,
   `Int32.MinValue`, `0`, `5`, `-7`, `2147483646` and a decimal past the scaled carrier's width sorts to
   ONE value sequence — the exact numeric order — from every one of ten arrival orders; and the two sort
   paths production runs, the order codes where every key's vector codes (`Ordering.codesAll`) and the
   comparator otherwise, answer one order over three hundred generated tables under drawn keys.

2. **A module of its own, `proofs/MaintainedSort.fst`, opening `Pipeline` and `Incremental`, under
   `IncrementalMaintained.fst`.** `IncrementalMaintained` was 229s of a 400s budget after D38, and the
   sort's lemmas are a theory of their own — a strict total order over a list's members (`sto_on`),
   sortedness, the insertion sort (`isort`, F#: `List.sortWith` as the reference's `evalSort`), the
   uniqueness of a sorted permutation (`sorted_unique`), the bisection (`bisect`, the `while a < b` loop
   with its invariant `bisect_cut`) and the merge (`merge_bin`, F#: `mergeOrders` — each moved row placed
   in the cached run by bisection under "goes out before", the run before it copied whole, the search
   continuing from there), positions (`indexed`, F#: `posOf`) and the order `lt_of` they make with a
   total preorder (F#: `before`, and `sortSlots`'s comparison: the comparator, then the position) — so
   they live where `IncrementalMaintained` can open them by name and cost nothing to its budget. The
   definitions the sort's theorem is stated over — `tokens_of`, `distinct`, `mem_a`, `wk`, `ok_row`,
   `wk_mem`, `wk_of_rows`, `mem_tokens`, `assoc_of_mem`, `assoc_mem` — MOVED down from
   `IncrementalMaintained.fst` verbatim, so there is one `wk` and one `distinct`. The step is modelled
   concretely (`sort_step`): the cached order merged where `arrived_as_before` holds — the stable tokens
   of the prior arrival still here, read in step with the stable tokens now, equal (F#: `arrivedAsBefore`
   compares the two sequences without building either) — with the cached run the prior produced order's
   tokens read through the rows that hold them now (`lookup_order`, F#: `prevOrder` through
   `stableNow`) and the moved rows sorted among themselves (`unstable`, F#: `arrival |> Array.filter
   (fun s -> not r.Stable[s])` then `sortSlots`); the whole arrival sorted from scratch where it does
   not (F#: `reusable = None`); and the step records its arrival and what it produced (`sort_record`,
   F#: `SortOrders[sortIdx] = (arrival, ordered)`). `IncrementalMaintained`'s alphabet gains `MSort`
   (F#: `WSort`, its keys literal — `split` refuses a param key and the walk never sees one), `entry`
   gains `e_order`, `mwalk` gains the arm (the reference's unknown-key refusal first, Phase 412),
   `mrows` the row function (`sort_rows`: the stable sort under the comparator over the frame's rows),
   and `mwalk_is_go`, `mwalk_no_reuse` and `mwalk_reuse` the arm each: a prime sorts from scratch and
   its rows are the reference's with the tokens riding along (`rows_of_sort_alive`), a walk reusing
   nothing sorts from scratch whatever record it was handed (`sort_step_no_reuse` — the merge of no
   cached row with every row moved IS the sort, F#: an empty `kept`), and a refresh answers the prime's
   sort (item 3).

3. **The theorems, and what each assumes.** `sort_step_reuse` (THE STEP THEOREM): a `Sort` refreshed
   against the record a prime left — the prior arrival and the prior full sort, as tokens — under the
   delta premise read one row at a time (`wk`), the prior source's tokens distinct, and the row
   comparator a total preorder, answers the full stable sort of the rows that arrived; `mrefresh_is_prime`
   and `mrefresh_agrees` carry it through the walk with their statements as D37 left them plus the one
   premise (`row_cmp_preorder sp`). The proof is the one the shard named: the merge of two sorted,
   disjoint runs is sorted with their union's members (`merge_bin_sorted`, over `bisect_cut`), and a
   sorted permutation under a strict total order is unique (`sorted_unique`), so the merge is the
   insertion sort of the arrival (`merge_is_isort`) — provided the cached run IS sorted under the order
   NOW, which is where `arrivedAsBefore` earns its keep (`lookup_sorted`, through `relabel_lt`): a prior
   stable row ordered before another is, read now, still before it, because its cells are the cells it
   had (`stable_cells`, from `wk` and the distinct tokens) and its position in the arrival now is on
   the same side of the other's as it was — a filter keeps the relative order of what it keeps
   (`filter_order`), and the two stable sequences `arrived_as_before` asserts equal are two such filters
   (`pos_is_idx`, `stable_pos`, `distinct_pos`). The cached run's members are exactly the stable rows
   now (`cached_memb`: the stable tokens now are distinct, `stable_now_nodup`, so a token finds one row,
   `find_tok_unique`), the moved rows the rest, and the premise survives the permutation on both sides
   (`sort_alive_wk`, through `assoc_perm`). `compare_resolved_preorder` (THE COMPARATOR THEOREM):
   `compare_resolved` — `compareResolved` restated arm for arm over `compare_cells`, `compareCells`
   arm for arm — is a total preorder over rows whose cell at each key is what a column of that key's
   type holds (`typed_row` through `fits`: its own type, an int under a float or a decimal column, a
   decimal whose text is decimal text, or `Null`), from the primitives' laws alone (`sprims_laws`,
   item 4): the per-key comparison is one (`key_cc_refl` / `key_cc_anti` / `key_cc_trans`, from
   `cells_preorder` — the decimal family through one key `dkey`, `cc_decimal`; the float family through
   the carrier, `cc_float`) and the lexicographic composition of preorders is one (`lex_preorder`).
   `compare_cells_none` names the pairs read as a tie, which is item 1's list. **What the two theorems
   compose through, and do not prove:** the models do not track cell typing through the walk (`wf` is
   the frame's width alone), so the step theorem is stated for a comparator that is a preorder
   everywhere (`row_cmp_preorder`) and the comparator theorem for the rows a typed frame holds; the
   composition is production's typing fact of item 1, carried as the premise row
   `incremental-maintained-sort-primitives` with `unscheduled` the typing invariant through
   `Pipeline.fst`'s evaluator that would discharge it. Stated rather than weakened silently.

4. **The parameters, each an assumption, and what the differential checks of each.** `sprims`: the row
   comparator the walk and the reference both call (`row_cmp` — F#: `compareResolved (resolveSortKeys
   cols by)`), the float carrier's order (`cmp_float` — `Kernels.compareFloat`), the decimal order and
   canonical form (`cmp_dec`, `canon_dec` — `DecimalText.compare`, `tryCanonical`), the ordinal order
   (`cmp_ord` — `String.CompareOrdinal`), beside Phase 470's `float_of_int` and `int_text` on `prims`.
   `sprims_laws`: the float and ordinal orders are total preorders; the decimal order is one over
   canonical decimal texts, where it always answers; an int's text is canonical, a canonical text is its
   own canonical form; and `compareFloat (float i) (float j)` IS `DecimalText.compare (string i)
   (string j)` — the 32-bit fact of item 1 as a law. Every one is instantiated at production's own and
   checked there on pools that include `NaN`, `-0.0`, the infinities, the int extremes and a decimal past
   the width; the model's `compare_resolved` under them answers what production's `compareResolved`
   answers on every pair of rows of three hundred generated typed tables under drawn keys.

5. **The differential, its go-red, and the acceptance's perturbation.** The maintained generator draws a
   `Sort` among the prefix steps — over the group column, where ties are the rule and the arrival
   position decides; over the int or the decimal column descending, nulls among them; over two keys —
   and, one time in three, REORDERS two rows of the mutated table after the mutations: `Delta.diff`
   reports a reordering as quiet, so a stable sort's answer moves while no row is named, the case
   `arrivedAsBefore` exists for; the tally counts sorted pipelines, produced orders that moved and
   refreshes under a reordering, and holds each above a floor. The go-red (`mStaleArrival`): every sort
   record's arrival overwritten with the arrival the refresh sees, so `arrived_as_before` never fails and
   the merge reads the prior produced order under the new positions — it disagrees with production where
   the stable rows were reordered. The acceptance's perturbation, made in the model and run once: a
   binary insertion that ignores the arrival position on a tie (`lt_of` as the comparator alone) — the
   prover refuses `lt_of_sto_indexed` (two rows that tie are neither before the other, so there is no
   total order over the arrival) and `sort_step_reuse` with it: SEMANTIC, `MaintainedSort.fst(493)`, "Failed to prove: sto_on (lt_of cmp) l" — the totality clause,
   two rows that tie being neither before the other; restored, the module verifies.

6. **Cost and evidence.** `MaintainedSort.fst` is seeded in `modules.json` from one cold run of the leg on this
   machine, 50s under `--quake 3` (a contended pass by the kit's own label, x0.93, the kit's self-tests
   and a test build beside it), and a direct run under `--quake 1` during authoring, 36s — a 100s budget
   and an 18s floor by the file's two rules. `IncrementalMaintained.fst`, re-measured with the arm: 185s
   under `--quake 3` in the same leg against the 198s recorded and the 400s budget, 104s direct — neither
   number moves. Every theorem D37 and D38 stated is re-proved with its statement unchanged but for the
   one premise (`row_cmp_preorder`); the oracle gains `MaintainedSort.fs` (726 lines extracted) and the
   suite's `Proofs.Oracle` family gains four cases (item 5), the maintained differential's tally reading
   155 sorted pipelines, 92 produced orders moved and 33 refreshes under a reordering over 700 trials,
   with the sort go-red disagreeing 21 times. Raised to 700 trials from 500 so the existing adequacy
   floors (a window's partition key moved among them) keep their room beside the new draws.

**Consequences.** D37 item 1 is closed: `Transform.Sort` joins `Incremental.prime` and `Incremental.refresh`
on the ladder's proved rows for the maintained walk, and the `Incremental` census entry's reason no longer
names the merged `Sort` among what no model states. Still declared out, each with D37's reason: the resumed
prefix-fold window, the in-place and append fast paths, the staleness guards, the quiet-delta shortcut, the
chunked path and the codec. Phase 472's widening (D38 item 5's faithful alternative) stands on its own
grounds — cell-locality of a column's reading — and not on this phase's: the sort is sound over the raw ints
too.

## 2026-10-15 — D38: the reference model packs a derived column as production does — `Vec.packAt` read back, verdict and all, as one definition the three models share; the cells' typing refuses two types beside each other as production does; the planner's reorder theorem names the packing premise its corollary discharges; and the walk's switch compares the PACK KEY, which closes a second production divergence (the typed pass abandoned)

**Context.** D37 item 4 named a gap in the REFERENCE model: `Pipeline.fst`'s `eval_derive` held a derived
column's cells raw (`zip_append f.rows cells`) where production packs them under the decided type
(`Vec.packAt`: an int under a float column is the float of its value, under a decimal column the decimal
of its value), and `Incremental.fst`'s `extend` held them raw too, so the pipeline differential and Phase
433's `prime_is_full` were stated against a reference that differs from production exactly where D37's
production defect lived. Phase 470 was cut to close it: the packing modelled on production's, as ONE
definition the three models reuse, every theorem over the derive re-proved with its statement unchanged,
the differential's generators reaching the corner, and the old raw reference shown failing on it once.

**Decision.**

1. **The packing, modelled concretely on `Vec.packAt` as the column reads back through `Vec.cellAt`**
   (`Pipeline.fst`, `pack_cells`): one pass over the whole column decides a VERDICT — carried typed when
   every present cell is one the type carries (`pack_admits`: its own type, or an int under a float or a
   decimal column) and, for a decimal column, when `ScaledDecimal.scaleOf` answers a scale over the
   column's present texts (an int's rendered as `string v`); else the typed pass is abandoned and the
   column is BOXED, every cell as it was. Under the verdict an int in a float column reads back as the
   float of its value and in a decimal column as the decimal of its value (`pack_cell`); every other cell
   reads back as itself. `eval_derive` packs its cells under the type `column_type_by` decides, and the
   two walks pack the same cells through `Incremental.pack_cache` (the cache's cells under the key, each
   under its token), whose cells ARE `pack_cells` over the raw cells (`pack_cache_is_pack_cells`) — the
   one definition the shard asked for. The verdict being a function of the WHOLE column is what the rest
   of this record is about. It is the twin of `Frame.fst`'s `pack` / `pack_cell` on the list column. (Corrected by
   D41, Phase 476: this record first said the two could not be one definition because `Frame.fst`
   opens `Pipeline` and not the reverse — the wrong reason, since that is what lets the frame read
   Pipeline's definition. What keeps them two is the carrier record, `prims` here and `host` there;
   they are tied by a proved equality, `widen_cell_is_pack_cell` and `pack_cell_is_pipeline`.)

2. **Three parameters more, each an assumption the theorems make nothing of beyond its type:** the float
   carrier of `float v` (`float_of_int`), `string v` (`int_text`) and `ScaledDecimal.scaleOf`
   (`scale_of`), carried on `prims` under the names `Frame.fst`'s host carries them, so the differential
   instantiates both models from one production function (`pPrims` beside `fHost`). What `scaleOf`
   answers — the width, the grammar — is neither modelled nor claimed: a decimal text is an opaque
   carrier here as it has been since Phase 277. One thing is read as the cell's own text by a NAMED
   premise (`pipeline-temporal-cells-canonical`): a date or a timestamp the typed carrier reads back as
   the canonical text of its value, which is the cell's text when that text is canonical — every
   primitive produces one, and only a literal can carry another into a derive — so `pack_admits` carries
   a temporal cell as itself where `packAt` consults `TemporalText`.

3. **The cells' typing refuses two types no column type holds together, as production has since D18.**
   `typeFromCells` refuses a derived column whose present cells span two types the widening join does
   not relate (`typesBesideEachOther`, D18 item 2), and the model's `column_type_by` did not — the
   differential excused the pair as outside the model's domain. Modelled now (`first_disjoint`: the first
   pair the fold met, the type so far beside the cell's, named in the refusal's own text), and the excuse
   is gone from the pipeline differential. It matters to the packing: with it, a column the cells type
   holds one family only, so the typed pass is abandoned on exactly one reachable corner — a decimal
   column past the scaled carrier's width — and that corner is where item 5 found production diverging.

4. **The planner's reorder theorem names a packing premise; the corollary does not.** Under the
   column-wide verdict `derive_then_filter` is FALSE as Phase 269 stated it, over arbitrary primitives:
   a primitive that yields, for a derive the typer decides `float`, an int on one row and a string on
   another, with the filter dropping the string's row, boxes the column on the derive-then-filter side
   (the int reads raw) and carries it typed on the filter-then-derive side (the int reads as a float).
   The theorem (`derive_then_filter`, `reorder_in_context`) now requires `packs_as_is`: the decided type
   carries the derived cells as they are — no int under a float or a decimal column — under which the
   packing is the identity on the whole column and on the kept rows alike (`pack_cells_as_is`,
   `kept_cells_has_type`). **The corollary `planner_reorder_sound` keeps Phase 338's statement exactly**:
   the verdict's premise `admits` already gives every derived cell the typing's type (`verdict_sound`),
   from which the packing premise follows (`derive_cells_fit`), so the planner's admission is proved as
   before. The premise is also read off production's own primitives by a `Proofs.Oracle` case over drawn
   tables and expressions. Stated here rather than weakened silently, per the phase's own rule.

5. **The walk's switch compares the PACK KEY — the decided type AND the verdict — and so does production's
   `retypedSince`; a second production divergence, found and fixed.** D37 recorded the type: a derive
   whose decided type is not the prior evaluation's reuses nothing from there on. The verdict is a
   function of the whole column as the type is, so a named row can flip it under ONE type — a decimal
   text past the width arriving, or leaving — and every stable row's int then reads the other way
   (`Decimal "1"` on the typed pass, `Int 1` on the boxed one) with no delta naming the row. The models
   record the key per derive (`Incremental.entry.e_pack`, `IncrementalMaintained.entry.e_pack`) and
   switch to `no_reuse` where it moved (`walk_reuse`'s two arms: `unchanged_extend` under one key,
   `walk_no_reuse` past another; `mwalk_reuse` likewise). Production's walk (`Incremental.fs`) widened an
   int under a float or decimal column UNCONDITIONALLY (D37's `widenCells`) where `Vec.packAt` abandons
   the typed pass, and compared the type alone: a probe (`pBoxedShapes` in `ProofOracleTests`) showed
   the full evaluation answering `Float 1.5` where the refresh refused `arithmetic between a decimal and
   a float`, on a derived column a named row retyped from int to decimal and boxed in one move. Fixed
   under the operator's 2026-10-12 ruling: `widenCells` widens only under `typedPack`'s verdict, which
   mirrors `packAt`'s (admission, and `ScaledDecimal.scaleOf` over the rendered texts for a decimal
   column), and `retypedSince` compares the type beside the verdict. A `behaviour` change on the open
   `0.41.0` draft (`STABILITY.md`). The faithful alternative — widening every int in the boxed carrier
   too, in `Vec.packAt` and Core's `Column.ofCells`, so that an int under a float column is ALWAYS the
   float of its value and the verdict vanishes — would make item 4's premise unnecessary and the key the
   type alone; it changes the full evaluation's answer on a degenerate column in two repositories and is
   the operator's to decide, not this phase's.

6. **Cost and evidence.** Every theorem over the derive is re-proved with its statement unchanged
   (`prime_is_full`, `refresh_is_prime`, `refresh_agrees`, `mprime_is_full`, `mrefresh_is_prime`,
   `mrefresh_agrees`, `eval_total`, `budget_monotone`, `work_bounded`, `verdict_sound`,
   `planner_reorder_sound`) except the two of item 4, whose premise is new. The differential's generators
   draw the corner one pipeline in four (`pCornerShapes`: a float retype read by a `Mod`, a decimal
   retype read by an `Add` of a float, and a decimal retype past the width, each on the rows that stand);
   the three models are held to production on every retype shape (`pCornerAgrees`); the old raw derive
   is kept once (`pRawDerive`) to show it answering the first shape where production refuses. The
   module budgets stand: the re-measured cold runs are recorded on each entry in `modules.json`.

**Consequences.** D37 item 4 is closed; the two incremental models are faithful to production on a
retyped derived column read by a type-sensitive step, and on a boxed one. `Incremental.prime` and
`Incremental.refresh` read a derived column exactly as `DataFrame.evalPipelineWithInEnv` carries it,
typed or boxed. The pipeline differential no longer excuses the D18 refusal. `proofs.json` gains the
premise row `pipeline-temporal-cells-canonical` and the rows over the derive name the packing.

## 2026-10-15 — D37: the incremental refresh is modelled and proved past the row-local class — the maintained `GroupBy`, `Project`, `Limit`, `Window` and the filtering joins — in a module of its own over `Incremental.fst`; a derived column the cells retype is read as the reference packs it and reuses nothing downstream (a production defect, fixed); `Sort`, the resumed prefix fold, the fast paths, the guards, the chunked path and the codec stay declared out

**Context.** D31 proved the refresh for `Filter` and `Derive` and declared the other classes out, each
with its reason; Phase 433's deferral carried them to Phase 464. Each class keeps state a delta must
update rather than recompute — the group table and its aggregate cells, a join's key index, a window's
column — and each is where a silent divergence from the full evaluation is most likely. The shard asked
for a scope note before modelling, the model, the agreement and state theorems per covered class
composed with 433's walk, a differential with a go-red per class, any production divergence fixed
under the operator's 2026-10-12 ruling, and the classes still out named.

**Decision.**

1. **The classes covered, and each aggregate's maintenance rule.** The walk production runs on the
   general refresh path (`runIncremental`) is modelled for `PropagateRows` (`Project` beside the two
   row-local verbs), `MaintainGroups`, `TruncateOrder`, `RecomputeFrame` and `FilterByRelation`:
   - the maintained `GroupBy` — the groups of the rows alive at the step in first-appearance order of
     the key cells, the partition `evalGroupBy` builds; a group's aggregates REUSED exactly when every
     member is stable and the group's member tokens are the prior group's in order (`groupStable`,
     `sameMembers`), and RECOMPUTED over its members in member order otherwise by the reference's own
     aggregate function. **Every aggregate is maintained by recompute on this path** — `Sum`, `Mean`,
     `Min`, `Max`, `Count`, `Median`, `StdDev`, `First`, `Last` and `CountDistinct` alike — because a
     touched group is recomputed whole rather than updated; the invertible `Count` and int `Sum` of
     the in-place and append fast path (`groupStepInPlace`, `maintained`) are that path's, which is
     declared out with it. A group with no member vanishes; the group table walks on under one token
     per key, a group reusable downstream exactly when its aggregates were (`Stable[g] = existed &&
     not RecomputedAt[g]`);
   - `Project` — a column permutation and renaming that touches no row and caches nothing, refused on
     an unknown source and then on a repeated output (D35);
   - `Limit` — the positional slice (`max 0 offset`, `max 0 n`, clamped) over the frame the walk holds,
     which is the reference's frame; a row re-entering the slice has no cached cells past where it
     left and is evaluated fresh — the model's cache misses it;
   - `Window` — the column the reference computes over the rows alive at the step in their order,
     appended, with NOTHING reused after it (`Array.fill r.Stable … false`); the resumed prefix fold
     of Phase 333, which keeps the stable prefix of each partition, reuses MORE than the model and is
     declared out (its output is the same column; what is unproved is that its reuse is sound);
   - the filtering joins (`Semi`, `Anti`) — a verdict per left row, kept iff some right key matches or
     none does, cached as a filter's cells are and reused for a stable row exactly when the joined
     relation's key index is the prior one (`relationMoved`); the relation is resolved afresh on every
     refresh.
   **Declared out, with its reason:** `Sort` (`MergeOrder`) — its refresh merges the named rows into
   the prior PRODUCED order by binary insertion, valid while the stable rows' arrival order stands
   (`arrivedAsBefore`); proving that equal to the full stable sort needs the uniqueness of a sorted
   permutation under the comparator read as a strict total order with the arrival position as the
   tie-break, and a comparator premise (`compareResolved` is a total preorder within a typed column;
   across cell types `compareCells` answers `None`, read as `0`, which is not transitive) — a proof of
   its own size, which this phase did not start rather than ship half of; the operator decides whether
   it gets a successor.
2. **A module of its own, `proofs/IncrementalMaintained.fst`, opening `Pipeline`, `Incremental` and
   `Relational`.** On proof cost, per the shard: `Incremental.fst` checks in 101s and its two
   theorems would have been re-priced by every arm added to its walk, where a sibling keeps each
   module's cold check its own (Phase 458's lesson). The row-local arms are `Incremental.fst`'s
   definitions by name (`step_cells` restated over any cell function as `step_cells_by`, so a join's
   verdicts are the same step; `keep_true`, `extend`, `ext_schema`); the reference is `Pipeline.fst`'s
   driver over `Relational.fst`'s `other_of`, narrowed to this module's row functions (`mrows`), so the
   schema and every refusal are the relational model's and only the rows are new. Four parameters,
   each an assumption and each instantiated at production's own by the differential: the aggregate
   function (`Column.aggregate`, which the streamed accumulators equal by law), the join key match
   (`joinKeysMatch`), the window column (`windowStep`) and the group token. Three theorems:
   `mprime_is_full`, `mrefresh_is_prime` (output, every entry, the group table and the tail),
   `mrefresh_agrees`. The delta premise is 433's read one row at a time (`wk`): a reusable row the
   prior source held under its token has the cells it had, and one it did NOT hold is admitted — the
   cache misses it, as production reads it unstable — so the premise is strictly weaker than
   `Incremental.unchanged`, and it is what a `Limit` needs (a row re-entering the slice). Two premises
   are added: the prior source's tokens are distinct (production's witness refuses a duplicate
   identity, `RowIdentityUnusable`) and the group token is one per key (production mints it so). The
   theorems are stated for a new source of the prior's SCHEMA, types included — a source whose schema
   moved is `SourceSchemaMoved` in production and evaluates in full; a DERIVED column's retype is
   inside the walk and is item 3.
3. **The defect, and the fix.** A derived column typed by its cells (`ByCells`: an int arm beside a
   float arm in a `Case`, a `Coalesce` of an int column and a float literal) is a function of the
   WHOLE column, so one named row can retype it from int to float while every other row's cell stands.
   The reference packs the column under its decided type (`Vec.packAt`: an int under a float column is
   the float of its value, under a decimal column the decimal of its value, Core `1.0.0`); the walk held
   the step's cells RAW. The probe (`mRetypeShapes` in `ProofOracleTests`) showed the refresh — and the
   PRIME — answering where the full evaluation refused: a `Mod` over the retyped column read `Int 1`
   where the reference read `Float 1.0`; a reused group's `Sum` stood at `Int 3` beside a fresh
   `Float 3.0`; a recomputed `Min` answered the raw int. Production's own contract ("The result equals
   a full `DataFrame.evalPipelineWithInEnv` over `source` — always, for every delta") was false on
   these, and the operator's ruling (2026-10-12) is that it is fixed, not documented. Three changes in
   `Incremental.fs`, all internal:
   - the FRAME's column after a derive is the step's cells widened to the decided type (`widenCells`);
     the CACHE keeps the cell as evaluated, because a later refresh may type the column back and the
     column's type is decided over the raw cells on every refresh, as the reference decides it;
   - a derive whose decided type is not the prior evaluation's (`retypedSince`: the prior type re-read
     off the prior step's reached cells for a `ByCells` typing; a decided type is the same on every
     refresh) clears stability for every row, in place as a window does, and ends the in-place
     reading — every cell cached downstream and every reused group was computed over the other reading
     of the column;
   - the in-place group path (`groupStepInPlace`) refuses a frame whose unlisted slots are not all
     stable, which is its own invariant made a precondition.
   The model carries the rule (`entry.e_type`, the switch to `no_reuse`), so `mrefresh_is_prime` is
   stated over a retype rather than around it. A `behaviour` change on the open `0.41.0` draft
   (`STABILITY.md`): a consumer whose pipeline retyped a derived column between refreshes read a result
   the full evaluation would not have given, and now reads the full evaluation's.
4. **A gap in the REFERENCE model, named and not closed here.** `Pipeline.fst`'s `eval_derive` holds
   the derived cells raw (`zip_append f.rows cells`) where production packs them, so the pipeline
   differential and Phase 433's `prime_is_full` were stated against a reference that differs from
   production on exactly the corner of item 3 — the pools drawn never reached it, and the comparison
   through `ColumnCodec.encode` packs the output, which hides a raw cell unless a type-sensitive step
   reads it first. The correct closure is a widening in `eval_derive` (and in `Incremental.fst`'s
   `extend`), which re-prices every theorem over the derive in three modules; `Pipeline.fst` is outside
   this phase's files, and the work is a phase of its own, named in Phase 464's outcome as a successor
   need. Until it lands the two incremental models are faithful to production everywhere but a
   retyped derived column read by a type-sensitive step, which the differential's generators do not
   draw and the regression case pins on production directly.
5. **Still declared out, each with its reason (the rest of D31 item 3):** the staleness guards
   (`PipelineChanged`, `EnvChanged`, `SourceSchemaMoved`, `DeltaIsFullRefresh`, `OrdinalAddressing`,
   `RowIdentityUnusable`) send a refresh to a full evaluation, which `Pipeline.fst` already models — what
   is unmodelled is the decision, a comparison of the state's keys with the inputs; the quiet-delta
   shortcut (`ReusedPrior`) returns the prior output under `Delta.isQuiet` and an unmoved source, a
   claim about `Delta.isQuiet`, which is `Delta.fs`'s; the in-place and append fast paths (Phases 323,
   359) compute the same caches from the prior arrays, with their invertible `Count` and int `Sum` and
   the `patchedColumn` output; the chunked path (Phase 268, `refreshPrepared`) is a second evaluator
   over a `Derive`-only pipeline; the state's wire form (`IncrementalCodec`) is a codec.

**Consequences.** `Transform.Project`, `Transform.GroupBy`, `Transform.Limit`, `Transform.Window` and
`Transform.Join` join `Incremental.prime` and `Incremental.refresh` on the ladder's proved rows for the
incremental seam, and `DataFrame.aggregateCells`, `DataFrame.joinKeysMatch` and `DataFrame.windowStep`
leave the census for the differential's tested row, as the parameters it instantiates. The
`Incremental` census entry's reason names what the two models now cover and what remains. The proof leg
gains its seventh module, costed in `modules.json`.

## 2026-10-15 — D36: the static walk types a `Union` as the evaluator does, through the evaluator's own per-column function; a pair the evaluator refuses reads as a column of undecided type, an undeclared right decides no type, and `walk_sound` holds for every verb

**Context.** D32 recorded finding (b): `SchemaWalk.ofTransform`'s `Union` arm answered its input, so
the walk kept a union column's LEFT type where the evaluator, since Phase 423 (D18), widens the column
to the join of the two sides' types — an `int` beside a `float` is a `float` column. A planner, or the
pipeline-query registry's agreement check, reading the walk read a type the evaluator does not produce;
`walk_sound` carried `Union` as its one exception and `union_walk_type_drifts` stated the drift. The
operator ruled (2026-10-15) that it is fixed, not documented.

**Decision.**

1. **One function.** The evaluator's per-column rule is lifted out of `evalUnion` into
   `DataFrame.unionField` (internal): the left field under the one unit the pair states (D29) and the
   join of the two types (`joinColumnType`, D18), or the refusal — the `UnitMismatch` first, then
   `typesBesideEachOther`. `evalUnion` folds it over the zipped schemas exactly as before, and the
   walk's `Union` arm reads the same function, so the two cannot drift again by one being edited.
2. **The walk's arm reads the right as a join does** (`ofSource`), and answers in three cases:
   - both operands closed, with the same names: the left's columns zipped by position with the right's
     (as the evaluator zips them), each typed through `unionField` — so the column's type, unit and
     metadata are the evaluator's;
   - an open left against a closed right: each visible column meets the right's first column of its
     name, which is the column it is zipped with whenever the evaluator answers;
   - an undeclared right (`Ref` with no declared schema): every name stays — any answer has the left's
     names — and no type is decided, because the right's type may widen any of them (`int` widens to
     `float` or `decimal`, a coarse timestamp to a finer unit). Keeping the types that no widening can
     move would need the walk to enumerate Core's widening relation, which a new column type would
     silently outgrow; an undeclared source already costs the walk certainty elsewhere (a join's right
     columns), and the registry refuses an undeclared source before it reads the walk.
3. **A refused pair is a column of undecided type, not a refusal.** The shard asked that "a pair the
   evaluator refuses, the walk refuses with the same typed error"; that premise does not hold.
   `SchemaKnowledge` has no refusal channel (D26, D28 item 5, D35 item 5) — the walk answers a knowledge
   for every pipeline, including the ones the evaluator refuses — and inventing one for a single arm
   would be a second, partial refusal surface beside the evaluator's. So: two closed operands whose
   names disagree answer the input, unchanged (the evaluator's `JoinError`); a column pair no type holds
   together (an `int` beside a `bool`) or whose units disagree answers the column with no decided type,
   as the unpivot's refused value column already does (Phase 338). The typed refusal lives where it
   always has, in the evaluator (`unionField`'s error, through `evalUnion`); a registered pipeline query
   meets the undecided column as `TypeUndecidable`, whose doc now names this case.
4. **The unit under an undeclared right is the left's.** `ColumnKnowledge.Unit` has no "unknown" (a unit
   is a fact of the schema); where the left states none and the right is undeclared, the evaluator may
   adopt the right's unit and the walk cannot say which. The arm keeps the left's, as the old arm did for
   every case. This is outside the proof (the model is unitless) and unreachable from the registry
   (every source declared); a closed right decides the unit exactly, through `unionField`.
5. **The model.** `proofs/Relational.fst` lifts `union_ty` out of `union_types` (the shared function's
   type half), models the arm's three cases (`union_known_pos`, `union_known_by_name`, `untyped`), and
   proves `union_sound`; `walk_sound` now holds for all fourteen arms with no `Union` exception.
   `union_walk_type_drifts` is retired with the arm it described, and its witness is restated as
   agreement (`union_walk_widens`: over `int` beside `float` both answer `float`); `union_sound_names`,
   the names-only half, is subsumed and retired. Re-introducing the old arm (the input, unchanged) leaves
   `union_sound` unprovable, and so `walk_sound` — measured, one perturbation run. The differential now
   also holds the shipped walk to the model's `sound` on every step production answers, and a go-red shows
   the old arm's knowledge is not sound on the widening shape.

**Consequences.** A behaviour change to a public walk, of D21's, D26's and D28's class: no public
signature moves (`unionField` is internal) and `api/` is unchanged. It rides the open `0.41.0` draft
(`STABILITY.md`). A consumer that read the left type off a walk over a widening union now reads the
joined type; one over a refused pair or an undeclared right reads an undecided type. A registered
pipeline query that declared the left type of a widening union column is now refused at registration
with `TypeDiffers` (declare the joined type), and one over a refused pair with `TypeUndecidable` (it was
refused by the evaluator on every frame already). The caller survey (Phase 469's record) found no
in-workspace consumer whose behaviour moves at its current pin.

## 2026-10-15 — D35: a `Project`, a `GroupBy`, an `Unpivot` and a `Pivot` refuse an output that names a column twice, with a `TypeError` naming the verb, the name and both producers, through one check; the pivot refuses too, rather than suffixing, although its collision is the data's

**Context.** D32 recorded finding (a): four verbs answer `Ok` with two columns of one name —
`Project [a, x; a, x]`, a `GroupBy` key named as an aggregate, an `Unpivot` id column named `variable`, and a
`Pivot` whose index column is named as an `on` value's text — and stated each as a theorem with a witness
that the differential asserted on production. Such a table re-enters the evaluator by name as the first of
its two columns (D25) and Core's `Table.validate` refuses it. D20 closed the join's way of producing one
(a suffix) and D28 the window's (a refusal). The operator ruled (2026-10-12, applied 2026-10-15) that the
four are fixed in production, not documented, and that the pivot's choice is made on correctness.

**Decision.**

1. **One check.** `DataFrame.repeatedOutput` takes each output column's name beside what produced it,
   in output order, and refuses the first name an earlier output took:
   `TypeError("<verb> output 'x' is named twice, by <first> and by <second>; give each output a name of
   its own")`, where a producer reads `the projection of 'a'`, `the key 'k'`, `the aggregate over 'v'`,
   `the id column 'n'`, `the unpivot's variable column`, `the index column 'i'` or `the column for the
   on value 't'`. The four verbs ask it through `projectRefusal`, `groupByRefusal`, `unpivotRefusal`
   and `pivotRefusal`. It covers every repeat a verb can make, not only the four witnesses: two pairs of
   one output, a key twice, two aggregates of one name, an id column twice or named `value`, an index
   column twice, and two value columns whose `on` values share a canonical text.
   **Except where the two value columns are one column.** The pivot's on-values are `List.distinct`
   over the cells, and two `NaN`s are distinct there while `cellEq` (Phase 325) equates them: the pivot
   answered two columns named `"NaN"`, each collecting every `NaN` row — identical in name and content,
   pinned by Phase 264's oracle. Refusing a float `on` column for holding two `NaN` rows would make
   ordinary data fail; the two are the one column they name, so the pivot keeps one on-value of each
   text among the cells `cellEq` equates, and refuses only a text shared by cells `cellEq` keeps apart.
   No information is lost (each of the pair collected the same rows), and the model already read the two
   `NaN`s as one cell (its floats are their round-trip text).
2. **Where it sits.** After every name the arm resolves (an unknown name is still named first, as in
   D28), and before any row is read: `Project`, `GroupBy` and `Unpivot` refuse over a source with no
   rows exactly as over one with rows, and the unpivot refuses before its value column is typed. The
   pivot's value names are its distinct present `on` values, so its check reads the `on` column and runs
   after the on values are collected and before any value is aggregated: an aggregate refusal over a
   colliding pivot is never reached.
3. **No new `EvalError` case.** `TypeError` is what D28 answers for the window's collision; a new case
   would break every exhaustive match over `EvalError` and the model's error type for a refusal a host
   reports by its text (D25, D28).
4. **Every evaluator path asks it.** The reference arms, the incremental seam's own `Project` and
   maintained `GroupBy` walk (`Incremental.fs`, which would otherwise have answered the table the
   reference refuses), and, through the reference, the pivot and the unpivot. The planner's totality
   verdict counts a repeating `Project` as not total and stops tracking the schema at one, so no rewrite
   moves a step across it.
5. **The schema walk still names the column twice.** The shard asked that "the static walk refuses
   the same pipelines"; that premise does not hold. `SchemaWalk.ofTransform` carries no refusal verdict
   (D26, D28 item 5): it answers a knowledge for every pipeline, including ones the evaluator refuses for
   an unknown key or a window's taken name. Over a refused `Project` it answers the closed knowledge with
   the name twice, which is about a pipeline the evaluator refuses and is marked by the repeat; the
   walk's soundness theorem is stated over steps that answer `Ok` and is untouched. The planner, which
   does decide, decides as the evaluator does (item 4).
6. **The model.** `proofs/Relational.fst` models the check clause for clause (`repeated_output`, the
   producer lists, the refusal placed where each arm places it), and proves `project_no_dups`,
   `group_by_no_dups`, `unpivot_no_dups` and `pivot_no_dups` with no hypothesis on the input, beside
   `window_no_dups` and `join_no_dups`: none of the six verbs that name their output answers `Ok` with a
   name twice. The four `*_repeats_name` witnesses become `*_repeat_refused`, the same inputs answering
   the refusal; the differential compares production's refusal text to the model's on the four shapes
   and over the generated sample. Removing the check from the group-by arm leaves `group_by_no_dups`
   unprovable (one perturbation run).

**Why the pivot refuses rather than suffixing.** D32 left the pivot open because its colliding name is
the data's, like a join operand's, and D20 suffixes a join to keep it total when an operand gains a
column. The pivot is not the join's case. A join's right column keeps its identity under a suffix —
`x_right` is still "the right's `x`", every right name is suffixed by one rule, and `joinRightNames`
lets a caller compute it. A pivot's value column has no identity BUT its name: the column for `on`
value `eng` is found by reading `eng`, and every consumer of a pivot — a later step, a host rendering
the table, a chart keyed on the on values — reads it that way. A suffix would rename exactly the one
column whose name is its meaning, silently, and only when the data happens to hold the index's name: a
later `Col "eng"` would read the index where it meant the value column (or the reverse, under the other
suffix), a plausible wrong answer with no error anywhere — the outcome D28 rejected for the window.
Suffixing the INDEX instead would break every read of the column the author named. A refusal is honest:
the pipeline names a column that the data also names, so the reads of that name are ambiguous, and the
author resolves it by renaming the index (a `Project` ahead of the pivot) or filtering the value. The
cost is that the pivot becomes partial in its data — a pipeline that ran over a table refuses over one
that gains such an `on` value. That is not new: the pivot already refuses by its cells through an
aggregate, and the unpivot by its melted cells (Phase 338); and the static walk already declares the
pivot's value names unknown, so no planner or registry relied on knowing them.

**Consequences.** A behaviour change to a public evaluator, of D21's, D26's and D28's class: no public
signature moves (the check and its four callers are internal), and `api/` is unchanged. `0.40.0` is
tagged, so the change ADVANCES the slot to `0.41.0`, a new draft (`STABILITY.md`). Four refusal vectors
join `conformance/laws/transform-laws.json` after the unit shapes (`repeat-*-refused`, over the decimal
table); every earlier vector is byte-identical, the file's `kitVersion` stamp follows `<Version>`, and the
corpus copy follows the release rule (D6 (4)) and is not re-stamped by a draft. A consumer whose pipeline
named an output twice renames one of the two columns.

## 2026-10-15 — D34: the dense frame and its kernels are modelled in their own module over `Pipeline.fst`'s frame and Core's column; the read-back is the table's rows and the filter kernels are the row-major filter, as theorems; the native member, the morsel runner, the sort kernels and the rope are declared out

**Context.** Phase 423 made the evaluator's frame a view over Core's typed column vectors, and Phases 280,
376, 420, 422 and 440 gave it a decimal carrier, byte masks, a null-free column with no mask, integer
temporal storage and a decimal view. `Frame.fs` and `Kernels.fs` had no model: every theorem of
`Pipeline.fst` is stated over the ROW-MAJOR frame, and nothing said the dense frame the evaluator
actually runs over is that frame. Core proved the matching fact for its own typed column (Phase 419,
`ColumnRefinement.fst`); Phase 435 asked for the compute side's.

**Decision.**

1. **A module of its own, `proofs/Frame.fst`, opening `Pipeline`.** D31 and D32 are the precedent. It
   reuses `Pipeline`'s `cell`, `frame`, `wframe`, `eval_expr`, `filter_rows`, `eval_step`, `index_of`
   and `assoc` by name, and states its theorems against them: `logical` is the dense frame as the
   `wframe` `Pipeline.fst` evaluates. It restates `List.tryItem` (`vnth`) because `Pipeline`'s is
   `noextract`.
2. **Core's column is restated to the minimum, and named.** `validity` (`AllValid | Mask`), the seven
   `ColumnData` cases and `Column.cell` (`core_cell`) are written at `Pipeline.cell`; `core_cell` is the
   Fuaran.Core repository's `ColumnRefinement.cell_at` with its `to_mask` unfolded, and that module's
   `cells_of_nth` is what makes it the i-th of `Column.toCells`. Fuaran.Core is read, not edited; the
   correspondence is stated in the module header and on the ladder row.
3. **The theorems say what the code computes.** `of_table_reads_columns`: over a table whose columns
   hold their fields' types and the table's length, cell `(i, j)` of `Frame.ofTable` IS
   `Column.cell i` of column `j`, at every column type, with a mask or `AllValid` — the abstraction
   theorem. `rows_of_reads`: the read-back is total and is the vectors through the selection.
   `pack_reads` / `of_rows_exact`: the row door carries each cell as its type does, and rows carried
   exactly come back as they went in. `to_table_reads`: `Frame.toTable` writes exactly the logical rows.
   `filter_kernel_is_step`: where `filterBits` answers, the kernel branch of the filter IS
   `Pipeline.eval_step`'s `Filter` over the read-back, never an error.
4. **Every parameter is an assumption, listed.** The host record (the temporal texts and readers, an
   int's float carrier and decimal text, `scaleOf` / `tryScaled`, `compareFloat`) is assumed NOTHING by
   the read-back theorems — `Column.cell` and `Vec.cellAt` call the same functions. The kernel theorem
   rests on `kernel_prims` (the comparison and Kleene primitives at exactly the cells a leaf meets,
   ladder row `frame-kernel-primitives`) and, for the decimal leaf, on `decs_ok`, which the boundary
   builds under one premise of `scaleOf` (`scale_admits`). Two representations are taken as given and
   named on `frame-host-abstract`: the decimal carrier's float64s are exact integers (`Width`), and a
   bitmap packs 32 rows to a word.
5. **The acceptance's two perturbations, measured, one per prover run.** Reading a present bit as absent
   in `Vec.cellAt`'s int arm fails `view_reads`; building `AllValid` as an all-absent mask (dropping the
   null-free guard) fails `mask_of_reads`. A third, comparing a left-hand constant without `flip`, fails
   `bits_eval`.

**Declared out, with the reason.**

- **The native kernel member** (`Kernels.Native`: 128-bit vectors, the thread pool). Its contract is the
  portable member's answer; the suite runs the transform law vectors through both on one host, and the
  differential runs `filterBits` through both against the model.
- **The compiled filter path and the morsel runner** (`compileExpr`, `filterMorsel`, `keptInOrder`,
  `RunMorsels`). It is the reference evaluator's per-row answer, the very evaluator `Pipeline.fst`
  models; a model of the compiled tree would be a second model of `evalExpr`. The theorem covers the
  kernel branch `filterBits` takes, which is the part of the filter with no other model.
- **The keyed verbs' kernels** (`SortFinite`, `SortPositions`, `Partitions`, `RunPipelines`). They are
  scheduling and sorting over verbs `Pipeline.fst` takes as a parameter (D32 narrowed it to the rows);
  their contract — every correct sort answers one array — is stated on the record and held by the suite.
- **`gatherOrNull`, `Vec.append` / `slice` / `concat` / `setAt` / `sameCells`, `ofTemporalTexts`,
  `Frame.project` / `withColumn` / `appendColumn` / `concat`, the `Shared` allocator.** The verbs that
  use them are parameters of `Pipeline.fst`; none is on the read-back's path or the filter's.
- **The rope** (`Chunked`, Phase 268), whose edit `ColumnOps.fst` models, and the two boxed arms of
  `Vec.toColumn` (a non-string string-family vector and `Cells`), which build through Core's
  `Column.ofCells`, modelled in the Fuaran.Core repository (`to_cells_of_cells`).

**No defect was found.** Two behaviours the model makes explicit and the theorems state rather than
change: a column of another type or length than its schema field is read through `Vec.pack` of its cells
padded or cut to the table's length (so an int column under a float field reads as floats); and a mask
of another length than its column's values is read padded absent or cut, as Core's `Column.cell` reads
it. Both are what the per-index reads always answered (Phase 423); `Table.validate` refuses both tables.

**Consequences.** `proofs.json` gains three proved rows (`frame-read-back`, `frame-table-out`,
`frame-kernel-agrees`), a tested row (`frame-differential`) and two assumed rows (`frame-host-abstract`,
`frame-kernel-primitives`). The operation census loses `DataFrame.prepare`, `DataFrame.prepareRows` and
`DataFrame.toTable`, now named by the read-back and table-out rows. `modules.json` budgets the module
(120s, floor 26s); `check.ps1` and the oracle project register it. No production code changed.

## 2026-10-15 — D33: a declined shape's tick is held to what a declined refresh is — the diff and the reference evaluation it runs, timed beside the tick, plus the per-call floor; and a refresh no longer walks an embedded relation to learn that its pipeline did not move

**Context.** Compute `v0.39.0` (`02d25fd`) failed its publish run (38554709427, attempt 1) in the
Release clock leg: `append pivot @ 100000` read tick 10.07 to 10.34 ms against a one-thread full
evaluation of 5.53 to 5.76 ms (x1.79 to x1.82, bound x1.6) on all three counted attempts. The run's second attempt
read the same cell at x1.19 and x1.29, passed and published `0.39.0` to nuget.org, so the release
stands; the gate had flapped, not caught a regression. The same cell had been red on `ci` at `fc0ef74` and `3142469`, and read x1.08 on the same
commit's `ci` run; `ci` attempt 1 at `02d25fd` was red on `append inner join` instead. Both cells that
flap are the two shapes the seam DECLINES (`ScalingTests.refreshDeclinedShapes`, D27): a refresh of
either is `Incremental.runReference`, the full evaluation through `DataFrame.evalPreparedCounted` on the
default kernel set, while the tick bound's denominator is the full evaluation on `Kernels.oneThread`
(Phase 371). D27 already leaves those two shapes out of the refresh-against-full case for that reason;
the edit and append tick cases still held them to `1.6 x full`, and on x64, which has no recorded
baseline table (D19), to that arm alone. The operator chose (2026-10-14): first measure the declined
path and remove any avoidable cost, then rule how a declined shape's tick is judged.

**Measured first (Phase 459, before any change).** Release, `dotnet <dll>`, Arm64 (12 logical
processors, calibration baseline 4.91 ms), the corpus nodes and deltas exactly as the clock leg builds
them, each figure the best of five batched samples, two probe runs; the probe split the tick into the
diff, the boundary (`DataFrame.prepare`) and the reference evaluation, and timed the evaluation on the
default set beside `Kernels.oneThread`. At 100,000 rows, in ms:

| Cell | tick | diff | refresh | prepare | eval, default set | eval, `oneThread` | full, `oneThread` | pipeline `=` |
|---|---|---|---|---|---|---|---|---|
| `inner join` edit | 6.7–9.1 | 1.31–1.39 | 6.66–6.79 | 0.12 | 4.7–5.5 | 5.6 | 5.6–5.8 | 1.19–1.22 |
| `inner join` append | 7.4–8.6 | 0.60–0.91 | 6.2–7.1 | 0.14 | 5.0–5.8 | 5.6 | 5.6 | 1.19–1.23 |
| `pivot` edit | 13.9–16.0 | 1.75–2.08 | 13.9–15.7 | 0.25 | 13.3–16.7 | 13.2–13.8 | 13.9–16.3 | 0 |
| `pivot` append | 15.1–17.1 | 1.09–1.22 | 14.4–15.1 | 0.21 | 13.7–14.5 | 13.2 | 13.5–15.4 | 0 |

Timed against each other in one interleaved window (the tick, and the diff followed by the reference
evaluation over a fresh source object, as the tick receives it), the tick's excess over "diff plus
reference evaluation" read 0.95 to 1.42 ms on the join at 100,000 rows (once 0.11), 0.23 to 0.28 ms at
20,000 and 0.015 ms at 1,000; on the pivot it read -1.48 to +0.38 ms at 100,000 and within 0.2 ms at the
other sizes, which is the reading's noise. The refresh allocates what the full evaluation allocates to
within 0.01 MB a call (join 7.58 against 7.57 MB, pivot 9.35 against 9.35 MB at 100,000 rows). The diff
allocates 0.19 MB a call for the edit and 1.63 MB for the append.

So the declined tick costs one diff, one reference evaluation and a preamble, and the three suspected
costs read as follows:

1. **A second boundary walk: none.** `runReference` prepares the source once (or takes the state's
   prepared form) and evaluates over it; the boundary is 0.12 to 0.25 ms at 100,000 rows either way.
2. **A kernel set that loses on few cores: none, for these shapes, by construction.** `evalPivot` and
   `evalJoin` take no kernel set, so the default set and `oneThread` run the same code for both; the
   measured evaluations agree within their noise. The x64 publish runner's x1.8 is therefore not the
   kernel set.
3. **Work the full evaluation does not do: found, on the join.** The refresh's staleness test asked
   whether the pipeline moved with structural equality (`pipelineMoved`, Phase 355), not through
   `samePipeline`, the identity-first test Phase 333 wrote for exactly this, which had been left
   unreferenced. The join EMBEDS its right relation in the pipeline, so every refresh walked 100,000
   rows of it cell by cell to learn that a pipeline held across ticks had not moved: 1.2 ms at 100,000
   rows, a fifth of the full evaluation, and 0.25 ms at 20,000. That is the join's whole measured
   excess.

The tick is not "one full evaluation plus a one-row diff" for an edit, and is not meant to be: an edit's
diff keys every row of both tables (Phase 272's floor of any diff by identity), 1.3 to 2.1 ms at 100,000
rows. That is the diff's cost for every shape, accepted or declined, and it is no part of the declined
path.

**x64, from the `ci` and publish logs** (no x64 machine here; `ci` runs on a push to `main` and on pull
requests only, so a branch push takes no x64 reading, and the split above was not repeated there). At
100,000 rows: publish attempt 1 (calibration 3.74 ms), `pivot` edit tick 7.00 ms against full 8.13 ms
(x0.86) and append 10.07 to 10.34 against 5.53 to 5.76 (x1.79 to x1.82); `inner join` edit x1.46, append
x1.19 to x1.39. Publish attempt 2 (calibration 4.71 ms): `pivot` x1.19 to x1.29, `inner join` x1.18 to
x1.38. `ci` at `02d25fd` (calibration 4.56 ms): `pivot` x1.08 to x1.24, `inner join` x1.28 to x1.63. The
pivot's full evaluation read 8.13 ms in the edit loop and 5.53 to 5.76 ms in the append loop of one leg,
over sources one row apart: the denominator itself moves by 1.4 times between two windows on that
runner, which a tick and a denominator timed in separate windows cannot cancel.

**Decision 1 — the avoidable cost goes (459.B).** `pipelineMoved` reads the in-process case through
`samePipeline`: identity first, structural equality only when the objects differ. A decoded state still
compares canonical hashes. The answer is the one `=` gives except for a pipeline object holding a NaN
literal, which now reads as unchanged against itself instead of moved; both readings refresh to the same
result, and the declined footprint and its reason are unchanged (the main suite's pin and the law
families are green). After it, the join's excess over "diff plus reference evaluation" read within 0.13
ms either way at every size. Nothing else in the declined path was avoidable: the boundary is a view
since Phase 423 (a second `prepare` injected into `runReference` moved no clock-leg cell), and the
kernel set is not read by either shape.

**Decision 2 — how a declined shape's tick is judged (459.C; operator ruling 2026-10-14).** A shape
in `refreshDeclinedShapes` has its edit and append ticks held to what a declined refresh is:
`tick <= diff + reference evaluation + floor`. The reference evaluation is `DataFrame.evalPreparedCounted`,
the kernel set the declined refresh itself runs on, over a fresh table object as the tick receives one,
and the diff is the same `Delta.diff`; both are timed beside the tick in one interleaved window
(`ScalingTests.pairedBatchedMs`: one call count for both, the order alternating, the best of 25 rounds
of samples of at least 2 ms each), so a load or a collection that reaches one reaches the other. That is
Phase 371's like-with-like rule applied to what a declined tick actually replaces. A declined tick cannot
be cheaper than recomputing, so `1.6 x one-thread full` was never its claim; what it can be is dearer
than the fall-back it reports, and that excess is the refresh's preamble, which the per-call floor
bounds. Every other corpus shape keeps D19's `max(1.6 x full, 1.5 x baseline) + floor`. `tickBound`,
`tickBaselineFactor` and `tickFloorFactor` did not move, no x64 baseline table was recorded, and
`refreshDeclinedShapes` stays the one list the edit tick case, the append tick case and D27's refresh
case read, still pinned against `Incremental.plan` by the main suite. The Arm64 table's rows for the two
declined shapes are now unread and are kept: the table is the recording script's verbatim output, and a
shape that stops being declined returns to the baseline arm with them.

Why 25 short samples rather than `batchedMs`'s five of 20 ms: at 100,000 rows a pivot call is about
15 ms, so a 20 ms sample is two calls, and the best of five of each arm still moved by up to 3% between
runs, three times the floor. One call a sample and the best of 25 read within the floor on the clock leg.

**Held.** On the fixed tree, three Arm64 clock legs (Release, `dotnet <dll>`) were green, 17 of 17 cases.
On the two at calibration 6.20 ms every declined cell read x0.93 to x1.04 of its diff and reference
evaluation; on the third, at 7.67 ms (the machine was loaded by other work), the `pivot` edit cells read
x1.09 to x1.17 on two attempts and x1.00 on the third, green under D22's same-cell rule. **Go-red:**
with the structural pipeline comparison put back, the `inner join` cells at 20,000 and 100,000 rows read
x1.24 to x1.33 and both tick cases were red on all three counted attempts, the same cells each time, while
the accepted shapes' cells kept their verdicts; under the old condition those cells had passed at 1.6
times the full evaluation. A redundant boundary walk, the perturbation the phase proposed, does not
redden it, because it costs nothing measurable since Phase 423, which is the finding rather than a gap in
the condition.

**Release record.** `0.39.0` was published (the publish run's second attempt, verified on nuget.org's
flat-container index), so its `STABILITY.md` heading stands as released and is not retitled; this
phase's entry rides the `0.40.0` draft. No public signature, answer or wire byte moves (class `none`,
`performance`).

## 2026-10-15 — D32: the relational verbs' output schema and refusals are modelled in their own module over `Pipeline.fst`'s frame, the twelve-verb parameter is narrowed to the rows, the static walk's soundness is a theorem; and two findings are recorded with their witnesses, not fixed — four verbs answer a repeated name, and the walk keeps a union's left type

**Context.** `proofs/Pipeline.fst` models the expression evaluator, `Filter`, `Derive`, the dispatch and
the counted driver, and takes the other twelve verbs as a parameter typed only to answer a well-formed
frame or a named error. Phase 411 (a join emitting two columns of one name) and Phase 412 (a sort key
naming no column dropped in silence) both sat in that assumption; a model of each verb's output schema
and refusals would have stated both, and Phase 434 asked for it, leaving open whether the model extends
`Pipeline.fst` or stands beside it.

**Decision.**

1. **A module of its own, `proofs/Relational.fst`, opening `Pipeline`.** `Pipeline.fst` verifies in
   about a minute cold and its budget is 530s; the relational model is sixteen hundred lines of
   definitions and lemmas and verifies in about a minute of its own. Folding it in would double the
   module every later phase re-verifies and put the two models' costs under one number. The new module
   reuses `Pipeline`'s types and helpers by name (`schema`, `frame`, the verbs, `eval_error`, `assoc`,
   `index_of`, `names`, `app`, `column_type_by`, `derived_typing_of`) and re-models nothing; it restates
   `List.tryItem` and `List.contains` only because `Pipeline`'s are `noextract`. `Incremental.fst` (D31)
   is the precedent for a model opening `Pipeline` rather than growing it.
2. **The narrowing is a construction, not an edit.** `Pipeline.fst`'s `other_fn` stays as it is;
   `Relational.other_of` builds one from `step_schema` — each verb's schema and schema-level refusals,
   clause for clause from `evalStepWith`'s arms — and a ROW FUNCTION, which answers the rows under the
   schema the model computed, as wide as it, or a refusal that read a cell (`AggError`). Every theorem
   of `Pipeline.fst` holds for `other_of` because it holds for every `other_fn`; what the ladder's
   twelve-verb row now assumes is the row function and nothing about a schema. The differential runs the
   pipeline comparison with `other_of` over production's rows as the parameter, so the narrowing is
   measured over four hundred generated pipelines, not only stated.
3. **Row content is read exactly where a schema reads it, and the header says where.** The pivot's
   distinct present `on` values name its columns; the unpivot's melted cells type `value` where the
   declared types leave it to them (Phase 338). Nothing else reads a cell. The parameters are a record of
   four host functions (the canonical cell text, an int's text, the string order, the join's suffix rule)
   and the resolver; each is listed with what the theorems assume of it, and only two assumptions are
   load-bearing: the suffix rule is injective in its index (`cand_injective`, a string fact the prover
   has no axiom for, on which `join_no_dups` rests and which the 411 go-red measures by crossing the
   pre-D20 rule), and the walk's declared sources are the resolver's schemas (`sources_agree`, the
   contract a caller of `SchemaWalk.ofPipelineFrom` with `ofMap` takes on).
4. **The walk's soundness is the agreement theorem, stated for open knowledge too.** `walk_sound`:
   over knowledge sound for the input's schema, when a verb answers `Ok`, `SchemaWalk.ofTransform`'s
   answer is sound for the output — a closed knowledge has the output's names in order and every type it
   knows is the output's; an open one names columns the output carries with the types it knows. The
   pivot's open answer is the index columns the evaluator's output carries first; a join over an
   undeclared `Ref` keeps the left columns and the right columns that keep their own names. The union's
   NAMES agree; its types are finding (b) below. A join's open-right case needs the left's names
   distinct: with a name repeated, the walk's second column of that name claims a type a by-name read
   never finds — which is finding (a) seen from the walk's side.
5. **Two findings, recorded with witnesses and not fixed here.** The acceptance allows either; these are
   filed, because each is a behaviour change to a public evaluator or walk of D21's, D26's and D28's
   class, which wants its own decision and `STABILITY.md` entry, and the files that carry the change are
   outside this phase's declared key files.
   - **(a) Four verbs answer `Ok` with a repeated output name.** `Project [a, x; a, x]` answers `x`
     twice; `GroupBy [k] [Sum v as k]` answers `k` twice; `Unpivot [variable] [v]` answers `variable`
     twice; a `Pivot` whose index column is named as an `on` value's canonical text answers that name
     twice. D20 closed this for a join (the suffix) and D28 for a window (the refusal); the four were
     not considered then. The model states each as a theorem (`project_repeats_name`, …) and the
     differential asserts each on the shipped evaluator, so a fix moves both. **The ruling this waits on**
     is D28's reasoning applied: in `Project`, `GroupBy` and `Unpivot` the repeated name is the author's
     own choice written into the pipeline, so D28's refusal — a `TypeError` naming the column, before any
     row is read — is the candidate; in `Pivot` the colliding name is the data's, like a join operand's,
     so D20's suffix or a refusal are both candidates and the choice is an operator's.
   - **(b) `SchemaWalk.ofTransform` keeps the left type for a `Union` where the evaluator widens it.**
     Since Phase 423 (D18) a union's column takes the join of the two sides' types — `Int` beside `Float`
     is a `Float` column — and the walk's `Union` arm is `input`. Over `Union` of an `Int` column with a
     `Float` one the walk says `Int` and the evaluator answers `Float`; a planner, or the registry's
     agreement check, reading the walk reads a type the evaluator does not produce. The model states it
     (`union_walk_type_drifts`), the walk comparison in the differential holds production's walk to the
     model's over the shape (`423-union-widens`), and the schema comparison records the evaluator's
     answer beside. The fix is the walk's `Union` arm reading the source as `Join` does and joining the
     types where both are known; it is one arm and one `schemaWalkLaws` law, and it is not this phase's.
6. **The census moves.** The twelve `Transform.<Verb>` entries D30 wrote leave
   `coverage-exclusions.json` for the ladder's rows, with `SchemaWalk.ofTransform`, `SchemaWalk.ofSource`
   and `DataFrame.joinRightNames`; the `SchemaWalk` entry's reason names what moved.

**Consequences.** No production source changed: the model found the two findings above and no
disagreement with the evaluator's schema over the generated sample or the named shapes. The proof leg
gains a module (`Relational`, budgeted in `modules.json` from this phase's runs) and the oracle project
its extraction. Re-introducing 411's defect in the model (a suffix free against the left names only)
leaves `join_no_dups` unprovable (`rename_right_no_dups`'s hypothesis that every right name is taken no longer holds) and the leg red — measured by one perturbation run; re-introducing 412's (an unknown sort key
dropped) makes the `Sort` arm answer `Ok` and `sort_unknown_refused` fails. A later phase that closes
finding (a) changes the four `*_repeats_name` theorems into refusal theorems and deletes the
`finding-*` shapes from the differential in the same commit; one that closes (b) removes
`union_walk_type_drifts` and extends `walk_sound` to the `Union` arm.

## 2026-10-15 — D31: the incremental refresh is modelled and proved for the row-local class — `Filter` and `Derive` — over `Pipeline.fst`'s frame; every other verb class is declared out, by name and with its reason, and the model's one premise is the delta's truthfulness

**Context.** `Incremental.fs` is about seven thousand lines and `Delta.fs` two thousand: a refresh walks
the pipeline step by step over a frame whose per-step cells it keeps (`StepCells`, aligned with the
source's row tokens), reads a cached cell for every row the delta does not name and evaluates the rest,
maintains a `GroupBy`'s groups, merges a `Sort`'s order, slices a `Limit`, resumes a `Window`'s run and
re-checks a filtering join's key index. Its agreement with full evaluation was sampled
(`Conformance.incrementalLaws`, the refresh-cost tests) and stated by no model (D30's census said so,
operation by operation). Phase 433 asked for the theorem, allowing a split by verb class provided the
split is recorded rather than implied.

**Decision.**

1. **The covered class is the pipeline every step of which is a `Filter` or a `Derive`** — the two verbs
   `Incremental.classifyStep` reads as `PropagateRows` AND `Pipeline.fst` models as a clause. The model
   (`proofs/Incremental.fst`) is the walk at the level production's comments state it: one cell cache per
   evaluating step keyed by the row's token, a step evaluated row by row in frame order with a reused row's
   cell read from the prior cache and every other row's evaluated by `Pipeline.eval_expr`, the first error
   winning; a `Filter` keeping the rows whose cell is `Bool true`; a `Derive` replacing or appending its
   column, typed over the rows alive at the step by `derived_typing_in` and `column_type_by` — the
   reference's own rule, re-run on every refresh, exactly as `walk`'s `WDerive` arm re-runs it
   (`Incremental.fs`, "Reading it off a cache would type the column differently from the reference the
   moment a filter downstream dropped the only typed row"). `prime` is the walk reusing nothing;
   `refresh` is the walk reusing every unnamed row against the primed caches. Three theorems:
   `prime_is_full` (priming is `Pipeline.eval_uncounted`, frame for frame and error for error),
   `refresh_is_prime` (the state theorem: output AND caches), `refresh_agrees` (the agreement theorem).
   The lemma that carries them is `eval_expr_names`: the evaluator reads a schema's names and never its
   types, so a cell cached under one column typing is the cell under another — which is what makes a
   per-row cache sound under a column typing that is a function of the whole column.
2. **The one premise beyond `Pipeline.fst`'s is the delta's truthfulness**, as the walk reads it:
   every row the delta does not name is a row of the prior source, under its token, with the cells it had
   (`unchanged`). It is the ladder's `incremental-delta-truthful` row, and it is production's own
   contract ("The delta must truthfully describe the change (`Delta.diff` produces exactly that)"). The
   theorems are stated for a new source that renames no column; a source whose schema moved is
   `SourceSchemaMoved` in production and evaluates in full, outside the walk.
3. **Declared out, each a follow-up of its own, each still a `measured-elsewhere` entry in the census:**
   - `Project` — row-local in production, but a parameter (`other_fn`) in `Pipeline.fst`; covering it
     needs a concrete `Project` clause there and a bridge row saying the parameter agrees with it;
   - the maintained `GroupBy` — the group table, the per-group member order and aggregate caches
     (`GroupMembers`, `GroupAggs`, `GroupOrder`, `TailCells`), and the rule that a touched group is
     recomputed over its members in order while an untouched one is reused; `GroupBy` is a parameter
     in `Pipeline.fst`, so the model needs the verb's own clause first;
   - top-N — `Sort` (`MergeOrder`: the arrival and produced orders, a merge of the named rows into the
     produced order, valid only while the unnamed rows' arrival order stands) and `Limit`
     (`TruncateOrder`: a slice over the maintained order); both parameters in `Pipeline.fst`;
   - `Window` (`RecomputeFrame`, the resumed prefix-fold runs of Phase 333) and the filtering joins
     (`FilterByRelation`, the cached key index of Phase 120);
   - the staleness guards (`PipelineChanged`, `EnvChanged`, `SourceSchemaMoved`, `DeltaIsFullRefresh`,
     `OrdinalAddressing`, `RowIdentityUnusable`), the quiet-delta shortcut (`ReusedPrior`), the in-place
     and append fast paths (Phases 323, 359 — the same caches reached from the prior arrays), the chunked
     path (Phase 268) and the state's wire form (`IncrementalCodec`).
   Row-local first because it is the class the other classes' walks run THROUGH — every admitted step
   after a `GroupBy` or a `Sort` is this same restricted walk one frame along — so its theorem is the
   base the others extend, and because it is the class `Pipeline.fst` already models concretely: nothing
   had to be assumed of a verb to state it.

**What the model is not.** It is not `Incremental.fs` clause for clause: the slot arrays, the `Order`
array, the sparse reads, the `WalkFrame`'s origins and tails are representation, and the model carries
the relation they implement — a row's cell at a step, found under its token. The differential
(`Proofs.Oracle`, the incremental cases) is what ties the two: production's `prime` and `refresh` beside
the extracted `prime` and `refresh` over generated row-local pipelines, priors and deltas built by
`Delta.diff`, with the delta's named rows read off the delta production was handed.

**Consequences.** `Incremental.prime` and `Incremental.refresh` move from the census to the ladder's
proved rows and `Incremental.result` to its tested row; the rest of the `Incremental` entry stands, its
reason now naming the row-local theorem and the classes still outside it. No production source changed:
the model found no defect. A later phase that covers a class above extends `Incremental.fst` (a new
`wstep` case, or a clause in `Pipeline.fst` first) and deletes that class's census members in the same
commit, as D30 requires.

## 2026-10-15 — D30: the operation census is ported from Fuaran.Core, and the two repositories have one coverage vocabulary; the census is the modelled packages' operations and the relational verbs

**Context.** Phase 259 carried only the package half of Fuaran.Core's coverage file into this
repository: every packable package is modelled or excluded. A modelled package therefore read as
covered whole, although `proofs/Pipeline.fst` takes the twelve verbs that evaluate no expression as a
parameter and nothing models the delta algebra, the incremental engine or the planner's rules. Those
gaps were recorded nowhere a check reads. Fuaran.Core closed the same gap for itself with an operation
census (its Phase 335): every public operation maps to a ladder row, a law family's roster or a recorded
exclusion with a class from a closed vocabulary.

**Decision.** Port that census (Phase 432), keeping Core's vocabulary and check shape.

1. **One vocabulary.** `proofs/coverage-exclusions.json` carries Core's `operationClasses` verbatim
   (`trivial`, `forward`, `obsolete`, `host-seam`, `measured-elsewhere`), with the same entry fields
   (`members`, `class`, `reason`, `forwardsTo`, `standIn`, `through`) and the same both-ways check:
   an unmapped operation fails, and so does an entry for an operation that is no longer published or
   that a ladder row now names. A term means the same thing in either repository. A change to the
   vocabulary is made in Core first and re-copied here. It is never edited here alone.
2. **The census.** It covers the packages a model in `proofs/modules.json` names. Their `method` lines
   in `api/*.txt` are censused, plus the `union-case` lines of `Transform`, spelled `Transform.<Verb>`.
   The verbs are this layer's operations even though the compiler publishes them as union cases, and a
   census of methods alone would never name them. A package with no model is not censused. Its
   package-level exclusion already records the decision for the whole package.
3. **The model route is the ladder.** A modelled operation is named in a `proved` or `tested` row's
   `evidence.operations` in `proofs.json`, as in Core. This repository ships no law-family operation
   roster, so Core's roster route is omitted from the check rather than carried empty. The law
   families that sample an operation are named in its entry's reason.
4. **The planner is not on the ladder.** `Pipeline.fst` proves the premise of the derive-filter
   reorder and the expression half of the totality verdict. It does not prove `Plan.rewrite`'s other
   rules or `Plan.isTotal` over the twelve unmodelled verbs. `Plan.rewrite`, `Plan.isTotal` and
   `Plan.explain` are therefore `measured-elsewhere`, and the entry says which half is proved.
5. **What the census says today** is generated into `proofs/README.md` ("What is not proved") by the
   `Proofs.Coverage` family (`CORE_APPROVE_LADDER=1` rewrites it). No count is written by hand. The
   phases that add models delete their entries in the commit that adds the ladder row.

**A premise that proved false.** The phase asked for entries for the public members of `Frame` and
`Kernels`. Neither has any: every module and type in `Frame.fs` and `Kernels.fs` is `internal`, and the
baseline publishes nothing from them. They reach the census only through the public operations that
call them, such as `DataFrame.prepare`, `DataFrame.evalPrepared` and `ColumnOps.applyPrepared`, whose
entries say that no model describes the dense representation.

**Consequences.** A newly published operation of a modelled package, or a new verb, reds the suite at
the commit that adds it and names itself. The code moves from `ProofsLadderTests.fs` to
`ProofCoverageTests.fs` together with the package clause, so `Proofs.Coverage` has one home, as in Core.

## 2026-10-14 — D29: the evaluator propagates and checks units — a column's unit rides every verb that keeps the column, addition and comparison require one unit, multiplication and division compose through Core's algebra, a conversion is explicit and answers a float, and a column that states no unit is unconstrained, so a unitless pipeline is untouched

**Context.** Since Phase 427 a schema entry is a `Field` that can carry a unit of measure (Core's
`Fuaran.Core.Unit`, Phase 426: a product of UCUM atoms with integer exponents, one canonical text per
product, `compatible` and `conversionFactor` reading what a unit denotes). The evaluator read none of
it: `Add`, `Sub`, `Mul` and `Div` typed their operands by cell type alone, so a model adding metres to
seconds evaluated, and every verb that produced a column (`Derive`, an aggregate, a window) made a fresh
field with no metadata — `Frame.withColumn` says so in its doc. The unit was a label.

**Decision.** The evaluator carries each column's metadata from input to output and refuses, before any
row is read, the operations a unit makes meaningless. The rules, each held by `UnitPropagationTests`
(one test per row, the refusal asserted by name) and by the law families named beside them:

| Where | Rule |
|---|---|
| A column | Its unit is its field's (`Field.Unit`). A column that states no unit is UNCONSTRAINED: it composes with anything and never refuses, so a pipeline over unitless columns answers byte-identically to `0.39.0` (held by every pre-429 vector in `conformance/laws/transform-laws.json` being byte-unchanged, and proved: `unitless_unchanged`, `checked_step_unitless`). |
| A literal, a param, a `now` | Unconstrained. A literal is given a unit by `Convert(Lit 5, m)`. |
| `Add`, `Sub` | The operands' stated units must be ONE unit — one canonical text; `km` beside `m` is refused as `N` beside `kg.m/s2` is, until converted, because implicit conversion hides exactly the error this exists to catch. The answer carries that unit; an unconstrained operand takes the other's. |
| `Eq` … `Ge` | The same rule; the answer is a boolean and carries none. `And`, `Or`, `Contains`, `StartsWith`, `EndsWith`, `Not`, `IsNull`: none. |
| `Mul` | Composes through `Unit.mul`; an unconstrained factor contributes nothing (`m * 2` is `m`, `m * m` is `m2`). |
| `Div`, `Quotient` | Composes through `Unit.div`; an unconstrained divisor leaves the dividend's unit, an unconstrained dividend is dimensionless over a stated divisor (`1 / s` is `/s`). `Mod` keeps the dividend's. |
| `Coalesce`, a `Case`'s arms, `Least`, `Greatest`, `InList` | One unit among the stated ones, in order, and the answer carries it (`InList` answers a boolean). A `Case` agrees its arms with its ELSE, the else first — it is the base the arms fall through to — so a mismatch names the else's unit first. |
| `Cast`, `Abs`, `Round`, `Floor`, `Ceil`, `Rounded` | Keep the operand's unit. |
| `Sqrt`, the text and date functions, `Length`, `IndexOf`, `DateDiffDays` | None. The algebra states integer powers only, so a root's unit is not derived; a text and a day count measure nothing. |
| `Convert(expr, unit)` | The one explicit conversion. Over an unconstrained operand it is a TAG; over an operand of the same unit the identity; over an operand of a compatible unit the exact ratio `Unit.conversionFactor` gives; across dimensions it is REFUSED. It is resolved by SUBSTITUTION against the step's schema before evaluation, as `InParam` and `Now` are: a tag or an identity becomes `Cast(FloatType, operand)`, a conversion `Cast(FloatType, operand) * Lit(Float factor)` with the ratio as a double — so a conversion always answers a FLOAT (a measurement), a null operand propagates, and a decimal operand is cast as `Cast` casts it; a caller that needs the exact product writes `Quotient` with the factor. The evaluator proper never meets a `Convert` (`resolved_convert_free`); `resolveExpr` resolves a stray one the same way. |
| `Sum`, `Mean`, `Min`, `Max`, `Median`, `First`, `Last`, `StdDev` | Keep the source column's unit. `Count`, `CountDistinct`: none. (`DataFrame.aggregateUnit`.) |
| `Lag`, `Lead`, `CumulSum`, `CumulMax`, `CumulMin`, `RollingSum`, `RollingMean` | Keep the source's. `RowNumber`, the ranks, `NTile`: none. (`DataFrame.windowUnit`.) |
| `Join` | A key pair is an equality, so the comparison's rule holds it: two stated units must be one unit (`join key 'a' = 'b'`), an unconstrained side matches anything. Non-key columns keep their own fields, the right's suffixed as D20 names them. |
| `Union`, `Intersect`, `Except` | A column pair is one column: two stated units are refused (`union column 'x'`), the left's field is kept, and a left that states none takes the right's stated unit (a union). |
| `Project`, `Filter`, `Sort`, `Limit`, `Distinct`, `Window` | Keep the input's fields — unit, label, description and extension metadata — a projection under its new name. |
| `Derive`, an aggregate column, a window column | Carry the unit the rule above derives and NO label or description: the step has no syntax to state one, and a derive onto an existing name is a fresh column (D-less until a step can name a label). |
| `Pivot` | The index columns keep their fields; every value column is one aggregate of `Values`, so each carries that aggregate's unit (`Sum` of kilograms is kilograms, a `Count` none). |
| `Unpivot` | The id columns keep their fields; `variable` carries none; `value` carries the one unit every value column states, and none where they differ or one states none — an unpivot melts measures of different kinds into one column by design, and a unit it cannot state it does not invent (`DataFrame.unpivotUnit`). |

1. **The refusal is typed and static.** `EvalError.UnitMismatch(step, expr, left, right)` names the step
   (`derive 'speed'`, `filter`, `join key 'd' = 'k'`, `union column 'd'`), the expression the two units met
   in — the node as written — and both units by canonical text. It is decided over the step's schema
   before any row, so an empty frame is refused exactly as a full one (the derive-typing family's built
   refusal holds it on a full and an empty frame alike). `errorString` renders it as
   `unit mismatch in derive 'x': m against s in add('d', 't')`.
2. **One check, read by everything.** `DataFrame.resolveUnits step cols expr` is the walk: the expression's
   unit (`None` for unconstrained) and the expression with every `Convert` resolved. `evalFilter` and
   `evalDerive` run it before their first row, as do the incremental seam's walk and the worker pool's
   hand-off (which falls back to the sequential path for a step the check refuses); the planner's verdict
   (`isTotal`) declines a step the check refuses, and its `Known` schema carries fields so a pruned or
   reordered pipeline's output schema is the reference's (`plannerLaws`: the planned schema is the
   reference's, metadata included); `SchemaWalk` states the unit, label, description and extension metadata
   of every output column (`ColumnKnowledge` gains the four members, built through the `ColumnKnowledge`
   module so the next member is additive; `schemaWalkLaws` holds the walk's metadata to the evaluator's); a
   registered pipeline query's result knowledge carries them, and registration refuses a declared field
   whose unit differs from the produced one (`ResultDisagreement.UnitDiffers`, both directions — the
   declaration is the contract); the incremental state wire and the registry wire carry a field's
   metadata through Core's own field document (`ColumnCodec.fieldJson`), the state wire keeping the
   `[name, type]` pair for a field that states none so a unitless state is the bytes it was.
3. **Proved, with the algebra as a parameter** (`proofs/Pipeline.fst` section 13; the rows
   `pipeline-unit-check-*` in `proofs.json`): an accepted expression evaluates convert-free; over a schema
   stating no unit a convert-free expression is accepted with no unit and UNCHANGED, step by step; every
   refusal names two different units or a conversion the algebra has no factor for; and each rule is the
   unfolding of the check. That the algebra is Core's — canonical in its texts, exact in its factors — is a
   named premise (`pipeline-unit-algebra-is-core`), Core's `proofs/Unit.fst`'s to prove and the unit
   differential's to instantiate (`ProofOracleTests`: the extracted check at Core's algebra beside
   production's, over the unit vectors and six hundred generated expressions).

**Considered and rejected.**

- **Implicit conversion of a compatible scale** (`km + m` scaling the kilometres). Rejected: a silent
  scale is the error class — pence read as pounds — and the explicit `Convert` costs one node.
- **A literal typed DIMENSIONLESS rather than unconstrained.** Rejected: `price + 1` over a priced column
  would refuse, and every unitless pipeline would refuse at its first literal beside a stated unit;
  unconstrained composes exactly as the unitless world did, and a literal that means a measurement says
  so with `Convert`.
- **A conversion that keeps the operand's type** (an int column in metres converted to kilometres as an
  int). Rejected: `1500 m` is `1.5 km`; a measurement under a factor is a float, and the typer's rule for
  a conversion is then one line (`Typing.cast FloatType`), with no second answer for an identity.
- **A `Sqrt` that halves exponents.** Rejected for now: the algebra states integer powers only, and a root
  of `m` has none; an unconstrained answer invents nothing, and a consumer that knows the root is in
  metres writes `Convert(Sqrt(x), m)` — a tag.
- **Refusing a join key of two DIFFERENT dimensions only** (the shard's wording). Rejected in favour of
  the comparison's rule: a key match is `Eq`, and `1 m = 1 km` is as wrong as `1 m = 1 s`.
- **Carrying the hand-off's schema as a `Field list`.** Rejected by the node leg: a hand-off crosses a
  worker boundary by structured clone, which cannot clone a map's comparer; it crosses as the canonical
  field documents, a string, as the step and the environment already do.

**Consequences.** `ColExpr` gains `Convert` and `EvalError` gains `UnitMismatch` (a consumer matching
either exhaustively adds an arm); `ColumnKnowledge` gains `Unit`, `Label`, `Description` and `Ext` (a
full literal gains four members — construct through `ColumnKnowledge.named` / `ofField`); the wire gains
`{"$type":"convert","expr":…,"unit":"<text>"}`; the planner's `Known` and the schema walk carry fields.
`0.39.0` is tagged, so the slot advances to `0.40.0` (STABILITY.md). What a consumer gains is the check:
a pipeline that adds metres to seconds fails where it is written, with the step, the expression and both
units named, and a sum over kilograms is a sum in kilograms on every host that reads the vectors.

## 2026-10-14 — D28: a window output that names a column the input already carries is refused with a `TypeError` naming it, through the window spec's one check, before any row is read; the join's suffix rule (D20) was considered and rejected for a window

**Context.** A `Window` appends its output column under the name its `As` gives. When that name was a
column the input already carried, the result held two columns of one name (Phase 450). D25 measured
what that costs: the evaluator reads a table's columns by name, so the result handed back through the
`Table` door reads its second column as the first, and Core's `Table.validate` refuses such a table
outright. D20 closed the join's way of producing a repeated name and left the window's as it stood.
This was pinned before the change: a `RowNumber` window over `people` whose `As` is `salary` answered
`Ok`, with `salary` twice in its schema.

**Decision.** The window is refused, with `TypeError("window output 'x' names a column the input
already carries; …")`.

1. **One check.** `windowRefusal`, the window spec's single check (D26), gains a last clause: after the
   bucket count, the read column, the partition keys and the order keys, an output name the schema
   carries is refused. An unknown key is still named first, because a spec whose keys do not resolve
   is wrong whatever its output is called.
2. **Before any row is read, in every form.** The frame form, the public row form (`windowStep`) and
   the incremental seam all ask `windowRefusal`, so all three refuse, over a source with no rows as over
   one with rows. A refresh whose source has gained the output's name refuses as the full evaluation of
   that source does.
3. **No new `EvalError` case.** `TypeError` is what the window already answers for a spec it cannot
   evaluate (a bucket count below one). A new case would break every exhaustive match over `EvalError`
   and the proof model's error type for a refusal a host reports by its text, as D25 reasoned for its
   own refusal.
4. **The planner's totality verdict already agrees.** `Plan.isTotal` has never counted a `Window` as
   total, and the pruning walk treats a window as a barrier, so no rewrite removes a column ahead of a
   window and the collision with it. The planned pipeline refuses as the pipeline as written does, and a
   test holds that over the shape a pruning would reach (`[ Window …; Project … ]`).
5. **The schema walk still appends.** `SchemaWalk` carries no refusal verdict, as for an unknown key
   (D26), so over a colliding window it names the column twice. That answer is about a pipeline the
   evaluator refuses, and the repeated name marks it. Retyping the column in place, as a `Derive` does,
   would describe an evaluation that answers `Ok`.

**Why a refusal and not D20's suffix.** The two verbs differ in who chose the name. A join's right
column names come from its operand, so a collision is a fact about two tables the author may not
control, and D20 keeps the join total rather than make it fail when an operand gains a column. A
window's `As` is the author's own choice, written into the pipeline, and the steps after it read the
column by that name. Under a suffix, `As = "rank"` over a table already carrying `rank` would put the
window's column under `rank_2`, and a later `Col "rank"`, `Sort` or `Project` would read the table's
`rank`: a plausible wrong answer with no error anywhere, which is the defect this phase removes. Under
the refusal, the author learns at once, by name, and renames the output or projects the old column
away. The cost is D20's own worry: a pipeline that ran over a table refuses over the same table once
it gains a column of the output's name. That is the honest answer there, because the pipeline's later
reads of that name have become ambiguous. **Replacing the column**, as a `Derive` does with a name it
already carries, was the third candidate and was not taken: a window reads its input's columns (its
`Of`, its keys) to make its output, so replacing one of them would let a single step's output erase its
own input under one name, a reading no SQL window offers and one every consumer would have to learn.

**The split law and the refresh.** The source seam's split law (`The source seam (Phase 415)`) now
holds over the colliding shape as a refusal: the whole pipeline refuses, and so does every split of it,
whether the window falls in the prefix or the remainder. The incremental refresh over a source that
gained the window's output name refuses as the full evaluation does.

**Consequences.** A behaviour change to a public evaluator, of D21's and D26's class. No public
signature moves, because the function that carries the rule is internal. It is versioned under the
`0.39.0` draft (`STABILITY.md`), which it rides. No vector in `conformance/laws/transform-laws.json`
names a window's output after an existing column, so none moves, and the corpus copy follows the release
rule (D6 (4)) unchanged. Two suite cases that used such a name only as a convenient output name now use
a fresh one.

## 2026-10-14 — D27: a refresh assembles its result from the prior one; no corpus shape is declined; the named tick exceptions are retired; a clock case holds the refresh against the full evaluation

**Context.** Phase 423 made the full evaluation a few passes over typed vectors. Phase 439 then found
that on three corpus shapes the refresh ALONE cost more than the full evaluation it replaces:
`lines`, `window CumulSum` and `group-by high-card`, 2.1 to 3.4 times (0.6 to 0.9 before 423). Phase
439's bound stayed green on the baseline arm, and D19 and D22 held three cells to their own post-423
cost as named exceptions until Phase 448: `lines` edit, `lines` append and `window CumulSum` edit, all
at 20,000 rows. The operator's rulings of 2026-10-12/13 bind this phase: the tick bound is
`max(1.6 x full, 1.5 x recorded pre-423 tick) + floor`, and a shape the seam declines (a visible
fall-back to the full evaluation, with the reason) is a legitimate answer where it is better for a
consumer.

**Measured first (448.A).** The refresh was timed apart from the diff and split by stage (Release,
Arm64, the 439 tree, 20,000 rows, the corpus's one-row edit). The refreshed span was not the cost, and
neither was re-keying (the delta carries the diff's keys). Result assembly was: 82% of the `lines`
refresh (both derived columns packed from every row's cell), 73% of the `group-by high-card` refresh
(the 10,000-row group table packed from its row arrays) and 41% of the `window CumulSum` refresh (the
window column built as a 20,000-cell list and then packed). The rest of the window's was its resume's
row-sized bookkeeping arrays.

**Decision, per shape (448.B).** No shape is declined. On each of the three, maintaining the result
is cheaper than recomputing it once assembly stops going through cells, and declining would make every
refresh a full evaluation and lose the edits and appends that are already far cheaper.

1. **`lines` (row-local steps): the prior result's column, written at the rows that moved.** When
   every step is row-local and the refresh reads in place, every other row of the result is the prior
   result's row. A computed column is then the prior column's typed vector, copied and written at the
   changed and appended rows (`patchedColumn`).
2. **`group-by high-card` (a maintained group-by): the prior group table, written at the recomputed
   groups** and grown by the new ones, through the same function. The group outcome's rows are an
   array now, so they are not converted between a list and an array on every refresh.
3. **`window CumulSum` (a resumed prefix fold): the column read straight off the typed carrier the
   scan wrote** (`runColumn`). The run's cell list, which the state's encoding carries, is deferred:
   it is read off the column only when the state is encoded, so no wire byte moves. The resume also
   writes its scanned suffix in one loop per carrier, and builds no position table where the run
   already holds the rows in arrival order.

Each copy answers what packing answers: the same carrier, values, absent rows and the zero values
under them. Where that cannot be shown (a carrier the copy does not handle, a written cell the carrier
does not hold, a field whose type moved), the refresh packs the cells as before. Making either new
path write one row wrongly reddens the incremental and window law families. A filter that keeps every
row now hands its order on rather than copying it.

**The named exceptions are retired (448.D).** `ScalingTests.tickBaselineExceptions`, the ceilings
beside it, the condition that read them and the main-suite case that pinned them are deleted. So is
the `-Ceiling` mode of `gates/record-tick-baselines.ps1`, which wrote nothing else. Every corpus cell is
judged by the two arms alike. On three clean Arm64 clock legs the three formerly excepted cells read
0.49–0.50 ms (`lines` edit), 0.43–0.44 ms (`lines` append) and 0.61–0.62 ms (`window CumulSum` edit),
each within 1.6 times its full evaluation. The `lines` edit cell had read 1.39 to 5.27 ms.

**Held (448.C).** A seventeenth clock case times the refresh alone, handed the delta the diff built,
against the one-thread full evaluation on every corpus node the seam accepts: `refresh <= full +
floor`. The two nodes the seam declines, `inner join` (a combining join's rows are not its left rows)
and `pivot` (its output depends on rows the delta does not name), are printed with the plan's reason
and not timed. A main-suite case pins that list against `Incremental.plan` and checks that a declined
node's refresh reports a full evaluation with that reason, so a shape cannot come to be declined, or
stop being declined, silently. Figures: `docs/performance.md`, "A refresh assembles its result from the
prior one".

**Where the case does not time, and why.** The refresh case times the edit at 1,000, 20,000 and
100,000 rows and the append at 1,000 and 100,000 (`ScalingTests.refreshSizes`). At 20,000 rows the
append's refresh on the small-group grouped nodes (`byRegion`, `filter > groupBy`, `filter > groupBy >
filter`) read 0.8 to 2.5 times a full evaluation that costs 0.24 to 0.45 ms. That was measured, not
assumed: an in-place append grows two per-row reference arrays (the row tokens and a step's cell
cache), about 160 KB each at that size, just past the runtime's large-object threshold, and the
collections those allocations trigger decide the reading. With the threshold raised past them the same
refresh read 0.43 times the full with no gen-2 collection, and the same cell read 1.05 times from a
pool of 500 prepared sources and 1.3 to 2.4 times from one, with the gen-2 count moving from 0 to 17
per 500 calls. The diff and refresh together stay inside the append tick case's bound on every
reading, and the edit and every other size of every shape read 0.13 to 0.98 of the full. It was so
before this phase too (1.0 to 2.0). Keeping those arrays below the threshold is a representation change
to the seam's per-row caches, not result assembly, and it is a later phase's. The case's reach was
proposed to the operator with this evidence; the ruling is recorded with the phase.

**Consequences.** No public signature, answer or wire byte moves (`STABILITY.md`, class `none`,
`performance`), and the `0.39.0` draft is ridden. The clock leg holds seventeen cases. Re-recording the
baseline table is unchanged (D19).

## 2026-10-13 — D26: a partition key that names no column is refused with `UnknownColumn`, through the one resolution a sort key passes; the window's keys are checked before any row is read

**Context.** D21 made a sort key that names no column a refusal, in a `Sort` and in a `Window`'s
ordering. It left a `Window`'s `PartitionBy` alone, because a partition key is not a sort key
(Phase 441). `windowPartitionIdx` still dropped a name the schema lacks, so a misspelled partition key
partitioned by nothing. The window then answered one partition's column as if it were several: a
plausible wrong result, `Ok`. This was pinned before the change: a `RowNumber` window over `[ "dpt" ]`
on a table carrying `dept` answered `Ok`.

**Decision.** A partition key that names no column is refused with `UnknownColumn(name, available)`,
the error every other verb answers.

1. **One resolution.** `unknownKey` is the one check for the keys a step takes from a pipeline to
   order or partition its rows by. `unknownSortKey` is now that check over a sort's columns, and
   `windowRefusal` is the window's whole spec check: a bucket count below one, then a read column the
   schema lacks, then the partition keys, then the order keys, refused at the first failure. Because
   the partition and order keys pass through the same function, the two rules cannot drift apart
   again, which is how the partition rule was left behind in D21.
2. **Partition keys before order keys.** Where both a partition key and an order key are unknown, the
   partition key is named. That is the spec's own order, and SQL's `PARTITION BY … ORDER BY`. The
   function's own checks still come first, as they did under D21.
3. **Before any row is read.** The frame form (`windowColumnOfWith` and `windowColumnOverWith`), the
   public row form (`windowStep`) and the incremental seam all ask `windowRefusal`. The incremental
   walk asks it at the step's entry, ahead of resuming a prior run or ordering the rows, as its `Sort`
   step does under D21. A source with no rows refuses exactly as one with rows does.
4. **The planner's totality verdict already agrees.** `Plan.isTotal` has never counted a `Window` as
   total, so no rewrite reorders a filter across one or prunes a column behind one. An unknown key
   leaves that unchanged.

**What it does not change.** A `Window` whose `As` names an existing column still appends a second
column of that name (D20, D25). The exported comparator `rowCompareBy` still drops an unknown key, as
D21 says. The proof model treats `Window` as one of the verbs it does not model, so it holds no copy
of this rule.

**Consequences.** A behaviour change to a public evaluator, of the same class as D21's. No public
signature moves, because both new functions are internal. It is versioned under the `0.39.0` draft
(`STABILITY.md`), which it rides. No vector in `conformance/laws/transform-laws.json` partitions a
window on a column its table lacks, so none moves, and the corpus copy follows the release rule
(D6 (4)) unchanged.

## 2026-10-13 — D25: the evaluator's source seam is the prepared source, entered by position; `prepareRows` reads a schema and its rows, so a split pipeline hands its prefix through it rather than through a re-entered `Table`

**Context.** Every evaluator entry point took a whole table (Phase 415). A placement that splits a
pipeline — a prefix pushed down to a database as a statement, a cached cell, a checkpoint — had to build
a `Table` from the rows it held and feed it back in. The `Table` door reads columns by NAME:
`Frame.ofTable` looks each schema field up with `Table.tryColumn`, which answers the first column of that
name. So a result that names a column twice re-enters wrong. D20 closed the join's way of producing such
a result, but the evaluator still produces one itself: a `Window` whose `As` names an existing column
appends a second column of that name, which D20 left as it stands. Handed back through the `Table` door,
that second column reads the first one's cells.

This was measured, not assumed. The split law below was run once with the hand-off going through
`prepare (toTable result)` instead of through the seam. It failed on the generated sample (a `CumulMax`
window whose `As` names an existing int column, split after the window) and on the test written for the
shape. Through the seam, both hold.

**Decision.**

1. **The carrier is `Prepared`.** The evaluator's own working form is a schema and one column vector per
   field, in order: a result that carries its column identities by position, not by name. `Prepared`
   already had a way in from a table (`prepare`) and a way out of an evaluation (`evalToPrepared`, Phase
   342). Every evaluator over a prepared source already takes it, and so do `ColumnOps` and the
   incremental seam. What it lacked was a way in that is not a `Table`. A new carrier type would have been
   a second form of the same thing, with one user.
2. **The new door is `DataFrame.prepareRows schema rows`: a schema plus a row reader, read by position.**
   Cell `c` of every row is column `c` of the schema, so a schema that names a column twice keeps both,
   as the evaluator's own frame does. Rows, not columns, because the readers a host holds (a statement's
   reader, a cache file) yield rows. Each column is packed under its field's type exactly as the
   evaluator packs the rows a verb emits. So the seam checks no more of a cell's type than the `Table`
   door does: a cell its column's type cannot hold is carried the way that door carries one. A row whose
   width is not the schema's is refused with `TypeError`, naming the row. No new `EvalError` case was
   added, because that would make an additive door a breaking change for every exhaustive match. The way
   out is `toTable`, whose `Columns` follow its `Schema` by position, or the `Prepared` itself.
3. **The whole-table entry points stay, and were already adapters.** Each of them is `prepare` followed
   by the prepared-source evaluator, and has been since Phase 267. This was checked against the tree
   before anything was built, and nothing in them moves.
4. **How it relates to the incremental seam.** The incremental seam takes rows APPENDED to a source it
   already primed: a delta against a state it holds. This seam takes a whole source, by position. A
   source entered through it primes the incremental seam (`Incremental.primePrepared`) exactly as the
   table it came from does, and a test holds that.
5. **How it relates to a host that pushes a prefix down.** Such a host runs a pipeline's prefix as a
   statement, reads the statement's rows into `prepareRows`, and runs the remainder with
   `evalPrepared`. No `Table` is built between the two halves, so nothing between them is read by name.
   The host still receives the source and the pipeline together, and choosing where to split is still
   the host's decision. The evaluator offers a split that is safe at every step, not a place to make one.
6. **What it deliberately does not do.** It does no IO and nothing asynchronous: the reader is a
   sequence the host hands in, pulled once, synchronously, and the evaluator opens and closes nothing.
   It does not stream. The rows become columns before the remainder runs, because every step reads whole
   columns. A `Ref` that a `Join`, `Union`, `Intersect` or `Except` names still resolves to a `Table`
   (`resolve : string -> Result<Table, EvalError>`). A resolved operand is a whole second source, not a
   prefix. Widening the resolver's type would move every entry point's signature, a breaking change for
   a shape that neither the corpus nor any host has needed. The planner plans each half of a split on
   its own, so a rewrite that would have crossed the split is not taken. That changes no answer: the
   planned evaluation equals the reference as written (`Conformance.plannerLaws`).
7. **The `Table` door is unchanged.** It reads by name, which is Core's `Table` contract:
   `Table.validate` refuses a repeated name, so a valid table never meets the defect. A `Window` result
   with a repeated name that re-enters through that door is still read wrong. That rule belongs to the
   `Window`'s `As`, not to this seam, and it is recorded here with its measurement so that the phase
   which takes it starts from it.

**The law.** The suite list `The source seam (Phase 415)` splits every pipeline after every step: the
prefix is evaluated with `evalToPrepared`, its rows are read out by position and back in through
`prepareRows`, and the remainder is evaluated with `evalPrepared`. Every split answers what the whole
pipeline answers, refusals included. That holds over the transform law vectors, and over 400 generated
pairs of chained pipelines under a param env, with more than 500 splits falling strictly inside a
pipeline. It also holds over Phase 411's collision shape (a left already carrying `x_right`, so the
right's `x` is named `x_right_2`) and over the repeated-name `Window` shape above.

**Consequences.** `DataFrame.prepareRows` is new and additive. Nothing else moves: no signature, no
output and no wire byte. It is recorded under the `0.39.0` draft in `STABILITY.md` without moving its
number. The corpus copy follows the release rule (D6 (4)) unchanged, because no vector moves.

## 2026-10-13 — D24: the bare `Rank` window function is deprecated in documentation, keeps its wire tag, and may be removed no earlier than `1.0.0`, never re-pointed

**Context.** `WindowFn.Rank` is the dense rank (`1, 1, 2`). SQL's `RANK()` is the competition rank
(`1, 1, 3`), so the name misleads any reader coming from SQL. Phase 101 added the two explicit
spellings: `DenseRank`, which computes exactly what `Rank` computes, and `CompetitionRank`, which is
SQL's `RANK()`. It documented `Rank` as dense and kept it, because re-pointing it would silently
change the output of every pipeline that uses it. Until now nothing told an author who writes it.

**1. How it is marked: documentation only.** Three ways were weighed.

- *`[<Obsolete>]` on the union case* — rejected. F# reports an obsolete union case wherever it is
  constructed AND wherever it is matched (FS0044). An exhaustive `match` over `WindowFn` must name
  the case, so every consumer that matches exhaustively would get a warning it cannot remove except
  by adding a wildcard. Adding a wildcard gives up the exhaustiveness check on a closed vocabulary. A
  consumer that builds with warnings as errors would break on the next package update. This
  repository's own evaluator, codec and conformance kit would also warn at every one of their arms.
- *A validator warning on the wire form* — rejected. No verb or reader here has a channel for
  advisory findings: every check answers `Ok` or a refusal. Adding one for a single deprecated tag is
  a new seam with one user, and the rule-of-three bar is not met. Refusing `"rank"` would break every
  stored document.
- *Documentation* — taken. The case's doc comment opens with the deprecation and names both explicit
  spellings, saying which one is the same computation. An IDE shows it in the tooltip at the point
  where an author writes `Rank`. It is the one marker that cannot break a build.

**2. The wire form and the encoder.** `"rank"` decodes to `Rank`, unchanged. The encoder keeps
writing `Rank` as `"rank"`, so a document holding it re-encodes byte for byte and decodes to the same
value. Encoding it as `"denseRank"` would make it decode to a different case, which breaks the codec's
round trip. A new pipeline is expected to construct `DenseRank` (or `CompetitionRank`), and each
encodes as its own tag. So the encoder never writes `"rank"` unless the pipeline it was given holds
`Rank`.

**3. When it may go, and the rule.** `Rank` is **never re-pointed** at the competition semantics:
evaluating a stored pipeline must not change its answer under the same tag. It may be **removed no
earlier than `Fuaran.Compute.*` `1.0.0`**, the first major version, and only on these terms:

- the removal is its own `STABILITY.md` entry of class `removal`, with the migration
  `Rank` → `DenseRank`;
- in the same release, the decoder keeps `"rank"` as a legacy alias that decodes to `DenseRank` and
  normalises on re-encode, as `"cumSum"` does for `CumulSum`. A stored document then evaluates to
  the same table after the removal as before it;
- before `1.0.0` it stays, deprecated in documentation, through every `0.x` release.

**The class.** No signature, no output and no wire byte changes, so this is documentation, recorded
under the `0.39.0` draft without moving its number.

## 2026-10-13 — D23: an evaluation decides a derive its params decide; `Round`, `Floor` and `Ceil` over an undecided operand are undecided; the rest of the cells' share is recorded, not decided

**Context.** D5 types a derived column by its expression, and leaves it to the cells only where the
typer answers `Unknown`. Under that rule an empty or an all-null frame types a cells-decided column
`string`, so a full answer and an empty one disagree, and the static schema cannot name the column.
Phase 413 walked every shape the typer answers `Unknown` for, to decide each one statically where the
expression determines the type and to record why the cells must decide where it does not. The walk
also found that one shape the typer DID decide was decided wrongly.

**The shapes, each with a sample and its verdict.**

| # | Shape | Sample | Verdict |
|---|---|---|---|
| 1 | A bound `Param`, and every node whose typing it leaves undecided | `Param "p"`, `Binary(Add, Col "i", Param "p")`, `ApplyFn(Round, [ Param "p" ])` | **Decided at evaluation** by the bound cell (ruling 1); the static reader holds no env and still cannot name it |
| 2 | `Round`, `Floor`, `Ceil` over an operand the typer cannot decide | `ApplyFn(Round, [ Case([ c, Col "i" ], Col "m") ])` | **Was decided `float`, wrongly**: now undecided (ruling 2) |
| 3 | An unbound `Param` | `Param "unbound"` | Residue: every row is refused `UnboundParam`, so only a frame with no row answers |
| 4 | An unpinned `Now` | `Now NowGrain.Date` | Residue (ruling 3) |
| 5 | A column the schema does not carry | `Col "nope"`, `Binary(Add, Col "nope", Lit(Int 1))` | Residue: statically the schema in hand may be open; at evaluation every row is refused `UnknownColumn` |
| 6 | A join of two types the exact typer keeps apart, and every node over one | `Case([ c, Col "i" ], Col "f")`, `Coalesce [ Col "m"; Lit(Int 0) ]`, `ApplyFn(Least, [ Col "i"; Col "f" ])`, `Case([ c, Col "s" ], Col "i")` | Residue (ruling 4) |
| 7 | An operator over operands it refuses | `Binary(Add, Col "s", Col "i")`, `Not(Col "i")`, `ApplyFn(Abs, [ Col "s" ])`, `Binary(Div, Col "m", Col "i")`, `Rounded(Col "f", r)`, `Binary(Eq, Col "s", Col "d")` | Residue (ruling 5) |
| 8 | A bound param whose substituted arms put a float beside a decimal | `Case([ c, Param "pf" ], Param "pd")`, `pf` a float and `pd` a decimal | Residue (ruling 1) |

An `Unpivot` of a string beside an int value column is the same rule's one cells-decided unpivot, and
D18 already refuses it where the cells hold both.

**1. A bound param decides the derive at evaluation.** `DataFrame.derivedTypingIn env cols e` is the
rule every evaluating path reads: the reference evaluator, the morsel runner, and the incremental
seam's row walk and chunked path. Where `derivedTyping` decides or refuses, it is the answer. Where it
leaves the column to the cells, the answer is what the same rule DECIDES over `e` with the
evaluation's params substituted. That is `ColExpr.substitute`'s expression, which evaluates exactly as
`e` does (the substitution witness `paramLaws` certifies). So a bound param types as the literal it is
bound to, on a full, an empty and an all-null frame of that evaluation alike. The planner and every
static reader (`SchemaWalk`, `PipelineQuery`, `Plan`) hold no env and read `derivedTyping` unchanged.
So a param-decided derive is still not total to the verdict, and the planner still moves no filter
past it.

The substituted rule DECIDES and never REFUSES. Where the substituted arms put a float beside a
decimal (row 8), the column stays the cells', as it was. Refusing it would turn every frame whose cells
hold one family from an answer into a refusal. The cells already refuse the frames that hold both. That
consistency with a literal arm, which D5 refuses statically, is the one thing this ruling declines. It
is a behaviour break with no defect behind it, and is left for a phase that wants it.

**2. `Round`, `Floor` and `Ceil` over an undecided operand are undecided.** The typer answered `float`
for any operand that was not a decided decimal, `Unknown` included. Over a decimal those three answer a
decimal (Phase 277), so an undecided operand that turned out to be one put a decimal cell in a column
declared `float`. Two cases were observed before the fix. `Round(Param p)` with `p` bound to a decimal
gave a `float` column holding `Decimal "3"`. `Round(Case([ i > 0, Col "i" ], Col "m")` over rows taking
both arms threw at the `Table` boundary ("column 'd' cannot be held by its type"), where it should
have answered. Now an undecided operand gives an undecided answer. The cells decide such a column, or
a bound param does by ruling 1. The cells refuse a float beside a decimal by name. The typer and the
model (`proofs/Pipeline.fst`, `typing_apply_fn`) change in the same clause.

**3. An unpinned `Now` stays undecided.** A pinned evaluation substitutes the witness's cell before
anything is typed, so the derive is decided by its literal on every frame of that evaluation, and the
shard's problem does not arise there. An unpinned one refuses every row. The static reader could name
`date` for the date grain. But that name would rest on the host's witness answering a `Date`, which
nothing checks (`nowLaws` certifies determinism, not the cell's type). A static type the evaluator
can contradict is worse than none. The timestamp grain's unit is the witness's in any case.

**4. A join of two types the exact typer keeps apart stays the cells'.** Today a frame whose rows
take only the int arm of `Case(int, float)` answers an int column of ints, and one whose rows take
both answers a float column. Deciding `float` statically would have to be honest at the derive
itself: the int cells would have to become floats there, or the static schema would claim `float`
over int cells for a later step. The totality verdict reads that claim as exact, because two ints
added can overflow where two floats cannot (D3). So the decision is a value change on every frame
that answers with ints today. That reopens D3's and D5's rulings, and is left to an operator ruling
rather than taken inside a typer phase. A pair no widening relates (a string beside an int) is refused
by the cells where both occur (D18). Deciding it statically would refuse frames that hold one of them.

**5. An operator over operands it refuses stays the cells'.** Every row with its operands present is
an error, so every frame that answers holds no present value. The cells already type that frame
`string`, which is what a decided "no present value" would say. No two answering frames disagree.
What is missing is only the static name of a column that can hold no value. Buying it costs an
absence rule per operator in the typer and in its model, and changes no answer.

**The law.** `DeriveTypingConformance.laws` gains a sixth law. A derive the walk leaves to the cells,
but decides over its expression with the evaluation's params substituted, types its column the same
over a full, an empty and an all-null frame. The menu gains `Round(Param p)`, `i + p` and a `Round`
over an undecided int/decimal join, and the param is drawn as an int, a float or a decimal. The suite
holds the law over GENERATED expressions too (`DataFrame.Typing`, "a decided derive types its column
the same over a full, an empty and an all-null frame (Phase 413)"). Wherever `derivedTypingIn`
decides, the column takes that type over every frame, every present cell is of it or widens into it,
and a schema-decided type is the one `SchemaWalk` names. Restoring the float answer for an undecided
`Round` turns it red. So do the explicit cases in `DeriveTypingTests`. Restoring the env-less rule in
the incremental seam turns the prime/refresh case and the law family red. In the model,
`derive_type_rows_free` now covers a derive `derived_typing_in` decides. Requiring `ByCells` instead
of `Decided` fails its proof.

**The class.** No public signature moves (`derivedTypingIn` is internal). What a consumer can observe
is `behaviour`:

- a `Round`, `Floor` or `Ceil` over an undecided operand is no longer typed `float` by `typeOf`,
  `SchemaWalk` or `PipelineQuery`, which now names it `TypeUndecidable`;
- such a derive is typed by its cells or its params;
- a bound-param derive over an empty or all-null frame is typed by the param's cell where it was
  `string`.

`STABILITY.md` records it under the `0.39.0` draft, which already carries a BREAKING class.

## 2026-10-13 — D22: the `lines` APPEND and `window CumulSum` edit ticks at 20,000 rows join D19's named exception on the same terms, and a clock case is red only when the same cell is red on every counted attempt

**Context.** D19 holds every corpus tick cell to `max(1.6 x full, 1.5 x baseline) + floor`. It holds one
named exception, `lines` edit at 20,000 rows, to 1.5 times its recorded post-423 cost plus the floor,
and to neither other arm. A later landing round went red on the neighbouring cell, `lines` APPEND at
20,000 rows. An alternating A/B on the Arm64 machine showed the cell reads the same with or without
that round's changes, against a baseline-arm allowance of 2.255 ms:

| Tree | Readings |
|---|---|
| Main | 2.52, 2.36, 2.39 ms |
| Main plus the round's changes | 2.02, 2.38, 2.62 ms |

Phase 439's own landing gate had read it at 1.854 ms. The cell is the same noisy post-423 shape as its
edit tick, not a regression of the round.

**The operator's ruling (2026-10-12, the fifth), in substance:** the `lines` APPEND tick at 20,000 rows
joins the named exception on the same terms. Its ceiling is today's cost, judged by the leg's
three-attempt rule and pinned by the main-suite case. Like the first entry, it is removable only by
Phase 448's acceptance.

**What was done.**

- **The second entry.** `ScalingTests.tickBaselineExceptions` gains exactly one entry:
  ("append", "lines", 20000, "the same noisy post-423 shape as its edit tick, which Phase 448 owns").
  The pin case ("the tick bound's named exceptions are exactly the ruled ones") lists the two ruled
  entries and nothing else.
- **The ceiling.** It was recorded by `gates/record-tick-baselines.ps1 -Ceiling 'append|lines|20000'`
  from main's tree, `12a825d`, by D19's method: the median over three full legs, none discarded, of each
  leg's best.
  - The clean legs' bests were 1.794, 1.915 and 2.196 ms, so the median is 1.915 ms (0.388911
    calibration units, `tickExceptionCeilings`). One leg with a discarded window was set aside.
  - The allowance is about 2.87 ms plus the floor, roughly 3.02 ms in all.
  - Every A/B reading above is under it.
- **Why the recording waited overnight.** The first two recording attempts, on the evening of
  2026-10-12, found no clean leg in eight. The machine was running a single-threaded workload at 1.9
  times its calibration baseline: a 2,000-reading probe had a median of 1.909. That is consistent with
  the operating system throttling a background process. Once the operator exempted the runtime from
  that throttling, a 200-reading probe read 1.000 and the recording took four legs.

**Refreshing it** follows D19: the operator decides, the script is the only writer, and the entry goes
when Phase 448's acceptance removes it.

**Amended 2026-10-13 — the operator's sixth ruling.** The first full gate with the append exception
in place reached a counted RED in its edit case. All three counted attempts were red, with no window
discarded, but a different cell was red on different attempts:

| Attempt | Red cell | Tick | Allowed |
|---|---|---:|---:|
| 1 | `lines` edit @ 20,000 (the first exception) | 3.155 ms | 2.822 ms |
| 2 | `window CumulSum` edit @ 20,000 | 2.351 ms | 1.949 ms |
| 3 | `window CumulSum` edit @ 20,000 | 1.991 ms | 1.949 ms |

`lines` edit @ 20,000 read 3.155, 1.850 and 1.751 ms. `window CumulSum` edit @ 20,000 read 1.916, 2.351
and 1.991 ms. The leg's rule was per CASE: red when every attempt has some red cell. With several noisy
cells, that reddens a case where no single cell is red every time.

The ruling ("Both"), in substance:

1. **The per-cell rule.** A clock case is red only when the SAME cell is red on all three counted
   attempts; different cells red on different attempts is green. The saturation path (no verdict,
   exit 3) is unchanged.
   - It is implemented as `ScalingTests.clockCellVerdict` over each attempt's red-cell set (their
     intersection). A case that judges many cells raises `ClockCellsRed` naming them, and a case with
     one assertion is one cell, so its verdict is the rule it always had.
   - The main-suite case "a clock case is red only when the same cell is red on every counted attempt"
     pins both halves:
     - a different cell red per attempt is green;
     - one cell red on all three is red, whatever else is red beside it.
2. **The third named exception.** `window CumulSum` edit at 20,000 rows joins the exception on the
   same terms, with its reason ("one of the shapes Phase 448 owns: pre-423 median 1.201 ms, post-423
   1.45–2.35 ms against a 1.95 ms allowance").
   - Its ceiling was recorded by `-Ceiling 'edit|window CumulSum|20000'` from `12a825d`. Three full
     legs, none discarded, gave per-leg bests of 1.733, 1.840 and 1.825 ms. The median is 1.825 ms
     (0.371086 units), an allowance of about 2.88 ms with the floor.
   - The pin case lists exactly the three ruled entries.

## 2026-10-12 — D19: the corpus tick bound gains a second arm, the tick's own recorded pre-423 cost, one table per CPU architecture; and the append diff's cheap prefix test is restored

**Context.** The clock leg holds every corpus node's one-row tick (edit and append; `Delta.diff` plus
the refresh) against the full evaluation it replaces: `tick <= 1.6 x full + floor` (Phases 274, 283,
327, 359 and 371). Phase 423 made the `Table` boundary a view. Full evaluations fell 2 to 5 times on
`lines`, `window CumulSum` and `group-by high-card`, and those cells read ×2.4 to ×4.5 against 1.6. The
gate's verify arm was waived to land 423, and the bound was left to the operator (Phases 285 and 327).

Phase 439 re-measured both trees on one machine, the full clock leg in Release, interleaved. The
figures are in `docs/performance.md` ("The tick bound after the boundary became a view"). Three
findings:

- **The edit ticks were no dearer than before 423** (within 1.17×).
- **The append ticks of the grouped nodes were 1.4 to 2.0 times dearer.** Timed apart, the cause is
  the append diff, which was 1.6 to 2.9 times its own pre-423 cost. Its prefix walk compared every row
  through a closure where Phase 359 had tried a cell reference check first. Only a clock sees this: the
  diff allocates nothing for it and mints no extra key.
- **On three shapes the refresh alone now costs 2.1 to 3.4 times the full evaluation** (`lines`,
  `window CumulSum`, `group-by high-card`). It cost 0.6 to 0.9 before 423.

Four bounds were put to the operator:

1. A recalibrated ratio. It needed about ×5 to be green, and it hid the append regression.
2. An absolute non-regression bound against each cell's recorded pre-423 tick.
3. Both of these arms together.
4. A split: the refresh against the full, and the diff held only by its counts. The count arm is blind
   to the regression, and the refresh arm was red on the three shapes.

A work bound (rows the refresh evaluates against rows the full evaluation reads) reaches a verdict on
any machine, because it needs no clock. It is green on every tree, including the regressed one, so it
complements a clock bound and cannot replace one.

**The operator's ruling (2026-10-12), in its own words, abridged to what binds this repository:**

> 1. The assertion: option C at c = 1.5. The incremental tick case (edit and append, every corpus node)
>    asserts tick ≤ max(1.6 × full, 1.5 × recorded pre-423 tick) + the existing per-call floor.
> 2. Fix the append-diff regression in `Delta.fs` in this phase. Restore the cheap prefix comparison
>    before the value compare (`ColumnRead.sameAt`), without changing any diff result. Prove the results
>    are unchanged with the existing delta laws and vectors. The phase lands only when the new bound is
>    GREEN.
> 3. Baselines: the pre-423 ticks, one table per CPU architecture. Record this machine's (Arm64) table
>    from the pre-423 tree `ccbddc7`. Ship a recording script so another architecture can record its own
>    table from `ccbddc7`. A machine whose architecture has no table must NOT pass vacuously: the absolute
>    arm reports loudly, naming the missing table and the script. Keep the 1.6 × full arm active
>    everywhere.
> 4. The finding that refreshing costs more than recomputing on lines, window CumulSum and high-card
>    group-by is NOT this phase's to fix; it goes to a successor phase. Record the finding and the
>    numbers.

**What was done.**

- **The condition.** `ScalingTests.judgeCorpusTick` holds both corpus tick cases to
  `tick <= max(tickBound * full, tickBaselineFactor * baseline) + floor`, with `tickBound` 1.6 and
  `tickBaselineFactor` 1.5. The `Scaling` tick case keeps the ratio alone.
- **The baselines.** `tickBaselines` holds, per `RuntimeInformation.ProcessArchitecture`, each cell's
  pre-423 tick (the median over clean legs of each leg's best, per the second ruling below) in units
  of the leg's calibration baseline. A unit scales with the machine within
  an architecture, as the floor does (Phase 327). The integer chain does not track a memory-bound tick
  across architectures, so tables are not shared across them.
- **A missing table.** On an architecture with no table, or for a cell missing from one, every cell
  prints `NO TICK BASELINE TABLE for architecture …` naming the script and holds the cell to the ratio
  arm alone. That is the stricter condition, so a missing table can redden a leg and never greens one.
- **The Arm64 table** was recorded by `gates/record-tick-baselines.ps1` on 2026-10-12: three full clock
  legs of `ccbddc7`, no window discarded. The two corpus cases run alone read 10 to 25 percent low
  against the gate's full leg, so the script runs the whole leg.
- **The append fix.** `Delta.ColumnRead.samePrefix` compares a column's whole prefix in one typed loop:
  both validities, then the two value vectors, with no closure per row. The append diff walks the rows
  only where that fails. Its `true` is sufficient for `sameAt i i` at every row of the prefix, and its
  `false` decides nothing. No diff result moves: the delta and append laws, the `diffTrustingPrefix`
  falsifier and the delta wire vectors are unchanged and green. After it, the grouped append ticks are
  0.46 to 0.88 of their pre-423 cost.

**Refreshing a table, and who decides.** A table is the contract a cell is held to, so refreshing one
is the operator's decision, recorded here as a dated note under this entry, and never a re-run to make
a red leg green. There are two occasions:

- **Adding the first table for an architecture.** Anyone may record it with the script from
  `ccbddc7`. Landing it is an operator act, because until then that architecture's leg holds the
  stricter ratio-only condition.
- **Changing the baseline TREE** (a later reference than `ccbddc7`), for example after a phase
  deliberately trades tick cost for something else. That is a new ruling, and every architecture's
  table is re-recorded from the new tree in the same change.

A table is never edited by hand, and never re-recorded from the tree under test.

**Scope.** `src/Fuaran.Compute.DataFrame/Delta.fs` and `gates/record-tick-baselines.ps1` lie outside
the phase's declared files and were approved as widenings with the ruling. No public signature moved:
`ColumnRead` is internal.

**Not decided here.** The three shapes whose refresh costs more than recomputing are admitted by the
baseline arm, not by the seam's claim. Making them cheaper than recomputing again is Phase 448's
work.

**Amended 2026-10-12 — the operator's second, third and fourth rulings.** The first ruling's bound
went to the gate and reached a counted RED. Gate runs 1 to 4 on the same tree had reached no verdict.
Two cells were red on all three counted attempts:

- `lines` edit at 20,000 rows: 2.59–5.27 ms against a baseline arm of 1.95 ms.
- `window CumulSum` edit at 100,000 rows: 7.66–8.02 ms against 7.45 ms.

The second cell's baseline was its single lowest pre-423 reading (4.97 ms; its pre-423 legs spread
from 4.97 to 7.46 ms). The first cell is a real regression. Three further rulings followed, each
summarised here as given:

- **Second.** A cell's baseline is the MEDIAN, over at least three full legs with no window
  discarded, of each leg's best reading, and the Arm64 table is re-recorded that way from `ccbddc7`.
  One named exception, `lines` edit at 20,000 rows ("a real 1.3–2× post-423 regression on a shape
  Phase 448 owns"), is listed in `ScalingTests.tickBaselineExceptions`. It is printed on every run,
  pinned exactly by a main-suite case so that no second exception can be added silently, and removable
  only by Phase 448's acceptance.
- **Third.** The exception's shape is a ceiling of TODAY'S COST: the cell is held to
  `1.5 x its recorded post-423 tick + floor`, and to neither other arm. The figure is recorded by the
  same script and method (`-Ceiling`) from the tree carrying Phase 423 and this phase's `Delta.fs`
  fix, and stored beside the exception with its provenance (`tickExceptionCeilings`). The second
  ruling's first wording, "ratio arm only", would have held the cell to ×1.6 of a 0.59 ms full
  evaluation, red on every run; it was corrected here.
- **Fourth.** The ceiling as recorded is kept. The cell is judged by the clock leg's own rule, like
  every other cell: red only when red on all three counted attempts.

**What was recorded.**

- **The baselines.** The Arm64 table was re-recorded on 2026-10-12 from `ccbddc7`: three full legs,
  none discarded.
- **The ceiling.** It was recorded from `3caa557` (the branch tree carrying the fix; committed in
  `1453743`). It took five legs to get three clean ones, after an earlier attempt found none clean in
  nine. The per-leg bests were 1.627, 1.789 and 1.783 ms, so the median is 1.783 ms (0.362989
  calibration units). That makes the allowance 1.5 × 1.783 + 0.147 = 2.821 ms on Arm64.
- **The evidence the ceiling inherits, for Phase 448.** The cell has 34 counted readings on the branch
  tree: minimum 1.389, median 2.397, p90 3.484 and maximum 5.270 ms. Nine are above 2.821 ms. They form
  two clusters (about 1.6–2.1 and 2.6–3.6 ms) with a tail. No leg had the cell above the allowance on
  all of its counted attempts. Its pre-423 readings were 1.30 to 1.59 ms.
- **Legs that discard windows.** The script now sets aside a leg with a discarded window and runs
  another in its place, up to `-MaxLegs`. Every recorded figure therefore comes from legs with none
  discarded, as the second ruling asks.
- **Why so many runs, on this machine.** Discarded windows and no-verdict gate runs share one cause:
  calibration readings step to 6.2 ms against 4.91 (1.26×, just past k = 1.25) at a window's opening
  and after the `lines` cells, and further after the main suites. The ruled answer is to re-run on a
  quieter machine, not to move k.

**Refreshing the ceiling** follows the table rule above. It is the operator's decision, recorded
here; the script is the only writer. It goes when Phase 448's acceptance removes the exception.

**Amended 2026-10-24 — the X64 table (Phase 461).** Until now only Arm64 had a table, so every x64
machine, CI's `ubuntu-latest` runner included, printed `NO TICK BASELINE TABLE for architecture X64`
and held each cell to the ratio arm alone. Phase 461 adds the x64 table. This is the first of the two
occasions above, and the phase that asked for it is the operator's decision to land it.

- **Where it was recorded.** No x64 machine is to hand, so the script got a hosted home:
  `.github/workflows/record-tick-baselines.yml`. It is triggered only by `workflow_dispatch`, so no
  push or pull request runs it. It runs `gates/record-tick-baselines.ps1` unchanged on `ubuntu-latest`,
  the label the ci clock leg runs on. It uploads the block, every leg's log and a description of the
  runner (image, `lscpu`, `dotnet --info`). On a hosted runner the script's provenance line also names
  the runner image (`ImageOS` / `ImageVersion`).
- **What was recorded.** Run 40110148786 used the script at `24bce4e` and recorded from `ccbddc7`,
  with the second ruling's method: the median over three full legs of each leg's best. On image
  `ubuntu24 20261012.334.1`, an AMD EPYC 7763 with 4 vCPUs, all three legs were clean, with no window
  discarded and calibration at 5.21 to 5.22 ms. That table is pasted beside Arm64 in
  `ScalingTests.tickBaselines` with its provenance.
- **Two tables, one rule.** Per cell, the X64 figure is 0.63 to 1.83 times the Arm64 one, with a
  median of 1.12. The two tables do not differ by a constant factor, which is why this entry keys
  tables by architecture rather than scaling one.
- **What CI now asserts.** On x64, every corpus cell is held to
  `max(1.6 x full, 1.5 x baseline) + floor`. Phase 459's declined shapes keep their own rule. Arm64 is
  unchanged.

**Re-recording the X64 table after a runner change.** A table is in calibration units, so a faster or
slower x64 machine reads the same figure. The routine weekly `ImageVersion` bump is therefore not, by
itself, a reason to re-record. Two kinds of change can move the table:

- the `ubuntu-latest` label moving to a new OS release or .NET runtime;
- hosted x64 runners moving to a different CPU family.

Either one is visible as a difference between the table's provenance line and the runner description
of a later run. The ci log's `Set up job` step names the image.

When the x64 clock leg goes red, or keeps reaching no verdict, after such a change, re-recording is
the operator's decision, recorded as a dated note here, and never a re-run to make a red leg green.
To re-record:

1. Dispatch the workflow (`gh workflow run record-tick-baselines.yml --ref main`) with the default
   `commit` (`ccbddc7`).
2. Download its `tick-baselines-x64` artefact.
3. Replace the X64 block with the artefact's `tick-baselines-X64.fs.txt` verbatim, with the run id
   in the provenance comment.

The same workflow serves a change of baseline tree: pass the new tree as `commit`. Arm64 is then
re-recorded from that tree locally, in the same change.

## 2026-10-12 — D21: a sort key that names no column is refused with `UnknownColumn`, in a `Sort` and in a `Window`'s ordering; the pinned "unknown columns skipped" is reversed

**Context.** `Sort` dropped a key that named no column: `resolveSortKeys` resolved the keys against
the step's schema and silently left the unknown ones out, which the comparator's documentation pinned
as "unknown columns skipped". Every other verb refuses an unknown column with a typed `EvalError`, and
the planner's totality verdict counted every literal-keyed `Sort` as total. So a misspelled sort key
returned the input order, `Ok`, with no sign the sort had not happened (Phase 412).

**Decision.** A key a `Sort` takes from a pipeline that the frame does not carry is refused with
`UnknownColumn(name, available)`, the error every other verb answers, at the FIRST such key in key
order, before anything is ordered. The same holds for a `Window`'s `OrderBy`, the other step whose
ordering keys come from the pipeline. The sites that held the old rule, and where each now stands:

- **the `Sort` step and the fused sort-then-limit** (the planner's top-n): both refuse, the fused
  form after the sort's slots resolve and before the limit's, so the first error is the one the two
  steps would have given unfused;
- **`Window`'s ordering** (`windowColumnOfWith`, and `windowColumnOverWith` for a caller holding an
  ordering already): refuses after the window's own `Of` checks;
- **the incremental seam** — its sort step merges under `compareResolved`, and its window steps order
  through the same functions: the sort step refuses before merging, and the window steps refuse
  through the window functions above, so a prime and a refresh refuse exactly where the reference does;
- **the planner's totality verdict** (`Plan.isTotal`): a `Sort` is total only when every key is a
  literal the schema carries;
- **`rowCompareBy`**, the exported comparator: a comparator has no error to answer, so it still drops
  an unknown key, and its documentation now says the reference refuses such a key before it ever
  compares; a caller reproducing the reference refuses first, as the reference does;
- **the conformance laws and the corpus**: no law pinned the skip, and no vector sorts or orders a
  window on a key its table lacks, so no vector moves; the corpus copy follows the release rule
  (D6 (4)) unchanged. The proof model treats `Sort` as one of the verbs it does not model, so it holds
  no copy of the rule.

**Why refuse rather than keep the skip.** The skip made a typo indistinguishable from a sort, and the
pipeline's author is the only one who could have noticed. A refusal is what every other verb already
does, so a consumer handling `UnknownColumn` handles this too.

**What it does not change.** A `Window`'s `PartitionBy` still partitions by the columns it can find
and drops a name the schema lacks; that is not a sort key and is a separate rule, left as it stands
here.

**Consequences.** A behaviour change to a public evaluator, versioned per the draft-slot rule
(`STABILITY.md` `0.39.0`); no public signature moves.

## 2026-10-12 — D20: a join's colliding right column takes the first free suffix against the whole output, `x_right`, `x_right_2`, …; a refusal was rejected

**Context.** A combining join suffixed a right column whose name collides with a left one by appending
`_right`, checked against the LEFT names only. A left carrying `x` and `x_right` joined to a right
carrying `x` therefore emitted two columns named `x_right`; so did a right carrying both `x` and its
own `x_right`. The evaluator reads a table's columns by name, so when such a table re-enters it — a
pushdown boundary, a cache, a checkpoint, a second query over the result — one of the two is read as
the other with no error anywhere (Phase 411).

**Decision.** The colliding right column takes `_right`, or `_right_2`, `_right_3`, … — the FIRST
suffix free against every name the output carries: every left name, every right name (a right name
that collides with no left name keeps its own name, so it is taken before any suffix is chosen), and
every suffix an earlier colliding right column took, decided in right column order. It is one
function, `DataFrame.joinRightNames`, which the evaluator and `SchemaWalk` both read. Over an open
right source the walk no longer names a colliding right column, because the right's unnamed columns
decide its suffix.

**Why the suffix and not a refusal.** A typed refusal naming the colliding column was the other
candidate. It would make a join's success depend on its operands' names, so a pipeline that runs today
over one table would refuse over a table that merely gained a column, and the static walk would have to
carry a new refusal verdict for a step that has always been total. The unique suffix keeps the join
total and changes nothing for any join whose output was already unique — every join the corpus vectors
record, and every one a consumer reading SQL-style output has met — while the case that changes was
already wrong. **What each costs a consumer reading SQL-style output:** under the suffix, a consumer
of a join whose names were already unique sees nothing move, and one that read an output which repeated
`x_right` now finds two addressable columns — the colliding right column under the first free suffix,
so a by-name read of `x_right` may land on the other of the two than it did, which is exactly the read
that was ambiguous before; under a refusal, it would get an error where it got a table, and have to
rename an operand to get one back. Neither
consumer can be told apart from the table alone, which is why the rule is deterministic in column
order and recorded here rather than left to a host to reproduce.

**Consequences.** A behaviour change to a public evaluator, versioned per the draft-slot rule
(`STABILITY.md` `0.39.0`); `DataFrame.joinRightNames` is new and additive. `Conformance.schemaWalkLaws`
gains the law that a combining join adds no repeated name and that the walk names a built pair of
joins as the evaluator does. No corpus vector moves; the corpus copy follows the release rule
(D6 (4)). A `Window` whose `As` names an existing column still appends it, leaving the name twice
(Phase 112's walk pins that); that is a separate rule and this decision does not change it.

## 2026-10-10 — D18: three rulings on what Core `1.0.0`'s typed column leaves without an object — a non-canonical text cast to a temporal type is refused by name, a derived column spanning two unrelated types is refused by name, and the fused sort-then-limit bound is on work, not bytes

**Context.** Until Core `1.0.0` a `Column` was a `Cell list`, so a column could hold a cell of the
wrong type beside the right ones, and three evaluator semantics were stated for such columns. A typed
vector cannot hold them (Phase 417), so each statement lost its object in the Phase 423 raise. The
operator ruled on each on 2026-10-10; the rulings are recorded here because each is a behaviour a
consumer can observe, and each was decided rather than fallen into.

**1. `Cast(date, x)` and `Cast(timestamp, x)` of text that is not the canonical form are REFUSED BY
NAME.** `castCell` used to answer `Date s` / `Timestamp s` for any string `s` and let the column carry
it; a `Date "nonsense"` cell is one Core's `Column.ofCells` refuses since its Phase 422, so the frame
could not hand such a column back as a `Table`. The cast now refuses with a `TypeError` naming the
cell, the target type and the text — `cannot cast 'x' to date: not a canonical YYYY-MM-DD` and
`cannot cast 'x' to timestamp: not a canonical instant of its unit` — at the row, the same way a
string that is not a number refuses `Cast(int, …)`. A canonical text casts as before. The alternative,
answering `Null` for the unparseable text, was rejected: a cast that silently drops a value is the
defect the typed refusals exist to prevent.

**2. A derived column — a `Derive`, or an `Unpivot`'s value column — whose cells span two types no
column type holds together is REFUSED BY NAME** (`typesBesideEachOther`: "derived column 'x' holds a
string beside a int, which no column type holds together: cast one to the other's type first"), on
D5's precedent for a float beside a decimal. Phase 321 typed such a column by its earlier cell and
carried the others as they were, which only a cell list could do. The static typer still decides where
it can (D5); the refusal is the cells' half of the rule, reached where the typer deferred to them. The
`Pipeline.fst` model's list column has no such refusal, so the oracle bridge classes it as outside the
model's domain rather than as a disagreement (`ProofOracleTests`). *The same rule reaches `Union`*,
the one other verb that puts two columns' cells into one: a column of the union takes the join of the
two sides' types where one exists (an int column over a float one is a float column) and is refused by
the same name where none does — the cell list used to carry the right side's cells under the left
side's type, a column no `1.0.0` table can hold.

**3. `PlanTests`' fused `Sort > Limit` bound is on WORK, not on bytes.** Phase 282 held the fused
top-n to a quarter of the full sort's allocation, and that held only because the `Table` boundary's
conversion dominated the full sort's cost. With the boundary a view (Phase 423), the full sort at
20,000 rows allocates LESS than the fused form (1.49 MB against 2.31 MB) while the clock still favours
the fused plan by an order of magnitude (1.3 ms against 18 ms). The claim was always about work — the
top-n keeps ten rows in order where the sort orders them all — so the case now counts the rows each
form hands to an ordering kernel (`SortFinite` / `SortPositions`, through a counting `KernelSet`) and
bounds that; the allocation figures are printed beside it and not gated.

## 2026-10-10 — D17: the substrate pin is raised to Core's `1.0.0` DRAFT through the local feed, with the public CI accepted RED until that version is published; the frame borrows and adopts at the `Table` boundary; and four choices the raise forced

**Decision (operator ruling 2026-10-10, Phase 423).** `FuaranCoreVersion` is `1.0.0`, a version the
public registry does not yet serve: Core's `1.0.0` is a draft slot, packed into the workspace's local
feed, which this repository's `nuget.config` already consults first. The compute strand is the first
consumer of the typed column (Core Phases 417, 418, 420, 421, 422, 426, 427), and the operator chose
to raise it against the draft rather than wait for the release, so that the release ships with a
consumer that has absorbed it.

**1. The accepted state: this repository's GitHub CI is RED from this change until Core `1.0.0` is
published.** CI restores from nuget.org and cannot see an unreleased version, so its restore fails.
This is accepted and dated: no workaround is added (no `nuget.config` change pointing CI at another
source, no conditional pin), because a workaround would make the public repository build against
packages a reader outside the workspace cannot obtain, which is the state the version-pinning mandate
forbids. The red clears when Core tags `v1.0.0`; the gate (`verify.ps1`) is green on the machine that
holds the feed, and that run is the one this change cites.

**Amended 2026-10-12 (fuaran-core Phase 444): the draft is released as `0.37.0`, so the red ends at
`v0.37.0`.** The operator ruled that Core is not published as `1.0.0`; the draft's contents, the same
packages and the same public surface this repository was raised against, are released as the minor
`0.37.0` (Core's DECISIONS.md D147). `FuaranCoreVersion` is `0.37.0`, packed into the local feed from
the renamed slot, and the accepted red now ENDS when Core tags `v0.37.0` and nuget.org serves every
`Fuaran.Core.*` package this file pins at that version; nothing else about the accepted state moves. The
`1.0.0` this entry and D18 name is that slot.

**2. The boundary converts nothing.** `Frame.ofTable` BORROWS each column's storage through
`Vector.Unsafe.borrow` (a slice is copied, since the frame indexes from zero; an `AllValid` column has a
mask built) and `Frame.toTable` ADOPTS the frame's arrays through `Vector.adopt`. The code that
converted — `Frame.Origins`, `Frame.unpackFallback`, the `Chunked.Cells` memo, `Vec.packList`,
`InternedCells` — goes. The no-write rule `Vector.Unsafe` states is one this assembly already obeyed
internally: no verb writes into an array it did not allocate.

**3. Four choices the raise forced, each the smallest that keeps a stated behaviour.**

- *Unknown-tag refusals are `MalformedShape`, not `UnknownType`.* Core `1.0.0` typed `UnknownType`
  to name column types only; the compute codecs' closed vocabularies (verbs, functions, modes) refuse
  an outside tag as `MalformedShape "unknown tag '<got>' (expected one of: …)"`, and the suite reads
  the pair back through its `UnknownTag` pattern.
- *A projection renames a field KEEPING its metadata; a derived, aggregate or pivot column is a fresh
  field with none.* `Schema = Field list` carries a unit, a label and a description per field (Core
  Phase 427). A `Project` that renames a column is the same column under another name, so the field's
  metadata travels (`Fields.rename`); a column the evaluator computes has no provenance a unit could
  be read from, so it is `Field.create name type`.
- *`Column` and `Table` are equatable by their cells and no longer comparable.* `ColumnData` is
  `NoComparison` in Core; nothing in this repository ordered tables, and the two places that compared
  columns for a diff (`ColumnOps.toOps`, `Delta`) compare CELLS, under the cell's own equality, which
  tells a NaN from itself where the vector identity does not.
- *A column that disagrees with its schema entry is read boxed, and one whose cells span two types
  cannot leave the frame.* Core `1.0.0`'s `Table` can hold a column of another type than its field
  says (the frame reads it as cells, as it always did), but never a mixed one: `Frame.toTable` refuses
  such a column by name (`cannot be held by its type`), and the suite's generators stop drawing the
  inputs that produced one (a mistyped column merged with a typed one), because the reference has no
  answer for them. The suite's column builder (`KitColumn.create`) draws a cell no column holds beside
  the others ABSENT, and documents it.

**4. The version.** The public surface of `Fuaran.Compute.DataFrame` and `Fuaran.Compute.PipelineQuery`
moves class `retype` (every signature that carried a schema), so the `0.38.1` release is followed by
`0.39.0`, per the draft-slot rule. The law vectors re-emitted from this tree move ONE vector's bytes:
`column-op-6-setColumn-decimal` records `Decimal "12"` where it recorded `Int 12`, because the decimal
column now holds the int as the decimal of its value (the representation Core's Phase 417 stamped into
its own `decimal-laws.json`). The shared corpus copy follows the release (D6).

## 2026-10-08 — D16: every float the evaluator computes leaves it with one NaN, `Double.NaN`'s bits, and the gate runs the main suite in Release as well as Debug

**Decision.** One rule, evaluator-wide: **every float the evaluator emits carries the canonical NaN.**
A NaN answer is `Double.NaN`'s bit pattern, whatever sign or payload the arithmetic produced; every
other value is untouched. The rule does not loosen the laws; it makes them keep their bit-for-bit
claim. `canonicalNaN` / `canonicalNaNCell` are applied where an answer LEAVES the evaluator, never per
function, so a function added later cannot forget:

- *expressions* — `evalResolved`, the row evaluator's one exit (its recursion is the private
  `evalNode`), which `evalExprInRow` and the incremental seam's re-evaluated cells read;
  `runCompiled`; and a compiled `Derive`'s float root and boxed root, both where the sequential
  member puts them and where a `MorselHandOff` runner puts them;
- *aggregates* — `aggCells` (the deferred path of every `GroupBy` and `Pivot` aggregate, and the
  whole of the public `aggregateCells`), `GroupAgg.Stream.TryCell` (the streamed answer, which the
  incremental seam reads directly) and `GroupAgg.Output.Float` (every float a `GroupBy` or `Pivot`
  writes, unboxed or as a cell);
- *windows* — the window column where `windowColumnOverWith` builds it, the one place every window
  answer passes: the frame form, the row form and the incremental seam's resumed runs.

The batch and the incremental paths therefore go through the SAME exits — the refresh re-evaluates a
derived cell through `evalResolved`, recomputes a group through `TryCell` and `aggregateCells`, and
resumes a window through `windowColumnOverWith` — so they cannot disagree on a NaN's bits. The steps
that compute nothing (`Project`, `Filter`, `Sort`, `Limit`, `Distinct`, the joins and set verbs,
`Unpivot`) hand on the caller's cells as they were given, so a source value keeps the caller's bits
until something computes over it; the moment an expression, an aggregate or a window reads it, the
answer is canonical (a `Derive` of `Col x` over a sign-clear NaN answers `Double.NaN`).

**Why canonicalise rather than compare NaNs as equal.** Bit-determinism of an answer across Debug,
Release and Fable builds, and across hosts, is a property other things stand on: the .NET half of a
parity vector is compared by bits, and any content addressing over raw values (rather than over the
canonical wire) hashes bits — a digest over a derived column included. A law loosened to "any NaN
equals any NaN" would stay green while those consumers saw two answers to one question. Making the
evaluator emit one NaN keeps the law's claim the strong one, and costs a test per float on the way
out.

**The evidence (checked 2026-10-08, Arm64, .NET 10).** The window law failed in Release and passed in
Debug on the same tree, latent since Phase 324 (`fdea31d`), because the main suite ran in Debug only.
A probe over the law's 3,000 seeds found exactly two disagreeing cases, both `CumulSum` (seeds 97 and
394), and it was the PRODUCT that moved between builds, not the reference: the reference answered
`0x7FF8000000000000` in both builds; the scan answered the same in Debug and `0xFFF8000000000000` in
Release, at the rows where the running total, already a NaN from `-inf + +inf` (Arm64's default NaN,
sign clear), met a source `NaN` cell (`Double.NaN`, sign set). Adding two NaNs answers one operand's
payload, and the Release JIT commuted the addition. Every other NaN in those 3,000 cases agreed. The
go-red vectors — a `CumulSum` over `-inf`, `+inf`, then NaNs of both signs, through the frame path and
the row form; a `GroupBy` of seven aggregates over the same; a `Derive` of `-inf + +inf`, `0 * inf`,
and a sign-clear and a sign-set source NaN carried and added both ways round, through the compiled
frame path, the row form and the incremental refresh — are red on the old code in Release and in
Debug on this host, and green on the new code in both. The compiled-versus-row-form law now compares
a float answer by its bits as well as by its token.

**The audit of the strand for the same class** (a float written to the wire, hashed, or compared by
bits, where a NaN can arise):

1. *The wire* (`DataFrameCodec`, `ColumnOps`, the incremental state's wire form, the law vectors) —
   a NaN is the token `NaN` (the substrate's `nonFiniteToken`), and decodes to `Double.NaN`; its bits
   never reach a byte. Nothing to fix.
2. *Hashes and keys* (`cellToken`, `CellKey.hashCell`, `RowHash`'s float codes, the sort's order
   codes, `Delta`'s cell equality, the incremental state's digest) — every one maps every NaN to one
   value before it hashes or compares. Nothing to fix.
3. *Bit comparisons* — only the suite's laws compare floats by bits: the window law, the streamed-
   aggregate law, the `GroupBy` and `Pivot` stream laws, the incremental `GroupBy` and window laws,
   and (since this decision) the compiled-expression law. All of them read answers that now leave
   through the exits above. Fixed by this decision.
4. *Derived columns* — `Derive`'s arithmetic makes a NaN (`inf - inf`, `0 * inf`) and carries a source
   NaN. Nothing observed those bits before this decision, but a later content address or digest over
   a derived column would have, so the rule covers expressions too (the exits above), and the
   incremental refresh reaches them through the same `evalResolved` rather than a copy.

**Fable.** JavaScript has one observable NaN, so `canonicalNaN` is the identity there and the rule
holds by construction; the helper costs a comparison per float.

**The gate.** `verify.ps1 -Configuration Debug|Release`, Debug by default, builds the solution and
runs the whole main suite in the named configuration, following the substrate's shape; `ci.yml`
runs a Debug and a Release leg, and `publish-packages.yml` verifies in Release and packs `--no-build`
from that output, so the assemblies verified are the assemblies shipped. The clock leg runs Release
under either configuration, as the 2026-09-28 operator decision has it (a bound on the clock is a
claim about the code consumers run); under `-Configuration Release` it reuses the gate's own build.
That choice departs from the phase's literal wording (the clock leg "in the named configuration") and
was ruled on and accepted on 2026-10-08.

## 2026-10-06 — D15: the pipeline-query registry's lifecycle verbs land, each delegating to the substrate's on the declarations first; the bodies move only on success

**Decision.** `PipelineQueryRegistry` gains `unregister`, `replace`, `restrict` and `union`,
closing D12's deferral. Each calls the substrate's `QueryRegistry` verb of the same name on the
declarations; a refusal is returned as `QueryRefused`, and the bodies change only when the
declarations did. `replace` then holds the new pair to `PipelineQuery.check`, as `register`
does, so a disagreeing replacement is refused by name and leaves the registry as it was.

**Premises (checked 2026-10-06).** `FuaranCoreVersion` reads `0.35.1`, tagged on the substrate's
remote. The four verbs have in `v0.35.1` the signatures Phase 316 landed (`unregister`, `replace`
and `union` answer `Result<QueryRegistry, QueryError>`; `restrict` answers a `QueryRegistry`).
The registry is still the private pair of declarations and bodies. All three held.

**Held by the laws.** `PipelineQueryConformance` gains a lifecycle family: `unregister` undoes
`register` for a fresh id, `restrict` never widens, `union` refuses a shared id and is
associative, and after any drawn sequence of the verbs the ids of `enumerate` equal the ids of
`declarations`.

## 2026-10-06 — D14: the worker pool lands on a byte-mask frame, opt-in and reached only asynchronously; the masks cost the whole evaluator some collection under node, recorded for the operator

**Decision.** fuaran-core#376 completes D10's yes on the seam D13 landed. Figures in
[`benchmarks/results/2026-10-06-i7-9700-phase-376.md`](benchmarks/results/2026-10-06-i7-9700-phase-376.md).

**1. The frame's masks are bytes under Fable.** A validity mask and a `Bools` vector's values are a
`Mask`: `bool[]` on .NET, as they always were (every accessor is the plain index, so the code there is
the code it was), and `byte[]` — a `Uint8Array` — under Fable, read and written only through the
`Mask` module. A typed vector's carrier and its mask share one buffer. A worker's rebuild of a hand-off
now reads the frame's own arrays (0.02 to 0.04 ms at any size, where 375's copied every mask, 8.7 to
9.7 ms a million rows), and the hand-off carries a `Mask` where it carried bytes.

**2. Shared memory is the opt-in's, not the frame's default.** Until a host opts in, the frame
allocates as before; from `WorkerPool.optIn` on, on a realm with `SharedArrayBuffer`, its int and float
values and masks are allocated over shared memory, and a hand-off copies a vector allocated earlier
into shared memory once, at plan (2 to 4 ms a million rows). A structured clone hands a shared array
to a worker by reference.

**3. One entry point reaches the pool.** `DataFrame.evalToPreparedAsync` folds the planned pipeline
through the same moves as `evalToPrepared` (`foldHead`, lifted out of the fold) and offers each
`Filter` and `Derive` of at least `WorkerPool.PoolRows` rows to the pool; `evalToPrepared` and the
synchronous kernel pair never reach it. From the page's thread a caller awaits it (the pool never uses
`Atomics.wait`, which the page refuses); a dedicated worker hosting the evaluator awaits it the same
way and starts the pool from there.

**4. The pool.** The host hands `optIn` a way to start a worker (`MorselWorker`: post, listen, stop)
whose script calls `WorkerPool.serve`. The pool starts lazily on the first step it would take, uses a
worker from the step after it reports ready, and stays warm until `optOut`. One job at a time: the
caller posts the hand-off to the ready workers, then drains morsels beside them, every runner claiming
the next morsel from one shared counter; results land in each morsel's own slots and the caller
finishes in morsel order. A failed morsel is flagged and no runner claims past it; the caller re-runs
the first failed morsel for its error (375's rule). A worker that faults, or whose error event the
host forwards, is retired and the step answered by the sequential member. A second evaluation that
finds the pool busy runs its morsels on its own thread through the same loop.

**5. The floor is measured.** `PoolRows` is 32,768 rows: at 10,000 rows the pool was faster in 7 of 18
cells (node, Edge's page thread, Edge's dedicated worker; 2, 4, 8 threads) by at most 14 per cent and
more than twice as slow at eight threads on the page's thread; at 20,000 faster in 15 of 18; at 50,000
in all. At 100,000 rows and up the evaluator runs 3.4 to 5.2 times faster under node, 3.5 to 4.7 times
from a dedicated worker and 3.2 to 4.0 times from the page's thread.

**6. The laws, and the gate's node leg.** `PooledLaw` holds every pooled step byte-identical to
`Kernels.oneThread`, and `evalToPreparedAsync` to `evalToPrepared` over whole pipelines, over the
transform vectors and the corpus, with the caller draining and with the workers alone; a shifted
boundary and out-of-order concatenation go red; without isolation or without `SharedArrayBuffer` the
opt-in is refused and the answers are the sequential member's. The suite runs it on .NET over the
thread pool and, through Fable 5.0.0 (now pinned in the tool manifest) and `node`, over
`worker_threads`: the node leg is a case of the gate's own suite, and it fails rather than skips without
Fable or node.

**7. What the masks cost — for the operator.** The step-level sequential figures 375 recorded do not
regress on .NET (identical code; within noise) and under node improve or hold, except the `filter`
(+3.6 per cent a million rows, inside the sittings' spread; +6 per cent at 100,000, outside it). The
corpus harness under node reads 19 of 71 cases faster by 5 per cent or more, the prepared chain at
100,000 rows by 29 to 31 per cent; but the sheet's whole evaluator at 100,000 rows — `Table` in, two
steps, `Table` out — is 5 to 17 per cent slower (by method), consistently, though each of its phases
measured apart is faster. The cause is collection: a call allocates about 3.1 MB of typed-array backing
stores where it allocated 1.6 MB (the masks and the derived carriers were heap arrays), and the
engine collects for them; the same pipeline with its masks drawn from one reused buffer runs faster
than before. That is the price of masks a worker can read in place, which this phase exists to pay.
A ruling the operator may take: keep it (the recorded state), or look for a host-side buffer reuse
that would recover it (no reuse ships: a buffer outliving its vector retains memory, and the lifetime
is not the evaluator's to know). Nothing here is a correctness question.

**Ruled 2026-10-21: keep it.** The collection cost under node is the worker path's price, accepted
as recorded. No host-side buffer reuse is pursued: the lifetime it would need is not the evaluator's
to know, and the regression is confined to the whole-evaluator figure at one size on one host. A
consumer that measures it on a real workload reopens the question with that measurement.

## 2026-10-06 — D13: the worker path's first piece is the step as data, compiled by one path; the frame's byte masks come before the pool, because every worker would otherwise copy them

**Decision.** fuaran-core#375 builds D10's yes in order, and the first piece is the seam a worker
compiles from: `MorselHandOff`, a row-local step (`Filter` or `Derive`) as plain data. It carries the
step and its environment on the canonical wire (a one-step pipeline; each bound name as the `lit`
expression of its cell), the frame's schema as names and type tags, the vectors the step reads (int
and float values as they are, bool values and validity masks as bytes), the logical rows, the morsel
geometry and range, and the result slots: a `Filter`'s kept rows from each morsel's own offset and its
count, a `Derive`'s typed carrier and mask by physical row, and a failed flag per morsel. Strings,
numbers and arrays of numbers only, so a structured clone carries it and shared memory could back
every array in it. It is internal: no runner ships yet.

**1. One compile path.** A runner rebuilds the frame from the data and resolves and compiles the step
with the evaluator's own `resolveExpr` / `compileExpr`, then runs each morsel through the evaluator's
own row loops, `filterMorsel` and `deriveMorsel`. Those loops were lifted out of `evalFilter` and
`evalDerivedColumn`, which now run them too, so the sequential member and a runner cannot disagree on
what a row keeps, what it writes or which error it raises. `deriveMorsel` is inlined at both sites so
neither pays an indirect call a row for its writes.

**2. Errors do not cross.** A runner records only that a morsel failed. The caller re-runs the first
failed morsel, in morsel order, on its own thread through the same loop, and answers that error, so a
runner that ran the morsels in another order still answers the first error in row order, and no
`EvalError` is ever serialised.

**3. What is handed off.** A step whose wire form decodes back to itself (and whose environment does),
whose every read column is an int, float or bool vector, that is not a comparison-kernel filter (Phase
270's bitmap answers that one faster than any tree), and, for a `Derive`, whose root is typed int,
float or bool. Every other step runs as it always ran. A string, decimal or boxed column cannot be
shared, so it is never carried.

**4. The laws.** `MorselHandOffLaw` (Fable-clean) holds every handed-off step byte-identical to
`Kernels.oneThread`, refusals included, over the transform vectors at 1, 2, 3 and 8,192 rows a morsel
and over the corpus's row-local pipelines at 20,000 rows, under runners that run the morsels in order,
in reverse, and (in the suite) one a task on the thread pool. A runner whose morsels after the first
start a row late, and one that hands kept rows back in swapped morsel pairs, go red. Under node the
same law passes with every morsel's record and results sent through `structuredClone`, which is what
`postMessage` does to them (`benchmarks/results/2026-10-06-i7-9700-phase-375.md`).

**5. Why the pool waits for the masks.** On one thread a hand-off's morsels cost what the sequential
member costs, under node and on .NET. Its fixed costs are the copies: under node, at a million rows,
4.9 to 8.0 ms to plan and 8.7 to 9.7 ms for EACH runner to rebuild, the second because the frame's
masks are plain `bool[]` (JavaScript `Array`s) and a runner must turn the hand-off's bytes back into
them. Seven workers would copy about 60 ms between them, half the sequential `filter`. With the masks
as byte arrays in the frame (D10 item 4a; #375's third task) a rebuild copies nothing, so that change
goes first and the pool is built on it rather than paying a copy it would then have to remove.

**What did not land in fuaran-core#375, and why.** The frame's vectors and masks in shared memory under
Fable: masks become byte arrays across `Frame.fs` and every verb that reads them, a representation
change on one host whose laws run only under node, which no gate here runs. The asynchronous entry
point, the pool and runner, the opt-in detection and fallback, and the browser measurements: each
stands on shared vectors, and without them a pool would copy every mask into every worker (item 5).
The pooled-under-node comparison of the laws waits for the pool. Nothing public moved.

**What it bought already.** A `Filter`'s kept rows now land in one buffer, each morsel from its own
offset, where each morsel filled a growable list before: the sequential `filter` is 1.6 to 1.8 times
faster under node at 100,000 and 1,000,000 rows, and .NET and the `derive` are unchanged or faster.

## 2026-10-05 — D12: the pipeline-query registry's lifecycle verbs wait for a released substrate that carries them; the pin names `0.34.0`, which does not

**Decision.** `PipelineQueryRegistry` does not gain `unregister`, `replace`, `restrict` and `union`
yet. They are to delegate to the substrate's `QueryRegistry` verbs of the same names, and those
verbs are not in the substrate this repository pins.

**Evidence (checked 2026-10-05).** `FuaranCoreVersion` reads `0.34.0`. The substrate's `v0.34.0`
tag has no `unregister`, `replace`, `restrict` or `union` in `Fuaran.Core.Query`; they landed with
the substrate's Phase 316 on its `0.35.0` draft. That version is not tagged on the substrate's
remote, so the public registry does not serve it, and only a machine-local feed holds a `0.35.0`.
Raising the pin to it would restore here and nowhere else, which the pin discipline refuses.

**What stays true.** The registry's representation is still the private pair of declarations and
bodies, so the delegation design the phase describes is unchanged. The work is deferred, not
refuted: when `0.35.0` is released and the pin raised, the four verbs, their laws and the
`enumerate`-equals-`declarations` property go in as one additive change.

## 2026-10-05 — D11: the consumer sweep to the `Fuaran.Compute.*` ids stops where a consumer's move is a cohort or cycle decision, not a rename

**Recorded (fuaran-core#361), carrying D4's second stage.** D4 renamed the packages and left the
consumers to a sweep. On 2026-10-05 every consumer but two already names the new ids, at `0.37.0`,
which nuget.org serves under all four. The two still name an old id, and neither is a sweep's to move:

**1. A consumer behind the substrate floor moves with its substrate, and that can be a cohort act.**
The new ids begin at `0.36.0`, whose substrate floor is `0.33.0` (`0.37.0`'s is `0.34.0`). A consumer
pinned at `0.34.0` of the old id over a `0.32.0` substrate takes the rename only together with the
substrate raise and that raise's own source breaks. Done in isolation the raise was measured green in
the consumer itself, and red one hop further out: a repository that builds it by project reference, with
its own substrate pins further behind, no longer compiles, because a type it matches on (a policy
denial's payload) changed shape in the raise. So that consumer moves when the repositories built
against it raise their substrate together, which is a cohort decision for the operator; the verified
change is held on a branch for it, and no pin moved.

**2. A consumer that pins a version to MEASURE it moves by its own cycle decision.** A repository that
holds one substrate version fixed so its measurements can be repeated (and is deliberately outside the
version cohort) cannot take a compute id whose substrate floor is above its cycle pin without moving
that pin. Moving it starts a new measurement cycle, which that repository's own records reserve to a
deliberate decision; a sweep does not make it.

**3. What the producer side changes.** Nothing in the packages. The two facts a consumer needs before
it moves — the substrate floor rides with the rename, and an old id carried transitively by another
package survives a consumer's own pin move — are added to `0.36.0`'s "What a consumer changes" in
`STABILITY.md`.

## 2026-10-05 — D10: whether the browser host takes a worker path that needs cross-origin isolation is the operator's ruling; the operator ruled YES (2026-10-05): an opt-in worker path, fuaran-core#375

**Measured (fuaran-core#346), not yet decided.** Phase 346 opens with an operator ruling — whether
the browser host gets a Web Worker morsel runner over `SharedArrayBuffer`, which exists only on a
page served with `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy:
require-corp`, and if so whether that is opt-in per host (the sequential member when the page is not
isolated) or required. A dispatched session cannot take that ruling (the side's doctrine escalates it),
so this entry records what the ruling needs and stops. Nothing in the evaluator moved. The figures are
in [`benchmarks/results/2026-10-05-i7-9700-phase-346.md`](benchmarks/results/2026-10-05-i7-9700-phase-346.md),
with the harness beside them; native x64 (i7-9700), node v25.9.0 and Edge 154 only.

**1. What threads buy, at best.** The prototype runs the two morsel steps Layer 6 times (`a + b > 500`
and the sheet's two `Derive`s) as hand loops over typed arrays already in shared memory, on a warm
pool, the caller draining morsels beside the workers; every pooled answer was byte-identical to the
sequential one (72 of 72). At 10,000 rows nothing is bought. At 100,000 and 1,000,000 rows, eight
threads run the steps 2.8 to 6.3 times faster under node and 3.6 to 4.8 times faster in Edge when the
caller is a dedicated worker; with the caller on the page's thread Edge reaches only 2.5 to 3.1 times,
and eight threads are slower than four in three of its four large cells.

**2. The sequential member is the larger lever.** The shipped evaluator under Fable on node runs the
same two steps at 251 and 419 ms a million rows — 42 to 50 times the prototype's single-threaded loop
over the same arrays (5.98 and 8.31 ms), and 39 to 44 times the .NET evaluator. Eight workers could
divide that by at most eight; the per-row machinery of the compiled tree under Fable (closures, the
error and null slots, the kept-row list, the boxed root) is a factor of forty, with no deployment
constraint attached to removing any of it.

**3. The costs a worker path adds.** A pool of the prototype's loops starts in 11 to 42 ms; a worker
that compiles a step from data must import the evaluator, which takes 144 ms for one worker and 284 ms
for seven under node. If the frame's vectors are not already in shared memory, copying a million rows
of inputs in costs 0.8 to 3.0 ms and a `Derive`'s outputs out 2.3 to 3.4 ms — 60 per cent of the pooled
`filter` and three times the pooled `derive`. Reading through shared buffers costs the sequential step
between 1 per cent less and 7 per cent more.

**4. The shard's premises, checked.** (a) Under Fable `int[]` and `float[]` are typed arrays, but every
validity mask and `Bools` vector is a plain `Array`, and `Strs` / `Decs` / `Cells` hold strings and
objects: only int and float VALUES could be shared today. (b) The runner cannot be "the JavaScript member
of the kernel pair": `RunMorsels`'s body is a closure over the frame, the compiled tree and the caller's
result arrays, which cannot cross into a worker, and the pair is synchronous, while the page's thread
refuses `Atomics.wait` (`TypeError`, measured) — so a synchronous member could wait only with the whole
evaluator running in a dedicated worker, and on the page's thread only an asynchronous evaluation entry
point could. (c) Without the two headers `SharedArrayBuffer` is undefined, so the opt-in shape's
fallback is exactly today's sequential member.

**5. What a yes touches.** Not `Kernels.fs` alone: a step-as-data seam the worker compiles from (the
pipeline is already data; the frame is not), frame vectors allocated in shared memory under Fable with
masks as byte arrays, the result hand-back, and either an evaluator hosted in a dedicated worker (the
host's job, in `fuaran-live` and every consumer that embeds the engine) or an asynchronous evaluation
entry point on the JavaScript host. Opt-in per host is the only shape that keeps a non-isolated page
working, because such a page has no shared memory at all. Every consumer that opts in takes COOP/COEP,
which constrains the cross-origin content it can embed. The laws are cheap: row-local morsels writing
disjoint rows concatenated in morsel order are byte-identical by construction, and were measured so.

**6. What a no keeps.** The browser host runs the sequential member on one thread, no page needs
isolation headers, and the measured order of levers stands: the Fable per-row cost (item 2) before
threads.

**The ruling (2026-10-05): YES.** The operator ruled to build the worker interface. The shape is opt-in per
host, the only one item 5 found that keeps a non-isolated page working: a host that does not opt in, or a page
without COOP/COEP, keeps the sequential member byte for byte. The runner is the successor phase
fuaran-core#375, sized by item 5: the step as data, frame vectors in shared memory, and an asynchronous
evaluation entry point. Item 2's per-row lever under Fable stands beside it and is not displaced by it.

## 2026-10-04 — D9: whether float `Sum` becomes exactly rounded is the operator's ruling; the operator ruled NO (2026-10-05): the fold stays the default

**Measured (fuaran-core#345); ruled NO on 2026-10-05 (below).** Phase 345 opens with an operator ruling — whether the
float `Sum`, and through it `Mean`, `StdDev`, `CumulSum`, `RollingSum` and `RollingMean`, moves from
the left-to-right fold (D3; `Kernels.fs` header) to an exactly-rounded sum, whose answer is the double
nearest the true sum and so cannot depend on the order or the partitioning of the additions. A
dispatched session cannot take that ruling (the side's doctrine escalates it), so this entry records
what the ruling needs and stops. Nothing in the evaluator moved. The figures are in
[`benchmarks/results/2026-10-04-i7-9700-phase-345.md`](benchmarks/results/2026-10-04-i7-9700-phase-345.md),
with the harness beside them; native x64 (i7-9700) and node only — the Arm64 leg was not run.

**1. The cost, single-threaded.** The fold costs 1.3 ns a value on both hosts. CPython's `math.fsum`
algorithm (Shewchuk's partials, correctly rounded collapse) costs 3.4 to 3.9 times the fold where
every partial sum is exact (whole quarters — the benchmark corpus's shape), 5.5 to 8 times at
ordinary dynamic range (uniform in `[0, 1)`) and 25 to 34 times at wide range (mixed signs across
sixteen decades), on .NET and node alike; the multiple is set by the live partial count (1, 2, 7 to
9), which the input's spread of magnitudes decides, not `n`. Against D7's whole streamed `GroupBy`
(3.94 ms, a million rows, ten keys) that is 7 ms more at ordinary range (a 2.8x slower step) and 42 ms
at wide range. Split into eight ranges on the pool it is 1.9x the fold at a million ordinary values,
below the fold where the inputs are exact, 8.5x at wide range.

**2. The invariance and the cross-host bytes hold.** On every input the exact sum of five shuffles,
of 3 / 5 / 8 / 64 / 1,000-way partitions (each range's partials fed through the same algorithm) and of
the eight-thread run has the sequential exact sum's bit pattern (45 checks); the shuffled fold differs
from the in-order fold on every non-exact input. `.NET`, node and CPython's own `math.fsum` return the
same 64 bits on all nine inputs. The parallel member the shard asks for is therefore sound by
construction: partials are a range's exact sum, and merging partial lists is the same algorithm.

**3. No transform law vector changes bytes.** The corpus's three float summations (`mean` over `w` in
`transform-3` and `transform-11`) are over one or two exactly-summable members; the `sum`s are int or
decimal. The fold and the exact sum agree on every vector, so the corpus cannot distinguish them — a
yes needs new vectors with inexact members (three or more `0.1`-class values) before any host is held
to the new sum.

**4. The twins, corrected from the shard.** Per D6, `fuaran-ts` is the only host running the
transform laws, and it still lacks the decimal strand (fuaran#2001) that blocks the corpus copy today;
`fuaran-rs` and `fuaran-go` carry evaluator twins with no laws leg; `fuaran-py` has no twin (and has
`math.fsum`). The algorithm is ~40 lines per language (`bench.mjs` is the TypeScript body).

**5. What a yes touches — wider than this repository.** The one aggregate semantics is
`Column.aggregate` in `fuaran-core` (`src/Fuaran.Core.Column/Column.fs`, `Sum` / `Mean` / `StdDev`
over `total` and `kept`), consumed here as a package: the exact sum lands THERE first, under a Core
cut, before this evaluator's streamed `MSumFloat` / `MMean` slots, the window's `CumulSum` seed and
running total and the rolling sums (`DataFrame.fs`) and the incremental seam's rescan rule
(`Incremental.fs`: "a float `Sum` is never maintained") can follow. Three consequences worth the
operator's eye: a `CumulSum` continued from a prior run must seed from the prior PARTIALS, not the
rounded float, or the continuation's bits differ from a fresh run's; Phase 306's finite-input overflow
refusal meets the algorithm's own intermediate overflow (CPython raises there); and an exactly-rounded
`Sum` becomes MAINTAINABLE under a delta (adding a member and adding a removed member's negation are
both exact over the partials), which the seam today rescans.

**6. What a no keeps.** D3's order, every byte today's vectors pin, the fold's cost, and the rule in
the `Kernels.fs` header that no float reduction is ever split — with D8's grouping-by-key and this
phase's eight-range figures as the measured price of that rule.

**The ruling — NO, taken by the operator on 2026-10-05.** The float `Sum` and every aggregate above
stay on D3's left-to-right fold; item 6 is what is kept, and no float reduction is split. The cost in
item 1 (a 2.8x slower `GroupBy` step at ordinary range, more at wide range) and the breaking change
in every non-exact answer are not paid for a property nothing today needs. An OPT-IN exactly-rounded
sum (a distinct aggregate beside `Sum`, landing in `Column.aggregate` first per item 5) is left to a
later Suggest-features candidate, not filed here. This entry and the figures are the phase; no
`STABILITY.md` entry is owed.

## 2026-10-04 — D8: the sort, the window and (since fuaran-core#371) the top-n run across threads; the grouping by key and the ranged join probe were measured and not kept

**Decided (fuaran-core#344), on measurement.** Native x64 only (i7-9700, 8 cores); the figures are
in [`benchmarks/results/2026-10-04-i7-9700-phase-344.md`](benchmarks/results/2026-10-04-i7-9700-phase-344.md).
The Arm64 leg was not run.

**1. Kept: three kernels in the pair.** `Partitions` (how many ranges a verb runs: one on the
portable member, one below `PartitionRows` = 32,768 on the native), `SortFinite` and `SortPositions`
(a merge sort, runs sorted on the pool and merged pairwise). The sort keys are distinct under a
total order — the packed keys end in the position, and the unpacked comparison ends in it — so any
correct sort answers the same array, and the portable member's sorts are the engine's own. Through
them: the sort, 2.0 times at 100,000 rows and 2.2 at a million, 4.7 where the keys do not pack; the
window's ordering through the sorts and its partitions scanned in chunks of whole partitions
across threads (1.6 to 3.9 times). No fold is ever split: a window partition's running total is one
sequential scan, so no float byte moves.

**2. Not kept: the grouping by key across threads.** D7 item 5 pointed here: partition by group so
that each thread owns whole groups and no table is merged. Built in the evaluator and measured, it
is 1.59 times faster at ten keys and a million rows and 1.16 at one key per ten rows, but 1.46 and
2.09 times SLOWER at 100,000 rows and slower still at 10,000, and it allocates 2.3 to 2.8 times as
much. Run on one thread it costs 22 ns a row against the sequential grouping's 10: each partition
reads its rows scattered across the whole vector, and the hashing and placing passes are work the
sequential path never does. A grouping that pays only above some size between 100,000 and a million
rows needs that crossover measured before a threshold can be set, so it is not shipped on a guess;
the prototype stays in the harness (`Partitioned.GroupByKey`) for whoever measures it.

**3. Held for the operator: the top-n a range of rows at a time.** Each range's heap on its own
thread, the candidates sorted under the same total order, answers the sequential heap's bytes and
is 2.8 to 4.4 times faster at 100,000 and a million rows. It is not shipped because the verify
gate's corpus tick case (`ScalingTests`, Phase 283) holds the incremental tick to 1.6 times the
full evaluation it replaces, and the full evaluation is what it speeds up: `filter > sort > limit`
at 100,000 rows read 1.75, 1.74 and 2.04 on the three attempts, against a sequential tick (diff and
refresh) of 14 to 16 ms. That case's own text makes a reading above the bound an operator decision
and forbids raising the bound to pass it, so the kernel waits on that decision: either the bound
is restated against the sequential evaluation (what the incremental seam replaces on every host),
or the tick gains threads of its own. The sort and the window, which the same case also times,
stayed within the bound (1.27 and 0.97 at 100,000 rows).

**Item 3 closed (fuaran-core#371) — operator ruling 2026-10-04, option A: the bound is restated
against the evaluation at one thread, it stays 1.6, and the parallel top-n ships.** What the tick
bound protects is that maintaining a result incrementally is not dearer than recomputing it on the
same footing; once the full evaluation ran on every core, the ratio measured the core count, not
the seam. Both tick clock cases (`ScalingTests`, Phases 274 and
283) now time the full evaluation on `oneThread` — the native member's vector kernels, its morsels
run in order on the caller's thread, never partitioned — and hold it equal to the reference answer
before timing it; the tick arm, `tickBound` (1.6) and `tickFloorMs` are unchanged. The ranged heap
from `fb25f9a` is back in the evaluator as it was, and the cross-member law that holds it ("over
frames thousands of rows long, ...") was checked to go red with one range's heap dropped. Figures
(Release, i7-9700, `benchmarks/results/2026-10-04-i7-9700-phase-371.md`): the corpus case's
`filter > sort > limit` cell at 100,000 rows read 1.54, 1.50 and 1.33 quiet and 1.31 under four
CPU burners against the parallel evaluation, and 0.63, 0.67 and 0.67 quiet and 0.66 loaded against
the one-thread evaluation; the worst cell of either case after the change was 1.36 (`inner join` at
1,000 rows, loaded). The top-n re-measured: 2.96 and 2.67 times at 100,000 rows, 4.33 and 3.78 at a
million (ten keys and one key per ten rows).

**4. Not kept: the join probe over ranges of left rows.** No consistent gain at either size (one
sitting, the machine under load): the probe is a small part of the join, whose cost is emitting
the matched pairs and gathering every output column.

**5. The shard's premise, checked: "boxing no longer dominates the grouping" held only at low
cardinality.** At one key per ten rows the grouping spent most of its time boxing the KEY cells
(a `Cell[]` per group, packed back into a vector): 20 of 32 ms at a million rows. The key column is
now read from each group's first row straight into the output vector (`DataFrame.keyColumn`), on
both members: 14.6 ms at a million rows, 1.23 ms at 100,000 (from 2.21). The window's partitioning
paid the same boxing for nothing and no longer does.

**Supporting change.** `windowOrder`, `windowOrderOf`, `windowColumnOf` and `windowColumnOver` keep
their signatures and run the portable member (the incremental seam's callers); the evaluator calls
their `...With` forms with the host kernels.

## 2026-10-03 — D7: the host-kernel verdicts are taken on a native JIT; Phase 270's are restated on Arm64 and x64, and two of them move

**Decided (fuaran-core#341), on measurement.** Phase 270 shipped the host kernels and removed an
order-preserving parallel `GroupBy`. Every timing behind it came from the x64 build running under
emulation on an Arm64 machine. Emulation penalises the intrinsics and the thread scheduling that were
being judged. The benchmark harness now refuses a run whose process architecture is not the
machine's (`Program.main`), and Layer 6 has committed suites (`benchmarks/.../Layer6.fs`). The Arm64
figures are in [`benchmarks/results/2026-10-03-snapdragon-x1e80100-phase-341.md`](benchmarks/results/2026-10-03-snapdragon-x1e80100-phase-341.md).
The x64 figures, taken natively on a 4-core i7-8650U with the machine quiet, are in
[`benchmarks/results/2026-10-03-i7-8650u-phase-341.md`](benchmarks/results/2026-10-03-i7-8650u-phase-341.md).
Each item below gives both. x64 agrees on every decision. One Arm64 reading does not reproduce: at
100,000 rows the morsel `Derive` favours larger morsels on x64 (item 4).

**1. Held: the comparison and bitmap kernels.** 270: 1M rows 37.84 to 1.447 ms (one comparison) and
34.49 to 1.901 ms (two under `And`). Native Arm64, portable over native: `cmp` 4.36 to 0.82 ms (5.3x),
`cmp-and` 6.69 to 1.22 ms (5.5x), and 4.2x to 5.1x at 10,000 and 100,000 rows. The ratio is smaller
because the portable loop is no longer emulated, not because the kernel is weaker. **x64 agrees:**
`cmp` 6.56 to 1.24 ms (5.3x), `cmp-and` 11.0 to 2.30 ms (4.8x) at 1M rows, and 3.4x to 5.1x at
10,000 and 100,000 rows.

**2. Held: the morsel-parallel compiled `Filter`.** 270: 1M rows 55.2 to 10.3 ms. Native: 15.2 to
5.29 ms (2.9x), with 2.9x at 100,000 rows and none at 10,000 (two morsels). **x64 agrees, with a
smaller gain:** 25.0 to 9.82 ms (2.5x) at 1M rows, 1.4x at 100,000 and none at 10,000, on four
physical cores against twelve.

**3. Overturned: the morsel `Derive`.** 270 found it at parity at 1M rows (36.9 to 38.2 ms, "within
noise"). Native: 22.5 to 7.37 ms (3.1x), and 2.4x at 100,000 rows. On a quiet machine it is 2.7x at
both sizes (21.1 to 7.93 ms, 1.94 to 0.717 ms). The morsel runner pays for both row-local verbs.
**x64 agrees:** 34.9 to 18.3 ms (1.9x) at 1M rows and 1.4x at 100,000 (a second sitting: 2.2x and
1.5x), so the verdict is overturned on both architectures.

**4. Held at 100,000 rows, qualified below, unsettled above: `MorselRows` = 8,192.** Swept twice,
once with the machine busy and once quiet, all four sizes per sitting. Both sittings agree on two
points:

- at 10,000 rows 4,096 leads (quiet: compiled `Filter` 0.078 against 0.123 ms, `Derive` 0.112
  against 0.167 ms), because it makes three morsels where 8,192 makes two;
- at 100,000 rows 8,192 is best or tied, and 4,096 loses 9% and 24%.

At 1,000,000 rows the compiled `Filter` is within 6% at every size. The `Derive` favoured 8,192 in the
busy sitting and 16,384 by about 20% in the quiet one, so nothing is decided there. No single constant
wins at every size. The constant does not move in this phase. A morsel size that depends on the row
count (finer for small inputs) is open to the phases that build on this record (343, 344).

**x64 (one quiet sitting of all four sizes, and a second of 8,192 and 32,768):**

- at 10,000 rows it agrees: 4,096 leads (compiled `Filter` 0.136 against 0.223 ms, `Derive` 0.236
  against 0.296 ms);
- at 100,000 rows the compiled `Filter` is tied (within 9% at every size), but **the `Derive` does
  not reproduce "8,192 best or tied"**: 32,768 beats it by 21% to 27% in both sittings (1.80 and
  1.79 against 2.46 and 2.25 ms). That is four morsels, one per physical core, against thirteen. On
  this machine 4,096 loses 15%, as it loses on Arm64;
- at 1,000,000 rows the compiled `Filter` favours 8,192 by about 10% in both sittings. The `Derive`
  read 32,768 ahead by 20% in one sitting and tied in the other (15.2 against 15.4 ms), so it stays
  unsettled on both architectures. Larger-than-8,192 morsels led the `Derive` in Arm64's quiet sitting
  and in one of x64's.

The decision holds and the x64 data strengthens it: no fixed constant wins everywhere. The best
morsel size depends on the core count as well as the row count, which is one more input for the
phases that may make it adaptive.

**5. Overturned in its reason, held in its decision: the parallel `GroupBy`.** 270: parity at 10 keys
(14.1 against 13.8 ms at 100k, 388 against 408 ms at 1M), and 2.3x and 1.8x slower at one key per
ten rows. Its stated cause was that "a grouping over this evaluator is dominated by boxing the
aggregated column". Phase 323 removed that boxing. Rebuilt from 270's description over the streamed
aggregates and measured natively, the per-morsel-tables-merged-on-first-seen shape is:

- **1.55x faster** at 10 keys and 1M rows (quiet: 3.94 against 6.10 ms; busy: 3.85 against 6.15);
- at parity at 100,000 rows (1.06x slower quiet);
- **4.0x and 7.2x slower** at one key per ten rows (quiet: 92.7 against 23.0 ms at 1M, 8.63 against
  1.20 ms at 100k; the busy sitting read 3.0x and 6.4x, because load slowed the sequential arm),
  allocating 177 MB against 20 MB.

**x64 agrees** on four physical cores: **1.63x faster** at 10 keys and 1M rows (7.86 against
12.8 ms); 1.38x slower at 10 keys and 100,000 rows, where Arm64 was at parity; and **3.7x and 6.2x
slower** at one key per ten rows (140.0 against 37.7 ms at 1M, 23.0 against 3.68 ms at 100k), with
the same allocation.

So threads now buy something, but this shape spends it on merging tables that hold nearly every row.
It also reassociates a float `Sum`, which moves wire bytes. It stays unshipped. What carries forward
to Phase 344 is the shape the numbers point to: partition by group, so each thread owns whole groups,
folds them in row order, and no per-morsel table is ever merged.

**6. Held: vectorised integer reductions are not built.** It was never a timing. Every aggregate
still folds scattered physical rows into per-slot accumulators (`GroupAgg.Stream.FeedAll`), so no step
reduces a contiguous typed range. The architecture does not bear on it.

**Supporting change.** The benchmark assembly is the third that may see the dataframe package's
internals (`InternalsVisibleTo`), so the harness can run both members of the kernel pair through
`evalPreparedCountedWith` / `evalStepWith`. It is never packed, and no public surface moves.

## 2026-10-03 — D6: the corpus copy of the transform laws is held at its published stamp until it lands with the first host that reads the new vectors

**Decided (fuaran-core#356), measured rather than assumed.** The shared wire-format corpus carries a
declared copy of `conformance/laws/transform-laws.json` (D1 (6)). That copy is stale: `kitVersion`
0.35.0, 16 `evalPipeline` vectors, in the pre-rename names. The copy is NOT re-published on its own.
It lands in the same change-set as the first host that reads the new vectors, which is fuaran#2001
(the host twins learn the exact decimal and the rounding vocabulary). Until then the record in
[`copies.json`](copies.json) carries a `lag` naming that phase, so the workspace copy registry
reports a lag with an owner rather than an unowned stale copy.

**1. What was measured (2026-10-03).** Emitted from a detached checkout of the `v0.37.0` tag, the
file is byte-identical to the one committed at that tag: 60 vectors, of which 47 are `evalPipeline`
(16 drawn shapes, the decimal shapes from iteration 16, the derive-typing shapes from D5), 9 are
`columnOp` and 4 are `delta`. The released 0.37.0 file and this repository's 0.38.0 draft differ in
the `kitVersion` stamp alone. Run against that file, the one host that certifies against the family
(fuaran-ts, `packages/ops/test/transformLaws.test.ts`) passed 15 vectors, failed 21 and could not
decode the pipeline of 26:

- it has no `decimal` column type, so every decimal source is refused at decode;
- it has no `quotient`/`rounded` expressions and no `intersect`, `except` or `countDistinct`, so
  those pipelines do not decode;
- it has no `ColumnOp` or delta codec, so the `columnOp` and `delta` cases cannot be run at all;
- two vectors that predate the decimal (`transform-5-div-by-zero`, `transform-13-div-by-zero`) now
  expect D5's typing of an all-null derived column, a numeric column of nulls rather than a string
  one, and the host still produces the old answer.

So the work the host needs is the decimal strand itself (D72 of the substrate, carried here by
Phases 276, 277, 321 and 338), not two new case kinds. fuaran#2001 already owns that port for all
four language hosts, and its own first task is this re-emit.

**2. Why the copy is not published ahead of the host.** Publishing the copy alone turns a public
host's gate red for a change that host did not make, 21 failures at once. Marking the vectors the
host cannot evaluate as skipped would make that gate green while certifying none of them. Neither
is acceptable, so the copy waits for the host.

**3. Who reads the family today.** fuaran-ts only. fuaran-go, fuaran-py and fuaran-rs each carry an
evaluator twin but no transform-laws leg (fuaran#2001 adds one to each). The .NET host evaluates
with this repository's packages, so it has no twin to certify. The bundled corpus snapshots in
fuaran-ts and fuaran-py do not carry `laws/`, so a laws-only corpus commit changes neither payload.

**4. Not decided here: release stamp or draft stamp.** fuaran-core#356 asked for the copy to be
emitted at a released version, never an untagged draft, and the measurement in (1) was taken that
way. D1 (6) and `version-derives.json` say a version cut re-emits the copy with the stamp, and the
copy registry compares the copy with this repository's committed file. Those two rules disagree
whenever a draft is ahead of the last release, as `0.38.0` is ahead of `v0.37.0` today. Then a copy
emitted at the release differs from `conformance/` in the stamp alone. This suite's freshness leg
reports that as `STAMP ONLY`, and the registry reports it as stale. The vectors are identical either
way. Which stamp the published copy follows is left to fuaran#2001's landing, and is recorded here
so that the choice is made deliberately.

**4 decided (2026-10-03, operator ruling): the published copy follows the RELEASE stamp.** The shared
corpus is public, and the hosts that certify against it should certify against a contract a consumer
can actually restore; an untagged draft's stamp names a version nobody can obtain. So:

- the copy is re-emitted when a version is **released** (tagged and published), not when a draft is
  cut, and carries that release's `kitVersion`;
- a copy whose stamp is this repository's **latest release** and whose vectors match is **fresh**, even
  while a draft is ahead of that release; the copy registry and this suite's freshness leg are to read
  it that way, rather than comparing with the committed draft file;
- the declared derivation in `version-derives.json` moves from the cut to the release accordingly, once
  the workspace copy registry and version tooling support it.

Until that support ships, a release-stamped copy still reads `STAMP ONLY` here and stale in the
registry; the `lag` in `copies.json` covers the present gap, owned by fuaran#2001.
The lag is kept deliberately (operator, 2026-10-03): it turns an unowned stale record into an honest one
with an owner.

**4 adopted (2026-10-08, Phase 715).** The record in [`copies.json`](copies.json) and the derivation in
[`version-derives.json`](version-derives.json) both declare `"against": "release"`, and the record names
`"stamp": "kitVersion"`. The workspace copy registry now compares the corpus copy with this repository's
file at its newest release tag, reads a committed draft that is ahead of that release in the stamp alone
as information rather than drift, and still reports any other difference as stale; the copy obligation
is raised when a version is released rather than when a draft is cut. A cut still re-emits
`conformance/laws/transform-laws.json` here, because this suite holds that file to what the kit renders
at the declared `<Version>`. The `lag` is unchanged and still owned by fuaran#2001: it says when the copy
moves, while the grading says what the copy is compared with.

**Amendment (Phase 573): the lag is removed.** The corpus copy of `conformance/laws/transform-laws.json` is now identical to this repository's file, so the `lag` block in [`copies.json`](copies.json) held nothing and the registry reported it as removable. The record keeps `against: release` and `stamp: kitVersion` (4, as decided), so it is graded against the released source with no stale exemption. The copy no longer waits for a host, and the hold in the opening paragraph no longer applies.

**Second amendment (2026-11-10, at the v0.44.0 release): the lag is declared again.** The copy was not re-emitted at v0.42.0 or v0.43.0. Re-emitted at v0.44.0 it carries 194 vectors, and the Rust host fails 63 of the 190 it runs, on the behaviour those releases added: int32 overflow refusal, the transcendentals on the pinned elementary routines, named functions and interpolated curves, and Fit and Predict, which fuaran#2444, #2586, #2587 and #2588 bring to the hosts. Under the landing rule of the opening paragraph the copy stands at v0.41.0, and the `lag` in [`copies.json`](copies.json) says so, until the first host that passes the new vectors lands it in the same change-set.

## 2026-10-02 — D5: a derived column is typed by its expression; the cells decide only where the typer cannot; a float beside a decimal is refused

**Decided (Phase 338, carrying D3's last paragraph).** One rule types every column the dataframe
strand derives — a `Derive`'s, and an `Unpivot`'s `value` — and every reader takes it from one
place (`DataFrame.derivedTyping`, `unpivotTyping`, `columnTypeBy`): the evaluator, the incremental
seam's walk and its chunked path, `SchemaWalk`, the planner and `PipelineQuery.check`. So refresh
and full, the walk and the evaluator, cannot disagree about a type by construction.

**1. The rule.** Over the step's schema, before any row:

- the expression's own arms (through `Case` and `Coalesce`, the nodes whose answer IS an operand's)
  carry a decided `float` and a decided `decimal` → **refused**, on every frame, by name;
- the typer decides it (`Of ty`) → `ty`, on every frame — an empty one, an all-null one;
- the typer knows it produces no present value (`Absent`) → `string`, the type an all-null
  column has always had;
- the typer cannot decide (`Unknown`) → **the cells decide**: Phase 321's widening join of the
  present cells' types (D3 rule 2), `string` where none is present, and a refusal over the whole
  column where the cells hold a `float` beside a `decimal`.

An unpivot reads the same rule with its value columns as the arms, joined by the widening join of
their DECLARED types: no totality verdict reads an unpivot's type as an exactness claim, so the
schema decides an `int`/`float` or `int`/`decimal` melt, and only a pair no widening relates (a
`string` beside an `int`) is left to the cells.

**2. A cell is admitted, never converted.** Under a decided type every present cell is of that type
or widens into it by `ColumnType.widens` (an `Int` in a `float` or `decimal` column); it is stored as
produced, as D2 stores an edited cell. A present cell the decided type does not admit would be the
typer and the evaluator disagreeing: `DeriveTypingConformance.laws` goes red on it (its go-red is in
the suite), and the evaluator keeps the cell as produced rather than coerce it.

**3. The one data-decided remainder.** A `Param` (its cell is the arguments'), a `Now` (the clock
witness's), a column the schema in hand does not carry (an open schema's), and a join the EXACT
typer keeps apart — a `Case` of an `int` and a `float`, a `Coalesce` of a `decimal` and an `int`.
The last is wider than the phase's shard assumed: it was written expecting the typer to join `int`
and `float` at `float`, which D3 had declined (the join is an exactness claim the totality verdict
reads). The rule here does not widen the typer either; those shapes stay the cells', named as such
in `PipelineQuery`'s `TypeUndecidable`, and the planner declines to move a filter past them.

**4. The verdict follows.** `Plan.isTotal` calls a derive total only where its expression is total
AND its column is decided: a cells-typed derive can refuse a float beside a decimal over the whole
column, and a statically refused one always does, so neither may be called total. That is also what
makes a decided derive of ANY type safe for `FilterBeforeDerive` (its type reads no row), so the
planner's string-only clause is gone. `proofs/Pipeline.fst` models the rule clause for clause —
the typer is now extracted, because `eval_derive` reads it — and `verdict_sound`, `step_total`,
`derive_then_filter` (now over a decided type) and the new `derive_type_rows_free` (a decided
derive's column has one type over any two frames of a schema) verify.

**Declined: converting a widened cell.** Writing an `Int` into a float column as `Float` would
make the column's carrier uniform, and would make the type change a VALUE change every host must
mirror; storing as produced keeps the vectors' cells byte-identical and matches the substrate's own
admission.

## 2026-10-02 — D4: the packages take their own ids — `Fuaran.Compute.*` and the `Fuaran.Compute` namespace — opening at `0.36.0`; the `Fuaran.Core.*` ids stop at `0.34.0`

**Decided (Phase 322, superseding D1's first ruling).** D1 kept the ids and namespaces the strand
shipped with, on the argument that a rename is a source change in every consumer for no gain. The
gain turned out to be real and the cost of not paying it recurring: a reader of `Fuaran.Core.DataFrame`
is told by its name that the dataframe is a property of the substrate, which the substrate's own
boundary (its D51: the compute layer is the witness-free strand, not a Core property) says it is not,
and every review of the split had to explain the mismatch again. A pin is a bad reason to keep a name
that says the wrong thing. The name follows the producer.

**1. The mapping.**

| Through `0.34.0` | From `0.36.0` |
|---|---|
| `Fuaran.Core.DataFrame` | `Fuaran.Compute.DataFrame` |
| `Fuaran.Core.Column.Ops` | `Fuaran.Compute.ColumnOps` |
| `Fuaran.Core.DataFrame.Conformance` | `Fuaran.Compute.Conformance` |
| `Fuaran.Core.DataFrame.PipelineQuery` (Phase 281; never published) | `Fuaran.Compute.PipelineQuery` |

The fourth row is the package Phase 281 added after D1; it is renamed with the others, so its first
publication is under the new id. The C# facade (`Fuaran.Core.DataFrame.CSharp`) has no row: it was
deleted outright beside the substrate raise to `0.33.0` (the `0.36.0` STABILITY entry on its removal),
so there is no `Fuaran.Compute.CSharp`.

**2. One namespace, `Fuaran.Compute`; the module names inside are unchanged.** Every source file
declares `namespace Fuaran.Compute` and opens `Fuaran.Core` for the substrate types it is built over.
`DataFrame`, `ColumnOps`, `Transform`, `Incremental`, `Delta`, `Plan`, `DataFrameConformance` and the
rest keep their names, so a consumer's change is its package references and one `open` per file.
The pre-split forwarding module moves with its namespace: `Conformance.<family>` still resolves for a
file opening both namespaces, the fully qualified `Fuaran.Core.Conformance.<family>` does not.

**3. The new ids open at `0.36.0`, the `0.35.0` draft ADVANCED rather than reclassed.** `0.35.0` was
untagged and unpinned, so it could have been reclassed in place; but the draft rule advances a slot
that takes a change of a higher class than it carries, and the first version under new ids should be
a fresh contract rather than a number whose entry already described additions to the old ones. The
`0.35.0` entry's content is folded into the `0.36.0` entry, which says `0.35.0` was never released.

**4. The old ids stop at `0.34.0`, with no deprecation package.** They are not republished; nuget.org
keeps every published version restorable, which is all a consumer that has not moved needs.

**5. What does NOT change.** The repository (`fuaran-core-compute`), its organisation, the
solution and the unshipped test, benchmark and proof-oracle projects keep the repository's name
(`Fuaran.Core.Compute.*`) — the repository is not renamed and none of those projects is a package —
and so does the claims ladder's subject. The wire format names no CLR type, so no canonical byte moves:
each `api/wire/` baseline's body is byte-identical to its predecessor's.

**6. How the move is classed.** The `Public surface` and `Wire surface` families compare each
package with its baseline at the newest tag; a tag cut before the rename holds the baseline under the
old id, so both read through a declared predecessor map (`PublicSurfaceTests.predecessorIds`) rather
than reporting a first snapshot. That is what makes the rename read as the `removal` it is, and what
keeps the decimal's wire `breaking` since `v0.34.0` stated across the rename instead of reset.

**7. The registration is an operator act.** nuget.org Trusted Publishing policies are scoped to
package ids, so the policy for this repository must be extended to the `Fuaran.Compute.*` ids before
the first `v*` tag under them; a failed `NuGet/login` on that tag is the missing registration, not an
authentication fault (the publish workflow's header says so where the failure is met).

## 2026-10-02 — D3: one float order, the substrate's; a derived column's type is the join its cells widen into, and the static typer stays exact

**Decided (Phase 321).** Three rulings about how the evaluator types and orders what it computes,
taken together because each is the decimal's arrival exposing an older looseness.

**1. The evaluator orders floats as the substrate does: `NaN` one value ABOVE every other.** The
evaluator compared floats with the host's `compare`, which puts `NaN` below every value on .NET and,
under Fable, answers `1` for both `compare nan 1.0` and `compare 1.0 nan` — an order on one host and
not an order on the other. The substrate's `Cell.compare` (its Phase 315) puts `NaN` last, and
`Column.aggregate`'s `Min` / `Max` already read that order, so a descending sort and a `Max` named
two different largest values. `Kernels.compareFloat` now states the order once; the sort kernel,
the compiled comparison arm and both comparison kernels (the portable loop and the vector one, whose
`NaN` lanes moved from `<` / `<=` to `>` / `>=`) read it. A float filter `x > k` now keeps a `NaN`
row and `x < k` drops it; a sort puts `NaN` after `+Inf` and before the nulls. Recorded as breaking.

**2. A derived column's type is the join of its present cells' types under `ColumnType.widens`.**
It was the type of the first present cell, so a `Case` answering `Int 1` on one row and `Float 2.5`
on the next built an int column holding a float — a table the substrate's `Table.validate` refuses,
and whose `Sum` its Phase 299 turns into an error. Now `Int` and `Float` join at `Float`, `Int` and
`Decimal` at `Decimal` (the common money shape: `Coalesce(amount, Lit(Int 0))`). A pair no widening
relates keeps the EARLIER type, which is the pre-321 answer for that pair, `Float` beside `Decimal`
included. **Declined: refusing `Float ⊔ Decimal` by name at a derive.** It would add a whole-column
refusal to `Derive`, which the planner's totality verdict (proved sound over the evaluator in
`proofs/Pipeline.fst`) does not model: a derive the verdict calls total could then fail, and the
reorder that verdict licenses could change which error a pipeline reports. That is a change to the
verdict and its proof, and it is left to a phase of its own rather than half-made here. The model
moved with the rule: `Pipeline.fst`'s `infer_type` is the same fold, re-verified and re-extracted.

**3. The static typer's join stays EXACT.** The phase that brought rule 2 also asked for
`Typing.join` to widen. It does not, because `Of t` is an exactness claim the totality verdict reads:
`Of IntType + Of IntType` is not total (two ints can overflow), and a `Case` of an int and a float
typed `Of FloatType` would make the same addition read as total while both operands are ints at run
time. So `Int ⊔ Float` and `Int ⊔ Decimal` stay `Unknown` in the typer, and `DataFrame.typeOf` answers
`None` for them — undecided, never wrong.

**Also recorded: the law vectors carry the two wires (operator ruling, 2026-10-02).**
`conformance/laws/transform-laws.json` is this repository's derived file, and Phase 277 had already
appended its decimal `evalPipeline` vectors to it, so decimal rows through the columnar op wire and
the delta wire are the same act. They are appended AFTER the 39 `evalPipeline` vectors, which are
byte-identical, as two further case kinds: `columnOp` (a source table and an op; the result table or
a refusal, and the op's re-encoded wire string) and `delta` (two tables and a key column; the
encoded delta). `iterations` still counts the `evalPipeline` vectors only. The hosts' copies of the
file follow the 2026-09-20 ruling: they stay as they are until each host raises, and meet the two
kinds then. None of the existing vectors moved under the rulings above.

**Carried to Phase 338, not built here (operator ruling, 2026-10-02).** Refusing a `Float` beside
a `Decimal` at a derive, and typing a derive by its expression rather than by its cells, move with
the rewrite of the totality verdict and `Pipeline.fst` that phase makes; rule 2 above is what this
phase ships in the meantime. (Built by Phase 338: D5. Rule 2 still types a column only its cells
decide, and its "earlier type" for a float beside a decimal is replaced there by the refusal.)

## 2026-10-02 — D2: FS0025 is an error here, and `cellFits` widens exactly as `ColumnType.widens` does

**Decided (Phase 321; the `cellFits` half is an operator ruling of 2026-09-30).**

**1. An incomplete match is a compile error in every project but the proof oracle.**
`Directory.Build.props` escalates FS0025. The strand matches over the substrate's closed unions
(`Cell`, `ColumnType`), which gain cases on a raise; as a warning, an arm blind to the new case
compiled and threw `MatchFailureException` on the first value of it. As an error, the raise itself
names every arm that must move. The proof oracle is exempt by project name: it is extracted F\*
code, total by proof rather than by syntax, and it keeps its own `NoWarn` set. **The dry run this
phase was asked to record:** with FS0025 an error, the repository at Core `0.33.0` built with ZERO
errors — the substrate's raise (Phase 277) had already given every exhaustive arm its `Decimal` case,
so the escalation now guards the next raise rather than closing a list on this one.

**2. `ColumnOps.cellFits` accepts a cell whose type widens into the column's.** It was exact type
equality. The substrate's codec decodes a JSON int into a decimal (and a float) column, and its
`Table.validate` checks every cell through `widens`, so a strict edit check refused cells the
substrate's own wire produces and its own validator accepts: a table valid on one side of the
boundary failed an edit on the other. Now an `Int` fits a `FloatType` or `DecimalType` column, and
nothing else widens (`Float` into decimal and decimal into float are refused by name). **The cell is
stored as given, never converted**, so `invert` restores the previous cell verbatim and its round
trip is exact as before. **The strict reading is declined**, for the reason above. `proofs/ColumnOps.fst`
moved with it: the model gained `DecimalType` and a `widens`, `cell_fits` reads it, every theorem
re-verified unchanged, and the oracle differential now draws decimal columns (it could not before).
`cellToJson` writes the canonical decimal text, so a cell built by hand as `1.50` encodes as `1.5`,
the spelling the decoder reads.

## 2026-09-26 — D1: the compute strand is cut from Fuaran.Core into this repository under the same ids; it opens at `0.33.0`, and the forwarding module goes when the substrate removes the strand

**Decided (Phase 259, carrying out the Fuaran.Core repository's D66).** The substrate's D66 ruled
that `Fuaran.Core.DataFrame` and `Fuaran.Core.Column.Ops` are produced by a repository of their own,
together with the dataframe law families, the dataframe half of the C# facade, the two proof models
that cover them and ownership of the transform law vectors, while `Fuaran.Core.Column` and every
other spine package stay. Its reason, in one line: the substrate's programme is stability, laws and
proofs, and it should break almost never; the compute layer's is a performance programme, and it
will break repeatedly — one `<Version>` over both would force either false breaks on the spine or
held-back compute. That entry, and the substrate's D68 (how the boundary was prepared inside it
first, so this cut is a copy of whole assemblies), are the record of WHY. This entry records what the
cut is.

**1. Same ids, same namespaces.** The four packages keep the ids and the CLR namespaces they shipped
with. A consumer raises a version and changes nothing else; nothing here renames anything, and a
rename would be its own breaking decision.

**2. History carried, not re-created.** The moved paths were carried with `git filter-repo` from a
clone of the substrate at `v0.32.0`, so `git log --follow` on a moved file reads its whole history.
Files this repository needed but the substrate keeps — the test infrastructure, the C# proof legs
shared with the substrate's facade proof, the proof-leg kit — are copies; the kit files are declared
in [`copies.json`](copies.json) so a drift between the two is named rather than discovered.

**3. `0.33.0`, the next minor above the substrate's last emission of the ids.** The substrate
released the four ids at `0.32.0`. Opening here at `0.33.0` keeps every consumer's version floor
monotone across the change of producer: no version number ever names two contracts. It is a DRAFT
until it is tagged.

**4. The substrate by package, at one pin.** The five substrate packages the strand stands on
(`Column`, `Wire`, `OpStream`, `Conformance`, `CSharp`) are package references at ONE property,
`FuaranCoreVersion`, restored from the public registry; the `Compute boundary` family refuses a
sixth, a project reference to the substrate, and any package outside the allowed set.

**5. The pre-split spellings go when the substrate removes the strand (Phase 258).**
`Fuaran.Core.DataFrame.Conformance` carries `Forwards.fs`: a module named `Fuaran.Core.Conformance`
whose members forward to `DataFrameConformance`, so code written against the pre-split
`Conformance.<family>` spellings kept compiling after the substrate's Phase 257. It exists for the
window in which both producers can ship the ids. It is removed in the change-set that answers the
substrate's Phase 258 — the phase that removes the strand from the substrate, on a breaking draft
whose entry names this repository and the version the ids continue at. Removing it is a breaking
change here too, so it advances this repository's draft slot rather than riding it.

**6. The transform law vectors are this repository's derived file.** `conformance/laws/transform-laws.json`
is emitted by `--emit-laws` from this repository's evaluator and stamped with this repository's
`<Version>`, and [`version-derives.json`](version-derives.json) declares the shared wire-format
corpus copy as derived from it. Until the substrate removes the strand, both repositories can emit
the file; the vectors are byte-identical and only the `kitVersion` stamp differs (`0.32.0` there,
this repository's version here), so the corpus copy can agree with only one of them. For that window
this repository's CI compares against the corpus and PRINTS a drift rather than failing on it (it
does not set `FUARAN_CORE_CORPUS_FRESHNESS`): made fatal on both sides, one producer's CI would be
red on every push for a byte neither can change. The corpus copy is re-emitted from here, and the
flag set in this repository's CI, in the change-set that answers the substrate's Phase 258 — the
point at which this repository becomes the only producer.
