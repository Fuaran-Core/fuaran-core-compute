module Fuaran.Compute.Tests.ProofOracleTests

// The F* oracles as hosts: each extracted model (`proofs/oracle/<Model>.fs`, generated from
// `proofs/<Model>.fst` by F*'s own F# code generator) run BESIDE the production code it models,
// over generated inputs, with a disagreement reported red. Two models live here: the columnar op
// algebra (`proofs/ColumnOps.fst`, Phase 176) and the counted pipeline driver with its expression
// evaluator (`proofs/Pipeline.fst`, Phases 154 and 234). Both sections, and the test cases below,
// were carried from the Fuaran.Core repository's `Proofs.Oracle` family with the models they are
// about (Phase 259); the substrate's other models and their differentials stayed there.
//
// What agreement here does and does not establish is stated in proofs/README.md and, row by row,
// in the claims ladder at the repository root (`proofs.json`): a `tested` row names the cases below.

open System.IO
open Expecto

// The extracted COLUMNAR model, bound BEFORE `open Fuaran.Core` puts the production `ColumnOps`
// module in front of it: the two share a name on purpose, and this is the one file where both are
// in scope. (Bound here rather than as `global.ColumnOps` beside its family: Fantomas rewrites a
// `global.`-qualified module abbreviation into invalid F#, and a `///` doc comment on any module
// abbreviation is FS0535.)
module ModelCol = ColumnOps

// The extracted COUNTED-PIPELINE-DRIVER model, bound the same way for consistency (production has
// no `Pipeline` module, but the alias keeps every model read the same way here).
module ModelPipe = Pipeline

// The row-local incremental refresh (`proofs/oracle/Incremental.fs`), aliased before the opens for
// the same reason: production has an `Incremental` module of its own.
module ModelInc = Incremental

// The relational verbs' schema and refusals (`proofs/oracle/Relational.fs`), aliased for the same
// reading.
module ModelRel = Relational

// The dense frame and its kernels (`proofs/oracle/Frame.fs`), aliased before the opens: production
// has a `Frame` type and module of its own in `Fuaran.Compute`.
module ModelFrame = Frame

// The incremental refresh past the row-local class (`proofs/oracle/IncrementalMaintained.fs`),
// aliased for the same reading as `ModelInc`.
module ModelMaint = IncrementalMaintained

// The maintained sort (`proofs/oracle/MaintainedSort.fs`): the stable sort, the binary-insertion
// merge and the comparator, aliased for the same reading.
module ModelSort = MaintainedSort

open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests

let private inv = System.Globalization.CultureInfo.InvariantCulture

// ---------------------------------------------------------------------------
//  Phase 176 — the COLUMNAR op algebra: `ColumnOps.apply` / `canApply` / `invert` / `toOps` /
//  `applyAll` beside the extracted model (`proofs/ColumnOps.fst`), over generated tables and op
//  scripts.
//
//  WHAT THE BRIDGE SAYS. The model reads a cell as its type and an OPAQUE CARRIER — `Present ty
//  carrier` — and nothing in the algebra looks inside one. The bridge renders each production
//  value to its carrier (`Int 5` to `"5"`, a float through the round-trip `"R"` format, a bool to
//  its lower-case word, the three string-carried kinds verbatim) and parses it back, so a
//  production table and its model image are the same table under either reading. Where the two
//  DISAGREE by construction is a NaN float — `Float nan <> Float nan` in production, `"NaN" =
//  "NaN"` in the model — and that is the `column-cell-carrier-opaque` row; the generator does not
//  draw one, and says so.
//
//  The pipeline evaluator is a PARAMETER of the model (`ev`), as `canHold` is of the container
//  model. Here it is production's own `DataFrame.evalPipeline`, reached through the bridge: the
//  model hands the evaluator a token, the token indexes a fixed pool of pipelines, and the answer
//  comes back through the same bridge. So the `ApplyTransform` arm compares the model's
//  ENVELOPE — replace wholesale, or `TransformRejected` carrying the rendered error — and nothing
//  about the pipeline, which is exactly what the model claims.
//
//  FIVE COMPARISONS, per (op, state):
//    1. `ColumnOps.apply` vs the model's `apply` — verdict, accepted result through the bridge,
//       rejection by class AND payload (name, row, count, tag).
//    2. `ColumnOps.canApply` vs `can_apply`, and the dry run against the mutating call on each side
//       separately (`canapply_agrees` instantiated).
//    3. `ColumnOps.invert` vs `invert` — verdict and the derived inverse as an operation; and,
//       where the pre-state is WELL-FORMED and the op accepted and invertible, the round trip
//       asserted EXACTLY on production and on the model (`invert_roundtrip` instantiated). Where
//       the pre-state is not well-formed the round trip is only counted, because the theorem
//       does not claim it there — and the count of failures is the evidence the hypothesis earns
//       its place. Since Phase 181 both sides are GUARDED by `canApply`, so this arm now also
//       compares a REFUSED op's rejection coming back out of `invert` rather than an inverse.
//    4. THE INVARIANT: a well-formed pre-state and an accepted structural op give a well-formed
//       result (`apply_preserves_wf` instantiated), judged by the MODEL's `wf` on the bridged
//       production result.
//    5. Every script as a whole: `ColumnOps.applyAll` vs `apply_all` — the short-circuit and the
//       all-or-nothing discipline (`reject_identity`) as one comparison.
//  And a sixth over PAIRS of tables: `ColumnOps.toOps` vs `to_ops` as scripts, and where both
//  tables are well-formed, `applyAll (toOps before after) before = Ok after` asserted on both
//  sides (`diff_applicable` instantiated), with both branches of the diff counted.
//
//  The tables are GENERATED with the invariant deliberately broken some of the time — a repeated
//  name, a schema entry with no column, a column a row short, a cell of the wrong type — because
//  `apply` is total over all of them and the model must agree there too; the theorems that need
//  well-formedness are asserted only where the generator produced it, and the tally says how
//  often that was.
// ---------------------------------------------------------------------------

let private colTypeToModel (t: ColumnType) : ModelCol.coltype =
    match t with
    | IntType -> ModelCol.IntType
    | FloatType -> ModelCol.FloatType
    | BoolType -> ModelCol.BoolType
    | StringType -> ModelCol.StringType
    | DateType -> ModelCol.DateType
    | TimestampType _ -> ModelCol.TimestampType
    // Phase 321: the model carries the decimal column type, so the differential draws one.
    | DecimalType -> ModelCol.DecimalType

let private colTypeOfModel (t: ModelCol.coltype) : ColumnType =
    match t with
    | ModelCol.IntType -> IntType
    | ModelCol.FloatType -> FloatType
    | ModelCol.BoolType -> BoolType
    | ModelCol.StringType -> StringType
    | ModelCol.DateType -> DateType
    | ModelCol.TimestampType -> TimestampType TimeUnit.Seconds
    | ModelCol.DecimalType -> DecimalType

/// The honest cell bridge: type + carrier, the carrier a rendering that parses back exactly.
let private cellToModel (c: Cell) : ModelCol.cell =
    match c with
    | Null -> ModelCol.Null
    | Int i -> ModelCol.Present(ModelCol.IntType, i.ToString(inv))
    | Float f -> ModelCol.Present(ModelCol.FloatType, f.ToString("R", inv))
    | Bool b -> ModelCol.Present(ModelCol.BoolType, (if b then "true" else "false"))
    | Str s -> ModelCol.Present(ModelCol.StringType, s)
    | Date s -> ModelCol.Present(ModelCol.DateType, s)
    | Timestamp s -> ModelCol.Present(ModelCol.TimestampType, s)
    // The decimal text VERBATIM, as the other string-carried kinds: production compares a decimal
    // cell structurally (its text), and the carrier premise is that the model compares the same.
    | Decimal s -> ModelCol.Present(ModelCol.DecimalType, s)

/// The BLIND cell bridge — the go-red's instrument: every present cell is read as a string, so
/// the model's type check sees a `Str` where production sees an `Int`, and the two must part.
let private blindCellToModel (c: Cell) : ModelCol.cell =
    match cellToModel c with
    | ModelCol.Present(_, carrier) -> ModelCol.Present(ModelCol.StringType, carrier)
    | ModelCol.Null -> ModelCol.Null

let private cellOfModel (c: ModelCol.cell) : Cell =
    match c with
    | ModelCol.Null -> Null
    | ModelCol.Present(ModelCol.IntType, s) -> Int(System.Int32.Parse(s, inv))
    | ModelCol.Present(ModelCol.FloatType, s) -> Float(System.Double.Parse(s, inv))
    | ModelCol.Present(ModelCol.BoolType, s) -> Bool(s = "true")
    | ModelCol.Present(ModelCol.StringType, s) -> Str s
    | ModelCol.Present(ModelCol.DateType, s) -> Date s
    | ModelCol.Present(ModelCol.TimestampType, s) -> Timestamp s
    | ModelCol.Present(ModelCol.DecimalType, s) -> Decimal s

let private columnToModelWith (bridge: Cell -> ModelCol.cell) (c: Column) : ModelCol.column =
    { ModelCol.column.name = c.Name
      ModelCol.column.ty = colTypeToModel c.Type
      ModelCol.column.cells = (Column.toCells c) |> List.map bridge }

let private columnOfModel (c: ModelCol.column) : Column =
    KitColumn.create c.name (colTypeOfModel c.ty) (c.cells |> List.map cellOfModel)

let private tableToModelWith (bridge: Cell -> ModelCol.cell) (t: Table) : ModelCol.table =
    { ModelCol.table.schema = t.Schema |> List.map (fun f -> f.Name, colTypeToModel f.Type)
      ModelCol.table.columns = t.Columns |> List.map (columnToModelWith bridge) }

let private tableToModel = tableToModelWith cellToModel

let private tableOfModel (t: ModelCol.table) : Table =
    { Schema = t.schema |> List.map (fun (n, ty) -> Field.create n (colTypeOfModel ty))
      Columns = t.columns |> List.map columnOfModel }

/// The pipelines `ApplyTransform` draws from; the model sees each as its index. Two accept on
/// most tables, two reject on most, one is the identity, one rejects on an empty one.
let private pipelinePool: Transform list list =
    [ [ Distinct ]
      [ Limit(Slot.Lit 2, Slot.Lit 0) ]
      [ Project [ "a", "a" ] ]
      [ Filter(Col "nope") ]
      [ Derive("d", Lit(Int 1)) ]
      [] ]

let private pipelineToken (p: Transform list) : string =
    match pipelinePool |> List.tryFindIndex (fun q -> q = p) with
    | Some i -> string i
    | None -> "?"

/// The model's evaluator parameter, instantiated at production's own `DataFrame.evalPipeline`.
let private modelEvaluator (token: string) (mt: ModelCol.table) : ModelCol.outcome<ModelCol.table, string> =
    match System.Int32.TryParse token with
    | true, i when i >= 0 && i < List.length pipelinePool ->
        (match DataFrame.evalPipeline (List.item i pipelinePool) (tableOfModel mt) with
         | Ok t -> ModelCol.Ok(tableToModel t)
         | Error e -> ModelCol.Error(DataFrame.errorString e))
    | _ -> ModelCol.Error("no such pipeline token: " + token)

let private colOpToModelWith (bridge: Cell -> ModelCol.cell) (op: ColumnOp) : ModelCol.op =
    match op with
    | SetCell(n, row, v) -> ModelCol.SetCell(n, bigint row, bridge v)
    | SetColumn c -> ModelCol.SetColumn(columnToModelWith bridge c)
    | InsertColumn(i, c) -> ModelCol.InsertColumn(bigint i, columnToModelWith bridge c)
    | RemoveColumn n -> ModelCol.RemoveColumn n
    | AppendRows rows -> ModelCol.AppendRows(rows |> List.map (List.map (fun (n, v) -> n, bridge v)))
    | ApplyTransform p -> ModelCol.ApplyTransform(pipelineToken p)

let private modelCellRender (c: ModelCol.cell) : string =
    match c with
    | ModelCol.Null -> "null"
    | ModelCol.Present(ty, carrier) -> ModelCol.tag ty + ":" + carrier

let private modelColumnRender (c: ModelCol.column) : string =
    sprintf "%s:%s[%s]" c.name (ModelCol.tag c.ty) (c.cells |> List.map modelCellRender |> String.concat ",")

/// The two renderings agree by construction — production renders THROUGH the honest bridge —
/// so a differing rendering is a differing value.
let private modelColOpRender (op: ModelCol.op) : string =
    match op with
    | ModelCol.SetCell(n, row, v) -> sprintf "SetCell(%s;%s;%s)" n (string row) (modelCellRender v)
    | ModelCol.SetColumn c -> sprintf "SetColumn(%s)" (modelColumnRender c)
    | ModelCol.InsertColumn(i, c) -> sprintf "InsertColumn(%s;%s)" (string i) (modelColumnRender c)
    | ModelCol.RemoveColumn n -> sprintf "RemoveColumn(%s)" n
    | ModelCol.AppendRows rows -> sprintf "AppendRows(%d)" (List.length rows)
    | ModelCol.ApplyTransform p -> sprintf "ApplyTransform(%s)" p

let private prodColOpRender (op: ColumnOp) : string =
    modelColOpRender (colOpToModelWith cellToModel op)

let private modelColRejRender (r: ModelCol.rejection) : string =
    match r with
    | ModelCol.NoSuchColumn(n, avail) -> sprintf "NoSuchColumn(%s;%s)" n (String.concat "," avail)
    | ModelCol.DuplicateColumn n -> sprintf "DuplicateColumn(%s)" n
    | ModelCol.RowOutOfRange(row, rc) -> sprintf "RowOutOfRange(%s;%s)" (string row) (string rc)
    | ModelCol.CellTypeMismatch(c, e, g) -> sprintf "CellTypeMismatch(%s;%s;%s)" c e g
    | ModelCol.ColumnLengthMismatch(c, e, g) -> sprintf "ColumnLengthMismatch(%s;%s;%s)" c (string e) (string g)
    | ModelCol.RowShapeUnknownColumn(n, avail) -> sprintf "RowShapeUnknownColumn(%s;%s)" n (String.concat "," avail)
    | ModelCol.TransformRejected d -> sprintf "TransformRejected(%s)" d
    | ModelCol.NotInvertible o -> sprintf "NotInvertible(%s)" o

let private prodColRejRender (r: ColumnRejection) : string =
    match r with
    | NoSuchColumn(n, avail) -> sprintf "NoSuchColumn(%s;%s)" n (String.concat "," avail)
    | DuplicateColumn n -> sprintf "DuplicateColumn(%s)" n
    | RowOutOfRange(row, rc) -> sprintf "RowOutOfRange(%d;%d)" row rc
    | CellTypeMismatch(c, e, g) -> sprintf "CellTypeMismatch(%s;%s;%s)" c e g
    | ColumnLengthMismatch(c, e, g) -> sprintf "ColumnLengthMismatch(%s;%d;%d)" c e g
    | RowShapeUnknownColumn(n, avail) -> sprintf "RowShapeUnknownColumn(%s;%s)" n (String.concat "," avail)
    | TransformRejected d -> sprintf "TransformRejected(%s)" d
    | NotInvertible o -> sprintf "NotInvertible(%s)" o

let private colRejClass (r: ColumnRejection) : string =
    let s = prodColRejRender r
    s.Substring(0, s.IndexOf '(')

// ---- the generator ----

let private colNamePool = [ "a"; "b"; "c"; "d" ]

let private colTypePool =
    [ IntType
      FloatType
      BoolType
      StringType
      DateType
      TimestampType TimeUnit.Seconds
      DecimalType ]

/// A cell for a column of type `ty`: mostly fitting, sometimes `Null`, sometimes of another type.
let private genColCell (ty: ColumnType) (r: ConfRng.T) : Cell * ConfRng.T =
    let roll, r1 = ConfRng.intBelow 10 r
    let v, r2 = ConfRng.intBelow 100 r1

    let ofType t =
        match t with
        | IntType -> Int v
        | FloatType -> Float(float v / 4.0)
        | BoolType -> Bool(v % 2 = 0)
        | StringType -> Str(sprintf "s%d" v)
        | DateType -> Date(sprintf "2026-01-%02d" (1 + v % 28))
        | TimestampType _ -> Timestamp(sprintf "2026-01-01T00:00:%02dZ" (v % 60))
        // Phase 321: a decimal with a fraction, so it is never the digits of an int.
        | DecimalType -> Decimal(sprintf "%d.%d" (v / 10) (1 + v % 9))

    if roll < 7 then
        ofType ty, r2
    elif roll < 9 then
        Null, r2
    else
        let other, r3 = ConfRng.choose colTypePool r2
        ofType other, r3

let private genColCells (ty: ColumnType) (n: int) (r: ConfRng.T) : Cell list * ConfRng.T =
    let mutable rng = r
    let cells = System.Collections.Generic.List<Cell>()

    for _ in 1..n do
        let c, r' = genColCell ty rng
        rng <- r'
        cells.Add c

    List.ofSeq cells, rng

let private genColumn (name: string) (rows: int) (r: ConfRng.T) : Column * ConfRng.T =
    let ty, r1 = ConfRng.choose colTypePool r
    let cells, r2 = genColCells ty rows r1
    KitColumn.create name ty cells, r2

/// A table: up to three columns over a four-name pool, up to three rows — and, one draw in ten
/// each, a repeated name, a column a row long or short, or a schema that is not the columns'
/// projection. `ModelCol.wf` on the bridged table says which it was.
let private genColTable (r: ConfRng.T) : Table * ConfRng.T =
    let ncols, r1 = ConfRng.intBelow 4 r
    let rows, r2 = ConfRng.intBelow 4 r1
    let names, r3 = ConfRng.shuffle colNamePool r2
    let mutable rng = r3
    let cols = System.Collections.Generic.List<Column>()

    for i in 0 .. ncols - 1 do
        let dupRoll, r4 = ConfRng.intBelow 10 rng
        let lenRoll, r5 = ConfRng.intBelow 10 r4
        rng <- r5

        let name =
            if dupRoll = 0 && i > 0 then
                cols.[0].Name
            else
                List.item i names

        let n = if lenRoll = 0 then rows + 1 else rows
        let c, r6 = genColumn name n rng
        rng <- r6
        cols.Add c

    let columns = List.ofSeq cols
    let schemaRoll, r7 = ConfRng.intBelow 10 rng
    rng <- r7

    let schema =
        columns
        |> List.map (fun c -> Field.create c.Name c.Type)
        |> fun s ->
            if schemaRoll = 0 then
                List.truncate (List.length s - 1) s
            else
                s

    { Schema = schema; Columns = columns }, rng

/// An op against `t`, drawn so that every rejection class is reachable and acceptance is common.
let private genColOp (t: Table) (r: ConfRng.T) : ColumnOp * ConfRng.T =
    let rc = Table.rowCount t
    let names = Table.columnNames t
    let nameOrStranger, r1 = ConfRng.choose (names @ [ "zz" ]) r
    let kind, r2 = ConfRng.intBelow 6 r1

    match kind with
    | 0 ->
        let row, r3 = ConfRng.intBelow (rc + 2) r2
        let ty, r4 = ConfRng.choose colTypePool r3
        let v, r5 = genColCell ty r4
        SetCell(nameOrStranger, row - 1, v), r5
    | 1 ->
        let lenRoll, r3 = ConfRng.intBelow 5 r2
        let n = if lenRoll = 0 then rc + 1 else rc
        let c, r4 = genColumn nameOrStranger n r3
        SetColumn c, r4
    | 2 ->
        let idx, r3 = ConfRng.intBelow (List.length t.Columns + 3) r2
        let name, r4 = ConfRng.choose colNamePool r3
        let lenRoll, r5 = ConfRng.intBelow 5 r4

        let n =
            if List.isEmpty t.Columns then 2
            elif lenRoll = 0 then rc + 1
            else rc

        let c, r6 = genColumn name n r5
        InsertColumn(idx - 1, c), r6
    | 3 -> RemoveColumn nameOrStranger, r2
    | 4 ->
        let nrows, r3 = ConfRng.intBelow 2 r2
        let mutable rng = r3
        let rows = System.Collections.Generic.List<(string * Cell) list>()

        for _ in 0..nrows do
            let subset, r4 = ConfRng.shuffle (names @ [ "zz" ]) rng
            let take, r5 = ConfRng.intBelow (List.length subset + 1) r4
            rng <- r5
            let row = System.Collections.Generic.List<string * Cell>()

            for n in List.truncate take subset do
                let ty =
                    match Table.tryColumn n t with
                    | Some c -> c.Type
                    | None -> IntType

                let v, r6 = genColCell ty rng
                rng <- r6
                row.Add((n, v))

            rows.Add(List.ofSeq row)

        AppendRows(List.ofSeq rows), rng
    | _ ->
        let p, r3 = ConfRng.choose pipelinePool r2
        ApplyTransform p, r3

let private colStructural (op: ColumnOp) =
    match op with
    | ApplyTransform _ -> false
    | _ -> true

let private colInvertible (op: ColumnOp) =
    match op with
    | SetCell _
    | SetColumn _
    | InsertColumn _
    | RemoveColumn _ -> true
    | _ -> false

type private ColTally =
    {
        Diffs: string list
        Accepted: int
        Rejected: int
        Classes: Set<string>
        Inverted: int
        RoundTripped: int
        /// round trips ATTEMPTED on a pre-state the theorem does not cover, and how many failed
        RoundTripsOutsideWf: int
        RoundTripFailuresOutsideWf: int
        WfPre: int
        WfPreserved: int
        Scripts: int
    }

let private emptyColTally =
    { Diffs = []
      Accepted = 0
      Rejected = 0
      Classes = Set.empty
      Inverted = 0
      RoundTripped = 0
      RoundTripsOutsideWf = 0
      RoundTripFailuresOutsideWf = 0
      WfPre = 0
      WfPreserved = 0
      Scripts = 0 }

/// One (op, state) asked of both sides, across the first four comparisons.
let private colProbe (bridge: Cell -> ModelCol.cell) (op: ColumnOp) (st: Table) (acc: ColTally) : ColTally =
    let mop = colOpToModelWith bridge op
    let mst = tableToModelWith bridge st

    let where =
        sprintf
            "op %s at table %s"
            (prodColOpRender op)
            (modelColumnRender |> fun f -> mst.columns |> List.map f |> String.concat "|")

    let prod = ColumnOps.apply op st
    let model = ModelCol.apply modelEvaluator mop mst
    let wfPre = ModelCol.wf mst

    // 1. apply
    let applyDiff, accepted, rejected, cls =
        match prod, model with
        | Ok pt, ModelCol.Ok mt ->
            (if tableToModelWith bridge pt <> mt then
                 [ sprintf "accepted result differs — %s" where ]
             else
                 []),
            1,
            0,
            None
        | Error pe, ModelCol.Error me ->
            let pr = prodColRejRender pe
            let mr = modelColRejRender me

            (if pr <> mr then
                 [ sprintf "rejection differs — %s\n  production: %s\n  oracle:     %s" where pr mr ]
             else
                 []),
            0,
            1,
            Some(colRejClass pe)
        | Ok _, ModelCol.Error me ->
            [ sprintf "production ACCEPTED but the oracle rejected (%s) — %s" (modelColRejRender me) where ], 0, 0, None
        | Error pe, ModelCol.Ok _ ->
            [ sprintf "production REJECTED (%s) but the oracle accepted — %s" (prodColRejRender pe) where ], 0, 0, None

    // 2. canApply, each side against the other and against its own apply
    let canDiff =
        let pv =
            match ColumnOps.canApply op st with
            | Ok() -> "ok"
            | Error e -> prodColRejRender e

        let mv =
            match ModelCol.can_apply modelEvaluator mop mst with
            | ModelCol.Ok() -> "ok"
            | ModelCol.Error e -> modelColRejRender e

        let pa =
            match prod with
            | Ok _ -> "ok"
            | Error e -> prodColRejRender e

        let ma =
            match model with
            | ModelCol.Ok _ -> "ok"
            | ModelCol.Error e -> modelColRejRender e

        (if pv <> mv then
             [ sprintf "canApply differs — %s\n  production: %s\n  oracle:     %s" where pv mv ]
         else
             [])
        @ (if pv <> pa then
               [ sprintf "production's canApply and apply DISAGREE — %s" where ]
           else
               [])
        @ (if mv <> ma then
               [ sprintf "the oracle's can_apply and apply DISAGREE — %s" where ]
           else
               [])

    // 3. invert — the derived inverse, and the round trip where the theorem claims it
    let pInv = ColumnOps.invert op st
    let mInv = ModelCol.invert modelEvaluator mop mst

    let invDiff =
        match pInv, mInv with
        | Ok pi, ModelCol.Ok mi ->
            if prodColOpRender pi <> modelColOpRender mi then
                [ sprintf
                      "the derived INVERSE differs — %s\n  production: %s\n  oracle:     %s"
                      where
                      (prodColOpRender pi)
                      (modelColOpRender mi) ]
            else
                []
        | Error pe, ModelCol.Error me ->
            if prodColRejRender pe <> modelColRejRender me then
                [ sprintf "invert's rejection differs — %s" where ]
            else
                []
        | _ -> [ sprintf "invert's verdict differs — %s" where ]

    let inverted, roundTripped, outsideWf, outsideWfFailed, roundTripDiff =
        match prod, pInv with
        | Ok pt, Ok pi when colInvertible op ->
            let back = ColumnOps.apply pi pt

            let mBack =
                match model, mInv with
                | ModelCol.Ok mt, ModelCol.Ok mi -> Some(ModelCol.apply modelEvaluator mi mt)
                | _ -> None

            let restored = (back = Ok st)
            let mRestored = (mBack = Some(ModelCol.Ok mst))

            if wfPre then
                1,
                1,
                0,
                0,
                (if not restored then
                     [ sprintf "the inverse did NOT restore a WELL-FORMED input on production — %s (got %A)" where back ]
                 else
                     [])
                @ (if not mRestored then
                       [ sprintf "the ORACLE's inverse did not restore a well-formed input — %s" where ]
                   else
                       [])
            else
                1, 0, 1, (if restored then 0 else 1), []
        | _ -> 0, 0, 0, 0, []

    // 4. the invariant, on production's result, judged by the model's `wf`
    let wfPreserved, wfDiff =
        match prod with
        | Ok pt when wfPre && colStructural op ->
            if ModelCol.wf (tableToModelWith bridge pt) then
                1, []
            else
                0, [ sprintf "a well-formed table and an accepted structural op gave a MALFORMED result — %s" where ]
        | _ -> 0, []

    { Diffs = acc.Diffs @ applyDiff @ canDiff @ invDiff @ roundTripDiff @ wfDiff
      Accepted = acc.Accepted + accepted
      Rejected = acc.Rejected + rejected
      Classes =
        (match cls with
         | Some c -> Set.add c acc.Classes
         | None -> acc.Classes)
        |> fun s ->
            match pInv with
            | Error(NotInvertible _) -> Set.add "NotInvertible" s
            | _ -> s
      Inverted = acc.Inverted + inverted
      RoundTripped = acc.RoundTripped + roundTripped
      RoundTripsOutsideWf = acc.RoundTripsOutsideWf + outsideWf
      RoundTripFailuresOutsideWf = acc.RoundTripFailuresOutsideWf + outsideWfFailed
      WfPre = acc.WfPre + (if wfPre then 1 else 0)
      WfPreserved = acc.WfPreserved + wfPreserved
      Scripts = acc.Scripts }

/// Every op of every generated script at every state the script reaches, plus the script whole.
let private colDifferential (bridge: Cell -> ModelCol.cell) (seed: int) (trials: int) : ColTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = emptyColTally

    for _ in 1..trials do
        let start, r1 = genColTable r
        r <- r1
        let mutable st = start
        let ops = System.Collections.Generic.List<ColumnOp>()

        for _ in 1..6 do
            let op, r2 = genColOp st r
            r <- r2
            ops.Add op
            tally <- colProbe bridge op st tally

            match ColumnOps.apply op st with
            | Ok t -> st <- t
            | Error _ -> ()

        // 5. the script whole — short-circuit and all-or-nothing as one comparison
        let script = List.ofSeq ops
        let prod = ColumnOps.applyAll script start

        let model =
            ModelCol.apply_all
                modelEvaluator
                (script |> List.map (colOpToModelWith bridge))
                (tableToModelWith bridge start)

        let scriptDiff =
            match prod, model with
            | Ok pt, ModelCol.Ok mt when tableToModelWith bridge pt = mt -> []
            | Error pe, ModelCol.Error me when prodColRejRender pe = modelColRejRender me -> []
            | _ -> [ sprintf "applyAll differs on a %d-op script (seed %d)" (List.length script) seed ]

        tally <-
            { tally with
                Diffs = tally.Diffs @ scriptDiff
                Scripts = tally.Scripts + 1 }

    tally

type private ColDiffTally =
    { DDiffs: string list
      Pairs: int
      BothWf: int
      Granular: int
      Rebuild: int }

/// Pairs of tables: `toOps` as a script compared, and where both are well-formed the
/// reconstruction asserted on both sides. Half the pairs share a schema (the column-granular
/// branch); half are independent draws (the rebuild branch, almost always).
let private colDiffDifferential (seed: int) (trials: int) : ColDiffTally =
    let mutable r = ConfRng.ofSeed seed

    let mutable tally =
        { DDiffs = []
          Pairs = 0
          BothWf = 0
          Granular = 0
          Rebuild = 0 }

    for i in 1..trials do
        let before, r1 = genColTable r
        r <- r1

        let after, r2 =
            if i % 2 = 0 then
                genColTable r
            else
                // same schema, one column's cells redrawn — the granular branch's home
                match before.Columns with
                | [] -> before, r
                | cols ->
                    let idx, r3 = ConfRng.intBelow (List.length cols) r
                    let target = List.item idx cols
                    let cells, r4 = genColCells target.Type (List.length (Column.toCells target)) r3

                    { before with
                        Columns =
                            cols
                            |> List.mapi (fun j c ->
                                if j = idx then
                                    KitColumn.create c.Name c.Type (cells)
                                else
                                    c) },
                    r4

        r <- r2
        let mb = tableToModel before
        let ma = tableToModel after
        let pScript = ColumnOps.toOps before after
        let mScript = ModelCol.to_ops mb ma

        let granular =
            (before.Schema = after.Schema && Table.rowCount before = Table.rowCount after)

        let scriptDiff =
            if (pScript |> List.map prodColOpRender) <> (mScript |> List.map modelColOpRender) then
                [ sprintf "toOps emits a different script (pair %d)" i ]
            else
                []

        let bothWf = ModelCol.wf mb && ModelCol.wf ma

        let reconDiff =
            if bothWf then
                (if ColumnOps.applyAll pScript before <> Ok after then
                     [ sprintf
                           "applyAll (toOps before after) before is NOT after on production (pair %d, %s)"
                           i
                           (if granular then "granular" else "rebuild") ]
                 else
                     [])
                @ (if ModelCol.apply_all modelEvaluator mScript mb <> ModelCol.Ok ma then
                       [ sprintf "the ORACLE's diff does not reconstruct (pair %d)" i ]
                   else
                       [])
            else
                []

        tally <-
            { DDiffs = tally.DDiffs @ scriptDiff @ reconDiff
              Pairs = tally.Pairs + 1
              BothWf = tally.BothWf + (if bothWf then 1 else 0)
              Granular = tally.Granular + (if bothWf && granular then 1 else 0)
              Rebuild = tally.Rebuild + (if bothWf && not granular then 1 else 0) }

    tally


// ------------------------------------------------------------------------------------------
// Phase 154 — the COUNTED PIPELINE DRIVER; Phase 234 — its EXPRESSION EVALUATOR made concrete.
// `proofs/Pipeline.fst` models `Fuaran.Compute.DataFrame`'s closed `ColExpr` and `Transform`
// algebra, the private `evalExpr` with its four inner loops, `evalFilter`, `evalDerive`,
// `evalStep`'s dispatch and `evalPipelineWithInEnvCounted`'s fold with its cost model, clause
// for clause, over TWO parameters: the CELL PRIMITIVES (`prims` — `arith` / `comparison` /
// `logical` / `stringPred` as one function of the operator, `castCell`, `applyScalar`,
// `compareCells`) and the TWELVE VERBS that evaluate no expression (`other_fn`).
// `proofs/oracle/Pipeline.fs` is that model extracted. This runs it BESIDE production over the
// `conformance/laws/transform-laws.json` vectors (decoded with the shipped codec) and a generated
// sample — the vectors' own draw recipe, WIDENED to reach all fourteen verbs and all fifteen
// expression kinds, with a `Ref` resolver, a param env, embedded and referenced right-hand
// sources, and expressions that refuse — comparing the TABLE (byte for byte through
// `ColumnCodec.encode`) and the COUNT, or the named `EvalError`.
//
// Both parameters are instantiated FROM production. Each cell primitive is read through the
// public `evalExprInRow` on a one-node expression over its literal arguments, so the model's
// `eval_expr` runs production's arithmetic, coercions and float layout under the model's own
// recursion — which is what `visits_le_nodes` is about. The twelve verbs are production's own,
// one verb at a time, through the public entry point: `evalPipelineWithInEnv resolve env [ step ]`
// over the frame crossed back to a `Table`. So a `Filter` or a `Derive` is evaluated by the MODEL
// (its loop, its short circuits, its replace-or-append) and compared to production's table byte
// for byte, while the other twelve are shared rather than compared. A float cell crosses as its
// round-trip `R` text and back; a `Table` crosses as its row-major view (the transpose `toFrame`
// / `ofFrame` perform), so the bridge never hands the model a zero-column frame with rows — a
// `Table` cannot carry one — and the generator keeps every `Project` to at least one column for
// that reason. The param env crosses as `Map.toList`, the sets-and-maps-are-lists bridge.
// ------------------------------------------------------------------------------------------

