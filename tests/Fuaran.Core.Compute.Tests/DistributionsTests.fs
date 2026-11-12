module Fuaran.Compute.Tests.DistributionsTests

// ============================================================================
//  Phase 618 — the suite over Fuaran.Compute.Distributions (DECISIONS.md D79):
//
//   * the laws hold on .NET (the node leg, LinearAlgebraTests, runs the same
//     laws and renders the same document under JavaScript);
//   * the committed law vectors are the package's answers, every computed one
//     carries a reference, and each is within its function's recorded bound;
//   * the vectors answer what was worked by hand;
//   * `Normal.cdf` and `Normal.quantile` are Elementary's functions bit for bit;
//   * the t table `Predict` read until this phase, kept here as data, is
//     answered by `StudentT.quantile` and `Normal.quantile` at every row;
//   * the source reaches no host mathematics call;
//   * the operation table is the public surface;
//   * the four families' names are declared once across the packages.
// ============================================================================

open System
open System.IO
open System.Reflection
open Expecto
open Fuaran.Core
open Fuaran.Compute.Elementary
open Fuaran.Compute.Distributions

let private committedPath () =
    Snapshots.repoFile (Path.Combine("conformance", "laws", DistributionsVectors.fileName))

let private lineOf (id: string) : string =
    DistributionsVectors.render().Split('\n')
    |> Array.find (fun l -> l.Contains(sprintf "\"id\": \"%s\"" id))

let private caseOf (id: string) : DistributionsVectors.Case =
    DistributionsVectors.cases |> List.find (fun c -> c.Id = id)

let private valueOf (id: string) : float = DistributionsVectors.answer (caseOf id)

let private bits (x: float) : int64 = BitConverter.DoubleToInt64Bits x

let private distributionsAssembly =
    typeof<DistributionsOperations.Exactness>.Assembly

/// The four families' modules, by their source names.
let private families = [ "Normal"; "StudentT"; "ChiSquare"; "FisherF" ]

let private sourceName (t: Type) : string =
    let n = t.Name.Split('`').[0]

    if n.EndsWith "Module" then
        n.Substring(0, n.Length - "Module".Length)
    else
        n

/// The public functions of the four families, spelled `<Module>.<function>` — the census
/// `DistributionsOperations.exactness` is held to.
let private publishedOperations () : Set<string> =
    distributionsAssembly.GetExportedTypes()
    |> Array.filter (fun t -> t.IsAbstract && t.IsSealed && not t.IsNested)
    |> Array.collect (fun t ->
        let name = sourceName t

        if List.contains name families then
            t.GetMethods(BindingFlags.Public ||| BindingFlags.Static)
            |> Array.filter (fun m -> not m.IsSpecialName)
            |> Array.map (fun m -> name + "." + m.Name)
        else
            [||])
    |> Set.ofArray

/// The coverage levels of the two-sided Student t table `Predict` read until Phase 618.
let private retiredLevels: float[] = [| 0.5; 0.8; 0.9; 0.95; 0.98; 0.99; 0.999 |]

