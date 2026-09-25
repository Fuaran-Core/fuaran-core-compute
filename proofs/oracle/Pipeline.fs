module Pipeline
type outcome<'a, 'e> =
| Ok of 'a
| Error of 'e


let uu___is_Ok = (fun ( projectee  :  outcome<'a, 'e> ) -> (match (projectee) with
| Ok (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Ok__item___0 = (fun ( projectee  :  outcome<'a, 'e> ) -> (match (projectee) with
| Ok (_0) -> begin
     _0
     end))


let uu___is_Error = (fun ( projectee  :  outcome<'a, 'e> ) -> (match (projectee) with
| Error (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Error__item___0 = (fun ( projectee  :  outcome<'a, 'e> ) -> (match (projectee) with
| Error (_0) -> begin
     _0
     end))


let result_map = (fun ( f  :  'a  ->  'b ) ( r  :  outcome<'a, 'e> ) -> (match (r) with
| Ok (x) -> begin
     Ok ((f x))
     end
| Error (err) -> begin
     Error (err)
     end))


let result_bind = (fun ( r  :  outcome<'a, 'e> ) ( f  :  'a  ->  outcome<'b, 'e> ) -> (match (r) with
| Ok (x) -> begin
     (f x)
     end
| Error (err) -> begin
     Error (err)
     end))


let rec len = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::t -> begin
     ((Prims.parse_int "1") + (len t))
     end))


let rec app = (fun ( l  :  Prims.list<'a> ) ( m  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     m
     end
| (h)::t -> begin
     (h)::(app t m)
     end))


let rec nth = (fun ( l  :  Prims.list<'a> ) ( i  :  Prims.nat ) -> (match (l) with
| (x)::t -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     x
     end else begin
     (nth t (i - (Prims.parse_int "1")))
     end
     end))


let rec set_at = (fun ( i  :  Prims.nat ) ( v  :  'a ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     (v)::t
     end else begin
     (x)::(set_at (i - (Prims.parse_int "1")) v t)
     end
     end))


let rec names = (fun ( l  :  Prims.list<(Prims.string * 'b)> ) -> (match (l) with
| [] -> begin
     []
     end
| ((n, uu___))::t -> begin
     (n)::(names t)
     end))


let rec assoc = (fun ( name  :  Prims.string ) ( l  :  Prims.list<(Prims.string * 'b)> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, v))::t -> begin
      
if (Prims.op_Equals n name) then begin
     FStar_Pervasives_Native.Some (v)
     end else begin
     (assoc name t)
     end
     end))


let rec index_of = (fun ( name  :  Prims.string ) ( l  :  Prims.list<(Prims.string * 'b)> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, uu___))::t -> begin
      
if (Prims.op_Equals n name) then begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "0"))
     end else begin
     (match ((index_of name t)) with
| FStar_Pervasives_Native.Some (i) -> begin
     FStar_Pervasives_Native.Some ((i + (Prims.parse_int "1")))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
     end))

type column_type =
| IntType
| FloatType
| BoolType
| StringType
| DateType
| TimestampType


let uu___is_IntType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| IntType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_FloatType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| FloatType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_BoolType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| BoolType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_StringType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| StringType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_DateType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| DateType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TimestampType : column_type  ->  Prims.bool = (fun ( projectee  :  column_type ) -> (match (projectee) with
| TimestampType -> begin
     true
     end
| uu___ -> begin
     false
     end))

type cell =
| Int of Prims.int
| Float of Prims.string
| Bool of Prims.bool
| Str of Prims.string
| Date of Prims.string
| Timestamp of Prims.string
| Null


let uu___is_Int : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Int (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Int__item___0 : cell  ->  Prims.int = (fun ( projectee  :  cell ) -> (match (projectee) with
| Int (_0) -> begin
     _0
     end))


let uu___is_Float : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Float (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Float__item___0 : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Float (_0) -> begin
     _0
     end))


let uu___is_Bool : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Bool (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Bool__item___0 : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Bool (_0) -> begin
     _0
     end))


let uu___is_Str : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Str (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Str__item___0 : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Str (_0) -> begin
     _0
     end))


let uu___is_Date : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Date (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Date__item___0 : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Date (_0) -> begin
     _0
     end))


let uu___is_Timestamp : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Timestamp (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Timestamp__item___0 : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Timestamp (_0) -> begin
     _0
     end))


let uu___is_Null : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Null -> begin
     true
     end
| uu___ -> begin
     false
     end))

type join_kind =
| Inner
| Left
| Right
| Outer
| Semi
| Anti


