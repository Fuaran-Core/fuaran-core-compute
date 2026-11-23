module Fuaran.Compute.Tests.ElementaryTests

// ---------------------------------------------------------------------------
// Phase 593 — the pinned elementary functions, held on both hosts; their own
// package, Fuaran.Compute.Elementary, since Phase 630 (DECISIONS.md D74).
//
//   * `ElementaryLaws.laws` on .NET (the node leg runs them under Fable beside
//     the linear-algebra laws, in LinearAlgebraTests).
//   * The committed hard-input vectors (`conformance/laws/elementary-laws.json`)
//     are the package's answers, rendered through the one corpus writer, each
//     computed vector within one ulp of its correctly rounded reference.
//   * The source reaches no host mathematics call.
//   * `ElementaryOperations.exactness` is the published operation set, both ways.
//   * Phase 725 (DECISIONS.md D84): the trigonometric vectors answer what was
//     worked by hand, `sinCos` is the pair of `sin` and `cos`, and each
//     polynomial summed in another order inside its shipped path answers
//     another bit at its witness.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Reflection
open Expecto
open Fuaran.Core
open Fuaran.Compute.Elementary

let private bits (x: float) = BitConverter.DoubleToInt64Bits x

let private committedPath () =
    Snapshots.repoFile (Path.Combine("conformance", "laws", ElementaryVectors.fileName))

/// The vector with this id, as the document's line for it.
let private lineOf (id: string) : string =
    SpecialVectors.render().Split('\n')
    |> Array.find (fun l -> l.Contains(sprintf "\"id\": \"%s\"" id))

/// The public functions of the package's operation module, spelled `<Module>.<function>` — the census
/// `ElementaryOperations.exactness` is held to.
let private publishedOperations () : Set<string> =
    typeof<SpecialValue>.Assembly.GetExportedTypes()
    |> Array.filter (fun t -> t.IsAbstract && t.IsSealed && t.Namespace = "Fuaran.Compute.Elementary")
    |> Array.collect (fun t ->
        let name =
            if t.Name.EndsWith "Module" then
                t.Name.Substring(0, t.Name.Length - "Module".Length)
            else
                t.Name

        if name = "Elementary" || name = "Rng" || name = "Special" then
            t.GetMethods(BindingFlags.Public ||| BindingFlags.Static)
            |> Array.filter (fun m -> not m.IsSpecialName)
            |> Array.map (fun m -> name + "." + m.Name)
        else
            [||])
    |> Set.ofArray

