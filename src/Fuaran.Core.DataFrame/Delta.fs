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
///
/// Since Phase 273 the scheme is also what licenses reusing keys: `Delta.diff` and the incremental
/// seam remember the keys they mint for a table object under a scheme, and read them back when they
/// meet that same object under that same scheme — so two witnesses sharing a `Scheme` must key every
/// table identically, which is what naming the keying rule already meant.
type RowIdentity<'Id> =
    { Scheme: string
      KeyOf: Table -> int -> 'Id option
      KeyString: 'Id -> string }

/// Phase 283 — the typed equality a witness's `KeyString` DECLARES it agrees with, so a diff can
/// pair rows by `'Id` and render a key string only for the rows its delta carries.
///
/// A declaration is a claim about ONE `KeyString` function: `Equals a b` holds exactly when
/// `KeyString a = KeyString b`, and equal ids hash alike. That is the only thing that makes the typed
/// pairing answer what the string pairing answers — the same deltas, the same refusals in the same
/// order, the same `DuplicateIdentity` payload — so it is declared, never inferred. F# structural
/// equality on `'Id` is NOT such a claim: a witness may render two structurally distinct ids to one
/// string (a case-folding key, a rounding one), and pairing those by structural equality would miss a
/// `DuplicateIdentity` the string path reports.
///
/// Keyed by the WITNESS record object (weakly, by reference): the claim is about that record's
/// `KeyString`, and a record's fields cannot change. Any copy (`{ w with … }` — a counting wrapper,
/// a renamed scheme) is a new object with no declaration, and takes the string path, which is always
/// correct. The key is the record and not the function object on purpose: the compiler may re-create
/// a function value it can see the definition of (it did, in a Release build), so a function's
/// identity is not something a declaration can rely on; a record allocation is never duplicated. A
/// witness nothing declared for takes the string path, exactly as before this phase.
///
/// Phase 284 — the public route in is `RowIdentity.withKeyEquality`, which declares on a FRESH copy
/// of the witness it is given; the reference witnesses are declared through it too, so there is one
/// route in, not two.
[<RequireQualifiedAccess>]
module internal KeyEqualities =

    let private byWitness =
        System.Runtime.CompilerServices.ConditionalWeakTable<obj, obj>()

    /// Declare that `equality` agrees with `idw.KeyString` (see the module comment for the obligation),
    /// and return the witness.
    let declare
        (equality: System.Collections.Generic.IEqualityComparer<'Id>)
        (idw: RowIdentity<'Id>)
        : RowIdentity<'Id> =
        byWitness.AddOrUpdate(box idw, box equality)
        idw

    /// The equality declared for this very witness record, if any.
    let tryOf (idw: RowIdentity<'Id>) : System.Collections.Generic.IEqualityComparer<'Id> option =
        match byWitness.TryGetValue(box idw) with
        | true, e -> Some(unbox<System.Collections.Generic.IEqualityComparer<'Id>> e)
        | _ -> None

    /// Token equality over one cell — `cellToken a = cellToken b`, computed without the tokens
    /// (`DataFrame.CellKey`, whose agreement with `cellToken` is a law in the suite).
    let cell: System.Collections.Generic.IEqualityComparer<Cell> =
        { new System.Collections.Generic.IEqualityComparer<Cell> with
            member _.Equals(a, b) = DataFrame.CellKey.equals a b
            member _.GetHashCode c = DataFrame.CellKey.hashCell c }

    /// Token equality over a list of cells — `rowTokenString a = rowTokenString b`: that string is
    /// length-prefixed per cell and so injective, so it is equal exactly when the lists are equal
    /// cell by cell under `cell`.
    let cells: System.Collections.Generic.IEqualityComparer<Cell list> =
        { new System.Collections.Generic.IEqualityComparer<Cell list> with
            member _.Equals(a, b) =
                let rec go (xs: Cell list) (ys: Cell list) =
                    match xs, ys with
                    | [], [] -> true
                    | x :: xt, y :: yt -> DataFrame.CellKey.equals x y && go xt yt
                    | _ -> false

                go a b

            member _.GetHashCode cs =
                let mutable h = 0

                for c in cs do
                    h <- ((h <<< 5) ^^^ (h >>> 27)) ^^^ DataFrame.CellKey.hashCell c

                h }

/// Phase 284 — how a key equality declared through `RowIdentity.withKeyEquality` disagreed with its
/// witness's `KeyString` over a table, as `RowIdentity.checkKeyEquality` found it. Row numbers are the
/// table's row indices, the earlier row first; a key is the witness's rendered `KeyString`.
type KeyEqualityDisagreement =
    /// The witness declares no key equality: `Delta.diff` takes the string path for it, which is
    /// always correct, and there is nothing to check. Usually the witness `withKeyEquality` RETURNED
    /// was dropped and the undeclared one it was given kept.
    | NotDeclared of scheme: string
    /// The declared equality holds for two rows whose key strings differ: the typed diff would pair
    /// them as one identity where the string diff sees two.
    | EqualIdsDistinctKeys of row: int * other: int * key: string * otherKey: string
    /// Two rows render the same key string and the declared equality does not hold for them: the
    /// typed diff would miss the `DuplicateIdentity` the string diff reports.
    | DistinctIdsEqualKey of row: int * other: int * key: string
    /// The declared equality holds for two rows whose ids it hashes differently — an equality that
    /// breaks its own hashing contract, so a lookup under it can miss a row.
    | UnequalHashes of row: int * other: int * key: string

/// Reference identity witnesses — enough to exercise the whole delta surface with no domain
/// dependency of any kind.
[<RequireQualifiedAccess>]
module RowIdentity =

    /// The reserved scheme name for an identity-free source, whose deltas address rows by ordinal.
    /// It is a reserved WORD, not a witness: a source with real identity must never claim it, and a
    /// delta under it must use `ByOrdinal` refs exclusively (`validate` enforces both directions).
    let ordinalScheme = "ordinal"

    /// Phase 284 — a copy of `idw` that DECLARES `equality` as the equality its `KeyString` agrees
    /// with, so `Delta.diff` pairs its rows by the typed `'Id` and renders a key string only for the
    /// rows a delta carries (Phase 283's typed path). `idw` itself is left as it was: the declaration
    /// belongs to the returned record, and so does the typed path — keep and pass the RETURNED witness.
    /// A later copy of it (`{ w with … }`) is a new witness with no declaration, and takes the string
    /// path. The reference witnesses `byColumn` and `byColumns` are declared through this very route.
    ///
    /// **The contract — the caller's, and used as given.** For every two ids the witness can produce,
    /// `equality.Equals(a, b)` holds EXACTLY when `KeyString a = KeyString b`, and ids it calls equal
    /// it hashes alike. The library cannot prove that, and does not check it on any path it runs: a
    /// check would cost the key strings the typed path exists to avoid. F# structural equality is NOT
    /// automatically such an equality — a `KeyString` that case-folds, rounds or truncates renders two
    /// structurally distinct ids to one string.
    ///
    /// **When it is broken, the diff is wrong, silently.** An equality coarser than the strings pairs
    /// two distinct identities as one row (mis-paired rows: a wrong `RowChanged`, a missing
    /// `RowAdded` / `RowRemoved`); one finer than the strings misses the `DuplicateIdentity` refusal
    /// the string path reports and answers a delta over keys that are not identities. Neither is
    /// detected at diff time. `checkKeyEquality` is the check, for a consumer's own test suite.
    let withKeyEquality
        (equality: System.Collections.Generic.IEqualityComparer<'Id>)
        (idw: RowIdentity<'Id>)
        : RowIdentity<'Id> =
        // A fresh record, so the declaration is this record's for life and never rewrites `idw`'s
        // (the registry is keyed by the record object — see `KeyEqualities`).
        KeyEqualities.declare equality { idw with Scheme = idw.Scheme }

    /// How many keyed rows `checkKeyEquality` compares PAIRWISE (every pair among the first this
    /// many), beside its linear pass over every row.
    let private pairwiseSample = 256

    /// Phase 284 — CHECKS the key equality `idw` declares (`withKeyEquality`) against its `KeyString`
    /// over one table, and answers the first disagreement found: rows whose ids the equality calls
    /// equal and whose key strings differ, rows sharing a key string that the equality calls
    /// distinct, or equal ids hashed differently. A witness that declares nothing is `NotDeclared`.
    /// Rows the witness gives no identity are skipped.
    ///
    /// Every row is checked in one linear pass by key string and by id; the first 256 keyed rows are
    /// also compared pairwise, which is what catches an equality whose hash disagrees for ids it
    /// calls equal (a lookup under such an equality can miss the pair). `Ok ()` is evidence over
    /// THIS table — a sample — not a proof: feed it tables that reach the ids the witness renders
    /// alike or apart (case variants, rounding boundaries, composite components that swap).
    ///
    /// A test helper: nothing in the library calls it, and no diff pays for it.
    let checkKeyEquality (idw: RowIdentity<'Id>) (table: Table) : Result<unit, KeyEqualityDisagreement> =
        match KeyEqualities.tryOf idw with
        | None -> Error(NotDeclared idw.Scheme)
        | Some eq ->
            let keyOf = idw.KeyOf table
            let rows = ResizeArray<int>()
            let ids = ResizeArray<'Id>()
            let keys = ResizeArray<string>()

            for i in 0 .. Table.rowCount table - 1 do
                match keyOf i with
                | Some id ->
                    rows.Add i
                    ids.Add id
                    keys.Add(idw.KeyString id)
                | None -> ()

            let mutable found = None

            // The linear pass: the first row per key string, and the first row per id under `eq`.
            let byKey = System.Collections.Generic.Dictionary<string, int>()
            let byId = System.Collections.Generic.Dictionary<'Id, int>(eq)
            let mutable j = 0

            while found.IsNone && j < rows.Count do
                let id = ids[j]
                let k = keys[j]

                match byKey.TryGetValue k with
                | true, p ->
                    if not (eq.Equals(ids[p], id)) then
                        found <- Some(DistinctIdsEqualKey(rows[p], rows[j], k))
                    elif eq.GetHashCode(ids[p]) <> eq.GetHashCode(id) then
                        found <- Some(UnequalHashes(rows[p], rows[j], k))
                | _ ->
                    byKey[k] <- j

                    if not (isNull (box id)) then
                        match byId.TryGetValue id with
                        | true, p -> found <- Some(EqualIdsDistinctKeys(rows[p], rows[j], keys[p], k))
                        | _ -> byId[id] <- j

                j <- j + 1

            // The pairwise sample: needs no hash, so an equality whose hash disagrees with it is seen.
            let m = min rows.Count pairwiseSample
            let mutable a = 0

            while found.IsNone && a < m do
                let mutable b = a + 1

                while found.IsNone && b < m do
                    let equal = eq.Equals(ids[a], ids[b])
                    let sameKey = keys[a] = keys[b]

                    if equal && not sameKey then
                        found <- Some(EqualIdsDistinctKeys(rows[a], rows[b], keys[a], keys[b]))
                    elif sameKey && not equal then
                        found <- Some(DistinctIdsEqualKey(rows[a], rows[b], keys[a]))
                    elif equal && eq.GetHashCode(ids[a]) <> eq.GetHashCode(ids[b]) then
                        found <- Some(UnequalHashes(rows[a], rows[b], keys[a]))

                    b <- b + 1

                a <- a + 1

            match found with
            | Some d -> Error d
            | None -> Ok()

    /// Identity is the value of one named column — the everyday case (a primary key column). A row
    /// whose key cell is `Null`, or whose key column is absent, has NO identity (`Null` is the
    /// validity mask's "absent", and absence is not an identity); such a source is a `MissingIdentity`
    /// defect under `Delta.diff` rather than a silently-ordinal fallback.
    ///
    /// The key string is the pinned `DataFrame.cellToken`, so a float key groups exactly as
    /// `GroupBy` / `Distinct` group it and two hosts mint the same key for the same cell.
    let byColumn (column: string) : RowIdentity<Cell> =
        // Phase 283 — this witness's key string is `cellToken`, and `CellKey` is token equality
        // without the token, so the diff may pair its rows by the cell itself.
        let idw: RowIdentity<Cell> =
            { Scheme = "column:" + column
              KeyOf =
                // The currying is load-bearing (Phase 206): the per-TABLE work sits in the FIRST
                // application, so a consumer that keys every row binds `idw.KeyOf t` once and then pays
                // O(1) per row. Reading the cell out of the `Cell list` by index instead is O(i) each,
                // and keying an n-row table that way is quadratic — which is what made `Delta.diff` a
                // hundred times dearer for ten times the rows. The answers are identical either way.
                //
                // A caller that writes `idw.KeyOf t i` inside its own loop re-does the first
                // application on every iteration and gets the old cost back; the two callers in this
                // package (`keyIndex` here, `tokensOf` in the incremental seam) hoist it deliberately.
                fun t ->
                    let cells =
                        match Table.tryColumn column t with
                        | Some c -> List.toArray c.Cells
                        | None -> [||]

                    fun i ->
                        if i >= 0 && i < cells.Length then
                            match cells[i] with
                            | Null -> None
                            | cell -> Some cell
                        else
                            None
              KeyString = DataFrame.cellToken }

        withKeyEquality KeyEqualities.cell idw

    /// Identity is the tuple of several named columns — the composite-key case. Any `Null` component
    /// makes the row identity-free, for the same reason as `byColumn`.
    let byColumns (columns: string list) : RowIdentity<Cell list> =
        // Phase 283 — as `byColumn`: `rowTokenString` is injective per cell, so list equality under
        // `CellKey` is exactly equality of the rendered keys.
        let idw: RowIdentity<Cell list> =
            { Scheme = "columns:" + String.concat "," columns
              KeyOf =
                // Staged exactly as `byColumn` above, and for the same reason: one pass per key column
                // on the first application, O(1) per row thereafter.
                fun t ->
                    let arrays =
                        columns
                        |> List.map (fun n ->
                            match Table.tryColumn n t with
                            | Some c -> List.toArray c.Cells
                            | None -> [||])

                    fun i ->
                        let cells =
                            arrays |> List.map (fun a -> if i >= 0 && i < a.Length then a[i] else Null)

                        if List.isEmpty cells || cells |> List.exists Cell.isNull then
                            None
                        else
                            Some cells
              KeyString = DataFrame.rowTokenString }

        withKeyEquality KeyEqualities.cells idw

/// Phase 273 — one table's row keys, minted ONCE under one identity scheme and already proved
/// unique: `Keys[i]` is row `i`'s `KeyString`, and `Index` maps a key back to its row. Internal and
/// opaque — no public type names it, and nothing about a delta's equality or its wire moves.
///
/// Only two things build one: `Delta.diff`, for a table it keyed, and the incremental seam, for a
/// source it keyed. Both have already checked that every row has a key and that no key repeats, so
/// a holder may skip both checks. The index is built on first use: a diff whose rows all sat still
/// never looks a key up, so it never pays for the hash table.
///
/// Phase 283 — it may also hold the rows' TYPED ids (`'Id[]`, boxed: the index is not generic) and
/// the witness record that minted them. `Delta.diff` reads them back only for that very witness,
/// which is what makes the unboxing safe: one record has one `'Id`. Absent where nothing has keyed
/// the table by id yet; the diff then asks the witness for them.
[<Sealed; AllowNullLiteral>]
type internal KeyedIndex(scheme: string, keys: string[], built: System.Collections.Generic.Dictionary<string, int>) =
    let mutable index = built
    // One reference, written in one assignment, so a reader never pairs one writer's ids with
    // another writer's witness.
    let mutable typedIds: (obj * obj) option = None

    member _.Scheme = scheme
    member _.Keys = keys

    /// The typed ids, when they were recorded by the witness object `by`; `null` otherwise.
    member _.IdsFor(by: obj) : obj =
        match typedIds with
        | Some(owner, ids) when obj.ReferenceEquals(owner, by) -> ids
        | _ -> null

    /// Record the typed ids (aligned with `Keys`) and the witness object that minted them.
    member _.SetIds(by: obj, ids: obj) = typedIds <- Some(by, ids)

    member _.Index =
        if isNull index then
            let d = System.Collections.Generic.Dictionary<string, int>(keys.Length)

            for i in 0 .. keys.Length - 1 do
                d[keys[i]] <- i

            // A benign race: two readers may both build it, and both builds are equal.
            index <- d

        index

/// Phase 273 — where a table's keys and a diff's keys are remembered, so a tick mints each row's key
/// once rather than three times (the diff keyed both tables, then the refresh keyed the new one
/// again, although the state had already keyed the old one at the previous tick).
///
/// Two weak maps, keyed by OBJECT identity and never by value, so an entry lives exactly as long as
/// the table or the delta it describes and a structurally equal copy never inherits it:
///
///  * a TABLE's keys under a scheme — what `Delta.diff` reads for a side it has keyed before (the
///    `before` of a tick is the previous tick's `after`, or the source the state last evaluated);
///  * a DELTA's keys for the table it was diffed INTO — what the refresh reads. Only the record
///    `Delta.diff` returned carries them: a hand-built delta, a composed one, a normalised or
///    decoded copy, an ordinal diff and `FullRefresh` carry nothing and take the minting path.
///
/// The reuse trusts `RowIdentity.Scheme` to name the keying rule, which is what the scheme already
/// claims — it is the same promise `Delta.compose` relies on to combine two deltas' keys.
[<RequireQualifiedAccess>]
module internal KeyedIndexes =

    let private byTable =
        System.Runtime.CompilerServices.ConditionalWeakTable<Table, KeyedIndex>()

    let private byDelta =
        System.Runtime.CompilerServices.ConditionalWeakTable<RowSetDelta, Table * KeyedIndex>()

    /// Remember `t`'s keys (replacing whatever was remembered for it, under any scheme).
    let remember (t: Table) (k: KeyedIndex) : unit = byTable.AddOrUpdate(t, k)

    /// `t`'s keys, when they were remembered under `scheme`.
    let tryOf (scheme: string) (t: Table) : KeyedIndex option =
        match byTable.TryGetValue t with
        | true, k when k.Scheme = scheme -> Some k
        | _ -> None

    /// Attach to the delta `Delta.diff` is about to return the keys of the table it diffed into.
    let attach (d: RowSetDelta) (into: Table) (k: KeyedIndex) : unit = byDelta.AddOrUpdate(d, (into, k))

    /// The keys a delta carries for `source` under `scheme` — only when this very delta was built by
    /// `Delta.diff` into this very table.
    let carried (scheme: string) (d: TableDelta) (source: Table) : KeyedIndex option =
        match d with
        | FullRefresh -> None
        | RowSet r ->
            match byDelta.TryGetValue r with
            | true, (into, k) when obj.ReferenceEquals(into, source) && k.Scheme = scheme -> Some k
            | _ -> None

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

    /// Every row's canonical content token, in row order, indexable in O(1) (Phase 206).
    ///
    /// This replaced a `rowContentToken t i` that read the row by index — a `Column.cell` per
    /// column, each walking its column list from the head — and was called once per candidate row.
    /// Computing the whole table's tokens in one transpose is linear, and the comparison below
    /// then costs a string equality rather than a table scan. Computed lazily at the point of use,
    /// so a diff that refuses on a keying defect never pays for it.
    let private rowTokens (t: Table) : string[] =
        RowAccess.rows t |> List.map DataFrame.rowTokenStringOfArray |> List.toArray

    /// Index a table's rows by identity, refusing whole if the witness cannot key every row uniquely.
    let private keyIndex (idw: RowIdentity<'Id>) (t: Table) : Result<(string * int) list, DeltaDefect> =
        let n = Table.rowCount t

        // Hoisted (Phase 206): the witness's per-table work happens once here, not once per row.
        // The reference witnesses build a row-indexable view of their key columns on this first
        // application; keeping it inside the loop is what made keying an n-row table quadratic.
        let keyAt = idw.KeyOf t

        let rec go i acc (seen: Set<string>) =
            if i >= n then
                Ok(List.rev acc)
            else
                match keyAt i with
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
    ///
    /// **Dense since Phase 272.** The answer is the one the row-token form gave, and the suite holds
    /// the two equal over drawn tables; what moved is what it costs. That form keyed both tables
    /// through a persistent `Set` and two `Map`s over the key strings, and decided "changed" by
    /// building a length-prefixed token STRING for every row of both tables — every cell of both
    /// tables minted into a string, for a comparison whose answer was almost always "equal". At
    /// 100,000 rows it cost up to 36 times the full evaluation of the pipeline it fed (see
    /// docs/incremental-evaluation.md, "What it costs on the clock"). Now:
    ///
    ///  * each table's key strings are minted ONCE into an array (the witness's `KeyString` is the
    ///    only thing that can say what a key is, so this is the floor), and the `before` keys are
    ///    indexed in one hash table, which is also the uniqueness check;
    ///  * a row whose key sits at the SAME index in both tables — the overwhelmingly common case, an
    ///    edit in place — is paired by one string comparison, never a lookup; only a row that moved
    ///    is looked up;
    ///  * "changed" is decided cell by cell under `CellKey.equals`, which is `cellToken` equality
    ///    without the token (a law in the suite pins the two equal), column by column over the
    ///    in-place pairs, and a column whose cell list is the SAME object in both tables — a column
    ///    the edit did not touch — is not read at all for them, because equal positions of one list
    ///    hold one cell.
    ///
    /// **Paired by the typed id since Phase 283**, for a witness that declares a key equality
    /// (`KeyEqualities`; the reference witnesses do). The new table's rows are paired with the prior's
    /// by `'Id` under that equality, and the key string is rendered only for a row the delta carries
    /// (an added row) or for a refusal's payload: a paired row reuses the prior source's own string.
    /// So the floor above, one key string per row of each table, is now one per row of a table
    /// nothing has keyed and one per added row after that. The declared equality agrees with the key
    /// strings exactly, so the answer, the refusals and their payloads are the string path's; a
    /// witness with no declaration takes the string path unchanged.
    ///
    /// The refusals are the old ones in the old order: every `before` defect before any `after`
    /// defect, and within a table the first row, in row order, that has no key or repeats an
    /// earlier row's key.
    let diff (idw: RowIdentity<'Id>) (before: Table) (after: Table) : Result<TableDelta, DeltaDefect> =
        if before.Schema <> after.Schema then
            Ok FullRefresh
        else
            let nb = Table.rowCount before
            let na = Table.rowCount after

            // ---- the before side: minted once, indexed once (the index IS the uniqueness check) ----
            //
            // Phase 273 — or not minted at all: a table keyed before under this scheme (the previous
            // tick's `after`, or the source the incremental state last evaluated) hands back its
            // keys, already proved unique, and its index is built only if something below looks a
            // key up.
            let knownB = KeyedIndexes.tryOf idw.Scheme before
            let knownA = KeyedIndexes.tryOf idw.Scheme after

            // Phase 283 — the typed pairing: taken when the witness DECLARES an equality its key
            // string agrees with (`KeyEqualities`), for an after side this diff must key. Every
            // other case is the string path, unchanged.
            let equality = if knownA.IsNone then KeyEqualities.tryOf idw else None

            let owner = box idw
            let mutable defect = None

            let (bKeys: string[]), (bKnown: KeyedIndex), (bIdsMinted: 'Id[] option) =
                match knownB with
                | Some k -> k.Keys, k, None
                | None ->
                    let keys: string[] = Array.zeroCreate nb
                    let index = System.Collections.Generic.Dictionary<string, int>(nb)
                    let ids: 'Id[] = Array.zeroCreate (if equality.IsSome then nb else 0)
                    let keyB = idw.KeyOf before
                    let mutable i = 0

                    while defect.IsNone && i < nb do
                        match keyB i with
                        | None -> defect <- Some(MissingIdentity(idw.Scheme, i))
                        | Some id ->
                            let k = idw.KeyString id

                            if index.ContainsKey k then
                                defect <- Some(DuplicateIdentity(idw.Scheme, k))
                            else
                                index[k] <- i
                                keys[i] <- k

                                if equality.IsSome then
                                    ids[i] <- id

                        i <- i + 1

                    let known = KeyedIndex(idw.Scheme, keys, index)

                    if equality.IsSome && defect.IsNone then
                        known.SetIds(owner, box ids)
                        keys, known, Some ids
                    else
                        keys, known, None

            let bIndex () = bKnown.Index

            // Phase 283 — the before side's typed ids: the ones just minted, the ones remembered
            // for this table by this very witness, or asked of the witness once and then
            // remembered (a table the seam keyed by string). A known table was keyed whole, so the
            // witness has an id for every row of it; if it now says otherwise the scheme's promise
            // is broken, and the string path, which reads only the remembered keys, answers.
            let bIds: 'Id[] option =
                match equality with
                | Some _ when defect.IsNone ->
                    match bIdsMinted with
                    | Some ids -> Some ids
                    | None ->
                        match bKnown.IdsFor owner with
                        | null ->
                            let keyB = idw.KeyOf before
                            let ids: 'Id[] = Array.zeroCreate nb
                            let mutable complete = true
                            let mutable i = 0

                            while complete && i < nb do
                                match keyB i with
                                | Some id -> ids[i] <- id
                                | None -> complete <- false

                                i <- i + 1

                            if complete then
                                bKnown.SetIds(owner, box ids)
                                Some ids
                            else
                                None
                        | remembered -> Some(unbox<'Id[]> remembered)
                | _ -> None

            // ---- the after side: a key at the same index as before needs no lookup ----
            //
            // `inPlace[i]` says after row `i` carries before row `i`'s key. Such keys are unique among
            // themselves (they are distinct positions of the unique `bKeys`), so the only duplicates
            // an after key can form involve a key that MOVED; those, and only those, go in `moved`.
            // A key `k` at row `i` repeats an EARLIER after row exactly when it is already in
            // `moved`, or when it is before row `j`'s key for some `j < i` that after row `j` holds
            // in place — the two ways an earlier row can hold it.
            //
            // Both paths answer what the content pass below reads: `aKeys` (every after row's key
            // string), `inPlace`, for a row NOT in place the before row holding its key (`-1` for
            // none: an added row), and whether a before row's key is still present.
            let aKeys: string[] =
                match knownA with
                | Some k -> k.Keys
                | None -> Array.zeroCreate na

            let inPlace: bool[] = Array.zeroCreate na

            let (matchOf: int -> int), (beforeStays: int -> bool), (aIds: 'Id[] option) =
                match equality, bIds with
                | Some eq, Some bIds ->
                    // Phase 283 — the typed pairing. Pairing, the uniqueness check and the lookups
                    // run on the ids under the declared equality, which agrees with the key strings
                    // exactly, so every decision below is the string path's decision. A row's key
                    // STRING is the before row's own instance wherever the row was paired (in place
                    // or moved), and is rendered only for an added row — a row the delta carries —
                    // or for the payload of a refusal.
                    let aIds: 'Id[] = Array.zeroCreate na
                    let aMatch: int[] = Array.create na -1
                    let moved = System.Collections.Generic.HashSet<'Id>(eq)
                    let mutable anyMoved = false

                    let bTyped: System.Collections.Generic.Dictionary<'Id, int> option[] = [| None |]

                    // Built only once something has moved: an edit in place never hashes an id.
                    let bLookup (id: 'Id) =
                        let d =
                            match bTyped[0] with
                            | Some d -> d
                            | None ->
                                let d = System.Collections.Generic.Dictionary<'Id, int>(nb, eq)

                                for j in 0 .. nb - 1 do
                                    d[bIds[j]] <- j

                                bTyped[0] <- Some d
                                d

                        match d.TryGetValue id with
                        | true, j -> j
                        | _ -> -1

                    let keyA = idw.KeyOf after
                    let mutable i = 0

                    while defect.IsNone && i < na do
                        match keyA i with
                        | None -> defect <- Some(MissingIdentity(idw.Scheme, i))
                        | Some id ->
                            aIds[i] <- id

                            if i < nb && eq.Equals(id, bIds[i]) then
                                if anyMoved && moved.Contains id then
                                    defect <- Some(DuplicateIdentity(idw.Scheme, bKeys[i]))
                                else
                                    inPlace[i] <- true
                                    aKeys[i] <- bKeys[i]
                            else
                                let j = bLookup id
                                let heldEarlierInPlace = j >= 0 && j < i && j < na && inPlace[j]

                                if heldEarlierInPlace || not (moved.Add id) then
                                    let k = if j >= 0 then bKeys[j] else idw.KeyString id
                                    defect <- Some(DuplicateIdentity(idw.Scheme, k))
                                else
                                    aMatch[i] <- j
                                    aKeys[i] <- (if j >= 0 then bKeys[j] else idw.KeyString id)

                                anyMoved <- true

                        i <- i + 1

                    let anyMoved = anyMoved

                    (fun r -> aMatch[r]),
                    (fun r -> (r < na && inPlace[r]) || (anyMoved && moved.Contains bIds[r])),
                    Some aIds
                | _ ->
                    let moved = System.Collections.Generic.HashSet<string>()
                    // `moved` is consulted only once something has moved, so an edit in place never
                    // hashes an after key at all. A flag rather than `moved.Count`, which the Fable
                    // runtime computes by walking its buckets (see `CellKey.slotOf`).
                    let mutable anyMoved = false
                    // A known `after` is keyed already and proved unique: its keys are read, not
                    // minted, and the walk below only classifies them.
                    let keyA =
                        if defect.IsNone && knownA.IsNone then
                            idw.KeyOf after
                        else
                            (fun _ -> None)

                    let mutable i = 0

                    while defect.IsNone && i < na do
                        let mutable have = true
                        let mutable k = ""

                        if knownA.IsSome then
                            k <- aKeys[i]
                        else
                            match keyA i with
                            | None ->
                                defect <- Some(MissingIdentity(idw.Scheme, i))
                                have <- false
                            | Some id ->
                                k <- idw.KeyString id
                                aKeys[i] <- k

                        if have then
                            if i < nb && System.String.Equals(k, bKeys[i]) then
                                if anyMoved && moved.Contains k then
                                    defect <- Some(DuplicateIdentity(idw.Scheme, k))
                                else
                                    inPlace[i] <- true
                            else
                                let heldEarlierInPlace =
                                    match bIndex().TryGetValue k with
                                    | true, j -> j < i && j < na && inPlace[j]
                                    | _ -> false

                                if heldEarlierInPlace || not (moved.Add k) then
                                    defect <- Some(DuplicateIdentity(idw.Scheme, k))

                                anyMoved <- true

                        i <- i + 1

                    let anyMoved = anyMoved

                    let matchOf r =
                        match bIndex().TryGetValue aKeys[r] with
                        | true, bi -> bi
                        | _ -> -1

                    matchOf, (fun r -> (r < na && inPlace[r]) || (anyMoved && moved.Contains bKeys[r])), None

            match defect with
            | Some d -> Error d
            | None ->
                // ---- content: cell by cell under token equality, one column at a time ----
                //
                // Each schema column's cells as an array of exactly the table's row count, padded
                // with `Null` — `RowAccess.columns`' reading, one column at a time and only for the
                // columns something needs.
                let columnOf (t: Table) (n: int) (name: string) : Cell[] =
                    match Table.tryColumn name t with
                    | Some c ->
                        let a = List.toArray c.Cells

                        if a.Length = n then
                            a
                        else
                            Array.init n (fun r -> if r < a.Length then a[r] else Null)
                    | None -> Array.create n Null

                let names = before.Schema |> List.map fst |> List.toArray
                let bCols: Cell[] option[] = Array.create names.Length None
                let aCols: Cell[] option[] = Array.create names.Length None

                let bCol ci =
                    match bCols[ci] with
                    | Some a -> a
                    | None ->
                        let a = columnOf before nb names[ci]
                        bCols[ci] <- Some a
                        a

                let aCol ci =
                    match aCols[ci] with
                    | Some a -> a
                    | None ->
                        let a = columnOf after na names[ci]
                        aCols[ci] <- Some a
                        a

                let sameList ci =
                    match Table.tryColumn names[ci] before, Table.tryColumn names[ci] after with
                    | Some b, Some a -> System.Object.ReferenceEquals(b.Cells, a.Cells)
                    | None, None -> true
                    | _ -> false

                let changed: bool[] = Array.zeroCreate na
                let shared = min na nb

                for ci in 0 .. names.Length - 1 do
                    if not (sameList ci) then
                        let b = bCol ci
                        let a = aCol ci

                        for r in 0 .. shared - 1 do
                            if inPlace[r] && not changed[r] && not (DataFrame.CellKey.equals b[r] a[r]) then
                                changed[r] <- true

                // A row that moved is compared whole, across every column: its two positions differ,
                // so a shared list says nothing about it.
                let rowDiffers (bi: int) (ai: int) =
                    let mutable differs = false
                    let mutable ci = 0

                    while not differs && ci < names.Length do
                        let b = bCol ci
                        let a = aCol ci
                        differs <- not (DataFrame.CellKey.equals b[bi] a[ai])
                        ci <- ci + 1

                    differs

                let rows = System.Collections.Generic.List<RowRef * RowChange>()

                for r in 0 .. na - 1 do
                    if inPlace[r] then
                        if changed[r] then
                            rows.Add((ByKey aKeys[r], RowChanged))
                    else
                        match matchOf r with
                        | -1 -> rows.Add((ByKey aKeys[r], RowAdded))
                        | bi ->
                            // present at both ends; byte-identical content is not a change
                            if rowDiffers bi r then
                                rows.Add((ByKey aKeys[r], RowChanged))

                // A before key is still present exactly when after holds it in place or holds it
                // having moved.
                for r in 0 .. nb - 1 do
                    if not (beforeStays r) then
                        rows.Add((ByKey bKeys[r], RowRemoved))

                // Phase 273 — both tables are now keyed and proved unique under this scheme. Remember
                // them, so the next tick's diff (whose `before` is this `after`) mints nothing for
                // that side, and let the delta carry `after`'s keys to the refresh. Phase 283 — with
                // the typed ids beside the keys, so the next tick pairs by id without asking the
                // witness for this side again.
                if knownB.IsNone then
                    KeyedIndexes.remember before bKnown

                let aKnown =
                    match knownA with
                    | Some k -> k
                    | None ->
                        let k = KeyedIndex(idw.Scheme, aKeys, null)

                        match aIds with
                        | Some ids -> k.SetIds(owner, box ids)
                        | None -> ()

                        KeyedIndexes.remember after k
                        k

                let delta =
                    { Scheme = idw.Scheme
                      Rows = sortRows (List.ofSeq rows)
                      InvalidatedColumns = [] }

                KeyedIndexes.attach delta after aKnown
                Ok(RowSet delta)

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
            let beforeTokens = rowTokens before
            let afterTokens = rowTokens after

            let changed =
                [ for i in 0 .. shared - 1 do
                      if beforeTokens[i] <> afterTokens[i] then
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
        match Json.parseDetailed s with
        | Error m -> Error(NotJson m)
        | Ok el -> decodeJson el
