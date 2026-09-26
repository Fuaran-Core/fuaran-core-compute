// The runtime the F*-extracted `DagFold.fs` beside this file compiles against.
//
// F*'s F# backend (`--codegen FSharp`) emits code that names a handful of `Prims` members —
// the list type, the string and bool types, and decidable equality — and the F* release ships
// no F# implementation of them (the OCaml backend has one; the F# one is second-class, which is
// one of the findings the Phase 131 spike recorded). These are the aliases that close the gap.
// Nothing here is semantics: every name is the F# primitive under an F* spelling.
module Prims

type list<'a> = Microsoft.FSharp.Collections.List<'a>
type string = System.String
type bool = System.Boolean

/// F*'s `=` on an `eqtype` — decidable structural equality.
let inline op_Equals (x: 'a) (y: 'a) : bool = (x = y)

/// F*'s `^` on strings — `Prims.strcat`. Named by the Phase 135 extraction, which reproduces
/// `Decode`'s error MESSAGES rather than merely their class, so the differential compares what
/// a failure says as well as that it failed.
let inline strcat (x: string) (y: string) : string = x + y

/// F*'s `<>` on an `eqtype` — decidable structural DISequality, the counterpart of `op_Equals`
/// above. Named by the Phase 146 extraction, whose error-kind confinement predicates are written
/// as "every kind but this one". Like everything else here it is the F# primitive under an F*
/// spelling; there is no semantics in this file.
let inline op_Less_Greater (x: 'a) (y: 'a) : bool = (x <> y)

/// F*'s `nat` — an UNBOUNDED integer, refined to be non-negative. Named by the Phase 160
/// extraction, the first model to carry a numeric index (the step index `Ops.applyAllWith`
/// reports). `bigint` rather than `int` because F*'s integers are unbounded and this file's rule
/// is the F# primitive under an F* spelling, not the F# primitive that happened to be convenient;
/// a host that reads one of these back against a production `int` converts at the boundary, and
/// the conversion is where the width assumption belongs.
///
/// The non-negativity is a REFINEMENT, and extraction erases every refinement — so this alias
/// cannot carry it and does not pretend to. What holds the property is the proof: the index is a
/// `nat` in `Preservation.fst`, where the prover checks it.
type nat = System.Numerics.BigInteger

/// F*'s integer literals, which the F# backend emits as a parse of their decimal text rather than
/// as a literal (the same second-class-backend finding the header records). Invariant by
/// construction — `BigInteger.Parse` of a decimal digit string reads no culture.
let parse_int (s: string) : nat = System.Numerics.BigInteger.Parse s

/// F*'s `int` — an UNBOUNDED integer, unrefined. Named by the Phase 176 extraction, the first
/// model to carry a SIGNED index: `ColumnOps.apply`'s row and `InsertColumn`'s index are F#
/// `int`s that a caller can pass negative, and the model keeps them signed so the `row < 0`
/// clause is a modelled refusal rather than a bridge-side convention. Same rule as `nat` above —
/// the F# primitive under an F* spelling, `bigint` because F*'s integers are unbounded — and
/// the same boundary: a host converts at the edge, and the width assumption lives there.
type int = System.Numerics.BigInteger
