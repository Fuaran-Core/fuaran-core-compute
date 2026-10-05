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
//  beside a `bool[]` validity mask — a present integer cell costs five bytes
//  at rest, where a `Cell` in a list cost about fifty-six), and a boxed
//  `Cell[]` where they do not. A selection vector says which physical rows
//  the frame currently holds and in what order, so a `Filter` keeps a subset,
//  a `Limit` slices and a `Sort` permutes without copying a cell; a `Derive`
//  adds one vector and shares every other by reference; the gathering verbs
//  read rows through the selection and emit fresh vectors.
//
//  Fable-clean: numeric arrays compile to typed arrays, the mask is a `bool[]`,
//  and nothing here uses spans, intrinsics, pooled buffers or threads.
// ============================================================================

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

/// One column's cells as a dense vector: a typed carrier beside a validity mask (`true` = present)
/// where every present cell agrees with the column's declared type, or the boxed cells where one
/// does not. The three string-carrying families share one carrier and are told apart by the type
/// the vector records, so the cell it reads back is the cell that was unpacked.
///
/// A decimal column (Phase 280) is `Decs`: the column's cells as they were handed in, beside each
/// present value as an unscaled integer at ONE scale for the column, carried in a float64 — so a
/// comparison, an order and a sum are float operations, exact because every value and every partial
/// sum the kernels keep is an integer of magnitude at most 2^53 - 1. The cells ride along so every
/// read of a cell is the cell the text path reads, byte for byte, with nothing rendered; only the
/// kernels read the integers. A column that does not fit (`ScaledDecimal.scaleOf`) stays `Cells`.
type internal Vec =
    | Ints of int[] * bool[]
    | Floats of float[] * bool[]
    | Bools of bool[] * bool[]
    | Strs of ColumnType * string[] * bool[]
    | Decs of scaled: float[] * scale: int * cells: Cell[] * mask: bool[]
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
/// of every vector, and the bound every selection entry is inside. `Origins` co-indexes with
/// `Vecs` too: the `Cell list` a vector was unpacked from at the boundary, where the vector is
/// still that column untouched and the list was neither padded nor cut, so the boundary out can
/// hand the consumer's own list back rather than box every cell again; `None` for a vector a verb
/// produced. Two frames may share a vector; no code in this assembly writes into a vector it did
/// not allocate itself.
type internal Frame =
    { Cols: Schema
      Vecs: Vec[]
      Origins: Cell list option[]
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
        | TimestampType -> Timestamp s
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
            if Raw.at p m then
                InternedCells.ofInt (Raw.at p a)
            else
                Null
        | Floats(a, m) -> if Raw.at p m then Float(Raw.at p a) else Null
        | Bools(a, m) ->
            if Raw.at p m then
                InternedCells.ofBool (Raw.at p a)
            else
                Null
        | Strs(ty, a, m) -> if Raw.at p m then strCell ty (Raw.at p a) else Null
        | Decs(_, _, cells, _) -> Raw.at p cells
        | Cells a -> Raw.at p a

    /// Is any selected row present?
    let anyPresent (mask: bool[]) (phys: int[]) : bool = phys |> Array.exists (fun p -> mask[p])

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
            let vals: int[] = Array.zeroCreate count
            let mask: bool[] = Array.zeroCreate count
            let mutable ok = true
            let mutable i = 0

            while ok && i < n do
                match Raw.get i cells with
                | Int v ->
                    let p = posOf i
                    Raw.put vals p v
                    Raw.put mask p true
                | Null -> ()
                | _ -> ok <- false

                i <- i + 1

            if ok then Ints(vals, mask) else boxed ()
        | FloatType ->
            let vals: float[] = Array.zeroCreate count
            let mask: bool[] = Array.zeroCreate count
            let mutable ok = true
            let mutable i = 0

            while ok && i < n do
                match Raw.get i cells with
                | Float v ->
                    let p = posOf i
                    Raw.put vals p v
                    Raw.put mask p true
                | Null -> ()
                | _ -> ok <- false

                i <- i + 1

            if ok then Floats(vals, mask) else boxed ()
        | BoolType ->
            let vals: bool[] = Array.zeroCreate count
            let mask: bool[] = Array.zeroCreate count
            let mutable ok = true
            let mutable i = 0

            while ok && i < n do
                match Raw.get i cells with
                | Bool v ->
                    let p = posOf i
                    Raw.put vals p v
                    Raw.put mask p true
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
                let vals: float[] = Array.zeroCreate count
                let mask: bool[] = Array.zeroCreate count
                let out = Array.create count Null

                for i in 0 .. n - 1 do
                    match Raw.get i cells with
                    | Decimal s ->
                        let p = posOf i
                        // `scaleOf` admitted every present cell at this scale, so this always reads.
                        Raw.put vals p (ScaledDecimal.tryScaled scale s |> ValueOption.defaultValue 0.0)
                        Raw.put mask p true
                        Raw.put out p (Raw.get i cells)
                    | _ -> ()

                Decs(vals, scale, out, mask)
            | None -> boxed ()
        | StringType
        | DateType
        | TimestampType ->
            let vals: string[] = Array.zeroCreate count
            let mask: bool[] = Array.zeroCreate count
            let mutable ok = true
            let mutable i = 0

            while ok && i < n do
                let s =
                    match Raw.get i cells, ty with
                    | Str s, StringType
                    | Date s, DateType
                    | Timestamp s, TimestampType -> s
                    | _ -> null

                if not (isNull s) then
                    let p = posOf i
                    Raw.put vals p s
                    Raw.put mask p true
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
            let vals: int[] = Array.zeroCreate n
            let mask: bool[] = Array.zeroCreate n

            let ok =
                walkExact n cells (fun i c ->
                    match c with
                    | Int v ->
                        Raw.set vals i v
                        Raw.set mask i true
                        true
                    | Null -> true
                    | _ -> false)

            if ok then ValueSome(Ints(vals, mask)) else ValueNone
        | FloatType ->
            let vals: float[] = Array.zeroCreate n
            let mask: bool[] = Array.zeroCreate n

            let ok =
                walkExact n cells (fun i c ->
                    match c with
                    | Float v ->
                        Raw.set vals i v
                        Raw.set mask i true
                        true
                    | Null -> true
                    | _ -> false)

            if ok then ValueSome(Floats(vals, mask)) else ValueNone
        | BoolType ->
            let vals: bool[] = Array.zeroCreate n
            let mask: bool[] = Array.zeroCreate n

            let ok =
                walkExact n cells (fun i c ->
                    match c with
                    | Bool v ->
                        Raw.set vals i v
                        Raw.set mask i true
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
                    let vals: float[] = Array.zeroCreate n
                    let mask: bool[] = Array.zeroCreate n

                    for i in 0 .. n - 1 do
                        match Raw.get i out with
                        | Decimal s ->
                            // `scaleOf` admitted every present cell at this scale, so this always reads.
                            Raw.set vals i (ScaledDecimal.tryScaled scale s |> ValueOption.defaultValue 0.0)
                            Raw.set mask i true
                        | _ -> ()

                    ValueSome(Decs(vals, scale, out, mask))
                | None -> ValueNone
            else
                ValueNone
        | StringType
        | DateType
        | TimestampType ->
            let vals: string[] = Array.zeroCreate n
            let mask: bool[] = Array.zeroCreate n

            let ok =
                walkExact n cells (fun i c ->
                    match c, ty with
                    | Str s, StringType
                    | Date s, DateType
                    | Timestamp s, TimestampType when not (isNull s) ->
                        Raw.set vals i s
                        Raw.set mask i true
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
        | Ints(a, m) -> Ints(pick a (Array.zeroCreate n), pick m (Array.zeroCreate n))
        | Floats(a, m) -> Floats(pick a (Array.zeroCreate n), pick m (Array.zeroCreate n))
        | Bools(a, m) -> Bools(pick a (Array.zeroCreate n), pick m (Array.zeroCreate n))
        | Strs(ty, a, m) -> Strs(ty, pick a (Array.zeroCreate n), pick m (Array.zeroCreate n))
        | Decs(a, s, c, m) ->
            Decs(pick a (Array.zeroCreate n), s, pick c (Array.zeroCreate n), pick m (Array.zeroCreate n))
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

        let maskOf (m: bool[]) = pick m false (Array.zeroCreate n)

        match v with
        | Ints(a, m) -> Ints(pick a 0 (Array.zeroCreate n), maskOf m)
        | Floats(a, m) -> Floats(pick a 0.0 (Array.zeroCreate n), maskOf m)
        | Bools(a, m) -> Bools(pick a false (Array.zeroCreate n), maskOf m)
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
        | Timestamp s, TimestampType -> Some s
        | _ -> None

    /// A copy of the dense vector with the cell at `i` replaced — never a write into `v`, whose
    /// arrays another version may hold. A typed vector stays typed for a cell of its type or
    /// `Null` (the value array is shared where only the mask moves), and is boxed for any other.
    let setAt (v: Vec) (i: int) (c: Cell) : Vec =
        let boxed () =
            let out = Array.init (length v) (cellAt v)
            out[i] <- c
            Cells out

        let masked (m: bool[]) =
            let m' = Array.copy m
            m'[i] <- false
            m'

        match v, c with
        | Ints(a, m), Int x ->
            let a' = Array.copy a
            let m' = Array.copy m
            a'[i] <- x
            m'[i] <- true
            Ints(a', m')
        | Ints(a, m), Null -> Ints(a, masked m)
        | Floats(a, m), Float x ->
            let a' = Array.copy a
            let m' = Array.copy m
            a'[i] <- x
            m'[i] <- true
            Floats(a', m')
        | Floats(a, m), Null -> Floats(a, masked m)
        | Bools(a, m), Bool x ->
            let a' = Array.copy a
            let m' = Array.copy m
            a'[i] <- x
            m'[i] <- true
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
                    m'[i] <- true
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
                m'[i] <- true
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
                        | Int x -> m[j] && a[j] = x
                        | Null -> not m[j]
                        | _ -> false
                    | Floats(a, m) ->
                        match cells[offset + j] with
                        | Float x -> m[j] && a[j] = x
                        | Null -> not m[j]
                        | _ -> false
                    | Bools(a, m) ->
                        match cells[offset + j] with
                        | Bool x -> m[j] && a[j] = x
                        | Null -> not m[j]
                        | _ -> false
                    | Strs(ty, a, m) ->
                        match cells[offset + j] with
                        | Null -> not m[j]
                        | c ->
                            match carrierOf ty c with
                            | Some s -> m[j] && a[j] = s
                            | None -> false
                    | Decs(_, _, a, _)
                    | Cells a -> a[j] = cells[offset + j]

                j <- j + 1

            ok

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
            let rows: int[] = Array.zeroCreate n

            for i in 0 .. n - 1 do
                Raw.set rows i i

            rows

    /// The frame holding the physical rows `sel`, in that order, over the same vectors.
    let select (f: Frame) (sel: int[]) : Frame = { f with Sel = Some sel }

    /// Well-formedness: one vector and one origin per schema column, every vector `Count` long,
    /// every origin the vector's own length, and every selection entry naming a physical row. The
    /// invariant every verb preserves; a law in the suite holds it after every step of every
    /// generated pipeline.
    let wellFormed (f: Frame) : bool =
        f.Vecs.Length = List.length f.Cols
        && f.Origins.Length = f.Vecs.Length
        && f.Vecs |> Array.forall (fun v -> Vec.length v = f.Count)
        && f.Origins
           |> Array.forall (fun o ->
               match o with
               | Some cells -> List.length cells = f.Count
               | None -> true)
        && (match f.Sel with
            | None -> true
            | Some s -> s |> Array.forall (fun p -> p >= 0 && p < f.Count))

    /// The fall-back of the boundary in (Phase 327): the column's list copied to an array, padded or
    /// cut, then packed — the path every column took before `Vec.packList`, and the one a ragged
    /// column, an absent one or one with a cell out of its type still takes.
    ///
    /// A short column is padded with `Null` to the table's row count and a long one is cut to it;
    /// a schema name the table carries no column for is all `Null`. That is exactly what the
    /// per-index reads this replaced answered (`Column.cell` is total and `Null` past the end), and
    /// what `RowAccess.columns` still answers for the row form. A column that needed neither keeps
    /// its list as the vector's origin.
    let unpackFallback (n: int) (ty: ColumnType) (column: Column option) : Vec * Cell list option =
        match column with
        | Some c ->
            let a = List.toArray c.Cells

            if a.Length = n then
                Vec.pack ty a, Some c.Cells
            else
                Vec.pack ty (Array.init n (fun i -> if i < a.Length then a[i] else Null)), None
        | None -> Vec.pack ty (Array.create n Null), None

    /// A table as a frame: one typed unpack per schema column, the identity selection. Each column
    /// is packed straight from its list in one walk
    /// (`Vec.packList`), keeping the list as its origin; a column that does not fit takes
    /// `unpackFallback`.
    let ofTable (t: Table) : Frame =
        let n = Table.rowCount t

        let unpacked =
            t.Schema
            |> List.map (fun (name, ty) ->
                match Table.tryColumn name t with
                | Some c ->
                    match Vec.packList ty n c.Cells with
                    | ValueSome v -> v, Some c.Cells
                    | ValueNone -> unpackFallback n ty (Some c)
                | None -> unpackFallback n ty None)
            |> List.toArray

        { Cols = t.Schema
          Vecs = unpacked |> Array.map fst
          Origins = unpacked |> Array.map snd
          Sel = None
          Count = n }

    /// The frame as a table: one `Cell list` per column under the schema's declared types — the
    /// origin list itself where the selection is still the identity and the vector still the
    /// column it was unpacked from, else built from the back through the selection.
    let toTable (f: Frame) : Table =
        let phys = physical f
        let n = phys.Length

        let columnOf (ci: int) : Cell list =
            match f.Sel, f.Origins[ci] with
            | None, Some cells -> cells
            | _ ->
                let v = f.Vecs[ci]
                let mutable acc = []

#if FABLE_COMPILER
                // Under JavaScript (Phase 326) a counted loop: a descending `for` compiles there to a
                // range enumerator. The loop proves `i` for `phys`.
                let mutable i = n - 1

                while i >= 0 do
                    acc <- Vec.cellAt v (Raw.get i phys) :: acc
                    i <- i - 1
#else
                for i in n - 1 .. -1 .. 0 do
                    acc <- Vec.cellAt v phys[i] :: acc
#endif

                acc

        { Schema = f.Cols
          Columns = f.Cols |> List.mapi (fun ci (name, ty) -> Column.create name ty (columnOf ci)) }

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
            |> List.mapi (fun ci (_, ty) -> Vec.pack ty (Array.init n (fun r -> rows[r][ci])))
            |> List.toArray
          Origins = Array.create (List.length cols) None
          Sel = None
          Count = n }

    /// The schema columns at `idx`, renamed per `cols`, sharing their vectors — a projection copies
    /// no cell, and a column projected twice is one vector held twice.
    let project (f: Frame) (idx: int[]) (cols: Schema) : Frame =
        { f with
            Cols = cols
            Vecs = idx |> Array.map (fun i -> f.Vecs[i])
            Origins = idx |> Array.map (fun i -> f.Origins[i]) }

    /// The frame with `name` upserted as `v` of type `ty`: replaced in place where the schema
    /// carries the name, appended otherwise. Every other vector is shared.
    let withColumn (f: Frame) (name: string) (ty: ColumnType) (v: Vec) : Frame =
        match f.Cols |> List.tryFindIndex (fun (n, _) -> n = name) with
        | Some i ->
            let vecs = Array.copy f.Vecs
            vecs[i] <- v
            let origins = Array.copy f.Origins
            origins[i] <- None

            { f with
                Cols = f.Cols |> List.mapi (fun j (n, t) -> if j = i then n, ty else n, t)
                Vecs = vecs
                Origins = origins }
        | None ->
            { f with
                Cols = f.Cols @ [ name, ty ]
                Vecs = Array.append f.Vecs [| v |]
                Origins = Array.append f.Origins [| None |] }

    /// The frame with `name` APPENDED as `v` of type `ty`, whether or not the schema already
    /// carries the name — the shape `Window` produces.
    let appendColumn (f: Frame) (name: string) (ty: ColumnType) (v: Vec) : Frame =
        { f with
            Cols = f.Cols @ [ name, ty ]
            Vecs = Array.append f.Vecs [| v |]
            Origins = Array.append f.Origins [| None |] }

    /// This frame's rows followed by `other`'s, under this frame's schema, column by column:
    /// each pair of vectors gathered dense through its selection and appended, typed where both
    /// are typed alike.
    let concat (f: Frame) (other: Frame) : Frame =
        let pa = physical f
        let pb = physical other

        { Cols = f.Cols
          Vecs =
            Array.init f.Vecs.Length (fun ci -> Vec.append (Vec.gather f.Vecs[ci] pa) (Vec.gather other.Vecs[ci] pb))
          Origins = Array.create f.Vecs.Length None
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
    {
        Type: ColumnType
        Size: int
        Length: int
        Chunks: Vec[]
        /// The rope's cells as a list, once something has asked for them — one list per rope, so
        /// every version that shares the rope shares the list, and the table a version stands for
        /// costs the columns an edit moved rather than every column. Seeded with the consumer's own
        /// list where the rope was cut from one. A memo, never read for anything but the list.
        Cells: Cell list option ref
    }

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
          Chunks = Array.init (count size n) (fun k -> Vec.slice v (k * size) (lengthOf size n k))
          Cells = ref None }

    /// Cells cut into chunks of `size` rows, each packed under `ty`.
    let ofCells (ty: ColumnType) (size: int) (cells: Cell[]) : Chunked =
        let n = cells.Length

        { Type = ty
          Size = size
          Length = n
          Chunks = Array.init (count size n) (fun k -> Vec.pack ty (Array.sub cells (k * size) (lengthOf size n k)))
          Cells = ref None }

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

        { c with
            Chunks = chunks
            Cells = ref None }

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
              Cells = ref None
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
                Cells = ref None
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

    /// The rope's cells as a list, in row order — the memo where the rope has one, else built
    /// from the back and kept.
    let toCells (c: Chunked) : Cell list =
        match c.Cells.Value with
        | Some cells -> cells
        | None ->
            let mutable acc = []

            for k in c.Chunks.Length - 1 .. -1 .. 0 do
                let v = c.Chunks[k]

                for i in Vec.length v - 1 .. -1 .. 0 do
                    acc <- Vec.cellAt v i :: acc

            c.Cells.Value <- Some acc
            acc

    /// The rope with its list memo seeded from `cells` — the list an op carried or a consumer
    /// handed in, which IS the rope's cells; a caller's assertion, checked by length only.
    let withCells (cells: Cell list) (c: Chunked) : Chunked =
        if List.length cells = c.Length then
            { c with Cells = ref (Some cells) }
        else
            c

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

    /// A table prepared: its frame unpacked now (one typed unpack per column, as Phase 267 paid
    /// it), its rope cut from the frame on first demand.
    let ofTable (t: Table) : Prepared =
        let f = Frame.ofTable t
        let types = t.Schema |> List.map snd |> List.toArray

        { Source = ready t
          Frame = ready f
          Cols = t.Schema
          Count = f.Count
          Columns =
            lazy
                (Array.mapi
                    (fun ci v ->
                        let rope = Chunked.ofVec types[ci] Chunked.rows v

                        match f.Origins[ci] with
                        | Some cells -> Chunked.withCells cells rope
                        | None -> rope)
                    f.Vecs) }

    /// A version from its rope: the frame and the table each concatenated from it on first read.
    let ofChunks (cols: Schema) (count: int) (columns: Chunked[]) : Prepared =
        let frame =
            lazy
                { Cols = cols
                  Vecs = columns |> Array.map Chunked.toVec
                  Origins = Array.create columns.Length None
                  Sel = None
                  Count = count }

        let table () : Table =
            { Schema = cols
              Columns =
                cols
                |> List.mapi (fun ci (name, ty) -> Column.create name ty (Chunked.toCells columns[ci])) }

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
    /// selection, `Count` its row count, the rope cut from its vectors on first demand. A vector the
    /// frame still holds untouched from a boundary keeps its origin list, so the table read back
    /// hands that list out rather than boxing the column again. A frame with no columns stands for
    /// the empty table, which has no rows, so its version has none either — as `ofTable` of that
    /// table would.
    let ofFrame (f: Frame) : Prepared =
        let dense =
            match f.Sel with
            | _ when f.Vecs.Length = 0 ->
                { Cols = f.Cols
                  Vecs = [||]
                  Origins = [||]
                  Sel = None
                  Count = 0 }
            | None -> f
            | Some s ->
                { Cols = f.Cols
                  Vecs = f.Vecs |> Array.map (fun v -> Vec.gather v s)
                  Origins = Array.create f.Vecs.Length None
                  Sel = None
                  Count = s.Length }

        let types = dense.Cols |> List.map snd |> List.toArray

        { Source = lazy (Frame.toTable dense)
          Frame = ready dense
          Cols = dense.Cols
          Count = dense.Count
          Columns =
            lazy
                (Array.mapi
                    (fun ci v ->
                        let rope = Chunked.ofVec types[ci] Chunked.rows v

                        match dense.Origins[ci] with
                        | Some cells -> Chunked.withCells cells rope
                        | None -> rope)
                    dense.Vecs) }

    /// The rope, cut if it has not been yet.
    let columns (p: Prepared) : Chunked[] = p.Columns.Value

    /// The table this version stands for, built if it has not been yet.
    let table (p: Prepared) : Table = p.Source.Value

    /// Is `t` the very object this version was prepared from? Never forces a table that was not.
    let isFrom (p: Prepared) (t: Table) : bool =
        p.Source.IsValueCreated && obj.ReferenceEquals(p.Source.Value, t)
