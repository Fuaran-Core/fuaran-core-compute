module Relational

let rec has : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( n  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     false
     end
| (h)::t -> begin
     ((Prims.op_Equals h n) || (has n t))
     end))


let rec no_dups : Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (h)::t -> begin
     ((not ((has h t))) && (no_dups t))
     end))


let rec snds = (fun ( l  :  Prims.list<('a * 'b)> ) -> (match (l) with
| [] -> begin
     []
     end
| ((uu___, y))::t -> begin
     (y)::(snds t)
     end))


let rec first_unknown : Pipeline.schema  ->  Prims.list<Prims.string>  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( cols  :  Pipeline.schema ) ( ns  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (n)::t -> begin
      
if (match ((Pipeline.index_of n cols)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     FStar_Pervasives_Native.Some (n)
     end else begin
     (first_unknown cols t)
     end
     end))


let rec lookup_cols : Pipeline.schema  ->  Prims.list<Prims.string>  ->  Pipeline.schema = (fun ( cols  :  Pipeline.schema ) ( ns  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     (match ((Pipeline.assoc n cols)) with
| FStar_Pervasives_Native.Some (ty) -> begin
     (((n), (ty)))::(lookup_cols cols t)
     end
| FStar_Pervasives_Native.None -> begin
     (lookup_cols cols t)
     end)
     end))


let type_tag : Pipeline.column_type  ->  Prims.string = (fun ( t  :  Pipeline.column_type ) -> (match (t) with
| Pipeline.IntType -> begin
     "int"
     end
| Pipeline.FloatType -> begin
     "float"
     end
| Pipeline.BoolType -> begin
     "bool"
     end
| Pipeline.StringType -> begin
     "string"
     end
| Pipeline.DateType -> begin
     "date"
     end
| Pipeline.TimestampType -> begin
     "timestamp"
     end
| Pipeline.DecimalType -> begin
     "decimal"
     end))


let cell_shape : Pipeline.cell  ->  Prims.string = (fun ( c  :  Pipeline.cell ) -> (match ((Pipeline.type_of c)) with
| FStar_Pervasives_Native.Some (t) -> begin
     (type_tag t)
     end
| FStar_Pervasives_Native.None -> begin
     "null"
     end))


let join_column_type : Pipeline.column_type  ->  Pipeline.column_type  ->  FStar_Pervasives_Native.option<Pipeline.column_type> = (fun ( a  :  Pipeline.column_type ) ( b  :  Pipeline.column_type ) ->  
if (Pipeline.widens a b) then begin
     FStar_Pervasives_Native.Some (b)
     end else begin
      
if (Pipeline.widens b a) then begin
     FStar_Pervasives_Native.Some (a)
     end else begin
     FStar_Pervasives_Native.None
     end
     end)


let types_beside_each_other : Prims.string  ->  Pipeline.column_type  ->  Pipeline.column_type  ->  Pipeline.eval_error = (fun ( column  :  Prims.string ) ( a  :  Pipeline.column_type ) ( b  :  Pipeline.column_type ) -> Pipeline.TypeError ((Prims.strcat "derived column \'" (Prims.strcat column (Prims.strcat "\' holds a " (Prims.strcat (type_tag a) (Prims.strcat " beside a " (Prims.strcat (type_tag b) ", which no column type holds together: cast one to the other\'s type first"))))))))


let agg_type : Pipeline.agg_fn  ->  Pipeline.column_type  ->  Pipeline.column_type = (fun ( fn  :  Pipeline.agg_fn ) ( src  :  Pipeline.column_type ) -> (match (fn) with
| Pipeline.Count -> begin
     Pipeline.IntType
     end
| Pipeline.CountDistinct -> begin
     Pipeline.IntType
     end
| Pipeline.Mean -> begin
     Pipeline.FloatType
     end
| Pipeline.Median -> begin
     Pipeline.FloatType
     end
| Pipeline.StdDev -> begin
     Pipeline.FloatType
     end
| Pipeline.Sum -> begin
     src
     end
| Pipeline.Min -> begin
     src
     end
| Pipeline.Max -> begin
     src
     end
| Pipeline.First -> begin
     src
     end
| Pipeline.Last -> begin
     src
     end))


let rec join_fold : FStar_Pervasives_Native.option<FStar_Pervasives_Native.option<Pipeline.column_type>>  ->  Prims.list<Pipeline.column_type>  ->  FStar_Pervasives_Native.option<FStar_Pervasives_Native.option<Pipeline.column_type>> = (fun ( acc  :  FStar_Pervasives_Native.option<FStar_Pervasives_Native.option<Pipeline.column_type>> ) ( types  :  Prims.list<Pipeline.column_type> ) -> (match (types) with
| [] -> begin
     acc
     end
| (t)::rest -> begin
     (

let acc' = (match (acc) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some (FStar_Pervasives_Native.Some (t))
     end
| FStar_Pervasives_Native.Some (FStar_Pervasives_Native.Some (a)) -> begin
     FStar_Pervasives_Native.Some ((join_column_type a t))
     end
| FStar_Pervasives_Native.Some (FStar_Pervasives_Native.None) -> begin
     FStar_Pervasives_Native.Some (FStar_Pervasives_Native.None)
     end)
in (join_fold acc' rest))
     end))


let rec has_ty : Pipeline.column_type  ->  Prims.list<Pipeline.column_type>  ->  Prims.bool = (fun ( ty  :  Pipeline.column_type ) ( types  :  Prims.list<Pipeline.column_type> ) -> (match (types) with
| [] -> begin
     false
     end
| (t)::rest -> begin
     ((Prims.op_Equals t ty) || (has_ty ty rest))
     end))


let unpivot_typing : Prims.list<Pipeline.column_type>  ->  Pipeline.derived_typing = (fun ( types  :  Prims.list<Pipeline.column_type> ) ->  
if ((has_ty Pipeline.FloatType types) && (has_ty Pipeline.DecimalType types)) then begin
     Pipeline.Refused
     end else begin
     (match ((join_fold FStar_Pervasives_Native.None types)) with
| FStar_Pervasives_Native.None -> begin
     Pipeline.Decided (Pipeline.StringType)
     end
| FStar_Pervasives_Native.Some (FStar_Pervasives_Native.Some (ty)) -> begin
     Pipeline.Decided (ty)
     end
| FStar_Pervasives_Native.Some (FStar_Pervasives_Native.None) -> begin
     Pipeline.ByCells
     end)
     end)

type rprims = {cell_string : Pipeline.cell  ->  Prims.string; int_text : Prims.int  ->  Prims.string; string_lt : Prims.string  ->  Prims.string  ->  Prims.bool; candidate : Prims.string  ->  Prims.int  ->  Prims.string}


let __proj__Mkrprims__item__cell_string : rprims  ->  Pipeline.cell  ->  Prims.string = (fun ( projectee  :  rprims ) -> (match (projectee) with
| {cell_string = cell_string; int_text = int_text; string_lt = string_lt; candidate = candidate} -> begin
     cell_string
     end))


let __proj__Mkrprims__item__int_text : rprims  ->  Prims.int  ->  Prims.string = (fun ( projectee  :  rprims ) -> (match (projectee) with
| {cell_string = cell_string; int_text = int_text; string_lt = string_lt; candidate = candidate} -> begin
     int_text
     end))


let __proj__Mkrprims__item__string_lt : rprims  ->  Prims.string  ->  Prims.string  ->  Prims.bool = (fun ( projectee  :  rprims ) -> (match (projectee) with
| {cell_string = cell_string; int_text = int_text; string_lt = string_lt; candidate = candidate} -> begin
     string_lt
     end))


let __proj__Mkrprims__item__candidate : rprims  ->  Prims.string  ->  Prims.int  ->  Prims.string = (fun ( projectee  :  rprims ) -> (match (projectee) with
| {cell_string = cell_string; int_text = int_text; string_lt = string_lt; candidate = candidate} -> begin
     candidate
     end))


type resolver = Prims.string  ->  Pipeline.outcome<Pipeline.wframe, Pipeline.eval_error>


let source_cols : resolver  ->  Pipeline.data_source  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( resolve  :  resolver ) ( src  :  Pipeline.data_source ) -> (match (src) with
| Pipeline.Embedded (f) -> begin
     Pipeline.Ok (f.cols)
     end
| Pipeline.Ref (name) -> begin
     (match ((resolve name)) with
| Pipeline.Ok (f) -> begin
     Pipeline.Ok (f.cols)
     end
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end)
     end))


let resolve_int_slot : Pipeline.param_env  ->  Prims.string  ->  Pipeline.slot<Prims.int>  ->  Pipeline.outcome<Prims.int, Pipeline.eval_error> = (fun ( env  :  Pipeline.param_env ) ( label  :  Prims.string ) ( s  :  Pipeline.slot<Prims.int> ) -> (match (s) with
| Pipeline.SlotLit (v) -> begin
     Pipeline.Ok (v)
     end
| Pipeline.SlotParam (n) -> begin
     (match ((Pipeline.assoc n env)) with
| FStar_Pervasives_Native.None -> begin
     Pipeline.Error (Pipeline.UnboundParam (n, (Pipeline.names env)))
     end
| FStar_Pervasives_Native.Some (Pipeline.Int (v)) -> begin
     Pipeline.Ok (v)
     end
| FStar_Pervasives_Native.Some (other) -> begin
     Pipeline.Error (Pipeline.TypeError ((Prims.strcat label (Prims.strcat ": param \'" (Prims.strcat n (Prims.strcat "\' is bound to a " (Prims.strcat (cell_shape other) " cell, not an integer")))))))
     end)
     end))


let resolve_str_slot : Pipeline.param_env  ->  Prims.string  ->  Pipeline.slot<Prims.string>  ->  Pipeline.outcome<Prims.string, Pipeline.eval_error> = (fun ( env  :  Pipeline.param_env ) ( label  :  Prims.string ) ( s  :  Pipeline.slot<Prims.string> ) -> (match (s) with
| Pipeline.SlotLit (v) -> begin
     Pipeline.Ok (v)
     end
| Pipeline.SlotParam (n) -> begin
     (match ((Pipeline.assoc n env)) with
| FStar_Pervasives_Native.None -> begin
     Pipeline.Error (Pipeline.UnboundParam (n, (Pipeline.names env)))
     end
| FStar_Pervasives_Native.Some (Pipeline.Str (v)) -> begin
     Pipeline.Ok (v)
     end
| FStar_Pervasives_Native.Some (other) -> begin
     Pipeline.Error (Pipeline.TypeError ((Prims.strcat label (Prims.strcat ": param \'" (Prims.strcat n (Prims.strcat "\' is bound to a " (Prims.strcat (cell_shape other) " cell, not a string")))))))
     end)
     end))


let rec resolve_sort_keys : Pipeline.param_env  ->  Prims.list<(Pipeline.slot<Prims.string> * Pipeline.sort_dir)>  ->  Pipeline.outcome<Prims.list<(Prims.string * Pipeline.sort_dir)>, Pipeline.eval_error> = (fun ( env  :  Pipeline.param_env ) ( sk  :  Prims.list<(Pipeline.slot<Prims.string> * Pipeline.sort_dir)> ) -> (match (sk) with
| [] -> begin
     Pipeline.Ok ([])
     end
| ((c, d))::rest -> begin
     (match ((resolve_str_slot env "sort key column" c)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (c') -> begin
     (match ((resolve_sort_keys env rest)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (rest') -> begin
     Pipeline.Ok ((((c'), (d)))::rest')
     end)
     end)
     end))


let repeat_text : Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.string  ->  Prims.string = (fun ( verb  :  Prims.string ) ( name  :  Prims.string ) ( first  :  Prims.string ) ( who  :  Prims.string ) -> (Prims.strcat verb (Prims.strcat " output \'" (Prims.strcat name (Prims.strcat "\' is named twice, by " (Prims.strcat first (Prims.strcat " and by " (Prims.strcat who "; give each output a name of its own"))))))))


let rec repeat_in : Prims.string  ->  Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<(Prims.string * Prims.string)>  ->  FStar_Pervasives_Native.option<Pipeline.eval_error> = (fun ( verb  :  Prims.string ) ( seen  :  Prims.list<(Prims.string * Prims.string)> ) ( outs  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (outs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, who))::t -> begin
     (match ((Pipeline.assoc n seen)) with
| FStar_Pervasives_Native.Some (first) -> begin
     FStar_Pervasives_Native.Some (Pipeline.TypeError ((repeat_text verb n first who)))
     end
| FStar_Pervasives_Native.None -> begin
     (repeat_in verb ((((n), (who)))::seen) t)
     end)
     end))


let repeated_output : Prims.string  ->  Prims.list<(Prims.string * Prims.string)>  ->  FStar_Pervasives_Native.option<Pipeline.eval_error> = (fun ( verb  :  Prims.string ) ( outs  :  Prims.list<(Prims.string * Prims.string)> ) -> (repeat_in verb [] outs))


let rec project_outs : Prims.list<(Prims.string * Prims.string)>  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( pairs  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (pairs) with
| [] -> begin
     []
     end
| ((src, out))::t -> begin
     (((out), ((Prims.strcat "the projection of \'" (Prims.strcat src "\'")))))::(project_outs t)
     end))


let rec key_outs : Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( keys  :  Prims.list<Prims.string> ) -> (match (keys) with
| [] -> begin
     []
     end
| (k)::t -> begin
     (((k), ((Prims.strcat "the key \'" (Prims.strcat k "\'")))))::(key_outs t)
     end))


let rec agg_outs : Prims.list<Pipeline.agg>  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( aggs  :  Prims.list<Pipeline.agg> ) -> (match (aggs) with
| [] -> begin
     []
     end
| (a)::t -> begin
     (((a.a_name), ((Prims.strcat "the aggregate over \'" (Prims.strcat a.a_of "\'")))))::(agg_outs t)
     end))


let rec id_outs : Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( ids  :  Prims.list<Prims.string> ) -> (match (ids) with
| [] -> begin
     []
     end
| (n)::t -> begin
     (((n), ((Prims.strcat "the id column \'" (Prims.strcat n "\'")))))::(id_outs t)
     end))


let unpivot_outs : Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( ids  :  Prims.list<Prims.string> ) -> (Pipeline.app (id_outs ids) (((("variable"), ("the unpivot\'s variable column")))::((("value"), ("the unpivot\'s value column")))::[])))


let rec index_outs : Prims.list<Prims.string>  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( ns  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     (((n), ((Prims.strcat "the index column \'" (Prims.strcat n "\'")))))::(index_outs t)
     end))


let rec value_outs : Pipeline.schema  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( vs  :  Pipeline.schema ) -> (match (vs) with
| [] -> begin
     []
     end
| ((n, uu___))::t -> begin
     (((n), ((Prims.strcat "the column for the on value \'" (Prims.strcat n "\'")))))::(value_outs t)
     end))


