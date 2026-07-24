namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.Column.Ops (Phase 31) — a columnar op-algebra over the
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

    /// A cell "fits" a column type iff it is `Null` or exactly that type (no widening — strict, so
    /// `invert` round-trips exactly).
    let private cellFits (colName: string) (ty: ColumnType) (c: Cell) : Result<unit, ColumnRejection> =
        match c with
        | Null -> Ok()
        | _ ->
            match Cell.typeOf c with
            | Some t when t = ty -> Ok()
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
    let invert (op: ColumnOp) (t: Table) : Result<ColumnOp, ColumnRejection> =
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
        | InsertColumn(_, col) -> Ok(RemoveColumn col.Name)
        | RemoveColumn name ->
            match t.Columns |> List.tryFindIndex (fun c -> c.Name = name) with
            | None -> Error(NoSuchColumn(name, Table.columnNames t))
            | Some idx -> Ok(InsertColumn(idx, List.item idx t.Columns))
        | AppendRows _ -> Error(NotInvertible "AppendRows")
        | ApplyTransform _ -> Error(NotInvertible "ApplyTransform")

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

    /// The `Fuaran.Core.OpStream` `StreamWitness` for the columnar op-algebra — `apply` + the wire
    /// `encode`/`decode`. With it, `OpStream.append` / `verifyChain` / `replay` / `toJsonl` chain,
    /// verify, replay, and persist a table-edit stream with NO core change (the witness pattern, GP2;
    /// no frozen-record field added, GP7).
    let streamWitness: StreamWitness<ColumnOp, Table, ColumnRejection> =
        { Apply = apply
          Encode = encode
          Decode = decode }
