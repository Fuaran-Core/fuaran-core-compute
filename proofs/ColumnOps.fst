(*
   ColumnOps — the compute strand's op algebra gets the Preservation theorem: `ColumnOps.apply`,
   `canApply`, `invert` and `Diff.toOps` over a table with a validity mask, clause for clause,
   with the five clauses Phase 138 proved for trees proved here for columns (fuaran-core
   Phase 176).

   WHAT IS MODELLED. `src/Fuaran.Core.Column.Ops/ColumnOps.fs` — the six-case `ColumnOp`, the
   eight-case `ColumnRejection`, and the four functions over the `Fuaran.Core.Column` `Table`: a
   schema (an ordered `(name, type)` list), the columns (each a name, a type and a cell list), where
   `Null` IS the validity mask and a present cell is its type and an OPAQUE CARRIER. Nothing in the
   algebra looks inside a carrier: `cellFits` reads a cell's type and `Diff.toOps` compares cell
   lists for equality, and those two are all the model needs — so `Present ty carrier` with the
   carrier a string is the whole of a cell here. `ApplyTransform` runs a `DataFrame` pipeline the
   algebra does not interpret: the pipeline is an opaque token and the evaluator is a PARAMETER
   (`ev`), exactly as Phase 135 made `float i` a parameter and Phase 138 made `ReplaceChildren`
   one. Row indices are `int` (a negative row is a real input the F# refuses); lengths are `nat`.

   WHAT IS PROVED, over the six operations, any table and any evaluator:

     - `apply_total` — `apply` reaches exactly one outcome on every input (the type of the
       function, discharged by construction) and WHICH rejection each clause can raise is
       characterised per clause (`raisable`). The corollary read off it: `NotInvertible` is
       unreachable from `apply` — it is `invert`'s alone.
     - `reject_identity` — a refused step leaves the caller holding the input table; for a script
       (`applyAll`) it is the all-or-nothing discipline at an arbitrary failure position, and a
       rejected `ApplyTransform` is inside the quantifier like every other operation
       (`reject_identity_transform` states it on its own).
     - `canapply_agrees` — the dry run answers exactly what the mutating call answers, verdict
       and rejection alike.
     - `apply_preserves_wf` — every accepted STRUCTURAL operation preserves table well-formedness
       (schema coherent with the columns, column names distinct, every column the row count long,
       every cell fitting its column's type). `ApplyTransform` preserves it under the one premise
       the evaluator is asked for (`ev_preserves_wf`), which is where the abstraction sits.
     - `invert_roundtrip` — on the four operations `invert` is defined for, the inverse of an
       accepted operation is accepted at the result and restores the input EXACTLY, on a
       well-formed table. The partial cases are CHARACTERISED beside it: `AppendRows` and
       `ApplyTransform` are `NotInvertible` unconditionally; `invert` on `SetCell`, `SetColumn`
       and `RemoveColumn` refuses exactly the pre-states `apply` refuses for the column and row it
       reads; and `invert` on `InsertColumn` reads NOTHING from the pre-state — the finding
       `invert_insert_reads_nothing` and its consequence `refused_insert_inverse_is_live`: the
       "inverse" of a REFUSED insert is a remove that succeeds at the pre-state and takes the
       column that was already there. Reported, not fixed here.
     - `diff_applicable` — every script `Diff.toOps` emits applies to the table it was computed
       against and yields the other table, on well-formed tables: the column-granular branch by
       replacing each changed column in place, the rebuild branch by removing every column and
       inserting every new one in order.

   WHAT IS NOT CLAIMED. Anything about a carrier: two cells with equal carriers are equal here,
   which is what production's structural equality says too except at a NaN float (the
   `column-cell-carrier-opaque` row). Anything about the pipeline evaluator beyond the premise
   named above (the `column-transform-evaluator-abstract` row). The wire codec, `changeOf` and
   the `StreamWitness`, which are outside the algebra.

   HOW TO READ IT. Every definition names its F# counterpart, as in `Preservation.fst`. The module
   is SELF-CONTAINED like `Chain.fst` and `JsonParse.fst` — it opens nothing, restates `outcome`
   and the few list helpers it needs, and extracts beside the other models into the same oracle
   assembly sharing only `Prims.fs`.

   Apache-2.0, like everything beside it.
*)
module ColumnOps

(* ======================================================================================
   0. The list helpers, self-contained, each naming the FSharp.Core function it stands for.
   ====================================================================================== *)

(* F#: `Result<'a, 'e>`. *)
type outcome (a e:Type) =
  | Ok    : a -> outcome a e
  | Error : e -> outcome a e

(* F#: `List.length`. *)
let rec len (#a:Type) (l:list a) : Tot nat =
  match l with
  | [] -> 0
  | _ :: t -> 1 + len t

(* F#: `List.item i` (as an option, so the model stays total; every caller guards the index). *)
let rec nth (#a:Type) (i:nat) (l:list a) : Tot (option a) =
  match l with
  | [] -> None
  | x :: t -> if i = 0 then Some x else nth (i - 1) t

(* F#: `List.mapi (fun j c -> if j = i then v else c)`. *)
let rec set_at (#a:Type) (i:nat) (v:a) (l:list a) : Tot (list a) =
  match l with
  | [] -> []
  | x :: t -> if i = 0 then v :: t else x :: set_at (i - 1) v t

(* F#: `List.truncate i`. *)
let rec take (#a:Type) (i:nat) (l:list a) : Tot (list a) =
  match l with
  | [] -> []
  | x :: t -> if i = 0 then [] else x :: take (i - 1) t

(* F#: `List.skip (min i (List.length xs))`. *)
let rec drop (#a:Type) (i:nat) (l:list a) : Tot (list a) =
  match l with
  | [] -> []
  | x :: t -> if i = 0 then l else drop (i - 1) t

(* F#: `@`. *)
let rec app (#a:Type) (l m:list a) : Tot (list a) =
  match l with
  | [] -> m
  | x :: t -> x :: app t m

(* F#: `List.rev`. *)
let rec rev (#a:Type) (l:list a) : Tot (list a) =
  match l with
  | [] -> []
  | x :: t -> app (rev t) [x]

(* F#: `insertColumnAt`'s local `insertAt i x xs` — `before @ [x] @ after`. *)
let insert_at (#a:Type) (i:nat) (x:a) (l:list a) : Tot (list a) =
  app (take i l) (x :: drop i l)

(* F#: `Set.contains n names` / `List.exists (fun c -> c.Name = name)` on a name list. *)
let rec mem (x:string) (l:list string) : Tot bool =
  match l with
  | [] -> false
  | y :: t -> x = y || mem x t

(* ======================================================================================
   1. The data — `Fuaran.Core.Column`'s `ColumnType`, `Cell`, `Column`, `Schema`, `Table`.
   ====================================================================================== *)

(* F#: `ColumnType`. *)
type coltype =
  | IntType
  | FloatType
  | BoolType
  | StringType
  | DateType
  | TimestampType

(* F#: `ColumnType.tag`. *)
let tag (t:coltype) : Tot string =
  match t with
  | IntType -> "int"
  | FloatType -> "float"
  | BoolType -> "bool"
  | StringType -> "string"
  | DateType -> "date"
  | TimestampType -> "timestamp"

(* F#: `Cell`. `Null` is the validity mask's absent marker; a present cell is its type and an
   opaque carrier — the six value constructors collapsed to the one thing the algebra reads. *)
type cell =
  | Null    : cell
  | Present : ty:coltype -> carrier:string -> cell

(* F#: `Cell.typeOf`. *)
let type_of (c:cell) : Tot (option coltype) =
  match c with
  | Null -> None
  | Present ty _ -> Some ty

(* F#: `Column`. *)
type column = { name: string; ty: coltype; cells: list cell }

(* F#: `Table` — `Schema` is `(string * ColumnType) list`. *)
type table = { schema: list (string & coltype); columns: list column }

(* F#: `Table.columnNames` — read from the SCHEMA, not the columns. *)
let rec names_of (s:list (string & coltype)) : Tot (list string) =
  match s with
  | [] -> []
  | (n, _) :: r -> n :: names_of r

let names (t:table) : Tot (list string) = names_of t.schema

(* F#: `Table.rowCount` — the first column's length, or 0 for a schema-only table. *)
let row_count (t:table) : Tot nat =
  match t.columns with
  | c :: _ -> len c.cells
  | [] -> 0

(* F#: `t.Columns |> List.tryFind (fun c -> c.Name = name)`. *)
let rec find_col (n:string) (cs:list column) : Tot (option column) =
  match cs with
  | [] -> None
  | c :: r -> if c.name = n then Some c else find_col n r

(* F#: `t.Columns |> List.tryFindIndex (fun c -> c.Name = name)`. *)
let rec find_index (n:string) (cs:list column) : Tot (option nat) =
  match cs with
  | [] -> None
  | c :: r -> if c.name = n then Some 0 else
              (match find_index n r with Some i -> Some (i + 1) | None -> None)

(* F#: `t.Columns |> List.exists (fun c -> c.Name = name)`. *)
let exists_col (n:string) (cs:list column) : Tot bool = Some? (find_col n cs)

(* ======================================================================================
   2. The operations and the rejections — `ColumnOp` and `ColumnRejection`.
   ====================================================================================== *)

(* F#: `ColumnOp`. The `ApplyTransform` payload is the pipeline as an opaque token: the algebra
   never reads it, only hands it to the evaluator. *)
type op =
  | SetCell        : column:string -> row:int -> value:cell -> op
  | SetColumn      : column -> op
  | InsertColumn   : index:int -> column -> op
  | RemoveColumn   : name:string -> op
  | AppendRows     : rows:list (list (string & cell)) -> op
  | ApplyTransform : pipeline:string -> op

(* F#: `ColumnRejection`. *)
type rejection =
  | NoSuchColumn          : name:string -> available:list string -> rejection
  | DuplicateColumn       : name:string -> rejection
  | RowOutOfRange         : row:int -> rowCount:nat -> rejection
  | CellTypeMismatch      : column:string -> expected:string -> got:string -> rejection
  | ColumnLengthMismatch  : column:string -> expected:nat -> got:nat -> rejection
  | RowShapeUnknownColumn : name:string -> available:list string -> rejection
  | TransformRejected     : detail:string -> rejection
  | NotInvertible         : op:string -> rejection

(* F#: `DataFrame.evalPipeline pipeline t`, with `DataFrame.errorString` already applied to its
   error — the evaluator as the algebra sees it, a parameter of every function below. *)
type evaluator = string -> table -> outcome table string

(* ======================================================================================
   3. The small Table helpers — `ColumnOps`'s private block, clause for clause.
   ====================================================================================== *)

(* F#: `cellTypeName`. *)
let cell_type_name (c:cell) : Tot string =
  match type_of c with
  | Some t -> tag t
  | None -> "null"

(* F#: `cellFits` — a cell fits a column type iff it is `Null` or exactly that type. *)
let cell_fits (col_name:string) (ty:coltype) (c:cell) : Tot (outcome unit rejection) =
  match c with
  | Null -> Ok ()
  | _ ->
    (match type_of c with
     | Some t -> if t = ty then Ok () else Error (CellTypeMismatch col_name (tag ty) (cell_type_name c))
     | None -> Error (CellTypeMismatch col_name (tag ty) (cell_type_name c)))

(* F#: `cellsFit` — `List.tryPick` over the column's cells: the FIRST misfit, else `Ok`. *)
let rec cells_fit_from (col_name:string) (ty:coltype) (cs:list cell) : Tot (outcome unit rejection) =
  match cs with
  | [] -> Ok ()
  | c :: r ->
    (match cell_fits col_name ty c with
     | Error e -> Error e
     | Ok () -> cells_fit_from col_name ty r)

let cells_fit (col:column) : Tot (outcome unit rejection) = cells_fit_from col.name col.ty col.cells

(* F#: `replaceColumn`. *)
let rec replace_schema (n:string) (ty:coltype) (s:list (string & coltype)) : Tot (list (string & coltype)) =
  match s with
  | [] -> []
  | (m, t) :: r -> (if m = n then (m, ty) else (m, t)) :: replace_schema n ty r

let rec replace_cols (n:string) (nc:column) (cs:list column) : Tot (list column) =
  match cs with
  | [] -> []
  | c :: r -> (if c.name = n then nc else c) :: replace_cols n nc r

let replace_column (n:string) (nc:column) (t:table) : Tot table =
  { schema = replace_schema n nc.ty t.schema; columns = replace_cols n nc t.columns }

(* F#: `insertColumnAt` — `clamp` is `max 0 (min i (List.length t.Columns))`, and the SAME clamped
   index is used for the schema and for the columns. *)
let clamp (i:int) (n:nat) : Tot nat = if i < 0 then 0 else if i > n then n else i

let insert_column_at (index:int) (col:column) (t:table) : Tot table =
  let i = clamp index (len t.columns) in
  { schema = insert_at i (col.name, col.ty) t.schema; columns = insert_at i col t.columns }

(* F#: `removeColumn`. *)
let rec remove_schema (n:string) (s:list (string & coltype)) : Tot (list (string & coltype)) =
  match s with
  | [] -> []
  | (m, t) :: r -> if m = n then remove_schema n r else (m, t) :: remove_schema n r

let rec remove_cols (n:string) (cs:list column) : Tot (list column) =
  match cs with
  | [] -> []
  | c :: r -> if c.name = n then remove_cols n r else c :: remove_cols n r

let remove_column (n:string) (t:table) : Tot table =
  { schema = remove_schema n t.schema; columns = remove_cols n t.columns }

(* ======================================================================================
   4. `apply` — clause for clause.
   ====================================================================================== *)

(* F#: the `rowFault` closure inside the `AppendRows` arm — the first pair that names an unknown
   column or carries a misfit cell. *)
let rec row_fault (t:table) (row:list (string & cell)) : Tot (option rejection) =
  match row with
  | [] -> None
  | (n, v) :: rest ->
    if not (mem n (names t)) then Some (RowShapeUnknownColumn n (names t))
    else
      (match find_col n t.columns with
       | Some col ->
         (match cell_fits n col.ty v with
          | Error e -> Some e
          | Ok () -> row_fault t rest)
       | None -> Some (RowShapeUnknownColumn n (names t)))

(* F#: `rows |> List.tryPick rowFault`. *)
let rec first_fault (t:table) (rows:list (list (string & cell))) : Tot (option rejection) =
  match rows with
  | [] -> None
  | row :: rest ->
    (match row_fault t row with
     | Some e -> Some e
     | None -> first_fault t rest)

(* F#: `row |> List.tryFind (fun (n, _) -> n = col.Name) |> Option.map snd |> Option.defaultValue Null`. *)
let rec lookup (n:string) (row:list (string & cell)) : Tot cell =
  match row with
  | [] -> Null
  | (m, v) :: rest -> if m = n then v else lookup n rest

(* F#: `rows |> List.map (fun row -> ...)` — the cells appended to the column named `n`. *)
let rec appended (n:string) (rows:list (list (string & cell))) : Tot (list cell) =
  match rows with
  | [] -> []
  | row :: rest -> lookup n row :: appended n rest

(* F#: `t.Columns |> List.map (fun col -> { col with Cells = col.Cells @ appended })`. *)
let rec append_cols (rows:list (list (string & cell))) (cs:list column) : Tot (list column) =
  match cs with
  | [] -> []
  | c :: r -> { c with cells = app c.cells (appended c.name rows) } :: append_cols rows r

(* F#: `ColumnOps.apply` — total; a typed rejection, never a throw. *)
let apply (ev:evaluator) (o:op) (t:table) : Tot (outcome table rejection) =
  match o with
  | SetCell n row value ->
    (match find_col n t.columns with
     | None -> Error (NoSuchColumn n (names t))
     | Some col ->
       let rc = row_count t in
       if row < 0 || row >= rc then Error (RowOutOfRange row rc)
       else
         (match cell_fits n col.ty value with
          | Error e -> Error e
          | Ok () -> Ok (replace_column n { col with cells = set_at row value col.cells } t)))
  | SetColumn nc ->
    (match find_col nc.name t.columns with
     | None -> Error (NoSuchColumn nc.name (names t))
     | Some _ ->
       let rc = row_count t in
       if len nc.cells <> rc then Error (ColumnLengthMismatch nc.name rc (len nc.cells))
       else
         (match cells_fit nc with
          | Error e -> Error e
          | Ok () -> Ok (replace_column nc.name nc t)))
  | InsertColumn index col ->
    if exists_col col.name t.columns then Error (DuplicateColumn col.name)
    else
      let rc = row_count t in
      let has_cols = Cons? t.columns in
      if has_cols && len col.cells <> rc then Error (ColumnLengthMismatch col.name rc (len col.cells))
      else
        (match cells_fit col with
         | Error e -> Error e
         | Ok () -> Ok (insert_column_at index col t))
  | RemoveColumn n ->
    if exists_col n t.columns then Ok (remove_column n t)
    else Error (NoSuchColumn n (names t))
  | AppendRows rows ->
    (match first_fault t rows with
     | Some e -> Error e
     | None -> Ok { t with columns = append_cols rows t.columns })
  | ApplyTransform p ->
    (match ev p t with
     | Ok t' -> Ok t'
     | Error e -> Error (TransformRejected e))

(* F#: `ColumnOps.canApply` — `apply op t |> Result.map ignore`. *)
let can_apply (ev:evaluator) (o:op) (t:table) : Tot (outcome unit rejection) =
  match apply ev o t with
  | Ok _ -> Ok ()
  | Error e -> Error e

(* F#: `ColumnOps.invert` — the inverse that undoes `o` applied to the PRE-state `t`. *)
let invert (o:op) (t:table) : Tot (outcome op rejection) =
  match o with
  | SetCell n row _ ->
    (match find_col n t.columns with
     | None -> Error (NoSuchColumn n (names t))
     | Some col ->
       let rc = row_count t in
       if row < 0 || row >= rc then Error (RowOutOfRange row rc)
       else
         (match nth row col.cells with
          | Some old -> Ok (SetCell n row old)
          | None -> Ok (SetCell n row Null)))   (* unreachable on a uniform table: the row is
                                                   below the first column's length; `List.item`
                                                   would throw here, the model returns `Null` *)
  | SetColumn nc ->
    (match find_col nc.name t.columns with
     | None -> Error (NoSuchColumn nc.name (names t))
     | Some old -> Ok (SetColumn old))
  | InsertColumn _ col -> Ok (RemoveColumn col.name)
  | RemoveColumn n ->
    (match find_index n t.columns with
     | None -> Error (NoSuchColumn n (names t))
     | Some idx ->
       (match nth idx t.columns with
        | Some c -> Ok (InsertColumn idx c)
        | None -> Error (NoSuchColumn n (names t))))   (* unreachable: `find_index` found it *)
  | AppendRows _ -> Error (NotInvertible "AppendRows")
  | ApplyTransform _ -> Error (NotInvertible "ApplyTransform")

(* F#: `ColumnOps.applyAll` — `List.fold` threading the table, short-circuiting on the first
   rejection. *)
let rec apply_all (ev:evaluator) (os:list op) (t:table) : Tot (outcome table rejection) (decreases os) =
  match os with
  | [] -> Ok t
  | o :: r ->
    (match apply ev o t with
     | Ok t' -> apply_all ev r t'
     | Error e -> Error e)

(* F#: `Diff.toOps` — the column-granular branch's `List.choose`. *)
let rec changed_cols (bcs:list column) (acs:list column) : Tot (list op) =
  match acs with
  | [] -> []
  | ac :: r ->
    (match find_col ac.name bcs with
     | Some bc -> if bc.cells <> ac.cells then SetColumn ac :: changed_cols bcs r else changed_cols bcs r
     | None -> changed_cols bcs r)

(* F#: `before.Columns |> List.rev |> List.map (fun c -> RemoveColumn c.Name)`. *)
let rec removes (cs:list column) : Tot (list op) =
  match cs with
  | [] -> []
  | c :: r -> RemoveColumn c.name :: removes r

(* F#: `after.Columns |> List.mapi (fun i c -> InsertColumn(i, c))`. *)
let rec inserts_from (i:nat) (cs:list column) : Tot (list op) (decreases cs) =
  match cs with
  | [] -> []
  | c :: r -> InsertColumn i c :: inserts_from (i + 1) r

(* F#: `ColumnOps.toOps`. *)
let to_ops (before after:table) : Tot (list op) =
  if before.schema = after.schema && row_count before = row_count after
  then changed_cols before.columns after.columns
  else app (removes (rev before.columns)) (inserts_from 0 after.columns)

(* ======================================================================================
   5. THE FIRST THEOREM — totality with its rejection characterisation.
   ====================================================================================== *)

(* Which rejection each clause of `apply` can raise. Eight classes, and no clause reaches
   `NotInvertible`: that one is `invert`'s. *)
let raisable (o:op) (e:rejection) : Tot bool =
  match o, e with
  | SetCell _ _ _, NoSuchColumn _ _            -> true
  | SetCell _ _ _, RowOutOfRange _ _           -> true
  | SetCell _ _ _, CellTypeMismatch _ _ _      -> true
  | SetColumn _, NoSuchColumn _ _              -> true
  | SetColumn _, ColumnLengthMismatch _ _ _    -> true
  | SetColumn _, CellTypeMismatch _ _ _        -> true
  | InsertColumn _ _, DuplicateColumn _        -> true
  | InsertColumn _ _, ColumnLengthMismatch _ _ _ -> true
  | InsertColumn _ _, CellTypeMismatch _ _ _   -> true
  | RemoveColumn _, NoSuchColumn _ _           -> true
  | AppendRows _, RowShapeUnknownColumn _ _    -> true
  | AppendRows _, CellTypeMismatch _ _ _       -> true
  | ApplyTransform _, TransformRejected _      -> true
  | _, _                                       -> false

(* `cells_fit` raises only a `CellTypeMismatch`, and it names the column it was asked about. *)
let rec cells_fit_from_shape (n:string) (ty:coltype) (cs:list cell)
  : Lemma (ensures (match cells_fit_from n ty cs with
                    | Ok () -> True
                    | Error e -> CellTypeMismatch? e /\ CellTypeMismatch?.column e == n))
  = match cs with
    | [] -> ()
    | c :: r -> cells_fit_from_shape n ty r

(* `row_fault` raises only the two `AppendRows` classes. *)
let rec row_fault_shape (t:table) (row:list (string & cell))
  : Lemma (ensures (match row_fault t row with
                    | None -> True
                    | Some e -> RowShapeUnknownColumn? e \/ CellTypeMismatch? e))
  = match row with
    | [] -> ()
    | (n, v) :: rest ->
      if not (mem n (names t)) then ()
      else (match find_col n t.columns with
            | Some col -> (match cell_fits n col.ty v with Error _ -> () | Ok () -> row_fault_shape t rest)
            | None -> ())

let rec first_fault_shape (t:table) (rows:list (list (string & cell)))
  : Lemma (ensures (match first_fault t rows with
                    | None -> True
                    | Some e -> RowShapeUnknownColumn? e \/ CellTypeMismatch? e))
  = match rows with
    | [] -> ()
    | row :: rest -> row_fault_shape t row; first_fault_shape t rest

(* THE FIRST THEOREM. F#: `apply` returns `Result<Table, ColumnRejection>` and never throws — the
   type of `apply` here — and the rejection it returns is one its own clause can produce. *)
let apply_total (ev:evaluator) (o:op) (t:table)
  : Lemma (ensures (match apply ev o t with
                    | Ok _ -> True
                    | Error e -> raisable o e))
  = match o with
    | SetCell _ _ _ -> ()
    | SetColumn nc -> cells_fit_from_shape nc.name nc.ty nc.cells
    | InsertColumn _ col -> cells_fit_from_shape col.name col.ty col.cells
    | RemoveColumn _ -> ()
    | AppendRows rows -> first_fault_shape t rows
    | ApplyTransform _ -> ()

(* The unreachable class, read off the characterisation. *)
let apply_never_not_invertible (ev:evaluator) (o:op) (t:table)
  : Lemma (ensures (match apply ev o t with
                    | Error (NotInvertible _) -> False
                    | _ -> True))
  = apply_total ev o t

(* ======================================================================================
   6. THE SECOND THEOREM — a refused step leaves the input table.
   ====================================================================================== *)

(* The state a caller holds after asking for `o` — the result when accepted, the input when not.
   F#: `match ColumnOps.apply op t with Ok t' -> t' | Error _ -> t`. *)
let state_after (ev:evaluator) (o:op) (t:table) : Tot table =
  match apply ev o t with
  | Ok t' -> t'
  | Error _ -> t

let state_after_all (ev:evaluator) (os:list op) (t:table) : Tot table =
  match apply_all ev os t with
  | Ok t' -> t'
  | Error _ -> t

let rec reject_identity_script (ev:evaluator) (pre:list op) (o:op) (post:list op) (t m:table)
  : Lemma (requires apply_all ev pre t == Ok m /\ Error? (apply ev o m))
          (ensures apply_all ev (app pre (o :: post)) t == apply ev o m /\
                   state_after_all ev (app pre (o :: post)) t == t)
          (decreases pre)
  = match pre with
    | [] -> ()
    | q :: rest ->
      (match apply ev q t with
       | Ok t1 -> reject_identity_script ev rest o post t1 m
       | Error _ -> ())

(* THE SECOND THEOREM. Both halves: the operation's own, and the script's at an arbitrary
   failure position. *)
let reject_identity (ev:evaluator) (o:op) (t:table)
  : Lemma (ensures (Error? (apply ev o t) ==> state_after ev o t == t) /\
                   (forall (pre:list op) (post:list op) (m:table).
                      (apply_all ev pre t == Ok m /\ Error? (apply ev o m)) ==>
                      (apply_all ev (app pre (o :: post)) t == apply ev o m /\
                       state_after_all ev (app pre (o :: post)) t == t)))
  = let aux (pre post:list op) (m:table)
      : Lemma ((apply_all ev pre t == Ok m /\ Error? (apply ev o m)) ==>
               (apply_all ev (app pre (o :: post)) t == apply ev o m /\
                state_after_all ev (app pre (o :: post)) t == t))
      = if apply_all ev pre t = Ok m && Error? (apply ev o m) then
          reject_identity_script ev pre o post t m
        else ()
    in
    FStar.Classical.forall_intro_3 aux

(* The clause the phase named: a rejected `ApplyTransform` — the one operation whose acceptance
   REPLACES the table wholesale — leaves the caller holding the input, alone and inside a script.
   `apply` builds nothing before the evaluator answers, so there is no partial table to escape. *)
let reject_identity_transform (ev:evaluator) (p:string) (t:table)
  : Lemma (ensures (Error? (ev p t) ==>
                      (apply ev (ApplyTransform p) t == Error (TransformRejected (Error?._0 (ev p t))) /\
                       state_after ev (ApplyTransform p) t == t)))
  = ()

(* ======================================================================================
   7. THE THIRD THEOREM — the dry run agrees with the mutating call.
   ====================================================================================== *)

(* F#: "`canApply` is `apply` with the result discarded — they can never disagree." Here it is the
   theorem: same verdict, and on a refusal the SAME rejection. *)
let canapply_agrees (ev:evaluator) (o:op) (t:table)
  : Lemma (ensures (Ok? (can_apply ev o t) <==> Ok? (apply ev o t)) /\
                   (forall (e:rejection). can_apply ev o t == Error e <==> apply ev o t == Error e))
  = ()

(* ======================================================================================
   8. Well-formedness, and THE FOURTH THEOREM — every accepted structural operation keeps it.

      F#: nothing computes this. It is what `Table`'s doc comment states ("column order follows
      the schema, every column the same length") plus what the codec enforces at the wire and
      `Column.create` deliberately does not ("no validation — the codec validates the wire"):
      the schema is the columns' `(name, type)` projection, no two columns share a name, every
      column is the row count long, and every cell fits its column's type. The round trip and the
      diff below are theorems about tables that satisfy it; a table that does not — two columns
      named `a`, a schema entry with no column — is one the F# `apply` still accepts and this
      section says exactly which conclusions it forfeits.
   ====================================================================================== *)

(* F#: `t.Columns |> List.map (fun c -> c.Name)`. *)
let rec col_names (cs:list column) : Tot (list string) =
  match cs with
  | [] -> []
  | c :: r -> c.name :: col_names r

(* The schema a column list PROJECTS — coherence is `t.Schema` being this. *)
let rec schema_of (cs:list column) : Tot (list (string & coltype)) =
  match cs with
  | [] -> []
  | c :: r -> (c.name, c.ty) :: schema_of r

let rec no_dups (l:list string) : Tot bool =
  match l with
  | [] -> true
  | x :: r -> not (mem x r) && no_dups r

(* Every column is `n` cells long. *)
let rec uniform (n:nat) (cs:list column) : Tot bool =
  match cs with
  | [] -> true
  | c :: r -> len c.cells = n && uniform n r

(* Every column's cells fit its type — `cellsFit` green on each. *)
let rec typed (cs:list column) : Tot bool =
  match cs with
  | [] -> true
  | c :: r -> Ok? (cells_fit c) && typed r

let coherent (t:table) : Tot bool = t.schema = schema_of t.columns

let wf (t:table) : Tot bool =
  coherent t && no_dups (col_names t.columns) && uniform (row_count t) t.columns && typed t.columns

(* An operation other than `ApplyTransform` — the five the algebra itself computes. *)
let structural (o:op) : Tot bool = not (ApplyTransform? o)

(* ---- membership and lookup ---- *)

(* A column is found by name exactly when its name is among the column names. *)
let rec find_col_mem (n:string) (cs:list column)
  : Lemma (ensures Some? (find_col n cs) <==> mem n (col_names cs))
  = match cs with
    | [] -> ()
    | c :: r -> if c.name = n then () else find_col_mem n r

let rec find_col_name (n:string) (cs:list column)
  : Lemma (ensures (match find_col n cs with Some c -> c.name == n | None -> True))
  = match cs with
    | [] -> ()
    | c :: r -> if c.name = n then () else find_col_name n r

let rec names_of_schema_of (cs:list column)
  : Lemma (ensures names_of (schema_of cs) == col_names cs)
  = match cs with
    | [] -> ()
    | _ :: r -> names_of_schema_of r

(* On a coherent table `Table.columnNames` (the schema) and the columns' names agree. *)
let names_coherent (t:table)
  : Lemma (requires coherent t) (ensures names t == col_names t.columns)
  = names_of_schema_of t.columns

let rec uniform_find (k:nat) (n:string) (cs:list column)
  : Lemma (requires uniform k cs)
          (ensures (match find_col n cs with Some c -> len c.cells == k | None -> True))
  = match cs with
    | [] -> ()
    | c :: r -> if c.name = n then () else uniform_find k n r

let rec typed_find (n:string) (cs:list column)
  : Lemma (requires typed cs)
          (ensures (match find_col n cs with Some c -> Ok? (cells_fit c) | None -> True))
  = match cs with
    | [] -> ()
    | c :: r -> if c.name = n then () else typed_find n r

(* `uniform` and a non-empty column list pin the row count. *)
let uniform_row_count (k:nat) (t:table)
  : Lemma (requires uniform k t.columns /\ Cons? t.columns) (ensures row_count t == k)
  = ()

(* ---- cells ---- *)

let rec len_set_at (#a:Type) (i:nat) (v:a) (l:list a)
  : Lemma (ensures len (set_at i v l) == len l)
  = match l with
    | [] -> ()
    | _ :: t -> if i = 0 then () else len_set_at (i - 1) v t

let rec cells_fit_set_at (n:string) (ty:coltype) (i:nat) (v:cell) (cs:list cell)
  : Lemma (requires Ok? (cells_fit_from n ty cs) /\ Ok? (cell_fits n ty v))
          (ensures Ok? (cells_fit_from n ty (set_at i v cs)))
  = match cs with
    | [] -> ()
    | c :: r -> if i = 0 then () else cells_fit_set_at n ty (i - 1) v r

let rec cells_fit_nth (n:string) (ty:coltype) (i:nat) (cs:list cell)
  : Lemma (requires Ok? (cells_fit_from n ty cs))
          (ensures (match nth i cs with Some c -> Ok? (cell_fits n ty c) | None -> True))
  = match cs with
    | [] -> ()
    | c :: r -> if i = 0 then () else cells_fit_nth n ty (i - 1) r

let rec cells_fit_app (n:string) (ty:coltype) (xs ys:list cell)
  : Lemma (requires Ok? (cells_fit_from n ty xs) /\ Ok? (cells_fit_from n ty ys))
          (ensures Ok? (cells_fit_from n ty (app xs ys)))
  = match xs with
    | [] -> ()
    | _ :: r -> cells_fit_app n ty r ys

let rec nth_some (#a:Type) (i:nat) (l:list a)
  : Lemma (requires i < len l) (ensures Some? (nth i l))
  = match l with
    | [] -> ()
    | _ :: t -> if i = 0 then () else nth_some (i - 1) t

(* ---- replace ---- *)

let rec schema_of_replace (n:string) (nc:column) (cs:list column)
  : Lemma (requires nc.name == n)
          (ensures schema_of (replace_cols n nc cs) == replace_schema n nc.ty (schema_of cs))
  = match cs with
    | [] -> ()
    | _ :: r -> schema_of_replace n nc r

let rec col_names_replace (n:string) (nc:column) (cs:list column)
  : Lemma (requires nc.name == n)
          (ensures col_names (replace_cols n nc cs) == col_names cs)
  = match cs with
    | [] -> ()
    | _ :: r -> col_names_replace n nc r

let rec uniform_replace (k:nat) (n:string) (nc:column) (cs:list column)
  : Lemma (requires uniform k cs /\ len nc.cells == k)
          (ensures uniform k (replace_cols n nc cs))
  = match cs with
    | [] -> ()
    | _ :: r -> uniform_replace k n nc r

let rec typed_replace (n:string) (nc:column) (cs:list column)
  : Lemma (requires typed cs /\ Ok? (cells_fit nc))
          (ensures typed (replace_cols n nc cs))
  = match cs with
    | [] -> ()
    | _ :: r -> typed_replace n nc r

let rec len_replace (n:string) (nc:column) (cs:list column)
  : Lemma (ensures len (replace_cols n nc cs) == len cs)
  = match cs with
    | [] -> ()
    | _ :: r -> len_replace n nc r

(* The replaced table is well-formed when the replacement is the right length, typed and named
   for the slot it takes. *)
let replace_wf (n:string) (nc:column) (t:table)
  : Lemma (requires wf t /\ nc.name == n /\ len nc.cells == row_count t /\ Ok? (cells_fit nc) /\
                    Some? (find_col n t.columns))
          (ensures wf (replace_column n nc t) /\ row_count (replace_column n nc t) == row_count t)
  = schema_of_replace n nc t.columns;
    col_names_replace n nc t.columns;
    uniform_replace (row_count t) n nc t.columns;
    typed_replace n nc t.columns;
    uniform_row_count (row_count t) (replace_column n nc t)

(* ---- insert ---- *)

let rec take_drop (#a:Type) (i:nat) (l:list a)
  : Lemma (ensures app (take i l) (drop i l) == l)
  = match l with
    | [] -> ()
    | _ :: t -> if i = 0 then () else take_drop (i - 1) t

let rec schema_of_app (xs ys:list column)
  : Lemma (ensures schema_of (app xs ys) == app (schema_of xs) (schema_of ys))
  = match xs with
    | [] -> ()
    | _ :: r -> schema_of_app r ys

let rec schema_of_take (i:nat) (cs:list column)
  : Lemma (ensures schema_of (take i cs) == take i (schema_of cs))
  = match cs with
    | [] -> ()
    | _ :: r -> if i = 0 then () else schema_of_take (i - 1) r

let rec schema_of_drop (i:nat) (cs:list column)
  : Lemma (ensures schema_of (drop i cs) == drop i (schema_of cs))
  = match cs with
    | [] -> ()
    | _ :: r -> if i = 0 then () else schema_of_drop (i - 1) r

let schema_of_insert_at (i:nat) (c:column) (cs:list column)
  : Lemma (ensures schema_of (insert_at i c cs) == insert_at i (c.name, c.ty) (schema_of cs))
  = schema_of_app (take i cs) (c :: drop i cs);
    schema_of_take i cs;
    schema_of_drop i cs

let rec col_names_app (xs ys:list column)
  : Lemma (ensures col_names (app xs ys) == app (col_names xs) (col_names ys))
  = match xs with
    | [] -> ()
    | _ :: r -> col_names_app r ys

let rec mem_app (x:string) (xs ys:list string)
  : Lemma (ensures mem x (app xs ys) <==> (mem x xs \/ mem x ys))
  = match xs with
    | [] -> ()
    | _ :: r -> mem_app x r ys

let rec no_dups_app_insert (x:string) (xs ys:list string)
  : Lemma (requires no_dups (app xs ys) /\ not (mem x (app xs ys)))
          (ensures no_dups (app xs (x :: ys)))
  = match xs with
    | [] -> ()
    | y :: r -> mem_app y r ys; mem_app y r (x :: ys); no_dups_app_insert x r ys

(* `insert_at` keeps names distinct when the inserted name is fresh. *)
let no_dups_insert_at (i:nat) (c:column) (cs:list column)
  : Lemma (requires no_dups (col_names cs) /\ not (mem c.name (col_names cs)))
          (ensures no_dups (col_names (insert_at i c cs)))
  = take_drop i cs;
    col_names_app (take i cs) (drop i cs);
    col_names_app (take i cs) (c :: drop i cs);
    no_dups_app_insert c.name (col_names (take i cs)) (col_names (drop i cs))

let rec uniform_app (k:nat) (xs ys:list column)
  : Lemma (ensures uniform k (app xs ys) <==> (uniform k xs /\ uniform k ys))
  = match xs with
    | [] -> ()
    | _ :: r -> uniform_app k r ys

let rec typed_app (xs ys:list column)
  : Lemma (ensures typed (app xs ys) <==> (typed xs /\ typed ys))
  = match xs with
    | [] -> ()
    | _ :: r -> typed_app r ys

let uniform_insert_at (k:nat) (i:nat) (c:column) (cs:list column)
  : Lemma (requires uniform k cs /\ len c.cells == k)
          (ensures uniform k (insert_at i c cs))
  = take_drop i cs;
    uniform_app k (take i cs) (drop i cs);
    uniform_app k (take i cs) (c :: drop i cs)

let typed_insert_at (i:nat) (c:column) (cs:list column)
  : Lemma (requires typed cs /\ Ok? (cells_fit c))
          (ensures typed (insert_at i c cs))
  = take_drop i cs;
    typed_app (take i cs) (drop i cs);
    typed_app (take i cs) (c :: drop i cs)

let rec len_app (#a:Type) (xs ys:list a)
  : Lemma (ensures len (app xs ys) == len xs + len ys)
  = match xs with
    | [] -> ()
    | _ :: r -> len_app r ys

let rec len_take (#a:Type) (i:nat) (l:list a)
  : Lemma (ensures len (take i l) == (if i < len l then i else len l))
  = match l with
    | [] -> ()
    | _ :: t -> if i = 0 then () else len_take (i - 1) t

(* The inserted table is well-formed when the column is fresh, typed, and the row count long —
   or the table had no columns, in which case the column sets the row count. *)
let insert_wf (index:int) (c:column) (t:table)
  : Lemma (requires wf t /\ not (mem c.name (col_names t.columns)) /\ Ok? (cells_fit c) /\
                    (Cons? t.columns ==> len c.cells == row_count t))
          (ensures wf (insert_column_at index c t))
  = let i = clamp index (len t.columns) in
    schema_of_insert_at i c t.columns;
    no_dups_insert_at i c t.columns;
    typed_insert_at i c t.columns;
    (match t.columns with
     | [] -> ()
     | _ ->
       uniform_insert_at (row_count t) i c t.columns;
       take_drop i t.columns;
       len_app (take i t.columns) (c :: drop i t.columns);
       uniform_row_count (row_count t) (insert_column_at index c t))

(* ---- remove ---- *)

let rec schema_of_remove (n:string) (cs:list column)
  : Lemma (ensures schema_of (remove_cols n cs) == remove_schema n (schema_of cs))
  = match cs with
    | [] -> ()
    | _ :: r -> schema_of_remove n r

(* F#: `List.filter ((<>) n)` on a name list — what `remove_cols` does to the names. *)
let rec remove_name (n:string) (l:list string) : Tot (list string) =
  match l with
  | [] -> []
  | x :: r -> if x = n then remove_name n r else x :: remove_name n r

let rec col_names_remove (n:string) (cs:list column)
  : Lemma (ensures col_names (remove_cols n cs) == remove_name n (col_names cs))
  = match cs with
    | [] -> ()
    | _ :: r -> col_names_remove n r

let rec mem_remove_name (x n:string) (l:list string)
  : Lemma (ensures mem x (remove_name n l) <==> (mem x l /\ x <> n))
  = match l with
    | [] -> ()
    | _ :: r -> mem_remove_name x n r

let rec no_dups_remove_name (n:string) (l:list string)
  : Lemma (requires no_dups l) (ensures no_dups (remove_name n l))
  = match l with
    | [] -> ()
    | x :: r -> mem_remove_name x n r; no_dups_remove_name n r

let rec uniform_remove (k:nat) (n:string) (cs:list column)
  : Lemma (requires uniform k cs) (ensures uniform k (remove_cols n cs))
  = match cs with
    | [] -> ()
    | _ :: r -> uniform_remove k n r

let rec typed_remove (n:string) (cs:list column)
  : Lemma (requires typed cs) (ensures typed (remove_cols n cs))
  = match cs with
    | [] -> ()
    | _ :: r -> typed_remove n r

let remove_wf (n:string) (t:table)
  : Lemma (requires wf t) (ensures wf (remove_column n t))
  = schema_of_remove n t.columns;
    col_names_remove n t.columns;
    no_dups_remove_name n (col_names t.columns);
    uniform_remove (row_count t) n t.columns;
    typed_remove n t.columns;
    (match remove_cols n t.columns with
     | [] -> ()
     | _ -> uniform_row_count (row_count t) (remove_column n t))

(* ---- append ---- *)

let rec schema_of_append (rows:list (list (string & cell))) (cs:list column)
  : Lemma (ensures schema_of (append_cols rows cs) == schema_of cs /\
                   col_names (append_cols rows cs) == col_names cs)
  = match cs with
    | [] -> ()
    | _ :: r -> schema_of_append rows r

let rec len_appended (n:string) (rows:list (list (string & cell)))
  : Lemma (ensures len (appended n rows) == len rows)
  = match rows with
    | [] -> ()
    | _ :: r -> len_appended n r

let rec uniform_append (k:nat) (rows:list (list (string & cell))) (cs:list column)
  : Lemma (requires uniform k cs) (ensures uniform (k + len rows) (append_cols rows cs))
  = match cs with
    | [] -> ()
    | c :: r -> len_app c.cells (appended c.name rows); len_appended c.name rows; uniform_append k rows r

(* A row that passed `row_fault` hands every column it names a fitting cell, and a column it does
   not name gets `Null`, which fits anything. *)
let rec lookup_fits (t:table) (n:string) (c:column) (row:list (string & cell))
  : Lemma (requires row_fault t row == None /\ find_col n t.columns == Some c)
          (ensures Ok? (cell_fits n c.ty (lookup n row)))
  = match row with
    | [] -> ()
    | (m, v) :: rest -> if m = n then () else lookup_fits t n c rest

let rec appended_fits (t:table) (n:string) (c:column) (rows:list (list (string & cell)))
  : Lemma (requires first_fault t rows == None /\ find_col n t.columns == Some c)
          (ensures Ok? (cells_fit_from n c.ty (appended n rows)))
  = match rows with
    | [] -> ()
    | row :: rest -> lookup_fits t n c row; appended_fits t n c rest

(* Walking the columns of a name-distinct list: each is what the WHOLE table finds for its own
   name, so `appended_fits` applies to it. *)
let rec typed_append_aux (t:table) (rows:list (list (string & cell))) (cs:list column)
  : Lemma (requires typed cs /\ first_fault t rows == None /\
                    (forall (d:column). find_col d.name cs == Some d ==> find_col d.name t.columns == Some d) /\
                    no_dups (col_names cs))
          (ensures typed (append_cols rows cs))
  = match cs with
    | [] -> ()
    | c :: r ->
      appended_fits t c.name c rows;
      cells_fit_app c.name c.ty c.cells (appended c.name rows);
      let tail_finds (d:column)
        : Lemma (find_col d.name r == Some d ==> find_col d.name cs == Some d)
        = if find_col d.name r = Some d then (find_col_name d.name r; find_col_mem d.name r) else ()
      in
      FStar.Classical.forall_intro tail_finds;
      typed_append_aux t rows r

let append_wf (rows:list (list (string & cell))) (t:table)
  : Lemma (requires wf t /\ first_fault t rows == None)
          (ensures wf { t with columns = append_cols rows t.columns })
  = schema_of_append rows t.columns;
    uniform_append (row_count t) rows t.columns;
    typed_append_aux t rows t.columns;
    (match t.columns with
     | [] -> ()
     | _ -> uniform_row_count (row_count t + len rows) { t with columns = append_cols rows t.columns })

(* THE FOURTH THEOREM, structural half: every accepted structural operation preserves
   well-formedness. *)
let apply_preserves_wf (ev:evaluator) (o:op) (t:table)
  : Lemma (requires wf t /\ structural o)
          (ensures (match apply ev o t with Ok t' -> wf t' | Error _ -> True))
  = match o with
    | SetCell n row value ->
      (match find_col n t.columns with
       | None -> ()
       | Some col ->
         let rc = row_count t in
         if row < 0 || row >= rc then ()
         else
           (match cell_fits n col.ty value with
            | Error _ -> ()
            | Ok () ->
              find_col_name n t.columns;
              uniform_find rc n t.columns;
              typed_find n t.columns;
              len_set_at row value col.cells;
              cells_fit_set_at n col.ty row value col.cells;
              replace_wf n { col with cells = set_at row value col.cells } t))
    | SetColumn nc ->
      (match find_col nc.name t.columns with
       | None -> ()
       | Some _ ->
         if len nc.cells <> row_count t then ()
         else (match cells_fit nc with
               | Error _ -> ()
               | Ok () -> replace_wf nc.name nc t))
    | InsertColumn index col ->
      find_col_mem col.name t.columns;
      if exists_col col.name t.columns then ()
      else if Cons? t.columns && len col.cells <> row_count t then ()
      else (match cells_fit col with
            | Error _ -> ()
            | Ok () -> insert_wf index col t)
    | RemoveColumn n -> if exists_col n t.columns then remove_wf n t else ()
    | AppendRows rows ->
      (match first_fault t rows with
       | Some _ -> ()
       | None -> append_wf rows t)
    | ApplyTransform _ -> ()

(* The evaluator premise — the ONE thing `ApplyTransform` is asked for. `DataFrame.evalPipeline`
   is production code this model does not interpret; that it returns coherent, name-distinct,
   uniform, typed tables is its own contract, stated here as the hypothesis and nowhere
   discharged (the `column-transform-evaluator-abstract` row). *)
let ev_preserves_wf (ev:evaluator) : Tot prop =
  forall (p:string) (t:table). wf t ==> (match ev p t with Ok t' -> wf t' | Error _ -> True)

(* THE FOURTH THEOREM, whole: under that premise, every accepted operation preserves
   well-formedness, and so does every accepted script. *)
let apply_preserves_wf_ev (ev:evaluator) (o:op) (t:table)
  : Lemma (requires wf t /\ ev_preserves_wf ev)
          (ensures (match apply ev o t with Ok t' -> wf t' | Error _ -> True))
  = match o with
    | ApplyTransform _ -> ()
    | _ -> apply_preserves_wf ev o t

let rec apply_all_preserves_wf (ev:evaluator) (os:list op) (t:table)
  : Lemma (requires wf t /\ ev_preserves_wf ev)
          (ensures (match apply_all ev os t with Ok t' -> wf t' | Error _ -> True))
          (decreases os)
  = match os with
    | [] -> ()
    | o :: r ->
      apply_preserves_wf_ev ev o t;
      (match apply ev o t with
       | Ok t' -> apply_all_preserves_wf ev r t'
       | Error _ -> ())

(* ======================================================================================
   9. THE FIFTH THEOREM — `invert`'s round trip, and the partial cases characterised.

      F#: "`apply (invert op t) (apply op t) = t`", the doc comment's defining law, sampled by
      `Conformance.columnarOpLaws` and proved here for the four operations `invert` is defined
      for, on a well-formed table. The two it refuses unconditionally, and the one it answers
      WITHOUT LOOKING, are stated beside it.
   ====================================================================================== *)

(* ---- replace twice ---- *)

(* The first column of that name is what a replace puts there. *)
let rec find_col_replace (n:string) (nc:column) (cs:list column)
  : Lemma (requires Some? (find_col n cs) /\ nc.name == n)
          (ensures find_col n (replace_cols n nc cs) == Some nc)
  = match cs with
    | [] -> ()
    | c :: r -> if c.name = n then () else find_col_replace n nc r

let rec replace_cols_absent (n:string) (nc:column) (cs:list column)
  : Lemma (requires not (mem n (col_names cs)))
          (ensures replace_cols n nc cs == cs)
  = match cs with
    | [] -> ()
    | _ :: r -> replace_cols_absent n nc r

(* Replacing a column by ITSELF is the identity on a name-distinct list. *)
let rec replace_cols_id (n:string) (c:column) (cs:list column)
  : Lemma (requires no_dups (col_names cs) /\ find_col n cs == Some c)
          (ensures replace_cols n c cs == cs)
  = match cs with
    | [] -> ()
    | d :: r -> if d.name = n then replace_cols_absent n c r else replace_cols_id n c r

(* Replacing a column and then putting the original back is the identity. *)
let rec replace_replace_id (n:string) (c nc:column) (cs:list column)
  : Lemma (requires no_dups (col_names cs) /\ find_col n cs == Some c /\ nc.name == n)
          (ensures replace_cols n c (replace_cols n nc cs) == cs)
  = match cs with
    | [] -> ()
    | d :: r -> if d.name = n then (replace_cols_absent n nc r; replace_cols_absent n c r)
                else replace_replace_id n c nc r

let replace_replace_table (n:string) (c nc:column) (t:table)
  : Lemma (requires wf t /\ find_col n t.columns == Some c /\ nc.name == n)
          (ensures replace_column n c (replace_column n nc t) == t)
  = find_col_name n t.columns;
    replace_replace_id n c nc t.columns;
    schema_of_replace n nc t.columns;
    schema_of_replace n c (replace_cols n nc t.columns)

let rec set_at_set_at (#a:Type) (i:nat) (x v:a) (l:list a)
  : Lemma (requires nth i l == Some x)
          (ensures set_at i x (set_at i v l) == l)
  = match l with
    | [] -> ()
    | _ :: t -> if i = 0 then () else set_at_set_at (i - 1) x v t

(* ---- insert then remove ---- *)

let rec remove_cols_app (n:string) (xs ys:list column)
  : Lemma (ensures remove_cols n (app xs ys) == app (remove_cols n xs) (remove_cols n ys))
  = match xs with
    | [] -> ()
    | _ :: r -> remove_cols_app n r ys

let rec remove_cols_absent (n:string) (cs:list column)
  : Lemma (requires not (mem n (col_names cs)))
          (ensures remove_cols n cs == cs)
  = match cs with
    | [] -> ()
    | _ :: r -> remove_cols_absent n r

let remove_insert_absent (i:nat) (c:column) (cs:list column)
  : Lemma (requires not (mem c.name (col_names cs)))
          (ensures remove_cols c.name (insert_at i c cs) == cs)
  = take_drop i cs;
    col_names_app (take i cs) (drop i cs);
    mem_app c.name (col_names (take i cs)) (col_names (drop i cs));
    remove_cols_app c.name (take i cs) (c :: drop i cs);
    remove_cols_absent c.name (take i cs);
    remove_cols_absent c.name (drop i cs)

let mem_insert_at (i:nat) (c:column) (cs:list column)
  : Lemma (ensures mem c.name (col_names (insert_at i c cs)))
  = col_names_app (take i cs) (c :: drop i cs);
    mem_app c.name (col_names (take i cs)) (c.name :: col_names (drop i cs))

(* ---- remove then insert ---- *)

let rec find_index_some (n:string) (cs:list column)
  : Lemma (ensures Some? (find_index n cs) <==> Some? (find_col n cs))
  = match cs with
    | [] -> ()
    | c :: r -> if c.name = n then () else find_index_some n r

let rec nth_find_index (n:string) (cs:list column)
  : Lemma (ensures (match find_index n cs with
                    | Some i -> i < len cs /\ (match nth i cs with Some c -> c.name == n | None -> False)
                    | None -> True))
  = match cs with
    | [] -> ()
    | c :: r -> if c.name = n then () else nth_find_index n r

let rec uniform_nth (k:nat) (i:nat) (cs:list column)
  : Lemma (requires uniform k cs)
          (ensures (match nth i cs with Some c -> len c.cells == k | None -> True))
  = match cs with
    | [] -> ()
    | _ :: r -> if i = 0 then () else uniform_nth k (i - 1) r

let rec typed_nth (i:nat) (cs:list column)
  : Lemma (requires typed cs)
          (ensures (match nth i cs with Some c -> Ok? (cells_fit c) | None -> True))
  = match cs with
    | [] -> ()
    | _ :: r -> if i = 0 then () else typed_nth (i - 1) r

let rec len_remove_mem (n:string) (cs:list column)
  : Lemma (requires mem n (col_names cs))
          (ensures len (remove_cols n cs) < len cs)
  = match cs with
    | [] -> ()
    | c :: r -> if c.name = n then (if mem n (col_names r) then len_remove_mem n r else remove_cols_absent n r)
                else len_remove_mem n r

let rec len_remove_nodups (n:string) (cs:list column)
  : Lemma (requires no_dups (col_names cs) /\ mem n (col_names cs))
          (ensures len (remove_cols n cs) == len cs - 1)
  = match cs with
    | [] -> ()
    | c :: r -> if c.name = n then remove_cols_absent n r else len_remove_nodups n r

let remove_cols_gone (n:string) (cs:list column)
  : Lemma (ensures not (mem n (col_names (remove_cols n cs))))
  = col_names_remove n cs; mem_remove_name n n (col_names cs)

(* Putting a removed column back at the index it was found at restores the list. *)
let rec insert_remove_restores (n:string) (i:nat) (c:column) (cs:list column)
  : Lemma (requires no_dups (col_names cs) /\ find_index n cs == Some i /\ nth i cs == Some c)
          (ensures insert_at i c (remove_cols n cs) == cs)
  = match cs with
    | [] -> ()
    | d :: r ->
      if d.name = n then remove_cols_absent n r
      else insert_remove_restores n (i - 1) c r

(* ---- the theorem ---- *)

(* The four operations `invert` is defined for. *)
let invertible (o:op) : Tot bool =
  SetCell? o || SetColumn? o || InsertColumn? o || RemoveColumn? o

(* THE FIFTH THEOREM. On a well-formed table, the inverse of an accepted invertible operation is
   itself accepted at the result and restores the input exactly. *)
let invert_roundtrip (ev:evaluator) (o:op) (t:table)
  : Lemma (requires wf t /\ invertible o /\ Ok? (apply ev o t))
          (ensures (match invert o t with
                    | Ok inv -> apply ev inv (Ok?._0 (apply ev o t)) == Ok t
                    | Error _ -> False))
  = match o with
    | SetCell n row value ->
      let Some col = find_col n t.columns in
      let rc = row_count t in
      find_col_name n t.columns;
      uniform_find rc n t.columns;
      typed_find n t.columns;
      nth_some row col.cells;
      let Some old = nth row col.cells in
      let nc = { col with cells = set_at row value col.cells } in
      let t' = replace_column n nc t in
      len_set_at row value col.cells;
      cells_fit_set_at n col.ty row value col.cells;
      replace_wf n nc t;
      find_col_replace n nc t.columns;
      cells_fit_nth n col.ty row col.cells;
      set_at_set_at row old value col.cells;
      replace_replace_table n col nc t
    | SetColumn nc ->
      let Some old = find_col nc.name t.columns in
      find_col_name nc.name t.columns;
      uniform_find (row_count t) nc.name t.columns;
      typed_find nc.name t.columns;
      replace_wf nc.name nc t;
      find_col_replace nc.name nc t.columns;
      replace_replace_table nc.name old nc t
    | InsertColumn index col ->
      find_col_mem col.name t.columns;
      let i = clamp index (len t.columns) in
      mem_insert_at i col t.columns;
      find_col_mem col.name (insert_at i col t.columns);
      remove_insert_absent i col t.columns;
      schema_of_insert_at i col t.columns;
      schema_of_remove col.name (insert_at i col t.columns)
    | RemoveColumn n ->
      find_col_mem n t.columns;
      find_index_some n t.columns;
      nth_find_index n t.columns;
      let Some idx = find_index n t.columns in
      let Some c = nth idx t.columns in
      uniform_nth (row_count t) idx t.columns;
      typed_nth idx t.columns;
      remove_wf n t;
      remove_cols_gone n t.columns;
      find_col_mem n (remove_cols n t.columns);
      len_remove_nodups n t.columns;
      insert_remove_restores n idx c t.columns;
      schema_of_remove n t.columns;
      schema_of_insert_at idx c (remove_cols n t.columns)

(* ---- the partial cases, characterised ---- *)

(* The two `invert` refuses whatever the table: no row removal, no general transform inverse. *)
let invert_not_invertible (rows:list (list (string & cell))) (p:string) (t:table)
  : Lemma (ensures invert (AppendRows rows) t == Error (NotInvertible "AppendRows") /\
                   invert (ApplyTransform p) t == Error (NotInvertible "ApplyTransform"))
  = ()

(* `invert` on `SetCell`, `SetColumn` and `RemoveColumn` refuses EXACTLY the pre-states `apply`
   refuses for the column and row it reads, with the same rejection; where `apply` goes on to
   refuse the VALUE (a cell of the wrong type, a column of the wrong length), `invert` has
   already answered — and `RemoveColumn` has no value to refuse, so there the two verdicts
   coincide outright. *)
let invert_refuses_as_apply (ev:evaluator) (o:op) (t:table)
  : Lemma (requires SetCell? o \/ SetColumn? o \/ RemoveColumn? o)
          (ensures (match invert o t with
                    | Error e -> apply ev o t == Error e
                    | Ok _ -> (match apply ev o t with
                               | Ok _ -> True
                               | Error e -> not (RemoveColumn? o) /\
                                           (CellTypeMismatch? e \/ ColumnLengthMismatch? e))))
  = match o with
    | SetCell n row _ ->
      (match find_col n t.columns with
       | None -> ()
       | Some col -> if row < 0 || row >= row_count t then () else ())
    | SetColumn nc -> cells_fit_from_shape nc.name nc.ty nc.cells
    | RemoveColumn n -> find_index_some n t.columns; nth_find_index n t.columns

(* THE FINDING. `invert` on `InsertColumn` reads nothing from the pre-state — the F# clause is
   `InsertColumn(_, col) -> Ok(RemoveColumn col.Name)`, unconditionally — so it answers for a
   REFUSED insert exactly as for an accepted one. *)
let invert_insert_reads_nothing (index:int) (col:column) (t u:table)
  : Lemma (ensures invert (InsertColumn index col) t == Ok (RemoveColumn col.name) /\
                   invert (InsertColumn index col) t == invert (InsertColumn index col) u)
  = ()

(* And the consequence: the "inverse" of an insert refused as a DUPLICATE is a remove that
   succeeds at the pre-state and takes the column that was already there. A caller that inverts
   without first checking acceptance loses a column the refused operation never touched.
   Reported here; `Column.Ops` is unchanged by this phase. *)
let refused_insert_inverse_is_live (ev:evaluator) (index:int) (col:column) (t:table)
  : Lemma (requires apply ev (InsertColumn index col) t == Error (DuplicateColumn col.name))
          (ensures (match invert (InsertColumn index col) t with
                    | Ok inv -> apply ev inv t == Ok (remove_column col.name t) /\
                               len (remove_cols col.name t.columns) < len t.columns
                    | Error _ -> False))
  = cells_fit_from_shape col.name col.ty col.cells;
    find_col_mem col.name t.columns;
    len_remove_mem col.name t.columns

(* ======================================================================================
   10. THE SIXTH THEOREM — every script `Diff.toOps` emits applies to the table it was
       computed against, and yields the other table.

       F#: "producing a script that reconstructs `after` from `before` (`apply`-ing it in order
       yields `after`)" — the doc comment's promise, for well-formed tables. Two branches, two
       arguments: the column-granular branch walks `after`'s columns replacing each changed one
       in place, so the invariant is what each prefix of the walk has already put right; the
       rebuild branch empties the table and refills it in order, so the invariant is that the
       table so far IS a prefix of `after`.
   ====================================================================================== *)

(* ---- membership on columns, and the extensionality of the two records ---- *)

let rec memc (c:column) (cs:list column) : Tot bool =
  match cs with
  | [] -> false
  | d :: r -> c = d || memc c r

let rec memc_name (c:column) (cs:list column)
  : Lemma (requires memc c cs) (ensures mem c.name (col_names cs))
  = match cs with
    | [] -> ()
    | d :: r -> if c = d then () else memc_name c r

(* On a name-distinct list a member is what `find_col` returns for its own name. *)
let rec find_col_memc (c:column) (cs:list column)
  : Lemma (requires no_dups (col_names cs) /\ memc c cs)
          (ensures find_col c.name cs == Some c)
  = match cs with
    | [] -> ()
    | d :: r -> if c = d then () else (memc_name c r; find_col_memc c r)

let rec uniform_memc (k:nat) (c:column) (cs:list column)
  : Lemma (requires uniform k cs /\ memc c cs) (ensures len c.cells == k)
  = match cs with
    | [] -> ()
    | d :: r -> if c = d then () else uniform_memc k c r

let rec typed_memc (c:column) (cs:list column)
  : Lemma (requires typed cs /\ memc c cs) (ensures Ok? (cells_fit c))
  = match cs with
    | [] -> ()
    | d :: r -> if c = d then () else typed_memc c r

let column_ext (x y:column)
  : Lemma (requires x.name == y.name /\ x.ty == y.ty /\ x.cells == y.cells) (ensures x == y)
  = match x, y with
    | Mkcolumn _ _ _, Mkcolumn _ _ _ -> ()

let table_ext (x y:table)
  : Lemma (requires x.schema == y.schema /\ x.columns == y.columns) (ensures x == y)
  = match x, y with
    | Mktable _ _, Mktable _ _ -> ()

(* Two lists with the same schema find same-named, same-typed columns. *)
let rec find_col_same_schema (n:string) (xs ys:list column)
  : Lemma (requires schema_of xs == schema_of ys)
          (ensures (match find_col n xs, find_col n ys with
                    | Some x, Some y -> x.ty == y.ty /\ x.name == y.name
                    | None, None -> True
                    | _, _ -> False))
  = match xs, ys with
    | [], [] -> ()
    | x :: xr, y :: yr -> if x.name = n then () else find_col_same_schema n xr yr
    | _, _ -> ()

(* Same names in the same order, no repeats, and every one of `ys` found by its own name in
   `xs` — then `xs` IS `ys`. *)
let rec same_names_same_finds (xs ys:list column)
  : Lemma (requires col_names xs == col_names ys /\ no_dups (col_names ys) /\
                    (forall (a:column). memc a ys ==> find_col a.name xs == Some a))
          (ensures xs == ys)
  = match xs, ys with
    | [], [] -> ()
    | x :: xr, a :: ar ->
      let tail (b:column)
        : Lemma (memc b ar ==> find_col b.name xr == Some b)
        = if memc b ar then memc_name b ar else ()
      in
      FStar.Classical.forall_intro tail;
      same_names_same_finds xr ar
    | _, _ -> ()

let rec find_col_replace_other (m n:string) (nc:column) (cs:list column)
  : Lemma (requires m <> n /\ nc.name == n)
          (ensures find_col m (replace_cols n nc cs) == find_col m cs)
  = match cs with
    | [] -> ()
    | _ :: r -> find_col_replace_other m n nc r

(* ---- the column-granular branch ---- *)

(* The walk. `rest` is the suffix of `after`'s columns still to process; each is found in the
   current table exactly as in `before` (nothing so far touched its name), is typed, is the row
   count long, and has the type `before` gives that name. The walk accepts every step it emits,
   and afterwards every column of the suffix is found as ITSELF, with every other name untouched. *)
let rec changed_apply (ev:evaluator) (bcs:list column) (rest:list column) (cur:table)
  : Lemma (requires wf cur /\ no_dups (col_names rest) /\
                    (forall (a:column). memc a rest ==>
                       (find_col a.name cur.columns == find_col a.name bcs /\
                        Some? (find_col a.name bcs) /\
                        (Some?.v (find_col a.name bcs)).ty == a.ty /\
                        len a.cells == row_count cur /\ Ok? (cells_fit a))))
          (ensures (match apply_all ev (changed_cols bcs rest) cur with
                    | Ok cur' ->
                      wf cur' /\ col_names cur'.columns == col_names cur.columns /\
                      row_count cur' == row_count cur /\
                      (forall (a:column). memc a rest ==> find_col a.name cur'.columns == Some a) /\
                      (forall (m:string). not (mem m (col_names rest)) ==>
                                          find_col m cur'.columns == find_col m cur.columns)
                    | Error _ -> False))
          (decreases rest)
  = match rest with
    | [] -> ()
    | a :: ar ->
      let Some bc = find_col a.name bcs in
      find_col_name a.name bcs;
      if bc.cells <> a.cells then begin
        (* the step is `SetColumn a`, accepted, replacing the column in place *)
        let cur1 = replace_column a.name a cur in
        replace_wf a.name a cur;
        col_names_replace a.name a cur.columns;
        find_col_replace a.name a cur.columns;
        let untouched (b:column)
          : Lemma (memc b ar ==>
                   (find_col b.name cur1.columns == find_col b.name bcs /\
                    Some? (find_col b.name bcs) /\
                    (Some?.v (find_col b.name bcs)).ty == b.ty /\
                    len b.cells == row_count cur1 /\ Ok? (cells_fit b)))
          = if memc b ar then (memc_name b ar; find_col_replace_other b.name a.name a cur.columns) else ()
        in
        FStar.Classical.forall_intro untouched;
        changed_apply ev bcs ar cur1;
        (match apply_all ev (changed_cols bcs ar) cur1 with
         | Ok cur' ->
           let others (m:string)
             : Lemma (not (mem m (col_names rest)) ==> find_col m cur'.columns == find_col m cur.columns)
             = if not (mem m (col_names rest)) then find_col_replace_other m a.name a cur.columns else ()
           in
           FStar.Classical.forall_intro others;
           let each (b:column)
             : Lemma (memc b rest ==> find_col b.name cur'.columns == Some b)
             = if b = a then () else ()
           in
           FStar.Classical.forall_intro each
         | Error _ -> ())
      end else begin
        (* no step: the column is already `after`'s, so the walk leaves it and moves on *)
        column_ext bc a;
        changed_apply ev bcs ar cur;
        (match apply_all ev (changed_cols bcs ar) cur with
         | Ok cur' ->
           let each (b:column)
             : Lemma (memc b rest ==> find_col b.name cur'.columns == Some b)
             = ()
           in
           FStar.Classical.forall_intro each
         | Error _ -> ())
      end

(* ---- the rebuild branch ---- *)

let rec apply_all_app (ev:evaluator) (xs ys:list op) (t:table)
  : Lemma (ensures apply_all ev (app xs ys) t ==
                   (match apply_all ev xs t with Ok m -> apply_all ev ys m | Error e -> Error e))
          (decreases xs)
  = match xs with
    | [] -> ()
    | o :: r ->
      (match apply ev o t with
       | Ok t' -> apply_all_app ev r ys t'
       | Error _ -> ())

(* Removing each name in turn. *)
let rec remove_names (ns:list string) (l:list string) : Tot (list string) =
  match ns with
  | [] -> l
  | n :: r -> remove_names r (remove_name n l)

let rec removes_apply (ev:evaluator) (rs:list column) (cur:table)
  : Lemma (requires wf cur /\ no_dups (col_names rs) /\
                    (forall (x:string). mem x (col_names rs) ==> mem x (col_names cur.columns)))
          (ensures (match apply_all ev (removes rs) cur with
                    | Ok cur' -> wf cur' /\
                                col_names cur'.columns == remove_names (col_names rs) (col_names cur.columns)
                    | Error _ -> False))
          (decreases rs)
  = match rs with
    | [] -> ()
    | r :: rr ->
      find_col_mem r.name cur.columns;
      let cur1 = remove_column r.name cur in
      remove_wf r.name cur;
      col_names_remove r.name cur.columns;
      let still (x:string)
        : Lemma (mem x (col_names rr) ==> mem x (col_names cur1.columns))
        = if mem x (col_names rr) then mem_remove_name x r.name (col_names cur.columns) else ()
      in
      FStar.Classical.forall_intro still;
      removes_apply ev rr cur1

let rec remove_names_all (ns l:list string)
  : Lemma (requires forall (x:string). mem x l ==> mem x ns)
          (ensures remove_names ns l == [])
  = match ns with
    | [] -> (match l with [] -> () | _ :: _ -> ())
    | n :: r ->
      let rest (x:string)
        : Lemma (mem x (remove_name n l) ==> mem x r)
        = mem_remove_name x n l
      in
      FStar.Classical.forall_intro rest;
      remove_names_all r (remove_name n l)

let rec app_nil (#a:Type) (l:list a)
  : Lemma (ensures app l [] == l)
  = match l with
    | [] -> ()
    | _ :: t -> app_nil t

let rec app_assoc (#a:Type) (xs ys zs:list a)
  : Lemma (ensures app (app xs ys) zs == app xs (app ys zs))
  = match xs with
    | [] -> ()
    | _ :: r -> app_assoc r ys zs

let rec col_names_rev (cs:list column)
  : Lemma (ensures col_names (rev cs) == rev (col_names cs))
  = match cs with
    | [] -> ()
    | c :: r -> col_names_rev r; col_names_app (rev r) [c]

let rec mem_rev (x:string) (l:list string)
  : Lemma (ensures mem x (rev l) <==> mem x l)
  = match l with
    | [] -> ()
    | y :: r -> mem_rev x r; mem_app x (rev r) [y]

let rec no_dups_rev (l:list string)
  : Lemma (requires no_dups l) (ensures no_dups (rev l))
  = match l with
    | [] -> ()
    | y :: r -> no_dups_rev r; mem_rev y r; app_nil (rev r); no_dups_app_insert y (rev r) []

let rec take_all (#a:Type) (l:list a)
  : Lemma (ensures take (len l) l == l)
  = match l with
    | [] -> ()
    | _ :: t -> take_all t

let rec drop_all (#a:Type) (l:list a)
  : Lemma (ensures drop (len l) l == [])
  = match l with
    | [] -> ()
    | _ :: t -> drop_all t

let insert_at_end (#a:Type) (x:a) (l:list a)
  : Lemma (ensures insert_at (len l) x l == app l [x])
  = take_all l; drop_all l

(* The refill. `k` is how many columns the table holds, which is where the next insert lands;
   every column still to insert is fresh, typed and `rc` long; and if the table already holds a
   column, `rc` is its row count. *)
let rec inserts_apply (ev:evaluator) (k:nat) (rest:list column) (cur:table) (rc:nat)
  : Lemma (requires wf cur /\ len cur.columns == k /\ no_dups (col_names rest) /\
                    (forall (x:string). mem x (col_names rest) ==> not (mem x (col_names cur.columns))) /\
                    (forall (c:column). memc c rest ==> (len c.cells == rc /\ Ok? (cells_fit c))) /\
                    (Cons? cur.columns ==> row_count cur == rc))
          (ensures (match apply_all ev (inserts_from k rest) cur with
                    | Ok cur' -> wf cur' /\ cur'.columns == app cur.columns rest
                    | Error _ -> False))
          (decreases rest)
  = match rest with
    | [] -> app_nil cur.columns
    | c :: rr ->
      find_col_mem c.name cur.columns;
      insert_at_end c cur.columns;
      let cur1 = insert_column_at k c cur in
      insert_wf k c cur;
      col_names_app cur.columns [c];
      len_app cur.columns [c];
      let fresh (x:string)
        : Lemma (mem x (col_names rr) ==> not (mem x (col_names cur1.columns)))
        = if mem x (col_names rr) then mem_app x (col_names cur.columns) [c.name] else ()
      in
      FStar.Classical.forall_intro fresh;
      inserts_apply ev (k + 1) rr cur1 rc;
      app_assoc cur.columns [c] rr

(* F#: `{ Schema = []; Columns = [] }` — what the removes leave, and what the inserts start from. *)
let empty_table : table = { schema = []; columns = [] }

(* ---- THE SIXTH THEOREM ---- *)

let diff_applicable (ev:evaluator) (before after:table)
  : Lemma (requires wf before /\ wf after)
          (ensures apply_all ev (to_ops before after) before == Ok after)
  = if before.schema = after.schema && row_count before = row_count after then begin
      (* the column-granular branch *)
      names_of_schema_of before.columns;
      names_of_schema_of after.columns;
      let ready (a:column)
        : Lemma (memc a after.columns ==>
                 (find_col a.name before.columns == find_col a.name before.columns /\
                  Some? (find_col a.name before.columns) /\
                  (Some?.v (find_col a.name before.columns)).ty == a.ty /\
                  len a.cells == row_count before /\ Ok? (cells_fit a)))
        = if memc a after.columns then begin
            memc_name a after.columns;
            find_col_mem a.name before.columns;
            find_col_memc a after.columns;
            find_col_same_schema a.name before.columns after.columns;
            uniform_memc (row_count after) a after.columns;
            typed_memc a after.columns
          end else ()
      in
      FStar.Classical.forall_intro ready;
      changed_apply ev before.columns after.columns before;
      (match apply_all ev (changed_cols before.columns after.columns) before with
       | Ok cur' ->
         same_names_same_finds cur'.columns after.columns;
         table_ext cur' after
       | Error _ -> ())
    end else begin
      (* the rebuild branch: every column out, then every column of `after` in, in order *)
      apply_all_app ev (removes (rev before.columns)) (inserts_from 0 after.columns) before;
      col_names_rev before.columns;
      no_dups_rev (col_names before.columns);
      let present (x:string)
        : Lemma (mem x (col_names (rev before.columns)) ==> mem x (col_names before.columns))
        = mem_rev x (col_names before.columns)
      in
      FStar.Classical.forall_intro present;
      removes_apply ev (rev before.columns) before;
      (match apply_all ev (removes (rev before.columns)) before with
       | Ok emptied ->
         let covered (x:string)
           : Lemma (mem x (col_names before.columns) ==> mem x (col_names (rev before.columns)))
           = mem_rev x (col_names before.columns)
         in
         FStar.Classical.forall_intro covered;
         remove_names_all (col_names (rev before.columns)) (col_names before.columns);
         (match emptied.columns with
          | [] -> ()
          | _ :: _ -> ());
         table_ext emptied empty_table;
         let sized (c:column)
           : Lemma (memc c after.columns ==> (len c.cells == row_count after /\ Ok? (cells_fit c)))
           = if memc c after.columns then (uniform_memc (row_count after) c after.columns; typed_memc c after.columns) else ()
         in
         FStar.Classical.forall_intro sized;
         inserts_apply ev 0 after.columns empty_table (row_count after);
         (match apply_all ev (inserts_from 0 after.columns) empty_table with
          | Ok cur' -> table_ext cur' after
          | Error _ -> ())
       | Error _ -> ())
    end
