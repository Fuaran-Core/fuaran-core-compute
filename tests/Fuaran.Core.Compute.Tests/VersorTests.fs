module Fuaran.Compute.Tests.VersorTests

// ---------------------------------------------------------------------------
// Phase 729 — the versors of Fuaran.Compute.GeometricAlgebra: rotors,
// translators, motors, dilators and reflections (DECISIONS.md D85 items 4, 5
// and 7; D88).
//
// What is held here, beside the law vectors themselves:
//   * every committed versor vector is the shipped arithmetic's answer, and the
//     published functions are that arithmetic;
//   * each wrong port is `Arithmetic.shipped` with ONE member changed, and for
//     every wrong port and every function that computes, either a committed
//     vector of that function is red under the port, or the port is unable to
//     change that function: because the function does not read the member, or
//     for a reason that is listed, and in both cases it answers the function as
//     the shipped arithmetic does on every element of a drawn sample;
//   * each rule D85 states by name has a committed vector that is red under
//     the one member that names it;
//   * a normalisation nobody asked for answers another bit;
//   * each bounded law's bound is its stated sample's measurement, and the
//     figures of eight other samples are held beside it;
//   * a versor and its matrix agree on points, through
//     `Fuaran.Compute.LinearAlgebra`, which only this suite references.
//
// No case reads the host's mathematics library, the clock or the file system
// for an expected value: an expectation is worked by hand, or the committed law
// file through the corpus reader.
// ---------------------------------------------------------------------------

open System
open System.IO
open Expecto
open Fuaran.Core
open Fuaran.Compute.GeometricAlgebra

module V = GeometricAlgebraVectors
module C = ConformalTests

let private shipped = Arithmetic.shipped

/// The committed versor vectors.
let private versorVectors: V.Case list =
    V.cases |> List.filter (fun c -> V.isVersorOp c.Op)

/// The published functions that perform no arithmetic a rule could change: the widening and the copy
/// back, and a translator from a vector, which is one exact halving and a negation.
let private copies: Set<string> =
    set
        [ for versor in V.versorTypes do
              yield versor + ".toMultivector"
              yield versor + ".ofMultivector"
          yield! [ "Motor.ofRotor"; "Motor.ofTranslator"; "Translator.ofVector" ] ]

let private indicesOf (kind: string) : int[] =
    match kind with
    | "Point" -> Indices.point
    | "PointPair" -> Indices.pair
    | "Line" -> Indices.line
    | "Circle" -> Indices.circle
    | "Plane" -> Indices.plane
    | "Sphere" -> Indices.sphere
    | "Rotor" -> Motion.rotorIndices
    | "Translator" -> Motion.translatorIndices
    | "Motor" -> Motion.motorIndices
    | "Dilator" -> Motion.dilatorIndices
    | "Reflection" -> Motion.reflectionIndices
    | other -> failwithf "no type is named %s" other

/// A versor case's answer under an arithmetic: the shipped function, called as the published one calls
/// it, with `r` where that one passes `Arithmetic.shipped`. `None` for a copy.
let private answerUnder (r: Arithmetic) (c: V.Case) : CorpusCell option =
    let floats = V.floats c
    let number = V.number c
    let triple = V.triple c
    let versor = c.Op.Substring(0, c.Op.IndexOf '.')
    let fn = c.Op.Substring(c.Op.IndexOf '.' + 1)

    let widened (name: string) =
        Conformal.widen (indicesOf versor) (floats name)

    let result (cell: 'a -> CorpusCell) (x: Result<'a, GeometryRefusal>) = Some(V.resultCell cell x)

    if copies.Contains c.Op then
        None
    elif fn.StartsWith "applyTo" then
        let target = indicesOf (fn.Substring "applyTo".Length)
        let x = Conformal.widen target (floats "x")

        let image =
            if versor = "Reflection" then
                Motion.reflect r (widened "v") x
            else
                Motion.sandwich r (widened "v") x

        Some(V.floatsCell (Conformal.copy target image))
    else
        match fn, c.Op with
        | "normalise", _ -> result V.floatsCell (Motion.normalise r (indicesOf versor) (floats "v"))
        | "toMatrix", _ -> result V.floatsCell (Motion.matrixOf r (widened "v"))
        | "compose", _ ->
            let into =
                if versor = "Reflection" then
                    Motion.motorIndices
                else
                    indicesOf versor

            Some(V.floatsCell (Motion.compose r into (widened "b") (widened "a")))
        | _, "Rotor.ofAxisAngle" -> result V.rotorCell (Motion.rotorOfAxisAngle r (triple "axis") (number "angle"))
        | _, "Rotor.exp" -> result V.rotorCell (Motion.rotorExp r (triple "bivector"))
        | _, "Rotor.log" -> result V.tripleCell (Motion.rotorLog r (V.rotorOf (floats "v")))
        | _, "Rotor.interpolate" ->
            result
                V.rotorCell
                (Motion.rotorInterpolate r (number "t") (V.rotorOf (floats "first")) (V.rotorOf (floats "second")))
        | _, "Motor.ofRotorTranslator" ->
            Some(
                V.motorCell (
                    Motion.motorOfRotorTranslator r (V.rotorOf (floats "rotor")) (V.translatorOf (floats "translator"))
                )
            )
        | _, "Motor.ofScrew" ->
            result
                V.motorCell
                (Motion.motorOfScrew r (triple "point") (triple "direction") (number "angle") (number "distance"))
        | _, "Motor.ofLine" ->
            result V.motorCell (Motion.motorOfLine r (V.lineOf (floats "line")) (number "angle") (number "distance"))
        | _, "Motor.exp" -> result V.motorCell (Motion.motorExp r (triple "bivector") (triple "vector"))
        | _, "Motor.log" -> result V.bivectorAndVectorCell (Motion.motorLog r (V.motorOf (floats "v")))
        | _, "Motor.interpolate" ->
            result
                V.motorCell
                (Motion.motorInterpolate r (number "t") (V.motorOf (floats "first")) (V.motorOf (floats "second")))
        | _, "Motor.ofMatrix" -> Some(V.motorAndFactorCell (Motion.motorOfMatrix r (number "tau") (floats "matrix")))
        | _, "Dilator.ofFactor" -> result V.dilatorCell (Motion.dilatorOfFactor r (number "factor"))
        | _, "Dilator.ofCentreFactor" ->
            result V.dilatorCell (Motion.dilatorOfCentreFactor r (triple "centre") (number "factor"))
        | _, "Reflection.ofNormalDistance" ->
            result V.reflectionCell (Motion.reflectionOfNormalDistance r (triple "normal") (number "distance"))
        | _, other -> failwithf "%s: no door for %s" c.Id other

// ---- the wrong ports of the versors: `Arithmetic.shipped` with one member changed ---------------------

let private bitsOf (x: float) : int64 = BitConverter.DoubleToInt64Bits x

/// The next double up: one unit in the last place.
let private up (x: float) : float =
    if Double.IsNaN x || Double.IsInfinity x then
        x
    elif x = 0.0 then
        BitConverter.Int64BitsToDouble 1L
    elif x > 0.0 then
        BitConverter.Int64BitsToDouble(bitsOf x + 1L)
    else
        BitConverter.Int64BitsToDouble(bitsOf x - 1L)

/// The correctly rounded value Phase 725's law file records beside one of its vectors, as a double.
let private referenceOf (id: string) : float =
    ElementaryVectors.references
    |> List.find (fun (vector, _) -> vector = id)
    |> snd
    |> fun hex -> BitConverter.Int64BitsToDouble(Convert.ToInt64(hex, 16))

let private fusedMinor (x: float) (y: float) (z: float) (w: float) : float = Math.FusedMultiplyAdd(x, y, -(z * w))

