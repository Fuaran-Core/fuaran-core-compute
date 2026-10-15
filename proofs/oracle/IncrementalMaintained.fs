module IncrementalMaintained
type mprims = {agg_eval : Pipeline.agg_fn  ->  Pipeline.column_type  ->  Prims.list<Pipeline.cell>  ->  Pipeline.outcome<Pipeline.cell, Pipeline.eval_error>; key_eq : Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.bool; win_cells : Pipeline.schema  ->  Pipeline.window_spec  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Pipeline.outcome<Prims.list<Pipeline.cell>, Pipeline.eval_error>; key_token : Prims.list<Pipeline.cell>  ->  Incremental.token}


let __proj__Mkmprims__item__agg_eval : mprims  ->  Pipeline.agg_fn  ->  Pipeline.column_type  ->  Prims.list<Pipeline.cell>  ->  Pipeline.outcome<Pipeline.cell, Pipeline.eval_error> = (fun ( projectee  :  mprims ) -> (match (projectee) with
| {agg_eval = agg_eval; key_eq = key_eq; win_cells = win_cells; key_token = key_token} -> begin
     agg_eval
     end))


let __proj__Mkmprims__item__key_eq : mprims  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell>  ->  Prims.bool = (fun ( projectee  :  mprims ) -> (match (projectee) with
| {agg_eval = agg_eval; key_eq = key_eq; win_cells = win_cells; key_token = key_token} -> begin
     key_eq
     end))


let __proj__Mkmprims__item__win_cells : mprims  ->  Pipeline.schema  ->  Pipeline.window_spec  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Pipeline.outcome<Prims.list<Pipeline.cell>, Pipeline.eval_error> = (fun ( projectee  :  mprims ) -> (match (projectee) with
| {agg_eval = agg_eval; key_eq = key_eq; win_cells = win_cells; key_token = key_token} -> begin
     win_cells
     end))


let __proj__Mkmprims__item__key_token : mprims  ->  Prims.list<Pipeline.cell>  ->  Incremental.token = (fun ( projectee  :  mprims ) -> (match (projectee) with
| {agg_eval = agg_eval; key_eq = key_eq; win_cells = win_cells; key_token = key_token} -> begin
     key_token
     end))

type mstep =
| MFilter of Pipeline.col_expr
| MDerive of Prims.string * Pipeline.col_expr
| MProject of Prims.list<(Prims.string * Prims.string)>
| MLimit of Prims.int * Prims.int
| MWindow of Pipeline.window_spec
| MJoin of Pipeline.data_source * Prims.list<(Prims.string * Prims.string)> * Prims.bool


let uu___is_MFilter : mstep  ->  Prims.bool = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MFilter (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MFilter__item___0 : mstep  ->  Pipeline.col_expr = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MFilter (_0) -> begin
     _0
     end))


let uu___is_MDerive : mstep  ->  Prims.bool = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MDerive (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MDerive__item___0 : mstep  ->  Prims.string = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MDerive (_0, _1) -> begin
     _0
     end))


let __proj__MDerive__item___1 : mstep  ->  Pipeline.col_expr = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MDerive (_0, _1) -> begin
     _1
     end))


let uu___is_MProject : mstep  ->  Prims.bool = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MProject (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MProject__item___0 : mstep  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MProject (_0) -> begin
     _0
     end))


let uu___is_MLimit : mstep  ->  Prims.bool = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MLimit (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MLimit__item___0 : mstep  ->  Prims.int = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MLimit (_0, _1) -> begin
     _0
     end))


let __proj__MLimit__item___1 : mstep  ->  Prims.int = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MLimit (_0, _1) -> begin
     _1
     end))


let uu___is_MWindow : mstep  ->  Prims.bool = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MWindow (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MWindow__item___0 : mstep  ->  Pipeline.window_spec = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MWindow (_0) -> begin
     _0
     end))


let uu___is_MJoin : mstep  ->  Prims.bool = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MJoin (_0, _1, _2) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__MJoin__item___0 : mstep  ->  Pipeline.data_source = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MJoin (_0, _1, _2) -> begin
     _0
     end))


let __proj__MJoin__item___1 : mstep  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MJoin (_0, _1, _2) -> begin
     _1
     end))


let __proj__MJoin__item___2 : mstep  ->  Prims.bool = (fun ( projectee  :  mstep ) -> (match (projectee) with
| MJoin (_0, _1, _2) -> begin
     _2
     end))


let embed_mstep : mstep  ->  Pipeline.transform = (fun ( s  :  mstep ) -> (match (s) with
| MFilter (pred) -> begin
     Pipeline.Filter (pred)
     end
| MDerive (name, x) -> begin
     Pipeline.Derive (name, x)
     end
| MProject (pairs) -> begin
     Pipeline.Project (pairs)
     end
| MLimit (n, off) -> begin
     Pipeline.Limit (Pipeline.SlotLit (n), Pipeline.SlotLit (off))
     end
| MWindow (spec) -> begin
     Pipeline.Window (spec)
     end
| MJoin (src, on, keep) -> begin
     Pipeline.Join (src, on,  
if keep then begin
     Pipeline.Semi
     end else begin
     Pipeline.Anti
     end)
     end))


let rec embed_steps : Prims.list<mstep>  ->  Prims.list<Pipeline.transform> = (fun ( p  :  Prims.list<mstep> ) -> (match (p) with
| [] -> begin
     []
     end
| (s)::rest -> begin
     ((embed_mstep s))::(embed_steps rest)
     end))

type mpipe = {prefix : Prims.list<mstep>; grouped : FStar_Pervasives_Native.option<(Prims.list<Prims.string> * Prims.list<Pipeline.agg> * Prims.list<mstep>)>}


let __proj__Mkmpipe__item__prefix : mpipe  ->  Prims.list<mstep> = (fun ( projectee  :  mpipe ) -> (match (projectee) with
| {prefix = prefix; grouped = grouped} -> begin
     prefix
     end))


let __proj__Mkmpipe__item__grouped : mpipe  ->  FStar_Pervasives_Native.option<(Prims.list<Prims.string> * Prims.list<Pipeline.agg> * Prims.list<mstep>)> = (fun ( projectee  :  mpipe ) -> (match (projectee) with
| {prefix = prefix; grouped = grouped} -> begin
     grouped
     end))


