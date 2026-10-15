(*
   MaintainedSort — the maintained `Sort` of the incremental refresh, and its agreement with the
   full stable sort (fuaran-core Phase 471, closing compute `DECISIONS.md` D37 item 1).

   WHAT IS MODELLED. `src/Fuaran.Compute.DataFrame/Incremental.fs`'s `WSort` arm of the walk
   (`classifyStep`: `MergeOrder`), at the level `Incremental.fst` and `IncrementalMaintained.fst`
   state the walk — a row under its token, the rows alive at the step in the order they ARRIVED:

     - the full stable sort (`sort_alive`, the reference's `evalSort` / `List.sortWith`): the rows
       ordered under the comparator with the ARRIVAL POSITION as the tie-break, stated as the
       insertion sort (`isort`) under the strict total order `lt_of` — "a stable sort is exactly a
       sort by (key, arrival position)" (F#: `sortSlots`);
     - the merge (`merge_sort` — F#: `mergeOrders`, Phase 274): the stable rows of the prior PRODUCED
       order, read in that order (F#: `prevOrder` through `stableNow`), and the moved rows sorted
       among themselves, merged by BINARY INSERTION (`bisect`): each moved row finds its place in
       the cached run by bisection under "goes out before" (`lt_of`: the comparator, then the
       arrival position), the run before it is copied whole, and the search for the next moved row
       continues from there (F#: `lo`);
     - the reuse condition (`arrived_as_before` — F#: `arrivedAsBefore`, Phase 215): the stable rows
       of the prior arrival still here, read in step with the stable rows of the arrival now, are
       the same sequence; where it fails the step sorts from scratch (F#: `reusable = None`);
     - the comparator, concretely: `compare_resolved` is `DataFrame.compareResolved` — multi-key,
       `Null` last whichever the direction, the direction flipping the sign, an incomparable pair
       read as a tie — over `compare_cells`, `DataFrame.compareCells` arm for arm: an int or a float
       against an int or a float in the float carrier (`Kernels.compareFloat`); a decimal against a
       decimal or an int EXACTLY (Core `Cell.compare`, `DecimalText.compare` over the canonical
       texts); a bool, a string, a date, a timestamp against its own type; anything else `None`.

   WHAT IS PROVED.

     - `merge_bin_sorted` / `merge_is_isort`: the binary-insertion merge of two sorted, disjoint
       runs is the sorted permutation of their union — and under a strict total order THAT
       permutation is unique (`sorted_unique`), so it is the insertion sort of any list with the
       same members;
     - `sort_step_reuse` (THE STEP THEOREM): a `Sort` refreshed against the record a prime left
       (the prior arrival and the prior produced order, as tokens), under the delta premise and a
       comparator that is a total preorder, answers the full stable sort of the rows that arrived —
       whether the merge ran or the condition failed and the step sorted from scratch;
     - `compare_resolved_preorder` (THE COMPARATOR THEOREM): over rows whose key cells are each of
       ONE family — the cells a typed column holds — `compare_resolved` is a total preorder, from
       the laws of the primitives it dispatches to; and `compare_cells_none` names exactly the pairs
       it reads as a tie: two families, or a float beside a decimal (`ColumnType.widens` refuses
       that retype), or a decimal whose text is not decimal text.

   WHAT IS A PARAMETER (`sprims`), each an assumption: the row comparator the walk and the reference
   both call (`row_cmp` — F#: `compareResolved (resolveSortKeys cols by)`), the float carrier's
   order (`cmp_float` — F#: `Kernels.compareFloat`), the decimal order (`cmp_dec` — F#:
   `DecimalText.compare`) and canonical form (`canon_dec` — F#: `DecimalText.tryCanonical`), the
   ordinal string order (`cmp_ord` — F#: `String.CompareOrdinal`). `sort_step_reuse` is conditional
   on `row_cmp` being a total preorder (`total_preorder`); `compare_resolved_preorder` proves that of
   the concrete comparator over typed rows from the primitives' own laws (`sprims_laws`: each is a
   total preorder on its carrier, and an int's two readings agree — `float_of_int` under
   `cmp_float` orders ints as `int_text` under `cmp_dec` does, which is the typing fact that a 32-bit
   int is exact in both carriers). THE TYPING FACT the two compose through is production's and is
   not a theorem of this module: every column of a frame a `Sort` reaches holds cells of one family
   (`typeFromCells` refuses two families beside each other, D18, D38 item 3; `Vec.packAt` carries
   an int under a float or decimal column as that type, or raw where the typed pass is abandoned,
   which is still ONE family — an int is exact in the float carrier and in the decimal one alike).
   Every type parameter is `Type0`.
*)
module MaintainedSort

open Pipeline

(* ======================================================================================
   1. Lists: membership, duplicates, filters, prefixes and suffixes.
   ====================================================================================== *)

let rec memb (#b:eqtype) (x:b) (l:list b) : Tot bool =
  match l with
  | [] -> false
  | h :: t -> h = x || memb x t

let rec nodup (#b:eqtype) (l:list b) : Tot bool =
  match l with
  | [] -> true
  | h :: t -> not (memb h t) && nodup t

let rec filter (#b:Type0) (p:b -> bool) (l:list b) : Tot (list b) =
  match l with
  | [] -> []
  | h :: t -> if p h then h :: filter p t else filter p t

let rec take (#b:Type0) (n:nat) (l:list b) : Tot (list b) (decreases l) =
  match l with
  | [] -> []
  | h :: t -> if n = 0 then [] else h :: take (n - 1) t

let rec drop (#b:Type0) (n:nat) (l:list b) : Tot (list b) (decreases l) =
  match l with
  | [] -> []
  | _ :: t -> if n = 0 then l else drop (n - 1) t

let rec memb_app (#b:eqtype) (x:b) (l m:list b)
  : Lemma (ensures memb x (app l m) == (memb x l || memb x m)) [SMTPat (memb x (app l m))] =
  match l with
  | [] -> ()
  | _ :: t -> memb_app x t m

let rec memb_filter (#b:eqtype) (p:b -> bool) (x:b) (l:list b)
  : Lemma (ensures memb x (filter p l) == (memb x l && p x)) [SMTPat (memb x (filter p l))] =
  match l with
  | [] -> ()
  | _ :: t -> memb_filter p x t

let rec nodup_filter (#b:eqtype) (p:b -> bool) (l:list b)
  : Lemma (requires nodup l) (ensures nodup (filter p l)) =
  match l with
  | [] -> ()
  | _ :: t -> nodup_filter p t

let rec app_take_drop (#b:Type0) (n:nat) (l:list b)
  : Lemma (ensures app (take n l) (drop n l) == l) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> if n = 0 then () else app_take_drop (n - 1) t

let rec memb_take (#b:eqtype) (x:b) (n:nat) (l:list b)
  : Lemma (requires memb x (take n l)) (ensures memb x l) (decreases l) [SMTPat (memb x (take n l))] =
  match l with
  | [] -> ()
  | _ :: t -> if n = 0 then () else if x = Cons?.hd l then () else memb_take x (n - 1) t

let rec memb_drop (#b:eqtype) (x:b) (n:nat) (l:list b)
  : Lemma (requires memb x (drop n l)) (ensures memb x l) (decreases l) [SMTPat (memb x (drop n l))] =
  match l with
  | [] -> ()
  | _ :: t -> if n = 0 then () else memb_drop x (n - 1) t

let rec nth_memb (#b:eqtype) (l:list b) (k:nat{k < len l})
  : Lemma (ensures memb (nth l k) l) [SMTPat (memb (nth l k) l)] =
  match l with
  | _ :: t -> if k = 0 then () else nth_memb t (k - 1)

(* ======================================================================================
   2. A strict total order over a list's members, sortedness, and the stable insertion sort —
      F#: `List.sortWith` (the reference `evalSort`), as the unique sorted permutation.
   ====================================================================================== *)

type lt_fn (b:Type0) = b -> b -> bool

(* The three laws, over the members of `l`: irreflexive, transitive, total on distinct members. *)
let sto_on (#b:eqtype) (lt:lt_fn b) (l:list b) : prop =
  (forall (x:b). memb x l ==> not (lt x x)) /\
  (forall (x y z:b). memb x l /\ memb y l /\ memb z l /\ lt x y /\ lt y z ==> lt x z) /\
  (forall (x y:b). memb x l /\ memb y l /\ x <> y ==> (lt x y \/ lt y x))

let subset (#b:eqtype) (l m:list b) : prop = forall (x:b). memb x l ==> memb x m

let sto_mono (#b:eqtype) (lt:lt_fn b) (l m:list b)
  : Lemma (requires sto_on lt m /\ subset l m) (ensures sto_on lt l) = ()

let rec sorted (#b:Type0) (lt:lt_fn b) (l:list b) : Tot bool =
  match l with
  | [] -> true
  | x :: t -> (match t with [] -> true | y :: _ -> lt x y && sorted lt t)

let rec insert (#b:Type0) (lt:lt_fn b) (x:b) (l:list b) : Tot (list b) =
  match l with
  | [] -> [x]
  | h :: t -> if lt h x then h :: insert lt x t else x :: l

let rec isort (#b:Type0) (lt:lt_fn b) (l:list b) : Tot (list b) =
  match l with
  | [] -> []
  | h :: t -> insert lt h (isort lt t)

let rec sorted_head_lt (#b:eqtype) (lt:lt_fn b) (h:b) (t:list b) (x:b)
  : Lemma (requires sorted lt (h :: t) /\ sto_on lt (h :: t) /\ memb x t) (ensures lt h x) (decreases t) =
  match t with
  | [] -> ()
  | y :: rest -> if y = x then () else (sto_mono lt (y :: rest) (h :: t); sorted_head_lt lt y rest x)

let sorted_cons (#b:eqtype) (lt:lt_fn b) (h:b) (t:list b)
  : Lemma (requires sorted lt t /\ (forall (x:b). memb x t ==> lt h x)) (ensures sorted lt (h :: t)) = ()

let rec sorted_app (#b:eqtype) (lt:lt_fn b) (l m:list b)
  : Lemma (requires sorted lt l /\ sorted lt m /\ (forall (x z:b). memb x l /\ memb z m ==> lt x z))
          (ensures sorted lt (app l m)) =
  match l with
  | [] -> ()
  | h :: t -> sorted_app lt t m

let rec sorted_take (#b:eqtype) (lt:lt_fn b) (n:nat) (l:list b)
  : Lemma (requires sorted lt l) (ensures sorted lt (take n l)) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> if n = 0 then () else sorted_take lt (n - 1) t

let rec sorted_drop (#b:eqtype) (lt:lt_fn b) (n:nat) (l:list b)
  : Lemma (requires sorted lt l) (ensures sorted lt (drop n l)) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> if n = 0 then () else sorted_drop lt (n - 1) t

let rec insert_memb (#b:eqtype) (lt:lt_fn b) (x:b) (l:list b) (y:b)
  : Lemma (ensures memb y (insert lt x l) == (y = x || memb y l)) [SMTPat (memb y (insert lt x l))] =
  match l with
  | [] -> ()
  | h :: t -> if lt h x then insert_memb lt x t y else ()

let rec insert_sorted (#b:eqtype) (lt:lt_fn b) (x:b) (l:list b)
  : Lemma (requires sorted lt l /\ sto_on lt (x :: l) /\ not (memb x l)) (ensures sorted lt (insert lt x l)) =
  match l with
  | [] -> ()
  | h :: t ->
    if lt h x then begin
      sto_mono lt (x :: t) (x :: l);
      insert_sorted lt x t;
      let after (y:b) : Lemma (requires memb y (insert lt x t)) (ensures lt h y) =
        if y = x then () else sorted_head_lt lt h t y in
      FStar.Classical.forall_intro (FStar.Classical.move_requires after);
      sorted_cons lt h (insert lt x t)
    end
    else ()

let rec isort_memb (#b:eqtype) (lt:lt_fn b) (l:list b) (y:b)
  : Lemma (ensures memb y (isort lt l) == memb y l) [SMTPat (memb y (isort lt l))] =
  match l with
  | [] -> ()
  | h :: t -> isort_memb lt t y

let rec isort_sorted (#b:eqtype) (lt:lt_fn b) (l:list b)
  : Lemma (requires nodup l /\ sto_on lt l) (ensures sorted lt (isort lt l)) =
  match l with
  | [] -> ()
  | h :: t ->
    sto_mono lt t l;
    isort_sorted lt t;
    sto_mono lt (h :: isort lt t) l;
    insert_sorted lt h (isort lt t)

let rec sorted_nodup (#b:eqtype) (lt:lt_fn b) (l:list b)
  : Lemma (requires sorted lt l /\ sto_on lt l) (ensures nodup l) =
  match l with
  | [] -> ()
  | h :: t -> (if memb h t then sorted_head_lt lt h t h else ()); sto_mono lt t l; sorted_nodup lt t

(* THE UNIQUENESS OF A SORTED PERMUTATION: two sorted lists with the same members are one list. *)
let rec sorted_unique (#b:eqtype) (lt:lt_fn b) (l1 l2:list b)
  : Lemma (requires sorted lt l1 /\ sorted lt l2 /\ sto_on lt l1 /\ (forall (x:b). memb x l1 <==> memb x l2))
          (ensures l1 == l2) (decreases l1) =
  match l1, l2 with
  | [], [] -> ()
  | [], _ :: _ -> ()
  | _ :: _, [] -> ()
  | h1 :: t1, h2 :: t2 ->
    sto_mono lt l2 l1;
    if h1 = h2 then begin
      sorted_nodup lt l1;
      sorted_nodup lt l2;
      sto_mono lt t1 l1;
      sorted_unique lt t1 t2
    end
    else begin
      sorted_head_lt lt h2 t2 h1;
      sorted_head_lt lt h1 t1 h2
    end

let rec sorted_nth (#b:eqtype) (lt:lt_fn b) (l:list b) (i:nat) (j:nat{i < j /\ j < len l})
  : Lemma (requires sorted lt l /\ sto_on lt l) (ensures lt (nth l i) (nth l j)) (decreases l) =
  match l with
  | h :: t ->
    if i = 0 then sorted_head_lt lt h t (nth t (j - 1))
    else (sto_mono lt t l; sorted_nth lt t (i - 1) (j - 1))

(* ======================================================================================
   3. Binary insertion — F#: `mergeOrders` (Phase 274): each moved row placed in the cached run by
      bisection under "goes out before", the run before it copied whole, the search continuing
      from there.
   ====================================================================================== *)

(* F#: the `while a < b` loop — the least position in `xs[lo, hi)` not before `y`. *)
let rec bisect (#b:Type0) (lt:lt_fn b) (xs:list b) (y:b) (lo:nat) (hi:nat{lo <= hi /\ hi <= len xs})
  : Tot (r:nat{lo <= r /\ r <= hi}) (decreases (hi - lo)) =
  if lo < hi then begin
    let m = (lo + hi) / 2 in
    if lt (nth xs m) y then bisect lt xs y (m + 1) hi else bisect lt xs y lo m
  end
  else lo

let rec all_lt_take (#b:eqtype) (lt:lt_fn b) (xs:list b) (y:b) (n:nat)
  : Lemma (requires n <= len xs /\ (forall (k:nat). k < n ==> lt (nth xs k) y))
          (ensures forall (x:b). memb x (take n xs) ==> lt x y) (decreases xs) =
  match xs with
  | [] -> ()
  | h :: t ->
    if n = 0 then ()
    else begin
      assert (len xs == 1 + len t);
      let shift (k:nat) : Lemma (ensures k < n - 1 ==> lt (nth t k) y) =
        if k < n - 1 then (assert (k + 1 < len xs); assert (nth xs (k + 1) == nth t k)) else () in
      FStar.Classical.forall_intro shift;
      all_lt_take lt t y (n - 1)
    end

let rec all_nth_memb (#b:eqtype) (p:b -> bool) (l:list b)
  : Lemma (requires forall (k:nat). k < len l ==> p (nth l k)) (ensures forall (x:b). memb x l ==> p x) =
  match l with
  | [] -> ()
  | h :: t ->
    assert (len l == 1 + len t);
    assert (p (nth l 0));
    let shift (k:nat) : Lemma (ensures k < len t ==> p (nth t k)) =
      if k < len t then (assert (k + 1 < len l); assert (nth l (k + 1) == nth t k)) else () in
    FStar.Classical.forall_intro shift;
    all_nth_memb p t

let not_lt (#b:Type0) (lt:lt_fn b) (y:b) (x:b) : Tot bool = not (lt x y)

let rec all_not_lt_drop (#b:eqtype) (lt:lt_fn b) (xs:list b) (y:b) (n:nat)
  : Lemma (requires n <= len xs /\ (forall (k:nat). n <= k /\ k < len xs ==> not (lt (nth xs k) y)))
          (ensures forall (x:b). memb x (drop n xs) ==> not (lt x y)) (decreases xs) =
  match xs with
  | [] -> ()
  | h :: t ->
    assert (len xs == 1 + len t);
    if n = 0 then all_nth_memb (not_lt lt y) xs
    else begin
      let shift (k:nat) : Lemma (ensures (n - 1 <= k /\ k < len t) ==> not (lt (nth t k) y)) =
        if n - 1 <= k && k < len t then (assert (k + 1 < len xs); assert (nth xs (k + 1) == nth t k)) else () in
      FStar.Classical.forall_intro shift;
      all_not_lt_drop lt t y (n - 1)
    end

(* The loop's invariant, and what it leaves: every position before the cut is before `y`, none
   from the cut on is. *)
let rec bisect_cut (#b:eqtype) (lt:lt_fn b) (xs:list b) (y:b) (lo:nat) (hi:nat{lo <= hi /\ hi <= len xs})
  : Lemma (requires sorted lt xs /\ sto_on lt (y :: xs) /\ not (memb y xs)
                    /\ (forall (k:nat). k < lo ==> lt (nth xs k) y)
                    /\ (forall (k:nat). hi <= k /\ k < len xs ==> not (lt (nth xs k) y)))
          (ensures (let r = bisect lt xs y lo hi in
                    (forall (k:nat). k < r ==> lt (nth xs k) y)
                    /\ (forall (k:nat). r <= k /\ k < len xs ==> not (lt (nth xs k) y))))
          (decreases (hi - lo)) =
  if lo < hi then begin
    let m = (lo + hi) / 2 in
    sto_mono lt xs (y :: xs);
    if lt (nth xs m) y then begin
      let before (k:nat) : Lemma (ensures k < m + 1 ==> lt (nth xs k) y) =
        if k < m then sorted_nth lt xs k m else () in
      FStar.Classical.forall_intro before;
      bisect_cut lt xs y (m + 1) hi
    end
    else begin
      let after (k:nat) : Lemma (ensures (m <= k /\ k < len xs) ==> not (lt (nth xs k) y)) =
        if m < k && k < len xs then sorted_nth lt xs m k else () in
      FStar.Classical.forall_intro after;
      bisect_cut lt xs y lo m
    end
  end
  else ()

(* F#: `mergeOrders` — `xs` the cached run, `ys` the moved rows, both sorted. *)
let rec merge_bin (#b:Type0) (lt:lt_fn b) (xs ys:list b) : Tot (list b) (decreases ys) =
  match ys with
  | [] -> xs
  | y :: rest ->
    let r = bisect lt xs y 0 (len xs) in
    app (take r xs) (y :: merge_bin lt (drop r xs) rest)

let disjoint (#b:eqtype) (l m:list b) : prop = forall (x:b). memb x l ==> not (memb x m)

let rec merge_bin_sorted (#b:eqtype) (lt:lt_fn b) (xs ys:list b)
  : Lemma (requires sorted lt xs /\ sorted lt ys /\ sto_on lt (app xs ys) /\ disjoint xs ys)
          (ensures sorted lt (merge_bin lt xs ys)
                   /\ (forall (x:b). memb x (merge_bin lt xs ys) <==> (memb x xs \/ memb x ys)))
          (decreases ys) =
  match ys with
  | [] -> ()
  | y :: rest ->
    let r = bisect lt xs y 0 (len xs) in
    let tk = take r xs in
    let dr = drop r xs in
    app_take_drop r xs;
    let in_xs (x:b) : Lemma (memb x xs <==> (memb x tk \/ memb x dr)) = memb_app x tk dr in
    FStar.Classical.forall_intro in_xs;
    let sub1 (x:b) : Lemma (memb x (y :: xs) ==> memb x (app xs ys)) = memb_app x xs ys in
    FStar.Classical.forall_intro sub1;
    sto_mono lt (y :: xs) (app xs ys);
    bisect_cut lt xs y 0 (len xs);
    sorted_take lt r xs;
    sorted_drop lt r xs;
    let sub2 (x:b) : Lemma (memb x (app dr rest) ==> memb x (app xs ys)) = memb_app x dr rest; memb_app x xs ys in
    FStar.Classical.forall_intro sub2;
    sto_mono lt (app dr rest) (app xs ys);
    let dj (x:b) : Lemma (memb x dr ==> not (memb x rest)) = () in
    FStar.Classical.forall_intro dj;
    merge_bin_sorted lt dr rest;
    let m = merge_bin lt dr rest in
    all_lt_take lt xs y r;
    all_not_lt_drop lt xs y r;
    let above (z:b) : Lemma (requires memb z m) (ensures lt y z) =
      memb_app z xs ys;
      memb_app y xs ys;
      if memb z dr then () else sorted_head_lt lt y rest z in
    FStar.Classical.forall_intro (FStar.Classical.move_requires above);
    sorted_cons lt y m;
    let across (x z:b) : Lemma (requires memb x tk /\ memb z (y :: m)) (ensures lt x z) =
      memb_app x xs ys;
      memb_app y xs ys;
      memb_app z xs ys in
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 across);
    sorted_app lt tk (y :: m);
    let fin (x:b) : Lemma (memb x (merge_bin lt xs ys) <==> (memb x xs \/ memb x ys)) =
      memb_app x tk (y :: m) in
    FStar.Classical.forall_intro fin

(* THE MERGE IS THE STABLE SORT: with the members of a duplicate-free list, it is that list's
   insertion sort — the unique sorted permutation. *)
let merge_is_isort (#b:eqtype) (lt:lt_fn b) (xs ys l:list b)
  : Lemma (requires sorted lt xs /\ sorted lt ys /\ sto_on lt l /\ nodup l /\ disjoint xs ys
                    /\ (forall (x:b). memb x l <==> (memb x xs \/ memb x ys)))
          (ensures merge_bin lt xs ys == isort lt l) =
  sto_mono lt (app xs ys) l;
  merge_bin_sorted lt xs ys;
  isort_sorted lt l;
  sto_mono lt (merge_bin lt xs ys) l;
  sorted_unique lt (merge_bin lt xs ys) (isort lt l)

(* ======================================================================================
   4. Positions — the arrival position as the tie-break (F#: `posOf`), and the strict total order
      a total preorder and distinct positions make.
   ====================================================================================== *)

let rec index_from (#e:Type0) (k:nat) (l:list e) : Tot (list (nat & e)) (decreases l) =
  match l with
  | [] -> []
  | h :: t -> (k, h) :: index_from (k + 1) t

let indexed (#e:Type0) (l:list e) : Tot (list (nat & e)) = index_from 0 l

let rec unidx (#e:Type0) (l:list (nat & e)) : Tot (list e) =
  match l with
  | [] -> []
  | (_, x) :: t -> x :: unidx t

let rec unidx_index_from (#e:Type0) (k:nat) (l:list e)
  : Lemma (ensures unidx (index_from k l) == l) (decreases l) =
  match l with
  | [] -> ()
  | _ :: t -> unidx_index_from (k + 1) t

let rec memb_index_from (#e:eqtype) (k:nat) (l:list e) (i:nat) (x:e)
  : Lemma (requires memb (i, x) (index_from k l)) (ensures k <= i /\ i < k + len l /\ nth l (i - k) == x) (decreases l) =
  match l with
  | [] -> ()
  | h :: t -> if i = k && x = h then () else memb_index_from (k + 1) t i x

let rec index_from_memb (#e:eqtype) (k:nat) (l:list e) (j:nat{j < len l})
  : Lemma (ensures memb (k + j, nth l j) (index_from k l)) (decreases l) =
  match l with
  | _ :: t -> if j = 0 then () else index_from_memb (k + 1) t (j - 1)

let rec same_pos (#e:eqtype) (k:nat) (l:list e) (i:nat) (x y:e)
  : Lemma (requires memb (i, x) (index_from k l) /\ memb (i, y) (index_from k l)) (ensures x == y) (decreases l) =
  match l with
  | [] -> ()
  | h :: t -> if i = k then (memb_index_from k l i x; memb_index_from k l i y) else same_pos (k + 1) t i x y

let rec nodup_index_from (#e:eqtype) (k:nat) (l:list e)
  : Lemma (ensures nodup (index_from k l)) (decreases l) =
  match l with
  | [] -> ()
  | h :: t -> (if memb (k, h) (index_from (k + 1) t) then memb_index_from (k + 1) t k h else ()); nodup_index_from (k + 1) t

let rec memb_unidx (#e:eqtype) (l:list (nat & e)) (i:nat) (x:e)
  : Lemma (requires memb (i, x) l) (ensures memb x (unidx l)) =
  match l with
  | [] -> ()
  | (j, y) :: t -> if j = i && y = x then () else memb_unidx t i x

let rec unidx_memb (#e:eqtype) (l:list (nat & e)) (x:e)
  : Lemma (requires memb x (unidx l)) (ensures exists (i:nat). memb (i, x) l) =
  match l with
  | [] -> ()
  | (j, y) :: t -> if y = x then () else unidx_memb t x

(* A total preorder — F#: a comparator `compare`'s laws: reflexive, sign-antisymmetric, transitive. *)
let total_preorder (#e:Type0) (cmp:e -> e -> int) : prop =
  (forall (x:e). cmp x x == 0) /\
  (forall (x y:e). (cmp x y < 0 <==> cmp y x > 0) /\ (cmp x y == 0 <==> cmp y x == 0)) /\
  (forall (x y z:e). cmp x y <= 0 /\ cmp y z <= 0 ==> cmp x z <= 0)

(* F#: `before` in `mergeOrders` and `sortSlots`'s comparison — the comparator, then the position. *)
let lt_of (#e:Type0) (cmp:e -> e -> int) (p q:nat & e) : Tot bool =
  let (i, x) = p in
  let (j, y) = q in
  cmp x y < 0 || (cmp x y = 0 && i < j)

let lt_of_sto (#e:eqtype) (cmp:e -> e -> int) (l:list (nat & e))
  : Lemma (requires total_preorder cmp /\ (forall (i:nat) (x y:e). memb (i, x) l /\ memb (i, y) l ==> x == y))
          (ensures sto_on (lt_of cmp) l) = ()

let lt_of_sto_indexed (#e:eqtype) (cmp:e -> e -> int) (l:list e)
  : Lemma (requires total_preorder cmp) (ensures sto_on (lt_of cmp) (indexed l) /\ nodup (indexed l)) =
  let same (i:nat) (x y:e) : Lemma (requires memb (i, x) (indexed l) /\ memb (i, y) (indexed l)) (ensures x == y) =
    same_pos 0 l i x y in
  FStar.Classical.forall_intro_3 (FStar.Classical.move_requires_3 same);
  lt_of_sto cmp (indexed l);
  nodup_index_from 0 l

(* THE STABLE SORT — F#: `evalSort` and `sortSlots`: the rows under the comparator, a tie broken by
   the arrival position. *)
let sort_by (#e:eqtype) (cmp:e -> e -> int) (l:list e) : Tot (list e) = unidx (isort (lt_of cmp) (indexed l))

let rec lmap (#b #c:Type0) (f:b -> c) (l:list b) : Tot (list c) =
  match l with
  | [] -> []
  | h :: t -> f h :: lmap f t

let rec insert_map (#b #c:Type0) (f:b -> c) (lt1:lt_fn b) (lt2:lt_fn c) (x:b) (l:list b)
  : Lemma (requires forall (p q:b). lt1 p q == lt2 (f p) (f q))
          (ensures lmap f (insert lt1 x l) == insert lt2 (f x) (lmap f l)) =
  match l with
  | [] -> ()
  | h :: t -> if lt1 h x then insert_map f lt1 lt2 x t else ()

let rec isort_map (#b #c:Type0) (f:b -> c) (lt1:lt_fn b) (lt2:lt_fn c) (l:list b)
  : Lemma (requires forall (p q:b). lt1 p q == lt2 (f p) (f q))
          (ensures lmap f (isort lt1 l) == isort lt2 (lmap f l)) =
  match l with
  | [] -> ()
  | h :: t -> isort_map f lt1 lt2 t; insert_map f lt1 lt2 h (isort lt1 t)

(* ======================================================================================
   5. The rows under their tokens — the walk's frame, as `Incremental.fst` and
      `IncrementalMaintained.fst` carry it, and the delta premise read one row at a time.
   ====================================================================================== *)

let rec tokens_of (a:Incremental.alive) : Tot (list Incremental.token) =
  match a with
  | [] -> []
  | (t, _) :: rest -> t :: tokens_of rest

let rec distinct (a:Incremental.alive) : Tot bool =
  match a with
  | [] -> true
  | (t, _) :: rest -> not (Incremental.mem_token t (tokens_of rest)) && distinct rest

let rec mem_a (x:Incremental.token & list cell) (a:Incremental.alive) : Tot bool =
  match a with
  | [] -> false
  | y :: rest -> y = x || mem_a x rest

(* A reusable row the prior source held under its token has the cells it had; one it did not hold is
   admitted (the cache misses it). Weaker than `Incremental.unchanged`. *)
let rec wk (reuse:Incremental.reuse_fn) (a:Incremental.alive) (a0:Incremental.alive) : Tot bool (decreases a) =
  match a with
  | [] -> true
  | (t, r) :: rest ->
    (not (reuse t) || (match assoc t a0 with Some r0 -> r0 = r | None -> true)) && wk reuse rest a0

let ok_row (reuse:Incremental.reuse_fn) (a0:Incremental.alive) (t:Incremental.token) (r:list cell) : Tot bool =
  not (reuse t) || (match assoc t a0 with Some r0 -> r0 = r | None -> true)

let rec wk_mem (reuse:Incremental.reuse_fn) (a a0:Incremental.alive) (t:Incremental.token) (r:list cell)
  : Lemma (requires wk reuse a a0 /\ mem_a (t, r) a) (ensures ok_row reuse a0 t r) (decreases a) =
  match a with
  | [] -> ()
  | y :: rest -> if y = (t, r) then () else wk_mem reuse rest a0 t r

let rec wk_of_rows (reuse:Incremental.reuse_fn) (a a0:Incremental.alive)
  : Lemma (requires forall (t:Incremental.token) (r:list cell). mem_a (t, r) a ==> ok_row reuse a0 t r)
          (ensures wk reuse a a0) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> wk_of_rows reuse rest a0

let rec mem_tokens (a:Incremental.alive) (t:Incremental.token) (r:list cell)
  : Lemma (requires mem_a (t, r) a) (ensures Incremental.mem_token t (tokens_of a)) =
  match a with
  | [] -> ()
  | y :: rest -> if y = (t, r) then () else mem_tokens rest t r

let rec assoc_of_mem (a0:Incremental.alive) (t:Incremental.token) (r:list cell)
  : Lemma (requires distinct a0 /\ mem_a (t, r) a0) (ensures assoc t a0 == Some r) =
  match a0 with
  | [] -> ()
  | (t0, r0) :: rest ->
    if (t0, r0) = (t, r) then ()
    else if t0 = t then mem_tokens rest t r
    else assoc_of_mem rest t r

let rec assoc_mem (a:Incremental.alive) (t:Incremental.token) (r:list cell)
  : Lemma (requires assoc t a == Some r) (ensures mem_a (t, r) a) =
  match a with
  | [] -> ()
  | (t0, _) :: rest -> if t0 = t then () else assoc_mem rest t r

let rec assoc_none (a:Incremental.alive) (t:Incremental.token) (r:list cell)
  : Lemma (requires assoc t a == None) (ensures not (mem_a (t, r) a)) =
  match a with
  | [] -> ()
  | _ :: rest -> assoc_none rest t r

let rec mem_token_is_memb (t:Incremental.token) (l:list Incremental.token)
  : Lemma (ensures Incremental.mem_token t l == memb t l) [SMTPat (Incremental.mem_token t l)] =
  match l with
  | [] -> ()
  | _ :: rest -> mem_token_is_memb t rest

let rec mem_a_is_memb (x:Incremental.token & list cell) (a:Incremental.alive)
  : Lemma (ensures mem_a x a == memb x a) [SMTPat (mem_a x a)] =
  match a with
  | [] -> ()
  | _ :: rest -> mem_a_is_memb x rest

let rec distinct_nodup (a:Incremental.alive) : Lemma (requires distinct a) (ensures nodup (tokens_of a)) =
  match a with
  | [] -> ()
  | _ :: rest -> distinct_nodup rest

let rec awidth_mem (n:nat) (a:Incremental.alive) (t:Incremental.token) (r:list cell)
  : Lemma (requires Incremental.awidth n a /\ mem_a (t, r) a) (ensures len r = n) =
  match a with
  | [] -> ()
  | y :: rest -> if y = (t, r) then () else awidth_mem n rest t r

let rec awidth_of_members (n:nat) (a b:Incremental.alive)
  : Lemma (requires Incremental.awidth n a /\ (forall (x:Incremental.token & list cell). mem_a x b ==> mem_a x a))
          (ensures Incremental.awidth n b) =
  match b with
  | [] -> ()
  | (t, r) :: rest -> awidth_mem n a t r; awidth_of_members n a rest

(* The rows of a permutation of a distinct source are found by `assoc` as in the source. *)
let assoc_perm (a0 b:Incremental.alive) (t:Incremental.token)
  : Lemma (requires distinct a0 /\ distinct b /\ (forall (x:Incremental.token & list cell). mem_a x b <==> mem_a x a0))
          (ensures assoc t b == assoc t a0) =
  match assoc t a0, assoc t b with
  | Some r, _ -> assoc_mem a0 t r; assoc_of_mem b t r
  | None, Some r -> assoc_mem b t r; assoc_none a0 t r
  | None, None -> ()

(* ======================================================================================
   6. The tokens' positions: the first index of a token (F#: `posOf` through the slot), and how a
      filter keeps the relative order of what it keeps — the fact `arrivedAsBefore` rests on.
   ====================================================================================== *)

let rec idx (#b:eqtype) (t:b) (l:list b) : Tot nat =
  match l with
  | [] -> 0
  | h :: rest -> if h = t then 0 else 1 + idx t rest

(* Two kept members are in the same relative order in the list and in its filter. *)
let rec filter_order (#b:eqtype) (p:b -> bool) (l:list b) (t u:b)
  : Lemma (requires p t /\ p u /\ t <> u /\ memb t l /\ memb u l)
          (ensures (idx t l < idx u l) <==> (idx t (filter p l) < idx u (filter p l))) =
  match l with
  | [] -> ()
  | h :: rest -> if h = t || h = u then () else filter_order p rest t u

(* A token's position in the indexed rows is its first index, where it occurs once. *)
let rec pos_is_idx (k:nat) (l:Incremental.alive) (i:nat) (t:Incremental.token) (r:list cell)
  : Lemma (requires memb (i, (t, r)) (index_from k l)
                    /\ (forall (j:nat) (r':list cell). memb (j, (t, r')) (index_from k l) ==> j == i))
          (ensures i == k + idx t (tokens_of l)) (decreases l) =
  match l with
  | [] -> ()
  | (u, s) :: rest -> if u = t then () else pos_is_idx (k + 1) rest i t r

let rec index_memb_tok (k:nat) (l:Incremental.alive) (i:nat) (t:Incremental.token) (r:list cell)
  : Lemma (requires memb (i, (t, r)) (index_from k l)) (ensures memb t (tokens_of l) /\ mem_a (t, r) l) (decreases l) =
  match l with
  | [] -> ()
  | (u, s) :: rest -> if i = k && (u, s) = (t, r) then () else index_memb_tok (k + 1) rest i t r

let rec tok_index_memb (k:nat) (l:Incremental.alive) (t:Incremental.token)
  : Lemma (requires memb t (tokens_of l)) (ensures exists (i:nat) (r:list cell). memb (i, (t, r)) (index_from k l)) (decreases l) =
  match l with
  | [] -> ()
  | (u, s) :: rest -> if u = t then assert (memb (k, (u, s)) (index_from k l)) else tok_index_memb (k + 1) rest t

(* In a distinct source a token has one position. *)
let rec distinct_pos (k:nat) (l:Incremental.alive) (i j:nat) (t:Incremental.token) (r r':list cell)
  : Lemma (requires distinct l /\ memb (i, (t, r)) (index_from k l) /\ memb (j, (t, r')) (index_from k l))
          (ensures i == j) (decreases l) =
  match l with
  | [] -> ()
  | (u, s) :: rest ->
    if u = t then begin
      (if i <> k then index_memb_tok (k + 1) rest i t r else ());
      (if j <> k then index_memb_tok (k + 1) rest j t r' else ())
    end
    else distinct_pos (k + 1) rest i j t r r'

(* F#: the `Stable` tokens of an arrival, in arrival order. *)
let rec stable_tokens (reuse:Incremental.reuse_fn) (ts:list Incremental.token) : Tot (list Incremental.token) =
  match ts with
  | [] -> []
  | t :: rest -> if reuse t then t :: stable_tokens reuse rest else stable_tokens reuse rest

let rec stable_tokens_is_filter (reuse:Incremental.reuse_fn) (ts:list Incremental.token)
  : Lemma (ensures stable_tokens reuse ts == filter reuse ts) [SMTPat (stable_tokens reuse ts)] =
  match ts with
  | [] -> ()
  | _ :: rest -> stable_tokens_is_filter reuse rest

(* F#: `stableNowOf` read over the prior arrival — present now, and `Stable`. *)
let present (reuse:Incremental.reuse_fn) (now:list Incremental.token) (t:Incremental.token) : Tot bool =
  reuse t && memb t now

let rec still_here (reuse:Incremental.reuse_fn) (now prev:list Incremental.token) : Tot (list Incremental.token) =
  match prev with
  | [] -> []
  | t :: rest -> if reuse t && memb t now then t :: still_here reuse now rest else still_here reuse now rest

let rec still_here_is_filter (reuse:Incremental.reuse_fn) (now prev:list Incremental.token)
  : Lemma (ensures still_here reuse now prev == filter (present reuse now) prev) [SMTPat (still_here reuse now prev)] =
  match prev with
  | [] -> ()
  | _ :: rest -> still_here_is_filter reuse now rest

(* F#: `arrivedAsBefore` — equal sequences, compared without building either. *)
let arrived_as_before (reuse:Incremental.reuse_fn) (prev now:list Incremental.token) : Tot bool =
  still_here reuse now prev = stable_tokens reuse now

(* Where the stable tokens of an arrival are distinct, a stable token has one position in it. *)
let rec stable_pos (reuse:Incremental.reuse_fn) (k:nat) (l:Incremental.alive) (i j:nat) (t:Incremental.token) (r r':list cell)
  : Lemma (requires reuse t /\ nodup (stable_tokens reuse (tokens_of l))
                    /\ memb (i, (t, r)) (index_from k l) /\ memb (j, (t, r')) (index_from k l))
          (ensures i == j) (decreases l) =
  match l with
  | [] -> ()
  | (u, s) :: rest ->
    if u = t then begin
      (if i <> k then index_memb_tok (k + 1) rest i t r else ());
      (if j <> k then index_memb_tok (k + 1) rest j t r' else ())
    end
    else stable_pos reuse (k + 1) rest i j t r r'

(* ======================================================================================
   7. The maintained step — F#: `walk`'s `WSort` arm.
   ====================================================================================== *)

type ielem = nat & (Incremental.token & list cell)

let tok (x:Incremental.token & list cell) : Tot Incremental.token = fst x

(* The comparator over the rows, lifted over their tokens. *)
let cmp_tok (cmp:list cell -> list cell -> int) (x y:Incremental.token & list cell) : Tot int = cmp (snd x) (snd y)

let cmp_tok_preorder (cmp:list cell -> list cell -> int)
  : Lemma (requires total_preorder cmp) (ensures total_preorder (cmp_tok cmp)) = ()

(* THE FULL STABLE SORT over the arrival — F#: `sortSlots (cmpReading false) arrival`. *)
let sort_alive (cmp:list cell -> list cell -> int) (a:Incremental.alive) : Tot Incremental.alive =
  sort_by (cmp_tok cmp) a

(* F#: `stableNow p` for a prior slot — the row that holds the token now. *)
let rec find_tok (t:Incremental.token) (l:list ielem) : Tot (option ielem) =
  match l with
  | [] -> None
  | (i, (u, r)) :: rest -> if u = t then Some (i, (u, r)) else find_tok t rest

(* F#: `for p in prevOrder do … kept.Add s` — the prior produced order's tokens read through the
   rows that hold them now, those no longer here dropped. *)
let rec lookup_order (ts:list Incremental.token) (ia:list ielem) : Tot (list ielem) =
  match ts with
  | [] -> []
  | t :: rest -> (match find_tok t ia with Some x -> x :: lookup_order rest ia | None -> lookup_order rest ia)

(* F#: `arrival |> Array.filter (fun s -> not r.Stable[s])`. *)
let rec unstable (reuse:Incremental.reuse_fn) (ia:list ielem) : Tot (list ielem) =
  match ia with
  | [] -> []
  | (i, (t, r)) :: rest -> if reuse t then unstable reuse rest else (i, (t, r)) :: unstable reuse rest

(* F#: `mergeOrders cmp posOf cachedStable moved`. *)
let merge_sort (cmp:list cell -> list cell -> int) (reuse:Incremental.reuse_fn) (prev_order:list Incremental.token) (a:Incremental.alive)
  : Tot Incremental.alive =
  let ia = indexed a in
  let cached = lookup_order (stable_tokens reuse prev_order) ia in
  let moved = isort (lt_of (cmp_tok cmp)) (unstable reuse ia) in
  unidx (merge_bin (lt_of (cmp_tok cmp)) cached moved)

(* F#: `SortOrders[sortIdx] = (arrival, ordered)` — the arrival and the produced order, as tokens. *)
type sort_cache = list Incremental.token & list Incremental.token

(* F#: the `WSort` arm — the cached order merged where the stable rows arrived as before, the
   whole arrival sorted otherwise. *)
let sort_step (cmp:list cell -> list cell -> int) (reuse:Incremental.reuse_fn) (prior:option sort_cache) (a:Incremental.alive)
  : Tot Incremental.alive =
  match prior with
  | Some (prev_arrival, prev_order) ->
    if arrived_as_before reuse prev_arrival (tokens_of a) then merge_sort cmp reuse prev_order a
    else sort_alive cmp a
  | None -> sort_alive cmp a

let sort_record (a ordered:Incremental.alive) : Tot sort_cache = (tokens_of a, tokens_of ordered)

(* ---- the sorted arrival is a permutation of the arrival ---- *)

let sort_alive_memb (cmp:list cell -> list cell -> int) (a:Incremental.alive) (x:Incremental.token & list cell)
  : Lemma (ensures mem_a x (sort_alive cmp a) == mem_a x a) [SMTPat (mem_a x (sort_alive cmp a))] =
  let lt = lt_of (cmp_tok cmp) in
  let s = isort lt (indexed a) in
  if memb x (unidx s) then begin
    unidx_memb s x;
    let w (i:nat) : Lemma (requires memb (i, x) s) (ensures memb x a) = memb_index_from 0 a i x; nth_memb a (i - 0) in
    FStar.Classical.forall_intro (FStar.Classical.move_requires w)
  end
  else begin
    if memb x a then begin
      let i = idx x a in
      let rec at_idx (l:Incremental.alive) (y:Incremental.token & list cell)
        : Lemma (requires memb y l) (ensures idx y l < len l /\ nth l (idx y l) == y) =
        match l with
        | h :: t -> if h = y then () else at_idx t y in
      at_idx a x;
      index_from_memb 0 a i;
      memb_unidx s i x
    end
    else ()
  end

let rec unidx_sub_distinct (a0:Incremental.alive) (l:list ielem)
  : Lemma (requires distinct a0 /\ nodup l /\ (forall (x:ielem). memb x l ==> memb x (indexed a0)))
          (ensures distinct (unidx l)) =
  match l with
  | [] -> ()
  | (i, (t, r)) :: rest ->
    unidx_sub_distinct a0 rest;
    if memb t (tokens_of (unidx rest)) then begin
      tok_index_memb 0 (unidx rest) t;
      let w (j:nat) (r':list cell) : Lemma (requires memb (j, (t, r')) (index_from 0 (unidx rest))) (ensures False) =
        index_memb_tok 0 (unidx rest) j t r';
        unidx_memb rest (t, r');
        let w2 (j2:nat) : Lemma (requires memb (j2, (t, r')) rest) (ensures False) =
          distinct_pos 0 a0 i j2 t r r';
          same_pos 0 a0 i (t, r) (t, r') in
        FStar.Classical.forall_intro (FStar.Classical.move_requires w2) in
      FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 w)
    end
    else ()

let sort_alive_distinct (cmp:list cell -> list cell -> int) (a0:Incremental.alive)
  : Lemma (requires distinct a0) (ensures distinct (sort_alive cmp a0)) =
  let lt = lt_of (cmp_tok cmp) in
  nodup_index_from 0 a0;
  if total_preorder (cmp_tok cmp) then () else ();
  (* nodup of the sorted list: a sorted list under a strict total order has no duplicate, but the
     comparator may be anything here — so read duplicates off the members instead. *)
  let rec isort_nodup (l:list ielem) : Lemma (requires nodup l) (ensures nodup (isort lt l)) =
    match l with
    | [] -> ()
    | h :: t ->
      isort_nodup t;
      let rec ins_nodup (x:ielem) (m:list ielem) : Lemma (requires nodup m /\ not (memb x m)) (ensures nodup (insert lt x m)) =
        match m with
        | [] -> ()
        | y :: ys -> if lt y x then ins_nodup x ys else () in
      ins_nodup h (isort lt t) in
  isort_nodup (indexed a0);
  unidx_sub_distinct a0 (isort lt (indexed a0))

let sort_alive_width (n:nat) (cmp:list cell -> list cell -> int) (a:Incremental.alive)
  : Lemma (requires Incremental.awidth n a) (ensures Incremental.awidth n (sort_alive cmp a)) =
  awidth_of_members n a (sort_alive cmp a)

(* The delta premise survives the sort on both sides. *)
let sort_alive_wk (cmp:list cell -> list cell -> int) (reuse:Incremental.reuse_fn) (a a0:Incremental.alive)
  : Lemma (requires wk reuse a a0 /\ distinct a0)
          (ensures wk reuse (sort_alive cmp a) (sort_alive cmp a0) /\ distinct (sort_alive cmp a0)) =
  sort_alive_distinct cmp a0;
  let ok (t:Incremental.token) (r:list cell)
    : Lemma (requires mem_a (t, r) (sort_alive cmp a)) (ensures ok_row reuse (sort_alive cmp a0) t r) =
    wk_mem reuse a a0 t r;
    assoc_perm a0 (sort_alive cmp a0) t in
  FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 ok);
  wk_of_rows reuse (sort_alive cmp a) (sort_alive cmp a0)

(* ---- the merge is the full sort ---- *)

let rec find_tok_memb (t:Incremental.token) (ia:list ielem) (x:ielem)
  : Lemma (requires find_tok t ia == Some x) (ensures memb x ia /\ tok (snd x) == t) =
  match ia with
  | [] -> ()
  | (i, (u, r)) :: rest -> if u = t then () else find_tok_memb t rest x

let rec find_tok_none (t:Incremental.token) (ia:list ielem) (x:ielem)
  : Lemma (requires find_tok t ia == None /\ memb x ia) (ensures tok (snd x) <> t) =
  match ia with
  | [] -> ()
  | (i, (u, r)) :: rest -> if u = t then () else if (i, (u, r)) = x then () else find_tok_none t rest x

(* Where the token has one position, the find is that element. *)
let rec find_tok_unique (t:Incremental.token) (ia:list ielem) (x:ielem)
  : Lemma (requires memb x ia /\ tok (snd x) == t
                    /\ (forall (y:ielem). memb y ia /\ tok (snd y) == t ==> y == x))
          (ensures find_tok t ia == Some x) =
  match ia with
  | [] -> ()
  | (i, (u, r)) :: rest -> if u = t then (assert (memb (i, (u, r)) ia); assert ((i, (u, r)) == x)) else find_tok_unique t rest x

let rec lookup_memb (ts:list Incremental.token) (ia:list ielem) (x:ielem)
  : Lemma (requires memb x (lookup_order ts ia)) (ensures memb x ia /\ memb (tok (snd x)) ts) =
  match ts with
  | [] -> ()
  | t :: rest ->
    (match find_tok t ia with
     | Some y -> if y = x then find_tok_memb t ia x else lookup_memb rest ia x
     | None -> lookup_memb rest ia x)

let rec lookup_finds (ts:list Incremental.token) (ia:list ielem) (t:Incremental.token) (x:ielem)
  : Lemma (requires memb t ts /\ find_tok t ia == Some x) (ensures memb x (lookup_order ts ia)) =
  match ts with
  | [] -> ()
  | u :: rest -> if u = t then () else lookup_finds rest ia t x

let rec unstable_memb (reuse:Incremental.reuse_fn) (ia:list ielem) (x:ielem)
  : Lemma (ensures memb x (unstable reuse ia) == (memb x ia && not (reuse (tok (snd x))))) [SMTPat (memb x (unstable reuse ia))] =
  match ia with
  | [] -> ()
  | _ :: rest -> unstable_memb reuse rest x

let rec unstable_nodup (reuse:Incremental.reuse_fn) (ia:list ielem)
  : Lemma (requires nodup ia) (ensures nodup (unstable reuse ia)) =
  match ia with
  | [] -> ()
  | _ :: rest -> unstable_nodup reuse rest

let rec tokens_unidx_memb (l:list ielem) (t:Incremental.token)
  : Lemma (requires memb t (tokens_of (unidx l))) (ensures exists (i:nat) (r:list cell). memb (i, (t, r)) l) =
  match l with
  | [] -> ()
  | (i, (u, r)) :: rest -> if u = t then assert (memb (i, (u, r)) l) else tokens_unidx_memb rest t

let rec memb_tokens_unidx (l:list ielem) (i:nat) (t:Incremental.token) (r:list cell)
  : Lemma (requires memb (i, (t, r)) l) (ensures memb t (tokens_of (unidx l))) =
  match l with
  | [] -> ()
  | (j, (u, s)) :: rest -> if (j, (u, s)) = (i, (t, r)) then () else memb_tokens_unidx rest i t r

(* The stable rows now, where they arrived as before: each a stable row of the prior source under
   its token, with the cells it had, and the stable tokens now distinct. *)
let stable_now_nodup (reuse:Incremental.reuse_fn) (a0 a:Incremental.alive)
  : Lemma (requires distinct a0 /\ arrived_as_before reuse (tokens_of a0) (tokens_of a))
          (ensures nodup (stable_tokens reuse (tokens_of a))) =
  distinct_nodup a0;
  nodup_filter (present reuse (tokens_of a)) (tokens_of a0)

(* The cells of a stable row now are the prior source's under its token. *)
let stable_cells (reuse:Incremental.reuse_fn) (a0 a:Incremental.alive) (i0 i:nat) (t:Incremental.token) (r0 r:list cell)
  : Lemma (requires wk reuse a a0 /\ distinct a0 /\ reuse t
                    /\ memb (i0, (t, r0)) (indexed a0) /\ memb (i, (t, r)) (indexed a))
          (ensures r == r0) =
  index_memb_tok 0 a0 i0 t r0;
  index_memb_tok 0 a i t r;
  wk_mem reuse a a0 t r;
  assoc_of_mem a0 t r0

(* The relabelling: a prior stable row ordered before another is, read now, still before it. *)
let relabel_lt (cmp:list cell -> list cell -> int) (reuse:Incremental.reuse_fn) (a0 a:Incremental.alive)
  (i0 j0 i j:nat) (t u:Incremental.token) (r0 s0 r s:list cell)
  : Lemma (requires total_preorder cmp /\ wk reuse a a0 /\ distinct a0
                    /\ arrived_as_before reuse (tokens_of a0) (tokens_of a)
                    /\ reuse t /\ reuse u /\ t <> u
                    /\ memb (i0, (t, r0)) (indexed a0) /\ memb (j0, (u, s0)) (indexed a0)
                    /\ memb (i, (t, r)) (indexed a) /\ memb (j, (u, s)) (indexed a)
                    /\ lt_of (cmp_tok cmp) (i0, (t, r0)) (j0, (u, s0)))
          (ensures lt_of (cmp_tok cmp) (i, (t, r)) (j, (u, s))) =
  stable_cells reuse a0 a i0 i t r0 r;
  stable_cells reuse a0 a j0 j u s0 s;
  if cmp r s = 0 then begin
    (* the positions: one in the prior source, one now, in the same relative order *)
    let ts0 = tokens_of a0 in
    let ts = tokens_of a in
    stable_now_nodup reuse a0 a;
    let one0 (jj:nat) (rr:list cell) : Lemma (requires memb (jj, (t, rr)) (indexed a0)) (ensures jj == i0) = distinct_pos 0 a0 i0 jj t r0 rr in
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 one0);
    pos_is_idx 0 a0 i0 t r0;
    let one0' (jj:nat) (rr:list cell) : Lemma (requires memb (jj, (u, rr)) (indexed a0)) (ensures jj == j0) = distinct_pos 0 a0 j0 jj u s0 rr in
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 one0');
    pos_is_idx 0 a0 j0 u s0;
    let one (jj:nat) (rr:list cell) : Lemma (requires memb (jj, (t, rr)) (indexed a)) (ensures jj == i) = stable_pos reuse 0 a i jj t r rr in
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 one);
    pos_is_idx 0 a i t r;
    let one' (jj:nat) (rr:list cell) : Lemma (requires memb (jj, (u, rr)) (indexed a)) (ensures jj == j) = stable_pos reuse 0 a j jj u s rr in
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 one');
    pos_is_idx 0 a j u s;
    index_memb_tok 0 a0 i0 t r0;
    index_memb_tok 0 a0 j0 u s0;
    index_memb_tok 0 a i t r;
    index_memb_tok 0 a j u s;
    filter_order (present reuse ts) ts0 t u;
    filter_order reuse ts t u
  end
  else ()

(* The cached run — the prior produced order read through the rows now — is sorted now. *)
let rec lookup_sorted (cmp:list cell -> list cell -> int) (reuse:Incremental.reuse_fn) (a0 a:Incremental.alive) (l:list ielem)
  : Lemma (requires total_preorder cmp /\ wk reuse a a0 /\ distinct a0
                    /\ arrived_as_before reuse (tokens_of a0) (tokens_of a)
                    /\ sorted (lt_of (cmp_tok cmp)) l /\ sto_on (lt_of (cmp_tok cmp)) l
                    /\ (forall (x:ielem). memb x l ==> memb x (indexed a0)))
          (ensures sorted (lt_of (cmp_tok cmp)) (lookup_order (stable_tokens reuse (tokens_of (unidx l))) (indexed a)))
          (decreases l) =
  let lt = lt_of (cmp_tok cmp) in
  match l with
  | [] -> ()
  | (i0, (t, r0)) :: rest ->
    sto_mono lt rest l;
    lookup_sorted cmp reuse a0 a rest;
    if reuse t then begin
      match find_tok t (indexed a) with
      | None -> ()
      | Some y ->
        let (i, (t', r)) = y in
        find_tok_memb t (indexed a) y;
        let m = lookup_order (stable_tokens reuse (tokens_of (unidx rest))) (indexed a) in
        let above (z:ielem) : Lemma (requires memb z m) (ensures lt y z) =
          lookup_memb (stable_tokens reuse (tokens_of (unidx rest))) (indexed a) z;
          let (j, (u, s)) = z in
          tokens_unidx_memb rest u;
          let w (j0:nat) (s0:list cell) : Lemma (requires memb (j0, (u, s0)) rest) (ensures lt y z) =
            sorted_head_lt lt (i0, (t, r0)) rest (j0, (u, s0));
            (if t = u then distinct_pos 0 a0 i0 j0 t r0 s0 else ());
            (if t = u then same_pos 0 a0 i0 (t, r0) (u, s0) else ());
            relabel_lt cmp reuse a0 a i0 j0 i j t u r0 s0 r s in
          FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 w) in
        FStar.Classical.forall_intro (FStar.Classical.move_requires above);
        sorted_cons lt y m
    end
    else ()

(* The cached run's members are exactly the stable rows now. *)
let cached_memb (reuse:Incremental.reuse_fn) (a0 a:Incremental.alive) (prev_order:list Incremental.token) (x:ielem)
  : Lemma (requires distinct a0 /\ arrived_as_before reuse (tokens_of a0) (tokens_of a)
                    /\ (forall (t:Incremental.token). memb t prev_order <==> memb t (tokens_of a0)))
          (ensures memb x (lookup_order (stable_tokens reuse prev_order) (indexed a))
                   <==> (memb x (indexed a) /\ reuse (tok (snd x)))) =
  let ts = stable_tokens reuse prev_order in
  let ia = indexed a in
  if memb x (lookup_order ts ia) then lookup_memb ts ia x
  else begin
    if memb x ia && reuse (tok (snd x)) then begin
      let (i, (t, r)) = x in
      stable_now_nodup reuse a0 a;
      index_memb_tok 0 a i t r;
      (* the token is a stable token now, so it was in the prior arrival *)
      assert (memb t (stable_tokens reuse (tokens_of a)));
      assert (memb t (still_here reuse (tokens_of a) (tokens_of a0)));
      let uniq (y:ielem) : Lemma (requires memb y ia /\ tok (snd y) == t) (ensures y == x) =
        let (j, (u, s)) = y in
        stable_pos reuse 0 a i j t r s;
        same_pos 0 a i (t, r) (u, s) in
      FStar.Classical.forall_intro (FStar.Classical.move_requires uniq);
      find_tok_unique t ia x;
      lookup_finds ts ia t x
    end
    else ()
  end

(* THE STEP THEOREM: against the record a prime left, the step answers the full stable sort. *)
let sort_step_reuse (cmp:list cell -> list cell -> int) (reuse:Incremental.reuse_fn) (a0 a:Incremental.alive)
  : Lemma (requires total_preorder cmp /\ wk reuse a a0 /\ distinct a0)
          (ensures sort_step cmp reuse (Some (sort_record a0 (sort_alive cmp a0))) a == sort_alive cmp a) =
  if arrived_as_before reuse (tokens_of a0) (tokens_of a) then begin
    let lt = lt_of (cmp_tok cmp) in
    let ia = indexed a in
    let ia0 = indexed a0 in
    let s0 = isort lt ia0 in
    let prev_order = tokens_of (unidx s0) in
    cmp_tok_preorder cmp;
    lt_of_sto_indexed (cmp_tok cmp) a;
    lt_of_sto_indexed (cmp_tok cmp) a0;
    isort_sorted lt ia0;
    sto_mono lt s0 ia0;
    (* the prior order's tokens are the prior source's *)
    let same_tokens (t:Incremental.token) : Lemma (memb t prev_order <==> memb t (tokens_of a0)) =
      if memb t prev_order then begin
        tokens_unidx_memb s0 t;
        let w (i:nat) (r:list cell) : Lemma (requires memb (i, (t, r)) s0) (ensures memb t (tokens_of a0)) = index_memb_tok 0 a0 i t r in
        FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 w)
      end
      else if memb t (tokens_of a0) then begin
        tok_index_memb 0 a0 t;
        let w (i:nat) (r:list cell) : Lemma (requires memb (i, (t, r)) ia0) (ensures memb t prev_order) = memb_tokens_unidx s0 i t r in
        FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 w)
      end
      else () in
    FStar.Classical.forall_intro same_tokens;
    let cached = lookup_order (stable_tokens reuse prev_order) ia in
    let moved = isort lt (unstable reuse ia) in
    lookup_sorted cmp reuse a0 a s0;
    FStar.Classical.forall_intro (FStar.Classical.move_requires (cached_memb reuse a0 a prev_order));
    unstable_nodup reuse ia;
    sto_mono lt (unstable reuse ia) ia;
    isort_sorted lt (unstable reuse ia);
    merge_is_isort lt cached moved ia
  end
  else ()

(* ---- what the step leaves holds the rows that arrived, whatever ran ---- *)

let rec merge_bin_sub (#b:eqtype) (lt:lt_fn b) (xs ys:list b) (z:b)
  : Lemma (requires memb z (merge_bin lt xs ys)) (ensures memb z xs \/ memb z ys) (decreases ys) =
  match ys with
  | [] -> ()
  | y :: rest ->
    let r = bisect lt xs y 0 (len xs) in
    memb_app z (take r xs) (y :: merge_bin lt (drop r xs) rest);
    if memb z (take r xs) then ()
    else if z = y then ()
    else merge_bin_sub lt (drop r xs) rest z

let sort_step_sub (cmp:list cell -> list cell -> int) (reuse:Incremental.reuse_fn) (prior:option sort_cache) (a:Incremental.alive)
  (x:Incremental.token & list cell)
  : Lemma (requires mem_a x (sort_step cmp reuse prior a)) (ensures mem_a x a) =
  match prior with
  | Some (prev_arrival, prev_order) ->
    if arrived_as_before reuse prev_arrival (tokens_of a) then begin
      let lt = lt_of (cmp_tok cmp) in
      let ia = indexed a in
      let cached = lookup_order (stable_tokens reuse prev_order) ia in
      let moved = isort lt (unstable reuse ia) in
      let m = merge_bin lt cached moved in
      unidx_memb m x;
      let w (i:nat) : Lemma (requires memb (i, x) m) (ensures mem_a x a) =
        merge_bin_sub lt cached moved (i, x);
        (if memb (i, x) cached then lookup_memb (stable_tokens reuse prev_order) ia (i, x) else ());
        index_memb_tok 0 a i (fst x) (snd x) in
      FStar.Classical.forall_intro (FStar.Classical.move_requires w)
    end
    else ()
  | None -> ()

let sort_step_width (n:nat) (cmp:list cell -> list cell -> int) (reuse:Incremental.reuse_fn) (prior:option sort_cache) (a:Incremental.alive)
  : Lemma (requires Incremental.awidth n a) (ensures Incremental.awidth n (sort_step cmp reuse prior a)) =
  FStar.Classical.forall_intro (FStar.Classical.move_requires (sort_step_sub cmp reuse prior a));
  awidth_of_members n a (sort_step cmp reuse prior a)

(* Reusing nothing, the step sorts from scratch whatever it was handed — the merge of no cached
   row with every row moved IS the sort (F#: an empty `kept`). *)
let rec unstable_no_reuse (ia:list ielem) : Lemma (ensures unstable Incremental.no_reuse ia == ia) =
  match ia with
  | [] -> ()
  | _ :: rest -> unstable_no_reuse rest

let rec merge_bin_nil (#b:Type0) (lt:lt_fn b) (ys:list b) : Lemma (ensures merge_bin lt [] ys == ys) =
  match ys with
  | [] -> ()
  | _ :: rest -> merge_bin_nil lt rest

let rec stable_tokens_no_reuse (ts:list Incremental.token) : Lemma (ensures stable_tokens Incremental.no_reuse ts == []) =
  match ts with
  | [] -> ()
  | _ :: rest -> stable_tokens_no_reuse rest

let rec still_here_no_reuse (now prev:list Incremental.token) : Lemma (ensures still_here Incremental.no_reuse now prev == []) =
  match prev with
  | [] -> ()
  | _ :: rest -> still_here_no_reuse now rest

let sort_step_no_reuse (cmp:list cell -> list cell -> int) (prior:option sort_cache) (a:Incremental.alive)
  : Lemma (ensures sort_step cmp Incremental.no_reuse prior a == sort_alive cmp a) =
  match prior with
  | Some (prev_arrival, prev_order) ->
    stable_tokens_no_reuse (tokens_of a);
    still_here_no_reuse (tokens_of a) prev_arrival;
    stable_tokens_no_reuse prev_order;
    unstable_no_reuse (indexed a);
    merge_bin_nil (lt_of (cmp_tok cmp)) (isort (lt_of (cmp_tok cmp)) (indexed a))
  | None -> ()

(* ======================================================================================
   8. The reference's rows — F#: `evalSort` over the frame's rows, and the walk's sorted rows ARE
      them (the tokens ride along).
   ====================================================================================== *)

(* F#: `evalSort`: `List.sortWith (compareResolved keys)`, stable over the frame order. *)
let sort_rows (cmp:list cell -> list cell -> int) (rows:list (list cell)) : Tot (list (list cell)) = sort_by cmp rows

let drop_tok (p:nat & (Incremental.token & list cell)) : Tot (nat & list cell) = (fst p, snd (snd p))

let rec index_from_rows (k:nat) (a:Incremental.alive)
  : Lemma (ensures lmap drop_tok (index_from k a) == index_from k (Incremental.rows_of a)) (decreases a) =
  match a with
  | [] -> ()
  | _ :: rest -> index_from_rows (k + 1) rest

let rec rows_of_unidx (l:list ielem)
  : Lemma (ensures Incremental.rows_of (unidx l) == unidx (lmap drop_tok l)) =
  match l with
  | [] -> ()
  | _ :: rest -> rows_of_unidx rest

let rows_of_sort_alive (cmp:list cell -> list cell -> int) (a:Incremental.alive)
  : Lemma (ensures Incremental.rows_of (sort_alive cmp a) == sort_rows cmp (Incremental.rows_of a)) =
  let lt1 = lt_of (cmp_tok cmp) in
  let lt2 = lt_of cmp in
  isort_map drop_tok lt1 lt2 (indexed a);
  index_from_rows 0 a;
  rows_of_unidx (isort lt1 (indexed a))

let rec all_width_of_members (n:nat) (rows rs:list (list cell))
  : Lemma (requires all_width n rows /\ (forall (r:list cell). memb r rs ==> memb r rows)) (ensures all_width n rs) =
  match rs with
  | [] -> ()
  | r :: rest ->
    let rec w (l:list (list cell)) : Lemma (requires all_width n l /\ memb r l) (ensures len r = n) =
      match l with
      | h :: t -> if h = r then () else w t in
    w rows;
    all_width_of_members n rows rest

let sort_rows_width (n:nat) (cmp:list cell -> list cell -> int) (rows:list (list cell))
  : Lemma (requires all_width n rows) (ensures all_width n (sort_rows cmp rows)) =
  let lt = lt_of cmp in
  let s = isort lt (indexed rows) in
  let mem (r:list cell) : Lemma (requires memb r (sort_rows cmp rows)) (ensures memb r rows) =
    unidx_memb s r;
    let w (i:nat) : Lemma (requires memb (i, r) s) (ensures memb r rows) = memb_index_from 0 rows i r; nth_memb rows (i - 0) in
    FStar.Classical.forall_intro (FStar.Classical.move_requires w) in
  FStar.Classical.forall_intro (FStar.Classical.move_requires mem);
  all_width_of_members n rows (sort_rows cmp rows)

(* ======================================================================================
   9. The comparator, concretely — F#: `DataFrame.compareResolved` over `compareCells`.
   ====================================================================================== *)

noeq type sprims = {
  (* F#: `compareResolved (resolveSortKeys cols by)` — THE row comparator the walk and the reference
     call, which `compare_resolved` below restates concretely over the three primitives. *)
  row_cmp   : schema -> list (string & sort_dir) -> list cell -> list cell -> int;
  (* F#: `Kernels.compareFloat`, over the float carrier (`NaN` one value above every other, `-0 = 0`). *)
  cmp_float : string -> string -> int;
  (* F#: `DecimalText.compare` — `None` where either text is not decimal text. *)
  cmp_dec   : string -> string -> option int;
  (* F#: `DecimalText.tryCanonical`. *)
  canon_dec : string -> option string;
  (* F#: `System.String.CompareOrdinal`. *)
  cmp_ord   : string -> string -> int;
}

(* F#: `numOf` — a numeric cell in the float carrier (`float i` is `pr.float_of_int`, Phase 470). *)
let num_of (pr:prims) (c:cell) : Tot string =
  match c with
  | Int i -> pr.float_of_int i
  | Float f -> f
  | _ -> ""

(* F#: Core's `asDecimal` — an int is exactly a decimal (`string i`), a decimal its canonical text. *)
let dec_of (sp:sprims) (pr:prims) (c:cell) : Tot (option string) =
  match c with
  | Int i -> Some (pr.int_text i)
  | Decimal s -> sp.canon_dec s
  | _ -> None

(* F#: `compareCells`, arm for arm. *)
let compare_cells (sp:sprims) (pr:prims) (a b:cell) : Tot (option int) =
  match a, b with
  | Int _, Int _ | Int _, Float _ | Float _, Int _ | Float _, Float _ -> Some (sp.cmp_float (num_of pr a) (num_of pr b))
  | Decimal _, Decimal _ | Decimal _, Int _ | Int _, Decimal _ ->
    (match dec_of sp pr a, dec_of sp pr b with
     | Some x, Some y -> sp.cmp_dec x y
     | _ -> None)
  | Bool x, Bool y -> Some (if x = y then 0 else if x then 1 else -1)
  | Str x, Str y -> Some (sp.cmp_ord x y)
  | Date x, Date y -> Some (sp.cmp_ord x y)
  | Timestamp x, Timestamp y -> Some (sp.cmp_ord x y)
  | _ -> None

(* F#: `resolveSortKeys` — each key's column index, in key order (an unknown key was refused). *)
let rec resolve_keys (cols:schema) (ks:list (string & sort_dir)) : Tot (list (nat & sort_dir)) =
  match ks with
  | [] -> []
  | (c, d) :: rest ->
    (match index_of c cols with
     | Some i -> (i, d) :: resolve_keys cols rest
     | None -> resolve_keys cols rest)

let cell_at (j:nat) (r:list cell) : Tot cell = if j < len r then nth r j else Null

(* F#: `compareResolved` — nulls last whichever the direction, an incomparable pair a tie. *)
let rec compare_resolved (sp:sprims) (pr:prims) (keys:list (nat & sort_dir)) (r1 r2:list cell) : Tot int =
  match keys with
  | [] -> 0
  | (i, dir) :: rest ->
    let a = cell_at i r1 in
    let b = cell_at i r2 in
    let c =
      (match a = Null, b = Null with
       | true, true -> 0
       | true, false -> 1
       | false, true -> -1
       | false, false ->
         (match compare_cells sp pr a b with
          | Some c -> if dir = Asc then c else 0 - c
          | None -> 0)) in
    if c <> 0 then c else compare_resolved sp pr rest r1 r2

(* The pairs the comparator reads as a tie, named: two families, a float beside a decimal, or a
   decimal whose text is not decimal text. *)
let family (c:cell) : Tot nat =
  match c with
  | Int _ -> 0 | Float _ -> 1 | Decimal _ -> 2 | Bool _ -> 3 | Str _ -> 4 | Date _ -> 5 | Timestamp _ -> 6 | Null -> 7

(* One family: ints with floats, ints with decimals, every other type with itself. *)
let one_family (a b:cell) : Tot bool =
  let fa = family a in
  let fb = family b in
  fa = fb || (fa = 0 && (fb = 1 || fb = 2)) || (fb = 0 && (fa = 1 || fa = 2))

let compare_cells_none (sp:sprims) (pr:prims) (a b:cell)
  : Lemma (requires a <> Null /\ b <> Null /\ None? (compare_cells sp pr a b))
          (ensures not (one_family a b)
                   \/ (Decimal? a /\ None? (sp.canon_dec (Decimal?._0 a)))
                   \/ (Decimal? b /\ None? (sp.canon_dec (Decimal?._0 b)))
                   \/ (Decimal? a /\ Decimal? b /\ None? (sp.cmp_dec (Some?.v (sp.canon_dec (Decimal?._0 a))) (Some?.v (sp.canon_dec (Decimal?._0 b)))))
                   \/ (Decimal? a /\ Int? b /\ None? (sp.cmp_dec (Some?.v (sp.canon_dec (Decimal?._0 a))) (pr.int_text (Int?._0 b))))
                   \/ (Int? a /\ Decimal? b /\ None? (sp.cmp_dec (pr.int_text (Int?._0 a)) (Some?.v (sp.canon_dec (Decimal?._0 b)))))) = ()

(* The laws, relative to a domain — the preorder over the cells a typed column holds, and over
   the rows whose key cells it holds. *)
let total_preorder_on (#e:Type0) (cmp:e -> e -> int) (dom:e -> bool) : prop =
  (forall (x:e). dom x ==> cmp x x == 0) /\
  (forall (x y:e). dom x /\ dom y ==> ((cmp x y < 0 <==> cmp y x > 0) /\ (cmp x y == 0 <==> cmp y x == 0))) /\
  (forall (x y z:e). dom x /\ dom y /\ dom z /\ cmp x y <= 0 /\ cmp y z <= 0 ==> cmp x z <= 0)

(* F#: `DecimalText.compare` read as an int over decimal texts — `None` never arises on them. *)
let dec_cmp (sp:sprims) (x y:string) : Tot int =
  match sp.cmp_dec x y with
  | Some c -> c
  | None -> 0

let canonical (sp:sprims) (x:string) : Tot bool = Some? (sp.canon_dec x) && Some?.v (sp.canon_dec x) = x

(* THE PRIMITIVES' LAWS — each an assumption the differential checks on production's own: the float
   carrier's order (`Kernels.compareFloat`) and the ordinal order are total preorders; the decimal
   order is one over canonical decimal texts, where it always answers; an int's text is canonical
   decimal text, and the canonical form of a canonical text is itself; and an int reads the same way
   in both carriers — `compareFloat (float i) (float j)` IS `DecimalText.compare (string i) (string j)`,
   the typing fact that a 32-bit int is exact in a float and in a decimal alike. *)
let sprims_laws (sp:sprims) (pr:prims) : prop =
  total_preorder sp.cmp_float /\
  total_preorder sp.cmp_ord /\
  total_preorder_on (dec_cmp sp) (canonical sp) /\
  (forall (x y:string). canonical sp x /\ canonical sp y ==> Some? (sp.cmp_dec x y)) /\
  (forall (i:int). canonical sp (pr.int_text i)) /\
  (forall (x:string). Some? (sp.canon_dec x) ==> canonical sp (Some?.v (sp.canon_dec x))) /\
  (forall (i j:int). sp.cmp_float (pr.float_of_int i) (pr.float_of_int j) == dec_cmp sp (pr.int_text i) (pr.int_text j))

(* F#: `Vec.packAt`'s reading of what a typed column holds (D38): its own type, an int under a float
   or a decimal column (packed as that type on the typed pass, raw on the boxed one — one family
   either way), a decimal whose text is decimal text, and `Null` anywhere. *)
let fits (sp:sprims) (ty:column_type) (c:cell) : Tot bool =
  match c with
  | Null -> true
  | Int _ -> ty = IntType || ty = FloatType || ty = DecimalType
  | Float _ -> ty = FloatType
  | Decimal s -> ty = DecimalType && Some? (sp.canon_dec s)
  | Bool _ -> ty = BoolType
  | Str _ -> ty = StringType
  | Date _ -> ty = DateType
  | Timestamp _ -> ty = TimestampType

let present_cell (sp:sprims) (ty:column_type) (c:cell) : Tot bool = fits sp ty c && c <> Null

(* The cell comparator read as an int — a tie where it does not answer, as `compareResolved` reads it. *)
let cc (sp:sprims) (pr:prims) (a b:cell) : Tot int =
  match compare_cells sp pr a b with
  | Some c -> c
  | None -> 0

(* The decimal family's key: every cell of a decimal column is a canonical decimal text. *)
let dkey (sp:sprims) (pr:prims) (c:cell) : Tot string =
  match c with
  | Int i -> pr.int_text i
  | Decimal s -> (match sp.canon_dec s with Some x -> x | None -> "")
  | _ -> ""

let cc_decimal (sp:sprims) (pr:prims) (a b:cell)
  : Lemma (requires sprims_laws sp pr /\ present_cell sp DecimalType a /\ present_cell sp DecimalType b)
          (ensures cc sp pr a b == dec_cmp sp (dkey sp pr a) (dkey sp pr b) /\ canonical sp (dkey sp pr a)) = ()

let cc_float (sp:sprims) (pr:prims) (a b:cell)
  : Lemma (requires sprims_laws sp pr /\ present_cell sp FloatType a /\ present_cell sp FloatType b)
          (ensures cc sp pr a b == sp.cmp_float (num_of pr a) (num_of pr b)) = ()

(* Over the present cells a typed column holds, the cell comparator answers and is a total preorder. *)
let cells_some (sp:sprims) (pr:prims) (ty:column_type) (a b:cell)
  : Lemma (requires sprims_laws sp pr /\ present_cell sp ty a /\ present_cell sp ty b)
          (ensures Some? (compare_cells sp pr a b)) = ()

let cells_preorder (sp:sprims) (pr:prims) (ty:column_type)
  : Lemma (requires sprims_laws sp pr) (ensures total_preorder_on (cc sp pr) (present_cell sp ty)) =
  match ty with
  | DecimalType ->
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 (cc_decimal sp pr))
  | FloatType ->
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 (cc_float sp pr))
  | _ -> ()

(* F#: one key's comparison — nulls last whichever the direction, the direction flipping the sign. *)
let key_cc (sp:sprims) (pr:prims) (dir:sort_dir) (a b:cell) : Tot int =
  match a = Null, b = Null with
  | true, true -> 0
  | true, false -> 1
  | false, true -> -1
  | false, false ->
    (match compare_cells sp pr a b with
     | Some c -> if dir = Asc then c else 0 - c
     | None -> 0)

let key_cc_refl (sp:sprims) (pr:prims) (ty:column_type) (dir:sort_dir) (a:cell)
  : Lemma (requires sprims_laws sp pr /\ fits sp ty a) (ensures key_cc sp pr dir a a == 0) =
  if a = Null then ()
  else begin
    cells_preorder sp pr ty;
    cells_some sp pr ty a a;
    assert (present_cell sp ty a)
  end

let key_cc_anti (sp:sprims) (pr:prims) (ty:column_type) (dir:sort_dir) (a b:cell)
  : Lemma (requires sprims_laws sp pr /\ fits sp ty a /\ fits sp ty b)
          (ensures (key_cc sp pr dir a b < 0 <==> key_cc sp pr dir b a > 0)
                   /\ (key_cc sp pr dir a b == 0 <==> key_cc sp pr dir b a == 0)) =
  if a = Null || b = Null then ()
  else begin
    cells_preorder sp pr ty;
    cells_some sp pr ty a b;
    cells_some sp pr ty b a;
    assert (present_cell sp ty a /\ present_cell sp ty b);
    assert ((cc sp pr a b < 0 <==> cc sp pr b a > 0) /\ (cc sp pr a b == 0 <==> cc sp pr b a == 0));
    assert (key_cc sp pr dir a b == (if dir = Asc then cc sp pr a b else 0 - cc sp pr a b));
    assert (key_cc sp pr dir b a == (if dir = Asc then cc sp pr b a else 0 - cc sp pr b a))
  end

let key_cc_trans (sp:sprims) (pr:prims) (ty:column_type) (dir:sort_dir) (a b c:cell)
  : Lemma (requires sprims_laws sp pr /\ fits sp ty a /\ fits sp ty b /\ fits sp ty c
                    /\ key_cc sp pr dir a b <= 0 /\ key_cc sp pr dir b c <= 0)
          (ensures key_cc sp pr dir a c <= 0) =
  if a = Null || b = Null || c = Null then ()
  else begin
    cells_preorder sp pr ty;
    cells_some sp pr ty a b;
    cells_some sp pr ty b c;
    cells_some sp pr ty a c;
    cells_some sp pr ty b a;
    cells_some sp pr ty c b;
    cells_some sp pr ty c a;
    assert (present_cell sp ty a /\ present_cell sp ty b /\ present_cell sp ty c);
    assert (key_cc sp pr dir a b == (if dir = Asc then cc sp pr a b else 0 - cc sp pr a b));
    assert (key_cc sp pr dir b c == (if dir = Asc then cc sp pr b c else 0 - cc sp pr b c));
    assert (key_cc sp pr dir a c == (if dir = Asc then cc sp pr a c else 0 - cc sp pr a c));
    assert ((cc sp pr a b < 0 <==> cc sp pr b a > 0) /\ (cc sp pr a b == 0 <==> cc sp pr b a == 0));
    assert ((cc sp pr b c < 0 <==> cc sp pr c b > 0) /\ (cc sp pr b c == 0 <==> cc sp pr c b == 0));
    assert ((cc sp pr a c < 0 <==> cc sp pr c a > 0) /\ (cc sp pr a c == 0 <==> cc sp pr c a == 0));
    assert (cc sp pr a b <= 0 /\ cc sp pr b c <= 0 ==> cc sp pr a c <= 0);
    assert (cc sp pr c b <= 0 /\ cc sp pr b a <= 0 ==> cc sp pr c a <= 0)
  end

let key_cc_preorder (sp:sprims) (pr:prims) (ty:column_type) (dir:sort_dir)
  : Lemma (requires sprims_laws sp pr) (ensures total_preorder_on (key_cc sp pr dir) (fits sp ty)) =
  FStar.Classical.forall_intro (FStar.Classical.move_requires (key_cc_refl sp pr ty dir));
  FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 (key_cc_anti sp pr ty dir));
  FStar.Classical.forall_intro_3 (FStar.Classical.move_requires_3 (key_cc_trans sp pr ty dir))

(* The row comparator, key by key, as the per-key comparison over the cell at the key's column. *)
let key_cmp (sp:sprims) (pr:prims) (k:nat & sort_dir) (r1 r2:list cell) : Tot int =
  key_cc sp pr (snd k) (cell_at (fst k) r1) (cell_at (fst k) r2)

let compare_resolved_is_lex (sp:sprims) (pr:prims) (keys:list (nat & sort_dir)) (r1 r2:list cell)
  : Lemma (ensures compare_resolved sp pr keys r1 r2 ==
                   (match keys with
                    | [] -> 0
                    | k :: rest -> (let c = key_cmp sp pr k r1 r2 in if c <> 0 then c else compare_resolved sp pr rest r1 r2))) = ()

(* A row whose cell at each key fits that key's column. *)
let rec typed_row (sp:sprims) (cols:schema) (keys:list (nat & sort_dir)) (r:list cell) : Tot bool =
  match keys with
  | [] -> true
  | (i, _) :: rest -> i < len cols && fits sp (snd (nth cols i)) (cell_at i r) && typed_row sp cols rest r

(* The lexicographic composition: a preorder on the first key, then one on the rest. *)
let rec lex_preorder (sp:sprims) (pr:prims) (cols:schema) (keys:list (nat & sort_dir))
  : Lemma (requires sprims_laws sp pr)
          (ensures total_preorder_on (compare_resolved sp pr keys) (typed_row sp cols keys)) =
  match keys with
  | [] -> ()
  | (i, dir) :: rest ->
    lex_preorder sp pr cols rest;
    let dom = typed_row sp cols keys in
    let cmp = compare_resolved sp pr keys in
    let refl (r:list cell) : Lemma (requires dom r) (ensures cmp r r == 0) =
      let ty = snd (nth cols i) in
      key_cc_refl sp pr ty dir (cell_at i r);
      assert (typed_row sp cols rest r);
      compare_resolved_is_lex sp pr keys r r in
    FStar.Classical.forall_intro (FStar.Classical.move_requires refl);
    let anti (r1 r2:list cell) : Lemma (requires dom r1 /\ dom r2)
      (ensures (cmp r1 r2 < 0 <==> cmp r2 r1 > 0) /\ (cmp r1 r2 == 0 <==> cmp r2 r1 == 0)) =
      let ty = snd (nth cols i) in
      key_cc_anti sp pr ty dir (cell_at i r1) (cell_at i r2);
      assert (typed_row sp cols rest r1 /\ typed_row sp cols rest r2);
      compare_resolved_is_lex sp pr keys r1 r2;
      compare_resolved_is_lex sp pr keys r2 r1 in
    FStar.Classical.forall_intro_2 (FStar.Classical.move_requires_2 anti);
    let trans (r1 r2 r3:list cell) : Lemma (requires dom r1 /\ dom r2 /\ dom r3 /\ cmp r1 r2 <= 0 /\ cmp r2 r3 <= 0)
      (ensures cmp r1 r3 <= 0) =
      let ty = snd (nth cols i) in
      let a = cell_at i r1 in
      let b = cell_at i r2 in
      let c = cell_at i r3 in
      key_cc_anti sp pr ty dir a b;
      key_cc_anti sp pr ty dir b c;
      key_cc_anti sp pr ty dir a c;
      (if key_cc sp pr dir a b <= 0 && key_cc sp pr dir b c <= 0 then key_cc_trans sp pr ty dir a b c else ());
      (if key_cc sp pr dir b c <= 0 && key_cc sp pr dir c a <= 0 then key_cc_trans sp pr ty dir b c a else ());
      (if key_cc sp pr dir c a <= 0 && key_cc sp pr dir a b <= 0 then key_cc_trans sp pr ty dir c a b else ());
      assert (typed_row sp cols rest r1 /\ typed_row sp cols rest r2 /\ typed_row sp cols rest r3);
      compare_resolved_is_lex sp pr keys r1 r2;
      compare_resolved_is_lex sp pr keys r2 r3;
      compare_resolved_is_lex sp pr keys r1 r3 in
    FStar.Classical.forall_intro_3 (FStar.Classical.move_requires_3 trans)

(* THE COMPARATOR THEOREM: over rows whose key cells are the cells a typed column holds,
   `compareResolved` is a total preorder, from the primitives' laws alone. *)
let compare_resolved_preorder (sp:sprims) (pr:prims) (cols:schema) (keys:list (nat & sort_dir))
  : Lemma (requires sprims_laws sp pr)
          (ensures total_preorder_on (compare_resolved sp pr keys) (typed_row sp cols keys)) =
  lex_preorder sp pr cols keys

(* What the walk's theorems assume of the row comparator: a total preorder, for every schema and
   every key list — true of production's `compareResolved` over the rows a typed frame holds
   (`compare_resolved_preorder`), which is every frame a `Sort` reaches (D18, D38 item 3). *)
let row_cmp_preorder (sp:sprims) : prop =
  forall (cols:schema) (ks:list (string & sort_dir)). total_preorder (sp.row_cmp cols ks)
