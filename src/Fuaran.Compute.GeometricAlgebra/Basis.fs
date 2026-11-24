namespace Fuaran.Compute.GeometricAlgebra

// ============================================================================
//  Phase 727 — the basis of the conformal algebra `Cl(4,1)` and the one table
//  every product folds over (DECISIONS.md D85 items 2 and 3).
//
//  Nothing here is a transcribed table. The blades are enumerated by item 2's
//  rule (grade, then lexicographic, over `n0 e1 e2 e3 n∞`), the 1,024 signed
//  terms are built by item 3's rule (a Euclidean factor and a null factor for
//  each blade, the four-by-four table of the null factors, and the three signs
//  `λ`, `κ` and the sort's parity), and the dual's signed permutation is read
//  off the terms that multiply by the pseudoscalar. The suite renders the
//  three term tables as item 3 says and holds their SHA-256 to the digests the
//  record pins, and holds every derived column to item 2's table as written.
//
//  FSharp.Core only; Fable-clean. Everything is built once, when the module is
//  first read, with integer arithmetic alone.
// ============================================================================

/// One term of a product's table (DECISIONS.md D85 item 3): the blade of index `Left` times the blade
/// of index `Right` contributes the blade of index `Out`, negated when `Negative`.
type internal Term =
    { Left: int
      Right: int
      Out: int
      Negative: bool }