/// That table (Phase 597, DECISIONS.md D56), kept as the function's test data: for a level `L` and `ν`
/// degrees of freedom the `t` with `P(|T| < t) = L`, each entry the nearest double to the exact
/// quantile, computed in 70-digit decimal arithmetic from Abramowitz and Stegun's closed forms 26.7.3
/// and 26.7.4 and checked by quadrature. One column a level; the row `0` is the normal distribution
/// (`ν = ∞`).
let private retiredRows: (int * float[]) list =
    [ 1,
      [| 1.0
         3.0776835371752536
         6.313751514675043
         12.706204736174705
         31.82051595377396
         63.65674116287158
         636.6192487687197 |]
      2,
      [| 0.816496580927726
         1.8856180831641267
         2.9199855803537256
         4.302652729749464
         6.964556734283274
         9.924843200918293
         31.59905457644362 |]
      3,
      [| 0.7648923284043453
         1.6377443536962102
         2.3533634348018238
         3.1824463052837095
         4.5407028585681335
         5.840909309733357
         12.923978636687483 |]
      4,
      [| 0.7406970841126826
         1.5332062740589438
         2.1318467863266504
         2.7764451051977943
         3.746947387979197
         4.604094871349993
         8.610301581379275 |]
      5,
      [| 0.7266868438004227
         1.475884048824481
         2.0150483733330242
         2.5705818356363155
         3.3649299989072188
         4.032142983555228
         6.86882662588111 |]
      6,
      [| 0.7175581964914126
         1.4397557472651483
         1.9431802805153031
         2.44691185114497
         3.1426684032909833
         3.70742802132478
         5.9588161788187595 |]
      7,
      [| 0.7111417780817864
         1.4149239276505086
         1.8945786050900073
         2.3646242515927853
         2.9979515668685286
         3.499483297350494
         5.407882520861725 |]
      8,
      [| 0.7063866126448386
         1.3968153097438647
         1.8595480375308984
         2.3060041352041667
         2.8964594477096224
         3.3553873313333953
         5.041305433373368 |]
      9,
      [| 0.7027221467513264
         1.3830287383966322
         1.8331129326562372
         2.2621571627982053
         2.821437925025809
         3.2498355415921263
         4.780912585931139 |]
      10,
      [| 0.6998120613124317
         1.3721836411103356
         1.8124611228116765
         2.228138851986275
         2.763769458112696
         3.1692726726169513
         4.586893858702636 |]
      11,
      [| 0.6974453275598815
         1.3634303180205407
         1.7958848187040442
         2.2009851600916397
         2.7180791838138623
         3.105806515539281
         4.436979338234449 |]
      12,
      [| 0.6954828655117926
         1.3562173340232053
         1.78228755564932
         2.178812829667229
         2.680997993120915
         3.054539589392902
         4.3177912836061845 |]
      13,
      [| 0.6938293042354409
         1.350171288780055
         1.770933395986873
         2.1603686564627926
         2.6503088379121915
         3.0122758387165787
         4.220831727707121 |]
      14,
      [| 0.6924170695700052
         1.3450303744546506
         1.7613101357748921
         2.144786687917804
         2.624494067590052
         2.976842734370835
         4.140454112738203 |]
      15,
      [| 0.6911969489584898
         1.3406056078504556
         1.7530503556925736
         2.1314495455597755
         2.6024802950111234
         2.946712883475239
         4.072765195903791 |]
      16,
      [| 0.6901322538105608
         1.3367571673273146
         1.74588367627625
         2.1199052992212546
         2.5834871852759913
         2.9207816224251
         4.014996327184056 |]
      17,
      [| 0.6891950751539389
         1.3333793897216268
         1.739606726075073
         2.109815577833317
         2.566933983724718
         2.8982305196774187
         3.9651262721190315 |]
      18,
      [| 0.6883638064662002
         1.330390943569909
         1.7340636066175388
         2.1009220402410387
         2.5523796301822514
         2.878440472738608
         3.9216458250851596 |]
      19,
      [| 0.6876214602039603
         1.3277282090267983
         1.7291328115213696
         2.0930240544083096
         2.5394831906239625
         2.860934606464979
         3.883405852592083 |]
      20,
      [| 0.6869544964488035
         1.3253407069850462
         1.7247182429207872
         2.085963447265865
         2.5279770027415736
         2.8453397097861086
         3.8495162749308274 |]
      21,
      [| 0.6863519907269535
         1.3231878738651726
         1.7207429028118786
         2.0796138447276804
         2.5176480160447423
         2.83135955802305
         3.8192771642744625 |]
      22,
      [| 0.6858050317218864
         1.3212367416133617
         1.7171443743802428
         2.0738730679040263
         2.5083245528990803
         2.8187560606001436
         3.792130671698391 |]
      23,
      [| 0.6853062780612957
         1.319460239816162
         1.713871527747048
         2.0686576104190486
         2.499866739494668
         2.807335683769999
         3.7676268043117807 |]
      24,
      [| 0.6848496272369818
         1.3178359336731496
         1.7108820799094284
         2.063898561628026
         2.492159473157756
         2.796939504774456
         3.7453986192900524 |]
      25,
      [| 0.6844299649042703
         1.3163450726738701
         1.7081407612518993
         2.0595385527532977
         2.485107175410763
         2.7874358136769706
         3.72514394972865 |]
      26,
      [| 0.6840429726828732
         1.3149718642705168
         1.7056179197592731
         2.055529438642873
         2.478629823591242
         2.778714533329683
         3.7066117434809107 |]
      27,
      [| 0.683684979131031
         1.3137029128292739
         1.7032884457221271
         2.0518305164802855
         2.472659911956007
         2.770682957122212
         3.689591713459236 |]
      28,
      [| 0.6833528429885058
         1.3125267815926667
         1.7011309342659315
         2.048407141795245
         2.4671400979674725
         2.7632624554614447
         3.6739064007012767 |]
      29,
      [| 0.6830438608216132
         1.311433647301551
         1.6991270265334977
         2.0452296421327043
         2.462021360150412
         2.7563859036706053
         3.659405019466333 |]
      30,
      [| 0.6827556933212926
         1.3104150253913955
         1.6972608865939578
         2.042272456301238
         2.4572615424005915
         2.7499956535672254
         3.645958635042022 |]
      40,
      [| 0.680672717164449
         1.3030770526071946
         1.6838510133356526
         2.0210753903062733
         2.4232567793348583
         2.7044592674331627
         3.550965760863311 |]
      50,
      [| 0.679428200326346
         1.2987136941948096
         1.6759050251630976
         2.008559112100761
         2.403271916674172
         2.6777932709408443
         3.496012881811139 |]
      60,
      [| 0.6786007206481358
         1.2958210935157308
         1.6706488649046365
         2.0002978220142604
         2.3901194726249133
         2.660283028855037
         3.460200469196356 |]
      80,
      [| 0.6775688463948343
         1.2922235830591335
         1.6641245785896674
         1.9900634212544461
         2.3738682729673433
         2.638690596344183
         3.416337458476946 |]
      100,
      [| 0.6769510430114715
         1.290074761346516
         1.6602343260853396
         1.9839715185235522
         2.364217366238482
         2.6258905214380177
         3.39049131116423 |]
      120,
      [| 0.6765397249125202
         1.288646233656378
         1.6576508993552357
         1.979930405082441
         2.3578246126487556
         2.617421145106866
         3.3734537685625003 |]
      200,
      [| 0.6757184114042202
         1.285798793994801
         1.6525081009108775
         1.9718962236339095
         2.3451370822594684
         2.600634436191558
         3.3398354062756783 |]
      500,
      [| 0.674980737965939
         1.2832470207103743
         1.6479068539295112
         1.9647198374673678
         2.333828955352305
         2.585697835141942
         3.31009115152267 |]
      1000,
      [| 0.6747351646070094
         1.2823987214609245
         1.6463788172854648
         1.9623390808264085
         2.330082674755513
         2.580754698065951
         3.300282648423913 |]
      0,
      [| 0.6744897501960817
         1.2815515655446004
         1.6448536269514726
         1.9599639845400543
         2.326347874040841
         2.575829303548901
         3.290526731491895 |] ]

