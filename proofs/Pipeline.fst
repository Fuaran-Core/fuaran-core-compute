(*
   Pipeline — the counted transform-pipeline driver: `Fuaran.Core.DataFrame`'s closed `ColExpr` and
   `Transform` algebra, `evalPipelineWithInEnvCounted`'s fold and its cost model, modelled clause
   for clause and proved total and budget-monotone (fuaran-core Phase 154).

   WHAT IS MODELLED. `src/Fuaran.Core.DataFrame/DataFrame.fs` — the DRIVER and the vocabulary it
   folds over:

     - the two closed DUs, `ColExpr` (thirteen cases) and `Transform` (fourteen verbs), with every
       payload type they carry (`Cell`, `ColumnType`, `BinOp`, `ScalarFn`, `AggFn`, `WindowFn`,
       `JoinKind`, `SortDir`, `NowGrain`, `Slot`, `Agg`, `WindowSpec`, `PivotSpec`, `DataSource`)
       — restated in full so that a clause over a VERB (`costOf`) and a walk over an EXPRESSION
       (`expr_nodes`) are total over the same alphabet the F# compiler closes;
     - the evaluator's working form `Frame` (a schema and row-major rows — `toFrame` / `ofFrame`
       are the transpose each way, so the model carries the row-major form only and a `Table`
       IS a frame here);
     - `evalPipelineWithInEnvCounted`: its local `costOf` — a `Filter` or a `Derive` is charged
       the frame's row count where it stands, every other verb is charged nothing — and its loop
       `go`, which folds the steps threading the frame and the count; and
     - `evalPipelineWithInEnv`, which is `evalPipelineWithInEnvCounted … |> Result.map fst` and
       nothing else.

   ONE thing is a PARAMETER rather than a clause, exactly as Phase 176 made the pipeline
   evaluator one and Phase 186 the node evaluator: the STEP EVALUATOR. Production's `evalStep
   resolve env` dispatches fourteen verbs to fourteen primitives — three-valued predicates, group
   aggregation, windows, pivots, joins, the pinned float layout — and none of that is what this
   phase is about. The model takes `step : frame -> transform -> outcome frame e` (the resolver
   and the param env closed over, as the F# closure closes over them), and every theorem holds
   for EVERY step evaluator. The differential instantiates it at production's own primitives, one
   step at a time, through the public entry point (`pipeline-step-evaluator-abstract`).

   WHAT IS PROVED, over any step evaluator, any pipeline and any input frame:

     - `eval_total` — the counted evaluator returns `Ok` exactly when every step succeeded along
       the walk, and then the count is the walk's cost; otherwise it returns the FIRST failing
       step's own error, verbatim. The driver invents no refusal: there is no case in `go` that
       produces an `Error` a step did not. Termination is structural on the pipeline, checked by
       the prover. `over_limit_not_refused` is the corollary that names the finding below.
     - `budget_monotone` — the count is monotone in the pipeline PREFIX: if `p ++ q` evaluates to
       `Ok (_, m')` then `p` evaluates to `Ok (_, m)` with `m <= m'` (`go_app` is the split —
       the fold over a concatenation is the fold over the prefix continued over the suffix from
       the prefix's frame and count — and `go_count_ge` that a count never goes down).
     - `work_bounded` — the §21.8 expression-node limit, taken as a HYPOTHESIS on the pipeline
       (`within_limit`: every expression a `Filter` or a `Derive` carries has at most
       `Limits.max_expr_nodes` nodes), bounds the expression work the count stands for: the
       node visits the walk can cost (`work` — rows times nodes at each charged step, since
       `evalExpr` is structural and visits a node at most once) are at most the count times the
       limit. This is what the count MEANS under the format's bound, and it is stated as a
       hypothesis because that is all it is on this tree — see the finding.
     - `uncounted_is_projection` — `evalPipelineWithInEnv` is `evalPipelineWithInEnvCounted`
       projected on its first component. It is the one-line identity the source is, and it
       replaces the `counted_agrees` the phase was chartered with: there is no second path for
       the counted one to agree with.

   THE FINDING, read off the tree and carried as a theorem so that it goes red if the tree moves:
   `Limits.max_expr_nodes` (§21.8, 512) is ENFORCED NOWHERE under `src/`. `over_limit_not_refused`
   says a pipeline outside the limit whose every step succeeds evaluates to `Ok` — the driver has
   no clause that reads the bound — and the differential asserts the same on the shipped evaluator
   with a 513-node expression. Whether a conformant host must refuse such a pipeline is §21.2's
   question for a later operator decision; this phase records it and changes nothing, because
   adding the refusal would breach its own zero-impact constraint.

   WHAT IS NOT CLAIMED. Anything about any verb's semantics: what a `Filter` keeps, what a
   `GroupBy` emits, what a `Join` fans out to — the step evaluator is the parameter and no theorem
   is about one. Anything about `toFrame` / `ofFrame` beyond their being the transpose (the model
   holds one form). That the count is bounded by the INPUT's row count: a `Join` or a `Union` can
   grow a frame, and the count is charged where the step stands, which is what monotone-in-the-
   prefix says and what a bound in the input would not. The `…At` entry points (`substituteNow`
   then this driver) and the codec.

   HOW TO READ IT. Every definition names its F# counterpart. The module opens `Limits` for the
   one constant it takes as a premise, and nothing else; it restates `outcome` and the list
   helpers it needs, and extracts beside the other models sharing only `Prims.fs` and the
   `option` shim.

   Apache-2.0, like everything beside it.
*)
module Pipeline

open Limits

(* ======================================================================================
   0. The helpers, self-contained, each naming the FSharp.Core function it stands for.
   ====================================================================================== *)

(* F#: `Result<'a, 'e>`. *)
type outcome (a e:Type) =
  | Ok    : a -> outcome a e
  | Error : e -> outcome a e

(* F#: `Result.map`. *)
let result_map (#a #b #e:Type) (f:a -> b) (r:outcome a e) : Tot (outcome b e) =
  match r with
  | Ok x -> Ok (f x)
  | Error err -> Error err

(* F#: `Result.bind`. *)
let result_bind (#a #b #e:Type) (r:outcome a e) (f:a -> outcome b e) : Tot (outcome b e) =
  match r with
  | Ok x -> f x
  | Error err -> Error err

(* F#: `List.length`. *)
let rec len (#a:Type) (l:list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

(* F#: `@`. *)
let rec app (#a:Type) (l m:list a) : Tot (list a) =
  match l with
  | [] -> m
  | h :: t -> h :: app t m

(* ======================================================================================
   1. The vocabulary — every closed type a `ColExpr` or a `Transform` carries, restated so the
      two algebras below are total over the alphabet the F# compiler closes. `Column.fs` owns
      `Cell` / `ColumnType` / `AggFn` / `DataSource`; `DataFrame.fs` the rest.
   ====================================================================================== *)

(* F#: `ColumnType`. *)
type column_type =
  | IntType
  | FloatType
  | BoolType
  | StringType
  | DateType
  | TimestampType

(* F#: `Cell`. A float crosses as an opaque carrier (its round-trip `R` text), as in `Query.fst`:
   the driver reads no cell, and the differential converts at the boundary. *)
type cell =
  | Int       : int -> cell
  | Float     : string -> cell
  | Bool      : bool -> cell
  | Str       : string -> cell
  | Date      : string -> cell
  | Timestamp : string -> cell
  | Null      : cell

(* F#: `JoinKind`. *)
type join_kind =
  | Inner
  | Left
  | Right
  | Outer
  | Semi
  | Anti

(* F#: `WindowFn`. `NTile` carries its bucket count. *)
type window_fn =
  | RowNumber
  | Rank
  | Lag
  | Lead
  | CumulSum
  | RollingMean
  | DenseRank
  | CompetitionRank
  | NTile : int -> window_fn
  | CumulMax
  | CumulMin
  | RollingSum

(* F#: `SortDir`. *)
type sort_dir =
  | Asc
  | Desc

(* F#: `ScalarFn`. *)
type scalar_fn =
  | Abs
  | Round
  | Floor
  | Ceil
  | Length
  | Lower
  | Upper
  | Substr
  | DatePart
  | Concat
  | Trim
  | Replace
  | DateDiffDays
  | Sqrt
  | Least
  | Greatest
  | IndexOf

(* F#: `BinOp`. *)
type bin_op =
  | Add
  | Sub
  | Mul
  | Div
  | Mod
  | Eq
  | Ne
  | Lt
  | Le
  | Gt
  | Ge
  | And
  | Or
  | Contains
  | StartsWith
  | EndsWith

(* F#: `NowGrain.Date` / `NowGrain.Timestamp` — prefixed here because `Date` and `Timestamp` are
   `cell` constructors in the same namespace (the F# type is `RequireQualifiedAccess` for exactly
   that collision). *)
type now_grain =
  | GrainDate
  | GrainTimestamp

(* F#: `AggFn`. *)
type agg_fn =
  | Sum
  | Mean
  | Min
  | Max
  | Count
  | Median
  | StdDev
  | First
  | Last
  | CountDistinct

(* F#: `Slot<'T>` — `Slot.Lit` / `Slot.Param`, prefixed for the same reason as `now_grain`. *)
type slot (a:Type) =
  | SlotLit   : a -> slot a
  | SlotParam : string -> slot a

(* F#: `Schema`. *)
type schema = list (string & column_type)

(* F#: the evaluator's private `Frame` — `{ Cols: Schema; Rows: Cell list list }` — and, through
   `toFrame` / `ofFrame` (the transpose each way, Phase 206), the `Table` it is a view of. *)
type frame = { cols : schema; rows : list (list cell) }

(* F#: `DataSource`. An embedded table crosses as its row-major view. *)
type data_source =
  | Embedded : frame -> data_source
  | Ref      : string -> data_source

(* F#: `ColExpr`, all thirteen cases. *)
type col_expr =
  | Col      : string -> col_expr
  | Lit      : cell -> col_expr
  | Param    : string -> col_expr
  | Binary   : bin_op -> col_expr -> col_expr -> col_expr
  | Not      : col_expr -> col_expr
  | Coalesce : list col_expr -> col_expr
  | Case     : list (col_expr & col_expr) -> col_expr -> col_expr
  | Cast     : column_type -> col_expr -> col_expr
  | ApplyFn  : scalar_fn -> list col_expr -> col_expr
  | InList   : col_expr -> list col_expr -> col_expr
  | IsNull   : col_expr -> col_expr
  | InParam  : col_expr -> string -> col_expr
  | Now      : now_grain -> col_expr

(* F#: `Agg` — `{ Name; Fn; Of }`. *)
type agg = { a_name : string; a_fn : agg_fn; a_of : string }

(* F#: `WindowSpec` — `{ PartitionBy; OrderBy; Fn; Of; As }`. *)
type window_spec =
  { partition_by : list string;
    order_by     : list (string & sort_dir);
    w_fn         : window_fn;
    w_of         : string;
    w_as         : string }

(* F#: `PivotSpec` — `{ Index; On; Values; Agg }`. *)
type pivot_spec = { p_index : list string; p_on : string; p_values : string; p_agg : agg_fn }

(* F#: `Transform`, all fourteen verbs, in the DU's order. *)
type transform =
  | Filter    : col_expr -> transform
  | Project   : list (string & string) -> transform
  | Derive    : string -> col_expr -> transform
  | GroupBy   : list string -> list agg -> transform
  | Join      : data_source -> list (string & string) -> join_kind -> transform
  | Window    : window_spec -> transform
  | Pivot     : pivot_spec -> transform
  | Unpivot   : list string -> list string -> transform
  | Sort      : list (slot string & sort_dir) -> transform
  | Distinct  : transform
  | Limit     : slot int -> slot int -> transform
  | Union     : data_source -> transform
  | Intersect : data_source -> transform
  | Except    : data_source -> transform

(* ======================================================================================
   2. The cost model — `costOf`, local to `evalPipelineWithInEnvCounted`:

        let costOf (f: Frame) (step: Transform) =
            match step with
            | Filter _
            | Derive _ -> List.length f.Rows
            | _ -> 0

      "The unit is one evaluation of one step's expression against one row … A `Filter` and a
      `Derive` evaluate their expression once per row alive at that step, so each is charged the
      frame's row count where it stands; every other verb evaluates no per-row expression and is
      charged none."
   ====================================================================================== *)

(* F#: `costOf`. *)
let cost_of (f:frame) (step:transform) : Tot nat =
  match step with
  | Filter _ -> len f.rows
  | Derive _ _ -> len f.rows
  | _ -> 0

(* ======================================================================================
   3. The driver — `evalPipelineWithInEnvCounted`'s loop and the two entry points:

        let rec go f evaluated =
            function
            | [] -> Ok(ofFrame f, evaluated)
            | step :: rest ->
                let cost = costOf f step
                evalStep resolve env f step
                |> Result.bind (fun f' -> go f' (evaluated + cost) rest)
        go (toFrame input) 0 pipeline

      `evalStep resolve env` is the PARAMETER: `step`. `toFrame` / `ofFrame` are the identity
      here because the model holds the row-major form. The error type is abstract — the driver
      reads no error, it threads the step's.
   ====================================================================================== *)

(* F#: `evalStep resolve env`, partially applied — the step evaluator the driver folds with. *)
type step_fn (e:Type) = frame -> transform -> outcome frame e

(* F#: `go`. *)
let rec go (#e:Type) (step:step_fn e) (f:frame) (evaluated:nat) (p:list transform)
  : Tot (outcome (frame & nat) e) (decreases p) =
  match p with
  | [] -> Ok (f, evaluated)
  | s :: rest ->
    let cost = cost_of f s in
    result_bind (step f s) (fun f' -> go step f' (evaluated + cost) rest)

(* F#: `evalPipelineWithInEnvCounted resolve env pipeline input`. *)
let eval_counted (#e:Type) (step:step_fn e) (p:list transform) (input:frame)
  : Tot (outcome (frame & nat) e) =
  go step input 0 p

(* F#: `evalPipelineWithInEnv resolve env pipeline input` —
   `evalPipelineWithInEnvCounted resolve env pipeline input |> Result.map fst`. *)
let eval_uncounted (#e:Type) (step:step_fn e) (p:list transform) (input:frame)
  : Tot (outcome frame e) =
  result_map fst (eval_counted step p input)

(* ======================================================================================
   4. The walk, read off the driver: whether every step succeeds, which error the first failing
      step raises, and what the walk costs. These are the theorems' vocabulary, not the source's
      — production carries the same information inside `go`'s recursion.
   ====================================================================================== *)

(* Every step along the walk from `f` succeeds. *)
let rec walk_ok (#e:Type) (step:step_fn e) (f:frame) (p:list transform) : Tot bool (decreases p) =
  match p with
  | [] -> true
  | s :: rest ->
    (match step f s with
     | Ok f' -> walk_ok step f' rest
     | Error _ -> false)

(* The error of the first failing step along the walk, if any. *)
let rec first_error (#e:Type) (step:step_fn e) (f:frame) (p:list transform)
  : Tot (option e) (decreases p) =
  match p with
  | [] -> None
  | s :: rest ->
    (match step f s with
     | Ok f' -> first_error step f' rest
     | Error err -> Some err)

(* The cost the walk accumulates, charged step by step where each step stands. *)
let rec cost (#e:Type) (step:step_fn e) (f:frame) (p:list transform) : Tot nat (decreases p) =
  match p with
  | [] -> 0
  | s :: rest ->
    (match step f s with
     | Ok f' -> cost_of f s + cost step f' rest
     | Error _ -> cost_of f s)

(* ======================================================================================
   5. Totality — `eval_total`.
   ====================================================================================== *)

(* The driver returns `Ok` exactly when the walk succeeds. *)
let rec go_ok_iff (#e:Type) (step:step_fn e) (f:frame) (n:nat) (p:list transform)
  : Lemma (ensures Ok? (go step f n p) <==> walk_ok step f p) (decreases p) =
  match p with
  | [] -> ()
  | s :: rest ->
    (match step f s with
     | Ok f' -> go_ok_iff step f' (n + cost_of f s) rest
     | Error _ -> ())

(* On a successful walk the count is the walk's cost over the count carried in. *)
let rec go_count (#e:Type) (step:step_fn e) (f:frame) (n:nat) (p:list transform)
  : Lemma (requires walk_ok step f p)
          (ensures (match go step f n p with
                    | Ok (_, m) -> m == n + cost step f p
                    | Error _ -> False))
          (decreases p) =
  match p with
  | [] -> ()
  | s :: rest ->
    (match step f s with
     | Ok f' -> go_count step f' (n + cost_of f s) rest
     | Error _ -> ())

(* On a failing walk the driver returns the first failing step's own error, verbatim. *)
let rec go_error (#e:Type) (step:step_fn e) (f:frame) (n:nat) (p:list transform)
  : Lemma (requires not (walk_ok step f p))
          (ensures (match go step f n p, first_error step f p with
                    | Error err, Some err' -> err == err'
                    | _ -> False))
          (decreases p) =
  match p with
  | [] -> ()
  | s :: rest ->
    (match step f s with
     | Ok f' -> go_error step f' (n + cost_of f s) rest
     | Error _ -> ())

(* THE THEOREM. Every pipeline over every input reaches `Ok` with the walk's cost, or the first
   failing step's named error — and nothing else. The driver adds no refusal of its own. *)
let eval_total (#e:Type) (step:step_fn e) (p:list transform) (input:frame)
  : Lemma (ensures
      (walk_ok step input p /\
       (match eval_counted step p input with
        | Ok (_, m) -> m == cost step input p
        | Error _ -> False))
      \/
      (not (walk_ok step input p) /\
       (match eval_counted step p input, first_error step input p with
        | Error err, Some err' -> err == err'
        | _ -> False))) =
  if walk_ok step input p then go_count step input 0 p else go_error step input 0 p

(* ======================================================================================
   6. Budget monotonicity — `budget_monotone`.
   ====================================================================================== *)

(* The fold over a concatenation is the fold over the prefix, continued over the suffix from
   the prefix's frame and count. *)
let rec go_app (#e:Type) (step:step_fn e) (f:frame) (n:nat) (p q:list transform)
  : Lemma (ensures go step f n (app p q) ==
                   (match go step f n p with
                    | Ok (f', m) -> go step f' m q
                    | Error err -> Error err))
          (decreases p) =
  match p with
  | [] -> ()
  | s :: rest ->
    (match step f s with
     | Ok f' -> go_app step f' (n + cost_of f s) rest q
     | Error _ -> ())

(* A count never goes down: the driver only ever adds. *)
let rec go_count_ge (#e:Type) (step:step_fn e) (f:frame) (n:nat) (p:list transform)
  : Lemma (ensures (match go step f n p with
                    | Ok (_, m) -> n <= m
                    | Error _ -> True))
          (decreases p) =
  match p with
  | [] -> ()
  | s :: rest ->
    (match step f s with
     | Ok f' -> go_count_ge step f' (n + cost_of f s) rest
     | Error _ -> ())

(* THE THEOREM. The count is monotone in the pipeline prefix: a pipeline that evaluates has every
   prefix evaluate, at a count no larger. *)
let budget_monotone (#e:Type) (step:step_fn e) (input:frame) (p q:list transform)
  : Lemma (requires Ok? (eval_counted step (app p q) input))
          (ensures (match eval_counted step p input, eval_counted step (app p q) input with
                    | Ok (_, m), Ok (_, m') -> m <= m'
                    | _ -> False)) =
  go_app step input 0 p q;
  (match go step input 0 p with
   | Ok (f', m) -> go_count_ge step f' m q
   | Error _ -> ())

(* ======================================================================================
   7. The §21.8 premise, and what the count means under it — `work_bounded`.

      `Limits.max_expr_nodes` bounds "`ColExpr` nodes in ONE expression". Nothing under `src/`
      counts them (the finding); here the count is a definition and the bound a hypothesis.
   ====================================================================================== *)

(* The nodes of an expression: one per constructor occurrence, through every list it carries. *)
let rec expr_nodes (e:col_expr) : Tot nat (decreases e) =
  match e with
  | Col _ -> 1
  | Lit _ -> 1
  | Param _ -> 1
  | Now _ -> 1
  | Binary _ a b -> 1 + expr_nodes a + expr_nodes b
  | Not x -> 1 + expr_nodes x
  | Cast _ x -> 1 + expr_nodes x
  | IsNull x -> 1 + expr_nodes x
  | InParam x _ -> 1 + expr_nodes x
  | Coalesce xs -> 1 + exprs_nodes xs
  | ApplyFn _ xs -> 1 + exprs_nodes xs
  | InList x items -> 1 + expr_nodes x + exprs_nodes items
  | Case cases els -> 1 + pairs_nodes cases + expr_nodes els
and exprs_nodes (l:list col_expr) : Tot nat (decreases l) =
  match l with
  | [] -> 0
  | x :: t -> expr_nodes x + exprs_nodes t
and pairs_nodes (l:list (col_expr & col_expr)) : Tot nat (decreases l) =
  match l with
  | [] -> 0
  | (w, t) :: r -> expr_nodes w + expr_nodes t + pairs_nodes r

(* The expressions a step evaluates per row — `evalStep`'s `Filter` and `Derive` arms are the
   only ones that reach `evalExprInRow`; every other verb carries no `ColExpr`. *)
let step_exprs (s:transform) : Tot (list col_expr) =
  match s with
  | Filter e -> [e]
  | Derive _ e -> [e]
  | _ -> []

(* Every expression in the list is within the §21.8 limit. *)
let rec all_within (l:list col_expr) : Tot bool =
  match l with
  | [] -> true
  | e :: t -> expr_nodes e <= max_expr_nodes && all_within t

(* THE PREMISE: every expression the pipeline evaluates is within `Limits.max_expr_nodes`. *)
let rec within_limit (p:list transform) : Tot bool =
  match p with
  | [] -> true
  | s :: rest -> all_within (step_exprs s) && within_limit rest

(* The node visits one step can cost: its charged rows times its expression's nodes. `evalExpr`
   is structural on the expression and visits each node at most once (`Case` and `Coalesce`
   short-circuit; nothing revisits), so this is an upper bound on the work the count stands for. *)
let step_work (f:frame) (s:transform) : Tot nat =
  match s with
  | Filter e ->
    FStar.Math.Lemmas.nat_times_nat_is_nat (len f.rows) (expr_nodes e);
    len f.rows * expr_nodes e
  | Derive _ e ->
    FStar.Math.Lemmas.nat_times_nat_is_nat (len f.rows) (expr_nodes e);
    len f.rows * expr_nodes e
  | _ -> 0

(* The node visits the walk can cost, charged where each step stands, as `cost` is. *)
let rec work (#e:Type) (step:step_fn e) (f:frame) (p:list transform) : Tot nat (decreases p) =
  match p with
  | [] -> 0
  | s :: rest ->
    (match step f s with
     | Ok f' -> step_work f s + work step f' rest
     | Error _ -> step_work f s)

(* One step within the limit costs at most its charge times the limit. *)
let step_work_bounded (f:frame) (s:transform)
  : Lemma (requires all_within (step_exprs s))
          (ensures step_work f s <= cost_of f s * max_expr_nodes) =
  match s with
  | Filter e -> FStar.Math.Lemmas.lemma_mult_le_left (len f.rows) (expr_nodes e) max_expr_nodes
  | Derive _ e -> FStar.Math.Lemmas.lemma_mult_le_left (len f.rows) (expr_nodes e) max_expr_nodes
  | _ -> ()

(* THE THEOREM. Under the §21.8 premise the expression work a walk can cost is at most the count
   it reports times the limit — the count bounds the work, with the format's own constant. *)
let rec work_bounded (#e:Type) (step:step_fn e) (f:frame) (p:list transform)
  : Lemma (requires within_limit p)
          (ensures work step f p <= cost step f p * max_expr_nodes)
          (decreases p) =
  match p with
  | [] -> ()
  | s :: rest ->
    step_work_bounded f s;
    (match step f s with
     | Ok f' ->
       work_bounded step f' rest;
       FStar.Math.Lemmas.distributivity_add_left (cost_of f s) (cost step f' rest) max_expr_nodes
     | Error _ -> ())

(* THE FINDING, as a theorem. A pipeline OUTSIDE the limit whose every step succeeds evaluates to
   `Ok`: the driver has no clause that reads the bound, so the limit is a premise a caller may
   assume and never a refusal the evaluator performs. Goes red the day `go` gains one. *)
let over_limit_not_refused (#e:Type) (step:step_fn e) (p:list transform) (input:frame)
  : Lemma (requires not (within_limit p) /\ walk_ok step input p)
          (ensures Ok? (eval_counted step p input)) =
  go_ok_iff step input 0 p

(* ======================================================================================
   8. The uncounted entry point is the counted one projected — `uncounted_is_projection`.
   ====================================================================================== *)

(* THE IDENTITY. `evalPipelineWithInEnv = evalPipelineWithInEnvCounted |> Result.map fst`. It is
   the definition, and it is stated so the ladder carries the fact the phase was chartered to
   prove as an agreement between two paths: there is one path. *)
let uncounted_is_projection (#e:Type) (step:step_fn e) (p:list transform) (input:frame)
  : Lemma (ensures eval_uncounted step p input == result_map fst (eval_counted step p input)) = ()

(* And so the uncounted path succeeds exactly when the walk does, with the counted path's frame. *)
let uncounted_ok_iff (#e:Type) (step:step_fn e) (p:list transform) (input:frame)
  : Lemma (ensures (Ok? (eval_uncounted step p input) <==> walk_ok step input p) /\
                   (match eval_uncounted step p input, eval_counted step p input with
                    | Ok t, Ok (t', _) -> t == t'
                    | Error err, Error err' -> err == err'
                    | _ -> False)) =
  go_ok_iff step input 0 p
