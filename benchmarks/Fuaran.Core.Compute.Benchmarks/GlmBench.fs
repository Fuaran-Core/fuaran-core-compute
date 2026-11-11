/// Phase 634 — the measurement behind DECISIONS.md D80: `Glm.fit` over the solvers, on this host.
///
/// Two things are measured. First, the cost of a fit: microseconds per `Glm.fit` over a fixed set of
/// designs (Poisson, binomial and Gamma by IRLS, and the negative binomial with θ estimated), so a
/// build before the swap and a build after it are compared on one table. Second, θ's iteration: the two
/// methods the decision was made between, each solving θ's score at the same fixed means (the Poisson
/// fit's, where the first alternation starts, and the negative binomial fit's own, where its last ends),
/// over the reference corpus's counts and over seeded negative binomial and Poisson data whose θ is
/// small, large, near the Poisson limit or beyond it:
///
///   - `reflected`: the loop `Glm` carried until Phase 634 (MASS's `theta.ml`), an unbracketed Newton
///     that reflects a negative θ, `θ ← |θ| + score/info`, refusing a θ above `2⁵⁰`;
///   - `bracketed`: `Root.bracketedNewton` over `[2⁻²⁰, 2²⁰]` with every narrowing admitted, an answer
///     on an end of the bracket refused, the iteration `Glm` calls since Phase 634; and the same over
///     `[2⁻²⁰, 2⁵⁰]`, `Glm`'s old bound, as `bracketed-2^50`.
///
/// Each solve prints its θ as an IEEE-754 bit pattern (or its refusal), its Newton steps and its score
/// evaluations, and each fixed set of means is printed between `BEGIN glm-theta` and `END` lines, so
/// `tests/Fuaran.Core.Compute.Tests/fixtures/glm-reference/theta_reference.py` can state each answer's
/// distance from θ's maximum-likelihood root at those means, computed to fifty digits. Two hosts' lines
/// are compared byte for byte. `node Program.js <runs> glm`, or the same program on .NET.
module Fuaran.Core.Compute.Benchmarks.GlmBench

open Fuaran.Core
open Fuaran.Compute.Solvers
open Fuaran.Compute.Elementary
open Fuaran.Compute.LinearAlgebra
open Fuaran.Compute.Regression
open Fuaran.Core.Compute.Benchmarks.ElementaryBench

// ---- the data -----------------------------------------------------------------------------------

/// One design: an intercept and `x_i = i / 10`, its counts and unit weights.
type private Case = { Name: string; X: Matrix; Y: float[] }

let private design (n: int) : Matrix =
    Matrix.init n 2 (fun i j -> if j = 0 then 1.0 else float i / 10.0)

/// A count drawn from the negative binomial of mean `mu` and shape `theta` by inversion of its
/// distribution function, the probabilities by the recursion `p₀ = (θ/(θ+μ))^θ`,
/// `p_{k+1} = p_k · (θ+k)/(k+1) · μ/(θ+μ)` — the pinned `pow` and `+ − × ÷` only, so every host draws
/// the same counts. `theta = 0` stands for Poisson's limit, `p₀ = e^{−μ}`, `p_{k+1} = p_k · μ/(k+1)`.
let private draw (d: Draw) (mu: float) (theta: float) : float =
    let u = d.Unit()

    let mutable p =
        if theta = 0.0 then
            Elementary.exp (-mu)
        else
            Elementary.pow (theta / (theta + mu)) theta

    let mutable cdf = p
    let mutable k = 0.0

    while cdf < u && k < 100000.0 do
        p <-
            if theta = 0.0 then
                p * mu / (k + 1.0)
            else
                p * (theta + k) / (k + 1.0) * (mu / (theta + mu))

        cdf <- cdf + p
        k <- k + 1.0

    k

/// `n` counts with `log μ_i = a + b·x_i`, drawn at shape `theta` (0 for Poisson's).
let private seeded (name: string) (seed: int) (n: int) (a: float) (b: float) (theta: float) : Case =
    let d = Draw seed

    let y =
        Array.init n (fun i -> draw d (Elementary.exp (a + b * (float i / 10.0))) theta)

    { Name = name; X = design n; Y = y }

