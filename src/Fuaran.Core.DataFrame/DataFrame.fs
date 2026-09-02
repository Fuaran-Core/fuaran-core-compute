namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.DataFrame (Phase 29) — the declarative-compute layer over the
//  `Fuaran.Core.Column` strand: a serializable dataframe-transform algebra
//  (`Transform` + `ColExpr`), a pure reference evaluator with *pinned* semantics
//  (null/NA propagation, type coercion, group/sort stability, float
//  canonicalisation), and a canonical wire codec. `transformLaws` (in
//  `Fuaran.Core.Conformance`) certifies any host evaluator byte-identical to the
//  reference over a generated sample — the cross-host parity contract.
//
//  This is the substrate that lets the liftable majority of "notebook compute"
//  (filter / group / derive / pivot / window) run anywhere as *data* rather than
//  code (Compute Layer spec §2). FSharp.Core only, Fable-clean — pure folds over
//  the columnar model, no platform primitives.
// ============================================================================

// `AggFn` (the group/window aggregate function set) moved to `Fuaran.Core.Column` (Phase 36) so the
// aggregate semantics are a public, single-source surface (`Column.aggregate`) the `GroupBy`/`Pivot`
// evaluation below *calls* rather than inlines. It stays `Fuaran.Core.AggFn` (same namespace), so every
// reference here is unchanged.

type JoinKind =
    | Inner
    | Left
    | Right
    | Outer
    /// Phase 101 — keep each LEFT row that has at least one match on the right, ONCE, with the left
    /// schema only (no right columns, no fan-out). Not expressible as `Left` + a filter: a left row
    /// matching two right rows is duplicated by `Left`, and a `Distinct` afterwards cannot undo that
    /// without also collapsing rows the input legitimately duplicated.
    | Semi
    /// Phase 101 — the complement of `Semi`: keep each LEFT row with NO match on the right, with the
    /// left schema only. This one IS expressible as `Left` + `IsNull(<right key>)` + `Project`
    /// (`cellEq` never matches a null, so a matched row's right key is always present, and an
    /// unmatched left row yields exactly one output row) — the case is here for closed-set symmetry
    /// with `Semi` and because the idiom is three steps and a schema leak, not one verb.
    | Anti

type WindowFn =
    | RowNumber
    /// Ties share a rank and the NEXT distinct order key is the next integer — i.e. this is the
    /// gapless "dense" rank, not SQL's `RANK()`. The name predates the distinction; `DenseRank` is
    /// the explicit spelling of the same computation, and `CompetitionRank` is SQL `RANK()`.
    /// Kept as-is because re-pointing it at the gapped semantics would silently change every
    /// existing pipeline's output — a major bump, not an additive one.
    | Rank
    | Lag
    | Lead
    | CumulSum
    | RollingMean
    /// Phase 101 — the explicit spelling of gapless ranking: ties share a rank, the next distinct
    /// order key is `rank + 1`. Byte-identical to `Rank`; a reader reaching for either gets the
    /// semantics its name promises.
    | DenseRank
    /// Phase 101 — SQL `RANK()`: ties share the LOWEST rank of the tied block and the next distinct
    /// order key skips by the block's size (`1, 1, 3`). The member of the ranking family that had no
    /// spelling at all: `Rank` already computed the dense variant.
    | CompetitionRank
    /// Phase 101 — SQL `NTILE(n)`: distribute the partition's ordered rows into `n` buckets as evenly
    /// as possible, the first `rowCount % n` buckets taking one extra row. `n < 1` is a named
    /// `EvalError`, never a division. The bucket count rides an additive `"n"` wire field present only
    /// for this case, so every other window step's wire is byte-unchanged.
    | NTile of buckets: int
    /// Phase 101 — the running maximum over present values (nulls carry the prior value forward; a
    /// leading run of nulls is `Null`). Keeps the source column's type, exactly as `AggFn.Max` does.
    | CumulMax
    /// Phase 101 — the running minimum; `CumulMax`'s pair.
    | CumulMin
    /// Phase 101 — the trailing-window total over the SAME pinned window `RollingMean` averages
    /// (current + 2 preceding, present values only; `Null` when the window holds none). `Float`, like
    /// `RollingMean`, so the two compose without a cast.
    | RollingSum

type SortDir =
    | Asc
    | Desc

// ---------------------------------------------------------------------------
//  Deliberate omissions from the scalar/verb vocabulary (Phase 101) — decisions,
//  not gaps. Recorded here because this is where a reader adding a function looks;
//  the full reasoning is DECISIONS.md D13.
//
//  * NO CLOCK — no `Now` / `Today` / `CurrentDate`. The evaluator is a pure function of
//    (table, env, pipeline): a clock would make the same pipeline over the same data
//    produce different answers on two hosts (and on one host twice), which is precisely
//    what `Conformance.transformLaws` byte-identity and deterministic replay certify
//    against. The intended route is a host-injected `Param` — bind `"today"` once at the
//    edge and the pipeline stays a total function. `DateDiffDays` then does the arithmetic.
//  * NO REGEX — no `Matches` / `RegexReplace` / `RegexExtract`. Regex has no portable
//    semantics: .NET, JS, Go and Rust differ on syntax, escapes, Unicode classes and
//    (for the backtracking engines) worst-case time, so a pattern is not a cross-host
//    value. Reach for `Contains` / `StartsWith` / `EndsWith` / `IndexOf` / `Substr` /
//    `Replace`, or derive the column host-side before it enters the algebra.
//  * NO `Pow` / `Log` — IEEE-754 does not require transcendental functions to be
//    correctly rounded, so `Math.Pow` / `Math.Log` may differ in the last ulp between
//    hosts, and one ulp is a different byte in the canonical float layout. `Sqrt` IS
//    pinned by IEEE-754 (exact, correctly rounded), which is why it is present and they
//    are not; integer powers compose from `Mul`.
//  * NO `Split` and NO explode/flatten — the columnar `Cell` is a closed FLAT scalar set,
//    and a list-valued cell was explicitly rejected (D12) for blast radius and model
//    coherence. Both verbs need a value shape the model does not have, so the gap is in
//    the type model rather than the verb set; a host flattens before handing Core a table.
//  * NO `PadLeft` / number formatting — the algebra pins VALUES; presentation belongs to
//    the render tier, which knows the locale and the column width and this does not.
// ---------------------------------------------------------------------------

/// The fixed scalar-function set a `ColExpr` may apply (spec §2).
type ScalarFn =
    | Abs
    | Round
    | Floor
    | Ceil
    | Length
    | Lower
    | Upper
    | Substr
    | DatePart
    // Phase 90 — string building + the pinned day-delta.
    | Concat
    | Trim
    | Replace
    | DateDiffDays
    // Phase 101 — the closed-set edges. `Pow`/`Log` are deliberately absent (see the block above).
    /// The non-negative square root. `Float`-valued; a NEGATIVE argument is `Null`, matching the
    /// pinned `Div`-by-zero rule (the strand answers a mathematically-undefined result with `Null`,
    /// never a `NaN` the canonical wire cannot even carry).
    | Sqrt
    /// SQL `LEAST` — the smallest of its variadic arguments by the pinned cell ordering, returned as
    /// the winning cell (source type preserved). Any null argument propagates, as `Concat`'s does;
    /// compose `Coalesce` for a treat-null-as-floor idiom. Named `Least`/`Greatest` rather than
    /// `Min`/`Max` because `AggFn` already owns those two names in this namespace.
    | Least
    /// SQL `GREATEST` — `Least`'s pair.
    | Greatest
    /// The 0-based ordinal index of the first occurrence of the second argument in the first, or `-1`
    /// when absent (an empty needle is `0`). 0-based deliberately: `Substr` is 0-based here, so
    /// `Substr(s, IndexOf(s, t), n)` composes — the 1-based SQL `POSITION` convention would not.
    | IndexOf

/// A binary operator: arithmetic, comparison, or logical. Null propagates through arithmetic +
/// comparison (any null operand ⇒ null); the logical pair is three-valued (Kleene).
type BinOp =
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
    // Phase 90 — Ordinal substring predicates (Str × Str → Bool, null-propagating).
    | Contains
    | StartsWith
    | EndsWith

/// A scalar expression over a row's columns + literals — the `ColExpr` algebra (spec §2).
type ColExpr =
    | Col of string
    | Lit of Cell
    /// A named parameter resolved per-evaluation from the host's binding environment (Phase 77):
    /// a UI filter chip's current value, a state slot, a host threshold. Core stays domain-agnostic
    /// (GP6) — a param is a named `Cell`; *who* binds it is the host's business. Strict: an unbound
    /// `Param` is an `EvalError.UnboundParam`, never a throw. Lenient "unset ⇒ no constraint" idioms
    /// are host-side policy, implemented by pruning steps whose params are unbound (via `paramsOf`).
    | Param of name: string
    | Binary of BinOp * ColExpr * ColExpr
    | Not of ColExpr
    | Coalesce of ColExpr list
    | Case of cases: (ColExpr * ColExpr) list * elseExpr: ColExpr
    | Cast of ColumnType * ColExpr
    | ApplyFn of ScalarFn * ColExpr list
    /// SQL three-valued membership (Phase 90): subject null => null; any equal item => true; no
    /// match with a null item => null; else false. Literal-list multi-select; list-valued Params
    /// are a deferred design (a Param is a scalar Cell).
    | InList of ColExpr * ColExpr list
    /// The honest presence test (Phase 90) — total: always Bool, never null.
    | IsNull of ColExpr
    /// A LIST-valued named parameter membership test (Phase 91) — the multi-select-chip binding.
    /// Resolves by SUBSTITUTION (`substituteListParams`: `InParam(x, n)` -> `InList(x, literals)`),
    /// mirroring how scalar `Param`s resolve via `substitute`; one that reaches evaluation unbound
    /// is a strict `UnboundParam`. Wire: `{"$type":"in","expr":...,"param":"<name>"}` — the same
    /// `in` tag as the literal form, with `param` in place of `items` (exactly one of the two).
    | InParam of ColExpr * name: string

/// One aggregate in a `GroupBy` / `Pivot`: an output `Name`, the aggregate `Fn`, over column `Of`.
type Agg = { Name: string; Fn: AggFn; Of: string }

/// A window step's specification (spec §2 — `Window`).
type WindowSpec =
    { PartitionBy: string list
      OrderBy: (string * SortDir) list
      Fn: WindowFn
      Of: string
      As: string }

/// A pivot step's specification (spec §2 — `Pivot`).
type PivotSpec =
    { Index: string list
      On: string
      Values: string
      Agg: AggFn }

/// One transform step — the full v1 verb set (spec §2). A pipeline is an ordered `Transform list`
/// over a `DataSource`.
type Transform =
    /// Keep rows whose predicate evaluates to `Bool true` (null / false drop).
    | Filter of ColExpr
    /// Keep/rename columns: ordered `(source, output)` pairs.
    | Project of (string * string) list
    /// A computed column `name` from a `ColExpr` (overwrites an existing column of the same name).
    | Derive of string * ColExpr
    /// Group by `keys`, producing one row per group with the listed aggregates.
    | GroupBy of string list * Agg list
    /// Join another source on `(leftCol, rightCol)` key pairs.
    | Join of DataSource * (string * string) list * JoinKind
    | Window of WindowSpec
    | Pivot of PivotSpec
    /// Long→wide's inverse: melt `valueVars` into `(variable, value)` rows, keeping `idVars`.
    | Unpivot of idVars: string list * valueVars: string list
    | Sort of (string * SortDir) list
    | Distinct
    | Limit of n: int * offset: int
    | Union of DataSource
    /// Phase 101 — keep the left rows whose FULL ROW also appears in `source`, preserving the left's
    /// order and its duplicate multiplicity (SQL `INTERSECT ALL`). Row identity is the same canonical
    /// token `Distinct` dedups on, so `Null` matches `Null` (unlike a `Join` key, where `cellEq`
    /// never matches a null) and an `Int 1` never matches a `Float 1.0`. Composes with `Distinct`
    /// exactly as `Union` does: `Intersect · Distinct` is SQL `INTERSECT`.
    | Intersect of DataSource
    /// Phase 101 — `Intersect`'s complement: keep the left rows whose full row does NOT appear in
    /// `source` (SQL `EXCEPT ALL`); `Except · Distinct` is SQL `EXCEPT`. The everyday "rows in A not
    /// in B" that had no spelling while `Union` shipped alone.
    | Except of DataSource

/// Pure, total derivations over the `ColExpr` algebra (Phase 77) — the param surface a host reads to
/// derive dependency edges, reactivity subscriptions, and its unbound-param pruning policy. No
/// evaluation, no env: the edge is *computed from the expression*, never separately declared.
[<RequireQualifiedAccess>]
module ColExpr =

    /// Every `Param` name the expression references, in stable left-to-right order **with**
    /// duplicates (recursing through every sub-expression kind). `paramsOf` dedups; the raw walk is
    /// exposed for callers that want occurrence order preserved.
    let rec paramNames (e: ColExpr) : string list =
        match e with
        | Col _
        | Lit _ -> []
        | Param n -> [ n ]
        | Binary(_, a, b) -> paramNames a @ paramNames b
        | Not x -> paramNames x
        | Coalesce xs -> xs |> List.collect paramNames
        | Case(cases, els) ->
            (cases |> List.collect (fun (w, t) -> paramNames w @ paramNames t))
            @ paramNames els
        | Cast(_, x) -> paramNames x
        | ApplyFn(_, xs) -> xs |> List.collect paramNames
        | InList(x, items) -> paramNames x @ (items |> List.collect paramNames)
        | IsNull x -> paramNames x
        // A list param shares the scalar params' namespace — reactivity/lease derivation needs it.
        | InParam(x, n) -> paramNames x @ [ n ]

    /// The distinct `Param` names an expression references, first-occurrence order, deduplicated.
    let paramsOf (e: ColExpr) : string list = paramNames e |> List.distinct

    /// Substitute every `Param n` bound in `env` with `Lit env.[n]` (leaving unbound params intact).
    /// The substitution witness `paramLaws` certifies against: `evalExpr` under `env` ≡ `evalExpr`
    /// over the substituted expression.
    let rec substitute (env: Map<string, Cell>) (e: ColExpr) : ColExpr =
        match e with
        | Col _
        | Lit _ -> e
        | Param n ->
            match Map.tryFind n env with
            | Some c -> Lit c
            | None -> e
        | Binary(op, a, b) -> Binary(op, substitute env a, substitute env b)
        | Not x -> Not(substitute env x)
        | Coalesce xs -> Coalesce(xs |> List.map (substitute env))
        | Case(cases, els) ->
            Case(cases |> List.map (fun (w, t) -> substitute env w, substitute env t), substitute env els)
        | Cast(ty, x) -> Cast(ty, substitute env x)
        | ApplyFn(fn, xs) -> ApplyFn(fn, xs |> List.map (substitute env))
        | InList(x, items) -> InList(substitute env x, items |> List.map (substitute env))
        | IsNull x -> IsNull(substitute env x)
        // Scalar substitution walks through but never binds a LIST param (that is
        // `substituteListParams`' job).
        | InParam(x, n) -> InParam(substitute env x, n)

    /// Substitute every `InParam(x, n)` bound in `listEnv` with `InList(x, <items as literals>)`,
    /// leaving unbound list params intact — the list-valued twin of `substitute` (Phase 91). A host
    /// binds a multi-select control's selection here; the "empty selection ⇒ no constraint" idiom
    /// is host-side policy (prune the step), exactly as for scalar params.
    let rec substituteListParams (listEnv: Map<string, Cell list>) (e: ColExpr) : ColExpr =
        match e with
        | Col _
        | Lit _
        | Param _ -> e
        | InParam(x, n) ->
            let x = substituteListParams listEnv x

            match Map.tryFind n listEnv with
            | Some items -> InList(x, items |> List.map Lit)
            | None -> InParam(x, n)
        | Binary(op, a, b) -> Binary(op, substituteListParams listEnv a, substituteListParams listEnv b)
        | Not x -> Not(substituteListParams listEnv x)
        | Coalesce xs -> Coalesce(xs |> List.map (substituteListParams listEnv))
        | Case(cases, els) ->
            Case(
                cases
                |> List.map (fun (w, t) -> substituteListParams listEnv w, substituteListParams listEnv t),
                substituteListParams listEnv els
            )
        | Cast(ty, x) -> Cast(ty, substituteListParams listEnv x)
        | ApplyFn(fn, xs) -> ApplyFn(fn, xs |> List.map (substituteListParams listEnv))
        | InList(x, items) -> InList(substituteListParams listEnv x, items |> List.map (substituteListParams listEnv))
        | IsNull x -> IsNull(substituteListParams listEnv x)