let rec project_cols : Pipeline.schema  ->  Prims.list<(Prims.string * Prims.string)>  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( cols  :  Pipeline.schema ) ( pairs  :  Prims.list<(Prims.string * Prims.string)> ) -> (match (pairs) with
| [] -> begin
     Pipeline.Ok ([])
     end
| ((src, out))::rest -> begin
     (match ((Pipeline.assoc src cols)) with
| FStar_Pervasives_Native.None -> begin
     Pipeline.Error (Pipeline.UnknownColumn (src, (Pipeline.names cols)))
     end
| FStar_Pervasives_Native.Some (ty) -> begin
     (match ((project_cols cols rest)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (tail) -> begin
     Pipeline.Ok ((((out), (ty)))::tail)
     end)
     end)
     end))


let project_step : Pipeline.schema  ->  Prims.list<(Prims.string * Prims.string)>  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( cols  :  Pipeline.schema ) ( pairs  :  Prims.list<(Prims.string * Prims.string)> ) -> (match ((project_cols cols pairs)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (out) -> begin
     (match ((repeated_output "project" (project_outs pairs))) with
| FStar_Pervasives_Native.Some (e) -> begin
     Pipeline.Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Ok (out)
     end)
     end))


let rec agg_cols : Pipeline.schema  ->  Prims.list<Pipeline.agg>  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( cols  :  Pipeline.schema ) ( aggs  :  Prims.list<Pipeline.agg> ) -> (match (aggs) with
| [] -> begin
     Pipeline.Ok ([])
     end
| (a)::rest -> begin
     (match ((Pipeline.assoc a.a_of cols)) with
| FStar_Pervasives_Native.None -> begin
     Pipeline.Error (Pipeline.UnknownColumn (a.a_of, (Pipeline.names cols)))
     end
| FStar_Pervasives_Native.Some (ty) -> begin
     (match ((agg_cols cols rest)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (tail) -> begin
     Pipeline.Ok ((((a.a_name), ((agg_type a.a_fn ty))))::tail)
     end)
     end)
     end))


