namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Fuaran.Compute.DataFrame (Phase 29) — the declarative-compute layer over the
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
// evaluation below *calls* rather than inlines. It stays `Fuaran.Core.AggFn`, reached here through
// `open Fuaran.Core` since this layer took its own namespace (Phase 322), so every reference is unchanged.

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

/// The grain of a `ColExpr.Now` reading (Phase 125) — the two `Cell` cases that can hold a clock
/// reading, and no more.
///
/// **Why there is no `hour` / `quarter` / `week` ladder.** A grain has to land in a cell, and the
/// columnar model has exactly two that carry a moment: `Cell.Date` (`YYYY-MM-DD`) and
/// `Cell.Timestamp` (`YYYY-MM-DDThh:mm:ssZ`), both canonical ISO-8601 strings. A finer or coarser
/// grain would be vocabulary with no semantics — admitted by the type, meaningless to every
/// evaluator. Truncating an existing moment to a period is `ScalarFn.DatePart`'s job and always was.
[<RequireQualifiedAccess>]
type NowGrain =
    /// A calendar day: `Cell.Date`, `YYYY-MM-DD`.
    | Date
    /// An instant: `Cell.Timestamp`, `YYYY-MM-DDThh:mm:ssZ`.
    | Timestamp

/// Wire tags for `NowGrain`.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module NowGrain =

    let tag (g: NowGrain) : string =
        match g with
        | NowGrain.Date -> "date"
        | NowGrain.Timestamp -> "timestamp"

    let ofTag (s: string) : NowGrain option =
        match s with
        | "date" -> Some NowGrain.Date
        | "timestamp" -> Some NowGrain.Timestamp
        | _ -> None

    /// Both tags, for a decoder's `UnknownType` enumeration (GP5 — a closed set names its
    /// alternatives when it refuses).
    let allTags: string list = [ "date"; "timestamp" ]

/// The clock seam (Phase 125): a pinned reading per grain.
///
/// Core is FSharp.Core-only and Fable-clean (GP3), so it holds no platform clock and never will —
/// the host supplies one. That is not merely a portability constraint: a pipeline that silently
/// picked up wall-clock time at evaluation would not be reproducible, and its cross-host parity
/// claim would not be falsifiable. PINNING the clock is what makes `Now` deterministic, and what
/// `Conformance.nowLaws` certifies.
type ClockWitness = NowGrain -> Cell

/// A SCALAR SLOT in a `Transform` verb (Phase 125): a pinned literal, or a named parameter resolved
/// per evaluation from the same binding environment `ColExpr.Param` reads.
///
/// **Why this exists rather than `ColExpr` at those slots.** A `Limit`'s count and a `SortKey`'s
/// column are positions with no row in scope, so `Col "x"` there is an expression the type would
/// admit and no evaluator could mean; the same goes for every other `ColExpr` case. Default-deny by
/// shape (GP5) says the type admits what the position means and nothing else, and at a scalar slot
/// that is exactly two things.
///
/// **Why it is Core's type rather than each host's.** The alternative is a host-side
/// pre-substitution: a parallel structure carrying "this slot is really a param", maintained
/// outside Core's type at every conformant host, and agreeing with the others only by discipline.
/// One closed type in the shared substrate is what makes the wire form, the param census and the
/// unbound-param refusal one answer instead of five.
///
/// `RequireQualifiedAccess` because `Lit` and `Param` are already `ColExpr` cases in this
/// namespace: `Slot.Lit 10` never shadows `Lit (Int 10)`.
[<RequireQualifiedAccess>]
type Slot<'T> =
    /// The value itself — what every slot held before this type existed.
    | Lit of 'T
    /// A named parameter, resolved from the evaluation env. Unbound at evaluation is a strict
    /// `EvalError.UnboundParam`; bound to a cell of the wrong shape is an `EvalError.TypeError`
    /// naming the slot. It shares the scalar params' namespace, so `Transform.paramsOf` reports it
    /// and a host's dependency edges, reactivity and unbound-param pruning pick it up with no new
    /// call.
    | Param of name: string

