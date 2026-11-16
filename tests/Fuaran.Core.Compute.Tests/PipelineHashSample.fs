namespace Fuaran.Compute.Tests

// ============================================================================
//  Phase 604 (`DECISIONS.md` D83) — the declared pipelines the pipeline hash is
//  held to on both hosts the gate runs.
//
//  Realistic pipelines, one reaching each step kind and each expression kind,
//  and the edge cases the canonical bytes turn on: the empty pipeline, the float
//  layout at both ends of its fixed-point window, a negative zero, text outside
//  ASCII and a control character, a 64-bit seed past 2^53, and three pipelines
//  that have no canonical bytes at all. FSharp.Core and the dataframe package
//  only, and Fable-clean: the node leg (`Node/PooledLawNode.fsproj`) compiles
//  this file and prints `render ()`, which the suite holds to the .NET render
//  line for line; and the corpus emitter writes every mintable one into the
//  specification's `pipeline` family, so the hashes node reproduces are the
//  corpus's own.
// ============================================================================

module PipelineHashSample =

    open Fuaran.Core
    open Fuaran.Compute

    let private unitOf (text: string) : UnitOfMeasure =
        match Unit.parse text with
        | Ok u -> u
        | Error e -> failwithf "the sample's unit %s does not parse: %A" text e

    let private agg name fn ofCol : Agg = { Name = name; Fn = fn; Of = ofCol }

    let private amountOver = Binary(Gt, Col "amount", Lit(Int 100))

    let private ols =
        { FitSpec.ols "sales" [ "price", Col "price"; "advertising", Col "advertising" ] with
            GroupBy = [ "region" ] }

    /// Every sample, by its corpus id. The ids are the `pipeline` family's vector names.
    let pipelines: (string * Transform list) list =
        [ "empty", []
          "filter-derive-sort",
          [ Filter amountOver
            Derive("net", Binary(Sub, Col "amount", Col "discount"))
            Sort [ Slot.Lit "net", Desc; Slot.Param "tieBreak", Asc ] ]
          "project-distinct-limit",
          [ Project [ "region", "region"; "amount", "value" ]
            Distinct
            Limit(Slot.Param "pageSize", Slot.Lit 0) ]
          "group-by-every-aggregate",
          [ GroupBy(
                [ "region" ],
                [ agg "total" Sum "amount"
                  agg "average" Mean "amount"
                  agg "low" Min "amount"
                  agg "high" Max "amount"
                  agg "rows" Count "amount"
                  agg "middle" Median "amount"
                  agg "spread" StdDev "amount"
                  agg "opening" First "amount"
                  agg "closing" Last "amount"
                  agg "customers" CountDistinct "customer" ]
            ) ]
          "join-union-intersect-except",
          [ Join(Ref "regions", [ "region", "code" ], Left)
            Union(Ref "archive")
            Intersect(Ref "active")
            Except(Ref "excluded") ]
          "embedded-source", [ Join(Embedded(TransformVectorSample.unitTable "m" 3 0), [ "g", "g" ], Inner) ]
          "window-pivot-unpivot",
          [ Window
                { PartitionBy = [ "region" ]
                  OrderBy = [ "month", Asc ]
                  Fn = NTile 4
                  Of = "amount"
                  As = "quartile" }
            Pivot
                { Index = [ "region" ]
                  On = "month"
                  Values = "amount"
                  Agg = Sum }
            Unpivot([ "region" ], [ "jan"; "feb" ]) ]
          "expression-kinds",
          [ Derive("label", Case([ IsNull(Col "amount"), Lit(Str "none") ], Coalesce [ Col "name"; Lit(Str "?") ]))
            Derive("whole", Cast(IntType, ApplyFn(Round, [ Col "ratio" ])))
            Filter(
                Binary(
                    And,
                    Not(InList(Col "region", [ Lit(Str "north"); Param "extra" ])),
                    InParam(Col "code", "codes")
                )
            )
            Derive("today", Now NowGrain.Date)
            Derive(
                "share",
                Quotient(
                    Col "part",
                    Col "whole",
                    { Scale = Slot.Lit 2
                      Mode = RoundingMode.HalfEven }
                )
            )
            Derive(
                "rounded",
                Rounded(
                    Col "price",
                    { Scale = Slot.Param "places"
                      Mode = RoundingMode.HalfUp }
                )
            )
            Derive("km", Convert(Col "distance", unitOf "km"))
            Derive(
                "rate",
                Interpolate(
                    Col "age",
                    [ 0.0, 0.5; 18.0, 1.25; 65.0, 2.0 ],
                    InterpKind.MonotoneCubic,
                    InterpOutside.Clamp
                )
            ) ]
          "define-invoke",
          [ Define
                { Name = "margin"
                  Params =
                    [ { Name = "revenue"; Default = None }
                      { Name = "rate"
                        Default = Some(Slot.Lit(Cell.Decimal "0.2")) } ]
                  Body = Binary(Mul, Arg "revenue", Arg "rate") }
            Derive("margin", Call("margin", [ Col "revenue"; Lit(Cell.Decimal "0.15") ])) ]
          "fit-predict",
          [ Fit ols
            Predict
                { Fit = ols
                  Model = PredictModel.Training(Ref "history")
                  Scale = PredictScale.Response
                  Interval =
                    Some
                        { Kind = IntervalKind.Prediction
                          Level = Slot.Lit 0.95 } } ]
          "fit-glm-options",
          [ Fit
                { ols with
                    Family = FitFamily.Tweedie 1.5
                    Link = FitLink.Log
                    Weights = Some "exposure"
                    Offset = Some(ApplyFn(Ln, [ Col "exposure" ]))
                    Ridge = Some(Slot.Param "ridge")
                    Output = FitOutput.Summary
                    StdErrors = FitStdErrors.HC3 } ]
          "kmeans-bootstrap",
          [ KMeans
                { KMeansSpec.create [ "x", Col "x"; "y", Col "y" ] 3 9007199254740993L with
                    Standardise = true
                    Silhouette = true
                    Output = KMeansOutput.Centres }
            Bootstrap
                { BootstrapSpec.create [ agg "mean" Mean "amount" ] 200 -42L with
                    GroupBy = [ "region" ]
                    Cluster = Some "store"
                    Level = Some 0.9
                    Output = BootstrapOutput.Summary } ]
          "literal-cells",
          [ Filter(
                Coalesce
                    [ Lit(Int -2147483648)
                      Lit(Float 2.5)
                      Lit(Bool false)
                      Lit(Str "")
                      Lit(Cell.Date "2026-01-15")
                      Lit(Cell.Timestamp "2026-01-15T09:30:00.25Z")
                      Lit(Cell.Decimal "-12.05")
                      Lit Null ]
            ) ]
          "float-layout",
          [ Filter(
                Coalesce
                    [ Lit(Float 0.0001)
                      Lit(Float 0.00001)
                      Lit(Float 1e16)
                      Lit(Float 1e17)
                      Lit(Float 1e21)
                      Lit(Float 123.456)
                      Lit(Float 2.0)
                      Lit(Float -0.0)
                      Lit(Float 5e-324)
                      Lit(Float 1.7976931348623157e308)
                      Lit(Float 0.1) ]
            ) ]
          "text",
          [ Derive("greeting", Lit(Str "naïve café — 日本 \U0001F600"))
            Derive("escaped", Lit(Str "tab\there \"quoted\" back\\slash\u0001"))
            Filter(Binary(StartsWith, Col "name", Lit(Str "/"))) ]
          // Three pipelines with no canonical bytes: the hash refuses each rather than digest an alias.
          "refused-nan", [ Filter(Binary(Gt, Col "x", Lit(Float nan))) ]
          "refused-infinity",
          [ Fit
                { ols with
                    Control =
                        { ols.Control with
                            Tol = Slot.Lit infinity } } ]
          // Built from the code unit: the F# compiler writes a lone surrogate in a literal as U+FFFD.
          "refused-lone-surrogate", [ Derive("bad", Lit(Str("a" + string (char 0xD800) + "b"))) ] ]

    /// One line per sample: its id, then its hash, or `refused` and the refusal. The same on every host.
    let render () : string =
        pipelines
        |> List.map (fun (id, p) ->
            match Pipeline.hash p with
            | Ok h -> id + " " + h
            | Error e -> id + " refused " + e)
        |> String.concat "\n"
