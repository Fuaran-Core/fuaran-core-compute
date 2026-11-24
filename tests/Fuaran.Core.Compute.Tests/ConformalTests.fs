module Fuaran.Compute.Tests.ConformalTests

// ---------------------------------------------------------------------------
// Phase 728 — the conformal objects of Fuaran.Compute.GeometricAlgebra: the
// point, the point pair, the line, the circle, the plane and the sphere, their
// meet, join, read-backs and incidence (DECISIONS.md D85 items 4 to 6, D87).
//
// The committed law vectors are held by the family beside this one
// ("Geometric algebra": the file is one document). What is held here:
//
//   * Every go-red witness is the SHIPPED code with one thing changed. Each
//     published function that computes is `Conformal.<function>` under
//     `Arithmetic.shipped`; a witness calls the same function under an
//     arithmetic that differs in one member, renders its answer as the shipped
//     one is rendered, and is shown red on a committed vector. It is never a
//     second implementation.
//   * Each wrong port a vector exists to catch, against every function it
//     could change: the table below is the functions each one turns red, and
//     it is held both ways.
//   * Each degenerate configuration answers its named case, and each named
//     case goes red where the rule that names it is changed.
//   * The norm is the linear-algebra package's, bit for bit; the sources name
//     no float literal in a comparison but the ones D85 item 6 lists, call no
//     host function but the root, and take no default threshold.
//
// No expected value here is read from the host's mathematics library, the
// clock or the file system: a reference is exact arithmetic on small integers,
// or the committed law file through the corpus reader.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Numerics
open System.Reflection
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core
open Fuaran.Compute.GeometricAlgebra

module V = GeometricAlgebraVectors

/// One cell as the one corpus writer writes it: the text of an `expected` member.
let private cellText (cell: CorpusCell) : string =
    let text =
        CorpusFile.render
            { Head = []
              Items = "v"
              Rows = [ cell ] }

    (text.Split('\n') |> Array.find (fun line -> line.StartsWith "    ")).Trim()

/// The committed document's `expected` text for each vector, by id.
let private committed: Lazy<Map<string, string>> =
    lazy
        (File.ReadAllText(Snapshots.repoFile (Path.Combine("conformance", "laws", V.fileName))).Split('\n')
         |> Array.choose (fun line ->
             let id = Regex.Match(line, "\"id\": \"([^\"]+)\"")
             let at = line.IndexOf "\"expected\": "

             if id.Success && at >= 0 then
                 let tail = line.Substring(at + "\"expected\": ".Length).TrimEnd()

                 let tail =
                     if tail.EndsWith "," then
                         tail.Substring(0, tail.Length - 1)
                     else
                         tail

                 Some(id.Groups[1].Value, tail.Substring(0, tail.Length - 2))
             else
                 None)
         |> Map.ofArray)

/// The vectors of the object modules.
let private conformal: V.Case list =
    V.cases |> List.filter (fun c -> c.Op.Contains ".")

/// The published functions that perform no arithmetic: copies and exact reads. They have no witness
/// because there is no rule of theirs to change.
let private copies: Set<string> =
    set
        [ for m in [ "Point"; "PointPair"; "Line"; "Circle"; "Plane"; "Sphere" ] do
              yield m + ".toMultivector"
              yield m + ".ofMultivector"
          yield!
              [ "Point.weight"
                "Sphere.weight"
                "Line.direction"
                "Line.moment"
                "Plane.normal"
                "Plane.distance" ] ]

/// A conformal case's answer under an arithmetic: the shipped function, called as the published one
/// calls it, with `r` where that one passes `Arithmetic.shipped`. `None` for a copy.
let private answerUnder (r: Arithmetic) (c: V.Case) : CorpusCell option =
    let point (name: string) = V.pointOf (V.floats c name)
    let pair () = V.pairOf (V.floats c "x")
    let line (name: string) = V.lineOf (V.floats c name)
    let circle () = V.circleOf (V.floats c "x")
    let plane (name: string) = V.planeOf (V.floats c name)
    let sphere (name: string) = V.sphereOf (V.floats c name)
    let tau () = V.number c "tau"
    let mv = V.mv c

    match c.Op with
    | "Blade.join" -> Some(V.multivectorCell (Conformal.join r (mv "a") (mv "b")))
    | "Blade.meet" -> Some(V.multivectorCell (Conformal.meet r (mv "a") (mv "b")))
    | "Blade.inverse" -> Some(V.resultCell V.multivectorCell (Conformal.inverse r (mv "a")))
    | "Blade.meetWithin" -> Some(V.resultCell V.multivectorCell (Conformal.meetWithin r (mv "j") (mv "a") (mv "b")))
    | "Point.up" -> Some(V.resultCell V.pointCell (Conformal.up r (V.triple c "x")))
    | "Point.coordinates" -> Some(V.resultCell V.tripleCell (Conformal.coordinates r (point "x")))
    | "Point.normalise" -> Some(V.resultCell V.pointCell (Conformal.pointNormalise r (point "x")))
    | "PointPair.through" -> Some(V.pairCell (Conformal.pairThrough r (point "p") (point "q")))
    | "PointPair.weight" -> Some(V.floatCell (Conformal.pairWeight r (pair ())))
    | "PointPair.normalise" -> Some(V.resultCell V.pairCell (Conformal.pairNormalise r (pair ())))
    | "PointPair.centre" -> Some(V.resultCell V.tripleCell (Conformal.pairCentre r (pair ())))
    | "PointPair.squaredRadius" -> Some(V.resultCell V.floatCell (Conformal.pairSquaredRadius r (pair ())))
    | "PointPair.points" -> Some(V.resultCell V.pairPointsCell (Conformal.pairPoints r (tau ()) (pair ())))
    | "PointPair.kind" -> Some(V.resultCell V.pairKindCell (Conformal.pairKind r (tau ()) (pair ())))
    | "Line.through" -> Some(V.lineCell (Conformal.lineThrough r (point "p") (point "q")))
    | "Line.ofPointDirection" ->
        Some(V.resultCell V.lineCell (Conformal.lineOfPointDirection r (V.triple c "p") (V.triple c "d")))
    | "Line.weight" -> Some(V.floatCell (Conformal.lineWeight r (line "x")))
    | "Line.normalise" -> Some(V.resultCell V.lineCell (Conformal.lineNormalise r (line "x")))
    | "Line.nearestPoint" -> Some(V.resultCell V.tripleCell (Conformal.lineNearestPoint r (line "x")))
    | "Line.kind" -> Some(V.resultCell V.flatKindCell (Conformal.lineKind r (tau ()) (line "x")))
    | "Line.meet" -> Some(V.resultCell V.lineMeetCell (Conformal.lineMeet r (tau ()) (line "x") (line "y")))
    | "Line.meetPlane" -> Some(V.pairCell (Conformal.lineMeetPlane r (line "x") (plane "y")))
    | "Line.meetSphere" -> Some(V.pairCell (Conformal.lineMeetSphere r (line "x") (sphere "y")))
    | "Circle.through" -> Some(V.circleCell (Conformal.circleThrough r (point "p") (point "q") (point "r")))
    | "Circle.ofCentreRadiusNormal" ->
        Some(
            V.resultCell
                V.circleCell
                (Conformal.circleOfCentreRadiusNormal
                    r
                    (V.triple c "centre")
                    (V.number c "radius")
                    (V.triple c "normal"))
        )
    | "Circle.weight" -> Some(V.floatCell (Conformal.circleWeight r (circle ())))
    | "Circle.normalise" -> Some(V.resultCell V.circleCell (Conformal.circleNormalise r (circle ())))
    | "Circle.centre" -> Some(V.resultCell V.tripleCell (Conformal.circleCentre r (circle ())))
    | "Circle.squaredRadius" -> Some(V.resultCell V.floatCell (Conformal.circleSquaredRadius r (circle ())))
    | "Circle.kind" -> Some(V.resultCell V.circleKindCell (Conformal.circleKind r (tau ()) (circle ())))
    | "Circle.plane" -> Some(V.planeCell (Conformal.circlePlane r (circle ())))
    | "Circle.power" -> Some(V.resultCell V.floatCell (Conformal.circlePower r (point "p") (circle ())))
    | "Circle.side" -> Some(V.resultCell V.sideCell (Conformal.circleSide r (tau ()) (point "p") (circle ())))
    | "Plane.through" -> Some(V.planeCell (Conformal.planeThrough r (point "p") (point "q") (point "r")))
    | "Plane.ofNormalDistance" ->
        Some(V.resultCell V.planeCell (Conformal.planeOfNormalDistance r (V.triple c "normal") (V.number c "distance")))
    | "Plane.weight" -> Some(V.floatCell (Conformal.planeWeight r (plane "x")))
    | "Plane.normalise" -> Some(V.resultCell V.planeCell (Conformal.planeNormalise r (plane "x")))
    | "Plane.nearestPoint" -> Some(V.resultCell V.tripleCell (Conformal.planeNearestPoint r (plane "x")))
    | "Plane.kind" -> Some(V.resultCell V.flatKindCell (Conformal.planeKind r (tau ()) (plane "x")))
    | "Plane.meet" -> Some(V.lineCell (Conformal.planeMeet r (plane "x") (plane "y")))
    | "Plane.signedDistance" -> Some(V.resultCell V.floatCell (Conformal.planeSignedDistance r (point "p") (plane "x")))
    | "Plane.side" -> Some(V.resultCell V.planeSideCell (Conformal.planeSide r (tau ()) (point "p") (plane "x")))
    | "Sphere.through" -> Some(V.sphereCell (Conformal.sphereThrough r (point "p") (point "q") (point "r") (point "s")))
    | "Sphere.ofCentreRadius" ->
        Some(V.resultCell V.sphereCell (Conformal.sphereOfCentreRadius r (V.triple c "centre") (V.number c "radius")))
    | "Sphere.normalise" -> Some(V.resultCell V.sphereCell (Conformal.sphereNormalise r (sphere "x")))
    | "Sphere.centre" -> Some(V.resultCell V.tripleCell (Conformal.sphereCentre r (sphere "x")))
    | "Sphere.squaredRadius" -> Some(V.resultCell V.floatCell (Conformal.sphereSquaredRadius r (sphere "x")))
    | "Sphere.kind" -> Some(V.resultCell V.sphereKindCell (Conformal.sphereKind r (tau ()) (sphere "x")))
    | "Sphere.meet" -> Some(V.circleCell (Conformal.sphereMeet r (sphere "x") (sphere "y")))
    | "Sphere.meetPlane" -> Some(V.circleCell (Conformal.sphereMeetPlane r (sphere "x") (plane "y")))
    | "Sphere.power" -> Some(V.resultCell V.floatCell (Conformal.spherePower r (point "p") (sphere "x")))
    | "Sphere.side" -> Some(V.resultCell V.sideCell (Conformal.sphereSide r (tau ()) (point "p") (sphere "x")))
    | _ -> None

