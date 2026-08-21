namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.DataFrame — the typed delta representation for the column layer
//  (Phase 98). A `TableDelta` says what changed in one columnar table between a
//  prior state and now: rows added / changed / removed **addressed by identity**,
//  columns invalidated, and `FullRefresh` as the honest top element — "everything
//  may have changed", the only truthful answer when the change cannot be located.
//
//  Deliberately separate from any evaluator. The representation is what an
//  event-driven refresh CARRIES instead of an opaque "source dirty" flag, so it
//  earns its keep before any incremental engine consumes it — and `Change`
//  (Phase 34), the coarse four-case predecessor, becomes a projection of it
//  (`Delta.toChange`) rather than a rival vocabulary.
//
//  Four properties are load-bearing, and each is pinned rather than asserted:
//
//   * IDENTITY, NOT ORDINALS. A row is named by the identity its source gives it;
//     an ordinal is used only where the source has no identity at all, and the two
//     addressing modes may not be mixed inside one delta (a checked invariant, not
//     a comment — see `DeltaDefect.MixedAddressing`). An ordinal names a position
//     in a projection; a key names the row.
//   * GENERIC OVER THE IDENTITY WITNESS. `RowIdentity<'Id>` is a per-call argument,
//     never a field on any core type and never a domain type: Core is told HOW to
//     key a row and knows nothing about what the key means. The columnar strand is
//     witness-free by design, so identity arrives the way `Propagation`'s
//     dependency relation does — as a function the caller supplies.
//   * COMPOSITION IS A REAL MONOID. `compose` is total and associative for EVERY
//     pair of inputs (not merely consistent ones), `FullRefresh` absorbs, and
//     `empty` is a two-sided identity within one identity scheme.
//   * TOTALITY. Nothing throws. A malformed or inconsistent delta is refused
//     WHOLE, with a typed defect naming what is wrong — never partially applied,
//     never silently repaired.
//
//  FSharp.Core only, Fable-clean.
// ============================================================================

/// How a delta addresses one row of a columnar table.
///
/// `ByKey` is the normal case: the identity the source gives the row, rendered to a string by the
/// caller's `RowIdentity` witness. `ByOrdinal` exists only for a source that has NO identity —
/// where the position is genuinely all there is to say. The two never mix inside one delta: a delta
/// declares its identity scheme, and `validate` refuses a ref that disagrees with it, so "ordinals
/// only where no identity exists" is enforced rather than hoped for.
type RowRef =
    | ByKey of key: string
    | ByOrdinal of index: int

/// What happened to one row across the window a delta describes.
///
/// The first three are the obvious ones. `RowTransient` is the fourth because composition needs it
/// and because it is genuinely informative: a row that was added and then removed inside the
/// composed window is absent at BOTH ends, so a consumer rebuilding the table does nothing — but a
/// consumer holding a per-row cache keyed by identity must still evict that key, and a consumer
/// watching for identity reuse must still see that the key was live. Collapsing it to "no change"
/// would throw that away, and would also break associativity: `Added ∘ Removed` has to land
/// somewhere whose before-state is absent and whose after-state is absent, and none of the other
/// three cases is that shape.
type RowChange =
    /// Absent before, present now.
    | RowAdded
    /// Present before and now, with at least one cell different.
    | RowChanged
    /// Present before, absent now.
    | RowRemoved
    /// Absent at both ends, but live somewhere inside the window (only ever produced by composing).
    | RowTransient

/// A row- and column-addressed change set over one table.
///
/// `Scheme` names the identity scheme the keys were minted under — two deltas built under different
/// schemes describe incomparable key spaces and may not be composed precisely (see `Delta.compose`).
/// `Rows` is canonically ordered (keys ordinally, then ordinals ascending) so the wire is
/// byte-stable and composition is order-independent. `InvalidatedColumns` names columns whose values
/// can no longer be trusted WITHOUT naming rows — the honest shape for "this column was recomputed"
/// or "this column's source moved", where the affected row set is unknown or is all of them.
type RowSetDelta =
    { Scheme: string
      Rows: (RowRef * RowChange) list
      InvalidatedColumns: string list }

