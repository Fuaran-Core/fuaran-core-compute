(*
   Incremental — the row-local incremental refresh, and its agreement with full evaluation
   (fuaran-core Phase 433).

   WHAT IS MODELLED. `src/Fuaran.Compute.DataFrame/Incremental.fs`, the row-local walk that a
   refresh takes over a pipeline whose every step `Incremental.classifyStep` reads as
   `PropagateRows` and `Pipeline.fst` models as a clause — a `Filter` or a `Derive`:

     - the retained state (`IncrementalEval`): per EVALUATING step, the cell the step produced for
       every source row it reached (`StepCells`, "one array per evaluating step, each aligned with
       `Tokens`; `null` where the row did not reach that step"), held here as a `step_cache` — an
       association from the row's identity token to that cell — one per step, in step order;
     - the delta, as the one thing the walk reads of it: the set of row tokens it NAMES
       (`namedTokens delta`), or every row when it invalidates a column ("Column invalidation names
       no rows, so every row is suspect"); a row the delta does not name is `reuse`d;
     - the walk (`walk`, `evalStep`): step by step, and within a step row by row in frame order,
       a reused row's cell is read from the prior step's cache (`cachedAt`) and every other row's
       is evaluated by the reference's own evaluator (`DataFrame.evalResolved` — here
       `Pipeline.eval_expr`), the first error winning; a `Filter` keeps the rows whose cell is
       `Bool true`, a `Derive` replaces or appends its column, its TYPE decided over the rows alive
       at the step by the reference's own rule (`derivedTypingIn` before any row, `columnTypeBy`
       over the step's cells after — "Reading it off a cache would type the column differently
       from the reference the moment a filter downstream dropped the only typed row");
     - the derived column PACKED as the frame carries it (Phase 470, D38: `pack_cache`, through
       `Pipeline.pack_cells` — an int under a float or a decimal column as that type's value, every
       cell as it was where the typed pass is abandoned), the cache kept raw as `StepCells` is, and
       the pack KEY — the decided type and the pack's verdict — recorded per derive (`entry.e_pack`);
       a derive whose key is not the prior evaluation's reuses nothing from there on (F#:
       `retypedSince`, D37 widened by D38), because every cell cached downstream was computed over
       the other reading of the column;
     - `prime` as the walk with nothing reused (`Incremental.prime` runs `run` with no prior
       state), `refresh` as the walk reusing every unnamed row against the prior state's entries.

   WHAT IS PROVED. Two theorems, each over every cell primitive, every param environment, every
   row-local pipeline and every well-formed source:

     - `prime_is_full`: priming computes exactly what `Pipeline.eval_uncounted` computes — the same
       frame, or the same error — so the walk's own step shape (cells first, then keep or extend)
       is the reference evaluator's step;
     - `refresh_is_prime` (THE STATE THEOREM): a refresh against a state primed over the prior
       source, under a delta that names every row that moved, is — output AND caches — the prime
       over the new source. It needs exactly the premise production states of a delta ("The delta
       must truthfully describe the change"): every row the delta does not name has the cells it
       had, found under its token in the prior source (`unchanged`);
     - `refresh_agrees` (THE AGREEMENT THEOREM): the two composed — the refreshed answer is the
       full evaluation of the pipeline over the updated source.

   The one lemma with content is `eval_expr_names`: the evaluator reads a schema's NAMES and never
   its types, so a cell cached when a derived column was typed one way is still the cell when a
   named row's change retypes that column. That is what makes a per-row cache sound under a
   typing that is a function of the whole column — for the RAW cell. The packed cell a row reads
   downstream is a function of the whole column too (the key), which is why the walk switches reuse
   off at a derive whose key moved (`walk_reuse`'s two arms: `unchanged_extend` under one key,
   `walk_no_reuse` past another).

   WHAT IS A PARAMETER. Exactly `Pipeline.fst`'s: the cell primitives (`prims`) and, through
   `eval_uncounted`, the twelve verbs that evaluate no expression (`other_fn`), which this walk
   never reaches. Nothing else is assumed.

   WHAT IS NOT MODELLED, and recorded in `DECISIONS.md` (D31) and on the ladder: `Project` (row-local
   in production, a parameter in `Pipeline.fst`), the maintained `GroupBy` with its per-group
   aggregate caches, `Sort`'s merged orders, `Limit`'s slice, `Window`'s runs and the filtering
   joins' key indexes; the staleness guards (`PipelineChanged`, `EnvChanged`, `SourceSchemaMoved`,
   `DeltaIsFullRefresh`, `OrdinalAddressing`) that send a refresh to a full run; the quiet-delta
   shortcut (`ReusedPrior`); the in-place and append fast paths (Phases 323 and 359), which compute
   the same caches from a different starting point; the chunked path (Phase 268); and the state's
   wire form. Every type parameter is `Type0`.
*)
module Incremental

open Pipeline

(* ======================================================================================
   1. The row-local pipeline — the walk's own step alphabet (F#: the walk's `WFilter` and
      `WDerive`), and its embedding into `Pipeline.transform`.
   ====================================================================================== *)

