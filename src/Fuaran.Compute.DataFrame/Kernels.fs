namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Fuaran.Compute.DataFrame — the evaluator's host kernels (Phase 270).
//
//  The few loops over the frame's typed vectors where the .NET host can be
//  helped and Fable cannot: a comparison of a typed column against a constant
//  into a selection bitmap, the conjunction and disjunction of two bitmaps,
//  the bitmap read back as a selection, and the morsel runner the row-local
//  verbs (`Filter`, `Derive`) evaluate their rows through.
//
//  Every kernel is a PAIR with one signature: `Kernels.Portable` is plain
//  loops over arrays and is the Fable path; `Kernels.Native` (compiled only
//  when `FABLE_COMPILER` is not defined) uses `System.Runtime.Intrinsics` —
//  in-box, so the package stays FSharp.Core-only — and the thread pool.
//  `Kernels.host` is the pair's member the evaluator runs, chosen at compile
//  time; nothing reads a flag at run time. The portable member compiles on
//  .NET too, so the suite runs the transform law vectors through both members
//  on one host and holds their answers byte-identical.
//
//  Determinism is the contract. A comparison is a per-row predicate and a
//  bitmap is read back in physical order, so neither can reorder anything;
//  the morsel runner partitions rows into fixed ranges whose results the
//  caller concatenates in range order. No float reduction lives here: a float
//  `Sum` / `Mean` / `StdDev` stays the sequential left-to-right fold of the
//  aggregate on every host, because a reassociated float sum changes the last
//  bit and with it the wire bytes.
//
//  Measured and NOT kept: an order-preserving parallel `GroupBy` (per-morsel
//  tables merged on first-seen index). At ten keys it was at parity with the
//  sequential grouping at 100,000 and 1,000,000 rows (14.1 against 13.8 ms,
//  388 against 408 ms); at one key per ten rows the merge made it 2.3 and 1.8
//  times slower (96.6 against 42.5 ms, 1,338 against 760 ms). A grouping over
//  this evaluator is dominated by boxing the aggregated column, not by the
//  probe, so threads bought nothing at the sizes measured.
//
//  Re-measured natively (Phase 341). Phase 270's figures above came from the
//  x64 build under emulation on an Arm64 machine. On the native Arm64 JIT
//  (benchmarks/results/2026-10-03-snapdragon-x1e80100-phase-341.md):
//  the comparison kernels and the morsel `Filter` hold (4.2 to 5.5 times,
//  and 2.9 times at 100,000 and 1,000,000 rows). The morsel `Derive`,
//  "within noise" at a million rows under emulation, pays 2.7 times at
//  100,000 and 1,000,000 rows (quiet machine). The parallel `GroupBy` above,
//  rebuilt over Phase 323's streamed aggregates, is 1.55 times FASTER at ten
//  keys and a million rows (3.94 against 6.10 ms) and 4.0 to 7.2 times slower
//  at one key per ten rows. Boxing no longer dominates the step. At high
//  cardinality the merge of per-morsel tables is the cost, and the merge
//  reassociates a float `Sum`. It stays out; Phase 344 partitions by group
//  instead. Morsel size, swept twice: at 10,000 rows 4,096 leads 8,192
//  (three morsels against two); at 100,000 rows 8,192 is best or tied; at a
//  million rows the two sittings disagree. `MorselRows` is unchanged.
//
//  Not built: vectorised integer Sum / Count / Min / Max. Every reduction
//  this evaluator makes is an aggregate over one GROUP's members — scattered
//  physical rows, gathered into the cell list `Column.aggregate` reads — so no
//  step reduces a contiguous typed range, and no shape of the benchmark corpus
//  (grouping, pivot, window) would reach such a kernel. It becomes worth
//  building when an aggregate reads a typed vector over a contiguous range.
// ============================================================================

#if !FABLE_COMPILER
open System
open System.Numerics
open System.Runtime.InteropServices
open System.Runtime.Intrinsics
open System.Threading.Tasks
#endif