let uu___is_Inner : join_kind  ->  Prims.bool = (fun ( projectee  :  join_kind ) -> (match (projectee) with
| Inner -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Left : join_kind  ->  Prims.bool = (fun ( projectee  :  join_kind ) -> (match (projectee) with
| Left -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Right : join_kind  ->  Prims.bool = (fun ( projectee  :  join_kind ) -> (match (projectee) with
| Right -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Outer : join_kind  ->  Prims.bool = (fun ( projectee  :  join_kind ) -> (match (projectee) with
| Outer -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Semi : join_kind  ->  Prims.bool = (fun ( projectee  :  join_kind ) -> (match (projectee) with
| Semi -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Anti : join_kind  ->  Prims.bool = (fun ( projectee  :  join_kind ) -> (match (projectee) with
| Anti -> begin
     true
     end
| uu___ -> begin
     false
     end))

type window_fn =
| RowNumber
| Rank
| Lag
| Lead
| CumulSum
| RollingMean
| DenseRank
| CompetitionRank
| NTile of Prims.int
| CumulMax
| CumulMin
| RollingSum


let uu___is_RowNumber : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| RowNumber -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Rank : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| Rank -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Lag : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| Lag -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Lead : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| Lead -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CumulSum : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| CumulSum -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_RollingMean : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| RollingMean -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_DenseRank : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| DenseRank -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CompetitionRank : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| CompetitionRank -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_NTile : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| NTile (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NTile__item___0 : window_fn  ->  Prims.int = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| NTile (_0) -> begin
     _0
     end))


let uu___is_CumulMax : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| CumulMax -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CumulMin : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| CumulMin -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_RollingSum : window_fn  ->  Prims.bool = (fun ( projectee  :  window_fn ) -> (match (projectee) with
| RollingSum -> begin
     true
     end
| uu___ -> begin
     false
     end))

type sort_dir =
| Asc
| Desc


let uu___is_Asc : sort_dir  ->  Prims.bool = (fun ( projectee  :  sort_dir ) -> (match (projectee) with
| Asc -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Desc : sort_dir  ->  Prims.bool = (fun ( projectee  :  sort_dir ) -> (match (projectee) with
| Desc -> begin
     true
     end
| uu___ -> begin
     false
     end))

type scalar_fn =
| Abs
| Round
| Floor
| Ceil
| Length
| Lower
| Upper
| Substr
| DatePart
| Concat
| Trim
| Replace
| DateDiffDays
| Sqrt
| Least
| Greatest
| IndexOf


let uu___is_Abs : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Abs -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Round : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Round -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Floor : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Floor -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Ceil : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Ceil -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Length : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Length -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Lower : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Lower -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Upper : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Upper -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Substr : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Substr -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_DatePart : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| DatePart -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Concat : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Concat -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Trim : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Trim -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Replace : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Replace -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_DateDiffDays : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| DateDiffDays -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Sqrt : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Sqrt -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Least : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Least -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Greatest : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| Greatest -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_IndexOf : scalar_fn  ->  Prims.bool = (fun ( projectee  :  scalar_fn ) -> (match (projectee) with
| IndexOf -> begin
     true
     end
| uu___ -> begin
     false
     end))

type bin_op =
| Add
| Sub
| Mul
| Div
| Mod
| Eq
| Ne
| Lt
| Le
| Gt
| Ge
| And
| Or
| Contains
| StartsWith
| EndsWith


let uu___is_Add : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Add -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Sub : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Sub -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Mul : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Mul -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Div : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Div -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Mod : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Mod -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Eq : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Eq -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Ne : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Ne -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Lt : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Lt -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Le : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Le -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Gt : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Gt -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Ge : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Ge -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_And : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| And -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Or : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Or -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Contains : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| Contains -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_StartsWith : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| StartsWith -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_EndsWith : bin_op  ->  Prims.bool = (fun ( projectee  :  bin_op ) -> (match (projectee) with
| EndsWith -> begin
     true
     end
| uu___ -> begin
     false
     end))

type now_grain =
| GrainDate
| GrainTimestamp


let uu___is_GrainDate : now_grain  ->  Prims.bool = (fun ( projectee  :  now_grain ) -> (match (projectee) with
| GrainDate -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_GrainTimestamp : now_grain  ->  Prims.bool = (fun ( projectee  :  now_grain ) -> (match (projectee) with
| GrainTimestamp -> begin
     true
     end
| uu___ -> begin
     false
     end))

type agg_fn =
| Sum
| Mean
| Min
| Max
| Count
| Median
| StdDev
| First
| Last
| CountDistinct


let uu___is_Sum : agg_fn  ->  Prims.bool = (fun ( projectee  :  agg_fn ) -> (match (projectee) with
| Sum -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Mean : agg_fn  ->  Prims.bool = (fun ( projectee  :  agg_fn ) -> (match (projectee) with
| Mean -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Min : agg_fn  ->  Prims.bool = (fun ( projectee  :  agg_fn ) -> (match (projectee) with
| Min -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Max : agg_fn  ->  Prims.bool = (fun ( projectee  :  agg_fn ) -> (match (projectee) with
| Max -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Count : agg_fn  ->  Prims.bool = (fun ( projectee  :  agg_fn ) -> (match (projectee) with
| Count -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Median : agg_fn  ->  Prims.bool = (fun ( projectee  :  agg_fn ) -> (match (projectee) with
| Median -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_StdDev : agg_fn  ->  Prims.bool = (fun ( projectee  :  agg_fn ) -> (match (projectee) with
| StdDev -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_First : agg_fn  ->  Prims.bool = (fun ( projectee  :  agg_fn ) -> (match (projectee) with
| First -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Last : agg_fn  ->  Prims.bool = (fun ( projectee  :  agg_fn ) -> (match (projectee) with
| Last -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CountDistinct : agg_fn  ->  Prims.bool = (fun ( projectee  :  agg_fn ) -> (match (projectee) with
| CountDistinct -> begin
     true
     end
| uu___ -> begin
     false
     end))

type slot<'a> =
| SlotLit of 'a
| SlotParam of Prims.string


let uu___is_SlotLit = (fun ( projectee  :  slot<'a> ) -> (match (projectee) with
| SlotLit (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SlotLit__item___0 = (fun ( projectee  :  slot<'a> ) -> (match (projectee) with
| SlotLit (_0) -> begin
     _0
     end))


let uu___is_SlotParam = (fun ( projectee  :  slot<'a> ) -> (match (projectee) with
| SlotParam (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SlotParam__item___0 = (fun ( projectee  :  slot<'a> ) -> (match (projectee) with
| SlotParam (_0) -> begin
     _0
     end))


type schema = Prims.list<(Prims.string * column_type)>

type frame = {cols : schema; rows : Prims.list<Prims.list<cell>>}


let __proj__Mkframe__item__cols : frame  ->  schema = (fun ( projectee  :  frame ) -> (match (projectee) with
| {cols = cols; rows = rows} -> begin
     cols
     end))


let __proj__Mkframe__item__rows : frame  ->  Prims.list<Prims.list<cell>> = (fun ( projectee  :  frame ) -> (match (projectee) with
| {cols = cols; rows = rows} -> begin
     rows
     end))

type data_source =
| Embedded of frame
| Ref of Prims.string


let uu___is_Embedded : data_source  ->  Prims.bool = (fun ( projectee  :  data_source ) -> (match (projectee) with
| Embedded (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Embedded__item___0 : data_source  ->  frame = (fun ( projectee  :  data_source ) -> (match (projectee) with
| Embedded (_0) -> begin
     _0
     end))


let uu___is_Ref : data_source  ->  Prims.bool = (fun ( projectee  :  data_source ) -> (match (projectee) with
| Ref (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Ref__item___0 : data_source  ->  Prims.string = (fun ( projectee  :  data_source ) -> (match (projectee) with
| Ref (_0) -> begin
     _0
     end))

type col_expr =
| Col of Prims.string
| Lit of cell
| Param of Prims.string
| Binary of bin_op * col_expr * col_expr
| Not of col_expr
| Coalesce of Prims.list<col_expr>
| Case of Prims.list<(col_expr * col_expr)> * col_expr
| Cast of column_type * col_expr
| ApplyFn of scalar_fn * Prims.list<col_expr>
| InList of col_expr * Prims.list<col_expr>
| IsNull of col_expr
| InParam of col_expr * Prims.string
| Now of now_grain


let uu___is_Col : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Col (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Col__item___0 : col_expr  ->  Prims.string = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Col (_0) -> begin
     _0
     end))


let uu___is_Lit : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Lit (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Lit__item___0 : col_expr  ->  cell = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Lit (_0) -> begin
     _0
     end))


let uu___is_Param : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Param (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Param__item___0 : col_expr  ->  Prims.string = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Param (_0) -> begin
     _0
     end))


let uu___is_Binary : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Binary (_0, _1, _2) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Binary__item___0 : col_expr  ->  bin_op = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Binary (_0, _1, _2) -> begin
     _0
     end))


let __proj__Binary__item___1 : col_expr  ->  col_expr = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Binary (_0, _1, _2) -> begin
     _1
     end))


let __proj__Binary__item___2 : col_expr  ->  col_expr = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Binary (_0, _1, _2) -> begin
     _2
     end))


let uu___is_Not : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Not (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Not__item___0 : col_expr  ->  col_expr = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Not (_0) -> begin
     _0
     end))


let uu___is_Coalesce : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Coalesce (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Coalesce__item___0 : col_expr  ->  Prims.list<col_expr> = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Coalesce (_0) -> begin
     _0
     end))


let uu___is_Case : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Case (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Case__item___0 : col_expr  ->  Prims.list<(col_expr * col_expr)> = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Case (_0, _1) -> begin
     _0
     end))


let __proj__Case__item___1 : col_expr  ->  col_expr = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Case (_0, _1) -> begin
     _1
     end))


let uu___is_Cast : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Cast (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Cast__item___0 : col_expr  ->  column_type = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Cast (_0, _1) -> begin
     _0
     end))


let __proj__Cast__item___1 : col_expr  ->  col_expr = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Cast (_0, _1) -> begin
     _1
     end))


let uu___is_ApplyFn : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| ApplyFn (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ApplyFn__item___0 : col_expr  ->  scalar_fn = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| ApplyFn (_0, _1) -> begin
     _0
     end))


let __proj__ApplyFn__item___1 : col_expr  ->  Prims.list<col_expr> = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| ApplyFn (_0, _1) -> begin
     _1
     end))


let uu___is_InList : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| InList (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__InList__item___0 : col_expr  ->  col_expr = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| InList (_0, _1) -> begin
     _0
     end))


let __proj__InList__item___1 : col_expr  ->  Prims.list<col_expr> = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| InList (_0, _1) -> begin
     _1
     end))


let uu___is_IsNull : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| IsNull (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__IsNull__item___0 : col_expr  ->  col_expr = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| IsNull (_0) -> begin
     _0
     end))


let uu___is_InParam : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| InParam (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__InParam__item___0 : col_expr  ->  col_expr = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| InParam (_0, _1) -> begin
     _0
     end))


let __proj__InParam__item___1 : col_expr  ->  Prims.string = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| InParam (_0, _1) -> begin
     _1
     end))


let uu___is_Now : col_expr  ->  Prims.bool = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Now (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Now__item___0 : col_expr  ->  now_grain = (fun ( projectee  :  col_expr ) -> (match (projectee) with
| Now (_0) -> begin
     _0
     end))

type agg = {a_name : Prims.string; a_fn : agg_fn; a_of : Prims.string}


let __proj__Mkagg__item__a_name : agg  ->  Prims.string = (fun ( projectee  :  agg ) -> (match (projectee) with
| {a_name = a_name; a_fn = a_fn; a_of = a_of} -> begin
     a_name
     end))


let __proj__Mkagg__item__a_fn : agg  ->  agg_fn = (fun ( projectee  :  agg ) -> (match (projectee) with
| {a_name = a_name; a_fn = a_fn; a_of = a_of} -> begin
     a_fn
     end))


let __proj__Mkagg__item__a_of : agg  ->  Prims.string = (fun ( projectee  :  agg ) -> (match (projectee) with
| {a_name = a_name; a_fn = a_fn; a_of = a_of} -> begin
     a_of
     end))

type window_spec = {partition_by : Prims.list<Prims.string>; order_by : Prims.list<(Prims.string * sort_dir)>; w_fn : window_fn; w_of : Prims.string; w_as : Prims.string}


let __proj__Mkwindow_spec__item__partition_by : window_spec  ->  Prims.list<Prims.string> = (fun ( projectee  :  window_spec ) -> (match (projectee) with
| {partition_by = partition_by; order_by = order_by; w_fn = w_fn; w_of = w_of; w_as = w_as} -> begin
     partition_by
     end))


let __proj__Mkwindow_spec__item__order_by : window_spec  ->  Prims.list<(Prims.string * sort_dir)> = (fun ( projectee  :  window_spec ) -> (match (projectee) with
| {partition_by = partition_by; order_by = order_by; w_fn = w_fn; w_of = w_of; w_as = w_as} -> begin
     order_by
     end))


let __proj__Mkwindow_spec__item__w_fn : window_spec  ->  window_fn = (fun ( projectee  :  window_spec ) -> (match (projectee) with
| {partition_by = partition_by; order_by = order_by; w_fn = w_fn; w_of = w_of; w_as = w_as} -> begin
     w_fn
     end))


let __proj__Mkwindow_spec__item__w_of : window_spec  ->  Prims.string = (fun ( projectee  :  window_spec ) -> (match (projectee) with
| {partition_by = partition_by; order_by = order_by; w_fn = w_fn; w_of = w_of; w_as = w_as} -> begin
     w_of
     end))


let __proj__Mkwindow_spec__item__w_as : window_spec  ->  Prims.string = (fun ( projectee  :  window_spec ) -> (match (projectee) with
| {partition_by = partition_by; order_by = order_by; w_fn = w_fn; w_of = w_of; w_as = w_as} -> begin
     w_as
     end))

type pivot_spec = {p_index : Prims.list<Prims.string>; p_on : Prims.string; p_values : Prims.string; p_agg : agg_fn}


let __proj__Mkpivot_spec__item__p_index : pivot_spec  ->  Prims.list<Prims.string> = (fun ( projectee  :  pivot_spec ) -> (match (projectee) with
| {p_index = p_index; p_on = p_on; p_values = p_values; p_agg = p_agg} -> begin
     p_index
     end))


let __proj__Mkpivot_spec__item__p_on : pivot_spec  ->  Prims.string = (fun ( projectee  :  pivot_spec ) -> (match (projectee) with
| {p_index = p_index; p_on = p_on; p_values = p_values; p_agg = p_agg} -> begin
     p_on
     end))


let __proj__Mkpivot_spec__item__p_values : pivot_spec  ->  Prims.string = (fun ( projectee  :  pivot_spec ) -> (match (projectee) with
| {p_index = p_index; p_on = p_on; p_values = p_values; p_agg = p_agg} -> begin
     p_values
     end))


let __proj__Mkpivot_spec__item__p_agg : pivot_spec  ->  agg_fn = (fun ( projectee  :  pivot_spec ) -> (match (projectee) with
| {p_index = p_index; p_on = p_on; p_values = p_values; p_agg = p_agg} -> begin
     p_agg
     end))

type transform =
| Filter of col_expr
| Project of Prims.list<(Prims.string * Prims.string)>
| Derive of Prims.string * col_expr
| GroupBy of Prims.list<Prims.string> * Prims.list<agg>
| Join of data_source * Prims.list<(Prims.string * Prims.string)> * join_kind
| Window of window_spec
| Pivot of pivot_spec
| Unpivot of Prims.list<Prims.string> * Prims.list<Prims.string>
| Sort of Prims.list<(slot<Prims.string> * sort_dir)>
| Distinct
| Limit of slot<Prims.int> * slot<Prims.int>
| Union of data_source
| Intersect of data_source
| Except of data_source


let uu___is_Filter : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Filter (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Filter__item___0 : transform  ->  col_expr = (fun ( projectee  :  transform ) -> (match (projectee) with
| Filter (_0) -> begin
     _0
     end))


let uu___is_Project : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Project (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Project__item___0 : transform  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( projectee  :  transform ) -> (match (projectee) with
| Project (_0) -> begin
     _0
     end))


let uu___is_Derive : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Derive (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Derive__item___0 : transform  ->  Prims.string = (fun ( projectee  :  transform ) -> (match (projectee) with
| Derive (_0, _1) -> begin
     _0
     end))


let __proj__Derive__item___1 : transform  ->  col_expr = (fun ( projectee  :  transform ) -> (match (projectee) with
| Derive (_0, _1) -> begin
     _1
     end))


let uu___is_GroupBy : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| GroupBy (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__GroupBy__item___0 : transform  ->  Prims.list<Prims.string> = (fun ( projectee  :  transform ) -> (match (projectee) with
| GroupBy (_0, _1) -> begin
     _0
     end))


let __proj__GroupBy__item___1 : transform  ->  Prims.list<agg> = (fun ( projectee  :  transform ) -> (match (projectee) with
| GroupBy (_0, _1) -> begin
     _1
     end))


let uu___is_Join : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Join (_0, _1, _2) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Join__item___0 : transform  ->  data_source = (fun ( projectee  :  transform ) -> (match (projectee) with
| Join (_0, _1, _2) -> begin
     _0
     end))


let __proj__Join__item___1 : transform  ->  Prims.list<(Prims.string * Prims.string)> = (fun ( projectee  :  transform ) -> (match (projectee) with
| Join (_0, _1, _2) -> begin
     _1
     end))


let __proj__Join__item___2 : transform  ->  join_kind = (fun ( projectee  :  transform ) -> (match (projectee) with
| Join (_0, _1, _2) -> begin
     _2
     end))


let uu___is_Window : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Window (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Window__item___0 : transform  ->  window_spec = (fun ( projectee  :  transform ) -> (match (projectee) with
| Window (_0) -> begin
     _0
     end))


let uu___is_Pivot : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Pivot (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Pivot__item___0 : transform  ->  pivot_spec = (fun ( projectee  :  transform ) -> (match (projectee) with
| Pivot (_0) -> begin
     _0
     end))


let uu___is_Unpivot : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Unpivot (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Unpivot__item___0 : transform  ->  Prims.list<Prims.string> = (fun ( projectee  :  transform ) -> (match (projectee) with
| Unpivot (_0, _1) -> begin
     _0
     end))


let __proj__Unpivot__item___1 : transform  ->  Prims.list<Prims.string> = (fun ( projectee  :  transform ) -> (match (projectee) with
| Unpivot (_0, _1) -> begin
     _1
     end))


let uu___is_Sort : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Sort (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Sort__item___0 : transform  ->  Prims.list<(slot<Prims.string> * sort_dir)> = (fun ( projectee  :  transform ) -> (match (projectee) with
| Sort (_0) -> begin
     _0
     end))


let uu___is_Distinct : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Distinct -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Limit : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Limit (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Limit__item___0 : transform  ->  slot<Prims.int> = (fun ( projectee  :  transform ) -> (match (projectee) with
| Limit (_0, _1) -> begin
     _0
     end))


let __proj__Limit__item___1 : transform  ->  slot<Prims.int> = (fun ( projectee  :  transform ) -> (match (projectee) with
| Limit (_0, _1) -> begin
     _1
     end))


let uu___is_Union : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Union (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Union__item___0 : transform  ->  data_source = (fun ( projectee  :  transform ) -> (match (projectee) with
| Union (_0) -> begin
     _0
     end))


let uu___is_Intersect : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Intersect (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Intersect__item___0 : transform  ->  data_source = (fun ( projectee  :  transform ) -> (match (projectee) with
| Intersect (_0) -> begin
     _0
     end))


let uu___is_Except : transform  ->  Prims.bool = (fun ( projectee  :  transform ) -> (match (projectee) with
| Except (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Except__item___0 : transform  ->  data_source = (fun ( projectee  :  transform ) -> (match (projectee) with
| Except (_0) -> begin
     _0
     end))

type eval_error =
| UnknownColumn of Prims.string * Prims.list<Prims.string>
| TypeError of Prims.string
| AggError of Prims.string
| JoinError of Prims.string
| ArityError of Prims.string * Prims.int * Prims.int
| UnresolvedSource of Prims.string
| OverflowError of Prims.string
| UnboundParam of Prims.string * Prims.list<Prims.string>
| UnpinnedClock of now_grain


let uu___is_UnknownColumn : eval_error  ->  Prims.bool = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| UnknownColumn (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnknownColumn__item___0 : eval_error  ->  Prims.string = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| UnknownColumn (_0, _1) -> begin
     _0
     end))


let __proj__UnknownColumn__item___1 : eval_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| UnknownColumn (_0, _1) -> begin
     _1
     end))


let uu___is_TypeError : eval_error  ->  Prims.bool = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| TypeError (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TypeError__item___0 : eval_error  ->  Prims.string = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| TypeError (_0) -> begin
     _0
     end))


let uu___is_AggError : eval_error  ->  Prims.bool = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| AggError (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AggError__item___0 : eval_error  ->  Prims.string = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| AggError (_0) -> begin
     _0
     end))


let uu___is_JoinError : eval_error  ->  Prims.bool = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| JoinError (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JoinError__item___0 : eval_error  ->  Prims.string = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| JoinError (_0) -> begin
     _0
     end))


let uu___is_ArityError : eval_error  ->  Prims.bool = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| ArityError (_0, _1, _2) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ArityError__item___0 : eval_error  ->  Prims.string = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| ArityError (_0, _1, _2) -> begin
     _0
     end))


let __proj__ArityError__item___1 : eval_error  ->  Prims.int = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| ArityError (_0, _1, _2) -> begin
     _1
     end))


let __proj__ArityError__item___2 : eval_error  ->  Prims.int = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| ArityError (_0, _1, _2) -> begin
     _2
     end))


let uu___is_UnresolvedSource : eval_error  ->  Prims.bool = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| UnresolvedSource (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnresolvedSource__item___0 : eval_error  ->  Prims.string = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| UnresolvedSource (_0) -> begin
     _0
     end))


let uu___is_OverflowError : eval_error  ->  Prims.bool = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| OverflowError (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__OverflowError__item___0 : eval_error  ->  Prims.string = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| OverflowError (_0) -> begin
     _0
     end))


let uu___is_UnboundParam : eval_error  ->  Prims.bool = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| UnboundParam (_0, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnboundParam__item___0 : eval_error  ->  Prims.string = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| UnboundParam (_0, _1) -> begin
     _0
     end))


let __proj__UnboundParam__item___1 : eval_error  ->  Prims.list<Prims.string> = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| UnboundParam (_0, _1) -> begin
     _1
     end))


let uu___is_UnpinnedClock : eval_error  ->  Prims.bool = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| UnpinnedClock (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__UnpinnedClock__item___0 : eval_error  ->  now_grain = (fun ( projectee  :  eval_error ) -> (match (projectee) with
| UnpinnedClock (_0) -> begin
     _0
     end))


type param_env = Prims.list<(Prims.string * cell)>


let cost_of : frame  ->  transform  ->  Prims.nat = (fun ( f  :  frame ) ( step  :  transform ) -> (match (step) with
| Filter (uu___) -> begin
     (len f.rows)
     end
| Derive (uu___, uu___1) -> begin
     (len f.rows)
     end
| uu___ -> begin
     (Prims.parse_int "0")
     end))

type prims = {binary : bin_op  ->  cell  ->  cell  ->  outcome<cell, eval_error>; cast_cell : column_type  ->  cell  ->  outcome<cell, eval_error>; apply_fn : scalar_fn  ->  Prims.list<cell>  ->  outcome<cell, eval_error>; compare : cell  ->  cell  ->  FStar_Pervasives_Native.option<Prims.int>}


let __proj__Mkprims__item__binary : prims  ->  bin_op  ->  cell  ->  cell  ->  outcome<cell, eval_error> = (fun ( projectee  :  prims ) -> (match (projectee) with
| {binary = binary; cast_cell = cast_cell; apply_fn = apply_fn; compare = compare} -> begin
     binary
     end))


let __proj__Mkprims__item__cast_cell : prims  ->  column_type  ->  cell  ->  outcome<cell, eval_error> = (fun ( projectee  :  prims ) -> (match (projectee) with
| {binary = binary; cast_cell = cast_cell; apply_fn = apply_fn; compare = compare} -> begin
     cast_cell
     end))


let __proj__Mkprims__item__apply_fn : prims  ->  scalar_fn  ->  Prims.list<cell>  ->  outcome<cell, eval_error> = (fun ( projectee  :  prims ) -> (match (projectee) with
| {binary = binary; cast_cell = cast_cell; apply_fn = apply_fn; compare = compare} -> begin
     apply_fn
     end))


let __proj__Mkprims__item__compare : prims  ->  cell  ->  cell  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  prims ) -> (match (projectee) with
| {binary = binary; cast_cell = cast_cell; apply_fn = apply_fn; compare = compare} -> begin
     compare
     end))


type row_of = Prims.list<cell>


let rec eval_expr : prims  ->  param_env  ->  schema  ->  row_of  ->  col_expr  ->  outcome<cell, eval_error> = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( row  :  row_of ) ( x  :  col_expr ) -> (match (x) with
| Col (name) -> begin
     (match ((index_of name cols)) with
| FStar_Pervasives_Native.Some (i) -> begin
     Ok ((nth row i))
     end
| FStar_Pervasives_Native.None -> begin
     Error (UnknownColumn (name, (names cols)))
     end)
     end
| Lit (c) -> begin
     Ok (c)
     end
| Param (name) -> begin
     (match ((assoc name env)) with
| FStar_Pervasives_Native.Some (c) -> begin
     Ok (c)
     end
| FStar_Pervasives_Native.None -> begin
     Error (UnboundParam (name, (names env)))
     end)
     end
| Binary (op, a, b) -> begin
     (match ((eval_expr pr env cols row a)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (av) -> begin
     (match ((eval_expr pr env cols row b)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (bv) -> begin
     (pr.binary op av bv)
     end)
     end)
     end
| Not (inner) -> begin
     (match ((eval_expr pr env cols row inner)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (Bool (b)) -> begin
     Ok (Bool ((not (b))))
     end
| Ok (Null) -> begin
     Ok (Null)
     end
| Ok (uu___) -> begin
     Error (TypeError ("not of a non-bool"))
     end)
     end
| Coalesce (xs) -> begin
     (eval_coalesce pr env cols row xs)
     end
| Case (cases, els) -> begin
     (match ((eval_case pr env cols row cases)) with
| FStar_Pervasives_Native.Some (r) -> begin
     r
     end
| FStar_Pervasives_Native.None -> begin
     (eval_expr pr env cols row els)
     end)
     end
| Cast (ty, inner) -> begin
     (match ((eval_expr pr env cols row inner)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (v) -> begin
     (pr.cast_cell ty v)
     end)
     end
| InList (subject, items) -> begin
     (match ((eval_expr pr env cols row subject)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (Null) -> begin
     Ok (Null)
     end
| Ok (sv) -> begin
     (eval_in pr env cols row sv false items)
     end)
     end
| IsNull (inner) -> begin
     (match ((eval_expr pr env cols row inner)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (Null) -> begin
     Ok (Bool (true))
     end
| Ok (uu___) -> begin
     Ok (Bool (false))
     end)
     end
| InParam (uu___, name) -> begin
     Error (UnboundParam (name, (names env)))
     end
| Now (grain) -> begin
     Error (UnpinnedClock (grain))
     end
| ApplyFn (fn, args) -> begin
     (match ((eval_args pr env cols row args)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (vs) -> begin
     (pr.apply_fn fn vs)
     end)
     end))
and eval_coalesce : prims  ->  param_env  ->  schema  ->  row_of  ->  Prims.list<col_expr>  ->  outcome<cell, eval_error> = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( row  :  row_of ) ( xs  :  Prims.list<col_expr> ) -> (match (xs) with
| [] -> begin
     Ok (Null)
     end
| (x)::rest -> begin
     (match ((eval_expr pr env cols row x)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (Null) -> begin
     (eval_coalesce pr env cols row rest)
     end
| Ok (c) -> begin
     Ok (c)
     end)
     end))
and eval_case : prims  ->  param_env  ->  schema  ->  row_of  ->  Prims.list<(col_expr * col_expr)>  ->  FStar_Pervasives_Native.option<outcome<cell, eval_error>> = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( row  :  row_of ) ( cases  :  Prims.list<(col_expr * col_expr)> ) -> (match (cases) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((when_e, then_e))::rest -> begin
     (match ((eval_expr pr env cols row when_e)) with
| Error (err) -> begin
     FStar_Pervasives_Native.Some (Error (err))
     end
| Ok (Bool (true)) -> begin
     FStar_Pervasives_Native.Some ((eval_expr pr env cols row then_e))
     end
| Ok (uu___) -> begin
     (eval_case pr env cols row rest)
     end)
     end))
and eval_in : prims  ->  param_env  ->  schema  ->  row_of  ->  cell  ->  Prims.bool  ->  Prims.list<col_expr>  ->  outcome<cell, eval_error> = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( row  :  row_of ) ( sv  :  cell ) ( saw_null  :  Prims.bool ) ( items  :  Prims.list<col_expr> ) -> (match (items) with
| [] -> begin
     Ok ( 
if saw_null then begin
     Null
     end else begin
     Bool (false)
     end)
     end
| (it)::rest -> begin
     (match ((eval_expr pr env cols row it)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (Null) -> begin
     (eval_in pr env cols row sv true rest)
     end
| Ok (iv) -> begin
     (match ((pr.compare sv iv)) with
| FStar_Pervasives_Native.Some (uu___) when (uu___ = (Prims.parse_int "0")) -> begin
     Ok (Bool (true))
     end
| FStar_Pervasives_Native.Some (uu___) -> begin
     (eval_in pr env cols row sv saw_null rest)
     end
| FStar_Pervasives_Native.None -> begin
     Error (TypeError ("in: comparison between incompatible types"))
     end)
     end)
     end))
and eval_args : prims  ->  param_env  ->  schema  ->  row_of  ->  Prims.list<col_expr>  ->  outcome<Prims.list<cell>, eval_error> = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( row  :  row_of ) ( args  :  Prims.list<col_expr> ) -> (match (args) with
| [] -> begin
     Ok ([])
     end
| (a)::rest -> begin
     (match ((eval_expr pr env cols row a)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (v) -> begin
     (match ((eval_args pr env cols row rest)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (vs) -> begin
     Ok ((v)::vs)
     end)
     end)
     end))


let rec all_width : Prims.nat  ->  Prims.list<Prims.list<cell>>  ->  Prims.bool = (fun ( n  :  Prims.nat ) ( rows  :  Prims.list<Prims.list<cell>> ) -> (match (rows) with
| [] -> begin
     true
     end
| (r)::t -> begin
     ((Prims.op_Equals (len r) n) && (all_width n t))
     end))


let wf : frame  ->  Prims.bool = (fun ( f  :  frame ) -> (all_width (len f.cols) f.rows))


type wframe = frame


let rows_ok : Prims.nat  ->  outcome<Prims.list<Prims.list<cell>>, eval_error>  ->  Prims.bool = (fun ( n  :  Prims.nat ) ( r  :  outcome<Prims.list<Prims.list<cell>>, eval_error> ) -> (match (r) with
| Ok (rs) -> begin
     (all_width n rs)
     end
| Error (uu___) -> begin
     true
     end))


let rec filter_rows : prims  ->  param_env  ->  schema  ->  Prims.list<Prims.list<cell>>  ->  col_expr  ->  outcome<Prims.list<Prims.list<cell>>, eval_error> = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( rows  :  Prims.list<Prims.list<cell>> ) ( pred  :  col_expr ) -> (match (rows) with
| [] -> begin
     Ok ([])
     end
| (r)::rest -> begin
     (match ((eval_expr pr env cols r pred)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (Bool (true)) -> begin
     (match ((filter_rows pr env cols rest pred)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (rs) -> begin
     Ok ((r)::rs)
     end)
     end
| Ok (uu___) -> begin
     (filter_rows pr env cols rest pred)
     end)
     end))


let rec derive_cells : prims  ->  param_env  ->  schema  ->  Prims.list<Prims.list<cell>>  ->  col_expr  ->  outcome<Prims.list<cell>, eval_error> = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( rows  :  Prims.list<Prims.list<cell>> ) ( x  :  col_expr ) -> (match (rows) with
| [] -> begin
     Ok ([])
     end
| (r)::rest -> begin
     (match ((eval_expr pr env cols r x)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (v) -> begin
     (match ((derive_cells pr env cols rest x)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (vs) -> begin
     Ok ((v)::vs)
     end)
     end)
     end))


let type_of : cell  ->  FStar_Pervasives_Native.option<column_type> = (fun ( c  :  cell ) -> (match (c) with
| Int (uu___) -> begin
     FStar_Pervasives_Native.Some (IntType)
     end
| Float (uu___) -> begin
     FStar_Pervasives_Native.Some (FloatType)
     end
| Bool (uu___) -> begin
     FStar_Pervasives_Native.Some (BoolType)
     end
| Str (uu___) -> begin
     FStar_Pervasives_Native.Some (StringType)
     end
| Date (uu___) -> begin
     FStar_Pervasives_Native.Some (DateType)
     end
| Timestamp (uu___) -> begin
     FStar_Pervasives_Native.Some (TimestampType)
     end
| Null -> begin
     FStar_Pervasives_Native.None
     end))


let rec first_type : Prims.list<cell>  ->  FStar_Pervasives_Native.option<column_type> = (fun ( cells  :  Prims.list<cell> ) -> (match (cells) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (v)::t -> begin
     (match ((type_of v)) with
| FStar_Pervasives_Native.Some (ty) -> begin
     FStar_Pervasives_Native.Some (ty)
     end
| FStar_Pervasives_Native.None -> begin
     (first_type t)
     end)
     end))


let infer_type : Prims.list<cell>  ->  column_type = (fun ( cells  :  Prims.list<cell> ) -> (match ((first_type cells)) with
| FStar_Pervasives_Native.Some (ty) -> begin
     ty
     end
| FStar_Pervasives_Native.None -> begin
     StringType
     end))


let rec retype_at : Prims.nat  ->  column_type  ->  schema  ->  schema = (fun ( i  :  Prims.nat ) ( ty  :  column_type ) ( cols  :  schema ) -> (match (cols) with
| [] -> begin
     []
     end
| ((n, t))::rest -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     (((n), (ty)))::rest
     end else begin
     (((n), (t)))::(retype_at (i - (Prims.parse_int "1")) ty rest)
     end
     end))


let rec zip_replace : Prims.nat  ->  Prims.list<Prims.list<cell>>  ->  Prims.list<cell>  ->  Prims.list<Prims.list<cell>> = (fun ( i  :  Prims.nat ) ( rows  :  Prims.list<Prims.list<cell>> ) ( cells  :  Prims.list<cell> ) -> (match (((rows), (cells))) with
| ([], []) -> begin
     []
     end
| ((r)::rt, (v)::vt) -> begin
     ((set_at i v r))::(zip_replace i rt vt)
     end))


let rec zip_append : Prims.list<Prims.list<cell>>  ->  Prims.list<cell>  ->  Prims.list<Prims.list<cell>> = (fun ( rows  :  Prims.list<Prims.list<cell>> ) ( cells  :  Prims.list<cell> ) -> (match (((rows), (cells))) with
| ([], []) -> begin
     []
     end
| ((r)::rt, (v)::vt) -> begin
     ((app r ((v)::[])))::(zip_append rt vt)
     end))


let eval_derive : prims  ->  param_env  ->  wframe  ->  Prims.string  ->  col_expr  ->  outcome<wframe, eval_error> = (fun ( pr  :  prims ) ( env  :  param_env ) ( f  :  wframe ) ( name  :  Prims.string ) ( x  :  col_expr ) -> (match ((derive_cells pr env f.cols f.rows x)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (cells) -> begin
     (

let ty = (infer_type cells)
in (match ((index_of name f.cols)) with
| FStar_Pervasives_Native.Some (i) -> begin
     Ok ({cols = (retype_at i ty f.cols); rows = (zip_replace i f.rows cells)})
     end
| FStar_Pervasives_Native.None -> begin
     Ok ({cols = (app f.cols ((((name), (ty)))::[])); rows = (zip_append f.rows cells)})
     end))
     end))


type other_fn = wframe  ->  transform  ->  outcome<wframe, eval_error>


let eval_step : prims  ->  other_fn  ->  param_env  ->  wframe  ->  transform  ->  outcome<wframe, eval_error> = (fun ( pr  :  prims ) ( other  :  other_fn ) ( env  :  param_env ) ( f  :  wframe ) ( t  :  transform ) -> (match (t) with
| Filter (pred) -> begin
     (match ((filter_rows pr env f.cols f.rows pred)) with
| Error (err) -> begin
     Error (err)
     end
| Ok (rs) -> begin
     Ok ({cols = f.cols; rows = rs})
     end)
     end
| Derive (name, x) -> begin
     (eval_derive pr env f name x)
     end
| uu___ -> begin
     (other f t)
     end))


type step_fn<'e> = wframe  ->  transform  ->  outcome<wframe, 'e>


let rec go = (fun ( step  :  step_fn<'e> ) ( f  :  wframe ) ( evaluated  :  Prims.nat ) ( p  :  Prims.list<transform> ) -> (match (p) with
| [] -> begin
     Ok (((f), (evaluated)))
     end
| (s)::rest -> begin
     (

let cost = (cost_of f s)
in (result_bind (step f s) (fun ( f'  :  wframe ) -> (go step f' (evaluated + cost) rest))))
     end))


let eval_counted : prims  ->  other_fn  ->  param_env  ->  Prims.list<transform>  ->  wframe  ->  outcome<(wframe * Prims.nat), eval_error> = (fun ( pr  :  prims ) ( other  :  other_fn ) ( env  :  param_env ) ( p  :  Prims.list<transform> ) ( input  :  wframe ) -> (go (eval_step pr other env) input (Prims.parse_int "0") p))


let eval_uncounted : prims  ->  other_fn  ->  param_env  ->  Prims.list<transform>  ->  wframe  ->  outcome<wframe, eval_error> = (fun ( pr  :  prims ) ( other  :  other_fn ) ( env  :  param_env ) ( p  :  Prims.list<transform> ) ( input  :  wframe ) -> (result_map FStar_Pervasives_Native.fst (eval_counted pr other env p input)))


let rec walk_ok = (fun ( step  :  step_fn<'e> ) ( f  :  wframe ) ( p  :  Prims.list<transform> ) -> (match (p) with
| [] -> begin
     true
     end
| (s)::rest -> begin
     (match ((step f s)) with
| Ok (f') -> begin
     (walk_ok step f' rest)
     end
| Error (uu___) -> begin
     false
     end)
     end))


let rec first_error = (fun ( step  :  step_fn<'e> ) ( f  :  wframe ) ( p  :  Prims.list<transform> ) -> (match (p) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (s)::rest -> begin
     (match ((step f s)) with
| Ok (f') -> begin
     (first_error step f' rest)
     end
| Error (err) -> begin
     FStar_Pervasives_Native.Some (err)
     end)
     end))


let rec cost = (fun ( step  :  step_fn<'e> ) ( f  :  wframe ) ( p  :  Prims.list<transform> ) -> (match (p) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (s)::rest -> begin
     (match ((step f s)) with
| Ok (f') -> begin
     ((cost_of f s) + (cost step f' rest))
     end
| Error (uu___) -> begin
     (cost_of f s)
     end)
     end))


let rec expr_nodes : col_expr  ->  Prims.nat = (fun ( e  :  col_expr ) -> (match (e) with
| Col (uu___) -> begin
     (Prims.parse_int "1")
     end
| Lit (uu___) -> begin
     (Prims.parse_int "1")
     end
| Param (uu___) -> begin
     (Prims.parse_int "1")
     end
| Now (uu___) -> begin
     (Prims.parse_int "1")
     end
| Binary (uu___, a, b) -> begin
     (((Prims.parse_int "1") + (expr_nodes a)) + (expr_nodes b))
     end
| Not (x) -> begin
     ((Prims.parse_int "1") + (expr_nodes x))
     end
| Cast (uu___, x) -> begin
     ((Prims.parse_int "1") + (expr_nodes x))
     end
| IsNull (x) -> begin
     ((Prims.parse_int "1") + (expr_nodes x))
     end
| InParam (x, uu___) -> begin
     ((Prims.parse_int "1") + (expr_nodes x))
     end
| Coalesce (xs) -> begin
     ((Prims.parse_int "1") + (exprs_nodes xs))
     end
| ApplyFn (uu___, xs) -> begin
     ((Prims.parse_int "1") + (exprs_nodes xs))
     end
| InList (x, items) -> begin
     (((Prims.parse_int "1") + (expr_nodes x)) + (exprs_nodes items))
     end
| Case (cases, els) -> begin
     (((Prims.parse_int "1") + (pairs_nodes cases)) + (expr_nodes els))
     end))
and exprs_nodes : Prims.list<col_expr>  ->  Prims.nat = (fun ( l  :  Prims.list<col_expr> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (x)::t -> begin
     ((expr_nodes x) + (exprs_nodes t))
     end))
and pairs_nodes : Prims.list<(col_expr * col_expr)>  ->  Prims.nat = (fun ( l  :  Prims.list<(col_expr * col_expr)> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| ((w, t))::r -> begin
     (((expr_nodes w) + (expr_nodes t)) + (pairs_nodes r))
     end))


let step_exprs : transform  ->  Prims.list<col_expr> = (fun ( s  :  transform ) -> (match (s) with
| Filter (e) -> begin
     (e)::[]
     end
| Derive (uu___, e) -> begin
     (e)::[]
     end
| uu___ -> begin
     []
     end))


let rec all_within : Prims.list<col_expr>  ->  Prims.bool = (fun ( l  :  Prims.list<col_expr> ) -> (match (l) with
| [] -> begin
     true
     end
| (e)::t -> begin
     (((expr_nodes e) <= Limits.max_expr_nodes) && (all_within t))
     end))


let rec within_limit : Prims.list<transform>  ->  Prims.bool = (fun ( p  :  Prims.list<transform> ) -> (match (p) with
| [] -> begin
     true
     end
| (s)::rest -> begin
     ((all_within (step_exprs s)) && (within_limit rest))
     end))


let rec expr_visits : prims  ->  param_env  ->  schema  ->  row_of  ->  col_expr  ->  Prims.nat = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( row  :  row_of ) ( x  :  col_expr ) -> (match (x) with
| Col (uu___) -> begin
     (Prims.parse_int "1")
     end
| Lit (uu___) -> begin
     (Prims.parse_int "1")
     end
| Param (uu___) -> begin
     (Prims.parse_int "1")
     end
| InParam (uu___, uu___1) -> begin
     (Prims.parse_int "1")
     end
| Now (uu___) -> begin
     (Prims.parse_int "1")
     end
| Binary (uu___, a, b) -> begin
     (((Prims.parse_int "1") + (expr_visits pr env cols row a)) + (match ((eval_expr pr env cols row a)) with
| Ok (uu___1) -> begin
     (expr_visits pr env cols row b)
     end
| Error (uu___1) -> begin
     (Prims.parse_int "0")
     end))
     end
| Not (a) -> begin
     ((Prims.parse_int "1") + (expr_visits pr env cols row a))
     end
| Cast (uu___, a) -> begin
     ((Prims.parse_int "1") + (expr_visits pr env cols row a))
     end
| IsNull (a) -> begin
     ((Prims.parse_int "1") + (expr_visits pr env cols row a))
     end
| Coalesce (xs) -> begin
     ((Prims.parse_int "1") + (coalesce_visits pr env cols row xs))
     end
| Case (cases, els) -> begin
     (((Prims.parse_int "1") + (case_visits pr env cols row cases)) + (match ((eval_case pr env cols row cases)) with
| FStar_Pervasives_Native.Some (uu___) -> begin
     (Prims.parse_int "0")
     end
| FStar_Pervasives_Native.None -> begin
     (expr_visits pr env cols row els)
     end))
     end
| InList (subject, items) -> begin
     (((Prims.parse_int "1") + (expr_visits pr env cols row subject)) + (match ((eval_expr pr env cols row subject)) with
| Error (uu___) -> begin
     (Prims.parse_int "0")
     end
| Ok (Null) -> begin
     (Prims.parse_int "0")
     end
| Ok (sv) -> begin
     (in_visits pr env cols row sv items)
     end))
     end
| ApplyFn (uu___, args) -> begin
     ((Prims.parse_int "1") + (args_visits pr env cols row args))
     end))
and coalesce_visits : prims  ->  param_env  ->  schema  ->  row_of  ->  Prims.list<col_expr>  ->  Prims.nat = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( row  :  row_of ) ( xs  :  Prims.list<col_expr> ) -> (match (xs) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (x)::rest -> begin
     ((expr_visits pr env cols row x) + (match ((eval_expr pr env cols row x)) with
| Ok (Null) -> begin
     (coalesce_visits pr env cols row rest)
     end
| uu___ -> begin
     (Prims.parse_int "0")
     end))
     end))
and case_visits : prims  ->  param_env  ->  schema  ->  row_of  ->  Prims.list<(col_expr * col_expr)>  ->  Prims.nat = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( row  :  row_of ) ( cases  :  Prims.list<(col_expr * col_expr)> ) -> (match (cases) with
| [] -> begin
     (Prims.parse_int "0")
     end
| ((when_e, then_e))::rest -> begin
     ((expr_visits pr env cols row when_e) + (match ((eval_expr pr env cols row when_e)) with
| Error (uu___) -> begin
     (Prims.parse_int "0")
     end
| Ok (Bool (true)) -> begin
     (expr_visits pr env cols row then_e)
     end
| Ok (uu___) -> begin
     (case_visits pr env cols row rest)
     end))
     end))
and in_visits : prims  ->  param_env  ->  schema  ->  row_of  ->  cell  ->  Prims.list<col_expr>  ->  Prims.nat = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( row  :  row_of ) ( sv  :  cell ) ( items  :  Prims.list<col_expr> ) -> (match (items) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (it)::rest -> begin
     ((expr_visits pr env cols row it) + (match ((eval_expr pr env cols row it)) with
| Error (uu___) -> begin
     (Prims.parse_int "0")
     end
| Ok (Null) -> begin
     (in_visits pr env cols row sv rest)
     end
| Ok (iv) -> begin
     (match ((pr.compare sv iv)) with
| FStar_Pervasives_Native.Some (uu___) when (uu___ = (Prims.parse_int "0")) -> begin
     (Prims.parse_int "0")
     end
| FStar_Pervasives_Native.Some (uu___) -> begin
     (in_visits pr env cols row sv rest)
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end)
     end))
     end))
and args_visits : prims  ->  param_env  ->  schema  ->  row_of  ->  Prims.list<col_expr>  ->  Prims.nat = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( row  :  row_of ) ( args  :  Prims.list<col_expr> ) -> (match (args) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (a)::rest -> begin
     ((expr_visits pr env cols row a) + (match ((eval_expr pr env cols row a)) with
| Ok (uu___) -> begin
     (args_visits pr env cols row rest)
     end
| Error (uu___) -> begin
     (Prims.parse_int "0")
     end))
     end))


let rec rows_visits : prims  ->  param_env  ->  schema  ->  Prims.list<Prims.list<cell>>  ->  col_expr  ->  Prims.nat = (fun ( pr  :  prims ) ( env  :  param_env ) ( cols  :  schema ) ( rows  :  Prims.list<Prims.list<cell>> ) ( x  :  col_expr ) -> (match (rows) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (r)::rest -> begin
     ((expr_visits pr env cols r x) + (match ((eval_expr pr env cols r x)) with
| Ok (uu___) -> begin
     (rows_visits pr env cols rest x)
     end
| Error (uu___) -> begin
     (Prims.parse_int "0")
     end))
     end))


let step_work : prims  ->  param_env  ->  wframe  ->  transform  ->  Prims.nat = (fun ( pr  :  prims ) ( env  :  param_env ) ( f  :  wframe ) ( s  :  transform ) -> (match (s) with
| Filter (e) -> begin
     (rows_visits pr env f.cols f.rows e)
     end
| Derive (uu___, e) -> begin
     (rows_visits pr env f.cols f.rows e)
     end
| uu___ -> begin
     (Prims.parse_int "0")
     end))


let rec work : prims  ->  other_fn  ->  param_env  ->  wframe  ->  Prims.list<transform>  ->  Prims.nat = (fun ( pr  :  prims ) ( other  :  other_fn ) ( env  :  param_env ) ( f  :  wframe ) ( p  :  Prims.list<transform> ) -> (match (p) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (s)::rest -> begin
     (match ((eval_step pr other env f s)) with
| Ok (f') -> begin
     ((step_work pr env f s) + (work pr other env f' rest))
     end
| Error (uu___) -> begin
     (step_work pr env f s)
     end)
     end))