let group_by_cols : Pipeline.schema  ->  Prims.list<Prims.string>  ->  Prims.list<Pipeline.agg>  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( cols  :  Pipeline.schema ) ( keys  :  Prims.list<Prims.string> ) ( aggs  :  Prims.list<Pipeline.agg> ) -> (match ((first_unknown cols keys)) with
| FStar_Pervasives_Native.Some (k) -> begin
     Pipeline.Error (Pipeline.UnknownColumn (k, (Pipeline.names cols)))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((agg_cols cols aggs)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (ac) -> begin
     (match ((repeated_output "groupBy" (Pipeline.app (key_outs keys) (agg_outs aggs)))) with
| FStar_Pervasives_Native.Some (e) -> begin
     Pipeline.Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Ok ((Pipeline.app (lookup_cols cols keys) ac))
     end)
     end)
     end))


let join_key_refusal : Pipeline.schema  ->  Pipeline.schema  ->  Prims.list<(Prims.string * Prims.string)>  ->  FStar_Pervasives_Native.option<Pipeline.eval_error> = (fun ( lcols  :  Pipeline.schema ) ( rcols  :  Pipeline.schema ) ( on  :  Prims.list<(Prims.string * Prims.string)> ) -> (match ((first_unknown lcols (Pipeline.names on))) with
| FStar_Pervasives_Native.Some (n) -> begin
     FStar_Pervasives_Native.Some (Pipeline.UnknownColumn (n, (Pipeline.app (Pipeline.names lcols) (Pipeline.names rcols))))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((first_unknown rcols (snds on))) with
| FStar_Pervasives_Native.Some (n) -> begin
     FStar_Pervasives_Native.Some (Pipeline.UnknownColumn (n, (Pipeline.app (Pipeline.names lcols) (Pipeline.names rcols))))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end))


let rec free : rprims  ->  Prims.string  ->  Prims.list<Prims.string>  ->  Prims.nat  ->  Prims.int  ->  Prims.string = (fun ( rp  :  rprims ) ( name  :  Prims.string ) ( taken  :  Prims.list<Prims.string> ) ( fuel  :  Prims.nat ) ( n  :  Prims.int ) -> (

let c = (rp.candidate name n)
in  
if (Prims.op_Equals fuel (Prims.parse_int "0")) then begin
     c
     end else begin
      
if (has c taken) then begin
     (free rp name taken (fuel - (Prims.parse_int "1")) (n + (Prims.parse_int "1")))
     end else begin
     c
     end
     end))


let rec rename_right : rprims  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( rp  :  rprims ) ( lnames  :  Prims.list<Prims.string> ) ( taken  :  Prims.list<Prims.string> ) ( rnames  :  Prims.list<Prims.string> ) -> (match (rnames) with
| [] -> begin
     []
     end
| (r)::rest -> begin
      
if (has r lnames) then begin
     (

let c = (free rp r taken ((Pipeline.len taken) + (Prims.parse_int "1")) (Prims.parse_int "1"))
in (c)::(rename_right rp lnames ((c)::taken) rest))
     end else begin
     (r)::(rename_right rp lnames taken rest)
     end
     end))


let join_right_names : rprims  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( rp  :  rprims ) ( lnames  :  Prims.list<Prims.string> ) ( rnames  :  Prims.list<Prims.string> ) -> (rename_right rp lnames (Pipeline.app lnames rnames) rnames))


let rec renamed : Prims.list<Prims.string>  ->  Pipeline.schema  ->  Pipeline.schema = (fun ( ns  :  Prims.list<Prims.string> ) ( cols  :  Pipeline.schema ) -> (match (((ns), (cols))) with
| ((n)::nt, ((uu___, ty))::ct) -> begin
     (((n), (ty)))::(renamed nt ct)
     end
| (uu___, uu___1) -> begin
     []
     end))


let join_cols : rprims  ->  Pipeline.schema  ->  Pipeline.schema  ->  Prims.list<(Prims.string * Prims.string)>  ->  Pipeline.join_kind  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( rp  :  rprims ) ( lcols  :  Pipeline.schema ) ( rcols  :  Pipeline.schema ) ( on  :  Prims.list<(Prims.string * Prims.string)> ) ( how  :  Pipeline.join_kind ) -> (match ((join_key_refusal lcols rcols on)) with
| FStar_Pervasives_Native.Some (e) -> begin
     Pipeline.Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     (match (how) with
| Pipeline.Semi -> begin
     Pipeline.Ok (lcols)
     end
| Pipeline.Anti -> begin
     Pipeline.Ok (lcols)
     end
| uu___ -> begin
     Pipeline.Ok ((Pipeline.app lcols (renamed (join_right_names rp (Pipeline.names lcols) (Pipeline.names rcols)) rcols)))
     end)
     end))