/// The comparison a comparison kernel tests between a row's value and the constant — the six
/// comparison operators, with the row's value on the LEFT.
type internal CmpOp =
    | CLt
    | CLe
    | CGt
    | CGe
    | CEq
    | CNe

/// One member of the kernel pair: the kernels as one record of functions, so the evaluator takes
/// the set it runs as a value and the suite can hand it either member. A selection bitmap holds
/// physical row `p` at bit `p % 32` of word `p / 32`; a bit past the vector's end is never set.
type internal KernelSet =
    {
        /// `CmpInts op vals mask k count` — the rows `p < count` whose value is present
        /// (`mask[p]`) and satisfies `vals[p] op k`: the rows on which the comparison is `true`.
        CmpInts: CmpOp -> int[] -> bool[] -> int -> int -> uint32[]
        /// `CmpFloats op vals mask k count` — the same over a float vector, under the evaluator's
        /// pinned float ordering (`compareFloat`: `NaN` equal to itself and above every other
        /// value, `-0.0` equal to `0.0`).
        CmpFloats: CmpOp -> float[] -> bool[] -> float -> int -> uint32[]
        /// The rows set in both bitmaps.
        And: uint32[] -> uint32[] -> uint32[]
        /// The rows set in either bitmap.
        Or: uint32[] -> uint32[] -> uint32[]
        /// The set rows, ascending.
        Selection: uint32[] -> int[]
        /// `RunMorsels m body` runs `body j` for the morsels `j` in `0 .. m - 1`. `body` answers
        /// `false` when its morsel recorded an error: the portable member stops there, and the
        /// native member (which may run morsels concurrently) runs them all — a caller reads the
        /// first error in morsel order, which is the first error in row order either way.
        RunMorsels: int -> (int -> bool) -> unit
    }