/// The reference corpus's counts (`fixtures/glm-reference/general.json`, `counts-negbin-*`), and seeded
/// data at five shapes and at Poisson's limit, five seeds each.
let private cases () : Case list =
    let counts =
        { Name = "corpus-counts"
          X = design 30
          Y =
            [| 0.0
               1.0
               0.0
               2.0
               1.0
               0.0
               3.0
               1.0
               2.0
               5.0
               0.0
               2.0
               4.0
               1.0
               6.0
               3.0
               2.0
               8.0
               4.0
               1.0
               7.0
               5.0
               3.0
               10.0
               6.0
               2.0
               12.0
               4.0
               9.0
               15.0 |] }

    let shapes =
        [ "theta-0.05", 200, 1.0, 0.05, 0.05
          "theta-0.5", 100, 1.0, 0.1, 0.5
          "theta-8", 60, 0.5, 0.1, 8.0
          "theta-200", 200, 1.5, 0.05, 200.0
          "theta-5000", 400, 2.0, 0.02, 5000.0
          "poisson", 200, 1.5, 0.05, 0.0 ]

    counts
    :: [ for name, n, a, b, theta in shapes do
             for s in 1..5 -> seeded (sprintf "%s-s%d" name s) (634 * 100 + s) n a b theta ]

// ---- θ's two iterations, at fixed means ---------------------------------------------------------

let private countSum (f: float -> float) (theta: float) (y: float) : float =
    let mutable acc = 0.0
    let mutable k = 0.0

    while k < y do
        acc <- acc + f (theta + k)
        k <- k + 1.0

    acc

/// θ's score and observed information at unit weights, in `Glm`'s order.
let private scoreInfo (ya: float[]) (mu: float[]) (theta: float) : float * float =
    let mutable score = 0.0
    let mutable info = 0.0
    let lnTheta = Elementary.ln theta

    for i in 0 .. ya.Length - 1 do
        let y = ya[i]
        let mt = mu[i] + theta
        let s1 = countSum (fun v -> 1.0 / v) theta y
        let s2 = countSum (fun v -> 1.0 / (v * v)) theta y
        score <- score + (s1 + lnTheta + 1.0 - Elementary.ln mt - (y + theta) / mt)
        info <- info + (s2 - 1.0 / theta + 2.0 / mt - (y + theta) / (mt * mt))

    score, info

let private start (ya: float[]) (mu: float[]) : float =
    let mutable sr = 0.0

    for i in 0 .. ya.Length - 1 do
        let r = ya[i] / mu[i] - 1.0
        sr <- sr + r * r

    float ya.Length / sr

let private finite (v: float) = v - v = 0.0

/// A solve's answer: θ or the reason it refused, the Newton steps taken, the score evaluations (the
/// information at the answer included, which `Glm` reads for θ's standard error) and the reflections of
/// a negative θ.
type private Solve =
    { Theta: Result<float, string>
      Steps: int
      Evaluations: int
      Reflections: int }

/// The loop `Glm` carried until Phase 634, at `MaxIter = 25`.
let private reflected (ya: float[]) (mu: float[]) : Solve =
    let mutable evaluations = 0
    let mutable reflections = 0
    let mutable t = start ya mu
    let mutable step = 1.0
    let mutable it = 1

    while finite t && it < 25 && abs step > 1.220703125e-4 do
        if t < 0.0 then
            reflections <- reflections + 1

        t <- abs t
        let score, info = scoreInfo ya mu t
        evaluations <- evaluations + 1
        step <- score / info
        t <- t + step
        it <- it + 1

    let theta =
        if not (finite t && finite step) then Error "not-finite"
        elif t <= 0.0 then Error "not-positive"
        elif t > 1125899906842624.0 then Error "above-2^50"
        elif abs step > 1.220703125e-4 then Error "cap"
        else Ok t

    { Theta = theta
      Steps = it - 1
      Evaluations = evaluations + (if Result.isOk theta then 1 else 0)
      Reflections = reflections }

