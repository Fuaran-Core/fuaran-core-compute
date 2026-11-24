namespace Fuaran.Compute.GeometricAlgebra

open System

// ============================================================================
//  Phase 727 — the multivector of `Cl(4,1)` and the products every later
//  operation is written in. The rules are DECISIONS.md D85, and each member
//  below cites the item it keeps: thirty-two coefficients in item 2's blade
//  order; every product one fold from `+0.0` over item 3's terms, ascending,
//  a rounded product and then a rounded sum, no term skipped on a
//  coefficient's value; the involutions and the dual by copies and exact
//  negation (item 4); a NaN leaving every public function as `Double.NaN`'s
//  bits (item 3); and the float identity as `Equals` (item 8).
//
//  The general product is the reference a specialised one is held to, so it is
//  written to be read: one loop over one table. Phase 730 adds the specialised
//  products (DECISIONS.md D89): the same fold over the terms of that table whose
//  blades two operand types carry, in the order they have there, taken only
//  where both operands are finite and hold a zero wherever their type carries
//  no blade, so that each answers the general product's bits on every input.
//
//  FSharp.Core only; Fable-clean. No host mathematics function is called.
// ============================================================================

/// A multivector of the conformal algebra `Cl(4,1)`: thirty-two coefficients over the outer products
/// of `n0`, `e1`, `e2`, `e3` and `n∞`, by grade and within a grade lexicographically, every blade the
/// outer product of its vectors in ascending order (DECISIONS.md D85 item 2). Opaque and immutable:
/// nothing public writes, and its storage is never handed out. Its storage holds no NaN but
/// `Double.NaN`'s bits.
///
/// Equality is the float identity (D85 item 8): two multivectors are equal when at every index the two
/// coefficients are both NaN or are equal as numbers, so every NaN is one value and `-0.0` equals
/// `0.0`. A comparison of bits is a law's, over `Multivector.toArray`, never `=`.
[<Sealed>]
type Multivector internal (coefficients: float[], carried: int) =

    /// The thirty-two coefficients, in index order; never written after construction.
    member internal _.Coefficients: float[] = coefficients

    /// The blades the value's type carries, one bit for each index, and every bit for a value of no
    /// type (DECISIONS.md D89). A specialised product reads it to choose its terms, and then reads the
    /// coefficients before it trusts it. Neither equality nor the hash reads it.
    member internal _.Carried: int = carried

    override _.Equals(other: obj) : bool =
        match other with
        | :? Multivector as that ->
            let theirs = that.Coefficients
            let mutable same = true
            let mutable index = 0

            while same && index < coefficients.Length do
                let x = coefficients[index]
                let y = theirs[index]
                same <- x = y || (Double.IsNaN x && Double.IsNaN y)
                index <- index + 1

            same
        | _ -> false

    /// Equal multivectors hash alike: a NaN hashes as one value and the two zeros as one. The number
    /// is not pinned, and may differ between hosts.
    override _.GetHashCode() : int =
        let mutable h = 32

        for x in coefficients do
            let k =
                if Double.IsNaN x then 0x7FC00000
                elif x = 0.0 then 0
                else hash x

            h <- h * 31 + k

        h