[<Tests>]
let tests =
    testList
        "Phase 593 - elementary functions"
        [ testCase "elementaryLaws hold on .NET"
          <| fun () ->
              for name, verdict in ElementaryLaws.laws 590 200 do
                  match verdict with
                  | Ok() -> ()
                  | Error why -> failtestf "%s: %s" name why

          testCase "the committed law vectors are the package's answers"
          <| fun () ->
              let committed = File.ReadAllText(committedPath ())

              match CorpusFile.firstDifference committed (SpecialVectors.render ()) with
              | None -> ()
              | Some d ->
                  failtestf
                      "%s is stale at line %d:\n  committed: %A\n  rendered:  %A\n  Remedy: dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws"
                      ElementaryVectors.fileName
                      d.Line
                      d.Committed
                      d.Rendered

          testCase "every elementary vector with a reference is within one ulp of it, and says so in its ulps cell"
          <| fun () ->
              let referenced =
                  ElementaryVectors.cases
                  |> List.choose (fun c ->
                      ElementaryVectors.references
                      |> List.tryFind (fun (id, _) -> id = c.Id)
                      |> Option.map (fun (_, h) -> c, h))

              Expect.equal referenced.Length ElementaryVectors.references.Length "every reference names a vector"
              Expect.isGreaterThan referenced.Length 60 "the hard inputs are referenced"

              for c, h in referenced do
                  let line = lineOf c.Id
                  Expect.stringContains line (sprintf "\"reference\": \"%s\"" h) (c.Id + " carries its reference")

                  let ulps =
                      line.Substring(line.IndexOf "\"ulps\": " + 8).TrimEnd(' ', '}', ',', '\r')
                      |> int

                  Expect.isLessThanOrEqual
                      ulps
                      ElementaryVectors.referenceUlps
                      (sprintf "%s is %d ulps from the reference" c.Id ulps)

          testCase "the elementary vectors answer what was worked by hand"
          <| fun () ->
              let has (id: string) (fragment: string) =
                  Expect.stringContains (lineOf id) fragment (sprintf "%s answers %s" id fragment)

              let h = ElementaryVectors.hex
              has "exp-zero" (sprintf "\"value\": \"%s\", \"table\": \"compute\"" (h 1.0))

              has
                  "exp-above-overflow-edge"
                  (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h Double.PositiveInfinity))

              has "exp-minus-thousand" (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h 0.0))
              has "exp-nan" "\"value\": \"nan\", \"table\": \"nan\""
              has "ln-zero" "\"value\": \"nan\", \"table\": \"undefined\""
              has "ln-one" (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h 0.0))
              has "ln-two" (sprintf "\"value\": \"%s\"" (h 0.6931471805599453))
              has "log10-thousand" (sprintf "\"value\": \"%s\"" (h 3.0))
              has "pow-square-shortcut" (sprintf "\"value\": \"%s\"" (h 100.0))
              has "pow-root-shortcut" (sprintf "\"value\": \"%s\"" (h (sqrt 2.0)))
              has "pow-negative-zero-odd" (sprintf "\"value\": \"%s\", \"table\": \"compute\"" (h -0.0))
              has "pow-negative-base-non-integer" "\"value\": \"nan\", \"table\": \"undefined\""
              has "pow-one-nan" "\"value\": \"nan\", \"table\": \"nan\""
              has "pow-zero-zero" (sprintf "\"value\": \"%s\", \"table\": \"value\"" (h 1.0))
              has "pow-two-cubed" (sprintf "\"value\": \"%s\"" (h 8.0))

          testCase
              "the host's own libm answers other bits on at least one elementary vector, so the byte comparison sees a port that cheats"
          <| fun () ->
              let differing =
                  ElementaryVectors.cases
                  |> List.choose (fun c ->
                      let scalar name =
                          c.Input |> List.find (fun (n, _) -> n = name) |> snd

                      match c.Op with
                      | "exp" -> Some(c.Id, Elementary.exp (scalar "x"), Math.Exp(scalar "x"))
                      | "ln" -> Some(c.Id, Elementary.ln (scalar "x"), Math.Log(scalar "x"))
                      | "log10" -> Some(c.Id, Elementary.log10 (scalar "x"), Math.Log10(scalar "x"))
                      | "pow" -> Some(c.Id, Elementary.pow (scalar "x") (scalar "y"), Math.Pow(scalar "x", scalar "y"))
                      | _ -> None)
                  |> List.filter (fun (_, pinned, host) ->
                      not (Double.IsNaN pinned) && not (Double.IsNaN host) && bits pinned <> bits host)

              Expect.isNonEmpty differing "the host's libm and the pinned routine differ somewhere on the vectors"

          testCase
              "the host's own libm answers other bits on at least one trigonometric vector of each function (Phase 725)"
          <| fun () ->
              for op, pinned, host in
                  [ "sin",
                    (fun (c: ElementaryVectors.Case) -> Elementary.sin (snd c.Input.Head)),
                    (fun x _ -> Math.Sin x)
                    "cos", (fun c -> Elementary.cos (snd c.Input.Head)), (fun x _ -> Math.Cos x)
                    "atan", (fun c -> Elementary.atan (snd c.Input.Head)), (fun x _ -> Math.Atan x)
                    "atan2",
                    (fun c -> Elementary.atan2 (snd c.Input.Head) (snd c.Input.Tail.Head)),
                    (fun y x -> Math.Atan2(y, x)) ] do
                  let differing =
                      ElementaryVectors.cases
                      |> List.filter (fun c -> c.Op = op)
                      |> List.filter (fun c ->
                          let first = snd c.Input.Head
                          let second = if c.Input.Length > 1 then snd c.Input.Tail.Head else 0.0
                          let ours = pinned c
                          let theirs = host first second
                          not (Double.IsNaN ours) && not (Double.IsNaN theirs) && bits ours <> bits theirs)

                  Expect.isNonEmpty differing (sprintf "the host's %s differs from the pinned one on a vector" op)

          testCase "the trigonometric vectors answer what was worked by hand (Phase 725)"
          <| fun () ->
              let has (id: string) (fragment: string) =
                  Expect.stringContains (lineOf id) fragment (sprintf "%s answers %s" id fragment)

              let h = ElementaryVectors.hex

              let valued (x: float) =
                  sprintf "\"value\": \"%s\", \"table\": \"value\"" (h x)

              let computed (x: float) =
                  sprintf "\"value\": \"%s\", \"table\": \"compute\"" (h x)

              has "sin-zero" (valued 0.0)
              has "sin-negative-zero" (valued -0.0)
              has "sin-infinity" "\"value\": \"nan\", \"table\": \"undefined\""
              has "sin-nan" "\"value\": \"nan\", \"table\": \"nan\""
              has "sin-min-subnormal" (computed Double.Epsilon)
              has "sin-nearest-any-multiple" (computed 1.0)
              has "cos-zero" (computed 1.0)
              has "cos-negative-infinity" "\"value\": \"nan\", \"table\": \"undefined\""
              has "cos-nearest-any-multiple" (computed -4.687165924254628e-19)
              has "sin-1e22" (computed -0.8522008497671888)
              has "atan-negative-zero" (valued -0.0)
              has "atan-infinity" (valued 1.5707963267948966)
              has "atan-negative-infinity" (valued -1.5707963267948966)
              has "atan-one" (computed 0.7853981633974483)
              has "atan-max" (computed 1.5707963267948966)
              has "atan2-zero-zero" (valued 0.0)
              has "atan2-zero-negative-zero" (valued 3.141592653589793)
              has "atan2-negative-zero-zero" (valued -0.0)
              has "atan2-negative-zero-negative-zero" (valued -3.141592653589793)
              has "atan2-negative-zero-x" (valued -1.5707963267948966)
              has "atan2-infinity-negative-infinity" (valued 2.356194490192345)
              has "atan2-negative-finite-infinity" (valued -0.0)
              has "atan2-finite-negative-infinity" (valued 3.141592653589793)
              has "atan2-nan-zero" "\"value\": \"nan\", \"table\": \"nan\""
              has "atan2-first-diagonal" (computed 0.7853981633974483)
              has "atan2-second-diagonal" (computed 2.356194490192345)
              has "atan2-third-diagonal" (computed -2.356194490192345)
              has "atan2-beside-negative-x-axis-below" (computed -3.141592653589793)
              has "atan2-quotient-overflows" (computed 1.5707963267948966)

          testCase "a sinCos vector is its sin vector and its cos vector, bit for bit (Phase 725)"
          <| fun () ->
              let pairs = ElementaryVectors.cases |> List.filter (fun c -> c.Op = "sinCos")
              Expect.isGreaterThan pairs.Length 8 "the pair is asked across the tables and the reductions"

              for c in pairs do
                  let x = snd c.Input.Head
                  let line = lineOf c.Id

                  Expect.stringContains
                      line
                      (sprintf
                          "\"sin\": \"%s\", \"cos\": \"%s\""
                          (ElementaryVectors.hex (Elementary.sin x))
                          (ElementaryVectors.hex (Elementary.cos x)))
                      (c.Id + " is the pair")

          testCase
              "a polynomial summed in another order, inside the shipped path, answers another bit at its witness, so the byte comparison sees a transcription that reorders (Phase 725)"
          <| fun () ->
              // Each function's path is written out here once, with the sum of its polynomial a
              // parameter: the square, the correction and the last additions are the package's, so the
              // path under the pinned order must BE the package, and whatever the other order moves, the
              // order moved. The sine and the cosine are pinned to Horner's rule and reordered to their
              // even and odd halves summed apart; the arc tangent is pinned to its halves and reordered
              // to Horner's rule. Each is as good a polynomial; only one is the pinned order.
              let s =
                  [| -0.16666666666666666
                     0.008333333333333333
                     -0.0001984126984126984
                     2.7557319223985893e-06
                     -2.505210838544172e-08
                     1.6059043836821613e-10
                     -7.647163731819816e-13
                     2.8114572543455206e-15
                     -8.22063524662433e-18 |]

              let c =
                  [| -0.5
                     0.041666666666666664
                     -0.001388888888888889
                     2.48015873015873e-05
                     -2.755731922398589e-07
                     2.08767569878681e-09
                     -1.1470745597729725e-11
                     4.779477332387385e-14
                     -1.5619206968586225e-16 |]

              let at =
                  [| 0.3333333333333293
                     -0.19999999999876483
                     0.14285714272503466
                     -0.11111110405462356
                     0.09090887133436507
                     -0.0769187620504483
                     0.06661073137387531
                     -0.058335701337905735
                     0.049768779946159324
                     -0.036531572744216916
                     0.016285820115365782 |]

              let horner (k: float[]) (z: float) =
                  let mutable p = k[k.Length - 1]

                  for i in k.Length - 2 .. -1 .. 0 do
                      p <- p * z + k[i]

                  p

              let halves (k: float[]) (z: float) =
                  let w = z * z
                  let even = k[0] + w * (k[2] + w * (k[4] + w * (k[6] + w * k[8])))
                  let odd = z * (k[1] + w * (k[3] + w * (k[5] + w * k[7])))
                  even + odd

              // `sin y` within π/4, where nothing is reduced and the tail is zero.
              let sinPath (poly: float[] -> float -> float) (y: float) =
                  let z = y * y
                  y + y * z * poly s z

              // `a · b` and its rounding error (Dekker's product over Veltkamp's split), as the package
              // forms the square of the cosine's argument.
              let twoProd (a: float) (b: float) =
                  let p = a * b
                  let ca = 134217729.0 * a
                  let ah = ca - (ca - a)
                  let al = a - ah
                  let cb = 134217729.0 * b
                  let bh = cb - (cb - b)
                  let bl = b - bh
                  struct (p, ((ah * bh - p) + ah * bl + al * bh) + al * bl)

              // `cos y` within π/4: the polynomial added to one, then the exact rounding error of that
              // addition, less the correction for the rounding error of `y²`, added back.
              let cosPath (poly: float[] -> float -> float) (y: float) =
                  let struct (z, e) = twoProd y y
                  let correction = e * (0.5 - 0.08333333333333333 * z)
                  let w = z * poly c z
                  let sum = 1.0 + w
                  sum + (((1.0 - sum) + w) - correction)

              // `atan x` below 7/16, where the argument stands and the tail is zero.
              let atanPath (poly: float -> float) (x: float) = x - x * poly (x * x)

              let atanHalves (z: float) =
                  let w = z * z

                  let s1 =
                      z * (at[0] + w * (at[2] + w * (at[4] + w * (at[6] + w * (at[8] + w * at[10])))))

                  let s2 = w * (at[1] + w * (at[3] + w * (at[5] + w * (at[7] + w * at[9]))))
                  s1 + s2

              let atanHorner (z: float) = z * horner at z

              let argumentOf (id: string) =
                  ElementaryVectors.cases
                  |> List.find (fun case -> case.Id = id)
                  |> fun case -> snd case.Input.Head

              let goesRed (id: string) (pinned: float) (transcribed: float) (reordered: float) =
                  Expect.equal
                      (bits transcribed)
                      (bits pinned)
                      (id + ": the path written out under the pinned order is the package, bit for bit")

                  Expect.notEqual (bits reordered) (bits pinned) (id + ": the other order answers another bit")

                  Expect.isLessThanOrEqual
                      (ElementaryVectors.ulpsApart reordered pinned)
                      1L
                      (id + ": and it is one unit away, a rounding and not a mistake")

              for id, pinned, transcribed, reordered in
                  [ "sin-reordered-polynomial-witness", Elementary.sin, sinPath horner, sinPath halves
                    "cos-reordered-polynomial-witness", Elementary.cos, cosPath horner, cosPath halves
                    "atan-reordered-polynomial-witness", Elementary.atan, atanPath atanHalves, atanPath atanHorner ] do
                  let x = argumentOf id

                  Expect.stringContains
                      (lineOf id)
                      (sprintf "\"value\": \"%s\"" (ElementaryVectors.hex (pinned x)))
                      (id + " records the pinned order's bits")

                  goesRed id (pinned x) (transcribed x) (reordered x)

              // The pair is asked at the cosine's witness, where BOTH its halves move: a host whose
              // `sinCos` sums either polynomial in the other order fails this one vector.
              let pair = "sincos-reordered-polynomial-witness"
              let x = argumentOf pair

              Expect.equal
                  (bits x)
                  (bits (argumentOf "cos-reordered-polynomial-witness"))
                  "the pair is asked at the cosine's witness"

              let struct (sine, cosine) = Elementary.sinCos x

              Expect.stringContains
                  (lineOf pair)
                  (sprintf
                      "\"sin\": \"%s\", \"cos\": \"%s\""
                      (ElementaryVectors.hex sine)
                      (ElementaryVectors.hex cosine))
                  (pair + " records the pinned order's bits")

              goesRed (pair + ", the sine") sine (sinPath horner x) (sinPath halves x)
              goesRed (pair + ", the cosine") cosine (cosPath horner x) (cosPath halves x)

          testCase "Elementary's and Rng's sources reach no host mathematics call"
          <| fun () ->
              // Phase 621: the generator's file is held to the same rule as the functions'.
              let source =
                  [ "Elementary.fs"; "Rng.fs" ]
                  |> List.map (fun file ->
                      File.ReadAllText(Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.Elementary", file))))
                  |> String.concat
                      "
"

              let hostCall =
                  Text.RegularExpressions.Regex(
                      @"Math\.(Exp|Log|Log10|Log2|Pow|Floor|Ceiling|Round|Truncate|FusedMultiplyAdd|ScaleB|ILogB|Sqrt|Cbrt|Sin|Cos|Tan|Asin|Acos|Atan|Atan2|SinCos)\b|\*\*"
                  )

              let hits =
                  hostCall.Matches source
                  |> Seq.map (fun m -> m.Value)
                  |> Seq.distinct
                  |> List.ofSeq

              Expect.isEmpty
                  hits
                  "no host mathematics call in Elementary.fs or Rng.fs (the square root is the F# operator over the premise's correctly rounded root)"

          testCase "ElementaryOperations.exactness classifies exactly the published operations, every one Pinned"
          <| fun () ->
              let declared = ElementaryOperations.exactness |> List.map fst
              Expect.equal (List.distinct declared) declared "no operation is classified twice"
              let published = publishedOperations ()
              Expect.isNonEmpty (Set.toList published) "the census found the operation module"
              Expect.equal (Set.ofList declared) published "the table is the published operation set"

              Expect.isTrue
                  (ElementaryOperations.exactness
                   |> List.forall (fun (_, e) -> e = ElementaryOperations.Exactness.Pinned))
                  "every operation here is Pinned (DECISIONS.md D52)"

              Expect.equal
                  (ElementaryOperations.exactnessOf "Elementary.exp")
                  (Some ElementaryOperations.Exactness.Pinned)
                  "a lookup"

              Expect.equal (ElementaryOperations.exactnessOf "Elementary.tan") None "no such operation" ]
