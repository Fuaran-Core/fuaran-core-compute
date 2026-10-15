module Incremental
type wstep =
| WFilter of Pipeline.col_expr
| WDerive of Prims.string * Pipeline.col_expr


let uu___is_WFilter : wstep  ->  Prims.bool = (fun ( projectee  :  wstep ) -> (match (projectee) with
| WFilter (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__WFilter__item___0 : wstep  ->  Pipeline.col_expr = (fun ( projectee  :  wstep ) -> (match (projectee) with
| WFilter (_0) -> begin
     _0
     end))


let uu___is_WDerive : wstep  ->  Prims.bool = (fun ( projectee  :  wstep ) -> (match (projectee) with
| WDerive (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__WDerive__item___0 : wstep  ->  Prims.string = (fun ( projectee  :  wstep ) -> (match (projectee) with
| WDerive (_0, _1) -> begin
     _0
     end))


let __proj__WDerive__item___1 : wstep  ->  Pipeline.col_expr = (fun ( projectee  :  wstep ) -> (match (projectee) with
| WDerive (_0, _1) -> begin
     _1
     end))


let embed_step : wstep  ->  Pipeline.transform = (fun ( s  :  wstep ) -> (match (s) with
| WFilter (pred) -> begin
     Pipeline.Filter (pred)
     end
| WDerive (name, x) -> begin
     Pipeline.Derive (name, x)
     end))


let rec embed : Prims.list<wstep>  ->  Prims.list<Pipeline.transform> = (fun ( p  :  Prims.list<wstep> ) -> (match (p) with
| [] -> begin
     []
     end
| (s)::rest -> begin
     ((embed_step s))::(embed rest)
     end))


type token = Prims.string


type alive = Prims.list<(token * Prims.list<Pipeline.cell>)>


let rec awidth : Prims.nat  ->  alive  ->  Prims.bool = (fun ( n  :  Prims.nat ) ( a  :  alive ) -> (match (a) with
| [] -> begin
     true
     end
| ((uu___, r))::rest -> begin
     ((Prims.op_Equals (Pipeline.len r) n) && (awidth n rest))
     end))


let rec rows_of : alive  ->  Prims.list<Prims.list<Pipeline.cell>> = (fun ( a  :  alive ) -> (match (a) with
| [] -> begin
     []
     end
| ((uu___, r))::rest -> begin
     (r)::(rows_of rest)
     end))


let frame_of : Pipeline.schema  ->  alive  ->  Pipeline.wframe = (fun ( cols  :  Pipeline.schema ) ( a  :  alive ) -> {Pipeline.cols = cols; Pipeline.rows = (rows_of a)})


type step_cache = Prims.list<(token * Pipeline.cell)>


let rec cells_of : step_cache  ->  Prims.list<Pipeline.cell> = (fun ( c  :  step_cache ) -> (match (c) with
| [] -> begin
     []
     end
| ((uu___, v))::rest -> begin
     (v)::(cells_of rest)
     end))


type reuse_fn = token  ->  Prims.bool


let no_reuse : reuse_fn = (fun ( uu___  :  token ) -> false)


let rec mem_token : token  ->  Prims.list<token>  ->  Prims.bool = (fun ( t  :  token ) ( l  :  Prims.list<token> ) -> (match (l) with
| [] -> begin
     false
     end
| (h)::rest -> begin
     ((Prims.op_Equals h t) || (mem_token t rest))
     end))


let reuse_of : FStar_Pervasives_Native.option<Prims.list<token>>  ->  reuse_fn = (fun ( named  :  FStar_Pervasives_Native.option<Prims.list<token>> ) ( t  :  token ) -> (match (named) with
| FStar_Pervasives_Native.None -> begin
     false
     end
| FStar_Pervasives_Native.Some (ns) -> begin
     (not ((mem_token t ns)))
     end))


let rec step_cells : Pipeline.prims  ->  Pipeline.param_env  ->  Pipeline.schema  ->  Pipeline.col_expr  ->  reuse_fn  ->  step_cache  ->  alive  ->  Pipeline.outcome<step_cache, Pipeline.eval_error> = (fun ( pr  :  Pipeline.prims ) ( env  :  Pipeline.param_env ) ( cols  :  Pipeline.schema ) ( x  :  Pipeline.col_expr ) ( reuse  :  reuse_fn ) ( prior  :  step_cache ) ( a  :  alive ) -> (match (a) with
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
     (Pipeline.eval_expr pr env cols r x)
     end)
     end else begin
     (Pipeline.eval_expr pr env cols r x)
     end
in (match (here) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (c) -> begin
     (match ((step_cells pr env cols x reuse prior rest)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (cs) -> begin
     Pipeline.Ok ((((t), (c)))::cs)
     end)
     end))
     end))


let rec keep_true : alive  ->  step_cache  ->  alive = (fun ( a  :  alive ) ( cs  :  step_cache ) -> (match (((a), (cs))) with
| ([], []) -> begin
     []
     end
| (((t, r))::ar, ((uu___, c))::cr) -> begin
     (match (c) with
| Pipeline.Bool (true) -> begin
     (((t), (r)))::(keep_true ar cr)
     end
| uu___1 -> begin
     (keep_true ar cr)
     end)
     end))


let ext_width : Prims.nat  ->  FStar_Pervasives_Native.option<Prims.nat>  ->  Prims.nat = (fun ( n  :  Prims.nat ) ( i  :  FStar_Pervasives_Native.option<Prims.nat> ) -> (match (i) with
| FStar_Pervasives_Native.Some (uu___) -> begin
     n
     end
| FStar_Pervasives_Native.None -> begin
     (n + (Prims.parse_int "1"))
     end))


let ext_one : FStar_Pervasives_Native.option<Prims.nat>  ->  Prims.list<Pipeline.cell>  ->  Pipeline.cell  ->  Prims.list<Pipeline.cell> = (fun ( i  :  FStar_Pervasives_Native.option<Prims.nat> ) ( r  :  Prims.list<Pipeline.cell> ) ( v  :  Pipeline.cell ) -> (match (i) with
| FStar_Pervasives_Native.Some (j) -> begin
     (Pipeline.set_at j v r)
     end
| FStar_Pervasives_Native.None -> begin
     (Pipeline.app r ((v)::[]))
     end))


let rec extend : FStar_Pervasives_Native.option<Prims.nat>  ->  alive  ->  step_cache  ->  alive = (fun ( i  :  FStar_Pervasives_Native.option<Prims.nat> ) ( a  :  alive ) ( cs  :  step_cache ) -> (match (((a), (cs))) with
| ([], []) -> begin
     []
     end
| (((t, r))::ar, ((uu___, v))::cr) -> begin
     (((t), ((ext_one i r v))))::(extend i ar cr)
     end))


let ext_schema : FStar_Pervasives_Native.option<Prims.nat>  ->  Prims.string  ->  Pipeline.column_type  ->  Pipeline.schema  ->  Pipeline.schema = (fun ( i  :  FStar_Pervasives_Native.option<Prims.nat> ) ( name  :  Prims.string ) ( ty  :  Pipeline.column_type ) ( cols  :  Pipeline.schema ) -> (match (i) with
| FStar_Pervasives_Native.Some (j) -> begin
     (Pipeline.retype_at j ty cols)
     end
| FStar_Pervasives_Native.None -> begin
     (Pipeline.app cols ((((name), (ty)))::[]))
     end))

type walked =
| W of Pipeline.schema * alive


let uu___is_W : walked  ->  Prims.bool = (fun ( projectee  :  walked ) -> true)


let __proj__W__item__cols : walked  ->  Pipeline.schema = (fun ( projectee  :  walked ) -> (match (projectee) with
| W (cols, a) -> begin
     cols
     end))


let __proj__W__item__a : walked  ->  alive = (fun ( projectee  :  walked ) -> (match (projectee) with
| W (cols, a) -> begin
     a
     end))


let frame_of_walked : walked  ->  Pipeline.wframe = (fun ( w  :  walked ) -> (match (w) with
| W (cols, a) -> begin
     (frame_of cols a)
     end))

type state = {s_out : walked; s_caches : Prims.list<step_cache>}


let __proj__Mkstate__item__s_out : state  ->  walked = (fun ( projectee  :  state ) -> (match (projectee) with
| {s_out = s_out; s_caches = s_caches} -> begin
     s_out
     end))


let __proj__Mkstate__item__s_caches : state  ->  Prims.list<step_cache> = (fun ( projectee  :  state ) -> (match (projectee) with
| {s_out = s_out; s_caches = s_caches} -> begin
     s_caches
     end))


let rec walk : Pipeline.prims  ->  Pipeline.param_env  ->  reuse_fn  ->  Prims.list<step_cache>  ->  Pipeline.schema  ->  alive  ->  Prims.list<wstep>  ->  Pipeline.outcome<state, Pipeline.eval_error> = (fun ( pr  :  Pipeline.prims ) ( env  :  Pipeline.param_env ) ( reuse  :  reuse_fn ) ( prior  :  Prims.list<step_cache> ) ( cols  :  Pipeline.schema ) ( a  :  alive ) ( p  :  Prims.list<wstep> ) -> (match (p) with
| [] -> begin
     Pipeline.Ok ({s_out = W (cols, a); s_caches = []})
     end
| (s)::rest -> begin
     (

let prior_k = (match (prior) with
| [] -> begin
     []
     end
| (c)::uu___ -> begin
     c
     end)
in (

let prior_rest = (match (prior) with
| [] -> begin
     []
     end
| (uu___)::cr -> begin
     cr
     end)
in (match (s) with
| WFilter (pred) -> begin
     (match ((step_cells pr env cols pred reuse prior_k a)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (cs) -> begin
     (match ((walk pr env reuse prior_rest cols (keep_true a cs) rest)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (st) -> begin
     Pipeline.Ok ({s_out = st.s_out; s_caches = (cs)::st.s_caches})
     end)
     end)
     end
| WDerive (name, x) -> begin
     (match ((Pipeline.derived_typing_in env cols x)) with
| Pipeline.Refused -> begin
     Pipeline.Error ((Pipeline.float_beside_decimal name))
     end
| dt -> begin
     (match ((step_cells pr env cols x reuse prior_k a)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (cs) -> begin
     (match ((Pipeline.column_type_by dt name (cells_of cs))) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (ty) -> begin
     (

let i = (Pipeline.index_of name cols)
in (match ((walk pr env reuse prior_rest (ext_schema i name ty cols) (extend i a cs) rest)) with
| Pipeline.Error (err) -> begin
     Pipeline.Error (err)
     end
| Pipeline.Ok (st) -> begin
     Pipeline.Ok ({s_out = st.s_out; s_caches = (cs)::st.s_caches})
     end))
     end)
     end)
     end)
     end)))
     end))


let prime : Pipeline.prims  ->  Pipeline.param_env  ->  Pipeline.schema  ->  alive  ->  Prims.list<wstep>  ->  Pipeline.outcome<state, Pipeline.eval_error> = (fun ( pr  :  Pipeline.prims ) ( env  :  Pipeline.param_env ) ( cols  :  Pipeline.schema ) ( a  :  alive ) ( p  :  Prims.list<wstep> ) -> (walk pr env no_reuse [] cols a p))


let refresh : Pipeline.prims  ->  Pipeline.param_env  ->  state  ->  FStar_Pervasives_Native.option<Prims.list<token>>  ->  Pipeline.schema  ->  alive  ->  Prims.list<wstep>  ->  Pipeline.outcome<state, Pipeline.eval_error> = (fun ( pr  :  Pipeline.prims ) ( env  :  Pipeline.param_env ) ( st  :  state ) ( named  :  FStar_Pervasives_Native.option<Prims.list<token>> ) ( cols  :  Pipeline.schema ) ( a  :  alive ) ( p  :  Prims.list<wstep> ) -> (walk pr env (reuse_of named) st.s_caches cols a p))


let rec unchanged : reuse_fn  ->  alive  ->  alive  ->  Prims.bool = (fun ( reuse  :  reuse_fn ) ( a  :  alive ) ( a0  :  alive ) -> (match (a) with
| [] -> begin
     true
     end
| ((t, r))::rest -> begin
     (((not ((reuse t))) || (match ((Pipeline.assoc t a0)) with
| FStar_Pervasives_Native.Some (r0) -> begin
     (Prims.op_Equals r0 r)
     end
| FStar_Pervasives_Native.None -> begin
     false
     end)) && (unchanged reuse rest a0))
     end))




