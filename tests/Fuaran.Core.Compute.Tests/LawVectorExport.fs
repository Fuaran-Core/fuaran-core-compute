namespace Fuaran.Compute.Tests

// ============================================================================
//  The host-neutral export of `Conformance.transformLaws`' reference answers,
//  so a host that ships its own dataframe evaluator can run the same family
//  over the same sample without owning the reference (fuaran#1479).
//
//  Why this family needed a different export shape from the one already in the
//  corpus. `capabilityLaws` is SELF-CONTAINED — it takes `(seed, iterations)`
//  and builds its own subjects — so "its vectors" are the pairs it draws, and
//  its exporter runs the law and records what the kit answered.
//  `transformLaws` is a PARITY family: it takes a HOST evaluator `under` and a
//  `gen`, and certifies that `under` agrees with
//  `Fuaran.Compute.DataFrame.evalPipeline` byte-for-byte. Running it HERE with the
//  reference as `under` would certify the reference against itself, which is
//  why the corpus manifest recorded it as not exported.
//
//  What is exportable is the other half of that comparison: not the law's
//  verdict but the REFERENCE ANSWER the verdict is taken against. A host with
//  its own evaluator has the `under` side already; what it cannot obtain
//  without the reference is what the reference said. So each vector carries a
//  drawn `(source, pipeline)` and the wire string `evalPipeline` produced from
//  it, and a host runs the family by evaluating its own pipeline over its own
//  decode of the same source and comparing the encoding — which is exactly the
//  comparison `transformLaws` makes internally
//  (`ColumnCodec.encode (Embedded a) = ColumnCodec.encode (Embedded b)`).
//
//  The sample generator is DECLARED here rather than drawn from the law,
//  because `transformLaws` has no sample of its own to draw: `gen` is its
//  caller's argument. `gen` below is that declaration, and the emitted file's
//  `description` states its draw recipe so a host can reproduce the sample
//  rather than only replay it.
//
//  Every `expected` is computed by CALLING the reference, never by restating
//  what it ought to answer; `LawVectorTests` then runs `transformLaws` itself
//  over this same generator and seed, so a sample the law would not certify
//  cannot be published.
// ============================================================================

