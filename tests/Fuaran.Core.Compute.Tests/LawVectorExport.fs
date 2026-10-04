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

    /// The declared sample (Phase 342: its definitions moved to `TransformVectorSample`, which the
    /// node harness compiles; these names are kept so every reader of them is unchanged).
    let seed = TransformVectorSample.seed

    let shapeNames = TransformVectorSample.shapeNames

    let iterations = TransformVectorSample.iterations

    let shapeOf (i: int) : string = TransformVectorSample.shapeOf i

    let private cents (c: int) : Cell = TransformVectorSample.cents c

    let private decimalTable (rows: int) (offset: int) : Table =
        TransformVectorSample.decimalTable rows offset

    let gen (iteration: int) (rng: ConfRng.T) : (Table * Transform list) * ConfRng.T =
        TransformVectorSample.gen iteration rng

    let lawGen () : ConfRng.T -> (Table * Transform list) * ConfRng.T = TransformVectorSample.lawGen ()
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
        + "delta to `expected.delta`. `iterations` counts the evalPipeline vectors only. From the "
        + "iteration after the last decimal shape, each takes the next TYPING shape named in its vector "
        + "id (Phase 338) over the decimal table: a derived or unpivoted column is typed by its "
        + "expression where the schema decides it, over an empty and an all-null frame alike, and a "
        + "float beside a decimal in one derived column is refused."

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
