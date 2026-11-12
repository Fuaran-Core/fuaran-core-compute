/// Phase 636 — the measurement behind DECISIONS.md D81: the incomplete beta function where one
/// parameter is large and the other small, read within a few standard deviations of the mean, and
/// the two complements `Special` publishes, on this host.
///
/// The harness prints nanoseconds per value over a pinned seeded sample (the same draw on every
/// host: Park and Miller's generator and the pinned `exp` and `ln`), then the sample itself with the
/// package's answers as IEEE-754 bit patterns between `BEGIN beta-corner-samples` and `END` lines, one
/// `<op>:<range> <operands> <answer>` a line. Those lines are what
/// `tests/Fuaran.Core.Compute.Tests/SpecialReference/special_reference.py check` reads to state each
/// function's error against the fifty-digit reference by range, and two hosts' lines are compared
/// byte for byte. The ranges: `corner` is `(a, b, 1 − c / a)` with `a` the large parameter, `b` the
/// small one and `c` log-uniform over `[1e-3, 30)`; `mirror` is the same point read from the other
/// end, `(b, a, c / a)`; `both-large` has both parameters log-uniform over `[1e3, 1e6)` and `x` within
/// six standard deviations of the mean; `by-sum` draws the parameters' sum from one power of two to the
/// next (`sum<2^5` to `sum<2^11`), which is where the threshold between the two fractions was chosen;
/// `general` is Phase 631's own draw of triples. After the `@`, the large parameter's range (`a<2e4`,
/// `a<1e6`) and the small one's (`b<0.05`, drawn down to `1e-6`; `b<1`; `b<10`).
/// `node Program.js <runs> beta-corner`, or the same program on .NET.
module Fuaran.Core.Compute.Benchmarks.BetaCornerBench

open Fuaran.Compute.Elementary
open Fuaran.Core.Compute.Benchmarks.ElementaryBench
open Fuaran.Core.Compute.Benchmarks.SpecialBench

/// A probability: uniform six draws in ten, within twelve decades of zero or of one otherwise.
let private probability (d: Draw) : float =
    let r = d.Unit()

    let p =
        if r < 0.6 then d.Unit()
        elif r < 0.8 then Elementary.pow 10.0 (d.Range -12.0 0.0)
        else 1.0 - Elementary.pow 10.0 (d.Range -12.0 0.0)

    if p > 0.0 && p < 1.0 then p else 0.5

/// `(a, b, c)`: the large parameter, the small one, and how many `1 / a` the argument is from the end.
let private corner (d: Draw) (n: int) (aLo: float) (aHi: float) (bLo: float) (bHi: float) : float[][] =
    draws n (fun () -> [| logUniform d aLo aHi; logUniform d bLo bHi; logUniform d 1e-3 30.0 |])

/// A range of the sample: its label and its `(a, b, c)` draws.
type private Range = { Label: string; Points: float[][] }

/// One measured family of lines: its name (`<op>:<range>`), the operands and the function over them.
type private Family =
    { Name: string
      Inputs: float[][]
      Apply: float[] -> float }

