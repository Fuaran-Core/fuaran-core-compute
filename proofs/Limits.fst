(*
   Limits — the WIRE_FORMAT §21 resource limits, as NAMED PREMISES and nothing else
   (fuaran-core Phase 149).

   WHAT THIS IS. §21 makes eight bounds part of the FORMAT rather than a per-host deployment
   choice: a document within them is one every conformant host MUST decode, and a document beyond
   them is one every conformant host MUST refuse with the same typed error. Several models in this
   directory need to say "within the format's limits" in a hypothesis, and before this module each
   would have spelled its own number inline. One home, so that a changed limit moves ONE constant
   and the claims ladder can say which theorem depends on which bound.

   WHAT THIS IS NOT. It is not a model of enforcement. Nothing here walks a document, counts a
   depth, or raises a `LIMIT_EXCEEDED`; §21.2's host obligations — refuse on the way DOWN, never as
   an exception, every walk and not only the decoder — are about code this module does not model.
   What a consumer gets from here is a NUMBER with a caption, usable as the bound in a premise.
   `Canon.fst` is the first consumer, and takes exactly one of the eight.

   WHY THE NUMBERS ARE `nat` AND NOT A LIST. Every other model in this directory spells a counter as
   an exhausted `list unit` budget, because the F# extraction carries no integers (README, finding
   2). That device exists to keep a RECURSION's measure extractable; these are constants that are
   never counted down, and `Prims.nat` has been extractable since the Phase 160 extraction added it.
   A bound that reads `24` is worth more to a reviewer than one that reads as a 24-element list.

   Apache-2.0, like everything beside it.
*)
module Limits

(* ======================================================================================
   1. The eight bounds (WIRE_FORMAT §21.1), each captioned with what it bounds.

      The values are the table's, verbatim. A change here is a change to the FORMAT and moves
      the corpus, every conformant host and this module together — which is the whole reason
      they sit in one place.
   ====================================================================================== *)

(* NODE nesting — the longest root-to-leaf chain of `Node` objects, the root counting as 1. *)
let max_node_depth : nat = 24

(* SYNTACTIC nesting — the depth of the underlying JSON document; every `{` and `[` counts,
   whether it carries a node, a spec, or a rule-12 payload. This is the one `Canon.fst` takes. *)
let max_json_depth : nat = 256

(* Unicode CODE POINTS in a single decoded JSON string (§21.6 — the unit is the normative half). *)
let max_string_length : nat = 1048576

(* Elements in a single JSON array, and members in a single JSON object. *)
let max_array_length : nat = 100000

(* `Node` objects in one document, summed across the whole tree. *)
let max_total_nodes : nat = 100000

(* UTF-8 bytes of the whole input document (§21.7). *)
let max_document_bytes : nat = 33554432

(* `ColExpr` nodes in ONE expression — a `Binding.Expr`'s, or one a `Binding.Transform` embeds
   (§21.8). *)
let max_expr_nodes : nat = 512

(* The value of ONE `Skeleton` node's `rows` slot (§21.9). *)
let max_skeleton_rows : nat = 10000

(* ======================================================================================
   2. The two facts §21 argues in prose, machine-checked — so that a future edit to the
      table cannot silently break the reasoning the section rests on.

      There are only two, deliberately. A module of constants earns a `proved` row by
      carrying the RELATIONS between them that the specification's own argument uses, not by
      restating each number as a lemma about itself.
   ====================================================================================== *)

(* Every bound admits something. A bound of zero would make the format's "MUST accept any document
   within every limit" vacuous for that dimension, and §21.2's obligation 1 unsatisfiable in an
   interesting way rather than a detectable one. *)
let limits_positive ()
  : Lemma (ensures max_node_depth > 0 /\ max_json_depth > 0 /\ max_string_length > 0 /\
                   max_array_length > 0 /\ max_total_nodes > 0 /\ max_document_bytes > 0 /\
                   max_expr_nodes > 0 /\ max_skeleton_rows > 0) = ()

(* §21.1: "256 is chosen so that it comfortably admits a maximally-deep tree of any kind shape with
   payload room left over — a host must never report a node-depth breach as a syntax-depth breach,
   because that diagnosis sends the author to repair the wrong thing."

   The two halves of that sentence, as the two halves of this lemma. `max_node_depth <
   max_json_depth` is the bare separation; the second conjunct is the "comfortably" — the section's
   own worst case is about five JSON levels per tree level, so a maximally-deep tree of the
   worst-shaped kinds costs `5 * max_node_depth` syntactic levels and the syntactic bound must
   still exceed it with room left. If a future revision ever tightens the JSON bound or loosens the
   node bound past that, this goes red and the diagnosis argument has to be re-made rather than
   silently lost. *)
let depth_bounds_do_not_alias ()
  : Lemma (ensures max_node_depth < max_json_depth /\ 5 * max_node_depth < max_json_depth) = ()
