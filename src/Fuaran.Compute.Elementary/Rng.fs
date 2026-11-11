namespace Fuaran.Compute.Elementary

/// The pinned pseudo-random generator (Phase 621, DECISIONS.md D76): SplitMix64 (Steele, Lea and Flood
/// 2014), whose every draw is the same sixty-four bits on every conformant host. A seeded step of a
/// pipeline — a clustering, a resample — draws from this and from nothing else, so a
/// `(pipeline, seed, data)` triple replays bit for bit.
///
/// The state and every intermediate are held as four 16-bit limbs in `int`s. A product of two limbs is
/// below `2³²` and a column of them below `2³⁵`, formed in float arithmetic where every such integer is
/// exact; the shifts and exclusive-ors act on limbs. No 64-bit integer is formed in a draw, so a host
/// whose 64-bit integers are arbitrary-precision objects (JavaScript's) pays for none, and a host whose
/// 32-bit product is rounded (the collapse Fuaran.Core's Phase 495 measured) is never asked for one.
/// `next` alone assembles a `uint64`, for a caller that wants the draw whole.
[<RequireQualifiedAccess>]
module Rng =

    /// One stream of draws: the generator's state, advanced in place by every draw. A stream is not
    /// shared between threads; two streams made by the same `ofSeed` arguments draw the same sequence.
    [<Sealed>]
    type Stream internal (s0: int, s1: int, s2: int, s3: int) =
        // The state, least significant limb first.
        member val internal S0 = s0 with get, set
        member val internal S1 = s1 with get, set
        member val internal S2 = s2 with get, set
        member val internal S3 = s3 with get, set
        // The latest draw, least significant limb first.
        member val internal O0 = 0 with get, set
        member val internal O1 = 0 with get, set
        member val internal O2 = 0 with get, set
        member val internal O3 = 0 with get, set

    /// A 64-bit value as four 16-bit limbs, least significant first: the working form of every step.
    [<Struct>]
    type private Limbs = { L0: int; L1: int; L2: int; L3: int }

    let private limbs (l0: int) (l1: int) (l2: int) (l3: int) : Limbs = { L0 = l0; L1 = l1; L2 = l2; L3 = l3 }

    // The Weyl increment `0x9E3779B97F4A7C15` and the two multipliers `0xBF58476D1CE4E5B9` and
    // `0x94D049BB133111EB`, as limbs.
    let private gamma = limbs 0x7C15 0x7F4A 0x79B9 0x9E37
    let private mulA = limbs 0xE5B9 0x1CE4 0x476D 0xBF58
    let private mulB = limbs 0x11EB 0x1331 0x49BB 0x94D0

    /// `a + b` modulo `2⁶⁴`.
    let private add (a: Limbs) (b: Limbs) : Limbs =
        let c0 = a.L0 + b.L0
        let c1 = a.L1 + b.L1 + (c0 >>> 16)
        let c2 = a.L2 + b.L2 + (c1 >>> 16)
        let c3 = a.L3 + b.L3 + (c2 >>> 16)
        limbs (c0 &&& 0xFFFF) (c1 &&& 0xFFFF) (c2 &&& 0xFFFF) (c3 &&& 0xFFFF)

    /// The carry out of a column sum `c` (an integer below `2³⁵`, held exactly in a float) and the limb
    /// it leaves: `c = carry · 2¹⁶ + limb`. The quotient by a power of two is exact, and truncating it
    /// to an `int` reads an integer below `2¹⁹`.
    let inline private split (c: float) : struct (float * int) =
        let carry = float (int (c / 65536.0))
        struct (carry, int (c - carry * 65536.0))

    /// `a · b` modulo `2⁶⁴`: the schoolbook columns, each limb product formed in float arithmetic
    /// (below `2³²`, exact), carried upward; the columns past the fourth limb are never formed.
    let private mul (a: Limbs) (b: Limbs) : Limbs =
        let a0, a1, a2, a3 = float a.L0, float a.L1, float a.L2, float a.L3
        let b0, b1, b2, b3 = float b.L0, float b.L1, float b.L2, float b.L3
        let struct (k0, l0) = split (a0 * b0)
        let struct (k1, l1) = split (a0 * b1 + a1 * b0 + k0)
        let struct (k2, l2) = split (a0 * b2 + a1 * b1 + a2 * b0 + k1)
        let struct (_, l3) = split (a0 * b3 + a1 * b2 + a2 * b1 + a3 * b0 + k2)
        limbs l0 l1 l2 l3

    /// `z xor (z >>> n)` for a shift `n` of sixteen to thirty-one bits: the three shifts SplitMix64
    /// takes (thirty, twenty-seven and thirty-one).
    let private xorShift (n: int) (z: Limbs) : Limbs =
        let r = n - 16
        let s0 = ((z.L1 >>> r) ||| (z.L2 <<< (16 - r))) &&& 0xFFFF
        let s1 = ((z.L2 >>> r) ||| (z.L3 <<< (16 - r))) &&& 0xFFFF
        let s2 = z.L3 >>> r
        limbs (z.L0 ^^^ s0) (z.L1 ^^^ s1) (z.L2 ^^^ s2) z.L3

    /// SplitMix64's output function: two xor-shift-multiply rounds and a final xor-shift.
    let private mix (z: Limbs) : Limbs =
        xorShift 31 (mul (xorShift 27 (mul (xorShift 30 z) mulA)) mulB)

    /// The limbs of a `uint64`.
    let private ofUInt64 (v: uint64) : Limbs =
        limbs
            (int (v &&& 0xFFFFUL))
            (int ((v >>> 16) &&& 0xFFFFUL))
            (int ((v >>> 32) &&& 0xFFFFUL))
            (int ((v >>> 48) &&& 0xFFFFUL))

    /// Advance the stream one step, leaving the draw in its output limbs.
    let private step (g: Stream) : unit =
        let s = add (limbs g.S0 g.S1 g.S2 g.S3) gamma
        g.S0 <- s.L0
        g.S1 <- s.L1
        g.S2 <- s.L2
        g.S3 <- s.L3
        let o = mix s
        g.O0 <- o.L0
        g.O1 <- o.L1
        g.O2 <- o.L2
        g.O3 <- o.L3

    /// The generator whose state is `state`, as the reference implementation seeds it: what the
    /// published SplitMix64 vectors are stated against, and what `ofSeed` is defined by.
    let internal ofState (state: uint64) : Stream =
        let s = ofUInt64 state
        Stream(s.L0, s.L1, s.L2, s.L3)

    /// `a · b` modulo `2⁶⁴` by the limb product, over `uint64` operands: what the suite holds to the
    /// host's own 64-bit product (D76's probe).
    let internal limbProduct (a: uint64) (b: uint64) : uint64 =
        let p = mul (ofUInt64 a) (ofUInt64 b)

        (uint64 p.L3 <<< 48)
        ||| (uint64 p.L2 <<< 32)
        ||| (uint64 p.L1 <<< 16)
        ||| uint64 p.L0

    /// Stream `stream` of the generator seeded with `seed`. Its starting state is the `(stream + 1)`-th
    /// draw of the SplitMix64 generator whose state is `seed` — `mix(seed + (stream + 1) · γ)`, one
    /// product — so every stream of one seed starts from a well-mixed state, stream `k` costs what
    /// stream `0` does, and a seed's streams are named by an index and nothing else. A negative `stream`
    /// is refused.
    let ofSeed (seed: int64) (stream: int) : Stream =
        if stream < 0 then
            invalidArg "stream" "Rng.ofSeed: a stream index is not negative"

        let k = stream + 1
        let index = limbs (k &&& 0xFFFF) ((k >>> 16) &&& 0xFFFF) 0 0
        let s = mix (add (ofUInt64 (uint64 seed)) (mul index gamma))
        Stream(s.L0, s.L1, s.L2, s.L3)

    /// The next draw, whole: sixty-four bits.
    let next (g: Stream) : uint64 =
        step g

        (uint64 g.O3 <<< 48)
        ||| (uint64 g.O2 <<< 32)
        ||| (uint64 g.O1 <<< 16)
        ||| uint64 g.O0

    /// The next draw as a float in `[0, 1)`: its top fifty-three bits times `2⁻⁵³`, every value a
    /// multiple of `2⁻⁵³` and each equally likely.
    let uniform53 (g: Stream) : float =
        step g

        (float g.O3 * 137438953472.0
         + float g.O2 * 2097152.0
         + float g.O1 * 32.0
         + float (g.O0 >>> 11))
        * (1.0 / 9007199254740992.0)

    /// An integer in `0 .. n − 1`, each equally likely, by rejection: the top thirty-two bits `x` of a
    /// draw are taken while `x < 2³² mod n` is redrawn, and the answer is `x mod n`. Every operand is an
    /// integer below `2³³` held in a float, where the remainder is exact. `n` below one is refused.
    let below (g: Stream) (n: int) : int =
        if n < 1 then
            invalidArg "n" "Rng.below: n is at least one"

        let m = float n
        let threshold = (4294967296.0 - m) % m
        let mutable x = -1.0

        while x < threshold do
            step g
            x <- float g.O3 * 65536.0 + float g.O2

        int (x % m)
