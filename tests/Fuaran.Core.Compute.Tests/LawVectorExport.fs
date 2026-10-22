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
    /// Every member is a cell of the substrate's one corpus writer (`CorpusFile`), which decides the
    /// file's every byte — its escaping, its spacing and its line layout — so this producer writes
    /// no JSON of its own.
    type Vector =
        { Id: string
          Case: string
          Input: (string * CorpusCell) list
          Expected: (string * CorpusCell) list }

    let private str = CorpusFile.str

    /// One vector as one item line: its members in this order, `input` and `expected` spaced objects.
    let private vectorCell (v: Vector) : CorpusCell =
        CorpusCell.Row
            [ "id", str v.Id
              "case", str v.Case
              "input", CorpusCell.Row v.Input
              "expected", CorpusCell.Row v.Expected ]

    /// The reference's answer, in the terms the parity law actually compares. An `Ok` carries the
    /// canonical wire string of the result table — the identity `transformLaws` asserts. An
    /// `Error` carries the verdict and NOTHING ELSE, because the law requires only that the host
    /// also refused; it does not compare which refusal, and a vector naming one would invite a host
    /// to gate on agreement the contract has never claimed.
    let private expectedOf (r: Result<Table, EvalError>) : (string * CorpusCell) list =
        match r with
        | Ok t -> [ "verdict", str "ok"; "table", str (ColumnCodec.encode (Embedded t)) ]
        | Error _ -> [ "verdict", str "error" ]

    /// The cross-host bound on a host-rounded column, in ulps (Phase 557, `DECISIONS.md` D43): what
    /// a certifying host compares a `HostRounded` column within, where a pinned column is compared
    /// byte for byte.
    let hostRoundedUlps = 2

    /// The columns of a pipeline's output the static walk marks `HostRounded` over the source's
    /// schema — the columns whose tolerance a vector carries.
    let hostRoundedColumns (table: Table) (pipeline: Transform list) : string list =
        SchemaWalk.ofPipeline table.Schema pipeline
        |> SchemaWalk.columns
        |> List.filter (fun c -> c.Exactness = Exactness.HostRounded)
        |> List.map _.Name

    /// `expectedOf`, with the per-column tolerance of an accepted pipeline's host-rounded columns
    /// (Phase 557): absent where the walk marks none, so every vector without one is the bytes it was.
    let private expectedWithTolerance (table: Table) (pipeline: Transform list) : (string * CorpusCell) list =
        let expected = expectedOf (DataFrame.evalPipeline pipeline table)

        match hostRoundedColumns table pipeline with
        | [] -> expected
        | rounded ->
            expected
            @ [ "tolerance", CorpusCell.Row(rounded |> List.map (fun name -> name, CorpusFile.num hostRoundedUlps)) ]

    let allVectors () : Vector list =
        let mutable rng = ConfRng.ofSeed seed

        [ for i in 0 .. iterations - 1 do
              let (table, pipeline), r' = gen i rng
              rng <- r'

              yield
                  { Id = sprintf "transform-%d-%s" i (shapeOf i)
                    Case = "evalPipeline"
                    Input =
                      [ "pipeline", str (DataFrameCodec.encodePipeline pipeline)
                        "source", str (ColumnCodec.encode (Embedded table)) ]
                    Expected = expectedWithTolerance table pipeline } ]

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

        { Schema = [ Field.create "id" DecimalType; Field.create "m" DecimalType ]
          Columns =
            [ KitColumn.create "id" DecimalType (ids |> List.map dec)
              KitColumn.create "m" DecimalType amounts ] }

    let private columnOpCases: (string * ColumnOp) list =
        [ "setCell-decimal", SetCell("m", 0, cents 1999)
          "setCell-int-into-decimal", SetCell("m", 1, Int 7)
          "setCell-int-into-float", SetCell("w", 0, Int 3)
          "setCell-float-into-decimal-refused", SetCell("m", 0, Float 1.5)
          "setCell-decimal-into-float-refused", SetCell("w", 0, cents 150)
          "setCell-decimal-into-int-refused", SetCell("v", 0, cents 100)
          "setColumn-decimal", SetColumn(KitColumn.create "m" DecimalType [ cents -5; Null; Int 12; cents 100001 ])
          "insertColumn-decimal",
          InsertColumn(4, KitColumn.create "fee" DecimalType [ cents 5; cents 0; Null; cents -125 ])
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
                  Input = [ "op", str wire; "source", str (ColumnCodec.encode (Embedded source)) ]
                  Expected =
                    (match ColumnOps.apply op source with
                     | Ok t -> [ "verdict", str "ok"; "table", str (ColumnCodec.encode (Embedded t)) ]
                     | Error _ -> [ "verdict", str "error" ])
                    @ [ "op", str reEncoded ] })

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
                    [ "after", str (ColumnCodec.encode (Embedded after))
                      "before", str (ColumnCodec.encode (Embedded before))
                      "key", str "id" ]
                  Expected = [ "verdict", str "ok"; "delta", str (DeltaCodec.encode d) ] })

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
        + "float beside a decimal in one derived column is refused. From the iteration after the last "
        + "typing shape, each takes the next UNIT shape named in its vector id (Phase 429) over the unit "
        + "table — rows = extra + 3; g as above; d = Int((i+1)*100 + offset*10) in metres (label "
        + "Distance), null when (i + offset) mod 4 = 3; t = Float((i+1+offset)*2.5) in seconds, null when "
        + "(i + offset) mod 5 = 4; h = Float(i + offset + 0.5) in seconds; w = Float((i*3 + offset)/4) in "
        + "kilograms; dk = d/1000 as Float in kilometres; n = Int(i + offset), unitless, label Count, "
        + "description How many; the second operand of a join or a set verb is the same build over "
        + "rows - 1 rows (the union refusal's declares d in kilometres): a column's unit, label and "
        + "description ride every verb that keeps a column, a derive carries the unit the check derives "
        + "and a conversion resolves to a float, an aggregate and a window keep their source's unit or "
        + "carry none, and two units met where the rule requires one are refused. From the iteration "
        + "after the last unit shape, each takes the next REPEAT shape named in its vector id (Phase 468) "
        + "over the decimal table: a project, a group-by, an unpivot and a pivot whose output would name "
        + "one column twice are refused. From the iteration after the last repeat shape, each takes the "
        + "next POWER shape named in its vector id (Phase 557) over the decimal table. An integer-exponent "
        + "`pow` is BYTE-EXACT on every host — one pinned routine, binary exponentiation over IEEE-754 "
        + "multiplication with one reciprocal for a negative exponent, equal bit for bit to the "
        + "repeated-multiplication spelling up to the cube — and so is every row of the special-value "
        + "table (`pow(0,0)`, `pow(1,y)`, `pow(0,negative)`, a negative base under a non-integer exponent, "
        + "`ln` at and below zero, `ln 1`, `exp` underflow); the special-value vector carries the bound "
        + "below because its columns read host-rounded functions, and what it pins is exact all the "
        + "same — no wrong special value lies within the bound of the right one. "
        + "A float-exponent `pow`, `exp`, `ln` and `log10` are HOST-ROUNDED: within one host deterministic, "
        + "across hosts within a bound. Such a vector carries `expected.tolerance`, an object naming each "
        + "output column the reference's static walk marks host-rounded with the bound in ULPS a host's "
        + "value may sit from the recorded one, the distance between two doubles being the difference of "
        + "their IEEE-754 binary64 bit patterns read as ordered integers (a negative double's pattern "
        + "reflected below zero, so adjacent doubles are one apart). A host "
        + "compares a named column cell by cell — one type, one length, null in the same rows, each pair "
        + "of present nonzero finite floats of one sign within the bound, a zero of either sign, a "
        + "non-finite float and any other cell equal exactly — and the rest of the table byte "
        + "for byte: its answer with every named column replaced by the recorded one encodes to "
        + "`expected.table` exactly (Phase 559). A host that does not implement the four functions reports "
        + "the vector as unsupported and never skips it silently. A pinned column is never named, so a "
        + "vector without the member is compared exactly as before."

    let renderTransformVectors () : string =
        CorpusFile.render
            { Head =
                [ "family", str "transformLaws"
                  "kitVersion", str (kitVersion ())
                  "seed", CorpusFile.num seed
                  "iterations", CorpusFile.num iterations
                  "description", str description ]
              Items = "vectors"
              Rows = allVectors () @ wireVectors () |> List.map vectorCell }

    // -----------------------------------------------------------------------
    //  Phase 1938 — the dataframeParity family: named feature cases, computed answers
    // -----------------------------------------------------------------------
    //
    // The transform sample above is DRAWN; this family is AUTHORED — one named (source, pipeline)
    // pair per evaluator feature (joins, windows, pivots, the apply functions, …), the coverage a
    // drawn sample reaches only by luck. Only the inputs are authored, in the test project's
    // `fixtures/dataframe-parity-inputs.json`; every answer is computed here by calling the
    // reference, exactly as the drawn vectors' are, and each input is written as the reference's
    // own re-encoding of its decode, so a host can also assert that its codec reproduces it.

    let parityFileName = "dataframe-parity.json"

    let private parityInputsFile () : string =
        Snapshots.repoFile (
            Path.Combine("tests", "Fuaran.Core.Compute.Tests", "fixtures", "dataframe-parity-inputs.json")
        )

    let private member' (name: string) (v: JVal) : string =
        match v with
        | JObj ms ->
            match ms |> List.tryPick (fun (k, x) -> if k = name then Some x else None) with
            | Some(JStr s) -> s
            | _ -> failwithf "dataframe-parity input: member '%s' is missing or not a string" name
        | _ -> failwith "dataframe-parity input: a case is not an object"

    let parityVectors () : Vector list =
        let cases =
            match Json.parse (File.ReadAllText(parityInputsFile ())) with
            | Ok(JObj ms) ->
                match ms |> List.tryPick (fun (k, v) -> if k = "cases" then Some v else None) with
                | Some(JArr items) -> items
                | _ -> failwith "dataframe-parity inputs carry no `cases` array"
            | Ok _ -> failwith "dataframe-parity inputs are not an object"
            | Error m -> failwithf "dataframe-parity inputs did not parse: %s" m

        [ for case in cases do
              let id = member' "id" case

              match
                  DataFrameCodec.decodePipeline (member' "pipeline" case), ColumnCodec.decode (member' "source" case)
              with
              | Ok pipeline, Ok(Embedded table) ->
                  yield
                      { Id = id
                        Case = "evalPipeline"
                        Input =
                          [ "pipeline", str (DataFrameCodec.encodePipeline pipeline)
                            "source", str (ColumnCodec.encode (Embedded table)) ]
                        Expected = expectedOf (DataFrame.evalPipeline pipeline table) }
              | Error e, _ ->
                  failwithf "dataframe-parity %s: pipeline did not decode (%s)" id (ColumnCodec.errorString e)
              | _, Error e -> failwithf "dataframe-parity %s: source did not decode (%s)" id (ColumnCodec.errorString e)
              | _, Ok _ -> failwithf "dataframe-parity %s: source is not an embedded table" id ]

    let private parityDescription =
        "The reference answers for the AUTHORED dataframe-parity cases: one named (source, pipeline) pair per "
        + "evaluator feature, each answer computed by calling Fuaran.Compute.DataFrame.evalPipeline. The vector "
        + "shape is transformLaws' own: `input.source` is a canonical DataSource wire string (an embedded "
        + "table) and `input.pipeline` a canonical Transform-pipeline wire string, each the reference's "
        + "re-encoding of its own decode, so a host asserts that its codec reproduces both byte for byte; "
        + "an `ok` vector's `expected.table` is the result table's canonical wire string, and an `error` "
        + "vector requires the host to refuse the pipeline too, without naming the refusal."

    let renderParityVectors () : string =
        CorpusFile.render
            { Head = [ "family", str "dataframeParity"; "description", str parityDescription ]
              Items = "vectors"
              Rows = parityVectors () |> List.map vectorCell }

    // -----------------------------------------------------------------------
    //  writing
    // -----------------------------------------------------------------------

    let familyDir (corpusDir: string) : string = Path.Combine(corpusDir, familyDirName)

    let transformPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, transformFileName)

    let parityPath (corpusDir: string) : string =
        Path.Combine(familyDir corpusDir, parityFileName)

    /// Every law set this repository emits, as (path under `corpusDir`, rendered bytes) — the one
    /// list `write` walks, so a family added here cannot be rendered and then forgotten by the
    /// writer. Two sets: the transform-parity family's drawn reference answers, and the
    /// dataframeParity family's answers over its authored cases. (The substrate's own
    /// `capabilityLaws` vectors are emitted by the substrate, beside the law they certify.)
    let emitted (corpusDir: string) : (string * string) list =
        [ transformPath corpusDir, renderTransformVectors ()
          parityPath corpusDir, renderParityVectors () ]

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
