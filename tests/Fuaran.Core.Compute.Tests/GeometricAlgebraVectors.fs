namespace Fuaran.Compute.Tests

// ============================================================================
//  Phase 727 — the geometric algebra law vectors: the multivector's published
//  functions over authored inputs, each answer computed by calling the package
//  and rendered through the substrate's one corpus writer (`CorpusFile`) into
//  `conformance/laws/geometric-algebra-laws.json` (DECISIONS.md D85, D86).
//
//  Three kinds of vector. The TABLE ROWS multiply each basis blade by the
//  multivector whose coefficients are the powers of two, under each of the
//  three products, so every one of the table's signed terms is one bit of one
//  answer. The HARD INPUTS are the ones on which a port that keeps another
//  rule answers another bit: another order of summation, a fused
//  multiply-add, a fold seeded from its first term, a term skipped because a
//  coefficient is zero, a negation that loses a zero's sign. The GENERAL
//  inputs are drawn by a pinned generator and written into the document, so a
//  port reads them and reproduces nothing but the answers. Every float is its
//  IEEE-754 binary64 bit pattern.
//
//  Phase 728 appends the vectors carried from Phase 727 and the
//  conformal objects' (DECISIONS.md D87): every published function of the
//  object modules over drawn configurations, and each named case, each
//  refusal and each boundary of a threshold by hand.
//
//  Fable-clean: the node leg (LinearAlgebraNode/) compiles this file and
//  renders the same document under JavaScript, and the suite holds the two
//  renderings and the committed file equal.
// ============================================================================

open System
open Fuaran.Core
open Fuaran.Compute.GeometricAlgebra

