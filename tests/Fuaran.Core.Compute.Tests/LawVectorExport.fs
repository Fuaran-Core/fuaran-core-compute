namespace Fuaran.Core.Tests

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
//  `Fuaran.Core.DataFrame.evalPipeline` byte-for-byte. Running it HERE with the
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

    /// The family directory inside the shared corpus and the artefact in it. The directory name is
    /// the interface — hosts resolve `laws/` — so it is named once here.
    let familyDirName = "laws"
    let transformFileName = "transform-laws.json"

    /// The seed and sample size the exported vectors are drawn from. Declared rather than taken
    /// from a test's own law invocation, because a host re-running the family must be able to
    /// reproduce the sample from the file alone.
    let seed = 20260904

    /// Two draws of each of the eight pipeline shapes below. Far fewer than a law run: each vector
    /// carries three whole wire strings, and a host does not need a hundred draws of the same eight
    /// shapes to disagree with the reference.
    let iterations = 16

    // -----------------------------------------------------------------------
    //  the declared sample generator
    // -----------------------------------------------------------------------

    /// The eight pipeline shapes, chosen to reach the semantics the parity contract pins rather
    /// than to be numerous: null propagation through a predicate, sort/distinct stability over a
    /// tie-heavy key, int↔float coercion, group stability and aggregate null handling, an
    /// offset window, the pinned division-by-zero answer, the canonical float layout on a
    /// non-terminating quotient, and a pipeline the reference REFUSES — which is a parity case in
    /// its own right, since the law requires the host to refuse it too.
    let shapeNames =
        [ "filter"
          "sort-distinct"
          "derive-coerce"
          "group-agg"
          "limit"
          "div-by-zero"
          "float-divide"
          "unknown-column" ]

    let private agg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

    let private pipelineOf (k: int) : Transform list =
        match k with
        | 0 -> [ Filter(Binary(Gt, Col "v", Lit(Int 0))) ]
        | 1 -> [ Sort [ "g", Asc; "v", Asc ]; Distinct ]
        | 2 -> [ Derive("d", Binary(Add, Col "v", Col "w")) ]
        | 3 -> [ GroupBy([ "g" ], [ agg "s" Sum "v"; agg "n" Count "v"; agg "m" Mean "w" ]) ]
        | 4 -> [ Limit(2, 1) ]
        | 5 -> [ Derive("q", Binary(Div, Col "v", Lit(Int 0))) ]
        | 6 -> [ Derive("r", Binary(Div, Col "w", Lit(Float 3.0))) ]
        | _ -> [ Filter(Binary(Gt, Col "nope", Lit(Int 0))) ]

    /// One drawn sample: a three-column table (a tie-heavy string key, an int column carrying
    /// nulls, a float column) and the shape for this iteration. The shape is the iteration index
    /// modulo eight rather than a draw, deliberately: a drawn shape can be missed over sixteen
    /// iterations, and a corpus artefact that silently stopped carrying a shape would leave a host
    /// certifying less than the file claims. The TABLE is drawn, so the eight shapes are exercised
    /// over different data on each pass.
    let gen (iteration: int) (rng: ConfRng.T) : (Table * Transform list) * ConfRng.T =
        let extra, r1 = ConfRng.intBelow 4 rng
        let offset, r2 = ConfRng.intBelow 7 r1
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

        (table, pipelineOf (iteration % List.length shapeNames)), r2

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

    /// The pinned kit's version, read from the assembly rather than a literal: the version decides
    /// what the reference answers, so a file naming it from a literal could describe a kit that is
    /// not the one that produced the vectors. The `+<sha>` build metadata is dropped — it moves
    /// with every build, and the committed artefact must be stable across rebuilds of the same pin.
    let kitVersion () : string =
        let asm = typeof<LawResult>.Assembly

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
                  { Id = sprintf "transform-%d-%s" i (List.item (i % List.length shapeNames) shapeNames)
                    Case = "evalPipeline"
                    Input =
                      [ "pipeline", jstr (DataFrameCodec.encodePipeline pipeline)
                        "source", jstr (ColumnCodec.encode (Embedded table)) ]
                    Expected = expectedOf (DataFrame.evalPipeline pipeline table) } ]

    // -----------------------------------------------------------------------
    //  the rendered artefact
    // -----------------------------------------------------------------------

    let private description =
        "The reference answers Fuaran.Core.Conformance.transformLaws compares a host evaluator "
        + "against, over a sample declared by `seed` and `iterations` and computed by calling "
        + "Fuaran.Core.DataFrame.evalPipeline. `input.source` is a canonical DataSource wire string "
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
        + "missed. These are BEHAVIOUR vectors: a host asserts the encoded result, not the framing "
        + "of this file."

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

        let rendered = allVectors () |> List.map renderVector
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

    /// Write the vectors with LF endings, whatever the host platform — the corpus is byte-compared
    /// by several hosts on three operating systems.
    ///
    /// The family MANIFEST beside it is deliberately not written here. It indexes every family in
    /// `laws/`, of which this is one, and a second wholesale renderer would silently drop whatever
    /// the first one does not know about. Moving a family between its `families` and `notExported`
    /// lists is an edit to a shared index, made once.
    let write (corpusDir: string) : unit =
        Directory.CreateDirectory(familyDir corpusDir) |> ignore
        File.WriteAllText(transformPath corpusDir, renderTransformVectors ())