// ---- the wrong ports: `Arithmetic.shipped` with one member changed ----------------------------------

let private shipped = Arithmetic.shipped

/// The four products through the fold kernel's door, under a rule of entry, a start and an order of
/// the terms. With the recorded three it is the shipped arithmetic, which the first case holds.
let private products
    (enter: bool -> float -> float -> float -> float)
    (start: float)
    (order: Term[] -> Term[])
    : Arithmetic =
    { shipped with
        Geometric = Kernel.productUnder start enter (order Basis.geometric)
        Outer = Kernel.productUnder start enter (order Basis.outer)
        Contraction = Kernel.productUnder start enter (order Basis.contraction)
        Scalar =
            fun a b ->
                Kernel.scalarUnder start enter (order Basis.scalar) (Multivector.toArray a) (Multivector.toArray b) }

let private fused (negative: bool) (r: float) (x: float) (y: float) : float =
    if negative then
        Math.FusedMultiplyAdd(-x, y, r)
    else
        Math.FusedMultiplyAdd(x, y, r)

/// The norm's sum of squared quotients with each square fused into the sum.
let private fusedSquares (quotients: float[]) : float =
    quotients |> Array.fold (fun acc q -> Math.FusedMultiplyAdd(q, q, acc)) 0.0

/// The norm's sum of squared quotients, correctly rounded: the exact sum in integers, rounded once to
/// the nearest double, ties to even. Each quotient is a double of magnitude at most one.
let private exactSquares (quotients: float[]) : float =
    let scale = 2200

    let total =
        quotients
        |> Array.fold
            (fun (acc: BigInteger) q ->
                let bits = BitConverter.DoubleToInt64Bits q
                let exponent = int ((bits >>> 52) &&& 0x7FFL)
                let fraction = bits &&& 0xFFFFFFFFFFFFFL

                let mantissa, e =
                    if exponent = 0 then
                        BigInteger fraction, -1074
                    else
                        BigInteger(fraction ||| (1L <<< 52)), exponent - 1075

                acc + ((mantissa * mantissa) <<< (2 * e + scale)))
            BigInteger.Zero

    if total.IsZero then
        0.0
    else
        let shift = int (total.GetBitLength()) - 53

        let rounded =
            if shift <= 0 then
                total
            else
                let kept = total >>> shift
                let remainder = total - (kept <<< shift)
                let half = BigInteger.One <<< (shift - 1)

                if remainder > half || (remainder = half && not kept.IsEven) then
                    kept + BigInteger.One
                else
                    kept

        Math.ScaleB(float rounded, (max shift 0) - scale)

let private wrongPorts: (string * Arithmetic) list =
    [ "another order of summation", products Kernel.twoRoundings 0.0 Array.rev
      "a fused multiply-add", products fused 0.0 id
      "a fold seeded from its first term", products Kernel.twoRoundings -0.0 id
      "a product with a reciprocal",
      { shipped with
          Divide = fun x y -> x * (1.0 / y) }
      "a statistic divided twice",
      { shipped with
          OverBoth = fun divide numerator first second -> divide (divide numerator first) second }
      "an unscaled norm",
      { shipped with
          Scaled = fun _ xs -> sqrt (xs |> Array.fold (fun acc v -> acc + v * v) 0.0) }
      "a fused multiply-add in the norm's sum",
      { shipped with
          SumOfSquares = fusedSquares }
      "a correctly rounded sum in the norm",
      { shipped with
          SumOfSquares = exactSquares }
      "the norm's squares summed in another order",
      { shipped with
          SumOfSquares = Array.rev >> Norm.foldedSquares }
      "a product of three factors formed from the right",
      { shipped with
          Chain = fun product factors -> List.reduceBack product factors }
      "half a squared length summed from the right",
      { shipped with
          HalfSquaredLength = fun x1 x2 x3 -> 0.5 * (x1 * x1 + (x2 * x2 + x3 * x3)) }
      "a radius halved before it is squared",
      { shipped with
          HalfSquare = fun r -> (0.5 * r) * r }
      "a power formed as zero less twice the product",
      { shipped with
          MinusTwice = fun s -> 0.0 - 2.0 * s }
      "no test of a zero divisor",
      { shipped with
          IsZeroDivisor = fun _ -> false }
      "no test of a zero direction",
      { shipped with
          IsZeroNorm = fun _ -> false }
      "a threshold met from above",
      { shipped with
          Above = fun s tau -> s >= tau }
      "a strict threshold",
      { shipped with
          Within = fun s tau -> s < tau && -s < tau }
      "a threshold never met",
      { shipped with
          Within = fun _ _ -> false }
      "a threshold always met",
      { shipped with
          Within = fun _ _ -> true }
      "a quotient formed at a threshold of zero",
      { shipped with
          SignsDecide = fun _ -> false } ]

/// The committed vectors an arithmetic answers otherwise, by function: where the list of a function is
/// not empty, the wrong port is shown red on a vector of that function.
let private redUnder (r: Arithmetic) : (string * string list) list =
    conformal
    |> List.choose (fun c ->
        match answerUnder r c with
        | Some cell when cellText cell <> committed.Value[c.Id] -> Some(c.Op, c.Id)
        | _ -> None)
    |> List.groupBy fst
    |> List.map (fun (op, reds) -> op, reds |> List.map snd)
    |> List.sortBy fst

// ---- the drawn search: what a wrong port CAN change -------------------------------------------------

/// The thresholds a function's own statistics name on one case: the norms of its blade's groups, the
/// magnitude of its squared radius or of its statistic, and for two lines the norms the two-line rule
/// tests. A wrong port that moves a statistic moves the answer at exactly such a threshold.
let private thresholdsOf (c: V.Case) : float list =
    let norms (indices: int[]) (m: Multivector) : float list =
        let o, n, b, i = Indices.groups indices
        [ for group in [ o; n; b; i ] -> Norm.scaled (Conformal.copy group m) ]

    let magnitude (r: Result<float, GeometryRefusal>) : float list =
        match r with
        | Ok x -> [ (if x < 0.0 then -x else x) ]
        | Error _ -> []

    let x () = V.floats c "x"
    let point () = V.pointOf (V.floats c "p")

    let named =
        match c.Op with
        | "PointPair.kind" ->
            let m = Conformal.pairMv (V.pairOf (x ()))
            norms Indices.pair m @ magnitude (Conformal.squaredRadius shipped m)
        | "Circle.kind" ->
            let m = Conformal.circleMv (V.circleOf (x ()))
            norms Indices.circle m @ magnitude (Conformal.squaredRadius shipped m)
        | "Sphere.kind" ->
            let m = Conformal.sphereMv (V.sphereOf (x ()))
            norms Indices.sphere m @ magnitude (Conformal.squaredRadius shipped m)
        | "Line.kind" -> norms Indices.line (Conformal.lineMv (V.lineOf (x ())))
        | "Plane.kind" -> norms Indices.plane (Conformal.planeMv (V.planeOf (x ())))
        | "PointPair.points" -> magnitude (Conformal.pairSquaredRadius shipped (V.pairOf (x ())))
        | "Sphere.side" -> magnitude (Conformal.spherePower shipped (point ()) (V.sphereOf (x ())))
        | "Circle.side" -> magnitude (Conformal.circlePower shipped (point ()) (V.circleOf (x ())))
        | "Plane.side" -> magnitude (Conformal.planeSignedDistance shipped (point ()) (V.planeOf (x ())))
        | "Line.meet" ->
            let a = Conformal.lineMv (V.lineOf (x ()))
            let b = Conformal.lineMv (V.lineOf (V.floats c "y"))

            let typed (indices: int[]) (m: Multivector) =
                Conformal.widen indices (Conformal.copy indices m)

            norms Indices.point (typed Indices.point (Conformal.meet shipped a b))
            @ (match Conformal.nearestPoint shipped b |> Result.bind (Conformal.up shipped) with
               | Ok q -> norms Indices.plane (typed Indices.plane (shipped.Outer a (Conformal.pointMv q)))
               | Error _ -> [])
        | _ -> []

    named
    |> List.filter (fun t -> t > 0.0 && not (Double.IsInfinity t))
    |> List.distinct