/// Each port, the members it changes, and the arithmetic. The first three members of a port of
/// Phase 728 are the four products.
let private versorPorts: (string * string list * Arithmetic) list =
    let correctSine = referenceOf "sin-not-correctly-rounded-below-quarter-pi"
    let correctCosine = referenceOf "cos-not-correctly-rounded-reduced"
    let correctAngle = referenceOf "atan2-not-correctly-rounded-first-quadrant"
    let y, x = V.arcTangentOffByOne

    [ "the correctly rounded sine and cosine where the pinned pair is a unit from them",
      [ "SinCos" ],
      { shipped with
          SinCos =
              fun a ->
                  let struct (s, c) = shipped.SinCos a

                  if bitsOf a = bitsOf V.sineOffByOne then
                      struct (correctSine, c)
                  elif bitsOf a = bitsOf V.cosineOffByOne then
                      struct (s, correctCosine)
                  else
                      struct (s, c) }
      "a sine one unit in the last place up",
      [ "SinCos" ],
      { shipped with
          SinCos =
              fun a ->
                  let struct (s, c) = shipped.SinCos a
                  struct (up s, c) }
      "a cosine one unit in the last place up",
      [ "SinCos" ],
      { shipped with
          SinCos =
              fun a ->
                  let struct (s, c) = shipped.SinCos a
                  struct (s, up c) }
      "the correctly rounded arc tangent where the pinned one is a unit from it",
      [ "Atan2" ],
      { shipped with
          Atan2 =
              fun n c ->
                  if bitsOf n = bitsOf y && bitsOf c = bitsOf x then
                      correctAngle
                  else
                      shipped.Atan2 n c }
      "an arc tangent one unit in the last place up",
      [ "Atan2" ],
      { shipped with
          Atan2 = fun n c -> up (shipped.Atan2 n c) }
      "the angle through an arc cosine",
      [ "Atan2" ],
      { shipped with
          Atan2 =
              fun n c ->
                  let q = c / sqrt (c * c + n * n)
                  shipped.Atan2 (sqrt (1.0 - q * q)) q }
      "the inverse in place of the reverse",
      [ "VersorReverse" ],
      { shipped with
          VersorReverse =
              fun v ->
                  let reversed = Multivector.reverse v
                  let d = Multivector.scalarProduct v reversed
                  Multivector.init (fun index -> Multivector.coefficient index reversed / d) }
      "the composition in the other order",
      [ "Compose" ],
      { shipped with
          Compose = fun product b a -> product a b }
      "a screw formed left to right",
      [ "Screw" ],
      { shipped with
          Screw = fun product along toAxis rotor back -> product (product (product along toAxis) rotor) back }
      "a quotient formed for each coefficient",
      [ "ByQuotient" ],
      { shipped with
          ByQuotient =
              fun divide numerator denominator xs -> xs |> Array.map (fun b -> divide (numerator * b) denominator) }
      "no test of a zero angle",
      [ "IsZeroAngle" ],
      { shipped with
          IsZeroAngle = fun _ -> false }
      "no test of a zero bivector part",
      [ "IsZeroBivector" ],
      { shipped with
          IsZeroBivector = fun _ -> false }
      "a dilator through the pinned exponential and logarithm",
      [ "DilatorParts" ],
      { shipped with
          DilatorParts =
              fun _ s ->
                  let h = 0.5 * Fuaran.Compute.Elementary.Elementary.ln s
                  let e = Fuaran.Compute.Elementary.Elementary.exp h
                  let f = Fuaran.Compute.Elementary.Elementary.exp -h
                  struct (0.5 * (e + f), 0.5 * (e - f)) }
      "a dilator by its root and the root's reciprocal",
      [ "DilatorParts" ],
      { shipped with
          DilatorParts =
              fun divide s ->
                  let root = sqrt s
                  let reciprocal = divide 1.0 root
                  struct (0.5 * (root + reciprocal), 0.5 * (root - reciprocal)) }
      "an interpolation that takes the shorter way",
      [ "Between" ],
      { shipped with
          Between =
              fun d ->
                  if Multivector.coefficient 0 d < 0.0 then
                      Multivector.negate d
                  else
                      d }
      "the vector split by the contraction",
      [ "SplitAlong" ],
      { shipped with
          SplitAlong = Forms.splitByContraction }
      "the vector split by two projections",
      [ "SplitAlong" ],
      { shipped with
          SplitAlong = Forms.splitByBoth }
      "the logarithm's vector by a projection",
      [ "MotorAcross" ],
      { shipped with
          MotorAcross = Forms.acrossByProjection }
      "a row-major matrix",
      [ "MatrixIndex" ],
      { shipped with
          MatrixIndex = fun row column -> 4 * row + column }
      "a fused multiply-add in a dot product",
      [ "Dot" ],
      { shipped with
          Dot = fun a b -> Array.fold2 (fun acc p q -> Math.FusedMultiplyAdd(p, q, acc)) 0.0 a b }
      "a dot product summed from the last",
      [ "Dot" ],
      { shipped with
          Dot = fun a b -> Forms.dot (Array.rev a) (Array.rev b) }
      "a fused cross product",
      [ "Orientation" ],
      { shipped with
          Orientation =
              fun dot (a: float[]) (b: float[]) c ->
                  dot
                      [| fusedMinor a[1] b[2] a[2] b[1]
                         fusedMinor a[2] b[0] a[0] b[2]
                         fusedMinor a[0] b[1] a[1] b[0] |]
                      c }
      "a rotor from the scalar's sum always",
      [ "RotorOfRotation" ],
      { shipped with
          RotorOfRotation = Forms.rotorByScalarSum }
      "no rule at a half turn",
      [ "Canonical" ],
      { shipped with
          Canonical = fun q -> if q[0] < 0.0 then q |> Array.map (fun v -> -v) else q }
      "the last in a tie at a half turn",
      [ "Canonical" ],
      { shipped with
          Canonical =
              fun q ->
                  let magnitude (v: float) = if v > -v then v else -v
                  let mutable largest = 1

                  for k in 2..3 do
                      if magnitude q[k] >= magnitude q[largest] then
                          largest <- k

                  if q[0] < 0.0 || (q[0] = 0.0 && q[largest] < 0.0) then
                      q |> Array.map (fun v -> -v)
                  else
                      q }
      "a direction divided by its norm a second time",
      [ "UnitAsGiven" ],
      { shipped with
          UnitAsGiven =
              fun (a, b, c) ->
                  let n = Norm.scaled [| a; b; c |]
                  a / n, b / n, c / n }
      "a screw's point moved to the point of its axis nearest the origin",
      [ "PointAsGiven" ],
      { shipped with
          PointAsGiven =
              fun (p1, p2, p3) (u1, u2, u3) ->
                  let along = (p1 * u1 + p2 * u2) + p3 * u3
                  p1 - along * u1, p2 - along * u2, p3 - along * u3 }
      "an exponential's parts summed with a zero",
      [ "Placed" ],
      { shipped with
          Placed = fun x -> 0.0 + x }
      "a normalisation applied twice",
      [ "NormaliseOnce" ],
      { shipped with
          NormaliseOnce = fun once values -> once values |> Result.bind once } ]

/// The member each port of Phase 728 changes, by the port's name in `ConformalTests.wrongPorts`.
let private objectPortMembers: (string * string list) list =
    let products = [ "Geometric"; "Outer"; "Contraction"; "Scalar" ]

    [ "another order of summation", products
      "a fused multiply-add", products
      "a fold seeded from its first term", products
      "a product with a reciprocal", [ "Divide" ]
      "a statistic divided twice", [ "OverBoth" ]
      "an unscaled norm", [ "Scaled" ]
      "a fused multiply-add in the norm's sum", [ "SumOfSquares" ]
      "a correctly rounded sum in the norm", [ "SumOfSquares" ]
      "the norm's squares summed in another order", [ "SumOfSquares" ]
      "a product of three factors formed from the right", [ "Chain" ]
      "half a squared length summed from the right", [ "HalfSquaredLength" ]
      "a radius halved before it is squared", [ "HalfSquare" ]
      "a power formed as zero less twice the product", [ "MinusTwice" ]
      "no test of a zero divisor", [ "IsZeroDivisor" ]
      "no test of a zero direction", [ "IsZeroNorm" ]
      "a threshold met from above", [ "Above" ]
      "a strict threshold", [ "Within" ]
      "a threshold never met", [ "Within" ]
      "a threshold always met", [ "Within" ]
      "a quotient formed at a threshold of zero", [ "SignsDecide" ] ]

/// Every wrong port held against the versors: Phase 728's twenty and this phase's.
let private ports: (string * string list * Arithmetic) list =
    [ for name, r in C.wrongPorts -> name, (objectPortMembers |> List.find (fun (n, _) -> n = name) |> snd), r ]
    @ versorPorts