/// The retired table's reading between two of its rows: linear in `1/ν`, the normal row at zero.
let private retiredBetween (column: int) (lower: int) (upper: int) (nu: int) : float =
    let at (v: int) =
        retiredRows |> List.find (fun (r, _) -> r = v) |> snd |> (fun qs -> qs[column])

    let x (v: int) = if v = 0 then 0.0 else 1.0 / float v
    at upper + (at lower - at upper) * (x nu - x upper) / (x lower - x upper)

[<Tests>]
let tests =
    testList
        "Phase 618 - distributions"
        [ testCase "distributionsLaws hold on .NET"
          <| fun () ->
              let laws = DistributionsLaws.laws 590 200
              Expect.equal laws.Length 5 "the family is the five laws"

              for name, verdict in laws do
                  match verdict with
                  | Ok() -> ()
                  | Error why -> failtestf "%s: %s" name why

          testCase "the committed law vectors are the package's answers"
          <| fun () ->
              let committed = File.ReadAllText(committedPath ())

              match CorpusFile.firstDifference committed (DistributionsVectors.render ()) with
              | None -> ()
              | Some d ->
                  failtestf
                      "%s is stale at line %d:\n  committed: %A\n  rendered:  %A\n  Remedy: dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws"
                      DistributionsVectors.fileName
                      d.Line
                      d.Committed
                      d.Rendered

          testCase "every computed vector carries a reference and is within its function's bound of it"
          <| fun () ->
              let referenced = DistributionsVectors.references |> List.map fst

              let computed =
                  DistributionsVectors.referenceCases ()
                  |> List.map (fun line -> line.Substring(0, line.IndexOf ' '))

              Expect.equal referenced computed "the references are the computed vectors, in file order"
              Expect.isTrue (referenced.Length > 120) "the family is not empty"

              let ids = DistributionsVectors.cases |> List.map (fun c -> c.Id)
              Expect.equal (List.distinct ids) ids "no vector id is used twice"

              for op, _ in DistributionsOperations.exactness do
                  Expect.isTrue
                      (DistributionsVectors.cases |> List.exists (fun c -> c.Op = op))
                      (sprintf "%s has vectors" op)

                  Expect.isTrue
                      (DistributionsVectors.referenceUlps |> List.exists (fun (o, _, _) -> o = op))
                      (sprintf "%s has a recorded bound" op)

              for id, h in DistributionsVectors.references do
                  let c = caseOf id

                  let bound = DistributionsVectors.boundOf c

                  let apart =
                      ElementaryVectors.ulpsApart (DistributionsVectors.answer c) (ElementaryVectors.ofHex h)

                  Expect.isLessThanOrEqual
                      apart
                      (int64 bound)
                      (sprintf "%s is %d ulps from its reference; %s's bound is %d" id apart c.Op bound)

                  Expect.stringContains
                      (lineOf id)
                      (sprintf "\"reference\": \"%s\", \"ulps\": %d" h apart)
                      (sprintf "%s states its reference and its distance" id)

              // Each bound is tight: its function's worst vector in that range is above half of it,
              // and a bound of zero says there is no such vector.
              for op, within, past in DistributionsVectors.referenceUlps do
                  for bound, far in [ within, false; past, true ] do
                      let distances =
                          DistributionsVectors.references
                          |> List.filter (fun (id, _) ->
                              (caseOf id).Op = op && DistributionsVectors.beyond (caseOf id) = far)
                          |> List.map (fun (id, h) ->
                              ElementaryVectors.ulpsApart (valueOf id) (ElementaryVectors.ofHex h))

                      let worst = if List.isEmpty distances then -1L else List.max distances

                      Expect.isTrue
                          (if bound = 0 then worst = -1L
                           elif bound = 1 then worst = 0L || worst = 1L
                           else worst * 2L > int64 bound)
                          (sprintf
                              "%s's bound %d (beyond forty thousand: %b) is the power of two at or above its worst vector, %d"
                              op
                              bound
                              far
                              worst)

          testCase "the vectors answer what was worked by hand"
          <| fun () ->
              let within (ulps: int64) (id: string) (want: float) =
                  let apart = ElementaryVectors.ulpsApart (valueOf id) want

                  Expect.isLessThanOrEqual
                      apart
                      ulps
                      (sprintf "%s is %.17g, %d ulps from %.17g" id (valueOf id) apart want)

              let exactly (id: string) (want: float) =
                  Expect.equal (bits (valueOf id)) (bits want) (sprintf "%s is %.17g exactly" id want)

              // The Cauchy distribution in closed form, F(x) = 1/2 + atan(x)/pi: F(-1) is one quarter
              // and the quartiles are -1 and 1.
              within 4L "t-cdf-cauchy-minus-one" 0.25
              within 4L "t-cdf-cauchy-one" 0.75
              within 16L "t-quantile-cauchy-upper-quartile" 1.0
              within 16L "t-quantile-cauchy-lower-quartile" -1.0
              // Two degrees of freedom, F(x) = 1/2 + x / (2 sqrt (2 + x^2)): at -sqrt 2, 1/2 - sqrt 2 / 4.
              within 8L "t-cdf-two-degrees" 0.14644660940672624
              // The chi-square with two degrees of freedom is the exponential: its median is 2 ln 2.
              within 4L "chi-cdf-two-median" 0.5
              within 4L "chi-quantile-two-median" 1.3862943611198906
              // F with equal degrees of freedom has median one.
              within 4L "f-cdf-one-one-median" 0.5
              within 4L "f-cdf-two-two-median" 0.5
              within 64L "f-quantile-one-one-median" 1.0
              within 16L "f-quantile-two-two-median" 1.0
              within 256L "f-quantile-tenth-both-median" 1.0
              // The square of a t with ten degrees of freedom at its 97.5 percent point is the 95 percent
              // point of F(1, 10).
              let t10 = valueOf "t-quantile-ten-97.5-percent"
              within 64L "f-quantile-one-ten-95-percent" (t10 * t10)

              // The special-value table, row by row.
              exactly "t-cdf-zero" 0.5
              exactly "t-cdf-negative-zero" 0.5
              exactly "t-cdf-negative-infinity" 0.0
              exactly "t-cdf-infinity" 1.0
              exactly "t-quantile-half" 0.0
              exactly "t-quantile-zero" Double.NegativeInfinity
              exactly "t-quantile-one" Double.PositiveInfinity
              exactly "t-quantile-half-degree-overflows" Double.NegativeInfinity
              exactly "chi-cdf-zero" 0.0
              exactly "chi-cdf-negative" 0.0
              exactly "chi-cdf-negative-infinity" 0.0
              exactly "chi-cdf-infinity" 1.0
              exactly "chi-quantile-zero" 0.0
              exactly "chi-quantile-one" Double.PositiveInfinity
              exactly "chi-quantile-thousandth-underflows" 0.0
              exactly "f-cdf-zero" 0.0
              exactly "f-cdf-negative" 0.0
              exactly "f-cdf-infinity" 1.0
              exactly "f-quantile-zero" 0.0
              exactly "f-quantile-one" Double.PositiveInfinity

              let refused =
                  DistributionsVectors.cases
                  |> List.filter (fun c ->
                      c.Input
                      |> List.exists (fun (name, x) ->
                          Double.IsNaN x
                          || (name = "p" && (x < 0.0 || x > 1.0))
                          || (name <> "x" && name <> "p" && not (x > 0.0 && x < Double.PositiveInfinity))))

              Expect.equal refused.Length 40 "the refusal rows"

              for c in refused do
                  Expect.isTrue (Double.IsNaN(DistributionsVectors.answer c)) (sprintf "%s is NaN" c.Id)

              // The quantile above one half is the negation of the one below, bit for bit.
              exactly "t-quantile-ten-2.5-percent" -t10

          testCase "Normal.cdf and Normal.quantile are Elementary's functions, bit for bit"
          <| fun () ->
              // One implementation under two names (DECISIONS.md D79 item 2): every normal vector of
              // this family, and a grid across each function's whole range.
              let mutable compared = 0

              for c in DistributionsVectors.cases do
                  match c.Op, c.Input with
                  | "Normal.cdf", [ _, x ] ->
                      compared <- compared + 1

                      Expect.equal
                          (ElementaryVectors.hex (Normal.cdf x))
                          (ElementaryVectors.hex (Elementary.normalCdf x))
                          c.Id
                  | "Normal.quantile", [ _, p ] ->
                      compared <- compared + 1

                      Expect.equal
                          (ElementaryVectors.hex (Normal.quantile p))
                          (ElementaryVectors.hex (Elementary.normalQuantile p))
                          c.Id
                  | _ -> ()

              Expect.equal compared 22 "every normal vector was compared"

              for i in -4100 .. 4100 do
                  let x = float i / 100.0
                  Expect.equal (bits (Normal.cdf x)) (bits (Elementary.normalCdf x)) (sprintf "cdf %g" x)

              for i in 1..9999 do
                  let p = float i / 10000.0

                  Expect.equal (bits (Normal.quantile p)) (bits (Elementary.normalQuantile p)) (sprintf "quantile %g" p)

          testCase "the retired t table is answered at every row, and between its rows it is not"
          <| fun () ->
              let twoSided (nu: int) (level: float) : float =
                  let tail = (1.0 - level) / 2.0

                  if nu = 0 then
                      -(Normal.quantile tail)
                  else
                      -(StudentT.quantile (float nu) tail)

              Expect.equal retiredRows.Length 40 "thirty-nine rows and the normal one"
              let mutable worst = 0L

              for nu, qs in retiredRows do
                  Expect.equal qs.Length retiredLevels.Length (sprintf "row %d has one entry a level" nu)

                  for c in 0 .. retiredLevels.Length - 1 do
                      let answer = twoSided nu retiredLevels[c]
                      let apart = ElementaryVectors.ulpsApart answer qs[c]
                      worst <- max worst apart

                      // The table's entries are correctly rounded; the function is within its own error
                      // of them (DECISIONS.md D79 item 6).
                      Expect.isLessThanOrEqual
                          apart
                          32L
                          (sprintf "nu %d, level %g: %.17g against the table's %.17g" nu retiredLevels[c] answer qs[c])

              Expect.isTrue (worst > 0L) "the function is not the table: it computes"

              // Where the table had no row it read between two in 1/nu, and that is what moved: at 35
              // degrees of freedom its answer was some five parts in a hundred thousand above the
              // quantile.
              let exact = twoSided 35 0.95
              let interpolated = retiredBetween 3 30 40 35
              Expect.isTrue (abs (exact - 2.0301079282503425) < 1e-13) "t(0.975, 35)"
              Expect.isTrue (interpolated - exact > 1e-5 && interpolated - exact < 2e-4) "the table's reading at 35"

              // And a level the table did not carry is a quantile like any other.
              Expect.isTrue (abs (twoSided 6 0.97 - 2.828927862488332) < 1e-12) "t(0.985, 6)"

          testCase "the package's source reaches no host mathematics call, writes no special function and no loop"
          <| fun () ->
              let source =
                  File.ReadAllText(
                      Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.Distributions", "Distributions.fs"))
                  )

              let hostCall =
                  Text.RegularExpressions.Regex(
                      @"Math\.\w+|(?<![\w.])(exp|log|log10|log2|pown|floor|ceil|round|truncate|abs|sin|cos|tan|sinh|cosh|tanh|atan|atan2|asin|acos)\s+[\w(]|\*\*"
                  )

              // Comment lines describe the mathematics in words; only code is held to the rule.
              let code =
                  source.Split('\n')
                  |> Array.filter (fun l -> not (l.TrimStart().StartsWith "//"))
                  |> String.concat "\n"

              let hits =
                  hostCall.Matches code
                  |> Seq.map (fun m -> m.Value)
                  |> Seq.distinct
                  |> List.ofSeq

              Expect.isEmpty
                  hits
                  "no host mathematics call in Distributions.fs (the square root is the F# operator over the premise's correctly rounded root; exp and ln are Elementary's)"

              // The package reads Elementary's special functions and the solvers' iteration.
              Expect.isTrue (code.Contains "Special.regularizedBeta") "the incomplete beta function is Special's"
              Expect.isTrue (code.Contains "Special.inverseRegularizedBeta") "the inverse is Special's"

              Expect.isTrue
                  (code.Contains "Special.regularizedBetaComplement"
                   && code.Contains "Special.inverseRegularizedBetaComplement")
                  "the complement and its inverse are Special's, read directly (DECISIONS.md D81)"

              Expect.isTrue (code.Contains "Special.regularizedGammaP") "the incomplete gamma function is Special's"
              Expect.isTrue (code.Contains "Root.bracketedNewton") "the one iteration is the solvers'"

              Expect.isFalse
                  (Text.RegularExpressions.Regex.IsMatch(code, @"\bwhile\b|\bfor\b|\blet\s+rec\b|\bmutable\b"))
                  "no loop is written in the package: its one iteration is the solver's"

          testCase "DistributionsOperations.exactness classifies exactly the published operations, every one Pinned"
          <| fun () ->
              let declared = DistributionsOperations.exactness |> List.map fst
              Expect.equal (List.distinct declared) declared "no operation is classified twice"
              let published = publishedOperations ()
              Expect.equal published.Count 8 "the census found the eight functions"
              Expect.equal (Set.ofList declared) published "the table is the published operation set"

              Expect.isTrue
                  (DistributionsOperations.exactness
                   |> List.forall (fun (_, e) -> e = DistributionsOperations.Exactness.Pinned))
                  "every operation here is Pinned (DECISIONS.md D79)"

              Expect.equal
                  (DistributionsOperations.exactnessOf "StudentT.quantile")
                  (Some DistributionsOperations.Exactness.Pinned)
                  "a lookup"

              Expect.equal (DistributionsOperations.exactnessOf "StudentT.density") None "no density is published"

          testCase "the four families are declared once across the packages, and no other package's name shadows them"
          <| fun () ->
              // Callers write `open Fuaran.Compute.Distributions` and `StudentT.quantile`; a module or
              // type of one of these names in another package, public or not, would shadow it in any
              // file that opens both (DECISIONS.md D74 item 2). First by source, which also sees what a
              // package keeps internal (the dataframe's `module internal StudentT` was one until this
              // phase):
              let named =
                  Text.RegularExpressions.Regex(
                      @"^\s*(module|type)\s+(private\s+|internal\s+)?(Normal|StudentT|ChiSquare|FisherF|DistributionsOperations|DistributionsLaws)\b"
                  )

              let declaring =
                  Directory.GetFiles(Snapshots.repoFile "src", "*.fs", SearchOption.AllDirectories)
                  |> Array.filter (fun path ->
                      not (path.Contains(sprintf "%cobj%c" Path.DirectorySeparatorChar Path.DirectorySeparatorChar))
                      && not (path.Contains(sprintf "%cbin%c" Path.DirectorySeparatorChar Path.DirectorySeparatorChar)))
                  |> Array.collect (fun path ->
                      File.ReadAllLines path
                      |> Array.choose (fun l ->
                          let m = named.Match l

                          if m.Success then
                              Some(
                                  Path.GetFileName(Path.GetDirectoryName path)
                                  + "/"
                                  + Path.GetFileName path
                                  + ": "
                                  + m.Groups[3].Value
                              )
                          else
                              None))
                  |> List.ofArray
                  |> List.sort

              Expect.equal
                  declaring
                  [ "Fuaran.Compute.Distributions/Distributions.fs: ChiSquare"
                    "Fuaran.Compute.Distributions/Distributions.fs: FisherF"
                    "Fuaran.Compute.Distributions/Distributions.fs: Normal"
                    "Fuaran.Compute.Distributions/Distributions.fs: StudentT"
                    "Fuaran.Compute.Distributions/DistributionsLaws.fs: DistributionsLaws"
                    "Fuaran.Compute.Distributions/DistributionsOperations.fs: DistributionsOperations" ]
                  "one declaration of each name, all in Fuaran.Compute.Distributions"

              // Then by the built assemblies: no exported top-level name of another package is one of
              // this package's.
              let mine =
                  distributionsAssembly.GetExportedTypes()
                  |> Array.filter (fun t -> not t.IsNested)
                  |> Array.map sourceName
                  |> Set.ofArray

              Expect.equal
                  mine
                  (set (families @ [ "DistributionsOperations"; "DistributionsLaws" ]))
                  "the package's top-level names"

              let others =
                  [ typeof<Fuaran.Compute.Solvers.StoppingRule>.Assembly
                    typeof<Fuaran.Compute.Elementary.SpecialValue>.Assembly
                    typeof<Fuaran.Compute.LinearAlgebra.Matrix>.Assembly
                    typeof<Fuaran.Compute.Regression.GlmFit>.Assembly
                    typeof<Fuaran.Compute.Transform>.Assembly
                    typeof<Fuaran.Compute.ColumnOp>.Assembly ]

              Expect.equal
                  (others |> List.map (fun a -> a.GetName().Name) |> List.distinct |> List.length)
                  6
                  "six other packages are read"

              let clashes =
                  [ for a in others do
                        for t in a.GetExportedTypes() do
                            if not t.IsNested && Set.contains (sourceName t) mine then
                                yield a.GetName().Name + ": " + sourceName t ]

              Expect.isEmpty clashes "no top-level name of the distributions is declared by another Compute package" ]