/// The kernels the public functions are written in. Each takes, before its operands, the rule it
/// keeps: where a fold starts, how a term enters it, which terms it visits; how a coefficient is
/// negated. A public function passes the recorded rule. The suite runs the same kernel with one of
/// them changed and shows a law vector fail, so a witness is this code and never a copy of it. That
/// is the only reason they are arguments. The kernels are `inline`, so a product pays nothing for it;
/// an inline member that reads internal ones cannot be used from another assembly, so the suite's
/// doors (`productUnder`, `scalarUnder`, `permutedUnder`) are the same kernels compiled here with the
/// rule left open.
module internal Kernel =

    /// THE NaN a float leaves this package with (D85 item 3, D16): `Double.NaN`'s bit pattern.
    let canonical (x: float) : float =
        if Double.IsNaN x then Double.NaN else x

    /// The mask of a value of no type: every blade (DECISIONS.md D89).
    let every: int = -1

    /// Whether a mask holds the blade of this index.
    let carries (mask: int) (index: int) : bool = (mask >>> index) &&& 1 = 1

    /// The mask of a type that carries the blades of these indices.
    let maskOf (indices: int[]) : int =
        indices |> Array.fold (fun mask index -> mask ||| (1 <<< index)) 0

    /// Canonicalise a fresh array in place, on the way out and never per step, and adopt it as a value
    /// whose type carries the blades of `carried`.
    let adoptAs (carried: int) (coefficients: float[]) : Multivector =
        for index in 0 .. coefficients.Length - 1 do
            coefficients[index] <- canonical coefficients[index]

        Multivector(coefficients, carried)

    /// `adoptAs` for a value of no type.
    let adopt (coefficients: float[]) : Multivector = adoptAs every coefficients

    /// How a term enters its accumulator (D85 item 3): `p = x × y`, one rounded product with the left
    /// coefficient first, and then `r + p` or `r − p`, one rounded sum. Two roundings and never one:
    /// no fused multiply-add on any host.
    let twoRoundings (negative: bool) (r: float) (x: float) (y: float) : float =
        let p = x * y
        if negative then r - p else r + p

    /// Item 3's fold: every output starts at `start`, and the terms are visited in the order given,
    /// each entering its output's accumulator as it is met. No term is skipped on the value of a
    /// coefficient: a term is absent only when the table has none.
    let inline foldWith
        (start: float)
        ([<InlineIfLambda>] enter: bool -> float -> float -> float -> float)
        (terms: Term[])
        (a: float[])
        (b: float[])
        : float[] =
        let r = Array.create Basis.count start

        for term in terms do
            r[term.Out] <- enter term.Negative r[term.Out] a[term.Left] b[term.Right]

        r

    /// A product of two multivectors by `foldWith`, its NaNs canonical on the way out.
    let inline productWith
        (start: float)
        ([<InlineIfLambda>] enter: bool -> float -> float -> float -> float)
        (terms: Term[])
        (a: Multivector)
        (b: Multivector)
        : Multivector =
        adopt (foldWith start enter terms a.Coefficients b.Coefficients)

    /// The scalar coefficient of a product by `foldWith`, a NaN canonical on the way out.
    let inline scalarWith
        (start: float)
        ([<InlineIfLambda>] enter: bool -> float -> float -> float -> float)
        (terms: Term[])
        (a: float[])
        (b: float[])
        : float =
        let r = foldWith start enter terms a b
        canonical r[0]

    /// Exact negation: the sign bit flipped, a zero's included.
    let exactNegation (x: float) : float = -x

    /// A signed permutation of the coefficients: the coefficient at `index` goes to `target[index]`,
    /// negated when `negative[index]`. Copies and negation, no arithmetic.
    let inline signedPermutationWith
        ([<InlineIfLambda>] negate: float -> float)
        (target: int[])
        (negative: bool[])
        (a: float[])
        : float[] =
        let r = Array.zeroCreate<float> Basis.count

        for index in 0 .. Basis.count - 1 do
            r[target[index]] <- if negative[index] then negate a[index] else a[index]

        r

    /// The type of a signed permutation's answer: the reverse, the grade involution and negation, which
    /// keep every blade where it is, keep the value's type; the dual and the undual, which move blades,
    /// answer a value of no type, since no specialised product reads one (D89).
    let permutedMask (target: int[]) (carried: int) : int =
        if obj.ReferenceEquals(target, Basis.identity) then
            carried
        else
            every

    /// A multivector's signed permutation, its NaNs canonical on the way out: negating a NaN moves
    /// its sign bit. The reverse of a typed value is a value of the same type.
    let inline permutedWith
        ([<InlineIfLambda>] negate: float -> float)
        (target: int[])
        (negative: bool[])
        (a: Multivector)
        : Multivector =
        adoptAs (permutedMask target a.Carried) (signedPermutationWith negate target negative a.Coefficients)

    /// Every blade negated: negation's mask.
    let everyBlade: bool[] = Array.create Basis.count true

    // ---- the specialised products (Phase 730, DECISIONS.md D89) --------------------------------------

    /// The general geometric product, item 3's fold over all 1,024 terms, as one compiled function: the
    /// published `Multivector.geometricProduct` is this function, and so is the path a specialised
    /// product takes where it does not specialise. Two inlined copies of the one loop were measured to
    /// run at different speeds in one process on .NET (D89), so there is one.
    let general (a: Multivector) (b: Multivector) : Multivector =
        productWith 0.0 twoRoundings Basis.geometric a b

    /// A specialised product: the masks of its two operand types, the terms of the geometric product's
    /// table whose left blade the left type carries and whose right blade the right type carries, in
    /// the order they have in that table, and the mask of the blades those terms reach, which is the
    /// type of its answer.
    type Specialisation =
        { Left: int
          Right: int
          Terms: Term[]
          Reached: int }

    /// The specialisation of the geometric product to two types. Its terms are the general table
    /// filtered, so they are visited in the general order by construction. A term it drops has a factor
    /// on a blade its operand's type does not carry.
    let specialise (left: int) (right: int) : Specialisation =
        let terms =
            Basis.geometric
            |> Array.filter (fun term -> carries left term.Left && carries right term.Right)

        { Left = left
          Right = right
          Terms = terms
          Reached = terms |> Array.fold (fun mask term -> mask ||| (1 <<< term.Out)) 0 }

    /// Whether a value's coefficients are what a specialised product may drop terms of: finite on
    /// every blade its type carries, and a zero of either sign on every other. Where this fails the
    /// general product answers (D89: a dropped term is a zero only between finite factors).
    let admits (carried: int) (xs: float[]) : bool =
        let mutable held = true
        let mutable index = 0

        while held && index < Basis.count do
            let x = xs[index]

            held <-
                if carries carried index then
                    not (Double.IsNaN x || Double.IsInfinity x)
                else
                    x = 0.0

            index <- index + 1

        held

    /// The geometric product, specialised where the two operands' types are a pair in `shapes` and
    /// both operands are admitted, and the general product otherwise. The specialised fold is item 3's
    /// under the rule given (its start, its entry, the order of its terms, and whether it reads the
    /// guard); the general product it falls back to is always the recorded one.
    let inline specialisedWith
        (start: float)
        ([<InlineIfLambda>] enter: bool -> float -> float -> float -> float)
        ([<InlineIfLambda>] order: Term[] -> Term[])
        (guarded: bool)
        (shapes: Specialisation[])
        (a: Multivector)
        (b: Multivector)
        : Multivector =
        let mutable found = -1
        let mutable k = 0

        while found < 0 && k < shapes.Length do
            if shapes[k].Left = a.Carried && shapes[k].Right = b.Carried then
                found <- k

            k <- k + 1

        if
            found >= 0
            && (not guarded
                || (admits a.Carried a.Coefficients && admits b.Carried b.Coefficients))
        then
            let shape = shapes[found]
            adoptAs shape.Reached (foldWith start enter (order shape.Terms) a.Coefficients b.Coefficients)
        else
            general a b

    /// `specialisedWith` under a rule the caller passes: the suite's door to the specialised fold.
    let specialisedUnder
        (start: float)
        (enter: bool -> float -> float -> float -> float)
        (order: Term[] -> Term[])
        (guarded: bool)
        (shapes: Specialisation[])
        (a: Multivector)
        (b: Multivector)
        : Multivector =
        specialisedWith start enter order guarded shapes a b

    /// `productWith` under a rule the caller passes: the suite's door to the fold.
    let productUnder
        (start: float)
        (enter: bool -> float -> float -> float -> float)
        (terms: Term[])
        (a: Multivector)
        (b: Multivector)
        : Multivector =
        productWith start enter terms a b

    /// `scalarWith` under a rule the caller passes.
    let scalarUnder
        (start: float)
        (enter: bool -> float -> float -> float -> float)
        (terms: Term[])
        (a: float[])
        (b: float[])
        : float =
        scalarWith start enter terms a b

    /// `permutedWith` under a negation the caller passes.
    let permutedUnder (negate: float -> float) (target: int[]) (negative: bool[]) (a: Multivector) : Multivector =
        permutedWith negate target negative a