/// Pure, total derivations over a scalar slot.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Slot =

    /// The param name this slot references, if any.
    let paramName (s: Slot<'T>) : string list =
        match s with
        | Slot.Lit _ -> []
        | Slot.Param n -> [ n ]

    /// Is this slot still a param? (`true` ⇒ the value is not known without an env.)
    let isParam (s: Slot<'T>) : bool =
        match s with
        | Slot.Lit _ -> false
        | Slot.Param _ -> true

    /// The literal, when the slot holds one.
    let tryLit (s: Slot<'T>) : 'T option =
        match s with
        | Slot.Lit v -> Some v
        | Slot.Param _ -> None

/// How an exact value is brought to a stated number of decimal places (Phase 277). The seven modes
/// of the well-known set — `java.math.RoundingMode` names the same seven — complete on purpose:
/// widening a closed union later is breaking, so the vocabulary ships whole. A mode is a VALUE of
/// this type, so an unknown mode is unrepresentable; its spelling exists only in the wire codec.
///
/// `RequireQualifiedAccess` because `Floor` is already a `ScalarFn` case in this namespace:
/// `RoundingMode.Floor` never shadows it.
[<RequireQualifiedAccess>]
type RoundingMode =
    /// Ties to the even neighbour (banker's rounding).
    | HalfEven
    /// Ties away from zero — the rule `Round` has always pinned.
    | HalfUp
    /// Ties toward zero.
    | HalfDown
    /// Away from zero.
    | Up
    /// Toward zero (truncation).
    | Down
    /// Toward positive infinity.
    | Ceiling
    /// Toward negative infinity.
    | Floor

/// A rounding policy (Phase 277): a SCALE — decimal places, a literal or a named param, as
/// `Limit`'s count is — and a MODE. Typed, so an invalid policy cannot be written: the scale is an
/// integer slot (refused by name at evaluation outside `0 .. 1000`) and the mode one of seven.
type Rounding =
    { Scale: Slot<int>; Mode: RoundingMode }

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
    /// A `now` literal at the declared grain (Phase 125) — a pipeline names the current moment
    /// itself, instead of a host threading a param by hand for it at every site.
    ///
    /// Resolves by SUBSTITUTION against a pinned `ClockWitness` (`ColExpr.substituteNow` /
    /// `Transform.substituteNow`, and the `…At` evaluator entry points that call them) — the same
    /// seam `InParam` resolves through, reused rather than joined by a second mechanism. One that
    /// reaches evaluation unresolved is a strict `EvalError.UnpinnedClock`, never a silent reading
    /// of the host's real clock: Core owns no clock, and a pipeline whose answer depends on when it
    /// ran is not reproducible and its parity claim is not falsifiable.
    ///
    /// Wire: `{"$type":"now","grain":"date"|"timestamp"}`.
    | Now of grain: NowGrain
    /// The quotient of two EXACT numbers, correctly rounded to `rounding` (Phase 277). The exact
    /// quotient of two decimals is not a finite decimal in general — a third has no finite
    /// expansion — so division of exact values always names its rounding: computed by exact long
    /// division to the scale, the remainder deciding the last digit under the mode, never a double
    /// rounding. Operands are `Decimal` or `Int` (an int promotes); a `Float` is refused by name
    /// (`Cast` first). The answer is a `Decimal`; a zero divisor answers `Null`, as `Div` does; a
    /// null operand propagates. Evaluation order: the scale, the dividend, the divisor.
    ///
    /// Wire: `{"$type":"quotient","dividend":…,"divisor":…,"rounding":{"mode":…,"scale":…}}`.
    | Quotient of dividend: ColExpr * divisor: ColExpr * rounding: Rounding
    /// An exact number brought to `rounding` (Phase 277) — the one general place a decimal loses
    /// digits. The operand is a `Decimal` or an `Int` (an int promotes); a `Float` is refused by
    /// name. The answer is a `Decimal`; a null operand propagates. `Round`, `Floor` and `Ceil` over a
    /// decimal are its scale-0 specialisations under `HalfUp`, `Floor` and `Ceiling`.
    ///
    /// Wire: `{"$type":"rounded","expr":…,"rounding":{"mode":…,"scale":…}}`.
    | Rounded of ColExpr * Rounding

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
    /// Order rows by the named keys. The COLUMN of each key is a `Slot` as of `0.23.0`, so a host
    /// can bind "sort by whichever column the user picked" without a parallel structure outside this
    /// type. The DIRECTION stays a literal: nothing asked for a bound direction, and a `Cell`-valued
    /// param would have to spell one as a string.
    | Sort of (Slot<string> * SortDir) list
    | Distinct
    /// Take `n` rows after skipping `offset`. Both are `Slot`s as of `0.23.0` — a page size and a
    /// page offset are the two slots a UI binds most often, and they were literals in this DU.
    | Limit of n: Slot<int> * offset: Slot<int>
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

// ---------------------------------------------------------------------------
//  Phase 269 — the planner's report vocabulary. The planner itself is `Plan` (Plan.fs), over the
//  engine `DataFrame.Planner` below; the types live here because the driver reads them.
// ---------------------------------------------------------------------------

/// One class of rewrite the planner performs (Phase 269). Three classes, and the rule that admits
/// each: FUSION changes which rows each step evaluates not at all and is always admissible;
/// PRUNING drops a column at the earliest step after its last read, never across a step that
/// reads the whole row, and changes no evaluation; REORDERING moves a `Filter` ahead of a step and
/// is admitted only where every evaluation the filter now removes from a row's path is provably
/// total — a `Filter` moved ahead of a `Derive` that could error on the dropped row would remove
/// an error the reference reports.
[<RequireQualifiedAccess>]
type RewriteClass =
    /// `Sort` then `Limit` run as one stable top-n over the pinned comparator with the arrival
    /// index as the tie-break — exactly `List.sortWith` then `List.truncate`, without sorting the
    /// rows the limit discards. The pipeline keeps both steps; the driver runs the pair as the
    /// kernel.
    | TopN
    /// A `Project` inserted where a column's last read is behind it, keeping the live columns in
    /// their order, so no step after it carries the dead column.
    | PruneColumns
    /// A `Filter` moved ahead of the `Sort` it followed.
    | FilterBeforeSort
    /// A `Filter` moved ahead of the `Derive` it followed.
    | FilterBeforeDerive

/// A rewrite the planner applied, at the 0-based index of the written pipeline's step it read.
type PlanRewrite =
    { Class: RewriteClass
      At: int
      Detail: string }

/// A rewrite the planner considered and declined, with the rule that declined it — for a host that
/// wants to know why its pipeline runs as written.
type PlanDeclined =
    { Class: RewriteClass
      At: int
      Reason: string }

/// What the planner did to a pipeline: the pipeline it read, the one it emits, and every rewrite
/// it applied or declined (Phase 269).
type PlanReport =
    { Written: Transform list
      Planned: Transform list
      Applied: PlanRewrite list
      Declined: PlanDeclined list }

/// Pure, total derivations over the `ColExpr` algebra (Phase 77) — the param surface a host reads to
/// derive dependency edges, reactivity subscriptions, and its unbound-param pruning policy. No
/// evaluation, no env: the edge is *computed from the expression*, never separately declared.
[<RequireQualifiedAccess>]
module ColExpr =

    /// Every `Param` name the expression references, in stable left-to-right order **with**
    /// duplicates (recursing through every sub-expression kind). `paramsOf` dedups; the raw walk is
    /// exposed for callers that want occurrence order preserved.
    let rec internal paramNames (e: ColExpr) : string list =
        match e with
        | Col _
        | Lit _
        // A `Now` carries no param name: the clock is a witness, not a binding.
        | Now _ -> []
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
        // A rounding's scale slot shares it too, as `Limit`'s does (Phase 277).
        | Quotient(a, b, r) -> paramNames a @ paramNames b @ Slot.paramName r.Scale
        | Rounded(x, r) -> paramNames x @ Slot.paramName r.Scale

    /// The distinct `Param` names an expression references, first-occurrence order, deduplicated.
    let paramsOf (e: ColExpr) : string list = paramNames e |> List.distinct

    /// A rounding's scale slot bound from `env` (Phase 277) — only to an `Int`, as `Limit`'s count
    /// slot binds: a cell of another shape is left as the param, so evaluation names the slot.
    let private bindScale (env: Map<string, Cell>) (r: Rounding) : Rounding =
        match r.Scale with
        | Slot.Param n ->
            match Map.tryFind n env with
            | Some(Int v) -> { r with Scale = Slot.Lit v }
            | _ -> r
        | Slot.Lit _ -> r


    /// Substitute every `Param n` bound in `env` with `Lit env.[n]` (leaving unbound params intact).
    /// The substitution witness `paramLaws` certifies against: `evalExpr` under `env` ≡ `evalExpr`
    /// over the substituted expression.
    let rec substitute (env: Map<string, Cell>) (e: ColExpr) : ColExpr =
        match e with
        | Col _
        | Lit _
        | Now _ -> e
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
        | Quotient(a, b, r) -> Quotient(substitute env a, substitute env b, bindScale env r)
        | Rounded(x, r) -> Rounded(substitute env x, bindScale env r)

    /// Substitute every `InParam(x, n)` bound in `listEnv` with `InList(x, <items as literals>)`,
    /// leaving unbound list params intact — the list-valued twin of `substitute` (Phase 91). A host
    /// binds a multi-select control's selection here; the "empty selection ⇒ no constraint" idiom
    /// is host-side policy (prune the step), exactly as for scalar params.
    let rec substituteListParams (listEnv: Map<string, Cell list>) (e: ColExpr) : ColExpr =
        match e with
        | Col _
        | Lit _
        | Param _
        | Now _ -> e
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
        | Quotient(a, b, r) -> Quotient(substituteListParams listEnv a, substituteListParams listEnv b, r)
        | Rounded(x, r) -> Rounded(substituteListParams listEnv x, r)

    /// Replace every `Now g` with `Lit (clock g)` — the clock-pinning twin of `substitute`
    /// (Phase 125). A `Now` resolves this way and no other: Core holds no clock, so the reading is
    /// the caller's, taken once and frozen into the expression before anything evaluates.
    ///
    /// **Substitution rather than an evaluator argument, deliberately.** Threading a clock down
    /// `evalExpr` would give every call site a chance to supply a different one, and "the same
    /// pipeline twice under one clock agrees" would then be a claim about call-site discipline.
    /// Substituted, it is a claim about the expression: after this call the pipeline contains no
    /// `Now` at all, so what evaluates is a pure function of literals, and the determinism law
    /// (`Conformance.nowLaws`) is about the value rather than about who called what.
    ///
    /// The witness is read AT MOST ONCE PER GRAIN per call (`pinOnce`): two `Now NowGrain.Timestamp`
    /// nodes in one expression must be the same instant, or a pipeline could straddle a second and
    /// compare a row against two different "now"s.
    let rec substituteNow (clock: ClockWitness) (e: ColExpr) : ColExpr = substituteWithPinned (pinOnce clock) e

    /// One reading per grain, taken on first use. `Lazy` rather than a mutable dictionary:
    /// FSharp.Core only, Fable-clean, and a grain the expression never names is never asked for —
    /// a witness that reads a real clock, or charges for one, is not invoked for a grain nobody
    /// wanted.
    and internal pinOnce (clock: ClockWitness) : ClockWitness =
        let d = lazy (clock NowGrain.Date)
        let ts = lazy (clock NowGrain.Timestamp)

        fun g ->
            match g with
            | NowGrain.Date -> d.Value
            | NowGrain.Timestamp -> ts.Value

    /// The walk itself, over an ALREADY-pinned witness — so the pinning happens once at the entry
    /// point rather than once per recursive step.
    and internal substituteWithPinned (pinned: ClockWitness) (e: ColExpr) : ColExpr =
        let go = substituteWithPinned pinned

        match e with
        | Col _
        | Lit _
        | Param _ -> e
        | Now g -> Lit(pinned g)
        | Binary(op, a, b) -> Binary(op, go a, go b)
        | Not x -> Not(go x)
        | Coalesce xs -> Coalesce(xs |> List.map go)
        | Case(cases, els) -> Case(cases |> List.map (fun (w, t) -> go w, go t), go els)
        | Cast(ty, x) -> Cast(ty, go x)
        | ApplyFn(fn, xs) -> ApplyFn(fn, xs |> List.map go)
        | InList(x, items) -> InList(go x, items |> List.map go)
        | IsNull x -> IsNull(go x)
        | InParam(x, n) -> InParam(go x, n)
        | Quotient(a, b, r) -> Quotient(go a, go b, r)
        | Rounded(x, r) -> Rounded(go x, r)

    /// Does the expression name `now` anywhere? The `paramsOf` analogue for the clock: a host that
    /// needs to know whether a pipeline is clock-dependent (to decide caching, or to refuse to
    /// evaluate one without pinning) reads it off the expression rather than declaring it beside.
    let rec usesNow (e: ColExpr) : bool =
        match e with
        | Now _ -> true
        | Col _
        | Lit _
        | Param _ -> false
        | Binary(_, a, b) -> usesNow a || usesNow b
        | Not x
        | Cast(_, x)
        | IsNull x
        | InParam(x, _) -> usesNow x
        | Coalesce xs
        | ApplyFn(_, xs) -> xs |> List.exists usesNow
        | Case(cases, els) -> (cases |> List.exists (fun (w, t) -> usesNow w || usesNow t)) || usesNow els
        | InList(x, items) -> usesNow x || (items |> List.exists usesNow)
        | Quotient(a, b, _) -> usesNow a || usesNow b
        | Rounded(x, _) -> usesNow x

/// Pure, total derivations over a `Transform` pipeline (Phase 77) — the load-bearing helper for a
/// host that wires a filter/state value into a declarative pipeline: `paramsOf` names every param the
/// pipeline depends on, so dependency edges + reactivity + unbound-param pruning are all *derived*.
[<RequireQualifiedAccess>]
module Transform =

    /// The `Param` names a single step references (only `Filter` / `Derive` carry a `ColExpr`; every
    /// other verb contributes none). Occurrence order, with duplicates.
    let internal stepParamNames (t: Transform) : string list =
        match t with
        | Filter p -> ColExpr.paramNames p
        | Derive(_, e) -> ColExpr.paramNames e
        // `0.23.0` — a SLOT param shares the scalar params' namespace, so it is reported here and
        // nowhere else: a host's dependency edges, reactivity subscriptions and unbound-param
        // pruning all read `paramsOf`, and adding a second census for slots would mean each of them
        // had to learn about it.
        | Sort by -> by |> List.collect (fun (c, _) -> Slot.paramName c)
        | Limit(n, offset) -> Slot.paramName n @ Slot.paramName offset
        | Project _
        | GroupBy _
        | Join _
        | Window _
        | Pivot _
        | Unpivot _
        | Distinct
        | Union _
        | Intersect _
        | Except _ -> []

    /// Every distinct `Param` name a pipeline references, in first-occurrence order across the steps,
    /// deduplicated. Total over every step kind (incl. `Join` / `Window` / `Pivot`, which carry no
    /// param sub-expressions today and so contribute nothing).
    let paramsOf (pipeline: Transform list) : string list =
        pipeline |> List.collect stepParamNames |> List.distinct

    /// Substitute every param bound in `env` through the whole pipeline — each step's `ColExpr`,
    /// and (`0.23.0`) each step's scalar `Slot`s.
    ///
    /// A slot param binds only when the bound cell has the slot's shape: an `Int` at a count slot,
    /// a `Str` at a column slot. A cell of the wrong shape is LEFT UNSUBSTITUTED rather than
    /// coerced, so the defect surfaces at evaluation as a `TypeError` naming the slot, where a
    /// coercion here would have silently sorted by the string "3".
    let substitute (env: Map<string, Cell>) (pipeline: Transform list) : Transform list =
        let bindInt (s: Slot<int>) =
            match s with
            | Slot.Param n ->
                match Map.tryFind n env with
                | Some(Int v) -> Slot.Lit v
                | _ -> s
            | _ -> s

        let bindStr (s: Slot<string>) =
            match s with
            | Slot.Param n ->
                match Map.tryFind n env with
                | Some(Str v) -> Slot.Lit v
                | _ -> s
            | _ -> s

        pipeline
        |> List.map (fun t ->
            match t with
            | Filter p -> Filter(ColExpr.substitute env p)
            | Derive(n, e) -> Derive(n, ColExpr.substitute env e)
            | Sort by -> Sort(by |> List.map (fun (c, d) -> bindStr c, d))
            | Limit(n, offset) -> Limit(bindInt n, bindInt offset)
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

    /// Pin the clock through the whole pipeline (Phase 125): every `ColExpr.Now` becomes the
    /// literal `clock` returns for its grain. ONE reading per grain per call is what makes a
    /// pipeline's two `Now`s agree with each other — a witness that returned a fresh reading per
    /// call would let `Derive("a", Now g)` and `Derive("b", Now g)` disagree within one evaluation,
    /// which is the defect a host threading a param by hand was already avoiding by accident.
    let substituteNow (clock: ClockWitness) (pipeline: Transform list) : Transform list =
        let pinned = ColExpr.pinOnce clock

        pipeline
        |> List.map (fun t ->
            match t with
            | Filter p -> Filter(ColExpr.substituteWithPinned pinned p)
            | Derive(n, e) -> Derive(n, ColExpr.substituteWithPinned pinned e)
            | other -> other)

    /// The literal `Limit` — `Transform.limit 10 0` for the common case, so a call site that never
    /// binds a param reads as it did before `0.23.0`.
    let limit (n: int) (offset: int) : Transform = Limit(Slot.Lit n, Slot.Lit offset)

    /// The literal `Sort` — `Transform.sortBy [ "total", Desc ]`, the pre-`0.23.0` spelling.
    let sortBy (by: (string * SortDir) list) : Transform =
        Sort(by |> List.map (fun (c, d) -> Slot.Lit c, d))

    /// Every SLOT param the pipeline still carries unresolved, first-occurrence order, deduplicated
    /// (`0.23.0`). A subset of `paramsOf`, and the half a STATIC reader has to know about: a slot
    /// param means the step's shape — which column it orders by, how many rows it keeps — is not
    /// known without an env, so a static walk over the pipeline cannot answer for it.
    let slotParamsOf (pipeline: Transform list) : string list =
        pipeline
        |> List.collect (fun t ->
            match t with
            | Sort by -> by |> List.collect (fun (c, _) -> Slot.paramName c)
            | Limit(n, offset) -> Slot.paramName n @ Slot.paramName offset
            | _ -> [])
        |> List.distinct

    /// Does the pipeline name `now` anywhere? The clock's `paramsOf` — a host reads clock
    /// dependence off the pipeline rather than declaring it beside.
    let usesNow (pipeline: Transform list) : bool =
        pipeline
        |> List.exists (fun t ->
            match t with
            | Filter p -> ColExpr.usesNow p
            | Derive(_, e) -> ColExpr.usesNow e
            | _ -> false)

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
    /// A `ColExpr.Now` reached evaluation with no clock pinned (Phase 125). Names the grain that
    /// was asked for. Strict-Core, and the exact analogue of `UnboundParam`: the reference
    /// evaluator never reads a host clock of its own, because an answer that depends on when the
    /// pipeline ran is not reproducible and not comparable across hosts. Pin one with
    /// `DataFrame.evalPipelineAt` (or `Transform.substituteNow` before evaluating).
    | UnpinnedClock of grain: NowGrain

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

/// Row-major access to a columnar table, in ONE pass over the column lists (Phase 206).
///
/// A table stores cells COLUMN-major, and every consumer that wants rows — the reference
/// evaluator's frame, the delta's row tokens, the incremental seam's per-row work units — used to
/// ask for them one index at a time through `Column.cell i c`. That is `List.item` over a linked
/// list, so each cell read walks the column from its head and reading the whole table costs
/// O(rows² × cols). It is why a ten-fold source cost a hundred-fold time, and why the incremental
/// seam evaluated one row expression out of ten thousand and still finished AFTER the full
/// evaluation it replaces.
///
/// Nothing about the representation forced it. The cells are already in row order inside each
/// column, so a row-major view is a TRANSPOSE, and a transpose is linear. The arrays below are the
/// transpose's working storage and never leave this assembly: `Column.Cells` is still a `Cell list`,
/// no public signature moves, and every result is the one the per-index reads produced.
///
/// Internal rather than public, deliberately. This is an access STRATEGY, not a contract — a
/// published helper would commit the packages to it, and a host that stores its columns some other
/// way has no use for it. One implementation for the three files of this package, which is also why
/// it lives here rather than as three private copies.
module internal RowAccess =

    /// Each schema column's cells as an array of exactly `Table.rowCount` entries, in schema order.
    ///
    /// Short and absent columns are padded with `Null`, which is exactly what the per-index reads
    /// answered: `Column.cell` is total and returns `Null` past the end, and a name the table does
    /// not carry resolved to `Null` at every row. The padding is therefore not a new policy — it is
    /// the old one, paid once per column instead of once per cell.
    let columns (t: Table) : Cell[] list =
        let n = Table.rowCount t

        t.Schema
        |> List.map (fun (name, _) ->
            match Table.tryColumn name t with
            | Some c ->
                let a = List.toArray c.Cells

                if a.Length = n then
                    a
                else
                    Array.init n (fun i -> if i < a.Length then a[i] else Null)
            | None -> Array.create n Null)

    /// Every row of the table, in row order — the transpose. Each row is an ARRAY of the schema's
    /// width (Phase 263), so a consumer that reads a cell by its column index pays one load for it
    /// rather than a walk down a row list. The arrays are fresh, and no reader in this assembly
    /// writes into one it did not allocate itself: a derived row is a copy.
    let rows (t: Table) : Cell[] list =
        let cols = columns t |> List.toArray
        let n = Table.rowCount t
        [ for i in 0 .. n - 1 -> cols |> Array.map (fun a -> a[i]) ]

    /// The transpose back: full-width `rows` in row order, under the schema `cols`.
    ///
    /// One read per cell. A row narrower than the schema throws here, as the list form's
    /// `List.item` did; that shape is a defect upstream and staying loud about it is the point.
    let toColumns (cols: Schema) (rows: Cell[] list) : Column list =
        cols
        |> List.mapi (fun ci (name, ty) -> Column.create name ty (rows |> List.map (fun r -> r[ci])))

/// Exact arithmetic over decimal text (Phase 277) — what the evaluator computes a `Decimal` cell's
/// `Sub`, `Mul`, `Mod`, negation, `Abs`, `Floor`, `Ceil`, `Round`, `Quotient` and `Rounded` with. The column layer's
/// `DecimalText` owns the canonical form, the order and the sum (Core `DECISIONS.md` D72 K7); this
/// is the rest of the arithmetic, which that record names the evaluator's.
///
/// Every value is held as `(negative, magnitude digits, scale)` — the number `±digits × 10^-scale`,
/// the digits a string with no leading zero (a lone `0` for zero) — and every answer is rendered
/// back through `DecimalText.tryCanonical`, so the evaluator only ever emits canonical text.
/// Arbitrary precision: the digits are strings, so nothing overflows; and no host `decimal`, whose
/// range differs by host. FSharp.Core only, Fable-clean.
///
/// `Add`, `Sub`, `Mul`, `Mod`, negation and `Abs` are CLOSED over finite decimals and exact. A
/// quotient is not (a third has no finite expansion), and neither is a value brought to fewer
/// places, so the ONE rounding kernel — `quantize` and `divide`, both over `roundQuotient` — takes a
/// `RoundingMode` and a scale. The modes are values; no mode is spelled here.
[<RequireQualifiedAccess>]
module internal DecimalArith =

    /// The largest scale a `Rounding` may name. A quotient is computed to the scale it
    /// names, one digit at a time, so the bound is a resource limit rather than a statement about
    /// decimals — a scale a pipeline cannot have meant would otherwise cost what it names.
    let maxScale: int = 1000

    let private isZeroDigits (d: string) : bool = d |> Seq.forall (fun c -> c = '0')

    /// Strip leading zeros, keeping a lone `0`.
    let private trimLeading (d: string) : string =
        let t = d.TrimStart '0'
        if t.Length = 0 then "0" else t

    /// `(negative, digits, scale)` of a decimal text, or `None` where it is not decimal text.
    let parts (s: string) : (bool * string * int) option =
        DecimalText.tryCanonical s
        |> Option.map (fun c ->
            let negative = c.StartsWith "-"
            let body = if negative then c.Substring 1 else c
            let dot = body.IndexOf '.'

            if dot < 0 then
                negative, body, 0
            else
                negative, trimLeading (body.Substring(0, dot) + body.Substring(dot + 1)), body.Length - dot - 1)

    /// The canonical text of `±digits × 10^-scale`.
    let render (negative: bool) (digits: string) (scale: int) : string =
        let digits = trimLeading digits

        let text =
            if scale <= 0 then
                digits + System.String('0', -scale)
            else
                let padded = digits.PadLeft(scale + 1, '0')

                padded.Substring(0, padded.Length - scale)
                + "."
                + padded.Substring(padded.Length - scale)

        let signed =
            if negative && not (isZeroDigits digits) then
                "-" + text
            else
                text

        DecimalText.tryCanonical signed |> Option.defaultValue signed

    let private digit (c: char) : int = int c - int '0'

    /// Compare two magnitudes (no leading zeros).
    let private compareMag (a: string) (b: string) : int =
        if a.Length <> b.Length then
            compare a.Length b.Length
        else
            sign (System.String.CompareOrdinal(a, b))

    let private addMag (a: string) (b: string) : string =
        let n = max a.Length b.Length
        let a = a.PadLeft(n, '0')
        let b = b.PadLeft(n, '0')
        let out = Array.zeroCreate<char> (n + 1)
        let mutable carry = 0

        for i in n - 1 .. -1 .. 0 do
            let d = digit a[i] + digit b[i] + carry
            out[i + 1] <- char (int '0' + d % 10)
            carry <- d / 10

        out[0] <- char (int '0' + carry)
        trimLeading (System.String out)

    /// `a - b` for magnitudes with `a >= b`.
    let private subMag (a: string) (b: string) : string =
        let b = b.PadLeft(a.Length, '0')
        let out = Array.zeroCreate<char> a.Length
        let mutable borrow = 0

        for i in a.Length - 1 .. -1 .. 0 do
            let d = digit a[i] - digit b[i] - borrow

            if d < 0 then
                out[i] <- char (int '0' + d + 10)
                borrow <- 1
            else
                out[i] <- char (int '0' + d)
                borrow <- 0

        trimLeading (System.String out)

    let private mulMag (a: string) (b: string) : string =
        if a = "0" || b = "0" then
            "0"
        else
            let acc = Array.zeroCreate<int> (a.Length + b.Length)

            for i in a.Length - 1 .. -1 .. 0 do
                for j in b.Length - 1 .. -1 .. 0 do
                    acc[i + j + 1] <- acc[i + j + 1] + digit a[i] * digit b[j]

            for k in acc.Length - 1 .. -1 .. 1 do
                acc[k - 1] <- acc[k - 1] + acc[k] / 10
                acc[k] <- acc[k] % 10

            trimLeading (System.String(acc |> Array.map (fun d -> char (int '0' + d))))

    /// Quotient and remainder of two magnitudes, `b` non-zero — long division, one digit at a time.
    let private divModMag (a: string) (b: string) : string * string =
        let q = System.Text.StringBuilder()
        let mutable r = "0"

        for c in a do
            r <- trimLeading (r + string c)
            let mutable d = 0

            while compareMag r b >= 0 do
                r <- subMag r b
                d <- d + 1

            q.Append(char (int '0' + d)) |> ignore

        trimLeading (q.ToString()), r

    let private shift (digits: string) (places: int) : string =
        if digits = "0" then
            digits
        else
            digits + System.String('0', places)

    /// Signed addition of `(negative, magnitude)` pairs.
    let private addSigned (na: bool, ma: string) (nb: bool, mb: string) : bool * string =
        if na = nb then
            na, addMag ma mb
        else
            match compareMag ma mb with
            | 0 -> false, "0"
            | c when c > 0 -> na, subMag ma mb
            | _ -> nb, subMag mb ma

    /// Both operands at their common scale.
    let private aligned (sa: int, da: string) (sb: int, db: string) : int * string * string =
        let scale = max sa sb
        scale, shift da (scale - sa), shift db (scale - sb)

    let negate (a: string) : string option =
        parts a |> Option.map (fun (n, d, s) -> render (not n) d s)

    let abs (a: string) : string option =
        parts a |> Option.map (fun (_, d, s) -> render false d s)

    let add (a: string) (b: string) : string option =
        match parts a, parts b with
        | Some(na, da, sa), Some(nb, db, sb) ->
            let scale, ma, mb = aligned (sa, da) (sb, db)
            let n, m = addSigned (na, ma) (nb, mb)
            Some(render n m scale)
        | _ -> None

    let sub (a: string) (b: string) : string option = negate b |> Option.bind (add a)

    let mul (a: string) (b: string) : string option =
        match parts a, parts b with
        | Some(na, da, sa), Some(nb, db, sb) -> Some(render (na <> nb) (mulMag da db) (sa + sb))
        | _ -> None

    /// The remainder of a truncating division, with the dividend's sign — the integer `Mod`'s
    /// convention, and exact: both operands at one scale, the remainder is at that scale. `None`
    /// for a zero divisor, which the evaluator answers `Null` as it does for an integer `Mod`.
    let rem (a: string) (b: string) : string option =
        match parts a, parts b with
        | Some(na, da, sa), Some(_, db, sb) when not (isZeroDigits db) ->
            let scale, ma, mb = aligned (sa, da) (sb, db)
            let _, r = divModMag ma mb
            Some(render na r scale)
        | _ -> None

    /// `numerator / denominator` (magnitudes, the denominator non-zero) rounded to an integer under
    /// `mode`, where `negative` is the sign of the exact quotient: the quotient's digits by long
    /// division, then the remainder decides the last one — twice the remainder against the divisor
    /// for the three half-way modes, its being non-zero for the four directed ones. One rounding,
    /// of the exact value: never a double rounding.
    let private roundQuotient (mode: RoundingMode) (negative: bool) (numerator: string) (denominator: string) : string =
        let q, r = divModMag numerator denominator

        if r = "0" then
            q
        else
            let half = compareMag (addMag r r) denominator
            let bump = addMag q "1"

            match mode with
            | RoundingMode.Down -> q
            | RoundingMode.Up -> bump
            | RoundingMode.Ceiling -> if negative then q else bump
            | RoundingMode.Floor -> if negative then bump else q
            | RoundingMode.HalfUp -> if half >= 0 then bump else q
            | RoundingMode.HalfDown -> if half > 0 then bump else q
            | RoundingMode.HalfEven ->
                if half > 0 then bump
                elif half < 0 then q
                elif digit q[q.Length - 1] % 2 = 0 then q
                else bump

    let private inScale (scale: int) : bool = scale >= 0 && scale <= maxScale

    /// `decimal` brought to `scale` places under `mode` — exact (the value itself) where it already
    /// has no more places than that. `None` where the text is not decimal text, or the scale is
    /// outside `0 .. maxScale` (the evaluator refuses such a scale by name before it gets here).
    let quantize (mode: RoundingMode) (scale: int) (decimal: string) : string option =
        match parts decimal with
        | Some(n, d, s) when inScale scale ->
            if s <= scale then
                Some(render n d s)
            else
                Some(render n (roundQuotient mode n d (shift "1" (s - scale))) scale)
        | _ -> None

    /// `dividend / divisor` correctly rounded to `scale` places under `mode`. `Some None` for a zero
    /// divisor; `None` where an operand is not decimal text or the scale is outside `0 .. maxScale`.
    let divide (mode: RoundingMode) (scale: int) (dividend: string) (divisor: string) : string option option =
        match parts dividend, parts divisor with
        | Some(na, da, sa), Some(nb, db, sb) when inScale scale ->
            if isZeroDigits db then
                Some None
            else
                // dividend / divisor = (da / db) * 10^(sb - sa); scaled by 10^scale, the integer to
                // round is da * 10^e / db with e = sb - sa + scale.
                let e = sb - sa + scale
                let numerator = if e >= 0 then shift da e else da
                let denominator = if e >= 0 then db else shift db (-e)
                let negative = (na <> nb) && not (isZeroDigits da)
                Some(Some(render negative (roundQuotient mode negative numerator denominator) scale))
        | _ -> None

    /// The decimal text of a FINITE float — its shortest round-trip digits (`FloatLayout.finite`,
    /// one layout on every host) laid out without an exponent. `None` for NaN and the infinities.
    /// This is the one place an approximation enters a decimal: the digits are the float's, and
    /// the float was already the nearest it could hold to whatever it was meant to be.
    let ofFloat (f: float) : string option =
        if System.Double.IsNaN f || System.Double.IsInfinity f then
            None
        else
            let text = FloatLayout.finite f
            let e = text.IndexOfAny [| 'E'; 'e' |]

            if e < 0 then
                DecimalText.tryCanonical text
            else
                let mantissa = text.Substring(0, e)
                let exponent = int (text.Substring(e + 1))

                parts mantissa
                |> Option.map (fun (n, d, s) ->
                    let scale = s - exponent

                    if scale >= 0 then
                        render n d scale
                    else
                        render n (shift d (-scale)) 0)

    /// The integer part of a decimal (truncated toward zero) as an `int64`, or `None` past `int64`.
    let truncateToInt64 (a: string) : int64 option =
        match parts a with
        | Some(n, d, s) ->
            let ip = if s >= d.Length then "0" else d.Substring(0, d.Length - s)

            match System.Int64.TryParse ip with
            | true, v -> Some(if n then -v else v)
            | _ -> None
        | None -> None

/// The pure reference evaluator + the algebra's pinned semantics. Every host evaluator is
/// certified byte-identical to this through `Conformance.transformLaws`.
module DataFrame =

    // ---- the evaluator's working form ----
    //
    // The frame is column-major and typed (Phase 267; `Frame.fs`): one vector per column beside a
    // validity mask, a selection vector for the rows a step kept and their order, and the `Table`
    // boundary paid once each way (`Frame.ofTable` / `Frame.toTable`). Rows were arrays (Phase 263)
    // and lists before that; the cells a pipeline produces are the same under every representation,
    // and the transform law vectors hold them so.

    let private colIndex (cols: Schema) (name: string) : int option =
        cols |> List.tryFindIndex (fun (n, _) -> n = name)

    let private colType (cols: Schema) (name: string) : ColumnType option =
        cols |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd

    let private available (cols: Schema) : string list = cols |> List.map fst

    /// Map a `Result`-returning function over a list, short-circuiting on the first `Error` (the
    /// standard traverse — threads `EvalError` out of per-element work without an exception).
    ///
    /// A loop, not a recursion through `Result.bind` (Phase 265): that recursion is a tail call only
    /// where the compiler emits one, so a build without tail calls, and every JavaScript host, spent a
    /// stack frame per element — and a group-by over twenty thousand distinct keys traverses twenty
    /// thousand groups. Same order, same first error.
    let private traverseResult (f: 'a -> Result<'b, EvalError>) (xs: 'a list) : Result<'b list, EvalError> =
        let mutable acc = []
        let mutable rest = xs
        let mutable failed = None

        while Option.isNone failed && not (List.isEmpty rest) do
            match f (List.head rest) with
            | Ok y ->
                acc <- y :: acc
                rest <- List.tail rest
            | Error e -> failed <- Some e

        match failed with
        | Some e -> Error e
        | None -> Ok(List.rev acc)

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
        | Decimal s -> DecimalText.tryCanonical s |> Option.defaultValue s
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
        // Phase 277: the CANONICAL decimal text under its own tag, so `1.50` and `1.5` are one value
        // and a decimal never shares a token with an int or a float of the same value — Core's
        // `Cell.token` spelling, which this function agrees with case for case.
        | Decimal _ -> Cell.token c
        | Null -> "n:"

    /// The canonical, host-deterministic grouping/dedup key for a row's cells — `cellToken` per cell,
    /// so `GroupBy` / `Distinct` / `Window` / `Pivot` partition identically on every host.
    let rowToken (cells: Cell list) : string list = cells |> List.map cellToken

    /// One cell token's length-prefixed form — the injective encoding `rowTokenString` joins.
    let private lengthPrefixed (t: string) : string = string (String.length t) + ":" + t

    /// The single-string form of a row's canonical token, LENGTH-PREFIXED per cell so it is
    /// injective (Phase 98). A bare concatenation gives `["a"; "bc"]` and `["ab"; "c"]` the same
    /// string — a collision between two distinct rows, which is exactly what a token exists to
    /// prevent. Row identity and row-content comparison both key on this.
    let rowTokenString (cells: Cell list) : string =
        rowToken cells |> List.map lengthPrefixed |> String.concat ""

    /// `rowTokenString` over a row held as an array (Phase 263) — the same string, cell for cell,
    /// built through the same `cellToken` and the same `lengthPrefixed`, so the two cannot disagree.
    let internal rowTokenStringOfArray (cells: Cell[]) : string =
        cells |> Array.map (cellToken >> lengthPrefixed) |> String.concat ""

    /// Where a probe of `OpenSlots` starts: the hash folded so its high bits reach the mask.
    let private probeStart (h: int) (mask: int) : int =
        (h ^^^ (h >>> 16) ^^^ (h >>> 8)) &&& mask

    /// The JavaScript host's slot table (Phase 326): keys to the slot numbers a caller gives them,
    /// by linear probing over the key's hash in power-of-two arrays kept at most half full. It
    /// stands where `Dictionary` stands on .NET wherever the evaluator numbers keys — the token
    /// slots (`CellKey`), the row hasher's codes and its code-tuple index (`RowHash`) — because the
    /// Fable runtime's dictionary hashes into a map of buckets and reaches a custom comparer
    /// through an interface on every probe. Find and add only, never remove; an entry whose value
    /// is `-1` is empty, so the keys array's initial fill (null, or the empty string for a string
    /// array) is never read as a key. The table numbers nothing itself: a slot is the number its
    /// caller passed on the add, so the order slots are numbered in is the order the caller met
    /// its keys — first-seen for groups — whatever the hashes. Compiled on both hosts, so the suite
    /// holds it to `Dictionary` on .NET; only the JavaScript build reaches it from the evaluator.
    [<Sealed>]
    type internal OpenSlots<'K>(hashOf: 'K -> int, same: 'K -> 'K -> bool) =
        let mutable cap = 16
        let mutable hashes: int[] = Array.zeroCreate 16
        let mutable values: int[] = Array.create 16 -1
        let mutable keys: 'K[] = Array.zeroCreate 16
        let mutable count = 0

        /// Every index below is masked by `cap - 1`, and every array is `cap` long: the mask is the
        /// range proof the unchecked accessors need.
        let place (h: int) (k: 'K) (v: int) : unit =
            let mask = cap - 1
            let mutable i = probeStart h mask

            while Raw.get i values >= 0 do
                i <- (i + 1) &&& mask

            Raw.set hashes i h
            Raw.set keys i k
            Raw.set values i v

        let grow () : unit =
            let oldHashes = hashes
            let oldKeys = keys
            let oldValues = values
            cap <- cap * 2
            hashes <- Array.zeroCreate cap
            keys <- Array.zeroCreate cap
            values <- Array.create cap -1

            for i in 0 .. oldValues.Length - 1 do
                let v = Raw.get i oldValues

                if v >= 0 then
                    place (Raw.get i oldHashes) (Raw.get i oldKeys) v

        /// The number of keys held.
        member _.Count = count

        /// `k`'s slot, or `-1` when the table does not hold it.
        member _.Find(k: 'K) : int =
            let h = hashOf k
            let mask = cap - 1
            let mutable i = probeStart h mask
            let mutable r = -2

            while r = -2 do
                let v = Raw.get i values

                if v < 0 then
                    r <- -1
                elif Raw.get i hashes = h && same (Raw.get i keys) k then
                    r <- v
                else
                    i <- (i + 1) &&& mask

            r

        /// Hold `k` at slot `v` (`v >= 0`); the caller has found `k` absent. The table then owns
        /// `k`, so a caller reusing a probe buffer passes a copy.
        member _.Add(k: 'K, v: int) : unit =
            if (count + 1) * 2 > cap then
                grow ()

            place (hashOf k) k v
            count <- count + 1

    // ---- the two cell equalities the evaluator partitions by (Phase 265) ----
    //
    // Two relations over cells sit side by side here, and they are DIFFERENT on purpose:
    //
    //   * TOKEN equality — `CellKey` below: `cellToken` string equality without the string. Type-tagged
    //     (`Int 1` and `Float 1.0` are two values), `-0.0` equal to `0.0`, one `NaN` bucket, and `Null` a
    //     value equal to itself. `GroupBy`, `Distinct`, `Intersect`/`Except`, `Pivot`'s index groups,
    //     `Window`'s partitions and the seam's maintained grouping partition on it.
    //   * `cellEq` equality — `cellEq` below: a join key's and a pivot on-value's match. The numeric
    //     family compares as floats (`Int 1` MATCHES `Float 1.0`), and a `Null` matches nothing,
    //     itself included.
    //
    // The verbs hash by both through one typed row hasher (`RowHash`, Phase 325), whose coder takes the
    // relation as an argument.
    //
    // A verb that swapped one for the other would still type-check and would silently merge or split
    // groups, so each call site names the relation it means.

    /// Token equality over cells as an equality and a hash with no string minted (Phase 265). Two cells
    /// are `equals` EXACTLY when their `cellToken`s are the same string, and `equals` cells `hashCell` alike,
    /// so a hash table keyed through `row` partitions exactly as a map keyed on the token did.
    /// The float case is the one to read: a finite float's token is its canonical round-trip layout,
    /// which is injective except that `-0.0` and `0.0` share `"0"` — exactly IEEE equality — while the
    /// three non-finite tokens are fixed, so `NaN` is equal to `NaN` here although IEEE says it is not.
    /// A law in the suite holds `equals a b = (cellToken a = cellToken b)` over cells drawn to reach
    /// every case.
    ///
    /// Internal: a hash table's comparer is how the evaluator partitions, not a surface a consumer
    /// composes; `cellToken` / `rowTokenString` stay the public statement of the rule, and the delta
    /// layer still keys row identity on them.
    module internal CellKey =

        /// Token equality between two cells — `cellToken a = cellToken b`, computed without the tokens.
        let equals (a: Cell) (b: Cell) : bool =
            match a, b with
            | Int x, Int y -> x = y
            | Float x, Float y -> x = y || (System.Double.IsNaN x && System.Double.IsNaN y)
            | Bool x, Bool y -> x = y
            | Str x, Str y
            | Date x, Date y
            | Timestamp x, Timestamp y -> System.String.Equals(x, y)
            | Decimal _, Decimal _ -> System.String.Equals(Cell.token a, Cell.token b)
            | Null, Null -> true
            | _ -> false

        /// A hash that agrees with `equals`: the tag is mixed in, `-0.0` hashes as `0.0` and every `NaN`
        /// alike. Mixed with bitwise operators only, so the value stays a 32-bit integer on every host.
        let hashCell (c: Cell) : int =
            let mix (tag: int) (h: int) = (tag <<< 24) ^^^ h

            match c with
            | Int i -> mix 1 (hash i)
            | Float f ->
                if System.Double.IsNaN f then mix 2 0x7ff80000
                elif f = 0.0 then mix 2 0
                else mix 2 (hash f)
            | Bool b -> mix 3 (if b then 1 else 0)
            | Str s -> mix 4 (hash s)
            | Date s -> mix 5 (hash s)
            | Timestamp s -> mix 6 (hash s)
            | Null -> mix 7 0
            | Decimal _ -> mix 8 (hash (Cell.token c))

        /// Token equality over rows of cells, cell by cell: two rows are equal exactly when their
        /// `rowTokenString`s are (that string is length-prefixed per cell and so injective: equal
        /// strings mean equal lengths and equal tokens position by position).
        let row: System.Collections.Generic.IEqualityComparer<Cell[]> =
            { new System.Collections.Generic.IEqualityComparer<Cell[]> with
                member _.Equals(a, b) =
                    if a.Length <> b.Length then
                        false
                    else
                        let mutable same = true
                        let mutable i = 0

                        while same && i < a.Length do
                            same <- equals a[i] b[i]
                            i <- i + 1

                        same

                member _.GetHashCode cells =
                    let mutable h = cells.Length

                    for c in cells do
                        h <- ((h <<< 5) ^^^ (h >>> 27)) ^^^ hashCell c

                    h }

#if FABLE_COMPILER
        /// A fresh slot table: token-equal rows of cells to a slot number. Under JavaScript the open
        /// slot table (Phase 326), over `row`'s hash and equality.
        let slots () : OpenSlots<Cell[]> =
            OpenSlots<Cell[]>((fun cells -> row.GetHashCode cells), (fun a b -> row.Equals(a, b)))

        /// Look `probe`'s cells up in `table`, opening slot `next` on a miss; returns the slot and
        /// whether this call opened it — `slotOf` below, over the open slot table.
        let inline slotOf (table: OpenSlots<Cell[]>) (probe: Cell[]) (next: int) : int * bool =
            let s = table.Find probe

            if s >= 0 then
                s, false
            else
                table.Add(Array.copy probe, next)
                next, true
#else
        /// A fresh slot table: token-equal rows of cells to a slot number.
        let slots () : System.Collections.Generic.Dictionary<Cell[], int> =
            System.Collections.Generic.Dictionary<Cell[], int>(row)

        /// Look `probe`'s cells up in `table`, opening slot `next` on a miss; returns the slot and
        /// whether this call opened it. Every caller passes the count of the groups it has opened,
        /// so slots number from 0 in first-appearance order, the order every caller emits in. The
        /// count is the CALLER's and never `table.Count`: the Fable runtime's dictionary counts its
        /// entries by walking every bucket, so reading it once per opened group made a group-by
        /// over ten thousand keys quadratic under JavaScript (measured: 190 ms against 9 ms for a
        /// `Distinct` over the same rows). The probe is a scratch buffer the caller refills per
        /// row, so a row that finds its slot allocates nothing; only a row that OPENS a slot stores
        /// a copy, which the table then owns — the caller may overwrite the probe freely.
        let inline slotOf
            (table: System.Collections.Generic.Dictionary<Cell[], int>)
            (probe: Cell[])
            (next: int)
            : int * bool =
            match table.TryGetValue probe with
            | true, s -> s, false
            | _ ->
                table[Array.copy probe] <- next
                next, true
#endif

    /// A total comparison between two *present, same-family* cells. `None` ⇒ incomparable (a type
    /// error). Numerics compare as float; strings/date/timestamp by ordinal (ISO sorts
    /// chronologically); bool false < true.
    /// The numeric family's comparison, in the float carrier every numeric cell is compared in. One
    /// definition, because the compiled comparison kernel (Phase 266) and `compareCells` must agree
    /// on every float, `NaN` and `-0.0` included, on every host — two calls to one function do.
    /// Since Phase 321 it is `Kernels.compareFloat` — `NaN` one value above every other, the
    /// substrate's `Cell.compare` order — and no longer the host's `compare`, which ordered `NaN`
    /// first on .NET and did not order it at all under Fable.
    let private compareNum (x: float) (y: float) : int = Kernels.compareFloat x y

    /// A numeric cell's value in the float carrier. Total only over the numeric family; a caller
    /// has matched the family before it reads this.
    let private numOf (c: Cell) : float =
        match c with
        | Int i -> float i
        | Float f -> f
        | Bool _
        | Str _
        | Date _
        | Timestamp _
        | Decimal _
        | Null -> nan

    let private compareCells (a: Cell) (b: Cell) : int option =
        match a, b with
        | (Int _ | Float _), (Int _ | Float _) -> Some(compareNum (numOf a) (numOf b))
        // Phase 277: a decimal against a decimal or an int compares EXACTLY (`Cell.compare`, the
        // column layer's one order, `DecimalText.compare` underneath); against a float it is
        // incomparable, a type error, as `ColumnType.widens` refuses that retype.
        | Decimal _, (Decimal _ | Int _)
        | Int _, Decimal _ -> Cell.compare a b
        | Bool x, Bool y -> Some(compare x y)
        | Str x, Str y -> Some(System.String.CompareOrdinal(x, y))
        | Date x, Date y -> Some(System.String.CompareOrdinal(x, y))
        | Timestamp x, Timestamp y -> Some(System.String.CompareOrdinal(x, y))
        | _ -> None

    let private cellEq (a: Cell) (b: Cell) : bool =
        match a, b with
        | Null, _
        | _, Null -> false
        // A decimal matches only a decimal (see `RowHash`'s coder, which this relation must agree with).
        | Decimal _, Decimal _ -> Cell.compare a b = Some 0
        | Decimal _, _
        | _, Decimal _ -> false
        | _ ->
            match compareCells a b with
            | Some 0 -> true
            | _ -> false

    /// The least column type two present cells' types both widen into (Phase 321): the type
    /// itself, or the wider of a pair `ColumnType.widens` relates — `Int` and `Float` join at
    /// `Float`, `Int` and `Decimal` at `Decimal` — and `None` for any other pair, `Float` beside
    /// `Decimal` included (`widens` refuses that retype in both directions). The derived column's
    /// `inferType` reads it; the typer's `Typing.join` deliberately does not (see there).
    let internal joinColumnType (a: ColumnType) (b: ColumnType) : ColumnType option =
        if ColumnType.widens a b then Some b
        elif ColumnType.widens b a then Some a
        else None

    /// One step of a derived column's typing over its present cells in row order (Phase 321): the
    /// join where the two types have one, and the EARLIER type where they have none — the
    /// first-present rule the column was typed by before the join, kept for a pair no widening
    /// relates, so a column whose cells span two families is typed exactly as it was.
    let internal widenColumnType (acc: ColumnType) (t: ColumnType) : ColumnType =
        match joinColumnType acc t with
        | Some j -> j
        | None -> acc

    /// Range-check an `int64` arithmetic result against the `int32` band (Phase 39). A value outside
    /// the band is a named `OverflowError`, never a silent two's-complement wrap (which diverges
    /// .NET-vs-JS and breaks three-host parity). `ctx` names the operation for the message.
    let private inInt32Range (r: int64) : bool =
        r >= int64 System.Int32.MinValue && r <= int64 System.Int32.MaxValue

    /// The named overflow an integer operation `ctx` reports for the out-of-band result `r` — the
    /// one message, whether the reference arm or the compiled integer kernel (Phase 266) raises it.
    let private overflowed (ctx: string) (r: int64) : EvalError =
        OverflowError(ctx + " overflowed int32: " + string r)

    let private checkedInt (ctx: string) (r: int64) : Result<Cell, EvalError> =
        if inInt32Range r then
            Ok(Int(int r))
        else
            Error(overflowed ctx r)

#if FABLE_COMPILER
    /// Is the float `r` inside the int32 band? (Phase 326.)
    let inline private inInt32Band (r: float) : bool = r >= -2147483648.0 && r <= 2147483647.0

    /// The JavaScript host's `checkedInt` (Phase 326): an int64 is a BigInt there, so the integer
    /// operation `op` over the int32 operands `x` and `y` is carried in float64 — its result `r` —
    /// and recomputed in int64 only when `r` leaves the int32 band, so the refusal and its message
    /// are the .NET host's, byte for byte. `r` is exact wherever it is inside the band: a sum or a
    /// difference of two int32s is below 2^33, a remainder below its divisor, and a product inside
    /// the band is below 2^53 — while a product past 2^53 rounds to a float that is still past the
    /// band (rounding is monotone and 2^31 is a float), so it is recomputed.
    let private checkedFloatInt (op: BinOp) (ctx: string) (r: float) (x: float) (y: float) : Result<Cell, EvalError> =
        if inInt32Band r then
            Ok(Int(int r))
        else
            let xi = int64 x
            let yi = int64 y

            checkedInt
                ctx
                (match op with
                 | Sub -> xi - yi
                 | Mul -> xi * yi
                 | Mod -> xi % yi
                 | _ -> xi + yi)
#endif

    /// A cell as exact-decimal text — a `Decimal`'s, or an `Int`'s digits (the lossless promotion
    /// `ColumnType.widens` pins) — or `None` for any other cell.
    let private decimalText (c: Cell) : string option =
        match c with
        | Decimal s -> Some s
        | Int i -> Some(string i)
        | _ -> None

    /// The refusal a `Decimal` beside a `Float` meets (Phase 277), naming both types and the `Cast`
    /// that resolves it: `widens` refuses that retype in either direction, so nothing converts
    /// between them unless the pipeline says which way.
    let private decimalFloatMismatch (what: string) : EvalError =
        TypeError(
            what
            + " between a decimal and a float: cast one to the other's type first — Cast(decimal, …) "
            + "to keep the digits exact, Cast(float, …) to compute approximately"
        )

    /// A decimal answer, or the refusal for an operand that is not decimal text.
    let private decimalResult (ctx: string) (r: string option) : Result<Cell, EvalError> =
        match r with
        | Some text -> Ok(Decimal text)
        | None -> Error(TypeError(ctx + ": an operand is not decimal text"))

    /// `Add` / `Sub` / `Mul` / `Mod` over two exact operands, at least one a decimal (Phase 277): exact,
    /// and a `Decimal`. `Div` is refused by name — a quotient of decimals is not a finite decimal in
    /// general, so the pipeline names its rounding through `Quotient`.
    let private decimalArith (op: BinOp) (x: string) (y: string) : Result<Cell, EvalError> =
        match op with
        | Add -> decimalResult "add" (DecimalArith.add x y)
        | Sub -> decimalResult "sub" (DecimalArith.sub x y)
        | Mul -> decimalResult "mul" (DecimalArith.mul x y)
        | Mod ->
            match DecimalArith.parts y with
            | Some(_, "0", _) -> Ok Null
            | _ -> decimalResult "mod" (DecimalArith.rem x y)
        | Div ->
            Error(
                TypeError
                    "division of an exact decimal names its rounding: write Quotient(dividend, divisor, { Scale; Mode })"
            )
        | _ -> Error(TypeError "not an arithmetic operator")

    let private arith (op: BinOp) (a: Cell) (b: Cell) : Result<Cell, EvalError> =
        match a, b with
        | Null, _
        | _, Null -> Ok Null
        | Decimal _, (Decimal _ | Int _)
        | Int _, Decimal _ ->
            match decimalText a, decimalText b with
            | Some x, Some y -> decimalArith op x y
            | _ -> Error(TypeError "arithmetic on a non-numeric operand")
        | Decimal _, Float _
        | Float _, Decimal _ -> Error(decimalFloatMismatch "arithmetic")
        | _ ->
            match asNum a, asNum b with
            | Some x, Some y ->
                let bothInt =
                    match a, b with
                    | Int _, Int _ -> true
                    | _ -> false

#if FABLE_COMPILER
                // Under JavaScript an int64 is a BigInt (Phase 326), so the integer result is carried
                // in the float the operands already are, and recomputed in int64 only when it leaves
                // the int32 band, for the refusal's message (`checkedFloatInt`).
                match op with
                | Add ->
                    if bothInt then
                        checkedFloatInt Add "add" (x + y) x y
                    else
                        Ok(Float(x + y))
                | Sub ->
                    if bothInt then
                        checkedFloatInt Sub "sub" (x - y) x y
                    else
                        Ok(Float(x - y))
                | Mul ->
                    if bothInt then
                        checkedFloatInt Mul "mul" (x * y) x y
                    else
                        Ok(Float(x * y))
                | Div -> Ok(if y = 0.0 then Null else Float(x / y))
                | Mod ->
                    if not bothInt then
                        Error(TypeError "mod requires integer operands")
                    elif y = 0.0 then
                        Ok Null
                    else
                        // The remainder of two integers is exact in a float, within int32, and `0` (a
                        // `-0.0` the int conversion reads as `0`) for `Int32.MinValue % -1`.
                        checkedFloatInt Mod "mod" (x % y) x y
                | _ -> Error(TypeError "not an arithmetic operator")
#else
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
#endif
            | _ -> Error(TypeError "arithmetic on a non-numeric operand")

    let private comparison (op: BinOp) (a: Cell) (b: Cell) : Result<Cell, EvalError> =
        match a, b with
        | Null, _
        | _, Null -> Ok Null
        | _ ->
            match compareCells a b with
            | None ->
                match a, b with
                | Decimal _, Float _
                | Float _, Decimal _ -> Error(decimalFloatMismatch "comparison")
                | _ -> Error(TypeError "comparison between incompatible types")
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
                    // Phase 277: the nearest float to the decimal — the cast says the pipeline asked
                    // for the approximation. A decimal past the float range is refused, never `∞`.
                    | Decimal s ->
                        match DecimalText.tryToFloat s with
                        | Some f -> Ok(Float f)
                        | None -> Error(TypeError("cannot cast decimal '" + s + "' to float: outside the float range"))
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
                // Phase 277: truncated toward zero, as a float is; outside int32 a named overflow.
                | Decimal s ->
                    match DecimalArith.truncateToInt64 s with
                    | Some v when inInt32Range v -> Ok(Int(int v))
                    | Some _
                    | None -> Error(OverflowError("decimal→int cast out of int32 range: " + s))
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
            // Phase 277. An int is exactly a decimal; a string is read by the decimal grammar
            // (`DecimalText`); a FLOAT is the one place an approximation enters a decimal — its
            // shortest round-trip digits, which are the float's and not whatever it was meant to be.
            | DecimalType ->
                match c with
                | Decimal s ->
                    match DecimalText.tryCanonical s with
                    | Some canonical -> Ok(Decimal canonical)
                    | None -> Error(TypeError("cannot cast '" + s + "' to decimal"))
                | Int i -> Ok(Decimal(string i))
                | Float f ->
                    match DecimalArith.ofFloat f with
                    | Some text -> Ok(Decimal text)
                    | None -> Error(TypeError "cannot cast a non-finite float to decimal")
                | Str s ->
                    match DecimalText.tryCanonical s with
                    | Some canonical -> Ok(Decimal canonical)
                    | None -> Error(TypeError("cannot cast '" + s + "' to decimal"))
                | _ -> Error(TypeError "cannot cast to decimal")

    /// Round half away from zero — host-independent (not `Math.Round`'s banker's rounding, which
    /// diverges between .NET and JS). Pinned so the three evaluators agree.
    let private roundHalfAway (x: float) : float =
        if x >= 0.0 then floor (x + 0.5) else ceil (x - 0.5)

    /// THE rounding primitive over cells (Phase 277): `Rounded`'s, and through it the decimal arm of
    /// `Round` / `Floor` / `Ceil`, which are its scale-0 specialisations — one implementation. The
    /// scale is checked first (a scale outside `0 .. 1000` is refused by name), then a null
    /// propagates, then an exact operand is brought to the scale; a `Float` names the `Cast`.
    let private roundedCell (mode: RoundingMode) (scale: int) (c: Cell) : Result<Cell, EvalError> =
        if scale < 0 || scale > DecimalArith.maxScale then
            Error(
                TypeError(
                    "rounding scale "
                    + string scale
                    + " is outside 0.."
                    + string DecimalArith.maxScale
                )
            )
        else
            match c with
            | Null -> Ok Null
            | Decimal _
            | Int _ -> decimalResult "rounded" (decimalText c |> Option.bind (DecimalArith.quantize mode scale))
            | Float _ -> Error(decimalFloatMismatch "rounding")
            | _ -> Error(TypeError "rounding of a non-numeric")

    /// `Quotient`'s primitive (Phase 277): the scale first, then a null operand propagates, then two
    /// exact operands divide, correctly rounded; a zero divisor answers `Null`, as `Div`'s does.
    let private quotientCell (mode: RoundingMode) (scale: int) (a: Cell) (b: Cell) : Result<Cell, EvalError> =
        if scale < 0 || scale > DecimalArith.maxScale then
            Error(
                TypeError(
                    "rounding scale "
                    + string scale
                    + " is outside 0.."
                    + string DecimalArith.maxScale
                )
            )
        else
            match a, b with
            | Null, _
            | _, Null -> Ok Null
            | (Decimal _ | Int _), (Decimal _ | Int _) ->
                match decimalText a, decimalText b with
                | Some x, Some y ->
                    match DecimalArith.divide mode scale x y with
                    | Some(Some q) -> Ok(Decimal q)
                    | Some None -> Ok Null
                    | None -> Error(TypeError "quotient: an operand is not decimal text")
                | _ -> Error(TypeError "quotient: an operand is not decimal text")
            | Float _, _
            | _, Float _ -> Error(decimalFloatMismatch "quotient")
            | _ -> Error(TypeError "quotient of a non-numeric")

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
                | Decimal s -> decimalResult "abs" (DecimalArith.abs s)
                | _ -> Error(TypeError "abs of a non-numeric"))
        // Over a decimal `Round`, `Floor` and `Ceil` are scale-0 `Rounded`s under their pinned modes
        // (Phase 277) — exact, through the one rounding kernel; over the numeric family, unchanged.
        | Round ->
            unary (fun c ->
                match c with
                | Decimal _ -> roundedCell RoundingMode.HalfUp 0 c
                | _ ->
                    match asNum c with
                    | Some f -> Ok(Float(roundHalfAway f))
                    | None -> Error(TypeError "round of a non-numeric"))
        | Floor ->
            unary (fun c ->
                match c with
                | Decimal _ -> roundedCell RoundingMode.Floor 0 c
                | _ ->
                    match asNum c with
                    | Some f -> Ok(Float(floor f))
                    | None -> Error(TypeError "floor of a non-numeric"))
        | Ceil ->
            unary (fun c ->
                match c with
                | Decimal _ -> roundedCell RoundingMode.Ceiling 0 c
                | _ ->
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
                match c with
                | Decimal _ -> Error(TypeError "sqrt of a decimal is not exact: write Sqrt(Cast(float, x))")
                | _ ->
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

    /// A `ColExpr` with every name it reads already resolved against one step's schema and env
    /// (Phase 263) — the per-step, index-resolved twin the evaluator walks once per row.
    ///
    /// The unresolved form looked each `Col` up by NAME in the schema and then walked the row list
    /// to that index, on every row, for every reference. Resolution is a pure function of the
    /// schema and the env, and both are fixed for the whole step, so it is done once before the row
    /// loop. It changes WHEN a name is looked up and nothing about what an evaluation answers:
    ///
    /// - a name that does not resolve becomes `RFail` carrying the very error the per-row lookup
    ///   raised, and that error is still raised only when an evaluation REACHES the node — a
    ///   missing column in an untaken `Case` branch, behind a `Coalesce` that stopped early, or in a
    ///   step over no rows is still no error at all;
    /// - a bound `Param` becomes its cell, an unbound one, an `InParam` and an unpinned `Now` become
    ///   `RFail`s with the errors they gave, and every other node keeps its shape and its
    ///   evaluation order, so the first error an evaluation meets is the one it met before.
    ///
    /// Internal, never public: this is the evaluator's working form, not a contract. The incremental
    /// seam in this assembly resolves through `resolveExpr` and evaluates through `evalResolved`; the
    /// public `evalExprInRow` is the two composed over a list row.
    type internal ResolvedExpr =
        | RCol of int
        | RConst of Cell
        | RFail of EvalError
        | RBinary of BinOp * ResolvedExpr * ResolvedExpr
        | RNot of ResolvedExpr
        | RCoalesce of ResolvedExpr list
        | RCase of (ResolvedExpr * ResolvedExpr) list * ResolvedExpr
        | RCast of ColumnType * ResolvedExpr
        | RInList of ResolvedExpr * ResolvedExpr list
        | RIsNull of ResolvedExpr
        | RApplyFn of ScalarFn * ResolvedExpr list
        /// Phase 277. The scale is resolved with the node (a param read once per step); a failure is
        /// held and raised only when an evaluation reaches the node, before its operands.
        | RQuotient of scale: Result<int, EvalError> * RoundingMode * ResolvedExpr * ResolvedExpr
        | RRounded of scale: Result<int, EvalError> * RoundingMode * ResolvedExpr

    /// A rounding's scale against the evaluation env (Phase 277): a literal is itself; a param reads
    /// the env — unbound is `UnboundParam` naming the bound set, as a `Param` gives, and a cell that
    /// is not an int is a `TypeError` naming the rounding scale. The range is the primitive's check.
    let internal resolveScale (env: Map<string, Cell>) (s: Slot<int>) : Result<int, EvalError> =
        match s with
        | Slot.Lit n -> Ok n
        | Slot.Param n ->
            match Map.tryFind n env with
            | Some(Int v) -> Ok v
            | Some _ -> Error(TypeError("rounding scale: param '" + n + "' is not bound to an int"))
            | None -> Error(UnboundParam(n, env |> Map.toList |> List.map fst))

    /// Resolve a `ColExpr` against a step's schema `cols` and evaluation environment `env` (Phase
    /// 77's params) — once per step, before its row loop. See `ResolvedExpr` for what is and is not
    /// moved by doing it here.
    let rec internal resolveExpr (env: Map<string, Cell>) (cols: Schema) (e: ColExpr) : ResolvedExpr =
        let go = resolveExpr env cols

        match e with
        | Col name ->
            match colIndex cols name with
            | Some i -> RCol i
            | None -> RFail(UnknownColumn(name, available cols))
        | Lit c -> RConst c
        | Param name ->
            match Map.tryFind name env with
            | Some c -> RConst c
            | None -> RFail(UnboundParam(name, env |> Map.toList |> List.map fst))
        | Binary(op, a, b) -> RBinary(op, go a, go b)
        | Not inner -> RNot(go inner)
        | Coalesce exprs -> RCoalesce(List.map go exprs)
        | Case(cases, elseExpr) -> RCase(cases |> List.map (fun (w, t) -> go w, go t), go elseExpr)
        | Cast(ty, inner) -> RCast(ty, go inner)
        | InList(subject, items) -> RInList(go subject, List.map go items)
        | IsNull inner -> RIsNull(go inner)
        // List params resolve by substitution (`substituteListParams`) BEFORE evaluation — one that
        // reaches the evaluator is unbound, same strictness as a scalar `Param`. The subject is
        // never evaluated, exactly as before.
        | InParam(_, name) -> RFail(UnboundParam(name, env |> Map.toList |> List.map fst))
        // A `now` resolves by substitution against a pinned clock (`substituteNow`) BEFORE
        // evaluation, exactly as a list param does. One that reaches here has no clock, and Core has
        // none to fall back on — reading the host's real clock here would make the answer depend on
        // when the pipeline ran, which is the property `Now` exists to keep.
        | Now grain -> RFail(UnpinnedClock grain)
        | ApplyFn(fn, args) -> RApplyFn(fn, List.map go args)
        | Quotient(a, b, r) -> RQuotient(resolveScale env r.Scale, r.Mode, go a, go b)
        | Rounded(a, r) -> RRounded(resolveScale env r.Scale, r.Mode, go a)

    let private binaryOp (op: BinOp) (av: Cell) (bv: Cell) : Result<Cell, EvalError> =
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
        | EndsWith -> stringPred op av bv

    /// `Not` over one cell: three-valued, and a typed error off the boolean family.
    let private notCell (c: Cell) : Result<Cell, EvalError> =
        match c with
        | Bool b -> Ok(Bool(not b))
        | Null -> Ok Null
        | _ -> Error(TypeError "not of a non-bool")

    /// One step of `InList`'s membership test: does the present item match the present subject
    /// under the pinned cell ordering? Incomparable families are the typed error; the null
    /// bookkeeping around the steps is the caller's loop.
    let private inMatch (subject: Cell) (item: Cell) : Result<bool, EvalError> =
        match compareCells subject item with
        | Some 0 -> Ok true
        | Some _ -> Ok false
        | None -> Error(TypeError "in: comparison between incompatible types")

    /// Evaluate a resolved expression against one row held as an array — the reference expression
    /// evaluator. Every `Param` was read from the environment at resolution; a hit is its bound
    /// `Cell`, and a miss is the strict `UnboundParam` naming the param and the bound set (GP4/GP5)
    /// — never a throw, never a silent default.
    let rec internal evalResolved (row: Cell[]) (e: ResolvedExpr) : Result<Cell, EvalError> =
        match e with
        | RCol i -> Ok(row[i])
        | RConst c -> Ok c
        | RFail err -> Error err
        | RBinary(op, a, b) ->
            evalResolved row a
            |> Result.bind (fun av -> evalResolved row b |> Result.bind (fun bv -> binaryOp op av bv))
        | RNot inner -> evalResolved row inner |> Result.bind notCell
        | RCoalesce exprs ->
            let rec go =
                function
                | [] -> Ok Null
                | x :: rest ->
                    evalResolved row x
                    |> Result.bind (function
                        | Null -> go rest
                        | c -> Ok c)

            go exprs
        | RCase(cases, elseExpr) ->
            let rec go =
                function
                | [] -> evalResolved row elseExpr
                | (whenE, thenE) :: rest ->
                    evalResolved row whenE
                    |> Result.bind (function
                        | Bool true -> evalResolved row thenE
                        | _ -> go rest)

            go cases
        | RCast(ty, inner) -> evalResolved row inner |> Result.bind (castCell ty)
        | RInList(subject, items) ->
            evalResolved row subject
            |> Result.bind (fun sv ->
                match sv with
                | Null -> Ok Null
                | _ ->
                    // SQL three-valued membership: any equal => true; no match seen a null => null.
                    let rec go sawNull =
                        function
                        | [] -> Ok(if sawNull then Null else Bool false)
                        | it :: rest ->
                            evalResolved row it
                            |> Result.bind (fun iv ->
                                match iv with
                                | Null -> go true rest
                                | _ ->
                                    match inMatch sv iv with
                                    | Ok true -> Ok(Bool true)
                                    | Ok false -> go sawNull rest
                                    | Error e -> Error e)

                    go false items)
        | RIsNull inner ->
            evalResolved row inner
            |> Result.map (fun v ->
                match v with
                | Null -> Bool true
                | _ -> Bool false)
        | RApplyFn(fn, args) ->
            let rec evalArgs acc =
                function
                | [] -> Ok(List.rev acc)
                | a :: rest -> evalResolved row a |> Result.bind (fun v -> evalArgs (v :: acc) rest)

            evalArgs [] args |> Result.bind (applyScalar fn)
        | RQuotient(scale, mode, a, b) ->
            scale
            |> Result.bind (fun n ->
                evalResolved row a
                |> Result.bind (fun av -> evalResolved row b |> Result.bind (fun bv -> quotientCell mode n av bv)))
        | RRounded(scale, mode, a) ->
            scale
            |> Result.bind (fun n -> evalResolved row a |> Result.bind (roundedCell mode n))

    // ---- static typing of expressions (Phase 266) ----
    //
    // What a node's PRESENT values can be, decided from a step's schema and nothing else. Three
    // states rather than two: a node that never produces a present value — a null literal, a name
    // that failed to resolve — is compatible with every type, and folding it into "unknown" would
    // cost the type of every `Case` whose else is `Lit Null`, the commonest idiom in the algebra.
    //
    // A typing is a claim about VALUES, never about errors: `Of IntType` says every present cell the
    // node produces is an `Int`, and says nothing about the rows on which it produces an error
    // instead. That is the claim a static reader needs, and it is the claim the compiler below
    // specialises on — with the tag still checked per row, so a column whose cells disagree with
    // its declared type is answered by the reference arm rather than by a wrong kernel.

    /// The static type of a node's present values.
    type internal Typing =
        /// No present value is ever produced: every row answers `Null` or an error.
        | Absent
        /// Every present value carries this type.
        | Of of ColumnType
        /// Undecidable from the schema: a `Param` the reader has no env for, a `Coalesce` of mixed
        /// operands, an operator over operands it would refuse.
        | Unknown

    /// The typing rules, one per node kind — shared by the two walks below (over `ColExpr` for a
    /// static reader, inside the compiler for the resolved form), so each rule has one source.
    module internal Typing =

        /// The least typing covering both: `Absent` is the identity, two equal types agree, and
        /// anything else is `Unknown`.
        ///
        /// Deliberately NOT the widening join the derived column's type takes (`inferType`,
        /// Phase 321, `DECISIONS.md` D3): `Of t` is an EXACTNESS claim the totality verdict reads.
        /// `Of IntType + Of IntType` is not total because two ints can overflow; a `Case` of an
        /// int and a float typed `Of FloatType` would let that same addition read as total while
        /// both operands are ints at run time. So `Int ⊔ Float` and `Int ⊔ Decimal` stay
        /// `Unknown` here, and `Float ⊔ Decimal` is `Unknown` as it always was.
        let join (a: Typing) (b: Typing) : Typing =
            match a, b with
            | Absent, t
            | t, Absent -> t
            | Of x, Of y -> if x = y then Of x else Unknown
            | Unknown, _
            | _, Unknown -> Unknown

        let joinAll (ts: Typing list) : Typing = List.fold join Absent ts

        /// A literal's typing: its cell's type, or `Absent` for the null literal.
        let ofCell (c: Cell) : Typing =
            match Cell.typeOf c with
            | Some ty -> Of ty
            | None -> Absent

        /// The `ColumnType option` view — a decided type, or `None` for both `Absent` and `Unknown`.
        let toOption (t: Typing) : ColumnType option =
            match t with
            | Of ty -> Some ty
            | Absent
            | Unknown -> None

        let private numeric (t: Typing) : bool =
            match t with
            | Of IntType
            | Of FloatType -> true
            | Absent
            | Of _
            | Unknown -> false

        let private boolLike (t: Typing) : bool =
            match t with
            | Absent
            | Of BoolType -> true
            | Of _
            | Unknown -> false

        /// The exact family (Phase 277): a decimal, or an int, which promotes to one losslessly.
        let private exact (t: Typing) : bool =
            match t with
            | Of IntType
            | Of DecimalType -> true
            | Absent
            | Of _
            | Unknown -> false

        /// Two exact operands at least one of which is a decimal — the pair decimal arithmetic takes.
        let private decimalPair (a: Typing) (b: Typing) : bool =
            exact a && exact b && (a = Of DecimalType || b = Of DecimalType)

        /// A binary node's typing. Null propagates through every operator but the logical pair
        /// BEFORE any type is examined, so an `Absent` operand makes the node `Absent` there; the
        /// logical pair is three-valued, so an `Absent` operand beside a boolean is still boolean.
        let binary (op: BinOp) (a: Typing) (b: Typing) : Typing =
            let nullPropagating (decide: unit -> Typing) : Typing =
                match a, b with
                | Absent, _
                | _, Absent -> Absent
                | _ -> decide ()

            match op with
            | Add
            | Sub
            | Mul ->
                nullPropagating (fun () ->
                    match a, b with
                    | Of IntType, Of IntType -> Of IntType
                    | (Of IntType | Of FloatType), (Of IntType | Of FloatType) -> Of FloatType
                    | _ -> if decimalPair a b then Of DecimalType else Unknown)
            // A decimal `Div` is refused on every row (it names `Quotient`), so no present value.
            | Div -> nullPropagating (fun () -> if numeric a && numeric b then Of FloatType else Unknown)
            | Mod ->
                nullPropagating (fun () ->
                    match a, b with
                    | Of IntType, Of IntType -> Of IntType
                    | _ -> if decimalPair a b then Of DecimalType else Unknown)
            | Eq
            | Ne
            | Lt
            | Le
            | Gt
            | Ge ->
                nullPropagating (fun () ->
                    match a, b with
                    | (Of IntType | Of FloatType), (Of IntType | Of FloatType)
                    | (Of IntType | Of DecimalType), (Of IntType | Of DecimalType)
                    | Of StringType, Of StringType
                    | Of DateType, Of DateType
                    | Of TimestampType, Of TimestampType
                    | Of BoolType, Of BoolType -> Of BoolType
                    | _ -> Unknown)
            | And
            | Or -> if boolLike a && boolLike b then join a b else Unknown
            | Contains
            | StartsWith
            | EndsWith ->
                nullPropagating (fun () ->
                    match a, b with
                    | Of StringType, Of StringType -> Of BoolType
                    | _ -> Unknown)

        let not' (a: Typing) : Typing =
            match a with
            | Absent -> Absent
            | Of BoolType -> Of BoolType
            | Of _
            | Unknown -> Unknown

        /// A cast's present values are always of the target type, whatever it was cast from: a
        /// value the cast cannot convert is an error, not a cell of another type.
        let cast (ty: ColumnType) (a: Typing) : Typing =
            match a with
            | Absent -> Absent
            | Of _
            | Unknown -> Of ty

        /// Membership is three-valued: a null subject answers null, everything else a boolean.
        let inList (subject: Typing) : Typing =
            match subject with
            | Absent -> Absent
            | Of _
            | Unknown -> Of BoolType

        /// The presence test is total: always a boolean, never null.
        let isNull: Typing = Of BoolType

        /// A `Case` produces one of its then-branches or its else.
        let case (thens: Typing list) (els: Typing) : Typing = joinAll (els :: thens)

        /// A scalar function's typing, total over `ScalarFn`. An arity the function refuses is an
        /// error on every row, so `Absent`; a null argument propagates exactly where the function
        /// propagates it (the subject of `Substr`, the date of `DatePart`, any argument of the
        /// variadic and two-string functions, the one argument of a unary function).
        let applyFn (fn: ScalarFn) (args: Typing list) : Typing =
            let arity (n: int) : bool = List.length args = n
            let anyAbsent = args |> List.exists (fun t -> t = Absent)

            let unary (decide: Typing -> Typing) : Typing =
                if not (arity 1) then
                    Absent
                else
                    match List.head args with
                    | Absent -> Absent
                    | t -> decide t

            match fn with
            | Abs ->
                unary (fun t ->
                    match t with
                    | Of IntType -> Of IntType
                    | Of FloatType -> Of FloatType
                    | Of DecimalType -> Of DecimalType
                    | Absent
                    | Of _
                    | Unknown -> Unknown)
            // Phase 277: over a decimal these three are scale-0 `Rounded`s and answer a decimal.
            | Round
            | Floor
            | Ceil ->
                unary (fun t ->
                    match t with
                    | Of DecimalType -> Of DecimalType
                    | _ -> Of FloatType)
            | Sqrt -> unary (fun _ -> Of FloatType)
            | Length -> unary (fun _ -> Of IntType)
            | Lower
            | Upper
            | Trim -> unary (fun _ -> Of StringType)
            | Substr ->
                if not (arity 3) then
                    Absent
                else
                    match List.head args with
                    | Absent -> Absent
                    | _ -> Of StringType
            | DatePart ->
                if not (arity 2) then
                    Absent
                else
                    match List.item 1 args with
                    | Absent -> Absent
                    | _ -> Of IntType
            | Concat ->
                if List.isEmpty args || anyAbsent then
                    Absent
                else
                    Of StringType
            | Replace -> if not (arity 3) || anyAbsent then Absent else Of StringType
            | DateDiffDays -> if not (arity 2) || anyAbsent then Absent else Of IntType
            | Least
            | Greatest ->
                if List.isEmpty args || anyAbsent then
                    Absent
                else
                    joinAll args
            | IndexOf -> if not (arity 2) || anyAbsent then Absent else Of IntType

        /// `Rounded` (Phase 277): a decimal over an exact operand, null where the operand is; any
        /// other operand is refused, so its present values are not decided.
        let rounded (a: Typing) : Typing =
            match a with
            | Absent -> Absent
            | Of IntType
            | Of DecimalType -> Of DecimalType
            | Of _
            | Unknown -> Unknown

        /// `Quotient` (Phase 277): a decimal over two exact operands, null where either is.
        let quotient (a: Typing) (b: Typing) : Typing =
            match a, b with
            | Absent, _
            | _, Absent -> Absent
            | _ -> if exact a && exact b then Of DecimalType else Unknown

    /// The static typing of an expression over a schema, with no env — the typer a static reader
    /// (`SchemaWalk`, a planner) asks. Total over the closed `ColExpr` union with no catch-all. A
    /// `Param` is `Unknown` because its cell is the env's; a `Now` is `Unknown` because its cell is
    /// the clock witness's; a column the schema does not carry is `Unknown` because the schema in
    /// hand may be open.
    let rec internal typing (cols: Schema) (e: ColExpr) : Typing =
        let go = typing cols

        match e with
        | Col name ->
            match colType cols name with
            | Some ty -> Of ty
            | None -> Unknown
        | Lit c -> Typing.ofCell c
        | Param _
        | Now _ -> Unknown
        | Binary(op, a, b) -> Typing.binary op (go a) (go b)
        | Not x -> Typing.not' (go x)
        | Coalesce xs -> Typing.joinAll (List.map go xs)
        | Case(cases, els) -> Typing.case (cases |> List.map (fun (_, t) -> go t)) (go els)
        | Cast(ty, x) -> Typing.cast ty (go x)
        | ApplyFn(fn, args) -> Typing.applyFn fn (List.map go args)
        | InList(subject, _) -> Typing.inList (go subject)
        | IsNull _ -> Typing.isNull
        // A bound list param evaluates as the `InList` it substitutes to; an unbound one is an
        // error on every row. Either way the present values are booleans.
        | InParam(subject, _) -> Typing.inList (go subject)
        | Quotient(a, b, _) -> Typing.quotient (go a) (go b)
        | Rounded(a, _) -> Typing.rounded (go a)

    /// The static type of an expression's present values over a schema, or `None` where the schema
    /// does not decide it — the typer a static reader (`SchemaWalk`, a planner) asks (Phase 266;
    /// public on the `0.34.0` draft).
    let typeOf (cols: Schema) (e: ColExpr) : ColumnType option = Typing.toOption (typing cols e)

    // ---- the type of a derived column (Phase 338, `DECISIONS.md` D5) ----
    //
    // ONE rule, read by the reference evaluator's `Derive` and `Unpivot`, the incremental seam's
    // walk and its chunked path, `SchemaWalk`, the planner and a registered query's check, so no two
    // of them can disagree about a column's type:
    //
    //  - where the typer decides the column (`Of ty`), `ty` IS its type on every frame — full,
    //    empty or all-null alike. Every present cell is of `ty` or widens into it by
    //    `ColumnType.widens` (an `Int` in a float or decimal column); the cell is stored as it was
    //    produced, never converted (D2's rule for an edit, applied here);
    //  - where the typer knows no present value can occur (`Absent`), the type is `StringType`,
    //    which is what an all-null column has always been typed;
    //  - where the typer cannot decide (`Unknown`: a `Param`, a `Now`, a column the schema does not
    //    carry, or a join of two types the exact typer keeps apart, D3), the CELLS decide, by
    //    Phase 321's widening join, `StringType` when none is present;
    //  - a `Float` beside a `Decimal` in one column is REFUSED by name, never widened: statically
    //    where the expression's own arms carry both (`mixesFloatDecimal`), and as a whole-column
    //    refusal where only the cells show it.

    /// How a derived column is typed (Phase 338).
    type internal DerivedTyping =
        /// The schema decides the type: the same on every frame.
        | Decided of ColumnType
        /// The cells decide it: Phase 321's widening join of the present cells' types.
        | ByCells
        /// A float beside a decimal, visible in the expression's own arms: refused on every frame.
        | Refused

    /// Does an arm the derived column's value is drawn from — through `Case` and `Coalesce`, the two
    /// nodes whose answer IS one of their operands' — carry the decided type `ty`?
    let rec private armsHave (ty: ColumnType) (cols: Schema) (e: ColExpr) : bool =
        match e with
        | Coalesce xs -> xs |> List.exists (armsHave ty cols)
        | Case(cases, els) -> armsHave ty cols els || cases |> List.exists (fun (_, t) -> armsHave ty cols t)
        | Col _
        | Lit _
        | Param _
        | Now _
        | Binary _
        | Not _
        | Cast _
        | ApplyFn _
        | InList _
        | IsNull _
        | InParam _
        | Quotient _
        | Rounded _ -> typing cols e = Of ty

    /// A float arm beside a decimal arm in one derived column (Phase 338, carried from 321): the
    /// static half of the refusal. Every such expression is `Unknown` to the typer, because the
    /// exact join of `Of FloatType` and `Of DecimalType` is `Unknown` and `Unknown` absorbs.
    let internal mixesFloatDecimal (cols: Schema) (e: ColExpr) : bool =
        armsHave FloatType cols e && armsHave DecimalType cols e

    /// The one rule over a typing and the types its arms are known to carry.
    let private derivedTypingOf (mixes: bool) (t: Typing) : DerivedTyping =
        if mixes then
            Refused
        else
            match t with
            | Of ty -> Decided ty
            | Absent -> Decided StringType
            | Unknown -> ByCells

    /// How a `Derive` of `e` over `cols` types its column (Phase 338).
    let internal derivedTyping (cols: Schema) (e: ColExpr) : DerivedTyping =
        derivedTypingOf (mixesFloatDecimal cols e) (typing cols e)

    /// How an `Unpivot`'s value column is typed over the value columns' declared types (Phase 338):
    /// the same rule, the value columns being its arms. Their join is Phase 321's widening join of
    /// the TYPES the schema declares — the join the cells would reach, read where the schema states
    /// it — because no totality verdict reads an unpivot's type as an exactness claim (D3 is about
    /// the expression typer). So an int and a float value column melt into a float column on every
    /// frame; no value column at all is `StringType`; a float beside a decimal is refused; and a
    /// pair no widening relates (a string beside an int) is the one case the cells decide.
    let internal unpivotTyping (types: ColumnType list) : DerivedTyping =
        if List.contains FloatType types && List.contains DecimalType types then
            Refused
        else
            let join (acc: ColumnType option option) (t: ColumnType) =
                match acc with
                | None -> Some(Some t)
                | Some(Some a) -> Some(joinColumnType a t)
                | Some None -> Some None

            match List.fold join None types with
            | None -> Decided StringType
            | Some(Some ty) -> Decided ty
            | Some None -> ByCells

    /// The column type a `Derive` of `e` produces wherever the schema decides it (Phase 338): the
    /// typer's own `typeOf` for every decided expression, `StringType` for one with no present
    /// value, and `None` exactly where the cells decide or the derive is refused.
    let internal derivedColumnType (cols: Schema) (e: ColExpr) : ColumnType option =
        match derivedTyping cols e with
        | Decided ty -> Some ty
        | ByCells
        | Refused -> None

    /// The refusal a column holding a float beside a decimal meets (Phase 338), naming the column
    /// and the `Cast` that resolves it. The text is the model's (`proofs/Pipeline.fst`), byte for byte.
    let internal floatBesideDecimal (column: string) : EvalError =
        TypeError(
            "derived column '"
            + column
            + "' joins a float and a decimal: cast one to the other's type first - Cast(decimal, ...) "
            + "to keep the digits exact, Cast(float, ...) to compute approximately"
        )

    /// The cells' half of the rule (Phase 338): Phase 321's widening join over the present cells
    /// an index range reads, `StringType` when none is present, and the named refusal where the
    /// cells hold a float beside a decimal.
    let internal typeFromCells (column: string) (count: int) (cellAt: int -> Cell) : Result<ColumnType, EvalError> =
        let mutable acc: ColumnType option = None
        let mutable sawFloat = false
        let mutable sawDecimal = false

        for i in 0 .. count - 1 do
            match Cell.typeOf (cellAt i) with
            | None -> ()
            | Some t ->
                match t with
                | FloatType -> sawFloat <- true
                | DecimalType -> sawDecimal <- true
                | IntType
                | BoolType
                | StringType
                | DateType
                | TimestampType -> ()

                acc <-
                    match acc with
                    | None -> Some t
                    | Some a -> Some(widenColumnType a t)

        if sawFloat && sawDecimal then
            Error(floatBesideDecimal column)
        else
            Ok(acc |> Option.defaultValue StringType)

    /// A derived column's type under its typing (Phase 338): the decided type, or the cells' answer.
    /// A `Refused` typing is answered before any row is evaluated, so it reaches here only from a
    /// caller that skipped that check, and is refused the same way.
    let internal columnTypeBy
        (dt: DerivedTyping)
        (column: string)
        (count: int)
        (cellAt: int -> Cell)
        : Result<ColumnType, EvalError> =
        match dt with
        | Decided ty -> Ok ty
        | ByCells -> typeFromCells column count cellAt
        | Refused -> Error(floatBesideDecimal column)

    // ---- the planner (Phase 269) ----
    //
    // Pipelines are data, so they can be rewritten before execution. The reference semantics are
    // strict and first-error — a step evaluates its expression over every row alive at it before
    // the next step runs, and the pipeline reports the first step's first `EvalError` — so a
    // rewrite is admissible only where it changes NEITHER the rows and cells each step sees NOR
    // the first error the walk meets. The engine here is what `Plan.rewrite` runs and what every
    // evaluator entry point runs before it folds (`evalPreparedCounted`); `Plan.fs` is its public
    // face and the doc of record for the three classes. It sits inside this module, before the
    // driver, because F# resolves forwards and the driver needs it — `SchemaWalk` is declared
    // after the driver, so the planner tracks the schema itself over the six row-set verbs it
    // rewrites across, and stops knowing at any other.
    //
    // The verdict `exprTotal` is the load-bearing judgement: it says an expression can raise NO
    // `EvalError` over ANY table of the schema, reading the typer's decision for each node and the
    // evaluator's own arms for what each can raise. It is conservative by construction — a `Param`,
    // a `Now`, a column the schema does not type, an integer `Add`/`Sub`/`Mul` (overflow), a `Mod`
    // (a non-integer operand), a `Cast` that parses, a comparison the typer cannot match, a scalar
    // function over an argument it would refuse — each is "may error", and the rewrite that needed
    // the verdict is declined. `proofs/Pipeline.fst` proves the verdict sound over the modelled
    // evaluator under the one assumption it names (the cell primitives answer where the verdict
    // admits them), and `Conformance.plannerLaws` holds the planned evaluation to the reference,
    // errors included, over generated triples.
    module internal Planner =

        /// Every column an expression reads, in occurrence order, with duplicates.
        let rec exprCols (e: ColExpr) : string list =
            match e with
            | Col n -> [ n ]
            | Lit _
            | Param _
            | Now _ -> []
            | Binary(_, a, b) -> exprCols a @ exprCols b
            | Not x
            | Cast(_, x)
            | IsNull x -> exprCols x
            | Coalesce xs
            | ApplyFn(_, xs) -> xs |> List.collect exprCols
            | Case(cases, els) -> (cases |> List.collect (fun (w, t) -> exprCols w @ exprCols t)) @ exprCols els
            | InList(x, items) -> exprCols x @ (items |> List.collect exprCols)
            | InParam(x, _) -> exprCols x
            | Quotient(a, b, _) -> exprCols a @ exprCols b
            | Rounded(x, _) -> exprCols x

        /// A typing whose present values the pinned cell ordering compares with `other`'s: an
        /// absent value propagates before any comparison, numbers compare across `Int`/`Float`,
        /// and every other type compares only with itself. `compareCells` answers `None` — a
        /// `TypeError` in every arm that reads it — exactly where this says `false`.
        let private comparable (a: Typing) (b: Typing) : bool =
            match a, b with
            | Absent, _
            | _, Absent -> true
            | (Of IntType | Of FloatType), (Of IntType | Of FloatType) -> true
            // Phase 277: a decimal compares exactly with a decimal or an int, and never with a float.
            | (Of IntType | Of DecimalType), (Of IntType | Of DecimalType) -> true
            | Of StringType, Of StringType
            | Of DateType, Of DateType
            | Of TimestampType, Of TimestampType
            | Of BoolType, Of BoolType -> true
            | _ -> false

        let private numeric (t: Typing) : bool =
            match t with
            | Of IntType
            | Of FloatType -> true
            | _ -> false

        /// Two exact operands at least one of which is a decimal (Phase 277): `decimalArith`'s pair,
        /// over which `Add`, `Sub`, `Mul` and `Mod` are exact and never refuse.
        let private decimalPair (a: Typing) (b: Typing) : bool =
            match a, b with
            | Of DecimalType, (Of IntType | Of DecimalType)
            | Of IntType, Of DecimalType -> true
            | _ -> false

        let private isStr (t: Typing) : bool =
            match t with
            | Absent
            | Of StringType -> true
            | _ -> false

        let private boolLike (t: Typing) : bool =
            match t with
            | Absent
            | Of BoolType -> true
            | _ -> false

        /// A syntactically NON-NULL expression: a present literal, or the presence test, which is
        /// total and always boolean. The typer types a column's present values and says nothing
        /// about its nulls, so an argument the evaluator refuses when null (`Substr`'s start and
        /// length) is admitted only in this shape.
        /// An operand a rounding admits (Phase 277): null, or exact.
        let private exactOrAbsent (t: Typing) : bool =
            match t with
            | Absent
            | Of IntType
            | Of DecimalType -> true
            | _ -> false

        /// A rounding whose scale the verdict can read and the primitive accepts: a literal in
        /// `0 .. 1000`. A param scale is the env's; a literal outside the range is refused.
        let private scaleAdmitted (r: Rounding) : bool =
            match r.Scale with
            | Slot.Lit n -> n >= 0 && n <= DecimalArith.maxScale
            | Slot.Param _ -> false

        let private neverNull (e: ColExpr) : bool =
            match e with
            | Lit c -> not (Cell.isNull c)
            | IsNull _ -> true
            | _ -> false

        /// THE TOTALITY VERDICT over an expression: `true` only where evaluating `e` against ANY row
        /// of a table of schema `cols` (any cells, nulls included, that fit the schema) returns
        /// `Ok`. Every `false` names an arm of the evaluator that can answer `Error` there; every
        /// `true` is backed by the arm's own code — `arith` on a null answers `Null` before it
        /// reads a type, `Float` arithmetic never overflows into an error, `Div` by zero is
        /// `Null`, `comparison` over a matching pair never fails, `castCell` to `String` accepts
        /// every cell, and so on. The soundness of this verdict over the modelled evaluator is the
        /// theorem `verdict_sound` in `proofs/Pipeline.fst`; the assumption it rests on is stated
        /// there (`prims_admit`), and it is exactly the per-arm reading this function encodes.
        let rec exprTotal (cols: Schema) (e: ColExpr) : bool =
            let ty = typing cols

            match e with
            | Col name -> Option.isSome (colType cols name)
            | Lit _ -> true
            // An env binds a param and a witness pins a clock; over the schema alone each is an
            // error, and the verdict answers over the schema alone.
            | Param _
            | Now _
            | InParam _ -> false
            | Binary(op, a, b) ->
                exprTotal cols a
                && exprTotal cols b
                && (let ta = ty a
                    let tb = ty b

                    match op with
                    | Add
                    | Sub
                    | Mul ->
                        (match ta, tb with
                         | Absent, _
                         | _, Absent -> true
                         // Two integers: `checkedInt` can name an overflow.
                         | Of IntType, Of IntType -> false
                         | _ -> (numeric ta && numeric tb) || decimalPair ta tb)
                    | Div ->
                        (match ta, tb with
                         | Absent, _
                         | _, Absent -> true
                         | _ -> numeric ta && numeric tb)
                    // `Mod` of two integers can overflow only at `MinValue % -1`, which the int64
                    // remainder absorbs, but the typer cannot see a null start from a column and
                    // the arm refuses a non-integer operand: declined whole, per the phase's rule.
                    // A decimal `Mod` is exact and a zero divisor is `Null` (Phase 277): total.
                    // A decimal `Div` refuses on every row (it names `Quotient`): never admitted.
                    | Mod ->
                        (match ta, tb with
                         | Absent, _
                         | _, Absent -> true
                         | _ -> decimalPair ta tb)
                    | Eq
                    | Ne
                    | Lt
                    | Le
                    | Gt
                    | Ge -> comparable ta tb
                    | And
                    | Or -> boolLike ta && boolLike tb
                    | Contains
                    | StartsWith
                    | EndsWith ->
                        (match ta, tb with
                         | Absent, _
                         | _, Absent -> true
                         | _ -> isStr ta && isStr tb))
            | Not x -> exprTotal cols x && boolLike (ty x)
            | IsNull x -> exprTotal cols x
            | Coalesce xs -> xs |> List.forall (exprTotal cols)
            // A `when` that is not `Bool true` falls through, whatever it is; only an error stops.
            | Case(cases, els) ->
                cases |> List.forall (fun (w, t) -> exprTotal cols w && exprTotal cols t)
                && exprTotal cols els
            | Cast(target, x) ->
                exprTotal cols x
                && (match target, ty x with
                    | _, Absent -> true
                    | StringType, _ -> true
                    | FloatType, (Of IntType | Of FloatType) -> true
                    | IntType, (Of IntType | Of BoolType) -> true
                    | BoolType, (Of BoolType | Of IntType) -> true
                    | DateType, (Of DateType | Of StringType) -> true
                    | TimestampType, (Of TimestampType | Of StringType) -> true
                    // An int is exactly a decimal; a decimal is itself. From a float (non-finite) or
                    // a string (parsed) the cast can refuse.
                    | DecimalType, (Of IntType | Of DecimalType) -> true
                    | _ -> false)
            | InList(subject, items) ->
                exprTotal cols subject
                && items |> List.forall (exprTotal cols)
                && (let ts = ty subject
                    items |> List.forall (fun it -> comparable ts (ty it)))
            | ApplyFn(fn, args) ->
                args |> List.forall (exprTotal cols)
                && (let ts = args |> List.map ty
                    let arity n = List.length args = n

                    match fn with
                    // `abs Int32.MinValue` throws; a float's and a decimal's absolute values are total.
                    | Abs ->
                        arity 1
                        && (match List.head ts with
                            | Absent
                            | Of FloatType
                            | Of DecimalType -> true
                            | _ -> false)
                    // Over a decimal the first three are exact scale-0 roundings (Phase 277): total.
                    | Round
                    | Floor
                    | Ceil ->
                        arity 1
                        && (match List.head ts with
                            | Absent
                            | Of DecimalType -> true
                            | t -> numeric t)
                    | Sqrt ->
                        arity 1
                        && (match List.head ts with
                            | Absent -> true
                            | t -> numeric t)
                    | Length
                    | Lower
                    | Upper
                    | Trim -> arity 1 && isStr (List.head ts)
                    | Substr ->
                        arity 3
                        && isStr (List.head ts)
                        && (let a1 = List.item 1 args
                            let a2 = List.item 2 args
                            neverNull a1 && neverNull a2 && ty a1 = Of IntType && ty a2 = Of IntType)
                    // Both parse the date they are handed.
                    | DatePart
                    | DateDiffDays -> false
                    // Variadic over any cells: a null propagates, everything else stringifies.
                    | Concat -> not (List.isEmpty args)
                    | Replace -> arity 3 && ts |> List.forall isStr
                    | Least
                    | Greatest ->
                        not (List.isEmpty args)
                        && (let present = ts |> List.filter (fun t -> t <> Absent)

                            match present with
                            | [] -> true
                            | first :: _ -> present |> List.forall (comparable first))
                    | IndexOf -> arity 2 && ts |> List.forall isStr)
            // Phase 277: exact operands (or null) and a literal scale in range — the primitive then
            // answers a decimal, or `Null` for a zero divisor; a float operand or a param scale may
            // refuse.
            | Quotient(a, b, r) ->
                exprTotal cols a
                && exprTotal cols b
                && scaleAdmitted r
                && exactOrAbsent (ty a)
                && exactOrAbsent (ty b)
            | Rounded(a, r) -> exprTotal cols a && scaleAdmitted r && exactOrAbsent (ty a)

        /// THE TOTALITY VERDICT over a step: `true` only where evaluating the step over ANY table
        /// of schema `cols` returns `Ok`. `Sort` drops a key the schema does not carry rather than
        /// refusing it, so a literal-keyed sort is total; a `Limit` clamps; `Distinct` compares
        /// tokens; a `Project` fails only on a source the schema lacks. A slot still holding a
        /// param is resolved against an env the verdict does not have. Every other verb reaches
        /// a resolver, an aggregate or a comparison the schema alone cannot vouch for.
        let isTotal (cols: Schema) (t: Transform) : bool =
            match t with
            | Filter p -> exprTotal cols p
            // Phase 338: a derive whose column the CELLS type can refuse a float beside a decimal
            // over the whole column, and one refused statically always does, so only a decided
            // derive is total.
            | Derive(_, e) ->
                exprTotal cols e
                && (match derivedTyping cols e with
                    | Decided _ -> true
                    | ByCells
                    | Refused -> false)
            | Sort by -> by |> List.forall (fun (c, _) -> not (Slot.isParam c))
            | Limit(n, offset) -> not (Slot.isParam n) && not (Slot.isParam offset)
            | Project pairs -> pairs |> List.forall (fun (src, _) -> Option.isSome (colType cols src))
            | Distinct -> true
            | GroupBy _
            | Join _
            | Window _
            | Pivot _
            | Unpivot _
            | Union _
            | Intersect _
            | Except _ -> false

        /// The columns a step reads by name, for the pruning walk. A step that reads the whole
        /// row, or whose reads the schema alone cannot name, answers `None`.
        let private reads (t: Transform) : string list option =
            match t with
            | Filter p -> Some(exprCols p)
            | Derive(_, e) -> Some(exprCols e)
            | Project pairs -> Some(pairs |> List.map fst)
            | Sort by ->
                let names = by |> List.map (fun (c, _) -> Slot.tryLit c)

                if names |> List.forall Option.isSome then
                    Some(names |> List.choose id)
                else
                    None
            | Limit _
            | Distinct -> Some []
            | GroupBy(keys, aggs) -> Some(keys @ (aggs |> List.map (fun a -> a.Of)))
            | Pivot spec -> Some(spec.Index @ [ spec.On; spec.Values ])
            | Join _
            | Window _
            | Unpivot _
            | Union _
            | Intersect _
            | Except _ -> None

        /// The planner's own schema knowledge: every column by name, its type where the typer
        /// decides it — a `Derive`'s column carries its decided type (`derivedColumnType`, Phase
        /// 338), and is present with no type where only its cells decide it.
        type private Known = (string * ColumnType option) list

        let private typed (k: Known) : Schema =
            k |> List.choose (fun (n, t) -> t |> Option.map (fun t -> n, t))

        let private has (k: Known) (name: string) : bool =
            k |> List.exists (fun (n, _) -> n = name)

        /// The schema after a step, over the verbs the planner rewrites across; `None` where the
        /// planner stops knowing — a step outside the six, or one whose reads the schema lacks
        /// (it errors, naming the schema, and no rewrite may change what it names).
        let private after (k: Known) (t: Transform) : Known option =
            let resolved =
                match reads t with
                | Some rs -> rs |> List.forall (has k)
                | None -> false

            if not resolved then
                None
            else
                match t with
                | Filter _
                | Sort _
                | Limit _
                | Distinct -> Some k
                | Project pairs ->
                    Some(
                        pairs
                        |> List.map (fun (src, out) ->
                            out, (k |> List.tryFind (fun (n, _) -> n = src) |> Option.bind snd))
                    )
                | Derive(name, e) ->
                    let ty = derivedColumnType (typed k) e

                    if has k name then
                        Some(k |> List.map (fun (n, t) -> if n = name then n, ty else n, t))
                    else
                        Some(k @ [ name, ty ])
                | _ -> None

        /// The schema BEFORE each step of the pipeline, where the planner knows it.
        let private schemasBefore (cols: Schema) (pipeline: Transform list) : Known option[] =
            let start: Known = cols |> List.map (fun (n, t) -> n, Some t)

            let rec go (k: Known option) acc =
                function
                | [] -> List.rev acc
                | t :: rest ->
                    let next = k |> Option.bind (fun k -> after k t)
                    go next (k :: acc) rest

            go (Some start) [] pipeline |> List.toArray

        let private verbName (t: Transform) : string =
            match t with
            | Filter _ -> "filter"
            | Project _ -> "project"
            | Derive _ -> "derive"
            | GroupBy _ -> "groupBy"
            | Join _ -> "join"
            | Window _ -> "window"
            | Pivot _ -> "pivot"
            | Unpivot _ -> "unpivot"
            | Sort _ -> "sort"
            | Distinct -> "distinct"
            | Limit _ -> "limit"
            | Union _ -> "union"
            | Intersect _ -> "intersect"
            | Except _ -> "except"

        // ---- reordering: a Filter bubbles ahead of the Sort / Derive it follows ----

        /// Why a `Filter` may not move ahead of `prev`, over the schema before `prev`; `None`
        /// admits the move. The schema before the pair is the same after the swap, and so is the
        /// schema after it, which is what lets the schemas be computed once.
        let private declineReorder (k: Known option) (prev: Transform) (pred: ColExpr) : string option =
            match k with
            | None -> Some "the schema before the step is not known to the planner"
            | Some k ->
                let cols = typed k

                match prev with
                | Sort _ ->
                    // Ahead of a sort the filter evaluates the same rows in ANOTHER order, so its
                    // first error could be another row's: it must have none. The sort must have
                    // none either, or the filter would remove nothing from its path but its
                    // failure.
                    if not (isTotal cols prev) then
                        Some "the sort's key is a param the schema cannot resolve"
                    elif not (exprTotal cols pred) then
                        Some
                            "the filter's predicate may error, and ahead of the sort its first error could be another row's"
                    else
                        None
                | Derive(name, e) ->
                    // Ahead of a derive the filter drops rows the derive no longer evaluates: the
                    // derive must have no error to lose on them. It must not read the derived
                    // column, and every column it reads must exist without it, or its own
                    // refusal would name a different schema. And the derived column's TYPE must
                    // not read the cells the derive produced: a decided derive's is the same over
                    // any subset of the rows (Phase 338), and only one the cells type can differ.
                    if not (exprTotal cols e) then
                        Some "the derive's expression may error on a row the filter would drop"
                    elif exprCols pred |> List.contains name then
                        Some "the filter reads the derived column"
                    elif not (exprCols pred |> List.forall (has k)) then
                        Some "the filter reads a column the schema before the derive does not carry"
                    else
                        match derivedTyping cols e with
                        | Decided _ -> None
                        | ByCells ->
                            Some
                                "the derived column's type is decided by its cells, which a filter ahead of it would change"
                        | Refused -> Some "the derive joins a float and a decimal, which it refuses on every frame"
                | _ -> Some "the step ahead is not a sort or a derive"

        let private classOf (prev: Transform) : RewriteClass option =
            match prev with
            | Sort _ -> Some RewriteClass.FilterBeforeSort
            | Derive _ -> Some RewriteClass.FilterBeforeDerive
            | _ -> None

        /// A step in the reordered prefix: the step, the WRITTEN index whose schema-before is the
        /// schema before it now (a swap leaves both members over the schema the pair began on),
        /// and the written index it is reported by.
        type private Placed =
            { Step: Transform
              SchemaAt: int
              WrittenAt: int }

        /// One pass, left to right: each `Filter` moves ahead of every `Sort` / `Derive` it
        /// follows while the rule admits it, and the first refusal is reported against the
        /// written index of the filter.
        let private reorder
            (before: Known option[])
            (pipeline: Transform list)
            : Transform list * PlanRewrite list * PlanDeclined list =
            let rec bubble (out: Placed list) (step: Placed) applied declined =
                match out, step.Step with
                | prev :: rest, Filter pred ->
                    match classOf prev.Step with
                    | None -> step :: out, applied, declined
                    | Some cls ->
                        match declineReorder before[prev.SchemaAt] prev.Step pred with
                        | None ->
                            let r =
                                { Class = cls
                                  At = step.WrittenAt
                                  Detail =
                                    "filter moved ahead of the "
                                    + verbName prev.Step
                                    + " at "
                                    + string prev.WrittenAt }

                            let out', a, d =
                                bubble rest { step with SchemaAt = prev.SchemaAt } (r :: applied) declined

                            prev :: out', a, d
                        | Some reason ->
                            let d =
                                { Class = cls
                                  At = step.WrittenAt
                                  Reason = reason }

                            step :: out, applied, d :: declined
                | _ -> step :: out, applied, declined

            let placed =
                pipeline
                |> List.mapi (fun i t ->
                    { Step = t
                      SchemaAt = i
                      WrittenAt = i })

            let out, applied, declined =
                (([], [], []), placed)
                ||> List.fold (fun (out, applied, declined) step -> bubble out step applied declined)

            out |> List.rev |> List.map _.Step, List.rev applied, List.rev declined

        // ---- projection pruning ----

        /// A step after which the columns it did not read or emit are gone whatever stood before.
        let private drops (t: Transform) : bool =
            match t with
            | Project _
            | GroupBy _
            | Pivot _ -> true
            | _ -> false

        /// The columns needed BEFORE a step, given the ones needed after it — `None` for "every
        /// column". The walk is over one region: from a barrier (or the start) to a dropping step.
        let private needsBefore (t: Transform) (after: Set<string> option) : Set<string> option =
            match t with
            | Project pairs -> Some(pairs |> List.map fst |> Set.ofList)
            | GroupBy(keys, aggs) -> Some(Set.ofList (keys @ (aggs |> List.map (fun a -> a.Of))))
            | Pivot spec -> Some(Set.ofList (spec.Index @ [ spec.On; spec.Values ]))
            | Filter p -> after |> Option.map (Set.union (Set.ofList (exprCols p)))
            | Derive(name, e) ->
                after
                |> Option.map (fun a -> Set.union (Set.remove name a) (Set.ofList (exprCols e)))
            | Sort _ ->
                (match reads t with
                 | Some keys -> after |> Option.map (Set.union (Set.ofList keys))
                 | None -> None)
            | Limit _ -> after
            // `Distinct` dedups on the whole row: every column is live ahead of it.
            | Distinct -> None
            | _ -> None

        /// Prune one region: `steps` from a known start schema up to and including a dropping
        /// step, every read resolved. Inserts a `Project` wherever the live set shrinks below the
        /// schema the region has reached, keeping the live columns in schema order.
        let private pruneRegion (start: Known) (steps: (Transform * int) list) : Transform list * PlanRewrite list =
            let rec backward (acc: Set<string> option list) (after: Set<string> option) =
                function
                | [] -> acc
                | (t, _) :: rest ->
                    let b = needsBefore t after
                    backward (b :: acc) b rest

            let needsAt = backward [] None (List.rev steps) |> List.toArray

            let rec forward (i: int) (cur: Known) acc applied =
                function
                | [] -> List.rev acc, List.rev applied
                | (t, wi) :: rest ->
                    // A `Limit` behind a `Sort` is the fused pair: nothing is inserted between
                    // them, and what dies at the limit dies at the step after it. Nothing is
                    // inserted ahead of a dropping step either — it drops the column itself, so
                    // a `Project` there would save no step any work and would not be a fixpoint.
                    let skip =
                        drops t
                        || (match t, acc with
                            | Limit _, Sort _ :: _ -> true
                            | _ -> false)

                    let inserted =
                        match needsAt[i] with
                        | Some live when not skip && cur |> List.exists (fun (n, _) -> not (Set.contains n live)) ->
                            let kept = cur |> List.filter (fun (n, _) -> Set.contains n live)

                            let dropped =
                                cur |> List.filter (fun (n, _) -> not (Set.contains n live)) |> List.map fst

                            Some(
                                kept,
                                { Class = RewriteClass.PruneColumns
                                  At = wi
                                  Detail = "dropped " + String.concat ", " dropped + " ahead of the " + verbName t }
                            )
                        | _ -> None

                    let cur', acc', applied' =
                        match inserted with
                        | Some(kept, r) -> kept, Project(kept |> List.map (fun (n, _) -> n, n)) :: acc, r :: applied
                        | None -> cur, acc, applied

                    // A dropping step ends the region, and the schema after it is not needed.
                    let next = after cur' t |> Option.defaultValue cur'
                    forward (i + 1) next (t :: acc') applied' rest

            forward 0 start [] [] steps

        /// The pruning pass over the whole (reordered) pipeline: regions delimited by the steps the
        /// planner does not see across, each pruned only when it ends in a dropping step. The
        /// schema before each step is recomputed over the reordered pipeline.
        let private prune (cols: Schema) (pipeline: Transform list) : Transform list * PlanRewrite list =
            let before = schemasBefore cols pipeline
            let indexed = pipeline |> List.mapi (fun i t -> t, i)

            let resolved (t: Transform) (i: int) : bool =
                match before[i], reads t with
                | Some k, Some rs -> rs |> List.forall (has k)
                | _ -> false

            // Regions: a run of resolved steps ending at a dropping step is prunable; a run cut
            // short by an unresolved step, or by the end, is emitted as written.
            let rec regions (cur: (Transform * int) list) acc =
                function
                | [] ->
                    List.rev (
                        if List.isEmpty cur then
                            acc
                        else
                            (List.rev cur, false) :: acc
                    )
                | (t, i) :: rest ->
                    if not (resolved t i) then
                        let acc' =
                            if List.isEmpty cur then
                                acc
                            else
                                (List.rev cur, false) :: acc

                        regions [] (([ t, i ], false) :: acc') rest
                    elif drops t then
                        regions [] ((List.rev ((t, i) :: cur), true) :: acc) rest
                    else
                        regions ((t, i) :: cur) acc rest

            let pruned =
                regions [] [] indexed
                |> List.map (fun (steps, prunable) ->
                    match steps, prunable with
                    | (_, first) :: _, true ->
                        (match before[first] with
                         | Some start -> pruneRegion start steps
                         | None -> steps |> List.map fst, [])
                    | _ -> steps |> List.map fst, [])

            pruned |> List.collect fst, pruned |> List.collect snd
        // ---- fusion: the Sort > Limit pair the driver runs as a stable top-n ----

        let private topN (pipeline: Transform list) : PlanRewrite list =
            pipeline
            |> List.pairwise
            |> List.mapi (fun i pair -> i, pair)
            |> List.choose (fun (i, pair) ->
                match pair with
                | Sort _, Limit _ ->
                    Some
                        { Class = RewriteClass.TopN
                          At = i
                          Detail = "sort then limit run as one stable top-n" }
                | _ -> None)

        /// Is the pair at the head of a pipeline the fused kernel's shape?
        let isTopN (pipeline: Transform list) : bool =
            match pipeline with
            | Sort _ :: Limit _ :: _ -> true
            | _ -> false

        /// The planner: reorder, then prune, then name the fusions the driver will take. Total —
        /// a pipeline it cannot read is emitted as written.
        let explain (cols: Schema) (pipeline: Transform list) : PlanReport =
            let before = schemasBefore cols pipeline
            let reordered, moved, declined = reorder before pipeline
            let planned, pruned = prune cols reordered

            { Written = pipeline
              Planned = planned
              Applied = moved @ pruned @ topN planned
              Declined = declined }

        let rewrite (cols: Schema) (pipeline: Transform list) : Transform list = (explain cols pipeline).Planned

    // ---- compilation to a closure tree (Phase 266) ----
    //
    // `evalResolved` walks the expression tree per row and threads a `Result` through every node —
    // a closure allocation per node per row through `Result.bind`, and under Fable a result object
    // as well, with the row loop continuing through the continuation so a JavaScript host spent a
    // stack frame per row. The compiled form below walks the tree ONCE per step and returns a
    // closure tree over the array row: each node is a function of the row, specialised where the
    // typer above decides its operands' types and the reference arm where it does not.
    //
    // Errors travel through one slot per compiled tree instead of a `Result` per node. A node that
    // meets an error records it — the FIRST one wins, and every ancestor returns as soon as a child
    // has recorded one — so the error a row reports is the one `evalResolved` reports, and the
    // nodes visited are the nodes it visits. The slot is reset by the caller before each row, which
    // is why the tree is a per-step object and never shared across steps or threads.
    //
    // Semantics are the reference's by construction rather than by re-statement: every typed kernel
    // handles exactly the tag combinations the reference arm handles the same way, and hands every
    // other combination — a cell that disagrees with its column's declared type included — to that
    // arm. The law in the suite holds the compiled form to `evalExprInRow` cell for cell and error
    // for error over generated (schema, expression, row) triples.

    /// The kernel a compiled binary node runs, chosen once per step from its operands' typings.
    type internal Kernel =
        /// `Add`/`Sub`/`Mul`/`Mod` over two `Int` operands: int64 accumulation, int32 range check.
        | IntArith
        /// `Add`/`Sub`/`Mul`/`Div` over the numeric family with a `Float` result.
        | FloatArith
        /// The six comparisons over the numeric family, in the float carrier.
        | NumCompare
        /// The six comparisons over two strings, two dates or two timestamps, ordinal.
        | OrdinalCompare
        /// `Contains`/`StartsWith`/`EndsWith` over two strings, ordinal.
        | StrPredicate
        /// Kleene `And`/`Or` over booleans.
        | Logical
        /// The reference arm, for operands the typer could not decide.
        | Boxed

    /// The one slot a compiled tree reports through: the first error a row met, and — for the
    /// typed nodes (Phase 267) — whether the node just evaluated answered null. A typed node
    /// returns its carrier value and writes `Null` on EVERY call, so its parent reads the flag
    /// fresh after each child; a boxed node answers `Null` as the cell and leaves the flag alone.
    type internal ErrorSlot =
        { mutable Error: EvalError option
          mutable Null: bool }

    /// A compiled node (Phase 267): a function of the PHYSICAL row index over the frame's vectors,
    /// typed where the node's type is decided — its present values read and produced unboxed, the
    /// typed path — and boxed where it is not (the reference arm, reading cells through the
    /// vectors' accessor). `NNull` is a node that never produces a present value — the null
    /// literal, an empty coalesce — which every typed parent absorbs in its own carrier; `NStr`
    /// names the string family its values belong to.
    type internal Node =
        | NInt of (int -> int)
        | NFloat of (int -> float)
        | NBool of (int -> bool)
        | NStr of ColumnType * (int -> string)
        | NNull
        | NCell of (int -> Cell)

    /// A step's compiled expression: the node tree over the frame it was compiled against, the
    /// same tree boxed at the root (`Run`), the slot it reports through, and `Kernels` — the kernel
    /// of every binary node in compile order, so a sample can be checked for reaching each.
    type internal CompiledExpr =
        { Node: Node
          Run: int -> Cell
          Slot: ErrorSlot
          Kernels: Kernel list }

    let private kernelOf (op: BinOp) (a: Typing) (b: Typing) : Kernel =
        let intLike (t: Typing) =
            match t with
            | Absent
            | Of IntType -> true
            | Of _
            | Unknown -> false

        let numLike (t: Typing) =
            match t with
            | Absent
            | Of IntType
            | Of FloatType -> true
            | Of _
            | Unknown -> false

        let strLike (t: Typing) =
            match t with
            | Absent
            | Of StringType -> true
            | Of _
            | Unknown -> false

        let boolLike (t: Typing) =
            match t with
            | Absent
            | Of BoolType -> true
            | Of _
            | Unknown -> false

        let ordinalFamily =
            match a, b with
            | (Absent | Of StringType), (Absent | Of StringType)
            | (Absent | Of DateType), (Absent | Of DateType)
            | (Absent | Of TimestampType), (Absent | Of TimestampType) -> true
            | _ -> false

        match op with
        | Add
        | Sub
        | Mul ->
            if intLike a && intLike b then IntArith
            elif numLike a && numLike b then FloatArith
            else Boxed
        | Div -> if numLike a && numLike b then FloatArith else Boxed
        | Mod -> if intLike a && intLike b then IntArith else Boxed
        | Eq
        | Ne
        | Lt
        | Le
        | Gt
        | Ge ->
            if numLike a && numLike b then NumCompare
            elif ordinalFamily then OrdinalCompare
            else Boxed
        | And
        | Or -> if boolLike a && boolLike b then Logical else Boxed
        | Contains
        | StartsWith
        | EndsWith -> if strLike a && strLike b then StrPredicate else Boxed

    /// The comparison a comparison operator tests on the ordering's answer; `None` for an operator
    /// that is not a comparison, which `kernelOf` never pairs with a comparison kernel.
    let private comparisonTest (op: BinOp) : (int -> bool) option =
        match op with
        | Eq -> Some(fun c -> c = 0)
        | Ne -> Some(fun c -> c <> 0)
        | Lt -> Some(fun c -> c < 0)
        | Le -> Some(fun c -> c <= 0)
        | Gt -> Some(fun c -> c > 0)
        | Ge -> Some(fun c -> c >= 0)
        | _ -> None

    /// The ordinal predicate a string operator tests; `None` off the string-predicate family.
    let private stringTest (op: BinOp) : (string -> string -> bool) option =
        match op with
        | Contains -> Some(fun s t -> s.Contains(t, System.StringComparison.Ordinal))
        | StartsWith -> Some(fun s t -> s.StartsWith(t, System.StringComparison.Ordinal))
        | EndsWith -> Some(fun s t -> s.EndsWith(t, System.StringComparison.Ordinal))
        | _ -> None

    /// Compile a resolved expression over the frame it will run against into a node tree — once
    /// per step, before the row loop. The vectors decide the leaves' types exactly (a typed vector
    /// IS its declared type), so the typed kernels read and produce carrier values unboxed; where a
    /// leaf is boxed or the typer cannot decide, the reference arm answers.
    let internal compileExpr (f: Frame) (e: ResolvedExpr) : CompiledExpr =
        let slot: ErrorSlot = { Error = None; Null = false }
        let kernels = ResizeArray<Kernel>()
        let types = f.Cols |> List.map snd |> List.toArray

        // Record an error — the first one in the row wins — and answer a cell the caller will
        // discard: every ancestor checks the slot before it reads a child's answer.
        let fail (err: EvalError) : Cell =
            if Option.isNone slot.Error then
                slot.Error <- Some err

            Null

        let erred () : bool = Option.isSome slot.Error

        let unwrap (r: Result<Cell, EvalError>) : Cell =
            match r with
            | Ok c -> c
            | Error err -> fail err

        // ---- the typed path (Phase 267): readers, adapters and kernels over unboxed values ----

        /// A node as a cell reader — the boxed view a reference-arm parent reads its children by.
        /// A typed node boxes its present value; the flag says whether there is one.
        let cellOf (n: Node) : int -> Cell =
            match n with
            | NCell r -> r
            | NNull -> fun _ -> Null
            | NInt r ->
                fun p ->
                    let v = r p
                    if slot.Null then Null else Int v
            | NFloat r ->
                fun p ->
                    let v = r p
                    if slot.Null then Null else Float v
            | NBool r ->
                fun p ->
                    let v = r p
                    if slot.Null then Null else Bool v
            | NStr(ty, r) ->
                let mk = Vec.strCell ty

                fun p ->
                    let v = r p
                    if slot.Null then Null else mk v

        /// A node that never produces a present value, read in a typed parent's carrier.
        let absent (dflt: 'a) : int -> 'a =
            fun _ ->
                slot.Null <- true
                dflt

        let asInt (n: Node) : (int -> int) option =
            match n with
            | NInt r -> Some r
            | NNull -> Some(absent 0)
            | NFloat _
            | NBool _
            | NStr _
            | NCell _ -> None

        /// An int node reads as a float node in the float carrier, as the reference widens it.
        let asFloat (n: Node) : (int -> float) option =
            match n with
            | NFloat r -> Some r
            | NInt r -> Some(fun p -> float (r p))
            | NNull -> Some(absent 0.0)
            | NBool _
            | NStr _
            | NCell _ -> None

        let asBool (n: Node) : (int -> bool) option =
            match n with
            | NBool r -> Some r
            | NNull -> Some(absent false)
            | NInt _
            | NFloat _
            | NStr _
            | NCell _ -> None

        let asStr (n: Node) : (int -> string) option =
            match n with
            | NStr(_, r) -> Some r
            | NNull -> Some(absent "")
            | NInt _
            | NFloat _
            | NBool _
            | NCell _ -> None

        /// Does the node answer null at this row? The typed readers are run for their flag (and
        /// their errors); a boxed reader for its cell.
        let presence (n: Node) : int -> bool =
            match n with
            | NInt r ->
                fun p ->
                    r p |> ignore
                    slot.Null
            | NFloat r ->
                fun p ->
                    r p |> ignore
                    slot.Null
            | NBool r ->
                fun p ->
                    r p |> ignore
                    slot.Null
            | NStr(_, r) ->
                fun p ->
                    r p |> ignore
                    slot.Null
            | NNull -> fun _ -> true
            | NCell r ->
                fun p ->
                    match r p with
                    | Null -> true
                    | _ -> false

        /// A typed binary node: the left operand, then the right, then `combine` over two present
        /// values — stopping at the first recorded error exactly where `evalResolved`'s
        /// `Result.bind` chain stops, and null where either operand is (`combine` may still answer
        /// null itself, as a division by zero does).
        let lift2 (ra: int -> 'a) (rb: int -> 'b) (dflt: 'r) (combine: 'a -> 'b -> 'r) : int -> 'r =
            fun p ->
                let x = ra p

                if erred () then
                    dflt
                else
                    let xn = slot.Null
                    let y = rb p

                    if erred () then
                        dflt
                    else
                        let yn = slot.Null

                        if xn || yn then
                            slot.Null <- true
                            dflt
                        else
                            slot.Null <- false
                            combine x y

        /// The integer kernel's result in the int32 band, or the reference's overflow, recorded.
        let ranged (ctx: string) (r: int64) : int =
            if inInt32Range r then
                int r
            else
                fail (overflowed ctx r) |> ignore
                0

        /// The integer kernel's operation over two present ints, with the reference's overflow
        /// rule and message; `Mod` by zero answers null. int64 remainder avoids the .NET
        /// `Int32.MinValue % -1` OverflowException (Phase 39).
        let intOp (op: BinOp) : (int -> int -> int) option =
#if FABLE_COMPILER
            // Under JavaScript (Phase 326) the result is carried in float64 and recomputed in int64
            // (a BigInt there) only when it leaves the int32 band, for `ranged`'s refusal and message;
            // `checkedFloatInt` says why the carried result is exact inside the band.
            match op with
            | Add ->
                Some(fun x y ->
                    let r = float x + float y

                    if inInt32Band r then
                        int r
                    else
                        ranged "add" (int64 x + int64 y))
            | Sub ->
                Some(fun x y ->
                    let r = float x - float y

                    if inInt32Band r then
                        int r
                    else
                        ranged "sub" (int64 x - int64 y))
            | Mul ->
                Some(fun x y ->
                    let r = float x * float y

                    if inInt32Band r then
                        int r
                    else
                        ranged "mul" (int64 x * int64 y))
            | Mod ->
                Some(fun x y ->
                    if y = 0 then
                        slot.Null <- true
                        0
                    else
                        // A remainder is inside the band, `Int32.MinValue % -1` included (`0`).
                        int (float x % float y))
            | _ -> None
#else
            match op with
            | Add -> Some(fun x y -> ranged "add" (int64 x + int64 y))
            | Sub -> Some(fun x y -> ranged "sub" (int64 x - int64 y))
            | Mul -> Some(fun x y -> ranged "mul" (int64 x * int64 y))
            | Mod ->
                Some(fun x y ->
                    if y = 0 then
                        slot.Null <- true
                        0
                    else
                        ranged "mod" (int64 x % int64 y))
            | _ -> None
#endif

        /// The float-carrier arithmetic an operator performs; `Div` by zero answers null, and `Mod`
        /// is integer-only in the reference, so it is not here.
        let floatOp (op: BinOp) : (float -> float -> float) option =
            match op with
            | Add -> Some(fun x y -> x + y)
            | Sub -> Some(fun x y -> x - y)
            | Mul -> Some(fun x y -> x * y)
            | Div ->
                Some(fun x y ->
                    if y = 0.0 then
                        slot.Null <- true
                        0.0
                    else
                        x / y)
            | _ -> None

        /// Kleene `And` (`isAnd`) / `Or` over two boolean readers: a decided operand decides —
        /// `false` under `And`, `true` under `Or` — then a null operand makes the answer null, then
        /// both are present. Both operands are read, as the reference reads them, for their errors.
        let kleene (isAnd: bool) (ra: int -> bool) (rb: int -> bool) : int -> bool =
            fun p ->
                let x = ra p

                if erred () then
                    false
                else
                    let xn = slot.Null
                    let y = rb p

                    if erred () then
                        false
                    else
                        let yn = slot.Null

                        if (not xn && x = not isAnd) || (not yn && y = not isAnd) then
                            slot.Null <- false
                            not isAnd
                        elif xn || yn then
                            slot.Null <- true
                            false
                        else
                            slot.Null <- false
                            isAnd

        /// The vector path of a binary node: the kernel over two typed children, unboxed — or
        /// `None`, where a child is boxed and the kernel's cell path (with its per-row tag check)
        /// answers instead.
        let typedBinary (op: BinOp) (kernel: Kernel) (na: Node) (nb: Node) : Node option =
            match kernel with
            | IntArith ->
                match asInt na, asInt nb, intOp op with
                | Some ra, Some rb, Some k -> Some(NInt(lift2 ra rb 0 k))
                | _ -> None
            | FloatArith ->
                match asFloat na, asFloat nb, floatOp op with
                | Some ra, Some rb, Some k -> Some(NFloat(lift2 ra rb 0.0 k))
                | _ -> None
            | NumCompare ->
                match comparisonTest op with
                | None -> None
                | Some test ->
                    match na, nb with
                    | (NInt _ | NNull), (NInt _ | NNull) ->
                        match asInt na, asInt nb with
                        | Some ra, Some rb -> Some(NBool(lift2 ra rb false (fun x y -> test (compare x y))))
                        | _ -> None
                    | _ ->
                        match asFloat na, asFloat nb with
                        | Some ra, Some rb -> Some(NBool(lift2 ra rb false (fun x y -> test (compareNum x y))))
                        | _ -> None
            | OrdinalCompare ->
                match comparisonTest op, asStr na, asStr nb with
                | Some test, Some ra, Some rb ->
                    Some(NBool(lift2 ra rb false (fun x y -> test (System.String.CompareOrdinal(x, y)))))
                | _ -> None
            | StrPredicate ->
                match stringTest op, asStr na, asStr nb with
                | Some test, Some ra, Some rb -> Some(NBool(lift2 ra rb false test))
                | _ -> None
            | Logical ->
                match asBool na, asBool nb with
                | Some ra, Some rb ->
                    match op with
                    | And -> Some(NBool(kleene true ra rb))
                    | Or -> Some(NBool(kleene false ra rb))
                    | _ -> None
                | _ -> None
            | Boxed -> None

        // ---- the cell path (Phase 266): the reference arm over boxed readers ----

        /// A binary node over boxed readers: evaluate the left operand, then the right, then
        /// combine — stopping at the first recorded error exactly where `evalResolved`'s
        /// `Result.bind` chain stops. A typed kernel here checks the tags per row: a pair it
        /// handles is answered in the carrier through the SAME operation the vector path runs, and
        /// every other pair — a cell disagreeing with its column's declared type included — by
        /// the reference's `binaryOp`.
        let binaryNode (op: BinOp) (kernel: Kernel) (ra: int -> Cell) (rb: int -> Cell) : int -> Cell =
            let boxed (x: Cell) (y: Cell) : Cell = unwrap (binaryOp op x y)

            let both (combine: Cell -> Cell -> Cell) : int -> Cell =
                fun p ->
                    let x = ra p

                    if erred () then
                        Null
                    else
                        let y = rb p
                        if erred () then Null else combine x y

            /// A carrier operation over two present values, boxed — null where the operation
            /// answered null.
            let inCarrier (k: 'a -> 'b -> 'r) (box: 'r -> Cell) (x: 'a) (y: 'b) : Cell =
                slot.Null <- false
                let r = k x y
                if slot.Null then Null else box r

            match kernel with
            | Boxed -> both boxed
            | IntArith ->
                match intOp op with
                | Some k ->
                    both (fun x y ->
                        match x, y with
                        | Int i, Int j -> inCarrier k Int i j
                        | Null, _
                        | _, Null -> Null
                        | _ -> boxed x y)
                | None -> both boxed
            | FloatArith ->
                match floatOp op with
                | Some k ->
                    // Two `Int`s under `Add`/`Sub`/`Mul` are INTEGER arithmetic in the reference
                    // (a typed kernel was chosen because a `Float` was declared, so the pair is a
                    // cell disagreeing with its column); only `Div` divides them as floats.
                    let ints: int -> int -> Cell =
                        match op with
                        | Div -> fun i j -> inCarrier k Float (float i) (float j)
                        | _ -> fun i j -> boxed (Int i) (Int j)

                    both (fun x y ->
                        match x, y with
                        | Float p, Float q -> inCarrier k Float p q
                        | Int i, Float q -> inCarrier k Float (float i) q
                        | Float p, Int j -> inCarrier k Float p (float j)
                        | Int i, Int j -> ints i j
                        | Null, _
                        | _, Null -> Null
                        | _ -> boxed x y)
                | None -> both boxed
            | NumCompare ->
                match comparisonTest op with
                | Some test ->
                    both (fun x y ->
                        match x, y with
                        | Int i, Int j -> Bool(test (compare i j))
                        | Float p, Float q -> Bool(test (compareNum p q))
                        | Int i, Float q -> Bool(test (compareNum (float i) q))
                        | Float p, Int j -> Bool(test (compareNum p (float j)))
                        | Null, _
                        | _, Null -> Null
                        | _ -> boxed x y)
                | None -> both boxed
            | OrdinalCompare ->
                match comparisonTest op with
                | Some test ->
                    both (fun x y ->
                        match x, y with
                        | Str p, Str q
                        | Date p, Date q
                        | Timestamp p, Timestamp q -> Bool(test (System.String.CompareOrdinal(p, q)))
                        | Null, _
                        | _, Null -> Null
                        | _ -> boxed x y)
                | None -> both boxed
            | StrPredicate ->
                match stringTest op with
                | Some test ->
                    both (fun x y ->
                        match x, y with
                        | Str s, Str t -> Bool(test s t)
                        | Null, _
                        | _, Null -> Null
                        | _ -> boxed x y)
                | None -> both boxed
            | Logical ->
                match op with
                | And ->
                    both (fun x y ->
                        match x, y with
                        | Bool p, Bool q -> Bool(p && q)
                        | Bool false, Null
                        | Null, Bool false -> Bool false
                        | Bool true, Null
                        | Null, Bool true
                        | Null, Null -> Null
                        | _ -> boxed x y)
                | Or ->
                    both (fun x y ->
                        match x, y with
                        | Bool p, Bool q -> Bool(p || q)
                        | Bool true, Null
                        | Null, Bool true -> Bool true
                        | Bool false, Null
                        | Null, Bool false
                        | Null, Null -> Null
                        | _ -> boxed x y)
                | _ -> both boxed

        // ---- the multi-way nodes: typed where every value child shares one carrier ----

        /// The carrier a set of value nodes shares, where every node is that carrier or `NNull`:
        /// 1 int, 2 float, 3 bool, 4 string, 0 every one null; -1 where they mix or one is boxed.
        let carrierOf (ns: Node list) : int =
            let kind (n: Node) : int =
                match n with
                | NInt _ -> 1
                | NFloat _ -> 2
                | NBool _ -> 3
                | NStr _ -> 4
                | NNull -> 0
                | NCell _ -> -1

            (0, ns)
            ||> List.fold (fun acc n ->
                let k = kind n

                if acc = -1 || k = -1 then -1
                elif acc = 0 then k
                elif k = 0 || k = acc then acc
                else -1)

        /// The first present child's value, or null when none is — children left to right, stopping
        /// at the first error, as `evalResolved` visits them.
        let firstPresent (rs: (int -> 'a)[]) (dflt: 'a) : int -> 'a =
            fun p ->
                let mutable result = dflt
                let mutable i = 0
                let mutable searching = true

                while searching && i < rs.Length do
                    let v = rs[i]p

                    if erred () then
                        searching <- false
                    elif slot.Null then
                        i <- i + 1
                    else
                        result <- v
                        searching <- false

                if searching then
                    slot.Null <- true

                result

        /// A `Coalesce`: typed where every child shares a carrier (`t` names the string family),
        /// the reference arm otherwise.
        let coalesceNode (t: Typing) (ns: Node list) : Node =
            let typed (adapt: Node -> (int -> 'a) option) (dflt: 'a) : int -> 'a =
                firstPresent (ns |> List.map (adapt >> Option.get) |> List.toArray) dflt

            let boxed () : Node =
                let rs = ns |> List.map cellOf |> List.toArray

                NCell(fun p ->
                    let mutable result = Null
                    let mutable i = 0
                    let mutable searching = true

                    while searching && i < rs.Length do
                        let v = rs[i]p

                        if erred () then
                            searching <- false
                        else
                            match v with
                            | Null -> i <- i + 1
                            | present ->
                                result <- present
                                searching <- false

                    result)

            match carrierOf ns, t with
            | 0, _ -> NNull
            | 1, _ -> NInt(typed asInt 0)
            | 2, _ -> NFloat(typed asFloat 0.0)
            | 3, _ -> NBool(typed asBool false)
            | 4, Of ty -> NStr(ty, typed asStr "")
            | _ -> boxed ()

        /// A `Case`: the first arm whose `when` is `Bool true` answers, else the `else`. Every
        /// `when` is read for its errors whatever its carrier; the arms are typed where they share
        /// one (`t` names the string family), the reference arm otherwise.
        let caseNode (t: Typing) (whens: Node list) (thens: Node list) (els: Node) : Node =
            let whenTrue (n: Node) : int -> bool =
                match n with
                | NBool r ->
                    fun p ->
                        let b = r p
                        (not slot.Null) && b
                | NNull -> fun _ -> false
                | NCell _
                | NInt _
                | NFloat _
                | NStr _ ->
                    let r = cellOf n

                    fun p ->
                        match r p with
                        | Bool true -> true
                        | _ -> false

            let ws = whens |> List.map whenTrue |> List.toArray

            let select (rs: (int -> 'a)[]) (rElse: int -> 'a) (dflt: 'a) : int -> 'a =
                fun p ->
                    let mutable result = dflt
                    let mutable i = 0
                    let mutable searching = true

                    while searching && i < ws.Length do
                        let w = ws[i]p

                        if erred () then
                            searching <- false
                        elif w then
                            result <- rs[i]p
                            searching <- false
                        else
                            i <- i + 1

                    if searching then rElse p else result

            let typed (adapt: Node -> (int -> 'a) option) (dflt: 'a) : int -> 'a =
                select (thens |> List.map (adapt >> Option.get) |> List.toArray) (adapt els |> Option.get) dflt

            match carrierOf (els :: thens), t with
            // Every arm null: the whens are still read, for their errors.
            | 0, _
            | 1, _ -> NInt(typed asInt 0)
            | 2, _ -> NFloat(typed asFloat 0.0)
            | 3, _ -> NBool(typed asBool false)
            | 4, Of ty -> NStr(ty, typed asStr "")
            | _ -> NCell(select (thens |> List.map cellOf |> List.toArray) (cellOf els) Null)

        let rec go (e: ResolvedExpr) : Typing * Node =
            match e with
            | RCol i ->
                match f.Vecs[i] with
                | Ints(vals, mask) ->
                    Of IntType,
                    NInt(fun p ->
                        slot.Null <- not (Raw.at p mask)
                        Raw.at p vals)
                | Floats(vals, mask) ->
                    Of FloatType,
                    NFloat(fun p ->
                        slot.Null <- not (Raw.at p mask)
                        Raw.at p vals)
                | Bools(vals, mask) ->
                    Of BoolType,
                    NBool(fun p ->
                        slot.Null <- not (Raw.at p mask)
                        Raw.at p vals)
                | Strs(ty, vals, mask) ->
                    Of ty,
                    NStr(
                        ty,
                        fun p ->
                            slot.Null <- not (Raw.at p mask)
                            Raw.at p vals
                    )
                // A column whose cells disagree with its declared type is typed as declared, as the
                // schema-only typer types it, and read boxed: every kernel over it checks the tag per
                // row and hands a disagreeing cell to the reference arm.
                // A decimal vector (Phase 280) reads its cells, as the boxed vector does: the expression
                // arms over a decimal are exact text arithmetic, and the scaled integers serve the
                // filter, sort and sum kernels only.
                | Decs(_, _, cells, _)
                | Cells cells -> Of types[i], NCell(fun p -> cells[p])
            | RConst c ->
                let present (v: 'a) : int -> 'a =
                    fun _ ->
                        slot.Null <- false
                        v

                Typing.ofCell c,
                (match c with
                 | Int v -> NInt(present v)
                 | Float v -> NFloat(present v)
                 | Bool v -> NBool(present v)
                 | Str s -> NStr(StringType, present s)
                 | Date s -> NStr(DateType, present s)
                 | Timestamp s -> NStr(TimestampType, present s)
                 // A decimal constant is read boxed: expressions over decimals are exact text
                 // arithmetic, and a filter's comparison of a carried column is the kernel's (Phase 280).
                 | Decimal _ -> NCell(present c)
                 | Null -> NNull)
            | RFail err -> Absent, NCell(fun _ -> fail err)
            | RBinary(op, a, b) ->
                let ta, na = go a
                let tb, nb = go b
                let kernel = kernelOf op ta tb
                kernels.Add kernel

                let node =
                    match typedBinary op kernel na nb with
                    | Some n -> n
                    | None -> NCell(binaryNode op kernel (cellOf na) (cellOf nb))

                Typing.binary op ta tb, node
            | RNot inner ->
                let t, n = go inner

                Typing.not' t,
                (match n with
                 // The flag is the child's: a null stays null.
                 | NBool r -> NBool(fun p -> not (r p))
                 | NNull -> NNull
                 | NInt _
                 | NFloat _
                 | NStr _
                 | NCell _ ->
                     let r = cellOf n

                     NCell(fun p ->
                         let v = r p
                         if erred () then Null else unwrap (notCell v)))
            | RCoalesce exprs ->
                let ts, ns = exprs |> List.map go |> List.unzip
                let t = Typing.joinAll ts
                t, coalesceNode t ns
            | RCase(cases, elseExpr) ->
                let arms =
                    cases
                    |> List.map (fun (w, t) ->
                        let _, nw = go w
                        let tt, nt = go t
                        tt, nw, nt)

                let tElse, nElse = go elseExpr
                let t = Typing.case (arms |> List.map (fun (tt, _, _) -> tt)) tElse
                t, caseNode t (arms |> List.map (fun (_, nw, _) -> nw)) (arms |> List.map (fun (_, _, nt) -> nt)) nElse
            | RCast(ty, inner) ->
                let t, n = go inner
                let r = cellOf n

                Typing.cast ty t,
                NCell(fun p ->
                    let v = r p
                    if erred () then Null else unwrap (castCell ty v))
            | RInList(subject, items) ->
                let ts, ns = go subject
                let rs = cellOf ns
                let ri = items |> List.map (go >> snd >> cellOf) |> List.toArray

                Typing.inList ts,
                NCell(fun p ->
                    let sv = rs p

                    if erred () then
                        Null
                    else
                        match sv with
                        | Null -> Null
                        | _ ->
                            // SQL three-valued membership: any equal => true; no match seen a null
                            // => null; the first incomparable item is the error.
                            let mutable result = Null
                            let mutable decided = false
                            let mutable sawNull = false
                            let mutable i = 0

                            while not decided && i < ri.Length do
                                let iv = ri[i]p

                                if erred () then
                                    decided <- true
                                else
                                    match iv with
                                    | Null ->
                                        sawNull <- true
                                        i <- i + 1
                                    | _ ->
                                        match inMatch sv iv with
                                        | Ok true ->
                                            result <- Bool true
                                            decided <- true
                                        | Ok false -> i <- i + 1
                                        | Error err ->
                                            result <- fail err
                                            decided <- true

                            if decided then result
                            elif sawNull then Null
                            else Bool false)
            | RIsNull inner ->
                let _, n = go inner
                let isNullAt = presence n

                Typing.isNull,
                NBool(fun p ->
                    let absent = isNullAt p
                    slot.Null <- false
                    absent)
            | RApplyFn(fn, args) ->
                let ts, ns = args |> List.map go |> List.unzip
                let rs = ns |> List.map cellOf |> List.toArray

                Typing.applyFn fn ts,
                NCell(fun p ->
                    // Arguments left to right, stopping at the first error, as `evalArgs` does.
                    let vals: Cell[] = Array.zeroCreate rs.Length
                    let mutable i = 0

                    while not (erred ()) && i < rs.Length do
                        vals[i] <- rs[i]p
                        i <- i + 1

                    if erred () then
                        Null
                    else
                        unwrap (applyScalar fn (List.ofArray vals)))
            // Phase 277 — boxed: no typed carrier holds a decimal. The scale first, then the
            // operands left to right, as `evalResolved` reads them.
            | RQuotient(scale, mode, a, b) ->
                let ta, na = go a
                let tb, nb = go b
                let ra = cellOf na
                let rb = cellOf nb

                Typing.quotient ta tb,
                NCell(fun p ->
                    match scale with
                    | Error err -> fail err
                    | Ok n ->
                        let av = ra p

                        if erred () then
                            Null
                        else
                            let bv = rb p

                            if erred () then
                                Null
                            else
                                unwrap (quotientCell mode n av bv))
            | RRounded(scale, mode, a) ->
                let ta, na = go a
                let ra = cellOf na

                Typing.rounded ta,
                NCell(fun p ->
                    match scale with
                    | Error err -> fail err
                    | Ok n ->
                        let av = ra p
                        if erred () then Null else unwrap (roundedCell mode n av))

        let _, node = go e

        { Node = node
          Run = cellOf node
          Slot = slot
          Kernels = List.ofSeq kernels }

    /// Evaluate a compiled expression at one PHYSICAL row of the frame it was compiled against, in
    /// the reference's envelope: the slot is reset, the tree runs, and the first error it recorded
    /// — or the cell — is the answer.
    let internal runCompiled (c: CompiledExpr) (p: int) : Result<Cell, EvalError> =
        c.Slot.Error <- None
        let v = c.Run p

        match c.Slot.Error with
        | Some err -> Error err
        | None -> Ok v

    // ---- type inference for derived/melted columns ----

    /// Infer a column type from its cells — the present cells' types joined in row order under
    /// `ColumnType.widens` (`widenColumnType`, Phase 321: a column holding `Int 1` and `Float 2.5`
    /// is a float column, one holding `Int 0` and `Decimal 1.5` a decimal column, where the first
    /// present cell used to decide and left a cell its column's type does not carry), else
    /// `StringType` (an all-null derived column has no observable type; `string` is the safe
    /// default).
    let private inferType (cells: Cell list) : ColumnType =
        let rec go (acc: ColumnType option) (rest: Cell list) =
            match rest with
            | [] -> acc
            | c :: tail ->
                match Cell.typeOf c, acc with
                | None, _ -> go acc tail
                | Some t, None -> go (Some t) tail
                | Some t, Some a -> go (Some(widenColumnType a t)) tail

        go None cells |> Option.defaultValue StringType

    /// `inferType` over the cells an index range reads (Phase 321) — the same fold, for a caller
    /// holding its cells in an array or behind a row order rather than in a list.
    let internal inferTypeAt (count: int) (cellAt: int -> Cell) : ColumnType =
        let mutable acc: ColumnType option = None

        for i in 0 .. count - 1 do
            match Cell.typeOf (cellAt i) with
            | None -> ()
            | Some t ->
                acc <-
                    match acc with
                    | None -> Some t
                    | Some a -> Some(widenColumnType a t)

        acc |> Option.defaultValue StringType

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
        // A present cell outside the column's type (Core `0.33.0`, its Phase 299): a cell the
        // evaluator's column carries but its declared type does not admit, or a decimal whose text
        // is not decimal text. Named, never truncated or dropped.
        | CellOutsideType(_, ct, cell) ->
            AggError("aggregate over a " + ct + " column met a cell outside its type: " + cell)

    /// Compute one aggregate over a cell list of the given source type — the evaluator's adapter onto the
    /// public `Column.aggregate` (single source of truth), threading the `EvalError` envelope.
    let private aggCells (fn: AggFn) (srcType: ColumnType) (cells: Cell list) : Result<Cell, EvalError> =
        Column.aggregate fn (Column.create "" srcType cells) |> Result.mapError aggErr

    let private aggType (fn: AggFn) (srcType: ColumnType) : ColumnType = Column.aggType fn srcType

    // ---- sort (pinned: stable; nulls last regardless of direction) ----

    /// A sort's keys resolved against one step's schema (Phase 263): each named column's index with
    /// its direction, in key order. A name the schema does not carry is DROPPED here, which is the
    /// comparator's pinned "unknown columns skipped" — the per-comparison lookup skipped it on every
    /// comparison, and resolving once skips it once.
    let internal resolveSortKeys (cols: Schema) (by: (string * SortDir) list) : (int * SortDir) list =
        by
        |> List.choose (fun (name, dir) -> colIndex cols name |> Option.map (fun i -> i, dir))

    /// The pinned row ordering over resolved keys and array rows — multi-key, nulls last regardless
    /// of direction. The comparator `evalSort`, `Window`'s ordering and the incremental seam's merge
    /// all call, so there is one definition of the order.
    let internal compareResolved (keys: (int * SortDir) list) (r1: Cell[]) (r2: Cell[]) : int =
        let rec go =
            function
            | [] -> 0
            | (i, dir) :: rest ->
                let a = r1[i]
                let b = r2[i]

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

        go keys

    // ---- per-verb evaluation ----

    // Every verb below is a function of the columnar frame (Phase 267; `Frame.fs`). Four families:
    //
    //   * the ROW-SET verbs — `Filter`, `Limit`, `Sort`, `Project` — change which rows the frame
    //     holds, in what order, or which columns: a new selection or a new column index over the
    //     same vectors, and no cell copied;
    //   * `Derive` adds one vector, filled through the compiled expression at the selected rows,
    //     and shares every other vector by reference;
    //   * the GATHERING verbs — `GroupBy`, `Join`, `Window`, `Pivot`, `Unpivot`, `Union` — read
    //     rows through the selection and emit fresh vectors (`Window` emits one and shares the
    //     rest; `Union` appends vector to vector);
    //   * `Distinct`, the filtering joins and the set operations keep a subset of the rows in
    //     their order — a selection again.
    //
    // The cells every verb produces are the cells the row form produced; the transform law
    // vectors hold them so.

    /// The comparison kernel a comparison operator is; `None` off the comparisons.
    let private cmpOpOf (op: BinOp) : CmpOp option =
        match op with
        | Lt -> Some CLt
        | Le -> Some CLe
        | Gt -> Some CGt
        | Ge -> Some CGe
        | Eq -> Some CEq
        | Ne -> Some CNe
        | _ -> None

    /// The physical rows on which a filter predicate is `true`, as a selection bitmap (Phase 270)
    /// — where the predicate is built from `And` and `Or` over comparisons of a typed numeric
    /// column with a constant (either side), and `None` for any other predicate, which the
    /// compiled path answers. Such a predicate raises no error on any row: a comparison of a
    /// present int, float or carried decimal (Phase 280) with a present number it can read is
    /// always decided, and an absent row compares null, which a filter drops. `And` keeps the rows
    /// both sides keep and `Or` the rows either side keeps, which is Kleene logic read at `true`.
    let rec internal filterBits (k: KernelSet) (f: Frame) (e: ResolvedExpr) : uint32[] option =
        let leaf (op: CmpOp) (i: int) (c: Cell) : uint32[] option =
            match f.Vecs[i], c with
            | Ints(vals, mask), Int x -> Some(k.CmpInts op vals mask x f.Count)
            | Floats(vals, mask), Float x -> Some(k.CmpFloats op vals mask x f.Count)
            | Floats(vals, mask), Int x -> Some(k.CmpFloats op vals mask (float x) f.Count)
            // A decimal vector (Phase 280) against a constant read at the column's scale: both sides
            // exact integers in the float carrier, so the float comparison is the decimal one. A
            // constant that does not read at that scale (more fraction digits, past the width) is
            // the compiled path's, as a decimal against a float always is (it is refused there).
            | Decs(vals, scale, _, mask), Decimal x ->
                match ScaledDecimal.tryScaled scale x with
                | ValueSome kx -> Some(k.CmpFloats op vals mask kx f.Count)
                | ValueNone -> None
            | Decs(vals, scale, _, mask), Int x ->
                let kx = float x * ScaledDecimal.pow10[scale]

                if abs kx <= ScaledDecimal.maxExact then
                    Some(k.CmpFloats op vals mask kx f.Count)
                else
                    None
            | _ -> None

        match e with
        | RBinary(And, a, b) -> Option.map2 k.And (filterBits k f a) (filterBits k f b)
        | RBinary(Or, a, b) -> Option.map2 k.Or (filterBits k f a) (filterBits k f b)
        | RBinary(op, RCol i, RConst c) -> cmpOpOf op |> Option.bind (fun o -> leaf o i c)
        | RBinary(op, RConst c, RCol i) -> cmpOpOf op |> Option.bind (fun o -> leaf (Kernels.flip o) i c)
        | _ -> None

    let private evalFilter
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (pred: ColExpr)
        : Result<Frame, EvalError> =
        let resolved = resolveExpr env f.Cols pred

        // Phase 333 — a selection naming a row past the vectors is REFUSED, by name. The comparison
        // kernels read a bitmap over the vectors, where such an entry reads as an unset bit and was
        // silently DROPPED; the compiled path refused it with the runtime's index message. Both
        // hosts now refuse it here, before either path, with one message.
        let past =
            match f.Sel with
            | Some sel -> sel |> Array.tryFind (fun p -> p < 0 || p >= f.Count)
            | None -> None

        match past, filterBits k f resolved with
        | Some p, _ ->
            Error(
                TypeError(
                    "the frame's selection names row "
                    + string p
                    + ", past its "
                    + string f.Count
                    + "-row vectors"
                )
            )
        | None, Some bits ->
            // The comparison kernels (Phase 270): the predicate's rows as a bitmap over the whole
            // vector, read back in the frame's logical order.
            match f.Sel with
            | None -> Ok(Frame.select f (k.Selection bits))
            | Some sel -> Ok(Frame.select f (sel |> Array.filter (Kernels.isSet bits)))
        | None, None ->
            // Compiled once per morsel (Phase 266; Phase 270's morsels), run once per logical row
            // in logical order within it; a typed boolean root is read unboxed. Each morsel keeps
            // its rows, and the morsels' rows in morsel order become the frame's selection; the
            // first morsel that met an error answers it, which is the first error in row order.
            let phys = Frame.physical f
            let m = Kernels.morselCount phys.Length
            let kept: int[][] = Array.create m [||]
            let errors: EvalError option[] = Array.create m None

            let run (j: int) : bool =
                let compiled = compileExpr f resolved
                let slot = compiled.Slot

                let keep: int -> bool =
                    match compiled.Node with
                    | NBool r ->
                        fun p ->
                            let b = r p
                            (not slot.Null) && b
                    | NNull -> fun _ -> false
                    | NInt _
                    | NFloat _
                    | NStr _
                    | NCell _ ->
                        let r = compiled.Run

                        fun p ->
                            match r p with
                            | Bool true -> true
                            | _ -> false

                let hi = Kernels.morselEnd phys.Length j
                let mine = ResizeArray<int>()
                let mutable i = Kernels.morselStart j

                while Option.isNone errors[j] && i < hi do
                    slot.Error <- None
                    let p = Raw.get i phys
                    let keepIt = keep p

                    match slot.Error with
                    | Some e -> errors[j] <- Some e
                    | None ->
                        if keepIt then
                            mine.Add p

                        i <- i + 1

                kept[j] <- mine.ToArray()
                Option.isNone errors[j]

            k.RunMorsels m run

            match Array.tryPick id errors with
            | Some e -> Error e
            | None -> Ok(Frame.select f (Array.concat kept))

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
            let idx = resolved |> List.map (fun (_, _, i) -> i) |> List.toArray
            Frame.project f idx (resolved |> List.map (fun (o, ty, _) -> o, ty)))

    /// A step's compiled tree has one shape: `compileExpr` is a function of the frame and the
    /// resolved expression, so every morsel's tree has the kind of root the first one has.
    let private sameShape () : 'a =
        invalidOp "a step's compiled trees disagree on their root's kind"

    /// `evalDerive` past the static refusal.
    let private evalDerivedColumn
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (name: string)
        (expr: ColExpr)
        (dt: DerivedTyping)
        : Result<Frame, EvalError> =
        // Compiled once per morsel (Phase 266; Phase 270's morsels), run once per logical row, in
        // logical order within the morsel, stopping at the first row that records an error; the
        // first morsel that met one answers it, which is the first error in row order. A typed
        // root is written straight into its carrier at the row's physical position — morsels
        // write disjoint positions — and the boxed root's cells are packed under the column's
        // type. The new vector is the frame's physical length, present exactly at the rows the
        // frame holds, and every other vector is shared.
        let resolved = resolveExpr env f.Cols expr
        let first = compileExpr f resolved
        let phys = Frame.physical f
        let n = phys.Length
        let count = f.Count
        let m = Kernels.morselCount n
        let errors: EvalError option[] = Array.create m None

        // Run `rowOf tree` at every logical row of every morsel, each morsel through its own tree.
        let run (rowOf: CompiledExpr -> int -> unit) : EvalError option =
            k.RunMorsels m (fun j ->
                let c = if j = 0 then first else compileExpr f resolved
                let slot = c.Slot
                let row = rowOf c
                let hi = Kernels.morselEnd n j
                let mutable i = Kernels.morselStart j

                while Option.isNone errors[j] && i < hi do
                    slot.Error <- None
                    row i

                    match slot.Error with
                    | Some e -> errors[j] <- Some e
                    | None -> i <- i + 1

                Option.isNone errors[j])

            Array.tryPick id errors

        // A column with no present cell (Phase 338): a decided column keeps its type, packed empty
        // under it; one the cells type is `StringType`, as it always was.
        let nonepresent () : ColumnType * Vec =
            let ty =
                match dt with
                | Decided ty -> ty
                | ByCells
                | Refused -> StringType

            ty, Vec.pack ty (Array.create count Null)

        // A typed vector holds one type, so the cells' join is that type and cannot refuse. Where
        // the column is decided, the decided type is the column's; a typed root of another type
        // would be the typer and the compiler disagreeing — its cells are kept as produced, boxed
        // under the decided type, and the derive-typing law (`Conformance.deriveTypingLaws`) is
        // what goes red on it.
        let typed
            (read: CompiledExpr -> int -> 'a)
            (mk: 'a[] -> bool[] -> Vec)
            (nodeTy: ColumnType)
            : Result<ColumnType * Vec, EvalError> =
            let vals: 'a[] = Array.zeroCreate count
            let mask: bool[] = Array.zeroCreate count

            let failed =
                run (fun c ->
                    let r = read c
                    let slot = c.Slot

                    fun i ->
                        let p = Raw.get i phys
                        let v = r p

                        if Option.isNone slot.Error && not slot.Null then
                            Raw.put vals p v
                            Raw.put mask p true)

            match failed with
            | Some e -> Error e
            | None ->
                if not (Vec.anyPresent mask phys) then
                    Ok(nonepresent ())
                else
                    match dt with
                    | Decided ty when ty <> nodeTy ->
                        let v = mk vals mask
                        Ok(ty, Cells(Array.init count (Vec.cellAt v)))
                    | Decided _
                    | ByCells
                    | Refused -> Ok(nodeTy, mk vals mask)

        let derived =
            match first.Node with
            | NInt _ ->
                typed
                    (fun c ->
                        match c.Node with
                        | NInt r -> r
                        | _ -> sameShape ())
                    (fun v m -> Ints(v, m))
                    IntType
            | NFloat _ ->
                typed
                    (fun c ->
                        match c.Node with
                        | NFloat r -> r
                        | _ -> sameShape ())
                    (fun v m -> Floats(v, m))
                    FloatType
            | NBool _ ->
                typed
                    (fun c ->
                        match c.Node with
                        | NBool r -> r
                        | _ -> sameShape ())
                    (fun v m -> Bools(v, m))
                    BoolType
            | NStr(sty, _) ->
                typed
                    (fun c ->
                        match c.Node with
                        | NStr(_, r) -> r
                        | _ -> sameShape ())
                    (fun v m -> Strs(sty, v, m))
                    sty
            | NNull -> Ok(nonepresent ())
            | NCell _ ->
                let cells: Cell[] = Array.zeroCreate n

                let failed =
                    run (fun c ->
                        let r =
                            match c.Node with
                            | NCell r -> r
                            | _ -> sameShape ()

                        let slot = c.Slot

                        fun i ->
                            let cell = r phys[i]

                            if Option.isNone slot.Error then
                                cells[i] <- cell)

                match failed with
                | Some e -> Error e
                | None ->
                    columnTypeBy dt name cells.Length (fun i -> cells[i])
                    |> Result.map (fun ty -> ty, Vec.packAt ty count (fun i -> phys[i]) cells)

        derived |> Result.map (fun (ty, vec) -> Frame.withColumn f name ty vec)

    let private evalDerive
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (name: string)
        (expr: ColExpr)
        : Result<Frame, EvalError> =
        // The column's typing (Phase 338) is read off the schema before any row: a derive refused
        // statically is refused on every frame, an empty one included, and a decided one carries
        // its type whatever rows the frame holds.
        match derivedTyping f.Cols expr with
        | Refused -> Error(floatBesideDecimal name)
        | dt -> evalDerivedColumn k env f name expr dt

    /// Phase 323 — the GroupBy aggregates STREAMED: one pass over the rows, one accumulator per group
    /// slot per aggregate, results written straight into typed output vectors. It replaced, for
    /// `Count`, `Sum`, `Mean`, `Min`, `Max`, `First` and `Last`, a member list per group, a boxed cell
    /// list per group per aggregate (`columnOf`), a `Column` per group and a row array per group
    /// assembled through `Frame.ofRows` — about two thirds of the step on the corpus's `byRegion`.
    ///
    /// **There is still ONE aggregate semantics, `Column.aggregate`, and a stream answers only where
    /// its answer is that one.** Every case the stream does not reproduce outright it DEFERS, and a
    /// deferred group-aggregate is computed exactly as before, by `Column.aggregate` over the group's
    /// members in member order: `Median`, `StdDev` and `CountDistinct` always (they need the members),
    /// a numeric aggregate over a non-numeric column (a refusal), a group holding a cell
    /// `Column.aggregate` refuses as outside its column's type, an int `Sum` past int32, a float
    /// `Sum` or `Mean` whose plain form left the float range over finite input (Phase 306's refusal
    /// and its scaled recomputation), and a decimal past the float range under `Mean`. A deferral is
    /// never an error of its own, so the first error in group order, then aggregate order, is the
    /// reference's. A differential law in the suite (`streamedAggregateLaws`) holds every streamed
    /// answer equal to `Column.aggregate` over the same members in the same order, over every
    /// aggregate and every cell mix its generators draw, and is red against each perturbed
    /// accumulator it is handed.
    ///
    /// What each stream folds is what `Column.aggregate` folds, in the same order: a float `Sum` is
    /// the left fold from `0.0` in member order, so it is the same value to the bit; an int `Sum` is
    /// the int64 fold with the same int32 range check; `Min` keeps the first of equal minima and
    /// `Max` the last of equal maxima under the column layer's order (NaN one value above every
    /// other, `-0` equal to `0`); `First` and `Last` keep a group's first and last cell, `Null`
    /// included; a decimal is read canonicalised, and a decimal `Sum` is exact.
    module internal GroupAgg =

        /// The column layer's float order — `Cell.compareFloat`, internal to Core and read here as
        /// a copy the differential law holds equal to it (`Min`/`Max` over floats answer through
        /// `Cell.compare`): IEEE order on the non-NaN values, `-0 = 0`, NaN one value above every
        /// other. `nanLast = false` is the law's perturbation (the host order that put NaN first).
        let orderFloat (nanLast: bool) (a: float) (b: float) : int =
            match System.Double.IsNaN a, System.Double.IsNaN b with
            | true, true -> 0
            | true, false -> if nanLast then 1 else -1
            | false, true -> if nanLast then -1 else 1
            | false, false ->
                if a < b then -1
                elif a > b then 1
                else 0

        /// A cell as `Column.aggregate` admits it into a column of type `ty` (Core's `admit`): the
        /// cell itself, a `Decimal` canonicalised; `ValueNone` where `Column.aggregate` refuses it as
        /// outside the column's type — which the stream does not reproduce, but defers.
        let admitted (ty: ColumnType) (c: Cell) : Cell voption =
            match c, ty with
            // The common pairs first, matched on both tags: a cell of the column's own type, or an
            // int in a float or decimal column — `ColumnType.widens`' three rules, without its
            // equality test per cell (Phase 323).
            | Null, _ -> ValueSome Null
            | Int _, (IntType | FloatType | DecimalType)
            | Float _, FloatType
            | Bool _, BoolType
            | Str _, StringType
            | Date _, DateType
            | Timestamp _, TimestampType -> ValueSome c
            | _ ->
                match c with
                | Null -> ValueSome Null
                | Decimal s when ty = DecimalType ->
                    match DecimalText.tryCanonical s with
                    | Some canonical -> ValueSome(if canonical = s then c else Decimal canonical)
                    | None -> ValueNone
                | _ ->
                    match Cell.typeOf c with
                    | Some t when ColumnType.widens t ty -> ValueSome c
                    | Some _ -> ValueNone
                    | None -> ValueSome c

        let private isFinite (f: float) : bool =
            not (System.Double.IsNaN f || System.Double.IsInfinity f)

        // What a stream folds. `Defer` folds nothing: every group defers.
        [<Literal>]
        let private MDefer = 0

        [<Literal>]
        let private MCount = 1

        [<Literal>]
        let private MFirst = 2

        [<Literal>]
        let private MLast = 3

        [<Literal>]
        let private MMin = 4

        [<Literal>]
        let private MMax = 5

        [<Literal>]
        let private MSumInt = 6

        [<Literal>]
        let private MSumFloat = 7

        [<Literal>]
        let private MSumDecimal = 8

        [<Literal>]
        let private MMean = 9

        /// A perturbation of the accumulator, for the differential law's teeth and nothing else; the
        /// evaluator and the seam construct every stream with `Exact`.
        type Perturbation =
            | Exact
            /// `Max` keeps the FIRST of equal maxima.
            | MaxKeepsFirstTie
            /// The float order puts NaN below every other value.
            | NaNFirst
            /// A float `Sum` over finite input answers its overflowed total instead of deferring.
            | SumIgnoresOverflow
            /// `Count` counts `Null` cells too.
            | CountCountsNulls

        /// One aggregate streamed over `groups` group slots, reading its source column from the
        /// vector `v` of declared type `ty`. Feed it rows (`Feed`, or `FeedAll` for a whole frame),
        /// then read each slot's answer (`TryCell` / `Emit`); `ValueNone` / `false` is a deferral.
        [<Sealed>]
        type Stream(fn: AggFn, ty: ColumnType, v: Vec, groups: int, perturbation: Perturbation) =
            let numeric = ty = IntType || ty = FloatType || ty = DecimalType

            let mode =
                match fn with
                | Count -> MCount
                | First -> MFirst
                | Last -> MLast
                | Min -> MMin
                | Max -> MMax
                | Sum when ty = IntType -> MSumInt
                | Sum when ty = FloatType -> MSumFloat
                | Sum when ty = DecimalType -> MSumDecimal
                | Mean when numeric -> MMean
                | _ -> MDefer

            // The typed carriers, where the vector's carrier agrees with the column's type — every
            // present cell is then admitted as it is, and read without boxing. Anything else (the
            // boxed vector, or a carrier of another type) is read cell by cell and admitted.
            //
            // Kind 4 is a decimal vector (Phase 280): `floats` is then its unscaled integers at
            // `scale`, read by the decimal `Sum` alone; every other aggregate over it reads cells.
            let ints, floats, mask, kind, scale =
                match v with
                | Ints(a, m) when ty = IntType -> a, [||], m, 1, 0
                | Floats(a, m) when ty = FloatType -> [||], a, m, 2, 0
                | Bools(_, m) when ty = BoolType -> [||], [||], m, 3, 0
                | Strs(t, _, m) when t = ty && (t = StringType || t = DateType || t = TimestampType) ->
                    [||], [||], m, 3, 0
                | Decs(a, s, _, m) when ty = DecimalType -> [||], a, m, 4, s
                | _ -> [||], [||], [||], 0, 0

            let keepsLastTie = perturbation <> MaxKeepsFirstTie
            let nanLast = perturbation <> NaNFirst
            let countsNulls = perturbation = CountCountsNulls

            let deferred: bool[] = Array.zeroCreate groups
            let count: int[] = Array.zeroCreate groups
            // The row a slot answers from: its first row (`First`), its last (`Last`), its best so
            // far (`Min`/`Max`); `-1` for none.
            let row: int[] =
                if mode = MFirst || mode = MLast || mode = MMin || mode = MMax then
                    Array.create groups -1
                else
                    [||]

            let longs: int64[] = if mode = MSumInt then Array.zeroCreate groups else [||]

            let totals: float[] =
                if mode = MSumFloat || mode = MMean || (mode = MSumDecimal && kind = 4) then
                    Array.zeroCreate groups
                else
                    [||]

            let nonFinite: bool[] =
                if mode = MSumFloat || mode = MMean then
                    Array.zeroCreate groups
                else
                    [||]

            // `null` marks a slot no decimal has reached, so it is filled with null explicitly:
            // Fable's `Array.zeroCreate` fills a string array with "" (a JS string's default), and
            // under node every decimal total then read as the empty text (Phase 280).
            let decimals: string[] =
                if mode = MSumDecimal then
                    Array.create groups null
                else
                    [||]

            /// The admitted cell at physical row `p` (only ever asked of a row already admitted).
            let cellOf (p: int) : Cell =
                match admitted ty (Vec.cellAt v p) with
                | ValueSome c -> c
                | ValueNone -> Vec.cellAt v p

            /// Does `candidate` replace `best` under `Min` / `Max`, given `c = compare best candidate`?
            let replaces (c: int) : bool =
                if mode = MMin then c > 0
                elif keepsLastTie then c <= 0
                else c < 0

            /// One present value of a decimal vector into slot `g`'s decimal `Sum` (Phase 280). The
            /// slot's total stays an exact integer in the float carrier while its magnitude is at most
            /// `maxExact` — an exact sum of two such integers within that bound is computed exactly,
            /// and one past it computes past it, so the test cannot pass in error. The first addition
            /// that leaves the bound moves the slot to the text path for good: its total so far and
            /// every later value are added as `DecimalText`, which has no width.
            let addScaled (g: int) (x: float) =
                count[g] <- count[g] + 1

                if isNull decimals[g] then
                    let t = totals[g] + x

                    if abs t <= ScaledDecimal.maxExact then
                        totals[g] <- t
                    else
                        let before = ScaledDecimal.render scale totals[g]
                        let value = ScaledDecimal.render scale x
                        decimals[g] <- DecimalText.add before value |> Option.defaultValue before
                else
                    let value = ScaledDecimal.render scale x
                    decimals[g] <- DecimalText.add decimals[g] value |> Option.defaultValue decimals[g]

            /// One admitted present number into a float fold.
            let addFloat (g: int) (f: float) =
                totals[g] <- totals[g] + f
                count[g] <- count[g] + 1

                if not (isFinite f) then
                    nonFinite[g] <- true

            /// One row, read as a cell and admitted — the route for every carrier the typed loops
            /// below do not specialise.
            let feedCell (g: int) (p: int) =
                match admitted ty (Vec.cellAt v p) with
                | ValueNone -> deferred[g] <- true
                | ValueSome cell ->
                    match mode with
                    | MCount ->
                        match cell with
                        | Null ->
                            if countsNulls then
                                count[g] <- count[g] + 1
                        | _ -> count[g] <- count[g] + 1
                    | MFirst ->
                        if row[g] < 0 then
                            row[g] <- p
                    | MLast -> row[g] <- p
                    | MMin
                    | MMax ->
                        match cell with
                        | Null -> ()
                        | _ ->
                            count[g] <- count[g] + 1

                            if row[g] < 0 then
                                row[g] <- p
                            else
                                match Cell.compare (cellOf row[g]) cell with
                                | Some c when replaces c -> row[g] <- p
                                | _ -> ()
                    | MSumInt ->
                        match cell with
                        | Int i ->
                            longs[g] <- longs[g] + int64 i
                            count[g] <- count[g] + 1
                        | _ -> ()
                    | MSumFloat
                    | MMean ->
                        match cell with
                        | Int i -> addFloat g (float i)
                        | Float f -> addFloat g f
                        | Decimal s ->
                            match DecimalText.tryToFloat s with
                            | Some f -> addFloat g f
                            | None -> deferred[g] <- true
                        | _ -> ()
                    | MSumDecimal ->
                        let d =
                            match cell with
                            | Decimal s -> DecimalText.tryCanonical s
                            | Int i -> Some(string i)
                            | _ -> None

                        match d with
                        | Some d ->
                            if isNull decimals[g] then
                                decimals[g] <- d
                            else
                                decimals[g] <- DecimalText.add decimals[g] d |> Option.defaultValue decimals[g]
                        | None -> ()
                    | _ -> ()

            /// Fold the row at physical row `p` into slot `g`.
            member _.Feed(g: int, p: int) : unit =
                if mode <> MDefer && not deferred[g] then
                    if kind = 0 then
                        feedCell g p
                    else
                        match mode with
                        | MCount ->
                            if mask[p] || countsNulls then
                                count[g] <- count[g] + 1
                        | MFirst ->
                            if row[g] < 0 then
                                row[g] <- p
                        | MLast -> row[g] <- p
                        | MSumInt ->
                            if mask[p] then
                                longs[g] <- longs[g] + int64 ints[p]
                                count[g] <- count[g] + 1
                        | MSumDecimal when kind = 4 ->
                            if mask[p] then
                                addScaled g floats[p]
                        | MSumFloat
                        | MMean when kind <> 4 ->
                            if mask[p] then
                                addFloat g (if kind = 1 then float ints[p] else floats[p])
                        | MMin
                        | MMax when kind = 1 ->
                            if mask[p] then
                                count[g] <- count[g] + 1
                                let b = row[g]

                                if b < 0 || replaces (compare ints[b] ints[p]) then
                                    row[g] <- p
                        | MMin
                        | MMax when kind = 2 ->
                            if mask[p] then
                                count[g] <- count[g] + 1
                                let b = row[g]

                                if b < 0 || replaces (orderFloat nanLast floats[b] floats[p]) then
                                    row[g] <- p
                        | _ -> feedCell g p

            /// Fold every logical row `i` of a frame — physical row `phys[i]` — into its slot
            /// `slotOf[i]`, in logical order. The loops a GroupBy spends its time in are written out
            /// for the typed carriers; every other case is `Feed` row by row.
            member this.FeedAll(slotOf: int[], phys: int[]) : unit =
                let n = phys.Length
                // The loops prove `i` for `phys` and, checked once here, for `slotOf` (Phase 326); the
                // physical row and the slot each reads are indexes they do not prove.
                Raw.within n slotOf

                if mode = MDefer then
                    ()
                elif mode = MCount && kind <> 0 && not countsNulls then
                    for i in 0 .. n - 1 do
                        if Raw.at (Raw.get i phys) mask then
                            let g = Raw.get i slotOf
                            Raw.put count g (Raw.at g count + 1)
                elif mode = MSumInt && kind = 1 then
                    for i in 0 .. n - 1 do
                        let p = Raw.get i phys

                        if Raw.at p mask then
                            let g = Raw.get i slotOf
                            Raw.put longs g (Raw.at g longs + int64 (Raw.at p ints))
                            Raw.put count g (Raw.at g count + 1)
                elif (mode = MSumFloat || mode = MMean) && kind = 2 then
                    for i in 0 .. n - 1 do
                        let p = Raw.get i phys

                        if Raw.at p mask then
                            let g = Raw.get i slotOf
                            let f = Raw.at p floats
                            Raw.put totals g (Raw.at g totals + f)
                            Raw.put count g (Raw.at g count + 1)

                            if not (isFinite f) then
                                Raw.put nonFinite g true
                elif mode = MSumDecimal && kind = 4 then
                    for i in 0 .. n - 1 do
                        let p = Raw.get i phys

                        if Raw.at p mask then
                            addScaled (Raw.get i slotOf) (Raw.at p floats)
                else
                    for i in 0 .. n - 1 do
                        this.Feed(Raw.get i slotOf, Raw.get i phys)

            /// Slot `g`'s answer, or `ValueNone` where it defers to `Column.aggregate`.
            member _.TryCell(g: int) : Cell voption =
                if mode = MDefer || deferred[g] then
                    ValueNone
                else
                    match mode with
                    | MCount -> ValueSome(Int count[g])
                    | MFirst
                    | MLast
                    | MMin
                    | MMax -> ValueSome(if row[g] < 0 then Null else cellOf row[g])
                    | MSumInt ->
                        if count[g] = 0 then
                            ValueSome Null
                        else
                            let s = longs[g]

                            if s >= int64 System.Int32.MinValue && s <= int64 System.Int32.MaxValue then
                                ValueSome(Int(int s))
                            else
                                ValueNone
                    | MSumFloat ->
                        let t = totals[g]

                        if count[g] = 0 then
                            ValueSome Null
                        elif nonFinite[g] || isFinite t || perturbation = SumIgnoresOverflow then
                            ValueSome(Float t)
                        else
                            ValueNone
                    | MMean ->
                        if count[g] = 0 then
                            ValueSome Null
                        else
                            let m = totals[g] / float count[g]

                            if isFinite m || nonFinite[g] then
                                ValueSome(Float m)
                            else
                                ValueNone
                    | MSumDecimal when kind = 4 ->
                        if count[g] = 0 then
                            ValueSome Null
                        elif isNull decimals[g] then
                            ValueSome(Decimal(ScaledDecimal.render scale totals[g]))
                        else
                            ValueSome(Decimal decimals[g])
                    | MSumDecimal -> ValueSome(if isNull decimals[g] then Null else Decimal decimals[g])
                    | _ -> ValueNone

            /// Write slot `g`'s answer into row `g` of `out`, unboxed where the answer is a number of
            /// the output's carrier; `false` (nothing written) where it defers.
            member this.Emit(g: int, out: Output) : bool =
                if mode = MDefer || deferred[g] then
                    false
                else
                    match mode with
                    | MCount ->
                        out.Int(g, count[g])
                        true
                    | MSumFloat when count[g] > 0 && isFinite totals[g] ->
                        out.Float(g, totals[g])
                        true
                    | MMean when count[g] > 0 && isFinite (totals[g] / float count[g]) ->
                        out.Float(g, totals[g] / float count[g])
                        true
                    | MSumInt when
                        count[g] > 0
                        && longs[g] >= int64 System.Int32.MinValue
                        && longs[g] <= int64 System.Int32.MaxValue
                        ->
                        out.Int(g, int longs[g])
                        true
                    | MFirst
                    | MLast
                    | MMin
                    | MMax when kind = 1 || kind = 2 ->
                        let p = row[g]

                        if p < 0 || not mask[p] then out.Null g
                        elif kind = 1 then out.Int(g, ints[p])
                        else out.Float(g, floats[p])

                        true
                    | _ ->
                        match this.TryCell g with
                        | ValueSome c ->
                            out.Cell(g, c)
                            true
                        | ValueNone -> false

        /// One output column of `n` rows under the declared type `ty`, written row by row: an
        /// `int[]` or `float[]` carrier beside its mask for an int or float column while every cell
        /// written agrees with it, and the cells otherwise. `ToVec` is exactly `Vec.pack ty` over the
        /// cells written, so a frame built from these is the frame `Frame.ofRows` built.
        and [<Sealed>] Output(ty: ColumnType, n: int) =
            let typedInt = ty = IntType
            let typedFloat = ty = FloatType
            let ints: int[] = if typedInt then Array.zeroCreate n else [||]
            let floats: float[] = if typedFloat then Array.zeroCreate n else [||]
            let mask: bool[] = if typedInt || typedFloat then Array.zeroCreate n else [||]
            let mutable typed = typedInt || typedFloat
            let mutable cells: Cell[] = if typed then [||] else Array.create n Null

            let unpack () =
                cells <-
                    Array.init n (fun i ->
                        if not mask[i] then Null
                        elif typedInt then Int ints[i]
                        else Float floats[i])

                typed <- false

            member _.Null(i: int) : unit =
                if not typed then
                    cells[i] <- Null

            member _.Int(i: int, x: int) : unit =
                if typed && typedInt then
                    ints[i] <- x
                    mask[i] <- true
                else
                    if typed then
                        unpack ()

                    cells[i] <- Int x

            member _.Float(i: int, x: float) : unit =
                if typed && typedFloat then
                    floats[i] <- x
                    mask[i] <- true
                else
                    if typed then
                        unpack ()

                    cells[i] <- Float x

            member this.Cell(i: int, c: Cell) : unit =
                match c with
                | Null -> this.Null i
                | Int x -> this.Int(i, x)
                | Float x -> this.Float(i, x)
                | _ ->
                    if typed then
                        unpack ()

                    cells[i] <- c

            member _.ToVec() : Vec =
                if typed && typedInt then Ints(ints, mask)
                elif typed then Floats(floats, mask)
                else Vec.pack ty cells

    /// The typed row hasher (Phase 325): every keyed verb — `GroupBy`'s key probe, `Window`'s
    /// partitions, `Distinct`, `Intersect` / `Except`, `Pivot`'s index groups and its on-value
    /// match, and the join — keys rows through it, reading the typed vectors and boxing no cell.
    ///
    /// Two passes. A CODER gives each key column's values dense integer codes, reading the vector's
    /// carrier (an `int`, a `float`, a `string`) where the vector is typed and the cell where it is
    /// not, so a typed column and a boxed one holding equal values code alike. An INDEX then numbers
    /// the rows' code tuples: one code is its own slot, and several pack into one exact integer
    /// where their ranges allow (a plain array lookup when the packed range is small), with a hash
    /// table over the code tuple only when they do not.
    ///
    /// The coder speaks one of the two relations the evaluator partitions by (see `CellKey` above),
    /// and the caller names which:
    ///
    ///  * TOKEN (`cellEq = false`) — `CellKey`'s: `Int 1` and `Float 1.0` are two values, the two
    ///    zeroes one, every `NaN` one, `Null` a value equal to itself. The partitioning verbs.
    ///  * `cellEq` (`cellEq = true`) — the join's and the pivot on-value's: the numeric family by
    ///    value as floats (`Int 1` matches `Float 1.0`, `NaN` matches `NaN`, `-0.0` matches `0.0`), a
    ///    decimal only a decimal of the same value, and `Null` nothing at all — it has no code, so a
    ///    row holding one is never indexed and never found.
    ///
    /// A join key whose two columns are both `int` vectors is coded in the token relation, which is
    /// `cellEq` over present ints (the null rule is the caller's either way): the int arm never
    /// goes through the float carrier, and a column that may hold a float always does.
    module internal RowHash =

        /// The four codes a value outside the dictionaries takes, by index into `Coder.Specials`.
        let private SNaN = 0
        let private SNull = 1
        let private SFalse = 2
        let private STrue = 3

#if FABLE_COMPILER
        /// The table a coder keeps per carrier: the open slot table under JavaScript (Phase 326),
        /// `Dictionary` on .NET.
        type CodeMap<'k> = OpenSlots<'k>
#else
        /// The table a coder keeps per carrier.
        type CodeMap<'k> = System.Collections.Generic.Dictionary<'k, int>
#endif

        /// One key column's codes: a dictionary per carrier, one counter over all of them, so two
        /// values share a code exactly when the relation equates them.
        type Coder =
            {
                CellEq: bool
                Ints: CodeMap<int>
                Nums: CodeMap<float>
                Strs: CodeMap<string>
                Dates: CodeMap<string>
                Stamps: CodeMap<string>
                Decs: CodeMap<string>
                Specials: int[]
                /// A direct table over the int values `[IntLo, IntHi]` (token relation
                /// only): built from the first int vector a fresh coder meets when its range is small, so
                /// an int key column is coded by an array read instead of a dictionary probe. A value in
                /// the range is coded through the table and never the dictionary, so the two never hold
                /// one value twice.
                mutable IntLo: int
                mutable IntHi: int
                mutable IntTable: int[]
                mutable Count: int
            }

#if FABLE_COMPILER
        let private ints () : CodeMap<int> =
            OpenSlots<int>((fun i -> i), (fun a b -> a = b))

        // A float key is never `NaN` here (a special code) and never `-0.0` (`floatCode` adds `0.0`).
        let private nums () : CodeMap<float> =
            OpenSlots<float>((fun f -> hash f), (fun a b -> a = b))

        let private strs () : CodeMap<string> =
            OpenSlots<string>((fun s -> hash s), (fun a b -> System.String.Equals(a, b)))

        let coder (cellEq: bool) : Coder =
            { CellEq = cellEq
              Ints = ints ()
              Nums = nums ()
              Strs = strs ()
              Dates = strs ()
              Stamps = strs ()
              Decs = strs ()
              Specials = Array.create 4 -1
              IntLo = 0
              IntHi = -1
              IntTable = [||]
              Count = 0 }
#else
        let coder (cellEq: bool) : Coder =
            { CellEq = cellEq
              Ints = System.Collections.Generic.Dictionary<int, int>()
              Nums = System.Collections.Generic.Dictionary<float, int>()
              Strs = System.Collections.Generic.Dictionary<string, int>()
              Dates = System.Collections.Generic.Dictionary<string, int>()
              Stamps = System.Collections.Generic.Dictionary<string, int>()
              Decs = System.Collections.Generic.Dictionary<string, int>()
              Specials = Array.create 4 -1
              IntLo = 0
              IntHi = -1
              IntTable = [||]
              Count = 0 }
#endif

        /// `k`'s code in `d`, opening the next one when `openNew` and it has none; `-1` otherwise.
        let inline private codeIn (c: Coder) (d: CodeMap<'k>) (k: 'k) (openNew: bool) : int =
#if FABLE_COMPILER
            let x = d.Find k

            if x >= 0 then
                x
            elif openNew then
                let y = c.Count
                d.Add(k, y)
                c.Count <- y + 1
                y
            else
                -1
#else
            match d.TryGetValue k with
            | true, x -> x
            | _ ->
                if openNew then
                    let x = c.Count
                    d[k] <- x
                    c.Count <- x + 1
                    x
                else
                    -1
#endif

        let private special (c: Coder) (s: int) (openNew: bool) : int =
            let x = c.Specials[s]

            if x >= 0 || not openNew then
                x
            else
                let y = c.Count
                c.Specials[s] <- y
                c.Count <- y + 1
                y

        /// A float's code: every `NaN` one value, `-0.0` with `0.0` (adding `0.0` turns `-0.0` into
        /// `0.0` and leaves every other float as it is), anything else by IEEE equality.
        let private floatCode (c: Coder) (f: float) (openNew: bool) : int =
            if System.Double.IsNaN f then
                special c SNaN openNew
            else
                codeIn c c.Nums (f + 0.0) openNew

        let private intCode (c: Coder) (i: int) (openNew: bool) : int =
            if c.CellEq then
                floatCode c (float i) openNew
            // Compared against both ends, never as `i - IntLo`, which overflows for a far value.
            elif i >= c.IntLo && i <= c.IntHi then
                let at = i - c.IntLo
                let x = Raw.get at c.IntTable

                if x >= 0 || not openNew then
                    x
                else
                    let y = c.Count
                    Raw.set c.IntTable at y
                    c.Count <- y + 1
                    y
            else
                codeIn c c.Ints i openNew

        /// Give a FRESH token coder a direct table over an int vector's present values (at `phys`)
        /// when their range is at most a small multiple of the rows; otherwise leave it without one.
        let private intTableFor (c: Coder) (a: int[]) (m: bool[]) (phys: int[]) : unit =
            if not c.CellEq && c.Count = 0 && c.IntTable.Length = 0 then
                let mutable lo = System.Int32.MaxValue
                let mutable hi = System.Int32.MinValue

                for p in phys do
                    if Raw.at p m then
                        let x = Raw.at p a

                        if x < lo then
                            lo <- x

                        if x > hi then
                            hi <- x

                // The range as a float, so a span across the whole int range cannot overflow.
                if lo <= hi && float hi - float lo < float (max 1024 (4 * phys.Length)) then
                    c.IntLo <- lo
                    c.IntHi <- hi
                    c.IntTable <- Array.create (hi - lo + 1) -1

        let private nullCode (c: Coder) (openNew: bool) : int =
            if c.CellEq then -1 else special c SNull openNew

        let private boolCode (c: Coder) (b: bool) (openNew: bool) : int =
            special c (if b then STrue else SFalse) openNew

        /// The dictionary a string carrier is coded in: the tag the cell would carry (`Vec.strCell`'s mapping).
        let private strsOf (c: Coder) (ty: ColumnType) : CodeMap<string> =
            match ty with
            | DateType -> c.Dates
            | TimestampType -> c.Stamps
            | StringType
            | IntType
            | FloatType
            | BoolType
            | DecimalType -> c.Strs

        /// One cell's code — the boxed path, agreeing with every typed one above case for case.
        let cellCode (c: Coder) (cell: Cell) (openNew: bool) : int =
            match cell with
            | Null -> nullCode c openNew
            | Int i -> intCode c i openNew
            | Float f -> floatCode c f openNew
            | Bool b -> boolCode c b openNew
            | Str s -> codeIn c c.Strs s openNew
            | Date s -> codeIn c c.Dates s openNew
            | Timestamp s -> codeIn c c.Stamps s openNew
            // The canonical text (Phase 277): `1.50` and `1.5` are one value, in both relations.
            | Decimal _ -> codeIn c c.Decs (Cell.token cell) openNew

        /// The code of the value at each physical row `phys[i]` of `v`, read from the carrier.
        let codesOf (c: Coder) (v: Vec) (phys: int[]) (openNew: bool) : int[] =
            let n = phys.Length
            let out: int[] = Array.zeroCreate n

            match v with
            | Ints(a, m) ->
                if openNew then
                    intTableFor c a m phys

                for i in 0 .. n - 1 do
                    let p = Raw.get i phys

                    let code =
                        if Raw.at p m then
                            intCode c (Raw.at p a) openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            | Floats(a, m) ->
                for i in 0 .. n - 1 do
                    let p = Raw.get i phys

                    let code =
                        if Raw.at p m then
                            floatCode c (Raw.at p a) openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            | Bools(a, m) ->
                for i in 0 .. n - 1 do
                    let p = Raw.get i phys

                    let code =
                        if Raw.at p m then
                            boolCode c (Raw.at p a) openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            | Strs(ty, a, m) ->
                let d = strsOf c ty

                for i in 0 .. n - 1 do
                    let p = Raw.get i phys

                    let code =
                        if Raw.at p m then
                            codeIn c d (Raw.at p a) openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            | Decs(_, _, cells, _) ->
                for i in 0 .. n - 1 do
                    let code = cellCode c (Raw.at (Raw.get i phys) cells) openNew
                    Raw.set out i code
            | Cells cells ->
                for i in 0 .. n - 1 do
                    let code = cellCode c (Raw.at (Raw.get i phys) cells) openNew
                    Raw.set out i code

            out

        /// Code tuples as a hash-table key: equal exactly when every code is.
        let private codeRow: System.Collections.Generic.IEqualityComparer<int[]> =
            { new System.Collections.Generic.IEqualityComparer<int[]> with
                member _.Equals(a, b) =
                    if a.Length <> b.Length then
                        false
                    else
                        let mutable same = true
                        let mutable i = 0

                        while same && i < a.Length do
                            same <- a[i] = b[i]
                            i <- i + 1

                        same

                member _.GetHashCode r =
                    let mutable h = r.Length

                    for x in r do
                        h <- ((h <<< 5) ^^^ (h >>> 27)) ^^^ x

                    h }

        /// Slots over rows of codes (`codes[j][i]` is row `i`'s code in key column `j`, each below
        /// `cards[j]`), numbered from 0 in the order `Open` first meets them. A row holding a `-1`
        /// code has no slot: `Open` and `Find` answer `-1` for it.
        [<Sealed>]
        type Index(cards: int[], rows: int) =
            let k = cards.Length

            // The packed key `((c0 * card1 + c1) * card2 + c2) ...` is exact while the product of the
            // ranges is an `int`; below a small multiple of the rows it indexes an array directly.
            let range = cards |> Array.fold (fun acc c -> acc * float (max c 1)) 1.0
            let packs = range <= 2147483647.0
            let direct = packs && range <= float (max 1024 (4 * rows))
            let table: int[] = if direct then Array.create (int range) -1 else [||]
#if FABLE_COMPILER
            // Under JavaScript the open slot table (Phase 326), over the packed key and over
            // `codeRow`'s hash and equality.
            let packed = OpenSlots<int>((fun key -> key), (fun a b -> a = b))

            let wide =
                OpenSlots<int[]>((fun r -> codeRow.GetHashCode r), (fun a b -> codeRow.Equals(a, b)))
#else
            let packed = System.Collections.Generic.Dictionary<int, int>()
            let wide = System.Collections.Generic.Dictionary<int[], int>(codeRow)
#endif
            let probe: int[] = Array.zeroCreate k
            let mutable count = 0

            let present (codes: int[][]) (i: int) : bool =
                let mutable ok = true
                let mutable j = 0

                while ok && j < k do
                    ok <- Raw.at i (Raw.at j codes) >= 0
                    j <- j + 1

                ok

            let keyOf (codes: int[][]) (i: int) : int =
                let mutable key = 0

                for j in 0 .. k - 1 do
                    key <- key * max (Raw.get j cards) 1 + Raw.at i (Raw.at j codes)

                key

            /// The number of slots opened.
            member _.Count = count

            /// Row `i`'s slot, opening the next when the tuple is new; `-1` for a row with no code.
            member _.Open(codes: int[][], i: int) : int =
                if not (present codes i) then
                    -1
                elif direct then
                    let key = keyOf codes i
                    let s = Raw.at key table

                    if s >= 0 then
                        s
                    else
                        let fresh = count
                        Raw.put table key fresh
                        count <- fresh + 1
                        fresh
                elif packs then
                    let key = keyOf codes i

#if FABLE_COMPILER
                    let s = packed.Find key

                    if s >= 0 then
                        s
                    else
                        let fresh = count
                        packed.Add(key, fresh)
                        count <- fresh + 1
                        fresh
#else
                    match packed.TryGetValue key with
                    | true, s -> s
                    | _ ->
                        let fresh = count
                        packed[key] <- fresh
                        count <- fresh + 1
                        fresh
#endif
                else
                    for j in 0 .. k - 1 do
                        Raw.set probe j (Raw.at i (Raw.at j codes))

#if FABLE_COMPILER
                    let s = wide.Find probe

                    if s >= 0 then
                        s
                    else
                        let fresh = count
                        wide.Add(Array.copy probe, fresh)
                        count <- fresh + 1
                        fresh
#else
                    match wide.TryGetValue probe with
                    | true, s -> s
                    | _ ->
                        let fresh = count
                        wide[Array.copy probe] <- fresh
                        count <- fresh + 1
                        fresh
#endif

            /// Row `i`'s slot if `Open` has met its tuple, else `-1`. Opens nothing.
            member _.Find(codes: int[][], i: int) : int =
                if not (present codes i) then
                    -1
                elif direct then
                    Raw.at (keyOf codes i) table
                elif packs then
#if FABLE_COMPILER
                    packed.Find(keyOf codes i)
#else
                    match packed.TryGetValue(keyOf codes i) with
                    | true, s -> s
                    | _ -> -1
#endif
                else
                    for j in 0 .. k - 1 do
                        Raw.set probe j (Raw.at i (Raw.at j codes))

#if FABLE_COMPILER
                    wide.Find probe
#else
                    match wide.TryGetValue probe with
                    | true, s -> s
                    | _ -> -1
#endif

        /// The TOKEN slot of every logical row under `keyVecs` (read at `phys`), numbered from 0 in
        /// first-appearance order, and each slot's first logical row. No key is one slot holding
        /// every row (none over no rows).
        let slots (keyVecs: Vec[]) (phys: int[]) : int[] * ResizeArray<int> =
            let n = phys.Length
            let first = ResizeArray<int>()

            match keyVecs with
            // One key: a fresh coder numbers its values in first-appearance order already, so the
            // codes ARE the slots.
            | [| v |] ->
                let c = coder false
                let codes = codesOf c v phys true

                // Each slot's first row, scanning only until the last slot has been met: a key of
                // five values reads a handful of rows, not every one.
                let mutable seen = 0
                let mutable i = 0

                while seen < c.Count && i < n do
                    if Raw.get i codes = seen then
                        first.Add i
                        seen <- seen + 1

                    i <- i + 1

                codes, first
            | _ ->
                let coders = keyVecs |> Array.map (fun _ -> coder false)

                let codes =
                    Array.init keyVecs.Length (fun j -> codesOf coders[j] keyVecs[j] phys true)

                let index = Index(coders |> Array.map (fun c -> c.Count), n)
                let slotOf: int[] = Array.zeroCreate n

                for i in 0 .. n - 1 do
                    let s = index.Open(codes, i)
                    Raw.set slotOf i s

                    if s = first.Count then
                        first.Add i

                slotOf, first

        /// Two sides keyed alike: one coder per key column, the BUILD side's rows opened (in its
        /// order) and the PROBE side's looked up, so a probe row's slot is the build slot its tuple
        /// equals, or `-1`. The coders see the build side first and only it opens codes, so every
        /// probe code is one the build side holds or `-1`.
        let twoSided
            (cellEqOf: int -> bool)
            (buildVecs: Vec[])
            (buildPhys: int[])
            (probeVecs: Vec[])
            (probePhys: int[])
            : int[] * int[] * int =
            let k = buildVecs.Length
            let coders = Array.init k (fun j -> coder (cellEqOf j))

            let buildCodes =
                Array.init k (fun j -> codesOf coders[j] buildVecs[j] buildPhys true)

            let probeCodes =
                Array.init k (fun j -> codesOf coders[j] probeVecs[j] probePhys false)

            let index = Index(coders |> Array.map (fun c -> c.Count), buildPhys.Length)
            let buildSlots = Array.init buildPhys.Length (fun i -> index.Open(buildCodes, i))
            let probeSlots = Array.init probePhys.Length (fun i -> index.Find(probeCodes, i))
            buildSlots, probeSlots, index.Count

    /// The slot of every LOGICAL row under the key vectors `keyVecs` (read at `phys`), and each
    /// slot's key cells — slots numbered from 0 in first-appearance order, keys equal exactly when
    /// their cells are token-equal (`CellKey`'s relation, through the typed row hasher `RowHash`).
    /// The grouping `GroupBy` records (Phase 323) and the partition a `Window` computes over
    /// (Phase 324): one definition, so a window's partitions are the groups a `GroupBy` over the
    /// same keys forms. A slot's key cells are its first row's.
    let private keySlots (keyVecs: Vec[]) (phys: int[]) : int[] * ResizeArray<Cell[]> =
        let slotOf, first = RowHash.slots keyVecs phys
        let groupKeys = ResizeArray<Cell[]>(first.Count)

        for i in first do
            let p = phys[i]
            groupKeys.Add(keyVecs |> Array.map (fun v -> Vec.cellAt v p))

        slotOf, groupKeys

    /// One column of a group's members — physical rows, in member order — as the cell list an
    /// aggregate reads: built from the back, so it is one pass and one cons per member, and read
    /// straight from the column's vector (Phase 267), so only the aggregated column is boxed.
    let private columnOf (v: Vec) (members: ResizeArray<int>) : Cell list =
        let mutable acc = []

        for j in members.Count - 1 .. -1 .. 0 do
            acc <- Vec.cellAt v members[j] :: acc

        acc

    let private evalGroupBy (f: Frame) (keys: string list) (aggs: Agg list) : Result<Frame, EvalError> =
        let keyIdx = keys |> List.map (fun k -> colIndex f.Cols k, k)

        match keyIdx |> List.tryPick (fun (i, k) -> if Option.isNone i then Some k else None) with
        | Some missing -> Error(UnknownColumn(missing, available f.Cols))
        | None ->
            let idxs = keyIdx |> List.map (fun (i, _) -> Option.get i) |> List.toArray

            // Group, preserving first-appearance order of keys, by TOKEN equality (`CellKey`; Phase 41's
            // canonical token, so float keys group host-identically), carrying the first row's key
            // cells for the output.
            //
            // Phase 265 — a hash table from the key cells to a slot, and one growable member list per
            // slot, all local to this step. The persistent map it replaces was keyed on the token LIST,
            // so every row minted two strings per key cell and every insertion copied a tree path
            // compared by walking string lists. Slots open in first-appearance order and members are
            // appended in frame order, so the groups and their order are the ones the map produced.
            //
            // Phase 267 — a group's members are PHYSICAL ROWS of the frame, read through the
            // selection in logical order, and only the key cells are boxed here: the aggregates
            // below read their one column each from its vector, so a row's other columns are
            // never gathered.
            //
            // Phase 323 — what the grouping records per row is its SLOT (`slotOf`, by logical row),
            // not a member list per group: the streamed aggregates below read the rows once, in
            // logical order, into per-slot accumulators. A group's member list is built only if an
            // aggregate defers to `Column.aggregate`, and then for every group at once, from
            // `slotOf`, in the same member order.
            let keyVecs = idxs |> Array.map (fun ci -> f.Vecs[ci])
            let phys = Frame.physical f
            let slotOf, groupKeys = keySlots keyVecs phys

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

                // Phase 323 — every aggregate streamed over the rows once, then one output column per
                // aggregate filled slot by slot. The first error — in group order, then aggregate
                // order, exactly as a traverse over groups of a traverse over aggregates reports it —
                // stops the loop; only a deferred group-aggregate can fail, and it fails exactly as
                // `Column.aggregate` over its members does.
                let aggArr = List.toArray resolvedAggs
                let groups = groupKeys.Count

                let streams =
                    aggArr
                    |> Array.map (fun (a, ty, ci) ->
                        let s = GroupAgg.Stream(a.Fn, ty, f.Vecs[ci], groups, GroupAgg.Exact)
                        s.FeedAll(slotOf, phys)
                        s)

                let outs =
                    aggArr |> Array.map (fun (a, ty, _) -> GroupAgg.Output(aggType a.Fn ty, groups))

                // The member lists, for a deferred group-aggregate: built once, for every group, on
                // the first deferral, physical rows in logical order — the lists the step kept for
                // every group before this phase.
                let mutable members: ResizeArray<int>[] = null

                let membersOf (g: int) : ResizeArray<int> =
                    if isNull members then
                        members <- Array.init groups (fun _ -> ResizeArray<int>())

                        for i in 0 .. phys.Length - 1 do
                            members[slotOf[i]].Add phys[i]

                    members[g]

                let mutable failed = None
                let mutable g = 0

                while Option.isNone failed && g < groups do
                    let mutable j = 0

                    while Option.isNone failed && j < aggArr.Length do
                        if not (streams[j].Emit(g, outs[j])) then
                            let a, ty, ci = aggArr[j]

                            match aggCells a.Fn ty (columnOf f.Vecs[ci] (membersOf g)) with
                            | Ok c -> outs[j].Cell(g, c)
                            | Error e -> failed <- Some e

                        j <- j + 1

                    g <- g + 1

                match failed with
                | Some e -> Error e
                | None ->
                    let keyOut =
                        keyCols
                        |> List.mapi (fun j (_, ty) -> Vec.pack ty (Array.init groups (fun g -> groupKeys[g][j])))

                    let cols = keyCols @ aggCols

                    Ok
                        { Cols = cols
                          Vecs = Array.append (List.toArray keyOut) (outs |> Array.map (fun o -> o.ToVec()))
                          Origins = Array.create (List.length cols) None
                          Sel = None
                          Count = groups })

    /// One sort key's ordering over two PHYSICAL rows of its vector — the pinned ordering
    /// (`compareResolved`) read from the carrier: nulls last regardless of direction, the numeric
    /// family in the float carrier, strings, dates and timestamps ordinal, and a boxed vector
    /// through `compareCells` with an incomparable pair ordered equal.
    let private keyComparer (v: Vec) (dir: SortDir) : int -> int -> int =
        // The direction read once, not per comparison: under JavaScript a union's `=` is a
        // structural comparison call (Phase 326).
        let asc = (dir = Asc)
        let signed (c: int) : int = if asc then c else -c

        let withNulls (mask: bool[]) (cmp: int -> int -> int) : int -> int -> int =
            fun p q ->
                match Raw.at p mask, Raw.at q mask with
                | true, true -> signed (cmp p q)
                | true, false -> -1 // null sorts last
                | false, true -> 1
                | false, false -> 0

        match v with
        | Ints(a, m) -> withNulls m (fun p q -> compare (Raw.at p a) (Raw.at q a))
        | Floats(a, m) -> withNulls m (fun p q -> compareNum (Raw.at p a) (Raw.at q a))
        | Bools(a, m) -> withNulls m (fun p q -> compare (Raw.at p a) (Raw.at q a))
        | Strs(_, a, m) -> withNulls m (fun p q -> System.String.CompareOrdinal(Raw.at p a, Raw.at q a))
        // Every value of one decimal vector is an exact integer at the column's one scale (Phase 280).
        | Decs(a, _, _, m) -> withNulls m (fun p q -> compare (Raw.at p a) (Raw.at q a))
        | Cells cells ->
            fun p q ->
                let a = Raw.at p cells
                let b = Raw.at q cells

                match Cell.isNull a, Cell.isNull b with
                | true, true -> 0
                | true, false -> 1
                | false, true -> -1
                | false, false ->
                    match compareCells a b with
                    | Some c -> signed c
                    | None -> 0

    /// Sort keys as ORDER CODES (Phase 324): each key read ONCE into an int per logical row, so a
    /// sort compares ints rather than calling a comparator per key per comparison.
    ///
    /// A key's codes ascend in the key's pinned order (`keyComparer`: its direction applied, nulls
    /// last regardless of direction), and two rows share a code EXACTLY when that comparator calls
    /// them equal:
    ///
    ///   * an `int` key is its value offset from the least present value — or from the greatest,
    ///     descending — where the values span a range a few times the row count; past that, and for
    ///     every `string`, `float` and `decimal` key, a DENSE RANK of the distinct present values,
    ///     computed once per sort through the key's existing total order (ordinal strings,
    ///     `Kernels.compareFloat` for floats: `-0.0` with `0.0`, `NaN` above every value; the exact
    ///     scaled integers of a decimal vector);
    ///   * a `bool` key is 0 / 1;
    ///   * a `Null` takes the code past every present one.
    ///
    /// A boxed key vector (`Cells`) is never coded: its comparator orders an incomparable pair equal,
    /// which is not a total order, so ranks cannot stand in for it, and its callers keep the
    /// comparator path.
    ///
    /// The tie-break is the LOGICAL POSITION, which is what makes a sort under the codes the stable
    /// sort of the frame order: a stable sort is exactly a sort by (keys, position). Where the
    /// product of the keys' ranges and the row count stays below 2^53, every row's (codes, position)
    /// is PACKED into one number exactly — the codes as mixed-radix digits, the position as the last —
    /// and the sort is a sort of plain numbers with no comparator. The packing is carried in a float
    /// rather than an `int64` because a float is exact to 2^53 on both hosts, where an `int64` is a
    /// big integer under Fable; past the bound the codes are compared as a tuple.
    module internal Ordering =

        /// What the ordering laws perturb to show they can fail (Phase 324). The evaluator runs `Exact`.
        type Perturbation =
            /// The stable sort's tie-break: the logical position, ascending.
            | Exact
            /// Ties broken by DESCENDING position — ordered correctly on every key, and different from
            /// the stable sort on the first duplicate key.
            | TieBreakReversed

        /// One key's codes, one per logical row, and how many distinct codes it may hold.
        [<Struct>]
        type KeyCodes = { Codes: int[]; Range: int }

        /// 2^53: every integer below it is exact in a float, on both hosts.
        let private exactBound: float = 9007199254740992.0

        /// Codes from per-row distinct-value ids (`-1` a null, `-2` a `NaN`): the ids ranked by
        /// sorting the distinct values once, a `NaN` above every value, the direction applied, a
        /// null last.
        let private ofIds
            (ids: int[])
            (distinct: 'T[])
            (cmp: 'T -> 'T -> int)
            (anyNaN: bool)
            (dir: SortDir)
            : KeyCodes =
            let d = distinct.Length
            // The distinct values' ids sorted by value: the id at index `r` has rank `r` (the values
            // are distinct, so no two compare equal). A plain list, not an `int[]`: under Fable a
            // typed array's sort with a comparator is the slow path in the JavaScript engines.
            let byValue = ResizeArray<int>(d)

            for j in 0 .. d - 1 do
                byValue.Add j

            byValue.Sort(System.Comparison(fun a b -> cmp (Raw.at a distinct) (Raw.at b distinct)))
            let rankOf: int[] = Array.zeroCreate d

            for r in 0 .. d - 1 do
                Raw.put rankOf byValue[r] r

            let nanShift = if anyNaN then 1 else 0
            let nullCode = d + nanShift
            let codes: int[] = Array.zeroCreate ids.Length
            // The direction read once, not per row: under JavaScript a union's `=` is a structural
            // comparison call (Phase 326).
            let asc = (dir = Asc)

            for i in 0 .. ids.Length - 1 do
                let id = Raw.get i ids

                let code =
                    if id = -1 then nullCode
                    elif id = -2 then (if asc then d else 0)
                    elif asc then Raw.at id rankOf
                    else nanShift + (d - 1 - Raw.at id rankOf)

                Raw.set codes i code

            { Codes = codes; Range = nullCode + 1 }

        /// A float carrier's codes: dense ranks under `Kernels.compareFloat` (`-0.0` keyed with
        /// `0.0`, every `NaN` one value above the rest).
        let private ofFloats (vals: float[]) (mask: bool[]) (phys: int[]) (dir: SortDir) : KeyCodes =
#if FABLE_COMPILER
            // Under JavaScript the open slot table (Phase 326).
            let index = OpenSlots<float>((fun f -> hash f), (fun a b -> a = b))
#else
            let index = System.Collections.Generic.Dictionary<float, int>()
#endif
            let distinct = ResizeArray<float>()
            let ids: int[] = Array.zeroCreate phys.Length
            let mutable anyNaN = false

            for i in 0 .. phys.Length - 1 do
                let p = Raw.get i phys

                if not (Raw.at p mask) then
                    Raw.set ids i -1
                else
                    let x = Raw.at p vals

                    if System.Double.IsNaN x then
                        Raw.set ids i -2
                        anyNaN <- true
                    else
                        let k = if x = 0.0 then 0.0 else x

#if FABLE_COMPILER
                        let found = index.Find k

                        if found >= 0 then
                            Raw.set ids i found
                        else
                            let id = distinct.Count
                            index.Add(k, id)
                            distinct.Add k
                            Raw.set ids i id
#else
                        match index.TryGetValue k with
                        | true, id -> Raw.set ids i id
                        | _ ->
                            let id = distinct.Count
                            index[k] <- id
                            distinct.Add k
                            Raw.set ids i id
#endif

            ofIds ids (distinct.ToArray()) (fun (a: float) b -> compare a b) anyNaN dir

        /// A string carrier's codes: dense ranks under the ordinal order.
        let private ofStrings (vals: string[]) (mask: bool[]) (phys: int[]) (dir: SortDir) : KeyCodes =
#if FABLE_COMPILER
            // Under JavaScript the open slot table (Phase 326).
            let index =
                OpenSlots<string>((fun s -> hash s), (fun a b -> System.String.Equals(a, b)))
#else
            let index = System.Collections.Generic.Dictionary<string, int>()
#endif
            let distinct = ResizeArray<string>()
            let ids: int[] = Array.zeroCreate phys.Length

            for i in 0 .. phys.Length - 1 do
                let p = Raw.get i phys

                if not (Raw.at p mask) then
                    Raw.set ids i -1
                else
                    let x = Raw.at p vals

#if FABLE_COMPILER
                    let found = index.Find x

                    if found >= 0 then
                        Raw.set ids i found
                    else
                        let id = distinct.Count
                        index.Add(x, id)
                        distinct.Add x
                        Raw.set ids i id
#else
                    match index.TryGetValue x with
                    | true, id -> Raw.set ids i id
                    | _ ->
                        let id = distinct.Count
                        index[x] <- id
                        distinct.Add x
                        Raw.set ids i id
#endif

            ofIds ids (distinct.ToArray()) (fun (a: string) b -> System.String.CompareOrdinal(a, b)) false dir

        /// An int carrier's codes: the value offset from the least present one (ascending) or the
        /// greatest (descending), where the values span at most a few times the row count; past
        /// that, dense ranks of the values in the float carrier, where every int is exact.
        let private ofInts (vals: int[]) (mask: bool[]) (phys: int[]) (dir: SortDir) : KeyCodes =
            let n = phys.Length
            let mutable lo = System.Int32.MaxValue
            let mutable hi = System.Int32.MinValue
            let mutable any = false

            for i in 0 .. n - 1 do
                let p = Raw.get i phys

                if Raw.at p mask then
                    any <- true
                    let v = Raw.at p vals

                    if v < lo then
                        lo <- v

                    if v > hi then
                        hi <- v

            if not any then
                { Codes = Array.zeroCreate n
                  Range = 1 }
            else
                let span = float hi - float lo

                if span <= 4.0 * float n + 16.0 then
                    let nullCode = int span + 1
                    let codes: int[] = Array.zeroCreate n
                    let asc = (dir = Asc)

                    for i in 0 .. n - 1 do
                        let p = Raw.get i phys

                        let code =
                            if not (Raw.at p mask) then nullCode
                            elif asc then Raw.at p vals - lo
                            else hi - Raw.at p vals

                        Raw.set codes i code

                    { Codes = codes; Range = nullCode + 1 }
                else
                    let asFloat: float[] = Array.zeroCreate vals.Length

                    for i in 0 .. n - 1 do
                        let p = Raw.get i phys
                        Raw.put asFloat p (float (Raw.at p vals))

                    ofFloats asFloat mask phys dir

        /// One key's codes over the logical rows `phys` reads, or `ValueNone` for a boxed vector.
        let codesOf (v: Vec) (dir: SortDir) (phys: int[]) : KeyCodes voption =
            match v with
            | Ints(a, m) -> ValueSome(ofInts a m phys dir)
            | Floats(a, m) -> ValueSome(ofFloats a m phys dir)
            // Every value of one decimal vector is an exact integer at the column's one scale.
            | Decs(a, _, _, m) -> ValueSome(ofFloats a m phys dir)
            | Strs(_, a, m) -> ValueSome(ofStrings a m phys dir)
            | Bools(a, m) ->
                let asc = (dir = Asc)

                let codes =
                    phys
                    |> Array.map (fun p ->
                        if not (Raw.at p m) then 2
                        elif Raw.at p a = asc then 1
                        else 0)

                ValueSome { Codes = codes; Range = 3 }
            | Cells _ -> ValueNone

        /// Every key's codes, in key order, or `ValueNone` when any key's vector is boxed.
        let codesAll (keys: (Vec * SortDir)[]) (phys: int[]) : KeyCodes[] voption =
            let out: KeyCodes[] = Array.zeroCreate keys.Length
            let mutable ok = true
            let mutable k = 0

            while ok && k < keys.Length do
                let v, dir = keys[k]

                match codesOf v dir phys with
                | ValueSome c -> out[k] <- c
                | ValueNone -> ok <- false

                k <- k + 1

            if ok then ValueSome out else ValueNone

        /// Do logical rows `a` and `b` share every key's code — are they tied under the keys?
        let sameCodes (keys: KeyCodes[]) (a: int) (b: int) : bool =
            let mutable same = true
            let mutable k = 0

            while same && k < keys.Length do
                same <- Raw.at a keys[k].Codes = Raw.at b keys[k].Codes
                k <- k + 1

            same

        /// A total order over the logical rows `0 .. N-1`: a LEAD code first when there is one (a
        /// window's partition slot), then the keys' codes in key order, then the position.
        /// `Packed` holds each row's whole sort key as one exact number where the ranges allow,
        /// and is `null` where they do not.
        type Order =
            { N: int
              Lead: int[]
              Keys: KeyCodes[]
              Packed: float[]
              Perturbation: Perturbation }

        /// The position term of the order: the position, or its reverse under the perturbation.
        let private tie (o: Order) (i: int) : int =
            match o.Perturbation with
            | Exact -> i
            | TieBreakReversed -> o.N - 1 - i

        /// The order over `n` rows under `lead` (`null` for none; `leadRange` codes) and `keys`.
        let build (perturbation: Perturbation) (lead: int[]) (leadRange: int) (keys: KeyCodes[]) (n: int) : Order =
            let mutable span = if isNull lead then 1.0 else float leadRange

            for k in keys do
                span <- span * float k.Range

            let o =
                { N = n
                  Lead = lead
                  Keys = keys
                  Packed = null
                  Perturbation = perturbation }

            if span * float n > exactBound then
                o
            else
                let packed: float[] = Array.zeroCreate n
                let fn = float n

                for i in 0 .. n - 1 do
                    let mutable c = if isNull lead then 0.0 else float (Raw.at i lead)

                    for k in keys do
                        c <- c * float k.Range + float (Raw.at i k.Codes)

                    Raw.set packed i (c * fn + float (tie o i))

                { o with Packed = packed }

        /// Compare logical rows `a` and `b` under the order; never 0 for two different rows.
        let compareRows (o: Order) (a: int) (b: int) : int =
            if not (isNull o.Packed) then
                compare (Raw.at a o.Packed) (Raw.at b o.Packed)
            else
                let mutable c =
                    if isNull o.Lead then
                        0
                    else
                        compare (Raw.at a o.Lead) (Raw.at b o.Lead)

                let mutable k = 0

                while c = 0 && k < o.Keys.Length do
                    let codes = o.Keys[k].Codes
                    c <- compare (Raw.at a codes) (Raw.at b codes)
                    k <- k + 1

                if c <> 0 then c else compare (tie o a) (tie o b)

        /// The logical rows in the order's sequence.
        let permutation (o: Order) : int[] =
            let n = o.N

            if not (isNull o.Packed) then
                let sorted = Array.copy o.Packed
                // Rows already in key order — a window over a sequence column, a sort of sorted
                // input — are the common case, and one pass recognises them without a sort.
                let mutable ascending = true
                let mutable i = 1

                Raw.within n sorted

                while ascending && i < n do
                    ascending <- Raw.get (i - 1) sorted < Raw.get i sorted
                    i <- i + 1

                if not ascending then
                    // The packed keys are finite and non-negative, so the engine's own numeric sort orders
                    // them exactly as `Array.sortInPlace` does (Phase 326: under JavaScript that is a
                    // comparator sort).
                    Raw.sortFinite sorted

                let fn = float n
                // The position is the packed key's last digit, and `tie` is its own inverse.
                sorted |> Array.map (fun k -> tie o (int (k % fn)))
            else
                // A plain list of positions, not an `int[]`: under Fable a typed array's sort with
                // a comparator is the slow path in the JavaScript engines.
                let order = ResizeArray<int>(n)

                for i in 0 .. n - 1 do
                    order.Add i

                order.Sort(System.Comparison(compareRows o))
                order.ToArray()

    /// A sort's keys over the logical rows `phys` reads, as the comparator over LOGICAL positions the
    /// pinned order is (`keyComparer`, key by key), ties broken by position — the path a boxed key
    /// vector keeps, since its comparator is not a total order and cannot be coded, and the top-n's
    /// order (Phase 324, measured there).
    let private comparatorOrder (phys: int[]) (keys: (Vec * SortDir)[]) : int -> int -> int =
        let cmps = keys |> Array.map (fun (v, dir) -> keyComparer v dir)

        fun a b ->
            let pa = Raw.at a phys
            let pb = Raw.at b phys
            let mutable c = 0
            let mutable k = 0

            while c = 0 && k < cmps.Length do
                let cmp = cmps[k]
                c <- cmp pa pb
                k <- k + 1

            if c <> 0 then c else compare a b

    /// A sort's resolved keys with their vectors, in key order.
    let private sortKeyVecs (f: Frame) (by: (string * SortDir) list) : (Vec * SortDir)[] =
        resolveSortKeys f.Cols by
        |> List.map (fun (ci, dir) -> f.Vecs[ci], dir)
        |> List.toArray

    let private evalSort (f: Frame) (by: (string * SortDir) list) : Frame =
        // A permutation of the selection (Phase 267): the logical positions sorted under the keys,
        // ties broken by position — which is exactly the stable sort over the frame order the
        // reference's `List.sortWith` is, stated as a total order so the algorithm cannot matter.
        // Phase 324: the keys as ORDER CODES, packed with the position into one number per row
        // where the ranges allow (`Ordering`); a boxed key keeps the comparator.
        let phys = Frame.physical f
        let keys = sortKeyVecs f by

        match Ordering.codesAll keys phys with
        | ValueSome codes ->
            let perm =
                Ordering.permutation (Ordering.build Ordering.Exact null 0 codes phys.Length)

            Frame.select f (perm |> Array.map (fun i -> phys[i]))
        | ValueNone ->
            // A plain list of positions rather than an `int[]`: under Fable an `int[]` is a typed
            // array, and a typed array's sort with a comparator is the slow path in the JavaScript
            // engines.
            let order = ResizeArray<int>(phys.Length)

            for i in 0 .. phys.Length - 1 do
                order.Add i

            order.Sort(System.Comparison(comparatorOrder phys keys))
            Frame.select f (Array.init order.Count (fun i -> phys[order[i]]))

    let private evalDistinct (f: Frame) : Frame =
        // Dedup by TOKEN equality over the whole row (`CellKey`; Phase 41's canonical token, so
        // float-bearing rows dedup host-identically), keeping each row's first appearance. Phase 265: a
        // hash set local to the step, keyed on the row array itself, replaces a persistent set of token
        // lists — no string is minted. The rows kept become the selection.
        //
        // Phase 325: the rows are keyed through the typed row hasher over every column (`RowHash`,
        // the token relation), so no row is gathered and no cell boxed; a row is kept when it opens
        // its slot.
        let phys = Frame.physical f
        let _, first = RowHash.slots f.Vecs phys
        Frame.select f (Array.init first.Count (fun s -> phys[first[s]]))

    let private evalLimit (f: Frame) (n: int) (offset: int) : Frame =
        let phys = Frame.physical f
        let len = phys.Length
        let skipped = min (max 0 offset) len
        let taken = min (max 0 n) (len - skipped)
        Frame.select f (Array.sub phys skipped taken)

    /// The stable top-n (Phase 269) — `evalLimit (evalSort f by) n offset` without sorting the
    /// rows the limit discards. The order is the same TOTAL order `evalSort` sorts under (the keys,
    /// then the logical position as the tie-break), so the `offset + n` least positions under it,
    /// sorted, are exactly the first `offset + n` of the full sort, whatever algorithm finds them:
    /// a bounded heap of that size takes one pass over the rows and a sort of the heap. A window
    /// that reaches the end of the frame is the full sort, which is then the cheaper of the two.
    let private evalTopN (f: Frame) (by: (string * SortDir) list) (n: int) (offset: int) : Frame =
        let phys = Frame.physical f
        let len = phys.Length
        let skipped = min (max 0 offset) len
        let taken = min (max 0 n) (len - skipped)
        let window = skipped + taken

        if window = 0 then
            Frame.select f [||]
        elif window >= len then
            evalLimit (evalSort f by) n offset
        else
            // The total order over LOGICAL positions `evalSort` sorts under, as the comparator.
            //
            // Phase 324 measured the top-n under ORDER CODES and kept the comparator: the heap
            // compares most rows once, against its root, so coding a key costs a pass over every
            // row (and, for a string, float or decimal key, a sort of its distinct values) to save
            // about one comparison per row. Over 100,000 rows with a limit of 10, an int key read
            // 12.0 to 12.6 ms coded against 12.0 to 12.3 ms compared, allocating 1.2 MB more; a
            // string key costs its ranking outright. The order is the same total order either way
            // (`Ordering`'s codes tie exactly where this comparator does), so a top-n that would
            // gain from codes — a window reaching most of the frame is the full sort already —
            // can take them without a change of answer.
            let cmp: int -> int -> int = comparatorOrder phys (sortKeyVecs f by)

            // A max-heap of the `window` least positions seen so far: its root is the greatest of
            // them, and a position that sorts before the root replaces it.
            let heap: int[] = Array.zeroCreate window

            let siftDown (start: int) =
                let mutable i = start
                let mutable go = true

                while go do
                    let l = 2 * i + 1
                    let r = l + 1
                    let mutable largest = i

                    if l < window && cmp heap[l] heap[largest] > 0 then
                        largest <- l

                    if r < window && cmp heap[r] heap[largest] > 0 then
                        largest <- r

                    if largest = i then
                        go <- false
                    else
                        let t = heap[i]
                        heap[i] <- heap[largest]
                        heap[largest] <- t
                        i <- largest

            let siftUp (start: int) =
                let mutable i = start

                while i > 0 && cmp heap[i] heap[(i - 1) / 2] > 0 do
                    let p = (i - 1) / 2
                    let t = heap[i]
                    heap[i] <- heap[p]
                    heap[p] <- t
                    i <- p

            for i in 0 .. window - 1 do
                heap[i] <- i
                siftUp i

            for i in window .. len - 1 do
                if cmp i heap[0] < 0 then
                    heap[0] <- i
                    siftDown 0

            // A plain list for the final sort, as `evalSort` uses: a typed array's comparator sort
            // is the slow path under Fable.
            let order = ResizeArray<int>(window)

            for i in 0 .. window - 1 do
                order.Add heap[i]

            order.Sort(System.Comparison cmp)
            Frame.select f (Array.init taken (fun j -> phys[order[skipped + j]]))

    /// The join's key-column resolution: the left and right indices `on` names, or the FIRST
    /// unresolvable name in the order this evaluator reports it (left names, then right names).
    /// Exposed downstream as `joinKeyIndices` (Phase 120) — one implementation, two callers.
    let private joinKeyIdx
        (leftCols: Schema)
        (rightCols: Schema)
        (on: (string * string) list)
        : Result<int list * int list, EvalError> =
        let leftIdx = on |> List.map (fun (l, _) -> colIndex leftCols l, l)
        let rightIdx = on |> List.map (fun (_, r) -> colIndex rightCols r, r)

        match
            (leftIdx @ rightIdx)
            |> List.tryPick (fun (i, n) -> if Option.isNone i then Some n else None)
        with
        | Some n -> Error(UnknownColumn(n, available leftCols @ available rightCols))
        | None -> Ok(leftIdx |> List.map (fst >> Option.get), rightIdx |> List.map (fst >> Option.get))

    /// The join's key predicate over two rows' already-projected key cells: `cellEq` pairwise, so a
    /// `Null` key matches nothing, itself included. Exposed downstream as `joinKeysMatch`.
    let private joinKeyEq (leftKeys: Cell list) (rightKeys: Cell list) : bool =
        List.length leftKeys = List.length rightKeys
        && List.forall2 cellEq leftKeys rightKeys

    /// The join (Phase 325 — typed; Phase 264 made it a hash join). The right side arrives as a
    /// frame, unpacked typed at the boundary. Its key columns are coded through the typed row
    /// hasher in the `cellEq` relation (`RowHash`; an int key column against an int key column in
    /// the token relation, which is `cellEq` over present ints), each right row whose key holds no
    /// `Null` is opened in right order, and the right rows of each key slot are listed in arrival
    /// order. Each left row then FINDS its slot — in left order, emitting that row's matches in
    /// right arrival order, which is the order the nested loop the hash join replaced produced.
    /// The answer is a pair of index vectors (left row, right row; `-1` for the side a combining
    /// join pads with nulls), and every output column is GATHERED through them from its typed
    /// vector: no row is assembled and no cell boxed on a typed column.
    let private evalJoin
        (f: Frame)
        (right: Frame)
        (on: (string * string) list)
        (how: JoinKind)
        : Result<Frame, EvalError> =
        let rightCols = right.Cols

        match joinKeyIdx f.Cols rightCols on with
        | Error e -> Error e
        | Ok(li, ri) ->
            let lphys = Frame.physical f
            let rphys = Frame.physical right
            let lkeys = li |> List.map (fun i -> f.Vecs[i]) |> List.toArray
            let rkeys = ri |> List.map (fun i -> right.Vecs[i]) |> List.toArray

            // An int key against an int key is coded in the token relation (no float carrier); every
            // other pair in `cellEq`'s.
            let intPair (j: int) =
                match lkeys[j], rkeys[j] with
                | Ints _, Ints _ -> true
                | _ -> false

            // The token relation codes a null; a join key matches none, so a null int key is struck
            // out (the `cellEq` coder gives a null no code of its own).
            let strikeNulls (j: int) (v: Vec) (phys: int[]) (codes: int[]) =
                match v with
                | Ints(_, m) when intPair j ->
                    for i in 0 .. codes.Length - 1 do
                        if not m[phys[i]] then
                            codes[i] <- -1
                | _ -> ()

                codes

            // The right side BUILDS (its rows opened in right order), the left side PROBES: a right
            // row whose key holds a `Null` is never indexed, a left row with one finds nothing.
            let coders = Array.init lkeys.Length (fun j -> RowHash.coder (not (intPair j)))

            let rcodes =
                Array.init rkeys.Length (fun j ->
                    strikeNulls j rkeys[j] rphys (RowHash.codesOf coders[j] rkeys[j] rphys true))

            let lcodes =
                Array.init lkeys.Length (fun j ->
                    strikeNulls j lkeys[j] lphys (RowHash.codesOf coders[j] lkeys[j] lphys false))

            let index = RowHash.Index(coders |> Array.map (fun c -> c.Count), rphys.Length)
            let rslots = Array.init rphys.Length (fun i -> index.Open(rcodes, i))
            let lslots = Array.init lphys.Length (fun i -> index.Find(lcodes, i))
            let slotCount = index.Count

            // Each slot's right rows, in right order: a count per slot, its start, then a fill.
            let starts: int[] = Array.zeroCreate (slotCount + 1)

            for s in rslots do
                if s >= 0 then
                    starts[s + 1] <- starts[s + 1] + 1

            for s in 0 .. slotCount - 1 do
                starts[s + 1] <- starts[s + 1] + starts[s]

            let members: int[] = Array.zeroCreate starts[slotCount]
            let cursor = Array.copy starts

            for j in 0 .. rslots.Length - 1 do
                let s = rslots[j]

                if s >= 0 then
                    let at = cursor[s]
                    members[at] <- j
                    cursor[s] <- at + 1

            match how with
            // Phase 101 — the filtering joins: the LEFT schema only, each qualifying left row once,
            // input order and multiplicity preserved (no fan-out, no right columns to project away)
            // — a selection over the left frame.
            | Semi
            | Anti ->
                let keep = (how = Semi)
                let kept = ResizeArray<int>()

                for i in 0 .. lphys.Length - 1 do
                    if (lslots[i] >= 0) = keep then
                        kept.Add lphys[i]

                Ok(Frame.select f (kept.ToArray()))
            // The combining joins (Inner / Left / Right / Outer) — left cols ++ right cols.
            | Inner
            | Left
            | Right
            | Outer ->
                // output schema: left cols ++ right cols (collisions suffixed _right)
                let leftNames = available f.Cols |> Set.ofList

                let outRight =
                    rightCols
                    |> List.map (fun (n, ty) -> (if Set.contains n leftNames then n + "_right" else n), ty)

                let padsLeft = (how = Left || how = Outer)
                let padsRight = (how = Right || how = Outer)
                let lout = ResizeArray<int>()
                let rout = ResizeArray<int>()

                // A right row is matched when some left row's probe emitted it — the key relation is
                // symmetric, so that is exactly the reverse scan's "some left row matches it".
                let matched = Array.create rphys.Length false

                for i in 0 .. lphys.Length - 1 do
                    let s = lslots[i]

                    if s >= 0 then
                        for at in starts[s] .. starts[s + 1] - 1 do
                            let j = members[at]
                            matched[j] <- true
                            lout.Add lphys[i]
                            rout.Add rphys[j]
                    elif padsLeft then
                        lout.Add lphys[i]
                        rout.Add -1

                // right-only unmatched rows (for Right / Outer), after every left-side row, in right
                // order — read from the matched flags rather than a second pass the other way.
                if padsRight then
                    for j in 0 .. rphys.Length - 1 do
                        if not matched[j] then
                            lout.Add -1
                            rout.Add rphys[j]

                let lidx = lout.ToArray()
                let ridx = rout.ToArray()

                let vecs =
                    Array.append
                        (f.Vecs |> Array.map (fun v -> Vec.gatherOrNull v lidx))
                        (right.Vecs |> Array.map (fun v -> Vec.gatherOrNull v ridx))

                Ok
                    { Cols = f.Cols @ outRight
                      Vecs = vecs
                      Origins = Array.create vecs.Length None
                      Sel = None
                      Count = lidx.Length }

    let private evalUnion (f: Frame) (other: Frame) : Result<Frame, EvalError> =
        if available f.Cols <> available other.Cols then
            Error(JoinError "union requires matching column names")
        else
            Ok(Frame.concat f other)

    /// `Intersect` / `Except` (Phase 101) — the multiset set-ops, keyed on the SAME canonical row
    /// token `Distinct` dedups on (Phase 41), so membership is host-identical and `Null` is a value
    /// that matches itself. `keepPresent` selects intersect (`true`) from except (`false`). The
    /// left's order and duplicate multiplicity survive, so `· Distinct` recovers the SQL set forms.
    let private evalSetOp (verb: string) (keepPresent: bool) (f: Frame) (other: Frame) : Result<Frame, EvalError> =
        if available f.Cols <> available other.Cols then
            Error(JoinError(verb + " requires matching column names"))
        else
            // TOKEN equality over whole rows (Phase 265), through the typed row hasher (Phase 325):
            // the other side's rows are opened, each left row looks its tuple up, and no row is
            // gathered or cell boxed on either side.
            let phys = Frame.physical f

            let _, found, _ =
                RowHash.twoSided (fun _ -> false) other.Vecs (Frame.physical other) f.Vecs phys

            let kept = ResizeArray<int>()

            for i in 0 .. phys.Length - 1 do
                if (found[i] >= 0) = keepPresent then
                    kept.Add phys[i]

            Ok(Frame.select f (kept.ToArray()))

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

    /// A `Window` step's ordering over the logical rows (Phase 324): every row's partition SLOT (the
    /// typed key slots a `GroupBy` over the partition columns forms), the PERMUTATION that lists the
    /// rows partition by partition, each partition in its order keys' order with ties in frame order,
    /// and the test of whether two rows tie on the order keys. No row is materialised.
    ///
    /// The order keys are ORDER CODES (`Ordering`) with the slot as the leading code; a boxed order
    /// key (a `Cells` vector, whose comparator is not a total order) keeps the reference's algorithm
    /// instead — each partition's rows, in frame order, through the stable `List.sortWith` under the
    /// pinned comparator — so its answer is the one it always was.
    [<NoComparison; NoEquality>]
    type internal WindowOrder =
        {
            /// The partition slot of each logical row.
            Slot: int[]
            /// How many partitions there are.
            Partitions: int
            /// The logical rows, partition by partition, each in its window order.
            Perm: int[]
            /// The order keys' codes by logical row, where every order key is coded; `ValueNone` for
            /// a boxed order key.
            Codes: Ordering.KeyCodes[] voption
            /// Do two logical rows tie on the order keys?
            Same: int -> int -> bool
        }

    /// The window ordering of the logical rows `phys` reads, over the vectors `vecOf` names by
    /// schema index, with the partition and order columns already resolved. `perturbation` is the
    /// laws' (Phase 324); the evaluator passes `Ordering.Exact`.
    let internal windowOrder
        (perturbation: Ordering.Perturbation)
        (vecOf: int -> Vec)
        (phys: int[])
        (partIdx: int[])
        (orderKeys: (int * SortDir) list)
        : WindowOrder =
        let n = phys.Length
        let slotOf, slotKeys = keySlots (partIdx |> Array.map vecOf) phys
        let partitions = slotKeys.Count
        let keyVecs = orderKeys |> List.map (fun (ci, dir) -> vecOf ci, dir) |> List.toArray

        match Ordering.codesAll keyVecs phys with
        | ValueSome codes ->
            let order = Ordering.build perturbation slotOf partitions codes n

            { Slot = slotOf
              Partitions = partitions
              Perm = Ordering.permutation order
              Codes = ValueSome codes
              Same = Ordering.sameCodes codes }
        | ValueNone ->
            let cmps = keyVecs |> Array.map (fun (v, dir) -> keyComparer v dir)

            let cmp (a: int) (b: int) : int =
                let pa = Raw.at a phys
                let pb = Raw.at b phys
                let mutable c = 0
                let mutable k = 0

                while c = 0 && k < cmps.Length do
                    let compareKey = cmps[k]
                    c <- compareKey pa pb
                    k <- k + 1

                c

            // Each partition's rows in frame order, then sorted stably — the reference's algorithm,
            // over positions rather than rows. Under the perturbation the partition is reversed
            // first, so a tie keeps the LATER row first.
            let members = Array.init partitions (fun _ -> ResizeArray<int>())

            for i in 0 .. n - 1 do
                members[slotOf[i]].Add i

            let perm =
                members
                |> Array.collect (fun m ->
                    let rows = List.ofSeq m

                    let rows =
                        match perturbation with
                        | Ordering.Exact -> rows
                        | Ordering.TieBreakReversed -> List.rev rows

                    rows |> List.sortWith cmp |> List.toArray)

            { Slot = slotOf
              Partitions = partitions
              Perm = perm
              Codes = ValueNone
              Same = fun a b -> cmp a b = 0 }

    /// A `Window` step's appended column, one value per LOGICAL row (Phase 324): typed where the
    /// function's output is — the positional and ranking family an `int`, a float running total or
    /// rolling window a `float` (absent where the window held no value) — so the frame form packs
    /// no cell it would then unpack, and boxed (`WCells`) otherwise.
    [<NoComparison; NoEquality>]
    type internal WindowColumn =
        | WInts of int[]
        | WFloats of float[] * present: bool[]
        | WCells of Cell[]

    /// The window column's cell at logical row `i` — what the row-form twins append.
    let internal windowCellAt (w: WindowColumn) (i: int) : Cell =
        match w with
        | WInts a -> Int a[i]
        | WFloats(a, m) -> if m[i] then Float a[i] else Null
        | WCells cells -> cells[i]

    /// The window column as a vector of `count` physical rows, logical row `i` at `phys[i]` — the
    /// vector `Vec.packAt` packs from the same cells.
    let private windowVecAt (ty: ColumnType) (count: int) (phys: int[]) (w: WindowColumn) : Vec =
        match w with
        | WInts a ->
            let vals: int[] = Array.zeroCreate count
            let mask: bool[] = Array.zeroCreate count
            // The loop proves `i` for `a`, and for `phys` once it is checked to be as long (Phase 326);
            // the physical row it writes is an index it does not prove.
            Raw.within a.Length phys

            for i in 0 .. a.Length - 1 do
                let p = Raw.get i phys
                Raw.put vals p (Raw.get i a)
                Raw.put mask p true

            Ints(vals, mask)
        | WFloats(a, m) ->
            let vals: float[] = Array.zeroCreate count
            let mask: bool[] = Array.zeroCreate count
            Raw.within a.Length phys
            Raw.within a.Length m

            for i in 0 .. a.Length - 1 do
                if Raw.get i m then
                    let p = Raw.get i phys
                    Raw.put vals p (Raw.get i a)
                    Raw.put mask p true

            Floats(vals, mask)
        | WCells cells -> Vec.packAt ty count (fun i -> phys[i]) cells

    /// Phase 333 — is the window function's value at a row a FOLD over its partition's prefix in
    /// window order: a function of the rows at or before it alone, carried from one row to the next
    /// by a single running state that the function's own output at a row IS. The row number (a
    /// count), the running total and the running extremes are; the ranks are not (a tie reads the
    /// previous row's order key, which the output does not carry), `NTile` is not (a bucket reads the
    /// partition's LENGTH), `Lead` is not (it reads the row after), and the lag and the rolling pair
    /// read a fixed neighbourhood of preceding values, which the output at the preceding row does not
    /// carry either. A prefix fold is what the incremental seam may recompute from the earliest
    /// changed position of a partition, seeded with the output at the position before it.
    let internal windowIsPrefixFold (fn: WindowFn) : bool =
        match fn with
        | RowNumber
        | CumulSum
        | CumulMax
        | CumulMin -> true
        | Rank
        | DenseRank
        | CompetitionRank
        | NTile _
        | Lag
        | Lead
        | RollingMean
        | RollingSum -> false

    /// Does a running total over `spec.Of` stay EXACT (Phase 277): is the source a decimal column?
    let private windowSourceIsDecimal (cols: Schema) (spec: WindowSpec) : bool = colType cols spec.Of = Some DecimalType

    /// Phase 333 — is `seed` a running state a prefix fold's scan can resume from: the cell the
    /// function's own scan writes at a row, which is the only place a seed may be read from. A row
    /// number is an `Int`, a float running total a `Float`, an exact one a `Decimal`; a running
    /// extreme is whatever cell it carries forward, `Null` (no value yet) included.
    let internal windowSeedFits (cols: Schema) (spec: WindowSpec) (seed: Cell) : bool =
        match spec.Fn, seed with
        | RowNumber, Int _ -> true
        | CumulSum, Decimal _ -> windowSourceIsDecimal cols spec
        | CumulSum, Float _ -> not (windowSourceIsDecimal cols spec)
        | CumulMax, _
        | CumulMin, _ -> true
        | _ -> false

    /// The `Window` step's appended column (Phase 324): its type, and one value per LOGICAL row of
    /// the rows `phys` reads, over the vectors `vecOf` names by schema index. The frame form packs it
    /// back at the physical rows; the public row form (`windowStep`) and the incremental seam's
    /// window step pack the columns they read and place the cell on each row. Each partition is one
    /// run of the window ordering's permutation, scanned in sequence.
    let rec internal windowColumnOf
        (perturbation: Ordering.Perturbation)
        (cols: Schema)
        (vecOf: int -> Vec)
        (phys: int[])
        (spec: WindowSpec)
        : Result<ColumnType * WindowColumn, EvalError> =
        match spec.Fn, colIndex cols spec.Of with
        | NTile b, _ when b < 1 -> Error(TypeError("ntile expects at least 1 bucket, got " + string b))
        | fn, None when windowReadsOf fn -> Error(UnknownColumn(spec.Of, available cols))
        | _ -> windowColumnOver cols vecOf phys spec (windowOrderOf perturbation cols vecOf phys spec) null

    /// The schema indexes of a window's PARTITION columns, a name the schema does not carry dropped
    /// (the window partitions by the columns it can find, as it always has).
    and internal windowPartitionIdx (cols: Schema) (spec: WindowSpec) : int[] =
        spec.PartitionBy |> List.choose (colIndex cols) |> List.toArray

    /// The schema index of the column a window reads, where the schema carries it.
    and internal windowOfIdx (cols: Schema) (spec: WindowSpec) : int option = colIndex cols spec.Of

    /// The window ordering of `spec` over the rows `phys` reads (`windowOrder`), with its partition
    /// and order columns resolved under `cols` — the ordering `windowColumnOf` scans.
    and internal windowOrderOf
        (perturbation: Ordering.Perturbation)
        (cols: Schema)
        (vecOf: int -> Vec)
        (phys: int[])
        (spec: WindowSpec)
        : WindowOrder =
        // The ORDER keys resolved once for the step (Phase 263), not once per comparison.
        windowOrder perturbation vecOf phys (windowPartitionIdx cols spec) (resolveSortKeys cols spec.OrderBy)

    /// The scan of `windowColumnOf` over an ordering already computed (Phase 333): each partition —
    /// a run of `wo.Perm` sharing a `wo.Slot` — scanned in sequence, and every output landing at its
    /// row's logical position. `seeds` is `null` for the whole evaluation; otherwise it holds, per
    /// partition slot, the running state a PREFIX FOLD (`windowIsPrefixFold`) resumes from — the
    /// output the same scan wrote at the row before the run's first row — or `null` for a run that
    /// starts its partition. That is how the incremental seam recomputes a partition's SUFFIX: the
    /// rows it hands in are the suffix alone, and the scan that continues them is this one, so a
    /// resumed run is the full run's continuation by construction rather than by a second
    /// implementation of each fold. A seed is read only by a prefix fold, and must fit
    /// (`windowSeedFits`).
    and internal windowColumnOver
        (cols: Schema)
        (vecOf: int -> Vec)
        (phys: int[])
        (spec: WindowSpec)
        (wo: WindowOrder)
        (seeds: Cell[])
        : Result<ColumnType * WindowColumn, EvalError> =
        match spec.Fn, colIndex cols spec.Of with
        | NTile b, _ when b < 1 -> Error(TypeError("ntile expects at least 1 bucket, got " + string b))
        | fn, None when windowReadsOf fn -> Error(UnknownColumn(spec.Of, available cols))
        | _, ofIdx ->
            let n = phys.Length
            // A running total over a decimal column stays exact (Phase 277).
            let sourceIsDecimal = windowSourceIsDecimal cols spec
            let perm = wo.Perm

            // The running state partition slot `g`'s run resumes from, or `null` where it starts the
            // partition (`windowColumnOver`).
            let seedOf (g: int) : Cell =
                if isNull seeds then Unchecked.defaultof<Cell> else seeds[g]

            // The output sink the function's type calls for; the other two stay `null`.
            let intOut =
                match spec.Fn with
                | RowNumber
                | Rank
                | DenseRank
                | CompetitionRank
                | NTile _ -> true
                | _ -> false

            let floatOut =
                match spec.Fn with
                | RollingMean -> true
                | CumulSum
                | RollingSum -> not sourceIsDecimal
                | _ -> false

            let ints: int[] = if intOut then Array.zeroCreate n else null
            let floats: float[] = if floatOut then Array.zeroCreate n else null
            let present: bool[] = if floatOut then Array.zeroCreate n else null

            let out: Cell[] = if intOut || floatOut then null else Array.create n Null

            let ofVec =
                if windowReadsOf spec.Fn then
                    ofIdx |> Option.map vecOf
                else
                    None

            let valueAt (i: int) : Cell =
                match ofVec with
                | Some v -> Vec.cellAt v (Raw.at i phys)
                | None -> Null

            // The source value in the float carrier, as `asNum` reads it — read from a typed vector
            // without boxing the cell.
            let numAt: int -> float voption =
                match ofVec with
                | Some(Ints(a, m)) ->
                    fun i ->
                        let p = Raw.at i phys

                        if Raw.at p m then
                            ValueSome(float (Raw.at p a))
                        else
                            ValueNone
                | Some(Floats(a, m)) ->
                    fun i ->
                        let p = Raw.at i phys
                        if Raw.at p m then ValueSome(Raw.at p a) else ValueNone
                | _ ->
                    fun i ->
                        match asNum (valueAt i) with
                        | Some x -> ValueSome x
                        | None -> ValueNone

            // A rolling window's value: as `numAt`, and a decimal at its nearest float (Phase 277):
            // a window's MEAN over a decimal column is a float, as `Mean` over one is (D72 K7).
            let windowNumAt: int -> float voption =
                match ofVec with
                | Some(Ints _)
                | Some(Floats _) -> numAt
                | _ ->
                    fun i ->
                        match valueAt i with
                        | Decimal t ->
                            match DecimalText.tryToFloat t with
                            | Some x -> ValueSome x
                            | None -> ValueNone
                        | c ->
                            match asNum c with
                            | Some x -> ValueSome x
                            | None -> ValueNone

            // One partition: the permutation's run `[s, e)`, of partition slot `g`. A prefix fold's
            // running state starts at its seed where the run resumes one (`windowColumnOver`).
            let partition (g: int) (s: int) (e: int) =
                let len = e - s
                let seed = seedOf g
                let fresh = isNull (box seed)

                match spec.Fn with
                | RowNumber ->
                    let start =
                        if fresh then
                            0
                        else
                            match seed with
                            | Int r -> r
                            | _ -> 0

                    for k in 0 .. len - 1 do
                        ints[perm[s + k]] <- start + k + 1
                // Dense-ish rank by the order key: ties (equal order keys) share a rank.
                | Rank
                | DenseRank ->
                    let mutable r = 0

                    for k in 0 .. len - 1 do
                        if k = 0 || not (wo.Same perm[s + k - 1] perm[s + k]) then
                            r <- r + 1

                        Raw.put ints (Raw.at (s + k) perm) r
                // Phase 101 — SQL RANK(): a tied block shares its LOWEST rank and the next distinct
                // order key skips by the block's size (1, 1, 3 — where the dense `Rank`/`DenseRank`
                // above give 1, 1, 2).
                | CompetitionRank ->
                    let mutable r = 0

                    for k in 0 .. len - 1 do
                        if k = 0 || not (wo.Same perm[s + k - 1] perm[s + k]) then
                            r <- k + 1

                        Raw.put ints (Raw.at (s + k) perm) r
                // Phase 101 — SQL NTILE(n): the first `count % n` buckets take one extra row.
                | NTile buckets ->
                    let small = len / buckets
                    let big = len % buckets
                    // rows [0, big*(small+1)) fill the oversized buckets; the rest the rest.
                    let bigRows = big * (small + 1)

                    for k in 0 .. len - 1 do
                        let bucket =
                            if k < bigRows then
                                k / (small + 1) + 1
                            else
                                big + (k - bigRows) / small + 1

                        Raw.put ints (Raw.at (s + k) perm) bucket
                | Lag ->
                    for k in 1 .. len - 1 do
                        out[perm[s + k]] <- valueAt perm[s + k - 1]
                | Lead ->
                    for k in 0 .. len - 2 do
                        out[perm[s + k]] <- valueAt perm[s + k + 1]
                // Phase 277: over a decimal column the running total is EXACT and a decimal, as
                // `Sum` over one is (Core `DECISIONS.md` D72 K7) — never a float in silence.
                | CumulSum when sourceIsDecimal ->
                    let mutable acc =
                        if fresh then
                            DecimalText.zero
                        else
                            match seed with
                            | Decimal t -> t
                            | _ -> DecimalText.zero

                    for k in 0 .. len - 1 do
                        let i = Raw.at (s + k) perm

                        match decimalText (valueAt i) with
                        | Some x -> acc <- DecimalText.add acc x |> Option.defaultValue acc
                        | None -> ()

                        out[i] <- Decimal acc
                // The running float total, left to right from 0.0 in window order.
                // A typed source is read straight from its carrier, one array read per row.
                | CumulSum ->
                    let mutable acc =
                        if fresh then
                            0.0
                        else
                            match seed with
                            | Float x -> x
                            | _ -> 0.0

                    match ofVec with
                    | Some(Ints(a, m)) ->
                        for k in 0 .. len - 1 do
                            let i = Raw.at (s + k) perm
                            let p = Raw.at i phys

                            if Raw.at p m then
                                acc <- acc + float (Raw.at p a)

                            Raw.put floats i acc
                            Raw.put present i true
                    | Some(Floats(a, m)) ->
                        for k in 0 .. len - 1 do
                            let i = Raw.at (s + k) perm
                            let p = Raw.at i phys

                            if Raw.at p m then
                                acc <- acc + Raw.at p a

                            Raw.put floats i acc
                            Raw.put present i true
                    | _ ->
                        for k in 0 .. len - 1 do
                            let i = Raw.at (s + k) perm

                            match numAt i with
                            | ValueSome x -> acc <- acc + x
                            | ValueNone -> ()

                            Raw.put floats i acc
                            Raw.put present i true
                // Trailing window of up to 3 (current + 2 preceding), present values only. Each
                // window is summed FRESH, left to right from zero (Phase 264): a running
                // add/subtract accumulator would change the last bit of a float sum, and therefore
                // the wire bytes.
                | RollingMean
                | RollingSum ->
                    let decimalSum = sourceIsDecimal && spec.Fn = RollingSum

                    for k in 0 .. len - 1 do
                        let first = s + max 0 (k - 2)
                        let last = s + k
                        let i = perm[last]

                        if decimalSum then
                            let mutable sum = DecimalText.zero
                            let mutable count = 0

                            for j in first..last do
                                match decimalText (valueAt perm[j]) with
                                | Some x ->
                                    sum <- DecimalText.add sum x |> Option.defaultValue sum
                                    count <- count + 1
                                | None -> ()

                            out[i] <- if count = 0 then Null else Decimal sum
                        else
                            let mutable sum = 0.0
                            let mutable count = 0

                            for j in first..last do
                                match windowNumAt perm[j] with
                                | ValueSome x ->
                                    sum <- sum + x
                                    count <- count + 1
                                | ValueNone -> ()

                            if count > 0 then
                                floats[i] <- if spec.Fn = RollingSum then sum else sum / float count
                                Raw.put present i true
                // Phase 101 — running max/min over present values; nulls carry the prior value
                // forward, so a leading run of nulls is `Null` (never a seeded 0).
                | CumulMax
                | CumulMin ->
                    let mutable acc = if fresh then Null else seed

                    for k in 0 .. len - 1 do
                        let i = Raw.at (s + k) perm
                        let v = valueAt i

                        acc <-
                            match acc, v with
                            | _, Null -> acc
                            | Null, _ -> v
                            | _ ->
                                match compareCells acc v with
                                | Some c -> if (spec.Fn = CumulMin) = (c <= 0) then acc else v
                                | None -> acc

                        out[i] <- acc

            // The partitions are the permutation's runs of one slot, scanned in sequence; each
            // output lands at its row's logical position, so the visiting order reaches nothing.
            let mutable s = 0

            while s < n do
                let g = wo.Slot[perm[s]]
                let mutable e = s + 1

                while e < n && wo.Slot[perm[e]] = g do
                    e <- e + 1

                partition g s e
                s <- e

            let ty =
                match spec.Fn with
                | RowNumber
                | Rank
                | DenseRank
                | CompetitionRank
                | NTile _ -> IntType
                | CumulSum
                | RollingSum when sourceIsDecimal -> DecimalType
                | CumulSum
                | RollingMean
                | RollingSum -> FloatType
                | Lag
                | Lead
                // The running extremes keep the source type, exactly as `AggFn.Min`/`Max` do.
                | CumulMax
                | CumulMin -> colType cols spec.Of |> Option.defaultValue StringType

            let column =
                if intOut then WInts ints
                elif floatOut then WFloats(floats, present)
                else WCells out

            Ok(ty, column)

    /// The window column over full-width rows under `cols` (the public row form, `windowStep`): each
    /// column the step reads packed once under its declared type — a column holding a
    /// cell outside its type packs boxed, and is ordered by the pinned comparator as before — and
    /// the frame path run over the rows in their own order.
    let private windowColumnOfRows (cols: Schema) (rows: Cell[][]) (spec: WindowSpec) =
        let types = cols |> List.map snd |> List.toArray
        let packed = System.Collections.Generic.Dictionary<int, Vec>()

        let vecOf (ci: int) : Vec =
            match packed.TryGetValue ci with
            | true, v -> v
            | _ ->
                let v = Vec.pack types[ci] (rows |> Array.map (fun r -> r[ci]))
                packed[ci] <- v
                v

        windowColumnOf Ordering.Exact cols vecOf (Array.init rows.Length id) spec

    let private evalWindow (f: Frame) (spec: WindowSpec) : Result<Frame, EvalError> =
        // The window read from the vectors through the selection (Phase 324); the one new column
        // placed back at its physical positions and APPENDED (a `Window` always appends, where a
        // `Derive` upserts); every other vector shared.
        let phys = Frame.physical f

        windowColumnOf Ordering.Exact f.Cols (fun ci -> f.Vecs[ci]) phys spec
        |> Result.map (fun (ty, column) -> Frame.appendColumn f spec.As ty (windowVecAt ty f.Count phys column))

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

                    let phys = Frame.physical f
                    let onVec = f.Vecs[onIdx]
                    let valVec = f.Vecs[valIdx]

                    // distinct on-values (sorted by canonical string for a deterministic column order)
                    let onValues =
                        phys
                        |> Array.map (fun p -> Vec.cellAt onVec p)
                        |> Array.filter (fun c -> not (Cell.isNull c))
                        |> List.ofArray
                        |> List.distinct
                        |> List.sortBy cellString

                    // Phase 264 — ONE pass groups the value cells by (index group, pivot column). The
                    // per-pair filter it replaces scanned the whole frame for every (group, on-value)
                    // pair, re-minting the index token per row inside the filter: O(g × v × n).
                    //
                    // An on-value's column set, keyed by its `cellEq` code (Phase 325: the typed row
                    // hasher's `cellEq` coder, where it was a token string per row): a row lands in
                    // EVERY column whose on-value `cellEq` matches its own, which is what the filter
                    // selected. Usually that is one column; two on-values `List.distinct` keeps apart
                    // but `cellEq` equates (`Int 1` and `Float 1.0`, or two `NaN`s) share a code, are
                    // two columns, and each collects every row either matches.
                    let onCount = List.length onValues
                    let onCoder = RowHash.coder true
                    let onCodes = onValues |> List.map (fun ov -> RowHash.cellCode onCoder ov true)
                    let columnsOf = Array.init onCoder.Count (fun _ -> ResizeArray<int>())

                    onCodes
                    |> List.iteri (fun c code ->
                        if code >= 0 then
                            columnsOf[code].Add c)

                    let rowOn = RowHash.codesOf onCoder onVec phys false

                    // Index groups in first-appearance order by TOKEN equality over the index cells
                    // (Phase 41's canonical token — NOT the `cellEq` relation the on-values match by
                    // above), through the typed row hasher (Phase 325), carrying the first row's
                    // index-key cells for the output rows. Each group's cells arrive in frame order,
                    // which is the order the filter aggregated.
                    let idxVecs = idxIdx |> List.map (fun i -> f.Vecs[i]) |> List.toArray
                    let slotOf, first = RowHash.slots idxVecs phys

                    let groups =
                        Array.init first.Count (fun g ->
                            let p = phys[first[g]]
                            let key = idxVecs |> Array.map (fun v -> Vec.cellAt v p) |> List.ofArray
                            key, Array.init onCount (fun _ -> ResizeArray<Cell>()))

                    // The loop proves `i` for `phys`, and for `rowOn` and `slotOf` once each is checked to
                    // hold a row per logical row; the slot and the code it reads are indexes it does not
                    // prove (Phase 326).
                    Raw.within phys.Length rowOn
                    Raw.within phys.Length slotOf

                    for i in 0 .. phys.Length - 1 do
                        let code = Raw.get i rowOn

                        if code >= 0 then
                            let _, cells = Raw.at (Raw.get i slotOf) groups
                            let v = Vec.cellAt valVec (Raw.get i phys)
#if FABLE_COMPILER
                            // A counted loop: `for` over a list compiles to an enumerator there.
                            let targets = Raw.at code columnsOf

                            for j in 0 .. targets.Count - 1 do
                                cells[targets[j]].Add v
#else
                            for c in columnsOf[code] do
                                cells[c].Add v
#endif

                    let idxCols = spec.Index |> List.map (fun n -> n, colType f.Cols n |> Option.get)

                    let pivotCols =
                        onValues |> List.map (fun ov -> cellString ov, aggType spec.Agg valType)

                    // An absent pair aggregates the EMPTY list, exactly as a filter that matched no
                    // row did — `Null` or `Int 0` by the aggregate's own rule.
                    List.ofArray groups
                    |> traverseResult (fun (k, cells) ->
                        List.init onCount id
                        |> traverseResult (fun c -> cells[c] |> List.ofSeq |> aggCells spec.Agg valType)
                        |> Result.map (fun vals -> List.toArray (k @ vals)))
                    |> Result.map (fun outRows -> Frame.ofRows (idxCols @ pivotCols) (List.toArray outRows)))))

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
            |> Result.bind (fun valIdx ->
                let idCols = idVars |> List.map (fun n -> n, colType f.Cols n |> Option.get)

                // The value column is typed by the one derived-column rule (Phase 338), the value
                // columns being its arms: their declared types' join where the schema decides it —
                // over an empty frame too — and the melted cells only where it does not.
                let dt =
                    unpivotTyping (valueVars |> List.map (fun n -> colType f.Cols n |> Option.get))

                match dt with
                | Refused -> Error(floatBesideDecimal "value")
                | _ ->
                    let out = ResizeArray<Cell[]>()

                    for row in Frame.rowsOf f do
                        let idCells = idIdx |> List.map (fun i -> row[i]) |> List.toArray

                        List.iter2
                            (fun name vi -> out.Add(Array.append idCells [| Str name; row[vi] |]))
                            valueVars
                            valIdx

                    let rows = out.ToArray()
                    let valueAt = List.length idCols + 1

                    columnTypeBy dt "value" rows.Length (fun i -> rows[i][valueAt])
                    |> Result.map (fun valType ->
                        Frame.ofRows (idCols @ [ "variable", StringType; "value", valType ]) rows)))

    // ---- pipeline driver ----

    /// Evaluate a `DataSource` to a concrete `Table`, resolving a `Ref` through `resolve`.
    let evalSource (resolve: string -> Result<Table, EvalError>) (src: DataSource) : Result<Table, EvalError> =
        match src with
        | Embedded t -> Ok t
        | Ref r -> resolve r

    /// Resolve a scalar slot against the evaluation env (`0.23.0`). A `Slot.Lit` is itself; a
    /// `Slot.Param` reads the env, and the two ways it can fail are the two the env already knows:
    /// an unbound name is `UnboundParam` enumerating the bound set (the same case a `ColExpr.Param`
    /// gives, because a host pruning unbound params does not want to learn a second one), and a cell
    /// of the wrong shape is a `TypeError` NAMING THE SLOT — which is the whole reason the slot
    /// label is threaded in, since "type error" alone would not say which of a `Limit`'s two slots
    /// it was about.
    let private cellShape (c: Cell) : string =
        match Cell.typeOf c with
        | Some t -> ColumnType.tag t
        | None -> "null"

    /// Resolve an integer slot: a count or an offset.
    let private resolveIntSlot (env: Map<string, Cell>) (slot: string) (s: Slot<int>) : Result<int, EvalError> =
        match s with
        | Slot.Lit v -> Ok v
        | Slot.Param n ->
            match Map.tryFind n env with
            | None -> Error(UnboundParam(n, env |> Map.toList |> List.map fst))
            | Some(Int v) -> Ok v
            | Some other ->
                Error(
                    TypeError(
                        slot
                        + ": param '"
                        + n
                        + "' is bound to a "
                        + cellShape other
                        + " cell, not an integer"
                    )
                )

    let private resolveStrSlot (env: Map<string, Cell>) (slot: string) (s: Slot<string>) : Result<string, EvalError> =
        match s with
        | Slot.Lit v -> Ok v
        | Slot.Param n ->
            match Map.tryFind n env with
            | None -> Error(UnboundParam(n, env |> Map.toList |> List.map fst))
            | Some(Str v) -> Ok v
            | Some other ->
                Error(
                    TypeError(
                        slot
                        + ": param '"
                        + n
                        + "' is bound to a "
                        + cellShape other
                        + " cell, not a string"
                    )
                )

    let private sequenceR (xs: Result<'a, EvalError> list) : Result<'a list, EvalError> =
        (Ok [], xs)
        ||> List.fold (fun acc r ->
            match acc, r with
            | Error e, _ -> Error e
            | Ok _, Error e -> Error e
            | Ok vs, Ok v -> Ok(v :: vs))
        |> Result.map List.rev

    /// A table's rows for the right-hand side of a two-table verb — the same transpose the left
    /// side paid at the boundary, without unpacking a frame it would only gather again.
    /// One step over the frame, running the row-local verbs through the kernel set `k` (Phase 270).
    /// Internal so the suite can run both members of the kernel pair on one host and hold their
    /// answers equal; every entry point folds through it with `Kernels.host`.
    let internal evalStepWith
        (k: KernelSet)
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (f: Frame)
        (t: Transform)
        : Result<Frame, EvalError> =
        match t with
        | Filter pred -> evalFilter k env f pred
        | Project pairs -> evalProject f pairs
        | Derive(name, expr) -> evalDerive k env f name expr
        | GroupBy(keys, aggs) -> evalGroupBy f keys aggs
        // `0.23.0` — resolve the scalar slots against the SAME env `ColExpr.Param` reads, then call
        // the unchanged reference primitives. `evalSort` / `evalLimit` still take resolved values,
        // so the pinned ordering and the pinned window semantics have exactly one definition and
        // the slot is a resolution step in front of them, not a second evaluator.
        | Sort by ->
            by
            |> List.map (fun (c, d) -> resolveStrSlot env "sort key column" c |> Result.map (fun c -> c, d))
            |> sequenceR
            |> Result.map (evalSort f)
        | Distinct -> Ok(evalDistinct f)
        | Limit(n, offset) ->
            resolveIntSlot env "limit n" n
            |> Result.bind (fun n ->
                resolveIntSlot env "limit offset" offset
                |> Result.map (fun offset -> evalLimit f n offset))
        | Window spec -> evalWindow f spec
        | Pivot spec -> evalPivot f spec
        | Unpivot(idVars, valueVars) -> evalUnpivot f idVars valueVars
        | Join(right, on, how) ->
            evalSource resolve right
            |> Result.bind (fun t -> evalJoin f (Frame.ofTable t) on how)
        | Union other ->
            evalSource resolve other
            |> Result.map Frame.ofTable
            |> Result.bind (evalUnion f)
        | Intersect other ->
            evalSource resolve other
            |> Result.bind (fun t -> evalSetOp "intersect" true f (Frame.ofTable t))
        | Except other ->
            evalSource resolve other
            |> Result.bind (fun t -> evalSetOp "except" false f (Frame.ofTable t))

    /// One step over the frame through the host kernels (`Kernels.host`, chosen when the package is
    /// compiled). Internal so the suite can hold the frame's well-formedness after every step of a
    /// generated pipeline.
    let internal evalStep
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (f: Frame)
        (t: Transform)
        : Result<Frame, EvalError> =
        evalStepWith Kernels.host resolve env f t

    /// A `Ref`-rejecting resolver — the default for embedded-only pipelines (and the conformance kit).
    let noResolve: string -> Result<Table, EvalError> =
        fun r -> Error(UnresolvedSource r)

    /// The reference evaluator over a prepared source, reporting alongside its answer how many row
    /// evaluations at steps it cost (Phase 267) — the one driver every entry point folds through;
    /// see `evalPipelineWithInEnvCounted` for what the count means. `k` is the kernel set the
    /// row-local verbs run through (Phase 270); `fused` says whether a `Sort` followed by a `Limit`
    /// runs as the stable top-n kernel (Phase 269) or as the two steps written. The pipeline is
    /// folded AS GIVEN: the planning is the callers' (`evalPreparedCountedWith`, which every entry
    /// point reaches, plans; `evalPreparedCountedAsWritten` does not).
    let private evalPreparedCountedFolding
        (k: KernelSet)
        (fused: bool)
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<Table * int, EvalError> =
        let costOf (f: Frame) (step: Transform) =
            match step with
            | Filter _
            | Derive _ -> Frame.rows f
            | _ -> 0

        let rec go f evaluated =
            function
            | [] -> Ok(Frame.toTable f, evaluated)
            // Phase 269 — the fusion the planner names as `TopN`: a `Sort` followed by a `Limit`
            // runs as the stable top-n kernel. The slots resolve through the same resolvers, in the
            // order the two steps would have resolved them, so the first error is the same one.
            // Neither step is charged an evaluation, as neither was.
            | Sort by :: Limit(n, offset) :: rest when fused ->
                by
                |> List.map (fun (c, d) -> resolveStrSlot env "sort key column" c |> Result.map (fun c -> c, d))
                |> sequenceR
                |> Result.bind (fun keys ->
                    resolveIntSlot env "limit n" n
                    |> Result.bind (fun n ->
                        resolveIntSlot env "limit offset" offset
                        |> Result.bind (fun offset -> go (evalTopN f keys n offset) evaluated rest)))
            | step :: rest ->
                let cost = costOf f step

                evalStepWith k resolve env f step
                |> Result.bind (fun f' -> go f' (evaluated + cost) rest)

        go prepared.Frame.Value 0 pipeline

    /// The reference evaluator over a prepared source through the kernel set `k` (Phase 270),
    /// reporting alongside its answer how many row evaluations at steps it cost (Phase 267) — the
    /// one driver every entry point folds through; see `evalPipelineWithInEnvCounted` for what the
    /// count means. Since Phase 269 the pipeline is PLANNED before it is folded (`Planner.rewrite`
    /// over the source's schema) and the `Sort` > `Limit` pair is run as one kernel: the answer is
    /// the reference's, errors included (`Conformance.plannerLaws`), and the count is the planned
    /// walk's, which is the honest one. Internal so the suite can run both members of the kernel
    /// pair on one host and hold their answers equal; every entry point passes `Kernels.host`.
    let internal evalPreparedCountedWith
        (k: KernelSet)
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<Table * int, EvalError> =
        let frame = prepared.Frame.Value
        evalPreparedCountedFolding k true resolve env (Planner.rewrite frame.Cols pipeline) prepared

    /// `evalPreparedCountedWith` through the host kernels.
    let internal evalPreparedCounted
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<Table * int, EvalError> =
        evalPreparedCountedWith Kernels.host resolve env pipeline prepared

    /// The reference evaluator over the pipeline EXACTLY AS WRITTEN (Phase 269): no rewrite, no
    /// fused kernel — every step folded in the order and the form the caller gave, through the host
    /// kernels, which is the semantics the planner is held to. The planned entry points answer the
    /// same; this one exists so that the claim is checkable (`Conformance.plannerLaws`) and so that
    /// a host certifying a planner of its own has the reference to certify against.
    let internal evalPreparedCountedAsWritten
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<Table * int, EvalError> =
        evalPreparedCountedFolding Kernels.host false resolve env pipeline prepared

    /// Prepare a table once for many evaluations (Phase 267): the `Table` boundary — one typed
    /// unpack per column — paid here rather than by every pipeline that reads the source. The table
    /// is held by reference beside its prepared form and never copied; a consumer that evaluates
    /// many pipelines over one source (a sheet, a dashboard) prepares it once and hands the result
    /// to `evalPrepared` per pipeline, or to `Incremental.primePrepared`. Since Phase 268 a prepared
    /// source is also a persistent VERSION: `ColumnOps.applyPrepared` edits it at the cost of a
    /// chunk, sharing everything an edit did not touch, and `toTable` reads any version back.
    let prepare (t: Table) : Prepared = Prepared.ofTable t

    /// The table a prepared source stands for (Phase 268): the very table it was prepared from,
    /// or — for a version an edit or a chunked refresh produced — the table built from its chunks
    /// the first time it is asked for, and kept. `toTable (prepare t)` is `t` itself.
    let toTable (prepared: Prepared) : Table = Prepared.table prepared

    /// The reference evaluator over a prepared source (Phase 267): `evalPipelineWithInEnv` with the
    /// boundary already paid — the same resolver, env and pipeline, the same cells, the same errors.
    let evalPrepared
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<Table, EvalError> =
        evalPreparedCounted resolve env pipeline prepared |> Result.map fst

    /// The reference evaluator, parameterised (Phase 77), reporting alongside its answer how many
    /// ROW EVALUATIONS AT STEPS producing that answer cost (Phase 117).
    ///
    /// The unit is one evaluation of one step's expression against one row — precisely what
    /// `evalExprInRow` is one of. A `Filter` and a `Derive` evaluate their expression once per row
    /// alive at that step, so each is charged the frame's row count where it stands; every other
    /// verb evaluates no per-row expression and is charged none, which is why a `Sort` and a
    /// `GroupBy` contribute nothing.
    ///
    /// It exists so that a full evaluation and a restricted one are measured on ONE scale. An
    /// incremental evaluator counts the same unit — the row evaluations it did not avoid — so
    /// projecting a full evaluation onto its SOURCE ROW COUNT instead made the two incomparable, and
    /// made a pipeline with several evaluating steps read as if a full evaluation were the cheaper
    /// answer. Deriving the count outside this driver would be a second semantics; it is counted
    /// here, where the steps are actually taken.
    let evalPipelineWithInEnvCounted
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (input: Table)
        : Result<Table * int, EvalError> =
        evalPreparedCounted resolve env pipeline (prepare input)

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
        evalPipelineWithInEnvCounted resolve env pipeline input |> Result.map fst

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

    // ---- the pipeline as written (Phase 269) ----
    // Every entry point above plans the pipeline before folding it (`Plan.rewrite` over the input's
    // schema, and the `Sort` > `Limit` pair as one kernel). These two fold the pipeline EXACTLY AS
    // WRITTEN — the strict, first-error semantics the planner is held to — so that "the planned
    // evaluation equals the reference, errors included" is a claim `Conformance.plannerLaws` can
    // check rather than assert, and so that a host with a planner of its own has the reference
    // to certify against.

    /// The reference evaluator over the pipeline as written, with a param env and a `Ref`
    /// resolver: `evalPipelineWithInEnv` with no rewrite and no fused kernel.
    let evalPipelineWithInEnvAsWritten
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (input: Table)
        : Result<Table, EvalError> =
        evalPreparedCountedAsWritten resolve env pipeline (prepare input)
        |> Result.map fst

    /// The reference evaluator over the pipeline as written, over embedded sources and no params:
    /// `evalPipeline` with no rewrite and no fused kernel.
    let evalPipelineAsWritten (pipeline: Transform list) (input: Table) : Result<Table, EvalError> =
        evalPipelineWithInEnvAsWritten noResolve Map.empty pipeline input

    // ---- clock-pinning entry points (Phase 125) ----
    // Each is `substituteNow` followed by the corresponding param-resolving entry point, in that
    // order and one line long — there is no second evaluator. Pinning the clock FIRST is what makes
    // `Now` deterministic: after the substitution the pipeline holds literals, so everything below
    // is the same pure fold it was before `Now` existed, and `Conformance.nowLaws` can state its
    // claim about a value rather than about an ordering of calls.

    /// The reference evaluator with a pinned clock, a param env, and a `Ref` resolver — the full
    /// form every other `…At` entry point delegates to.
    let evalPipelineWithInEnvAt
        (clock: ClockWitness)
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (input: Table)
        : Result<Table, EvalError> =
        evalPipelineWithInEnv resolve env (Transform.substituteNow clock pipeline) input

    /// The reference evaluator over embedded sources, with a pinned clock and a param env.
    let evalPipelineInEnvAt
        (clock: ClockWitness)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (input: Table)
        : Result<Table, EvalError> =
        evalPipelineWithInEnvAt clock noResolve env pipeline input

    /// The reference evaluator over embedded sources, with a pinned clock and no params.
    let evalPipelineAt (clock: ClockWitness) (pipeline: Transform list) (input: Table) : Result<Table, EvalError> =
        evalPipelineWithInEnvAt clock noResolve Map.empty pipeline input

    // ---- the reference primitives, exposed (Phase 99) ----
    // An incremental evaluator recomputes a SUBSET of what a full evaluation recomputes, so it must
    // compute that subset through the SAME code as the reference — otherwise the two answers can
    // differ and the "incremental ≡ reference" claim becomes a coincidence maintained by hand. These
    // three are the whole of what the row-local + maintained-group strategies need; each is a
    // one-line wrapper over the private definition above, so there is exactly one implementation.

    /// Evaluate one `ColExpr` against a single row, resolving `Param`s from `env` — the reference
    /// expression evaluator itself. Exposed so an incremental evaluator computes a re-evaluated
    /// cell through the same path as a full evaluation rather than a copy of it.
    ///
    /// Its twin inside this assembly is `resolveExpr` once per step and `evalResolved` once per
    /// array row (Phase 263) — the same evaluator, split at the point where the names are looked up,
    /// which is how the incremental seam calls it. This list form is that pair composed.
    let evalExprInRow (env: Map<string, Cell>) (cols: Schema) (row: Cell list) (e: ColExpr) : Result<Cell, EvalError> =
        evalResolved (List.toArray row) (resolveExpr env cols e)

    /// Compute one aggregate over a cell list of the given source type, in the evaluator's
    /// `EvalError` envelope — what `GroupBy` calls per group. Exposed so an incremental evaluator
    /// that recomputes only the affected groups produces the same cells (and the same errors) the
    /// reference would.
    let aggregateCells (fn: AggFn) (srcType: ColumnType) (cells: Cell list) : Result<Cell, EvalError> =
        aggCells fn srcType cells

    /// The output column type of an aggregate over a source of the given type — `GroupBy`'s
    /// aggregate-column typing.
    let aggregateType (fn: AggFn) (srcType: ColumnType) : ColumnType = aggType fn srcType

    /// The column type a `Derive`d column takes from its computed cells WHERE ONLY THE CELLS DECIDE
    /// IT: the present cells' types joined under `ColumnType.widens` in row order (Phase 321; it was
    /// the first present cell's type), `StringType` when every cell is null. Since Phase 338 a
    /// derive the typer decides takes the typer's type on every frame instead (`SchemaWalk` states
    /// it), and only a derive reading a `Param`, a `Now`, an unknown column or a join the exact
    /// typer keeps apart is typed by this fold (which, there, also refuses a float beside a
    /// decimal). Exposed because such a type is a function of the WHOLE column, not of one row.
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
    ///
    /// Its twin inside this assembly is `resolveSortKeys` once per step and `compareResolved` once
    /// per comparison of array rows (Phase 263); this list form is that pair composed.
    let rowCompareBy (cols: Schema) (by: (string * SortDir) list) (r1: Cell list) (r2: Cell list) : int =
        compareResolved (resolveSortKeys cols by) (List.toArray r1) (List.toArray r2)

    /// Is this window function's frame BOUNDED — its output for a row a function of the rows within
    /// a fixed offset of it in its partition's order (Phase 120)?
    ///
    /// `Lag` and `Lead` read one row either side; the rolling pair reads the pinned trailing window
    /// `evalWindow` implements (current + two preceding). Every other member reads the whole
    /// partition: the ranking family and `NTile` are functions of a row's POSITION among all of its
    /// partition's rows (and, for `NTile`, of the partition's size), and the cumulative aggregates
    /// are functions of every preceding row's value.
    ///
    /// It is a statement about the evaluator's own frames, so it lives beside them rather than in
    /// the incremental seam that reads it — a copy there would be a second answer to a question this
    /// module already answers in `evalWindow`, and would drift the first time a window function is
    /// added.
    ///
    /// **It is NOT what admits a `Window` to the incremental walk, and was only ever a proxy for
    /// it (`0.19.0`).** That walk recomputes the appended column wholesale over the frame it
    /// walked, which is correct for every member here, so what it needs is row-set preservation —
    /// which every member has. This predicate stays because the distinction it draws is real and
    /// load-bearing elsewhere: it is the line a phase restricting the recompute to the rows a delta
    /// names or DISPLACES would have to draw, and the incremental equivalence family uses it to
    /// name the partition-global family its sample-adequacy demand requires be reached.
    let windowFrameBounded (fn: WindowFn) : bool =
        match fn with
        | Lag
        | Lead
        | RollingMean
        | RollingSum -> true
        | RowNumber
        | Rank
        | DenseRank
        | CompetitionRank
        | NTile _
        | CumulSum
        | CumulMax
        | CumulMin -> false

    /// The reference `Window` step over a frame given as its schema and rows (Phase 120): the new
    /// schema (the input's, plus the appended column) and the new rows, in the input's own order.
    /// Exposed for the reason the five above are — an incremental evaluator that recomputed a
    /// window column through its own copy of the frame semantics would agree with the reference on
    /// every corpus anyone thought to write and disagree on the first tie, the first null and the
    /// first empty partition.
    let windowStep
        (cols: Schema)
        (rows: Cell list list)
        (spec: WindowSpec)
        : Result<Schema * Cell list list, EvalError> =
        let arr = rows |> List.map List.toArray |> List.toArray

        windowColumnOfRows cols arr spec
        |> Result.map (fun (ty, col) ->
            cols @ [ spec.As, ty ],
            arr
            |> Array.mapi (fun i r -> List.ofArray (Array.append r [| windowCellAt col i |]))
            |> List.ofArray)

    /// The reference `Join`'s key-column resolution (Phase 120): the left and right column indices
    /// its `on` pairs name, or the FIRST unresolvable name in the order the reference reports it —
    /// left names before right names, with both schemas' available columns. Exposed so a restricted
    /// evaluation refuses exactly what the reference refuses, and says the same thing when it does.
    let joinKeyIndices
        (leftCols: Schema)
        (rightCols: Schema)
        (on: (string * string) list)
        : Result<int list * int list, EvalError> =
        joinKeyIdx leftCols rightCols on

    /// The reference `Join`'s key predicate (Phase 120), over the two rows' already-projected key
    /// cells. It is `cellEq` pairwise, so a `Null` key matches NOTHING — including another `Null` —
    /// which is exactly where a second implementation would diverge: the canonical row token
    /// `Distinct` and the set ops dedup on says `Null` matches itself, and a join says it does not.
    let joinKeysMatch (leftKeys: Cell list) (rightKeys: Cell list) : bool = joinKeyEq leftKeys rightKeys

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
        | Now _ -> Set.empty
        | Quotient(a, b, _) -> Set.union (exprCols a) (exprCols b)
        | Rounded(x, _) -> exprCols x

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
        // `0.23.0` — only the LITERAL key columns are named here. A slot param's column is not
        // known without an env, which would make this an UNDER-approximation (unsafe: `evalFrom`
        // reuses a prior result when a changed column is absent from this set). `evalFrom` therefore
        // declines the reuse outright while any slot param stands — see its guard.
        | Sort by -> by |> List.choose (fun (c, _) -> Slot.tryLit c) |> Set.ofList
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
                // `0.23.0` — an unresolved SLOT param means `readColumns` cannot see which column a
                // `Sort` orders by, so "the pipeline does not read `c`" is not a claim this walk can
                // make. Declining the reuse costs a full evaluation; taking it would return a stale
                // table for a change that did matter.
                && List.isEmpty (Transform.slotParamsOf pipeline)
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
        | UnpinnedClock g ->
            "unpinned clock: a now("
            + NowGrain.tag g
            + ") reached evaluation with no ClockWitness; pin one with evalPipelineAt (or Transform.substituteNow)"


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
    ///
    /// Public again since `0.22.0` (Phase 129). `0.19.0` narrowed it to `internal` as one of
    /// thirty-one members a caller-count sweep found unreferenced; it had a caller the sweep could
    /// not see, and the narrowing entry in `STABILITY.md` invited exactly this correction.
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
    /// restated loosely: the positional/ranking family is `Int`, the accumulating family is `Float`
    /// (a running total over a decimal column a `Decimal`, Phase 277),
    /// and the shifting + running-extreme family keeps the source column's type, which is unknown
    /// exactly when the source column's type is.
    let private windowType (input: SchemaKnowledge) (spec: WindowSpec) : ColumnType option =
        match spec.Fn with
        | RowNumber
        | Rank
        | DenseRank
        | CompetitionRank
        | NTile _ -> Some IntType
        | RollingMean -> Some FloatType
        // Phase 277: a running total over a decimal column is a decimal (exact), over any other a
        // float — so it is known exactly when the source column's type is.
        | CumulSum
        | RollingSum ->
            match typeOf spec.Of input with
            | Some DecimalType -> Some DecimalType
            | Some _ -> Some FloatType
            | None -> None
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

        // The name is declared; the type is the one the evaluator gives the column (Phase 338): the
        // typer's decided type on every frame, `StringType` for an expression with no present
        // value, and unknown exactly where the cells decide it — a `Param`, a `Now`, a column the
        // walk cannot type, a join of two types the exact typer keeps apart — or the derive is
        // refused. The typer reads the columns whose types are known; one the walk cannot type
        // reads as absent, which types the expression as undecidable rather than as anything false.
        | Derive(name, expr) ->
            let known =
                columns input
                |> List.choose (fun c -> c.Type |> Option.map (fun ty -> c.Name, ty))

            upsert
                { Name = name
                  Type = DataFrame.derivedColumnType known expr }
                input

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
                        // The evaluator's rule over the value columns' declared types (Phase 338):
                        // decided where every value column's type is known and they join, unknown
                        // where one is not known or only the cells decide.
                        let types = valueVars |> List.map (fun name -> typeOf name input)

                        if types |> List.forall Option.isSome then
                            match DataFrame.unpivotTyping (types |> List.choose id) with
                            | DataFrame.Decided ty -> Some ty
                            | DataFrame.ByCells
                            | DataFrame.Refused -> None
                        else
                            None } ]
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
        // Phase 277: the canonical decimal TEXT, a JSON string, as the column codec carries it
        // (Core `DECISIONS.md` D72 K5) — a number token would have been through a float.
        | Decimal s -> Canon.typed "Decimal" [ "value", JStr s ]

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
                // Decimal text, canonicalised on read; an integer token is exact and read too, and
                // anything else — a fractional number token included — is refused (D72 K5).
                | Some(JStr s), "Decimal" ->
                    match DecimalText.tryCanonical s with
                    | Some canonical -> Ok(Decimal canonical)
                    | None -> Error(TypeMismatch("lit", t, "value"))
                | Some(JInt i), "Decimal" -> Ok(Decimal(string i))
                | _ -> Error(TypeMismatch("lit", t, "value"))
            | _ -> Error(MissingField "lit.$type")
        | _ -> Error(MalformedShape "lit: expected object")

    // ---- ColExpr ----

    // ---- scalar slots (`0.23.0`) ----
    // A LITERAL slot encodes exactly as the bare value did before this type existed, so every
    // pre-`0.23.0` pipeline is byte-identical on the wire and every pre-`0.23.0` document still
    // decodes. A param is the one new shape: `{"$param":"<name>"}`, an object where a scalar was,
    // which no literal spelling of an int or a column name can collide with.
    let private slotJson (litJson: 'T -> JVal) (s: Slot<'T>) : JVal =
        match s with
        | Slot.Lit v -> litJson v
        | Slot.Param n -> JObj [ "$param", JStr n ]

    let private slotOf
        (litOf: JVal -> Result<'T, ColumnError>)
        (what: string)
        (el: JVal)
        : Result<Slot<'T>, ColumnError> =
        match el with
        | JObj fields ->
            match fields |> List.tryFind (fun (n, _) -> n = "$param") with
            | Some(_, JStr n) -> Ok(Slot.Param n)
            | Some _ -> Error(MalformedShape("\"$param\" must be a JSON string (" + what + ")"))
            | None -> Error(MalformedShape(what + ": an object here is a parameter slot and must carry \"$param\""))
        | _ -> litOf el |> Result.map Slot.Lit

    let private intOf el =
        match el with
        | JInt i -> Ok i
        | _ -> Error(MalformedShape "expected int")

    // ---- the rounding policy (Phase 277) ----
    // The ONE place a `RoundingMode` is spelled: a total round trip over the seven modes. The scale
    // is an integer slot in the same wire form as `Limit`'s count.

    let private modeTag (m: RoundingMode) : string =
        match m with
        | RoundingMode.HalfEven -> "half-even"
        | RoundingMode.HalfUp -> "half-up"
        | RoundingMode.HalfDown -> "half-down"
        | RoundingMode.Up -> "up"
        | RoundingMode.Down -> "down"
        | RoundingMode.Ceiling -> "ceiling"
        | RoundingMode.Floor -> "floor"

    let private allModes: RoundingMode list =
        [ RoundingMode.HalfEven
          RoundingMode.HalfUp
          RoundingMode.HalfDown
          RoundingMode.Up
          RoundingMode.Down
          RoundingMode.Ceiling
          RoundingMode.Floor ]

    let private modeOf (tag: string) : RoundingMode option =
        allModes |> List.tryFind (fun m -> modeTag m = tag)

    let private roundingJson (r: Rounding) : JVal =
        JObj [ "mode", JStr(modeTag r.Mode); "scale", slotJson JInt r.Scale ]

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
        | Now grain -> Canon.typed "now" [ "grain", JStr(NowGrain.tag grain) ]
        | Quotient(a, b, r) ->
            Canon.typed
                "quotient"
                [ "dividend", encodeExpr a
                  "divisor", encodeExpr b
                  "rounding", roundingJson r ]
        | Rounded(x, r) -> Canon.typed "rounded" [ "expr", encodeExpr x; "rounding", roundingJson r ]

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

    /// A rounding policy: `mode` one of the seven spellings (anything else is `UnknownType` naming
    /// them), `scale` an integer slot.
    let private roundingOf (el: JVal) : Result<Rounding, ColumnError> =
        field "mode" el
        |> Result.bind strOf
        |> Result.bind (fun ms ->
            match modeOf ms with
            | None -> Error(UnknownType(ms, allModes |> List.map modeTag))
            | Some mode ->
                field "scale" el
                |> Result.bind (slotOf intOf "rounding scale")
                |> Result.map (fun scale -> { Scale = scale; Mode = mode }))

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
            | "quotient" ->
                field "dividend" el
                |> Result.bind decodeExpr
                |> Result.bind (fun a ->
                    field "divisor" el
                    |> Result.bind decodeExpr
                    |> Result.bind (fun b ->
                        field "rounding" el
                        |> Result.bind roundingOf
                        |> Result.map (fun r -> Quotient(a, b, r))))
            | "rounded" ->
                field "expr" el
                |> Result.bind decodeExpr
                |> Result.bind (fun x ->
                    field "rounding" el
                    |> Result.bind roundingOf
                    |> Result.map (fun r -> Rounded(x, r)))
            | "now" ->
                field "grain" el
                |> Result.bind strOf
                |> Result.bind (fun gs ->
                    match NowGrain.ofTag gs with
                    | Some g -> Ok(Now g)
                    | None -> Error(UnknownType(gs, NowGrain.allTags)))
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
                          "isNull"
                          "now" ]
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

    /// A plain `(column, direction)` key — a `WindowSpec.OrderBy` entry. NOT a `Sort` key: a
    /// window's frame ordering was not asked for as a slot, and widening it too would be a breaking
    /// change taken on a symmetry argument rather than on a demand.
    ///
    /// `0.28.0` — the member is `column`, not `col`: a member whose only honest name is "the
    /// column" is spelled out (DECISIONS D48). `col` remains a decode alias and is never emitted.
    let private orderJson (col: string, dir) =
        JObj [ "column", JStr col; "dir", JStr(dirTag dir) ]

    /// A `Sort` key, whose COLUMN is a slot (`0.23.0`). A literal encodes as the bare string it
    /// always did, so the slot widening cost a pre-`0.23.0` sort nothing on the wire; the MEMBER's
    /// spelling then moved in `0.28.0` (`col` → `column`, D48), which is what does change its bytes.
    let private sortKeyJson (col: Slot<string>, dir) =
        JObj [ "column", slotJson JStr col; "dir", JStr(dirTag dir) ]

    /// The shared key decoder, parameterised over how the COLUMN half reads — so the alias set
    /// (`col`, `descending`, `direction`) has one definition across a `Sort` key and a window's
    /// frame ordering rather than two that can drift.
    let private keyOfWith (colOf: JVal -> Result<'C, ColumnError>) el =
        // Phase 92 admitted `column` as an alias of `col`; `0.28.0` (D48) swapped which of the two
        // is canonical, so both spellings still decode and `column` is what re-encodes. Boolean
        // `descending` remains an alias for `dir`.
        fieldAliased "column" "col" el
        |> Result.bind colOf
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

    /// A plain key — a window's frame ordering.
    let private orderOf el : Result<string * SortDir, ColumnError> = keyOfWith strOf el

    /// A `Sort` key, whose column may be a slot (`0.23.0`).
    let private sortKeyOf el : Result<Slot<string> * SortDir, ColumnError> =
        keyOfWith (slotOf strOf "sort key column") el

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
        // `0.28.0` (D48) — the member is `columns`, not `cols`: it holds the LIST of column renames,
        // and `cols` is the wire format's name for an integer column COUNT. `cols` remains a decode alias
        // and is never emitted.
        | Project pairs -> Canon.typed "project" [ "columns", JArr(pairs |> List.map pairJson) ]
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
        | Sort by -> Canon.typed "sort" [ "by", JArr(by |> List.map sortKeyJson) ]
        | Distinct -> Canon.typed "distinct" []
        | Limit(n, offset) -> Canon.typed "limit" [ "n", slotJson JInt n; "offset", slotJson JInt offset ]
        | Union src -> Canon.typed "union" [ "source", ColumnCodec.encodeJson src ]
        | Intersect src -> Canon.typed "intersect" [ "source", ColumnCodec.encodeJson src ]
        | Except src -> Canon.typed "except" [ "source", ColumnCodec.encodeJson src ]

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
                    // `0.28.0` (D48) — `cols` is the pre-rename spelling, kept as a decode alias;
                    // giving both is the same ambiguity refusal every other aliased member makes.
                    fieldAliased "columns" "cols" el
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
                    |> Result.bind (mapM sortKeyOf)
                    |> Result.map Sort
                | "distinct" -> Ok Distinct
                | "limit" ->
                    // Phase 92 — `count` aliases `n`; an absent `offset` is unambiguously 0.
                    fieldAliased "n" "count" el
                    |> Result.bind (slotOf intOf "limit n")
                    |> Result.bind (fun n ->
                        match tryField "offset" el with
                        | Some o -> slotOf intOf "limit offset" o |> Result.map (fun ofs -> Limit(n, ofs))
                        | None -> Ok(Limit(n, Slot.Lit 0)))
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
        match Json.parseDetailed s with
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
