/// Phase 730 — the measurement behind DECISIONS.md D89: what the general product of
/// `Fuaran.Compute.GeometricAlgebra` costs on the operand shapes the typed functions give it, and what
/// the typed functions that call it cost, on this host.
///
/// The general product folds all 1,024 signed terms of D85 item 3's table whatever its operands hold.
/// The shapes are the ones the library's typed functions form: a point with a sphere (the geometric
/// product, the join's outer product and the power's scalar product against the sphere's undual), a
/// rotor with a rotor and a motor with a motor (`compose`), a translator with a rotor
/// (`Motor.ofRotorTranslator`), and each product of the sandwich `(V X) Ṽ` of a rotor, a translator
/// and a motor with a point (`applyToPoint`). Every operand is a typed value widened by its own
/// `toMultivector`, so its uncarried coefficients are `+0.0`, as a typed function's are. Beside each
/// geometric product of a pair D89 specialises, the specialised product on the same operands: its
/// checksum is the general product's, so the two answered the same bits over the sample.
///
/// Each case is timed by Phase 593's procedure (`ElementaryBench.perValue`): at least half a second of
/// warm-up, then passes doubled until one sample spans fifty milliseconds, then `runs` samples, with
/// every sample kept so that the spread is reported beside the median. Nanoseconds per call, over a
/// pinned seeded sample of operands, the same on every host. `node Program.js <runs> geometric-algebra`,
/// or the same program on .NET.
module Fuaran.Core.Compute.Benchmarks.GeometricAlgebraBench

open System
open Fuaran.Compute.GeometricAlgebra
open Fuaran.Core.Compute.Benchmarks.ElementaryBench

let private nowMs () : float = float DateTime.UtcNow.Ticks / 10_000.0

/// The operands of one pinned draw: `n` of each type, finite and of ordinary magnitude.
type private Operands =
    { Points: Point[]
      Spheres: Sphere[]
      Rotors: Rotor[]
      Translators: Translator[]
      Motors: Motor[] }

