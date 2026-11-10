namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Fuaran.Compute.Syntax (Phase 626, `DECISIONS.md` D67) — authoring helpers
//  for hand-written `ColExpr`, opt-in by `open Fuaran.Compute.Syntax`.
//
//  Every helper returns exactly the node its constructor builds and nothing
//  else: no new node, no new semantics, no evaluation. A consumer that does not
//  open the module cannot observe it. FSharp.Core only, Fable-clean.
//
//  The naming rule (D67): a helper takes the plain name of what it builds,
//  lower-cased (`col`, `param`, `cast`, `coalesce`); where that name is already
//  an FSharp.Core function or an F# keyword (`not`, `isNull`, `and`, `or`), it
//  takes a trailing prime (`not'`, `isNull'`, `and'`, `or'`), so an `open`
//  never captures a name FSharp.Core binds. A literal helper names the `Cell`
//  case it wraps (`litInt`, `litStr`), because `int`, `float` and `string` are
//  FSharp.Core conversion functions.
//
//  The operators are the `BinOp` arithmetic and comparison cases, each the F#
//  operator of the same meaning behind a leading `.`. F# ignores that `.` when
//  it decides precedence, so `.*` binds as `*`, `.+` as `+` and `.>` as `>`.
//  `And` and `Or` are NOT operators: `.&&` and `.||` are read as `&op` and
//  `|op`, which F# places at the COMPARISON level, so `a .> b .&& c .< d`
//  would build `((a > b) && c) < d` — a well-typed, wrong tree. They are the
//  functions `and'` and `or'` instead (D67).
// ============================================================================

/// Authoring helpers for `ColExpr` (Phase 626). Opt-in: `open Fuaran.Compute.Syntax`.
module Syntax =

    // ---- leaves ----

    /// `Col name`.
    let col (name: string) : ColExpr = Col name

    /// `Param name`.
    let param (name: string) : ColExpr = Param name

    /// `Lit cell`.
    let lit (cell: Cell) : ColExpr = Lit cell

    /// `Lit(Int v)`.
    let litInt (v: int) : ColExpr = Lit(Int v)

    /// `Lit(Float v)`.
    let litFloat (v: float) : ColExpr = Lit(Float v)

    /// `Lit(Bool v)`.
    let litBool (v: bool) : ColExpr = Lit(Bool v)

    /// `Lit(Str v)`.
    let litStr (v: string) : ColExpr = Lit(Str v)

    /// `Lit(Date text)`: the text as given, which must be canonical `YYYY-MM-DD`.
    let litDate (text: string) : ColExpr = Lit(Date text)

    /// `Lit(Timestamp text)`: the text as given, which must be a canonical instant.
    let litTimestamp (text: string) : ColExpr = Lit(Timestamp text)

    /// `Lit(Decimal text)`: the text as given, as the constructor takes it. `Cell.decimal`
    /// canonicalises a text first, where a caller needs that.
    let litDecimal (text: string) : ColExpr = Lit(Decimal text)

    /// `Lit Null`.
    let litNull: ColExpr = Lit Null

    /// `Now grain`.
    let now (grain: NowGrain) : ColExpr = Now grain

    // ---- the BinOp operators: arithmetic, then comparison ----

    /// `Binary(Add, a, b)`.
    let (.+) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Add, a, b)

    /// `Binary(Sub, a, b)`.
    let (.-) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Sub, a, b)

    /// `Binary(Mul, a, b)`.
    let (.*) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Mul, a, b)

    /// `Binary(Div, a, b)`.
    let (./) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Div, a, b)

    /// `Binary(Mod, a, b)`.
    let (.%) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Mod, a, b)

    /// `Binary(Eq, a, b)`.
    let (.=) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Eq, a, b)

    /// `Binary(Ne, a, b)`.
    let (.<>) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Ne, a, b)

    /// `Binary(Lt, a, b)`.
    let (.<) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Lt, a, b)

    /// `Binary(Le, a, b)`.
    let (.<=) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Le, a, b)

    /// `Binary(Gt, a, b)`.
    let (.>) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Gt, a, b)

    /// `Binary(Ge, a, b)`.
    let (.>=) (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Ge, a, b)

    // ---- the logical pair, as functions (see the header: an operator would bind wrongly) ----

    /// `Binary(And, a, b)`.
    let and' (a: ColExpr) (b: ColExpr) : ColExpr = Binary(And, a, b)

    /// `Binary(Or, a, b)`.
    let or' (a: ColExpr) (b: ColExpr) : ColExpr = Binary(Or, a, b)

    // ---- the ordinal predicates ----

    /// `Binary(Contains, subject, needle)`.
    let contains (subject: ColExpr) (needle: ColExpr) : ColExpr = Binary(Contains, subject, needle)

    /// `Binary(StartsWith, subject, prefix)`.
    let startsWith (subject: ColExpr) (prefix: ColExpr) : ColExpr = Binary(StartsWith, subject, prefix)

    /// `Binary(EndsWith, subject, suffix)`.
    let endsWith (subject: ColExpr) (suffix: ColExpr) : ColExpr = Binary(EndsWith, subject, suffix)

    // ---- the other named constructors ----

    /// `Not e`.
    let not' (e: ColExpr) : ColExpr = Not e

    /// `IsNull e`.
    let isNull' (e: ColExpr) : ColExpr = IsNull e

    /// `Coalesce es`.
    let coalesce (es: ColExpr list) : ColExpr = Coalesce es

    /// `InList(e, items)`.
    let inList (e: ColExpr) (items: ColExpr list) : ColExpr = InList(e, items)

    /// `InParam(e, name)`.
    let inParam (e: ColExpr) (name: string) : ColExpr = InParam(e, name)

    /// `Cast(ty, e)`.
    let cast (ty: ColumnType) (e: ColExpr) : ColExpr = Cast(ty, e)