/// The basis blades in their recorded order, the term tables and the signed permutations.
module internal Basis =

    /// The basis vectors, by position: `n0` at 0, `e1`, `e2` and `e3` at 1 to 3, `n∞` at 4.
    let private origin = 0

    let private infinity = 4

    /// The number of basis blades, and so of a multivector's coefficients.
    let count = 32

    /// The basis vectors of each blade, ascending, the blades in index order: by grade, and within a
    /// grade lexicographically (item 2). Every blade is the outer product of its vectors in this order.
    let blades: int list[] =
        let rec ascending (from: int) (size: int) : int list list =
            if size = 0 then
                [ [] ]
            else
                [ for v in from..infinity do
                      for rest in ascending (v + 1) (size - 1) -> v :: rest ]

        [| for grade in 0 .. infinity + 1 do
               yield! ascending origin grade |]

    /// The grade of each blade.
    let grade: int[] = blades |> Array.map List.length

    /// The index of the blade with these basis vectors, ascending.
    let private indexOf (vectors: int list) : int =
        blades |> Array.findIndex (fun blade -> blade = vectors)

    /// Whether the reverse negates each blade: the sign `(−1)^(g(g−1)/2)` (item 2).
    let reverseNegative: bool[] =
        grade |> Array.map (fun g -> (g * (g - 1) / 2) % 2 = 1)

    /// Whether the grade involution negates each blade: the sign `(−1)^g` (item 2).
    let involutionNegative: bool[] = grade |> Array.map (fun g -> g % 2 = 1)

    /// A blade's null factor in item 3's factoring `U N`: `1`, `n0`, `n∞` or `E = n0 ∧ n∞`.
    type private Null =
        | One
        | Origin
        | Infinity
        | Pair

    /// A blade as item 3 factors it: its Euclidean vectors, ascending, and its null factor.
    let private factor (index: int) : int list * Null =
        let blade = blades[index]
        let euclidean = blade |> List.filter (fun v -> v <> origin && v <> infinity)

        let nullFactor =
            match List.contains origin blade, List.contains infinity blade with
            | false, false -> One
            | true, false -> Origin
            | false, true -> Infinity
            | true, true -> Pair

        euclidean, nullFactor

    /// `λ`: the stored blade `n0 ∧ U ∧ n∞` is the geometric product `λ U N`, and `λ = −1` when `N` is
    /// `n0` or `E` and `U` has odd grade. `true` is `−1`.
    let private lambdaNegative (euclidean: int list) (nullFactor: Null) : bool =
        (nullFactor = Origin || nullFactor = Pair) && List.length euclidean % 2 = 1

    /// The product `N₁ N₂` of two null factors, as item 3's table gives it: no term, one, or two, each
    /// a null factor and whether it is negated.
    let private nullProduct (left: Null) (right: Null) : (Null * bool) list =
        match left, right with
        | One, n -> [ n, false ]
        | n, One -> [ n, false ]
        | Origin, Origin -> []
        | Origin, Infinity -> [ One, true; Pair, false ]
        | Origin, Pair -> [ Origin, false ]
        | Infinity, Origin -> [ One, true; Pair, true ]
        | Infinity, Infinity -> []
        | Infinity, Pair -> [ Infinity, true ]
        | Pair, Origin -> [ Origin, true ]
        | Pair, Infinity -> [ Infinity, false ]
        | Pair, Pair -> [ One, false ]

    /// The product `U V` of two Euclidean factors: the vectors in exactly one of the two, and whether
    /// sorting them takes an odd number of transpositions (each `e_i² = +1`).
    let private euclideanProduct (left: int list) (right: int list) : int list * bool =
        let transpositions =
            List.sum [ for u in left -> right |> List.filter (fun v -> v < u) |> List.length ]

        let inOne =
            (left |> List.filter (fun u -> not (List.contains u right)))
            @ (right |> List.filter (fun v -> not (List.contains v left)))
            |> List.sort

        inOne, transpositions % 2 = 1

    /// The stored blade a Euclidean factor and a null factor make.
    let private stored (euclidean: int list) (nullFactor: Null) : int =
        let withOrigin = nullFactor = Origin || nullFactor = Pair
        let withInfinity = nullFactor = Infinity || nullFactor = Pair

        indexOf (
            (if withOrigin then [ origin ] else [])
            @ euclidean
            @ (if withInfinity then [ infinity ] else [])
        )

    /// The terms of the geometric product, ascending in `(Left, Right, Out)`: item 3's table. For the
    /// blades `λ₁ U N₁` and `λ₂ V N₂` the product is `λ₁ λ₂ κ (U V)(N₁ N₂)`, with `κ = −1` when `N₁`
    /// is `n0` or `n∞` and `V` has odd grade, and each `W N` that results is put back as a stored
    /// blade by its own `λ`. A term's sign is the product of those signs: negative when an odd number
    /// of them are.
    let geometric: Term[] =
        [| for left in 0 .. count - 1 do
               let u, n1 = factor left

               for right in 0 .. count - 1 do
                   let v, n2 = factor right
                   let w, sortNegative = euclideanProduct u v
                   let kappaNegative = (n1 = Origin || n1 = Infinity) && List.length v % 2 = 1

                   yield!
                       nullProduct n1 n2
                       |> List.map (fun (n, entryNegative) ->
                           let negatives =
                               [ lambdaNegative u n1
                                 lambdaNegative v n2
                                 kappaNegative
                                 sortNegative
                                 entryNegative
                                 lambdaNegative w n ]
                               |> List.filter id
                               |> List.length

                           { Left = left
                             Right = right
                             Out = stored w n
                             Negative = negatives % 2 = 1 })
                       |> List.sortBy (fun term -> term.Out) |]

    /// The outer product's terms: those of the geometric product with `grade Out = grade Left + grade
    /// Right`, in the order they have there.
    let outer: Term[] =
        geometric
        |> Array.filter (fun term -> grade[term.Out] = grade[term.Left] + grade[term.Right])

    /// The left contraction's terms: those with `grade Out = grade Right − grade Left`, in the order
    /// they have in the geometric product.
    let contraction: Term[] =
        geometric
        |> Array.filter (fun term -> grade[term.Out] = grade[term.Right] - grade[term.Left])

    /// The scalar product's terms: those whose output is the scalar, in the order they have in the
    /// geometric product.
    let scalar: Term[] = geometric |> Array.filter (fun term -> term.Out = 0)

    /// The index of the pseudoscalar `I = n0 ∧ e1 ∧ e2 ∧ e3 ∧ n∞`.
    let pseudoscalar = count - 1

    /// The one term each blade has against the pseudoscalar, on its right.
    let private timesPseudoscalar: Term[] =
        Array.init count (fun index ->
            geometric
            |> Array.find (fun term -> term.Left = index && term.Right = pseudoscalar))

    /// Every blade to itself: the permutation of the reverse, the grade involution and negation.
    let identity: int[] = Array.init count id

    /// Where the dual `A ⌋ I⁻¹` sends each blade (item 2's last column). `I` is the pseudoscalar, so
    /// the contraction is the geometric product `A I⁻¹`, and `I⁻¹ = −I`: a blade goes where its term
    /// against `I` sends it, with the opposite sign.
    let dualTarget: int[] = timesPseudoscalar |> Array.map (fun term -> term.Out)

    /// Whether the dual negates each blade.
    let dualNegative: bool[] =
        timesPseudoscalar |> Array.map (fun term -> not term.Negative)

    /// Where the undual sends each blade: the inverse permutation.
    let undualTarget: int[] =
        Array.init count (fun index -> Array.findIndex (fun target -> target = index) dualTarget)

    /// Whether the undual negates each blade: the sign the dual gave the blade it came from.
    let undualNegative: bool[] =
        Array.init count (fun index -> dualNegative[undualTarget[index]])
