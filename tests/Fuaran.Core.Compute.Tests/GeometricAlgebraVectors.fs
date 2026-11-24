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

    /// Every case, in document order: Phase 727's, and then the ones appended since.
    let cases: Case list = tableRows @ hardInputs @ generalInputs @ carried

    /// The case with this id.
    let find (id: string) : Case = cases |> List.find (fun c -> c.Id = id)

    // ---- the answers and the document --------------------------------------------------------------

    /// The operand of this name.
    let operand (c: Case) (name: string) : Operand =
        c.Input |> List.find (fun (n, _) -> n = name) |> snd

    let private mv (c: Case) (name: string) : Multivector = multivector (operand c name)

    let private number (c: Case) (name: string) : float =
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

    let private multivectorCell (a: Multivector) : CorpusCell =
        CorpusCell.List [ for digits in hexes a -> CorpusFile.str digits ]

    let private floatCell (x: float) : CorpusCell =
        CorpusFile.str (ElementaryVectors.hex x)

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
        | other -> failwithf "%s: no such operation as %s" c.Id other

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
        + "it, and a grade projection writes `+0` outside its grade."

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
