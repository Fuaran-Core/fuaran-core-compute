(*
   Relational — the twelve relational verbs' OUTPUT SCHEMA and REFUSALS, and the static schema walk
   that reads them, modelled clause for clause and proved to agree (fuaran-core Phase 434; the
   repeated-name refusal, Phase 468; the union's walk, Phase 469).

   WHAT IS MODELLED. `src/Fuaran.Compute.DataFrame/DataFrame.fs`, for the twelve verbs
   `Pipeline.fst` takes as its second parameter (`other_fn`) — `Project`, `GroupBy`, `Join`,
   `Window`, `Pivot`, `Unpivot`, `Sort`, `Distinct`, `Limit`, `Union`, `Intersect`, `Except`:

     - each verb's SCHEMA computation — the output column names and types `evalStepWith`'s arm
       for it answers (`step_schema`): `evalProject`'s pairs, `evalGroupBy`'s keys then one column
       per aggregate under `Column.aggType`, the combining join's `f.Cols @ outRight` with the
       colliding right names suffixed by `joinRightNamesOf` (Phase 411, D20) and the filtering
       joins' left schema, `evalWindow`'s appended column under the window's own type rule,
       `evalPivot`'s index columns then one column per distinct present `on` value in canonical
       text order, `evalUnpivot`'s id columns then `variable` and `value` typed by `unpivotTyping`
       and, where that leaves it to the cells, by `columnTypeBy` over the melted cells (Phase 338),
       `evalUnion`'s per-column type join (Phase 423, D18), and the schema the row-set verbs keep;
     - each verb's REFUSALS that read the schema and the pipeline text — every `UnknownColumn` in
       the order the arm reports it (`joinKeyIdx`'s left names then right names, `windowRefusal`'s
       bucket count then read column then partition keys then order keys then output name,
       `evalPivot`'s index then `on` then `values`, …), the `Sort` and `Limit` slot resolutions
       (`resolveStrSlot` / `resolveIntSlot`: `UnboundParam`, then the `TypeError` naming the slot),
       `unknownSortKey` (Phase 412, D21), the window's taken output name (Phase 450, D28), the
       repeated output name of a `Project`, a `GroupBy`, an `Unpivot` and a `Pivot` (`repeatedOutput`,
       Phase 468, D35: after the arm's unknown names, before any row is read or aggregated), the two
       set operations' and the union's name agreement (`JoinError`), and the union's and the
       unpivot's type refusals (`typesBesideEachOther`, `floatBesideDecimal`);
     - the source seam the two-table verbs pass first, `evalSource` (`Embedded` is itself, `Ref`
       is the resolver's answer);
     - `SchemaWalk.ofTransform` — the static walk a planner and the pipeline-query registry read —
       over `SchemaKnowledge` (`Closed` / `AtLeast`), every one of its fourteen arms (`walk`), and
       the reading of a source it makes (`ofSource`: an undeclared `Ref` opens the knowledge).

   WHAT STAYS ABSTRACT, and where the boundary is. ROW CONTENT is read only where a verb's schema
   reads it: the pivot's `on` column (its distinct present values NAME the value columns) and the
   unpivot's melted value cells (they TYPE the value column where the declared types leave it to
   them). No theorem reads a cell otherwise. What a verb does to the rows — what a `GroupBy`
   aggregates, what a `Join` fans out to, which rows a `Sort` orders first — and every refusal
   that reads a cell (an aggregate over a column it cannot aggregate, `AggError`) is the ROW
   FUNCTION (`row_fn`), the parameter `other_of` builds the twelve-verb evaluator from. That is
   the narrowing of `Pipeline.fst`'s twelve-verb assumption this module makes: the verbs' schema
   and schema-level refusals are clauses, and only the rows and their refusals are assumed.

   THE PARAMETERS, each an assumption, and what the theorems assume of each (`rprims`):
     - `cell_string` (F# `DataFrame.cellString`): the canonical text of a cell — names the pivot's
       value columns. Nothing is assumed of it — `pivot_no_dups` holds whatever text it gives;
       `pivot_repeat_refused` is conditional on its text for one string cell being that string,
       which it is.
     - `int_text` (F# `string` over an `int`): the decimal text of the window's bucket count in
       the `NTile` refusal. Nothing is assumed.
     - `string_lt` (F# `compare` on strings, through `List.sortBy`): the order the pivot's value
       columns take. Nothing is assumed; no theorem reads the order.
     - `candidate` (the suffix rule inside `joinRightNamesOf`: `name + "_right"` for 1,
       `name + "_right_" + string n` for n >= 2): `join_no_dups` and the open-right half of
       `walk_sound` assume it INJECTIVE in `n` for a fixed name (`cand_injective`) — a fact about
       string concatenation and decimal rendering the prover has no axiom for. The go-red in the
       oracle crosses the pre-D20 rule (every n to `name + "_right"`) as this parameter and
       measures the disagreement.
     - the RESOLVER (`resolver`, the `resolve` closure `evalStepWith` closes over) and the walk's
       SOURCES (`sources`): `walk_sound` assumes they AGREE (`sources_agree`) — a declared schema
       is the schema the resolver's table has — which is the contract a caller of
       `SchemaWalk.ofPipelineFrom` with `ofMap` takes on.
   Units (Phase 429) are not in `Pipeline.fst`'s schema and are not here: the model is the unitless
   pipeline, every `UnitMismatch` a verb raises is outside it, and so is the unit each walk arm
   carries.

   WHAT IS PROVED.
     - `walk_sound` (SCHEMA AGREEMENT): for every verb, over knowledge SOUND for the
       input frame's schema (`sound`: a closed knowledge has the schema's names in order and every
       type it knows is the schema's; an open knowledge names columns the schema carries, with the
       types it knows), when the verb answers `Ok out` the walk's answer is sound for `out` — so a
       planner reading a closed walk reads the evaluator's names and types, and reading an open
       one reads names and types the evaluator produces. The union's arm (`union_sound`, Phase 469,
       D36) reads the evaluator's own per-column rule (`union_ty`); Phase 434's finding that the
       walk kept the left type where the evaluator widens it (D18) is closed, and its witness is
       restated as agreement (`union_walk_widens`).
     - `keeps_schema`, `union_keeps_names`: the row-set verbs and the filtering joins answer the
       input schema; the union answers its names.
     - THE SIX `*_no_dups` THEOREMS — no verb that names its output answers `Ok` with a name
       twice: `window_no_dups` (over distinct input names; the taken output name is refused, D28),
       `join_no_dups` (over two distinct-named operands; D20's suffix is free against the whole
       output), and, with no hypothesis on the input, `project_no_dups`, `group_by_no_dups`,
       `unpivot_no_dups` and `pivot_no_dups` (Phase 468, D35: `repeated_output` refuses the first
       name an earlier output took — for the pivot whatever the `on` column holds).
     - `project_names`, `group_by_names`, `unpivot_names`, `pivot_names_prefix`: what the other
       four verbs' names ARE — exactly the pipeline text's (`Project`'s output names; `GroupBy`'s
       keys then aggregate names; `Unpivot`'s id names then `variable` and `value`; a pivot's index
       names first). Phase 434 found that each of the four answered `Ok` with a repeated name and
       carried the finding as four witness theorems (`*_repeats_name`); Phase 468 closed it, and
       the same four witnesses are now refusals (`project_repeat_refused`,
       `group_by_repeat_refused`, `unpivot_repeat_refused`, `pivot_repeat_refused`), which the
       differential asserts on the shipped evaluator.
     - `*_unknown_refused`, eight theorems: every name a verb takes from the pipeline that the
       schema does not carry is refused with `UnknownColumn` naming it, in the arm's own order —
       the sort's keys after its slots resolve (412's class), the join's left then right keys, the
       window's read column then partition then order keys, the pivot's index then `on` then
       `values`, the unpivot's ids then values, the project's sources, the group-by's keys then
       aggregate sources.
     - `other_of_schema`, `other_of_unreached`: the twelve-verb evaluator `other_of` builds from
       `step_schema` and a row function answers a frame whose schema IS `step_schema`'s, or an
       error `step_schema` or the row function raised; asked a `Filter` or a `Derive`, which
       `Pipeline.eval_step` never does, it refuses.

   WHAT IS NOT CLAIMED. Anything about the rows a verb produces. Anything about a `Filter` or a
   `Derive` (Pipeline's clauses; `walk`'s two arms for them are modelled so that the walk is whole,
   and `walk_sound` covers the filter through `step_schema`'s own `Filter` arm). The text of a
   walk's `AtLeast` REASON (prose; the knowledge's shape and columns are what a reader acts on).
   Anything about units. That the resolver resolves what the walk's sources declare — assumed.

   HOW TO READ IT. Opens `Pipeline` and reuses its definitions by name (`schema`, `frame`, the
   verbs, `eval_error`, `assoc`, `index_of`, `names`, `app`, `type_of`, `widens`, `type_tag`,
   `infer_type`, `column_type_by`, `derived_typing_of`, `float_beside_decimal`, `wf`, `wframe`,
   and — for the union's type join and its refusal (Phase 476) — the cells' typing's
   `disjoint_types`, `widen_type` and `types_beside`), and re-models nothing it models. Every definition names its F# counterpart. Every type
   parameter is `Type0`; default context, no scoped options.
*)
module Relational

open Pipeline

(* ======================================================================================
   1. List helpers `Pipeline` does not carry (its `mem` is `noextract`, so one is restated here).
   ====================================================================================== *)

(* F#: `List.contains`. *)
let rec has (n:string) (l:list string) : Tot bool =
  match l with
  | [] -> false
  | h :: t -> h = n || has n t

(* Pairwise-distinct names. *)
let rec no_dups (l:list string) : Tot bool =
  match l with
  | [] -> true
  | h :: t -> not (has h t) && no_dups t

(* F#: `List.map snd`. *)
let rec snds (#a #b:Type0) (l:list (a & b)) : Tot (list b) =
  match l with
  | [] -> []
  | (_, y) :: t -> y :: snds t

(* F#: `unknownKey cols names` / `List.tryPick` over resolved names — the FIRST name the schema
   does not carry, in the names' order. *)
let rec first_unknown (cols:schema) (ns:list string) : Tot (option string) =
  match ns with
  | [] -> None
  | n :: t -> if None? (index_of n cols) then Some n else first_unknown cols t

(* F#: `names |> List.map (fun n -> f.Cols |> List.find (fun fd -> fd.Name = n))`, over names the
   caller has checked the schema carries (a name it does not is skipped, never reached). *)
let rec lookup_cols (cols:schema) (ns:list string) : Tot schema =
  match ns with
  | [] -> []
  | n :: t ->
    (match assoc n cols with
     | Some ty -> (n, ty) :: lookup_cols cols t
     | None -> lookup_cols cols t)

(* F#: the private `cellShape` — a slot refusal's account of the cell it found. *)
let cell_shape (c:cell) : Tot string =
  match type_of c with
  | Some t -> type_tag t
  | None -> "null"

(* F#: `joinColumnType` — the join of two column types where `widens` relates them, and nothing
   where it relates neither way. Read through the cells' typing's own two definitions by name
   (Phase 476): `Pipeline.disjoint_types` is `typeFromCells`' test of a pair no column type holds
   together, and `Pipeline.widen_type` its join, so the union and a derived column refuse one pair
   by one rule. *)
let join_column_type (a b:column_type) : Tot (option column_type) =
  if disjoint_types a b then None else Some (widen_type a b)

(* F#: `Column.aggType fn srcType` (the substrate's). *)
let agg_type (fn:agg_fn) (src:column_type) : Tot column_type =
  match fn with
  | Count | CountDistinct -> IntType
  | Mean | Median | StdDev -> FloatType
  | Sum | Min | Max | First | Last -> src

(* F#: `unpivotTyping types` (Phase 338). *)
let rec join_fold (acc:option (option column_type)) (types:list column_type)
  : Tot (option (option column_type)) (decreases types) =
  match types with
  | [] -> acc
  | t :: rest ->
    let acc' =
      match acc with
      | None -> Some (Some t)
      | Some (Some a) -> Some (join_column_type a t)
      | Some None -> Some None in
    join_fold acc' rest

let rec has_ty (ty:column_type) (types:list column_type) : Tot bool =
  match types with
  | [] -> false
  | t :: rest -> t = ty || has_ty ty rest

let unpivot_typing (types:list column_type) : Tot derived_typing =
  if has_ty FloatType types && has_ty DecimalType types then Refused
  else
    match join_fold None types with
    | None -> Decided StringType
    | Some (Some ty) -> Decided ty
    | Some None -> ByCells

(* ======================================================================================
   2. The parameters: the relational primitives and the resolver.
   ====================================================================================== *)

(* THE PRIMITIVES. See the header for what each is and what is assumed of it. *)
noeq type rprims = {
  cell_string : cell -> string;
  int_text    : int -> string;
  string_lt   : string -> string -> bool;
  candidate   : string -> int -> string;
}

(* The suffix rule is injective in `n` for a fixed name: what `join_no_dups` assumes of it. *)
let cand_injective (rp:rprims) : prop =
  forall (name:string) (i j:int). i <> j ==> rp.candidate name i <> rp.candidate name j

(* F#: the `resolve` closure `evalStepWith` closes over, read through `evalSource`. *)
type resolver = string -> outcome wframe eval_error

(* F#: `evalSource resolve src`, as the schema reads it. *)
let source_cols (resolve:resolver) (src:data_source) : Tot (outcome schema eval_error) =
  match src with
  | Embedded f -> Ok f.cols
  | Ref name ->
    (match resolve name with
     | Ok f -> Ok f.cols
     | Error e -> Error e)

(* ======================================================================================
   3. The slots — `resolveIntSlot` / `resolveStrSlot` (`0.23.0`), then `sequenceR`.
   ====================================================================================== *)

(* F#: `resolveIntSlot env slot s`. *)
let resolve_int_slot (env:param_env) (label:string) (s:slot int) : Tot (outcome int eval_error) =
  match s with
  | SlotLit v -> Ok v
  | SlotParam n ->
    (match assoc n env with
     | None -> Error (UnboundParam n (names env))
     | Some (Int v) -> Ok v
     | Some other ->
       Error (TypeError (label ^ ": param '" ^ n ^ "' is bound to a " ^ cell_shape other
                         ^ " cell, not an integer")))

(* F#: `resolveStrSlot env slot s`. *)
let resolve_str_slot (env:param_env) (label:string) (s:slot string) : Tot (outcome string eval_error) =
  match s with
  | SlotLit v -> Ok v
  | SlotParam n ->
    (match assoc n env with
     | None -> Error (UnboundParam n (names env))
     | Some (Str v) -> Ok v
     | Some other ->
       Error (TypeError (label ^ ": param '" ^ n ^ "' is bound to a " ^ cell_shape other
                         ^ " cell, not a string")))

(* F#: the `Sort` arm's `List.map (resolveStrSlot …) |> sequenceR` — the first error, in key order. *)
let rec resolve_sort_keys (env:param_env) (sk:list (slot string & sort_dir))
  : Tot (outcome (list (string & sort_dir)) eval_error) =
  match sk with
  | [] -> Ok []
  | (c, d) :: rest ->
    (match resolve_str_slot env "sort key column" c with
     | Error e -> Error e
     | Ok c' ->
       (match resolve_sort_keys env rest with
        | Error e -> Error e
        | Ok rest' -> Ok ((c', d) :: rest')))

(* ======================================================================================
   4. The verbs' schema computations and refusals, one function per arm.
   ====================================================================================== *)

(* F#: `DataFrame.repeatedOutput` (Phase 468, D35) — THE ONE CHECK every verb that names its output
   asks: `outs` is each output column's name beside what produced it, and the first name an earlier
   output took is refused with a `TypeError` naming the verb, the name and both producers. *)
let repeat_text (verb name first who:string) : Tot string =
  verb ^ " output '" ^ name ^ "' is named twice, by " ^ first ^ " and by " ^ who ^ "; give each output a name of its own"

let rec repeat_in (verb:string) (seen outs:list (string & string)) : Tot (option eval_error) (decreases outs) =
  match outs with
  | [] -> None
  | (n, who) :: t ->
    (match assoc n seen with
     | Some first -> Some (TypeError (repeat_text verb n first who))
     | None -> repeat_in verb ((n, who) :: seen) t)

let repeated_output (verb:string) (outs:list (string & string)) : Tot (option eval_error) =
  repeat_in verb [] outs

(* The producers each verb names, in output order (F#: `projectRefusal`, `groupByRefusal`,
   `unpivotRefusal`, `pivotRefusal`). *)
let rec project_outs (pairs:list (string & string)) : Tot (list (string & string)) =
  match pairs with
  | [] -> []
  | (src, out) :: t -> (out, "the projection of '" ^ src ^ "'") :: project_outs t

let rec key_outs (keys:list string) : Tot (list (string & string)) =
  match keys with
  | [] -> []
  | k :: t -> (k, "the key '" ^ k ^ "'") :: key_outs t

let rec agg_outs (aggs:list agg) : Tot (list (string & string)) =
  match aggs with
  | [] -> []
  | a :: t -> (a.a_name, "the aggregate over '" ^ a.a_of ^ "'") :: agg_outs t

let rec id_outs (ids:list string) : Tot (list (string & string)) =
  match ids with
  | [] -> []
  | n :: t -> (n, "the id column '" ^ n ^ "'") :: id_outs t

let unpivot_outs (ids:list string) : Tot (list (string & string)) =
  app (id_outs ids) [("variable", "the unpivot's variable column"); ("value", "the unpivot's value column")]

let rec index_outs (ns:list string) : Tot (list (string & string)) =
  match ns with
  | [] -> []
  | n :: t -> (n, "the index column '" ^ n ^ "'") :: index_outs t

let rec value_outs (vs:schema) : Tot (list (string & string)) =
  match vs with
  | [] -> []
  | (n, _) :: t -> (n, "the column for the on value '" ^ n ^ "'") :: value_outs t

(* F#: `evalProject` — each pair's source resolved in order, the first unknown refused; the
   output is the pairs' output names under the sources' types. *)
let rec project_cols (cols:schema) (pairs:list (string & string)) : Tot (outcome schema eval_error) =
  match pairs with
  | [] -> Ok []
  | (src, out) :: rest ->
    (match assoc src cols with
     | None -> Error (UnknownColumn src (names cols))
     | Some ty ->
       (match project_cols cols rest with
        | Error e -> Error e
        | Ok tail -> Ok ((out, ty) :: tail)))

(* F#: `evalProject` whole — the sources, then the output names (Phase 468, D35). *)
let project_step (cols:schema) (pairs:list (string & string)) : Tot (outcome schema eval_error) =
  match project_cols cols pairs with
  | Error e -> Error e
  | Ok out ->
    (match repeated_output "project" (project_outs pairs) with
     | Some e -> Error e
     | None -> Ok out)

(* F#: `evalGroupBy`'s `resolveAgg` over the aggregates — the first unknown source refused; each
   aggregate's column is its name under `aggType`. *)
let rec agg_cols (cols:schema) (aggs:list agg) : Tot (outcome schema eval_error) =
  match aggs with
  | [] -> Ok []
  | a :: rest ->
    (match assoc a.a_of cols with
     | None -> Error (UnknownColumn a.a_of (names cols))
     | Some ty ->
       (match agg_cols cols rest with
        | Error e -> Error e
        | Ok tail -> Ok ((a.a_name, agg_type a.a_fn ty) :: tail)))

(* F#: `evalGroupBy f keys aggs` — the first unknown key, then the aggregates, then the output
   names (Phase 468, D35); the key columns then the aggregate columns. *)
let group_by_cols (cols:schema) (keys:list string) (aggs:list agg) : Tot (outcome schema eval_error) =
  match first_unknown cols keys with
  | Some k -> Error (UnknownColumn k (names cols))
  | None ->
    (match agg_cols cols aggs with
     | Error e -> Error e
     | Ok ac ->
       (match repeated_output "groupBy" (app (key_outs keys) (agg_outs aggs)) with
        | Some e -> Error e
        | None -> Ok (app (lookup_cols cols keys) ac)))

(* F#: `joinKeyIdx`'s refusal — the first unresolvable name, left names then right names, with
   both schemas' names listed. *)
let join_key_refusal (lcols rcols:schema) (on:list (string & string)) : Tot (option eval_error) =
  match first_unknown lcols (names on) with
  | Some n -> Some (UnknownColumn n (app (names lcols) (names rcols)))
  | None ->
    (match first_unknown rcols (snds on) with
     | Some n -> Some (UnknownColumn n (app (names lcols) (names rcols)))
     | None -> None)

(* F#: the `free` loop inside `joinRightNamesOf` — the first candidate suffix not taken. The loop
   has no bound in production; here it carries a fuel every caller sets to one more than the taken
   list's length, which `free_fresh` proves is enough for an injective rule (there are at most as
   many taken candidates as taken names). On exhaustion it answers the last candidate, which an
   injective rule never reaches. *)
let rec free (rp:rprims) (name:string) (taken:list string) (fuel:nat) (n:int)
  : Tot string (decreases fuel) =
  let c = rp.candidate name n in
  if fuel = 0 then c
  else if has c taken then free rp name taken (fuel - 1) (n + 1)
  else c

(* F#: `joinRightNamesOf`'s `mapFold` — each right name in order: kept when no left column carries
   it, otherwise the first free suffix, which is then taken. *)
let rec rename_right (rp:rprims) (lnames:list string) (taken:list string) (rnames:list string)
  : Tot (list string) (decreases rnames) =
  match rnames with
  | [] -> []
  | r :: rest ->
    if has r lnames then
      let c = free rp r taken (len taken + 1) 1 in
      c :: rename_right rp lnames (c :: taken) rest
    else r :: rename_right rp lnames taken rest

(* F#: `DataFrame.joinRightNames leftNames rightNames` — the fold seeded with every left and
   every right name taken (`Set.union leftSet (Set.ofList rightNames)`). *)
let join_right_names (rp:rprims) (lnames rnames:list string) : Tot (list string) =
  rename_right rp lnames (app lnames rnames) rnames

(* F#: `List.map2 (fun col name -> Fields.rename name col) rightCols` — the right columns under
   their output names. *)
let rec renamed (ns:list string) (cols:schema) : Tot schema =
  match ns, cols with
  | n :: nt, (_, ty) :: ct -> (n, ty) :: renamed nt ct
  | _, _ -> []

(* F#: `evalJoin`'s output schema after `joinKeyIdx` — the left schema for a filtering join, left
   columns then the renamed right columns for a combining one. *)
let join_cols (rp:rprims) (lcols rcols:schema) (on:list (string & string)) (how:join_kind)
  : Tot (outcome schema eval_error) =
  match join_key_refusal lcols rcols on with
  | Some e -> Error e
  | None ->
    (match how with
     | Semi | Anti -> Ok lcols
     | _ -> Ok (app lcols (renamed (join_right_names rp (names lcols) (names rcols)) rcols)))

(* F#: `windowReadsOf fn`. *)
let window_reads_of (fn:window_fn) : Tot bool =
  match fn with
  | RowNumber | Rank | DenseRank | CompetitionRank | NTile _ -> false
  | Lag | Lead | CumulSum | CumulMax | CumulMin | RollingMean | RollingSum -> true

(* F#: the output type `windowColumnOverWith` gives the window's column. *)
let window_type (cols:schema) (spec:window_spec) : Tot column_type =
  match spec.w_fn with
  | RowNumber | Rank | DenseRank | CompetitionRank | NTile _ -> IntType
  | CumulSum | RollingSum -> if assoc spec.w_of cols = Some DecimalType then DecimalType else FloatType
  | RollingMean -> FloatType
  | Lag | Lead | CumulMax | CumulMin ->
    (match assoc spec.w_of cols with
     | Some t -> t
     | None -> StringType)

(* F#: `windowRefusal cols spec` (Phases 441, 450; D26, D28), in its order. *)
let window_refusal (rp:rprims) (cols:schema) (spec:window_spec) : Tot (option eval_error) =
  let keys () =
    match first_unknown cols (app spec.partition_by (names spec.order_by)) with
    | Some n -> Some (UnknownColumn n (names cols))
    | None ->
      if Some? (index_of spec.w_as cols) then
        Some (TypeError ("window output '" ^ spec.w_as
                         ^ "' names a column the input already carries; name the window's column something else, or project the existing one away first"))
      else None in
  match spec.w_fn with
  | NTile b ->
    if b < 1 then Some (TypeError ("ntile expects at least 1 bucket, got " ^ rp.int_text b))
    else keys ()
  | fn ->
    if window_reads_of fn && None? (index_of spec.w_of cols) then Some (UnknownColumn spec.w_of (names cols))
    else keys ()

(* F#: `evalWindow` — refused by the spec check, else the column appended. *)
let window_cols (rp:rprims) (cols:schema) (spec:window_spec) : Tot (outcome schema eval_error) =
  match window_refusal rp cols spec with
  | Some e -> Error e
  | None -> Ok (app cols [(spec.w_as, window_type cols spec)])

(* F#: `List.tryItem` — restated because Pipeline's `nth_opt` is `noextract`. *)
let rec at_opt (#a:Type0) (l:list a) (j:nat) : Tot (option a) (decreases l) =
  match l with
  | [] -> None
  | x :: t -> if j = 0 then Some x else at_opt t (j - 1)

(* The cells of column `i` down the rows (`Null` past a row's end, which a well-formed frame has
   none of). *)
let rec col_cells (i:nat) (rows:list (list cell)) : Tot (list cell) =
  match rows with
  | [] -> []
  | r :: t ->
    (match at_opt r i with
     | Some c -> c :: col_cells i t
     | None -> Null :: col_cells i t)

(* F#: `Array.filter (fun c -> not (Cell.isNull c))`. *)
let rec present (cs:list cell) : Tot (list cell) =
  match cs with
  | [] -> []
  | Null :: t -> present t
  | c :: t -> c :: present t

let rec remove_all (c:cell) (cs:list cell) : Tot (list cell) =
  match cs with
  | [] -> []
  | x :: t -> if x = c then remove_all c t else x :: remove_all c t

let rec len_remove_all (c:cell) (cs:list cell) : Lemma (ensures len (remove_all c cs) <= len cs) =
  match cs with
  | [] -> ()
  | _ :: t -> len_remove_all c t

(* F#: `List.distinct` — first appearances kept, in order. *)
let rec distinct (cs:list cell) : Tot (list cell) (decreases (len cs)) =
  match cs with
  | [] -> []
  | c :: t -> len_remove_all c t; c :: distinct (remove_all c t)

(* F#: `List.sortBy cellString` — a stable sort by canonical text, as an insertion sort under the
   parameter order. *)
let rec insert_by (rp:rprims) (c:cell) (sorted:list cell) : Tot (list cell) =
  match sorted with
  | [] -> [c]
  | h :: t -> if rp.string_lt (rp.cell_string c) (rp.cell_string h) then c :: h :: t else h :: insert_by rp c t

let rec sort_by (rp:rprims) (cs:list cell) : Tot (list cell) =
  match cs with
  | [] -> []
  | c :: t -> insert_by rp c (sort_by rp t)

(* F#: `evalPivot`'s `onValues` — the distinct present `on` cells in canonical text order. Since
   Phase 468 (D35) the host keeps one on-value of each text among the cells `cellEq` equates (two
   `NaN`s were two identical columns of one name); the model's cell equality reads a float by its
   round-trip text, so its `distinct` already keeps one `NaN`, and the two agree on every column of
   one cell kind. *)
let on_values (rp:rprims) (i:nat) (rows:list (list cell)) : Tot (list cell) =
  sort_by rp (distinct (present (col_cells i rows)))

let rec pivot_value_cols (rp:rprims) (ty:column_type) (vs:list cell) : Tot schema =
  match vs with
  | [] -> []
  | v :: t -> (rp.cell_string v, ty) :: pivot_value_cols rp ty t

(* F#: `evalPivot f spec` — index names then `on` then `values` refused in that order, then the
   output names (Phase 468, D35: the value columns' names are the data's, read here); the index
   columns then one column per `on` value under `aggType`. *)
let pivot_cols (rp:rprims) (cols:schema) (rows:list (list cell)) (spec:pivot_spec)
  : Tot (outcome schema eval_error) =
  match first_unknown cols spec.p_index with
  | Some n -> Error (UnknownColumn n (names cols))
  | None ->
    (match index_of spec.p_on cols with
     | None -> Error (UnknownColumn spec.p_on (names cols))
     | Some on_i ->
       (match assoc spec.p_values cols with
        | None -> Error (UnknownColumn spec.p_values (names cols))
        | Some vty ->
          let vals = pivot_value_cols rp (agg_type spec.p_agg vty) (on_values rp on_i rows) in
          (match repeated_output "pivot" (app (index_outs spec.p_index) (value_outs vals)) with
           | Some e -> Error e
           | None -> Ok (app (lookup_cols cols spec.p_index) vals))))

(* The value columns' indexes, each known to resolve. *)
let rec indexes_of (cols:schema) (ns:list string) : Tot (list nat) =
  match ns with
  | [] -> []
  | n :: t ->
    (match index_of n cols with
     | Some i -> i :: indexes_of cols t
     | None -> indexes_of cols t)

let rec cells_at (r:list cell) (is:list nat) : Tot (list cell) =
  match is with
  | [] -> []
  | i :: t ->
    (match at_opt r i with
     | Some c -> c :: cells_at r t
     | None -> Null :: cells_at r t)

(* F#: the melted `value` cells in the order `evalUnpivot` emits them — row by row, each row's
   value columns in `valueVars` order. *)
let rec melt (is:list nat) (rows:list (list cell)) : Tot (list cell) =
  match rows with
  | [] -> []
  | r :: t -> app (cells_at r is) (melt is t)

(* F#: `evalUnpivot f idVars valueVars` — ids then values refused, then the output names (Phase
   468, D35); the id columns, `variable` as a string, `value` by the derived-column rule over the
   value columns' types and, where those leave it to the cells, the melted cells. *)
let unpivot_cols (cols:schema) (rows:list (list cell)) (ids vals:list string)
  : Tot (outcome schema eval_error) =
  match first_unknown cols ids with
  | Some n -> Error (UnknownColumn n (names cols))
  | None ->
    (match first_unknown cols vals with
     | Some n -> Error (UnknownColumn n (names cols))
     | None ->
       (match repeated_output "unpivot" (unpivot_outs ids) with
        | Some e -> Error e
        | None ->
          let dt = unpivot_typing (snds (lookup_cols cols vals)) in
          (match dt with
           | Refused -> Error (float_beside_decimal "value")
           | _ ->
             (match column_type_by dt "value" (melt (indexes_of cols vals) rows) with
              | Error e -> Error e
              | Ok vty -> Ok (app (lookup_cols cols ids) [("variable", StringType); ("value", vty)])))))

(* F#: `unionField`'s type (Phase 469, D36) — the left type where the two agree, their join where
   one exists, and `None` where no column type holds the two. The evaluator's fold (`union_types`)
   and the walk's union arm (`union_k`) both read this one function, as F#'s `evalUnion` and
   `SchemaWalk.ofTransform` both read `unionField`. *)
let union_ty (lt rt:column_type) : Tot (option column_type) =
  if lt = rt then Some lt else join_column_type lt rt

(* F#: `evalUnion`'s per-column fold after the names agree — `union_ty`, refused by name where it
   answers `None`. *)
let rec union_types (l r:schema) : Tot (outcome schema eval_error) =
  match l, r with
  | (n, lt) :: lrest, (_, rt) :: rrest ->
    (match union_ty lt rt with
     | None -> Error (types_beside n lt rt)
     | Some ty ->
       (match union_types lrest rrest with
        | Error e -> Error e
        | Ok rest -> Ok ((n, ty) :: rest)))
  | _, _ -> Ok []

(* F#: `evalUnion f other`. *)
let union_cols (l r:schema) : Tot (outcome schema eval_error) =
  if names l = names r then union_types l r
  else Error (JoinError "union requires matching column names")

(* F#: `evalSetOp verb _ f other` — the left schema once the names agree. *)
let set_op_cols (verb:string) (l r:schema) : Tot (outcome schema eval_error) =
  if names l = names r then Ok l
  else Error (JoinError (verb ^ " requires matching column names"))

(* ======================================================================================
   5. The dispatch — `evalStepWith`'s twelve arms, read for their schema and refusals. A `Filter`
      keeps the schema (Pipeline's own clause says so); a `Derive` is Pipeline's clause and not
      this model's, and is refused here so that the dispatch is total.
   ====================================================================================== *)

let step_schema (rp:rprims) (resolve:resolver) (env:param_env) (f:wframe) (t:transform)
  : Tot (outcome schema eval_error) =
  match t with
  | Filter _ -> Ok f.cols
  | Derive _ _ -> Error (TypeError "Derive is Pipeline.fst's clause, not the relational model's")
  | Project pairs -> project_step f.cols pairs
  | GroupBy keys aggs -> group_by_cols f.cols keys aggs
  | Join src on how ->
    (match source_cols resolve src with
     | Error e -> Error e
     | Ok rcols -> join_cols rp f.cols rcols on how)
  | Window spec -> window_cols rp f.cols spec
  | Pivot spec -> pivot_cols rp f.cols f.rows spec
  | Unpivot ids vals -> unpivot_cols f.cols f.rows ids vals
  | Sort sk ->
    (match resolve_sort_keys env sk with
     | Error e -> Error e
     | Ok keys ->
       (match first_unknown f.cols (names keys) with
        | Some n -> Error (UnknownColumn n (names f.cols))
        | None -> Ok f.cols))
  | Distinct -> Ok f.cols
  | Limit n off ->
    (match resolve_int_slot env "limit n" n with
     | Error e -> Error e
     | Ok _ ->
       (match resolve_int_slot env "limit offset" off with
        | Error e -> Error e
        | Ok _ -> Ok f.cols))
  | Union src ->
    (match source_cols resolve src with
     | Error e -> Error e
     | Ok rcols -> union_cols f.cols rcols)
  | Intersect src ->
    (match source_cols resolve src with
     | Error e -> Error e
     | Ok rcols -> set_op_cols "intersect" f.cols rcols)
  | Except src ->
    (match source_cols resolve src with
     | Error e -> Error e
     | Ok rcols -> set_op_cols "except" f.cols rcols)

(* ======================================================================================
   6. The narrowing of Pipeline's twelve-verb parameter: an `other_fn` built from the schema
      model and a row function — the rows, and only the rows, stay assumed.
   ====================================================================================== *)

(* THE ROW FUNCTION: the rows a verb answers under the schema the model computed, as wide as that
   schema, or a refusal that read the cells (`AggError`, …). *)
type row_fn = f:wframe -> t:transform -> out:schema -> outcome (rs:list (list cell){all_width (len out) rs}) eval_error

(* One step of it, as a function of the frame and the verb; `other_of` is its partial application. *)
let other_step (rp:rprims) (resolve:resolver) (env:param_env) (rows:row_fn) (f:wframe) (t:transform)
  : Tot (outcome wframe eval_error) =
  match step_schema rp resolve env f t with
  | Error e -> Error e
  | Ok out ->
    (match rows f t out with
     | Error e -> Error e
     | Ok rs -> Ok ({ cols = out; rows = rs }))

let other_of (rp:rprims) (resolve:resolver) (env:param_env) (rows:row_fn) : other_fn =
  other_step rp resolve env rows

let other_of_schema (rp:rprims) (resolve:resolver) (env:param_env) (rows:row_fn) (f:wframe) (t:transform)
  : Lemma (ensures (match other_step rp resolve env rows f t with
                    | Ok g -> step_schema rp resolve env f t == Ok g.cols
                    | Error e -> step_schema rp resolve env f t == Error e
                               \/ (exists (out:schema). step_schema rp resolve env f t == Ok out
                                                   /\ rows f t out == Error e))) =
  match step_schema rp resolve env f t with
  | Error _ -> ()
  | Ok out ->
    (match rows f t out with
     | Error e -> assert (step_schema rp resolve env f t == Ok out /\ rows f t out == Error e)
     | Ok _ -> ())

(* `eval_step` hands its parameter no `Filter` and no `Derive`, and `other_of` refuses a `Derive`
   it is handed anyway: the two clauses stay Pipeline's. *)
let other_of_unreached (rp:rprims) (resolve:resolver) (env:param_env) (rows:row_fn) (f:wframe) (name:string) (x:col_expr)
  : Lemma (ensures Error? (other_step rp resolve env rows f (Derive name x))) = ()

(* ======================================================================================
   7. The static walk — `SchemaWalk.ofTransform`, over `SchemaKnowledge`.
   ====================================================================================== *)

(* F#: `ColumnKnowledge` — a name and the type the walk knows, if any. *)
type col_knowledge = string & option column_type

(* F#: `SchemaKnowledge` — `Closed cols` / `AtLeast (cols, reason)`; the reason is prose and not
   carried here. *)
type knowledge =
  | Closed  : list col_knowledge -> knowledge
  | AtLeast : list col_knowledge -> knowledge

(* F#: the `sources: string -> Schema option` a walk is given. *)
type sources = string -> option schema

(* F#: `SchemaWalk.columns`. *)
let columns (k:knowledge) : Tot (list col_knowledge) =
  match k with
  | Closed cs -> cs
  | AtLeast cs -> cs

(* F#: `withColumns`. *)
let with_columns (cs:list col_knowledge) (k:knowledge) : Tot knowledge =
  match k with
  | Closed _ -> Closed cs
  | AtLeast _ -> AtLeast cs

(* F#: `typeOf name knowledge`. *)
let type_of_k (name:string) (k:knowledge) : Tot (option column_type) =
  match assoc name (columns k) with
  | Some t -> t
  | None -> None

(* F#: `known name knowledge` — the column, or one of that name the walk knows nothing about. *)
let known (name:string) (k:knowledge) : Tot col_knowledge = (name, type_of_k name k)

(* F#: `ColumnKnowledge.renamed out c`. *)
let rec known_each (ns:list string) (k:knowledge) : Tot (list col_knowledge) =
  match ns with
  | [] -> []
  | n :: t -> known n k :: known_each t k

let rec renamed_known (pairs:list (string & string)) (k:knowledge) : Tot (list col_knowledge) =
  match pairs with
  | [] -> []
  | (src, out) :: t -> (out, type_of_k src k) :: renamed_known t k

(* F#: `ofColumns` / `ofSchema`. *)
let rec known_of (s:schema) : Tot (list col_knowledge) =
  match s with
  | [] -> []
  | (n, ty) :: t -> (n, Some ty) :: known_of t

(* F#: `ofSource sources source`. *)
let of_source (srcs:sources) (src:data_source) : Tot knowledge =
  match src with
  | Embedded f -> Closed (known_of f.cols)
  | Ref name ->
    (match srcs name with
     | Some s -> Closed (known_of s)
     | None -> AtLeast [])

(* F#: `upsert column knowledge` — every column of the name retyped in place, else appended. *)
let rec replace_named (c:col_knowledge) (cs:list col_knowledge) : Tot (list col_knowledge) =
  match cs with
  | [] -> []
  | (n, t) :: rest -> (if n = fst c then c else (n, t)) :: replace_named c rest

let upsert (c:col_knowledge) (k:knowledge) : Tot knowledge =
  let cs = columns k in
  if Some? (assoc (fst c) cs) then with_columns (replace_named c cs) k
  else with_columns (app cs [c]) k

(* F#: `aggregateType fn sourceType`. *)
let aggregate_type (fn:agg_fn) (src:option column_type) : Tot (option column_type) =
  match src with
  | Some ty -> Some (agg_type fn ty)
  | None ->
    (match fn with
     | Count | CountDistinct -> Some IntType
     | Mean | Median | StdDev -> Some FloatType
     | Sum | Min | Max | First | Last -> None)

let rec agg_known (aggs:list agg) (k:knowledge) : Tot (list col_knowledge) =
  match aggs with
  | [] -> []
  | a :: t -> (a.a_name, aggregate_type a.a_fn (type_of_k a.a_of k)) :: agg_known t k

(* F#: `windowType input spec`. *)
let window_type_k (k:knowledge) (spec:window_spec) : Tot (option column_type) =
  match spec.w_fn with
  | RowNumber | Rank | DenseRank | CompetitionRank | NTile _ -> Some IntType
  | RollingMean -> Some FloatType
  | CumulSum | RollingSum ->
    (match type_of_k spec.w_of k with
     | Some DecimalType -> Some DecimalType
     | Some _ -> Some FloatType
     | None -> None)
  | Lag | Lead | CumulMax | CumulMin -> type_of_k spec.w_of k

(* F#: `columns input |> List.choose ColumnKnowledge.toField` — the typed columns, as a schema. *)
let rec known_fields (cs:list col_knowledge) : Tot schema =
  match cs with
  | [] -> []
  | (n, Some t) :: rest -> (n, t) :: known_fields rest
  | (_, None) :: rest -> known_fields rest

(* F#: `derivedColumnType cols e` (Phase 338). *)
let derived_column_type (cols:schema) (x:col_expr) : Tot (option column_type) =
  match derived_typing_of cols x with
  | Decided ty -> Some ty
  | _ -> None

(* The walk's reading of the unpivot's value type: decided where every value column's type is
   known and they join. *)
let rec all_known (ns:list string) (k:knowledge) : Tot bool =
  match ns with
  | [] -> true
  | n :: t -> Some? (type_of_k n k) && all_known t k

let rec known_types (ns:list string) (k:knowledge) : Tot (list column_type) =
  match ns with
  | [] -> []
  | n :: t ->
    (match type_of_k n k with
     | Some ty -> ty :: known_types t k
     | None -> known_types t k)

let unpivot_type_k (vals:list string) (k:knowledge) : Tot (option column_type) =
  if all_known vals k then
    (match unpivot_typing (known_types vals k) with
     | Decided ty -> Some ty
     | _ -> None)
  else None

(* F#: the combining join's open-right arm — the right columns that keep their own name. *)
let rec kept_right (lnames:list string) (rcs:list col_knowledge) : Tot (list col_knowledge) =
  match rcs with
  | [] -> []
  | (n, t) :: rest -> if has n lnames then kept_right lnames rest else (n, t) :: kept_right lnames rest

(* F#: `List.map2 (fun c name -> ColumnKnowledge.renamed name c) rightCols`. *)
let rec renamed_k (ns:list string) (cs:list col_knowledge) : Tot (list col_knowledge) =
  match ns, cs with
  | n :: nt, (_, t) :: ct -> (n, t) :: renamed_k nt ct
  | _, _ -> []

(* F#: `unionColumn`'s type (Phase 469, D36) — `union_ty` where both sides' types are known,
   unknown where either is not, and unknown for a pair the evaluator refuses (the walk carries no
   refusal verdict, D26). *)
let union_k (lt rt:option column_type) : Tot (option column_type) =
  match lt, rt with
  | Some a, Some b -> union_ty a b
  | _, _ -> None

(* F#: the union arm over two closed operands — zipped by position, as `evalUnion` zips them. *)
let rec union_known_pos (l r:list col_knowledge) : Tot (list col_knowledge) =
  match l, r with
  | (n, lt) :: lrest, (_, rt) :: rrest -> (n, union_k lt rt) :: union_known_pos lrest rrest
  | _, _ -> []

(* F#: the union arm over an open left and a closed right — by name: each visible column meets the
   right's first column of its name. *)
let rec union_known_by_name (cs r:list col_knowledge) : Tot (list col_knowledge) =
  match cs with
  | [] -> []
  | (n, lt) :: rest ->
    (n, (match assoc n r with Some rt -> union_k lt rt | None -> lt)) :: union_known_by_name rest r

(* F#: the union arm over an undeclared right — every name stays, no type is known. *)
let rec untyped (cs:list col_knowledge) : Tot (list col_knowledge) =
  match cs with
  | [] -> []
  | (n, _) :: rest -> (n, None) :: untyped rest

(* F#: `SchemaWalk.ofTransform sources input step`, all fourteen arms. *)
let walk (rp:rprims) (srcs:sources) (k:knowledge) (t:transform) : Tot knowledge =
  match t with
  | Filter _ | Sort _ | Distinct | Limit _ _ | Intersect _ | Except _ -> k
  | Union src ->
    (match of_source srcs src, k with
     | AtLeast _, _ -> with_columns (untyped (columns k)) k
     | Closed rcs, Closed left -> if names left = names rcs then Closed (union_known_pos left rcs) else k
     | Closed rcs, AtLeast cs -> AtLeast (union_known_by_name cs rcs))
  | Project pairs -> Closed (renamed_known pairs k)
  | Derive name x -> upsert (name, derived_column_type (known_fields (columns k)) x) k
  | GroupBy keys aggs -> Closed (app (known_each keys k) (agg_known aggs k))
  | Window spec -> with_columns (app (columns k) [(spec.w_as, window_type_k k spec)]) k
  | Pivot spec -> AtLeast (known_each spec.p_index k)
  | Unpivot ids vals ->
    Closed (app (known_each ids k) [("variable", Some StringType); ("value", unpivot_type_k vals k)])
  | Join src _ how ->
    (match how with
     | Semi | Anti -> k
     | _ ->
       (match k with
        | AtLeast cs -> AtLeast cs
        | Closed left ->
          let right = of_source srcs src in
          let rcs = columns right in
          (match right with
           | Closed _ -> Closed (app left (renamed_k (join_right_names rp (names left) (names rcs)) rcs))
           | AtLeast _ -> AtLeast (app left (kept_right (names left) rcs)))))

(* ======================================================================================
   8. Soundness of the walk's knowledge for the evaluator's schema.
   ====================================================================================== *)

(* A closed knowledge is sound for a schema when it has the schema's names in order and every
   type it knows is the schema's. *)
let rec cols_agree (cs:list col_knowledge) (cols:schema) : Tot bool =
  match cs, cols with
  | [], [] -> true
  | (n, Some t) :: ct, (m, u) :: st -> n = m && t = u && cols_agree ct st
  | (n, None) :: ct, (m, _) :: st -> n = m && cols_agree ct st
  | _, _ -> false

(* An open knowledge is sound when every column it names is a column of the schema, with the
   type it knows where it knows one. *)
let rec at_least (cs:list col_knowledge) (cols:schema) : Tot bool =
  match cs with
  | [] -> true
  | (n, Some t) :: ct -> assoc n cols = Some t && at_least ct cols
  | (n, None) :: ct -> Some? (assoc n cols) && at_least ct cols

let sound (k:knowledge) (cols:schema) : Tot bool =
  match k with
  | Closed cs -> cols_agree cs cols
  | AtLeast cs -> at_least cs cols

(* The walk's sources and the evaluator's resolver agree: a declared schema is the resolved
   table's. *)
let sources_agree (srcs:sources) (resolve:resolver) : prop =
  forall (name:string). (match srcs name, resolve name with
                         | Some s, Ok f -> f.cols == s
                         | _, _ -> True)

(* ---- lemmas about assoc, names and agreement ---- *)

let rec cols_agree_names (cs:list col_knowledge) (cols:schema)
  : Lemma (requires cols_agree cs cols) (ensures names cs == names cols) =
  match cs, cols with
  | [], [] -> ()
  | _ :: ct, _ :: st -> cols_agree_names ct st

let rec cols_agree_assoc (cs:list col_knowledge) (cols:schema) (n:string)
  : Lemma (requires cols_agree cs cols)
          (ensures (match assoc n cs with
                    | Some (Some t) -> assoc n cols == Some t
                    | Some None -> Some? (assoc n cols)
                    | None -> assoc n cols == None)) =
  match cs, cols with
  | [], [] -> ()
  | (m, _) :: ct, _ :: st -> if m = n then () else cols_agree_assoc ct st n

let rec at_least_assoc (cs:list col_knowledge) (cols:schema) (n:string)
  : Lemma (requires at_least cs cols)
          (ensures (match assoc n cs with
                    | Some (Some t) -> assoc n cols == Some t
                    | Some None -> Some? (assoc n cols)
                    | None -> True)) =
  match cs with
  | [] -> ()
  | (m, _) :: ct -> if m = n then () else at_least_assoc ct cols n

(* The one direction every arm needs: a type the knowledge knows is the schema's. *)
let sound_type (k:knowledge) (cols:schema) (n:string)
  : Lemma (requires sound k cols)
          (ensures (match type_of_k n k with
                    | Some t -> assoc n cols == Some t
                    | None -> True)) =
  match k with
  | Closed cs -> cols_agree_assoc cs cols n
  | AtLeast cs -> at_least_assoc cs cols n

let rec cols_agree_app (a:list col_knowledge) (b:schema) (c:list col_knowledge) (d:schema)
  : Lemma (requires cols_agree a b /\ cols_agree c d) (ensures cols_agree (app a c) (app b d)) =
  match a, b with
  | [], [] -> ()
  | _ :: at, _ :: bt -> cols_agree_app at bt c d

let rec cols_agree_known_of (s:schema) : Lemma (ensures cols_agree (known_of s) s) =
  match s with
  | [] -> ()
  | _ :: t -> cols_agree_known_of t

let rec names_known_of (s:schema) : Lemma (ensures names (known_of s) == names s) =
  match s with
  | [] -> ()
  | _ :: t -> names_known_of t

let rec assoc_app_left (#b:Type0) (n:string) (l m:list (string & b))
  : Lemma (requires Some? (assoc n l)) (ensures assoc n (app l m) == assoc n l) =
  match l with
  | (k, _) :: t -> if k = n then () else assoc_app_left n t m

let rec assoc_app_absent (#b:Type0) (n:string) (l m:list (string & b))
  : Lemma (requires None? (assoc n l)) (ensures assoc n (app l m) == assoc n m) =
  match l with
  | [] -> ()
  | (k, _) :: t -> assoc_app_absent n t m

let rec index_of_none_assoc (#b:Type0) (n:string) (l:list (string & b))
  : Lemma (ensures None? (index_of n l) <==> None? (assoc n l)) =
  match l with
  | [] -> ()
  | (k, _) :: t -> if k = n then () else index_of_none_assoc n t

let rec first_unknown_none (cols:schema) (ns:list string)
  : Lemma (requires None? (first_unknown cols ns))
          (ensures (forall n. has n ns ==> Some? (assoc n cols))) =
  match ns with
  | [] -> ()
  | n :: t -> index_of_none_assoc n cols; first_unknown_none cols t

let rec at_least_app_left (cs:list col_knowledge) (cols more:schema)
  : Lemma (requires at_least cs cols) (ensures at_least cs (app cols more)) =
  match cs with
  | [] -> ()
  | (n, _) :: ct -> assoc_app_left n cols more; at_least_app_left ct cols more

let rec names_app (#b:Type0) (l m:list (string & b)) : Lemma (ensures names (app l m) == app (names l) (names m)) =
  match l with
  | [] -> ()
  | _ :: t -> names_app t m

let rec names_lookup (cols:schema) (ns:list string)
  : Lemma (requires None? (first_unknown cols ns)) (ensures names (lookup_cols cols ns) == ns) =
  match ns with
  | [] -> ()
  | n :: t -> index_of_none_assoc n cols; names_lookup cols t

(* ---- per-verb soundness ---- *)

let rec project_sound (k:knowledge) (cols:schema) (pairs:list (string & string))
  : Lemma (requires sound k cols /\ Ok? (project_cols cols pairs))
          (ensures cols_agree (renamed_known pairs k) (Ok?._0 (project_cols cols pairs))) =
  match pairs with
  | [] -> ()
  | (src, _) :: t -> sound_type k cols src; project_sound k cols t

let rec known_each_sound (k:knowledge) (cols:schema) (ns:list string)
  : Lemma (requires sound k cols /\ None? (first_unknown cols ns))
          (ensures cols_agree (known_each ns k) (lookup_cols cols ns)) =
  match ns with
  | [] -> ()
  | n :: t -> index_of_none_assoc n cols; sound_type k cols n; known_each_sound k cols t

let aggregate_type_sound (fn:agg_fn) (k:option column_type) (ty:column_type)
  : Lemma (requires (match k with Some t -> t == ty | None -> True))
          (ensures (match aggregate_type fn k with Some u -> u == agg_type fn ty | None -> True)) = ()

let rec agg_sound (k:knowledge) (cols:schema) (aggs:list agg)
  : Lemma (requires sound k cols /\ Ok? (agg_cols cols aggs))
          (ensures cols_agree (agg_known aggs k) (Ok?._0 (agg_cols cols aggs))) =
  match aggs with
  | [] -> ()
  | a :: t ->
    sound_type k cols a.a_of;
    aggregate_type_sound a.a_fn (type_of_k a.a_of k) (Some?.v (assoc a.a_of cols));
    agg_sound k cols t

let group_by_sound (k:knowledge) (cols:schema) (keys:list string) (aggs:list agg)
  : Lemma (requires sound k cols /\ Ok? (group_by_cols cols keys aggs))
          (ensures cols_agree (app (known_each keys k) (agg_known aggs k)) (Ok?._0 (group_by_cols cols keys aggs))) =
  known_each_sound k cols keys;
  agg_sound k cols aggs;
  cols_agree_app (known_each keys k) (lookup_cols cols keys) (agg_known aggs k) (Ok?._0 (agg_cols cols aggs))

let window_type_sound (k:knowledge) (cols:schema) (spec:window_spec)
  : Lemma (requires sound k cols)
          (ensures (match window_type_k k spec with
                    | Some u -> u == window_type cols spec
                    | None -> True)) =
  sound_type k cols spec.w_of

let rec at_least_snoc (cs:list col_knowledge) (out:schema) (c:col_knowledge)
  : Lemma (requires at_least cs out
                    /\ (match snd c with
                        | Some t -> assoc (fst c) out == Some t
                        | None -> Some? (assoc (fst c) out)))
          (ensures at_least (app cs [c]) out) =
  match cs with
  | [] -> ()
  | _ :: ct -> at_least_snoc ct out c

let window_sound (rp:rprims) (k:knowledge) (cols:schema) (spec:window_spec)
  : Lemma (requires sound k cols /\ Ok? (window_cols rp cols spec))
          (ensures sound (walk rp (fun _ -> None) k (Window spec)) (Ok?._0 (window_cols rp cols spec))) =
  window_type_sound k cols spec;
  let out = Ok?._0 (window_cols rp cols spec) in
  match k with
  | Closed cs -> cols_agree_app cs cols [(spec.w_as, window_type_k k spec)] [(spec.w_as, window_type cols spec)]
  | AtLeast cs ->
    at_least_app_left cs cols [(spec.w_as, window_type cols spec)];
    index_of_none_assoc spec.w_as cols;
    assoc_app_absent spec.w_as cols [(spec.w_as, window_type cols spec)];
    at_least_snoc cs out (spec.w_as, window_type_k k spec)

let rec at_least_known_each (k:knowledge) (cols:schema) (ns:list string) (out:schema)
  : Lemma (requires sound k cols /\ None? (first_unknown cols ns)
                    /\ (forall n. has n ns ==> assoc n out == assoc n cols))
          (ensures at_least (known_each ns k) out) =
  match ns with
  | [] -> ()
  | n :: t -> index_of_none_assoc n cols; sound_type k cols n; at_least_known_each k cols t out

let rec assoc_lookup (cols:schema) (ns:list string) (n:string)
  : Lemma (requires None? (first_unknown cols ns) /\ has n ns)
          (ensures assoc n (lookup_cols cols ns) == assoc n cols) =
  match ns with
  | m :: t -> index_of_none_assoc m cols; if m = n then () else assoc_lookup cols t n

let pivot_sound (rp:rprims) (k:knowledge) (f:wframe) (spec:pivot_spec)
  : Lemma (requires sound k f.cols /\ Ok? (pivot_cols rp f.cols f.rows spec))
          (ensures at_least (known_each spec.p_index k) (Ok?._0 (pivot_cols rp f.cols f.rows spec))) =
  let idx = lookup_cols f.cols spec.p_index in
  let out = Ok?._0 (pivot_cols rp f.cols f.rows spec) in
  let each (n:string) : Lemma (requires has n spec.p_index) (ensures assoc n out == assoc n f.cols) =
    assoc_lookup f.cols spec.p_index n;
    first_unknown_none f.cols spec.p_index;
    assoc_app_left n idx (pivot_value_cols rp (agg_type spec.p_agg (Some?.v (assoc spec.p_values f.cols))) (on_values rp (Some?.v (index_of spec.p_on f.cols)) f.rows)) in
  FStar.Classical.forall_intro (FStar.Classical.move_requires each);
  at_least_known_each k f.cols spec.p_index out

let rec known_types_sound (k:knowledge) (cols:schema) (vals:list string)
  : Lemma (requires sound k cols /\ all_known vals k /\ None? (first_unknown cols vals))
          (ensures known_types vals k == snds (lookup_cols cols vals)) =
  match vals with
  | [] -> ()
  | n :: t -> index_of_none_assoc n cols; sound_type k cols n; known_types_sound k cols t

let unpivot_sound (k:knowledge) (f:wframe) (ids vals:list string)
  : Lemma (requires sound k f.cols /\ Ok? (unpivot_cols f.cols f.rows ids vals))
          (ensures cols_agree (app (known_each ids k) [("variable", Some StringType); ("value", unpivot_type_k vals k)])
                              (Ok?._0 (unpivot_cols f.cols f.rows ids vals))) =
  known_each_sound k f.cols ids;
  let dt = unpivot_typing (snds (lookup_cols f.cols vals)) in
  let vty = Ok?._0 (column_type_by dt "value" (melt (indexes_of f.cols vals) f.rows)) in
  (if all_known vals k then known_types_sound k f.cols vals);
  cols_agree_app (known_each ids k) (lookup_cols f.cols ids)
    [("variable", Some StringType); ("value", unpivot_type_k vals k)]
    [("variable", StringType); ("value", vty)]

let rec renamed_len (ns:list string) (cols:schema)
  : Lemma (ensures len (renamed ns cols) == (if len ns <= len cols then len ns else len cols)) =
  match ns, cols with
  | _ :: nt, _ :: ct -> renamed_len nt ct
  | _, _ -> ()

let rec renamed_sound (ns:list string) (rcs:list col_knowledge) (rcols:schema)
  : Lemma (requires cols_agree rcs rcols) (ensures cols_agree (renamed_k ns rcs) (renamed ns rcols)) =
  match ns, rcs, rcols with
  | _ :: nt, _ :: kt, _ :: ct -> renamed_sound nt kt ct
  | _, _, _ -> ()

(* ---- the suffix search is fresh (the pigeonhole under injectivity) ---- *)

let rec remove_one (x:string) (l:list string) : Tot (list string) =
  match l with
  | [] -> []
  | h :: t -> if h = x then t else h :: remove_one x t

let rec len_remove_one (x:string) (l:list string)
  : Lemma (requires has x l) (ensures len (remove_one x l) == len l - 1) =
  match l with
  | h :: t -> if h = x then () else len_remove_one x t

let rec has_remove_one (x y:string) (l:list string)
  : Lemma (requires x <> y) (ensures has y (remove_one x l) == has y l) =
  match l with
  | [] -> ()
  | h :: t -> if h = x then () else has_remove_one x y t

(* The search reads the taken list only through membership of its candidates. *)
let rec free_ext (rp:rprims) (name:string) (t1 t2:list string) (fuel:nat) (n:int)
  : Lemma (requires (forall (i:int). i >= n ==> has (rp.candidate name i) t1 == has (rp.candidate name i) t2))
          (ensures free rp name t1 fuel n == free rp name t2 fuel n) (decreases fuel) =
  if fuel = 0 then ()
  else if has (rp.candidate name n) t1 then free_ext rp name t1 t2 (fuel - 1) (n + 1)
  else ()

(* The search answers a candidate at or past its start. *)
let rec free_is_candidate (rp:rprims) (name:string) (taken:list string) (fuel:nat) (n:int)
  : Lemma (ensures (exists (i:int). i >= n /\ free rp name taken fuel n == rp.candidate name i)) (decreases fuel) =
  if fuel = 0 then ()
  else if has (rp.candidate name n) taken then free_is_candidate rp name taken (fuel - 1) (n + 1)
  else ()

(* With one more candidate than taken names, an injective rule finds a free one. *)
let rec free_fresh (rp:rprims) (name:string) (taken:list string) (fuel:nat) (n:int)
  : Lemma (requires cand_injective rp /\ fuel > len taken)
          (ensures not (has (free rp name taken fuel n) taken)) (decreases fuel) =
  let c = rp.candidate name n in
  if has c taken then begin
    let taken' = remove_one c taken in
    len_remove_one c taken;
    let agree (i:int) : Lemma (requires i >= n + 1) (ensures has (rp.candidate name i) taken == has (rp.candidate name i) taken') =
      has_remove_one c (rp.candidate name i) taken in
    FStar.Classical.forall_intro (FStar.Classical.move_requires agree);
    free_ext rp name taken taken' (fuel - 1) (n + 1);
    free_fresh rp name taken' (fuel - 1) (n + 1);
    free_is_candidate rp name taken' (fuel - 1) (n + 1);
    has_remove_one c (free rp name taken' (fuel - 1) (n + 1)) taken
  end

(* Every output name is a right name that kept itself, or a chosen suffix the initial taken list
   does not carry. *)
let rec rename_right_mem (rp:rprims) (lnames taken rnames:list string) (x:string)
  : Lemma (requires cand_injective rp /\ has x (rename_right rp lnames taken rnames))
          (ensures has x rnames \/ not (has x taken)) (decreases rnames) =
  match rnames with
  | [] -> ()
  | r :: rest ->
    if has r lnames then begin
      let c = free rp r taken (len taken + 1) 1 in
      free_fresh rp r taken (len taken + 1) 1;
      if x = c then () else rename_right_mem rp lnames (c :: taken) rest x
    end
    else if x = r then () else rename_right_mem rp lnames taken rest x

let rec has_app (x:string) (l m:list string) : Lemma (ensures has x (app l m) == (has x l || has x m)) =
  match l with
  | [] -> ()
  | _ :: t -> has_app x t m

(* The renamed right names are distinct and avoid the left names, when the right names are
   distinct and every left and right name is taken. *)
let rec rename_right_no_dups (rp:rprims) (lnames taken rnames:list string)
  : Lemma (requires cand_injective rp /\ no_dups rnames
                    /\ (forall x. has x rnames ==> has x taken)
                    /\ (forall x. has x lnames ==> has x taken))
          (ensures no_dups (rename_right rp lnames taken rnames)
                   /\ (forall x. has x (rename_right rp lnames taken rnames) ==> not (has x lnames))) (decreases rnames) =
  match rnames with
  | [] -> ()
  | r :: rest ->
    if has r lnames then begin
      let c = free rp r taken (len taken + 1) 1 in
      free_fresh rp r taken (len taken + 1) 1;
      rename_right_no_dups rp lnames (c :: taken) rest;
      let out = rename_right rp lnames (c :: taken) rest in
      let not_c (x:string) : Lemma (requires has x out) (ensures x <> c) =
        rename_right_mem rp lnames (c :: taken) rest x in
      FStar.Classical.forall_intro (FStar.Classical.move_requires not_c)
    end
    else begin
      rename_right_no_dups rp lnames taken rest;
      let out = rename_right rp lnames taken rest in
      let not_r (x:string) : Lemma (requires has x out) (ensures x <> r) =
        rename_right_mem rp lnames taken rest x in
      FStar.Classical.forall_intro (FStar.Classical.move_requires not_r)
    end

(* Every output right name avoids the left names, whether the right names are distinct or not:
   a kept name is one no left column carries, a chosen suffix is not taken and every left name is. *)
let rec rename_right_avoids_left (rp:rprims) (lnames taken rnames:list string)
  : Lemma (requires cand_injective rp /\ (forall x. has x lnames ==> has x taken))
          (ensures (forall x. has x (rename_right rp lnames taken rnames) ==> not (has x lnames)))
          (decreases rnames) =
  match rnames with
  | [] -> ()
  | r :: rest ->
    if has r lnames then begin
      let c = free rp r taken (len taken + 1) 1 in
      free_fresh rp r taken (len taken + 1) 1;
      rename_right_avoids_left rp lnames (c :: taken) rest
    end
    else rename_right_avoids_left rp lnames taken rest

let rec no_dups_app (l m:list string)
  : Lemma (requires no_dups l /\ no_dups m /\ (forall x. has x m ==> not (has x l)))
          (ensures no_dups (app l m)) =
  match l with
  | [] -> ()
  | h :: t -> has_app h t m; no_dups_app t m

let rec names_renamed (ns:list string) (cols:schema)
  : Lemma (requires len ns == len cols) (ensures names (renamed ns cols) == ns) =
  match ns, cols with
  | _ :: nt, _ :: ct -> names_renamed nt ct
  | _, _ -> ()

let rec len_rename_right (rp:rprims) (lnames taken rnames:list string)
  : Lemma (ensures len (rename_right rp lnames taken rnames) == len rnames) (decreases rnames) =
  match rnames with
  | [] -> ()
  | r :: rest ->
    if has r lnames then len_rename_right rp lnames (free rp r taken (len taken + 1) 1 :: taken) rest
    else len_rename_right rp lnames taken rest

let rec len_names (#b:Type0) (l:list (string & b)) : Lemma (ensures len (names l) == len l) =
  match l with
  | [] -> ()
  | _ :: t -> len_names t

(* THE JOIN THEOREM (Phase 411, D20): a combining join over two distinct-named operands answers
   distinct names. Re-introducing 411's defect — a suffix checked against the left names only —
   leaves this lemma unprovable: `rename_right_no_dups` needs every right name taken. *)
let join_no_dups (rp:rprims) (lcols rcols:schema) (on:list (string & string)) (how:join_kind)
  : Lemma (requires cand_injective rp /\ no_dups (names lcols) /\ no_dups (names rcols)
                    /\ Ok? (join_cols rp lcols rcols on how))
          (ensures no_dups (names (Ok?._0 (join_cols rp lcols rcols on how)))) =
  match how with
  | Semi | Anti -> ()
  | _ ->
    let ln = names lcols in
    let rn = names rcols in
    let taken = app ln rn in
    let all_taken (x:string) : Lemma (ensures (has x rn ==> has x taken) /\ (has x ln ==> has x taken)) = has_app x ln rn in
    FStar.Classical.forall_intro all_taken;
    rename_right_no_dups rp ln taken rn;
    let out = rename_right rp ln taken rn in
    len_rename_right rp ln taken rn;
    len_names rcols;
    names_renamed out rcols;
    names_app lcols (renamed out rcols);
    no_dups_app ln out

let rec no_dups_snoc (l:list string) (x:string)
  : Lemma (requires no_dups l /\ not (has x l)) (ensures no_dups (app l [x])) =
  match l with
  | [] -> ()
  | h :: t -> has_app h t [x]; no_dups_snoc t x

let rec index_of_none_has (#b:Type0) (n:string) (l:list (string & b))
  : Lemma (ensures None? (index_of n l) <==> not (has n (names l))) =
  match l with
  | [] -> ()
  | (k, _) :: t -> if k = n then () else index_of_none_has n t

(* THE WINDOW THEOREM (Phase 450, D28): a window over distinct names answers distinct names,
   because an output name the input carries is refused. *)
let window_no_dups (rp:rprims) (cols:schema) (spec:window_spec)
  : Lemma (requires no_dups (names cols) /\ Ok? (window_cols rp cols spec))
          (ensures no_dups (names (Ok?._0 (window_cols rp cols spec)))) =
  index_of_none_has spec.w_as cols;
  names_app cols [(spec.w_as, window_type cols spec)];
  no_dups_snoc (names cols) spec.w_as

(* ---- the join's soundness, closed and open right ---- *)

let rec kept_right_at_least (lnames:list string) (rcs:list col_knowledge) (rcols lcols:schema) (rn:list string)
  : Lemma (requires at_least rcs rcols
                    /\ (forall x. has x rn ==> not (has x lnames))
                    /\ (forall x. has x (names rcols) ==> has x lnames \/ assoc x (renamed rn rcols) == assoc x rcols)
                    /\ lnames == names lcols)
          (ensures at_least (kept_right lnames rcs) (app lcols (renamed rn rcols))) =
  match rcs with
  | [] -> ()
  | (n, t) :: rest ->
    kept_right_at_least lnames rest rcols lcols rn;
    if has n lnames then ()
    else begin
      (* n is a right column the left does not carry: it keeps its name, and no left column has it *)
      let rec not_in_left (l:schema) : Lemma (requires not (has n (names l))) (ensures None? (assoc n l)) =
        match l with
        | [] -> ()
        | _ :: lt -> not_in_left lt in
      let rec in_right (r:schema) : Lemma (requires Some? (assoc n r)) (ensures has n (names r)) =
        match r with
        | (m, _) :: rt -> if m = n then () else in_right rt in
      in_right rcols;
      not_in_left lcols;
      assoc_app_absent n lcols (renamed rn rcols)
    end

(* A right column the left does not carry keeps its name under the rename, and is the first of
   that name in the renamed right: so its lookup by name is its lookup in the right. *)
let rec renamed_assoc_kept (rp:rprims) (lnames taken:list string) (rcols:schema) (x:string)
  : Lemma (requires cand_injective rp /\ has x (names rcols) /\ not (has x lnames)
                    /\ (forall y. has y (names rcols) ==> has y taken))
          (ensures assoc x (renamed (rename_right rp lnames taken (names rcols)) rcols) == assoc x rcols) (decreases rcols) =
  match rcols with
  | (m, ty) :: rest ->
    if has m lnames then begin
      let c = free rp m taken (len taken + 1) 1 in
      free_fresh rp m taken (len taken + 1) 1;
      (* c is not x: x is taken, c is not *)
      if m = x then () else renamed_assoc_kept rp lnames (c :: taken) rest x
    end
    else if m = x then () else renamed_assoc_kept rp lnames taken rest x

let rec at_least_app (a b:list col_knowledge) (out:schema)
  : Lemma (requires at_least a out /\ at_least b out) (ensures at_least (app a b) out) =
  match a with
  | [] -> ()
  | _ :: t -> at_least_app t b out

let rec at_least_cons (cs:list col_knowledge) (n:string) (ty:column_type) (tl:schema)
  : Lemma (requires at_least cs tl /\ not (has n (names cs))) (ensures at_least cs ((n, ty) :: tl)) =
  match cs with
  | [] -> ()
  | _ :: ct -> at_least_cons ct n ty tl

(* A closed knowledge sound for a schema of DISTINCT names is open-sound for it too: with a name
   repeated, the walk's second column of that name would claim a type a by-name read never finds. *)
let rec cols_agree_at_least (cs:list col_knowledge) (cols:schema)
  : Lemma (requires cols_agree cs cols /\ no_dups (names cols)) (ensures at_least cs cols) =
  match cs, cols with
  | [], [] -> ()
  | _ :: ct, (n, ty) :: st ->
    cols_agree_at_least ct st;
    cols_agree_names ct st;
    at_least_cons ct n ty st

let rec has_names_assoc (#b:Type0) (n:string) (l:list (string & b))
  : Lemma (ensures has n (names l) <==> Some? (assoc n l)) =
  match l with
  | [] -> ()
  | (k, _) :: t -> if k = n then () else has_names_assoc n t

let join_sound (rp:rprims) (srcs:sources) (resolve:resolver) (k:knowledge) (f:wframe) (src:data_source)
               (on:list (string & string)) (how:join_kind)
  : Lemma (requires cand_injective rp /\ sources_agree srcs resolve /\ sound k f.cols /\ no_dups (names f.cols)
                    /\ Ok? (step_schema rp resolve [] f (Join src on how)))
          (ensures sound (walk rp srcs k (Join src on how)) (Ok?._0 (step_schema rp resolve [] f (Join src on how)))) =
  let rcols = Ok?._0 (source_cols resolve src) in
  let out = Ok?._0 (step_schema rp resolve [] f (Join src on how)) in
  match how with
  | Semi | Anti -> ()
  | _ ->
    (match k with
     | AtLeast cs -> at_least_app_left cs f.cols (renamed (join_right_names rp (names f.cols) (names rcols)) rcols)
     | Closed left ->
       cols_agree_names left f.cols;
       let right = of_source srcs src in
       let rcs = columns right in
       (match right with
        | Closed _ ->
          (* the closed right IS the resolved schema *)
          (match src with
           | Embedded g -> cols_agree_known_of g.cols; names_known_of g.cols
           | Ref name -> cols_agree_known_of (Some?.v (srcs name)); names_known_of (Some?.v (srcs name)));
          cols_agree_names rcs rcols;
          renamed_sound (join_right_names rp (names left) (names rcs)) rcs rcols;
          cols_agree_app left f.cols (renamed_k (join_right_names rp (names left) (names rcs)) rcs)
                         (renamed (join_right_names rp (names left) (names rcs)) rcols)
        | AtLeast _ ->
          (* an undeclared Ref: the walk knows nothing of the right (rcs = []) *)
          let rn = join_right_names rp (names left) (names rcols) in
          let ln = names f.cols in
          let taken = app ln (names rcols) in
          let all_taken (y:string) : Lemma (ensures (has y (names rcols) ==> has y taken) /\ (has y ln ==> has y taken)) = has_app y ln (names rcols) in
          FStar.Classical.forall_intro all_taken;
          rename_right_avoids_left rp ln taken (names rcols);
          let kept (x:string) : Lemma (requires has x (names rcols)) (ensures has x ln \/ assoc x (renamed rn rcols) == assoc x rcols) =
            if has x ln then () else renamed_assoc_kept rp ln taken rcols x in
          FStar.Classical.forall_intro (FStar.Classical.move_requires kept);
          kept_right_at_least ln rcs rcols f.cols rn;
          cols_agree_at_least left f.cols;
          at_least_app_left left f.cols (renamed rn rcols);
          at_least_app left (kept_right ln rcs) (app f.cols (renamed rn rcols))))

(* ---- the union's soundness (Phase 469, D36) ---- *)

let rec union_types_names (l r:schema)
  : Lemma (requires Ok? (union_types l r) /\ len l == len r) (ensures names (Ok?._0 (union_types l r)) == names l) =
  match l, r with
  | _ :: lt, _ :: rt -> union_types_names lt rt
  | _, _ -> ()

let rec len_names_eq (l r:schema) : Lemma (requires names l == names r) (ensures len l == len r) =
  match l, r with
  | _ :: lt, _ :: rt -> len_names_eq lt rt
  | _, _ -> ()

(* Two closed operands: the walk's positional zip is sound for the evaluator's. *)
let rec union_pos_sound (left:list col_knowledge) (l r:schema)
  : Lemma (requires cols_agree left l /\ Ok? (union_types l r) /\ len l == len r)
          (ensures cols_agree (union_known_pos left (known_of r)) (Ok?._0 (union_types l r))) =
  match left, l, r with
  | _ :: ct, _ :: lt, _ :: rt -> union_pos_sound ct lt rt
  | _, _, _ -> ()

(* By name: the evaluator's column of a name is `union_ty` of the two sides' first columns of it. *)
let rec union_types_assoc (l r:schema) (n:string)
  : Lemma (requires Ok? (union_types l r) /\ names l == names r /\ Some? (assoc n l))
          (ensures Some? (assoc n r) /\ Some? (assoc n (Ok?._0 (union_types l r)))
                   /\ union_ty (Some?.v (assoc n l)) (Some?.v (assoc n r)) == assoc n (Ok?._0 (union_types l r))) =
  match l, r with
  | (k, _) :: lt, _ :: rt -> if k = n then () else union_types_assoc lt rt n
  | _, _ -> ()

let rec assoc_known_of (s:schema) (n:string)
  : Lemma (ensures assoc n (known_of s) == (match assoc n s with Some t -> Some (Some t) | None -> None)) =
  match s with
  | [] -> ()
  | (m, _) :: t -> if m = n then () else assoc_known_of t n

(* An open left against a closed right: the walk's by-name reading is sound for the evaluator's. *)
let rec union_by_name_sound (cs:list col_knowledge) (l r:schema)
  : Lemma (requires at_least cs l /\ Ok? (union_types l r) /\ names l == names r)
          (ensures at_least (union_known_by_name cs (known_of r)) (Ok?._0 (union_types l r))) =
  match cs with
  | [] -> ()
  | (n, _) :: ct ->
    union_types_assoc l r n;
    assoc_known_of r n;
    union_by_name_sound ct l r

(* An undeclared right: names only, which the evaluator's answer has. *)
let rec untyped_agree (cs:list col_knowledge) (s:schema)
  : Lemma (requires names cs == names s) (ensures cols_agree (untyped cs) s) =
  match cs, s with
  | _ :: ct, _ :: st -> untyped_agree ct st
  | _, _ -> ()

let rec untyped_at_least (cs:list col_knowledge) (l out:schema)
  : Lemma (requires at_least cs l /\ names l == names out) (ensures at_least (untyped cs) out) =
  match cs with
  | [] -> ()
  | (n, _) :: ct -> has_names_assoc n l; has_names_assoc n out; untyped_at_least ct l out

let union_sound (rp:rprims) (srcs:sources) (resolve:resolver) (k:knowledge) (f:wframe) (src:data_source)
  : Lemma (requires sources_agree srcs resolve /\ sound k f.cols /\ Ok? (step_schema rp resolve [] f (Union src)))
          (ensures sound (walk rp srcs k (Union src)) (Ok?._0 (step_schema rp resolve [] f (Union src)))) =
  let rcols = Ok?._0 (source_cols resolve src) in
  let out = Ok?._0 (union_types f.cols rcols) in
  len_names_eq f.cols rcols;
  union_types_names f.cols rcols;
  match of_source srcs src with
  | AtLeast _ ->
    (match k with
     | Closed cs -> cols_agree_names cs f.cols; untyped_agree cs out
     | AtLeast cs -> untyped_at_least cs f.cols out)
  | Closed rcs ->
    (* the closed right IS the resolved schema *)
    assert (rcs == known_of rcols);
    names_known_of rcols;
    (match k with
     | Closed left -> cols_agree_names left f.cols; union_pos_sound left f.cols rcols
     | AtLeast cs -> union_by_name_sound cs f.cols rcols)

(* THE AGREEMENT THEOREM — every verb, the union included (Phase 469, D36). *)
let walk_sound (rp:rprims) (srcs:sources) (resolve:resolver) (k:knowledge) (f:wframe) (t:transform)
  : Lemma (requires cand_injective rp /\ sources_agree srcs resolve /\ sound k f.cols
                    /\ (Join? t ==> no_dups (names f.cols))
                    /\ Ok? (step_schema rp resolve [] f t))
          (ensures sound (walk rp srcs k t) (Ok?._0 (step_schema rp resolve [] f t))) =
  match t with
  | Filter _ | Sort _ | Distinct | Limit _ _ | Intersect _ | Except _ -> ()
  | Project pairs -> project_sound k f.cols pairs; assert (project_step f.cols pairs == project_cols f.cols pairs)
  | GroupBy keys aggs -> group_by_sound k f.cols keys aggs
  | Window spec -> window_sound rp k f.cols spec
  | Pivot spec -> pivot_sound rp k f spec
  | Unpivot ids vals -> unpivot_sound k f ids vals
  | Join src on how -> join_sound rp srcs resolve k f src on how
  | Union src -> union_sound rp srcs resolve k f src

(* The theorem holds under any param env — `step_schema` reads the env only for the `Sort` and
   `Limit` slots, which keep the schema. *)
let union_keeps_names (l r:schema)
  : Lemma (requires Ok? (union_cols l r)) (ensures names (Ok?._0 (union_cols l r)) == names l) =
  len_names_eq l r; union_types_names l r

(* D32's witness, closed (Phase 469, D36): over an int column beside a float one, the walk and the
   evaluator both answer a float column. Phase 434 stated it as `union_walk_type_drifts`, the walk
   answering the left's int; that theorem is retired with the arm it described. *)
let union_walk_widens (rp:rprims) (srcs:sources)
  : Lemma (ensures (let l = [("a", IntType)] in
                    let r = [("a", FloatType)] in
                    union_cols l r == Ok [("a", FloatType)]
                    /\ walk rp srcs (Closed (known_of l)) (Union (Embedded ({ cols = r; rows = [] })))
                       == Closed [("a", Some FloatType)])) = ()

(* ======================================================================================
   9. What the row-set verbs and the four name-listing verbs answer.
   ====================================================================================== *)

let keeps_schema (rp:rprims) (resolve:resolver) (env:param_env) (f:wframe) (t:transform)
  : Lemma (requires Ok? (step_schema rp resolve env f t)
                    /\ (Filter? t \/ Sort? t \/ Distinct? t \/ Limit? t \/ Intersect? t \/ Except? t
                        \/ (Join? t /\ (Semi? (Join?._2 t) \/ Anti? (Join?._2 t)))))
          (ensures Ok?._0 (step_schema rp resolve env f t) == f.cols) = ()

let rec project_names (cols:schema) (pairs:list (string & string))
  : Lemma (requires Ok? (project_cols cols pairs)) (ensures names (Ok?._0 (project_cols cols pairs)) == snds pairs) =
  match pairs with
  | [] -> ()
  | _ :: t -> project_names cols t

(* The aggregates' output names, in order. *)
let rec agg_name_list (l:list agg) : Tot (list string) =
  match l with
  | [] -> []
  | a :: t -> a.a_name :: agg_name_list t

let rec agg_names (cols:schema) (aggs:list agg)
  : Lemma (requires Ok? (agg_cols cols aggs))
          (ensures names (Ok?._0 (agg_cols cols aggs)) == agg_name_list aggs) =
  match aggs with
  | [] -> ()
  | _ :: t -> agg_names cols t

let group_by_names (cols:schema) (keys:list string) (aggs:list agg)
  : Lemma (requires Ok? (group_by_cols cols keys aggs))
          (ensures names (Ok?._0 (group_by_cols cols keys aggs)) == app keys (agg_name_list aggs)) =
  names_lookup cols keys;
  agg_names cols aggs;
  names_app (lookup_cols cols keys) (Ok?._0 (agg_cols cols aggs))

let unpivot_names (f:wframe) (ids vals:list string)
  : Lemma (requires Ok? (unpivot_cols f.cols f.rows ids vals))
          (ensures names (Ok?._0 (unpivot_cols f.cols f.rows ids vals)) == app ids ["variable"; "value"]) =
  names_lookup f.cols ids;
  let dt = unpivot_typing (snds (lookup_cols f.cols vals)) in
  let vty = Ok?._0 (column_type_by dt "value" (melt (indexes_of f.cols vals) f.rows)) in
  names_app (lookup_cols f.cols ids) [("variable", StringType); ("value", vty)]

let rec prefix (l m:list string) : Tot bool =
  match l, m with
  | [], _ -> true
  | x :: lt, y :: mt -> x = y && prefix lt mt
  | _, _ -> false

let rec prefix_app (l m:list string) : Lemma (ensures prefix l (app l m)) =
  match l with
  | [] -> ()
  | _ :: t -> prefix_app t m

let pivot_names_prefix (rp:rprims) (f:wframe) (spec:pivot_spec)
  : Lemma (requires Ok? (pivot_cols rp f.cols f.rows spec))
          (ensures prefix spec.p_index (names (Ok?._0 (pivot_cols rp f.cols f.rows spec)))) =
  names_lookup f.cols spec.p_index;
  let vals = pivot_value_cols rp (agg_type spec.p_agg (Some?.v (assoc spec.p_values f.cols))) (on_values rp (Some?.v (index_of spec.p_on f.cols)) f.rows) in
  names_app (lookup_cols f.cols spec.p_index) vals;
  prefix_app spec.p_index (names vals)

(* ---- THE REPEATED NAME, closed (Phase 468, D35) ----
   Phase 434 carried four witnesses here (`project_repeats_name`, `group_by_repeats_name`,
   `unpivot_repeats_name`, `pivot_repeats_name`), each a verb answering `Ok` with a name twice. The
   four verbs now ask `repeated_output`, so the witnesses are refusals (`*_repeat_refused`, below)
   and the four `*_no_dups` theorems join `window_no_dups` and `join_no_dups`: none of the six verbs
   that name their output answers `Ok` with a name twice. *)

(* An output list the check admits has distinct names, none of them already seen. *)
let rec repeat_in_none (verb:string) (seen outs:list (string & string))
  : Lemma (requires None? (repeat_in verb seen outs))
          (ensures no_dups (names outs) /\ (forall x. has x (names outs) ==> None? (assoc x seen)))
          (decreases outs) =
  match outs with
  | [] -> ()
  | (n, who) :: t -> repeat_in_none verb ((n, who) :: seen) t

let rec names_project_outs (pairs:list (string & string)) : Lemma (ensures names (project_outs pairs) == snds pairs) =
  match pairs with
  | [] -> ()
  | _ :: t -> names_project_outs t

let rec names_key_outs (keys:list string) : Lemma (ensures names (key_outs keys) == keys) =
  match keys with
  | [] -> ()
  | _ :: t -> names_key_outs t

let rec names_agg_outs (aggs:list agg) : Lemma (ensures names (agg_outs aggs) == agg_name_list aggs) =
  match aggs with
  | [] -> ()
  | _ :: t -> names_agg_outs t

let rec names_id_outs (ids:list string) : Lemma (ensures names (id_outs ids) == ids) =
  match ids with
  | [] -> ()
  | _ :: t -> names_id_outs t

let rec names_index_outs (ns:list string) : Lemma (ensures names (index_outs ns) == ns) =
  match ns with
  | [] -> ()
  | _ :: t -> names_index_outs t

let rec names_value_outs (vs:schema) : Lemma (ensures names (value_outs vs) == names vs) =
  match vs with
  | [] -> ()
  | _ :: t -> names_value_outs t

(* THE PROJECT THEOREM: a projection never answers a name twice. *)
let project_no_dups (cols:schema) (pairs:list (string & string))
  : Lemma (requires Ok? (project_step cols pairs))
          (ensures no_dups (names (Ok?._0 (project_step cols pairs)))) =
  repeat_in_none "project" [] (project_outs pairs);
  names_project_outs pairs;
  project_names cols pairs

(* THE GROUP-BY THEOREM: the keys and the aggregates never share a name. *)
let group_by_no_dups (cols:schema) (keys:list string) (aggs:list agg)
  : Lemma (requires Ok? (group_by_cols cols keys aggs))
          (ensures no_dups (names (Ok?._0 (group_by_cols cols keys aggs)))) =
  repeat_in_none "groupBy" [] (app (key_outs keys) (agg_outs aggs));
  names_app (key_outs keys) (agg_outs aggs);
  names_key_outs keys;
  names_agg_outs aggs;
  group_by_names cols keys aggs

(* THE UNPIVOT THEOREM: no id column is named twice, `variable` or `value`. *)
let unpivot_no_dups (f:wframe) (ids vals:list string)
  : Lemma (requires Ok? (unpivot_cols f.cols f.rows ids vals))
          (ensures no_dups (names (Ok?._0 (unpivot_cols f.cols f.rows ids vals)))) =
  repeat_in_none "unpivot" [] (unpivot_outs ids);
  names_app (id_outs ids) [("variable", "the unpivot's variable column"); ("value", "the unpivot's value column")];
  names_id_outs ids;
  unpivot_names f ids vals

(* THE PIVOT THEOREM: no index column takes a value column's name, and no two value columns share
   one — whatever the `on` column holds and whatever text `cell_string` gives it. *)
let pivot_no_dups (rp:rprims) (f:wframe) (spec:pivot_spec)
  : Lemma (requires Ok? (pivot_cols rp f.cols f.rows spec))
          (ensures no_dups (names (Ok?._0 (pivot_cols rp f.cols f.rows spec)))) =
  let vals = pivot_value_cols rp (agg_type spec.p_agg (Some?.v (assoc spec.p_values f.cols))) (on_values rp (Some?.v (index_of spec.p_on f.cols)) f.rows) in
  repeat_in_none "pivot" [] (app (index_outs spec.p_index) (value_outs vals));
  names_app (index_outs spec.p_index) (value_outs vals);
  names_index_outs spec.p_index;
  names_value_outs vals;
  names_lookup f.cols spec.p_index;
  names_app (lookup_cols f.cols spec.p_index) vals

(* The four witnesses Phase 434 recorded as `Ok`, each now the refusal naming the verb, the name
   and both producers — the differential holds the shipped evaluator to the same answer. *)
let project_repeat_refused (_:unit)
  : Lemma (ensures project_step [("a", IntType)] [("a", "x"); ("a", "x")]
                   == Error (TypeError (repeat_text "project" "x" "the projection of 'a'" "the projection of 'a'"))) =
  assert_norm (project_step [("a", IntType)] [("a", "x"); ("a", "x")]
               == Error (TypeError (repeat_text "project" "x" "the projection of 'a'" "the projection of 'a'")))

let group_by_repeat_refused (_:unit)
  : Lemma (ensures group_by_cols [("k", StringType); ("v", IntType)] ["k"] [{ a_name = "k"; a_fn = Sum; a_of = "v" }]
                   == Error (TypeError (repeat_text "groupBy" "k" "the key 'k'" "the aggregate over 'v'"))) =
  assert_norm (group_by_cols [("k", StringType); ("v", IntType)] ["k"] [{ a_name = "k"; a_fn = Sum; a_of = "v" }]
               == Error (TypeError (repeat_text "groupBy" "k" "the key 'k'" "the aggregate over 'v'")))

let unpivot_repeat_refused (_:unit)
  : Lemma (ensures unpivot_cols [("variable", StringType); ("v", IntType)] [] ["variable"] ["v"]
                   == Error (TypeError (repeat_text "unpivot" "variable" "the id column 'variable'" "the unpivot's variable column"))) =
  assert_norm (unpivot_cols [("variable", StringType); ("v", IntType)] [] ["variable"] ["v"]
               == Error (TypeError (repeat_text "unpivot" "variable" "the id column 'variable'" "the unpivot's variable column")))

(* The pivot's witness reads the data, so it holds for any `cell_string` that gives the string cell
   `i` the text `i`; the refusal's text is the oracle's to compare. *)
let pivot_repeat_refused (rp:rprims) (f:wframe)
  : Lemma (requires rp.cell_string (Str "i") == "i"
                    /\ f.cols == [("i", IntType); ("g", StringType); ("w", IntType)]
                    /\ f.rows == [[Int 1; Str "i"; Int 2]])
          (ensures Error? (pivot_cols rp f.cols f.rows { p_index = ["i"]; p_on = "g"; p_values = "w"; p_agg = Sum })
                   /\ TypeError? (Error?._0 (pivot_cols rp f.cols f.rows { p_index = ["i"]; p_on = "g"; p_values = "w"; p_agg = Sum }))) = ()

(* ======================================================================================
   10. Unknown-column refusal, verb by verb, in each arm's own order.
   ====================================================================================== *)

let rec project_unknown_refused (cols:schema) (pairs:list (string & string))
  : Lemma (requires Some? (first_unknown cols (names pairs)))
          (ensures project_cols cols pairs == Error (UnknownColumn (Some?.v (first_unknown cols (names pairs))) (names cols))) =
  match pairs with
  | (src, _) :: t -> index_of_none_assoc src cols; if None? (index_of src cols) then () else project_unknown_refused cols t

let group_by_key_unknown_refused (cols:schema) (keys:list string) (aggs:list agg)
  : Lemma (requires Some? (first_unknown cols keys))
          (ensures group_by_cols cols keys aggs == Error (UnknownColumn (Some?.v (first_unknown cols keys)) (names cols))) = ()

let rec agg_of_names (l:list agg) : Tot (list string) =
  match l with
  | [] -> []
  | a :: t -> a.a_of :: agg_of_names t

let rec agg_unknown_refused (cols:schema) (aggs:list agg)
  : Lemma (requires Some? (first_unknown cols (agg_of_names aggs)))
          (ensures agg_cols cols aggs == Error (UnknownColumn (Some?.v (first_unknown cols (agg_of_names aggs))) (names cols))) =
  match aggs with
  | a :: t -> index_of_none_assoc a.a_of cols; if None? (index_of a.a_of cols) then () else agg_unknown_refused cols t

let group_by_agg_unknown_refused (cols:schema) (keys:list string) (aggs:list agg)
  : Lemma (requires None? (first_unknown cols keys) /\ Some? (first_unknown cols (agg_of_names aggs)))
          (ensures group_by_cols cols keys aggs == Error (UnknownColumn (Some?.v (first_unknown cols (agg_of_names aggs))) (names cols))) =
  agg_unknown_refused cols aggs

(* The join: the left keys first, then the right keys, both schemas' names listed. *)
let join_unknown_refused (rp:rprims) (lcols rcols:schema) (on:list (string & string)) (how:join_kind)
  : Lemma (requires Some? (first_unknown lcols (names on)) \/ Some? (first_unknown rcols (snds on)))
          (ensures join_cols rp lcols rcols on how
                   == Error (UnknownColumn (match first_unknown lcols (names on) with
                                            | Some n -> n
                                            | None -> Some?.v (first_unknown rcols (snds on)))
                                           (app (names lcols) (names rcols)))) = ()

(* The window: a read column the schema lacks, then the first partition or order key it lacks. *)
let window_of_unknown_refused (rp:rprims) (cols:schema) (spec:window_spec)
  : Lemma (requires window_reads_of spec.w_fn /\ None? (index_of spec.w_of cols))
          (ensures window_cols rp cols spec == Error (UnknownColumn spec.w_of (names cols))) = ()

let window_key_unknown_refused (rp:rprims) (cols:schema) (spec:window_spec)
  : Lemma (requires (match spec.w_fn with NTile b -> b >= 1 | _ -> True)
                    /\ (window_reads_of spec.w_fn ==> Some? (index_of spec.w_of cols))
                    /\ Some? (first_unknown cols (app spec.partition_by (names spec.order_by))))
          (ensures window_cols rp cols spec
                   == Error (UnknownColumn (Some?.v (first_unknown cols (app spec.partition_by (names spec.order_by)))) (names cols))) = ()

(* The pivot: the index names, then `on`, then `values`. *)
let pivot_unknown_refused (rp:rprims) (f:wframe) (spec:pivot_spec)
  : Lemma (requires Some? (first_unknown f.cols spec.p_index) \/ None? (index_of spec.p_on f.cols) \/ None? (assoc spec.p_values f.cols))
          (ensures pivot_cols rp f.cols f.rows spec
                   == Error (UnknownColumn (match first_unknown f.cols spec.p_index with
                                            | Some n -> n
                                            | None -> if None? (index_of spec.p_on f.cols) then spec.p_on else spec.p_values)
                                           (names f.cols))) = ()

(* The unpivot: the id names, then the value names. *)
let unpivot_unknown_refused (f:wframe) (ids vals:list string)
  : Lemma (requires Some? (first_unknown f.cols ids) \/ Some? (first_unknown f.cols vals))
          (ensures unpivot_cols f.cols f.rows ids vals
                   == Error (UnknownColumn (match first_unknown f.cols ids with
                                            | Some n -> n
                                            | None -> Some?.v (first_unknown f.cols vals))
                                           (names f.cols))) = ()

(* THE SORT THEOREM (Phase 412, D21): once every key's slot resolves, the first key the schema does
   not carry is refused. Re-introducing 412's defect — the key dropped instead — makes the `Sort`
   arm answer `Ok`, and this lemma fails. *)
let sort_unknown_refused (rp:rprims) (resolve:resolver) (env:param_env) (f:wframe) (sk:list (slot string & sort_dir))
  : Lemma (requires Ok? (resolve_sort_keys env sk)
                    /\ Some? (first_unknown f.cols (names (Ok?._0 (resolve_sort_keys env sk)))))
          (ensures step_schema rp resolve env f (Sort sk)
                   == Error (UnknownColumn (Some?.v (first_unknown f.cols (names (Ok?._0 (resolve_sort_keys env sk))))) (names f.cols))) = ()

(* A literal key the schema does not carry is refused whatever the env: the 412 shape itself. *)
let sort_literal_unknown_refused (rp:rprims) (resolve:resolver) (env:param_env) (f:wframe) (key:string) (dir:sort_dir)
  : Lemma (requires None? (index_of key f.cols))
          (ensures step_schema rp resolve env f (Sort [(SlotLit key, dir)]) == Error (UnknownColumn key (names f.cols))) = ()