/// The members of the arithmetic each computing function reads, by what `Motion` writes.
let private reads (op: string) : Set<string> =
    let norm = [ "Scaled"; "SumOfSquares" ]
    let unit = norm @ [ "IsZeroNorm"; "Divide"; "UnitAsGiven" ]
    let exp = norm @ [ "IsZeroAngle"; "SinCos"; "ByQuotient"; "Divide"; "Placed" ]
    let log = norm @ [ "IsZeroBivector"; "Atan2"; "ByQuotient"; "Divide" ]
    let motorExp = exp @ [ "SplitAlong"; "Geometric"; "Outer" ]
    let motorLog = log @ [ "MotorAcross"; "Geometric" ]
    let fn = op.Substring(op.IndexOf '.' + 1)

    set (
        if fn.StartsWith "applyTo" then
            if op.StartsWith "Reflection." then
                [ "Geometric"; "Chain" ]
            else
                [ "Geometric"; "Chain"; "VersorReverse" ]
        else
            match fn, op with
            | "normalise", _ -> [ "Scalar"; "Divide"; "IsZeroDivisor"; "NormaliseOnce" ]
            | "toMatrix", _ ->
                [ "Geometric"
                  "Chain"
                  "VersorReverse"
                  "Divide"
                  "IsZeroDivisor"
                  "MatrixIndex" ]
            | "compose", _ -> [ "Geometric"; "Compose" ]
            | _, "Rotor.ofAxisAngle" -> unit @ [ "SinCos" ]
            | _, "Rotor.exp" -> exp
            | _, "Rotor.log" -> log
            | _, "Rotor.interpolate" -> exp @ log @ [ "Geometric"; "VersorReverse"; "Between" ]
            | _, "Motor.ofRotorTranslator" -> [ "Geometric"; "Compose" ]
            | _, "Motor.ofScrew" -> unit @ [ "SinCos"; "Screw"; "Geometric"; "PointAsGiven" ]
            | _, "Motor.ofLine" ->
                norm
                @ [ "IsZeroDivisor"
                    "Divide"
                    "Geometric"
                    "Contraction"
                    "Scalar"
                    "SinCos"
                    "Screw"
                    "UnitAsGiven"
                    "PointAsGiven" ]
            | _, "Motor.exp" -> motorExp
            | _, "Motor.log" -> motorLog
            | _, "Motor.interpolate" -> motorExp @ motorLog @ [ "VersorReverse"; "Between" ]
            | _, "Motor.ofMatrix" ->
                norm
                @ [ "Above"
                    "Within"
                    "SignsDecide"
                    "MatrixIndex"
                    "Dot"
                    "IsZeroDivisor"
                    "Divide"
                    "OverBoth"
                    "Orientation"
                    "RotorOfRotation"
                    "Canonical"
                    "Compose"
                    "Geometric" ]
            | _, "Dilator.ofFactor" -> [ "DilatorParts"; "Divide" ]
            | _, "Dilator.ofCentreFactor" -> [ "DilatorParts"; "Divide"; "Chain"; "Geometric" ]
            | _, "Reflection.ofNormalDistance" -> unit
            | _, other -> failwithf "no function is named %s" other
    )

/// The committed vectors of each function an arithmetic answers otherwise.
let private redUnder (r: Arithmetic) : Map<string, string list> =
    versorVectors
    |> List.choose (fun c ->
        match answerUnder r c with
        | Some cell when C.cellText cell <> C.committed.Value[c.Id] -> Some(c.Op, c.Id)
        | _ -> None)
    |> List.groupBy fst
    |> List.map (fun (op, reds) -> op, reds |> List.map snd)
    |> Map.ofList

/// The thresholds a matrix's own statistics name: asked at zero it is refused with the first statistic
/// that is not zero, asked at that statistic's magnitude with the next, and so on. A wrong port that
/// moves a statistic moves the answer at exactly such a threshold.
let private thresholdsOf (c: V.Case) : float list =
    if c.Op <> "Motor.ofMatrix" then
        []
    else
        let matrix = V.floats c "matrix"

        let rec next (tau: float) (found: float list) : float list =
            if List.length found >= 5 then
                found
            else
                match Motion.motorOfMatrix shipped tau matrix with
                | Error(MatrixRefusal.NotOneLength q)
                | Error(MatrixRefusal.NotOrthogonal q) ->
                    let magnitude = if q < 0.0 then -q else q

                    if magnitude > tau && not (Double.IsInfinity magnitude) then
                        next magnitude (magnitude :: found)
                    else
                        found
                | _ -> found

        next 0.0 []

/// The drawn sample: `V.versorSearched 0 .. 449`, every function that computes over each, and a matrix
/// asked again at every threshold its statistics name. The first ninety are drawn versors, the even
/// ones with zeros of both signs among their coefficients; the next sixty are versors and objects every
/// coefficient of which is a zero of a drawn sign, or every one but one; and the last three hundred are versors
/// and objects a third of whose coefficients are such zeros and a third powers of two so small that a
/// product of two of them underflows to a zero of either sign. Each element is the number drawn at, the case
/// as drawn, and the case asked.
let private drawnSample: Lazy<(int * V.Case * V.Case)[]> =
    lazy
        ([| for index in 0..449 do
                for c in V.versorSearched index do
                    if not (copies.Contains c.Op) then
                        yield index, c, c

                        for tau in thresholdsOf c do
                            yield
                                index,
                                c,
                                { c with
                                    Input =
                                        c.Input
                                        |> List.map (fun (name, o) ->
                                            if name = "tau" then name, V.Number tau else name, o) } |])

let private drawnShipped: Lazy<string[]> =
    lazy
        (drawnSample.Value
         |> Array.Parallel.map (fun (_, _, c) ->
             match answerUnder shipped c with
             | Some cell -> C.cellText cell
             | None -> ""))

/// The functions an arithmetic answers otherwise somewhere in the drawn sample, each with the first
/// element where it does: the number drawn at, the case's id and the threshold asked.
let private changedUnder (r: Arithmetic) : Map<string, int * string * float option> =
    let shippedAnswers = drawnShipped.Value

    drawnSample.Value
    |> Array.Parallel.mapi (fun at (index, drawn, c) ->
        match answerUnder r c with
        | Some cell when C.cellText cell <> shippedAnswers[at] ->
            let tau =
                if c.Op = "Motor.ofMatrix" && not (obj.ReferenceEquals(drawn, c)) then
                    Some(V.number c "tau")
                else
                    None

            Some(c.Op, (index, drawn.Id, tau))
        | _ -> None)
    |> Array.choose id
    |> Array.fold
        (fun found (op, witness) ->
            if Map.containsKey op found then
                found
            else
                Map.add op witness found)
        Map.empty