type wstep =
  | WFilter : col_expr -> wstep
  | WDerive : string -> col_expr -> wstep

let embed_step (s:wstep) : Tot transform =
  match s with
  | WFilter pred -> Filter pred
  | WDerive name x -> Derive name x

let rec embed (p:list wstep) : Tot (list transform) =
  match p with
  | [] -> []
  | s :: rest -> embed_step s :: embed rest

(* ======================================================================================
   2. The walked frame — the alive rows, each with its identity token (F#: `Tokens` beside the
      frame's `Order`), every row as wide as the schema.
   ====================================================================================== *)

(* F#: a row's identity token (`Delta.refToken`, the witness's `KeyString`). *)
type token = string

type alive = list (token & list cell)

(* Every alive row is `n` wide — `toFrame`'s well-formedness, on the tokened rows. *)
let rec awidth (n:nat) (a:alive) : Tot bool =
  match a with
  | [] -> true
  | (_, r) :: rest -> len r = n && awidth n rest

let rec rows_of (a:alive) : Tot (list (list cell)) =
  match a with
  | [] -> []
  | (_, r) :: rest -> r :: rows_of rest

let rec rows_of_width (n:nat) (a:alive)
  : Lemma (requires awidth n a) (ensures all_width n (rows_of a)) =
  match a with
  | [] -> ()
  | _ :: rest -> rows_of_width n rest

(* The frame the reference evaluator sees: the alive rows in order, tokens dropped. *)
let frame_of (cols:schema) (a:alive{awidth (len cols) a}) : Tot wframe =
  rows_of_width (len cols) a;
  { cols = cols; rows = rows_of a }

(* ======================================================================================
   3. One step's cells — F#: `evalStep r f evalIdx resolved`. Per row in frame order: a row the
      delta names (or one with no cached cell) is evaluated; any other reads `cachedAt`.
   ====================================================================================== *)

(* F#: `StepCells[k]` — the cell step `k` produced, per reached row, under the row's token. *)
type step_cache = list (token & cell)

let rec cells_of (c:step_cache) : Tot (list cell) =
  match c with
  | [] -> []
  | (_, v) :: rest -> v :: cells_of rest

let rec len_cells_of (c:step_cache) : Lemma (ensures len (cells_of c) == len c) =
  match c with
  | [] -> ()
  | _ :: rest -> len_cells_of rest

(* F#: whether `cachedAt` is consulted for a row — `not (mem token named)`, or never. *)
type reuse_fn = token -> bool

let no_reuse : reuse_fn = fun _ -> false

(* `List.contains`, spelled here because `Pipeline.mem` is `noextract`. *)
let rec mem_token (t:token) (l:list token) : Tot bool =
  match l with
  | [] -> false
  | h :: rest -> h = t || mem_token t rest

(* F#: `namedTokens delta`, `None` where a column is invalidated and every row is suspect. *)
let reuse_of (named:option (list token)) : Tot reuse_fn =
  fun t -> match named with
        | None -> false
        | Some ns -> not (mem_token t ns)

let rec step_cells (pr:prims) (env:param_env) (cols:schema) (x:col_expr) (reuse:reuse_fn)
  (prior:step_cache) (a:alive{awidth (len cols) a})
  : Tot (r:outcome step_cache eval_error{match r with Ok cs -> len cs = len a | Error _ -> true})
        (decreases a) =
  match a with
  | [] -> Ok []
  | (t, r) :: rest ->
    let here =
      if reuse t then
        (match assoc t prior with
         | Some c -> Ok c
         | None -> eval_expr pr env cols r x)
      else eval_expr pr env cols r x in
    (match here with
     | Error err -> Error err
     | Ok c ->
       (match step_cells pr env cols x reuse prior rest with
        | Error err -> Error err
        | Ok cs -> Ok ((t, c) :: cs)))

(* F#: `keeps` — a `Filter` keeps the rows whose cell is `Bool true`, in order. *)
let rec keep_true (a:alive) (cs:step_cache{len cs = len a}) : Tot alive (decreases a) =
  match a, cs with
  | [], [] -> []
  | (t, r) :: ar, (_, c) :: cr ->
    (match c with
     | Bool true -> (t, r) :: keep_true ar cr
     | _ -> keep_true ar cr)

let rec awidth_keep_true (n:nat) (a:alive) (cs:step_cache{len cs = len a})
  : Lemma (requires awidth n a) (ensures awidth n (keep_true a cs)) (decreases a) =
  match a, cs with
  | [], [] -> ()
  | _ :: ar, _ :: cr -> awidth_keep_true n ar cr

(* F#: a `Derive` replaces the named column in place (`colIndex` found it) or appends it. *)
let ext_width (n:nat) (i:option nat) : Tot nat =
  match i with
  | Some _ -> n
  | None -> n + 1

let ext_one (i:option nat) (r:list cell) (v:cell) : Tot (list cell) =
  match i with
  | Some j -> set_at j v r
  | None -> app r [v]

let rec extend (i:option nat) (a:alive) (cs:step_cache{len cs = len a}) : Tot alive (decreases a) =
  match a, cs with
  | [], [] -> []
  | (t, r) :: ar, (_, v) :: cr -> (t, ext_one i r v) :: extend i ar cr

let rec awidth_extend (n:nat) (i:option nat) (a:alive) (cs:step_cache{len cs = len a})
  : Lemma (requires awidth n a) (ensures awidth (ext_width n i) (extend i a cs)) (decreases a) =
  match a, cs with
  | [], [] -> ()
  | (_, r) :: ar, (_, v) :: cr ->
    (match i with
     | Some j -> len_set_at j v r
     | None -> len_app r [v]);
    awidth_extend n i ar cr

let ext_schema (i:option nat) (name:string) (ty:column_type) (cols:schema)
  : Tot (c:schema{len c == ext_width (len cols) i}) =
  match i with
  | Some j -> len_retype_at j ty cols; retype_at j ty cols
  | None -> len_app cols [(name, ty)]; app cols [(name, ty)]

(* ======================================================================================
   Phase 470 (D38) — the derived column PACKED, through Pipeline's one definition. The walk extends
   the alive rows with the step's cells as the frame's column carries them (`pack_cells`: an int
   under a float or a decimal column as that type's value, every cell as it was where the column
   is boxed), and KEEPS the cache raw, as production keeps `StepCells` (a later refresh may type
   the column back). What a reused row's packed cell depends on beyond its raw cell is the pack
   KEY — the decided type and the verdict — so the key is what the state records per derive, and a
   derive whose key is not the prior evaluation's reuses nothing from there on (F#: `retypedSince`,
   D37, widened to the verdict by this phase).
   ====================================================================================== *)

type pack_key = { k_type : column_type; k_typed : bool }

let pack_key_of (pr:prims) (ty:column_type) (cs:step_cache) : Tot pack_key =
  { k_type = ty; k_typed = typed_pack pr ty (cells_of cs) }

(* The cache's cells packed under a key, each under its token. *)
let rec pack_cache (pr:prims) (k:pack_key) (cs:step_cache) : Tot (c:step_cache{len c = len cs}) (decreases cs) =
  match cs with
  | [] -> []
  | (t, v) :: rest -> (t, pack_one pr k.k_type k.k_typed v) :: pack_cache pr k rest

let rec cells_of_pack_cache (pr:prims) (k:pack_key) (cs:step_cache)
  : Lemma (ensures cells_of (pack_cache pr k cs) == pack_with pr k.k_type k.k_typed (cells_of cs)) (decreases cs) =
  match cs with
  | [] -> ()
  | _ :: rest -> cells_of_pack_cache pr k rest

(* Packing the cache under the key it decides IS `Pipeline.pack_cells` over its cells. *)
let pack_cache_is_pack_cells (pr:prims) (ty:column_type) (cs:step_cache)
  : Lemma (ensures cells_of (pack_cache pr (pack_key_of pr ty cs) cs) == pack_cells pr ty (cells_of cs)) =
  cells_of_pack_cache pr (pack_key_of pr ty cs) cs

(* ======================================================================================
   4. The walk — F#: `walk`, over the row-local steps; and the state it leaves.
   ====================================================================================== *)

(* The walk's result frame: a schema and the alive rows under it. *)
type walked =
  | W : cols:schema -> a:alive{awidth (len cols) a} -> walked

let frame_of_walked (w:walked) : Tot wframe =
  match w with
  | W cols a -> frame_of cols a

(* F#: one evaluating step's retained state — its cells under the rows' tokens (`StepCells[k]`),
   raw as evaluated, and the key a derive packed them under (D37, D38). *)
type entry = { e_cells : step_cache; e_pack : option pack_key }

(* F#: the two halves of `IncrementalEval` this walk writes — `Output` and `StepCells`. *)
type state = { s_out : walked; s_entries : list entry }

let prior_cells (prior:list entry) : Tot step_cache =
  match prior with
  | [] -> []
  | e :: _ -> e.e_cells

let prior_pack (prior:list entry) : Tot (option pack_key) =
  match prior with
  | [] -> None
  | e :: _ -> e.e_pack

let prior_rest (prior:list entry) : Tot (list entry) =
  match prior with
  | [] -> []
  | _ :: rest -> rest

let rec walk (pr:prims) (env:param_env) (reuse:reuse_fn) (prior:list entry)
  (cols:schema) (a:alive{awidth (len cols) a}) (p:list wstep)
  : Tot (outcome state eval_error) (decreases p) =
  match p with
  | [] -> Ok ({ s_out = W cols a; s_entries = [] })
  | s :: rest ->
    (match s with
     | WFilter pred ->
       (match step_cells pr env cols pred reuse (prior_cells prior) a with
        | Error err -> Error err
        | Ok cs ->
          awidth_keep_true (len cols) a cs;
          (match walk pr env reuse (prior_rest prior) cols (keep_true a cs) rest with
           | Error err -> Error err
           | Ok st -> Ok ({ st with s_entries = { e_cells = cs; e_pack = None } :: st.s_entries })))
     | WDerive name x ->
       (match derived_typing_in env cols x with
        | Refused -> Error (float_beside_decimal name)
        | dt ->
          (match step_cells pr env cols x reuse (prior_cells prior) a with
           | Error err -> Error err
           | Ok cs ->
             (match column_type_by dt name (cells_of cs) with
              | Error err -> Error err
              | Ok ty ->
                let k = pack_key_of pr ty cs in
                let i = index_of name cols in
                (* D37, D38: the column packed as before keeps reusing; retyped, or carried the
                   other way, nothing is reused from here. *)
                let reuse' = if prior_pack prior = Some k then reuse else no_reuse in
                awidth_extend (len cols) i a (pack_cache pr k cs);
                (match walk pr env reuse' (prior_rest prior) (ext_schema i name ty cols) (extend i a (pack_cache pr k cs)) rest with
                 | Error err -> Error err
                 | Ok st -> Ok ({ st with s_entries = { e_cells = cs; e_pack = Some k } :: st.s_entries }))))))

(* F#: `Incremental.prime` — the walk with no prior state: every row evaluated. *)
let prime (pr:prims) (env:param_env) (cols:schema) (a:alive{awidth (len cols) a}) (p:list wstep)
  : Tot (outcome state eval_error) =
  walk pr env no_reuse [] cols a p

(* F#: `Incremental.refresh` on the row-local path — the walk reusing every unnamed row's cells
   from the prior state's caches. *)
let refresh (pr:prims) (env:param_env) (st:state) (named:option (list token))
  (cols:schema) (a:alive{awidth (len cols) a}) (p:list wstep)
  : Tot (outcome state eval_error) =
  walk pr env (reuse_of named) st.s_entries cols a p

(* ======================================================================================
   5. The evaluator reads names, never types.
   ====================================================================================== *)

let rec len_names (#b:Type0) (l:list (string & b)) : Lemma (ensures len (names l) == len l) =
  match l with
  | [] -> ()
  | _ :: t -> len_names t

let rec index_of_names (#b #c:Type0) (n:string) (l:list (string & b)) (m:list (string & c))
  : Lemma (requires names l == names m) (ensures index_of n l == index_of n m) =
  match l, m with
  | [], [] -> ()
  | _ :: lt, _ :: mt -> index_of_names n lt mt

let rec eval_expr_names (pr:prims) (env:param_env) (cols:schema) (cols':schema{names cols' == names cols})
  (row:row_of (len cols)) (x:col_expr)
  : Lemma (ensures (len_names cols; len_names cols'; eval_expr pr env cols row x == eval_expr pr env cols' row x))
          (decreases x) =
  len_names cols; len_names cols';
  match x with
  | Col n -> index_of_names n cols cols'
  | Lit _ | Param _ | Now _ | InParam _ _ | Convert _ _ -> ()
  | Binary _ a b ->
    eval_expr_names pr env cols cols' row a;
    eval_expr_names pr env cols cols' row b
  | Not a | Cast _ a | IsNull a -> eval_expr_names pr env cols cols' row a
  | Coalesce xs -> coalesce_names pr env cols cols' row xs
  | Case cases els ->
    case_names pr env cols cols' row cases;
    eval_expr_names pr env cols cols' row els
  | InList a items ->
    eval_expr_names pr env cols cols' row a;
    (match eval_expr pr env cols row a with
     | Error _ -> ()
     | Ok Null -> ()
     | Ok sv -> in_names pr env cols cols' row sv false items)
  | ApplyFn _ args -> args_names pr env cols cols' row args
  | Quotient a b _ ->
    eval_expr_names pr env cols cols' row a;
    eval_expr_names pr env cols cols' row b
  | Rounded a _ -> eval_expr_names pr env cols cols' row a
and coalesce_names (pr:prims) (env:param_env) (cols:schema) (cols':schema{names cols' == names cols})
  (row:row_of (len cols)) (xs:list col_expr)
  : Lemma (ensures (len_names cols; len_names cols'; eval_coalesce pr env cols row xs == eval_coalesce pr env cols' row xs))
          (decreases xs) =
  len_names cols; len_names cols';
  match xs with
  | [] -> ()
  | x :: rest ->
    eval_expr_names pr env cols cols' row x;
    coalesce_names pr env cols cols' row rest
and case_names (pr:prims) (env:param_env) (cols:schema) (cols':schema{names cols' == names cols})
  (row:row_of (len cols)) (cases:list (col_expr & col_expr))
  : Lemma (ensures (len_names cols; len_names cols'; eval_case pr env cols row cases == eval_case pr env cols' row cases))
          (decreases cases) =
  len_names cols; len_names cols';
  match cases with
  | [] -> ()
  | (w, t) :: rest ->
    eval_expr_names pr env cols cols' row w;
    eval_expr_names pr env cols cols' row t;
    case_names pr env cols cols' row rest
and in_names (pr:prims) (env:param_env) (cols:schema) (cols':schema{names cols' == names cols})
  (row:row_of (len cols)) (sv:cell) (saw_null:bool) (items:list col_expr)
  : Lemma (ensures (len_names cols; len_names cols'; eval_in pr env cols row sv saw_null items == eval_in pr env cols' row sv saw_null items))
          (decreases items) =
  len_names cols; len_names cols';
  match items with
  | [] -> ()
  | it :: rest ->
    eval_expr_names pr env cols cols' row it;
    in_names pr env cols cols' row sv true rest;
    in_names pr env cols cols' row sv saw_null rest
and args_names (pr:prims) (env:param_env) (cols:schema) (cols':schema{names cols' == names cols})
  (row:row_of (len cols)) (args:list col_expr)
  : Lemma (ensures (len_names cols; len_names cols'; eval_args pr env cols row args == eval_args pr env cols' row args))
          (decreases args) =
  len_names cols; len_names cols';
  match args with
  | [] -> ()
  | a :: rest ->
    eval_expr_names pr env cols cols' row a;
    args_names pr env cols cols' row rest

(* ======================================================================================
   6. Priming is full evaluation — `prime_is_full`.
   ====================================================================================== *)

(* The walk's filter step is `filter_rows`: same error, or the kept rows are the filtered rows. *)
let rec cells_filter (pr:prims) (env:param_env) (cols:schema) (pred:col_expr) (a:alive{awidth (len cols) a})
  : Lemma (ensures (rows_of_width (len cols) a;
                    (match step_cells pr env cols pred no_reuse [] a, filter_rows pr env cols (rows_of a) pred with
                     | Ok cs, Ok rs -> rows_of (keep_true a cs) == rs
                     | Error e, Error e' -> e == e'
                     | _ -> False)))
          (decreases a) =
  rows_of_width (len cols) a;
  match a with
  | [] -> ()
  | (t, r) :: rest -> cells_filter pr env cols pred rest

(* The walk's derive cells are `derive_cells`: same error, or the same cells. *)
let rec cells_derive (pr:prims) (env:param_env) (cols:schema) (x:col_expr) (a:alive{awidth (len cols) a})
  : Lemma (ensures (rows_of_width (len cols) a;
                    (match step_cells pr env cols x no_reuse [] a, derive_cells pr env cols (rows_of a) x with
                     | Ok cs, Ok vs -> cells_of cs == vs
                     | Error e, Error e' -> e == e'
                     | _ -> False)))
          (decreases a) =
  rows_of_width (len cols) a;
  match a with
  | [] -> ()
  | (t, r) :: rest -> cells_derive pr env cols x rest

let rec len_rows_of (a:alive) : Lemma (ensures len (rows_of a) == len a) =
  match a with
  | [] -> ()
  | _ :: rest -> len_rows_of rest

let rec rows_of_extend (i:option nat) (a:alive) (cs:step_cache{len cs = len a})
  : Lemma (ensures (len_cells_of cs; len_rows_of a;
                    (match i with
                     | Some j -> rows_of (extend i a cs) == zip_replace j (rows_of a) (cells_of cs)
                     | None -> rows_of (extend i a cs) == zip_append (rows_of a) (cells_of cs))))
          (decreases a) =
  len_cells_of cs; len_rows_of a;
  match a, cs with
  | [], [] -> ()
  | _ :: ar, _ :: cr -> rows_of_extend i ar cr

let rec walk_is_go (pr:prims) (other:other_fn) (env:param_env) (cols:schema) (a:alive{awidth (len cols) a})
  (p:list wstep) (n:nat)
  : Lemma (ensures (match walk pr env no_reuse [] cols a p, go (eval_step pr other env) (frame_of cols a) n (embed p) with
                    | Ok st, Ok (f, _) -> frame_of_walked st.s_out == f
                    | Error e, Error e' -> e == e'
                    | _ -> False))
          (decreases p) =
  rows_of_width (len cols) a;
  match p with
  | [] -> ()
  | WFilter pred :: rest ->
    cells_filter pr env cols pred a;
    (match step_cells pr env cols pred no_reuse [] a with
     | Error _ -> ()
     | Ok cs ->
       awidth_keep_true (len cols) a cs;
       walk_is_go pr other env cols (keep_true a cs) rest (n + cost_of (frame_of cols a) (Filter pred)))
  | WDerive name x :: rest ->
    (match derived_typing_in env cols x with
     | Refused -> ()
     | dt ->
       cells_derive pr env cols x a;
       (match step_cells pr env cols x no_reuse [] a with
        | Error _ -> ()
        | Ok cs ->
          (match column_type_by dt name (cells_of cs) with
           | Error _ -> ()
           | Ok ty ->
             let k = pack_key_of pr ty cs in
             let i = index_of name cols in
             pack_cache_is_pack_cells pr ty cs;
             rows_of_extend i a (pack_cache pr k cs);
             awidth_extend (len cols) i a (pack_cache pr k cs);
             walk_is_go pr other env (ext_schema i name ty cols) (extend i a (pack_cache pr k cs)) rest
               (n + cost_of (frame_of cols a) (Derive name x)))))

(* THEOREM. Priming a row-local pipeline computes what the reference evaluator computes: the same
   frame, or the same error, over every cell primitive, every twelve-verb evaluator (never reached),
   every environment and every well-formed source. *)
let prime_is_full (pr:prims) (other:other_fn) (env:param_env) (cols:schema) (a:alive{awidth (len cols) a}) (p:list wstep)
  : Lemma (ensures (match prime pr env cols a p, eval_uncounted pr other env (embed p) (frame_of cols a) with
                    | Ok st, Ok f -> frame_of_walked st.s_out == f
                    | Error e, Error e' -> e == e'
                    | _ -> False)) =
  walk_is_go pr other env cols a p 0

(* ======================================================================================
   7. A refresh is a prime — `refresh_is_prime`.
   ====================================================================================== *)

(* THE DELTA PREMISE, as the walk reads it: every alive row the walk may reuse (one the delta does
   not name) is a row of the prior source, under its token, with the cells it had. This is what
   production requires of a delta — "The delta must truthfully describe the change (`Delta.diff`
   produces exactly that)" — read as the one fact the per-row cache needs. *)
let rec unchanged (reuse:reuse_fn) (a:alive) (a0:alive) : Tot bool (decreases a) =
  match a with
  | [] -> true
  | (t, r) :: rest ->
    (not (reuse t) || (match assoc t a0 with Some r0 -> r0 = r | None -> false))
    && unchanged reuse rest a0

(* A row found under a token in a well-formed source is as wide as the schema. *)
let rec assoc_width (n:nat) (a0:alive) (t:token)
  : Lemma (requires awidth n a0)
          (ensures (match assoc t a0 with Some r0 -> len r0 = n | None -> True)) =
  match a0 with
  | [] -> ()
  | (t0, _) :: rest -> if t0 = t then () else assoc_width n rest t

(* A fresh step's cache answers every token of its source with that row's evaluation. *)
let rec assoc_step_cells (pr:prims) (env:param_env) (cols:schema) (x:col_expr) (a0:alive{awidth (len cols) a0}) (t:token)
  : Lemma (requires Ok? (step_cells pr env cols x no_reuse [] a0))
          (ensures (match assoc t a0 with
                    | Some r0 ->
                      if len r0 = len cols then
                        Ok? (eval_expr pr env cols r0 x)
                        /\ assoc t (Ok?._0 (step_cells pr env cols x no_reuse [] a0)) == Some (Ok?._0 (eval_expr pr env cols r0 x))
                      else True
                    | None -> True))
          (decreases a0) =
  match a0 with
  | [] -> ()
  | (t0, r0) :: rest -> if t0 = t then () else assoc_step_cells pr env cols x rest t

(* The reused step is the fresh step: a reused row's cached cell IS its evaluation, by the
   premise and by `eval_expr_names` (the prior walk may have typed a column differently). *)
let rec step_cells_reuse (pr:prims) (env:param_env) (cols0:schema) (cols:schema{names cols == names cols0})
  (x:col_expr) (reuse:reuse_fn) (a0:alive{awidth (len cols0) a0}) (a:alive{awidth (len cols) a})
  : Lemma (requires Ok? (step_cells pr env cols0 x no_reuse [] a0) /\ unchanged reuse a a0)
          (ensures step_cells pr env cols x reuse (Ok?._0 (step_cells pr env cols0 x no_reuse [] a0)) a
                   == step_cells pr env cols x no_reuse [] a)
          (decreases a) =
  len_names cols; len_names cols0;
  match a with
  | [] -> ()
  | (t, r) :: rest ->
    (if reuse t then
       (match assoc t a0 with
        | Some r0 -> assoc_width (len cols0) a0 t; assoc_step_cells pr env cols0 x a0 t; eval_expr_names pr env cols0 cols r0 x
        | None -> ()));
    step_cells_reuse pr env cols0 cols x reuse a0 rest

(* The premise survives a filter: a reused row the filter keeps now was kept then, under its
   token, because its cell is the same cell. *)
let rec assoc_keep_true (pr:prims) (env:param_env) (cols:schema) (x:col_expr) (a0:alive{awidth (len cols) a0}) (t:token)
  : Lemma (requires Ok? (step_cells pr env cols x no_reuse [] a0))
          (ensures (match assoc t a0 with
                    | Some r0 ->
                      if len r0 = len cols then
                        (match eval_expr pr env cols r0 x with
                         | Ok (Bool true) -> assoc t (keep_true a0 (Ok?._0 (step_cells pr env cols x no_reuse [] a0))) == Some r0
                         | _ -> True)
                      else True
                    | None -> True))
          (decreases a0) =
  match a0 with
  | [] -> ()
  | (t0, r0) :: rest -> if t0 = t then () else assoc_keep_true pr env cols x rest t

let rec unchanged_keep_true (pr:prims) (env:param_env) (cols0:schema) (cols:schema{names cols == names cols0})
  (x:col_expr) (reuse:reuse_fn) (a0:alive{awidth (len cols0) a0}) (a:alive{awidth (len cols) a})
  : Lemma (requires Ok? (step_cells pr env cols0 x no_reuse [] a0) /\ Ok? (step_cells pr env cols x no_reuse [] a)
                    /\ unchanged reuse a a0)
          (ensures unchanged reuse (keep_true a (Ok?._0 (step_cells pr env cols x no_reuse [] a)))
                                   (keep_true a0 (Ok?._0 (step_cells pr env cols0 x no_reuse [] a0))))
          (decreases a) =
  len_names cols; len_names cols0;
  match a with
  | [] -> ()
  | (t, r) :: rest ->
    (if reuse t then
       (match assoc t a0 with
        | Some r0 -> assoc_width (len cols0) a0 t; eval_expr_names pr env cols0 cols r0 x; assoc_keep_true pr env cols0 x a0 t
        | None -> ()));
    unchanged_keep_true pr env cols0 cols x reuse a0 rest

(* The premise survives a derive: a reused row's extended row is the extended prior row — its
   cell packed under the key, the same key on both walks (Phase 470). *)
let rec assoc_extend (pr:prims) (env:param_env) (cols:schema) (x:col_expr) (k:pack_key) (i:option nat) (a0:alive{awidth (len cols) a0}) (t:token)
  : Lemma (requires Ok? (step_cells pr env cols x no_reuse [] a0))
          (ensures (match assoc t a0 with
                    | Some r0 ->
                      if len r0 = len cols then
                        (match eval_expr pr env cols r0 x with
                         | Ok v -> assoc t (extend i a0 (pack_cache pr k (Ok?._0 (step_cells pr env cols x no_reuse [] a0))))
                                   == Some (ext_one i r0 (pack_one pr k.k_type k.k_typed v))
                         | _ -> True)
                      else True
                    | None -> True))
          (decreases a0) =
  match a0 with
  | [] -> ()
  | (t0, r0) :: rest -> if t0 = t then () else assoc_extend pr env cols x k i rest t

let rec unchanged_extend (pr:prims) (env:param_env) (cols0:schema) (cols:schema{names cols == names cols0})
  (x:col_expr) (reuse:reuse_fn) (k:pack_key) (i:option nat) (a0:alive{awidth (len cols0) a0}) (a:alive{awidth (len cols) a})
  : Lemma (requires Ok? (step_cells pr env cols0 x no_reuse [] a0) /\ Ok? (step_cells pr env cols x no_reuse [] a)
                    /\ unchanged reuse a a0)
          (ensures unchanged reuse (extend i a (pack_cache pr k (Ok?._0 (step_cells pr env cols x no_reuse [] a))))
                                   (extend i a0 (pack_cache pr k (Ok?._0 (step_cells pr env cols0 x no_reuse [] a0)))))
          (decreases a) =
  len_names cols; len_names cols0;
  match a with
  | [] -> ()
  | (t, r) :: rest ->
    (if reuse t then
       (match assoc t a0 with
        | Some r0 -> assoc_width (len cols0) a0 t; eval_expr_names pr env cols0 cols r0 x; assoc_extend pr env cols0 x k i a0 t
        | None -> ()));
    unchanged_extend pr env cols0 cols x reuse k i a0 rest

let rec names_retype_at (i:nat) (ty:column_type) (cols:schema)
  : Lemma (ensures names (retype_at i ty cols) == names cols) (decreases cols) =
  match cols with
  | [] -> ()
  | _ :: rest -> if i = 0 then () else names_retype_at (i - 1) ty rest

let rec names_app (#b:Type0) (l m:list (string & b))
  : Lemma (ensures names (app l m) == app (names l) (names m)) =
  match l with
  | [] -> ()
  | _ :: t -> names_app t m

let names_ext_schema (i:option nat) (name:string) (ty ty0:column_type) (cols0:schema) (cols:schema{names cols == names cols0})
  : Lemma (ensures names (ext_schema i name ty cols) == names (ext_schema i name ty0 cols0)) =
  match i with
  | Some j -> names_retype_at j ty cols; names_retype_at j ty0 cols0
  | None -> names_app cols [(name, ty)]; names_app cols0 [(name, ty0)]

(* A walk that reuses nothing reads no prior entry. *)
let rec step_cells_no_reuse (pr:prims) (env:param_env) (cols:schema) (x:col_expr) (prior:step_cache) (a:alive{awidth (len cols) a})
  : Lemma (ensures step_cells pr env cols x no_reuse prior a == step_cells pr env cols x no_reuse [] a) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> step_cells_no_reuse pr env cols x prior rest

let rec walk_no_reuse (pr:prims) (env:param_env) (prior:list entry) (cols:schema) (a:alive{awidth (len cols) a}) (p:list wstep)
  : Lemma (ensures walk pr env no_reuse prior cols a p == walk pr env no_reuse [] cols a p) (decreases p) =
  match p with
  | [] -> ()
  | WFilter pred :: rest ->
    step_cells_no_reuse pr env cols pred (prior_cells prior) a;
    (match step_cells pr env cols pred no_reuse [] a with
     | Error _ -> ()
     | Ok cs -> awidth_keep_true (len cols) a cs; walk_no_reuse pr env (prior_rest prior) cols (keep_true a cs) rest)
  | WDerive name x :: rest ->
    (match derived_typing_in env cols x with
     | Refused -> ()
     | dt ->
       step_cells_no_reuse pr env cols x (prior_cells prior) a;
       (match step_cells pr env cols x no_reuse [] a with
        | Error _ -> ()
        | Ok cs ->
          (match column_type_by dt name (cells_of cs) with
           | Error _ -> ()
           | Ok ty ->
             let k = pack_key_of pr ty cs in
             let i = index_of name cols in
             awidth_extend (len cols) i a (pack_cache pr k cs);
             walk_no_reuse pr env (prior_rest prior) (ext_schema i name ty cols) (extend i a (pack_cache pr k cs)) rest)))

(* The walk reusing against a prior walk's entries is the walk reusing nothing: through a derive
   packed under the prior's key by the premise, and past one packed under another key because
   nothing is reused from there on. *)
let rec walk_reuse (pr:prims) (env:param_env) (reuse:reuse_fn) (cols0:schema) (cols:schema{names cols == names cols0})
  (a0:alive{awidth (len cols0) a0}) (a:alive{awidth (len cols) a}) (p:list wstep)
  : Lemma (requires Ok? (walk pr env no_reuse [] cols0 a0 p) /\ unchanged reuse a a0)
          (ensures walk pr env reuse (Ok?._0 (walk pr env no_reuse [] cols0 a0 p)).s_entries cols a p
                   == walk pr env no_reuse [] cols a p)
          (decreases p) =
  len_names cols; len_names cols0;
  match p with
  | [] -> ()
  | WFilter pred :: rest ->
    step_cells_reuse pr env cols0 cols pred reuse a0 a;
    (match step_cells pr env cols pred no_reuse [] a with
     | Error _ -> ()
     | Ok cs ->
       unchanged_keep_true pr env cols0 cols pred reuse a0 a;
       let cs0 = Ok?._0 (step_cells pr env cols0 pred no_reuse [] a0) in
       awidth_keep_true (len cols0) a0 cs0;
       awidth_keep_true (len cols) a cs;
       walk_reuse pr env reuse cols0 cols (keep_true a0 cs0) (keep_true a cs) rest)
  | WDerive name x :: rest ->
    (match derived_typing_in env cols x with
     | Refused -> ()
     | dt ->
       step_cells_reuse pr env cols0 cols x reuse a0 a;
       (match step_cells pr env cols x no_reuse [] a with
        | Error _ -> ()
        | Ok cs ->
          (match column_type_by dt name (cells_of cs) with
           | Error _ -> ()
           | Ok ty ->
             let cs0 = Ok?._0 (step_cells pr env cols0 x no_reuse [] a0) in
             let dt0 = derived_typing_in env cols0 x in
             let ty0 = Ok?._0 (column_type_by dt0 name (cells_of cs0)) in
             let k = pack_key_of pr ty cs in
             let k0 = pack_key_of pr ty0 cs0 in
             index_of_names name cols cols0;
             let i = index_of name cols in
             names_ext_schema i name ty ty0 cols0 cols;
             awidth_extend (len cols0) i a0 (pack_cache pr k0 cs0);
             awidth_extend (len cols) i a (pack_cache pr k cs);
             if k0 = k then begin
               unchanged_extend pr env cols0 cols x reuse k i a0 a;
               walk_reuse pr env reuse (ext_schema i name ty0 cols0) (ext_schema i name ty cols)
                 (extend i a0 (pack_cache pr k0 cs0)) (extend i a (pack_cache pr k cs)) rest
             end else
               walk_no_reuse pr env (prior_rest (Ok?._0 (walk pr env no_reuse [] cols0 a0 p)).s_entries)
                 (ext_schema i name ty cols) (extend i a (pack_cache pr k cs)) rest)))

(* THEOREM (the state theorem). A refresh against a state primed over the prior source, under a
   delta that names every row that moved, is the prime over the new source — the output and
   every step's cache. The new source may retype a column; it may not rename one. *)
let refresh_is_prime (pr:prims) (env:param_env) (cols0:schema) (cols:schema{names cols == names cols0})
  (a0:alive{awidth (len cols0) a0}) (a:alive{awidth (len cols) a}) (named:option (list token)) (p:list wstep)
  : Lemma (requires Ok? (prime pr env cols0 a0 p) /\ unchanged (reuse_of named) a a0)
          (ensures refresh pr env (Ok?._0 (prime pr env cols0 a0 p)) named cols a p == prime pr env cols a p) =
  walk_reuse pr env (reuse_of named) cols0 cols a0 a p

(* THEOREM (the agreement theorem). The refreshed answer is the full evaluation of the pipeline
   over the updated source — the same frame, or the same error. *)
let refresh_agrees (pr:prims) (other:other_fn) (env:param_env) (cols0:schema) (cols:schema{names cols == names cols0})
  (a0:alive{awidth (len cols0) a0}) (a:alive{awidth (len cols) a}) (named:option (list token)) (p:list wstep)
  : Lemma (requires Ok? (prime pr env cols0 a0 p) /\ unchanged (reuse_of named) a a0)
          (ensures (match refresh pr env (Ok?._0 (prime pr env cols0 a0 p)) named cols a p,
                          eval_uncounted pr other env (embed p) (frame_of cols a) with
                    | Ok st, Ok f -> frame_of_walked st.s_out == f
                    | Error e, Error e' -> e == e'
                    | _ -> False)) =
  refresh_is_prime pr env cols0 cols a0 a named p;
  prime_is_full pr other env cols a p