let window_reads_of : Pipeline.window_fn  ->  Prims.bool = (fun ( fn  :  Pipeline.window_fn ) -> (match (fn) with
| Pipeline.RowNumber -> begin
     false
     end
| Pipeline.Rank -> begin
     false
     end
| Pipeline.DenseRank -> begin
     false
     end
| Pipeline.CompetitionRank -> begin
     false
     end
| Pipeline.NTile (uu___) -> begin
     false
     end
| Pipeline.Lag -> begin
     true
     end
| Pipeline.Lead -> begin
     true
     end
| Pipeline.CumulSum -> begin
     true
     end
| Pipeline.CumulMax -> begin
     true
     end
| Pipeline.CumulMin -> begin
     true
     end
| Pipeline.RollingMean -> begin
     true
     end
| Pipeline.RollingSum -> begin
     true
     end))


let window_type : Pipeline.schema  ->  Pipeline.window_spec  ->  Pipeline.column_type = (fun ( cols  :  Pipeline.schema ) ( spec  :  Pipeline.window_spec ) -> (match (spec.w_fn) with
| Pipeline.RowNumber -> begin
     Pipeline.IntType
     end
| Pipeline.Rank -> begin
     Pipeline.IntType
     end
| Pipeline.DenseRank -> begin
     Pipeline.IntType
     end
| Pipeline.CompetitionRank -> begin
     Pipeline.IntType
     end
| Pipeline.NTile (uu___) -> begin
     Pipeline.IntType
     end
| Pipeline.CumulSum -> begin
      
if (Prims.op_Equals (Pipeline.assoc spec.w_of cols) (FStar_Pervasives_Native.Some (Pipeline.DecimalType))) then begin
     Pipeline.DecimalType
     end else begin
     Pipeline.FloatType
     end
     end
| Pipeline.RollingSum -> begin
      
if (Prims.op_Equals (Pipeline.assoc spec.w_of cols) (FStar_Pervasives_Native.Some (Pipeline.DecimalType))) then begin
     Pipeline.DecimalType
     end else begin
     Pipeline.FloatType
     end
     end
| Pipeline.RollingMean -> begin
     Pipeline.FloatType
     end
| Pipeline.Lag -> begin
     (match ((Pipeline.assoc spec.w_of cols)) with
| FStar_Pervasives_Native.Some (t) -> begin
     t
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.StringType
     end)
     end
| Pipeline.Lead -> begin
     (match ((Pipeline.assoc spec.w_of cols)) with
| FStar_Pervasives_Native.Some (t) -> begin
     t
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.StringType
     end)
     end
| Pipeline.CumulMax -> begin
     (match ((Pipeline.assoc spec.w_of cols)) with
| FStar_Pervasives_Native.Some (t) -> begin
     t
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.StringType
     end)
     end
| Pipeline.CumulMin -> begin
     (match ((Pipeline.assoc spec.w_of cols)) with
| FStar_Pervasives_Native.Some (t) -> begin
     t
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.StringType
     end)
     end))


let window_refusal : rprims  ->  Pipeline.schema  ->  Pipeline.window_spec  ->  FStar_Pervasives_Native.option<Pipeline.eval_error> = (fun ( rp  :  rprims ) ( cols  :  Pipeline.schema ) ( spec  :  Pipeline.window_spec ) -> (

let keys = (fun ( uu___  :  unit ) -> (match ((first_unknown cols (Pipeline.app spec.partition_by (Pipeline.names spec.order_by)))) with
| FStar_Pervasives_Native.Some (n) -> begin
     FStar_Pervasives_Native.Some (Pipeline.UnknownColumn (n, (Pipeline.names cols)))
     end
| FStar_Pervasives_Native.None -> begin
      
if (match ((Pipeline.index_of spec.w_as cols)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___1 -> begin
     false
     end) then begin
     FStar_Pervasives_Native.Some (Pipeline.TypeError ((Prims.strcat "window output \'" (Prims.strcat spec.w_as "\' names a column the input already carries; name the window\'s column something else, or project the existing one away first"))))
     end else begin
     FStar_Pervasives_Native.None
     end
     end))
in (match (spec.w_fn) with
| Pipeline.NTile (b) -> begin
      
if (b < (Prims.parse_int "1")) then begin
     FStar_Pervasives_Native.Some (Pipeline.TypeError ((Prims.strcat "ntile expects at least 1 bucket, got " (rp.int_text b))))
     end else begin
     (keys ())
     end
     end
| fn -> begin
      
if ((window_reads_of fn) && (match ((Pipeline.index_of spec.w_of cols)) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| uu___ -> begin
     false
     end)) then begin
     FStar_Pervasives_Native.Some (Pipeline.UnknownColumn (spec.w_of, (Pipeline.names cols)))
     end else begin
     (keys ())
     end
     end)))


let window_cols : rprims  ->  Pipeline.schema  ->  Pipeline.window_spec  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( rp  :  rprims ) ( cols  :  Pipeline.schema ) ( spec  :  Pipeline.window_spec ) -> (match ((window_refusal rp cols spec)) with
| FStar_Pervasives_Native.Some (e) -> begin
     Pipeline.Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Ok ((Pipeline.app cols ((((spec.w_as), ((window_type cols spec))))::[])))
     end))


let rec at_opt = (fun ( l  :  Prims.list<'a> ) ( j  :  Prims.nat ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (x)::t -> begin
      
if (Prims.op_Equals j (Prims.parse_int "0")) then begin
     FStar_Pervasives_Native.Some (x)
     end else begin
     (at_opt t (j - (Prims.parse_int "1")))
     end
     end))


let rec col_cells : Prims.nat  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.list<Pipeline.cell> = (fun ( i  :  Prims.nat ) ( rows  :  Prims.list<Prims.list<Pipeline.cell>> ) -> (match (rows) with
| [] -> begin
     []
     end
| (r)::t -> begin
     (match ((at_opt r i)) with
| FStar_Pervasives_Native.Some (c) -> begin
     (c)::(col_cells i t)
     end
| FStar_Pervasives_Native.None -> begin
     (Pipeline.Null)::(col_cells i t)
     end)
     end))


let rec present : Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell> = (fun ( cs  :  Prims.list<Pipeline.cell> ) -> (match (cs) with
| [] -> begin
     []
     end
| (Pipeline.Null)::t -> begin
     (present t)
     end
| (c)::t -> begin
     (c)::(present t)
     end))


let rec remove_all : Pipeline.cell  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell> = (fun ( c  :  Pipeline.cell ) ( cs  :  Prims.list<Pipeline.cell> ) -> (match (cs) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (Prims.op_Equals x c) then begin
     (remove_all c t)
     end else begin
     (x)::(remove_all c t)
     end
     end))


let rec distinct : Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell> = (fun ( cs  :  Prims.list<Pipeline.cell> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (c)::(distinct (remove_all c t))
     end))


