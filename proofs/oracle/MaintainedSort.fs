module MaintainedSort

let rec memb = (fun ( x  :  'b ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     false
     end
| (h)::t -> begin
     ((Prims.op_Equals h x) || (memb x t))
     end))


let rec nodup = (fun ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((not ((memb h t))) && (nodup t))
     end))


let rec filter = (fun ( p  :  'b  ->  Prims.bool ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (p h) then begin
     (h)::(filter p t)
     end else begin
     (filter p t)
     end
     end))


let rec take = (fun ( n  :  Prims.nat ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     []
     end else begin
     (h)::(take (n - (Prims.parse_int "1")) t)
     end
     end))


let rec drop = (fun ( n  :  Prims.nat ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     []
     end
| (uu___)::t -> begin
      
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     l
     end else begin
     (drop (n - (Prims.parse_int "1")) t)
     end
     end))


type lt_fn<'b> = 'b  ->  'b  ->  Prims.bool


let rec sorted = (fun ( lt  :  lt_fn<'b> ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     (match (t) with
| [] -> begin
     true
     end
| (y)::uu___ -> begin
     ((lt x y) && (sorted lt t))
     end)
     end))


let rec insert = (fun ( lt  :  lt_fn<'b> ) ( x  :  'b ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     (x)::[]
     end
| (h)::t -> begin
      
if (lt h x) then begin
     (h)::(insert lt x t)
     end else begin
     (x)::l
     end
     end))


let rec isort = (fun ( lt  :  lt_fn<'b> ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
     (insert lt h (isort lt t))
     end))