/// A typed description of what changed in one columnar table.
///
/// `FullRefresh` is the top element, and it is a first-class answer rather than a failure: a
/// structural schema change, a wholesale replacement, or a change whose location is simply not known
/// IS "everything may have changed", and saying so precisely is better than a `RowSet` that quietly
/// under-reports. It absorbs under composition, which is what makes it a top rather than merely a
/// large delta.
type TableDelta =
    | FullRefresh
    | RowSet of RowSetDelta

/// Why a delta was refused — recoverable + enumerated (GP4/GP5), never a throw, never a partial
/// application. Every case names the offending value.
type DeltaDefect =
    /// A delta must name the identity scheme its keys were minted under; the empty string names none.
    | EmptyScheme
    /// A `ByKey` ref carrying the empty string — not an identity.
    | EmptyRowKey
    /// A `ByOrdinal` ref below zero — no row has a negative position.
    | NegativeOrdinal of index: int
    /// The same row is named more than once inside one delta (the token is `k:<key>` / `o:<index>`).
    | DuplicateRow of row: string
    /// A ref whose addressing mode disagrees with the delta's scheme: an ordinal ref in an
    /// identity-bearing scheme, or a key ref in the reserved `ordinal` scheme. Ordinals are for
    /// sources with no identity, and a delta is one or the other.
    | MixedAddressing of scheme: string * row: string
    /// An invalidated-column entry that is the empty string.
    | EmptyColumnName
    /// The same column named twice in the invalidation list.
    | DuplicateInvalidatedColumn of name: string
    /// Two deltas composed across different identity schemes — their keys are not comparable, so no
    /// precise composite exists. (`Delta.compose` degrades to `FullRefresh`, the honest top;
    /// `Delta.composeChecked` refuses with this.)
    | SchemeMismatch of left: string * right: string
    /// The identity witness returned no key for a row while diffing — a source that cannot key every
    /// row cannot be diffed by identity at all (use the ordinal diff, deliberately).
    | MissingIdentity of scheme: string * index: int
    /// The identity witness returned the same key for two rows — the keys are not identities.
    | DuplicateIdentity of scheme: string * key: string

