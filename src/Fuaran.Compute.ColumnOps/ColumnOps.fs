namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Fuaran.Compute.ColumnOps (Phase 31) — a columnar op-algebra over the
//  `Fuaran.Core.Column` `Table`, bridging Core's two strands. `Column` /
//  `DataFrame` are a witness-free *data* strand with no replayable *edit*
//  history; this adds an op DU + a total `apply` + a partial `invert` + a
//  structural `Diff` + a canonical codec + a `Fuaran.Core.OpStream`
//  `StreamWitness`, so table mutations become an append-only, hash-chained,
//  replayable, tamper-evident stream — the columnar analogue of the tree
//  op-stream.
//
//  It introduces NO base type and NO new permanent witness field (GP1/GP2): the
//  op DU is self-contained over the existing `Table`, and `OpStream` stays
//  generic over its `(apply, encode, decode)` witness — the columnar stream is
//  just an instance of it. Totality (GP4): `apply` returns a typed
//  `ColumnRejection`, never throws. FSharp.Core only, Fable-clean.
//
//  Sequencing (cross-side): `office#26`'s adoption of `Fuaran.Core.OpStream` for
//  the workspace op-stream is the real-world proof that the existing
//  `StreamWitness` suffices for a witness-free strand; this phase confirms it in
//  the small — no witness-shape change was needed (the columnar op DU + its
//  codec are purely additive).
// ============================================================================

/// A columnar edit op over a `Table`.
type ColumnOp =
    /// Set the cell at `row` of column `column` to `value` (`value` must be `Null` or the column's type).
    | SetCell of column: string * row: int * value: Cell
    /// Replace an existing column (by name) with `Column` (its length must equal the row count; its
    /// `Type` updates the schema entry). Use `InsertColumn` to add a new one.
    | SetColumn of Column
    /// Insert a NEW column at `index` (its name must not already exist; its length must equal the row
    /// count unless the table currently has no columns, in which case it sets the row count).
    | InsertColumn of index: int * Column
    /// Remove the column named `name` (must exist).
    | RemoveColumn of name: string
    /// Append rows; each row maps a subset of column names to cells (missing columns get `Null`; an
    /// unknown column name is rejected).
    | AppendRows of rows: (string * Cell) list list
    /// Apply a whole `DataFrame` pipeline as a single op — the result table replaces the current one.
    | ApplyTransform of Transform list

/// Why a columnar op was rejected — recoverable + enumerated (GP5), never a throw (GP4).
type ColumnRejection =
    | NoSuchColumn of name: string * available: string list
    | DuplicateColumn of name: string
    | RowOutOfRange of row: int * rowCount: int
    | CellTypeMismatch of column: string * expected: string * got: string
    | ColumnLengthMismatch of column: string * expected: int * got: int
    | RowShapeUnknownColumn of name: string * available: string list
    | TransformRejected of detail: string
    | NotInvertible of op: string