/// The drawn sample: `V.searched 0 .. 164`. That is fifty-five configurations drawn at each of three
/// scales of coordinate (the generator's seeds 9000 to 9164), every published function over each; the
/// same configurations with two coefficients in three of every operand replaced by a zero of a drawn
/// sign (seeds 7000 to 7164); and the blade operations over untyped multivectors that are zeros of both
/// signs, alone, around one coefficient and around a third of them (seeds 8000 to 8164). Each function
/// that takes a threshold is asked again at every threshold of `thresholdsOf`. Each element is the
/// number drawn at, the case as drawn, the threshold it is asked at when that is not the drawn one, and
/// the case asked.
let private drawnSample: Lazy<(int * V.Case * float option * V.Case)[]> =
    lazy
        ([| for index in 0..164 do
                for c in V.searched index do
                    if not (copies.Contains c.Op) then
                        yield index, c, None, c

                        for tau in thresholdsOf c do
                            yield
                                index,
                                c,
                                Some tau,
                                { c with
                                    Input =
                                        c.Input
                                        |> List.map (fun (name, o) ->
                                            if name = "tau" then name, V.Number tau else name, o) } |])

/// The shipped answer of each element of the sample.
let private drawnShipped: Lazy<string[]> =
    lazy
        (drawnSample.Value
         |> Array.Parallel.map (fun (_, _, _, c) ->
             match answerUnder shipped c with
             | Some cell -> cellText cell
             | None -> ""))

/// The functions an arithmetic answers otherwise somewhere in the drawn sample, each with the first
/// element where it does.
let private changedUnder (r: Arithmetic) : Map<string, int * string * float option> =
    let shippedAnswers = drawnShipped.Value

    drawnSample.Value
    |> Array.Parallel.mapi (fun at (index, drawnCase, tau, c) ->
        match answerUnder r c with
        | Some cell when cellText cell <> shippedAnswers[at] -> Some(c.Op, (index, drawnCase.Id, tau))
        | _ -> None)
    |> Array.choose id
    |> Array.fold
        (fun found (op, witness) ->
            if Map.containsKey op found then
                found
            else
                Map.add op witness found)
        Map.empty