/// How a delta learns a row's identity — the per-call witness (never a field on a core type, never a
/// domain type). `Scheme` names the keying rule so two deltas minted under different rules are never
/// composed as if their keys meant the same thing; `KeyOf` reads the identity of the row at an index
/// (`None` = this source has no identity for that row); `KeyString` renders it to the canonical
/// string form the delta and its wire carry, playing the role `IdWitness.ToString` plays in the tree
/// strand without dragging the tree witness into the witness-free columnar strand.
type RowIdentity<'Id> =
    { Scheme: string
      KeyOf: Table -> int -> 'Id option
      KeyString: 'Id -> string }

/// Reference identity witnesses — enough to exercise the whole delta surface with no domain
/// dependency of any kind.
[<RequireQualifiedAccess>]
module RowIdentity =

    /// The reserved scheme name for an identity-free source, whose deltas address rows by ordinal.
    /// It is a reserved WORD, not a witness: a source with real identity must never claim it, and a
    /// delta under it must use `ByOrdinal` refs exclusively (`validate` enforces both directions).
    let ordinalScheme = "ordinal"

    /// Identity is the value of one named column — the everyday case (a primary key column). A row
    /// whose key cell is `Null`, or whose key column is absent, has NO identity (`Null` is the
    /// validity mask's "absent", and absence is not an identity); such a source is a `MissingIdentity`
    /// defect under `Delta.diff` rather than a silently-ordinal fallback.
    ///
    /// The key string is the pinned `DataFrame.cellToken`, so a float key groups exactly as
    /// `GroupBy` / `Distinct` group it and two hosts mint the same key for the same cell.
    let byColumn (column: string) : RowIdentity<Cell> =
        { Scheme = "column:" + column
          KeyOf =
            fun t i ->
                match Table.tryColumn column t with
                | Some c when i >= 0 && i < List.length c.Cells ->
                    match Column.cell i c with
                    | Null -> None
                    | cell -> Some cell
                | _ -> None
          KeyString = DataFrame.cellToken }

    /// Identity is the tuple of several named columns — the composite-key case. Any `Null` component
    /// makes the row identity-free, for the same reason as `byColumn`.
    let byColumns (columns: string list) : RowIdentity<Cell list> =
        { Scheme = "columns:" + String.concat "," columns
          KeyOf =
            fun t i ->
                let cells =
                    columns
                    |> List.map (fun n ->
                        match Table.tryColumn n t with
                        | Some c when i >= 0 && i < List.length c.Cells -> Column.cell i c
                        | _ -> Null)

                if List.isEmpty cells || cells |> List.exists Cell.isNull then
                    None
                else
                    Some cells
          KeyString = DataFrame.rowTokenString }

/// The delta algebra: construction, validation, composition, and the projections a consumer reads.
[<RequireQualifiedAccess>]
module Delta =

    // ---- canonical form ----

    /// The stable token identifying a row ref (also the string a defect names it by).
    let refToken (r: RowRef) : string =
        match r with
        | ByKey k -> "k:" + k
        | ByOrdinal i -> "o:" + string i

    /// The canonical order of row refs: keys first (ordinally), then ordinals ascending. Ordinal
    /// string comparison, not culture-aware — the same cross-host pin the rest of the strand uses.
    let private compareRef (a: RowRef) (b: RowRef) : int =
        match a, b with
        | ByKey x, ByKey y -> System.String.CompareOrdinal(x, y)
        | ByOrdinal x, ByOrdinal y -> compare x y
        | ByKey _, ByOrdinal _ -> -1
        | ByOrdinal _, ByKey _ -> 1

    let private sortRows (rows: (RowRef * RowChange) list) : (RowRef * RowChange) list =
        rows |> List.sortWith (fun (a, _) (b, _) -> compareRef a b)

    let private sortColumns (cols: string list) : string list =
        cols |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))

    /// Put a delta in canonical form: rows and invalidated columns in the pinned order. Sorting is
    /// STABLE and nothing is deduplicated — a duplicate is a defect for `validate` to name, not
    /// something normalisation should quietly erase. `compose` and the codec both normalise, so the
    /// only way to hold a non-canonical delta is to hand-build the record.
    let normalise (d: TableDelta) : TableDelta =
        match d with
        | FullRefresh -> FullRefresh
        | RowSet r ->
            RowSet
                { r with
                    Rows = sortRows r.Rows
                    InvalidatedColumns = sortColumns r.InvalidatedColumns }

    // ---- construction ----

    /// The quiet delta for a scheme — nothing changed. The two-sided identity of `compose` within
    /// that scheme.
    let empty (scheme: string) : TableDelta =
        RowSet
            { Scheme = scheme
              Rows = []
              InvalidatedColumns = [] }

    /// A delta over explicit row refs (canonicalised; NOT validated — pass it through `validate` or
    /// `composeChecked` where the refs came from outside).
    let ofRows (scheme: string) (rows: (RowRef * RowChange) list) : TableDelta =
        RowSet
            { Scheme = scheme
              Rows = sortRows rows
              InvalidatedColumns = [] }

    /// A column-invalidation delta: these columns' values can no longer be trusted, and the delta
    /// deliberately says nothing about which rows.
    let ofColumns (scheme: string) (columns: string list) : TableDelta =
        RowSet
            { Scheme = scheme
              Rows = []
              InvalidatedColumns = sortColumns columns }

    // ---- projections ----

    let isFullRefresh (d: TableDelta) : bool =
        match d with
        | FullRefresh -> true
        | RowSet _ -> false

    /// Does this delta assert that nothing changed? `FullRefresh` never is (it asserts the opposite).
    let isQuiet (d: TableDelta) : bool =
        match d with
        | FullRefresh -> false
        | RowSet r -> List.isEmpty r.Rows && List.isEmpty r.InvalidatedColumns

    /// The row set, when the delta has one. `None` for `FullRefresh` — deliberately an option rather
    /// than an empty list, because "names no rows" and "names every row" must not read alike.
    let tryRowSet (d: TableDelta) : RowSetDelta option =
        match d with
        | FullRefresh -> None
        | RowSet r -> Some r

    /// The refs carrying a given change, in canonical order (empty for `FullRefresh` — check
    /// `isFullRefresh` first).
    let rowsWith (change: RowChange) (d: TableDelta) : RowRef list =
        match d with
        | FullRefresh -> []
        | RowSet r -> r.Rows |> List.filter (fun (_, c) -> c = change) |> List.map fst

    // ---- validation (GP4/GP5 — total, enumerating, whole-delta) ----

    /// Every defect in a delta, in a stable order. Empty ⇒ the delta is well-formed.
    let defects (d: TableDelta) : DeltaDefect list =
        match d with
        | FullRefresh -> []
        | RowSet r ->
            let schemeFaults = if r.Scheme = "" then [ EmptyScheme ] else []

            let isOrdinalScheme = r.Scheme = RowIdentity.ordinalScheme

            let refFaults =
                r.Rows
                |> List.collect (fun (ref, _) ->
                    let shape =
                        match ref with
                        | ByKey "" -> [ EmptyRowKey ]
                        | ByKey _ -> []
                        | ByOrdinal i when i < 0 -> [ NegativeOrdinal i ]
                        | ByOrdinal _ -> []

                    let addressing =
                        match ref with
                        | ByKey _ when isOrdinalScheme -> [ MixedAddressing(r.Scheme, refToken ref) ]
                        | ByOrdinal _ when not isOrdinalScheme -> [ MixedAddressing(r.Scheme, refToken ref) ]
                        | _ -> []

                    shape @ addressing)

            let dupRows =
                r.Rows
                |> List.map (fst >> refToken)
                |> List.countBy id
                |> List.filter (fun (_, n) -> n > 1)
                |> List.map (fst >> DuplicateRow)

            let colFaults =
                (if r.InvalidatedColumns |> List.exists (fun c -> c = "") then
                     [ EmptyColumnName ]
                 else
                     [])
                @ (r.InvalidatedColumns
                   |> List.countBy id
                   |> List.filter (fun (_, n) -> n > 1)
                   |> List.map (fst >> DuplicateInvalidatedColumn))

            schemeFaults @ refFaults @ dupRows @ colFaults

    /// Accept a well-formed delta, or refuse it WHOLE with its first defect. Never partial.
    let validate (d: TableDelta) : Result<TableDelta, DeltaDefect> =
        match defects d with
        | [] -> Ok d
        | first :: _ -> Error first

    /// A stable human string for a defect.
    let defectString (e: DeltaDefect) : string =
        match e with
        | EmptyScheme -> "a delta must name its identity scheme"
        | EmptyRowKey -> "a row key is the empty string"
        | NegativeOrdinal i -> "negative row ordinal: " + string i
        | DuplicateRow r -> "row named more than once: " + r
        | MixedAddressing(scheme, r) ->
            "row "
            + r
            + " disagrees with the delta's addressing scheme '"
            + scheme
            + "' (ordinals only where no identity exists)"
        | EmptyColumnName -> "an invalidated column name is the empty string"
        | DuplicateInvalidatedColumn n -> "column invalidated more than once: " + n
        | SchemeMismatch(l, r) -> "cannot compose deltas across identity schemes '" + l + "' and '" + r + "'"
        | MissingIdentity(scheme, i) -> "scheme '" + scheme + "' gives row " + string i + " no identity"
        | DuplicateIdentity(scheme, k) -> "scheme '" + scheme + "' gives two rows the identity " + k

    // ---- composition ----

    // A row change is exactly a (before-exists, after-exists) pair, and composing two of them is
    // relational composition: take the FIRST's before and the SECOND's after. That is associative by
    // construction — `pre (compose a b) = pre a` and `post (compose a b) = post b` hold for all four
    // cases, so `(a ∘ b) ∘ c` and `a ∘ (b ∘ c)` are both `classify (pre a) (post c)`. It is why
    // `RowTransient` exists: without a case for (absent, absent), `Added ∘ Removed` has nowhere
    // truthful to land and the algebra stops being associative on exactly the inputs a
    // re-add-then-drop sequence produces.
    let private pre (c: RowChange) : bool =
        match c with
        | RowChanged
        | RowRemoved -> true
        | RowAdded
        | RowTransient -> false

    let private post (c: RowChange) : bool =
        match c with
        | RowAdded
        | RowChanged -> true
        | RowRemoved
        | RowTransient -> false

    let private classify (before: bool) (after: bool) : RowChange =
        match before, after with
        | false, true -> RowAdded
        | true, true -> RowChanged
        | true, false -> RowRemoved
        | false, false -> RowTransient

    /// Compose two row changes over the same row: `a` happened, then `b`.
    let composeChange (a: RowChange) (b: RowChange) : RowChange = classify (pre a) (post b)

    /// Compose two deltas: `a` describes the earlier window, `b` the later one, and the result
    /// describes both as one. Total and associative for every pair.
    ///
    /// `FullRefresh` absorbs on both sides. Two deltas from DIFFERENT identity schemes also compose
    /// to `FullRefresh` — not as a failure but as the only truthful answer: their keys name different
    /// things, so no precise composite exists, and the top element is exactly the value that says
    /// "everything may have changed". A caller that wants that refused instead of degraded uses
    /// `composeChecked`.
    let compose (a: TableDelta) (b: TableDelta) : TableDelta =
        match a, b with
        | FullRefresh, _
        | _, FullRefresh -> FullRefresh
        | RowSet x, RowSet y when x.Scheme <> y.Scheme -> FullRefresh
        | RowSet x, RowSet y ->
            let merged =
                (Map.empty, x.Rows @ y.Rows)
                ||> List.fold (fun acc (ref, change) ->
                    let token = refToken ref

                    match Map.tryFind token acc with
                    | Some(ref0, change0) -> Map.add token (ref0, composeChange change0 change) acc
                    | None -> Map.add token (ref, change) acc)

            RowSet
                { Scheme = x.Scheme
                  Rows = merged |> Map.toList |> List.map snd |> sortRows
                  InvalidatedColumns = (x.InvalidatedColumns @ y.InvalidatedColumns) |> List.distinct |> sortColumns }

    /// `compose`, refusing rather than degrading: both operands must be well-formed and share an
    /// identity scheme. Whole-or-nothing (GP4) — a defect in either operand yields no composite.
    let composeChecked (a: TableDelta) (b: TableDelta) : Result<TableDelta, DeltaDefect> =
        validate a
        |> Result.bind (fun _ -> validate b)
        |> Result.bind (fun _ ->
            match a, b with
            | RowSet x, RowSet y when x.Scheme <> y.Scheme -> Error(SchemeMismatch(x.Scheme, y.Scheme))
            | _ -> Ok(compose a b))

    /// Fold a sequence of deltas (earliest first) into one, starting from the quiet delta of `scheme`.
    let composeAll (scheme: string) (deltas: TableDelta list) : TableDelta =
        deltas |> List.fold compose (empty scheme)

    // ---- the Phase 34 `Change` bridge ----

    /// Lift the coarse `Change` (Phase 34) into a delta. Only `ColumnValuesChanged` carries locatable
    /// information — one column invalidated, rows unknown — so it is the only case that yields a
    /// `RowSet`. `RowsAppended` names no count and no identity, and a schema change is structural:
    /// both are honestly `FullRefresh` rather than a `RowSet` that pretends to more precision than
    /// the source had. (This is also exactly what `DataFrame.evalFrom` already does with them.)
    let ofChange (scheme: string) (change: Change) : TableDelta =
        match change with
        | ColumnValuesChanged c -> ofColumns scheme [ c ]
        | RowsAppended
        | SchemaChanged _
        | FullChange -> FullRefresh

    /// Project a delta back onto the coarse `Change`, for a consumer still on that vocabulary
    /// (`DataFrame.evalFrom`). `None` means the delta asserts nothing changed — a statement `Change`
    /// has no case for, which is why this returns an option rather than inventing one. Anything the
    /// four cases cannot express precisely projects to `FullChange`: the projection is CONSERVATIVE
    /// by construction, so a consumer that acts on it recomputes too much, never too little.
    let toChange (d: TableDelta) : Change option =
        match d with
        | FullRefresh -> Some FullChange
        | RowSet r ->
            match r.Rows, r.InvalidatedColumns with
            | [], [] -> None
            | [], [ one ] -> Some(ColumnValuesChanged one)
            | _ -> Some FullChange

    // ---- diffing two tables (the reference producer) ----

    let private rowCells (t: Table) (i: int) : Cell list =
        t.Schema
        |> List.map (fun (name, _) ->
            match Table.tryColumn name t with
            | Some c -> Column.cell i c
            | None -> Null)

    let private rowContentToken (t: Table) (i: int) : string = DataFrame.rowTokenString (rowCells t i)

    /// Index a table's rows by identity, refusing whole if the witness cannot key every row uniquely.
    let private keyIndex (idw: RowIdentity<'Id>) (t: Table) : Result<(string * int) list, DeltaDefect> =
        let n = Table.rowCount t

        let rec go i acc (seen: Set<string>) =
            if i >= n then
                Ok(List.rev acc)
            else
                match idw.KeyOf t i with
                | None -> Error(MissingIdentity(idw.Scheme, i))
                | Some id ->
                    let k = idw.KeyString id

                    if Set.contains k seen then
                        Error(DuplicateIdentity(idw.Scheme, k))
                    else
                        go (i + 1) ((k, i) :: acc) (Set.add k seen)

        go 0 [] Set.empty

    /// The delta from `before` to `after`, addressed by the witness's identity.
    ///
    /// A schema difference is `FullRefresh` — the column set moved, so a row-addressed answer would
    /// be describing two different shapes as if they were one. Otherwise every key is classified by
    /// presence, and a key present in both is `RowChanged` iff its cells differ under the pinned
    /// canonical token (the same rule `Distinct` / `Intersect` compare rows by, so a float that
    /// groups equal also diffs equal, on every host).
    let diff (idw: RowIdentity<'Id>) (before: Table) (after: Table) : Result<TableDelta, DeltaDefect> =
        if before.Schema <> after.Schema then
            Ok FullRefresh
        else
            keyIndex idw before
            |> Result.bind (fun beforeKeys ->
                keyIndex idw after
                |> Result.map (fun afterKeys ->
                    let beforeMap = Map.ofList beforeKeys
                    let afterMap = Map.ofList afterKeys

                    let fromAfter =
                        afterKeys
                        |> List.choose (fun (k, ai) ->
                            match Map.tryFind k beforeMap with
                            | None -> Some(ByKey k, RowAdded)
                            | Some bi ->
                                if rowContentToken before bi = rowContentToken after ai then
                                    None // present at both ends, byte-identical content — not a change
                                else
                                    Some(ByKey k, RowChanged))

                    let removed =
                        beforeKeys
                        |> List.filter (fun (k, _) -> not (Map.containsKey k afterMap))
                        |> List.map (fun (k, _) -> ByKey k, RowRemoved)

                    RowSet
                        { Scheme = idw.Scheme
                          Rows = sortRows (fromAfter @ removed)
                          InvalidatedColumns = [] }))

    /// The delta from `before` to `after` for a source with NO identity — rows compared by position,
    /// under the reserved `ordinal` scheme. This is the deliberate fallback, not a default: a
    /// positional diff calls an insert at the front a change to every row after it, which is exactly
    /// why identity is preferred wherever it exists.
    let diffByOrdinal (before: Table) (after: Table) : TableDelta =
        if before.Schema <> after.Schema then
            FullRefresh
        else
            let nb = Table.rowCount before
            let na = Table.rowCount after
            let shared = min nb na

            let changed =
                [ for i in 0 .. shared - 1 do
                      if rowContentToken before i <> rowContentToken after i then
                          ByOrdinal i, RowChanged ]

            let added = [ for i in shared .. na - 1 -> ByOrdinal i, RowAdded ]
            let removed = [ for i in shared .. nb - 1 -> ByOrdinal i, RowRemoved ]

            RowSet
                { Scheme = RowIdentity.ordinalScheme
                  Rows = sortRows (changed @ added @ removed)
                  InvalidatedColumns = [] }

    /// Resolve the delta's PRESENT rows (`RowAdded` / `RowChanged`) to their indexes in `t` — what an
    /// incremental consumer recomputes. `RowRemoved` / `RowTransient` rows are absent by definition
    /// and resolve to nothing. `None` for `FullRefresh`: every row is affected, and an index list
    /// would be a worse way to say so. Whole-or-nothing — a witness that cannot key `t` yields a
    /// defect, never a partial list.
    let resolve (idw: RowIdentity<'Id>) (t: Table) (d: TableDelta) : Result<int list option, DeltaDefect> =
        match d with
        | FullRefresh -> Ok None
        | RowSet r ->
            if r.Scheme = RowIdentity.ordinalScheme then
                let n = Table.rowCount t

                Ok(
                    Some(
                        r.Rows
                        |> List.choose (fun (ref, c) ->
                            match ref, c with
                            | ByOrdinal i, (RowAdded | RowChanged) when i >= 0 && i < n -> Some i
                            | _ -> None)
                        |> List.distinct
                        |> List.sort
                    )
                )
            elif r.Scheme <> idw.Scheme then
                Error(SchemeMismatch(r.Scheme, idw.Scheme))
            else
                keyIndex idw t
                |> Result.map (fun keys ->
                    let index = Map.ofList keys

                    Some(
                        r.Rows
                        |> List.choose (fun (ref, c) ->
                            match ref, c with
                            | ByKey k, (RowAdded | RowChanged) -> Map.tryFind k index
                            | _ -> None)
                        |> List.distinct
                        |> List.sort
                    ))

/// The canonical wire codec for `TableDelta` — `"$type"`-tagged objects (the `Fuaran.Core` envelope
/// discipline), rendered through `Canon` so keys are Ordinal-sorted and the bytes are identical on
/// every host. Decode carries the columnar strand's six-code `ColumnError` envelope, and refuses a
/// structurally-decodable but INCONSISTENT delta as `Malformed`, naming the defect — a delta that
/// decodes into something `validate` would reject is not a delta. Fable-clean.
module DeltaCodec =

    let private changeTag (c: RowChange) : string =
        match c with
        | RowAdded -> "added"
        | RowChanged -> "changed"
        | RowRemoved -> "removed"
        | RowTransient -> "transient"

    let private allChangeTags = [ "added"; "changed"; "removed"; "transient" ]

    let private changeOfTag (s: string) : RowChange option =
        match s with
        | "added" -> Some RowAdded
        | "changed" -> Some RowChanged
        | "removed" -> Some RowRemoved
        | "transient" -> Some RowTransient
        | _ -> None

    let private rowToJson (ref: RowRef, change: RowChange) : JVal =
        match ref with
        | ByKey k -> Canon.typed (changeTag change) [ "key", JStr k ]
        | ByOrdinal i -> Canon.typed (changeTag change) [ "ordinal", JInt i ]

    /// Encode a delta to a `JVal`. Normalises first, so the bytes are canonical whatever order the
    /// caller built the record in.
    let encodeJson (d: TableDelta) : JVal =
        match Delta.normalise d with
        | FullRefresh -> Canon.typed "fullRefresh" []
        | RowSet r ->
            Canon.typed
                "rowSet"
                [ "scheme", JStr r.Scheme
                  "rows", JArr(r.Rows |> List.map rowToJson)
                  "columns", JArr(r.InvalidatedColumns |> List.map JStr) ]

    /// The canonical wire string for a delta (Ordinal-sorted keys + the cross-host float layout —
    /// byte-identical across hosts).
    let encode (d: TableDelta) : string = Canon.render (encodeJson d)

    // ---- decode ----

    let private field (k: string) (el: JVal) : Result<JVal, ColumnError> =
        match el with
        | JObj fields ->
            match fields |> List.tryFind (fun (n, _) -> n = k) with
            | Some(_, v) -> Ok v
            | None -> Error(MissingField k)
        | _ -> Error(MalformedShape("expected an object carrying field '" + k + "'"))

    let private tryField (k: string) (el: JVal) : JVal option =
        match el with
        | JObj fields -> fields |> List.tryFind (fun (n, _) -> n = k) |> Option.map snd
        | _ -> None

    let private strOf (el: JVal) : Result<string, ColumnError> =
        match el with
        | JStr s -> Ok s
        | _ -> Error(MalformedShape "expected a string")

    let private intOf (el: JVal) : Result<int, ColumnError> =
        match el with
        | JInt i -> Ok i
        | _ -> Error(MalformedShape "expected an int")

    let private arrOf (el: JVal) : Result<JVal list, ColumnError> =
        match el with
        | JArr xs -> Ok xs
        | _ -> Error(MalformedShape "expected an array")

    let private mapM (f: 'a -> Result<'b, ColumnError>) (xs: 'a list) : Result<'b list, ColumnError> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | x :: rest -> f x |> Result.bind (fun v -> go (v :: acc) rest)

        go [] xs

    let private rowOfJson (el: JVal) : Result<RowRef * RowChange, ColumnError> =
        field "$type" el
        |> Result.bind strOf
        |> Result.bind (fun tag ->
            match changeOfTag tag with
            | None -> Error(UnknownType(tag, allChangeTags))
            | Some change ->
                match tryField "key" el, tryField "ordinal" el with
                | Some _, Some _ ->
                    Error(MalformedShape "a row entry names both 'key' and 'ordinal'; exactly one addresses a row")
                | Some k, None -> strOf k |> Result.map (fun s -> ByKey s, change)
                | None, Some o -> intOf o |> Result.map (fun i -> ByOrdinal i, change)
                | None, None -> Error(MissingField "key"))

    /// Decode a delta from a `JVal`. A structurally-valid but inconsistent delta (duplicate rows,
    /// mixed addressing, an unnamed scheme) is refused as `Malformed` naming the defect — the wire
    /// carries no delta the in-memory algebra would reject.
    let decodeJson (el: JVal) : Result<TableDelta, ColumnError> =
        field "$type" el
        |> Result.bind strOf
        |> Result.bind (fun tag ->
            match tag with
            | "fullRefresh" -> Ok FullRefresh
            | "rowSet" ->
                field "scheme" el
                |> Result.bind strOf
                |> Result.bind (fun scheme ->
                    field "rows" el
                    |> Result.bind arrOf
                    |> Result.bind (mapM rowOfJson)
                    |> Result.bind (fun rows ->
                        field "columns" el
                        |> Result.bind arrOf
                        |> Result.bind (mapM strOf)
                        |> Result.map (fun cols ->
                            RowSet
                                { Scheme = scheme
                                  Rows = rows
                                  InvalidatedColumns = cols })))
            | other -> Error(UnknownType(other, [ "fullRefresh"; "rowSet" ])))
        |> Result.bind (fun d ->
            match Delta.validate d with
            | Ok _ -> Ok(Delta.normalise d)
            | Error defect -> Error(Malformed(Delta.defectString defect)))

    /// Decode a delta from a wire string.
    let decode (s: string) : Result<TableDelta, ColumnError> =
        match Json.parse s with
        | Error m -> Error(NotJson m)
        | Ok el -> decodeJson el