let run (runs: int) : unit =
    let d = Draw 636

    let ranges =
        [ { Label = "a<2e4,b<1"
            Points = corner d 300 128.0 2e4 0.05 1.0 }
          { Label = "a<2e4,b<10"
            Points = corner d 300 128.0 2e4 1.0 10.0 }
          { Label = "a<1e6,b<0.05"
            Points = corner d 300 128.0 1e6 1e-6 0.05 }
          { Label = "a<1e6,b<1"
            Points = corner d 300 2e4 1e6 0.05 1.0 }
          { Label = "a<1e6,b<10"
            Points = corner d 300 2e4 1e6 1.0 10.0 } ]

    // The corner read from the large parameter's end, `x = 1 − c / a`, and from the other, `x = c / a`.
    let near (p: float[]) = [| p[0]; p[1]; 1.0 - p[2] / p[0] |]
    let far (p: float[]) = [| p[1]; p[0]; p[2] / p[0] |]

    let bothLarge =
        draws 120 (fun () ->
            let a = logUniform d 1e3 1e6
            let b = logUniform d 1e3 1e6
            let mean = a / (a + b)
            let sd = sqrt (a * b / ((a + b) * (a + b) * (a + b + 1.0)))
            let x = mean + d.Range -6.0 6.0 * sd
            [| a; b; (if x > 0.0 && x < 1.0 then x else mean) |])

    // By the sum of the parameters, a power of two a range, where the threshold between the two
    // fractions was chosen: half the draws with one parameter small, half with both of a size, the
    // argument within six standard deviations of the mean.
    let bySum =
        [ for k in 4..10 ->
              let lo = twoTo k

              let points =
                  draws 240 (fun () ->
                      let s = logUniform d lo (2.0 * lo)

                      let small =
                          if d.Unit() < 0.5 then
                              logUniform d 0.05 (if s / 4.0 < 10.0 then s / 4.0 else 10.0)
                          else
                              s * d.Range 0.1 0.5

                      let a, b =
                          if d.Unit() < 0.5 then
                              small, s - small
                          else
                              s - small, small

                      let mean = a / (a + b)
                      let sd = sqrt (a * b / ((a + b) * (a + b) * (a + b + 1.0)))
                      let x = mean + d.Range -6.0 6.0 * sd
                      [| a; b; (if x > 0.0 && x < 1.0 then x else mean) |])

              sprintf "sum<2^%d" (k + 1), points ]

    let general =
        Array.concat
            [ betaTriples d 400 1e-3 1.0
              betaTriples d 400 1e-2 10.0
              betaTriples d 300 10.0 1e3
              betaTriples d 60 1e3 2e4 ]

    // A parameter pair of the corner with a probability, for the two inverses.
    let cornerProbabilities (points: float[][]) =
        points |> Array.map (fun p -> [| p[0]; p[1]; probability d |])

    let generalInverse = inverseSample d

    let lower (a: float[]) = Special.regularizedBeta a[0] a[1] a[2]

    let upper (a: float[]) =
        Special.regularizedBetaComplement a[0] a[1] a[2]

    let inverse (a: float[]) =
        Special.inverseRegularizedBeta a[0] a[1] a[2]

    let inverseComplement (a: float[]) =
        Special.inverseRegularizedBetaComplement a[0] a[1] a[2]

    let families =
        [ for name, f in [ "regularizedBeta", lower; "regularizedBetaComplement", upper ] do
              for r in ranges do
                  yield
                      { Name = sprintf "%s:corner@%s" name r.Label
                        Inputs = r.Points |> Array.map near
                        Apply = f }

                  yield
                      { Name = sprintf "%s:mirror@%s" name r.Label
                        Inputs = r.Points |> Array.map far
                        Apply = f }

              yield
                  { Name = sprintf "%s:both-large" name
                    Inputs = bothLarge
                    Apply = f }

              for label, points in bySum do
                  yield
                      { Name = sprintf "%s:by-sum@%s" name label
                        Inputs = points
                        Apply = f }
          yield
              { Name = "regularizedBetaComplement:general"
                Inputs = general
                Apply = upper }
          for name, f in
              [ "inverseRegularizedBeta", inverse
                "inverseRegularizedBetaComplement", inverseComplement ] do
              for r in ranges do
                  let points = cornerProbabilities r.Points

                  yield
                      { Name = sprintf "%s:corner@%s" name r.Label
                        Inputs = points
                        Apply = f }

                  yield
                      { Name = sprintf "%s:mirror@%s" name r.Label
                        Inputs = points |> Array.map (fun p -> [| p[1]; p[0]; p[2] |])
                        Apply = f }
          yield
              { Name = "inverseRegularizedBetaComplement:general"
                Inputs = generalInverse
                Apply = inverseComplement } ]

    printfn ""
    printfn "### The incomplete beta function at one large parameter, and the complements (Phase 636): ns per value"
    printfn ""
    printfn "| function and range | sample | ns per value |"
    printfn "|---|---:|---:|"

    for f in families do
        let ns, _ = perValue runs f.Inputs.Length (pass f.Inputs f.Apply)
        printfn "| %s | %d | %.1f |" f.Name f.Inputs.Length ns

    printfn ""
    printfn "BEGIN beta-corner-samples"

    for f in families do
        for a in f.Inputs do
            printfn "%s %s %s" f.Name (a |> Array.map hex |> String.concat " ") (hex (f.Apply a))

    printfn "END beta-corner-samples"
