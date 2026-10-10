namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Fuaran.Compute.DataFrame — the evaluator's dense columnar frame (Phase 267).
//
//  The evaluator's working form between the `Table` it is handed and the
//  `Table` it returns. Internal to this assembly: `Table` stays the boundary
//  and `Column` in the column layer is untouched, so a consumer never sees a
//  vector, a mask or a selection. What it sees is the same cells in the same
//  order — the transform law vectors decide that, byte for byte.
//
//  One vector per column, typed where the column's cells agree with its
//  declared type (an `int[]`, a `float[]`, a `bool[]`, a `string[]`, or for a
//  decimal the scaled integers in a `float[]` beside the cells (Phase 280), each
//  beside a validity mask (a `Mask`: `bool[]` on .NET, bytes under Fable) — a
//  present integer cell costs five bytes at rest, where a `Cell` in a list
//  cost about fifty-six), and a boxed
//  `Cell[]` where they do not. A selection vector says which physical rows
//  the frame currently holds and in what order, so a `Filter` keeps a subset,
//  a `Limit` slices and a `Sort` permutes without copying a cell; a `Derive`
//  adds one vector and shares every other by reference; the gathering verbs
//  read rows through the selection and emit fresh vectors.
//
//  Fable-clean: numeric arrays compile to typed arrays, a mask to a `Uint8Array` (Phase 376),
//  and nothing here uses spans, intrinsics or threads; under Fable the `Shared` module allocates over
//  shared memory once a host opts in to the worker pool.
// ============================================================================

/// The schema entries the evaluator writes (Phase 423: a `Schema` is a `Field list`, and a field
/// carries metadata the evaluator neither reads nor invents).
module internal Fields =

    /// `f` under another name, its type and every stated metadatum kept: a projection renames a
    /// column and states nothing new about it.
    let rename (name: string) (f: Field) : Field =
        let g = Field.create name f.Type

        let g =
            match f.Unit with
            | Some u -> Field.withUnit u g
            | None -> g

        let g =
            match f.Label with
            | Some l -> Field.withLabel l g
            | None -> g

        let g =
            match f.Description with
            | Some d -> Field.withDescription d g
            | None -> g

        f.Ext |> Map.fold (fun g k v -> Field.withExt k v g) g

    /// The names of a schema, in order.
    let names (cols: Schema) : string list = cols |> List.map _.Name

    /// The types of a schema, in order.
    let types (cols: Schema) : ColumnType list = cols |> List.map _.Type

/// The refusal a compute codec makes of a tag outside its vocabulary (Phase 423). Core `1.0.0`'s
/// `ColumnError.UnknownType` names column TYPES, so a verb, a function or a mode outside its closed
/// set is a `MalformedShape` whose detail names the tag and every admitted spelling.
module internal WireRefusal =

    let unknownTag (got: string, expected: string list) : ColumnError =
        MalformedShape(
            "unknown tag '"
            + got
            + "' (expected one of: "
            + String.concat ", " expected
            + ")"
        )

/// Array access as the JavaScript host should emit it (Phase 326). Fable compiles every `a[i]` on an
/// `Int32Array`, a `Float64Array` or a plain array to one shared bounds-checked helper, which goes
/// megamorphic across the three and costs more than the loop around it; these accessors are what
/// the evaluator's loops index through instead. On .NET each is the plain index, inlined, so the
/// compiled code there is the code it was.
///
/// Two families, and the rule that keeps refusal behaviour unchanged:
///
///  * `get` / `set` carry NO bounds check under JavaScript. They are used only where the code
///    around the access proves the index: a loop over `0 .. n - 1` reading arrays allocated `n`
///    long (or checked to be at least `n` long before it starts), or a range test on the index
///    immediately before it. No general accessor drops the check.
///  * `at` / `put` are the checked read and write for every other index — a row number read out of
///    a selection, an index vector, a caller's argument. Under JavaScript each is the bounds test
///    inlined at the call site rather than the shared helper, and an index outside the array is
///    refused with the .NET runtime's own message, so a malformed frame is refused alike on both
///    hosts.
module internal Raw =

    /// The refusal of an index outside its array: the .NET runtime's message, on every host.
    let outOfRange () : 'T =
        failwith "Index was outside the bounds of the array."

    /// The refusal of a span reaching past its array: the .NET runtime's message, on every host.
    let outOfSpan () : 'T =
        failwith "Specified argument was out of the range of valid values."

