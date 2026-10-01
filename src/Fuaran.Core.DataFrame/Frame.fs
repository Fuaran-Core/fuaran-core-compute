namespace Fuaran.Core

// ============================================================================
//  Fuaran.Core.DataFrame — the evaluator's dense columnar frame (Phase 267).
//
//  The evaluator's working form between the `Table` it is handed and the
//  `Table` it returns. Internal to this assembly: `Table` stays the boundary
//  and `Column` in the column layer is untouched, so a consumer never sees a
//  vector, a mask or a selection. What it sees is the same cells in the same
//  order — the transform law vectors decide that, byte for byte.
//
//  One vector per column, typed where the column's cells agree with its
//  declared type (an `int[]`, a `float[]`, a `bool[]` or a `string[]`, each
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

/// One column's cells as a dense vector: a typed carrier beside a validity mask (`true` = present)
/// where every present cell agrees with the column's declared type, or the boxed cells where one
/// does not. The three string-carrying families share one carrier and are told apart by the type
/// the vector records, so the cell it reads back is the cell that was unpacked.
type internal Vec =
    | Ints of int[] * bool[]
    | Floats of float[] * bool[]
    | Bools of bool[] * bool[]
    | Strs of ColumnType * string[] * bool[]
    | Cells of Cell[]

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
        | Cells a -> a.Length

    /// The declared type a typed vector carries; `None` for the boxed fall-back, whose cells may
    /// disagree with the schema.
    let declaredType (v: Vec) : ColumnType option =
        match v with
        | Ints _ -> Some IntType
        | Floats _ -> Some FloatType
        | Bools _ -> Some BoolType
        | Strs(ty, _, _) -> Some ty
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

    /// The cell at physical row `p` — boxed on demand from the typed carrier, or read as it is from
    /// the boxed one.
    let cellAt (v: Vec) (p: int) : Cell =
        match v with
        | Ints(a, m) -> if m[p] then Int a[p] else Null
        | Floats(a, m) -> if m[p] then Float a[p] else Null
        | Bools(a, m) -> if m[p] then Bool a[p] else Null
        | Strs(ty, a, m) -> if m[p] then strCell ty a[p] else Null
        | Cells a -> a[p]

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
                out[posOf i] <- cells[i]

            Cells out

        match ty with
        | IntType ->
            let vals: int[] = Array.zeroCreate count
            let mask: bool[] = Array.zeroCreate count
            let mutable ok = true
            let mutable i = 0

            while ok && i < n do
                match cells[i] with
                | Int v ->
                    let p = posOf i
                    vals[p] <- v
                    mask[p] <- true
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
                match cells[i] with
                | Float v ->
                    let p = posOf i
                    vals[p] <- v
                    mask[p] <- true
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
                match cells[i] with
                | Bool v ->
                    let p = posOf i
                    vals[p] <- v
                    mask[p] <- true
                | Null -> ()
                | _ -> ok <- false

                i <- i + 1

            if ok then Bools(vals, mask) else boxed ()
        // A decimal column has no typed carrier here (Phase 277): it is packed boxed, and every kernel
        // over it reads cells through the reference arm. Its typed vector is Phase 280's.
        | DecimalType -> boxed ()
        | StringType
        | DateType
        | TimestampType ->
            let vals: string[] = Array.zeroCreate count
            let mask: bool[] = Array.zeroCreate count
            let mutable ok = true
            let mutable i = 0

            while ok && i < n do
                let s =
                    match cells[i], ty with
                    | Str s, StringType
                    | Date s, DateType
                    | Timestamp s, TimestampType -> s
                    | _ -> null

                if not (isNull s) then
                    let p = posOf i
                    vals[p] <- s
                    mask[p] <- true
                else
                    match cells[i] with
                    | Null -> ()
                    | _ -> ok <- false

                i <- i + 1

            if ok then Strs(ty, vals, mask) else boxed ()

    /// Pack a dense column: `cells[i]` at physical row `i`.
    let pack (ty: ColumnType) (cells: Cell[]) : Vec = packAt ty cells.Length id cells

    /// The vector's cells at the physical rows `phys`, in that order, as a dense vector of the same
    /// kind — no cell is boxed on the typed path.
    let gather (v: Vec) (phys: int[]) : Vec =
        let n = phys.Length

        match v with
        | Ints(a, m) -> Ints(Array.init n (fun i -> a[phys[i]]), Array.init n (fun i -> m[phys[i]]))
        | Floats(a, m) -> Floats(Array.init n (fun i -> a[phys[i]]), Array.init n (fun i -> m[phys[i]]))
        | Bools(a, m) -> Bools(Array.init n (fun i -> a[phys[i]]), Array.init n (fun i -> m[phys[i]]))
        | Strs(ty, a, m) -> Strs(ty, Array.init n (fun i -> a[phys[i]]), Array.init n (fun i -> m[phys[i]]))
        | Cells a -> Cells(Array.init n (fun i -> a[phys[i]]))

    /// Two DENSE vectors end to end: the same kind when both are typed alike, boxed otherwise.
    let append (a: Vec) (b: Vec) : Vec =
        match a, b with
        | Ints(x, mx), Ints(y, my) -> Ints(Array.append x y, Array.append mx my)
        | Floats(x, mx), Floats(y, my) -> Floats(Array.append x y, Array.append mx my)
        | Bools(x, mx), Bools(y, my) -> Bools(Array.append x y, Array.append mx my)
        | Strs(ta, x, mx), Strs(tb, y, my) when ta = tb -> Strs(ta, Array.append x y, Array.append mx my)
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
    let physical (f: Frame) : int[] =
        match f.Sel with
        | Some s -> s
        | None -> Array.init f.Count id

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

    /// A table as a frame: one typed unpack per schema column, the identity selection.
    ///
    /// A short column is padded with `Null` to the table's row count and a long one is cut to it;
    /// a schema name the table carries no column for is all `Null`. That is exactly what the
    /// per-index reads this replaced answered (`Column.cell` is total and `Null` past the end), and
    /// what `RowAccess.columns` still answers for the row form. A column that needed neither keeps
    /// its list as the vector's origin.
    let ofTable (t: Table) : Frame =
        let n = Table.rowCount t

        let unpacked =
            t.Schema
            |> List.map (fun (name, ty) ->
                match Table.tryColumn name t with
                | Some c ->
                    let a = List.toArray c.Cells

                    if a.Length = n then
                        Vec.pack ty a, Some c.Cells
                    else
                        Vec.pack ty (Array.init n (fun i -> if i < a.Length then a[i] else Null)), None
                | None -> Vec.pack ty (Array.create n Null), None)
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

                for i in n - 1 .. -1 .. 0 do
                    acc <- Vec.cellAt v phys[i] :: acc

                acc

        { Schema = f.Cols
          Columns = f.Cols |> List.mapi (fun ci (name, ty) -> Column.create name ty (columnOf ci)) }

    /// Every logical row, in logical order, as an array of the schema's width — the gather the
    /// row-oriented verbs read through. The arrays are fresh; a cell is boxed per read on the
    /// typed path.
    let rowsOf (f: Frame) : Cell[][] =
        let phys = physical f
        let w = f.Vecs.Length

        Array.init phys.Length (fun i ->
            let p = phys[i]
            Array.init w (fun ci -> Vec.cellAt f.Vecs[ci] p))

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

    /// The rope, cut if it has not been yet.
    let columns (p: Prepared) : Chunked[] = p.Columns.Value

    /// The table this version stands for, built if it has not been yet.
    let table (p: Prepared) : Table = p.Source.Value

    /// Is `t` the very object this version was prepared from? Never forces a table that was not.
    let isFrom (p: Prepared) (t: Table) : bool =
        p.Source.IsValueCreated && obj.ReferenceEquals(p.Source.Value, t)
