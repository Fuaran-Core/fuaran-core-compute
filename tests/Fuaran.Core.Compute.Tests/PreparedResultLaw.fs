/// Phase 342 — the law that an evaluation kept prepared changes no answer: for every pipeline and
/// source in the transform law vectors, `DataFrame.evalToPrepared` refuses exactly when
/// `DataFrame.evalPrepared` refuses, with an equal error, and otherwise the table it stands for
/// (`DataFrame.toTable`) is `evalPrepared`'s answer, byte for byte as canonical wire.
///
/// A second clause holds the result to what it is FOR — being the next pipeline's source. Every
/// prepared result is fed every pipeline of the sample as a follow-on, and its answer must equal
/// the answer over the same table prepared afresh from the boundary (`prepare (toTable result)`),
/// refusals included. That is the claim a chain stands on: a hop that skips the boundary answers
/// what a hop through it answers. The follow-ons run over results whose last step left a
/// selection (a filter, a sort, a limit), an emptied frame, and a frame with no rows at all, so
/// the gather that makes a selected result dense is exercised, not just the identity case.
///
/// The sample is the transform vectors' own, reproduced from the declared generator
/// (`TransformVectorSample`) rather than read from `conformance/laws/transform-laws.json`, so the
/// law runs on every host that compiles this file: the suite runs it on .NET (and separately holds
/// the committed file's evalPipeline vectors to the first clause), and the node benchmark harness
/// compiles it with Fable and runs it under node before it times the chained pipelines.
///
/// FSharp.Core, the dataframe package and the conformance kit's generator only, Fable-clean.
module Fuaran.Compute.Tests.PreparedResultLaw

open Fuaran.Core
open Fuaran.Compute

/// The transform vectors' `(source, pipeline)` pairs, in vector order, drawn as `LawVectorExport`
/// draws them.
let sample () : (Table * Transform list) list =
    let mutable rng = ConfRng.ofSeed TransformVectorSample.seed

    [ for i in 0 .. TransformVectorSample.iterations - 1 do
          let pair, r = TransformVectorSample.gen i rng
          rng <- r
          yield pair ]

/// An answer as the text the law compares: the canonical wire string, or the refusal's text.
let private wire (r: Result<Table, EvalError>) : string =
    match r with
    | Ok t -> "ok " + ColumnCodec.encode (Embedded t)
    | Error e -> "error " + DataFrame.errorString e

/// The disagreements of one result against its reference, named by `what`.
let private agree (what: string) (reference: Result<Table, EvalError>) (kept: Result<Prepared, EvalError>) =
    match reference, kept with
    | Error a, Error b when a = b -> []
    | Error a, Error b ->
        [ sprintf "%s: the errors differ (%s / %s)" what (DataFrame.errorString a) (DataFrame.errorString b) ]
    | Ok _, Ok p ->
        let w = wire (Ok(DataFrame.toTable p))

        if w = wire reference then
            []
        else
            [ sprintf "%s: toTable of the kept result is not evalPrepared's answer" what ]
    | _ -> [ sprintf "%s: one refused and the other did not (%s)" what (wire reference) ]

/// Every disagreement over the sample, named; an empty list is a pass. Also answers how many
/// comparisons it made, so a caller can tell a pass from a law that ran nothing.
let check () : string list * int =
    let pairs = sample ()
    let pipelines = pairs |> List.map snd

    let ev p src =
        DataFrame.evalPrepared DataFrame.noResolve Map.empty p src

    let keep p src =
        DataFrame.evalToPrepared DataFrame.noResolve Map.empty p src

    let compared = ref 0

    let failures =
        [ for i, (table, pipeline) in List.indexed pairs do
              let source = DataFrame.prepare table
              compared.Value <- compared.Value + 1
              let kept = keep pipeline source
              yield! agree (sprintf "vector %d" i) (ev pipeline source) kept

              match kept with
              | Ok result ->
                  let afresh = DataFrame.prepare (DataFrame.toTable result)

                  for j, next in List.indexed pipelines do
                      compared.Value <- compared.Value + 1
                      let what = sprintf "vector %d then pipeline %d" i j
                      let reference = ev next afresh

                      if wire (ev next result) <> wire reference then
                          yield sprintf "%s: the kept result answers differently from its table prepared afresh" what

                      // A kept result kept again: the chain's middle hop.
                      yield! agree (what + ", kept") reference (keep next result)
              | Error _ -> () ]

    failures, compared.Value