module internal Kernels =

    /// The rows in one morsel of a row-local step. A step over at most this many rows runs as
    /// one morsel, on the caller's thread, exactly as it did before morsels existed. Chosen by
    /// measurement (Phase 270): a compiled `Filter` and the sheet's two `Derive`s over 20,000,
    /// 100,000 and 1,000,000 rows at 4,096, 8,192, 16,384 and 32,768 rows a morsel; 4,096 and
    /// 8,192 led at 20,000 and 100,000 rows and the four were within noise at a million, and
    /// the larger of the two leaders compiles half as many trees per step.
    [<Literal>]
    let MorselRows = 8192

    /// How many morsels `n` rows make.
    let morselCount (n: int) : int =
        if n <= 0 then 0 else (n + MorselRows - 1) / MorselRows

    /// The first row of morsel `j`.
    let morselStart (j: int) : int = j * MorselRows

    /// One past the last row of morsel `j` of `n` rows.
    let morselEnd (n: int) (j: int) : int = min n ((j + 1) * MorselRows)

    /// The words a bitmap of `count` rows needs.
    let words (count: int) : int = (count + 31) >>> 5

    /// Is row `p` set in `bits`?
    let isSet (bits: uint32[]) (p: int) : bool =
        (Raw.at (p >>> 5) bits >>> (p &&& 31)) &&& 1u <> 0u

    /// THE evaluator's float ordering (Phase 321): IEEE order on the values that are not `NaN`,
    /// `-0.0` equal to `0.0`, and `NaN` one value ABOVE every other — the substrate's `Cell.compare`
    /// order, which `Column.aggregate`'s `Min` / `Max` already read, so a sort and an aggregate
    /// agree about where a `NaN` sits. Stated here rather than delegated to the host's `compare`,
    /// which put `NaN` below every value on .NET and answered `1` for both `compare nan 1.0` and
    /// `compare 1.0 nan` under Fable — an order on one host and not an order on the other.
    let compareFloat (a: float) (b: float) : int =
        if System.Double.IsNaN a then
            (if System.Double.IsNaN b then 0 else 1)
        elif System.Double.IsNaN b then
            -1
        elif a < b then
            -1
        elif a > b then
            1
        else
            0

    /// The comparison `op` makes of an ordering's answer `c`.
    let holds (op: CmpOp) (c: int) : bool =
        match op with
        | CLt -> c < 0
        | CLe -> c <= 0
        | CGt -> c > 0
        | CGe -> c >= 0
        | CEq -> c = 0
        | CNe -> c <> 0

    /// The comparison that tests the same thing with its operands swapped: `k op v` is
    /// `v (flip op) k`.
    let flip (op: CmpOp) : CmpOp =
        match op with
        | CLt -> CGt
        | CLe -> CGe
        | CGt -> CLt
        | CGe -> CLe
        | CEq -> CEq
        | CNe -> CNe

    /// The portable member: loops over arrays, one row at a time. The Fable path, and the
    /// definition the native member is held equal to.
    module Portable =

        let cmpInts (op: CmpOp) (vals: int[]) (mask: bool[]) (k: int) (count: int) : uint32[] =
            let out: uint32[] = Array.zeroCreate (words count)
            // The loop proves `p` once `mask` and `vals` are known to hold `count` rows (Phase 326),
            // and `p >>> 5` for `out`, which holds a bit per row. The mask is checked first and as a
            // span, because the native member takes a span over it before anything else.
            Raw.withinSpan count mask
            Raw.within count vals

            for p in 0 .. count - 1 do
                if Raw.get p mask && holds op (compare (Raw.get p vals) k) then
                    Raw.set out (p >>> 5) (Raw.get (p >>> 5) out ||| (1u <<< (p &&& 31)))

            out

        let cmpFloats (op: CmpOp) (vals: float[]) (mask: bool[]) (k: float) (count: int) : uint32[] =
            let out: uint32[] = Array.zeroCreate (words count)
            // The loop proves `p` once `mask` and `vals` are known to hold `count` rows (Phase 326),
            // and `p >>> 5` for `out`, which holds a bit per row. The mask is checked first and as a
            // span, because the native member takes a span over it before anything else.
            Raw.withinSpan count mask
            Raw.within count vals

            for p in 0 .. count - 1 do
                if Raw.get p mask && holds op (compareFloat (Raw.get p vals) k) then
                    Raw.set out (p >>> 5) (Raw.get (p >>> 5) out ||| (1u <<< (p &&& 31)))

            out

        let andBits (a: uint32[]) (b: uint32[]) : uint32[] = Array.map2 (fun x y -> x &&& y) a b

        let orBits (a: uint32[]) (b: uint32[]) : uint32[] = Array.map2 (fun x y -> x ||| y) a b

        let selection (bits: uint32[]) : int[] =
            let out = ResizeArray<int>()

            for w in 0 .. bits.Length - 1 do
                let word = Raw.get w bits

                if word <> 0u then
                    for b in 0..31 do
                        if (word >>> b) &&& 1u <> 0u then
                            out.Add((w <<< 5) + b)

            out.ToArray()

        let runMorsels (m: int) (body: int -> bool) : unit =
            let mutable j = 0

            while j < m && body j do
                j <- j + 1

    /// The portable member as a kernel set.
    let portable: KernelSet =
        { CmpInts = Portable.cmpInts
          CmpFloats = Portable.cmpFloats
          And = Portable.andBits
          Or = Portable.orBits
          Selection = Portable.selection
          RunMorsels = Portable.runMorsels }

