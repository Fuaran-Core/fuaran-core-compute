namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.DataFrame.Conformance (Phase 257) — the law families over the
//  dataframe layer: the transform-parity, columnar op-algebra, incremental,
//  param, static-schema and pinned-clock families, and the `GroupBy` half of
//  aggregate parity. They moved here from `Fuaran.Core.Conformance` so that no
//  spine assembly references `Fuaran.Core.DataFrame` or `Fuaran.Core.Column.Ops`
//  (DECISIONS.md D66, D68). The rule is mechanical: a family that reads either
//  assembly ships from this package; everything else stays in the kit.
//
//  `DataFrameConformance` is the families' own home. A consumer that spells them
//  `Conformance.<family>` keeps compiling through the same-named forwarding module
//  in `Forwards.fs`, which is marked for removal in Phase 258.
//
//  FSharp.Core only, Fable-clean, like the kit it extends.
// ============================================================================

/// The law families over the dataframe layer (Phase 257). Each is the family that shipped in
/// `Fuaran.Core.Conformance` under the same name, unchanged except `aggregateParityLaws`, which
/// keeps its `GroupBy` parity law and leaves the null-skip law to `Conformance.aggregateNullSkipLaws`.
module DataFrameConformance =

    /// The dataframe-transform parity laws (Phase 29) — the teeth on a host evaluator's agreement
    /// with the `Fuaran.Core.DataFrame` reference. A domain supplies its own evaluator `under`
    /// (signature-identical to `DataFrame.evalPipeline`) and a generator of `(table, pipeline)`
    /// samples; the kit certifies, over a seed-replayable sample, that for every case the evaluator
    /// agrees with the reference **byte-for-byte**:
    ///
    ///  - both `Ok` and their result tables encode to the identical wire string (`ColumnCodec.encode`
    ///    — the cross-host parity contract: same null/coercion/order/float semantics), **or**
    ///  - both `Error` (the reference rejected the pipeline and so did the evaluator).
    ///
    /// Byte-identity (not value equality) is the contract because the parity gate compares wire
    /// output across hosts. Opt-in like `dagLaws` / `captureReplayLaws` — a domain that ships a
    /// `Transform` evaluator runs it alongside its base certification. `transformLaws` over the
    /// reference itself is the reference's own self-consistency check.
    let transformLaws
        (under: Transform list -> Table -> Result<Table, EvalError>)
        (gen: ConfRng.T -> (Table * Transform list) * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable parity = None
        let mutable totality = None
        // Phase 223 — the reference's outcome populations over the caller's DRAWN pipelines: the
        // Error/Error arm of the parity law is reached only when the reference REFUSES a pipeline.
        let mutable accepted = 0
        let mutable refused = 0

        for i in 0 .. iterations - 1 do
            let (table, pipeline), r' = gen rng
            rng <- r'

            let refResult =
                try
                    Ok(DataFrame.evalPipeline pipeline table)
                with ex ->
                    Error ex.Message

            let underResult =
                try
                    Ok(under pipeline table)
                with ex ->
                    Error ex.Message

            match refResult, underResult with
            | Error m, _
            | _, Error m ->
                // an evaluator that throws (rather than returning EvalError) breaks totality (GP4)
                if totality.IsNone then
                    totality <- Some(sprintf "seed=%d iter=%d: an evaluator threw: %s" seed i m)
            | Ok refR, Ok underR ->
                (match refR with
                 | Ok _ -> accepted <- accepted + 1
                 | Error _ -> refused <- refused + 1)

                let agree =
                    match refR, underR with
                    | Ok a, Ok b -> ColumnCodec.encode (Embedded a) = ColumnCodec.encode (Embedded b)
                    | Error _, Error _ -> true
                    | _ -> false

                if not agree && parity.IsNone then
                    parity <- Some(sprintf "seed=%d iter=%d: evaluator ≠ reference (pipeline=%A)" seed i pipeline)

        [ { Law = "host evaluator is byte-identical to the DataFrame reference"
            Passed = parity.IsNone
            Counterexample = parity }
          { Law = "evaluators are total (return EvalError, never throw)"
            Passed = totality.IsNone
            Counterexample = totality }
          // Phase 223 — `Guarded ["accepted"; "refused"]`, after the subject laws. A generator of
          // only well-formed pipelines certifies the Error/Error parity arm by nothing, and green.
          SampleAdequacy.reached "Conformance.transformLaws" "evaluated pipeline" seed [ "accepted", accepted ]
          SampleAdequacy.reached "Conformance.transformLaws" "refused pipeline" seed [ "refused", refused ] ]

    // ---- aggregate parity (Phase 36) ----
    // The teeth on `Column.aggregate` as the SINGLE source the DataFrame `GroupBy` calls: the public
    // surface must produce byte-identically what a single-group `GroupBy` produces. The null-skip
    // half reads `Column` alone and stays in `Fuaran.Core.Conformance` as `aggregateNullSkipLaws`
    // (Phase 257, D68).

    /// The aggregate-parity law (Phase 36; the `GroupBy` half since Phase 257). Self-contained — over
    /// a seed-replayable sample of random (int/float, null-bearing) columns it certifies, for every
    /// `AggFn`:
    ///
    ///  - **single-source parity** — `Column.aggregate fn col` equals the cell a single-group
    ///    `GroupBy([], [agg])` produces over the same column (the de-duplication is the point: one
    ///    implementation, byte-identical value).
    let aggregateParityLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable parity = None

        let fns = [ Sum; Mean; Min; Max; Count; Median; StdDev; First; Last; CountDistinct ]

        for i in 0 .. iterations - 1 do
            let isInt, r1 = ConfRng.intBelow 2 rng
            let nRows, r2 = ConfRng.intBelow 6 r1
            rng <- r2
            let ty = if isInt = 0 then IntType else FloatType
            let mutable r = rng

            let cells =
                [ for _ in 0..nRows ->
                      let k, r' = ConfRng.intBelow 4 r
                      r <- r'

                      if k = 0 then
                          Null
                      else
                          let v, r'' = ConfRng.intBelow 200 r
                          r <- r''

                          if ty = IntType then
                              Int(v - 100)
                          else
                              Float(float (v - 100) * 0.5) ]

            rng <- r
            let col = Column.create "c" ty cells

            let table =
                { Schema = [ "c", ty ]
                  Columns = [ col ] }

            for fn in fns do
                let direct = Column.aggregate fn col |> Result.mapError (fun _ -> "aggErr")

                let viaGroup =
                    match DataFrame.evalPipeline [ GroupBy([], [ { Name = "a"; Fn = fn; Of = "c" } ]) ] table with
                    | Ok t ->
                        match Table.tryColumn "a" t with
                        | Some ac -> Ok(Column.cell 0 ac)
                        | None -> Error "no agg column"
                    | Error _ -> Error "aggErr"

                if direct <> viaGroup && parity.IsNone then
                    parity <-
                        Some(
                            sprintf
                                "seed=%d iter=%d fn=%A: aggregate ≠ single-group GroupBy (%A vs %A)"
                                seed
                                i
                                fn
                                direct
                                viaGroup
                        )

        [ { Law = "Column.aggregate is byte-identical to a single-group GroupBy (single source of truth)"
            Passed = parity.IsNone
            Counterexample = parity } ]

    // ---- columnar op-algebra + op-stream (Phase 31) ----
    // The teeth on `ColumnOps`: a table-edit op DU applies totally, `canApply ≡ apply`, `apply ∘ invert =
    // id` (where invert is defined), an inverse exists ONLY for an applicable op (Phase 181), and a
    // table-edit stream chains + verifies + replays byte-identically through the EXISTING
    // `Fuaran.Core.OpStream` `StreamWitness` (no core change — the witness-free data strand survives the
    // op-stream adoption).

    // Phase 246 — the kit's own sample, lifted out of the family so the witness-taking form can run
    // the same laws over a DOMAIN'S generator. `columnarOpLaws` still runs exactly this pair.

    let private columnarKitTable: Table =
        { Schema = [ "a", IntType; "b", IntType ]
          Columns =
            [ Column.create "a" IntType [ Int 1; Int 2; Int 3 ]
              Column.create "b" IntType [ Int 4; Int 5; Int 6 ] ] }

    // a (possibly-invalid) op generated against the current table state
    let private columnarKitOp (t: Table) (r: ConfRng.T) : ColumnOp * ConfRng.T =
        let rc = Table.rowCount t
        let names = Table.columnNames t
        let kind, r1 = ConfRng.intBelow 7 r

        match kind with
        | 0 ->
            let v, r2 = ConfRng.intBelow 100 r1

            if List.isEmpty names || rc = 0 then
                InsertColumn(0, Column.create "a" IntType [ Int v; Int v; Int v ]), r2
            else
                let ci, r3 = ConfRng.intBelow (List.length names) r2
                let row, r4 = ConfRng.intBelow rc r3
                SetCell(List.item ci names, row, Int v), r4
        | 1 ->
            let v, r2 = ConfRng.intBelow 100 r1

            if List.isEmpty names then
                InsertColumn(0, Column.create "a" IntType []), r2
            else
                let ci, r3 = ConfRng.intBelow (List.length names) r2
                let nm = List.item ci names
                SetColumn(Column.create nm IntType (List.replicate rc (Int v))), r3
        | 2 ->
            let id, r2 = ConfRng.intBelow 1000 r1
            let v, r3 = ConfRng.intBelow 100 r2
            let len = if List.isEmpty names then 3 else rc

            InsertColumn(List.length names, Column.create ("c" + string id) IntType (List.replicate len (Int v))), r3
        | 3 ->
            if List.isEmpty names then
                InsertColumn(0, Column.create "a" IntType [ Int 0; Int 0; Int 0 ]), r1
            else
                let ci, r2 = ConfRng.intBelow (List.length names) r1
                RemoveColumn(List.item ci names), r2
        | 4 ->
            let v, r2 = ConfRng.intBelow 100 r1
            AppendRows([ names |> List.map (fun n -> n, Int v) ]), r2
        | 5 ->
            // Phase 181 — an insert the table MUST refuse as a duplicate. The four arms above draw
            // ops the table usually accepts, so without this the inverse-only-for-applicable law
            // below would be certified over a sample that never reaches the shape it is about.
            let v, r2 = ConfRng.intBelow 100 r1

            if List.isEmpty names then
                InsertColumn(0, Column.create "a" IntType [ Int v; Int v; Int v ]), r2
            else
                let ci, r3 = ConfRng.intBelow (List.length names) r2
                let nm = List.item ci names
                InsertColumn(0, Column.create nm IntType (List.replicate rc (Int v))), r3
        | _ ->
            // Phase 181 — a `SetCell` the table MUST refuse on the VALUE. `invert`'s pre-181
            // `SetCell` clause read the column and the row but never the value, so this is the
            // second shape where a refused op had a live inverse.
            if List.isEmpty names || rc = 0 then
                AppendRows([]), r1
            else
                let ci, r2 = ConfRng.intBelow (List.length names) r1
                let row, r3 = ConfRng.intBelow rc r2
                SetCell(List.item ci names, row, Str "wrong"), r3

    /// The shared body of `columnarOpLaws` and `columnarOpLawsWith`: the laws over a base table and
    /// a table-reading op source. `family` names the guard's owner.
    let private columnarOpRun
        (family: string)
        (invertUnderTest: ColumnOp -> Table -> Result<ColumnOp, ColumnRejection>)
        (baseTable: Table)
        (genColOp: Table -> ConfRng.T -> ColumnOp * ConfRng.T)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable totality = None
        let mutable equivalence = None
        let mutable inversion = None
        let mutable refusedInverse = None
        let mutable refusedStructural = 0
        let mutable verify = None
        let mutable replayLaw = None

        let hashFn = OpStream.defaultHash
        let sw = ColumnOps.streamWitness

        // The four ops an inverse can exist for — the two that never have one are skipped by the
        // coverage count below, since a law about refused ops learns nothing from them.
        let structural op =
            match op with
            | SetCell _
            | SetColumn _
            | InsertColumn _
            | RemoveColumn _ -> true
            | AppendRows _
            | ApplyTransform _ -> false

        for i in 0 .. iterations - 1 do
            let mutable state = baseTable
            let mutable recs = OpStream.empty

            for _ in 0..6 do
                let op, r' = genColOp state rng
                rng <- r'

                let applied =
                    try
                        Some(ColumnOps.apply op state)
                    with _ ->
                        None

                match applied with
                | None ->
                    if totality.IsNone then
                        totality <- Some(sprintf "seed=%d iter=%d: apply threw on %A" seed i op)
                | Some res ->
                    let chk = ColumnOps.canApply op state

                    let equiv =
                        match res, chk with
                        | Ok _, Ok() -> true
                        | Error e1, Error e2 -> e1 = e2
                        | _ -> false

                    if not equiv && equivalence.IsNone then
                        equivalence <- Some(sprintf "seed=%d iter=%d: canApply≠apply on %A" seed i op)

                    match res with
                    | Ok post ->
                        (match invertUnderTest op state with
                         | Ok inv ->
                             match ColumnOps.apply inv post with
                             | Ok restored when restored = state -> ()
                             | other ->
                                 if inversion.IsNone then
                                     inversion <-
                                         Some(sprintf "seed=%d iter=%d: apply∘invert≠id on %A (got %A)" seed i op other)
                         | Error(NotInvertible _) -> ()
                         | Error e ->
                             if inversion.IsNone then
                                 inversion <-
                                     Some(
                                         sprintf
                                             "seed=%d iter=%d: a structural op failed to invert: %A (%A)"
                                             seed
                                             i
                                             op
                                             e
                                     ))

                        match OpStream.append hashFn sw (Human "conf") op state recs with
                        | Ok(s', recs') ->
                            state <- s'
                            recs <- recs'
                        | Error _ -> ()
                    | Error rej ->
                        // Phase 181 — an inverse exists ONLY for an applicable op. A refused op that
                        // still yields one hands an undo stack a LIVE op derived from a step the table
                        // never took: the pre-181 `InsertColumn` clause answered `RemoveColumn` for an
                        // insert refused as a duplicate, and that remove SUCCEEDS at the pre-state and
                        // takes the column that was already there.
                        if structural op then
                            refusedStructural <- refusedStructural + 1

                            match invertUnderTest op state with
                            | Ok inv ->
                                if refusedInverse.IsNone then
                                    refusedInverse <-
                                        Some(
                                            sprintf
                                                "seed=%d iter=%d: %A was REFUSED (%A) and still has an inverse %A"
                                                seed
                                                i
                                                op
                                                rej
                                                inv
                                        )
                            | Error _ -> ()

            if not (OpStream.verifyChain hashFn sw recs) && verify.IsNone then
                verify <- Some(sprintf "seed=%d iter=%d: verifyChain rejected an intact table-edit stream" seed i)

            match OpStream.replay sw baseTable recs with
            | Ok s when s = state -> ()
            | other ->
                if replayLaw.IsNone then
                    replayLaw <- Some(sprintf "seed=%d iter=%d: replay ≠ live state (got %A)" seed i other)

        [ { Law = "columnar apply totality (never throws)"
            Passed = totality.IsNone
            Counterexample = totality }
          { Law = "columnar canApply ≡ apply (accept/reject + rejection)"
            Passed = equivalence.IsNone
            Counterexample = equivalence }
          { Law = "columnar apply ∘ invert = identity (where invert is defined)"
            Passed = inversion.IsNone
            Counterexample = inversion }
          { Law = "columnar inverse exists only for an applicable op"
            Passed = refusedInverse.IsNone
            Counterexample = refusedInverse }
          { Law = "verifyChain accepts an intact table-edit stream (over the columnar StreamWitness)"
            Passed = verify.IsNone
            Counterexample = verify }
          { Law = "replay re-derives the live table from the base"
            Passed = replayLaw.IsNone
            Counterexample = replayLaw }
          // The law above is about REFUSED ops, so a run that refused no invertible op certifies
          // nothing by it — and would report a hollow green. Phase 121's guard says so instead.
          SampleAdequacy.reached
              family
              "invert's refusal population"
              seed
              [ "refused invertible op", refusedStructural ] ]

    /// The kit's reference `StreamGen<ColumnOp, Table>` (Phase 246): the fixture table
    /// `columnarOpLaws` starts from, and ops drawn WITHOUT reading the current table — so it is the
    /// shape a domain hands `columnarOpLawsWith`, and it reaches every population those laws read
    /// (accepted edits of every kind, a duplicate insert and a mistyped cell the table refuses). It
    /// is not `columnarOpLaws`' own sample: that one reads the evolving table, which a `StreamGen`
    /// cannot.
    let columnarOpStreamGen: StreamGen<ColumnOp, Table> =
        { State0 = columnarKitTable
          Op =
            fun r ->
                let names = [ "a"; "b"; "c0" ]
                let kind, r1 = ConfRng.intBelow 7 r
                let nm, r2 = ConfRng.choose names r1
                let v, r3 = ConfRng.intBelow 100 r2
                let row, r4 = ConfRng.intBelow 4 r3

                match kind with
                | 0 -> SetCell(nm, row, Int v), r4
                | 1 -> SetColumn(Column.create nm IntType (List.replicate 3 (Int v))), r4
                | 2 ->
                    InsertColumn(row % 3, Column.create ("c" + string (v % 3)) IntType (List.replicate 3 (Int v))), r4
                | 3 -> RemoveColumn nm, r4
                | 4 -> AppendRows [ [ "a", Int v; "b", Int v ] ], r4
                | 5 -> InsertColumn(0, Column.create nm IntType (List.replicate 3 (Int v))), r4
                | _ -> SetCell(nm, row % 3, Str "wrong"), r4 }

    /// The columnar op-algebra laws at a DOMAIN'S generator (Phase 246), with the **injectable
    /// `invert`** Phase 181 gave it — the `concurrencyLawsWith` shape: the domain's witness and the
    /// teeth seam in one entry point. It runs `columnarOpLaws`' laws with `gen.State0` as the base
    /// table and `gen.Op` as the op source: apply totality, `canApply ≡ apply`, `apply ∘ invert = id`,
    /// an inverse only for an applicable op, and the table-edit stream's `verifyChain` and replay.
    /// A domain passes `ColumnOps.invert`; a test hands in a defective `invert` and watches the
    /// inverse-only-for-applicable law bite.
    ///
    /// **Vacuity.** Guarded on the refused invertible ops the generator draws, as `columnarOpLaws` is.
    ///
    /// **Changed in `0.32.0`:** it took `invertUnderTest seed iterations` and ran the kit's fixture
    /// sample. A caller that wants the kit's sample passes `Conformance.columnarOpStreamGen`.
    let columnarOpLawsWith
        (invertUnderTest: ColumnOp -> Table -> Result<ColumnOp, ColumnRejection>)
        (gen: StreamGen<ColumnOp, Table>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        columnarOpRun "Conformance.columnarOpLawsWith" invertUnderTest gen.State0 (fun _ r -> gen.Op r) seed iterations

    /// The columnar op-algebra laws (Phase 31). Self-contained — over a seed-replayable sample it evolves
    /// an all-int reference `Table` by random ops and certifies: **apply totality** (never throws —
    /// failures are a typed `ColumnRejection`); **canApply ≡ apply**; **apply ∘ invert = id** (for an
    /// invertible op; `AppendRows`/`ApplyTransform` report `NotInvertible` and are skipped); **an inverse
    /// exists only for an applicable op** (Phase 181 — a REFUSED op yields its rejection, never an op,
    /// with a coverage guard so a sample that refuses nothing invertible reports vacuity rather than a
    /// hollow green); and that the table-edit stream built via `OpStream.append` over the columnar
    /// `StreamWitness` **verifies** and **replays** back to the live state from the base table.
    let columnarOpLaws (seed: int) (iterations: int) : LawResult list =
        columnarOpRun "columnarOpLaws" ColumnOps.invert columnarKitTable columnarKitOp seed iterations

    // ---- incremental DataFrame evaluation (Phase 34) ----
    // The teeth on `DataFrame.evalFrom`: the incremental path is byte-identical to a full `evalPipeline`
    // over the changed source, for EVERY generated change (the reuse is a sound optimisation, not a
    // different answer). Includes a `ColumnOps.changeOf`-driven case (an edit-stream op → a `Change`).

    /// The incremental-equivalence laws (Phase 34). Self-contained — over a seed-replayable sample it
    /// builds an `(a:int, b:int, c:int)` source, a pipeline, and a change (with the matching changed
    /// source) and certifies:
    ///
    ///  - **change-driven equivalence** — `evalFrom (evalPipeline old) change pipeline new` equals
    ///    `evalPipeline pipeline new` for a directly-supplied `Change` (covering the reuse short-circuit
    ///    *and* the full-recompute path);
    ///  - **op-driven equivalence** — the same, where the `Change` is derived from a columnar op via
    ///    `ColumnOps.changeOf` (a `SetCell` edit), so an edit-stream drives incremental re-eval correctly.
    let incrementalLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable changeDriven = None
        let mutable opDriven = None

        let col name cells : Column = Column.create name IntType cells

        let mkTable (a: Cell list) (b: Cell list) (c: Cell list) : Table =
            { Schema = [ "a", IntType; "b", IntType; "c", IntType ]
              Columns = [ col "a" a; col "b" b; col "c" c ] }

        let pipelineOf k : Transform list =
            match k with
            | 0 -> [ Filter(Binary(Gt, Col "a", Lit(Int 0))) ]
            | 1 -> [ Project [ "a", "a"; "b", "b" ] ] // drops c → an irrelevant c-change can reuse
            | 2 -> [ Derive("d", Binary(Add, Col "a", Lit(Int 1))) ]
            | 3 -> [ GroupBy([ "a" ], [ { Name = "s"; Fn = Sum; Of = "b" } ]) ] // drops c
            // Phase 202 — a group-by with a step AFTER it. This family is a different seam from
            // `Incremental.*` (it reuses a whole prior RESULT against a coarse `Change`, where that
            // one propagates a row delta), so it makes no claim about the group-table tail; the
            // shape is here because the tail admission makes such pipelines common, and every
            // pipeline this family draws has to keep answering as a full evaluation would.
            | 4 ->
                [ GroupBy([ "a" ], [ { Name = "s"; Fn = Sum; Of = "b" }; { Name = "n"; Fn = Count; Of = "c" } ])
                  Filter(Binary(Gt, Col "n", Lit(Int 0))) ]
            | _ -> [ Transform.sortBy [ "b", Asc ]; Project [ "a", "a" ] ] // drops b, c

        for i in 0 .. iterations - 1 do
            let nRows, r1 = ConfRng.intBelow 4 rng
            let rows = nRows + 1
            let mutable r = r1

            let draw () =
                let v, r' = ConfRng.intBelow 20 r
                r <- r'
                Int(v - 10)

            let a0 = [ for _ in 1..rows -> draw () ]
            let b0 = [ for _ in 1..rows -> draw () ]
            let c0 = [ for _ in 1..rows -> draw () ]
            let oldSrc = mkTable a0 b0 c0

            let pk, r2 = ConfRng.intBelow 6 r
            r <- r2
            let pipeline = pipelineOf pk

            // a directly-supplied change + the matching changed source
            let ck, r3 = ConfRng.intBelow 3 r
            r <- r3

            let change, newSrc =
                match ck with
                | 0 -> ColumnValuesChanged "c", mkTable a0 b0 (c0 |> List.map (fun _ -> Int 999))
                | 1 -> ColumnValuesChanged "a", mkTable (a0 |> List.map (fun _ -> Int 7)) b0 c0
                | _ -> RowsAppended, mkTable (a0 @ [ Int 5 ]) (b0 @ [ Int 6 ]) (c0 @ [ Int 7 ])

            rng <- r

            (match DataFrame.evalPipeline pipeline oldSrc with
             | Ok prior ->
                 let viaIncr = DataFrame.evalFrom prior change pipeline newSrc
                 let viaFull = DataFrame.evalPipeline pipeline newSrc

                 if viaIncr <> viaFull && changeDriven.IsNone then
                     changeDriven <-
                         Some(sprintf "seed=%d iter=%d: evalFrom ≠ evalPipeline (pk=%d change=%A)" seed i pk change)
             | Error _ -> ())

            // op-driven: a SetCell on column c → ColumnOps.changeOf → the same equivalence
            let editOp = SetCell("c", 0, Int 1234)

            (match ColumnOps.apply editOp oldSrc with
             | Ok newSrc2 ->
                 match DataFrame.evalPipeline pipeline oldSrc with
                 | Ok prior ->
                     let viaIncr = DataFrame.evalFrom prior (ColumnOps.changeOf editOp) pipeline newSrc2
                     let viaFull = DataFrame.evalPipeline pipeline newSrc2

                     if viaIncr <> viaFull && opDriven.IsNone then
                         opDriven <-
                             Some(sprintf "seed=%d iter=%d: op-driven evalFrom ≠ evalPipeline (pk=%d)" seed i pk)
                 | Error _ -> ()
             | Error _ -> ())

        [ { Law = "evalFrom is byte-identical to a full evalPipeline over the changed source (every change)"
            Passed = changeDriven.IsNone
            Counterexample = changeDriven }
          { Law = "a columnar op's changeOf drives evalFrom equivalently (edit-stream incremental re-eval)"
            Passed = opDriven.IsNone
            Counterexample = opDriven } ]

    /// The incremental-equivalence law at a DOMAIN'S pipelines and tables (Phase 246).
    /// `incrementalLaws` beside it draws the kit's own `(a, b, c)` tables and six fixed pipelines;
    /// this form runs the domain's: each iteration starts from `gen.State0` and draws up to seven ops
    /// from `gen.Op`, and for every op the table ACCEPTS it evaluates one of the domain's `pipelines`
    /// over the table before the edit and certifies that
    /// `DataFrame.evalFrom prior (ColumnOps.changeOf op) pipeline after` equals
    /// `DataFrame.evalPipeline pipeline after` — the edit-stream incremental path is the full
    /// evaluation, at the domain's own shapes. An op the table refuses moves nothing and is skipped,
    /// and so is a pipeline that does not evaluate over the table before the edit (there is no prior
    /// to reuse).
    ///
    /// **Vacuity.** `evalFrom` can only differ from a full evaluation on a VALUE edit — every other
    /// change it answers by evaluating in full — so the guard counts accepted value edits
    /// (`SetCell` / `SetColumn`) that reached the comparison. A domain whose generator never edits a
    /// value certifies nothing here, and the family says so. An empty `pipelines` list is starved the
    /// same way.
    let incrementalLawsWith
        (pipelines: Transform list list)
        (gen: StreamGen<ColumnOp, Table>)
        (seed: int)
        (iterations: int)
        : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable equivalence = None
        let mutable valueEdits = 0

        for i in 0 .. iterations - 1 do
            let mutable state = gen.State0

            for _ in 0..6 do
                let op, r1 = gen.Op rng
                rng <- r1

                match ColumnOps.apply op state, pipelines with
                | Ok after, _ :: _ ->
                    let pipeline, r2 = ConfRng.choose pipelines rng
                    rng <- r2

                    (match DataFrame.evalPipeline pipeline state with
                     | Ok prior ->
                         let change = ColumnOps.changeOf op

                         (match change with
                          | ColumnValuesChanged _ -> valueEdits <- valueEdits + 1
                          | _ -> ())

                         let viaIncr = DataFrame.evalFrom prior change pipeline after
                         let viaFull = DataFrame.evalPipeline pipeline after

                         if viaIncr <> viaFull && equivalence.IsNone then
                             equivalence <-
                                 Some(
                                     sprintf
                                         "seed=%d iter=%d: evalFrom ≠ evalPipeline after %A (pipeline=%A)"
                                         seed
                                         i
                                         op
                                         pipeline
                                 )
                     | Error _ -> ())

                    state <- after
                | Ok after, [] -> state <- after
                | Error _, _ -> ()

        [ { Law = "at the domain's pipelines, evalFrom over an edit's changeOf is byte-identical to a full evalPipeline"
            Passed = equivalence.IsNone
            Counterexample = equivalence }
          SampleAdequacy.reached "Conformance.incrementalLawsWith" "value edit" seed [ "value edit", valueEdits ] ]

    /// The `ColExpr.Param` + evaluation-environment laws (Phase 77) — the teeth on the parameterised
    /// `DataFrame` evaluator, the substrate a UI tier binds a filter/state value into. Self-contained
    /// (it builds its own param pipelines from the seed); over a seed-replayable sample it certifies:
    ///
    ///  - **substitution equivalence** — `evalPipelineInEnv env p` ≡ `evalPipeline (Transform.substitute
    ///    env p)`: binding a param through the env is the same as replacing it with its literal;
    ///  - **unbound-param defect** — dropping a referenced param from the env is a named
    ///    `UnboundParam(name, bound)` (the missing name + the enumerated bound set), never a throw;
    ///  - **`paramsOf` completeness** — an env binding *exactly* `Transform.paramsOf p` evaluates with no
    ///    `UnboundParam` (evaluation consults no param outside `paramsOf`), `paramsOf` equals the params
    ///    the pipeline actually references, and any `paramsOf` member absent from the env yields the
    ///    defect naming exactly it;
    ///  - **codec round-trip** — a pipeline carrying `Param` steps `encode`→`decode`s back byte-stably.
    let paramLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable substEquiv = None
        let mutable unboundDefect = None
        let mutable completeness = None
        let mutable roundTrip = None

        let col name cells : Column = Column.create name IntType cells

        for i in 0 .. iterations - 1 do
            let nRows, r1 = ConfRng.intBelow 4 rng
            let rows = nRows + 1
            let mutable r = r1

            let draw () =
                let v, r' = ConfRng.intBelow 40 r
                r <- r'
                Int(v - 20)

            let aCells = [ for _ in 1..rows -> draw () ]

            let table =
                { Schema = [ "a", IntType ]
                  Columns = [ col "a" aCells ] }

            // 1..3 params p0..p(k-1), each bound to an Int in the env
            let pk, r2 = ConfRng.intBelow 3 r
            r <- r2
            let paramCount = pk + 1
            let names = [ for j in 0 .. paramCount - 1 -> "p" + string j ]

            let mutable env = Map.empty

            for nm in names do
                let v, r' = ConfRng.intBelow 40 r
                r <- r'
                env <- Map.add nm (Int(v - 20)) env

            // a pipeline referencing every param exactly once — one Derive per param over the input
            // table (rows ≥ 1), so every param is *consulted* on evaluation (a later step behind a
            // row-dropping Filter would be lazily skipped and never surface its unbound param).
            let pipeline =
                [ for j in 0 .. paramCount - 1 -> Derive("d" + string j, Binary(Add, Col "a", Param("p" + string j))) ]

            rng <- r

            // --- substitution equivalence ---
            let viaEnv = DataFrame.evalPipelineInEnv env pipeline table
            let viaSubst = DataFrame.evalPipeline (Transform.substitute env pipeline) table

            if viaEnv <> viaSubst && substEquiv.IsNone then
                substEquiv <- Some(sprintf "seed=%d iter=%d: evalPipelineInEnv ≠ evalPipeline∘substitute" seed i)

            // --- paramsOf completeness ---
            let declared = Transform.paramsOf pipeline

            let fullEnv =
                declared |> List.fold (fun m nm -> Map.add nm (Map.find nm env) m) Map.empty

            (match DataFrame.evalPipelineInEnv fullEnv pipeline table with
             | Error(UnboundParam(nm, _)) when completeness.IsNone ->
                 completeness <-
                     Some(sprintf "seed=%d iter=%d: env binding exactly paramsOf still reported unbound '%s'" seed i nm)
             | _ -> ())

            if Set.ofList declared <> Set.ofList names && completeness.IsNone then
                completeness <- Some(sprintf "seed=%d iter=%d: paramsOf %A ≠ referenced %A" seed i declared names)

            // --- unbound-param defect: drop one paramsOf member ---
            (match declared with
             | [] -> ()
             | _ ->
                 let dropIdx, r3 = ConfRng.intBelow (List.length declared) rng
                 rng <- r3
                 let dropped = List.item dropIdx declared
                 let partial = Map.remove dropped fullEnv

                 match DataFrame.evalPipelineInEnv partial pipeline table with
                 | Error(UnboundParam(nm, bound)) ->
                     let expectedBound = partial |> Map.toList |> List.map fst

                     if nm <> dropped && unboundDefect.IsNone then
                         unboundDefect <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: UnboundParam named '%s', expected dropped '%s'"
                                     seed
                                     i
                                     nm
                                     dropped
                             )
                     elif bound <> expectedBound && unboundDefect.IsNone then
                         unboundDefect <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: UnboundParam bound-set %A ≠ env keys %A"
                                     seed
                                     i
                                     bound
                                     expectedBound
                             )
                 | other ->
                     if unboundDefect.IsNone then
                         unboundDefect <-
                             Some(
                                 sprintf
                                     "seed=%d iter=%d: dropping '%s' gave %A, expected UnboundParam"
                                     seed
                                     i
                                     dropped
                                     other
                             ))

            // --- codec round-trip including the param case ---
            let once = DataFrameCodec.encodePipeline pipeline

            (match DataFrameCodec.decodePipeline once with
             | Ok p2 ->
                 if (p2 <> pipeline || DataFrameCodec.encodePipeline p2 <> once) && roundTrip.IsNone then
                     roundTrip <-
                         Some(sprintf "seed=%d iter=%d: param pipeline codec not byte-stable round-trip" seed i)
             | Error e ->
                 if roundTrip.IsNone then
                     roundTrip <-
                         Some(
                             sprintf
                                 "seed=%d iter=%d: param pipeline decode failed: %s"
                                 seed
                                 i
                                 (ColumnCodec.errorString e)
                         ))

        [ { Law = "evalPipelineInEnv env ≡ evalPipeline (substitute Lit) for every binding in env"
            Passed = substEquiv.IsNone
            Counterexample = substEquiv }
          { Law = "an unbound param is UnboundParam(name, bound) — names the param, enumerates the bound set"
            Passed = unboundDefect.IsNone
            Counterexample = unboundDefect }
          { Law = "Transform.paramsOf is total + complete (evaluation consults no param outside it)"
            Passed = completeness.IsNone
            Counterexample = completeness }
          { Law = "a pipeline carrying Param steps round-trips the codec byte-stably"
            Passed = roundTrip.IsNone
            Counterexample = roundTrip } ]

    // ---- Static output-schema derivation (Phase 112) ----
    // The teeth on `SchemaWalk`: a walk that derives a pipeline's output columns WITHOUT evaluating
    // it is only worth having if its answer is the one the evaluator would give, and the two live in
    // different functions over the same closed DU — exactly the shape that drifts silently.

    /// The static output-schema laws (Phase 112) — the agreement between `SchemaWalk.ofPipeline` and
    /// the schema `DataFrame.evalPipelineWith` actually produces. Self-contained: it builds its own
    /// tables and pipelines from the seed, over a verb menu that reaches every schema-shaping case
    /// (the closing verbs, the appending verbs, the pivot that opens the set, the four combining
    /// join kinds and the two filtering ones, an undeclared `Ref` right-hand source, and a window
    /// whose output name COLLIDES with an existing column). Only samples the evaluator accepts are
    /// judged — a pipeline it rejects has no output schema to be right or wrong about.
    ///
    ///  - **closed ⇒ exact** — where the walk says `Closed`, the derived column names equal the
    ///    evaluated schema's names, IN ORDER and with duplicates: `Closed` is the only case a
    ///    consumer may conclude an absence from, so anything less than equality would make a
    ///    refusal unsound.
    ///  - **open ⇒ sound** — where the walk says `AtLeast`, every derived name is a name the
    ///    evaluated schema carries. The walk may know less than the truth; it may never claim a
    ///    column the result does not have.
    ///  - **declared types agree** — wherever the walk states a column's type (`Some`), it is the
    ///    type the evaluator gave that column. `None` is the honest "decidable only from the data"
    ///    and is not judged.
    ///  - **the sample is not vacuous** — both verdicts were actually reached. A parity law whose
    ///    generator never produced one of the two cases is a green that proves half of what it says.
    let schemaWalkLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable closedExact = None
        let mutable openSound = None
        let mutable typesAgree = None
        let mutable closedSeen = 0
        let mutable openSeen = 0

        let col name ty cells : Column = Column.create name ty cells

        // The left/base table: two int columns and a string grouping column.
        let baseTable (n: int) : Table =
            let a = [ for i in 1..n -> Int(i % 3) ]
            let b = [ for i in 1..n -> if i % 4 = 0 then Null else Int(i * 2) ]
            let g = [ for i in 1..n -> Str(if i % 2 = 0 then "x" else "y") ]

            { Schema = [ "a", IntType; "b", IntType; "g", StringType ]
              Columns = [ col "a" IntType a; col "b" IntType b; col "g" StringType g ] }

        // The right-hand table a combining join reaches. Its `g` COLLIDES with the left's, so the
        // evaluator's `_right` suffix rule is exercised on every combining sample.
        let rightTable: Table =
            { Schema = [ "g", StringType; "v", FloatType ]
              Columns =
                [ col "g" StringType [ Str "x"; Str "y" ]
                  col "v" FloatType [ Float 1.0; Float 2.5 ] ] }

        // A schema-identical peer, so the three set ops have something they accept.
        let peerTable: Table =
            { Schema = [ "a", IntType; "b", IntType; "g", StringType ]
              Columns =
                [ col "a" IntType [ Int 1; Int 2 ]
                  col "b" IntType [ Int 8; Null ]
                  col "g" StringType [ Str "x"; Str "z" ] ] }

        let resolve (name: string) : Result<Table, EvalError> =
            match name with
            | "right" -> Ok rightTable
            | "peer" -> Ok peerTable
            | other -> Error(UnresolvedSource other)

        let win partitionBy orderBy fn ofCol asCol : WindowSpec =
            { PartitionBy = partitionBy
              OrderBy = orderBy
              Fn = fn
              Of = ofCol
              As = asCol }

        let menu: Transform list =
            [ Filter(Binary(Gt, Col "a", Lit(Int 0)))
              Transform.sortBy [ "b", Asc ]
              Distinct
              Transform.limit 3 0
              Derive("d", Binary(Add, Col "a", Lit(Int 1)))
              // A derive onto an EXISTING name — the evaluator retypes in place, keeping position.
              Derive("b", Cast(FloatType, Col "b"))
              Window(win [ "g" ] [ "a", Asc ] RowNumber "a" "rn")
              Window(win [] [ "a", Asc ] CumulSum "a" "cs")
              Window(win [ "g" ] [ "a", Asc ] Lag "b" "lagb")
              Window(win [] [ "a", Asc ] CumulMax "b" "runmax")
              // `As` COLLIDES with an existing column: the evaluator appends regardless, leaving the
              // name twice, and the walk must say the same rather than tidying it away.
              Window(win [] [ "a", Asc ] Rank "a" "a")
              Project [ "a", "a"; "g", "grp" ]
              GroupBy(
                  [ "g" ],
                  [ { Name = "s"; Fn = Sum; Of = "a" }
                    { Name = "n"; Fn = Count; Of = "b" }
                    { Name = "m"; Fn = Mean; Of = "a" }
                    { Name = "lo"; Fn = Min; Of = "b" } ]
              )
              Unpivot([ "g" ], [ "a"; "b" ])
              Pivot
                  { Index = [ "g" ]
                    On = "g"
                    Values = "a"
                    Agg = Sum }
              Join(Embedded rightTable, [ "g", "g" ], Inner)
              Join(Embedded rightTable, [ "g", "g" ], Left)
              Join(Embedded rightTable, [ "g", "g" ], Right)
              Join(Ref "right", [ "g", "g" ], Outer)
              Join(Embedded rightTable, [ "g", "g" ], Semi)
              Join(Ref "right", [ "g", "g" ], Anti)
              Union(Embedded peerTable)
              Intersect(Ref "peer")
              Except(Embedded peerTable) ]

        for i in 0 .. iterations - 1 do
            let rows, r1 = ConfRng.intBelow 4 rng
            let steps, r2 = ConfRng.intBelow 3 r1
            let mutable r = r2

            let pipeline =
                [ for _ in 0..steps do
                      let k, r' = ConfRng.intBelow (List.length menu) r
                      r <- r'
                      yield List.item k menu ]

            rng <- r

            let table = baseTable (rows + 2)

            match DataFrame.evalPipelineWith resolve pipeline table with
            | Error _ -> () // the evaluator rejected it; there is no output schema to agree about
            | Ok result ->
                // The walk is given the input schema and NO source declarations, so an undeclared
                // `Ref` is the honest "unknown" while the evaluator resolves it — which is precisely
                // the asymmetry the `AtLeast` case exists to carry.
                let derived = SchemaWalk.ofPipeline table.Schema pipeline
                let actualNames = result.Schema |> List.map fst
                let derivedCols = SchemaWalk.columns derived

                let describe () =
                    sprintf
                        "seed=%d iter=%d: pipeline=%A derived=%A actual=%A"
                        seed
                        i
                        pipeline
                        (SchemaWalk.names derived)
                        actualNames

                match derived with
                | SchemaKnowledge.Closed _ ->
                    closedSeen <- closedSeen + 1

                    if SchemaWalk.names derived <> actualNames && closedExact.IsNone then
                        closedExact <- Some(describe ())

                    // Positional, and only where the lengths agree — a name mismatch is already
                    // reported above and zipping unequal lists would report it twice as a type fault.
                    if List.length derivedCols = List.length result.Schema then
                        let bad =
                            List.zip derivedCols result.Schema
                            |> List.tryFind (fun (d, (_, ty)) ->
                                match d.Type with
                                | Some t -> t <> ty
                                | None -> false)

                        if bad.IsSome && typesAgree.IsNone then
                            typesAgree <- Some(sprintf "%s (closed, at %A)" (describe ()) bad)

                | SchemaKnowledge.AtLeast _ ->
                    openSeen <- openSeen + 1

                    let unclaimed =
                        derivedCols |> List.tryFind (fun d -> not (List.contains d.Name actualNames))

                    if unclaimed.IsSome && openSound.IsNone then
                        openSound <- Some(sprintf "%s (claimed %A)" (describe ()) unclaimed)

                    // By name rather than position: an open derivation is a SUBSET, so its columns
                    // need not sit where the evaluator put them.
                    let badType =
                        derivedCols
                        |> List.tryFind (fun d ->
                            match d.Type with
                            | None -> false
                            | Some t -> not (result.Schema |> List.exists (fun (n, ty) -> n = d.Name && ty = t)))

                    if badType.IsSome && typesAgree.IsNone then
                        typesAgree <- Some(sprintf "%s (open, at %A)" (describe ()) badType)

        [ { Law = "a Closed derivation names exactly the columns evalPipeline produces, in order"
            Passed = closedExact.IsNone
            Counterexample = closedExact }
          { Law = "an AtLeast derivation names only columns evalPipeline produces (never claims one)"
            Passed = openSound.IsNone
            Counterexample = openSound }
          { Law = "a derived column's DECLARED type is the type evalPipeline gave that column"
            Passed = typesAgree.IsNone
            Counterexample = typesAgree }
          { Law = "the generated sample reached both verdicts (the parity claim is not vacuous)"
            Passed = closedSeen > 0 && openSeen > 0
            Counterexample =
              if closedSeen > 0 && openSeen > 0 then
                  None
              else
                  Some(sprintf "seed=%d: closed=%d open=%d over %d iterations" seed closedSeen openSeen iterations) } ]

    /// **`Now` at a pinned clock is deterministic** (Phase 125) — the law that makes a
    /// clock-dependent pipeline a legitimate thing for a conformance kit to certify at all.
    ///
    /// A `ColExpr.Now` names the current moment. Left to read a host clock at evaluation it would
    /// make every downstream parity claim unfalsifiable: two hosts computing the same pipeline would
    /// legitimately disagree, and no vector could say which was wrong. `Now` therefore resolves by
    /// SUBSTITUTION against a `ClockWitness` the caller pins, and these five laws are what that buys:
    ///
    ///   1. **Determinism.** The same pipeline under the same witness evaluates to the same table,
    ///      twice, whatever the witness returns.
    ///   2. **The reading is the witness's.** A `Derive` of a bare `Now g` produces exactly the cell
    ///      `clock g` returned — not a re-derivation, not a coercion.
    ///   3. **One reading per grain per pinning.** A COUNTING witness — one that returns a different
    ///      cell on every call — still yields ONE value per grain across the whole pipeline, so two
    ///      `Now`s in one evaluation cannot straddle a tick and disagree with each other. This is
    ///      the law a naive `fun g -> DateTime.Now` implementation fails, which is why it is stated
    ///      over a witness that is deliberately not constant.
    ///   4. **Unpinned is REFUSED, by name.** A `Now` reaching the evaluator with no clock is
    ///      `EvalError.UnpinnedClock`, never a silent reading, and the grain it names is the one
    ///      that was asked for.
    ///   5. **Substitution is the only resolution**, so a pipeline that carried no `Now` evaluates
    ///      byte-identically whether or not a clock was pinned — pinning a clock is not an
    ///      evaluation mode with its own semantics.
    ///
    /// The sample is drawn, but every law's evidence is BUILT each iteration: both grains, a
    /// clock-bearing pipeline and a clock-free one, on the same input.
    let nowLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable deterministic = None
        let mutable isWitnessReading = None
        let mutable oneReading = None
        let mutable unpinned = None
        let mutable clockFreeUnchanged = None

        let col name cells : Column = Column.create name IntType cells

        for i in 0 .. iterations - 1 do
            let nRows, r1 = ConfRng.intBelow 4 rng
            let rows = nRows + 1
            let mutable r = r1

            let draw () =
                let v, r' = ConfRng.intBelow 40 r
                r <- r'
                Int(v - 20)

            let table =
                { Schema = [ "a", IntType ]
                  Columns = [ col "a" [ for _ in 1..rows -> draw () ] ] }

            // Which grain this iteration leads with — both are exercised below regardless.
            let gPick, r2 = ConfRng.intBelow 2 r
            r <- r2

            let grain = if gPick = 0 then NowGrain.Date else NowGrain.Timestamp

            let stampN, r3 = ConfRng.intBelow 28 r
            r <- r3
            rng <- r

            // A drawn but CONSTANT witness: the reading varies across iterations (so no law can be
            // passing on one fixed string) and is fixed within one, which is what a clock pinned for
            // an evaluation means.
            let dateOf (n: int) =
                "2026-09-" + (if n < 9 then "0" else "") + string (n + 1)

            let stampFor (g: NowGrain) (n: int) : Cell =
                match g with
                | NowGrain.Date -> Date(dateOf n)
                | NowGrain.Timestamp -> Timestamp(dateOf n + "T00:00:00Z")

            let clock: ClockWitness = fun g -> stampFor g stampN

            let pipeline =
                [ Derive("now1", Now grain)
                  Filter(Binary(Gt, Col "a", Lit(Int -100)))
                  Derive("now2", Now grain) ]

            // ---- 1. determinism ----
            let once = DataFrame.evalPipelineAt clock pipeline table
            let twice = DataFrame.evalPipelineAt clock pipeline table

            if once <> twice && deterministic.IsNone then
                deterministic <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: the same pipeline under the same ClockWitness evaluated to two different tables — a pinned clock is the whole determinism claim"
                            seed
                            i
                    )

            // ---- 2. the reading IS the witness's ----
            let expected = stampFor grain stampN

            (match DataFrame.evalPipelineAt clock [ Derive("n", Now grain) ] table with
             | Ok t ->
                 let got =
                     t.Columns
                     |> List.tryFind (fun c -> c.Name = "n")
                     |> Option.bind (fun c -> List.tryHead c.Cells)

                 if got <> Some expected && isWitnessReading.IsNone then
                     isWitnessReading <-
                         Some(
                             sprintf
                                 "seed=%d iter=%d: Derive(Now %A) produced %A where the witness returned %A"
                                 seed
                                 i
                                 grain
                                 got
                                 expected
                         )
             | Error e ->
                 if isWitnessReading.IsNone then
                     isWitnessReading <- Some(sprintf "seed=%d iter=%d: a pinned Now failed to evaluate: %A" seed i e))

            // ---- 3. ONE reading per grain per pinning, under a witness that is NOT constant ----
            // A counting witness answers differently on every call. If the pinning did not read it
            // once per grain, the two `Now`s above would land on different cells and this fails —
            // which is exactly the defect `fun _ -> <read the real clock>` would exhibit.
            let mutable calls = 0

            let counting: ClockWitness =
                fun g ->
                    calls <- calls + 1
                    stampFor g (calls % 28)

            (match DataFrame.evalPipelineAt counting pipeline table with
             | Ok t ->
                 let cellsOf n =
                     t.Columns |> List.tryFind (fun c -> c.Name = n) |> Option.map (fun c -> c.Cells)

                 if cellsOf "now1" <> cellsOf "now2" && oneReading.IsNone then
                     oneReading <-
                         Some(
                             sprintf
                                 "seed=%d iter=%d: two Now nodes of one grain in one evaluation produced different readings (%A vs %A) — the witness was read more than once per grain, so a pipeline can straddle a tick"
                                 seed
                                 i
                                 (cellsOf "now1")
                                 (cellsOf "now2")
                         )
             | Error e ->
                 if oneReading.IsNone then
                     oneReading <- Some(sprintf "seed=%d iter=%d: the counting-witness run failed: %A" seed i e))

            // ---- 4. unpinned is refused BY NAME, on both grains ----
            for g in [ NowGrain.Date; NowGrain.Timestamp ] do
                match DataFrame.evalPipeline [ Derive("n", Now g) ] table with
                | Error(UnpinnedClock got) when got = g -> ()
                | other ->
                    if unpinned.IsNone then
                        unpinned <-
                            Some(
                                sprintf
                                    "seed=%d iter=%d: an unpinned Now %A gave %A — it must be a strict UnpinnedClock naming that grain, never a silent reading and never a different error"
                                    seed
                                    i
                                    g
                                    other
                            )

            // ---- 5. a clock-free pipeline is unaffected by pinning one ----
            let clockFree =
                [ Filter(Binary(Gt, Col "a", Lit(Int -100)))
                  Derive("b", Binary(Add, Col "a", Lit(Int 1))) ]

            if
                DataFrame.evalPipelineAt clock clockFree table
                <> DataFrame.evalPipeline clockFree table
                && clockFreeUnchanged.IsNone
            then
                clockFreeUnchanged <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: pinning a clock changed the answer of a pipeline that names no Now — resolution must be substitution and nothing else"
                            seed
                            i
                    )

        [ { Law = "Now at a pinned ClockWitness is deterministic: the same pipeline twice gives the same table"
            Passed = deterministic.IsNone
            Counterexample = deterministic }
          { Law = "a pinned Now evaluates to exactly the cell the witness returned for its grain"
            Passed = isWitnessReading.IsNone
            Counterexample = isWitnessReading }
          { Law = "the witness is read at most ONCE PER GRAIN per pinning, so two Now nodes agree"
            Passed = oneReading.IsNone
            Counterexample = oneReading }
          { Law = "an unpinned Now is EvalError.UnpinnedClock naming its grain, never a silent reading"
            Passed = unpinned.IsNone
            Counterexample = unpinned }
          { Law = "pinning a clock does not change a pipeline that names no Now"
            Passed = clockFreeUnchanged.IsNone
            Counterexample = clockFreeUnchanged } ]

    /// **A scalar slot's parameter resolves exactly as an expression parameter does** (Phase 125) —
    /// the law that makes `Slot<'T>` a widening of the param seam rather than a second one beside it.
    ///
    /// `Transform.Limit` and `Transform.Sort` took literals until `0.23.0`. The whole argument for
    /// putting the param INSIDE Core's type — rather than leaving each host to pre-substitute it
    /// through a parallel structure — is that the answers then come from the machinery that already
    /// exists: one param namespace, one census (`Transform.paramsOf`), one unbound refusal
    /// (`EvalError.UnboundParam`). These six laws are that argument, checked:
    ///
    ///   1. **A bound slot param evaluates as its literal.** Binding `n` to `Int k` and evaluating
    ///      gives byte-identically what `Slot.Lit k` gives.
    ///   2. **Substitution and env-resolution agree** — `evalPipelineInEnv env p` ≡
    ///      `evalPipeline (Transform.substitute env p)`, the same law `paramLaws` states for
    ///      expression params, over the slots.
    ///   3. **The census is complete.** `Transform.paramsOf` names every slot param, so a host that
    ///      prunes or subscribes off it does not silently miss one.
    ///   4. **An unbound slot param is `UnboundParam` naming it**, never a default, never a skip.
    ///   5. **A wrongly-typed binding is a `TypeError` naming the slot**, not a coercion: a `Str`
    ///      at a count slot must not become a count, and the message must say WHICH slot, since a
    ///      `Limit` has two.
    ///   6. **A literal-only pipeline is untouched** — the wire and the answer are what they were
    ///      before slots existed, so adopting `0.23.0` costs a pipeline that binds nothing exactly
    ///      nothing.
    ///
    /// Every law's evidence is BUILT each iteration: a bound run, a substituted run, an unbound run,
    /// a mistyped run and a literal-only run over the same drawn table.
    let slotParamLaws (seed: int) (iterations: int) : LawResult list =
        let mutable rng = ConfRng.ofSeed seed
        let mutable asLiteral = None
        let mutable substEquiv = None
        let mutable census = None
        let mutable unbound = None
        let mutable mistyped = None
        let mutable literalOnly = None

        let col name cells : Column = Column.create name IntType cells

        for i in 0 .. iterations - 1 do
            let nRows, r1 = ConfRng.intBelow 6 rng
            let rows = nRows + 3
            let mutable r = r1

            let draw () =
                let v, r' = ConfRng.intBelow 40 r
                r <- r'
                Int(v - 20)

            let aCells = [ for _ in 1..rows -> draw () ]
            let bCells = [ for _ in 1..rows -> draw () ]

            let table =
                { Schema = [ "a", IntType; "b", IntType ]
                  Columns = [ col "a" aCells; col "b" bCells ] }

            let nTake, r2 = ConfRng.intBelow rows r
            let take = nTake + 1
            let offPick, r3 = ConfRng.intBelow 2 r2
            let sortPick, r4 = ConfRng.intBelow 2 r3
            rng <- r4

            let sortCol = if sortPick = 0 then "a" else "b"

            let env =
                Map.ofList [ "take", Int take; "skip", Int offPick; "orderBy", Str sortCol ]

            let bound =
                [ Sort [ Slot.Param "orderBy", Asc ]
                  Limit(Slot.Param "take", Slot.Param "skip") ]

            let literal = [ Transform.sortBy [ sortCol, Asc ]; Transform.limit take offPick ]

            // ---- 1. a bound slot param evaluates as its literal ----
            let viaEnv = DataFrame.evalPipelineInEnv env bound table
            let viaLit = DataFrame.evalPipeline literal table

            if viaEnv <> viaLit && asLiteral.IsNone then
                asLiteral <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: the bound slot pipeline gave %A where the literal one gave %A — a slot param must stand for its value and nothing else"
                            seed
                            i
                            viaEnv
                            viaLit
                    )

            // ---- 2. substitution ≡ env resolution ----
            let viaSubst = DataFrame.evalPipeline (Transform.substitute env bound) table

            if viaEnv <> viaSubst && substEquiv.IsNone then
                substEquiv <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: evalPipelineInEnv ≠ evalPipeline∘substitute over slot params — the two resolution routes have come apart"
                            seed
                            i
                    )

            // ---- 3. the census names every slot param ----
            let declared = Transform.paramsOf bound |> Set.ofList

            if declared <> Set.ofList [ "orderBy"; "take"; "skip" ] && census.IsNone then
                census <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: Transform.paramsOf reported %A over a pipeline binding orderBy/take/skip — a host prunes and subscribes off this list, so a missing name is a param nobody binds"
                            seed
                            i
                            declared
                    )

            // ---- 4. unbound is UnboundParam, naming it ----
            (match DataFrame.evalPipelineInEnv (Map.remove "take" env) bound table with
             | Error(UnboundParam("take", _)) -> ()
             | other ->
                 if unbound.IsNone then
                     unbound <-
                         Some(
                             sprintf
                                 "seed=%d iter=%d: an unbound slot param gave %A — it must be the strict UnboundParam naming it, the same case an expression param gives"
                                 seed
                                 i
                                 other
                         ))

            // ---- 5. a wrongly-typed binding is a TypeError naming the slot ----
            (match DataFrame.evalPipelineInEnv (Map.add "take" (Str "three") env) bound table with
             | Error(TypeError detail) when detail.Contains "limit n" -> ()
             | other ->
                 if mistyped.IsNone then
                     mistyped <-
                         Some(
                             sprintf
                                 "seed=%d iter=%d: a Str bound at a count slot gave %A — it must be a TypeError NAMING the slot, since a Limit has two and 'type error' alone would not say which"
                                 seed
                                 i
                                 other
                         ))

            // ---- 6. a literal-only pipeline is untouched by any of this ----
            let litWire = DataFrameCodec.encodePipeline literal

            if
                (litWire <> DataFrameCodec.encodePipeline literal
                 || not (litWire.Contains("\"n\":" + string take))
                 || litWire.Contains "$param")
                && literalOnly.IsNone
            then
                literalOnly <-
                    Some(
                        sprintf
                            "seed=%d iter=%d: a literal-only pipeline encoded as %s — a literal slot must be the bare value it always was, so a pre-0.23.0 document is byte-identical"
                            seed
                            i
                            litWire
                    )

        [ { Law = "a bound slot param evaluates byte-identically to the literal it stands for"
            Passed = asLiteral.IsNone
            Counterexample = asLiteral }
          { Law = "slot params: evalPipelineInEnv ≡ evalPipeline ∘ Transform.substitute"
            Passed = substEquiv.IsNone
            Counterexample = substEquiv }
          { Law = "Transform.paramsOf names every slot param alongside the expression params"
            Passed = census.IsNone
            Counterexample = census }
          { Law = "an unbound slot param is EvalError.UnboundParam naming it, never a default"
            Passed = unbound.IsNone
            Counterexample = unbound }
          { Law = "a slot param bound to the wrong cell shape is a TypeError NAMING the slot"
            Passed = mistyped.IsNone
            Counterexample = mistyped }
          { Law = "a literal slot encodes as the bare value it did before slots existed"
            Passed = literalOnly.IsNone
            Counterexample = literalOnly } ]