/// Pure, total derivations over a `Transform` pipeline (Phase 77) — the load-bearing helper for a
/// host that wires a filter/state value into a declarative pipeline: `paramsOf` names every param the
/// pipeline depends on, so dependency edges + reactivity + unbound-param pruning are all *derived*.
[<RequireQualifiedAccess>]
module Transform =

    /// The `Param` names a single step references (only `Filter` / `Derive` carry a `ColExpr`; every
    /// other verb contributes none). Occurrence order, with duplicates.
    let stepParamNames (t: Transform) : string list =
        match t with
        | Filter p -> ColExpr.paramNames p
        | Derive(_, e) -> ColExpr.paramNames e
        | Project _
        | GroupBy _
        | Join _
        | Window _
        | Pivot _
        | Unpivot _
        | Sort _
        | Distinct
        | Limit _
        | Union _
        | Intersect _
        | Except _ -> []

    /// Every distinct `Param` name a pipeline references, in first-occurrence order across the steps,
    /// deduplicated. Total over every step kind (incl. `Join` / `Window` / `Pivot`, which carry no
    /// param sub-expressions today and so contribute nothing).
    let paramsOf (pipeline: Transform list) : string list =
        pipeline |> List.collect stepParamNames |> List.distinct

    /// Substitute every param bound in `env` through the whole pipeline (each step's `ColExpr`).
    let substitute (env: Map<string, Cell>) (pipeline: Transform list) : Transform list =
        pipeline
        |> List.map (fun t ->
            match t with
            | Filter p -> Filter(ColExpr.substitute env p)
            | Derive(n, e) -> Derive(n, ColExpr.substitute env e)
            | other -> other)

    /// Substitute every LIST param bound in `listEnv` through the whole pipeline (Phase 91) —
    /// the list-valued twin of `substitute`.
    let substituteListParams (listEnv: Map<string, Cell list>) (pipeline: Transform list) : Transform list =
        pipeline
        |> List.map (fun t ->
            match t with
            | Filter p -> Filter(ColExpr.substituteListParams listEnv p)
            | Derive(n, e) -> Derive(n, ColExpr.substituteListParams listEnv e)
            | other -> other)

/// The reference evaluator's recoverable error envelope — names the failure, enumerates the
/// alternatives where a closed set is expected (GP5).
type EvalError =
    | UnknownColumn of name: string * available: string list
    | TypeError of detail: string
    | AggError of detail: string
    | JoinError of detail: string
    | ArityError of fn: string * expected: int * got: int
    | UnresolvedSource of ref: string
    /// An integer operation (`Add`/`Sub`/`Mul`/`Mod`/`Sum`) or a `Float`→`Int` cast produced a value
    /// outside the `int32` range. The pinned evaluator names it rather than wrapping silently — a
    /// two's-complement wrap diverges between the .NET and JS hosts, breaking three-host parity.
    | OverflowError of detail: string
    /// A `ColExpr.Param` referenced no binding in the evaluation environment (Phase 77). Names the
    /// missing param and enumerates the bound names (GP5) so a host can report or repair. Strict-Core:
    /// the reference evaluator never guesses a default — lenient "unset ⇒ no constraint" is host policy
    /// (prune the step via `Transform.paramsOf` before evaluating).
    | UnboundParam of name: string * bound: string list

/// A description of what changed in a source table between a prior evaluation and now (Phase 34) — the
/// input to the incremental `DataFrame.evalFrom`. `ColumnValuesChanged` = the cells of one existing
/// column changed (schema + row count unchanged); `RowsAppended` = rows added; `SchemaChanged` = a
/// structural change (carries the `Schema.diff` from Phase 33); `FullChange` = an opaque / wholesale
/// change. A columnar op maps to one of these (`ColumnOps.changeOf`), so an edit-stream drives
/// incremental re-evaluation.
type Change =
    | ColumnValuesChanged of column: string
    | RowsAppended
    | SchemaChanged of SchemaDelta
    | FullChange

