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
        | BoolType -> Str s

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
