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


type step_fn<'e> = frame  ->  transform  ->  outcome<frame, 'e>


let rec go = (fun ( step  :  step_fn<'e> ) ( f  :  frame ) ( evaluated  :  Prims.nat ) ( p  :  Prims.list<transform> ) -> (match (p) with
| [] -> begin
     Ok (((f), (evaluated)))
     end
| (s)::rest -> begin
     (

let cost = (cost_of f s)
in (result_bind (step f s) (fun ( f'  :  frame ) -> (go step f' (evaluated + cost) rest))))
     end))


let eval_counted = (fun ( step  :  step_fn<'e> ) ( p  :  Prims.list<transform> ) ( input  :  frame ) -> (go step input (Prims.parse_int "0") p))


let eval_uncounted = (fun ( step  :  step_fn<'e> ) ( p  :  Prims.list<transform> ) ( input  :  frame ) -> (result_map FStar_Pervasives_Native.fst (eval_counted step p input)))


let rec walk_ok = (fun ( step  :  step_fn<'e> ) ( f  :  frame ) ( p  :  Prims.list<transform> ) -> (match (p) with
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


let rec first_error = (fun ( step  :  step_fn<'e> ) ( f  :  frame ) ( p  :  Prims.list<transform> ) -> (match (p) with
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


let rec cost = (fun ( step  :  step_fn<'e> ) ( f  :  frame ) ( p  :  Prims.list<transform> ) -> (match (p) with
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


let step_work : frame  ->  transform  ->  Prims.nat = (fun ( f  :  frame ) ( s  :  transform ) -> (match (s) with
| Filter (e) -> begin
     ((len f.rows) * (expr_nodes e))
     end
| Derive (uu___, e) -> begin
     ((len f.rows) * (expr_nodes e))
     end
| uu___ -> begin
     (Prims.parse_int "0")
     end))


let rec work = (fun ( step  :  step_fn<'e> ) ( f  :  frame ) ( p  :  Prims.list<transform> ) -> (match (p) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (s)::rest -> begin
     (match ((step f s)) with
| Ok (f') -> begin
     ((step_work f s) + (work step f' rest))
     end
| Error (uu___) -> begin
     (step_work f s)
     end)
     end))