let private pColToModel (t: ColumnType) : ModelPipe.column_type =
    match t with
    | IntType -> ModelPipe.IntType
    | FloatType -> ModelPipe.FloatType
    | BoolType -> ModelPipe.BoolType
    | StringType -> ModelPipe.StringType
    | DateType -> ModelPipe.DateType
    | TimestampType _ -> ModelPipe.TimestampType
    | DecimalType -> ModelPipe.DecimalType

let private pColOfModel (t: ModelPipe.column_type) : ColumnType =
    match t with
    | ModelPipe.IntType -> IntType
    | ModelPipe.FloatType -> FloatType
    | ModelPipe.BoolType -> BoolType
    | ModelPipe.StringType -> StringType
    | ModelPipe.DateType -> DateType
    | ModelPipe.TimestampType -> TimestampType TimeUnit.Seconds
    | ModelPipe.DecimalType -> DecimalType

let private pCellToModel (c: Cell) : ModelPipe.cell =
    match c with
    | Cell.Int v -> ModelPipe.Int(bigint v)
    | Cell.Float v -> ModelPipe.Float(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
    | Cell.Bool v -> ModelPipe.Bool v
    | Cell.Str v -> ModelPipe.Str v
    | Cell.Date v -> ModelPipe.Date v
    | Cell.Timestamp v -> ModelPipe.Timestamp v
    | Cell.Null -> ModelPipe.Null
    | Cell.Decimal v -> ModelPipe.Decimal v

let private pCellOfModel (c: ModelPipe.cell) : Cell =
    match c with
    | ModelPipe.Int v -> Cell.Int(int v)
    | ModelPipe.Float s ->
        Cell.Float(
            System.Double.Parse(
                s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture
            )
        )
    | ModelPipe.Bool v -> Cell.Bool v
    | ModelPipe.Str v -> Cell.Str v
    | ModelPipe.Date v -> Cell.Date v
    | ModelPipe.Timestamp v -> Cell.Timestamp v
    | ModelPipe.Null -> Cell.Null
    | ModelPipe.Decimal v -> Cell.Decimal v

/// A closed enumeration crosses through one table read both ways, so a case the table misses is
/// a `KeyNotFoundException` on the first draw that reaches it rather than a silent default.
let private pFwd (table: ('a * 'b) list) (x: 'a) : 'b =
    table |> List.find (fun (n, _) -> n = x) |> snd

let private pBack (table: ('a * 'b) list) (y: 'b) : 'a =
    table |> List.find (fun (_, b) -> b = y) |> fst

let private pBinOps: (BinOp * ModelPipe.bin_op) list =
    [ Add, ModelPipe.Add
      Sub, ModelPipe.Sub
      Mul, ModelPipe.Mul
      Div, ModelPipe.Div
      Mod, ModelPipe.Mod
      Eq, ModelPipe.Eq
      Ne, ModelPipe.Ne
      Lt, ModelPipe.Lt
      Le, ModelPipe.Le
      Gt, ModelPipe.Gt
      Ge, ModelPipe.Ge
      And, ModelPipe.And
      Or, ModelPipe.Or
      Contains, ModelPipe.Contains
      StartsWith, ModelPipe.StartsWith
      EndsWith, ModelPipe.EndsWith ]

let private pScalarFns: (ScalarFn * ModelPipe.scalar_fn) list =
    [ Abs, ModelPipe.Abs
      Round, ModelPipe.Round
      Floor, ModelPipe.Floor
      Ceil, ModelPipe.Ceil
      Length, ModelPipe.Length
      Lower, ModelPipe.Lower
      Upper, ModelPipe.Upper
      Substr, ModelPipe.Substr
      DatePart, ModelPipe.DatePart
      Concat, ModelPipe.Concat
      Trim, ModelPipe.Trim
      Replace, ModelPipe.Replace
      DateDiffDays, ModelPipe.DateDiffDays
      Sqrt, ModelPipe.Sqrt
      Least, ModelPipe.Least
      Greatest, ModelPipe.Greatest
      IndexOf, ModelPipe.IndexOf ]

/// Phase 277 — the seven rounding modes, one table read both ways.
let private pModes: (RoundingMode * ModelPipe.rounding_mode) list =
    [ RoundingMode.HalfEven, ModelPipe.RoundHalfEven
      RoundingMode.HalfUp, ModelPipe.RoundHalfUp
      RoundingMode.HalfDown, ModelPipe.RoundHalfDown
      RoundingMode.Up, ModelPipe.RoundUp
      RoundingMode.Down, ModelPipe.RoundDown
      RoundingMode.Ceiling, ModelPipe.RoundCeiling
      RoundingMode.Floor, ModelPipe.RoundFloor ]

let private pAggFns: (AggFn * ModelPipe.agg_fn) list =
    [ Sum, ModelPipe.Sum
      Mean, ModelPipe.Mean
      Min, ModelPipe.Min
      Max, ModelPipe.Max
      Count, ModelPipe.Count
      Median, ModelPipe.Median
      StdDev, ModelPipe.StdDev
      First, ModelPipe.First
      Last, ModelPipe.Last
      CountDistinct, ModelPipe.CountDistinct ]

let private pWindowFns: (WindowFn * ModelPipe.window_fn) list =
    [ RowNumber, ModelPipe.RowNumber
      Rank, ModelPipe.Rank
      Lag, ModelPipe.Lag
      Lead, ModelPipe.Lead
      CumulSum, ModelPipe.CumulSum
      RollingMean, ModelPipe.RollingMean
      DenseRank, ModelPipe.DenseRank
      CompetitionRank, ModelPipe.CompetitionRank
      CumulMax, ModelPipe.CumulMax
      CumulMin, ModelPipe.CumulMin
      RollingSum, ModelPipe.RollingSum ]

let private pWindowFnToModel (f: WindowFn) : ModelPipe.window_fn =
    match f with
    | NTile n -> ModelPipe.NTile(bigint n)
    | other -> pFwd pWindowFns other

let private pWindowFnOfModel (f: ModelPipe.window_fn) : WindowFn =
    match f with
    | ModelPipe.NTile n -> NTile(int n)
    | other -> pBack pWindowFns other

let private pJoinKinds: (JoinKind * ModelPipe.join_kind) list =
    [ Inner, ModelPipe.Inner
      Left, ModelPipe.Left
      Right, ModelPipe.Right
      Outer, ModelPipe.Outer
      Semi, ModelPipe.Semi
      Anti, ModelPipe.Anti ]

let private pSortDirs: (SortDir * ModelPipe.sort_dir) list =
    [ Asc, ModelPipe.Asc; Desc, ModelPipe.Desc ]

let private pGrains: (NowGrain * ModelPipe.now_grain) list =
    [ NowGrain.Date, ModelPipe.GrainDate
      NowGrain.Timestamp, ModelPipe.GrainTimestamp ]

let private pSlotToModel (f: 'a -> 'b) (s: Slot<'a>) : ModelPipe.slot<'b> =
    match s with
    | Slot.Lit v -> ModelPipe.SlotLit(f v)
    | Slot.Param n -> ModelPipe.SlotParam n

let private pSlotOfModel (f: 'b -> 'a) (s: ModelPipe.slot<'b>) : Slot<'a> =
    match s with
    | ModelPipe.SlotLit v -> Slot.Lit(f v)
    | ModelPipe.SlotParam n -> Slot.Param n

let private pRoundingToModel (r: Rounding) : ModelPipe.rounding =
    { ModelPipe.rounding.r_scale = pSlotToModel (fun (v: int) -> bigint v) r.Scale
      ModelPipe.rounding.r_mode = pFwd pModes r.Mode }

let private pRoundingOfModel (r: ModelPipe.rounding) : Rounding =
    { Scale = pSlotOfModel int r.r_scale
      Mode = pBack pModes r.r_mode }

let rec private pExprToModel (e: ColExpr) : ModelPipe.col_expr =
    match e with
    | Col n -> ModelPipe.Col n
    | Lit c -> ModelPipe.Lit(pCellToModel c)
    | Param n -> ModelPipe.Param n
    | Binary(op, a, b) -> ModelPipe.Binary(pFwd pBinOps op, pExprToModel a, pExprToModel b)
    | Not x -> ModelPipe.Not(pExprToModel x)
    | Coalesce xs -> ModelPipe.Coalesce(xs |> List.map pExprToModel)
    | Case(cases, els) ->
        ModelPipe.Case(cases |> List.map (fun (w, t) -> pExprToModel w, pExprToModel t), pExprToModel els)
    | Cast(ty, x) -> ModelPipe.Cast(pColToModel ty, pExprToModel x)
    | ApplyFn(fn, xs) -> ModelPipe.ApplyFn(pFwd pScalarFns fn, xs |> List.map pExprToModel)
    | InList(x, items) -> ModelPipe.InList(pExprToModel x, items |> List.map pExprToModel)
    | IsNull x -> ModelPipe.IsNull(pExprToModel x)
    | InParam(x, n) -> ModelPipe.InParam(pExprToModel x, n)
    | Now g -> ModelPipe.Now(pFwd pGrains g)
    | Quotient(a, b, r) -> ModelPipe.Quotient(pExprToModel a, pExprToModel b, pRoundingToModel r)
    | Rounded(x, r) -> ModelPipe.Rounded(pExprToModel x, pRoundingToModel r)
    // Phase 429: the target unit crosses as its canonical text (one product, one text).
    | Convert(x, u) -> ModelPipe.Convert(pExprToModel x, Unit.render u)

/// A unit back from its canonical text — the model carries texts, production the value.
let private pUnitOfText (text: string) : UnitOfMeasure =
    match Unit.parse text with
    | Ok u -> u
    | Error e -> failtestf "the model named a unit text production does not parse: '%s' (%A)" text e

let rec private pExprOfModel (e: ModelPipe.col_expr) : ColExpr =
    match e with
    | ModelPipe.Col n -> Col n
    | ModelPipe.Lit c -> Lit(pCellOfModel c)
    | ModelPipe.Param n -> Param n
    | ModelPipe.Binary(op, a, b) -> Binary(pBack pBinOps op, pExprOfModel a, pExprOfModel b)
    | ModelPipe.Not x -> Not(pExprOfModel x)
    | ModelPipe.Coalesce xs -> Coalesce(xs |> List.map pExprOfModel)
    | ModelPipe.Case(cases, els) ->
        Case(cases |> List.map (fun (w, t) -> pExprOfModel w, pExprOfModel t), pExprOfModel els)
    | ModelPipe.Cast(ty, x) -> Cast(pColOfModel ty, pExprOfModel x)
    | ModelPipe.ApplyFn(fn, xs) -> ApplyFn(pBack pScalarFns fn, xs |> List.map pExprOfModel)
    | ModelPipe.InList(x, items) -> InList(pExprOfModel x, items |> List.map pExprOfModel)
    | ModelPipe.IsNull x -> IsNull(pExprOfModel x)
    | ModelPipe.InParam(x, n) -> InParam(pExprOfModel x, n)
    | ModelPipe.Now g -> Now(pBack pGrains g)
    | ModelPipe.Convert(x, text) -> Convert(pExprOfModel x, pUnitOfText text)
    | ModelPipe.Quotient(a, b, r) -> Quotient(pExprOfModel a, pExprOfModel b, pRoundingOfModel r)
    | ModelPipe.Rounded(x, r) -> Rounded(pExprOfModel x, pRoundingOfModel r)

/// A `Table` as its row-major view — the transpose `toFrame` performs.
let private pFrameOfTable (t: Table) : ModelPipe.frame =
    let n = Table.rowCount t
    let cols = t.Columns |> List.map (fun c -> List.toArray (Column.toCells c))

    { ModelPipe.frame.cols = t.Schema |> List.map (fun f -> f.Name, pColToModel f.Type)
      ModelPipe.frame.rows = [ for i in 0 .. n - 1 -> cols |> List.map (fun a -> pCellToModel a[i]) ] }

/// The transpose back — `ofFrame`.
let private pTableOfFrame (f: ModelPipe.frame) : Table =
    let arrs = f.rows |> List.map List.toArray

    { Schema = f.cols |> List.map (fun (name, ty) -> Field.create name (pColOfModel ty))
      Columns =
        f.cols
        |> List.mapi (fun ci (name, ty) ->
            KitColumn.create name (pColOfModel ty) (arrs |> List.map (fun r -> pCellOfModel r[ci]))) }

let private pSourceToModel (s: DataSource) : ModelPipe.data_source =
    match s with
    | Embedded t -> ModelPipe.Embedded(pFrameOfTable t)
    | Ref r -> ModelPipe.Ref r

let private pSourceOfModel (s: ModelPipe.data_source) : DataSource =
    match s with
    | ModelPipe.Embedded f -> Embedded(pTableOfFrame f)
    | ModelPipe.Ref r -> Ref r

let private pTransformToModel (t: Transform) : ModelPipe.transform =
    match t with
    | Filter e -> ModelPipe.Filter(pExprToModel e)
    | Project pairs -> ModelPipe.Project pairs
    | Derive(name, e) -> ModelPipe.Derive(name, pExprToModel e)
    | GroupBy(keys, aggs) ->
        ModelPipe.GroupBy(
            keys,
            aggs
            |> List.map (fun a ->
                { ModelPipe.agg.a_name = a.Name
                  ModelPipe.agg.a_fn = pFwd pAggFns a.Fn
                  ModelPipe.agg.a_of = a.Of })
        )
    | Join(src, on, kind) -> ModelPipe.Join(pSourceToModel src, on, pFwd pJoinKinds kind)
    | Window w ->
        ModelPipe.Window
            { ModelPipe.window_spec.partition_by = w.PartitionBy
              ModelPipe.window_spec.order_by = w.OrderBy |> List.map (fun (c, d) -> c, pFwd pSortDirs d)
              ModelPipe.window_spec.w_fn = pWindowFnToModel w.Fn
              ModelPipe.window_spec.w_of = w.Of
              ModelPipe.window_spec.w_as = w.As }
    | Pivot p ->
        ModelPipe.Pivot
            { ModelPipe.pivot_spec.p_index = p.Index
              ModelPipe.pivot_spec.p_on = p.On
              ModelPipe.pivot_spec.p_values = p.Values
              ModelPipe.pivot_spec.p_agg = pFwd pAggFns p.Agg }
    | Unpivot(idVars, valueVars) -> ModelPipe.Unpivot(idVars, valueVars)
    | Sort by -> ModelPipe.Sort(by |> List.map (fun (c, d) -> pSlotToModel id c, pFwd pSortDirs d))
    | Distinct -> ModelPipe.Distinct
    | Limit(n, offset) ->
        ModelPipe.Limit(pSlotToModel (fun (v: int) -> bigint v) n, pSlotToModel (fun (v: int) -> bigint v) offset)
    | Union src -> ModelPipe.Union(pSourceToModel src)
    | Intersect src -> ModelPipe.Intersect(pSourceToModel src)
    | Except src -> ModelPipe.Except(pSourceToModel src)

let private pTransformOfModel (t: ModelPipe.transform) : Transform =
    match t with
    | ModelPipe.Filter e -> Filter(pExprOfModel e)
    | ModelPipe.Project pairs -> Project pairs
    | ModelPipe.Derive(name, e) -> Derive(name, pExprOfModel e)
    | ModelPipe.GroupBy(keys, aggs) ->
        GroupBy(
            keys,
            aggs
            |> List.map (fun a ->
                { Name = a.a_name
                  Fn = pBack pAggFns a.a_fn
                  Of = a.a_of })
        )
    | ModelPipe.Join(src, on, kind) -> Join(pSourceOfModel src, on, pBack pJoinKinds kind)
    | ModelPipe.Window w ->
        Window
            { PartitionBy = w.partition_by
              OrderBy = w.order_by |> List.map (fun (c, d) -> c, pBack pSortDirs d)
              Fn = pWindowFnOfModel w.w_fn
              Of = w.w_of
              As = w.w_as }
    | ModelPipe.Pivot p ->
        Pivot
            { Index = p.p_index
              On = p.p_on
              Values = p.p_values
              Agg = pBack pAggFns p.p_agg }
    | ModelPipe.Unpivot(idVars, valueVars) -> Unpivot(idVars, valueVars)
    | ModelPipe.Sort by -> Sort(by |> List.map (fun (c, d) -> pSlotOfModel id c, pBack pSortDirs d))
    | ModelPipe.Distinct -> Distinct
    | ModelPipe.Limit(n, offset) -> Limit(pSlotOfModel int n, pSlotOfModel int offset)
    | ModelPipe.Union src -> Union(pSourceOfModel src)
    | ModelPipe.Intersect src -> Intersect(pSourceOfModel src)
    | ModelPipe.Except src -> Except(pSourceOfModel src)

/// The pipeline crossed to the model, each step through `perturb` first — the identity for the
/// faithful bridge, and the go-reds' lever.
let private pPipelineToModelWith (perturb: Transform -> Transform) (p: Transform list) : ModelPipe.transform list =
    p |> List.map (perturb >> pTransformToModel)

let private pPipelineToModel (p: Transform list) : ModelPipe.transform list = pPipelineToModelWith id p

let private pRenderError (e: EvalError) : string = sprintf "%A" e

/// `EvalError` crossed to the model's `eval_error`, case for case — the two closed alphabets.
let private pErrToModel (e: EvalError) : ModelPipe.eval_error =
    match e with
    | UnknownColumn(name, available) -> ModelPipe.UnknownColumn(name, available)
    | TypeError detail -> ModelPipe.TypeError detail
    | AggError detail -> ModelPipe.AggError detail
    | JoinError detail -> ModelPipe.JoinError detail
    | ArityError(fn, expected, got) -> ModelPipe.ArityError(fn, bigint expected, bigint got)
    | UnresolvedSource r -> ModelPipe.UnresolvedSource r
    | OverflowError detail -> ModelPipe.OverflowError detail
    | UnboundParam(name, bound) -> ModelPipe.UnboundParam(name, bound)
    | UnpinnedClock grain -> ModelPipe.UnpinnedClock(pFwd pGrains grain)
    | UnitMismatch(step, at, l, r) -> ModelPipe.UnitMismatch(step, pExprToModel at, l, r)

let private pErrOfModel (e: ModelPipe.eval_error) : EvalError =
    match e with
    | ModelPipe.UnknownColumn(name, available) -> UnknownColumn(name, available)
    | ModelPipe.TypeError detail -> TypeError detail
    | ModelPipe.AggError detail -> AggError detail
    | ModelPipe.JoinError detail -> JoinError detail
    | ModelPipe.ArityError(fn, expected, got) -> ArityError(fn, int expected, int got)
    | ModelPipe.UnresolvedSource r -> UnresolvedSource r
    | ModelPipe.OverflowError detail -> OverflowError detail
    | ModelPipe.UnboundParam(name, bound) -> UnboundParam(name, bound)
    | ModelPipe.UnpinnedClock grain -> UnpinnedClock(pBack pGrains grain)
    | ModelPipe.UnitMismatch(step, at, l, r) -> UnitMismatch(step, pExprOfModel at, l, r)

/// The param env as the model reads it: `Map.toList`, sorted by key — the sets-and-maps-are-lists
/// bridge, and exactly the list `UnboundParam` enumerates.
let private pEnvToModel (env: Map<string, Cell>) : ModelPipe.param_env =
    env |> Map.toList |> List.map (fun (k, v) -> k, pCellToModel v)

/// A one-node evaluation on production — the way each private primitive is read out through the
/// public entry point. No column, no param: a literal-only expression reads neither.
let private pOneNode (e: ColExpr) : ModelPipe.outcome<ModelPipe.cell, ModelPipe.eval_error> =
    match DataFrame.evalExprInRow Map.empty [] [] e with
    | Ok c -> ModelPipe.Ok(pCellToModel c)
    | Error err -> ModelPipe.Error(pErrToModel err)

/// The model's FIRST parameter, instantiated at production's own primitives. `Binary`'s operator
/// dispatch is read through a two-literal `Binary`, `castCell` through a `Cast` of a literal,
/// `applyScalar` through an `ApplyFn` over literals, and `compareCells` through a one-item `InList`
/// — which answers `Bool true` on `Some 0`, `Bool false` on `Some _`, and the incompatible-types
/// `TypeError` on `None`, the three readings the model's `InList` arm makes of it. The model
/// never asks `compare` about a `Null` (its arm tests for one first), so the null answers a
/// one-item `InList` gives are never read.
let private pPrims: ModelPipe.prims =
    { ModelPipe.prims.binary =
        fun op a b -> pOneNode (Binary(pBack pBinOps op, Lit(pCellOfModel a), Lit(pCellOfModel b)))
      ModelPipe.prims.cast_cell = fun ty c -> pOneNode (Cast(pColOfModel ty, Lit(pCellOfModel c)))
      ModelPipe.prims.apply_fn =
        fun fn vs -> pOneNode (ApplyFn(pBack pScalarFns fn, vs |> List.map (pCellOfModel >> Lit)))
      ModelPipe.prims.compare =
        fun a b ->
            match DataFrame.evalExprInRow Map.empty [] [] (InList(Lit(pCellOfModel a), [ Lit(pCellOfModel b) ])) with
            | Ok(Cell.Bool true) -> FStar_Pervasives_Native.Some 0I
            | Ok(Cell.Bool false) -> FStar_Pervasives_Native.Some 1I
            | _ -> FStar_Pervasives_Native.None
      // Phase 277 — the two rounding primitives, through a one-node expression at a literal scale.
      // A scale past int32 cannot reach production; the model's differential never draws one.
      ModelPipe.prims.quotient =
        fun m n a b ->
            pOneNode (
                Quotient(
                    Lit(pCellOfModel a),
                    Lit(pCellOfModel b),
                    { Scale = Slot.Lit(int n)
                      Mode = pBack pModes m }
                )
            )
      ModelPipe.prims.rounded =
        fun m n a ->
            pOneNode (
                Rounded(
                    Lit(pCellOfModel a),
                    { Scale = Slot.Lit(int n)
                      Mode = pBack pModes m }
                )
            )
      // Phase 470 (D38) — the packing's two parameters, at the functions `Vec.packAt` calls, as the
      // frame model's host (`fHost`) carries them: the float carrier of `float v`, and `string v`
      // (`ScaledDecimal.scaleOf` was a third until Phase 472: the packing decides no verdict now).
      ModelPipe.prims.float_of_int =
        fun x -> (float (int x)).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
      ModelPipe.prims.int_text = fun x -> string (int x) }

/// The model's SECOND parameter — the twelve verbs that evaluate no expression — instantiated at
/// production's own, one verb through the public entry point, over the frame crossed back to a
/// `Table`. This is the faithful instantiation — the model's transform is what is evaluated. (It
/// answers a `Filter` or a `Derive` too, but the model never asks it one: those two arms are the
/// model's own.)
let private pStepOfProduction
    (resolve: string -> Result<Table, EvalError>)
    (env: Map<string, Cell>)
    : ModelPipe.other_fn =
    fun f t ->
        match DataFrame.evalPipelineWithInEnv resolve env [ pTransformOfModel t ] (pTableOfFrame f) with
        | Ok t' -> ModelPipe.Ok(pFrameOfTable t')
        | Error e -> ModelPipe.Error(pErrToModel e)

/// The faithful step evaluator in the differential's shape — it reads no production pipeline,
/// because the model's transform is what it evaluates.
let private pFaithfulStep
    (resolve: string -> Result<Table, EvalError>)
    (env: Map<string, Cell>)
    (_: Transform list)
    : ModelPipe.other_fn =
    pStepOfProduction resolve env

/// The go-red's step evaluator: it walks PRODUCTION's pipeline in lock-step and ignores the
/// transform the model hands it, so the model's transform reaches `costOf` and nothing else. Behind
/// a bridge that crosses a `Derive` as a `Distinct`, that is a model whose count skips one step
/// kind while every table stays right — which is exactly what the count comparison has to catch.
/// Since Phase 234 the model evaluates every `Filter` itself and asks this parameter about nothing
/// else, so the cursor walks production's NON-`Filter` steps: the model's own Filters keep the two
/// walks aligned (the faithful case is what says they agree), and each bridged `Distinct` lands on
/// the `Derive` it stands for.
let private pStepLockstep
    (resolve: string -> Result<Table, EvalError>)
    (env: Map<string, Cell>)
    (production: Transform list)
    : ModelPipe.other_fn =
    let cursor =
        ref (
            production
            |> List.filter (function
                | Filter _ -> false
                | _ -> true)
        )

    fun f _ ->
        match cursor.Value with
        | s :: rest ->
            cursor.Value <- rest

            match DataFrame.evalPipelineWithInEnv resolve env [ s ] (pTableOfFrame f) with
            | Ok t' -> ModelPipe.Ok(pFrameOfTable t')
            | Error e -> ModelPipe.Error(pErrToModel e)
        | [] ->
            ModelPipe.Error(
                ModelPipe.TypeError "lock-step cursor exhausted: the model asked for a step production does not have"
            )

let private pVerbTag (t: Transform) : string =
    match t with
    | Filter _ -> "Filter"
    | Project _ -> "Project"
    | Derive _ -> "Derive"
    | GroupBy _ -> "GroupBy"
    | Join _ -> "Join"
    | Window _ -> "Window"
    | Pivot _ -> "Pivot"
    | Unpivot _ -> "Unpivot"
    | Sort _ -> "Sort"
    | Distinct -> "Distinct"
    | Limit _ -> "Limit"
    | Union _ -> "Union"
    | Intersect _ -> "Intersect"
    | Except _ -> "Except"

let rec private pExprTags (e: ColExpr) : string list =
    match e with
    | Col _ -> [ "Col" ]
    | Lit _ -> [ "Lit" ]
    | Param _ -> [ "Param" ]
    | Binary(_, a, b) -> "Binary" :: pExprTags a @ pExprTags b
    | Not x -> "Not" :: pExprTags x
    | Coalesce xs -> "Coalesce" :: List.collect pExprTags xs
    | Case(cases, els) ->
        "Case" :: (cases |> List.collect (fun (w, t) -> pExprTags w @ pExprTags t))
        @ pExprTags els
    | Cast(_, x) -> "Cast" :: pExprTags x
    | ApplyFn(_, xs) -> "ApplyFn" :: List.collect pExprTags xs
    | InList(x, items) -> "InList" :: pExprTags x @ List.collect pExprTags items
    | IsNull x -> "IsNull" :: pExprTags x
    | InParam(x, _) -> "InParam" :: pExprTags x
    | Now _ -> [ "Now" ]
    | Quotient(a, b, _) -> "Quotient" :: pExprTags a @ pExprTags b
    | Rounded(x, _) -> "Rounded" :: pExprTags x
    | Convert(x, _) -> "Convert" :: pExprTags x

type private PipeTally =
    {
        /// Every disagreement, rendered — the tally's verdict is that this is empty.
        PDiffs: string list
        PCompared: int
        /// Pipelines both sides evaluated to a table.
        POk: int
        /// Pipelines both sides refused.
        PErr: int
        /// The counts compared, summed — so the count half is known to have been reached.
        PCountTotal: int
        /// Pipelines that evaluated to a table with a NONZERO count.
        PCounted: int
        /// The verb kinds reached, by tag.
        PVerbs: Set<string>
        /// The expression kinds reached, by tag.
        PExprs: Set<string>
        /// Pipelines whose model crossing crossed BACK to the same pipeline.
        PRoundTrips: int
        /// The pipelines that did not, rendered — so a lossy bridge names what it lost.
        PLossy: string list
    }

let private pEmptyTally =
    { PDiffs = []
      PCompared = 0
      POk = 0
      PErr = 0
      PCountTotal = 0
      PCounted = 0
      PVerbs = Set.empty
      PExprs = Set.empty
      PRoundTrips = 0
      PLossy = [] }

/// One comparison: production's counted evaluator against the model's, over one pipeline and one
/// input, with the step evaluator `mkStep` builds for that pipeline.
let private pCompare
    (label: string)
    (resolve: string -> Result<Table, EvalError>)
    (env: Map<string, Cell>)
    (bridge: Transform list -> ModelPipe.transform list)
    (mkStep: Transform list -> ModelPipe.other_fn)
    (p: Transform list)
    (input: Table)
    (t: PipeTally)
    : PipeTally =
    let prod = DataFrame.evalPipelineWithInEnvCounted resolve env p input

    let model =
        ModelPipe.eval_counted pPrims (mkStep p) (pEnvToModel env) (bridge p) (pFrameOfTable input)

    let verbs = p |> List.map pVerbTag |> Set.ofList

    let exprs =
        p
        |> List.collect (function
            | Filter e
            | Derive(_, e) -> pExprTags e
            | _ -> [])
        |> Set.ofList

    let back = pPipelineToModel p |> List.map pTransformOfModel
    let roundTrip = if back = p then 1 else 0

    let diff, ok, err, count =
        match prod, model with
        | Ok(table, n), ModelPipe.Ok(f, m) ->
            let a = ColumnCodec.encode (Embedded table)
            let b = ColumnCodec.encode (Embedded(pTableOfFrame f))

            if a <> b then
                Some(sprintf "%s: table differs — production %s, model %s" label a b), 1, 0, n
            elif bigint n <> m then
                Some(sprintf "%s: count differs — production %d, model %A" label n m), 1, 0, n
            else
                None, 1, 0, n
        | Error e, ModelPipe.Error me ->
            (if pErrToModel e <> me then
                 Some(
                     sprintf
                         "%s: error differs — production %s, model %s"
                         label
                         (pRenderError e)
                         (pRenderError (pErrOfModel me))
                 )
             else
                 None),
            0,
            1,
            0
        | Ok(_, n), ModelPipe.Error me ->
            Some(sprintf "%s: production evaluated, model refused %s" label (pRenderError (pErrOfModel me))), 1, 0, n
        | Error e, ModelPipe.Ok _ ->
            Some(sprintf "%s: production refused %s, model evaluated" label (pRenderError e)), 0, 1, 0

    { t with
        PDiffs =
            (match diff with
             | Some d -> d :: t.PDiffs
             | None -> t.PDiffs)
        PCompared = t.PCompared + 1
        POk = t.POk + ok
        PErr = t.PErr + err
        PCountTotal = t.PCountTotal + count
        PCounted = t.PCounted + (if ok = 1 && count > 0 then 1 else 0)
        PVerbs = Set.union t.PVerbs verbs
        PExprs = Set.union t.PExprs exprs
        PRoundTrips = t.PRoundTrips + roundTrip
        PLossy =
            if roundTrip = 1 then
                t.PLossy
            else
                sprintf "%s: %A crossed back as %A" label p back :: t.PLossy }

/// The `conformance/laws/transform-laws.json` vectors, decoded with the shipped codec: id, the
/// pipeline, the embedded source, and whether the file expects the reference to refuse.
let private pLawVectors () : (string * Transform list * Table * bool) list =
    use doc =
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Snapshots.repoFile "conformance/laws/transform-laws.json"))

    // Phase 321: the evalPipeline vectors; the columnOp and delta kinds after them run no pipeline.
    [ for v in
          doc.RootElement.GetProperty("vectors").EnumerateArray()
          |> Seq.filter (fun v -> v.GetProperty("case").GetString() = "evalPipeline") ->
          let id = v.GetProperty("id").GetString()
          let input = v.GetProperty("input")

          let pipeline =
              match DataFrameCodec.decodePipeline (input.GetProperty("pipeline").GetString()) with
              | Ok p -> p
              | Error e -> failtestf "vector %s: the pipeline did not decode: %A" id e

          let table =
              match ColumnCodec.decode (input.GetProperty("source").GetString()) with
              | Ok(Embedded t) -> t
              | Ok(Ref r) -> failtestf "vector %s: the source is a Ref %s, not an embedded table" id r
              | Error e -> failtestf "vector %s: the source did not decode: %A" id e

          let refuses = v.GetProperty("expected").GetProperty("verdict").GetString() = "error"
          id, pipeline, table, refuses ]

/// The generated sample — the vectors' own draw recipe for the table (a tie-heavy string key, an
/// int column carrying nulls, a float column, and since Phase 277 a decimal column), WIDENED in the pipeline: one to four steps over all
/// fourteen verbs, expressions over all fifteen kinds, a right-hand source that is embedded,
/// resolved through `resolve` or unresolvable, and slots that are literals or params.
let private pGenTable (rng: ConfRng.T) : Table * ConfRng.T =
    let extra, r1 = ConfRng.intBelow 4 rng
    let offset, r2 = ConfRng.intBelow 7 r1
    let rows = extra + 2
    let groupKeys = [| "a"; "b"; "c" |]
    let g = [ for i in 0 .. rows - 1 -> Cell.Str groupKeys[i % 3] ]

    let v =
        [ for i in 0 .. rows - 1 ->
              if (i + offset) % 4 = 0 then
                  Cell.Null
              else
                  Cell.Int(i * 3 + offset - 5) ]

    let w = [ for i in 0 .. rows - 1 -> Cell.Float(float (i + offset) / 2.0) ]

    // Phase 277 — an exact decimal column, so every verb and kind meets one: `v`'s value plus a
    // quarter, null where `v` is.
    let m =
        v
        |> List.map (fun c ->
            match c with
            | Cell.Int x -> Cell.decimal (string x + ".25") |> Option.defaultValue Cell.Null
            | _ -> Cell.Null)

    let table: Table =
        { Schema =
            [ Field.create "g" StringType
              Field.create "v" IntType
              Field.create "w" FloatType
              Field.create "m" DecimalType ]
          Columns =
            [ KitColumn.create "g" StringType g
              KitColumn.create "v" IntType v
              KitColumn.create "w" FloatType w
              KitColumn.create "m" DecimalType m ] }

    table, r2

let private pPick (xs: 'a list) (rng: ConfRng.T) : 'a * ConfRng.T =
    let i, r = ConfRng.intBelow (List.length xs) rng
    List.item i xs, r

let rec private pGenExpr (depth: int) (rng: ConfRng.T) : ColExpr * ConfRng.T =
    // Twenty draws over fifteen kinds (Phase 277's two share `InParam`'s draw): the seven extra land on the three leaves, so a tree is
    // mostly columns and literals with the rarer kinds (an unbound list param, an unpinned clock,
    // an unknown column) present but not dominant — enough refusals to compare, enough tables too.
    let k, r = ConfRng.intBelow (if depth = 0 then 3 else 20) rng

    match k with
    | 0
    | 13
    | 14
    | 15 ->
        pPick [ Col "g"; Col "v"; Col "w"; Col "v"; Col "w"; Col "nope"; Col "m" ] r
        |> fun (e, r) -> e, r
    | 1
    | 16
    | 17 ->
        pPick
            [ Lit(Cell.Int 1)
              Lit(Cell.Int 0)
              Lit(Cell.Float 2.5)
              Lit(Cell.Str "b")
              Lit(Cell.Bool true)
              Lit Cell.Null
              Lit(Cell.Decimal "1.5") ]
            r
    | 2
    | 18
    | 19 -> pPick [ Param "p"; Param "s"; Param "unbound" ] r
    | 3 ->
        let op, r1 = pPick (pBinOps |> List.map fst) r
        let a, r2 = pGenExpr (depth - 1) r1
        let b, r3 = pGenExpr (depth - 1) r2
        Binary(op, a, b), r3
    | 4 ->
        let x, r1 = pGenExpr (depth - 1) r
        Not x, r1
    | 5 ->
        let x, r1 = pGenExpr (depth - 1) r
        let y, r2 = pGenExpr (depth - 1) r1
        Coalesce [ x; y ], r2
    | 6 ->
        let w, r1 = pGenExpr (depth - 1) r
        let t, r2 = pGenExpr (depth - 1) r1
        let els, r3 = pGenExpr (depth - 1) r2
        Case([ w, t ], els), r3
    | 7 ->
        // The model's `TimestampType` carries no unit (it predates Core's Phase 422), so a cast to a
        // sub-second unit cannot cross to it and back; the sample draws the types the model has.
        let ty, r1 =
            pPick
                (ColumnType.all
                 |> List.filter (fun t ->
                     match t with
                     | TimestampType u -> u = TimeUnit.Seconds
                     | _ -> true))
                r

        let x, r2 = pGenExpr (depth - 1) r1
        Cast(ty, x), r2
    | 8 ->
        let fn, r1 =
            pPick
                [ Abs
                  Round
                  Length
                  Lower
                  Upper
                  Trim
                  Sqrt
                  Least
                  IndexOf
                  Concat
                  Floor
                  Ceil ]
                r

        let x, r2 = pGenExpr (depth - 1) r1
        let y, r3 = pGenExpr (depth - 1) r2

        let args =
            match fn with
            | Concat
            | Least
            | IndexOf -> [ x; y ]
            | _ -> [ x ]

        ApplyFn(fn, args), r3
    | 9 ->
        let x, r1 = pGenExpr (depth - 1) r
        InList(x, [ Lit(Cell.Int 1); Lit(Cell.Str "a"); Lit Cell.Null ]), r1
    | 10 ->
        let x, r1 = pGenExpr (depth - 1) r
        IsNull x, r1
    | 11 ->
        // Phase 277 — the rounding nodes share this draw with `InParam`, so the kind count and
        // every other draw's frequency stay as they were: a quotient, a rounded value, and the
        // list param, a third each.
        let which, r0 = ConfRng.intBelow 3 r
        let x, r1 = pGenExpr (depth - 1) r0
        let mode, r2 = pPick (pModes |> List.map fst) r1

        let scale, r3 =
            pPick [ Slot.Lit 0; Slot.Lit 2; Slot.Lit 1001; Slot.Param "p"; Slot.Param "s" ] r2

        let rounding = { Scale = scale; Mode = mode }

        match which with
        | 0 ->
            let y, r4 = pGenExpr (depth - 1) r3
            Quotient(x, y, rounding), r4
        | 1 -> Rounded(x, rounding), r3
        | _ -> InParam(x, "items"), r3
    | _ ->
        let g, r1 = pPick [ NowGrain.Date; NowGrain.Timestamp ] r
        Now g, r1

let private pAgg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

let private pGenSource (rng: ConfRng.T) : DataSource * ConfRng.T =
    let k, r = ConfRng.intBelow 4 rng

    match k with
    | 0 -> Ref "r", r
    | 1 -> Ref "missing", r
    | _ ->
        let t, r1 = pGenTable r
        Embedded t, r1

let private pGenStep (rng: ConfRng.T) : Transform * ConfRng.T =
    let k, r = ConfRng.intBelow 15 rng

    match k with
    | 0 ->
        let e, r1 = pGenExpr 2 r
        Filter e, r1
    | 1 -> Project [ "g", "g"; "v", "v2" ], r
    | 2 ->
        let e, r1 = pGenExpr 2 r
        Derive("d", e), r1
    | 3 ->
        let fn, r1 = pPick (pAggFns |> List.map fst) r
        GroupBy([ "g" ], [ pAgg "s" fn "v"; pAgg "n" Count "v"; pAgg "m" Mean "w" ]), r1
    | 4 ->
        let kind, r1 = pPick (pJoinKinds |> List.map fst) r
        let src, r2 = pGenSource r1
        Join(src, [ "g", "g" ], kind), r2
    | 5 ->
        let fn, r1 = pPick ((pWindowFns |> List.map fst) @ [ NTile 2; NTile 0 ]) r

        Window
            { PartitionBy = [ "g" ]
              OrderBy = [ "v", Asc ]
              Fn = fn
              Of = "v"
              As = "wv" },
        r1
    | 6 ->
        let fn, r1 = pPick [ Sum; Count; Max ] r

        Pivot
            { Index = [ "v" ]
              On = "g"
              Values = "w"
              Agg = fn },
        r1
    | 7 -> Unpivot([ "g" ], [ "v"; "w" ]), r
    | 8 ->
        let key, r1 =
            pPick
                [ Slot.Lit "g", Asc
                  Slot.Lit "v", Desc
                  Slot.Param "sortcol", Asc
                  Slot.Param "nope", Asc ]
                r

        Sort [ key; Slot.Lit "w", Asc ], r1
    | 9 -> Distinct, r
    | 10 ->
        let n, r1 = pPick [ Slot.Lit 3; Slot.Lit 1; Slot.Param "n"; Slot.Param "s" ] r
        let off, r2 = pPick [ Slot.Lit 0; Slot.Lit 1; Slot.Param "off" ] r1
        Limit(n, off), r2
    | 11 ->
        let src, r1 = pGenSource r
        Union src, r1
    | 12 ->
        let src, r1 = pGenSource r
        Intersect src, r1
    | 13 ->
        let src, r1 = pGenSource r
        Except src, r1
    | _ ->
        // a keep-all filter: charges the frame's rows and changes nothing, so a count is compared
        // past every other verb rather than only where a drawn predicate happens to hold
        Filter(Lit(Cell.Bool true)), r

let private pGenPipeline (rng: ConfRng.T) : Transform list * ConfRng.T =
    let n, r = ConfRng.intBelow 4 rng

    let rec draw k acc r =
        if k = 0 then
            List.rev acc, r
        else
            let s, r' = pGenStep r
            draw (k - 1) (s :: acc) r'

    draw (n + 1) [] r

/// The environment and the resolver every generated pipeline evaluates under — the same on both
/// sides, since the step evaluator closes over them exactly as production's does.
let private pEnv: Map<string, Cell> =
    Map.ofList
        [ "p", Cell.Int 2
          "s", Cell.Str "b"
          "sortcol", Cell.Str "v"
          "n", Cell.Int 2
          "off", Cell.Int 1 ]

let private pResolveWith (right: Table) : string -> Result<Table, EvalError> =
    fun r -> if r = "r" then Ok right else Error(UnresolvedSource r)

/// The whole differential: the law vectors under `noResolve` and an empty env (as the law runs
/// them), then `trials` generated pipelines at `seed`, each under the resolver and env above.
/// The vectors the pipeline differential runs: every evalPipeline vector but the UNIT shapes (Phase
/// 429). The model's frame carries no field metadata and its evaluator takes a RESOLVED expression,
/// so a unit vector is the unit differential's below — `resolve_units` against production's check,
/// and production's evaluation of the resolved expression against its evaluation of the original.
let private pPipelineVectors () : (string * Transform list * Table * bool) list =
    pLawVectors () |> List.filter (fun (id, _, _, _) -> not (id.Contains "-unit-"))

let private pipelineDifferential
    (bridge: Transform list -> ModelPipe.transform list)
    (mkStep: (string -> Result<Table, EvalError>) -> Map<string, Cell> -> Transform list -> ModelPipe.other_fn)
    (seed: int)
    (trials: int)
    : PipeTally =
    let mutable tally = pEmptyTally

    for id, p, table, refuses in pPipelineVectors () do
        let verdict =
            DataFrame.evalPipelineWithInEnvCounted DataFrame.noResolve Map.empty p table

        Expect.equal
            (Result.isError verdict)
            refuses
            (sprintf
                "vector %s: the file's verdict is production's verdict (the vectors are the reference's own answers)"
                id)

        tally <- pCompare id DataFrame.noResolve Map.empty bridge (mkStep DataFrame.noResolve Map.empty) p table tally

    let mutable rng = ConfRng.ofSeed seed

    for i in 1..trials do
        let table, r1 = pGenTable rng
        let right, r2 = pGenTable r1
        let p, r3 = pGenPipeline r2
        let resolve = pResolveWith right
        tally <- pCompare (sprintf "generated %d" i) resolve pEnv bridge (mkStep resolve pEnv) p table tally
        rng <- r3

    tally

/// An expression of exactly `nodes` `ColExpr` nodes (odd, at least 1): `Col "v"` wrapped in
/// `Binary(Add, _, Lit 1)` — two nodes a wrap.
let private pExprOfNodes (nodes: int) : ColExpr =
    let rec wrap (acc: ColExpr) (k: int) =
        if k = 0 then
            acc
        else
            wrap (Binary(Add, acc, Lit(Cell.Int 1))) (k - 1)

    wrap (Col "v") ((nodes - 1) / 2)

/// A node counter that FORGETS a `Case`'s nested `when` / `then` arms — the go-red's under-counting
/// model (Phase 234). It agrees with `expr_nodes` on every other constructor, so only an expression
/// whose `Case` arms are actually walked can expose it, and the visit count is what does.
let rec private pUnderCountNodes (e: ColExpr) : int =
    match e with
    | Col _
    | Lit _
    | Param _
    | Now _ -> 1
    | Binary(_, a, b) -> 1 + pUnderCountNodes a + pUnderCountNodes b
    | Not x
    | Cast(_, x)
    | IsNull x
    | InParam(x, _)
    | Rounded(x, _)
    | Convert(x, _) -> 1 + pUnderCountNodes x
    | Quotient(a, b, _) -> 1 + pUnderCountNodes a + pUnderCountNodes b
    | Coalesce xs
    | ApplyFn(_, xs) -> 1 + List.sumBy pUnderCountNodes xs
    | InList(x, items) -> 1 + pUnderCountNodes x + List.sumBy pUnderCountNodes items
    | Case(_, els) -> 1 + pUnderCountNodes els

/// One row-level comparison (Phase 234): the model's `eval_expr` — production's primitives under
/// the model's own recursion — against the shipped `evalExprInRow`, over one expression and one
/// row, returning the disagreement if any, and the model's visit count and node count beside it.
let private pRowCompare
    (env: Map<string, Cell>)
    (cols: Schema)
    (row: Cell list)
    (e: ColExpr)
    : string option * int * int =
    let prod = DataFrame.evalExprInRow env cols row e
    let mcols = cols |> List.map (fun f -> f.Name, pColToModel f.Type)
    let mrow = row |> List.map pCellToModel
    let menv = pEnvToModel env
    let me = pExprToModel e
    let model = ModelPipe.eval_expr pPrims menv mcols mrow me
    let visits = int (ModelPipe.expr_visits pPrims menv mcols mrow me)
    let nodes = int (ModelPipe.expr_nodes me)

    let diff =
        match prod, model with
        | Ok c, ModelPipe.Ok mc ->
            if pCellToModel c <> mc then
                Some(sprintf "cell differs — production %A, model %A" c (pCellOfModel mc))
            else
                None
        | Error pe, ModelPipe.Error merr ->
            if pErrToModel pe <> merr then
                Some(
                    sprintf
                        "error differs — production %s, model %s"
                        (pRenderError pe)
                        (pRenderError (pErrOfModel merr))
                )
            else
                None
        | Ok c, ModelPipe.Error merr ->
            Some(sprintf "production evaluated to %A, model refused %s" c (pRenderError (pErrOfModel merr)))
        | Error pe, ModelPipe.Ok mc ->
            Some(sprintf "production refused %s, model evaluated to %A" (pRenderError pe) (pCellOfModel mc))

    diff, visits, nodes

/// The row sample the evaluator differential walks: `trials` expressions of depth three, each on
/// every row of a freshly drawn table (the vectors' own recipe, so nulls, ties and floats are all
/// present), under the generated pipelines' env. Seeded and replayable.
let private pRowSample (seed: int) (trials: int) : (string * Schema * Cell list * ColExpr) list =
    let mutable rng = ConfRng.ofSeed seed

    [ for i in 1..trials do
          let table, r1 = pGenTable rng
          let e, r2 = pGenExpr 3 r1
          rng <- r2

          let rows =
              [ for r in 0 .. Table.rowCount table - 1 ->
                    table.Columns |> List.map (fun c -> List.item r (Column.toCells c)) ]

          for j, row in List.indexed rows do
              yield sprintf "expression %d row %d" i j, table.Schema, row, e ]
// ---------------------------------------------------------------------------
//  Phase 429 — the unit check: `resolve_units` against `DataFrame.resolveUnits`
// ---------------------------------------------------------------------------

/// The model's algebra parameter, instantiated at Core's `Unit` module over canonical texts — the
/// premise `pipeline-unit-algebra-is-core` names, made concrete here.
let private pUnitAlg: ModelPipe.unit_alg =
    let parsed (text: string) = pUnitOfText text

    { ModelPipe.unit_alg.u_mul = fun a b -> Unit.render (Unit.mul (parsed a) (parsed b))
      ModelPipe.unit_alg.u_div = fun a b -> Unit.render (Unit.div (parsed a) (parsed b))
      ModelPipe.unit_alg.u_one = Unit.render Unit.dimensionless
      ModelPipe.unit_alg.u_factor =
        fun a b ->
            match Unit.conversionFactor (parsed a) (parsed b) with
            | Ok r ->
                // The factor as the float LITERAL the resolved expression carries, in the model's
                // float carrier: production's `ratioToFloat`, crossed as a cell is.
                match pCellToModel (Cell.Float(double r.Numerator / double r.Denominator)) with
                | ModelPipe.Float text -> FStar_Pervasives_Native.Some text
                | _ -> FStar_Pervasives_Native.None
            | Error _ -> FStar_Pervasives_Native.None }

/// The unit differential's schema: the generator's columns under units — `v` in metres, `w` in
/// seconds, `m` in kilograms, `g` and the unknown column unconstrained.
let private pUnitSchema: Schema =
    [ Field.create "g" StringType
      Field.create "v" IntType |> Field.withUnit (pUnitOfText "m")
      Field.create "w" FloatType |> Field.withUnit (pUnitOfText "s")
      Field.create "m" DecimalType |> Field.withUnit (pUnitOfText "kg") ]

let private pUnitsOfSchema (schema: Schema) : ModelPipe.units =
    schema
    |> List.map (fun f ->
        f.Name,
        (match f.Unit with
         | Some u -> FStar_Pervasives_Native.Some(Unit.render u)
         | None -> FStar_Pervasives_Native.None))

/// An expression of the generator's, with conversions grafted in: a conversion over the whole, over
/// one operand of a binary, or none — so the sample reaches every rule with and without a `Convert`.
let private pGenUnitExpr (rng: ConfRng.T) : ColExpr * ConfRng.T =
    let e, r1 = pGenExpr 3 rng
    let k, r2 = ConfRng.intBelow 4 r1
    let target, r3 = pPick [ "m"; "km"; "s"; "kg"; "m/s"; "cm" ] r2

    match k with
    | 0 -> Convert(e, pUnitOfText target), r3
    | 1 ->
        let op, r4 = pPick [ Add; Sub; Mul; Div; Gt; Eq ] r3
        let f, r5 = pGenExpr 2 r4
        Binary(op, Convert(e, pUnitOfText target), f), r5
    | _ -> e, r3

let private pUnitResult
    (r: Result<UnitOfMeasure option * ColExpr, EvalError>)
    : Result<string option * ColExpr, EvalError> =
    r |> Result.map (fun (u, e) -> u |> Option.map Unit.render, e)

let private pModelUnitResult
    (r: ModelPipe.outcome<FStar_Pervasives_Native.option<string> * ModelPipe.col_expr, ModelPipe.eval_error>)
    : Result<string option * ColExpr, EvalError> =
    match r with
    | ModelPipe.Ok(u, e) ->
        Ok(
            (match u with
             | FStar_Pervasives_Native.Some t -> Some t
             | FStar_Pervasives_Native.None -> None),
            pExprOfModel e
        )
    | ModelPipe.Error e -> Error(pErrOfModel e)

type private UnitTally =
    { UDiffs: string list
      UCompared: int
      UAccepted: int
      URefused: int
      UWithUnit: int
      UConverts: int
      UConvertRefused: int }

/// The unit differential (Phase 429): over the unit vectors' expressions and a generated sample,
/// production's `resolveUnits` and the model's `resolve_units` answer the same unit, the same
/// resolved expression and the same refusal — and evaluating the ORIGINAL expression through
/// production's derive gives exactly what evaluating the RESOLVED one gives, so resolution is the
/// whole of a conversion's semantics.
let private unitDifferential (seed: int) (trials: int) : UnitTally =
    let mutable t =
        { UDiffs = []
          UCompared = 0
          UAccepted = 0
          URefused = 0
          UWithUnit = 0
          UConverts = 0
          UConvertRefused = 0 }

    let compare (label: string) (schema: Schema) (table: Table option) (e: ColExpr) =
        let step = "derive 'd'"
        let prod = pUnitResult (DataFrame.resolveUnits step schema e)

        let model =
            pModelUnitResult (ModelPipe.resolve_units pUnitAlg step (pUnitsOfSchema schema) (pExprToModel e))

        let hasConvert = pExprTags e |> List.contains "Convert"
        t <- { t with UCompared = t.UCompared + 1 }

        if hasConvert then
            t <- { t with UConverts = t.UConverts + 1 }

        if prod <> model then
            t <-
                { t with
                    UDiffs = sprintf "%s: %A | production %A | model %A" label e prod model :: t.UDiffs }

        match prod with
        | Ok(u, resolved) ->
            t <- { t with UAccepted = t.UAccepted + 1 }

            if u.IsSome then
                t <- { t with UWithUnit = t.UWithUnit + 1 }

            // Resolution is the whole of a conversion's VALUE semantics: the original, derived over the
            // unit-bearing table, and the resolved expression, derived over the same table with its
            // units STRIPPED (a resolved expression is arithmetic the check has already read — its
            // factor literal is unconstrained, so re-checking it would read the source's unit, and
            // production never re-checks it), give one column, cell for cell and type for type. The
            // unit is the check's, not the arithmetic's, so the schemas are compared without it.
            match table with
            | Some table ->
                let unitless (r: Result<Table, EvalError>) =
                    r
                    |> Result.map (fun (t: Table) ->
                        { t with
                            Schema = t.Schema |> List.map Field.withoutUnit })

                let plain =
                    { table with
                        Schema = table.Schema |> List.map Field.withoutUnit }

                let a = unitless (DataFrame.evalPipelineInEnv pEnv [ Derive("d", e) ] table)
                let b = unitless (DataFrame.evalPipelineInEnv pEnv [ Derive("d", resolved) ] plain)

                if a <> b then
                    t <-
                        { t with
                            UDiffs =
                                sprintf "%s: the original and the resolved derive differ: %A vs %A" label a b
                                :: t.UDiffs }
            | None -> ()
        | Error(UnitMismatch(_, at, _, _)) ->
            t <- { t with URefused = t.URefused + 1 }

            match at with
            | Convert _ ->
                t <-
                    { t with
                        UConvertRefused = t.UConvertRefused + 1 }
            | _ -> ()
        | Error _ -> ()

    // The unit vectors' own expressions, over their own tables.
    for id, p, table, _ in pLawVectors () |> List.filter (fun (id, _, _, _) -> id.Contains "-unit-") do
        for step in p do
            match step with
            | Filter e
            | Derive(_, e) -> compare id table.Schema (Some table) e
            | _ -> ()

    let mutable rng = ConfRng.ofSeed seed

    for i in 1..trials do
        let e, r1 = pGenUnitExpr rng
        let table, r2 = pGenTable r1
        rng <- r2

        // The generated table under the differential's units, where its columns are the schema's.
        let typed =
            if (table.Schema |> List.map _.Name) = (pUnitSchema |> List.map _.Name) then
                Some
                    { table with
                        Schema =
                            List.map2
                                (fun (f: Field) (u: Field) ->
                                    f
                                    |> (match u.Unit with
                                        | Some unit -> Field.withUnit unit
                                        | None -> id))
                                table.Schema
                                pUnitSchema }
            else
                None

        let schema =
            match typed with
            | Some t -> t.Schema
            | None -> pUnitSchema

        compare (sprintf "generated %d" i) schema typed e

    t



// ---- Phase 433 — the ROW-LOCAL INCREMENTAL REFRESH against the model ----
//
// `proofs/Incremental.fst` models `Incremental.prime` / `Incremental.refresh` on the row-local path:
// one cell cache per evaluating step keyed by the row's identity token, a walk that reads a cached
// cell for every row the delta does not name and evaluates the rest, over pipelines of `Filter` and
// `Derive`. `proofs/oracle/Incremental.fs` is that model extracted. This runs it BESIDE production
// over generated row-local pipelines, a prior table keyed by an `id` column, and deltas `Delta.diff`
// builds to a mutated table — the delta's named rows read off the delta production was handed, the
// source's tokens minted by the same witness — and compares the RESULT byte for byte, or the error.
// The cell primitives are production's own (`pPrims`), as the pipeline differential instantiates
// them; the twelve verbs are never reached.

/// The identity every incremental trial keys by: an `id` column the mutations never touch.
let private iIdw = RowIdentity.byColumn "id"

/// A row's token as production's witness mints it and as `Delta.diff` names it (`ByKey` of the
/// witness's `KeyString`, rendered by `Delta.refToken`). A row with no identity (a `Null` id, which
/// the trials never draw) takes a token no delta can name, so it is always evaluated.
let private iTokens (t: Table) : string list =
    let keyOf = iIdw.KeyOf t

    [ for i in 0 .. Table.rowCount t - 1 ->
          match keyOf i with
          | Some k -> Delta.refToken (ByKey(iIdw.KeyString k))
          | None -> sprintf "none:%d" i ]

/// The model's walked source: the frame's rows, each under its token.
let private iAliveOf (t: Table) : ModelInc.alive =
    List.zip (iTokens t) (pFrameOfTable t).rows

/// The model's reading of a delta: the tokens it names, or every row (`None`) where it invalidates a
/// column or is a full refresh — exactly `namedTokens` and the `InvalidatedColumns` clause.
let private iNamedOf (d: TableDelta) : FStar_Pervasives_Native.option<string list> =
    match d with
    | FullRefresh -> FStar_Pervasives_Native.None
    | RowSet r ->
        if List.isEmpty r.InvalidatedColumns then
            FStar_Pervasives_Native.Some(r.Rows |> List.map (fst >> Delta.refToken))
        else
            FStar_Pervasives_Native.None

/// A row-local pipeline crossed to the model's own step alphabet.
let private iStepsOf (p: Transform list) : ModelInc.wstep list =
    p
    |> List.map (function
        | Filter e -> ModelInc.WFilter(pExprToModel e)
        | Derive(name, e) -> ModelInc.WDerive(name, pExprToModel e)
        | other -> failwithf "not a row-local step: %A" other)

/// Phase 470 (D38) — the retype corner: a derived column the cells type, retyped by a row whose `v`
/// a mutation moves over a threshold, read on the rows that STAND by a step that tells the packing
/// apart. A `Mod` over the float reading refuses where the int reading answers; an `Add` of a float
/// over the decimal reading refuses where the int reading answers; and a decimal text past the
/// scaled carrier's width abandons the typed pass, where the int reads as its decimal on the boxed
/// form too (Phase 472) — on the reference, on the walk, and on the models. The old raw reference
/// answered the first two where production refuses (`pRawReferenceFails` shows it once).
let private pCornerShapes: Transform list list =
    let crosses = Binary(Gt, Col "v", Lit(Int 9))
    let read inner = Case([ crosses, Lit(Int 0) ], inner)

    [ [ Derive("d", Case([ crosses, Lit(Float 0.5) ], Col "v"))
        Derive("m", read (Binary(Mod, Col "d", Lit(Int 2)))) ]
      [ Derive("d", Case([ crosses, Lit(Cell.Decimal "0.5") ], Col "v"))
        Derive("m", read (Binary(Add, Col "d", Lit(Float 0.5)))) ]
      [ Derive("d", Case([ crosses, Lit(Cell.Decimal "1234567890123456789012345.1234567") ], Col "v"))
        Derive("m", read (Binary(Add, Col "d", Lit(Float 0.5)))) ] ]

/// One to four `Filter` / `Derive` steps over the pipeline differential's expression generator, or,
/// one time in four, a retype corner (Phase 470).
let private iGenPipeline (rng: ConfRng.T) : Transform list * ConfRng.T =
    let corner, r0 = ConfRng.intBelow 4 rng

    if corner = 0 then
        let which, r1 = ConfRng.intBelow pCornerShapes.Length r0
        pCornerShapes[which], r1
    else

        let n, r = ConfRng.intBelow 4 r0

        let rec draw k acc r =
            if k = 0 then
                List.rev acc, r
            else
                let kind, r1 = ConfRng.intBelow 2 r
                let e, r2 = pGenExpr 2 r1

                let s = if kind = 0 then Filter e else Derive("d", e)

                draw (k - 1) (s :: acc) r2

        draw (n + 1) [] r

/// A cell of the column's own type (never another type, so a mutation keeps the column typed and
/// the schema fixed, which is what keeps `Delta.diff` on the keyed path rather than `FullRefresh`),
/// null one time in five on the nullable columns.
let private iGenCell (ty: ColumnType) (rng: ConfRng.T) : Cell * ConfRng.T =
    let v, r1 = ConfRng.intBelow 40 rng
    let roll, r2 = ConfRng.intBelow 5 r1

    let cell =
        match ty with
        | StringType -> Str [| "a"; "b"; "c" |].[v % 3]
        | IntType -> if roll = 0 then Null else Int(v - 7)
        | FloatType -> Float(float v / 2.0)
        | DecimalType ->
            if roll = 0 then
                Null
            else
                Cell.decimal (sprintf "%d.25" (v - 7)) |> Option.defaultValue Null
        | other -> failwithf "the incremental trials draw no %A column" other

    cell, r2

/// The prior table: the pipeline differential's recipe (`g`, `v`, `w`, `m`) under an `id` column.
let private iGenTable (rng: ConfRng.T) : Table * ConfRng.T =
    let plain, r1 = pGenTable rng
    let n = Table.rowCount plain
    let ids = KitColumn.create "id" IntType [ for i in 0 .. n - 1 -> Int(100 + i) ]

    { Schema = Field.create "id" IntType :: plain.Schema
      Columns = ids :: plain.Columns },
    r1

/// One mutation of a table: a cell changed in place, a row appended under a fresh id, or a row
/// removed. The `id` column is never written.
let private iMutate (t: Table) (rng: ConfRng.T) : Table * ConfRng.T =
    let n = Table.rowCount t
    let cols = t.Columns |> List.map (fun c -> c.Name, c.Type, Column.toCells c)
    let kind, r1 = ConfRng.intBelow (if n = 0 then 1 else 3) rng

    let rebuilt (cols: (string * ColumnType * Cell list) list) : Table =
        { Schema = cols |> List.map (fun (name, ty, _) -> Field.create name ty)
          Columns = cols |> List.map (fun (name, ty, cells) -> KitColumn.create name ty cells) }

    match kind with
    | 0 ->
        // append a row under an id past every id in the table
        let maxId =
            cols
            |> List.pick (fun (name, _, cells) -> if name = "id" then Some cells else None)
            |> List.fold
                (fun acc c ->
                    match c with
                    | Int i -> max acc i
                    | _ -> acc)
                99

        let mutable r = r1

        let cols' =
            cols
            |> List.map (fun (name, ty, cells) ->
                if name = "id" then
                    name, ty, cells @ [ Int(maxId + 1) ]
                else
                    let c, r' = iGenCell ty r
                    r <- r'
                    name, ty, cells @ [ c ])

        rebuilt cols', r
    | 1 ->
        // change one cell of one non-id column in one row
        let row, r2 = ConfRng.intBelow n r1
        let which, r3 = ConfRng.intBelow (List.length cols - 1) r2
        let mutable r = r3

        let cols' =
            cols
            |> List.mapi (fun ci (name, ty, cells) ->
                if ci = which + 1 then
                    let c, r' = iGenCell ty r
                    r <- r'
                    name, ty, cells |> List.mapi (fun i x -> if i = row then c else x)
                else
                    name, ty, cells)

        rebuilt cols', r
    | _ ->
        // remove one row
        let row, r2 = ConfRng.intBelow n r1

        let cols' =
            cols
            |> List.map (fun (name, ty, cells) ->
                name, ty, cells |> List.indexed |> List.filter (fun (i, _) -> i <> row) |> List.map snd)

        rebuilt cols', r2

/// One to three mutations.
let private iMutateSome (t: Table) (rng: ConfRng.T) : Table * ConfRng.T =
    let k, r = ConfRng.intBelow 3 rng

    let rec go i t r =
        if i = 0 then
            t, r
        else
            let t', r' = iMutate t r
            go (i - 1) t' r'

    go (k + 1) t r

/// Phase 471 — one to three mutations (one time in three), the same followed by a REORDERING of two
/// rows (one time in three), or that reordering alone (one time in three): `Delta.diff` reports a
/// reordering as quiet, so a stable sort's answer moves while the delta names no row — the case
/// `arrivedAsBefore` exists for.
let private mMutateSome (t: Table) (rng: ConfRng.T) : Table * ConfRng.T =
    let kind, r0 = ConfRng.intBelow 3 rng
    let t1, r1 = if kind = 2 then t, r0 else iMutateSome t r0
    let n = Table.rowCount t1
    let r2 = r1

    if kind = 0 || n < 2 then
        t1, r2
    else
        let i, r3 = ConfRng.intBelow n r2
        let j, r4 = ConfRng.intBelow n r3

        if i = j then
            t1, r4
        else
            let order =
                [ 0 .. n - 1 ]
                |> List.map (fun k ->
                    if k = i then j
                    elif k = j then i
                    else k)

            let cols =
                t1.Columns
                |> List.map (fun c ->
                    let cells = Column.toCells c
                    KitColumn.create c.Name c.Type (order |> List.map (fun k -> cells[k])))

            { t1 with Columns = cols }, r4

/// The model's result rendered as production renders a table — the comparison the pipeline
/// differential makes, byte for byte through `ColumnCodec.encode`.
let private iRender (t: Table) : string = ColumnCodec.encode (Embedded t)

let private iRenderModel (st: ModelInc.state) : string =
    iRender (pTableOfFrame (ModelInc.frame_of_walked st.s_out))

type private IncTally =
    {
        /// Every disagreement, rendered — the tally's verdict is that this is empty.
        IDiffs: string list
        /// Trials whose prime both sides evaluated to a table.
        IPrimed: int
        /// Trials both sides refused at the prime, with the same error.
        IRefused: int
        /// Refreshes compared (first and second).
        IRefreshed: int
        /// Rows the model REUSED across every refresh — the sum of unnamed tokens the prior held.
        /// Zero would mean the tokens never matched and every comparison was a full walk in disguise.
        IReused: int
        /// Rows the model evaluated at a refresh because the delta named them.
        INamed: int
        /// Refreshes under a column-invalidating delta (every row evaluated).
        IInvalidated: int
        /// Refreshes under a hand-built delta naming every row (over-named, still truthful).
        IOverNamed: int
    }

let private iEmptyTally =
    { IDiffs = []
      IPrimed = 0
      IRefused = 0
      IRefreshed = 0
      IReused = 0
      INamed = 0
      IInvalidated = 0
      IOverNamed = 0 }

/// Production's outcome and the model's, compared on the table or on the error.
let private iCompare
    (label: string)
    (prod: Result<Fuaran.Compute.IncrementalEval, EvalError>)
    (model: ModelPipe.outcome<ModelInc.state, ModelPipe.eval_error>)
    : string option =
    match prod, model with
    | Ok ps, ModelPipe.Ok ms ->
        let a = iRender (Fuaran.Compute.Incremental.result ps)
        let b = iRenderModel ms

        if a = b then
            None
        else
            Some(sprintf "%s: result differs — production %s, model %s" label a b)
    | Error pe, ModelPipe.Error me ->
        if pErrToModel pe = me then
            None
        else
            Some(
                sprintf
                    "%s: error differs — production %s, model %s"
                    label
                    (pRenderError pe)
                    (pRenderError (pErrOfModel me))
            )
    | Ok _, ModelPipe.Error me ->
        Some(sprintf "%s: production evaluated, model refused %s" label (pRenderError (pErrOfModel me)))
    | Error pe, ModelPipe.Ok _ -> Some(sprintf "%s: production refused %s, model evaluated" label (pRenderError pe))

/// The model's result against the reference evaluator over the same table — `refresh_agrees` read
/// numerically.
let private iAgainstReference
    (label: string)
    (p: Transform list)
    (t: Table)
    (model: ModelPipe.outcome<ModelInc.state, ModelPipe.eval_error>)
    : string option =
    match DataFrame.evalPipelineWithInEnv DataFrame.noResolve pEnv p t, model with
    | Ok rt, ModelPipe.Ok ms ->
        let a = iRender rt
        let b = iRenderModel ms

        if a = b then
            None
        else
            Some(sprintf "%s: the model's refresh differs from the reference — reference %s, model %s" label a b)
    | Error re, ModelPipe.Error me when pErrToModel re = me -> None
    | Error re, ModelPipe.Error me ->
        Some(
            sprintf
                "%s: the model's refresh error differs from the reference's — reference %s, model %s"
                label
                (pRenderError re)
                (pRenderError (pErrOfModel me))
        )
    | Ok _, ModelPipe.Error me ->
        Some(
            sprintf "%s: the reference evaluated, the model's refresh refused %s" label (pRenderError (pErrOfModel me))
        )
    | Error re, ModelPipe.Ok _ ->
        Some(sprintf "%s: the reference refused %s, the model's refresh evaluated" label (pRenderError re))

/// How a refresh's delta is built for a trial: the diff, the diff plus every prior row named, or a
/// column invalidation beside the diff's row changes.
type private IncDeltaShape =
    | Diffed
    | OverNamed
    | Invalidated

let private iDeltaOf (shape: IncDeltaShape) (before: Table) (after: Table) : TableDelta option =
    match Delta.diff iIdw before after with
    | Error _ -> None
    | Ok d ->
        match shape, d with
        | Diffed, _ -> Some d
        | OverNamed, RowSet r ->
            let named = r.Rows |> List.map fst |> Set.ofList

            let every =
                [ for i in 0 .. Table.rowCount after - 1 do
                      match iIdw.KeyOf after i with
                      | Some k -> yield ByKey(iIdw.KeyString k)
                      | None -> () ]
                |> List.filter (fun ref -> not (Set.contains ref named))
                |> List.map (fun ref -> ref, RowChanged)

            Some(Delta.ofRows r.Scheme (r.Rows @ every))
        | Invalidated, RowSet r -> Some(RowSet { r with InvalidatedColumns = [ "v" ] })
        | _, FullRefresh -> Some d

/// Rows of `after` the model reuses under `named`: unnamed, and held by the prior.
let private iReusedCount (before: Table) (after: Table) (named: FStar_Pervasives_Native.option<string list>) : int =
    match named with
    | FStar_Pervasives_Native.None -> 0
    | FStar_Pervasives_Native.Some ns ->
        let prior = iTokens before |> Set.ofList

        iTokens after
        |> List.filter (fun t -> not (List.contains t ns) && Set.contains t prior)
        |> List.length

/// One trial: a prime, a refresh under one delta, and a second refresh — each compared to
/// production, the refreshes also to the reference and the second to a refresh of a primed state.
/// `reuse` is the model's walk: the faithful one reuses every unnamed row; the go-red reuses all.
let private iTrial
    (label: string)
    (reuseFor: FStar_Pervasives_Native.option<string list> -> ModelInc.reuse_fn)
    (shape: IncDeltaShape)
    (rng: ConfRng.T)
    (t: IncTally)
    : IncTally * ConfRng.T =
    let p, r1 = iGenPipeline rng
    let t0, r2 = iGenTable r1
    let steps = iStepsOf p
    let menv = pEnvToModel pEnv
    let prod0 = Fuaran.Compute.Incremental.prime DataFrame.noResolve pEnv iIdw p t0
    let model0 = ModelInc.prime pPrims menv (pFrameOfTable t0).cols (iAliveOf t0) steps
    let diffs = ResizeArray<string>()
    let add (d: string option) = d |> Option.iter diffs.Add
    add (iCompare (label + " prime") prod0 model0)

    let refreshWith
        (prodState: Fuaran.Compute.IncrementalEval)
        (modelState: ModelInc.state)
        (before: Table)
        (after: Table)
        (delta: TableDelta)
        =
        let named = iNamedOf delta

        let prod =
            Fuaran.Compute.Incremental.refresh DataFrame.noResolve pEnv iIdw p prodState delta after

        let cols = (pFrameOfTable after).cols

        let model =
            ModelInc.walk pPrims menv (reuseFor named) modelState.s_entries cols (iAliveOf after) steps

        prod, model, named

    match prod0, model0 with
    | Ok ps0, ModelPipe.Ok ms0 ->
        let t1, r3 = iMutateSome t0 r2

        match iDeltaOf shape t0 t1 with
        | None ->
            { t with
                IDiffs = List.rev (List.ofSeq diffs) @ t.IDiffs
                IPrimed = t.IPrimed + 1 },
            r3
        | Some d1 ->
            let prod1, model1, named1 = refreshWith ps0 ms0 t0 t1 d1
            add (iCompare (label + " refresh 1") prod1 model1)
            add (iAgainstReference (label + " refresh 1") p t1 model1)
            let reused1 = iReusedCount t0 t1 named1

            let named1Count =
                match named1 with
                | FStar_Pervasives_Native.Some ns -> List.length ns
                | FStar_Pervasives_Native.None -> Table.rowCount t1

            let invalidated1 =
                match d1 with
                | RowSet r when not (List.isEmpty r.InvalidatedColumns) -> 1
                | _ -> 0

            let t' =
                { t with
                    IPrimed = t.IPrimed + 1
                    IRefreshed = t.IRefreshed + 1
                    IReused = t.IReused + reused1
                    INamed = t.INamed + named1Count
                    IInvalidated = t.IInvalidated + invalidated1
                    IOverNamed = t.IOverNamed + (if shape = OverNamed then 1 else 0) }

            match prod1, model1 with
            | Ok ps1, ModelPipe.Ok ms1 ->
                let t2, r4 = iMutateSome t1 r3

                match iDeltaOf shape t1 t2 with
                | None ->
                    { t' with
                        IDiffs = List.rev (List.ofSeq diffs) @ t'.IDiffs },
                    r4
                | Some d2 ->
                    let prod2, model2, named2 = refreshWith ps1 ms1 t1 t2 d2
                    add (iCompare (label + " refresh 2") prod2 model2)
                    add (iAgainstReference (label + " refresh 2") p t2 model2)

                    // The state theorem, observably: a refresh of the REFRESHED state is a refresh
                    // of a state PRIMED over the first mutation — on the model and on production.
                    (match Fuaran.Compute.Incremental.prime DataFrame.noResolve pEnv iIdw p t1 with
                     | Ok primed1 ->
                         let prodFromPrimed =
                             Fuaran.Compute.Incremental.refresh DataFrame.noResolve pEnv iIdw p primed1 d2 t2

                         (match prod2, prodFromPrimed with
                          | Ok a, Ok b when
                              iRender (Fuaran.Compute.Incremental.result a)
                              <> iRender (Fuaran.Compute.Incremental.result b)
                              ->
                              diffs.Add(
                                  sprintf
                                      "%s: production's second refresh differs between a refreshed and a primed state — refreshed %s, primed %s"
                                      label
                                      (iRender (Fuaran.Compute.Incremental.result a))
                                      (iRender (Fuaran.Compute.Incremental.result b))
                              )
                          | Ok _, Error e ->
                              diffs.Add(
                                  sprintf "%s: production's refresh of a primed state refused %s" label (pRenderError e)
                              )
                          | Error e, Ok _ ->
                              diffs.Add(
                                  sprintf
                                      "%s: production's refresh of a refreshed state refused %s"
                                      label
                                      (pRenderError e)
                              )
                          | _ -> ())
                     | Error e ->
                         diffs.Add(
                             sprintf "%s: production's prime over the first mutation refused %s" label (pRenderError e)
                         ))

                    let modelPrimed1 =
                        ModelInc.prime pPrims menv (pFrameOfTable t1).cols (iAliveOf t1) steps

                    (match modelPrimed1 with
                     | ModelPipe.Ok mp1 ->
                         let modelFromPrimed =
                             ModelInc.walk
                                 pPrims
                                 menv
                                 (reuseFor named2)
                                 mp1.s_entries
                                 (pFrameOfTable t2).cols
                                 (iAliveOf t2)
                                 steps

                         (match model2, modelFromPrimed with
                          | ModelPipe.Ok a, ModelPipe.Ok b when iRenderModel a <> iRenderModel b ->
                              diffs.Add(
                                  sprintf
                                      "%s: the model's second refresh differs between a refreshed and a primed state"
                                      label
                              )
                          | ModelPipe.Ok _, ModelPipe.Error _
                          | ModelPipe.Error _, ModelPipe.Ok _ ->
                              diffs.Add(
                                  sprintf "%s: the model's second refresh and its primed twin disagree on refusal" label
                              )
                          | _ -> ())
                     | ModelPipe.Error me ->
                         diffs.Add(
                             sprintf
                                 "%s: the model's prime over the first mutation refused %s"
                                 label
                                 (pRenderError (pErrOfModel me))
                         ))

                    let reused2 = iReusedCount t1 t2 named2

                    let named2Count =
                        match named2 with
                        | FStar_Pervasives_Native.Some ns -> List.length ns
                        | FStar_Pervasives_Native.None -> Table.rowCount t2

                    { t' with
                        IDiffs = List.rev (List.ofSeq diffs) @ t'.IDiffs
                        IRefreshed = t'.IRefreshed + 1
                        IReused = t'.IReused + reused2
                        INamed = t'.INamed + named2Count },
                    r4
            | _ ->
                { t' with
                    IDiffs = List.rev (List.ofSeq diffs) @ t'.IDiffs },
                r3
    | Error _, ModelPipe.Error _ ->
        { t with
            IDiffs = List.rev (List.ofSeq diffs) @ t.IDiffs
            IRefused = t.IRefused + 1 },
        r2
    | _ ->
        { t with
            IDiffs = List.rev (List.ofSeq diffs) @ t.IDiffs },
        r2

let private iDifferential
    (reuseFor: FStar_Pervasives_Native.option<string list> -> ModelInc.reuse_fn)
    (seed: int)
    (trials: int)
    : IncTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = iEmptyTally

    for i in 1..trials do
        let shape =
            match i % 5 with
            | 0 -> OverNamed
            | 1 -> Invalidated
            | _ -> Diffed

        let t, r' = iTrial (sprintf "incremental %d (%A)" i shape) reuseFor shape r tally
        tally <- t
        r <- r'

    { tally with
        IDiffs = List.rev tally.IDiffs }

/// The faithful walk: `reuse_of named` — an unnamed row's cached cell is reused, a named one's
/// is evaluated.
let private iFaithful (named: FStar_Pervasives_Native.option<string list>) : ModelInc.reuse_fn = ModelInc.reuse_of named

/// The go-red: every row's cached cell is reused, the delta ignored — a named row that changed
/// answers with the cell it had.
let private iReuseAll (_: FStar_Pervasives_Native.option<string list>) : ModelInc.reuse_fn = fun _ -> true

// ---- Phase 434 — the RELATIONAL verbs' output schema and refusals (`proofs/Relational.fst`),
//      against the evaluator step by step and against `SchemaWalk.ofTransform` ----

/// The relational primitives instantiated at production's own: the canonical cell text, an int's
/// text, the string order `List.sortBy` sorts under, and the join's suffix rule as `DECISIONS.md`
/// D20 states it.
let private rPrims: ModelRel.rprims =
    { ModelRel.rprims.cell_string = fun c -> DataFrame.cellString (pCellOfModel c)
      ModelRel.rprims.int_text = fun n -> string (int n)
      ModelRel.rprims.string_lt = fun a b -> compare a b < 0
      ModelRel.rprims.candidate =
        fun name n ->
            if n = 1I then
                name + "_right"
            else
                name + "_right_" + string (int n) }

/// The go-red's suffix rule: the pre-D20 one, every collision to `_right` whatever is taken.
let private rPrimsPreD20: ModelRel.rprims =
    { rPrims with
        ModelRel.rprims.candidate = fun name _ -> name + "_right" }

/// The model's resolver — production's, with the table crossed to a frame.
let private rResolver (resolve: string -> Result<Table, EvalError>) : ModelRel.resolver =
    fun name ->
        match resolve name with
        | Ok t -> ModelPipe.Ok(pFrameOfTable t)
        | Error e -> ModelPipe.Error(pErrToModel e)

/// The walk's sources — the declared schemas, as the model reads them.
let private rSources (sources: string -> Schema option) : ModelRel.sources =
    fun name ->
        match sources name with
        | Some s -> FStar_Pervasives_Native.Some(s |> List.map (fun f -> f.Name, pColToModel f.Type))
        | None -> FStar_Pervasives_Native.None

// ---- Phase 464 — the INCREMENTAL REFRESH PAST THE ROW-LOCAL CLASS against the model ----
//
// `proofs/IncrementalMaintained.fst` models `Incremental.prime` / `Incremental.refresh` over the
// maintained `GroupBy`, `Project`, `Limit`, `Window` and the filtering joins, beside `Filter` and
// `Derive`: the walk reusing an unnamed row's cells, a group's aggregates reused exactly when every
// member is reusable and its member tokens are the prior group's, a join's verdicts reused under the
// prior key index, nothing reused after a window or a retyped derive. `proofs/oracle/
// IncrementalMaintained.fs` is that model extracted. This runs it BESIDE production over generated
// pipelines of those classes, a prior table keyed by `id`, deltas `Delta.diff` builds to a mutated
// table — rows removed (a group's last row among them), cells moved (a partition key among them),
// rows appended and removed across a limit's window — and a relation `r` the join reads through the
// resolver, moved between the prime and the refresh one time in two. The model's parameters are
// production's own: `DataFrame.aggregateCells`, `DataFrame.joinKeysMatch`, `DataFrame.windowStep`.

/// The aggregate function, the join key match, the window column and the group token, at
/// production's own.
let private mPrims: ModelMaint.mprims =
    { ModelMaint.mprims.agg_eval =
        fun fn ty cells ->
            match DataFrame.aggregateCells (pBack pAggFns fn) (pColOfModel ty) (cells |> List.map pCellOfModel) with
            | Ok c -> ModelPipe.Ok(pCellToModel c)
            | Error e -> ModelPipe.Error(pErrToModel e)
      ModelMaint.mprims.key_eq =
        fun lk rk -> DataFrame.joinKeysMatch (lk |> List.map pCellOfModel) (rk |> List.map pCellOfModel)
      ModelMaint.mprims.win_cells =
        fun cols spec rows ->
            let schema = cols |> List.map (fun (n, ty) -> Field.create n (pColOfModel ty))

            let wspec: WindowSpec =
                { PartitionBy = spec.partition_by
                  OrderBy = spec.order_by |> List.map (fun (c, d) -> c, pBack pSortDirs d)
                  Fn = pWindowFnOfModel spec.w_fn
                  Of = spec.w_of
                  As = spec.w_as }

            match DataFrame.windowStep schema (rows |> List.map (List.map pCellOfModel)) wspec with
            | Ok(_, out) -> ModelPipe.Ok(out |> List.map (fun r -> pCellToModel (List.last r)))
            | Error e -> ModelPipe.Error(pErrToModel e)
      ModelMaint.mprims.key_token =
        fun cells ->
            "group:"
            + (cells |> List.map (fun c -> sprintf "%A" c) |> String.concat "\u001f") }

/// Phase 471 — the sort's parameters at production's own: the row comparator `compareResolved`
/// over `resolveSortKeys`, the float carrier's order `Kernels.compareFloat`, `DecimalText.compare`
/// and `tryCanonical`, and `String.CompareOrdinal`.
let private sPrims: ModelSort.sprims =
    { ModelSort.sprims.row_cmp =
        fun cols ks r1 r2 ->
            let schema = cols |> List.map (fun (n, ty) -> Field.create n (pColOfModel ty))
            let by = ks |> List.map (fun (c, d) -> c, pBack pSortDirs d)
            let keys = DataFrame.resolveSortKeys schema by

            bigint (
                DataFrame.compareResolved
                    keys
                    (r1 |> List.map pCellOfModel |> List.toArray)
                    (r2 |> List.map pCellOfModel |> List.toArray)
            )
      ModelSort.sprims.cmp_float =
        fun a b ->
            let parse (x: string) =
                System.Double.Parse(x, System.Globalization.NumberStyles.Float, inv)

            bigint (Kernels.compareFloat (parse a) (parse b))
      ModelSort.sprims.cmp_dec =
        fun a b ->
            match DecimalText.compare a b with
            | Some c -> FStar_Pervasives_Native.Some(bigint c)
            | None -> FStar_Pervasives_Native.None
      ModelSort.sprims.canon_dec =
        fun a ->
            match DecimalText.tryCanonical a with
            | Some c -> FStar_Pervasives_Native.Some c
            | None -> FStar_Pervasives_Native.None
      ModelSort.sprims.cmp_ord = fun a b -> bigint (System.String.CompareOrdinal(a, b)) }

/// The walk's step alphabet, from the model's transform.
let private mStepOf (t: ModelPipe.transform) : ModelMaint.mstep =
    match t with
    | ModelPipe.Filter e -> ModelMaint.MFilter e
    | ModelPipe.Derive(name, e) -> ModelMaint.MDerive(name, e)
    | ModelPipe.Project pairs -> ModelMaint.MProject pairs
    | ModelPipe.Limit(ModelPipe.SlotLit n, ModelPipe.SlotLit off) -> ModelMaint.MLimit(n, off)
    | ModelPipe.Window spec -> ModelMaint.MWindow spec
    | ModelPipe.Join(src, on, ModelPipe.Semi) -> ModelMaint.MJoin(src, on, true)
    | ModelPipe.Join(src, on, ModelPipe.Anti) -> ModelMaint.MJoin(src, on, false)
    | ModelPipe.Sort keys ->
        ModelMaint.MSort(
            keys
            |> List.map (fun (c, d) ->
                match c with
                | ModelPipe.SlotLit n -> n, d
                | ModelPipe.SlotParam p -> failwithf "a sort key the walk never sees, the param %s" p)
        )
    | other -> failwithf "not a step the maintained walk takes: %A" other

/// A pipeline split as production splits it: a prefix, at most one `GroupBy`, a tail.
let private mPipeOf (p: Transform list) : ModelMaint.mpipe =
    let rec split acc rest =
        match rest with
        | [] ->
            { ModelMaint.mpipe.prefix = List.rev acc
              ModelMaint.mpipe.grouped = FStar_Pervasives_Native.None }
        | GroupBy(keys, aggs) :: tail ->
            match pTransformToModel (GroupBy(keys, aggs)) with
            | ModelPipe.GroupBy(mkeys, maggs) ->
                { ModelMaint.mpipe.prefix = List.rev acc
                  ModelMaint.mpipe.grouped =
                    FStar_Pervasives_Native.Some(mkeys, maggs, tail |> List.map (pTransformToModel >> mStepOf)) }
            | _ -> failwith "unreachable"
        | t :: more -> split (mStepOf (pTransformToModel t) :: acc) more

    split [] p

/// The relation the join reads, through the resolver production and the model are both handed.
let private mResolveOf (relation: Table) : string -> Result<Table, EvalError> =
    fun name ->
        if name = "r" then
            Ok relation
        else
            Error(UnresolvedSource name)

/// A relation over `g`: some of the three group keys, each once.
let private mGenRelation (rng: ConfRng.T) : Table * ConfRng.T =
    let mask, r1 = ConfRng.intBelow 8 rng

    let keys =
        [ "a"; "b"; "c" ]
        |> List.indexed
        |> List.filter (fun (i, _) -> (mask >>> i) &&& 1 = 1)
        |> List.map snd

    { Schema = [ Field.create "g" StringType; Field.create "n" IntType ]
      Columns =
        [ KitColumn.create "g" StringType (keys |> List.map Str)
          KitColumn.create "n" IntType (keys |> List.mapi (fun i _ -> Int i)) ] },
    r1

/// One prefix step of the maintained classes beside the row-local two.
let private mGenStep (rng: ConfRng.T) : Transform * ConfRng.T =
    let k, r1 = ConfRng.intBelow 10 rng

    match k with
    | 0 ->
        let e, r2 = pGenExpr 2 r1
        Filter e, r2
    | 1 ->
        let e, r2 = pGenExpr 2 r1
        Derive("d", e), r2
    | 2 ->
        let j, r2 = ConfRng.intBelow 2 r1

        (if j = 0 then
             Project [ "id", "id"; "g", "g"; "v", "v"; "w", "w"; "m", "m" ]
         else
             Project [ "g", "g"; "v", "v2"; "w", "w"; "id", "id" ]),
        r2
    | 3 ->
        let n, r2 = ConfRng.intBelow 5 r1
        let off, r3 = ConfRng.intBelow 3 r2
        Limit(Slot.Lit n, Slot.Lit off), r3
    | 4 ->
        let f, r2 = ConfRng.intBelow 5 r1
        let fn = [| RowNumber; Rank; Lag; CumulSum; RollingMean |].[f]

        Window
            { PartitionBy = [ "g" ]
              OrderBy = [ "v", Asc ]
              Fn = fn
              Of = "v"
              As = "wn" },
        r2
    | 5 ->
        let j, r2 = ConfRng.intBelow 2 r1
        Join(Ref "r", [ "g", "g" ], (if j = 0 then Semi else Anti)), r2
    | _ ->
        // Phase 471 — a `Sort`, three draws in ten: over the group column (three values, so ties
        // are the rule and the arrival position decides) two times in five, over the int or the
        // decimal column descending (nulls among them), or over two keys.
        let j, r2 = ConfRng.intBelow 5 r1

        (match j with
         | 0 -> Transform.sortBy [ "g", Asc ]
         | 1 -> Transform.sortBy [ "g", Desc ]
         | 2 -> Transform.sortBy [ "v", Desc ]
         | 3 -> Transform.sortBy [ "m", Asc ]
         | _ -> Transform.sortBy [ "g", Desc; "w", Asc ]),
        r2

/// A pipeline of the maintained classes: a prefix of up to two steps — one time in four a retype
/// corner (Phase 470), whose derived column the aggregates may then read — a `GroupBy` over `g` one
/// time in two with one or two aggregates, and a tail of up to two steps over the group table.
let private mGenPipeline (rng: ConfRng.T) : Transform list * ConfRng.T =
    let corner, r0 = ConfRng.intBelow 4 rng
    let n, r1 = ConfRng.intBelow 3 r0

    let rec draw k acc r =
        if k = 0 then
            List.rev acc, r
        else
            let s, r' = mGenStep r
            draw (k - 1) (s :: acc) r'

    let prefix, r2 =
        if corner = 0 then
            let which, r' = ConfRng.intBelow pCornerShapes.Length r1
            pCornerShapes[which], r'
        else
            draw n [] r1

    let grouped, r3 = ConfRng.intBelow 2 r2

    if grouped = 0 then
        prefix, r3
    else
        let fns =
            [| Sum; Mean; Min; Max; Count; Median; StdDev; First; Last; CountDistinct |]

        let ofs =
            if corner = 0 then
                [| "v"; "w"; "m"; "d" |]
            else
                [| "v"; "w"; "m" |]

        let f1, r4 = ConfRng.intBelow fns.Length r3
        let o1, r5 = ConfRng.intBelow ofs.Length r4
        let two, r6 = ConfRng.intBelow 2 r5
        let f2, r7 = ConfRng.intBelow fns.Length r6
        let o2, r8 = ConfRng.intBelow ofs.Length r7

        let aggs =
            [ pAgg "a1" fns[f1] ofs[o1] ]
            @ (if two = 1 then [ pAgg "a2" fns[f2] ofs[o2] ] else [])

        let tailKind, r9 = ConfRng.intBelow 4 r8

        let tail =
            match tailKind with
            | 0 -> []
            | 1 -> [ Derive("t", Binary(Add, Col "a1", Lit(Int 1))) ]
            | 2 -> [ Filter(Binary(Gt, Col "a1", Lit(Int 0))); Limit(Slot.Lit 2, Slot.Lit 0) ]
            | _ ->
                [ Derive("t", Binary(Mul, Col "a1", Lit(Int 2)))
                  Filter(Binary(Ne, Col "t", Lit(Int 4))) ]

        prefix @ [ GroupBy([ "g" ], aggs) ] @ tail, r9

let private mRenderModel (st: ModelMaint.mstate) : string =
    iRender (pTableOfFrame (ModelInc.frame_of_walked st.m_out))

/// Production's outcome and the model's, compared on the table or on the error.
let private mCompare
    (label: string)
    (prod: Result<Fuaran.Compute.IncrementalEval, EvalError>)
    (model: ModelPipe.outcome<ModelMaint.mstate, ModelPipe.eval_error>)
    : string option =
    match prod, model with
    | Ok ps, ModelPipe.Ok ms ->
        let a = iRender (Fuaran.Compute.Incremental.result ps)
        let b = mRenderModel ms

        if a = b then
            None
        else
            Some(sprintf "%s: result differs — production %s, model %s" label a b)
    | Error pe, ModelPipe.Error me when pErrToModel pe = me -> None
    | Error pe, ModelPipe.Error me ->
        Some(
            sprintf
                "%s: error differs — production %s, model %s"
                label
                (pRenderError pe)
                (pRenderError (pErrOfModel me))
        )
    | Ok _, ModelPipe.Error me ->
        Some(sprintf "%s: production evaluated, model refused %s" label (pRenderError (pErrOfModel me)))
    | Error pe, ModelPipe.Ok _ -> Some(sprintf "%s: production refused %s, model evaluated" label (pRenderError pe))

/// The model's refresh against the reference evaluator over the same table — `mrefresh_agrees`
/// read numerically.
let private mAgainstReference
    (label: string)
    (resolve: string -> Result<Table, EvalError>)
    (p: Transform list)
    (t: Table)
    (model: ModelPipe.outcome<ModelMaint.mstate, ModelPipe.eval_error>)
    : string option =
    match DataFrame.evalPipelineWithInEnv resolve pEnv p t, model with
    | Ok rt, ModelPipe.Ok ms ->
        let a = iRender rt
        let b = mRenderModel ms

        if a = b then
            None
        else
            Some(sprintf "%s: the model's refresh differs from the reference — reference %s, model %s" label a b)
    | Error re, ModelPipe.Error me when pErrToModel re = me -> None
    | Error re, ModelPipe.Error me ->
        Some(
            sprintf
                "%s: the model's refresh error differs from the reference's — reference %s, model %s"
                label
                (pRenderError re)
                (pRenderError (pErrOfModel me))
        )
    | Ok _, ModelPipe.Error me ->
        Some(
            sprintf "%s: the reference evaluated, the model's refresh refused %s" label (pRenderError (pErrOfModel me))
        )
    | Error re, ModelPipe.Ok _ ->
        Some(sprintf "%s: the reference refused %s, the model's refresh evaluated" label (pRenderError re))

type private MaintTally =
    {
        /// Every disagreement, rendered — the tally's verdict is that this is empty.
        MDiffs: string list
        /// Trials whose prime both sides evaluated to a table.
        MPrimed: int
        /// Trials both sides refused at the prime, with the same error.
        MRefused: int
        /// Refreshes compared.
        MRefreshed: int
        /// Primed trials whose pipeline carried a `GroupBy`.
        MGrouped: int
        /// Refreshes of a grouped pipeline whose delta removed a group's last row (the group table shrank).
        MGroupVanished: int
        /// Refreshes of a pipeline with a `Limit` whose kept rows moved (the slice differs from the prior's).
        MSliceMoved: int
        /// Refreshes of a pipeline with a `Window` whose delta changed a row's partition key.
        MPartitionMoved: int
        /// Refreshes of a pipeline with a filtering join whose relation moved between the prime and the refresh.
        MRelationMoved: int
        /// Refreshes of a pipeline with a `Project`.
        MProjected: int
        /// Refreshes of a pipeline with a `Sort` (Phase 471).
        MSorted: int
        /// Refreshes of a sorted pipeline whose produced order moved (a key changed, a row arrived
        /// or left, the stable rows reordered).
        MOrderMoved: int
        /// Refreshes of a sorted pipeline under a mutation that reordered the source's rows.
        MReordered: int
    }

let private mEmptyTally =
    { MDiffs = []
      MPrimed = 0
      MRefused = 0
      MRefreshed = 0
      MGrouped = 0
      MGroupVanished = 0
      MSliceMoved = 0
      MPartitionMoved = 0
      MRelationMoved = 0
      MProjected = 0
      MSorted = 0
      MOrderMoved = 0
      MReordered = 0 }

/// A perturbation of the model's prior state before a refresh — the faithful run leaves it as it
/// is; a go-red breaks one maintenance rule. Handed the prior state, the pipeline, the new table
/// and the relation at the refresh.
type private MaintPerturb = ModelMaint.mstate -> Transform list -> Table -> Table -> ModelMaint.mstate

let private mFaithful: MaintPerturb = fun st _ _ _ -> st

/// The row count of a production result, where it is one.
let private mRowsOf (r: Result<Fuaran.Compute.IncrementalEval, EvalError>) : int option =
    match r with
    | Ok s -> Some(Table.rowCount (Fuaran.Compute.Incremental.result s))
    | Error _ -> None

/// The `g` cell of the row under a key, where the table holds it.
let private mGroupKeyOf (t: Table) (key: string) : Cell option =
    let gs =
        t.Columns |> List.tryFind (fun c -> c.Name = "g") |> Option.map Column.toCells

    let n = Table.rowCount t

    [ for i in 0 .. n - 1 do
          match iIdw.KeyOf t i, gs with
          | Some k, Some cells when Delta.refToken (ByKey(iIdw.KeyString k)) = key -> yield cells[i]
          | _ -> () ]
    |> List.tryHead

/// One trial: a prime, a refresh under the diff to a mutated table (the relation moved one time
/// in two), compared to production and to the reference; and the state theorem read observably,
/// a second refresh of the refreshed state against a second refresh of a state primed over the
/// first mutation, on the model.
let private mTrial
    (label: string)
    (reuse: FStar_Pervasives_Native.option<string list> -> ModelInc.reuse_fn)
    (perturb: MaintPerturb)
    (rng: ConfRng.T)
    (t: MaintTally)
    : MaintTally * ConfRng.T =
    let p, r1 = mGenPipeline rng
    let t0, r2 = iGenTable r1
    let rel0, r3 = mGenRelation r2
    let moveRel, r4 = ConfRng.intBelow 2 r3
    let rel1, r5 = if moveRel = 0 then rel0, r4 else mGenRelation r4
    let pipe = mPipeOf p
    let menv = pEnvToModel pEnv

    let hasGroup =
        p
        |> List.exists (function
            | GroupBy _ -> true
            | _ -> false)

    let has (f: Transform -> bool) = p |> List.exists f

    let diffs = ResizeArray<string>()
    let add (d: string option) = d |> Option.iter diffs.Add

    let modelRun (resolve) (reuseFn: ModelInc.reuse_fn) (st: ModelMaint.mstate) (table: Table) =
        ModelMaint.mrun
            mPrims
            sPrims
            rPrims
            pPrims
            (rResolver resolve)
            menv
            reuseFn
            st
            (pFrameOfTable table).cols
            (iAliveOf table)
            pipe

    let prod0 = Fuaran.Compute.Incremental.prime (mResolveOf rel0) pEnv iIdw p t0

    let model0 =
        modelRun (mResolveOf rel0) ModelInc.no_reuse (ModelMaint.empty_state (pFrameOfTable t0).cols) t0

    add (mCompare (label + " prime") prod0 model0)
    add (mAgainstReference (label + " prime") (mResolveOf rel0) p t0 model0)

    match prod0, model0 with
    | Ok ps0, ModelPipe.Ok ms0 ->
        let t1, r6 = mMutateSome t0 r5

        match Delta.diff iIdw t0 t1 with
        | Error _ ->
            { t with
                MDiffs = List.rev (List.ofSeq diffs) @ t.MDiffs
                MPrimed = t.MPrimed + 1 },
            r6
        | Ok d1 ->
            let named1 = iNamedOf d1

            let prod1 =
                Fuaran.Compute.Incremental.refresh (mResolveOf rel1) pEnv iIdw p ps0 d1 t1

            let model1 = modelRun (mResolveOf rel1) (reuse named1) (perturb ms0 p t1 rel1) t1
            add (mCompare (label + " refresh") prod1 model1)
            add (mAgainstReference (label + " refresh") (mResolveOf rel1) p t1 model1)

            let vanished =
                match hasGroup, mRowsOf prod0, mRowsOf prod1 with
                | true, Some a, Some b when b < a -> 1
                | _ -> 0

            let sliceMoved =
                match
                    has (function
                        | Limit _ -> true
                        | _ -> false),
                    prod0,
                    prod1
                with
                | true, Ok a, Ok b when
                    iRender (Fuaran.Compute.Incremental.result a)
                    <> iRender (Fuaran.Compute.Incremental.result b)
                    ->
                    1
                | _ -> 0

            let partitionMoved =
                match
                    has (function
                        | Window _ -> true
                        | _ -> false),
                    d1
                with
                | true, RowSet r ->
                    if
                        r.Rows
                        |> List.exists (fun (ref, kind) ->
                            kind = RowChanged
                            && mGroupKeyOf t0 (Delta.refToken ref) <> mGroupKeyOf t1 (Delta.refToken ref))
                    then
                        1
                    else
                        0
                | _ -> 0

            let relationMoved =
                if
                    has (function
                        | Join _ -> true
                        | _ -> false)
                    && moveRel = 1
                then
                    1
                else
                    0

            let projected =
                if
                    has (function
                        | Project _ -> true
                        | _ -> false)
                then
                    1
                else
                    0

            let sorted =
                if
                    has (function
                        | Sort _ -> true
                        | _ -> false)
                then
                    1
                else
                    0

            let orderMoved =
                match sorted, prod0, prod1 with
                | 1, Ok a, Ok b when
                    iRender (Fuaran.Compute.Incremental.result a)
                    <> iRender (Fuaran.Compute.Incremental.result b)
                    ->
                    1
                | _ -> 0

            let reordered =
                if
                    sorted = 1
                    && iTokens t0 <> iTokens t1
                    && Set.ofList (iTokens t0) = Set.ofList (iTokens t1)
                then
                    1
                else
                    0

            // The state theorem, observably, on the model: a refresh of the refreshed state is a
            // refresh of a state primed over the first mutation.
            (match prod1, model1 with
             | Ok _, ModelPipe.Ok ms1 ->
                 let t2, _ = mMutateSome t1 r6

                 match Delta.diff iIdw t1 t2 with
                 | Ok d2 ->
                     let named2 = iNamedOf d2

                     let fromRefreshed =
                         modelRun (mResolveOf rel1) (reuse named2) (perturb ms1 p t2 rel1) t2

                     let modelPrimed1 =
                         modelRun
                             (mResolveOf rel1)
                             ModelInc.no_reuse
                             (ModelMaint.empty_state (pFrameOfTable t1).cols)
                             t1

                     (match modelPrimed1 with
                      | ModelPipe.Ok mp1 ->
                          let fromPrimed =
                              modelRun (mResolveOf rel1) (reuse named2) (perturb mp1 p t2 rel1) t2

                          match fromRefreshed, fromPrimed with
                          | ModelPipe.Ok a, ModelPipe.Ok b when mRenderModel a <> mRenderModel b ->
                              diffs.Add(
                                  sprintf
                                      "%s: the model's second refresh differs between a refreshed and a primed state"
                                      label
                              )
                          | ModelPipe.Ok _, ModelPipe.Error _
                          | ModelPipe.Error _, ModelPipe.Ok _ ->
                              diffs.Add(
                                  sprintf "%s: the model's second refresh and its primed twin disagree on refusal" label
                              )
                          | _ -> ()
                      | ModelPipe.Error _ -> ())
                 | Error _ -> ()
             | _ -> ())

            { t with
                MDiffs = List.rev (List.ofSeq diffs) @ t.MDiffs
                MPrimed = t.MPrimed + 1
                MRefreshed = t.MRefreshed + 1
                MGrouped = t.MGrouped + (if hasGroup then 1 else 0)
                MGroupVanished = t.MGroupVanished + vanished
                MSliceMoved = t.MSliceMoved + sliceMoved
                MPartitionMoved = t.MPartitionMoved + partitionMoved
                MRelationMoved = t.MRelationMoved + relationMoved
                MProjected = t.MProjected + projected
                MSorted = t.MSorted + sorted
                MOrderMoved = t.MOrderMoved + orderMoved
                MReordered = t.MReordered + reordered },
            r6
    | Error _, ModelPipe.Error _ ->
        { t with
            MDiffs = List.rev (List.ofSeq diffs) @ t.MDiffs
            MRefused = t.MRefused + 1 },
        r5
    | _ ->
        { t with
            MDiffs = List.rev (List.ofSeq diffs) @ t.MDiffs },
        r5

let private mDifferential
    (reuse: FStar_Pervasives_Native.option<string list> -> ModelInc.reuse_fn)
    (perturb: MaintPerturb)
    (seed: int)
    (trials: int)
    : MaintTally =
    let mutable r = ConfRng.ofSeed seed
    let mutable tally = mEmptyTally

    for i in 1..trials do
        let t, r' = mTrial (sprintf "maintained %d" i) reuse perturb r tally
        tally <- t
        r <- r'

    { tally with
        MDiffs = List.rev tally.MDiffs }

/// The go-red for the group class: the prior state's member tokens are overwritten with the
/// CURRENT groups' member tokens, so `sameMembers` can no longer tell that a member left or joined,
/// and a group every remaining member of which is unnamed reuses its prior aggregates — a `Max`
/// over a removed row among them. The prefix is walked as the refresh would walk it to read the
/// groups the refresh will form.
let private mStaleMembers: MaintPerturb =
    fun st p t1 rel1 ->
        let pipe = mPipeOf p

        match pipe.grouped with
        | FStar_Pervasives_Native.None -> st
        | FStar_Pervasives_Native.Some(keys, _, _) ->
            let cols = (pFrameOfTable t1).cols

            match
                ModelMaint.mwalk
                    mPrims
                    sPrims
                    rPrims
                    pPrims
                    (rResolver (mResolveOf rel1))
                    (pEnvToModel pEnv)
                    ModelInc.no_reuse
                    []
                    cols
                    (iAliveOf t1)
                    pipe.prefix
            with
            | ModelPipe.Error _ -> st
            | ModelPipe.Ok res ->
                match res.r_out with
                | ModelInc.W(cols1, a1) ->
                    let groups = ModelMaint.groups_of cols1 keys a1

                    let stale =
                        st.m_groups
                        |> List.map (fun pg ->
                            match groups |> List.tryFind (fun g -> g.g_key = pg.p_key) with
                            | Some g ->
                                { pg with
                                    ModelMaint.pgroup.p_tokens = ModelSort.tokens_of g.g_members }
                            | None -> pg)

                    { st with
                        ModelMaint.mstate.m_groups = stale }

/// The go-red for the filtering-join class: every join entry's key index is overwritten with the
/// index of the relation AT THE REFRESH, so `relationMoved` reads as false and a stable row's prior
/// verdict is reused under a relation that no longer holds its key.
let private mStaleRelation: MaintPerturb =
    fun st _ _ rel1 ->
        let rframe = pFrameOfTable rel1
        let rks = ModelMaint.proj_rows rframe.cols [ "g" ] rframe.rows

        let stale =
            st.m_entries
            |> List.map (fun e ->
                match e.e_keys with
                | FStar_Pervasives_Native.Some _ ->
                    { e with
                        ModelMaint.entry.e_keys = FStar_Pervasives_Native.Some rks }
                | FStar_Pervasives_Native.None -> e)

        { st with
            ModelMaint.mstate.m_entries = stale }

/// Phase 471 — the go-red for the sort class: every sort entry's recorded ARRIVAL is overwritten
/// with the arrival the refresh will see (read off a fresh walk of the prefix over the new table),
/// so `arrivedAsBefore` can no longer tell that the stable rows arrived in another order, and the
/// merge reads the prior produced order under the new positions — a tie now broken the other way
/// stays as it was. The reordering mutation is what makes it fire.
let private mStaleArrival: MaintPerturb =
    fun st p t1 rel1 ->
        let pipe = mPipeOf p
        let cols = (pFrameOfTable t1).cols

        match
            ModelMaint.mwalk
                mPrims
                sPrims
                rPrims
                pPrims
                (rResolver (mResolveOf rel1))
                (pEnvToModel pEnv)
                ModelInc.no_reuse
                []
                cols
                (iAliveOf t1)
                pipe.prefix
        with
        | ModelPipe.Error _ -> st
        | ModelPipe.Ok res ->
            let stale =
                List.zip
                    st.m_entries
                    (if List.length res.r_entries = List.length st.m_entries then
                         res.r_entries
                     else
                         st.m_entries)
                |> List.map (fun (old, fresh) ->
                    match old.e_order, fresh.e_order with
                    | FStar_Pervasives_Native.Some(_, order), FStar_Pervasives_Native.Some(arrival, _) ->
                        { old with
                            ModelMaint.entry.e_order = FStar_Pervasives_Native.Some(arrival, order) }
                    | _ -> old)

            { st with
                ModelMaint.mstate.m_entries = stale }

/// The go-red every class shares with Phase 433's: every row's cached cell is reused, the delta
/// ignored — a named row that moved answers with the cells it had, through a limit's slice, a
/// projection, a window's partition and a group's members alike.
let private mReuseAll (_: FStar_Pervasives_Native.option<string list>) : ModelInc.reuse_fn = fun _ -> true

/// Phase 464 (D37) — the three shapes the probe found production diverging on before the fix: a
/// derived column the cells type, retyped from int to float by ONE named row, read raw by the walk
/// where the reference packs it. Each is a table, a pipeline and the mutation; the test holds the
/// refresh equal to the full evaluation over the mutated table, result or refusal.
let private mRetypeShapes: (string * Transform list) list =
    // d = Case [ x > 10 -> 0.5 ] else x: an int arm beside a float arm, typed by the cells.
    let d = Case([ Binary(Gt, Col "x", Lit(Int 10)), Lit(Float 0.5) ], Col "x")

    let onA inner =
        Case([ Binary(Eq, Col "g", Lit(Str "A")), inner ], Lit(Int 0))

    [ "row-local: a Mod over the retyped column, on the unnamed rows",
      [ Derive("d", d); Derive("m", onA (Binary(Mod, Col "d", Lit(Int 2)))) ]
      "group: a reused Sum over the retyped column, read by a Mod in the tail",
      [ Derive("d", d)
        GroupBy([ "g" ], [ pAgg "s" Sum "d" ])
        Derive("m", onA (Binary(Mod, Col "s", Lit(Int 2)))) ]
      "group: Min and First over the retyped column, recomputed, read by a Mod in the tail",
      [ Derive("d", d)
        GroupBy([ "g" ], [ pAgg "mn" Min "d"; pAgg "fs" First "d" ])
        Derive("m", onA (Binary(Mod, Col "mn", Lit(Int 2)))) ] ]

let private mRetypeTables: Table * Table =
    let table (xs: int list) : Table =
        { Schema =
            [ Field.create "id" IntType
              Field.create "g" StringType
              Field.create "x" IntType ]
          Columns =
            [ KitColumn.create "id" IntType [ Int 1; Int 2; Int 3 ]
              KitColumn.create "g" StringType [ Str "A"; Str "A"; Str "B" ]
              KitColumn.create "x" IntType (xs |> List.map Int) ] }

    // row 3 (group B) moves x to 11: its d is now Float 0.5, the column widens to float; group A stands.
    table [ 1; 2; 3 ], table [ 1; 2; 11 ]

/// Phase 470 (D38) — the shapes the probe found production's walk diverging from the reference on
/// AFTER D37: the reference's typed pass abandoned for the boxed cells (a decimal text past the scaled
/// carrier's width among the column's), so an int read as it was where the walk widened it. The
/// first retypes the column from int to decimal and boxes it in one move; the second keeps the type
/// and boxes it alone. Phase 472 (D40) — the boxed form widens an int as the typed carrier does, so
/// on both shapes the int reads as its decimal on every side and there is no verdict to track; the
/// shapes stand as the regression they were, and the first is where the verdict model is shown
/// failing. Each is a pipeline, the table before and the table after; the test holds the refresh to
/// the full evaluation.
let private pBoxedShapes: (string * Transform list * Table * Table) list =
    let wide = Cell.Decimal "1234567890123456789012345.1234567"

    let table (xs: int list) : Table =
        { Schema =
            [ Field.create "id" IntType
              Field.create "g" StringType
              Field.create "x" IntType ]
          Columns =
            [ KitColumn.create "id" IntType [ Int 1; Int 2; Int 3 ]
              KitColumn.create "g" StringType [ Str "A"; Str "A"; Str "B" ]
              KitColumn.create "x" IntType (xs |> List.map Int) ] }

    let onA inner =
        Case([ Binary(Eq, Col "g", Lit(Str "A")), inner ], Lit(Int 0))

    [ "int to decimal, boxed by the width: an Add of a float over the standing rows",
      [ Derive("d", Case([ Binary(Gt, Col "x", Lit(Int 10)), Lit wide ], Col "x"))
        Derive("m", onA (Binary(Add, Col "d", Lit(Float 0.5)))) ],
      table [ 1; 2; 3 ],
      table [ 1; 2; 11 ]
      "decimal on both, boxed on the second alone: a Mod over the standing rows",
      [ Derive(
            "d",
            Case(
                [ Binary(Gt, Col "x", Lit(Int 10)), Lit wide
                  Binary(Eq, Col "g", Lit(Str "B")), Lit(Cell.Decimal "0.25") ],
                Col "x"
            )
        )
        Derive("m", onA (Binary(Mod, Col "d", Lit(Int 2)))) ],
      table [ 1; 2; 3 ],
      table [ 1; 2; 11 ] ]

/// Production's refresh held to its full evaluation over the mutated table, result or refusal.
let private pHoldRefreshToFull (label: string) (p: Transform list) (before: Table) (after: Table) : unit =
    let full = DataFrame.evalPipelineWithInEnv DataFrame.noResolve Map.empty p after

    let refreshed =
        match Fuaran.Compute.Incremental.prime DataFrame.noResolve Map.empty iIdw p before with
        | Error e -> failtestf "%s: the prime refused %s" label (pRenderError e)
        | Ok st ->
            match Delta.diff iIdw before after with
            | Error e -> failtestf "%s: the diff refused %A" label e
            | Ok d -> Fuaran.Compute.Incremental.refresh DataFrame.noResolve Map.empty iIdw p st d after

    match full, refreshed with
    | Ok f, Ok r ->
        Expect.equal
            (iRender (Fuaran.Compute.Incremental.result r))
            (iRender f)
            (sprintf "%s: the refresh differs from the full evaluation" label)
    | Error fe, Error re ->
        Expect.equal re fe (sprintf "%s: the refresh's error differs from the full evaluation's" label)
    | Ok f, Error re ->
        failtestf "%s: the full evaluation answered %s, the refresh refused %s" label (iRender f) (pRenderError re)
    | Error fe, Ok r ->
        failtestf
            "%s: the full evaluation refused %s, the refresh answered %s"
            label
            (pRenderError fe)
            (iRender (Fuaran.Compute.Incremental.result r))

/// Phase 470 — the reference's derive BEFORE this phase: the cells held raw, appended under the
/// decided type (D37 item 4). Kept once, to show the corner the old reference answered where
/// production refuses; nothing else calls it.
let private pRawDerive
    (env: ModelPipe.param_env)
    (f: ModelPipe.frame)
    (name: string)
    (x: ModelPipe.col_expr)
    : ModelPipe.outcome<ModelPipe.frame, ModelPipe.eval_error> =
    match ModelPipe.derived_typing_in env f.cols x with
    | ModelPipe.Refused -> ModelPipe.Error(ModelPipe.float_beside_decimal name)
    | dt ->
        match ModelPipe.derive_cells pPrims env f.cols f.rows x with
        | ModelPipe.Error e -> ModelPipe.Error e
        | ModelPipe.Ok cells ->
            match ModelPipe.column_type_by dt name cells with
            | ModelPipe.Error e -> ModelPipe.Error e
            | ModelPipe.Ok ty ->
                ModelPipe.Ok
                    { ModelPipe.frame.cols = f.cols @ [ name, ty ]
                      ModelPipe.frame.rows = ModelPipe.zip_append f.rows cells }

/// Phase 472 — the reference's derive as Phase 470 modelled it, kept once: a VERDICT over the whole
/// column — typed where `Vec.packAt` carries the cells typed, the cells packed under the type; boxed
/// with every cell AS IT WAS otherwise. Shown failing the differential once, on the first boxed shape,
/// where production now reads the int as its decimal on the boxed form too; nothing else calls it.
let private pVerdictDerive
    (env: ModelPipe.param_env)
    (f: ModelPipe.frame)
    (name: string)
    (x: ModelPipe.col_expr)
    : ModelPipe.outcome<ModelPipe.frame, ModelPipe.eval_error> =
    match ModelPipe.derived_typing_in env f.cols x with
    | ModelPipe.Refused -> ModelPipe.Error(ModelPipe.float_beside_decimal name)
    | dt ->
        match ModelPipe.derive_cells pPrims env f.cols f.rows x with
        | ModelPipe.Error e -> ModelPipe.Error e
        | ModelPipe.Ok cells ->
            match ModelPipe.column_type_by dt name cells with
            | ModelPipe.Error e -> ModelPipe.Error e
            | ModelPipe.Ok ty ->
                let typed =
                    match Vec.pack (pColOfModel ty) (cells |> List.map pCellOfModel |> List.toArray) with
                    | Cells _ -> false
                    | _ -> true

                let packed =
                    if typed then
                        ModelPipe.pack_cells pPrims ty cells
                    else
                        cells

                ModelPipe.Ok
                    { ModelPipe.frame.cols = f.cols @ [ name, ty ]
                      ModelPipe.frame.rows = ModelPipe.zip_append f.rows packed }

/// The three models beside production on one corner: the full evaluation, production's prime and
/// refresh, the row-local model's prime and refresh, and the maintained model's — every pair held
/// equal, result or refusal.
let private pCornerAgrees (label: string) (p: Transform list) (before: Table) (after: Table) : unit =
    let menv = pEnvToModel Map.empty

    // The row-local model takes only its own two verbs; a shape with a `GroupBy` is the maintained
    // model's alone.
    let rowLocal =
        p
        |> List.forall (function
            | Filter _
            | Derive _ -> true
            | _ -> false)

    let steps = if rowLocal then iStepsOf p else []
    let pipe = mPipeOf p
    let resolve = rResolver DataFrame.noResolve

    let named =
        match Delta.diff iIdw before after with
        | Error e -> failtestf "%s: the diff refused %A" label e
        | Ok d -> d, iNamedOf d

    let delta, namedTokens = named
    let full = DataFrame.evalPipelineWithInEnv DataFrame.noResolve Map.empty p after

    let prodPrime =
        Fuaran.Compute.Incremental.prime DataFrame.noResolve Map.empty iIdw p before

    let prodRefresh =
        prodPrime
        |> Result.bind (fun st ->
            Fuaran.Compute.Incremental.refresh DataFrame.noResolve Map.empty iIdw p st delta after)

    let incPrime =
        ModelInc.prime pPrims menv (pFrameOfTable before).cols (iAliveOf before) steps

    let incRefresh =
        match incPrime with
        | ModelPipe.Ok st ->
            ModelInc.refresh pPrims menv st namedTokens (pFrameOfTable after).cols (iAliveOf after) steps
        | ModelPipe.Error e -> ModelPipe.Error e

    let maintRun (st: ModelMaint.mstate) (reuseFn: ModelInc.reuse_fn) (t: Table) =
        ModelMaint.mrun mPrims sPrims rPrims pPrims resolve menv reuseFn st (pFrameOfTable t).cols (iAliveOf t) pipe

    let maintPrime =
        maintRun (ModelMaint.empty_state (pFrameOfTable before).cols) ModelInc.no_reuse before

    let maintRefresh =
        match maintPrime with
        | ModelPipe.Ok st -> maintRun st (ModelInc.reuse_of namedTokens) after
        | ModelPipe.Error e -> ModelPipe.Error e

    let fullText =
        match full with
        | Ok t -> iRender t
        | Error e -> "refused " + pRenderError e

    let hold (what: string) (text: string) =
        Expect.equal text fullText (sprintf "%s: %s differs from the full evaluation" label what)

    hold
        "production's refresh"
        (match prodRefresh with
         | Ok r -> iRender (Fuaran.Compute.Incremental.result r)
         | Error e -> "refused " + pRenderError e)

    if rowLocal then
        hold
            "the row-local model's refresh"
            (match incRefresh with
             | ModelPipe.Ok st -> iRenderModel st
             | ModelPipe.Error e -> "refused " + pRenderError (pErrOfModel e))

    hold
        "the maintained model's refresh"
        (match maintRefresh with
         | ModelPipe.Ok st -> mRenderModel st
         | ModelPipe.Error e -> "refused " + pRenderError (pErrOfModel e))

    // And the full evaluation over the MUTATED table on the models' own reference.
    hold
        "the reference model"
        (match
            ModelPipe.eval_uncounted
                pPrims
                (pStepOfProduction DataFrame.noResolve Map.empty)
                menv
                (p |> List.map pTransformToModel)
                (pFrameOfTable after)
         with
         | ModelPipe.Ok f -> iRender (pTableOfFrame f)
         | ModelPipe.Error e -> "refused " + pRenderError (pErrOfModel e))

let private rSchemaOfModel (s: ModelPipe.schema) : (string * ColumnType) list =
    s |> List.map (fun (n, ty) -> n, pColOfModel ty)

let private rSchemaOfProd (s: Schema) : (string * ColumnType) list = s |> List.map (fun f -> f.Name, f.Type)

/// A knowledge read for comparison: closed?, then each column's name and known type. The reason
/// text and the unit are outside the model.
let private rKnowledgeOfProd (k: SchemaKnowledge) : bool * (string * ColumnType option) list =
    SchemaWalk.isClosed k, SchemaWalk.columns k |> List.map (fun c -> c.Name, c.Type)

let private rKnowledgeOfModel (k: ModelRel.knowledge) : bool * (string * ColumnType option) list =
    let closed =
        match k with
        | ModelRel.Closed _ -> true
        | ModelRel.AtLeast _ -> false

    closed,
    ModelRel.columns k
    |> List.map (fun (n, ty) ->
        n,
        match ty with
        | FStar_Pervasives_Native.Some t -> Some(pColOfModel t)
        | FStar_Pervasives_Native.None -> None)

let private rKnowledgeToModel (k: SchemaKnowledge) : ModelRel.knowledge =
    let cols =
        SchemaWalk.columns k
        |> List.map (fun c ->
            c.Name,
            match c.Type with
            | Some t -> FStar_Pervasives_Native.Some(pColToModel t)
            | None -> FStar_Pervasives_Native.None)

    if SchemaWalk.isClosed k then
        ModelRel.Closed cols
    else
        ModelRel.AtLeast cols

type private RelTally =
    { RDiffs: string list
      RSteps: int
      ROk: int
      RErr: int
      RRowRefused: int
      RBeside: int
      RWalks: int
      RVerbs: Set<string> }

let private rEmptyTally =
    { RDiffs = []
      RSteps = 0
      ROk = 0
      RErr = 0
      RRowRefused = 0
      RBeside = 0
      RWalks = 0
      RVerbs = Set.empty }

/// ONE step: production's evaluator against the model's schema and refusal, and production's walk
/// against the model's, over one input table and one knowledge. Answers the next table (where both
/// evaluated) and the next pair of knowledges.
let private rCompareStep
    (label: string)
    (rp: ModelRel.rprims)
    (resolve: string -> Result<Table, EvalError>)
    (sources: string -> Schema option)
    (env: Map<string, Cell>)
    (table: Table)
    (kProd: SchemaKnowledge)
    (kModel: ModelRel.knowledge)
    (t: Transform)
    (tally: RelTally)
    : Table option * SchemaKnowledge * ModelRel.knowledge * RelTally =
    let frame = pFrameOfTable table
    let prod = DataFrame.evalPipelineWithInEnv resolve env [ t ] table

    let model =
        match t with
        // Pipeline.fst's two clauses, not this model's: production's answer carries the table
        // forward and the walk is still compared, the schema is not.
        | Filter _
        | Derive _ -> None
        | _ -> Some(ModelRel.step_schema rp (rResolver resolve) (pEnvToModel env) frame (pTransformToModel t))

    let diff, ok, err, rowRefused, next =
        match prod, model with
        | Ok t', None -> None, 0, 0, 0, Some t'
        | Error _, None -> None, 0, 0, 0, None
        | Ok t', Some(ModelPipe.Ok out) ->
            let a = rSchemaOfProd t'.Schema
            let b = rSchemaOfModel out

            if a <> b then
                Some(sprintf "%s: %s — schema differs: production %A, model %A" label (pVerbTag t) a b),
                1,
                0,
                0,
                Some t'
            else
                None, 1, 0, 0, Some t'
        | Error e, Some(ModelPipe.Error me) ->
            (if pErrToModel e <> me then
                 Some(
                     sprintf
                         "%s: %s — refusal differs: production %s, model %s"
                         label
                         (pVerbTag t)
                         (pRenderError e)
                         (pRenderError (pErrOfModel me))
                 )
             else
                 None),
            0,
            1,
            0,
            None
        | Error(AggError _), Some(ModelPipe.Ok _) ->
            // A refusal that read the cells: an aggregate over a column it cannot aggregate. The
            // row function's, outside the schema model by its header.
            None, 0, 0, 1, None
        | Ok t', Some(ModelPipe.Error me) ->
            Some(
                sprintf
                    "%s: %s — production evaluated to %A, model refused %s"
                    label
                    (pVerbTag t)
                    (rSchemaOfProd t'.Schema)
                    (pRenderError (pErrOfModel me))
            ),
            1,
            0,
            0,
            None
        | Error e, Some(ModelPipe.Ok out) ->
            Some(
                sprintf
                    "%s: %s — production refused %s, model evaluated to %A"
                    label
                    (pVerbTag t)
                    (pRenderError e)
                    (rSchemaOfModel out)
            ),
            0,
            1,
            0,
            None

    // A union production refuses on D18 — two types no column type holds together — that the
    // model refuses with the same text, through the cells' typing's own rule (Phase 476). Counted
    // so the case below can say the differential MET the refusal rather than never reaching it.
    let beside =
        match t, prod, model with
        | Union _, Error((TypeError detail) as e), Some(ModelPipe.Error me) when
            detail.Contains "which no column type holds together" && pErrToModel e = me
            ->
            1
        | _ -> 0

    let kProd' = SchemaWalk.ofTransform sources kProd t
    let kModel' = ModelRel.walk rp (rSources sources) kModel (pTransformToModel t)
    let wp = rKnowledgeOfProd kProd'
    let wm = rKnowledgeOfModel kModel'

    let walkDiff =
        if wp <> wm then
            Some(sprintf "%s: %s — the walk differs: production %A, model %A" label (pVerbTag t) wp wm)
        else
            None

    // `walk_sound` measured on the shipped evaluator (Phase 469, every verb, the union included):
    // where production answers a table, the walk's knowledge is sound for the schema it answers,
    // read by the model's own `sound`.
    let soundDiff =
        match prod with
        | Ok t' when not (ModelRel.sound kModel' (pFrameOfTable t').cols) ->
            Some(
                sprintf
                    "%s: %s — the walk is not sound for the evaluator's output: walk %A, evaluator %A"
                    label
                    (pVerbTag t)
                    wm
                    (rSchemaOfProd t'.Schema)
            )
        | _ -> None

    next,
    kProd',
    kModel',
    { tally with
        RDiffs =
            (Option.toList soundDiff @ Option.toList walkDiff @ Option.toList diff)
            @ tally.RDiffs
        RSteps = tally.RSteps + 1
        ROk = tally.ROk + ok
        RErr = tally.RErr + err
        RRowRefused = tally.RRowRefused + rowRefused
        RBeside = tally.RBeside + beside
        RWalks = tally.RWalks + 1
        RVerbs = Set.add (pVerbTag t) tally.RVerbs }

/// A whole pipeline step by step from a table, stopping at the first step both sides refuse; the
/// walk continues to the end, as `SchemaWalk.ofPipeline` does.
let private rComparePipeline
    (label: string)
    (rp: ModelRel.rprims)
    (resolve: string -> Result<Table, EvalError>)
    (sources: string -> Schema option)
    (env: Map<string, Cell>)
    (table: Table)
    (p: Transform list)
    (tally: RelTally)
    : RelTally =
    let rec go (table: Table option) kProd kModel steps tally i =
        match steps with
        | [] -> tally
        | t :: rest ->
            match table with
            | Some table ->
                let next, kProd', kModel', tally' =
                    rCompareStep (sprintf "%s/%d" label i) rp resolve sources env table kProd kModel t tally

                go next kProd' kModel' rest tally' (i + 1)
            | None ->
                // the evaluation stopped; the walk alone goes on
                let kProd' = SchemaWalk.ofTransform sources kProd t
                let kModel' = ModelRel.walk rp (rSources sources) kModel (pTransformToModel t)

                let d =
                    if rKnowledgeOfProd kProd' <> rKnowledgeOfModel kModel' then
                        [ sprintf
                              "%s/%d: %s — the walk differs past a refusal: production %A, model %A"
                              label
                              i
                              (pVerbTag t)
                              (rKnowledgeOfProd kProd')
                              (rKnowledgeOfModel kModel') ]
                    else
                        []

                go
                    None
                    kProd'
                    kModel'
                    rest
                    { tally with
                        RDiffs = d @ tally.RDiffs
                        RWalks = tally.RWalks + 1 }
                    (i + 1)

    let k0 = SchemaWalk.ofSchema table.Schema
    go (Some table) k0 (rKnowledgeToModel k0) p tally 0

let private rTable (cols: (string * ColumnType * Cell list) list) : Table =
    { Schema = cols |> List.map (fun (n, ty, _) -> Field.create n ty)
      Columns = cols |> List.map (fun (n, ty, cells) -> KitColumn.create n ty cells) }

/// The shapes the phase names, each with the table it runs over: the 411 collisions, the 412
/// unknown sort keys, the window's taken output (D28), the union's widening (D18), and the four
/// repeated-name witnesses Phase 434 found answering `Ok` and Phase 468 (D35) made refusals.
let private rShapes: (string * Table * Transform list) list =
    let xr = rTable [ "x", IntType, [ Int 1 ]; "x_right", IntType, [ Int 2 ] ]
    let x = rTable [ "x", IntType, [ Int 1 ] ]

    let xr2 =
        rTable
            [ "x", IntType, [ Int 1 ]
              "x_right", IntType, [ Int 2 ]
              "x_right_2", IntType, [ Int 3 ] ]

    let people =
        rTable [ "g", StringType, [ Str "a"; Str "b" ]; "v", IntType, [ Int 1; Int 2 ] ]

    [ "411-left-carries-x-and-x_right", xr, [ Join(Embedded x, [ "x", "x" ], Inner) ]
      "411-right-carries-x-and-x_right", x, [ Join(Embedded xr, [ "x", "x" ], Left) ]
      "411-both-carry-three", xr2, [ Join(Embedded xr2, [ "x", "x" ], Outer) ]
      "411-right-join", x, [ Join(Embedded xr, [ "x", "x" ], Right) ]
      "411-semi-keeps-left", xr, [ Join(Embedded x, [ "x", "x" ], Semi) ]
      "411-unknown-left-key", xr, [ Join(Embedded x, [ "nope", "x" ], Inner) ]
      "411-unknown-right-key", xr, [ Join(Embedded x, [ "x", "nope" ], Inner) ]
      "412-literal-unknown-key", people, [ Sort [ Slot.Lit "nope", Asc ] ]
      "412-second-key-unknown", people, [ Sort [ Slot.Lit "g", Asc; Slot.Lit "zz", Desc ] ]
      "412-param-unknown-key", people, [ Sort [ Slot.Param "sortcol", Asc; Slot.Lit "zz", Asc ] ]
      "412-unbound-param", people, [ Sort [ Slot.Param "absent", Asc ] ]
      "412-param-not-a-string", people, [ Sort [ Slot.Param "n", Asc ] ]
      "412-window-order-key-unknown",
      people,
      [ Window
            { PartitionBy = [ "g" ]
              OrderBy = [ "zz", Asc ]
              Fn = RowNumber
              Of = "v"
              As = "r" } ]
      "412-window-partition-key-unknown",
      people,
      [ Window
            { PartitionBy = [ "zz" ]
              OrderBy = [ "v", Asc ]
              Fn = RowNumber
              Of = "v"
              As = "r" } ]
      "450-window-output-taken",
      people,
      [ Window
            { PartitionBy = []
              OrderBy = [ "v", Asc ]
              Fn = RowNumber
              Of = "v"
              As = "g" } ]
      "441-window-ntile-zero",
      people,
      [ Window
            { PartitionBy = []
              OrderBy = [ "v", Asc ]
              Fn = NTile 0
              Of = "v"
              As = "r" } ]
      "window-lag-keeps-source-type",
      people,
      [ Window
            { PartitionBy = []
              OrderBy = [ "v", Asc ]
              Fn = Lag
              Of = "g"
              As = "r" } ]
      "423-union-widens",
      rTable [ "a", IntType, [ Int 1 ] ],
      [ Union(Embedded(rTable [ "a", FloatType, [ Float 1.5 ] ])) ]
      "423-union-refuses-unrelated",
      rTable [ "a", IntType, [ Int 1 ] ],
      [ Union(Embedded(rTable [ "a", StringType, [ Str "s" ] ])) ]
      "union-names-differ", people, [ Union(Embedded x) ]
      "intersect-names-differ", people, [ Intersect(Embedded x) ]
      "except-keeps-left", people, [ Except(Embedded people) ]
      "limit-param-not-an-int", people, [ Limit(Slot.Param "s", Slot.Lit 0) ]
      "limit-offset-unbound", people, [ Limit(Slot.Lit 1, Slot.Param "absent") ]
      "468-project-repeats", rTable [ "a", IntType, [ Int 1 ] ], [ Project [ "a", "x"; "a", "x" ] ]
      "468-group-by-repeats",
      rTable [ "k", StringType, [ Str "a" ]; "v", IntType, [ Int 1 ] ],
      [ GroupBy([ "k" ], [ pAgg "k" Sum "v" ]) ]
      "468-unpivot-repeats",
      rTable [ "variable", StringType, [ Str "a" ]; "v", IntType, [ Int 1 ] ],
      [ Unpivot([ "variable" ], [ "v" ]) ]
      "468-pivot-repeats",
      rTable
          [ "i", IntType, [ Int 1 ]
            "g", StringType, [ Str "i" ]
            "w", IntType, [ Int 2 ] ],
      [ Pivot
            { Index = [ "i" ]
              On = "g"
              Values = "w"
              Agg = Sum } ]
      "unpivot-by-cells",
      rTable
          [ "k", StringType, [ Str "a" ]
            "v", IntType, [ Int 1 ]
            "w", FloatType, [ Float 2.0 ] ],
      [ Unpivot([ "k" ], [ "v"; "w" ]) ]
      "unpivot-float-beside-decimal",
      rTable
          [ "k", StringType, [ Str "a" ]
            "v", DecimalType, [ Cell.decimal "1.5" |> Option.get ]
            "w", FloatType, [ Float 2.0 ] ],
      [ Unpivot([ "k" ], [ "v"; "w" ]) ]
      "pivot-unknown-on",
      people,
      [ Pivot
            { Index = [ "g" ]
              On = "zz"
              Values = "v"
              Agg = Sum } ]
      "pivot-unknown-values",
      people,
      [ Pivot
            { Index = [ "g" ]
              On = "g"
              Values = "zz"
              Agg = Count } ]
      "pivot-unknown-index",
      people,
      [ Pivot
            { Index = [ "zz" ]
              On = "g"
              Values = "v"
              Agg = Max } ]
      "group-by-unknown-agg-source", people, [ GroupBy([ "g" ], [ pAgg "s" Sum "zz" ]) ]
      "project-unknown-source", people, [ Project [ "g", "g"; "zz", "z" ] ]
      "join-ref-undeclared-opens-walk", people, [ Join(Ref "r", [ "g", "g" ], Inner); Project [ "g", "g" ] ]
      "join-ref-missing", people, [ Join(Ref "missing", [ "g", "g" ], Inner) ]
      "pivot-then-project-over-open",
      people,
      [ Pivot
            { Index = [ "g" ]
              On = "g"
              Values = "v"
              Agg = Sum }
        Project [ "g", "g" ] ] ]

/// Every ordered pair of the generator's column types as a one-column union (Phase 476): the left
/// typed by the first, the embedded right by the second, one null row each. The 38 pairs no
/// widening relates either way are the D18 refusal the relational differential once excused.
let private rUnionPairs: (string * Table * Transform list) list =
    [ for a in colTypePool do
          for b in colTypePool ->
              sprintf "union-pair %A/%A" a b,
              rTable [ "a", a, [ Null ] ],
              [ Union(Embedded(rTable [ "a", b, [ Null ] ])) ] ]

/// The whole relational differential: the named shapes, every union type pair, then `trials`
/// generated pipelines at `seed`, each compared step by step — and, for the walk, past the first
/// refusal.
let private rDifferential (rp: ModelRel.rprims) (seed: int) (trials: int) : RelTally =
    let right, _ = pGenTable (ConfRng.ofSeed 7)
    let resolve = pResolveWith right
    let sources = fun r -> if r = "r" then Some right.Schema else None
    let mutable tally = rEmptyTally

    for label, table, p in rShapes @ rUnionPairs do
        tally <- rComparePipeline label rp resolve sources pEnv table p tally

    let mutable r = ConfRng.ofSeed seed

    for i in 1..trials do
        let table, r1 = pGenTable r
        let p, r2 = pGenPipeline r1
        r <- r2
        tally <- rComparePipeline (sprintf "trial %d" i) rp resolve sources pEnv table p tally

    tally

/// The model's twelve-verb evaluator built from its own schema and production's ROWS: the step
/// evaluator `Relational.other_of` builds, with the row function reading production's answer
/// for the rows alone. Run through the pipeline differential, it is the narrowing measured end to
/// end — the schema is the model's, and the table still has to agree byte for byte.
let private pRelationalStep
    (resolve: string -> Result<Table, EvalError>)
    (env: Map<string, Cell>)
    (_: Transform list)
    : ModelPipe.other_fn =
    let rows: ModelRel.row_fn =
        fun f t _ ->
            match DataFrame.evalPipelineWithInEnv resolve env [ pTransformOfModel t ] (pTableOfFrame f) with
            | Ok t' -> ModelPipe.Ok((pFrameOfTable t').rows)
            | Error e -> ModelPipe.Error(pErrToModel e)

    ModelRel.other_of rPrims (rResolver resolve) (pEnvToModel env) rows

/// The 412 go-red's step evaluator: `pRelationalStep` handed a `Sort` WITHOUT the literal keys the
/// frame's schema does not carry — the pre-412 evaluator, which dropped an unknown key, as the
/// model's twelve-verb parameter. A generated `Sort` names `w` after a step that projected or
/// aggregated it away, which production refuses and this evaluates.
let private pDropUnknownSortKeysStep
    (resolve: string -> Result<Table, EvalError>)
    (env: Map<string, Cell>)
    (p: Transform list)
    : ModelPipe.other_fn =
    let faithful = pRelationalStep resolve env p

    fun f t ->
        match t with
        | ModelPipe.Sort by ->
            let known = f.cols |> List.map fst

            faithful
                f
                (ModelPipe.Sort(
                    by
                    |> List.filter (fun (c, _) ->
                        match c with
                        | ModelPipe.SlotLit n -> List.contains n known
                        | ModelPipe.SlotParam _ -> true)
                ))
        | _ -> faithful f t

// ---- Phase 435 — the DENSE FRAME and its KERNELS (`proofs/Frame.fst`): the boundary in and out
//      and the comparison kernels, against `Frame.fs` and `Kernels.fs` ----
//
// WHAT THE BRIDGE SAYS. The model reads a cell as `Pipeline.fst`'s (a float as its round-trip `R`
// carrier) and a vector as the lists its arrays hold; its `host` is instantiated at production's
// own functions (`TemporalText`, `ScaledDecimal`, `Kernels.compareFloat`) and its `prims` at the
// pipeline differential's `pPrims`. Every comparison is cell by cell through `pCellToModel`.

let private fUnit (u: TimeUnit) : ModelFrame.time_unit =
    match u with
    | TimeUnit.Seconds -> ModelFrame.Seconds
    | TimeUnit.Milliseconds -> ModelFrame.Millis
    | TimeUnit.Microseconds -> ModelFrame.Micros
    | TimeUnit.Nanoseconds -> ModelFrame.Nanos

let private fUnitOf (u: ModelFrame.time_unit) : TimeUnit =
    match u with
    | ModelFrame.Seconds -> TimeUnit.Seconds
    | ModelFrame.Millis -> TimeUnit.Milliseconds
    | ModelFrame.Micros -> TimeUnit.Microseconds
    | ModelFrame.Nanos -> TimeUnit.Nanoseconds

let private fType (t: ColumnType) : ModelFrame.ftype =
    match t with
    | IntType -> ModelFrame.TInt
    | FloatType -> ModelFrame.TFloat
    | BoolType -> ModelFrame.TBool
    | StringType -> ModelFrame.TString
    | DateType -> ModelFrame.TDate
    | TimestampType u -> ModelFrame.TStamp(fUnit u)
    | DecimalType -> ModelFrame.TDecimal

let private fFloat (x: float) : string = x.ToString("R", inv)

let private fParse (s: string) : float =
    System.Double.Parse(s, System.Globalization.NumberStyles.Float, inv)

let private fOpt (o: 'a option) : FStar_Pervasives_Native.option<'a> =
    match o with
    | Some x -> FStar_Pervasives_Native.Some x
    | None -> FStar_Pervasives_Native.None

/// The model's `host`, at production's own functions.
let private fHostWith (compareFloat: float -> float -> int) : ModelFrame.host =
    { ModelFrame.host.date_text = fun d -> TemporalText.dateText (int d)
      ModelFrame.host.instant_text = fun u s f -> TemporalText.instantText (fUnitOf u) (float s) (int f)
      ModelFrame.host.try_days = fun s -> TemporalText.tryDays s |> Option.map bigint |> fOpt
      ModelFrame.host.try_instant =
        fun u s ->
            TemporalText.tryInstant (fUnitOf u) s
            |> Option.map (fun (sec, frac) -> bigint sec, bigint frac)
            |> fOpt
      ModelFrame.host.float_of_int = fun x -> fFloat (float (int x))
      ModelFrame.host.int_text = fun x -> string (int x)
      ModelFrame.host.scale_of =
        fun texts mask ->
            // On .NET a `Mask` is the `bool[]` itself; the inline `Mask.ofBools` does not inline
            // across the assembly boundary under the Release build.
            ScaledDecimal.scaleOf (List.toArray texts) (List.toArray mask)
            |> Option.map bigint
            |> fOpt
      ModelFrame.host.try_scaled =
        fun s t ->
            match ScaledDecimal.tryScaled (int s) t with
            | ValueSome k -> FStar_Pervasives_Native.Some(bigint k)
            | ValueNone -> FStar_Pervasives_Native.None
      ModelFrame.host.compare_float = fun a b -> bigint (compareFloat (fParse a) (fParse b)) }

let private fHost = fHostWith Kernels.compareFloat

let private fValidity (v: Validity) : ModelFrame.validity =
    match v with
    | AllValid -> ModelFrame.AllValid
    | Validity.Mask m -> ModelFrame.VMask(Vector.toList m)

/// The go-red's bridge: every validity read with its meaning FLIPPED — a set bit absent, and a
/// null-free column's every row absent.
let private fValidityFlipped (n: int) (v: Validity) : ModelFrame.validity =
    match v with
    | AllValid -> ModelFrame.VMask(List.replicate n false)
    | Validity.Mask m -> ModelFrame.VMask(Vector.toList m |> List.map not)

let private fColumnWith (validity: int -> Validity -> ModelFrame.validity) (c: Column) : ModelFrame.ccolumn =
    let ints (v: Vector<int>) = Vector.toList v |> List.map bigint
    let n = Column.length c

    let data =
        match c.Data with
        | ColumnData.Ints(v, va) -> ModelFrame.DInts(ints v, validity n va)
        | ColumnData.Floats(v, va) -> ModelFrame.DFloats(Vector.toList v |> List.map fFloat, validity n va)
        | ColumnData.Bools(v, va) -> ModelFrame.DBools(Vector.toList v, validity n va)
        | ColumnData.Strs(v, va) -> ModelFrame.DStrs(Vector.toList v, validity n va)
        | ColumnData.Dates(v, va) -> ModelFrame.DDates(ints v, validity n va)
        | ColumnData.Timestamps(u, s, f, va) ->
            ModelFrame.DStamps(fUnit u, Vector.toList s |> List.map bigint, f |> Option.map ints |> fOpt, validity n va)
        | ColumnData.Decimals(v, va) -> ModelFrame.DDecimals(Vector.toList v, validity n va)

    { ModelFrame.ccolumn.cname = c.Name
      ModelFrame.ccolumn.cdata = data }

let private fTableWith (validity: int -> Validity -> ModelFrame.validity) (t: Table) : ModelFrame.ctable =
    { ModelFrame.ctable.tschema = t.Schema |> List.map (fun fl -> fl.Name, fType fl.Type)
      ModelFrame.ctable.tcolumns = t.Columns |> List.map (fColumnWith validity) }

let private fTable = fTableWith (fun _ v -> fValidity v)

let private fMaskOf (m: Mask) : bool list = List.ofArray m

/// Production's vector as the model's — the arrays as the lists they hold.
let private fVec (v: Vec) : ModelFrame.vec =
    let ints (a: int[]) = a |> Array.toList |> List.map bigint

    match v with
    | Ints(a, m) -> ModelFrame.VInts(ints a, fMaskOf m)
    | Floats(a, m) -> ModelFrame.VFloats(a |> Array.toList |> List.map fFloat, fMaskOf m)
    | Bools(a, m) -> ModelFrame.VBools(fMaskOf a, fMaskOf m)
    | Strs(ty, a, m) -> ModelFrame.VStrs(fType ty, Array.toList a, fMaskOf m)
    | Decs(a, s, t, m) -> ModelFrame.VDecs(a |> Array.toList |> List.map bigint, bigint s, Array.toList t, fMaskOf m)
    | Dates(a, m) -> ModelFrame.VDates(ints a, fMaskOf m)
    | Stamps(u, s, f, m) ->
        ModelFrame.VStamps(
            fUnit u,
            s |> Array.toList |> List.map bigint,
            (if isNull f then
                 FStar_Pervasives_Native.None
             else
                 FStar_Pervasives_Native.Some(ints f)),
            fMaskOf m
        )
    | Cells a -> ModelFrame.VCells(a |> Array.toList |> List.map pCellToModel)

let private fFrame (f: Frame) : ModelFrame.dframe =
    { ModelFrame.dframe.fcols = f.Cols |> List.map (fun fl -> fl.Name, fType fl.Type)
      ModelFrame.dframe.vecs = f.Vecs |> Array.toList |> List.map fVec
      ModelFrame.dframe.sel = f.Sel |> Option.map (Array.toList >> List.map bigint) |> fOpt
      ModelFrame.dframe.count = bigint f.Count }

/// The kind of vector — the representation both sides chose for a column.
let private fKind (v: Vec) : string =
    match v with
    | Ints _ -> "ints"
    | Floats _ -> "floats"
    | Bools _ -> "bools"
    | Strs _ -> "strs"
    | Decs _ -> "decs"
    | Dates _ -> "dates"
    | Stamps(_, _, f, _) -> if isNull f then "stamps" else "stamps+fraction"
    | Cells _ -> "cells"

let private fModelKind (v: ModelFrame.vec) : string =
    match v with
    | ModelFrame.VInts _ -> "ints"
    | ModelFrame.VFloats _ -> "floats"
    | ModelFrame.VBools _ -> "bools"
    | ModelFrame.VStrs _ -> "strs"
    | ModelFrame.VDecs _ -> "decs"
    | ModelFrame.VDates _ -> "dates"
    | ModelFrame.VStamps(_, _, f, _) ->
        match f with
        | FStar_Pervasives_Native.None -> "stamps"
        | FStar_Pervasives_Native.Some _ -> "stamps+fraction"
    | ModelFrame.VCells _ -> "cells"

type private FrameTally =
    { FDiffs: string list
      FCells: int
      FKinds: Set<string>
      FViews: int
      FFallbacks: int
      FAllValid: int
      FMasked: int
      FOddMasks: int
      FTablesOut: int
      FBoxedOut: int
      FRowsRefused: int
      FKernelSome: int
      FKernelNone: int
      FKernelRows: int
      FKernelKinds: Set<string> }

let private fEmptyTally =
    { FDiffs = []
      FCells = 0
      FKinds = Set.empty
      FViews = 0
      FFallbacks = 0
      FAllValid = 0
      FMasked = 0
      FOddMasks = 0
      FTablesOut = 0
      FBoxedOut = 0
      FRowsRefused = 0
      FKernelSome = 0
      FKernelNone = 0
      FKernelRows = 0
      FKernelKinds = Set.empty }

/// A generator over one RNG, threaded through a mutable cell.
type private FDraw(seed: int) =
    let mutable r = ConfRng.ofSeed seed

    member _.Below(n: int) : int =
        let v, r' = ConfRng.intBelow n r
        r <- r'
        v

    member this.Pick(xs: 'a list) : 'a =
        List.item (this.Below(List.length xs)) xs

let private fUnits =
    [ TimeUnit.Seconds
      TimeUnit.Milliseconds
      TimeUnit.Microseconds
      TimeUnit.Nanoseconds ]

let private fTypes =
    [ IntType
      FloatType
      BoolType
      StringType
      DateType
      TimestampType TimeUnit.Seconds
      TimestampType TimeUnit.Milliseconds
      TimestampType TimeUnit.Nanoseconds
      DecimalType ]

/// A decimal text: mostly at a scale the carrier holds, now and then too wide for it (the column
/// then takes the text path, `Cells`).
let private fDecimalText (d: FDraw) : string =
    let whole = d.Below 2000 - 1000

    match d.Below 6 with
    | 0 -> string whole
    | 1 -> sprintf "%d.%d" whole (d.Below 10)
    | 2 -> sprintf "%d.%02d" whole (d.Below 100)
    | 3 -> sprintf "%d.125" whole
    | 4 ->
        if d.Below 3 = 0 then
            "1234567890123456.5"
        else
            sprintf "%d.5" whole
    | _ -> sprintf "-0.%03d" (d.Below 1000 + 1)
    |> fun s ->
        Cell.decimal s
        |> Option.map (fun c ->
            match c with
            | Cell.Decimal t -> t
            | _ -> s)
        |> Option.defaultValue s

/// A Core column of `ty` and `n` rows: null-free (`AllValid`) a third of the time, else a mask with
/// absent rows — now and then a mask of another length than the values, which a Core column can
/// hold and the frame's `maskOf` reads padded absent or cut.
let private fGenColumn (d: FDraw) (name: string) (ty: ColumnType) (n: int) : Column =
    let validity =
        match d.Below 3 with
        | 0 -> AllValid
        | _ ->
            let len =
                match d.Below 8 with
                | 0 -> max 0 (n - 1)
                | 1 -> n + 1
                | _ -> n

            let bits = Array.init len (fun _ -> d.Below 4 <> 0)

            if Array.forall id bits && len = n then
                AllValid
            else
                Validity.Mask(Vector.ofArray bits)

    match ty with
    | IntType -> Column.ofInts name (Vector.ofArray (Array.init n (fun _ -> d.Below 200 - 100))) validity
    | FloatType ->
        Column.ofFloats
            name
            (Vector.ofArray (
                Array.init n (fun _ ->
                    match d.Below 12 with
                    | 0 -> -0.0
                    | 1 -> 1e300
                    | _ -> float (d.Below 400 - 200) / 4.0)
            ))
            validity
    | BoolType -> Column.ofBools name (Vector.ofArray (Array.init n (fun _ -> d.Below 2 = 0))) validity
    | StringType -> Column.ofStrs name (Vector.ofArray (Array.init n (fun _ -> d.Pick [ "a"; "b"; ""; "zz" ]))) validity
    | DateType -> Column.ofDates name (Vector.ofArray (Array.init n (fun _ -> d.Below 40000 - 5000))) validity
    | TimestampType u ->
        let seconds = Array.init n (fun _ -> float (d.Below 2000000000))
        let scale = TimeUnit.scale u

        let fraction =
            if u = TimeUnit.Seconds then
                None
            else
                Some(Vector.ofArray (Array.init n (fun _ -> if d.Below 3 = 0 then 0 else d.Below scale)))

        Column.ofTimestamps name u (Vector.ofArray seconds) fraction validity
    | DecimalType -> Column.ofDecimals name (Vector.ofArray (Array.init n (fun _ -> fDecimalText d))) validity

/// A table: one field per drawn type, its column usually of the field's type and the table's
/// length, and now and then of another type (an int column under a float field, …), of another
/// length, or missing — the fall-back the frame packs from the cells.
let private fGenTable (d: FDraw) : Table =
    let n = d.Below 7
    let width = 1 + d.Below 5

    let fields =
        [ for j in 0 .. width - 1 ->
              let ty = d.Pick fTypes
              Field.create (sprintf "c%d" j) ty ]

    let columns =
        fields
        |> List.choose (fun fl ->
            match d.Below 10 with
            | 0 -> None
            | 1 ->
                let other =
                    match fl.Type with
                    | FloatType -> IntType
                    | DecimalType -> IntType
                    | IntType -> StringType
                    | _ -> IntType

                Some(fGenColumn d fl.Name other n)
            | 2 -> Some(fGenColumn d fl.Name fl.Type (n + 1))
            | _ -> Some(fGenColumn d fl.Name fl.Type n))

    // `Table.rowCount` reads the FIRST column, so a first column of another length moves the frame's
    // length for every other column — drawn too, at the rate it comes.
    { Schema = fields; Columns = columns }

let private fCellText (c: ModelPipe.cell) : string = sprintf "%A" c

/// Production's read-back of `f` and the model's of its image, cell by cell.
let private fCompareRows (label: string) (prod: Cell[][]) (model: ModelPipe.cell list list) (acc: FrameTally) =
    if prod.Length <> List.length model then
        { acc with
            FDiffs =
                sprintf "%s: %d rows in production, %d in the model" label prod.Length (List.length model)
                :: acc.FDiffs }
    else
        let mutable a = acc

        List.iteri
            (fun i (mr: ModelPipe.cell list) ->
                let pr = prod[i] |> Array.toList |> List.map pCellToModel

                if pr <> mr then
                    a <-
                        { a with
                            FDiffs = sprintf "%s: row %d production %A model %A" label i pr mr :: a.FDiffs }
                else
                    a <-
                        { a with
                            FCells = a.FCells + List.length pr })
            model

        a

/// The TABLE DOOR: `Frame.ofTable` against `of_table`, the vector each side chose per column, every
/// vector read at every physical row through `Vec.cellAt` against `cell_at` over production's own
/// vector, and — for a column of its field's type and the table's length — against Core's
/// `Column.toCells` (the theorem `of_table_reads_columns` states: the frame reads the table's rows).
let private fTableDoor (bridge: Table -> ModelFrame.ctable) (label: string) (t: Table) (acc: FrameTally) =
    let f = Frame.ofTable t
    let mf = ModelFrame.of_table fHost (bridge t)

    let mutable a =
        fCompareRows (label + " ofTable") (Frame.rowsOf f) (ModelFrame.rows_of fHost mf) acc

    let n = Table.rowCount t

    t.Schema
    |> List.iteri (fun j fl ->
        let pv = f.Vecs[j]
        let mv = List.item j mf.vecs

        a <-
            { a with
                FKinds = Set.add (fKind pv) a.FKinds }

        if fKind pv <> fModelKind mv then
            a <-
                { a with
                    FDiffs =
                        sprintf
                            "%s: column %s is %s in production, %s in the model"
                            label
                            fl.Name
                            (fKind pv)
                            (fModelKind mv)
                        :: a.FDiffs }

        for p in 0 .. Vec.length pv - 1 do
            let pc = pCellToModel (Vec.cellAt pv p)
            let mc = ModelFrame.cell_at fHost (fVec pv) (bigint p)

            if pc <> mc then
                a <-
                    { a with
                        FDiffs =
                            sprintf "%s: cellAt %s[%d] production %A model %A" label fl.Name p pc mc
                            :: a.FDiffs }

        match Table.tryColumn fl.Name t with
        | Some c when c.Type = fl.Type && Column.length c = n ->
            let core = Column.toCells c |> List.map pCellToModel
            let read = [ for i in 0 .. n - 1 -> pCellToModel (Vec.cellAt pv i) ]

            if core <> read then
                a <-
                    { a with
                        FDiffs =
                            sprintf "%s: column %s reads %A, Core's toCells %A" label fl.Name read core
                            :: a.FDiffs }

            match c.Data.Validity with
            | AllValid ->
                a <-
                    { a with
                        FViews = a.FViews + 1
                        FAllValid = a.FAllValid + 1 }
            | Validity.Mask m ->
                a <-
                    { a with
                        FViews = a.FViews + 1
                        FMasked = a.FMasked + 1
                        FOddMasks = a.FOddMasks + (if m.Length <> n then 1 else 0) }
        | _ -> a <- { a with FFallbacks = a.FFallbacks + 1 })

    a

/// A cell for a column of `ty` as the ROW door hands one in: usually one the type carries, now and
/// then an int in a float or decimal column, a temporal text at a coarser unit or not canonical, or
/// a cell of another kind (the column then stays boxed).
let private fGenRowCell (d: FDraw) (ty: ColumnType) : Cell =
    match d.Below 14 with
    | 0 -> Null
    | 1 -> d.Pick [ Str "x"; Int 3; Bool true; Cell.Date "2020-01-01" ]
    | _ ->
        match ty with
        | IntType -> Int(d.Below 100 - 50)
        | FloatType ->
            if d.Below 4 = 0 then
                Int(d.Below 9)
            else
                Float(float (d.Below 100) / 8.0)
        | BoolType -> Bool(d.Below 2 = 0)
        | StringType -> Str(d.Pick [ "a"; "b"; "" ])
        | DateType ->
            Cell.Date(
                if d.Below 9 = 0 then
                    "2020-1-1"
                else
                    TemporalText.dateText (d.Below 30000)
            )
        | TimestampType u ->
            let s = float (d.Below 1000000000)

            match d.Below 4 with
            | 0 -> Cell.Timestamp(TemporalText.instantText TimeUnit.Seconds s 0)
            | 1 -> Cell.Timestamp "2020-01-01T00:00:00.5Z"
            | _ -> Cell.Timestamp(TemporalText.instantText u s (d.Below(TimeUnit.scale u)))
        | DecimalType ->
            if d.Below 5 = 0 then
                Int(d.Below 50)
            else
                Cell.Decimal(fDecimalText d)

let private fRowsOf (rows: Cell[][]) : ModelPipe.cell list list =
    rows |> Array.toList |> List.map (Array.toList >> List.map pCellToModel)

/// The ROW door and the boundary OUT: `Frame.ofRows` against `of_rows` (and, under a drawn selection,
/// `Frame.toTable` against `to_table` — and both against the read-back, `to_table_reads`), and
/// `DataFrame.prepareRows` / `DataFrame.toTable` against `prepare_rows` / `prepared_table`, a row of
/// the wrong width included.
let private fRowDoor (d: FDraw) (label: string) (acc: FrameTally) =
    let width = 1 + d.Below 4

    let schema =
        [ for j in 0 .. width - 1 -> Field.create (sprintf "r%d" j) (d.Pick fTypes) ]

    let n = d.Below 6

    let rows =
        Array.init n (fun _ -> schema |> List.map (fun fl -> fGenRowCell d fl.Type) |> List.toArray)

    let mcols = schema |> List.map (fun fl -> fl.Name, fType fl.Type)
    let mrows = fRowsOf rows
    let f = Frame.ofRows schema rows
    let mf = ModelFrame.of_rows fHost mcols mrows

    let mutable a =
        fCompareRows (label + " ofRows") (Frame.rowsOf f) (ModelFrame.rows_of fHost mf) acc

    for j in 0 .. width - 1 do
        a <-
            { a with
                FKinds = Set.add (fKind f.Vecs[j]) a.FKinds }

        if fKind f.Vecs[j] <> fModelKind (List.item j mf.vecs) then
            a <-
                { a with
                    FDiffs = sprintf "%s: row-door column %d kinds differ" label j :: a.FDiffs }

    // The boundary out, through a drawn selection (repeats and any order allowed).
    let sel =
        if n = 0 || d.Below 3 = 0 then
            None
        else
            Some(Array.init (d.Below 6) (fun _ -> d.Below n))

    let fs =
        match sel with
        | Some s -> Frame.select f s
        | None -> f

    let mfs = fFrame fs
    let readBack = ModelFrame.rows_of fHost mfs

    a <- fCompareRows (label + " select") (Frame.rowsOf fs) readBack a

    match ModelFrame.to_table mfs with
    | FStar_Pervasives_Native.None -> a <- { a with FBoxedOut = a.FBoxedOut + 1 }
    | FStar_Pervasives_Native.Some mt ->
        let pt = Frame.toTable fs
        a <- { a with FTablesOut = a.FTablesOut + 1 }

        List.iteri
            (fun j (mc: ModelFrame.ccolumn) ->
                let core = Column.toCells (List.item j pt.Columns) |> List.map pCellToModel
                let model = ModelFrame.core_cells fHost mc
                let fromRows = readBack |> List.map (List.item j)

                if core <> model || model <> fromRows then
                    a <-
                        { a with
                            FDiffs =
                                sprintf
                                    "%s: toTable column %d production %A model %A read-back %A"
                                    label
                                    j
                                    core
                                    model
                                    fromRows
                                :: a.FDiffs })
            mt.tcolumns

    // The prepared source: a refused width, or the frame and the table it stands for.
    let handed =
        if n > 0 && d.Below 4 = 0 then
            Array.append rows [| Array.create (width + 1) Null |]
        else
            rows

    match DataFrame.prepareRows schema handed, ModelFrame.prepare_rows fHost mcols (fRowsOf handed) with
    | Error e, ModelPipe.Error me ->
        a <-
            { a with
                FRowsRefused = a.FRowsRefused + 1 }

        if pErrToModel e <> me then
            a <-
                { a with
                    FDiffs = sprintf "%s: prepareRows refused %A, model %A" label e me :: a.FDiffs }
    | Ok p, ModelPipe.Ok mp ->
        match ModelFrame.prepared_table mp with
        | FStar_Pervasives_Native.Some mt ->
            let pt = DataFrame.toTable p

            let prodCells = pt.Columns |> List.map (Column.toCells >> List.map pCellToModel)

            let modelCells = mt.tcolumns |> List.map (ModelFrame.core_cells fHost)

            if prodCells <> modelCells then
                a <-
                    { a with
                        FDiffs =
                            sprintf "%s: prepared toTable %A model %A" label prodCells modelCells
                            :: a.FDiffs }
        | FStar_Pervasives_Native.None -> ()
    | pr, mr ->
        a <-
            { a with
                FDiffs = sprintf "%s: prepareRows %A, model %A" label pr mr :: a.FDiffs }

    a

let private fCmpOps = [ Lt; Le; Gt; Ge; Eq; Ne ]

/// A filter predicate over `t`'s columns: `And` / `Or` over comparisons of a column with a constant
/// (an int, a float, a decimal, a null, a bound param) on either side — the shapes `filterBits`
/// answers — and now and then one it declines (two columns, an arithmetic operator, an unknown
/// column), which the compiled path answers.
let rec private fGenPred (d: FDraw) (depth: int) (t: Table) : ColExpr =
    let cols = t.Schema |> List.map (fun fl -> fl.Name)

    // The numeric columns, where the kernels live, drawn three times in four when there are any.
    let numeric =
        t.Schema
        |> List.filter (fun fl -> fl.Type = IntType || fl.Type = FloatType || fl.Type = DecimalType)
        |> List.map (fun fl -> fl.Name)

    let leafCols =
        if List.isEmpty numeric || d.Below 4 = 0 then
            cols
        else
            numeric

    match (if depth = 0 then 2 + d.Below 8 else d.Below 10) with
    | 0 -> Binary(And, fGenPred d (depth - 1) t, fGenPred d (depth - 1) t)
    | 1 -> Binary(Or, fGenPred d (depth - 1) t, fGenPred d (depth - 1) t)
    | 9 ->
        d.Pick
            [ Binary(Lt, Col(d.Pick cols), Col(d.Pick cols))
              Binary(Add, Col(d.Pick cols), Lit(Int 1))
              Binary(Gt, Col "nope", Lit(Int 0)) ]
    | _ ->
        let constant =
            match d.Below 7 with
            | 0 -> Lit(Int(d.Below 200 - 100))
            | 1 -> Lit(Float(float (d.Below 400 - 200) / 4.0))
            | 2 -> Lit(Cell.Decimal(fDecimalText d))
            | 3 -> Lit Null
            | 4 -> Param "k"
            | 5 -> Lit(Float -0.0)
            | _ -> Lit(Int(d.Below 20))

        let column = Col(d.Pick leafCols)
        let op = d.Pick fCmpOps

        if d.Below 2 = 0 then
            Binary(op, column, constant)
        else
            Binary(op, constant, column)

let private fEnv: Map<string, Cell> = Map.ofList [ "k", Int 7 ]

let private fBits (k: KernelSet) (f: Frame) (bits: uint32[]) : bool list =
    ignore k
    [ for p in 0 .. f.Count - 1 -> Kernels.isSet bits p ]

/// The KERNELS: `filterBits` (portable and host members) against `filter_bits`, read through
/// `Kernels.isSet`; `selection` against `selection`; and the filter step through the host kernels
/// against `filter_kernel` where the kernels answer — and, there, against `Pipeline.fst`'s row-major
/// `filter_rows` over the model's read-back at production's primitives: the kernel theorem's
/// conclusion (`filter_kernel_is_step`) at the instantiation its premise names.
let private fKernels (host: ModelFrame.host) (d: FDraw) (label: string) (t: Table) (acc: FrameTally) =
    let f0 = Frame.ofTable t

    let f =
        if f0.Count > 0 && d.Below 3 = 0 then
            Frame.select f0 (Array.init (d.Below 8) (fun _ -> d.Below f0.Count))
        else
            f0

    let mf = fFrame f
    let menv = pEnvToModel fEnv
    let pred = fGenPred d 2 t
    let mpred = pExprToModel pred
    let resolved = DataFrame.resolveExpr fEnv f.Cols pred
    let mutable a = acc

    let modelBits = ModelFrame.filter_bits host menv mf mpred

    for k in [ Kernels.portable; Kernels.host ] do
        match DataFrame.filterBits k f resolved, modelBits with
        | None, FStar_Pervasives_Native.None -> ()
        | Some bits, FStar_Pervasives_Native.Some mb ->
            if fBits k f bits <> mb then
                a <-
                    { a with
                        FDiffs =
                            sprintf "%s: %A bits production %A model %A" label pred (fBits k f bits) mb
                            :: a.FDiffs }

            let ps = k.Selection bits |> Array.toList |> List.map bigint

            if ps <> ModelFrame.selection mb then
                a <-
                    { a with
                        FDiffs =
                            sprintf "%s: %A selection production %A model %A" label pred ps (ModelFrame.selection mb)
                            :: a.FDiffs }
        | pb, _ ->
            a <-
                { a with
                    FDiffs =
                        sprintf "%s: %A filterBits production %A model %A" label pred (Option.isSome pb) modelBits
                        :: a.FDiffs }

    match ModelFrame.filter_kernel host menv mf mpred with
    | FStar_Pervasives_Native.None ->
        a <-
            { a with
                FKernelNone = a.FKernelNone + 1 }
    | FStar_Pervasives_Native.Some mf' ->
        // The vectors the answered predicate's leaves read.
        let rec reads (e: ColExpr) : string list =
            match e with
            | Binary(_, x, y) -> reads x @ reads y
            | Col name -> [ name ]
            | _ -> []

        let kinds =
            reads pred
            |> List.choose (fun name -> f.Cols |> List.tryFindIndex (fun fl -> fl.Name = name))
            |> List.map (fun j -> fKind f.Vecs[j])

        a <-
            { a with
                FKernelSome = a.FKernelSome + 1
                FKernelKinds = Set.union a.FKernelKinds (Set.ofList kinds) }

        let kernelRows = ModelFrame.rows_of host mf'

        match DataFrame.evalStepWith Kernels.host DataFrame.noResolve fEnv f (Filter pred) with
        | Ok f' ->
            a <- fCompareRows (sprintf "%s filter %A" label pred) (Frame.rowsOf f') kernelRows a

            a <-
                { a with
                    FKernelRows = a.FKernelRows + List.length kernelRows }
        | Error e ->
            a <-
                { a with
                    FDiffs =
                        sprintf "%s: %A production refused %A, the kernel did not" label pred e
                        :: a.FDiffs }

        let schema = ModelFrame.erase_schema mf.fcols

        match ModelPipe.filter_rows pPrims menv schema (ModelFrame.rows_of host mf) mpred with
        | ModelPipe.Ok rows when rows = kernelRows -> ()
        | other ->
            a <-
                { a with
                    FDiffs =
                        sprintf "%s: %A Pipeline's filter %A, the kernel's rows %A" label pred other kernelRows
                        :: a.FDiffs }

    a

let private frameDifferential (bridge: Table -> ModelFrame.ctable) (host: ModelFrame.host) (seed: int) (trials: int) =
    let d = FDraw seed
    let mutable a = fEmptyTally

    for i in 1..trials do
        let t = fGenTable d
        a <- fTableDoor bridge (sprintf "trial %d" i) t a
        a <- fRowDoor d (sprintf "trial %d" i) a

        for _ in 1..3 do
            a <- fKernels host d (sprintf "trial %d" i) t a

    a

/// Phase 471 — a table's rows as the array rows `compareResolved` reads.
let private sRowsOf (t: Table) : Cell[] list =
    let cols = t.Columns |> List.map Column.toCells

    [ for i in 0 .. Table.rowCount t - 1 -> cols |> List.map (fun c -> c[i]) |> List.toArray ]

[<Tests>]
let proofOracleTests =
    testList
        "Proofs.Oracle"
        [

          // ---- Phase 176 — the COLUMNAR op algebra: apply, canApply, invert, applyAll and toOps
          //      against the model ----

          testCase
              "the columnar oracle agrees with ColumnOps.apply, canApply and invert over generated tables and scripts"
          <| fun _ ->
              let t = colDifferential cellToModel 1760 60

              match t.Diffs with
              | d :: _ -> failtestf "the columnar oracle and production DISAGREE\n%s" d
              | [] ->
                  // Adequacy, per shape. Measured at 60 trials (360 probes): accepted 154,
                  // rejected 206, inverted 88, roundTripped 71, wfPre 278, wfPreserved 85, and 8
                  // of 17 round trips outside well-formedness failed. Each threshold sits below
                  // its measurement with room; they catch a generator that stops reaching a
                  // shape, not pin the numbers.
                  Expect.isGreaterThan t.Accepted 100 (sprintf "ops were accepted (accepted=%d)" t.Accepted)
                  Expect.isGreaterThan t.Rejected 100 (sprintf "ops were refused (rejected=%d)" t.Rejected)

                  Expect.isGreaterThan
                      t.Inverted
                      40
                      (sprintf "accepted invertible ops were inverted (inverted=%d)" t.Inverted)

                  Expect.isGreaterThan
                      t.RoundTripped
                      20
                      (sprintf "round trips were ASSERTED on well-formed pre-states (roundTripped=%d)" t.RoundTripped)

                  Expect.isGreaterThan
                      t.WfPre
                      60
                      (sprintf "the generator produced well-formed pre-states (wfPre=%d)" t.WfPre)

                  Expect.isGreaterThan
                      t.WfPreserved
                      20
                      (sprintf "the invariant was asserted on accepted structural ops (wfPreserved=%d)" t.WfPreserved)

                  Expect.equal t.Scripts 60 "every script was compared whole"

                  for cls in
                      [ "NoSuchColumn"
                        "DuplicateColumn"
                        "RowOutOfRange"
                        "CellTypeMismatch"
                        "ColumnLengthMismatch"
                        "RowShapeUnknownColumn"
                        "TransformRejected"
                        "NotInvertible" ] do
                      Expect.isTrue
                          (Set.contains cls t.Classes)
                          (sprintf "the sample reached a %s rejection (reached: %A)" cls t.Classes)

                  // The hypothesis earns its place: outside well-formedness the round trip is
                  // only counted, and the count of failures there must be NON-ZERO — otherwise
                  // `wf` is a hypothesis the theorem does not need and the ladder overstates.
                  Expect.isGreaterThan
                      t.RoundTripsOutsideWf
                      0
                      (sprintf "round trips were attempted outside wf (%d)" t.RoundTripsOutsideWf)

                  Expect.isGreaterThan
                      t.RoundTripFailuresOutsideWf
                      0
                      (sprintf
                          "some round trip FAILED on a malformed pre-state (%d of %d) — the well-formedness hypothesis is load-bearing"
                          t.RoundTripFailuresOutsideWf
                          t.RoundTripsOutsideWf)

                  // seeded, replayable
                  Expect.equal (colDifferential cellToModel 1760 60) t "same seed => identical tally"

          testCase
              "the columnar oracle agrees with ColumnOps.toOps and applyAll, and the diff reconstructs on well-formed pairs"
          <| fun _ ->
              let t = colDiffDifferential 1761 120

              match t.DDiffs with
              | d :: _ -> failtestf "the columnar diff oracle and production DISAGREE\n%s" d
              | [] ->
                  // Measured at 120 pairs: bothWf 67, granular 44, rebuild 23.
                  Expect.equal t.Pairs 120 "every pair was compared"
                  Expect.isGreaterThan t.BothWf 40 (sprintf "well-formed pairs were reached (bothWf=%d)" t.BothWf)

                  Expect.isGreaterThan
                      t.Granular
                      10
                      (sprintf "the column-granular branch was asserted (granular=%d)" t.Granular)

                  Expect.isGreaterThan t.Rebuild 10 (sprintf "the rebuild branch was asserted (rebuild=%d)" t.Rebuild)

          testCase
              "a columnar oracle handed a BLIND cell bridge DISAGREES with ColumnOps.apply — the measurement can fail"
          <| fun _ ->
              // The teeth. Under the blind bridge every present cell reaches the model as a
              // string, so the model's type check refuses what production accepts (an `Int` into
              // an int column) and accepts what production refuses (a `Str` into one). If this
              // ever passes, the type clauses have stopped reaching the comparison and the green
              // run above certifies nothing about them.
              let t = colDifferential blindCellToModel 1760 20

              Expect.isNonEmpty t.Diffs "a blind cell bridge MUST disagree with production"

              Expect.isTrue
                  (t.Diffs |> List.exists (fun d -> d.Contains "CellTypeMismatch"))
                  "and the disagreement is about the TYPE check, which is what the bridge blinded"

          testCase "a REFUSED insert has no inverse — `refused_insert_inverse_is_live` is now about `invert_pre181`"
          <| fun _ ->
              // Phase 176's finding, CLOSED by Phase 181, with the negative kept pinned. The
              // shipped `invert` refuses the refused insert; the pre-181 clause the model still
              // carries as `invert_pre181` answers a remove that SUCCEEDS at the pre-state and
              // takes the column that was already there. Both halves are asserted, so this goes
              // red either if the guard is reverted OR if the finding it closed stops being what
              // the closed finding was.
              let t: Table =
                  { Schema = [ Field.create "a" IntType; Field.create "b" IntType ]
                    Columns =
                      [ KitColumn.create "a" IntType [ Int 1; Int 2 ]
                        KitColumn.create "b" IntType [ Int 3; Int 4 ] ] }

              let op = InsertColumn(0, KitColumn.create "a" IntType [ Int 9; Int 9 ])
              Expect.equal (ColumnOps.apply op t) (Error(DuplicateColumn "a")) "the insert is refused as a duplicate"

              match ColumnOps.invert op t with
              | Ok inv ->
                  failtestf "invert answered %s for a REFUSED insert — Phase 181's guard is gone" (prodColOpRender inv)
              | Error e ->
                  Expect.equal
                      (prodColRejRender e)
                      (prodColRejRender (DuplicateColumn "a"))
                      "and the refusal is the one `apply` gave, not a blanket NotInvertible"

              // the model agrees, on the shipped clause and on the pinned negative beside it
              let mt = tableToModel t
              let mop = colOpToModelWith cellToModel op

              Expect.equal
                  (ModelCol.apply modelEvaluator mop mt)
                  (ModelCol.Error(ModelCol.DuplicateColumn "a"))
                  "the model refuses the same insert"

              Expect.equal
                  (ModelCol.invert modelEvaluator mop mt)
                  (ModelCol.Error(ModelCol.DuplicateColumn "a"))
                  "and its guarded invert refuses it too"

              Expect.equal
                  (ModelCol.invert_pre181 mop mt)
                  (ModelCol.Ok(ModelCol.RemoveColumn "a"))
                  "while the pre-181 clause still derives the live remove — the finding, kept"

              // and that remove really was live: the column that was already there goes
              match ColumnOps.apply (RemoveColumn "a") t with
              | Error e -> failtestf "the pre-181 inverse's remove was refused (%s)" (prodColRejRender e)
              | Ok after ->
                  Expect.equal
                      (Table.columnNames after)
                      [ "b" ]
                      "the pre-existing column `a` would have GONE — what the guard now prevents"


          // ---- Phase 429: the unit check (`proofs/Pipeline.fst` section 13) ----

          testCase
              "the unit check agrees with the model's resolve_units on the unit, the resolved expression and the refusal over the unit vectors and a generated sample, and resolution is the whole of a conversion's semantics"
          <| fun _ ->
              let t = unitDifferential 429 600
              Expect.isEmpty t.UDiffs (sprintf "disagreements:\n%s" (String.concat "\n" (List.rev t.UDiffs)))

              Expect.isGreaterThan
                  t.UAccepted
                  150
                  (sprintf "expressions accepted on both sides (accepted=%d)" t.UAccepted)

              Expect.isGreaterThan t.URefused 40 (sprintf "expressions refused on both sides (refused=%d)" t.URefused)

              Expect.isGreaterThan
                  t.UWithUnit
                  80
                  (sprintf "accepted expressions that carried a unit (withUnit=%d)" t.UWithUnit)

              Expect.isGreaterThan
                  t.UConverts
                  100
                  (sprintf "expressions carrying a conversion (converts=%d)" t.UConverts)

              Expect.isGreaterThan
                  t.UConvertRefused
                  5
                  (sprintf "conversions refused across dimensions (convertRefused=%d)" t.UConvertRefused)

              printfn
                  "unit differential: compared=%d accepted=%d refused=%d withUnit=%d converts=%d convertRefused=%d"
                  t.UCompared
                  t.UAccepted
                  t.URefused
                  t.UWithUnit
                  t.UConverts
                  t.UConvertRefused

          testCase "a unitless schema leaves every convert-free expression unchanged, in production as in the model"
          <| fun _ ->
              let mutable rng = ConfRng.ofSeed 4291
              let plain = pUnitSchema |> List.map Field.withoutUnit

              for i in 1..200 do
                  let e, r1 = pGenExpr 3 rng
                  rng <- r1

                  Expect.equal
                      (DataFrame.resolveUnits "filter" plain e)
                      (Ok(None, e))
                      (sprintf "production, %d: %A" i e)

                  Expect.equal
                      (pModelUnitResult (
                          ModelPipe.resolve_units pUnitAlg "filter" (pUnitsOfSchema plain) (pExprToModel e)
                      ))
                      (Ok(None, e))
                      (sprintf "the model, %d: %A" i e)

          // ---- Phase 154: the counted pipeline driver (`proofs/Pipeline.fst`) ----

          testCase
              "the pipeline oracle agrees with DataFrame.evalPipelineWithInEnvCounted on table and count over the transform-laws vectors and a generated sample that reaches every verb and every expression kind"
          <| fun _ ->
              let t = pipelineDifferential pPipelineToModel pFaithfulStep 154 400

              Expect.isEmpty t.PDiffs (sprintf "disagreements:\n%s" (String.concat "\n" (List.rev t.PDiffs)))

              Expect.equal
                  t.PCompared
                  (List.length (pPipelineVectors ()) + 400)
                  "every unit-free vector and four hundred generated pipelines were compared"

              Expect.isGreaterThan t.POk 120 (sprintf "pipelines evaluated to a table on both sides (ok=%d)" t.POk)
              Expect.isGreaterThan t.PErr 60 (sprintf "pipelines were refused on both sides (err=%d)" t.PErr)

              Expect.isGreaterThan
                  t.PCounted
                  60
                  (sprintf
                      "pipelines evaluated with a NONZERO count, so the count half was reached (counted=%d)"
                      t.PCounted)

              Expect.isGreaterThan
                  t.PCountTotal
                  200
                  (sprintf "row evaluations were compared in total (total=%d)" t.PCountTotal)

              printfn
                  "pipeline differential: compared=%d ok=%d err=%d counted=%d rowEvaluations=%d roundTrips=%d verbs=%d exprKinds=%d"
                  t.PCompared
                  t.POk
                  t.PErr
                  t.PCounted
                  t.PCountTotal
                  t.PRoundTrips
                  t.PVerbs.Count
                  t.PExprs.Count

              Expect.equal
                  t.PRoundTrips
                  t.PCompared
                  ("every pipeline crossed to the model and back unchanged — the two closed alphabets are the same; lost: "
                   + String.concat "; " (List.truncate 5 t.PLossy))

              for verb in
                  [ "Filter"
                    "Project"
                    "Derive"
                    "GroupBy"
                    "Join"
                    "Window"
                    "Pivot"
                    "Unpivot"
                    "Sort"
                    "Distinct"
                    "Limit"
                    "Union"
                    "Intersect"
                    "Except" ] do
                  Expect.isTrue
                      (Set.contains verb t.PVerbs)
                      (sprintf "the sample reached the %s verb (reached: %A)" verb t.PVerbs)

              for kind in
                  [ "Col"
                    "Lit"
                    "Param"
                    "Binary"
                    "Not"
                    "Coalesce"
                    "Case"
                    "Cast"
                    "ApplyFn"
                    "InList"
                    "IsNull"
                    "InParam"
                    "Now"
                    "Quotient"
                    "Rounded" ] do
                  Expect.isTrue
                      (Set.contains kind t.PExprs)
                      (sprintf "the sample reached the %s expression kind (reached: %A)" kind t.PExprs)

              Expect.equal
                  (pipelineDifferential pPipelineToModel pFaithfulStep 154 400)
                  t
                  "same seed => identical tally"

          testCase
              "a pipeline oracle whose count skips one step kind DISAGREES with DataFrame.evalPipelineWithInEnvCounted on the count and on nothing else — the measurement can fail"
          <| fun _ ->
              // The teeth. Behind the lock-step evaluator production's own Derive runs on both sides,
              // so every table agrees; the bridge crosses that Derive to the model as a Distinct, which
              // `costOf` charges nothing — a model whose count skips one step kind. If this ever
              // passes, the count comparison has stopped reaching the clause and the green run above
              // certifies nothing about it.
              let forgetful =
                  pipelineDifferential
                      (pPipelineToModelWith (function
                          | Derive _ -> Distinct
                          | other -> other))
                      pStepLockstep
                      154
                      400

              Expect.isNonEmpty forgetful.PDiffs "a model that does not charge a Derive MUST disagree with production"

              printfn
                  "pipeline go-red: the forgetful model disagreed on %d of %d pipelines"
                  (List.length forgetful.PDiffs)
                  forgetful.PCompared

              Expect.isTrue
                  (forgetful.PDiffs |> List.forall (fun d -> d.Contains "count differs"))
                  (sprintf
                      "and every disagreement is about the count — the tables agree, because production's steps ran on both sides:\n%s"
                      (String.concat "\n" (forgetful.PDiffs |> List.filter (fun d -> not (d.Contains "count differs")))))

              // And the TABLE half can lose too: under the faithful step evaluator a bridge that
              // negates every Filter's predicate hands the model a different pipeline, and the byte
              // comparison must see it.
              let negated =
                  pipelineDifferential
                      (pPipelineToModelWith (function
                          | Filter e -> Filter(Not e)
                          | other -> other))
                      pFaithfulStep
                      154
                      120

              Expect.isNonEmpty negated.PDiffs "a bridge that negates every Filter MUST disagree with production"

              printfn
                  "pipeline go-red: the negated bridge disagreed on %d of %d pipelines"
                  (List.length negated.PDiffs)
                  negated.PCompared

              Expect.isTrue
                  (negated.PDiffs |> List.exists (fun d -> d.Contains "table differs"))
                  "and at least one disagreement is about the table"

          testCase
              "`uncounted_is_projection` on the shipped evaluator — evalPipelineWithInEnv is the counted path projected, over the vectors and the sample"
          <| fun _ ->
              // The identity the phase was chartered to prove as an agreement between two paths,
              // pinned on production so that a second path — a counter, a check, a refusal added to
              // one entry point and not the other — turns this red.
              let mutable checked = 0

              let assertProjection resolve env (p: Transform list) (table: Table) label =
                  let counted = DataFrame.evalPipelineWithInEnvCounted resolve env p table
                  let uncounted = DataFrame.evalPipelineWithInEnv resolve env p table

                  Expect.equal
                      uncounted
                      (counted |> Result.map fst)
                      (sprintf "%s: the uncounted entry point is the counted one projected" label)

                  // and the model's reading of the same identity, over the same input
                  let other = pStepOfProduction resolve env
                  let menv = pEnvToModel env
                  let mp = pPipelineToModel p
                  let mf = pFrameOfTable table

                  Expect.equal
                      (ModelPipe.eval_uncounted pPrims other menv mp mf)
                      (ModelPipe.result_map fst (ModelPipe.eval_counted pPrims other menv mp mf))
                      (sprintf "%s: and the model says the same" label)

                  checked <- checked + 1

              for id, p, table, _ in pLawVectors () do
                  assertProjection DataFrame.noResolve Map.empty p table id

              let mutable rng = ConfRng.ofSeed 1540

              for i in 1..200 do
                  let table, r1 = pGenTable rng
                  let right, r2 = pGenTable r1
                  let p, r3 = pGenPipeline r2
                  assertProjection (pResolveWith right) pEnv p table (sprintf "generated %d" i)
                  rng <- r3

              Expect.equal
                  checked
                  (LawVectorExport.iterations + 200)
                  "every vector and two hundred generated pipelines were checked"

          testCase
              "the finding holds on the shipped evaluator — an expression over `Limits.max_expr_nodes` is evaluated, never refused"
          <| fun _ ->
              // THE FINDING, pinned on production so that an enforcement of §21.8 turns this red and
              // sends its author to the ladder row (`pipeline-limit-unenforced`) and the README's
              // theorem 14 section, where the decision it needs is recorded as open.
              let table, _ = pGenTable (ConfRng.ofSeed 512)
              let rows = Table.rowCount table
              let over = pExprOfNodes 513
              let within = pExprOfNodes 511

              Expect.equal
                  (ModelPipe.expr_nodes (pExprToModel over))
                  (bigint 513)
                  "the model counts 513 nodes in the expression"

              Expect.equal (ModelPipe.expr_nodes (pExprToModel within)) (bigint 511) "and 511 in its neighbour"

              Expect.isFalse
                  (ModelPipe.within_limit (pPipelineToModel [ Derive("big", over) ]))
                  "a pipeline carrying the 513-node expression is OUTSIDE the §21.8 limit (512) on the model"

              Expect.isTrue
                  (ModelPipe.within_limit (pPipelineToModel [ Derive("ok", within); Filter(Lit(Cell.Bool true)) ]))
                  "and one carrying the 511-node expression is within it — the premise is not constant"

              match DataFrame.evalPipeline [ Derive("big", over) ] table with
              | Ok t -> Expect.equal (Table.rowCount t) rows "the shipped evaluator evaluates the over-limit pipeline"
              | Error e ->
                  failtestf
                      "the shipped evaluator refused the over-limit pipeline: %A — the finding no longer holds; re-read the ladder row"
                      e

              match
                  DataFrame.evalPipelineWithInEnvCounted DataFrame.noResolve Map.empty [ Derive("big", over) ] table
              with
              | Ok(_, n) -> Expect.equal n rows "and charges it exactly the frame's rows, as any Derive"
              | Error e -> failtestf "the counted path refused it: %A" e

              // the model agrees: the walk succeeds, so `over_limit_not_refused` applies and it is Ok
              match
                  ModelPipe.eval_counted
                      pPrims
                      (pStepOfProduction DataFrame.noResolve Map.empty)
                      (pEnvToModel Map.empty)
                      (pPipelineToModel [ Derive("big", over) ])
                      (pFrameOfTable table)
              with
              | ModelPipe.Ok(_, m) -> Expect.equal m (bigint rows) "the model evaluates it too, at the same count"
              | ModelPipe.Error e ->
                  failtestf "the model refused the over-limit pipeline: %s" (pRenderError (pErrOfModel e))

          // ---- Phase 234: the expression evaluator, concrete (`proofs/Pipeline.fst` §3, §9) ----

          testCase
              "the concrete expression evaluator agrees with DataFrame.evalExprInRow cell for cell and error for error over generated expressions and rows, and its visit count never exceeds the node count"
          <| fun _ ->
              // The evaluator the pipeline differential above runs inside every Filter and Derive,
              // compared on its own: one expression, one row, production's primitives under the
              // model's recursion against the shipped `evalExprInRow`. Beside each comparison the
              // model's `expr_visits` is read and held to `expr_nodes` — `visits_le_nodes`, on the
              // sample, numerically — and the sample must reach both a short circuit (visits below
              // nodes) and a full walk (visits equal to nodes), so the count is known to follow the
              // evaluator rather than the tree.
              let sample = pRowSample 234 600

              let results =
                  sample
                  |> List.map (fun (label, cols, row, e) ->
                      let diff, visits, nodes = pRowCompare pEnv cols row e
                      label, e, diff, visits, nodes)

              let diffs =
                  results
                  |> List.choose (fun (label, _, d, _, _) -> d |> Option.map (fun d -> label + ": " + d))

              Expect.isEmpty diffs (sprintf "disagreements:\n%s" (String.concat "\n" diffs))

              let compared = List.length results

              let oks =
                  sample
                  |> List.filter (fun (_, cols, row, e) -> Result.isOk (DataFrame.evalExprInRow pEnv cols row e))
                  |> List.length

              let errs = compared - oks
              Expect.isGreaterThan compared 1500 (sprintf "expression-row pairs were compared (compared=%d)" compared)
              Expect.isGreaterThan oks 300 (sprintf "pairs evaluated to a cell on both sides (ok=%d)" oks)
              Expect.isGreaterThan errs 300 (sprintf "pairs were refused on both sides (err=%d)" errs)

              let violations =
                  results
                  |> List.filter (fun (_, _, _, v, n) -> v > n)
                  |> List.map (fun (l, _, _, v, n) -> sprintf "%s: visits %d > nodes %d" l v n)

              Expect.isEmpty
                  violations
                  (sprintf
                      "`visits_le_nodes` on the sample — a row's visits never exceed the nodes:\n%s"
                      (String.concat "\n" violations))

              let shortCircuits =
                  results |> List.filter (fun (_, _, _, v, n) -> v < n) |> List.length

              let fullWalks = results |> List.filter (fun (_, _, _, v, n) -> v = n) |> List.length

              Expect.isGreaterThan
                  shortCircuits
                  100
                  (sprintf "short circuits were reached — visits strictly below nodes (short=%d)" shortCircuits)

              Expect.isGreaterThan
                  fullWalks
                  100
                  (sprintf "full walks were reached — visits equal to nodes (full=%d)" fullWalks)

              let kinds =
                  results |> List.collect (fun (_, e, _, _, _) -> pExprTags e) |> Set.ofList

              for kind in
                  [ "Col"
                    "Lit"
                    "Param"
                    "Binary"
                    "Not"
                    "Coalesce"
                    "Case"
                    "Cast"
                    "ApplyFn"
                    "InList"
                    "IsNull"
                    "InParam"
                    "Now"
                    "Quotient"
                    "Rounded" ] do
                  Expect.isTrue
                      (Set.contains kind kinds)
                      (sprintf "the sample reached the %s expression kind (reached: %A)" kind kinds)

              printfn
                  "evaluator differential: compared=%d ok=%d err=%d shortCircuits=%d fullWalks=%d visits=%d nodes=%d"
                  compared
                  oks
                  errs
                  shortCircuits
                  fullWalks
                  (results |> List.sumBy (fun (_, _, _, v, _) -> v))
                  (results |> List.sumBy (fun (_, _, _, _, n) -> n))

              Expect.equal
                  (pRowSample 234 600
                   |> List.map (fun (l, cols, row, e) -> l, pRowCompare pEnv cols row e))
                  (results |> List.map (fun (l, _, d, v, n) -> l, (d, v, n)))
                  "same seed => identical results"

          testCase
              "a model that under-counts a nested expression's nodes DISAGREES with the evaluator's visit count — the node bound can fail"
          <| fun _ ->
              // The teeth. `pUnderCountNodes` is `expr_nodes` with a `Case`'s arms forgotten — a
              // model that under-counts a nested expression. Held to the same visit count the case
              // above holds the faithful counter to, it MUST lose: the evaluator walks the arms it
              // forgot. If this ever passes, the visit count has stopped following the evaluator
              // and the green run above certifies nothing about `visits_le_nodes`.
              let losses =
                  pRowSample 234 600
                  |> List.choose (fun (label, cols, row, e) ->
                      let _, visits, _ = pRowCompare pEnv cols row e
                      let under = pUnderCountNodes e

                      if visits > under then
                          Some(sprintf "%s: visits %d > under-count %d" label visits under)
                      else
                          None)

              Expect.isNonEmpty losses "a model that forgets a Case's arms MUST be exceeded by the visits"

              printfn "evaluator go-red: the under-counting model lost on %d rows" (List.length losses)

              // And one witness, exactly: a `Case` whose `when` holds walks the Case, the when and the
              // then's three nodes — five visits, never the else — against six nodes and an
              // under-count of two.
              let table, _ = pGenTable (ConfRng.ofSeed 234)
              let row = table.Columns |> List.map (fun c -> List.head (Column.toCells c))

              let nested =
                  Case([ Lit(Cell.Bool true), Binary(Add, Col "v", Lit(Cell.Int 1)) ], Lit(Cell.Int 0))

              let diff, visits, nodes = pRowCompare pEnv table.Schema row nested
              Expect.isNone diff "the witness evaluates the same on both sides"

              Expect.equal
                  visits
                  5
                  "five visits: the Case, its when, and the then's three nodes; the else is never read"

              Expect.equal nodes 6 "six nodes, faithfully counted"
              Expect.equal (pUnderCountNodes nested) 2 "two nodes under the forgetful count"
              Expect.isTrue (visits <= nodes) "the faithful count bounds the visits"
              Expect.isTrue (visits > pUnderCountNodes nested) "and the forgetful one does not"

          testCase
              "`work_bounded` on the shipped primitives — over every generated pipeline within the limit the evaluator's visits are at most the count times `Limits.max_expr_nodes`, and exceed the count where an expression has more than one node"
          <| fun _ ->
              // The theorem's numeric shadow on the pipeline sample: `work` (the model's visits over
              // the walk, read off its own Filter and Derive) against `cost` (the count), with the
              // format's constant between them — and the strict half, that the visits EXCEED the
              // count on real expressions, so the bound is a bound on something and not on itself.
              let mutable rng = ConfRng.ofSeed 154
              let mutable checkedCount = 0
              let mutable exceeding = 0
              let mutable workTotal = 0
              let mutable costTotal = 0

              for i in 1..400 do
                  let table, r1 = pGenTable rng
                  let right, r2 = pGenTable r1
                  let p, r3 = pGenPipeline r2
                  rng <- r3
                  let other = pStepOfProduction (pResolveWith right) pEnv
                  let menv = pEnvToModel pEnv
                  let mp = pPipelineToModel p
                  let mf = pFrameOfTable table

                  if ModelPipe.within_limit mp then
                      let work = int (ModelPipe.work pPrims other menv mf mp)
                      let cost = int (ModelPipe.cost (ModelPipe.eval_step pPrims other menv) mf mp)

                      Expect.isTrue
                          (work <= cost * 512)
                          (sprintf
                              "generated %d: work %d exceeds cost %d x 512 — `work_bounded` fails on the sample"
                              i
                              work
                              cost)

                      match ModelPipe.eval_counted pPrims other menv mp mf with
                      | ModelPipe.Ok(_, m) ->
                          Expect.equal
                              (int m)
                              cost
                              (sprintf "generated %d: the count is the walk's cost (`eval_total`)" i)
                      | ModelPipe.Error _ -> ()

                      checkedCount <- checkedCount + 1
                      workTotal <- workTotal + work
                      costTotal <- costTotal + cost

                      if work > cost then
                          exceeding <- exceeding + 1

              Expect.isGreaterThan
                  checkedCount
                  300
                  (sprintf "pipelines within the limit were checkedCount (checkedCount=%d)" checkedCount)

              Expect.isGreaterThan
                  exceeding
                  10
                  (sprintf
                      "pipelines whose visits exceed their count — the bound is doing work (exceeding=%d; most drawn Filter and Derive expressions are a single leaf, one visit per row, so work equals cost there and the strict half is reached on the multi-node ones)"
                      exceeding)

              printfn
                  "work bound: checkedCount=%d exceeding=%d visits=%d count=%d"
                  checkedCount
                  exceeding
                  workTotal
                  costTotal

          // ---- Phase 433 — the ROW-LOCAL INCREMENTAL REFRESH: prime and refresh against the model ----

          testCase
              "the incremental oracle agrees with Incremental.prime and Incremental.refresh on the result over generated row-local pipelines, priors and deltas, and a refreshed state refreshes as a primed one"
          <| fun _ ->
              let t = iDifferential iFaithful 433 600

              match t.IDiffs with
              | d :: _ ->
                  failtestf
                      "the incremental oracle and production DISAGREE (%d disagreements)\n%s"
                      (List.length t.IDiffs)
                      d
              | [] ->
                  // Adequacy: the sample primes, refuses, refreshes, REUSES cached rows (a zero here
                  // would mean the tokens never matched and every refresh was a full walk in
                  // disguise), evaluates named rows, and reaches both delta shapes. Measured at 600
                  // trials, seed 433: primed 280, refused 320 (the generator draws expressions that
                  // type-error at the prime, and both sides refuse alike), refreshed 560, reused 848,
                  // named 1380, invalidated 55, over-named 55. Each threshold sits under its figure
                  // with room; they catch a generator that stops reaching a shape, not pin the numbers.
                  Expect.isGreaterThan t.IPrimed 150 (sprintf "primes evaluated on both sides (primed=%d)" t.IPrimed)
                  Expect.isGreaterThan t.IRefreshed 300 (sprintf "refreshes compared (refreshed=%d)" t.IRefreshed)

                  Expect.isGreaterThan
                      t.IReused
                      400
                      (sprintf "rows reused from the prior state across the refreshes (reused=%d)" t.IReused)

                  Expect.isGreaterThan t.INamed 600 (sprintf "rows named by the deltas (named=%d)" t.INamed)

                  Expect.isGreaterThan
                      t.IInvalidated
                      25
                      (sprintf "refreshes under a column invalidation (invalidated=%d)" t.IInvalidated)

                  Expect.isGreaterThan
                      t.IOverNamed
                      25
                      (sprintf "refreshes under an over-named delta (overNamed=%d)" t.IOverNamed)

                  printfn
                      "incremental differential: primed=%d refused=%d refreshed=%d reused=%d named=%d invalidated=%d overNamed=%d"
                      t.IPrimed
                      t.IRefused
                      t.IRefreshed
                      t.IReused
                      t.INamed
                      t.IInvalidated
                      t.IOverNamed

          testCase
              "an incremental oracle that reuses a NAMED row's cached cell DISAGREES with Incremental.refresh — the measurement can fail"
          <| fun _ ->
              // The broken maintenance rule: every token's cached cell is reused, the delta ignored —
              // so a named row whose cells changed answers with the cell it had. Over the same
              // sample, this must lose on the result on some trial. The faithful case above is what
              // says the sample's deltas are honoured; this is what says the comparison can fail.
              let t = iDifferential iReuseAll 433 600

              Expect.isGreaterThan
                  (List.length t.IDiffs)
                  0
                  "a walk that reuses a named row's cached cell agreed with production everywhere — the differential cannot fail"

              printfn "incremental go-red: %d disagreements, first: %s" (List.length t.IDiffs) (List.head t.IDiffs)

          // ---- Phase 464 — the INCREMENTAL REFRESH PAST THE ROW-LOCAL CLASS: prime and refresh
          //      against the model over the maintained GroupBy, Project, Limit, Window and the
          //      filtering joins ----

          testCase
              "the maintained incremental oracle agrees with Incremental.prime and Incremental.refresh on the result over generated pipelines of the maintained classes, priors, deltas and a moving relation, and a refreshed state refreshes as a primed one"
          <| fun _ ->
              let t = mDifferential iFaithful mFaithful 464 700

              match t.MDiffs with
              | d :: _ ->
                  failtestf
                      "the maintained incremental oracle and production DISAGREE (%d disagreements)\n%s"
                      (List.length t.MDiffs)
                      d
              | [] ->
                  // Adequacy: the sample primes, refreshes, reaches every class, removes a group's
                  // last row, moves a limit's slice, moves a window's partition key and moves the
                  // joined relation. Each threshold sits under its measured figure with room; they
                  // catch a generator that stops reaching a shape, not pin the numbers.
                  Expect.isGreaterThan t.MPrimed 100 (sprintf "primes evaluated on both sides (primed=%d)" t.MPrimed)
                  Expect.isGreaterThan t.MRefreshed 100 (sprintf "refreshes compared (refreshed=%d)" t.MRefreshed)
                  Expect.isGreaterThan t.MGrouped 40 (sprintf "grouped pipelines primed (grouped=%d)" t.MGrouped)

                  Expect.isGreaterThan
                      t.MGroupVanished
                      3
                      (sprintf "refreshes whose delta removed a group's last row (vanished=%d)" t.MGroupVanished)

                  Expect.isGreaterThan
                      t.MSliceMoved
                      3
                      (sprintf "refreshes whose limit's slice moved (sliceMoved=%d)" t.MSliceMoved)

                  Expect.isGreaterThan
                      t.MPartitionMoved
                      3
                      (sprintf
                          "refreshes whose delta changed a window's partition key (partitionMoved=%d)"
                          t.MPartitionMoved)

                  Expect.isGreaterThan
                      t.MRelationMoved
                      3
                      (sprintf "refreshes whose joined relation moved (relationMoved=%d)" t.MRelationMoved)

                  Expect.isGreaterThan
                      t.MProjected
                      3
                      (sprintf "refreshes through a projection (projected=%d)" t.MProjected)

                  // Phase 471 — the sort class: sorted pipelines, produced orders that moved, and
                  // refreshes under a pure reordering of the source (the `arrivedAsBefore` fall-back).
                  Expect.isGreaterThan t.MSorted 20 (sprintf "refreshes through a sort (sorted=%d)" t.MSorted)

                  Expect.isGreaterThan
                      t.MOrderMoved
                      3
                      (sprintf "refreshes whose produced order moved (orderMoved=%d)" t.MOrderMoved)

                  Expect.isGreaterThan
                      t.MReordered
                      3
                      (sprintf "refreshes of a sorted pipeline under a reordered source (reordered=%d)" t.MReordered)

                  printfn
                      "maintained differential: primed=%d refused=%d refreshed=%d grouped=%d vanished=%d sliceMoved=%d partitionMoved=%d relationMoved=%d projected=%d sorted=%d orderMoved=%d reordered=%d"
                      t.MPrimed
                      t.MRefused
                      t.MRefreshed
                      t.MGrouped
                      t.MGroupVanished
                      t.MSliceMoved
                      t.MPartitionMoved
                      t.MRelationMoved
                      t.MProjected
                      t.MSorted
                      t.MOrderMoved
                      t.MReordered

          testCase
              "a maintained oracle that reuses a group whose members changed DISAGREES with Incremental.refresh — the group measurement can fail"
          <| fun _ ->
              // The broken maintenance rule: the prior group's member tokens are overwritten with the
              // current group's, so a group that lost or gained a member reuses its prior aggregates.
              let t = mDifferential iFaithful mStaleMembers 464 700

              Expect.isGreaterThan
                  (List.length t.MDiffs)
                  0
                  "a walk that reuses a group whose members changed agreed with production everywhere — the group differential cannot fail"

              printfn "maintained group go-red: %d disagreements, first: %s" (List.length t.MDiffs) (List.head t.MDiffs)

          testCase
              "a maintained oracle that reuses a join's verdicts under a moved relation DISAGREES with Incremental.refresh — the join measurement can fail"
          <| fun _ ->
              // The broken maintenance rule: the prior key index is overwritten with the relation's
              // index at the refresh, so a stable row's verdict is reused under a relation that moved.
              let t = mDifferential iFaithful mStaleRelation 464 700

              Expect.isGreaterThan
                  (List.length t.MDiffs)
                  0
                  "a walk that reuses a join's verdicts under a moved relation agreed with production everywhere — the join differential cannot fail"

              printfn "maintained join go-red: %d disagreements, first: %s" (List.length t.MDiffs) (List.head t.MDiffs)

          testCase
              "a maintained oracle that reuses a NAMED row's cells DISAGREES with Incremental.refresh through a limit, a projection and a window — the measurement can fail"
          <| fun _ ->
              let t = mDifferential mReuseAll mFaithful 464 700

              Expect.isGreaterThan
                  (List.length t.MDiffs)
                  0
                  "a walk that reuses a named row's cells agreed with production everywhere — the maintained differential cannot fail"

              printfn
                  "maintained reuse-all go-red: %d disagreements, first: %s"
                  (List.length t.MDiffs)
                  (List.head t.MDiffs)

          // ---- Phase 471 — the maintained SORT: the merge against the prior produced order, the
          //      comparator, and the premise check ----

          testCase
              "a maintained oracle that merges against a prior order whose recorded arrival is overwritten with the arrival now DISAGREES with Incremental.refresh — the sort measurement can fail"
          <| fun _ ->
              // The broken reuse condition: `arrivedAsBefore` reads the arrival now on both sides, so
              // a reordering among the stable rows is never seen and the merge keeps the prior
              // order's tie-break where the reference breaks the tie the other way.
              let t = mDifferential iFaithful mStaleArrival 464 700

              Expect.isGreaterThan
                  (List.length t.MDiffs)
                  0
                  "a walk that merges the prior produced order under a stale arrival record agreed with production everywhere — the sort differential cannot fail"

              printfn "maintained sort go-red: %d disagreements, first: %s" (List.length t.MDiffs) (List.head t.MDiffs)

          testCase
              "the model's compareResolved is production's over typed rows, and production's two sort paths — the order codes and the comparator — answer one order (Phase 471)"
          <| fun _ ->
              let keyPool = [ "g", Asc; "v", Desc; "w", Asc; "m", Desc; "g", Desc; "v", Asc ]
              let mutable rng = ConfRng.ofSeed 471
              let mutable pairs = 0
              let mutable sorts = 0

              for _ in 1..300 do
                  let table, r1 = pGenTable rng
                  let mask, r2 = ConfRng.intBelow 63 r1
                  rng <- r2

                  let keys =
                      keyPool
                      |> List.indexed
                      |> List.filter (fun (i, _) -> ((mask + 1) >>> i) &&& 1 = 1)
                      |> List.map snd

                  let resolved = DataFrame.resolveSortKeys table.Schema keys
                  let mkeys = resolved |> List.map (fun (i, d) -> bigint i, pFwd pSortDirs d)
                  let rows = sRowsOf table

                  for a in rows do
                      for b in rows do
                          pairs <- pairs + 1
                          let prod = DataFrame.compareResolved resolved a b

                          let model =
                              ModelSort.compare_resolved
                                  sPrims
                                  pPrims
                                  mkeys
                                  (a |> Array.toList |> List.map pCellToModel)
                                  (b |> Array.toList |> List.map pCellToModel)

                          if bigint prod <> model then
                              failtestf "compareResolved %A: production %d, model %A on %A / %A" keys prod model a b

                  // the typed path (order codes where the keys allow) against the comparator path,
                  // the stable `List.sortWith` production's row form documents
                  sorts <- sorts + 1

                  match
                      DataFrame.evalPipelineWithInEnv DataFrame.noResolve Map.empty [ Transform.sortBy keys ] table
                  with
                  | Error e -> failtestf "the sort refused %A" e
                  | Ok sortedTable ->
                      let coded = sRowsOf sortedTable |> List.map Array.toList

                      let viaComparator =
                          rows
                          |> List.sortWith (fun a b -> DataFrame.compareResolved resolved a b)
                          |> List.map Array.toList

                      Expect.equal coded viaComparator (sprintf "the two sort paths disagree under %A" keys)

              printfn "sort comparator: %d row pairs compared, %d sorts cross-checked" pairs sorts

          testCase
              "production's comparison primitives satisfy the laws the sort theorems assume — `sprims_laws` instantiated at Kernels.compareFloat, DecimalText.compare and String.CompareOrdinal (Phase 471)"
          <| fun _ ->
              let preorder (label: string) (cmp: 'a -> 'a -> int) (pool: 'a list) =
                  for x in pool do
                      Expect.equal (cmp x x) 0 (sprintf "%s: reflexive at %A" label x)

                      for y in pool do
                          Expect.equal (cmp x y < 0) (cmp y x > 0) (sprintf "%s: sign-antisymmetric at %A %A" label x y)
                          Expect.equal (cmp x y = 0) (cmp y x = 0) (sprintf "%s: tie-symmetric at %A %A" label x y)

                          for z in pool do
                              if cmp x y <= 0 && cmp y z <= 0 then
                                  Expect.isTrue (cmp x z <= 0) (sprintf "%s: transitive at %A %A %A" label x y z)

              let floats =
                  [ nan
                    -0.0
                    0.0
                    infinity
                    -infinity
                    1.5
                    -1.5
                    2147483647.0
                    2147483648.0
                    -2147483648.0
                    1e300
                    5e-324 ]

              preorder "compareFloat" Kernels.compareFloat floats

              preorder
                  "CompareOrdinal"
                  (fun (a: string) b -> System.String.CompareOrdinal(a, b))
                  [ ""; "a"; "A"; "ab"; "b"; "é"; "z0" ]

              let ints =
                  [ 0
                    1
                    -1
                    7
                    2147483647
                    -2147483648
                    2147483646
                    -2147483647
                    1000000
                    999999 ]

              let decimals =
                  (ints |> List.map string)
                  @ [ "0.5"
                      "-0.5"
                      "2147483647.25"
                      "-2147483648.5"
                      "1234567890123456789012345.1234567"
                      "1.50"
                      "1.5" ]
                  |> List.choose DecimalText.tryCanonical

              for d in decimals do
                  Expect.equal
                      (DecimalText.tryCanonical d)
                      (Some d)
                      (sprintf "the canonical form of a canonical text is itself: %s" d)

              for i in ints do
                  Expect.equal
                      (DecimalText.tryCanonical (string i))
                      (Some(string i))
                      (sprintf "an int's text is canonical decimal text: %d" i)

              let decCmp (a: string) (b: string) =
                  match DecimalText.compare a b with
                  | Some c -> c
                  | None -> failtestf "DecimalText.compare answered None on canonical texts %s / %s" a b

              preorder "DecimalText.compare" decCmp decimals

              for i in ints do
                  for j in ints do
                      Expect.equal
                          (Kernels.compareFloat (float i) (float j))
                          (decCmp (string i) (string j))
                          (sprintf "an int reads the same way in the float carrier and the decimal one: %d %d" i j)

          testCase
              "a boxed decimal column holding ints beside a decimal past the width sorts to ONE order from every arrival order — the comparator is a total preorder on the corner Phase 472 closes, so no incomparable pair reaches a Sort (Phase 471 premise check)"
          <| fun _ ->
              // D38 item 5's corner: a derived decimal column whose typed pass `Vec.packAt` abandons
              // (a text past the scaled carrier's width among the cells), so the ints stay raw beside
              // the decimal. `compareCells` reads an int against an int in the float carrier — exact
              // for every 32-bit int — and an int against a decimal exactly, so the pairs agree and the
              // sort is one order whatever the arrival. The lead the shard named (an incomparable pair
              // read as a tie) needs a float beside a decimal, which the typer refuses (D18).
              let wide = Cell.Decimal "1234567890123456789012345.1234567"

              let xs = [ 2147483647; -2147483648; 5; 11; 0; 2147483646; -7 ]

              let table (order: int list) : Table =
                  { Schema = [ Field.create "id" IntType; Field.create "x" IntType ]
                    Columns =
                      [ KitColumn.create "id" IntType (order |> List.map Int)
                        KitColumn.create "x" IntType (order |> List.map (fun i -> Int xs[i])) ] }

              let p =
                  [ Derive("d", Case([ Binary(Eq, Col "x", Lit(Int 11)), Lit wide ], Col "x"))
                    Transform.sortBy [ "d", Asc ] ]

              let sortedValues (order: int list) =
                  match DataFrame.evalPipelineWithInEnv DataFrame.noResolve Map.empty p (table order) with
                  | Error e -> failtestf "the sort refused %A" e
                  | Ok t ->
                      t.Columns
                      |> List.find (fun c -> c.Name = "d")
                      |> Column.toCells
                      |> List.map Cell.token

              let n = List.length xs
              let expected = sortedValues [ 0 .. n - 1 ]

              let orders =
                  [ for k in 0 .. n - 1 -> [ 0 .. n - 1 ] |> List.map (fun i -> (i + k) % n) ]
                  @ [ [ n - 1 .. -1 .. 0 ]; [ 3; 0; 5; 1; 6; 2; 4 ]; [ 6; 5; 0; 4; 1; 3; 2 ] ]

              for order in orders do
                  Expect.equal
                      (sortedValues order)
                      expected
                      (sprintf "the sort answered another order from arrival %A" order)

              // the exact numeric order, the wide decimal last; the cells read back as decimals at the
              // frame's boundary (`Frame.toTable`), which says nothing of the carrier the sort compared
              Expect.equal
                  expected
                  [ "m:-2147483648"
                    "m:-7"
                    "m:0"
                    "m:5"
                    "m:2147483646"
                    "m:2147483647"
                    "m:1234567890123456789012345.1234567" ]
                  "the boxed column's order is the exact numeric order over its ints and its decimal"

          testCase
              "a derived column the cells retype between refreshes is read by the walk as the reference packs it, and the refresh agrees with the full evaluation (Phase 464, D37)"
          <| fun _ ->
              // Before D37 the walk held the retyped column's cells raw (an int under a float
              // column) where the reference packs them, so a Mod downstream — or a group whose
              // aggregates were reused from the int reading — answered where the reference refused.
              let before, after = mRetypeTables

              for (label, p) in mRetypeShapes do
                  pHoldRefreshToFull label p before after

          testCase
              "a derived column the reference boxes is read by the walk as the reference reads it, an int as the column's type on either form (Phase 470, D38; Phase 472, D40)"
          <| fun _ ->
              // After D37 the walk widened an int under a float or decimal column unconditionally, where
              // the reference's `Vec.packAt` abandoned the typed pass for the boxed cells — a decimal text
              // past the scaled carrier's width among them — and read the int as it was; Phase 470
              // mirrored that verdict in the walk, and Phase 472 made the boxed form widen as the typed
              // carrier does, so the walk widens unconditionally again and the verdict is gone. The
              // shapes still hold the refresh to the full evaluation.
              for (label, p, before, after) in pBoxedShapes do
                  pHoldRefreshToFull label p before after

          testCase
              "the reference model, the row-local walk and the maintained walk pack a derived column as production does on every retype corner, and the three refuse two types no column type holds together as production does (Phase 470, D38)"
          <| fun _ ->
              let before, after = mRetypeTables

              for (label, p) in mRetypeShapes do
                  pCornerAgrees ("retype: " + label) p before after

              for (label, p, before, after) in pBoxedShapes do
                  pCornerAgrees ("boxed: " + label) p before after

              // The cells' typing refuses a pair no column type holds together (D18), by name, on
              // every side — the model's `column_type_by` carries the refusal since this phase.
              let beside =
                  [ Derive("d", Case([ Binary(Eq, Col "g", Lit(Str "A")), Lit(Str "s") ], Col "x")) ]

              pCornerAgrees "beside: a string beside an int" beside before after

              match DataFrame.evalPipelineWithInEnv DataFrame.noResolve Map.empty beside after with
              | Error(TypeError detail) ->
                  Expect.stringContains detail "which no column type holds together" "production's refusal"
              | other -> failtestf "production answered %A" other

          testCase
              "the raw reference of D37 item 4 answered the retype corner production refuses — shown once, on the old derive (Phase 470)"
          <| fun _ ->
              // The first retype shape over the mutated table: `d` is a float column holding the standing
              // rows' ints, which the reference packs as floats and the old reference held raw, so the
              // `Mod` downstream refused on production and answered on the model.
              let _, after = mRetypeTables
              let menv = pEnvToModel Map.empty

              let dStep, mStep =
                  match snd (List.head mRetypeShapes) with
                  | [ Derive(dn, dx); m ] -> (dn, pExprToModel dx), pTransformToModel m
                  | other -> failtestf "unexpected shape %A" other

              let raw =
                  match pRawDerive menv (pFrameOfTable after) (fst dStep) (snd dStep) with
                  | ModelPipe.Error e -> failtestf "the raw derive refused %s" (pRenderError (pErrOfModel e))
                  | ModelPipe.Ok f ->
                      ModelPipe.eval_uncounted pPrims (pStepOfProduction DataFrame.noResolve Map.empty) menv [ mStep ] f

              match
                  DataFrame.evalPipelineWithInEnv DataFrame.noResolve Map.empty (snd (List.head mRetypeShapes)) after,
                  raw
              with
              | Error(TypeError detail), ModelPipe.Ok _ ->
                  Expect.stringContains detail "mod requires integer operands" "production refuses the float reading"
              | full, raw ->
                  failtestf "expected production to refuse and the raw reference to answer; got %A and %A" full raw

              // The corrected reference refuses as production does.
              match
                  ModelPipe.eval_uncounted
                      pPrims
                      (pStepOfProduction DataFrame.noResolve Map.empty)
                      menv
                      (snd (List.head mRetypeShapes) |> List.map pTransformToModel)
                      (pFrameOfTable after)
              with
              | ModelPipe.Error(ModelPipe.TypeError detail) ->
                  Expect.stringContains detail "mod requires integer operands" "the reference refuses the float reading"
              | other -> failtestf "the corrected reference answered %A" other

          testCase
              "an int under a float or a decimal column reads as that type's value on the boxed form too — in `Vec.packAt` and in the model — and a model that leaves the boxed int raw fails the differential, shown once (Phase 472, D40)"
          <| fun _ ->
              // The go-red probe, red before this phase: `Vec.packAt` abandons the typed carrier on a cell
              // the type does not carry, and for a decimal column on texts past the scaled carrier's
              // width; the boxed cells now read every int as the column's type, as the typed carrier does.
              let wide = Cell.Decimal "1234567890123456789012345.1234567"
              let boxedFloat = Vec.pack FloatType [| Int 1; Str "x"; Null |]
              let boxedDecimal = Vec.pack DecimalType [| Int 1; wide; Int -30 |]

              for v in [ boxedFloat; boxedDecimal ] do
                  match v with
                  | Cells _ -> ()
                  | other -> failtestf "expected the boxed form, got %A" other

              Expect.equal
                  [ for i in 0..2 -> Vec.cellAt boxedFloat i ]
                  [ Float 1.0; Str "x"; Null ]
                  "an int under a boxed float column is the float of its value"

              Expect.equal
                  [ for i in 0..2 -> Vec.cellAt boxedDecimal i ]
                  [ Cell.Decimal "1"; wide; Cell.Decimal "-30" ]
                  "an int under a boxed decimal column is the decimal of its value"

              // The model packs a cell by the cell and the column's type alone, whatever stands beside it.
              let modelPacked (ty: ColumnType) (cells: Cell list) : Cell list =
                  ModelPipe.pack_cells pPrims (pColToModel ty) (cells |> List.map pCellToModel)
                  |> List.map pCellOfModel

              Expect.equal
                  (modelPacked FloatType [ Int 1; Str "x"; Null ])
                  [ Float 1.0; Str "x"; Null ]
                  "the model packs the float column cell by cell"

              Expect.equal
                  (modelPacked DecimalType [ Int 1; wide; Int -30 ])
                  [ Cell.Decimal "1"; wide; Cell.Decimal "-30" ]
                  "the model packs the decimal column cell by cell"

              // The verdict Phase 470 modelled, kept as `pVerdictDerive`, answers the first boxed shape —
              // an int beside a decimal past the width, read by an `Add` of a float — where production
              // and the corrected reference refuse: a boxed int left raw in the model fails the leg.
              let menv = pEnvToModel Map.empty
              let label, p, _, after = List.head pBoxedShapes

              let dStep, rest =
                  match p with
                  | Derive(dn, dx) :: rest -> (dn, pExprToModel dx), rest |> List.map pTransformToModel
                  | other -> failtestf "unexpected shape %A" other

              let verdict =
                  match pVerdictDerive menv (pFrameOfTable after) (fst dStep) (snd dStep) with
                  | ModelPipe.Error e -> failtestf "the verdict derive refused %s" (pRenderError (pErrOfModel e))
                  | ModelPipe.Ok f ->
                      ModelPipe.eval_uncounted pPrims (pStepOfProduction DataFrame.noResolve Map.empty) menv rest f

              match DataFrame.evalPipelineWithInEnv DataFrame.noResolve Map.empty p after, verdict with
              | Error(TypeError detail), ModelPipe.Ok _ ->
                  Expect.stringContains detail "decimal" (label + ": production refuses the decimal reading")
              | full, verdict ->
                  failtestf
                      "%s: expected production to refuse and the verdict model to answer; got %A and %A"
                      label
                      full
                      verdict

              match
                  ModelPipe.eval_uncounted
                      pPrims
                      (pStepOfProduction DataFrame.noResolve Map.empty)
                      menv
                      (p |> List.map pTransformToModel)
                      (pFrameOfTable after)
              with
              | ModelPipe.Error(ModelPipe.TypeError detail) ->
                  Expect.stringContains detail "decimal" (label + ": the reference refuses the decimal reading")
              | other -> failtestf "%s: the corrected reference answered %A" label other

          // ---- Phase 434 — the RELATIONAL verbs: output schema and refusals against the
          //      evaluator, the walk against SchemaWalk ----

          testCase
              "the relational oracle agrees with the evaluator on every verb's output schema and refusal, and with SchemaWalk.ofTransform on the knowledge, over the 411 and 412 shapes and generated pipelines"
          <| fun _ ->
              let t = rDifferential rPrims 434 400

              Expect.isEmpty t.RDiffs (sprintf "disagreements:\n%s" (String.concat "\n" (List.rev t.RDiffs)))

              Expect.isGreaterThan t.ROk 300 (sprintf "steps evaluated on both sides (ok=%d)" t.ROk)
              Expect.isGreaterThan t.RErr 150 (sprintf "steps refused on both sides (err=%d)" t.RErr)
              Expect.isGreaterThan t.RWalks 600 (sprintf "walk steps compared (walks=%d)" t.RWalks)

              // No excuse arm (Phase 476): a union production refuses on D18 is refused by the model,
              // text for text — every one of the 38 type pairs no widening relates, and the 423 shape.
              Expect.isGreaterThanOrEqual
                  t.RBeside
                  39
                  (sprintf "unions refused on D18 by both sides (beside=%d)" t.RBeside)

              for verb in
                  [ "Project"
                    "GroupBy"
                    "Join"
                    "Window"
                    "Pivot"
                    "Unpivot"
                    "Sort"
                    "Distinct"
                    "Limit"
                    "Union"
                    "Intersect"
                    "Except" ] do
                  Expect.isTrue
                      (Set.contains verb t.RVerbs)
                      (sprintf "the sample reached the %s verb (reached: %A)" verb t.RVerbs)

              // The join's names, through the public function the evaluator and the walk share,
              // against the model's over the 411 shapes and a few more.
              for left, right in
                  [ [ "x"; "x_right" ], [ "x" ]
                    [ "x" ], [ "x"; "x_right" ]
                    [ "x"; "x_right"; "x_right_2" ], [ "x"; "x_right"; "x_right_2" ]
                    [ "a"; "b" ], [ "b"; "a"; "c" ]
                    [ "a" ], [ "a"; "a_right_2"; "a_right" ] ] do
                  Expect.equal
                      (ModelRel.join_right_names rPrims left right)
                      (DataFrame.joinRightNames left right)
                      (sprintf "joinRightNames %A %A" left right)

              printfn
                  "relational differential: steps=%d ok=%d err=%d rowRefused=%d beside=%d walks=%d verbs=%d"
                  t.RSteps
                  t.ROk
                  t.RErr
                  t.RRowRefused
                  t.RBeside
                  t.RWalks
                  t.RVerbs.Count

              Expect.equal (rDifferential rPrims 434 400) t "same seed => identical tally"

          testCase
              "the four repeated-name witnesses are refused on the shipped evaluator — a Project, a GroupBy, an Unpivot and a Pivot each name the verb, the name and both producers"
          <| fun _ ->
              // The four witnesses Phase 434 recorded answering `Ok` with a name twice, which
              // `Relational.fst`'s `*_repeat_refused` theorems now state as refusals (Phase 468, D35).
              // The differential above compares production's refusal to the model's text; this case
              // goes red, naming the verb, the moment one of the four answers a table again.
              let shapes = rShapes |> List.filter (fun (label, _, _) -> label.StartsWith "468-")

              Expect.equal (List.length shapes) 4 "the four witnesses are among the shapes"

              for label, table, p in shapes do
                  match DataFrame.evalPipelineWithInEnv DataFrame.noResolve Map.empty p table with
                  | Ok t' ->
                      failtestf
                          "%s: the evaluator answered %A — a repeated name is no longer refused"
                          label
                          (t'.Schema |> List.map _.Name)
                  | Error(TypeError detail) ->
                      Expect.stringContains
                          detail
                          "is named twice, by "
                          (sprintf "%s: the refusal names both producers" label)
                  | Error e -> failtestf "%s: refused with %A, not the repeated-name TypeError" label e

          testCase
              "a walk that keeps a union's LEFT type is not sound for the evaluator on 423-union-widens, and the shipped walk is — the soundness measurement can fail"
          <| fun _ ->
              // The pre-469 `Union` arm answered its input knowledge (D32 finding (b)). Over the
              // widening shape the evaluator answers a float column; the old arm's knowledge says
              // int, and the model's `sound` must say so. The differential above holds the shipped
              // walk to the same `sound` on every step; this is what says that check has teeth.
              let label, table, p =
                  rShapes |> List.find (fun (label, _, _) -> label = "423-union-widens")

              let out =
                  match DataFrame.evalPipelineWithInEnv DataFrame.noResolve Map.empty p table with
                  | Ok t' -> (pFrameOfTable t').cols
                  | Error e -> failtestf "%s: the evaluator refused the widening union: %A" label e

              let input = SchemaWalk.ofSchema table.Schema

              Expect.isFalse
                  (ModelRel.sound (rKnowledgeToModel input) out)
                  "the left-type answer is sound for the widened output — the check cannot fail"

              let shipped = SchemaWalk.ofPipelineFrom SchemaWalk.noSources input p

              Expect.isTrue
                  (ModelRel.sound (rKnowledgeToModel shipped) out)
                  (sprintf "the shipped walk %A is not sound for %A" (rKnowledgeOfProd shipped) out)

          testCase
              "the twelve-verb evaluator built from the model's schema and production's rows agrees with DataFrame.evalPipelineWithInEnvCounted — the narrowing measured end to end"
          <| fun _ ->
              let t = pipelineDifferential pPipelineToModel pRelationalStep 434 400

              Expect.isEmpty t.PDiffs (sprintf "disagreements:\n%s" (String.concat "\n" (List.rev t.PDiffs)))
              Expect.isGreaterThan t.POk 120 (sprintf "pipelines evaluated to a table on both sides (ok=%d)" t.POk)
              Expect.isGreaterThan t.PErr 60 (sprintf "pipelines were refused on both sides (err=%d)" t.PErr)

          testCase
              "a relational oracle crossed the pre-D20 suffix rule DISAGREES with the evaluator on the 411 shapes — the measurement can fail"
          <| fun _ ->
              let t = rDifferential rPrimsPreD20 434 0

              Expect.isGreaterThan
                  (List.length t.RDiffs)
                  0
                  "a suffix rule that answers `_right` for every collision agreed with production everywhere — the differential cannot fail"

              Expect.isTrue
                  (t.RDiffs |> List.exists (fun d -> d.StartsWith "411-"))
                  (sprintf "the disagreement is on a 411 shape: %A" t.RDiffs)

              printfn "relational go-red (411): %d disagreements, first: %s" (List.length t.RDiffs) (List.last t.RDiffs)

          testCase
              "a relational oracle whose bridge drops a sort key the schema does not carry DISAGREES with the evaluator on the 412 shape — the measurement can fail"
          <| fun _ ->
              let t = pipelineDifferential pPipelineToModel pDropUnknownSortKeysStep 434 400

              Expect.isGreaterThan
                  (List.length t.PDiffs)
                  0
                  "a model that drops an unknown sort key agreed with production everywhere — the differential cannot fail"

              Expect.isTrue
                  (t.PDiffs |> List.exists (fun d -> d.Contains "production refused"))
                  (sprintf
                      "a disagreement is production refusing what the model evaluated: %A"
                      (List.truncate 3 t.PDiffs))

              printfn "relational go-red (412): %d disagreements, first: %s" (List.length t.PDiffs) (List.last t.PDiffs)
          // ---- Phase 435 — the DENSE FRAME and its KERNELS: the boundary in and out against
          //      Frame.fs, the kernels against Kernels.fs and Pipeline's filter ----

          testCase
              "the frame oracle agrees with Frame.fs at the table door, the row door and the boundary out, and with the kernels and Pipeline's filter where the kernels answer, over generated frames at every column type"
          <| fun _ ->
              let t = frameDifferential fTable fHost 435 300

              Expect.isEmpty
                  t.FDiffs
                  (sprintf "disagreements:\n%s" (String.concat "\n" (List.rev t.FDiffs |> List.truncate 20)))

              for kind in
                  [ "ints"
                    "floats"
                    "bools"
                    "strs"
                    "decs"
                    "dates"
                    "stamps"
                    "stamps+fraction"
                    "cells" ] do
                  Expect.isTrue
                      (Set.contains kind t.FKinds)
                      (sprintf "the sample built a %s vector (built: %A)" kind t.FKinds)

              Expect.isGreaterThan t.FCells 2000 (sprintf "cells compared (%d)" t.FCells)
              Expect.isGreaterThan t.FAllValid 20 (sprintf "null-free views read (%d)" t.FAllValid)
              Expect.isGreaterThan t.FMasked 20 (sprintf "masked views read (%d)" t.FMasked)
              Expect.isGreaterThan t.FOddMasks 0 (sprintf "views over a mask of another length (%d)" t.FOddMasks)
              Expect.isGreaterThan t.FFallbacks 20 (sprintf "fall-back columns packed (%d)" t.FFallbacks)
              Expect.isGreaterThan t.FTablesOut 50 (sprintf "frames written out as tables (%d)" t.FTablesOut)
              Expect.isGreaterThan t.FBoxedOut 0 (sprintf "boxed frames left to Column.ofCells (%d)" t.FBoxedOut)
              Expect.isGreaterThan t.FRowsRefused 0 (sprintf "row widths refused (%d)" t.FRowsRefused)
              Expect.isGreaterThan t.FKernelSome 150 (sprintf "filters the kernels answered (%d)" t.FKernelSome)

              for kind in [ "ints"; "floats"; "decs" ] do
                  Expect.isTrue
                      (Set.contains kind t.FKernelKinds)
                      (sprintf "a kernel answered over a %s vector (answered over: %A)" kind t.FKernelKinds)

              Expect.isGreaterThan t.FKernelNone 100 (sprintf "filters left to the compiled path (%d)" t.FKernelNone)
              Expect.isGreaterThan t.FKernelRows 150 (sprintf "rows the kernels kept (%d)" t.FKernelRows)

              printfn
                  "frame differential: cells=%d kinds=%d views=%d (allValid=%d masked=%d odd=%d) fallbacks=%d out=%d boxedOut=%d refused=%d kernel=%d/%d rows=%d"
                  t.FCells
                  t.FKinds.Count
                  t.FViews
                  t.FAllValid
                  t.FMasked
                  t.FOddMasks
                  t.FFallbacks
                  t.FTablesOut
                  t.FBoxedOut
                  t.FRowsRefused
                  t.FKernelSome
                  t.FKernelNone
                  t.FKernelRows

              Expect.equal (frameDifferential fTable fHost 435 300) t "same seed => identical tally"

          testCase
              "the frame model's packed cell is Pipeline.fst's pack_cell at the differential's instantiation, typed or boxed, at every non-temporal type (Phase 476)"
          <| fun _ ->
              // `Frame.fst`'s `pack_cell_is_pipeline`, read at the two records the differentials
              // instantiate (`fHost`, `pPrims`): the lemma's premise is that the two agree on the
              // carriers of an int, which this case is where it is checked.
              let mutable r = ConfRng.ofSeed 476
              let mutable compared = 0

              for ty in colTypePool do
                  match ty with
                  | DateType
                  | TimestampType _ -> ()
                  | _ ->
                      for _ in 1..40 do
                          let cells =
                              [ for _ in 1..5 ->
                                    let c, r' = genColCell ty r
                                    r <- r'
                                    pCellToModel c ]

                          cells
                          |> List.iteri (fun i c ->
                              let pipe = ModelPipe.pack_cell pPrims (pColToModel ty) c

                              Expect.equal
                                  (ModelFrame.pack_cell fHost (fType ty) cells (bigint i))
                                  pipe
                                  (sprintf "%A column %A, row %d" ty cells i)

                              Expect.equal (ModelFrame.widen_cell fHost (fType ty) c) pipe (sprintf "%A cell %A" ty c)
                              compared <- compared + 1)

              Expect.isGreaterThan compared 900 (sprintf "cells compared (%d)" compared)

          testCase
              "a frame oracle whose bridge reads every validity with its meaning flipped DISAGREES with Frame.ofTable — the read-back measurement can fail"
          <| fun _ ->
              let t = frameDifferential (fTableWith fValidityFlipped) fHost 435 60

              Expect.isGreaterThan
                  (List.length t.FDiffs)
                  0
                  "a model reading every mask inverted agreed with production everywhere — the differential cannot fail"

              Expect.isTrue
                  (t.FDiffs |> List.exists (fun s -> s.Contains "ofTable"))
                  (sprintf "a disagreement is the table door's read-back: %A" (List.truncate 3 t.FDiffs))

              printfn "frame go-red (validity): %d disagreements, first: %s" (List.length t.FDiffs) (List.last t.FDiffs)

          testCase
              "a frame oracle whose float order is reversed DISAGREES with the comparison kernels — the kernel measurement can fail"
          <| fun _ ->
              let t =
                  frameDifferential fTable (fHostWith (fun x y -> -(Kernels.compareFloat x y))) 435 100

              Expect.isGreaterThan
                  (List.length t.FDiffs)
                  0
                  "a model with the float order reversed agreed with the kernels everywhere — the differential cannot fail"

              Expect.isTrue
                  (t.FDiffs |> List.exists (fun s -> s.Contains " bits "))
                  (sprintf "a disagreement is a kernel bitmap: %A" (List.truncate 3 t.FDiffs))

              printfn "frame go-red (kernels): %d disagreements, first: %s" (List.length t.FDiffs) (List.last t.FDiffs) ]
