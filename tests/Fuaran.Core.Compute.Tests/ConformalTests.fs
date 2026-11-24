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

let private wrongPorts: (string * Arithmetic) list =
    [ "another order of summation", products Kernel.twoRoundings 0.0 Array.rev
      "a fused multiply-add", products fused 0.0 id
      "a fold seeded from its first term", products Kernel.twoRoundings -0.0 id
      "a product with a reciprocal",
      { shipped with
          Divide = fun x y -> x * (1.0 / y) }
      "an unscaled norm",
      { shipped with
          Norm = fun xs -> sqrt (xs |> Array.fold (fun acc v -> acc + v * v) 0.0) }
      "half a squared length summed from the right",
      { shipped with
          HalfSquaredLength = fun x1 x2 x3 -> 0.5 * (x1 * x1 + (x2 * x2 + x3 * x3)) }
      "a radius halved before it is squared",
      { shipped with
          HalfSquare = fun r -> (0.5 * r) * r }
      "a power formed as zero less twice the product",
      { shipped with
          MinusTwice = fun s -> 0.0 - 2.0 * s }
      "no test of a zero divisor", { shipped with IsZero = fun _ -> false }
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
          SignsDecideAtZero = false } ]

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

/// Every wrong port with the vectors it fails, by function: the listing DECISIONS.md D87's table is
/// written from.
let witnessListing () : (string * (string * string list) list) list =
    wrongPorts |> List.map (fun (name, r) -> name, redUnder r)

/// The functions each wrong port turns red. Held both ways: a function listed has a vector that fails
/// that port, and a function not listed has none, so nothing here is asserted of a vector that does
/// not exist.
let private redFunctions: (string * string list) list =
    [ "another order of summation",
      [ "Blade.inverse"
        "Blade.join"
        "Blade.meet"
        "Blade.meetWithin"
        "Circle.centre"
        "Circle.ofCentreRadiusNormal"
        "Circle.power"
        "Circle.squaredRadius"
        "Circle.through"
        "Line.meet"
        "Line.meetPlane"
        "Line.meetSphere"
        "Plane.nearestPoint"
        "Plane.signedDistance"
        "Plane.through"
        "PointPair.centre"
        "PointPair.points"
        "PointPair.squaredRadius"
        "Sphere.power"
        "Sphere.squaredRadius"
        "Sphere.through" ]
      "a fused multiply-add",
      [ "Blade.inverse"
        "Blade.join"
        "Blade.meet"
        "Blade.meetWithin"
        "Circle.centre"
        "Circle.ofCentreRadiusNormal"
        "Circle.power"
        "Circle.squaredRadius"
        "Circle.through"
        "Line.meet"
        "Line.meetPlane"
        "Line.meetSphere"
        "Line.nearestPoint"
        "Line.ofPointDirection"
        "Line.through"
        "Plane.meet"
        "Plane.signedDistance"
        "Plane.through"
        "PointPair.centre"
        "PointPair.points"
        "PointPair.squaredRadius"
        "PointPair.through"
        "Sphere.meet"
        "Sphere.meetPlane"
        "Sphere.power"
        "Sphere.squaredRadius"
        "Sphere.through" ]
      "a fold seeded from its first term", [ "Blade.meet"; "Blade.meetWithin" ]
      "a product with a reciprocal",
      [ "Blade.inverse"
        "Circle.centre"
        "Circle.normalise"
        "Circle.ofCentreRadiusNormal"
        "Circle.power"
        "Circle.squaredRadius"
        "Line.meet"
        "Line.nearestPoint"
        "Line.normalise"
        "Line.ofPointDirection"
        "Plane.nearestPoint"
        "Plane.normalise"
        "Plane.ofNormalDistance"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.centre"
        "PointPair.kind"
        "PointPair.normalise"
        "PointPair.points"
        "Sphere.centre"
        "Sphere.normalise"
        "Sphere.power"
        "Sphere.squaredRadius" ]
      "an unscaled norm",
      [ "Circle.normalise"
        "Circle.ofCentreRadiusNormal"
        "Circle.weight"
        "Line.normalise"
        "Line.ofPointDirection"
        "Line.weight"
        "Plane.normalise"
        "Plane.ofNormalDistance"
        "Plane.signedDistance"
        "Plane.weight"
        "PointPair.normalise"
        "PointPair.weight" ]
      "half a squared length summed from the right",
      [ "Circle.ofCentreRadiusNormal"; "Point.up"; "Sphere.ofCentreRadius" ]
      "a radius halved before it is squared", [ "Circle.ofCentreRadiusNormal"; "Sphere.ofCentreRadius" ]
      "a power formed as zero less twice the product", [ "Circle.power"; "Sphere.power" ]
      "no test of a zero divisor",
      [ "Blade.inverse"
        "Blade.meetWithin"
        "Circle.centre"
        "Circle.normalise"
        "Circle.ofCentreRadiusNormal"
        "Circle.power"
        "Circle.side"
        "Circle.squaredRadius"
        "Line.meet"
        "Line.nearestPoint"
        "Line.normalise"
        "Line.ofPointDirection"
        "Plane.nearestPoint"
        "Plane.normalise"
        "Plane.ofNormalDistance"
        "Plane.side"
        "Plane.signedDistance"
        "Point.coordinates"
        "Point.normalise"
        "PointPair.centre"
        "PointPair.normalise"
        "PointPair.points"
        "PointPair.squaredRadius"
        "Sphere.centre"
        "Sphere.normalise"
        "Sphere.power"
        "Sphere.side"
        "Sphere.squaredRadius" ]
      "a strict threshold",
      [ "Circle.kind"
        "Circle.side"
        "Line.kind"
        "Line.meet"
        "Plane.kind"
        "Plane.side"
        "PointPair.kind"
        "PointPair.points"
        "Sphere.kind"
        "Sphere.side" ]
      "a threshold never met",
      [ "Circle.kind"
        "Circle.side"
        "Line.kind"
        "Line.meet"
        "Plane.kind"
        "Plane.side"
        "PointPair.kind"
        "PointPair.points"
        "Sphere.kind"
        "Sphere.side" ]
      "a threshold always met",
      [ "Circle.kind"
        "Circle.side"
        "Plane.side"
        "PointPair.kind"
        "PointPair.points"
        "Sphere.kind"
        "Sphere.side" ]
      "a quotient formed at a threshold of zero", [ "Circle.side"; "Plane.side"; "Sphere.side" ] ]