/// Why a wrong port is UNABLE to change a function that reads the member it changes: the port, the
/// reason, and the functions. Held both ways by the case below. A function is listed here exactly when
/// it reads the member, no committed vector of it fails the port, and the port answers it as the shipped
/// arithmetic does on every element of the drawn sample.
let private unable: (string * string * string list) list =
    [ "an exponential's parts summed with a zero",
      "the exponential it forms enters one more product as a factor, and the sign of a zero factor cannot move a bit of a product: a fold starts at a positive zero and never holds a negative one, so a zero term of either sign leaves it as it is (D85 item 3)",
      [ "Rotor.interpolate"; "Motor.interpolate" ]
      "a fold seeded from its first term",
      "a fold from a negative zero answers otherwise only where every signed term of a coefficient is a negative zero, and the search finds no such coefficient: it asks each of these functions sixty times over versors and objects whose every coefficient is a zero of a drawn sign, or every one but one, three hundred times over operands a third of whose coefficients are such zeros and a third so small that their products underflow to a zero of either sign, and forty-five times more with zeros of both signs among a versor's coefficients. That is a search and no proof",
      [ "Rotor.applyToPoint"
        "Rotor.applyToPointPair"
        "Rotor.applyToLine"
        "Rotor.applyToCircle"
        "Rotor.applyToPlane"
        "Rotor.applyToSphere"
        "Rotor.compose"
        "Rotor.normalise"
        "Translator.applyToPoint"
        "Translator.applyToPointPair"
        "Translator.applyToLine"
        "Translator.applyToCircle"
        "Translator.applyToPlane"
        "Translator.applyToSphere"
        "Translator.compose"
        "Translator.normalise"
        "Motor.applyToPoint"
        "Motor.applyToPointPair"
        "Motor.applyToLine"
        "Motor.applyToCircle"
        "Motor.applyToPlane"
        "Motor.applyToSphere"
        "Motor.compose"
        "Motor.normalise"
        "Dilator.applyToPoint"
        "Dilator.applyToPointPair"
        "Dilator.applyToLine"
        "Dilator.applyToCircle"
        "Dilator.applyToPlane"
        "Dilator.applyToSphere"
        "Dilator.compose"
        "Dilator.normalise"
        "Reflection.applyToPoint"
        "Reflection.applyToPointPair"
        "Reflection.applyToLine"
        "Reflection.applyToCircle"
        "Reflection.applyToPlane"
        "Reflection.applyToSphere"
        "Reflection.compose"
        "Reflection.normalise"
        "Rotor.toMatrix"
        "Translator.toMatrix"
        "Motor.toMatrix"
        "Dilator.toMatrix"
        "Rotor.interpolate"
        "Motor.interpolate"
        "Motor.ofRotorTranslator"
        "Motor.ofScrew"
        "Motor.ofLine"
        "Motor.exp"
        "Motor.log"
        "Motor.ofMatrix"
        "Dilator.ofCentreFactor" ]
      "another order of summation",
      "its squared norm has one term that is not a zero, the square of its scalar part: every other term pairs a blade a translator holds with one its reverse holds as a zero, and a zero term leaves a sum as it is in any order",
      [ "Translator.normalise" ]
      "another order of summation",
      "its squared norm has two terms that are not zeros, the square of its scalar part and the negated square of its part on n0^ninf, among zero terms: two terms round alike in either order, a zero term moves neither, and a sum that is a zero is refused whatever its sign",
      [ "Dilator.normalise" ]
      "another order of summation",
      "each coefficient of the product of two translators has at most two terms that are not zeros, a scalar part times a part each way, among zero terms: two terms round alike in either order, and where both underflow the fold answers a positive zero in either order",
      [ "Translator.compose" ]
      "another order of summation",
      "the one product it reads has a left operand of one coefficient, so each coefficient it reads has one term that is not a zero among zero terms, and a fold from a positive zero answers one term and zeros alike in any order",
      [ "Motor.log" ]
      "another order of summation",
      "no argument is given that covers every coefficient. It is believed that each coefficient it reads of an image has one term that is not a zero, or two, because a translator's parts beside its scalar square to zero; that count was not computed, and the search, over 450 configurations, finds that the other order answers the same bits. That is a search and no proof",
      [ "Translator.toMatrix" ]
      "a fused multiply-add",
      "its squared norm has one term that is not a zero, a square, which is not negative: fused into the fold's positive zero it is that square rounded, the zero terms after it leave a positive sum as it is, and a sum that is a zero is refused whatever its sign",
      [ "Translator.normalise" ]
      "a fused multiply-add",
      "where no term underflows, each entry it reads is one term of its products, or two equal terms, which fused or not round to twice one. Where a term underflows, a fused product can leave a negative zero in a fold, and no argument is given that none survives to an entry: the search draws translators whose parts underflow in a product beside a weight that does not, and finds that the zero terms after it restore the positive zero every time. That half is a search and no proof",
      [ "Translator.toMatrix" ]
      "a product of three factors formed from the right",
      "no argument is given. On the four blades whose images it reads each entry is one term or two equal ones under the grouping shipped, and the search, over 450 configurations, finds that the other grouping answers the same bits. That is a search and no proof",
      [ "Translator.toMatrix" ]
      "the composition in the other order",
      "two translators commute, and each coefficient of their product has the same two terms that are not zeros in either order of the operands, a scalar part times a part each way, which one rounded sum adds alike",
      [ "Translator.compose" ] ]

/// The rules D85 states by name, each with a committed vector that is red under the one member that
/// names it: the vector, and the port.
let private named: (string * string) list =
    [ "Rotor.normalise-of-a-rotor-a-second-division-would-move", "a normalisation applied twice"
      "Translator.normalise-of-a-scalar-part-whose-square-is-subnormal", "a normalisation applied twice"
      "Motor.ofScrew-searched-1000", "a direction divided by its norm a second time"
      "Motor.ofLine-searched-of-a-weighted-line-1000", "a direction divided by its norm a second time"
      "Rotor.ofAxisAngle-general-2", "a direction divided by its norm a second time"
      "Reflection.ofNormalDistance-general-1", "a direction divided by its norm a second time"
      "Motor.ofScrew-general-1", "a screw's point moved to the point of its axis nearest the origin"
      "Motor.log-whose-one-product-underflows-to-a-negative-zero", "a fused multiply-add"
      "Rotor.exp-of-zeros-keeps-each-sign", "an exponential's parts summed with a zero"
      "Rotor.exp-of-a-bivector-with-a-zero-of-each-sign", "an exponential's parts summed with a zero"
      "Motor.exp-of-a-vector-alone-is-one-and-the-vector", "an exponential's parts summed with a zero"
      "Rotor.ofAxisAngle-at-a-sine-a-unit-from-correctly-rounded",
      "the correctly rounded sine and cosine where the pinned pair is a unit from them"
      "Rotor.ofAxisAngle-at-a-cosine-a-unit-from-correctly-rounded",
      "the correctly rounded sine and cosine where the pinned pair is a unit from them"
      "Rotor.exp-at-a-sine-a-unit-from-correctly-rounded",
      "the correctly rounded sine and cosine where the pinned pair is a unit from them"
      "Rotor.exp-at-a-cosine-a-unit-from-correctly-rounded",
      "the correctly rounded sine and cosine where the pinned pair is a unit from them"
      "Motor.ofScrew-at-a-sine-a-unit-from-correctly-rounded",
      "the correctly rounded sine and cosine where the pinned pair is a unit from them"
      "Motor.ofLine-at-a-sine-a-unit-from-correctly-rounded",
      "the correctly rounded sine and cosine where the pinned pair is a unit from them"
      "Motor.exp-at-a-sine-a-unit-from-correctly-rounded",
      "the correctly rounded sine and cosine where the pinned pair is a unit from them"
      "Rotor.interpolate-at-a-sine-a-unit-from-correctly-rounded",
      "the correctly rounded sine and cosine where the pinned pair is a unit from them"
      "Motor.interpolate-at-a-sine-a-unit-from-correctly-rounded",
      "the correctly rounded sine and cosine where the pinned pair is a unit from them"
      "Rotor.log-at-an-arc-tangent-a-unit-from-correctly-rounded",
      "the correctly rounded arc tangent where the pinned one is a unit from it"
      "Motor.log-at-an-arc-tangent-a-unit-from-correctly-rounded",
      "the correctly rounded arc tangent where the pinned one is a unit from it"
      "Rotor.interpolate-at-an-arc-tangent-a-unit-from-correctly-rounded",
      "the correctly rounded arc tangent where the pinned one is a unit from it"
      "Motor.interpolate-at-an-arc-tangent-a-unit-from-correctly-rounded",
      "the correctly rounded arc tangent where the pinned one is a unit from it"
      "Rotor.log-general-1", "the angle through an arc cosine"
      "Rotor.applyToPoint-of-a-rotor-twice-a-unit-scales-the-weight-by-four", "the inverse in place of the reverse"
      "Translator.applyToPoint-of-a-translator-that-is-no-unit", "the inverse in place of the reverse"
      "Motor.applyToLine-general-1", "the inverse in place of the reverse"
      "Rotor.applyToPoint-general-1", "a product of three factors formed from the right"
      "Reflection.applyToPoint-general-1", "a product of three factors formed from the right"
      "Dilator.ofCentreFactor-general-1", "a product of three factors formed from the right"
      "Rotor.compose-applies-the-second-operand-first", "the composition in the other order"
      "Motor.compose-of-a-translator-and-a-rotor-is-the-rotation-first", "the composition in the other order"
      "Motor.ofRotorTranslator-is-the-rotation-and-then-the-translation", "the composition in the other order"
      "Reflection.compose-of-x1-then-x2-is-minus-one-at-index-10", "the composition in the other order"
      "Motor.ofScrew-general-1", "a screw formed left to right"
      "Rotor.exp-general-1", "a quotient formed for each coefficient"
      "Rotor.log-general-1", "a quotient formed for each coefficient"
      "Rotor.exp-of-zero-is-one", "no test of a zero angle"
      "Motor.exp-of-a-vector-alone-is-one-and-the-vector", "no test of a zero angle"
      "Rotor.log-of-one-is-zero", "no test of a zero bivector part"
      "Rotor.log-of-minus-one-has-no-plane", "no test of a zero bivector part"
      "Motor.log-of-a-translator-is-its-vector-part-itself", "no test of a zero bivector part"
      "Dilator.ofFactor-by-three", "a dilator through the pinned exponential and logarithm"
      "Dilator.ofFactor-by-three", "a dilator by its root and the root's reciprocal"
      "Rotor.interpolate-goes-the-long-way-round", "an interpolation that takes the shorter way"
      "Motor.interpolate-goes-the-long-way-round", "an interpolation that takes the shorter way"
      "Motor.exp-general-1", "the vector split by the contraction"
      "Motor.exp-general-1", "the vector split by two projections"
      "Motor.log-general-1", "the logarithm's vector by a projection"
      "Rotor.toMatrix-about-e3-is-column-major", "a row-major matrix"
      "Translator.toMatrix-holds-the-vector-in-its-last-column", "a row-major matrix"
      "Motor.ofMatrix-of-a-quarter-turn-about-e3", "a row-major matrix"
      "Motor.ofMatrix-general-of-a-stretched-1", "a fused multiply-add in a dot product"
      "Motor.ofMatrix-general-of-a-stretched-2", "a dot product summed from the last"
      "Motor.ofMatrix-general-of-a-mirrored-1", "a fused cross product"
      "Motor.ofMatrix-of-a-half-turn-about-e3", "a rotor from the scalar's sum always"
      "Motor.ofMatrix-of-a-half-turn-whose-largest-component-comes-out-negative", "no rule at a half turn"
      "Motor.ofMatrix-of-a-half-turn-whose-two-largest-components-tie", "the last in a tie at a half turn"
      "Motor.ofMatrix-of-a-positive-cosine-that-underflows-at-a-threshold-of-zero",
      "a quotient formed at a threshold of zero"
      "Motor.ofMatrix-of-a-negative-cosine-that-underflows-at-a-threshold-of-zero",
      "a quotient formed at a threshold of zero"
      "Motor.ofMatrix-of-a-slanted-column-at-its-own-threshold", "a strict threshold"
      "Motor.ofMatrix-of-a-longer-column-at-its-own-threshold", "a strict threshold"
      "Motor.ofMatrix-of-a-shorter-column-at-its-own-threshold", "a strict threshold"
      "Motor.ofMatrix-of-a-slanted-column-at-its-own-threshold", "a threshold met from above"
      "Motor.ofMatrix-of-a-slanted-column-within-a-wide-threshold", "a threshold never met"
      "Motor.ofMatrix-of-a-shorter-column-below-its-threshold", "a threshold always met"
      "Rotor.normalise-of-the-zero-rotor", "no test of a zero divisor"
      "Rotor.toMatrix-of-the-zero-rotor", "no test of a zero divisor"
      "Motor.ofMatrix-of-a-zero-column", "no test of a zero divisor"
      "Motor.ofLine-of-the-zero-line", "no test of a zero divisor"
      "Rotor.ofAxisAngle-of-a-zero-axis", "no test of a zero direction"
      "Motor.ofScrew-of-a-zero-direction", "no test of a zero direction"
      "Reflection.ofNormalDistance-of-a-zero-normal", "no test of a zero direction"
      "Rotor.ofAxisAngle-of-the-least-axis", "an unscaled norm"
      "Rotor.applyToPoint-general-1", "a fused multiply-add"
      "Motor.compose-general-1", "another order of summation"
      "Rotor.normalise-general-1", "a product with a reciprocal"
      "Rotor.toMatrix-general-1", "a product with a reciprocal"
      "Motor.ofMatrix-searched-of-a-slanted-1007", "a statistic divided twice" ]