let rec insert_by : rprims  ->  Pipeline.cell  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell> = (fun ( rp  :  rprims ) ( c  :  Pipeline.cell ) ( sorted  :  Prims.list<Pipeline.cell> ) -> (match (sorted) with
| [] -> begin
     (c)::[]
     end
| (h)::t -> begin
      
if (rp.string_lt (rp.cell_string c) (rp.cell_string h)) then begin
     (c)::(h)::t
     end else begin
     (h)::(insert_by rp c t)
     end
     end))


let rec sort_by : rprims  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell> = (fun ( rp  :  rprims ) ( cs  :  Prims.list<Pipeline.cell> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::t -> begin
     (insert_by rp c (sort_by rp t))
     end))


let on_values : rprims  ->  Prims.nat  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.list<Pipeline.cell> = (fun ( rp  :  rprims ) ( i  :  Prims.nat ) ( rows  :  Prims.list<Prims.list<Pipeline.cell>> ) -> (sort_by rp (distinct (present (col_cells i rows)))))


let rec pivot_value_cols : rprims  ->  Pipeline.column_type  ->  Prims.list<Pipeline.cell>  ->  Pipeline.schema = (fun ( rp  :  rprims ) ( ty  :  Pipeline.column_type ) ( vs  :  Prims.list<Pipeline.cell> ) -> (match (vs) with
| [] -> begin
     []
     end
| (v)::t -> begin
     ((((rp.cell_string v)), (ty)))::(pivot_value_cols rp ty t)
     end))


let pivot_cols : rprims  ->  Pipeline.schema  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Pipeline.pivot_spec  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( rp  :  rprims ) ( cols  :  Pipeline.schema ) ( rows  :  Prims.list<Prims.list<Pipeline.cell>> ) ( spec  :  Pipeline.pivot_spec ) -> (match ((first_unknown cols spec.p_index)) with
| FStar_Pervasives_Native.Some (n) -> begin
     Pipeline.Error (Pipeline.UnknownColumn (n, (Pipeline.names cols)))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((Pipeline.index_of spec.p_on cols)) with
| FStar_Pervasives_Native.None -> begin
     Pipeline.Error (Pipeline.UnknownColumn (spec.p_on, (Pipeline.names cols)))
     end
| FStar_Pervasives_Native.Some (on_i) -> begin
     (match ((Pipeline.assoc spec.p_values cols)) with
| FStar_Pervasives_Native.None -> begin
     Pipeline.Error (Pipeline.UnknownColumn (spec.p_values, (Pipeline.names cols)))
     end
| FStar_Pervasives_Native.Some (vty) -> begin
     (

let vals = (pivot_value_cols rp (agg_type spec.p_agg vty) (on_values rp on_i rows))
in (match ((repeated_output "pivot" (Pipeline.app (index_outs spec.p_index) (value_outs vals)))) with
| FStar_Pervasives_Native.Some (e) -> begin
     Pipeline.Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Ok ((Pipeline.app (lookup_cols cols spec.p_index) vals))
     end))
     end)
     end)
     end))


let rec indexes_of : Pipeline.schema  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.nat> = (fun ( cols  :  Pipeline.schema ) ( ns  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     (match ((Pipeline.index_of n cols)) with
| FStar_Pervasives_Native.Some (i) -> begin
     (i)::(indexes_of cols t)
     end
| FStar_Pervasives_Native.None -> begin
     (indexes_of cols t)
     end)
     end))


let rec cells_at : Prims.list<Pipeline.cell>  ->  Prims.list<Prims.nat>  ->  Prims.list<Pipeline.cell> = (fun ( r  :  Prims.list<Pipeline.cell> ) ( is  :  Prims.list<Prims.nat> ) -> (match (is) with
| [] -> begin
     []
     end
| (i)::t -> begin
     (match ((at_opt r i)) with
| FStar_Pervasives_Native.Some (c) -> begin
     (c)::(cells_at r t)
     end
| FStar_Pervasives_Native.None -> begin
     (Pipeline.Null)::(cells_at r t)
     end)
     end))


let rec melt : Prims.list<Prims.nat>  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.list<Pipeline.cell> = (fun ( is  :  Prims.list<Prims.nat> ) ( rows  :  Prims.list<Prims.list<Pipeline.cell>> ) -> (match (rows) with
| [] -> begin
     []
     end
| (r)::t -> begin
     (Pipeline.app (cells_at r is) (melt is t))
     end))


let unpivot_cols : Pipeline.schema  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( cols  :  Pipeline.schema ) ( rows  :  Prims.list<Prims.list<Pipeline.cell>> ) ( ids  :  Prims.list<Prims.string> ) ( vals  :  Prims.list<Prims.string> ) -> (match ((first_unknown cols ids)) with
| FStar_Pervasives_Native.Some (n) -> begin
     Pipeline.Error (Pipeline.UnknownColumn (n, (Pipeline.names cols)))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((first_unknown cols vals)) with
| FStar_Pervasives_Native.Some (n) -> begin
     Pipeline.Error (Pipeline.UnknownColumn (n, (Pipeline.names cols)))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((repeated_output "unpivot" (unpivot_outs ids))) with
| FStar_Pervasives_Native.Some (e) -> begin
     Pipeline.Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     (

let dt = (unpivot_typing (snds (lookup_cols cols vals)))
in (match (dt) with
| Pipeline.Refused -> begin
     Pipeline.Error ((Pipeline.float_beside_decimal "value"))
     end
| uu___ -> begin
     (match ((Pipeline.column_type_by dt "value" (melt (indexes_of cols vals) rows))) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (vty) -> begin
     Pipeline.Ok ((Pipeline.app (lookup_cols cols ids) (((("variable"), (Pipeline.StringType)))::((("value"), (vty)))::[])))
     end)
     end))
     end)
     end)
     end))


let rec union_types : Pipeline.schema  ->  Pipeline.schema  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( l  :  Pipeline.schema ) ( r  :  Pipeline.schema ) -> (match (((l), (r))) with
| (((n, lt))::lrest, ((uu___, rt))::rrest) -> begin
     (

let ty =  
if (Prims.op_Equals lt rt) then begin
     Pipeline.Ok (lt)
     end else begin
     (match ((join_column_type lt rt)) with
| FStar_Pervasives_Native.Some (j) -> begin
     Pipeline.Ok (j)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Error ((types_beside_each_other n lt rt))
     end)
     end
in (match (ty) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (ty1) -> begin
     (match ((union_types lrest rrest)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (rest) -> begin
     Pipeline.Ok ((((n), (ty1)))::rest)
     end)
     end))
     end
| (uu___, uu___1) -> begin
     Pipeline.Ok ([])
     end))


let union_cols : Pipeline.schema  ->  Pipeline.schema  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( l  :  Pipeline.schema ) ( r  :  Pipeline.schema ) ->  
if (Prims.op_Equals (Pipeline.names l) (Pipeline.names r)) then begin
     (union_types l r)
     end else begin
     Pipeline.Error (Pipeline.JoinError ("union requires matching column names"))
     end)


let set_op_cols : Prims.string  ->  Pipeline.schema  ->  Pipeline.schema  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( verb  :  Prims.string ) ( l  :  Pipeline.schema ) ( r  :  Pipeline.schema ) ->  
if (Prims.op_Equals (Pipeline.names l) (Pipeline.names r)) then begin
     Pipeline.Ok (l)
     end else begin
     Pipeline.Error (Pipeline.JoinError ((Prims.strcat verb " requires matching column names")))
     end)