[<Tests>]
let tests =
    testList
        "Conformal objects"
        [

          testCase "the arithmetic a witness perturbs is the shipped arithmetic"
          <| fun () ->
              Expect.equal (List.length conformal) 747 "the object modules' vectors"

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

          testCase "each wrong port fails a vector of every function it could change, and of no other"
          <| fun () ->
              let actual =
                  witnessListing ()
                  |> List.map (fun (name, byFunction) -> name, byFunction |> List.map fst)

              if actual <> redFunctions then
                  let source =
                      actual
                      |> List.map (fun (name, functions) ->
                          sprintf
                              "      \"%s\",\n      [ %s ]"
                              name
                              (functions |> List.map (sprintf "\"%s\"") |> String.concat "; "))
                      |> String.concat "\n"

                  failtestf "the functions each wrong port turns red have moved. As measured:\n%s" source

              // No port is vacuous, and none is the shipped arithmetic under another name.
              for name, functions in redFunctions do
                  Expect.isNonEmpty functions (sprintf "%s fails a vector" name)

              // Every function that computes is failed by at least one wrong port.
              let failed = redFunctions |> List.collect snd |> Set.ofList

              let computing =
                  conformal
                  |> List.map (fun c -> c.Op)
                  |> List.filter (fun op -> not (copies.Contains op))
                  |> Set.ofList

              // `Circle.plane` is `X ∧ n∞`: each coefficient it answers has one term, so it is a signed
              // copy and no arithmetic can move it. What fails it is a normalisation (the next case).
              Expect.equal
                  (Set.difference computing failed)
                  (set [ "Circle.plane" ])
                  "a wrong port fails every function that computes but the one that only copies"

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

          testCase "each named case goes red where the rule that names it is changed"
          <| fun () ->
              let port (name: string) : Arithmetic =
                  wrongPorts |> List.find (fun (n, _) -> n = name) |> snd

              let never = "a threshold never met"
              let always = "a threshold always met"
              let strict = "a strict threshold"
              let undivided = "no test of a zero divisor"
              let quotient = "a quotient formed at a threshold of zero"
              let signless = "a power formed as zero less twice the product"

              for id, name in
                  [ "PointPair.points-of-a-tangent-pair", never
                    "PointPair.points-of-an-imaginary-pair", always
                    "PointPair.points-tangent-at-the-threshold", strict
                    "PointPair.points-tangent-at-the-negative-threshold", strict
                    "PointPair.points-of-a-flat-point", undivided
                    "PointPair.kind-of-a-tangent-pair", never
                    "PointPair.kind-of-a-flat-point", never
                    "PointPair.kind-at-infinity", never
                    "PointPair.kind-of-the-zero-blade", never
                    "PointPair.kind-of-a-euclidean-bivector", always
                    "PointPair.kind-no-round-at-the-threshold", strict
                    "PointPair.kind-no-flat-at-the-threshold", strict
                    "PointPair.kind-zero-at-the-threshold", strict
                    "Circle.kind-of-three-collinear-points", never
                    "Circle.kind-of-two-spheres-apart", always
                    "Circle.kind-of-a-point", never
                    "Circle.kind-of-the-zero-blade", never
                    "Circle.kind-tangent-at-the-negative-threshold", strict
                    "Circle.squaredRadius-of-three-collinear-points", undivided
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
                    "Line.ofPointDirection-of-a-zero-direction", undivided
                    "Plane.ofNormalDistance-of-a-zero-normal", undivided
                    "Circle.ofCentreRadiusNormal-of-a-zero-normal", undivided
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

          testCase "the operation modules name no float literal in a comparison but the ones the record lists"
          <| fun () ->
              // DECISIONS.md D85 item 6, read against the sources. Its scope is the OPERATION modules:
              // the law family is not one, and compares bits against float literals as a law must.
              let literal = @"-?\d+\.\d+(?:[eE][-+]?\d+)?|-?\d+[eE][-+]?\d+"
              let comparator = @"(?<![-<>=!:|])(?:<=|>=|<>|=|<|>)(?![-<>=|])"

              let comparison =
                  Regex(
                      sprintf
                          @"(?<op>%s)\s*(?:%s)(?![\w.])|(?<![\w.])(?:%s)\s*(?:%s)"
                          comparator
                          literal
                          literal
                          comparator
                  )

              let binding =
                  Regex
                      @"^\s*let\s+(?:mutable\s+|private\s+|internal\s+|inline\s+)*[\w`]+(?:\s*:\s*[\w\[\]<>. ]+)?\s*=\s*"

              let read (file: string) : (string * string * string) list =
                  File
                      .ReadAllText(Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.GeometricAlgebra", file)))
                      .Split('\n')
                  |> Array.filter (fun line -> not (line.TrimStart().StartsWith "//"))
                  |> Array.collect (fun line ->
                      [| for m in comparison.Matches(binding.Replace(line, "")) ->
                             file, Regex.Replace(m.Value, @"\s+", " "), line.Trim() |])
                  |> List.ofArray

              let found =
                  [ "Basis.fs"
                    "Multivector.fs"
                    "Objects.fs"
                    "Conformal.fs"
                    "Geometry.fs"
                    "GeometricAlgebraOperations.fs" ]
                  |> List.collect read

              // Each comparison, and the phrase of item 6 that states it.
              let listed =
                  [ "Multivector.fs", "= 0.0", "elif x", "the zero test inside the hash (item 8)"
                    "Conformal.fs", "= 0.0", "elif m", "inside the norm itself (D49 item 4), outside the rule"
                    "Conformal.fs", "= 0.0", "IsZero", "a divisor, a direction's norm and a threshold against zero"
                    "Conformal.fs", ">= 0.0", "tau", "a threshold that is not negative"
                    "Conformal.fs", "< 0.0", "rho", "a radius that is not negative"
                    "Conformal.fs", "< 0.0", "numerator", "the sign of a numerator at a threshold of zero"
                    "Conformal.fs", "< 0.0", "(d <", "the sign of a denominator at a threshold of zero" ]

              Expect.equal (List.length found) (List.length listed) (sprintf "the comparisons found: %A" found)

              for file, text, word, phrase in listed do
                  Expect.isTrue
                      (found
                       |> List.exists (fun (f, t, line) -> f = file && t = text && line.Contains word))
                      (sprintf "%s names `%s` for %s" file text phrase)

              // The rule is not vacuous: it reads a literal on either side, an exponent, and `1.0`.
              Expect.equal
                  ([ "if abs d <= 1e-9 then"
                     "elif 0.0 = x then"
                     "if s <> 1.0 then"
                     "x < -2.5E-3" ]
                   |> List.filter comparison.IsMatch
                   |> List.length)
                  4
                  "the rule reads each form of comparison"

              Expect.isFalse
                  (comparison.IsMatch(binding.Replace("        let mutable m = 0.0", "")))
                  "a binding is no comparison"

              // No tolerance is a constant: no optional argument, and no named one.
              let named = Regex @"\?\s*tau|[eE]psilon|[tT]olerance|\beps\b|\bulp\b"

              let code =
                  [ "Basis.fs"
                    "Multivector.fs"
                    "Objects.fs"
                    "Conformal.fs"
                    "Geometry.fs"
                    "GeometricAlgebraOperations.fs" ]
                  |> List.collect (fun file ->
                      File
                          .ReadAllText(
                              Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.GeometricAlgebra", file))
                          )
                          .Split('\n')
                      |> Array.filter (fun line -> not (line.TrimStart().StartsWith "//"))
                      |> List.ofArray)

              Expect.isGreaterThan (List.length code) 600 "the sources were read"
              Expect.isEmpty (code |> List.filter named.IsMatch) "no default threshold and no named tolerance"

              Expect.isTrue
                  (named.IsMatch "let kind (?tau: float)" && named.IsMatch "let epsilon = 1e-9")
                  "the rule reads one"

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