let rec bisect = (fun ( lt  :  lt_fn<'b> ) ( xs  :  Prims.list<'b> ) ( y  :  'b ) ( lo  :  Prims.nat ) ( hi  :  Prims.nat ) ->  
if (lo < hi) then begin
     (

let m = ((lo + hi) / (Prims.parse_int "2"))
in  
if (lt (Pipeline.nth xs m) y) then begin
     (bisect lt xs y (m + (Prims.parse_int "1")) hi)
     end else begin
     (bisect lt xs y lo m)
     end)
     end else begin
     lo
     end)


let not_lt = (fun ( lt  :  lt_fn<'b> ) ( y  :  'b ) ( x  :  'b ) -> (not ((lt x y))))


let rec merge_bin = (fun ( lt  :  lt_fn<'b> ) ( xs  :  Prims.list<'b> ) ( ys  :  Prims.list<'b> ) -> (match (ys) with
| [] -> begin
     xs
     end
| (y)::rest -> begin
     (

let r = (bisect lt xs y (Prims.parse_int "0") (Pipeline.len xs))
in (Pipeline.app (take r xs) ((y)::(merge_bin lt (drop r xs) rest))))
     end))


let rec index_from = (fun ( k  :  Prims.nat ) ( l  :  Prims.list<'e> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
     (((k), (h)))::(index_from (k + (Prims.parse_int "1")) t)
     end))


let indexed = (fun ( l  :  Prims.list<'e> ) -> (index_from (Prims.parse_int "0") l))


let rec unidx = (fun ( l  :  Prims.list<(Prims.nat * 'e)> ) -> (match (l) with
| [] -> begin
     []
     end
| ((uu___, x))::t -> begin
     (x)::(unidx t)
     end))


let lt_of = (fun ( cmp  :  'e  ->  'e  ->  Prims.int ) ( p  :  (Prims.nat * 'e) ) ( q  :  (Prims.nat * 'e) ) -> (

let uu___ = p
in (match (uu___) with
| (i, x) -> begin
     (

let uu___1 = q
in (match (uu___1) with
| (j, y) -> begin
     (((cmp x y) < (Prims.parse_int "0")) || ((Prims.op_Equals (cmp x y) (Prims.parse_int "0")) && (i < j)))
     end))
     end)))


let sort_by = (fun ( cmp  :  'e  ->  'e  ->  Prims.int ) ( l  :  Prims.list<'e> ) -> (unidx (isort (lt_of cmp) (indexed l))))


let rec lmap = (fun ( f  :  'b  ->  'c ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
     ((f h))::(lmap f t)
     end))


let rec tokens_of : Incremental.alive  ->  Prims.list<Incremental.token> = (fun ( a  :  Incremental.alive ) -> (match (a) with
| [] -> begin
     []
     end
| ((t, uu___))::rest -> begin
     (t)::(tokens_of rest)
     end))


let rec distinct : Incremental.alive  ->  Prims.bool = (fun ( a  :  Incremental.alive ) -> (match (a) with
| [] -> begin
     true
     end
| ((t, uu___))::rest -> begin
     ((not ((Incremental.mem_token t (tokens_of rest)))) && (distinct rest))
     end))


let rec mem_a : (Incremental.token * Prims.list<Pipeline.cell>)  ->  Incremental.alive  ->  Prims.bool = (fun ( x  :  (Incremental.token * Prims.list<Pipeline.cell>) ) ( a  :  Incremental.alive ) -> (match (a) with
| [] -> begin
     false
     end
| (y)::rest -> begin
     ((Prims.op_Equals y x) || (mem_a x rest))
     end))


let rec wk : Incremental.reuse_fn  ->  Incremental.alive  ->  Incremental.alive  ->  Prims.bool = (fun ( reuse  :  Incremental.reuse_fn ) ( a  :  Incremental.alive ) ( a0  :  Incremental.alive ) -> (match (a) with
| [] -> begin
     true
     end
| ((t, r))::rest -> begin
     (((not ((reuse t))) || (match ((Pipeline.assoc t a0)) with
| FStar_Pervasives_Native.Some (r0) -> begin
     (Prims.op_Equals r0 r)
     end
| FStar_Pervasives_Native.None -> begin
     true
     end)) && (wk reuse rest a0))
     end))


let ok_row : Incremental.reuse_fn  ->  Incremental.alive  ->  Incremental.token  ->  Prims.list<Pipeline.cell>  ->  Prims.bool = (fun ( reuse  :  Incremental.reuse_fn ) ( a0  :  Incremental.alive ) ( t  :  Incremental.token ) ( r  :  Prims.list<Pipeline.cell> ) -> ((not ((reuse t))) || (match ((Pipeline.assoc t a0)) with
| FStar_Pervasives_Native.Some (r0) -> begin
     (Prims.op_Equals r0 r)
     end
| FStar_Pervasives_Native.None -> begin
     true
     end)))


let rec idx = (fun ( t  :  'b ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (h)::rest -> begin
      
if (Prims.op_Equals h t) then begin
     (Prims.parse_int "0")
     end else begin
     ((Prims.parse_int "1") + (idx t rest))
     end
     end))


let rec stable_tokens : Incremental.reuse_fn  ->  Prims.list<Incremental.token>  ->  Prims.list<Incremental.token> = (fun ( reuse  :  Incremental.reuse_fn ) ( ts  :  Prims.list<Incremental.token> ) -> (match (ts) with
| [] -> begin
     []
     end
| (t)::rest -> begin
      
if (reuse t) then begin
     (t)::(stable_tokens reuse rest)
     end else begin
     (stable_tokens reuse rest)
     end
     end))


let present : Incremental.reuse_fn  ->  Prims.list<Incremental.token>  ->  Incremental.token  ->  Prims.bool = (fun ( reuse  :  Incremental.reuse_fn ) ( now  :  Prims.list<Incremental.token> ) ( t  :  Incremental.token ) -> ((reuse t) && (memb t now)))


let rec still_here : Incremental.reuse_fn  ->  Prims.list<Incremental.token>  ->  Prims.list<Incremental.token>  ->  Prims.list<Incremental.token> = (fun ( reuse  :  Incremental.reuse_fn ) ( now  :  Prims.list<Incremental.token> ) ( prev  :  Prims.list<Incremental.token> ) -> (match (prev) with
| [] -> begin
     []
     end
| (t)::rest -> begin
      
if ((reuse t) && (memb t now)) then begin
     (t)::(still_here reuse now rest)
     end else begin
     (still_here reuse now rest)
     end
     end))


let arrived_as_before : Incremental.reuse_fn  ->  Prims.list<Incremental.token>  ->  Prims.list<Incremental.token>  ->  Prims.bool = (fun ( reuse  :  Incremental.reuse_fn ) ( prev  :  Prims.list<Incremental.token> ) ( now  :  Prims.list<Incremental.token> ) -> (Prims.op_Equals (still_here reuse now prev) (stable_tokens reuse now)))


type ielem = (Prims.nat * (Incremental.token * Prims.list<Pipeline.cell>))


let tok : (Incremental.token * Prims.list<Pipeline.cell>)  ->  Incremental.token = (fun ( x  :  (Incremental.token * Prims.list<Pipeline.cell>) ) -> (FStar_Pervasives_Native.fst x))


let cmp_tok : (Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int)  ->  (Incremental.token * Prims.list<Pipeline.cell>)  ->  (Incremental.token * Prims.list<Pipeline.cell>)  ->  Prims.int = (fun ( cmp  :  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int ) ( x  :  (Incremental.token * Prims.list<Pipeline.cell>) ) ( y  :  (Incremental.token * Prims.list<Pipeline.cell>) ) -> (cmp (FStar_Pervasives_Native.snd x) (FStar_Pervasives_Native.snd y)))


let sort_alive : (Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int)  ->  Incremental.alive  ->  Incremental.alive = (fun ( cmp  :  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int ) ( a  :  Incremental.alive ) -> (sort_by (cmp_tok cmp) a))


let rec find_tok : Incremental.token  ->  Prims.list<ielem>  ->  FStar_Pervasives_Native.option<ielem> = (fun ( t  :  Incremental.token ) ( l  :  Prims.list<ielem> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((i, (u, r)))::rest -> begin
      
if (Prims.op_Equals u t) then begin
     FStar_Pervasives_Native.Some (((i), (((u), (r)))))
     end else begin
     (find_tok t rest)
     end
     end))


let rec lookup_order : Prims.list<Incremental.token>  ->  Prims.list<ielem>  ->  Prims.list<ielem> = (fun ( ts  :  Prims.list<Incremental.token> ) ( ia  :  Prims.list<ielem> ) -> (match (ts) with
| [] -> begin
     []
     end
| (t)::rest -> begin
     (match ((find_tok t ia)) with
| FStar_Pervasives_Native.Some (x) -> begin
     (x)::(lookup_order rest ia)
     end
| FStar_Pervasives_Native.None -> begin
     (lookup_order rest ia)
     end)
     end))


let rec unstable : Incremental.reuse_fn  ->  Prims.list<ielem>  ->  Prims.list<ielem> = (fun ( reuse  :  Incremental.reuse_fn ) ( ia  :  Prims.list<ielem> ) -> (match (ia) with
| [] -> begin
     []
     end
| ((i, (t, r)))::rest -> begin
      
if (reuse t) then begin
     (unstable reuse rest)
     end else begin
     (((i), (((t), (r)))))::(unstable reuse rest)
     end
     end))


let merge_sort : (Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int)  ->  Incremental.reuse_fn  ->  Prims.list<Incremental.token>  ->  Incremental.alive  ->  Incremental.alive = (fun ( cmp  :  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int ) ( reuse  :  Incremental.reuse_fn ) ( prev_order  :  Prims.list<Incremental.token> ) ( a  :  Incremental.alive ) -> (

let ia = (indexed a)
in (

let cached = (lookup_order (stable_tokens reuse prev_order) ia)
in (

let moved = (isort (lt_of (cmp_tok cmp)) (unstable reuse ia))
in (unidx (merge_bin (lt_of (cmp_tok cmp)) cached moved))))))


type sort_cache = (Prims.list<Incremental.token> * Prims.list<Incremental.token>)


let sort_step : (Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int)  ->  Incremental.reuse_fn  ->  FStar_Pervasives_Native.option<sort_cache>  ->  Incremental.alive  ->  Incremental.alive = (fun ( cmp  :  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int ) ( reuse  :  Incremental.reuse_fn ) ( prior  :  FStar_Pervasives_Native.option<sort_cache> ) ( a  :  Incremental.alive ) -> (match (prior) with
| FStar_Pervasives_Native.Some (prev_arrival, prev_order) -> begin
      
if (arrived_as_before reuse prev_arrival (tokens_of a)) then begin
     (merge_sort cmp reuse prev_order a)
     end else begin
     (sort_alive cmp a)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (sort_alive cmp a)
     end))


let sort_record : Incremental.alive  ->  Incremental.alive  ->  sort_cache = (fun ( a  :  Incremental.alive ) ( ordered  :  Incremental.alive ) -> (((tokens_of a)), ((tokens_of ordered))))


let sort_rows : (Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int)  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.list<Prims.list<Pipeline.cell>> = (fun ( cmp  :  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int ) ( rows  :  Prims.list<Prims.list<Pipeline.cell>> ) -> (sort_by cmp rows))


let drop_tok : (Prims.nat * (Incremental.token * Prims.list<Pipeline.cell>))  ->  (Prims.nat * Prims.list<Pipeline.cell>) = (fun ( p  :  (Prims.nat * (Incremental.token * Prims.list<Pipeline.cell>)) ) -> (((FStar_Pervasives_Native.fst p)), ((FStar_Pervasives_Native.snd (FStar_Pervasives_Native.snd p)))))

type sprims = {row_cmp : Pipeline.schema  ->  Prims.list<(Prims.string * Pipeline.sort_dir)>  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int; cmp_float : Prims.string  ->  Prims.string  ->  Prims.int; cmp_dec : Prims.string  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.int>; canon_dec : Prims.string  ->  FStar_Pervasives_Native.option<Prims.string>; cmp_ord : Prims.string  ->  Prims.string  ->  Prims.int}


let __proj__Mksprims__item__row_cmp : sprims  ->  Pipeline.schema  ->  Prims.list<(Prims.string * Pipeline.sort_dir)>  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int = (fun ( projectee  :  sprims ) -> (match (projectee) with
| {row_cmp = row_cmp; cmp_float = cmp_float; cmp_dec = cmp_dec; canon_dec = canon_dec; cmp_ord = cmp_ord} -> begin
     row_cmp
     end))


let __proj__Mksprims__item__cmp_float : sprims  ->  Prims.string  ->  Prims.string  ->  Prims.int = (fun ( projectee  :  sprims ) -> (match (projectee) with
| {row_cmp = row_cmp; cmp_float = cmp_float; cmp_dec = cmp_dec; canon_dec = canon_dec; cmp_ord = cmp_ord} -> begin
     cmp_float
     end))


let __proj__Mksprims__item__cmp_dec : sprims  ->  Prims.string  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  sprims ) -> (match (projectee) with
| {row_cmp = row_cmp; cmp_float = cmp_float; cmp_dec = cmp_dec; canon_dec = canon_dec; cmp_ord = cmp_ord} -> begin
     cmp_dec
     end))


let __proj__Mksprims__item__canon_dec : sprims  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( projectee  :  sprims ) -> (match (projectee) with
| {row_cmp = row_cmp; cmp_float = cmp_float; cmp_dec = cmp_dec; canon_dec = canon_dec; cmp_ord = cmp_ord} -> begin
     canon_dec
     end))


let __proj__Mksprims__item__cmp_ord : sprims  ->  Prims.string  ->  Prims.string  ->  Prims.int = (fun ( projectee  :  sprims ) -> (match (projectee) with
| {row_cmp = row_cmp; cmp_float = cmp_float; cmp_dec = cmp_dec; canon_dec = canon_dec; cmp_ord = cmp_ord} -> begin
     cmp_ord
     end))


let num_of : Pipeline.prims  ->  Pipeline.cell  ->  Prims.string = (fun ( pr  :  Pipeline.prims ) ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Int (i) -> begin
     (pr.float_of_int i)
     end
| Pipeline.Float (f) -> begin
     f
     end
| uu___ -> begin
     ""
     end))


let dec_of : sprims  ->  Pipeline.prims  ->  Pipeline.cell  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( sp  :  sprims ) ( pr  :  Pipeline.prims ) ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Int (i) -> begin
     FStar_Pervasives_Native.Some ((pr.int_text i))
     end
| Pipeline.Decimal (s) -> begin
     (sp.canon_dec s)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let compare_cells : sprims  ->  Pipeline.prims  ->  Pipeline.cell  ->  Pipeline.cell  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( sp  :  sprims ) ( pr  :  Pipeline.prims ) ( a  :  Pipeline.cell ) ( b  :  Pipeline.cell ) -> (match (((a), (b))) with
| (Pipeline.Int (uu___), Pipeline.Int (uu___1)) -> begin
     FStar_Pervasives_Native.Some ((sp.cmp_float (num_of pr a) (num_of pr b)))
     end
| (Pipeline.Int (uu___), Pipeline.Float (uu___1)) -> begin
     FStar_Pervasives_Native.Some ((sp.cmp_float (num_of pr a) (num_of pr b)))
     end
| (Pipeline.Float (uu___), Pipeline.Int (uu___1)) -> begin
     FStar_Pervasives_Native.Some ((sp.cmp_float (num_of pr a) (num_of pr b)))
     end
| (Pipeline.Float (uu___), Pipeline.Float (uu___1)) -> begin
     FStar_Pervasives_Native.Some ((sp.cmp_float (num_of pr a) (num_of pr b)))
     end
| (Pipeline.Decimal (uu___), Pipeline.Decimal (uu___1)) -> begin
     (match ((((dec_of sp pr a)), ((dec_of sp pr b)))) with
| (FStar_Pervasives_Native.Some (x), FStar_Pervasives_Native.Some (y)) -> begin
     (sp.cmp_dec x y)
     end
| uu___2 -> begin
     FStar_Pervasives_Native.None
     end)
     end
| (Pipeline.Decimal (uu___), Pipeline.Int (uu___1)) -> begin
     (match ((((dec_of sp pr a)), ((dec_of sp pr b)))) with
| (FStar_Pervasives_Native.Some (x), FStar_Pervasives_Native.Some (y)) -> begin
     (sp.cmp_dec x y)
     end
| uu___2 -> begin
     FStar_Pervasives_Native.None
     end)
     end
| (Pipeline.Int (uu___), Pipeline.Decimal (uu___1)) -> begin
     (match ((((dec_of sp pr a)), ((dec_of sp pr b)))) with
| (FStar_Pervasives_Native.Some (x), FStar_Pervasives_Native.Some (y)) -> begin
     (sp.cmp_dec x y)
     end
| uu___2 -> begin
     FStar_Pervasives_Native.None
     end)
     end
| (Pipeline.Bool (x), Pipeline.Bool (y)) -> begin
     FStar_Pervasives_Native.Some ( 
if (Prims.op_Equals x y) then begin
     (Prims.parse_int "0")
     end else begin
      
if x then begin
     (Prims.parse_int "1")
     end else begin
     (Prims.parse_int "-1")
     end
     end)
     end
| (Pipeline.Str (x), Pipeline.Str (y)) -> begin
     FStar_Pervasives_Native.Some ((sp.cmp_ord x y))
     end
| (Pipeline.Date (x), Pipeline.Date (y)) -> begin
     FStar_Pervasives_Native.Some ((sp.cmp_ord x y))
     end
| (Pipeline.Timestamp (x), Pipeline.Timestamp (y)) -> begin
     FStar_Pervasives_Native.Some ((sp.cmp_ord x y))
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let rec resolve_keys : Pipeline.schema  ->  Prims.list<(Prims.string * Pipeline.sort_dir)>  ->  Prims.list<(Prims.nat * Pipeline.sort_dir)> = (fun ( cols  :  Pipeline.schema ) ( ks  :  Prims.list<(Prims.string * Pipeline.sort_dir)> ) -> (match (ks) with
| [] -> begin
     []
     end
| ((c, d))::rest -> begin
     (match ((Pipeline.index_of c cols)) with
| FStar_Pervasives_Native.Some (i) -> begin
     (((i), (d)))::(resolve_keys cols rest)
     end
| FStar_Pervasives_Native.None -> begin
     (resolve_keys cols rest)
     end)
     end))


let cell_at : Prims.nat  ->  Prims.list<Pipeline.cell>  ->  Pipeline.cell = (fun ( j  :  Prims.nat ) ( r  :  Prims.list<Pipeline.cell> ) ->  
if (j < (Pipeline.len r)) then begin
     (Pipeline.nth r j)
     end else begin
     Pipeline.Null
     end)


let rec compare_resolved : sprims  ->  Pipeline.prims  ->  Prims.list<(Prims.nat * Pipeline.sort_dir)>  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int = (fun ( sp  :  sprims ) ( pr  :  Pipeline.prims ) ( keys  :  Prims.list<(Prims.nat * Pipeline.sort_dir)> ) ( r1  :  Prims.list<Pipeline.cell> ) ( r2  :  Prims.list<Pipeline.cell> ) -> (match (keys) with
| [] -> begin
     (Prims.parse_int "0")
     end
| ((i, dir))::rest -> begin
     (

let a = (cell_at i r1)
in (

let b = (cell_at i r2)
in (

let c = (match ((((Prims.op_Equals a Pipeline.Null)), ((Prims.op_Equals b Pipeline.Null)))) with
| (true, true) -> begin
     (Prims.parse_int "0")
     end
| (true, false) -> begin
     (Prims.parse_int "1")
     end
| (false, true) -> begin
     (Prims.parse_int "-1")
     end
| (false, false) -> begin
     (match ((compare_cells sp pr a b)) with
| FStar_Pervasives_Native.Some (c1) -> begin
      
if (Prims.op_Equals dir Pipeline.Asc) then begin
     c1
     end else begin
     ((Prims.parse_int "0") - c1)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end)
     end)
in  
if (Prims.op_Less_Greater c (Prims.parse_int "0")) then begin
     c
     end else begin
     (compare_resolved sp pr rest r1 r2)
     end)))
     end))


let family : Pipeline.cell  ->  Prims.nat = (fun ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Int (uu___) -> begin
     (Prims.parse_int "0")
     end
| Pipeline.Float (uu___) -> begin
     (Prims.parse_int "1")
     end
| Pipeline.Decimal (uu___) -> begin
     (Prims.parse_int "2")
     end
| Pipeline.Bool (uu___) -> begin
     (Prims.parse_int "3")
     end
| Pipeline.Str (uu___) -> begin
     (Prims.parse_int "4")
     end
| Pipeline.Date (uu___) -> begin
     (Prims.parse_int "5")
     end
| Pipeline.Timestamp (uu___) -> begin
     (Prims.parse_int "6")
     end
| Pipeline.Null -> begin
     (Prims.parse_int "7")
     end))


let one_family : Pipeline.cell  ->  Pipeline.cell  ->  Prims.bool = (fun ( a  :  Pipeline.cell ) ( b  :  Pipeline.cell ) -> (

let fa = (family a)
in (

let fb = (family b)
in (((Prims.op_Equals fa fb) || ((Prims.op_Equals fa (Prims.parse_int "0")) && ((Prims.op_Equals fb (Prims.parse_int "1")) || (Prims.op_Equals fb (Prims.parse_int "2"))))) || ((Prims.op_Equals fb (Prims.parse_int "0")) && ((Prims.op_Equals fa (Prims.parse_int "1")) || (Prims.op_Equals fa (Prims.parse_int "2"))))))))


let dec_cmp : sprims  ->  Prims.string  ->  Prims.string  ->  Prims.int = (fun ( sp  :  sprims ) ( x  :  Prims.string ) ( y  :  Prims.string ) -> (match ((sp.cmp_dec x y)) with
| FStar_Pervasives_Native.Some (c) -> begin
     c
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end))


let canonical : sprims  ->  Prims.string  ->  Prims.bool = (fun ( sp  :  sprims ) ( x  :  Prims.string ) -> ((match ((sp.canon_dec x)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (Prims.op_Equals (match ((sp.canon_dec x)) with
| FStar_Pervasives_Native.Some (v) -> begin
     v
     end) x)))


let fits : sprims  ->  Pipeline.column_type  ->  Pipeline.cell  ->  Prims.bool = (fun ( sp  :  sprims ) ( ty  :  Pipeline.column_type ) ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Null -> begin
     true
     end
| Pipeline.Int (uu___) -> begin
     (((Prims.op_Equals ty Pipeline.IntType) || (Prims.op_Equals ty Pipeline.FloatType)) || (Prims.op_Equals ty Pipeline.DecimalType))
     end
| Pipeline.Float (uu___) -> begin
     (Prims.op_Equals ty Pipeline.FloatType)
     end
| Pipeline.Decimal (s) -> begin
     ((Prims.op_Equals ty Pipeline.DecimalType) && (match ((sp.canon_dec s)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))
     end
| Pipeline.Bool (uu___) -> begin
     (Prims.op_Equals ty Pipeline.BoolType)
     end
| Pipeline.Str (uu___) -> begin
     (Prims.op_Equals ty Pipeline.StringType)
     end
| Pipeline.Date (uu___) -> begin
     (Prims.op_Equals ty Pipeline.DateType)
     end
| Pipeline.Timestamp (uu___) -> begin
     (Prims.op_Equals ty Pipeline.TimestampType)
     end))


let present_cell : sprims  ->  Pipeline.column_type  ->  Pipeline.cell  ->  Prims.bool = (fun ( sp  :  sprims ) ( ty  :  Pipeline.column_type ) ( c  :  Pipeline.cell ) -> ((fits sp ty c) && (Prims.op_Less_Greater c Pipeline.Null)))


let cc : sprims  ->  Pipeline.prims  ->  Pipeline.cell  ->  Pipeline.cell  ->  Prims.int = (fun ( sp  :  sprims ) ( pr  :  Pipeline.prims ) ( a  :  Pipeline.cell ) ( b  :  Pipeline.cell ) -> (match ((compare_cells sp pr a b)) with
| FStar_Pervasives_Native.Some (c) -> begin
     c
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end))


let dkey : sprims  ->  Pipeline.prims  ->  Pipeline.cell  ->  Prims.string = (fun ( sp  :  sprims ) ( pr  :  Pipeline.prims ) ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Int (i) -> begin
     (pr.int_text i)
     end
| Pipeline.Decimal (s) -> begin
     (match ((sp.canon_dec s)) with
| FStar_Pervasives_Native.Some (x) -> begin
     x
     end
| FStar_Pervasives_Native.None -> begin
     ""
     end)
     end
| uu___ -> begin
     ""
     end))


let key_cc : sprims  ->  Pipeline.prims  ->  Pipeline.sort_dir  ->  Pipeline.cell  ->  Pipeline.cell  ->  Prims.int = (fun ( sp  :  sprims ) ( pr  :  Pipeline.prims ) ( dir  :  Pipeline.sort_dir ) ( a  :  Pipeline.cell ) ( b  :  Pipeline.cell ) -> (match ((((Prims.op_Equals a Pipeline.Null)), ((Prims.op_Equals b Pipeline.Null)))) with
| (true, true) -> begin
     (Prims.parse_int "0")
     end
| (true, false) -> begin
     (Prims.parse_int "1")
     end
| (false, true) -> begin
     (Prims.parse_int "-1")
     end
| (false, false) -> begin
     (match ((compare_cells sp pr a b)) with
| FStar_Pervasives_Native.Some (c) -> begin
      
if (Prims.op_Equals dir Pipeline.Asc) then begin
     c
     end else begin
     ((Prims.parse_int "0") - c)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end)
     end))


let key_cmp : sprims  ->  Pipeline.prims  ->  (Prims.nat * Pipeline.sort_dir)  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.int = (fun ( sp  :  sprims ) ( pr  :  Pipeline.prims ) ( k  :  (Prims.nat * Pipeline.sort_dir) ) ( r1  :  Prims.list<Pipeline.cell> ) ( r2  :  Prims.list<Pipeline.cell> ) -> (key_cc sp pr (FStar_Pervasives_Native.snd k) (cell_at (FStar_Pervasives_Native.fst k) r1) (cell_at (FStar_Pervasives_Native.fst k) r2)))


let rec typed_row : sprims  ->  Pipeline.schema  ->  Prims.list<(Prims.nat * Pipeline.sort_dir)>  ->  Prims.list<Pipeline.cell>  ->  Prims.bool = (fun ( sp  :  sprims ) ( cols  :  Pipeline.schema ) ( keys  :  Prims.list<(Prims.nat * Pipeline.sort_dir)> ) ( r  :  Prims.list<Pipeline.cell> ) -> (match (keys) with
| [] -> begin
     true
     end
| ((i, uu___))::rest -> begin
     (((i < (Pipeline.len cols)) && (fits sp (FStar_Pervasives_Native.snd (Pipeline.nth cols i)) (cell_at i r))) && (typed_row sp cols rest r))
     end))




