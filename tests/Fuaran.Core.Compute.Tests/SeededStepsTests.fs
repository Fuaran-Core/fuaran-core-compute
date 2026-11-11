module Fuaran.Compute.Tests.SeededStepsTests

// ============================================================================
//  Phase 621 — the seeded steps, `Transform.KMeans` and `Transform.Bootstrap`
//  (`DECISIONS.md` D76), over the pinned generator `Rng`.
//
//  The same bytes in give the same bytes out, and another seed differs; a
//  clustering reads its features and nothing else; a resampling's draws are
//  `Rng.below`'s, re-derived here by hand; a summary's standard error is the
//  `StdDev` a `GroupBy` over the replicates answers, bit for bit, and its
//  interval the pinned quantile rule; the static walk; the wire; the planner's
//  barrier; the incremental seam; every refusal by name.
// ============================================================================

open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Elementary

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private unitOf (text: string) : UnitOfMeasure =
    match Unit.parse text with
    | Ok u -> u
    | Error e -> failtestf "not a unit: %s (%A)" text e

let private table (cols: (Field * Cell list) list) : Table =
    { Schema = cols |> List.map fst
      Columns = cols |> List.map (fun (f, cells) -> KitColumn.create f.Name f.Type cells) }

let private run (p: Transform list) (t: Table) = DataFrame.evalPipeline p t

let private refusedWith (fragment: string) (r: Result<Table, EvalError>) =
    match r with
    | Error e -> Expect.stringContains (DataFrame.errorString e) fragment "the refusal names it"
    | Ok t -> failtestf "expected a refusal naming %s, got %A" fragment t.Schema

let private bytes (t: Table) : string = ColumnCodec.encode (Embedded t)

let private column (name: string) (t: Table) : Cell list =
    t.Columns |> List.find (fun c -> c.Name = name) |> Column.toCells

let private floats (name: string) (t: Table) : float list =
    column name t
    |> List.map (function
        | Float v -> v
        | Int v -> float v
        | other -> failtestf "%s holds %A" name other)

let private ints (name: string) (t: Table) : int list =
    column name t
    |> List.map (function
        | Int v -> v
        | other -> failtestf "%s holds %A" name other)

let private bitsOf (x: float) : int64 = System.BitConverter.DoubleToInt64Bits x

/// The four-cluster plane: sixteen points, four about each of `(0, 0)`, `(10, 0)`, `(0, 10)` and
/// `(10, 10)`, interleaved so no cluster's rows are adjacent; `truth` names the corner, `label` is a
/// column no feature reads, `half` splits the rows into two groups of eight, `id` numbers them.
let private plane: Table =
    let corner i = i % 4
    let jitter i = float ((i * 7) % 5 - 2) / 8.0

    let x i =
        (if corner i % 2 = 1 then 10.0 else 0.0) + jitter i

    let y i =
        (if corner i >= 2 then 10.0 else 0.0) + jitter (i + 3)

    table
        [ Field.create "id" IntType, [ for i in 0..15 -> Int i ]
          Field.create "half" StringType, [ for i in 0..15 -> Str(if i < 8 then "first" else "second") ]
          Field.create "truth" IntType, [ for i in 0..15 -> Int(corner i) ]
          Field.create "label" StringType, [ for i in 0..15 -> Str(sprintf "row-%d" i) ]
          Field.create "x" FloatType |> Field.withUnit (unitOf "m"), [ for i in 0..15 -> Float(x i) ]
          Field.create "y" FloatType, [ for i in 0..15 -> Float(y i) ]
          Field.create "n" IntType, [ for i in 0..15 -> Int((i * 5) % 7) ] ]

let private features = [ "x", Col "x"; "y", Col "y" ]
let private kmeans = KMeansSpec.create features 4 20261111L

let private sumN: Agg = { Name = "total"; Fn = Sum; Of = "n" }
let private meanX: Agg = { Name = "mean_x"; Fn = Mean; Of = "x" }
let private bootstrap = BootstrapSpec.create [ sumN; meanX ] 25 7L

/// The rows a group's replicate draws, re-derived from the generator: `count` answers of `below`.
let private drawsOf (rng: Rng.Stream) (count: int) : int list =
    List.init count (fun _ -> Rng.below rng count)

/// D76's quantile rule, as the suite states it.
let private quantile (sorted: float[]) (q: float) : float =
    let h = float (sorted.Length - 1) * q
    let lo = int h

    if lo + 1 >= sorted.Length then
        sorted[sorted.Length - 1]
    else
        sorted[lo] + (h - float lo) * (sorted[lo + 1] - sorted[lo])