let step_schema : rprims  ->  resolver  ->  Pipeline.param_env  ->  Pipeline.wframe  ->  Pipeline.transform  ->  Pipeline.outcome<Pipeline.schema, Pipeline.eval_error> = (fun ( rp  :  rprims ) ( resolve  :  resolver ) ( env  :  Pipeline.param_env ) ( f  :  Pipeline.wframe ) ( t  :  Pipeline.transform ) -> (match (t) with
| Pipeline.Filter (uu___) -> begin
     Pipeline.Ok (f.cols)
     end
| Pipeline.Derive (uu___, uu___1) -> begin
     Pipeline.Error (Pipeline.TypeError ("Derive is Pipeline.fst\'s clause, not the relational model\'s"))
     end
| Pipeline.Project (pairs) -> begin
     (project_step f.cols pairs)
     end
| Pipeline.GroupBy (keys, aggs) -> begin
     (group_by_cols f.cols keys aggs)
     end
| Pipeline.Join (src, on, how) -> begin
     (match ((source_cols resolve src)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (rcols) -> begin
     (join_cols rp f.cols rcols on how)
     end)
     end
| Pipeline.Window (spec) -> begin
     (window_cols rp f.cols spec)
     end
| Pipeline.Pivot (spec) -> begin
     (pivot_cols rp f.cols f.rows spec)
     end
| Pipeline.Unpivot (ids, vals) -> begin
     (unpivot_cols f.cols f.rows ids vals)
     end
| Pipeline.Sort (sk) -> begin
     (match ((resolve_sort_keys env sk)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (keys) -> begin
     (match ((first_unknown f.cols (Pipeline.names keys))) with
| FStar_Pervasives_Native.Some (n) -> begin
     Pipeline.Error (Pipeline.UnknownColumn (n, (Pipeline.names f.cols)))
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Ok (f.cols)
     end)
     end)
     end
| Pipeline.Distinct -> begin
     Pipeline.Ok (f.cols)
     end
| Pipeline.Limit (n, off) -> begin
     (match ((resolve_int_slot env "limit n" n)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (uu___) -> begin
     (match ((resolve_int_slot env "limit offset" off)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (uu___1) -> begin
     Pipeline.Ok (f.cols)
     end)
     end)
     end
| Pipeline.Union (src) -> begin
     (match ((source_cols resolve src)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (rcols) -> begin
     (union_cols f.cols rcols)
     end)
     end
| Pipeline.Intersect (src) -> begin
     (match ((source_cols resolve src)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (rcols) -> begin
     (set_op_cols "intersect" f.cols rcols)
     end)
     end
| Pipeline.Except (src) -> begin
     (match ((source_cols resolve src)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (rcols) -> begin
     (set_op_cols "except" f.cols rcols)
     end)
     end))


type row_fn = Pipeline.wframe  ->  Pipeline.transform  ->  Pipeline.schema  ->  Pipeline.outcome<Prims.list<Prims.list<Pipeline.cell>>, Pipeline.eval_error>


let other_step : rprims  ->  resolver  ->  Pipeline.param_env  ->  row_fn  ->  Pipeline.wframe  ->  Pipeline.transform  ->  Pipeline.outcome<Pipeline.wframe, Pipeline.eval_error> = (fun ( rp  :  rprims ) ( resolve  :  resolver ) ( env  :  Pipeline.param_env ) ( rows  :  row_fn ) ( f  :  Pipeline.wframe ) ( t  :  Pipeline.transform ) -> (match ((step_schema rp resolve env f t)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (out) -> begin
     (match ((rows f t out)) with
| Pipeline.Error (e) -> begin
     Pipeline.Error (e)
     end
| Pipeline.Ok (rs) -> begin
     Pipeline.Ok ({Pipeline.cols = out; Pipeline.rows = rs})
     end)
     end))


let other_of : rprims  ->  resolver  ->  Pipeline.param_env  ->  row_fn  ->  Pipeline.other_fn = (fun ( rp  :  rprims ) ( resolve  :  resolver ) ( env  :  Pipeline.param_env ) ( rows  :  row_fn ) -> (other_step rp resolve env rows))


type col_knowledge = (Prims.string * FStar_Pervasives_Native.option<Pipeline.column_type>)

type knowledge =
| Closed of Prims.list<col_knowledge>
| AtLeast of Prims.list<col_knowledge>


let uu___is_Closed : knowledge  ->  Prims.bool = (fun ( projectee  :  knowledge ) -> (match (projectee) with
| Closed (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Closed__item___0 : knowledge  ->  Prims.list<col_knowledge> = (fun ( projectee  :  knowledge ) -> (match (projectee) with
| Closed (_0) -> begin
     _0
     end))


let uu___is_AtLeast : knowledge  ->  Prims.bool = (fun ( projectee  :  knowledge ) -> (match (projectee) with
| AtLeast (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AtLeast__item___0 : knowledge  ->  Prims.list<col_knowledge> = (fun ( projectee  :  knowledge ) -> (match (projectee) with
| AtLeast (_0) -> begin
     _0
     end))


type sources = Prims.string  ->  FStar_Pervasives_Native.option<Pipeline.schema>


let columns : knowledge  ->  Prims.list<col_knowledge> = (fun ( k  :  knowledge ) -> (match (k) with
| Closed (cs) -> begin
     cs
     end
| AtLeast (cs) -> begin
     cs
     end))


let with_columns : Prims.list<col_knowledge>  ->  knowledge  ->  knowledge = (fun ( cs  :  Prims.list<col_knowledge> ) ( k  :  knowledge ) -> (match (k) with
| Closed (uu___) -> begin
     Closed (cs)
     end
| AtLeast (uu___) -> begin
     AtLeast (cs)
     end))


let type_of_k : Prims.string  ->  knowledge  ->  FStar_Pervasives_Native.option<Pipeline.column_type> = (fun ( name  :  Prims.string ) ( k  :  knowledge ) -> (match ((Pipeline.assoc name (columns k))) with
| FStar_Pervasives_Native.Some (t) -> begin
     t
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end))


let known : Prims.string  ->  knowledge  ->  col_knowledge = (fun ( name  :  Prims.string ) ( k  :  knowledge ) -> ((name), ((type_of_k name k))))


let rec known_each : Prims.list<Prims.string>  ->  knowledge  ->  Prims.list<col_knowledge> = (fun ( ns  :  Prims.list<Prims.string> ) ( k  :  knowledge ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     ((known n k))::(known_each t k)
     end))


let rec renamed_known : Prims.list<(Prims.string * Prims.string)>  ->  knowledge  ->  Prims.list<col_knowledge> = (fun ( pairs  :  Prims.list<(Prims.string * Prims.string)> ) ( k  :  knowledge ) -> (match (pairs) with
| [] -> begin
     []
     end
| ((src, out))::t -> begin
     (((out), ((type_of_k src k))))::(renamed_known t k)
     end))


let rec known_of : Pipeline.schema  ->  Prims.list<col_knowledge> = (fun ( s  :  Pipeline.schema ) -> (match (s) with
| [] -> begin
     []
     end
| ((n, ty))::t -> begin
     (((n), (FStar_Pervasives_Native.Some (ty))))::(known_of t)
     end))


let of_source : sources  ->  Pipeline.data_source  ->  knowledge = (fun ( srcs  :  sources ) ( src  :  Pipeline.data_source ) -> (match (src) with
| Pipeline.Embedded (f) -> begin
     Closed ((known_of f.cols))
     end
| Pipeline.Ref (name) -> begin
     (match ((srcs name)) with
| FStar_Pervasives_Native.Some (s) -> begin
     Closed ((known_of s))
     end
| FStar_Pervasives_Native.None -> begin
     AtLeast ([])
     end)
     end))


let rec replace_named : col_knowledge  ->  Prims.list<col_knowledge>  ->  Prims.list<col_knowledge> = (fun ( c  :  col_knowledge ) ( cs  :  Prims.list<col_knowledge> ) -> (match (cs) with
| [] -> begin
     []
     end
| ((n, t))::rest -> begin
     ( 
if (Prims.op_Equals n (FStar_Pervasives_Native.fst c)) then begin
     c
     end else begin
     ((n), (t))
     end)::(replace_named c rest)
     end))


let upsert : col_knowledge  ->  knowledge  ->  knowledge = (fun ( c  :  col_knowledge ) ( k  :  knowledge ) -> (

let cs = (columns k)
in  
if (match ((Pipeline.assoc (FStar_Pervasives_Native.fst c) cs)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     (with_columns (replace_named c cs) k)
     end else begin
     (with_columns (Pipeline.app cs ((c)::[])) k)
     end))


let aggregate_type : Pipeline.agg_fn  ->  FStar_Pervasives_Native.option<Pipeline.column_type>  ->  FStar_Pervasives_Native.option<Pipeline.column_type> = (fun ( fn  :  Pipeline.agg_fn ) ( src  :  FStar_Pervasives_Native.option<Pipeline.column_type> ) -> (match (src) with
| FStar_Pervasives_Native.Some (ty) -> begin
     FStar_Pervasives_Native.Some ((agg_type fn ty))
     end
| FStar_Pervasives_Native.None -> begin
     (match (fn) with
| Pipeline.Count -> begin
     FStar_Pervasives_Native.Some (Pipeline.IntType)
     end
| Pipeline.CountDistinct -> begin
     FStar_Pervasives_Native.Some (Pipeline.IntType)
     end
| Pipeline.Mean -> begin
     FStar_Pervasives_Native.Some (Pipeline.FloatType)
     end
| Pipeline.Median -> begin
     FStar_Pervasives_Native.Some (Pipeline.FloatType)
     end
| Pipeline.StdDev -> begin
     FStar_Pervasives_Native.Some (Pipeline.FloatType)
     end
| Pipeline.Sum -> begin
     FStar_Pervasives_Native.None
     end
| Pipeline.Min -> begin
     FStar_Pervasives_Native.None
     end
| Pipeline.Max -> begin
     FStar_Pervasives_Native.None
     end
| Pipeline.First -> begin
     FStar_Pervasives_Native.None
     end
| Pipeline.Last -> begin
     FStar_Pervasives_Native.None
     end)
     end))


let rec agg_known : Prims.list<Pipeline.agg>  ->  knowledge  ->  Prims.list<col_knowledge> = (fun ( aggs  :  Prims.list<Pipeline.agg> ) ( k  :  knowledge ) -> (match (aggs) with
| [] -> begin
     []
     end
| (a)::t -> begin
     (((a.a_name), ((aggregate_type a.a_fn (type_of_k a.a_of k)))))::(agg_known t k)
     end))


let window_type_k : knowledge  ->  Pipeline.window_spec  ->  FStar_Pervasives_Native.option<Pipeline.column_type> = (fun ( k  :  knowledge ) ( spec  :  Pipeline.window_spec ) -> (match (spec.w_fn) with
| Pipeline.RowNumber -> begin
     FStar_Pervasives_Native.Some (Pipeline.IntType)
     end
| Pipeline.Rank -> begin
     FStar_Pervasives_Native.Some (Pipeline.IntType)
     end
| Pipeline.DenseRank -> begin
     FStar_Pervasives_Native.Some (Pipeline.IntType)
     end
| Pipeline.CompetitionRank -> begin
     FStar_Pervasives_Native.Some (Pipeline.IntType)
     end
| Pipeline.NTile (uu___) -> begin
     FStar_Pervasives_Native.Some (Pipeline.IntType)
     end
| Pipeline.RollingMean -> begin
     FStar_Pervasives_Native.Some (Pipeline.FloatType)
     end
| Pipeline.CumulSum -> begin
     (match ((type_of_k spec.w_of k)) with
| FStar_Pervasives_Native.Some (Pipeline.DecimalType) -> begin
     FStar_Pervasives_Native.Some (Pipeline.DecimalType)
     end
| FStar_Pervasives_Native.Some (uu___) -> begin
     FStar_Pervasives_Native.Some (Pipeline.FloatType)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
| Pipeline.RollingSum -> begin
     (match ((type_of_k spec.w_of k)) with
| FStar_Pervasives_Native.Some (Pipeline.DecimalType) -> begin
     FStar_Pervasives_Native.Some (Pipeline.DecimalType)
     end
| FStar_Pervasives_Native.Some (uu___) -> begin
     FStar_Pervasives_Native.Some (Pipeline.FloatType)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
| Pipeline.Lag -> begin
     (type_of_k spec.w_of k)
     end
| Pipeline.Lead -> begin
     (type_of_k spec.w_of k)
     end
| Pipeline.CumulMax -> begin
     (type_of_k spec.w_of k)
     end
| Pipeline.CumulMin -> begin
     (type_of_k spec.w_of k)
     end))


let rec known_fields : Prims.list<col_knowledge>  ->  Pipeline.schema = (fun ( cs  :  Prims.list<col_knowledge> ) -> (match (cs) with
| [] -> begin
     []
     end
| ((n, FStar_Pervasives_Native.Some (t)))::rest -> begin
     (((n), (t)))::(known_fields rest)
     end
| ((uu___, FStar_Pervasives_Native.None))::rest -> begin
     (known_fields rest)
     end))


let derived_column_type : Pipeline.schema  ->  Pipeline.col_expr  ->  FStar_Pervasives_Native.option<Pipeline.column_type> = (fun ( cols  :  Pipeline.schema ) ( x  :  Pipeline.col_expr ) -> (match ((Pipeline.derived_typing_of cols x)) with
| Pipeline.Decided (ty) -> begin
     FStar_Pervasives_Native.Some (ty)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let rec all_known : Prims.list<Prims.string>  ->  knowledge  ->  Prims.bool = (fun ( ns  :  Prims.list<Prims.string> ) ( k  :  knowledge ) -> (match (ns) with
| [] -> begin
     true
     end
| (n)::t -> begin
     ((match ((type_of_k n k)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (all_known t k))
     end))


let rec known_types : Prims.list<Prims.string>  ->  knowledge  ->  Prims.list<Pipeline.column_type> = (fun ( ns  :  Prims.list<Prims.string> ) ( k  :  knowledge ) -> (match (ns) with
| [] -> begin
     []
     end
| (n)::t -> begin
     (match ((type_of_k n k)) with
| FStar_Pervasives_Native.Some (ty) -> begin
     (ty)::(known_types t k)
     end
| FStar_Pervasives_Native.None -> begin
     (known_types t k)
     end)
     end))


let unpivot_type_k : Prims.list<Prims.string>  ->  knowledge  ->  FStar_Pervasives_Native.option<Pipeline.column_type> = (fun ( vals  :  Prims.list<Prims.string> ) ( k  :  knowledge ) ->  
if (all_known vals k) then begin
     (match ((unpivot_typing (known_types vals k))) with
| Pipeline.Decided (ty) -> begin
     FStar_Pervasives_Native.Some (ty)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end)
     end else begin
     FStar_Pervasives_Native.None
     end)


let rec kept_right : Prims.list<Prims.string>  ->  Prims.list<col_knowledge>  ->  Prims.list<col_knowledge> = (fun ( lnames  :  Prims.list<Prims.string> ) ( rcs  :  Prims.list<col_knowledge> ) -> (match (rcs) with
| [] -> begin
     []
     end
| ((n, t))::rest -> begin
      
if (has n lnames) then begin
     (kept_right lnames rest)
     end else begin
     (((n), (t)))::(kept_right lnames rest)
     end
     end))


let rec renamed_k : Prims.list<Prims.string>  ->  Prims.list<col_knowledge>  ->  Prims.list<col_knowledge> = (fun ( ns  :  Prims.list<Prims.string> ) ( cs  :  Prims.list<col_knowledge> ) -> (match (((ns), (cs))) with
| ((n)::nt, ((uu___, t))::ct) -> begin
     (((n), (t)))::(renamed_k nt ct)
     end
| (uu___, uu___1) -> begin
     []
     end))


let walk : rprims  ->  sources  ->  knowledge  ->  Pipeline.transform  ->  knowledge = (fun ( rp  :  rprims ) ( srcs  :  sources ) ( k  :  knowledge ) ( t  :  Pipeline.transform ) -> (match (t) with
| Pipeline.Filter (uu___) -> begin
     k
     end
| Pipeline.Sort (uu___) -> begin
     k
     end
| Pipeline.Distinct -> begin
     k
     end
| Pipeline.Limit (uu___, uu___1) -> begin
     k
     end
| Pipeline.Union (uu___) -> begin
     k
     end
| Pipeline.Intersect (uu___) -> begin
     k
     end
| Pipeline.Except (uu___) -> begin
     k
     end
| Pipeline.Project (pairs) -> begin
     Closed ((renamed_known pairs k))
     end
| Pipeline.Derive (name, x) -> begin
     (upsert ((name), ((derived_column_type (known_fields (columns k)) x))) k)
     end
| Pipeline.GroupBy (keys, aggs) -> begin
     Closed ((Pipeline.app (known_each keys k) (agg_known aggs k)))
     end
| Pipeline.Window (spec) -> begin
     (with_columns (Pipeline.app (columns k) ((((spec.w_as), ((window_type_k k spec))))::[])) k)
     end
| Pipeline.Pivot (spec) -> begin
     AtLeast ((known_each spec.p_index k))
     end
| Pipeline.Unpivot (ids, vals) -> begin
     Closed ((Pipeline.app (known_each ids k) (((("variable"), (FStar_Pervasives_Native.Some (Pipeline.StringType))))::((("value"), ((unpivot_type_k vals k))))::[])))
     end
| Pipeline.Join (src, uu___, how) -> begin
     (match (how) with
| Pipeline.Semi -> begin
     k
     end
| Pipeline.Anti -> begin
     k
     end
| uu___1 -> begin
     (match (k) with
| AtLeast (cs) -> begin
     AtLeast (cs)
     end
| Closed (left) -> begin
     (

let right = (of_source srcs src)
in (

let rcs = (columns right)
in (match (right) with
| Closed (uu___2) -> begin
     Closed ((Pipeline.app left (renamed_k (join_right_names rp (Pipeline.names left) (Pipeline.names rcs)) rcs)))
     end
| AtLeast (uu___2) -> begin
     AtLeast ((Pipeline.app left (kept_right (Pipeline.names left) rcs)))
     end)))
     end)
     end)
     end))


let rec cols_agree : Prims.list<col_knowledge>  ->  Pipeline.schema  ->  Prims.bool = (fun ( cs  :  Prims.list<col_knowledge> ) ( cols  :  Pipeline.schema ) -> (match (((cs), (cols))) with
| ([], []) -> begin
     true
     end
| (((n, FStar_Pervasives_Native.Some (t)))::ct, ((m, u))::st) -> begin
     (((Prims.op_Equals n m) && (Prims.op_Equals t u)) && (cols_agree ct st))
     end
| (((n, FStar_Pervasives_Native.None))::ct, ((m, uu___))::st) -> begin
     ((Prims.op_Equals n m) && (cols_agree ct st))
     end
| (uu___, uu___1) -> begin
     false
     end))


let rec at_least : Prims.list<col_knowledge>  ->  Pipeline.schema  ->  Prims.bool = (fun ( cs  :  Prims.list<col_knowledge> ) ( cols  :  Pipeline.schema ) -> (match (cs) with
| [] -> begin
     true
     end
| ((n, FStar_Pervasives_Native.Some (t)))::ct -> begin
     ((Prims.op_Equals (Pipeline.assoc n cols) (FStar_Pervasives_Native.Some (t))) && (at_least ct cols))
     end
| ((n, FStar_Pervasives_Native.None))::ct -> begin
     ((match ((Pipeline.assoc n cols)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (at_least ct cols))
     end))


let sound : knowledge  ->  Pipeline.schema  ->  Prims.bool = (fun ( k  :  knowledge ) ( cols  :  Pipeline.schema ) -> (match (k) with
| Closed (cs) -> begin
     (cols_agree cs cols)
     end
| AtLeast (cs) -> begin
     (at_least cs cols)
     end))


let rec remove_one : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( x  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (h)::t -> begin
      
if (Prims.op_Equals h x) then begin
     t
     end else begin
     (h)::(remove_one x t)
     end
     end))


let rec agg_name_list : Prims.list<Pipeline.agg>  ->  Prims.list<Prims.string> = (fun ( l  :  Prims.list<Pipeline.agg> ) -> (match (l) with
| [] -> begin
     []
     end
| (a)::t -> begin
     (a.a_name)::(agg_name_list t)
     end))


let rec prefix : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) ( m  :  Prims.list<Prims.string> ) -> (match (((l), (m))) with
| ([], uu___) -> begin
     true
     end
| ((x)::lt, (y)::mt) -> begin
     ((Prims.op_Equals x y) && (prefix lt mt))
     end
| (uu___, uu___1) -> begin
     false
     end))


let rec agg_of_names : Prims.list<Pipeline.agg>  ->  Prims.list<Prims.string> = (fun ( l  :  Prims.list<Pipeline.agg> ) -> (match (l) with
| [] -> begin
     []
     end
| (a)::t -> begin
     (a.a_of)::(agg_of_names t)
     end))




