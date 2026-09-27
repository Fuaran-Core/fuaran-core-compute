(*
   Pipeline — the counted transform-pipeline driver AND the expression evaluator it charges for:
   `Fuaran.Core.DataFrame`'s closed `ColExpr` and `Transform` algebra, the private `evalExpr` with
   its four inner loops, `evalFilter`, `evalDerive`, `evalStep`'s dispatch, and
   `evalPipelineWithInEnvCounted`'s fold with its cost model, modelled clause for clause and proved
   total, budget-monotone and work-bounded (fuaran-core Phase 154; the evaluator made concrete by
   Phase 234).

   WHAT IS MODELLED. `src/Fuaran.Core.DataFrame/DataFrame.fs`:

     - the two closed DUs, `ColExpr` (thirteen cases) and `Transform` (fourteen verbs), with every
       payload type they carry (`Cell`, `ColumnType`, `BinOp`, `ScalarFn`, `AggFn`, `WindowFn`,
       `JoinKind`, `SortDir`, `NowGrain`, `Slot`, `Agg`, `WindowSpec`, `PivotSpec`, `DataSource`)
       and the `EvalError` DU — restated in full so that a clause over a VERB (`costOf`), a walk
       over an EXPRESSION (`expr_nodes`) and the evaluator's own arms are total over the same
       alphabet the F# compiler closes;
     - the evaluator's working form `Frame` (a schema and row-major rows — `toFrame` / `ofFrame`
       are the transpose each way, so the model carries the row-major form only and a `Table`
       IS a frame here), with the well-formedness `toFrame` establishes (every row as wide as
       the schema) carried as a refinement, because it is what `List.item i row` needs;
     - the private `evalExpr env cols row e`, every one of its thirteen arms and its four inner
       loops (`Coalesce`'s `go`, `Case`'s `go`, `InList`'s `go sawNull`, `ApplyFn`'s `evalArgs`),
       in the order production evaluates and with every short circuit production takes;
     - `evalFilter` and `evalDerive` (with `inferType`, `colIndex` and the replace-or-append of a
       derived column) — the two verbs that reach `evalExpr`, and the only two;
     - `evalStep`'s dispatch: `Filter` and `Derive` to the two above, every other verb to its
       primitive;
     - `evalPipelineWithInEnvCounted`: its local `costOf` — a `Filter` or a `Derive` is charged
       the frame's row count where it stands, every other verb is charged nothing — and its loop
       `go`, which folds the steps threading the frame and the count; and
     - `evalPipelineWithInEnv`, which is `evalPipelineWithInEnvCounted … |> Result.map fst` and
       nothing else.

   TWO things are PARAMETERS rather than clauses, and each is an ASSUMPTION the theorems below
   are conditional on exactly as far as this header says and no further:

     - THE CELL PRIMITIVES (`prims`): the four operator-class primitives `evalExpr`'s `Binary`
       arm dispatches to (`arith` / `comparison` / `logical` / `stringPred`, taken as ONE function
       of the operator, which is what the arm's `match op with …` makes of them), `castCell`,
       `applyScalar` and `compareCells`. They are the arithmetic, the coercions and the pinned
       float layout, and no theorem here reads a cell they produce: `eval_expr` threads their
       outcome and nothing else. What is ASSUMED of them is only what their TYPE says — each is a
       total function of its arguments that returns a cell or a named error — and, because a
       function of cells cannot call `evalExpr`, that none of them evaluates an expression. Every
       theorem holds for every such record; the differential instantiates it at production's own
       primitives, each read through the public `evalExprInRow` on a one-node expression.
     - THE OTHER TWELVE VERBS (`other_fn`): `Project`, `GroupBy`, `Join`, `Window`, `Pivot`,
       `Unpivot`, `Sort`, `Distinct`, `Limit`, `Union`, `Intersect`, `Except` — group aggregation,
       windows, pivots, joins, the set operations — with the resolver and the param env closed
       over, as the F# closure closes over them. What is ASSUMED of them is, again, their type:
       on a well-formed frame each returns a well-formed frame or a named error (`toFrame`
       produces one by construction and `ofFrame` throws on anything else), and each evaluates
       NO expression — which is a fact about the source (`evalStep`'s twelve other arms carry no
       `ColExpr` and reach neither `evalExpr` nor `evalExprInRow`), and is what `costOf` charging
       them nothing already asserts. `work` below charges them no expression work for that reason.

   Everything else — the expression evaluator, the filter, the derive, the dispatch, the driver,
   the cost model, the two entry points — is a clause, and the theorems about EXPRESSION WORK are
   proved over the clauses, not assumed of a host.

   WHAT IS PROVED, over any cell primitives, any twelve-verb evaluator, any param environment, any
   pipeline and any well-formed input frame:

     - `eval_total` — the counted evaluator returns `Ok` exactly when every step succeeded along
       the walk, and then the count is the walk's cost; otherwise it returns the FIRST failing
       step's own error, verbatim. The driver invents no refusal: there is no case in `go` that
       produces an `Error` a step did not. Termination is structural on the pipeline, and inside
       a step structural on the expression and on the rows, all checked by the prover; the one
       partial operation on the modelled path — `List.item i row` in the `Col` arm — is in range
       on a well-formed frame by refinement, and so are `evalDerive`'s `List.map2` lengths.
       `over_limit_not_refused` is the corollary that names the finding below.
     - `budget_monotone` — the count is monotone in the pipeline PREFIX: if `p ++ q` evaluates to
       `Ok (_, m')` then `p` evaluates to `Ok (_, m)` with `m <= m'` (`go_app` is the split —
       the fold over a concatenation is the fold over the prefix continued over the suffix from
       the prefix's frame and count — and `go_count_ge` that a count never goes down).
     - `visits_le_nodes` — ONE row's evaluation of an expression makes at most as many `evalExpr`
       invocations as the expression has nodes. `expr_visits` counts the invocations `eval_expr`
       makes on that row, itself included, following every short circuit exactly as the
       evaluator takes it (a `Binary` whose left operand fails never evaluates its right; a
       `Coalesce` stops at the first non-null; a `Case` stops at the first true `when` and reads
       its `else` only when none was; an `InList` stops at the first match, and reads no item
       when its subject is null; an `ApplyFn` stops at the first failing argument). The lemma is
       a mutual induction over the expression and its three list walks, and it is what "the
       evaluator is structural and visits a node at most once" MEANS, proved rather than said.
     - `rows_visits_le` — over a step's rows, the invocations the step makes (stopping at the
       first row that fails, as `evalFilter` and `evalDerive` do) are at most rows times nodes.
     - `work_bounded` — the §21.8 expression-node limit, taken as a HYPOTHESIS on the pipeline
       (`within_limit`: every expression a `Filter` or a `Derive` carries has at most
       `Limits.max_expr_nodes` nodes), bounds the expression work the count stands for: the
       `evalExpr` invocations the walk makes (`work` — the visits at each charged step, read off
       the concrete evaluator through `rows_visits`) are at most the count times the limit. This
       is what the count MEANS under the format's bound. For the two expression-evaluating steps
       it is UNCONDITIONAL on any parameter: `work` is computed by the modelled evaluator and the
       bound is proved of it. For the twelve other steps it rests on the assumption named above —
       that they evaluate no expression — which is the same assumption `costOf` makes in charging
       them nothing, and the only place a parameter enters this theorem.
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

   WHAT IS NOT CLAIMED. Anything about a cell primitive beyond its type: what `Add` does to two
   floats, what `Cast` accepts, when `compareCells` says `None` — those are the laws'
   (`Conformance.transformLaws`) and the differential's, never a theorem here. Anything about the
   twelve verbs' semantics: what a `GroupBy` emits, what a `Join` fans out to. Anything about
   `toFrame` / `ofFrame` beyond their being the transpose (the model holds one form). That the
   count is bounded by the INPUT's row count: a `Join` or a `Union` can grow a frame, and the
   count is charged where the step stands, which is what monotone-in-the-prefix says and what a
   bound in the input would not. The `…At` entry points (`substituteNow` then this driver) and
   the codec.

   HOW TO READ IT. Every definition names its F# counterpart. The module opens `Limits` for the
   one constant it takes as a premise, and nothing else; it restates `outcome` and the list
   helpers it needs, and extracts beside the other models sharing only `Prims.fs` and the
   `option` shim. Every type parameter is `Type0` — a bare `Type` is universe-polymorphic, and
   Phase 154's trial measured the universe terms keeping a seven-parameter draft from finishing
   in 720s where `Type0` finished in seconds; an F# type parameter is `Type0` anyway.

   Apache-2.0, like everything beside it.
*)
module Pipeline

open Limits

(* ======================================================================================
   0. The helpers, self-contained, each naming the FSharp.Core function it stands for.
   ====================================================================================== *)