/// The columnar op-algebra: `apply` / `canApply` / `invert` / `Diff.toOps` over a `Table`, the wire
/// codec, and the `OpStream` `StreamWitness` that makes a table-edit stream chainable + replayable.
module ColumnOps =

    // ---- small Table helpers ----

    let private cellTypeName (c: Cell) : string =
        match Cell.typeOf c with
        | Some t -> ColumnType.tag t
        | None -> "null"

    /// A cell "fits" a column type iff it is `Null` or of a type that WIDENS into it
    /// (`ColumnType.widens`: the type itself, or an `Int` into a float or a decimal column) — the
    /// rule the substrate's `Table.validate` checks every cell by, so a table valid on one side of
    /// the boundary never fails an edit on the other (Phase 321, `DECISIONS.md` D2). The cell is
    /// stored as given, never converted: `invert` restores the previous cell verbatim, so its round
    /// trip is exact under the widened rule as it was under the strict one.
    let private cellFits (colName: string) (ty: ColumnType) (c: Cell) : Result<unit, ColumnRejection> =
        match c with
        | Null -> Ok()
        | _ ->
            match Cell.typeOf c with
            | Some t when ColumnType.widens t ty -> Ok()
            | _ -> Error(CellTypeMismatch(colName, ColumnType.tag ty, cellTypeName c))

    let private cellsFit (col: Column) : Result<unit, ColumnRejection> =
        col.Cells
        |> List.tryPick (fun c ->
            match cellFits col.Name col.Type c with
            | Error e -> Some e
            | Ok() -> None)
        |> function
            | Some e -> Error e
            | None -> Ok()

    let private replaceColumn (name: string) (newCol: Column) (t: Table) : Table =
        { Schema = t.Schema |> List.map (fun (n, ty) -> if n = name then n, newCol.Type else n, ty)
          Columns = t.Columns |> List.map (fun c -> if c.Name = name then newCol else c) }

    let private insertColumnAt (index: int) (col: Column) (t: Table) : Table =
        let clamp i = max 0 (min i (List.length t.Columns))
        let i = clamp index

        let insertAt i x xs =
            let before = xs |> List.truncate i
            let after = xs |> List.skip (min i (List.length xs))
            before @ [ x ] @ after

        { Schema = insertAt i (col.Name, col.Type) t.Schema
          Columns = insertAt i col t.Columns }

    let private removeColumn (name: string) (t: Table) : Table =
        { Schema = t.Schema |> List.filter (fun (n, _) -> n <> name)
          Columns = t.Columns |> List.filter (fun c -> c.Name <> name) }

    // ---- apply ----

    /// Apply a columnar op to a table — total (a typed `ColumnRejection`, never a throw).
    let apply (op: ColumnOp) (t: Table) : Result<Table, ColumnRejection> =
        match op with
        | SetCell(name, row, value) ->
            match t.Columns |> List.tryFind (fun c -> c.Name = name) with
            | None -> Error(NoSuchColumn(name, Table.columnNames t))
            | Some col ->
                let rc = Table.rowCount t

                if row < 0 || row >= rc then
                    Error(RowOutOfRange(row, rc))
                else
                    cellFits name col.Type value
                    |> Result.map (fun () ->
                        let cells' = col.Cells |> List.mapi (fun i c -> if i = row then value else c)
                        replaceColumn name { col with Cells = cells' } t)
        | SetColumn newCol ->
            match t.Columns |> List.tryFind (fun c -> c.Name = newCol.Name) with
            | None -> Error(NoSuchColumn(newCol.Name, Table.columnNames t))
            | Some _ ->
                let rc = Table.rowCount t

                if List.length newCol.Cells <> rc then
                    Error(ColumnLengthMismatch(newCol.Name, rc, List.length newCol.Cells))
                else
                    cellsFit newCol |> Result.map (fun () -> replaceColumn newCol.Name newCol t)
        | InsertColumn(index, col) ->
            if t.Columns |> List.exists (fun c -> c.Name = col.Name) then
                Error(DuplicateColumn col.Name)
            else
                let rc = Table.rowCount t
                let hasCols = not (List.isEmpty t.Columns)

                if hasCols && List.length col.Cells <> rc then
                    Error(ColumnLengthMismatch(col.Name, rc, List.length col.Cells))
                else
                    cellsFit col |> Result.map (fun () -> insertColumnAt index col t)
        | RemoveColumn name ->
            if t.Columns |> List.exists (fun c -> c.Name = name) then
                Ok(removeColumn name t)
            else
                Error(NoSuchColumn(name, Table.columnNames t))
        | AppendRows rows ->
            let names = Table.columnNames t |> Set.ofList

            let rowFault (row: (string * Cell) list) =
                row
                |> List.tryPick (fun (n, v) ->
                    if not (Set.contains n names) then
                        Some(RowShapeUnknownColumn(n, Table.columnNames t))
                    else
                        match t.Columns |> List.tryFind (fun c -> c.Name = n) with
                        | Some col ->
                            match cellFits n col.Type v with
                            | Error e -> Some e
                            | Ok() -> None
                        | None -> Some(RowShapeUnknownColumn(n, Table.columnNames t)))

            match rows |> List.tryPick rowFault with
            | Some e -> Error e
            | None ->
                let columns' =
                    t.Columns
                    |> List.map (fun col ->
                        let appended =
                            rows
                            |> List.map (fun row ->
                                row
                                |> List.tryFind (fun (n, _) -> n = col.Name)
                                |> Option.map snd
                                |> Option.defaultValue Null)

                        { col with
                            Cells = col.Cells @ appended })

                Ok { t with Columns = columns' }
        | ApplyTransform pipeline ->
            match DataFrame.evalPipeline pipeline t with
            | Ok t' -> Ok t'
            | Error e -> Error(TransformRejected(DataFrame.errorString e))

    /// Dry-run accept/reject, consistent with `apply` by construction (the columnar `apply` is a cheap
    /// pure fold, so `canApply` is `apply` with the result discarded — they can never disagree).
    let canApply (op: ColumnOp) (t: Table) : Result<unit, ColumnRejection> = apply op t |> Result.map ignore

    /// The inverse op that undoes `op` applied to the PRE-state `t` (so `apply (invert op t) (apply op t) =
    /// t`). Defined for the structural ops; `AppendRows` / `ApplyTransform` are `NotInvertible` (no
    /// row-removal / no general transform inverse). Reads the pre-state for the values it must restore.
    ///
    /// **Guarded by `canApply` (Phase 181).** An op the table would REFUSE has no inverse: its rejection
    /// is returned, not an op. Until Phase 181 the `InsertColumn` clause read NOTHING from the pre-state
    /// and answered `RemoveColumn col.Name` unconditionally, so the "inverse" of an insert refused as a
    /// `DuplicateColumn` was a remove that SUCCEEDED and took the column that was already there — an undo
    /// stack that records `invert op pre` beside every op it attempts lost a column the refused op never
    /// touched. The guard is the tree engine's (`Ops.invert`), and it strengthens all four invertible
    /// clauses rather than one: `SetCell` and `SetColumn` read the pre-state for the column and row but
    /// not for the VALUE, so a wrong-typed `SetCell` that `apply` refuses had an inverse too.
    let invert (op: ColumnOp) (t: Table) : Result<ColumnOp, ColumnRejection> =
        match op with
        // Neither has an inverse at ANY table — no row removal, no general transform inverse — so both
        // answer before the guard. That is not just brevity: `canApply (ApplyTransform p)` runs the
        // pipeline, and `invert` must not evaluate a pipeline to say what it already knows.
        | AppendRows _ -> Error(NotInvertible "AppendRows")
        | ApplyTransform _ -> Error(NotInvertible "ApplyTransform")
        | _ ->
            // An op is invertible only if it would apply to the pre-state. The per-clause reads below
            // are unreachable in their `None` / out-of-range arms once this has passed; they are kept
            // rather than replaced by `List.item` + `Option.get` so `invert` stays total by shape
            // (GP4) and not merely by argument.
            match canApply op t with
            | Error e -> Error e
            | Ok() ->
                match op with
                | SetCell(name, row, _) ->
                    match t.Columns |> List.tryFind (fun c -> c.Name = name) with
                    | None -> Error(NoSuchColumn(name, Table.columnNames t))
                    | Some col ->
                        let rc = Table.rowCount t

                        if row < 0 || row >= rc then
                            Error(RowOutOfRange(row, rc))
                        else
                            Ok(SetCell(name, row, List.item row col.Cells))
                | SetColumn newCol ->
                    match t.Columns |> List.tryFind (fun c -> c.Name = newCol.Name) with
                    | None -> Error(NoSuchColumn(newCol.Name, Table.columnNames t))
                    | Some old -> Ok(SetColumn old)
                | InsertColumn(_, col) ->
                    // The guard has established the column is ABSENT, which is what makes the remove
                    // the true inverse: it takes back exactly what the insert put in.
                    Ok(RemoveColumn col.Name)
                | RemoveColumn name ->
                    match t.Columns |> List.tryFindIndex (fun c -> c.Name = name) with
                    | None -> Error(NoSuchColumn(name, Table.columnNames t))
                    | Some idx -> Ok(InsertColumn(idx, List.item idx t.Columns))
                | AppendRows _ -> Error(NotInvertible "AppendRows") // unreachable — answered above
                | ApplyTransform _ -> Error(NotInvertible "ApplyTransform") // unreachable — answered above

    // ---- structural diff ----

    /// A structural diff between two tables, producing a script that reconstructs `after` from `before`
    /// (`apply`-ing it in order yields `after`). When the schemas match shape (same names/types/order)
    /// and the row counts agree, it is column-granular (`SetColumn` for each changed column); otherwise
    /// it is a full rebuild (`RemoveColumn` every old column, then `InsertColumn` every new one in
    /// order). Total — never throws.
    let toOps (before: Table) (after: Table) : ColumnOp list =
        if before.Schema = after.Schema && Table.rowCount before = Table.rowCount after then
            after.Columns
            |> List.choose (fun ac ->
                match before.Columns |> List.tryFind (fun c -> c.Name = ac.Name) with
                | Some bc when bc.Cells <> ac.Cells -> Some(SetColumn ac)
                | _ -> None)
        else
            let removes = before.Columns |> List.rev |> List.map (fun c -> RemoveColumn c.Name)
            let inserts = after.Columns |> List.mapi (fun i c -> InsertColumn(i, c))
            removes @ inserts

    /// Apply a script in order, threading the table (short-circuits on the first rejection).
    let applyAll (ops: ColumnOp list) (t: Table) : Result<Table, ColumnRejection> =
        (Ok t, ops) ||> List.fold (fun acc op -> acc |> Result.bind (apply op))

    // ---- canonical wire codec ----

    let private cellToJson (c: Cell) : JVal =
        match c with
        | Null -> Canon.typed "Null" []
        | Int i -> Canon.typed "Int" [ "v", JInt i ]
        | Float f -> Canon.typed "Float" [ "v", JFloat f ]
        | Bool b -> Canon.typed "Bool" [ "v", JBool b ]
        | Str s -> Canon.typed "Str" [ "v", JStr s ]
        | Date s -> Canon.typed "Date" [ "v", JStr s ]
        | Timestamp s -> Canon.typed "Timestamp" [ "v", JStr s ]
        // Phase 277: the canonical decimal text as a JSON string (Core `DECISIONS.md` D72 K5).
        // Phase 321: canonicalised on the way OUT as the decoder canonicalises on the way in, so a
        // cell built by hand as `1.50` encodes as `1.5` and the wire round trip is byte-identical;
        // text that is not decimal at all is written as found, for the decoder to refuse by name.
        | Decimal s -> Canon.typed "Decimal" [ "v", JStr(DecimalText.tryCanonical s |> Option.defaultValue s) ]

    let private field (k: string) (el: JVal) : Result<JVal, string> =
        match el with
        | JObj fields ->
            match fields |> List.tryFind (fun (n, _) -> n = k) with
            | Some(_, v) -> Ok v
            | None -> Error("missing field: " + k)
        | _ -> Error("expected object for field " + k)

    let private strOf =
        function
        | JStr s -> Ok s
        | _ -> Error "expected string"

    let private intOf =
        function
        | JInt i -> Ok i
        | _ -> Error "expected int"

    let private arrOf =
        function
        | JArr xs -> Ok xs
        | _ -> Error "expected array"

    let private kindOf el = field "$type" el |> Result.bind strOf

    let private mapM (f: 'a -> Result<'b, string>) (xs: 'a list) : Result<'b list, string> =
        let rec go acc =
            function
            | [] -> Ok(List.rev acc)
            | x :: rest -> f x |> Result.bind (fun v -> go (v :: acc) rest)

        go [] xs

    let private cellOfJson (el: JVal) : Result<Cell, string> =
        kindOf el
        |> Result.bind (fun k ->
            let v () = field "v" el

            match k with
            | "Null" -> Ok Null
            | "Int" -> v () |> Result.bind intOf |> Result.map Int
            | "Float" ->
                v ()
                |> Result.bind (function
                    | JFloat f -> Ok(Float f)
                    | JInt i -> Ok(Float(float i))
                    | _ -> Error "expected float")
            | "Bool" ->
                v ()
                |> Result.bind (function
                    | JBool b -> Ok(Bool b)
                    | _ -> Error "expected bool")
            | "Str" -> v () |> Result.bind strOf |> Result.map Str
            | "Date" -> v () |> Result.bind strOf |> Result.map Date
            | "Timestamp" -> v () |> Result.bind strOf |> Result.map Timestamp
            // Decimal text, canonicalised on read; an exact integer token is read too, and a
            // fractional number token is refused, as the column codec refuses it (D72 K5).
            | "Decimal" ->
                v ()
                |> Result.bind (function
                    | JStr s ->
                        match Cell.decimal s with
                        | Some c -> Ok c
                        | None -> Error("not decimal text: " + s)
                    | JInt i -> Ok(Decimal(string i))
                    | _ -> Error "expected decimal text")
            | other -> Error("unknown cell kind: " + other))

    let private columnToJson (c: Column) : JVal =
        JObj
            [ "name", JStr c.Name
              "type", JStr(ColumnType.tag c.Type)
              "cells", JArr(c.Cells |> List.map cellToJson) ]

    let private columnOfJson (el: JVal) : Result<Column, string> =
        field "name" el
        |> Result.bind strOf
        |> Result.bind (fun name ->
            field "type" el
            |> Result.bind strOf
            |> Result.bind (fun tag ->
                match ColumnType.ofTag tag with
                | None -> Error("unknown column type: " + tag)
                | Some ty ->
                    field "cells" el
                    |> Result.bind arrOf
                    |> Result.bind (mapM cellOfJson)
                    |> Result.map (fun cells -> Column.create name ty cells)))

    let private rowToJson (row: (string * Cell) list) : JVal =
        JArr(row |> List.map (fun (n, v) -> JObj [ "col", JStr n; "value", cellToJson v ]))

    let private rowOfJson (el: JVal) : Result<(string * Cell) list, string> =
        arrOf el
        |> Result.bind (
            mapM (fun cellEl ->
                field "col" cellEl
                |> Result.bind strOf
                |> Result.bind (fun n -> field "value" cellEl |> Result.bind cellOfJson |> Result.map (fun v -> n, v)))
        )

    /// Encode a `ColumnOp` to a `JVal` (`"$type"`-tagged, the `Fuaran.Core` envelope discipline).
    let encodeJson (op: ColumnOp) : JVal =
        match op with
        | SetCell(name, row, v) -> Canon.typed "setCell" [ "col", JStr name; "row", JInt row; "value", cellToJson v ]
        | SetColumn col -> Canon.typed "setColumn" [ "column", columnToJson col ]
        | InsertColumn(i, col) -> Canon.typed "insertColumn" [ "index", JInt i; "column", columnToJson col ]
        | RemoveColumn name -> Canon.typed "removeColumn" [ "name", JStr name ]
        | AppendRows rows -> Canon.typed "appendRows" [ "rows", JArr(rows |> List.map rowToJson) ]
        | ApplyTransform pipeline ->
            Canon.typed "applyTransform" [ "pipeline", JArr(pipeline |> List.map DataFrameCodec.encodeTransform) ]

    /// The canonical wire string for a `ColumnOp` (Ordinal-sorted keys + cross-host float layout → byte-
    /// identical across hosts).
    let encode (op: ColumnOp) : string = Canon.render (encodeJson op)

    let decodeJson (el: JVal) : Result<ColumnOp, string> =
        kindOf el
        |> Result.bind (fun k ->
            match k with
            | "setCell" ->
                field "col" el
                |> Result.bind strOf
                |> Result.bind (fun name ->
                    field "row" el
                    |> Result.bind intOf
                    |> Result.bind (fun row ->
                        field "value" el
                        |> Result.bind cellOfJson
                        |> Result.map (fun v -> SetCell(name, row, v))))
            | "setColumn" -> field "column" el |> Result.bind columnOfJson |> Result.map SetColumn
            | "insertColumn" ->
                field "index" el
                |> Result.bind intOf
                |> Result.bind (fun i ->
                    field "column" el
                    |> Result.bind columnOfJson
                    |> Result.map (fun col -> InsertColumn(i, col)))
            | "removeColumn" -> field "name" el |> Result.bind strOf |> Result.map RemoveColumn
            | "appendRows" ->
                field "rows" el
                |> Result.bind arrOf
                |> Result.bind (mapM rowOfJson)
                |> Result.map AppendRows
            | "applyTransform" ->
                field "pipeline" el
                |> Result.bind arrOf
                |> Result.bind (
                    mapM (fun stepEl ->
                        DataFrameCodec.decodeTransform stepEl |> Result.mapError ColumnCodec.errorString)
                )
                |> Result.map ApplyTransform
            | other -> Error("unknown columnar op: " + other))

    /// Decode a `ColumnOp` from a wire string (a JSON-syntax error becomes a `not json` message).
    let decode (s: string) : Result<ColumnOp, string> =
        match Json.parse s with
        | Error m -> Error("not json: " + m)
        | Ok el -> decodeJson el

    /// Render a `ColumnRejection` as a stable human string.
    let rejectionString (r: ColumnRejection) : string =
        match r with
        | NoSuchColumn(n, avail) -> "no such column '" + n + "'; available: " + String.concat ", " avail
        | DuplicateColumn n -> "duplicate column: " + n
        | RowOutOfRange(row, rc) -> "row " + string row + " out of range (row count " + string rc + ")"
        | CellTypeMismatch(c, exp, got) -> "column '" + c + "': expected " + exp + " cell, got " + got
        | ColumnLengthMismatch(c, exp, got) -> "column '" + c + "': length " + string got + " ≠ row count " + string exp
        | RowShapeUnknownColumn(n, avail) ->
            "row references unknown column '"
            + n
            + "'; available: "
            + String.concat ", " avail
        | TransformRejected d -> "transform rejected: " + d
        | NotInvertible o -> o + " is not invertible"

    /// Map a columnar op to the `DataFrame.Change` it represents (Phase 34) — so a table-edit stream
    /// drives incremental re-evaluation. A cell / whole-column value edit is `ColumnValuesChanged`; a row
    /// append is `RowsAppended`; a structural (`InsertColumn`/`RemoveColumn`) or whole-table
    /// (`ApplyTransform`) op is a `FullChange` (recompute — the structural delta is not reconstructable
    /// from the op alone, and `evalFrom` full-recomputes those cases regardless).
    let changeOf (op: ColumnOp) : Change =
        match op with
        | SetCell(col, _, _) -> ColumnValuesChanged col
        | SetColumn col -> ColumnValuesChanged col.Name
        | AppendRows _ -> RowsAppended
        | InsertColumn _
        | RemoveColumn _
        | ApplyTransform _ -> FullChange

    /// The columns a columnar op moved, as a part set for `Propagation.dirtyFromChangedParts`
    /// (Phase 250) — the column-vocabulary adapter the tree-generic propagation strand takes as a
    /// parameter and never names itself. Read off `changeOf`: a value edit moved its one column;
    /// anything else — an append (every column gains cells), a schema change, a whole-table
    /// transform — is `None`, "unknown or all", which every reader meets.
    let changedColumns (op: ColumnOp) : Set<string> option =
        match changeOf op with
        | ColumnValuesChanged col -> Some(Set.singleton col)
        | RowsAppended
        | SchemaChanged _
        | FullChange -> None

    /// The row delta a columnar op induces on the table it is applied to (Phase 250), built from
    /// the op and the BEFORE table rather than by diffing before against after (`Delta.diff` is a
    /// full pass over both tables). `changeOf` lifts a cell edit to a whole-column invalidation, so a
    /// one-cell edit reaches `Incremental` as every row; this reaches it as one.
    ///
    /// Row identity is the caller's witness, opaque here — which columns form the key is not
    /// assumed, so identity is read from the rows themselves:
    ///
    /// - `SetCell` — the edited row, by its key BEFORE and AFTER the edit. The same key: that row
    ///   `RowChanged` (or the empty delta when the cell already held the value). Different keys (the
    ///   edit wrote a key column): the old key `RowRemoved` and the new one `RowAdded`.
    /// - `SetColumn` — when no row's identity moves, every row whose cell in that column differs,
    ///   `RowChanged`; when any identity moves, `FullRefresh`.
    /// - `AppendRows` — each appended row `RowAdded`, when every one has a key that is new to the
    ///   table and unique among them.
    /// - `InsertColumn`, `RemoveColumn`, `ApplyTransform` — `FullRefresh`: the schema or the whole
    ///   table moved, which is what the top element is for.
    ///
    /// `FullRefresh` is also the answer wherever identity is missing (a row the witness cannot key)
    /// or the op does not apply to `before` — an op with no result has no delta of its own, and the
    /// top is always a true description. The delta's scheme is `rid.Scheme`. Pure, total.
    let deltaOf (rid: RowIdentity<'Id>) (before: Table) (op: ColumnOp) : TableDelta =
        let keysOf (t: Table) =
            let keyOf = rid.KeyOf t
            Array.init (Table.rowCount t) (fun i -> keyOf i |> Option.map rid.KeyString)

        let rowSet (rows: (RowRef * RowChange) list) = Delta.ofRows rid.Scheme rows

        match op with
        | InsertColumn _
        | RemoveColumn _
        | ApplyTransform _ -> FullRefresh
        | _ ->
            match apply op before with
            | Error _ -> FullRefresh
            | Ok after ->
                match op with
                | SetCell(col, row, value) ->
                    let unchanged =
                        match Table.tryColumn col before with
                        | Some c -> List.item row c.Cells = value
                        | None -> false

                    match
                        rid.KeyOf before row |> Option.map rid.KeyString,
                        rid.KeyOf after row |> Option.map rid.KeyString
                    with
                    | Some _, Some _ when unchanged -> Delta.empty rid.Scheme
                    | Some k0, Some k1 when k0 = k1 -> rowSet [ ByKey k0, RowChanged ]
                    | Some k0, Some k1 -> rowSet [ ByKey k0, RowRemoved; ByKey k1, RowAdded ]
                    | _ -> FullRefresh
                | SetColumn col ->
                    let k0 = keysOf before
                    let k1 = keysOf after

                    if k0 <> k1 || Array.exists Option.isNone k0 then
                        FullRefresh
                    else
                        match Table.tryColumn col.Name before with
                        | None -> FullRefresh
                        | Some old ->
                            List.zip old.Cells col.Cells
                            |> List.indexed
                            |> List.choose (fun (i, (a, b)) ->
                                if a = b then None else Some(ByKey k0[i].Value, RowChanged))
                            |> rowSet
                | AppendRows _ ->
                    let n0 = Table.rowCount before
                    let k0 = keysOf before
                    let k1 = keysOf after
                    let added = k1[n0..] |> List.ofArray
                    let existing = k0 |> Array.choose id |> Set.ofArray

                    let fresh =
                        added |> List.forall Option.isSome
                        && (added |> List.choose id |> List.distinct |> List.length) = added.Length
                        && added |> List.forall (fun k -> not (Set.contains k.Value existing))

                    if fresh then
                        added |> List.map (fun k -> ByKey k.Value, RowAdded) |> rowSet
                    else
                        FullRefresh
                | InsertColumn _
                | RemoveColumn _
                | ApplyTransform _ -> FullRefresh

    // ---- Phase 268 — the ops over a prepared version ----
    //
    // `apply` over a `Table` rebuilds a whole column list to set one cell, because a list shares
    // nothing but its tail. Over a `Prepared` — since Phase 268 a persistent version whose columns
    // are ropes of chunks — the same op costs what it touches: `SetCell` copies one chunk of one
    // column and shares every other, `AppendRows` copies the partial last chunk of each column and
    // packs the rest, `SetColumn` compares the new cells chunk by chunk and keeps every chunk it
    // did not move, `InsertColumn` packs the one column it adds and `RemoveColumn` copies only the
    // array of column ropes. The prior version is untouched, so `invertPrepared` reads it, and a
    // refresh over the new version recognises by identity what did not move.
    //
    // The verdicts are `apply`'s, clause for clause, over the schema rather than the column list —
    // the same thing on a coherent table (one whose columns are its schema, which every version an
    // op produces is) — and `toTable (applyPrepared op (prepare t))` is `apply op t` on every such
    // table: the equality the suite holds, and what ties these forms to the proved model.

    let private columnAt (p: Prepared) (columns: Chunked[]) (ci: int) : Column =
        let name, ty = List.item ci p.Cols
        Column.create name ty (Chunked.toCells columns[ci])

    let private indexIn (p: Prepared) (name: string) : int option =
        p.Cols |> List.tryFindIndex (fun (n, _) -> n = name)

    let private insertAt (i: int) (x: 'a) (xs: 'a list) : 'a list =
        let before = xs |> List.truncate i
        let after = xs |> List.skip (min i (List.length xs))
        before @ [ x ] @ after

    /// `apply` over a prepared version — total, with `apply`'s verdicts — at the cost of what the
    /// op touches rather than of the table. The `Table` form stays as it is; on a coherent table
    /// the two agree cell for cell, so `apply op t` reads as `applyPrepared op (prepare t)` handed
    /// back through `DataFrame.toTable`.
    let applyPrepared (op: ColumnOp) (p: Prepared) : Result<Prepared, ColumnRejection> =
        let names = p.Cols |> List.map fst

        match op with
        | SetCell(name, row, value) ->
            match indexIn p name with
            | None -> Error(NoSuchColumn(name, names))
            | Some ci ->
                let rc = p.Count

                if row < 0 || row >= rc then
                    Error(RowOutOfRange(row, rc))
                else
                    cellFits name (snd (List.item ci p.Cols)) value
                    |> Result.map (fun () ->
                        let columns = Array.copy (Prepared.columns p)
                        columns[ci] <- Chunked.setCell columns[ci] row value
                        Prepared.ofChunks p.Cols rc columns)
        | SetColumn newCol ->
            match indexIn p newCol.Name with
            | None -> Error(NoSuchColumn(newCol.Name, names))
            | Some ci ->
                let rc = p.Count
                let n = List.length newCol.Cells

                if n <> rc then
                    Error(ColumnLengthMismatch(newCol.Name, rc, n))
                else
                    cellsFit newCol
                    |> Result.map (fun () ->
                        let columns = Array.copy (Prepared.columns p)

                        columns[ci] <-
                            Chunked.ofCellsSharing columns[ci] newCol.Type (List.toArray newCol.Cells)
                            |> Chunked.withCells newCol.Cells

                        let cols =
                            p.Cols
                            |> List.mapi (fun i (nm, ty) -> if i = ci then nm, newCol.Type else nm, ty)

                        Prepared.ofChunks cols rc columns)
        | InsertColumn(index, col) ->
            if p.Cols |> List.exists (fun (n, _) -> n = col.Name) then
                Error(DuplicateColumn col.Name)
            else
                let rc = p.Count
                let hasCols = not (List.isEmpty p.Cols)
                let n = List.length col.Cells

                if hasCols && n <> rc then
                    Error(ColumnLengthMismatch(col.Name, rc, n))
                else
                    cellsFit col
                    |> Result.map (fun () ->
                        let i = max 0 (min index (List.length p.Cols))
                        let columns = Prepared.columns p

                        let rope =
                            Chunked.ofCells col.Type Chunked.rows (List.toArray col.Cells)
                            |> Chunked.withCells col.Cells

                        let columns' =
                            Array.concat
                                [ Array.sub columns 0 i; [| rope |]; Array.sub columns i (columns.Length - i) ]

                        Prepared.ofChunks (insertAt i (col.Name, col.Type) p.Cols) (if hasCols then rc else n) columns')
        | RemoveColumn name ->
            match indexIn p name with
            | None -> Error(NoSuchColumn(name, names))
            | Some ci ->
                let columns = Prepared.columns p

                let columns' =
                    Array.append (Array.sub columns 0 ci) (Array.sub columns (ci + 1) (columns.Length - ci - 1))

                let cols = p.Cols |> List.filter (fun (n, _) -> n <> name)
                Ok(Prepared.ofChunks cols (if List.isEmpty cols then 0 else p.Count) columns')
        | AppendRows rows ->
            let nameSet = Set.ofList names

            let rowFault (row: (string * Cell) list) =
                row
                |> List.tryPick (fun (n, v) ->
                    if not (Set.contains n nameSet) then
                        Some(RowShapeUnknownColumn(n, names))
                    else
                        match indexIn p n with
                        | Some ci ->
                            match cellFits n (snd (List.item ci p.Cols)) v with
                            | Error e -> Some e
                            | Ok() -> None
                        | None -> Some(RowShapeUnknownColumn(n, names)))

            match rows |> List.tryPick rowFault with
            | Some e -> Error e
            | None ->
                let rowsA = List.toArray rows
                let columns = Prepared.columns p

                let columns' =
                    columns
                    |> Array.mapi (fun ci c ->
                        let name = fst (List.item ci p.Cols)

                        let appended =
                            rowsA
                            |> Array.map (fun row ->
                                row
                                |> List.tryFind (fun (n, _) -> n = name)
                                |> Option.map snd
                                |> Option.defaultValue Null)

                        Chunked.append c appended)

                let count = if columns.Length = 0 then 0 else p.Count + rowsA.Length
                Ok(Prepared.ofChunks p.Cols count columns')
        | ApplyTransform pipeline ->
            // A transform replaces the table, as the `Table` form says; the result is prepared
            // afresh, and evaluated over the version's frame rather than through its table.
            match DataFrame.evalPrepared DataFrame.noResolve Map.empty pipeline p with
            | Ok t' -> Ok(DataFrame.prepare t')
            | Error e -> Error(TransformRejected(DataFrame.errorString e))

    /// `canApply` over a prepared version — `applyPrepared` with the result discarded, so the two
    /// can never disagree; at the cost of what the op touches.
    let canApplyPrepared (op: ColumnOp) (p: Prepared) : Result<unit, ColumnRejection> =
        applyPrepared op p |> Result.map ignore

    /// `invert` over a prepared version: the inverse op that undoes `op` applied to the PRE-state
    /// `p`, read from `p` — which the edit left untouched, every chunk of it still reachable — under
    /// the same guard `invert` applies (an op the version would refuse has no inverse). A `SetCell`'s
    /// inverse reads one cell of one chunk; a `SetColumn`'s or `RemoveColumn`'s reads the column it
    /// must restore, which the op carries whole either way.
    let invertPrepared (op: ColumnOp) (p: Prepared) : Result<ColumnOp, ColumnRejection> =
        match op with
        | AppendRows _ -> Error(NotInvertible "AppendRows")
        | ApplyTransform _ -> Error(NotInvertible "ApplyTransform")
        | _ ->
            match canApplyPrepared op p with
            | Error e -> Error e
            | Ok() ->
                let names = p.Cols |> List.map fst
                let columns = Prepared.columns p

                match op with
                | SetCell(name, row, _) ->
                    match indexIn p name with
                    | None -> Error(NoSuchColumn(name, names))
                    | Some ci ->
                        if row < 0 || row >= p.Count then
                            Error(RowOutOfRange(row, p.Count))
                        else
                            Ok(SetCell(name, row, Chunked.cellAt columns[ci] row))
                | SetColumn newCol ->
                    match indexIn p newCol.Name with
                    | None -> Error(NoSuchColumn(newCol.Name, names))
                    | Some ci -> Ok(SetColumn(columnAt p columns ci))
                | InsertColumn(_, col) -> Ok(RemoveColumn col.Name)
                | RemoveColumn name ->
                    match indexIn p name with
                    | None -> Error(NoSuchColumn(name, names))
                    | Some ci -> Ok(InsertColumn(ci, columnAt p columns ci))
                | AppendRows _ -> Error(NotInvertible "AppendRows") // unreachable — answered above
                | ApplyTransform _ -> Error(NotInvertible "ApplyTransform") // unreachable — answered above

    /// `deltaOf` over a prepared version: the same delta, with a `SetCell`'s read off the one row
    /// it names — its key before and after the edit, from one chunk — rather than off the table,
    /// so the cell edit that reaches `Incremental.refreshPrepared` as one row costs one row to
    /// describe. Every other op reads the version's table, which the op's own cost already covers.
    let deltaOfPrepared (rid: RowIdentity<'Id>) (p: Prepared) (op: ColumnOp) : TableDelta =
        match op with
        | SetCell(col, row, value) ->
            match canApplyPrepared op p, indexIn p col with
            | Ok(), Some ci ->
                let columns = Prepared.columns p
                let unchanged = Chunked.cellAt columns[ci] row = value

                let rowTable (edited: bool) : Table =
                    { Schema = p.Cols
                      Columns =
                        p.Cols
                        |> List.mapi (fun i (n, ty) ->
                            Column.create
                                n
                                ty
                                [ (if edited && i = ci then
                                       value
                                   else
                                       Chunked.cellAt columns[i] row) ]) }

                let keyOf (t: Table) =
                    rid.KeyOf t 0 |> Option.map rid.KeyString

                match keyOf (rowTable false), keyOf (rowTable true) with
                | Some _, Some _ when unchanged -> Delta.empty rid.Scheme
                | Some k0, Some k1 when k0 = k1 -> Delta.ofRows rid.Scheme [ ByKey k0, RowChanged ]
                | Some k0, Some k1 -> Delta.ofRows rid.Scheme [ ByKey k0, RowRemoved; ByKey k1, RowAdded ]
                | _ -> FullRefresh
            | _ -> FullRefresh
        | _ -> deltaOf rid (DataFrame.toTable p) op

    /// The `Fuaran.Core.OpStream` `StreamWitness` for the columnar op-algebra — `apply` + the wire
    /// `encode`/`decode`. With it, `OpStream.append` / `verifyChain` / `replay` / `toJsonl` chain,
    /// verify, replay, and persist a table-edit stream with NO core change (the witness pattern, GP2;
    /// no frozen-record field added, GP7).
    let streamWitness: StreamWitness<ColumnOp, Table, ColumnRejection> =
        { Apply = apply
          Encode = encode
          Decode = decode }