#if !FABLE_COMPILER
    /// The native member: 128-bit vectors (the width both x64 and Arm64 accelerate) sixteen rows
    /// at a time, a scalar tail through the portable loop's own test, and the thread pool for
    /// morsels. Every answer is the portable member's answer; the suite holds them so.
    module Native =

        /// Sixteen rows of the validity mask, from row `p`, as sixteen bits in row order (a `bool` is
        /// one byte, `1` for true).
        let inline private present16 (maskBytes: ReadOnlySpan<byte>) (p: int) : uint32 =
            Vector128.ExtractMostSignificantBits(
                Vector128.GreaterThan(Vector128.Create<byte>(maskBytes.Slice(p, 16)), Vector128<byte>.Zero)
            )

        let inline private intLoop
            ([<InlineIfLambda>] cmp: Vector128<int> -> Vector128<int> -> Vector128<int>)
            (op: CmpOp)
            (vals: int[])
            (mask: bool[])
            (k: int)
            (count: int)
            : uint32[] =
            let out: uint32[] = Array.zeroCreate (words count)
            let maskBytes = MemoryMarshal.Cast<bool, byte>(ReadOnlySpan<bool>(mask, 0, count))
            let kv = Vector128.Create k
            let mutable p = 0

            while p + 16 <= count do
                let bits =
                    Vector128.ExtractMostSignificantBits(cmp (Vector128.Create(vals, p)) kv)
                    ||| (Vector128.ExtractMostSignificantBits(cmp (Vector128.Create(vals, p + 4)) kv)
                         <<< 4)
                    ||| (Vector128.ExtractMostSignificantBits(cmp (Vector128.Create(vals, p + 8)) kv)
                         <<< 8)
                    ||| (Vector128.ExtractMostSignificantBits(cmp (Vector128.Create(vals, p + 12)) kv)
                         <<< 12)

                // `p` is a multiple of sixteen, so the sixteen bits land whole in one word.
                out[p >>> 5] <- out[p >>> 5] ||| ((bits &&& present16 maskBytes p) <<< (p &&& 31))
                p <- p + 16

            while p < count do
                if mask[p] && holds op (compare vals[p] k) then
                    out[p >>> 5] <- out[p >>> 5] ||| (1u <<< (p &&& 31))

                p <- p + 1

            out

        let cmpInts (op: CmpOp) (vals: int[]) (mask: bool[]) (k: int) (count: int) : uint32[] =
            match op with
            | CLt -> intLoop (fun a b -> Vector128.LessThan(a, b)) op vals mask k count
            | CLe -> intLoop (fun a b -> Vector128.LessThanOrEqual(a, b)) op vals mask k count
            | CGt -> intLoop (fun a b -> Vector128.GreaterThan(a, b)) op vals mask k count
            | CGe -> intLoop (fun a b -> Vector128.GreaterThanOrEqual(a, b)) op vals mask k count
            | CEq -> intLoop (fun a b -> Vector128.Equals(a, b)) op vals mask k count
            | CNe -> intLoop (fun a b -> Vector128.OnesComplement(Vector128.Equals(a, b))) op vals mask k count

        // The pinned float ordering differs from the IEEE comparisons the vector unit makes in one
        // place only: a `NaN` row compares ABOVE a non-`NaN` constant (Phase 321), where every IEEE
        // comparison but `<>` is false. So `>` and `>=` also take the `NaN` rows (`v <> v`); `<`,
        // `<=` and `=` are false there under both, and `<>` true under both. A `NaN` CONSTANT
        // orders above every row that is not `NaN` and equal to the ones that are, which no single
        // IEEE comparison says; that case takes the portable loop.
        let inline private floatLoop
            ([<InlineIfLambda>] cmp: Vector128<float> -> Vector128<float> -> Vector128<float>)
            (nanTrue: bool)
            (op: CmpOp)
            (vals: float[])
            (mask: bool[])
            (k: float)
            (count: int)
            : uint32[] =
            let out: uint32[] = Array.zeroCreate (words count)
            let maskBytes = MemoryMarshal.Cast<bool, byte>(ReadOnlySpan<bool>(mask, 0, count))
            let kv = Vector128.Create k

            let two (q: int) : uint32 =
                let v = Vector128.Create(vals, q)
                let r = cmp v kv

                let r =
                    if nanTrue then
                        r ||| Vector128.OnesComplement(Vector128.Equals(v, v))
                    else
                        r

                Vector128.ExtractMostSignificantBits r

            let mutable p = 0

            while p + 16 <= count do
                let bits =
                    two p
                    ||| (two (p + 2) <<< 2)
                    ||| (two (p + 4) <<< 4)
                    ||| (two (p + 6) <<< 6)
                    ||| (two (p + 8) <<< 8)
                    ||| (two (p + 10) <<< 10)
                    ||| (two (p + 12) <<< 12)
                    ||| (two (p + 14) <<< 14)

                out[p >>> 5] <- out[p >>> 5] ||| ((bits &&& present16 maskBytes p) <<< (p &&& 31))
                p <- p + 16

            while p < count do
                if mask[p] && holds op (compareFloat vals[p] k) then
                    out[p >>> 5] <- out[p >>> 5] ||| (1u <<< (p &&& 31))

                p <- p + 1

            out

        let cmpFloats (op: CmpOp) (vals: float[]) (mask: bool[]) (k: float) (count: int) : uint32[] =
            if Double.IsNaN k then
                Portable.cmpFloats op vals mask k count
            else
                match op with
                | CLt -> floatLoop (fun a b -> Vector128.LessThan(a, b)) false op vals mask k count
                | CLe -> floatLoop (fun a b -> Vector128.LessThanOrEqual(a, b)) false op vals mask k count
                | CGt -> floatLoop (fun a b -> Vector128.GreaterThan(a, b)) true op vals mask k count
                | CGe -> floatLoop (fun a b -> Vector128.GreaterThanOrEqual(a, b)) true op vals mask k count
                | CEq -> floatLoop (fun a b -> Vector128.Equals(a, b)) false op vals mask k count
                | CNe ->
                    floatLoop (fun a b -> Vector128.OnesComplement(Vector128.Equals(a, b))) false op vals mask k count

        let inline private zip
            ([<InlineIfLambda>] vec: Vector128<uint32> -> Vector128<uint32> -> Vector128<uint32>)
            ([<InlineIfLambda>] scalar: uint32 -> uint32 -> uint32)
            (a: uint32[])
            (b: uint32[])
            : uint32[] =
            let n = min a.Length b.Length
            let out: uint32[] = Array.zeroCreate n
            let mutable i = 0

            while i + 4 <= n do
                (vec (Vector128.Create(a, i)) (Vector128.Create(b, i))).CopyTo(out, i)
                i <- i + 4

            while i < n do
                out[i] <- scalar a[i] b[i]
                i <- i + 1

            out

        let andBits (a: uint32[]) (b: uint32[]) : uint32[] =
            zip (fun x y -> x &&& y) (fun x y -> x &&& y) a b

        let orBits (a: uint32[]) (b: uint32[]) : uint32[] =
            zip (fun x y -> x ||| y) (fun x y -> x ||| y) a b

        let selection (bits: uint32[]) : int[] =
            let mutable total = 0

            for w in bits do
                total <- total + BitOperations.PopCount w

            let out: int[] = Array.zeroCreate total
            let mutable at = 0

            for w in 0 .. bits.Length - 1 do
                let mutable word = bits[w]

                while word <> 0u do
                    out[at] <- (w <<< 5) + BitOperations.TrailingZeroCount word
                    at <- at + 1
                    word <- word &&& (word - 1u)

            out

        let runMorsels (m: int) (body: int -> bool) : unit =
            if m = 1 then
                body 0 |> ignore
            elif m > 1 then
                Parallel.For(0, m, (fun j -> body j |> ignore)) |> ignore

    /// The native member as a kernel set.
    let native: KernelSet =
        { CmpInts = Native.cmpInts
          CmpFloats = Native.cmpFloats
          And = Native.andBits
          Or = Native.orBits
          Selection = Native.selection
          RunMorsels = Native.runMorsels }

    /// The member the evaluator runs on this host — chosen when the package is compiled.
    let host: KernelSet = native
#else
    /// The member the evaluator runs on this host — chosen when the package is compiled.
    let host: KernelSet = portable
#endif
