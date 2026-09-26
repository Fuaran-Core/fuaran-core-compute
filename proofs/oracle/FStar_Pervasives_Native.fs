// The second half of the runtime the F*-extracted models beside this file compile against —
// Phase 131's finding 2, met again by a model that needed one more library type.
//
// `Prims.fs` closes the gap for the names the F# backend emits from `Prims` (the list type, the
// string and bool types, decidable equality, string concatenation). A model that uses F*'s
// `option` — `TreeOps.fst` does, because `Tree.tryFind` and `Tree.parentOf` return one and the
// model is faithful to them — makes the backend emit `FStar_Pervasives_Native.option` /
// `.Some` / `.None`, and the release ships no F# implementation of THAT module either.
//
// Two lines closed it. Nothing here is semantics: the type is F#'s own option under F*'s spelling,
// and the constructor names are the ones the extractor writes. Phase 141 added two more for the
// same reason — see the note beside them.
//
// It is hand-written and therefore NOT diffed by the proof leg, exactly as `Prims.fs` is not —
// the leg holds the GENERATED files to a fresh extraction, and these two are the floor they stand
// on. Adding a model that reaches for another library type means adding to this floor; the
// alternative, restating `option` inside each model so the extraction depends on `Prims` alone,
// buys a shorter shim at the cost of a model that no longer reads like the F# it is about.
module FStar_Pervasives_Native

type option<'a> =
    | None
    | Some of 'a

/// F*'s `fst` / `snd` on a pair, which the F# backend emits fully qualified out of this module.
/// Named by the Phase 141 extraction, the first model with a function that returns a TUPLE:
/// `Diff.toOps`' second pass computes the move script AND the list of parents whose order must be
/// restated later in ONE walk, so a model that split it into two functions would be splitting the
/// very walk the emission-order theorem is about. Like everything else in this floor these are the
/// F# primitives under an F* spelling; there is no semantics here.
let inline fst ((a, _): 'a * 'b) : 'a = a
let inline snd ((_, b): 'a * 'b) : 'b = b