/// Why a wrong port is UNABLE to change a function: the port, the reason, and the functions. Held both
/// ways by the case below. A function is listed here exactly when no committed vector of it fails the
/// port, and no function listed is answered otherwise by that port anywhere in the drawn sample.
let private unable: (string * string * string list) list =
    [ "another order of summation",
      "it calls no product",
      [ "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.weight"
        "PointPair.normalise"
        "Line.weight"
        "Line.normalise"
        "Line.kind"
        "Circle.weight"
        "Circle.normalise"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.kind"
        "Sphere.ofCentreRadius"
        "Sphere.normalise" ]
      "another order of summation",
      "on the blades its operands carry, each coefficient it answers or reads is a sum of at most two terms of its table, and two terms round alike in either order",
      [ "PointPair.through"
        "Line.through"
        "Line.ofPointDirection"
        "Circle.plane"
        "Plane.meet"
        "Sphere.centre"
        "Sphere.meet"
        "Sphere.meetPlane" ]
      "a fused multiply-add",
      "it calls no product",
      [ "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.weight"
        "PointPair.normalise"
        "Line.weight"
        "Line.normalise"
        "Line.kind"
        "Circle.weight"
        "Circle.normalise"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.kind"
        "Sphere.ofCentreRadius"
        "Sphere.normalise" ]
      "a fused multiply-add",
      "each coefficient it answers is one term of its table, and one product fused into a zero is that product rounded",
      [ "Circle.plane" ]
      "a fused multiply-add",
      "each coefficient it reads is a sum of two equal terms, and a product fused into its own rounded value rounds to twice that value, as the two roundings do",
      [ "Sphere.centre" ]
      "a fold seeded from its first term",
      "it calls no product",
      [ "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.weight"
        "PointPair.normalise"
        "Line.weight"
        "Line.normalise"
        "Line.kind"
        "Circle.weight"
        "Circle.normalise"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.kind"
        "Sphere.ofCentreRadius"
        "Sphere.normalise" ]
      "a fold seeded from its first term",
      "a fold from `-0.0` answers otherwise only where every term of a coefficient is a negative zero; the search draws typed operands heavy in zeros of both signs, and untyped ones that are nothing else, and finds no such coefficient in an answer of this function",
      [ "Blade.inverse"
        "PointPair.through"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "PointPair.points"
        "PointPair.kind"
        "Line.through"
        "Line.ofPointDirection"
        "Line.nearestPoint"
        "Line.meet"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.kind"
        "Circle.plane"
        "Circle.power"
        "Circle.side"
        "Plane.through"
        "Plane.nearestPoint"
        "Plane.meet"
        "Plane.signedDistance"
        "Plane.side"
        "Sphere.through"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.kind"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power"
        "Sphere.side" ]
      "a product with a reciprocal",
      "it forms no quotient",
      [ "Blade.join"
        "Blade.meet"
        "Point.up"
        "PointPair.through"
        "PointPair.weight"
        "Line.through"
        "Line.weight"
        "Line.kind"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.weight"
        "Circle.plane"
        "Plane.through"
        "Plane.weight"
        "Plane.kind"
        "Plane.meet"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.meet"
        "Sphere.meetPlane" ]
      "a statistic divided twice",
      "it is no incidence statistic",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "PointPair.points"
        "PointPair.kind"
        "Line.through"
        "Line.ofPointDirection"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.kind"
        "Line.meet"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.ofCentreRadiusNormal"
        "Circle.weight"
        "Circle.normalise"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.kind"
        "Circle.plane"
        "Plane.through"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.kind"
        "Plane.meet"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.kind"
        "Sphere.meet"
        "Sphere.meetPlane" ]
      "an unscaled norm",
      "it reads no norm",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "PointPair.points"
        "Line.through"
        "Line.nearestPoint"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.plane"
        "Circle.power"
        "Circle.side"
        "Plane.through"
        "Plane.nearestPoint"
        "Plane.meet"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power"
        "Sphere.side" ]
      "a fused multiply-add in the norm's sum",
      "it reads no norm",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "PointPair.points"
        "Line.through"
        "Line.nearestPoint"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.plane"
        "Circle.power"
        "Circle.side"
        "Plane.through"
        "Plane.nearestPoint"
        "Plane.meet"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power"
        "Sphere.side" ]
      "a correctly rounded sum in the norm",
      "it reads no norm",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "PointPair.points"
        "Line.through"
        "Line.nearestPoint"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.plane"
        "Circle.power"
        "Circle.side"
        "Plane.through"
        "Plane.nearestPoint"
        "Plane.meet"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power"
        "Sphere.side" ]
      "the norm's squares summed in another order",
      "it reads no norm",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "PointPair.points"
        "Line.through"
        "Line.nearestPoint"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.plane"
        "Circle.power"
        "Circle.side"
        "Plane.through"
        "Plane.nearestPoint"
        "Plane.meet"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power"
        "Sphere.side" ]
      "a product of three factors formed from the right",
      "it forms no product of more than two factors",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.squaredRadius"
        "PointPair.kind"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.kind"
        "Line.meet"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.ofCentreRadiusNormal"
        "Circle.weight"
        "Circle.normalise"
        "Circle.squaredRadius"
        "Circle.kind"
        "Circle.plane"
        "Circle.power"
        "Circle.side"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.kind"
        "Plane.meet"
        "Plane.signedDistance"
        "Plane.side"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.squaredRadius"
        "Sphere.kind"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power"
        "Sphere.side" ]
      "a product of three factors formed from the right",
      "its product has two factors, which have one grouping",
      [ "PointPair.through" ]
      "a product of three factors formed from the right",
      "its last factor is `n∞`, which either grouping meets with one term for each coefficient, so both form the same two products into each coefficient",
      [ "Line.through"; "Line.ofPointDirection" ]
      "a product of three factors formed from the right",
      "its middle factor is `n∞`, which only copies and negates a sphere's coefficients on either side, so both groupings form the same products into the coefficients the centre reads",
      [ "Sphere.centre" ]
      "half a squared length summed from the right",
      "it embeds no point",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "PointPair.points"
        "PointPair.kind"
        "Line.through"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.kind"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.weight"
        "Circle.normalise"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.kind"
        "Circle.plane"
        "Circle.power"
        "Circle.side"
        "Plane.through"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.kind"
        "Plane.meet"
        "Plane.signedDistance"
        "Plane.side"
        "Sphere.through"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.kind"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power"
        "Sphere.side" ]
      "half a squared length summed from the right",
      "the point's `n∞` coefficient, which is all this member forms, is annihilated by the outer product with `n∞`: it reaches no coefficient of a line or of a span through the point",
      [ "Line.ofPointDirection"; "Line.meet" ]
      "a radius halved before it is squared",
      "it takes no radius",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "PointPair.points"
        "PointPair.kind"
        "Line.through"
        "Line.ofPointDirection"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.kind"
        "Line.meet"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.weight"
        "Circle.normalise"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.kind"
        "Circle.plane"
        "Circle.power"
        "Circle.side"
        "Plane.through"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.kind"
        "Plane.meet"
        "Plane.signedDistance"
        "Plane.side"
        "Sphere.through"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.kind"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power"
        "Sphere.side" ]
      "a power formed as zero less twice the product",
      "it forms no power",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "PointPair.points"
        "PointPair.kind"
        "Line.through"
        "Line.ofPointDirection"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.kind"
        "Line.meet"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.ofCentreRadiusNormal"
        "Circle.weight"
        "Circle.normalise"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.kind"
        "Circle.plane"
        "Plane.through"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.kind"
        "Plane.meet"
        "Plane.signedDistance"
        "Plane.side"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.kind"
        "Sphere.meet"
        "Sphere.meetPlane" ]
      "a power formed as zero less twice the product",
      "a test compares a magnitude with its threshold and does not read the sign of a zero (D85 item 6)",
      [ "Circle.side"; "Sphere.side" ]
      "no test of a zero divisor",
      "it tests no divisor against zero (a direction's norm is another member's)",
      [ "Blade.join"
        "Blade.meet"
        "Point.up"
        "PointPair.through"
        "PointPair.weight"
        "Line.through"
        "Line.ofPointDirection"
        "Line.weight"
        "Line.kind"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.ofCentreRadiusNormal"
        "Circle.weight"
        "Circle.plane"
        "Plane.through"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.kind"
        "Plane.meet"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.meet"
        "Sphere.meetPlane" ]
      "no test of a zero direction",
      "it takes no direction",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "PointPair.points"
        "PointPair.kind"
        "Line.through"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.kind"
        "Line.meet"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.weight"
        "Circle.normalise"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.kind"
        "Circle.plane"
        "Circle.power"
        "Circle.side"
        "Plane.through"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.kind"
        "Plane.meet"
        "Plane.signedDistance"
        "Plane.side"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.kind"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power"
        "Sphere.side" ]
      "a threshold met from above",
      "it takes no threshold",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "Line.through"
        "Line.ofPointDirection"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.ofCentreRadiusNormal"
        "Circle.weight"
        "Circle.normalise"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.plane"
        "Circle.power"
        "Plane.through"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.meet"
        "Plane.signedDistance"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power" ]
      "a strict threshold",
      "it takes no threshold",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "Line.through"
        "Line.ofPointDirection"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.ofCentreRadiusNormal"
        "Circle.weight"
        "Circle.normalise"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.plane"
        "Circle.power"
        "Plane.through"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.meet"
        "Plane.signedDistance"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power" ]
      "a threshold never met",
      "it takes no threshold",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "Line.through"
        "Line.ofPointDirection"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.ofCentreRadiusNormal"
        "Circle.weight"
        "Circle.normalise"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.plane"
        "Circle.power"
        "Plane.through"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.meet"
        "Plane.signedDistance"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power" ]
      "a threshold always met",
      "it takes no threshold",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "Line.through"
        "Line.ofPointDirection"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.ofCentreRadiusNormal"
        "Circle.weight"
        "Circle.normalise"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.plane"
        "Circle.power"
        "Plane.through"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.meet"
        "Plane.signedDistance"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power" ]
      "a threshold always met",
      "a line's and a plane's blades all hold `n∞`, and the meet of two lines in the whole space is a multiple of `n∞` whatever their coefficients, so the only norms it asks `within` about that `above` has not settled are of groups that hold nothing",
      [ "Line.kind"; "Line.meet"; "Plane.kind" ]
      "a quotient formed at a threshold of zero",
      "it tests no quotient against a threshold",
      [ "Blade.join"
        "Blade.meet"
        "Blade.inverse"
        "Blade.meetWithin"
        "Point.up"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.through"
        "PointPair.weight"
        "PointPair.normalise"
        "PointPair.centre"
        "PointPair.squaredRadius"
        "Line.through"
        "Line.ofPointDirection"
        "Line.weight"
        "Line.normalise"
        "Line.nearestPoint"
        "Line.kind"
        "Line.meet"
        "Line.meetPlane"
        "Line.meetSphere"
        "Circle.through"
        "Circle.ofCentreRadiusNormal"
        "Circle.weight"
        "Circle.normalise"
        "Circle.centre"
        "Circle.squaredRadius"
        "Circle.plane"
        "Circle.power"
        "Plane.through"
        "Plane.ofNormalDistance"
        "Plane.weight"
        "Plane.normalise"
        "Plane.nearestPoint"
        "Plane.kind"
        "Plane.meet"
        "Plane.signedDistance"
        "Sphere.through"
        "Sphere.ofCentreRadius"
        "Sphere.normalise"
        "Sphere.centre"
        "Sphere.squaredRadius"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power" ] ]

// ---- the float literals of the operation modules (DECISIONS.md D85 item 6) ---------------------------

/// The operation modules: every source of the package but the law family, which compares bits against
/// float literals as a law must.
let private operationModules =
    [ "Basis.fs"
      "Multivector.fs"
      "Objects.fs"
      "Conformal.fs"
      "Geometry.fs"
      "GeometricAlgebraOperations.fs" ]

/// A source with its comments removed and each string literal emptied to `""`.
let private codeOf (source: string) : string =
    let q = "\""

    let pattern =
        @"\(\*.*?\*\)|//[^\n]*|"
        + (q + q + q + @"[\s\S]*?" + q + q + q)
        + ("|@" + q + "(?:[^" + q + "]|" + q + q + ")*" + q)
        + ("|" + q + @"(?:\\.|[^" + q + @"\\])*" + q)

    Regex(pattern, RegexOptions.Singleline)
        .Replace(
            source,
            MatchEvaluator(fun m ->
                if m.Value.StartsWith "//" || m.Value.StartsWith "(*" then
                    " "
                else
                    q + q)
        )

/// Every float literal token of a source, and every float it names. A literal is digits, underscores
/// allowed, with a decimal point (`0.5`, `1.`, `1_000.0`), an exponent (`1e-9`) or a suffix that makes
/// a number of another floating or decimal type (`1.0f`, `1e-9M`), or a hexadecimal double
/// (`0x3FF0000000000000LF`). A sign is not part of the token. A named float is `infinity`, `nan`,
/// `NaN`, `PositiveInfinity`, `NegativeInfinity`, `MaxValue` or `MinValue`, whatever it is bound to.
let private floatLiterals (source: string) : string list =
    let code = codeOf source

    [ for m in
          Regex.Matches(
              code,
              @"(?<![\w.])(?:0[xX][0-9a-fA-F_]+(?:LF|lf)|\d[\d_]*\.[\d_]*(?:[eE][-+]?\d+)?[fFmM]?|\d[\d_]*[eE][-+]?\d+[fFmM]?|\d[\d_]*[fFmM])(?![\w.])"
          ) -> m.Value
      for m in Regex.Matches(code, @"\b(?:infinity|nan|NaN|PositiveInfinity|NegativeInfinity|MaxValue|MinValue)\b") ->
          m.Value ]

/// The ways a tolerance could be had without a float literal the census counts: a default for an
/// argument, the least double by name, a float made from an integer or a string literal by `float`,
/// and the two tokens no operation module has a use for: `double`, the other name of that
/// conversion, and anything named for parsing (`Double.Parse`, `TryParse`), whatever it is given.
let private defaults =
    Regex
        @"\bdefaultArg\b|\bOption\.defaultValue\b|\bdefaultWith\b|\bValueOption\.default\w*|\bEpsilon\b|\bfloat(?:32)?\s*\(?\s*(?:""""|-?\d)|\bdouble\b|\b\w*Parse\w*\b|(?<=[(,]\s*)\?\s*[A-Za-z_]\w*"

/// The float literals the record accounts for, by module: the literal, how often it is written, and
/// what it is there for. A literal that is not here, or is here another number of times, is a finding.
let private accountedLiterals: (string * string * int * string) list =
    [ "Multivector.fs",
      "0.0",
      9,
      "the zero test inside the hash, once (item 8); the `+0.0` a fold starts from, five times (item 3); and the `+0.0` of a coefficient a scalar, a basis blade and a grade projection do not hold, three times (items 2 and 4)"
      "Multivector.fs", "1.0", 1, "a basis blade's one coefficient (item 2); in no comparison"
      "Conformal.fs",
      "0.0",
      19,
      "in a comparison, eight times: a divisor against zero, the norm of a direction against zero, a threshold of zero, a threshold that is not negative, a radius that is not negative, a numerator that is zero, and the signs of a numerator and of a denominator at a threshold of zero (item 6); inside the norm, four times: its `m = 0`, the zero it answers, and the `+0.0` its largest magnitude and its sum start from (D49 item 4, outside the rule); and the `+0.0` of a coefficient a vector built from parameters does not hold, seven times (item 4)"
      "Conformal.fs", "0.5", 2, "the half of `½ x²` and of `½ r²` (item 4); in no comparison"
      "Conformal.fs", "2.0", 1, "the `−2.0` of a power (item 6); in no comparison"
      "Conformal.fs", "1.0", 1, "the weight of `up x` (item 4); in no comparison"
      // The floats that are named and not written.
      "Basis.fs",
      "infinity",
      6,
      "the position of `n∞` among the basis vectors: an integer that shadows the float of that name, in no comparison of floats"
      "Conformal.fs", "infinity", 4, "the same position, where the groups of item 6 are read off the blades"
      "Multivector.fs", "NaN", 1, "the one NaN a float leaves a public function with (item 3)"
      "Conformal.fs", "NaN", 1, "the norm's answer where an element is a NaN (D49 item 4)"
      "Conformal.fs", "PositiveInfinity", 1, "the norm's answer where its largest magnitude is infinite (D49 item 4)"
      "Conformal.fs",
      "nan",
      3,
      "a flag inside the norm, raised where an element is a NaN: a boolean that shadows the float of that name" ]

/// One finding for each way some sources depart from the account: a literal written more or fewer
/// times than accounted for, and each way of giving a default.
let private literalFindings (sources: (string * string) list) : string list =
    let found =
        [ for file, source in sources do
              for literal, uses in floatLiterals source |> List.countBy id -> (file, literal), uses ]
        |> Map.ofList

    let accounted =
        accountedLiterals
        |> List.map (fun (file, literal, uses, _) -> (file, literal), uses)
        |> Map.ofList

    [ for KeyValue((file, literal), uses) in found do
          match Map.tryFind (file, literal) accounted with
          | Some n when n = uses -> ()
          | Some n -> yield sprintf "%s writes `%s` %d times, and %d are accounted for" file literal uses n
          | None -> yield sprintf "%s writes `%s` %d times, and none is accounted for" file literal uses
      for KeyValue((file, literal), n) in accounted do
          if not (Map.containsKey (file, literal) found) then
              yield sprintf "%s no longer writes `%s`, accounted for %d times" file literal n
      for file, source in sources do
          for m in defaults.Matches(codeOf source) -> sprintf "%s gives a default: `%s`" file m.Value ]

[<Tests>]
let tests =
    testList
        "Conformal objects"
        [

          testCase "the arithmetic a witness perturbs is the shipped arithmetic"
          <| fun () ->
              Expect.equal (List.length conformal) 836 "the object modules' vectors"

              for name, r in
                  [ "Arithmetic.shipped", shipped
                    "the fold's door", products Kernel.twoRoundings 0.0 id ] do
                  for c in conformal do
                      match answerUnder r c with
                      | Some cell ->
                          Expect.equal (cellText cell) committed.Value[c.Id] (sprintf "%s under %s" c.Id name)
                      | None -> Expect.isTrue (copies.Contains c.Op) (sprintf "%s has no door and is no copy" c.Op)

              // Every function but the copies has a door, and the copies have none.
              let doored =
                  conformal
                  |> List.filter (fun c -> Option.isSome (answerUnder shipped c))
                  |> List.map (fun c -> c.Op)
                  |> Set.ofList

              // A real pair's root is of a positive number (DECISIONS.md D87 item 5): each term of `X ∗ X` is
              // the negation of a term of `X ∗ X̃`, so one is the other negated wherever either is not zero.
              for c in
                  conformal
                  |> List.filter (fun c -> c.Op.StartsWith "PointPair." && List.exists (fun (n, _) -> n = "x") c.Input) do
                  let x = PointPair.toMultivector (V.pairOf (V.floats c "x"))
                  let square = Multivector.scalarProduct x x
                  let norm = Multivector.squaredNorm x

                  Expect.isTrue
                      (square = -norm || (Double.IsNaN square && Double.IsNaN norm))
                      (sprintf "%s: X * X is -(X * rev X)" c.Id)

              Expect.equal (Set.count doored) 52 "fifty-two functions compute"
              Expect.equal (Set.count copies) 18 "eighteen copy"
              Expect.isTrue (Set.isEmpty (Set.intersect doored copies)) "no function is both"

          testCase "each wrong port fails a vector of a function, or a drawn search finds it unable to change it"
          <| fun () ->
              // What this holds, for every wrong port and every function that computes, is ONE of two
              // things. Either a committed vector of that function is red under the port; or the port is
              // listed as unable to change that function, with the reason, and it answers that function
              // as the shipped arithmetic does on every element of the drawn sample. A port that changes
              // an answer in the sample and fails no committed vector of that function fails this case.
              // It does not hold that a port fails a vector of every function it COULD change: a search
              // over a sample is not a proof that a port cannot.
              let computing =
                  conformal
                  |> List.map (fun c -> c.Op)
                  |> List.filter (fun op -> not (copies.Contains op))
                  |> Set.ofList

              Expect.equal (Set.count computing) 52 "the functions that compute"
              Expect.isGreaterThan drawnSample.Value.Length 15000 "the drawn sample"

              let findings =
                  [ for name, port in wrongPorts do
                        let red = redUnder port |> List.map fst |> Set.ofList

                        let declared =
                            unable
                            |> List.filter (fun (p, _, _) -> p = name)
                            |> List.collect (fun (_, _, functions) -> functions)

                        if List.distinct declared <> declared then
                            yield sprintf "REPEATED\t%s: a function is listed unable twice" name

                        if Set.isEmpty red then
                            yield sprintf "VACUOUS\t%s fails no committed vector at all" name

                        for f in Set.difference (Set.difference computing red) (Set.ofList declared) do
                            yield sprintf "UNLISTED\t%s\t%s" name f

                        for f in Set.intersect red (Set.ofList declared) do
                            yield sprintf "LISTED-BUT-RED\t%s\t%s" name f

                        for KeyValue(f, (index, id, tau)) in changedUnder port do
                            if not (red.Contains f) then
                                yield
                                    sprintf
                                        "MISSING\t%s\t%s\t%d\t%s\t%s"
                                        name
                                        f
                                        index
                                        id
                                        (match tau with
                                         | Some t -> t.ToString "R"
                                         | None -> "-") ]

              if not (List.isEmpty findings) then
                  failtestf "the wrong ports and the vectors disagree:\n%s" (String.concat "\n" findings)

          testCase "a normalisation the record does not apply fails a vector"
          <| fun () ->
              // (DECISIONS.md D85 item 5.) An object built through points or by a meet has the weight its
              // product gives. The witness is the published function followed by the published
              // normalisation: red wherever that weight is not one.
              let normalisedAnswer (c: V.Case) : CorpusCell option =
                  let point (name: string) = V.pointOf (V.floats c name)
                  let line (name: string) = V.lineOf (V.floats c name)
                  let plane (name: string) = V.planeOf (V.floats c name)
                  let sphere (name: string) = V.sphereOf (V.floats c name)

                  let pair (x: PointPair) =
                      PointPair.normalise x |> Result.toOption |> Option.map V.pairCell

                  let circle (x: Circle) =
                      Circle.normalise x |> Result.toOption |> Option.map V.circleCell

                  let lineOf (x: Line) =
                      Line.normalise x |> Result.toOption |> Option.map V.lineCell

                  let planeOf (x: Plane) =
                      Plane.normalise x |> Result.toOption |> Option.map V.planeCell

                  match c.Op with
                  | "PointPair.through" -> pair (PointPair.through (point "p") (point "q"))
                  | "Line.through" -> lineOf (Line.through (point "p") (point "q"))
                  | "Circle.through" -> circle (Circle.through (point "p") (point "q") (point "r"))
                  | "Plane.through" -> planeOf (Plane.through (point "p") (point "q") (point "r"))
                  | "Sphere.through" ->
                      Sphere.normalise (Sphere.through (point "p") (point "q") (point "r") (point "s"))
                      |> Result.toOption
                      |> Option.map V.sphereCell
                  | "Line.meetPlane" -> pair (Line.meetPlane (line "x") (plane "y"))
                  | "Line.meetSphere" -> pair (Line.meetSphere (line "x") (sphere "y"))
                  | "Plane.meet" -> lineOf (Plane.meet (plane "x") (plane "y"))
                  | "Sphere.meet" -> circle (Sphere.meet (sphere "x") (sphere "y"))
                  | "Sphere.meetPlane" -> circle (Sphere.meetPlane (sphere "x") (plane "y"))
                  | "Circle.plane" -> planeOf (Circle.plane (V.circleOf (V.floats c "x")))
                  | _ -> None

              // An operand normalised before a statistic that divides its weight out itself.
              let operandsNormalised (c: V.Case) : CorpusCell option =
                  let point () =
                      Point.normalise (V.pointOf (V.floats c "p"))

                  let x () = V.floats c "x"

                  match c.Op with
                  | "Sphere.power" ->
                      match point (), Sphere.normalise (V.sphereOf (x ())) with
                      | Ok p, Ok s -> Some(V.resultCell V.floatCell (Sphere.power p s))
                      | _ -> None
                  | "Plane.signedDistance" ->
                      match point (), Plane.normalise (V.planeOf (x ())) with
                      | Ok p, Ok s -> Some(V.resultCell V.floatCell (Plane.signedDistance p s))
                      | _ -> None
                  | "Circle.power" ->
                      match point (), Circle.normalise (V.circleOf (x ())) with
                      | Ok p, Ok s -> Some(V.resultCell V.floatCell (Circle.power p s))
                      | _ -> None
                  | _ -> None

              let red (perturbed: V.Case -> CorpusCell option) : Set<string> =
                  conformal
                  |> List.filter (fun c ->
                      match perturbed c with
                      | Some cell -> cellText cell <> committed.Value[c.Id]
                      | None -> false)
                  |> List.map (fun c -> c.Op)
                  |> Set.ofList

              Expect.equal
                  (red normalisedAnswer)
                  (set
                      [ "PointPair.through"
                        "Line.through"
                        "Circle.through"
                        "Plane.through"
                        "Sphere.through"
                        "Line.meetSphere"
                        "Plane.meet"
                        "Sphere.meet"
                        "Sphere.meetPlane"
                        "Circle.plane" ])
                  "an answer normalised unasked fails a vector of each function that builds an object"

              // `Line.meetPlane` answers a flat point, a pair of no weight as a round, so its
              // normalisation is refused and there is no normalised answer to compare.
              Expect.isFalse (Set.contains "Line.meetPlane" (red normalisedAnswer)) "a flat point has no round weight"

              Expect.equal
                  (red operandsNormalised)
                  (set [ "Sphere.power"; "Plane.signedDistance"; "Circle.power" ])
                  "operands normalised first fail a vector of each statistic"

          testCase "every degenerate configuration answers its named case"
          <| fun () ->
              let ok (r: Result<'a, GeometryRefusal>) : 'a =
                  match r with
                  | Ok x -> x
                  | Error refusal -> failtestf "refused: %A" refusal

              let u (x: float) (y: float) (z: float) = ok (Point.up (x, y, z))

              let lineAt (h: float) =
                  ok (Line.ofPointDirection (0.0, h, 0.0) (1.0, 0.0, 0.0))

              let planeAt (h: float) =
                  ok (Plane.ofNormalDistance (0.0, 0.0, 1.0) h)

              let sphereAt (x: float) =
                  ok (Sphere.ofCentreRadius (x, 0.0, 0.0) 1.0)

              let xAxis = lineAt 0.0
              let unitSphere = sphereAt 0.0

              let within (first: Line) (second: Line) : PointPairKind =
                  match ok (Line.meet 0.0 first second) with
                  | LineMeet.Within pair -> ok (PointPair.kind 0.0 pair)
                  | other -> failtestf "two lines of one plane answered %A" other

              // Two lines.
              Expect.equal (within xAxis (lineAt 2.0)) PointPairKind.AtInfinity "two parallel lines meet at infinity"

              Expect.equal
                  (within xAxis (ok (Line.ofPointDirection (2.0, -1.0, 0.0) (0.0, 1.0, 0.0))))
                  (PointPairKind.FlatPoint(2.0, 0.0, 0.0))
                  "two lines that cross meet in a point"

              Expect.equal (Line.meet 0.0 (lineAt 2.0) (lineAt 2.0)) (Ok LineMeet.SameLine) "one line twice"

              match ok (Line.meet 0.0 xAxis (ok (Line.ofPointDirection (0.0, 0.0, 1.0) (0.0, 1.0, 0.0)))) with
              | LineMeet.Skew p ->
                  Expect.equal (p.N0, p.E1, p.E2, p.E3, p.NInf) (0.0, 0.0, 0.0, 0.0, -1.0) "skew: a multiple of n∞"
              | other -> failtestf "two skew lines answered %A" other

              // Two spheres.
              let apart = Sphere.meet unitSphere (sphereAt 3.0)
              Expect.equal (Circle.kind 0.0 apart) (Ok(CircleKind.Round Reality.Imaginary)) "two spheres apart"
              Expect.equal (Circle.squaredRadius apart) (Ok -1.25) "a negative squared radius is reported"
              Expect.equal (Circle.centre apart) (Ok(1.5, 0.0, 0.0)) "and its centre is still known"

              Expect.equal
                  (Circle.kind 0.0 (Sphere.meet unitSphere (sphereAt 2.0)))
                  (Ok(CircleKind.Round Reality.Tangent))
                  "two spheres that touch"

              Expect.equal (Circle.kind 0.0 (Sphere.meet unitSphere unitSphere)) (Ok CircleKind.Zero) "one sphere twice"

              // Points that define no round.
              let collinear = Circle.through (u 0.0 0.0 0.0) (u 1.0 2.0 3.0) (u 2.0 4.0 6.0)

              match ok (Circle.kind 0.0 collinear) with
              | CircleKind.Flat line ->
                  Expect.equal
                      (Line.direction line)
                      (14.0, 28.0, 42.0)
                      "three collinear points: the case carries the line"
              | other -> failtestf "three collinear points answered %A" other

              Expect.equal
                  (Circle.squaredRadius collinear)
                  (Error GeometryRefusal.ZeroDivisor)
                  "and no radius is invented"

              match
                  ok (Sphere.kind 0.0 (Sphere.through (u 0.0 0.0 0.0) (u 1.0 0.0 0.0) (u 0.0 1.0 0.0) (u 1.0 2.0 0.0)))
              with
              | SphereKind.Flat plane ->
                  Expect.equal (Plane.normal plane) (0.0, 0.0, 1.0) "four coplanar points: the plane"
              | other -> failtestf "four coplanar points answered %A" other

              Expect.equal
                  (Sphere.kind 0.0 (Sphere.through (u 0.0 0.0 0.0) (u 1.0 0.0 0.0) (u 0.0 1.0 0.0) (u 1.0 1.0 0.0)))
                  (Ok SphereKind.Zero)
                  "four points of one circle"

              // A line and a sphere.
              match
                  ok (PointPair.points 0.0 (Line.meetSphere xAxis (ok (Sphere.ofCentreRadius (0.0, 0.0, 0.0) 5.0))))
              with
              | PairPoints.Real(first, second) ->
                  Expect.equal
                      (Point.coordinates first, Point.coordinates second)
                      (Ok(-5.0, 0.0, 0.0), Ok(5.0, 0.0, 0.0))
                      "a secant: two points, along the line's direction"
              | other -> failtestf "a secant answered %A" other

              Expect.equal
                  (PointPair.points 0.0 (Line.meetSphere (lineAt 1.0) unitSphere))
                  (Ok(PairPoints.Tangent(0.0, 1.0, 0.0)))
                  "a tangent: its one point"

              Expect.equal
                  (PointPair.points 0.0 (Line.meetSphere (lineAt 2.0) unitSphere))
                  (Ok(PairPoints.Imaginary((0.0, 2.0, 0.0), -3.0)))
                  "a miss: the centre and the negative squared radius, and no root of it"

              // A line and a plane; two planes.
              Expect.equal
                  (PointPair.kind 0.0 (Line.meetPlane xAxis (ok (Plane.ofNormalDistance (1.0, 0.0, 0.0) 3.0))))
                  (Ok(PointPairKind.FlatPoint(3.0, 0.0, 0.0)))
                  "a line through a plane: the point"

              Expect.equal
                  (PointPair.kind 0.0 (Line.meetPlane xAxis (planeAt 1.0)))
                  (Ok PointPairKind.AtInfinity)
                  "parallel"

              Expect.equal (PointPair.kind 0.0 (Line.meetPlane xAxis (planeAt 0.0))) (Ok PointPairKind.Zero) "within"

              Expect.equal
                  (Line.kind 0.0 (Plane.meet (planeAt 0.0) (planeAt 1.0)))
                  (Ok FlatKind.AtInfinity)
                  "parallel planes"

              Expect.equal (Line.kind 0.0 (Plane.meet (planeAt 1.0) (planeAt 1.0))) (Ok FlatKind.Zero) "one plane twice"

              // What is refused, by name.
              Expect.equal (Point.up (nan, 0.0, 0.0)) (Error GeometryRefusal.NonFiniteCoordinate) "a NaN coordinate"

              Expect.equal
                  (Line.ofPointDirection (0.0, 0.0, 0.0) (0.0, 0.0, 0.0))
                  (Error GeometryRefusal.ZeroDirection)
                  "a zero direction"

              Expect.equal
                  (Plane.ofNormalDistance (infinity, 0.0, 0.0) 0.0)
                  (Error GeometryRefusal.NonFiniteDirection)
                  "an infinite normal"

              Expect.equal
                  (Sphere.ofCentreRadius (0.0, 0.0, 0.0) -1.0)
                  (Error GeometryRefusal.NegativeRadius)
                  "a negative radius"

              Expect.equal
                  (Sphere.ofCentreRadius (0.0, 0.0, 0.0) nan)
                  (Error GeometryRefusal.NonFiniteRadius)
                  "a NaN radius"

              Expect.equal
                  (Plane.ofNormalDistance (0.0, 0.0, 1.0) infinity)
                  (Error GeometryRefusal.NonFiniteDistance)
                  "an infinite distance"

              Expect.equal
                  (Sphere.side -1.0 (u 0.0 0.0 0.0) unitSphere)
                  (Error GeometryRefusal.InvalidThreshold)
                  "a negative threshold"

              Expect.equal
                  (Blade.inverse (Multivector.basis 1))
                  (Error GeometryRefusal.ZeroDivisor)
                  "a null blade has no inverse"

              // A line whose nearest point overflows: the read answers the infinity, which is a value, and
              // the two-line rule, which embeds that point, is refused as `up` refuses it.
              let far: Line =
                  { N0E1NInf = 0.0
                    N0E2NInf = -1.0
                    N0E3NInf = 0.07918293826789391
                    E1E2NInf = -Double.MaxValue
                    E1E3NInf = Double.MaxValue
                    E2E3NInf = 0.0 }

              Expect.equal (Line.nearestPoint far) (Ok(infinity, 0.0, 0.0)) "a nearest point past the largest double"

              Expect.equal
                  (Line.meet Double.MaxValue xAxis far)
                  (Error GeometryRefusal.NonFiniteCoordinate)
                  "and the two-line rule cannot embed it"

          testCase "each named case goes red where the rule that names it is changed"
          <| fun () ->
              let port (name: string) : Arithmetic =
                  wrongPorts |> List.find (fun (n, _) -> n = name) |> snd

              let never = "a threshold never met"
              let always = "a threshold always met"
              let strict = "a strict threshold"
              let undivided = "no test of a zero divisor"
              let seeded = "a fold seeded from its first term"
              let regrouped = "a product of three factors formed from the right"
              let reordered = "the norm's squares summed in another order"
              let undirected = "no test of a zero direction"
              let fromAbove = "a threshold met from above"
              let quotient = "a quotient formed at a threshold of zero"
              let signless = "a power formed as zero less twice the product"

              for id, name in
                  [ "PointPair.points-tangent-at-the-threshold", never
                    "PointPair.points-imaginary-just-below-the-threshold", always
                    "PointPair.points-tangent-at-the-threshold", strict
                    "PointPair.points-tangent-at-the-negative-threshold", strict
                    "PointPair.points-of-a-flat-point", undivided
                    "PointPair.kind-tangent-at-the-threshold", never
                    "PointPair.kind-of-a-flat-point", never
                    "PointPair.kind-at-infinity", never
                    "PointPair.kind-of-the-zero-blade", never
                    "PointPair.kind-of-a-euclidean-bivector", always
                    "PointPair.kind-no-round-at-the-threshold", strict
                    "PointPair.kind-no-flat-at-the-threshold", strict
                    "PointPair.kind-zero-at-the-threshold", strict
                    "Circle.kind-of-three-collinear-points", never
                    "Circle.kind-imaginary-just-below-the-threshold", always
                    "Circle.kind-tangent-at-the-negative-threshold", never
                    "Circle.kind-of-the-zero-blade", never
                    "Circle.kind-tangent-at-the-negative-threshold", strict
                    "Circle.squaredRadius-of-three-collinear-points", undivided
                    "PointPair.kind-of-a-round-whose-divisor-underflows", undivided
                    "Circle.kind-of-a-round-whose-divisor-underflows", undivided
                    "Sphere.kind-of-a-round-whose-divisor-underflows", undivided
                    "Sphere.kind-of-four-coplanar-points", never
                    "Sphere.kind-of-four-points-of-one-circle", never
                    "Sphere.kind-no-round-at-the-threshold", strict
                    "Sphere.centre-of-four-coplanar-points", undivided
                    "Line.kind-at-infinity", never
                    "Line.kind-of-the-zero-blade", never
                    "Line.kind-at-infinity-at-the-threshold", strict
                    "Plane.kind-at-infinity", never
                    "Plane.kind-zero-at-the-threshold", strict
                    "Line.meet-of-two-lines-that-cross", never
                    "Line.meet-of-two-parallel-lines", never
                    "Line.meet-of-one-line-twice", never
                    "Line.meet-not-skew-at-the-threshold", strict
                    "Line.meet-one-line-at-the-threshold", strict
                    "Line.nearestPoint-of-a-line-at-infinity", undivided
                    "Line.ofPointDirection-of-a-zero-direction", undirected
                    "Plane.ofNormalDistance-of-a-zero-normal", undirected
                    "Circle.ofCentreRadiusNormal-of-a-zero-normal", undirected
                    "Point.coordinates-of-a-zero-weight", undivided
                    "PointPair.normalise-of-a-flat-point", undivided
                    "Blade.inverse-of-a-null-vector", undivided
                    "Blade.meetWithin-a-null-span", undivided
                    "Sphere.power-of-a-zero-weight-point", undivided
                    "Sphere.power-whose-weights-underflow", undivided
                    "Sphere.side-on-at-the-threshold", strict
                    "Sphere.side-on-at-the-negative-threshold", never
                    "Plane.side-on-at-the-threshold", strict
                    "Circle.side-on-at-the-threshold", strict
                    "Sphere.side-decided-by-signs-where-the-quotient-underflows", quotient
                    "Plane.side-decided-by-signs-where-the-quotient-underflows", quotient
                    "Circle.side-decided-by-signs-where-the-quotient-underflows", quotient
                    "Sphere.power-on", signless
                    // Every term a negative zero, over untyped operands; a centre formed in the other
                    // grouping, in a pair's points; a norm summed from the right.
                    "Blade.join-whose-every-term-is-a-negative-zero", seeded
                    "PointPair.points-general-1000-at-a-further-threshold", regrouped
                    "Line.weight-general-1002", reordered
                    "PointPair.weight-general-1002", reordered
                    // The other side of each threshold: "above" is a member of its own.
                    "PointPair.points-tangent-at-the-threshold", fromAbove
                    "PointPair.kind-no-round-at-the-threshold", fromAbove
                    "PointPair.kind-no-flat-at-the-threshold", fromAbove
                    "Line.kind-at-infinity-at-the-threshold", fromAbove
                    "Plane.kind-zero-at-the-threshold", fromAbove
                    "Sphere.kind-no-round-at-the-threshold", fromAbove
                    "Line.meet-not-skew-at-the-threshold", fromAbove
                    "Sphere.side-on-at-the-threshold", fromAbove
                    "Plane.side-on-at-the-threshold", fromAbove
                    "Circle.side-on-at-the-threshold", fromAbove
                    // A round whose squared radius underflows: at a threshold of zero its reality is
                    // read from the signs, and a port that forms the quotient reads a tangent.
                    "PointPair.kind-imaginary-by-signs-where-the-quotient-underflows", quotient
                    "PointPair.kind-real-by-signs-where-the-quotient-underflows", quotient
                    "PointPair.points-imaginary-by-signs-where-the-quotient-underflows", quotient
                    "PointPair.points-real-by-signs-where-the-quotient-underflows", quotient
                    "Circle.kind-imaginary-by-signs-where-the-quotient-underflows", quotient
                    "Circle.kind-real-by-signs-where-the-quotient-underflows", quotient
                    "Sphere.kind-imaginary-by-signs-where-the-quotient-underflows", quotient
                    "Sphere.kind-real-by-signs-where-the-quotient-underflows", quotient
                    "Circle.power-on", signless ] do
                  let c = V.find id

                  match answerUnder shipped c, answerUnder (port name) c with
                  | Some green, Some red ->
                      Expect.equal (cellText green) committed.Value[id] (sprintf "%s: the shipped rule answers it" id)
                      Expect.notEqual (cellText red) committed.Value[id] (sprintf "%s: %s answers it too" id name)
                  | _ -> failtestf "%s has no door" id

          testCase "the two constructions of each object agree in orientation"
          <| fun () ->
              let ok (r: Result<'a, GeometryRefusal>) : 'a =
                  match r with
                  | Ok x -> x
                  | Error refusal -> failtestf "refused: %A" refusal

              let u (x: float, y: float, z: float) = ok (Point.up (x, y, z))

              let p, q, r, s =
                  (1.0, -2.0, 3.0), (4.0, 0.0, -1.0), (-2.0, 5.0, 2.0), (0.0, 1.0, -4.0)

              let minus (a1: float, a2: float, a3: float) (b1: float, b2: float, b3: float) = a1 - b1, a2 - b2, a3 - b3

              let cross (a1: float, a2: float, a3: float) (b1: float, b2: float, b3: float) =
                  a2 * b3 - a3 * b2, a3 * b1 - a1 * b3, a1 * b2 - a2 * b1

              let dot (a1: float, a2: float, a3: float) (b1: float, b2: float, b3: float) =
                  (a1 * b1 + a2 * b2) + a3 * b3

              let normal = cross (minus q p) (minus r p)

              Expect.equal
                  (Line.direction (Line.through (u p) (u q)))
                  (minus q p)
                  "a line through p and q has direction q − p"

              Expect.equal
                  (Plane.normal (Plane.through (u p) (u q) (u r)))
                  normal
                  "a plane's normal is (q − p) × (r − p)"

              Expect.equal
                  (Sphere.weight (Sphere.through (u p) (u q) (u r) (u s)))
                  (dot normal (minus s p))
                  "a sphere's weight is det [q − p, r − p, s − p]"

              // A circle from parameters runs counter-clockwise about its normal: it is the circle through
              // three points taken in that order, divided by its weight.
              Expect.equal
                  (Circle.ofCentreRadiusNormal (0.0, 0.0, 0.0) 1.0 (0.0, 0.0, 1.0))
                  (Circle.normalise (Circle.through (u (1.0, 0.0, 0.0)) (u (0.0, 1.0, 0.0)) (u (-1.0, 0.0, 0.0))))
                  "a circle from parameters is counter-clockwise about its normal"

              // The line built from a point and a direction is the line through two points of it.
              Expect.equal
                  (Line.ofPointDirection (1.0, 1.0, 0.0) (2.0, 0.0, 0.0))
                  (Line.normalise (Line.through (u (1.0, 1.0, 0.0)) (u (5.0, 1.0, 0.0))))
                  "a line from parameters has the direction given"

              Expect.equal
                  (Plane.ofNormalDistance (0.0, 0.0, 4.0) 1.0 |> Result.map Plane.normal)
                  (Ok(Plane.normal (Plane.through (u (0.0, 0.0, 1.0)) (u (1.0, 0.0, 1.0)) (u (0.0, 1.0, 1.0)))))
                  "a plane from parameters has the normal given"

          testCase "the objects carry the indices of the record's table, in order"
          <| fun () ->
              // DECISIONS.md D85 item 8's table, as written.
              let carried (toMultivector: float[] -> Multivector) (size: int) : int list =
                  let m =
                      Multivector.toArray (toMultivector (Array.init size (fun k -> float (k + 1))))

                  Expect.equal (m |> Array.filter (fun x -> x <> 0.0) |> Array.length) size "and nothing else"
                  [ for value in 1..size -> Array.findIndex (fun x -> x = float value) m ]

              Expect.equal (carried (V.pointOf >> Point.toMultivector) 5) [ 1..5 ] "Point"
              Expect.equal (carried (V.pairOf >> PointPair.toMultivector) 10) [ 6..15 ] "PointPair"
              Expect.equal (carried (V.lineOf >> Line.toMultivector) 6) [ 18; 20; 21; 23; 24; 25 ] "Line"
              Expect.equal (carried (V.circleOf >> Circle.toMultivector) 10) [ 16..25 ] "Circle"
              Expect.equal (carried (V.planeOf >> Plane.toMultivector) 4) [ 27..30 ] "Plane"
              Expect.equal (carried (V.sphereOf >> Sphere.toMultivector) 5) [ 26..30 ] "Sphere"

              let line = V.lineOf [| 1.0; 2.0; 3.0; 4.0; 5.0; 6.0 |]
              Expect.equal (Line.ofMultivector (Line.toMultivector line)) line "a copy back is the object"

          testCase "the norm is the linear-algebra package's, bit for bit"
          <| fun () ->
              // The algebra package cannot reference LinearAlgebra, so DECISIONS.md D49 item 4's routine is
              // transcribed there. This suite references both.
              let bits (x: float) = BitConverter.DoubleToInt64Bits x

              let theirs (xs: float[]) =
                  Fuaran.Compute.LinearAlgebra.Blas.norm2 (Vector.ofList (List.ofArray xs))

              let mutable state = 728.0

              let next () =
                  state <- (state * 48271.0) % 2147483647.0
                  state

              let draw () =
                  let mutable x = next () / 2147483648.0 - 0.5
                  let exponent = int (next () % 601.0) - 300

                  for _ in 1 .. (if exponent < 0 then -exponent else exponent) do
                      x <- if exponent < 0 then x / 2.0 else x * 2.0

                  x

              let drawn =
                  [ for _ in 1..2000 do
                        yield [| draw () |]
                        yield [| draw (); draw (); draw () |] ]

              let special =
                  [ [||]
                    [| 0.0; -0.0; 0.0 |]
                    [| nan; 1.0; infinity |]
                    [| 1.0; -infinity; 2.0 |]
                    [| 1e200; -1e200; 1e200 |]
                    [| 1.5e308; 1.5e308; 1.5e308 |]
                    [| 3e-200; 4e-200; 0.0 |]
                    [| 5e-324; 5e-324; -5e-324 |]
                    [| -3.0; 4.0; 0.0 |]
                    [| -7.5 |] ]

              for xs in special @ drawn do
                  Expect.equal (bits (Norm.scaled xs)) (bits (theirs xs)) (sprintf "the norm of %A" xs)

              Expect.equal (List.length drawn) 4000 "the sample"

          testCase "the operation modules hold exactly the float literals the record accounts for, and give no default"
          <| fun () ->
              // DECISIONS.md D85 item 6: no tolerance is a constant, and `0.0` and `1.0` are named only
              // where the record says. A hunt for comparisons misses a default argument, a constant
              // with a neutral name, an operator section, a comparison split over lines, a match on a
              // literal. So this is a census: EVERY float literal token of the operation modules, held
              // equal to a stated account. Its scope is the operation modules, not the law family.
              let sources =
                  [ for file in operationModules ->
                        file,
                        File.ReadAllText(
                            Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.GeometricAlgebra", file))
                        ) ]

              let wholeAccount =
                  [ for file, source in sources do
                        for literal, uses in floatLiterals source |> List.countBy id ->
                            sprintf "\"%s\", \"%s\", %d" file literal uses ]

              Expect.isEmpty (literalFindings sources) (sprintf "the literals as written: %A" wholeAccount)

              Expect.isGreaterThan
                  (sources |> List.sumBy (fun (_, source) -> (codeOf source).Length))
                  20000
                  "the sources were read"

              // The census is not vacuous: each of these, written into a module, is a finding.
              let conformalSource =
                  sources |> List.find (fun (file, _) -> file = "Conformal.fs") |> snd

              for shape in
                  [ "let side (tau: float option) = decide (defaultArg tau 1e-9)"
                    "let side (tau: float option) = decide (Option.defaultValue 1e-9 tau)"
                    "let side (tau: float) = decide (max tau 1e-12)"
                    "let smallest = 1e-9"
                    "let isOne (x: float) = x = 1."
                    "let small = List.filter ((>) 1e-9)"
                    "let small (x: float) =\n    x <\n        1e-9"
                    "let isZero (x: float) =\n    match x with\n    | 0.0 -> true\n    | _ -> false"
                    "member _.Side(p: Point, ?tau: float) = p"
                    // Spellings and routes a first census did not see.
                    "let tiny = 0x3E112E0BE826D695LF"
                    "let step = 1_000.0"
                    "let single = 1.0f"
                    "let money = 1e-9M"
                    "let least = Double.Epsilon"
                    "let least = System.Double.Epsilon"
                    "let parsed = float \"1e-9\""
                    "let ratio = float 1 / float 1_000_000_000"
                    // The two forms the account omitted until Phase 729: the conversion under its other
                    // name, and a parsed string, in a literal or in a variable.
                    "let ratio = double 1 / double 1_000_000_000"
                    "let parsed = Double.Parse \"1e-9\""
                    "let parsed (text: string) = System.Double.TryParse text"
                    "let side tau = decide (Option.defaultWith (fun () -> one) tau)"
                    "let side tau = decide (ValueOption.defaultValue one tau)"
                    "let far = infinity"
                    "let isZero (x: float) = x = 0.0" ] do
                  Expect.isNonEmpty
                      (literalFindings [ "Conformal.fs", conformalSource + "\n" + shape ])
                      (sprintf "the census reads: %s" shape)

              // And it reads through what is not code: a literal in a comment or in a string is none.
              Expect.equal
                  (floatLiterals "let a = 1 // 0.5\nlet b = \"2.0\" (* 3.0 *)\nlet c = xs[0].Length + 1..5")
                  []
                  "a comment, a string, an index and a range hold no float literal"

              Expect.equal
                  (floatLiterals "x = 1. || y < -2.5E-3 || z > 1e9")
                  [ "1."; "2.5E-3"; "1e9" ]
                  "the three spellings"

              Expect.equal
                  (floatLiterals "0x3FF0000000000000LF + 1_000.0 + 1.0f + 1e-9M + 0x7FC00000 + xs[1_000] + nan")
                  [ "0x3FF0000000000000LF"; "1_000.0"; "1.0f"; "1e-9M"; "nan" ]
                  "a hexadecimal double, an underscore, two suffixes and a name; and no integer"

              // Every published function that tests a zero takes `tau`, and takes it first.
              let thresholded =
                  [ for t in typeof<Multivector>.Assembly.GetExportedTypes() do
                        for m in t.GetMethods(BindingFlags.Public ||| BindingFlags.Static) do
                            if
                                m.Name = "kind"
                                || m.Name = "side"
                                || m.Name = "points"
                                || (m.Name = "meet" && t.Name = "LineModule")
                            then
                                yield t.Name + "." + m.Name, (m.GetParameters()[0]).Name ]

              Expect.equal (List.length thresholded) 10 "ten functions test a zero"

              Expect.isTrue
                  (thresholded |> List.forall (fun (_, first) -> first = "tau"))
                  "each takes its threshold first"

          testCase "the object modules call no host function but the root"
          <| fun () ->
              let hostCall =
                  Regex
                      @"Math\.\w+|(?<![\w.])(sqrt|exp|log|log10|log2|pown|floor|ceil|round|truncate|abs|sin|cos|tan|sinh|cosh|tanh|atan|atan2|asin|acos)\s+[\w(]|\*\*"

              let hits =
                  [ for file in [ "Objects.fs"; "Conformal.fs"; "Geometry.fs" ] do
                        for line in
                            File
                                .ReadAllText(
                                    Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.GeometricAlgebra", file))
                                )
                                .Split('\n') do
                            if not (line.TrimStart().StartsWith "//") then
                                for m in hostCall.Matches line -> file, m.Groups[1].Value ]

              // The root is correctly rounded on every host (D49's premise): in the norm, and for a real
              // point pair's `√(X ∗ X)`. No absolute value, no power, no fused multiply-add.
              Expect.equal hits [ "Conformal.fs", "sqrt"; "Conformal.fs", "sqrt" ] "two roots and nothing else"

          testCase "the closed-form bound is the largest error over the stated sample"
          <| fun () ->
              // DECISIONS.md D87: 2,000 drawn lines and spheres whose points of intersection are lattice
              // points, known exactly by construction and confirmed against the quadratic formula in exact
              // arithmetic by the cross-check. The law holds the bound; this holds the bound to the
              // measurement, so the record's two numbers are the ones measured.
              Expect.equal
                  (GeometricAlgebraLaws.closedFormSample ())
                  (Ok(GeometricAlgebraLaws.closedFormBoundUlps, GeometricAlgebraLaws.closedFormBoundAbsolute))
                  "the bound is the measurement, with no margin"

              Expect.isGreaterThan
                  GeometricAlgebraLaws.closedFormBoundUlps
                  0.0
                  "the agreement is to rounding, and not exact"


              // The bound is ONE sample's maximum. Other samples of the same kind measure other maxima,
              // and here they are: the largest error in units in the last place, and the largest
              // absolute error where the reference is zero, in units of `2^-53`. The absolute figure of
              // seed 4242 is more than twice the stated sample's. So the law does not take its sample
              // from the seed it is run at, and no law of the family goes red at another seed.
              for seed, ulps, absolute in
                  [ 728, 292.0, 208.0
                    1, 162.0, 304.0
                    2, 192.0, 384.0
                    3, 224.0, 272.0
                    590, 264.0, 272.0
                    727, 160.0, 320.0
                    729, 192.0, 231.0
                    1000, 196.0, 304.0
                    4242, 256.0, 512.0 ] do
                  Expect.equal
                      (GeometricAlgebraLaws.closedFormSampleAt seed)
                      (Ok(ulps, absolute / 9007199254740992.0))
                      (sprintf "the sample of seed %d" seed)

              for seed in [ 1; 4242 ] do
                  let closedForm =
                      GeometricAlgebraLaws.laws seed 3
                      |> List.find (fun (name, _) ->
                          name.StartsWith "a line and a sphere meet where the closed form says")

                  Expect.equal (snd closedForm) (Ok()) (sprintf "the law holds when the family is run at seed %d" seed)

          testCase "the claims ladder's row names this family's cases, word for word"
          <| fun () ->
              use ladder =
                  Text.Json.JsonDocument.Parse(File.ReadAllText(Snapshots.repoFile "proofs.json"))

              let row =
                  ladder.RootElement.GetProperty("claims").EnumerateArray()
                  |> Seq.find (fun c -> c.GetProperty("id").GetString() = "conformal-objects-pinned-by-recorded-rules")

              let evidence = row.GetProperty "evidence"
              Expect.equal (row.GetProperty("level").GetString()) "tested" "the row says tested, and no more"
              Expect.equal (evidence.GetProperty("family").GetString()) "Conformal objects" "the row names this family"

              let cases =
                  [ for c in evidence.GetProperty("cases").EnumerateArray() -> c.GetString() ]

              Expect.isGreaterThan (List.length cases) 5 "the row lists its cases"

              let source =
                  File.ReadAllText(
                      Snapshots.repoFile (Path.Combine("tests", "Fuaran.Core.Compute.Tests", "ConformalTests.fs"))
                  )

              for case in cases do
                  Expect.stringContains
                      source
                      (sprintf "testCase \"%s\"" case)
                      (sprintf "the case \"%s\" is a test here" case) ]