/// The multivector's functions: construction and read-back, the sum, the involutions, the dual and
/// the products (DECISIONS.md D85 items 3 and 4). There are no operators: a product is a named
/// function (item 8). Nothing normalises (item 5), and no function takes a tolerance.
[<RequireQualifiedAccess>]
module Multivector =

    /// A blade index is an argument, not data: a read outside `0 .. 31` raises, as an array's would.
    let private checkIndex (index: int) : unit =
        if index < 0 || index >= Basis.count then
            raise (IndexOutOfRangeException())

    // ---- construction and read-back ----------------------------------------------------------------

    /// The zero multivector: `+0.0` at every index.
    let zero: Multivector = Kernel.adopt (Array.zeroCreate<float> Basis.count)

    /// The multivector whose coefficient at index `i` is `f i`, called once for each index, ascending.
    let init (f: int -> float) : Multivector = Kernel.adopt (Array.init Basis.count f)

    /// The scalar `s`: `s` at index 0 and `+0.0` elsewhere.
    let ofScalar (s: float) : Multivector =
        init (fun index -> if index = 0 then s else 0.0)

    /// The basis blade of this index: `1.0` there and `+0.0` elsewhere. Index 0 is the scalar `1`, 1
    /// to 5 are `n0`, `e1`, `e2`, `e3` and `n∞`, and 31 is the pseudoscalar. Raises
    /// `IndexOutOfRangeException` outside `0 .. 31`.
    let basis (index: int) : Multivector =
        checkIndex index
        init (fun i -> if i = index then 1.0 else 0.0)

    /// The coefficient of the blade of this index. Raises `IndexOutOfRangeException` outside `0 .. 31`.
    let coefficient (index: int) (a: Multivector) : float =
        checkIndex index
        a.Coefficients[index]

    /// The thirty-two coefficients in index order, as a fresh array.
    let toArray (a: Multivector) : float[] = Array.copy a.Coefficients

    /// The grade projection `⟨A⟩_g`: a copy of the coefficients of grade `g`, and `+0.0` elsewhere. A
    /// grade the algebra does not have (below zero, above five) holds no blade, so its projection is
    /// the zero multivector.
    let grade (g: int) (a: Multivector) : Multivector =
        let xs = a.Coefficients
        Kernel.adopt (Array.init Basis.count (fun index -> if Basis.grade[index] = g then xs[index] else 0.0))

    // ---- one rounded operation for each coefficient -----------------------------------------------

    /// `A + B`: one rounded sum for each coefficient.
    let add (a: Multivector) (b: Multivector) : Multivector =
        let xs = a.Coefficients
        let ys = b.Coefficients
        Kernel.adopt (Array.init Basis.count (fun index -> xs[index] + ys[index]))

    /// `A − B`: one rounded difference for each coefficient.
    let subtract (a: Multivector) (b: Multivector) : Multivector =
        let xs = a.Coefficients
        let ys = b.Coefficients
        Kernel.adopt (Array.init Basis.count (fun index -> xs[index] - ys[index]))

    /// `s A`: one rounded product for each coefficient, the scalar first.
    let scale (s: float) (a: Multivector) : Multivector =
        let xs = a.Coefficients
        Kernel.adopt (Array.init Basis.count (fun index -> s * xs[index]))

    // ---- copies and exact negation -----------------------------------------------------------------

    /// `−A`: every coefficient negated exactly, a zero's sign included.
    let negate (a: Multivector) : Multivector =
        Kernel.permutedWith Kernel.exactNegation Basis.identity Kernel.everyBlade a

    /// The reverse `Ã`: the blades of grade 2 and 3 negated exactly (D85 item 2's `rev`).
    let reverse (a: Multivector) : Multivector =
        Kernel.permutedWith Kernel.exactNegation Basis.identity Basis.reverseNegative a

    /// The grade involution `Â`: the blades of odd grade negated exactly (D85 item 2's `inv`).
    let gradeInvolution (a: Multivector) : Multivector =
        Kernel.permutedWith Kernel.exactNegation Basis.identity Basis.involutionNegative a

    /// The dual `A* = A ⌋ I⁻¹`, as the signed permutation of D85 item 2's last column: copies and
    /// exact negation, no arithmetic.
    let dual (a: Multivector) : Multivector =
        Kernel.permutedWith Kernel.exactNegation Basis.dualTarget Basis.dualNegative a

    /// The undual `A ⌋ I`, the inverse permutation: `undual (dual A)` is `A` bit for bit on every
    /// input free of a NaN.
    let undual (a: Multivector) : Multivector =
        Kernel.permutedWith Kernel.exactNegation Basis.undualTarget Basis.undualNegative a

    // ---- the products: item 3's fold over item 3's terms ------------------------------------------

    /// The geometric product `A B`: every output from `+0.0`, the 1,024 terms ascending in the left
    /// index, the right index and the output. No coefficient it returns is `-0.0`.
    let geometricProduct (a: Multivector) (b: Multivector) : Multivector = Kernel.general a b

    /// The outer product `A ∧ B`: the terms of the geometric product that raise the grade by the left
    /// operand's, in the order they have there.
    let outerProduct (a: Multivector) (b: Multivector) : Multivector =
        Kernel.productWith 0.0 Kernel.twoRoundings Basis.outer a b

    /// The left contraction `A ⌋ B`, the one inner product this package publishes: the terms that
    /// lower the right operand's grade by the left's, in the order they have in the geometric product.
    let leftContraction (a: Multivector) (b: Multivector) : Multivector =
        Kernel.productWith 0.0 Kernel.twoRoundings Basis.contraction a b

    /// The scalar product `A ∗ B`: the scalar coefficient of the geometric product, bit for bit.
    let scalarProduct (a: Multivector) (b: Multivector) : float =
        Kernel.scalarWith 0.0 Kernel.twoRoundings Basis.scalar a.Coefficients b.Coefficients

    /// The squared norm `A ∗ Ã`: a signed number and not a length, negative on a blade of negative
    /// signature.
    let squaredNorm (a: Multivector) : float =
        let xs = a.Coefficients

        Kernel.scalarWith
            0.0
            Kernel.twoRoundings
            Basis.scalar
            xs
            (Kernel.signedPermutationWith Kernel.exactNegation Basis.identity Basis.reverseNegative xs)