(* F#: `Result<'a, 'e>`. *)
type outcome (a e:Type0) =
  | Ok    : a -> outcome a e
  | Error : e -> outcome a e

(* F#: `Result.map`. *)
let result_map (#a #b #e:Type0) (f:a -> b) (r:outcome a e) : Tot (outcome b e) =
  match r with
  | Ok x -> Ok (f x)
  | Error err -> Error err

(* F#: `Result.bind`. *)
let result_bind (#a #b #e:Type0) (r:outcome a e) (f:a -> outcome b e) : Tot (outcome b e) =
  match r with
  | Ok x -> f x
  | Error err -> Error err

(* F#: `List.length`. *)
let rec len (#a:Type0) (l:list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

(* F#: `@`. *)
let rec app (#a:Type0) (l m:list a) : Tot (list a) =
  match l with
  | [] -> m
  | h :: t -> h :: app t m

(* F#: `List.item i l`, at an index the caller has proved in range — production's `List.item`
   throws past the end, so the refinement is exactly the obligation the model has to discharge,
   and the `Col` arm below discharges it from the frame's well-formedness. *)
let rec nth (#a:Type0) (l:list a) (i:nat{i < len l}) : Tot a (decreases l) =
  match l with
  | x :: t -> if i = 0 then x else nth t (i - 1)

(* F#: `l |> List.mapi (fun j x -> if j = i then v else x)` — `evalDerive`'s in-place replace. *)
let rec set_at (#a:Type0) (i:nat) (v:a) (l:list a) : Tot (list a) (decreases l) =
  match l with
  | [] -> []
  | x :: t -> if i = 0 then v :: t else x :: set_at (i - 1) v t

(* F#: `cols |> List.map fst` — the private `available`, and `env |> Map.toList |> List.map fst`. *)
let rec names (#b:Type0) (l:list (string & b)) : Tot (list string) =
  match l with
  | [] -> []
  | (n, _) :: t -> n :: names t

(* F#: `Map.tryFind name env`, over the map as the association list `Map.toList` renders it —
   the standing sets-and-maps-are-lists bridge. *)
let rec assoc (#b:Type0) (name:string) (l:list (string & b)) : Tot (option b) =
  match l with
  | [] -> None
  | (n, v) :: t -> if n = name then Some v else assoc name t

(* F#: `cols |> List.tryFindIndex (fun (n, _) -> n = name)` — the private `colIndex`. The
   refinement carries the fact every caller needs: a found index is inside the schema. *)
let rec index_of (#b:Type0) (name:string) (l:list (string & b))
  : Tot (o:option nat{match o with Some i -> i < len l | None -> True}) =
  match l with
  | [] -> None
  | (n, _) :: t ->
    if n = name then Some 0
    else (match index_of name t with Some i -> Some (i + 1) | None -> None)

let rec len_app (#a:Type0) (l m:list a) : Lemma (ensures len (app l m) == len l + len m) =
  match l with
  | [] -> ()
  | _ :: t -> len_app t m

let rec len_set_at (#a:Type0) (i:nat) (v:a) (l:list a)
  : Lemma (ensures len (set_at i v l) == len l) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> if i = 0 then () else len_set_at (i - 1) v t

(* ======================================================================================
   1. The vocabulary — every closed type a `ColExpr` or a `Transform` carries, restated so the
      algebras below are total over the alphabet the F# compiler closes. `Column.fs` owns
      `Cell` / `ColumnType` / `AggFn` / `DataSource`; `DataFrame.fs` the rest and `EvalError`.
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
   the evaluator's arms read a cell only for `Bool` and `Null` (the two `Not` reads, the `Filter`
   keep, the `Coalesce` / `InList` / `IsNull` null tests), every other read is a primitive's. *)
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
type slot (a:Type0) =
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

(* F#: `EvalError`, all nine cases, in the DU's order. The evaluator's own arms raise four of them
   (`UnknownColumn`, `TypeError`, `UnboundParam`, `UnpinnedClock`); the rest are the primitives'
   and the twelve verbs' to raise, and the driver threads whichever it is handed. *)
type eval_error =
  | UnknownColumn    : string -> list string -> eval_error
  | TypeError        : string -> eval_error
  | AggError         : string -> eval_error
  | JoinError        : string -> eval_error
  | ArityError       : string -> int -> int -> eval_error
  | UnresolvedSource : string -> eval_error
  | OverflowError    : string -> eval_error
  | UnboundParam     : string -> list string -> eval_error
  | UnpinnedClock    : now_grain -> eval_error

(* F#: `Map<string, Cell>` — the param environment, as the association list `Map.toList` renders
   it (sorted by key, keys distinct), which is exactly what `Map.tryFind` and the `UnboundParam`
   listing read of it. *)
type param_env = list (string & cell)

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
   3. The cell primitives the host supplies, and `evalExpr` clause for clause.
   ====================================================================================== *)

(* THE FIRST PARAMETER. The primitives `evalExpr` calls on cells, each naming its F# source. A
   record of functions rather than four parameters, so that the differential hands the model
   production's own in one value. *)
noeq type prims = {
  (* `Binary`'s dispatch — `arith` / `comparison` / `logical` / `stringPred` by operator class,
     as the arm's `match op with …` glues them. *)
  binary    : bin_op -> cell -> cell -> outcome cell eval_error;
  (* `castCell`. *)
  cast_cell : column_type -> cell -> outcome cell eval_error;
  (* `applyScalar`. *)
  apply_fn  : scalar_fn -> list cell -> outcome cell eval_error;
  (* `compareCells` — `InList`'s membership test reads `Some 0` / `Some _` / `None` of it. *)
  compare   : cell -> cell -> option int;
}

(* A row that fits its schema: the one fact `List.item` needs. *)
type row_of (n:nat) = r:list cell{len r = n}

(* F#: `evalExpr env cols row e`, clause for clause and in evaluation order. `Case`'s loop returns
   `None` where production's `go []` falls through to the `else` expression, so the fall-through
   is taken HERE, in the arm, and the loop recurses only over the list — the same evaluation, in
   the same order. `evalArgs` accumulates and reverses; `eval_args` builds the same list. *)
let rec eval_expr (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (x:col_expr)
  : Tot (outcome cell eval_error) (decreases x) =
  match x with
  | Col name ->
    (match index_of name cols with
     | Some i -> Ok (nth row i)
     | None -> Error (UnknownColumn name (names cols)))
  | Lit c -> Ok c
  | Param name ->
    (match assoc name env with
     | Some c -> Ok c
     | None -> Error (UnboundParam name (names env)))
  | Binary op a b ->
    (match eval_expr pr env cols row a with
     | Error err -> Error err
     | Ok av ->
       (match eval_expr pr env cols row b with
        | Error err -> Error err
        | Ok bv -> pr.binary op av bv))
  | Not inner ->
    (match eval_expr pr env cols row inner with
     | Error err -> Error err
     | Ok (Bool b) -> Ok (Bool (not b))
     | Ok Null -> Ok Null
     | Ok _ -> Error (TypeError "not of a non-bool"))
  | Coalesce xs -> eval_coalesce pr env cols row xs
  | Case cases els ->
    (match eval_case pr env cols row cases with
     | Some r -> r
     | None -> eval_expr pr env cols row els)
  | Cast ty inner ->
    (match eval_expr pr env cols row inner with
     | Error err -> Error err
     | Ok v -> pr.cast_cell ty v)
  | InList subject items ->
    (match eval_expr pr env cols row subject with
     | Error err -> Error err
     | Ok Null -> Ok Null
     | Ok sv -> eval_in pr env cols row sv false items)
  | IsNull inner ->
    (match eval_expr pr env cols row inner with
     | Error err -> Error err
     | Ok Null -> Ok (Bool true)
     | Ok _ -> Ok (Bool false))
  (* A list param resolves by substitution BEFORE evaluation; one that reaches here is unbound. *)
  | InParam _ name -> Error (UnboundParam name (names env))
  (* A `now` resolves against a pinned clock BEFORE evaluation; one that reaches here has none. *)
  | Now grain -> Error (UnpinnedClock grain)
  | ApplyFn fn args ->
    (match eval_args pr env cols row args with
     | Error err -> Error err
     | Ok vs -> pr.apply_fn fn vs)

(* `Coalesce`'s `go`: the first non-null value, `Null` when every one is null. *)
and eval_coalesce (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (xs:list col_expr)
  : Tot (outcome cell eval_error) (decreases xs) =
  match xs with
  | [] -> Ok Null
  | x :: rest ->
    (match eval_expr pr env cols row x with
     | Error err -> Error err
     | Ok Null -> eval_coalesce pr env cols row rest
     | Ok c -> Ok c)

(* `Case`'s `go`, up to the fall-through: `None` is production's `go [] -> evalExpr elseExpr`. *)
and eval_case (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols))
  (cases:list (col_expr & col_expr))
  : Tot (option (outcome cell eval_error)) (decreases cases) =
  match cases with
  | [] -> None
  | (when_e, then_e) :: rest ->
    (match eval_expr pr env cols row when_e with
     | Error err -> Some (Error err)
     | Ok (Bool true) -> Some (eval_expr pr env cols row then_e)
     | Ok _ -> eval_case pr env cols row rest)

(* `InList`'s `go sawNull`: SQL three-valued membership — any equal is true; no match having seen
   a null is null. *)
and eval_in (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (sv:cell)
  (saw_null:bool) (items:list col_expr)
  : Tot (outcome cell eval_error) (decreases items) =
  match items with
  | [] -> Ok (if saw_null then Null else Bool false)
  | it :: rest ->
    (match eval_expr pr env cols row it with
     | Error err -> Error err
     | Ok Null -> eval_in pr env cols row sv true rest
     | Ok iv ->
       (match pr.compare sv iv with
        | Some 0 -> Ok (Bool true)
        | Some _ -> eval_in pr env cols row sv saw_null rest
        | None -> Error (TypeError "in: comparison between incompatible types")))

(* `ApplyFn`'s `evalArgs`: every argument, left to right, the first error wins. *)
and eval_args (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (args:list col_expr)
  : Tot (outcome (list cell) eval_error) (decreases args) =
  match args with
  | [] -> Ok []
  | a :: rest ->
    (match eval_expr pr env cols row a with
     | Error err -> Error err
     | Ok v ->
       (match eval_args pr env cols row rest with
        | Error err -> Error err
        | Ok vs -> Ok (v :: vs)))

(* ======================================================================================
   4. The frame's well-formedness, `evalFilter`, `evalDerive`, and `evalStep`'s dispatch.
   ====================================================================================== *)

(* Every row as wide as `n`. *)
let rec all_width (n:nat) (rows:list (list cell)) : Tot bool =
  match rows with
  | [] -> true
  | r :: t -> len r = n && all_width n t

(* A well-formed frame: every row as wide as the schema. `toFrame` produces one by construction
   (it pads short columns with `Null`), and `ofFrame` throws on anything else. *)
let wf (f:frame) : Tot bool = all_width (len f.cols) f.rows

(* A well-formed frame, as a type: what every step takes and every step returns. *)
type wframe = f:frame{wf f}

let rows_ok (n:nat) (r:outcome (list (list cell)) eval_error) : Tot bool =
  match r with
  | Ok rs -> all_width n rs
  | Error _ -> true

(* F#: `evalFilter env cols rows pred` — a row survives on `Ok (Bool true)`, any other `Ok` drops
   it, the first error wins. Production accumulates and reverses; this is the same list. *)
let rec filter_rows (pr:prims) (env:param_env) (cols:schema)
  (rows:list (list cell){all_width (len cols) rows}) (pred:col_expr)
  : Tot (r:outcome (list (list cell)) eval_error{rows_ok (len cols) r}) (decreases rows) =
  match rows with
  | [] -> Ok []
  | r :: rest ->
    (match eval_expr pr env cols r pred with
     | Error err -> Error err
     | Ok (Bool true) ->
       (match filter_rows pr env cols rest pred with
        | Error err -> Error err
        | Ok rs -> Ok (r :: rs))
     | Ok _ -> filter_rows pr env cols rest pred)

(* `evalDerive`'s `go`: the expression once per row, the first error wins; one cell per row. *)
let rec derive_cells (pr:prims) (env:param_env) (cols:schema)
  (rows:list (list cell){all_width (len cols) rows}) (x:col_expr)
  : Tot (r:outcome (list cell) eval_error{match r with Ok vs -> len vs = len rows | Error _ -> true})
        (decreases rows) =
  match rows with
  | [] -> Ok []
  | r :: rest ->
    (match eval_expr pr env cols r x with
     | Error err -> Error err
     | Ok v ->
       (match derive_cells pr env cols rest x with
        | Error err -> Error err
        | Ok vs -> Ok (v :: vs)))

(* F#: `Cell.typeOf` — the type a present cell carries, `None` for `Null`. *)
let type_of (c:cell) : Tot (option column_type) =
  match c with
  | Int _ -> Some IntType
  | Float _ -> Some FloatType
  | Bool _ -> Some BoolType
  | Str _ -> Some StringType
  | Date _ -> Some DateType
  | Timestamp _ -> Some TimestampType
  | Null -> None

(* F#: `cells |> List.tryPick Cell.typeOf`. *)
let rec first_type (cells:list cell) : Tot (option column_type) =
  match cells with
  | [] -> None
  | v :: t -> (match type_of v with Some ty -> Some ty | None -> first_type t)

(* F#: `inferType` — `… |> Option.defaultValue StringType`. *)
let infer_type (cells:list cell) : Tot column_type =
  match first_type cells with
  | Some ty -> ty
  | None -> StringType

(* F#: `f.Cols |> List.mapi (fun j (n, t) -> if j = i then n, ty else n, t)`. *)
let rec retype_at (i:nat) (ty:column_type) (cols:schema) : Tot schema (decreases cols) =
  match cols with
  | [] -> []
  | (n, t) :: rest -> if i = 0 then (n, ty) :: rest else (n, t) :: retype_at (i - 1) ty rest

(* F#: `List.map2 (fun row c -> row |> List.mapi (fun j cell -> if j = i then c else cell))`. The
   refinement is `List.map2`'s own precondition (it throws on unequal lengths). *)
let rec zip_replace (i:nat) (rows:list (list cell)) (cells:list cell{len cells = len rows})
  : Tot (list (list cell)) (decreases rows) =
  match rows, cells with
  | [], [] -> []
  | r :: rt, v :: vt -> set_at i v r :: zip_replace i rt vt

(* F#: `List.map2 (fun row c -> row @ [ c ])`. *)
let rec zip_append (rows:list (list cell)) (cells:list cell{len cells = len rows})
  : Tot (list (list cell)) (decreases rows) =
  match rows, cells with
  | [], [] -> []
  | r :: rt, v :: vt -> app r [v] :: zip_append rt vt

let rec len_retype_at (i:nat) (ty:column_type) (cols:schema)
  : Lemma (ensures len (retype_at i ty cols) == len cols) (decreases cols) =
  match cols with
  | [] -> ()
  | _ :: rest -> if i = 0 then () else len_retype_at (i - 1) ty rest

let rec width_replace (n:nat) (i:nat) (rows:list (list cell)) (cells:list cell{len cells = len rows})
  : Lemma (requires all_width n rows) (ensures all_width n (zip_replace i rows cells))
          (decreases rows) =
  match rows, cells with
  | [], [] -> ()
  | r :: rt, v :: vt -> len_set_at i v r; width_replace n i rt vt

let rec width_append (n:nat) (rows:list (list cell)) (cells:list cell{len cells = len rows})
  : Lemma (requires all_width n rows) (ensures all_width (n + 1) (zip_append rows cells))
          (decreases rows) =
  match rows, cells with
  | [], [] -> ()
  | r :: rt, v :: vt -> len_app r [v]; width_append n rt vt

(* F#: `evalDerive env f name expr` — the expression once per row, the column's type inferred
   from the cells, then replace the named column in place or append it. *)
let eval_derive (pr:prims) (env:param_env) (f:wframe) (name:string) (x:col_expr)
  : Tot (outcome wframe eval_error) =
  match derive_cells pr env f.cols f.rows x with
  | Error err -> Error err
  | Ok cells ->
    let ty = infer_type cells in
    (match index_of name f.cols with
     | Some i ->
       len_retype_at i ty f.cols;
       width_replace (len f.cols) i f.rows cells;
       Ok ({ cols = retype_at i ty f.cols; rows = zip_replace i f.rows cells })
     | None ->
       len_app f.cols [(name, ty)];
       width_append (len f.cols) f.rows cells;
       Ok ({ cols = app f.cols [(name, ty)]; rows = zip_append f.rows cells }))

(* THE SECOND PARAMETER. `evalStep resolve env` restricted to the twelve verbs that evaluate no
   expression, the resolver and the env closed over as the F# closure closes over them. Its TYPE
   is the whole of its contract: a well-formed frame in, a well-formed frame or a named error out. *)
type other_fn = wframe -> transform -> outcome wframe eval_error

(* F#: `evalStep resolve env f t` — `Filter` and `Derive` to the two evaluators above, every
   other verb to its primitive. *)
let eval_step (pr:prims) (other:other_fn) (env:param_env) (f:wframe) (t:transform)
  : Tot (outcome wframe eval_error) =
  match t with
  | Filter pred ->
    (match filter_rows pr env f.cols f.rows pred with
     | Error err -> Error err
     | Ok rs -> Ok ({ cols = f.cols; rows = rs }))
  | Derive name x -> eval_derive pr env f name x
  | _ -> other f t

(* ======================================================================================
   5. The driver — `evalPipelineWithInEnvCounted`'s loop and the two entry points:

        let rec go f evaluated =
            function
            | [] -> Ok(ofFrame f, evaluated)
            | step :: rest ->
                let cost = costOf f step
                evalStep resolve env f step
                |> Result.bind (fun f' -> go f' (evaluated + cost) rest)
        go (toFrame input) 0 pipeline

      The loop is stated over ANY step evaluator (`step_fn`) — the lemmas about counting are
      about the fold and hold for every one — and the entry points fix it at `eval_step`, as
      production does. `toFrame` / `ofFrame` are the identity here because the model holds the
      row-major form.
   ====================================================================================== *)

(* A step evaluator of the driver's shape: `eval_step pr other env` is one. *)
type step_fn (e:Type0) = wframe -> transform -> outcome wframe e

(* F#: `go`. *)
let rec go (#e:Type0) (step:step_fn e) (f:wframe) (evaluated:nat) (p:list transform)
  : Tot (outcome (wframe & nat) e) (decreases p) =
  match p with
  | [] -> Ok (f, evaluated)
  | s :: rest ->
    let cost = cost_of f s in
    result_bind (step f s) (fun f' -> go step f' (evaluated + cost) rest)

(* F#: `evalPipelineWithInEnvCounted resolve env pipeline input`. *)
let eval_counted (pr:prims) (other:other_fn) (env:param_env) (p:list transform) (input:wframe)
  : Tot (outcome (wframe & nat) eval_error) =
  go (eval_step pr other env) input 0 p

(* F#: `evalPipelineWithInEnv resolve env pipeline input` —
   `evalPipelineWithInEnvCounted resolve env pipeline input |> Result.map fst`. *)
let eval_uncounted (pr:prims) (other:other_fn) (env:param_env) (p:list transform) (input:wframe)
  : Tot (outcome wframe eval_error) =
  result_map fst (eval_counted pr other env p input)

(* ======================================================================================
   6. The walk, read off the driver: whether every step succeeds, which error the first failing
      step raises, and what the walk costs. These are the theorems' vocabulary, not the source's
      — production carries the same information inside `go`'s recursion.
   ====================================================================================== *)

(* Every step along the walk from `f` succeeds. *)
let rec walk_ok (#e:Type0) (step:step_fn e) (f:wframe) (p:list transform) : Tot bool (decreases p) =
  match p with
  | [] -> true
  | s :: rest ->
    (match step f s with
     | Ok f' -> walk_ok step f' rest
     | Error _ -> false)

(* The error of the first failing step along the walk, if any. *)
let rec first_error (#e:Type0) (step:step_fn e) (f:wframe) (p:list transform)
  : Tot (option e) (decreases p) =
  match p with
  | [] -> None
  | s :: rest ->
    (match step f s with
     | Ok f' -> first_error step f' rest
     | Error err -> Some err)

(* The cost the walk accumulates, charged step by step where each step stands. *)
let rec cost (#e:Type0) (step:step_fn e) (f:wframe) (p:list transform) : Tot nat (decreases p) =
  match p with
  | [] -> 0
  | s :: rest ->
    (match step f s with
     | Ok f' -> cost_of f s + cost step f' rest
     | Error _ -> cost_of f s)

(* ======================================================================================
   7. Totality — `eval_total`.
   ====================================================================================== *)

(* The driver returns `Ok` exactly when the walk succeeds. *)
let rec go_ok_iff (#e:Type0) (step:step_fn e) (f:wframe) (n:nat) (p:list transform)
  : Lemma (ensures Ok? (go step f n p) <==> walk_ok step f p) (decreases p) =
  match p with
  | [] -> ()
  | s :: rest ->
    (match step f s with
     | Ok f' -> go_ok_iff step f' (n + cost_of f s) rest
     | Error _ -> ())

(* On a successful walk the count is the walk's cost over the count carried in. *)
let rec go_count (#e:Type0) (step:step_fn e) (f:wframe) (n:nat) (p:list transform)
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
let rec go_error (#e:Type0) (step:step_fn e) (f:wframe) (n:nat) (p:list transform)
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

(* THE THEOREM. Every pipeline over every well-formed input reaches `Ok` with a well-formed frame
   and the walk's cost, or the first failing step's named error — and nothing else. The driver
   adds no refusal of its own, and neither does the expression evaluator: every error it returns
   is one of its arms' or a primitive's. *)
let eval_total (pr:prims) (other:other_fn) (env:param_env) (p:list transform) (input:wframe)
  : Lemma (ensures
      (walk_ok (eval_step pr other env) input p /\
       (match eval_counted pr other env p input with
        | Ok (_, m) -> m == cost (eval_step pr other env) input p
        | Error _ -> False))
      \/
      (not (walk_ok (eval_step pr other env) input p) /\
       (match eval_counted pr other env p input, first_error (eval_step pr other env) input p with
        | Error err, Some err' -> err == err'
        | _ -> False))) =
  if walk_ok (eval_step pr other env) input p
  then go_count (eval_step pr other env) input 0 p
  else go_error (eval_step pr other env) input 0 p

(* ======================================================================================
   8. Budget monotonicity — `budget_monotone`.
   ====================================================================================== *)

(* The fold over a concatenation is the fold over the prefix, continued over the suffix from
   the prefix's frame and count. *)
let rec go_app (#e:Type0) (step:step_fn e) (f:wframe) (n:nat) (p q:list transform)
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
let rec go_count_ge (#e:Type0) (step:step_fn e) (f:wframe) (n:nat) (p:list transform)
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
let budget_monotone (pr:prims) (other:other_fn) (env:param_env) (input:wframe) (p q:list transform)
  : Lemma (requires Ok? (eval_counted pr other env (app p q) input))
          (ensures (match eval_counted pr other env p input, eval_counted pr other env (app p q) input with
                    | Ok (_, m), Ok (_, m') -> m <= m'
                    | _ -> False)) =
  go_app (eval_step pr other env) input 0 p q;
  (match go (eval_step pr other env) input 0 p with
   | Ok (f', m) -> go_count_ge (eval_step pr other env) f' m q
   | Error _ -> ())

(* ======================================================================================
   9. The §21.8 premise, and what the count means under it — `work_bounded`.

      `Limits.max_expr_nodes` bounds "`ColExpr` nodes in ONE expression". Nothing under `src/`
      counts them (the finding); here the count is a definition and the bound a hypothesis. The
      WORK the count stands for is not a definition any more: it is the number of `evalExpr`
      invocations the evaluator modelled in section 3 makes, read off it (`expr_visits`), and
      the bound on it is proved (`visits_le_nodes`, `rows_visits_le`).
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
   only ones that reach `evalExpr`; every other verb carries no `ColExpr`. *)
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

(* The `evalExpr` invocations evaluating `x` on `row` makes, itself included, following every
   short circuit exactly as `eval_expr` takes it: an operand after a failure is never reached, a
   `Coalesce` stops at its first non-null, a `Case` at its first true `when` (its `else` is read
   only when none was), an `InList` at its first match (and reads no item when its subject is
   null), an `ApplyFn` at its first failing argument. `InParam` and `Now` refuse without reading
   their payload, as production does. *)
let rec expr_visits (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (x:col_expr)
  : Tot nat (decreases x) =
  match x with
  | Col _ -> 1
  | Lit _ -> 1
  | Param _ -> 1
  | InParam _ _ -> 1
  | Now _ -> 1
  | Binary _ a b ->
    1 + expr_visits pr env cols row a
      + (match eval_expr pr env cols row a with
         | Ok _ -> expr_visits pr env cols row b
         | Error _ -> 0)
  | Not a -> 1 + expr_visits pr env cols row a
  | Cast _ a -> 1 + expr_visits pr env cols row a
  | IsNull a -> 1 + expr_visits pr env cols row a
  | Coalesce xs -> 1 + coalesce_visits pr env cols row xs
  | Case cases els ->
    1 + case_visits pr env cols row cases
      + (match eval_case pr env cols row cases with
         | Some _ -> 0
         | None -> expr_visits pr env cols row els)
  | InList subject items ->
    1 + expr_visits pr env cols row subject
      + (match eval_expr pr env cols row subject with
         | Error _ -> 0
         | Ok Null -> 0
         | Ok sv -> in_visits pr env cols row sv items)
  | ApplyFn _ args -> 1 + args_visits pr env cols row args

and coalesce_visits (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (xs:list col_expr)
  : Tot nat (decreases xs) =
  match xs with
  | [] -> 0
  | x :: rest ->
    expr_visits pr env cols row x
    + (match eval_expr pr env cols row x with
       | Ok Null -> coalesce_visits pr env cols row rest
       | _ -> 0)

and case_visits (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols))
  (cases:list (col_expr & col_expr))
  : Tot nat (decreases cases) =
  match cases with
  | [] -> 0
  | (when_e, then_e) :: rest ->
    expr_visits pr env cols row when_e
    + (match eval_expr pr env cols row when_e with
       | Error _ -> 0
       | Ok (Bool true) -> expr_visits pr env cols row then_e
       | Ok _ -> case_visits pr env cols row rest)

(* `sawNull` steers `eval_in`'s answer, not its walk, so the count does not carry it. *)
and in_visits (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (sv:cell)
  (items:list col_expr)
  : Tot nat (decreases items) =
  match items with
  | [] -> 0
  | it :: rest ->
    expr_visits pr env cols row it
    + (match eval_expr pr env cols row it with
       | Error _ -> 0
       | Ok Null -> in_visits pr env cols row sv rest
       | Ok iv ->
         (match pr.compare sv iv with
          | Some 0 -> 0
          | Some _ -> in_visits pr env cols row sv rest
          | None -> 0))

and args_visits (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (args:list col_expr)
  : Tot nat (decreases args) =
  match args with
  | [] -> 0
  | a :: rest ->
    expr_visits pr env cols row a
    + (match eval_expr pr env cols row a with
       | Ok _ -> args_visits pr env cols row rest
       | Error _ -> 0)

(* THE LEMMA. One row's evaluation of `x` makes at most `expr_nodes x` invocations: the evaluator
   is structural on the expression and reads a node at most once. Over any primitives — none of
   them can re-enter the evaluator — and any row. *)
let rec visits_le_nodes (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (x:col_expr)
  : Lemma (ensures expr_visits pr env cols row x <= expr_nodes x) (decreases x) =
  match x with
  | Binary _ a b -> visits_le_nodes pr env cols row a; visits_le_nodes pr env cols row b
  | Not a -> visits_le_nodes pr env cols row a
  | Cast _ a -> visits_le_nodes pr env cols row a
  | IsNull a -> visits_le_nodes pr env cols row a
  | Coalesce xs -> coalesce_le pr env cols row xs
  | ApplyFn _ args -> args_le pr env cols row args
  | Case cases els -> case_le pr env cols row cases; visits_le_nodes pr env cols row els
  | InList subject items ->
    visits_le_nodes pr env cols row subject;
    (match eval_expr pr env cols row subject with
     | Ok sv -> in_le pr env cols row sv items
     | Error _ -> ())
  | _ -> ()

and coalesce_le (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (xs:list col_expr)
  : Lemma (ensures coalesce_visits pr env cols row xs <= exprs_nodes xs) (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest -> visits_le_nodes pr env cols row x; coalesce_le pr env cols row rest

and case_le (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols))
  (cases:list (col_expr & col_expr))
  : Lemma (ensures case_visits pr env cols row cases <= pairs_nodes cases) (decreases cases) =
  match cases with
  | [] -> ()
  | (when_e, then_e) :: rest ->
    visits_le_nodes pr env cols row when_e;
    visits_le_nodes pr env cols row then_e;
    case_le pr env cols row rest

and in_le (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (sv:cell) (items:list col_expr)
  : Lemma (ensures in_visits pr env cols row sv items <= exprs_nodes items) (decreases items) =
  match items with
  | [] -> ()
  | it :: rest -> visits_le_nodes pr env cols row it; in_le pr env cols row sv rest

and args_le (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (args:list col_expr)
  : Lemma (ensures args_visits pr env cols row args <= exprs_nodes args) (decreases args) =
  match args with
  | [] -> ()
  | a :: rest -> visits_le_nodes pr env cols row a; args_le pr env cols row rest

(* Over a step's rows: the invocations `evalFilter` / `evalDerive` make, evaluating the expression
   once per row and stopping at the first row that fails (both loops do). *)
let rec rows_visits (pr:prims) (env:param_env) (cols:schema)
  (rows:list (list cell){all_width (len cols) rows}) (x:col_expr)
  : Tot nat (decreases rows) =
  match rows with
  | [] -> 0
  | r :: rest ->
    expr_visits pr env cols r x
    + (match eval_expr pr env cols r x with
       | Ok _ -> rows_visits pr env cols rest x
       | Error _ -> 0)

(* THE LEMMA. A step's invocations are at most its rows times its expression's nodes. *)
let rec rows_visits_le (pr:prims) (env:param_env) (cols:schema)
  (rows:list (list cell){all_width (len cols) rows}) (x:col_expr)
  : Lemma (ensures rows_visits pr env cols rows x <= len rows * expr_nodes x) (decreases rows) =
  match rows with
  | [] -> ()
  | r :: rest ->
    visits_le_nodes pr env cols r x;
    rows_visits_le pr env cols rest x;
    FStar.Math.Lemmas.distributivity_add_left 1 (len rest) (expr_nodes x)

(* The expression work one step does: the invocations its evaluator makes over its rows, read off
   the concrete evaluator. The twelve other verbs evaluate no expression — the assumption the
   header names, and the one `costOf` already makes in charging them nothing. *)
let step_work (pr:prims) (env:param_env) (f:wframe) (s:transform) : Tot nat =
  match s with
  | Filter e -> rows_visits pr env f.cols f.rows e
  | Derive _ e -> rows_visits pr env f.cols f.rows e
  | _ -> 0

(* The expression work the walk does, charged where each step stands, as `cost` is. *)
let rec work (pr:prims) (other:other_fn) (env:param_env) (f:wframe) (p:list transform)
  : Tot nat (decreases p) =
  match p with
  | [] -> 0
  | s :: rest ->
    (match eval_step pr other env f s with
     | Ok f' -> step_work pr env f s + work pr other env f' rest
     | Error _ -> step_work pr env f s)

(* One step within the limit does at most its charge times the limit of work. *)
let step_work_bounded (pr:prims) (env:param_env) (f:wframe) (s:transform)
  : Lemma (requires all_within (step_exprs s))
          (ensures step_work pr env f s <= cost_of f s * max_expr_nodes) =
  match s with
  | Filter e ->
    rows_visits_le pr env f.cols f.rows e;
    FStar.Math.Lemmas.lemma_mult_le_left (len f.rows) (expr_nodes e) max_expr_nodes
  | Derive _ e ->
    rows_visits_le pr env f.cols f.rows e;
    FStar.Math.Lemmas.lemma_mult_le_left (len f.rows) (expr_nodes e) max_expr_nodes
  | _ -> ()

(* THE THEOREM. Under the §21.8 premise the expression work a walk does — the `evalExpr`
   invocations the modelled evaluator makes — is at most the count it reports times the limit:
   the count bounds the work, with the format's own constant. Unconditional for the two
   expression-evaluating steps; for the twelve others it rests on their evaluating no expression. *)
let rec work_bounded (pr:prims) (other:other_fn) (env:param_env) (f:wframe) (p:list transform)
  : Lemma (requires within_limit p)
          (ensures work pr other env f p <= cost (eval_step pr other env) f p * max_expr_nodes)
          (decreases p) =
  match p with
  | [] -> ()
  | s :: rest ->
    step_work_bounded pr env f s;
    (match eval_step pr other env f s with
     | Ok f' ->
       work_bounded pr other env f' rest;
       FStar.Math.Lemmas.distributivity_add_left (cost_of f s) (cost (eval_step pr other env) f' rest) max_expr_nodes
     | Error _ -> ())

(* THE FINDING, as a theorem. A pipeline OUTSIDE the limit whose every step succeeds evaluates to
   `Ok`: the driver has no clause that reads the bound, so the limit is a premise a caller may
   assume and never a refusal the evaluator performs. Goes red the day `go` gains one. *)
let over_limit_not_refused (pr:prims) (other:other_fn) (env:param_env) (p:list transform) (input:wframe)
  : Lemma (requires not (within_limit p) /\ walk_ok (eval_step pr other env) input p)
          (ensures Ok? (eval_counted pr other env p input)) =
  go_ok_iff (eval_step pr other env) input 0 p

(* ======================================================================================
   10. The uncounted entry point is the counted one projected — `uncounted_is_projection`.
   ====================================================================================== *)

(* THE IDENTITY. `evalPipelineWithInEnv = evalPipelineWithInEnvCounted |> Result.map fst`. It is
   the definition, and it is stated so the ladder carries the fact the phase was chartered to
   prove as an agreement between two paths: there is one path. *)
let uncounted_is_projection (pr:prims) (other:other_fn) (env:param_env) (p:list transform) (input:wframe)
  : Lemma (ensures eval_uncounted pr other env p input ==
                   result_map fst (eval_counted pr other env p input)) = ()

(* And so the uncounted path succeeds exactly when the walk does, with the counted path's frame. *)
let uncounted_ok_iff (pr:prims) (other:other_fn) (env:param_env) (p:list transform) (input:wframe)
  : Lemma (ensures (Ok? (eval_uncounted pr other env p input) <==> walk_ok (eval_step pr other env) input p) /\
                   (match eval_uncounted pr other env p input, eval_counted pr other env p input with
                    | Ok t, Ok (t', _) -> t == t'
                    | Error err, Error err' -> err == err'
                    | _ -> False)) =
  go_ok_iff (eval_step pr other env) input 0 p

(* ======================================================================================
   11. The planner's reorder (Phase 269) — a total step's removal from a filtered row's path
       preserves the driver's result.

   `Plan.rewrite` moves a `Filter` ahead of the `Derive` it follows. On a row the filter drops,
   the derive is then never evaluated: the step is REMOVED from that row's path. The theorem
   `derive_then_filter` says when that changes nothing: the derive answers `Ok` on every row of
   the frame (the totality the verdict of section 12 decides), the filter reads no column the
   derive writes and every column it reads exists before the derive (so its answer on a row is
   the same with or without the derived cell, and so is its refusal), and the derived cells are
   strings or nulls (so the column's inferred type — read off the cells, `infer_type` — is the
   same over the kept rows as over all of them). Under those three the two orders fold to the
   same frame and the same first error, and `reorder_in_context` carries the equality to any
   pipeline the pair sits in, through `go_app`.

   Every definition here is `noextract`: the planner is not modelled as a clause of the oracle
   (its F# is held to the reference by `Conformance.plannerLaws`), and the extraction the leg
   diffs must stay what Phase 234 committed.
   ====================================================================================== *)

noextract
let rec mem (n:string) (l:list string) : Tot bool =
  match l with
  | [] -> false
  | h :: t -> h = n || mem n t

(* F#: `Planner.exprCols` — every column an expression reads, in occurrence order. *)
noextract
let rec reads (x:col_expr) : Tot (list string) (decreases x) =
  match x with
  | Col n -> [n]
  | Lit _ | Param _ | Now _ -> []
  | Binary _ a b -> app (reads a) (reads b)
  | Not a | Cast _ a | IsNull a | InParam a _ -> reads a
  | Coalesce xs | ApplyFn _ xs -> reads_list xs
  | Case cases els -> app (reads_cases cases) (reads els)
  | InList a items -> app (reads a) (reads_list items)
and reads_list (xs:list col_expr) : Tot (list string) (decreases xs) =
  match xs with
  | [] -> []
  | x :: rest -> app (reads x) (reads_list rest)
and reads_cases (cs:list (col_expr & col_expr)) : Tot (list string) (decreases cs) =
  match cs with
  | [] -> []
  | (w, t) :: rest -> app (reads w) (app (reads t) (reads_cases rest))

(* `List.tryItem` — `nth` without the range obligation, for lemmas whose statement would
   otherwise have to establish a length before it could be read. *)
noextract
let rec nth_opt (#a:Type0) (l:list a) (j:nat) : Tot (option a) (decreases l) =
  match l with
  | [] -> None
  | x :: t -> if j = 0 then Some x else nth_opt t (j - 1)

let rec nth_opt_nth (#a:Type0) (l:list a) (j:nat{j < len l})
  : Lemma (ensures nth_opt l j == Some (nth l j)) (decreases l) =
  match l with
  | x :: t -> if j = 0 then () else nth_opt_nth t (j - 1)

let rec nth_opt_set_at (#a:Type0) (i:nat) (v:a) (l:list a) (j:nat)
  : Lemma (requires j <> i) (ensures nth_opt (set_at i v l) j == nth_opt l j) (decreases l) =
  match l with
  | [] -> ()
  | x :: t -> if i = 0 then () else if j = 0 then () else nth_opt_set_at (i - 1) v t (j - 1)

let rec nth_opt_app_left (#a:Type0) (l m:list a) (j:nat)
  : Lemma (requires j < len l) (ensures nth_opt (app l m) j == nth_opt l j) (decreases l) =
  match l with
  | x :: t -> if j = 0 then () else nth_opt_app_left t m (j - 1)

(* A retype changes no name, so no index. *)
let rec index_of_retype_at (n:string) (i:nat) (ty:column_type) (cols:schema)
  : Lemma (ensures index_of n (retype_at i ty cols) == index_of n cols) (decreases cols) =
  match cols with
  | [] -> ()
  | _ :: rest -> if i = 0 then () else index_of_retype_at n (i - 1) ty rest

(* A name found before an append is found at the same index after it. *)
let rec index_of_app_found (#b:Type0) (n:string) (l m:list (string & b))
  : Lemma (requires Some? (index_of n l)) (ensures index_of n (app l m) == index_of n l) (decreases l) =
  match l with
  | (h, _) :: t -> if h = n then () else index_of_app_found n t m

(* Two names at one index are one name. *)
let rec index_of_distinct (#b:Type0) (n m:string) (l:list (string & b)) (j:nat)
  : Lemma (requires index_of n l == Some j /\ index_of m l == Some j) (ensures n == m) (decreases l) =
  match l with
  | (h, _) :: t ->
    if h = n then ()
    else if h = m then ()
    else index_of_distinct n m t (j - 1)

(* Two rows over two schemas answer the same cell for every name in `ns`, each name found in
   both — what a predicate that reads only `ns` needs to answer the same on both. *)
noextract
let rec agrees (cols:schema) (row:list cell) (cols':schema) (row':list cell) (ns:list string) : Tot bool =
  match ns with
  | [] -> true
  | n :: rest ->
    (match index_of n cols, index_of n cols' with
     | Some j, Some j' -> nth_opt row j = nth_opt row' j' && agrees cols row cols' row' rest
     | _ -> false)

let rec agrees_app (cols:schema) (row:list cell) (cols':schema) (row':list cell) (l m:list string)
  : Lemma (requires agrees cols row cols' row' (app l m))
          (ensures agrees cols row cols' row' l /\ agrees cols row cols' row' m) (decreases l) =
  match l with
  | [] -> ()
  | _ :: rest -> agrees_app cols row cols' row' rest m

(* Every name in `ns` is not `name` and is a column of `cols`: the filter's reads, as the
   planner's rule requires them (`declineReorder`'s second and third clauses). *)
noextract
let rec closed_not (cols:schema) (name:string) (ns:list string) : Tot bool =
  match ns with
  | [] -> true
  | n :: rest -> n <> name && Some? (index_of n cols) && closed_not cols name rest

(* THE EXPRESSION LEMMA. An expression answers the same on two rows that agree on every column
   it reads — value for value, error for error, through every short circuit. *)
let rec eval_extends (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols))
  (cols':schema) (row':row_of (len cols')) (x:col_expr)
  : Lemma (requires agrees cols row cols' row' (reads x))
          (ensures eval_expr pr env cols row x == eval_expr pr env cols' row' x)
          (decreases x) =
  match x with
  | Col n ->
    (match index_of n cols, index_of n cols' with
     | Some j, Some j' -> nth_opt_nth row j; nth_opt_nth row' j'
     | _ -> ())
  | Lit _ | Param _ | Now _ | InParam _ _ -> ()
  | Binary _ a b ->
    agrees_app cols row cols' row' (reads a) (reads b);
    eval_extends pr env cols row cols' row' a;
    (match eval_expr pr env cols row a with
     | Error _ -> ()
     | Ok _ -> eval_extends pr env cols row cols' row' b)
  | Not a | Cast _ a | IsNull a -> eval_extends pr env cols row cols' row' a
  | Coalesce xs -> extends_coalesce pr env cols row cols' row' xs
  | Case cases els ->
    agrees_app cols row cols' row' (reads_cases cases) (reads els);
    extends_case pr env cols row cols' row' cases;
    (match eval_case pr env cols row cases with
     | Some _ -> ()
     | None -> eval_extends pr env cols row cols' row' els)
  | InList a items ->
    agrees_app cols row cols' row' (reads a) (reads_list items);
    eval_extends pr env cols row cols' row' a;
    (match eval_expr pr env cols row a with
     | Error _ -> ()
     | Ok Null -> ()
     | Ok sv -> extends_in pr env cols row cols' row' sv false items)
  | ApplyFn _ args -> extends_args pr env cols row cols' row' args
and extends_coalesce (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols))
  (cols':schema) (row':row_of (len cols')) (xs:list col_expr)
  : Lemma (requires agrees cols row cols' row' (reads_list xs))
          (ensures eval_coalesce pr env cols row xs == eval_coalesce pr env cols' row' xs)
          (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest ->
    agrees_app cols row cols' row' (reads x) (reads_list rest);
    eval_extends pr env cols row cols' row' x;
    (match eval_expr pr env cols row x with
     | Error _ -> ()
     | Ok Null -> extends_coalesce pr env cols row cols' row' rest
     | Ok _ -> ())
and extends_case (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols))
  (cols':schema) (row':row_of (len cols')) (cases:list (col_expr & col_expr))
  : Lemma (requires agrees cols row cols' row' (reads_cases cases))
          (ensures eval_case pr env cols row cases == eval_case pr env cols' row' cases)
          (decreases cases) =
  match cases with
  | [] -> ()
  | (w, t) :: rest ->
    agrees_app cols row cols' row' (reads w) (app (reads t) (reads_cases rest));
    agrees_app cols row cols' row' (reads t) (reads_cases rest);
    eval_extends pr env cols row cols' row' w;
    (match eval_expr pr env cols row w with
     | Error _ -> ()
     | Ok (Bool true) -> eval_extends pr env cols row cols' row' t
     | Ok _ -> extends_case pr env cols row cols' row' rest)
and extends_in (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols))
  (cols':schema) (row':row_of (len cols')) (sv:cell) (saw_null:bool) (items:list col_expr)
  : Lemma (requires agrees cols row cols' row' (reads_list items))
          (ensures eval_in pr env cols row sv saw_null items == eval_in pr env cols' row' sv saw_null items)
          (decreases items) =
  match items with
  | [] -> ()
  | it :: rest ->
    agrees_app cols row cols' row' (reads it) (reads_list rest);
    eval_extends pr env cols row cols' row' it;
    (match eval_expr pr env cols row it with
     | Error _ -> ()
     | Ok Null -> extends_in pr env cols row cols' row' sv true rest
     | Ok iv ->
       (match pr.compare sv iv with
        | Some 0 -> ()
        | Some _ -> extends_in pr env cols row cols' row' sv saw_null rest
        | None -> ()))
and extends_args (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols))
  (cols':schema) (row':row_of (len cols')) (args:list col_expr)
  : Lemma (requires agrees cols row cols' row' (reads_list args))
          (ensures eval_args pr env cols row args == eval_args pr env cols' row' args)
          (decreases args) =
  match args with
  | [] -> ()
  | a :: rest ->
    agrees_app cols row cols' row' (reads a) (reads_list rest);
    eval_extends pr env cols row cols' row' a;
    (match eval_expr pr env cols row a with
     | Error _ -> ()
     | Ok _ -> extends_args pr env cols row cols' row' rest)

(* The two ways `eval_derive` extends a row — a cell replaced in place, a cell appended — each
   agree with the row on every column the filter reads. *)
let rec agrees_replace (cols:schema) (row:list cell) (i:nat) (name:string) (ty:column_type) (v:cell) (ns:list string)
  : Lemma (requires index_of name cols == Some i /\ closed_not cols name ns)
          (ensures agrees cols row (retype_at i ty cols) (set_at i v row) ns) (decreases ns) =
  match ns with
  | [] -> ()
  | n :: rest ->
    index_of_retype_at n i ty cols;
    (match index_of n cols with
     | Some j ->
       if j = i then index_of_distinct n name cols j
       else (nth_opt_set_at i v row j; agrees_replace cols row i name ty v rest)
     | None -> ())

let rec agrees_append (cols:schema) (row:list cell{len row = len cols}) (name:string) (ty:column_type) (v:cell) (ns:list string)
  : Lemma (requires closed_not cols name ns)
          (ensures agrees cols row (app cols [(name, ty)]) (app row [v]) ns) (decreases ns) =
  match ns with
  | [] -> ()
  | n :: rest ->
    (match index_of n cols with
     | Some j ->
       index_of_app_found n cols [(name, ty)];
       nth_opt_app_left row [v] j;
       agrees_append cols row name ty v rest
     | None -> ())

(* The two ways `eval_derive` extends a row — a cell replaced in place at the column's index, a
   cell appended — as one shape, so the row lemma is stated once. *)
noextract
type ext_kind =
  | ExtReplace : nat -> ext_kind
  | ExtAppend  : ext_kind

noextract
let ext_row (k:ext_kind) (r:list cell) (v:cell) : Tot (list cell) =
  match k with
  | ExtReplace i -> set_at i v r
  | ExtAppend -> app r [v]

noextract
let ext_cols (k:ext_kind) (name:string) (ty:column_type) (cols:schema) : Tot schema =
  match k with
  | ExtReplace i -> retype_at i ty cols
  | ExtAppend -> app cols [(name, ty)]

(* The shape `eval_derive` takes for `name` over `cols`. *)
noextract
let ext_of (k:ext_kind) (name:string) (cols:schema) : Tot bool =
  match k with
  | ExtReplace i -> index_of name cols = Some i
  | ExtAppend -> None? (index_of name cols)

noextract
let rec zip_ext (k:ext_kind) (rows:list (list cell)) (cells:list cell{len cells = len rows})
  : Tot (list (list cell)) (decreases rows) =
  match rows, cells with
  | [], [] -> []
  | r :: rt, v :: vt -> ext_row k r v :: zip_ext k rt vt

let rec zip_replace_is_ext (i:nat) (rows:list (list cell)) (cells:list cell{len cells = len rows})
  : Lemma (ensures zip_replace i rows cells == zip_ext (ExtReplace i) rows cells) (decreases rows) =
  match rows, cells with
  | [], [] -> ()
  | _ :: rt, _ :: vt -> zip_replace_is_ext i rt vt

let rec zip_append_is_ext (rows:list (list cell)) (cells:list cell{len cells = len rows})
  : Lemma (ensures zip_append rows cells == zip_ext ExtAppend rows cells) (decreases rows) =
  match rows, cells with
  | [], [] -> ()
  | _ :: rt, _ :: vt -> zip_append_is_ext rt vt

let len_ext_row (k:ext_kind) (name:string) (ty:column_type) (cols:schema) (r:list cell{len r = len cols}) (v:cell)
  : Lemma (ensures len (ext_row k r v) = len (ext_cols k name ty cols)) =
  match k with
  | ExtReplace i -> len_set_at i v r; len_retype_at i ty cols
  | ExtAppend -> len_app r [v]; len_app cols [(name, ty)]

let agrees_ext (k:ext_kind) (cols:schema) (row:list cell{len row = len cols}) (name:string) (ty:column_type) (v:cell) (ns:list string)
  : Lemma (requires closed_not cols name ns /\ ext_of k name cols)
          (ensures agrees cols row (ext_cols k name ty cols) (ext_row k row v) ns) =
  match k with
  | ExtReplace i -> agrees_replace cols row i name ty v ns
  | ExtAppend -> agrees_append cols row name ty v ns

(* Every derived cell a string or a null: the one shape whose inferred column type is the same
   over any subset of the rows (`derivedColumnType` decides it statically, and nothing else). *)
noextract
let rec all_str_or_null (cells:list cell) : Tot bool =
  match cells with
  | [] -> true
  | Str _ :: t | Null :: t -> all_str_or_null t
  | _ -> false

let rec infer_str_or_null (cells:list cell)
  : Lemma (requires all_str_or_null cells) (ensures infer_type cells == StringType) =
  match cells with
  | [] -> ()
  | _ :: t -> infer_str_or_null t

(* THE ROW LEMMA. Filtering the derived rows answers what filtering the rows answers — the same
   first error, or the kept rows, each extended by the cell the derive gives it; and the derive
   over the kept rows alone answers `Ok`, with cells drawn from the cells over all of them. *)
let rec filter_ext (pr:prims) (env:param_env) (cols:schema) (k:ext_kind) (name:string) (ty:column_type)
  (p x:col_expr) (rows:list (list cell){all_width (len cols) rows}) (vs:list cell{len vs = len rows})
  : Lemma (requires closed_not cols name (reads p) /\ ext_of k name cols
                    /\ derive_cells pr env cols rows x == Ok vs
                    /\ all_width (len (ext_cols k name ty cols)) (zip_ext k rows vs))
          (ensures (match filter_rows pr env (ext_cols k name ty cols) (zip_ext k rows vs) p,
                          filter_rows pr env cols rows p with
                    | Error e, Error e' -> e == e'
                    | Ok rs', Ok rs ->
                      (match derive_cells pr env cols rs x with
                       | Ok vs' -> rs' == zip_ext k rs vs' /\ (all_str_or_null vs ==> all_str_or_null vs')
                       | Error _ -> False)
                    | _ -> False))
          (decreases rows) =
  match rows, vs with
  | [], [] -> ()
  | r :: rt, v :: vt ->
    (match eval_expr pr env cols r x with
     | Ok _ ->
       len_ext_row k name ty cols r v;
       agrees_ext k cols r name ty v (reads p);
       eval_extends pr env cols r (ext_cols k name ty cols) (ext_row k r v) p;
       (match eval_expr pr env cols r p with
        | Error _ -> ()
        | Ok _ -> filter_ext pr env cols k name ty p x rt vt)
     | Error _ -> ())

#push-options "--fuel 4 --ifuel 2 --z3rlimit 120"
(* The theorem, one extension shape at a time: the derived column replaced in place ... *)
let derive_then_filter_replace (pr:prims) (other:other_fn) (env:param_env) (f:wframe) (name:string) (x p:col_expr) (i:nat)
  : Lemma (requires (match derive_cells pr env f.cols f.rows x with
                     | Ok vs -> all_str_or_null vs
                     | Error _ -> False)
                    /\ closed_not f.cols name (reads p)
                    /\ index_of name f.cols == Some i)
          (ensures eval_uncounted pr other env [Derive name x; Filter p] f ==
                   eval_uncounted pr other env [Filter p; Derive name x] f) =
  match derive_cells pr env f.cols f.rows x with
  | Ok vs ->
    infer_str_or_null vs;
    len_retype_at i StringType f.cols;
    width_replace (len f.cols) i f.rows vs;
    zip_replace_is_ext i f.rows vs;
    let f1 : wframe = { cols = retype_at i StringType f.cols; rows = zip_replace i f.rows vs } in
    assert (eval_derive pr env f name x == Ok f1);
    assert (ext_cols (ExtReplace i) name StringType f.cols == f1.cols);
    assert (zip_ext (ExtReplace i) f.rows vs == f1.rows);
    filter_ext pr env f.cols (ExtReplace i) name StringType p x f.rows vs;
    (match filter_rows pr env f.cols f.rows p with
     | Error e -> assert (filter_rows pr env f1.cols f1.rows p == Error e)
     | Ok rs ->
       let f2 : wframe = { cols = f.cols; rows = rs } in
       assert (eval_step pr other env f (Filter p) == Ok f2);
       (match derive_cells pr env f.cols rs x with
        | Ok vs' ->
          infer_str_or_null vs';
          zip_replace_is_ext i rs vs';
          len_retype_at i StringType f.cols;
          width_replace (len f.cols) i rs vs';
          let f3 : wframe = { cols = retype_at i StringType f.cols; rows = zip_replace i rs vs' } in
          assert (eval_derive pr env f2 name x == Ok f3);
          assert (filter_rows pr env f1.cols f1.rows p == Ok f3.rows);
          assert (eval_step pr other env f1 (Filter p) == Ok f3)
        | Error _ -> ()))
  | Error _ -> ()

(* ... and appended. *)
let derive_then_filter_append (pr:prims) (other:other_fn) (env:param_env) (f:wframe) (name:string) (x p:col_expr)
  : Lemma (requires (match derive_cells pr env f.cols f.rows x with
                     | Ok vs -> all_str_or_null vs
                     | Error _ -> False)
                    /\ closed_not f.cols name (reads p)
                    /\ None? (index_of name f.cols))
          (ensures eval_uncounted pr other env [Derive name x; Filter p] f ==
                   eval_uncounted pr other env [Filter p; Derive name x] f) =
  match derive_cells pr env f.cols f.rows x with
  | Ok vs ->
    infer_str_or_null vs;
    len_app f.cols [(name, StringType)];
    width_append (len f.cols) f.rows vs;
    zip_append_is_ext f.rows vs;
    let f1 : wframe = { cols = app f.cols [(name, StringType)]; rows = zip_append f.rows vs } in
    assert (eval_derive pr env f name x == Ok f1);
    assert (ext_cols ExtAppend name StringType f.cols == f1.cols);
    assert (zip_ext ExtAppend f.rows vs == f1.rows);
    filter_ext pr env f.cols ExtAppend name StringType p x f.rows vs;
    (match filter_rows pr env f.cols f.rows p with
     | Error e -> assert (filter_rows pr env f1.cols f1.rows p == Error e)
     | Ok rs ->
       let f2 : wframe = { cols = f.cols; rows = rs } in
       assert (eval_step pr other env f (Filter p) == Ok f2);
       (match derive_cells pr env f.cols rs x with
        | Ok vs' ->
          infer_str_or_null vs';
          zip_append_is_ext rs vs';
          width_append (len f.cols) rs vs';
          let f3 : wframe = { cols = app f.cols [(name, StringType)]; rows = zip_append rs vs' } in
          assert (eval_derive pr env f2 name x == Ok f3);
          assert (filter_rows pr env f1.cols f1.rows p == Ok f3.rows);
          assert (eval_step pr other env f1 (Filter p) == Ok f3)
        | Error _ -> ()))
  | Error _ -> ()
#pop-options

(* THE THEOREM. Over a frame on which the derive answers `Ok` with string-or-null cells, and a
   filter that reads neither the derived column nor any the frame lacks, the derive then the
   filter and the filter then the derive fold to the same frame, or the same first error: the
   derive is removed from every dropped row's path and nothing is lost. *)
let derive_then_filter (pr:prims) (other:other_fn) (env:param_env) (f:wframe) (name:string) (x p:col_expr)
  : Lemma (requires (match derive_cells pr env f.cols f.rows x with
                     | Ok vs -> all_str_or_null vs
                     | Error _ -> False)
                    /\ closed_not f.cols name (reads p))
          (ensures eval_uncounted pr other env [Derive name x; Filter p] f ==
                   eval_uncounted pr other env [Filter p; Derive name x] f) =
  match index_of name f.cols with
  | Some i -> derive_then_filter_replace pr other env f name x p i
  | None -> derive_then_filter_append pr other env f name x p

(* The frame a walk reaches does not depend on the count it was handed: the count is threaded
   beside the frame and read by nothing. *)
let rec go_frame_independent (#e:Type0) (step:step_fn e) (f:wframe) (n m:nat) (p:list transform)
  : Lemma (ensures result_map fst (go step f n p) == result_map fst (go step f m p)) (decreases p) =
  match p with
  | [] -> ()
  | s :: rest ->
    (match step f s with
     | Ok f' -> go_frame_independent step f' (n + cost_of f s) (m + cost_of f s) rest
     | Error _ -> ())

(* THE COROLLARY IN CONTEXT. Wherever the pair sits in a pipeline, with the theorem's premises
   holding on the frame the prefix reaches, the whole pipeline folds to the same answer. *)
let reorder_in_context (pr:prims) (other:other_fn) (env:param_env) (pre post:list transform) (input:wframe)
  (name:string) (x p:col_expr)
  : Lemma (requires (match eval_uncounted pr other env pre input with
                     | Ok f ->
                       (match derive_cells pr env f.cols f.rows x with
                        | Ok vs -> all_str_or_null vs
                        | Error _ -> False)
                       /\ closed_not f.cols name (reads p)
                     | Error _ -> True))
          (ensures eval_uncounted pr other env (app pre (app [Derive name x; Filter p] post)) input ==
                   eval_uncounted pr other env (app pre (app [Filter p; Derive name x] post)) input) =
  let step = eval_step pr other env in
  go_app step input 0 pre (app [Derive name x; Filter p] post);
  go_app step input 0 pre (app [Filter p; Derive name x] post);
  match go step input 0 pre with
  | Error _ -> ()
  | Ok (f, n) ->
    go_app step f n [Derive name x; Filter p] post;
    go_app step f n [Filter p; Derive name x] post;
    derive_then_filter pr other env f name x p;
    go_frame_independent step f n 0 [Derive name x; Filter p];
    go_frame_independent step f n 0 [Filter p; Derive name x];
    (match go step f n [Derive name x; Filter p], go step f n [Filter p; Derive name x] with
     | Ok (g, a), Ok (g', b) -> go_frame_independent step g a b post
     | _ -> ())

(* ======================================================================================
   12. The totality verdict (Phase 269) — `Plan.isTotal`'s expression half, modelled clause for
       clause over the typer (Phase 266), and proved SOUND over the modelled evaluator.

   `verdict_sound`: an expression the verdict admits over a schema answers `Ok` on every row
   that fits the schema, with a cell that fits the type the typer gave it. The proof is the
   mutual induction the evaluator's four loops make necessary, and it discharges every error
   the evaluator's OWN arms can raise — an unknown column, an unbound param, an unpinned clock,
   a `Not` of a non-bool, an incomparable membership — from the verdict's clauses. What it
   assumes is stated in ONE place, `admits`, and it is the fourth parameter of this module: the
   four cell primitives answer `Ok`, with a cell of the typer's type, on exactly the operand
   shapes the verdict admits (an integer `Add` is not one of them; a `Float` one is). That
   record is the reading of `arith`, `castCell`, `applyScalar` and `compareCells` the F#
   verdict's every `true` is backed by, and `Conformance.plannerLaws` holds production to it
   over drawn tables; no theorem here reads a primitive's cell. Every theorem in this section
   is conditional on it, and on nothing else.

   The corollary `planner_reorder_sound` ties the two sections: the verdict's `true` on the
   derive, the typer's `Absent` or `Of StringType` on its expression (the F# `derivedColumnType
   = Some StringType`), and a filter closed over the schema less the derived name — the three
   clauses of `Planner.declineReorder`'s admission — are exactly the premises under which the
   reorder preserves the driver's result.
   ====================================================================================== *)

(* F#: `Typing` — what the typer knows of an expression's present values. *)
noextract
type typing =
  | Absent  : typing
  | Of      : column_type -> typing
  | Unknown : typing

noextract
let join (a b:typing) : Tot typing =
  match a, b with
  | Absent, t -> t
  | t, Absent -> t
  | Of x, Of y -> if x = y then Of x else Unknown
  | _ -> Unknown

(* F#: `List.fold join Absent ts` — the left fold, as written. *)
noextract
let rec fold_join (acc:typing) (ts:list typing) : Tot typing (decreases ts) =
  match ts with
  | [] -> acc
  | t :: rest -> fold_join (join acc t) rest

noextract
let join_all (ts:list typing) : Tot typing = fold_join Absent ts

noextract
let of_cell (c:cell) : Tot typing =
  match type_of c with
  | Some ty -> Of ty
  | None -> Absent

noextract
let numeric_t (t:typing) : Tot bool =
  match t with
  | Of IntType | Of FloatType -> true
  | _ -> false

noextract
let bool_like (t:typing) : Tot bool =
  match t with
  | Absent | Of BoolType -> true
  | _ -> false

noextract
let is_str (t:typing) : Tot bool =
  match t with
  | Absent | Of StringType -> true
  | _ -> false

noextract
let same_scalar (a b:typing) : Tot bool =
  match a, b with
  | Of x, Of y -> x = y
  | _ -> false

(* F#: `Typing.binary`. *)
noextract
let typing_binary (op:bin_op) (a b:typing) : Tot typing =
  let null_prop (decide:unit -> typing) : typing =
    match a, b with
    | Absent, _ | _, Absent -> Absent
    | _ -> decide () in
  match op with
  | Add | Sub | Mul ->
    null_prop (fun () ->
      if a = Of IntType && b = Of IntType then Of IntType
      else if numeric_t a && numeric_t b then Of FloatType
      else Unknown)
  | Div -> null_prop (fun () -> if numeric_t a && numeric_t b then Of FloatType else Unknown)
  | Mod -> null_prop (fun () -> if a = Of IntType && b = Of IntType then Of IntType else Unknown)
  | Eq | Ne | Lt | Le | Gt | Ge ->
    null_prop (fun () -> if (numeric_t a && numeric_t b) || same_scalar a b then Of BoolType else Unknown)
  | And | Or -> if bool_like a && bool_like b then join a b else Unknown
  | Contains | StartsWith | EndsWith ->
    null_prop (fun () -> if a = Of StringType && b = Of StringType then Of BoolType else Unknown)

(* F#: `Typing.not'`. *)
noextract
let typing_not (a:typing) : Tot typing =
  match a with
  | Absent -> Absent
  | Of BoolType -> Of BoolType
  | _ -> Unknown

(* F#: `Typing.cast`. *)
noextract
let typing_cast (ty:column_type) (a:typing) : Tot typing =
  match a with
  | Absent -> Absent
  | _ -> Of ty

(* F#: `Typing.inList`. *)
noextract
let typing_in_list (subject:typing) : Tot typing =
  match subject with
  | Absent -> Absent
  | _ -> Of BoolType

noextract
let rec any_absent (ts:list typing) : Tot bool =
  match ts with
  | [] -> false
  | Absent :: _ -> true
  | _ :: rest -> any_absent rest

(* F#: `Typing.applyFn`, function by function. *)
noextract
let typing_apply_fn (fn:scalar_fn) (ts:list typing) : Tot typing =
  let unary (decide:typing -> typing) : typing =
    match ts with
    | [Absent] -> Absent
    | [t] -> decide t
    | _ -> Absent in
  match fn with
  | Abs ->
    unary (fun t ->
      match t with
      | Of IntType -> Of IntType
      | Of FloatType -> Of FloatType
      | _ -> Unknown)
  | Round | Floor | Ceil | Sqrt -> unary (fun _ -> Of FloatType)
  | Length -> unary (fun _ -> Of IntType)
  | Lower | Upper | Trim -> unary (fun _ -> Of StringType)
  | Substr ->
    (match ts with
     | [Absent; _; _] -> Absent
     | [_; _; _] -> Of StringType
     | _ -> Absent)
  | DatePart ->
    (match ts with
     | [_; Absent] -> Absent
     | [_; _] -> Of IntType
     | _ -> Absent)
  | Concat -> (match ts with [] -> Absent | _ -> if any_absent ts then Absent else Of StringType)
  | Replace -> (match ts with [_; _; _] -> if any_absent ts then Absent else Of StringType | _ -> Absent)
  | DateDiffDays -> (match ts with [_; _] -> if any_absent ts then Absent else Of IntType | _ -> Absent)
  | Least | Greatest -> (match ts with [] -> Absent | _ -> if any_absent ts then Absent else join_all ts)
  | IndexOf -> (match ts with [_; _] -> if any_absent ts then Absent else Of IntType | _ -> Absent)

(* F#: `typing cols e` — the static typing over a schema, arm for arm. `colType` is the first
   column of that name, as `assoc` is. *)
noextract
let rec typing_of (cols:schema) (x:col_expr) : Tot typing (decreases x) =
  match x with
  | Col n -> (match assoc n cols with Some ty -> Of ty | None -> Unknown)
  | Lit c -> of_cell c
  | Param _ | Now _ -> Unknown
  | Binary op a b -> typing_binary op (typing_of cols a) (typing_of cols b)
  | Not a -> typing_not (typing_of cols a)
  | Coalesce xs -> join_all (typings_of cols xs)
  | Case cases els -> join_all (typing_of cols els :: typings_thens cols cases)
  | Cast ty a -> typing_cast ty (typing_of cols a)
  | ApplyFn fn args -> typing_apply_fn fn (typings_of cols args)
  | InList a _ -> typing_in_list (typing_of cols a)
  | IsNull _ -> Of BoolType
  | InParam a _ -> typing_in_list (typing_of cols a)
and typings_of (cols:schema) (xs:list col_expr) : Tot (list typing) (decreases xs) =
  match xs with
  | [] -> []
  | x :: rest -> typing_of cols x :: typings_of cols rest
and typings_thens (cols:schema) (cases:list (col_expr & col_expr)) : Tot (list typing) (decreases cases) =
  match cases with
  | [] -> []
  | (_, t) :: rest -> typing_of cols t :: typings_thens cols rest

(* A cell fits a typing: `Absent` is the null, `Of ty` the null or a present cell of that type,
   `Unknown` anything. *)
noextract
let fits (t:typing) (c:cell) : Tot bool =
  match t with
  | Absent -> Null? c
  | Of ty -> Null? c || type_of c = Some ty
  | Unknown -> true

noextract
let rec cells_fit (ts:list typing) (cs:list cell) : Tot bool =
  match ts, cs with
  | [], [] -> true
  | t :: tr, c :: cr -> fits t c && cells_fit tr cr
  | _ -> false

(* A row fits its schema: cell by cell, the null or the column's type. *)
noextract
let rec row_typed (cols:schema) (row:list cell) : Tot bool =
  match cols, row with
  | [], [] -> true
  | (_, ty) :: ct, c :: rt -> fits (Of ty) c && row_typed ct rt
  | _ -> false

noextract
let rec rows_typed (cols:schema) (rows:list (list cell)) : Tot bool =
  match rows with
  | [] -> true
  | r :: rest -> row_typed cols r && rows_typed cols rest

(* ---- the verdict, clause for clause (F#: `Planner.exprTotal`) ---- *)

(* F#: `comparable`. *)
noextract
let comparable (a b:typing) : Tot bool =
  match a, b with
  | Absent, _ | _, Absent -> true
  | _ -> (numeric_t a && numeric_t b) || same_scalar a b

noextract
let rec all_comparable (t:typing) (ts:list typing) : Tot bool =
  match ts with
  | [] -> true
  | u :: rest -> comparable t u && all_comparable t rest

noextract
let rec present (ts:list typing) : Tot (list typing) =
  match ts with
  | [] -> []
  | Absent :: rest -> present rest
  | t :: rest -> t :: present rest

(* The `Binary` clause's admission, by operator. *)
noextract
let admit_binary (op:bin_op) (a b:typing) : Tot bool =
  match op with
  | Add | Sub | Mul ->
    (match a, b with
     | Absent, _ | _, Absent -> true
     | Of IntType, Of IntType -> false
     | _ -> numeric_t a && numeric_t b)
  | Div ->
    (match a, b with
     | Absent, _ | _, Absent -> true
     | _ -> numeric_t a && numeric_t b)
  | Mod ->
    (match a, b with
     | Absent, _ | _, Absent -> true
     | _ -> false)
  | Eq | Ne | Lt | Le | Gt | Ge -> comparable a b
  | And | Or -> bool_like a && bool_like b
  | Contains | StartsWith | EndsWith ->
    (match a, b with
     | Absent, _ | _, Absent -> true
     | _ -> is_str a && is_str b)

(* The `Cast` clause's admission. *)
noextract
let admit_cast (target:column_type) (t:typing) : Tot bool =
  match target, t with
  | _, Absent -> true
  | StringType, _ -> true
  | FloatType, Of IntType | FloatType, Of FloatType -> true
  | IntType, Of IntType | IntType, Of BoolType -> true
  | BoolType, Of BoolType | BoolType, Of IntType -> true
  | DateType, Of DateType | DateType, Of StringType -> true
  | TimestampType, Of TimestampType | TimestampType, Of StringType -> true
  | _ -> false

(* The `ApplyFn` clause's admission over the argument typings. `Substr`'s start and length
   must also be non-null, which is a fact about the ARGUMENT EXPRESSIONS (`never_null`) rather
   than their typings, and is checked in `expr_total` beside this. *)
noextract
let admit_fn (fn:scalar_fn) (ts:list typing) : Tot bool =
  match fn with
  | Abs -> (match ts with [Absent] | [Of FloatType] -> true | _ -> false)
  | Round | Floor | Ceil | Sqrt -> (match ts with [Absent] -> true | [t] -> numeric_t t | _ -> false)
  | Length | Lower | Upper | Trim -> (match ts with [t] -> is_str t | _ -> false)
  | Substr -> (match ts with [t0; Of IntType; Of IntType] -> is_str t0 | _ -> false)
  | DatePart | DateDiffDays -> false
  | Concat -> Cons? ts
  | Replace -> (match ts with [a; b; c] -> is_str a && is_str b && is_str c | _ -> false)
  | Least | Greatest ->
    (match ts with
     | [] -> false
     | _ -> (match present ts with [] -> true | first :: _ -> all_comparable first (present ts)))
  | IndexOf -> (match ts with [a; b] -> is_str a && is_str b | _ -> false)

(* F#: `neverNull` — a present literal, or the presence test. *)
noextract
let never_null (x:col_expr) : Tot bool =
  match x with
  | Lit c -> not (Null? c)
  | IsNull _ -> true
  | _ -> false

noextract
let substr_args_never_null (fn:scalar_fn) (args:list col_expr) : Tot bool =
  match fn, args with
  | Substr, [_; a1; a2] -> never_null a1 && never_null a2
  | Substr, _ -> false
  | _ -> true

(* THE VERDICT, F#: `Planner.exprTotal`. *)
noextract
let rec expr_total (cols:schema) (x:col_expr) : Tot bool (decreases x) =
  match x with
  | Col n -> Some? (assoc n cols)
  | Lit _ -> true
  | Param _ | Now _ | InParam _ _ -> false
  | Binary op a b ->
    expr_total cols a && expr_total cols b && admit_binary op (typing_of cols a) (typing_of cols b)
  | Not a -> expr_total cols a && bool_like (typing_of cols a)
  | IsNull a -> expr_total cols a
  | Coalesce xs -> all_total cols xs
  | Case cases els -> all_total_cases cols cases && expr_total cols els
  | Cast ty a -> expr_total cols a && admit_cast ty (typing_of cols a)
  | InList a items ->
    expr_total cols a && all_total cols items && all_comparable (typing_of cols a) (typings_of cols items)
  | ApplyFn fn args ->
    all_total cols args && admit_fn fn (typings_of cols args) && substr_args_never_null fn args
and all_total (cols:schema) (xs:list col_expr) : Tot bool (decreases xs) =
  match xs with
  | [] -> true
  | x :: rest -> expr_total cols x && all_total cols rest
and all_total_cases (cols:schema) (cases:list (col_expr & col_expr)) : Tot bool (decreases cases) =
  match cases with
  | [] -> true
  | (w, t) :: rest -> expr_total cols w && expr_total cols t && all_total_cases cols rest

(* ---- the assumption, in one place ---- *)

(* `Substr`'s start and length present, where the function is `Substr`. *)
noextract
let substr_args_present (fn:scalar_fn) (cs:list cell) : Tot bool =
  match fn, cs with
  | Substr, [_; a1; a2] -> not (Null? a1) && not (Null? a2)
  | Substr, _ -> false
  | _ -> true

(* THE ASSUMPTION. On the operand shapes the verdict admits, each primitive answers `Ok`, with
   a cell that fits the typing the typer assigns the node; and the pinned ordering compares any
   two present cells of comparable typings. It is what `arith`, `castCell`, `applyScalar` and
   `compareCells` do on those shapes — each `true` of the F# verdict cites the arm — and it is
   held to production by `Conformance.plannerLaws`; here it is the one hypothesis every theorem
   of this section is conditional on. *)
noextract
noeq type admits (pr:prims) = {
  binary_ok  : (op:bin_op) -> (ta:typing) -> (tb:typing) -> (a:cell) -> (b:cell) ->
               Lemma (requires admit_binary op ta tb /\ fits ta a /\ fits tb b)
                     (ensures (match pr.binary op a b with
                               | Ok c -> fits (typing_binary op ta tb) c
                               | Error _ -> False));
  cast_ok    : (ty:column_type) -> (t:typing) -> (c:cell) ->
               Lemma (requires admit_cast ty t /\ fits t c)
                     (ensures (match pr.cast_cell ty c with
                               | Ok r -> fits (typing_cast ty t) r
                               | Error _ -> False));
  apply_ok   : (fn:scalar_fn) -> (ts:list typing) -> (cs:list cell) ->
               Lemma (requires admit_fn fn ts /\ cells_fit ts cs /\ substr_args_present fn cs)
                     (ensures (match pr.apply_fn fn cs with
                               | Ok r -> fits (typing_apply_fn fn ts) r
                               | Error _ -> False));
  compare_ok : (ta:typing) -> (tb:typing) -> (a:cell) -> (b:cell) ->
               Lemma (requires comparable ta tb /\ fits ta a /\ fits tb b /\ not (Null? a) /\ not (Null? b))
                     (ensures Some? (pr.compare a b))
}

(* ---- the join's algebra, as far as the proof needs it ---- *)

let fits_join_l (t u:typing) (c:cell) : Lemma (requires fits t c) (ensures fits (join t u) c) = ()

let fits_join_r (t u:typing) (c:cell) : Lemma (requires fits u c) (ensures fits (join t u) c) = ()

let join_assoc (a b c:typing) : Lemma (ensures join (join a b) c == join a (join b c)) = ()

(* The left fold is the join of the accumulator with the fold from `Absent`. *)
let rec fold_join_acc (acc:typing) (ts:list typing)
  : Lemma (ensures fold_join acc ts == join acc (fold_join Absent ts)) (decreases ts) =
  match ts with
  | [] -> ()
  | t :: rest ->
    fold_join_acc (join acc t) rest;
    fold_join_acc t rest;
    join_assoc acc t (fold_join Absent rest)

let join_all_cons (t:typing) (ts:list typing)
  : Lemma (ensures join_all (t :: ts) == join t (join_all ts)) =
  fold_join_acc t ts

let fits_of_cell (c:cell) : Lemma (ensures fits (of_cell c) c) = ()

(* A typed row answers a typed cell at every column it has. *)
let rec typed_at (cols:schema) (row:list cell) (n:string)
  : Lemma (requires row_typed cols row)
          (ensures (match assoc n cols, index_of n cols with
                    | Some ty, Some i ->
                      (match nth_opt row i with
                       | Some c -> fits (Of ty) c
                       | None -> False)
                    | None, None -> True
                    | _ -> False))
          (decreases cols) =
  match cols, row with
  | [], [] -> ()
  | (m, _) :: ct, _ :: rt -> if m = n then () else typed_at ct rt n

(* A never-null argument evaluates, when it evaluates, to a present cell. *)
let never_null_sound (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (x:col_expr)
  : Lemma (requires never_null x)
          (ensures (match eval_expr pr env cols row x with
                    | Ok c -> not (Null? c)
                    | Error _ -> True)) =
  match x with
  | Lit _ -> ()
  | IsNull a -> ()

(* `eval_args` over three arguments answers the three cells, one per argument. *)
#push-options "--fuel 4"
let eval_args_3 (pr:prims) (env:param_env) (cols:schema) (row:row_of (len cols)) (a0 a1 a2:col_expr)
  : Lemma (ensures (match eval_args pr env cols row [a0; a1; a2] with
                    | Ok [c0; c1; c2] ->
                      eval_expr pr env cols row a1 == Ok c1 /\ eval_expr pr env cols row a2 == Ok c2
                    | Ok _ -> False
                    | Error _ -> True)) = ()
#pop-options

(* THE THEOREM. *)
let rec verdict_sound (pr:prims) (h:admits pr) (env:param_env) (cols:schema) (row:row_of (len cols)) (x:col_expr)
  : Lemma (requires expr_total cols x /\ row_typed cols row)
          (ensures (match eval_expr pr env cols row x with
                    | Ok c -> fits (typing_of cols x) c
                    | Error _ -> False))
          (decreases x) =
  match x with
  | Col n ->
    typed_at cols row n;
    (match index_of n cols with
     | Some i -> nth_opt_nth row i
     | None -> ())
  | Lit c -> fits_of_cell c
  | Param _ | Now _ | InParam _ _ -> ()
  | Binary op a b ->
    verdict_sound pr h env cols row a;
    verdict_sound pr h env cols row b;
    (match eval_expr pr env cols row a, eval_expr pr env cols row b with
     | Ok av, Ok bv -> h.binary_ok op (typing_of cols a) (typing_of cols b) av bv
     | _ -> ())
  | Not a -> verdict_sound pr h env cols row a
  | IsNull a -> verdict_sound pr h env cols row a
  | Coalesce xs -> sound_coalesce pr h env cols row xs
  | Case cases els ->
    sound_case pr h env cols row cases;
    verdict_sound pr h env cols row els;
    join_all_cons (typing_of cols els) (typings_thens cols cases);
    (match eval_case pr env cols row cases with
     | Some (Ok c) -> fits_join_r (typing_of cols els) (join_all (typings_thens cols cases)) c
     | Some (Error _) -> ()
     | None ->
       (match eval_expr pr env cols row els with
        | Ok c -> fits_join_l (typing_of cols els) (join_all (typings_thens cols cases)) c
        | Error _ -> ()))
  | Cast ty a ->
    verdict_sound pr h env cols row a;
    (match eval_expr pr env cols row a with
     | Ok c -> h.cast_ok ty (typing_of cols a) c
     | Error _ -> ())
  | InList a items ->
    verdict_sound pr h env cols row a;
    (match eval_expr pr env cols row a with
     | Ok Null -> ()
     | Ok sv -> sound_in pr h env cols row (typing_of cols a) sv false items
     | Error _ -> ())
  | ApplyFn fn args ->
    sound_args pr h env cols row args;
    (match eval_args pr env cols row args with
     | Ok cs ->
       (match fn, args with
        | Substr, [a0; a1; a2] ->
          eval_args_3 pr env cols row a0 a1 a2;
          never_null_sound pr env cols row a1;
          never_null_sound pr env cols row a2
        | _ -> ());
       h.apply_ok fn (typings_of cols args) cs
     | Error _ -> ())
and sound_coalesce (pr:prims) (h:admits pr) (env:param_env) (cols:schema) (row:row_of (len cols)) (xs:list col_expr)
  : Lemma (requires all_total cols xs /\ row_typed cols row)
          (ensures (match eval_coalesce pr env cols row xs with
                    | Ok c -> fits (join_all (typings_of cols xs)) c
                    | Error _ -> False))
          (decreases xs) =
  match xs with
  | [] -> ()
  | x :: rest ->
    verdict_sound pr h env cols row x;
    join_all_cons (typing_of cols x) (typings_of cols rest);
    (match eval_expr pr env cols row x with
     | Ok Null ->
       sound_coalesce pr h env cols row rest;
       (match eval_coalesce pr env cols row rest with
        | Ok c -> fits_join_r (typing_of cols x) (join_all (typings_of cols rest)) c
        | Error _ -> ())
     | Ok c -> fits_join_l (typing_of cols x) (join_all (typings_of cols rest)) c
     | Error _ -> ())
and sound_case (pr:prims) (h:admits pr) (env:param_env) (cols:schema) (row:row_of (len cols)) (cases:list (col_expr & col_expr))
  : Lemma (requires all_total_cases cols cases /\ row_typed cols row)
          (ensures (match eval_case pr env cols row cases with
                    | None -> True
                    | Some (Ok c) -> fits (join_all (typings_thens cols cases)) c
                    | Some (Error _) -> False))
          (decreases cases) =
  match cases with
  | [] -> ()
  | (w, t) :: rest ->
    verdict_sound pr h env cols row w;
    join_all_cons (typing_of cols t) (typings_thens cols rest);
    (match eval_expr pr env cols row w with
     | Error _ -> ()
     | Ok (Bool true) ->
       verdict_sound pr h env cols row t;
       (match eval_expr pr env cols row t with
        | Ok c -> fits_join_l (typing_of cols t) (join_all (typings_thens cols rest)) c
        | Error _ -> ())
     | Ok _ ->
       sound_case pr h env cols row rest;
       (match eval_case pr env cols row rest with
        | Some (Ok c) -> fits_join_r (typing_of cols t) (join_all (typings_thens cols rest)) c
        | _ -> ()))
and sound_in (pr:prims) (h:admits pr) (env:param_env) (cols:schema) (row:row_of (len cols))
  (ta:typing) (sv:cell) (saw_null:bool) (items:list col_expr)
  : Lemma (requires all_total cols items /\ all_comparable ta (typings_of cols items)
                    /\ fits ta sv /\ not (Null? sv) /\ row_typed cols row)
          (ensures (match eval_in pr env cols row sv saw_null items with
                    | Ok c -> fits (Of BoolType) c
                    | Error _ -> False))
          (decreases items) =
  match items with
  | [] -> ()
  | it :: rest ->
    verdict_sound pr h env cols row it;
    (match eval_expr pr env cols row it with
     | Error _ -> ()
     | Ok Null -> sound_in pr h env cols row ta sv true rest
     | Ok iv ->
       h.compare_ok ta (typing_of cols it) sv iv;
       (match pr.compare sv iv with
        | Some 0 -> ()
        | Some _ -> sound_in pr h env cols row ta sv saw_null rest
        | None -> ()))
and sound_args (pr:prims) (h:admits pr) (env:param_env) (cols:schema) (row:row_of (len cols)) (args:list col_expr)
  : Lemma (requires all_total cols args /\ row_typed cols row)
          (ensures (match eval_args pr env cols row args with
                    | Ok cs -> cells_fit (typings_of cols args) cs
                    | Error _ -> False))
          (decreases args) =
  match args with
  | [] -> ()
  | a :: rest ->
    verdict_sound pr h env cols row a;
    (match eval_expr pr env cols row a with
     | Error _ -> ()
     | Ok _ -> sound_args pr h env cols row rest)

(* ---- the verdict lifted to the two steps that evaluate an expression ---- *)

(* A derive the verdict admits answers `Ok` over every typed frame. *)
let rec derive_total (pr:prims) (h:admits pr) (env:param_env) (cols:schema) (x:col_expr)
  (rows:list (list cell){all_width (len cols) rows})
  : Lemma (requires expr_total cols x /\ rows_typed cols rows)
          (ensures Ok? (derive_cells pr env cols rows x)) (decreases rows) =
  match rows with
  | [] -> ()
  | r :: rest ->
    verdict_sound pr h env cols r x;
    (match eval_expr pr env cols r x with
     | Ok _ -> derive_total pr h env cols x rest
     | Error _ -> ())

(* A filter the verdict admits answers `Ok` over every typed frame. *)
let rec filter_total (pr:prims) (h:admits pr) (env:param_env) (cols:schema) (p:col_expr)
  (rows:list (list cell){all_width (len cols) rows})
  : Lemma (requires expr_total cols p /\ rows_typed cols rows)
          (ensures Ok? (filter_rows pr env cols rows p)) (decreases rows) =
  match rows with
  | [] -> ()
  | r :: rest ->
    verdict_sound pr h env cols r p;
    (match eval_expr pr env cols r p with
     | Ok (Bool true) -> filter_total pr h env cols p rest
     | Ok _ -> filter_total pr h env cols p rest
     | Error _ -> ())

(* F#: `Plan.isTotal` on the two expression-evaluating verbs: the step answers `Ok` on every
   typed frame of the schema. *)
let step_total (pr:prims) (h:admits pr) (other:other_fn) (env:param_env) (f:wframe) (t:transform)
  : Lemma (requires rows_typed f.cols f.rows
                    /\ (match t with
                        | Filter p -> expr_total f.cols p
                        | Derive _ x -> expr_total f.cols x
                        | _ -> False))
          (ensures Ok? (eval_step pr other env f t)) =
  match t with
  | Filter p -> filter_total pr h env f.cols p f.rows
  | Derive _ x -> derive_total pr h env f.cols x f.rows

(* A derive whose expression the typer calls `Absent` or `Of StringType` — the F#
   `derivedColumnType = Some StringType` — produces strings and nulls, over every typed frame. *)
let rec derive_str_or_null (pr:prims) (h:admits pr) (env:param_env) (cols:schema) (x:col_expr)
  (rows:list (list cell){all_width (len cols) rows})
  : Lemma (requires expr_total cols x /\ rows_typed cols rows
                    /\ (typing_of cols x == Absent \/ typing_of cols x == Of StringType))
          (ensures (match derive_cells pr env cols rows x with
                    | Ok vs -> all_str_or_null vs
                    | Error _ -> False)) (decreases rows) =
  match rows with
  | [] -> ()
  | r :: rest ->
    verdict_sound pr h env cols r x;
    (match eval_expr pr env cols r x with
     | Ok _ -> derive_str_or_null pr h env cols x rest
     | Error _ -> ())

(* THE COROLLARY. The planner's admission of `Filter p` ahead of `Derive name x` — the derive
   total by the verdict, its column string-typed by the typer, the filter closed over the schema
   less the derived name — preserves the driver's result over every typed frame. *)
let planner_reorder_sound (pr:prims) (h:admits pr) (other:other_fn) (env:param_env) (f:wframe)
  (name:string) (x p:col_expr)
  : Lemma (requires expr_total f.cols x
                    /\ (typing_of f.cols x == Absent \/ typing_of f.cols x == Of StringType)
                    /\ rows_typed f.cols f.rows
                    /\ closed_not f.cols name (reads p))
          (ensures eval_uncounted pr other env [Derive name x; Filter p] f ==
                   eval_uncounted pr other env [Filter p; Derive name x] f) =
  derive_str_or_null pr h env f.cols x f.rows;
  derive_then_filter pr other env f name x p