let private orFail (what: string) (r: Result<'T, GeometryRefusal>) : 'T =
    match r with
    | Ok x -> x
    | Error e -> failwithf "benchmark sample: %s was refused (%A)" what e

let private draw (n: int) : Operands =
    let d = Draw 730
    let coordinate () = d.Range -10.0 10.0

    let triple () =
        coordinate (), coordinate (), coordinate ()

    let axis () =
        d.Range -1.0 1.0, d.Range -1.0 1.0, d.Range 0.25 1.0

    let rotors =
        Array.init n (fun _ -> Rotor.ofAxisAngle (axis ()) (d.Range -3.0 3.0) |> orFail "a rotor")

    let translators =
        Array.init n (fun _ -> Translator.ofVector (triple ()) |> orFail "a translator")

    { Points = Array.init n (fun _ -> Point.up (triple ()) |> orFail "a point")
      Spheres = Array.init n (fun _ -> Sphere.ofCentreRadius (triple ()) (d.Range 0.5 5.0) |> orFail "a sphere")
      Rotors = rotors
      Translators = translators
      Motors = Array.init n (fun k -> Motor.ofRotorTranslator rotors[(k + 1) % n] translators[k]) }

/// Phase 593's timer with its samples kept: the median, the least and the greatest sample, in
/// nanoseconds per call, and the checksum that keeps the work alive.
let private timed (runs: int) (n: int) (f: unit -> float) : float * float * float * float =
    let mutable check = 0.0
    let start = nowMs ()

    while nowMs () - start < 500.0 do
        check <- f ()

    let sample (passes: int) =
        let start = nowMs ()

        for _ in 1..passes do
            check <- f ()

        nowMs () - start

    let rec calibrate passes =
        if passes >= 1_000_000 || sample passes >= 50.0 then
            passes
        else
            calibrate (passes * 2)

    let passes = calibrate 1

    let samples =
        Array.init runs (fun _ -> sample passes / float passes * 1e6 / float n)

    Array.sortInPlace samples
    samples[runs / 2], samples[0], samples[runs - 1], check

/// One pass over the sample: `f` at every index, summed into a float checksum.
let private pass (n: int) (f: int -> float) () : float =
    let mutable c = 0.0

    for k in 0 .. n - 1 do
        c <- c + f k

    c

let private c (index: int) (m: Multivector) : float = Multivector.coefficient index m

/// The cases: a name and the pass that times it. Operands are widened before the clock starts, so a
/// product case times the product alone.
let private cases (o: Operands) (n: int) : (string * (unit -> float)) list =
    let points = o.Points |> Array.map Point.toMultivector
    let spheres = o.Spheres |> Array.map Sphere.toMultivector
    let sigmas = spheres |> Array.map Multivector.undual
    let rotors = o.Rotors |> Array.map Rotor.toMultivector
    let translators = o.Translators |> Array.map Translator.toMultivector
    let motors = o.Motors |> Array.map Motor.toMultivector
    let rotorsReversed = rotors |> Array.map Multivector.reverse
    let translatorsReversed = translators |> Array.map Multivector.reverse
    let motorsReversed = motors |> Array.map Multivector.reverse
    let next (k: int) = (k + 1) % n

    let g = Multivector.geometricProduct
    // Phase 730: the geometric product as the typed functions form it, specialised where D89 keeps a
    // pair of operand types. Its answers are the general product's bits, and so are its checksums.
    let s = Specialised.geometric

    // The sandwich's first products: typed, as the specialised product answers them, so that the
    // second product meets the type it has inside a typed function. Their bits are the general ones.
    let rotorPoint = Array.init n (fun k -> s rotors[k] points[k])
    let translatorPoint = Array.init n (fun k -> s translators[k] points[k])
    let motorPoint = Array.init n (fun k -> s motors[k] points[k])

    [ "product: point * sphere (geometric)", pass n (fun k -> c 31 (g points[k] spheres[k]))
      "product: point ^ sphere (outer)", pass n (fun k -> c 31 (Multivector.outerProduct points[k] spheres[k]))
      "product: point * undual sphere (scalar)", pass n (fun k -> Multivector.scalarProduct points[k] sigmas[k])
      "product: rotor * rotor (geometric)", pass n (fun k -> c 0 (g rotors[k] rotors[next k]))
      "specialised: rotor * rotor", pass n (fun k -> c 0 (s rotors[k] rotors[next k]))
      "product: motor * motor (geometric)", pass n (fun k -> c 0 (g motors[k] motors[next k]))
      "specialised: motor * motor", pass n (fun k -> c 0 (s motors[k] motors[next k]))
      "product: translator * rotor (geometric)", pass n (fun k -> c 0 (g translators[k] rotors[k]))
      "specialised: translator * rotor", pass n (fun k -> c 0 (s translators[k] rotors[k]))
      "product: rotor * point (sandwich, first)", pass n (fun k -> c 2 (g rotors[k] points[k]))
      "specialised: rotor * point (sandwich, first)", pass n (fun k -> c 2 (s rotors[k] points[k]))
      "product: (rotor point) * reverse rotor (sandwich, second)",
      pass n (fun k -> c 2 (g rotorPoint[k] rotorsReversed[k]))
      "specialised: (rotor point) * reverse rotor (sandwich, second)",
      pass n (fun k -> c 2 (s rotorPoint[k] rotorsReversed[k]))
      "product: translator * point (sandwich, first)", pass n (fun k -> c 2 (g translators[k] points[k]))
      "specialised: translator * point (sandwich, first)", pass n (fun k -> c 2 (s translators[k] points[k]))
      "product: (translator point) * reverse translator (sandwich, second)",
      pass n (fun k -> c 2 (g translatorPoint[k] translatorsReversed[k]))
      "specialised: (translator point) * reverse translator (sandwich, second)",
      pass n (fun k -> c 2 (s translatorPoint[k] translatorsReversed[k]))
      "product: motor * point (sandwich, first)", pass n (fun k -> c 2 (g motors[k] points[k]))
      "specialised: motor * point (sandwich, first)", pass n (fun k -> c 2 (s motors[k] points[k]))
      "product: (motor point) * reverse motor (sandwich, second)",
      pass n (fun k -> c 2 (g motorPoint[k] motorsReversed[k]))
      "specialised: (motor point) * reverse motor (sandwich, second)",
      pass n (fun k -> c 2 (s motorPoint[k] motorsReversed[k]))
      "function: Rotor.compose", pass n (fun k -> (Rotor.compose o.Rotors[k] o.Rotors[next k]).Scalar)
      "function: Motor.compose", pass n (fun k -> (Motor.compose o.Motors[k] o.Motors[next k]).Scalar)
      "function: Motor.ofRotorTranslator",
      pass n (fun k -> (Motor.ofRotorTranslator o.Rotors[k] o.Translators[k]).Scalar)
      "function: Rotor.applyToPoint", pass n (fun k -> (Rotor.applyToPoint o.Rotors[k] o.Points[k]).E1)
      "function: Translator.applyToPoint", pass n (fun k -> (Translator.applyToPoint o.Translators[k] o.Points[k]).E1)
      "function: Motor.applyToPoint", pass n (fun k -> (Motor.applyToPoint o.Motors[k] o.Points[k]).E1)
      "function: Motor.applyToSphere", pass n (fun k -> (Motor.applyToSphere o.Motors[k] o.Spheres[k]).N0E1E2E3)
      "function: Sphere.power",
      pass n (fun k ->
          match Sphere.power o.Points[k] o.Spheres[k] with
          | Ok x -> x
          | Error _ -> 0.0) ]

/// Times every case over a sample of 1,000 operands of each type and prints one table.
let run (runs: int) : unit =
    let n = 1000
    let o = draw n
    printfn "## Geometric algebra: the general product and the typed functions that call it"
    printfn ""
    printfn "%d operands of each type drawn at seed 730; the median, least and greatest of %d samples" n runs
    printfn ""
    printfn "| case | median ns per call | least | greatest | checksum |"
    printfn "|---|---:|---:|---:|---:|"

    for name, f in cases o n do
        let median, least, greatest, check = timed runs n f
        printfn "| %s | %.1f | %.1f | %.1f | %s |" name median least greatest (hex check)