let embed_pipe : mpipe  ->  Prims.list<Pipeline.transform> = (fun ( p  :  mpipe ) -> (match (p.grouped) with
| FStar_Pervasives_Native.None -> begin
     (embed_steps p.prefix)
     end
| FStar_Pervasives_Native.Some (keys, aggs, tail) -> begin
     (Pipeline.app (embed_steps p.prefix) ((Pipeline.GroupBy (keys, aggs))::(embed_steps tail)))
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


let rec take_a = (fun ( n  :  Prims.nat ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::rest -> begin
      
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     []
     end else begin
     (x)::(take_a (n - (Prims.parse_int "1")) rest)
     end
     end))


let rec drop_a = (fun ( n  :  Prims.nat ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::rest -> begin
      
if (Prims.op_Equals n (Prims.parse_int "0")) then begin
     l
     end else begin
     (drop_a (n - (Prims.parse_int "1")) rest)
     end
     end))


let max0 : Prims.int  ->  Prims.nat = (fun ( i  :  Prims.int ) ->  
if (i < (Prims.parse_int "0")) then begin
     (Prims.parse_int "0")
     end else begin
     i
     end)


let slice = (fun ( off  :  Prims.int ) ( n  :  Prims.int ) ( l  :  Prims.list<'b> ) -> (take_a (max0 n) (drop_a (max0 off) l)))


let cell_at : Prims.nat  ->  Prims.list<Pipeline.cell>  ->  Pipeline.cell = (fun ( j  :  Prims.nat ) ( r  :  Prims.list<Pipeline.cell> ) ->  
if (j < (Pipeline.len r)) then begin
     (Pipeline.nth r j)
     end else begin
     Pipeline.Null
     end)


let rec proj_row : Pipeline.schema  ->  Prims.list<Prims.string>  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell> = (fun ( cols  :  Pipeline.schema ) ( ns  :  Prims.list<Prims.string> ) ( r  :  Prims.list<Pipeline.cell> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::rest -> begin
     ((match ((Pipeline.index_of n cols)) with
| FStar_Pervasives_Native.Some (j) -> begin
     (cell_at j r)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end))::(proj_row cols rest r)
     end))


let rec proj_alive : Pipeline.schema  ->  Prims.list<Prims.string>  ->  Incremental.alive  ->  Incremental.alive = (fun ( cols  :  Pipeline.schema ) ( ns  :  Prims.list<Prims.string> ) ( a  :  Incremental.alive ) -> (match (a) with
| [] -> begin
     []
     end
| ((t, r))::rest -> begin
     (((t), ((proj_row cols ns r))))::(proj_alive cols ns rest)
     end))


let rec proj_rows : Pipeline.schema  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.list<Prims.list<Pipeline.cell>> = (fun ( cols  :  Pipeline.schema ) ( ns  :  Prims.list<Prims.string> ) ( rows  :  Prims.list<Prims.list<Pipeline.cell>> ) -> (match (rows) with
| [] -> begin
     []
     end
| (r)::rest -> begin
     ((proj_row cols ns r))::(proj_rows cols ns rest)
     end))


let rec pair_cells : Incremental.alive  ->  Prims.list<Pipeline.cell>  ->  Incremental.step_cache = (fun ( a  :  Incremental.alive ) ( cs  :  Prims.list<Pipeline.cell> ) -> (match (((a), (cs))) with
| ([], []) -> begin
     []
     end
| (((t, uu___))::ar, (v)::cr) -> begin
     (((t), (v)))::(pair_cells ar cr)
     end))


let rec keep_rows_by : (Prims.list<Pipeline.cell>  ->  Prims.bool)  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.list<Prims.list<Pipeline.cell>> = (fun ( vf  :  Prims.list<Pipeline.cell>  ->  Prims.bool ) ( rows  :  Prims.list<Prims.list<Pipeline.cell>> ) -> (match (rows) with
| [] -> begin
     []
     end
| (r)::rest -> begin
      
if (vf r) then begin
     (r)::(keep_rows_by vf rest)
     end else begin
     (keep_rows_by vf rest)
     end
     end))


type cell_fn = Pipeline.row_of  ->  Pipeline.outcome<Pipeline.cell, Pipeline.eval_error>


let expr_fn : Pipeline.prims  ->  Pipeline.param_env  ->  Pipeline.schema  ->  Pipeline.col_expr  ->  cell_fn = (fun ( pr  :  Pipeline.prims ) ( env  :  Pipeline.param_env ) ( cols  :  Pipeline.schema ) ( x  :  Pipeline.col_expr ) ( r  :  Pipeline.row_of ) -> (Pipeline.eval_expr pr env cols r x))


let bool_fn : Prims.nat  ->  (Prims.list<Pipeline.cell>  ->  Prims.bool)  ->  cell_fn = (fun ( n  :  Prims.nat ) ( vf  :  Prims.list<Pipeline.cell>  ->  Prims.bool ) ( r  :  Pipeline.row_of ) -> Pipeline.Ok (Pipeline.Bool ((vf r))))


let rec step_cells_by : Prims.nat  ->  cell_fn  ->  Incremental.reuse_fn  ->  Incremental.step_cache  ->  Incremental.alive  ->  Pipeline.outcome<Incremental.step_cache, Pipeline.eval_error> = (fun ( n  :  Prims.nat ) ( cf  :  cell_fn ) ( reuse  :  Incremental.reuse_fn ) ( prior  :  Incremental.step_cache ) ( a  :  Incremental.alive ) -> (match (a) with
| [] -> begin
     Pipeline.Ok ([])
     end
| ((t, r))::rest -> begin
     (

let here =  
if (reuse t) then begin
     (match ((Pipeline.assoc t prior)) with
| FStar_Pervasives_Native.Some (c) -> begin
     Pipeline.Ok (c)
     end
| FStar_Pervasives_Native.None -> begin
     (cf r)
     end)
     end else begin
     (cf r)
     end
in (match (here) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (c) -> begin
     (match ((step_cells_by n cf reuse prior rest)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (cs) -> begin
     Pipeline.Ok ((((t), (c)))::cs)
     end)
     end))
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


let rec mem_a_gen = (fun ( x  :  'b ) ( l  :  Prims.list<'b> ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::rest -> begin
     ((Prims.op_Equals y x) || (mem_a_gen x rest))
     end))

type group = {g_key : Prims.list<Pipeline.cell>; g_members : Incremental.alive}


let __proj__Mkgroup__item__g_key : group  ->  Prims.list<Pipeline.cell> = (fun ( projectee  :  group ) -> (match (projectee) with
| {g_key = g_key; g_members = g_members} -> begin
     g_key
     end))


let __proj__Mkgroup__item__g_members : group  ->  Incremental.alive = (fun ( projectee  :  group ) -> (match (projectee) with
| {g_key = g_key; g_members = g_members} -> begin
     g_members
     end))


let rec add_member : Prims.list<Pipeline.cell>  ->  (Incremental.token * Prims.list<Pipeline.cell>)  ->  Prims.list<group>  ->  Prims.list<group> = (fun ( key  :  Prims.list<Pipeline.cell> ) ( m  :  (Incremental.token * Prims.list<Pipeline.cell>) ) ( gs  :  Prims.list<group> ) -> (match (gs) with
| [] -> begin
     ({g_key = key; g_members = (m)::[]})::[]
     end
| (g)::rest -> begin
      
if (Prims.op_Equals g.g_key key) then begin
     ({g_key = g.g_key; g_members = (Pipeline.app g.g_members ((m)::[]))})::rest
     end else begin
     (g)::(add_member key m rest)
     end
     end))


let rec groups_acc : Pipeline.schema  ->  Prims.list<Prims.string>  ->  Prims.list<group>  ->  Incremental.alive  ->  Prims.list<group> = (fun ( cols  :  Pipeline.schema ) ( keys  :  Prims.list<Prims.string> ) ( gs  :  Prims.list<group> ) ( a  :  Incremental.alive ) -> (match (a) with
| [] -> begin
     gs
     end
| ((t, r))::rest -> begin
     (groups_acc cols keys (add_member (proj_row cols keys r) ((t), (r)) gs) rest)
     end))


let groups_of : Pipeline.schema  ->  Prims.list<Prims.string>  ->  Incremental.alive  ->  Prims.list<group> = (fun ( cols  :  Pipeline.schema ) ( keys  :  Prims.list<Prims.string> ) ( a  :  Incremental.alive ) -> (groups_acc cols keys [] a))

type pgroup = {p_key : Prims.list<Pipeline.cell>; p_tokens : Prims.list<Incremental.token>; p_aggs : Prims.list<Pipeline.cell>}


let __proj__Mkpgroup__item__p_key : pgroup  ->  Prims.list<Pipeline.cell> = (fun ( projectee  :  pgroup ) -> (match (projectee) with
| {p_key = p_key; p_tokens = p_tokens; p_aggs = p_aggs} -> begin
     p_key
     end))


let __proj__Mkpgroup__item__p_tokens : pgroup  ->  Prims.list<Incremental.token> = (fun ( projectee  :  pgroup ) -> (match (projectee) with
| {p_key = p_key; p_tokens = p_tokens; p_aggs = p_aggs} -> begin
     p_tokens
     end))


let __proj__Mkpgroup__item__p_aggs : pgroup  ->  Prims.list<Pipeline.cell> = (fun ( projectee  :  pgroup ) -> (match (projectee) with
| {p_key = p_key; p_tokens = p_tokens; p_aggs = p_aggs} -> begin
     p_aggs
     end))


let rec find_key : Prims.list<Pipeline.cell>  ->  Prims.list<pgroup>  ->  FStar_Pervasives_Native.option<pgroup> = (fun ( key  :  Prims.list<Pipeline.cell> ) ( ps  :  Prims.list<pgroup> ) -> (match (ps) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (p)::rest -> begin
      
if (Prims.op_Equals p.p_key key) then begin
     FStar_Pervasives_Native.Some (p)
     end else begin
     (find_key key rest)
     end
     end))


let rec every_reuse : Incremental.reuse_fn  ->  Incremental.alive  ->  Prims.bool = (fun ( reuse  :  Incremental.reuse_fn ) ( a  :  Incremental.alive ) -> (match (a) with
| [] -> begin
     true
     end
| ((t, uu___))::rest -> begin
     ((reuse t) && (every_reuse reuse rest))
     end))


let all_reuse : Incremental.reuse_fn  ->  Incremental.alive  ->  Prims.bool = (fun ( reuse  :  Incremental.reuse_fn ) ( a  :  Incremental.alive ) -> (match (a) with
| [] -> begin
     false
     end
| uu___ -> begin
     (every_reuse reuse a)
     end))


let rec agg_cells_of : mprims  ->  Pipeline.schema  ->  Prims.list<Pipeline.agg>  ->  Incremental.alive  ->  Pipeline.outcome<Prims.list<Pipeline.cell>, Pipeline.eval_error> = (fun ( mp  :  mprims ) ( cols  :  Pipeline.schema ) ( aggs  :  Prims.list<Pipeline.agg> ) ( members  :  Incremental.alive ) -> (match (aggs) with
| [] -> begin
     Pipeline.Ok ([])
     end
| (a)::rest -> begin
     (

let ty = (match ((Pipeline.assoc a.a_of cols)) with
| FStar_Pervasives_Native.Some (ty1) -> begin
     ty1
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.StringType
     end)
in (

let j = (match ((Pipeline.index_of a.a_of cols)) with
| FStar_Pervasives_Native.Some (j1) -> begin
     j1
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end)
in (match ((mp.agg_eval a.a_fn ty (Relational.col_cells j (Incremental.rows_of members)))) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (c) -> begin
     (match ((agg_cells_of mp cols rest members)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (cs) -> begin
     Pipeline.Ok ((c)::cs)
     end)
     end)))
     end))


let group_aggs : mprims  ->  Incremental.reuse_fn  ->  Prims.list<pgroup>  ->  Pipeline.schema  ->  Prims.list<Pipeline.agg>  ->  group  ->  Pipeline.outcome<(Prims.list<Pipeline.cell> * Prims.bool), Pipeline.eval_error> = (fun ( mp  :  mprims ) ( reuse  :  Incremental.reuse_fn ) ( prior  :  Prims.list<pgroup> ) ( cols  :  Pipeline.schema ) ( aggs  :  Prims.list<Pipeline.agg> ) ( g  :  group ) -> (

let found =  
if (all_reuse reuse g.g_members) then begin
     (find_key g.g_key prior)
     end else begin
     FStar_Pervasives_Native.None
     end
in (match (found) with
| FStar_Pervasives_Native.Some (pg) -> begin
      
if ((Prims.op_Equals pg.p_tokens (tokens_of g.g_members)) && (Prims.op_Equals (Pipeline.len pg.p_aggs) (Pipeline.len aggs))) then begin
     Pipeline.Ok (((pg.p_aggs), (true)))
     end else begin
     (match ((agg_cells_of mp cols aggs g.g_members)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (cs) -> begin
     Pipeline.Ok (((cs), (false)))
     end)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (match ((agg_cells_of mp cols aggs g.g_members)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (cs) -> begin
     Pipeline.Ok (((cs), (false)))
     end)
     end)))

type gout = {o_key : Prims.list<Pipeline.cell>; o_tokens : Prims.list<Incremental.token>; o_aggs : Prims.list<Pipeline.cell>; o_reused : Prims.bool}


let __proj__Mkgout__item__o_key : gout  ->  Prims.list<Pipeline.cell> = (fun ( projectee  :  gout ) -> (match (projectee) with
| {o_key = o_key; o_tokens = o_tokens; o_aggs = o_aggs; o_reused = o_reused} -> begin
     o_key
     end))


let __proj__Mkgout__item__o_tokens : gout  ->  Prims.list<Incremental.token> = (fun ( projectee  :  gout ) -> (match (projectee) with
| {o_key = o_key; o_tokens = o_tokens; o_aggs = o_aggs; o_reused = o_reused} -> begin
     o_tokens
     end))


let __proj__Mkgout__item__o_aggs : gout  ->  Prims.list<Pipeline.cell> = (fun ( projectee  :  gout ) -> (match (projectee) with
| {o_key = o_key; o_tokens = o_tokens; o_aggs = o_aggs; o_reused = o_reused} -> begin
     o_aggs
     end))


let __proj__Mkgout__item__o_reused : gout  ->  Prims.bool = (fun ( projectee  :  gout ) -> (match (projectee) with
| {o_key = o_key; o_tokens = o_tokens; o_aggs = o_aggs; o_reused = o_reused} -> begin
     o_reused
     end))


let rec all_agg_width : Prims.list<Pipeline.agg>  ->  Prims.list<gout>  ->  Prims.bool = (fun ( aggs  :  Prims.list<Pipeline.agg> ) ( os  :  Prims.list<gout> ) -> (match (os) with
| [] -> begin
     true
     end
| (o)::rest -> begin
     ((Prims.op_Equals (Pipeline.len o.o_aggs) (Pipeline.len aggs)) && (all_agg_width aggs rest))
     end))


let rec groups_out : mprims  ->  Incremental.reuse_fn  ->  Prims.list<pgroup>  ->  Pipeline.schema  ->  Prims.list<Pipeline.agg>  ->  Prims.list<group>  ->  Pipeline.outcome<Prims.list<gout>, Pipeline.eval_error> = (fun ( mp  :  mprims ) ( reuse  :  Incremental.reuse_fn ) ( prior  :  Prims.list<pgroup> ) ( cols  :  Pipeline.schema ) ( aggs  :  Prims.list<Pipeline.agg> ) ( gs  :  Prims.list<group> ) -> (match (gs) with
| [] -> begin
     Pipeline.Ok ([])
     end
| (g)::rest -> begin
     (match ((group_aggs mp reuse prior cols aggs g)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (cs, reused) -> begin
     (match ((groups_out mp reuse prior cols aggs rest)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (os) -> begin
     Pipeline.Ok (({o_key = g.g_key; o_tokens = (tokens_of g.g_members); o_aggs = cs; o_reused = reused})::os)
     end)
     end)
     end))


let rec grows : Prims.list<gout>  ->  Prims.list<Prims.list<Pipeline.cell>> = (fun ( os  :  Prims.list<gout> ) -> (match (os) with
| [] -> begin
     []
     end
| (o)::rest -> begin
     ((Pipeline.app o.o_key o.o_aggs))::(grows rest)
     end))


let rec galive : mprims  ->  Prims.list<gout>  ->  Incremental.alive = (fun ( mp  :  mprims ) ( os  :  Prims.list<gout> ) -> (match (os) with
| [] -> begin
     []
     end
| (o)::rest -> begin
     ((((mp.key_token o.o_key)), ((Pipeline.app o.o_key o.o_aggs))))::(galive mp rest)
     end))


let rec pgroups_of : Prims.list<gout>  ->  Prims.list<pgroup> = (fun ( os  :  Prims.list<gout> ) -> (match (os) with
| [] -> begin
     []
     end
| (o)::rest -> begin
     ({p_key = o.o_key; p_tokens = o.o_tokens; p_aggs = o.o_aggs})::(pgroups_of rest)
     end))


let rec unreused_tokens : mprims  ->  Prims.list<gout>  ->  Prims.list<Incremental.token> = (fun ( mp  :  mprims ) ( os  :  Prims.list<gout> ) -> (match (os) with
| [] -> begin
     []
     end
| (o)::rest -> begin
      
if o.o_reused then begin
     (unreused_tokens mp rest)
     end else begin
     ((mp.key_token o.o_key))::(unreused_tokens mp rest)
     end
     end))


let rec all_unreused : Prims.list<gout>  ->  Prims.bool = (fun ( os  :  Prims.list<gout> ) -> (match (os) with
| [] -> begin
     true
     end
| (o)::rest -> begin
     ((not (o.o_reused)) && (all_unreused rest))
     end))


let tail_reuse : mprims  ->  Prims.list<gout>  ->  Incremental.reuse_fn = (fun ( mp  :  mprims ) ( os  :  Prims.list<gout> ) ->  
if (all_unreused os) then begin
     Incremental.no_reuse
     end else begin
     (Incremental.reuse_of (FStar_Pervasives_Native.Some ((unreused_tokens mp os))))
     end)


let rec all_key_width : Prims.list<Prims.string>  ->  Prims.list<gout>  ->  Prims.bool = (fun ( keys  :  Prims.list<Prims.string> ) ( os  :  Prims.list<gout> ) -> (match (os) with
| [] -> begin
     true
     end
| (o)::rest -> begin
     ((Prims.op_Equals (Pipeline.len o.o_key) (Pipeline.len keys)) && (all_key_width keys rest))
     end))


let rec all_gkey_width : Prims.list<Prims.string>  ->  Prims.list<group>  ->  Prims.bool = (fun ( keys  :  Prims.list<Prims.string> ) ( gs  :  Prims.list<group> ) -> (match (gs) with
| [] -> begin
     true
     end
| (g)::rest -> begin
     ((Prims.op_Equals (Pipeline.len g.g_key) (Pipeline.len keys)) && (all_gkey_width keys rest))
     end))

type gres = {gs_out : Incremental.walked; gs_os : Prims.list<gout>}


let __proj__Mkgres__item__gs_out : gres  ->  Incremental.walked = (fun ( projectee  :  gres ) -> (match (projectee) with
| {gs_out = gs_out; gs_os = gs_os} -> begin
     gs_out
     end))


let __proj__Mkgres__item__gs_os : gres  ->  Prims.list<gout> = (fun ( projectee  :  gres ) -> (match (projectee) with
| {gs_out = gs_out; gs_os = gs_os} -> begin
     gs_os
     end))


let group_step : mprims  ->  Incremental.reuse_fn  ->  Prims.list<pgroup>  ->  Pipeline.schema  ->  Prims.list<Prims.string>  ->  Prims.list<Pipeline.agg>  ->  Incremental.alive  ->  Pipeline.outcome<gres, Pipeline.eval_error> = (fun ( mp  :  mprims ) ( reuse  :  Incremental.reuse_fn ) ( prior  :  Prims.list<pgroup> ) ( cols  :  Pipeline.schema ) ( keys  :  Prims.list<Prims.string> ) ( aggs  :  Prims.list<Pipeline.agg> ) ( a  :  Incremental.alive ) -> (match ((Relational.group_by_cols cols keys aggs)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (out) -> begin
     (

let gs = (groups_of cols keys a)
in (match ((groups_out mp reuse prior cols aggs gs)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (os) -> begin
     Pipeline.Ok ({gs_out = Incremental.W (out, (galive mp os)); gs_os = os})
     end))
     end))

type entry = {e_cells : Incremental.step_cache; e_type : FStar_Pervasives_Native.option<Pipeline.column_type>; e_keys : FStar_Pervasives_Native.option<Prims.list<Prims.list<Pipeline.cell>>>}


let __proj__Mkentry__item__e_cells : entry  ->  Incremental.step_cache = (fun ( projectee  :  entry ) -> (match (projectee) with
| {e_cells = e_cells; e_type = e_type; e_keys = e_keys} -> begin
     e_cells
     end))


let __proj__Mkentry__item__e_type : entry  ->  FStar_Pervasives_Native.option<Pipeline.column_type> = (fun ( projectee  :  entry ) -> (match (projectee) with
| {e_cells = e_cells; e_type = e_type; e_keys = e_keys} -> begin
     e_type
     end))


let __proj__Mkentry__item__e_keys : entry  ->  FStar_Pervasives_Native.option<Prims.list<Prims.list<Pipeline.cell>>> = (fun ( projectee  :  entry ) -> (match (projectee) with
| {e_cells = e_cells; e_type = e_type; e_keys = e_keys} -> begin
     e_keys
     end))


let prior_head : Prims.list<entry>  ->  entry = (fun ( prior  :  Prims.list<entry> ) -> (match (prior) with
| [] -> begin
     {e_cells = []; e_type = FStar_Pervasives_Native.None; e_keys = FStar_Pervasives_Native.None}
     end
| (e)::uu___ -> begin
     e
     end))


let prior_rest : Prims.list<entry>  ->  Prims.list<entry> = (fun ( prior  :  Prims.list<entry> ) -> (match (prior) with
| [] -> begin
     []
     end
| (uu___)::rest -> begin
     rest
     end))


let right_rows : Relational.resolver  ->  Pipeline.data_source  ->  Pipeline.outcome<Prims.list<Prims.list<Pipeline.cell>>, Pipeline.eval_error> = (fun ( resolve  :  Relational.resolver ) ( src  :  Pipeline.data_source ) -> (match (src) with
| Pipeline.Embedded (f) -> begin
     Pipeline.Ok (f.rows)
     end
| Pipeline.Ref (name) -> begin
     (match ((resolve name)) with
| Pipeline.Ok (f) -> begin
     Pipeline.Ok (f.rows)
     end
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end)
     end))


let rec lnames : Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<Prims.string> = (fun ( on  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (on) with
| [] -> begin
     []
     end
| ((l, uu___))::rest -> begin
     (l)::(lnames rest)
     end))


let rec rnames : Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<Prims.string> = (fun ( on  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (on) with
| [] -> begin
     []
     end
| ((uu___, r))::rest -> begin
     (r)::(rnames rest)
     end))


let rec matches : mprims  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.bool = (fun ( mp  :  mprims ) ( lk  :  Prims.list<Pipeline.cell> ) ( rks  :  Prims.list<Prims.list<Pipeline.cell>> ) -> (match (rks) with
| [] -> begin
     false
     end
| (rk)::rest -> begin
     ((mp.key_eq lk rk) || (matches mp lk rest))
     end))


let verdict : mprims  ->  Pipeline.schema  ->  Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.bool  ->  Prims.list<Pipeline.cell>  ->  Prims.bool = (fun ( mp  :  mprims ) ( cols  :  Pipeline.schema ) ( on  :  Prims.list<(Prims.string * Prims.string)> ) ( rks  :  Prims.list<Prims.list<Pipeline.cell>> ) ( keep  :  Prims.bool ) ( r  :  Prims.list<Pipeline.cell> ) -> (Prims.op_Equals (matches mp (proj_row cols (lnames on) r) rks) keep))

type mres = {r_out : Incremental.walked; r_entries : Prims.list<entry>; r_switched : Prims.bool}


let __proj__Mkmres__item__r_out : mres  ->  Incremental.walked = (fun ( projectee  :  mres ) -> (match (projectee) with
| {r_out = r_out; r_entries = r_entries; r_switched = r_switched} -> begin
     r_out
     end))


let __proj__Mkmres__item__r_entries : mres  ->  Prims.list<entry> = (fun ( projectee  :  mres ) -> (match (projectee) with
| {r_out = r_out; r_entries = r_entries; r_switched = r_switched} -> begin
     r_entries
     end))


let __proj__Mkmres__item__r_switched : mres  ->  Prims.bool = (fun ( projectee  :  mres ) -> (match (projectee) with
| {r_out = r_out; r_entries = r_entries; r_switched = r_switched} -> begin
     r_switched
     end))


let prior_rest_window : Prims.list<entry>  ->  Prims.list<entry> = (fun ( prior  :  Prims.list<entry> ) -> prior)


let rec mwalk : mprims  ->  Relational.rprims  ->  Pipeline.prims  ->  Relational.resolver  ->  Pipeline.param_env  ->  Incremental.reuse_fn  ->  Prims.list<entry>  ->  Pipeline.schema  ->  Incremental.alive  ->  Prims.list<mstep>  ->  Pipeline.outcome<mres, Pipeline.eval_error> = (fun ( mp  :  mprims ) ( rp  :  Relational.rprims ) ( pr  :  Pipeline.prims ) ( resolve  :  Relational.resolver ) ( env  :  Pipeline.param_env ) ( reuse  :  Incremental.reuse_fn ) ( prior  :  Prims.list<entry> ) ( cols  :  Pipeline.schema ) ( a  :  Incremental.alive ) ( p  :  Prims.list<mstep> ) -> (match (p) with
| [] -> begin
     Pipeline.Ok ({r_out = Incremental.W (cols, a); r_entries = []; r_switched = false})
     end
| (MFilter (pred))::rest -> begin
     (match ((step_cells_by (Pipeline.len cols) (expr_fn pr env cols pred) reuse (prior_head prior).e_cells a)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (cs) -> begin
     (match ((mwalk mp rp pr resolve env reuse (prior_rest prior) cols (Incremental.keep_true a cs) rest)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (st) -> begin
     Pipeline.Ok ({r_out = st.r_out; r_entries = ({e_cells = cs; e_type = FStar_Pervasives_Native.None; e_keys = FStar_Pervasives_Native.None})::st.r_entries; r_switched = st.r_switched})
     end)
     end)
     end
| (MDerive (name, x))::rest -> begin
     (match ((Pipeline.derived_typing_in env cols x)) with
| Pipeline.Refused -> begin
     Pipeline.Error ((Pipeline.float_beside_decimal name))
     end
| dt -> begin
     (match ((step_cells_by (Pipeline.len cols) (expr_fn pr env cols x) reuse (prior_head prior).e_cells a)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (cs) -> begin
     (match ((Pipeline.column_type_by dt name (Incremental.cells_of cs))) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (ty) -> begin
     (

let i = (Pipeline.index_of name cols)
in (

let same = (Prims.op_Equals (prior_head prior).e_type (FStar_Pervasives_Native.Some (ty)))
in (

let reuse' =  
if same then begin
     reuse
     end else begin
     Incremental.no_reuse
     end
in (match ((mwalk mp rp pr resolve env reuse' (prior_rest prior) (Incremental.ext_schema i name ty cols) (Incremental.extend i a cs) rest)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (st) -> begin
     Pipeline.Ok ({r_out = st.r_out; r_entries = ({e_cells = cs; e_type = FStar_Pervasives_Native.Some (ty); e_keys = FStar_Pervasives_Native.None})::st.r_entries; r_switched = (st.r_switched || (not (same)))})
     end))))
     end)
     end)
     end)
     end
| (MProject (pairs))::rest -> begin
     (match ((Relational.project_step cols pairs)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (out) -> begin
     (mwalk mp rp pr resolve env reuse prior out (proj_alive cols (Pipeline.names pairs) a) rest)
     end)
     end
| (MLimit (n, off))::rest -> begin
     (mwalk mp rp pr resolve env reuse prior cols (slice off n a) rest)
     end
| (MWindow (spec))::rest -> begin
     (match ((Relational.window_cols rp cols spec)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (out) -> begin
     (match ((mp.win_cells cols spec (Incremental.rows_of a))) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (ws) -> begin
     (

let cs = (pair_cells a ws)
in (match ((mwalk mp rp pr resolve env Incremental.no_reuse (prior_rest_window prior) out (Incremental.extend FStar_Pervasives_Native.None a cs) rest)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (st) -> begin
     Pipeline.Ok ({r_out = st.r_out; r_entries = st.r_entries; r_switched = true})
     end))
     end)
     end)
     end
| (MJoin (src, on, keep))::rest -> begin
     (match ((Relational.source_cols resolve src)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (rcols) -> begin
     (match ((Relational.join_cols rp cols rcols on ( 
if keep then begin
     Pipeline.Semi
     end else begin
     Pipeline.Anti
     end))) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (uu___) -> begin
     (match ((right_rows resolve src)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (rrows) -> begin
     (

let rks = (proj_rows rcols (rnames on) rrows)
in (

let reuse' =  
if (Prims.op_Equals (prior_head prior).e_keys (FStar_Pervasives_Native.Some (rks))) then begin
     reuse
     end else begin
     Incremental.no_reuse
     end
in (

let vf = (verdict mp cols on rks keep)
in (

let vs = (match ((step_cells_by (Pipeline.len cols) (bool_fn (Pipeline.len cols) vf) reuse' (prior_head prior).e_cells a)) with
| Pipeline.Ok (_0) -> begin
     _0
     end)
in (match ((mwalk mp rp pr resolve env reuse (prior_rest prior) cols (Incremental.keep_true a vs) rest)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (st) -> begin
     Pipeline.Ok ({r_out = st.r_out; r_entries = ({e_cells = vs; e_type = FStar_Pervasives_Native.None; e_keys = FStar_Pervasives_Native.Some (rks)})::st.r_entries; r_switched = st.r_switched})
     end)))))
     end)
     end)
     end)
     end))

type mstate = {m_out : Incremental.walked; m_entries : Prims.list<entry>; m_groups : Prims.list<pgroup>; m_tail : Prims.list<entry>}


let __proj__Mkmstate__item__m_out : mstate  ->  Incremental.walked = (fun ( projectee  :  mstate ) -> (match (projectee) with
| {m_out = m_out; m_entries = m_entries; m_groups = m_groups; m_tail = m_tail} -> begin
     m_out
     end))


let __proj__Mkmstate__item__m_entries : mstate  ->  Prims.list<entry> = (fun ( projectee  :  mstate ) -> (match (projectee) with
| {m_out = m_out; m_entries = m_entries; m_groups = m_groups; m_tail = m_tail} -> begin
     m_entries
     end))


let __proj__Mkmstate__item__m_groups : mstate  ->  Prims.list<pgroup> = (fun ( projectee  :  mstate ) -> (match (projectee) with
| {m_out = m_out; m_entries = m_entries; m_groups = m_groups; m_tail = m_tail} -> begin
     m_groups
     end))


let __proj__Mkmstate__item__m_tail : mstate  ->  Prims.list<entry> = (fun ( projectee  :  mstate ) -> (match (projectee) with
| {m_out = m_out; m_entries = m_entries; m_groups = m_groups; m_tail = m_tail} -> begin
     m_tail
     end))


let empty_state : Pipeline.schema  ->  mstate = (fun ( cols  :  Pipeline.schema ) -> {m_out = Incremental.W (cols, []); m_entries = []; m_groups = []; m_tail = []})


let mrun : mprims  ->  Relational.rprims  ->  Pipeline.prims  ->  Relational.resolver  ->  Pipeline.param_env  ->  Incremental.reuse_fn  ->  mstate  ->  Pipeline.schema  ->  Incremental.alive  ->  mpipe  ->  Pipeline.outcome<mstate, Pipeline.eval_error> = (fun ( mp  :  mprims ) ( rp  :  Relational.rprims ) ( pr  :  Pipeline.prims ) ( resolve  :  Relational.resolver ) ( env  :  Pipeline.param_env ) ( reuse  :  Incremental.reuse_fn ) ( prior  :  mstate ) ( cols  :  Pipeline.schema ) ( a  :  Incremental.alive ) ( p  :  mpipe ) -> (match ((mwalk mp rp pr resolve env reuse prior.m_entries cols a p.prefix)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (st) -> begin
     (match (p.grouped) with
| FStar_Pervasives_Native.None -> begin
     Pipeline.Ok ({m_out = st.r_out; m_entries = st.r_entries; m_groups = []; m_tail = []})
     end
| FStar_Pervasives_Native.Some (keys, aggs, tail) -> begin
     (

let uu___ = st.r_out
in (match (uu___) with
| Incremental.W (cols1, a1) -> begin
     (

let reuse1 =  
if st.r_switched then begin
     Incremental.no_reuse
     end else begin
     reuse
     end
in (match ((group_step mp reuse1 prior.m_groups cols1 keys aggs a1)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (gr) -> begin
     (

let uu___1 = gr.gs_out
in (match (uu___1) with
| Incremental.W (gcols, ga) -> begin
     (

let os = gr.gs_os
in (match ((mwalk mp rp pr resolve env (tail_reuse mp os) prior.m_tail gcols ga tail)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (ts) -> begin
     Pipeline.Ok ({m_out = ts.r_out; m_entries = st.r_entries; m_groups = (pgroups_of os); m_tail = ts.r_entries})
     end))
     end))
     end))
     end))
     end)
     end))


let mprime : mprims  ->  Relational.rprims  ->  Pipeline.prims  ->  Relational.resolver  ->  Pipeline.param_env  ->  Pipeline.schema  ->  Incremental.alive  ->  mpipe  ->  Pipeline.outcome<mstate, Pipeline.eval_error> = (fun ( mp  :  mprims ) ( rp  :  Relational.rprims ) ( pr  :  Pipeline.prims ) ( resolve  :  Relational.resolver ) ( env  :  Pipeline.param_env ) ( cols  :  Pipeline.schema ) ( a  :  Incremental.alive ) ( p  :  mpipe ) -> (mrun mp rp pr resolve env Incremental.no_reuse (empty_state cols) cols a p))


let mrefresh : mprims  ->  Relational.rprims  ->  Pipeline.prims  ->  Relational.resolver  ->  Pipeline.param_env  ->  mstate  ->  FStar_Pervasives_Native.option<Prims.list<Incremental.token>>  ->  Pipeline.schema  ->  Incremental.alive  ->  mpipe  ->  Pipeline.outcome<mstate, Pipeline.eval_error> = (fun ( mp  :  mprims ) ( rp  :  Relational.rprims ) ( pr  :  Pipeline.prims ) ( resolve  :  Relational.resolver ) ( env  :  Pipeline.param_env ) ( st  :  mstate ) ( named  :  FStar_Pervasives_Native.option<Prims.list<Incremental.token>> ) ( cols  :  Pipeline.schema ) ( a  :  Incremental.alive ) ( p  :  mpipe ) -> (mrun mp rp pr resolve env (Incremental.reuse_of named) st cols a p))


let rec untok : Prims.list<Prims.list<Pipeline.cell>>  ->  Incremental.alive = (fun ( rows  :  Prims.list<Prims.list<Pipeline.cell>> ) -> (match (rows) with
| [] -> begin
     []
     end
| (r)::rest -> begin
     (((""), (r)))::(untok rest)
     end))


let fit : Prims.nat  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Pipeline.outcome<Prims.list<Prims.list<Pipeline.cell>>, Pipeline.eval_error> = (fun ( n  :  Prims.nat ) ( rs  :  Prims.list<Prims.list<Pipeline.cell>> ) ->  
if (Pipeline.all_width n rs) then begin
     Pipeline.Ok (rs)
     end else begin
     Pipeline.Error (Pipeline.TypeError ("the row function\'s rows are not as wide as the schema"))
     end)


let mrows : mprims  ->  Relational.resolver  ->  Relational.row_fn = (fun ( mp  :  mprims ) ( resolve  :  Relational.resolver ) ( f  :  Pipeline.wframe ) ( t  :  Pipeline.transform ) ( out  :  Pipeline.schema ) -> (match (t) with
| Pipeline.Project (pairs) -> begin
     (fit (Pipeline.len out) (proj_rows f.cols (Pipeline.names pairs) f.rows))
     end
| Pipeline.GroupBy (keys, aggs) -> begin
     (match ((groups_out mp Incremental.no_reuse [] f.cols aggs (groups_of f.cols keys (untok f.rows)))) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (os) -> begin
     (fit (Pipeline.len out) (grows os))
     end)
     end
| Pipeline.Window (spec) -> begin
     (match ((mp.win_cells f.cols spec f.rows)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (ws) -> begin
     (fit (Pipeline.len out) (Pipeline.zip_append f.rows ws))
     end)
     end
| Pipeline.Limit (Pipeline.SlotLit (n), Pipeline.SlotLit (off)) -> begin
     (fit (Pipeline.len out) (slice off n f.rows))
     end
| Pipeline.Join (src, on, Pipeline.Semi) -> begin
     (match ((((right_rows resolve src)), ((Relational.source_cols resolve src)))) with
| (Pipeline.Ok (rrows), Pipeline.Ok (rcols)) -> begin
     (fit (Pipeline.len out) (keep_rows_by (verdict mp f.cols on (proj_rows rcols (rnames on) rrows) true) f.rows))
     end
| (Pipeline.Error (e), uu___) -> begin
     Pipeline.Error (e)
     end
| (uu___, Pipeline.Error (e)) -> begin
     Pipeline.Error (e)
     end)
     end
| Pipeline.Join (src, on, Pipeline.Anti) -> begin
     (match ((((right_rows resolve src)), ((Relational.source_cols resolve src)))) with
| (Pipeline.Ok (rrows), Pipeline.Ok (rcols)) -> begin
     (fit (Pipeline.len out) (keep_rows_by (verdict mp f.cols on (proj_rows rcols (rnames on) rrows) false) f.rows))
     end
| (Pipeline.Error (e), uu___) -> begin
     Pipeline.Error (e)
     end
| (uu___, Pipeline.Error (e)) -> begin
     Pipeline.Error (e)
     end)
     end
| uu___ -> begin
     Pipeline.Error (Pipeline.TypeError ("a verb this model does not reach"))
     end))


let mother : mprims  ->  Relational.rprims  ->  Relational.resolver  ->  Pipeline.param_env  ->  Pipeline.other_fn = (fun ( mp  :  mprims ) ( rp  :  Relational.rprims ) ( resolve  :  Relational.resolver ) ( env  :  Pipeline.param_env ) -> (Relational.other_of rp resolve env (mrows mp resolve)))


let rec retok_groups : Prims.list<group>  ->  Prims.list<group> = (fun ( gs  :  Prims.list<group> ) -> (match (gs) with
| [] -> begin
     []
     end
| (g)::rest -> begin
     ({g_key = g.g_key; g_members = (untok (Incremental.rows_of g.g_members))})::(retok_groups rest)
     end))


let entries_of : Pipeline.outcome<mres, Pipeline.eval_error>  ->  Prims.list<entry> = (fun ( r  :  Pipeline.outcome<mres, Pipeline.eval_error> ) -> (match (r) with
| Pipeline.Ok (st) -> begin
     st.r_entries
     end
| Pipeline.Error (uu___) -> begin
     []
     end))


let rec sub_a : Incremental.alive  ->  Incremental.alive  ->  Prims.bool = (fun ( ms  :  Incremental.alive ) ( a  :  Incremental.alive ) -> (match (ms) with
| [] -> begin
     true
     end
| (m)::rest -> begin
     ((mem_a m a) && (sub_a rest a))
     end))


let rec members_in : Incremental.alive  ->  Prims.list<group>  ->  Prims.bool = (fun ( a  :  Incremental.alive ) ( gs  :  Prims.list<group> ) -> (match (gs) with
| [] -> begin
     true
     end
| (g)::rest -> begin
     ((sub_a g.g_members a) && (members_in a rest))
     end))


let rec suffix_of : Incremental.alive  ->  Incremental.alive  ->  Prims.bool = (fun ( ms  :  Incremental.alive ) ( a  :  Incremental.alive ) -> ((Prims.op_Equals ms a) || (match (a) with
| [] -> begin
     false
     end
| (uu___)::rest -> begin
     (suffix_of ms rest)
     end)))


let rec find_group : Prims.list<Pipeline.cell>  ->  Prims.list<group>  ->  FStar_Pervasives_Native.option<group> = (fun ( key  :  Prims.list<Pipeline.cell> ) ( gs  :  Prims.list<group> ) -> (match (gs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (g)::rest -> begin
      
if (Prims.op_Equals g.g_key key) then begin
     FStar_Pervasives_Native.Some (g)
     end else begin
     (find_group key rest)
     end
     end))


let rec eqv : Prims.list<gout>  ->  Prims.list<gout>  ->  Prims.bool = (fun ( os  :  Prims.list<gout> ) ( os'  :  Prims.list<gout> ) -> (match (((os), (os'))) with
| ([], []) -> begin
     true
     end
| ((o)::r, (o')::r') -> begin
     ((((Prims.op_Equals o.o_key o'.o_key) && (Prims.op_Equals o.o_tokens o'.o_tokens)) && (Prims.op_Equals o.o_aggs o'.o_aggs)) && (eqv r r'))
     end
| uu___ -> begin
     false
     end))


let rec mem_key : Prims.list<Pipeline.cell>  ->  Prims.list<group>  ->  Prims.bool = (fun ( k  :  Prims.list<Pipeline.cell> ) ( gs  :  Prims.list<group> ) -> (match (gs) with
| [] -> begin
     false
     end
| (g)::rest -> begin
     ((Prims.op_Equals g.g_key k) || (mem_key k rest))
     end))


let rec nodup_keys : Prims.list<group>  ->  Prims.bool = (fun ( gs  :  Prims.list<group> ) -> (match (gs) with
| [] -> begin
     true
     end
| (g)::rest -> begin
     ((not ((mem_key g.g_key rest))) && (nodup_keys rest))
     end))


let rec mem_okey : Prims.list<Pipeline.cell>  ->  Prims.list<gout>  ->  Prims.bool = (fun ( k  :  Prims.list<Pipeline.cell> ) ( os  :  Prims.list<gout> ) -> (match (os) with
| [] -> begin
     false
     end
| (o)::rest -> begin
     ((Prims.op_Equals o.o_key k) || (mem_okey k rest))
     end))


let rec nodup_okeys : Prims.list<gout>  ->  Prims.bool = (fun ( os  :  Prims.list<gout> ) -> (match (os) with
| [] -> begin
     true
     end
| (o)::rest -> begin
     ((not ((mem_okey o.o_key rest))) && (nodup_okeys rest))
     end))


let rec mem_gout : gout  ->  Prims.list<gout>  ->  Prims.bool = (fun ( o  :  gout ) ( os  :  Prims.list<gout> ) -> (match (os) with
| [] -> begin
     false
     end
| (o')::rest -> begin
     ((Prims.op_Equals o' o) || (mem_gout o rest))
     end))