/// `Root.bracketedNewton` over `[2⁻²⁰, hi]`, every narrowing admitted, the rule the absolute step
/// against `2⁻¹³` under a cap of 24 steps. The answer is the point last evaluated, so its information
/// is read from that evaluation rather than computed again.
let private bracketed (hi: float) (ya: float[]) (mu: float[]) : Solve =
    let mutable evaluations = 0

    let fdf (t: float) =
        evaluations <- evaluations + 1
        let score, info = scoreInfo ya mu t
        score, -info

    let rule = StoppingRule.create 24 (StoppingTest.AbsoluteStep 1.220703125e-4)

    match Root.bracketedNewton rule 1.0 fdf 9.5367431640625e-07 hi (start ya mu) with
    | Ok s when s.State <= 9.5367431640625e-07 || s.State >= hi ->
        { Theta = Error "end"
          Steps = s.Iterations
          Evaluations = evaluations
          Reflections = 0 }
    | Ok s ->
        { Theta = Ok s.State
          Steps = s.Iterations
          Evaluations = evaluations
          Reflections = 0 }
    | Error(e: SolverError<float, unit>) ->
        let reason =
            match e with
            | SolverError.BracketInvalid -> "bracket"
            | SolverError.NotConverged _ -> "cap"
            | SolverError.NotFinite _ -> "not-finite"
            | other -> sprintf "%A" other

        { Theta = Error reason
          Steps = 0
          Evaluations = evaluations
          Reflections = 0 }

let private methods: (string * (float[] -> float[] -> Solve)) list =
    [ "reflected", reflected
      "bracketed", bracketed 1048576.0
      "bracketed-2^50", bracketed 1125899906842624.0 ]

// ---- the run ------------------------------------------------------------------------------------

let private zeros (n: int) = Vector.adopt (Array.zeroCreate n)
let private ones (n: int) = Vector.adopt (Array.create n 1.0)

let private fit (family: GlmFamily) (link: GlmLink) (c: Case) =
    Glm.fit family link GlmControl.defaults c.X (Vector.adopt c.Y) (ones c.Y.Length) (zeros c.Y.Length)

/// Microseconds per fit, the median of `runs` passes.
let private perFit (runs: int) (f: unit -> unit) : float =
    let ns, _ =
        perValue runs 1 (fun () ->
            f ()
            0.0)

    ns / 1000.0

/// θ's two iterations at fixed means over the cases and the sweep, and the lines for the reference.
let private thetaTables (runs: int) (all: Case list) : unit =
    printfn ""
    printfn "### theta at fixed means: Newton steps, score evaluations and ns per solve"
    printfn ""
    printfn "| case | means | method | theta | steps | evaluations | ns per solve |"
    printfn "|---|---|---|---|---:|---:|---:|"

    let lines = ResizeArray<string>()

    for c in all do
        let poisson =
            fit GlmFamily.Poisson GlmLink.Log c
            |> Result.map (fun g -> Vector.toArray g.Fitted)

        let negbin =
            fit (GlmFamily.NegativeBinomial None) GlmLink.Log c
            |> Result.map (fun g -> Vector.toArray g.Fitted)

        for where, means in [ "poisson", poisson; "negbin", negbin ] do
            match means with
            | Error _ -> ()
            | Ok mu ->
                lines.Add(
                    sprintf
                        "means %s %s %s %s"
                        c.Name
                        where
                        (c.Y |> Array.map hex |> String.concat ",")
                        (mu |> Array.map hex |> String.concat ",")
                )

                for name, solve in methods do
                    let s = solve c.Y mu

                    let ns, _ =
                        perValue runs 1 (fun () ->
                            match (solve c.Y mu).Theta with
                            | Ok t -> t
                            | Error _ -> 0.0)

                    let theta =
                        match s.Theta with
                        | Ok t -> hex t
                        | Error r -> "refused:" + r

                    printfn
                        "| %s | %s | %s | %s | %d | %d | %.0f |"
                        c.Name
                        where
                        name
                        (match s.Theta with
                         | Ok t -> sprintf "%.17g" t
                         | Error r -> "refused: " + r)
                        s.Steps
                        s.Evaluations
                        ns

                    lines.Add(sprintf "theta %s %s %s %s %d %d" c.Name where name theta s.Steps s.Evaluations)

    // The sweep: small seeded designs at shapes log-uniform over [1e-2, 1e6] and one in eight at Poisson's
    // limit, θ solved at the Poisson fit's means by both iterations. Every set of means where the two
    // differ in a bit or a refusal, or where the reflected loop reflected, joins the reference lines.
    let sweep = 4000
    let d = Draw 634
    let mutable same = 0
    let mutable bothRefused = 0
    let mutable differ = 0
    let mutable onlyReflected = 0
    let mutable onlyBracketed = 0
    let mutable reflectedRuns = 0
    let mutable stepsR = 0
    let mutable stepsB = 0
    let mutable evalsR = 0
    let mutable evalsB = 0
    let mutable solved = 0

    for k in 1..sweep do
        let n = 10 + k % 31
        let a = d.Range 0.0 2.5
        let b = d.Range -0.3 0.3

        let theta =
            if d.Unit() < 0.125 then
                0.0
            else
                Elementary.pow 10.0 (d.Range -2.0 6.0)

        let c = seeded (sprintf "sweep-%d" k) (634000 + k) n a b theta

        match fit GlmFamily.Poisson GlmLink.Log c with
        | Error _ -> ()
        | Ok g ->
            let mu = Vector.toArray g.Fitted
            let r = reflected c.Y mu
            let br = bracketed 1048576.0 c.Y mu
            solved <- solved + 1

            if r.Reflections > 0 then
                reflectedRuns <- reflectedRuns + 1

            let interesting =
                match r.Theta, br.Theta with
                | Ok x, Ok y when x = y ->
                    same <- same + 1
                    stepsR <- stepsR + r.Steps
                    stepsB <- stepsB + br.Steps
                    evalsR <- evalsR + r.Evaluations
                    evalsB <- evalsB + br.Evaluations
                    false
                | Ok _, Ok _ ->
                    differ <- differ + 1
                    true
                | Ok _, Error _ ->
                    onlyReflected <- onlyReflected + 1
                    true
                | Error _, Ok _ ->
                    onlyBracketed <- onlyBracketed + 1
                    true
                | Error _, Error _ ->
                    bothRefused <- bothRefused + 1
                    false

            if (interesting || r.Reflections > 0) && lines.Count < 4000 then
                lines.Add(
                    sprintf
                        "means %s poisson %s %s"
                        c.Name
                        (c.Y |> Array.map hex |> String.concat ",")
                        (mu |> Array.map hex |> String.concat ",")
                )

                for name, s in [ "reflected", r; "bracketed", br ] do
                    let t =
                        match s.Theta with
                        | Ok t -> hex t
                        | Error e -> "refused:" + e

                    lines.Add(sprintf "theta %s poisson %s %s %d %d" c.Name name t s.Steps s.Evaluations)

    printfn ""
    printfn "### theta over the sweep: %d seeded designs, solved at the Poisson fit's means" solved
    printfn ""

    printfn
        "| same bits | both refused | different bits | reflected answers, bracketed refuses | bracketed answers, reflected refuses | runs that reflected |"

    printfn "|---:|---:|---:|---:|---:|---:|"
    printfn "| %d | %d | %d | %d | %d | %d |" same bothRefused differ onlyReflected onlyBracketed reflectedRuns
    printfn ""

    printfn
        "Where both answer the same bits: %d Newton steps and %d evaluations reflected, %d and %d bracketed."
        stepsR
        evalsR
        stepsB
        evalsB

    printfn ""
    printfn "BEGIN glm-theta"

    for l in lines do
        printfn "%s" l

    printfn "END glm-theta"