/// A sample of the agreement of a versor with its matrix on points, 2,000 draws from `seed`: a rotor,
/// a translator and a dilator about a centre, each applied to a point through the sandwich and
/// through its sixteen doubles as a `Matrix` of `Fuaran.Compute.LinearAlgebra` times the column
/// `(x1, x2, x3, 1)`. Answers the largest error of a coordinate by the matrix against the same
/// coordinate by the versor, in units in the last place of the versor's, and the largest absolute
/// error where the versor's coordinate is zero, in units of `2^-52`.
let private matrixAgreementAt (seed: int) : float * float =
    let mutable state = float seed

    let next () =
        state <- (state * 48271.0) % 2147483647.0
        state

    let within1 () = next () / 1073741824.0 - 1.0

    let triple (scale: float) =
        let a = scale * within1 ()
        let b = scale * within1 ()
        let c = scale * within1 ()
        a, b, c

    let lastPlace (x: float) : float =
        let magnitude = if x < 0.0 then -x else x
        let mutable power = 1.0

        while power > magnitude do
            power <- power / 2.0

        while power * 2.0 <= magnitude do
            power <- power * 2.0

        power / 4503599627370496.0

    let ok (r: Result<'a, GeometryRefusal>) : 'a =
        match r with
        | Ok x -> x
        | Error refusal -> failtestf "a drawn operand was refused: %A" refusal

    let mutable worstUlps = 0.0
    let mutable worstAbsolute = 0.0

    let compare (byVersor: Point) (matrix: float[]) (x1: float, x2: float, x3: float) : unit =
        let m = Fuaran.Compute.LinearAlgebra.Matrix.init 4 4 (fun i j -> matrix[4 * j + i])

        Expect.equal
            (Array.map bitsOf (Fuaran.Compute.LinearAlgebra.Matrix.toColumnMajor m))
            (Array.map bitsOf matrix)
            "the sixteen doubles are the matrix's own column-major layout"

        let byMatrix =
            match Fuaran.Compute.LinearAlgebra.Blas.gemv m (Vector.ofList [ x1; x2; x3; 1.0 ]) with
            | Ok image -> Vector.toList image
            | Error e -> failtestf "the matrix route was refused: %A" e

        let (v1, v2, v3) = ok (Point.coordinates byVersor)

        for got, reference in List.zip (List.take 3 byMatrix) [ v1; v2; v3 ] do
            let error = if got < reference then reference - got else got - reference

            if reference = 0.0 then
                (if error > worstAbsolute then
                     worstAbsolute <- error)
            else
                let ulps = error / lastPlace reference

                if ulps > worstUlps then
                    worstUlps <- ulps

    for _ in 1..2000 do
        let x = triple 8.0
        let p = ok (Point.up x)
        let rotor = ok (Rotor.ofAxisAngle (triple 1.0) (8.0 * within1 ()))
        compare (Rotor.applyToPoint rotor p) (ok (Rotor.toMatrix rotor)) x
        let translator = ok (Translator.ofVector (triple 8.0))
        compare (Translator.applyToPoint translator p) (ok (Translator.toMatrix translator)) x
        let centre = triple 8.0

        let dilator =
            ok (Dilator.ofCentreFactor centre (1.0 + 7.0 * (0.5 + 0.5 * within1 ())))

        compare (Dilator.applyToPoint dilator p) (ok (Dilator.toMatrix dilator)) x

    worstUlps, worstAbsolute * 4503599627370496.0

/// The seeds every measured figure is given at: the stated sample's, and eight others.
let private seeds = [ 729; 1; 2; 3; 590; 727; 728; 1000; 4242 ]

/// What each bounded law of the versors measures at each seed of `seeds`, in that order: the largest
/// error in units in the last place, and the largest absolute error at a zero reference in units of
/// `2^-52`. The first row of each is the stated sample's, and is the law's bound (DECISIONS.md D88).
let private measured: (string * (float * float) list) list =
    [ "a rotor times its reverse is one, within the stated bound",
      [ 2.5, 0.25
        3.0, 0.25
        3.0, 0.25
        2.5, 0.25
        2.0, 0.25
        2.5, 0.25
        2.5, 0.25
        2.5, 0.25
        2.5, 0.25 ]
      "the logarithm of a rotor's exponential is its bivector, within the stated bound",
      [ 3.0, 0.0
        3.0, 0.0
        3.0, 0.0
        3.0, 0.0
        3.0, 0.0
        3.0, 0.0
        3.0, 0.0
        3.0, 0.0
        3.0, 0.0 ]
      "the logarithm of a motor's exponential is its bivector and its vector, within the stated bound",
      [ 512.0, 0.0
        256.0, 0.0
        4096.0, 0.0
        4096.0, 0.0
        512.0, 0.0
        1024.0, 0.0
        256.0, 0.0
        1024.0, 0.0
        512.0, 0.0 ]
      "a rotor's interpolation returns its ends at 0 and at 1, within the stated bound",
      [ 39755.0, 0.0
        52884.0, 0.0
        16782.0, 0.0
        17132.0, 0.0
        17650.0, 0.0
        338947.0, 0.0
        29901.0, 0.0
        22046.0, 0.0
        10014.0, 0.0 ]
      "a motor's interpolation returns its ends at 0 and at 1, within the stated bound",
      [ 49763.0, 0.0
        900352.0, 0.0
        278847.0, 0.0
        25831.0, 0.0
        1916928.0, 0.0
        62672.0, 0.0
        194923.0, 0.0
        51828.0, 0.0
        65536.0, 0.0 ] ]

/// The agreement of a versor with its matrix on points, at each seed of `seeds`.
let private agreement: (float * float) list =
    [ 1534.0, 0.0
      5213.0, 0.0
      1758.0, 0.0
      5101.0, 0.0
      18433.0, 0.0
      6375.0, 0.0
      40052.0, 0.0
      75202.0, 0.0
      6783.0, 0.0 ]

[<Tests>]
let tests =
    testList
        "Versors"
        [

          testCase "the versors' vectors are the shipped arithmetic's answers"
          <| fun () ->
              Expect.equal (List.length versorVectors) 533 "the versors' vectors"

              for c in versorVectors do
                  match answerUnder shipped c with
                  | Some cell ->
                      Expect.equal (C.cellText cell) C.committed.Value[c.Id] (c.Id + " under the shipped arithmetic")
                  | None -> Expect.isTrue (copies.Contains c.Op) (c.Id + " is a copy")

              let published =
                  GeometricAlgebraOperations.exactness
                  |> List.map fst
                  |> List.filter V.isVersorOp
                  |> Set.ofList

              let vectored = versorVectors |> List.map (fun c -> c.Op) |> Set.ofList
              Expect.equal (Set.count published) 71 "seventy-one functions are published"
              Expect.equal vectored published "every published function of the versors has a vector"
              Expect.equal (Set.count (Set.difference published copies)) 58 "fifty-eight of them compute"

          testCase "each wrong port fails a vector of a versor's function, or a search finds it unable to change it"
          <| fun () ->
              let computing =
                  Set.difference (versorVectors |> List.map (fun c -> c.Op) |> Set.ofList) copies

              Expect.equal (List.length ports) 49 "forty-nine wrong ports"

              Expect.equal
                  drawnSample.Value.Length
                  32001
                  "the drawn sample: every computing function over four hundred and fifty configurations, and each matrix at its own thresholds"

              let listed =
                  [ for port, reason, functions in unable do
                        for f in functions -> (port, f), reason ]

              Expect.equal
                  (listed |> List.map fst |> List.distinct |> List.length)
                  (List.length listed)
                  "no pair is listed twice"

              let listedMap = Map.ofList listed
              let mutable red = 0
              let mutable notRead = 0
              let mutable listedUnable = 0

              let findings =
                  [ for port, members, r in ports do
                        let reds = redUnder r
                        let changed = changedUnder r

                        for f in computing do
                            let readsIt = members |> List.exists (fun m -> Set.contains m (reads f))
                            let isListed = Map.containsKey (port, f) listedMap

                            match Map.tryFind f reds, readsIt, Map.tryFind f changed with
                            | Some _, true, _ ->
                                red <- red + 1

                                if isListed then
                                    yield sprintf "%s | %s: listed unable, and a committed vector is red" port f
                            | Some ids, false, _ ->
                                yield
                                    sprintf "%s | %s: said not to read the member, and %s is red" port f (List.head ids)
                            | None, false, Some(index, id, tau) ->
                                yield
                                    sprintf
                                        "%s | %s: said not to read the member, and drawn case %d %s %A differs"
                                        port
                                        f
                                        index
                                        id
                                        tau
                            | None, false, None ->
                                notRead <- notRead + 1

                                if isListed then
                                    yield sprintf "%s | %s: listed, and it does not read the member" port f
                            | None, true, Some(index, id, tau) ->
                                yield
                                    sprintf
                                        "%s | %s: no red vector, and drawn case %d %s %A differs: commit it"
                                        port
                                        f
                                        index
                                        id
                                        tau
                            | None, true, None ->
                                listedUnable <- listedUnable + 1

                                if not isListed then
                                    yield
                                        sprintf "%s | %s: reads the member, has no red vector, and is not listed" port f ]

              if not (List.isEmpty findings) then
                  failtestf
                      "%d pairs of a port and a function are neither red nor unable for a stated reason:\n%s"
                      (List.length findings)
                      (String.Join("\n", findings))

              Expect.equal (red + notRead + listedUnable) (49 * 58) "every pair was read"

              // The search is not blind to an underflow. A motor's logarithm reads one product each of
              // whose coefficients is one term, so a fused product can move it only where that term
              // underflows to a negative zero; and among the last three hundred configurations it does.
              let _, _, fusedPort =
                  ports |> List.find (fun (name, _, _) -> name = "a fused multiply-add")

              let underflowing =
                  drawnSample.Value
                  |> Array.mapi (fun at (index, _, c) -> at, index, c)
                  |> Array.filter (fun (at, index, c) ->
                      index >= 150
                      && c.Op = "Motor.log"
                      && (match answerUnder fusedPort c with
                          | Some cell -> C.cellText cell <> drawnShipped.Value[at]
                          | None -> false))

              Expect.isNonEmpty
                  underflowing
                  "a drawn logarithm of a motor moves under a fused product where its one term underflows"

              Expect.equal
                  (red, notRead, listedUnable)
                  (340, 2438, 64)
                  "red, unable for not reading the member, unable for a listed reason"

          testCase "the objects read one member this phase added, and each normalise is held to it"
          <| fun () ->
              // Of the members Phase 729 added, the functions of Phase 728 read one: a normalisation
              // divides once (DECISIONS.md D85 item 5). Under every other port of the versors no
              // committed vector of an object's function moves, and no element of that phase's drawn
              // sample. Under the port that normalises twice, only the six `normalise` can move, and
              // each of them is red on a committed vector or is immune for the reason stated, which
              // the drawn sample holds.
              let twice = "a normalisation applied twice"

              // A point's weight and a sphere's are each one of the coefficients divided, so the
              // first division leaves that coefficient exactly one (a finite double that is not zero,
              // over itself, is one), and a second division, by one, is exact at every coefficient.
              let immune = set [ "Point.normalise"; "Sphere.normalise" ]

              let dividedByTheirNorm =
                  set
                      [ "PointPair.normalise"
                        "Line.normalise"
                        "Circle.normalise"
                        "Plane.normalise" ]

              // The two vectors this phase appends are the shipped arithmetic's answers.
              for c in V.objectCasesAppended do
                  Expect.equal
                      (C.answerUnder shipped c |> Option.map C.cellText)
                      (Some C.committed.Value[c.Id])
                      (c.Id + " under the shipped arithmetic")

              for port, _, r in versorPorts do
                  let reds =
                      C.conformal @ V.objectCasesAppended
                      |> List.filter (fun c ->
                          match C.answerUnder r c with
                          | Some cell -> C.cellText cell <> C.committed.Value[c.Id]
                          | None -> false)
                      |> List.map (fun c -> c.Op)
                      |> Set.ofList

                  let changed = C.changedUnder r |> Map.toList |> List.map fst |> Set.ofList

                  if port = twice then
                      Expect.equal
                          reds
                          dividedByTheirNorm
                          "each normalise that divides by a norm is red on a committed vector"

                      Expect.isTrue
                          (Set.isSubset changed dividedByTheirNorm)
                          "no other function of the objects moves in the drawn sample"

                      Expect.isEmpty
                          (Set.intersect immune (Set.union reds changed))
                          "a point's and a sphere's normalise are immune: their second divisor is exactly one"
                  else
                      Expect.isEmpty reds (port + " moves no vector of the objects")
                      Expect.isEmpty changed (port + " moves no drawn case of the objects")

          testCase "each rule the record names has a vector that is red under the one member that names it"
          <| fun () ->
              Expect.isGreaterThan (List.length named) 40 "the named rules"

              let green =
                  [ for id, port in named do
                        let _, _, r = ports |> List.find (fun (name, _, _) -> name = port)

                        match answerUnder r (V.find id) with
                        | Some cell when C.cellText cell <> C.committed.Value[id] -> ()
                        | _ -> yield sprintf "%s is not red under: %s" id port ]

              Expect.isEmpty green (sprintf "each named vector is red under its port: %A" green)

          testCase "a normalisation nobody asked for answers another bit"
          <| fun () ->
              // DECISIONS.md D85 item 5: nothing normalises. A port that normalises a versor before it
              // applies it, or a product after it composes it, fails a committed vector: the versors
              // these vectors take are no units.
              let differs (id: string) (cell: CorpusCell) =
                  Expect.notEqual
                      (C.cellText cell)
                      C.committed.Value[id]
                      (id + " is red where a versor is normalised unasked")

              let ok (r: Result<'a, GeometryRefusal>) : 'a =
                  match r with
                  | Ok x -> x
                  | Error refusal -> failtestf "a normalisation was refused: %A" refusal

              for k in 1..3 do
                  let applied = V.find (sprintf "Rotor.applyToPoint-general-%d" k)
                  let rotor = ok (Rotor.normalise (V.rotorOf (V.floats applied "v")))
                  differs applied.Id (V.pointCell (Rotor.applyToPoint rotor (V.pointOf (V.floats applied "x"))))
                  let moved = V.find (sprintf "Motor.applyToLine-general-%d" k)
                  let motor = ok (Motor.normalise (V.motorOf (V.floats moved "v")))
                  differs moved.Id (V.lineCell (Motor.applyToLine motor (V.lineOf (V.floats moved "x"))))
                  let composed = V.find (sprintf "Rotor.compose-general-%d" k)

                  differs
                      composed.Id
                      (V.rotorCell (
                          ok (
                              Rotor.normalise (
                                  Rotor.compose (V.rotorOf (V.floats composed "b")) (V.rotorOf (V.floats composed "a"))
                              )
                          )
                      ))

                  let scaled = V.find (sprintf "Dilator.applyToSphere-general-%d" k)
                  let dilator = ok (Dilator.normalise (V.dilatorOf (V.floats scaled "v")))
                  differs scaled.Id (V.sphereCell (Dilator.applyToSphere dilator (V.sphereOf (V.floats scaled "x"))))

              // And the one worked by hand: a rotor twice a unit scales a point's weight by four.
              let doubled =
                  V.find "Rotor.applyToPoint-of-a-rotor-twice-a-unit-scales-the-weight-by-four"

              let image =
                  Rotor.applyToPoint (V.rotorOf (V.floats doubled "v")) (V.pointOf (V.floats doubled "x"))

              Expect.equal (bitsOf image.N0) (bitsOf 4.0) "the weight of the image is four, and nothing repaired it"

          testCase "an exponential places its coefficients, and the summed form fails each of three properties"
          <| fun () ->
              // DECISIONS.md D88 item 3: the scalar and the bivector part of an exponential lie on
              // different blades, so a coefficient is placed and a zero keeps its sign. Three things
              // follow, held here over a drawn sample with zeros of both signs, and each fails where
              // the parts are summed with a zero: the shipped function with the one member changed.
              let _, _, summed =
                  versorPorts
                  |> List.find (fun (name, _, _) -> name = "an exponential's parts summed with a zero")

              let mutable state = 729.0

              let next () =
                  state <- (state * 48271.0) % 2147483647.0
                  state

              let drawn () : float =
                  match int (next () % 4.0) with
                  | 0 -> 0.0
                  | 1 -> -0.0
                  | _ -> next () / 1073741824.0 - 1.0

              let sample =
                  [ for _ in 1..500 -> (drawn (), drawn (), drawn ()), (drawn (), drawn (), drawn ()) ]

              let same (xs: float list) (ys: float list) = List.map bitsOf xs = List.map bitsOf ys

              // (b) `exp (−B)` is the reverse of `exp B`, bit for bit.
              let negatedIsTheReverse (r: Arithmetic) ((b12, b13, b23), _) : bool =
                  match Motion.rotorExp r (b12, b13, b23), Motion.rotorExp r (-b12, -b13, -b23) with
                  | Ok e, Ok n -> same [ n.Scalar; n.E1E2; n.E1E3; n.E2E3 ] [ e.Scalar; -e.E1E2; -e.E1E3; -e.E2E3 ]
                  | _ -> false

              // (c) the logarithm of the exponential of a vector alone is the vector, bit for bit.
              let vectorComesBack (r: Arithmetic) (_, (v1, v2, v3)) : bool =
                  match
                      Motion.motorExp r (0.0, 0.0, 0.0) (v1, v2, v3)
                      |> Result.bind (Motion.motorLog r)
                  with
                  | Ok(_, (w1, w2, w3)) -> same [ w1; w2; w3 ] [ v1; v2; v3 ]
                  | _ -> false

              // (d) about a coordinate axis, the exponential of `−½θ â I₃` has the zeros, signs included,
              // of the rotor built from the axis and the angle.
              let axes =
                  [ 1.0, 0.0, 0.0
                    0.0, 1.0, 0.0
                    0.0, 0.0, 1.0
                    -1.0, 0.0, 0.0
                    0.0, -1.0, 0.0
                    0.0, 0.0, -1.0 ]

              let zerosAgree (r: Arithmetic) ((a1, a2, a3): float * float * float) (theta: float) : bool =
                  let h = 0.5 * theta

                  match
                      Motion.rotorOfAxisAngle r (a1, a2, a3) theta, Motion.rotorExp r (-(h * a3), h * a2, -(h * a1))
                  with
                  | Ok built, Ok e ->
                      List.zip [ built.E1E2; built.E1E3; built.E2E3 ] [ e.E1E2; e.E1E3; e.E2E3 ]
                      |> List.forall (fun (x, y) -> x <> 0.0 || bitsOf x = bitsOf y)
                  | _ -> false

              let angles = [ for _ in 1..50 -> 8.0 * (next () / 1073741824.0 - 1.0) ]

              let allAxes (r: Arithmetic) =
                  axes |> List.forall (fun axis -> angles |> List.forall (zerosAgree r axis))

              Expect.isTrue
                  (sample |> List.forall (negatedIsTheReverse shipped))
                  "exp (−B) is the reverse of exp B, bit for bit"

              Expect.isTrue (sample |> List.forall (vectorComesBack shipped)) "log (exp (v ∧ n∞)) is v, bit for bit"
              Expect.isTrue (allAxes shipped) "the zeros of an axis-aligned exponential are the constructor's"
              Expect.isFalse (sample |> List.forall (negatedIsTheReverse summed)) "summed, exp (−B) is not the reverse"

              Expect.isFalse
                  (sample |> List.forall (vectorComesBack summed))
                  "summed, a negative zero of v does not come back"

              Expect.isFalse
                  (allAxes summed)
                  "summed, an axis-aligned exponential loses the constructor's negative zeros"

              // What revisiting the reading would cost: every committed vector the summed form fails.
              Expect.equal
                  (redUnder summed |> Map.toList |> List.collect snd |> List.sort)
                  [ "Motor.exp-of-a-vector-alone-is-one-and-the-vector"
                    "Rotor.exp-at-a-cosine-a-unit-from-correctly-rounded"
                    "Rotor.exp-of-a-bivector-with-a-zero-of-each-sign"
                    "Rotor.exp-of-that-bivector-negated-is-the-reverse"
                    "Rotor.exp-of-zeros-keeps-each-sign" ]
                  "the vectors that pin the placed exponential"

              // And the committed pair: one bivector and its negative, reverses of each other.
              let cellOf (id: string) = C.committed.Value[id]
              let h = ElementaryVectors.hex

              match Rotor.exp (0.5, -0.0, 0.0) with
              | Ok e ->
                  let list (xs: float list) =
                      "[" + String.Join(", ", xs |> List.map (fun x -> "\"" + h x + "\"")) + "]"

                  Expect.equal
                      (cellOf "Rotor.exp-of-a-bivector-with-a-zero-of-each-sign")
                      (list [ e.Scalar; e.E1E2; e.E1E3; e.E2E3 ])
                      "the exponential"

                  Expect.equal
                      (cellOf "Rotor.exp-of-that-bivector-negated-is-the-reverse")
                      (list [ e.Scalar; -e.E1E2; -e.E1E3; -e.E2E3 ])
                      "its negative's exponential is its reverse"

                  Expect.equal (bitsOf e.E1E3, bitsOf e.E2E3) (bitsOf -0.0, bitsOf 0.0) "each zero keeps its sign"
              | Error refusal -> failtestf "refused: %A" refusal

          testCase "the vectors on exact inputs answer what was worked by hand"
          <| fun () ->
              let has (id: string) (expected: string) =
                  Expect.equal C.committed.Value[id] expected (id + " answers what was worked by hand")

              let h = ElementaryVectors.hex

              let list (xs: float list) =
                  "[" + String.Join(", ", xs |> List.map (fun x -> "\"" + h x + "\"")) + "]"

              has "Translator.compose-with-its-opposite-is-the-identity" (list [ 1.0; 0.0; 0.0; 0.0 ])
              has "Translator.compose-adds-the-two-vectors" (list [ 1.0; -0.75; 1.0; -5.5 ])
              has "Translator.ofVector-halves-and-negates" (list [ 1.0; -0.5; 1.0; -0.25 ])

              has
                  "Reflection.compose-of-x1-then-x2-is-minus-one-at-index-10"
                  (list [ 0.0; -1.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0 ])

              has
                  "Reflection.compose-of-x2-then-x3-is-minus-one-at-index-13"
                  (list [ 0.0; 0.0; 0.0; 0.0; -1.0; 0.0; 0.0; 0.0 ])

              has
                  "Reflection.compose-of-x3-then-x1-is-one-at-index-11"
                  (list [ 0.0; 0.0; 1.0; 0.0; 0.0; 0.0; 0.0; 0.0 ])

              has "Dilator.ofFactor-by-four-is-exact" (list [ 1.25; 0.75; 0.0; 0.0; 0.0 ])
              has "Dilator.ofFactor-by-a-quarter-is-exact" (list [ 1.25; -0.75; 0.0; 0.0; 0.0 ])
              has "Dilator.ofFactor-by-one-is-the-identity" (list [ 1.0; 0.0; 0.0; 0.0; 0.0 ])
              has "Dilator.ofFactor-by-sixteen-is-exact" (list [ 2.125; 1.875; 0.0; 0.0; 0.0 ])
              // `(2, 1, 0)` about `(1, 1, 1)` by four is `(5, 1, −3)`, of weight a quarter.
              has "Dilator.applyToPoint-by-four-divides-the-weight-by-four" (list [ 0.25; 1.25; 0.25; -0.75; 4.375 ])
              has "Rotor.exp-of-zero-is-one" (list [ 1.0; 0.0; 0.0; 0.0 ])
              has "Rotor.exp-of-zeros-keeps-each-sign" (list [ 1.0; -0.0; 0.0; -0.0 ])
              has "Rotor.log-of-one-is-zero" (list [ 0.0; 0.0; 0.0 ])
              has "Rotor.log-of-minus-one-has-no-plane" "{ \"refused\": \"NoRotationPlane\" }"
              has "Rotor.log-of-the-zero-rotor-has-no-plane" "{ \"refused\": \"NoRotationPlane\" }"
              has "Rotor.normalise-three-four-five" (list [ 0.6; 0.0; 0.8; 0.0 ])
              has "Dilator.normalise-five-four-three" (list [ 5.0 / 3.0; 4.0 / 3.0; 1.0 / 3.0; 2.0 / 3.0; 1.0 ])
              has "Dilator.normalise-of-a-negative-squared-norm" "{ \"refused\": \"NegativeSquaredNorm\" }"

              has
                  "Motor.ofMatrix-of-the-identity"
                  ("{ \"motor\": "
                   + list [ 1.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0 ]
                   + ", \"factor\": \""
                   + h 1.0
                   + "\" }")

              has
                  "Motor.ofMatrix-of-a-half-turn-about-e3"
                  ("{ \"motor\": "
                   + list [ 0.0; -1.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0 ]
                   + ", \"factor\": \""
                   + h 1.0
                   + "\" }")

              has
                  "Motor.ofMatrix-of-a-mirror"
                  ("{ \"refused\": \"ReversesOrientation\", \"measure\": \"" + h -1.0 + "\" }")

              has "Motor.ofMatrix-of-a-shear" ("{ \"refused\": \"NotOneLength\", \"measure\": \"" + h 1.0 + "\" }")
              has "Motor.ofMatrix-of-a-last-row-that-is-not-affine" "{ \"refused\": \"NotAffine\" }"

              has
                  "Motor.ofMatrix-of-a-positive-cosine-that-underflows-at-a-threshold-of-zero"
                  ("{ \"refused\": \"NotOrthogonal\", \"measure\": \"" + h 0.0 + "\" }")

              has
                  "Motor.ofMatrix-of-a-negative-cosine-that-underflows-at-a-threshold-of-zero"
                  ("{ \"refused\": \"NotOrthogonal\", \"measure\": \"" + h -0.0 + "\" }")

              // At a threshold above zero the quotient is the statistic, and a zero is within it.
              for sign in [ "positive"; "negative" ] do
                  Expect.stringStarts
                      C.committed.Value[sprintf
                                            "Motor.ofMatrix-of-a-%s-cosine-that-underflows-at-the-least-threshold"
                                            sign]
                      "{ \"motor\": "
                      "at the least threshold the underflowed cosine is within it"

          testCase "each bounded law's bound is its stated sample's measurement, with eight other samples beside it"
          <| fun () ->
              Expect.equal (List.head seeds) GeometricAlgebraLaws.versorSampleSeed "the stated sample is the first seed"
              Expect.equal (List.length GeometricAlgebraLaws.versorBounds) 5 "five bounded laws"

              let rows =
                  [ for name, sample, boundUlps, boundAbsolute in GeometricAlgebraLaws.versorBounds ->
                        name,
                        [ for seed in seeds ->
                              match sample seed with
                              | Ok(ulps, absolute) -> ulps, absolute * 4503599627370496.0
                              | Error why -> failtestf "%s at seed %d: %s" name seed why ],
                        (boundUlps, boundAbsolute * 4503599627370496.0) ]

              Expect.equal
                  (rows |> List.map (fun (name, figures, _) -> name, figures))
                  measured
                  (sprintf
                      "what each law measures at each seed: %A"
                      (rows |> List.map (fun (name, figures, _) -> name, figures)))

              for name, figures, bound in rows do
                  Expect.equal
                      bound
                      (List.head figures)
                      (name + ": the bound is the stated sample's measurement, with no margin")

          testCase "every law of the family holds at eight other seeds"
          <| fun () ->
              // A bounded law draws nothing from the seed it is run at, so no law goes red at another.
              for seed in seeds do
                  let failures =
                      GeometricAlgebraLaws.laws seed 200
                      |> List.choose (fun (name, verdict) ->
                          match verdict with
                          | Ok() -> None
                          | Error why -> Some(sprintf "seed %d, %s: %s" seed name why))

                  Expect.isEmpty failures "every law holds"

          testCase "a versor and its matrix agree on points, within the stated bound"
          <| fun () ->
              let figures = seeds |> List.map matrixAgreementAt
              Expect.equal figures agreement (sprintf "the agreement at each seed: %A" figures)

          testCase "the package calls the pinned sine, cosine and arc tangent, and no other function of Elementary"
          <| fun () ->
              // DECISIONS.md D85 item 7: `Elementary.exp`, `ln` and `pow` are called nowhere. The dilator
              // is a root and two divisions.
              let calls =
                  [ for file in C.operationModules do
                        let code =
                            C.codeOf (
                                File.ReadAllText(
                                    Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.GeometricAlgebra", file))
                                )
                            )

                        for m in Text.RegularExpressions.Regex.Matches(code, @"Elementary\.(\w+)\b(?!\.)") ->
                            file, m.Groups[1].Value ]

              Expect.equal
                  calls
                  [ "Conformal.fs", "sinCos"; "Conformal.fs", "atan2" ]
                  "the pinned pair and the pinned arc tangent, each named once, in the shipped arithmetic"

          testCase "the claims ladder's row names this family's cases, word for word"
          <| fun () ->
              use ladder =
                  Text.Json.JsonDocument.Parse(File.ReadAllText(Snapshots.repoFile "proofs.json"))

              let row =
                  ladder.RootElement.GetProperty("claims").EnumerateArray()
                  |> Seq.find (fun c -> c.GetProperty("id").GetString() = "versors-pinned-by-recorded-rules")

              let evidence = row.GetProperty "evidence"
              Expect.equal (row.GetProperty("level").GetString()) "tested" "the row says tested, and no more"
              Expect.equal (evidence.GetProperty("family").GetString()) "Versors" "the row names this family"

              let cases =
                  [ for c in evidence.GetProperty("cases").EnumerateArray() -> c.GetString() ]

              Expect.equal (List.length cases) 11 "the row lists its cases"

              let source =
                  File.ReadAllText(
                      Snapshots.repoFile (Path.Combine("tests", "Fuaran.Core.Compute.Tests", "VersorTests.fs"))
                  )

              for case in cases do
                  Expect.stringContains
                      source
                      (sprintf "testCase \"%s\"" case)
                      (sprintf "the case \"%s\" is a test here" case)

              // The row stands over the trigonometry's premise, and names it.
              Expect.stringContains
                  (row.GetProperty("claim").GetString())
                  "trigonometry-pinned-by-transcription"
                  "the row names the premise the pinned sine and arc tangent stand on" ]