/// The pure reference evaluator + the algebra's pinned semantics. Every host evaluator is
/// certified byte-identical to this through `Conformance.transformLaws`.
module DataFrame =

    // ---- internal row-oriented frame (the evaluator's working form) ----

    type private Frame = { Cols: Schema; Rows: Cell list list }

    let private toFrame (t: Table) : Frame =
        let n = Table.rowCount t

        let rows =
            [ for i in 0 .. n - 1 ->
                  t.Schema
                  |> List.map (fun (name, _) ->
                      match Table.tryColumn name t with
                      | Some c -> Column.cell i c
                      | None -> Null) ]

        { Cols = t.Schema; Rows = rows }

    let private ofFrame (f: Frame) : Table =
        let columns =
            f.Cols
            |> List.mapi (fun ci (name, ty) -> Column.create name ty (f.Rows |> List.map (fun row -> List.item ci row)))

        { Schema = f.Cols; Columns = columns }

    let private colIndex (cols: Schema) (name: string) : int option =
        cols |> List.tryFindIndex (fun (n, _) -> n = name)

    let private colType (cols: Schema) (name: string) : ColumnType option =
        cols |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd

    let private available (cols: Schema) : string list = cols |> List.map fst

    /// Map a `Result`-returning function over a list, short-circuiting on the first `Error` (the
    /// standard traverse — threads `EvalError` out of per-element work without an exception).
    let private traverseResult (f: 'a -> Result<'b, EvalError>) (xs: 'a list) : Result<'b list, EvalError> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | x :: rest -> f x |> Result.bind (fun y -> go (y :: acc) rest)

        go [] xs

    // ---- pinned scalar semantics ----

    /// The canonical string of a cell (float via the shared `Canon` cross-host layout) — used for
    /// pivot column names and string casts, so every host stringifies identically.
    let cellString (c: Cell) : string =
        match c with
        | Int i -> string i
        | Float f -> Canon.canonicalFloat f
        | Bool b -> if b then "true" else "false"
        | Str s -> s
        | Date s -> s
        | Timestamp s -> s
        | Null -> ""

    let private asNum (c: Cell) : float option =
        match c with
        | Int i -> Some(float i)
        | Float f -> Some f
        | _ -> None

    /// The canonical, host-deterministic token for ONE cell (Phase 41; made public in Phase 98). A raw
    /// `Cell` keys a `Map`/`Set` on each host's float comparison/equality semantics, which differ for
    /// `NaN` and `-0.0`. This normalises floats so `NaN` collapses to one bucket and `-0.0`/`0.0`
    /// coincide, via the pinned `Canon` float layout, and type-tags the token so distinct cell types
    /// never collide.
    ///
    /// Public because the delta layer (Phase 98) has to decide "is this row's content the same as
    /// before" by exactly the rule `GroupBy` / `Distinct` / `Intersect` partition by, and a second
    /// hand-written copy of a canonicalisation is how two answers to one question get shipped (the
    /// same lesson the spine's six FNV-1a copies taught). One implementation, called twice.
    let cellToken (c: Cell) : string =
        match c with
        | Int i -> "i:" + string i
        | Float f ->
            "f:"
            + (if System.Double.IsNaN f then "NaN"
               elif System.Double.IsPositiveInfinity f then "Inf"
               elif System.Double.IsNegativeInfinity f then "-Inf"
               else Canon.canonicalFloat f) // canonical layout collapses -0.0 → "0"
        | Bool b -> "b:" + (if b then "1" else "0")
        | Str s -> "s:" + s
        | Date s -> "d:" + s
        | Timestamp s -> "t:" + s
        | Null -> "n:"

    /// The canonical, host-deterministic grouping/dedup key for a row's cells — `cellToken` per cell,
    /// so `GroupBy` / `Distinct` / `Window` / `Pivot` partition identically on every host.
    let rowToken (cells: Cell list) : string list = cells |> List.map cellToken

    /// The single-string form of a row's canonical token, LENGTH-PREFIXED per cell so it is
    /// injective (Phase 98). A bare concatenation gives `["a"; "bc"]` and `["ab"; "c"]` the same
    /// string — a collision between two distinct rows, which is exactly what a token exists to
    /// prevent. Row identity and row-content comparison both key on this.
    let rowTokenString (cells: Cell list) : string =
        rowToken cells
        |> List.map (fun t -> string (String.length t) + ":" + t)
        |> String.concat ""

    let private groupKey (cells: Cell list) : string list = rowToken cells

    /// A total comparison between two *present, same-family* cells. `None` ⇒ incomparable (a type
    /// error). Numerics compare as float; strings/date/timestamp by ordinal (ISO sorts
    /// chronologically); bool false < true.
    let private compareCells (a: Cell) (b: Cell) : int option =
        match a, b with
        | (Int _ | Float _), (Int _ | Float _) -> Some(compare (asNum a) (asNum b))
        | Bool x, Bool y -> Some(compare x y)
        | Str x, Str y -> Some(System.String.CompareOrdinal(x, y))
        | Date x, Date y -> Some(System.String.CompareOrdinal(x, y))
        | Timestamp x, Timestamp y -> Some(System.String.CompareOrdinal(x, y))
        | _ -> None

    let private cellEq (a: Cell) (b: Cell) : bool =
        match a, b with
        | Null, _
        | _, Null -> false
        | _ ->
            match compareCells a b with
            | Some 0 -> true
            | _ -> false

    /// Range-check an `int64` arithmetic result against the `int32` band (Phase 39). A value outside
    /// the band is a named `OverflowError`, never a silent two's-complement wrap (which diverges
    /// .NET-vs-JS and breaks three-host parity). `ctx` names the operation for the message.
    let private checkedInt (ctx: string) (r: int64) : Result<Cell, EvalError> =
        if r >= int64 System.Int32.MinValue && r <= int64 System.Int32.MaxValue then
            Ok(Int(int r))
        else
            Error(OverflowError(ctx + " overflowed int32: " + string r))

    let private arith (op: BinOp) (a: Cell) (b: Cell) : Result<Cell, EvalError> =
        match a, b with
        | Null, _
        | _, Null -> Ok Null
        | _ ->
            match asNum a, asNum b with
            | Some x, Some y ->
                let bothInt =
                    match a, b with
                    | Int _, Int _ -> true
                    | _ -> false

                // Integer operands are exact in their `float` carrier (an `Int` holds an int32, which a
                // double represents exactly); recover them and accumulate in int64 so the range check
                // sees the true result before any int32 wrap. int32*int32 ≤ 2^62, so int64 cannot overflow.
                let xi = int64 x
                let yi = int64 y

                match op with
                | Add ->
                    if bothInt then
                        checkedInt "add" (xi + yi)
                    else
                        Ok(Float(x + y))
                | Sub ->
                    if bothInt then
                        checkedInt "sub" (xi - yi)
                    else
                        Ok(Float(x - y))
                | Mul ->
                    if bothInt then
                        checkedInt "mul" (xi * yi)
                    else
                        Ok(Float(x * y))
                | Div -> Ok(if y = 0.0 then Null else Float(x / y))
                | Mod ->
                    if not bothInt then
                        Error(TypeError "mod requires integer operands")
                    elif yi = 0L then
                        Ok Null
                    else
                        // int64 remainder avoids the .NET `Int32.MinValue % -1` OverflowException; the
                        // result is always within int32, so the check is a no-op safeguard.
                        checkedInt "mod" (xi % yi)
                | _ -> Error(TypeError "not an arithmetic operator")
            | _ -> Error(TypeError "arithmetic on a non-numeric operand")

    let private comparison (op: BinOp) (a: Cell) (b: Cell) : Result<Cell, EvalError> =
        match a, b with
        | Null, _
        | _, Null -> Ok Null
        | _ ->
            match compareCells a b with
            | None -> Error(TypeError "comparison between incompatible types")
            | Some c ->
                let r =
                    match op with
                    | Eq -> c = 0
                    | Ne -> c <> 0
                    | Lt -> c < 0
                    | Le -> c <= 0
                    | Gt -> c > 0
                    | Ge -> c >= 0
                    | _ -> false

                Ok(Bool r)

    /// Kleene three-valued AND/OR over `Bool`/`Null` operands.
    let private logical (op: BinOp) (a: Cell) (b: Cell) : Result<Cell, EvalError> =
        let asBool =
            function
            | Bool b -> Ok(Some b)
            | Null -> Ok None
            | _ -> Error(TypeError "logical operator on a non-bool operand")

        match asBool a, asBool b with
        | Error e, _
        | _, Error e -> Error e
        | Ok x, Ok y ->
            match op with
            | And ->
                match x, y with
                | Some false, _
                | _, Some false -> Ok(Bool false)
                | Some true, Some true -> Ok(Bool true)
                | _ -> Ok Null
            | Or ->
                match x, y with
                | Some true, _
                | _, Some true -> Ok(Bool true)
                | Some false, Some false -> Ok(Bool false)
                | _ -> Ok Null
            | _ -> Error(TypeError "not a logical operator")

    /// Ordinal substring predicates (Phase 90). Null propagates (matching `comparison`); a
    /// non-string operand is a typed error. Ordinal is the cross-host pin: JS `includes` /
    /// `startsWith` / `endsWith` are code-unit-wise over the same UTF-16.
    let private stringPred (op: BinOp) (a: Cell) (b: Cell) : Result<Cell, EvalError> =
        match a, b with
        | Null, _
        | _, Null -> Ok Null
        | Str s, Str t ->
            let r =
                match op with
                | Contains -> s.Contains(t, System.StringComparison.Ordinal)
                | StartsWith -> s.StartsWith(t, System.StringComparison.Ordinal)
                | EndsWith -> s.EndsWith(t, System.StringComparison.Ordinal)
                | _ -> false

            Ok(Bool r)
        | _ -> Error(TypeError "string predicate on a non-string operand")

    /// Days since the civil epoch (1970-01-01 = 0) for the first 10 chars (`YYYY-MM-DD`) of a
    /// date-like cell — days-from-civil as pure integer math (Phase 90, `DateDiffDays`). No host
    /// date library: determinism + a trivial TS mirror.
    let private civilDays (c: Cell) : Result<int, EvalError> =
        match c with
        | Date s
        | Timestamp s
        | Str s ->
            let bad () =
                Error(TypeError("dateDiffDays: '" + s + "' is not YYYY-MM-DD[...]"))

            if s.Length < 10 || s.[4] <> '-' || s.[7] <> '-' then
                bad ()
            else
                let part (lo: int) (len: int) =
                    match System.Int32.TryParse(s.Substring(lo, len)) with
                    | true, v -> Some v
                    | _ -> None

                match part 0 4, part 5 2, part 8 2 with
                | Some y, Some m, Some d ->
                    let y = if m <= 2 then y - 1 else y
                    let era = (if y >= 0 then y else y - 399) / 400
                    let yoe = y - era * 400
                    let mp = (m + 9) % 12
                    let doy = (153 * mp + 2) / 5 + d - 1
                    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy
                    Ok(era * 146097 + doe - 719468)
                | _ -> bad ()
        | _ -> Error(TypeError "dateDiffDays expects date/timestamp/string operands")

    let private castCell (ty: ColumnType) (c: Cell) : Result<Cell, EvalError> =
        match c with
        | Null -> Ok Null
        | _ ->
            match ty with
            | StringType -> Ok(Str(cellString c))
            | FloatType ->
                match asNum c with
                | Some f -> Ok(Float f)
                | None ->
                    match c with
                    | Str s ->
                        match
                            System.Double.TryParse(
                                s,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        with
                        | true, f -> Ok(Float f)
                        | _ -> Error(TypeError("cannot cast '" + s + "' to float"))
                    | _ -> Error(TypeError "cannot cast to float")
            | IntType ->
                match c with
                | Int _ -> Ok c
                | Float f ->
                    // `int f` is undefined for NaN/±∞/out-of-range and diverges .NET-vs-JS (Phase 39);
                    // only an in-range finite float truncates toward zero.
                    if System.Double.IsNaN f || System.Double.IsInfinity f then
                        Error(TypeError "cannot cast a non-finite float to int")
                    elif f < float System.Int32.MinValue || f > float System.Int32.MaxValue then
                        Error(OverflowError("float→int cast out of int32 range: " + Canon.render (JFloat f)))
                    else
                        Ok(Int(int f))
                | Bool b -> Ok(Int(if b then 1 else 0))
                | Str s ->
                    match System.Int32.TryParse s with
                    | true, i -> Ok(Int i)
                    | _ -> Error(TypeError("cannot cast '" + s + "' to int"))
                | _ -> Error(TypeError "cannot cast to int")
            | BoolType ->
                match c with
                | Bool _ -> Ok c
                | Int i -> Ok(Bool(i <> 0))
                | _ -> Error(TypeError "cannot cast to bool")
            | DateType ->
                match c with
                | Date _ -> Ok c
                | Str s -> Ok(Date s)
                | _ -> Error(TypeError "cannot cast to date")
            | TimestampType ->
                match c with
                | Timestamp _ -> Ok c
                | Str s -> Ok(Timestamp s)
                | _ -> Error(TypeError "cannot cast to timestamp")

    /// Round half away from zero — host-independent (not `Math.Round`'s banker's rounding, which
    /// diverges between .NET and JS). Pinned so the three evaluators agree.
    let private roundHalfAway (x: float) : float =
        if x >= 0.0 then floor (x + 0.5) else ceil (x - 0.5)

    let private applyScalar (fn: ScalarFn) (args: Cell list) : Result<Cell, EvalError> =
        let arity n =
            if List.length args = n then
                Ok()
            else
                Error(ArityError(string fn, n, List.length args))

        let unary f =
            arity 1
            |> Result.bind (fun () ->
                match args.[0] with
                | Null -> Ok Null
                | c -> f c)

        match fn with
        | Abs ->
            unary (fun c ->
                match c with
                | Int i -> Ok(Int(abs i))
                | Float f -> Ok(Float(abs f))
                | _ -> Error(TypeError "abs of a non-numeric"))
        | Round ->
            unary (fun c ->
                match asNum c with
                | Some f -> Ok(Float(roundHalfAway f))
                | None -> Error(TypeError "round of a non-numeric"))
        | Floor ->
            unary (fun c ->
                match asNum c with
                | Some f -> Ok(Float(floor f))
                | None -> Error(TypeError "floor of a non-numeric"))
        | Ceil ->
            unary (fun c ->
                match asNum c with
                | Some f -> Ok(Float(ceil f))
                | None -> Error(TypeError "ceil of a non-numeric"))
        | Length ->
            unary (fun c ->
                match c with
                | Str s -> Ok(Int s.Length)
                | _ -> Error(TypeError "length of a non-string"))
        | Lower ->
            unary (fun c ->
                match c with
                | Str s -> Ok(Str(s.ToLowerInvariant()))
                | _ -> Error(TypeError "lower of a non-string"))
        | Upper ->
            unary (fun c ->
                match c with
                | Str s -> Ok(Str(s.ToUpperInvariant()))
                | _ -> Error(TypeError "upper of a non-string"))
        | Substr ->
            arity 3
            |> Result.bind (fun () ->
                match args.[0], args.[1], args.[2] with
                | Null, _, _ -> Ok Null
                | Str s, Int start, Int len ->
                    let start = max 0 start
                    let start = min start s.Length
                    let len = max 0 (min len (s.Length - start))
                    Ok(Str(s.Substring(start, len)))
                | _ -> Error(TypeError "substr expects (string, int, int)"))
        | DatePart ->
            arity 2
            |> Result.bind (fun () ->
                match args.[0], args.[1] with
                | _, Null -> Ok Null
                | Str part, (Date s | Timestamp s | Str s) ->
                    // ISO-8601: YYYY-MM-DD[Thh:mm:ss]. Slice the requested part.
                    let slice (lo: int) (len: int) =
                        if s.Length >= lo + len then
                            match System.Int32.TryParse(s.Substring(lo, len)) with
                            | true, v -> Ok(Int v)
                            | _ -> Error(TypeError("datePart: unparseable component in '" + s + "'"))
                        else
                            Error(TypeError("datePart: '" + s + "' too short for " + part))

                    match part with
                    | "year" -> slice 0 4
                    | "month" -> slice 5 2
                    | "day" -> slice 8 2
                    | other -> Error(TypeError("datePart: unknown part '" + other + "'"))
                | _ -> Error(TypeError "datePart expects (string part, date/timestamp/string)"))
        | Concat ->
            // Variadic; any null arg propagates (compose Coalesce for treat-as-empty). Non-string
            // args stringify via the SAME rendering as `Cast StringType` — no cast noise needed.
            if List.isEmpty args then
                Error(ArityError("Concat", 1, 0))
            elif args |> List.exists ((=) Null) then
                Ok Null
            else
                Ok(Str(args |> List.map cellString |> String.concat ""))
        | Trim ->
            unary (fun c ->
                match c with
                // Pinned ASCII set — NOT Char.IsWhiteSpace / JS trim(), which disagree (U+0085 et al.).
                | Str str -> Ok(Str(str.Trim([| ' '; '\t'; '\r'; '\n' |])))
                | _ -> Error(TypeError "trim of a non-string"))
        | Replace ->
            arity 3
            |> Result.bind (fun () ->
                match args.[0], args.[1], args.[2] with
                | Null, _, _
                | _, Null, _
                | _, _, Null -> Ok Null
                | Str subj, Str find, Str repl ->
                    // Pinned: empty `find` returns the subject unchanged (.NET throws; JS interleaves).
                    if find = "" then
                        Ok(Str subj)
                    else
                        Ok(Str(subj.Replace(find, repl)))
                | _ -> Error(TypeError "replace expects (string, string, string)"))
        | DateDiffDays ->
            arity 2
            |> Result.bind (fun () ->
                match args.[0], args.[1] with
                | Null, _
                | _, Null -> Ok Null
                | a, b ->
                    civilDays a
                    |> Result.bind (fun da -> civilDays b |> Result.map (fun db -> Int(db - da))))
        | Sqrt ->
            unary (fun c ->
                match asNum c with
                // A negative root is undefined over the reals; answer `Null`, the same way `Div` by
                // zero does. A `NaN` would not survive the canonical wire at all.
                | Some f -> Ok(if f < 0.0 then Null else Float(sqrt f))
                | None -> Error(TypeError "sqrt of a non-numeric"))
        | Least
        | Greatest ->
            // Variadic (>= 1). Any null propagates, matching `Concat`; incomparable operands are a
            // typed error via the shared cell ordering, so the pinned comparison is the single source.
            if List.isEmpty args then
                Error(ArityError((if fn = Least then "Least" else "Greatest"), 1, 0))
            elif args |> List.exists ((=) Null) then
                Ok Null
            else
                let rec go (best: Cell) =
                    function
                    | [] -> Ok best
                    | c :: rest ->
                        match compareCells best c with
                        | None -> Error(TypeError "least/greatest over incompatible types")
                        | Some k -> go (if (fn = Least) = (k <= 0) then best else c) rest

                go (List.head args) (List.tail args)
        | IndexOf ->
            arity 2
            |> Result.bind (fun () ->
                match args.[0], args.[1] with
                | Null, _
                | _, Null -> Ok Null
                // Ordinal, 0-based, `-1` when absent — the same cross-host pin as `Contains`
                // (JS `indexOf` is code-unit-wise over the same UTF-16).
                | Str subj, Str needle -> Ok(Int(subj.IndexOf(needle, System.StringComparison.Ordinal)))
                | _ -> Error(TypeError "indexOf expects (string, string)"))

    /// Evaluate a `ColExpr` against one row (resolved through `cols`), reading `Param`s from the
    /// evaluation environment `env` (Phase 77). A `Param` hit resolves to its bound `Cell`; a miss is
    /// a strict `UnboundParam` naming the param + the bound set (GP4/GP5) — never a throw, never a
    /// silent default.
    let rec private evalExpr
        (env: Map<string, Cell>)
        (cols: Schema)
        (row: Cell list)
        (e: ColExpr)
        : Result<Cell, EvalError> =
        match e with
        | Col name ->
            match colIndex cols name with
            | Some i -> Ok(List.item i row)
            | None -> Error(UnknownColumn(name, available cols))
        | Lit c -> Ok c
        | Param name ->
            match Map.tryFind name env with
            | Some c -> Ok c
            | None -> Error(UnboundParam(name, env |> Map.toList |> List.map fst))
        | Binary(op, a, b) ->
            evalExpr env cols row a
            |> Result.bind (fun av ->
                evalExpr env cols row b
                |> Result.bind (fun bv ->
                    match op with
                    | Add
                    | Sub
                    | Mul
                    | Div
                    | Mod -> arith op av bv
                    | Eq
                    | Ne
                    | Lt
                    | Le
                    | Gt
                    | Ge -> comparison op av bv
                    | And
                    | Or -> logical op av bv
                    | Contains
                    | StartsWith
                    | EndsWith -> stringPred op av bv))
        | Not inner ->
            evalExpr env cols row inner
            |> Result.bind (function
                | Bool b -> Ok(Bool(not b))
                | Null -> Ok Null
                | _ -> Error(TypeError "not of a non-bool"))
        | Coalesce exprs ->
            let rec go =
                function
                | [] -> Ok Null
                | x :: rest ->
                    evalExpr env cols row x
                    |> Result.bind (function
                        | Null -> go rest
                        | c -> Ok c)

            go exprs
        | Case(cases, elseExpr) ->
            let rec go =
                function
                | [] -> evalExpr env cols row elseExpr
                | (whenE, thenE) :: rest ->
                    evalExpr env cols row whenE
                    |> Result.bind (function
                        | Bool true -> evalExpr env cols row thenE
                        | _ -> go rest)

            go cases
        | Cast(ty, inner) -> evalExpr env cols row inner |> Result.bind (castCell ty)
        | InList(subject, items) ->
            evalExpr env cols row subject
            |> Result.bind (fun sv ->
                match sv with
                | Null -> Ok Null
                | _ ->
                    // SQL three-valued membership: any equal => true; no match seen a null => null.
                    let rec go sawNull =
                        function
                        | [] -> Ok(if sawNull then Null else Bool false)
                        | it :: rest ->
                            evalExpr env cols row it
                            |> Result.bind (fun iv ->
                                match iv with
                                | Null -> go true rest
                                | _ ->
                                    match compareCells sv iv with
                                    | Some 0 -> Ok(Bool true)
                                    | Some _ -> go sawNull rest
                                    | None -> Error(TypeError "in: comparison between incompatible types"))

                    go false items)
        | IsNull inner ->
            evalExpr env cols row inner
            |> Result.map (fun v ->
                match v with
                | Null -> Bool true
                | _ -> Bool false)
        | InParam(_, name) ->
            // List params resolve by substitution (`substituteListParams`) BEFORE evaluation —
            // one that reaches the evaluator is unbound, same strictness as a scalar `Param`.
            Error(UnboundParam(name, env |> Map.toList |> List.map fst))
        | ApplyFn(fn, args) ->
            let rec evalArgs acc =
                function
                | [] -> Ok(List.rev acc)
                | a :: rest -> evalExpr env cols row a |> Result.bind (fun v -> evalArgs (v :: acc) rest)

            evalArgs [] args |> Result.bind (applyScalar fn)

    // ---- type inference for derived/melted columns ----

    /// Infer a column type from its cells — the first present cell's type, else `StringType`
    /// (an all-null derived column has no observable type; `string` is the safe default).
    let private inferType (cells: Cell list) : ColumnType =
        cells |> List.tryPick Cell.typeOf |> Option.defaultValue StringType

    // ---- aggregates (Phase 36: the pinned semantics live in `Column.aggregate`; the evaluator calls it) ----

    /// Lift a `Column.AggregateError` into the evaluator's `EvalError` envelope. An overflow maps to the
    /// pinned `OverflowError` (so the existing "Sum overflow is a named OverflowError" contract holds); a
    /// type incompatibility maps to the `AggError` case, naming the expected types.
    let private aggErr (e: AggregateError) : EvalError =
        match e with
        | AggregateOverflow d -> OverflowError d
        | IncompatibleAggType(fn, ct, expected) ->
            AggError(
                "aggregate "
                + fn
                + " over a "
                + ct
                + " column (expected "
                + String.concat "/" expected
                + ")"
            )

    /// Compute one aggregate over a cell list of the given source type — the evaluator's adapter onto the
    /// public `Column.aggregate` (single source of truth), threading the `EvalError` envelope.
    let private aggCells (fn: AggFn) (srcType: ColumnType) (cells: Cell list) : Result<Cell, EvalError> =
        Column.aggregate fn (Column.create "" srcType cells) |> Result.mapError aggErr

    let private aggType (fn: AggFn) (srcType: ColumnType) : ColumnType = Column.aggType fn srcType

    // ---- sort (pinned: stable; nulls last regardless of direction) ----

    let private rowKeyCompare (cols: Schema) (by: (string * SortDir) list) (r1: Cell list) (r2: Cell list) : int =
        let rec go =
            function
            | [] -> 0
            | (name, dir) :: rest ->
                match colIndex cols name with
                | None -> go rest
                | Some i ->
                    let a = List.item i r1
                    let b = List.item i r2

                    let c =
                        match Cell.isNull a, Cell.isNull b with
                        | true, true -> 0
                        | true, false -> 1 // null sorts last
                        | false, true -> -1
                        | false, false ->
                            match compareCells a b with
                            | Some c -> if dir = Asc then c else -c
                            | None -> 0

                    if c <> 0 then c else go rest

        go by

    // ---- per-verb evaluation ----

    let private evalFilter
        (env: Map<string, Cell>)
        (cols: Schema)
        (rows: Cell list list)
        (pred: ColExpr)
        : Result<Cell list list, EvalError> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | row :: rest ->
                match evalExpr env cols row pred with
                | Ok(Bool true) -> go (row :: acc) rest
                | Ok _ -> go acc rest
                | Error e -> Error e

        go [] rows

    let private evalProject (f: Frame) (pairs: (string * string) list) : Result<Frame, EvalError> =
        let resolve (src, out) =
            match colIndex f.Cols src with
            | None -> Error(UnknownColumn(src, available f.Cols))
            | Some i -> Ok(out, snd (List.item i f.Cols), i)

        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | p :: rest -> resolve p |> Result.bind (fun r -> go (r :: acc) rest)

        go [] pairs
        |> Result.map (fun resolved ->
            { Cols = resolved |> List.map (fun (o, ty, _) -> o, ty)
              Rows =
                f.Rows
                |> List.map (fun row -> resolved |> List.map (fun (_, _, i) -> List.item i row)) })

    let private evalDerive
        (env: Map<string, Cell>)
        (f: Frame)
        (name: string)
        (expr: ColExpr)
        : Result<Frame, EvalError> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | row :: rest -> evalExpr env f.Cols row expr |> Result.bind (fun c -> go (c :: acc) rest)

        go [] f.Rows
        |> Result.map (fun newCells ->
            let ty = inferType newCells

            match colIndex f.Cols name with
            | Some i ->
                { Cols = f.Cols |> List.mapi (fun j (n, t) -> if j = i then n, ty else n, t)
                  Rows =
                    List.map2 (fun row c -> row |> List.mapi (fun j cell -> if j = i then c else cell)) f.Rows newCells }
            | None ->
                { Cols = f.Cols @ [ name, ty ]
                  Rows = List.map2 (fun row c -> row @ [ c ]) f.Rows newCells })

    let private evalGroupBy (f: Frame) (keys: string list) (aggs: Agg list) : Result<Frame, EvalError> =
        let keyIdx = keys |> List.map (fun k -> colIndex f.Cols k, k)

        match keyIdx |> List.tryPick (fun (i, k) -> if Option.isNone i then Some k else None) with
        | Some missing -> Error(UnknownColumn(missing, available f.Cols))
        | None ->
            let idxs = keyIdx |> List.map (fun (i, _) -> Option.get i)

            let keyOf row =
                idxs |> List.map (fun i -> List.item i row)

            // group, preserving first-appearance order of keys; key the map on the canonical token
            // (Phase 41) so float keys group host-identically, but carry the original key cells for output
            let order, groups =
                f.Rows
                |> List.fold
                    (fun (order, map: Map<string list, Cell list * Cell list list>) row ->
                        let k = keyOf row
                        let kt = groupKey k

                        match Map.tryFind kt map with
                        | Some(k0, rows) -> order, Map.add kt (k0, rows @ [ row ]) map
                        | None -> order @ [ kt ], Map.add kt (k, [ row ]) map)
                    ([], Map.empty)

            // resolve each agg's source column + type
            let resolveAgg (a: Agg) =
                match colType f.Cols a.Of with
                | Some ty -> Ok(a, ty, colIndex f.Cols a.Of |> Option.get)
                | None -> Error(UnknownColumn(a.Of, available f.Cols))

            let rec resAll acc =
                function
                | [] -> Ok(List.rev acc)
                | a :: rest -> resolveAgg a |> Result.bind (fun r -> resAll (r :: acc) rest)

            resAll [] aggs
            |> Result.bind (fun resolvedAggs ->
                let keyCols = keys |> List.map (fun k -> k, colType f.Cols k |> Option.get)
                let aggCols = resolvedAggs |> List.map (fun (a, ty, _) -> a.Name, aggType a.Fn ty)

                order
                |> traverseResult (fun kt ->
                    let k, grp = Map.find kt groups

                    resolvedAggs
                    |> traverseResult (fun (a, ty, ci) -> aggCells a.Fn ty (grp |> List.map (List.item ci)))
                    |> Result.map (fun aggVals -> k @ aggVals))
                |> Result.map (fun rows ->
                    { Cols = keyCols @ aggCols
                      Rows = rows }))

    let private evalSort (f: Frame) (by: (string * SortDir) list) : Frame =
        { f with
            Rows = f.Rows |> List.sortWith (rowKeyCompare f.Cols by) }

    let private evalDistinct (f: Frame) : Frame =
        // dedup on the canonical token (Phase 41) so float-bearing rows dedup host-identically
        let rec go (seen: Set<string list>) acc =
            function
            | [] -> List.rev acc
            | row :: rest ->
                let kt = groupKey row

                if Set.contains kt seen then
                    go seen acc rest
                else
                    go (Set.add kt seen) (row :: acc) rest

        { f with Rows = go Set.empty [] f.Rows }

    let private evalLimit (f: Frame) (n: int) (offset: int) : Frame =
        let skipped = f.Rows |> List.skip (min (max 0 offset) (List.length f.Rows))

        { f with
            Rows = skipped |> List.truncate (max 0 n) }

    let private evalJoin
        (f: Frame)
        (right: Frame)
        (on: (string * string) list)
        (how: JoinKind)
        : Result<Frame, EvalError> =
        let leftIdx = on |> List.map (fun (l, _) -> colIndex f.Cols l, l)
        let rightIdx = on |> List.map (fun (_, r) -> colIndex right.Cols r, r)

        let missing =
            (leftIdx @ rightIdx)
            |> List.tryPick (fun (i, n) -> if Option.isNone i then Some n else None)

        match missing with
        | Some n -> Error(UnknownColumn(n, available f.Cols @ available right.Cols))
        | None ->
            let li = leftIdx |> List.map (fst >> Option.get)
            let ri = rightIdx |> List.map (fst >> Option.get)

            let keyMatch (lr: Cell list) (rr: Cell list) =
                List.forall2 (fun i j -> cellEq (List.item i lr) (List.item j rr)) li ri

            // The combining joins (Inner / Left / Right / Outer) — left cols ++ right cols.
            let combiningJoin () =
                // output schema: left cols ++ right cols (collisions suffixed _right)
                let leftNames = available f.Cols |> Set.ofList

                let rightCols =
                    right.Cols
                    |> List.map (fun (n, ty) -> (if Set.contains n leftNames then n + "_right" else n), ty)

                let outCols = f.Cols @ rightCols
                let leftNulls = f.Cols |> List.map (fun _ -> Null)
                let rightNulls = right.Cols |> List.map (fun _ -> Null)

                let combine lr rr = lr @ rr

                let leftSide =
                    f.Rows
                    |> List.collect (fun lr ->
                        let matches = right.Rows |> List.filter (keyMatch lr)

                        match matches, how with
                        | [], (Left | Outer) -> [ combine lr rightNulls ]
                        | [], (Inner | Right) -> []
                        | ms, _ -> ms |> List.map (combine lr))

                // right-only unmatched rows (for Right / Outer)
                let rightOnly =
                    match how with
                    | Right
                    | Outer ->
                        right.Rows
                        |> List.filter (fun rr -> not (f.Rows |> List.exists (fun lr -> keyMatch lr rr)))
                        |> List.map (fun rr -> combine leftNulls rr)
                    | _ -> []

                { Cols = outCols
                  Rows = leftSide @ rightOnly }

            match how with
            // Phase 101 — the filtering joins: the LEFT schema only, each qualifying left row once,
            // input order and multiplicity preserved (no fan-out, no right columns to project away).
            | Semi
            | Anti ->
                let matched lr = right.Rows |> List.exists (keyMatch lr)

                Ok
                    { Cols = f.Cols
                      Rows = f.Rows |> List.filter (fun lr -> matched lr = (how = Semi)) }
            | Inner
            | Left
            | Right
            | Outer -> Ok(combiningJoin ())

    let private evalUnion (f: Frame) (other: Frame) : Result<Frame, EvalError> =
        if available f.Cols <> available other.Cols then
            Error(JoinError "union requires matching column names")
        else
            Ok { f with Rows = f.Rows @ other.Rows }

    /// `Intersect` / `Except` (Phase 101) — the multiset set-ops, keyed on the SAME canonical row
    /// token `Distinct` dedups on (Phase 41), so membership is host-identical and `Null` is a value
    /// that matches itself. `keepPresent` selects intersect (`true`) from except (`false`). The
    /// left's order and duplicate multiplicity survive, so `· Distinct` recovers the SQL set forms.
    let private evalSetOp (verb: string) (keepPresent: bool) (f: Frame) (other: Frame) : Result<Frame, EvalError> =
        if available f.Cols <> available other.Cols then
            Error(JoinError(verb + " requires matching column names"))
        else
            let rightKeys = other.Rows |> List.map groupKey |> Set.ofList

            Ok
                { f with
                    Rows =
                        f.Rows
                        |> List.filter (fun row -> Set.contains (groupKey row) rightKeys = keepPresent) }

    /// Does the window function read the `Of` column at all? The positional/ranking family
    /// (`RowNumber` / the three ranks / `NTile`) is computed entirely from the ORDER key, so its
    /// `Of` is unused and an unresolvable name there is not an error (Phase 101 extends the
    /// pre-existing `RowNumber`/`Rank` carve-out to the ranking family it grew into).
    let private windowReadsOf (fn: WindowFn) : bool =
        match fn with
        | RowNumber
        | Rank
        | DenseRank
        | CompetitionRank
        | NTile _ -> false
        | Lag
        | Lead
        | CumulSum
        | CumulMax
        | CumulMin
        | RollingMean
        | RollingSum -> true

    let private evalWindow (f: Frame) (spec: WindowSpec) : Result<Frame, EvalError> =
        match spec.Fn, colIndex f.Cols spec.Of with
        | NTile b, _ when b < 1 -> Error(TypeError("ntile expects at least 1 bucket, got " + string b))
        | fn, None when windowReadsOf fn -> Error(UnknownColumn(spec.Of, available f.Cols))
        | _ ->
            let partIdx = spec.PartitionBy |> List.choose (colIndex f.Cols)

            let partKey row =
                partIdx |> List.map (fun i -> List.item i row)

            // tag each row with its original position so we can restore input order after windowing
            let tagged = f.Rows |> List.mapi (fun i row -> i, row)

            // partition (first-appearance order), then order within partition
            let partitions =
                tagged
                |> List.fold
                    (fun (order, map: Map<string list, (int * Cell list) list>) (i, row) ->
                        // partition on the canonical token (Phase 41) — float partition keys group
                        // host-identically; the partition cells are not needed for output (rows are
                        // restored to input order by their tag)
                        let k = groupKey (partKey row)

                        match Map.tryFind k map with
                        | Some rs -> order, Map.add k (rs @ [ i, row ]) map
                        | None -> order @ [ k ], Map.add k [ i, row ] map)
                    ([], Map.empty)
                |> snd

            let ofIdx = colIndex f.Cols spec.Of

            let valueAt row =
                match ofIdx with
                | Some i -> List.item i row
                | None -> Null

            let computed =
                partitions
                |> Map.toList
                |> List.collect (fun (_, members) ->
                    let ordered =
                        members
                        |> List.sortWith (fun (_, a) (_, b) -> rowKeyCompare f.Cols spec.OrderBy a b)

                    let vals = ordered |> List.map (snd >> valueAt)

                    let outs =
                        match spec.Fn with
                        | RowNumber -> ordered |> List.mapi (fun i _ -> Int(i + 1))
                        | Rank
                        | DenseRank ->
                            // dense-ish rank by the order key: ties (equal order keys) share a rank
                            ordered
                            |> List.mapi (fun i (_, row) ->
                                if i = 0 then
                                    1
                                else
                                    let prev = ordered |> List.item (i - 1) |> snd

                                    if rowKeyCompare f.Cols spec.OrderBy prev row = 0 then
                                        0
                                    else
                                        1)
                            |> List.scan (+) 0
                            |> List.tail
                            |> List.map Int
                        | Lag -> Null :: (vals |> List.truncate (max 0 (List.length vals - 1)))
                        | Lead -> (vals |> List.skip (min 1 (List.length vals))) @ [ Null ]
                        | CumulSum ->
                            vals
                            |> List.scan
                                (fun (acc: float) v ->
                                    match asNum v with
                                    | Some x -> acc + x
                                    | None -> acc)
                                0.0
                            |> List.tail
                            |> List.map Float
                        | RollingMean
                        | RollingSum ->
                            // trailing window of up to 3 (current + 2 preceding), present values only
                            vals
                            |> List.mapi (fun i _ ->
                                let lo = max 0 (i - 2)
                                let window = vals |> List.skip lo |> List.truncate (i - lo + 1)
                                let nums = window |> List.choose asNum

                                if List.isEmpty nums then Null
                                elif spec.Fn = RollingSum then Float(List.sum nums)
                                else Float(List.sum nums / float (List.length nums)))
                        // Phase 101 — SQL RANK(): a tied block shares its LOWEST rank and the next
                        // distinct order key skips by the block's size (1, 1, 3 — where the dense
                        // `Rank`/`DenseRank` above give 1, 1, 2).
                        | CompetitionRank ->
                            ordered
                            |> List.fold
                                (fun (acc, i, cur, prev) (_, row) ->
                                    let r =
                                        match prev with
                                        | Some p when rowKeyCompare f.Cols spec.OrderBy p row = 0 -> cur
                                        | _ -> i + 1

                                    (Int r :: acc), i + 1, r, Some row)
                                ([], 0, 0, None)
                            |> fun (acc, _, _, _) -> List.rev acc
                        // Phase 101 — SQL NTILE(n): the first `count % n` buckets take one extra row.
                        | NTile buckets ->
                            let count = List.length ordered
                            let small = count / buckets
                            let big = count % buckets
                            // rows [0, big*(small+1)) fill the oversized buckets; the rest the rest.
                            let bigRows = big * (small + 1)

                            ordered
                            |> List.mapi (fun i _ ->
                                if i < bigRows then
                                    Int(i / (small + 1) + 1)
                                else
                                    Int(big + (i - bigRows) / small + 1))
                        // Phase 101 — running max/min over present values; nulls carry the prior
                        // value forward, so a leading run of nulls is `Null` (never a seeded 0).
                        | CumulMax
                        | CumulMin ->
                            let pick (acc: Cell) (v: Cell) =
                                match acc, v with
                                | _, Null -> acc
                                | Null, _ -> v
                                | _ ->
                                    match compareCells acc v with
                                    | Some c -> if (spec.Fn = CumulMin) = (c <= 0) then acc else v
                                    | None -> acc

                            vals |> List.scan pick Null |> List.tail

                    List.map2 (fun (i, _) out -> i, out) ordered outs)
                |> List.sortBy fst
                |> List.map snd

            let ty =
                match spec.Fn with
                | RowNumber
                | Rank
                | DenseRank
                | CompetitionRank
                | NTile _ -> IntType
                | CumulSum
                | RollingMean
                | RollingSum -> FloatType
                | Lag
                | Lead
                // The running extremes keep the source type, exactly as `AggFn.Min`/`Max` do.
                | CumulMax
                | CumulMin -> colType f.Cols spec.Of |> Option.defaultValue StringType

            Ok
                { Cols = f.Cols @ [ spec.As, ty ]
                  Rows = List.map2 (fun row out -> row @ [ out ]) f.Rows computed }

    let private evalPivot (f: Frame) (spec: PivotSpec) : Result<Frame, EvalError> =
        let need name =
            match colIndex f.Cols name with
            | Some i -> Ok i
            | None -> Error(UnknownColumn(name, available f.Cols))

        let rec resolveIdx acc =
            function
            | [] -> Ok(List.rev acc)
            | n :: rest -> need n |> Result.bind (fun i -> resolveIdx (i :: acc) rest)

        resolveIdx [] spec.Index
        |> Result.bind (fun idxIdx ->
            need spec.On
            |> Result.bind (fun onIdx ->
                need spec.Values
                |> Result.bind (fun valIdx ->
                    let valType = snd (List.item valIdx f.Cols)

                    let indexKey row =
                        idxIdx |> List.map (fun i -> List.item i row)

                    // distinct on-values (sorted by canonical string for a deterministic column order)
                    let onValues =
                        f.Rows
                        |> List.map (fun row -> List.item onIdx row)
                        |> List.filter (fun c -> not (Cell.isNull c))
                        |> List.distinct
                        |> List.sortBy cellString

                    // index groups, first-appearance order; dedup on the canonical token (Phase 41) but
                    // carry the original index-key cells for the output rows
                    let order =
                        f.Rows
                        |> List.fold
                            (fun (ord, seen: Set<string list>) row ->
                                let k = indexKey row
                                let kt = groupKey k

                                if Set.contains kt seen then
                                    ord, seen
                                else
                                    ord @ [ k, kt ], Set.add kt seen)
                            ([], Set.empty)
                        |> fst

                    let idxCols = spec.Index |> List.map (fun n -> n, colType f.Cols n |> Option.get)

                    let pivotCols =
                        onValues |> List.map (fun ov -> cellString ov, aggType spec.Agg valType)

                    order
                    |> traverseResult (fun (k, kt) ->
                        let cellsFor ov =
                            let matching =
                                f.Rows
                                |> List.filter (fun row ->
                                    groupKey (indexKey row) = kt && cellEq (List.item onIdx row) ov)
                                |> List.map (fun row -> List.item valIdx row)

                            aggCells spec.Agg valType matching

                        onValues |> traverseResult cellsFor |> Result.map (fun vals -> k @ vals))
                    |> Result.map (fun rows ->
                        { Cols = idxCols @ pivotCols
                          Rows = rows }))))

    let private evalUnpivot (f: Frame) (idVars: string list) (valueVars: string list) : Result<Frame, EvalError> =
        let need name =
            match colIndex f.Cols name with
            | Some i -> Ok i
            | None -> Error(UnknownColumn(name, available f.Cols))

        let rec resolve acc =
            function
            | [] -> Ok(List.rev acc)
            | n :: rest -> need n |> Result.bind (fun i -> resolve (i :: acc) rest)

        resolve [] idVars
        |> Result.bind (fun idIdx ->
            resolve [] valueVars
            |> Result.map (fun valIdx ->
                let idCols = idVars |> List.map (fun n -> n, colType f.Cols n |> Option.get)

                let valType =
                    valueVars |> List.tryPick (colType f.Cols) |> Option.defaultValue StringType

                let cols = idCols @ [ "variable", StringType; "value", valType ]

                let rows =
                    f.Rows
                    |> List.collect (fun row ->
                        let idCells = idIdx |> List.map (fun i -> List.item i row)

                        List.map2 (fun name vi -> idCells @ [ Str name; List.item vi row ]) valueVars valIdx)

                { Cols = cols; Rows = rows }))

    // ---- pipeline driver ----

    /// Evaluate a `DataSource` to a concrete `Table`, resolving a `Ref` through `resolve`.
    let evalSource (resolve: string -> Result<Table, EvalError>) (src: DataSource) : Result<Table, EvalError> =
        match src with
        | Embedded t -> Ok t
        | Ref r -> resolve r

    let private evalStep
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (f: Frame)
        (t: Transform)
        : Result<Frame, EvalError> =
        match t with
        | Filter pred ->
            evalFilter env f.Cols f.Rows pred
            |> Result.map (fun rows -> { f with Rows = rows })
        | Project pairs -> evalProject f pairs
        | Derive(name, expr) -> evalDerive env f name expr
        | GroupBy(keys, aggs) -> evalGroupBy f keys aggs
        | Sort by -> Ok(evalSort f by)
        | Distinct -> Ok(evalDistinct f)
        | Limit(n, offset) -> Ok(evalLimit f n offset)
        | Window spec -> evalWindow f spec
        | Pivot spec -> evalPivot f spec
        | Unpivot(idVars, valueVars) -> evalUnpivot f idVars valueVars
        | Join(right, on, how) ->
            evalSource resolve right
            |> Result.map toFrame
            |> Result.bind (fun rf -> evalJoin f rf on how)
        | Union other -> evalSource resolve other |> Result.map toFrame |> Result.bind (evalUnion f)
        | Intersect other ->
            evalSource resolve other
            |> Result.map toFrame
            |> Result.bind (evalSetOp "intersect" true f)
        | Except other ->
            evalSource resolve other
            |> Result.map toFrame
            |> Result.bind (evalSetOp "except" false f)

    /// A `Ref`-rejecting resolver — the default for embedded-only pipelines (and the conformance kit).
    let noResolve: string -> Result<Table, EvalError> =
        fun r -> Error(UnresolvedSource r)

    /// The reference evaluator, parameterised (Phase 77): fold the pipeline over the input table
    /// threading a `Frame`, resolving `ColExpr.Param`s from `env` and any `Ref` source through
    /// `resolve`. Every other entry point delegates here — `evalPipelineWith` / `evalPipeline` pass
    /// `Map.empty`, so a param-free pipeline evaluates byte-identically to before.
    let evalPipelineWithInEnv
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (input: Table)
        : Result<Table, EvalError> =
        let rec go f =
            function
            | [] -> Ok(ofFrame f)
            | step :: rest -> evalStep resolve env f step |> Result.bind (fun f' -> go f' rest)

        go (toFrame input) pipeline

    /// The reference evaluator over embedded sources only, resolving params from `env` (Phase 77).
    let evalPipelineInEnv
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (input: Table)
        : Result<Table, EvalError> =
        evalPipelineWithInEnv noResolve env pipeline input

    /// The reference evaluator: fold the pipeline over the input table, threading a `Frame`.
    /// `resolve` provides any `Ref` source a `Join` / `Union` names (default `noResolve` errors).
    /// Param-free by construction (empty env); an env-aware caller uses `evalPipelineWithInEnv`.
    let evalPipelineWith
        (resolve: string -> Result<Table, EvalError>)
        (pipeline: Transform list)
        (input: Table)
        : Result<Table, EvalError> =
        evalPipelineWithInEnv resolve Map.empty pipeline input

    /// The reference evaluator over embedded sources only (`Ref` ⇒ `UnresolvedSource`).
    let evalPipeline (pipeline: Transform list) (input: Table) : Result<Table, EvalError> =
        evalPipelineWithInEnv noResolve Map.empty pipeline input

    // ---- the reference primitives, exposed (Phase 99) ----
    // An incremental evaluator recomputes a SUBSET of what a full evaluation recomputes, so it must
    // compute that subset through the SAME code as the reference — otherwise the two answers can
    // differ and the "incremental ≡ reference" claim becomes a coincidence maintained by hand. These
    // three are the whole of what the row-local + maintained-group strategies need; each is a
    // one-line wrapper over the private definition above, so there is exactly one implementation.

    /// Evaluate one `ColExpr` against a single row, resolving `Param`s from `env` — the reference
    /// expression evaluator itself. Exposed so an incremental evaluator computes a re-evaluated
    /// cell through the same path as a full evaluation rather than a copy of it.
    let evalExprInRow (env: Map<string, Cell>) (cols: Schema) (row: Cell list) (e: ColExpr) : Result<Cell, EvalError> =
        evalExpr env cols row e

    /// Compute one aggregate over a cell list of the given source type, in the evaluator's
    /// `EvalError` envelope — what `GroupBy` calls per group. Exposed so an incremental evaluator
    /// that recomputes only the affected groups produces the same cells (and the same errors) the
    /// reference would.
    let aggregateCells (fn: AggFn) (srcType: ColumnType) (cells: Cell list) : Result<Cell, EvalError> =
        aggCells fn srcType cells

    /// The output column type of an aggregate over a source of the given type — `GroupBy`'s
    /// aggregate-column typing.
    let aggregateType (fn: AggFn) (srcType: ColumnType) : ColumnType = aggType fn srcType

    /// The column type a `Derive`d column takes from its computed cells: the first non-null cell's
    /// type, `StringType` when every cell is null. Exposed because the type is a function of the
    /// WHOLE column, not of one row — the one place a row-local step is not row-local, and an
    /// incremental evaluator that overlooked it would type a column differently from the reference.
    let inferCellType (cells: Cell list) : ColumnType = inferType cells

    /// The reference `Sort`'s row comparator: the pinned ordering (multi-key, nulls last regardless
    /// of direction, unknown columns skipped). Exposed for the same reason as the four above — an
    /// incremental evaluator that merged rows into a cached order under its OWN comparator would
    /// agree with the reference on every corpus anyone thought to write and disagree on the first
    /// null, the first tie and the first misspelled key. `List.sortWith` over it is `evalSort`.
    ///
    /// It is a comparator, so it says nothing about STABILITY: `Sort`'s stability comes from
    /// `List.sortWith` being stable over the frame order, and a caller reproducing the reference
    /// ordering must reproduce that too, not only this function.
    let rowCompareBy (cols: Schema) (by: (string * SortDir) list) (r1: Cell list) (r2: Cell list) : int =
        rowKeyCompare cols by r1 r2

    // ---- incremental evaluation (Phase 34) ----
    // A full-recompute evaluator made incremental via change-relevance analysis: when a change provably
    // cannot alter the output, the prior result is reused; otherwise the pipeline re-runs over the
    // changed source. It remains the SAME reference evaluator (the cross-host parity contract, GP6) — the
    // reuse is a sound optimisation, certified byte-identical to a full `evalPipeline` (Phase 34 law).

    let private unionAll (xs: Set<string> list) : Set<string> = (Set.empty, xs) ||> List.fold Set.union

    /// The source columns a `ColExpr` references.
    let rec private exprCols (e: ColExpr) : Set<string> =
        match e with
        | Col n -> Set.singleton n
        | Lit _ -> Set.empty
        // A `Param` references the evaluation env, not a source column (Phase 77) — no column dep.
        | Param _ -> Set.empty
        | Binary(_, a, b) -> Set.union (exprCols a) (exprCols b)
        | Not x -> exprCols x
        | Coalesce xs -> unionAll (xs |> List.map exprCols)
        | Case(cases, els) ->
            unionAll (
                exprCols els
                :: (cases |> List.collect (fun (w, t) -> [ exprCols w; exprCols t ]))
            )
        | Cast(_, x) -> exprCols x
        | ApplyFn(_, xs) -> unionAll (xs |> List.map exprCols)
        | InList(x, items) -> unionAll ((x :: items) |> List.map exprCols)
        | IsNull x -> exprCols x
        | InParam(x, _) -> exprCols x

    /// The source columns a single step references — an over-approximation is safe (it only makes the
    /// incremental check more conservative, never less). A right-hand `Join`/`Union` source is a
    /// *different* table, so only the left key columns count here.
    let private stepCols (t: Transform) : Set<string> =
        match t with
        | Filter p -> exprCols p
        | Project pairs -> pairs |> List.map fst |> Set.ofList
        | Derive(_, e) -> exprCols e
        | GroupBy(keys, aggs) -> Set.union (Set.ofList keys) (aggs |> List.map (fun a -> a.Of) |> Set.ofList)
        | Join(_, on, _) -> on |> List.map fst |> Set.ofList
        | Window spec ->
            unionAll
                [ Set.ofList spec.PartitionBy
                  spec.OrderBy |> List.map fst |> Set.ofList
                  Set.singleton spec.Of ]
        | Pivot spec -> unionAll [ Set.ofList spec.Index; Set.singleton spec.On; Set.singleton spec.Values ]
        | Unpivot(idVars, valueVars) -> Set.union (Set.ofList idVars) (Set.ofList valueVars)
        | Sort by -> by |> List.map fst |> Set.ofList
        | Distinct
        | Limit _
        | Union _
        | Intersect _
        | Except _ -> Set.empty

    let private readColumns (pipeline: Transform list) : Set<string> =
        unionAll (pipeline |> List.map stepCols)

    // `Distinct` dedups on the FULL row, so a column dropped *after* a Distinct still influences the
    // output through the dedup — the steps where the "not read + not in output" check is unsound.
    // `Intersect` / `Except` (Phase 101) match on the full row for the same reason and belong here:
    // a change to a column neither read nor emitted can still flip a row's membership, so the
    // incremental reuse short-circuit must not fire.
    let private hasFullRowDedup (pipeline: Transform list) : bool =
        pipeline
        |> List.exists (function
            | Distinct
            | Intersect _
            | Except _ -> true
            | _ -> false)

    /// Incrementally evaluate a pipeline given the PRIOR result and a description of what changed (Phase
    /// 34). When the change provably cannot alter the output — a `ColumnValuesChanged` on a column the
    /// pipeline neither reads nor emits, and no full-row `Distinct` is present — the prior result is
    /// reused unchanged (zero recompute). Every other change re-runs `evalPipeline` over the changed
    /// source. **Byte-identical to a full `evalPipeline` over the changed source for every change** (the
    /// certified equivalence, `Conformance.incrementalLaws`); the reuse is a sound optimisation, never a
    /// different answer. The reference evaluator stays the single cross-host contract.
    let evalFrom
        (prior: Table)
        (change: Change)
        (pipeline: Transform list)
        (changedSource: Table)
        : Result<Table, EvalError> =
        let irrelevant =
            match change with
            | ColumnValuesChanged c ->
                not (hasFullRowDedup pipeline)
                && not (Set.contains c (readColumns pipeline))
                && not (prior.Schema |> List.exists (fun (n, _) -> n = c))
            | RowsAppended
            | SchemaChanged _
            | FullChange -> false

        if irrelevant then
            Ok prior
        else
            evalPipeline pipeline changedSource

    /// Render an `EvalError` as a stable human string.
    let errorString (e: EvalError) : string =
        match e with
        | UnknownColumn(n, avail) -> "unknown column '" + n + "'; available: " + String.concat ", " avail
        | TypeError d -> "type error: " + d
        | AggError d -> "aggregate error: " + d
        | JoinError d -> "join error: " + d
        | ArityError(fn, exp, got) -> "function '" + fn + "' expects " + string exp + " args, got " + string got
        | UnresolvedSource r -> "unresolved source ref: " + r
        | OverflowError d -> "overflow: " + d
        | UnboundParam(n, bound) -> "unbound param '" + n + "'; bound: " + String.concat ", " bound


// ============================================================================
//  Phase 112 — the STATIC output-schema walk over a `Transform` pipeline.
//
//  A consumer needs a pipeline's OUTPUT columns without evaluating it: a UI
//  tier's grid validator refusing a field no step can produce, a planner sizing
//  a result, a domain checking a reader against its producer. `Transform` is
//  data, so the answer is derivable from the input schema alone — and deriving
//  it in a CONSUMER is where it goes wrong, because the next verb this file
//  admits silently invalidates a copy nobody recompiles.
//
//  ── Why a static walk can answer this, and where it stops ───────────────────
//  The verb set is a closed DU, the expression algebra is a closed DU, and
//  neither carries code. So the walk ENUMERATES; it does not analyse, and there
//  is no fixpoint to reach. What it cannot do is see VALUES, and three shapes
//  genuinely depend on them:
//
//    Derive    the output column's NAME is declared, but its TYPE is inferred
//              from the cells the expression produced. So the column is known to
//              exist with an unknown type — not guessed at from the expression,
//              because a guess that disagreed with the evaluator would be worse
//              than no answer.
//    Pivot     the output's value columns are NAMED BY THE DATA — one per
//              distinct present value in the `on` column. The index columns are
//              known; the rest are not even countable.
//    Ref       a named source's rows are resolved by the host (the wire carries
//              the name, never the data), so its schema is whatever the caller
//              declares — and nothing at all when it declares none.
//
//  `SchemaKnowledge` carries that distinction in its SHAPE rather than in a
//  comment: `Closed` means these columns and no others, and it is the ONLY case
//  from which "that column is absent" can be concluded. `AtLeast` means these
//  columns are present and the walk cannot name the rest, so it can confirm a
//  reader but never refute one. A check that cannot refute reports itself as
//  underivable and produces no finding.
//
//  ── The evaluator is the oracle ────────────────────────────────────────────
//  Every case below mirrors what `DataFrame`'s evaluator does to a frame's
//  columns, and `Conformance.schemaWalkLaws` certifies the agreement over
//  generated pipelines rather than leaving it to review. Two places where the
//  mirror is easy to get wrong, and is deliberate here:
//
//    * `Window` APPENDS its output column unconditionally, it does not upsert —
//      so a window whose `As` collides with an existing column leaves the schema
//      carrying that name twice, and the walk says so.
//    * `Derive` UPSERTS (retype in place, position kept), because that is what
//      the evaluator does with a name it already carries.
//
//  FORWARD-COUPLING: a new `Transform` verb, a new `JoinKind`, a new `WindowFn`
//  or a new `AggFn` extends the matches below — all four are closed DUs matched
//  with NO catch-all, so the compiler stops a new verb here rather than letting
//  it drift in a consumer. That is the whole reason this lives beside the
//  evaluator instead of downstream of it.
//
//  FSharp.Core only, Fable-clean; evaluates nothing and allocates no table.
// ============================================================================

/// What the static walk knows about ONE output column. The name is always known — every verb that
/// adds a column declares its name — and the type is not always, which is why only one of the two
/// is an option.
type ColumnKnowledge =
    {
        Name: string
        /// `None` where the column exists but its type is decidable only from the DATA. A `Derive`'s
        /// type is inferred from the cells its expression produced, so it is `None` however simple
        /// the expression looks.
        Type: ColumnType option
    }

/// What a static walk knows about a pipeline's output columns (Phase 112).
///
/// Two cases, not three: `AtLeast([], reason)` already says "nothing is known", so a separate
/// opaque case would be a second spelling of one state — and a state with two spellings is one a
/// check eventually gets wrong.
[<RequireQualifiedAccess>]
type SchemaKnowledge =
    /// The column set is CLOSED: these columns, in this order, and no others. **The only case that
    /// supports a negative verdict** — an absence is a fact here and an ignorance everywhere else.
    | Closed of columns: ColumnKnowledge list
    /// These columns are present; the walk cannot name what else might be. A reader can still be
    /// CONFIRMED against it and can never be REFUTED, and the reason names what cost the walk its
    /// certainty.
    | AtLeast of columns: ColumnKnowledge list * reason: string

/// The static output-schema walk (Phase 112) — `Transform`'s schema semantics, derived without
/// evaluating anything. See the block comment above for what it can and cannot know.
///
/// (Named `SchemaWalk` rather than `Schema`: `Fuaran.Core` already publishes a `Schema` type
/// abbreviation and a `Schema` module of schema-level operations beside it, and a second module of
/// that name in one namespace does not compile.)
module SchemaWalk =

    /// No named source declared. The default, and honest: a walk over a `Ref` under it derives
    /// nothing about that source and says which name it could not resolve.
    let noSources: string -> Schema option = fun _ -> None

    /// Declared source schemas as a map — the ordinary caller-side lookup, lifted so a caller
    /// holding a `Map` does not write the lambda.
    let ofMap (sources: Map<string, Schema>) : string -> Schema option = fun name -> Map.tryFind name sources

    // ---- reading the knowledge ----

    /// The columns the walk can name, whichever case it is in.
    let columns (knowledge: SchemaKnowledge) : ColumnKnowledge list =
        match knowledge with
        | SchemaKnowledge.Closed cols -> cols
        | SchemaKnowledge.AtLeast(cols, _) -> cols

    /// The named columns, in schema order.
    let names (knowledge: SchemaKnowledge) : string list = columns knowledge |> List.map _.Name

    /// True when the column set is closed, so an absence is a fact rather than an ignorance.
    let isClosed (knowledge: SchemaKnowledge) : bool =
        match knowledge with
        | SchemaKnowledge.Closed _ -> true
        | SchemaKnowledge.AtLeast _ -> false

    /// Why the walk lost its certainty, where it did.
    let reason (knowledge: SchemaKnowledge) : string option =
        match knowledge with
        | SchemaKnowledge.Closed _ -> None
        | SchemaKnowledge.AtLeast(_, r) -> Some r

    /// The declared type of a named column: `None` both when the column is absent and when it is
    /// present with an undecidable type. The two are different facts, and a caller that needs to
    /// tell them apart asks `has` as well.
    let typeOf (name: string) (knowledge: SchemaKnowledge) : ColumnType option =
        columns knowledge |> List.tryFind (fun c -> c.Name = name) |> Option.bind _.Type

    /// True when the walk can SEE this column. False on an `AtLeast` means "not visible", never
    /// "absent" — refuting a reader is sound only under `isClosed`.
    let has (name: string) (knowledge: SchemaKnowledge) : bool =
        columns knowledge |> List.exists (fun c -> c.Name = name)

    // ---- building it ----

    let private withColumns (cols: ColumnKnowledge list) (knowledge: SchemaKnowledge) : SchemaKnowledge =
        match knowledge with
        | SchemaKnowledge.Closed _ -> SchemaKnowledge.Closed cols
        | SchemaKnowledge.AtLeast(_, r) -> SchemaKnowledge.AtLeast(cols, r)

    /// Add a column, or RETYPE it in place where the name is already known — exactly what the
    /// evaluator's `Derive` does, position included.
    let private upsert (column: ColumnKnowledge) (knowledge: SchemaKnowledge) : SchemaKnowledge =
        let cols = columns knowledge

        if cols |> List.exists (fun c -> c.Name = column.Name) then
            knowledge
            |> withColumns (cols |> List.map (fun c -> if c.Name = column.Name then column else c))
        else
            knowledge |> withColumns (cols @ [ column ])

    /// Append a column unconditionally, duplicate name included — what the evaluator's `Window`
    /// does. Deliberately not `upsert`: a window whose `As` names an existing column leaves the
    /// evaluated schema carrying that name twice, and a walk that tidied it away would be wrong
    /// about the shape the consumer actually receives.
    let private appendColumn (column: ColumnKnowledge) (knowledge: SchemaKnowledge) : SchemaKnowledge =
        knowledge |> withColumns (columns knowledge @ [ column ])

    let private ofColumns (schema: Schema) : ColumnKnowledge list =
        schema |> List.map (fun (name, ty) -> { Name = name; Type = Some ty })

    /// A concrete schema is closed knowledge — the walk's starting point.
    let ofSchema (schema: Schema) : SchemaKnowledge =
        SchemaKnowledge.Closed(ofColumns schema)

    /// What is known about a `DataSource` before any transform runs. An `Embedded` table declares
    /// its own schema; a `Ref` is whatever `sources` declares, and an undeclared name degrades to
    /// "unknown" rather than to a guess or a refusal — refusing on the strength of a schema nobody
    /// declared would punish a caller for not answering a question it was never asked.
    let ofSource (sources: string -> Schema option) (source: DataSource) : SchemaKnowledge =
        match source with
        | Embedded table -> ofSchema table.Schema
        | Ref name ->
            match sources name with
            | Some schema -> ofSchema schema
            | None -> SchemaKnowledge.AtLeast([], "source '" + name + "' is a Ref with no declared schema")

    /// The type a window function's output column carries — pinned to the evaluator's own rule, not
    /// restated loosely: the positional/ranking family is `Int`, the accumulating family is `Float`,
    /// and the shifting + running-extreme family keeps the source column's type, which is unknown
    /// exactly when the source column's type is.
    let private windowType (input: SchemaKnowledge) (spec: WindowSpec) : ColumnType option =
        match spec.Fn with
        | RowNumber
        | Rank
        | DenseRank
        | CompetitionRank
        | NTile _ -> Some IntType
        | CumulSum
        | RollingMean
        | RollingSum -> Some FloatType
        | Lag
        | Lead
        | CumulMax
        | CumulMin -> typeOf spec.Of input

    /// The type an aggregate produces over a source column of `sourceType`. Where the source type is
    /// unknown, only the aggregates that IGNORE it can still be typed — which is a fact about the
    /// aggregate, not a fallback. `Column.aggType` stays the single source for the known case.
    let private aggregateType (fn: AggFn) (sourceType: ColumnType option) : ColumnType option =
        match sourceType with
        | Some ty -> Some(Column.aggType fn ty)
        | None ->
            match fn with
            | Count
            | CountDistinct -> Some IntType
            | Mean
            | Median
            | StdDev -> Some FloatType
            | Sum
            | Min
            | Max
            | First
            | Last -> None

    /// The output knowledge of ONE transform step over an input knowledge. Total, and evaluates
    /// nothing: every case is a rearrangement of names and declared types.
    let ofTransform (sources: string -> Schema option) (input: SchemaKnowledge) (step: Transform) : SchemaKnowledge =
        match step with
        // Row-set verbs: they drop, reorder or dedup ROWS and touch no column. The three set ops
        // take the LEFT schema through unchanged — the evaluator requires the two column-name lists
        // to agree before it gets here, so a disagreement is an `EvalError`, never a schema.
        | Filter _
        | Sort _
        | Distinct
        | Limit _
        | Union _
        | Intersect _
        | Except _ -> input

        // Project CLOSES the set however open the input was: the output is exactly the listed
        // columns, under their output names, in the listed order, whatever else the input carried.
        | Project pairs ->
            SchemaKnowledge.Closed(
                pairs
                |> List.map (fun (source, out) ->
                    { Name = out
                      Type = typeOf source input })
            )

        // The name is declared; the type is inferred from the cells the expression produced, so it
        // is data-dependent and stays unknown.
        | Derive(name, _) -> upsert { Name = name; Type = None } input

        // GroupBy closes the set too: the key columns then one column per aggregate, and nothing
        // survives that was not named.
        | GroupBy(keys, aggs) ->
            SchemaKnowledge.Closed(
                (keys |> List.map (fun key -> { Name = key; Type = typeOf key input }))
                @ (aggs
                   |> List.map (fun agg ->
                       { Name = agg.Name
                         Type = aggregateType agg.Fn (typeOf agg.Of input) }))
            )

        | Window spec ->
            input
            |> appendColumn
                { Name = spec.As
                  Type = windowType input spec }

        // The index columns are known; the value columns are one per DISTINCT PRESENT VALUE in the
        // `on` column, which is data. Not even their number is derivable, so the set opens here and
        // every later step inherits that.
        | Pivot spec ->
            SchemaKnowledge.AtLeast(
                spec.Index
                |> List.map (fun name ->
                    { Name = name
                      Type = typeOf name input }),
                "a pivot's value columns are named by the data — one per distinct value in its `on` column"
            )

        | Unpivot(idVars, valueVars) ->
            SchemaKnowledge.Closed(
                (idVars
                 |> List.map (fun name ->
                     { Name = name
                       Type = typeOf name input }))
                @ [ { Name = "variable"
                      Type = Some StringType }
                    { Name = "value"
                      Type =
                        // The evaluator types the melted column from the first value column it can
                        // resolve, and falls back to `String` when there is no value column at all.
                        match valueVars with
                        | [] -> Some StringType
                        | _ -> valueVars |> List.tryPick (fun name -> typeOf name input) } ]
            )

        | Join(source, _, how) ->
            match how with
            // The FILTERING joins (Phase 101) keep the LEFT schema only — each qualifying left row
            // once, no right columns — so neither the right source's schema nor the collision-suffix
            // rule below is reached, and an unknown right source costs the walk nothing.
            | Semi
            | Anti -> input

            | Inner
            | Left
            | Right
            | Outer ->
                match input with
                // The evaluator suffixes a right column whose name collides with a LEFT one. So a
                // right column's OUTPUT name is a function of the left's names — and while any left
                // name is invisible, every right column's name is undecidable between `x` and
                // `x_right`. That is why an open left contributes no right columns at all rather
                // than guessing that no collision occurred.
                | SchemaKnowledge.AtLeast(cols, r) ->
                    SchemaKnowledge.AtLeast(
                        cols,
                        r
                        + " — and a join's right-hand output names depend on the left's, so they cannot be named either"
                    )
                | SchemaKnowledge.Closed left ->
                    let right = ofSource sources source
                    let leftNames = left |> List.map _.Name |> Set.ofList

                    let renamed =
                        columns right
                        |> List.map (fun c ->
                            if Set.contains c.Name leftNames then
                                { c with Name = c.Name + "_right" }
                            else
                                c)

                    match right with
                    | SchemaKnowledge.Closed _ -> SchemaKnowledge.Closed(left @ renamed)
                    | SchemaKnowledge.AtLeast(_, r) -> SchemaKnowledge.AtLeast(left @ renamed, r)

    /// Fold a pipeline over knowledge already in hand — the general form, and the one a consumer
    /// that interleaves its own per-step checks with the walk reaches for.
    let ofPipelineFrom
        (sources: string -> Schema option)
        (input: SchemaKnowledge)
        (pipeline: Transform list)
        : SchemaKnowledge =
        pipeline |> List.fold (ofTransform sources) input

    /// The output knowledge of a whole pipeline over a concrete input schema. Total. A `Ref` source
    /// inside a `Join` / set op is undeclared here (the result opens, with the reason naming the
    /// unresolved name); a caller that CAN declare them uses `ofPipelineFrom` with `ofMap`.
    let ofPipeline (schema: Schema) (pipeline: Transform list) : SchemaKnowledge =
        ofPipelineFrom noSources (ofSchema schema) pipeline

/// The canonical wire codec for the `Transform` + `ColExpr` trees — `"kind"`-tagged objects (the
/// `Fuaran.Core` envelope discipline), reusing `ColumnCodec` for embedded `DataSource` operands and
/// the `Wire` canonical-float rules for literals. Decode is `Result`-typed with the same six-code
/// `ColumnError` envelope (a `Transform` wire is a columnar-strand wire). Fable-clean.
module DataFrameCodec =

    let private aggFnTag =
        function
        | Sum -> "sum"
        | Mean -> "mean"
        | Min -> "min"
        | Max -> "max"
        | Count -> "count"
        | Median -> "median"
        | StdDev -> "stddev"
        | First -> "first"
        | Last -> "last"
        | CountDistinct -> "countDistinct"

    let private aggFnOf =
        function
        | "sum" -> Some Sum
        | "mean" -> Some Mean
        | "min" -> Some Min
        | "max" -> Some Max
        | "count" -> Some Count
        | "median" -> Some Median
        | "stddev" -> Some StdDev
        | "first" -> Some First
        | "last" -> Some Last
        | "countDistinct" -> Some CountDistinct
        // Phase 92 alias — the SQL prior; canonical encode stays "mean".
        | "avg" -> Some Mean
        | _ -> None

    let private joinTag =
        function
        | Inner -> "inner"
        | Left -> "left"
        | Right -> "right"
        | Outer -> "outer"
        | Semi -> "semi"
        | Anti -> "anti"

    let private joinOf =
        function
        | "inner" -> Some Inner
        | "left" -> Some Left
        | "right" -> Some Right
        | "outer" -> Some Outer
        | "semi" -> Some Semi
        | "anti" -> Some Anti
        | _ -> None

    let private windowTag =
        function
        | RowNumber -> "rowNumber"
        | Rank -> "rank"
        | Lag -> "lag"
        | Lead -> "lead"
        | CumulSum -> "cumulSum"
        | RollingMean -> "rollingMean"
        | DenseRank -> "denseRank"
        | CompetitionRank -> "competitionRank"
        // The bucket count rides an additive `"n"` field on the step object, not the tag.
        | NTile _ -> "ntile"
        | CumulMax -> "cumulMax"
        | CumulMin -> "cumulMin"
        | RollingSum -> "rollingSum"

    let private windowOf =
        function
        | "rowNumber" -> Some RowNumber
        | "rank" -> Some Rank
        | "lag" -> Some Lag
        | "lead" -> Some Lead
        | "cumulSum" -> Some CumulSum
        // Legacy alias — the pre-rename wire tag (operator rename 2026-07-19); normalises on re-encode.
        | "cumSum" -> Some CumulSum
        | "rollingMean" -> Some RollingMean
        | "denseRank" -> Some DenseRank
        | "competitionRank" -> Some CompetitionRank
        | "cumulMax" -> Some CumulMax
        | "cumulMin" -> Some CumulMin
        | "rollingSum" -> Some RollingSum
        // "ntile" is NOT here: it carries a bucket count, so only the step decoder (which can see
        // the sibling `"n"` field) can build it.
        | _ -> None

    let private dirTag =
        function
        | Asc -> "asc"
        | Desc -> "desc"

    let private dirOf =
        function
        | "desc" -> Desc
        | _ -> Asc

    let private scalarTag =
        function
        | Abs -> "abs"
        | Round -> "round"
        | Floor -> "floor"
        | Ceil -> "ceil"
        | Length -> "length"
        | Lower -> "lower"
        | Upper -> "upper"
        | Substr -> "substr"
        | DatePart -> "datePart"
        | Concat -> "concat"
        | Trim -> "trim"
        | Replace -> "replace"
        | DateDiffDays -> "dateDiffDays"
        | Sqrt -> "sqrt"
        | Least -> "least"
        | Greatest -> "greatest"
        | IndexOf -> "indexOf"

    let private scalarOf =
        function
        | "abs" -> Some Abs
        | "round" -> Some Round
        | "floor" -> Some Floor
        | "ceil" -> Some Ceil
        | "length" -> Some Length
        | "lower" -> Some Lower
        | "upper" -> Some Upper
        | "substr" -> Some Substr
        | "datePart" -> Some DatePart
        | "concat" -> Some Concat
        | "trim" -> Some Trim
        | "replace" -> Some Replace
        | "dateDiffDays" -> Some DateDiffDays
        | "sqrt" -> Some Sqrt
        | "least" -> Some Least
        | "greatest" -> Some Greatest
        | "indexOf" -> Some IndexOf
        | _ -> None

    let private binTag =
        function
        | Add -> "add"
        | Sub -> "sub"
        | Mul -> "mul"
        | Div -> "div"
        | Mod -> "mod"
        | Eq -> "eq"
        | Ne -> "ne"
        | Lt -> "lt"
        | Le -> "le"
        | Gt -> "gt"
        | Ge -> "ge"
        | And -> "and"
        | Or -> "or"
        | Contains -> "contains"
        | StartsWith -> "startsWith"
        | EndsWith -> "endsWith"

    let private binOf =
        function
        | "add" -> Some Add
        | "sub" -> Some Sub
        | "mul" -> Some Mul
        | "div" -> Some Div
        | "mod" -> Some Mod
        | "eq" -> Some Eq
        | "ne" -> Some Ne
        | "lt" -> Some Lt
        | "le" -> Some Le
        | "gt" -> Some Gt
        | "ge" -> Some Ge
        | "and" -> Some And
        | "or" -> Some Or
        | "contains" -> Some Contains
        | "startsWith" -> Some StartsWith
        | "endsWith" -> Some EndsWith
        | _ -> None

    // ---- a single literal cell (type-tagged so decode reconstructs the scalar) ----

    let private cellToJson (c: Cell) : JVal =
        match c with
        | Null -> Canon.typed "Null" []
        | Int i -> Canon.typed "Int" [ "value", JInt i ]
        | Float f -> Canon.typed "Float" [ "value", JFloat f ]
        | Bool b -> Canon.typed "Bool" [ "value", JBool b ]
        | Str s -> Canon.typed "Str" [ "value", JStr s ]
        | Date s -> Canon.typed "Date" [ "value", JStr s ]
        | Timestamp s -> Canon.typed "Timestamp" [ "value", JStr s ]

    let private cellOfJson (el: JVal) : Result<Cell, ColumnError> =
        match el with
        | JObj fields ->
            let find k =
                fields |> List.tryFind (fun (n, _) -> n = k) |> Option.map snd

            match find "$type" with
            | Some(JStr "Null") -> Ok Null
            | Some(JStr t) ->
                match find "value", t with
                | Some(JInt i), "Int" -> Ok(Int i)
                | Some(JFloat f), "Float" -> Ok(Float f)
                | Some(JInt i), "Float" -> Ok(Float(float i))
                | Some(JBool b), "Bool" -> Ok(Bool b)
                | Some(JStr s), "Str" -> Ok(Str s)
                | Some(JStr s), "Date" -> Ok(Date s)
                | Some(JStr s), "Timestamp" -> Ok(Timestamp s)
                | _ -> Error(TypeMismatch("lit", t, "value"))
            | _ -> Error(MissingField "lit.$type")
        | _ -> Error(MalformedShape "lit: expected object")

    // ---- ColExpr ----

    let rec encodeExpr (e: ColExpr) : JVal =
        match e with
        | Col name -> Canon.typed "col" [ "name", JStr name ]
        | Lit c -> Canon.typed "lit" [ "cell", cellToJson c ]
        | Param name -> Canon.typed "param" [ "name", JStr name ]
        | Binary(op, a, b) ->
            Canon.typed "binary" [ "op", JStr(binTag op); "left", encodeExpr a; "right", encodeExpr b ]
        | Not inner -> Canon.typed "not" [ "expr", encodeExpr inner ]
        | Coalesce exprs -> Canon.typed "coalesce" [ "exprs", JArr(exprs |> List.map encodeExpr) ]
        | Case(cases, elseExpr) ->
            Canon.typed
                "case"
                [ "cases",
                  JArr(
                      cases
                      |> List.map (fun (w, t) -> JObj [ "when", encodeExpr w; "then", encodeExpr t ])
                  )
                  "else", encodeExpr elseExpr ]
        | Cast(ty, inner) -> Canon.typed "cast" [ "type", JStr(ColumnType.tag ty); "expr", encodeExpr inner ]
        | ApplyFn(fn, args) ->
            Canon.typed "apply" [ "fn", JStr(scalarTag fn); "args", JArr(args |> List.map encodeExpr) ]
        | InList(subject, items) ->
            Canon.typed "in" [ "expr", encodeExpr subject; "items", JArr(items |> List.map encodeExpr) ]
        | InParam(subject, name) -> Canon.typed "in" [ "expr", encodeExpr subject; "param", JStr name ]
        | IsNull inner -> Canon.typed "isNull" [ "expr", encodeExpr inner ]

    let private field k el =
        match el with
        | JObj fields ->
            match fields |> List.tryFind (fun (n, _) -> n = k) with
            | Some(_, v) -> Ok v
            | None -> Error(MissingField k)
        | _ -> Error(MalformedShape("expected object for field " + k))

    let private tryField k el =
        match el with
        | JObj fields -> fields |> List.tryFind (fun (n, _) -> n = k) |> Option.map snd
        | _ -> None

    /// Phase 92 (lenient-ingest) — accept exactly one of the canonical field or its observed
    /// alias (the SQL/pandas prior, pilot-4 census); both present is ambiguous (didactic),
    /// neither reports the canonical name.
    let private fieldAliased (canonical: string) (alias: string) el =
        match tryField canonical el, tryField alias el with
        | Some v, None
        | None, Some v -> Ok v
        | Some _, Some _ ->
            Error(MalformedShape("give \"" + canonical + "\" (canonical) or \"" + alias + "\" (alias), not both"))
        | None, None -> Error(MissingField canonical)

    let private strOf el =
        match el with
        | JStr s -> Ok s
        | _ -> Error(MalformedShape "expected string")

    let private arrOf el =
        match el with
        | JArr xs -> Ok xs
        | _ -> Error(MalformedShape "expected array")

    let private kindOf el = field "$type" el |> Result.bind strOf

    let private mapM (f: 'a -> Result<'b, ColumnError>) (xs: 'a list) : Result<'b list, ColumnError> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | x :: rest -> f x |> Result.bind (fun v -> go (v :: acc) rest)

        go [] xs

    let rec decodeExpr (el: JVal) : Result<ColExpr, ColumnError> =
        kindOf el
        |> Result.bind (fun k ->
            match k with
            | "col" -> field "name" el |> Result.bind strOf |> Result.map Col
            | "lit" -> field "cell" el |> Result.bind cellOfJson |> Result.map Lit
            | "param" -> field "name" el |> Result.bind strOf |> Result.map Param
            | "binary" ->
                field "op" el
                |> Result.bind strOf
                |> Result.bind (fun ops ->
                    match binOf ops with
                    | None ->
                        Error(
                            UnknownType(
                                ops,
                                [ "add"
                                  "sub"
                                  "mul"
                                  "div"
                                  "mod"
                                  "eq"
                                  "ne"
                                  "lt"
                                  "le"
                                  "gt"
                                  "ge"
                                  "and"
                                  "or"
                                  "contains"
                                  "startsWith"
                                  "endsWith" ]
                            )
                        )
                    | Some op ->
                        field "left" el
                        |> Result.bind decodeExpr
                        |> Result.bind (fun a ->
                            field "right" el
                            |> Result.bind decodeExpr
                            |> Result.map (fun b -> Binary(op, a, b))))
            | "not" -> field "expr" el |> Result.bind decodeExpr |> Result.map Not
            | "coalesce" ->
                field "exprs" el
                |> Result.bind arrOf
                |> Result.bind (mapM decodeExpr)
                |> Result.map Coalesce
            | "case" ->
                field "cases" el
                |> Result.bind arrOf
                |> Result.bind (
                    mapM (fun c ->
                        field "when" c
                        |> Result.bind decodeExpr
                        |> Result.bind (fun w ->
                            field "then" c |> Result.bind decodeExpr |> Result.map (fun t -> w, t)))
                )
                |> Result.bind (fun cases ->
                    field "else" el
                    |> Result.bind decodeExpr
                    |> Result.map (fun e -> Case(cases, e)))
            | "cast" ->
                field "type" el
                |> Result.bind strOf
                |> Result.bind (fun ts ->
                    match ColumnType.ofTag ts with
                    | None -> Error(UnknownType(ts, ColumnType.allTags))
                    | Some ty ->
                        field "expr" el
                        |> Result.bind decodeExpr
                        |> Result.map (fun inner -> Cast(ty, inner)))
            // Phase 93 — `call` aliases `apply` (same fn/args fields); Phase 94 adds the
            // third observed spelling `fn` ({"$type":"fn","fn":"lower","args":[…]}).
            | "apply"
            | "call"
            | "fn" ->
                field "fn" el
                |> Result.bind strOf
                |> Result.bind (fun fns ->
                    match scalarOf fns with
                    | None ->
                        Error(
                            UnknownType(
                                fns,
                                [ "abs"
                                  "round"
                                  "floor"
                                  "ceil"
                                  "length"
                                  "lower"
                                  "upper"
                                  "substr"
                                  "datePart"
                                  "concat"
                                  "trim"
                                  "replace"
                                  "dateDiffDays"
                                  "sqrt"
                                  "least"
                                  "greatest"
                                  "indexOf" ]
                            )
                        )
                    | Some fn ->
                        field "args" el
                        |> Result.bind arrOf
                        |> Result.bind (mapM decodeExpr)
                        |> Result.map (fun args -> ApplyFn(fn, args)))
            | "in" ->
                field "expr" el
                |> Result.bind decodeExpr
                |> Result.bind (fun subject ->
                    // Phase 91 — exactly one of `items` (literal list) / `param` (a bound
                    // multi-select list param).
                    match tryField "items" el, tryField "param" el with
                    | Some ij, None ->
                        arrOf ij
                        |> Result.bind (mapM decodeExpr)
                        |> Result.map (fun items -> InList(subject, items))
                    | None, Some pj -> strOf pj |> Result.map (fun p -> InParam(subject, p))
                    | Some _, Some _ ->
                        Error(
                            MalformedShape(
                                "in: give exactly ONE of \"items\" (a literal list) or \"param\" (a multi-select list param), not both"
                            )
                        )
                    | None, None -> Error(MissingField "items"))
            | "isNull" -> field "expr" el |> Result.bind decodeExpr |> Result.map IsNull
            // Phase 93 — expression-level string-predicate spellings (stretch-wave-2 census):
            // {"$type":"contains","expr":X,"other":Y} (also left/right) denotes exactly
            // Binary(Contains, X, Y); same for startsWith/endsWith. Canonical stays the
            // "binary" form — these normalise on re-encode.
            | "contains"
            | "startsWith"
            | "endsWith" ->
                let op =
                    match k with
                    | "contains" -> Contains
                    | "startsWith" -> StartsWith
                    | _ -> EndsWith

                fieldAliased "left" "expr" el
                |> Result.bind decodeExpr
                |> Result.bind (fun subject ->
                    fieldAliased "right" "other" el
                    |> Result.bind decodeExpr
                    |> Result.map (fun rhs -> Binary(op, subject, rhs)))
            // Phase 94 — flat logical spellings (pilot-5 census): SQL-prior models emit
            // {"$type":"or","exprs":[e1,e2,…]} (variadic) or {"$type":"and","left":X,"right":Y}
            // instead of the canonical nested "binary". A variadic list left-folds into the
            // nested form (and/or are associative); canonical stays "binary" — these
            // normalise on re-encode.
            | "and"
            | "or" ->
                let op = if k = "and" then And else Or

                match tryField "exprs" el with
                | Some exprsEl ->
                    arrOf exprsEl
                    |> Result.bind (mapM decodeExpr)
                    |> Result.bind (function
                        | [] -> Error(MalformedShape(k + ".exprs: expected a non-empty array"))
                        | [ single ] -> Ok single
                        | first :: rest -> Ok(rest |> List.fold (fun acc e -> Binary(op, acc, e)) first))
                | None ->
                    fieldAliased "left" "expr" el
                    |> Result.bind decodeExpr
                    |> Result.bind (fun a ->
                        fieldAliased "right" "other" el
                        |> Result.bind decodeExpr
                        |> Result.map (fun b -> Binary(op, a, b)))
            // Phase 94 — flat comparison spellings, the same class ("eq" sighted in the
            // pilot-5 window): {"$type":"eq","left":X,"right":Y} denotes exactly
            // Binary(Eq, X, Y); same for ne/lt/le/gt/ge.
            | "eq"
            | "ne"
            | "lt"
            | "le"
            | "gt"
            | "ge" ->
                let op =
                    match k with
                    | "eq" -> Eq
                    | "ne" -> Ne
                    | "lt" -> Lt
                    | "le" -> Le
                    | "gt" -> Gt
                    | _ -> Ge

                fieldAliased "left" "expr" el
                |> Result.bind decodeExpr
                |> Result.bind (fun a ->
                    fieldAliased "right" "other" el
                    |> Result.bind decodeExpr
                    |> Result.map (fun b -> Binary(op, a, b)))
            // Phase 94 — flat scalar-fn spellings: {"$type":"lower","expr":X} /
            // {"$type":"concat","args":[…]} denote ApplyFn(fn, args). The scalar-fn
            // name vocabulary is disjoint from the node-kind vocabulary, so the
            // mapping is one-to-one; canonical stays "apply".
            | other when (scalarOf other).IsSome ->
                let fn = (scalarOf other).Value

                (match tryField "args" el with
                 | Some argsEl -> arrOf argsEl |> Result.bind (mapM decodeExpr)
                 | None -> field "expr" el |> Result.bind decodeExpr |> Result.map List.singleton)
                |> Result.map (fun args -> ApplyFn(fn, args))
            | other ->
                Error(
                    UnknownType(
                        other,
                        [ "col"
                          "lit"
                          "param"
                          "binary"
                          "not"
                          "coalesce"
                          "case"
                          "cast"
                          "apply"
                          "in"
                          "isNull" ]
                    )
                ))

    // ---- shared small encoders ----

    let private pairJson (a, b) = JObj [ "a", JStr a; "b", JStr b ]

    let private pairOf el =
        field "a" el
        |> Result.bind strOf
        |> Result.bind (fun a -> field "b" el |> Result.bind strOf |> Result.map (fun b -> a, b))

    let private strList (xs: string list) = JArr(xs |> List.map JStr)

    let private strListOf el = arrOf el |> Result.bind (mapM strOf)

    let private orderJson (name, dir) =
        JObj [ "col", JStr name; "dir", JStr(dirTag dir) ]

    let private orderOf el =
        // Phase 92 — the sort-key aliases: `column` for `col`, boolean `descending` for `dir`.
        fieldAliased "col" "column" el
        |> Result.bind strOf
        |> Result.bind (fun n ->
            // Phase 93 — `direction` is a third observed spelling; a directionless entry is
            // the SQL default (asc) — both unambiguous.
            match tryField "dir" el, tryField "descending" el, tryField "direction" el with
            | Some d, None, None
            | None, None, Some d -> strOf d |> Result.map (fun ds -> n, dirOf ds)
            | None, Some(JBool b), None -> Ok(n, (if b then Desc else Asc))
            | None, Some _, None -> Error(MalformedShape "\"descending\" must be a JSON boolean")
            | None, None, None -> Ok(n, Asc)
            | _ ->
                Error(
                    MalformedShape
                        "give ONE of \"dir\" (canonical: asc|desc), \"descending\" (alias boolean), or \"direction\" (alias: asc|desc)"
                ))

    let private aggJson (a: Agg) =
        JObj [ "name", JStr a.Name; "fn", JStr(aggFnTag a.Fn); "of", JStr a.Of ]

    let private aggOf el =
        // Phase 92 — the aggregate-entry aliases: `as` for `name`, `op` for `fn`, `column` for `of`.
        fieldAliased "name" "as" el
        |> Result.bind strOf
        |> Result.bind (fun name ->
            fieldAliased "fn" "op" el
            |> Result.bind strOf
            |> Result.bind (fun fns ->
                match aggFnOf fns with
                | None ->
                    Error(
                        UnknownType(
                            fns,
                            [ "sum"
                              "mean"
                              "min"
                              "max"
                              "count"
                              "median"
                              "stddev"
                              "first"
                              "last"
                              "countDistinct" ]
                        )
                    )
                | Some fn ->
                    fieldAliased "of" "column" el
                    |> Result.bind strOf
                    |> Result.map (fun ofc -> { Name = name; Fn = fn; Of = ofc })))

    // ---- Transform ----

    let encodeTransform (t: Transform) : JVal =
        match t with
        | Filter pred -> Canon.typed "filter" [ "pred", encodeExpr pred ]
        | Project pairs -> Canon.typed "project" [ "cols", JArr(pairs |> List.map pairJson) ]
        | Derive(name, expr) -> Canon.typed "derive" [ "name", JStr name; "expr", encodeExpr expr ]
        | GroupBy(keys, aggs) -> Canon.typed "groupBy" [ "keys", strList keys; "aggs", JArr(aggs |> List.map aggJson) ]
        | Join(src, on, how) ->
            Canon.typed
                "join"
                [ "source", ColumnCodec.encodeJson src
                  "on", JArr(on |> List.map pairJson)
                  "how", JStr(joinTag how) ]
        | Window spec ->
            let fields =
                [ "partitionBy", strList spec.PartitionBy
                  "orderBy", JArr(spec.OrderBy |> List.map orderJson)
                  "fn", JStr(windowTag spec.Fn)
                  "of", JStr spec.Of
                  "as", JStr spec.As ]

            Canon.typed
                "window"
                // Phase 101 — the bucket count is emitted ONLY for `ntile`, so every pre-existing
                // window step's wire is byte-unchanged. `Canon.render` sorts keys, so position
                // does not matter.
                (match spec.Fn with
                 | NTile buckets -> fields @ [ "n", JInt buckets ]
                 | _ -> fields)
        | Pivot spec ->
            Canon.typed
                "pivot"
                [ "index", strList spec.Index
                  "on", JStr spec.On
                  "values", JStr spec.Values
                  "agg", JStr(aggFnTag spec.Agg) ]
        | Unpivot(idVars, valueVars) ->
            Canon.typed "unpivot" [ "idVars", strList idVars; "valueVars", strList valueVars ]
        | Sort by -> Canon.typed "sort" [ "by", JArr(by |> List.map orderJson) ]
        | Distinct -> Canon.typed "distinct" []
        | Limit(n, offset) -> Canon.typed "limit" [ "n", JInt n; "offset", JInt offset ]
        | Union src -> Canon.typed "union" [ "source", ColumnCodec.encodeJson src ]
        | Intersect src -> Canon.typed "intersect" [ "source", ColumnCodec.encodeJson src ]
        | Except src -> Canon.typed "except" [ "source", ColumnCodec.encodeJson src ]

    let private intOf el =
        match el with
        | JInt i -> Ok i
        | _ -> Error(MalformedShape "expected int")

    let decodeTransform (el: JVal) : Result<Transform, ColumnError> =
        // Phase 88 DIDACTIC — a step without `$type` names the op roster (the
        // pilot-3 "pipeline: missing field: $type" class); ambiguity means no
        // coercion, but the error teaches the shape.
        match kindOf el with
        | Error(MissingField "$type") ->
            Error(
                MalformedShape(
                    "a pipeline step is a $type-discriminated op (filter | project | derive | groupBy | join | window | pivot | unpivot | sort | distinct | limit | union | intersect | except) — this step object has no \"$type\""
                )
            )
        | r ->
            r
            |> Result.bind (fun k ->
                match k with
                | "filter" ->
                    // Phase 89 (lenient-ingest) — the flat filter-step prior:
                    // {"$type":"filter","column":C,"op":O,"param":P|"value":V}
                    // coerces to the canonical nested predicate — exactly one
                    // canonical value, so the coercion is admitted. `pred`
                    // present takes the canonical path untouched.
                    let tryF k =
                        match el with
                        | JObj fields -> fields |> List.tryFind (fun (n, _) -> n = k) |> Option.map snd
                        | _ -> None

                    // Phase 93 — `predicate` aliases `pred` (stretch-wave-2 census).
                    match tryF "pred", tryF "predicate" with
                    | Some _, Some _ ->
                        Error(MalformedShape("give \"pred\" (canonical) or \"predicate\" (alias), not both"))
                    | Some predEl, None
                    | None, Some predEl -> decodeExpr predEl |> Result.map Filter
                    | None, None ->
                        match tryF "column", tryF "op" with
                        | Some colEl, Some opEl ->
                            strOf colEl
                            |> Result.bind (fun col ->
                                strOf opEl
                                |> Result.bind (fun opTag ->
                                    match binOf opTag with
                                    | None ->
                                        Error(
                                            UnknownType(
                                                opTag,
                                                [ "add"
                                                  "sub"
                                                  "mul"
                                                  "div"
                                                  "mod"
                                                  "eq"
                                                  "ne"
                                                  "lt"
                                                  "le"
                                                  "gt"
                                                  "ge"
                                                  "and"
                                                  "or"
                                                  "contains"
                                                  "startsWith"
                                                  "endsWith" ]
                                            )
                                        )
                                    | Some op ->
                                        match tryF "param", tryF "value" with
                                        | Some pEl, None ->
                                            strOf pEl |> Result.map (fun p -> Filter(Binary(op, Col col, Param p)))
                                        | None, Some vEl ->
                                            (match vEl with
                                             | JStr s -> Ok(Str s)
                                             | JInt i -> Ok(Int i)
                                             | JFloat f -> Ok(Float f)
                                             | JBool b -> Ok(Bool b)
                                             | other ->
                                                 ignore other

                                                 Error(
                                                     MalformedShape(
                                                         "flat filter step: \"value\" must be a scalar (string/int/float/bool)"
                                                     )
                                                 ))
                                            |> Result.map (fun cell -> Filter(Binary(op, Col col, Lit cell)))
                                        | Some _, Some _ ->
                                            Error(
                                                MalformedShape(
                                                    "flat filter step: give exactly ONE of \"param\" (a pipeline param name) or \"value\" (a scalar literal), not both"
                                                )
                                            )
                                        | None, None ->
                                            Error(
                                                MalformedShape(
                                                    "flat filter step: {column, op} needs \"param\" (a pipeline param name) or \"value\" (a scalar literal) as the right-hand side"
                                                )
                                            )))
                        | _ ->
                            Error(
                                MalformedShape(
                                    "a filter step carries \"pred\" (a $type-discriminated expression: binary/col/param/lit/apply) — or the flat short form {\"column\":…,\"op\":…,\"param\":…|\"value\":…}"
                                )
                            )
                | "project" ->
                    field "cols" el
                    |> Result.bind arrOf
                    |> Result.bind (mapM pairOf)
                    |> Result.map Project
                | "derive" ->
                    field "name" el
                    |> Result.bind strOf
                    |> Result.bind (fun n ->
                        field "expr" el |> Result.bind decodeExpr |> Result.map (fun e -> Derive(n, e)))
                | "groupBy" ->
                    // Phase 92 — `by` (pandas prior) aliases `keys`; `aggregations` aliases `aggs`.
                    fieldAliased "keys" "by" el
                    |> Result.bind strListOf
                    |> Result.bind (fun keys ->
                        fieldAliased "aggs" "aggregations" el
                        |> Result.bind arrOf
                        |> Result.bind (mapM aggOf)
                        |> Result.map (fun aggs -> GroupBy(keys, aggs)))
                | "join" ->
                    field "source" el
                    |> Result.bind ColumnCodec.decodeJson
                    |> Result.bind (fun src ->
                        field "on" el
                        |> Result.bind arrOf
                        |> Result.bind (mapM pairOf)
                        |> Result.bind (fun on ->
                            field "how" el
                            |> Result.bind strOf
                            |> Result.bind (fun hows ->
                                match joinOf hows with
                                | None ->
                                    Error(UnknownType(hows, [ "inner"; "left"; "right"; "outer"; "semi"; "anti" ]))
                                | Some how -> Ok(Join(src, on, how)))))
                | "window" ->
                    field "partitionBy" el
                    |> Result.bind strListOf
                    |> Result.bind (fun pb ->
                        field "orderBy" el
                        |> Result.bind arrOf
                        |> Result.bind (mapM orderOf)
                        |> Result.bind (fun ob ->
                            field "fn" el
                            |> Result.bind strOf
                            |> Result.bind (fun fns ->
                                // Phase 101 — `ntile` is the one window fn carrying an operand, so
                                // only this decoder (which can see the sibling `"n"` field) builds
                                // it; every other fn is nullary and resolves through `windowOf`.
                                let fnRes =
                                    if fns = "ntile" then
                                        field "n" el |> Result.bind intOf |> Result.map NTile
                                    else
                                        match windowOf fns with
                                        | Some fn -> Ok fn
                                        | None ->
                                            Error(
                                                UnknownType(
                                                    fns,
                                                    [ "rowNumber"
                                                      "rank"
                                                      "lag"
                                                      "lead"
                                                      "cumulSum"
                                                      "rollingMean"
                                                      "denseRank"
                                                      "competitionRank"
                                                      "ntile"
                                                      "cumulMax"
                                                      "cumulMin"
                                                      "rollingSum" ]
                                                )
                                            )

                                fnRes
                                |> Result.bind (fun fn ->
                                    field "of" el
                                    |> Result.bind strOf
                                    |> Result.bind (fun ofc ->
                                        field "as" el
                                        |> Result.bind strOf
                                        |> Result.map (fun asn ->
                                            Window
                                                { PartitionBy = pb
                                                  OrderBy = ob
                                                  Fn = fn
                                                  Of = ofc
                                                  As = asn }))))))
                | "pivot" ->
                    field "index" el
                    |> Result.bind strListOf
                    |> Result.bind (fun index ->
                        field "on" el
                        |> Result.bind strOf
                        |> Result.bind (fun onc ->
                            field "values" el
                            |> Result.bind strOf
                            |> Result.bind (fun vals ->
                                field "agg" el
                                |> Result.bind strOf
                                |> Result.bind (fun aggs ->
                                    match aggFnOf aggs with
                                    | None ->
                                        Error(
                                            UnknownType(
                                                aggs,
                                                [ "sum"
                                                  "mean"
                                                  "min"
                                                  "max"
                                                  "count"
                                                  "median"
                                                  "stddev"
                                                  "first"
                                                  "last"
                                                  "countDistinct" ]
                                            )
                                        )
                                    | Some agg ->
                                        Ok(
                                            Pivot
                                                { Index = index
                                                  On = onc
                                                  Values = vals
                                                  Agg = agg }
                                        )))))
                | "unpivot" ->
                    field "idVars" el
                    |> Result.bind strListOf
                    |> Result.bind (fun idv ->
                        field "valueVars" el
                        |> Result.bind strListOf
                        |> Result.map (fun vv -> Unpivot(idv, vv)))
                | "sort" ->
                    // Phase 92 — `keys` (SQL ORDER-BY-list prior) aliases `by`.
                    fieldAliased "by" "keys" el
                    |> Result.bind arrOf
                    |> Result.bind (mapM orderOf)
                    |> Result.map Sort
                | "distinct" -> Ok Distinct
                | "limit" ->
                    // Phase 92 — `count` aliases `n`; an absent `offset` is unambiguously 0.
                    fieldAliased "n" "count" el
                    |> Result.bind intOf
                    |> Result.bind (fun n ->
                        match tryField "offset" el with
                        | Some o -> intOf o |> Result.map (fun ofs -> Limit(n, ofs))
                        | None -> Ok(Limit(n, 0)))
                | "union" -> field "source" el |> Result.bind ColumnCodec.decodeJson |> Result.map Union
                | "intersect" -> field "source" el |> Result.bind ColumnCodec.decodeJson |> Result.map Intersect
                | "except" -> field "source" el |> Result.bind ColumnCodec.decodeJson |> Result.map Except
                | other ->
                    Error(
                        UnknownType(
                            other,
                            [ "filter"
                              "project"
                              "derive"
                              "groupBy"
                              "join"
                              "window"
                              "pivot"
                              "unpivot"
                              "sort"
                              "distinct"
                              "limit"
                              "union"
                              "intersect"
                              "except" ]
                        )
                    ))

    /// Encode a pipeline (ordered `Transform list`) to a canonical wire string.
    let encodePipeline (pipeline: Transform list) : string =
        Canon.render (JArr(pipeline |> List.map encodeTransform))

    /// Decode a pipeline from a wire string (six-code `ColumnError` envelope; `NotJson` on a
    /// syntax error).
    let decodePipeline (s: string) : Result<Transform list, ColumnError> =
        match Json.parse s with
        | Error m -> Error(NotJson m)
        | Ok(JArr xs) -> mapM decodeTransform xs
        | Ok _ -> Error(MalformedShape "pipeline: expected a JSON array of transform steps")

    /// Decode a pipeline from an already-parsed `JVal` (the `JArr` of step objects) — for a host
    /// that parses the wire with its own JSON reader and bridges to `JVal` (e.g. `Fuaran.UI`'s
    /// decoder converting its `Json` AST). Symmetric with the public `decodeTransform`; the
    /// string-based `decodePipeline` is `Json.parse` ∘ this.
    let decodePipelineJson (el: JVal) : Result<Transform list, ColumnError> =
        match el with
        | JArr xs -> mapM decodeTransform xs
        | _ -> Error(MalformedShape "pipeline: expected a JSON array of transform steps")

    /// The `Corpus.Codec` over a pipeline — string-error decode, so the algebra plugs into the
    /// conformance corpus tooling.
    let pipelineCodec: Corpus.Codec<Transform list> =
        { Encode = encodePipeline
          Decode = fun s -> decodePipeline s |> Result.mapError ColumnCodec.errorString }