module LawVectorExport =

    open System.IO
    open System.Reflection
    open System.Text
    open Fuaran.Core
    open Fuaran.Compute

    /// The family directory inside the shared corpus and the artefact in it. The directory name is
    /// the interface — hosts resolve `laws/` — so it is named once here.
    let familyDirName = "laws"
    let transformFileName = "transform-laws.json"

    /// The seed and sample size the exported vectors are drawn from. Declared rather than taken
    /// from a test's own law invocation, because a host re-running the family must be able to
    /// reproduce the sample from the file alone.
    let seed = 20260904

    /// Two draws of each of the eight pipeline shapes below, then one of each decimal shape (Phase
    /// 277). Far fewer than a law run: each vector carries three whole wire strings, and a host does
    /// not need a hundred draws of the same eight shapes to disagree with the reference.
    let private baseIterations = 16

    // -----------------------------------------------------------------------
    //  the declared sample generator
    // -----------------------------------------------------------------------

    /// The eight pipeline shapes, chosen to reach the semantics the parity contract pins rather
    /// than to be numerous: null propagation through a predicate, sort/distinct stability over a
    /// tie-heavy key, int↔float coercion, group stability and aggregate null handling, an
    /// offset window, the pinned division-by-zero answer, the canonical float layout on a
    /// non-terminating quotient, and a pipeline the reference REFUSES — which is a parity case in
    /// its own right, since the law requires the host to refuse it too.
    let private baseShapeNames =
        [ "filter"
          "sort-distinct"
          "derive-coerce"
          "group-agg"
          "limit"
          "div-by-zero"
          "float-divide"
          "unknown-column" ]

    /// The decimal shapes (Phase 277), one vector each after the sixteen above: a decimal column
    /// through every verb, the exact arithmetic, `Quotient` and `Rounded` under a stated rounding,
    /// `Round` / `Floor` / `Ceil` as their scale-0 specialisations, and four refusals — a decimal
    /// `Div`, a decimal beside a float, a float `Rounded`, and a scale past `1000`.
    let private decimalShapeNames =
        [ "decimal-filter"
          "decimal-project"
          "decimal-derive-exact"
          "decimal-quotient"
          "decimal-rounded-mod-abs"
          "decimal-round-floor-ceil"
          "decimal-cast"
          "decimal-group-agg"
          "decimal-join"
          "decimal-window"
          "decimal-pivot"
          "decimal-unpivot"
          "decimal-sort"
          "decimal-distinct"
          "decimal-limit"
          "decimal-union"
          "decimal-intersect"
          "decimal-except"
          "decimal-in-list"
          "decimal-div-refused"
          "decimal-float-refused"
          "decimal-rounded-float-refused"
          "decimal-quotient-scale-refused" ]

    /// Every shape a vector is rendered for, in the order the iterations take them.
    let shapeNames = baseShapeNames @ decimalShapeNames

    /// The sample size: the sixteen base draws, then one draw per decimal shape.
    let iterations = baseIterations + List.length decimalShapeNames

    /// The shape iteration `i` takes.
    let shapeOf (i: int) : string =
        if i < baseIterations then
            List.item (i % List.length baseShapeNames) baseShapeNames
        else
            List.item (i - baseIterations) decimalShapeNames

    let private agg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

    /// A decimal cell from a count of hundredths, through the canonicalising constructor.
    let private cents (c: int) : Cell =
        let a = abs c

        let text =
            (if c < 0 then "-" else "")
            + string (a / 100)
            + "."
            + (string (a % 100)).PadLeft(2, '0')

        Cell.decimal text |> Option.defaultValue Null

    /// The decimal draw's table (Phase 277): a tie-heavy string key, a decimal column carrying
    /// nulls, an int column and a float column.
    let private decimalTable (rows: int) (offset: int) : Table =
        let keys = [| "a"; "b"; "c" |]
        let g = [ for i in 0 .. rows - 1 -> Str keys[i % 3] ]
        let v = [ for i in 0 .. rows - 1 -> Int(i * 2 - offset) ]
        let w = [ for i in 0 .. rows - 1 -> Float(float (i + offset) / 4.0) ]

        let m =
            [ for i in 0 .. rows - 1 ->
                  if (i + offset) % 4 = 3 then
                      Null
                  else
                      cents ((i * 137 + offset * 25) % 500 - 200) ]

        { Schema = [ "g", StringType; "m", DecimalType; "v", IntType; "w", FloatType ]
          Columns =
            [ Column.create "g" StringType g
              Column.create "m" DecimalType m
              Column.create "v" IntType v
              Column.create "w" FloatType w ] }

    let private decimalPipelineOf (shape: string) (rows: int) (offset: int) : Transform list =
        let m = Col "m"

        let dec (t: string) =
            Lit(Cell.decimal t |> Option.defaultValue Null)
        // The second operand of a join or a set verb: the same table less its last row, so the two
        // share every row but one.
        let other = Embedded(decimalTable (rows - 1) offset)

        match shape with
        | "decimal-filter" -> [ Filter(Binary(Ge, m, dec "0.5")) ]
        | "decimal-project" -> [ Project [ "m", "amount"; "g", "g" ] ]
        | "decimal-derive-exact" ->
            [ Derive("t", Binary(Sub, Binary(Add, Binary(Mul, m, Lit(Int 3)), Col "v"), dec "0.05")) ]
        | "decimal-quotient" ->
            [ Derive(
                  "q",
                  Quotient(
                      m,
                      Lit(Int 3),
                      { Scale = Slot.Lit 4
                        Mode = RoundingMode.HalfEven }
                  )
              ) ]
        | "decimal-rounded-mod-abs" ->
            [ Derive(
                  "r",
                  Rounded(
                      Binary(Mod, ApplyFn(Abs, [ m ]), dec "0.7"),
                      { Scale = Slot.Lit 1
                        Mode = RoundingMode.HalfUp }
                  )
              ) ]
        | "decimal-round-floor-ceil" ->
            [ Derive("r", ApplyFn(Round, [ m ]))
              Derive("lo", ApplyFn(Floor, [ m ]))
              Derive("hi", ApplyFn(Ceil, [ m ])) ]
        | "decimal-cast" -> [ Derive("c", Cast(DecimalType, Col "w")); Derive("f", Cast(FloatType, m)) ]
        | "decimal-group-agg" ->
            [ GroupBy(
                  [ "g" ],
                  [ agg "s" Sum "m"
                    agg "lo" Min "m"
                    agg "hi" Max "m"
                    agg "k" CountDistinct "m"
                    agg "mean" Mean "m" ]
              ) ]
        | "decimal-join" -> [ Join(other, [ "m", "m" ], Inner) ]
        | "decimal-window" ->
            [ Window
                  { PartitionBy = [ "g" ]
                    OrderBy = [ "v", Asc ]
                    Fn = CumulSum
                    Of = "m"
                    As = "run" } ]
        | "decimal-pivot" ->
            [ Pivot
                  { Index = [ "v" ]
                    On = "g"
                    Values = "m"
                    Agg = Sum } ]
        | "decimal-unpivot" -> [ Unpivot([ "g" ], [ "m" ]) ]
        | "decimal-sort" -> [ Transform.sortBy [ "m", Desc ] ]
        | "decimal-distinct" -> [ Project [ "m", "m" ]; Distinct ]
        | "decimal-limit" -> [ Transform.sortBy [ "m", Asc ]; Transform.limit 2 1 ]
        | "decimal-union" -> [ Union other ]
        | "decimal-intersect" -> [ Intersect other ]
        | "decimal-except" -> [ Except other ]
        // The items: the decimal the table holds at row 1, an int, and a decimal it does not hold.
        | "decimal-in-list" ->
            [ Filter(InList(m, [ cents ((137 + offset * 25) % 500 - 200) |> Lit; Lit(Int 2); dec "9.99" ])) ]
        | "decimal-div-refused" -> [ Derive("q", Binary(Div, m, Lit(Int 2))) ]
        | "decimal-float-refused" -> [ Derive("x", Binary(Add, m, Col "w")) ]
        | "decimal-rounded-float-refused" ->
            [ Derive(
                  "r",
                  Rounded(
                      Col "w",
                      { Scale = Slot.Lit 2
                        Mode = RoundingMode.HalfEven }
                  )
              ) ]
        | _ ->
            [ Derive(
                  "q",
                  Quotient(
                      m,
                      Lit(Int 3),
                      { Scale = Slot.Lit 1001
                        Mode = RoundingMode.Down }
                  )
              ) ]

    let private pipelineOf (k: int) : Transform list =
        match k with
        | 0 -> [ Filter(Binary(Gt, Col "v", Lit(Int 0))) ]
        | 1 -> [ Transform.sortBy [ "g", Asc; "v", Asc ]; Distinct ]
        | 2 -> [ Derive("d", Binary(Add, Col "v", Col "w")) ]
        | 3 -> [ GroupBy([ "g" ], [ agg "s" Sum "v"; agg "n" Count "v"; agg "m" Mean "w" ]) ]
        | 4 -> [ Transform.limit 2 1 ]
        | 5 -> [ Derive("q", Binary(Div, Col "v", Lit(Int 0))) ]
        | 6 -> [ Derive("r", Binary(Div, Col "w", Lit(Float 3.0))) ]
        | _ -> [ Filter(Binary(Gt, Col "nope", Lit(Int 0))) ]

    /// One drawn sample: a three-column table (a tie-heavy string key, an int column carrying
    /// nulls, a float column) and the shape for this iteration. The shape is the iteration index
    /// modulo eight rather than a draw, deliberately: a drawn shape can be missed over sixteen
    /// iterations, and a corpus artefact that silently stopped carrying a shape would leave a host
    /// certifying less than the file claims. The TABLE is drawn, so the eight shapes are exercised
    /// over different data on each pass.
    let private baseSample (iteration: int) (extra: int) (offset: int) : Table * Transform list =
        let rows = extra + 2

        let groupKeys = [| "a"; "b"; "c" |]
        let g = [ for i in 0 .. rows - 1 -> Str groupKeys[i % 3] ]

        let v =
            [ for i in 0 .. rows - 1 ->
                  if (i + offset) % 4 = 0 then
                      Null
                  else
                      Int(i * 3 + offset - 5) ]

        let w = [ for i in 0 .. rows - 1 -> Float(float (i + offset) / 2.0) ]

        let table: Table =
            { Schema = [ "g", StringType; "v", IntType; "w", FloatType ]
              Columns =
                [ Column.create "g" StringType g
                  Column.create "v" IntType v
                  Column.create "w" FloatType w ] }

        table, pipelineOf (iteration % List.length baseShapeNames)

    /// One drawn sample. The first sixteen iterations are the base shapes above; from there each
    /// takes the next decimal shape (Phase 277) over the decimal table, with three rows or more so
    /// that a present decimal exists for each refusal to meet. Both read the same two draws, so the
    /// base vectors are the bytes they were before the decimal shapes were appended.
    let gen (iteration: int) (rng: ConfRng.T) : (Table * Transform list) * ConfRng.T =
        let extra, r1 = ConfRng.intBelow 4 rng
        let offset, r2 = ConfRng.intBelow 7 r1

        if iteration < baseIterations then
            baseSample iteration extra offset, r2
        else
            let rows = extra + 3
            (decimalTable rows offset, decimalPipelineOf (shapeOf iteration) rows offset), r2

    /// The generator in the shape `Conformance.transformLaws` takes — the iteration counter folded
    /// into the state, so the law and the export draw the identical sample.
    let lawGen () : ConfRng.T -> (Table * Transform list) * ConfRng.T =
        let mutable i = -1

        fun rng ->
            i <- i + 1
            gen i rng

    // -----------------------------------------------------------------------
    //  a small deterministic JSON renderer
    // -----------------------------------------------------------------------
    //  Hand-rolled rather than `Utf8JsonWriter`, for two reasons, both about the artefact being an
    //  ORACLE rather than merely valid JSON. The writer's indented mode emits
    //  `Environment.NewLine`, so the same run would produce different bytes on Windows and Linux.
    //  And its default string encoder escapes every character outside a conservative HTML-safe set
    //  — backticks, `+`, apostrophes and em-dashes all become `\uXXXX` — which is a
    //  framework-version-dependent choice this corpus should not inherit. The escaper below is the
    //  JSON minimum and nothing more: the two structural characters, the named short escapes, and
    //  the control range. Everything else is written as itself, in UTF-8.

    let private jstr (s: string) : string =
        let sb = StringBuilder()
        sb.Append('"') |> ignore

        for ch in s do
            match ch with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\b' -> sb.Append("\\b") |> ignore
            | '\f' -> sb.Append("\\f") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when c < ' ' -> sb.AppendFormat("\\u{0:x4}", int c) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.Append('"') |> ignore
        sb.ToString()

    let private jint (n: int) : string = string n

    let private jobj (members: (string * string) list) : string =
        "{ "
        + (members |> List.map (fun (k, v) -> jstr k + ": " + v) |> String.concat ", ")
        + " }"

    /// The version of the reference that produced the answers, read from the assembly rather than a
    /// literal: the version decides what the reference answers, so a file naming it from a literal
    /// could describe a reference that is not the one that produced the vectors. The assembly is
    /// `Fuaran.Compute.DataFrame`'s — the evaluator the vectors record, built from THIS repository at
    /// its `<Version>` (Phase 259; until the split it was read off the conformance kit's assembly,
    /// which shipped at the same number). The `+<sha>` build metadata is dropped — it moves with
    /// every build, and the committed artefact must be stable across rebuilds of the same version.
    let kitVersion () : string =
        let asm = typeof<Transform>.Assembly

        match asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
        | null -> string (asm.GetName().Version)
        | attr -> attr.InformationalVersion.Split('+')[0]

    // -----------------------------------------------------------------------
    //  the vectors
    // -----------------------------------------------------------------------

    /// A single exported vector: what a host is given, and what the reference actually answered.
    type Vector =
        { Id: string
          Case: string
          Input: (string * string) list
          Expected: (string * string) list }

    let private renderVector (v: Vector) : string =
        jobj
            [ "id", jstr v.Id
              "case", jstr v.Case
              "input", jobj v.Input
              "expected", jobj v.Expected ]

    /// The reference's answer, in the terms the parity law actually compares. An `Ok` carries the
    /// canonical wire string of the result table — the identity `transformLaws` asserts. An
    /// `Error` carries the verdict and NOTHING ELSE, because the law requires only that the host
    /// also refused; it does not compare which refusal, and a vector naming one would invite a host
    /// to gate on agreement the contract has never claimed.
    let private expectedOf (r: Result<Table, EvalError>) : (string * string) list =
        match r with
        | Ok t -> [ "verdict", jstr "ok"; "table", jstr (ColumnCodec.encode (Embedded t)) ]
        | Error _ -> [ "verdict", jstr "error" ]

    let allVectors () : Vector list =
        let mutable rng = ConfRng.ofSeed seed

        [ for i in 0 .. iterations - 1 do
              let (table, pipeline), r' = gen i rng
              rng <- r'

              yield
                  { Id = sprintf "transform-%d-%s" i (shapeOf i)
                    Case = "evalPipeline"
                    Input =
                      [ "pipeline", jstr (DataFrameCodec.encodePipeline pipeline)
                        "source", jstr (ColumnCodec.encode (Embedded table)) ]
                    Expected = expectedOf (DataFrame.evalPipeline pipeline table) } ]

    // -----------------------------------------------------------------------
    //  Phase 321 — the decimal through the columnar op wire and the delta wire
    // -----------------------------------------------------------------------
    //
    // Two further case kinds, appended AFTER the evalPipeline vectors so those stay byte-identical.
    // Each is a reference answer computed by calling the reference, as the evalPipeline ones are:
    //
    //   * `columnOp` — `input.source` an embedded table and `input.op` a `ColumnOp` wire string; a
    //     host decodes both, applies the op, and compares: an `ok` vector requires the result table
    //     to encode to `expected.table` byte for byte, an `error` vector requires the host to refuse
    //     the op too (unnamed, as above). Every vector also records `expected.op`, the op's wire
    //     string after a decode and re-encode, which a host's op codec must reproduce.
    //   * `delta` — `input.before` / `input.after` two embedded tables and `input.key` the column
    //     whose cell is each row's identity; a host diffs them by that identity and encodes the
    //     delta, which must equal `expected.delta` byte for byte (a decimal key renders as its
    //     canonical `m:` token).

    /// A delta vector's table: a unique decimal identity `id` and a decimal measure `m`.
    let private keyedMoney (ids: string list) (amounts: Cell list) : Table =
        let dec (text: string) =
            Cell.decimal text |> Option.defaultValue Null

        { Schema = [ "id", DecimalType; "m", DecimalType ]
          Columns =
            [ Column.create "id" DecimalType (ids |> List.map dec)
              Column.create "m" DecimalType amounts ] }

    let private columnOpCases: (string * ColumnOp) list =
        [ "setCell-decimal", SetCell("m", 0, cents 1999)
          "setCell-int-into-decimal", SetCell("m", 1, Int 7)
          "setCell-int-into-float", SetCell("w", 0, Int 3)
          "setCell-float-into-decimal-refused", SetCell("m", 0, Float 1.5)
          "setCell-decimal-into-float-refused", SetCell("w", 0, cents 150)
          "setCell-decimal-into-int-refused", SetCell("v", 0, cents 100)
          "setColumn-decimal", SetColumn(Column.create "m" DecimalType [ cents -5; Null; Int 12; cents 100001 ])
          "insertColumn-decimal",
          InsertColumn(4, Column.create "fee" DecimalType [ cents 5; cents 0; Null; cents -125 ])
          "appendRows-decimal",
          AppendRows(
              [ [ "g", Str "d"; "m", cents 12345; "v", Int 9; "w", Float 0.5 ]
                [ "g", Str "e"; "m", Int 2; "v", Int 10; "w", Float 1.5 ] ]
          ) ]

    let private deltaCases: (string * Table * Table) list =
        let ids = [ "0.5"; "1.25"; "2"; "10.75" ]
        let amounts = [ cents 150; cents -2999; cents 0; Null ]
        let before = keyedMoney ids amounts

        [ "decimal-edit", before, keyedMoney ids [ cents 150; cents -3000; cents 0; Null ]
          "decimal-key-removed-and-added",
          before,
          keyedMoney [ "0.5"; "1.25"; "10.75"; "11.5" ] [ cents 150; cents -2999; Null; cents 1 ]
          "null-filled-with-a-decimal", before, keyedMoney ids [ cents 150; cents -2999; cents 0; cents 1 ]
          "quiet", before, before ]

    /// The columnar op and delta vectors, in a fixed order after the evalPipeline ones.
    let wireVectors () : Vector list =
        let source = decimalTable 4 1

        let opVectors =
            columnOpCases
            |> List.mapi (fun i (name, op) ->
                let wire = ColumnOps.encode op

                let reEncoded =
                    match ColumnOps.decode wire with
                    | Ok back -> ColumnOps.encode back
                    | Error m -> failwithf "column-op vector %s did not decode: %s" name m

                { Id = sprintf "column-op-%d-%s" i name
                  Case = "columnOp"
                  Input = [ "op", jstr wire; "source", jstr (ColumnCodec.encode (Embedded source)) ]
                  Expected =
                    (match ColumnOps.apply op source with
                     | Ok t -> [ "verdict", jstr "ok"; "table", jstr (ColumnCodec.encode (Embedded t)) ]
                     | Error _ -> [ "verdict", jstr "error" ])
                    @ [ "op", jstr reEncoded ] })

        let deltaVectors =
            deltaCases
            |> List.mapi (fun i (name, before, after) ->
                let d =
                    match Delta.diff (RowIdentity.byColumn "id") before after with
                    | Ok d -> d
                    | Error e -> failwithf "delta vector %s did not diff: %A" name e

                { Id = sprintf "delta-%d-%s" i name
                  Case = "delta"
                  Input =
                    [ "after", jstr (ColumnCodec.encode (Embedded after))
                      "before", jstr (ColumnCodec.encode (Embedded before))
                      "key", jstr "id" ]
                  Expected = [ "verdict", jstr "ok"; "delta", jstr (DeltaCodec.encode d) ] })

        opVectors @ deltaVectors

    // -----------------------------------------------------------------------
    //  the rendered artefact
    // -----------------------------------------------------------------------

    let private description =
        "The reference answers Fuaran.Compute.DataFrameConformance.transformLaws compares a host evaluator "
        + "against, over a sample declared by `seed` and `iterations` and computed by calling "
        + "Fuaran.Compute.DataFrame.evalPipeline. `input.source` is a canonical DataSource wire string "
        + "(an Embedded table) and `input.pipeline` a canonical Transform pipeline wire string — "
        + "both decode with the host's existing dataframe codec; no new codec is needed. A host "
        + "runs the family by decoding both, evaluating the pipeline with ITS OWN evaluator, and "
        + "comparing: an `ok` vector requires the host's result table to encode byte-for-byte to "
        + "`expected.table`, and an `error` vector requires the host to refuse the pipeline too. "
        + "The refusal is deliberately unnamed — the parity contract compares that both sides "
        + "errored, never which error, so a host must not gate on a class this file does not carry. "
        + "To reproduce the sample rather than replay it: per iteration draw intBelow(4) = extra "
        + "(rows = extra + 2) and intBelow(7) = offset, then build columns g = Str of "
        + "[a;b;c][i mod 3], v = Null when (i + offset) mod 4 = 0 else Int(i*3 + offset - 5), and "
        + "w = Float((i + offset) / 2). The pipeline shape is the iteration index modulo eight over "
        + "the shapes named in each vector id — a fixed cycle rather than a draw, so no shape can be "
        + "missed. From iteration 16 each iteration takes the next DECIMAL shape named in its id over "
        + "the same two draws, with rows = extra + 3 and columns g as above; m = Decimal, null when "
        + "(i + offset) mod 4 = 3, else ((i*137 + offset*25) mod 500 - 200) hundredths; v = "
        + "Int(i*2 - offset); w = Float((i + offset) / 4). A second operand table, where a shape "
        + "takes one, is the same build over rows - 1 rows. These are BEHAVIOUR vectors: a host asserts the encoded result, not the framing "
        + "of this file. "
        + "After the evalPipeline vectors come two further case kinds (Phase 321), each a reference "
        + "answer over hand-declared inputs: `columnOp` vectors carry `input.source` (an embedded table) "
        + "and `input.op` (a canonical ColumnOp wire string); a host applies the op and compares as for "
        + "evalPipeline (`expected.table` on `ok`, a refusal on `error`), and re-encodes its decode of "
        + "`input.op` to `expected.op`. `delta` vectors carry `input.before`, `input.after` and "
        + "`input.key`; a host diffs the two tables keyed by that column's cell token and encodes the "
        + "delta to `expected.delta`. `iterations` counts the evalPipeline vectors only."

    let renderTransformVectors () : string =
        let sb = StringBuilder()
        let line (s: string) = sb.Append(s).Append('\n') |> ignore

        line "{"
        line ("  \"family\": " + jstr "transformLaws" + ",")
        line ("  \"kitVersion\": " + jstr (kitVersion ()) + ",")
        line ("  \"seed\": " + jint seed + ",")
        line ("  \"iterations\": " + jint iterations + ",")
        line ("  \"description\": " + jstr description + ",")
        line "  \"vectors\": ["

        let rendered = allVectors () @ wireVectors () |> List.map renderVector
        let last = List.length rendered - 1

        rendered
        |> List.iteri (fun i v -> line ("    " + v + (if i = last then "" else ",")))

        line "  ]"
        line "}"
        sb.ToString()

    // -----------------------------------------------------------------------
    //  writing
    // -----------------------------------------------------------------------

    let familyDir (corpusDir: string) : string = Path.Combine(corpusDir, familyDirName)

    let transformPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, transformFileName)

    /// Every law set this repository emits, as (path under `corpusDir`, rendered bytes) — the one
    /// list `write` walks, so a family added here cannot be rendered and then forgotten by the
    /// writer. One set: the transform-parity family's reference answers. (The substrate's own
    /// `capabilityLaws` vectors are emitted by the substrate, beside the law they certify.)
    let emitted (corpusDir: string) : (string * string) list =
        [ transformPath corpusDir, renderTransformVectors () ]

    /// Write the vectors with LF endings, whatever the host platform — the corpus is byte-compared
    /// by several hosts on three operating systems.
    ///
    /// The family MANIFEST beside them is deliberately not written here. It indexes every family in
    /// `laws/`, and a wholesale renderer would silently drop whatever it does not know about.
    /// Moving a family between its `families` and `notExported` lists is an edit to a shared index,
    /// made once.
    let write (corpusDir: string) : unit =
        Directory.CreateDirectory(familyDir corpusDir) |> ignore

        for path, text in emitted corpusDir do
            File.WriteAllText(path, text)
