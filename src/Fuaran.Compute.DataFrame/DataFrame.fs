namespace Fuaran.Compute

open Fuaran.Core
open Fuaran.Compute.Elementary
open Fuaran.Compute.Regression
// The frame's vector cases, over Core's `ColumnData` cases of the same names (Phase 423).
open type Vec

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
    /// **Deprecated (Phase 414, `DECISIONS.md` D24): write `DenseRank` or `CompetitionRank`.**
    /// `DenseRank` is this exact computation; `CompetitionRank` is SQL's `RANK()`, which this is
    /// NOT. Ties share a rank and the NEXT distinct order key is the next integer — the gapless
    /// "dense" rank (`1, 1, 2`), where SQL's `RANK()` gaps (`1, 1, 3`). The name predates the
    /// distinction.
    ///
    /// Deprecated in documentation only, never by `[<Obsolete>]`: an obsolete union case warns at
    /// every exhaustive match over `WindowFn`, which a consumer building with warnings as errors
    /// could not silence without a wildcard. It evaluates, encodes (`"rank"`) and decodes exactly
    /// as it always has, so no existing pipeline's output or wire form changes. It is never
    /// re-pointed at the gapped semantics; it may be REMOVED no earlier than `1.0.0`, with the wire
    /// tag `"rank"` kept as a decode alias of `DenseRank` (D24).
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
//  the full reasoning is D13 of the Fuaran.Core repository's DECISIONS.md, taken
//  before this strand was cut from it (this repository's own D13 is another entry).
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
//  * `Pow` / `Exp` / `Ln` / `Log10` ARE present (Phase 557, D43) and EXACT on every host
//    (Phase 594, D54): an INTEGER-exponent `Pow` runs one pinned routine (binary
//    exponentiation over IEEE-754 multiplication, one reciprocal for a negative exponent);
//    a non-integer `Pow`, `Exp`, `Ln` and `Log10` read the special-value table first and
//    then the pinned routines of `Fuaran.Compute.Elementary` (D52), never the
//    host's maths library, so every one is byte-identical across hosts. `Sqrt` is pinned
//    by IEEE-754 itself (correctly rounded). The static walk's `HostRounded` mark
//    (`Exactness`) is no longer reached by any scalar function here; it stays for a value
//    whose lineage the caller declares host-rounded.
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
    // Phase 101 — the closed-set edges.
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
    // Phase 557 (D43) — the power functions, each exact on every host since Phase 594 (D54).
    /// `Pow(base, exponent)`. An INTEGER exponent (an `Int` cell) runs the one pinned routine —
    /// binary exponentiation over IEEE-754 multiplication, one reciprocal for a negative exponent
    /// (`DataFrame.powInt`) — so the bits are the same on every host; an `Int` base under an
    /// exponent `>= 0` answers an `Int` and refuses int32 overflow by name, as `Mul` does, and under a
    /// negative one answers the float reciprocal. A FLOAT exponent reads the special-value table
    /// (`Elementary.powTable`) first, then the pinned `Elementary.pow` (D52), the same bits on every
    /// host. Outside the reals' domain — a finite negative base under
    /// a non-integer exponent, a zero base under a negative one — the answer is `Null`, as `Div` by
    /// zero is; a NaN operand propagates as D16's one NaN. A `Decimal` operand is refused by name, as
    /// `Sqrt` refuses it.
    | Pow
    /// `e` raised to the operand: the pinned `Elementary.exp`, the same bits on every host. Overflow is
    /// `Inf` and underflow `0`, both decided by the table before the routine is called. A `Decimal`
    /// operand is refused by name.
    | Exp
    /// The natural logarithm: the pinned `Elementary.ln`, the same bits on every host; `Null` at zero
    /// and below, as `Sqrt` is below zero; `0` exactly at `1`, `Inf` at `Inf`. A `Decimal` operand is refused by name.
    | Ln
    /// The base-10 logarithm, on `Ln`'s terms.
    | Log10

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

/// How `ColExpr.Interpolate` reads between two neighbouring knots (Phase 596, `DECISIONS.md` D48).
/// Every kind is exact at a knot and is computed from additions, subtractions, multiplications and
/// divisions only, in a pinned order, so its bytes are the same on every host.
///
/// `RequireQualifiedAccess` because the names are ordinary words other unions may want.
[<RequireQualifiedAccess>]
type InterpKind =
    /// The straight line between the two knots: `y0 + (y1 - y0) * ((x - x0) / (x1 - x0))`.
    | Linear
    /// The left knot's value, held until the next knot: a right-continuous step.
    | Step
    /// The monotone cubic Hermite curve of Fritsch and Carlson, with the slope rule pinned in D48:
    /// monotone wherever the knot data are, and flat on a segment whose two knots are equal.
    | MonotoneCubic

/// What `ColExpr.Interpolate` answers for an operand outside its knots' range (Phase 596).
///
/// `RequireQualifiedAccess` because `Null` is already a `Cell` case in this namespace.
[<RequireQualifiedAccess>]
type InterpOutside =
    /// The nearest end knot's value.
    | Clamp
    /// `Null`.
    | Null

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
    /// The operand brought to `unit` (Phase 429, `DECISIONS.md` D29) — the one explicit conversion
    /// between two units of one dimension, and the one way to give a literal or an unconstrained
    /// column a unit. Resolved by SUBSTITUTION against the step's schema before evaluation
    /// (`DataFrame.resolveUnits`), as `InParam` and `Now` are: over an operand that states no unit it
    /// is a TAG, `Cast(FloatType, operand)` under `unit`; over an operand of a compatible unit it is
    /// `Cast(FloatType, operand) * factor`, the exact ratio `Unit.conversionFactor` gives, as a float
    /// literal; over an operand of another dimension it is refused by name (`EvalError.UnitMismatch`).
    /// A conversion therefore always answers a `Float` (a measurement), a null operand propagates,
    /// and a decimal operand is cast as `Cast(FloatType, …)` casts it — a caller that needs the exact
    /// product writes `Quotient` with the factor itself.
    ///
    /// Wire: `{"$type":"convert","expr":…,"unit":"<canonical unit text>"}`.
    | Convert of ColExpr * unit: UnitOfMeasure
    /// A parameter of the named function whose body this is (Phase 596, `DECISIONS.md` D48) — the
    /// one way a `FnDef`'s body reads its input, as `Col` is the way a step reads a row. It exists
    /// only inside a definition: expansion (`FnDef.expand`) replaces every one with the call's
    /// argument, so one that reaches evaluation is refused by name (`EvalError.FunctionError`).
    ///
    /// Wire: `{"$type":"arg","name":"<parameter>"}`.
    | Arg of name: string
    /// A call of a function the pipeline defines (Phase 596, D48): its arguments bind the
    /// definition's parameters in order, and a parameter left without one takes its default.
    /// Resolved by EXPANSION before evaluation (`FnDef.expand`), as `Now` and `InParam` are
    /// resolved by substitution: the call is replaced by the definition's body with every `Arg`
    /// replaced by its argument, so typing, units, exactness and incremental behaviour are exactly
    /// the body's and nothing new reaches the evaluator. One that reaches evaluation unexpanded is
    /// refused by name (`EvalError.FunctionError`).
    ///
    /// Wire: `{"$type":"invoke","name":"<function>","args":[…]}` — `invoke`, because `call` has been a
    /// lenient spelling of `apply` since Phase 93.
    | Call of name: string * args: ColExpr list
    /// The operand read through a curve given as data (Phase 596, D48): `knots` are `(x, y)` pairs
    /// with strictly increasing, finite `x` and finite `y` — at least two — and `kind` says how the
    /// curve runs between them. An operand outside the knots' range takes `outside`. The operand is
    /// an `Int` or a `Float` (an int promotes); a `Decimal` is refused by name (`Cast` first) and a
    /// null operand propagates. The answer is a `Float`, the knot's own `y` at a knot exactly, and a
    /// NaN operand answers NaN. Knots that break the rule are refused by name when the step resolves
    /// (`EvalError.InvalidKnots`).
    ///
    /// Wire: `{"$type":"interpolate","expr":…,"knots":[[x,y],…],"kind":"linear"|"step"|"monotoneCubic",
    /// "outside":"clamp"|"null"}`.
    | Interpolate of ColExpr * knots: (float * float) list * kind: InterpKind * outside: InterpOutside

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

/// One parameter of a named function (Phase 596, `DECISIONS.md` D48): its name, read in the body
/// as `Arg name`, and the value a call that passes no argument for it takes. A default is a `Slot`:
/// a literal cell, or a named parameter of the pipeline resolved from the evaluation env as any
/// `Param` is — so a Hill curve's exponent and half-saturation point can be bound per evaluation.
/// A parameter with no default must be passed.
type FnParam =
    { Name: string
      Default: Slot<Cell> option }

/// A named function a pipeline carries as data (Phase 596, D48): parameters and a body. The body
/// reads its parameters through `Arg` and never reads a column; it may read a pipeline `Param`, a
/// literal, and call another function the pipeline defines. A pipeline carries its own definitions
/// (`Transform.Define`); there is no shared registry (D11 of the accepted plan).
type FnDef =
    { Name: string
      Params: FnParam list
      Body: ColExpr }

/// The error distribution a `Fit` assumes (Phase 592, `DECISIONS.md` D5 of the accepted plan): one
/// verb, the family and the link as data. `Gaussian` under `FitLink.Identity` is ordinary least
/// squares; every other admitted pair is a generalised linear model fitted by IRLS through
/// `Fuaran.Compute.Regression.Glm` — `Poisson` and `Gamma` (Phase 595), `Tweedie` and
/// `QuasiPoisson` (Phase 598), the binomials, inverse Gaussian and the negative binomial under the
/// whole link set (Phase 600, `DECISIONS.md` D58), the pairs `Glm.admitted` names. The wire refuses a
/// pair outside that table when it decodes; one built in memory is refused by name when the step runs
/// (`EvalError.UnsupportedFit`).
[<RequireQualifiedAccess>]
type FitFamily =
    | Gaussian
    | Poisson
    | Gamma
    /// The Tweedie family with variance power `power` (`V(μ) = μ^power`): a power strictly between
    /// zero and one, or not finite, is refused when the step resolves (Phase 598, D57).
    | Tweedie of power: float
    /// Poisson's mean model and iterations, the dispersion estimated (Phase 598).
    | QuasiPoisson
    /// `V(μ) = μ(1 − μ)`, the response a proportion in `[0, 1]`, trials carried as the prior weights
    /// (Phase 600).
    | Binomial
    /// The binomial mean model and iterations, the dispersion estimated (Phase 600).
    | QuasiBinomial
    /// `V(μ) = μ³` (Phase 600).
    | InverseGaussian
    /// `V(μ) = μ + μ²/θ` (Phase 600): `Some θ` fits at that θ; `None` estimates it by maximum likelihood,
    /// and the `Summary` table states it.
    | NegativeBinomial of theta: float option

/// The link function a `Fit` relates the linear predictor to the mean through (Phase 592; the whole
/// set from Phase 600, `DECISIONS.md` D58).
[<RequireQualifiedAccess>]
type FitLink =
    | Identity
    | Log
    | Logit
    | Probit
    | Cloglog
    | Inverse
    | InverseSquare
    | Sqrt

/// The iteration control of a `Fit` (Phase 592): the most iterations and the convergence tolerance,
/// each a `Slot` so a host can bind them per evaluation. Ordinary least squares solves in one pass and
/// reads neither, but both are checked when the step resolves, so a pipeline is refused for an
/// out-of-range control whichever family it names.
type FitControl =
    { MaxIter: Slot<int>; Tol: Slot<float> }

/// Which table a `Fit` step answers (Phase 592). Every one has a closed schema, decided before any row
/// is read.
[<RequireQualifiedAccess>]
type FitOutput =
    /// One row per group and term: the group keys, `term`, `estimate`, `std_error`, `t` and `unit`.
    | Coefficients
    /// One row per group: the group keys, `n`, `df`, `sigma`, `r2`, `rank`, `deviance`,
    /// `null_deviance`, `dispersion`, `iterations` and `converged`.
    | Summary
    /// The step's input rows, in order, with a `fitted` column appended.
    | Fitted
    /// The step's input rows, in order, with a `residual` column appended.
    | Residuals

/// Which standard errors a `Fit` states in its `Coefficients` table's `std_error` and `t` (Phase 598,
/// `DECISIONS.md` D57): the classical `√(φ · diag((RᵀR)⁻¹))`, or a heteroskedasticity-consistent
/// ("sandwich") variant, HC0 to HC3.
[<RequireQualifiedAccess>]
type FitStdErrors =
    /// `√(φ · diag((RᵀR)⁻¹))` over the final solve's `R`.
    | Classical
    /// White's sandwich: `(RᵀR)⁻¹ · Σ ψᵢψᵢᵀ · (RᵀR)⁻¹`, `ψᵢ` row `i`'s score.
    | HC0
    /// HC0 scaled by `n / (n − rank)`.
    | HC1
    /// Each score divided by `√(1 − hᵢ)`, `hᵢ` the row's leverage.
    | HC2
    /// Each score divided by `1 − hᵢ`.
    | HC3

    /// The variant a caller that asks for robust standard errors without naming one is given: `HC3`
    /// (`DECISIONS.md` D57; wire spelling `robust`, which decodes to `hc3`).
    static member Robust: FitStdErrors = FitStdErrors.HC3

/// A regression a pipeline fits (Phase 592; plan §2.4, `DECISIONS.md` of this phase). The response is
/// a column; each predictor is a named `ColExpr`, so an interaction is a `Mul` and a polynomial an
/// integer `Pow`; a string or boolean predictor is a categorical term, one-hot coded against its first
/// level in the cell order. One fit per group of `GroupBy` (none: one fit over every row).
type FitSpec =
    {
        /// The response column: int, float or decimal. A decimal response is estimated through
        /// `Cast(FloatType, Col response)` and every output stated against it is computed in decimal.
        Response: string
        /// The named terms, in coefficient order after the intercept.
        Predictors: (string * ColExpr) list
        /// Whether the design carries the `(Intercept)` column, first.
        Intercept: bool
        /// The grouping columns; one fit per group, groups in first-appearance order.
        GroupBy: string list
        /// The prior weights column, if any: finite and not negative; a zero weight row is in the
        /// design but not counted.
        Weights: string option
        /// An offset added to the linear predictor, if any (its coefficient fixed at one).
        Offset: ColExpr option
        /// A ridge penalty `λ ≥ 0`, if any: one row of `√λ` per non-intercept column, in column order,
        /// folded in after the data rows.
        Ridge: Slot<float> option
        Family: FitFamily
        Link: FitLink
        Control: FitControl
        Output: FitOutput
        /// The standard errors the `Coefficients` table states (Phase 598): classical unless a
        /// sandwich variant is named.
        StdErrors: FitStdErrors
    }

/// Construction and derivations of a `FitSpec` (Phase 592).
[<RequireQualifiedAccess>]
module FitSpec =

    /// The control a spec takes unless it says otherwise: at most 25 iterations, tolerance `1e-8`
    /// (`DECISIONS.md` D49 item 13).
    let defaultControl: FitControl =
        { MaxIter = Slot.Lit 25
          Tol = Slot.Lit 1e-8 }

    /// Ordinary least squares of `response` on `predictors`, with an intercept, ungrouped, unweighted,
    /// answering the coefficients.
    let ols (response: string) (predictors: (string * ColExpr) list) : FitSpec =
        { Response = response
          Predictors = predictors
          Intercept = true
          GroupBy = []
          Weights = None
          Offset = None
          Ridge = None
          Family = FitFamily.Gaussian
          Link = FitLink.Identity
          Control = defaultControl
          Output = FitOutput.Coefficients
          StdErrors = FitStdErrors.Classical }

    /// The response the estimation reads: `Cast(FloatType, Col response)`, the evaluator's own cast, so
    /// an int or a decimal response reaches the factorization exactly as a pipeline's `Cast` would hand
    /// it over, and a float response unchanged.
    let estimationResponse (spec: FitSpec) : ColExpr = Cast(FloatType, Col spec.Response)

    /// Every expression the spec carries, predictors in order and then the offset.
    let exprs (spec: FitSpec) : ColExpr list =
        (spec.Predictors |> List.map snd) @ Option.toList spec.Offset

    /// The spec with `f` applied to every expression it carries.
    let mapExprs (f: ColExpr -> ColExpr) (spec: FitSpec) : FitSpec =
        { spec with
            Predictors = spec.Predictors |> List.map (fun (n, e) -> n, f e)
            Offset = spec.Offset |> Option.map f }

    /// The params the spec's slots name: the ridge penalty, then the control's iteration bound and
    /// tolerance.
    let slotParams (spec: FitSpec) : string list =
        (spec.Ridge |> Option.map Slot.paramName |> Option.defaultValue [])
        @ Slot.paramName spec.Control.MaxIter
        @ Slot.paramName spec.Control.Tol

    /// The spec with every slot param bound in `env` replaced by its literal (a float slot takes a
    /// float cell, or an int cell read as its float; an int slot an int cell).
    let substituteSlots (env: Map<string, Cell>) (spec: FitSpec) : FitSpec =
        let bindFloat (s: Slot<float>) =
            match s with
            | Slot.Param n ->
                match Map.tryFind n env with
                | Some(Float v) -> Slot.Lit v
                | Some(Int v) -> Slot.Lit(float v)
                | _ -> s
            | Slot.Lit _ -> s

        let bindInt (s: Slot<int>) =
            match s with
            | Slot.Param n ->
                match Map.tryFind n env with
                | Some(Int v) -> Slot.Lit v
                | _ -> s
            | Slot.Lit _ -> s

        { spec with
            Ridge = spec.Ridge |> Option.map bindFloat
            Control =
                { MaxIter = bindInt spec.Control.MaxIter
                  Tol = bindFloat spec.Control.Tol } }

/// The scale a `Predict` answers on (Phase 597).
[<RequireQualifiedAccess>]
type PredictScale =
    /// The linear predictor `η = Σ xⱼ·βⱼ + offset`.
    | Link
    /// The mean `μ = g⁻¹(η)` through the link's pinned inverse (`Glm.inverseLink`); its standard error
    /// by the delta method, `|dμ/dη| · se(η)`, and an interval's ends mapped through `g⁻¹` (Phase 600,
    /// D58 item 7).
    | Response

/// Which interval a `Predict` states about a row (Phase 597, `DECISIONS.md` D56).
[<RequireQualifiedAccess>]
type IntervalKind =
    /// For the mean at the row: `η ± q·se_fit`.
    | Confidence
    /// For one new observation at the row: `η ± q·se_pred`. The gaussian family's only.
    | Prediction

/// A `Predict` interval: its kind and its level, a two-sided coverage the t table carries
/// (`0.5`, `0.8`, `0.9`, `0.95`, `0.98`, `0.99` or `0.999`), a `Slot` so a host can bind it.
type PredictInterval =
    { Kind: IntervalKind
      Level: Slot<float> }

/// Where a `Predict` reads its model (Phase 597).
[<RequireQualifiedAccess>]
type PredictModel =
    /// The training rows: the step fits its spec over this source — the factorization a `Fit` step
    /// over the same rows makes, bit for bit — and scores its input rows against every group's fit.
    /// The standard errors and the interval need this form: they read the fit's `R`.
    | Training of DataSource
    /// A table of `FitOutput.Coefficients`'s schema: the spec's group keys, `term` and `estimate`
    /// (other columns are not read). It carries neither `R` nor a categorical term's reference
    /// level, so it scores a point estimate over numeric terms only.
    | Coefficients of DataSource

/// Score rows against a fitted model (Phase 597, `DECISIONS.md` D56). The model's terms, intercept,
/// groups, weights, offset, ridge, family, link and control are the `Fit` spec's (its `Output` is not
/// read): a row is coded exactly as the fit coded its training rows, a categorical term against the
/// fit's own reference level, and its group's fit scores it.
type PredictSpec =
    {
        /// The model's spec, as a `Fit` step of it would carry it.
        Fit: FitSpec
        Model: PredictModel
        Scale: PredictScale
        /// The interval, if any: `lower` and `upper` columns appended after the standard errors.
        Interval: PredictInterval option
    }

/// Construction and derivations of a `PredictSpec` (Phase 597).
[<RequireQualifiedAccess>]
module PredictSpec =

    /// Score against `fit` refitted over the training rows `training`, on the response scale, with no
    /// interval.
    let ofTraining (fit: FitSpec) (training: DataSource) : PredictSpec =
        { Fit = fit
          Model = PredictModel.Training training
          Scale = PredictScale.Response
          Interval = None }

    /// The spec with `f` applied to every expression its model carries.
    let mapExprs (f: ColExpr -> ColExpr) (spec: PredictSpec) : PredictSpec =
        { spec with
            Fit = FitSpec.mapExprs f spec.Fit }

    /// The params the spec's slots name: the model's (`FitSpec.slotParams`), then the interval's
    /// level.
    let slotParams (spec: PredictSpec) : string list =
        FitSpec.slotParams spec.Fit
        @ (spec.Interval
           |> Option.map (fun i -> Slot.paramName i.Level)
           |> Option.defaultValue [])

    /// The spec with every slot param bound in `env` replaced by its literal.
    let substituteSlots (env: Map<string, Cell>) (spec: PredictSpec) : PredictSpec =
        let bindLevel (i: PredictInterval) =
            match i.Level with
            | Slot.Param n ->
                match Map.tryFind n env with
                | Some(Float v) -> { i with Level = Slot.Lit v }
                | _ -> i
            | Slot.Lit _ -> i

        { spec with
            Fit = FitSpec.substituteSlots env spec.Fit
            Interval = spec.Interval |> Option.map bindLevel }

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
    /// Phase 596 (`DECISIONS.md` D48) — a named function, defined for the whole pipeline: every
    /// `ColExpr.Call` of its name, in any step and in any other definition's body, is expanded to
    /// its body before evaluation (`FnDef.expand`). The step itself changes no frame. Wire:
    /// `{"$type":"define","name":…,"params":[{"name":…,"default":<slot>?},…],"body":…}`.
    | Define of FnDef
    /// Phase 592 — fit a regression (plan §2.4, `DECISIONS.md` D53): one factorization per group by the
    /// streaming Givens QR of `Fuaran.Compute.Regression`, answering the table `FitOutput` names.
    /// Wire: `{"$type":"fit","response":…,"predictors":[{"name":…,"expr":…},…],"intercept":…,
    /// "groupBy":[…],"weights":…?,"offset":…?,"ridge":<slot>?,"family":…,"link":…,
    /// "control":{"maxIter":<slot>,"tol":<slot>},"output":…}`.
    | Fit of FitSpec
    /// Phase 597 — score the step's input rows against a fitted model (`DECISIONS.md` D56), appending
    /// `predicted` and, from training rows, `se_fit`, `se_pred` and an interval's `lower` and `upper`.
    /// Wire: `{"$type":"predict","fit":{<the fit step's members>},"model":{"from":"training"|
    /// "coefficients","source":…},"scale":"link"|"response","interval":{"kind":"confidence"|
    /// "prediction","level":<slot>}?}`.
    | Predict of PredictSpec

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
        | Convert(x, _) -> paramNames x
        // Phase 596: a call's params are its arguments'; the definition's own are the `Define`'s.
        | Arg _ -> []
        | Call(_, args) -> args |> List.collect paramNames
        | Interpolate(x, _, _, _) -> paramNames x

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
        | Convert(x, u) -> Convert(substitute env x, u)
        | Arg _ -> e
        | Call(name, args) -> Call(name, args |> List.map (substitute env))
        | Interpolate(x, knots, kind, outside) -> Interpolate(substitute env x, knots, kind, outside)

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
        | Convert(x, u) -> Convert(substituteListParams listEnv x, u)
        | Arg _ -> e
        | Call(name, args) -> Call(name, args |> List.map (substituteListParams listEnv))
        | Interpolate(x, knots, kind, outside) -> Interpolate(substituteListParams listEnv x, knots, kind, outside)

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
        | Convert(x, u) -> Convert(go x, u)
        | Arg _ -> e
        | Call(name, args) -> Call(name, args |> List.map go)
        | Interpolate(x, knots, kind, outside) -> Interpolate(go x, knots, kind, outside)

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
        | Convert(x, _) -> usesNow x
        | Arg _ -> false
        | Call(_, args) -> args |> List.exists usesNow
        | Interpolate(x, _, _, _) -> usesNow x

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
        // Phase 596: a definition's defaults and the params its body reads — the params every call
        // of it may read once expanded.
        | Define fd ->
            (fd.Params
             |> List.collect (fun p -> p.Default |> Option.map Slot.paramName |> Option.defaultValue []))
            @ ColExpr.paramNames fd.Body
        // Phase 592: the terms' and the offset's params, then the slots' (`FitSpec.slotParams`).
        | Fit spec ->
            (FitSpec.exprs spec |> List.collect ColExpr.paramNames)
            @ FitSpec.slotParams spec
        // Phase 597: the model's, as its `Fit` would report them, then the interval's level.
        | Predict spec ->
            (FitSpec.exprs spec.Fit |> List.collect ColExpr.paramNames)
            @ PredictSpec.slotParams spec
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
            // Phase 596: a default param binds to any cell, as an expression `Param` does.
            | Define fd ->
                let bindCell (p: FnParam) =
                    match p.Default with
                    | Some(Slot.Param n) ->
                        match Map.tryFind n env with
                        | Some c -> { p with Default = Some(Slot.Lit c) }
                        | None -> p
                    | _ -> p

                Define
                    { fd with
                        Params = fd.Params |> List.map bindCell
                        Body = ColExpr.substitute env fd.Body }
            | Fit spec -> Fit(spec |> FitSpec.mapExprs (ColExpr.substitute env) |> FitSpec.substituteSlots env)
            | Predict spec ->
                Predict(
                    spec
                    |> PredictSpec.mapExprs (ColExpr.substitute env)
                    |> PredictSpec.substituteSlots env
                )
            | other -> other)

    /// Substitute every LIST param bound in `listEnv` through the whole pipeline (Phase 91) —
    /// the list-valued twin of `substitute`.
    let substituteListParams (listEnv: Map<string, Cell list>) (pipeline: Transform list) : Transform list =
        pipeline
        |> List.map (fun t ->
            match t with
            | Filter p -> Filter(ColExpr.substituteListParams listEnv p)
            | Derive(n, e) -> Derive(n, ColExpr.substituteListParams listEnv e)
            | Define fd ->
                Define
                    { fd with
                        Body = ColExpr.substituteListParams listEnv fd.Body }
            | Fit spec -> Fit(FitSpec.mapExprs (ColExpr.substituteListParams listEnv) spec)
            | Predict spec -> Predict(PredictSpec.mapExprs (ColExpr.substituteListParams listEnv) spec)
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
            | Define fd ->
                Define
                    { fd with
                        Body = ColExpr.substituteWithPinned pinned fd.Body }
            | Fit spec -> Fit(FitSpec.mapExprs (ColExpr.substituteWithPinned pinned) spec)
            | Predict spec -> Predict(PredictSpec.mapExprs (ColExpr.substituteWithPinned pinned) spec)
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
            | Fit spec -> FitSpec.slotParams spec
            | Predict spec -> PredictSpec.slotParams spec
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
            | Define fd -> ColExpr.usesNow fd.Body
            | Fit spec -> FitSpec.exprs spec |> List.exists ColExpr.usesNow
            | Predict spec -> FitSpec.exprs spec.Fit |> List.exists ColExpr.usesNow
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
    /// Two units met where the rule requires one (Phase 429, `DECISIONS.md` D29): an addition, a
    /// subtraction, a comparison, a `Coalesce` / `Case` / `Least` / `Greatest` / `InList` across
    /// two stated units that are not one unit (a different scale of one dimension included — `km`
    /// beside `m` is refused until converted), a `Convert` between two dimensions, a join key pair,
    /// or a set-op column pair. Names the STEP (`derive 'speed'`, `filter`, `join key 'a' = 'b'`,
    /// `union column 'x'`), the EXPRESSION the two units met in, and both units by their canonical
    /// text. Static: it is decided over the step's schema before any row is read, so an empty frame
    /// is refused exactly as a full one.
    | UnitMismatch of step: string * expr: ColExpr * left: string * right: string
    /// A pipeline's named functions refused at registration (Phase 596, `DECISIONS.md` D48): a call
    /// of a name no `Define` gives (`detail` enumerates the defined names), two definitions of one
    /// name, a parameter named twice, a body that reads a column, an `Arg` naming no parameter of
    /// its definition, an `Arg` outside any definition, or a `Call` that reached evaluation
    /// unexpanded. Names the function. A wrong argument count is the existing `ArityError`.
    | FunctionError of name: string * detail: string
    /// A pipeline's functions call one another in a cycle (Phase 596, D48), so expansion would not
    /// terminate: refused at registration, naming the cycle in call order, first name repeated last.
    | RecursiveFunction of cycle: string list
    /// An expression whose expansion would exceed the bound (Phase 596, D48: `FnDef.maxExpandedNodes`
    /// nodes), refused at registration before the expansion is built. Names where — the step, or
    /// the function whose expanded body alone exceeds it — and the bound.
    | ExpansionTooLarge of at: string * bound: int
    /// An `Interpolate`'s knots break the rule (Phase 596, D48): fewer than two, an `x` that does
    /// not strictly increase, or a coordinate that is not finite. Static: refused when the step
    /// resolves, before any row is read, so an empty frame is refused exactly as a full one.
    | InvalidKnots of detail: string
    /// A `Fit` whose family and link this version does not evaluate (Phase 592, D53): every pairing but
    /// `Gaussian` under `Identity` and, since Phase 595, `Poisson` and `Gamma` under `Log` is typed in
    /// `FitSpec` and refused here by name, before any row is read. Names both by their wire spellings.
    | UnsupportedFit of family: string * link: string
    /// A `Fit` refused (Phase 592, D53): a spec that cannot be fitted as written (no design column, a
    /// term named twice, an output column that already exists, a predictor of a type the bridge does
    /// not code, a ridge penalty or a control out of range, a positive ridge penalty under a family but
    /// `Gaussian`), a row the bridge refuses (a non-finite response, term or offset, a negative or
    /// non-finite weight, a response outside its family's range), naming the step's input row, or an
    /// IRLS fit that did not converge (Phase 595). A mean that leaves its range is the separation
    /// `OverflowError`.
    | FitError of detail: string

/// A curve prepared once per step (Phase 596, `DECISIONS.md` D48): the knots as arrays, and for a
/// monotone cubic the tangent at every knot under the pinned slope rule.
type internal PreparedCurve =
    {
        Xs: float[]
        Ys: float[]
        /// The tangent at each knot (`InterpKind.MonotoneCubic` only; empty for the other kinds).
        Slopes: float[]
        Kind: InterpKind
        Outside: InterpOutside
    }

/// `ColExpr.Interpolate`'s arithmetic (Phase 596, D48) — one definition, read by the reference
/// evaluator and the compiled form alike. Every value is computed from IEEE additions,
/// subtractions, multiplications, divisions and comparisons in the order written here, so the bytes
/// are the same on every host; no square root, no fused multiply-add, no host library function.
[<RequireQualifiedAccess>]
module internal Interpolation =

    let private finite (v: float) =
        not (System.Double.IsNaN v || System.Double.IsInfinity v)

    /// The knot rule: at least two knots; every coordinate finite; `x` strictly increasing; and every
    /// gap, rise and secant finite, so no arithmetic below meets an infinity the knots did not state.
    let validate (knots: (float * float) list) : Result<unit, EvalError> =
        let xs = knots |> List.map fst |> Array.ofList
        let ys = knots |> List.map snd |> Array.ofList
        let n = xs.Length

        let refused (detail: string) =
            Error(InvalidKnots("interpolate: " + detail))

        if n < 2 then
            refused (string n + " knot(s); a curve needs at least two")
        else
            let rec check k =
                if k = n then
                    Ok()
                elif not (finite xs[k] && finite ys[k]) then
                    refused ("knot " + string k + " is not finite")
                elif k > 0 && not (xs[k] > xs[k - 1]) then
                    refused (
                        "knot "
                        + string k
                        + "'s x does not strictly increase on knot "
                        + string (k - 1)
                        + "'s"
                    )
                elif
                    k > 0
                    && not (
                        finite (xs[k] - xs[k - 1])
                        && finite (ys[k] - ys[k - 1])
                        && finite ((ys[k] - ys[k - 1]) / (xs[k] - xs[k - 1]))
                    )
                then
                    refused (
                        "the segment from knot "
                        + string (k - 1)
                        + " to knot "
                        + string k
                        + " overflows"
                    )
                else
                    check (k + 1)

            check 0

    /// The Fritsch–Carlson tangents (D48's slope rule, pinned): the secant `d` of every segment; at
    /// the two ends the end segment's secant, and at an interior knot the mean of its two secants
    /// where they share a strict sign and zero otherwise; then, segment by segment from the left, a
    /// flat segment sets both its tangents to zero, and a tangent more than three times its
    /// segment's secant is brought down to three times it — the square of Fritsch and Carlson's
    /// monotonicity region, which needs no square root.
    let private slopes (xs: float[]) (ys: float[]) : float[] =
        let n = xs.Length
        let d = Array.init (n - 1) (fun k -> (ys[k + 1] - ys[k]) / (xs[k + 1] - xs[k]))
        let m: float[] = Array.zeroCreate n
        m[0] <- d[0]
        m[n - 1] <- d[n - 2]

        for k in 1 .. n - 2 do
            m[k] <-
                if (d[k - 1] > 0.0 && d[k] > 0.0) || (d[k - 1] < 0.0 && d[k] < 0.0) then
                    (d[k - 1] + d[k]) / 2.0
                else
                    0.0

        for k in 0 .. n - 2 do
            if d[k] = 0.0 then
                m[k] <- 0.0
                m[k + 1] <- 0.0
            else
                if m[k] / d[k] > 3.0 then
                    m[k] <- 3.0 * d[k]

                if m[k + 1] / d[k] > 3.0 then
                    m[k + 1] <- 3.0 * d[k]

        m

    /// The curve, once per step: the knots checked and, for a monotone cubic, the tangents computed.
    let prepare
        (knots: (float * float) list)
        (kind: InterpKind)
        (outside: InterpOutside)
        : Result<PreparedCurve, EvalError> =
        validate knots
        |> Result.map (fun () ->
            let xs = knots |> List.map fst |> Array.ofList
            let ys = knots |> List.map snd |> Array.ofList

            { Xs = xs
              Ys = ys
              Slopes =
                match kind with
                | InterpKind.MonotoneCubic -> slopes xs ys
                | InterpKind.Linear
                | InterpKind.Step -> [||]
              Kind = kind
              Outside = outside })

    /// The largest `k` with `xs[k] <= x`, for `xs[0] <= x <= xs[last]`.
    let private segment (xs: float[]) (x: float) : int =
        let mutable lo = 0
        let mutable hi = xs.Length - 1

        while lo < hi do
            let mid = (lo + hi + 1) / 2

            if xs[mid] <= x then lo <- mid else hi <- mid - 1

        lo

    /// The curve at `x`: `None` where the answer is `Null` (an operand outside the knots under
    /// `InterpOutside.Null`). A knot's own `x` answers its own `y` exactly; a NaN answers NaN.
    let at (c: PreparedCurve) (x: float) : float option =
        let last = c.Xs.Length - 1

        if System.Double.IsNaN x then
            Some nan
        elif x < c.Xs[0] || x > c.Xs[last] then
            match c.Outside with
            | InterpOutside.Null -> None
            | InterpOutside.Clamp -> Some(if x < c.Xs[0] then c.Ys[0] else c.Ys[last])
        else
            let k = segment c.Xs x

            if x = c.Xs[k] then
                Some c.Ys[k]
            else
                let x0 = c.Xs[k]
                let x1 = c.Xs[k + 1]
                let y0 = c.Ys[k]
                let y1 = c.Ys[k + 1]

                match c.Kind with
                | InterpKind.Step -> Some y0
                | InterpKind.Linear -> Some(y0 + (y1 - y0) * ((x - x0) / (x1 - x0)))
                | InterpKind.MonotoneCubic ->
                    let h = x1 - x0
                    let t = (x - x0) / h
                    let d = (y1 - y0) / h
                    let m0 = c.Slopes[k]
                    let m1 = c.Slopes[k + 1]
                    let c2 = 3.0 * d - 2.0 * m0 - m1
                    let c3 = m0 + m1 - 2.0 * d
                    let y = y0 + h * (t * (m0 + t * (c2 + t * c3)))
                    // The cubic lies between its two knots' values on a monotone segment; the clamp
                    // removes only a rounding overshoot, so the curve never steps past its knots.
                    let lo = min y0 y1
                    let hi = max y0 y1

                    Some(
                        if y < lo then lo
                        elif y > hi then hi
                        else y
                    )

/// The two-sided Student t quantiles a `Predict` interval reads (Phase 597, `DECISIONS.md` D56): for a
/// level `L` and `ν` residual degrees of freedom, the `t` with `P(|T| < t) = L`, the `(1 + L)/2`
/// quantile of Student's t. One row per tabled `ν` (1 to 30, 40, 50, 60, 80, 100, 120, 200, 500 and
/// 1000) and the normal row (`ν = ∞`), one column per level. Every entry is the nearest double to the
/// exact quantile, computed once in 70-digit decimal arithmetic from Abramowitz and Stegun's closed
/// forms 26.7.3 and 26.7.4 (the error function for the normal row) and checked by quadrature; the
/// table is data, so no transcendental function is on the path and every host reads the same bits.
/// Between rows the quantile is read by `Interpolate` (`InterpKind.Linear`) in `1/ν`, the normal row
/// at `1/ν = 0`, so a tabled `ν` answers its row exactly and a `ν` above 1000 reads between the 1000
/// row and the normal one.
[<RequireQualifiedAccess>]
module internal StudentT =

    /// The tabled coverage levels, ascending.
    let levels: float[] = [| 0.5; 0.8; 0.9; 0.95; 0.98; 0.99; 0.999 |]

    /// `(ν, the quantile at each level)`, `ν` ascending; `0` is the normal row (`ν = ∞`).
    let rows: (int * float[]) list =
        [ 1,
          [| 1.0
             3.0776835371752536
             6.313751514675043
             12.706204736174705
             31.82051595377396
             63.65674116287158
             636.6192487687197 |]
          2,
          [| 0.816496580927726
             1.8856180831641267
             2.9199855803537256
             4.302652729749464
             6.964556734283274
             9.924843200918293
             31.59905457644362 |]
          3,
          [| 0.7648923284043453
             1.6377443536962102
             2.3533634348018238
             3.1824463052837095
             4.5407028585681335
             5.840909309733357
             12.923978636687483 |]
          4,
          [| 0.7406970841126826
             1.5332062740589438
             2.1318467863266504
             2.7764451051977943
             3.746947387979197
             4.604094871349993
             8.610301581379275 |]
          5,
          [| 0.7266868438004227
             1.475884048824481
             2.0150483733330242
             2.5705818356363155
             3.3649299989072188
             4.032142983555228
             6.86882662588111 |]
          6,
          [| 0.7175581964914126
             1.4397557472651483
             1.9431802805153031
             2.44691185114497
             3.1426684032909833
             3.70742802132478
             5.9588161788187595 |]
          7,
          [| 0.7111417780817864
             1.4149239276505086
             1.8945786050900073
             2.3646242515927853
             2.9979515668685286
             3.499483297350494
             5.407882520861725 |]
          8,
          [| 0.7063866126448386
             1.3968153097438647
             1.8595480375308984
             2.3060041352041667
             2.8964594477096224
             3.3553873313333953
             5.041305433373368 |]
          9,
          [| 0.7027221467513264
             1.3830287383966322
             1.8331129326562372
             2.2621571627982053
             2.821437925025809
             3.2498355415921263
             4.780912585931139 |]
          10,
          [| 0.6998120613124317
             1.3721836411103356
             1.8124611228116765
             2.228138851986275
             2.763769458112696
             3.1692726726169513
             4.586893858702636 |]
          11,
          [| 0.6974453275598815
             1.3634303180205407
             1.7958848187040442
             2.2009851600916397
             2.7180791838138623
             3.105806515539281
             4.436979338234449 |]
          12,
          [| 0.6954828655117926
             1.3562173340232053
             1.78228755564932
             2.178812829667229
             2.680997993120915
             3.054539589392902
             4.3177912836061845 |]
          13,
          [| 0.6938293042354409
             1.350171288780055
             1.770933395986873
             2.1603686564627926
             2.6503088379121915
             3.0122758387165787
             4.220831727707121 |]
          14,
          [| 0.6924170695700052
             1.3450303744546506
             1.7613101357748921
             2.144786687917804
             2.624494067590052
             2.976842734370835
             4.140454112738203 |]
          15,
          [| 0.6911969489584898
             1.3406056078504556
             1.7530503556925736
             2.1314495455597755
             2.6024802950111234
             2.946712883475239
             4.072765195903791 |]
          16,
          [| 0.6901322538105608
             1.3367571673273146
             1.74588367627625
             2.1199052992212546
             2.5834871852759913
             2.9207816224251
             4.014996327184056 |]
          17,
          [| 0.6891950751539389
             1.3333793897216268
             1.739606726075073
             2.109815577833317
             2.566933983724718
             2.8982305196774187
             3.9651262721190315 |]
          18,
          [| 0.6883638064662002
             1.330390943569909
             1.7340636066175388
             2.1009220402410387
             2.5523796301822514
             2.878440472738608
             3.9216458250851596 |]
          19,
          [| 0.6876214602039603
             1.3277282090267983
             1.7291328115213696
             2.0930240544083096
             2.5394831906239625
             2.860934606464979
             3.883405852592083 |]
          20,
          [| 0.6869544964488035
             1.3253407069850462
             1.7247182429207872
             2.085963447265865
             2.5279770027415736
             2.8453397097861086
             3.8495162749308274 |]
          21,
          [| 0.6863519907269535
             1.3231878738651726
             1.7207429028118786
             2.0796138447276804
             2.5176480160447423
             2.83135955802305
             3.8192771642744625 |]
          22,
          [| 0.6858050317218864
             1.3212367416133617
             1.7171443743802428
             2.0738730679040263
             2.5083245528990803
             2.8187560606001436
             3.792130671698391 |]
          23,
          [| 0.6853062780612957
             1.319460239816162
             1.713871527747048
             2.0686576104190486
             2.499866739494668
             2.807335683769999
             3.7676268043117807 |]
          24,
          [| 0.6848496272369818
             1.3178359336731496
             1.7108820799094284
             2.063898561628026
             2.492159473157756
             2.796939504774456
             3.7453986192900524 |]
          25,
          [| 0.6844299649042703
             1.3163450726738701
             1.7081407612518993
             2.0595385527532977
             2.485107175410763
             2.7874358136769706
             3.72514394972865 |]
          26,
          [| 0.6840429726828732
             1.3149718642705168
             1.7056179197592731
             2.055529438642873
             2.478629823591242
             2.778714533329683
             3.7066117434809107 |]
          27,
          [| 0.683684979131031
             1.3137029128292739
             1.7032884457221271
             2.0518305164802855
             2.472659911956007
             2.770682957122212
             3.689591713459236 |]
          28,
          [| 0.6833528429885058
             1.3125267815926667
             1.7011309342659315
             2.048407141795245
             2.4671400979674725
             2.7632624554614447
             3.6739064007012767 |]
          29,
          [| 0.6830438608216132
             1.311433647301551
             1.6991270265334977
             2.0452296421327043
             2.462021360150412
             2.7563859036706053
             3.659405019466333 |]
          30,
          [| 0.6827556933212926
             1.3104150253913955
             1.6972608865939578
             2.042272456301238
             2.4572615424005915
             2.7499956535672254
             3.645958635042022 |]
          40,
          [| 0.680672717164449
             1.3030770526071946
             1.6838510133356526
             2.0210753903062733
             2.4232567793348583
             2.7044592674331627
             3.550965760863311 |]
          50,
          [| 0.679428200326346
             1.2987136941948096
             1.6759050251630976
             2.008559112100761
             2.403271916674172
             2.6777932709408443
             3.496012881811139 |]
          60,
          [| 0.6786007206481358
             1.2958210935157308
             1.6706488649046365
             2.0002978220142604
             2.3901194726249133
             2.660283028855037
             3.460200469196356 |]
          80,
          [| 0.6775688463948343
             1.2922235830591335
             1.6641245785896674
             1.9900634212544461
             2.3738682729673433
             2.638690596344183
             3.416337458476946 |]
          100,
          [| 0.6769510430114715
             1.290074761346516
             1.6602343260853396
             1.9839715185235522
             2.364217366238482
             2.6258905214380177
             3.39049131116423 |]
          120,
          [| 0.6765397249125202
             1.288646233656378
             1.6576508993552357
             1.979930405082441
             2.3578246126487556
             2.617421145106866
             3.3734537685625003 |]
          200,
          [| 0.6757184114042202
             1.285798793994801
             1.6525081009108775
             1.9718962236339095
             2.3451370822594684
             2.600634436191558
             3.3398354062756783 |]
          500,
          [| 0.674980737965939
             1.2832470207103743
             1.6479068539295112
             1.9647198374673678
             2.333828955352305
             2.585697835141942
             3.31009115152267 |]
          1000,
          [| 0.6747351646070094
             1.2823987214609245
             1.6463788172854648
             1.9623390808264085
             2.330082674755513
             2.580754698065951
             3.300282648423913 |]
          0,
          [| 0.6744897501960817
             1.2815515655446004
             1.6448536269514726
             1.9599639845400543
             2.326347874040841
             2.575829303548901
             3.290526731491895 |] ]

    /// The knots of one level's curve, `x = 1/ν` ascending: the normal row at `0`, then `ν` descending.
    let private knots (column: int) : (float * float) list =
        let normal = rows |> List.find (fun (v, _) -> v = 0) |> snd
        let tabled = rows |> List.filter (fun (v, _) -> v > 0) |> List.rev

        (0.0, normal[column])
        :: (tabled |> List.map (fun (v, qs) -> 1.0 / float v, qs[column]))

    /// One prepared curve per level.
    let private curves: PreparedCurve[] =
        Array.init levels.Length (fun c ->
            match Interpolation.prepare (knots c) InterpKind.Linear InterpOutside.Clamp with
            | Ok curve -> curve
            | Error _ -> failwith "the t table is malformed")

    /// The quantile at `level` for `df` residual degrees of freedom (`None`: the normal row); `None`
    /// where the level is not tabled or `df` is not positive.
    let quantile (level: float) (df: int option) : float option =
        match Array.tryFindIndex (fun l -> l = level) levels with
        | None -> None
        | Some c ->
            match df with
            | None -> Interpolation.at curves[c] 0.0
            | Some v when v > 0 -> Interpolation.at curves[c] (1.0 / float v)
            | Some _ -> None


/// Named functions over a pipeline (Phase 596, `DECISIONS.md` D48): registration and inline
/// expansion. A pipeline's `Transform.Define` steps are its functions; `expand` checks them and every
/// call, then replaces every `ColExpr.Call` with the callee's body — each `Arg` replaced by the call's
/// argument, or the parameter's default — and drops the definitions. What evaluates is the
/// expanded pipeline, so a call means exactly its body: its typing, units, exactness and incremental
/// behaviour are the body's, and nothing new reaches the evaluator.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FnDef =

    /// The bound on an expanded expression (D48): §21.8's 512 nodes per expression, the limit the
    /// proof model states as `Limits.max_expr_nodes`. A call is refused where its expansion would
    /// exceed it, so a function never builds an expression the specification refuses, and the size is
    /// computed from the definitions before any expansion is built — an exponential nest is refused
    /// in time linear in its text.
    let internal maxExpandedNodes = 512

    /// Every immediate sub-expression, left to right.
    let private children (e: ColExpr) : ColExpr list =
        match e with
        | Col _
        | Lit _
        | Param _
        | Now _
        | Arg _ -> []
        | Binary(_, a, b)
        | Quotient(a, b, _) -> [ a; b ]
        | Not x
        | IsNull x
        | Cast(_, x)
        | InParam(x, _)
        | Rounded(x, _)
        | Convert(x, _)
        | Interpolate(x, _, _, _) -> [ x ]
        | Coalesce xs
        | ApplyFn(_, xs)
        | Call(_, xs) -> xs
        | Case(cases, els) -> (cases |> List.collect (fun (w, t) -> [ w; t ])) @ [ els ]
        | InList(x, items) -> x :: items

    /// Every node, pre-order.
    let rec private nodes (e: ColExpr) : ColExpr list = e :: (children e |> List.collect nodes)

    let rec private firstError (checks: (unit -> Result<unit, EvalError>) list) : Result<unit, EvalError> =
        match checks with
        | [] -> Ok()
        | c :: rest ->
            match c () with
            | Ok() -> firstError rest
            | Error e -> Error e

    /// The calls in an expression against the definitions: each names one, with an argument count
    /// it admits — at most one per parameter, and one for every parameter up to the last that has
    /// no default.
    let private checkCalls (defs: Map<string, FnDef>) (e: ColExpr) : Result<unit, EvalError> =
        nodes e
        |> List.choose (function
            | Call(name, args) -> Some(name, List.length args)
            | _ -> None)
        |> List.map (fun (name, got) ->
            fun () ->
                match Map.tryFind name defs with
                | None ->
                    Error(
                        FunctionError(
                            name,
                            "a call names no definition; defined: "
                            + String.concat ", " (defs |> Map.toList |> List.map fst)
                        )
                    )
                | Some fd ->
                    let total = List.length fd.Params

                    let required =
                        fd.Params
                        |> List.mapi (fun i p -> if Option.isNone p.Default then i + 1 else 0)
                        |> List.fold max 0

                    if got > total then
                        Error(ArityError(name, total, got))
                    elif got < required then
                        Error(ArityError(name, required, got))
                    else
                        Ok())
        |> firstError

    /// One definition's own text: its parameters named once each, and a body that reads no column
    /// and only its own parameters.
    let private checkDefinition (fd: FnDef) : Result<unit, EvalError> =
        let names = fd.Params |> List.map (fun p -> p.Name)

        let repeated =
            names |> List.countBy id |> List.tryFind (fun (_, c) -> c > 1) |> Option.map fst

        match repeated with
        | Some p -> Error(FunctionError(fd.Name, "parameter '" + p + "' is named twice"))
        | None ->
            nodes fd.Body
            |> List.tryPick (function
                | Col c ->
                    Some(
                        FunctionError(
                            fd.Name,
                            "the body reads column '" + c + "'; a body reads its parameters through Arg"
                        )
                    )
                | Arg a when not (List.contains a names) ->
                    Some(
                        FunctionError(
                            fd.Name,
                            "Arg '" + a + "' names no parameter; parameters: " + String.concat ", " names
                        )
                    )
                | _ -> None)
            |> Option.map Error
            |> Option.defaultValue (Ok())

    /// The call graph's first cycle, in definition order: the names in call order, the first repeated
    /// last. Every name a body calls is defined (checked first).
    let private cycle (order: FnDef list) (defs: Map<string, FnDef>) : string list option =
        let callees (name: string) =
            nodes defs[name].Body
            |> List.choose (function
                | Call(n, _) -> Some n
                | _ -> None)
            |> List.distinct

        // Depth-first from each definition; `path` is the chain from the root, nearest last.
        let rec visit (path: string list) (finished: Set<string>) (name: string) : Result<Set<string>, string list> =
            if List.contains name path then
                let from = path |> List.findIndex ((=) name)
                Error((path |> List.skip from) @ [ name ])
            elif finished.Contains name then
                Ok finished
            else
                let rec over fin =
                    function
                    | [] -> Ok(Set.add name fin)
                    | c :: rest ->
                        match visit (path @ [ name ]) fin c with
                        | Ok fin' -> over fin' rest
                        | Error cyc -> Error cyc

                over finished (callees name)

        let rec roots fin =
            function
            | [] -> None
            | (fd: FnDef) :: rest ->
                match visit [] fin fd.Name with
                | Ok fin' -> roots fin' rest
                | Error cyc -> Some cyc

        roots Set.empty order

    /// The size of an expression once expanded, as `constant + Σ coefficient(p) · size(argument p)`
    /// over the enclosing definition's parameters — linear because an argument is substituted
    /// whole wherever its parameter is read. Every number saturates one past the bound, which is all
    /// a refusal needs to know, so no nest, however deep, overflows the arithmetic.
    type private Size =
        { Const: int64
          Coeffs: Map<string, int64> }

    let private cap = int64 maxExpandedNodes + 1L

    let private sat (v: int64) = if v > cap then cap else v

    let private sizeAdd (a: Size) (b: Size) : Size =
        { Const = sat (a.Const + b.Const)
          Coeffs =
            (a.Coeffs, b.Coeffs)
            ||> Map.fold (fun acc k v -> acc |> Map.add k (sat (v + (Map.tryFind k acc |> Option.defaultValue 0L)))) }

    let private sizeScale (k: int64) (a: Size) : Size =
        { Const = sat (k * a.Const)
          Coeffs = a.Coeffs |> Map.map (fun _ v -> sat (k * v)) }

    let private one: Size = { Const = 1L; Coeffs = Map.empty }

    /// An expression's size over its enclosing definition's parameters, given each callee's own
    /// size over ITS parameters (`known`, filled callee first).
    let rec private sizeOf (defs: Map<string, FnDef>) (known: Map<string, Size>) (e: ColExpr) : Size =
        match e with
        | Arg a ->
            { Const = 0L
              Coeffs = Map.ofList [ a, 1L ] }
        | Call(name, args) ->
            let fd = defs[name]
            let s = known[name]

            fd.Params
            |> List.mapi (fun i p ->
                let argSize =
                    match List.tryItem i args with
                    | Some a -> sizeOf defs known a
                    // A default is one node: a literal or a param.
                    | None -> one

                sizeScale (Map.tryFind p.Name s.Coeffs |> Option.defaultValue 0L) argSize)
            |> List.fold sizeAdd { Const = s.Const; Coeffs = Map.empty }
        | _ -> children e |> List.map (sizeOf defs known) |> List.fold sizeAdd one

    /// A size with every argument one node — the expanded size of a body on its own.
    let private alone (s: Size) : int64 =
        s.Coeffs |> Map.fold (fun acc _ v -> sat (acc + v)) s.Const

    let rec private hasCalls (e: ColExpr) : bool =
        match e with
        | Call _
        | Arg _ -> true
        | _ -> children e |> List.exists hasCalls

    /// The expansion itself, over checked definitions: `bindings` maps the enclosing definition's
    /// parameters to their (already expanded) arguments.
    let rec private inline' (defs: Map<string, FnDef>) (bindings: Map<string, ColExpr>) (e: ColExpr) : ColExpr =
        let go = inline' defs bindings

        match e with
        | Col _
        | Lit _
        | Param _
        | Now _ -> e
        | Arg a -> bindings[a]
        | Call(name, args) ->
            let fd = defs[name]
            let args' = args |> List.map go

            let bound =
                fd.Params
                |> List.mapi (fun i p ->
                    p.Name,
                    match List.tryItem i args' with
                    | Some a -> a
                    | None ->
                        match p.Default with
                        | Some(Slot.Lit c) -> Lit c
                        | Some(Slot.Param n) -> Param n
                        // Unreachable over a checked call: the arity check refused it.
                        | None -> Lit Null)
                |> Map.ofList

            inline' defs bound fd.Body
        | Binary(op, a, b) -> Binary(op, go a, go b)
        | Not x -> Not(go x)
        | Coalesce xs -> Coalesce(List.map go xs)
        | Case(cases, els) -> Case(cases |> List.map (fun (w, t) -> go w, go t), go els)
        | Cast(ty, x) -> Cast(ty, go x)
        | ApplyFn(fn, xs) -> ApplyFn(fn, List.map go xs)
        | InList(x, items) -> InList(go x, List.map go items)
        | IsNull x -> IsNull(go x)
        | InParam(x, n) -> InParam(go x, n)
        | Quotient(a, b, r) -> Quotient(go a, go b, r)
        | Rounded(x, r) -> Rounded(go x, r)
        | Convert(x, u) -> Convert(go x, u)
        | Interpolate(x, knots, kind, outside) -> Interpolate(go x, knots, kind, outside)

    /// Callee first: every definition's size over its parameters, refusing one whose body alone
    /// expands past the bound. The call graph is acyclic (checked first), so every pass sizes one.
    let rec private sized
        (defs: Map<string, FnDef>)
        (known: Map<string, Size>)
        (pending: FnDef list)
        : Result<Map<string, Size>, EvalError> =
        match pending with
        | [] -> Ok known
        | _ ->
            let ready, waiting =
                pending
                |> List.partition (fun fd ->
                    nodes fd.Body
                    |> List.forall (function
                        | Call(n, _) -> known.ContainsKey n
                        | _ -> true))

            let rec add known' =
                function
                | [] -> sized defs known' waiting
                | (fd: FnDef) :: rest ->
                    let s = sizeOf defs known' fd.Body

                    if alone s > int64 maxExpandedNodes then
                        Error(ExpansionTooLarge("function '" + fd.Name + "'", maxExpandedNodes))
                    else
                        add (Map.add fd.Name s known') rest

            add known ready

    /// One step expression's checks: no `Arg` outside a body, every call admitted, and the expansion
    /// within the bound.
    let private checkStep
        (defs: Map<string, FnDef>)
        (known: Map<string, Size>)
        (at: string)
        (e: ColExpr)
        : Result<unit, EvalError> =
        match
            nodes e
            |> List.tryPick (function
                | Arg a -> Some a
                | _ -> None)
        with
        | Some a -> Error(FunctionError(a, "Arg read in a step, outside any function body (" + at + ")"))
        | None ->
            checkCalls defs e
            |> Result.bind (fun () ->
                if hasCalls e && (sizeOf defs known e).Const > int64 maxExpandedNodes then
                    Error(ExpansionTooLarge(at, maxExpandedNodes))
                else
                    Ok())

    /// Register the pipeline's functions and expand every call (Phase 596, D48). Refused, at the
    /// first failure in this order: a name defined twice, a definition's own text (`FunctionError`:
    /// a parameter named twice, a body reading a column or an `Arg` naming no parameter), a call
    /// naming no definition (`FunctionError`) or passing too many or too few arguments
    /// (`ArityError`), a cycle in the call graph (`RecursiveFunction`), a definition whose body
    /// alone expands past the bound (`ExpansionTooLarge`), and then, step by step, an `Arg` outside
    /// any body (`FunctionError`), a call refused as above, or an expansion past the bound
    /// (`ExpansionTooLarge`). Answered: the pipeline with every `Define` removed and every `Call`
    /// replaced, which a pipeline with neither is unchanged by. Pure and total: nothing evaluates,
    /// and every other refusal a step can make is still the step's own, at evaluation.
    let expand (pipeline: Transform list) : Result<Transform list, EvalError> =
        let order =
            pipeline
            |> List.choose (function
                | Define fd -> Some fd
                | _ -> None)

        let stepExprs =
            pipeline
            |> List.collect (function
                | Filter p -> [ "filter", p ]
                | Derive(name, e) -> [ "derive '" + name + "'", e ]
                // Phase 592: a fit's terms and offset are expressions of the step like any other.
                | Fit spec -> FitSpec.exprs spec |> List.map (fun e -> "fit", e)
                | Predict spec -> FitSpec.exprs spec.Fit |> List.map (fun e -> "predict", e)
                | _ -> [])


        if List.isEmpty order && not (stepExprs |> List.exists (snd >> hasCalls)) then
            Ok pipeline
        else
            let duplicate =
                order |> List.countBy (fun fd -> fd.Name) |> List.tryFind (fun (_, c) -> c > 1)

            match duplicate with
            | Some(name, _) -> Error(FunctionError(name, "defined twice"))
            | None ->
                let defs = order |> List.map (fun fd -> fd.Name, fd) |> Map.ofList

                firstError (
                    (order |> List.map (fun fd () -> checkDefinition fd))
                    @ (order |> List.map (fun fd () -> checkCalls defs fd.Body))
                )
                |> Result.bind (fun () ->
                    match cycle order defs with
                    | Some cyc -> Error(RecursiveFunction cyc)
                    | None -> Ok())
                |> Result.bind (fun () -> sized defs Map.empty order)
                |> Result.bind (fun known ->
                    stepExprs
                    |> List.map (fun (at, e) () -> checkStep defs known at e)
                    |> firstError)
                |> Result.map (fun () ->
                    pipeline
                    |> List.choose (fun t ->
                        match t with
                        | Define _ -> None
                        | Filter p -> Some(Filter(inline' defs Map.empty p))
                        | Derive(name, e) -> Some(Derive(name, inline' defs Map.empty e))
                        | Fit spec -> Some(Fit(FitSpec.mapExprs (inline' defs Map.empty) spec))
                        | Predict spec -> Some(Predict(PredictSpec.mapExprs (inline' defs Map.empty) spec))
                        | other -> Some other))

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
        |> List.map (fun field ->
            match Table.tryColumn field.Name t with
            | Some c ->
                let a = List.toArray (Column.toCells c)

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
        cols |> List.tryFindIndex (fun f -> f.Name = name)

    let private colType (cols: Schema) (name: string) : ColumnType option =
        cols |> List.tryFind (fun f -> f.Name = name) |> Option.map _.Type

    let private available (cols: Schema) : string list = Fields.names cols

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
        | Float f -> FloatLayout.canonical f
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
               else FloatLayout.canonical f) // canonical layout collapses -0.0 → "0"
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

        let grow (next: int) : unit =
            let oldHashes = hashes
            let oldKeys = keys
            let oldValues = values
            cap <- next
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
                grow (cap * 2)

            place (hashOf k) k v
            count <- count + 1

        /// Room for `n` keys without growing again (D64): one rehash to the power of two that keeps
        /// `n` keys at most half full, where the table is smaller.
        member _.Reserve(n: int) : unit =
            let mutable next = cap

            while n * 2 > next do
                next <- next * 2

            if next > cap then
                grow next

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
    /// date-like cell (Phase 90, `DateDiffDays`) — Core's integer temporal form (Phase 422), read
    /// through `TemporalText.tryDays`, the days-from-civil arithmetic this evaluator carried a copy
    /// of until Phase 423. A `Date` cell's text is canonical, so it is the day count rendered; a
    /// timestamp or a string is admitted by its first ten characters, as it always was.
    let private civilDays (c: Cell) : Result<int, EvalError> =
        match c with
        | Date s
        | Timestamp s
        | Str s ->
            let head = if s.Length > 10 then s.Substring(0, 10) else s

            match TemporalText.tryDays head with
            | Some days -> Ok days
            | None -> Error(TypeError("dateDiffDays: '" + s + "' is not YYYY-MM-DD[...]"))
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
                        Error(
                            OverflowError(
                                "float→int cast out of int32 range: "
                                + Canonical.write Rendering.CrossHost (JFloat f)
                            )
                        )
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
            // Phase 423 (operator ruling 2026-10-10, `DECISIONS.md` D18): a text that is not the
            // canonical form is REFUSED by name. Core `1.0.0`'s date column holds calendar days and
            // its timestamp column seconds in a unit, so there is no column for a `Date "x"` to
            // land in; the cast used to admit any string and let the cell list carry it.
            | DateType ->
                match c with
                | Date _ -> Ok c
                | Str s when TemporalText.isCanonicalDate s -> Ok(Date s)
                | Str s -> Error(TypeError("cannot cast '" + s + "' to date: not a canonical YYYY-MM-DD"))
                | _ -> Error(TypeError "cannot cast to date")
            | TimestampType unit ->
                match c with
                | Timestamp _ -> Ok c
                | Str s when (TemporalText.tryInstant unit s).IsSome -> Ok(Timestamp s)
                | Str s -> Error(TypeError("cannot cast '" + s + "' to timestamp: not a canonical instant of its unit"))
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
    /// `Interpolate` over one cell (Phase 596, `DECISIONS.md` D48): a null propagates, an int
    /// promotes, a float reads the curve; a decimal is refused by name (`Cast` first), and so is every
    /// other cell. One implementation, read by the reference arm and the compiled form alike.
    let internal interpolateCell (c: PreparedCurve) (v: Cell) : Result<Cell, EvalError> =
        let read (x: float) =
            match Interpolation.at c x with
            | Some y -> Ok(Float y)
            | None -> Ok Null

        match v with
        | Null -> Ok Null
        | Int i -> read (float i)
        | Float f -> read f
        | Decimal _ -> Error(TypeError "interpolate: a decimal operand is refused; cast it to a float first (Cast)")
        | Bool _
        | Str _
        | Date _
        | Timestamp _ -> Error(TypeError "interpolate: the operand must be an int or a float")

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

    // ---- Phase 557 (D43), Phase 594 (D54): the power functions ---------------------------------
    //
    // Every one is EXACT on every host. An INTEGER exponent runs one pinned product tree over the
    // host's IEEE-754 multiply, which every host rounds the same way, so the tree and not the host
    // decides the bits. A float exponent, `Exp`, `Ln` and `Log10` read D43's special-value table
    // FIRST — published as data by `Fuaran.Compute.Elementary` — and every input the
    // table does not answer goes to that module's pinned routine (D52): one argument reduction and
    // one polynomial in `+ − × ÷` and comparisons, which a host that transcribes it computes to the
    // same bits. Until Phase 594 that arm was the host's own maths library, deterministic within one
    // host and within 2 ulp across hosts; D54 supersedes that tier for these four functions.
    // `proofs/Pipeline.fst` section 14 models the routine and the tables clause for clause, and the
    // pinned routine as the parameter the premise `elementary-functions-pinned-by-transcription`
    // fixes.

    /// Binary exponentiation over the host's IEEE-754 multiply — the one pinned routine for an
    /// integer exponent. `n = 0` is `1.0` with no multiply, `n = 1` is `x` with none, `n = 2` is
    /// `x * x` and `n = 3` is `x * (x * x)`: the bits of the repeated-multiplication spelling, a single
    /// multiply being commutative. Higher exponents take the squaring tree, which is pinned but is not
    /// the left fold's tree. A negative exponent is one reciprocal of the positive power. The caller
    /// has already answered a NaN base and a zero base under a negative exponent.
    // The accumulator is `None` until the first set bit, so a power takes exactly the multiplies
    // its bits name: `x^1` is `x` itself, never `1.0 * x`. `k` is the exponent's magnitude.
    let rec private powGo (acc: float option) (b: float) (k: int64) : float =
        let acc' =
            if k % 2L = 1L then
                Some(
                    match acc with
                    | Some a -> a * b
                    | None -> b
                )
            else
                acc

        let k' = k / 2L

        if k' = 0L then
            (match acc' with
             | Some a -> a
             | None -> 1.0)
        else
            powGo acc' (b * b) k'

    let internal powInt (x: float) (n: int) : float =
        if n = 0 then 1.0
        elif n > 0 then powGo None x (int64 n)
        else 1.0 / powGo None x (-(int64 n))

    /// A negative integer exponent as the evaluator answers it: the reciprocal of the positive
    /// power, `Null` where that power is zero — a zero base, or a power that underflowed to zero —
    /// exactly as `Div` by zero is `Null`, so `Pow(x, -2)` and `1 / (x * x)` answer one cell on every
    /// input. Called with `n < 0`.
    let private reciprocal (x: float) (n: int) : Result<Cell, EvalError> =
        let p = powGo None x (-(int64 n))
        if p = 0.0 then Ok Null else Ok(Float(1.0 / p))

    /// The integer power under the int32 band: `powInt`'s product tree over `int64`, every product
    /// that leaves the band refused by name as `Mul` refuses one. A base square that leaves the band
    /// with exponent bits still to take is refused too: every value it would still multiply into is
    /// at least as large as it. Called with `n >= 0`.
    let private powIntExact (x: int) (n: int) : Result<Cell, EvalError> =
        let rec go (acc: int64 option) (b: int64) (k: int) : Result<Cell, EvalError> =
            let acc' =
                if k % 2 = 1 then
                    Some(
                        match acc with
                        | Some a -> a * b
                        | None -> b
                    )
                else
                    acc

            match acc' with
            | Some a when not (inInt32Range a) -> Error(overflowed "pow" a)
            | _ ->
                let k' = k / 2

                if k' = 0 then
                    Ok(
                        Int(
                            match acc' with
                            | Some a -> int a
                            | None -> 1
                        )
                    )
                else
                    let b2 = b * b

                    if inInt32Range b2 then
                        go acc' b2 k'
                    else
                        Error(overflowed "pow" b2)

        if n = 0 then Ok(Int 1) else go None (int64 x) n

    /// A cell from one of `Elementary`'s special-value tables (D43 item 3, carried by D52): a NaN
    /// is D16's one NaN, the table's own value is that value, an input outside the reals is `Null`
    /// (as `Div` by zero is), and every other input is the pinned routine's — `compute`, called only
    /// there.
    let private ofTable (table: SpecialValue) (compute: unit -> float) : Cell =
        match table with
        | SpecialValue.NaN -> Float System.Double.NaN
        | SpecialValue.Value v -> Float v
        | SpecialValue.Undefined -> Null
        | SpecialValue.Compute -> Float(compute ())

    /// A float `Pow` (Phase 594, D54): the table, then the pinned `Elementary.pow`.
    let internal powFloat (x: float) (y: float) : Cell =
        ofTable (Elementary.powTable x y) (fun () -> Elementary.pow x y)

    /// `Exp` (Phase 594, D54): the table — a NaN, the overflow limit, the underflow to zero — then
    /// the pinned `Elementary.exp`.
    let internal expCell (x: float) : Cell =
        ofTable (Elementary.expTable x) (fun () -> Elementary.exp x)

    /// `Ln` and `Log10` (Phase 594, D54): the one table — a NaN, `Null` at zero and below, `0` at
    /// one, `Inf` at `Inf` — then the pinned `Elementary.ln` or `Elementary.log10`.
    let internal logCell (base10: bool) (x: float) : Cell =
        ofTable (Elementary.lnTable x) (fun () -> if base10 then Elementary.log10 x else Elementary.ln x)

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
                // A `Date` cell is canonical text, so it is read through Core's integer form (Phase
                // 423): the day count, then its civil date — the arithmetic the typed path shares.
                | Str part, Date s when (TemporalText.tryDays s).IsSome ->
                    let civil = TemporalText.civilOfDays (TemporalText.tryDays s).Value

                    match part with
                    | "year" -> Ok(Int civil.Year)
                    | "month" -> Ok(Int civil.Month)
                    | "day" -> Ok(Int civil.Day)
                    | other -> Error(TypeError("datePart: unknown part '" + other + "'"))
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
        // Phase 557 (D43), Phase 594 (D54). A null operand propagates first, as the unary functions'
        // does; a decimal is refused by name, as `Sqrt` refuses one; then the exponent's CELL decides
        // the path — an `Int` exponent is the product tree (exact; an `Int` base under `n >= 0`
        // answers an `Int`), a float exponent is the table and then the pinned `Elementary.pow`.
        | Pow ->
            arity 2
            |> Result.bind (fun () ->
                match args.[0], args.[1] with
                | Null, _
                | _, Null -> Ok Null
                | Decimal _, _
                | _, Decimal _ -> Error(TypeError "pow of a decimal is not exact: write Pow(Cast(float, x), n)")
                | Int b, Int n -> if n >= 0 then powIntExact b n else reciprocal (float b) n
                | x, Int n ->
                    match asNum x with
                    | Some f ->
                        if System.Double.IsNaN f then Ok(Float f)
                        elif n >= 0 then Ok(Float(powInt f n))
                        else reciprocal f n
                    | None -> Error(TypeError "pow of a non-numeric")
                | x, y ->
                    match asNum x, asNum y with
                    | Some f, Some g -> Ok(powFloat f g)
                    | _ -> Error(TypeError "pow of a non-numeric"))
        | Exp ->
            unary (fun c ->
                match c with
                | Decimal _ -> Error(TypeError "exp of a decimal is not exact: write Exp(Cast(float, x))")
                | _ ->
                    match asNum c with
                    | Some f -> Ok(expCell f)
                    | None -> Error(TypeError "exp of a non-numeric"))
        | Ln
        | Log10 ->
            let name = if fn = Ln then "ln" else "log10"

            unary (fun c ->
                match c with
                | Decimal _ ->
                    Error(
                        TypeError(
                            name
                            + " of a decimal is not exact: write "
                            + (if fn = Ln then "Ln" else "Log10")
                            + "(Cast(float, x))"
                        )
                    )
                | _ ->
                    match asNum c with
                    | Some f -> Ok(logCell (fn = Log10) f)
                    | None -> Error(TypeError(name + " of a non-numeric")))
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
    // ---- Phase 429: the unit check, resolved per step over the schema's units (D29) ----
    //
    // A column's unit is a fact of its FIELD (Phase 427), so the check is a static walk over the
    // expression and the step's schema; there is nothing to look at in a row, and an empty frame is
    // checked exactly as a full one. The walk answers the expression's unit — `None` is
    // UNCONSTRAINED: a literal, a param, a column that states no unit; it composes with anything, so
    // a unitless pipeline passes the check and evaluates as it always did — and rewrites every
    // `Convert` into the cast and the multiplication that perform it, so the evaluator proper never
    // meets one (`resolveExpr` resolves a stray one the same way).

    /// The unit a schema states for a column, where it states one.
    let private colUnit (cols: Schema) (name: string) : UnitOfMeasure option =
        cols |> List.tryFind (fun f -> f.Name = name) |> Option.bind _.Unit

    /// Two units are one unit when they render to one canonical text (Phase 426: identity is the
    /// product, so `N` and `kg.m/s2` are two units, compatible with factor 1, and are refused here
    /// until converted).
    let private sameUnit (a: UnitOfMeasure) (b: UnitOfMeasure) : bool = Unit.render a = Unit.render b

    /// The unit of a product: an unconstrained factor contributes nothing.
    let private unitProduct (a: UnitOfMeasure option) (b: UnitOfMeasure option) : UnitOfMeasure option =
        match a, b with
        | None, u
        | u, None -> u
        | Some x, Some y -> Some(Unit.mul x y)

    /// The unit of a quotient: an unconstrained dividend is dimensionless over a stated divisor
    /// (`1 / s` is `/s`), an unconstrained divisor leaves the dividend's unit.
    let private unitQuotient (a: UnitOfMeasure option) (b: UnitOfMeasure option) : UnitOfMeasure option =
        match a, b with
        | None, None -> None
        | Some x, None -> Some x
        | None, Some y -> Some(Unit.div Unit.dimensionless y)
        | Some x, Some y -> Some(Unit.div x y)

    /// What a power the algebra cannot raise a unit to is refused against (Phase 557): a text naming
    /// the refusal rather than a unit — the node in the error carries the exponent asked for.
    let private powerRangeText = "a power the algebra can carry"

    /// The unit to an integer power (Phase 557): `None` where an exponent would leave the algebra's
    /// range, which `Unit.pow` reports by raising.
    let private unitPower (u: UnitOfMeasure) (n: int) : UnitOfMeasure option =
        try
            Some(Unit.pow u n)
        with _ ->
            None

    /// The first operand stating a unit OTHER than the dimensionless one (Phase 557): what a
    /// transcendental, or a power under an exponent that is not a literal int, refuses against `1`.
    let private firstDimensioned (units: UnitOfMeasure option list) : UnitOfMeasure option =
        units
        |> List.tryPick (fun u ->
            match u with
            | Some x when not (sameUnit x Unit.dimensionless) -> Some x
            | _ -> None)

    /// The exact conversion ratio as the double the resolved expression multiplies by.
    let private ratioToFloat (r: Ratio) : float =
        double r.Numerator / double r.Denominator

    /// A field under a stated unit, or as it is.
    let internal stated (u: UnitOfMeasure option) (f: Field) : Field =
        match u with
        | Some u -> Field.withUnit u f
        | None -> f

    /// The unit of `e` over `cols`, and `e` with every `Convert` resolved (Phase 429). `step` names
    /// the step a refusal is about. The rules, held by `UnitPropagationTests` and `DECISIONS.md` D29:
    /// addition, subtraction, comparison, `Coalesce`, `Case` arms, `Least` / `Greatest` and `InList`
    /// require ONE unit among the operands that state one, and the answer carries it;
    /// multiplication, division, `Quotient` and `Mod` compose through Core's algebra; `Cast`,
    /// `Abs`, `Round`, `Floor`, `Ceil` and `Rounded` keep their operand's unit; a boolean, a text, a
    /// date and a `Sqrt` carry none; a literal, a param and a `Now` are unconstrained.
    let rec internal resolveUnitsIn
        (step: string)
        (cols: Schema)
        (e: ColExpr)
        : Result<UnitOfMeasure option * ColExpr, EvalError> =
        let go = resolveUnitsIn step cols

        let mismatch (at: ColExpr) (l: UnitOfMeasure) (r: UnitOfMeasure) =
            Error(UnitMismatch(step, at, Unit.render l, Unit.render r))

        // One unit among the stated ones: an unconstrained operand takes the other's.
        let agree (at: ColExpr) (a: UnitOfMeasure option) (b: UnitOfMeasure option) =
            match a, b with
            | None, u
            | u, None -> Ok u
            | Some x, Some y -> if sameUnit x y then Ok(Some x) else mismatch at x y

        let rec agreeAll (at: ColExpr) (acc: UnitOfMeasure option) (units: UnitOfMeasure option list) =
            match units with
            | [] -> Ok acc
            | u :: rest -> agree at acc u |> Result.bind (fun acc -> agreeAll at acc rest)

        let rec many (xs: ColExpr list) : Result<(UnitOfMeasure option * ColExpr) list, EvalError> =
            match xs with
            | [] -> Ok []
            | x :: rest -> go x |> Result.bind (fun r -> many rest |> Result.map (fun rs -> r :: rs))

        let both a b k =
            go a
            |> Result.bind (fun (ua, a') -> go b |> Result.bind (fun (ub, b') -> k ua a' ub b'))

        match e with
        | Col name -> Ok(colUnit cols name, e)
        | Lit _
        | Param _
        | Now _ -> Ok(None, e)
        | Binary(op, a, b) ->
            both a b (fun ua a' ub b' ->
                let e' = Binary(op, a', b')

                match op with
                | Add
                | Sub -> agree e ua ub |> Result.map (fun u -> u, e')
                | Mul -> Ok(unitProduct ua ub, e')
                | Div -> Ok(unitQuotient ua ub, e')
                | Mod -> Ok(ua, e')
                | Eq
                | Ne
                | Lt
                | Le
                | Gt
                | Ge -> agree e ua ub |> Result.map (fun _ -> None, e')
                | And
                | Or
                | Contains
                | StartsWith
                | EndsWith -> Ok(None, e'))
        | Not x -> go x |> Result.map (fun (_, x') -> None, Not x')
        | IsNull x -> go x |> Result.map (fun (_, x') -> None, IsNull x')
        | InParam(x, n) -> go x |> Result.map (fun (_, x') -> None, InParam(x', n))
        | Coalesce xs ->
            many xs
            |> Result.bind (fun rs ->
                agreeAll e None (List.map fst rs)
                |> Result.map (fun u -> u, Coalesce(List.map snd rs)))
        | Case(cases, els) ->
            many (cases |> List.map fst)
            |> Result.bind (fun whens ->
                many (cases |> List.map snd)
                |> Result.bind (fun thens ->
                    go els
                    |> Result.bind (fun (ue, els') ->
                        agreeAll e ue (List.map fst thens)
                        |> Result.map (fun u -> u, Case(List.zip (List.map snd whens) (List.map snd thens), els')))))
        | Cast(ty, x) -> go x |> Result.map (fun (u, x') -> u, Cast(ty, x'))
        | ApplyFn(fn, args) ->
            many args
            |> Result.bind (fun rs ->
                let units = List.map fst rs
                let e' = ApplyFn(fn, List.map snd rs)

                match fn with
                | Abs
                | Round
                | Floor
                | Ceil -> Ok((units |> List.tryHead |> Option.flatten), e')
                | Least
                | Greatest -> agreeAll e None units |> Result.map (fun u -> u, e')
                // Phase 557 (D43): an integer-LITERAL exponent composes the unit through the algebra
                // (`m` squared is `m2`; to the power zero, unconstrained); any other power and the
                // transcendentals take a dimensionless operand only, refused as a mismatch against
                // `1`. The algebra refuses an exponent product that leaves its range (`m2` to the
                // power of a billion), named as a mismatch against `a power the algebra can carry`.
                | Pow ->
                    (match args, units with
                     | [ _; Lit(Int n) ], [ ub; _ ] ->
                         match ub with
                         | None -> Ok(None, e')
                         | Some u when n = 0 -> Ok(None, e')
                         | Some u ->
                             match unitPower u n with
                             | Some p -> Ok(Some p, e')
                             | None -> Error(UnitMismatch(step, e, Unit.render u, powerRangeText))
                     | _ ->
                         match firstDimensioned units with
                         | None -> Ok(None, e')
                         | Some u -> mismatch e u Unit.dimensionless)
                | Exp
                | Ln
                | Log10 ->
                    (match firstDimensioned units with
                     | None -> Ok(None, e')
                     | Some u -> mismatch e u Unit.dimensionless)
                // The algebra states integer powers only, so a root's unit is not derived: the
                // answer is unconstrained, as a text, a date and a count are.
                | Sqrt
                | Length
                | Lower
                | Upper
                | Substr
                | DatePart
                | Concat
                | Trim
                | Replace
                | DateDiffDays
                | IndexOf -> Ok(None, e'))
        | InList(x, items) ->
            go x
            |> Result.bind (fun (ux, x') ->
                many items
                |> Result.bind (fun rs ->
                    agreeAll e ux (List.map fst rs)
                    |> Result.map (fun _ -> None, InList(x', List.map snd rs))))
        | Quotient(a, b, r) -> both a b (fun ua a' ub b' -> Ok(unitQuotient ua ub, Quotient(a', b', r)))
        | Rounded(x, r) -> go x |> Result.map (fun (u, x') -> u, Rounded(x', r))
        // Phase 596: a curve's answer states no unit — its knots' `y` are numbers the curve gives,
        // whatever the operand's unit; the operand's own units are checked as anywhere else.
        // The knots are checked here, with the step's other static refusals, so an empty frame is
        // refused exactly as a full one.
        | Interpolate(x, knots, kind, outside) ->
            go x
            |> Result.bind (fun (_, x') ->
                Interpolation.validate knots
                |> Result.map (fun () -> None, Interpolate(x', knots, kind, outside)))
        // Phase 596: expansion (`FnDef.expand`) removes every call and argument before a step
        // resolves; one that reaches here is refused by name.
        | Call(name, _) ->
            Error(
                FunctionError(
                    name,
                    "a call reached evaluation unexpanded; expand the pipeline's functions first (FnDef.expand)"
                )
            )
        | Arg a -> Error(FunctionError(a, "an Arg reached evaluation outside a function body"))
        | Convert(x, target) ->
            go x
            |> Result.bind (fun (ux, x') ->
                let asFloat = Cast(FloatType, x')

                match ux with
                | None -> Ok(Some target, asFloat)
                | Some source ->
                    if sameUnit source target then
                        Ok(Some target, asFloat)
                    else
                        match Unit.conversionFactor source target with
                        | Ok ratio -> Ok(Some target, Binary(Mul, asFloat, Lit(Float(ratioToFloat ratio))))
                        | Error(UnitConversionRefusal.Incompatible _) -> mismatch e source target)

    /// `resolveUnitsIn` with no step to name — the unit of an expression, for a reader.
    let internal unitOfExpr (cols: Schema) (e: ColExpr) : Result<UnitOfMeasure option, EvalError> =
        resolveUnitsIn "expression" cols e |> Result.map fst

    /// The output frame with the named column under `u` — a verb's own column, stated after the
    /// verb built it (`Frame.withColumn` / `appendColumn` make a fresh field).
    let internal unitised (name: string) (u: UnitOfMeasure option) (f: Frame) : Frame =
        match u with
        | None -> f
        | Some u ->
            { f with
                Cols =
                    f.Cols
                    |> List.map (fun fd -> if fd.Name = name then Field.withUnit u fd else fd) }

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
        /// Phase 596. The curve is prepared with the node (its knots checked, a monotone cubic's
        /// tangents computed) once per step; a refusal is held and raised when an evaluation reaches
        /// the node, before its operand.
        | RInterpolate of curve: Result<PreparedCurve, EvalError> * ResolvedExpr

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
        // A conversion resolves by substitution against the schema's units (Phase 429) BEFORE
        // evaluation, as a list param and a `now` do; every step entry point has done so by here,
        // and one that reaches this arm is resolved the same way — a mismatch is held and raised
        // when a row reaches it, as an unbound param's is.
        | Convert _ ->
            match resolveUnitsIn "expression" cols e with
            | Ok(_, resolved) -> go resolved
            | Error err -> RFail err
        | Interpolate(x, knots, kind, outside) -> RInterpolate(Interpolation.prepare knots kind outside, go x)
        // Phase 596: expanded away before evaluation; one that reaches here is refused by name.
        | Call(name, _) ->
            RFail(
                FunctionError(
                    name,
                    "a call reached evaluation unexpanded; expand the pipeline's functions first (FnDef.expand)"
                )
            )
        | Arg a -> RFail(FunctionError(a, "an Arg reached evaluation outside a function body"))

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

    /// Phase 404 — THE NaN a float leaves the evaluator with: `Double.NaN`'s bit pattern, every other
    /// value untouched. IEEE 754 does not say which payload or sign a NaN result carries — the
    /// hardware's default NaN differs between x64 (sign set) and Arm64 (sign clear), an operation on
    /// two NaNs answers either operand's, and an optimising JIT may commute an addition — so a running
    /// total meeting `-inf + +inf` answered one bit pattern in a Debug build and another in a Release
    /// build of the same code. Every float the evaluator COMPUTES passes through here at the point it
    /// is emitted, so the bits of an answer are a function of its inputs alone (`DECISIONS.md` D16):
    /// an expression's answer (`evalResolved`, `runCompiled`, and a compiled `Derive`'s float and boxed
    /// roots, sequential or handed off), an aggregate's (`aggCells`, `GroupAgg.Stream.TryCell`,
    /// `GroupAgg.Output.Float`) and a window's (the column as `windowColumnOverWith` builds it).
    let internal canonicalNaN (x: float) : float =
        if System.Double.IsNaN x then System.Double.NaN else x

    /// `canonicalNaN` over a cell: a float cell holding a NaN becomes `Float Double.NaN`; every other
    /// cell is itself.
    let internal canonicalNaNCell (c: Cell) : Cell =
        match c with
        | Float x when System.Double.IsNaN x -> Float System.Double.NaN
        | _ -> c

    /// Evaluate a resolved expression against one row held as an array — the reference expression
    /// evaluator. Every `Param` was read from the environment at resolution; a hit is its bound
    /// `Cell`, and a miss is the strict `UnboundParam` naming the param and the bound set (GP4/GP5)
    /// — never a throw, never a silent default.
    let rec private evalNode (row: Cell[]) (e: ResolvedExpr) : Result<Cell, EvalError> =
        match e with
        | RCol i -> Ok(row[i])
        | RConst c -> Ok c
        | RFail err -> Error err
        | RBinary(op, a, b) ->
            evalNode row a
            |> Result.bind (fun av -> evalNode row b |> Result.bind (fun bv -> binaryOp op av bv))
        | RNot inner -> evalNode row inner |> Result.bind notCell
        | RCoalesce exprs ->
            let rec go =
                function
                | [] -> Ok Null
                | x :: rest ->
                    evalNode row x
                    |> Result.bind (function
                        | Null -> go rest
                        | c -> Ok c)

            go exprs
        | RCase(cases, elseExpr) ->
            let rec go =
                function
                | [] -> evalNode row elseExpr
                | (whenE, thenE) :: rest ->
                    evalNode row whenE
                    |> Result.bind (function
                        | Bool true -> evalNode row thenE
                        | _ -> go rest)

            go cases
        | RCast(ty, inner) -> evalNode row inner |> Result.bind (castCell ty)
        | RInList(subject, items) ->
            evalNode row subject
            |> Result.bind (fun sv ->
                match sv with
                | Null -> Ok Null
                | _ ->
                    // SQL three-valued membership: any equal => true; no match seen a null => null.
                    let rec go sawNull =
                        function
                        | [] -> Ok(if sawNull then Null else Bool false)
                        | it :: rest ->
                            evalNode row it
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
            evalNode row inner
            |> Result.map (fun v ->
                match v with
                | Null -> Bool true
                | _ -> Bool false)
        | RApplyFn(fn, args) ->
            let rec evalArgs acc =
                function
                | [] -> Ok(List.rev acc)
                | a :: rest -> evalNode row a |> Result.bind (fun v -> evalArgs (v :: acc) rest)

            evalArgs [] args |> Result.bind (applyScalar fn)
        | RQuotient(scale, mode, a, b) ->
            scale
            |> Result.bind (fun n ->
                evalNode row a
                |> Result.bind (fun av -> evalNode row b |> Result.bind (fun bv -> quotientCell mode n av bv)))
        | RRounded(scale, mode, a) ->
            scale
            |> Result.bind (fun n -> evalNode row a |> Result.bind (roundedCell mode n))
        | RInterpolate(curve, a) ->
            curve
            |> Result.bind (fun c -> evalNode row a |> Result.bind (interpolateCell c))

    /// `evalNode`'s answer as it leaves the evaluator: a NaN made canonical (Phase 404). The one exit
    /// of the row evaluator — `evalExprInRow`, and the incremental seam's re-evaluated cells, read it
    /// here, so the row form and the incremental refresh answer the bits the compiled form does.
    let internal evalResolved (row: Cell[]) (e: ResolvedExpr) : Result<Cell, EvalError> =
        evalNode row e |> Result.map canonicalNaNCell


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
                    | Of(TimestampType _), Of(TimestampType _)
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
            // Phase 413: over an operand the typer cannot decide they cannot be decided either,
            // because that operand may be a decimal; typing them a float there let a float column
            // hold a decimal cell.
            | Round
            | Floor
            | Ceil ->
                unary (fun t ->
                    match t with
                    | Of DecimalType -> Of DecimalType
                    | Unknown -> Unknown
                    | Absent
                    | Of _ -> Of FloatType)
            | Sqrt -> unary (fun _ -> Of FloatType)
            // Phase 557 (D43): the transcendentals answer a float, as `Sqrt` does. A power's type
            // over two typed operands: an int base under an int exponent is decided by the
            // exponent's SIGN (an `Int` at or above zero, a float below), which the typings do not
            // carry — `powLit` reads a literal exponent, and otherwise the cells decide; an
            // undecided operand may be that pair, so it is undecided too; every other numeric pair
            // and every refused pair is a float, as `Sqrt` over a refused operand is.
            | Exp
            | Ln
            | Log10 -> unary (fun _ -> Of FloatType)
            | Pow ->
                if not (arity 2) then
                    Absent
                else
                    match List.head args, List.item 1 args with
                    | Absent, _
                    | _, Absent -> Absent
                    | Of IntType, Of IntType -> Unknown
                    | Unknown, _
                    | _, Unknown -> Unknown
                    | _ -> Of FloatType
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

        /// A power under a LITERAL integer exponent `n` (Phase 557, D43): an int base answers an `Int`
        /// at or above zero (the exact routine, overflow refused) and a float below (the reciprocal);
        /// an undecided base may be an int; every other base is a float, refused ones included.
        let powLit (n: int) (b: Typing) : Typing =
            match b with
            | Absent -> Absent
            | Of IntType -> if n >= 0 then Of IntType else Of FloatType
            | Unknown -> Unknown
            | Of _ -> Of FloatType

        /// `Interpolate` (Phase 596): a float, null where the operand is. Any other operand is refused
        /// on every row it reaches, so no present value contradicts the float.
        let interpolate (a: Typing) : Typing =
            match a with
            | Absent -> Absent
            | Of _
            | Unknown -> Of FloatType

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
        // Phase 557: a literal integer exponent decides a power's type by its sign.
        | ApplyFn(Pow, [ b; Lit(Int n) ]) -> Typing.powLit n (go b)
        | ApplyFn(fn, args) -> Typing.applyFn fn (List.map go args)
        | InList(subject, _) -> Typing.inList (go subject)
        | IsNull _ -> Typing.isNull
        // A bound list param evaluates as the `InList` it substitutes to; an unbound one is an
        // error on every row. Either way the present values are booleans.
        | InParam(subject, _) -> Typing.inList (go subject)
        | Quotient(a, b, _) -> Typing.quotient (go a) (go b)
        | Rounded(a, _) -> Typing.rounded (go a)
        // A conversion answers a float (D29): the typing of the cast it resolves to, a factor or not.
        | Convert(x, _) -> Typing.cast FloatType (go x)
        // Phase 596: a curve answers a float, null where its operand is.
        | Interpolate(x, _, _, _) -> Typing.interpolate (go x)
        // Expanded away before a step is typed; unexpanded, the typer does not decide it.
        | Call _
        | Arg _ -> Unknown

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
    //  - where the typer cannot decide over the schema alone but can once the evaluation's params
    //    are substituted (a `Param`, and everything built over one), the evaluator types the column
    //    by the substituted expression (`derivedTypingIn`, Phase 413): the same on every frame of
    //    that evaluation, where the static reader, which holds no env, still cannot name it;
    //  - where the typer cannot decide at all (`Unknown`: an unpinned `Now`, a column the schema
    //    does not carry, an unbound `Param`, an operator over operands it would refuse, or a join of
    //    two types the exact typer keeps apart, D3), the CELLS decide, by Phase 321's widening join,
    //    `StringType` when none is present (the residue, `DECISIONS.md` D23);
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
        | Rounded _
        | Convert _
        | Arg _
        | Call _
        | Interpolate _ -> typing cols e = Of ty

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

    /// How an EVALUATION of a `Derive` of `e` over `cols` under `env` types its column (Phase 413):
    /// the static rule wherever it decides or refuses, and where it leaves the column to the cells,
    /// the type the same rule DECIDES over `e` with the env's params substituted — the expression
    /// the evaluator answers exactly as it answers `e` (`ColExpr.substitute`'s witness), so a bound
    /// param types as the literal it is bound to on a full, an empty and an all-null frame alike.
    /// It decides and never refuses: a bound float beside a decimal arm stays the cells' to refuse,
    /// as it was, because refusing it here would refuse frames whose cells hold one family
    /// (`DECISIONS.md` D23). Every evaluating path reads this one (the reference, the morsel runner,
    /// the incremental seam's walk and its chunked path); a static reader holds no env and reads
    /// `derivedTyping`.
    let internal derivedTypingIn (env: Map<string, Cell>) (cols: Schema) (e: ColExpr) : DerivedTyping =
        match derivedTyping cols e with
        | ByCells ->
            match derivedTyping cols (ColExpr.substitute env e) with
            | Decided _ as dt -> dt
            | ByCells
            | Refused -> ByCells
        | Decided _
        | Refused as dt -> dt

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

    /// The refusal of a derived column whose cells span two types no column type holds together
    /// (Phase 423). Until Core `1.0.0` such a column was typed by its first present cell and carried
    /// the other cells as they were; a typed column cannot carry them, so the join is refused as a
    /// float beside a decimal is, naming both types.
    let internal typesBesideEachOther (column: string) (a: ColumnType) (b: ColumnType) : EvalError =
        TypeError(
            "derived column '"
            + column
            + "' holds a "
            + ColumnType.tag a
            + " beside a "
            + ColumnType.tag b
            + ", which no column type holds together: cast one to the other's type first"
        )

    /// The cells' half of the rule (Phase 338): Phase 321's widening join over the present cells
    /// an index range reads, `StringType` when none is present, and the named refusals where the
    /// cells hold a float beside a decimal, or two types with no widening between them.
    let internal typeFromCells (column: string) (count: int) (cellAt: int -> Cell) : Result<ColumnType, EvalError> =
        let mutable acc: ColumnType option = None
        let mutable sawFloat = false
        let mutable sawDecimal = false
        let mutable disjoint: (ColumnType * ColumnType) option = None

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
                | TimestampType _ -> ()

                acc <-
                    match acc with
                    | None -> Some t
                    | Some a ->
                        if disjoint.IsNone && (joinColumnType a t).IsNone then
                            disjoint <- Some(a, t)

                        Some(widenColumnType a t)

        if sawFloat && sawDecimal then
            Error(floatBesideDecimal column)
        else
            match disjoint with
            | Some(a, b) -> Error(typesBesideEachOther column a b)
            | None -> Ok(acc |> Option.defaultValue StringType)

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

    // ---- a repeated output name (Phase 468, D35) ----

    /// THE ONE CHECK every verb that names its output columns asks (Phase 468, D35): `outs` is each
    /// output column's name beside what produced it, in output order, and the first name an earlier
    /// output already took is refused with a `TypeError` naming the verb, the name and both
    /// producers. A table cannot hold two columns of one name, and one handed back by name reads
    /// its second column as the first, the defect a join's suffix (D20) and a window's refusal (D28)
    /// closed for those two verbs. `None` is an output whose names are distinct.
    let internal repeatedOutput (verb: string) (outs: (string * string) list) : EvalError option =
        let seen = System.Collections.Generic.Dictionary<string, string>()

        let rec go =
            function
            | [] -> None
            | (name: string, by: string) :: rest ->
                match seen.TryGetValue name with
                | true, first ->
                    Some(
                        TypeError(
                            verb
                            + " output '"
                            + name
                            + "' is named twice, by "
                            + first
                            + " and by "
                            + by
                            + "; give each output a name of its own"
                        )
                    )
                | _ ->
                    seen[name] <- by
                    go rest

        go outs

    /// A `Project` whose pairs name one output twice — `Project [a, x; b, x]`.
    let internal projectRefusal (pairs: (string * string) list) : EvalError option =
        repeatedOutput "project" (pairs |> List.map (fun (src, out) -> out, "the projection of '" + src + "'"))

    /// A `GroupBy` whose keys and aggregate names repeat one — a key named as an aggregate, or two
    /// aggregates of one name.
    let internal groupByRefusal (keys: string list) (aggs: Agg list) : EvalError option =
        repeatedOutput
            "groupBy"
            ((keys |> List.map (fun k -> k, "the key '" + k + "'"))
             @ (aggs |> List.map (fun a -> a.Name, "the aggregate over '" + a.Of + "'")))

    /// An `Unpivot` whose id columns repeat a name or take one of its own two, `variable` and
    /// `value`.
    let internal unpivotRefusal (idVars: string list) : EvalError option =
        repeatedOutput
            "unpivot"
            ((idVars |> List.map (fun n -> n, "the id column '" + n + "'"))
             @ [ "variable", "the unpivot's variable column"
                 "value", "the unpivot's value column" ])

    /// A `Pivot` whose index columns repeat a name or take a value column's: `texts` are the value
    /// columns' names, each the canonical text of an `on` value, so this check reads the data.
    let internal pivotRefusal (index: string list) (texts: string list) : EvalError option =
        repeatedOutput
            "pivot"
            ((index |> List.map (fun n -> n, "the index column '" + n + "'"))
             @ (texts |> List.map (fun t -> t, "the column for the on value '" + t + "'")))

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
            | Convert(x, _) -> exprCols x
            | Interpolate(x, _, _, _) -> exprCols x
            // A body reads no column, so a call reads exactly what its arguments read.
            | Call(_, xs) -> xs |> List.collect exprCols
            | Arg _ -> []

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
            | Of(TimestampType _), Of(TimestampType _)
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
                    | TimestampType _, (Of(TimestampType _) | Of StringType) -> true
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
                    // Phase 557: the transcendentals answer a value or `Null` over every numeric
                    // cell; a power does too, except an int base under an int exponent, which can
                    // overflow the band (a decimal, as for `Sqrt`, is refused).
                    | Exp
                    | Ln
                    | Log10 ->
                        arity 1
                        && (match List.head ts with
                            | Absent -> true
                            | t -> numeric t)
                    | Pow ->
                        arity 2
                        && (match List.head ts, List.item 1 ts with
                            | Absent, _
                            | _, Absent -> true
                            | Of IntType, Of IntType -> false
                            | a, b -> numeric a && numeric b)
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
            // Conservative (Phase 429): the verdict reads the expression as written, and a
            // conversion's arithmetic is what it resolves to over the step's units.
            | Convert _ -> false
            // Phase 596: total where its operand is, is a number or absent, and its knots hold.
            | Interpolate(x, knots, _, _) ->
                exprTotal cols x
                && Result.isOk (Interpolation.validate knots)
                && (match ty x with
                    | Absent
                    | Of IntType
                    | Of FloatType -> true
                    | _ -> false)
            // Unexpanded, each is refused at evaluation.
            | Call _
            | Arg _ -> false

        /// THE TOTALITY VERDICT over a step: `true` only where evaluating the step over ANY table
        /// of schema `cols` returns `Ok`. `Sort` refuses a key the schema does not carry (Phase
        /// 412), so a sort is total only where every key is a literal the schema carries; a `Limit`
        /// clamps; `Distinct` compares tokens; a `Project` fails only on a source the schema lacks or an output
        /// it names twice (Phase 468, D35). A slot still holding a
        /// param is resolved against an env the verdict does not have. Every other verb reaches
        /// a resolver, an aggregate or a comparison the schema alone cannot vouch for.
        /// Phase 429: a step whose expression the unit check refuses (D29) refuses on every frame.
        let private unitsAdmit (cols: Schema) (e: ColExpr) : bool =
            Result.isOk (resolveUnitsIn "expression" cols e)

        let isTotal (cols: Schema) (t: Transform) : bool =
            match t with
            | Filter p -> exprTotal cols p && unitsAdmit cols p
            // Phase 338: a derive whose column the CELLS type can refuse a float beside a decimal
            // over the whole column, and one refused statically always does, so only a decided
            // derive is total.
            | Derive(_, e) ->
                exprTotal cols e
                && unitsAdmit cols e
                && (match derivedTyping cols e with
                    | Decided _ -> true
                    | ByCells
                    | Refused -> false)
            | Sort by ->
                by
                |> List.forall (fun (c, _) ->
                    match Slot.tryLit c with
                    | Some name -> Option.isSome (colType cols name)
                    | None -> false)
            | Limit(n, offset) -> not (Slot.isParam n) && not (Slot.isParam offset)
            | Project pairs ->
                pairs |> List.forall (fun (src, _) -> Option.isSome (colType cols src))
                && Option.isNone (projectRefusal pairs)
            | Distinct -> true
            // A definition changes no frame.
            | Define _ -> true
            // Phase 592: a fit reaches the factorization, whose refusals the schema cannot vouch for.
            | Fit _
            // Phase 597: and so does a prediction, which also reads a model source.
            | Predict _ -> false
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
            | Distinct
            | Define _ -> Some []
            | GroupBy(keys, aggs) -> Some(keys @ (aggs |> List.map (fun a -> a.Of)))
            | Pivot spec -> Some(spec.Index @ [ spec.On; spec.Values ])
            // Phase 592: a coefficient or summary table reads the response, the keys, the weights and the
            // terms' columns; a fitted or residual table hands every input column on, so it reads the row.
            | Fit spec ->
                match spec.Output with
                | FitOutput.Coefficients
                | FitOutput.Summary ->
                    Some(
                        spec.GroupBy
                        @ [ spec.Response ]
                        @ Option.toList spec.Weights
                        @ (FitSpec.exprs spec |> List.collect exprCols)
                    )
                | FitOutput.Fitted
                | FitOutput.Residuals -> None
            // Phase 597: a prediction hands every input column on.
            | Predict _
            | Join _
            | Window _
            | Unpivot _
            | Union _
            | Intersect _
            | Except _ -> None

        /// The planner's own schema knowledge: every column by name, its type where the typer
        /// decides it — a `Derive`'s column carries its decided type (`derivedColumnType`, Phase
        /// 338), and is present with no type where only its cells decide it.
        /// A column the planner knows: its FIELD (name and metadata; Phase 429 carries the unit and
        /// the labels through every rewrite, so a planned pipeline's schema is the reference's) and
        /// its type where the schema decides it — the field's own type is read only through that
        /// option.
        type private Known = (Field * ColumnType option) list

        let private typed (k: Known) : Schema =
            k |> List.choose (fun (f, t) -> t |> Option.map (fun ty -> Field.withType ty f))

        /// Every known column as a field, for the unit walk, which reads no type.
        let private fields (k: Known) : Schema = k |> List.map fst

        let private nameOf ((f, _): Field * ColumnType option) : string = f.Name

        let private has (k: Known) (name: string) : bool =
            k |> List.exists (fun (f, _) -> f.Name = name)

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
                // A projection that names an output twice is refused (Phase 468, D35), so the
                // planner stops knowing at it, as at a step whose reads it lacks.
                | Project pairs when Option.isSome (projectRefusal pairs) -> None
                | Project pairs ->
                    Some(
                        pairs
                        |> List.map (fun (src, out) ->
                            let f, t = k |> List.find (fun (f, _) -> f.Name = src)
                            Fields.rename out f, t)
                    )
                | Derive(name, e) ->
                    // A derive the unit check refuses errors, naming the schema, so the planner
                    // stops knowing at it (Phase 429) exactly as at a step whose reads it lacks.
                    match unitOfExpr (fields k) e with
                    | Error _ -> None
                    | Ok unit ->
                        let ty = derivedColumnType (typed k) e
                        let field = Field.create name (defaultArg ty StringType) |> stated unit

                        if has k name then
                            Some(k |> List.map (fun (f, t) -> if f.Name = name then field, ty else f, t))
                        else
                            Some(k @ [ field, ty ])
                | _ -> None

        /// The schema BEFORE each step of the pipeline, where the planner knows it.
        let private schemasBefore (cols: Schema) (pipeline: Transform list) : Known option[] =
            let start: Known = cols |> List.map (fun f -> f, Some f.Type)

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
            | Define _ -> "define"
            | Fit _ -> "fit"
            | Predict _ -> "predict"

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
                        | Some live when not skip && cur |> List.exists (fun c -> not (Set.contains (nameOf c) live)) ->
                            let kept = cur |> List.filter (fun c -> Set.contains (nameOf c) live)

                            let dropped =
                                cur
                                |> List.filter (fun c -> not (Set.contains (nameOf c) live))
                                |> List.map nameOf

                            Some(
                                kept,
                                { Class = RewriteClass.PruneColumns
                                  At = wi
                                  Detail = "dropped " + String.concat ", " dropped + " ahead of the " + verbName t }
                            )
                        | _ -> None

                    let cur', acc', applied' =
                        match inserted with
                        | Some(kept, r) ->
                            kept, Project(kept |> List.map (fun c -> nameOf c, nameOf c)) :: acc, r :: applied
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

        /// The planner: reorder, then prune, then name the fusions the driver will take. Total —
        /// a pipeline it cannot read is emitted as written.
        ///
        /// Phase 596: a pipeline carrying functions is planned in its EXPANDED form
        /// (`FnDef.expand`): its definitions are dropped and its calls inlined before any rewrite,
        /// so `Planned` holds no call and every index counts the expanded pipeline's steps.
        /// Expansion is the pipeline's meaning rather than a rewrite class, so no `Applied` entry
        /// names it. One whose functions are refused is emitted as written, and its evaluation
        /// answers the refusal.
        let explain (cols: Schema) (pipeline: Transform list) : PlanReport =
            match FnDef.expand pipeline with
            | Error _ ->
                { Written = pipeline
                  Planned = pipeline
                  Applied = []
                  Declined = [] }
            | Ok expanded ->
                let before = schemasBefore cols expanded
                let reordered, moved, declined = reorder before expanded
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
        /// A calendar day as its day count (Phase 423): what a `Dates` vector reads as, so a date
        /// function reads the integer form; boxed, it is the `Date` cell of that day's text.
        | NDays of (int -> int)
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
            | (Absent | Of(TimestampType _)), (Absent | Of(TimestampType _)) -> true
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

    /// The day counts of `0000-01-01` and `9999-12-31`: the range where canonical date text has a four-digit
    /// year, so its ordinal order is the order of the days.
    let private firstFourDigitDay = TemporalText.daysOfCivil 0 1 1

    let private lastFourDigitDay = TemporalText.daysOfCivil 9999 12 31

    /// True where a day's canonical text has a four-digit year.
    let internal fourDigitDay (d: int) : bool =
        d >= firstFourDigitDay && d <= lastFourDigitDay

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
        let types = Fields.types f.Cols |> List.toArray

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
            | NDays r ->
                fun p ->
                    let v = r p
                    if slot.Null then Null else Date(TemporalText.dateText v)

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
            | NDays _
            | NCell _ -> None

        /// An int node reads as a float node in the float carrier, as the reference widens it.
        let asFloat (n: Node) : (int -> float) option =
            match n with
            | NFloat r -> Some r
            | NInt r -> Some(fun p -> float (r p))
            | NNull -> Some(absent 0.0)
            | NBool _
            | NStr _
            | NDays _
            | NCell _ -> None

        let asBool (n: Node) : (int -> bool) option =
            match n with
            | NBool r -> Some r
            | NNull -> Some(absent false)
            | NInt _
            | NFloat _
            | NStr _
            | NDays _
            | NCell _ -> None

        let asStr (n: Node) : (int -> string) option =
            match n with
            | NStr(_, r) -> Some r
            // A day reads as its canonical text where a string is wanted, rendered on demand.
            | NDays r -> Some(fun p -> TemporalText.dateText (r p))
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
            | NDays r ->
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

        /// A date comparison read on day counts. The reference orders dates by their text, so the
        /// days decide only where both spell a four-digit year, the range in which the two orders
        /// are one; any other row compares the rendered text, as the reference does. A date
        /// constant takes part only where it is canonical: other text has no day count.
        let dayCompare
            (op: BinOp)
            (kernel: Kernel)
            (a: ResolvedExpr)
            (na: Node)
            (b: ResolvedExpr)
            (nb: Node)
            : Node option =
            let days (e: ResolvedExpr) (n: Node) : (int -> int) option =
                match n, e with
                | NDays r, _ -> Some r
                | _, RConst(Date s) ->
                    TemporalText.tryDays s
                    |> Option.map (fun d ->
                        fun _ ->
                            slot.Null <- false
                            d)
                | _ -> None

            match kernel, na, nb with
            | OrdinalCompare, (NDays _), _
            | OrdinalCompare, _, (NDays _) ->
                match comparisonTest op, days a na, days b nb with
                | Some test, Some ra, Some rb ->
                    Some(
                        NBool(
                            lift2 ra rb false (fun x y ->
                                if fourDigitDay x && fourDigitDay y then
                                    test (compare x y)
                                else
                                    test (
                                        System.String.CompareOrdinal(TemporalText.dateText x, TemporalText.dateText y)
                                    ))
                        )
                    )
                | _ -> None
            | _ -> None

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
                | NDays _ -> 5
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
                | NStr _
                | NDays _ ->
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
                        slot.Null <- not (Mask.at p mask)
                        Raw.at p vals)
                | Floats(vals, mask) ->
                    Of FloatType,
                    NFloat(fun p ->
                        slot.Null <- not (Mask.at p mask)
                        Raw.at p vals)
                | Bools(vals, mask) ->
                    Of BoolType,
                    NBool(fun p ->
                        slot.Null <- not (Mask.at p mask)
                        Mask.at p vals)
                | Strs(ty, vals, mask) ->
                    Of ty,
                    NStr(
                        ty,
                        fun p ->
                            slot.Null <- not (Mask.at p mask)
                            Raw.at p vals
                    )
                // A temporal vector (Phase 423) renders its canonical text on read, so the string
                // kernels over a date or an instant read what the `Date` / `Timestamp` cell carries.
                | Dates(days, mask) ->
                    Of DateType,
                    NDays(fun p ->
                        slot.Null <- not (Mask.at p mask)
                        Raw.at p days)
                | Stamps(unit, seconds, fraction, mask) ->
                    Of(TimestampType unit),
                    NStr(
                        TimestampType unit,
                        fun p ->
                            slot.Null <- not (Mask.at p mask)
                            Vec.stampText unit seconds fraction p
                    )
                // A column whose cells disagree with its declared type is typed as declared, as the
                // schema-only typer types it, and read boxed: every kernel over it checks the tag per
                // row and hands a disagreeing cell to the reference arm.
                // A decimal vector (Phase 280) reads its cells, as the boxed vector does — each the
                // `Decimal` of the row's text, boxed on read (Phase 440): the expression arms over a
                // decimal are exact text arithmetic, and the scaled integers serve the filter, sort
                // and sum kernels only.
                | Decs(_, _, texts, mask) ->
                    Of types[i], NCell(fun p -> if Mask.at p mask then Decimal(Raw.at p texts) else Null)
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
                 | Timestamp s -> NStr(TimestampType(TemporalText.unitOf s), present s)
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
                    match dayCompare op kernel a na b nb with
                    | Some n -> n
                    | None ->
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
                 | NDays _
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

                // Phase 557: the typer's literal-exponent rule, read off the resolved literal.
                let typed =
                    match fn, args, ts with
                    | Pow, [ _; RConst(Int n) ], [ tb; _ ] -> Typing.powLit n tb
                    | _ -> Typing.applyFn fn ts

                match fn, ns with
                // The date functions over the integer form (Phase 423, carrying Phase 422's task):
                // a difference of two days is a subtraction, and a part of a day is read off its
                // civil date — no text is rendered or parsed. The arguments are read left to right
                // and the first error wins, as `evalArgs` reads them; a null date answers null.
                | DateDiffDays, [ NDays a; NDays b ] ->
                    typed,
                    NInt(fun p ->
                        let da = a p

                        if slot.Null || erred () then
                            0
                        else
                            let db = b p
                            if slot.Null || erred () then 0 else db - da)
                | DatePart, [ part; NDays d ] ->
                    let readPart (which: Cell) : int -> int =
                        match which with
                        | Str "year" -> TemporalText.yearOfDays
                        | Str "month" -> TemporalText.monthOfDays
                        | Str "day" -> TemporalText.dayOfDays
                        | Str other ->
                            fun _ ->
                                fail (TypeError("datePart: unknown part '" + other + "'")) |> ignore
                                0
                        | _ ->
                            fun _ ->
                                fail (TypeError "datePart expects (string part, date/timestamp/string)")
                                |> ignore

                                0

                    match args with
                    // A constant part is read once, and raises nothing until a present date needs it.
                    | RConst which :: _ ->
                        let read = readPart which

                        typed,
                        NInt(fun p ->
                            let days = d p
                            if erred () || slot.Null then 0 else read days)
                    | _ ->
                        let partOf = cellOf part

                        typed,
                        NInt(fun p ->
                            let which = partOf p

                            if erred () then
                                0
                            else
                                let days = d p
                                if erred () || slot.Null then 0 else readPart which days)
                | _ ->

                    let rs = ns |> List.map cellOf |> List.toArray

                    typed,
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
            // Phase 596 — boxed: the curve is the one `interpolateCell` reads, prepared with the
            // node; a refused curve is raised before the operand, as `evalResolved` raises it.
            | RInterpolate(curve, a) ->
                let ta, na = go a
                let ra = cellOf na

                Typing.interpolate ta,
                NCell(fun p ->
                    match curve with
                    | Error err -> fail err
                    | Ok c ->
                        let av = ra p
                        if erred () then Null else unwrap (interpolateCell c av))

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
        | None -> Ok(canonicalNaNCell v)

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

    // ---- aggregates (Phase 36: the pinned semantics live in `Column.aggregate`; the evaluator calls it) ----

    /// The wire spelling of an aggregate function — the codec's, and the refusal messages' (one
    /// spelling, so a message names the function as the wire does).
    let internal aggFnTag (fn: AggFn) : string =
        match fn with
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

    /// Lift a `Column.AggregateError` into the evaluator's `EvalError` envelope. An overflow maps to the
    /// pinned `OverflowError` (so the existing "Sum overflow is a named OverflowError" contract holds); a
    /// type incompatibility maps to the `AggError` case, naming the expected types.
    let private aggErr (e: AggregateError) : EvalError =
        match e with
        | AggregateOverflow d -> OverflowError d
        | IncompatibleAggType(fn, ct, expected) ->
            AggError(
                "aggregate "
                + aggFnTag fn
                + " over a "
                + ColumnType.tag ct
                + " column (expected "
                + String.concat "/" (expected |> List.map ColumnType.tag)
                + ")"
            )
        // A present cell outside the column's type (Core `0.33.0`, its Phase 299): since Core
        // `1.0.0` a decimal whose text is not decimal text, the one cell a typed column can still
        // carry outside its type. Named, never truncated or dropped.
        | CellOutsideType(_, ct, cell) ->
            AggError(
                "aggregate over a "
                + ColumnType.tag ct
                + " column met a cell outside its type: "
                + cell
            )

    /// Compute one aggregate over a cell list of the given source type — the evaluator's adapter onto the
    /// public `Column.aggregate` (single source of truth), threading the `EvalError` envelope. The
    /// answer leaves with the canonical NaN (Phase 404): this adapter is the deferred path of every
    /// `GroupBy` and `Pivot` aggregate and the whole of `aggregateCells`. A cell the typed column
    /// cannot hold (Core `1.0.0`: `Column.ofCells` refuses it) is the cell-outside-type refusal the
    /// aggregate itself named before.
    let private aggCells (fn: AggFn) (srcType: ColumnType) (cells: Cell list) : Result<Cell, EvalError> =
        match Column.ofCells "" srcType cells with
        | Ok column ->
            Column.aggregate fn column
            |> Result.map canonicalNaNCell
            |> Result.mapError aggErr
        | Error(TypeMismatch(_, expected, got)) ->
            Error(
                AggError(
                    "aggregate over a "
                    + ColumnType.tag expected
                    + " column met a cell outside its type: "
                    + got
                )
            )
        | Error e -> Error(AggError("aggregate over a " + ColumnType.tag srcType + " column: " + sprintf "%A" e))

    let private aggType (fn: AggFn) (srcType: ColumnType) : ColumnType = Column.aggType fn srcType

    /// The unit an aggregate's column carries over a source of unit `source` (Phase 429, D29): a
    /// count is dimensionless, every other aggregate keeps its source's.
    let private aggregateUnitOf (fn: AggFn) (source: UnitOfMeasure option) : UnitOfMeasure option =
        match fn with
        | Count
        | CountDistinct -> None
        | Sum
        | Mean
        | Min
        | Max
        | Median
        | StdDev
        | First
        | Last -> source

    /// The unit a window function's column carries over a source of unit `source` (Phase 429,
    /// D29): the positional and ranking family carries none, the shifting, running and rolling
    /// families keep their source's.
    let private windowUnitOf (fn: WindowFn) (source: UnitOfMeasure option) : UnitOfMeasure option =
        match fn with
        | RowNumber
        | Rank
        | DenseRank
        | CompetitionRank
        | NTile _ -> None
        | Lag
        | Lead
        | CumulSum
        | RollingMean
        | CumulMax
        | CumulMin
        | RollingSum -> source

    /// The unit an unpivot's `value` column carries (Phase 429, D29): the one unit every value
    /// column states, none where they differ or any states none — an unpivot melts measures of
    /// different kinds into one column by design, and a unit it cannot state it does not invent.
    let private sharedUnit (cols: Schema) (valueVars: string list) : UnitOfMeasure option =
        match valueVars |> List.map (colUnit cols) with
        | Some u :: rest when rest |> List.forall (fun v -> v |> Option.exists (sameUnit u)) -> Some u
        | _ -> None

    // ---- sort (pinned: stable; nulls last regardless of direction) ----

    /// The first of `keys` a schema does not carry, as the `UnknownColumn` every other verb answers
    /// for an unknown column: the ONE resolution of the keys a step takes from a pipeline to order
    /// or partition its rows by (Phase 412, `DECISIONS.md` D21; Phase 441, D26). `Sort` refuses
    /// with it before it orders anything, and a `Window` before it reads a row
    /// (`windowRefusal`), so a misspelled key is an error, never the input order returned as if it
    /// had been sorted or one partition answered as if it were several.
    let internal unknownKey (cols: Schema) (keys: string list) : EvalError option =
        keys
        |> List.tryFind (fun name -> Option.isNone (colIndex cols name))
        |> Option.map (fun name -> UnknownColumn(name, available cols))

    /// The first sort key a schema does not carry (`unknownKey` over the keys' columns).
    let internal unknownSortKey (cols: Schema) (by: (string * SortDir) list) : EvalError option =
        unknownKey cols (List.map fst by)

    /// A sort's keys resolved against one step's schema (Phase 263): each named column's index with
    /// its direction, in key order. The steps that take keys from a pipeline refuse an unknown key
    /// first (`unknownSortKey`, Phase 412), so a name the schema does not carry reaches this only
    /// through the exported comparator `rowCompareBy`, which has no error to answer and still
    /// drops it.
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
    /// — where the predicate is built from `And` and `Or` over comparisons of a typed numeric or
    /// date column with a constant (either side), and `None` for any other predicate, which the
    /// compiled path answers. Such a predicate raises no error on any row: a comparison of a
    /// present int, float or carried decimal (Phase 280) with a present number it can read, or of
    /// a present date with a date, is always decided, and an absent row compares null, which a
    /// filter drops. `And` keeps the rows
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
            // Dates order by their text, which is the order of their day counts only between two
            // four-digit years, so the int kernel answers only for a canonical constant over a
            // vector wholly in that range. Anything else is the compiled path's.
            | Dates(days, mask), Date x ->
                match TemporalText.tryDays x with
                | Some d when Array.forall fourDigitDay days -> Some(k.CmpInts op days mask d f.Count)
                | _ -> None
            | _ -> None

        match e with
        | RBinary(And, a, b) -> Option.map2 k.And (filterBits k f a) (filterBits k f b)
        | RBinary(Or, a, b) -> Option.map2 k.Or (filterBits k f a) (filterBits k f b)
        | RBinary(op, RCol i, RConst c) -> cmpOpOf op |> Option.bind (fun o -> leaf o i c)
        | RBinary(op, RConst c, RCol i) -> cmpOpOf op |> Option.bind (fun o -> leaf (Kernels.flip o) i c)
        | _ -> None

    /// One morsel of a compiled `Filter` (Phase 375; the loop Phase 266 and 270 wrote inline): the
    /// predicate compiled against `f`, run once per logical row `lo .. hi - 1` (the morsel) in
    /// logical order, stopping at the first row that records an error. The physical rows it keeps
    /// are written to `rows` from offset `lo` — a morsel keeps at most its own rows, so morsels
    /// write disjoint ranges — and it answers how many it kept and the error it met, if any. A
    /// typed boolean root is read unboxed.
    ///
    /// The ONE row loop of the step: the sequential evaluator runs it through `RunMorsels`, and a
    /// runner holding the step as data (`MorselHandOff`) runs it over the frame it rebuilt, so the
    /// two cannot disagree on what a row keeps or which error it raises.
    let internal filterMorsel
        (f: Frame)
        (resolved: ResolvedExpr)
        (phys: int[])
        (rows: int[])
        (lo: int)
        (hi: int)
        : struct (int * EvalError option) =
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
            | NDays _
            | NCell _ ->
                let r = compiled.Run

                fun p ->
                    match r p with
                    | Bool true -> true
                    | _ -> false

        let mutable error = None
        let mutable count = 0
        let mutable i = lo

        while Option.isNone error && i < hi do
            slot.Error <- None
            let p = Raw.get i phys
            let keepIt = keep p

            match slot.Error with
            | Some e -> error <- Some e
            | None ->
                if keepIt then
                    Raw.set rows (lo + count) p
                    count <- count + 1

                i <- i + 1

        struct (count, error)

    /// The kept rows of a morselled `Filter`, in morsel order (Phase 375): morsel `j`'s `counts[j]`
    /// rows from its first row's offset (`j * morselRows`) in `rows`, morsel after morsel. Morsels
    /// are ranges of logical rows in order, so morsel order is row order.
    let internal keptInOrder (morselRows: int) (rows: int[]) (counts: int[]) : int[] =
        let mutable total = 0

        for j in 0 .. counts.Length - 1 do
            total <- total + counts[j]

        let out: int[] = Array.zeroCreate total
        let mutable at = 0

        for j in 0 .. counts.Length - 1 do
            let c = counts[j]

            if c > 0 then
                Array.blit rows (j * morselRows) out at c
                at <- at + c

        out

    let private evalFilterChecked
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
            //
            // Phase 375: the morsel's row loop is `filterMorsel`, the one a worker runs over the step
            // as data (`MorselHandOff`), and the kept rows land in one buffer, morsel `j`'s from its
            // first row's offset, concatenated in morsel order by `keptInOrder`.
            let phys = Frame.physical f
            let m = Kernels.morselCount phys.Length
            let rows: int[] = Array.zeroCreate phys.Length
            let counts: int[] = Array.zeroCreate m
            let errors: EvalError option[] = Array.create m None

            let run (j: int) : bool =
                let struct (count, error) =
                    filterMorsel f resolved phys rows (Kernels.morselStart j) (Kernels.morselEnd phys.Length j)

                counts[j] <- count
                errors[j] <- error
                Option.isNone error

            k.RunMorsels m run

            match Array.tryPick id errors with
            | Some e -> Error e
            | None -> Ok(Frame.select f (keptInOrder Kernels.MorselRows rows counts))

    let private evalProject (f: Frame) (pairs: (string * string) list) : Result<Frame, EvalError> =
        let resolve (src, out) =
            match colIndex f.Cols src with
            | None -> Error(UnknownColumn(src, available f.Cols))
            | Some i -> Ok(out, List.item i f.Cols, i)

        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | p :: rest -> resolve p |> Result.bind (fun r -> go (r :: acc) rest)

        // Every source resolves first, then the output names are checked (Phase 468, D35).
        go [] pairs
        |> Result.bind (fun resolved ->
            match projectRefusal pairs with
            | Some e -> Error e
            | None ->
                let idx = resolved |> List.map (fun (_, _, i) -> i) |> List.toArray
                Ok(Frame.project f idx (resolved |> List.map (fun (o, field, _) -> Fields.rename o field))))

    /// A step's compiled tree has one shape: `compileExpr` is a function of the frame and the
    /// resolved expression, so every morsel's tree has the kind of root the first one has.
    let private sameShape () : 'a =
        invalidOp "a step's compiled trees disagree on their root's kind"

    /// The int, float and bool roots of a compiled tree (Phase 375), for `rootOf`.
    let internal pickInt (n: Node) : (int -> int) option =
        match n with
        | NInt r -> Some r
        | _ -> None

    let internal pickFloat (n: Node) : (int -> float) option =
        match n with
        | NFloat r -> Some r
        | _ -> None

    let internal pickBool (n: Node) : (int -> bool) option =
        match n with
        | NBool r -> Some r
        | _ -> None

    /// The typed root of a compiled `Derive`'s tree, read through `pick` (Phase 375): every morsel's
    /// tree has the first one's kind of root, so another kind is the compiler disagreeing with
    /// itself.
    let internal rootOf (pick: Node -> (int -> 'a) option) (c: CompiledExpr) : int -> 'a =
        match pick c.Node with
        | Some r -> r
        | None -> sameShape ()

    /// One morsel of a compiled `Derive` with a typed root (Phase 375; the loop Phase 266 and 270
    /// wrote inline): `read` (the root of a tree compiled for this morsel) run once per logical row
    /// `lo .. hi - 1` (the morsel) in logical order, stopping at the first row that records an
    /// error. A row whose
    /// value is present is handed to `put` at its PHYSICAL position — morsels write disjoint
    /// positions — and it answers the error it met, if any.
    ///
    /// The ONE row loop of the step, inlined at both of its sites so neither pays an indirect call
    /// a row for `put`: the sequential evaluator's, which puts into the column's carrier and mask,
    /// and a runner holding the step as data (`MorselHandOff`), which puts into its result slots.
    let inline internal deriveMorsel
        (slot: ErrorSlot)
        (read: int -> 'a)
        (phys: int[])
        (lo: int)
        (hi: int)
        ([<InlineIfLambda>] put: int -> 'a -> unit)
        : EvalError option =
        let mutable error = None
        let mutable i = lo

        while Option.isNone error && i < hi do
            slot.Error <- None
            let p = Raw.get i phys
            let v = read p

            match slot.Error with
            | Some e -> error <- Some e
            | None ->
                if not slot.Null then
                    put p v

                i <- i + 1

        error

    /// A derived column with no present cell (Phase 338): a decided column keeps its type, packed
    /// empty under it; one the cells type is `StringType`, as it always was.
    let internal derivedNonePresent (dt: DerivedTyping) (count: int) : ColumnType * Vec =
        let ty =
            match dt with
            | Decided ty -> ty
            | ByCells
            | Refused -> StringType

        ty, Vec.pack ty (Array.create count Null)

    /// A derived column from a typed root's carrier and mask, once every morsel has run (Phase 375,
    /// shared by the sequential evaluator and the step as data). A typed vector holds one type, so
    /// the cells' join is that type and cannot refuse. Where the column is decided, the decided type
    /// is the column's; a typed root of another type would be the typer and the compiler disagreeing
    /// — its cells are kept as produced, boxed under the decided type, and the derive-typing law
    /// (`Conformance.deriveTypingLaws`) is what goes red on it.
    let internal derivedTyped
        (dt: DerivedTyping)
        (count: int)
        (phys: int[])
        (nodeTy: ColumnType)
        (v: Vec)
        (mask: Mask)
        : ColumnType * Vec =
        if not (Vec.anyPresent mask phys) then
            derivedNonePresent dt count
        else
            match dt with
            | Decided ty when ty <> nodeTy -> ty, Cells(Array.init count (Vec.cellAt v))
            | Decided _
            | ByCells
            | Refused -> nodeTy, v

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

        let nonepresent () : ColumnType * Vec = derivedNonePresent dt count

        // A typed root (Phase 375): each morsel through `deriveMorsel`, its own tree's root put
        // straight into the carrier at the row's physical position.
        // The carrier and its mask are allocated together by `alloc` and the carrier written by `put`
        // (Phase 376): a bool root's values are a `Mask`, and int and float carriers are typed arrays,
        // in one buffer with the mask (in shared memory once a host has opted in to the worker pool).
        let inline typed
            (pick: Node -> (int -> 'a) option)
            (alloc: int -> struct ('c * Mask))
            (put: 'c -> int -> 'a -> unit)
            (mk: 'c -> Mask -> Vec)
            (nodeTy: ColumnType)
            : Result<ColumnType * Vec, EvalError> =
            let struct (vals, mask) = alloc count

            k.RunMorsels m (fun j ->
                let c = if j = 0 then first else compileExpr f resolved

                let error =
                    deriveMorsel
                        c.Slot
                        (rootOf pick c)
                        phys
                        (Kernels.morselStart j)
                        (Kernels.morselEnd n j)
                        (fun p v ->
                            put vals p v
                            Mask.put mask p true)

                errors[j] <- error
                Option.isNone error)

            match Array.tryPick id errors with
            | Some e -> Error e
            | None -> Ok(derivedTyped dt count phys nodeTy (mk vals mask) mask)

        let derived =
            match first.Node with
            | NInt _ -> typed pickInt Shared.intsAndMask Raw.put (fun v m -> Ints(v, m)) IntType
            // A float root's value is put with the canonical NaN (Phase 404), as the hand-off's is.
            | NFloat _ ->
                typed
                    pickFloat
                    Shared.floatsAndMask
                    (fun vals p v -> Raw.put vals p (canonicalNaN v))
                    (fun v m -> Floats(v, m))
                    FloatType
            | NBool _ -> typed pickBool Shared.boolsAndMask Mask.put (fun v m -> Bools(v, m)) BoolType
            // A day root writes the day count straight into a `Dates` vector (Phase 423).
            | NDays _ ->
                typed
                    (function
                    | NDays r -> Some r
                    | _ -> None)
                    Shared.intsAndMask
                    Raw.put
                    (fun v m -> Dates(v, m))
                    DateType
            | NStr(sty, _) ->
                // A date or an instant the kernels computed as text is held as the integer temporal
                // vector of its type (Phase 423): the frame carries no temporal column as text.
                typed
                    (function
                    | NStr(_, r) -> Some r
                    | _ -> None)
                    (fun n -> struct ((Array.zeroCreate n: string[]), Shared.mask n))
                    Raw.put
                    (fun v m -> Vec.ofTemporalTexts sty v m)
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
                                cells[i] <- canonicalNaNCell cell)

                match failed with
                | Some e -> Error e
                | None ->
                    columnTypeBy dt name cells.Length (fun i -> cells[i])
                    |> Result.map (fun ty -> ty, Vec.packAt ty count (fun i -> phys[i]) cells)

        derived |> Result.map (fun (ty, vec) -> Frame.withColumn f name ty vec)

    /// The predicate's units are checked over the schema before any row (Phase 429), then the
    /// resolved predicate is evaluated.
    let private evalFilter
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (pred: ColExpr)
        : Result<Frame, EvalError> =
        resolveUnitsIn "filter" f.Cols pred
        |> Result.bind (fun (_, pred) -> evalFilterChecked k env f pred)

    let private evalDerive
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (name: string)
        (expr: ColExpr)
        : Result<Frame, EvalError> =
        // The column's unit (Phase 429) and typing (Phase 338) are read off the schema before any
        // row: a derive refused statically is refused on every frame, an empty one included, and a
        // decided one carries its type, and its unit, whatever rows the frame holds.
        resolveUnitsIn ("derive '" + name + "'") f.Cols expr
        |> Result.bind (fun (unit, expr) ->
            match derivedTypingIn env f.Cols expr with
            | Refused -> Error(floatBesideDecimal name)
            | dt -> evalDerivedColumn k env f name expr dt |> Result.map (unitised name unit))

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
            | Timestamp _, TimestampType _ -> ValueSome c
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
                | Strs(t, _, m) when t = ty -> [||], [||], m, 3, 0
                | Dates(_, m) when ty = DateType -> [||], [||], m, 3, 0
                | Stamps(u, _, _, m) when ty = TimestampType u -> [||], [||], m, 3, 0
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

            /// Does every slot defer, whatever it is fed (`Median`, `StdDev`, `CountDistinct`, and a
            /// numeric aggregate over a non-numeric column)?
            member _.DefersAll: bool = mode = MDefer

            /// Fold the row at physical row `p` into slot `g`.
            member _.Feed(g: int, p: int) : unit =
                if mode <> MDefer && not deferred[g] then
                    if kind = 0 then
                        feedCell g p
                    else
                        match mode with
                        | MCount ->
                            if Mask.at p mask || countsNulls then
                                count[g] <- count[g] + 1
                        | MFirst ->
                            if row[g] < 0 then
                                row[g] <- p
                        | MLast -> row[g] <- p
                        | MSumInt ->
                            if Mask.at p mask then
                                longs[g] <- longs[g] + int64 ints[p]
                                count[g] <- count[g] + 1
                        | MSumDecimal when kind = 4 ->
                            if Mask.at p mask then
                                addScaled g floats[p]
                        | MSumFloat
                        | MMean when kind <> 4 ->
                            if Mask.at p mask then
                                addFloat g (if kind = 1 then float ints[p] else floats[p])
                        | MMin
                        | MMax when kind = 1 ->
                            if Mask.at p mask then
                                count[g] <- count[g] + 1
                                let b = row[g]

                                if b < 0 || replaces (compare ints[b] ints[p]) then
                                    row[g] <- p
                        | MMin
                        | MMax when kind = 2 ->
                            if Mask.at p mask then
                                count[g] <- count[g] + 1
                                let b = row[g]

                                if b < 0 || replaces (orderFloat nanLast floats[b] floats[p]) then
                                    row[g] <- p
                        | _ -> feedCell g p

            /// Fold the logical rows `i` from `lo` below `hi` of a frame — physical row `phys[i]` —
            /// into their slots `slotOf[i]`, in logical order. The loops a GroupBy spends its time in
            /// are written out for the typed carriers; every other case is `Feed` row by row.
            member this.FeedRange(slotOf: int[], phys: int[], lo: int, hi: int) : unit =
                // The loops prove `i` for `phys` and `slotOf`, checked once here (Phase 326); the
                // physical row and the slot each reads are indexes they do not prove.
                Raw.within hi slotOf
                Raw.within hi phys

                if mode = MDefer then
                    ()
                elif mode = MCount && kind <> 0 && not countsNulls then
                    for i in lo .. hi - 1 do
                        if Mask.at (Raw.get i phys) mask then
                            let g = Raw.get i slotOf
                            Raw.put count g (Raw.at g count + 1)
                elif mode = MSumInt && kind = 1 then
                    for i in lo .. hi - 1 do
                        let p = Raw.get i phys

                        if Mask.at p mask then
                            let g = Raw.get i slotOf
                            Raw.put longs g (Raw.at g longs + int64 (Raw.at p ints))
                            Raw.put count g (Raw.at g count + 1)
                elif (mode = MSumFloat || mode = MMean) && kind = 2 then
                    for i in lo .. hi - 1 do
                        let p = Raw.get i phys

                        if Mask.at p mask then
                            let g = Raw.get i slotOf
                            let f = Raw.at p floats
                            Raw.put totals g (Raw.at g totals + f)
                            Raw.put count g (Raw.at g count + 1)

                            if not (isFinite f) then
                                Raw.put nonFinite g true
                elif mode = MSumDecimal && kind = 4 then
                    for i in lo .. hi - 1 do
                        let p = Raw.get i phys

                        if Mask.at p mask then
                            addScaled (Raw.get i slotOf) (Raw.at p floats)
                else
                    for i in lo .. hi - 1 do
                        this.Feed(Raw.get i slotOf, Raw.get i phys)

            /// `FeedRange` over every logical row.
            member this.FeedAll(slotOf: int[], phys: int[]) : unit =
                this.FeedRange(slotOf, phys, 0, phys.Length)

            /// Whether slots fed by ranges of `rows` rows, each range into a stream of its own, and
            /// merged in range order (`MergeFrom`) answer what one feed in row order does (D68):
            /// counts and int sums add exactly; `First`, `Last`, `Min` and `Max` keep a row, and the
            /// earlier range's row against the later one's is the comparison the later rows would
            /// have made; an int `Mean` adds integers in floats, exactly while the total is below
            /// 2^53, which `rows` of int32 values below 2^22 keep it. A float or decimal sum does not:
            /// added by ranges, its total rounds at other places.
            member _.Mergeable(rows: int) : bool =
                match mode with
                | MCount
                | MSumInt
                | MFirst
                | MLast -> true
                | MMin
                | MMax -> kind = 1 || kind = 2
                | MMean -> kind = 1 && rows <= 4194304
                | _ -> false

            /// A stream of the same aggregate over the same vector, fed nothing.
            member _.Fresh() : Stream = Stream(fn, ty, v, groups, perturbation)

            member internal _.Parts = struct (deferred, count, row, longs, totals)

            /// Merge `o`, which was fed rows after every row this one was fed, into slots `lo` below
            /// `hi` (D68; only where `Mergeable`).
            member _.MergeFrom(o: Stream, lo: int, hi: int) : unit =
                let struct (od, oc, orow, ol, ot) = o.Parts

                for g in lo .. hi - 1 do
                    if od[g] then
                        deferred[g] <- true

                    if mode = MCount then
                        count[g] <- count[g] + oc[g]
                    elif mode = MSumInt then
                        longs[g] <- longs[g] + ol[g]
                        count[g] <- count[g] + oc[g]
                    elif mode = MMean then
                        totals[g] <- totals[g] + ot[g]
                        count[g] <- count[g] + oc[g]
                    elif mode = MFirst then
                        if row[g] < 0 then
                            row[g] <- orow[g]
                    elif mode = MLast then
                        if orow[g] >= 0 then
                            row[g] <- orow[g]
                    elif mode = MMin || mode = MMax then
                        count[g] <- count[g] + oc[g]
                        let l = orow[g]

                        if l >= 0 then
                            let b = row[g]

                            if
                                b < 0
                                || replaces (
                                    if kind = 1 then
                                        compare ints[b] ints[l]
                                    else
                                        orderFloat nanLast floats[b] floats[l]
                                )
                            then
                                row[g] <- l

            /// Whether `Emit` writes only an int, a float or a null of the output's carrier, so slots
            /// may be emitted on several threads into one output (D68): a count, a sum or a mean
            /// (its fallback answers a number or a null), and a row-keeping aggregate over an int or
            /// float carrier. Any other may write a cell that unpacks the output.
            member _.EmitsTyped: bool =
                mode = MCount
                || mode = MSumInt
                || mode = MSumFloat
                || mode = MMean
                || ((mode = MFirst || mode = MLast || mode = MMin || mode = MMax)
                    && (kind = 1 || kind = 2))

            /// Slot `g`'s answer, or `ValueNone` where it defers to `Column.aggregate`. A NaN answer
            /// leaves canonical (Phase 404), exactly as the deferred path's does.
            member this.TryCell(g: int) : Cell voption =
                match this.Accumulated g with
                | ValueSome c -> ValueSome(canonicalNaNCell c)
                | ValueNone -> ValueNone

            /// Slot `g`'s answer as its accumulator holds it, before the NaN is made canonical.
            member private _.Accumulated(g: int) : Cell voption =
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

                        if p < 0 || not (Mask.at p mask) then out.Null g
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
            let mask: Mask = if typedInt || typedFloat then Array.zeroCreate n else [||]
            let mutable typed = typedInt || typedFloat
            let mutable cells: Cell[] = if typed then [||] else Array.create n Null

            let unpack () =
                cells <-
                    Array.init n (fun i ->
                        if not (Mask.at i mask) then Null
                        elif typedInt then Int ints[i]
                        else Float floats[i])

                typed <- false

            member _.Null(i: int) : unit =
                if not typed then
                    cells[i] <- Null

            member _.Int(i: int, x: int) : unit =
                if typed && typedInt then
                    ints[i] <- x
                    Mask.put mask i true
                else
                    if typed then
                        unpack ()

                    cells[i] <- Int x

            /// Every float answer a `GroupBy` or `Pivot` writes passes here — a stream's unboxed write
            /// and every cell written through `Cell` — so it lands with the canonical NaN (Phase 404).
            member _.Float(i: int, x: float) : unit =
                let x = canonicalNaN x

                if typed && typedFloat then
                    floats[i] <- x
                    Mask.put mask i true
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

        /// Feed `s` every logical row, as `FeedAll` does, across threads where `k` partitions the rows,
        /// the stream merges exactly (`Mergeable`) and the slots are few beside the rows (D68): the
        /// first of at most 16 ranges into `s`, each other into a fresh stream, on the pool; then each
        /// fresh stream merged into `s` in range order, slots split across the pool where they are many.
        /// Where a slot per range for every range would outnumber the rows, the merge is the feed's
        /// cost again, and `s` is fed alone.
        let feedAcross (k: KernelSet) (s: Stream) (groups: int) (slotOf: int[]) (phys: int[]) : unit =
            let n = phys.Length
            let parts = min 16 (k.Partitions n)

            if parts <= 1 || not (s.Mergeable n) || int64 groups * int64 parts > int64 n then
                s.FeedAll(slotOf, phys)
            else
                let bound = Ranges.bound n parts
                let fed = Array.init parts (fun j -> if j = 0 then s else s.Fresh())

                k.RunMorsels parts (fun j ->
                    fed[j].FeedRange(slotOf, phys, bound j, bound (j + 1))
                    true)

                let slices = k.Partitions groups

                let at = Ranges.bound groups slices

                k.RunMorsels slices (fun r ->
                    for j in 1 .. parts - 1 do
                        s.MergeFrom(fed[j], at r, at (r + 1))

                    true)

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

        /// A direct table over the int values `[Lo, Hi]`, empty until built: a value in the range is
        /// coded by an array read instead of a dictionary probe, and through the table only, so the
        /// table and the dictionary beside it never hold one value twice.
        type Direct() =
            member val Lo = 0 with get, set
            member val Hi = -1 with get, set
            member val Table: int[] = [||] with get, set

        /// A direct table over a decimal column's unscaled values at one scale (D62), empty until
        /// built: `Lo` is the smallest unscaled value at `Scale`, and a value is coded through it
        /// exactly when it is exact at `Scale` and its unscaled value there is in range.
        type ScaledDirect() =
            member val Scale = -1 with get, set
            member val Lo = 0.0 with get, set
            member val Table: int[] = [||] with get, set

        /// One key column's codes: a dictionary per carrier, one counter over all of them, so two
        /// values share a code exactly when the relation equates them.
        ///
        /// A date, a timestamp and a decimal are coded by VALUE where their canonical text is the
        /// value's (D61): a date by its day count (`Days`, with a direct table of its own); an instant
        /// by its epoch microsecond as one 64-bit key (`Micros`, D62), or by its second and nanosecond
        /// where it has a sub-microsecond digit (`Instants`); a decimal through a direct table at one
        /// scale where it fits (`DecDirect`, D62), and otherwise by its unscaled integer with trailing
        /// fraction zeros removed, beside that scale, as one 64-bit key (`DecVals`). Which way a value
        /// goes depends on the value alone, so one value has one code. Canonical texts are equal
        /// exactly when those values are, so the codes are the codes the texts had. Any other text
        /// keeps its text code (`Dates`, `Stamps`, `Decs`), and no such text is any value's canonical
        /// text, so it still equals only itself.
        type Coder =
            {
                CellEq: bool
                Ints: CodeMap<int>
                Nums: CodeMap<float>
                Strs: CodeMap<string>
                Dates: CodeMap<string>
                Stamps: CodeMap<string>
                Decs: CodeMap<string>
                Days: CodeMap<int>
                Micros: CodeMap<int64>
                Instants: CodeMap<struct (float * int)>
                DecVals: CodeMap<int64>
                Specials: int[]
                /// The int direct table (token relation only): built from the first int vector a
                /// fresh coder meets when its range is small.
                IntDirect: Direct
                /// The day direct table (either relation: a date equals only a date): built from the
                /// first date vector the coder meets before it has coded any day, when its range is small.
                DayDirect: Direct
                /// The decimal direct table: built from the first decimal vector the coder meets before
                /// it has coded any decimal, at that vector's scale, when its range is small.
                DecDirect: ScaledDirect
                /// A hasher (D66): a coder that opens nothing and answers, for a value it has not met,
                /// the hash of the key it would open, so that equal values hash alike. Its dictionaries
                /// stay empty, so threads may share one.
                Hashing: bool
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

        // An instant's second is a whole number and never `-0.0` (`instantCode` adds `0.0`).
        let private instants () : CodeMap<struct (float * int)> =
            OpenSlots<struct (float * int)>(
                (fun (struct (s, n)) -> hash s * 31 + n),
                (fun (struct (s1, n1)) (struct (s2, n2)) -> s1 = s2 && n1 = n2)
            )

        let private longs () : CodeMap<int64> =
            OpenSlots<int64>((fun i -> hash i), (fun a b -> a = b))

        let coder (cellEq: bool) : Coder =
            { CellEq = cellEq
              Ints = ints ()
              Nums = nums ()
              Strs = strs ()
              Dates = strs ()
              Stamps = strs ()
              Decs = strs ()
              Days = ints ()
              Micros = longs ()
              Instants = instants ()
              DecVals = longs ()
              Specials = Array.create 4 -1
              IntDirect = Direct()
              DayDirect = Direct()
              DecDirect = ScaledDirect()
              Hashing = false
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
              Days = System.Collections.Generic.Dictionary<int, int>()
              Micros = System.Collections.Generic.Dictionary<int64, int>()
              Instants = System.Collections.Generic.Dictionary<struct (float * int), int>()
              DecVals = System.Collections.Generic.Dictionary<int64, int>()
              Specials = Array.create 4 -1
              IntDirect = Direct()
              DayDirect = Direct()
              DecDirect = ScaledDirect()
              Hashing = false
              Count = 0 }
#endif

        /// `k`'s code in `d`, opening the next one when `openNew` and it has none; `-1` otherwise.
        /// A hasher's answer for a key it has not met (D66): a hash that agrees with the dictionaries' equality.
        let private keyHash (k: 'k) : int =
            System.Collections.Generic.EqualityComparer<'k>.Default.GetHashCode k

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
                elif c.Hashing then
                    // A hasher's count is 0. The addition keeps the call out of tail position: F# marks a
                    // call there `.tail`, and the JIT then compiles every method this is inlined into,
                    // `intCode` and `directCode` among them, without tiering or inlining them, which
                    // cost an int key column five times its coding time.
                    keyHash k + c.Count
                else
                    -1
#endif

        /// The rows a key column codes before its dictionary is sized (D64).
        let private sample = 1024

        /// After a key column's first `sample` rows: where more than half of them opened a key in `d`
        /// (since `before`), room in `d` for the column's `n` rows at that rate (D64). A dictionary
        /// grown one doubling at a time copies every entry it holds at each doubling, and at 100,000
        /// keys its arrays are past the large-object threshold. A column whose sample repeats its keys
        /// is left to grow, so a group-by over a few groups reserves nothing.
        let private sizeFor (d: CodeMap<'k>) (before: int) (n: int) : unit =
            let opened = d.Count - before

            if opened * 2 > sample then
                let want = before + int (int64 opened * int64 n / int64 sample)
#if FABLE_COMPILER
                d.Reserve want
#else
                d.EnsureCapacity want |> ignore
#endif

        /// Whether row `i` of `n` is where a column that opens codes sizes its dictionary.
        let inline private sizesAt (i: int) (n: int) (openNew: bool) : bool =
            openNew && i = sample && n >= 4 * sample

        let private special (c: Coder) (s: int) (openNew: bool) : int =
            let x = c.Specials[s]

            if x >= 0 then
                x
            elif openNew then
                let y = c.Count
                c.Specials[s] <- y
                c.Count <- y + 1
                y
            elif c.Hashing then
                s
            else
                -1

        /// A float's code: every `NaN` one value, `-0.0` with `0.0` (adding `0.0` turns `-0.0` into
        /// `0.0` and leaves every other float as it is), anything else by IEEE equality.
        let private floatCode (c: Coder) (f: float) (openNew: bool) : int =
            if System.Double.IsNaN f then
                special c SNaN openNew
            else
                codeIn c c.Nums (f + 0.0) openNew

        /// `i`'s code through the direct table `t` where it is in range, through `d` otherwise.
        let private directCode (c: Coder) (t: Direct) (d: CodeMap<int>) (i: int) (openNew: bool) : int =
            // Compared against both ends, never as `i - Lo`, which overflows for a far value.
            if i >= t.Lo && i <= t.Hi then
                let at = i - t.Lo
                let x = Raw.get at t.Table

                if x >= 0 || not openNew then
                    x
                else
                    let y = c.Count
                    Raw.set t.Table at y
                    c.Count <- y + 1
                    y
            else
                codeIn c d i openNew

        /// Build `t` over the values of `a` that are present (`m`) at `phys` and that `keep` admits,
        /// when their range is at most a small multiple of the rows; otherwise leave it empty.
        let private buildDirect (t: Direct) (a: int[]) (m: Mask) (phys: int[]) (keep: int -> bool) : unit =
            let mutable lo = System.Int32.MaxValue
            let mutable hi = System.Int32.MinValue

            for p in phys do
                if Mask.at p m then
                    let x = Raw.at p a

                    if keep x then
                        if x < lo then
                            lo <- x

                        if x > hi then
                            hi <- x

            // The range as a float, so a span across the whole int range cannot overflow.
            if lo <= hi && float hi - float lo < float (max 1024 (4 * phys.Length)) then
                t.Lo <- lo
                t.Hi <- hi
                t.Table <- Array.create (hi - lo + 1) -1

        let private intCode (c: Coder) (i: int) (openNew: bool) : int =
            if c.CellEq then
                floatCode c (float i) openNew
            else
                directCode c c.IntDirect c.Ints i openNew

        /// Give a FRESH token coder a direct table over an int vector's present values.
        let private intTableFor (c: Coder) (a: int[]) (m: Mask) (phys: int[]) : unit =
            if not c.CellEq && c.Count = 0 && c.IntDirect.Table.Length = 0 then
                buildDirect c.IntDirect a m phys (fun _ -> true)

        /// Give a coder that has coded no day yet a direct table over a date vector's days in range.
        let private dayTableFor (c: Coder) (a: int[]) (m: Mask) (phys: int[]) : unit =
            if c.Days.Count = 0 && c.DayDirect.Table.Length = 0 then
                buildDirect c.DayDirect a m phys TemporalText.isDayInRange

        /// A day's code; `days` is in range, so its text is canonical.
        let private dayCode (c: Coder) (days: int) (openNew: bool) : int =
            directCode c c.DayDirect c.Days days openNew

        /// A date text's code: by its day where it is canonical, by the text otherwise.
        let private dateTextCode (c: Coder) (s: string) (openNew: bool) : int =
            match TemporalText.tryDays s with
            | Some days -> dayCode c days openNew
            | None -> codeIn c c.Dates s openNew

        /// An instant's code; the instant is in range. By its epoch microsecond, one 64-bit key, where
        /// it is a whole microsecond (the canonical range is under 3.2e17 microseconds either side of
        /// the epoch); by its second and nanosecond otherwise.
        let private instantCode (c: Coder) (second: float) (nanos: int) (openNew: bool) : int =
            if nanos % 1000 = 0 then
                codeIn c c.Micros (int64 second * 1000000L + int64 (nanos / 1000)) openNew
            else
                codeIn c c.Instants (struct (second + 0.0, nanos)) openNew

        /// A timestamp text's code: by its instant where the text is that instant's canonical text,
        /// by the text otherwise.
        let private stampTextCode (c: Coder) (s: string) (openNew: bool) : int =
            match TemporalText.tryInstant TimeUnit.Nanoseconds s with
            | Some(second, nanos) when TemporalText.instantText TimeUnit.Nanoseconds second nanos = s ->
                instantCode c second nanos openNew
            | _ -> codeIn c c.Stamps s openNew

        /// Give a coder that has coded no decimal yet a direct table over a decimal vector's unscaled
        /// values at its scale, when their range is at most a small multiple of the rows.
        let private decTableFor (c: Coder) (scaled: float[]) (scale: int) (m: Mask) (phys: int[]) : unit =
            let t = c.DecDirect

            if t.Scale < 0 && c.DecVals.Count = 0 then
                let mutable lo = System.Double.MaxValue
                let mutable hi = System.Double.MinValue

                for p in phys do
                    if Mask.at p m then
                        let x = Raw.at p scaled

                        if x < lo then
                            lo <- x

                        if x > hi then
                            hi <- x

                if lo <= hi && hi - lo < float (max 1024 (4 * phys.Length)) then
                    t.Scale <- scale
                    t.Lo <- lo
                    t.Table <- Array.create (int (hi - lo) + 1) -1

        /// A decimal's code by value: `unscaled` at `scale` (at most the width; an exact integer of
        /// magnitude below 10^15). Through the direct table where the value is exact at the table's
        /// scale and in its range; otherwise by one 64-bit key, the unscaled value with its trailing
        /// fraction zeros removed beside the scale left, so that one value has one key.
        let private decCode (c: Coder) (unscaled: float) (scale: int) (openNew: bool) : int =
            let t = c.DecDirect

            // The value's unscaled integer at the table's scale, where it is exact there: scaling up
            // multiplies an integer by a power of ten (a product past 2^53 is far past the range);
            // scaling down divides, and is exact only where the quotient is a whole number.
            let atTable =
                if t.Scale < 0 then
                    nan
                elif scale = t.Scale then
                    unscaled
                elif scale < t.Scale then
                    unscaled * ScaledDecimal.pow10[t.Scale - scale]
                else
                    let q = unscaled / ScaledDecimal.pow10[scale - t.Scale]
                    if q = floor q then q else nan

            let at = atTable - t.Lo

            if at >= 0.0 && at < float t.Table.Length then
                let i = int at
                let x = Raw.get i t.Table

                if x >= 0 || not openNew then
                    x
                else
                    let y = c.Count
                    Raw.set t.Table i y
                    c.Count <- y + 1
                    y
            else
                let mutable u = int64 unscaled
                let mutable k = scale

                while k > 0 && u % 10L = 0L do
                    u <- u / 10L
                    k <- k - 1

                // A magnitude below 10^15 times 16 is far inside 64 bits, and the scale is in 0 .. 15,
                // so the key is one value's alone; `int64` of a negative zero is zero, as its text is.
                codeIn c c.DecVals (u * 16L + int64 k) openNew

        /// A decimal text's code: by value where it fits the width at its own scale, by its
        /// canonical text (`Cell.token`) otherwise.
        let private decTextCode (c: Coder) (s: string) (openNew: bool) : int =
            let byText () =
                codeIn c c.Decs (Cell.token (Decimal s)) openNew

            match ScaledDecimal.shape s with
            | ValueSome(struct (intDigits, fracDigits)) when intDigits + fracDigits <= ScaledDecimal.Width ->
                match ScaledDecimal.tryScaled fracDigits s with
                | ValueSome u -> decCode c u fracDigits openNew
                | ValueNone -> byText ()
            | _ -> byText ()

        let private nullCode (c: Coder) (openNew: bool) : int =
            if c.CellEq then -1 else special c SNull openNew

        let private boolCode (c: Coder) (b: bool) (openNew: bool) : int =
            special c (if b then STrue else SFalse) openNew

        /// How a string carrier's text is coded: as the cell it would read back as (`Vec.strCell`'s mapping).
        let private strCodeOf (c: Coder) (ty: ColumnType) : string -> bool -> int =
            match ty with
            | DateType -> dateTextCode c
            | TimestampType _ -> stampTextCode c
            | StringType
            | IntType
            | FloatType
            | BoolType
            | DecimalType -> fun s openNew -> codeIn c c.Strs s openNew

        /// One cell's code — the boxed path, agreeing with every typed one above case for case.
        let cellCode (c: Coder) (cell: Cell) (openNew: bool) : int =
            match cell with
            | Null -> nullCode c openNew
            | Int i -> intCode c i openNew
            | Float f -> floatCode c f openNew
            | Bool b -> boolCode c b openNew
            | Str s -> codeIn c c.Strs s openNew
            | Date s -> dateTextCode c s openNew
            | Timestamp s -> stampTextCode c s openNew
            // By value (Phase 277, D61): `1.50` and `1.5` are one value, in both relations.
            | Decimal s -> decTextCode c s openNew

        /// Give a coder the direct table `v`'s carrier codes through, where it has none yet, over the
        /// values at `phys`.
        let private tablesFor (c: Coder) (v: Vec) (phys: int[]) : unit =
            match v with
            | Ints(a, m) -> intTableFor c a m phys
            | Decs(scaled, scale, _, m) -> decTableFor c scaled scale m phys
            | Dates(a, m) -> dayTableFor c a m phys
            | _ -> ()

        /// The code of the value at each physical row `phys[i]` of `v`, for `i` from `lo` below `hi`,
        /// into `out[i]`, read from the carrier. No direct table is built here (`codesOf` builds one):
        /// a coder that has none codes through its dictionaries, with the same codes. A row's code
        /// lands at the row's own index, so every loop indexes both arrays by its counter, the shape
        /// the .NET JIT checks once per loop rather than once per row; an offset between the two kept
        /// both checks, at 11 to 13 per cent of an int grouping (D65). A dictionary is sized (D64) for
        /// `rows`, the rows the coder is about to code from `lo`, which a range coded first passes as
        /// the column's.
        let private codesInto
            (c: Coder)
            (v: Vec)
            (phys: int[])
            (lo: int)
            (hi: int)
            (rows: int)
            (openNew: bool)
            (out: int[])
            : unit =

            match v with
            | Ints(a, m) ->
                for i in lo .. hi - 1 do
                    let p = Raw.get i phys

                    let code =
                        if Mask.at p m then
                            intCode c (Raw.at p a) openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            | Floats(a, m) ->
                for i in lo .. hi - 1 do
                    let p = Raw.get i phys

                    let code =
                        if Mask.at p m then
                            floatCode c (Raw.at p a) openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            | Bools(a, m) ->
                for i in lo .. hi - 1 do
                    let p = Raw.get i phys

                    let code =
                        if Mask.at p m then
                            boolCode c (Mask.at p a) openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            | Strs(ty, a, m) ->
                let code = strCodeOf c ty

                // The dictionary a text of this type opens its keys in, where it is canonical.
                let size: unit -> unit =
                    match ty with
                    | DateType ->
                        let b = c.Days.Count
                        fun () -> sizeFor c.Days b rows
                    | TimestampType _ ->
                        let b = c.Micros.Count
                        fun () -> sizeFor c.Micros b rows
                    | StringType
                    | IntType
                    | FloatType
                    | BoolType
                    | DecimalType ->
                        let b = c.Strs.Count
                        fun () -> sizeFor c.Strs b rows

                for i in lo .. hi - 1 do
                    if sizesAt (i - lo) rows openNew then
                        size ()

                    let p = Raw.get i phys

                    let code =
                        if Mask.at p m then
                            code (Raw.at p a) openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            // A decimal key is coded by value off the scaled carrier (D61), which is every present
            // row's value at the column's one scale; the texts are not read.
            | Decs(scaled, scale, _, m) ->
                let before = c.DecVals.Count

                for i in lo .. hi - 1 do
                    if sizesAt (i - lo) rows openNew then
                        sizeFor c.DecVals before rows

                    let p = Raw.get i phys

                    let code =
                        if Mask.at p m then
                            decCode c (Raw.at p scaled) scale openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            // A temporal key is coded by value off its carrier (D61): a day in range by its day, an
            // instant in range by its second and nanosecond, and anything else by the text it renders.
            | Dates(a, m) ->
                for i in lo .. hi - 1 do
                    let p = Raw.get i phys

                    let code =
                        if Mask.at p m then
                            let days = Raw.at p a

                            if TemporalText.isDayInRange days then
                                dayCode c days openNew
                            else
                                codeIn c c.Dates (TemporalText.dateText days) openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            | Stamps(u, s, f, m) ->
                let nanosPer = TimeUnit.scale TimeUnit.Nanoseconds / TimeUnit.scale u
                let before = c.Micros.Count

                for i in lo .. hi - 1 do
                    if sizesAt (i - lo) rows openNew then
                        sizeFor c.Micros before rows

                    let p = Raw.get i phys

                    let code =
                        if Mask.at p m then
                            let second = Raw.at p s
                            let fraction = Vec.fractionAt f p

                            if TemporalText.isInstantInRange u second fraction then
                                instantCode c second (fraction * nanosPer) openNew
                            else
                                codeIn c c.Stamps (Vec.stampText u s f p) openNew
                        else
                            nullCode c openNew

                    Raw.set out i code
            | Cells cells ->
                for i in lo .. hi - 1 do
                    let code = cellCode c (Raw.at (Raw.get i phys) cells) openNew
                    Raw.set out i code


        /// The code of the value at each physical row `phys[i]` of `v`, read from the carrier.
        let codesOf (c: Coder) (v: Vec) (phys: int[]) (openNew: bool) : int[] =
            let out: int[] = Array.zeroCreate phys.Length

            if openNew then
                tablesFor c v phys

            codesInto c v phys 0 phys.Length phys.Length openNew out
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

        /// Each code's first index in `codes`, whose codes are numbered from 0 in first-appearance
        /// order below `count`: the scan stops once the last code is met, so a key of five values
        /// reads a handful of rows, not every one.
        let private firstRows (codes: int[]) (count: int) : int[] =
            let first: int[] = Array.zeroCreate count
            let mutable seen = 0
            let mutable i = 0

            while seen < count && i < codes.Length do
                if Raw.get i codes = seen then
                    first[seen] <- i
                    seen <- seen + 1

                i <- i + 1

            first

        /// `firstRows`, across threads where the codes are many (D71): with codes numbered in
        /// first-appearance order, a row is its code's first exactly when its code is above every
        /// code before it. Each range takes its largest code; then each range, from the largest
        /// code of the ranges before it, writes the rows whose code rises above the largest so far.
        /// A few codes are met in the first rows, and the scan in order stops there.
        let private firstRowsAcross (k: KernelSet) (codes: int[]) (count: int) : int[] =
            let n = codes.Length
            let parts = min 16 (k.Partitions(min (n / 4) count))

            if parts <= 1 then
                firstRows codes count
            else
                let bound = Ranges.bound n parts
                let tops: int[] = Array.zeroCreate parts

                k.RunMorsels parts (fun j ->
                    let mutable m = -1

                    let upTo = bound (j + 1)


                    for i in bound j .. upTo - 1 do
                        if codes[i] > m then
                            m <- codes[i]

                    tops[j] <- m
                    true)

                let first: int[] = Array.zeroCreate count

                k.RunMorsels parts (fun j ->
                    let mutable m = -1

                    for r in 0 .. j - 1 do
                        if tops[r] > m then
                            m <- tops[r]

                    let upTo = bound (j + 1)


                    for i in bound j .. upTo - 1 do
                        let c = codes[i]

                        if c > m then
                            first[c] <- i
                            m <- c

                    true)

                first

        /// `firstRows` over `codes[lo .. hi - 1]`, a range coded by a coder of its own: each of its
        /// `count` codes' first index in `codes`.
        let private firstRowsIn (codes: int[]) (lo: int) (hi: int) (count: int) : int[] =
            let first: int[] = Array.zeroCreate count
            let mutable seen = 0
            let mutable i = lo

            while seen < count && i < hi do
                if codes[i] = seen then
                    first[seen] <- i
                    seen <- seen + 1

                i <- i + 1

            first

        /// Whether `c`, its direct table chosen for `v`, codes `v`'s values through its dictionaries.
        let private throughDictionaries (c: Coder) (v: Vec) : bool =
            match v with
            | Ints _ -> c.IntDirect.Table.Length = 0
            | Dates _ -> c.DayDirect.Table.Length = 0
            | Decs _ -> c.DecDirect.Scale < 0
            | _ -> true

        /// A column coded by partitions of its values (D66): each partition's coder, and the map from
        /// its codes to the column's.
        type Partitions =
            { Parts: int
              CellEq: bool
              Coders: Coder[]
              Maps: int[][] }

        /// The coder a column's codes are held by: one, or one a partition of its values.
        type Coded =
            | ByCoder of Coder
            | ByPartitions of Partitions

        /// The rows of `v` at `phys` split into `parts` partitions by the hash of the key a hasher in
        /// the relation `cellEq` answers for each value, so that equal values, in either column of a
        /// join, fall in one partition (D66, D69): each row's partition (`part`), and each
        /// partition's rows in row order, `order[start[q] .. start[q + 1] - 1]`, with their physical
        /// rows in `byPart`. Ranges are hashed and counted, then scattered, on the pool.
        let private partitionByHash
            (k: KernelSet)
            (parts: int)
            (cellEq: bool)
            (v: Vec)
            (phys: int[])
            : int[] * int[] * int[] * int[] =
            let n = phys.Length
            let bound = Ranges.bound n parts
            let hasher = { coder cellEq with Hashing = true }
            let part: int[] = Array.zeroCreate n
            let counts: int[][] = Array.init parts (fun _ -> Array.zeroCreate parts)

            k.RunMorsels parts (fun j ->
                let lo = bound j
                let hi = bound (j + 1)
                codesInto hasher v phys lo hi 0 false part
                let cs = counts[j]

                for i in lo .. hi - 1 do
                    // Fibonacci hashing: the high bits of the product, so close ints spread.
                    let q = int (((uint32 part[i] * 2654435769u) >>> 16) % uint32 parts)
                    part[i] <- q
                    cs[q] <- cs[q] + 1

                true)

            // Partition `q` holds `order[start[q] .. start[q + 1] - 1]`, range by range in row order.
            let start: int[] = Array.zeroCreate (parts + 1)
            let at: int[][] = Array.init parts (fun _ -> Array.zeroCreate parts)

            for q in 0 .. parts - 1 do
                let mutable o = start[q]

                for j in 0 .. parts - 1 do
                    at[j][q] <- o
                    o <- o + counts[j][q]

                start[q + 1] <- o

            let order: int[] = Array.zeroCreate n
            let byPart: int[] = Array.zeroCreate n

            k.RunMorsels parts (fun j ->
                let a = at[j]

                let upTo = bound (j + 1)


                for i in bound j .. upTo - 1 do
                    let q = part[i]
                    let t = a[q]
                    order[t] <- i
                    byPart[t] <- phys[i]
                    a[q] <- t + 1

                true)

            part, start, order, byPart

        /// The codes of `v` at `phys` and how many there are, as `codesOf (coder cellEq) v phys true`
        /// answers them, coded by partitions of the VALUES (D66; the `cellEq` relation for a join's
        /// build side, D69), and the partitions' coders, for a probe side to look up: a column of
        /// many distinct keys, where a merge of row ranges in row order costs what coding them does
        /// (D65).
        ///
        /// 1. The rows are split by the hash of their values (`partitionByHash`).
        /// 2. Each partition is coded on the pool by a coder of its own, which numbers its values in
        ///    the order their rows come, and no value is looked up in more than one coder.
        /// 3. A value's code is how many values first appear before its first row: the first rows
        ///    are marked, then counted range by range, so the codes are first-appearance codes.
        let private codesByHash
            (k: KernelSet)
            (parts: int)
            (cellEq: bool)
            (v: Vec)
            (phys: int[])
            : int[] * int * Partitions =
            let n = phys.Length
            let bound = Ranges.bound n parts
            let part, start, order, byPart = partitionByHash k parts cellEq v phys

            // In place: `codesInto` reads a row's physical row before it writes the row's code there.
            let local = byPart
            let opened: int[] = Array.zeroCreate parts
            let coders: Coder[] = Array.zeroCreate parts

            k.RunMorsels parts (fun q ->
                let lo = start[q]
                let hi = start[q + 1]
                let pc = coder cellEq
                codesInto pc v byPart lo hi (hi - lo) true local
                opened[q] <- pc.Count
                coders[q] <- pc
                true)

            // Each partition's first rows, marked; `part` is free again, and every entry is rewritten.
            let firsts: int[][] = Array.zeroCreate parts
            let rank = part

            k.RunMorsels parts (fun j ->
                Array.fill rank (bound j) (bound (j + 1) - bound j) 0
                true)

            k.RunMorsels parts (fun q ->
                let f = firstRowsIn local start[q] start[q + 1] opened[q]

                for x in 0 .. f.Length - 1 do
                    let r = order[f[x]]
                    f[x] <- r
                    rank[r] <- 1

                firsts[q] <- f
                true)

            let before: int[] = Array.zeroCreate (parts + 1)

            k.RunMorsels parts (fun j ->
                let mutable m = 0

                let upTo = bound (j + 1)


                for i in bound j .. upTo - 1 do
                    m <- m + rank[i]

                before[j + 1] <- m
                true)

            for j in 1..parts do
                before[j] <- before[j] + before[j - 1]

            // A marked row's mark is read before its rank replaces it, and each range rewrites only its own.
            k.RunMorsels parts (fun j ->
                let mutable r = before[j]

                let upTo = bound (j + 1)


                for i in bound j .. upTo - 1 do
                    if rank[i] = 1 then
                        rank[i] <- r
                        r <- r + 1

                true)

            let maps: int[][] = Array.zeroCreate parts

            k.RunMorsels parts (fun q ->
                let f = firsts[q]
                maps[q] <- Array.init f.Length (fun x -> rank[f[x]])
                true)

            // The ranks are read; the array takes the codes, each partition writing its own rows.
            let codes = rank

            k.RunMorsels parts (fun q ->
                let map = maps[q]

                // A `cellEq` coder gives a null no code.
                for t in start[q] .. start[q + 1] - 1 do
                    let x = local[t]
                    codes[order[t]] <- if x < 0 then -1 else map[x]

                true)

            codes,
            before[parts],
            { Parts = parts
              CellEq = cellEq
              Coders = coders
              Maps = maps }

        /// `codesOf (coder cellEq) v phys true`: its codes, how many there are, and the coder that
        /// holds them where one does, across threads where `k` partitions `phys.Length` rows.
        ///
        /// The coder's direct table is chosen over the whole column, as the sequential coder chooses
        /// it, and the coder codes the first rows of the first of `parts` ranges on the caller's
        /// thread. Where `byHash` and more than half of those opened a code, and the column codes
        /// through dictionaries, the column is coded by partitions of its values (`codesByHash`, D66)
        /// and no coder holds the codes. Otherwise the coder codes the rest of the first range. Where
        /// that range opened more than a quarter of `n / parts` codes, merging the ranges would cost
        /// what coding them does, and the coder codes the rest itself. Otherwise each other range is
        /// coded on the pool by a coder of its own (D65). Then, range by range in row order, the coder
        /// codes each range's values at their first rows, opening those it has not met in the order
        /// the range met them, which is the order the sequential coder opens them in; each range's
        /// codes are rewritten through that map on the pool. Every value is coded by `codesInto`, so
        /// the codes are the relation's.
        let private across (k: KernelSet) (cellEq: bool) (byHash: bool) (v: Vec) (phys: int[]) : int[] * int * Coded =
            let n = phys.Length
            let c = coder cellEq
            let parts = k.Partitions n

            if parts <= 1 then
                let codes = codesOf c v phys true
                codes, c.Count, ByCoder c
            else
                let bound = Ranges.bound n parts
                let codes: int[] = Array.zeroCreate n
                tablesFor c v phys

                // At most 16 partitions, from four times the rows the kernel set partitions from: on a
                // 32-thread machine 32 partitions ran no faster at a million rows and seven times
                // slower at 100,000 rows, where a partition held 3,000 (D66).
                let hashParts = min 16 (k.Partitions(n / 4))
                let mayHash = byHash && hashParts > 1

                // The probe sizes its dictionary for the column, as the first range did (D65), unless
                // the column may yet be coded by partitions; then for itself, and the rest of the
                // range for the column.
                let probe = min (bound 1) (4 * sample)
                codesInto c v phys 0 probe (if mayHash then probe else n) true codes

                if mayHash && c.Count * 2 > probe && throughDictionaries c v then
                    let codes, count, partitions = codesByHash k hashParts cellEq v phys
                    codes, count, ByPartitions partitions
                else
                    codesInto c v phys probe (bound 1) (n - probe) true codes

                    if c.Count * parts * 4 > n then
                        codesInto c v phys (bound 1) n (n - bound 1) true codes
                    else
                        // Each other range by a coder of its own, with no direct table: its keys are few,
                        // so its dictionaries are small, and a table per range would cost the column's
                        // range in every one of them.
                        let counts: int[] = Array.zeroCreate parts

                        k.RunMorsels (parts - 1) (fun j ->
                            let lo = bound (j + 1)
                            let lc = coder cellEq
                            codesInto lc v phys lo (bound (j + 2)) (bound (j + 2) - lo) true codes
                            counts[j + 1] <- lc.Count
                            true)

                        let maps: int[][] = Array.zeroCreate parts

                        for j in 1 .. parts - 1 do
                            let lo = bound j
                            let first = firstRowsIn codes lo (bound (j + 1)) counts[j]
                            maps[j] <- codesOf c v (Array.init first.Length (fun q -> phys[first[q]])) true

                        k.RunMorsels (parts - 1) (fun j ->
                            let map = maps[j + 1]

                            // A `cellEq` coder gives a null no code: `-1` in a range is `-1` in the column.
                            let upTo = bound (j + 2)

                            for i in bound (j + 1) .. upTo - 1 do
                                let x = codes[i]

                                if x >= 0 then
                                    codes[i] <- map[x]

                            true)


                    codes, c.Count, ByCoder c

        /// A join's build side: `codesOf (coder cellEq) v phys true`, how many codes there are, and
        /// what holds them for the probe side (`lookUp`), across threads where `k` partitions the rows
        /// (`across`): by row ranges where the column repeats its values (D65), by partitions of the
        /// values where it has many (D69).
        let buildAcross (k: KernelSet) (cellEq: bool) (v: Vec) (phys: int[]) : int[] * int * Coded =
            across k cellEq true v phys

        /// The probe side's codes: `codesOf c v phys false` against the build side's codes, `-1` for a
        /// value it does not hold, across threads where `k` partitions the rows. A lookup opens no
        /// code, builds no table and sizes no dictionary, so the threads only read the coders. Held by
        /// one coder, ranges of rows are looked up in it (D65). Held by partitions, the rows are split
        /// by the same hash, each looked up in its partition's coder, and its code mapped to the
        /// column's (D69).
        let lookUp (k: KernelSet) (coded: Coded) (v: Vec) (phys: int[]) : int[] =
            let n = phys.Length

            match coded with
            | ByCoder c ->
                let parts = k.Partitions n

                if parts <= 1 then
                    codesOf c v phys false
                else
                    let codes: int[] = Array.zeroCreate n

                    k.RunMorsels parts (fun j ->
                        let lo = Ranges.bound n parts j
                        let hi = Ranges.bound n parts (j + 1)
                        codesInto c v phys lo hi 0 false codes
                        true)

                    codes
            | ByPartitions p ->
                let part, start, order, byPart = partitionByHash k p.Parts p.CellEq v phys
                // In place, as the build side's coding is.
                let local = byPart

                k.RunMorsels p.Parts (fun q ->
                    codesInto p.Coders[q] v byPart start[q] start[q + 1] 0 false local
                    true)

                let codes = part

                k.RunMorsels p.Parts (fun q ->
                    let map = p.Maps[q]

                    for t in start[q] .. start[q + 1] - 1 do
                        let x = local[t]
                        codes[order[t]] <- if x < 0 then -1 else map[x]

                    true)

                codes

        /// The TOKEN slot of every logical row under `keyVecs` (read at `phys`), numbered from 0 in
        /// first-appearance order, and each slot's first logical row. No key is one slot holding
        /// every row (none over no rows). Each key column is coded through `k` (`across`), by
        /// partitions of its values where it has many (D66).
        let slotsWith (k: KernelSet) (keyVecs: Vec[]) (phys: int[]) : int[] * int[] =
            let n = phys.Length

            match keyVecs with
            // One key: a fresh coder numbers its values in first-appearance order already, so the
            // codes ARE the slots.
            | [| v |] ->
                let codes, count, _ = across k false true v phys
                codes, firstRowsAcross k codes count
            | _ ->
                let coded = keyVecs |> Array.map (fun v -> across k false true v phys)
                let codes = coded |> Array.map (fun (codes, _, _) -> codes)
                let index = Index(coded |> Array.map (fun (_, count, _) -> count), n)
                let slotOf: int[] = Array.zeroCreate n
                let first = ResizeArray<int>()

                for i in 0 .. n - 1 do
                    let s = index.Open(codes, i)
                    Raw.set slotOf i s

                    if s = first.Count then
                        first.Add i

                slotOf, first.ToArray()

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
    /// same keys forms. A slot's key cells are its first row's: the second answer is each slot's
    /// first PHYSICAL row (Phase 344), from which `keyColumn` reads the keys without boxing them.
    let private keySlots (k: KernelSet) (keyVecs: Vec[]) (phys: int[]) : int[] * int[] =
        let slotOf, first = RowHash.slotsWith k keyVecs phys
        let groups = first.Length
        let parts = min 16 (k.Partitions(groups / 4))

        if parts <= 1 then
            slotOf, Array.init groups (fun s -> phys[first[s]])
        else
            // Many groups: by ranges of slots (D71).
            let firstPhys: int[] = Array.zeroCreate groups

            k.RunMorsels parts (fun j ->
                let upTo = Ranges.bound groups parts (j + 1)

                for s in Ranges.bound groups parts j .. upTo - 1 do
                    firstPhys[s] <- phys[first[s]]

                true)

            slotOf, firstPhys

    /// The key column of declared type `ty` read from the vector `v` at the physical rows `rows` —
    /// exactly the vector `Vec.pack ty` packs from the cells there (Phase 344). Phase 344 measured
    /// the boxed form as most of a grouping at one key per ten rows: 20 of 32 ms at a million rows,
    /// a `Cell[]` per group and a boxed cell per key. Where the carrier is the type's own, the
    /// values are read straight across, a missing one left at the carrier's default as `pack`
    /// leaves it; any other pairing is packed from its cells as before.
    let private keyColumn (k: KernelSet) (ty: ColumnType) (v: Vec) (rows: int[]) : Vec =
        let n = rows.Length
        // Many groups: by ranges of their rows (D71), each writing only its own.
        let parts = min 16 (k.Partitions(n / 4))
        let bound = Ranges.bound n parts

        let inline across (a: 'T[]) (m: Mask) : 'T[] * Mask =
            let vals: 'T[] = Array.zeroCreate n
            let mask: Mask = Array.zeroCreate n

            k.RunMorsels (max 1 parts) (fun j ->
                let upTo = bound (j + 1)

                for i in bound j .. upTo - 1 do
                    let p = Raw.get i rows

                    if Mask.at p m then
                        Raw.set vals i (Raw.at p a)
                        Mask.set mask i true

                true)

            vals, mask

        match v, ty with
        | Ints(a, m), IntType -> Ints(across a m)
        | Floats(a, m), FloatType -> Floats(across a m)
        | Bools(a, m), BoolType -> Bools(across a m)
        | Strs(t, a, m), _ when t = ty ->
            let vals, mask = across a m
            Strs(t, vals, mask)
        | Dates(a, m), DateType -> Dates(across a m)
        | Stamps(u, s, f, m), TimestampType t when t = u ->
            let secs, mask = across s m
            let fraction = if isNull f then null else fst (across f m)
            Stamps(u, secs, fraction, mask)
        // A decimal key column at its scale, as every other verb gathers one (D62): boxing each key and
        // packing the cells again parsed every group's text a second time.
        | Decs _, DecimalType ->
            if parts > 1 then
                Vec.gatherRanges k.RunMorsels parts v rows
            else
                Vec.gather v rows
        | _ -> Vec.pack ty (rows |> Array.map (Vec.cellAt v))

    /// One column of a group's members — physical rows, in member order — as the cell list an
    /// aggregate reads: built from the back, so it is one pass and one cons per member, and read
    /// straight from the column's vector (Phase 267), so only the aggregated column is boxed.
    let private columnOf (v: Vec) (members: ResizeArray<int>) : Cell list =
        let mutable acc = []
        // A counted loop (Phase 353): a descending `for` compiles to a range enumerator under Fable,
        // and the pivot calls this once per deferred (group, on-value) pair.
        let mutable j = members.Count - 1

        while j >= 0 do
            acc <- Vec.cellAt v members[j] :: acc
            j <- j - 1

        acc

    let private evalGroupBy (k: KernelSet) (f: Frame) (keys: string list) (aggs: Agg list) : Result<Frame, EvalError> =
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
            //
            // Phase 344 — nor are the key cells boxed any more: each group's key is read from its
            // first physical row straight into the key column (`keyColumn`). A grouping BY KEY
            // across threads was built and measured here, and removed: see `Kernels.fs`.
            let keyVecs = idxs |> Array.map (fun ci -> f.Vecs[ci])
            let phys = Frame.physical f
            let slotOf, firstPhys = keySlots k keyVecs phys

            // resolve each agg's source column + type
            let resolveAgg (a: Agg) =
                match colType f.Cols a.Of with
                | Some ty -> Ok(a, ty, colIndex f.Cols a.Of |> Option.get)
                | None -> Error(UnknownColumn(a.Of, available f.Cols))

            let rec resAll acc =
                function
                | [] -> Ok(List.rev acc)
                | a :: rest -> resolveAgg a |> Result.bind (fun r -> resAll (r :: acc) rest)

            // Every key and every aggregate's source resolves first, then the output names are
            // checked (Phase 468, D35), before any row is read.
            resAll [] aggs
            |> Result.bind (fun resolvedAggs ->
                match groupByRefusal keys aggs with
                | Some e -> Error e
                | None -> Ok resolvedAggs)
            |> Result.bind (fun resolvedAggs ->
                let keyCols =
                    keys |> List.map (fun k -> f.Cols |> List.find (fun fd -> fd.Name = k))

                // An aggregate's column keeps its source's unit, a count none (Phase 429, D29).
                let aggCols =
                    resolvedAggs
                    |> List.map (fun (a, ty, _) ->
                        Field.create a.Name (aggType a.Fn ty)
                        |> stated (aggregateUnitOf a.Fn (colUnit f.Cols a.Of)))

                // Phase 323 — every aggregate streamed over the rows once, then one output column per
                // aggregate filled slot by slot. The first error — in group order, then aggregate
                // order, exactly as a traverse over groups of a traverse over aggregates reports it —
                // stops the loop; only a deferred group-aggregate can fail, and it fails exactly as
                // `Column.aggregate` over its members does.
                let aggArr = List.toArray resolvedAggs
                let groups = firstPhys.Length

                let streams =
                    aggArr
                    |> Array.map (fun (a, ty, ci) ->
                        let s = GroupAgg.Stream(a.Fn, ty, f.Vecs[ci], groups, GroupAgg.Exact)
                        GroupAgg.feedAcross k s groups slotOf phys
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

                // D68: where the groups are many, the streams that write only numbers emit every slot
                // across the pool first; the loop below then reads which of their slots deferred, and
                // emits every other stream and every deferral in group order, so the first error is
                // the one the loop always reported.
                let slices = k.Partitions groups
                let typedAt = streams |> Array.map (fun s -> slices > 1 && s.EmitsTyped)

                let emitted: bool[][] =
                    if Array.exists id typedAt then
                        let e = typedAt |> Array.map (fun t -> if t then Array.zeroCreate groups else null)

                        let at = Ranges.bound groups slices

                        k.RunMorsels slices (fun r ->
                            for j in 0 .. streams.Length - 1 do
                                if typedAt[j] then
                                    let ej = e[j]
                                    let sj = streams[j]
                                    let oj = outs[j]

                                    let upTo = at (r + 1)


                                    for g in at r .. upTo - 1 do
                                        ej[g] <- sj.Emit(g, oj)

                            true)

                        e
                    else
                        [||]

                let mutable failed = None
                let mutable g = 0

                while Option.isNone failed && g < groups do
                    let mutable j = 0

                    while Option.isNone failed && j < aggArr.Length do
                        let written =
                            if typedAt[j] then
                                emitted[j][g]
                            else
                                streams[j].Emit(g, outs[j])

                        if not written then
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
                        keyCols |> List.mapi (fun j f -> keyColumn k f.Type keyVecs[j] firstPhys)

                    let cols = keyCols @ aggCols

                    Ok
                        { Cols = cols
                          Vecs = Array.append (List.toArray keyOut) (outs |> Array.map (fun o -> o.ToVec()))
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

        let withNulls (mask: Mask) (cmp: int -> int -> int) : int -> int -> int =
            fun p q ->
                match Mask.at p mask, Mask.at q mask with
                | true, true -> signed (cmp p q)
                | true, false -> -1 // null sorts last
                | false, true -> 1
                | false, false -> 0

        match v with
        | Ints(a, m) -> withNulls m (fun p q -> compare (Raw.at p a) (Raw.at q a))
        | Floats(a, m) -> withNulls m (fun p q -> compareNum (Raw.at p a) (Raw.at q a))
        | Bools(a, m) -> withNulls m (fun p q -> compare (Mask.at p a) (Mask.at q a))
        | Strs(_, a, m) -> withNulls m (fun p q -> System.String.CompareOrdinal(Raw.at p a, Raw.at q a))
        // Every value of one decimal vector is an exact integer at the column's one scale (Phase 280).
        | Decs(a, _, _, m) -> withNulls m (fun p q -> compare (Raw.at p a) (Raw.at q a))
        // A calendar day's order is its canonical text's; an instant's is its seconds', then its
        // fraction's — the order the text had, read off the integers (Phase 423).
        | Dates(a, m) -> withNulls m (fun p q -> compare (Raw.at p a) (Raw.at q a))
        | Stamps(_, s, f, m) ->
            withNulls m (fun p q ->
                let bySeconds = compare (Raw.at p s) (Raw.at q s)

                if bySeconds <> 0 then
                    bySeconds
                else
                    compare (Vec.fractionAt f p) (Vec.fractionAt f q))
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
        let private ofFloats (vals: float[]) (mask: Mask) (phys: int[]) (dir: SortDir) : KeyCodes =
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

                if not (Mask.at p mask) then
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
        let private ofStrings (vals: string[]) (mask: Mask) (phys: int[]) (dir: SortDir) : KeyCodes =
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

                if not (Mask.at p mask) then
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
        let private ofInts (vals: int[]) (mask: Mask) (phys: int[]) (dir: SortDir) : KeyCodes =
            let n = phys.Length
            let mutable lo = System.Int32.MaxValue
            let mutable hi = System.Int32.MinValue
            let mutable any = false

            for i in 0 .. n - 1 do
                let p = Raw.get i phys

                if Mask.at p mask then
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
                            if not (Mask.at p mask) then nullCode
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
            // A calendar day codes as the int it is (Phase 423). An instant at whole seconds codes as
            // its seconds; at a sub-second unit the seconds and the fraction are joined into one
            // exact float where the unit allows it (milliseconds and microseconds stay inside 2^53
            // for every instant Core admits), and a nanosecond instant keeps the comparator.
            | Dates(a, m) -> ValueSome(ofInts a m phys dir)
            | Stamps(unit, s, f, m) ->
                if isNull f then
                    ValueSome(ofFloats s m phys dir)
                else
                    match unit with
                    | TimeUnit.Milliseconds
                    | TimeUnit.Microseconds ->
                        let scale = if unit = TimeUnit.Milliseconds then 1000.0 else 1000000.0

                        let joined: float[] = Array.zeroCreate s.Length

                        for i in 0 .. phys.Length - 1 do
                            let p = Raw.get i phys
                            Raw.put joined p (Raw.at p s * scale + float (Raw.at p f))

                        ValueSome(ofFloats joined m phys dir)
                    | TimeUnit.Seconds
                    | TimeUnit.Nanoseconds -> ValueNone
            | Bools(a, m) ->
                let asc = (dir = Asc)

                let codes =
                    phys
                    |> Array.map (fun p ->
                        if not (Mask.at p m) then 2
                        elif Mask.at p a = asc then 1
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

        /// The logical rows in the order's sequence, sorted through the kernel set `k` (Phase 344):
        /// the packed keys by its `SortFinite`, the unpacked rows by its `SortPositions` under
        /// `compareRows`. Either way the keys are distinct under a total order, so every correct
        /// sort answers alike, and the portable member's sorts are the engine's own.
        let permutationWith (k: KernelSet) (o: Order) : int[] =
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
                    k.SortFinite sorted

                let fn = float n
                // The position is the packed key's last digit, and `tie` is its own inverse.
                sorted |> Array.map (fun k -> tie o (int (k % fn)))
            else
                // The positions under the total order; the portable member sorts them as a plain
                // list, the slow path under Fable being a typed array's comparator sort.
                let order = Array.init n id
                k.SortPositions order (compareRows o)
                order

        /// The logical rows in the order's sequence, through the portable member's sorts — the
        /// engine's own.
        let permutation (o: Order) : int[] = permutationWith Kernels.portable o

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

    /// The keys a `Sort` takes from a pipeline, refused at the first one the frame does not carry
    /// (`unknownSortKey`, Phase 412).
    let private knownSortKeys (f: Frame) (by: (string * SortDir) list) : Result<(string * SortDir) list, EvalError> =
        match unknownSortKey f.Cols by with
        | Some e -> Error e
        | None -> Ok by

    let private evalSort (k: KernelSet) (f: Frame) (by: (string * SortDir) list) : Frame =
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
                Ordering.permutationWith k (Ordering.build Ordering.Exact null 0 codes phys.Length)

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
        let _, first = RowHash.slotsWith Kernels.oneThread f.Vecs phys
        Frame.select f (Array.init first.Length (fun s -> phys[first[s]]))

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
    let private evalTopN (k: KernelSet) (f: Frame) (by: (string * SortDir) list) (n: int) (offset: int) : Frame =
        let phys = Frame.physical f
        let len = phys.Length
        let skipped = min (max 0 offset) len
        let taken = min (max 0 n) (len - skipped)
        let window = skipped + taken

        if window = 0 then
            Frame.select f [||]
        elif window >= len then
            evalLimit (evalSort k f by) n offset
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

            // The `window` least positions of the range `lo .. hi - 1` (all of them, if it is
            // shorter), as a max-heap: its root is the greatest of them, and a position that sorts
            // before the root replaces it.
            let leastOf (lo: int) (hi: int) : int[] =
                let size = min window (hi - lo)
                let heap: int[] = Array.zeroCreate size

                let siftDown (start: int) =
                    let mutable i = start
                    let mutable go = true

                    while go do
                        let l = 2 * i + 1
                        let r = l + 1
                        let mutable largest = i

                        if l < size && cmp heap[l] heap[largest] > 0 then
                            largest <- l

                        if r < size && cmp heap[r] heap[largest] > 0 then
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

                for i in 0 .. size - 1 do
                    heap[i] <- lo + i
                    siftUp i

                for i in lo + size .. hi - 1 do
                    if cmp i heap[0] < 0 then
                        heap[0] <- i
                        siftDown 0

                heap

            // Phase 344 — on a kernel set that answers more than one partition, each partition is a
            // range of the positions with a heap of its own, on its own thread. Every one of the
            // `window` least positions is among the least of its own range, so the ranges' heaps
            // together hold them, and the one sort below under the total order picks them out:
            // the answer is the sequential heap's whatever the ranges.
            let parts = k.Partitions len

            let candidates =
                if parts <= 1 then
                    leastOf 0 len
                else
                    let bounds =
                        Array.init (parts + 1) (fun j -> int (int64 len * int64 j / int64 parts))

                    let heaps: int[][] = Array.zeroCreate parts

                    k.RunMorsels parts (fun j ->
                        heaps[j] <- leastOf bounds[j] bounds[j + 1]
                        true)

                    Array.concat heaps

            // A plain list for the final sort, as `evalSort` uses: a typed array's comparator sort
            // is the slow path under Fable.
            let order = ResizeArray<int>(candidates.Length)

            for i in 0 .. candidates.Length - 1 do
                order.Add candidates[i]

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
        | None ->
            // A key pair is an equality, so the comparison's rule holds it (Phase 429, D29): two
            // stated units must be one unit; an unconstrained side matches anything.
            let mismatch =
                on
                |> List.tryPick (fun (l, r) ->
                    match colUnit leftCols l, colUnit rightCols r with
                    | Some lu, Some ru when not (sameUnit lu ru) ->
                        Some(
                            UnitMismatch(
                                "join key '" + l + "' = '" + r + "'",
                                Binary(Eq, Col l, Col r),
                                Unit.render lu,
                                Unit.render ru
                            )
                        )
                    | _ -> None)

            match mismatch with
            | Some e -> Error e
            | None -> Ok(leftIdx |> List.map (fst >> Option.get), rightIdx |> List.map (fst >> Option.get))

    /// The join's key predicate over two rows' already-projected key cells: `cellEq` pairwise, so a
    /// `Null` key matches nothing, itself included. Exposed downstream as `joinKeysMatch`.
    let private joinKeyEq (leftKeys: Cell list) (rightKeys: Cell list) : bool =
        List.length leftKeys = List.length rightKeys
        && List.forall2 cellEq leftKeys rightKeys

    /// The output names of a combining join's right columns (Phase 411, `DECISIONS.md` D20). A right
    /// name no left column carries keeps its name. A right name that collides with a left one takes
    /// `_right`, or — when that is taken too — `_right_2`, `_right_3`, …, the first suffix that is
    /// free against EVERY name the output carries: the left's, the right's own, and the suffixes
    /// earlier colliding right columns took, decided in right column order. So a join adds no
    /// repeated name, and the static schema walk reads its names from this same function.
    let private joinRightNamesOf (leftNames: string list) (rightNames: string list) : string list =
        let leftSet = Set.ofList leftNames

        rightNames
        |> List.mapFold
            (fun taken name ->
                if Set.contains name leftSet then
                    let rec free (n: int) =
                        let candidate =
                            if n = 1 then
                                name + "_right"
                            else
                                name + "_right_" + string n

                        if Set.contains candidate taken then
                            free (n + 1)
                        else
                            candidate

                    let chosen = free 1
                    chosen, Set.add chosen taken
                else
                    name, taken)
            (Set.union leftSet (Set.ofList rightNames))
        |> fst

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
        (k: KernelSet)
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
                        if not (Mask.at phys[i] m) then
                            codes[i] <- -1
                | _ -> ()

                codes

            // The right side BUILDS (its rows opened in right order), the left side PROBES: a right
            // row whose key holds a `Null` is never indexed, a left row with one finds nothing.
            let built =
                Array.init rkeys.Length (fun j -> RowHash.buildAcross k (not (intPair j)) rkeys[j] rphys)

            let counts = built |> Array.map (fun (_, count, _) -> count)

            let rcodes =
                Array.init rkeys.Length (fun j ->
                    let codes, _, _ = built[j]
                    strikeNulls j rkeys[j] rphys codes)

            let lcodes =
                Array.init lkeys.Length (fun j ->
                    let _, _, coded = built[j]
                    strikeNulls j lkeys[j] lphys (RowHash.lookUp k coded lkeys[j] lphys))

            // One key column's codes already are its slots: a left row has a code only where a right
            // row opened it (a struck null is `-1` on both sides), so no second table is needed. Only a
            // tuple of columns needs numbering.
            let struct (rslots, lslots, slotCount) =
                if lkeys.Length = 1 then
                    struct (rcodes[0], lcodes[0], counts[0])
                else
                    let index = RowHash.Index(counts, rphys.Length)
                    let rs = Array.init rphys.Length (fun i -> index.Open(rcodes, i))
                    let ls = Array.init lphys.Length (fun i -> index.Find(lcodes, i))
                    struct (rs, ls, index.Count)

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
                // output schema: left cols ++ right cols, a colliding right name suffixed so that no
                // output name repeats (`joinRightNamesOf`)
                let outRight =
                    joinRightNamesOf (available f.Cols) (available rightCols)
                    |> List.map2 (fun col name -> Fields.rename name col) rightCols

                let padsLeft = (how = Left || how = Outer)
                let padsRight = (how = Right || how = Outer)

                // Each slot's right rows, in right order: a count per slot, its start, then a fill.
                // D72 measured this listing, and the right-only rows below, across ranges of right rows
                // and did not keep either: a million-row join read level.
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

                // A right row is matched when some left row's probe found its slot — the key relation
                // is symmetric, so that is exactly the reverse scan's "some left row matches it".
                //
                // D70: the left rows are taken in ranges, each counting the rows it emits (and the
                // padded ones among them) and marking its slots probed, on the pool where `k`
                // partitions them; the counts' running sum is where each range writes, so the rows
                // land in the order one pass writes them. Two ranges marking one slot write the same
                // `true`.
                let probed = Array.create slotCount false
                let ln = lphys.Length
                // At most 16 ranges, from four times the rows the kernel set partitions from, as D66
                // found for the coding: at 100,000 rows 32 ranges of a cheap join's pairs and columns
                // cost an int join 15 per cent in thread-pool overhead (D70).
                let parts = min 16 (k.Partitions(ln / 4))
                let lbound = Ranges.bound ln parts
                let emits: int[] = Array.zeroCreate (parts + 1)
                let pads: int[] = Array.zeroCreate parts

                k.RunMorsels parts (fun j ->
                    let mutable e = 0
                    let mutable padded = 0

                    let upTo = lbound (j + 1)


                    for i in lbound j .. upTo - 1 do
                        let s = lslots[i]

                        if s >= 0 then
                            probed[s] <- true
                            e <- e + starts[s + 1] - starts[s]
                        elif padsLeft then
                            e <- e + 1
                            padded <- padded + 1

                    emits[j + 1] <- e
                    pads[j] <- padded
                    true)

                for j in 1..parts do
                    emits[j] <- emits[j] + emits[j - 1]

                let leftRows = emits[parts]
                let leftOnly = Array.sum pads

                let unmatched (j: int) =
                    let s = rslots[j]
                    s < 0 || not probed[s]

                let mutable rightOnly = 0

                if padsRight then
                    for j in 0 .. rphys.Length - 1 do
                        if unmatched j then
                            rightOnly <- rightOnly + 1

                // The output's size is known now, so the index vectors are written once.
                let count = leftRows + rightOnly
                let lidx: int[] = Array.zeroCreate count
                let ridx: int[] = Array.zeroCreate count

                k.RunMorsels parts (fun j ->
                    let mutable at = emits[j]

                    let upTo = lbound (j + 1)


                    for i in lbound j .. upTo - 1 do
                        let s = lslots[i]

                        if s >= 0 then
                            for m in starts[s] .. starts[s + 1] - 1 do
                                lidx[at] <- lphys[i]
                                ridx[at] <- rphys[members[m]]
                                at <- at + 1
                        elif padsLeft then
                            lidx[at] <- lphys[i]
                            ridx[at] <- -1
                            at <- at + 1

                    true)

                let mutable at = leftRows

                // right-only unmatched rows (for Right / Outer), after every left-side row, in right
                // order.
                if padsRight then
                    for j in 0 .. rphys.Length - 1 do
                        if unmatched j then
                            lidx[at] <- -1
                            ridx[at] <- rphys[j]
                            at <- at + 1

                // A side with no padded row needs no null handling. Where `k` partitions the output,
                // each column is written by ranges of its rows (D70).
                let outParts = min 16 (k.Partitions(count / 4))

                let gatherSide (padded: int) (v: Vec) (idx: int[]) =
                    if outParts > 1 then
                        Vec.gatherRanges k.RunMorsels outParts v idx
                    elif padded = 0 then
                        Vec.gather v idx
                    else
                        Vec.gatherOrNull v idx

                let vecs =
                    Array.append
                        (f.Vecs |> Array.map (fun v -> gatherSide rightOnly v lidx))
                        (right.Vecs |> Array.map (fun v -> gatherSide leftOnly v ridx))

                Ok
                    { Cols = f.Cols @ outRight
                      Vecs = vecs
                      Sel = None
                      Count = count }

    /// Two columns met as one (a union's pair, a set op's pair; Phase 429, D29): the left field under
    /// the one unit the pair states, or the mismatch naming `what` and the column.
    let private unitsAgree (what: string) (left: Field) (right: Field) : Result<Field, EvalError> =
        match left.Unit, right.Unit with
        | Some l, Some r when not (sameUnit l r) ->
            Error(UnitMismatch(what + " '" + left.Name + "'", Col left.Name, Unit.render l, Unit.render r))
        | None, Some r -> Ok(Field.withUnit r left)
        | _ -> Ok left

    /// The first set-op column pair two units meet in, if any (`evalSetOp`).
    let private setOpUnitFault (verb: string) (left: Schema) (right: Schema) : EvalError option =
        List.zip left right
        |> List.tryPick (fun (l, r) ->
            match unitsAgree (verb + " column") l r with
            | Error e -> Some e
            | Ok _ -> None)

    /// One column of a union, from the left side's field and the right side's: the left field under
    /// the one unit the pair states and the JOIN of the two types, or the refusal. Phase 423: an int
    /// column over a float one is a float column, and a pair no column type holds together is
    /// refused by name — Core `1.0.0`'s typed column cannot hold a bool beside a float (D18). Phase
    /// 429 (D29): the unit is the left's, the right's where only the right states one, and two
    /// stated units are refused. The evaluator (`evalUnion`) and the static walk's `Union` arm both
    /// read this one function (Phase 469, D36), so the walk cannot type a union the evaluator does
    /// not produce.
    let internal unionField (left: Field) (right: Field) : Result<Field, EvalError> =
        unitsAgree "union column" left right
        |> Result.bind (fun left ->
            if left.Type = right.Type then
                Ok left
            else
                match joinColumnType left.Type right.Type with
                | Some ty -> Ok(Field.withType ty left)
                | None -> Error(typesBesideEachOther left.Name left.Type right.Type))

    let private evalUnion (f: Frame) (other: Frame) : Result<Frame, EvalError> =
        if available f.Cols <> available other.Cols then
            Error(JoinError "union requires matching column names")
        else
            // The appended cells are packed under the joined type; the common case, both sides of
            // one type, is the plain append.
            let joined =
                List.zip f.Cols other.Cols |> traverseResult (fun (l, r) -> unionField l r)

            joined
            |> Result.map (fun cols ->
                let g = Frame.concat f other

                let vecs =
                    g.Vecs
                    |> Array.mapi (fun ci v ->
                        let ty = (List.item ci cols).Type

                        if Vec.declaredType v = Some ty then
                            v
                        else
                            Vec.pack ty (Array.init g.Count (Vec.cellAt v)))

                { g with Cols = cols; Vecs = vecs })

    /// `Intersect` / `Except` (Phase 101) — the multiset set-ops, keyed on the SAME canonical row
    /// token `Distinct` dedups on (Phase 41), so membership is host-identical and `Null` is a value
    /// that matches itself. `keepPresent` selects intersect (`true`) from except (`false`). The
    /// left's order and duplicate multiplicity survive, so `· Distinct` recovers the SQL set forms.
    let private evalSetOp (verb: string) (keepPresent: bool) (f: Frame) (other: Frame) : Result<Frame, EvalError> =
        if available f.Cols <> available other.Cols then
            Error(JoinError(verb + " requires matching column names"))
        elif Option.isSome (setOpUnitFault verb f.Cols other.Cols) then
            // Row identity across two units is the comparison's error (Phase 429, D29): checked,
            // and the left schema is what the answer keeps.
            Error(Option.get (setOpUnitFault verb f.Cols other.Cols))
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
    /// schema index, with the partition and order columns already resolved, its permutation sorted
    /// through the kernel set `k` (Phase 344). `perturbation` is the laws' (Phase 324); the
    /// evaluator passes `Ordering.Exact`.
    let internal windowOrderWith
        (k: KernelSet)
        (perturbation: Ordering.Perturbation)
        (vecOf: int -> Vec)
        (phys: int[])
        (partIdx: int[])
        (orderKeys: (int * SortDir) list)
        : WindowOrder =
        let n = phys.Length
        let slotOf, slotKeys = keySlots k (partIdx |> Array.map vecOf) phys
        let partitions = slotKeys.Length
        let keyVecs = orderKeys |> List.map (fun (ci, dir) -> vecOf ci, dir) |> List.toArray

        match Ordering.codesAll keyVecs phys with
        | ValueSome codes ->
            let order = Ordering.build perturbation slotOf partitions codes n

            { Slot = slotOf
              Partitions = partitions
              Perm = Ordering.permutationWith k order
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

    /// `windowOrderWith` through the portable member's sorts — the engine's own.
    let internal windowOrder
        (perturbation: Ordering.Perturbation)
        (vecOf: int -> Vec)
        (phys: int[])
        (partIdx: int[])
        (orderKeys: (int * SortDir) list)
        : WindowOrder =
        windowOrderWith Kernels.portable perturbation vecOf phys partIdx orderKeys

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
            let mask: Mask = Array.zeroCreate count
            // The loop proves `i` for `a`, and for `phys` once it is checked to be as long (Phase 326);
            // the physical row it writes is an index it does not prove.
            Raw.within a.Length phys

            for i in 0 .. a.Length - 1 do
                let p = Raw.get i phys
                Raw.put vals p (Raw.get i a)
                Mask.put mask p true

            Ints(vals, mask)
        | WFloats(a, m) ->
            let vals: float[] = Array.zeroCreate count
            let mask: Mask = Array.zeroCreate count
            Raw.within a.Length phys
            Raw.within a.Length m

            for i in 0 .. a.Length - 1 do
                if Raw.get i m then
                    let p = Raw.get i phys
                    Raw.put vals p (Raw.get i a)
                    Mask.put mask p true

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

    /// The one check a `Window` step's spec passes before any row is read (Phase 441, `DECISIONS.md`
    /// D26), in the order the step refuses: a bucket count below one, then a column the function
    /// reads that the schema lacks, then the first key the spec takes from the pipeline that the
    /// schema does not carry — its partition keys, then its order keys, through `unknownKey`, the
    /// resolution a `Sort`'s keys pass, so the partition and the order keys cannot drift apart
    /// again — and last an output name the schema already carries (Phase 450, D28): a `Window`
    /// appends its column, so an `As` naming an existing column would answer two columns of one
    /// name, which no table can hold and which re-enters by name as the first of the two. The
    /// frame form, the row form (`windowStep`) and the incremental seam all ask it; `None` is a
    /// spec the step evaluates.
    let internal windowRefusal (cols: Schema) (spec: WindowSpec) : EvalError option =
        match spec.Fn with
        | NTile b when b < 1 -> Some(TypeError("ntile expects at least 1 bucket, got " + string b))
        | fn when windowReadsOf fn && Option.isNone (colIndex cols spec.Of) ->
            Some(UnknownColumn(spec.Of, available cols))
        | _ ->
            match unknownKey cols (spec.PartitionBy @ List.map fst spec.OrderBy) with
            | Some e -> Some e
            | None when Option.isSome (colIndex cols spec.As) ->
                Some(
                    TypeError(
                        "window output '"
                        + spec.As
                        + "' names a column the input already carries; name the window's column something else, or project the existing one away first"
                    )
                )
            | None -> None

    /// The `Window` step's appended column (Phase 324): its type, and one value per LOGICAL row of
    /// the rows `phys` reads, over the vectors `vecOf` names by schema index. The frame form packs it
    /// back at the physical rows; the public row form (`windowStep`) and the incremental seam's
    /// window step pack the columns they read and place the cell on each row. Each partition is one
    /// run of the window ordering's permutation, scanned in sequence.
    let rec internal windowColumnOfWith
        (k: KernelSet)
        (perturbation: Ordering.Perturbation)
        (cols: Schema)
        (vecOf: int -> Vec)
        (phys: int[])
        (spec: WindowSpec)
        : Result<ColumnType * WindowColumn, EvalError> =
        // Phase 412 / 441 — the spec is refused before the ordering is computed
        // (`windowColumnOverWith` refuses it too, for a caller holding an ordering already).
        match windowRefusal cols spec with
        | Some e -> Error e
        | None ->
            windowColumnOverWith k cols vecOf phys spec (windowOrderOfWith k perturbation cols vecOf phys spec) null

    /// The schema indexes of a window's PARTITION columns. Every reader reaches it behind
    /// `windowRefusal`, which refuses a name the schema does not carry, so every name resolves.
    and internal windowPartitionIdx (cols: Schema) (spec: WindowSpec) : int[] =
        spec.PartitionBy |> List.choose (colIndex cols) |> List.toArray

    /// The schema index of the column a window reads, where the schema carries it.
    and internal windowOfIdx (cols: Schema) (spec: WindowSpec) : int option = colIndex cols spec.Of

    /// The window ordering of `spec` over the rows `phys` reads (`windowOrder`), with its partition
    /// and order columns resolved under `cols` — the ordering `windowColumnOf` scans.
    and internal windowOrderOfWith
        (k: KernelSet)
        (perturbation: Ordering.Perturbation)
        (cols: Schema)
        (vecOf: int -> Vec)
        (phys: int[])
        (spec: WindowSpec)
        : WindowOrder =
        // The ORDER keys resolved once for the step (Phase 263), not once per comparison.
        windowOrderWith k perturbation vecOf phys (windowPartitionIdx cols spec) (resolveSortKeys cols spec.OrderBy)

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
    and internal windowColumnOverWith
        (k: KernelSet)
        (cols: Schema)
        (vecOf: int -> Vec)
        (phys: int[])
        (spec: WindowSpec)
        (wo: WindowOrder)
        (seeds: Cell[])
        : Result<ColumnType * WindowColumn, EvalError> =
        match windowRefusal cols spec, colIndex cols spec.Of with
        | Some e, _ -> Error e
        | None, ofIdx ->
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

                        if Mask.at p m then
                            ValueSome(float (Raw.at p a))
                        else
                            ValueNone
                | Some(Floats(a, m)) ->
                    fun i ->
                        let p = Raw.at i phys
                        if Mask.at p m then ValueSome(Raw.at p a) else ValueNone
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

                            if Mask.at p m then
                                acc <- acc + float (Raw.at p a)

                            Raw.put floats i acc
                            Raw.put present i true
                    | Some(Floats(a, m)) ->
                        for k in 0 .. len - 1 do
                            let i = Raw.at (s + k) perm
                            let p = Raw.at i phys

                            if Mask.at p m then
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

            // The partitions are the permutation's runs of one slot; each output lands at its row's
            // logical position, so the visiting order reaches nothing. Phase 344 — on a kernel set
            // that answers more than one partition, the runs are cut into that many chunks of whole
            // runs, about as many rows each, scanned on the kernel set's runner: every partition's
            // fold is still one sequential scan, and no two chunks write a row in common.
            let parts = k.Partitions n

            if parts <= 1 then
                let mutable s = 0

                while s < n do
                    let g = wo.Slot[perm[s]]
                    let mutable e = s + 1

                    while e < n && wo.Slot[perm[e]] = g do
                        e <- e + 1

                    partition g s e
                    s <- e
            else
                let starts = ResizeArray<int>()
                let chunkAt = ResizeArray<int>()
                let mutable s = 0

                while s < n do
                    // A chunk opens at the first run starting past its share of the rows.
                    if int64 s >= int64 chunkAt.Count * int64 n / int64 parts then
                        chunkAt.Add starts.Count

                    starts.Add s
                    let g = wo.Slot[perm[s]]
                    let mutable e = s + 1

                    while e < n && wo.Slot[perm[e]] = g do
                        e <- e + 1

                    s <- e

                starts.Add n
                chunkAt.Add(starts.Count - 1)

                k.RunMorsels (chunkAt.Count - 1) (fun c ->
                    for r in chunkAt[c] .. chunkAt[c + 1] - 1 do
                        partition wo.Slot[perm[starts[r]]] starts[r] starts[r + 1]

                    true)

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

            // Phase 404 — the window column leaves with the canonical NaN, whichever function wrote
            // it: this is the one place every window answer passes, the frame form, the row form and
            // the incremental seam's resumed runs alike (a seed is read back from a canonical output).
            if floatOut then
                for i in 0 .. n - 1 do
                    floats[i] <- canonicalNaN floats[i]
            elif not intOut then
                for i in 0 .. n - 1 do
                    out[i] <- canonicalNaNCell out[i]

            let column =
                if intOut then WInts ints
                elif floatOut then WFloats(floats, present)
                else WCells out

            Ok(ty, column)

    /// `windowColumnOverWith` through the portable member — the scan in sequence, as the
    /// incremental seam's suffix recompute runs it.
    let internal windowColumnOver
        (cols: Schema)
        (vecOf: int -> Vec)
        (phys: int[])
        (spec: WindowSpec)
        (wo: WindowOrder)
        (seeds: Cell[])
        : Result<ColumnType * WindowColumn, EvalError> =
        windowColumnOverWith Kernels.portable cols vecOf phys spec wo seeds

    /// `windowColumnOfWith` through the portable member's sorts — the engine's own.
    let internal windowColumnOf
        (perturbation: Ordering.Perturbation)
        (cols: Schema)
        (vecOf: int -> Vec)
        (phys: int[])
        (spec: WindowSpec)
        : Result<ColumnType * WindowColumn, EvalError> =
        windowColumnOfWith Kernels.portable perturbation cols vecOf phys spec

    /// `windowOrderOfWith` through the portable member's sorts — the engine's own.
    let internal windowOrderOf
        (perturbation: Ordering.Perturbation)
        (cols: Schema)
        (vecOf: int -> Vec)
        (phys: int[])
        (spec: WindowSpec)
        : WindowOrder =
        windowOrderOfWith Kernels.portable perturbation cols vecOf phys spec

    /// The window column over full-width rows under `cols` (the public row form, `windowStep`): each
    /// column the step reads packed once under its declared type — a column holding a
    /// cell outside its type packs boxed, and is ordered by the pinned comparator as before — and
    /// the frame path run over the rows in their own order.
    let private windowColumnOfRows (cols: Schema) (rows: Cell[][]) (spec: WindowSpec) =
        let types = Fields.types cols |> List.toArray
        let packed = System.Collections.Generic.Dictionary<int, Vec>()

        let vecOf (ci: int) : Vec =
            match packed.TryGetValue ci with
            | true, v -> v
            | _ ->
                let v = Vec.pack types[ci] (rows |> Array.map (fun r -> r[ci]))
                packed[ci] <- v
                v

        windowColumnOf Ordering.Exact cols vecOf (Array.init rows.Length id) spec

    let private evalWindow (k: KernelSet) (f: Frame) (spec: WindowSpec) : Result<Frame, EvalError> =
        // The window read from the vectors through the selection (Phase 324); the one new column
        // placed back at its physical positions and APPENDED (a `Window` always appends, where a
        // `Derive` upserts); every other vector shared.
        let phys = Frame.physical f

        windowColumnOfWith k Ordering.Exact f.Cols (fun ci -> f.Vecs[ci]) phys spec
        |> Result.map (fun (ty, column) ->
            Frame.appendColumn f spec.As ty (windowVecAt ty f.Count phys column)
            |> unitised spec.As (windowUnitOf spec.Fn (colUnit f.Cols spec.Of)))

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
                    let valType = (List.item valIdx f.Cols).Type

                    let phys = Frame.physical f
                    let onVec = f.Vecs[onIdx]
                    let valVec = f.Vecs[valIdx]

                    // The on-values' `cellEq` coder (Phase 325), read here and by the one pass below.
                    let onCoder = RowHash.coder true

                    // distinct on-values (sorted by canonical string for a deterministic column order).
                    // Phase 468 (D35) — two on-values `List.distinct` keeps apart (two `NaN`s) that render
                    // ONE text and that `cellEq` equates would be two identical columns of one name, each
                    // collecting every row either matches: they are one column. Two of one text that
                    // `cellEq` keeps apart are refused below with the other repeated names.
                    let onValues =
                        let seen = System.Collections.Generic.HashSet<string>()

                        phys
                        |> Array.map (fun p -> Vec.cellAt onVec p)
                        |> Array.filter (fun c -> not (Cell.isNull c))
                        |> List.ofArray
                        |> List.distinct
                        |> List.sortBy cellString
                        |> List.filter (fun ov ->
                            seen.Add(string (RowHash.cellCode onCoder ov true) + ":" + cellString ov))

                    // The output's names are the index names then the on values' texts, so they are
                    // checked here, before any value is aggregated (Phase 468, D35).
                    match pivotRefusal spec.Index (onValues |> List.map cellString) with
                    | Some e -> Error e
                    | None ->

                        // Phase 264 — ONE pass groups the value cells by (index group, pivot column). The
                        // per-pair filter it replaces scanned the whole frame for every (group, on-value)
                        // pair, re-minting the index token per row inside the filter: O(g × v × n).
                        //
                        // An on-value's column set, keyed by its `cellEq` code (Phase 325: the typed row
                        // hasher's `cellEq` coder, where it was a token string per row): a row lands in
                        // EVERY column whose on-value `cellEq` matches its own, which is what the filter
                        // selected. Usually that is one column; two on-values `List.distinct` keeps apart
                        // but `cellEq` equates under two texts (`Int 1` and `Float 1.0` in a mixed column)
                        // share a code, are two columns, and each collects every row either matches.
                        let onCount = List.length onValues
                        let onCodes = onValues |> List.map (fun ov -> RowHash.cellCode onCoder ov true)
                        let columnsOf = Array.init onCoder.Count (fun _ -> ResizeArray<int>())

                        onCodes
                        |> List.iteri (fun c code ->
                            if code >= 0 then
                                columnsOf[code].Add c)

                        let rowOn = RowHash.codesOf onCoder onVec phys false

                        // Index groups in first-appearance order by TOKEN equality over the index cells
                        // (Phase 41's canonical token — NOT the `cellEq` relation the on-values match by
                        // above), through the typed row hasher (Phase 325). Each group's key is read from
                        // its first physical row straight into the key column (`keyColumn`, Phase 344).
                        let idxVecs = idxIdx |> List.map (fun i -> f.Vecs[i]) |> List.toArray
                        let slotOf, firstPhys = keySlots Kernels.oneThread idxVecs phys
                        let groups = firstPhys.Length

                        // Phase 353 — every (group, on-value) pair aggregated by Phase 323's streams: one
                        // stream per pivot column, a slot per group, fed each row in frame order, which is
                        // the order the per-pair cell list held. It replaced a boxed cell list per pair
                        // and a `Column.aggregate` call per pair (5,000 of them over one-cell lists for
                        // the corpus's 50-on-value pivot, and the most of its node profile). A pair a
                        // stream defers — `Median`, `StdDev`, `CountDistinct`, a cell outside the column's
                        // type, an int `Sum` past int32, a float overflow — is `Column.aggregate` over its
                        // members, as before; the differential law (`pivotStreamLaws`) holds the two equal.
                        let streams =
                            Array.init onCount (fun _ ->
                                GroupAgg.Stream(spec.Agg, valType, valVec, groups, GroupAgg.Exact))

                        // The loop proves `i` for `phys`, and for `rowOn` and `slotOf` once each is checked to
                        // hold a row per logical row; the slot and the code it reads are indexes it does not
                        // prove (Phase 326).
                        Raw.within phys.Length rowOn
                        Raw.within phys.Length slotOf

                        // An aggregate every stream defers (`Median`, `StdDev`, `CountDistinct`) is fed nothing.
                        let fed =
                            if onCount > 0 && not streams[0].DefersAll then
                                phys.Length
                            else
                                0

                        for i in 0 .. fed - 1 do
                            let code = Raw.get i rowOn

                            if code >= 0 then
                                let g = Raw.get i slotOf
                                let p = Raw.get i phys
#if FABLE_COMPILER
                                // A counted loop: `for` over a list compiles to an enumerator there.
                                let targets = Raw.at code columnsOf

                                for j in 0 .. targets.Count - 1 do
                                    streams[targets[j]].Feed(g, p)
#else
                                for c in columnsOf[code] do
                                    streams[c].Feed(g, p)
#endif

                        // A deferred pair's members — physical rows in frame order — built once, for
                        // every pair, on the first deferral: the lists the step kept before this phase.
                        let mutable members: ResizeArray<int>[][] = null

                        let membersOf (g: int) (c: int) : ResizeArray<int> =
                            if isNull members then
                                members <-
                                    Array.init onCount (fun _ -> Array.init groups (fun _ -> ResizeArray<int>()))

                                for i in 0 .. phys.Length - 1 do
                                    let code = rowOn[i]

                                    if code >= 0 then
                                        let targets = columnsOf[code]

                                        for j in 0 .. targets.Count - 1 do
                                            let pair = members[targets[j]]
                                            pair[slotOf[i]].Add phys[i]

                            members[c][g]

                        let outTy = aggType spec.Agg valType
                        let outs = Array.init onCount (fun _ -> GroupAgg.Output(outTy, groups))

                        // An absent pair aggregates the EMPTY list, exactly as a filter that matched no
                        // row did — `Null` or `Int 0` by the aggregate's own rule; a stream that saw no
                        // row answers just that. The first error, in group order then column order, is
                        // the traverse's.
                        let mutable failed = None
                        let mutable g = 0

                        while Option.isNone failed && g < groups do
                            let mutable c = 0

                            while Option.isNone failed && c < onCount do
                                if not (streams[c].Emit(g, outs[c])) then
                                    match aggCells spec.Agg valType (columnOf valVec (membersOf g c)) with
                                    | Ok cell -> outs[c].Cell(g, cell)
                                    | Error e -> failed <- Some e

                                c <- c + 1

                            g <- g + 1

                        match failed with
                        | Some e -> Error e
                        | None ->
                            let idxCols =
                                spec.Index |> List.map (fun n -> f.Cols |> List.find (fun fd -> fd.Name = n))

                            // Every value column is one aggregate of `Values`, so each carries that
                            // aggregate's unit (Phase 429, D29).
                            let valueUnit = aggregateUnitOf spec.Agg (colUnit f.Cols spec.Values)

                            let pivotCols =
                                onValues
                                |> List.map (fun ov -> Field.create (cellString ov) outTy |> stated valueUnit)

                            let cols = idxCols @ pivotCols

                            let keyOut =
                                idxCols
                                |> List.mapi (fun j f -> keyColumn Kernels.oneThread f.Type idxVecs[j] firstPhys)

                            Ok
                                { Cols = cols
                                  Vecs = Array.append (List.toArray keyOut) (outs |> Array.map (fun o -> o.ToVec()))
                                  Sel = None
                                  Count = groups })))

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
                // The output's names are checked once every name resolves, before the value
                // column is typed or any row is read (Phase 468, D35).
                match unpivotRefusal idVars with
                | Some e -> Error e
                | None ->

                    let idCols =
                        idVars |> List.map (fun n -> f.Cols |> List.find (fun fd -> fd.Name = n))

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
                            Frame.ofRows
                                (idCols
                                 @ [ Field.create "variable" StringType
                                     Field.create "value" valType |> stated (sharedUnit f.Cols valueVars) ])
                                rows)))

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

    // -----------------------------------------------------------------------------------------
    //  Phase 592 — `Fit` (plan §2.4, `DECISIONS.md` D53). The frame-to-matrix bridge, the units,
    //  and the tables a fit answers live here; the algebra — the streaming Givens QR, the rank
    //  rule and the solve — is `Fuaran.Compute.Regression`'s, called and never restated.
    // -----------------------------------------------------------------------------------------

    /// The wire spelling of a family — what the codec writes and `UnsupportedFit` names.
    let internal fitFamilyTag (f: FitFamily) : string =
        match f with
        | FitFamily.Gaussian -> "gaussian"
        | FitFamily.Poisson -> "poisson"
        | FitFamily.Gamma -> "gamma"
        | FitFamily.Tweedie _ -> "tweedie"
        | FitFamily.QuasiPoisson -> "quasiPoisson"
        | FitFamily.Binomial -> "binomial"
        | FitFamily.QuasiBinomial -> "quasiBinomial"
        | FitFamily.InverseGaussian -> "inverseGaussian"
        | FitFamily.NegativeBinomial _ -> "negativeBinomial"

    /// The wire spelling of a link.
    let internal fitLinkTag (l: FitLink) : string =
        match l with
        | FitLink.Identity -> "identity"
        | FitLink.Log -> "log"
        | FitLink.Logit -> "logit"
        | FitLink.Probit -> "probit"
        | FitLink.Cloglog -> "cloglog"
        | FitLink.Inverse -> "inverse"
        | FitLink.InverseSquare -> "inverseSquare"
        | FitLink.Sqrt -> "sqrt"

    /// The family as `Fuaran.Compute.Regression` names it.
    let internal glmFamily (f: FitFamily) : GlmFamily =
        match f with
        | FitFamily.Gaussian -> GlmFamily.Gaussian
        | FitFamily.Poisson -> GlmFamily.Poisson
        | FitFamily.Gamma -> GlmFamily.Gamma
        | FitFamily.Tweedie p -> GlmFamily.Tweedie p
        | FitFamily.QuasiPoisson -> GlmFamily.QuasiPoisson
        | FitFamily.Binomial -> GlmFamily.Binomial
        | FitFamily.QuasiBinomial -> GlmFamily.QuasiBinomial
        | FitFamily.InverseGaussian -> GlmFamily.InverseGaussian
        | FitFamily.NegativeBinomial t -> GlmFamily.NegativeBinomial t

    /// The link as `Fuaran.Compute.Regression` names it.
    let internal glmLink (l: FitLink) : GlmLink =
        match l with
        | FitLink.Identity -> GlmLink.Identity
        | FitLink.Log -> GlmLink.Log
        | FitLink.Logit -> GlmLink.Logit
        | FitLink.Probit -> GlmLink.Probit
        | FitLink.Cloglog -> GlmLink.Cloglog
        | FitLink.Inverse -> GlmLink.Inverse
        | FitLink.InverseSquare -> GlmLink.InverseSquare
        | FitLink.Sqrt -> GlmLink.Sqrt

    /// Whether `Fit` evaluates the pair (Phase 600): `Glm.admitted`'s table, one table for the codec
    /// and the evaluator.
    let internal fitAdmitted (family: FitFamily) (link: FitLink) : bool =
        Glm.admitted (glmFamily family) (glmLink link)

    /// Whether a spec is ordinary least squares — Gaussian under the identity link — which folds into
    /// the streaming accumulator and is maintained; every other admitted pair is fitted by IRLS.
    let internal fitIsOls (spec: FitSpec) : bool =
        spec.Family = FitFamily.Gaussian && spec.Link = FitLink.Identity

    /// Whether a `Summary` table states the negative binomial's θ (Phase 600, D58 item 8): its `theta`
    /// and `theta_std_error` columns in the response's unit and its `alternations`, after `converged`,
    /// under the negative binomial family only — a closed schema, decided by the spec before any row is read.
    let internal fitStatesTheta (spec: FitSpec) : bool =
        match spec.Family with
        | FitFamily.NegativeBinomial _ -> true
        | _ -> false

    /// Whether the family fixes its dispersion at one (Poisson, binomial, negative binomial), so an
    /// interval reads the normal row rather than the t at the residual degrees of freedom.
    let internal fitFixedDispersion (f: FitFamily) : bool =
        match f with
        | FitFamily.Poisson
        | FitFamily.Binomial
        | FitFamily.NegativeBinomial _ -> true
        | _ -> false

    /// The wire spelling of an output.
    let internal fitOutputTag (o: FitOutput) : string =
        match o with
        | FitOutput.Coefficients -> "coefficients"
        | FitOutput.Summary -> "summary"
        | FitOutput.Fitted -> "fitted"
        | FitOutput.Residuals -> "residuals"

    /// The unit of a squared response (a deviance's), where the response states one the algebra can
    /// square.
    let internal fitUnitSquared (u: UnitOfMeasure option) : UnitOfMeasure option =
        u |> Option.bind (fun u -> unitPower u 2)

    /// The units a fit states its statistics in, read off the family and the link (D53 item 10; Phase
    /// 595; Phase 600, D58 item 9). The linear predictor is in the response's unit under the identity
    /// link, so a coefficient is `unit(y) / unit(x)`; a pure number under the log, logit, probit and
    /// cloglog links, so a numeric term's coefficient is `1 / unit(x)` and the intercept's and a level's
    /// state none; `unit(y)⁻¹` under the inverse link and `unit(y)⁻²` under the inverse-square link;
    /// and under the square-root link, `unit(y)^½`, which the algebra cannot carry, so no coefficient
    /// states a unit when the response does (`EtaStated`). A deviance is in the unit of the family's unit
    /// deviance — Gaussian's `unit(y)²`, Poisson's, quasi-Poisson's and the negative binomial's
    /// `unit(y)`, inverse Gaussian's `unit(y)⁻¹`, Tweedie's `unit(y)^(2 − p)` where `2 − p` is an integer,
    /// none for Gamma and the binomials; `sigma` and the dispersion are Gaussian's `unit(y)` and
    /// `unit(y)²` and pure numbers otherwise; and only least squares states a decimal response's
    /// deviances in decimal — a logarithm or a reciprocal is never an exact decimal.
    type internal FitStatUnits =
        {
            /// The unit the linear predictor is in, which a coefficient is stated against.
            Eta: UnitOfMeasure option
            /// Whether `Eta` is the linear predictor's unit; `false` where the algebra cannot state it,
            /// and then no coefficient states a unit.
            EtaStated: bool
            Sigma: UnitOfMeasure option
            Deviance: UnitOfMeasure option
            Dispersion: UnitOfMeasure option
            /// The deviances of a decimal response are decimal.
            DecimalDeviance: bool
        }

    let internal fitStatUnits (spec: FitSpec) (yUnit: UnitOfMeasure option) : FitStatUnits =
        let power (n: int) =
            yUnit |> Option.bind (fun u -> unitPower u n)

        let stated (n: int) =
            match yUnit with
            | None -> true
            | Some u -> Option.isSome (unitPower u n)

        let eta, etaStated =
            match spec.Link with
            | FitLink.Identity -> yUnit, true
            | FitLink.Log
            | FitLink.Logit
            | FitLink.Probit
            | FitLink.Cloglog -> None, true
            | FitLink.Inverse -> power -1, stated -1
            | FitLink.InverseSquare -> power -2, stated -2
            | FitLink.Sqrt -> None, Option.isNone yUnit

        let gaussian = spec.Family = FitFamily.Gaussian

        { Eta = eta
          EtaStated = etaStated
          Sigma = (if gaussian then yUnit else None)
          Deviance =
            (match spec.Family with
             | FitFamily.Gaussian -> fitUnitSquared yUnit
             | FitFamily.Poisson
             | FitFamily.QuasiPoisson
             | FitFamily.NegativeBinomial _ -> yUnit
             // Phase 598: a Tweedie unit deviance is in `unit(y)^(2 − p)`.
             | FitFamily.Tweedie p when p = System.Math.Floor p && p <> 2.0 && abs p < 1e9 -> power (2 - int p)
             | FitFamily.InverseGaussian -> power -1
             | _ -> None)
          Dispersion = (if gaussian then fitUnitSquared yUnit else None)
          DecimalDeviance = fitIsOls spec }

    /// The units a `Predict` states its columns in (Phase 597, D56): the prediction and an interval's
    /// bounds on the scale it answers — the linear predictor's unit (`fitStatUnits`'s `Eta`) on the
    /// link scale, the response's on the response scale — and the standard errors, which are on the
    /// link scale whatever the prediction's.
    type internal PredictUnits =
        { Predicted: UnitOfMeasure option
          StdError: UnitOfMeasure option }

        /// The unit of one appended column: a standard error's, or the prediction's scale's.
        member u.Of(column: string) : UnitOfMeasure option =
            if column.StartsWith "se_" then u.StdError else u.Predicted

    let internal predictUnits (spec: PredictSpec) (yUnit: UnitOfMeasure option) : PredictUnits =
        let eta = (fitStatUnits spec.Fit yUnit).Eta

        match spec.Scale with
        | PredictScale.Link -> { Predicted = eta; StdError = eta }
        | PredictScale.Response -> { Predicted = yUnit; StdError = yUnit }

    /// The columns a `Predict` appends, in order (Phase 597, D56).
    let internal predictColumns (spec: PredictSpec) : string list =
        match spec.Model with
        | PredictModel.Coefficients _ -> [ "predicted" ]
        | PredictModel.Training _ ->
            [ "predicted"; "se_fit"; "se_pred" ]
            @ (if Option.isSome spec.Interval then
                   [ "lower"; "upper" ]
               else
                   [])

    /// The name of the coefficient of the design's constant column (R's spelling).
    let internal interceptTerm = "(Intercept)"

    /// The column a `Fitted` / `Residuals` table appends.
    let internal fitAppendedColumn (o: FitOutput) : string option =
        match o with
        | FitOutput.Fitted -> Some "fitted"
        | FitOutput.Residuals -> Some "residual"
        | FitOutput.Coefficients
        | FitOutput.Summary -> None

    /// A float slot: a float cell, or an int cell read as its float.
    let private resolveFloatSlot (env: Map<string, Cell>) (slot: string) (s: Slot<float>) : Result<float, EvalError> =
        match s with
        | Slot.Lit v -> Ok v
        | Slot.Param n ->
            match Map.tryFind n env with
            | None -> Error(UnboundParam(n, env |> Map.toList |> List.map fst))
            | Some(Float v) -> Ok v
            | Some(Int v) -> Ok(float v)
            | Some other ->
                Error(
                    TypeError(
                        slot
                        + ": param '"
                        + n
                        + "' is bound to a "
                        + cellShape other
                        + " cell, not a number"
                    )
                )

    /// A spec's slots, resolved and checked, and its family and link admitted: the static half of a
    /// fit, decided before any row is read (D53 items 1 and 2).
    type internal FitResolved =
        { Ridge: float
          MaxIter: int
          Tol: float }

    let private finite (v: float) : bool =
        not (System.Double.IsNaN v || System.Double.IsInfinity v)

    let internal fitResolve (env: Map<string, Cell>) (spec: FitSpec) : Result<FitResolved, EvalError> =
        match spec.Family, spec.Link with
        | family, link when not (fitAdmitted family link) -> Error(UnsupportedFit(fitFamilyTag family, fitLinkTag link))
        | FitFamily.Tweedie p, _ when not (finite p) || (p > 0.0 && p < 1.0) ->
            // Phase 598 (D57): no Tweedie distribution has a power strictly between zero and one.
            Error(FitError "fit: the tweedie power is not finite, or lies strictly between 0 and 1")
        | FitFamily.NegativeBinomial(Some t), _ when not (finite t && t > 0.0) ->
            // Phase 600 (D58): a negative binomial's θ is finite and positive.
            Error(FitError "fit: the negative binomial theta is not finite and positive")
        | _ ->
            let ridge =
                match spec.Ridge with
                | None -> Ok 0.0
                | Some s ->
                    resolveFloatSlot env "fit ridge" s
                    |> Result.bind (fun l ->
                        if not (finite l && l >= 0.0) then
                            Error(FitError "fit: the ridge penalty must be finite and not negative")
                        // Phase 595: a penalised IRLS is not a fit this version states; a zero penalty
                        // folds nothing, so it is no penalty under any family.
                        elif l > 0.0 && spec.Family <> FitFamily.Gaussian then
                            Error(FitError "fit: a ridge penalty is fitted under the gaussian family only")
                        // Phase 600: nor under a link other than the identity.
                        elif l > 0.0 && not (fitIsOls spec) then
                            Error(FitError "fit: a ridge penalty is fitted under the identity link only")
                        // Phase 598 (D57): the sandwich of a penalised fit is not a covariance this
                        // version states.
                        elif l > 0.0 && spec.StdErrors <> FitStdErrors.Classical then
                            Error(FitError "fit: robust standard errors are not stated for a ridge fit")
                        else
                            Ok l)

            ridge
            |> Result.bind (fun ridge ->
                resolveIntSlot env "fit maxIter" spec.Control.MaxIter
                |> Result.bind (fun maxIter ->
                    if maxIter < 1 then
                        Error(FitError "fit: maxIter must be at least 1")
                    else
                        resolveFloatSlot env "fit tol" spec.Control.Tol
                        |> Result.bind (fun tol ->
                            if finite tol && tol > 0.0 then
                                Ok
                                    { Ridge = ridge
                                      MaxIter = maxIter
                                      Tol = tol }
                            else
                                Error(FitError "fit: tol must be finite and positive"))))

    /// How the bridge coded one term (Phase 597): a numeric term is its value; a categorical term is
    /// one indicator per coded level, against the levels the training rows held — the first of
    /// which is the reference level unless every level is coded.
    type internal TermCoding =
        | NumericCoding of name: string * unit: UnitOfMeasure option
        | CategoricalCoding of name: string * levels: string[] * coded: string[]

    /// The design a `Fit` step assembles over its input frame (D53 items 3 to 6): one row per logical
    /// row of the frame, in the frame's order.
    type internal FitDesign =
        {
            /// The coefficient names, design-column order.
            Names: string[]
            /// Each coefficient's unit, `unit(y) / unit(xⱼ)`, where one is stated.
            CoefUnits: UnitOfMeasure option[]
            /// The design width, `p`.
            P: int
            /// Per logical row: in the design (no null among the response, the terms, the offset and
            /// the weight).
            Used: bool[]
            /// Row-major, `p` per logical row; zeros at a row not in the design.
            X: float[]
            /// The estimation response, `Cast(FloatType, Col response)`.
            Y: float[]
            /// The offset, `0.0` where the spec has none.
            Off: float[]
            /// The prior weight, `1.0` where the spec has none.
            W: float[]
            /// The response cells as the frame holds them: what a decimal response's outputs read.
            YCells: Cell[]
            /// The response column is decimal.
            Decimal: bool
            /// The response's unit.
            YUnit: UnitOfMeasure option
            /// Per group, its members' logical rows in frame order.
            Members: int[][]
            /// Per group, its key cells.
            KeyCells: Cell[][]
            /// The key fields, in `GroupBy` order.
            KeyCols: Field list
            /// How each term was coded, in term order: what a `Predict` codes another frame's rows by
            /// (Phase 597).
            Coding: TermCoding list
        }

    /// The text a level of a categorical term is named by in its coefficient: the string itself, or
    /// `true` / `false`.
    let private levelText (c: Cell) : string =
        match c with
        | Str s -> s
        | Bool b -> if b then "true" else "false"
        | other -> cellString other

    /// A column the bridge evaluates an expression into: the expression's cells at every logical row,
    /// its column type and its unit. The derive's own typing and unit check, under the step's label.
    let private fitColumn
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (label: string)
        (expr: ColExpr)
        : Result<Cell[] * ColumnType * UnitOfMeasure option, EvalError> =
        resolveUnitsIn label f.Cols expr
        |> Result.bind (fun (unit, resolved) ->
            let tmp = "\u0000fit"

            match derivedTypingIn env f.Cols resolved with
            | Refused -> Error(floatBesideDecimal label)
            | dt ->
                evalDerivedColumn k env f tmp resolved dt
                |> Result.map (fun f' ->
                    let at = f'.Vecs.Length - 1
                    let v = f'.Vecs[at]
                    let ty = (List.last f'.Cols).Type
                    let phys = Frame.physical f'
                    phys |> Array.map (Vec.cellAt v), ty, unit))

    /// A numeric cell's float: an int exactly, a float as itself, a decimal through the cast's rule.
    let private fitFloat (c: Cell) : float option =
        match c with
        | Int i -> Some(float i)
        | Float v -> Some v
        | Decimal s -> DecimalText.tryToFloat s
        | _ -> None

    /// A row the bridge refuses, named by its index among the step's input rows.
    let private rowError (row: int) (what: string) : EvalError =
        FitError("fit: row " + string row + ": " + what)

    /// A linear-algebra refusal in the words the evaluator reports it, one spelling on every host.
    let private linAlgText (e: LinearAlgebra.LinAlgError) : string =
        match e with
        | LinearAlgebra.LinAlgError.ShapeMismatch(op, _, _) -> "a shape mismatch in " + op
        | LinearAlgebra.LinAlgError.Singular i -> "a singular triangle at " + string i
        | LinearAlgebra.LinAlgError.RankDeficient r -> "rank deficient at rank " + string r
        | LinearAlgebra.LinAlgError.NotFinite(r, c) -> "a non-finite value at (" + string r + ", " + string c + ")"
        | LinearAlgebra.LinAlgError.NotTriangular(r, c) -> "not triangular at (" + string r + ", " + string c + ")"
        | LinearAlgebra.LinAlgError.NotConverged n -> "not converged after " + string n + " iterations"
        | LinearAlgebra.LinAlgError.InvalidWeight r -> "an invalid weight at " + string r
        | LinearAlgebra.LinAlgError.InvalidResponse r -> "a response outside the family at " + string r
        | LinearAlgebra.LinAlgError.Separation r -> "the mean left its range at " + string r
        | LinearAlgebra.LinAlgError.UnsupportedModel(f, l) -> "no fit of the " + f + " family under the " + l + " link"
        | LinearAlgebra.LinAlgError.InvalidPower _ -> "a tweedie power no tweedie distribution has"
        | LinearAlgebra.LinAlgError.MeanOutOfRange r ->
            "the mean left the range its family and link allow at " + string r
        | LinearAlgebra.LinAlgError.InvalidTheta _ -> "a negative binomial theta that is not finite and positive"
        | LinearAlgebra.LinAlgError.ThetaNotConverged k ->
            "the negative binomial theta did not converge (alternation " + string k + ")"

    /// One predictor as the bridge codes it.
    type private FitTerm =
        | NumericTerm of name: string * cells: Cell[] * unit: UnitOfMeasure option
        | CategoricalTerm of name: string * cells: Cell[]

    /// Assemble the design (D53 items 3 to 6). The checks run in this order, each refusal naming what
    /// it refuses: the response column, the group keys, the weights column, the appended output's
    /// name, then each term, then the offset, then the rows.
    let internal fitDesign
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (spec: FitSpec)
        : Result<FitDesign, EvalError> =
        let n = Frame.rows f
        let phys = Frame.physical f

        let cellsOf (name: string) =
            let ci = colIndex f.Cols name |> Option.get
            phys |> Array.map (Vec.cellAt f.Vecs[ci])

        let numericType (ty: ColumnType) =
            match ty with
            | IntType
            | FloatType
            | DecimalType -> true
            | _ -> false

        let known (name: string) =
            match colType f.Cols name with
            | Some ty -> Ok ty
            | None -> Error(UnknownColumn(name, available f.Cols))

        let checkKeys () =
            spec.GroupBy
            |> List.tryPick (fun key ->
                match known key with
                | Error e -> Some e
                | Ok _ -> None)

        known spec.Response
        |> Result.bind (fun yTy ->
            if not (numericType yTy) then
                Error(
                    TypeError(
                        "fit: the response '"
                        + spec.Response
                        + "' is a "
                        + ColumnType.tag yTy
                        + " column; a response is int, float or decimal"
                    )
                )
            else
                match checkKeys () with
                | Some e -> Error e
                | None ->
                    let weightsOk =
                        match spec.Weights with
                        | None -> Ok()
                        | Some w ->
                            known w
                            |> Result.bind (fun ty ->
                                if numericType ty then
                                    Ok()
                                else
                                    Error(
                                        TypeError(
                                            "fit: the weights '"
                                            + w
                                            + "' are a "
                                            + ColumnType.tag ty
                                            + " column; weights are int, float or decimal"
                                        )
                                    ))

                    weightsOk
                    |> Result.bind (fun () ->
                        match fitAppendedColumn spec.Output with
                        | Some out when Option.isSome (colIndex f.Cols out) ->
                            Error(FitError("fit: the input already has a column '" + out + "'"))
                        | _ -> Ok())
                    |> Result.bind (fun () ->
                        let names = spec.Predictors |> List.map fst

                        match
                            names
                            |> List.countBy id
                            |> List.tryFind (fun (nm, c) -> c > 1 || (spec.Intercept && nm = interceptTerm))
                        with
                        | Some(nm, _) -> Error(FitError("fit: the term '" + nm + "' is named twice"))
                        | None -> Ok())
                    |> Result.bind (fun () ->
                        fitColumn k env f "fit response" (FitSpec.estimationResponse spec)
                        |> Result.bind (fun (yCast, _, _) ->
                            let terms =
                                spec.Predictors
                                |> List.map (fun (name, e) ->
                                    fitColumn k env f ("fit term '" + name + "'") e
                                    |> Result.bind (fun (cells, ty, unit) ->
                                        match ty with
                                        | IntType
                                        | FloatType
                                        | DecimalType -> Ok(NumericTerm(name, cells, unit))
                                        | StringType
                                        | BoolType -> Ok(CategoricalTerm(name, cells))
                                        | other ->
                                            Error(
                                                TypeError(
                                                    "fit: the term '"
                                                    + name
                                                    + "' is a "
                                                    + ColumnType.tag other
                                                    + " expression; a term is numeric, string or boolean"
                                                )
                                            )))
                                |> sequenceR

                            let offset =
                                match spec.Offset with
                                | None -> Ok None
                                | Some e ->
                                    fitColumn k env f "fit offset" e
                                    |> Result.bind (fun (cells, ty, _) ->
                                        if numericType ty then
                                            Ok(Some cells)
                                        else
                                            Error(
                                                TypeError(
                                                    "fit: the offset is a "
                                                    + ColumnType.tag ty
                                                    + " expression; an offset is numeric"
                                                )
                                            ))

                            terms
                            |> Result.bind (fun terms ->
                                offset
                                |> Result.bind (fun offset ->
                                    let yCells = cellsOf spec.Response
                                    let wCells = spec.Weights |> Option.map cellsOf

                                    let present (c: Cell) =
                                        match c with
                                        | Null -> false
                                        | _ -> true

                                    // Listwise deletion (D49 item 13): a null anywhere the row is read.
                                    let used =
                                        Array.init n (fun i ->
                                            present yCast[i]
                                            && terms
                                               |> List.forall (fun t ->
                                                   match t with
                                                   | NumericTerm(_, cells, _)
                                                   | CategoricalTerm(_, cells) -> present cells[i])
                                            && (match offset with
                                                | Some cells -> present cells[i]
                                                | None -> true)
                                            && (match wCells with
                                                | Some cells -> present cells[i]
                                                | None -> true))

                                    // The levels of each categorical term, over the rows in the design,
                                    // in the cell order; the first is the reference (D53 item 4).
                                    let levelsOf (cells: Cell[]) =
                                        let seen = System.Collections.Generic.HashSet<string>()
                                        let levels = ResizeArray<Cell>()

                                        for i in 0 .. n - 1 do
                                            if used[i] && seen.Add(levelText cells[i]) then
                                                levels.Add cells[i]

                                        levels.ToArray()
                                        |> Array.sortWith (fun a b -> compareCells a b |> Option.defaultValue 0)

                                    let firstCategorical =
                                        terms
                                        |> List.tryFindIndex (fun t ->
                                            match t with
                                            | CategoricalTerm _ -> true
                                            | NumericTerm _ -> false)

                                    let yUnit = colUnit f.Cols spec.Response
                                    let statUnits = fitStatUnits spec yUnit
                                    let etaUnit = statUnits.Eta

                                    // Phase 600 (D58 item 9): no coefficient states a unit the
                                    // algebra cannot carry.
                                    let coefUnit (u: UnitOfMeasure option) =
                                        if statUnits.EtaStated then u else None

                                    let coding =
                                        terms
                                        |> List.mapi (fun ti t ->
                                            match t with
                                            | NumericTerm(name, _, unit) -> NumericCoding(name, unit)
                                            | CategoricalTerm(name, cells) ->
                                                let levels = levelsOf cells |> Array.map levelText

                                                let coded =
                                                    if not spec.Intercept && firstCategorical = Some ti then
                                                        levels
                                                    else
                                                        Array.skip (min 1 levels.Length) levels

                                                CategoricalCoding(name, levels, coded))

                                    // Each design column: its name, its unit, and its value at a row.
                                    let columns: (string * UnitOfMeasure option * (int -> float)) list =
                                        (if spec.Intercept then
                                             [ interceptTerm, coefUnit etaUnit, (fun _ -> 1.0) ]
                                         else
                                             [])
                                        @ (List.zip terms coding
                                           |> List.map (fun (t, c) ->
                                               match t, c with
                                               | NumericTerm(name, cells, unit), _ ->
                                                   [ name,
                                                     coefUnit (unitQuotient etaUnit unit),
                                                     (fun i -> fitFloat cells[i] |> Option.defaultValue nan) ]
                                               | CategoricalTerm(name, cells), CategoricalCoding(_, _, coded) ->
                                                   coded
                                                   |> Array.toList
                                                   |> List.map (fun token ->
                                                       name + "[" + token + "]",
                                                       coefUnit etaUnit,
                                                       (fun i -> if levelText cells[i] = token then 1.0 else 0.0))
                                               | CategoricalTerm _, NumericCoding _ -> [])
                                           |> List.concat)

                                    let names = columns |> List.map (fun (nm, _, _) -> nm) |> List.toArray
                                    let p = names.Length

                                    match names |> Array.countBy id |> Array.tryFind (fun (_, c) -> c > 1) with
                                    | Some(nm, _) -> Error(FitError("fit: the term '" + nm + "' is named twice"))
                                    | None when p = 0 -> Error(FitError "fit: the design has no column")
                                    | None ->
                                        let colFns = columns |> List.map (fun (_, _, g) -> g) |> List.toArray
                                        let x = Array.zeroCreate<float> (n * p)
                                        let y = Array.zeroCreate<float> n
                                        let off = Array.zeroCreate<float> n
                                        let w = Array.create n 1.0
                                        let mutable failure = None
                                        let mutable i = 0

                                        while Option.isNone failure && i < n do
                                            if used[i] then
                                                match fitFloat yCast[i] with
                                                | Some v when finite v -> y[i] <- v
                                                | _ -> failure <- Some(rowError i "the response is not finite")

                                                let mutable j = 0

                                                while Option.isNone failure && j < p do
                                                    let v = colFns[j]i

                                                    if finite v then
                                                        x[i * p + j] <- v
                                                    else
                                                        failure <-
                                                            Some(
                                                                rowError
                                                                    i
                                                                    ("the term '" + names[j] + "' is not finite")
                                                            )

                                                    j <- j + 1

                                                if Option.isNone failure then
                                                    match offset with
                                                    | Some cells ->
                                                        match fitFloat cells[i] with
                                                        | Some v when finite v -> off[i] <- v
                                                        | _ ->
                                                            failure <- Some(rowError i "the offset is not finite")
                                                    | None -> ()

                                                if Option.isNone failure then
                                                    match wCells with
                                                    | Some cells ->
                                                        match fitFloat cells[i] with
                                                        | Some v when finite v && v >= 0.0 -> w[i] <- v
                                                        | _ ->
                                                            failure <-
                                                                Some(
                                                                    rowError
                                                                        i
                                                                        "the weight is negative or not finite"
                                                                )
                                                    | None -> ()

                                            i <- i + 1

                                        match failure with
                                        | Some e -> Error e
                                        | None ->
                                            // Groups over the rows in the design, in first-appearance
                                            // order (`GroupBy`'s rule).
                                            let usedRows = Array.init n id |> Array.filter (fun i -> used[i])

                                            let keyIdx =
                                                spec.GroupBy
                                                |> List.map (fun key -> colIndex f.Cols key |> Option.get)
                                                |> List.toArray

                                            let keyVecs = keyIdx |> Array.map (fun ci -> f.Vecs[ci])

                                            let slotOf, _ =
                                                keySlots
                                                    Kernels.oneThread
                                                    keyVecs
                                                    (usedRows |> Array.map (fun i -> phys[i]))

                                            let groups = if usedRows.Length = 0 then 0 else (Array.max slotOf + 1)
                                            let members = Array.init groups (fun _ -> ResizeArray<int>())

                                            for u in 0 .. usedRows.Length - 1 do
                                                members[slotOf[u]].Add usedRows[u]

                                            let members = members |> Array.map (fun m -> m.ToArray())

                                            let keyCells =
                                                members
                                                |> Array.map (fun m ->
                                                    keyIdx
                                                    |> Array.map (fun ci -> Vec.cellAt f.Vecs[ci] phys[m[0]]))

                                            Ok
                                                { Names = names
                                                  CoefUnits =
                                                    columns |> List.map (fun (_, u, _) -> u) |> List.toArray
                                                  P = p
                                                  Used = used
                                                  X = x
                                                  Y = y
                                                  Off = off
                                                  W = w
                                                  YCells = yCells
                                                  Decimal = (yTy = DecimalType)
                                                  YUnit = yUnit
                                                  Members = members
                                                  KeyCells = keyCells
                                                  KeyCols =
                                                    spec.GroupBy
                                                    |> List.map (fun key ->
                                                        f.Cols |> List.find (fun fd -> fd.Name = key))
                                                  Coding = coding })))))

    /// The data accumulator of one group: its member rows from `from` on folded onto `acc`, in member
    /// order (D49 items 8 and 9). `addRows` over the batch, which `RegressionLaws` holds byte-equal
    /// to `addRow` row by row — so a group extended by its appended rows is the group folded whole.
    let private fitFold
        (d: FitDesign)
        (members: int[])
        (from: int)
        (acc: QRAccumulator)
        : Result<QRAccumulator, EvalError> =
        let rows = members.Length - from

        if rows <= 0 then
            Ok acc
        else
            let p = d.P
            let x = LinearAlgebra.Matrix.init rows p (fun r j -> d.X[members[from + r] * p + j])

            let y =
                Vector.adopt (
                    Array.init rows (fun r ->
                        let i = members[from + r]
                        d.Y[i] - d.Off[i])
                )

            let w = Vector.adopt (Array.init rows (fun r -> d.W[members[from + r]]))

            QRAccumulator.addRows x y w acc
            |> Result.mapError (fun e ->
                match e with
                | LinearAlgebra.LinAlgError.InvalidWeight r ->
                    FitError(
                        "fit: row "
                        + string members[from + r]
                        + ": the weight is negative or not finite"
                    )
                | LinearAlgebra.LinAlgError.NotFinite(r, _) ->
                    FitError("fit: row " + string members[from + r] + ": a scaled value overflowed")
                | other -> FitError("fit: " + linAlgText other))

    /// One group's fit read off its data accumulator (D53 items 5 to 9).
    type internal FitGroup =
        {
            /// Per design column: the estimate, `None` where the column is aliased.
            Estimates: float option[]
            StdErrors: float option[]
            T: float option[]
            /// Rows of positive weight.
            N: int
            Rank: int
            Df: int
            Sigma: float option
            Dispersion: float option
            R2: float option
            /// A float, or for a decimal response a decimal.
            Deviance: Cell
            NullDeviance: Cell
            /// Per member (aligned with the group's members): the fitted value and the residual, each
            /// a float, or for a decimal response a decimal.
            Fitted: Cell[]
            Residuals: Cell[]
            /// The IRLS iterations run: one for least squares (Phase 595).
            Iterations: int
            /// The factorization the standard errors read — for a GLM its last weighted solve — which a
            /// `Predict` reads a row's standard errors from (Phase 597).
            Ls: LeastSquaresFit
            /// The negative binomial's θ, its standard error where estimated, and the alternations the
            /// estimate took (Phase 600); `None`, `None` and zero for every other family.
            Theta: float option
            ThetaStdError: float option
            Alternations: int
        }

    /// The sandwich variant a spec's standard errors name, if any (Phase 598, D57).
    let private fitRobustVariant (e: FitStdErrors) : RobustVariant option =
        match e with
        | FitStdErrors.Classical -> None
        | FitStdErrors.HC0 -> Some RobustVariant.HC0
        | FitStdErrors.HC1 -> Some RobustVariant.HC1
        | FitStdErrors.HC2 -> Some RobustVariant.HC2
        | FitStdErrors.HC3 -> Some RobustVariant.HC3

    /// A group's design over its members, row `t` the member `members[t]`'s design row.
    let private fitGroupMatrix (d: FitDesign) (members: int[]) : LinearAlgebra.Matrix =
        LinearAlgebra.Matrix.init members.Length d.P (fun t j -> d.X[members[t] * d.P + j])

    /// `t = estimate / std_error` per design column, null where the error is zero or null (D53 item 8).
    let private fitTStats (beta: float option[]) (se: float option[]) : float option[] =
        Array.init beta.Length (fun j ->
            match beta[j], se[j] with
            | Some b, Some s when s > 0.0 -> Some(b / s)
            | _ -> None)

    /// The fit of one group from its data accumulator: the ridge rows folded in after the data, the
    /// solve, then the fitted values, the residuals and the statistics, each a pinned left-to-right
    /// fold in member order.
    let internal fitGroupOf
        (d: FitDesign)
        (r: FitResolved)
        (spec: FitSpec)
        (members: int[])
        (data: QRAccumulator)
        : Result<FitGroup, EvalError> =
        let p = d.P

        let penalised =
            if r.Ridge > 0.0 then
                // One row of `√λ` per non-intercept column, in column order (D53 item 7).
                let s = sqrt r.Ridge
                let first = if spec.Intercept then 1 else 0
                let rows = p - first

                if rows <= 0 then
                    Ok data
                else
                    let x =
                        LinearAlgebra.Matrix.init rows p (fun i j -> if j = first + i then s else 0.0)

                    QRAccumulator.addRows
                        x
                        (Vector.adopt (Array.zeroCreate rows))
                        (Vector.adopt (Array.create rows 1.0))
                        data
                    |> Result.mapError (fun e -> FitError("fit: the ridge rows: " + linAlgText e))
            else
                Ok data

        penalised
        |> Result.bind (fun acc ->
            LeastSquares.ofAccumulator acc
            |> Result.mapError (fun e -> FitError("fit: " + linAlgText e)))
        |> Result.bind (fun ls ->
            let beta = ls.Coefficients |> List.toArray
            let n = data.Count
            let rank = ls.Rank
            let df = max 0 (n - rank)

            let fittedFloat (i: int) =
                let mutable acc = 0.0

                for j in 0 .. p - 1 do
                    match beta[j] with
                    | Some b -> acc <- acc + d.X[i * p + j] * b
                    | None -> ()

                acc + d.Off[i]

            let m = members.Length
            let fitted = Array.init m (fun t -> fittedFloat members[t])

            // The null model's mean (R's `glm.fit`, Phase 595): with an intercept the intercept-only
            // fit with the offset, `Σw(y − offset) / Σw + offset` (the weighted mean of the response
            // where there is no offset, `y − 0` being `y`), without one the offset.
            let mutable sw = 0.0
            let mutable swy = 0.0

            for t in 0 .. m - 1 do
                let i = members[t]
                sw <- sw + d.W[i]
                swy <- swy + d.W[i] * (d.Y[i] - d.Off[i])

            let sw = sw
            let swy = swy

            let nullMean (i: int) =
                if spec.Intercept then swy / sw + d.Off[i] else d.Off[i]

            let fail (what: string) = FitError("fit: " + what)

            let devianceAndCells: Result<Cell * Cell * float * float * Cell[] * Cell[], EvalError> =
                if d.Decimal then
                    // D53 item 10: every output stated against a decimal response is decimal — the fitted
                    // value is the float estimate's exact decimal, and the residual, the deviance and the
                    // null deviance are exact decimal arithmetic on the response as the frame holds it.
                    let dec (v: float) =
                        match DecimalArith.ofFloat v with
                        | Some s -> Ok s
                        | None -> Error(fail "a fitted value is not finite")

                    let yDec (i: int) =
                        match d.YCells[i] with
                        | Decimal s -> Ok s
                        | Int v -> Ok(string v)
                        | other -> Error(fail ("the response cell " + cellString other + " is not decimal"))

                    let opt (what: string) (o: string option) =
                        match o with
                        | Some s -> Ok s
                        | None -> Error(fail what)

                    let fittedDec = Array.zeroCreate<string> m
                    let residDec = Array.zeroCreate<string> m
                    let mutable dev = "0"
                    let mutable nul = "0"
                    let mutable failure = None
                    let mutable t = 0

                    while Option.isNone failure && t < m do
                        let i = members[t]
                        let devNow = dev
                        let nulNow = nul

                        let step =
                            dec fitted[t]
                            |> Result.bind (fun fd ->
                                yDec i
                                |> Result.bind (fun yd ->
                                    dec d.W[i]
                                    |> Result.bind (fun wd ->
                                        opt "a residual" (DecimalArith.sub yd fd)
                                        |> Result.bind (fun rd ->
                                            dec (nullMean i)
                                            |> Result.bind (fun md ->
                                                opt "a null residual" (DecimalArith.sub yd md)
                                                |> Result.bind (fun nd ->
                                                    opt
                                                        "a deviance term"
                                                        (DecimalArith.mul rd rd
                                                         |> Option.bind (DecimalArith.mul wd))
                                                    |> Result.bind (fun dt ->
                                                        opt
                                                            "a null deviance term"
                                                            (DecimalArith.mul nd nd
                                                             |> Option.bind (DecimalArith.mul wd))
                                                        |> Result.bind (fun nt ->
                                                            opt "the deviance" (DecimalArith.add devNow dt)
                                                            |> Result.bind (fun dev' ->
                                                                opt
                                                                    "the null deviance"
                                                                    (DecimalArith.add nulNow nt)
                                                                |> Result.map (fun nul' -> fd, rd, dev', nul'))))))))))

                        match step with
                        | Ok(fd, rd, dev', nul') ->
                            fittedDec[t] <- fd
                            residDec[t] <- rd
                            dev <- dev'
                            nul <- nul'
                        | Error e -> failure <- Some e

                        t <- t + 1

                    match failure with
                    | Some e -> Error e
                    | None ->
                        let toF (s: string) =
                            DecimalText.tryToFloat s |> Option.defaultValue nan

                        Ok(
                            Decimal dev,
                            Decimal nul,
                            toF dev,
                            toF nul,
                            fittedDec |> Array.map Decimal,
                            residDec |> Array.map Decimal
                        )
                else
                    let mutable dev = 0.0
                    let mutable nul = 0.0
                    let resid = Array.zeroCreate<float> m

                    for t in 0 .. m - 1 do
                        let i = members[t]
                        let rr = d.Y[i] - fitted[t]
                        resid[t] <- rr
                        dev <- dev + d.W[i] * (rr * rr)
                        let nr = d.Y[i] - nullMean i
                        nul <- nul + d.W[i] * (nr * nr)

                    Ok(Float dev, Float nul, dev, nul, fitted |> Array.map Float, resid |> Array.map Float)

            devianceAndCells
            |> Result.bind (fun (devCell, nulCell, dev, nul, fittedCells, residCells) ->
                let dispersion = if df > 0 then Some(dev / float df) else None

                // Phase 598 (D57): the sandwich over the group's rows, the scores `w·(y − ŷ)·x` in
                // member order; otherwise the classical errors.
                let stdErrors =
                    match fitRobustVariant spec.StdErrors with
                    | Some variant ->
                        LeastSquares.robustStandardErrors
                            variant
                            (fitGroupMatrix d members)
                            (Vector.adopt (Array.init m (fun t -> d.W[members[t]])))
                            (Vector.adopt (Array.init m (fun t -> d.Y[members[t]] - fitted[t])))
                            ls
                        |> Result.map List.toArray
                        |> Result.mapError (fun e -> FitError("fit: " + linAlgText e))
                    | None ->
                        match dispersion with
                        | Some phi -> Ok(LeastSquares.standardErrors phi ls |> List.toArray)
                        | None -> Ok(Array.create p None)

                stdErrors
                |> Result.map (fun se ->
                    { Estimates = beta
                      StdErrors = se
                      T = fitTStats beta se
                      N = n
                      Rank = rank
                      Df = df
                      Sigma = dispersion |> Option.map sqrt
                      Dispersion = dispersion
                      R2 =
                        (if sw > 0.0 && nul > 0.0 then
                             Some(1.0 - dev / nul)
                         else
                             None)
                      Deviance = devCell
                      NullDeviance = nulCell
                      Fitted = fittedCells
                      Residuals = residCells
                      Iterations = 1
                      Theta = None
                      ThetaStdError = None
                      Alternations = 0
                      Ls = ls })))

    /// One group's generalised linear model (Phase 595): `Glm.fit` over the group's rows
    /// in member order — the design, the estimation response, the prior weights and the offset as the
    /// bridge assembled them — and the null deviance by `Glm.nullDeviance`. The fitted value is the mean
    /// `μ` and the residual the response residual `y − μ`; for a decimal response the fitted value is
    /// the mean's exact decimal and the residual exact decimal arithmetic, while the deviances, being
    /// sums of logarithms, stay floats (`fitStatUnits`). Refusals name the step's input row: a response
    /// outside the family and a non-converging fit as `FitError`, a mean out of range as the separation
    /// `OverflowError`.
    let internal fitGlmGroupOf
        (d: FitDesign)
        (r: FitResolved)
        (spec: FitSpec)
        (members: int[])
        : Result<FitGroup, EvalError> =
        let m = members.Length
        let x = fitGroupMatrix d members

        let column (a: float[]) =
            Vector.adopt (Array.init m (fun t -> a[members[t]]))

        let y = column d.Y
        let w = column d.W
        let o = column d.Off

        let family = glmFamily spec.Family
        let link = glmLink spec.Link

        let refuse (e: LinearAlgebra.LinAlgError) : EvalError =
            match e with
            | LinearAlgebra.LinAlgError.InvalidResponse t ->
                rowError members[t] ("the response is outside the " + fitFamilyTag spec.Family + " family's range")
            | LinearAlgebra.LinAlgError.InvalidWeight t -> rowError members[t] "the weight is negative or not finite"
            | LinearAlgebra.LinAlgError.Separation t ->
                OverflowError("fit: row " + string members[t] + ": the mean left its range (separation)")
            | LinearAlgebra.LinAlgError.NotConverged n ->
                FitError("fit: not converged after " + string n + " iterations")
            | LinearAlgebra.LinAlgError.MeanOutOfRange t ->
                rowError members[t] "the mean left the range its family and link allow"
            | other -> FitError("fit: " + linAlgText other)

        match GlmControl.create r.MaxIter r.Tol with
        | None -> Error(FitError "fit: the control is out of range")
        | Some control ->
            Glm.fit family link control x y w o
            |> Result.bind (fun g ->
                // An estimated negative binomial's null model is stated at the fit's θ (D58 item 8).
                let nullFamily =
                    match family with
                    | GlmFamily.NegativeBinomial None -> GlmFamily.NegativeBinomial g.Theta
                    | other -> other

                Glm.nullDeviance nullFamily link control spec.Intercept y w o
                |> Result.bind (fun nul ->
                    // Phase 598 (D57): the sandwich in the fit's working weights and residuals.
                    match fitRobustVariant spec.StdErrors with
                    | Some variant ->
                        Glm.robustStandardErrors variant x g
                        |> Result.map (fun se -> g, nul, List.toArray se)
                    | None -> Ok(g, nul, List.toArray g.StdErrors)))
            |> Result.mapError refuse
            |> Result.bind (fun (g, nul, se) ->
                let mu = Vector.toArray g.Fitted
                let fail (what: string) = FitError("fit: " + what)

                let cells: Result<Cell[] * Cell[], EvalError> =
                    if d.Decimal then
                        let fitted = Array.zeroCreate<Cell> m
                        let resid = Array.zeroCreate<Cell> m
                        let mutable failure = None
                        let mutable t = 0

                        while Option.isNone failure && t < m do
                            let yd =
                                match d.YCells[members[t]] with
                                | Decimal s -> Some s
                                | Int v -> Some(string v)
                                | _ -> None

                            match DecimalArith.ofFloat mu[t], yd with
                            | Some fd, Some yd ->
                                match DecimalArith.sub yd fd with
                                | Some rd ->
                                    fitted[t] <- Decimal fd
                                    resid[t] <- Decimal rd
                                | None -> failure <- Some(fail "a residual")
                            | _ -> failure <- Some(fail "a fitted value is not a decimal")

                            t <- t + 1

                        match failure with
                        | Some e -> Error e
                        | None -> Ok(fitted, resid)
                    else
                        let ya = Vector.toArray y
                        Ok(mu |> Array.map Float, Array.init m (fun t -> Float(ya[t] - mu[t])))

                cells
                |> Result.map (fun (fitted, resid) ->
                    let mutable sw = 0.0

                    for v in Vector.toArray w do
                        sw <- sw + v

                    let beta = g.Coefficients |> List.toArray

                    { Estimates = beta
                      StdErrors = se
                      T = fitTStats beta se
                      N = g.Count
                      Rank = g.Rank
                      Df = g.ResidualDf
                      Sigma = g.Dispersion |> Option.map sqrt
                      Dispersion = g.Dispersion
                      R2 =
                        (if sw > 0.0 && nul > 0.0 then
                             Some(1.0 - g.Deviance / nul)
                         else
                             None)
                      Deviance = Float g.Deviance
                      NullDeviance = Float nul
                      Fitted = fitted
                      Residuals = resid
                      Iterations = g.Iterations
                      Ls = g.LeastSquares
                      Theta = g.Theta
                      ThetaStdError = g.ThetaStdError
                      Alternations = g.Alternations }))

    /// One group's fit (D53; Phase 595): a generalised linear model from the pinned start, a least-squares
    /// fit by its member rows from `from` on folded onto `acc0`. Answers the data accumulator with the
    /// fit — `acc0` unchanged for a GLM, which keeps no state.
    let private fitOneGroup
        (d: FitDesign)
        (r: FitResolved)
        (spec: FitSpec)
        (members: int[])
        (acc0: QRAccumulator)
        (from: int)
        : Result<QRAccumulator * FitGroup, EvalError> =
        if not (fitIsOls spec) then
            fitGlmGroupOf d r spec members |> Result.map (fun fg -> acc0, fg)
        else
            fitFold d members from acc0
            |> Result.bind (fun acc -> fitGroupOf d r spec members acc |> Result.map (fun fg -> acc, fg))

    /// What a maintained fit carries from one evaluation to the next (D53 item 12): the design's layout
    /// and, per group token, the group's member row tokens in order and its data accumulator.
    type internal FitMemo =
        { Layout: string
          Groups: Map<string, string[] * QRAccumulator> }

    /// The memo of a fit nothing maintains.
    let internal emptyFitMemo: FitMemo = { Layout = ""; Groups = Map.empty }

    /// How a maintained fit reads the rows it is handed: each logical row's token, and whether the row
    /// is the very row the prior evaluation held under that token.
    type internal FitRows =
        { Tokens: string[]
          Stable: int -> bool }

    /// The fit's output types: a float column, or a decimal one for a decimal response.
    let private fitCellType (d: FitDesign) : ColumnType =
        if d.Decimal then DecimalType else FloatType

    /// `Fit` over the frame (D53), maintained against `prior` where `rows` is given: a group whose
    /// member tokens extend the prior members — every prior member stable, in the prior order —
    /// continues from the prior accumulator by its appended rows; every other group is folded whole.
    /// Answers the output frame, the memo for the next evaluation (empty without `rows`) and how
    /// many groups were folded from their first row.
    let internal evalFitWith
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (spec: FitSpec)
        (rows: FitRows option)
        (prior: FitMemo)
        : Result<Frame * FitMemo * int, EvalError> =
        fitResolve env spec
        |> Result.bind (fun resolved ->
            fitDesign k env f spec
            |> Result.bind (fun d ->
                let layout = System.String.Join("\u0001", d.Names)
                let groups = d.Members.Length
                let keyToken (g: int) = rowTokenStringOfArray d.KeyCells[g]

                let start (g: int) : QRAccumulator * int =
                    let fresh = QRAccumulator.empty d.P, 0

                    match rows with
                    | Some rr when prior.Layout = layout ->
                        match Map.tryFind (keyToken g) prior.Groups with
                        | Some(old, acc) when old.Length <= d.Members[g].Length ->
                            let ms = d.Members[g]
                            let mutable ok = true
                            let mutable j = 0

                            while ok && j < old.Length do
                                ok <- rr.Tokens[ms[j]] = old[j] && rr.Stable ms[j]
                                j <- j + 1

                            if ok then acc, old.Length else fresh
                        | _ -> fresh
                    | _ -> fresh

                // Phase 595: a generalised linear model keeps no state — every group is fitted from the
                // pinned start on every evaluation (D49 item 22; a warm start is never certified).
                let glm = not (fitIsOls spec)
                let mutable refit = 0
                let accs = Array.zeroCreate<QRAccumulator> groups
                let fits = Array.zeroCreate<FitGroup> groups
                let mutable failure = None
                let mutable g = 0

                while Option.isNone failure && g < groups do
                    let gi = g

                    let acc0, from = if glm then QRAccumulator.empty d.P, 0 else start gi

                    if from = 0 then
                        refit <- refit + 1

                    match fitOneGroup d resolved spec d.Members[gi] acc0 from with
                    | Ok(acc, fg) ->
                        accs[gi] <- acc
                        fits[gi] <- fg
                    | Error e -> failure <- Some e

                    g <- g + 1

                match failure with
                | Some e -> Error e
                | None ->
                    let memo =
                        match rows with
                        | Some rr when not glm ->
                            { Layout = layout
                              Groups =
                                Seq.init groups (fun g ->
                                    keyToken g, ((d.Members[g] |> Array.map (fun i -> rr.Tokens[i])), accs[g]))
                                |> Map.ofSeq }
                        | _ -> emptyFitMemo

                    let floatCell (o: float option) =
                        match o with
                        | Some v -> Float v
                        | None -> Null

                    let unitCell (u: UnitOfMeasure option) =
                        match u with
                        | Some u -> Str(Unit.render u)
                        | None -> Null

                    let table (cols: Field list) (cells: Cell[][]) : Frame =
                        { Cols = cols
                          Vecs =
                            cols
                            |> List.mapi (fun c fd -> Vec.pack fd.Type (cells |> Array.map (fun row -> row[c])))
                            |> List.toArray
                          Sel = None
                          Count = cells.Length }

                    let units = fitStatUnits spec d.YUnit

                    let out =
                        match spec.Output with
                        | FitOutput.Coefficients ->
                            let cols =
                                d.KeyCols
                                @ [ Field.create "term" StringType
                                    Field.create "estimate" FloatType
                                    Field.create "std_error" FloatType
                                    Field.create "t" FloatType
                                    Field.create "unit" StringType ]

                            let cells =
                                [| for g in 0 .. groups - 1 do
                                       for j in 0 .. d.P - 1 do
                                           Array.append
                                               d.KeyCells[g]
                                               [| Str d.Names[j]
                                                  floatCell fits[g].Estimates[j]
                                                  floatCell fits[g].StdErrors[j]
                                                  floatCell fits[g].T[j]
                                                  unitCell d.CoefUnits[j] |] |]

                            table cols cells
                        | FitOutput.Summary ->
                            let devType = if units.DecimalDeviance then fitCellType d else FloatType

                            let cols =
                                d.KeyCols
                                @ [ Field.create "n" IntType
                                    Field.create "df" IntType
                                    Field.create "sigma" FloatType |> stated units.Sigma
                                    Field.create "r2" FloatType
                                    Field.create "rank" IntType
                                    Field.create "deviance" devType |> stated units.Deviance
                                    Field.create "null_deviance" devType |> stated units.Deviance
                                    Field.create "dispersion" FloatType |> stated units.Dispersion
                                    Field.create "iterations" IntType
                                    Field.create "converged" BoolType ]
                                @ (if fitStatesTheta spec then
                                       [ Field.create "theta" FloatType |> stated d.YUnit
                                         Field.create "theta_std_error" FloatType |> stated d.YUnit
                                         Field.create "alternations" IntType ]
                                   else
                                       [])

                            let cells =
                                Array.init groups (fun g ->
                                    let fg = fits[g]

                                    Array.append
                                        d.KeyCells[g]
                                        (Array.append
                                            [| Int fg.N
                                               Int fg.Df
                                               floatCell fg.Sigma
                                               floatCell fg.R2
                                               Int fg.Rank
                                               fg.Deviance
                                               fg.NullDeviance
                                               floatCell fg.Dispersion
                                               Int fg.Iterations
                                               Bool true |]
                                            (if fitStatesTheta spec then
                                                 [| floatCell fg.Theta
                                                    floatCell fg.ThetaStdError
                                                    Int fg.Alternations |]
                                             else
                                                 [||])))

                            table cols cells
                        | FitOutput.Fitted
                        | FitOutput.Residuals ->
                            let name = fitAppendedColumn spec.Output |> Option.get
                            let n = Frame.rows f
                            let cells = Array.create n Null

                            for g in 0 .. groups - 1 do
                                let source =
                                    if spec.Output = FitOutput.Fitted then
                                        fits[g].Fitted
                                    else
                                        fits[g].Residuals

                                d.Members[g] |> Array.iteri (fun t i -> cells[i] <- source[t])

                            let ty = fitCellType d
                            let phys = Frame.physical f
                            let v = Vec.packAt ty f.Count (fun i -> phys[i]) cells
                            Frame.appendColumn f name ty v |> unitised name d.YUnit

                    Ok(out, memo, refit)))

    /// `Fit` over the frame, as the reference evaluates it: every group folded from its first row.
    let private evalFit (k: KernelSet) (env: Map<string, Cell>) (f: Frame) (spec: FitSpec) : Result<Frame, EvalError> =
        evalFitWith k env f spec None emptyFitMemo
        |> Result.map (fun (out, _, _) -> out)

    // -----------------------------------------------------------------------------------------
    //  Phase 597 — `Predict` (D56): a row coded as the fit coded its training rows, scored against its
    //  group's fit; its standard errors read the fit's `R` by one triangular solve and a sum of
    //  squares, and an interval's quantile is read from the t table through `Interpolate`.
    // -----------------------------------------------------------------------------------------

    /// A prediction's refusal, in the words the evaluator reports it.
    let private predictError (what: string) : EvalError = FitError("predict: " + what)

    /// A row of the step's input a prediction refuses.
    let private predictRowError (row: int) (what: string) : EvalError =
        predictError ("row " + string row + ": " + what)

    /// Group key cells as a refusal names them.
    let private keyText (cells: Cell[]) : string =
        "(" + System.String.Join(", ", cells |> Array.map cellString) + ")"

    /// The interval resolved before any row is read: its kind and its level. A level the t table does
    /// not carry is refused naming the tabled ones; a prediction interval outside the gaussian family,
    /// and any interval from a coefficient table, are refused by name.
    let private predictInterval
        (env: Map<string, Cell>)
        (spec: PredictSpec)
        : Result<(IntervalKind * float) option, EvalError> =
        match spec.Interval with
        | None -> Ok None
        | Some i ->
            match spec.Model with
            | PredictModel.Coefficients _ ->
                Error(predictError "a coefficient table carries no factorization; an interval needs the training rows")
            | PredictModel.Training _ ->
                resolveFloatSlot env "predict level" i.Level
                |> Result.bind (fun level ->
                    if not (Array.contains level StudentT.levels) then
                        Error(
                            predictError (
                                "the level "
                                + cellString (Float level)
                                + " is not tabled; a level is one of "
                                + System.String.Join(", ", StudentT.levels |> Array.map (Float >> cellString))
                            )
                        )
                    elif i.Kind = IntervalKind.Prediction && spec.Fit.Family <> FitFamily.Gaussian then
                        Error(predictError "a prediction interval is stated for the gaussian family only")
                    // Phase 600: and under the identity link, where a new observation's error is on
                    // the linear predictor's scale.
                    elif i.Kind = IntervalKind.Prediction && spec.Fit.Link <> FitLink.Identity then
                        Error(predictError "a prediction interval is stated under the identity link only")
                    else
                        Ok(Some(i.Kind, level)))

    /// The step's input rows coded by the training coding (D56 item 2): per logical row, the design row
    /// and the offset, or `None` where a term or the offset is null. Every term is evaluated over the
    /// input as the fit evaluated it over the training rows; a term whose kind or unit differs from the
    /// training term's, a level the training rows did not hold and a non-finite value are refused by
    /// name.
    let private predictDesign
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (fit: FitSpec)
        (coding: TermCoding list)
        (p: int)
        : Result<(float[] * float) option[], EvalError> =
        let n = Frame.rows f

        let isNumeric (ty: ColumnType) =
            match ty with
            | IntType
            | FloatType
            | DecimalType -> true
            | _ -> false

        let unitText (u: UnitOfMeasure option) =
            match u with
            | Some u -> Unit.render u
            | None -> "no unit"

        List.zip fit.Predictors coding
        |> List.map (fun ((name, e), c) ->
            fitColumn k env f ("predict term '" + name + "'") e
            |> Result.bind (fun (cells, ty, unit) ->
                match c with
                | NumericCoding(_, trained) when isNumeric ty ->
                    if unit = trained then
                        Ok(c, cells)
                    else
                        Error(
                            predictError (
                                "the term '"
                                + name
                                + "' is in "
                                + unitText unit
                                + "; the fit's was in "
                                + unitText trained
                            )
                        )
                | CategoricalCoding _ when ty = StringType || ty = BoolType -> Ok(c, cells)
                | _ ->
                    Error(
                        TypeError(
                            "predict: the term '"
                            + name
                            + "' is a "
                            + ColumnType.tag ty
                            + " expression; the fit coded it as "
                            + (match c with
                               | NumericCoding _ -> "numeric"
                               | CategoricalCoding _ -> "categorical")
                        )
                    )))
        |> sequenceR
        |> Result.bind (fun terms ->
            match fit.Offset with
            | None -> Ok(terms, None)
            | Some e ->
                fitColumn k env f "predict offset" e
                |> Result.bind (fun (cells, ty, _) ->
                    if isNumeric ty then
                        Ok(terms, Some cells)
                    else
                        Error(
                            TypeError(
                                "predict: the offset is a "
                                + ColumnType.tag ty
                                + " expression; an offset is numeric"
                            )
                        )))
        |> Result.bind (fun (terms, offset) ->
            let rows = Array.create n None
            let mutable failure = None
            let mutable i = 0

            let isNull (c: Cell) =
                match c with
                | Null -> true
                | _ -> false

            while Option.isNone failure && i < n do
                let present =
                    terms |> List.forall (fun (_, cells) -> not (isNull cells[i]))
                    && (match offset with
                        | Some cells -> not (isNull cells[i])
                        | None -> true)

                if present then
                    let x = Array.zeroCreate<float> p
                    let mutable j = 0

                    if fit.Intercept then
                        x[0] <- 1.0
                        j <- 1

                    for (c, cells) in terms do
                        if Option.isNone failure then
                            match c with
                            | NumericCoding(name, _) ->
                                match fitFloat cells[i] with
                                | Some v when finite v -> x[j] <- v
                                | _ -> failure <- Some(predictRowError i ("the term '" + name + "' is not finite"))

                                j <- j + 1
                            | CategoricalCoding(name, levels, coded) ->
                                let token = levelText cells[i]

                                if not (Array.contains token levels) then
                                    failure <-
                                        Some(
                                            predictRowError
                                                i
                                                ("the term '"
                                                 + name
                                                 + "' has the level '"
                                                 + token
                                                 + "', which the fit's rows did not hold")
                                        )
                                else
                                    for level in coded do
                                        x[j] <- (if level = token then 1.0 else 0.0)
                                        j <- j + 1

                    let off =
                        match offset with
                        | Some cells ->
                            match fitFloat cells[i] with
                            | Some v when finite v -> v
                            | _ ->
                                if Option.isNone failure then
                                    failure <- Some(predictRowError i "the offset is not finite")

                                0.0
                        | None -> 0.0

                    if Option.isNone failure then
                        rows[i] <- Some(x, off)

                i <- i + 1

            match failure with
            | Some e -> Error e
            | None -> Ok rows)

    /// The group each input row is scored by: its key cells looked up among the model's groups, a key
    /// the model has no group for refused by name. Keys are matched by the token `GroupBy` groups on.
    let private predictGroups
        (f: Frame)
        (groupBy: string list)
        (groups: Cell[][])
        (rows: (float[] * float) option[])
        : Result<int[], EvalError> =
        let index =
            groups
            |> Array.mapi (fun g cells -> rowTokenStringOfArray cells, g)
            |> Map.ofArray

        groupBy
        |> List.map (fun key ->
            match colIndex f.Cols key with
            | Some ci -> Ok ci
            | None -> Error(UnknownColumn(key, available f.Cols)))
        |> sequenceR
        |> Result.bind (fun keyIdx ->
            let phys = Frame.physical f
            let out = Array.create rows.Length (-1)
            let mutable failure = None
            let mutable i = 0

            while Option.isNone failure && i < rows.Length do
                if Option.isSome rows[i] then
                    let cells =
                        keyIdx |> List.map (fun ci -> Vec.cellAt f.Vecs[ci] phys[i]) |> List.toArray

                    match Map.tryFind (rowTokenStringOfArray cells) index with
                    | Some g -> out[i] <- g
                    | None -> failure <- Some(predictRowError i ("the model has no group " + keyText cells))

                i <- i + 1

            match failure with
            | Some e -> Error e
            | None -> Ok out)

    /// `η = Σⱼ xⱼ·βⱼ + offset`, the columns `j` ascending from `+0` and the offset added last, an aliased
    /// column contributing nothing: the order a `Fit`'s fitted value is formed in, so a training row's
    /// prediction is its fitted value bit for bit (D56 item 3).
    let private linearPredictor (beta: float option[]) (x: float[]) (off: float) : float =
        let mutable acc = 0.0

        for j in 0 .. beta.Length - 1 do
            match beta[j] with
            | Some b -> acc <- acc + x[j] * b
            | None -> ()

        acc + off

    /// The quadratic form `x₀ᵀ(RᵀR)⁻¹x₀` over the kept columns (D56 item 4): `z` with `Rᵀz = x₀` by
    /// `UpperTriangular.solveUpperT`, then `Σ zᵢ²` left to right.
    let private quadraticForm (ls: LeastSquaresFit) (x: float[]) : Result<float, EvalError> =
        let kept = ls.Kept |> List.toArray
        let b = Vector.adopt (kept |> Array.map (fun j -> x[j]))

        LinearAlgebra.UpperTriangular.solveUpperT ls.R b
        |> Result.mapError (fun e -> predictError (linAlgText e))
        |> Result.map (fun z ->
            let mutable q = 0.0

            for v in Vector.toArray z do
                q <- q + v * v

            q)

    /// Append the prediction's columns to `f`, each a float column stated in its unit.
    let private appendPredicted (f: Frame) (columns: (string * Cell[] * UnitOfMeasure option) list) : Frame =
        let phys = Frame.physical f

        columns
        |> List.fold
            (fun acc (name, cells, unit) ->
                let v = Vec.packAt FloatType f.Count (fun i -> phys[i]) cells
                Frame.appendColumn acc name FloatType v |> unitised name unit)
            f

    /// A response-scale prediction's refusal (Phase 600, D58 item 7): a linear predictor the link has no
    /// mean for under the family, a negative one under a positive family's identity link, say.
    let private predictOutOfRange (row: int) : EvalError =
        predictRowError row "the linear predictor has no mean under the family and link"

    /// `Predict` against the training rows (D56): the fit of every group, exactly as `Fit` makes it, then
    /// every input row coded, matched to its group and scored, with its standard errors and interval.
    let private predictFromTraining
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (spec: PredictSpec)
        (training: Frame)
        (interval: (IntervalKind * float) option)
        : Result<Frame, EvalError> =
        // The training design is the fit's; the output it names is not read.
        let fit =
            { spec.Fit with
                Output = FitOutput.Coefficients }

        fitResolve env fit
        |> Result.bind (fun resolved ->
            fitDesign k env training fit
            |> Result.bind (fun d ->
                d.Members
                |> Array.toList
                |> List.map (fun members ->
                    fitOneGroup d resolved fit members (QRAccumulator.empty d.P) 0 |> Result.map snd)
                |> sequenceR
                |> Result.bind (fun fits ->
                    predictDesign k env f fit d.Coding d.P
                    |> Result.map (fun rows -> List.toArray fits, rows))
                |> Result.bind (fun (fits, rows) ->
                    predictGroups f fit.GroupBy d.KeyCells rows
                    |> Result.bind (fun groupOf ->
                        let n = rows.Length
                        let predicted = Array.create n Null
                        let seFit = Array.create n Null
                        let sePred = Array.create n Null
                        let lower = Array.create n Null
                        let upper = Array.create n Null
                        let gaussian = fitIsOls fit
                        let family = glmFamily fit.Family
                        let link = glmLink fit.Link
                        let response = spec.Scale = PredictScale.Response
                        let mutable failure = None
                        let mutable i = 0

                        while Option.isNone failure && i < n do
                            match rows[i] with
                            | None -> ()
                            | Some(x, off) ->
                                let fg = fits[groupOf[i]]
                                let eta = linearPredictor fg.Estimates x off

                                if response && not (Glm.validPredictor family link eta) then
                                    failure <- Some(predictOutOfRange i)
                                else
                                    // On the response scale the mean is `g⁻¹(η)` and a standard error
                                    // `|dμ/dη|` times the link scale's (D58 item 7): `1` under the
                                    // identity link, so least squares is unchanged bit for bit.
                                    let slope = if response then abs (Glm.meanDerivative link eta) else 1.0

                                    predicted[i] <- Float(if response then Glm.inverseLink link eta else eta)

                                    match fg.Dispersion with
                                    | None -> ()
                                    | Some phi ->
                                        match quadraticForm fg.Ls x with
                                        | Error e -> failure <- Some e
                                        | Ok q ->
                                            let sf = sqrt (phi * q)
                                            let sp = sqrt (phi * (1.0 + q))
                                            seFit[i] <- Float(sf * slope)

                                            if gaussian then
                                                sePred[i] <- Float(sp * slope)

                                            match interval with
                                            | None -> ()
                                            | Some(kind, level) ->
                                                // A dispersion the family fixes reads the normal row; an
                                                // estimated one the t at the residual df.
                                                let df = if fitFixedDispersion fit.Family then None else Some fg.Df

                                                match StudentT.quantile level df with
                                                | None -> ()
                                                | Some t ->
                                                    let se =
                                                        match kind with
                                                        | IntervalKind.Confidence -> sf
                                                        | IntervalKind.Prediction -> sp

                                                    let a = eta - t * se
                                                    let b = eta + t * se

                                                    // An interval's ends are mapped through `g⁻¹`, held
                                                    // inside the mean's range; an end with no finite
                                                    // mean is null.
                                                    let lo, hi =
                                                        if response then
                                                            Glm.responseInterval family link eta a b
                                                        else
                                                            Some a, Some b

                                                    let cell (v: float option) =
                                                        match v with
                                                        | Some v -> Float v
                                                        | None -> Null

                                                    lower[i] <- cell lo
                                                    upper[i] <- cell hi

                            i <- i + 1

                        match failure with
                        | Some e -> Error e
                        | None ->
                            let units = predictUnits spec d.YUnit

                            let bounds =
                                match interval with
                                | Some _ -> [ "lower", lower, units.Predicted; "upper", upper, units.Predicted ]
                                | None -> []

                            Ok(
                                appendPredicted
                                    f
                                    ([ "predicted", predicted, units.Predicted
                                       "se_fit", seFit, units.StdError
                                       "se_pred", sePred, units.StdError ]
                                     @ bounds)
                            )))))

    /// `Predict` against a coefficient table (D56 item 6): the table's estimates per group and term, every
    /// term of the spec present and every term of the table one of the spec's; numeric terms and the
    /// intercept only, since the table records no categorical term's reference level.
    let private predictFromCoefficients
        (k: KernelSet)
        (env: Map<string, Cell>)
        (f: Frame)
        (spec: PredictSpec)
        (table: Frame)
        : Result<Frame, EvalError> =
        let fit = spec.Fit

        let need (name: string) (ok: ColumnType -> bool) (what: string) =
            match colType table.Cols name with
            | None -> Error(UnknownColumn(name, available table.Cols))
            | Some ty when ok ty -> Ok()
            | Some ty ->
                Error(
                    TypeError(
                        "predict: the coefficient table's '"
                        + name
                        + "' is a "
                        + ColumnType.tag ty
                        + " column; "
                        + what
                    )
                )

        let numeric (ty: ColumnType) =
            match ty with
            | IntType
            | FloatType
            | DecimalType -> true
            | _ -> false

        fitResolve env fit
        |> Result.bind (fun _ ->
            (fit.GroupBy |> List.map (fun key -> need key (fun _ -> true) ""))
            @ [ need "term" (fun ty -> ty = StringType) "a term is a string"
                need "estimate" numeric "an estimate is numeric" ]
            |> sequenceR)
        |> Result.bind (fun _ ->
            // Each term is evaluated once over the input to learn its kind; a categorical one is refused.
            fit.Predictors
            |> List.map (fun (name, e) ->
                fitColumn k env f ("predict term '" + name + "'") e
                |> Result.bind (fun (_, ty, unit) ->
                    if numeric ty then
                        Ok(NumericCoding(name, unit))
                    elif ty = StringType || ty = BoolType then
                        Error(
                            predictError (
                                "the term '"
                                + name
                                + "' is categorical; a coefficient table does not record its reference level"
                            )
                        )
                    else
                        Error(
                            TypeError(
                                "predict: the term '"
                                + name
                                + "' is a "
                                + ColumnType.tag ty
                                + " expression; a term is numeric, string or boolean"
                            )
                        )))
            |> sequenceR)
        |> Result.bind (fun coding ->
            let names =
                (if fit.Intercept then [ interceptTerm ] else [])
                @ (fit.Predictors |> List.map fst)
                |> List.toArray

            let p = names.Length
            let tphys = Frame.physical table

            let cellOf (name: string) (r: int) =
                let ci = colIndex table.Cols name |> Option.get
                Vec.cellAt table.Vecs[ci] tphys[r]

            // Per group token: its key cells and its estimates by term, in the table's order.
            let groups =
                System.Collections.Generic.Dictionary<string, Cell[] * Map<string, float option>>()

            let order = ResizeArray<string>()
            let mutable failure = None
            let mutable r = 0

            while Option.isNone failure && r < tphys.Length do
                let keys = fit.GroupBy |> List.map (fun key -> cellOf key r) |> List.toArray
                let token = rowTokenStringOfArray keys

                let term =
                    match cellOf "term" r with
                    | Str t -> Some t
                    | _ -> None

                match term with
                | None -> failure <- Some(predictError ("the coefficient table's row " + string r + " names no term"))
                | Some term when not (Array.contains term names) ->
                    failure <-
                        Some(predictError ("the coefficient table's term '" + term + "' is not a term of the model"))
                | Some term ->
                    let estimate = fitFloat (cellOf "estimate" r)

                    let keysNow, terms =
                        match groups.TryGetValue token with
                        | true, v -> v
                        | _ ->
                            order.Add token
                            keys, Map.empty

                    if Map.containsKey term terms then
                        failure <-
                            Some(
                                predictError (
                                    "the coefficient table names the term '"
                                    + term
                                    + "' twice for the group "
                                    + keyText keys
                                )
                            )
                    else
                        groups[token] <- (keysNow, Map.add term estimate terms)

                r <- r + 1

            match failure with
            | Some e -> Error e
            | None ->
                let keyCells = order |> Seq.map (fun t -> fst groups[t]) |> Seq.toArray

                let betas =
                    order
                    |> Seq.map (fun t ->
                        let keys, terms = groups[t]

                        match names |> Array.tryFind (fun nm -> not (Map.containsKey nm terms)) with
                        | Some missing ->
                            Error(
                                predictError (
                                    "the coefficient table has no term '"
                                    + missing
                                    + "' for the group "
                                    + keyText keys
                                )
                            )
                        | None -> Ok(names |> Array.map (fun nm -> terms[nm])))
                    |> Seq.toList
                    |> sequenceR

                betas
                |> Result.bind (fun betas ->
                    let betas = List.toArray betas

                    predictDesign k env f fit coding p
                    |> Result.bind (fun rows ->
                        predictGroups f fit.GroupBy keyCells rows
                        |> Result.bind (fun groupOf ->
                            let family = glmFamily fit.Family
                            let link = glmLink fit.Link
                            let predicted = Array.create rows.Length Null
                            let mutable failure = None
                            let mutable i = 0

                            while Option.isNone failure && i < rows.Length do
                                match rows[i] with
                                | None -> ()
                                | Some(x, off) ->
                                    let eta = linearPredictor betas[groupOf[i]] x off

                                    match spec.Scale with
                                    | PredictScale.Link -> predicted[i] <- Float eta
                                    | PredictScale.Response ->
                                        if Glm.validPredictor family link eta then
                                            predicted[i] <- Float(Glm.inverseLink link eta)
                                        else
                                            failure <- Some(predictOutOfRange i)

                                i <- i + 1

                            match failure with
                            | Some e -> Error e
                            | None -> Ok(appendPredicted f [ "predicted", predicted, None ])))))

    /// `Predict` over the frame (Phase 597, D56): the appended names checked free, the interval
    /// resolved, the model source read, then the rows scored.
    let private evalPredict
        (k: KernelSet)
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (f: Frame)
        (spec: PredictSpec)
        : Result<Frame, EvalError> =
        match predictColumns spec |> List.tryFind (fun c -> Option.isSome (colIndex f.Cols c)) with
        | Some c -> Error(predictError ("the input already has a column '" + c + "'"))
        // Phase 598 (D57): a prediction's standard errors are the classical ones read off `R`; a model
        // whose spec names a sandwich is refused rather than answered with errors it did not ask for.
        | None when spec.Fit.StdErrors <> FitStdErrors.Classical ->
            Error(predictError "the model's standard errors are robust; predict states classical errors only")
        | None ->
            predictInterval env spec
            |> Result.bind (fun interval ->
                match spec.Model with
                | PredictModel.Training source ->
                    evalSource resolve source
                    |> Result.bind (fun t -> predictFromTraining k env f spec (Frame.ofTable t) interval)
                | PredictModel.Coefficients source ->
                    evalSource resolve source
                    |> Result.bind (fun t -> predictFromCoefficients k env f spec (Frame.ofTable t)))

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
        | GroupBy(keys, aggs) -> evalGroupBy k f keys aggs
        // `0.23.0` — resolve the scalar slots against the SAME env `ColExpr.Param` reads, then call
        // the unchanged reference primitives. `evalSort` / `evalLimit` still take resolved values,
        // so the pinned ordering and the pinned window semantics have exactly one definition and
        // the slot is a resolution step in front of them, not a second evaluator.
        | Sort by ->
            by
            |> List.map (fun (c, d) -> resolveStrSlot env "sort key column" c |> Result.map (fun c -> c, d))
            |> sequenceR
            |> Result.bind (knownSortKeys f)
            |> Result.map (evalSort k f)
        | Distinct -> Ok(evalDistinct f)
        | Limit(n, offset) ->
            resolveIntSlot env "limit n" n
            |> Result.bind (fun n ->
                resolveIntSlot env "limit offset" offset
                |> Result.map (fun offset -> evalLimit f n offset))
        | Window spec -> evalWindow k f spec
        | Pivot spec -> evalPivot f spec
        | Unpivot(idVars, valueVars) -> evalUnpivot f idVars valueVars
        | Join(right, on, how) ->
            evalSource resolve right
            |> Result.bind (fun t -> evalJoin k f (Frame.ofTable t) on how)
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
        // Phase 596: a definition changes no frame; `FnDef.expand` has removed it before any fold.
        | Define _ -> Ok f
        // Phase 592: one factorization per group (D53).
        | Fit spec -> evalFit k env f spec
        // Phase 597: the model, then every input row scored against its group's fit (D56).
        | Predict spec -> evalPredict k resolve env f spec

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

    /// One move of the fold over a non-empty `steps` (Phase 376: lifted out of
    /// `evalPreparedCountedFolding` so the asynchronous entry point folds through the same moves): the
    /// step at the head — or, `fused`, a `Sort` followed by a `Limit` as one — over `f`, answering the
    /// frame, the row evaluations it cost and the steps left.
    let private foldHead
        (k: KernelSet)
        (fused: bool)
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (f: Frame)
        (steps: Transform list)
        : Result<Frame * int * Transform list, EvalError> =
        match steps with
        | [] -> Ok(f, 0, [])
        // Phase 269 — the fusion the planner names as `TopN`: a `Sort` followed by a `Limit`
        // runs as the stable top-n kernel. The slots resolve through the same resolvers, in the
        // order the two steps would have resolved them, so the first error is the same one.
        // Neither step is charged an evaluation, as neither was.
        | Sort by :: Limit(n, offset) :: rest when fused ->
            by
            |> List.map (fun (c, d) -> resolveStrSlot env "sort key column" c |> Result.map (fun c -> c, d))
            |> sequenceR
            |> Result.bind (knownSortKeys f)
            |> Result.bind (fun keys ->
                resolveIntSlot env "limit n" n
                |> Result.bind (fun n ->
                    resolveIntSlot env "limit offset" offset
                    |> Result.map (fun offset -> evalTopN k f keys n offset, 0, rest)))
        | step :: rest ->
            let cost =
                match step with
                | Filter _
                | Derive _
                // Phase 592: a fit evaluates its terms at every input row, once per step.
                | Fit _
                // Phase 597: and so does a prediction (its training rows are another table's).
                | Predict _ -> Frame.rows f
                | _ -> 0

            evalStepWith k resolve env f step |> Result.map (fun f' -> f', cost, rest)

    /// The reference evaluator over a prepared source, reporting alongside its answer how many row
    /// evaluations at steps it cost (Phase 267) — the one driver every entry point folds through;
    /// see `evalPipelineWithInEnvCounted` for what the count means. `k` is the kernel set the
    /// row-local verbs run through (Phase 270); `fused` says whether a `Sort` followed by a `Limit`
    /// runs as the stable top-n kernel (Phase 269) or as the two steps written. The pipeline is
    /// folded AS GIVEN: the planning is the callers' (`evalPreparedCountedWith`, which every entry
    /// point reaches, plans; `evalPreparedCountedAsWritten` does not). It answers the FRAME the fold
    /// ended on (Phase 342): the boundary out is the caller's — `Frame.toTable` for every entry point
    /// that answers a `Table`, `Prepared.ofFrame` for `evalToPrepared`, which keeps it.
    let private evalPreparedCountedFolding
        (k: KernelSet)
        (fused: bool)
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<Frame * int, EvalError> =
        let rec go f evaluated steps =
            match steps with
            | [] -> Ok(f, evaluated)
            | _ ->
                match foldHead k fused resolve env f steps with
                | Ok(f', cost, rest) -> go f' (evaluated + cost) rest
                | Error e -> Error e

        // Phase 596: the pipeline's functions are registered and expanded before the first step, so
        // a refused definition is the answer and what folds holds no call (a no-op without them).
        FnDef.expand pipeline |> Result.bind (go prepared.Frame.Value 0)

    /// The reference evaluator over a prepared source through the kernel set `k` (Phase 270),
    /// reporting alongside its answer how many row evaluations at steps it cost (Phase 267) — the
    /// one driver every entry point folds through; see `evalPipelineWithInEnvCounted` for what the
    /// count means. Since Phase 269 the pipeline is PLANNED before it is folded (`Planner.rewrite`
    /// over the source's schema) and the `Sort` > `Limit` pair is run as one kernel: the answer is
    /// the reference's, errors included (`Conformance.plannerLaws`), and the count is the planned
    /// walk's, which is the honest one. Internal so the suite can run both members of the kernel
    /// pair on one host and hold their answers equal; every entry point passes `Kernels.host`.
    /// This one answers the frame the planned fold ended on (Phase 342); `evalPreparedCountedWith`
    /// is it with the boundary out paid.
    let private evalPreparedFrameCountedWith
        (k: KernelSet)
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<Frame * int, EvalError> =
        let frame = prepared.Frame.Value
        evalPreparedCountedFolding k true resolve env (Planner.rewrite frame.Cols pipeline) prepared

    /// `evalPreparedFrameCountedWith` with the boundary out paid: the answer as a `Table`.
    let internal evalPreparedCountedWith
        (k: KernelSet)
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<Table * int, EvalError> =
        evalPreparedFrameCountedWith k resolve env pipeline prepared
        |> Result.map (fun (f, n) -> Frame.toTable f, n)

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
        |> Result.map (fun (f, n) -> Frame.toTable f, n)

    /// Prepare a table once for many evaluations (Phase 267): the `Table` boundary — one typed
    /// unpack per column — paid here rather than by every pipeline that reads the source. The table
    /// is held by reference beside its prepared form and never copied; a consumer that evaluates
    /// many pipelines over one source (a sheet, a dashboard) prepares it once and hands the result
    /// to `evalPrepared` per pipeline, or to `Incremental.primePrepared`. Since Phase 268 a prepared
    /// source is also a persistent VERSION: `ColumnOps.applyPrepared` edits it at the cost of a
    /// chunk, sharing everything an edit did not touch, and `toTable` reads any version back.
    /// A table's columns are read by NAME, as Core's `Table` addresses them; a table Core's
    /// `Table.validate` admits names each column once. A source whose columns are identified by
    /// position — rows from a reader, or a split pipeline's prefix — enters through `prepareRows`.
    let prepare (t: Table) : Prepared = Prepared.ofTable t

    /// The source seam (Phase 415, `DECISIONS.md` D25): a source handed to the evaluator as a
    /// schema and its rows, read by POSITION. Cell `c` of every row is column `c` of `schema`, so a
    /// schema that names a column twice keeps both, as the evaluator's own frame does; nothing is
    /// looked up by name on the way in. It is the door for a source that is not a `Table`: the
    /// rows a statement returned, a cached or checkpointed result, the prefix of a pipeline run
    /// somewhere else. The rows are read once, in order, and not retained; each column is packed
    /// under its field's type exactly as the evaluator packs the rows a verb emits, so a cell the
    /// type cannot hold is carried as the `Table` door carries one. A row whose width is not the
    /// schema's is refused with `TypeError`, naming the row. No IO and nothing asynchronous: the
    /// reader is whatever sequence the host hands in. The answer is a `Prepared`, which every
    /// evaluator entry point over a prepared source, `ColumnOps` and the incremental seam take.
    let prepareRows (schema: Schema) (rows: seq<Cell[]>) : Result<Prepared, EvalError> =
        let all = Seq.toArray rows
        let width = List.length schema

        match all |> Array.tryFindIndex (fun r -> isNull r || r.Length <> width) with
        | Some i ->
            let got = if isNull all[i] then 0 else all[i].Length

            Error(
                TypeError(
                    "source row "
                    + string i
                    + ": "
                    + string got
                    + " cells, schema width "
                    + string width
                )
            )
        | None -> Ok(Prepared.ofFrame (Frame.ofRows schema all))

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

    /// The reference evaluator over a prepared source, answering a prepared source (Phase 342):
    /// `evalPrepared` with the boundary out NOT paid. The same resolver, env, pipeline and source,
    /// the same errors, and a result whose table — the very cells `evalPrepared` answers — is built
    /// only if `toTable` is called. So a consumer that feeds one pipeline's answer to the next (a
    /// sheet's nodes, chained dashboard bindings) hands the result straight back to `evalPrepared`,
    /// `evalToPrepared`, the `ColumnOps` forms or `Incremental.primePrepared`, and pays the `Table`
    /// boundary once at each end of the chain rather than out and back in at every hop. Where the
    /// final step left a selection (a `Filter`, a `Sort`, a `Limit`), the result is gathered dense
    /// first, so it holds its own rows and no more.
    let evalToPrepared
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Result<Prepared, EvalError> =
        evalPreparedFrameCountedWith Kernels.host resolve env pipeline prepared
        |> Result.map (fst >> Prepared.ofFrame)

    /// The worker pool as the evaluator reaches it (Phase 376): for a row-local step over a frame,
    /// `Some` work that evaluates it on the pool, or `None` to run it as `evalToPrepared` runs it.
    /// An interface rather than a mutable function value, because Fable may store a curried function
    /// value in one arity and call it in another.
    type internal PoolHook =
        abstract Step: env: Map<string, Cell> * f: Frame * t: Transform -> Async<Result<Frame, EvalError>> option

    /// The opted-in pool: set by `WorkerPool.optIn` and cleared by `WorkerPool.optOut`, which are
    /// defined after this module because the pool hands the step off as data (`MorselHandOff`).
    let mutable internal pool: PoolHook option = None

    /// The pool's work for `t` over `f`, or `None` — always `None` until a host opts in.
    let internal pooledStep
        (env: Map<string, Cell>)
        (f: Frame)
        (t: Transform)
        : Async<Result<Frame, EvalError>> option =
        match pool with
        | Some hook -> hook.Step(env, f, t)
        | None -> None

    /// `evalToPrepared`, answered asynchronously (Phase 376): the JavaScript host's ONLY route to the
    /// worker pool. The same resolver, env, pipeline and source, the same answer and the same errors,
    /// byte for byte, on every host. The pipeline is planned and folded as `evalToPrepared` folds it;
    /// where a host has opted in (`WorkerPool.optIn`) and shared memory exists, each `Filter` or
    /// `Derive` the pool takes runs across the pool's workers with the caller draining morsels beside
    /// them, and every other step runs on the caller's thread as it always ran. A host that has not
    /// opted in, or a page without cross-origin isolation, gets `evalToPrepared`'s answer through the
    /// same moves. `evalToPrepared` and the synchronous kernels are unchanged and never reach the pool.
    ///
    /// Two entry shapes on the JavaScript host. From the PAGE'S thread, call this and await it: the
    /// page thread drains morsels and then yields until the workers report, so it never blocks on
    /// `Atomics.wait` (which a page's thread refuses). From a DEDICATED WORKER that hosts the whole
    /// evaluator, call it the same way, with the pool's workers started from that worker (a worker
    /// may start workers); the page then stays free while the evaluator works. On .NET the native
    /// kernels already run morsels across threads, and a host has no reason to opt in.
    let evalToPreparedAsync
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipeline: Transform list)
        (prepared: Prepared)
        : Async<Result<Prepared, EvalError>> =
        let rec go (f: Frame) (steps: Transform list) : Async<Result<Frame, EvalError>> =
            async {
                match steps with
                | [] -> return Ok f
                | t :: rest ->
                    let pooled =
                        match t with
                        | Filter _
                        | Derive _ -> pooledStep env f t
                        | _ -> None

                    match pooled with
                    | Some work ->
                        match! work with
                        | Ok f' -> return! go f' rest
                        | Error e -> return Error e
                    | None ->
                        match foldHead Kernels.host true resolve env f steps with
                        | Ok(f', _, rest') -> return! go f' rest'
                        | Error e -> return Error e
            }

        async {
            let frame = prepared.Frame.Value

            // Phase 596: registered and expanded first, as `evalToPrepared` folds it.
            match FnDef.expand pipeline with
            | Error e -> return Error e
            | Ok expanded ->
                let! answer = go frame (Planner.rewrite frame.Cols expanded)
                return answer |> Result.map Prepared.ofFrame
        }

    /// `evalManyToPrepared` through the kernel set `k` (Phase 343). Internal so the suite can run
    /// the batch through every member, and through a native member that runs alongside at any
    /// work, and hold the answers equal; the entry point passes `Kernels.host`.
    let internal evalManyToPreparedWith
        (k: KernelSet)
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipelines: Transform list list)
        (prepared: Prepared)
        : Result<Prepared, EvalError> list =
        let batch = Array.ofList pipelines
        let n = batch.Length
        // The boundary in, paid once on the caller's thread before any pipeline runs, so the
        // pipelines share one frame rather than wait on one another for it.
        let rows = Frame.rows prepared.Frame.Value
        let work = int (min (int64 System.Int32.MaxValue) (int64 n * int64 rows))
        let results: Result<Prepared, EvalError>[] = Array.zeroCreate n

        k.RunPipelines n work (fun alongside i ->
            let set = if alongside then Kernels.oneThread else k

            results[i] <-
                evalPreparedFrameCountedWith set resolve env batch[i] prepared
                |> Result.map (fst >> Prepared.ofFrame))

        List.ofArray results

    /// Independent pipelines over ONE prepared source, each answered as `evalToPrepared` answers it
    /// (Phase 343): the results in list order, every one equal to `evalToPrepared resolve env p
    /// prepared` for its pipeline `p`, refusals included and each pipeline's own. A dashboard's
    /// bindings and a sheet's same-level nodes are this shape: each is a pure function of the
    /// shared, immutable source, so on .NET the batch runs on the thread pool once there is enough
    /// work between them (1,000 rows, pipelines times source rows), and the answers are the sequential answers
    /// whatever the schedule. A pipeline running alongside others fans out no morsels or partitions
    /// of its own, so the batch occupies at most one thread per logical processor. Under Fable the
    /// pipelines run one after another. `resolve` may be called from several threads at once on
    /// .NET; it must be safe to, as a lookup in an immutable map is.
    let evalManyToPrepared
        (resolve: string -> Result<Table, EvalError>)
        (env: Map<string, Cell>)
        (pipelines: Transform list list)
        (prepared: Prepared)
        : Result<Prepared, EvalError> list =
        evalManyToPreparedWith Kernels.host resolve env pipelines prepared

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

    /// The unit of an aggregate's column over a source column of unit `source` (Phase 429, D29):
    /// `Count` and `CountDistinct` carry none; `Sum`, `Mean`, `Min`, `Max`, `Median`, `First`,
    /// `Last` and `StdDev` keep the source's.
    let aggregateUnit (fn: AggFn) (source: UnitOfMeasure option) : UnitOfMeasure option = aggregateUnitOf fn source

    /// The unit of a window function's column over a source column of unit `source` (Phase 429,
    /// D29): the positional and ranking functions carry none; `Lag`, `Lead`, `CumulSum`, `CumulMax`,
    /// `CumulMin`, `RollingSum` and `RollingMean` keep the source's.
    let windowUnit (fn: WindowFn) (source: UnitOfMeasure option) : UnitOfMeasure option = windowUnitOf fn source

    /// The unit an unpivot's `value` column carries over `valueVars` of `cols` (Phase 429, D29):
    /// the one unit they all state, none otherwise.
    let unpivotUnit (cols: Schema) (valueVars: string list) : UnitOfMeasure option = sharedUnit cols valueVars

    /// The unit check over an expression (Phase 429, `DECISIONS.md` D29), decided over the schema
    /// alone: the expression's unit — `None` where it states none — and the expression with every
    /// `Convert` resolved into the cast and the multiplication that perform it, or the
    /// `UnitMismatch` naming `step`, the expression and both units. This is the check every
    /// `Filter` and `Derive` runs before its first row; a reader (the schema walk, a planner, a
    /// registered query) asks it the same question.
    let resolveUnits (step: string) (cols: Schema) (e: ColExpr) : Result<UnitOfMeasure option * ColExpr, EvalError> =
        resolveUnitsIn step cols e

    /// The unit of an expression's values over a schema (Phase 429) — `Ok None` where it states
    /// none, the mismatch where the schema refuses it. The unit twin of `typeOf`.
    let unitOf (cols: Schema) (e: ColExpr) : Result<UnitOfMeasure option, EvalError> = unitOfExpr cols e

    /// The unit a schema states for a column, where it states one.
    let unitOfColumn (cols: Schema) (name: string) : UnitOfMeasure option = colUnit cols name

    /// The column type a `Derive`d column takes from its computed cells WHERE ONLY THE CELLS DECIDE
    /// IT: the present cells' types joined under `ColumnType.widens` in row order (Phase 321; it was
    /// the first present cell's type), `StringType` when every cell is null. Since Phase 338 a
    /// derive the typer decides takes the typer's type on every frame instead (`SchemaWalk` states
    /// it), and only a derive reading a `Param`, a `Now`, an unknown column or a join the exact
    /// typer keeps apart is typed by this fold (which, there, also refuses a float beside a
    /// decimal). Exposed because such a type is a function of the WHOLE column, not of one row.
    let inferCellType (cells: Cell list) : ColumnType = inferType cells

    /// The reference `Sort`'s row comparator: the pinned ordering (multi-key, nulls last regardless
    /// of direction). The reference `Sort` refuses a key the schema does not carry before it orders
    /// anything (Phase 412, `DECISIONS.md` D21), so it never compares under one; this comparator,
    /// which has no error to answer, drops such a key, and a caller reproducing the reference refuses
    /// it first, as the reference does. Exposed for the same reason as the four above — an
    /// incremental evaluator that merged rows into a cached order under its OWN comparator would
    /// agree with the reference on every corpus anyone thought to write and disagree on the first
    /// null and the first tie. `List.sortWith` over it is `evalSort`.
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
            cols
            @ [ Field.create spec.As ty |> stated (windowUnitOf spec.Fn (colUnit cols spec.Of)) ],
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

    /// The output names a combining join (`Inner` / `Left` / `Right` / `Outer`) gives the right
    /// side's columns, in right column order, over the left's and the right's column names (Phase
    /// 411, `DECISIONS.md` D20): a right name no left column carries keeps its name; a colliding one
    /// takes `_right`, then `_right_2`, `_right_3`, … — the first that is free against every name the
    /// output carries. The evaluator and the schema walk both read it, so the evaluated and the static
    /// names cannot disagree, and a join adds no repeated name to its output.
    let joinRightNames (leftNames: string list) (rightNames: string list) : string list =
        joinRightNamesOf leftNames rightNames

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
        | Convert(x, _) -> exprCols x
        | Interpolate(x, _, _, _) -> exprCols x
        | Call(_, xs) -> unionAll (xs |> List.map exprCols)
        | Arg _ -> Set.empty

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
        // Phase 592: the response, the keys, the weights and every column a term or the offset reads.
        | Fit spec ->
            unionAll (
                Set.ofList (spec.Response :: spec.GroupBy @ Option.toList spec.Weights)
                :: (FitSpec.exprs spec |> List.map exprCols)
            )
        // Phase 597: the keys and every column a term or the offset reads; the model's source is
        // another table, as a join's right side is.
        | Predict spec -> unionAll (Set.ofList spec.Fit.GroupBy :: (FitSpec.exprs spec.Fit |> List.map exprCols))
        | Distinct
        | Limit _
        | Union _
        | Intersect _
        | Except _
        | Define _ -> Set.empty

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
                && not (prior.Schema |> List.exists (fun f -> f.Name = c))
            | RowsAppended
            | SchemaChanged _
            | FullChange -> false

        if irrelevant then
            Ok prior
        else
            evalPipeline pipeline changedSource

    /// Render an `EvalError` as a stable human string.
    /// A compact text of an expression for a refusal: operators and functions by name, columns
    /// quoted, literals as their cell text, params as `$name`.
    let rec private exprText (e: ColExpr) : string =
        let args (xs: ColExpr list) =
            String.concat ", " (xs |> List.map exprText)

        match e with
        | Col n -> "'" + n + "'"
        | Lit c -> cellString c
        | Param n -> "$" + n
        | Binary(op, a, b) -> (string op).ToLowerInvariant() + "(" + exprText a + ", " + exprText b + ")"
        | Not x -> "not(" + exprText x + ")"
        | Coalesce xs -> "coalesce(" + args xs + ")"
        | Case(cases, els) ->
            "case("
            + args (cases |> List.collect (fun (w, t) -> [ w; t ]))
            + ", "
            + exprText els
            + ")"
        | Cast(ty, x) -> "cast(" + ColumnType.tag ty + ", " + exprText x + ")"
        | ApplyFn(fn, xs) -> (string fn).ToLowerInvariant() + "(" + args xs + ")"
        | InList(x, items) -> "in(" + exprText x + "; " + args items + ")"
        | IsNull x -> "isNull(" + exprText x + ")"
        | InParam(x, n) -> "in(" + exprText x + "; $" + n + ")"
        | Now g -> "now(" + NowGrain.tag g + ")"
        | Quotient(a, b, _) -> "quotient(" + exprText a + ", " + exprText b + ")"
        | Rounded(x, _) -> "rounded(" + exprText x + ")"
        | Convert(x, u) -> "convert(" + exprText x + ", " + Unit.render u + ")"
        | Interpolate(x, _, _, _) -> "interpolate(" + exprText x + ")"
        | Call(name, xs) -> name + "(" + args xs + ")"
        | Arg a -> "arg(" + a + ")"

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
        | FunctionError(name, detail) -> "function '" + name + "': " + detail
        | RecursiveFunction cycle -> "recursive functions: " + String.concat " -> " cycle
        | ExpansionTooLarge(at, bound) -> "the expansion of " + at + " exceeds " + string bound + " expression nodes"
        | InvalidKnots detail -> "invalid knots: " + detail
        | UnsupportedFit(family, link) ->
            "fit: the family '"
            + family
            + "' under the link '"
            + link
            + "' is not evaluated by this version"
        | FitError detail -> detail
        | UnitMismatch(step, expr, left, right) ->
            "unit mismatch in "
            + step
            + ": "
            + left
            + " against "
            + right
            + " in "
            + exprText expr


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
//    * `Window` APPENDS its output column, it does not upsert. A window whose
//      `As` names an existing column is REFUSED by the evaluator (Phase 450,
//      D28); the walk carries no refusal verdict, as for an unknown key (D26),
//      so it still appends, and a repeated name in a walk's answer marks a
//      pipeline the evaluator refuses rather than a shape anyone receives.
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

/// How exact a column's bytes are across hosts (Phase 557, `DECISIONS.md` D43; D54). `Pinned`: every
/// conformant host computes the same bits, so the cross-host law compares the column byte for byte —
/// every scalar function of the evaluator is pinned since Phase 594, `Exp`, `Ln`, `Log10` and a
/// float-exponent `Pow` included. `HostRounded`: the column's lineage passes through a value its
/// caller declares host-rounded (an input column marked so with `ColumnKnowledge.withExactness`, the
/// seam a registered host function stands on), so two hosts agree within a stated ulp bound and the
/// law compares the column in ulps. Within ONE host a `HostRounded` column is as deterministic as any
/// other: repeated, reordered and incremental evaluation answer the same bytes.
[<RequireQualifiedAccess>]
type Exactness =
    | Pinned
    | HostRounded

/// A discrete decision a host-rounded value reaches (Phase 557, D43 choice (c)): a `Filter`'s
/// predicate, a `GroupBy` / `Join` / `Distinct` / set-verb / pivot key, a `Sort` or window key, a
/// `Round` / `Floor` / `Ceil` / `Rounded`, or a `Cast` to an int or a decimal. Each is ADMITTED and
/// REPORTED, never refused: two hosts a last ulp apart can decide it differently, so the cross-host
/// law promises its tolerance only where the mark reaches no such decision. `Step` is the step's
/// index in the pipeline, `Verb` the decision's kind, and `Operand` the host-rounded column the
/// decision reads — or the name of a call the walk could not expand. Since Phase 594 (D54) no scalar
/// function of the evaluator produces such a value; a pipeline over pinned inputs reports none.
type HostRoundedDecision =
    { Step: int
      Verb: string
      Operand: string }

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
        /// The unit the column carries (Phase 429, `DECISIONS.md` D29): the input field's through the
        /// verbs that keep a column, the unit the check derives for a derive, the source's for an
        /// aggregate or a window that keeps it, none for a count or a rank. `None` is "states none",
        /// never "unknown": a unit is a fact of the schema, so the walk always decides it.
        Unit: UnitOfMeasure option
        /// The field's label, where the verb keeps the field (Phase 429): a column a verb produced
        /// carries none.
        Label: string option
        /// The field's description, on the same rule.
        Description: string option
        /// The field's extension metadata, on the same rule.
        Ext: Map<string, string>
        /// How exact the column's bytes are across hosts (Phase 557, D43; D54): an input field is
        /// `Pinned`; a column a verb computes is `HostRounded` exactly when a column so marked is in
        /// its lineage, and the verbs that keep or combine columns carry the mark along.
        Exactness: Exactness
    }

/// Builders over `ColumnKnowledge` (Phase 429): construct through these, so the next metadata member
/// is an addition here and not a break at every literal.
module ColumnKnowledge =

    /// A column of the given name and type knowledge, stating no metadata — what a verb's own column
    /// is until a rule says otherwise.
    let named (name: string) (ty: ColumnType option) : ColumnKnowledge =
        { Name = name
          Type = ty
          Unit = None
          Label = None
          Description = None
          Ext = Map.empty
          Exactness = Exactness.Pinned }

    /// What a schema's field states: its type, and every metadatum. An input column is pinned: its
    /// bytes are the caller's.
    let ofField (f: Field) : ColumnKnowledge =
        { Name = f.Name
          Type = Some f.Type
          Unit = f.Unit
          Label = f.Label
          Description = f.Description
          Ext = f.Ext
          Exactness = Exactness.Pinned }

    /// The same knowledge under a unit.
    let withUnit (u: UnitOfMeasure option) (c: ColumnKnowledge) : ColumnKnowledge = { c with Unit = u }

    /// The same knowledge under an exactness mark (Phase 557).
    let withExactness (x: Exactness) (c: ColumnKnowledge) : ColumnKnowledge = { c with Exactness = x }

    /// The mark two columns' values combine into: host-rounded where either is.
    let joinExactness (a: Exactness) (b: Exactness) : Exactness =
        match a, b with
        | Exactness.Pinned, Exactness.Pinned -> Exactness.Pinned
        | _ -> Exactness.HostRounded

    /// The same knowledge under another name — a projection's rename, a join's suffix.
    let renamed (name: string) (c: ColumnKnowledge) : ColumnKnowledge = { c with Name = name }

    /// The field this knowledge states, under `ty` where its own type is undecided: the shape a
    /// reader that needs a `Schema` (the typer, the unit check) is handed.
    let toFieldAs (ty: ColumnType) (c: ColumnKnowledge) : Field =
        let f = Field.create c.Name (defaultArg c.Type ty)

        let f =
            match c.Unit with
            | Some u -> Field.withUnit u f
            | None -> f

        let f =
            match c.Label with
            | Some l -> Field.withLabel l f
            | None -> f

        let f =
            match c.Description with
            | Some d -> Field.withDescription d f
            | None -> f

        c.Ext |> Map.fold (fun f k v -> Field.withExt k v f) f

    /// The field this knowledge states, where its type is decided.
    let toField (c: ColumnKnowledge) : Field option =
        c.Type |> Option.map (fun ty -> toFieldAs ty c)

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

    /// The unit a named column states (Phase 429): `None` when the column is absent and when it
    /// states none, as `typeOf` is for a type.
    let unitOf (name: string) (knowledge: SchemaKnowledge) : UnitOfMeasure option =
        columns knowledge |> List.tryFind (fun c -> c.Name = name) |> Option.bind _.Unit

    /// The named column's knowledge, or a column of that name the walk knows nothing about.
    let private known (name: string) (knowledge: SchemaKnowledge) : ColumnKnowledge =
        columns knowledge
        |> List.tryFind (fun c -> c.Name = name)
        |> Option.defaultValue (ColumnKnowledge.named name None)

    /// The schema the unit check reads: every column as a field, an undecided type standing in as
    /// a string — the check reads units, never types.
    let private unitSchema (knowledge: SchemaKnowledge) : Schema =
        columns knowledge |> List.map (ColumnKnowledge.toFieldAs StringType)

    /// The unit a derive's column carries over the input (Phase 429): what the check derives, and
    /// none where it refuses — the walk carries no refusal verdict (D26), so a refused derive reads
    /// as a column that states no unit, and the evaluator is where the refusal is met.
    let private derivedUnit (input: SchemaKnowledge) (expr: ColExpr) : UnitOfMeasure option =
        match DataFrame.unitOf (unitSchema input) expr with
        | Ok u -> u
        | Error _ -> None

    /// The exactness a named column carries (Phase 557, D43): `None` when the column is absent.
    let exactnessOf (name: string) (knowledge: SchemaKnowledge) : Exactness option =
        columns knowledge
        |> List.tryFind (fun c -> c.Name = name)
        |> Option.map _.Exactness

    /// A named column's mark; a column the walk cannot see is read as pinned, because the mark is a
    /// claim about what a host-rounded value reached and an unseen column was reached by none.
    let private markOf (input: SchemaKnowledge) (name: string) : Exactness =
        exactnessOf name input |> Option.defaultValue Exactness.Pinned

    /// The host-rounded operand an expression reads first, in evaluation order: a column so marked,
    /// or the name of a call the walk could not expand. `None` where it reads none. No scalar
    /// function is one since Phase 594 (D54): `Exp`, `Ln`, `Log10` and a float `Pow` are pinned.
    let rec private hostRoundedOperand (input: SchemaKnowledge) (e: ColExpr) : string option =
        let go = hostRoundedOperand input

        let first (xs: ColExpr list) = xs |> List.tryPick go

        match e with
        | Col name ->
            if markOf input name = Exactness.HostRounded then
                Some name
            else
                None
        | Lit _
        | Param _
        | Now _ -> None
        | ApplyFn(_, xs)
        | Coalesce xs -> first xs
        | Binary(_, a, b)
        | Quotient(a, b, _) -> first [ a; b ]
        | Not a
        | IsNull a
        | InParam(a, _)
        | Cast(_, a)
        | Rounded(a, _)
        | Convert(a, _)
        | Interpolate(a, _, _, _) -> go a
        | Case(cases, els) -> first ((cases |> List.collect (fun (w, t) -> [ w; t ])) @ [ els ])
        | InList(a, items) -> first (a :: items)
        // Phase 596: the walk reads the EXPANDED pipeline (`ofPipelineFrom`), so a call is its body
        // by here; one it cannot expand is marked, never read as pinned.
        | Call(name, _) -> Some name
        | Arg _ -> None

    /// The mark a derive's column carries (Phase 557, D43; D54): host-rounded exactly where the
    /// expression reads a column already so marked.
    let internal exprExactness (input: SchemaKnowledge) (expr: ColExpr) : Exactness =
        match hostRoundedOperand input expr with
        | Some _ -> Exactness.HostRounded
        | None -> Exactness.Pinned

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
    /// does. Deliberately not `upsert`: a window whose `As` names an existing column is refused by
    /// the evaluator (Phase 450, D28), and a walk that retyped the column in place would describe
    /// an evaluation that answers `Ok`. Appending leaves the repeated name visible instead.
    let private appendColumn (column: ColumnKnowledge) (knowledge: SchemaKnowledge) : SchemaKnowledge =
        knowledge |> withColumns (columns knowledge @ [ column ])

    let private ofColumns (schema: Schema) : ColumnKnowledge list =
        schema |> List.map ColumnKnowledge.ofField

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

    /// One column of a union, from the left's knowledge and the right's, read through the evaluator's
    /// `unionField` (Phase 469, D36): its type where both sides' types are known and join, unknown
    /// where either is not — a join with an unknown type is no type the walk can name. A pair the
    /// evaluator refuses (two types no column holds, two units) reads as a column of unknown type:
    /// the walk carries no refusal verdict (D26), and the evaluator is where the refusal is met.
    let private unionColumn (left: ColumnKnowledge) (right: ColumnKnowledge) : ColumnKnowledge =
        // The output holds both sides' cells, so it is host-rounded where either side is (Phase 557).
        let mark = ColumnKnowledge.joinExactness left.Exactness right.Exactness

        match ColumnKnowledge.toField left, ColumnKnowledge.toField right with
        | Some l, Some r ->
            match DataFrame.unionField l r with
            | Ok f -> ColumnKnowledge.ofField f |> ColumnKnowledge.withExactness mark
            | Error _ ->
                { left with
                    Type = None
                    Exactness = mark }
        | _ ->
            // The unit is still decided: the unit rule reads units, never types, so both sides
            // stand in under one type for it, as the unit check's schema does.
            let untyped (c: ColumnKnowledge) =
                ColumnKnowledge.toFieldAs StringType { c with Type = None }

            match DataFrame.unionField (untyped left) (untyped right) with
            | Ok f ->
                { left with
                    Type = None
                    Unit = f.Unit
                    Exactness = mark }
            | Error _ ->
                { left with
                    Type = None
                    Exactness = mark }

    /// The output knowledge of ONE transform step over an input knowledge. Total, and evaluates
    /// nothing: every case is a rearrangement of names and declared types.
    let ofTransform (sources: string -> Schema option) (input: SchemaKnowledge) (step: Transform) : SchemaKnowledge =
        match step with
        // Row-set verbs: they drop, reorder or dedup ROWS and touch no column. The two set ops
        // take the LEFT schema through unchanged — the evaluator requires the two column-name lists
        // to agree before it gets here, so a disagreement is an `EvalError`, never a schema.
        | Filter _
        | Sort _
        | Distinct
        | Limit _
        | Intersect _
        | Except _
        // Phase 596: a definition changes no column (the walk reads the expanded pipeline).
        | Define _ -> input

        // A union keeps the left's names and joins each column's type with the right's, through the
        // evaluator's own `unionField` (Phase 469, D36). The right is read as a join reads it.
        | Union source ->
            match input, ofSource sources source with
            // An undeclared right: every name stays (an answer has the left's names), and every
            // type is unknown, because the right's type may widen it.
            | _, SchemaKnowledge.AtLeast _ ->
                input
                |> withColumns (columns input |> List.map (fun c -> { c with Type = None }))
            // Both closed: the evaluator zips the two schemas by position once their names agree,
            // and so does the walk. Names that disagree are the evaluator's refusal; the walk
            // carries no refusal verdict (D26) and answers the input.
            | SchemaKnowledge.Closed left, SchemaKnowledge.Closed right ->
                if List.map _.Name left = List.map _.Name right then
                    SchemaKnowledge.Closed(List.map2 unionColumn left right)
                else
                    input
            // An open left names columns, not positions: each visible column meets the right's
            // first column of its name, which is the one it is zipped with when the names agree.
            | SchemaKnowledge.AtLeast(cols, r), SchemaKnowledge.Closed right ->
                SchemaKnowledge.AtLeast(
                    cols
                    |> List.map (fun c ->
                        match right |> List.tryFind (fun rc -> rc.Name = c.Name) with
                        | Some rc -> unionColumn c rc
                        | None -> c),
                    r
                )

        // Project CLOSES the set however open the input was: the output is exactly the listed
        // columns, under their output names, in the listed order, whatever else the input carried.
        | Project pairs ->
            SchemaKnowledge.Closed(
                pairs
                |> List.map (fun (source, out) -> known source input |> ColumnKnowledge.renamed out)
            )

        // The name is declared; the type is the one the evaluator gives the column (Phase 338): the
        // typer's decided type on every frame, `StringType` for an expression with no present
        // value, and unknown exactly where the cells decide it — a `Param`, a `Now`, a column the
        // walk cannot type, a join of two types the exact typer keeps apart — or the derive is
        // refused. The typer reads the columns whose types are known; one the walk cannot type
        // reads as absent, which types the expression as undecidable rather than as anything false.
        | Derive(name, expr) ->
            let typed = columns input |> List.choose ColumnKnowledge.toField

            upsert
                (ColumnKnowledge.named name (DataFrame.derivedColumnType typed expr)
                 |> ColumnKnowledge.withUnit (derivedUnit input expr)
                 |> ColumnKnowledge.withExactness (exprExactness input expr))
                input

        // GroupBy closes the set too: the key columns then one column per aggregate, and nothing
        // survives that was not named. An aggregate over a host-rounded column is host-rounded,
        // `Count` excepted — presence is decided by the table before any host is called (Phase 557);
        // a `CountDistinct` is a decision over the values, so it keeps the mark.
        | GroupBy(keys, aggs) ->
            SchemaKnowledge.Closed(
                (keys |> List.map (fun key -> known key input))
                @ (aggs
                   |> List.map (fun agg ->
                       ColumnKnowledge.named agg.Name (aggregateType agg.Fn (typeOf agg.Of input))
                       |> ColumnKnowledge.withUnit (DataFrame.aggregateUnit agg.Fn (unitOf agg.Of input))
                       |> ColumnKnowledge.withExactness (
                           match agg.Fn with
                           | Count -> Exactness.Pinned
                           | _ -> markOf input agg.Of
                       )))
            )

        // Phase 592 (D53) — a fit's tables are closed: the keys and a fixed set of statistics, or the
        // input with one appended column. Every statistic is a pinned operation over the rows, so a
        // column is host-rounded only where the response, a key, the weights or a term already is.
        | Fit spec ->
            let mark =
                (spec.Response :: spec.GroupBy @ Option.toList spec.Weights)
                |> List.map (markOf input)
                |> List.append (FitSpec.exprs spec |> List.map (exprExactness input))
                |> List.fold ColumnKnowledge.joinExactness Exactness.Pinned

            let yType = typeOf spec.Response input
            let yUnit = unitOf spec.Response input
            let units = DataFrame.fitStatUnits spec yUnit

            // The type a column stated against the response takes: decimal for a decimal response,
            // float for an int or a float one, unknown where the response's type is.
            let againstY =
                yType
                |> Option.map (fun ty -> if ty = DecimalType then DecimalType else FloatType)

            // A deviance is decimal only where the link keeps it a sum of decimal squares (Phase 595).
            let devianceType = if units.DecimalDeviance then againstY else Some FloatType

            let col (name: string) (ty: ColumnType option) (unit: UnitOfMeasure option) =
                ColumnKnowledge.named name ty
                |> ColumnKnowledge.withUnit unit
                |> ColumnKnowledge.withExactness mark

            let keys = spec.GroupBy |> List.map (fun key -> known key input)

            match spec.Output with
            | FitOutput.Coefficients ->
                SchemaKnowledge.Closed(
                    keys
                    @ [ col "term" (Some StringType) None
                        col "estimate" (Some FloatType) None
                        col "std_error" (Some FloatType) None
                        col "t" (Some FloatType) None
                        col "unit" (Some StringType) None ]
                )
            | FitOutput.Summary ->
                SchemaKnowledge.Closed(
                    keys
                    @ [ col "n" (Some IntType) None
                        col "df" (Some IntType) None
                        col "sigma" (Some FloatType) units.Sigma
                        col "r2" (Some FloatType) None
                        col "rank" (Some IntType) None
                        col "deviance" devianceType units.Deviance
                        col "null_deviance" devianceType units.Deviance
                        col "dispersion" (Some FloatType) units.Dispersion
                        col "iterations" (Some IntType) None
                        col "converged" (Some BoolType) None ]
                    // Phase 600 (D58 item 8): θ in the response's unit, under the negative binomial.
                    @ (if DataFrame.fitStatesTheta spec then
                           [ col "theta" (Some FloatType) yUnit
                             col "theta_std_error" (Some FloatType) yUnit
                             col "alternations" (Some IntType) None ]
                       else
                           [])
                )
            | FitOutput.Fitted
            | FitOutput.Residuals ->
                let name = DataFrame.fitAppendedColumn spec.Output |> Option.get
                input |> appendColumn (col name againstY yUnit)

        // Phase 597 (D56) — a prediction appends closed float columns to its input: `predicted`, and from
        // training rows `se_fit`, `se_pred` and an interval's bounds. Every value is a pinned operation
        // (the t table is read by `Interpolate`, `exp` is `Elementary`'s), so a column is host-rounded
        // only where a term, a key or the training rows it reads already are.
        | Predict spec ->
            let fit = spec.Fit

            let inputMark =
                (fit.GroupBy |> List.map (markOf input))
                @ (FitSpec.exprs fit |> List.map (exprExactness input))

            let col (mark: Exactness) (name: string) (unit: UnitOfMeasure option) =
                ColumnKnowledge.named name (Some FloatType)
                |> ColumnKnowledge.withUnit unit
                |> ColumnKnowledge.withExactness mark

            match spec.Model with
            | PredictModel.Coefficients source ->
                let table = ofSource sources source

                let mark =
                    (markOf table "estimate" :: inputMark)
                    |> List.fold ColumnKnowledge.joinExactness Exactness.Pinned

                input |> appendColumn (col mark "predicted" None)
            | PredictModel.Training source ->
                let training = ofSource sources source

                let mark =
                    ((fit.Response :: fit.GroupBy @ Option.toList fit.Weights)
                     |> List.map (markOf training))
                    @ (FitSpec.exprs fit |> List.map (exprExactness training))
                    @ inputMark
                    |> List.fold ColumnKnowledge.joinExactness Exactness.Pinned

                let units = DataFrame.predictUnits spec (unitOf fit.Response training)

                DataFrame.predictColumns spec
                |> List.fold (fun k name -> appendColumn (col mark name (units.Of name)) k) input

        // A window column's lineage is its source column and its keys (Phase 557): a value window
        // reads the source, and every window's partition and order are decided over the keys.
        | Window spec ->
            let keys = spec.PartitionBy @ (spec.OrderBy |> List.map fst)

            let mark =
                let overKeys =
                    keys
                    |> List.fold (fun m k -> ColumnKnowledge.joinExactness m (markOf input k)) Exactness.Pinned

                match spec.Fn with
                | RowNumber
                | Rank
                | DenseRank
                | CompetitionRank
                | NTile _ -> overKeys
                | _ -> ColumnKnowledge.joinExactness overKeys (markOf input spec.Of)

            input
            |> appendColumn (
                ColumnKnowledge.named spec.As (windowType input spec)
                |> ColumnKnowledge.withUnit (DataFrame.windowUnit spec.Fn (unitOf spec.Of input))
                |> ColumnKnowledge.withExactness mark
            )

        // The index columns are known; the value columns are one per DISTINCT PRESENT VALUE in the
        // `on` column, which is data. Not even their number is derivable, so the set opens here and
        // every later step inherits that.
        | Pivot spec ->
            SchemaKnowledge.AtLeast(
                spec.Index |> List.map (fun name -> known name input),
                "a pivot's value columns are named by the data — one per distinct value in its `on` column"
            )

        | Unpivot(idVars, valueVars) ->
            // The evaluator's rule over the value columns' declared types (Phase 338): decided where
            // every value column's type is known and they join, unknown where one is not known or
            // only the cells decide. The value column's unit is the one every value column states
            // (Phase 429), read off the same knowledge.
            let valueType =
                let types = valueVars |> List.map (fun name -> typeOf name input)

                if types |> List.forall Option.isSome then
                    match DataFrame.unpivotTyping (types |> List.choose id) with
                    | DataFrame.Decided ty -> Some ty
                    | DataFrame.ByCells
                    | DataFrame.Refused -> None
                else
                    None

            // The value column holds every value column's cells, so it is host-rounded where any
            // of them is (Phase 557).
            let valueMark =
                valueVars
                |> List.fold (fun m v -> ColumnKnowledge.joinExactness m (markOf input v)) Exactness.Pinned

            SchemaKnowledge.Closed(
                (idVars |> List.map (fun name -> known name input))
                @ [ ColumnKnowledge.named "variable" (Some StringType)
                    ColumnKnowledge.named "value" valueType
                    |> ColumnKnowledge.withUnit (DataFrame.unpivotUnit (unitSchema input) valueVars)
                    |> ColumnKnowledge.withExactness valueMark ]
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
                // The evaluator suffixes a right column whose name collides with a LEFT one
                // (`DataFrame.joinRightNames`, unique against the whole output). So a right column's
                // OUTPUT name is a function of the left's names — and while any left name is
                // invisible, every right column's name is undecidable between `x`, `x_right` and
                // `x_right_2`. That is why an open left contributes no right columns at all rather
                // than guessing that no collision occurred.
                | SchemaKnowledge.AtLeast(cols, r) ->
                    SchemaKnowledge.AtLeast(
                        cols,
                        r
                        + " — and a join's right-hand output names depend on the left's, so they cannot be named either"
                    )
                | SchemaKnowledge.Closed left ->
                    let right = ofSource sources source
                    let rightCols = columns right

                    let renamed =
                        DataFrame.joinRightNames (left |> List.map _.Name) (rightCols |> List.map _.Name)
                        |> List.map2 (fun c name -> ColumnKnowledge.renamed name c) rightCols

                    match right with
                    | SchemaKnowledge.Closed _ -> SchemaKnowledge.Closed(left @ renamed)
                    // An OPEN right: a colliding right column's suffix is the first one free against
                    // every right name too, and the right's unnamed columns may hold `x_right` — so
                    // only the right columns that keep their own names can be named.
                    | SchemaKnowledge.AtLeast(_, r) ->
                        let leftNames = left |> List.map _.Name |> Set.ofList
                        let kept = rightCols |> List.filter (fun c -> not (Set.contains c.Name leftNames))

                        if kept.Length = rightCols.Length then
                            SchemaKnowledge.AtLeast(left @ kept, r)
                        else
                            SchemaKnowledge.AtLeast(
                                left @ kept,
                                r
                                + " — and a right column colliding with a left name takes a suffix the right's unnamed columns decide, so it cannot be named either"
                            )

    /// The pipeline a static reader walks (Phase 596): expanded where its functions register, as
    /// written where they are refused (the evaluation answers that refusal).
    let private expandedOrWritten (pipeline: Transform list) : Transform list =
        match FnDef.expand pipeline with
        | Ok expanded -> expanded
        | Error _ -> pipeline

    /// Fold a pipeline over knowledge already in hand — the general form, and the one a consumer
    /// that interleaves its own per-step checks with the walk reaches for.
    let ofPipelineFrom
        (sources: string -> Schema option)
        (input: SchemaKnowledge)
        (pipeline: Transform list)
        : SchemaKnowledge =
        // Phase 596: the walk reads the EXPANDED pipeline, so a call's column is typed, unitised and
        // marked exactly as its body's; one whose functions are refused is walked as written.
        expandedOrWritten pipeline |> List.fold (ofTransform sources) input

    /// The output knowledge of a whole pipeline over a concrete input schema. Total. A `Ref` source
    /// inside a `Join` / set op is undeclared here (the result opens, with the reason naming the
    /// unresolved name); a caller that CAN declare them uses `ofPipelineFrom` with `ofMap`.
    let ofPipeline (schema: Schema) (pipeline: Transform list) : SchemaKnowledge =
        ofPipelineFrom noSources (ofSchema schema) pipeline

    // ---- Phase 557 (D43 choice (c)): the discrete decisions a host-rounded value reaches ----

    /// The decisions inside ONE expression at step `i`: a `Round` / `Floor` / `Ceil` / `Rounded`
    /// over a host-rounded operand, and a `Cast` of one to an int or a decimal — each a value two
    /// hosts a last ulp apart can round to different results.
    let rec private decisionsIn (i: int) (input: SchemaKnowledge) (e: ColExpr) : HostRoundedDecision list =
        let go = decisionsIn i input
        let many (xs: ColExpr list) = xs |> List.collect go

        let over (verb: string) (a: ColExpr) =
            match hostRoundedOperand input a with
            | Some operand ->
                [ { Step = i
                    Verb = verb
                    Operand = operand } ]
            | None -> []

        match e with
        | Col _
        | Lit _
        | Param _
        | Now _ -> []
        | ApplyFn(Round, [ a ]) -> over "round" a @ go a
        | ApplyFn(Floor, [ a ]) -> over "floor" a @ go a
        | ApplyFn(Ceil, [ a ]) -> over "ceil" a @ go a
        | Rounded(a, _) -> over "rounded" a @ go a
        | Cast(IntType, a) -> over "cast int" a @ go a
        | Cast(DecimalType, a) -> over "cast decimal" a @ go a
        | ApplyFn(_, xs)
        | Coalesce xs -> many xs
        | Binary(_, a, b)
        | Quotient(a, b, _) -> many [ a; b ]
        | Not a
        | IsNull a
        | InParam(a, _)
        | Cast(_, a)
        | Convert(a, _) -> go a
        // Phase 596: a curve that jumps — a step, or a null outside its knots — decides which side of
        // a knot its operand fell; a continuous one (linear or monotone cubic, clamped) decides nothing.
        | Interpolate(a, _, kind, outside) ->
            match kind, outside with
            | InterpKind.Step, _
            | _, InterpOutside.Null -> over "interpolate" a @ go a
            | _ -> go a
        | Call(_, xs) -> many xs
        | Arg _ -> []
        | Case(cases, els) -> many ((cases |> List.collect (fun (w, t) -> [ w; t ])) @ [ els ])
        | InList(a, items) -> many (a :: items)

    /// The decisions ONE step makes over its input's marks.
    let private decisionsOf (i: int) (input: SchemaKnowledge) (step: Transform) : HostRoundedDecision list =
        let keyed (verb: string) (names: string list) =
            names
            |> List.filter (fun n -> markOf input n = Exactness.HostRounded)
            |> List.map (fun n -> { Step = i; Verb = verb; Operand = n })

        match step with
        | Filter pred ->
            (match hostRoundedOperand input pred with
             | Some operand ->
                 [ { Step = i
                     Verb = "filter"
                     Operand = operand } ]
             | None -> [])
            @ decisionsIn i input pred
        | Derive(_, e) -> decisionsIn i input e
        | Sort keys ->
            keys
            |> List.choose (fun (key, _) ->
                match key with
                | Slot.Lit name -> Some name
                | Slot.Param _ -> None)
            |> keyed "sort key"
        | GroupBy(keys, aggs) ->
            keyed "group-by key" keys
            @ (aggs
               |> List.filter (fun agg -> agg.Fn = CountDistinct)
               |> List.map _.Of
               |> keyed "count-distinct")
        | Join(_, on, _) -> keyed "join key" (on |> List.map fst)
        | Distinct -> keyed "distinct" (names input)
        | Intersect _ -> keyed "intersect" (names input)
        | Except _ -> keyed "except" (names input)
        | Pivot spec -> keyed "pivot key" (spec.Index @ [ spec.On ])
        | Window spec -> keyed "window key" (spec.PartitionBy @ (spec.OrderBy |> List.map fst))
        // Phase 592: a fit groups on its keys, codes a categorical term by its levels, and decides each
        // column's rank (D49 item 11) over the values its terms compute.
        | Fit spec ->
            keyed "fit key" spec.GroupBy
            @ (FitSpec.exprs spec
               |> List.collect (fun e ->
                   (match hostRoundedOperand input e with
                    | Some operand ->
                        [ { Step = i
                            Verb = "fit rank"
                            Operand = operand } ]
                    | None -> [])
                   @ decisionsIn i input e))
        // Phase 597: a prediction finds each row's group by its keys and codes a categorical term by the
        // fit's levels, over the values its terms compute on the step's input.
        | Predict spec ->
            keyed "predict key" spec.Fit.GroupBy
            @ (FitSpec.exprs spec.Fit
               |> List.collect (fun e ->
                   (match hostRoundedOperand input e with
                    | Some operand ->
                        [ { Step = i
                            Verb = "predict level"
                            Operand = operand } ]
                    | None -> [])
                   @ decisionsIn i input e))
        | Project _
        | Limit _
        | Union _
        | Unpivot _
        | Define _ -> []

    /// Every discrete decision a host-rounded value reaches in `pipeline` over knowledge already in
    /// hand (Phase 557, D43 choice (c)), in pipeline order: a `Filter` whose predicate reads one; a
    /// `Sort`, `GroupBy`, `Join`, `Distinct`, `Intersect`, `Except`, `Pivot` or `Window` keyed on one;
    /// a `CountDistinct` over one; and, inside any expression, a `Round` / `Floor` / `Ceil` /
    /// `Rounded` over one or a `Cast` of one to an int or a decimal. Each is admitted — the pipeline
    /// evaluates — and reported, because two hosts a last ulp apart can decide it differently, so
    /// the cross-host law promises its tolerance only on a column no such decision reaches. A
    /// pipeline reading no host-rounded value reports none, which is every pipeline over pinned input
    /// knowledge since Phase 594 (D54).
    let hostRoundedDecisionsFrom
        (sources: string -> Schema option)
        (input: SchemaKnowledge)
        (pipeline: Transform list)
        : HostRoundedDecision list =
        // Phase 596: the decisions are read off the expanded pipeline; each `Step` is then the
        // index of the WRITTEN step it came from (expansion drops only `Define` steps).
        let walked = expandedOrWritten pipeline

        let written =
            if List.length walked = List.length pipeline then
                [| 0 .. List.length pipeline - 1 |]
            else
                pipeline
                |> List.indexed
                |> List.choose (fun (i, t) ->
                    match t with
                    | Define _ -> None
                    | _ -> Some i)
                |> Array.ofList

        let _, _, decisions =
            walked
            |> List.fold
                (fun (i, knowledge, acc) step ->
                    i + 1, ofTransform sources knowledge step, acc @ decisionsOf written[i] knowledge step)
                (0, input, [])

        decisions

    /// `hostRoundedDecisionsFrom` over a concrete input schema with no declared sources.
    let hostRoundedDecisions (schema: Schema) (pipeline: Transform list) : HostRoundedDecision list =
        hostRoundedDecisionsFrom noSources (ofSchema schema) pipeline

/// Reading a wire text into the JSON model with a refusal in the codecs' `ColumnError` envelope —
/// the one read every decoder in this assembly starts from. It reads under `ReadOptions.strict` and
/// projects the refusal exactly as the substrate's column codec does: malformed text is `NotJson`
/// carrying the parser's classified error, and the depth cap, the one breach a strict read meets,
/// keeps its `NotJson` spelling (`MaxDepthExceeded` at the container's position), so no refusal a
/// caller reads moved when the substrate's reader began reporting the cap as a breach.
module internal WireText =

    let read (text: string) : Result<JVal, ColumnError> =
        match Json.read ReadOptions.strict text with
        | Ok v -> Ok v
        | Error(JsonRefusal.Malformed e) -> Error(NotJson e)
        | Error(JsonRefusal.LimitExceeded b) when b.Limit = JsonLimit.Depth ->
            Error(
                NotJson
                    { Kind = MaxDepthExceeded
                      Position = b.Position
                      Message = "max nesting depth " + string b.Declared + " exceeded" }
            )
        | Error r -> Error(MalformedShape (DecodeError.ofJsonRefusal r).Message)

/// The canonical wire codec for the `Transform` + `ColExpr` trees — `"kind"`-tagged objects (the
/// `Fuaran.Core` envelope discipline), reusing `ColumnCodec` for embedded `DataSource` operands and
/// the `Wire` canonical-float rules for literals. Decode is `Result`-typed with the same six-code
/// `ColumnError` envelope (a `Transform` wire is a columnar-strand wire). Fable-clean.
module DataFrameCodec =

    let private aggFnTag = DataFrame.aggFnTag

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
        // Deprecated `Rank` (Phase 414, D24) keeps its own tag, so a document holding it re-encodes
        // byte for byte; a new pipeline that writes `DenseRank` encodes `"denseRank"`.
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
        | Pow -> "pow"
        | Exp -> "exp"
        | Ln -> "ln"
        | Log10 -> "log10"

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
        | "pow" -> Some Pow
        | "exp" -> Some Exp
        | "ln" -> Some Ln
        | "log10" -> Some Log10
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
        | Null -> Canonical.typed "Null" []
        | Int i -> Canonical.typed "Int" [ "value", JInt i ]
        | Float f -> Canonical.typed "Float" [ "value", JFloat f ]
        | Bool b -> Canonical.typed "Bool" [ "value", JBool b ]
        | Str s -> Canonical.typed "Str" [ "value", JStr s ]
        | Date s -> Canonical.typed "Date" [ "value", JStr s ]
        | Timestamp s -> Canonical.typed "Timestamp" [ "value", JStr s ]
        // Phase 277: the canonical decimal TEXT, a JSON string, as the column codec carries it
        // (Core `DECISIONS.md` D72 K5) — a number token would have been through a float.
        | Decimal s -> Canonical.typed "Decimal" [ "value", JStr s ]

    /// The refusal of a literal whose `value` is not of its `$type`: a `TypeMismatch` naming the
    /// column type the tag denotes (Core `1.0.0` types the expected side), or, for a tag outside
    /// the cell vocabulary, the unknown-tag refusal.
    let private litMismatch (tag: string) : ColumnError =
        let known =
            match tag with
            | "Int" -> Some IntType
            | "Float" -> Some FloatType
            | "Bool" -> Some BoolType
            | "Str" -> Some StringType
            | "Date" -> Some DateType
            | "Timestamp" -> Some(TimestampType TimeUnit.Seconds)
            | "Decimal" -> Some DecimalType
            | _ -> None

        match known with
        | Some ty -> TypeMismatch("lit", ty, "value")
        | None ->
            WireRefusal.unknownTag (tag, [ "Null"; "Int"; "Float"; "Bool"; "Str"; "Date"; "Timestamp"; "Decimal" ])

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
                    | None -> Error(litMismatch t)
                | Some(JInt i), "Decimal" -> Ok(Decimal(string i))
                | _ -> Error(litMismatch t)
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

    // ---- the curve vocabulary (Phase 596) ----
    // The ONE place an `InterpKind` and an `InterpOutside` are spelled, each a total round trip.

    let private interpKinds: (InterpKind * string) list =
        [ InterpKind.Linear, "linear"
          InterpKind.Step, "step"
          InterpKind.MonotoneCubic, "monotoneCubic" ]

    let private interpOutsides: (InterpOutside * string) list =
        [ InterpOutside.Clamp, "clamp"; InterpOutside.Null, "null" ]

    let private tagIn (table: ('a * string) list) (v: 'a) : string =
        table |> List.find (fun (k, _) -> k = v) |> snd

    let private knotsJson (knots: (float * float) list) : JVal =
        JArr(knots |> List.map (fun (x, y) -> JArr [ JFloat x; JFloat y ]))

    let rec encodeExpr (e: ColExpr) : JVal =
        match e with
        | Col name -> Canonical.typed "col" [ "name", JStr name ]
        | Lit c -> Canonical.typed "lit" [ "cell", cellToJson c ]
        | Param name -> Canonical.typed "param" [ "name", JStr name ]
        | Binary(op, a, b) ->
            Canonical.typed "binary" [ "op", JStr(binTag op); "left", encodeExpr a; "right", encodeExpr b ]
        | Not inner -> Canonical.typed "not" [ "expr", encodeExpr inner ]
        | Coalesce exprs -> Canonical.typed "coalesce" [ "exprs", JArr(exprs |> List.map encodeExpr) ]
        | Case(cases, elseExpr) ->
            Canonical.typed
                "case"
                [ "cases",
                  JArr(
                      cases
                      |> List.map (fun (w, t) -> JObj [ "when", encodeExpr w; "then", encodeExpr t ])
                  )
                  "else", encodeExpr elseExpr ]
        | Cast(ty, inner) -> Canonical.typed "cast" [ "type", JStr(ColumnType.tag ty); "expr", encodeExpr inner ]
        | ApplyFn(fn, args) ->
            Canonical.typed "apply" [ "fn", JStr(scalarTag fn); "args", JArr(args |> List.map encodeExpr) ]
        | InList(subject, items) ->
            Canonical.typed "in" [ "expr", encodeExpr subject; "items", JArr(items |> List.map encodeExpr) ]
        | InParam(subject, name) -> Canonical.typed "in" [ "expr", encodeExpr subject; "param", JStr name ]
        | IsNull inner -> Canonical.typed "isNull" [ "expr", encodeExpr inner ]
        | Now grain -> Canonical.typed "now" [ "grain", JStr(NowGrain.tag grain) ]
        | Quotient(a, b, r) ->
            Canonical.typed
                "quotient"
                [ "dividend", encodeExpr a
                  "divisor", encodeExpr b
                  "rounding", roundingJson r ]
        | Rounded(x, r) -> Canonical.typed "rounded" [ "expr", encodeExpr x; "rounding", roundingJson r ]
        | Convert(x, u) -> Canonical.typed "convert" [ "expr", encodeExpr x; "unit", JStr(Unit.render u) ]
        | Arg name -> Canonical.typed "arg" [ "name", JStr name ]
        // `invoke`, not `call`: Phase 93 made `call` a lenient spelling of `apply`.
        | Call(name, args) -> Canonical.typed "invoke" [ "name", JStr name; "args", JArr(args |> List.map encodeExpr) ]
        | Interpolate(x, knots, kind, outside) ->
            Canonical.typed
                "interpolate"
                [ "expr", encodeExpr x
                  "knots", knotsJson knots
                  "kind", JStr(tagIn interpKinds kind)
                  "outside", JStr(tagIn interpOutsides outside) ]

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
            | None -> Error(WireRefusal.unknownTag (ms, allModes |> List.map modeTag))
            | Some mode ->
                field "scale" el
                |> Result.bind (slotOf intOf "rounding scale")
                |> Result.map (fun scale -> { Scale = scale; Mode = mode }))

    /// A knot coordinate: a float token, or an integer token read as its float.
    let private knotNum (el: JVal) : Result<float, ColumnError> =
        match el with
        | JFloat f -> Ok f
        | JInt i -> Ok(float i)
        | _ -> Error(MalformedShape "interpolate: a knot is a two-number array [x, y]")

    let private knotsOf (el: JVal) : Result<(float * float) list, ColumnError> =
        arrOf el
        |> Result.bind (
            mapM (fun k ->
                match k with
                | JArr [ x; y ] -> knotNum x |> Result.bind (fun x -> knotNum y |> Result.map (fun y -> x, y))
                | _ -> Error(MalformedShape "interpolate: a knot is a two-number array [x, y]"))
        )

    let private tagOf (table: ('a * string) list) (el: JVal) : Result<'a, ColumnError> =
        strOf el
        |> Result.bind (fun t ->
            match table |> List.tryFind (fun (_, tag) -> tag = t) with
            | Some(v, _) -> Ok v
            | None -> Error(WireRefusal.unknownTag (t, table |> List.map snd)))

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
                            WireRefusal.unknownTag (
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
                    | None -> Error(UnknownType(ts, ColumnType.all))
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
                            WireRefusal.unknownTag (
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
                                  "indexOf"
                                  "pow"
                                  "exp"
                                  "ln"
                                  "log10" ]
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
            | "arg" -> field "name" el |> Result.bind strOf |> Result.map Arg
            | "invoke" ->
                field "name" el
                |> Result.bind strOf
                |> Result.bind (fun name ->
                    field "args" el
                    |> Result.bind arrOf
                    |> Result.bind (mapM decodeExpr)
                    |> Result.map (fun args -> Call(name, args)))
            | "interpolate" ->
                field "expr" el
                |> Result.bind decodeExpr
                |> Result.bind (fun x ->
                    field "knots" el
                    |> Result.bind knotsOf
                    |> Result.bind (fun knots ->
                        field "kind" el
                        |> Result.bind (tagOf interpKinds)
                        |> Result.bind (fun kind ->
                            field "outside" el
                            |> Result.bind (tagOf interpOutsides)
                            |> Result.map (fun outside -> Interpolate(x, knots, kind, outside)))))
            | "convert" ->
                field "unit" el
                |> Result.bind strOf
                |> Result.bind (fun text ->
                    match Unit.parse text with
                    | Error _ -> Error(MalformedShape("convert: '" + text + "' is not a unit"))
                    | Ok u -> field "expr" el |> Result.bind decodeExpr |> Result.map (fun x -> Convert(x, u)))
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
                    | None -> Error(WireRefusal.unknownTag (gs, NowGrain.allTags)))
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
                    WireRefusal.unknownTag (
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
                        WireRefusal.unknownTag (
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


    /// A fit spec's members, as the fit step writes them and a predict step's `fit` nests them (Phase
    /// 592, D53; Phase 597).
    let private fitFields (spec: FitSpec) : (string * JVal) list =
        ([ "response", JStr spec.Response
           "predictors",
           JArr(
               spec.Predictors
               |> List.map (fun (name, e) -> JObj [ "name", JStr name; "expr", encodeExpr e ])
           )
           "intercept", JBool spec.Intercept
           "groupBy", strList spec.GroupBy
           "family", JStr(DataFrame.fitFamilyTag spec.Family)
           "link", JStr(DataFrame.fitLinkTag spec.Link)
           "control",
           JObj
               [ "maxIter", slotJson JInt spec.Control.MaxIter
                 "tol", slotJson JFloat spec.Control.Tol ]
           "output", JStr(DataFrame.fitOutputTag spec.Output) ]
         @ (match spec.Family with
            | FitFamily.Tweedie power -> [ "power", JFloat power ]
            // Phase 600 (D58 item 10): a fixed θ; an estimated one carries no member.
            | FitFamily.NegativeBinomial(Some theta) -> [ "theta", JFloat theta ]
            | _ -> [])
         @ (spec.Weights |> Option.map (fun w -> "weights", JStr w) |> Option.toList)
         @ (spec.Offset |> Option.map (fun e -> "offset", encodeExpr e) |> Option.toList)
         @ (spec.Ridge |> Option.map (fun r -> "ridge", slotJson JFloat r) |> Option.toList)
         // Phase 598 (D57): `stdErrors` only where the spec names a sandwich variant.
         @ (match spec.StdErrors with
            | FitStdErrors.Classical -> []
            | FitStdErrors.HC0 -> [ "stdErrors", JStr "hc0" ]
            | FitStdErrors.HC1 -> [ "stdErrors", JStr "hc1" ]
            | FitStdErrors.HC2 -> [ "stdErrors", JStr "hc2" ]
            | FitStdErrors.HC3 -> [ "stdErrors", JStr "hc3" ]))

    let encodeTransform (t: Transform) : JVal =
        match t with
        | Filter pred -> Canonical.typed "filter" [ "pred", encodeExpr pred ]
        // `0.28.0` (D48) — the member is `columns`, not `cols`: it holds the LIST of column renames,
        // and `cols` is the wire format's name for an integer column COUNT. `cols` remains a decode alias
        // and is never emitted.
        | Project pairs -> Canonical.typed "project" [ "columns", JArr(pairs |> List.map pairJson) ]
        | Derive(name, expr) -> Canonical.typed "derive" [ "name", JStr name; "expr", encodeExpr expr ]
        | GroupBy(keys, aggs) ->
            Canonical.typed "groupBy" [ "keys", strList keys; "aggs", JArr(aggs |> List.map aggJson) ]
        | Join(src, on, how) ->
            Canonical.typed
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

            Canonical.typed
                "window"
                // Phase 101 — the bucket count is emitted ONLY for `ntile`, so every pre-existing
                // window step's wire is byte-unchanged. `Canonical.write Rendering.CrossHost` sorts keys, so position
                // does not matter.
                (match spec.Fn with
                 | NTile buckets -> fields @ [ "n", JInt buckets ]
                 | _ -> fields)
        | Pivot spec ->
            Canonical.typed
                "pivot"
                [ "index", strList spec.Index
                  "on", JStr spec.On
                  "values", JStr spec.Values
                  "agg", JStr(aggFnTag spec.Agg) ]
        | Unpivot(idVars, valueVars) ->
            Canonical.typed "unpivot" [ "idVars", strList idVars; "valueVars", strList valueVars ]
        | Sort by -> Canonical.typed "sort" [ "by", JArr(by |> List.map sortKeyJson) ]
        | Distinct -> Canonical.typed "distinct" []
        | Limit(n, offset) -> Canonical.typed "limit" [ "n", slotJson JInt n; "offset", slotJson JInt offset ]
        | Union src -> Canonical.typed "union" [ "source", ColumnCodec.encodeJson src ]
        | Intersect src -> Canonical.typed "intersect" [ "source", ColumnCodec.encodeJson src ]
        | Except src -> Canonical.typed "except" [ "source", ColumnCodec.encodeJson src ]
        | Define fd ->
            let paramJson (p: FnParam) =
                JObj(
                    [ "name", JStr p.Name ]
                    @ (match p.Default with
                       | None -> []
                       | Some(Slot.Lit c) -> [ "default", cellToJson c ]
                       | Some(Slot.Param n) -> [ "default", JObj [ "$param", JStr n ] ])
                )

            Canonical.typed
                "define"
                [ "name", JStr fd.Name
                  "params", JArr(fd.Params |> List.map paramJson)
                  "body", encodeExpr fd.Body ]
        // Phase 592 (D53): every member is emitted; `power` only for a Tweedie family, and `weights`,
        // `offset` and `ridge` only where the spec carries them; `stdErrors` (Phase 598) only where it
        // is not classical.
        | Fit spec -> Canonical.typed "fit" (fitFields spec)
        // Phase 597 (D56): the model's spec as the fit step's members, the model's source, the scale,
        // and `interval` only where the spec carries one.
        | Predict spec ->
            let model =
                match spec.Model with
                | PredictModel.Training src -> JObj [ "from", JStr "training"; "source", ColumnCodec.encodeJson src ]
                | PredictModel.Coefficients src ->
                    JObj [ "from", JStr "coefficients"; "source", ColumnCodec.encodeJson src ]

            Canonical.typed
                "predict"
                ([ "fit", JObj(fitFields spec.Fit)
                   "model", model
                   "scale",
                   JStr(
                       match spec.Scale with
                       | PredictScale.Link -> "link"
                       | PredictScale.Response -> "response"
                   ) ]
                 @ (spec.Interval
                    |> Option.map (fun i ->
                        "interval",
                        JObj
                            [ "kind",
                              JStr(
                                  match i.Kind with
                                  | IntervalKind.Confidence -> "confidence"
                                  | IntervalKind.Prediction -> "prediction"
                              )
                              "level", slotJson JFloat i.Level ])
                    |> Option.toList))

    /// A function parameter (Phase 596): its `name`, and an optional `default` — a literal cell, or
    /// `{"$param": <name>}`, the slot form every other slot on the wire takes.
    let private fnParamOf (el: JVal) : Result<FnParam, ColumnError> =
        field "name" el
        |> Result.bind strOf
        |> Result.bind (fun name ->
            match tryField "default" el with
            | None -> Ok { Name = name; Default = None }
            | Some d ->
                match tryField "$param" d with
                | Some(JStr n) ->
                    Ok
                        { Name = name
                          Default = Some(Slot.Param n) }
                | Some _ -> Error(MalformedShape "\"$param\" must be a JSON string (function parameter default)")
                | None ->
                    cellOfJson d
                    |> Result.map (fun c ->
                        { Name = name
                          Default = Some(Slot.Lit c) }))

    /// A float literal of a slot: a float token, or an integer token read as its float.
    let private floatOf (el: JVal) : Result<float, ColumnError> =
        match el with
        | JFloat f -> Ok f
        | JInt i -> Ok(float i)
        | _ -> Error(MalformedShape "expected number")

    let private boolOf (el: JVal) : Result<bool, ColumnError> =
        match el with
        | JBool b -> Ok b
        | _ -> Error(MalformedShape "expected boolean")

    /// A fit step's spec (Phase 592, D53). Every member but `weights`, `offset`, `ridge`, `power`, `theta`
    /// and `stdErrors` is required; `power` is required for, and only read with, a Tweedie family;
    /// `theta` is read only with a negative binomial family, which estimates θ without it (Phase 600). A
    /// family and link the estimator does not fit (`Glm.admitted`) is refused by name.
    /// `stdErrors` (Phase 598, D57) is `classical`, `hc0` to `hc3`, or `robust` — the default variant,
    /// `hc3` — and classical where absent.
    let private fitSpecOf (el: JVal) : Result<FitSpec, ColumnError> =
        let tag (table: (string * 'a) list) (s: string) : Result<'a, ColumnError> =
            match table |> List.tryFind (fun (t, _) -> t = s) with
            | Some(_, v) -> Ok v
            | None -> Error(WireRefusal.unknownTag (s, table |> List.map fst))

        let optional (name: string) (f: JVal -> Result<'a, ColumnError>) : Result<'a option, ColumnError> =
            match tryField name el with
            | Some v -> f v |> Result.map Some
            | None -> Ok None

        let predictorOf (p: JVal) =
            field "name" p
            |> Result.bind strOf
            |> Result.bind (fun name -> field "expr" p |> Result.bind decodeExpr |> Result.map (fun e -> name, e))

        field "response" el
        |> Result.bind strOf
        |> Result.bind (fun response ->
            field "predictors" el
            |> Result.bind arrOf
            |> Result.bind (mapM predictorOf)
            |> Result.bind (fun predictors ->
                field "intercept" el
                |> Result.bind boolOf
                |> Result.bind (fun intercept ->
                    field "groupBy" el
                    |> Result.bind strListOf
                    |> Result.bind (fun groupBy ->
                        field "family" el
                        |> Result.bind strOf
                        |> Result.bind (fun familyTag ->
                            match familyTag with
                            | "tweedie" -> field "power" el |> Result.bind floatOf |> Result.map FitFamily.Tweedie
                            | "negativeBinomial" ->
                                optional "theta" floatOf |> Result.map FitFamily.NegativeBinomial
                            | other ->
                                tag
                                    [ "gaussian", FitFamily.Gaussian
                                      "poisson", FitFamily.Poisson
                                      "gamma", FitFamily.Gamma
                                      "quasiPoisson", FitFamily.QuasiPoisson
                                      "binomial", FitFamily.Binomial
                                      "quasiBinomial", FitFamily.QuasiBinomial
                                      "inverseGaussian", FitFamily.InverseGaussian ]
                                    other
                                |> Result.mapError (fun _ ->
                                    WireRefusal.unknownTag (
                                        other,
                                        [ "gaussian"
                                          "poisson"
                                          "gamma"
                                          "tweedie"
                                          "quasiPoisson"
                                          "binomial"
                                          "quasiBinomial"
                                          "inverseGaussian"
                                          "negativeBinomial" ]
                                    )))
                        |> Result.bind (fun family ->
                            field "link" el
                            |> Result.bind strOf
                            |> Result.bind (
                                tag
                                    [ "identity", FitLink.Identity
                                      "log", FitLink.Log
                                      "logit", FitLink.Logit
                                      "probit", FitLink.Probit
                                      "cloglog", FitLink.Cloglog
                                      "inverse", FitLink.Inverse
                                      "inverseSquare", FitLink.InverseSquare
                                      "sqrt", FitLink.Sqrt ]
                            )
                            // Phase 600 (D58 item 10): a pair the estimator does not fit is refused
                            // here, by name, never carried to the step.
                            |> Result.bind (fun link ->
                                if DataFrame.fitAdmitted family link then
                                    Ok link
                                else
                                    Error(
                                        MalformedShape(
                                            "fit: the "
                                            + DataFrame.fitFamilyTag family
                                            + " family is not fitted under the "
                                            + DataFrame.fitLinkTag link
                                            + " link"
                                        )
                                    ))
                            |> Result.bind (fun link ->
                                field "control" el
                                |> Result.bind (fun c ->
                                    field "maxIter" c
                                    |> Result.bind (slotOf intOf "fit maxIter")
                                    |> Result.bind (fun maxIter ->
                                        field "tol" c
                                        |> Result.bind (slotOf floatOf "fit tol")
                                        |> Result.map (fun tol -> { MaxIter = maxIter; Tol = tol })))
                                |> Result.bind (fun control ->
                                    field "output" el
                                    |> Result.bind strOf
                                    |> Result.bind (
                                        tag
                                            [ "coefficients", FitOutput.Coefficients
                                              "summary", FitOutput.Summary
                                              "fitted", FitOutput.Fitted
                                              "residuals", FitOutput.Residuals ]
                                    )
                                    |> Result.bind (fun output ->
                                        optional "weights" strOf
                                        |> Result.bind (fun weights ->
                                            optional "offset" decodeExpr
                                            |> Result.bind (fun offset ->
                                                optional "ridge" (slotOf floatOf "fit ridge")
                                                |> Result.bind (fun ridge ->
                                                    optional
                                                        "stdErrors"
                                                        (strOf
                                                         >> Result.bind (
                                                             tag
                                                                 [ "classical", FitStdErrors.Classical
                                                                   "hc0", FitStdErrors.HC0
                                                                   "hc1", FitStdErrors.HC1
                                                                   "hc2", FitStdErrors.HC2
                                                                   "hc3", FitStdErrors.HC3
                                                                   "robust", FitStdErrors.Robust ]
                                                         ))
                                                    |> Result.map (fun stdErrors ->
                                                        { Response = response
                                                          Predictors = predictors
                                                          Intercept = intercept
                                                          GroupBy = groupBy
                                                          Weights = weights
                                                          Offset = offset
                                                          Ridge = ridge
                                                          Family = family
                                                          Link = link
                                                          Control = control
                                                          Output = output
                                                          StdErrors =
                                                            stdErrors
                                                            |> Option.defaultValue FitStdErrors.Classical }))))))))))))

    /// A predict step's spec (Phase 597, D56): `fit` (a fit step's members), `model`, `scale` and an
    /// optional `interval`.
    let private predictSpecOf (el: JVal) : Result<PredictSpec, ColumnError> =
        let tag (table: (string * 'a) list) (s: string) : Result<'a, ColumnError> =
            match table |> List.tryFind (fun (t, _) -> t = s) with
            | Some(_, v) -> Ok v
            | None -> Error(WireRefusal.unknownTag (s, table |> List.map fst))

        field "fit" el
        |> Result.bind fitSpecOf
        |> Result.bind (fun fit ->
            field "model" el
            |> Result.bind (fun m ->
                field "from" m
                |> Result.bind strOf
                |> Result.bind (tag [ "training", PredictModel.Training; "coefficients", PredictModel.Coefficients ])
                |> Result.bind (fun from -> field "source" m |> Result.bind ColumnCodec.decodeJson |> Result.map from))
            |> Result.bind (fun model ->
                field "scale" el
                |> Result.bind strOf
                |> Result.bind (tag [ "link", PredictScale.Link; "response", PredictScale.Response ])
                |> Result.bind (fun scale ->
                    match tryField "interval" el with
                    | None -> Ok None
                    | Some i ->
                        field "kind" i
                        |> Result.bind strOf
                        |> Result.bind (
                            tag [ "confidence", IntervalKind.Confidence; "prediction", IntervalKind.Prediction ]
                        )
                        |> Result.bind (fun kind ->
                            field "level" i
                            |> Result.bind (slotOf floatOf "predict level")
                            |> Result.map (fun level -> Some { Kind = kind; Level = level }))
                    |> Result.map (fun interval ->
                        { Fit = fit
                          Model = model
                          Scale = scale
                          Interval = interval }))))

    let decodeTransform (el: JVal) : Result<Transform, ColumnError> =
        // Phase 88 DIDACTIC — a step without `$type` names the op roster (the
        // pilot-3 "pipeline: missing field: $type" class); ambiguity means no
        // coercion, but the error teaches the shape.
        match kindOf el with
        | Error(MissingField "$type") ->
            Error(
                MalformedShape(
                    "a pipeline step is a $type-discriminated op (filter | project | derive | groupBy | join | window | pivot | unpivot | sort | distinct | limit | union | intersect | except | define) — this step object has no \"$type\""
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
                                            WireRefusal.unknownTag (
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
                                    Error(
                                        WireRefusal.unknownTag (
                                            hows,
                                            [ "inner"; "left"; "right"; "outer"; "semi"; "anti" ]
                                        )
                                    )
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
                                                WireRefusal.unknownTag (
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
                                            WireRefusal.unknownTag (
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
                | "define" ->
                    field "name" el
                    |> Result.bind strOf
                    |> Result.bind (fun name ->
                        field "params" el
                        |> Result.bind arrOf
                        |> Result.bind (mapM fnParamOf)
                        |> Result.bind (fun ps ->
                            field "body" el
                            |> Result.bind decodeExpr
                            |> Result.map (fun body ->
                                Define
                                    { Name = name
                                      Params = ps
                                      Body = body })))
                | "fit" -> fitSpecOf el |> Result.map Fit
                | "predict" -> predictSpecOf el |> Result.map Predict
                | other ->
                    Error(
                        WireRefusal.unknownTag (
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
                              "except"
                              "define"
                              "fit"
                              "predict" ]
                        )
                    ))

    /// Encode a pipeline (ordered `Transform list`) to a canonical wire string.
    let encodePipeline (pipeline: Transform list) : string =
        Canonical.write Rendering.CrossHost (JArr(pipeline |> List.map encodeTransform))

    /// Decode a pipeline from a wire string (six-code `ColumnError` envelope; `NotJson` on a
    /// syntax error).
    let decodePipeline (s: string) : Result<Transform list, ColumnError> =
        match WireText.read s with
        | Error m -> Error m
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

// ============================================================================
//  Phase 375 — the step as data: a row-local step handed to a runner that
//  shares no closure with the caller.
//
//  The morsel runner Layer 6 runs (`KernelSet.RunMorsels`) takes a closure over
//  the frame, the compiled tree and the caller's result arrays, which cannot
//  cross into a Web Worker (compute `DECISIONS.md` D10 item 4b). The hand-off
//  below is that step as PLAIN DATA: the step and its environment on the
//  canonical wire, the frame's schema, the vectors the step reads (int and float
//  values as they are, bool values and validity masks as bytes), the logical
//  rows, the morsel geometry and range, and the result slots — strings, numbers
//  and arrays of numbers only, so it survives a structured clone, and every
//  array in it is one a typed array over shared memory can back.
//
//  There is ONE compile path. A runner rebuilds the frame from the data, resolves
//  and compiles the step with the evaluator's own `resolveExpr` / `compileExpr`,
//  and runs each morsel through the evaluator's own row loop (`filterMorsel`,
//  `deriveMorsel`) — the loops the sequential member runs. The caller finishes:
//  a `Filter`'s kept rows concatenated in morsel order (`keptInOrder`), a
//  `Derive`'s carrier through `derivedTyped`; an error is recovered by re-running
//  the first failed morsel on the caller's thread, through the same loop, so the
//  error crosses no boundary and is the one the sequential member raises.
//
//  A step is handed off only when the wire carries it exactly (it decodes back to
//  itself, and so does its environment), when every column it reads is an int,
//  float or bool vector (a string, decimal or boxed column cannot be shared), when
//  it is not a comparison-kernel filter (Phase 270's bitmap answers that one), and
//  when a `Derive`'s root is typed int, float or bool. Every other step runs as it
//  always ran. Internal: the worker pool (`WorkerPool`, below; Phase 376) is its
//  runner, and the laws (`MorselHandOffTests`, `PooledLawTests`) hold every handed-off
//  step byte-identical to the sequential member.
// ============================================================================

/// One row-local step as plain data (Phase 375): what a runner needs to compile and run a range of
/// the step's morsels without any closure of the caller's, and the slots it writes its results to.
type internal MorselHandOff =
    {
        /// The step — a `Filter` or a `Derive` — as a one-step pipeline on the canonical wire.
        Step: string
        /// The evaluation environment on the canonical wire: an object from each bound name to the
        /// `lit` expression of its cell.
        Env: string
        /// The frame's schema, every column, as the canonical wire of its field documents
        /// (`ColumnCodec.fieldJson`: name, type and the metadata a field states — Phase 429, so a
        /// hand-off's answer carries what the sequential path's does). A STRING, as `Step` and `Env`
        /// are: a hand-off crosses a worker boundary by structured clone, which carries primitives
        /// and arrays and not a map's comparer.
        Fields: string
        /// What is carried of each column: `0` nothing (the step reads no such column), `1` int
        /// values (`Ints`), `2` float values (`Floats`), `3` bool values as bytes (`Bools`); a
        /// carried column's validity mask is in `Masks`, one byte a row. Empty where not carried.
        Kinds: int[]
        Ints: int[][]
        Floats: float[][]
        Bools: Mask[]
        Masks: Mask[]
        /// The frame's physical row count: the length of every carried vector.
        Count: int
        /// The physical row of every logical row, in logical order.
        Rows: int[]
        /// The rows in one morsel: morsel `j` is the logical rows from `j * MorselRows` to the next.
        MorselRows: int
        /// The morsels a runner given this record runs: `First .. Last - 1`.
        First: int
        Last: int
        /// What the step answers: `0` a `Filter`'s kept rows; a `Derive`'s typed root, `1` int,
        /// `2` float, `3` bool.
        Root: int
        /// A `Filter`'s kept physical rows, morsel `j`'s from offset `j * MorselRows`, and how many
        /// each morsel kept.
        OutRows: int[]
        OutCounts: int[]
        /// `1` for a morsel that met an error.
        OutFailed: byte[]
        /// A `Derive`'s carrier at each physical row (the one its root's type names) and its mask.
        OutInts: int[]
        OutFloats: float[]
        OutBools: Mask
        OutMask: Mask
    }

module internal MorselHandOff =

    // Since Phase 376 the hand-off carries the frame's own arrays: a mask and a `Bools` vector's
    // values are a `Mask` (bytes under Fable), so nothing is copied between the evaluator's form and
    // the hand-off's; Phase 375 copied every mask to bytes to plan and back to `bool[]` in every
    // runner. Where allocation is shared (`Shared`: a host opted in to the pool) an array not yet over
    // shared memory is copied into it once, at plan; elsewhere every array is carried as it is.

    let private encodeEnv (env: Map<string, Cell>) : string =
        Canonical.write
            Rendering.CrossHost
            (JObj [ for KeyValue(name, c) in env -> name, DataFrameCodec.encodeExpr (Lit c) ])

    let private decodeEnv (s: string) : Map<string, Cell> option =
        match WireText.read s with
        | Ok(JObj fields) ->
            let cells =
                fields
                |> List.map (fun (name, el) ->
                    match DataFrameCodec.decodeExpr el with
                    | Ok(Lit c) -> Some(name, c)
                    | _ -> None)

            if List.forall Option.isSome cells then
                Some(cells |> List.choose id |> Map.ofList)
            else
                None
        | _ -> None

    let private decodeStep (s: string) : Transform option =
        match DataFrameCodec.decodePipeline s with
        | Ok [ t ] -> Some t
        | _ -> None

    /// The schema as its field documents, canonically rendered (Phase 429).
    let private encodeFields (cols: Schema) : string =
        Canonical.write Rendering.CrossHost (JArr(cols |> List.map ColumnCodec.fieldJson))

    let private decodeFields (s: string) : Schema option =
        match WireText.read s with
        | Ok(JArr items) ->
            let fields =
                items
                |> List.map (fun el ->
                    match ColumnCodec.decodeField el with
                    | Ok f -> Some f
                    | Error _ -> None)

            if List.forall Option.isSome fields then
                Some(fields |> List.choose id)
            else
                None
        | _ -> None

    /// The columns a resolved expression reads.
    let rec private reads (e: DataFrame.ResolvedExpr) : int list =
        match e with
        | DataFrame.RCol i -> [ i ]
        | DataFrame.RConst _
        | DataFrame.RFail _ -> []
        | DataFrame.RBinary(_, a, b) -> reads a @ reads b
        | DataFrame.RNot a
        | DataFrame.RCast(_, a)
        | DataFrame.RIsNull a
        | DataFrame.RRounded(_, _, a) -> reads a
        | DataFrame.RInterpolate(_, a) -> reads a
        | DataFrame.RCoalesce xs
        | DataFrame.RApplyFn(_, xs) -> List.collect reads xs
        | DataFrame.RCase(cases, e) -> (cases |> List.collect (fun (w, t) -> reads w @ reads t)) @ reads e
        | DataFrame.RInList(s, xs) -> reads s @ List.collect reads xs
        | DataFrame.RQuotient(_, _, a, b) -> reads a @ reads b

    /// A kernel set that computes nothing: `filterBits` through it answers only WHETHER the
    /// comparison kernels take a predicate, at no cost.
    let private dry: KernelSet =
        { Kernels.portable with
            CmpInts = fun _ _ _ _ _ -> [||]
            CmpFloats = fun _ _ _ _ _ -> [||]
            And = fun _ _ -> [||]
            Or = fun _ _ -> [||] }

    let private morsels (morselRows: int) (n: int) : int = (n + morselRows - 1) / morselRows

    /// How many morsels the hand-off's rows make.
    let morselCount (h: MorselHandOff) : int = morsels h.MorselRows h.Rows.Length

    /// The step `t` over `f` under `env` as data, in morsels of `morselRows` rows, the range covering
    /// every morsel — or `None` when the step is not one a runner may take (see the header), in
    /// which case it runs as it always ran.
    let planAt (morselRows: int) (env: Map<string, Cell>) (f: Frame) (t: Transform) : MorselHandOff option =
        let expr =
            match t with
            | Filter e
            | Derive(_, e) -> Some e
            | _ -> None

        match expr with
        | None -> None
        | Some _ when morselRows < 1 -> None
        // A step the unit check refuses (Phase 429) is the sequential path's to refuse, by name.
        | Some e when Result.isError (DataFrame.resolveUnits "" f.Cols e) -> None
        | Some e ->
            let step = DataFrameCodec.encodePipeline [ t ]
            let envWire = encodeEnv env
            let exact = decodeStep step = Some t && decodeEnv envWire = Some env
            let resolved = DataFrame.resolveExpr env f.Cols e
            let used = reads resolved |> List.distinct

            let shareable =
                used
                |> List.forall (fun i ->
                    match f.Vecs[i] with
                    | Ints _
                    | Floats _
                    | Bools _ -> true
                    | Strs _
                    | Decs _
                    | Dates _
                    | Stamps _
                    | Cells _ -> false)

            let root =
                match t with
                | Filter _ ->
                    let past =
                        match f.Sel with
                        | Some sel -> sel |> Array.exists (fun p -> p < 0 || p >= f.Count)
                        | None -> false

                    if past || Option.isSome (DataFrame.filterBits dry f resolved) then
                        None
                    else
                        Some 0
                | _ ->
                    match DataFrame.derivedTypingIn env f.Cols e with
                    | DataFrame.Refused -> None
                    | _ ->
                        match (DataFrame.compileExpr f resolved).Node with
                        | DataFrame.NInt _ -> Some 1
                        | DataFrame.NFloat _ -> Some 2
                        | DataFrame.NBool _ -> Some 3
                        | _ -> None

            match root with
            | Some root when exact && shareable ->
                let k = List.length f.Cols
                let rows = Shared.shareInts (Frame.physical f)
                let m = morsels morselRows rows.Length
                let kinds: int[] = Array.zeroCreate k
                let ints: int[][] = Array.create k [||]
                let floats: float[][] = Array.create k [||]
                let bools: Mask[] = Array.create k [||]
                let masks: Mask[] = Array.create k [||]

                for i in used do
                    match f.Vecs[i] with
                    | Ints(v, mask) ->
                        kinds[i] <- 1
                        ints[i] <- Shared.shareInts v
                        masks[i] <- Shared.shareMask mask
                    | Floats(v, mask) ->
                        kinds[i] <- 2
                        floats[i] <- Shared.shareFloats v
                        masks[i] <- Shared.shareMask mask
                    | Bools(v, mask) ->
                        kinds[i] <- 3
                        bools[i] <- Shared.shareMask v
                        masks[i] <- Shared.shareMask mask
                    | Strs _
                    | Decs _
                    | Dates _
                    | Stamps _
                    | Cells _ -> ()

                let sized (want: int) (n: int) = if root = want then n else 0

                // A derive's carrier and its mask in one buffer, as the sequential member allocates them.
                let struct (outInts, outFloats, outBools, outMask) =
                    match root with
                    | 1 ->
                        let struct (v, m) = Shared.intsAndMask f.Count
                        struct (v, Shared.floats 0, Shared.mask 0, m)
                    | 2 ->
                        let struct (v, m) = Shared.floatsAndMask f.Count
                        struct (Shared.ints 0, v, Shared.mask 0, m)
                    | 3 ->
                        let struct (v, m) = Shared.boolsAndMask f.Count
                        struct (Shared.ints 0, Shared.floats 0, v, m)
                    | _ -> struct (Shared.ints 0, Shared.floats 0, Shared.mask 0, Shared.mask 0)

                Some
                    { Step = step
                      Env = envWire
                      Fields = encodeFields f.Cols
                      Kinds = kinds
                      Ints = ints
                      Floats = floats
                      Bools = bools
                      Masks = masks
                      Count = f.Count
                      Rows = rows
                      MorselRows = morselRows
                      First = 0
                      Last = m
                      Root = root
                      OutRows = Shared.ints (sized 0 rows.Length)
                      OutCounts = Shared.ints m
                      OutFailed = Shared.bytes m
                      OutInts = outInts
                      OutFloats = outFloats
                      OutBools = outBools
                      OutMask = outMask }
            | _ -> None

    /// `planAt` in the evaluator's own morsels (`Kernels.MorselRows`).
    let plan (env: Map<string, Cell>) (f: Frame) (t: Transform) : MorselHandOff option =
        planAt Kernels.MorselRows env f t

    /// The frame, environment and step a runner rebuilds from the data alone. A column the step does
    /// not read is an empty placeholder: the compiled tree reads only the columns the step names.
    let rebuild (h: MorselHandOff) : Frame * Map<string, Cell> * Transform =
        let cols =
            match decodeFields h.Fields with
            | Some cols -> cols
            | None -> invalidOp "a hand-off whose schema does not decode"

        let vecs =
            Array.init (List.length cols) (fun i ->
                match h.Kinds[i] with
                | 1 -> Ints(h.Ints[i], h.Masks[i])
                | 2 -> Floats(h.Floats[i], h.Masks[i])
                | 3 -> Bools(h.Bools[i], h.Masks[i])
                | _ -> Cells [||])

        let frame: Frame =
            { Cols = cols
              Vecs = vecs
              Sel = Some h.Rows
              Count = h.Count }

        match decodeStep h.Step, decodeEnv h.Env with
        | Some t, Some env -> frame, env, t
        | _ -> invalidOp "a hand-off whose step or environment does not decode"

    /// The step resolved against the frame a runner rebuilt — once per range, before its morsels.
    let resolve (f: Frame) (env: Map<string, Cell>) (t: Transform) : DataFrame.ResolvedExpr =
        match t with
        | Filter e
        | Derive(_, e) -> DataFrame.resolveExpr env f.Cols e
        | _ -> invalidOp "a hand-off whose step is not row-local"

    /// Run morsel `j` over the logical rows `lo .. hi - 1` of the rebuilt step, writing its results
    /// to the hand-off's slots, through the evaluator's own row loop: what `run` does for each
    /// morsel of its range. A runner passes morsel `j`'s own rows; the laws pass others.
    let runSpan (f: Frame) (resolved: DataFrame.ResolvedExpr) (h: MorselHandOff) (j: int) (lo: int) (hi: int) : unit =
        let phys = h.Rows

        let failed =
            match h.Root with
            | 0 ->
                let struct (count, error) = DataFrame.filterMorsel f resolved phys h.OutRows lo hi

                h.OutCounts[j] <- count
                Option.isSome error
            | 1 ->
                let c = DataFrame.compileExpr f resolved

                DataFrame.deriveMorsel c.Slot (DataFrame.rootOf DataFrame.pickInt c) phys lo hi (fun p v ->
                    Raw.put h.OutInts p v
                    Mask.put h.OutMask p true)
                |> Option.isSome
            | 2 ->
                let c = DataFrame.compileExpr f resolved

                // The canonical NaN, as the sequential member puts it (Phase 404).
                DataFrame.deriveMorsel c.Slot (DataFrame.rootOf DataFrame.pickFloat c) phys lo hi (fun p v ->
                    Raw.put h.OutFloats p (DataFrame.canonicalNaN v)
                    Mask.put h.OutMask p true)
                |> Option.isSome
            | _ ->
                let c = DataFrame.compileExpr f resolved

                DataFrame.deriveMorsel c.Slot (DataFrame.rootOf DataFrame.pickBool c) phys lo hi (fun p v ->
                    Mask.put h.OutBools p v
                    Mask.put h.OutMask p true)
                |> Option.isSome

        h.OutFailed[j] <- if failed then 1uy else 0uy

    /// The runner's side: rebuild the step from the data, compile it, and run the morsels
    /// `First .. Last - 1` in order, each over its own rows, stopping at the first that fails.
    let run (h: MorselHandOff) : unit =
        let f, env, t = rebuild h
        let resolved = resolve f env t
        let n = h.Rows.Length
        let mutable j = h.First

        while j < h.Last do
            runSpan f resolved h j (j * h.MorselRows) (min n ((j + 1) * h.MorselRows))
            j <- if h.OutFailed[j] <> 0uy then h.Last else j + 1

    /// The caller's side, once every morsel has run: the step's answer from the result slots — the
    /// sequential member's answer, byte for byte. An error is recovered by re-running the first
    /// failed morsel on the caller's own frame through the same row loop.
    let finish (env: Map<string, Cell>) (f: Frame) (t: Transform) (h: MorselHandOff) : Result<Frame, EvalError> =
        let n = h.Rows.Length
        let m = morselCount h

        match Seq.tryFind (fun j -> h.OutFailed[j] <> 0uy) (seq { 0 .. m - 1 }) with
        | Some j ->
            let resolved = resolve f env t
            let lo = j * h.MorselRows
            let hi = min n ((j + 1) * h.MorselRows)

            let error =
                match h.Root with
                | 0 ->
                    let struct (_, error) =
                        DataFrame.filterMorsel f resolved h.Rows (Array.zeroCreate n) lo hi

                    error
                | 1 ->
                    let c = DataFrame.compileExpr f resolved
                    DataFrame.deriveMorsel c.Slot (DataFrame.rootOf DataFrame.pickInt c) h.Rows lo hi (fun _ _ -> ())
                | 2 ->
                    let c = DataFrame.compileExpr f resolved
                    DataFrame.deriveMorsel c.Slot (DataFrame.rootOf DataFrame.pickFloat c) h.Rows lo hi (fun _ _ -> ())
                | _ ->
                    let c = DataFrame.compileExpr f resolved
                    DataFrame.deriveMorsel c.Slot (DataFrame.rootOf DataFrame.pickBool c) h.Rows lo hi (fun _ _ -> ())

            match error with
            | Some e -> Error e
            | None -> invalidOp "a morsel the runner failed answers no error on the caller's thread"
        | None ->
            match t with
            | Filter _ -> Ok(Frame.select f (DataFrame.keptInOrder h.MorselRows h.OutRows h.OutCounts))
            | Derive(name, e) ->
                let dt = DataFrame.derivedTypingIn env f.Cols e

                let unit =
                    match DataFrame.resolveUnits "" f.Cols e with
                    | Ok(u, _) -> u
                    | Error _ -> None

                let mask = h.OutMask

                let ty, vec =
                    match h.Root with
                    | 1 -> DataFrame.derivedTyped dt f.Count h.Rows IntType (Ints(h.OutInts, mask)) mask
                    | 2 -> DataFrame.derivedTyped dt f.Count h.Rows FloatType (Floats(h.OutFloats, mask)) mask
                    | _ -> DataFrame.derivedTyped dt f.Count h.Rows BoolType (Bools(h.OutBools, mask)) mask

                Ok(Frame.withColumn f name ty vec |> DataFrame.unitised name unit)
            | _ -> invalidOp "a hand-off whose step is not row-local"

    /// The step through a runner: planned, handed to `runner` (which must run every morsel of the
    /// hand-off it is given, in any order and on any thread), and finished — or `None` when the step
    /// is not handed off.
    let evalWith
        (runner: MorselHandOff -> unit)
        (morselRows: int)
        (env: Map<string, Cell>)
        (f: Frame)
        (t: Transform)
        : Result<Frame, EvalError> option =
        planAt morselRows env f t
        |> Option.map (fun h ->
            runner h
            finish env f t h)

// ============================================================================
//  The worker pool (Phase 376) — D10's yes, built on the step as data.
//
//  A host opts in (`WorkerPool.optIn`) by handing the pool a way to start a
//  worker; each worker runs a script the host owns that imports this package
//  and calls `WorkerPool.serve`. Only on a realm with shared memory: without
//  `SharedArrayBuffer` (a page served without COOP/COEP) the opt-in answers
//  `false` and nothing changes. Once opted in, the frame allocates its int and
//  float values and its masks over shared memory (`Shared`), so a hand-off
//  (`MorselHandOff`) reaches a worker by reference, and `evalToPreparedAsync`
//  — the only route here — hands each eligible `Filter` and `Derive` of at
//  least `PoolRows` rows to the pool.
//
//  One job at a time. The caller posts the hand-off to every worker that has
//  reported ready, then drains morsels beside them; every runner claims the
//  next morsel from one shared counter, so each morsel lands in its own result
//  slots, and the caller finishes in morsel order (`MorselHandOff.finish`):
//  the kept rows concatenated morsel by morsel, a derived column assembled by
//  physical row. A morsel that fails is flagged, no runner claims past it,
//  and the caller re-runs the first failed morsel to answer its error (375's
//  rule). A worker that faults, or whose error event the host forwards, is
//  retired and the step is answered by the sequential member: the pool never
//  answers differently, it answers or it steps aside.
//
//  The pool starts lazily: the first eligible step starts the workers and
//  runs as it always ran while they load; each worker is used from the step
//  after it reports ready, and stays warm until `optOut`.
// ============================================================================

/// A worker the host started for the pool (Phase 376), as the pool talks to it. A host wraps what
/// its platform gives it: a Web Worker's `postMessage`, `onmessage` (passing `event.data`) and
/// `terminate`, or node's `worker_threads` equivalents. The worker must run a script that calls
/// `WorkerPool.serve`; its error event, where the platform has one, is forwarded to the listener as
/// `null`, and the pool then retires the worker.
type MorselWorker =
    /// Send `message` to the worker.
    abstract Post: message: obj -> unit
    /// Hand every message the worker sends to `handler`.
    abstract Listen: handler: (obj -> unit) -> unit
    /// Stop the worker.
    abstract Stop: unit -> unit

/// One job, as the caller posts it to each worker: the hand-off and the shared control words
/// (`0` the next morsel to claim, `1` set once a morsel has failed, `2` how many runners have
/// finished, `3` how many morsels the workers ran).
type internal MorselJob =
    { Job: int
      HandOff: MorselHandOff
      Control: int[] }

/// What a worker sends back: `Ready` once on start, then one reply per job, `Faulted` when the
/// worker failed to run its share (`Fault` says why).
type internal MorselReply =
    { Ready: bool
      Job: int
      Faulted: bool
      Fault: string }

/// The atomic operations the runners share their control words through: `Atomics` over shared
/// memory under Fable, `Interlocked` / `Volatile` on .NET.
module internal Atomic =

#if FABLE_COMPILER
    /// Add `v` to `a[i]`, answering what it held before.
    [<Fable.Core.Emit("Atomics.add($0, $1, $2)")>]
    let fetchAdd (a: int[]) (i: int) (v: int) : int = Fable.Core.Util.jsNative

    /// `a[i]`, read atomically.
    [<Fable.Core.Emit("Atomics.load($0, $1)")>]
    let load (a: int[]) (i: int) : int = Fable.Core.Util.jsNative

    /// `a[i] <- v`, written atomically.
    [<Fable.Core.Emit("Atomics.store($0, $1, $2)")>]
    let store (a: int[]) (i: int) (v: int) : unit = Fable.Core.Util.jsNative
#else
    /// Add `v` to `a[i]`, answering what it held before.
    let fetchAdd (a: int[]) (i: int) (v: int) : int =
        System.Threading.Interlocked.Add(&a[i], v) - v

    /// `a[i]`, read atomically.
    let load (a: int[]) (i: int) : int = System.Threading.Volatile.Read(&a[i])

    /// `a[i] <- v`, written atomically.
    let store (a: int[]) (i: int) (v: int) : unit =
        System.Threading.Volatile.Write(&a[i], v)
#endif

/// The worker pool (Phase 376): opt-in, and reached only through `DataFrame.evalToPreparedAsync`.
[<RequireQualifiedAccess>]
module WorkerPool =

    /// The logical rows from which a step goes to the pool (four morsels); a smaller frame runs on the
    /// caller's thread as it always did. Set from this phase's own measurement
    /// (`benchmarks/results/2026-10-06-i7-9700-phase-376.md`), the evaluator's `filter` and `derive`
    /// under node and in Edge from the page's thread and from a dedicated worker, at 2, 4 and 8
    /// threads: at 10,000 rows (two morsels) it was faster in 7 of 18 cells, by at most 14 per cent,
    /// and more than doubled the time at eight threads on the page's thread; at 20,000 rows it was
    /// faster in 15 of 18; at 50,000 in all 18.
    [<Literal>]
    let PoolRows = 32768

    /// How a runner runs morsel `j` over the logical rows `lo .. hi - 1` of a rebuilt step: the
    /// pool runs `MorselHandOff.runSpan`; the laws hand it perturbed ones.
    type internal Span = Frame -> DataFrame.ResolvedExpr -> MorselHandOff -> int -> int -> int -> unit

    /// Run morsels of `h`, claimed one at a time from the shared counter, until none is left or
    /// one has failed: what every runner does, the caller included. Answers how many it ran.
    let internal drain (span: Span) (h: MorselHandOff) (control: int[]) : int =
        let f, env, t = MorselHandOff.rebuild h
        let resolved = MorselHandOff.resolve f env t
        let n = h.Rows.Length
        let mutable ran = 0
        let mutable going = true

        while going do
            if Atomic.load control 1 <> 0 then
                going <- false
            else
                let j = Atomic.fetchAdd control 0 1

                if j >= h.Last then
                    going <- false
                else
                    span f resolved h j (j * h.MorselRows) (min n ((j + 1) * h.MorselRows))
                    ran <- ran + 1

                    if h.OutFailed[j] <> 0uy then
                        Atomic.store control 1 1

        ran

    /// The worker's side with the morsel runner `span`: `serve`, for the laws' perturbed runners.
    let internal serveWith (span: Span) (post: obj -> unit) : obj -> unit =
        post (
            box
                { Ready = true
                  Job = -1
                  Faulted = false
                  Fault = "" }
        )

        fun (message: obj) ->
            let job = unbox<MorselJob> message

            let reply =
                try
                    let ran = drain span job.HandOff job.Control
                    Atomic.fetchAdd job.Control 3 ran |> ignore

                    { Ready = false
                      Job = job.Job
                      Faulted = false
                      Fault = "" }
                with e ->
                    { Ready = false
                      Job = job.Job
                      Faulted = true
                      Fault = e.Message }

            // The runner's last act on the shared words, so the caller's atomic read of them after
            // every reply sees every slot this runner wrote.
            Atomic.fetchAdd job.Control 2 1 |> ignore
            post (box reply)

    /// The worker's side (Phase 376). A worker's script calls this once with the function that posts
    /// a message back (`m => self.postMessage(m)` in a Web Worker, `m => parentPort.postMessage(m)`
    /// under node), and hands every message it receives to the function this answers
    /// (`self.onmessage = e => handle(e.data)`). The worker reports ready at once, then runs its share
    /// of each job and replies.
    let serve (post: obj -> unit) : obj -> unit = serveWith MorselHandOff.runSpan post

    let mutable private workerMorsels = 0

    /// The morsels the pool's workers (not the caller) have run since this realm loaded: the laws read
    /// it to show that the workers, and not the caller alone, answered. A function, not the mutable
    /// itself, because Fable reads a `let` bound to a mutable module value as an alias of it.
    let internal morselsByWorkers () : int = workerMorsels

    /// One opted-in pool: its workers, which of them are ready or retired, and the job in flight.
    [<AllowNullLiteral>]
    type private Pool
        (start: unit -> MorselWorker, size: int, minRows: int, morselRows: int, span: Span, callerDrains: bool) =
        let workers: MorselWorker[] = Array.zeroCreate size
        let ready: bool[] = Array.zeroCreate size
        let retired: bool[] = Array.zeroCreate size
        let busy: int[] = Array.zeroCreate 1
        let mutable started = false
        let mutable nextJob = 0
        // The job awaiting replies (`-1` for none), what a reply to it does, and who waits for the
        // workers to settle.
        let mutable awaiting = -1
        let mutable onReply: int -> MorselReply -> unit = fun _ _ -> ()
        let mutable onSettled: unit -> unit = fun () -> ()

        member _.MinRows = minRows
        member _.MorselRows = morselRows
        member _.Span = span

        /// A message from worker `w`.
        member _.Hear (w: int) (message: obj) : unit =
            if isNull message then
                retired[w] <- true

                if awaiting >= 0 then
                    onReply
                        w
                        { Ready = false
                          Job = awaiting
                          Faulted = true
                          Fault = "the worker failed" }
                else
                    onSettled ()
            else
                let reply = unbox<MorselReply> message

                if reply.Ready then
                    ready[w] <- true
                    onSettled ()
                elif reply.Job = awaiting then
                    onReply w reply

        /// Start the workers, once.
        member this.Start() : unit =
            if not started then
                started <- true

                for w in 0 .. size - 1 do
                    let worker = start ()
                    workers[w] <- worker
                    worker.Listen(this.Hear w)

        /// The workers that have reported ready and not been retired.
        member _.Live() : int list =
            [ for w in 0 .. size - 1 do
                  if ready[w] && not retired[w] then
                      yield w ]

        /// Has every worker reported ready, or been retired?
        member _.Settled() : bool =
            started && Array.forall2 (fun r d -> r || d) ready retired

        /// Call `k` whenever a worker reports ready or is retired.
        member _.WhenSettled(k: unit -> unit) : unit = onSettled <- k

        member _.TryAcquire() : bool = Atomic.fetchAdd busy 0 1 = 0
        member _.Release() : unit = Atomic.store busy 0 0

        member _.Stop() : unit =
            for w in 0 .. size - 1 do
                if not (isNull (box workers[w])) then
                    workers[w].Stop()

        /// Run every morsel of `h` across the workers `targets` and the caller; answers whether every
        /// runner finished its share (no worker faulted).
        member this.Run(h: MorselHandOff, targets: int list) : Async<bool> =
            Async.FromContinuations(fun (ok, _, _) ->
                let control = Shared.ints 4
                control[0] <- h.First
                // Every target and the caller; the last of them to finish settles the job. A worker
                // counts once: its reply, or its failure, whichever comes first.
                let remaining = [| List.length targets + 1 |]
                let pending: int[] = Array.zeroCreate size
                let faulted = [| 0 |]

                for w in targets do
                    pending[w] <- 1

                let settle () =
                    if Atomic.fetchAdd remaining 0 -1 = 1 then
                        awaiting <- -1
                        // The atomic read that orders every runner's writes before the caller's.
                        Atomic.load control 2 |> ignore
                        workerMorsels <- workerMorsels + Atomic.load control 3
                        ok (Atomic.load faulted 0 = 0)

                nextJob <- nextJob + 1
                let job = nextJob
                awaiting <- job

                onReply <-
                    fun w reply ->
                        if Atomic.fetchAdd pending w -1 = 1 then
                            if reply.Faulted then
                                retired[w] <- true
                                Atomic.store faulted 0 1
                                // A faulted runner may hold morsels it claimed and never ran: no
                                // runner claims another, and the step goes to the sequential member.
                                Atomic.store control 1 1

                            settle ()

                let message =
                    box
                        { Job = job
                          HandOff = h
                          Control = control }

                for w in targets do
                    workers[w].Post message

                if callerDrains then
                    drain this.Span h control |> ignore

                Atomic.fetchAdd control 2 1 |> ignore
                settle ())

    let mutable private current: Pool = null

    /// The pool's step: `Some` work for a row-local step it takes, `None` for one it leaves to the
    /// caller's thread.
    let private step (pool: Pool) (env: Map<string, Cell>) (f: Frame) (t: Transform) =
        if Frame.rows f < pool.MinRows then
            None
        else
            pool.Start()

            match pool.Live() with
            | [] -> None
            | targets ->
                match MorselHandOff.planAt pool.MorselRows env f t with
                | None -> None
                | Some h ->
                    Some(
                        async {
                            let! complete =
                                if pool.TryAcquire() then
                                    async {
                                        try
                                            return! pool.Run(h, targets)
                                        finally
                                            pool.Release()
                                    }
                                else
                                    // Another evaluation holds the pool: this one runs its morsels
                                    // on its own thread, through the same loop.
                                    async {
                                        let control = Shared.ints 4
                                        control[0] <- h.First
                                        drain pool.Span h control |> ignore
                                        return true
                                    }

                            if complete then
                                return MorselHandOff.finish env f t h
                            else
                                return DataFrame.evalStepWith Kernels.host DataFrame.noResolve env f t
                        }
                    )

    /// Stop the pool's workers and allocate as before; `evalToPreparedAsync` then runs every step on
    /// the caller's thread. Nothing, when the host has not opted in.
    let optOut () : unit =
        if not (isNull current) then
            current.Stop()
            current <- null
            DataFrame.pool <- None
            Shared.disable ()

    /// `optIn` with the pool's floor in rows, its morsel size, its morsel runner, and whether the caller
    /// drains morsels beside the workers: the laws run the pool over small frames and small morsels,
    /// with perturbed runners, and with the workers alone.
    let internal optInWith
        (start: unit -> MorselWorker)
        (workers: int)
        (minRows: int)
        (morselRows: int)
        (span: Span)
        (callerDrains: bool)
        : bool =
        if workers < 1 then
            invalidArg "workers" "a pool needs at least one worker"

        optOut ()

        if not (Shared.enable ()) then
            false
        else
            let pool = Pool(start, workers, minRows, morselRows, span, callerDrains)
            current <- pool

            DataFrame.pool <-
                Some
                    { new DataFrame.PoolHook with
                        member _.Step(env, f, t) = step pool env f t }

            true

    /// Opt this host in to the worker pool (Phase 376): `start` starts one worker running a script
    /// that calls `serve`, and the pool starts `workers` of them, lazily, on the first step it takes.
    /// Answers `false`, and changes nothing, where this realm has no shared memory — a page served
    /// without `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy:
    /// require-corp` — so a host may call it unconditionally. From `true` on, the frame allocates its
    /// vectors over shared memory and `DataFrame.evalToPreparedAsync` hands each eligible step of at
    /// least `PoolRows` rows to the pool; every answer is the sequential member's, byte for byte.
    /// Opting in again replaces the pool.
    let optIn (start: unit -> MorselWorker) (workers: int) : bool =
        optInWith start workers PoolRows Kernels.MorselRows MorselHandOff.runSpan true

    /// Has this host opted in, on a realm with shared memory?
    let isActive () : bool = not (isNull current)

    /// Start the pool's workers now rather than on the first step it takes, and complete once every
    /// one has reported ready or been retired. Completes at once when the host has not opted in.
    let warm () : Async<unit> =
        if isNull current then
            async { return () }
        else
            let pool = current
            pool.Start()

            if pool.Settled() then
                async { return () }
            else
                Async.FromContinuations(fun (ok, _, _) ->
                    pool.WhenSettled(fun () ->
                        if pool.Settled() then
                            pool.WhenSettled(fun () -> ())
                            ok ()))