#if FABLE_COMPILER
    /// `a[i]`, unchecked: only where the enclosing loop proves `0 <= i < a.Length`.
    [<Fable.Core.Emit("$1[$0]")>]
    let get (i: int) (a: 'T[]) : 'T = Fable.Core.Util.jsNative

    /// `a[i] <- v`, unchecked: only where the enclosing loop proves `0 <= i < a.Length`.
    [<Fable.Core.Emit("$0[$1] = $2")>]
    let set (a: 'T[]) (i: int) (v: 'T) : unit = Fable.Core.Util.jsNative

    /// `a[i]`, checked: the bounds test inlined, refusing with `outOfRange`.
    let inline at (i: int) (a: 'T[]) : 'T =
        if uint32 i >= uint32 a.Length then
            outOfRange ()
        else
            get i a

    /// `a[i] <- v`, checked: the bounds test inlined, refusing with `outOfRange`.
    let inline put (a: 'T[]) (i: int) (v: 'T) : unit =
        if uint32 i >= uint32 a.Length then
            outOfRange ()
        else
            set a i v

    /// The check a loop over `0 .. n - 1` makes once, before it indexes `a` through `get` / `set`:
    /// an `a` shorter than `n` is refused as the first out-of-range index in it would have been.
    let inline within (n: int) (a: 'T[]) : unit =
        if a.Length < n then
            outOfRange ()

    /// `within`, refusing as a span over the first `n` elements of `a` is refused: where the .NET
    /// host takes such a span before its loop, the JavaScript host refuses with its message.
    let inline withinSpan (n: int) (a: 'T[]) : unit =
        if a.Length < n then
            outOfSpan ()

    /// Sort `a` ascending in place with the engine's own numeric sort, which a typed array of
    /// floats sorts without a comparator. Only for floats holding no `NaN`: on those it is the order
    /// `Array.sortInPlace` gives, where that compiles to a comparator sort under JavaScript.
    [<Fable.Core.Emit("$0.sort()")>]
    let sortFinite (a: float[]) : unit = Fable.Core.Util.jsNative
#else
    /// `a[i]`; the runtime checks it, as it always did.
    let inline get (i: int) (a: 'T[]) : 'T = a[i]

    /// `a[i] <- v`; the runtime checks it, as it always did.
    let inline set (a: 'T[]) (i: int) (v: 'T) : unit = a[i] <- v

    /// `a[i]`; the runtime checks it, as it always did.
    let inline at (i: int) (a: 'T[]) : 'T = a[i]

    /// `a[i] <- v`; the runtime checks it, as it always did.
    let inline put (a: 'T[]) (i: int) (v: 'T) : unit = a[i] <- v

    /// Nothing: the runtime checks every index the loop makes, as it always did.
    let inline within (_: int) (_: 'T[]) : unit = ()

    /// Nothing: the runtime checks the span, as it always did.
    let inline withinSpan (_: int) (_: 'T[]) : unit = ()

    /// `Array.sortInPlace`, as it always was.
    let inline sortFinite (a: float[]) : unit = Array.sortInPlace a
#endif

/// A row of booleans as the frame holds it (Phase 376): a vector's validity mask (`true` = present),
/// and a `Bools` vector's values. On .NET a `bool[]`, the representation it always was. Under Fable
/// a `byte[]` — a `Uint8Array`, one byte a row, `1` for `true` — where a `bool[]` would be a plain
/// JavaScript `Array`: the typed form allocates zeroed (every row absent) without a fill, indexes
/// monomorphically, and is the form a typed array over shared memory can back, so a worker reads a
/// frame's masks where they lie instead of rebuilding them (`DECISIONS.md` D13 item 5, D14). Read and
/// written only through the `Mask` module, whose accessors are the plain index on .NET.
#if FABLE_COMPILER
type internal Mask = byte[]
#else
type internal Mask = bool[]
#endif

/// The accessors of a `Mask`, in `Raw`'s two families: `get` / `set` unchecked under JavaScript
/// (only where the loop proves the index), `at` / `put` checked, refusing as `Raw` does. On .NET
/// each is the plain index, inlined, so the code there is the code it was.
module internal Mask =

#if FABLE_COMPILER
    /// `m[i]`, unchecked: only where the enclosing loop proves `0 <= i < m.Length`.
    [<Fable.Core.Emit("($1[$0] !== 0)")>]
    let get (i: int) (m: Mask) : bool = Fable.Core.Util.jsNative

    /// `m[i] <- v`, unchecked; a `Uint8Array` stores `true` as `1` and `false` as `0`.
    [<Fable.Core.Emit("$0[$1] = $2")>]
    let set (m: Mask) (i: int) (v: bool) : unit = Fable.Core.Util.jsNative

    /// `m[i]`, checked.
    let inline at (i: int) (m: Mask) : bool =
        if uint32 i >= uint32 m.Length then
            Raw.outOfRange ()
        else
            get i m

    /// `m[i] <- v`, checked.
    let inline put (m: Mask) (i: int) (v: bool) : unit =
        if uint32 i >= uint32 m.Length then
            Raw.outOfRange ()
        else
            set m i v

    /// A mask of `n` rows, every one `false`.
    let inline zero (n: int) : Mask = Array.zeroCreate n

    /// The mask holding `xs`, row for row.
    let ofBools (xs: bool[]) : Mask =
        let m: Mask = Array.zeroCreate xs.Length

        for i in 0 .. xs.Length - 1 do
            set m i (Raw.get i xs)

        m

    /// The mask's rows as booleans.
    let toBools (m: Mask) : bool[] =
        let out: bool[] = Array.zeroCreate m.Length

        for i in 0 .. m.Length - 1 do
            Raw.set out i (get i m)

        out
#else
    /// `m[i]`; the runtime checks it, as it always did.
    let inline get (i: int) (m: Mask) : bool = m[i]

    /// `m[i] <- v`; the runtime checks it, as it always did.
    let inline set (m: Mask) (i: int) (v: bool) : unit = m[i] <- v

    /// `m[i]`; the runtime checks it, as it always did.
    let inline at (i: int) (m: Mask) : bool = m[i]

    /// `m[i] <- v`; the runtime checks it, as it always did.
    let inline put (m: Mask) (i: int) (v: bool) : unit = m[i] <- v

    /// A mask of `n` rows, every one `false`.
    let inline zero (n: int) : Mask = Array.zeroCreate n

    /// The mask holding `xs`: the very array, on this host.
    let inline ofBools (xs: bool[]) : Mask = xs

    /// The mask's rows as booleans: the very array, on this host.
    let inline toBools (m: Mask) : bool[] = m
#endif

    /// A mask of `n` rows, every one `v`.
    let create (n: int) (v: bool) : Mask =
        let m = zero n

        if v then
            for i in 0 .. n - 1 do
                set m i true

        m

    /// A mask of `n` rows, row `i` being `f i`.
    let init (n: int) (f: int -> bool) : Mask =
        let m = zero n

        for i in 0 .. n - 1 do
            set m i (f i)

        m

    /// Is any row `true`?
    let any (m: Mask) : bool =
        let mutable found = false
        let mutable i = 0

        while not found && i < m.Length do
            found <- get i m
            i <- i + 1

        found

    /// Is every row `true`?
    let all (m: Mask) : bool =
        let mutable ok = true
        let mutable i = 0

        while ok && i < m.Length do
            ok <- get i m
            i <- i + 1

        ok

/// Where the frame's typed arrays live (Phase 376). Under Fable, once a host has opted in to the
/// worker pool (`WorkerPool.optIn`) on a page that has shared memory, int and float values and masks
/// are allocated in typed arrays over a `SharedArrayBuffer`, which a structured clone hands a worker
/// by reference rather than by copy; the `share` forms copy an array that is not into one that is,
/// once, for the worker path. A host that has not opted in allocates exactly as before. On .NET every
/// array is already visible to every thread, so nothing here allocates differently and `share` is the
/// array itself.
module internal Shared =

#if FABLE_COMPILER
    /// Does this realm have shared memory? `SharedArrayBuffer` exists only on a cross-origin isolated
    /// page (COOP `same-origin` and COEP `require-corp`) or outside a browser.
    [<Fable.Core.Emit("(typeof SharedArrayBuffer !== 'undefined' && globalThis.crossOriginIsolated !== false)")>]
    let available () : bool = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("(typeof SharedArrayBuffer !== 'undefined' && $0.buffer instanceof SharedArrayBuffer)")>]
    let private sharedBacked (a: 'T[]) : bool = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("new Int32Array(new SharedArrayBuffer($0 * 4))")>]
    let private newInts (n: int) : int[] = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("new Float64Array(new SharedArrayBuffer($0 * 8))")>]
    let private newFloats (n: int) : float[] = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("new Uint8Array(new SharedArrayBuffer($0))")>]
    let private newBytes (n: int) : byte[] = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0.set($1)")>]
    let private copyInto (dst: 'T[]) (src: 'T[]) : unit = Fable.Core.Util.jsNative

    let mutable private on = false

    /// Allocate in shared memory from now on, where this realm has it; answers whether it does.
    let enable () : bool =
        on <- available ()
        on

    /// Allocate as before.
    let disable () : unit = on <- false

    /// Is the frame being allocated in shared memory?
    let isOn () : bool = on

    /// Is `a` over shared memory, and so handed to a worker by reference?
    let isShared (a: 'T[]) : bool = sharedBacked a

    /// `n` zeroed ints: shared when allocation is.
    let ints (n: int) : int[] =
        if on then newInts n else Array.zeroCreate n

    /// `n` zeroed floats: shared when allocation is.
    let floats (n: int) : float[] =
        if on then newFloats n else Array.zeroCreate n

    /// A mask of `n` rows, every one `false`: shared when allocation is.
    let mask (n: int) : Mask = if on then newBytes n else Mask.zero n

    /// `n` zeroed bytes: shared when allocation is.
    let bytes (n: int) : byte[] =
        if on then newBytes n else Array.zeroCreate n

    /// `a` itself when allocation is not shared or `a` is over shared memory already, else a copy
    /// of it that is.
    let shareInts (a: int[]) : int[] =
        if not on || sharedBacked a then
            a
        else
            let s = newInts a.Length
            copyInto s a
            s

    /// `a` itself when allocation is not shared or `a` is over shared memory already, else a copy
    /// of it that is.
    let shareFloats (a: float[]) : float[] =
        if not on || sharedBacked a then
            a
        else
            let s = newFloats a.Length
            copyInto s a
            s

    /// `m` itself when allocation is not shared or `m` is over shared memory already, else a copy
    /// of it that is.
    let shareMask (m: Mask) : Mask =
        if not on || sharedBacked m then
            m
        else
            let s = newBytes m.Length
            copyInto s m
            s

    // A typed vector's carrier and its mask in ONE buffer, the carrier first: half the buffers, each
    // an allocation the engine tracks outside its heap and sweeps when it collects. Measured as a
    // small gain in the evaluator's steady state, none in a single step
    // (`benchmarks/results/2026-10-06-i7-9700-phase-376.md`).

    [<Fable.Core.Emit("((n, s) => { const b = s ? new SharedArrayBuffer(n * 5) : new ArrayBuffer(n * 5); return [new Int32Array(b, 0, n), new Uint8Array(b, n * 4, n)]; })($0, $1)")>]
    let private newIntsAndMask (n: int) (shared: bool) : struct (int[] * Mask) = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("((n, s) => { const b = s ? new SharedArrayBuffer(n * 9) : new ArrayBuffer(n * 9); return [new Float64Array(b, 0, n), new Uint8Array(b, n * 8, n)]; })($0, $1)")>]
    let private newFloatsAndMask (n: int) (shared: bool) : struct (float[] * Mask) = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("((n, s) => { const b = s ? new SharedArrayBuffer(n * 2) : new ArrayBuffer(n * 2); return [new Uint8Array(b, 0, n), new Uint8Array(b, n, n)]; })($0, $1)")>]
    let private newBoolsAndMask (n: int) (shared: bool) : struct (Mask * Mask) = Fable.Core.Util.jsNative

    /// `n` zeroed ints and a mask of `n` absent rows, in one buffer: shared when allocation is.
    let intsAndMask (n: int) : struct (int[] * Mask) = newIntsAndMask n on

    /// `n` zeroed floats and a mask of `n` absent rows, in one buffer: shared when allocation is.
    let floatsAndMask (n: int) : struct (float[] * Mask) = newFloatsAndMask n on

    /// `n` false values and a mask of `n` absent rows, in one buffer: shared when allocation is.
    let boolsAndMask (n: int) : struct (Mask * Mask) = newBoolsAndMask n on
#else
    /// Every .NET array is visible to every thread.
    let available () : bool = true

    let mutable private on = false

    /// Recorded, and nothing else changes: every .NET array is already shared.
    let enable () : bool =
        on <- true
        true

    /// Recorded.
    let disable () : unit = on <- false

    /// Has a host opted in?
    let isOn () : bool = on

    /// Every .NET array is shared.
    let inline isShared (_: 'T[]) : bool = true

    /// `n` zeroed ints.
    let inline ints (n: int) : int[] = Array.zeroCreate n

    /// `n` zeroed floats.
    let inline floats (n: int) : float[] = Array.zeroCreate n

    /// A mask of `n` rows, every one `false`.
    let inline mask (n: int) : Mask = Array.zeroCreate n

    /// `n` zeroed bytes.
    let inline bytes (n: int) : byte[] = Array.zeroCreate n

    /// The array itself.
    let inline shareInts (a: int[]) : int[] = a

    /// The array itself.
    let inline shareFloats (a: float[]) : float[] = a

    /// The mask itself.
    let inline shareMask (m: Mask) : Mask = m

    /// `n` zeroed ints and a mask of `n` absent rows.
    let inline intsAndMask (n: int) : struct (int[] * Mask) =
        struct (Array.zeroCreate n, Array.zeroCreate n)

    /// `n` zeroed floats and a mask of `n` absent rows.
    let inline floatsAndMask (n: int) : struct (float[] * Mask) =
        struct (Array.zeroCreate n, Array.zeroCreate n)

    /// `n` false values and a mask of `n` absent rows.
    let inline boolsAndMask (n: int) : struct (Mask * Mask) =
        struct (Array.zeroCreate n, Array.zeroCreate n)
#endif

/// One column's cells as a dense vector: a typed carrier beside a validity mask (`true` = present)
/// where every present cell agrees with the column's declared type, or the boxed cells where one
/// does not. The three string-carrying families share one carrier and are told apart by the type
/// the vector records, so the cell it reads back is the cell that was unpacked. Masks and a
/// `Bools` vector's values are `Mask`s: a `bool[]` on .NET, bytes under Fable (Phase 376).
///
/// A decimal column (Phase 280) is `Decs`: the column's cells as they were handed in, beside each
/// present value as an unscaled integer at ONE scale for the column, carried in a float64 — so a
/// comparison, an order and a sum are float operations, exact because every value and every partial
/// sum the kernels keep is an integer of magnitude at most 2^53 - 1. The cells ride along so every
/// read of a cell is the cell the text path reads, byte for byte, with nothing rendered; only the
/// kernels read the integers. A column that does not fit (`ScaledDecimal.scaleOf`) stays `Cells`.
type internal Vec =
    | Ints of int[] * Mask
    | Floats of float[] * Mask
    | Bools of Mask * Mask
    | Strs of ColumnType * string[] * Mask
    | Decs of scaled: float[] * scale: int * cells: Cell[] * mask: Mask
    | Cells of Cell[]

/// The scaled-integer reading of decimal text the `Decs` vector carries (Phase 280). The grammar
/// is `DecimalText`'s, `-?[0-9]+(\.[0-9]+)?`; leading integer zeros and trailing fraction zeros
/// carry no digit, so `"012.50"` reads as 125 at scale 1.
module internal ScaledDecimal =

    /// The width: a column is carried scaled only when every value has at most this many
    /// significant digits at the column's scale, so every unscaled value is below 10^15 < 2^53
    /// and its float64 is exact. Past it — a scale past the width, or a value too large at the
    /// column's scale — the column takes the text path, by name, and nothing is refused.
    [<Literal>]
    let Width = 15

    /// The largest integer a float64 holds with every smaller one: a total or a sum the kernels
    /// keep in the float carrier stays at or below it in magnitude, or leaves the carrier.
    let maxExact: float = 9007199254740991.0

    /// 10^k as an exact float64, for k in 0 .. Width.
    let pow10: float[] = Array.init (Width + 1) (fun k -> pown 10.0 k)

    /// The shape of decimal text `s`: the number of significant integer digits (leading zeros
    /// dropped) and of fraction digits (trailing zeros dropped), or `None` where `s` is not in the
    /// grammar. Allocation-free.
    let shape (s: string) : struct (int * int) voption =
        let n = s.Length
        let start = if n > 0 && s[0] = '-' then 1 else 0
        let mutable i = start
        let mutable ok = true
        let mutable point = -1

        while ok && i < n do
            let c = s[i]

            if c = '.' && point < 0 && i > start && i < n - 1 then
                point <- i
            elif c < '0' || c > '9' then
                ok <- false

            i <- i + 1

        if not ok || n = start then
            ValueNone
        else
            let intEnd = if point < 0 then n else point
            let mutable lead = start

            while lead < intEnd && s[lead] = '0' do
                lead <- lead + 1

            let mutable fracEnd = n

            if point >= 0 then
                while fracEnd > point + 1 && s[fracEnd - 1] = '0' do
                    fracEnd <- fracEnd - 1

            let fracDigits = if point < 0 then 0 else fracEnd - point - 1
            ValueSome(struct (intEnd - lead, fracDigits))

    /// The unscaled value of decimal text `s` at `scale`, or `None` where `s` is not in the grammar,
    /// has more fraction digits than `scale`, or does not fit the width at `scale`.
    let tryScaled (scale: int) (s: string) : float voption =
        match shape s with
        | ValueSome(struct (intDigits, fracDigits)) when
            scale >= 0
            && scale <= Width
            && fracDigits <= scale
            && intDigits + scale <= Width
            ->
            let n = s.Length
            let negative = s[0] = '-'
            let mutable u = 0.0
            let mutable seen = 0
            let mutable i = if negative then 1 else 0
            let mutable fraction = false

            // Every digit up to the last significant fraction digit, as one integer; the integer
            // part's leading zeros add nothing and the fraction's trailing zeros are never reached.
            while i < n && (not fraction || seen < fracDigits) do
                let c = s[i]

                if c = '.' then
                    fraction <- true
                else
                    u <- u * 10.0 + float (int c - int '0')

                    if fraction then
                        seen <- seen + 1

                i <- i + 1

            let v = u * pow10[scale - fracDigits]
            ValueSome(if negative && v <> 0.0 then -v else v)
        | _ -> ValueNone

    /// The one scale a column of `cells` is carried at — its largest fraction-digit count — or
    /// `None` where it cannot be: a present cell that is not a well-formed `Decimal`, or a column
    /// past the width. `Null` cells are absent and read nothing.
    let scaleOf (cells: Cell[]) : int option =
        let mutable ok = true
        let mutable maxInt = 0
        let mutable maxFrac = 0
        let mutable i = 0

        while ok && i < cells.Length do
            match cells[i] with
            | Decimal s ->
                match shape s with
                | ValueSome(struct (intDigits, fracDigits)) ->
                    if intDigits > maxInt then
                        maxInt <- intDigits

                    if fracDigits > maxFrac then
                        maxFrac <- fracDigits
                | ValueNone -> ok <- false
            | Null -> ()
            | _ -> ok <- false

            i <- i + 1

        if ok && maxInt + maxFrac <= Width then
            Some maxFrac
        else
            None

    /// The canonical decimal text (`DecimalText`) of the unscaled value `u` at `scale`. `u` is an
    /// integer of magnitude at most `maxExact`.
    let render (scale: int) (u: float) : string =
        let digits = string (int64 (abs u))

        let padded =
            if digits.Length <= scale then
                String.replicate (scale + 1 - digits.Length) "0" + digits
            else
                digits

        let cut = padded.Length - scale

        let text =
            if scale = 0 then
                padded
            else
                padded.Substring(0, cut) + "." + padded.Substring cut

        let signed = if u < 0.0 then "-" + text else text
        DecimalText.tryCanonical signed |> Option.defaultValue signed

/// The cells a typed vector boxes on the way out, shared where they can be (Phase 327): the two
/// `Bool` cells, and one `Int` cell per value in `[Lo, Hi]`, allocated once. A shared cell is the
/// same union case with the same value as a fresh one, so every comparison the evaluator makes —
/// structural equality, `Cell.compare`, `cellToken`, `CellKey` — reads them alike; nothing in this
/// assembly compares a cell by reference, and interning only ever makes EQUAL cells identical.
module internal InternedCells =

    /// The lowest interned integer.
    [<Literal>]
    let Lo = -128

    /// The highest interned integer.
    [<Literal>]
    let Hi = 1023

    let private trueCell = Bool true
    let private falseCell = Bool false
    let private ints: Cell[] = Array.init (Hi - Lo + 1) (fun k -> Int(k + Lo))

    /// The `Bool` cell for `b` — one of two shared instances.
    let ofBool (b: bool) : Cell = if b then trueCell else falseCell

    /// The `Int` cell for `v` — shared inside `[Lo, Hi]`, fresh outside it.
    let ofInt (v: int) : Cell =
        if v >= Lo && v <= Hi then Raw.get (v - Lo) ints else Int v

/// The evaluator's frame: a schema, one vector per schema column (`Vecs` co-indexes with `Cols`),
/// and the selection — the PHYSICAL row of each LOGICAL row, in logical order — or `None` for the
/// identity, every physical row in physical order. `Count` is the physical row count: the length
/// of every vector, and the bound every selection entry is inside. Two frames may share a vector,
/// and a vector may be the very storage of a Core column (Phase 423: the boundary borrows in and
/// adopts out); no code in this assembly writes into an array it did not allocate itself.
type internal Frame =
    { Cols: Schema
      Vecs: Vec[]
      Sel: int[] option
      Count: int }

module internal Vec =

    /// The vector's length — the physical row count of any frame holding it.
    let length (v: Vec) : int =
        match v with
        | Ints(a, _) -> a.Length
        | Floats(a, _) -> a.Length
        | Bools(a, _) -> a.Length
        | Strs(_, a, _) -> a.Length
        | Decs(a, _, _, _) -> a.Length
        | Cells a -> a.Length

    /// The declared type a typed vector carries; `None` for the boxed fall-back, whose cells may
    /// disagree with the schema.
    let declaredType (v: Vec) : ColumnType option =
        match v with
        | Ints _ -> Some IntType
        | Floats _ -> Some FloatType
        | Bools _ -> Some BoolType
        | Strs(ty, _, _) -> Some ty
        | Decs _ -> Some DecimalType
        | Cells _ -> None

    /// The cell a string-family column of type `ty` holds for the carrier value `s`.
    let strCell (ty: ColumnType) (s: string) : Cell =
        match ty with
        | DateType -> Date s
        | TimestampType _ -> Timestamp s
        | StringType
        | IntType
        | FloatType
        | BoolType
        | DecimalType -> Str s

    /// The cell at physical row `p` — boxed on demand from the typed carrier (a `Bool`, and an `Int`
    /// in the interned range, read as the shared cell: `InternedCells`), or read as it is from the
    /// boxed one.
    let cellAt (v: Vec) (p: int) : Cell =
        match v with
        | Ints(a, m) ->
            if Mask.at p m then
                InternedCells.ofInt (Raw.at p a)
            else
                Null
        | Floats(a, m) -> if Mask.at p m then Float(Raw.at p a) else Null
        | Bools(a, m) ->
            if Mask.at p m then
                InternedCells.ofBool (Mask.at p a)
            else
                Null
        | Strs(ty, a, m) -> if Mask.at p m then strCell ty (Raw.at p a) else Null
        | Decs(_, _, cells, _) -> Raw.at p cells
        | Cells a -> Raw.at p a

    /// Is any selected row present?
    let anyPresent (mask: Mask) (phys: int[]) : bool =
        phys |> Array.exists (fun p -> Mask.at p mask)

    /// Pack `cells` — one per LOGICAL row, `cells[i]` landing at physical row `posOf i` — into a
    /// vector of `count` physical rows under the declared type `ty`. The typed unpack: one pass
    /// fills the typed carrier and its mask, and the first present cell that disagrees with `ty`
    /// abandons it for the boxed form. A physical row no logical row lands on is absent (its mask
    /// entry `false`, its boxed cell `Null`); a caller packing a dense column passes the identity.
    let packAt (ty: ColumnType) (count: int) (posOf: int -> int) (cells: Cell[]) : Vec =
        let n = cells.Length

        let boxed () =
            let out = Array.create count Null

            for i in 0 .. n - 1 do
                Raw.put out (posOf i) (Raw.get i cells)

            Cells out

        match ty with
        | IntType ->
            let struct (vals, mask) = Shared.intsAndMask count
            let mutable ok = true
            let mutable i = 0

            while ok && i < n do
                match Raw.get i cells with
                | Int v ->
                    let p = posOf i
                    Raw.put vals p v
                    Mask.put mask p true
                | Null -> ()
                | _ -> ok <- false

                i <- i + 1

            if ok then Ints(vals, mask) else boxed ()
        | FloatType ->
            let struct (vals, mask) = Shared.floatsAndMask count
            let mutable ok = true
            let mutable i = 0

            while ok && i < n do
                match Raw.get i cells with
                | Float v ->
                    let p = posOf i
                    Raw.put vals p v
                    Mask.put mask p true
                | Null -> ()
                | _ -> ok <- false

                i <- i + 1

            if ok then Floats(vals, mask) else boxed ()
        | BoolType ->
            let struct (vals, mask) = Shared.boolsAndMask count
            let mutable ok = true
            let mutable i = 0

            while ok && i < n do
                match Raw.get i cells with
                | Bool v ->
                    let p = posOf i
                    Mask.put vals p v
                    Mask.put mask p true
                | Null -> ()
                | _ -> ok <- false

                i <- i + 1

            if ok then Bools(vals, mask) else boxed ()
        // A decimal column (Phase 280): one scan finds the column's scale, then every present value
        // is read as an unscaled integer beside its cell. A column past the width, or holding any
        // present cell that is not a well-formed `Decimal`, takes the text path: packed boxed, every
        // kernel reading its cells through the reference arm.
        | DecimalType ->
            match ScaledDecimal.scaleOf cells with
            | Some scale ->
                let struct (vals, mask) = Shared.floatsAndMask count
                let out = Array.create count Null

                for i in 0 .. n - 1 do
                    match Raw.get i cells with
                    | Decimal s ->
                        let p = posOf i
                        // `scaleOf` admitted every present cell at this scale, so this always reads.
                        Raw.put vals p (ScaledDecimal.tryScaled scale s |> ValueOption.defaultValue 0.0)
                        Mask.put mask p true
                        Raw.put out p (Raw.get i cells)
                    | _ -> ()

                Decs(vals, scale, out, mask)
            | None -> boxed ()
        | StringType
        | DateType
        | TimestampType _ ->
            let vals: string[] = Array.zeroCreate count
            let mask = Shared.mask count
            let mutable ok = true
            let mutable i = 0

            while ok && i < n do
                let s =
                    match Raw.get i cells, ty with
                    | Str s, StringType
                    | Date s, DateType
                    | Timestamp s, TimestampType _ -> s
                    | _ -> null

                if not (isNull s) then
                    let p = posOf i
                    Raw.put vals p s
                    Mask.put mask p true
                else
                    match Raw.get i cells with
                    | Null -> ()
                    | _ -> ok <- false

                i <- i + 1

            if ok then Strs(ty, vals, mask) else boxed ()

    /// Pack a dense column: `cells[i]` at physical row `i`.
    let pack (ty: ColumnType) (cells: Cell[]) : Vec = packAt ty cells.Length id cells

    /// Hand the cells of `cells` to `put` with their rows, in one walk of the list; `false` where
    /// `put` refused a cell or the list is not exactly `n` long (a ragged column).
    let inline private walkExact (n: int) (cells: Cell list) ([<InlineIfLambda>] put: int -> Cell -> bool) : bool =
        let mutable rest = cells
        let mutable ok = true
        let mutable i = 0

        while ok && i < n do
            match rest with
            | c :: tail ->
                ok <- put i c
                rest <- tail
                i <- i + 1
            | [] -> ok <- false

        ok && List.isEmpty rest

    /// Pack a column's own list straight into the typed vector `ty` names (Phase 327): one walk of
    /// the list per column, no intermediate cell array. `ValueNone` — and the boundary takes its
    /// fall-back, `Frame.unpackFallback` — where the list is not exactly `n` long, or a present cell
    /// is not of `ty` (or, for a decimal, the column does not fit `ScaledDecimal`), or a string-family
    /// cell carries a null string: every case in which `pack` would not answer the typed vector this
    /// answers. Where it answers, the vector is the one `pack ty (List.toArray cells)` answers.
    let packList (ty: ColumnType) (n: int) (cells: Cell list) : Vec voption =
        match ty with
        | IntType ->
            let struct (vals, mask) = Shared.intsAndMask n

            let ok =
                walkExact n cells (fun i c ->
                    match c with
                    | Int v ->
                        Raw.set vals i v
                        Mask.set mask i true
                        true
                    | Null -> true
                    | _ -> false)

            if ok then ValueSome(Ints(vals, mask)) else ValueNone
        | FloatType ->
            let struct (vals, mask) = Shared.floatsAndMask n

            let ok =
                walkExact n cells (fun i c ->
                    match c with
                    | Float v ->
                        Raw.set vals i v
                        Mask.set mask i true
                        true
                    | Null -> true
                    | _ -> false)

            if ok then ValueSome(Floats(vals, mask)) else ValueNone
        | BoolType ->
            let struct (vals, mask) = Shared.boolsAndMask n

            let ok =
                walkExact n cells (fun i c ->
                    match c with
                    | Bool v ->
                        Mask.set vals i v
                        Mask.set mask i true
                        true
                    | Null -> true
                    | _ -> false)

            if ok then ValueSome(Bools(vals, mask)) else ValueNone
        // The cells ride in the vector (Phase 280), so the walk fills that array — the carrier, not
        // an intermediate — and the scale is read from it. Every slot is written before the array is
        // kept, so its initial fill (null on .NET, not on node) is never read.
        | DecimalType ->
            let out: Cell[] = Array.zeroCreate n

            if
                walkExact n cells (fun i c ->
                    Raw.set out i c
                    true)
            then
                match ScaledDecimal.scaleOf out with
                | Some scale ->
                    let struct (vals, mask) = Shared.floatsAndMask n

                    for i in 0 .. n - 1 do
                        match Raw.get i out with
                        | Decimal s ->
                            // `scaleOf` admitted every present cell at this scale, so this always reads.
                            Raw.set vals i (ScaledDecimal.tryScaled scale s |> ValueOption.defaultValue 0.0)
                            Mask.set mask i true
                        | _ -> ()

                    ValueSome(Decs(vals, scale, out, mask))
                | None -> ValueNone
            else
                ValueNone
        | StringType
        | DateType
        | TimestampType _ ->
            let vals: string[] = Array.zeroCreate n
            let mask = Shared.mask n

            let ok =
                walkExact n cells (fun i c ->
                    match c, ty with
                    | Str s, StringType
                    | Date s, DateType
                    | Timestamp s, TimestampType _ when not (isNull s) ->
                        Raw.set vals i s
                        Mask.set mask i true
                        true
                    | Null, _ -> true
                    | _ -> false)

            if ok then ValueSome(Strs(ty, vals, mask)) else ValueNone

#if FABLE_COMPILER
    /// The vector's cells at the physical rows `phys`, in that order, as a dense vector of the same
    /// kind — no cell is boxed on the typed path.
    ///
    /// Under JavaScript (Phase 326) one loop per carrier rather than `Array.init` over a closure:
    /// the loop proves `i` for `phys` and the output (both `n` long); the row it reads, `phys[i]`,
    /// is an index the loop does not prove, so it goes through the checked `Raw.at`.
    let gather (v: Vec) (phys: int[]) : Vec =
        let n = phys.Length

        let inline pick (a: 'T[]) (out: 'T[]) : 'T[] =
            for i in 0 .. n - 1 do
                let x = Raw.at (Raw.get i phys) a
                Raw.set out i x

            out

        match v with
        | Ints(a, m) ->
            let struct (va, vm) = Shared.intsAndMask n
            Ints(pick a va, pick m vm)
        | Floats(a, m) ->
            let struct (va, vm) = Shared.floatsAndMask n
            Floats(pick a va, pick m vm)
        | Bools(a, m) ->
            let struct (va, vm) = Shared.boolsAndMask n
            Bools(pick a va, pick m vm)
        | Strs(ty, a, m) -> Strs(ty, pick a (Array.zeroCreate n), pick m (Array.zeroCreate n))
        | Decs(a, s, c, m) ->
            let struct (va, vm) = Shared.floatsAndMask n
            Decs(pick a va, s, pick c (Array.zeroCreate n), pick m vm)
        | Cells a -> Cells(pick a (Array.zeroCreate n))

    /// `gather` where a negative index reads `Null` (Phase 325): the side a combining join pads.
    /// A padded slot of a typed vector is a cleared mask bit over a zero carrier (the empty string
    /// for a string vector, as a host that fills string arrays with it would hold anyway). Under
    /// JavaScript one loop per carrier, as `gather`.
    let gatherOrNull (v: Vec) (idx: int[]) : Vec =
        let n = idx.Length

        let inline pick (a: 'T[]) (pad: 'T) (out: 'T[]) : 'T[] =
            for i in 0 .. n - 1 do
                let p = Raw.get i idx
                let x = if p >= 0 then Raw.at p a else pad
                Raw.set out i x

            out

        let maskOf (m: Mask) = pick m 0uy (Array.zeroCreate n)

        match v with
        | Ints(a, m) -> Ints(pick a 0 (Array.zeroCreate n), maskOf m)
        | Floats(a, m) -> Floats(pick a 0.0 (Array.zeroCreate n), maskOf m)
        | Bools(a, m) -> Bools(pick a 0uy (Array.zeroCreate n), maskOf m)
        | Strs(ty, a, m) -> Strs(ty, pick a "" (Array.zeroCreate n), maskOf m)
        | Decs(a, s, c, m) -> Decs(pick a 0.0 (Array.zeroCreate n), s, pick c Null (Array.zeroCreate n), maskOf m)
        | Cells a -> Cells(pick a Null (Array.zeroCreate n))
#else
    /// The vector's cells at the physical rows `phys`, in that order, as a dense vector of the same
    /// kind — no cell is boxed on the typed path.
    let gather (v: Vec) (phys: int[]) : Vec =
        let n = phys.Length

        match v with
        | Ints(a, m) -> Ints(Array.init n (fun i -> a[phys[i]]), Array.init n (fun i -> m[phys[i]]))
        | Floats(a, m) -> Floats(Array.init n (fun i -> a[phys[i]]), Array.init n (fun i -> m[phys[i]]))
        | Bools(a, m) -> Bools(Array.init n (fun i -> a[phys[i]]), Array.init n (fun i -> m[phys[i]]))
        | Strs(ty, a, m) -> Strs(ty, Array.init n (fun i -> a[phys[i]]), Array.init n (fun i -> m[phys[i]]))
        | Decs(a, s, c, m) ->
            Decs(
                Array.init n (fun i -> a[phys[i]]),
                s,
                Array.init n (fun i -> c[phys[i]]),
                Array.init n (fun i -> m[phys[i]])
            )
        | Cells a -> Cells(Array.init n (fun i -> a[phys[i]]))

    /// `gather` where a negative index reads `Null` (Phase 325): the side a combining join pads.
    /// A padded slot of a typed vector is a cleared mask bit over a zero carrier (the empty string
    /// for a string vector, as a host that fills string arrays with it would hold anyway).
    let gatherOrNull (v: Vec) (idx: int[]) : Vec =
        let n = idx.Length
        let present = Array.init n (fun i -> idx[i] >= 0)

        let maskOf (m: bool[]) =
            Array.init n (fun i -> present[i] && m[idx[i]])

        match v with
        | Ints(a, m) -> Ints(Array.init n (fun i -> if present[i] then a[idx[i]] else 0), maskOf m)
        | Floats(a, m) -> Floats(Array.init n (fun i -> if present[i] then a[idx[i]] else 0.0), maskOf m)
        | Bools(a, m) -> Bools(Array.init n (fun i -> present[i] && a[idx[i]]), maskOf m)
        | Strs(ty, a, m) -> Strs(ty, Array.init n (fun i -> if present[i] then a[idx[i]] else ""), maskOf m)
        | Decs(a, s, c, m) ->
            Decs(
                Array.init n (fun i -> if present[i] then a[idx[i]] else 0.0),
                s,
                Array.init n (fun i -> if present[i] then c[idx[i]] else Null),
                maskOf m
            )
        | Cells a -> Cells(Array.init n (fun i -> if present[i] then a[idx[i]] else Null))
#endif

    /// Two DENSE vectors end to end: the same kind when both are typed alike, boxed otherwise. Two
    /// decimal vectors at different scales are packed again from their cells, so the result is the
    /// vector `pack DecimalType` makes of the joined column.
    let append (a: Vec) (b: Vec) : Vec =
        match a, b with
        | Ints(x, mx), Ints(y, my) -> Ints(Array.append x y, Array.append mx my)
        | Floats(x, mx), Floats(y, my) -> Floats(Array.append x y, Array.append mx my)
        | Bools(x, mx), Bools(y, my) -> Bools(Array.append x y, Array.append mx my)
        | Strs(ta, x, mx), Strs(tb, y, my) when ta = tb -> Strs(ta, Array.append x y, Array.append mx my)
        | Decs(x, sa, cx, mx), Decs(y, sb, cy, my) when sa = sb ->
            Decs(Array.append x y, sa, Array.append cx cy, Array.append mx my)
        | Decs(_, _, cx, _), Decs(_, _, cy, _) -> pack DecimalType (Array.append cx cy)
        | _ ->
            let na = length a
            let nb = length b
            Cells(Array.init (na + nb) (fun i -> if i < na then cellAt a i else cellAt b (i - na)))

    // ---- Phase 268 — what a chunk needs of a vector ----

    /// The dense slice `[start, start + len)` of a vector, as a fresh vector of the same kind.
    let slice (v: Vec) (start: int) (len: int) : Vec =
        match v with
        | Ints(a, m) -> Ints(Array.sub a start len, Array.sub m start len)
        | Floats(a, m) -> Floats(Array.sub a start len, Array.sub m start len)
        | Bools(a, m) -> Bools(Array.sub a start len, Array.sub m start len)
        | Strs(ty, a, m) -> Strs(ty, Array.sub a start len, Array.sub m start len)
        | Decs(a, s, c, m) -> Decs(Array.sub a start len, s, Array.sub c start len, Array.sub m start len)
        | Cells a -> Cells(Array.sub a start len)

    /// Dense vectors end to end, `ty` deciding the empty case: one vector of the shared kind where
    /// every piece is typed alike, boxed otherwise. A single piece is handed back as it is.
    let concat (ty: ColumnType) (vs: Vec[]) : Vec =
        let boxed () =
            Cells(vs |> Array.collect (fun v -> Array.init (length v) (cellAt v)))

        match vs with
        | [||] -> pack ty [||]
        | [| v |] -> v
        | _ ->
            match vs[0] with
            | Ints _ ->
                let parts =
                    vs
                    |> Array.choose (function
                        | Ints(a, m) -> Some(a, m)
                        | _ -> None)

                if parts.Length = vs.Length then
                    Ints(Array.concat (Array.map fst parts), Array.concat (Array.map snd parts))
                else
                    boxed ()
            | Floats _ ->
                let parts =
                    vs
                    |> Array.choose (function
                        | Floats(a, m) -> Some(a, m)
                        | _ -> None)

                if parts.Length = vs.Length then
                    Floats(Array.concat (Array.map fst parts), Array.concat (Array.map snd parts))
                else
                    boxed ()
            | Bools _ ->
                let parts =
                    vs
                    |> Array.choose (function
                        | Bools(a, m) -> Some(a, m)
                        | _ -> None)

                if parts.Length = vs.Length then
                    Bools(Array.concat (Array.map fst parts), Array.concat (Array.map snd parts))
                else
                    boxed ()
            | Strs(t0, _, _) ->
                let parts =
                    vs
                    |> Array.choose (function
                        | Strs(t, a, m) when t = t0 -> Some(a, m)
                        | _ -> None)

                if parts.Length = vs.Length then
                    Strs(t0, Array.concat (Array.map fst parts), Array.concat (Array.map snd parts))
                else
                    boxed ()
            | Decs(_, s0, _, _) ->
                let parts =
                    vs
                    |> Array.choose (function
                        | Decs(a, s, c, m) -> Some(a, s, c, m)
                        | _ -> None)

                if parts.Length < vs.Length then
                    boxed ()
                elif parts |> Array.forall (fun (_, s, _, _) -> s = s0) then
                    Decs(
                        Array.concat (parts |> Array.map (fun (a, _, _, _) -> a)),
                        s0,
                        Array.concat (parts |> Array.map (fun (_, _, c, _) -> c)),
                        Array.concat (parts |> Array.map (fun (_, _, _, m) -> m))
                    )
                else
                    pack DecimalType (Array.concat (parts |> Array.map (fun (_, _, c, _) -> c)))
            | Cells _ -> boxed ()

    /// The carrier string a string-family column of type `ty` holds for `c`, or `None` where `c`
    /// is not that family's cell.
    let private carrierOf (ty: ColumnType) (c: Cell) : string option =
        match c, ty with
        | Str s, StringType
        | Date s, DateType
        | Timestamp s, TimestampType _ -> Some s
        | _ -> None

    /// A copy of the dense vector with the cell at `i` replaced — never a write into `v`, whose
    /// arrays another version may hold. A typed vector stays typed for a cell of its type or
    /// `Null` (the value array is shared where only the mask moves), and is boxed for any other.
    let setAt (v: Vec) (i: int) (c: Cell) : Vec =
        let boxed () =
            let out = Array.init (length v) (cellAt v)
            out[i] <- c
            Cells out

        let masked (m: Mask) =
            let m' = Array.copy m
            Mask.put m' i false
            m'

        match v, c with
        | Ints(a, m), Int x ->
            let a' = Array.copy a
            let m' = Array.copy m
            a'[i] <- x
            Mask.put m' i true
            Ints(a', m')
        | Ints(a, m), Null -> Ints(a, masked m)
        | Floats(a, m), Float x ->
            let a' = Array.copy a
            let m' = Array.copy m
            a'[i] <- x
            Mask.put m' i true
            Floats(a', m')
        | Floats(a, m), Null -> Floats(a, masked m)
        | Bools(a, m), Bool x ->
            let a' = Array.copy a
            let m' = Array.copy m
            Mask.put a' i x
            Mask.put m' i true
            Bools(a', m')
        | Bools(a, m), Null -> Bools(a, masked m)
        // A decimal vector stays at its scale for a value that fits it; any other cell — a value
        // past the scale or the width, or a cell of another type — packs the edited column again,
        // which picks the scale that fits or takes the text path.
        | Decs(a, s, cells, m), _ ->
            let edited = Array.copy cells
            edited[i] <- c

            match c with
            | Null -> Decs(a, s, edited, masked m)
            | Decimal text ->
                match ScaledDecimal.tryScaled s text with
                | ValueSome x ->
                    let a' = Array.copy a
                    let m' = Array.copy m
                    a'[i] <- x
                    Mask.put m' i true
                    Decs(a', s, edited, m')
                | ValueNone -> pack DecimalType edited
            | _ -> pack DecimalType edited
        | Strs(ty, a, m), Null -> Strs(ty, a, masked m)
        | Strs(ty, a, m), _ ->
            match carrierOf ty c with
            | Some s ->
                let a' = Array.copy a
                let m' = Array.copy m
                a'[i] <- s
                Mask.put m' i true
                Strs(ty, a', m')
            | None -> boxed ()
        | Cells a, _ ->
            let out = Array.copy a
            out[i] <- c
            Cells out
        | _ -> boxed ()

    /// Does the dense vector hold exactly `cells[offset ..]`, cell for cell, over its whole length?
    /// Allocation-free on the typed path — the comparison a `SetColumn` makes per chunk to keep the
    /// chunks it did not change. A float is compared as `=` compares it, so a `NaN` never agrees
    /// with itself, which is what `Cell` equality says too.
    let sameCells (v: Vec) (cells: Cell[]) (offset: int) : bool =
        let n = length v

        if offset + n > cells.Length then
            false
        else
            let mutable ok = true
            let mutable j = 0

            while ok && j < n do
                ok <-
                    match v with
                    | Ints(a, m) ->
                        match cells[offset + j] with
                        | Int x -> Mask.at j m && a[j] = x
                        | Null -> not (Mask.at j m)
                        | _ -> false
                    | Floats(a, m) ->
                        match cells[offset + j] with
                        | Float x -> Mask.at j m && a[j] = x
                        | Null -> not (Mask.at j m)
                        | _ -> false
                    | Bools(a, m) ->
                        match cells[offset + j] with
                        | Bool x -> Mask.at j m && Mask.at j a = x
                        | Null -> not (Mask.at j m)
                        | _ -> false
                    | Strs(ty, a, m) ->
                        match cells[offset + j] with
                        | Null -> not (Mask.at j m)
                        | c ->
                            match carrierOf ty c with
                            | Some s -> Mask.at j m && a[j] = s
                            | None -> false
                    | Decs(_, _, a, _)
                    | Cells a -> a[j] = cells[offset + j]

                j <- j + 1

            ok

    // ---- The `Table` boundary (Phase 423): a view over Core's vectors, in and out ----

    /// The backing array of `v` where the vector occupies the whole of it, else a copy of its range:
    /// the one place this assembly reads Core storage, under Phase 418's contract that a borrower
    /// never writes — and nothing in this assembly writes into an array it did not allocate.
    let private borrowed (v: Vector<'T>) : 'T[] =
        let b = Vector.Unsafe.borrow v

        if b.Offset = 0 && b.Length = b.Array.Length then
            b.Array
        else
            Vector.toArray v

    /// A column's validity as the frame's mask of `n` rows. An `AllValid` column holds no mask, so
    /// one is built with every row present; a `Mask` is borrowed where it is `n` long (on .NET the
    /// very array), and read into one of `n` rows otherwise — a row past a shorter mask is absent,
    /// as Core reads it.
    let private maskOf (n: int) (validity: Validity) : Mask =
        match validity with
        | AllValid -> Mask.create n true
        | Validity.Mask present ->
            let a = borrowed present

            if a.Length = n then
                Mask.ofBools a
            else
                Mask.init n (fun i -> i < a.Length && Raw.get i a)

    /// The texts of a temporal column, rendered per present row from its integers; an absent row
    /// holds no text. The frame still carries a date or an instant as its canonical text, so this is
    /// the one conversion the boundary in still makes (recorded; the typed temporal vector is the
    /// follow-on the phase names).
    let private renderTexts (n: int) (mask: Mask) (render: int -> string) : string[] =
        let out: string[] = Array.zeroCreate n

        for i in 0 .. n - 1 do
            if Mask.get i mask then
                Raw.set out i (render i)

        out

    /// A Core column as a vector of `n` physical rows under the declared type `ty`. A column of that
    /// type and that length is a VIEW: its values and its mask are borrowed, never copied (a `Bools`
    /// column's values and every mask under Fable are read into the `Uint8Array` form the kernels
    /// index). A column of another type, another length or none at all takes the fall-back the
    /// per-index reads always answered: its cells padded with `Null` or cut to `n`, then packed.
    let ofColumn (n: int) (ty: ColumnType) (column: Column option) : Vec =
        let fallback (cells: Cell list) : Vec =
            let a = List.toArray cells

            if a.Length = n then
                pack ty a
            else
                pack ty (Array.init n (fun i -> if i < a.Length then Raw.get i a else Null))

        match column with
        | None -> pack ty (Array.create n Null)
        | Some c when c.Type <> ty || Column.length c <> n -> fallback (Column.toCells c)
        | Some c ->
            match c.Data with
            | ColumnData.Ints(v, va) -> Ints(borrowed v, maskOf n va)
            | ColumnData.Floats(v, va) -> Floats(borrowed v, maskOf n va)
            | ColumnData.Bools(v, va) -> Bools(Mask.ofBools (borrowed v), maskOf n va)
            | ColumnData.Strs(v, va) -> Strs(StringType, borrowed v, maskOf n va)
            | ColumnData.Dates(v, va) ->
                let days = borrowed v
                let mask = maskOf n va
                Strs(DateType, renderTexts n mask (fun i -> TemporalText.dateText (Raw.get i days)), mask)
            | ColumnData.Timestamps(unit, seconds, fraction, va) ->
                let secs = borrowed seconds
                let mask = maskOf n va

                let fractionAt: int -> int =
                    match fraction with
                    | Some f ->
                        let fa = borrowed f
                        fun i -> if i < fa.Length then Raw.get i fa else 0
                    | None -> fun _ -> 0

                Strs(
                    ty,
                    renderTexts n mask (fun i -> TemporalText.instantText unit (Raw.get i secs) (fractionAt i)),
                    mask
                )
            | ColumnData.Decimals(v, va) ->
                let texts = borrowed v
                let mask = maskOf n va
                pack DecimalType (Array.init n (fun i -> if Mask.get i mask then Decimal(Raw.get i texts) else Null))

    /// The frame's mask as a column's validity: `AllValid` where every row is present, else the
    /// mask itself — on .NET the very array, adopted; `Validity.ofVector` normalises.
    let private validityOf (m: Mask) : Validity =
        Validity.ofVector (Vector.adopt (Mask.toBools m))

    /// The refusal of a column Core's typed storage cannot hold. A frame holds such a column only
    /// where it was handed one — a table `Table.validate` refuses — so the evaluator cannot answer
    /// it as a `Table`.
    let private unrepresentable (name: string) (detail: string) : 'a =
        invalidOp ("column '" + name + "' cannot be held by its type: " + detail)

    /// The one type every present cell of `cells` widens into, if there is one: the type a boxed
    /// column's cells agree on where they disagree with the schema's.
    let private commonType (cells: Cell[]) : ColumnType option =
        let mutable acc: ColumnType option = None
        let mutable ok = true
        let mutable i = 0

        while ok && i < cells.Length do
            match Cell.typeOf cells[i] with
            | None -> ()
            | Some t ->
                match acc with
                | None -> acc <- Some t
                | Some a when a = t || ColumnType.widens t a -> ()
                | Some a when ColumnType.widens a t -> acc <- Some t
                | Some _ -> ok <- false

            i <- i + 1

        if ok then acc else None

    /// The dense vector as a Core column under `field`, its storage ADOPTED, never copied and never
    /// boxed: the array the frame allocated becomes the column's vector (a `Bools` vector's values
    /// and every mask under Fable are read out of the `Uint8Array` form first). A boxed vector goes
    /// through `Column.ofCells`, which is the one path that still walks cells: under the field's
    /// type where the cells fit it, else under the one type they agree on — a column that
    /// disagreed with its schema entry on the way in disagrees with it the same way on the way out.
    let toColumn (field: Field) (v: Vec) : Column =
        let name = field.Name

        let viaCells (cells: Cell[]) : Column =
            match Column.ofCells name field.Type (List.ofArray cells) with
            | Ok c -> c
            | Error e ->
                match commonType cells with
                | Some t when t <> field.Type ->
                    match Column.ofCells name t (List.ofArray cells) with
                    | Ok c -> c
                    | Error e -> unrepresentable name (sprintf "%A" e)
                | _ -> unrepresentable name (sprintf "%A" e)

        match v with
        | Ints(a, m) -> Column.ofInts name (Vector.adopt a) (validityOf m)
        | Floats(a, m) -> Column.ofFloats name (Vector.adopt a) (validityOf m)
        | Bools(a, m) -> Column.ofBools name (Vector.adopt (Mask.toBools a)) (validityOf m)
        | Strs(StringType, a, m) -> Column.ofStrs name (Vector.adopt a) (validityOf m)
        | Strs(DateType, a, m) ->
            let n = a.Length
            let days: int[] = Array.zeroCreate n
            let mutable bad: string = null

            for i in 0 .. n - 1 do
                if isNull bad && Mask.get i m then
                    match TemporalText.tryDays (Raw.get i a) with
                    | Some d -> Raw.set days i d
                    | None -> bad <- Raw.get i a

            if isNull bad then
                Column.ofDates name (Vector.adopt days) (validityOf m)
            else
                unrepresentable name ("'" + bad + "' is not a canonical date")
        | Strs(TimestampType unit, a, m) ->
            let n = a.Length
            let seconds: float[] = Array.zeroCreate n
            let fraction: int[] = Array.zeroCreate n
            let mutable bad: string = null

            for i in 0 .. n - 1 do
                if isNull bad && Mask.get i m then
                    match TemporalText.tryInstant unit (Raw.get i a) with
                    | Some(s, f) ->
                        Raw.set seconds i s
                        Raw.set fraction i f
                    | None -> bad <- Raw.get i a

            if isNull bad then
                let fraction =
                    if unit = TimeUnit.Seconds then
                        None
                    else
                        Some(Vector.adopt fraction)

                Column.ofTimestamps name unit (Vector.adopt seconds) fraction (validityOf m)
            else
                unrepresentable name ("'" + bad + "' is not a canonical instant of its unit")
        | Strs(_, a, m) -> viaCells (Array.init a.Length (fun i -> if Mask.get i m then Str(Raw.get i a) else Null))
        | Decs(_, _, cells, m) ->
            let texts =
                cells
                |> Array.map (fun c ->
                    match c with
                    | Decimal s -> s
                    | _ -> null)

            Column.ofDecimals name (Vector.adopt texts) (validityOf m)
        | Cells cells -> viaCells cells

    /// A column built from cells under `field` — the row form's boundary out: packed, then adopted.
    let columnOfCells (field: Field) (cells: Cell list) : Column =
        toColumn field (pack field.Type (List.toArray cells))

module internal Frame =

    /// The logical row count — the rows a `Table` of this frame would have.
    let rows (f: Frame) : int =
        match f.Sel with
        | Some s -> s.Length
        | None -> f.Count

    /// The physical row of every logical row, in logical order — the selection, or the identity
    /// materialised.
    ///
    /// The identity is filled by a counted loop (Phase 353): under Fable `Array.init` writes each
    /// entry through the runtime's shared bounds-checked `setItem`, and every verb over a frame with
    /// no selection asks for this array — it was 31 per cent of the node `lines` sheet at 100,000
    /// rows, called once per derive and once more at the boundary out. The loop proves `i`.
    let physical (f: Frame) : int[] =
        match f.Sel with
        | Some s -> s
        | None ->
            let n = f.Count
            let rows = Shared.ints n

            for i in 0 .. n - 1 do
                Raw.set rows i i

            rows

    /// The frame holding the physical rows `sel`, in that order, over the same vectors.
    let select (f: Frame) (sel: int[]) : Frame = { f with Sel = Some sel }

    /// Well-formedness: one vector per schema column, every vector `Count` long, and every
    /// selection entry naming a physical row. The invariant every verb preserves; a law in the
    /// suite holds it after every step of every generated pipeline.
    let wellFormed (f: Frame) : bool =
        f.Vecs.Length = List.length f.Cols
        && f.Vecs |> Array.forall (fun v -> Vec.length v = f.Count)
        && (match f.Sel with
            | None -> true
            | Some s -> s |> Array.forall (fun p -> p >= 0 && p < f.Count))

    /// A table as a frame (Phase 423): one VIEW per schema column over the column's own storage —
    /// `Vec.ofColumn` borrows the values and the mask, and copies nothing — under the identity
    /// selection. A column the schema names and the table lacks, or holds at another type or
    /// length, is packed from its cells as the per-index reads always answered it.
    let ofTable (t: Table) : Frame =
        let n = Table.rowCount t

        { Cols = t.Schema
          Vecs =
            t.Schema
            |> List.map (fun field -> Vec.ofColumn n field.Type (Table.tryColumn field.Name t))
            |> List.toArray
          Sel = None
          Count = n }

    /// The frame as a table (Phase 423): one column per schema field, its storage ADOPTED from the
    /// frame's vector — the very arrays where the selection is the identity, else one typed gather
    /// per column through it. No cell is boxed on the typed path.
    let toTable (f: Frame) : Table =
        let dense (ci: int) : Vec =
            match f.Sel with
            | None -> f.Vecs[ci]
            | Some s -> Vec.gather f.Vecs[ci] s

        { Schema = f.Cols
          Columns = f.Cols |> List.mapi (fun ci field -> Vec.toColumn field (dense ci)) }

    /// Every logical row, in logical order, as an array of the schema's width — the gather the
    /// row-oriented verbs read through. The arrays are fresh; a cell is boxed per read on the
    /// typed path.
    let rowsOf (f: Frame) : Cell[][] =
        let phys = physical f
        let w = f.Vecs.Length

#if FABLE_COMPILER
        // Under JavaScript (Phase 326) counted loops rather than `Array.init` over closures; the loops
        // prove `i` for `phys` and the rows, and `ci` for `Vecs` and each row (all `w` wide).
        let n = phys.Length
        let rows: Cell[][] = Array.zeroCreate n

        for i in 0 .. n - 1 do
            let p = Raw.get i phys
            let row: Cell[] = Array.zeroCreate w

            for ci in 0 .. w - 1 do
                let c = Vec.cellAt (Raw.get ci f.Vecs) p
                Raw.set row ci c

            Raw.set rows i row

        rows
#else
        Array.init phys.Length (fun i ->
            let p = phys[i]
            Array.init w (fun ci -> Vec.cellAt f.Vecs[ci] p))
#endif

    /// A frame from full-width rows under `cols`: one typed pack per column, the identity
    /// selection. The inverse of `rowsOf` — what a verb that emitted fresh rows hands back.
    let ofRows (cols: Schema) (rows: Cell[][]) : Frame =
        let n = rows.Length

        { Cols = cols
          Vecs =
            cols
            |> List.mapi (fun ci field -> Vec.pack field.Type (Array.init n (fun r -> rows[r][ci])))
            |> List.toArray
          Sel = None
          Count = n }

    /// The schema columns at `idx`, renamed per `cols`, sharing their vectors — a projection copies
    /// no cell, and a column projected twice is one vector held twice.
    let project (f: Frame) (idx: int[]) (cols: Schema) : Frame =
        { f with
            Cols = cols
            Vecs = idx |> Array.map (fun i -> f.Vecs[i]) }

    /// The frame with `name` upserted as `v` of type `ty`: replaced in place where the schema
    /// carries the name, appended otherwise. Every other vector is shared. A column a verb produced
    /// is a fresh field — its name and type, no metadata: the verb states nothing about a unit.
    let withColumn (f: Frame) (name: string) (ty: ColumnType) (v: Vec) : Frame =
        match f.Cols |> List.tryFindIndex (fun field -> field.Name = name) with
        | Some i ->
            let vecs = Array.copy f.Vecs
            vecs[i] <- v

            { f with
                Cols =
                    f.Cols
                    |> List.mapi (fun j field -> if j = i then Field.create name ty else field)
                Vecs = vecs }
        | None ->
            { f with
                Cols = f.Cols @ [ Field.create name ty ]
                Vecs = Array.append f.Vecs [| v |] }

    /// The frame with `name` APPENDED as `v` of type `ty`, whether or not the schema already
    /// carries the name — the shape `Window` produces.
    let appendColumn (f: Frame) (name: string) (ty: ColumnType) (v: Vec) : Frame =
        { f with
            Cols = f.Cols @ [ Field.create name ty ]
            Vecs = Array.append f.Vecs [| v |] }

    /// This frame's rows followed by `other`'s, under this frame's schema, column by column:
    /// each pair of vectors gathered dense through its selection and appended, typed where both
    /// are typed alike.
    let concat (f: Frame) (other: Frame) : Frame =
        let pa = physical f
        let pb = physical other

        { Cols = f.Cols
          Vecs =
            Array.init f.Vecs.Length (fun ci -> Vec.append (Vec.gather f.Vecs[ci] pa) (Vec.gather other.Vecs[ci] pb))
          Sel = None
          Count = pa.Length + pb.Length }

// ============================================================================
//  Phase 268 — persistent chunked columns.
//
//  A column as a rope of dense chunks, each a `Vec` of at most `Chunked.rows`
//  rows, every chunk but the last exactly that long. Nothing writes into a
//  chunk: an edit copies the one chunk it lands in and shares every other by
//  reference, so successive versions of a source share structure, every prior
//  version stays reachable, and "what moved" between two versions is one
//  pointer comparison per chunk. The Phase 267 vectors are a VIEW over the
//  rope — `Chunked.toVec` concatenates it — taken lazily, so an edit costs a
//  chunk and a full evaluation pays the concatenation once per version.
// ============================================================================

/// One column as a persistent rope of dense chunks under its declared type. `Chunks[k]` holds
/// physical rows `[k * Size, (k + 1) * Size)`; `Length` is the row count; every chunk but the
/// last is `Size` long and none is empty.
type internal Chunked =
    { Type: ColumnType
      Size: int
      Length: int
      Chunks: Vec[] }

module internal Chunked =

    /// Rows per chunk: the smallest of the range the design names (1,024 to 4,096), so an edit
    /// copies and a refresh re-evaluates the least it can, and a full pass over 100,000 rows still
    /// walks under a hundred chunks.
    [<Literal>]
    let rows = 1024

    /// How many chunks `n` rows take at `size` rows a chunk.
    let count (size: int) (n: int) : int = (n + size - 1) / size

    /// The length of chunk `k` of `n` rows at `size` a chunk.
    let lengthOf (size: int) (n: int) (k: int) : int = min size (n - k * size)

    /// A dense vector cut into chunks of `size` rows under `ty`.
    let ofVec (ty: ColumnType) (size: int) (v: Vec) : Chunked =
        let n = Vec.length v

        { Type = ty
          Size = size
          Length = n
          Chunks = Array.init (count size n) (fun k -> Vec.slice v (k * size) (lengthOf size n k)) }

    /// Cells cut into chunks of `size` rows, each packed under `ty`.
    let ofCells (ty: ColumnType) (size: int) (cells: Cell[]) : Chunked =
        let n = cells.Length

        { Type = ty
          Size = size
          Length = n
          Chunks = Array.init (count size n) (fun k -> Vec.pack ty (Array.sub cells (k * size) (lengthOf size n k))) }

    /// The rope as one dense vector — the Phase 267 view. One chunk is handed back as it is.
    let toVec (c: Chunked) : Vec = Vec.concat c.Type c.Chunks

    /// The cell at physical row `i`.
    let cellAt (c: Chunked) (i: int) : Cell =
        Vec.cellAt c.Chunks[i / c.Size] (i % c.Size)

    /// The rope with the cell at `i` replaced: one chunk copied, every other shared.
    let setCell (c: Chunked) (i: int) (cell: Cell) : Chunked =
        let k = i / c.Size
        let chunks = Array.copy c.Chunks
        chunks[k] <- Vec.setAt c.Chunks[k] (i % c.Size) cell
        { c with Chunks = chunks }

    /// `cells` as a rope of `prior`'s shape, KEEPING every chunk of `prior` whose cells it holds
    /// unchanged — a `SetColumn` that moved a few cells shares every chunk it did not move, so a
    /// refresh recognises them. Cells are compared, never allocated, on the typed path; a chunk is
    /// shared only under the same declared type. A different length is packed afresh.
    let ofCellsSharing (prior: Chunked) (ty: ColumnType) (cells: Cell[]) : Chunked =
        let n = cells.Length

        if n <> prior.Length then
            ofCells ty prior.Size cells
        else
            { Type = ty
              Size = prior.Size
              Length = n
              Chunks =
                Array.init (count prior.Size n) (fun k ->
                    let off = k * prior.Size

                    if ty = prior.Type && Vec.sameCells prior.Chunks[k] cells off then
                        prior.Chunks[k]
                    else
                        Vec.pack ty (Array.sub cells off (lengthOf prior.Size n k))) }

    /// The rope with `cells` appended: every full chunk shared, the partial last chunk copied and
    /// extended, the rest packed fresh — O(chunk + appended), never a pass over the rope.
    let append (c: Chunked) (cells: Cell[]) : Chunked =
        if cells.Length = 0 then
            c
        else
            let size = c.Size
            let total = c.Length + cells.Length
            let full = c.Length / size
            let partial = c.Length - full * size

            { c with
                Length = total
                Chunks =
                    Array.init (count size total) (fun k ->
                        if k < full then
                            c.Chunks[k]
                        elif k = full && partial > 0 then
                            let take = min (size - partial) cells.Length
                            Vec.append c.Chunks[k] (Vec.pack c.Type (Array.sub cells 0 take))
                        else
                            let off = k * size - c.Length
                            Vec.pack c.Type (Array.sub cells off (min size (total - k * size)))) }

    /// The rope as a Core column under `field` (Phase 423): one dense vector — one chunk handed
    /// back as it is, several concatenated once — adopted as the column's storage.
    let toColumn (field: Field) (c: Chunked) : Column = Vec.toColumn field (toVec c)

/// A source prepared once for many evaluations (Phase 267), and since Phase 268 a persistent
/// VERSION of a table: its schema and row count, and one chunked column per schema column (the
/// rope every edit shares), with the table it stands for and the evaluator's dense frame of it
/// each derived on demand and kept. Prepared from a table, the table and the frame are what it was
/// made from and the rope is cut from the frame the first time an edit or a chunk-aware refresh
/// asks; produced by an edit (`ColumnOps.applyPrepared`) or a chunked refresh, the rope is primary
/// and the other two are concatenated from it the first time something reads them. Opaque: nothing
/// is readable from one but through `DataFrame.toTable`, `DataFrame.evalPrepared`, the `ColumnOps`
/// forms over it and `Incremental.primePrepared` / `refreshPrepared`, so the working form stays
/// free to move behind it.
type Prepared =
    internal
        {
            /// The table this version stands for — the consumer's own object where it was prepared
            /// from one, else built from the rope on first read.
            Source: Lazy<Table>
            /// The evaluator's dense frame of it (Phase 267) — the vectors, as a view over the rope.
            Frame: Lazy<Frame>
            /// Its schema; the column order every array below follows.
            Cols: Schema
            /// Its row count.
            Count: int
            /// One rope per schema column.
            Columns: Lazy<Chunked[]>
        }

module internal Prepared =

    /// A lazy already holding `x`, so `IsValueCreated` reads true from the start.
    let ready (x: 'a) : Lazy<'a> =
        let l = lazy x
        l.Force() |> ignore
        l

    /// A table prepared: its frame a view over the table's own vectors (Phase 423: nothing is
    /// copied), its rope cut from the frame on first demand.
    let ofTable (t: Table) : Prepared =
        let f = Frame.ofTable t
        let types = t.Schema |> List.map _.Type |> List.toArray

        { Source = ready t
          Frame = ready f
          Cols = t.Schema
          Count = f.Count
          Columns = lazy (Array.mapi (fun ci v -> Chunked.ofVec types[ci] Chunked.rows v) f.Vecs) }

    /// A version from its rope: the frame and the table each concatenated from it on first read.
    let ofChunks (cols: Schema) (count: int) (columns: Chunked[]) : Prepared =
        let frame =
            lazy
                { Cols = cols
                  Vecs = columns |> Array.map Chunked.toVec
                  Sel = None
                  Count = count }

        let table () : Table =
            { Schema = cols
              Columns = cols |> List.mapi (fun ci field -> Chunked.toColumn field columns[ci]) }

        { Source = lazy (table ())
          Frame = frame
          Cols = cols
          Count = count
          Columns = ready columns }

    /// An evaluation's result kept prepared (Phase 342): the frame the evaluator ended on, as the
    /// version it stands for, with the table built from it only the first time something reads it.
    /// A frame carrying a selection is gathered dense first — one typed gather per column, no cell
    /// boxed — so the version holds exactly its own rows (a `Limit` of ten over a large source keeps
    /// ten rows alive, not the source's vectors) and reads like any other prepared source: identity
    /// selection, `Count` its row count, the rope cut from its vectors on first demand. The table
    /// read back adopts the vectors (Phase 423), so a vector still held from a boundary is the
    /// consumer's own storage handed back. A frame with no columns stands for the empty table,
    /// which has no rows, so its version has none either — as `ofTable` of that table would.
    let ofFrame (f: Frame) : Prepared =
        let dense =
            match f.Sel with
            | _ when f.Vecs.Length = 0 ->
                { Cols = f.Cols
                  Vecs = [||]
                  Sel = None
                  Count = 0 }
            | None -> f
            | Some s ->
                { Cols = f.Cols
                  Vecs = f.Vecs |> Array.map (fun v -> Vec.gather v s)
                  Sel = None
                  Count = s.Length }

        let types = dense.Cols |> List.map _.Type |> List.toArray

        { Source = lazy (Frame.toTable dense)
          Frame = ready dense
          Cols = dense.Cols
          Count = dense.Count
          Columns = lazy (Array.mapi (fun ci v -> Chunked.ofVec types[ci] Chunked.rows v) dense.Vecs) }

    /// The rope, cut if it has not been yet.
    let columns (p: Prepared) : Chunked[] = p.Columns.Value

    /// The table this version stands for, built if it has not been yet.
    let table (p: Prepared) : Table = p.Source.Value

    /// Is `t` the very object this version was prepared from? Never forces a table that was not.
    let isFrom (p: Prepared) (t: Table) : bool =
        p.Source.IsValueCreated && obj.ReferenceEquals(p.Source.Value, t)