module GeometricAlgebraVectors =

    /// The file this family is written to under `laws/`.
    let fileName = "geometric-algebra-laws.json"

    /// One operand of a case.
    type Operand =
        /// A multivector, its thirty-two coefficients in index order.
        | Coefficients of float[]
        /// The basis blade of this index.
        | Blade of int
        /// The multivector whose coefficient at index `j` is `2^j`.
        | PowersOfTwo
        /// A float.
        | Number of float
        /// A blade index or a grade.
        | Whole of int

    /// One authored case: a published function over named operands.
    type Case =
        { Id: string
          Op: string
          Input: (string * Operand) list }

    /// The number of coefficients.
    let private count = 32

    // ---- the operands -----------------------------------------------------------------------------

    /// `2^n`, by exact doubling or halving.
    let private pow2 (n: int) : float =
        let mutable x = 1.0

        for _ in 1 .. abs n do
            x <- if n > 0 then x * 2.0 else x / 2.0

        x

    /// The multivector of an operand.
    let multivector (operand: Operand) : Multivector =
        match operand with
        | Coefficients xs -> Multivector.init (fun index -> xs[index])
        | Blade index -> Multivector.basis index
        | PowersOfTwo -> Multivector.init pow2
        | Number _
        | Whole _ -> failwith "this operand is not a multivector"

    /// Coefficients that are `+0.0` but where stated.
    let private sparse (entries: (int * float) list) : float[] =
        let xs = Array.zeroCreate<float> count

        for index, value in entries do
            xs[index] <- value

        xs

    /// The pinned generator: Park and Miller's minimal standard, run in float arithmetic, every step
    /// exact, so a seed draws the same coefficients on every host.
    type private Draw(seed: int) =
        let mutable state = float seed

        member _.Next() : float =
            state <- (state * 48271.0) % 2147483647.0
            state

        member this.Int (lo: int) (hi: int) : int =
            lo + int (this.Next() % float (hi - lo + 1))

        /// A double in `(−½, ½)` times a power of two between `2^(centre − spread)` and
        /// `2^(centre + spread)`.
        member this.Scaled (centre: int) (spread: int) : float =
            let mantissa = this.Next() / 2147483648.0 - 0.5
            mantissa * pow2 (centre + this.Int -spread spread)

    /// Thirty-two drawn coefficients about `2^centre`, across `spread` binary orders either way.
    let private dense (seed: int) (centre: int) (spread: int) : float[] =
        let d = Draw seed
        Array.init count (fun _ -> d.Scaled centre spread)

    /// Drawn coefficients, two in three of them a zero of a drawn sign.
    let private withZeros (seed: int) (spread: int) : float[] =
        let d = Draw seed

        Array.init count (fun _ ->
            match d.Int 0 5 with
            | 0
            | 1 -> d.Scaled 0 spread
            | 2
            | 3 -> 0.0
            | _ -> -0.0)

    /// Drawn coefficients with one replaced.
    let private withOne (index: int) (value: float) (xs: float[]) : float[] =
        Array.mapi (fun i x -> if i = index then value else x) xs

    /// The blades the scalar coefficient of a geometric product takes with a minus sign, as
    /// DECISIONS.md D85 item 3 writes that output out.
    let private scalarTermsSubtracted =
        [ 1; 5; 6; 7; 8; 10; 11; 12; 13; 14; 15; 22; 27; 28; 29; 31 ]

    /// The left operand under which every term of the scalar coefficient, with its sign, is a negative
    /// zero against a right operand of `+0.0`s: `-0.0` where the term is added, `+0.0` where it is
    /// subtracted.
    let private everyScalarTermANegativeZero: float[] =
        Array.init count (fun index ->
            if List.contains index scalarTermsSubtracted then
                0.0
            else
                -0.0)

    /// `+0.0` at the even indices and `-0.0` at the odd ones.
    let private alternatingZeros: float[] =
        Array.init count (fun index -> if index % 2 = 0 then 0.0 else -0.0)

    let private negativeZeros: float[] = Array.create count -0.0

    /// The scalar coefficient is `((2^60 + 1) − 2^60) + 1` in the recorded order, which is `1`: the
    /// first `1` is absorbed, then the large terms cancel. With the right operand's index outermost it
    /// is `2`, and in descending order `0`.
    let private absorbedLeft = sparse [ 0, pow2 60; 1, 1.0; 2, -(pow2 60); 3, 1.0 ]

    let private absorbedRight = sparse [ 0, 1.0; 2, 1.0; 3, 1.0; 5, -1.0 ]

    /// The scalar coefficient is `−1` and then `(1 + 2^-30)(1 − 2^-30)`, which rounds to `1`: two
    /// roundings answer `+0.0`, and a fused multiply-add answers `−2^-60`.
    let private fusedLeft = sparse [ 0, -1.0; 2, 1.0 + pow2 -30 ]

    let private fusedRight = sparse [ 0, 1.0; 2, 1.0 - pow2 -30 ]

    /// A zero against an infinity: every coefficient of the left operand but one is a zero, and the
    /// right operand's scalar is `+∞`.
    let private zeroLeft = sparse [ 2, 1.0 ]

    let private infiniteRight = sparse [ 0, Double.PositiveInfinity; 2, 1.0 ]

    // ---- the cases -------------------------------------------------------------------------------

    let private unary (id: string) (op: string) (a: float[]) : Case =
        { Id = id
          Op = op
          Input = [ "a", Coefficients a ] }

    let private binary (id: string) (op: string) (a: float[]) (b: float[]) : Case =
        { Id = id
          Op = op
          Input = [ "a", Coefficients a; "b", Coefficients b ] }

    let private products =
        [ "geometric", "geometricProduct"
          "outer", "outerProduct"
          "contraction", "leftContraction" ]

    /// Each basis blade against the powers of two, under each product: an output coefficient is the
    /// signed sum of the powers `2^j` whose term reaches it, so the vectors of one product spell out
    /// its whole table.
    let private tableRows: Case list =
        [ for short, op in products do
              for index in 0 .. count - 1 ->
                  { Id = sprintf "table-%s-%02d" short index
                    Op = op
                    Input = [ "a", Blade index; "b", PowersOfTwo ] } ]

    let private hardInputs: Case list =
        [ binary "geometric-absorbed-then-cancelled" "geometricProduct" absorbedLeft absorbedRight
          binary "geometric-fma-witness" "geometricProduct" fusedLeft fusedRight
          binary
              "geometric-every-scalar-term-a-negative-zero"
              "geometricProduct"
              everyScalarTermANegativeZero
              (sparse [])
          binary "geometric-zero-against-infinity" "geometricProduct" zeroLeft infiniteRight
          binary "outer-zero-against-infinity" "outerProduct" zeroLeft infiniteRight
          binary "contraction-zero-against-infinity" "leftContraction" zeroLeft infiniteRight
          binary "scalar-absorbed-then-cancelled" "scalarProduct" absorbedLeft absorbedRight
          binary "scalar-fma-witness" "scalarProduct" fusedLeft fusedRight
          binary "scalar-every-term-a-negative-zero" "scalarProduct" everyScalarTermANegativeZero (sparse [])
          binary "scalar-zero-against-infinity" "scalarProduct" zeroLeft infiniteRight
          // `½ n∞ + n0`, the vector of negative signature, and the null vector `n0`.
          unary "squaredNorm-negative-signature" "squaredNorm" (sparse [ 1, 1.0; 5, 0.5 ])
          unary "squaredNorm-null-vector" "squaredNorm" (sparse [ 1, 1.0 ])
          unary "squaredNorm-zero-against-infinity" "squaredNorm" (sparse [ 1, Double.PositiveInfinity ])
          // Exact negation flips a zero's sign, and a copy keeps it.
          unary "negate-zeros" "negate" alternatingZeros
          unary "reverse-zeros" "reverse" alternatingZeros
          unary "gradeInvolution-zeros" "gradeInvolution" alternatingZeros
          unary "dual-zeros" "dual" alternatingZeros
          unary "undual-zeros" "undual" alternatingZeros
          unary "dual-positive-zeros" "dual" (sparse [])
          unary "reverse-positive-zeros" "reverse" (sparse [])
          { Id = "grade-2-of-negative-zeros"
            Op = "grade"
            Input = [ "grade", Whole 2; "a", Coefficients negativeZeros ] }
          // One rounded operation for each coefficient.
          binary "add-zeros-of-both-signs" "add" alternatingZeros negativeZeros
          binary "add-opposites" "add" (dense 31 0 20) (dense 31 0 20 |> Array.map (fun x -> -x))
          binary "subtract-itself" "subtract" (dense 32 0 20) (dense 32 0 20)
          { Id = "scale-by-negative-zero"
            Op = "scale"
            Input = [ "s", Number -0.0; "a", Coefficients(dense 33 0 20) ] }
          { Id = "scale-zeros-by-infinity"
            Op = "scale"
            Input = [ "s", Number Double.PositiveInfinity; "a", Coefficients alternatingZeros ] } ]

    let private generalInputs: Case list =
        [ // Dense operands at one scale, then across more and more binary orders of magnitude: the
          // wider the spread, the more of every sum is rounding.
          yield binary "geometric-general-1" "geometricProduct" (dense 1 0 0) (dense 2 0 0)
          yield binary "geometric-general-2" "geometricProduct" (dense 3 0 8) (dense 4 0 8)
          yield binary "geometric-general-3" "geometricProduct" (dense 5 0 20) (dense 6 0 20)
          yield binary "geometric-general-4" "geometricProduct" (dense 7 0 40) (dense 8 0 40)
          yield binary "geometric-with-both-zeros" "geometricProduct" (withZeros 9 20) (withZeros 10 20)
          yield binary "geometric-with-a-nan" "geometricProduct" (withOne 7 Double.NaN (withZeros 11 8)) (dense 12 0 8)
          yield
              binary
                  "geometric-with-an-infinity"
                  "geometricProduct"
                  (dense 13 0 8)
                  (withOne 20 Double.NegativeInfinity (dense 14 0 8))
          // Products that underflow: into the subnormals, and past them to a zero.
          yield binary "geometric-with-subnormals" "geometricProduct" (dense 15 -530 8) (dense 16 -535 8)
          yield
              binary
                  "geometric-of-subnormals"
                  "geometricProduct"
                  (withOne 3 5e-324 (dense 17 -1040 20))
                  (dense 18 10 20)
          // Products that overflow: an infinity of each sign in one sum is a NaN, in this order.
          yield binary "geometric-with-overflow" "geometricProduct" (dense 19 520 8) (dense 20 510 8)
          yield binary "outer-general-1" "outerProduct" (dense 21 0 8) (dense 22 0 8)
          yield binary "outer-general-2" "outerProduct" (dense 23 0 40) (dense 24 0 40)
          yield binary "outer-with-both-zeros" "outerProduct" (withZeros 25 20) (withZeros 26 20)
          yield binary "contraction-general-1" "leftContraction" (dense 27 0 8) (dense 28 0 8)
          yield binary "contraction-general-2" "leftContraction" (dense 29 0 40) (dense 30 0 40)
          yield binary "contraction-with-both-zeros" "leftContraction" (withZeros 34 20) (withZeros 35 20)
          yield binary "scalar-general-1" "scalarProduct" (dense 36 0 8) (dense 37 0 8)
          yield binary "scalar-general-2" "scalarProduct" (dense 38 0 40) (dense 39 0 40)
          yield binary "scalar-with-both-zeros" "scalarProduct" (withZeros 40 20) (withZeros 41 20)
          yield unary "squaredNorm-general-1" "squaredNorm" (dense 42 0 8)
          yield unary "squaredNorm-general-2" "squaredNorm" (dense 43 0 40)
          yield binary "add-general" "add" (dense 44 0 20) (dense 45 0 20)
          yield binary "subtract-general" "subtract" (dense 46 0 20) (dense 47 0 20)
          yield
              { Id = "scale-general"
                Op = "scale"
                Input = [ "s", Number((Draw 48).Scaled 0 20); "a", Coefficients(dense 49 0 20) ] }

          // The copies, over one operand holding both zeros, an infinity and a NaN.
          let mixed =
              withZeros 50 20 |> withOne 4 Double.NegativeInfinity |> withOne 17 Double.NaN

          for op in [ "negate"; "reverse"; "gradeInvolution"; "dual"; "undual" ] do
              yield unary (op + "-general") op mixed

          for g in [ 0; 1; 2; 3; 4; 5; 6; -1 ] do
              yield
                  { Id = sprintf "grade-%s-general" (if g < 0 then "below-zero" else string g)
                    Op = "grade"
                    Input = [ "grade", Whole g; "a", Coefficients mixed ] }

          yield
              { Id = "ofScalar-general"
                Op = "ofScalar"
                Input = [ "s", Number((Draw 51).Scaled 0 20) ] }

          yield
              { Id = "ofScalar-nan"
                Op = "ofScalar"
                Input = [ "s", Number Double.NaN ] }

          for index in [ 0; 9; 31 ] do
              yield
                  { Id = sprintf "basis-%02d" index
                    Op = "basis"
                    Input = [ "index", Whole index ] }

          for index in [ 0; 9; 31 ] do
              yield
                  { Id = sprintf "coefficient-%02d" index
                    Op = "coefficient"
                    Input = [ "index", Whole index; "a", Coefficients mixed ] } ]

    // ============================================================================================
    //  Phase 728 — vectors carried from Phase 727, and the conformal objects
    //  (DECISIONS.md D85 items 4 to 6, D87). New cases are appended: the 167 cases above keep
    //  their place and their bytes.
    // ============================================================================================

    /// Four vectors over the multivector's own functions. The squared norm's scalar coefficient is
    /// `1 − (1 + 2^-30)(1 − 2^-30) + 1 − (1 − 2^-30)(1 + 2^-30)`: two roundings answer `+0.0` and a
    /// fused multiply-add `2^-60`. It is `((2^60 + 1) − 2^60) + 1 = 1` in the recorded order and `0`
    /// descending. Twenty-four of its thirty-two terms can be a negative zero and here each is one;
    /// the other eight are squares, so the answer is `+0.0` under any summation. And the scalar
    /// product `((2^60 + 1) − 2^60) + 2 = 2` is `((2^60 + 2) − 2^60) + 1 = 1` with the right operand's
    /// index outermost.
    let private carried: Case list =
        [ unary
              "squaredNorm-fma-witness"
              "squaredNorm"
              (sparse [ 0, 1.0; 1, 1.0 + pow2 -30; 2, 1.0; 5, 1.0 - pow2 -30 ])
          unary "squaredNorm-absorbed-then-cancelled" "squaredNorm" (sparse [ 0, pow2 30; 2, 1.0; 9, pow2 30; 10, 1.0 ])
          unary
              "squaredNorm-every-term-that-can-be-a-negative-zero"
              "squaredNorm"
              (sparse [ 12, -0.0; 14, -0.0; 15, -0.0; 30, -0.0 ])
          binary
              "scalar-right-index-outermost"
              "scalarProduct"
              (sparse [ 0, pow2 60; 1, 1.0; 2, -(pow2 60); 5, 1.0 ])
              (sparse [ 0, 1.0; 1, -2.0; 2, 1.0; 5, -1.0 ]) ]

    // ---- the typed objects, as the document writes them: a type's coefficients in index order ---

    let pointOf (v: float[]) : Point =
        { N0 = v[0]
          E1 = v[1]
          E2 = v[2]
          E3 = v[3]
          NInf = v[4] }

    let pointValues (p: Point) : float[] = [| p.N0; p.E1; p.E2; p.E3; p.NInf |]

    let pairOf (v: float[]) : PointPair =
        { N0E1 = v[0]
          N0E2 = v[1]
          N0E3 = v[2]
          N0NInf = v[3]
          E1E2 = v[4]
          E1E3 = v[5]
          E1NInf = v[6]
          E2E3 = v[7]
          E2NInf = v[8]
          E3NInf = v[9] }

    let pairValues (x: PointPair) : float[] =
        [| x.N0E1
           x.N0E2
           x.N0E3
           x.N0NInf
           x.E1E2
           x.E1E3
           x.E1NInf
           x.E2E3
           x.E2NInf
           x.E3NInf |]

    let circleOf (v: float[]) : Circle =
        { N0E1E2 = v[0]
          N0E1E3 = v[1]
          N0E1NInf = v[2]
          N0E2E3 = v[3]
          N0E2NInf = v[4]
          N0E3NInf = v[5]
          E1E2E3 = v[6]
          E1E2NInf = v[7]
          E1E3NInf = v[8]
          E2E3NInf = v[9] }

    let circleValues (x: Circle) : float[] =
        [| x.N0E1E2
           x.N0E1E3
           x.N0E1NInf
           x.N0E2E3
           x.N0E2NInf
           x.N0E3NInf
           x.E1E2E3
           x.E1E2NInf
           x.E1E3NInf
           x.E2E3NInf |]

    let lineOf (v: float[]) : Line =
        { N0E1NInf = v[0]
          N0E2NInf = v[1]
          N0E3NInf = v[2]
          E1E2NInf = v[3]
          E1E3NInf = v[4]
          E2E3NInf = v[5] }

    let lineValues (x: Line) : float[] =
        [| x.N0E1NInf; x.N0E2NInf; x.N0E3NInf; x.E1E2NInf; x.E1E3NInf; x.E2E3NInf |]

    let sphereOf (v: float[]) : Sphere =
        { N0E1E2E3 = v[0]
          N0E1E2NInf = v[1]
          N0E1E3NInf = v[2]
          N0E2E3NInf = v[3]
          E1E2E3NInf = v[4] }

    let sphereValues (x: Sphere) : float[] =
        [| x.N0E1E2E3; x.N0E1E2NInf; x.N0E1E3NInf; x.N0E2E3NInf; x.E1E2E3NInf |]

    let planeOf (v: float[]) : Plane =
        { N0E1E2NInf = v[0]
          N0E1E3NInf = v[1]
          N0E2E3NInf = v[2]
          E1E2E3NInf = v[3] }

    let planeValues (x: Plane) : float[] =
        [| x.N0E1E2NInf; x.N0E1E3NInf; x.N0E2E3NInf; x.E1E2E3NInf |]

    let private case (id: string) (op: string) (input: (string * Operand) list) : Case =
        { Id = id; Op = op; Input = input }

    let private P (p: Point) : Operand = Coefficients(pointValues p)
    let private PP (x: PointPair) : Operand = Coefficients(pairValues x)
    let private C (x: Circle) : Operand = Coefficients(circleValues x)
    let private L (x: Line) : Operand = Coefficients(lineValues x)
    let private S (x: Sphere) : Operand = Coefficients(sphereValues x)
    let private PL (x: Plane) : Operand = Coefficients(planeValues x)
    let private T (a: float, b: float, c: float) : Operand = Coefficients [| a; b; c |]
    let private N (x: float) : Operand = Number x
    let private M (a: Multivector) : Operand = Coefficients(Multivector.toArray a)

    /// An operand the author built through the package: one it refuses is an authoring mistake.
    let private ok (r: Result<'a, GeometryRefusal>) : 'a =
        match r with
        | Ok x -> x
        | Error _ -> failwith "an authored operand was refused"

    let private u (x: float) (y: float) (z: float) : Point = ok (Point.up (x, y, z))

    let private times (w: float) (values: float[]) : float[] = values |> Array.map (fun v -> w * v)

    let private weighted (w: float) (p: Point) : Point = pointOf (times w (pointValues p))

    /// One drawn configuration: five weighted points, and the parameters of two spheres, two planes, a
    /// line and a circle, every coordinate about `2^centre` across `spread` binary orders either way.
    type private Scene(seed: int, centre: int, spread: int) =
        let d = Draw seed

        let triple () : float * float * float =
            let a = d.Scaled centre spread
            let b = d.Scaled centre spread
            let c = d.Scaled centre spread
            a, b, c

        let positive () : float =
            let x = d.Scaled centre spread
            if x < 0.0 then -x else x

        let point () : Point =
            let w = d.Scaled 1 1
            weighted w (ok (Point.up (triple ())))

        let p = point ()
        let q = point ()
        let r = point ()
        let s = point ()
        let t = point ()
        let c1 = triple ()
        let rho1 = positive ()
        let c2 = triple ()
        let rho2 = positive ()
        let n1 = triple ()
        let delta1 = d.Scaled centre spread
        let n2 = triple ()
        let delta2 = d.Scaled centre spread
        let a1 = triple ()
        let d1 = triple ()

        member _.P = p
        member _.Q = q
        member _.R = r
        member _.S = s
        member _.T = t
        member _.C1 = c1
        member _.Rho1 = rho1
        member _.N1 = n1
        member _.Delta1 = delta1
        member _.A1 = a1
        member _.D1 = d1
        member _.SphereP = ok (Sphere.ofCentreRadius c1 rho1)
        member _.SphereQ = ok (Sphere.ofCentreRadius c2 rho2)
        member _.SphereT = Sphere.through p q r s
        member _.PlaneP = ok (Plane.ofNormalDistance n1 delta1)
        member _.PlaneQ = ok (Plane.ofNormalDistance n2 delta2)
        member _.PlaneT = Plane.through p q r
        member _.LineP = ok (Line.ofPointDirection a1 d1)
        member _.LineT = Line.through p q
        /// A line through the first sphere's centre: it meets that sphere in a real pair.
        member _.LineC = ok (Line.ofPointDirection c1 d1)
        member _.CircleP = ok (Circle.ofCentreRadiusNormal c1 rho1 n1)
        member _.CircleT = Circle.through p q r
        member _.PairT = PointPair.through p q

    /// The drawn configurations, each with the threshold under which its two lines through one drawn
    /// point read as lines of one plane.
    let private scenes: (int * Scene * float) list =
        [ 1, Scene(7281, 0, 1), pow2 -30
          2, Scene(7282, 7, 5), pow2 -20
          3, Scene(7283, -6, 3), pow2 -60 ]

    /// Every published function over one drawn configuration. `k` is the configuration's number in a
    /// vector's id, and `coplanarWithin` the threshold under which its two lines through one drawn point
    /// read as lines of one plane.
    let private sceneCases (k: int) (s: Scene) (coplanarWithin: float) : Case list =
        [ let g (op: string) (what: string) (input: (string * Operand) list) : Case =
              case (sprintf "%s-general-%s%d" op what k) op input

          let zero = N 0.0
          let pairM = Line.meetSphere s.LineC s.SphereP
          let lineR = Line.through s.P s.R
          let any = Coefficients(dense (7300 + k) 0 4)
          let other = Coefficients(dense (7310 + k) 0 4)

          yield g "Blade.join" "" [ "a", any; "b", other ]
          yield g "Blade.meet" "" [ "a", any; "b", other ]

          yield
              g
                  "Blade.meet"
                  "of-two-spheres-"
                  [ "a", M(Sphere.toMultivector s.SphereP)
                    "b", M(Sphere.toMultivector s.SphereT) ]

          yield g "Blade.inverse" "of-a-sphere-" [ "a", M(Sphere.toMultivector s.SphereT) ]
          yield g "Blade.inverse" "of-a-line-" [ "a", M(Line.toMultivector s.LineP) ]

          yield
              g
                  "Blade.meetWithin"
                  "the-whole-space-"
                  [ "j", Blade 31
                    "a", M(Sphere.toMultivector s.SphereP)
                    "b", M(Plane.toMultivector s.PlaneP) ]

          yield
              g
                  "Blade.meetWithin"
                  "a-plane-"
                  [ "j", M(Plane.toMultivector s.PlaneT)
                    "a", M(Line.toMultivector s.LineT)
                    "b", M(Line.toMultivector lineR) ]

          yield g "Point.up" "" [ "x", T s.C1 ]
          yield g "Point.coordinates" "" [ "x", P s.P ]
          yield g "Point.weight" "" [ "x", P s.P ]
          yield g "Point.normalise" "" [ "x", P s.Q ]
          yield g "Point.toMultivector" "" [ "x", P s.R ]
          yield g "Point.ofMultivector" "" [ "a", any ]

          yield g "PointPair.through" "" [ "p", P s.P; "q", P s.Q ]
          yield g "PointPair.weight" "" [ "x", PP s.PairT ]
          yield g "PointPair.normalise" "" [ "x", PP s.PairT ]
          yield g "PointPair.centre" "" [ "x", PP s.PairT ]
          yield g "PointPair.centre" "of-a-meet-" [ "x", PP pairM ]
          yield g "PointPair.squaredRadius" "" [ "x", PP s.PairT ]
          yield g "PointPair.squaredRadius" "of-a-meet-" [ "x", PP pairM ]
          yield g "PointPair.points" "" [ "tau", zero; "x", PP s.PairT ]
          yield g "PointPair.points" "of-a-meet-" [ "tau", zero; "x", PP pairM ]
          yield g "PointPair.kind" "" [ "tau", zero; "x", PP s.PairT ]

          yield g "PointPair.kind" "of-a-line-and-a-plane-" [ "tau", zero; "x", PP(Line.meetPlane s.LineP s.PlaneP) ]

          yield g "PointPair.toMultivector" "" [ "x", PP s.PairT ]
          yield g "PointPair.ofMultivector" "" [ "a", any ]

          yield g "Line.through" "" [ "p", P s.P; "q", P s.Q ]
          yield g "Line.ofPointDirection" "" [ "p", T s.A1; "d", T s.D1 ]
          yield g "Line.weight" "" [ "x", L s.LineT ]
          yield g "Line.normalise" "" [ "x", L s.LineT ]
          yield g "Line.direction" "" [ "x", L s.LineT ]
          yield g "Line.moment" "" [ "x", L s.LineT ]
          yield g "Line.nearestPoint" "" [ "x", L s.LineT ]
          yield g "Line.nearestPoint" "of-a-unit-line-" [ "x", L s.LineP ]
          yield g "Line.kind" "" [ "tau", zero; "x", L s.LineT ]
          // Two lines through one drawn point: in exact arithmetic they cross, and in binary64 their
          // meet in the whole space is rounding residue, which `tau = 0` reads as skew.
          yield g "Line.meet" "at-tau-zero-" [ "tau", zero; "x", L s.LineT; "y", L lineR ]
          yield g "Line.meet" "within-a-threshold-" [ "tau", N coplanarWithin; "x", L s.LineT; "y", L lineR ]
          yield g "Line.meet" "of-two-skew-lines-" [ "tau", zero; "x", L s.LineT; "y", L s.LineP ]
          yield g "Line.meetPlane" "" [ "x", L s.LineT; "y", PL s.PlaneP ]
          yield g "Line.meetSphere" "" [ "x", L s.LineC; "y", S s.SphereP ]
          yield g "Line.meetSphere" "through-points-" [ "x", L s.LineT; "y", S s.SphereT ]
          yield g "Line.toMultivector" "" [ "x", L s.LineT ]
          yield g "Line.ofMultivector" "" [ "a", any ]

          yield g "Circle.through" "" [ "p", P s.P; "q", P s.Q; "r", P s.R ]
          yield g "Circle.ofCentreRadiusNormal" "" [ "centre", T s.C1; "radius", N s.Rho1; "normal", T s.N1 ]

          for what, circle in [ "", s.CircleT; "from-parameters-", s.CircleP ] do
              yield g "Circle.weight" what [ "x", C circle ]
              yield g "Circle.normalise" what [ "x", C circle ]
              yield g "Circle.centre" what [ "x", C circle ]
              yield g "Circle.squaredRadius" what [ "x", C circle ]
              yield g "Circle.kind" what [ "tau", zero; "x", C circle ]
              yield g "Circle.plane" what [ "x", C circle ]
              yield g "Circle.power" what [ "p", P s.S; "x", C circle ]
              yield g "Circle.side" what [ "tau", zero; "p", P s.T; "x", C circle ]

          yield g "Circle.toMultivector" "" [ "x", C s.CircleT ]
          yield g "Circle.ofMultivector" "" [ "a", any ]

          yield g "Plane.through" "" [ "p", P s.P; "q", P s.Q; "r", P s.R ]
          yield g "Plane.ofNormalDistance" "" [ "normal", T s.N1; "distance", N s.Delta1 ]

          for what, plane in [ "", s.PlaneT; "from-parameters-", s.PlaneP ] do
              yield g "Plane.weight" what [ "x", PL plane ]
              yield g "Plane.normalise" what [ "x", PL plane ]
              yield g "Plane.normal" what [ "x", PL plane ]
              yield g "Plane.distance" what [ "x", PL plane ]
              yield g "Plane.nearestPoint" what [ "x", PL plane ]
              yield g "Plane.kind" what [ "tau", zero; "x", PL plane ]
              yield g "Plane.signedDistance" what [ "p", P s.S; "x", PL plane ]
              yield g "Plane.side" what [ "tau", zero; "p", P s.T; "x", PL plane ]

          yield g "Plane.meet" "" [ "x", PL s.PlaneP; "y", PL s.PlaneQ ]
          yield g "Plane.meet" "through-points-" [ "x", PL s.PlaneT; "y", PL s.PlaneP ]
          yield g "Plane.toMultivector" "" [ "x", PL s.PlaneT ]
          yield g "Plane.ofMultivector" "" [ "a", any ]

          yield g "Sphere.through" "" [ "p", P s.P; "q", P s.Q; "r", P s.R; "s", P s.S ]
          yield g "Sphere.ofCentreRadius" "" [ "centre", T s.C1; "radius", N s.Rho1 ]

          for what, sphere in [ "", s.SphereT; "from-parameters-", s.SphereP ] do
              yield g "Sphere.weight" what [ "x", S sphere ]
              yield g "Sphere.normalise" what [ "x", S sphere ]
              yield g "Sphere.centre" what [ "x", S sphere ]
              yield g "Sphere.squaredRadius" what [ "x", S sphere ]
              yield g "Sphere.kind" what [ "tau", zero; "x", S sphere ]
              yield g "Sphere.power" what [ "p", P s.T; "x", S sphere ]
              yield g "Sphere.side" what [ "tau", zero; "p", P s.T; "x", S sphere ]

          yield g "Sphere.meet" "" [ "x", S s.SphereP; "y", S s.SphereQ ]
          yield g "Sphere.meet" "through-points-" [ "x", S s.SphereT; "y", S s.SphereP ]
          yield g "Sphere.meetPlane" "" [ "x", S s.SphereP; "y", PL s.PlaneP ]
          yield g "Sphere.meetPlane" "through-points-" [ "x", S s.SphereT; "y", PL s.PlaneT ]
          yield g "Sphere.toMultivector" "" [ "x", S s.SphereT ]
          yield g "Sphere.ofMultivector" "" [ "a", any ] ]

    let private conformalGeneral: Case list =
        [ for k, s, coplanarWithin in scenes do
              yield! sceneCases k s coplanarWithin ]

    /// The drawn configuration of this number, at one of three scales of coordinate in turn: the sample a
    /// search for a wrong port's reach draws from (the suite states it), and where the vectors of
    /// `drawnWitnesses` come from.
    let drawn (index: int) : Case list =
        let centre, spread, coplanarWithin =
            List.item (index % 3) [ 0, 1, pow2 -30; 7, 5, pow2 -20; -6, 3, pow2 -60 ]

        sceneCases (1000 + index) (Scene(9000 + index, centre, spread)) coplanarWithin

    // ---- the hard inputs: each case, each refusal and each boundary by name ----------------------

    let private nan = Double.NaN
    let private inf = Double.PositiveInfinity
    let private least = 5e-324

    /// The double below `x`, given the exponent of the last place of the binade under it.
    let private below (x: float) (lastPlace: int) : float = x - pow2 lastPlace

    let private origin = u 0.0 0.0 0.0
    let private unitSphere = ok (Sphere.ofCentreRadius (0.0, 0.0, 0.0) 1.0)
    let private xAxis = ok (Line.ofPointDirection (0.0, 0.0, 0.0) (1.0, 0.0, 0.0))
    let private yAxis = ok (Line.ofPointDirection (0.0, 0.0, 0.0) (0.0, 1.0, 0.0))
    let private floor0 = ok (Plane.ofNormalDistance (0.0, 0.0, 1.0) 0.0)

    let private unitCircle =
        Circle.through (u 1.0 0.0 0.0) (u 0.0 1.0 0.0) (u -1.0 0.0 0.0)

    let private clockwise =
        Circle.through (u -1.0 0.0 0.0) (u 0.0 1.0 0.0) (u 1.0 0.0 0.0)

    let private collinear =
        Circle.through (u 0.0 0.0 0.0) (u 1.0 2.0 3.0) (u 2.0 4.0 6.0)

    let private zeroPair = PointPair.through (u 1.0 2.0 3.0) (u 1.0 2.0 3.0)
    let private zeroLine = Line.through (u 1.0 2.0 3.0) (u 1.0 2.0 3.0)
    let private pairOnX = PointPair.through (u -1.0 0.0 0.0) (u 1.0 0.0 0.0)

    /// The line `x2 = h` in the plane `x3 = 0`, along `e1`.
    let private lineAt (h: float) : Line =
        ok (Line.ofPointDirection (0.0, h, 0.0) (1.0, 0.0, 0.0))

    let private planeAt (h: float) : Plane =
        ok (Plane.ofNormalDistance (0.0, 0.0, 1.0) h)

    let private secant =
        Line.meetSphere xAxis (ok (Sphere.ofCentreRadius (0.0, 0.0, 0.0) 5.0))

    let private tangentPair = Line.meetSphere (lineAt 1.0) unitSphere
    let private imaginaryPair = Line.meetSphere (lineAt 2.0) unitSphere

    let private flatPoint =
        Line.meetPlane xAxis (ok (Plane.ofNormalDistance (1.0, 0.0, 0.0) 3.0))

    let private farSphere = ok (Sphere.ofCentreRadius (3.0, 0.0, 0.0) 1.0)
    let private imaginaryCircle = Sphere.meet unitSphere farSphere
    let private parallelPlanes = Plane.meet floor0 (planeAt 1.0)

    let private coplanar =
        Sphere.through (u 0.0 0.0 0.0) (u 1.0 0.0 0.0) (u 0.0 1.0 0.0) (u 1.0 2.0 0.0)

    /// Four points of one circle: their outer product is the zero blade.
    let private concyclic =
        Sphere.through (u 0.0 0.0 0.0) (u 1.0 0.0 0.0) (u 0.0 1.0 0.0) (u 1.0 1.0 0.0)

    /// A blade of a type that holds `value` at one of its coefficients and `+0.0` at the others.
    let private only (size: int) (position: int) (value: float) : float[] =
        Array.init size (fun k -> if k = position then value else 0.0)

    /// The first three coefficients, then seven of one value: a point pair by its `n0` group.
    let private pairFrom (a: float) (b: float) (c: float) (rest: float) : float[] =
        Array.append [| a; b; c |] (Array.create 7 rest)

    /// What a threshold is refused for, and the negative zero it is not, for a function that takes one.
    let private thresholds (op: string) (rest: (string * Operand) list) : Case list =
        [ for what, tau in [ "negative", -1.0; "nan", nan; "infinite", inf; "negative-zero", -0.0 ] ->
              case (sprintf "%s-with-a-%s-threshold" op what) op (("tau", N tau) :: rest) ]

    /// What a division by a weight refuses: the coefficient at `position` set to each value that cannot
    /// divide.
    let private divisors (op: string) (values: float[]) (position: int) : Case list =
        [ for what, w in [ "zero", 0.0; "negative-zero", -0.0; "infinite", inf; "nan", nan ] ->
              case
                  (sprintf "%s-of-a-%s-weight" op what)
                  op
                  [ "x", Coefficients(values |> Array.mapi (fun k v -> if k = position then w else v)) ] ]

    let private pointHard: Case list =
        let weightThree = [| 3.0; 1.0; 5.0; 7.0; 12.5 |]
        let negative = [| -2.0; 1.0; -0.0; 3.0; 2.5 |]

        [ yield case "Point.up-of-the-origin" "Point.up" [ "x", T(0.0, 0.0, 0.0) ]
          yield case "Point.up-of-negative-zeros" "Point.up" [ "x", T(-0.0, -0.0, -0.0) ]
          // `(1 + 9·2^-56) + 9·2^-56` is `1 + 2^-51`, and `1 + (9·2^-56 + 9·2^-56)` is `1 + 2^-52`.
          yield case "Point.up-sums-left-to-right" "Point.up" [ "x", T(1.0, 3.0 * pow2 -28, 3.0 * pow2 -28) ]
          yield case "Point.up-whose-square-overflows" "Point.up" [ "x", T(1e200, 0.0, 0.0) ]
          yield case "Point.up-whose-square-is-subnormal" "Point.up" [ "x", T(pow2 -530, 3.0 * pow2 -532, 0.0) ]
          yield case "Point.up-of-a-nan" "Point.up" [ "x", T(0.0, nan, 0.0) ]
          yield case "Point.up-of-an-infinity" "Point.up" [ "x", T(inf, 0.0, 0.0) ]
          yield case "Point.up-of-a-negative-infinity" "Point.up" [ "x", T(0.0, 0.0, -inf) ]
          yield
              case
                  "Point.coordinates-divides-and-never-multiplies-by-a-reciprocal"
                  "Point.coordinates"
                  [ "x", Coefficients weightThree ]
          yield case "Point.coordinates-of-a-negative-weight" "Point.coordinates" [ "x", Coefficients negative ]
          yield
              case
                  "Point.coordinates-that-overflow"
                  "Point.coordinates"
                  [ "x", Coefficients [| pow2 -600; pow2 600; 0.0; 0.0; 0.0 |] ]
          yield
              case
                  "Point.coordinates-of-a-nan-coordinate"
                  "Point.coordinates"
                  [ "x", Coefficients [| 2.0; 1.0; nan; 3.0; 0.0 |] ]
          yield! divisors "Point.coordinates" weightThree 0
          yield case "Point.weight-is-signed" "Point.weight" [ "x", Coefficients [| -3.0; 1.0; 2.0; 3.0; 4.0 |] ]
          yield case "Point.weight-of-a-nan" "Point.weight" [ "x", Coefficients [| nan; 1.0; 2.0; 3.0; 4.0 |] ]
          yield
              case
                  "Point.normalise-divides-and-never-multiplies-by-a-reciprocal"
                  "Point.normalise"
                  [ "x", Coefficients weightThree ]
          yield case "Point.normalise-of-a-negative-weight" "Point.normalise" [ "x", Coefficients negative ]
          yield
              case
                  "Point.normalise-of-a-nan-coefficient"
                  "Point.normalise"
                  [ "x", Coefficients [| 2.0; 1.0; 1.0; 3.0; nan |] ]
          yield! divisors "Point.normalise" weightThree 0
          yield
              case
                  "Point.toMultivector-keeps-each-zero"
                  "Point.toMultivector"
                  [ "x", Coefficients [| -0.0; 0.0; -0.0; 2.0; -3.0 |] ]
          yield case "Point.ofMultivector-of-the-powers-of-two" "Point.ofMultivector" [ "a", PowersOfTwo ] ]

    let private pairHard: Case list =
        let threeFour = pairFrom 3.0 4.0 0.0 0.0

        [ yield
              case
                  "PointPair.through-two-unit-points"
                  "PointPair.through"
                  [ "p", P(u -1.0 0.0 0.0); "q", P(u 1.0 0.0 0.0) ]
          yield
              case
                  "PointPair.through-one-point-twice"
                  "PointPair.through"
                  [ "p", P(u 1.0 2.0 3.0); "q", P(u 1.0 2.0 3.0) ]
          // The norm is the scaled one: these squares overflow, and these underflow.
          yield
              case
                  "PointPair.weight-whose-squares-overflow"
                  "PointPair.weight"
                  [ "x", Coefficients(pairFrom 1e200 -1e200 1e200 0.0) ]
          yield
              case
                  "PointPair.weight-whose-squares-underflow"
                  "PointPair.weight"
                  [ "x", Coefficients(pairFrom 3e-200 4e-200 0.0 0.0) ]
          yield
              case
                  "PointPair.weight-three-four-five"
                  "PointPair.weight"
                  [ "x", Coefficients(pairFrom 3.0 -4.0 0.0 9.0) ]
          yield case "PointPair.weight-of-a-nan" "PointPair.weight" [ "x", Coefficients(pairFrom 3.0 nan inf 0.0) ]
          yield
              case "PointPair.weight-of-an-infinity" "PointPair.weight" [ "x", Coefficients(pairFrom 3.0 -inf 0.0 0.0) ]
          yield case "PointPair.weight-of-a-flat-point" "PointPair.weight" [ "x", PP flatPoint ]
          yield case "PointPair.normalise-of-a-flat-point" "PointPair.normalise" [ "x", PP flatPoint ]
          yield
              case
                  "PointPair.normalise-of-an-infinite-weight"
                  "PointPair.normalise"
                  [ "x", Coefficients(pairFrom inf 1.0 1.0 1.0) ]
          yield
              case
                  "PointPair.normalise-three-four-five"
                  "PointPair.normalise"
                  [ "x", Coefficients(pairFrom 3.0 -4.0 0.0 9.0) ]
          yield
              case
                  "PointPair.normalise-of-a-nan-coefficient"
                  "PointPair.normalise"
                  [ "x", Coefficients(pairFrom 3.0 -4.0 0.0 nan) ]

          for what, pair in
              [ "of-two-unit-points", pairOnX
                "of-a-tangent-pair", tangentPair
                "of-an-imaginary-pair", imaginaryPair
                "of-a-flat-point", flatPoint
                "of-the-zero-blade", zeroPair ] do
              yield case ("PointPair.centre-" + what) "PointPair.centre" [ "x", PP pair ]
              yield case ("PointPair.squaredRadius-" + what) "PointPair.squaredRadius" [ "x", PP pair ]
              yield case ("PointPair.points-" + what) "PointPair.points" [ "tau", N 0.0; "x", PP pair ]
              yield case ("PointPair.kind-" + what) "PointPair.kind" [ "tau", N 0.0; "x", PP pair ]

          yield case "PointPair.points-run-along-the-line" "PointPair.points" [ "tau", N 0.0; "x", PP secant ]
          yield
              case
                  "PointPair.points-of-a-weighted-pair"
                  "PointPair.points"
                  [ "tau", N 0.0
                    "x", PP(PointPair.through (weighted -3.0 (u 1.0 2.0 2.0)) (weighted 0.5 (u 3.0 -2.0 6.0))) ]
          // The squared radius of the pair on the first axis is `1`, and of the imaginary pair `−3`.
          yield
              case
                  "PointPair.points-real-just-above-the-threshold"
                  "PointPair.points"
                  [ "tau", N(below 1.0 -53); "x", PP pairOnX ]
          yield case "PointPair.points-tangent-at-the-threshold" "PointPair.points" [ "tau", N 1.0; "x", PP pairOnX ]
          yield
              case
                  "PointPair.points-imaginary-just-below-the-threshold"
                  "PointPair.points"
                  [ "tau", N(below 3.0 -51); "x", PP imaginaryPair ]
          yield
              case
                  "PointPair.points-tangent-at-the-negative-threshold"
                  "PointPair.points"
                  [ "tau", N 3.0; "x", PP imaginaryPair ]
          yield
              case
                  "PointPair.points-of-a-nan-coefficient"
                  "PointPair.points"
                  [ "tau", N 0.0; "x", Coefficients(pairFrom 3.0 -4.0 0.0 nan) ]
          yield! thresholds "PointPair.points" [ "x", PP pairOnX ]
          yield
              case
                  "PointPair.kind-at-infinity"
                  "PointPair.kind"
                  [ "tau", N 0.0; "x", PP(Line.meetPlane xAxis (planeAt 1.0)) ]
          yield
              case
                  "PointPair.kind-of-a-euclidean-bivector"
                  "PointPair.kind"
                  [ "tau", N 0.0; "x", Coefficients(only 10 4 1.0) ]
          yield
              case
                  "PointPair.kind-of-a-nan-coefficient"
                  "PointPair.kind"
                  [ "tau", N 0.0; "x", Coefficients(only 10 9 nan) ]
          yield
              case
                  "PointPair.kind-real-just-above-the-threshold"
                  "PointPair.kind"
                  [ "tau", N(below 1.0 -53); "x", PP pairOnX ]
          yield case "PointPair.kind-tangent-at-the-threshold" "PointPair.kind" [ "tau", N 1.0; "x", PP pairOnX ]
          // Norms of `5`, `2` and `1` on three of the groups, each against itself and the double below.
          yield
              case
                  "PointPair.kind-round-just-above-the-threshold"
                  "PointPair.kind"
                  [ "tau", N(below 5.0 -50); "x", Coefficients threeFour ]
          yield
              case
                  "PointPair.kind-no-round-at-the-threshold"
                  "PointPair.kind"
                  [ "tau", N 5.0; "x", Coefficients threeFour ]
          yield
              case
                  "PointPair.kind-flat-just-above-the-threshold"
                  "PointPair.kind"
                  [ "tau", N(below 2.0 -52); "x", Coefficients(only 10 3 2.0) ]
          yield
              case
                  "PointPair.kind-no-flat-at-the-threshold"
                  "PointPair.kind"
                  [ "tau", N 2.0; "x", Coefficients(only 10 3 2.0) ]
          yield
              case
                  "PointPair.kind-at-infinity-just-above-the-threshold"
                  "PointPair.kind"
                  [ "tau", N(below 1.0 -53); "x", Coefficients(only 10 6 -1.0) ]
          yield
              case
                  "PointPair.kind-zero-at-the-threshold"
                  "PointPair.kind"
                  [ "tau", N 1.0; "x", Coefficients(only 10 6 -1.0) ]
          yield! thresholds "PointPair.kind" [ "x", PP pairOnX ]
          yield
              case
                  "PointPair.toMultivector-keeps-each-zero"
                  "PointPair.toMultivector"
                  [ "x", Coefficients [| -0.0; 1.0; 0.0; -2.0; -0.0; 3.0; 0.0; -4.0; -0.0; 5.0 |] ]
          yield case "PointPair.ofMultivector-of-the-powers-of-two" "PointPair.ofMultivector" [ "a", PowersOfTwo ] ]

    let private lineHard: Case list =
        let op = "Line.ofPointDirection"
        let slanted = [| 3.0; 0.0; -4.0; 7.0; 7.0; 7.0 |]
        let withNaN = [| 1.0; 0.0; 0.0; nan; 0.0; 0.0 |]
        let crossing = ok (Line.ofPointDirection (2.0, -1.0, 0.0) (0.0, 1.0, 0.0))
        let lifted = ok (Line.ofPointDirection (0.0, 0.0, 1.0) (0.0, 1.0, 0.0))

        [ yield case "Line.through-two-unit-points" "Line.through" [ "p", P(u 1.0 1.0 0.0); "q", P(u 4.0 5.0 0.0) ]
          yield case "Line.through-one-point-twice" "Line.through" [ "p", P(u 1.0 2.0 3.0); "q", P(u 1.0 2.0 3.0) ]
          yield case (op + "-along-the-third-axis") op [ "p", T(1.0, 2.0, 0.0); "d", T(0.0, 0.0, 2.0) ]
          yield
              case
                  (op + "-divides-and-never-multiplies-by-a-reciprocal")
                  op
                  [ "p", T(1.0, 2.0, 3.0); "d", T(1.0, 2.0, 3.0) ]
          yield case (op + "-of-the-least-direction") op [ "p", T(0.0, 1.0, 0.0); "d", T(least, 0.0, 0.0) ]
          yield case (op + "-of-a-large-direction") op [ "p", T(0.0, 1.0, 0.0); "d", T(1e200, -1e200, 1e200) ]
          yield case (op + "-of-a-nan-coordinate") op [ "p", T(nan, 0.0, 0.0); "d", T(1.0, 0.0, 0.0) ]
          yield case (op + "-of-an-infinite-coordinate") op [ "p", T(0.0, 0.0, -inf); "d", T(1.0, 0.0, 0.0) ]
          yield case (op + "-of-a-zero-direction") op [ "p", T(0.0, 0.0, 0.0); "d", T(0.0, -0.0, 0.0) ]
          yield case (op + "-of-a-nan-direction") op [ "p", T(0.0, 0.0, 0.0); "d", T(1.0, nan, 0.0) ]
          yield case (op + "-of-an-infinite-direction") op [ "p", T(0.0, 0.0, 0.0); "d", T(1.0, inf, 0.0) ]
          yield case (op + "-whose-norm-overflows") op [ "p", T(0.0, 0.0, 0.0); "d", T(1.5e308, 1.5e308, 1.5e308) ]
          yield case (op + "-refuses-the-point-first") op [ "p", T(nan, 0.0, 0.0); "d", T(0.0, 0.0, 0.0) ]
          yield case "Line.weight-three-four-five" "Line.weight" [ "x", Coefficients slanted ]
          yield
              case
                  "Line.weight-whose-squares-overflow"
                  "Line.weight"
                  [ "x", Coefficients [| 1e200; 1e200; 1e200; 0.0; 0.0; 0.0 |] ]
          yield case "Line.weight-of-a-line-at-infinity" "Line.weight" [ "x", L parallelPlanes ]
          yield case "Line.normalise-three-four-five" "Line.normalise" [ "x", Coefficients slanted ]
          yield case "Line.normalise-of-a-line-at-infinity" "Line.normalise" [ "x", L parallelPlanes ]
          yield
              case
                  "Line.normalise-of-an-infinite-weight"
                  "Line.normalise"
                  [ "x", Coefficients [| 3.0; inf; -4.0; 7.0; 7.0; 7.0 |] ]
          yield
              case "Line.direction-is-read" "Line.direction" [ "x", Coefficients [| -0.0; 2.0; -3.0; 5.0; 7.0; 11.0 |] ]
          yield case "Line.moment-is-read" "Line.moment" [ "x", Coefficients [| -0.0; 2.0; -3.0; 5.0; -0.0; 11.0 |] ]
          yield
              case
                  "Line.nearestPoint-of-a-line-beside-the-origin"
                  "Line.nearestPoint"
                  [ "x", L(Line.through (u 1.0 1.0 0.0) (u 4.0 1.0 0.0)) ]
          yield case "Line.nearestPoint-of-a-line-through-the-origin" "Line.nearestPoint" [ "x", L yAxis ]
          yield case "Line.nearestPoint-of-the-zero-blade" "Line.nearestPoint" [ "x", L zeroLine ]
          yield case "Line.nearestPoint-of-a-line-at-infinity" "Line.nearestPoint" [ "x", L parallelPlanes ]
          yield case "Line.nearestPoint-of-a-nan-coefficient" "Line.nearestPoint" [ "x", Coefficients withNaN ]
          yield case "Line.kind-of-a-line" "Line.kind" [ "tau", N 0.0; "x", L xAxis ]
          yield case "Line.kind-at-infinity" "Line.kind" [ "tau", N 0.0; "x", L parallelPlanes ]
          yield case "Line.kind-of-the-zero-blade" "Line.kind" [ "tau", N 0.0; "x", L zeroLine ]
          yield case "Line.kind-of-a-nan-coefficient" "Line.kind" [ "tau", N 0.0; "x", Coefficients withNaN ]
          // A direction of norm `5` and a moment of norm `6`.
          yield
              case
                  "Line.kind-flat-just-above-the-threshold"
                  "Line.kind"
                  [ "tau", N(below 5.0 -50)
                    "x", Coefficients [| 3.0; 0.0; -4.0; 0.0; 0.0; 6.0 |] ]
          yield
              case
                  "Line.kind-at-infinity-at-the-threshold"
                  "Line.kind"
                  [ "tau", N 5.0; "x", Coefficients [| 3.0; 0.0; -4.0; 0.0; 0.0; 6.0 |] ]
          yield
              case
                  "Line.kind-zero-at-the-threshold"
                  "Line.kind"
                  [ "tau", N 6.0; "x", Coefficients [| 3.0; 0.0; -4.0; 0.0; 0.0; 6.0 |] ]
          yield! thresholds "Line.kind" [ "x", L xAxis ]
          yield case "Line.meet-of-two-lines-that-cross" "Line.meet" [ "tau", N 0.0; "x", L xAxis; "y", L crossing ]
          yield
              case
                  "Line.meet-through-a-point-of-the-first-line"
                  "Line.meet"
                  [ "tau", N 0.0; "x", L xAxis; "y", L yAxis ]
          yield case "Line.meet-of-two-parallel-lines" "Line.meet" [ "tau", N 0.0; "x", L xAxis; "y", L(lineAt 2.0) ]
          yield case "Line.meet-of-one-line-twice" "Line.meet" [ "tau", N 0.0; "x", L(lineAt 2.0); "y", L(lineAt 2.0) ]
          yield
              case
                  "Line.meet-of-a-line-and-its-opposite"
                  "Line.meet"
                  [ "tau", N 0.0
                    "x", L(lineAt 2.0)
                    "y", Coefficients(times -2.0 (lineValues (lineAt 2.0))) ]
          yield case "Line.meet-of-two-skew-lines" "Line.meet" [ "tau", N 0.0; "x", L xAxis; "y", L lifted ]
          // The skew pair's meet in the whole space has one coefficient, of magnitude `1`.
          yield
              case
                  "Line.meet-skew-just-above-the-threshold"
                  "Line.meet"
                  [ "tau", N(below 1.0 -53); "x", L xAxis; "y", L lifted ]
          yield case "Line.meet-not-skew-at-the-threshold" "Line.meet" [ "tau", N 1.0; "x", L xAxis; "y", L lifted ]
          // The parallel pair's span has weight `2`.
          yield
              case
                  "Line.meet-parallel-just-above-the-threshold"
                  "Line.meet"
                  [ "tau", N(below 2.0 -52); "x", L xAxis; "y", L(lineAt 2.0) ]
          yield
              case "Line.meet-one-line-at-the-threshold" "Line.meet" [ "tau", N 2.0; "x", L xAxis; "y", L(lineAt 2.0) ]
          yield case "Line.meet-with-the-zero-blade" "Line.meet" [ "tau", N 0.0; "x", L xAxis; "y", L zeroLine ]
          yield
              case
                  "Line.meet-with-a-nan-coefficient"
                  "Line.meet"
                  [ "tau", N 0.0; "x", L xAxis; "y", Coefficients withNaN ]
          yield! thresholds "Line.meet" [ "x", L xAxis; "y", L crossing ]
          yield
              case
                  "Line.meetPlane-where-it-crosses"
                  "Line.meetPlane"
                  [ "x", L xAxis; "y", PL(ok (Plane.ofNormalDistance (1.0, 0.0, 0.0) 3.0)) ]
          yield case "Line.meetPlane-parallel-to-it" "Line.meetPlane" [ "x", L xAxis; "y", PL(planeAt 1.0) ]
          yield case "Line.meetPlane-within-it" "Line.meetPlane" [ "x", L xAxis; "y", PL floor0 ]
          yield
              case
                  "Line.meetSphere-secant"
                  "Line.meetSphere"
                  [ "x", L xAxis; "y", S(ok (Sphere.ofCentreRadius (0.0, 0.0, 0.0) 5.0)) ]
          yield case "Line.meetSphere-tangent" "Line.meetSphere" [ "x", L(lineAt 1.0); "y", S unitSphere ]
          yield case "Line.meetSphere-missing-it" "Line.meetSphere" [ "x", L(lineAt 2.0); "y", S unitSphere ]
          yield
              case
                  "Line.toMultivector-keeps-each-zero"
                  "Line.toMultivector"
                  [ "x", Coefficients [| -0.0; 2.0; -3.0; 0.0; -0.0; 11.0 |] ]
          yield case "Line.ofMultivector-of-the-powers-of-two" "Line.ofMultivector" [ "a", PowersOfTwo ] ]

    let private circleHard: Case list =
        let op = "Circle.ofCentreRadiusNormal"
        let third = T(0.0, 0.0, 1.0)
        let centre = T(0.0, 0.0, 0.0)
        let off = T(1.0, 2.0, 3.0)
        let flatAndFar = [| 0.0; 0.0; 3.0; 0.0; 4.0; 0.0; 0.0; 0.0; 9.0; 0.0 |]
        // A circle of radius `2^-540` and weight `2^300`, and a point at `2^-539` from its centre, of
        // weight `2^300`: the power is `3·2^-1080`, which no double holds, and its sign is plain.
        let tiny =
            circleValues (ok (Circle.ofCentreRadiusNormal (0.0, 0.0, 0.0) 1.0 (0.0, 0.0, 1.0)))
            |> Array.mapi (fun k v -> if k = 0 then v * pow2 300 else (v * 2.0) * pow2 -781)

        let near = [| pow2 300; pow2 -239; 0.0; 0.0; pow2 -779 |]

        [ yield
              case
                  "Circle.through-three-unit-points"
                  "Circle.through"
                  [ "p", P(u 1.0 0.0 0.0); "q", P(u 0.0 1.0 0.0); "r", P(u -1.0 0.0 0.0) ]
          yield
              case
                  "Circle.through-three-collinear-points"
                  "Circle.through"
                  [ "p", P(u 0.0 0.0 0.0); "q", P(u 1.0 2.0 3.0); "r", P(u 2.0 4.0 6.0) ]
          yield
              case
                  "Circle.through-one-point-twice"
                  "Circle.through"
                  [ "p", P(u 1.0 2.0 3.0); "q", P(u 1.0 2.0 3.0); "r", P(u 0.0 0.0 1.0) ]
          yield case (op + "-about-the-third-axis") op [ "centre", centre; "radius", N 1.0; "normal", T(0.0, 0.0, 4.0) ]
          yield case (op + "-of-radius-zero") op [ "centre", off; "radius", N 0.0; "normal", third ]
          yield case (op + "-of-radius-negative-zero") op [ "centre", off; "radius", N -0.0; "normal", third ]
          // `r × r` overflows here, where `(0.5 × r) × r` would not: the sphere holds an infinity, and
          // its meet with the plane a NaN, which is refused.
          yield case (op + "-squares-the-radius-first") op [ "centre", centre; "radius", N 1.5e154; "normal", third ]
          yield case (op + "-of-a-nan-coordinate") op [ "centre", T(0.0, nan, 0.0); "radius", N 1.0; "normal", third ]
          yield case (op + "-of-the-least-negative-radius") op [ "centre", centre; "radius", N -least; "normal", third ]
          yield case (op + "-of-a-nan-radius") op [ "centre", centre; "radius", N nan; "normal", third ]
          yield case (op + "-of-an-infinite-radius") op [ "centre", centre; "radius", N inf; "normal", third ]
          yield case (op + "-of-a-negative-infinite-radius") op [ "centre", centre; "radius", N -inf; "normal", third ]
          yield case (op + "-of-a-zero-normal") op [ "centre", centre; "radius", N 1.0; "normal", T(0.0, 0.0, 0.0) ]
          yield case (op + "-of-a-nan-normal") op [ "centre", centre; "radius", N 1.0; "normal", T(nan, 0.0, 1.0) ]
          yield
              case
                  (op + "-refuses-the-radius-before-the-normal")
                  op
                  [ "centre", centre; "radius", N -1.0; "normal", T(0.0, 0.0, 0.0) ]
          yield
              case
                  (op + "-whose-arithmetic-overflows")
                  op
                  [ "centre", T(1e200, 0.0, 0.0); "radius", N 1e200; "normal", third ]

          for what, circle in
              [ "of-the-unit-circle", unitCircle
                "of-a-clockwise-circle", clockwise
                "of-a-point", ok (Circle.ofCentreRadiusNormal (1.0, 2.0, 3.0) 0.0 (0.0, 0.0, 1.0))
                "of-two-spheres-apart", imaginaryCircle
                "of-three-collinear-points", collinear
                "of-the-zero-blade", Sphere.meet unitSphere unitSphere ] do
              yield case ("Circle.weight-" + what) "Circle.weight" [ "x", C circle ]
              yield case ("Circle.normalise-" + what) "Circle.normalise" [ "x", C circle ]
              yield case ("Circle.centre-" + what) "Circle.centre" [ "x", C circle ]
              yield case ("Circle.squaredRadius-" + what) "Circle.squaredRadius" [ "x", C circle ]
              yield case ("Circle.kind-" + what) "Circle.kind" [ "tau", N 0.0; "x", C circle ]
              yield case ("Circle.plane-" + what) "Circle.plane" [ "x", C circle ]

          yield case "Circle.kind-at-infinity" "Circle.kind" [ "tau", N 0.0; "x", Coefficients(only 10 7 1.0) ]
          yield
              case
                  "Circle.kind-of-a-euclidean-trivector"
                  "Circle.kind"
                  [ "tau", N 0.0; "x", Coefficients(only 10 6 1.0) ]
          yield case "Circle.kind-of-a-nan-coefficient" "Circle.kind" [ "tau", N 0.0; "x", Coefficients(only 10 0 nan) ]
          // The imaginary circle's squared radius is `−5/4`.
          yield
              case
                  "Circle.kind-imaginary-just-below-the-threshold"
                  "Circle.kind"
                  [ "tau", N(below 1.25 -52); "x", C imaginaryCircle ]
          yield
              case
                  "Circle.kind-tangent-at-the-negative-threshold"
                  "Circle.kind"
                  [ "tau", N 1.25; "x", C imaginaryCircle ]
          // A direction of norm `5` and a moment of norm `9`; then with `2` on `e1∧e2∧e3`.
          yield
              case
                  "Circle.kind-flat-just-above-the-threshold"
                  "Circle.kind"
                  [ "tau", N(below 5.0 -50); "x", Coefficients flatAndFar ]
          yield
              case
                  "Circle.kind-at-infinity-at-the-threshold"
                  "Circle.kind"
                  [ "tau", N 5.0; "x", Coefficients flatAndFar ]
          yield
              case
                  "Circle.kind-no-object-above-the-threshold"
                  "Circle.kind"
                  [ "tau", N(below 2.0 -52)
                    "x", Coefficients [| 0.0; 0.0; 3.0; 0.0; 4.0; 0.0; 2.0; 0.0; 9.0; 0.0 |] ]
          yield
              case
                  "Circle.kind-flat-at-the-threshold-of-the-euclidean-blade"
                  "Circle.kind"
                  [ "tau", N 2.0
                    "x", Coefficients [| 0.0; 0.0; 3.0; 0.0; 4.0; 0.0; 2.0; 0.0; 9.0; 0.0 |] ]
          yield! thresholds "Circle.kind" [ "x", C unitCircle ]

          // In the plane of the unit circle: the origin has power `−1`, `(2, 0, 0)` has `3`, and
          // `(0, −1, 0)` is on it. The sign is the same whatever the order of the circle's points.
          for order, circle in [ "", unitCircle; "-of-a-clockwise-circle", clockwise ] do
              for what, p in [ "inside", origin; "outside", u 2.0 0.0 0.0; "on", u 0.0 -1.0 0.0 ] do
                  yield case (sprintf "Circle.power-%s%s" what order) "Circle.power" [ "p", P p; "x", C circle ]

                  yield
                      case
                          (sprintf "Circle.side-%s%s" what order)
                          "Circle.side"
                          [ "tau", N 0.0; "p", P p; "x", C circle ]

          yield
              case
                  "Circle.power-of-a-negative-weight"
                  "Circle.power"
                  [ "p", P(weighted -2.0 (u 0.0 -1.0 0.0)); "x", C unitCircle ]
          yield
              case
                  "Circle.side-inside-just-below-the-threshold"
                  "Circle.side"
                  [ "tau", N(below 1.0 -53); "p", P origin; "x", C unitCircle ]
          yield
              case
                  "Circle.side-on-at-the-negative-threshold"
                  "Circle.side"
                  [ "tau", N 1.0; "p", P origin; "x", C unitCircle ]
          yield
              case
                  "Circle.side-outside-just-above-the-threshold"
                  "Circle.side"
                  [ "tau", N(below 3.0 -51); "p", P(u 2.0 0.0 0.0); "x", C unitCircle ]
          yield
              case
                  "Circle.side-on-at-the-threshold"
                  "Circle.side"
                  [ "tau", N 3.0; "p", P(u 2.0 0.0 0.0); "x", C unitCircle ]
          yield case "Circle.power-that-underflows" "Circle.power" [ "p", Coefficients near; "x", Coefficients tiny ]
          yield
              case
                  "Circle.side-decided-by-signs-where-the-quotient-underflows"
                  "Circle.side"
                  [ "tau", N 0.0; "p", Coefficients near; "x", Coefficients tiny ]

          for name in [ "Circle.power"; "Circle.side" ] do
              let tau = if name = "Circle.side" then [ "tau", N 0.0 ] else []

              yield
                  case
                      (name + "-of-a-zero-weight-point")
                      name
                      (tau @ [ "p", Coefficients [| 0.0; 1.0; 0.0; 0.0; 0.5 |]; "x", C unitCircle ])

              yield case (name + "-against-three-collinear-points") name (tau @ [ "p", P origin; "x", C collinear ])

              yield
                  case
                      (name + "-of-an-infinite-weight-point")
                      name
                      (tau @ [ "p", Coefficients [| inf; 1.0; 0.0; 0.0; 0.5 |]; "x", C unitCircle ])

              yield
                  case
                      (name + "-of-a-nan-coordinate")
                      name
                      (tau @ [ "p", Coefficients [| 1.0; nan; 0.0; 0.0; 0.5 |]; "x", C unitCircle ])

          yield! thresholds "Circle.side" [ "p", P origin; "x", C unitCircle ]
          yield
              case
                  "Circle.toMultivector-keeps-each-zero"
                  "Circle.toMultivector"
                  [ "x", Coefficients [| -0.0; 1.0; 0.0; -2.0; -0.0; 3.0; 0.0; -4.0; -0.0; 5.0 |] ]
          yield case "Circle.ofMultivector-of-the-powers-of-two" "Circle.ofMultivector" [ "a", PowersOfTwo ] ]

    let private planeHard: Case list =
        let op = "Plane.ofNormalDistance"
        let slanted = [| 3.0; 0.0; -4.0; 10.0 |]
        let far = [| 0.0; 0.0; 0.0; 10.0 |]
        // A point of weight `2^200` at `2^-1080` from a plane of weight `2^200`.
        let grazing = [| pow2 200; pow2 -880; 0.0; 0.0; 0.0 |]
        let heavy = [| 0.0; 0.0; pow2 200; 0.0 |]

        [ yield
              case
                  "Plane.through-three-unit-points"
                  "Plane.through"
                  [ "p", P(u 0.0 0.0 1.0); "q", P(u 1.0 0.0 1.0); "r", P(u 0.0 1.0 1.0) ]
          yield
              case
                  "Plane.through-three-collinear-points"
                  "Plane.through"
                  [ "p", P(u 0.0 0.0 0.0); "q", P(u 1.0 2.0 3.0); "r", P(u 2.0 4.0 6.0) ]
          yield case (op + "-above-the-origin") op [ "normal", T(0.0, 0.0, 4.0); "distance", N 1.0 ]
          yield
              case
                  (op + "-divides-and-never-multiplies-by-a-reciprocal")
                  op
                  [ "normal", T(1.0, 2.0, 3.0); "distance", N -2.0 ]
          yield case (op + "-of-a-zero-normal") op [ "normal", T(-0.0, 0.0, 0.0); "distance", N 1.0 ]
          yield case (op + "-of-a-nan-normal") op [ "normal", T(0.0, nan, 0.0); "distance", N 1.0 ]
          yield case (op + "-of-an-infinite-normal") op [ "normal", T(0.0, 1.0, -inf); "distance", N 1.0 ]
          yield case (op + "-whose-norm-overflows") op [ "normal", T(1.5e308, 1.5e308, 0.0); "distance", N 1.0 ]
          yield case (op + "-of-a-nan-distance") op [ "normal", T(0.0, 0.0, 1.0); "distance", N nan ]
          yield case (op + "-of-an-infinite-distance") op [ "normal", T(0.0, 0.0, 1.0); "distance", N -inf ]
          yield case (op + "-refuses-the-normal-first") op [ "normal", T(0.0, 0.0, 0.0); "distance", N nan ]
          yield case "Plane.weight-three-four-five" "Plane.weight" [ "x", Coefficients slanted ]
          yield case "Plane.weight-of-a-plane-at-infinity" "Plane.weight" [ "x", Coefficients far ]
          yield case "Plane.normalise-three-four-five" "Plane.normalise" [ "x", Coefficients slanted ]
          yield case "Plane.normalise-of-a-plane-at-infinity" "Plane.normalise" [ "x", Coefficients far ]
          yield
              case "Plane.normalise-of-a-nan-weight" "Plane.normalise" [ "x", Coefficients [| 3.0; nan; -4.0; 10.0 |] ]
          yield case "Plane.normal-is-read" "Plane.normal" [ "x", Coefficients [| 2.0; 0.0; -0.0; 7.0 |] ]
          yield case "Plane.distance-is-read" "Plane.distance" [ "x", Coefficients [| 2.0; 0.0; -0.0; -7.0 |] ]
          yield case "Plane.nearestPoint-of-a-plane-above-the-origin" "Plane.nearestPoint" [ "x", PL(planeAt 3.0) ]
          yield case "Plane.nearestPoint-three-four-five" "Plane.nearestPoint" [ "x", Coefficients slanted ]
          yield case "Plane.nearestPoint-of-a-plane-at-infinity" "Plane.nearestPoint" [ "x", Coefficients far ]
          yield
              case
                  "Plane.nearestPoint-of-the-zero-blade"
                  "Plane.nearestPoint"
                  [ "x", Coefficients [| 0.0; 0.0; 0.0; 0.0 |] ]
          yield case "Plane.kind-of-a-plane" "Plane.kind" [ "tau", N 0.0; "x", PL floor0 ]
          yield case "Plane.kind-at-infinity" "Plane.kind" [ "tau", N 0.0; "x", Coefficients far ]
          yield
              case
                  "Plane.kind-of-the-zero-blade"
                  "Plane.kind"
                  [ "tau", N 0.0; "x", Coefficients [| 0.0; -0.0; 0.0; 0.0 |] ]
          yield
              case
                  "Plane.kind-of-a-nan-coefficient"
                  "Plane.kind"
                  [ "tau", N 0.0; "x", Coefficients [| 0.0; 0.0; 1.0; nan |] ]
          // A normal of norm `5` and `10` on `e1∧e2∧e3∧n∞`.
          yield
              case
                  "Plane.kind-flat-just-above-the-threshold"
                  "Plane.kind"
                  [ "tau", N(below 5.0 -50); "x", Coefficients slanted ]
          yield case "Plane.kind-at-infinity-at-the-threshold" "Plane.kind" [ "tau", N 5.0; "x", Coefficients slanted ]
          yield case "Plane.kind-zero-at-the-threshold" "Plane.kind" [ "tau", N 10.0; "x", Coefficients slanted ]
          yield! thresholds "Plane.kind" [ "x", PL floor0 ]
          yield
              case
                  "Plane.meet-of-two-planes-that-cross"
                  "Plane.meet"
                  [ "x", PL floor0; "y", PL(ok (Plane.ofNormalDistance (1.0, 0.0, 0.0) 2.0)) ]
          yield case "Plane.meet-of-two-parallel-planes" "Plane.meet" [ "x", PL floor0; "y", PL(planeAt 1.0) ]
          yield case "Plane.meet-of-one-plane-twice" "Plane.meet" [ "x", PL(planeAt 1.0); "y", PL(planeAt 1.0) ]

          // Against the plane `x3 = 1`: `(0, 0, 3)` is at `2`, `(0, 0, −1)` at `−2`, `(5, 5, 1)` on it.
          for what, p in [ "above", u 0.0 0.0 3.0; "below", u 0.0 0.0 -1.0; "on", u 5.0 5.0 1.0 ] do
              yield case ("Plane.signedDistance-" + what) "Plane.signedDistance" [ "p", P p; "x", PL(planeAt 1.0) ]
              yield case ("Plane.side-" + what) "Plane.side" [ "tau", N 0.0; "p", P p; "x", PL(planeAt 1.0) ]

          yield
              case
                  "Plane.signedDistance-of-a-negative-weight"
                  "Plane.signedDistance"
                  [ "p", P(weighted -2.0 (u 5.0 5.0 1.0)); "x", PL(planeAt 1.0) ]
          yield
              case
                  "Plane.signedDistance-of-a-weighted-plane"
                  "Plane.signedDistance"
                  [ "p", P(weighted 3.0 (u 1.0 2.0 3.0)); "x", Coefficients slanted ]
          yield
              case
                  "Plane.side-positive-just-above-the-threshold"
                  "Plane.side"
                  [ "tau", N(below 2.0 -52); "p", P(u 0.0 0.0 3.0); "x", PL(planeAt 1.0) ]
          yield
              case
                  "Plane.side-on-at-the-threshold"
                  "Plane.side"
                  [ "tau", N 2.0; "p", P(u 0.0 0.0 3.0); "x", PL(planeAt 1.0) ]
          yield
              case
                  "Plane.side-negative-just-below-the-threshold"
                  "Plane.side"
                  [ "tau", N(below 2.0 -52); "p", P(u 0.0 0.0 -1.0); "x", PL(planeAt 1.0) ]
          yield
              case
                  "Plane.side-on-at-the-negative-threshold"
                  "Plane.side"
                  [ "tau", N 2.0; "p", P(u 0.0 0.0 -1.0); "x", PL(planeAt 1.0) ]
          yield
              case
                  "Plane.signedDistance-that-underflows"
                  "Plane.signedDistance"
                  [ "p", Coefficients grazing; "x", Coefficients heavy ]
          yield
              case
                  "Plane.side-decided-by-signs-where-the-quotient-underflows"
                  "Plane.side"
                  [ "tau", N 0.0; "p", Coefficients grazing; "x", Coefficients heavy ]

          for name in [ "Plane.signedDistance"; "Plane.side" ] do
              let tau = if name = "Plane.side" then [ "tau", N 0.0 ] else []

              yield
                  case
                      (name + "-of-a-zero-weight-point")
                      name
                      (tau @ [ "p", Coefficients [| -0.0; 1.0; 0.0; 0.0; 0.5 |]; "x", PL(planeAt 1.0) ])

              yield case (name + "-against-a-plane-at-infinity") name (tau @ [ "p", P origin; "x", Coefficients far ])

              yield
                  case
                      (name + "-whose-weights-underflow")
                      name
                      (tau
                       @ [ "p", Coefficients [| pow2 -600; 0.0; 0.0; pow2 -600; 0.0 |]
                           "x", Coefficients [| pow2 -600; 0.0; 0.0; 0.0 |] ])

              yield
                  case
                      (name + "-whose-weights-overflow")
                      name
                      (tau
                       @ [ "p", Coefficients [| pow2 600; 0.0; 0.0; pow2 600; 0.0 |]
                           "x", Coefficients [| pow2 600; 0.0; 0.0; 0.0 |] ])

              yield
                  case
                      (name + "-of-a-nan-coordinate")
                      name
                      (tau @ [ "p", Coefficients [| 1.0; 0.0; 0.0; nan; 0.5 |]; "x", PL(planeAt 1.0) ])

          yield! thresholds "Plane.side" [ "p", P origin; "x", PL(planeAt 1.0) ]
          yield
              case
                  "Plane.toMultivector-keeps-each-zero"
                  "Plane.toMultivector"
                  [ "x", Coefficients [| -0.0; 2.0; 0.0; -3.0 |] ]
          yield case "Plane.ofMultivector-of-the-powers-of-two" "Plane.ofMultivector" [ "a", PowersOfTwo ] ]

    let private sphereHard: Case list =
        let op = "Sphere.ofCentreRadius"
        let centre = T(0.0, 0.0, 0.0)
        let off = T(1.0, -2.0, 3.0)
        // An imaginary sphere of weight `8`: the dual of `8 (n0 + 2 n∞)`, whose squared radius is `−4`.
        let imaginary = [| 8.0; 0.0; 0.0; 0.0; 16.0 |]
        // A sphere of radius `2^-540` and weight `2^100`, and a point at `2^-539` from its centre, of
        // weight `2^100`: the power is `3·2^-1080`.
        let speck = [| pow2 100; 0.0; 0.0; 0.0; -(pow2 -981) |]
        let beside = [| pow2 100; pow2 -439; 0.0; 0.0; pow2 -979 |]

        let scaled (w: float) (sphere: Sphere) =
            sphereOf (times w (sphereValues sphere))

        [ yield
              case
                  "Sphere.through-four-unit-points"
                  "Sphere.through"
                  [ "p", P(u 1.0 0.0 0.0)
                    "q", P(u 0.0 1.0 0.0)
                    "r", P(u 0.0 0.0 1.0)
                    "s", P(u -1.0 0.0 0.0) ]
          yield
              case
                  "Sphere.through-four-coplanar-points"
                  "Sphere.through"
                  [ "p", P(u 0.0 0.0 0.0)
                    "q", P(u 1.0 0.0 0.0)
                    "r", P(u 0.0 1.0 0.0)
                    "s", P(u 1.0 2.0 0.0) ]
          yield
              case
                  "Sphere.through-four-points-of-one-circle"
                  "Sphere.through"
                  [ "p", P(u 0.0 0.0 0.0)
                    "q", P(u 1.0 0.0 0.0)
                    "r", P(u 0.0 1.0 0.0)
                    "s", P(u 1.0 1.0 0.0) ]
          yield case (op + "-at-the-origin") op [ "centre", centre; "radius", N 1.0 ]
          yield case (op + "-off-the-origin") op [ "centre", off; "radius", N 5.0 ]
          yield case (op + "-of-radius-zero") op [ "centre", off; "radius", N 0.0 ]
          yield case (op + "-of-radius-negative-zero") op [ "centre", centre; "radius", N -0.0 ]
          // `r × r` overflows here, and `(0.5 × r) × r` would not: the square is formed first.
          yield case (op + "-squares-the-radius-first") op [ "centre", centre; "radius", N 1.5e154 ]
          yield case (op + "-of-a-nan-coordinate") op [ "centre", T(nan, 0.0, 0.0); "radius", N 1.0 ]
          yield case (op + "-of-an-infinite-coordinate") op [ "centre", T(0.0, inf, 0.0); "radius", N 1.0 ]
          yield case (op + "-of-a-negative-radius") op [ "centre", centre; "radius", N -1.0 ]
          yield case (op + "-of-the-least-negative-radius") op [ "centre", centre; "radius", N -least ]
          yield case (op + "-of-a-nan-radius") op [ "centre", centre; "radius", N nan ]
          yield case (op + "-of-an-infinite-radius") op [ "centre", centre; "radius", N inf ]
          yield case (op + "-of-a-negative-infinite-radius") op [ "centre", centre; "radius", N -inf ]
          yield case (op + "-refuses-the-centre-first") op [ "centre", T(nan, 0.0, 0.0); "radius", N -1.0 ]
          yield case (op + "-whose-arithmetic-overflows") op [ "centre", T(1e200, 0.0, 0.0); "radius", N 1e200 ]

          for what, sphere in
              [ "of-the-unit-sphere", unitSphere
                "of-a-negative-weight", scaled -2.0 farSphere
                "of-a-point", ok (Sphere.ofCentreRadius (1.0, -2.0, 3.0) 0.0)
                "of-four-coplanar-points", coplanar
                "of-four-points-of-one-circle", concyclic ] do
              yield case ("Sphere.weight-" + what) "Sphere.weight" [ "x", S sphere ]
              yield case ("Sphere.normalise-" + what) "Sphere.normalise" [ "x", S sphere ]
              yield case ("Sphere.centre-" + what) "Sphere.centre" [ "x", S sphere ]
              yield case ("Sphere.squaredRadius-" + what) "Sphere.squaredRadius" [ "x", S sphere ]
              yield case ("Sphere.kind-" + what) "Sphere.kind" [ "tau", N 0.0; "x", S sphere ]

          yield
              case "Sphere.squaredRadius-of-an-imaginary-sphere" "Sphere.squaredRadius" [ "x", Coefficients imaginary ]
          yield case "Sphere.kind-of-an-imaginary-sphere" "Sphere.kind" [ "tau", N 0.0; "x", Coefficients imaginary ]
          yield
              case
                  "Sphere.kind-imaginary-just-below-the-threshold"
                  "Sphere.kind"
                  [ "tau", N(below 4.0 -51); "x", Coefficients imaginary ]
          yield
              case
                  "Sphere.kind-tangent-at-the-negative-threshold"
                  "Sphere.kind"
                  [ "tau", N 4.0; "x", Coefficients imaginary ]
          yield
              case
                  "Sphere.kind-round-just-above-the-threshold"
                  "Sphere.kind"
                  [ "tau", N(below 8.0 -50); "x", Coefficients imaginary ]
          yield case "Sphere.kind-no-round-at-the-threshold" "Sphere.kind" [ "tau", N 8.0; "x", Coefficients imaginary ]
          yield
              case
                  "Sphere.kind-real-just-above-the-threshold"
                  "Sphere.kind"
                  [ "tau", N(below 1.0 -53); "x", S unitSphere ]
          yield
              case
                  "Sphere.kind-at-infinity"
                  "Sphere.kind"
                  [ "tau", N 0.0; "x", Coefficients [| 0.0; 0.0; 0.0; 0.0; -3.0 |] ]
          yield
              case
                  "Sphere.kind-of-a-nan-coefficient"
                  "Sphere.kind"
                  [ "tau", N 0.0; "x", Coefficients [| nan; 0.0; 0.0; 0.0; -3.0 |] ]
          yield
              case
                  "Sphere.kind-of-a-nan-beside-a-weight"
                  "Sphere.kind"
                  [ "tau", N 0.0; "x", Coefficients [| 1.0; 0.0; nan; 0.0; -3.0 |] ]
          yield! thresholds "Sphere.kind" [ "x", S unitSphere ]
          yield
              case
                  "Sphere.normalise-of-an-infinite-weight"
                  "Sphere.normalise"
                  [ "x", Coefficients [| inf; 0.0; 0.0; 0.0; -3.0 |] ]
          yield
              case
                  "Sphere.normalise-divides-and-never-multiplies-by-a-reciprocal"
                  "Sphere.normalise"
                  [ "x", Coefficients [| 3.0; 1.0; 5.0; 7.0; 12.5 |] ]
          yield
              case
                  "Sphere.meet-of-two-spheres-that-cross"
                  "Sphere.meet"
                  [ "x", S unitSphere; "y", S(ok (Sphere.ofCentreRadius (1.0, 0.0, 0.0) 1.0)) ]
          yield
              case
                  "Sphere.meet-of-two-spheres-that-touch"
                  "Sphere.meet"
                  [ "x", S unitSphere; "y", S(ok (Sphere.ofCentreRadius (2.0, 0.0, 0.0) 1.0)) ]
          yield case "Sphere.meet-of-two-spheres-apart" "Sphere.meet" [ "x", S unitSphere; "y", S farSphere ]
          yield case "Sphere.meet-of-one-sphere-twice" "Sphere.meet" [ "x", S unitSphere; "y", S unitSphere ]
          yield case "Sphere.meetPlane-through-the-centre" "Sphere.meetPlane" [ "x", S unitSphere; "y", PL floor0 ]
          yield case "Sphere.meetPlane-that-touches" "Sphere.meetPlane" [ "x", S unitSphere; "y", PL(planeAt 1.0) ]
          yield case "Sphere.meetPlane-apart" "Sphere.meetPlane" [ "x", S unitSphere; "y", PL(planeAt 2.0) ]

          // Against the unit sphere: the origin has power `−1`, `(2, 0, 0)` has `3`, `(0, 0, 1)` is on it.
          for what, p in [ "inside", origin; "outside", u 2.0 0.0 0.0; "on", u 0.0 0.0 1.0 ] do
              yield case ("Sphere.power-" + what) "Sphere.power" [ "p", P p; "x", S unitSphere ]
              yield case ("Sphere.side-" + what) "Sphere.side" [ "tau", N 0.0; "p", P p; "x", S unitSphere ]

          // A power that is zero is `-0.0` over a positive divisor and `+0.0` over a negative one.
          yield
              case
                  "Sphere.power-on-of-a-negative-weight-point"
                  "Sphere.power"
                  [ "p", P(weighted -2.0 (u 0.0 0.0 1.0)); "x", S unitSphere ]
          yield
              case
                  "Sphere.power-on-of-two-negative-weights"
                  "Sphere.power"
                  [ "p", P(weighted -2.0 (u 0.0 0.0 1.0)); "x", S(scaled -3.0 unitSphere) ]
          yield
              case
                  "Sphere.power-of-a-weighted-point-and-sphere"
                  "Sphere.power"
                  [ "p", P(weighted 3.0 (u 1.0 2.0 2.0)); "x", S(scaled -7.0 farSphere) ]
          yield
              case
                  "Sphere.side-of-a-negative-weight-sphere"
                  "Sphere.side"
                  [ "tau", N 0.0; "p", P origin; "x", S(scaled -3.0 unitSphere) ]
          yield
              case
                  "Sphere.side-inside-just-below-the-threshold"
                  "Sphere.side"
                  [ "tau", N(below 1.0 -53); "p", P origin; "x", S unitSphere ]
          yield
              case
                  "Sphere.side-on-at-the-negative-threshold"
                  "Sphere.side"
                  [ "tau", N 1.0; "p", P origin; "x", S unitSphere ]
          yield
              case
                  "Sphere.side-outside-just-above-the-threshold"
                  "Sphere.side"
                  [ "tau", N(below 3.0 -51); "p", P(u 2.0 0.0 0.0); "x", S unitSphere ]
          yield
              case
                  "Sphere.side-on-at-the-threshold"
                  "Sphere.side"
                  [ "tau", N 3.0; "p", P(u 2.0 0.0 0.0); "x", S unitSphere ]
          yield case "Sphere.power-that-underflows" "Sphere.power" [ "p", Coefficients beside; "x", Coefficients speck ]
          yield
              case
                  "Sphere.side-decided-by-signs-where-the-quotient-underflows"
                  "Sphere.side"
                  [ "tau", N 0.0; "p", Coefficients beside; "x", Coefficients speck ]

          for name in [ "Sphere.power"; "Sphere.side" ] do
              let tau = if name = "Sphere.side" then [ "tau", N 0.0 ] else []

              yield
                  case
                      (name + "-of-a-zero-weight-point")
                      name
                      (tau @ [ "p", Coefficients [| 0.0; 1.0; 0.0; 0.0; 0.5 |]; "x", S unitSphere ])

              yield case (name + "-against-four-coplanar-points") name (tau @ [ "p", P origin; "x", S coplanar ])

              yield
                  case
                      (name + "-whose-weights-underflow")
                      name
                      (tau
                       @ [ "p", Coefficients [| pow2 -600; 0.0; 0.0; 0.0; 0.0 |]
                           "x", Coefficients [| pow2 -600; 0.0; 0.0; 0.0; pow2 -600 |] ])

              yield
                  case
                      (name + "-whose-weights-overflow")
                      name
                      (tau
                       @ [ "p", Coefficients [| pow2 600; 0.0; 0.0; 0.0; 0.0 |]
                           "x", Coefficients [| pow2 600; 0.0; 0.0; 0.0; pow2 600 |] ])

              yield
                  case
                      (name + "-of-a-nan-coordinate")
                      name
                      (tau @ [ "p", Coefficients [| 1.0; 0.0; nan; 0.0; 0.5 |]; "x", S unitSphere ])

          yield! thresholds "Sphere.side" [ "p", P origin; "x", S unitSphere ]
          yield
              case
                  "Sphere.toMultivector-keeps-each-zero"
                  "Sphere.toMultivector"
                  [ "x", Coefficients [| -0.0; 2.0; 0.0; -3.0; -0.0 |] ]
          yield case "Sphere.ofMultivector-of-the-powers-of-two" "Sphere.ofMultivector" [ "a", PowersOfTwo ] ]

    let private bladeHard: Case list =
        let line (x: Line) = M(Line.toMultivector x)
        let plane (x: Plane) = M(Plane.toMultivector x)
        let sphere (x: Sphere) = M(Sphere.toMultivector x)

        [ case "Blade.join-of-two-flats" "Blade.join" [ "a", line xAxis; "b", plane (planeAt 1.0) ]
          case
              "Blade.join-of-a-line-and-a-point"
              "Blade.join"
              [ "a", line xAxis; "b", M(Point.toMultivector (u 0.0 2.0 0.0)) ]
          case "Blade.meet-of-two-coplanar-lines" "Blade.meet" [ "a", line xAxis; "b", line yAxis ]
          case "Blade.meet-of-a-sphere-and-a-plane" "Blade.meet" [ "a", sphere unitSphere; "b", plane floor0 ]
          case "Blade.meet-with-the-operands-exchanged" "Blade.meet" [ "a", plane floor0; "b", sphere unitSphere ]
          case "Blade.inverse-of-the-pseudoscalar" "Blade.inverse" [ "a", Blade 31 ]
          case "Blade.inverse-of-a-vector" "Blade.inverse" [ "a", Coefficients(sparse [ 2, 3.0; 3, -4.0 ]) ]
          case
              "Blade.inverse-divides-and-never-multiplies-by-a-reciprocal"
              "Blade.inverse"
              [ "a", Coefficients(sparse [ 2, 1.0; 3, 1.0; 4, 1.0 ]) ]
          case "Blade.inverse-of-a-null-vector" "Blade.inverse" [ "a", Blade 1 ]
          case "Blade.inverse-of-the-zero-blade" "Blade.inverse" [ "a", Coefficients(sparse []) ]
          case "Blade.inverse-of-an-infinity" "Blade.inverse" [ "a", Coefficients(sparse [ 2, inf ]) ]
          case "Blade.inverse-of-a-nan" "Blade.inverse" [ "a", Coefficients(sparse [ 1, nan ]) ]
          case
              "Blade.meetWithin-the-plane-of-two-lines"
              "Blade.meetWithin"
              [ "j", plane floor0; "a", line xAxis; "b", line yAxis ]
          case
              "Blade.meetWithin-the-whole-space"
              "Blade.meetWithin"
              [ "j", Blade 31; "a", sphere unitSphere; "b", plane floor0 ]
          case "Blade.meetWithin-a-null-span" "Blade.meetWithin" [ "j", Blade 5; "a", line xAxis; "b", line yAxis ]
          case
              "Blade.meetWithin-that-overflows"
              "Blade.meetWithin"
              [ "j", Blade 31
                "a", Coefficients(sparse [ 26, 1e200; 30, -1e200 ])
                "b", Coefficients(sparse [ 26, 1e200; 30, 1e200 ]) ]
          case
              "Blade.meetWithin-of-a-nan"
              "Blade.meetWithin"
              [ "j", Blade 31; "a", Coefficients(sparse [ 26, nan ]); "b", sphere unitSphere ] ]

    let private conformalHard: Case list =
        pointHard
        @ pairHard
        @ lineHard
        @ circleHard
        @ planeHard
        @ sphereHard
        @ bladeHard

    // ---- appended after the 918: a round's reality at a threshold of zero, the order of refusal, and
    // ---- the drawn cases a wrong port fails where no vector above does (DECISIONS.md D87 items 2, 7, 10)

    /// A round of weight `2^500` whose squared radius has a numerator of about `2^-573` and a divisor of
    /// `2^1000`: the quotient underflows to a zero of the numerator's sign, and at `tau = 0` the signs
    /// decide. The second holds the other sign.
    let private underflowing: Case list =
        let big = pow2 500

        [ for sign, what in [ 1.0, "imaginary"; -1.0, "real" ] do
              let pair =
                  Array.init 10 (fun k ->
                      if k = 0 then big
                      elif k = 6 then sign * least
                      else 0.0)

              let circle =
                  Array.init 10 (fun k ->
                      if k = 0 then big
                      elif k = 7 then -sign * least
                      else 0.0)

              let sphere =
                  Array.init 5 (fun k ->
                      if k = 0 then big
                      elif k = 4 then sign * least
                      else 0.0)

              let named (op: string) =
                  sprintf "%s-%s-by-signs-where-the-quotient-underflows" op what

              yield case (named "PointPair.kind") "PointPair.kind" [ "tau", N 0.0; "x", Coefficients pair ]
              yield case (named "PointPair.points") "PointPair.points" [ "tau", N 0.0; "x", Coefficients pair ]
              yield case (named "PointPair.squaredRadius") "PointPair.squaredRadius" [ "x", Coefficients pair ]
              yield case (named "Circle.kind") "Circle.kind" [ "tau", N 0.0; "x", Coefficients circle ]
              yield case (named "Sphere.kind") "Sphere.kind" [ "tau", N 0.0; "x", Coefficients sphere ]
              // At a threshold above zero the quotient is formed, and a zero is within it.
              yield
                  case
                      (sprintf "PointPair.kind-%s-read-as-tangent-above-a-threshold-of-zero" what)
                      "PointPair.kind"
                      [ "tau", N least; "x", Coefficients pair ] ]

    /// A round by its weight whose squared radius has a divisor that underflows to zero: the kind is
    /// refused, as the squared radius is.
    let private undivided: Case list =
        [ case
              "PointPair.kind-of-a-round-whose-divisor-underflows"
              "PointPair.kind"
              [ "tau", N 0.0; "x", Coefficients(only 10 0 1e-200) ]
          case
              "Circle.kind-of-a-round-whose-divisor-underflows"
              "Circle.kind"
              [ "tau", N 0.0; "x", Coefficients(only 10 0 1e-200) ]
          case
              "Sphere.kind-of-a-round-whose-divisor-underflows"
              "Sphere.kind"
              [ "tau", N 0.0; "x", Coefficients(only 5 0 1e-200) ] ]

    /// Every parameter is refused in the order it is passed, before anything is computed: a centre and
    /// a radius whose arithmetic would overflow into a NaN, and a normal or a direction that is zero.
    let private refusedInOrder: Case list =
        let op = "Circle.ofCentreRadiusNormal"
        let zero = T(0.0, 0.0, 0.0)

        [ case
              (op + "-refuses-a-zero-normal-before-it-computes")
              op
              [ "centre", T(1e300, 0.0, 0.0); "radius", N 1e300; "normal", zero ]
          case
              (op + "-refuses-a-nan-normal-before-it-computes")
              op
              [ "centre", T(1e300, 0.0, 0.0); "radius", N 1e300; "normal", T(0.0, nan, 1.0) ]
          case
              (op + "-refuses-the-centre-before-the-normal")
              op
              [ "centre", T(inf, 0.0, 0.0); "radius", N 1.0; "normal", zero ]
          case
              "Line.ofPointDirection-refuses-a-zero-direction-of-a-far-point"
              "Line.ofPointDirection"
              [ "p", T(1e300, 1e300, 0.0); "d", zero ]
          case
              "Sphere.ofCentreRadius-refuses-the-radius-of-a-far-centre"
              "Sphere.ofCentreRadius"
              [ "centre", T(1e300, 0.0, 0.0); "radius", N -1.0 ] ]

    /// The drawn cases chosen as witnesses: the configuration's number, the case's id there, and, for a
    /// function that takes one, the threshold it is asked at with the word its id gains. Each is a case
    /// some wrong port answers otherwise where it answered every earlier vector of that function.
    let private chosen: (int * string * (string * float) option) list =
        [ 0, "Circle.kind-general-from-parameters-1000", Some("at-a-threshold-it-names", 0.8359553183532337)
          0, "Circle.side-general-from-parameters-1000", Some("at-a-threshold-it-names", 0.4622722622854725)
          0, "Line.meet-general-at-tau-zero-1000", Some("at-a-threshold-it-names", 0.08277625368451899)
          0, "Plane.nearestPoint-general-1000", None
          0, "Plane.side-general-1000", Some("at-a-threshold-it-names", 0.35415855107180055)
          0, "Sphere.kind-general-from-parameters-1000", Some("at-a-threshold-it-names", 0.8359553183532336)
          1, "Blade.meetWithin-general-a-plane-1001", None
          1, "Circle.side-general-1001", Some("at-a-threshold-it-names", 26298.091753688364)
          1, "Line.kind-general-1001", Some("at-a-threshold-it-names", 15.34265041165795)
          1, "Line.nearestPoint-general-1001", None
          1, "Plane.side-general-1001", Some("at-a-threshold-it-names", 22.67911464878033)
          1, "PointPair.kind-general-1001", Some("at-a-threshold-it-names", 621.1161015918115)
          1, "PointPair.kind-general-1001", Some("at-a-threshold-it-names-2", 15.34265041165795)
          1, "Sphere.kind-general-1001", Some("at-a-threshold-it-names", 16517776.314716326)
          1, "Sphere.side-general-1001", Some("at-a-threshold-it-names", 178593.3473601453)
          2, "Plane.signedDistance-general-from-parameters-1002", None
          2, "PointPair.squaredRadius-general-1002", None
          2, "Sphere.power-general-1002", None
          3, "Circle.kind-general-1003", Some("at-a-threshold-it-names", 0.007109267023052834)
          3, "Line.ofPointDirection-general-1003", None
          3, "Plane.kind-general-1003", Some("at-a-threshold-it-names", 0.007109267023052834)
          5, "Circle.power-general-1005", None
          6, "Circle.kind-general-from-parameters-1006", Some("at-a-threshold-it-names", 0.012740901889207238)
          6, "Circle.ofCentreRadiusNormal-general-1006", None
          6, "Plane.ofNormalDistance-general-1006", None
          6, "Plane.side-general-1006", Some("at-a-threshold-it-names", 0.21762764549648791)
          7, "Circle.side-general-1007", Some("at-a-threshold-it-names", 8328.365015034862)
          8, "Circle.kind-general-from-parameters-1008", Some("at-a-threshold-it-names", 1.4485834005322929e-05)
          9, "Plane.side-general-1009", Some("at-a-threshold-it-names", 0.23557410895303643)
          10, "Circle.ofCentreRadiusNormal-general-1010", None
          10, "Plane.ofNormalDistance-general-1010", None
          10, "PointPair.kind-general-of-a-line-and-a-plane-1010", Some("at-a-threshold-it-names", 315.7451780195358)
          12, "Line.normalise-general-1012", None
          12, "Line.weight-general-1012", None
          12, "PointPair.normalise-general-1012", None
          12, "PointPair.weight-general-1012", None
          12, "Sphere.side-general-1012", Some("at-a-threshold-it-names", 0.01569940232372319)
          13, "Sphere.kind-general-from-parameters-1013", Some("at-a-threshold-it-names", 175.8870228535389)
          14, "Line.kind-general-1014", Some("at-a-threshold-it-names", 0.07229763043860672)
          14, "Sphere.side-general-from-parameters-1014", Some("at-a-threshold-it-names", 0.0014643461390965928)
          26, "Line.kind-general-1026", Some("at-a-threshold-it-names", 0.010525242673356434)
          26, "Line.normalise-general-1026", None
          26, "Line.weight-general-1026", None
          26, "PointPair.kind-general-1026", Some("at-a-threshold-it-names", 0.010525242673356434)
          26, "PointPair.normalise-general-1026", None
          26, "PointPair.weight-general-1026", None
          33, "Circle.kind-general-1033", Some("at-a-threshold-it-names", 0.03840522867906613)
          33, "Circle.normalise-general-1033", None
          33, "Circle.weight-general-1033", None
          33, "Plane.kind-general-1033", Some("at-a-threshold-it-names", 0.03840522867906613)
          33, "Plane.normalise-general-1033", None
          33, "Plane.signedDistance-general-1033", None
          33, "Plane.weight-general-1033", None
          59, "Plane.side-general-1059", Some("at-a-threshold-it-names", 0.002221685343600184)
          63, "Line.ofPointDirection-general-1063", None
          108, "Line.meet-general-of-two-skew-lines-1108", Some("at-a-threshold-it-names", 0.0029454291267690994)
          148, "Sphere.kind-general-1148", Some("at-a-threshold-it-names", 2148118.4004810504) ]

    let private drawnWitnesses: Case list =
        [ for index, id, threshold in chosen do
              let c = drawn index |> List.find (fun c -> c.Id = id)

              match threshold with
              | None -> yield c
              | Some(word, tau) ->
                  yield
                      { c with
                          Id = c.Id + "-" + word
                          Input =
                              c.Input
                              |> List.map (fun (name, o) -> if name = "tau" then name, Number tau else name, o) } ]

    /// A sphere of no weight is its plane. The two planes a wrong norm fails at a threshold they name,
    /// asked as spheres at the same threshold.
    let private flatSpheres: Case list =
        [ for c in drawnWitnesses do
              if c.Op = "Plane.kind" && c.Id.Contains "at-a-threshold-it-names" then
                  let plane =
                      match c.Input |> List.find (fun (name, _) -> name = "x") |> snd with
                      | Coefficients xs -> xs
                      | _ -> failwith "a plane is a list of coefficients"

                  yield
                      case
                          (c.Id.Replace("Plane.kind", "Sphere.kind-of-the-plane-of"))
                          "Sphere.kind"
                          [ c.Input |> List.find (fun (name, _) -> name = "tau")
                            "x", Coefficients(Array.append [| 0.0 |] plane) ] ]

    // ---- appended after the 997: zeros of both signs, a nearest point that overflows, and the cases a
    // ---- search over zero-heavy and untyped operands found (DECISIONS.md D87 items 4 and 10)

    /// The outer product of `e1` with a multivector that is `-0.0` at every index but `e1`'s: every term
    /// of the eight coefficients it reaches is a negative zero, and the fold from `+0.0` answers `+0.0`.
    let private signedZeros: Case list =
        [ case
              "Blade.join-whose-every-term-is-a-negative-zero"
              "Blade.join"
              [ "a", Blade 2
                "b", Coefficients(Array.init count (fun index -> if index = 2 then 0.0 else -0.0)) ] ]

    /// A line whose point nearest the origin has a first coordinate past the largest double. The read
    /// answers the infinity; the two-line rule embeds that point, and `up` refuses it by name.
    let private nearestOverflows: Case list =
        let line =
            [| 0.0; -1.0; 0.07918293826789391; -Double.MaxValue; Double.MaxValue; 0.0 |]

        [ case "Line.nearestPoint-whose-coordinate-overflows" "Line.nearestPoint" [ "x", Coefficients line ]
          case
              "Line.meet-whose-nearest-point-overflows"
              "Line.meet"
              [ "tau", N Double.MaxValue; "x", L xAxis; "y", Coefficients line ] ]

    /// The drawn configuration of this number with two coefficients in three of every operand replaced
    /// by a zero of a drawn sign: typed operands that are heavy in zeros of both signs.
    let zeroed (index: int) : Case list =
        let d = Draw(7000 + index)

        drawn index
        |> List.map (fun c ->
            { c with
                Id = c.Id + "-with-zeros"
                Input =
                    c.Input
                    |> List.map (fun (name, o) ->
                        match o with
                        | Coefficients xs ->
                            name,
                            Coefficients(
                                xs
                                |> Array.map (fun x ->
                                    match d.Int 0 5 with
                                    | 0
                                    | 1 -> x
                                    | 2
                                    | 3 -> 0.0
                                    | _ -> -0.0)
                            )
                        | other -> name, other) })

    /// The blade operations over untyped multivectors that are zeros of both signs, alone, around one
    /// drawn coefficient, and around a third of them: operands no typed object gives.
    let untyped (index: int) : Case list =
        let d = Draw(8000 + index)

        let zeros () : float[] =
            Array.init count (fun _ -> if d.Int 0 1 = 0 then 0.0 else -0.0)

        let oneAmongZeros () : float[] =
            let xs = zeros ()
            let at = d.Int 0 (count - 1)
            withOne at (d.Scaled 0 2) xs

        let mostlyZeros () : float[] =
            Array.init count (fun _ ->
                match d.Int 0 5 with
                | 0
                | 1 -> d.Scaled 0 4
                | 2
                | 3 -> 0.0
                | _ -> -0.0)

        [ for what, make in
              [ "zeros", zeros
                "one-among-zeros", oneAmongZeros
                "mostly-zeros", mostlyZeros ] do
              let named (op: string) =
                  sprintf "%s-untyped-%s-%d" op what (1000 + index)

              let a = make ()
              let b = make ()
              let j = make ()
              yield case (named "Blade.join") "Blade.join" [ "a", Coefficients a; "b", Coefficients b ]
              yield case (named "Blade.meet") "Blade.meet" [ "a", Coefficients a; "b", Coefficients b ]
              yield case (named "Blade.inverse") "Blade.inverse" [ "a", Coefficients a ]

              yield
                  case
                      (named "Blade.meetWithin")
                      "Blade.meetWithin"
                      [ "j", Coefficients j; "a", Coefficients a; "b", Coefficients b ]

              yield
                  case
                      (named "Blade.meetWithin" + "-within-the-whole-space")
                      "Blade.meetWithin"
                      [ "j", Blade 31; "a", Coefficients a; "b", Coefficients b ] ]

    /// Everything a search for a wrong port's reach draws at one number: the configuration, the same
    /// with zeros, and the untyped operands.
    let searched (index: int) : Case list =
        drawn index @ zeroed index @ untyped index

    /// The cases of `searched` chosen as witnesses after the 997, as `chosen` is read.
    let private chosenLater: (int * string * (string * float) option) list =
        [ 0, "PointPair.points-general-1000", Some("at-a-further-threshold", 0.14820163862845376)
          2, "Circle.ofCentreRadiusNormal-general-1002-with-zeros", None
          2, "Line.normalise-general-1002", None
          2, "Line.weight-general-1002", None
          2, "PointPair.normalise-general-1002", None
          2, "PointPair.weight-general-1002", None
          31, "Plane.side-general-from-parameters-1031", Some("at-a-further-threshold", 192.56395506780785) ]

    let private laterWitnesses: Case list =
        [ for index, id, threshold in chosenLater do
              let c = searched index |> List.find (fun c -> c.Id = id)

              match threshold with
              | None -> yield c
              | Some(word, tau) ->
                  yield
                      { c with
                          Id = c.Id + "-" + word
                          Input =
                              c.Input
                              |> List.map (fun (name, o) -> if name = "tau" then name, Number tau else name, o) } ]

    // ---- the versors (Phase 729, DECISIONS.md D88): appended after the 1,007 -----------------------

    let rotorOf (v: float[]) : Rotor =
        { Scalar = v[0]
          E1E2 = v[1]
          E1E3 = v[2]
          E2E3 = v[3] }

    let rotorValues (x: Rotor) : float[] = [| x.Scalar; x.E1E2; x.E1E3; x.E2E3 |]

    let translatorOf (v: float[]) : Translator =
        { Scalar = v[0]
          E1NInf = v[1]
          E2NInf = v[2]
          E3NInf = v[3] }

    let translatorValues (x: Translator) : float[] =
        [| x.Scalar; x.E1NInf; x.E2NInf; x.E3NInf |]

    let motorOf (v: float[]) : Motor =
        { Scalar = v[0]
          E1E2 = v[1]
          E1E3 = v[2]
          E1NInf = v[3]
          E2E3 = v[4]
          E2NInf = v[5]
          E3NInf = v[6]
          E1E2E3NInf = v[7] }

    let motorValues (x: Motor) : float[] =
        [| x.Scalar
           x.E1E2
           x.E1E3
           x.E1NInf
           x.E2E3
           x.E2NInf
           x.E3NInf
           x.E1E2E3NInf |]

    let dilatorOf (v: float[]) : Dilator =
        { Scalar = v[0]
          N0NInf = v[1]
          E1NInf = v[2]
          E2NInf = v[3]
          E3NInf = v[4] }

    let dilatorValues (x: Dilator) : float[] =
        [| x.Scalar; x.N0NInf; x.E1NInf; x.E2NInf; x.E3NInf |]

    let reflectionOf (v: float[]) : Reflection =
        { E1 = v[0]
          E2 = v[1]
          E3 = v[2]
          NInf = v[3] }

    let reflectionValues (x: Reflection) : float[] = [| x.E1; x.E2; x.E3; x.NInf |]

    let private RO (x: Rotor) : Operand = Coefficients(rotorValues x)
    let private TR (x: Translator) : Operand = Coefficients(translatorValues x)
    let private MO (x: Motor) : Operand = Coefficients(motorValues x)
    let private DI (x: Dilator) : Operand = Coefficients(dilatorValues x)
    let private RE (x: Reflection) : Operand = Coefficients(reflectionValues x)

    /// The versor types, each with the functions every one of them has.
    let versorTypes: string list =
        [ "Rotor"; "Translator"; "Motor"; "Dilator"; "Reflection" ]

    /// One drawn configuration of versors: the parameters of a rotor, a translator, a screw, a dilator
    /// and a reflection, a second of each, a bivector and a vector, and an interpolation's parameter;
    /// every length about `2^centre` across `spread` binary orders either way, every angle within
    /// `(−4, 4)`. `zeros` says what an operation takes of each versor: `0` its coefficients times a
    /// drawn weight; `1` two coefficients in three replaced by a zero of a drawn sign; `2` every
    /// coefficient a zero of a drawn sign; `3` every coefficient but a drawn one such a zero; `4`
    /// one coefficient in three as drawn, one in three a zero of a drawn sign, and one in three a
    /// power of two between `2^-650` and `2^-450` of a drawn sign, so that a product of two of those
    /// can underflow to a zero of either sign where the square of one of them does not, beside terms
    /// that are ordinary.
    type private Moves(seed: int, centre: int, spread: int, zeros: int) =
        let d = Draw seed

        let triple () : float * float * float =
            let a = d.Scaled centre spread
            let b = d.Scaled centre spread
            let c = d.Scaled centre spread
            a, b, c

        let direction () : float * float * float =
            let a = d.Scaled 0 2
            let b = d.Scaled 0 2
            let c = d.Scaled 0 2
            a, b, c

        let angle () : float = d.Scaled 3 0

        let positive () : float =
            let x = d.Scaled 1 3
            if x < 0.0 then -x else x

        /// A versor's coefficients as an operation takes them: times a drawn weight, so that it is no
        /// unit, and with zeros of both signs where the configuration asks for them.
        let taken (values: float[]) : float[] =
            let w = d.Scaled 1 1
            let kept = if zeros = 3 then d.Int 0 (values.Length - 1) else -1

            values
            |> Array.mapi (fun at v ->
                match zeros with
                | 0 -> w * v
                | 1 ->
                    match d.Int 0 5 with
                    | 0
                    | 1 -> w * v
                    | 2
                    | 3 -> 0.0
                    | _ -> -0.0
                | 4 ->
                    match d.Int 0 5 with
                    | 0 -> pow2 (d.Int -650 -450)
                    | 1 -> -(pow2 (d.Int -650 -450))
                    | 2 -> 0.0
                    | 3 -> -0.0
                    | _ -> w * v
                | _ ->
                    if at = kept then w * v
                    elif d.Int 0 1 = 0 then 0.0
                    else -0.0)

        /// An object's coefficients as an operation takes them: as they are, or, where every
        /// coefficient of a versor is a zero, zeros of drawn signs too.
        let object (values: float[]) : float[] =
            if zeros = 4 then
                values
                |> Array.map (fun v ->
                    match d.Int 0 5 with
                    | 0 -> pow2 (d.Int -650 -450)
                    | 1 -> -(pow2 (d.Int -650 -450))
                    | 2 -> 0.0
                    | 3 -> -0.0
                    | _ -> v)
            elif zeros >= 2 then
                values |> Array.map (fun _ -> if d.Int 0 1 = 0 then 0.0 else -0.0)
            else
                values

        let axis1 = direction ()
        let angle1 = angle ()
        let axis2 = direction ()
        let angle2 = angle ()
        let t1 = triple ()
        let t2 = triple ()
        let point1 = triple ()
        let direction1 = direction ()
        let screwAngle1 = angle ()
        let distance1 = d.Scaled centre spread
        let point2 = triple ()
        let direction2 = direction ()
        let screwAngle2 = angle ()
        let distance2 = d.Scaled centre spread
        let centre1 = triple ()
        let factor1 = positive ()
        let centre2 = triple ()
        let factor2 = positive ()
        let normal1 = direction ()
        let delta1 = d.Scaled centre spread
        let normal2 = direction ()
        let delta2 = d.Scaled centre spread
        let bivector = (d.Scaled -1 2, d.Scaled -1 2, d.Scaled -1 2)
        let vector = triple ()
        let parameter = d.Scaled -1 1
        let rotor1 = ok (Rotor.ofAxisAngle axis1 angle1)
        let rotor2 = ok (Rotor.ofAxisAngle axis2 angle2)
        let translator1 = ok (Translator.ofVector t1)
        let translator2 = ok (Translator.ofVector t2)
        let motor1 = ok (Motor.ofScrew point1 direction1 screwAngle1 distance1)
        let motor2 = ok (Motor.ofScrew point2 direction2 screwAngle2 distance2)
        let dilator1 = ok (Dilator.ofCentreFactor centre1 factor1)
        let dilator2 = ok (Dilator.ofCentreFactor centre2 factor2)
        let reflection1 = ok (Reflection.ofNormalDistance normal1 delta1)
        let reflection2 = ok (Reflection.ofNormalDistance normal2 delta2)
        let rotorA = taken (rotorValues rotor1)
        let rotorB = taken (rotorValues rotor2)
        let translatorA = taken (translatorValues translator1)
        let translatorB = taken (translatorValues translator2)
        let motorA = taken (motorValues motor1)
        let motorB = taken (motorValues motor2)
        let dilatorA = taken (dilatorValues dilator1)
        let dilatorB = taken (dilatorValues dilator2)
        let reflectionA = taken (reflectionValues reflection1)
        let reflectionB = taken (reflectionValues reflection2)

        member _.Axis = axis1
        member _.Angle = angle1
        member _.T = t1
        member _.Point = point1
        member _.Direction = direction1
        member _.ScrewAngle = screwAngle1
        member _.Distance = distance1
        member _.Centre = centre1
        member _.Factor = factor1
        member _.Normal = normal1
        member _.Delta = delta1
        member _.Bivector = bivector
        member _.Vector = vector
        member _.Parameter = parameter
        member _.Rotor1 = rotor1
        member _.Rotor2 = rotor2
        member _.Translator1 = translator1
        member _.Motor1 = motor1
        member _.Motor2 = motor2
        member _.Dilator1 = dilator1
        member _.Object(values: float[]) : float[] = object values

        /// The two versors of a type as an operation takes them, by the type's name.
        member _.Taken(versor: string) : float[] * float[] =
            match versor with
            | "Rotor" -> rotorA, rotorB
            | "Translator" -> translatorA, translatorB
            | "Motor" -> motorA, motorB
            | "Dilator" -> dilatorA, dilatorB
            | _ -> reflectionA, reflectionB

    /// Every published function of the versors over one drawn configuration and the objects of a
    /// scene. `tag` is what a vector's id says after its function, before the configuration's number.
    let private movesCases (tag: string) (k: int) (s: Scene) (m: Moves) : Case list =
        [ let g (op: string) (what: string) (input: (string * Operand) list) : Case =
              case (sprintf "%s-%s-%s%d" op tag what k) op input

          let objects =
              [ "Point", Coefficients(m.Object(pointValues s.P))
                "PointPair", Coefficients(m.Object(pairValues s.PairT))
                "Line", Coefficients(m.Object(lineValues s.LineP))
                "Circle", Coefficients(m.Object(circleValues s.CircleT))
                "Plane", Coefficients(m.Object(planeValues s.PlaneP))
                "Sphere", Coefficients(m.Object(sphereValues s.SphereT)) ]

          yield g "Rotor.ofAxisAngle" "" [ "axis", T m.Axis; "angle", N m.Angle ]
          yield g "Rotor.exp" "" [ "bivector", T m.Bivector ]
          yield g "Rotor.log" "" [ "v", RO m.Rotor1 ]
          yield g "Rotor.log" "of-a-scaled-rotor-" [ "v", Coefficients(fst (m.Taken "Rotor")) ]
          yield g "Rotor.interpolate" "" [ "t", N m.Parameter; "first", RO m.Rotor1; "second", RO m.Rotor2 ]
          yield g "Translator.ofVector" "" [ "t", T m.T ]
          yield g "Motor.ofRotorTranslator" "" [ "rotor", RO m.Rotor1; "translator", TR m.Translator1 ]
          yield g "Motor.ofRotor" "" [ "rotor", RO m.Rotor1 ]
          yield g "Motor.ofTranslator" "" [ "translator", TR m.Translator1 ]

          yield
              g
                  "Motor.ofScrew"
                  ""
                  [ "point", T m.Point
                    "direction", T m.Direction
                    "angle", N m.ScrewAngle
                    "distance", N m.Distance ]

          yield g "Motor.ofLine" "" [ "line", L s.LineP; "angle", N m.ScrewAngle; "distance", N m.Distance ]
          yield g "Motor.ofLine" "of-a-weighted-line-" [ "line", L s.LineT; "angle", N m.Angle; "distance", N m.Delta ]
          yield g "Motor.exp" "" [ "bivector", T m.Bivector; "vector", T m.Vector ]
          yield g "Motor.log" "" [ "v", MO m.Motor1 ]
          yield g "Motor.log" "of-a-scaled-motor-" [ "v", Coefficients(fst (m.Taken "Motor")) ]
          yield g "Motor.interpolate" "" [ "t", N m.Parameter; "first", MO m.Motor1; "second", MO m.Motor2 ]
          yield g "Motor.ofMatrix" "" [ "tau", N(pow2 -40); "matrix", Coefficients(ok (Motor.toMatrix m.Motor1)) ]

          yield
              g
                  "Motor.ofMatrix"
                  "of-a-dilation-"
                  [ "tau", N(pow2 -40); "matrix", Coefficients(ok (Dilator.toMatrix m.Dilator1)) ]

          // The same motion's matrix made no similarity three ways, each refused with a measure a
          // drawn matrix gives general bits: its first column negated; its second slanted towards its
          // first by `2^-25` of it, which keeps its length within the threshold; its third stretched.
          let drawnMatrix = ok (Motor.toMatrix m.Motor1)

          let remade (what: string) (f: int -> float -> float) : Case =
              g "Motor.ofMatrix" what [ "tau", N(pow2 -40); "matrix", Coefficients(Array.mapi f drawnMatrix) ]

          yield remade "of-a-mirrored-" (fun i x -> if i < 3 then -x else x)

          yield
              remade "of-a-slanted-" (fun i x ->
                  if i >= 4 && i < 7 then
                      x + pow2 -25 * drawnMatrix[i - 4]
                  else
                      x)

          yield remade "of-a-stretched-" (fun i x -> if i >= 8 && i < 11 then (1.0 + pow2 -8) * x else x)
          yield g "Dilator.ofFactor" "" [ "factor", N m.Factor ]
          yield g "Dilator.ofCentreFactor" "" [ "centre", T m.Centre; "factor", N m.Factor ]
          yield g "Reflection.ofNormalDistance" "" [ "normal", T m.Normal; "distance", N m.Delta ]

          for versor in versorTypes do
              let a, b = m.Taken versor
              yield g (versor + ".compose") "" [ "b", Coefficients b; "a", Coefficients a ]

              for name, x in objects do
                  yield g (sprintf "%s.applyTo%s" versor name) "" [ "v", Coefficients a; "x", x ]

              if versor <> "Reflection" then
                  yield g (versor + ".toMatrix") "" [ "v", Coefficients a ]

              yield g (versor + ".normalise") "" [ "v", Coefficients a ]
              yield g (versor + ".toMultivector") "" [ "v", Coefficients a ]
              yield g (versor + ".ofMultivector") "" [ "a", Coefficients(dense (7290 + k) 0 4) ] ]

    /// The drawn configurations of the committed general vectors.
    let private versorGeneral: Case list =
        [ yield! movesCases "general" 1 (Scene(7281, 0, 1)) (Moves(7291, 0, 1, 0))
          yield! movesCases "general" 2 (Scene(7282, 7, 5)) (Moves(7292, 5, 3, 0))
          yield! movesCases "general" 3 (Scene(7283, -6, 3)) (Moves(7293, -6, 3, 0)) ]

    /// The configuration a search draws at an index: a third of them at each of three scales. Below
    /// ninety, the even ones have zeros of both signs among the versors' coefficients; from ninety,
    /// every coefficient of every versor and of every object a versor is applied to is a zero of a
    /// drawn sign, all of them at the even indices and all but one of each versor's at the odd ones;
    /// and from a hundred and fifty, a third of the coefficients of each versor and of each object
    /// are zeros of drawn signs and a third are powers of two so small that a product of two of them
    /// underflows to a zero of either sign.
    let versorSearched (index: int) : Case list =
        let centre, spread =
            match index % 3 with
            | 0 -> 0, 1
            | 1 -> 5, 3
            | _ -> -6, 3

        movesCases
            "searched"
            (1000 + index)
            (Scene(9000 + index, centre, spread))
            (Moves(
                6000 + index,
                centre,
                spread,
                (if index >= 150 then 4
                 elif index >= 90 then 2 + index % 2
                 elif index % 2 = 0 then 1
                 else 0)
            ))

    // The hard inputs of the versors: each refusal, each boundary and each rule by name.

    let private v3 (a: float) (b: float) (c: float) : Operand = Coefficients [| a; b; c |]

    let private identityMatrix: float[] =
        [| 1.0
           0.0
           0.0
           0.0
           0.0
           1.0
           0.0
           0.0
           0.0
           0.0
           1.0
           0.0
           0.0
           0.0
           0.0
           1.0 |]

    /// A matrix from its three columns and its translation: column-major, the last row `0 0 0 1`.
    let private similarity
        (a: float * float * float)
        (b: float * float * float)
        (c: float * float * float)
        (t: float * float * float)
        : float[] =
        let (a1, a2, a3), (b1, b2, b3), (c1, c2, c3), (t1, t2, t3) = a, b, c, t
        [| a1; a2; a3; 0.0; b1; b2; b3; 0.0; c1; c2; c3; 0.0; t1; t2; t3; 1.0 |]

    let private withEntry (index: int) (value: float) (m: float[]) : float[] =
        Array.mapi (fun i x -> if i = index then value else x) m

    /// The arguments at which Phase 725's law file records the pinned sine, the pinned cosine and the
    /// pinned arc tangent a unit in the last place from the correctly rounded value: `x` for the
    /// sine, `x` for the cosine, and `y` and `x` for the arc tangent.
    let sineOffByOne = 0.5796341513095583
    let cosineOffByOne = 35.22326444351266
    let arcTangentOffByOne = 0.2996283596892042, 1.0252480688622445

    let private versorHard: Case list =
        let max = Double.MaxValue
        let h (op: string) (what: string) (input: (string * Operand) list) : Case = case (op + "-" + what) op input
        let unitRotor = ok (Rotor.ofAxisAngle (0.0, 0.0, 1.0) 1.0)
        let quarter = ok (Rotor.ofAxisAngle (0.0, 0.0, 1.0) (pow2 -2))
        let identityRotor = [| 1.0; 0.0; 0.0; 0.0 |]
        let identityMotor = [| 1.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0 |]
        let y, x = arcTangentOffByOne
        let p = u 1.0 2.0 3.0

        [ // ---- a rotor from an axis and an angle
          yield h "Rotor.ofAxisAngle" "about-e3-takes-e1-towards-e2" [ "axis", v3 0.0 0.0 1.0; "angle", N 1.0 ]
          yield h "Rotor.ofAxisAngle" "divides-the-axis-by-its-norm" [ "axis", v3 3.0 0.0 4.0; "angle", N 1.0 ]
          yield h "Rotor.ofAxisAngle" "of-a-zero-angle" [ "axis", v3 1.0 2.0 2.0; "angle", N 0.0 ]
          yield h "Rotor.ofAxisAngle" "of-a-negative-zero-angle" [ "axis", v3 1.0 2.0 2.0; "angle", N -0.0 ]
          yield
              h "Rotor.ofAxisAngle" "of-the-double-nearest-pi" [ "axis", v3 0.0 0.0 1.0; "angle", N 3.141592653589793 ]
          yield h "Rotor.ofAxisAngle" "of-the-largest-angle" [ "axis", v3 0.0 1.0 0.0; "angle", N max ]

          yield
              h
                  "Rotor.ofAxisAngle"
                  "at-a-sine-a-unit-from-correctly-rounded"
                  [ "axis", v3 0.0 0.0 1.0; "angle", N(2.0 * sineOffByOne) ]

          yield
              h
                  "Rotor.ofAxisAngle"
                  "at-a-cosine-a-unit-from-correctly-rounded"
                  [ "axis", v3 0.0 0.0 1.0; "angle", N(2.0 * cosineOffByOne) ]

          yield h "Rotor.ofAxisAngle" "of-a-zero-axis" [ "axis", v3 0.0 -0.0 0.0; "angle", N 1.0 ]
          yield h "Rotor.ofAxisAngle" "of-the-least-axis" [ "axis", v3 0.0 least 0.0; "angle", N 1.0 ]
          yield h "Rotor.ofAxisAngle" "of-an-axis-whose-norm-overflows" [ "axis", v3 max max 0.0; "angle", N 1.0 ]
          yield h "Rotor.ofAxisAngle" "of-an-infinite-axis" [ "axis", v3 inf 0.0 0.0; "angle", N 1.0 ]
          yield h "Rotor.ofAxisAngle" "of-an-infinite-angle" [ "axis", v3 0.0 0.0 1.0; "angle", N inf ]
          yield h "Rotor.ofAxisAngle" "of-an-angle-that-is-no-number" [ "axis", v3 0.0 0.0 1.0; "angle", N nan ]
          yield h "Rotor.ofAxisAngle" "refuses-the-axis-before-the-angle" [ "axis", v3 0.0 0.0 0.0; "angle", N nan ]

          // ---- the exponential and the logarithm of a rotor
          yield h "Rotor.exp" "of-zero-is-one" [ "bivector", v3 0.0 0.0 0.0 ]
          yield h "Rotor.exp" "of-zeros-keeps-each-sign" [ "bivector", v3 -0.0 0.0 -0.0 ]
          // A coefficient is placed and never added to a zero: the exponential of a negated bivector
          // is the reverse of the exponential, the sign of each zero included.
          yield h "Rotor.exp" "of-a-bivector-with-a-zero-of-each-sign" [ "bivector", v3 0.5 -0.0 0.0 ]
          yield h "Rotor.exp" "of-that-bivector-negated-is-the-reverse" [ "bivector", v3 -0.5 0.0 -0.0 ]
          yield h "Rotor.exp" "of-the-least-bivector" [ "bivector", v3 least 0.0 0.0 ]
          yield h "Rotor.exp" "of-a-quarter-turn-about-e3" [ "bivector", v3 -0.7853981633974483 0.0 0.0 ]
          yield h "Rotor.exp" "of-three-four-five" [ "bivector", v3 0.375 0.0 0.5 ]
          yield h "Rotor.exp" "at-a-sine-a-unit-from-correctly-rounded" [ "bivector", v3 sineOffByOne 0.0 0.0 ]
          yield h "Rotor.exp" "at-a-cosine-a-unit-from-correctly-rounded" [ "bivector", v3 0.0 cosineOffByOne 0.0 ]
          yield h "Rotor.exp" "of-a-bivector-whose-norm-overflows" [ "bivector", v3 max max max ]
          yield h "Rotor.exp" "of-the-largest-bivector" [ "bivector", v3 0.0 max 0.0 ]
          yield h "Rotor.exp" "of-an-infinite-bivector" [ "bivector", v3 0.0 inf 0.0 ]
          yield h "Rotor.exp" "of-a-bivector-that-is-no-number" [ "bivector", v3 nan 0.0 0.0 ]
          yield h "Rotor.log" "of-one-is-zero" [ "v", Coefficients identityRotor ]
          yield h "Rotor.log" "of-a-positive-scalar-keeps-each-zero" [ "v", Coefficients [| 2.0; -0.0; 0.0; -0.0 |] ]
          yield h "Rotor.log" "of-minus-one-has-no-plane" [ "v", Coefficients [| -1.0; 0.0; 0.0; 0.0 |] ]
          yield h "Rotor.log" "of-the-zero-rotor-has-no-plane" [ "v", Coefficients [| 0.0; 0.0; 0.0; 0.0 |] ]
          yield h "Rotor.log" "of-a-negative-zero-scalar-has-no-plane" [ "v", Coefficients [| -0.0; 0.0; 0.0; 0.0 |] ]
          yield h "Rotor.log" "of-the-least-positive-scalar" [ "v", Coefficients [| least; 0.0; 0.0; 0.0 |] ]
          yield h "Rotor.log" "of-the-least-bivector-part" [ "v", Coefficients [| -1.0; 0.0; least; 0.0 |] ]
          yield h "Rotor.log" "of-a-half-turn-is-half-pi" [ "v", Coefficients [| 0.0; -1.0; 0.0; 0.0 |] ]
          yield h "Rotor.log" "of-a-negative-scalar-goes-the-long-way" [ "v", Coefficients [| -0.6; 0.0; 0.8; 0.0 |] ]
          yield h "Rotor.log" "of-its-negative-goes-the-short-way" [ "v", Coefficients [| 0.6; 0.0; -0.8; 0.0 |] ]
          yield h "Rotor.log" "of-a-rotor-that-is-no-unit" [ "v", Coefficients [| 3.0; 0.0; 0.0; 4.0 |] ]
          yield
              h "Rotor.log" "at-an-arc-tangent-a-unit-from-correctly-rounded" [ "v", Coefficients [| x; y; 0.0; 0.0 |] ]
          yield h "Rotor.log" "of-an-infinite-bivector-part" [ "v", Coefficients [| 1.0; inf; 0.0; 0.0 |] ]
          yield h "Rotor.log" "of-an-infinite-scalar" [ "v", Coefficients [| inf; 1.0; 0.0; 0.0 |] ]
          yield h "Rotor.log" "of-a-rotor-that-holds-no-number" [ "v", Coefficients [| 1.0; 0.0; nan; 0.0 |] ]

          // ---- a rotor's interpolation
          for name, t in
              [ "at-zero", 0.0
                "at-one", 1.0
                "at-a-half", 0.5
                "past-one", 1.5
                "before-zero", -0.25 ] do
              yield
                  h
                      "Rotor.interpolate"
                      name
                      [ "t", N t
                        "first", RO quarter
                        "second", RO(ok (Rotor.ofAxisAngle (1.0, 2.0, 2.0) 2.0)) ]

          yield
              h
                  "Rotor.interpolate"
                  "at-zero-returns-a-negative-zero-positive"
                  [ "t", N 0.0
                    "first", Coefficients [| 1.0; -0.0; 0.0; -0.0 |]
                    "second", RO unitRotor ]

          yield
              h
                  "Rotor.interpolate"
                  "goes-the-long-way-round"
                  [ "t", N 0.5
                    "first", Coefficients identityRotor
                    "second", Coefficients [| -0.6; 0.0; 0.8; 0.0 |] ]

          yield
              h
                  "Rotor.interpolate"
                  "to-the-negated-rotor-goes-the-short-way"
                  [ "t", N 0.5
                    "first", Coefficients identityRotor
                    "second", Coefficients [| 0.6; 0.0; -0.8; 0.0 |] ]

          yield
              h
                  "Rotor.interpolate"
                  "at-an-arc-tangent-a-unit-from-correctly-rounded"
                  [ "t", N 0.5
                    "first", Coefficients identityRotor
                    "second", Coefficients [| x; y; 0.0; 0.0 |] ]

          // An interpolation steered so that its exponential asks the sine at the argument above:
          // from one to `exp (a e1∧e2)`, by the `t` for which `t` times the logarithm is that argument
          // exactly.
          let steeredBy, steeredTo =
              Seq.init 4096 (fun k -> float (k + 1) / 4096.0)
              |> Seq.pick (fun a ->
                  let second = ok (Rotor.exp (a, 0.0, 0.0))

                  match Rotor.log second with
                  | Ok(b, _, _) when b > 0.0 ->
                      let t = sineOffByOne / b
                      if t * b = sineOffByOne then Some(t, second) else None
                  | _ -> None)

          yield
              h
                  "Rotor.interpolate"
                  "at-a-sine-a-unit-from-correctly-rounded"
                  [ "t", N steeredBy
                    "first", Coefficients identityRotor
                    "second", RO steeredTo ]

          yield
              h
                  "Motor.interpolate"
                  "at-a-sine-a-unit-from-correctly-rounded"
                  [ "t", N steeredBy
                    "first", Coefficients identityMotor
                    "second", MO(Motor.ofRotor steeredTo) ]

          // From one to the rotor of a drawn case whose bivector part's norm a fused sum moves.
          yield
              h
                  "Rotor.interpolate"
                  "from-one-to-a-rotor-whose-norm-a-fused-sum-moves"
                  [ "t", N 0.5
                    "first", Coefficients identityRotor
                    "second",
                    (versorSearched 68
                     |> List.find (fun c -> c.Id = "Rotor.log-searched-1068")
                     |> fun c -> c.Input |> List.find (fun (name, _) -> name = "v") |> snd) ]

          yield
              h
                  "Rotor.interpolate"
                  "to-the-opposite-rotor-has-no-plane"
                  [ "t", N 0.5
                    "first", Coefficients identityRotor
                    "second", Coefficients [| -1.0; 0.0; 0.0; 0.0 |] ]

          yield
              h
                  "Rotor.interpolate"
                  "of-an-infinite-parameter"
                  [ "t", N inf; "first", RO quarter; "second", RO unitRotor ]

          yield
              h
                  "Rotor.interpolate"
                  "refuses-the-parameter-before-the-rotors"
                  [ "t", N nan
                    "first", Coefficients identityRotor
                    "second", Coefficients [| -1.0; 0.0; 0.0; 0.0 |] ]

          // ---- composition, and the laws on exact inputs
          yield
              h
                  "Rotor.compose"
                  "applies-the-second-operand-first"
                  [ "b", RO quarter; "a", RO(ok (Rotor.ofAxisAngle (1.0, 0.0, 0.0) 1.0)) ]

          yield
              h
                  "Rotor.compose"
                  "in-the-other-order"
                  [ "b", RO(ok (Rotor.ofAxisAngle (1.0, 0.0, 0.0) 1.0)); "a", RO quarter ]

          yield
              h
                  "Rotor.compose"
                  "of-a-rotor-with-its-reverse"
                  [ "b", RO unitRotor; "a", RO(ok (Rotor.ofAxisAngle (0.0, 0.0, 1.0) -1.0)) ]

          yield
              h
                  "Translator.compose"
                  "with-its-opposite-is-the-identity"
                  [ "b", TR(ok (Translator.ofVector (-1.5, 2.0, -0.25)))
                    "a", TR(ok (Translator.ofVector (1.5, -2.0, 0.25))) ]

          yield
              h
                  "Translator.compose"
                  "adds-the-two-vectors"
                  [ "b", TR(ok (Translator.ofVector (1.0, 2.0, 3.0)))
                    "a", TR(ok (Translator.ofVector (0.5, -4.0, 8.0))) ]

          for name, second, first in
              [ "of-x1-then-x2-is-minus-one-at-index-10", (0.0, 1.0, 0.0), (1.0, 0.0, 0.0)
                "of-x2-then-x3-is-minus-one-at-index-13", (0.0, 0.0, 1.0), (0.0, 1.0, 0.0)
                "of-x3-then-x1-is-one-at-index-11", (1.0, 0.0, 0.0), (0.0, 0.0, 1.0) ] do
              yield
                  h
                      "Reflection.compose"
                      name
                      [ "b", RE(ok (Reflection.ofNormalDistance second 0.0))
                        "a", RE(ok (Reflection.ofNormalDistance first 0.0)) ]

          yield
              h
                  "Reflection.compose"
                  "of-two-parallel-planes-is-a-translation"
                  [ "b", RE(ok (Reflection.ofNormalDistance (0.0, 0.0, 1.0) 3.0))
                    "a", RE(ok (Reflection.ofNormalDistance (0.0, 0.0, 1.0) 1.0)) ]

          yield
              h
                  "Motor.compose"
                  "of-a-translator-and-a-rotor-is-the-rotation-first"
                  [ "b", MO(Motor.ofTranslator (ok (Translator.ofVector (1.0, 2.0, 3.0))))
                    "a", MO(Motor.ofRotor quarter) ]

          yield
              h
                  "Motor.ofRotorTranslator"
                  "is-the-rotation-and-then-the-translation"
                  [ "rotor", RO quarter
                    "translator", TR(ok (Translator.ofVector (1.0, 2.0, 3.0))) ]

          yield
              h
                  "Dilator.compose"
                  "of-two-about-one-centre-multiplies-the-factors"
                  [ "b", DI(ok (Dilator.ofCentreFactor (1.0, 0.0, 0.0) 4.0))
                    "a", DI(ok (Dilator.ofCentreFactor (1.0, 0.0, 0.0) 0.25)) ]

          // ---- a translator
          yield h "Translator.ofVector" "halves-and-negates" [ "t", v3 1.0 -2.0 0.5 ]
          yield h "Translator.ofVector" "of-zeros-of-both-signs" [ "t", v3 0.0 -0.0 0.0 ]
          yield h "Translator.ofVector" "of-the-least-and-the-largest" [ "t", v3 least max -max ]
          yield h "Translator.ofVector" "of-an-infinite-component" [ "t", v3 0.0 0.0 inf ]
          yield h "Translator.ofVector" "of-a-component-that-is-no-number" [ "t", v3 nan 0.0 0.0 ]

          // ---- a screw
          yield
              h
                  "Motor.ofScrew"
                  "about-a-line-parallel-to-e3"
                  [ "point", v3 1.0 0.0 0.0
                    "direction", v3 0.0 0.0 2.0
                    "angle", N 1.0
                    "distance", N 5.0 ]

          yield
              h
                  "Motor.ofScrew"
                  "with-no-turn-is-a-translation"
                  [ "point", v3 1.0 2.0 3.0
                    "direction", v3 0.0 3.0 4.0
                    "angle", N 0.0
                    "distance", N 5.0 ]

          yield
              h
                  "Motor.ofScrew"
                  "with-no-distance-is-a-rotation-about-the-line"
                  [ "point", v3 1.0 2.0 3.0
                    "direction", v3 0.0 3.0 4.0
                    "angle", N 1.0
                    "distance", N 0.0 ]

          yield
              h
                  "Motor.ofScrew"
                  "at-a-sine-a-unit-from-correctly-rounded"
                  [ "point", v3 1.0 2.0 3.0
                    "direction", v3 0.0 0.0 1.0
                    "angle", N(2.0 * sineOffByOne)
                    "distance", N 1.0 ]

          for name, point, direction, angle, distance in
              [ "of-an-infinite-point", (inf, 0.0, 0.0), (0.0, 0.0, 1.0), 1.0, 1.0
                "of-a-zero-direction", (1.0, 0.0, 0.0), (0.0, 0.0, 0.0), 1.0, 1.0
                "of-a-direction-whose-norm-overflows", (1.0, 0.0, 0.0), (max, max, max), 1.0, 1.0
                "of-an-infinite-angle", (1.0, 0.0, 0.0), (0.0, 0.0, 1.0), inf, 1.0
                "of-an-infinite-distance", (1.0, 0.0, 0.0), (0.0, 0.0, 1.0), 1.0, inf
                "refuses-the-point-before-the-direction", (nan, 0.0, 0.0), (0.0, 0.0, 0.0), inf, nan
                "refuses-the-direction-before-the-angle", (1.0, 0.0, 0.0), (0.0, 0.0, 0.0), inf, nan
                "refuses-the-angle-before-the-distance", (1.0, 0.0, 0.0), (0.0, 0.0, 1.0), nan, inf
                "refuses-the-distance-before-it-computes", (max, max, max), (0.0, 0.0, 1.0), 1.0, nan
                "whose-arithmetic-overflows-into-no-number", (max, max, max), (1.0, 1.0, 1.0), 1.0, max ] do
              let (p1, p2, p3), (d1, d2, d3) = point, direction

              yield
                  h
                      "Motor.ofScrew"
                      name
                      [ "point", v3 p1 p2 p3
                        "direction", v3 d1 d2 d3
                        "angle", N angle
                        "distance", N distance ]

          yield h "Motor.ofLine" "about-the-first-axis" [ "line", L xAxis; "angle", N 1.0; "distance", N 2.0 ]

          yield
              h
                  "Motor.ofLine"
                  "divides-the-direction-by-the-weight"
                  [ "line", L(Line.through (u 1.0 0.0 0.0) (u 1.0 0.0 4.0))
                    "angle", N 1.0
                    "distance", N 5.0 ]

          yield
              h
                  "Motor.ofLine"
                  "at-a-sine-a-unit-from-correctly-rounded"
                  [ "line", L yAxis; "angle", N(2.0 * sineOffByOne); "distance", N 1.0 ]

          yield h "Motor.ofLine" "of-the-zero-line" [ "line", L zeroLine; "angle", N 1.0; "distance", N 1.0 ]
          yield h "Motor.ofLine" "of-a-line-at-infinity" [ "line", L parallelPlanes; "angle", N 1.0; "distance", N 1.0 ]

          yield
              h
                  "Motor.ofLine"
                  "of-a-line-that-holds-no-number"
                  [ "line", Coefficients [| nan; 0.0; 1.0; 0.0; 0.0; 0.0 |]
                    "angle", N 1.0
                    "distance", N 1.0 ]

          yield h "Motor.ofLine" "of-an-infinite-angle" [ "line", L xAxis; "angle", N inf; "distance", N 1.0 ]
          yield h "Motor.ofLine" "of-an-infinite-distance" [ "line", L xAxis; "angle", N 1.0; "distance", N -inf ]

          yield
              h
                  "Motor.ofLine"
                  "refuses-the-line-before-the-angle"
                  [ "line", L zeroLine; "angle", N nan; "distance", N nan ]

          yield
              h
                  "Motor.ofLine"
                  "refuses-the-angle-before-the-distance"
                  [ "line", L xAxis; "angle", N nan; "distance", N inf ]

          yield
              h
                  "Motor.ofLine"
                  "of-a-line-whose-nearest-point-overflows"
                  [ "line", Coefficients [| 0.0; -1.0; 0.07918293826789391; -max; max; 0.0 |]
                    "angle", N 1.0
                    "distance", N 1.0 ]

          // ---- the exponential and the logarithm of a motor
          yield h "Motor.exp" "of-zero-is-one" [ "bivector", v3 0.0 0.0 0.0; "vector", v3 0.0 0.0 0.0 ]

          yield
              h
                  "Motor.exp"
                  "of-a-vector-alone-is-one-and-the-vector"
                  [ "bivector", v3 -0.0 0.0 -0.0; "vector", v3 1.5 -0.0 -2.0 ]

          yield h "Motor.exp" "along-its-axis" [ "bivector", v3 0.5 0.0 0.0; "vector", v3 0.0 0.0 3.0 ]
          yield h "Motor.exp" "across-its-axis" [ "bivector", v3 0.5 0.0 0.0; "vector", v3 1.0 -2.0 0.0 ]
          yield h "Motor.exp" "of-the-least-bivector" [ "bivector", v3 0.0 least 0.0; "vector", v3 1.0 2.0 3.0 ]

          yield
              h
                  "Motor.exp"
                  "of-a-bivector-whose-square-underflows"
                  [ "bivector", v3 3e-170 -4e-170 1e-171; "vector", v3 1.0 2.0 3.0 ]

          yield
              h
                  "Motor.exp"
                  "at-a-sine-a-unit-from-correctly-rounded"
                  [ "bivector", v3 0.0 0.0 sineOffByOne; "vector", v3 1.0 2.0 3.0 ]

          yield h "Motor.exp" "of-an-infinite-bivector" [ "bivector", v3 inf 0.0 0.0; "vector", v3 1.0 2.0 3.0 ]

          yield
              h
                  "Motor.exp"
                  "of-a-bivector-whose-norm-overflows"
                  [ "bivector", v3 max max 0.0; "vector", v3 1.0 2.0 3.0 ]

          yield h "Motor.exp" "of-an-infinite-vector" [ "bivector", v3 0.5 0.0 0.0; "vector", v3 1.0 -inf 3.0 ]

          yield
              h
                  "Motor.exp"
                  "refuses-the-bivector-before-the-vector"
                  [ "bivector", v3 nan 0.0 0.0; "vector", v3 nan 0.0 0.0 ]

          yield h "Motor.exp" "of-the-largest-vector" [ "bivector", v3 0.5 0.25 0.125; "vector", v3 max max max ]
          yield h "Motor.log" "of-one-is-zero" [ "v", Coefficients identityMotor ]

          yield
              h
                  "Motor.log"
                  "of-a-translator-is-its-vector-part-itself"
                  [ "v", MO(Motor.ofTranslator (ok (Translator.ofVector (2.0, -4.0, 6.0)))) ]

          yield
              h
                  "Motor.log"
                  "of-a-scaled-translator-divides-nothing"
                  [ "v", Coefficients [| 2.0; -0.0; 0.0; 3.0; 0.0; -0.0; 5.0; 7.0 |] ]

          yield
              h
                  "Motor.log"
                  "of-minus-one-has-no-plane"
                  [ "v", Coefficients [| -1.0; 0.0; 0.0; 1.0; 0.0; 2.0; 3.0; 0.0 |] ]

          yield h "Motor.log" "of-the-zero-motor-has-no-plane" [ "v", Coefficients(Array.zeroCreate<float> 8) ]

          yield
              h
                  "Motor.log"
                  "of-a-screw-about-a-line-parallel-to-e3"
                  [ "v", MO(ok (Motor.ofScrew (1.0, 0.0, 0.0) (0.0, 0.0, 1.0) 1.0 5.0)) ]

          yield h "Motor.log" "of-a-rotor-has-no-vector" [ "v", MO(Motor.ofRotor quarter) ]

          yield
              h "Motor.log" "of-a-half-turn-screw" [ "v", Coefficients [| 0.0; -1.0; 0.0; 0.0; 0.0; 0.5; -1.0; 1.0 |] ]

          yield
              h
                  "Motor.log"
                  "of-a-negative-scalar-goes-the-long-way"
                  [ "v", Coefficients [| -0.6; 0.0; 0.8; 1.0; 0.0; 2.0; 3.0; 0.5 |] ]

          yield
              h
                  "Motor.log"
                  "at-an-arc-tangent-a-unit-from-correctly-rounded"
                  [ "v", Coefficients [| x; y; 0.0; 1.0; 0.0; 2.0; 3.0; 0.5 |] ]

          // A product that underflows to a negative zero: the fold answers `+0.0` at the first
          // component of the vector, where a product fused into the fold's zero answers `-0.0`.
          yield
              h
                  "Motor.log"
                  "whose-one-product-underflows-to-a-negative-zero"
                  [ "v", Coefficients [| -0.5; 1.0; 0.0; -0.0; 1e-300; -0.0; -0.0; -1e-100 |] ]

          yield
              h
                  "Motor.log"
                  "of-the-least-bivector-part"
                  [ "v", Coefficients [| 1.0; least; 0.0; 1.0; 0.0; 2.0; 3.0; least |] ]

          yield
              h
                  "Motor.log"
                  "of-a-motor-that-holds-no-number"
                  [ "v", Coefficients [| 1.0; 0.5; 0.0; 0.0; 0.0; 0.0; 0.0; nan |] ]

          yield
              h
                  "Motor.log"
                  "of-an-infinite-vector-part"
                  [ "v", Coefficients [| 1.0; 0.5; 0.0; inf; 0.0; 0.0; 0.0; 0.0 |] ]

          for name, t in [ "at-zero", 0.0; "at-one", 1.0; "at-a-half", 0.5; "past-one", 2.0 ] do
              yield
                  h
                      "Motor.interpolate"
                      name
                      [ "t", N t
                        "first", MO(ok (Motor.ofScrew (1.0, 0.0, 0.0) (0.0, 0.0, 1.0) 1.0 5.0))
                        "second", MO(ok (Motor.ofScrew (0.0, 2.0, 0.0) (1.0, 2.0, 2.0) -2.0 1.0)) ]

          yield
              h
                  "Motor.interpolate"
                  "between-two-translators-is-a-translator"
                  [ "t", N 0.25
                    "first", MO(Motor.ofTranslator (ok (Translator.ofVector (1.0, 2.0, 3.0))))
                    "second", MO(Motor.ofTranslator (ok (Translator.ofVector (5.0, -2.0, 7.0)))) ]

          yield
              h
                  "Motor.interpolate"
                  "goes-the-long-way-round"
                  [ "t", N 0.5
                    "first", Coefficients identityMotor
                    "second", Coefficients [| -0.6; 0.0; 0.8; 0.0; 0.0; 0.0; 0.0; 0.0 |] ]

          yield
              h
                  "Motor.interpolate"
                  "at-an-arc-tangent-a-unit-from-correctly-rounded"
                  [ "t", N 0.5
                    "first", Coefficients identityMotor
                    "second", Coefficients [| x; y; 0.0; 1.0; 0.0; 2.0; 3.0; 0.5 |] ]

          yield
              h
                  "Motor.interpolate"
                  "to-the-opposite-motor-has-no-plane"
                  [ "t", N 0.5
                    "first", Coefficients identityMotor
                    "second", Coefficients [| -1.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0; 0.0 |] ]

          yield
              h
                  "Motor.interpolate"
                  "of-a-parameter-that-is-no-number"
                  [ "t", N nan
                    "first", Coefficients identityMotor
                    "second", Coefficients identityMotor ]

          // ---- a dilator
          for name, s in
              [ "by-four-is-exact", 4.0
                "by-a-quarter-is-exact", 0.25
                "by-one-is-the-identity", 1.0
                "by-sixteen-is-exact", 16.0
                "by-two-takes-a-root", 2.0
                "by-three", 3.0
                "by-the-least-factor", least
                "by-the-largest-factor", max ] do
              yield h "Dilator.ofFactor" name [ "factor", N s ]

          yield h "Dilator.ofFactor" "of-a-zero-factor" [ "factor", N 0.0 ]
          yield h "Dilator.ofFactor" "of-a-negative-zero-factor" [ "factor", N -0.0 ]
          yield h "Dilator.ofFactor" "of-a-negative-factor" [ "factor", N -4.0 ]
          yield h "Dilator.ofFactor" "of-an-infinite-factor" [ "factor", N inf ]
          yield h "Dilator.ofFactor" "of-a-factor-that-is-no-number" [ "factor", N nan ]
          yield h "Dilator.ofCentreFactor" "by-four-about-a-point" [ "centre", v3 1.0 1.0 1.0; "factor", N 4.0 ]

          yield
              h
                  "Dilator.ofCentreFactor"
                  "about-the-origin-is-the-dilator-of-the-factor"
                  [ "centre", v3 0.0 0.0 0.0; "factor", N 3.0 ]

          yield h "Dilator.ofCentreFactor" "of-an-infinite-centre" [ "centre", v3 0.0 inf 0.0; "factor", N 4.0 ]

          yield
              h
                  "Dilator.ofCentreFactor"
                  "refuses-the-centre-before-the-factor"
                  [ "centre", v3 nan 0.0 0.0; "factor", N -1.0 ]

          yield
              h
                  "Dilator.ofCentreFactor"
                  "refuses-the-factor-before-it-computes"
                  [ "centre", v3 max max max; "factor", N 0.0 ]

          yield h "Dilator.ofCentreFactor" "whose-arithmetic-overflows" [ "centre", v3 max max max; "factor", N 4.0 ]

          yield
              h
                  "Dilator.applyToPoint"
                  "by-four-divides-the-weight-by-four"
                  [ "v", DI(ok (Dilator.ofCentreFactor (1.0, 1.0, 1.0) 4.0))
                    "x", P(u 2.0 1.0 0.0) ]

          yield
              h
                  "Dilator.applyToSphere"
                  "by-four-scales-the-radius"
                  [ "v", DI(ok (Dilator.ofFactor 4.0))
                    "x", S(ok (Sphere.ofCentreRadius (1.0, 0.0, 0.0) 1.0)) ]

          // ---- a reflection
          yield
              h
                  "Reflection.ofNormalDistance"
                  "divides-the-normal-by-its-norm"
                  [ "normal", v3 0.0 3.0 4.0; "distance", N 2.0 ]

          yield h "Reflection.ofNormalDistance" "of-a-zero-normal" [ "normal", v3 0.0 0.0 0.0; "distance", N 1.0 ]
          yield h "Reflection.ofNormalDistance" "of-an-infinite-normal" [ "normal", v3 0.0 0.0 -inf; "distance", N 1.0 ]

          yield
              h "Reflection.ofNormalDistance" "of-an-infinite-distance" [ "normal", v3 0.0 0.0 1.0; "distance", N inf ]

          yield
              h
                  "Reflection.ofNormalDistance"
                  "refuses-the-normal-before-the-distance"
                  [ "normal", v3 0.0 0.0 0.0; "distance", N nan ]

          yield
              h
                  "Reflection.applyToPoint"
                  "in-the-plane-x3-equals-one"
                  [ "v", RE(ok (Reflection.ofNormalDistance (0.0, 0.0, 1.0) 1.0)); "x", P p ]

          yield
              h
                  "Reflection.applyToLine"
                  "reverses-the-first-axis-in-the-plane-x1-equals-zero"
                  [ "v", RE(ok (Reflection.ofNormalDistance (1.0, 0.0, 0.0) 0.0)); "x", L xAxis ]

          // ---- applying: the reverse and never the inverse, and nothing normalised
          yield h "Rotor.applyToPoint" "about-e3-takes-e1-towards-e2" [ "v", RO unitRotor; "x", P(u 1.0 0.0 0.0) ]

          yield
              h
                  "Rotor.applyToPoint"
                  "of-a-rotor-twice-a-unit-scales-the-weight-by-four"
                  [ "v", Coefficients(times 2.0 (rotorValues unitRotor)); "x", P p ]

          yield
              h
                  "Translator.applyToPoint"
                  "moves-a-point-by-the-vector"
                  [ "v", TR(ok (Translator.ofVector (1.0, 2.0, 3.0))); "x", P(u 1.0 1.0 1.0) ]

          yield
              h
                  "Translator.applyToPoint"
                  "of-a-translator-that-is-no-unit"
                  [ "v", Coefficients [| 3.0; -1.5; 0.0; 0.75 |]; "x", P p ]

          yield
              h
                  "Motor.applyToPoint"
                  "is-the-rotation-and-then-the-translation"
                  [ "v", MO(Motor.ofRotorTranslator quarter (ok (Translator.ofVector (1.0, 2.0, 3.0))))
                    "x", P(u 1.0 0.0 0.0) ]

          yield
              h
                  "Motor.applyToLine"
                  "of-a-screw-keeps-its-own-axis"
                  [ "v", MO(ok (Motor.ofScrew (1.0, 0.0, 0.0) (0.0, 0.0, 1.0) 1.0 5.0))
                    "x", L(ok (Line.ofPointDirection (1.0, 0.0, 0.0) (0.0, 0.0, 1.0))) ]

          yield
              h
                  "Rotor.applyToPoint"
                  "of-a-point-of-zeros-of-both-signs"
                  [ "v", RO unitRotor; "x", Coefficients [| -0.0; 0.0; -0.0; 0.0; -0.0 |] ]

          yield
              h
                  "Rotor.applyToPoint"
                  "of-an-infinite-coefficient"
                  [ "v", Coefficients [| 1.0; inf; 0.0; 0.0 |]; "x", P p ]

          // ---- normalisation
          yield h "Rotor.normalise" "three-four-five" [ "v", Coefficients [| 3.0; 0.0; 4.0; 0.0 |] ]
          // A normalisation divides once: these coefficients, normalised a second time, each end one
          // unit in the last place higher.
          yield
              h
                  "Rotor.normalise"
                  "of-a-rotor-a-second-division-would-move"
                  [ "v",
                    Coefficients
                        [| 0.289994475309797
                           0.25182595232670363
                           0.10433806195987796
                           0.21266119422814747 |] ]

          // The square of this scalar part is subnormal and loses its last place, so its root is not
          // the scalar part and the quotient is not one: a second division would answer another bit.
          yield
              h
                  "Translator.normalise"
                  "of-a-scalar-part-whose-square-is-subnormal"
                  [ "v", Coefficients [| (1.0 + pow2 -52) * pow2 -520; pow2 -520; 0.0; 0.0 |] ]

          yield h "Rotor.normalise" "of-the-zero-rotor" [ "v", Coefficients [| 0.0; -0.0; 0.0; 0.0 |] ]
          yield h "Rotor.normalise" "of-a-squared-norm-that-overflows" [ "v", Coefficients [| max; max; 0.0; 0.0 |] ]

          yield
              h
                  "Rotor.normalise"
                  "of-a-squared-norm-that-underflows-to-zero"
                  [ "v", Coefficients [| pow2 -600; 0.0; 0.0; 0.0 |] ]

          yield h "Rotor.normalise" "of-the-least-squared-norm" [ "v", Coefficients [| pow2 -537; 0.0; 0.0; 0.0 |] ]
          yield h "Rotor.normalise" "of-a-rotor-that-holds-no-number" [ "v", Coefficients [| 1.0; nan; 0.0; 0.0 |] ]
          yield h "Translator.normalise" "divides-by-the-scalar-part" [ "v", Coefficients [| -4.0; 2.0; 0.0; -6.0 |] ]
          yield h "Translator.normalise" "of-a-zero-scalar-part" [ "v", Coefficients [| 0.0; 2.0; 0.0; -6.0 |] ]

          yield
              h
                  "Motor.normalise"
                  "ignores-the-parts-that-square-to-zero"
                  [ "v", Coefficients [| 3.0; 0.0; 4.0; 7.0; 0.0; 8.0; 9.0; 10.0 |] ]

          yield h "Dilator.normalise" "five-four-three" [ "v", Coefficients [| 5.0; 4.0; 1.0; 2.0; 3.0 |] ]
          yield h "Dilator.normalise" "of-a-negative-squared-norm" [ "v", Coefficients [| 4.0; 5.0; 0.0; 0.0; 0.0 |] ]
          yield h "Dilator.normalise" "of-a-zero-squared-norm" [ "v", Coefficients [| 4.0; -4.0; 1.0; 0.0; 0.0 |] ]
          yield h "Reflection.normalise" "three-four-five" [ "v", Coefficients [| 0.0; 3.0; 4.0; 10.0 |] ]
          yield h "Reflection.normalise" "of-a-plane-at-infinity" [ "v", Coefficients [| 0.0; 0.0; 0.0; 1.0 |] ]

          // ---- the copies
          for versor, size in [ "Rotor", 4; "Translator", 4; "Motor", 8; "Dilator", 5; "Reflection", 4 ] do
              yield
                  h
                      (versor + ".toMultivector")
                      "keeps-each-zero"
                      [ "v", Coefficients(Array.init size (fun i -> if i % 2 = 0 then -0.0 else 0.0)) ]

              yield h (versor + ".ofMultivector") "of-the-powers-of-two" [ "a", PowersOfTwo ]

          yield h "Motor.ofRotor" "keeps-each-zero" [ "rotor", Coefficients [| -0.0; 0.0; -0.0; 1.0 |] ]
          yield h "Motor.ofTranslator" "keeps-each-zero" [ "translator", Coefficients [| 1.0; -0.0; 0.0; -0.0 |] ]

          // ---- the matrix of a versor
          yield h "Rotor.toMatrix" "about-e3-is-column-major" [ "v", RO unitRotor ]

          yield
              h
                  "Rotor.toMatrix"
                  "of-a-rotor-twice-a-unit-is-the-same-rotation"
                  [ "v", Coefficients(times 2.0 (rotorValues unitRotor)) ]

          yield h "Rotor.toMatrix" "of-the-zero-rotor" [ "v", Coefficients [| 0.0; 0.0; 0.0; 0.0 |] ]
          yield h "Rotor.toMatrix" "of-a-weight-that-overflows" [ "v", Coefficients [| max; 0.0; 0.0; 0.0 |] ]
          yield h "Rotor.toMatrix" "of-a-rotor-that-holds-no-number" [ "v", Coefficients [| 1.0; nan; 0.0; 0.0 |] ]
          // A scalar part of `2^-500` and translation parts of `2^-600`: the weight, the scalar part's
          // square, is a double, and each product of the two underflows to a zero of its own sign. The
          // fold answers `+0.0` at each entry of the last column.
          yield
              h
                  "Translator.toMatrix"
                  "whose-translation-underflows-beside-a-weight-that-does-not"
                  [ "v", Coefficients [| pow2 -500; -(pow2 -600); pow2 -600; 0.0 |] ]

          yield
              h
                  "Translator.toMatrix"
                  "holds-the-vector-in-its-last-column"
                  [ "v", TR(ok (Translator.ofVector (1.0, 2.0, 3.0))) ]

          yield
              h
                  "Motor.toMatrix"
                  "of-a-screw-about-a-line-parallel-to-e3"
                  [ "v", MO(ok (Motor.ofScrew (1.0, 0.0, 0.0) (0.0, 0.0, 1.0) 1.0 5.0)) ]

          yield
              h "Dilator.toMatrix" "by-four-about-a-point" [ "v", DI(ok (Dilator.ofCentreFactor (1.0, 1.0, 1.0) 4.0)) ]

          yield h "Dilator.toMatrix" "of-a-zero-weight" [ "v", Coefficients [| 1.0; 1.0; 0.0; 0.0; 0.0 |] ]

          // ---- a matrix read as a motor and a factor
          let m (name: string) (tau: float) (matrix: float[]) : Case =
              h "Motor.ofMatrix" name [ "tau", N tau; "matrix", Coefficients matrix ]

          yield m "of-the-identity" 0.0 identityMatrix

          yield
              m
                  "of-a-quarter-turn-about-e3"
                  0.0
                  (similarity (0.0, 1.0, 0.0) (-1.0, 0.0, 0.0) (0.0, 0.0, 1.0) (1.0, 2.0, 3.0))

          yield
              m
                  "of-an-integer-similarity"
                  0.0
                  (similarity (6.0, 9.0, 18.0) (9.0, -18.0, 6.0) (18.0, 6.0, -9.0) (1.0, 2.0, 3.0))

          yield
              m
                  "of-a-uniform-scale-by-four"
                  0.0
                  (similarity (4.0, 0.0, 0.0) (0.0, 4.0, 0.0) (0.0, 0.0, 4.0) (-3.0, -3.0, -3.0))

          for name, a, b, c in
              [ "of-a-half-turn-about-e1", (1.0, 0.0, 0.0), (0.0, -1.0, 0.0), (0.0, 0.0, -1.0)
                "of-a-half-turn-about-e2", (-1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, -1.0)
                "of-a-half-turn-about-e3", (-1.0, 0.0, 0.0), (0.0, -1.0, 0.0), (0.0, 0.0, 1.0)
                "of-a-half-turn-about-e1-plus-e2", (0.0, 1.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, -1.0)
                "of-a-half-turn-about-e1-less-e2", (0.0, -1.0, 0.0), (-1.0, 0.0, 0.0), (0.0, 0.0, -1.0)
                "of-a-half-turn-about-e2-less-e3", (-1.0, 0.0, 0.0), (0.0, 0.0, -1.0), (0.0, -1.0, 0.0)
                "of-a-half-turn-about-e1-less-e3", (0.0, 0.0, -1.0), (0.0, -1.0, 0.0), (-1.0, 0.0, 0.0)
                "of-a-third-of-a-turn-about-the-diagonal", (0.0, 1.0, 0.0), (0.0, 0.0, 1.0), (1.0, 0.0, 0.0)
                "of-a-quarter-turn-about-e1", (1.0, 0.0, 0.0), (0.0, 0.0, 1.0), (0.0, -1.0, 0.0)
                "of-a-quarter-turn-about-e2", (0.0, 0.0, -1.0), (0.0, 1.0, 0.0), (1.0, 0.0, 0.0) ] do
              yield m name 0.0 (similarity a b c (0.0, 0.0, 0.0))

          yield m "of-a-mirror" 0.0 (similarity (-1.0, 0.0, 0.0) (0.0, 1.0, 0.0) (0.0, 0.0, 1.0) (0.0, 0.0, 0.0))
          yield m "of-a-shear" 0.0 (similarity (1.0, 0.0, 0.0) (1.0, 1.0, 0.0) (0.0, 0.0, 1.0) (0.0, 0.0, 0.0))

          yield
              m
                  "of-a-scale-that-is-not-uniform"
                  0.0
                  (similarity (2.0, 0.0, 0.0) (0.0, 3.0, 0.0) (0.0, 0.0, 2.0) (0.0, 0.0, 0.0))

          yield
              m
                  "of-a-third-column-of-another-length"
                  0.0
                  (similarity (2.0, 0.0, 0.0) (0.0, 2.0, 0.0) (0.0, 0.0, 3.0) (0.0, 0.0, 0.0))

          yield
              m
                  "of-columns-that-are-not-orthogonal"
                  0.0
                  (similarity (3.0, 4.0, 0.0) (4.0, 3.0, 0.0) (0.0, 0.0, 5.0) (0.0, 0.0, 0.0))

          yield m "of-a-zero-column" 0.0 (similarity (0.0, 0.0, 0.0) (0.0, 1.0, 0.0) (0.0, 0.0, 1.0) (0.0, 0.0, 0.0))

          yield
              m
                  "of-a-zero-third-column-under-a-large-threshold"
                  2.0
                  (similarity (1.0, 0.0, 0.0) (0.0, 1.0, 0.0) (0.0, 0.0, 0.0) (0.0, 0.0, 0.0))

          yield
              m
                  "of-columns-whose-squares-underflow"
                  0.0
                  (similarity (pow2 -600, 0.0, 0.0) (0.0, pow2 -600, 0.0) (0.0, 0.0, pow2 -600) (0.0, 0.0, 0.0))

          yield
              m
                  "of-columns-whose-squares-overflow"
                  0.0
                  (similarity (pow2 600, 0.0, 0.0) (0.0, pow2 600, 0.0) (0.0, 0.0, pow2 600) (0.0, 0.0, 0.0))
          // A rotation with every entry times a power of two. Its columns' squared lengths are plain
          // sums of squares: at `2^-519` it is read; at `2^-530` the squares are subnormal, their
          // rounding is the statistic, and the refusal's name does not describe the matrix; at
          // `2^-537` a square is zero.
          let turned = ok (Rotor.toMatrix (ok (Rotor.ofAxisAngle (1.0, 2.0, 2.0) 1.0)))

          for name, power in
              [ "of-a-rotation-at-the-smallest-scale-it-reads", -519
                "of-a-rotation-whose-squares-are-subnormal", -530
                "of-a-rotation-whose-squares-underflow", -537
                "of-a-rotation-at-the-largest-scale-it-reads", 511
                "of-a-rotation-whose-squares-overflow", 512 ] do
              yield
                  m
                      name
                      (pow2 -40)
                      (turned
                       |> Array.mapi (fun i x -> if i % 4 < 3 && i < 12 then x * pow2 power else x))

          yield
              m
                  "of-the-largest-columns-it-reads"
                  0.0
                  (similarity (pow2 511, 0.0, 0.0) (0.0, pow2 511, 0.0) (0.0, 0.0, pow2 511) (max, -max, 0.0))

          yield m "of-a-last-row-that-is-not-affine" 0.0 (withEntry 3 (pow2 -1074) identityMatrix)
          yield m "of-a-last-entry-that-is-not-one" 0.0 (withEntry 15 (1.0 + pow2 -52) identityMatrix)

          yield
              m
                  "of-a-last-row-of-negative-zeros"
                  0.0
                  (identityMatrix |> withEntry 3 -0.0 |> withEntry 7 -0.0 |> withEntry 11 -0.0)

          yield m "of-an-infinite-entry" 0.0 (withEntry 12 inf identityMatrix)
          yield m "of-an-entry-that-is-no-number" 0.0 (withEntry 5 nan identityMatrix)
          yield m "of-fifteen-entries" 0.0 (Array.sub identityMatrix 0 15)
          yield m "of-seventeen-entries" 0.0 (Array.append identityMatrix [| 0.0 |])
          yield m "of-no-entry" 0.0 [||]
          yield m "of-a-negative-threshold" -1.0 identityMatrix
          yield m "of-a-negative-zero-threshold" -0.0 identityMatrix
          yield m "of-an-infinite-threshold" inf identityMatrix
          yield m "of-a-threshold-that-is-no-number" nan identityMatrix
          yield m "refuses-the-threshold-before-the-matrix" -1.0 [||]
          yield m "refuses-the-length-before-an-entry" 0.0 [| nan |]
          yield m "refuses-an-entry-before-the-last-row" 0.0 (identityMatrix |> withEntry 3 1.0 |> withEntry 0 inf)

          // A statistic at its threshold, from within and from above.
          let slanted =
              similarity (1.0, 0.0, 0.0) (pow2 -10, 1.0, 0.0) (0.0, 0.0, 1.0) (0.0, 0.0, 0.0)

          let longer =
              similarity (1.0, 0.0, 0.0) (0.0, 1.0, 0.0) (0.0, 0.0, 1.0 + pow2 -10) (0.0, 0.0, 0.0)

          yield m "of-a-slanted-column-at-a-threshold-of-zero" 0.0 slanted
          yield m "of-a-slanted-column-below-its-threshold" (pow2 -11) slanted
          yield m "of-a-slanted-column-within-a-wide-threshold" (pow2 -9) slanted
          yield m "of-a-longer-column-at-a-threshold-of-zero" 0.0 longer
          yield m "of-a-longer-column-below-its-threshold" (pow2 -10) longer
          yield m "of-a-longer-column-within-a-wide-threshold" (pow2 -8) longer

          let shorter =
              similarity (1.0, 0.0, 0.0) (0.0, 1.0 - pow2 -10, 0.0) (0.0, 0.0, 1.0) (0.0, 0.0, 0.0)

          // A statistic that equals its threshold is within it.
          let ownThreshold (matrix: float[]) : float =
              match Motor.ofMatrix 0.0 matrix with
              | Error(MatrixRefusal.NotOneLength q)
              | Error(MatrixRefusal.NotOrthogonal q) -> if q < 0.0 then -q else q
              | _ -> failwith "an authored matrix was not refused for a statistic"

          yield m "of-a-slanted-column-at-its-own-threshold" (ownThreshold slanted) slanted
          yield m "of-a-longer-column-at-its-own-threshold" (ownThreshold longer) longer
          yield m "of-a-shorter-column-at-a-threshold-of-zero" 0.0 shorter
          yield m "of-a-shorter-column-at-its-own-threshold" (ownThreshold shorter) shorter
          yield m "of-a-shorter-column-below-its-threshold" (pow2 -12) shorter

          // Half turns about an axis with integer components, `2 a aᵀ / |a|² − 1` formed in doubles,
          // at which the rule for the rotor's sign decides: the axis component of largest magnitude
          // comes out negative at the first, and two components tie at the second.
          for name, (a1, a2, a3) in
              [ "of-a-half-turn-whose-largest-component-comes-out-negative", (-5.0, -3.0, 5.0)
                "of-a-half-turn-whose-two-largest-components-tie", (-6.0, -3.0, 6.0) ] do
              let a = [| a1; a2; a3 |]
              let n2 = a1 * a1 + a2 * a2 + a3 * a3

              let entry (i: int) (j: int) : float =
                  2.0 * a[i] * a[j] / n2 - (if i = j then 1.0 else 0.0)

              yield
                  m
                      name
                      (pow2 -40)
                      (similarity
                          (entry 0 0, entry 1 0, entry 2 0)
                          (entry 0 1, entry 1 1, entry 2 1)
                          (entry 0 2, entry 1 2, entry 2 2)
                          (0.0, 0.0, 0.0))

          // At a threshold of zero the signs decide: a quotient that underflows to a zero is no zero
          // statistic.
          for name, sign in [ "positive", 1.0; "negative", -1.0 ] do
              let underflowing =
                  similarity (pow2 500, 0.0, 0.0) (sign * pow2 -600, pow2 500, 0.0) (0.0, 0.0, pow2 500) (0.0, 0.0, 0.0)

              yield m (sprintf "of-a-%s-cosine-that-underflows-at-a-threshold-of-zero" name) 0.0 underflowing
              yield m (sprintf "of-a-%s-cosine-that-underflows-at-the-least-threshold" name) least underflowing ]

    /// The cases a search found: a drawn configuration's case of one function, kept because a wrong
    /// port fails it where it failed no earlier vector of that function (DECISIONS.md D88).
    let private versorChosen: (int * string) list =
        [ 3, "Motor.normalise-searched-1003"
          17, "Reflection.normalise-searched-1017"
          7, "Dilator.normalise-searched-1007"
          33, "Motor.normalise-searched-1033"
          37, "Reflection.normalise-searched-1037"
          17, "Rotor.normalise-searched-1017"
          2, "Translator.applyToPlane-searched-1002"
          7, "Motor.ofMatrix-searched-of-a-slanted-1007"
          36, "Motor.exp-searched-1036"
          5, "Motor.interpolate-searched-1005"
          23, "Motor.log-searched-1023"
          26, "Motor.ofLine-searched-of-a-weighted-line-1026"
          5, "Reflection.ofNormalDistance-searched-1005"
          76, "Rotor.exp-searched-1076"
          68, "Rotor.log-searched-1068"
          9, "Motor.exp-searched-1009"
          8, "Motor.interpolate-searched-1008"
          3, "Motor.ofLine-searched-1003"
          9, "Rotor.exp-searched-1009"
          0, "Rotor.log-searched-1000"
          4, "Rotor.ofAxisAngle-searched-1004"
          27, "Motor.log-searched-1027"
          27, "Motor.ofScrew-searched-1027"
          0, "Rotor.ofAxisAngle-searched-1000"
          0, "Motor.normalise-searched-1000"
          0, "Motor.toMatrix-searched-1000"
          0, "Translator.toMatrix-searched-1000"
          0, "Motor.ofLine-searched-of-a-weighted-line-1000"
          0, "Motor.ofScrew-searched-1000" ]

    let private versorWitnesses: Case list =
        [ for index, id in versorChosen -> versorSearched index |> List.find (fun c -> c.Id = id) ]

    /// Two vectors of Phase 728's objects that Phase 729 appends (DECISIONS.md D88 item 11): a point
    /// pair and a line whose normalisation, applied a second time, would answer another bit. No earlier
    /// vector of either function fails a port that normalises twice.
    let objectCasesAppended: Case list =
        [ case
              "PointPair.normalise-of-a-pair-a-second-division-would-move"
              "PointPair.normalise"
              [ "x",
                Coefficients
                    [| 1.4615937732160091
                       0.5930597633123398
                       -4.41215181723237
                       7.571844474417995
                       -2.3019526424929087
                       2.2106695274283705
                       -7.495698132052895
                       -6.0519588617268925
                       8.883884615767856
                       11.175001281512586 |] ]
          case
              "Line.normalise-of-a-line-a-second-division-would-move"
              "Line.normalise"
              [ "x",
                Coefficients
                    [| -1.3172310665249825
                       -0.06084183603525162
                       -0.8962686248123646
                       -2.372059167667058
                       3.6911205773358375
                       1.784483239779819 |] ] ]

    /// The versors' cases, in document order, and after them the two of the objects this phase appends.
    let versorCases: Case list =
        versorGeneral @ versorHard @ versorWitnesses @ objectCasesAppended

    /// Every case, in document order: Phase 727's, and then the ones appended since.
    let cases: Case list =
        tableRows
        @ hardInputs
        @ generalInputs
        @ carried
        @ conformalGeneral
        @ conformalHard
        @ underflowing
        @ refusedInOrder
        @ undivided
        @ drawnWitnesses
        @ flatSpheres
        @ signedZeros
        @ nearestOverflows
        @ laterWitnesses
        @ versorCases

    /// The case with this id.
    let find (id: string) : Case = cases |> List.find (fun c -> c.Id = id)

    // ---- the answers and the document --------------------------------------------------------------

    /// The operand of this name.
    let operand (c: Case) (name: string) : Operand =
        c.Input |> List.find (fun (n, _) -> n = name) |> snd

    let mv (c: Case) (name: string) : Multivector = multivector (operand c name)

    let number (c: Case) (name: string) : float =
        match operand c name with
        | Number x -> x
        | _ -> failwithf "%s: %s is not a number" c.Id name

    let private whole (c: Case) (name: string) : int =
        match operand c name with
        | Whole n -> n
        | _ -> failwithf "%s: %s is not an integer" c.Id name

    /// The sixteen hex digits of each coefficient, in index order.
    let hexes (a: Multivector) : string list =
        Multivector.toArray a |> Array.map ElementaryVectors.hex |> List.ofArray

    let multivectorCell (a: Multivector) : CorpusCell =
        CorpusCell.List [ for digits in hexes a -> CorpusFile.str digits ]

    let floatCell (x: float) : CorpusCell =
        CorpusFile.str (ElementaryVectors.hex x)

    // ---- the conformal objects' answers (Phase 728) ------------------------------------------------

    /// The floats of an operand written as a list: a typed object's coefficients, a triple, a
    /// multivector.
    let floats (c: Case) (name: string) : float[] =
        match operand c name with
        | Coefficients xs -> xs
        | _ -> failwithf "%s: %s is not a list of floats" c.Id name

    let triple (c: Case) (name: string) : float * float * float =
        let v = floats c name
        v[0], v[1], v[2]

    let floatsCell (xs: float[]) : CorpusCell =
        CorpusCell.List [ for x in xs -> floatCell x ]

    let tripleCell (a: float, b: float, c: float) : CorpusCell = floatsCell [| a; b; c |]
    let pointCell (p: Point) : CorpusCell = floatsCell (pointValues p)
    let pairCell (x: PointPair) : CorpusCell = floatsCell (pairValues x)
    let circleCell (x: Circle) : CorpusCell = floatsCell (circleValues x)
    let lineCell (x: Line) : CorpusCell = floatsCell (lineValues x)
    let sphereCell (x: Sphere) : CorpusCell = floatsCell (sphereValues x)
    let planeCell (x: Plane) : CorpusCell = floatsCell (planeValues x)

    /// A constructor refusal by its case name; the op algebra's document names one the same way.
    let internal refusalName (refusal: GeometryRefusal) : string =
        match refusal with
        | GeometryRefusal.NonFiniteCoordinate -> "NonFiniteCoordinate"
        | GeometryRefusal.ZeroDirection -> "ZeroDirection"
        | GeometryRefusal.NonFiniteDirection -> "NonFiniteDirection"
        | GeometryRefusal.NegativeRadius -> "NegativeRadius"
        | GeometryRefusal.NonFiniteRadius -> "NonFiniteRadius"
        | GeometryRefusal.NonFiniteDistance -> "NonFiniteDistance"
        | GeometryRefusal.ZeroDivisor -> "ZeroDivisor"
        | GeometryRefusal.NonFiniteDivisor -> "NonFiniteDivisor"
        | GeometryRefusal.InvalidThreshold -> "InvalidThreshold"
        | GeometryRefusal.NotANumber -> "NotANumber"
        | GeometryRefusal.NonFiniteAngle -> "NonFiniteAngle"
        | GeometryRefusal.NonFiniteTranslation -> "NonFiniteTranslation"
        | GeometryRefusal.NonFiniteFactor -> "NonFiniteFactor"
        | GeometryRefusal.NonPositiveFactor -> "NonPositiveFactor"
        | GeometryRefusal.NonFiniteBivector -> "NonFiniteBivector"
        | GeometryRefusal.NonFiniteParameter -> "NonFiniteParameter"
        | GeometryRefusal.NegativeSquaredNorm -> "NegativeSquaredNorm"
        | GeometryRefusal.NoRotationPlane -> "NoRotationPlane"

    /// An answer that can be refused: the answer's own cell, or `{ refused: <the case> }`.
    let resultCell (ok: 'a -> CorpusCell) (r: Result<'a, GeometryRefusal>) : CorpusCell =
        match r with
        | Ok x -> ok x
        | Error refusal -> CorpusCell.Row [ "refused", CorpusFile.str (refusalName refusal) ]

    /// An answer that has kinds: `{ kind: <the case>, … what it carries }`.
    let private kindCell (name: string) (carried: (string * CorpusCell) list) : CorpusCell =
        CorpusCell.Row(("kind", CorpusFile.str name) :: carried)

    let private round (reality: Reality) : CorpusCell =
        let name =
            match reality with
            | Reality.Real -> "Real"
            | Reality.Tangent -> "Tangent"
            | Reality.Imaginary -> "Imaginary"

        kindCell "Round" [ "reality", CorpusFile.str name ]

    let pairKindCell (kind: PointPairKind) : CorpusCell =
        match kind with
        | PointPairKind.Round reality -> round reality
        | PointPairKind.FlatPoint(a, b, c) -> kindCell "FlatPoint" [ "point", tripleCell (a, b, c) ]
        | PointPairKind.AtInfinity -> kindCell "AtInfinity" []
        | PointPairKind.Zero -> kindCell "Zero" []
        | PointPairKind.NoObject -> kindCell "NoObject" []

    let circleKindCell (kind: CircleKind) : CorpusCell =
        match kind with
        | CircleKind.Round reality -> round reality
        | CircleKind.Flat line -> kindCell "Flat" [ "line", lineCell line ]
        | CircleKind.AtInfinity -> kindCell "AtInfinity" []
        | CircleKind.Zero -> kindCell "Zero" []
        | CircleKind.NoObject -> kindCell "NoObject" []

    let sphereKindCell (kind: SphereKind) : CorpusCell =
        match kind with
        | SphereKind.Round reality -> round reality
        | SphereKind.Flat plane -> kindCell "Flat" [ "plane", planeCell plane ]
        | SphereKind.AtInfinity -> kindCell "AtInfinity" []
        | SphereKind.Zero -> kindCell "Zero" []
        | SphereKind.NoObject -> kindCell "NoObject" []

    let flatKindCell (kind: FlatKind) : CorpusCell =
        match kind with
        | FlatKind.Flat -> kindCell "Flat" []
        | FlatKind.AtInfinity -> kindCell "AtInfinity" []
        | FlatKind.Zero -> kindCell "Zero" []
        | FlatKind.NoObject -> kindCell "NoObject" []

    let pairPointsCell (points: PairPoints) : CorpusCell =
        match points with
        | PairPoints.Real(first, second) -> kindCell "Real" [ "first", pointCell first; "second", pointCell second ]
        | PairPoints.Tangent(a, b, c) -> kindCell "Tangent" [ "centre", tripleCell (a, b, c) ]
        | PairPoints.Imaginary(centre, squaredRadius) ->
            kindCell "Imaginary" [ "centre", tripleCell centre; "squaredRadius", floatCell squaredRadius ]

    let lineMeetCell (met: LineMeet) : CorpusCell =
        match met with
        | LineMeet.Within pair -> kindCell "Within" [ "pair", pairCell pair ]
        | LineMeet.SameLine -> kindCell "SameLine" []
        | LineMeet.Skew point -> kindCell "Skew" [ "point", pointCell point ]

    let sideCell (side: Side) : CorpusCell =
        match side with
        | Side.Inside -> kindCell "Inside" []
        | Side.On -> kindCell "On" []
        | Side.Outside -> kindCell "Outside" []

    let planeSideCell (side: PlaneSide) : CorpusCell =
        match side with
        | PlaneSide.Negative -> kindCell "Negative" []
        | PlaneSide.On -> kindCell "On" []
        | PlaneSide.Positive -> kindCell "Positive" []

    /// A conformal case's answer, by calling the package's published function.
    let private conformalAnswer (c: Case) : CorpusCell =
        let point (name: string) = pointOf (floats c name)
        let pair () = pairOf (floats c "x")
        let line (name: string) = lineOf (floats c name)
        let circle () = circleOf (floats c "x")
        let plane (name: string) = planeOf (floats c name)
        let sphere (name: string) = sphereOf (floats c name)
        let tau () = number c "tau"

        match c.Op with
        | "Blade.join" -> multivectorCell (Blade.join (mv c "a") (mv c "b"))
        | "Blade.meet" -> multivectorCell (Blade.meet (mv c "a") (mv c "b"))
        | "Blade.inverse" -> resultCell multivectorCell (Blade.inverse (mv c "a"))
        | "Blade.meetWithin" -> resultCell multivectorCell (Blade.meetWithin (mv c "j") (mv c "a") (mv c "b"))
        | "Point.up" -> resultCell pointCell (Point.up (triple c "x"))
        | "Point.coordinates" -> resultCell tripleCell (Point.coordinates (point "x"))
        | "Point.weight" -> floatCell (Point.weight (point "x"))
        | "Point.normalise" -> resultCell pointCell (Point.normalise (point "x"))
        | "Point.toMultivector" -> multivectorCell (Point.toMultivector (point "x"))
        | "Point.ofMultivector" -> pointCell (Point.ofMultivector (mv c "a"))
        | "PointPair.through" -> pairCell (PointPair.through (point "p") (point "q"))
        | "PointPair.weight" -> floatCell (PointPair.weight (pair ()))
        | "PointPair.normalise" -> resultCell pairCell (PointPair.normalise (pair ()))
        | "PointPair.centre" -> resultCell tripleCell (PointPair.centre (pair ()))
        | "PointPair.squaredRadius" -> resultCell floatCell (PointPair.squaredRadius (pair ()))
        | "PointPair.points" -> resultCell pairPointsCell (PointPair.points (tau ()) (pair ()))
        | "PointPair.kind" -> resultCell pairKindCell (PointPair.kind (tau ()) (pair ()))
        | "PointPair.toMultivector" -> multivectorCell (PointPair.toMultivector (pair ()))
        | "PointPair.ofMultivector" -> pairCell (PointPair.ofMultivector (mv c "a"))
        | "Line.through" -> lineCell (Line.through (point "p") (point "q"))
        | "Line.ofPointDirection" -> resultCell lineCell (Line.ofPointDirection (triple c "p") (triple c "d"))
        | "Line.weight" -> floatCell (Line.weight (line "x"))
        | "Line.normalise" -> resultCell lineCell (Line.normalise (line "x"))
        | "Line.direction" -> tripleCell (Line.direction (line "x"))
        | "Line.moment" -> tripleCell (Line.moment (line "x"))
        | "Line.nearestPoint" -> resultCell tripleCell (Line.nearestPoint (line "x"))
        | "Line.kind" -> resultCell flatKindCell (Line.kind (tau ()) (line "x"))
        | "Line.meet" -> resultCell lineMeetCell (Line.meet (tau ()) (line "x") (line "y"))
        | "Line.meetPlane" -> pairCell (Line.meetPlane (line "x") (plane "y"))
        | "Line.meetSphere" -> pairCell (Line.meetSphere (line "x") (sphere "y"))
        | "Line.toMultivector" -> multivectorCell (Line.toMultivector (line "x"))
        | "Line.ofMultivector" -> lineCell (Line.ofMultivector (mv c "a"))
        | "Circle.through" -> circleCell (Circle.through (point "p") (point "q") (point "r"))
        | "Circle.ofCentreRadiusNormal" ->
            resultCell
                circleCell
                (Circle.ofCentreRadiusNormal (triple c "centre") (number c "radius") (triple c "normal"))
        | "Circle.weight" -> floatCell (Circle.weight (circle ()))
        | "Circle.normalise" -> resultCell circleCell (Circle.normalise (circle ()))
        | "Circle.centre" -> resultCell tripleCell (Circle.centre (circle ()))
        | "Circle.squaredRadius" -> resultCell floatCell (Circle.squaredRadius (circle ()))
        | "Circle.kind" -> resultCell circleKindCell (Circle.kind (tau ()) (circle ()))
        | "Circle.plane" -> planeCell (Circle.plane (circle ()))
        | "Circle.power" -> resultCell floatCell (Circle.power (point "p") (circle ()))
        | "Circle.side" -> resultCell sideCell (Circle.side (tau ()) (point "p") (circle ()))
        | "Circle.toMultivector" -> multivectorCell (Circle.toMultivector (circle ()))
        | "Circle.ofMultivector" -> circleCell (Circle.ofMultivector (mv c "a"))
        | "Plane.through" -> planeCell (Plane.through (point "p") (point "q") (point "r"))
        | "Plane.ofNormalDistance" ->
            resultCell planeCell (Plane.ofNormalDistance (triple c "normal") (number c "distance"))
        | "Plane.weight" -> floatCell (Plane.weight (plane "x"))
        | "Plane.normalise" -> resultCell planeCell (Plane.normalise (plane "x"))
        | "Plane.normal" -> tripleCell (Plane.normal (plane "x"))
        | "Plane.distance" -> floatCell (Plane.distance (plane "x"))
        | "Plane.nearestPoint" -> resultCell tripleCell (Plane.nearestPoint (plane "x"))
        | "Plane.kind" -> resultCell flatKindCell (Plane.kind (tau ()) (plane "x"))
        | "Plane.meet" -> lineCell (Plane.meet (plane "x") (plane "y"))
        | "Plane.signedDistance" -> resultCell floatCell (Plane.signedDistance (point "p") (plane "x"))
        | "Plane.side" -> resultCell planeSideCell (Plane.side (tau ()) (point "p") (plane "x"))
        | "Plane.toMultivector" -> multivectorCell (Plane.toMultivector (plane "x"))
        | "Plane.ofMultivector" -> planeCell (Plane.ofMultivector (mv c "a"))
        | "Sphere.through" -> sphereCell (Sphere.through (point "p") (point "q") (point "r") (point "s"))
        | "Sphere.ofCentreRadius" ->
            resultCell sphereCell (Sphere.ofCentreRadius (triple c "centre") (number c "radius"))
        | "Sphere.weight" -> floatCell (Sphere.weight (sphere "x"))
        | "Sphere.normalise" -> resultCell sphereCell (Sphere.normalise (sphere "x"))
        | "Sphere.centre" -> resultCell tripleCell (Sphere.centre (sphere "x"))
        | "Sphere.squaredRadius" -> resultCell floatCell (Sphere.squaredRadius (sphere "x"))
        | "Sphere.kind" -> resultCell sphereKindCell (Sphere.kind (tau ()) (sphere "x"))
        | "Sphere.meet" -> circleCell (Sphere.meet (sphere "x") (sphere "y"))
        | "Sphere.meetPlane" -> circleCell (Sphere.meetPlane (sphere "x") (plane "y"))
        | "Sphere.power" -> resultCell floatCell (Sphere.power (point "p") (sphere "x"))
        | "Sphere.side" -> resultCell sideCell (Sphere.side (tau ()) (point "p") (sphere "x"))
        | "Sphere.toMultivector" -> multivectorCell (Sphere.toMultivector (sphere "x"))
        | "Sphere.ofMultivector" -> sphereCell (Sphere.ofMultivector (mv c "a"))
        | other -> failwithf "%s: no such operation as %s" c.Id other

    // ---- the versors' answers (Phase 729) ------------------------------------------------------------

    let rotorCell (x: Rotor) : CorpusCell = floatsCell (rotorValues x)
    let translatorCell (x: Translator) : CorpusCell = floatsCell (translatorValues x)
    let motorCell (x: Motor) : CorpusCell = floatsCell (motorValues x)
    let dilatorCell (x: Dilator) : CorpusCell = floatsCell (dilatorValues x)
    let reflectionCell (x: Reflection) : CorpusCell = floatsCell (reflectionValues x)

    /// A motor's logarithm: `{ bivector, vector }`, three coefficients each.
    let bivectorAndVectorCell (b: float * float * float, v: float * float * float) : CorpusCell =
        CorpusCell.Row [ "bivector", tripleCell b; "vector", tripleCell v ]

    /// A matrix read back: `{ motor, factor }`, or `{ refused: <the case> }` with the measure that
    /// failed where the case carries one.
    let motorAndFactorCell (r: Result<Motor * float, MatrixRefusal>) : CorpusCell =
        let refused (name: string) (measure: float option) : CorpusCell =
            CorpusCell.Row(
                ("refused", CorpusFile.str name)
                :: (match measure with
                    | Some x -> [ "measure", floatCell x ]
                    | None -> [])
            )

        match r with
        | Ok(motor, factor) -> CorpusCell.Row [ "motor", motorCell motor; "factor", floatCell factor ]
        | Error MatrixRefusal.InvalidThreshold -> refused "InvalidThreshold" None
        | Error MatrixRefusal.NotSixteenEntries -> refused "NotSixteenEntries" None
        | Error MatrixRefusal.NonFiniteEntry -> refused "NonFiniteEntry" None
        | Error MatrixRefusal.NotAffine -> refused "NotAffine" None
        | Error MatrixRefusal.ZeroDivisor -> refused "ZeroDivisor" None
        | Error MatrixRefusal.NonFiniteDivisor -> refused "NonFiniteDivisor" None
        | Error(MatrixRefusal.NotOneLength x) -> refused "NotOneLength" (Some x)
        | Error(MatrixRefusal.NotOrthogonal x) -> refused "NotOrthogonal" (Some x)
        | Error(MatrixRefusal.ReversesOrientation x) -> refused "ReversesOrientation" (Some x)
        | Error MatrixRefusal.NotANumber -> refused "NotANumber" None

    /// Whether an `op` names a function of a versor's module.
    let isVersorOp (op: string) : bool =
        versorTypes |> List.exists (fun versor -> op.StartsWith(versor + "."))

    /// A versor case's answer, by calling the package's published function.
    let private versorAnswer (c: Case) : CorpusCell =
        match c.Op with
        | "Rotor.ofAxisAngle" -> resultCell rotorCell (Rotor.ofAxisAngle (triple c "axis") (number c "angle"))
        | "Rotor.exp" -> resultCell rotorCell (Rotor.exp (triple c "bivector"))
        | "Rotor.log" -> resultCell tripleCell (Rotor.log (rotorOf (floats c "v")))
        | "Rotor.interpolate" ->
            resultCell
                rotorCell
                (Rotor.interpolate (number c "t") (rotorOf (floats c "first")) (rotorOf (floats c "second")))
        | "Translator.ofVector" -> resultCell translatorCell (Translator.ofVector (triple c "t"))
        | "Motor.ofRotorTranslator" ->
            motorCell (Motor.ofRotorTranslator (rotorOf (floats c "rotor")) (translatorOf (floats c "translator")))
        | "Motor.ofRotor" -> motorCell (Motor.ofRotor (rotorOf (floats c "rotor")))
        | "Motor.ofTranslator" -> motorCell (Motor.ofTranslator (translatorOf (floats c "translator")))
        | "Motor.ofScrew" ->
            resultCell
                motorCell
                (Motor.ofScrew (triple c "point") (triple c "direction") (number c "angle") (number c "distance"))
        | "Motor.ofLine" ->
            resultCell motorCell (Motor.ofLine (lineOf (floats c "line")) (number c "angle") (number c "distance"))
        | "Motor.exp" -> resultCell motorCell (Motor.exp (triple c "bivector") (triple c "vector"))
        | "Motor.log" -> resultCell bivectorAndVectorCell (Motor.log (motorOf (floats c "v")))
        | "Motor.interpolate" ->
            resultCell
                motorCell
                (Motor.interpolate (number c "t") (motorOf (floats c "first")) (motorOf (floats c "second")))
        | "Motor.ofMatrix" -> motorAndFactorCell (Motor.ofMatrix (number c "tau") (floats c "matrix"))
        | "Dilator.ofFactor" -> resultCell dilatorCell (Dilator.ofFactor (number c "factor"))
        | "Dilator.ofCentreFactor" ->
            resultCell dilatorCell (Dilator.ofCentreFactor (triple c "centre") (number c "factor"))
        | "Reflection.ofNormalDistance" ->
            resultCell reflectionCell (Reflection.ofNormalDistance (triple c "normal") (number c "distance"))
        | "Rotor.applyToPoint" -> pointCell (Rotor.applyToPoint (rotorOf (floats c "v")) (pointOf (floats c "x")))
        | "Rotor.applyToPointPair" -> pairCell (Rotor.applyToPointPair (rotorOf (floats c "v")) (pairOf (floats c "x")))
        | "Rotor.applyToLine" -> lineCell (Rotor.applyToLine (rotorOf (floats c "v")) (lineOf (floats c "x")))
        | "Rotor.applyToCircle" -> circleCell (Rotor.applyToCircle (rotorOf (floats c "v")) (circleOf (floats c "x")))
        | "Rotor.applyToPlane" -> planeCell (Rotor.applyToPlane (rotorOf (floats c "v")) (planeOf (floats c "x")))
        | "Rotor.applyToSphere" -> sphereCell (Rotor.applyToSphere (rotorOf (floats c "v")) (sphereOf (floats c "x")))
        | "Rotor.normalise" -> resultCell rotorCell (Rotor.normalise (rotorOf (floats c "v")))
        | "Rotor.toMultivector" -> multivectorCell (Rotor.toMultivector (rotorOf (floats c "v")))
        | "Rotor.ofMultivector" -> rotorCell (Rotor.ofMultivector (mv c "a"))
        | "Rotor.toMatrix" -> resultCell floatsCell (Rotor.toMatrix (rotorOf (floats c "v")))
        | "Rotor.compose" -> rotorCell (Rotor.compose (rotorOf (floats c "b")) (rotorOf (floats c "a")))
        | "Translator.applyToPoint" ->
            pointCell (Translator.applyToPoint (translatorOf (floats c "v")) (pointOf (floats c "x")))
        | "Translator.applyToPointPair" ->
            pairCell (Translator.applyToPointPair (translatorOf (floats c "v")) (pairOf (floats c "x")))
        | "Translator.applyToLine" ->
            lineCell (Translator.applyToLine (translatorOf (floats c "v")) (lineOf (floats c "x")))
        | "Translator.applyToCircle" ->
            circleCell (Translator.applyToCircle (translatorOf (floats c "v")) (circleOf (floats c "x")))
        | "Translator.applyToPlane" ->
            planeCell (Translator.applyToPlane (translatorOf (floats c "v")) (planeOf (floats c "x")))
        | "Translator.applyToSphere" ->
            sphereCell (Translator.applyToSphere (translatorOf (floats c "v")) (sphereOf (floats c "x")))
        | "Translator.normalise" -> resultCell translatorCell (Translator.normalise (translatorOf (floats c "v")))
        | "Translator.toMultivector" -> multivectorCell (Translator.toMultivector (translatorOf (floats c "v")))
        | "Translator.ofMultivector" -> translatorCell (Translator.ofMultivector (mv c "a"))
        | "Translator.toMatrix" -> resultCell floatsCell (Translator.toMatrix (translatorOf (floats c "v")))
        | "Translator.compose" ->
            translatorCell (Translator.compose (translatorOf (floats c "b")) (translatorOf (floats c "a")))
        | "Motor.applyToPoint" -> pointCell (Motor.applyToPoint (motorOf (floats c "v")) (pointOf (floats c "x")))
        | "Motor.applyToPointPair" -> pairCell (Motor.applyToPointPair (motorOf (floats c "v")) (pairOf (floats c "x")))
        | "Motor.applyToLine" -> lineCell (Motor.applyToLine (motorOf (floats c "v")) (lineOf (floats c "x")))
        | "Motor.applyToCircle" -> circleCell (Motor.applyToCircle (motorOf (floats c "v")) (circleOf (floats c "x")))
        | "Motor.applyToPlane" -> planeCell (Motor.applyToPlane (motorOf (floats c "v")) (planeOf (floats c "x")))
        | "Motor.applyToSphere" -> sphereCell (Motor.applyToSphere (motorOf (floats c "v")) (sphereOf (floats c "x")))
        | "Motor.normalise" -> resultCell motorCell (Motor.normalise (motorOf (floats c "v")))
        | "Motor.toMultivector" -> multivectorCell (Motor.toMultivector (motorOf (floats c "v")))
        | "Motor.ofMultivector" -> motorCell (Motor.ofMultivector (mv c "a"))
        | "Motor.toMatrix" -> resultCell floatsCell (Motor.toMatrix (motorOf (floats c "v")))
        | "Motor.compose" -> motorCell (Motor.compose (motorOf (floats c "b")) (motorOf (floats c "a")))
        | "Dilator.applyToPoint" -> pointCell (Dilator.applyToPoint (dilatorOf (floats c "v")) (pointOf (floats c "x")))
        | "Dilator.applyToPointPair" ->
            pairCell (Dilator.applyToPointPair (dilatorOf (floats c "v")) (pairOf (floats c "x")))
        | "Dilator.applyToLine" -> lineCell (Dilator.applyToLine (dilatorOf (floats c "v")) (lineOf (floats c "x")))
        | "Dilator.applyToCircle" ->
            circleCell (Dilator.applyToCircle (dilatorOf (floats c "v")) (circleOf (floats c "x")))
        | "Dilator.applyToPlane" -> planeCell (Dilator.applyToPlane (dilatorOf (floats c "v")) (planeOf (floats c "x")))
        | "Dilator.applyToSphere" ->
            sphereCell (Dilator.applyToSphere (dilatorOf (floats c "v")) (sphereOf (floats c "x")))
        | "Dilator.normalise" -> resultCell dilatorCell (Dilator.normalise (dilatorOf (floats c "v")))
        | "Dilator.toMultivector" -> multivectorCell (Dilator.toMultivector (dilatorOf (floats c "v")))
        | "Dilator.ofMultivector" -> dilatorCell (Dilator.ofMultivector (mv c "a"))
        | "Dilator.toMatrix" -> resultCell floatsCell (Dilator.toMatrix (dilatorOf (floats c "v")))
        | "Dilator.compose" -> dilatorCell (Dilator.compose (dilatorOf (floats c "b")) (dilatorOf (floats c "a")))
        | "Reflection.applyToPoint" ->
            pointCell (Reflection.applyToPoint (reflectionOf (floats c "v")) (pointOf (floats c "x")))
        | "Reflection.applyToPointPair" ->
            pairCell (Reflection.applyToPointPair (reflectionOf (floats c "v")) (pairOf (floats c "x")))
        | "Reflection.applyToLine" ->
            lineCell (Reflection.applyToLine (reflectionOf (floats c "v")) (lineOf (floats c "x")))
        | "Reflection.applyToCircle" ->
            circleCell (Reflection.applyToCircle (reflectionOf (floats c "v")) (circleOf (floats c "x")))
        | "Reflection.applyToPlane" ->
            planeCell (Reflection.applyToPlane (reflectionOf (floats c "v")) (planeOf (floats c "x")))
        | "Reflection.applyToSphere" ->
            sphereCell (Reflection.applyToSphere (reflectionOf (floats c "v")) (sphereOf (floats c "x")))
        | "Reflection.normalise" -> resultCell reflectionCell (Reflection.normalise (reflectionOf (floats c "v")))
        | "Reflection.toMultivector" -> multivectorCell (Reflection.toMultivector (reflectionOf (floats c "v")))
        | "Reflection.ofMultivector" -> reflectionCell (Reflection.ofMultivector (mv c "a"))
        | "Reflection.compose" ->
            motorCell (Reflection.compose (reflectionOf (floats c "b")) (reflectionOf (floats c "a")))
        | other -> failwithf "%s: no such operation as %s" c.Id other

    /// A case's answer, by calling the package's published function.
    let private answer (c: Case) : CorpusCell =
        match c.Op with
        | "ofScalar" -> multivectorCell (Multivector.ofScalar (number c "s"))
        | "basis" -> multivectorCell (Multivector.basis (whole c "index"))
        | "coefficient" -> floatCell (Multivector.coefficient (whole c "index") (mv c "a"))
        | "grade" -> multivectorCell (Multivector.grade (whole c "grade") (mv c "a"))
        | "add" -> multivectorCell (Multivector.add (mv c "a") (mv c "b"))
        | "subtract" -> multivectorCell (Multivector.subtract (mv c "a") (mv c "b"))
        | "scale" -> multivectorCell (Multivector.scale (number c "s") (mv c "a"))
        | "negate" -> multivectorCell (Multivector.negate (mv c "a"))
        | "reverse" -> multivectorCell (Multivector.reverse (mv c "a"))
        | "gradeInvolution" -> multivectorCell (Multivector.gradeInvolution (mv c "a"))
        | "dual" -> multivectorCell (Multivector.dual (mv c "a"))
        | "undual" -> multivectorCell (Multivector.undual (mv c "a"))
        | "geometricProduct" -> multivectorCell (Multivector.geometricProduct (mv c "a") (mv c "b"))
        | "outerProduct" -> multivectorCell (Multivector.outerProduct (mv c "a") (mv c "b"))
        | "leftContraction" -> multivectorCell (Multivector.leftContraction (mv c "a") (mv c "b"))
        | "scalarProduct" -> floatCell (Multivector.scalarProduct (mv c "a") (mv c "b"))
        | "squaredNorm" -> floatCell (Multivector.squaredNorm (mv c "a"))
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | op when isVersorOp op -> versorAnswer c
        | _ -> conformalAnswer c

    let private operandCell (operand: Operand) : CorpusCell =
        match operand with
        | Coefficients xs -> CorpusCell.List [ for x in xs -> floatCell x ]
        | Blade index -> CorpusCell.Row [ "basis", CorpusFile.num index ]
        | PowersOfTwo -> CorpusFile.str "powersOfTwo"
        | Number x -> floatCell x
        | Whole n -> CorpusFile.num n

    let private caseCell (c: Case) : CorpusCell =
        CorpusCell.Row
            [ "id", CorpusFile.str c.Id
              "op", CorpusFile.str c.Op
              "input", CorpusCell.Row [ for name, operand in c.Input -> name, operandCell operand ]
              "expected", answer c ]

    let private description =
        "The reference answers for Fuaran.Compute.GeometricAlgebra over AUTHORED inputs (Phase 727, "
        + "DECISIONS.md D85 and D86): the multivector of the conformal algebra Cl(4,1) and its products. A "
        + "float is its IEEE-754 binary64 bit pattern as sixteen lowercase hex digits, or `nan`. A "
        + "multivector is the list of its thirty-two coefficients in the order of `blades`; an operand "
        + "written `{ basis: i }` is the basis blade of index `i`, `1` there and `+0` elsewhere, and "
        + "`powersOfTwo` is the multivector whose coefficient at index `j` is `2^j`. `op` names the "
        + "published function of the module `Multivector` and `input` its arguments; `expected` is a "
        + "multivector, or one float for `coefficient`, `scalarProduct` and `squaredNorm`. Every cell is "
        + "compared byte for byte, the sign of a zero included. A vector whose id begins `table-` multiplies "
        + "one basis blade by `powersOfTwo`: each coefficient it answers is the signed sum of the `2^j` "
        + "whose term reaches it, so the thirty-two vectors of a product spell out every term of its table "
        + "and every sign. The products are one fold: each output starts at `+0`, the terms are visited "
        + "ascending in the left index, then the right index, then the output, and each forms the rounded "
        + "product of its two coefficients and then one rounded sum or difference, never a fused "
        + "multiply-add, and never skipped because a coefficient is zero. `geometric-absorbed-then-cancelled` "
        + "answers another scalar in another order of summation; `geometric-fma-witness` answers `-2^-60` "
        + "where a multiply-add is fused; `geometric-every-scalar-term-a-negative-zero` answers `-0` where a "
        + "fold is seeded from its first term; the `zero-against-infinity` vectors answer no NaN where a "
        + "term with a zero coefficient is skipped. A negation flips the sign bit of a zero, a copy keeps "
        + "it, and a grade projection writes `+0` outside its grade. Since Phase 728 (DECISIONS.md D87) an "
        + "`op` written `Module.function` names a published function of the module `Blade`, `Point`, "
        + "`PointPair`, `Line`, `Circle`, `Plane` or `Sphere`. A typed object is the list of the "
        + "coefficients its type carries, in index order: a point the blades of indices 1 to 5, a point "
        + "pair 6 to 15, a circle 16 to 25, a line 18, 20, 21, 23, 24 and 25, a sphere 26 to 30 and a "
        + "plane 27 to 30; a Euclidean point, a direction or a normal is a list of three floats. `tau` is "
        + "the threshold a zero test is given. An answer that can be refused is its own cell or `{ "
        + "refused: <the case> }`, and one that has kinds is `{ kind: <the case> }` with what the case "
        + "carries. Nothing is normalised unasked: an object built through points or by a meet has the "
        + "weight its product gives. A quotient is one division, never a product with a reciprocal; a "
        + "norm is `m × sqrt(sum((x_i / m)^2))` with `m` the largest magnitude; and at `tau = 0` the kind "
        + "of a quotient is decided by the signs of its numerator and denominator, the quotient unformed. "
        + "Since Phase 729 (DECISIONS.md D88) an `op` also names a published function of the module "
        + "`Rotor`, `Translator`, `Motor`, `Dilator` or `Reflection`. A versor is the list of the "
        + "coefficients its type carries, in index order: a rotor the blades of indices 0, 10, 11 and 13, "
        + "a translator 0, 12, 14 and 15, a motor 0, 10 to 15 and 30, a dilator 0, 9, 12, 14 and 15, and a "
        + "reflection 2 to 5. A bivector is the list of its coefficients on `e1^e2`, `e1^e3` and `e2^e3`; "
        + "a motor's logarithm answers `{ bivector, vector }`. A matrix is its sixteen entries in "
        + "column-major order, and one read back answers `{ motor, factor }`, or `{ refused }` with the "
        + "`measure` that failed where the case carries one. A versor is applied by `(V X) rev(V)`, the "
        + "left product first, with its reverse and never its inverse, and nothing is normalised: a "
        + "versor that is no unit scales what it is applied to. `compose` answers the product `b a`. The "
        + "sine and the cosine of an angle and the arc tangent of a logarithm are the pinned "
        + "`Elementary.sinCos` and `Elementary.atan2`, each asked once; a vector whose id says `a unit from "
        + "correctly rounded` asks one at an argument where its answer is not the correctly rounded double."

    /// The blades, in index order: the position of a coefficient in every list of this document.
    let blades: string list =
        [ "1"
          "n0"
          "e1"
          "e2"
          "e3"
          "ninf"
          "n0^e1"
          "n0^e2"
          "n0^e3"
          "n0^ninf"
          "e1^e2"
          "e1^e3"
          "e1^ninf"
          "e2^e3"
          "e2^ninf"
          "e3^ninf"
          "n0^e1^e2"
          "n0^e1^e3"
          "n0^e1^ninf"
          "n0^e2^e3"
          "n0^e2^ninf"
          "n0^e3^ninf"
          "e1^e2^e3"
          "e1^e2^ninf"
          "e1^e3^ninf"
          "e2^e3^ninf"
          "n0^e1^e2^e3"
          "n0^e1^e2^ninf"
          "n0^e1^e3^ninf"
          "n0^e2^e3^ninf"
          "e1^e2^e3^ninf"
          "n0^e1^e2^e3^ninf" ]

    /// The family's document as the one corpus writer renders it.
    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "geometricAlgebra"
                  "description", CorpusFile.str description
                  "blades", CorpusCell.List [ for blade in blades -> CorpusFile.str blade ] ]
              Items = "vectors"
              Rows = cases |> List.map caseCell }
