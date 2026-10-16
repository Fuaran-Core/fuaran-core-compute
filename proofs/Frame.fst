(*
   Frame — the dense frame means exactly its rows: the evaluator's columnar working form, read back
   cell by cell, is the row-major frame `Pipeline.fst` evaluates, and the kernels the evaluator runs
   over it compute what the row-major step computes (fuaran-core Phase 435).

   WHAT IS MODELLED. `src/Fuaran.Compute.DataFrame/Frame.fs` and `Kernels.fs`, clause for clause
   where named:

     - `Vec` (every case: ints, floats, bools, the string family, the decimal vector's unscaled
       integers beside its held texts, the calendar days, the timestamp's seconds beside its
       optional fraction vector, the boxed cells) and `Vec.cellAt`, the one read every verb and the
       boundary out make of a vector — each typed carrier beside a validity mask, `true` = present;
     - the boundary IN: `Vec.ofColumn` with `maskOf` (a null-free Core column holds NO mask —
       Phase 420's `AllValid` — and becomes an all-set mask here: THE NULL-FREE GUARD), the view of
       a column of the schema's type and length, `Vec.ofDecimalTexts` with `ScaledDecimal.fill`, and
       the fall-back `Vec.pack` (`packAt` at the identity) for any other column; `Frame.ofTable`,
       `Frame.ofRows`, `Frame.rows`, `physical`, `select`, `wellFormed` and `Frame.rowsOf`;
     - the boundary OUT: `Vec.gather`, `Vec.toColumn` with `validityOf` (`Validity.ofVector`: an
       all-set mask is `AllValid` again) over every typed arm, and `Frame.toTable`;
     - `DataFrame.prepare`, `prepareRows` (its width refusal, then `Prepared.ofFrame` of the frame of
       the rows) and `toTable`, reduced to the source table and the frame they hold;
     - the comparison kernels and the bitmap algebra — `CmpOp`, `holds`, `flip`, the portable
       member's `cmpInts`, `cmpFloats`, `andBits`, `orBits`, `selection`, `isSet` — and
       `DataFrame.filterBits` with the kernel branch of `evalFilterChecked`.

   Core's column is restated to the minimum this module reads (section 1): `Validity`,
   `ColumnData`, `Column.cell`. It is `proofs/ColumnRefinement.fst` in the Fuaran.Core repository
   (Phase 419) read at `Pipeline.cell`: `core_cell` is that module's `cell_at` with its `to_mask`
   unfolded, and that module's `cells_of_nth` proves `cell_at i` is the i-th of `Column.toCells`.
   The cell and the row-major frame are `Pipeline.fst`'s, by name.

   WHAT IS PROVED, at every column type (int, float, bool, string, date, timestamp at every unit,
   decimal), with a validity mask and without one:

     - `view_reads`: a Core column of the schema's type and length, read through the frame's view
       of it, is `Column.cell` row for row; `of_column_reads` and `of_table_reads`: `Frame.ofTable`
       reads back every column the schema names — the column's own cells where it holds the field's
       type and the table's length, the fall-back's otherwise; `of_table_reads_columns`: over a
       table every field of which holds its column at its type and length (what `Table.validate`
       admits), the frame's cell `(i, j)` IS `Column.cell i` of column `j`;
     - `rows_of_reads`: the read-back is TOTAL — `rows` rows, each as wide as the schema — and cell
       `(i, j)` is vector `j` at the physical row the selection gives logical row `i`;
     - `pack_reads` and `of_rows_reads`: `Vec.pack` and `Frame.ofRows` read back every cell as the
       type carries it (`pack_cell`: an int in a float or decimal column is that type's value — on
       the boxed form as on the typed carrier since Phase 472 — a temporal text is the canonical
       text of what it parsed to, and a column holding any cell its type does not carry stays
       boxed, every other cell as it was); `of_rows_exact`: rows the types carry
       exactly come back from `ofRows` then `rowsOf` as they went in;
     - `gather_reads`, `to_column_reads` and `to_table_reads`: `Frame.toTable` writes exactly the
       logical rows — through the selection, one typed gather per column, the column adopting the
       storage and `AllValid` where every row is present; `of_frame_reads`, `prepare_rows_reads`,
       `prepare_is_source`: the prepared source's frame and its table are those rows;
     - `filter_kernel_is_step` (THE KERNEL THEOREM): where `filterBits` answers a bitmap, the kernel
       branch of the filter is `Pipeline.eval_step`'s `Filter` over the read-back — the same rows,
       in the same order, under the same schema, and never an error — from `bits_eval` (per row,
       the bitmap's bit is the evaluator's answer) and `leaf_eval` (per comparison leaf, the int,
       float, float-against-int, decimal and decimal-against-int kernels, either operand order).

   WHAT IS A PARAMETER, AND WHAT EACH THEOREM ASSUMES OF IT.

     - `host` — the functions the frame calls that this module does not restate: the canonical
       temporal texts and their readers (Core's `TemporalText`; `Column.cell` and `Vec.cellAt`
       call ONE function, so every read-back theorem holds for every such function and assumes
       nothing of it), the float carrier and decimal text of an int, `ScaledDecimal.scaleOf` /
       `tryScaled`, and `Kernels.compareFloat` over carriers. The read-back theorems assume NOTHING
       of the host. `decs_ok` (the decimal vector carries its texts' readings) is established by the
       boundary under `scale_admits` — a scale `scaleOf` answers reads every present text — and the
       kernel theorem rests on it.
     - `kernel_prims` — the kernel theorem's premise on `Pipeline.fst`'s cell primitives at exactly
       the cells a kernel leaf meets: a comparison with `Null` is `Null`; a comparison of the cell
       kinds the kernels read is `Bool` of `holds` over the ordering the kernel computes;
       `compareFloat` is antisymmetric; `And` / `Or` over `Bool` / `Null` are Kleene's. Each is
       production's `comparison` / `logical` at those cells, and the differential instantiates it
       at production's primitives.
     - Two representations are taken as given and named: a decimal vector's unscaled integers are
       float64s in production, exact because each is an integer of magnitude at most 2^53 - 1
       (`ScaledDecimal.Width`; the model holds the integers), and a selection bitmap is one bit per
       row here where production packs 32 rows to a word (`isSet` reads that packing; the
       differential reads production's words through it).

   WHAT IS NOT MODELLED, recorded in `DECISIONS.md` (D34): the native kernel member (128-bit
   vectors and the thread pool; held equal to the portable member by the suite), the morsel runner
   and the compiled filter path that `filterBits` declines to (`filterMorsel`, `compileExpr` — the
   evaluator `Pipeline.fst` models is the reference they are held to), the keyed verbs' sort and
   partition kernels (`SortFinite`, `SortPositions`, `Partitions`, `RunPipelines`), `gatherOrNull`,
   `Vec.append` / `slice` / `concat` / `setAt` / `sameCells`, `ofTemporalTexts`, the persistent rope
   (`Chunked`, Phase 268, whose edit `ColumnOps.fst` models), the `Shared` allocator, and the two
   boxed arms of `Vec.toColumn` (a non-string string-family vector and `Cells`, which go through
   Core's `Column.ofCells`, modelled in the Fuaran.Core repository). Every type parameter is
   `Type0`; the module opens `Pipeline` and nothing else.

   Apache-2.0, like everything beside it.
*)
module Frame

open Pipeline

(* ======================================================================================
   0. List helpers (self-contained, as Pipeline's are; each names what it stands for).
   ====================================================================================== *)

(* F#: `List.map` / `Array.map`. *)
let rec lmap (#a #b:Type0) (g:a -> b) (l:list a) : Tot (list b) (decreases l) =
  match l with
  | [] -> []
  | x :: t -> g x :: lmap g t

(* F#: `List.filter` / `Array.filter`. *)
let rec lfilter (#a:Type0) (g:a -> bool) (l:list a) : Tot (list a) (decreases l) =
  match l with
  | [] -> []
  | x :: t -> if g x then x :: lfilter g t else lfilter g t

(* F#: `List.forall`. *)
let rec lfor_all (#a:Type0) (g:a -> bool) (l:list a) : Tot bool (decreases l) =
  match l with
  | [] -> true
  | x :: t -> g x && lfor_all g t

(* F#: `Array.map2` over two arrays of one length. *)
let rec lmap2 (#a #b #c:Type0) (g:a -> b -> c) (l:list a) (m:list b) : Tot (list c) (decreases l) =
  match l, m with
  | x :: t, y :: u -> g x y :: lmap2 g t u
  | _ -> []

(* F#: `[k .. n - 1]` — the counted loop `for p in k .. n - 1`. *)
let rec upto_from (k n:nat) : Tot (list nat) (decreases (if k >= n then 0 else n - k)) =
  if k >= n then [] else k :: upto_from (k + 1) n

let upto (n:nat) : Tot (list nat) = upto_from 0 n

(* F#: `Array.tryItem p a` — the `p`-th element, `None` past the end. *)
let rec vnth (#a:Type0) (l:list a) (p:nat) : Tot (option a) (decreases l) =
  match l with
  | [] -> None
  | x :: t -> if p = 0 then Some x else vnth t (p - 1)

let rec vnth_nth (#a:Type0) (l:list a) (j:nat{j < len l})
  : Lemma (ensures vnth l j == Some (nth l j)) (decreases l) =
  match l with
  | x :: t -> if j = 0 then () else vnth_nth t (j - 1)

(* F#: `Raw.at p a` — a read the caller keeps in range; past the end the model answers the
   default, which no well-formed frame reaches (the lemmas say where it is in range). *)
let at_or (#a:Type0) (d:a) (l:list a) (p:nat) : Tot a =
  match vnth l p with
  | Some x -> x
  | None -> d

(* F#: `Mask.at p m` — `true` = present. Past the end of the mask, absent. *)
let bit (m:list bool) (p:nat) : Tot bool = at_or false m p

let rec len_lmap (#a #b:Type0) (g:a -> b) (l:list a)
  : Lemma (ensures len (lmap g l) == len l) [SMTPat (len (lmap g l))] =
  match l with
  | [] -> ()
  | _ :: t -> len_lmap g t

let rec len_lmap2 (#a #b #c:Type0) (g:a -> b -> c) (l:list a) (m:list b)
  : Lemma (requires len l == len m) (ensures len (lmap2 g l m) == len l) =
  match l, m with
  | _ :: t, _ :: u -> len_lmap2 g t u
  | _ -> ()

let rec vnth_lmap (#a #b:Type0) (g:a -> b) (l:list a) (i:nat)
  : Lemma (ensures vnth (lmap g l) i == (match vnth l i with Some x -> Some (g x) | None -> None))
          (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> if i = 0 then () else vnth_lmap g t (i - 1)

let rec vnth_lmap2 (#a #b #c:Type0) (g:a -> b -> c) (l:list a) (m:list b) (i:nat)
  : Lemma (ensures vnth (lmap2 g l m) i ==
                   (match vnth l i, vnth m i with Some x, Some y -> Some (g x y) | _ -> None))
          (decreases l) =
  match l, m with
  | _ :: t, _ :: u -> if i = 0 then () else vnth_lmap2 g t u (i - 1)
  | _ -> ()

let rec len_upto_from (k n:nat)
  : Lemma (ensures len (upto_from k n) == (if k >= n then 0 else n - k)) (decreases (if k >= n then 0 else n - k)) =
  if k >= n then () else len_upto_from (k + 1) n

let rec vnth_upto_from (k n i:nat)
  : Lemma (ensures vnth (upto_from k n) i == (if k + i < n then Some (k + i) else None))
          (decreases (if k >= n then 0 else n - k)) =
  if k >= n then () else if i = 0 then () else vnth_upto_from (k + 1) n (i - 1)

let len_upto (n:nat) : Lemma (ensures len (upto n) == n) [SMTPat (len (upto n))] = len_upto_from 0 n

(* Reading back a list built from the counted loop: entry `p` is the loop body at `p`. *)
let vnth_lmap_upto (#b:Type0) (g:nat -> b) (n p:nat)
  : Lemma (ensures vnth (lmap g (upto n)) p == (if p < n then Some (g p) else None)) =
  vnth_lmap g (upto n) p;
  vnth_upto_from 0 n p

let rec vnth_some_lt (#a:Type0) (l:list a) (i:nat)
  : Lemma (ensures (Some? (vnth l i) <==> i < len l)) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> if i = 0 then () else vnth_some_lt t (i - 1)

let rec lfor_all_nth (#a:Type0) (g:a -> bool) (l:list a) (i:nat)
  : Lemma (requires lfor_all g l /\ i < len l) (ensures (match vnth l i with Some x -> g x | None -> False)) (decreases l) =
  match l with
  | _ :: t -> if i = 0 then () else lfor_all_nth g t (i - 1)

(* ======================================================================================
   1. CORE'S COLUMN, restated to the minimum this module reads — the read-back target.

      F# (Fuaran.Core, `src/Fuaran.Core.Column/Column.fs`): `Validity` (`AllValid | Mask`, Phase
      420), `ColumnData` (one typed vector and a validity per case, Phases 417 and 422), `Column`
      and `Column.cell`. This is `proofs/ColumnRefinement.fst` in the Fuaran.Core repository read
      at THIS module's cell (`Pipeline.cell`): there `cell_at i c` reads the materialised mask
      `to_mask (length c) v` and the i-th value, `Null` where the mask says absent or the vector
      has ended, and `cells_of_nth` proves it is the i-th of `Column.toCells` — so `core_cell`
      below is Core's `cell_at` with `to_mask` unfolded (`AllValid` reads every row present,
      `all_valid_reads_all_true`), and a statement about `core_cell` is a statement about
      `toCells`, the column's logical cells. A vector is the list of its elements
      (`Vector.toArray`), as there.
   ====================================================================================== *)

(* F#: `TimeUnit`. *)
type time_unit =
  | Seconds : time_unit
  | Millis  : time_unit
  | Micros  : time_unit
  | Nanos   : time_unit

(* F#: Core's `ColumnType`, WITH the timestamp's unit — the type a `Field` and a `Column` carry.
   `Pipeline.column_type` is its erasure (`erase`). *)
type ftype =
  | TInt     : ftype
  | TFloat   : ftype
  | TBool    : ftype
  | TString  : ftype
  | TDate    : ftype
  | TStamp   : u:time_unit -> ftype
  | TDecimal : ftype

let erase (t:ftype) : Tot column_type =
  match t with
  | TInt -> IntType
  | TFloat -> FloatType
  | TBool -> BoolType
  | TString -> StringType
  | TDate -> DateType
  | TStamp _ -> TimestampType
  | TDecimal -> DecimalType

(* F#: `Validity` (Phase 420) — a null-free column holds NO mask. *)
type validity =
  | AllValid : validity
  | VMask    : present:list bool -> validity

(* F#: `ColumnData`, case for case. A float crosses as its carrier, as `Pipeline.cell`'s does; a
   temporal value is an integer (days; whole epoch seconds beside an optional fraction vector). *)
type cdata =
  | DInts     : values:list int -> v:validity -> cdata
  | DFloats   : values:list string -> v:validity -> cdata
  | DBools    : values:list bool -> v:validity -> cdata
  | DStrs     : values:list string -> v:validity -> cdata
  | DDates    : values:list int -> v:validity -> cdata
  | DStamps   : u:time_unit -> seconds:list int -> fraction:option (list int) -> v:validity -> cdata
  | DDecimals : values:list string -> v:validity -> cdata

(* F#: `Column = { Name; Data }`. *)
type ccolumn = { cname : string; cdata : cdata }

(* F#: `Table = { Schema; Columns }`, a schema field as its name and type. *)
type ctable = { tschema : list (string & ftype); tcolumns : list ccolumn }

(* F#: `Column.Type`. *)
let ctype (c:ccolumn) : Tot ftype =
  match c.cdata with
  | DInts _ _ -> TInt
  | DFloats _ _ -> TFloat
  | DBools _ _ -> TBool
  | DStrs _ _ -> TString
  | DDates _ _ -> TDate
  | DStamps u _ _ _ -> TStamp u
  | DDecimals _ _ -> TDecimal

(* F#: `Column.length` — the values vector's length. *)
let clength (c:ccolumn) : Tot nat =
  match c.cdata with
  | DInts xs _ -> len xs
  | DFloats xs _ -> len xs
  | DBools xs _ -> len xs
  | DStrs xs _ -> len xs
  | DDates xs _ -> len xs
  | DStamps _ xs _ _ -> len xs
  | DDecimals xs _ -> len xs

(* THE PARAMETERS. Every function below the frame calls that this module does not restate: the
   canonical temporal texts and their readers (Core's `TemporalText`, which `Column.cell` and the
   frame's `Vec.cellAt` both call — one function, so the read-back theorems hold for every one),
   the float carrier of an int and the decimal text of one (what `packAt` writes for an int in a
   float or decimal column), the decimal vector's scale scan and reader (`ScaledDecimal.scaleOf`,
   `tryScaled`), and the kernels' float order (`Kernels.compareFloat`) over carriers. What any
   theorem ASSUMES of them is stated where it is used (`scale_admits`, `kernel_prims`). *)
noeq type host = {
  date_text     : int -> string;                       (* TemporalText.dateText *)
  instant_text  : time_unit -> int -> int -> string;   (* TemporalText.instantText *)
  try_days      : string -> option int;                (* TemporalText.tryDays *)
  try_instant   : time_unit -> string -> option (int & int); (* TemporalText.tryInstant *)
  float_of_int  : int -> string;                       (* `float v`, as its carrier *)
  int_text      : int -> string;                       (* `string v` *)
  scale_of      : list string -> list bool -> option nat; (* ScaledDecimal.scaleOf *)
  try_scaled    : nat -> string -> option int;         (* ScaledDecimal.tryScaled *)
  compare_float : string -> string -> int;             (* Kernels.compareFloat *)
}

(* F#: `ColumnStorage.fractionAt` / the frame's `Vec.fractionAt` — `0` where no fraction vector is
   held (the unit is seconds) and past its end, as Core's `zip_frac` reads it. *)
let frac_at (f:option (list int)) (p:nat) : Tot int =
  match f with
  | None -> 0
  | Some fs -> at_or 0 fs p

(* F#: `Validity.isPresent` over the two cases: `AllValid` holds every row, a mask its bit. *)
let present (v:validity) (i:nat) : Tot bool =
  match v with
  | AllValid -> true
  | VMask m -> bit m i

(* F#: `Column.cell i c` — Core's `cell_at` (see the section head). *)
let core_cell (h:host) (c:ccolumn) (i:nat) : Tot cell =
  match c.cdata with
  | DInts xs v -> if present v i then (match vnth xs i with Some x -> Int x | None -> Null) else Null
  | DFloats xs v -> if present v i then (match vnth xs i with Some x -> Float x | None -> Null) else Null
  | DBools xs v -> if present v i then (match vnth xs i with Some x -> Bool x | None -> Null) else Null
  | DStrs xs v -> if present v i then (match vnth xs i with Some x -> Str x | None -> Null) else Null
  | DDates xs v ->
    if present v i then (match vnth xs i with Some x -> Date (h.date_text x) | None -> Null) else Null
  | DStamps u xs f v ->
    if present v i then
      (match vnth xs i with Some x -> Timestamp (h.instant_text u x (frac_at f i)) | None -> Null)
    else Null
  | DDecimals xs v -> if present v i then (match vnth xs i with Some x -> Decimal x | None -> Null) else Null

(* F#: `Column.toCells c` — every row's cell, in order. *)
let core_cells (h:host) (c:ccolumn) : Tot (list cell) = lmap (core_cell h c) (upto (clength c))

(* ======================================================================================
   2. THE DENSE VECTOR — F#: `Vec` (Frame.fs), case for case, and `Vec.cellAt`.

      A typed carrier beside a validity mask (`true` = present), or the boxed cells. The mask is
      always materialised in the frame (a `Mask`: `bool[]` on .NET, bytes under Fable, Phase 376);
      a null-free Core column's `AllValid` becomes an all-set mask at the boundary in (`mask_of`)
      and an all-set mask becomes `AllValid` again at the boundary out (`validity_of`). A decimal
      vector's unscaled integers are float64s in production, exact because every one is an
      integer of magnitude at most 2^53 - 1 (`ScaledDecimal.Width`); the model holds them as the
      integers they are, which is that exactness taken as given.
   ====================================================================================== *)

type vec =
  | VInts   : vals:list int -> mask:list bool -> vec
  | VFloats : vals:list string -> mask:list bool -> vec
  | VBools  : vals:list bool -> mask:list bool -> vec
  | VStrs   : ty:ftype -> vals:list string -> mask:list bool -> vec
  | VDecs   : scaled:list int -> scale:nat -> texts:list string -> mask:list bool -> vec
  | VDates  : days:list int -> mask:list bool -> vec
  | VStamps : u:time_unit -> seconds:list int -> fraction:option (list int) -> mask:list bool -> vec
  | VCells  : cells:list cell -> vec

(* F#: `Vec.length` — the physical row count of any frame holding it. *)
let vlen (v:vec) : Tot nat =
  match v with
  | VInts a _ -> len a
  | VFloats a _ -> len a
  | VBools a _ -> len a
  | VStrs _ a _ -> len a
  | VDecs a _ _ _ -> len a
  | VDates a _ -> len a
  | VStamps _ a _ _ -> len a
  | VCells a -> len a

(* F#: `Vec.strCell` — the cell a string-family vector of type `ty` holds for `s`. *)
let str_cell (ty:ftype) (s:string) : Tot cell =
  match ty with
  | TDate -> Date s
  | TStamp _ -> Timestamp s
  | _ -> Str s

(* F#: `Vec.cellAt v p` — the cell at physical row `p`. *)
let cell_at (h:host) (v:vec) (p:nat) : Tot cell =
  match v with
  | VInts a m -> if bit m p then (match vnth a p with Some x -> Int x | None -> Null) else Null
  | VFloats a m -> if bit m p then (match vnth a p with Some x -> Float x | None -> Null) else Null
  | VBools a m -> if bit m p then (match vnth a p with Some x -> Bool x | None -> Null) else Null
  | VStrs ty a m -> if bit m p then (match vnth a p with Some x -> str_cell ty x | None -> Null) else Null
  | VDecs _ _ t m -> if bit m p then (match vnth t p with Some x -> Decimal x | None -> Null) else Null
  | VDates a m -> if bit m p then (match vnth a p with Some x -> Date (h.date_text x) | None -> Null) else Null
  | VStamps u s f m ->
    if bit m p then (match vnth s p with Some x -> Timestamp (h.instant_text u x (frac_at f p)) | None -> Null)
    else Null
  | VCells a -> (match vnth a p with Some c -> c | None -> Null)

(* A vector's arrays agree in length: the mask (and a decimal vector's texts) is the values'
   length. What every constructor below builds, and what `to_column_reads` needs: a mask SHORTER
   than its values would read its tail absent here and present through `AllValid` there. *)
let vec_wf (v:vec) : Tot bool =
  match v with
  | VInts a m -> len m = len a
  | VFloats a m -> len m = len a
  | VBools a m -> len m = len a
  | VStrs _ a m -> len m = len a
  | VDecs a _ t m -> len m = len a && len t = len a
  | VDates a m -> len m = len a
  | VStamps _ a _ m -> len m = len a
  | VCells _ -> true

(* ======================================================================================
   3. THE BOUNDARY IN — F#: `Vec.ofColumn` with its private `maskOf`, `Vec.ofDecimalTexts`,
      `ScaledDecimal.fill`, and `Vec.packAt` / `Vec.pack` (the fall-back, and the row form's door).
   ====================================================================================== *)

(* F#: `Mask.create n true`. *)
let always (_:nat) : Tot bool = true

let all_true (n:nat) : Tot (list bool) = lmap always (upto n)

(* F#: `maskOf n validity` — `AllValid` holds no mask, so one is built with every row present (THE
   NULL-FREE GUARD); a mask `n` long is borrowed; any other is read into one of `n` rows, a row past
   a shorter mask absent. *)
let mask_of (n:nat) (v:validity) : Tot (list bool) =
  match v with
  | AllValid -> all_true n
  | VMask m -> if len m = n then m else lmap (bit m) (upto n)

(* F#: `ScaledDecimal.fill scale texts mask into` over a fresh zeroed array — every present text's
   unscaled value at `scale` at its row, `0` where `tryScaled` says nothing and at every absent row. *)
let fill_at (h:host) (s:nat) (texts:list string) (mask:list bool) (i:nat) : Tot int =
  if bit mask i then (match h.try_scaled s (at_or "" texts i) with Some k -> k | None -> 0) else 0

let fill (h:host) (s:nat) (texts:list string) (mask:list bool) : Tot (list int) =
  lmap (fill_at h s texts mask) (upto (len texts))

(* The boxed fall-back's cell: `if Mask.at i mask then Decimal texts[i] else Null`. *)
let dec_boxed (texts:list string) (mask:list bool) (i:nat) : Tot cell =
  if bit mask i then Decimal (at_or "" texts i) else Null

(* F#: `Vec.ofDecimalTexts texts mask`. *)
let of_decimal_texts (h:host) (texts:list string) (mask:list bool) : Tot vec =
  match h.scale_of texts mask with
  | Some s -> VDecs (fill h s texts mask) s texts mask
  | None -> VCells (lmap (dec_boxed texts mask) (upto (len texts)))

(* F#: `packAt`'s per-type test of one cell: does a column of type `ty` carry it typed? (`Null` is
   carried as an absent row.) The first cell that is not is where `packAt` abandons the typed
   carrier for the boxed one. *)
let admits (h:host) (ty:ftype) (c:cell) : Tot bool =
  match ty, c with
  | _, Null -> true
  | TInt, Int _ -> true
  | TFloat, Float _ -> true
  | TFloat, Int _ -> true
  | TBool, Bool _ -> true
  | TString, Str _ -> true
  | TDate, Date s -> Some? (h.try_days s)
  | TStamp u, Timestamp s -> Some? (h.try_instant u s)
  | TDecimal, Decimal _ -> true
  | TDecimal, Int _ -> true
  | _ -> false

let is_present (c:cell) : Tot bool = match c with Null -> false | _ -> true

(* The carriers `packAt` writes, one per cell (each read only where the cell is present). *)
let int_of (c:cell) : Tot int = match c with Int v -> v | _ -> 0
let float_of (h:host) (c:cell) : Tot string = match c with Float v -> v | Int v -> h.float_of_int v | _ -> "0"
let bool_of (c:cell) : Tot bool = match c with Bool v -> v | _ -> false
let str_of (c:cell) : Tot string = match c with Str s -> s | _ -> ""
let dec_of (h:host) (c:cell) : Tot string = match c with Decimal s -> s | Int v -> h.int_text v | _ -> ""
let day_of (h:host) (c:cell) : Tot int =
  match c with Date s -> (match h.try_days s with Some d -> d | None -> 0) | _ -> 0
let instant_of (h:host) (u:time_unit) (c:cell) : Tot (int & int) =
  match c with Timestamp s -> (match h.try_instant u s with Some p -> p | None -> (0, 0)) | _ -> (0, 0)

(* `packAt`'s loop body at row `i` (posOf the identity): the cell, and each carrier it writes. *)
let cell_i (cells:list cell) (i:nat) : Tot cell = at_or Null cells i
let pk_present (cells:list cell) (i:nat) : Tot bool = is_present (cell_i cells i)
let pk_int (cells:list cell) (i:nat) : Tot int = int_of (cell_i cells i)
let pk_float (h:host) (cells:list cell) (i:nat) : Tot string = float_of h (cell_i cells i)
let pk_bool (cells:list cell) (i:nat) : Tot bool = bool_of (cell_i cells i)
let pk_str (cells:list cell) (i:nat) : Tot string = str_of (cell_i cells i)
let pk_day (h:host) (cells:list cell) (i:nat) : Tot int = day_of h (cell_i cells i)
let pk_sec (h:host) (u:time_unit) (cells:list cell) (i:nat) : Tot int = fst (instant_of h u (cell_i cells i))
let pk_frac (h:host) (u:time_unit) (cells:list cell) (i:nat) : Tot int = snd (instant_of h u (cell_i cells i))
let pk_dec (h:host) (cells:list cell) (i:nat) : Tot string = dec_of h (cell_i cells i)

let pack_mask (cells:list cell) : Tot (list bool) = lmap (pk_present cells) (upto (len cells))
let pack_texts (h:host) (cells:list cell) : Tot (list string) = lmap (pk_dec h cells) (upto (len cells))

(* Phase 472 — the boxed fall-back's cell (F#: `packAt`'s `widened`): an int under a float or a
   decimal column is that type's value on the boxed form as on the typed carrier; every other cell
   as it was. *)
let widen_cell (h:host) (ty:ftype) (c:cell) : Tot cell =
  match ty, c with
  | TFloat, Int v -> Float (h.float_of_int v)
  | TDecimal, Int v -> Decimal (h.int_text v)
  | _ -> c
let pk_boxed (h:host) (ty:ftype) (cells:list cell) (i:nat) : Tot cell = widen_cell h ty (cell_i cells i)
let pack_boxed (h:host) (ty:ftype) (cells:list cell) : Tot (list cell) = lmap (pk_boxed h ty cells) (upto (len cells))

(* F#: `Vec.pack ty cells` = `packAt ty cells.Length id cells` — one pass filling the typed carrier
   and its mask; any cell the type does not carry, and for a decimal a scale `scaleOf` refuses,
   leaves the cells boxed — each as it was, an int under a float or a decimal column widened
   (Phase 472). A timestamp column at seconds holds no fraction vector. *)
let pack (h:host) (ty:ftype) (cells:list cell) : Tot vec =
  let n = len cells in
  let mask = pack_mask cells in
  if not (lfor_all (admits h ty) cells) then VCells (pack_boxed h ty cells)
  else
    match ty with
    | TInt -> VInts (lmap (pk_int cells) (upto n)) mask
    | TFloat -> VFloats (lmap (pk_float h cells) (upto n)) mask
    | TBool -> VBools (lmap (pk_bool cells) (upto n)) mask
    | TString -> VStrs TString (lmap (pk_str cells) (upto n)) mask
    | TDate -> VDates (lmap (pk_day h cells) (upto n)) mask
    | TStamp u ->
      VStamps u (lmap (pk_sec h u cells) (upto n))
                (if u = Seconds then None else Some (lmap (pk_frac h u cells) (upto n)))
                mask
    | TDecimal ->
      let texts = pack_texts h cells in
      (match h.scale_of texts mask with
       | Some s -> VDecs (fill h s texts mask) s texts mask
       | None -> VCells (pack_boxed h ty cells))

(* F#: `Vec.ofColumn n ty column`. A column of the schema's type and length is a VIEW — its values
   and its validity borrowed; any other (another type, another length, none) is packed from its
   cells padded with `Null` or cut to `n`, as the per-index reads always answered. *)
let pad (n:nat) (cells:list cell) : Tot (list cell) = lmap (cell_i cells) (upto n)

let of_column (h:host) (n:nat) (ty:ftype) (column:option ccolumn) : Tot vec =
  match column with
  | None -> pack h ty (pad n [])
  | Some c ->
    if ctype c <> ty || clength c <> n then
      (let a = core_cells h c in if len a = n then pack h ty a else pack h ty (pad n a))
    else
      (match c.cdata with
       | DInts v va -> VInts v (mask_of n va)
       | DFloats v va -> VFloats v (mask_of n va)
       | DBools v va -> VBools v (mask_of n va)
       | DStrs v va -> VStrs TString v (mask_of n va)
       | DDates v va -> VDates v (mask_of n va)
       | DStamps u s f va -> VStamps u s f (mask_of n va)
       | DDecimals v va -> of_decimal_texts h v (mask_of n va))

(* ======================================================================================
   4. THE FRAME — F#: `Frame` and `Frame.rows` / `physical` / `select` / `wellFormed` / `ofTable` /
      `rowsOf` / `ofRows`.
   ====================================================================================== *)

(* F#: `{ Cols; Vecs; Sel; Count }`. The selection is the PHYSICAL row of each LOGICAL row, in
   logical order, `None` for the identity. *)
type dframe = { fcols : list (string & ftype); vecs : list vec; sel : option (list nat); count : nat }

let erase_field (p:string & ftype) : Tot (string & column_type) = (fst p, erase (snd p))

let erase_schema (cols:list (string & ftype)) : Tot schema = lmap erase_field cols

(* F#: `Frame.rows`. *)
let rows (f:dframe) : Tot nat =
  match f.sel with
  | Some s -> len s
  | None -> f.count

(* F#: `Frame.physical`. *)
let physical (f:dframe) : Tot (list nat) =
  match f.sel with
  | Some s -> s
  | None -> upto f.count

(* F#: `Frame.select`. *)
let select (f:dframe) (s:list nat) : Tot dframe = { f with sel = Some s }

(* F#: `Frame.wellFormed`: one vector per schema column, every vector `Count` long, every selection
   entry a physical row; and, beside it, `vec_wf` for every vector. *)
let vec_ok (n:nat) (v:vec) : Tot bool = vlen v = n && vec_wf v

let in_range (n:nat) (p:nat) : Tot bool = p < n

let dwf (f:dframe) : Tot bool =
  len f.vecs = len f.fcols
  && lfor_all (vec_ok f.count) f.vecs
  && (match f.sel with None -> true | Some s -> lfor_all (in_range f.count) s)

let rec len_erase_schema (cols:list (string & ftype))
  : Lemma (ensures len (erase_schema cols) == len cols) [SMTPat (len (erase_schema cols))] =
  len_lmap erase_field cols

(* One logical row: physical row `p` read across every vector. *)
let read_at (h:host) (p:nat) (v:vec) : Tot cell = cell_at h v p

let row_at (h:host) (f:dframe) (p:nat) : Tot (r:list cell{len r == len f.vecs}) =
  lmap (read_at h p) f.vecs

let rec rows_at (h:host) (f:dframe{dwf f}) (ps:list nat)
  : Tot (rs:list (list cell){all_width (len (erase_schema f.fcols)) rs /\ len rs == len ps}) (decreases ps) =
  match ps with
  | [] -> []
  | p :: t -> row_at h f p :: rows_at h f t

(* F#: `Frame.rowsOf` — every logical row, in logical order, as an array of the schema's width. *)
let rows_of (h:host) (f:dframe{dwf f}) : Tot (rs:list (list cell){all_width (len (erase_schema f.fcols)) rs}) =
  rows_at h f (physical f)

(* THE ABSTRACTION: the dense frame as the row-major frame `Pipeline.fst` evaluates — its schema
   (the units erased, as `Pipeline.fst`'s schema carries none) and its rows read back. *)
let logical (h:host) (f:dframe{dwf f}) : Tot wframe =
  { cols = erase_schema f.fcols; rows = rows_of h f }

(* F#: `Table.rowCount` — the length of the first column, 0 for a schema-only table. *)
let row_count (t:ctable) : Tot nat =
  match t.tcolumns with
  | c :: _ -> clength c
  | [] -> 0

(* F#: `Table.tryColumn name t` — the first column of that name. *)
let rec try_column (name:string) (cs:list ccolumn) : Tot (option ccolumn) (decreases cs) =
  match cs with
  | [] -> None
  | c :: t -> if c.cname = name then Some c else try_column name t

(* The view of schema field `fld` over the table's columns. *)
let column_vec (h:host) (n:nat) (cs:list ccolumn) (fld:string & ftype) : Tot vec =
  of_column h n (snd fld) (try_column (fst fld) cs)

(* F#: `Frame.ofTable t` — one view per schema column, the identity selection. *)
let of_table (h:host) (t:ctable) : Tot dframe =
  let n = row_count t in
  { fcols = t.tschema;
    vecs = lmap (column_vec h n t.tcolumns) t.tschema;
    sel = None;
    count = n }

(* Column `j` of full-width rows: `Array.init n (fun r -> rows[r][ci])`. *)
let cell_of_row (j:nat) (r:list cell) : Tot cell = at_or Null r j

let column_of (rs:list (list cell)) (j:nat) : Tot (list cell) = lmap (cell_of_row j) rs

let rec lmapi_from (#a #b:Type0) (k:nat) (g:nat -> a -> b) (l:list a) : Tot (list b) (decreases l) =
  match l with
  | [] -> []
  | x :: t -> g k x :: lmapi_from (k + 1) g t

(* F#: `List.mapi`. *)
let lmapi (#a #b:Type0) (g:nat -> a -> b) (l:list a) : Tot (list b) = lmapi_from 0 g l

(* Column `ci` of the rows, packed under its field's type. *)
let row_vec (h:host) (rs:list (list cell)) (ci:nat) (fld:string & ftype) : Tot vec = pack h (snd fld) (column_of rs ci)

(* F#: `Frame.ofRows cols rows` — one typed pack per column, the identity selection. *)
let of_rows (h:host) (cols:list (string & ftype)) (rs:list (list cell)) : Tot dframe =
  { fcols = cols;
    vecs = lmapi (row_vec h rs) cols;
    sel = None;
    count = len rs }

(* ======================================================================================
   5. THE BOUNDARY OUT — F#: `Vec.gather`, `Vec.toColumn` with its private `validityOf`, and
      `Frame.toTable`.
   ====================================================================================== *)

(* F#: `Vec.gather v phys` — the vector's cells at the physical rows `phys`, in that order, as a
   dense vector of the same kind. *)
let gather (v:vec) (phys:list nat) : Tot vec =
  let mk (m:list bool) : list bool = lmap (bit m) phys in
  match v with
  | VInts a m -> VInts (lmap (at_or 0 a) phys) (mk m)
  | VFloats a m -> VFloats (lmap (at_or "0" a) phys) (mk m)
  | VBools a m -> VBools (lmap (at_or false a) phys) (mk m)
  | VStrs ty a m -> VStrs ty (lmap (at_or "" a) phys) (mk m)
  | VDecs a s t m -> VDecs (lmap (at_or 0 a) phys) s (lmap (at_or "" t) phys) (mk m)
  | VDates a m -> VDates (lmap (at_or 0 a) phys) (mk m)
  | VStamps u s f m ->
    VStamps u (lmap (at_or 0 s) phys)
              (match f with None -> None | Some fs -> Some (lmap (at_or 0 fs) phys))
              (mk m)
  | VCells a -> VCells (lmap (at_or Null a) phys)

(* F#: `Validity.ofVector` — `AllValid` where every bit is set, else the mask (Phase 420's
   normal form, THE NULL-FREE GUARD on the way out). *)
let is_true (b:bool) : Tot bool = b

let validity_of (m:list bool) : Tot validity =
  if lfor_all is_true m then AllValid else VMask m

(* F#: `Vec.toColumn field v` — the storage ADOPTED. The two boxed arms (a non-string string-family
   vector, and `Cells`) go through `Column.ofCells`, Core's own builder, which the Fuaran.Core
   repository models (`ColumnRefinement.to_cells_of_cells`); here they answer `None`. *)
let to_column (fld:string & ftype) (v:vec) : Tot (option ccolumn) =
  let name = fst fld in
  match v with
  | VInts a m -> Some ({ cname = name; cdata = DInts a (validity_of m) })
  | VFloats a m -> Some ({ cname = name; cdata = DFloats a (validity_of m) })
  | VBools a m -> Some ({ cname = name; cdata = DBools a (validity_of m) })
  | VStrs TString a m -> Some ({ cname = name; cdata = DStrs a (validity_of m) })
  | VStrs _ _ _ -> None
  | VDates a m -> Some ({ cname = name; cdata = DDates a (validity_of m) })
  | VStamps u s fr m -> Some ({ cname = name; cdata = DStamps u s fr (validity_of m) })
  | VDecs _ _ t m -> Some ({ cname = name; cdata = DDecimals t (validity_of m) })
  | VCells _ -> None

let rec all_some (#a:Type0) (l:list (option a)) : Tot (option (list a)) (decreases l) =
  match l with
  | [] -> Some []
  | Some x :: t -> (match all_some t with Some r -> Some (x :: r) | None -> None)
  | None :: _ -> None

let rec zip_cols (fs:list (string & ftype)) (vs:list vec) : Tot (list (option ccolumn)) (decreases fs) =
  match fs, vs with
  | fld :: ft, v :: vt -> to_column fld v :: zip_cols ft vt
  | _ -> []

let gather_by (s:list nat) (v:vec) : Tot vec = gather v s

(* F#: `Frame.toTable f` — one column per schema field, adopted from the vector where the selection
   is the identity, else from one typed gather through it. `None` where a vector is boxed. *)
let to_table (f:dframe) : Tot (option ctable) =
  let dense = match f.sel with None -> f.vecs | Some s -> lmap (gather_by s) f.vecs in
  match all_some (zip_cols f.fcols dense) with
  | Some cs -> Some ({ tschema = f.fcols; tcolumns = cs })
  | None -> None

(* ======================================================================================
   6. THE PREPARED SOURCE — F#: `DataFrame.prepare` (`Prepared.ofTable`), `DataFrame.prepareRows`
      (`Prepared.ofFrame (Frame.ofRows schema rows)` behind its width check) and `DataFrame.toTable`
      (`Prepared.table`), reduced to what they read and answer: the source table where there is
      one, and the frame. The rope (`Columns`, Phase 268) is cut from the frame lazily and read by
      none of the three.
   ====================================================================================== *)

type prepared = { psource : option ctable; pframe : dframe }

(* F#: `DataFrame.prepare`. *)
let prepare (h:host) (t:ctable) : Tot prepared = { psource = Some t; pframe = of_table h t }

(* F#: `Prepared.ofFrame f` — gathered dense; a frame with no columns stands for the empty table. *)
let of_frame (f:dframe) : Tot prepared =
  let dense =
    if len f.vecs = 0 then { fcols = f.fcols; vecs = []; sel = None; count = 0 }
    else match f.sel with
         | None -> f
         | Some s -> { fcols = f.fcols; vecs = lmap (gather_by s) f.vecs; sel = None; count = len s }
  in
  { psource = None; pframe = dense }

let rec bad_row (w:nat) (k:nat) (rs:list (list cell)) : Tot (option (nat & nat)) (decreases rs) =
  match rs with
  | [] -> None
  | r :: t -> if len r <> w then Some (k, len r) else bad_row w (k + 1) t

(* F#: `DataFrame.prepareRows schema rows` — a row whose width is not the schema's is refused with
   `TypeError`, naming it. *)
let prepare_rows (h:host) (cols:list (string & ftype)) (rs:list (list cell)) : Tot (outcome prepared eval_error) =
  match bad_row (len cols) 0 rs with
  | Some (i, got) ->
    Error (TypeError ("source row " ^ h.int_text i ^ ": " ^ h.int_text got ^ " cells, schema width "
                      ^ h.int_text (len cols)))
  | None -> Ok (of_frame (of_rows h cols rs))

(* F#: `DataFrame.toTable` — the very table it was prepared from, else the frame's. *)
let prepared_table (p:prepared) : Tot (option ctable) =
  match p.psource with
  | Some t -> Some t
  | None -> to_table p.pframe

(* ======================================================================================
   7. THE KERNELS — F#: `Kernels.fs` (`CmpOp`, `holds`, `flip`, the portable member's `cmpInts` /
      `cmpFloats` / `andBits` / `orBits` / `selection`, `isSet`) and `DataFrame.filterBits` with the
      kernel branch of `evalFilterChecked`.

      A selection bitmap is held as one bool per physical row. Production packs it into words —
      row `p` at bit `p % 32` of word `p / 32`, a bit past the vector never set — and `isSet` /
      `selection` read that packing back; the packing is the one representation this model does
      not describe, and the differential reads production's words through `Kernels.isSet`.
   ====================================================================================== *)

(* F#: `CmpOp`. *)
type cmp_op =
  | CLt : cmp_op
  | CLe : cmp_op
  | CGt : cmp_op
  | CGe : cmp_op
  | CEq : cmp_op
  | CNe : cmp_op

(* F#: `Kernels.holds`. *)
let holds (op:cmp_op) (c:int) : Tot bool =
  match op with
  | CLt -> c < 0
  | CLe -> c <= 0
  | CGt -> c > 0
  | CGe -> c >= 0
  | CEq -> c = 0
  | CNe -> c <> 0

(* F#: `Kernels.flip`. *)
let flip (op:cmp_op) : Tot cmp_op =
  match op with
  | CLt -> CGt
  | CLe -> CGe
  | CGt -> CLt
  | CGe -> CLe
  | CEq -> CEq
  | CNe -> CNe

(* F#: `compare` on two ints. *)
let int_cmp (a b:int) : Tot int = if a < b then -1 else if a > b then 1 else 0

(* F#: the portable `cmpInts op vals mask k count` — the rows `p < count` whose value is present
   and satisfies `vals[p] op k`. *)
let cmp_int_at (op:cmp_op) (vals:list int) (mask:list bool) (k:int) (p:nat) : Tot bool =
  bit mask p && holds op (int_cmp (at_or 0 vals p) k)

let cmp_ints (op:cmp_op) (vals:list int) (mask:list bool) (k:int) (count:nat) : Tot (list bool) =
  lmap (cmp_int_at op vals mask k) (upto count)

(* F#: the portable `cmpFloats op vals mask k count`, under `compareFloat`. *)
let cmp_float_at (h:host) (op:cmp_op) (vals:list string) (mask:list bool) (k:string) (p:nat) : Tot bool =
  bit mask p && holds op (h.compare_float (at_or "0" vals p) k)

let cmp_floats (h:host) (op:cmp_op) (vals:list string) (mask:list bool) (k:string) (count:nat) : Tot (list bool) =
  lmap (cmp_float_at h op vals mask k) (upto count)

(* F#: `andBits` / `orBits`. *)
let band (x y:bool) : Tot bool = x && y
let bor (x y:bool) : Tot bool = x || y
let and_bits (a b:list bool) : Tot (list bool) = lmap2 band a b
let or_bits (a b:list bool) : Tot (list bool) = lmap2 bor a b

(* F#: `isSet`. *)
let is_set (bits:list bool) (p:nat) : Tot bool = bit bits p

(* F#: `selection` — the set rows, ascending. *)
let selection (bits:list bool) : Tot (list nat) = lfilter (is_set bits) (upto (len bits))

(* F#: `cmpOpOf`. *)
let cmp_op_of (op:bin_op) : Tot (option cmp_op) =
  match op with
  | Lt -> Some CLt
  | Le -> Some CLe
  | Gt -> Some CGt
  | Ge -> Some CGe
  | Eq -> Some CEq
  | Ne -> Some CNe
  | _ -> None

(* F#: `resolveExpr`'s two leaves `filterBits` matches: `Col name` resolves to `RCol i` where the
   schema carries the name, and `Lit c` / a bound `Param` to `RConst c`. *)
let col_of (f:dframe) (x:col_expr) : Tot (option nat) =
  match x with
  | Col name -> (match index_of name (erase_schema f.fcols) with Some i -> Some i | None -> None)
  | _ -> None

let const_of (env:param_env) (x:col_expr) : Tot (option cell) =
  match x with
  | Lit c -> Some c
  | Param name -> assoc name env
  | _ -> None

let max_exact : int = 9007199254740991

let rec pow10 (s:nat) : Tot int = if s = 0 then 1 else 10 * pow10 (s - 1)

let abs_int (x:int) : Tot int = if x < 0 then 0 - x else x

(* F#: `filterBits`' `leaf op i c`. *)
let leaf (h:host) (f:dframe) (op:cmp_op) (i:nat) (c:cell) : Tot (option (list bool)) =
  match vnth f.vecs i, c with
  | Some (VInts vals mask), Int x -> Some (cmp_ints op vals mask x f.count)
  | Some (VFloats vals mask), Float x -> Some (cmp_floats h op vals mask x f.count)
  | Some (VFloats vals mask), Int x -> Some (cmp_floats h op vals mask (h.float_of_int x) f.count)
  | Some (VDecs vals s _ mask), Decimal x ->
    (match h.try_scaled s x with
     | Some kx -> Some (cmp_ints op vals mask kx f.count)
     | None -> None)
  | Some (VDecs vals s _ mask), Int x ->
    let kx = x * pow10 s in
    if abs_int kx <= max_exact then Some (cmp_ints op vals mask kx f.count) else None
  | _ -> None

(* F#: `filterBits k f e` — `And` / `Or` over comparisons of a typed numeric column with a
   constant (either side); `None` for any other predicate, which the compiled path answers. *)
let rec filter_bits (h:host) (env:param_env) (f:dframe) (x:col_expr) : Tot (option (list bool)) (decreases x) =
  match x with
  | Binary And a b ->
    (match filter_bits h env f a, filter_bits h env f b with
     | Some p, Some q -> Some (and_bits p q)
     | _ -> None)
  | Binary Or a b ->
    (match filter_bits h env f a, filter_bits h env f b with
     | Some p, Some q -> Some (or_bits p q)
     | _ -> None)
  | Binary op a b ->
    (match cmp_op_of op with
     | None -> None
     | Some o ->
       (match col_of f a, const_of env b with
        | Some i, Some c -> leaf h f o i c
        | _ ->
          (match const_of env a, col_of f b with
           | Some c, Some i -> leaf h f (flip o) i c
           | _ -> None)))
  | _ -> None

(* F#: `evalFilterChecked`'s kernel branch: the predicate's rows as a bitmap over the whole vector,
   read back in the frame's logical order — `selection` under the identity, the selection's own
   rows that are set otherwise. `None` where `filterBits` declines (the compiled path's). *)
let filter_kernel (h:host) (env:param_env) (f:dframe) (pred:col_expr) : Tot (option dframe) =
  match filter_bits h env f pred with
  | None -> None
  | Some bits ->
    (match f.sel with
     | None -> Some (select f (selection bits))
     | Some s -> Some (select f (lfilter (is_set bits) s)))

(* ======================================================================================
   8. THE READ-BACK THEOREMS.
   ====================================================================================== *)

(* Two lists that agree at every index are one list. *)
let rec list_ext (#a:Type0) (l1 l2:list a)
  : Lemma (requires (forall (i:nat). vnth l1 i == vnth l2 i)) (ensures l1 == l2) (decreases l1) =
  match l1, l2 with
  | [], [] -> ()
  | [], _ :: _ -> assert (vnth l1 0 == vnth l2 0)
  | _ :: _, [] -> assert (vnth l1 0 == vnth l2 0)
  | x :: t, y :: u ->
    assert (vnth l1 0 == vnth l2 0);
    introduce forall (i:nat). vnth t i == vnth u i
    with assert (vnth l1 (i + 1) == vnth l2 (i + 1));
    list_ext t u

(* A list padded to its own length is itself — so `ofColumn`'s two fall-back branches are one. *)
let pad_id (n:nat) (a:list cell)
  : Lemma (requires len a == n) (ensures pad n a == a) =
  introduce forall (i:nat). vnth (pad n a) i == vnth a i
  with (vnth_lmap_upto (cell_i a) n i; vnth_some_lt a i);
  list_ext (pad n a) a

(* `maskOf` is the validity: one bit per row, and the bit is `isPresent` — `AllValid` reads every
   row present (the null-free guard), a mask its own bit, a mask of another length padded absent. *)
let mask_of_reads (n:nat) (v:validity) (i:nat)
  : Lemma (ensures len (mask_of n v) == n /\ (i < n ==> bit (mask_of n v) i == present v i)) =
  match v with
  | AllValid -> vnth_lmap_upto always n i
  | VMask m -> if len m = n then () else vnth_lmap_upto (bit m) n i

(* The decimal vector's carried integers are the texts' readings at its scale, at every present
   row. What `filterBits`' decimal leaf reads, and what `of_decimal_texts` and `pack` build — under
   the one fact assumed of `scaleOf` (`scale_admits`): a scale it answers reads every present text. *)
[@@ noextract_to "FSharp"]
let decs_ok_vec (h:host) (v:vec) : prop =
  match v with
  | VDecs a s t m ->
    forall (p:nat). (p < len a /\ bit m p) ==> (p < len t /\ h.try_scaled s (at_or "" t p) == Some (at_or 0 a p))
  | _ -> True

[@@ noextract_to "FSharp"]
let rec decs_ok (h:host) (vs:list vec) : prop =
  match vs with
  | [] -> True
  | v :: t -> decs_ok_vec h v /\ decs_ok h t

[@@ noextract_to "FSharp"]
let scale_admits (h:host) : prop =
  forall (texts:list string) (mask:list bool) (s:nat) (p:nat).
    (h.scale_of texts mask == Some s /\ p < len texts /\ bit mask p) ==> Some? (h.try_scaled s (at_or "" texts p))

let fill_reads (h:host) (s:nat) (texts:list string) (mask:list bool) (p:nat)
  : Lemma (ensures len (fill h s texts mask) == len texts /\
                   (p < len texts ==> at_or 0 (fill h s texts mask) p ==
                      (if bit mask p then (match h.try_scaled s (at_or "" texts p) with Some k -> k | None -> 0) else 0))) =
  vnth_lmap_upto (fill_at h s texts mask) (len texts) p

let of_decimal_texts_shape (h:host) (texts:list string) (mask:list bool)
  : Lemma (ensures vlen (of_decimal_texts h texts mask) == len texts /\
                   (len mask == len texts ==> vec_wf (of_decimal_texts h texts mask))) =
  fill_reads h 0 texts mask 0;
  match h.scale_of texts mask with
  | Some s -> fill_reads h s texts mask 0
  | None -> ()

let of_decimal_texts_ok (h:host) (texts:list string) (mask:list bool)
  : Lemma (requires scale_admits h) (ensures decs_ok_vec h (of_decimal_texts h texts mask)) =
  match h.scale_of texts mask with
  | Some s ->
    fill_reads h s texts mask 0;
    introduce forall (p:nat). (p < len (fill h s texts mask) /\ bit mask p) ==>
                (p < len texts /\ h.try_scaled s (at_or "" texts p) == Some (at_or 0 (fill h s texts mask) p))
    with fill_reads h s texts mask p
  | None -> ()

(* THE VIEW THEOREM. A Core column of the schema's type and the frame's length, read through the
   frame's view of it, is Core's column cell for cell — at every type (temporal through the same
   canonical texts, decimal through its held texts whether or not the scaled carrier fits), with a
   mask or `AllValid`. With `ColumnRefinement.cells_of_nth` in the Fuaran.Core repository, every
   cell is the cell `Column.toCells` lists. *)
let view_reads (h:host) (n:nat) (ty:ftype) (c:ccolumn) (i:nat)
  : Lemma (requires ctype c == ty /\ clength c == n /\ i < n)
          (ensures cell_at h (of_column h n ty (Some c)) i == core_cell h c i) =
  match c.cdata with
  | DDecimals v va ->
    mask_of_reads n va i;
    (match h.scale_of v (mask_of n va) with
     | Some _ -> ()
     | None ->
       vnth_lmap_upto (dec_boxed v (mask_of n va)) (len v) i;
       vnth_some_lt v i)
  | DInts _ va -> mask_of_reads n va i
  | DFloats _ va -> mask_of_reads n va i
  | DBools _ va -> mask_of_reads n va i
  | DStrs _ va -> mask_of_reads n va i
  | DDates _ va -> mask_of_reads n va i
  | DStamps _ _ _ va -> mask_of_reads n va i

(* What `pack` reads back: a carried cell normalised by its type (an int in a float or decimal
   column is that type's value, a temporal text is the canonical text of what it parsed to) where
   every cell is carried; where one is not (the boxed fall-back), the cells as they are but for an
   int under a float or decimal column, widened on this form too (Phase 472, `widen_cell`). *)
let packs (h:host) (ty:ftype) (cells:list cell) : Tot bool =
  lfor_all (admits h ty) cells
  && (match ty with TDecimal -> Some? (h.scale_of (pack_texts h cells) (pack_mask cells)) | _ -> true)

let norm_cell (h:host) (ty:ftype) (c:cell) : Tot cell =
  match ty, c with
  | _, Null -> Null
  | TFloat, Int v -> Float (h.float_of_int v)
  | TDecimal, Int v -> Decimal (h.int_text v)
  | TDate, Date _ -> Date (h.date_text (day_of h c))
  | TStamp u, Timestamp _ ->
    Timestamp (h.instant_text u (fst (instant_of h u c)) (if u = Seconds then 0 else snd (instant_of h u c)))
  | _ -> c

let pack_cell (h:host) (ty:ftype) (cells:list cell) (i:nat) : Tot cell =
  if packs h ty cells then norm_cell h ty (at_or Null cells i) else widen_cell h ty (at_or Null cells i)

let pack_shape (h:host) (ty:ftype) (cells:list cell)
  : Lemma (ensures vlen (pack h ty cells) == len cells /\ vec_wf (pack h ty cells) /\
                   (scale_admits h ==> decs_ok_vec h (pack h ty cells))) =
  if lfor_all (admits h ty) cells && ty = TDecimal then begin
    of_decimal_texts_shape h (pack_texts h cells) (pack_mask cells);
    FStar.Classical.move_requires (of_decimal_texts_ok h (pack_texts h cells)) (pack_mask cells)
  end else ()

#push-options "--fuel 1 --ifuel 1"
(* THE PACK THEOREM: `Vec.pack` reads back as `pack_cell`, at every type. *)
let pack_reads (h:host) (ty:ftype) (cells:list cell) (i:nat)
  : Lemma (requires i < len cells)
          (ensures cell_at h (pack h ty cells) i == pack_cell h ty cells i) =
  let n = len cells in
  vnth_some_lt cells i;
  vnth_lmap_upto (pk_present cells) n i;
  if not (lfor_all (admits h ty) cells) then vnth_lmap_upto (pk_boxed h ty cells) n i
  else begin
    lfor_all_nth (admits h ty) cells i;
    match ty with
    | TInt -> vnth_lmap_upto (pk_int cells) n i
    | TFloat -> vnth_lmap_upto (pk_float h cells) n i
    | TBool -> vnth_lmap_upto (pk_bool cells) n i
    | TString -> vnth_lmap_upto (pk_str cells) n i
    | TDate -> vnth_lmap_upto (pk_day h cells) n i
    | TStamp u ->
      vnth_lmap_upto (pk_sec h u cells) n i;
      vnth_lmap_upto (pk_frac h u cells) n i
    | TDecimal ->
      vnth_lmap_upto (pk_dec h cells) n i;
      (match h.scale_of (pack_texts h cells) (pack_mask cells) with
       | Some s -> fill_reads h s (pack_texts h cells) (pack_mask cells) i
       | None -> vnth_lmap_upto (pk_boxed h ty cells) n i)
  end
#pop-options

(* The fall-back's read: a column of another type or length, or none, packed from its cells
   padded or cut to the frame's length. *)
let column_read (h:host) (n:nat) (ty:ftype) (column:option ccolumn) (i:nat) : Tot cell =
  match column with
  | Some c -> if ctype c = ty && clength c = n then core_cell h c i else pack_cell h ty (pad n (core_cells h c)) i
  | None -> pack_cell h ty (pad n []) i

let column_validity (c:ccolumn) : Tot validity =
  match c.cdata with
  | DInts _ v -> v
  | DFloats _ v -> v
  | DBools _ v -> v
  | DStrs _ v -> v
  | DDates _ v -> v
  | DStamps _ _ _ v -> v
  | DDecimals _ v -> v

let of_column_shape (h:host) (n:nat) (ty:ftype) (column:option ccolumn)
  : Lemma (ensures vlen (of_column h n ty column) == n /\ vec_wf (of_column h n ty column) /\
                   (scale_admits h ==> decs_ok_vec h (of_column h n ty column))) =
  match column with
  | None -> pack_shape h ty (pad n [])
  | Some c ->
    if ctype c <> ty || clength c <> n then begin
      let a = core_cells h c in
      (if len a = n then pad_id n a else ());
      pack_shape h ty (pad n a)
    end else begin
      mask_of_reads n (column_validity c) 0;
      match c.cdata with
      | DDecimals v va ->
        of_decimal_texts_shape h v (mask_of n va);
        FStar.Classical.move_requires (of_decimal_texts_ok h v) (mask_of n va)
      | _ -> ()
    end

let of_column_reads (h:host) (n:nat) (ty:ftype) (column:option ccolumn) (i:nat)
  : Lemma (requires i < n)
          (ensures cell_at h (of_column h n ty column) i == column_read h n ty column i) =
  match column with
  | None -> pack_reads h ty (pad n []) i
  | Some c ->
    if ctype c <> ty || clength c <> n then begin
      let a = core_cells h c in
      (if len a = n then pad_id n a else ());
      pack_reads h ty (pad n a) i
    end else view_reads h n ty c i

(* One logical row is one physical row read across the vectors. *)
let rec vnth_rows_at (h:host) (f:dframe{dwf f}) (ps:list nat) (i:nat)
  : Lemma (ensures vnth (rows_at h f ps) i == (match vnth ps i with Some p -> Some (row_at h f p) | None -> None))
          (decreases ps) =
  match ps with
  | [] -> ()
  | _ :: t -> if i = 0 then () else vnth_rows_at h f t (i - 1)

(* Cell `(i, j)` of the read-back. *)
let cell_ij (h:host) (f:dframe{dwf f}) (i j:nat) : Tot (option cell) =
  match vnth (rows_of h f) i with
  | Some r -> vnth r j
  | None -> None

(* THE READ-BACK IS TOTAL AND IS THE VECTORS: a well-formed frame reads back as `rows` rows of the
   schema's width, and cell `(i, j)` is vector `j` at the physical row the selection gives logical
   row `i` (`Frame.rowsOf` through `Vec.cellAt`). *)
let rows_of_reads (h:host) (f:dframe) (i j:nat)
  : Lemma (requires dwf f)
          (ensures len (rows_of h f) == rows f /\ all_width (len f.fcols) (rows_of h f) /\
                   cell_ij h f i j ==
                     (if i < rows f then
                        (match vnth f.vecs j with Some v -> Some (cell_at h v (at_or 0 (physical f) i)) | None -> None)
                      else None)) =
  vnth_rows_at h f (physical f) i;
  vnth_some_lt (physical f) i;
  vnth_lmap (read_at h (at_or 0 (physical f) i)) f.vecs j

let vnth_upto (n i:nat) : Lemma (ensures vnth (upto n) i == (if i < n then Some i else None)) =
  vnth_upto_from 0 n i

let rec lfor_all_lmap_column_vec (h:host) (n:nat) (cs:list ccolumn) (sch:list (string & ftype))
  : Lemma (ensures lfor_all (vec_ok n) (lmap (column_vec h n cs) sch) /\
                   (scale_admits h ==> decs_ok h (lmap (column_vec h n cs) sch)))
          (decreases sch) =
  match sch with
  | [] -> ()
  | fld :: t -> of_column_shape h n (snd fld) (try_column (fst fld) cs); lfor_all_lmap_column_vec h n cs t

(* `Frame.ofTable` builds a well-formed frame, and its decimal vectors carry their texts' readings. *)
let of_table_wf (h:host) (t:ctable)
  : Lemma (ensures dwf (of_table h t) /\ (scale_admits h ==> decs_ok h (of_table h t).vecs)) =
  lfor_all_lmap_column_vec h (row_count t) t.tcolumns t.tschema

(* THEOREM — `Frame.ofTable` READS BACK THE TABLE. Cell `(i, j)` of the frame of a table is, for a
   schema field whose column the table holds at the field's type and the table's length, that
   column's cell — Core's `Column.cell`; for any other field, what the fall-back packs from the
   column's cells. Total: every logical row of the table is a row of the frame. *)
let of_table_reads (h:host) (t:ctable) (i j:nat)
  : Lemma (requires i < row_count t /\ j < len t.tschema)
          (ensures dwf (of_table h t) /\ rows (of_table h t) == row_count t /\
                   cell_ij h (of_table h t) i j ==
                     (match vnth t.tschema j with
                      | Some fld -> Some (column_read h (row_count t) (snd fld) (try_column (fst fld) t.tcolumns) i)
                      | None -> None)) =
  let n = row_count t in
  of_table_wf h t;
  rows_of_reads h (of_table h t) i j;
  vnth_upto n i;
  vnth_lmap (column_vec h n t.tcolumns) t.tschema j;
  match vnth t.tschema j with
  | Some fld -> of_column_reads h n (snd fld) (try_column (fst fld) t.tcolumns) i
  | None -> ()

(* A table whose every schema field names a column of the field's type and the table's length —
   what Core's `Table.validate` admits. *)
let typed_in (t:ctable) (fld:string & ftype) : Tot bool =
  match try_column (fst fld) t.tcolumns with
  | Some c -> ctype c = snd fld && clength c = row_count t
  | None -> false

let table_typed (t:ctable) : Tot bool = lfor_all (typed_in t) t.tschema

(* THEOREM — THE ABSTRACTION, at the table door. Over a table every schema field of which holds its
   column at its type and the table's length, the frame's cell `(i, j)` IS Core's `Column.cell i` of
   column `j` — the frame means exactly the table's logical rows, at every column type, with a
   validity mask or `AllValid`. *)
let of_table_reads_columns (h:host) (t:ctable) (i j:nat)
  : Lemma (requires table_typed t /\ i < row_count t /\ j < len t.tschema)
          (ensures dwf (of_table h t) /\
                   (match vnth t.tschema j with
                    | Some fld ->
                      (match try_column (fst fld) t.tcolumns with
                       | Some c -> cell_ij h (of_table h t) i j == Some (core_cell h c i)
                       | None -> False)
                    | None -> False)) =
  of_table_reads h t i j;
  vnth_some_lt t.tschema j;
  lfor_all_nth (typed_in t) t.tschema j

let rec vnth_lmapi_from (#a #b:Type0) (k:nat) (g:nat -> a -> b) (l:list a) (i:nat)
  : Lemma (ensures vnth (lmapi_from k g l) i == (match vnth l i with Some x -> Some (g (k + i) x) | None -> None))
          (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> if i = 0 then () else vnth_lmapi_from (k + 1) g t (i - 1)

let rec len_lmapi_from (#a #b:Type0) (k:nat) (g:nat -> a -> b) (l:list a)
  : Lemma (ensures len (lmapi_from k g l) == len l) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> len_lmapi_from (k + 1) g t

let rec lfor_all_row_vecs (h:host) (rs:list (list cell)) (k:nat) (cols:list (string & ftype))
  : Lemma (ensures lfor_all (vec_ok (len rs)) (lmapi_from k (row_vec h rs) cols) /\
                   (scale_admits h ==> decs_ok h (lmapi_from k (row_vec h rs) cols)))
          (decreases cols) =
  match cols with
  | [] -> ()
  | fld :: t -> pack_shape h (snd fld) (column_of rs k); lfor_all_row_vecs h rs (k + 1) t

(* `Frame.ofRows` builds a well-formed frame. *)
let of_rows_wf (h:host) (cols:list (string & ftype)) (rs:list (list cell))
  : Lemma (ensures dwf (of_rows h cols rs) /\ (scale_admits h ==> decs_ok h (of_rows h cols rs).vecs)) =
  len_lmapi_from 0 (row_vec h rs) cols;
  lfor_all_row_vecs h rs 0 cols

(* THEOREM — `Frame.ofRows` READS BACK AS ITS ROWS, PACKED. Cell `(i, j)` of the frame of rows is
   cell `i` of column `j` as `Vec.pack` carries it under the field's type (`pack_cell`). *)
let of_rows_reads (h:host) (cols:list (string & ftype)) (rs:list (list cell)) (i j:nat)
  : Lemma (requires i < len rs /\ j < len cols)
          (ensures dwf (of_rows h cols rs) /\ rows (of_rows h cols rs) == len rs /\
                   cell_ij h (of_rows h cols rs) i j ==
                     (match vnth cols j with
                      | Some fld -> Some (pack_cell h (snd fld) (column_of rs j) i)
                      | None -> None)) =
  of_rows_wf h cols rs;
  rows_of_reads h (of_rows h cols rs) i j;
  vnth_upto (len rs) i;
  vnth_lmapi_from 0 (row_vec h rs) cols j;
  match vnth cols j with
  | Some fld -> pack_reads h (snd fld) (column_of rs j) i
  | None -> ()

(* A column the type carries exactly: it packs, and every cell is already the cell `pack` reads
   back (an int column of ints, a float column of floats, canonical temporal texts at the column's
   unit, …). *)
let is_normal (h:host) (ty:ftype) (c:cell) : Tot bool = norm_cell h ty c = c

let col_exact (h:host) (ty:ftype) (cells:list cell) : Tot bool = packs h ty cells && lfor_all (is_normal h ty) cells

[@@ noextract_to "FSharp"]
let rows_exact (h:host) (cols:list (string & ftype)) (rs:list (list cell)) : prop =
  forall (j:nat). (match vnth cols j with Some fld -> col_exact h (snd fld) (column_of rs j) | None -> True)

let rec nth_all_width (n:nat) (rs:list (list cell)) (i:nat)
  : Lemma (requires all_width n rs /\ i < len rs)
          (ensures (match vnth rs i with Some r -> len r == n | None -> False)) (decreases rs) =
  match rs with
  | _ :: t -> if i = 0 then () else nth_all_width n t (i - 1)

(* THEOREM — THE ROUND TRIP at the row door: rows the schema's types carry exactly come back from
   `Frame.ofRows` then `Frame.rowsOf` as they went in, cell for cell and row for row. *)
let of_rows_exact (h:host) (cols:list (string & ftype)) (rs:list (list cell))
  : Lemma (requires all_width (len cols) rs /\ rows_exact h cols rs)
          (ensures dwf (of_rows h cols rs) /\ rows_of h (of_rows h cols rs) == rs) =
  let f = of_rows h cols rs in
  of_rows_wf h cols rs;
  introduce forall (i:nat). vnth (rows_of h f) i == vnth rs i
  with begin
    rows_of_reads h f i 0;
    vnth_rows_at h f (physical f) i;
    vnth_upto (len rs) i;
    vnth_some_lt rs i;
    if i < len rs then begin
      nth_all_width (len cols) rs i;
      let r = Some?.v (vnth rs i) in
      let r' = row_at h f i in
      introduce forall (j:nat). vnth r' j == vnth r j
      with begin
        rows_of_reads h f i j;
        vnth_some_lt r j;
        vnth_some_lt cols j;
        if j < len cols then begin
          of_rows_reads h cols rs i j;
          let cells = column_of rs j in
          vnth_lmap (cell_of_row j) rs i;
          vnth_some_lt cells i;
          (match vnth cols j with
           | Some fld -> lfor_all_nth (is_normal h (snd fld)) cells i
           | None -> ())
        end else begin
          len_lmapi_from 0 (row_vec h rs) cols;
          vnth_some_lt r' j
        end
      end;
      list_ext r' r
    end else ()
  end;
  list_ext (rows_of h f) rs

(* ---- the boundary out ---- *)

let gather_shape (v:vec) (phys:list nat)
  : Lemma (ensures vlen (gather v phys) == len phys /\ vec_wf (gather v phys)) = ()

(* `Vec.gather` reads row `i` of the gathered vector as the source's row `phys[i]`. *)
let gather_reads (h:host) (v:vec) (phys:list nat) (i:nat)
  : Lemma (requires vec_wf v /\ i < len phys /\ at_or 0 phys i < vlen v)
          (ensures cell_at h (gather v phys) i == cell_at h v (at_or 0 phys i)) =
  let p = at_or 0 phys i in
  vnth_some_lt phys i;
  vnth_lmap (bit (match v with
                     | VInts _ m | VFloats _ m | VBools _ m | VStrs _ _ m | VDecs _ _ _ m | VDates _ m -> m
                     | VStamps _ _ _ m -> m | VCells _ -> [])) phys i;
  match v with
  | VInts a _ -> vnth_lmap (at_or 0 a) phys i; vnth_some_lt a p
  | VFloats a _ -> vnth_lmap (at_or "0" a) phys i; vnth_some_lt a p
  | VBools a _ -> vnth_lmap (at_or false a) phys i; vnth_some_lt a p
  | VStrs _ a _ -> vnth_lmap (at_or "" a) phys i; vnth_some_lt a p
  | VDecs a _ t _ -> vnth_lmap (at_or "" t) phys i; vnth_some_lt t p
  | VDates a _ -> vnth_lmap (at_or 0 a) phys i; vnth_some_lt a p
  | VStamps _ s fr _ ->
    vnth_lmap (at_or 0 s) phys i; vnth_some_lt s p;
    (match fr with Some fs -> vnth_lmap (at_or 0 fs) phys i | None -> ())
  | VCells a -> vnth_lmap (at_or Null a) phys i; vnth_some_lt a p

let present_validity_of (m:list bool) (i:nat)
  : Lemma (ensures (i < len m ==> present (validity_of m) i == bit m i)) =
  if lfor_all is_true m && i < len m then lfor_all_nth is_true m i else ()

(* THEOREM — `Vec.toColumn` IS THE VECTOR. The Core column a typed vector becomes (its storage
   adopted, its mask normalised by `Validity.ofVector` — `AllValid` where every row is present) has
   the vector's length and reads, through `Column.cell`, every cell the vector reads. *)
let to_column_reads (h:host) (fld:string & ftype) (v:vec) (c:ccolumn) (i:nat)
  : Lemma (requires vec_wf v /\ to_column fld v == Some c)
          (ensures clength c == vlen v /\ c.cname == fst fld /\ core_cell h c i == cell_at h v i) =
  match v with
  | VInts a m -> present_validity_of m i; vnth_some_lt a i
  | VFloats a m -> present_validity_of m i; vnth_some_lt a i
  | VBools a m -> present_validity_of m i; vnth_some_lt a i
  | VStrs _ a m -> present_validity_of m i; vnth_some_lt a i
  | VDecs a _ t m -> present_validity_of m i; vnth_some_lt t i
  | VDates a m -> present_validity_of m i; vnth_some_lt a i
  | VStamps _ s _ m -> present_validity_of m i; vnth_some_lt s i
  | VCells _ -> ()

let rec vnth_zip_cols (fs:list (string & ftype)) (vs:list vec) (j:nat)
  : Lemma (requires len fs == len vs)
          (ensures vnth (zip_cols fs vs) j ==
                   (match vnth fs j, vnth vs j with Some fl, Some v -> Some (to_column fl v) | _ -> None))
          (decreases fs) =
  match fs, vs with
  | _ :: ft, _ :: vt -> if j = 0 then () else vnth_zip_cols ft vt (j - 1)
  | _ -> ()

let rec len_zip_cols (fs:list (string & ftype)) (vs:list vec)
  : Lemma (requires len fs == len vs) (ensures len (zip_cols fs vs) == len fs) (decreases fs) =
  match fs, vs with
  | _ :: ft, _ :: vt -> len_zip_cols ft vt
  | _ -> ()

let rec all_some_nth (#a:Type0) (l:list (option a)) (r:list a) (j:nat)
  : Lemma (requires all_some l == Some r)
          (ensures len r == len l /\ vnth l j == (match vnth r j with Some x -> Some (Some x) | None -> None))
          (decreases l) =
  match l with
  | [] -> ()
  | Some x :: t -> let Some r' = all_some t in if j = 0 then all_some_nth t r' 0 else all_some_nth t r' (j - 1)

(* THEOREM — `Frame.toTable` WRITES EXACTLY THE LOGICAL ROWS. The table a well-formed frame of typed
   vectors becomes has the frame's schema, one column per field, each the frame's row count long,
   and column `j`'s cell `i` is the frame's read-back cell `(i, j)` — through the selection, by one
   typed gather per column. *)
let to_table_reads (h:host) (f:dframe) (t:ctable) (i j:nat)
  : Lemma (requires dwf f /\ to_table f == Some t /\ i < rows f /\ j < len f.fcols)
          (ensures t.tschema == f.fcols /\ len t.tcolumns == len f.fcols /\
                   (match vnth t.tcolumns j with
                    | Some c -> clength c == rows f /\ Some (core_cell h c i) == cell_ij h f i j
                    | None -> False)) =
  let dense = match f.sel with None -> f.vecs | Some s -> lmap (gather_by s) f.vecs in
  let Some cs = all_some (zip_cols f.fcols dense) in
  len_zip_cols f.fcols dense;
  all_some_nth (zip_cols f.fcols dense) cs j;
  vnth_zip_cols f.fcols dense j;
  vnth_some_lt f.fcols j;
  vnth_some_lt f.vecs j;
  lfor_all_nth (vec_ok f.count) f.vecs j;
  rows_of_reads h f i j;
  let fld = Some?.v (vnth f.fcols j) in
  let v = Some?.v (vnth f.vecs j) in
  vnth_some_lt cs j;
  match vnth cs j with
  | None -> ()
  | Some c ->
    match f.sel with
    | None ->
      vnth_upto f.count i;
      to_column_reads h fld v c i
    | Some s ->
      vnth_lmap (gather_by s) f.vecs j;
      vnth_some_lt s i;
      lfor_all_nth (in_range f.count) s i;
      gather_shape v s;
      gather_reads h v s i;
      to_column_reads h fld (gather v s) c i

(* ---- the prepared source ---- *)

(* `DataFrame.toTable (DataFrame.prepare t)` is `t` itself, and the prepared frame is `Frame.ofTable t`
   — whose read-back `of_table_reads` states. *)
let prepare_is_source (h:host) (t:ctable)
  : Lemma (ensures prepared_table (prepare h t) == Some t /\ (prepare h t).pframe == of_table h t) = ()

let rec bad_row_none (w:nat) (k:nat) (rs:list (list cell))
  : Lemma (ensures (None? (bad_row w k rs) <==> all_width w rs)) (decreases rs) =
  match rs with
  | [] -> ()
  | _ :: t -> bad_row_none w (k + 1) t

let rec lfor_all_lmap_gather (n:nat) (s:list nat) (vs:list vec)
  : Lemma (requires lfor_all (vec_ok n) vs) (ensures lfor_all (vec_ok (len s)) (lmap (gather_by s) vs)) (decreases vs) =
  match vs with
  | [] -> ()
  | v :: t -> gather_shape v s; lfor_all_lmap_gather n s t

let rec decs_ok_nth (h:host) (vs:list vec) (j:nat)
  : Lemma (requires decs_ok h vs) (ensures (match vnth vs j with Some v -> decs_ok_vec h v | None -> True)) (decreases vs) =
  match vs with
  | [] -> ()
  | _ :: t -> if j = 0 then () else decs_ok_nth h t (j - 1)

(* `Prepared.ofFrame` keeps the frame's read-back: the dense version is well-formed and, where the
   frame has a column, reads back exactly the frame's logical rows. *)
let of_frame_reads (h:host) (f:dframe) (i j:nat)
  : Lemma (requires dwf f)
          (ensures dwf (of_frame f).pframe /\
                   (len f.vecs > 0 ==> rows (of_frame f).pframe == rows f /\
                                      cell_ij h (of_frame f).pframe i j == cell_ij h f i j)) =
  if len f.vecs = 0 then ()
  else match f.sel with
  | None -> ()
  | Some s ->
    let d = (of_frame f).pframe in
    lfor_all_lmap_gather f.count s f.vecs;
    rows_of_reads h f i j;
    rows_of_reads h d i j;
    vnth_upto (len s) i;
    vnth_lmap (gather_by s) f.vecs j;
    vnth_some_lt s i;
    (if i < len s then lfor_all_nth (in_range f.count) s i else ());
    match vnth f.vecs j with
    | Some v ->
      vnth_some_lt f.vecs j;
      lfor_all_nth (vec_ok f.count) f.vecs j;
      if i < len s then gather_reads h v s i else ()
    | None -> ()

(* THEOREM — `DataFrame.prepareRows` refuses exactly a row whose width is not the schema's, and
   otherwise prepares a frame whose cell `(i, j)` is the row's cell as `Vec.pack` carries it under
   the field's type (`of_rows_reads`; `of_rows_exact` for rows the types carry exactly). *)
let prepare_rows_reads (h:host) (cols:list (string & ftype)) (rs:list (list cell)) (i j:nat)
  : Lemma (ensures (Error? (prepare_rows h cols rs) <==> ~(all_width (len cols) rs)) /\
                   (match prepare_rows h cols rs with
                    | Ok p ->
                      dwf p.pframe /\
                      ((len cols > 0 /\ i < len rs /\ j < len cols) ==>
                         cell_ij h p.pframe i j ==
                           (match vnth cols j with Some fld -> Some (pack_cell h (snd fld) (column_of rs j) i) | None -> None))
                    | Error _ -> True)) =
  bad_row_none (len cols) 0 rs;
  of_rows_wf h cols rs;
  len_lmapi_from 0 (row_vec h rs) cols;
  of_frame_reads h (of_rows h cols rs) i j;
  if i < len rs && j < len cols then of_rows_reads h cols rs i j else ()

(* ======================================================================================
   9. THE KERNEL THEOREMS — a filter the kernels answer is `Pipeline.fst`'s row-major `Filter`
      step over the read-back.
   ====================================================================================== *)

let bool_or_null (c:cell) : Tot bool =
  match c with
  | Bool _ -> true
  | Null -> true
  | _ -> false

(* F#: `logical`'s Kleene AND and OR over two `Bool` / `Null` operands. *)
let kleene_and (a b:cell) : Tot cell =
  match a, b with
  | Bool false, _ -> Bool false
  | _, Bool false -> Bool false
  | Bool true, Bool true -> Bool true
  | _ -> Null

let kleene_or (a b:cell) : Tot cell =
  match a, b with
  | Bool true, _ -> Bool true
  | _, Bool true -> Bool true
  | Bool false, Bool false -> Bool false
  | _ -> Null

(* WHAT THE KERNEL THEOREM ASSUMES of `Pipeline.fst`'s cell primitives and of this module's host
   functions — each the reading of a primitive at exactly the cells a kernel leaf meets, and each
   sampled by the differential at production's own: a comparison with `Null` on either side is
   `Null` (`comparison`'s first arm); a comparison of two ints, of two float carriers, of a float
   and an int, of two decimals or of a decimal and an int is `Bool` of `holds` over the ordering
   the kernel computes (`compareCells` over ints; `compareFloat` over carriers, the int read as
   its float; the two decimals' unscaled integers at any scale both read at, the int scaled to it);
   `compareFloat` is antisymmetric; and `And` / `Or` over `Bool` / `Null` are Kleene's
   (`logical`). Nothing is assumed of any other cell. *)
[@@ noextract_to "FSharp"]
let kernel_prims (h:host) (pr:prims) : prop =
  (forall (op:bin_op) (c:cell).
     (match cmp_op_of op with
      | Some _ -> pr.binary op Null c == Ok Null /\ pr.binary op c Null == Ok Null
      | None -> True)) /\
  (forall (op:bin_op) (a b:int).
     (match cmp_op_of op with
      | Some o -> pr.binary op (Int a) (Int b) == Ok (Bool (holds o (int_cmp a b)))
      | None -> True)) /\
  (forall (op:bin_op) (a b:string).
     (match cmp_op_of op with
      | Some o -> pr.binary op (Float a) (Float b) == Ok (Bool (holds o (h.compare_float a b)))
      | None -> True)) /\
  (forall (op:bin_op) (a:string) (x:int).
     (match cmp_op_of op with
      | Some o ->
        pr.binary op (Float a) (Int x) == Ok (Bool (holds o (h.compare_float a (h.float_of_int x)))) /\
        pr.binary op (Int x) (Float a) == Ok (Bool (holds o (h.compare_float (h.float_of_int x) a)))
      | None -> True)) /\
  (forall (op:bin_op) (s:nat) (a b:string) (ka kb:int).
     (h.try_scaled s a == Some ka /\ h.try_scaled s b == Some kb) ==>
     (match cmp_op_of op with
      | Some o -> pr.binary op (Decimal a) (Decimal b) == Ok (Bool (holds o (int_cmp ka kb)))
      | None -> True)) /\
  (forall (op:bin_op) (s:nat) (a:string) (ka:int) (x:int).
     h.try_scaled s a == Some ka ==>
     (match cmp_op_of op with
      | Some o ->
        pr.binary op (Decimal a) (Int x) == Ok (Bool (holds o (int_cmp ka (x * pow10 s)))) /\
        pr.binary op (Int x) (Decimal a) == Ok (Bool (holds o (int_cmp (x * pow10 s) ka)))
      | None -> True)) /\
  (forall (a b:string). h.compare_float a b == 0 - h.compare_float b a) /\
  (forall (a b:cell).
     (bool_or_null a /\ bool_or_null b) ==>
     (pr.binary And a b == Ok (kleene_and a b) /\ pr.binary Or a b == Ok (kleene_or a b)))

let prem_null (h:host) (pr:prims) (op:bin_op) (o:cmp_op) (c:cell)
  : Lemma (requires kernel_prims h pr /\ cmp_op_of op == Some o)
          (ensures pr.binary op Null c == Ok Null /\ pr.binary op c Null == Ok Null) = ()

let prem_int (h:host) (pr:prims) (op:bin_op) (o:cmp_op) (a b:int)
  : Lemma (requires kernel_prims h pr /\ cmp_op_of op == Some o)
          (ensures pr.binary op (Int a) (Int b) == Ok (Bool (holds o (int_cmp a b)))) = ()

let prem_float (h:host) (pr:prims) (op:bin_op) (o:cmp_op) (a b:string)
  : Lemma (requires kernel_prims h pr /\ cmp_op_of op == Some o)
          (ensures pr.binary op (Float a) (Float b) == Ok (Bool (holds o (h.compare_float a b)))) = ()

let prem_float_int (h:host) (pr:prims) (op:bin_op) (o:cmp_op) (a:string) (x:int)
  : Lemma (requires kernel_prims h pr /\ cmp_op_of op == Some o)
          (ensures pr.binary op (Float a) (Int x) == Ok (Bool (holds o (h.compare_float a (h.float_of_int x)))) /\
                   pr.binary op (Int x) (Float a) == Ok (Bool (holds o (h.compare_float (h.float_of_int x) a)))) = ()

let prem_dec (h:host) (pr:prims) (op:bin_op) (o:cmp_op) (s:nat) (a b:string) (ka kb:int)
  : Lemma (requires kernel_prims h pr /\ cmp_op_of op == Some o /\ h.try_scaled s a == Some ka /\ h.try_scaled s b == Some kb)
          (ensures pr.binary op (Decimal a) (Decimal b) == Ok (Bool (holds o (int_cmp ka kb)))) = ()

let prem_dec_int (h:host) (pr:prims) (op:bin_op) (o:cmp_op) (s:nat) (a:string) (ka:int) (x:int)
  : Lemma (requires kernel_prims h pr /\ cmp_op_of op == Some o /\ h.try_scaled s a == Some ka)
          (ensures pr.binary op (Decimal a) (Int x) == Ok (Bool (holds o (int_cmp ka (x * pow10 s)))) /\
                   pr.binary op (Int x) (Decimal a) == Ok (Bool (holds o (int_cmp (x * pow10 s) ka)))) = ()

let prem_antisym (h:host) (pr:prims) (a b:string)
  : Lemma (requires kernel_prims h pr) (ensures h.compare_float a b == 0 - h.compare_float b a) = ()

let prem_logic (h:host) (pr:prims) (a b:cell)
  : Lemma (requires kernel_prims h pr /\ bool_or_null a /\ bool_or_null b)
          (ensures pr.binary And a b == Ok (kleene_and a b) /\ pr.binary Or a b == Ok (kleene_or a b)) = ()

(* `k op v` is `v (flip op) k`: the swapped comparison tests the negated ordering. *)
let holds_neg (o:cmp_op) (c:int) : Lemma (ensures holds o (0 - c) == holds (flip o) c) = ()

let int_cmp_swap (a b:int) : Lemma (ensures int_cmp a b == 0 - int_cmp b a) = ()

(* A kernel bit and an evaluator answer agree: the answer is a `Bool` or `Null` — never an error,
   which is why the kernel path needs no error slot — and it is `Bool true` exactly on a set bit. *)
[@@ noextract_to "FSharp"]
let agrees (r:outcome cell eval_error) (b:bool) : prop =
  match r with
  | Ok c -> bool_or_null c == true /\ (c = Bool true) == b
  | Error _ -> False

#push-options "--fuel 2 --ifuel 2 --z3rlimit 40"
(* One kernel leaf, at one physical row: the comparison kernel's bit is the evaluator's comparison
   of the vector's cell with the constant (on the left where `flipped`). *)
let leaf_eval (h:host) (pr:prims) (f:dframe{dwf f}) (op:bin_op) (o:cmp_op) (i:nat) (c:cell)
              (flipped:bool) (bits:list bool) (p:nat)
  : Lemma (requires kernel_prims h pr /\ decs_ok h f.vecs /\ cmp_op_of op == Some o /\
                    leaf h f (if flipped then flip o else o) i c == Some bits /\ p < f.count)
          (ensures len bits == f.count /\
                   (match vnth f.vecs i with
                    | Some v -> agrees (if flipped then pr.binary op c (cell_at h v p) else pr.binary op (cell_at h v p) c)
                                       (bit bits p)
                    | None -> False)) =
  let o' = if flipped then flip o else o in
  vnth_some_lt f.vecs i;
  decs_ok_nth h f.vecs i;
  match vnth f.vecs i with
  | Some v ->
    lfor_all_nth (vec_ok f.count) f.vecs i;
    prem_null h pr op o c;
    (match v, c with
     | VInts vals mask, Int x ->
       vnth_lmap_upto (cmp_int_at o' vals mask x) f.count p;
       vnth_some_lt vals p;
       let a = at_or 0 vals p in
       prem_int h pr op o a x; prem_int h pr op o x a;
       int_cmp_swap x a; holds_neg o (int_cmp a x)
     | VFloats vals mask, Float x ->
       vnth_lmap_upto (cmp_float_at h o' vals mask x) f.count p;
       vnth_some_lt vals p;
       let a = at_or "0" vals p in
       prem_float h pr op o a x; prem_float h pr op o x a;
       prem_antisym h pr x a; holds_neg o (h.compare_float a x)
     | VFloats vals mask, Int x ->
       vnth_lmap_upto (cmp_float_at h o' vals mask (h.float_of_int x)) f.count p;
       vnth_some_lt vals p;
       let a = at_or "0" vals p in
       prem_float_int h pr op o a x;
       prem_antisym h pr (h.float_of_int x) a; holds_neg o (h.compare_float a (h.float_of_int x))
     | VDecs vals s t mask, Decimal x ->
       (match h.try_scaled s x with
        | Some kx ->
          vnth_lmap_upto (cmp_int_at o' vals mask kx) f.count p;
          vnth_some_lt vals p;
          vnth_some_lt t p;
          let ka = at_or 0 vals p in
          let ta = at_or "" t p in
          if bit mask p then begin
            prem_dec h pr op o s ta x ka kx; prem_dec h pr op o s x ta kx ka;
            int_cmp_swap kx ka; holds_neg o (int_cmp ka kx)
          end else ()
        | None -> ())
     | VDecs vals s t mask, Int x ->
       let kx = x * pow10 s in
       vnth_lmap_upto (cmp_int_at o' vals mask kx) f.count p;
       vnth_some_lt vals p;
       vnth_some_lt t p;
       let ka = at_or 0 vals p in
       let ta = at_or "" t p in
       if bit mask p then begin
         prem_dec_int h pr op o s ta ka x;
         int_cmp_swap kx ka; holds_neg o (int_cmp ka kx)
       end else ()
     | _ -> ())
  | None -> ()
#pop-options

let row_cell (h:host) (f:dframe{dwf f}) (p:nat) (i:nat)
  : Lemma (requires i < len f.vecs)
          (ensures (match vnth f.vecs i with
                    | Some v -> nth (row_at h f p) i == cell_at h v p
                    | None -> False)) =
  vnth_some_lt f.vecs i;
  vnth_lmap (read_at h p) f.vecs i;
  vnth_nth (row_at h f p) i

let index_of_lt_len (x:col_expr) (f:dframe{dwf f})
  : Lemma (ensures (match col_of f x with Some i -> i < len f.vecs | None -> True)) = ()

let rec len_filter_bits (h:host) (env:param_env) (f:dframe) (x:col_expr) (bits:list bool)
  : Lemma (requires filter_bits h env f x == Some bits) (ensures len bits == f.count) (decreases x) =
  match x with
  | Binary And a b | Binary Or a b ->
    let Some ba = filter_bits h env f a in
    let Some bb = filter_bits h env f b in
    len_filter_bits h env f a ba; len_filter_bits h env f b bb;
    len_lmap2 band ba bb; len_lmap2 bor ba bb
  | Binary op a b ->
    (match cmp_op_of op with
     | None -> ()
     | Some o ->
       (match col_of f a, const_of env b with
        | Some i, Some c -> ()
        | _ -> ()))
  | _ -> ()

#push-options "--fuel 2 --ifuel 2 --z3rlimit 40"
(* Every row: the predicate's bitmap bit is the evaluator's answer on the read-back row. *)
let rec bits_eval (h:host) (pr:prims) (env:param_env) (f:dframe{dwf f}) (x:col_expr) (bits:list bool) (p:nat)
  : Lemma (requires kernel_prims h pr /\ decs_ok h f.vecs /\ filter_bits h env f x == Some bits /\ p < f.count)
          (ensures agrees (eval_expr pr env (erase_schema f.fcols) (row_at h f p) x) (bit bits p))
          (decreases x) =
  len_filter_bits h env f x bits;
  match x with
  | Binary And a b ->
    let Some ba = filter_bits h env f a in
    let Some bb = filter_bits h env f b in
    bits_eval h pr env f a ba p; bits_eval h pr env f b bb p;
    len_filter_bits h env f a ba; len_filter_bits h env f b bb;
    vnth_lmap2 band ba bb p;
    vnth_some_lt ba p; vnth_some_lt bb p;
    (match eval_expr pr env (erase_schema f.fcols) (row_at h f p) a,
           eval_expr pr env (erase_schema f.fcols) (row_at h f p) b with
     | Ok ca, Ok cb -> prem_logic h pr ca cb
     | _ -> ())
  | Binary Or a b ->
    let Some ba = filter_bits h env f a in
    let Some bb = filter_bits h env f b in
    bits_eval h pr env f a ba p; bits_eval h pr env f b bb p;
    len_filter_bits h env f a ba; len_filter_bits h env f b bb;
    vnth_lmap2 bor ba bb p;
    vnth_some_lt ba p; vnth_some_lt bb p;
    (match eval_expr pr env (erase_schema f.fcols) (row_at h f p) a,
           eval_expr pr env (erase_schema f.fcols) (row_at h f p) b with
     | Ok ca, Ok cb -> prem_logic h pr ca cb
     | _ -> ())
  | Binary op a b ->
    let Some o = cmp_op_of op in
    (match col_of f a, const_of env b with
     | Some i, Some c ->
       index_of_lt_len a f;
       row_cell h f p i;
       leaf_eval h pr f op o i c false bits p
     | _ ->
       let Some c = const_of env a in
       let Some i = col_of f b in
       index_of_lt_len b f;
       row_cell h f p i;
       leaf_eval h pr f op o i c true bits p)
#pop-options

let rec lfor_all_lfilter (#a:Type0) (g:a -> bool) (k:a -> bool) (l:list a)
  : Lemma (requires lfor_all g l) (ensures lfor_all g (lfilter k l)) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> lfor_all_lfilter g k t

let rec lfor_all_upto_from (n k:nat)
  : Lemma (ensures lfor_all (in_range n) (upto_from k n)) (decreases (if k >= n then 0 else n - k)) =
  if k >= n then () else lfor_all_upto_from n (k + 1)

(* The rows a frame reads at physical rows `ps` depend on its vectors alone. *)
let rec rows_at_vecs (h:host) (f:dframe{dwf f}) (g:dframe{dwf g}) (ps:list nat)
  : Lemma (requires f.vecs == g.vecs /\ f.fcols == g.fcols) (ensures rows_at h f ps == rows_at h g ps) (decreases ps) =
  match ps with
  | [] -> ()
  | _ :: t -> rows_at_vecs h f g t

#push-options "--fuel 2 --ifuel 1"
(* `Pipeline.filter_rows` over the rows the kernel's bitmap was computed on keeps exactly the rows
   whose bit is set, in order, and refuses none. *)
let rec filter_rows_kernel (h:host) (pr:prims) (env:param_env) (f:dframe{dwf f}) (pred:col_expr)
                           (bits:list bool) (ps:list nat)
  : Lemma (requires kernel_prims h pr /\ decs_ok h f.vecs /\ filter_bits h env f pred == Some bits /\
                    lfor_all (in_range f.count) ps)
          (ensures filter_rows pr env (erase_schema f.fcols) (rows_at h f ps) pred ==
                   Ok (rows_at h f (lfilter (is_set bits) ps)))
          (decreases ps) =
  match ps with
  | [] -> ()
  | p :: t ->
    bits_eval h pr env f pred bits p;
    filter_rows_kernel h pr env f pred bits t
#pop-options

(* THEOREM — THE FILTER KERNEL IS THE ROW-MAJOR FILTER. Where `filterBits` answers a bitmap (a
   predicate of `And` / `Or` over comparisons of an int, float or decimal vector with a constant),
   the kernel branch of `evalFilterChecked` — the bitmap read back through the frame's selection —
   is `Pipeline.fst`'s `Filter` step over the read-back: the same rows, in the same order, under the
   same schema, and never an error. Over every frame the boundary builds, under `kernel_prims`; the
   decimal leaf reads the carried integers, which `of_table_wf` / `of_rows_wf` show the boundary
   builds as the texts' readings (`decs_ok`) under `scale_admits`. *)
let filter_kernel_is_step (h:host) (pr:prims) (other:other_fn) (env:param_env) (f:dframe) (pred:col_expr) (f':dframe)
  : Lemma (requires dwf f /\ kernel_prims h pr /\ decs_ok h f.vecs /\ filter_kernel h env f pred == Some f')
          (ensures dwf f' /\ f'.vecs == f.vecs /\ f'.fcols == f.fcols /\
                   eval_step pr other env (logical h f) (Filter pred) == Ok (logical h f')) =
  let Some bits = filter_bits h env f pred in
  len_filter_bits h env f pred bits;
  match f.sel with
  | None ->
    lfor_all_upto_from f.count 0;
    lfor_all_lfilter (in_range f.count) (is_set bits) (upto f.count);
    filter_rows_kernel h pr env f pred bits (upto f.count);
    rows_at_vecs h f f' (lfilter (is_set bits) (upto f.count))
  | Some s ->
    lfor_all_lfilter (in_range f.count) (is_set bits) s;
    filter_rows_kernel h pr env f pred bits s;
    rows_at_vecs h f f' (lfilter (is_set bits) s)
