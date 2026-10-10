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
            ) }

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
        | Error(TypeError detail), ModelPipe.Ok _ when detail.Contains "which no column type holds together" ->
            // Outside the model's domain (Phase 423): the list model's column holds any cells side by
            // side, so it evaluates a derive whose cells span two unrelated types; Core `1.0.0`'s typed
            // column cannot hold them, and production refuses by name (`DECISIONS.md` D18). The pair is
            // compared on nothing — a refusal the model has no object for is not a disagreement.
            None, 0, 1, 0
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
let private pipelineDifferential
    (bridge: Transform list -> ModelPipe.transform list)
    (mkStep: (string -> Result<Table, EvalError>) -> Map<string, Cell> -> Transform list -> ModelPipe.other_fn)
    (seed: int)
    (trials: int)
    : PipeTally =
    let mutable tally = pEmptyTally

    for id, p, table, refuses in pLawVectors () do
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
    | Rounded(x, _) -> 1 + pUnderCountNodes x
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


          // ---- Phase 154: the counted pipeline driver (`proofs/Pipeline.fst`) ----

          testCase
              "the pipeline oracle agrees with DataFrame.evalPipelineWithInEnvCounted on table and count over the transform-laws vectors and a generated sample that reaches every verb and every expression kind"
          <| fun _ ->
              let t = pipelineDifferential pPipelineToModel pFaithfulStep 154 400

              Expect.isEmpty t.PDiffs (sprintf "disagreements:\n%s" (String.concat "\n" (List.rev t.PDiffs)))

              Expect.equal
                  t.PCompared
                  (LawVectorExport.iterations + 400)
                  "every vector and four hundred generated pipelines were compared"

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
                  costTotal ]
