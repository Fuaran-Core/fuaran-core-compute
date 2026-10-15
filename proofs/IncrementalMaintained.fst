(*
   IncrementalMaintained — the incremental refresh past the row-local class: the maintained
   `GroupBy`, `Project`, `Limit`, `Window` and the filtering joins, and their agreement with full
   evaluation (fuaran-core Phase 464, extending Phase 433's `Incremental.fst`).

   WHAT IS MODELLED. `src/Fuaran.Compute.DataFrame/Incremental.fs`'s walk over the step classes it
   admits beside `Filter` and `Derive` (`classifyStep`: `PropagateRows` for `Project`, `MaintainGroups`,
   `TruncateOrder`, `RecomputeFrame`, `FilterByRelation`), at the level `Incremental.fst` states the
   row-local walk — a row's cell at a step, found under its token:

     - the walk (`mwalk`) threads a REUSE predicate over tokens (F#: `Stable`): a `Filter`'s and a
       `Derive`'s cells are `Incremental.fst`'s `step_cells` (restated over any cell function, `step_cells_by`,
       so a join's verdicts are the same step); a `Project` permutes and renames columns and touches no
       row (F#: `WProject` — "evaluates nothing and caches nothing"); a `Limit` keeps a positional slice
       (F#: `WLimit` — `max 0 offset`, `max 0 n`, clamped); a `Window` appends the column the reference
       computes over the rows alive at the step, in their order, and reuses NOTHING after it (F#:
       `Array.fill r.Stable … false` — the resumed prefix fold of Phase 333 keeps more and is declared
       out); a filtering join records a verdict per row — kept iff some right key matches (`Semi`) or none
       does (`Anti`) — reusing a stable row's verdict exactly when the joined relation's key index is the
       prior one (F#: `relationMoved`);
     - the derived column is PACKED as the frame carries it (`Incremental.pack_cache`, through
       `Pipeline.pack_cells`, Phase 470) and its pack KEY — the decided type and the pack's verdict —
       is recorded per evaluating step (`entry.e_pack`); a `Derive` whose key is not the prior
       evaluation's reuses nothing from there on (F#: `retypedSince`, D37, widened by D38):
       a column the cells type can be retyped by a named row while every other row's cell stands, and
       every cell cached downstream, and every reused group, was computed over the other reading;
     - the maintained `GroupBy` (`group_step`): the groups of the rows alive at the step in
       first-appearance order of the key cells (F#: `groupStep` — "the same partition `evalGroupBy`
       builds"), each group's aggregates REUSED from the prior state exactly when every member is
       reusable and the group's member tokens are the prior group's, in order (F#: `groupStable` and
       `sameMembers`), and RECOMPUTED over its members in member order otherwise, by the reference's own
       aggregate function; a group with no member vanishes; the group table walks on under one token per
       key (`key_token`), a group reusable downstream exactly when its aggregates were reused (F#:
       `Stable[g] = existed && not RecomputedAt[g]`). Every aggregate is maintained by recompute on this
       path — the invertible `Count` and int `Sum` of the in-place fast path (`groupStepInPlace`,
       `maintained`) are that path's, which is declared out with it.

   WHAT IS PROVED. Over every cell primitive, every param environment, every well-formed source and
   every pipeline of these classes (a prefix, at most one `GroupBy`, a tail):

     - `mprime_is_full`: priming computes what `Pipeline.eval_uncounted` computes over the reference
       `Relational.other_of` builds from the relational schema model and this module's row functions
       (`mrows`) — the same frame, or the same error;
     - `mrefresh_is_prime` (THE STATE THEOREM): a refresh against a state primed over the prior source,
       under a delta that names every row that moved, is the prime over the new source — output, every
       step's cache and type, the group table and the tail's caches;
     - `mrefresh_agrees` (THE AGREEMENT THEOREM): the two composed.

   The delta premise is Phase 433's read one row at a time (`wk`): a row the delta does not name that
   the prior source held under its token has the cells it had — a row it did NOT hold is admitted
   (production reads it as unstable; the model's cache misses it), which is strictly weaker than
   `Incremental.unchanged`. Two premises are added: the prior source's tokens are distinct (production's
   witness refuses a duplicate identity, `RowIdentityUnusable`), and `key_token` is injective
   (production mints one token per group key). The theorems are stated for a new source of the prior
   source's SCHEMA, types included: a source whose schema moved is `SourceSchemaMoved` in production
   and evaluates in full, outside the walk; a DERIVED column's retype is inside the walk and is what
   the type record handles.

   WHAT IS A PARAMETER (`mprims`), each an assumption: the aggregate function (`agg_eval` — F#:
   `Column.aggregate` over the members in order, which the streamed accumulators equal by law), the
   join's key match (`key_eq` — F#: `joinKeysMatch`), the window column (`win_cells` — F#:
   `windowColumnOf`, one cell per row in the frame's order) and the group token (`key_token`). Every
   theorem is conditional on them only through the reference `mrows` builds from the same parameters;
   `mrefresh_is_prime` needs `key_token` injective. With `Pipeline.fst`'s `prims` and `Relational.fst`'s
   `rprims`, nothing else is assumed. Every type parameter is `Type0`.

   WHAT IS NOT MODELLED, and recorded in `DECISIONS.md` (D37): `Sort` (`MergeOrder` — the merge of the
   named rows into the prior produced order), the resumed prefix-fold window (Phase 333), the in-place
   and append fast paths (Phases 323 and 359, with their invertible `Count` and int `Sum`), the
   staleness guards, the quiet-delta shortcut, the chunked path and the state's wire form.
*)
module IncrementalMaintained

open Pipeline

(* ======================================================================================
   1. The parameters.
   ====================================================================================== *)

noeq type mprims = {
  (* F#: `Column.aggregate fn` over a column of type `ty` holding the members' cells in order. *)
  agg_eval  : agg_fn -> column_type -> list cell -> outcome cell eval_error;
  (* F#: `DataFrame.joinKeysMatch` — a left key against a right key. *)
  key_eq    : list cell -> list cell -> bool;
  (* F#: `DataFrame.windowColumnOf` — the appended cell per row, over the rows in frame order. *)
  win_cells : cols:schema -> window_spec -> rows:list (list cell) -> outcome (cs:list cell{len cs = len rows}) eval_error;
  (* F#: the token minted for a group, one per distinct key. *)
  key_token : list cell -> Incremental.token;
}

let key_token_injective (mp:mprims) : prop =
  forall (k1 k2:list cell). mp.key_token k1 = mp.key_token k2 ==> k1 == k2

(* ======================================================================================
   2. The step alphabet — F#: `PrefixStep` less `WSort`, and `split`'s shape: a prefix, at most one
      `GroupBy`, a tail.
   ====================================================================================== *)

type mstep =
  | MFilter  : col_expr -> mstep
  | MDerive  : string -> col_expr -> mstep
  | MProject : list (string & string) -> mstep
  | MLimit   : int -> int -> mstep
  | MWindow  : window_spec -> mstep
  | MJoin    : data_source -> list (string & string) -> bool -> mstep

let embed_mstep (s:mstep) : Tot transform =
  match s with
  | MFilter pred -> Filter pred
  | MDerive name x -> Derive name x
  | MProject pairs -> Project pairs
  | MLimit n off -> Limit (SlotLit n) (SlotLit off)
  | MWindow spec -> Window spec
  | MJoin src on keep -> Join src on (if keep then Semi else Anti)

let rec embed_steps (p:list mstep) : Tot (list transform) =
  match p with
  | [] -> []
  | s :: rest -> embed_mstep s :: embed_steps rest

type mpipe = { prefix : list mstep; grouped : option (list string & list agg & list mstep) }

let embed_pipe (p:mpipe) : Tot (list transform) =
  match p.grouped with
  | None -> embed_steps p.prefix
  | Some (keys, aggs, tail) -> app (embed_steps p.prefix) (GroupBy keys aggs :: embed_steps tail)

(* ======================================================================================
   3. List helpers over the walked frame.
   ====================================================================================== *)

let rec tokens_of (a:Incremental.alive) : Tot (list Incremental.token) =
  match a with
  | [] -> []
  | (t, _) :: rest -> t :: tokens_of rest

let rec distinct (a:Incremental.alive) : Tot bool =
  match a with
  | [] -> true
  | (t, _) :: rest -> not (Incremental.mem_token t (tokens_of rest)) && distinct rest

let rec mem_a (x:Incremental.token & list cell) (a:Incremental.alive) : Tot bool =
  match a with
  | [] -> false
  | y :: rest -> y = x || mem_a x rest

let rec take_a (#b:Type0) (n:nat) (l:list b) : Tot (list b) (decreases l) =
  match l with
  | [] -> []
  | x :: rest -> if n = 0 then [] else x :: take_a (n - 1) rest

let rec drop_a (#b:Type0) (n:nat) (l:list b) : Tot (list b) (decreases l) =
  match l with
  | [] -> []
  | x :: rest -> if n = 0 then l else drop_a (n - 1) rest

let max0 (i:int) : Tot nat = if i < 0 then 0 else i

(* F#: `Array.sub f.Order lo (stop - lo)` — the slice `evalLimit` keeps. *)
let slice (#b:Type0) (off n:int) (l:list b) : Tot (list b) = take_a (max0 n) (drop_a (max0 off) l)

(* The cells at column `j` of a row, `Null` past its end (Relational's `col_cells` reads one row). *)
let cell_at (j:nat) (r:list cell) : Tot cell = if j < len r then nth r j else Null

(* F#: the key cells of a row under the named columns, in the names' order — `keySlots`' probe,
   `joinKeyIndices`' projection, and `evalProject`'s columns read one row at a time. *)
let rec proj_row (cols:schema) (ns:list string) (r:list cell) : Tot (list cell) =
  match ns with
  | [] -> []
  | n :: rest ->
    (match index_of n cols with
     | Some j -> cell_at j r
     | None -> Null) :: proj_row cols rest r

let rec len_proj_row (cols:schema) (ns:list string) (r:list cell)
  : Lemma (ensures len (proj_row cols ns r) == len ns) =
  match ns with
  | [] -> ()
  | _ :: rest -> len_proj_row cols rest r

let rec proj_alive (cols:schema) (ns:list string) (a:Incremental.alive) : Tot Incremental.alive =
  match a with
  | [] -> []
  | (t, r) :: rest -> (t, proj_row cols ns r) :: proj_alive cols ns rest

let rec proj_rows (cols:schema) (ns:list string) (rows:list (list cell)) : Tot (list (list cell)) =
  match rows with
  | [] -> []
  | r :: rest -> proj_row cols ns r :: proj_rows cols ns rest

let rec awidth_proj (cols:schema) (ns:list string) (a:Incremental.alive)
  : Lemma (ensures Incremental.awidth (len ns) (proj_alive cols ns a)) =
  match a with
  | [] -> ()
  | (_, r) :: rest -> len_proj_row cols ns r; awidth_proj cols ns rest

let rec all_width_proj (cols:schema) (ns:list string) (rows:list (list cell))
  : Lemma (ensures all_width (len ns) (proj_rows cols ns rows)) =
  match rows with
  | [] -> ()
  | r :: rest -> len_proj_row cols ns r; all_width_proj cols ns rest

let rec rows_of_proj (cols:schema) (ns:list string) (a:Incremental.alive)
  : Lemma (ensures Incremental.rows_of (proj_alive cols ns a) == proj_rows cols ns (Incremental.rows_of a)) =
  match a with
  | [] -> ()
  | _ :: rest -> rows_of_proj cols ns rest

let rec awidth_take (n:nat) (k:nat) (a:Incremental.alive)
  : Lemma (requires Incremental.awidth n a) (ensures Incremental.awidth n (take_a k a)) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> if k = 0 then () else awidth_take n (k - 1) rest

let rec awidth_drop (n:nat) (k:nat) (a:Incremental.alive)
  : Lemma (requires Incremental.awidth n a) (ensures Incremental.awidth n (drop_a k a)) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> if k = 0 then () else awidth_drop n (k - 1) rest

let rec all_width_take (n:nat) (k:nat) (rows:list (list cell))
  : Lemma (requires all_width n rows) (ensures all_width n (take_a k rows)) (decreases rows) =
  match rows with
  | [] -> ()
  | _ :: rest -> if k = 0 then () else all_width_take n (k - 1) rest

let rec all_width_drop (n:nat) (k:nat) (rows:list (list cell))
  : Lemma (requires all_width n rows) (ensures all_width n (drop_a k rows)) (decreases rows) =
  match rows with
  | [] -> ()
  | _ :: rest -> if k = 0 then () else all_width_drop n (k - 1) rest

let rec rows_of_take (k:nat) (a:Incremental.alive)
  : Lemma (ensures Incremental.rows_of (take_a k a) == take_a k (Incremental.rows_of a)) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> if k = 0 then () else rows_of_take (k - 1) rest

let rec rows_of_drop (k:nat) (a:Incremental.alive)
  : Lemma (ensures Incremental.rows_of (drop_a k a) == drop_a k (Incremental.rows_of a)) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> if k = 0 then () else rows_of_drop (k - 1) rest

(* The window's appended cells, one per alive row, as a step cache under the rows' tokens. *)
let rec pair_cells (a:Incremental.alive) (cs:list cell{len cs = len a})
  : Tot (c:Incremental.step_cache{len c = len a}) (decreases a) =
  match a, cs with
  | [], [] -> []
  | (t, _) :: ar, v :: cr -> (t, v) :: pair_cells ar cr

let rec cells_of_pair (a:Incremental.alive) (cs:list cell{len cs = len a})
  : Lemma (ensures Incremental.cells_of (pair_cells a cs) == cs) (decreases a) =
  match a, cs with
  | [], [] -> ()
  | _ :: ar, _ :: cr -> cells_of_pair ar cr

(* The rows kept by a boolean row function, in order (F#: the filtering join's kept rows). *)
let rec keep_rows_by (vf:list cell -> bool) (rows:list (list cell)) : Tot (list (list cell)) =
  match rows with
  | [] -> []
  | r :: rest -> if vf r then r :: keep_rows_by vf rest else keep_rows_by vf rest

let rec all_width_keep_rows (n:nat) (vf:list cell -> bool) (rows:list (list cell))
  : Lemma (requires all_width n rows) (ensures all_width n (keep_rows_by vf rows)) =
  match rows with
  | [] -> ()
  | _ :: rest -> all_width_keep_rows n vf rest

(* ======================================================================================
   4. One step's cells over ANY cell function — `Incremental.step_cells` restated so that a filtering
      join's verdicts are the same step as a `Filter`'s cells.
   ====================================================================================== *)

type cell_fn (n:nat) = row_of n -> outcome cell eval_error

let expr_fn (pr:prims) (env:param_env) (cols:schema) (x:col_expr) : Tot (cell_fn (len cols)) =
  fun r -> eval_expr pr env cols r x

let bool_fn (n:nat) (vf:list cell -> bool) : Tot (cell_fn n) = fun r -> Ok (Bool (vf r))

let rec step_cells_by (#n:nat) (cf:cell_fn n) (reuse:Incremental.reuse_fn) (prior:Incremental.step_cache)
  (a:Incremental.alive{Incremental.awidth n a})
  : Tot (r:outcome Incremental.step_cache eval_error{match r with Ok cs -> len cs = len a | Error _ -> true})
        (decreases a) =
  match a with
  | [] -> Ok []
  | (t, r) :: rest ->
    let here =
      if reuse t then
        (match assoc t prior with
         | Some c -> Ok c
         | None -> cf r)
      else cf r in
    (match here with
     | Error err -> Error err
     | Ok c ->
       (match step_cells_by cf reuse prior rest with
        | Error err -> Error err
        | Ok cs -> Ok ((t, c) :: cs)))

let rec step_cells_by_is (pr:prims) (env:param_env) (cols:schema) (x:col_expr) (reuse:Incremental.reuse_fn)
  (prior:Incremental.step_cache) (a:Incremental.alive{Incremental.awidth (len cols) a})
  : Lemma (ensures step_cells_by (expr_fn pr env cols x) reuse prior a == Incremental.step_cells pr env cols x reuse prior a)
          (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> step_cells_by_is pr env cols x reuse prior rest

(* A boolean step never errors, and its cells are the verdicts. *)
let rec bool_step_ok (n:nat) (vf:list cell -> bool) (reuse:Incremental.reuse_fn) (prior:Incremental.step_cache)
  (a:Incremental.alive{Incremental.awidth n a})
  : Lemma (ensures Ok? (step_cells_by (bool_fn n vf) reuse prior a)) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> bool_step_ok n vf reuse prior rest

let rec rows_of_keep_by (n:nat) (vf:list cell -> bool) (a:Incremental.alive{Incremental.awidth n a})
  : Lemma (ensures (bool_step_ok n vf Incremental.no_reuse [] a;
                    Incremental.rows_of (Incremental.keep_true a (Ok?._0 (step_cells_by (bool_fn n vf) Incremental.no_reuse [] a)))
                    == keep_rows_by vf (Incremental.rows_of a)))
          (decreases a) =
  bool_step_ok n vf Incremental.no_reuse [] a;
  match a with
  | [] -> ()
  | _ :: rest -> rows_of_keep_by n vf rest

(* ======================================================================================
   5. The delta premise, read one row at a time (`wk`), and its survival through every step.
   ====================================================================================== *)

(* A reusable row the prior source held under its token has the cells it had; one it did not hold is
   admitted (the cache misses it). Weaker than `Incremental.unchanged`. *)
let rec wk (reuse:Incremental.reuse_fn) (a:Incremental.alive) (a0:Incremental.alive) : Tot bool (decreases a) =
  match a with
  | [] -> true
  | (t, r) :: rest ->
    (not (reuse t) || (match assoc t a0 with Some r0 -> r0 = r | None -> true)) && wk reuse rest a0

let ok_row (reuse:Incremental.reuse_fn) (a0:Incremental.alive) (t:Incremental.token) (r:list cell) : Tot bool =
  not (reuse t) || (match assoc t a0 with Some r0 -> r0 = r | None -> true)

let rec wk_mem (reuse:Incremental.reuse_fn) (a a0:Incremental.alive) (t:Incremental.token) (r:list cell)
  : Lemma (requires wk reuse a a0 /\ mem_a (t, r) a) (ensures ok_row reuse a0 t r) (decreases a) =
  match a with
  | [] -> ()
  | y :: rest -> if y = (t, r) then () else wk_mem reuse rest a0 t r

let rec wk_of_rows (reuse:Incremental.reuse_fn) (a a0:Incremental.alive)
  : Lemma (requires forall (t:Incremental.token) (r:list cell). mem_a (t, r) a ==> ok_row reuse a0 t r)
          (ensures wk reuse a a0) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> wk_of_rows reuse rest a0

(* A fresh step's cache answers exactly the prior source's tokens, each with that row's cell. *)
let rec assoc_cells_by (#n:nat) (cf:cell_fn n) (a0:Incremental.alive{Incremental.awidth n a0}) (t:Incremental.token)
  : Lemma (requires Ok? (step_cells_by cf Incremental.no_reuse [] a0))
          (ensures (match assoc t a0 with
                    | Some r0 ->
                      len r0 = n /\ Ok? (cf r0)
                      /\ assoc t (Ok?._0 (step_cells_by cf Incremental.no_reuse [] a0)) == Some (Ok?._0 (cf r0))
                    | None -> assoc t (Ok?._0 (step_cells_by cf Incremental.no_reuse [] a0)) == None))
          (decreases a0) =
  match a0 with
  | [] -> ()
  | (t0, _) :: rest -> if t0 = t then () else assoc_cells_by cf rest t

let rec step_cells_by_reuse (#n:nat) (cf:cell_fn n) (reuse:Incremental.reuse_fn)
  (a0:Incremental.alive{Incremental.awidth n a0}) (a:Incremental.alive{Incremental.awidth n a})
  : Lemma (requires Ok? (step_cells_by cf Incremental.no_reuse [] a0) /\ wk reuse a a0)
          (ensures step_cells_by cf reuse (Ok?._0 (step_cells_by cf Incremental.no_reuse [] a0)) a
                   == step_cells_by cf Incremental.no_reuse [] a)
          (decreases a) =
  match a with
  | [] -> ()
  | (t, _) :: rest ->
    (if reuse t then assoc_cells_by cf a0 t);
    step_cells_by_reuse cf reuse a0 rest

(* The premise survives a keep: a reusable row kept now was kept then, under its token, because its
   cell is the same cell; one absent then is absent from the kept rows too. *)
let rec assoc_keep_by (#n:nat) (cf:cell_fn n) (a0:Incremental.alive{Incremental.awidth n a0}) (t:Incremental.token)
  : Lemma (requires Ok? (step_cells_by cf Incremental.no_reuse [] a0))
          (ensures (let cs0 = Ok?._0 (step_cells_by cf Incremental.no_reuse [] a0) in
                    match assoc t a0 with
                    | Some r0 ->
                      len r0 = n /\
                      (match cf r0 with
                       | Ok (Bool true) -> assoc t (Incremental.keep_true a0 cs0) == Some r0
                       | _ -> True)
                    | None -> assoc t (Incremental.keep_true a0 cs0) == None))
          (decreases a0) =
  match a0 with
  | [] -> ()
  | (t0, _) :: rest -> if t0 = t then () else assoc_keep_by cf rest t

let rec wk_keep_by (#n:nat) (cf:cell_fn n) (reuse:Incremental.reuse_fn)
  (a0:Incremental.alive{Incremental.awidth n a0}) (a:Incremental.alive{Incremental.awidth n a})
  : Lemma (requires Ok? (step_cells_by cf Incremental.no_reuse [] a0) /\ Ok? (step_cells_by cf Incremental.no_reuse [] a)
                    /\ wk reuse a a0)
          (ensures wk reuse (Incremental.keep_true a (Ok?._0 (step_cells_by cf Incremental.no_reuse [] a)))
                            (Incremental.keep_true a0 (Ok?._0 (step_cells_by cf Incremental.no_reuse [] a0))))
          (decreases a) =
  match a with
  | [] -> ()
  | (t, _) :: rest ->
    (if reuse t then assoc_keep_by cf a0 t);
    wk_keep_by cf reuse a0 rest

(* The premise survives an extension: a reusable row's extended row is the extended prior row —
   its cell packed under the key, the same key on both walks (Phase 470). *)
let rec assoc_extend_by (#n:nat) (cf:cell_fn n) (pr:prims) (k:Incremental.pack_key) (i:option nat) (a0:Incremental.alive{Incremental.awidth n a0}) (t:Incremental.token)
  : Lemma (requires Ok? (step_cells_by cf Incremental.no_reuse [] a0))
          (ensures (let cs0 = Incremental.pack_cache pr k (Ok?._0 (step_cells_by cf Incremental.no_reuse [] a0)) in
                    match assoc t a0 with
                    | Some r0 ->
                      len r0 = n /\
                      (match cf r0 with
                       | Ok v -> assoc t (Incremental.extend i a0 cs0) == Some (Incremental.ext_one i r0 (pack_one pr k.k_type k.k_typed v))
                       | _ -> True)
                    | None -> assoc t (Incremental.extend i a0 cs0) == None))
          (decreases a0) =
  match a0 with
  | [] -> ()
  | (t0, _) :: rest -> if t0 = t then () else assoc_extend_by cf pr k i rest t

let rec wk_extend_by (#n:nat) (cf:cell_fn n) (pr:prims) (k:Incremental.pack_key) (reuse:Incremental.reuse_fn) (i:option nat)
  (a0:Incremental.alive{Incremental.awidth n a0}) (a:Incremental.alive{Incremental.awidth n a})
  : Lemma (requires Ok? (step_cells_by cf Incremental.no_reuse [] a0) /\ Ok? (step_cells_by cf Incremental.no_reuse [] a)
                    /\ wk reuse a a0)
          (ensures wk reuse (Incremental.extend i a (Incremental.pack_cache pr k (Ok?._0 (step_cells_by cf Incremental.no_reuse [] a))))
                            (Incremental.extend i a0 (Incremental.pack_cache pr k (Ok?._0 (step_cells_by cf Incremental.no_reuse [] a0)))))
          (decreases a) =
  match a with
  | [] -> ()
  | (t, _) :: rest ->
    (if reuse t then assoc_extend_by cf pr k i a0 t);
    wk_extend_by cf pr k reuse i a0 rest

(* The premise survives a projection. *)
let rec assoc_proj (cols:schema) (ns:list string) (a0:Incremental.alive) (t:Incremental.token)
  : Lemma (ensures assoc t (proj_alive cols ns a0) == (match assoc t a0 with Some r0 -> Some (proj_row cols ns r0) | None -> None)) =
  match a0 with
  | [] -> ()
  | (t0, _) :: rest -> if t0 = t then () else assoc_proj cols ns rest t

let rec wk_proj (cols:schema) (ns:list string) (reuse:Incremental.reuse_fn) (a a0:Incremental.alive)
  : Lemma (requires wk reuse a a0) (ensures wk reuse (proj_alive cols ns a) (proj_alive cols ns a0)) (decreases a) =
  match a with
  | [] -> ()
  | (t, _) :: rest -> assoc_proj cols ns a0 t; wk_proj cols ns reuse rest a0

(* The premise survives a slice — over a prior source of distinct tokens, whose every sublist answers
   `assoc` as the source does. *)
let rec mem_tokens (a:Incremental.alive) (t:Incremental.token) (r:list cell)
  : Lemma (requires mem_a (t, r) a) (ensures Incremental.mem_token t (tokens_of a)) =
  match a with
  | [] -> ()
  | y :: rest -> if y = (t, r) then () else mem_tokens rest t r

let rec assoc_of_mem (a0:Incremental.alive) (t:Incremental.token) (r:list cell)
  : Lemma (requires distinct a0 /\ mem_a (t, r) a0) (ensures assoc t a0 == Some r) =
  match a0 with
  | [] -> ()
  | (t0, r0) :: rest ->
    if (t0, r0) = (t, r) then ()
    else if t0 = t then mem_tokens rest t r
    else assoc_of_mem rest t r

let rec mem_a_gen (#b:eqtype) (x:b) (l:list b) : Tot bool =
  match l with
  | [] -> false
  | y :: rest -> y = x || mem_a_gen x rest

let rec mem_take (#b:eqtype) (k:nat) (l:list b) (x:b)
  : Lemma (requires mem_a_gen x (take_a k l)) (ensures mem_a_gen x l) (decreases l) =
  match l with
  | [] -> ()
  | y :: rest -> if k = 0 then () else if y = x then () else mem_take (k - 1) rest x

let rec mem_drop (#b:eqtype) (k:nat) (l:list b) (x:b)
  : Lemma (requires mem_a_gen x (drop_a k l)) (ensures mem_a_gen x l) (decreases l) =
  match l with
  | [] -> ()
  | y :: rest -> if k = 0 then () else mem_drop (k - 1) rest x

let rec mem_a_is (x:Incremental.token & list cell) (a:Incremental.alive)
  : Lemma (ensures mem_a x a == mem_a_gen x a) =
  match a with
  | [] -> ()
  | _ :: rest -> mem_a_is x rest

let rec tokens_take (k:nat) (a:Incremental.alive) (t:Incremental.token)
  : Lemma (requires not (Incremental.mem_token t (tokens_of a)))
          (ensures not (Incremental.mem_token t (tokens_of (take_a k a)))) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> if k = 0 then () else tokens_take (k - 1) rest t

let rec distinct_take (k:nat) (a:Incremental.alive)
  : Lemma (requires distinct a) (ensures distinct (take_a k a)) (decreases a) =
  match a with
  | [] -> ()
  | (t, _) :: rest ->
    if k = 0 then ()
    else (tokens_take (k - 1) rest t; distinct_take (k - 1) rest)

let rec distinct_drop (k:nat) (a:Incremental.alive)
  : Lemma (requires distinct a) (ensures distinct (drop_a k a)) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> if k = 0 then () else distinct_drop (k - 1) rest

let rec assoc_mem (a:Incremental.alive) (t:Incremental.token) (r:list cell)
  : Lemma (requires assoc t a == Some r) (ensures mem_a (t, r) a) =
  match a with
  | [] -> ()
  | (t0, _) :: rest -> if t0 = t then () else assoc_mem rest t r

(* A row of a slice of a distinct source is found by `assoc` in the slice exactly as in the source. *)
let wk_slice (off n:int) (reuse:Incremental.reuse_fn) (a a0:Incremental.alive)
  : Lemma (requires wk reuse a a0 /\ distinct a0) (ensures wk reuse (slice off n a) (slice off n a0)) =
  let a' = slice off n a in
  let a0' = slice off n a0 in
  distinct_drop (max0 off) a0;
  distinct_take (max0 n) (drop_a (max0 off) a0);
  let aux (t:Incremental.token) (r:list cell)
    : Lemma (requires mem_a (t, r) a') (ensures ok_row reuse a0' t r) =
    mem_a_is (t, r) a';
    mem_take (max0 n) (drop_a (max0 off) a) (t, r);
    mem_drop (max0 off) a (t, r);
    mem_a_is (t, r) a;
    wk_mem reuse a a0 t r;
    (match assoc t a0' with
     | Some r0 ->
       assoc_mem a0' t r0;
       mem_a_is (t, r0) a0';
       mem_take (max0 n) (drop_a (max0 off) a0) (t, r0);
       mem_drop (max0 off) a0 (t, r0);
       mem_a_is (t, r0) a0;
       assoc_of_mem a0 t r0
     | None -> ()) in
  let aux' (t:Incremental.token) (r:list cell) : Lemma (mem_a (t, r) a' ==> ok_row reuse a0' t r) =
    Classical.move_requires (aux t) r in
  Classical.forall_intro_2 aux';
  wk_of_rows reuse a' a0'

(* Distinct tokens survive every row-preserving or row-dropping step. *)
let rec tokens_keep (a:Incremental.alive) (cs:Incremental.step_cache{len cs = len a}) (t:Incremental.token)
  : Lemma (requires not (Incremental.mem_token t (tokens_of a)))
          (ensures not (Incremental.mem_token t (tokens_of (Incremental.keep_true a cs)))) (decreases a) =
  match a, cs with
  | [], [] -> ()
  | _ :: ar, _ :: cr -> tokens_keep ar cr t

let rec distinct_keep (a:Incremental.alive) (cs:Incremental.step_cache{len cs = len a})
  : Lemma (requires distinct a) (ensures distinct (Incremental.keep_true a cs)) (decreases a) =
  match a, cs with
  | [], [] -> ()
  | (t, _) :: ar, _ :: cr -> tokens_keep ar cr t; distinct_keep ar cr

let rec tokens_extend (i:option nat) (a:Incremental.alive) (cs:Incremental.step_cache{len cs = len a})
  : Lemma (ensures tokens_of (Incremental.extend i a cs) == tokens_of a) (decreases a) =
  match a, cs with
  | [], [] -> ()
  | _ :: ar, _ :: cr -> tokens_extend i ar cr

let rec distinct_extend (i:option nat) (a:Incremental.alive) (cs:Incremental.step_cache{len cs = len a})
  : Lemma (requires distinct a) (ensures distinct (Incremental.extend i a cs)) (decreases a) =
  match a, cs with
  | [], [] -> ()
  | _ :: ar, _ :: cr -> tokens_extend i ar cr; distinct_extend i ar cr

let rec tokens_proj (cols:schema) (ns:list string) (a:Incremental.alive)
  : Lemma (ensures tokens_of (proj_alive cols ns a) == tokens_of a) =
  match a with
  | [] -> ()
  | _ :: rest -> tokens_proj cols ns rest

let rec distinct_proj (cols:schema) (ns:list string) (a:Incremental.alive)
  : Lemma (requires distinct a) (ensures distinct (proj_alive cols ns a)) =
  match a with
  | [] -> ()
  | _ :: rest -> tokens_proj cols ns rest; distinct_proj cols ns rest

(* ======================================================================================
   6. The maintained `GroupBy` — F#: `groupStep`.
   ====================================================================================== *)

(* A group under construction: its key cells and its members in frame order. *)
type group = { g_key : list cell; g_members : Incremental.alive }

(* F#: a row joins the group its key names, or opens a new one after every group so far. *)
let rec add_member (key:list cell) (m:Incremental.token & list cell) (gs:list group) : Tot (list group) =
  match gs with
  | [] -> [{ g_key = key; g_members = [m] }]
  | g :: rest ->
    if g.g_key = key then { g with g_members = app g.g_members [m] } :: rest
    else g :: add_member key m rest

(* F#: the walk over `f.Order` — groups in first-appearance order, members in frame order. *)
let rec groups_acc (cols:schema) (keys:list string) (gs:list group) (a:Incremental.alive) : Tot (list group) (decreases a) =
  match a with
  | [] -> gs
  | (t, r) :: rest -> groups_acc cols keys (add_member (proj_row cols keys r) (t, r) gs) rest

let groups_of (cols:schema) (keys:list string) (a:Incremental.alive) : Tot (list group) = groups_acc cols keys [] a

(* F#: `GroupMembers` / `GroupAggs` / `GroupOrder` — what the state retains per group. *)
type pgroup = { p_key : list cell; p_tokens : list Incremental.token; p_aggs : list cell }

let rec find_key (key:list cell) (ps:list pgroup) : Tot (option pgroup) =
  match ps with
  | [] -> None
  | p :: rest -> if p.p_key = key then Some p else find_key key rest

(* Every member reusable — and a group has a member. *)
let rec every_reuse (reuse:Incremental.reuse_fn) (a:Incremental.alive) : Tot bool =
  match a with
  | [] -> true
  | (t, _) :: rest -> reuse t && every_reuse reuse rest

let all_reuse (reuse:Incremental.reuse_fn) (a:Incremental.alive) : Tot bool =
  match a with
  | [] -> false
  | _ -> every_reuse reuse a

(* F#: one `GroupAgg.Stream` per aggregate fed the group's members in order, else `aggregateCells`
   — the reference's aggregate over the member cells of the aggregate's column, the first error in
   aggregate order. The source column resolved before any group (`group_by_cols`), so the misses here
   are unreachable. *)
let rec agg_cells_of (mp:mprims) (cols:schema) (aggs:list agg) (members:Incremental.alive)
  : Tot (r:outcome (list cell) eval_error{match r with Ok cs -> len cs = len aggs | Error _ -> true}) =
  match aggs with
  | [] -> Ok []
  | a :: rest ->
    let ty = (match assoc a.a_of cols with Some ty -> ty | None -> StringType) in
    let j = (match index_of a.a_of cols with Some j -> j | None -> 0) in
    (match mp.agg_eval a.a_fn ty (Relational.col_cells j (Incremental.rows_of members)) with
     | Error e -> Error e
     | Ok c ->
       (match agg_cells_of mp cols rest members with
        | Error e -> Error e
        | Ok cs -> Ok (c :: cs)))

(* F#: `groupStep`'s reuse — every member stable, the group continues a prior one, and its member
   tokens are the prior's in order (`sameMembers`); else the recompute. *)
let group_aggs (mp:mprims) (reuse:Incremental.reuse_fn) (prior:list pgroup) (cols:schema) (aggs:list agg) (g:group)
  : Tot (r:outcome (list cell & bool) eval_error{match r with Ok (cs, _) -> len cs = len aggs | Error _ -> true}) =
  let found = (if all_reuse reuse g.g_members then find_key g.g_key prior else None) in
  match found with
  | Some pg ->
    if pg.p_tokens = tokens_of g.g_members && len pg.p_aggs = len aggs then Ok (pg.p_aggs, true)
    else (match agg_cells_of mp cols aggs g.g_members with Error e -> Error e | Ok cs -> Ok (cs, false))
  | None -> (match agg_cells_of mp cols aggs g.g_members with Error e -> Error e | Ok cs -> Ok (cs, false))

(* One output group: its key, its member tokens, its aggregates, and whether they were reused. *)
type gout = { o_key : list cell; o_tokens : list Incremental.token; o_aggs : list cell; o_reused : bool }

let rec all_agg_width (aggs:list agg) (os:list gout) : Tot bool =
  match os with
  | [] -> true
  | o :: rest -> len o.o_aggs = len aggs && all_agg_width aggs rest

let rec groups_out (mp:mprims) (reuse:Incremental.reuse_fn) (prior:list pgroup) (cols:schema) (aggs:list agg) (gs:list group)
  : Tot (r:outcome (list gout) eval_error{match r with Ok os -> all_agg_width aggs os | Error _ -> true}) =
  match gs with
  | [] -> Ok []
  | g :: rest ->
    (match group_aggs mp reuse prior cols aggs g with
     | Error e -> Error e
     | Ok (cs, reused) ->
       (match groups_out mp reuse prior cols aggs rest with
        | Error e -> Error e
        | Ok os -> Ok ({ o_key = g.g_key; o_tokens = tokens_of g.g_members; o_aggs = cs; o_reused = reused } :: os)))

(* The group table's rows: the key cells then the aggregates (F#: `groupKeys @ aggCells`). *)
let rec grows (os:list gout) : Tot (list (list cell)) =
  match os with
  | [] -> []
  | o :: rest -> app o.o_key o.o_aggs :: grows rest

(* The group table walked on: one token per key. *)
let rec galive (mp:mprims) (os:list gout) : Tot Incremental.alive =
  match os with
  | [] -> []
  | o :: rest -> (mp.key_token o.o_key, app o.o_key o.o_aggs) :: galive mp rest

let rec pgroups_of (os:list gout) : Tot (list pgroup) =
  match os with
  | [] -> []
  | o :: rest -> { p_key = o.o_key; p_tokens = o.o_tokens; p_aggs = o.o_aggs } :: pgroups_of rest

(* The tokens of the groups NOT reused — the tail's named rows (F#: `not RecomputedAt[g]` is the
   stable group; a new group is unstable, and the model's cache misses it either way). *)
let rec unreused_tokens (mp:mprims) (os:list gout) : Tot (list Incremental.token) =
  match os with
  | [] -> []
  | o :: rest -> if o.o_reused then unreused_tokens mp rest else mp.key_token o.o_key :: unreused_tokens mp rest

let rec all_unreused (os:list gout) : Tot bool =
  match os with
  | [] -> true
  | o :: rest -> not o.o_reused && all_unreused rest

(* The reuse predicate the tail walks under: nothing where no group was reused (literally
   `no_reuse`, so the prime's tail is the prime's), else every token but the recomputed groups'. *)
let tail_reuse (mp:mprims) (os:list gout) : Tot Incremental.reuse_fn =
  if all_unreused os then Incremental.no_reuse else Incremental.reuse_of (Some (unreused_tokens mp os))

let rec index_of_assoc (#b:Type0) (n:string) (l:list (string & b))
  : Lemma (ensures Some? (index_of n l) <==> Some? (assoc n l)) =
  match l with
  | [] -> ()
  | (m, _) :: rest -> if m = n then () else index_of_assoc n rest

let rec len_lookup_cols (cols:schema) (ns:list string)
  : Lemma (requires None? (Relational.first_unknown cols ns)) (ensures len (Relational.lookup_cols cols ns) == len ns) =
  match ns with
  | [] -> ()
  | n :: rest -> index_of_assoc n cols; len_lookup_cols cols rest

let rec len_agg_cols (cols:schema) (aggs:list agg)
  : Lemma (requires Ok? (Relational.agg_cols cols aggs)) (ensures len (Ok?._0 (Relational.agg_cols cols aggs)) == len aggs) =
  match aggs with
  | [] -> ()
  | _ :: rest -> len_agg_cols cols rest

let len_group_by_cols (cols:schema) (keys:list string) (aggs:list agg)
  : Lemma (requires Ok? (Relational.group_by_cols cols keys aggs))
          (ensures len (Ok?._0 (Relational.group_by_cols cols keys aggs)) == len keys + len aggs) =
  len_lookup_cols cols keys;
  len_agg_cols cols aggs;
  len_app (Relational.lookup_cols cols keys) (Ok?._0 (Relational.agg_cols cols aggs))

let rec all_key_width (keys:list string) (os:list gout) : Tot bool =
  match os with
  | [] -> true
  | o :: rest -> len o.o_key = len keys && all_key_width keys rest

let rec all_gkey_width (keys:list string) (gs:list group) : Tot bool =
  match gs with
  | [] -> true
  | g :: rest -> len g.g_key = len keys && all_gkey_width keys rest

let rec add_member_width (keys:list string) (key:list cell{len key = len keys}) (m:Incremental.token & list cell) (gs:list group)
  : Lemma (requires all_gkey_width keys gs) (ensures all_gkey_width keys (add_member key m gs)) =
  match gs with
  | [] -> ()
  | g :: rest -> if g.g_key = key then () else add_member_width keys key m rest

let rec groups_key_width (cols:schema) (keys:list string) (gs:list group) (a:Incremental.alive)
  : Lemma (requires all_gkey_width keys gs) (ensures all_gkey_width keys (groups_acc cols keys gs a)) (decreases a) =
  match a with
  | [] -> ()
  | (t, r) :: rest -> len_proj_row cols keys r; add_member_width keys (proj_row cols keys r) (t, r) gs; groups_key_width cols keys (add_member (proj_row cols keys r) (t, r) gs) rest

let rec groups_out_key_width (mp:mprims) (reuse:Incremental.reuse_fn) (prior:list pgroup) (cols:schema) (keys:list string) (aggs:list agg) (gs:list group)
  : Lemma (requires all_gkey_width keys gs /\ Ok? (groups_out mp reuse prior cols aggs gs))
          (ensures all_key_width keys (Ok?._0 (groups_out mp reuse prior cols aggs gs))) =
  match gs with
  | [] -> ()
  | _ :: rest -> groups_out_key_width mp reuse prior cols keys aggs rest

let rec awidth_galive (mp:mprims) (keys:list string) (aggs:list agg) (os:list gout)
  : Lemma (requires all_key_width keys os /\ all_agg_width aggs os)
          (ensures Incremental.awidth (len keys + len aggs) (galive mp os)) =
  match os with
  | [] -> ()
  | o :: rest -> len_app o.o_key o.o_aggs; awidth_galive mp keys aggs rest

let rec all_width_grows (keys:list string) (aggs:list agg) (os:list gout)
  : Lemma (requires all_key_width keys os /\ all_agg_width aggs os)
          (ensures all_width (len keys + len aggs) (grows os)) =
  match os with
  | [] -> ()
  | o :: rest -> len_app o.o_key o.o_aggs; all_width_grows keys aggs rest

(* The group step's result: the group table walked on, and the groups it was built from. *)
type gres = { gs_out : Incremental.walked; gs_os : list gout }

(* F#: `groupStep` — the schema and its refusals as the reference reads them (`group_by_cols`), then
   the groups, then the table walked on. *)
let group_step (mp:mprims) (reuse:Incremental.reuse_fn) (prior:list pgroup) (cols:schema) (keys:list string) (aggs:list agg)
  (a:Incremental.alive{Incremental.awidth (len cols) a})
  : Tot (outcome gres eval_error) =
  match Relational.group_by_cols cols keys aggs with
  | Error e -> Error e
  | Ok out ->
    let gs = groups_of cols keys a in
    (match groups_out mp reuse prior cols aggs gs with
     | Error e -> Error e
     | Ok os ->
       len_group_by_cols cols keys aggs;
       groups_key_width cols keys [] a;
       groups_out_key_width mp reuse prior cols keys aggs gs;
       awidth_galive mp keys aggs os;
       Ok ({ gs_out = Incremental.W out (galive mp os); gs_os = os }))

(* ======================================================================================
   7. The walk — F#: `walk` over the prefix steps (and the tail), and the state it leaves.
   ====================================================================================== *)

(* One evaluating step's retained state: its cells under the rows' tokens (F#: `StepCells[k]`), the
   key a `Derive` packed them under — the type it decided and the pack's verdict (D37, D38) — and
   the joined relation's key index where a join (F#: `JoinKeys`). *)
type entry = { e_cells : Incremental.step_cache; e_pack : option Incremental.pack_key; e_keys : option (list (list cell)) }

let prior_head (prior:list entry) : Tot entry =
  match prior with
  | [] -> { e_cells = []; e_pack = None; e_keys = None }
  | e :: _ -> e

let prior_rest (prior:list entry) : Tot (list entry) =
  match prior with
  | [] -> []
  | _ :: rest -> rest

(* F#: `evalSource resolve src` — the joined relation's rows. *)
let right_rows (resolve:Relational.resolver) (src:data_source) : Tot (outcome (list (list cell)) eval_error) =
  match src with
  | Embedded f -> Ok f.rows
  | Ref name -> (match resolve name with Ok f -> Ok f.rows | Error e -> Error e)

let rec lnames (on:list (string & string)) : Tot (list string) =
  match on with
  | [] -> []
  | (l, _) :: rest -> l :: lnames rest

let rec rnames (on:list (string & string)) : Tot (list string) =
  match on with
  | [] -> []
  | (_, r) :: rest -> r :: rnames rest

(* F#: `rightKeys |> Array.exists (joinKeysMatch leftKeys)`. *)
let rec matches (mp:mprims) (lk:list cell) (rks:list (list cell)) : Tot bool =
  match rks with
  | [] -> false
  | rk :: rest -> mp.key_eq lk rk || matches mp lk rest

(* The verdict of one left row: kept iff it matches, for `Semi`; iff it does not, for `Anti`. *)
let verdict (mp:mprims) (cols:schema) (on:list (string & string)) (rks:list (list cell)) (keep:bool) : Tot (list cell -> bool) =
  fun r -> matches mp (proj_row cols (lnames on) r) rks = keep

(* The walk's result: the frame, this walk's entries in step order, and whether reuse was switched
   off along the way (a retyped derive, a window) — so the step after the walk knows what to reuse. *)
type mres = { r_out : Incremental.walked; r_entries : list entry; r_switched : bool }

let rec len_project_cols (cols:schema) (pairs:list (string & string))
  : Lemma (requires Ok? (Relational.project_cols cols pairs)) (ensures len (Ok?._0 (Relational.project_cols cols pairs)) == len pairs) =
  match pairs with
  | [] -> ()
  | _ :: rest -> len_project_cols cols rest

let rec len_names (#b:Type0) (l:list (string & b)) : Lemma (ensures len (names l) == len l) =
  match l with
  | [] -> ()
  | _ :: t -> len_names t

(* A window records no entry (F#: no `Steps` array), so the prior entries pass through it. *)
let prior_rest_window (prior:list entry) : Tot (list entry) = prior

let rec mwalk (mp:mprims) (rp:Relational.rprims) (pr:prims) (resolve:Relational.resolver) (env:param_env)
  (reuse:Incremental.reuse_fn) (prior:list entry) (cols:schema) (a:Incremental.alive{Incremental.awidth (len cols) a}) (p:list mstep)
  : Tot (outcome mres eval_error) (decreases p) =
  match p with
  | [] -> Ok ({ r_out = Incremental.W cols a; r_entries = []; r_switched = false })
  | MFilter pred :: rest ->
    (match step_cells_by (expr_fn pr env cols pred) reuse (prior_head prior).e_cells a with
     | Error err -> Error err
     | Ok cs ->
       Incremental.awidth_keep_true (len cols) a cs;
       (match mwalk mp rp pr resolve env reuse (prior_rest prior) cols (Incremental.keep_true a cs) rest with
        | Error err -> Error err
        | Ok st -> Ok ({ st with r_entries = ({ e_cells = cs; e_pack = None; e_keys = None } :: st.r_entries) })))
  | MDerive name x :: rest ->
    (match derived_typing_in env cols x with
     | Refused -> Error (float_beside_decimal name)
     | dt ->
       (match step_cells_by (expr_fn pr env cols x) reuse (prior_head prior).e_cells a with
        | Error err -> Error err
        | Ok cs ->
          (match column_type_by dt name (Incremental.cells_of cs) with
           | Error err -> Error err
           | Ok ty ->
             let k = Incremental.pack_key_of pr ty cs in
             let i = index_of name cols in
             Incremental.awidth_extend (len cols) i a (Incremental.pack_cache pr k cs);
             (* D37, D38: the column packed as before keeps reusing; retyped, or carried the other way,
                nothing is reused from here. *)
             let same = ((prior_head prior).e_pack = Some k) in
             let reuse' = if same then reuse else Incremental.no_reuse in
             (match mwalk mp rp pr resolve env reuse' (prior_rest prior) (Incremental.ext_schema i name ty cols) (Incremental.extend i a (Incremental.pack_cache pr k cs)) rest with
              | Error err -> Error err
              | Ok st -> Ok ({ st with r_entries = ({ e_cells = cs; e_pack = Some k; e_keys = None } :: st.r_entries);
                                       r_switched = (st.r_switched || not same) })))))
  | MProject pairs :: rest ->
    (match Relational.project_step cols pairs with
     | Error e -> Error e
     | Ok out ->
       len_project_cols cols pairs;
       awidth_proj cols (names pairs) a;
       len_names pairs;
       mwalk mp rp pr resolve env reuse prior out (proj_alive cols (names pairs) a) rest)
  | MLimit n off :: rest ->
    awidth_drop (len cols) (max0 off) a;
    awidth_take (len cols) (max0 n) (drop_a (max0 off) a);
    mwalk mp rp pr resolve env reuse prior cols (slice off n a) rest
  | MWindow spec :: rest ->
    (match Relational.window_cols rp cols spec with
     | Error e -> Error e
     | Ok out ->
       (match mp.win_cells cols spec (Incremental.rows_of a) with
        | Error e -> Error e
        | Ok ws ->
          Incremental.len_rows_of a;
          let cs = pair_cells a ws in
          Incremental.awidth_extend (len cols) None a cs;
          len_app cols [(spec.w_as, Relational.window_type cols spec)];
          (* F#: `Array.fill r.Stable 0 r.Stable.Length false` — nothing is reused after a window. *)
          (match mwalk mp rp pr resolve env Incremental.no_reuse (prior_rest_window prior) out (Incremental.extend None a cs) rest with
           | Error err -> Error err
           | Ok st -> Ok ({ st with r_switched = true }))))
  | MJoin src on keep :: rest ->
    (match Relational.source_cols resolve src with
     | Error e -> Error e
     | Ok rcols ->
       (match Relational.join_cols rp cols rcols on (if keep then Semi else Anti) with
        | Error e -> Error e
        | Ok _ ->
          (match right_rows resolve src with
           | Error e -> Error e
           | Ok rrows ->
             let rks = proj_rows rcols (rnames on) rrows in
             (* F#: `relationMoved` — a stable row's verdict is reused only under the prior key index. *)
             let reuse' = if (prior_head prior).e_keys = Some rks then reuse else Incremental.no_reuse in
             let vf = verdict mp cols on rks keep in
             bool_step_ok (len cols) vf reuse' (prior_head prior).e_cells a;
             let vs = Ok?._0 (step_cells_by (bool_fn (len cols) vf) reuse' (prior_head prior).e_cells a) in
             Incremental.awidth_keep_true (len cols) a vs;
             (match mwalk mp rp pr resolve env reuse (prior_rest prior) cols (Incremental.keep_true a vs) rest with
              | Error err -> Error err
              | Ok st -> Ok ({ st with r_entries = ({ e_cells = vs; e_pack = None; e_keys = Some rks } :: st.r_entries) })))))

(* The whole pipeline — F#: `runIncremental`: the prefix, the group step, the tail. *)
type mstate = { m_out : Incremental.walked; m_entries : list entry; m_groups : list pgroup; m_tail : list entry }

let empty_state (cols:schema) : Tot mstate =
  { m_out = Incremental.W cols []; m_entries = []; m_groups = []; m_tail = [] }

let mrun (mp:mprims) (rp:Relational.rprims) (pr:prims) (resolve:Relational.resolver) (env:param_env)
  (reuse:Incremental.reuse_fn) (prior:mstate) (cols:schema) (a:Incremental.alive{Incremental.awidth (len cols) a}) (p:mpipe)
  : Tot (outcome mstate eval_error) =
  match mwalk mp rp pr resolve env reuse prior.m_entries cols a p.prefix with
  | Error e -> Error e
  | Ok st ->
    (match p.grouped with
     | None -> Ok ({ m_out = st.r_out; m_entries = st.r_entries; m_groups = []; m_tail = [] })
     | Some (keys, aggs, tail) ->
       let Incremental.W cols1 a1 = st.r_out in
       let reuse1 = if st.r_switched then Incremental.no_reuse else reuse in
       (match group_step mp reuse1 prior.m_groups cols1 keys aggs a1 with
        | Error e -> Error e
        | Ok gr ->
          let Incremental.W gcols ga = gr.gs_out in
          let os = gr.gs_os in
          (match mwalk mp rp pr resolve env (tail_reuse mp os) prior.m_tail gcols ga tail with
           | Error e -> Error e
           | Ok ts -> Ok ({ m_out = ts.r_out; m_entries = st.r_entries; m_groups = pgroups_of os; m_tail = ts.r_entries }))))

(* F#: `Incremental.prime` — no prior state, every row evaluated. *)
let mprime (mp:mprims) (rp:Relational.rprims) (pr:prims) (resolve:Relational.resolver) (env:param_env)
  (cols:schema) (a:Incremental.alive{Incremental.awidth (len cols) a}) (p:mpipe) : Tot (outcome mstate eval_error) =
  mrun mp rp pr resolve env Incremental.no_reuse (empty_state cols) cols a p

(* F#: `Incremental.refresh` — reusing every unnamed row against the prior state. *)
let mrefresh (mp:mprims) (rp:Relational.rprims) (pr:prims) (resolve:Relational.resolver) (env:param_env)
  (st:mstate) (named:option (list Incremental.token))
  (cols:schema) (a:Incremental.alive{Incremental.awidth (len cols) a}) (p:mpipe) : Tot (outcome mstate eval_error) =
  mrun mp rp pr resolve env (Incremental.reuse_of named) st cols a p

(* ======================================================================================
   8. The reference — Pipeline's driver over `Relational.other_of` with this module's row functions.
   ====================================================================================== *)

let rec untok (rows:list (list cell)) : Tot Incremental.alive =
  match rows with
  | [] -> []
  | r :: rest -> ("", r) :: untok rest

let fit (n:nat) (rs:list (list cell)) : Tot (outcome (rs':list (list cell){all_width n rs'}) eval_error) =
  if all_width n rs then Ok rs else Error (TypeError "the row function's rows are not as wide as the schema")

(* THE ROW FUNCTIONS: `evalProject`'s columns, `evalGroupBy`'s groups and aggregates, `evalWindow`'s
   column, `evalLimit`'s slice, `evalJoin`'s filtering kinds — each over the frame's rows. *)
let mrows (mp:mprims) (resolve:Relational.resolver) : Tot Relational.row_fn =
  fun f t out ->
    match t with
    | Project pairs -> fit (len out) (proj_rows f.cols (names pairs) f.rows)
    | GroupBy keys aggs ->
      (match groups_out mp Incremental.no_reuse [] f.cols aggs (groups_of f.cols keys (untok f.rows)) with
       | Error e -> Error e
       | Ok os -> fit (len out) (grows os))
    | Window spec ->
      (match mp.win_cells f.cols spec f.rows with
       | Error e -> Error e
       | Ok ws -> fit (len out) (zip_append f.rows ws))
    | Limit (SlotLit n) (SlotLit off) -> fit (len out) (slice off n f.rows)
    | Join src on Semi -> (match right_rows resolve src, Relational.source_cols resolve src with
                           | Ok rrows, Ok rcols -> fit (len out) (keep_rows_by (verdict mp f.cols on (proj_rows rcols (rnames on) rrows) true) f.rows)
                           | Error e, _ -> Error e
                           | _, Error e -> Error e)
    | Join src on Anti -> (match right_rows resolve src, Relational.source_cols resolve src with
                           | Ok rrows, Ok rcols -> fit (len out) (keep_rows_by (verdict mp f.cols on (proj_rows rcols (rnames on) rrows) false) f.rows)
                           | Error e, _ -> Error e
                           | _, Error e -> Error e)
    | _ -> Error (TypeError "a verb this model does not reach")

let mother (mp:mprims) (rp:Relational.rprims) (resolve:Relational.resolver) (env:param_env) : Tot other_fn =
  Relational.other_of rp resolve env (mrows mp resolve)

(* ======================================================================================
   9. Priming is full evaluation — `mprime_is_full`.
   ====================================================================================== *)

let rec go_app (#e:Type0) (step:step_fn e) (f:wframe) (n:nat) (p q:list transform)
  : Lemma (ensures go step f n (app p q) == (match go step f n p with
                                            | Ok (f', n') -> go step f' n' q
                                            | Error err -> Error err))
          (decreases p) =
  match p with
  | [] -> ()
  | s :: rest ->
    (match step f s with
     | Error _ -> ()
     | Ok f' -> go_app step f' (n + cost_of f s) rest q)

(* The group arm reads only the rows: the groups of the alive rows under any tokens have the rows'
   groups, member rows for member rows. *)
let rec retok_groups (gs:list group) : Tot (list group) =
  match gs with
  | [] -> []
  | g :: rest -> { g with g_members = untok (Incremental.rows_of g.g_members) } :: retok_groups rest

let rec untok_app (rows more:list (list cell)) : Lemma (ensures untok (app rows more) == app (untok rows) (untok more)) =
  match rows with
  | [] -> ()
  | _ :: rest -> untok_app rest more

let rec rows_of_app (a b:Incremental.alive) : Lemma (ensures Incremental.rows_of (app a b) == app (Incremental.rows_of a) (Incremental.rows_of b)) =
  match a with
  | [] -> ()
  | _ :: rest -> rows_of_app rest b

let rec add_member_retok (key:list cell) (t:Incremental.token) (r:list cell) (gs:list group)
  : Lemma (ensures add_member key ("", r) (retok_groups gs) == retok_groups (add_member key (t, r) gs)) =
  match gs with
  | [] -> ()
  | g :: rest ->
    if g.g_key = key then (rows_of_app g.g_members [(t, r)]; untok_app (Incremental.rows_of g.g_members) [r])
    else add_member_retok key t r rest

let rec groups_acc_retok (cols:schema) (keys:list string) (gs:list group) (a:Incremental.alive)
  : Lemma (ensures groups_acc cols keys (retok_groups gs) (untok (Incremental.rows_of a)) == retok_groups (groups_acc cols keys gs a))
          (decreases a) =
  match a with
  | [] -> ()
  | (t, r) :: rest ->
    add_member_retok (proj_row cols keys r) t r gs;
    groups_acc_retok cols keys (add_member (proj_row cols keys r) (t, r) gs) rest

let rec rows_of_untok (rows:list (list cell)) : Lemma (ensures Incremental.rows_of (untok rows) == rows) =
  match rows with
  | [] -> ()
  | _ :: rest -> rows_of_untok rest

(* Reusing nothing reuses no group. *)
let all_reuse_no (a:Incremental.alive) : Lemma (ensures not (all_reuse Incremental.no_reuse a)) =
  match a with
  | [] -> ()
  | _ :: _ -> ()

let rec all_unreused_fresh (mp:mprims) (cols:schema) (aggs:list agg) (gs:list group)
  : Lemma (requires Ok? (groups_out mp Incremental.no_reuse [] cols aggs gs))
          (ensures all_unreused (Ok?._0 (groups_out mp Incremental.no_reuse [] cols aggs gs))) =
  match gs with
  | [] -> ()
  | g :: rest -> all_reuse_no g.g_members; all_unreused_fresh mp cols aggs rest

(* The aggregates read the member rows alone. *)
let rec agg_cells_rows (mp:mprims) (cols:schema) (aggs:list agg) (ms ms':Incremental.alive)
  : Lemma (requires Incremental.rows_of ms == Incremental.rows_of ms')
          (ensures agg_cells_of mp cols aggs ms == agg_cells_of mp cols aggs ms') =
  match aggs with
  | [] -> ()
  | _ :: rest -> agg_cells_rows mp cols rest ms ms'

(* With nothing to reuse, the output of a group depends on its member rows alone. *)
let rec groups_out_retok (mp:mprims) (cols:schema) (aggs:list agg) (gs:list group)
  : Lemma (ensures (match groups_out mp Incremental.no_reuse [] cols aggs (retok_groups gs), groups_out mp Incremental.no_reuse [] cols aggs gs with
                    | Ok os', Ok os -> grows os' == grows os
                    | Error e', Error e -> e' == e
                    | _ -> False)) =
  match gs with
  | [] -> ()
  | g :: rest ->
    rows_of_untok (Incremental.rows_of g.g_members);
    all_reuse_no g.g_members;
    all_reuse_no (untok (Incremental.rows_of g.g_members));
    agg_cells_rows mp cols aggs (untok (Incremental.rows_of g.g_members)) g.g_members;
    groups_out_retok mp cols aggs rest

let rec rows_of_galive (mp:mprims) (os:list gout) : Lemma (ensures Incremental.rows_of (galive mp os) == grows os) =
  match os with
  | [] -> ()
  | _ :: rest -> rows_of_galive mp rest

let rec rows_of_pair_extend (a:Incremental.alive) (ws:list cell{len ws = len a})
  : Lemma (ensures (Incremental.len_rows_of a;
                    Incremental.rows_of (Incremental.extend None a (pair_cells a ws)) == zip_append (Incremental.rows_of a) ws))
          (decreases a) =
  Incremental.len_rows_of a;
  match a, ws with
  | [], [] -> ()
  | _ :: ar, _ :: wr -> rows_of_pair_extend ar wr

let rec mwalk_is_go (mp:mprims) (rp:Relational.rprims) (pr:prims) (resolve:Relational.resolver) (env:param_env)
  (cols:schema) (a:Incremental.alive{Incremental.awidth (len cols) a}) (p:list mstep) (n:nat)
  : Lemma (ensures (match mwalk mp rp pr resolve env Incremental.no_reuse [] cols a p,
                          go (eval_step pr (mother mp rp resolve env) env) (Incremental.frame_of cols a) n (embed_steps p) with
                    | Ok st, Ok (f, _) -> Incremental.frame_of_walked st.r_out == f
                    | Error e, Error e' -> e == e'
                    | _ -> False))
          (decreases p) =
  Incremental.rows_of_width (len cols) a;
  let f0 = Incremental.frame_of cols a in
  match p with
  | [] -> ()
  | MFilter pred :: rest ->
    step_cells_by_is pr env cols pred Incremental.no_reuse [] a;
    Incremental.cells_filter pr env cols pred a;
    (match step_cells_by (expr_fn pr env cols pred) Incremental.no_reuse [] a with
     | Error _ -> ()
     | Ok cs ->
       Incremental.awidth_keep_true (len cols) a cs;
       mwalk_is_go mp rp pr resolve env cols (Incremental.keep_true a cs) rest (n + cost_of f0 (Filter pred)))
  | MDerive name x :: rest ->
    (match derived_typing_in env cols x with
     | Refused -> ()
     | dt ->
       step_cells_by_is pr env cols x Incremental.no_reuse [] a;
       Incremental.cells_derive pr env cols x a;
       (match step_cells_by (expr_fn pr env cols x) Incremental.no_reuse [] a with
        | Error _ -> ()
        | Ok cs ->
          (match column_type_by dt name (Incremental.cells_of cs) with
           | Error _ -> ()
           | Ok ty ->
             let k = Incremental.pack_key_of pr ty cs in
             let i = index_of name cols in
             Incremental.pack_cache_is_pack_cells pr ty cs;
             Incremental.rows_of_extend i a (Incremental.pack_cache pr k cs);
             Incremental.awidth_extend (len cols) i a (Incremental.pack_cache pr k cs);
             mwalk_is_go mp rp pr resolve env (Incremental.ext_schema i name ty cols) (Incremental.extend i a (Incremental.pack_cache pr k cs)) rest
               (n + cost_of f0 (Derive name x)))))
  | MProject pairs :: rest ->
    (match Relational.project_step cols pairs with
     | Error _ -> ()
     | Ok out ->
       len_project_cols cols pairs;
       len_names pairs;
       awidth_proj cols (names pairs) a;
       rows_of_proj cols (names pairs) a;
       all_width_proj cols (names pairs) (Incremental.rows_of a);
       mwalk_is_go mp rp pr resolve env out (proj_alive cols (names pairs) a) rest (n + cost_of f0 (Project pairs)))
  | MLimit k off :: rest ->
    awidth_drop (len cols) (max0 off) a;
    awidth_take (len cols) (max0 k) (drop_a (max0 off) a);
    rows_of_drop (max0 off) a;
    rows_of_take (max0 k) (drop_a (max0 off) a);
    all_width_drop (len cols) (max0 off) (Incremental.rows_of a);
    all_width_take (len cols) (max0 k) (drop_a (max0 off) (Incremental.rows_of a));
    mwalk_is_go mp rp pr resolve env cols (slice off k a) rest (n + cost_of f0 (Limit (SlotLit k) (SlotLit off)))
  | MWindow spec :: rest ->
    (match Relational.window_cols rp cols spec with
     | Error _ -> ()
     | Ok out ->
       (match mp.win_cells cols spec (Incremental.rows_of a) with
        | Error _ -> ()
        | Ok ws ->
          Incremental.len_rows_of a;
          let cs = pair_cells a ws in
          Incremental.awidth_extend (len cols) None a cs;
          len_app cols [(spec.w_as, Relational.window_type cols spec)];
          rows_of_pair_extend a ws;
          width_append (len cols) (Incremental.rows_of a) ws;
          mwalk_is_go mp rp pr resolve env out (Incremental.extend None a cs) rest (n + cost_of f0 (Window spec))))
  | MJoin src on keep :: rest ->
    (match Relational.source_cols resolve src with
     | Error _ -> ()
     | Ok rcols ->
       (match Relational.join_cols rp cols rcols on (if keep then Semi else Anti) with
        | Error _ -> ()
        | Ok _ ->
          (match right_rows resolve src with
           | Error _ -> ()
           | Ok rrows ->
             let rks = proj_rows rcols (rnames on) rrows in
             let vf = verdict mp cols on rks keep in
             bool_step_ok (len cols) vf Incremental.no_reuse [] a;
             let vs = Ok?._0 (step_cells_by (bool_fn (len cols) vf) Incremental.no_reuse [] a) in
             rows_of_keep_by (len cols) vf a;
             all_width_keep_rows (len cols) vf (Incremental.rows_of a);
             Incremental.awidth_keep_true (len cols) a vs;
             mwalk_is_go mp rp pr resolve env cols (Incremental.keep_true a vs) rest (n + cost_of f0 (Join src on (if keep then Semi else Anti))))))

(* The group step is the reference's `GroupBy` over the walked frame's rows. *)
let group_step_is_other (mp:mprims) (rp:Relational.rprims) (resolve:Relational.resolver) (env:param_env)
  (cols:schema) (keys:list string) (aggs:list agg) (a:Incremental.alive{Incremental.awidth (len cols) a})
  : Lemma (ensures (match group_step mp Incremental.no_reuse [] cols keys aggs a,
                          mother mp rp resolve env (Incremental.frame_of cols a) (GroupBy keys aggs) with
                    | Ok gr, Ok f -> Incremental.frame_of_walked gr.gs_out == f
                    | Error e, Error e' -> e == e'
                    | _ -> False)) =
  Incremental.rows_of_width (len cols) a;
  match Relational.group_by_cols cols keys aggs with
  | Error _ -> ()
  | Ok out ->
    let gs = groups_of cols keys a in
    groups_acc_retok cols keys [] a;
    groups_out_retok mp cols aggs gs;
    (match groups_out mp Incremental.no_reuse [] cols aggs gs with
     | Error _ -> ()
     | Ok os ->
       len_group_by_cols cols keys aggs;
       groups_key_width cols keys [] a;
       groups_out_key_width mp Incremental.no_reuse [] cols keys aggs gs;
       all_width_grows keys aggs os;
       rows_of_galive mp os)

(* THEOREM. Priming computes what the reference evaluator computes over `mother`: the same frame,
   or the same error, over every cell primitive, every environment and every well-formed source. *)
let mprime_is_full (mp:mprims) (rp:Relational.rprims) (pr:prims) (resolve:Relational.resolver) (env:param_env)
  (cols:schema) (a:Incremental.alive{Incremental.awidth (len cols) a}) (p:mpipe)
  : Lemma (ensures (match mprime mp rp pr resolve env cols a p,
                          eval_uncounted pr (mother mp rp resolve env) env (embed_pipe p) (Incremental.frame_of cols a) with
                    | Ok st, Ok f -> Incremental.frame_of_walked st.m_out == f
                    | Error e, Error e' -> e == e'
                    | _ -> False)) =
  let other = mother mp rp resolve env in
  let step = eval_step pr other env in
  let f0 = Incremental.frame_of cols a in
  mwalk_is_go mp rp pr resolve env cols a p.prefix 0;
  assert (eval_uncounted pr other env (embed_pipe p) f0 == result_map fst (go step f0 0 (embed_pipe p)));
  match p.grouped with
  | None ->
    assert (embed_pipe p == embed_steps p.prefix);
    (match mwalk mp rp pr resolve env Incremental.no_reuse [] cols a p.prefix, go step f0 0 (embed_steps p.prefix) with
     | Ok st, Ok (f, _) -> assert (mprime mp rp pr resolve env cols a p == Ok ({ m_out = st.r_out; m_entries = st.r_entries; m_groups = []; m_tail = [] }))
     | Error e, Error e' -> assert (mprime mp rp pr resolve env cols a p == Error e)
     | _ -> ())
  | Some (keys, aggs, tail) ->
    assert (embed_pipe p == app (embed_steps p.prefix) (GroupBy keys aggs :: embed_steps tail));
    go_app step f0 0 (embed_steps p.prefix) (GroupBy keys aggs :: embed_steps tail);
    (match mwalk mp rp pr resolve env Incremental.no_reuse [] cols a p.prefix, go step f0 0 (embed_steps p.prefix) with
     | Error e, Error e' -> assert (mprime mp rp pr resolve env cols a p == Error e)
     | Ok st, Ok (f1, n1) ->
       let Incremental.W cols1 a1 = st.r_out in
       assert (f1 == Incremental.frame_of cols1 a1);
       group_step_is_other mp rp resolve env cols1 keys aggs a1;
       assert (step f1 (GroupBy keys aggs) == other f1 (GroupBy keys aggs));
       (match group_step mp Incremental.no_reuse [] cols1 keys aggs a1, other f1 (GroupBy keys aggs) with
        | Error e, Error e' -> assert (mprime mp rp pr resolve env cols a p == Error e)
        | Ok gr, Ok f2 ->
          let Incremental.W gcols ga = gr.gs_out in
          let os = gr.gs_os in
          assert (f2 == Incremental.frame_of gcols ga);
          mwalk_is_go mp rp pr resolve env gcols ga tail (n1 + cost_of f1 (GroupBy keys aggs));
          (match mwalk mp rp pr resolve env (tail_reuse mp os) [] gcols ga tail with
           | Error _ -> all_unreused_fresh mp cols1 aggs (groups_of cols1 keys a1)
           | Ok _ -> all_unreused_fresh mp cols1 aggs (groups_of cols1 keys a1))
        | _ -> ())
     | _ -> ())

(* ======================================================================================
   10. A refresh is a prime — `mrefresh_is_prime`.
   ====================================================================================== *)

(* Two walks that agree on the frame and the entries — the switch flag is the REFRESH's reading of
   its own prior and says nothing of a prime, whose reuse is already nothing. *)
let walk_eq (x y:outcome mres eval_error) : prop =
  match x, y with
  | Ok s, Ok s' -> s.r_out == s'.r_out /\ s.r_entries == s'.r_entries
  | Error e, Error e' -> e == e'
  | _ -> False

(* A walk that reuses nothing reads no prior entry. *)
let rec step_cells_no_reuse (#n:nat) (cf:cell_fn n) (prior:Incremental.step_cache) (a:Incremental.alive{Incremental.awidth n a})
  : Lemma (ensures step_cells_by cf Incremental.no_reuse prior a == step_cells_by cf Incremental.no_reuse [] a) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> step_cells_no_reuse cf prior rest

let rec mwalk_no_reuse (mp:mprims) (rp:Relational.rprims) (pr:prims) (resolve:Relational.resolver) (env:param_env)
  (prior:list entry) (cols:schema) (a:Incremental.alive{Incremental.awidth (len cols) a}) (p:list mstep)
  : Lemma (ensures walk_eq (mwalk mp rp pr resolve env Incremental.no_reuse prior cols a p) (mwalk mp rp pr resolve env Incremental.no_reuse [] cols a p))
          (decreases p) =
  match p with
  | [] -> ()
  | MFilter pred :: rest ->
    step_cells_no_reuse (expr_fn pr env cols pred) (prior_head prior).e_cells a;
    (match step_cells_by (expr_fn pr env cols pred) Incremental.no_reuse [] a with
     | Error _ -> ()
     | Ok cs -> Incremental.awidth_keep_true (len cols) a cs; mwalk_no_reuse mp rp pr resolve env (prior_rest prior) cols (Incremental.keep_true a cs) rest)
  | MDerive name x :: rest ->
    (match derived_typing_in env cols x with
     | Refused -> ()
     | dt ->
       step_cells_no_reuse (expr_fn pr env cols x) (prior_head prior).e_cells a;
       (match step_cells_by (expr_fn pr env cols x) Incremental.no_reuse [] a with
        | Error _ -> ()
        | Ok cs ->
          (match column_type_by dt name (Incremental.cells_of cs) with
           | Error _ -> ()
           | Ok ty ->
             let k = Incremental.pack_key_of pr ty cs in
             let i = index_of name cols in
             Incremental.awidth_extend (len cols) i a (Incremental.pack_cache pr k cs);
             mwalk_no_reuse mp rp pr resolve env (prior_rest prior) (Incremental.ext_schema i name ty cols) (Incremental.extend i a (Incremental.pack_cache pr k cs)) rest)))
  | MProject pairs :: rest ->
    (match Relational.project_step cols pairs with
     | Error _ -> ()
     | Ok out -> len_project_cols cols pairs; len_names pairs; awidth_proj cols (names pairs) a; mwalk_no_reuse mp rp pr resolve env prior out (proj_alive cols (names pairs) a) rest)
  | MLimit k off :: rest ->
    awidth_drop (len cols) (max0 off) a;
    awidth_take (len cols) (max0 k) (drop_a (max0 off) a);
    mwalk_no_reuse mp rp pr resolve env prior cols (slice off k a) rest
  | MWindow spec :: rest ->
    (match Relational.window_cols rp cols spec with
     | Error _ -> ()
     | Ok out ->
       (match mp.win_cells cols spec (Incremental.rows_of a) with
        | Error _ -> ()
        | Ok ws ->
          Incremental.len_rows_of a;
          let cs = pair_cells a ws in
          Incremental.awidth_extend (len cols) None a cs;
          len_app cols [(spec.w_as, Relational.window_type cols spec)];
          mwalk_no_reuse mp rp pr resolve env (prior_rest_window prior) out (Incremental.extend None a cs) rest))
  | MJoin src on keep :: rest ->
    (match Relational.source_cols resolve src with
     | Error _ -> ()
     | Ok rcols ->
       (match Relational.join_cols rp cols rcols on (if keep then Semi else Anti) with
        | Error _ -> ()
        | Ok _ ->
          (match right_rows resolve src with
           | Error _ -> ()
           | Ok rrows ->
             let rks = proj_rows rcols (rnames on) rrows in
             let vf = verdict mp cols on rks keep in
             bool_step_ok (len cols) vf Incremental.no_reuse (prior_head prior).e_cells a;
             bool_step_ok (len cols) vf Incremental.no_reuse [] a;
             step_cells_no_reuse (bool_fn (len cols) vf) (prior_head prior).e_cells a;
             let vs = Ok?._0 (step_cells_by (bool_fn (len cols) vf) Incremental.no_reuse [] a) in
             Incremental.awidth_keep_true (len cols) a vs;
             mwalk_no_reuse mp rp pr resolve env (prior_rest prior) cols (Incremental.keep_true a vs) rest)))

(* The prime's entries at a step: what the state theorem threads through the walk. *)
let entries_of (r:outcome mres eval_error) : Tot (list entry) =
  match r with
  | Ok st -> st.r_entries
  | Error _ -> []

(* THE WALK'S STATE THEOREM. The walk reusing against a prior walk's entries, over a source of
   distinct tokens and under the premise, is the walk reusing nothing — and leaves the premise standing
   for the step after it, or switched reuse off. *)
let rec mwalk_reuse (mp:mprims) (rp:Relational.rprims) (pr:prims) (resolve:Relational.resolver) (env:param_env)
  (reuse:Incremental.reuse_fn) (cols:schema) (a0:Incremental.alive{Incremental.awidth (len cols) a0})
  (a:Incremental.alive{Incremental.awidth (len cols) a}) (p:list mstep)
  : Lemma (requires Ok? (mwalk mp rp pr resolve env Incremental.no_reuse [] cols a0 p) /\ wk reuse a a0 /\ distinct a0)
          (ensures (let fresh = mwalk mp rp pr resolve env Incremental.no_reuse [] cols a p in
                    let refreshed = mwalk mp rp pr resolve env reuse (entries_of (mwalk mp rp pr resolve env Incremental.no_reuse [] cols a0 p)) cols a p in
                    walk_eq refreshed fresh
                    /\ (match refreshed, mwalk mp rp pr resolve env Incremental.no_reuse [] cols a0 p with
                        | Ok st, Ok st0 ->
                          st.r_switched \/
                          (let Incremental.W c1 a1 = st.r_out in
                           let Incremental.W c0 a01 = st0.r_out in
                           c1 == c0 /\ wk reuse a1 a01 /\ distinct a01)
                        | _ -> True)))
          (decreases p) =
  match p with
  | [] -> ()
  | MFilter pred :: rest ->
    let cf = expr_fn pr env cols pred in
    step_cells_by_reuse cf reuse a0 a;
    (match step_cells_by cf Incremental.no_reuse [] a with
     | Error _ -> ()
     | Ok cs ->
       let cs0 = Ok?._0 (step_cells_by cf Incremental.no_reuse [] a0) in
       wk_keep_by cf reuse a0 a;
       distinct_keep a0 cs0;
       Incremental.awidth_keep_true (len cols) a0 cs0;
       Incremental.awidth_keep_true (len cols) a cs;
       mwalk_reuse mp rp pr resolve env reuse cols (Incremental.keep_true a0 cs0) (Incremental.keep_true a cs) rest)
  | MDerive name x :: rest ->
    (match derived_typing_in env cols x with
     | Refused -> ()
     | dt ->
       let cf = expr_fn pr env cols x in
       step_cells_by_reuse cf reuse a0 a;
       (match step_cells_by cf Incremental.no_reuse [] a with
        | Error _ -> ()
        | Ok cs ->
          (match column_type_by dt name (Incremental.cells_of cs) with
           | Error _ -> ()
           | Ok ty ->
             let cs0 = Ok?._0 (step_cells_by cf Incremental.no_reuse [] a0) in
             let ty0 = Ok?._0 (column_type_by dt name (Incremental.cells_of cs0)) in
             let k = Incremental.pack_key_of pr ty cs in
             let k0 = Incremental.pack_key_of pr ty0 cs0 in
             let i = index_of name cols in
             Incremental.awidth_extend (len cols) i a0 (Incremental.pack_cache pr k0 cs0);
             Incremental.awidth_extend (len cols) i a (Incremental.pack_cache pr k cs);
             if k0 = k then begin
               wk_extend_by cf pr k reuse i a0 a;
               distinct_extend i a0 (Incremental.pack_cache pr k0 cs0);
               mwalk_reuse mp rp pr resolve env reuse (Incremental.ext_schema i name ty cols) (Incremental.extend i a0 (Incremental.pack_cache pr k0 cs0)) (Incremental.extend i a (Incremental.pack_cache pr k cs)) rest
             end else begin
               let prior' = entries_of (mwalk mp rp pr resolve env Incremental.no_reuse [] (Incremental.ext_schema i name ty0 cols) (Incremental.extend i a0 (Incremental.pack_cache pr k0 cs0)) rest) in
               mwalk_no_reuse mp rp pr resolve env prior' (Incremental.ext_schema i name ty cols) (Incremental.extend i a (Incremental.pack_cache pr k cs)) rest
             end)))
  | MProject pairs :: rest ->
    (match Relational.project_step cols pairs with
     | Error _ -> ()
     | Ok out ->
       len_project_cols cols pairs; len_names pairs;
       awidth_proj cols (names pairs) a0; awidth_proj cols (names pairs) a;
       wk_proj cols (names pairs) reuse a a0;
       distinct_proj cols (names pairs) a0;
       mwalk_reuse mp rp pr resolve env reuse out (proj_alive cols (names pairs) a0) (proj_alive cols (names pairs) a) rest)
  | MLimit k off :: rest ->
    awidth_drop (len cols) (max0 off) a0; awidth_take (len cols) (max0 k) (drop_a (max0 off) a0);
    awidth_drop (len cols) (max0 off) a; awidth_take (len cols) (max0 k) (drop_a (max0 off) a);
    wk_slice off k reuse a a0;
    distinct_drop (max0 off) a0; distinct_take (max0 k) (drop_a (max0 off) a0);
    mwalk_reuse mp rp pr resolve env reuse cols (slice off k a0) (slice off k a) rest
  | MWindow spec :: rest ->
    (match Relational.window_cols rp cols spec with
     | Error _ -> ()
     | Ok out ->
       (match mp.win_cells cols spec (Incremental.rows_of a) with
        | Error _ -> ()
        | Ok ws ->
          Incremental.len_rows_of a;
          let cs = pair_cells a ws in
          Incremental.awidth_extend (len cols) None a cs;
          len_app cols [(spec.w_as, Relational.window_type cols spec)];
          let ws0 = Ok?._0 (mp.win_cells cols spec (Incremental.rows_of a0)) in
          Incremental.len_rows_of a0;
          let cs0 = pair_cells a0 ws0 in
          Incremental.awidth_extend (len cols) None a0 cs0;
          let prior' = entries_of (mwalk mp rp pr resolve env Incremental.no_reuse [] out (Incremental.extend None a0 cs0) rest) in
          mwalk_no_reuse mp rp pr resolve env prior' out (Incremental.extend None a cs) rest))
  | MJoin src on keep :: rest ->
    (match Relational.source_cols resolve src with
     | Error _ -> ()
     | Ok rcols ->
       (match Relational.join_cols rp cols rcols on (if keep then Semi else Anti) with
        | Error _ -> ()
        | Ok _ ->
          (match right_rows resolve src with
           | Error _ -> ()
           | Ok rrows ->
             let rks = proj_rows rcols (rnames on) rrows in
             let vf = verdict mp cols on rks keep in
             let cf = bool_fn (len cols) vf in
             bool_step_ok (len cols) vf Incremental.no_reuse [] a0;
             bool_step_ok (len cols) vf Incremental.no_reuse [] a;
             let vs0 = Ok?._0 (step_cells_by cf Incremental.no_reuse [] a0) in
             let vs = Ok?._0 (step_cells_by cf Incremental.no_reuse [] a) in
             (* the prior entry's key index is this walk's own, so the verdicts are reused *)
             step_cells_by_reuse cf reuse a0 a;
             wk_keep_by cf reuse a0 a;
             distinct_keep a0 vs0;
             Incremental.awidth_keep_true (len cols) a0 vs0;
             Incremental.awidth_keep_true (len cols) a vs;
             mwalk_reuse mp rp pr resolve env reuse cols (Incremental.keep_true a0 vs0) (Incremental.keep_true a vs) rest)))

(* ---- the group step reuses soundly ---- *)

(* Every member of every group is a row of the frame it was grouped from. *)
let rec sub_a (ms a:Incremental.alive) : Tot bool =
  match ms with
  | [] -> true
  | m :: rest -> mem_a m a && sub_a rest a

let rec members_in (a:Incremental.alive) (gs:list group) : Tot bool =
  match gs with
  | [] -> true
  | g :: rest -> sub_a g.g_members a && members_in a rest

let rec sub_a_app (ms ns a:Incremental.alive)
  : Lemma (requires sub_a ms a /\ sub_a ns a) (ensures sub_a (app ms ns) a) =
  match ms with
  | [] -> ()
  | _ :: rest -> sub_a_app rest ns a

let rec add_member_in (a:Incremental.alive) (key:list cell) (m:Incremental.token & list cell) (gs:list group)
  : Lemma (requires members_in a gs /\ mem_a m a) (ensures members_in a (add_member key m gs)) =
  match gs with
  | [] -> ()
  | g :: rest -> if g.g_key = key then sub_a_app g.g_members [m] a else add_member_in a key m rest

let rec groups_acc_in (cols:schema) (keys:list string) (a:Incremental.alive) (gs:list group) (rest:Incremental.alive)
  : Lemma (requires members_in a gs /\ sub_a rest a) (ensures members_in a (groups_acc cols keys gs rest)) (decreases rest) =
  match rest with
  | [] -> ()
  | (t, r) :: more ->
    add_member_in a (proj_row cols keys r) (t, r) gs;
    groups_acc_in cols keys a (add_member (proj_row cols keys r) (t, r) gs) more

(* `ms` is a suffix of `a`. *)
let rec suffix_of (ms a:Incremental.alive) : Tot bool (decreases a) =
  ms = a || (match a with [] -> false | _ :: rest -> suffix_of ms rest)

let rec mem_a_suffix (m:Incremental.token & list cell) (rest a:Incremental.alive)
  : Lemma (requires suffix_of (m :: rest) a) (ensures mem_a m a /\ suffix_of rest a) (decreases a) =
  match a with
  | [] -> ()
  | _ :: more -> if (m :: rest) = a then () else mem_a_suffix m rest more

let rec sub_a_of_suffix (ms a:Incremental.alive)
  : Lemma (requires suffix_of ms a) (ensures sub_a ms a) (decreases ms) =
  match ms with
  | [] -> ()
  | m :: rest -> mem_a_suffix m rest a; sub_a_of_suffix rest a

let groups_of_in (cols:schema) (keys:list string) (a:Incremental.alive)
  : Lemma (ensures members_in a (groups_of cols keys a)) =
  sub_a_of_suffix a a; groups_acc_in cols keys a [] a

(* A group's member rows are determined by its member tokens under the premise: a reusable member
   the prior source held under its token is the prior member. *)
let rec members_eq (reuse:Incremental.reuse_fn) (a a0:Incremental.alive) (ms ms0:Incremental.alive)
  : Lemma (requires wk reuse a a0 /\ distinct a0 /\ sub_a ms a /\ sub_a ms0 a0 /\ every_reuse reuse ms
                    /\ tokens_of ms == tokens_of ms0)
          (ensures ms == ms0) (decreases ms) =
  match ms, ms0 with
  | [], [] -> ()
  | (t, r) :: mr, (t0, r0) :: mr0 ->
    wk_mem reuse a a0 t r;
    assoc_of_mem a0 t0 r0;
    members_eq reuse a a0 mr mr0

(* The first group under a key, and what `find_key` over the fresh gouts says of it. *)
let rec find_group (key:list cell) (gs:list group) : Tot (option group) =
  match gs with
  | [] -> None
  | g :: rest -> if g.g_key = key then Some g else find_group key rest

let rec find_group_in (a:Incremental.alive) (key:list cell) (gs:list group)
  : Lemma (requires members_in a gs)
          (ensures (match find_group key gs with Some g -> sub_a g.g_members a | None -> True)) =
  match gs with
  | [] -> ()
  | g :: rest -> if g.g_key = key then () else find_group_in a key rest

let rec find_key_sound (mp:mprims) (cols:schema) (aggs:list agg) (gs0:list group) (key:list cell)
  : Lemma (requires Ok? (groups_out mp Incremental.no_reuse [] cols aggs gs0))
          (ensures (match find_key key (pgroups_of (Ok?._0 (groups_out mp Incremental.no_reuse [] cols aggs gs0))), find_group key gs0 with
                    | Some pg, Some g0 ->
                      pg.p_tokens == tokens_of g0.g_members /\ agg_cells_of mp cols aggs g0.g_members == Ok pg.p_aggs
                    | None, None -> True
                    | _ -> False)) =
  match gs0 with
  | [] -> ()
  | g :: rest -> if g.g_key = key then () else find_key_sound mp cols aggs rest key

(* One group's aggregates, reused or recomputed, are the recomputed ones. *)
let group_aggs_reuse (mp:mprims) (reuse:Incremental.reuse_fn) (cols:schema) (aggs:list agg)
  (a a0:Incremental.alive) (g:group) (gs0:list group)
  : Lemma (requires wk reuse a a0 /\ distinct a0 /\ sub_a g.g_members a /\ members_in a0 gs0
                    /\ Ok? (groups_out mp Incremental.no_reuse [] cols aggs gs0))
          (ensures (let prior = pgroups_of (Ok?._0 (groups_out mp Incremental.no_reuse [] cols aggs gs0)) in
                    match group_aggs mp reuse prior cols aggs g, agg_cells_of mp cols aggs g.g_members with
                    | Ok (cs, _), Ok cs' -> cs == cs'
                    | Error e, Error e' -> e == e'
                    | _ -> False)) =
  let prior = pgroups_of (Ok?._0 (groups_out mp Incremental.no_reuse [] cols aggs gs0)) in
  if all_reuse reuse g.g_members then begin
    find_key_sound mp cols aggs gs0 g.g_key;
    match find_key g.g_key prior with
    | Some pg ->
      if pg.p_tokens = tokens_of g.g_members && len pg.p_aggs = len aggs then begin
        let g0 = Some?.v (find_group g.g_key gs0) in
        find_group_in a0 g.g_key gs0;
        members_eq reuse a a0 g.g_members g0.g_members
      end else ()
    | None -> ()
  end else ()

(* Two gout lists that agree on everything but the reuse flag. *)
let rec eqv (os os':list gout) : Tot bool =
  match os, os' with
  | [], [] -> true
  | o :: r, o' :: r' -> o.o_key = o'.o_key && o.o_tokens = o'.o_tokens && o.o_aggs = o'.o_aggs && eqv r r'
  | _ -> false

let rec galive_eqv (mp:mprims) (os os':list gout)
  : Lemma (requires eqv os os') (ensures galive mp os == galive mp os') =
  match os, os' with
  | [], [] -> ()
  | _ :: r, _ :: r' -> galive_eqv mp r r'

let rec pgroups_eqv (os os':list gout)
  : Lemma (requires eqv os os') (ensures pgroups_of os == pgroups_of os') =
  match os, os' with
  | [], [] -> ()
  | _ :: r, _ :: r' -> pgroups_eqv r r'

let rec groups_out_reuse (mp:mprims) (reuse:Incremental.reuse_fn) (cols:schema) (aggs:list agg)
  (a a0:Incremental.alive) (gs gs0:list group)
  : Lemma (requires wk reuse a a0 /\ distinct a0 /\ members_in a gs /\ members_in a0 gs0
                    /\ Ok? (groups_out mp Incremental.no_reuse [] cols aggs gs0))
          (ensures (let prior = pgroups_of (Ok?._0 (groups_out mp Incremental.no_reuse [] cols aggs gs0)) in
                    match groups_out mp reuse prior cols aggs gs, groups_out mp Incremental.no_reuse [] cols aggs gs with
                    | Ok os, Ok os' -> eqv os os'
                    | Error e, Error e' -> e == e'
                    | _ -> False)) =
  match gs with
  | [] -> ()
  | g :: rest ->
    group_aggs_reuse mp reuse cols aggs a a0 g gs0;
    groups_out_reuse mp reuse cols aggs a a0 rest gs0

(* Under no reuse the prior is never read, and no group is reused. *)
let rec groups_out_no_reuse (mp:mprims) (prior:list pgroup) (cols:schema) (aggs:list agg) (gs:list group)
  : Lemma (ensures groups_out mp Incremental.no_reuse prior cols aggs gs == groups_out mp Incremental.no_reuse [] cols aggs gs) =
  match gs with
  | [] -> ()
  | g :: rest -> all_reuse_no g.g_members; groups_out_no_reuse mp prior cols aggs rest

(* ---- the group table's tokens ---- *)

let rec mem_key (k:list cell) (gs:list group) : Tot bool =
  match gs with
  | [] -> false
  | g :: rest -> g.g_key = k || mem_key k rest

let rec nodup_keys (gs:list group) : Tot bool =
  match gs with
  | [] -> true
  | g :: rest -> not (mem_key g.g_key rest) && nodup_keys rest

let rec mem_key_add (k key:list cell) (m:Incremental.token & list cell) (gs:list group)
  : Lemma (requires mem_key k (add_member key m gs)) (ensures mem_key k gs \/ k == key) =
  match gs with
  | [] -> ()
  | g :: rest -> if g.g_key = key then () else if g.g_key = k then () else mem_key_add k key m rest

let rec add_member_nodup (key:list cell) (m:Incremental.token & list cell) (gs:list group)
  : Lemma (requires nodup_keys gs) (ensures nodup_keys (add_member key m gs)) =
  match gs with
  | [] -> ()
  | g :: rest ->
    if g.g_key = key then ()
    else begin
      add_member_nodup key m rest;
      Classical.move_requires (mem_key_add g.g_key key m) rest
    end

let rec groups_acc_nodup (cols:schema) (keys:list string) (gs:list group) (a:Incremental.alive)
  : Lemma (requires nodup_keys gs) (ensures nodup_keys (groups_acc cols keys gs a)) (decreases a) =
  match a with
  | [] -> ()
  | (t, r) :: rest ->
    add_member_nodup (proj_row cols keys r) (t, r) gs;
    groups_acc_nodup cols keys (add_member (proj_row cols keys r) (t, r) gs) rest

let rec mem_okey (k:list cell) (os:list gout) : Tot bool =
  match os with
  | [] -> false
  | o :: rest -> o.o_key = k || mem_okey k rest

let rec nodup_okeys (os:list gout) : Tot bool =
  match os with
  | [] -> true
  | o :: rest -> not (mem_okey o.o_key rest) && nodup_okeys rest

let rec mem_okey_out (mp:mprims) (reuse:Incremental.reuse_fn) (prior:list pgroup) (cols:schema) (aggs:list agg) (gs:list group) (k:list cell)
  : Lemma (requires Ok? (groups_out mp reuse prior cols aggs gs))
          (ensures mem_okey k (Ok?._0 (groups_out mp reuse prior cols aggs gs)) == mem_key k gs) =
  match gs with
  | [] -> ()
  | _ :: rest -> mem_okey_out mp reuse prior cols aggs rest k

let rec nodup_out (mp:mprims) (reuse:Incremental.reuse_fn) (prior:list pgroup) (cols:schema) (aggs:list agg) (gs:list group)
  : Lemma (requires nodup_keys gs /\ Ok? (groups_out mp reuse prior cols aggs gs))
          (ensures nodup_okeys (Ok?._0 (groups_out mp reuse prior cols aggs gs))) =
  match gs with
  | [] -> ()
  | g :: rest -> mem_okey_out mp reuse prior cols aggs rest g.g_key; nodup_out mp reuse prior cols aggs rest

let rec token_of_key (mp:mprims) (k:list cell) (os:list gout)
  : Lemma (requires key_token_injective mp /\ not (mem_okey k os))
          (ensures not (Incremental.mem_token (mp.key_token k) (tokens_of (galive mp os)))) =
  match os with
  | [] -> ()
  | _ :: rest -> token_of_key mp k rest

let rec distinct_galive (mp:mprims) (os:list gout)
  : Lemma (requires key_token_injective mp /\ nodup_okeys os) (ensures distinct (galive mp os)) =
  match os with
  | [] -> ()
  | o :: rest -> token_of_key mp o.o_key rest; distinct_galive mp rest

(* ---- the tail's premise ---- *)

let rec mem_gout (o:gout) (os:list gout) : Tot bool =
  match os with
  | [] -> false
  | o' :: rest -> o' = o || mem_gout o rest

(* A reused group's aggregates are a prior group's, under the same key. *)
let rec reused_from_prior (mp:mprims) (reuse:Incremental.reuse_fn) (prior:list pgroup) (cols:schema) (aggs:list agg) (gs:list group)
  (o:gout)
  : Lemma (requires Ok? (groups_out mp reuse prior cols aggs gs) /\ mem_gout o (Ok?._0 (groups_out mp reuse prior cols aggs gs)) /\ o.o_reused)
          (ensures (match find_key o.o_key prior with Some pg -> pg.p_aggs == o.o_aggs | None -> False)) =
  match gs with
  | [] -> ()
  | g :: rest ->
    (match group_aggs mp reuse prior cols aggs g with
     | Error _ -> ()
     | Ok (cs, reused) ->
       let here = { o_key = g.g_key; o_tokens = tokens_of g.g_members; o_aggs = cs; o_reused = reused } in
       if here = o then () else reused_from_prior mp reuse prior cols aggs rest o)

(* A prior group found under its key is a row of the prior group table, under its token. *)
let rec found_in_galive (mp:mprims) (os0:list gout) (key:list cell)
  : Lemma (ensures (match find_key key (pgroups_of os0) with
                    | Some pg -> mem_a (mp.key_token key, app key pg.p_aggs) (galive mp os0)
                    | None -> True)) =
  match os0 with
  | [] -> ()
  | o :: rest -> if o.o_key = key then () else found_in_galive mp rest key

(* A row of the group table comes from a gout; one not among the unreused tokens was reused. *)
let rec galive_row (mp:mprims) (os:list gout) (t:Incremental.token) (r:list cell)
  : Lemma (requires mem_a (t, r) (galive mp os) /\ not (Incremental.mem_token t (unreused_tokens mp os)))
          (ensures exists (o:gout). mem_gout o os /\ o.o_reused /\ t == mp.key_token o.o_key /\ r == app o.o_key o.o_aggs) =
  match os with
  | [] -> ()
  | o :: rest ->
    if (mp.key_token o.o_key, app o.o_key o.o_aggs) = (t, r) then ()
    else galive_row mp rest t r

let tail_premise (mp:mprims) (reuse:Incremental.reuse_fn) (cols:schema) (aggs:list agg) (gs gs0:list group)
  : Lemma (requires key_token_injective mp /\ Ok? (groups_out mp Incremental.no_reuse [] cols aggs gs0)
                    /\ Ok? (groups_out mp reuse (pgroups_of (Ok?._0 (groups_out mp Incremental.no_reuse [] cols aggs gs0))) cols aggs gs)
                    /\ nodup_keys gs0)
          (ensures (let os0 = Ok?._0 (groups_out mp Incremental.no_reuse [] cols aggs gs0) in
                    let os = Ok?._0 (groups_out mp reuse (pgroups_of os0) cols aggs gs) in
                    wk (Incremental.reuse_of (Some (unreused_tokens mp os))) (galive mp os) (galive mp os0)
                    /\ distinct (galive mp os0))) =
  let os0 = Ok?._0 (groups_out mp Incremental.no_reuse [] cols aggs gs0) in
  let prior = pgroups_of os0 in
  let os = Ok?._0 (groups_out mp reuse prior cols aggs gs) in
  let reuse_g = Incremental.reuse_of (Some (unreused_tokens mp os)) in
  nodup_out mp Incremental.no_reuse [] cols aggs gs0;
  distinct_galive mp os0;
  let pick (t:Incremental.token) (r:list cell) (o:gout)
    : Lemma (requires mem_gout o os /\ o.o_reused /\ t == mp.key_token o.o_key /\ r == app o.o_key o.o_aggs)
            (ensures ok_row reuse_g (galive mp os0) t r) =
    reused_from_prior mp reuse prior cols aggs gs o;
    found_in_galive mp os0 o.o_key;
    (match find_key o.o_key prior with
     | Some pg -> assoc_of_mem (galive mp os0) t (app o.o_key pg.p_aggs)
     | None -> ()) in
  let aux (t:Incremental.token) (r:list cell)
    : Lemma (requires mem_a (t, r) (galive mp os)) (ensures ok_row reuse_g (galive mp os0) t r) =
    if reuse_g t then begin
      galive_row mp os t r;
      let pick' (o:gout) : Lemma ((mem_gout o os /\ o.o_reused /\ t == mp.key_token o.o_key /\ r == app o.o_key o.o_aggs) ==> ok_row reuse_g (galive mp os0) t r) =
        Classical.move_requires (pick t r) o in
      Classical.forall_intro pick'
    end in
  let aux' (t:Incremental.token) (r:list cell) : Lemma (mem_a (t, r) (galive mp os) ==> ok_row reuse_g (galive mp os0) t r) =
    Classical.move_requires (aux t) r in
  Classical.forall_intro_2 aux';
  wk_of_rows reuse_g (galive mp os) (galive mp os0)

(* ======================================================================================
   11. The theorems.
   ====================================================================================== *)

(* THEOREM (the state theorem). A refresh against a state primed over the prior source, under a
   delta that names every row that moved, over a prior source of distinct tokens and a group token
   one per key, is the prime over the new source — output, every entry, the group table and the
   tail. *)
let mrefresh_is_prime (mp:mprims) (rp:Relational.rprims) (pr:prims) (resolve:Relational.resolver) (env:param_env)
  (cols:schema) (a0:Incremental.alive{Incremental.awidth (len cols) a0}) (a:Incremental.alive{Incremental.awidth (len cols) a})
  (named:option (list Incremental.token)) (p:mpipe)
  : Lemma (requires Ok? (mprime mp rp pr resolve env cols a0 p) /\ wk (Incremental.reuse_of named) a a0 /\ distinct a0
                    /\ key_token_injective mp)
          (ensures mrefresh mp rp pr resolve env (Ok?._0 (mprime mp rp pr resolve env cols a0 p)) named cols a p
                   == mprime mp rp pr resolve env cols a p) =
  let reuse = Incremental.reuse_of named in
  let prime0 = Ok?._0 (mprime mp rp pr resolve env cols a0 p) in
  let st0 = Ok?._0 (mwalk mp rp pr resolve env Incremental.no_reuse [] cols a0 p.prefix) in
  assert (prime0.m_entries == st0.r_entries);
  mwalk_reuse mp rp pr resolve env reuse cols a0 a p.prefix;
  match mwalk mp rp pr resolve env reuse st0.r_entries cols a p.prefix, mwalk mp rp pr resolve env Incremental.no_reuse [] cols a p.prefix with
  | Error _, Error _ -> ()
  | Ok st, Ok st' ->
    (match p.grouped with
     | None -> ()
     | Some (keys, aggs, tail) ->
       let Incremental.W c1 a1 = st.r_out in
       let Incremental.W c0 a01 = st0.r_out in
       assert (st'.r_out == Incremental.W c1 a1);
       (match Relational.group_by_cols c1 keys aggs with
        | Error _ -> ()
        | Ok gcols ->
          let gs = groups_of c1 keys a1 in
          len_group_by_cols c1 keys aggs;
          groups_key_width c1 keys [] a1;
          if st.r_switched then begin
            groups_out_no_reuse mp prime0.m_groups c1 aggs gs;
            (match groups_out mp Incremental.no_reuse [] c1 aggs gs with
             | Error _ -> ()
             | Ok os ->
               groups_out_key_width mp Incremental.no_reuse [] c1 keys aggs gs;
               awidth_galive mp keys aggs os;
               all_unreused_fresh mp c1 aggs gs;
               mwalk_no_reuse mp rp pr resolve env prime0.m_tail gcols (galive mp os) tail)
          end else begin
            let gs0 = groups_of c0 keys a01 in
            let os0 = Ok?._0 (groups_out mp Incremental.no_reuse [] c0 aggs gs0) in
            assert (prime0.m_groups == pgroups_of os0);
            groups_of_in c1 keys a1;
            groups_of_in c0 keys a01;
            groups_out_reuse mp reuse c1 aggs a1 a01 gs gs0;
            (match groups_out mp Incremental.no_reuse [] c1 aggs gs with
             | Error _ -> ()
             | Ok os' ->
               let os = Ok?._0 (groups_out mp reuse (pgroups_of os0) c1 aggs gs) in
               galive_eqv mp os os';
               pgroups_eqv os os';
               groups_out_key_width mp Incremental.no_reuse [] c1 keys aggs gs;
               awidth_galive mp keys aggs os';
               groups_key_width c0 keys [] a01;
               groups_out_key_width mp Incremental.no_reuse [] c0 keys aggs gs0;
               awidth_galive mp keys aggs os0;
               all_unreused_fresh mp c1 aggs gs;
               all_unreused_fresh mp c0 aggs gs0;
               assert (c1 == c0);
               assert (tail_reuse mp os0 == Incremental.no_reuse);
               assert (Relational.group_by_cols c0 keys aggs == Ok gcols);
               assert (group_step mp Incremental.no_reuse [] c0 keys aggs a01 == Ok ({ gs_out = Incremental.W gcols (galive mp os0); gs_os = os0 }));
               assert (prime0.m_tail == entries_of (mwalk mp rp pr resolve env Incremental.no_reuse [] gcols (galive mp os0) tail));
               if all_unreused os then
                 mwalk_no_reuse mp rp pr resolve env prime0.m_tail gcols (galive mp os') tail
               else begin
                 groups_acc_nodup c0 keys [] a01;
                 tail_premise mp reuse c1 aggs gs gs0;
                 mwalk_reuse mp rp pr resolve env (Incremental.reuse_of (Some (unreused_tokens mp os))) gcols (galive mp os0) (galive mp os') tail
               end)
          end))
  | _ -> ()

(* THEOREM (the agreement theorem). The refreshed answer is the full evaluation of the pipeline
   over the updated source — the same frame, or the same error. *)
let mrefresh_agrees (mp:mprims) (rp:Relational.rprims) (pr:prims) (resolve:Relational.resolver) (env:param_env)
  (cols:schema) (a0:Incremental.alive{Incremental.awidth (len cols) a0}) (a:Incremental.alive{Incremental.awidth (len cols) a})
  (named:option (list Incremental.token)) (p:mpipe)
  : Lemma (requires Ok? (mprime mp rp pr resolve env cols a0 p) /\ wk (Incremental.reuse_of named) a a0 /\ distinct a0
                    /\ key_token_injective mp)
          (ensures (match mrefresh mp rp pr resolve env (Ok?._0 (mprime mp rp pr resolve env cols a0 p)) named cols a p,
                          eval_uncounted pr (mother mp rp resolve env) env (embed_pipe p) (Incremental.frame_of cols a) with
                    | Ok st, Ok f -> Incremental.frame_of_walked st.m_out == f
                    | Error e, Error e' -> e == e'
                    | _ -> False)) =
  mrefresh_is_prime mp rp pr resolve env cols a0 a named p;
  mprime_is_full mp rp pr resolve env cols a p