[<Tests>]
let tests =
    testList
        "Phase 621 - seeded steps"
        [ testCase "kmeans recovers the four corners of the plane, numbered by seed order, with every input column kept"
          <| fun _ ->
              let out = ok (run [ KMeans kmeans ] plane)

              Expect.equal
                  (out.Schema |> List.map _.Name)
                  ((plane.Schema |> List.map _.Name) @ [ "cluster" ])
                  "appended"

              for name in plane.Schema |> List.map _.Name do
                  Expect.equal (column name out) (column name plane) (name + " is carried unchanged")

              let pairs = List.zip (ints "truth" out) (ints "cluster" out) |> List.distinct
              Expect.equal pairs.Length 4 "each corner is one cluster and each cluster one corner"
              Expect.equal (pairs |> List.map snd |> List.sort) [ 1; 2; 3; 4 ] "clusters are numbered from one"

              let centres =
                  ok (
                      run
                          [ KMeans
                                { kmeans with
                                    Output = KMeansOutput.Centres } ]
                          plane
                  )

              Expect.equal (centres.Schema |> List.map _.Name) [ "cluster"; "x"; "y"; "size"; "withinSS" ] "the centres"
              Expect.equal (ints "cluster" centres) [ 1; 2; 3; 4 ] "one row per cluster, in seed order"
              Expect.equal (ints "size" centres) [ 4; 4; 4; 4 ] "four rows each"

              // A centre is its rows' mean, summed in row order.
              let assigned = ints "cluster" out

              for c in 1..4 do
                  let mean name =
                      let members =
                          List.zip assigned (floats name plane)
                          |> List.filter (fun (a, _) -> a = c)
                          |> List.map snd

                      List.fold (+) 0.0 members / float members.Length

                  Expect.equal
                      (bitsOf ((floats "x" centres)[c - 1]), bitsOf ((floats "y" centres)[c - 1]))
                      (bitsOf (mean "x"), bitsOf (mean "y"))
                      (sprintf "centre %d" c)

              let summary =
                  ok (
                      run
                          [ KMeans
                                { kmeans with
                                    Output = KMeansOutput.Summary } ]
                          plane
                  )

              Expect.equal
                  (summary.Schema |> List.map _.Name)
                  [ "k"; "restarts"; "iterations"; "withinSS"; "converged"; "seed" ]
                  "the summary, with no silhouette unasked"

              Expect.equal (column "k" summary) [ Int 4 ] "k"
              Expect.equal (column "converged" summary) [ Bool true ] "converged"
              Expect.equal (column "seed" summary) [ Cell.decimal "20261111" |> Option.get ] "the seed, exactly"

              Expect.equal
                  (bitsOf (floats "withinSS" summary).Head)
                  (bitsOf (List.fold (+) 0.0 (floats "withinSS" centres)))
                  "the total is the clusters' within-SS summed in cluster order"

          testCase "the same bytes in give the same bytes out, and the next seed differs"
          <| fun _ ->
              let grouped =
                  { kmeans with
                      GroupBy = [ "half" ]
                      K = Slot.Lit 2 }

              for p in
                  [ [ KMeans kmeans ]
                    [ KMeans grouped ]
                    [ Bootstrap bootstrap ]
                    [ Bootstrap
                          { bootstrap with
                              GroupBy = [ "half" ]
                              Level = Some 0.9
                              Output = BootstrapOutput.Summary } ] ] do
                  Expect.equal (bytes (ok (run p plane))) (bytes (ok (run p plane))) "a replay is byte-equal"

              // Go-red: were the seed not read, these would be equal.
              let reseeded (p: Transform) =
                  match p with
                  | KMeans s -> KMeans { s with Seed = Slot.Lit 20261112L }
                  | Bootstrap s -> Bootstrap { s with Seed = Slot.Lit 8L }
                  | other -> other

              for p in [ KMeans kmeans; Bootstrap bootstrap ] do
                  Expect.notEqual
                      (bytes (ok (run [ p ] plane)))
                      (bytes (ok (run [ reseeded p ] plane)))
                      "another seed answers other bytes"

          testCase "a clustering reads its features only: relabelling another column moves no assignment"
          <| fun _ ->
              let relabelled =
                  { plane with
                      Columns =
                          plane.Columns
                          |> List.map (fun c ->
                              if c.Name = "label" then
                                  KitColumn.create
                                      "label"
                                      StringType
                                      [ for i in 0..15 -> Str(sprintf "other-%d" (15 - i)) ]
                              else
                                  c) }

              Expect.equal
                  (column "cluster" (ok (run [ KMeans kmeans ] relabelled)))
                  (column "cluster" (ok (run [ KMeans kmeans ] plane)))
                  "the assignments"

          testCase "each group draws its own stream: the first group is the step over its rows alone"
          <| fun _ ->
              let firstHalf = Filter(Binary(Eq, Col "half", Lit(Str "first")))

              let grouped =
                  { kmeans with
                      GroupBy = [ "half" ]
                      K = Slot.Lit 2 }

              Expect.equal
                  (column "cluster" (ok (run [ KMeans grouped; firstHalf ] plane)))
                  (column "cluster" (ok (run [ firstHalf; KMeans grouped ] plane)))
                  "kmeans: group one is stream zero"

              let both = ok (run [ Bootstrap { bootstrap with GroupBy = [ "half" ] } ] plane)

              let alone =
                  ok (run [ firstHalf; Bootstrap { bootstrap with GroupBy = [ "half" ] } ] plane)

              Expect.equal
                  (column "total" both |> List.take 25)
                  (column "total" alone)
                  "bootstrap: group one is stream zero"

              Expect.notEqual
                  (column "total" both |> List.skip 25)
                  (column "total" alone)
                  "group two draws another stream"

          testCase "restarts keep the lowest total; standardising scales within the group; a constant feature is zero"
          <| fun _ ->
              let total (spec: KMeansSpec) =
                  (floats
                      "withinSS"
                      (ok (
                          run
                              [ KMeans
                                    { spec with
                                        Output = KMeansOutput.Summary } ]
                              plane
                      )))
                      .Head

              let three = { kmeans with K = Slot.Lit 3 }

              Expect.isLessThanOrEqual
                  (total { three with Restarts = 6 })
                  (total three)
                  "six seedings are no worse than the first"

              let standardised =
                  ok (
                      run
                          [ Derive("flat", Lit(Float 3.0))
                            KMeans
                                { kmeans with
                                    Features = features @ [ "flat", Col "flat" ]
                                    Standardise = true
                                    Output = KMeansOutput.Centres } ]
                          plane
                  )

              Expect.equal (floats "flat" standardised) [ 0.0; 0.0; 0.0; 0.0 ] "a constant feature standardises to zero"
              Expect.equal (ints "size" standardised) [ 4; 4; 4; 4 ] "and the corners are still found"

              Expect.isTrue
                  (standardised.Schema |> List.forall (fun f -> Option.isNone f.Unit))
                  "a standardised centre states no unit"

          testCase "rows that coincide: a cluster left empty takes the farthest row, and the passes converge"
          <| fun _ ->
              let same = table [ Field.create "x" FloatType, [ for _ in 0..4 -> Float 2.5 ] ]

              let spec = KMeansSpec.create [ "x", Col "x" ] 2 1L
              let out = ok (run [ KMeans spec ] same)
              Expect.equal (ints "cluster" out) [ 2; 1; 1; 1; 1 ] "the lowest row is moved to the empty cluster"

              let summary =
                  ok (
                      run
                          [ KMeans
                                { spec with
                                    Output = KMeansOutput.Summary } ]
                          same
                  )

              Expect.equal (column "converged" summary) [ Bool true ] "converged"
              Expect.equal (floats "withinSS" summary) [ 0.0 ] "nothing within"

          testCase "the silhouette is opt-in and is the stated mean width"
          <| fun _ ->
              let line =
                  table [ Field.create "x" FloatType, [ Float 0.0; Float 1.0; Float 10.0; Float 11.0 ] ]

              let summary =
                  ok (
                      run
                          [ KMeans
                                { KMeansSpec.create [ "x", Col "x" ] 2 3L with
                                    Silhouette = true
                                    Output = KMeansOutput.Summary } ]
                          line
                  )

              Expect.equal (summary.Schema |> List.map _.Name |> List.last) "silhouette" "the column"

              // Rows 0 and 11 are 1 from their neighbour and a mean 10.5 from the other pair; rows 1
              // and 10 are 1 and 9.5.
              let outer = (10.5 - 1.0) / 10.5
              let inner = (9.5 - 1.0) / 9.5
              let expected = (0.0 + outer + inner + inner + outer) / 4.0
              Expect.equal (bitsOf (floats "silhouette" summary).Head) (bitsOf expected) "the mean width, bit for bit"

          testCase "kmeans refuses by name"
          <| fun _ ->
              let withNull = table [ Field.create "x" FloatType, [ Float 1.0; Null; Float 3.0 ] ]

              run [ KMeans { kmeans with K = Slot.Lit 1 } ] plane
              |> refusedWith "kmeans: k must be at least 2"

              run [ KMeans { kmeans with K = Slot.Lit 17 } ] plane
              |> refusedWith "kmeans: k is 17 and group 1 has 16 rows"

              run
                  [ KMeans
                        { kmeans with
                            GroupBy = [ "half" ]
                            K = Slot.Lit 9 } ]
                  plane
              |> refusedWith "group 1 has 8 rows"

              run [ KMeans(KMeansSpec.create [ "x", Col "x" ] 2 1L) ] withNull
              |> refusedWith "kmeans: row 1: the feature 'x' is null"

              run [ KMeans { kmeans with Features = [] } ] plane
              |> refusedWith "kmeans: no features"

              run
                  [ KMeans
                        { kmeans with
                            Features = [ "label", Col "label" ] } ]
                  plane
              |> refusedWith "kmeans: the feature 'label' is a string column"

              run
                  [ KMeans
                        { kmeans with
                            Features = [ "x", Col "x"; "x", Col "y" ] } ]
                  plane
              |> refusedWith "kmeans: the feature 'x' is named twice"

              run [ KMeans { kmeans with Restarts = 0 } ] plane
              |> refusedWith "kmeans: restarts must be at least 1"

              run [ KMeans { kmeans with MaxIterations = 0 } ] plane
              |> refusedWith "kmeans: maxIterations must be at least 1"

              run [ Derive("cluster", Lit(Int 0)); KMeans kmeans ] plane
              |> refusedWith "kmeans: the input already has a column 'cluster'"

              run
                  [ KMeans
                        { kmeans with
                            Features = [ "huge", Binary(Div, Col "x", Lit(Float 0.0)) ] } ]
                  plane
              |> refusedWith "kmeans: row"

              run
                  [ KMeans
                        { kmeans with
                            Features = [ "size", Col "x" ]
                            Output = KMeansOutput.Centres } ]
                  plane
              |> refusedWith "kmeans: the centres table would name 'size' twice"

              run [ KMeans { kmeans with GroupBy = [ "nowhere" ] } ] plane
              |> refusedWith "nowhere"

              match run [ KMeans { kmeans with Seed = Slot.Param "seed" } ] plane with
              | Error(UnboundParam("seed", _)) -> ()
              | other -> failtestf "an unbound seed is refused by name, got %A" other

          testCase "the seed and k are slots: reported, bound from the env, substituted"
          <| fun _ ->
              let slotted =
                  [ KMeans
                        { kmeans with
                            K = Slot.Param "k"
                            Seed = Slot.Param "seed" }
                    Bootstrap
                        { bootstrap with
                            Replicates = Slot.Param "b"
                            Seed = Slot.Param "seed" } ]

              Expect.equal (Transform.paramsOf slotted) [ "k"; "seed"; "b" ] "the census"
              Expect.equal (Transform.slotParamsOf slotted) [ "k"; "seed"; "b" ] "the slot census"

              let env = Map.ofList [ "k", Int 4; "seed", Int 99; "b", Int 5 ]

              let literal =
                  [ KMeans { kmeans with Seed = Slot.Lit 99L }
                    Bootstrap
                        { bootstrap with
                            Replicates = Slot.Lit 5
                            Seed = Slot.Lit 99L } ]

              Expect.equal (Transform.substitute env slotted) literal "bound"

              Expect.equal
                  (bytes (ok (DataFrame.evalPipelineInEnv env [ slotted.Head ] plane)))
                  (bytes (ok (run [ literal.Head ] plane)))
                  "an env-bound seed is the literal's run"

              match DataFrame.evalPipelineInEnv (Map.ofList [ "k", Int 4; "seed", Str "x" ]) [ slotted.Head ] plane with
              | Error(TypeError detail) -> Expect.stringContains detail "kmeans seed" "the slot is named"
              | other -> failtestf "a seed bound to a string is refused, got %A" other

          testCase "the specs' own derivations: expressions, slot params and slot substitution"
          <| fun _ ->
              let k =
                  { kmeans with
                      K = Slot.Param "k"
                      Seed = Slot.Param "seed" }

              Expect.equal (KMeansSpec.exprs k) [ Col "x"; Col "y" ] "the features, in order"

              Expect.equal
                  (KMeansSpec.mapExprs (fun e -> Binary(Mul, e, Lit(Float 2.0))) k
                   |> KMeansSpec.exprs)
                  [ Binary(Mul, Col "x", Lit(Float 2.0)); Binary(Mul, Col "y", Lit(Float 2.0)) ]
                  "every feature mapped"

              Expect.equal (KMeansSpec.slotParams k) [ "k"; "seed" ] "k, then the seed"

              Expect.equal
                  (KMeansSpec.substituteSlots (Map.ofList [ "k", Int 4; "seed", Int -5 ]) k)
                  { kmeans with Seed = Slot.Lit -5L }
                  "an int cell binds each slot"

              Expect.equal
                  (KMeansSpec.substituteSlots (Map.ofList [ "k", Float 4.0; "seed", Str "5" ]) k)
                  k
                  "a cell of another shape binds nothing: the step refuses it by name when it runs"

              let b =
                  { bootstrap with
                      Replicates = Slot.Param "b"
                      Seed = Slot.Param "seed" }

              Expect.equal (BootstrapSpec.slotParams b) [ "b"; "seed" ] "the replicates, then the seed"

              Expect.equal
                  (BootstrapSpec.substituteSlots (Map.ofList [ "b", Int 25; "seed", Int 7 ]) b)
                  bootstrap
                  "bound"

          testCase "a replicate is the generator's draws: below n, n times, per replicate, from the group's stream"
          <| fun _ ->
              let out = ok (run [ Bootstrap bootstrap ] plane)
              Expect.equal (out.Schema |> List.map _.Name) [ "replicate"; "total"; "mean_x" ] "the replicates table"
              Expect.equal (ints "replicate" out) [ 1..25 ] "numbered from one"

              Expect.equal
                  ((out.Schema |> List.find (fun f -> f.Name = "mean_x")).Unit
                   |> Option.map Unit.render)
                  (Some "m")
                  "an aggregate keeps its unit"

              let rng = Rng.ofSeed 7L 0
              let n = ints "n" plane |> List.toArray
              let x = floats "x" plane |> List.toArray

              let expected =
                  [ for _ in 1..25 ->
                        let rows = drawsOf rng 16
                        rows |> List.sumBy (fun r -> n[r]), (rows |> List.fold (fun s r -> s + x[r]) 0.0) / 16.0 ]

              Expect.equal (ints "total" out) (expected |> List.map fst) "each replicate's sum"

              Expect.equal
                  (floats "mean_x" out |> List.map bitsOf)
                  (expected |> List.map (snd >> bitsOf))
                  "each replicate's mean, bit for bit"

          testCase "with a cluster column whole clusters are drawn, in first-appearance order"
          <| fun _ ->
              let out =
                  ok (
                      run
                          [ Bootstrap
                                { bootstrap with
                                    Cluster = Some "truth"
                                    Aggregates = [ sumN; { Name = "rows"; Fn = Count; Of = "n" } ] } ]
                          plane
                  )

              Expect.isTrue
                  (ints "rows" out |> List.forall (fun c -> c = 16))
                  "four clusters of four rows are drawn each time"

              let rng = Rng.ofSeed 7L 0
              let n = ints "n" plane |> List.toArray
              let truth = ints "truth" plane |> List.toArray

              let expected =
                  [ for _ in 1..25 ->
                        drawsOf rng 4
                        |> List.sumBy (fun cluster ->
                            [ 0..15 ]
                            |> List.filter (fun r -> truth[r] = cluster)
                            |> List.sumBy (fun r -> n[r])) ]

              Expect.equal (ints "total" out) expected "each replicate sums the clusters it drew"

          testCase
              "a summary states the estimate, the replicates' StdDev as a GroupBy answers it, and the pinned interval"
          <| fun _ ->
              let spec =
                  { bootstrap with
                      GroupBy = [ "half" ]
                      Replicates = Slot.Lit 40
                      Level = Some 0.9 }

              let replicates = ok (run [ Bootstrap spec ] plane)

              let summary =
                  ok (
                      run
                          [ Bootstrap
                                { spec with
                                    Output = BootstrapOutput.Summary } ]
                          plane
                  )

              Expect.equal
                  (summary.Schema |> List.map (fun f -> f.Name, f.Type))
                  [ "half", StringType
                    "aggregate", StringType
                    "estimate", FloatType
                    "std_error", FloatType
                    "lower", FloatType
                    "upper", FloatType ]
                  "the summary table"

              Expect.equal
                  (column "aggregate" summary)
                  [ Str "total"; Str "mean_x"; Str "total"; Str "mean_x" ]
                  "one row per group and aggregate"

              // The estimate is the aggregate over the group's own rows.
              let own = ok (run [ GroupBy([ "half" ], [ sumN; meanX ]) ] plane)

              Expect.equal
                  (floats "estimate" summary |> List.map bitsOf)
                  ([ (floats "total" own)[0]
                     (floats "mean_x" own)[0]
                     (floats "total" own)[1]
                     (floats "mean_x" own)[1] ]
                   |> List.map bitsOf)
                  "the estimates"

              // The standard error is the replicate column's StdDev, as a GroupBy over the replicates
              // table answers it.
              let spread =
                  ok (
                      run
                          [ GroupBy(
                                [ "half" ],
                                [ { Name = "se_total"
                                    Fn = StdDev
                                    Of = "total" }
                                  { Name = "se_mean_x"
                                    Fn = StdDev
                                    Of = "mean_x" } ]
                            ) ]
                          replicates
                  )

              Expect.equal
                  (floats "std_error" summary |> List.map bitsOf)
                  ([ (floats "se_total" spread)[0]
                     (floats "se_mean_x" spread)[0]
                     (floats "se_total" spread)[1]
                     (floats "se_mean_x" spread)[1] ]
                   |> List.map bitsOf)
                  "the standard errors, bit for bit"

              // The interval is the pinned quantile of the group's sorted replicates.
              let sorted name skip =
                  floats name replicates
                  |> List.skip skip
                  |> List.take 40
                  |> List.sort
                  |> List.toArray

              let expectedBounds =
                  [ for skip, name in [ 0, "total"; 0, "mean_x"; 40, "total"; 40, "mean_x" ] ->
                        let s = sorted name skip
                        let tail = (1.0 - 0.9) * 0.5
                        bitsOf (quantile s tail), bitsOf (quantile s (1.0 - tail)) ]

              Expect.equal
                  (List.zip (floats "lower" summary |> List.map bitsOf) (floats "upper" summary |> List.map bitsOf))
                  expectedBounds
                  "the percentile interval, bit for bit"

              Expect.isTrue
                  (List.zip (floats "lower" summary) (floats "upper" summary)
                   |> List.forall (fun (l, u) -> l <= u))
                  "lower is not above upper"

          testCase "sanity: the mean of the replicates' sums approaches the group's sum as the replicates grow"
          <| fun _ ->
              let full = ints "n" plane |> List.sum |> float

              // Stated bound: the mean of B replicates lies within four of its own standard errors,
              // 4 · sd / √B, of the group's sum. Not a bit law.
              for b in [ 200; 2000 ] do
                  let sums =
                      floats
                          "total"
                          (ok (
                              run
                                  [ Bootstrap
                                        { bootstrap with
                                            Replicates = Slot.Lit b } ]
                                  plane
                          ))

                  let mean = List.sum sums / float b

                  let sd =
                      sqrt ((sums |> List.sumBy (fun s -> (s - mean) * (s - mean))) / float (b - 1))

                  Expect.isLessThan (abs (mean - full)) (4.0 * sd / sqrt (float b)) (sprintf "B = %d" b)

          testCase "bootstrap refuses by name"
          <| fun _ ->
              let holed =
                  table
                      [ Field.create "unit" StringType, [ Str "a"; Null; Str "b" ]
                        Field.create "v" FloatType, [ Float 1.0; Float 2.0; Float 3.0 ]
                        Field.create "w" FloatType, [ Null; Null; Null ] ]

              run
                  [ Bootstrap
                        { bootstrap with
                            Replicates = Slot.Lit 0 } ]
                  plane
              |> refusedWith "bootstrap: replicates must be at least 1"

              run
                  [ Bootstrap
                        { BootstrapSpec.create [ { Name = "s"; Fn = Sum; Of = "v" } ] 3 1L with
                            Cluster = Some "unit" } ]
                  holed
              |> refusedWith "bootstrap: row 1: the cluster column 'unit' is null"

              // An aggregate the group cannot evaluate: a sum over strings.
              match run [ Bootstrap(BootstrapSpec.create [ { Name = "s"; Fn = Sum; Of = "label" } ] 3 1L) ] plane with
              | Error(AggError detail) -> Expect.stringContains detail "sum" "the aggregate is named"
              | other -> failtestf "a sum over strings is refused, got %A" other

              // And one a summary cannot state: every replicate's mean of an all-null column is null.
              run
                  [ Bootstrap
                        { BootstrapSpec.create [ { Name = "m"; Fn = Mean; Of = "w" } ] 3 1L with
                            Output = BootstrapOutput.Summary } ]
                  holed
              |> refusedWith "bootstrap: group 1, replicate 1: the aggregate 'm' is null"

              run [ Bootstrap { bootstrap with Aggregates = [] } ] plane
              |> refusedWith "bootstrap: no aggregates"

              for level in [ 0.0; 1.0; 1.5; nan ] do
                  run [ Bootstrap { bootstrap with Level = Some level } ] plane
                  |> refusedWith "bootstrap: the level must lie strictly between 0 and 1"

              run
                  [ Bootstrap
                        { bootstrap with
                            Aggregates = [ sumN; { sumN with Fn = Max } ] } ]
                  plane
              |> refusedWith "bootstrap: the replicates table would name 'total' twice"

              run
                  [ Bootstrap
                        { bootstrap with
                            Aggregates = [ { sumN with Name = "replicate" } ] } ]
                  plane
              |> refusedWith "would name 'replicate' twice"

              run
                  [ Bootstrap
                        { bootstrap with
                            Aggregates =
                                [ { Name = "first"
                                    Fn = First
                                    Of = "label" } ]
                            Output = BootstrapOutput.Summary } ]
                  plane
              |> refusedWith "bootstrap: the aggregate 'first' is a string column"

              run
                  [ Bootstrap
                        { bootstrap with
                            Aggregates = [ { sumN with Of = "nowhere" } ] } ]
                  plane
              |> refusedWith "nowhere"

              run
                  [ Bootstrap
                        { bootstrap with
                            Cluster = Some "nowhere" } ]
                  plane
              |> refusedWith "nowhere"

          testCase "the static walk states each table's names, types and units, every column pinned"
          <| fun _ ->
              for p in
                  [ [ KMeans kmeans ]
                    [ KMeans
                          { kmeans with
                              GroupBy = [ "half" ]
                              K = Slot.Lit 2
                              Output = KMeansOutput.Centres } ]
                    [ KMeans
                          { kmeans with
                              Standardise = true
                              Output = KMeansOutput.Centres } ]
                    [ KMeans
                          { kmeans with
                              GroupBy = [ "half" ]
                              K = Slot.Lit 2
                              Silhouette = true
                              Output = KMeansOutput.Summary } ]
                    [ Bootstrap { bootstrap with GroupBy = [ "half" ] } ]
                    [ Bootstrap
                          { bootstrap with
                              GroupBy = [ "half" ]
                              Level = Some 0.8
                              Output = BootstrapOutput.Summary } ]
                    [ Bootstrap
                          { bootstrap with
                              Output = BootstrapOutput.Summary } ] ] do
                  let out = ok (run p plane)

                  match SchemaWalk.ofPipeline plane.Schema p with
                  | SchemaKnowledge.Closed cols ->
                      Expect.equal
                          (cols |> List.map (fun c -> c.Name, c.Type, c.Unit |> Option.map Unit.render))
                          (out.Schema
                           |> List.map (fun f -> f.Name, Some f.Type, f.Unit |> Option.map Unit.render))
                          "names, types and units"

                      Expect.isTrue
                          (cols |> List.forall (fun c -> c.Exactness = Fuaran.Compute.Exactness.Pinned))
                          "every column pinned"
                  | other -> failtestf "the walk is closed, got %A" other

          testCase "the wire: a canonical form, a required seed written as digits, a round trip"
          <| fun _ ->
              let big =
                  { kmeans with
                      GroupBy = [ "half" ]
                      Standardise = true
                      Seed = Slot.Lit System.Int64.MinValue
                      Silhouette = true
                      Output = KMeansOutput.Summary }

              let clustered =
                  { bootstrap with
                      GroupBy = [ "half" ]
                      Cluster = Some "truth"
                      Seed = Slot.Lit 9007199254740993L
                      Level = Some 0.95
                      Output = BootstrapOutput.Summary }

              for p in
                  [ [ KMeans kmeans ]
                    [ KMeans big ]
                    [ KMeans
                          { kmeans with
                              K = Slot.Param "k"
                              Seed = Slot.Param "seed" } ]
                    [ Bootstrap bootstrap ]
                    [ Bootstrap clustered ] ] do
                  Expect.equal (DataFrameCodec.decodePipeline (DataFrameCodec.encodePipeline p)) (Ok p) "round trip"

              Expect.equal
                  (DataFrameCodec.encodePipeline [ KMeans(KMeansSpec.create [ "x", Col "x" ] 3 42L) ])
                  """[{"$type":"kmeans","features":[{"expr":{"$type":"col","name":"x"},"name":"x"}],"groupBy":[],"k":3,"maxIterations":100,"output":"assignments","restarts":1,"seed":"42","silhouette":false,"standardise":false}]"""
                  "the kmeans step's canonical bytes"

              Expect.equal
                  (DataFrameCodec.encodePipeline [ Bootstrap(BootstrapSpec.create [ sumN ] 10 -3L) ])
                  """[{"$type":"bootstrap","aggregates":[{"fn":"sum","name":"total","of":"n"}],"groupBy":[],"output":"replicates","replicates":10,"seed":"-3"}]"""
                  "the bootstrap step's canonical bytes"

              let kmeansWith (seed: string) =
                  sprintf
                      """[{"$type":"kmeans","features":[{"expr":{"$type":"col","name":"x"},"name":"x"}],"groupBy":[],"k":3,"maxIterations":100,"output":"assignments","restarts":1,"seed":%s,"silhouette":false,"standardise":false}]"""
                      seed

              Expect.isOk (DataFrameCodec.decodePipeline (kmeansWith "\"9223372036854775807\"")) "the largest seed"
              Expect.isOk (DataFrameCodec.decodePipeline (kmeansWith "\"-9223372036854775808\"")) "the smallest seed"

              // A seed is canonical digits in a string: no number, no sign but a minus, no leading zero,
              // no negative zero, nothing outside the 64-bit range.
              for seed in
                  [ "42"
                    "\"+42\""
                    "\"042\""
                    "\"-0\""
                    "\"\""
                    "\"-\""
                    "\"4 2\""
                    "\"9223372036854775808\""
                    "\"-9223372036854775809\""
                    "\"12345678901234567890\"" ] do
                  Expect.isError
                      (DataFrameCodec.decodePipeline (kmeansWith seed))
                      (sprintf "the seed %s is refused" seed)

              // The seed is required: a step without one does not decode.
              Expect.isError
                  (DataFrameCodec.decodePipeline
                      """[{"$type":"bootstrap","aggregates":[{"fn":"sum","name":"total","of":"n"}],"groupBy":[],"output":"replicates","replicates":10}]""")
                  "no seed, no step"

              Expect.isError
                  (DataFrameCodec.decodePipeline
                      """[{"$type":"bootstrap","aggregates":[],"groupBy":[],"output":"centres","replicates":10,"seed":"1"}]""")
                  "an output the step does not answer"

          testCase "a seeded step is a planner barrier, and is not total"
          <| fun _ ->
              let late = Filter(Binary(Gt, Col "x", Lit(Float 5.0)))

              for step in [ KMeans kmeans; Bootstrap bootstrap ] do
                  Expect.isFalse (Plan.isTotal plane.Schema step) "a seeded step can refuse"

              // A filter moved ahead of the clustering would change the rows it clusters, and a column
              // pruned ahead of it is one the assignments carry: nothing is rewritten across the step.
              let around (step: Transform) (last: Transform) =
                  [ Derive("spare", Lit(Int 1)); step; late; last ]

              let written = around (KMeans kmeans) (Project [ "id", "id"; "cluster", "cluster" ])
              Expect.equal (Plan.rewrite plane.Schema written) written "nothing is rewritten across a seeded step"

              // The control: the same pipeline around a sort is rewritten, so the equality above is
              // the barrier's doing.
              let control = around (Sort [ Slot.Lit "x", Asc ]) (Project [ "id", "id" ])
              Expect.notEqual (Plan.rewrite plane.Schema control) control "the planner does rewrite around a sort"

              Expect.equal
                  (bytes (ok (run written plane)))
                  (bytes (ok (DataFrame.evalPipelineAsWritten written plane)))
                  "planned and as written agree"

          testCase "the incremental seam re-evaluates a seeded step whole, by name, byte-equal to a full evaluation"
          <| fun _ ->
              for step, verb in [ KMeans kmeans, "kmeans"; Bootstrap bootstrap, "bootstrap" ] do
                  match Incremental.classifyStep true step with
                  | FallBack(StepNotRowLocal v) -> Expect.equal v verb "the verb's name"
                  | other -> failtestf "expected the named fall-back, got %A" other

              let idw = RowIdentity.byColumn "id"

              for p in
                  [ [ KMeans kmeans ]
                    [ KMeans
                          { kmeans with
                              Output = KMeansOutput.Centres } ]
                    [ Bootstrap bootstrap ]
                    [ Bootstrap
                          { bootstrap with
                              Level = Some 0.9
                              Output = BootstrapOutput.Summary } ] ] do
                  let before = ok (run [ Transform.limit 12 0 ] plane)
                  let state = ok (Incremental.primeOn idw p before)
                  let delta = ok (Delta.diff idw before plane)
                  let refreshed = ok (Incremental.refreshOn idw p state delta plane)

                  Expect.equal
                      (bytes (Incremental.result refreshed))
                      (bytes (ok (run p plane)))
                      "byte-equal to a full evaluation" ]
