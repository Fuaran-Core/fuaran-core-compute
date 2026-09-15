module ColumnOps
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


let rec len = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     (Prims.parse_int "0")
     end
| (uu___)::t -> begin
     ((Prims.parse_int "1") + (len t))
     end))


let rec nth = (fun ( i  :  Prims.nat ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (x)::t -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     FStar_Pervasives_Native.Some (x)
     end else begin
     (nth (i - (Prims.parse_int "1")) t)
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


let rec take = (fun ( i  :  Prims.nat ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     []
     end else begin
     (x)::(take (i - (Prims.parse_int "1")) t)
     end
     end))


let rec drop = (fun ( i  :  Prims.nat ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (Prims.op_Equals i (Prims.parse_int "0")) then begin
     l
     end else begin
     (drop (i - (Prims.parse_int "1")) t)
     end
     end))


let rec app = (fun ( l  :  Prims.list<'a> ) ( m  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     m
     end
| (x)::t -> begin
     (x)::(app t m)
     end))


let rec rev = (fun ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     (app (rev t) ((x)::[]))
     end))


let insert_at = (fun ( i  :  Prims.nat ) ( x  :  'a ) ( l  :  Prims.list<'a> ) -> (app (take i l) ((x)::(drop i l))))


let rec mem : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.bool = (fun ( x  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     false
     end
| (y)::t -> begin
     ((Prims.op_Equals x y) || (mem x t))
     end))

type coltype =
| IntType
| FloatType
| BoolType
| StringType
| DateType
| TimestampType


let uu___is_IntType : coltype  ->  Prims.bool = (fun ( projectee  :  coltype ) -> (match (projectee) with
| IntType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_FloatType : coltype  ->  Prims.bool = (fun ( projectee  :  coltype ) -> (match (projectee) with
| FloatType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_BoolType : coltype  ->  Prims.bool = (fun ( projectee  :  coltype ) -> (match (projectee) with
| BoolType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_StringType : coltype  ->  Prims.bool = (fun ( projectee  :  coltype ) -> (match (projectee) with
| StringType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_DateType : coltype  ->  Prims.bool = (fun ( projectee  :  coltype ) -> (match (projectee) with
| DateType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TimestampType : coltype  ->  Prims.bool = (fun ( projectee  :  coltype ) -> (match (projectee) with
| TimestampType -> begin
     true
     end
| uu___ -> begin
     false
     end))


let tag : coltype  ->  Prims.string = (fun ( t  :  coltype ) -> (match (t) with
| IntType -> begin
     "int"
     end
| FloatType -> begin
     "float"
     end
| BoolType -> begin
     "bool"
     end
| StringType -> begin
     "string"
     end
| DateType -> begin
     "date"
     end
| TimestampType -> begin
     "timestamp"
     end))

type cell =
| Null
| Present of coltype * Prims.string


let uu___is_Null : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Null -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Present : cell  ->  Prims.bool = (fun ( projectee  :  cell ) -> (match (projectee) with
| Present (ty, carrier) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Present__item__ty : cell  ->  coltype = (fun ( projectee  :  cell ) -> (match (projectee) with
| Present (ty, carrier) -> begin
     ty
     end))


let __proj__Present__item__carrier : cell  ->  Prims.string = (fun ( projectee  :  cell ) -> (match (projectee) with
| Present (ty, carrier) -> begin
     carrier
     end))


let type_of : cell  ->  FStar_Pervasives_Native.option<coltype> = (fun ( c  :  cell ) -> (match (c) with
| Null -> begin
     FStar_Pervasives_Native.None
     end
| Present (ty, uu___) -> begin
     FStar_Pervasives_Native.Some (ty)
     end))

type column = {name : Prims.string; ty : coltype; cells : Prims.list<cell>}


let __proj__Mkcolumn__item__name : column  ->  Prims.string = (fun ( projectee  :  column ) -> (match (projectee) with
| {name = name; ty = ty; cells = cells} -> begin
     name
     end))


let __proj__Mkcolumn__item__ty : column  ->  coltype = (fun ( projectee  :  column ) -> (match (projectee) with
| {name = name; ty = ty; cells = cells} -> begin
     ty
     end))


let __proj__Mkcolumn__item__cells : column  ->  Prims.list<cell> = (fun ( projectee  :  column ) -> (match (projectee) with
| {name = name; ty = ty; cells = cells} -> begin
     cells
     end))

type table = {schema : Prims.list<(Prims.string * coltype)>; columns : Prims.list<column>}


let __proj__Mktable__item__schema : table  ->  Prims.list<(Prims.string * coltype)> = (fun ( projectee  :  table ) -> (match (projectee) with
| {schema = schema; columns = columns} -> begin
     schema
     end))


let __proj__Mktable__item__columns : table  ->  Prims.list<column> = (fun ( projectee  :  table ) -> (match (projectee) with
| {schema = schema; columns = columns} -> begin
     columns
     end))


let rec names_of : Prims.list<(Prims.string * coltype)>  ->  Prims.list<Prims.string> = (fun ( s  :  Prims.list<(Prims.string * coltype)> ) -> (match (s) with
| [] -> begin
     []
     end
| ((n, uu___))::r -> begin
     (n)::(names_of r)
     end))


let names : table  ->  Prims.list<Prims.string> = (fun ( t  :  table ) -> (names_of t.schema))


let row_count : table  ->  Prims.nat = (fun ( t  :  table ) -> (match (t.columns) with
| (c)::uu___ -> begin
     (len c.cells)
     end
| [] -> begin
     (Prims.parse_int "0")
     end))


let rec find_col : Prims.string  ->  Prims.list<column>  ->  FStar_Pervasives_Native.option<column> = (fun ( n  :  Prims.string ) ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::r -> begin
      
if (Prims.op_Equals c.name n) then begin
     FStar_Pervasives_Native.Some (c)
     end else begin
     (find_col n r)
     end
     end))


let rec find_index : Prims.string  ->  Prims.list<column>  ->  FStar_Pervasives_Native.option<Prims.nat> = (fun ( n  :  Prims.string ) ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::r -> begin
      
if (Prims.op_Equals c.name n) then begin
     FStar_Pervasives_Native.Some ((Prims.parse_int "0"))
     end else begin
     (match ((find_index n r)) with
| FStar_Pervasives_Native.Some (i) -> begin
     FStar_Pervasives_Native.Some ((i + (Prims.parse_int "1")))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
     end))


let exists_col : Prims.string  ->  Prims.list<column>  ->  Prims.bool = (fun ( n  :  Prims.string ) ( cs  :  Prims.list<column> ) -> (match ((find_col n cs)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))

type op =
| SetCell of Prims.string * Prims.int * cell
| SetColumn of column
| InsertColumn of Prims.int * column
| RemoveColumn of Prims.string
| AppendRows of Prims.list<Prims.list<(Prims.string * cell)>>
| ApplyTransform of Prims.string


let uu___is_SetCell : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| SetCell (column1, row, value) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SetCell__item__column : op  ->  Prims.string = (fun ( projectee  :  op ) -> (match (projectee) with
| SetCell (column1, row, value) -> begin
     column1
     end))


let __proj__SetCell__item__row : op  ->  Prims.int = (fun ( projectee  :  op ) -> (match (projectee) with
| SetCell (column1, row, value) -> begin
     row
     end))


let __proj__SetCell__item__value : op  ->  cell = (fun ( projectee  :  op ) -> (match (projectee) with
| SetCell (column1, row, value) -> begin
     value
     end))


let uu___is_SetColumn : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| SetColumn (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__SetColumn__item___0 : op  ->  column = (fun ( projectee  :  op ) -> (match (projectee) with
| SetColumn (_0) -> begin
     _0
     end))


let uu___is_InsertColumn : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| InsertColumn (index, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__InsertColumn__item__index : op  ->  Prims.int = (fun ( projectee  :  op ) -> (match (projectee) with
| InsertColumn (index, _1) -> begin
     index
     end))


let __proj__InsertColumn__item___1 : op  ->  column = (fun ( projectee  :  op ) -> (match (projectee) with
| InsertColumn (index, _1) -> begin
     _1
     end))


let uu___is_RemoveColumn : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| RemoveColumn (name) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RemoveColumn__item__name : op  ->  Prims.string = (fun ( projectee  :  op ) -> (match (projectee) with
| RemoveColumn (name) -> begin
     name
     end))


let uu___is_AppendRows : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| AppendRows (rows) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__AppendRows__item__rows : op  ->  Prims.list<Prims.list<(Prims.string * cell)>> = (fun ( projectee  :  op ) -> (match (projectee) with
| AppendRows (rows) -> begin
     rows
     end))


let uu___is_ApplyTransform : op  ->  Prims.bool = (fun ( projectee  :  op ) -> (match (projectee) with
| ApplyTransform (pipeline) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ApplyTransform__item__pipeline : op  ->  Prims.string = (fun ( projectee  :  op ) -> (match (projectee) with
| ApplyTransform (pipeline) -> begin
     pipeline
     end))

type rejection =
| NoSuchColumn of Prims.string * Prims.list<Prims.string>
| DuplicateColumn of Prims.string
| RowOutOfRange of Prims.int * Prims.nat
| CellTypeMismatch of Prims.string * Prims.string * Prims.string
| ColumnLengthMismatch of Prims.string * Prims.nat * Prims.nat
| RowShapeUnknownColumn of Prims.string * Prims.list<Prims.string>
| TransformRejected of Prims.string
| NotInvertible of Prims.string


let uu___is_NoSuchColumn : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| NoSuchColumn (name, available) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NoSuchColumn__item__name : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| NoSuchColumn (name, available) -> begin
     name
     end))


let __proj__NoSuchColumn__item__available : rejection  ->  Prims.list<Prims.string> = (fun ( projectee  :  rejection ) -> (match (projectee) with
| NoSuchColumn (name, available) -> begin
     available
     end))


let uu___is_DuplicateColumn : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| DuplicateColumn (name) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DuplicateColumn__item__name : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| DuplicateColumn (name) -> begin
     name
     end))


let uu___is_RowOutOfRange : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| RowOutOfRange (row, rowCount) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RowOutOfRange__item__row : rejection  ->  Prims.int = (fun ( projectee  :  rejection ) -> (match (projectee) with
| RowOutOfRange (row, rowCount) -> begin
     row
     end))


let __proj__RowOutOfRange__item__rowCount : rejection  ->  Prims.nat = (fun ( projectee  :  rejection ) -> (match (projectee) with
| RowOutOfRange (row, rowCount) -> begin
     rowCount
     end))


let uu___is_CellTypeMismatch : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| CellTypeMismatch (column1, expected, got) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CellTypeMismatch__item__column : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| CellTypeMismatch (column1, expected, got) -> begin
     column1
     end))


let __proj__CellTypeMismatch__item__expected : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| CellTypeMismatch (column1, expected, got) -> begin
     expected
     end))


let __proj__CellTypeMismatch__item__got : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| CellTypeMismatch (column1, expected, got) -> begin
     got
     end))


let uu___is_ColumnLengthMismatch : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| ColumnLengthMismatch (column1, expected, got) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ColumnLengthMismatch__item__column : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| ColumnLengthMismatch (column1, expected, got) -> begin
     column1
     end))


let __proj__ColumnLengthMismatch__item__expected : rejection  ->  Prims.nat = (fun ( projectee  :  rejection ) -> (match (projectee) with
| ColumnLengthMismatch (column1, expected, got) -> begin
     expected
     end))


let __proj__ColumnLengthMismatch__item__got : rejection  ->  Prims.nat = (fun ( projectee  :  rejection ) -> (match (projectee) with
| ColumnLengthMismatch (column1, expected, got) -> begin
     got
     end))


let uu___is_RowShapeUnknownColumn : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| RowShapeUnknownColumn (name, available) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RowShapeUnknownColumn__item__name : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| RowShapeUnknownColumn (name, available) -> begin
     name
     end))


let __proj__RowShapeUnknownColumn__item__available : rejection  ->  Prims.list<Prims.string> = (fun ( projectee  :  rejection ) -> (match (projectee) with
| RowShapeUnknownColumn (name, available) -> begin
     available
     end))


let uu___is_TransformRejected : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| TransformRejected (detail) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TransformRejected__item__detail : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| TransformRejected (detail) -> begin
     detail
     end))


let uu___is_NotInvertible : rejection  ->  Prims.bool = (fun ( projectee  :  rejection ) -> (match (projectee) with
| NotInvertible (op1) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__NotInvertible__item__op : rejection  ->  Prims.string = (fun ( projectee  :  rejection ) -> (match (projectee) with
| NotInvertible (op1) -> begin
     op1
     end))


type evaluator = Prims.string  ->  table  ->  outcome<table, Prims.string>


let cell_type_name : cell  ->  Prims.string = (fun ( c  :  cell ) -> (match ((type_of c)) with
| FStar_Pervasives_Native.Some (t) -> begin
     (tag t)
     end
| FStar_Pervasives_Native.None -> begin
     "null"
     end))


let cell_fits : Prims.string  ->  coltype  ->  cell  ->  outcome<unit, rejection> = (fun ( col_name  :  Prims.string ) ( ty  :  coltype ) ( c  :  cell ) -> (match (c) with
| Null -> begin
     Ok (())
     end
| uu___ -> begin
     (match ((type_of c)) with
| FStar_Pervasives_Native.Some (t) -> begin
      
if (Prims.op_Equals t ty) then begin
     Ok (())
     end else begin
     Error (CellTypeMismatch (col_name, (tag ty), (cell_type_name c)))
     end
     end
| FStar_Pervasives_Native.None -> begin
     Error (CellTypeMismatch (col_name, (tag ty), (cell_type_name c)))
     end)
     end))


let rec cells_fit_from : Prims.string  ->  coltype  ->  Prims.list<cell>  ->  outcome<unit, rejection> = (fun ( col_name  :  Prims.string ) ( ty  :  coltype ) ( cs  :  Prims.list<cell> ) -> (match (cs) with
| [] -> begin
     Ok (())
     end
| (c)::r -> begin
     (match ((cell_fits col_name ty c)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     (cells_fit_from col_name ty r)
     end)
     end))


let cells_fit : column  ->  outcome<unit, rejection> = (fun ( col  :  column ) -> (cells_fit_from col.name col.ty col.cells))


let rec replace_schema : Prims.string  ->  coltype  ->  Prims.list<(Prims.string * coltype)>  ->  Prims.list<(Prims.string * coltype)> = (fun ( n  :  Prims.string ) ( ty  :  coltype ) ( s  :  Prims.list<(Prims.string * coltype)> ) -> (match (s) with
| [] -> begin
     []
     end
| ((m, t))::r -> begin
     ( 
if (Prims.op_Equals m n) then begin
     ((m), (ty))
     end else begin
     ((m), (t))
     end)::(replace_schema n ty r)
     end))


let rec replace_cols : Prims.string  ->  column  ->  Prims.list<column>  ->  Prims.list<column> = (fun ( n  :  Prims.string ) ( nc  :  column ) ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::r -> begin
     ( 
if (Prims.op_Equals c.name n) then begin
     nc
     end else begin
     c
     end)::(replace_cols n nc r)
     end))


let replace_column : Prims.string  ->  column  ->  table  ->  table = (fun ( n  :  Prims.string ) ( nc  :  column ) ( t  :  table ) -> {schema = (replace_schema n nc.ty t.schema); columns = (replace_cols n nc t.columns)})


let clamp : Prims.int  ->  Prims.nat  ->  Prims.nat = (fun ( i  :  Prims.int ) ( n  :  Prims.nat ) ->  
if (i < (Prims.parse_int "0")) then begin
     (Prims.parse_int "0")
     end else begin
      
if (i > n) then begin
     n
     end else begin
     i
     end
     end)


let insert_column_at : Prims.int  ->  column  ->  table  ->  table = (fun ( index  :  Prims.int ) ( col  :  column ) ( t  :  table ) -> (

let i = (clamp index (len t.columns))
in {schema = (insert_at i ((col.name), (col.ty)) t.schema); columns = (insert_at i col t.columns)}))


let rec remove_schema : Prims.string  ->  Prims.list<(Prims.string * coltype)>  ->  Prims.list<(Prims.string * coltype)> = (fun ( n  :  Prims.string ) ( s  :  Prims.list<(Prims.string * coltype)> ) -> (match (s) with
| [] -> begin
     []
     end
| ((m, t))::r -> begin
      
if (Prims.op_Equals m n) then begin
     (remove_schema n r)
     end else begin
     (((m), (t)))::(remove_schema n r)
     end
     end))


let rec remove_cols : Prims.string  ->  Prims.list<column>  ->  Prims.list<column> = (fun ( n  :  Prims.string ) ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::r -> begin
      
if (Prims.op_Equals c.name n) then begin
     (remove_cols n r)
     end else begin
     (c)::(remove_cols n r)
     end
     end))


let remove_column : Prims.string  ->  table  ->  table = (fun ( n  :  Prims.string ) ( t  :  table ) -> {schema = (remove_schema n t.schema); columns = (remove_cols n t.columns)})


let rec row_fault : table  ->  Prims.list<(Prims.string * cell)>  ->  FStar_Pervasives_Native.option<rejection> = (fun ( t  :  table ) ( row  :  Prims.list<(Prims.string * cell)> ) -> (match (row) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| ((n, v))::rest -> begin
      
if (not ((mem n (names t)))) then begin
     FStar_Pervasives_Native.Some (RowShapeUnknownColumn (n, (names t)))
     end else begin
     (match ((find_col n t.columns)) with
| FStar_Pervasives_Native.Some (col) -> begin
     (match ((cell_fits n col.ty v)) with
| Error (e) -> begin
     FStar_Pervasives_Native.Some (e)
     end
| Ok (()) -> begin
     (row_fault t rest)
     end)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some (RowShapeUnknownColumn (n, (names t)))
     end)
     end
     end))


let rec first_fault : table  ->  Prims.list<Prims.list<(Prims.string * cell)>>  ->  FStar_Pervasives_Native.option<rejection> = (fun ( t  :  table ) ( rows  :  Prims.list<Prims.list<(Prims.string * cell)>> ) -> (match (rows) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (row)::rest -> begin
     (match ((row_fault t row)) with
| FStar_Pervasives_Native.Some (e) -> begin
     FStar_Pervasives_Native.Some (e)
     end
| FStar_Pervasives_Native.None -> begin
     (first_fault t rest)
     end)
     end))


let rec lookup : Prims.string  ->  Prims.list<(Prims.string * cell)>  ->  cell = (fun ( n  :  Prims.string ) ( row  :  Prims.list<(Prims.string * cell)> ) -> (match (row) with
| [] -> begin
     Null
     end
| ((m, v))::rest -> begin
      
if (Prims.op_Equals m n) then begin
     v
     end else begin
     (lookup n rest)
     end
     end))


let rec appended : Prims.string  ->  Prims.list<Prims.list<(Prims.string * cell)>>  ->  Prims.list<cell> = (fun ( n  :  Prims.string ) ( rows  :  Prims.list<Prims.list<(Prims.string * cell)>> ) -> (match (rows) with
| [] -> begin
     []
     end
| (row)::rest -> begin
     ((lookup n row))::(appended n rest)
     end))


let rec append_cols : Prims.list<Prims.list<(Prims.string * cell)>>  ->  Prims.list<column>  ->  Prims.list<column> = (fun ( rows  :  Prims.list<Prims.list<(Prims.string * cell)>> ) ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::r -> begin
     ({name = c.name; ty = c.ty; cells = (app c.cells (appended c.name rows))})::(append_cols rows r)
     end))


let apply : evaluator  ->  op  ->  table  ->  outcome<table, rejection> = (fun ( ev  :  evaluator ) ( o  :  op ) ( t  :  table ) -> (match (o) with
| SetCell (n, row, value) -> begin
     (match ((find_col n t.columns)) with
| FStar_Pervasives_Native.None -> begin
     Error (NoSuchColumn (n, (names t)))
     end
| FStar_Pervasives_Native.Some (col) -> begin
     (

let rc = (row_count t)
in  
if ((row < (Prims.parse_int "0")) || (row >= rc)) then begin
     Error (RowOutOfRange (row, rc))
     end else begin
     (match ((cell_fits n col.ty value)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     Ok ((replace_column n {name = col.name; ty = col.ty; cells = (set_at row value col.cells)} t))
     end)
     end)
     end)
     end
| SetColumn (nc) -> begin
     (match ((find_col nc.name t.columns)) with
| FStar_Pervasives_Native.None -> begin
     Error (NoSuchColumn (nc.name, (names t)))
     end
| FStar_Pervasives_Native.Some (uu___) -> begin
     (

let rc = (row_count t)
in  
if (Prims.op_Less_Greater (len nc.cells) rc) then begin
     Error (ColumnLengthMismatch (nc.name, rc, (len nc.cells)))
     end else begin
     (match ((cells_fit nc)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     Ok ((replace_column nc.name nc t))
     end)
     end)
     end)
     end
| InsertColumn (index, col) -> begin
      
if (exists_col col.name t.columns) then begin
     Error (DuplicateColumn (col.name))
     end else begin
     (

let rc = (row_count t)
in (

let has_cols = (match (t.columns) with
| (hd)::tl -> begin
     true
     end
| uu___ -> begin
     false
     end)
in  
if (has_cols && (Prims.op_Less_Greater (len col.cells) rc)) then begin
     Error (ColumnLengthMismatch (col.name, rc, (len col.cells)))
     end else begin
     (match ((cells_fit col)) with
| Error (e) -> begin
     Error (e)
     end
| Ok (()) -> begin
     Ok ((insert_column_at index col t))
     end)
     end))
     end
     end
| RemoveColumn (n) -> begin
      
if (exists_col n t.columns) then begin
     Ok ((remove_column n t))
     end else begin
     Error (NoSuchColumn (n, (names t)))
     end
     end
| AppendRows (rows) -> begin
     (match ((first_fault t rows)) with
| FStar_Pervasives_Native.Some (e) -> begin
     Error (e)
     end
| FStar_Pervasives_Native.None -> begin
     Ok ({schema = t.schema; columns = (append_cols rows t.columns)})
     end)
     end
| ApplyTransform (p) -> begin
     (match ((ev p t)) with
| Ok (t') -> begin
     Ok (t')
     end
| Error (e) -> begin
     Error (TransformRejected (e))
     end)
     end))


let can_apply : evaluator  ->  op  ->  table  ->  outcome<unit, rejection> = (fun ( ev  :  evaluator ) ( o  :  op ) ( t  :  table ) -> (match ((apply ev o t)) with
| Ok (uu___) -> begin
     Ok (())
     end
| Error (e) -> begin
     Error (e)
     end))


let invert : op  ->  table  ->  outcome<op, rejection> = (fun ( o  :  op ) ( t  :  table ) -> (match (o) with
| SetCell (n, row, uu___) -> begin
     (match ((find_col n t.columns)) with
| FStar_Pervasives_Native.None -> begin
     Error (NoSuchColumn (n, (names t)))
     end
| FStar_Pervasives_Native.Some (col) -> begin
     (

let rc = (row_count t)
in  
if ((row < (Prims.parse_int "0")) || (row >= rc)) then begin
     Error (RowOutOfRange (row, rc))
     end else begin
     (match ((nth row col.cells)) with
| FStar_Pervasives_Native.Some (old) -> begin
     Ok (SetCell (n, row, old))
     end
| FStar_Pervasives_Native.None -> begin
     Ok (SetCell (n, row, Null))
     end)
     end)
     end)
     end
| SetColumn (nc) -> begin
     (match ((find_col nc.name t.columns)) with
| FStar_Pervasives_Native.None -> begin
     Error (NoSuchColumn (nc.name, (names t)))
     end
| FStar_Pervasives_Native.Some (old) -> begin
     Ok (SetColumn (old))
     end)
     end
| InsertColumn (uu___, col) -> begin
     Ok (RemoveColumn (col.name))
     end
| RemoveColumn (n) -> begin
     (match ((find_index n t.columns)) with
| FStar_Pervasives_Native.None -> begin
     Error (NoSuchColumn (n, (names t)))
     end
| FStar_Pervasives_Native.Some (idx) -> begin
     (match ((nth idx t.columns)) with
| FStar_Pervasives_Native.Some (c) -> begin
     Ok (InsertColumn (idx, c))
     end
| FStar_Pervasives_Native.None -> begin
     Error (NoSuchColumn (n, (names t)))
     end)
     end)
     end
| AppendRows (uu___) -> begin
     Error (NotInvertible ("AppendRows"))
     end
| ApplyTransform (uu___) -> begin
     Error (NotInvertible ("ApplyTransform"))
     end))


let rec apply_all : evaluator  ->  Prims.list<op>  ->  table  ->  outcome<table, rejection> = (fun ( ev  :  evaluator ) ( os  :  Prims.list<op> ) ( t  :  table ) -> (match (os) with
| [] -> begin
     Ok (t)
     end
| (o)::r -> begin
     (match ((apply ev o t)) with
| Ok (t') -> begin
     (apply_all ev r t')
     end
| Error (e) -> begin
     Error (e)
     end)
     end))


let rec changed_cols : Prims.list<column>  ->  Prims.list<column>  ->  Prims.list<op> = (fun ( bcs  :  Prims.list<column> ) ( acs  :  Prims.list<column> ) -> (match (acs) with
| [] -> begin
     []
     end
| (ac)::r -> begin
     (match ((find_col ac.name bcs)) with
| FStar_Pervasives_Native.Some (bc) -> begin
      
if (Prims.op_Less_Greater bc.cells ac.cells) then begin
     (SetColumn (ac))::(changed_cols bcs r)
     end else begin
     (changed_cols bcs r)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (changed_cols bcs r)
     end)
     end))


let rec removes : Prims.list<column>  ->  Prims.list<op> = (fun ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::r -> begin
     (RemoveColumn (c.name))::(removes r)
     end))


let rec inserts_from : Prims.nat  ->  Prims.list<column>  ->  Prims.list<op> = (fun ( i  :  Prims.nat ) ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::r -> begin
     (InsertColumn (i, c))::(inserts_from (i + (Prims.parse_int "1")) r)
     end))


let to_ops : table  ->  table  ->  Prims.list<op> = (fun ( before  :  table ) ( after  :  table ) ->  
if ((Prims.op_Equals before.schema after.schema) && (Prims.op_Equals (row_count before) (row_count after))) then begin
     (changed_cols before.columns after.columns)
     end else begin
     (app (removes (rev before.columns)) (inserts_from (Prims.parse_int "0") after.columns))
     end)


let raisable : op  ->  rejection  ->  Prims.bool = (fun ( o  :  op ) ( e  :  rejection ) -> (match (((o), (e))) with
| (SetCell (uu___, uu___1, uu___2), NoSuchColumn (uu___3, uu___4)) -> begin
     true
     end
| (SetCell (uu___, uu___1, uu___2), RowOutOfRange (uu___3, uu___4)) -> begin
     true
     end
| (SetCell (uu___, uu___1, uu___2), CellTypeMismatch (uu___3, uu___4, uu___5)) -> begin
     true
     end
| (SetColumn (uu___), NoSuchColumn (uu___1, uu___2)) -> begin
     true
     end
| (SetColumn (uu___), ColumnLengthMismatch (uu___1, uu___2, uu___3)) -> begin
     true
     end
| (SetColumn (uu___), CellTypeMismatch (uu___1, uu___2, uu___3)) -> begin
     true
     end
| (InsertColumn (uu___, uu___1), DuplicateColumn (uu___2)) -> begin
     true
     end
| (InsertColumn (uu___, uu___1), ColumnLengthMismatch (uu___2, uu___3, uu___4)) -> begin
     true
     end
| (InsertColumn (uu___, uu___1), CellTypeMismatch (uu___2, uu___3, uu___4)) -> begin
     true
     end
| (RemoveColumn (uu___), NoSuchColumn (uu___1, uu___2)) -> begin
     true
     end
| (AppendRows (uu___), RowShapeUnknownColumn (uu___1, uu___2)) -> begin
     true
     end
| (AppendRows (uu___), CellTypeMismatch (uu___1, uu___2, uu___3)) -> begin
     true
     end
| (ApplyTransform (uu___), TransformRejected (uu___1)) -> begin
     true
     end
| (uu___, uu___1) -> begin
     false
     end))


let state_after : evaluator  ->  op  ->  table  ->  table = (fun ( ev  :  evaluator ) ( o  :  op ) ( t  :  table ) -> (match ((apply ev o t)) with
| Ok (t') -> begin
     t'
     end
| Error (uu___) -> begin
     t
     end))


let state_after_all : evaluator  ->  Prims.list<op>  ->  table  ->  table = (fun ( ev  :  evaluator ) ( os  :  Prims.list<op> ) ( t  :  table ) -> (match ((apply_all ev os t)) with
| Ok (t') -> begin
     t'
     end
| Error (uu___) -> begin
     t
     end))


let rec col_names : Prims.list<column>  ->  Prims.list<Prims.string> = (fun ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::r -> begin
     (c.name)::(col_names r)
     end))


let rec schema_of : Prims.list<column>  ->  Prims.list<(Prims.string * coltype)> = (fun ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     []
     end
| (c)::r -> begin
     (((c.name), (c.ty)))::(schema_of r)
     end))


let rec no_dups : Prims.list<Prims.string>  ->  Prims.bool = (fun ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::r -> begin
     ((not ((mem x r))) && (no_dups r))
     end))


let rec uniform : Prims.nat  ->  Prims.list<column>  ->  Prims.bool = (fun ( n  :  Prims.nat ) ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     true
     end
| (c)::r -> begin
     ((Prims.op_Equals (len c.cells) n) && (uniform n r))
     end))


let rec typed : Prims.list<column>  ->  Prims.bool = (fun ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     true
     end
| (c)::r -> begin
     ((match ((cells_fit c)) with
| Ok (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end) && (typed r))
     end))


let coherent : table  ->  Prims.bool = (fun ( t  :  table ) -> (Prims.op_Equals t.schema (schema_of t.columns)))


let wf : table  ->  Prims.bool = (fun ( t  :  table ) -> ((((coherent t) && (no_dups (col_names t.columns))) && (uniform (row_count t) t.columns)) && (typed t.columns)))


let structural : op  ->  Prims.bool = (fun ( o  :  op ) -> (not ((match (o) with
| ApplyTransform (pipeline) -> begin
     true
     end
| uu___ -> begin
     false
     end))))


let rec remove_name : Prims.string  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( n  :  Prims.string ) ( l  :  Prims.list<Prims.string> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::r -> begin
      
if (Prims.op_Equals x n) then begin
     (remove_name n r)
     end else begin
     (x)::(remove_name n r)
     end
     end))


let invertible : op  ->  Prims.bool = (fun ( o  :  op ) -> ((((match (o) with
| SetCell (column1, row, value) -> begin
     true
     end
| uu___ -> begin
     false
     end) || (match (o) with
| SetColumn (_0) -> begin
     true
     end
| uu___ -> begin
     false
     end)) || (match (o) with
| InsertColumn (index, _1) -> begin
     true
     end
| uu___ -> begin
     false
     end)) || (match (o) with
| RemoveColumn (name) -> begin
     true
     end
| uu___ -> begin
     false
     end)))


let rec memc : column  ->  Prims.list<column>  ->  Prims.bool = (fun ( c  :  column ) ( cs  :  Prims.list<column> ) -> (match (cs) with
| [] -> begin
     false
     end
| (d)::r -> begin
     ((Prims.op_Equals c d) || (memc c r))
     end))


let rec remove_names : Prims.list<Prims.string>  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.string> = (fun ( ns  :  Prims.list<Prims.string> ) ( l  :  Prims.list<Prims.string> ) -> (match (ns) with
| [] -> begin
     l
     end
| (n)::r -> begin
     (remove_names r (remove_name n l))
     end))


let empty_table : table = {schema = []; columns = []}