/// The fit table, then (unless `fitsOnly`) θ's two iterations and the sweep.
let run (runs: int) (fitsOnly: bool) : unit =
    let all = cases ()

    printfn ""
    printfn "### Glm.fit (Phase 634): microseconds per fit"
    printfn ""
    printfn "| case | rows | family | outcome | iterations | alternations | us per fit |"
    printfn "|---|---:|---|---|---:|---:|---:|"

    let timed (c: Case) (label: string) (family: GlmFamily) (link: GlmLink) (y: float[]) =
        let c = { c with Y = y }

        let outcome, iterations, alternations =
            match fit family link c with
            | Ok g -> "ok", g.Iterations, g.Alternations
            | Error e -> sprintf "%A" e |> fun s -> s.Replace("\n", " "), 0, 0

        let us = perFit runs (fun () -> fit family link c |> ignore)
        printfn "| %s | %d | %s | %s | %d | %d | %.1f |" c.Name c.Y.Length label outcome iterations alternations us

    let counts = List.head all
    timed counts "poisson log" GlmFamily.Poisson GlmLink.Log counts.Y
    timed counts "poisson sqrt" GlmFamily.Poisson GlmLink.Sqrt counts.Y

    timed
        counts
        "binomial logit"
        GlmFamily.Binomial
        GlmLink.Logit
        (counts.Y |> Array.map (fun v -> if v > 2.0 then 1.0 else 0.0))

    timed counts "gamma inverse" GlmFamily.Gamma GlmLink.Inverse (counts.Y |> Array.map (fun v -> v + 0.5))

    for c in all do
        timed c "negbin log" (GlmFamily.NegativeBinomial None) GlmLink.Log c.Y

    if not fitsOnly then
        thetaTables runs all
