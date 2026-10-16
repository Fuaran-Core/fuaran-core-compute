module Frame

let rec lmap = (fun ( g  :  'a  ->  'b ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((g x))::(lmap g t)
     end))


let rec lfilter = (fun ( g  :  'a  ->  Prims.bool ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (g x) then begin
     (x)::(lfilter g t)
     end else begin
     (lfilter g t)
     end
     end))


let rec lfor_all = (fun ( g  :  'a  ->  Prims.bool ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((g x) && (lfor_all g t))
     end))


let rec lmap2 = (fun ( g  :  'a  ->  'b  ->  'c ) ( l  :  Prims.list<'a> ) ( m  :  Prims.list<'b> ) -> (match (((l), (m))) with
| ((x)::t, (y)::u) -> begin
     ((g x y))::(lmap2 g t u)
     end
| uu___ -> begin
     []
     end))


let rec upto_from : Prims.nat  ->  Prims.nat  ->  Prims.list<Prims.nat> = (fun ( k  :  Prims.nat ) ( n  :  Prims.nat ) ->  
if (k >= n) then begin
     []
     end else begin
     (k)::(upto_from (k + (Prims.parse_int "1")) n)
     end)


let upto : Prims.nat  ->  Prims.list<Prims.nat> = (fun ( n  :  Prims.nat ) -> (upto_from (Prims.parse_int "0") n))


let rec vnth = (fun ( l  :  Prims.list<'a> ) ( p  :  Prims.nat ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (x)::t -> begin
      
if (Prims.op_Equals p (Prims.parse_int "0")) then begin
     FStar_Pervasives_Native.Some (x)
     end else begin
     (vnth t (p - (Prims.parse_int "1")))
     end
     end))


let at_or = (fun ( d  :  'a ) ( l  :  Prims.list<'a> ) ( p  :  Prims.nat ) -> (match ((vnth l p)) with
| FStar_Pervasives_Native.Some (x) -> begin
     x
     end
| FStar_Pervasives_Native.None -> begin
     d
     end))


let bit : Prims.list<Prims.bool>  ->  Prims.nat  ->  Prims.bool = (fun ( m  :  Prims.list<Prims.bool> ) ( p  :  Prims.nat ) -> (at_or false m p))

type time_unit =
| Seconds
| Millis
| Micros
| Nanos


let uu___is_Seconds : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Seconds -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Millis : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Millis -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Micros : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Micros -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Nanos : time_unit  ->  Prims.bool = (fun ( projectee  :  time_unit ) -> (match (projectee) with
| Nanos -> begin
     true
     end
| uu___ -> begin
     false
     end))

type ftype =
| TInt
| TFloat
| TBool
| TString
| TDate
| TStamp of time_unit
| TDecimal


let uu___is_TInt : ftype  ->  Prims.bool = (fun ( projectee  :  ftype ) -> (match (projectee) with
| TInt -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TFloat : ftype  ->  Prims.bool = (fun ( projectee  :  ftype ) -> (match (projectee) with
| TFloat -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TBool : ftype  ->  Prims.bool = (fun ( projectee  :  ftype ) -> (match (projectee) with
| TBool -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TString : ftype  ->  Prims.bool = (fun ( projectee  :  ftype ) -> (match (projectee) with
| TString -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TDate : ftype  ->  Prims.bool = (fun ( projectee  :  ftype ) -> (match (projectee) with
| TDate -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TStamp : ftype  ->  Prims.bool = (fun ( projectee  :  ftype ) -> (match (projectee) with
| TStamp (u) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__TStamp__item__u : ftype  ->  time_unit = (fun ( projectee  :  ftype ) -> (match (projectee) with
| TStamp (u) -> begin
     u
     end))


let uu___is_TDecimal : ftype  ->  Prims.bool = (fun ( projectee  :  ftype ) -> (match (projectee) with
| TDecimal -> begin
     true
     end
| uu___ -> begin
     false
     end))


let erase : ftype  ->  Pipeline.column_type = (fun ( t  :  ftype ) -> (match (t) with
| TInt -> begin
     Pipeline.IntType
     end
| TFloat -> begin
     Pipeline.FloatType
     end
| TBool -> begin
     Pipeline.BoolType
     end
| TString -> begin
     Pipeline.StringType
     end
| TDate -> begin
     Pipeline.DateType
     end
| TStamp (uu___) -> begin
     Pipeline.TimestampType
     end
| TDecimal -> begin
     Pipeline.DecimalType
     end))

type validity =
| AllValid
| VMask of Prims.list<Prims.bool>


let uu___is_AllValid : validity  ->  Prims.bool = (fun ( projectee  :  validity ) -> (match (projectee) with
| AllValid -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_VMask : validity  ->  Prims.bool = (fun ( projectee  :  validity ) -> (match (projectee) with
| VMask (present) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VMask__item__present : validity  ->  Prims.list<Prims.bool> = (fun ( projectee  :  validity ) -> (match (projectee) with
| VMask (present) -> begin
     present
     end))

type cdata =
| DInts of Prims.list<Prims.int> * validity
| DFloats of Prims.list<Prims.string> * validity
| DBools of Prims.list<Prims.bool> * validity
| DStrs of Prims.list<Prims.string> * validity
| DDates of Prims.list<Prims.int> * validity
| DStamps of time_unit * Prims.list<Prims.int> * FStar_Pervasives_Native.option<Prims.list<Prims.int>> * validity
| DDecimals of Prims.list<Prims.string> * validity


let uu___is_DInts : cdata  ->  Prims.bool = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DInts (values, v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DInts__item__values : cdata  ->  Prims.list<Prims.int> = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DInts (values, v) -> begin
     values
     end))


let __proj__DInts__item__v : cdata  ->  validity = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DInts (values, v) -> begin
     v
     end))


let uu___is_DFloats : cdata  ->  Prims.bool = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DFloats (values, v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DFloats__item__values : cdata  ->  Prims.list<Prims.string> = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DFloats (values, v) -> begin
     values
     end))


let __proj__DFloats__item__v : cdata  ->  validity = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DFloats (values, v) -> begin
     v
     end))


let uu___is_DBools : cdata  ->  Prims.bool = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DBools (values, v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DBools__item__values : cdata  ->  Prims.list<Prims.bool> = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DBools (values, v) -> begin
     values
     end))


let __proj__DBools__item__v : cdata  ->  validity = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DBools (values, v) -> begin
     v
     end))


let uu___is_DStrs : cdata  ->  Prims.bool = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DStrs (values, v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DStrs__item__values : cdata  ->  Prims.list<Prims.string> = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DStrs (values, v) -> begin
     values
     end))


let __proj__DStrs__item__v : cdata  ->  validity = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DStrs (values, v) -> begin
     v
     end))


let uu___is_DDates : cdata  ->  Prims.bool = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DDates (values, v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DDates__item__values : cdata  ->  Prims.list<Prims.int> = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DDates (values, v) -> begin
     values
     end))


let __proj__DDates__item__v : cdata  ->  validity = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DDates (values, v) -> begin
     v
     end))


let uu___is_DStamps : cdata  ->  Prims.bool = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DStamps (u, seconds, fraction, v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DStamps__item__u : cdata  ->  time_unit = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DStamps (u, seconds, fraction, v) -> begin
     u
     end))


let __proj__DStamps__item__seconds : cdata  ->  Prims.list<Prims.int> = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DStamps (u, seconds, fraction, v) -> begin
     seconds
     end))


let __proj__DStamps__item__fraction : cdata  ->  FStar_Pervasives_Native.option<Prims.list<Prims.int>> = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DStamps (u, seconds, fraction, v) -> begin
     fraction
     end))


let __proj__DStamps__item__v : cdata  ->  validity = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DStamps (u, seconds, fraction, v) -> begin
     v
     end))


let uu___is_DDecimals : cdata  ->  Prims.bool = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DDecimals (values, v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__DDecimals__item__values : cdata  ->  Prims.list<Prims.string> = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DDecimals (values, v) -> begin
     values
     end))


let __proj__DDecimals__item__v : cdata  ->  validity = (fun ( projectee  :  cdata ) -> (match (projectee) with
| DDecimals (values, v) -> begin
     v
     end))

type ccolumn = {cname : Prims.string; cdata : cdata}


let __proj__Mkccolumn__item__cname : ccolumn  ->  Prims.string = (fun ( projectee  :  ccolumn ) -> (match (projectee) with
| {cname = cname; cdata = cdata1} -> begin
     cname
     end))


let __proj__Mkccolumn__item__cdata : ccolumn  ->  cdata = (fun ( projectee  :  ccolumn ) -> (match (projectee) with
| {cname = cname; cdata = cdata1} -> begin
     cdata1
     end))

type ctable = {tschema : Prims.list<(Prims.string * ftype)>; tcolumns : Prims.list<ccolumn>}


let __proj__Mkctable__item__tschema : ctable  ->  Prims.list<(Prims.string * ftype)> = (fun ( projectee  :  ctable ) -> (match (projectee) with
| {tschema = tschema; tcolumns = tcolumns} -> begin
     tschema
     end))


let __proj__Mkctable__item__tcolumns : ctable  ->  Prims.list<ccolumn> = (fun ( projectee  :  ctable ) -> (match (projectee) with
| {tschema = tschema; tcolumns = tcolumns} -> begin
     tcolumns
     end))


let ctype : ccolumn  ->  ftype = (fun ( c  :  ccolumn ) -> (match (c.cdata) with
| DInts (uu___, uu___1) -> begin
     TInt
     end
| DFloats (uu___, uu___1) -> begin
     TFloat
     end
| DBools (uu___, uu___1) -> begin
     TBool
     end
| DStrs (uu___, uu___1) -> begin
     TString
     end
| DDates (uu___, uu___1) -> begin
     TDate
     end
| DStamps (u, uu___, uu___1, uu___2) -> begin
     TStamp (u)
     end
| DDecimals (uu___, uu___1) -> begin
     TDecimal
     end))


let clength : ccolumn  ->  Prims.nat = (fun ( c  :  ccolumn ) -> (match (c.cdata) with
| DInts (xs, uu___) -> begin
     (Pipeline.len xs)
     end
| DFloats (xs, uu___) -> begin
     (Pipeline.len xs)
     end
| DBools (xs, uu___) -> begin
     (Pipeline.len xs)
     end
| DStrs (xs, uu___) -> begin
     (Pipeline.len xs)
     end
| DDates (xs, uu___) -> begin
     (Pipeline.len xs)
     end
| DStamps (uu___, xs, uu___1, uu___2) -> begin
     (Pipeline.len xs)
     end
| DDecimals (xs, uu___) -> begin
     (Pipeline.len xs)
     end))

type host = {date_text : Prims.int  ->  Prims.string; instant_text : time_unit  ->  Prims.int  ->  Prims.int  ->  Prims.string; try_days : Prims.string  ->  FStar_Pervasives_Native.option<Prims.int>; try_instant : time_unit  ->  Prims.string  ->  FStar_Pervasives_Native.option<(Prims.int * Prims.int)>; float_of_int : Prims.int  ->  Prims.string; int_text : Prims.int  ->  Prims.string; scale_of : Prims.list<Prims.string>  ->  Prims.list<Prims.bool>  ->  FStar_Pervasives_Native.option<Prims.nat>; try_scaled : Prims.nat  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.int>; compare_float : Prims.string  ->  Prims.string  ->  Prims.int}


let __proj__Mkhost__item__date_text : host  ->  Prims.int  ->  Prims.string = (fun ( projectee  :  host ) -> (match (projectee) with
| {date_text = date_text; instant_text = instant_text; try_days = try_days; try_instant = try_instant; float_of_int = float_of_int; int_text = int_text; scale_of = scale_of; try_scaled = try_scaled; compare_float = compare_float} -> begin
     date_text
     end))


let __proj__Mkhost__item__instant_text : host  ->  time_unit  ->  Prims.int  ->  Prims.int  ->  Prims.string = (fun ( projectee  :  host ) -> (match (projectee) with
| {date_text = date_text; instant_text = instant_text; try_days = try_days; try_instant = try_instant; float_of_int = float_of_int; int_text = int_text; scale_of = scale_of; try_scaled = try_scaled; compare_float = compare_float} -> begin
     instant_text
     end))


let __proj__Mkhost__item__try_days : host  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  host ) -> (match (projectee) with
| {date_text = date_text; instant_text = instant_text; try_days = try_days; try_instant = try_instant; float_of_int = float_of_int; int_text = int_text; scale_of = scale_of; try_scaled = try_scaled; compare_float = compare_float} -> begin
     try_days
     end))


let __proj__Mkhost__item__try_instant : host  ->  time_unit  ->  Prims.string  ->  FStar_Pervasives_Native.option<(Prims.int * Prims.int)> = (fun ( projectee  :  host ) -> (match (projectee) with
| {date_text = date_text; instant_text = instant_text; try_days = try_days; try_instant = try_instant; float_of_int = float_of_int; int_text = int_text; scale_of = scale_of; try_scaled = try_scaled; compare_float = compare_float} -> begin
     try_instant
     end))


let __proj__Mkhost__item__float_of_int : host  ->  Prims.int  ->  Prims.string = (fun ( projectee  :  host ) -> (match (projectee) with
| {date_text = date_text; instant_text = instant_text; try_days = try_days; try_instant = try_instant; float_of_int = float_of_int; int_text = int_text; scale_of = scale_of; try_scaled = try_scaled; compare_float = compare_float} -> begin
     float_of_int
     end))


let __proj__Mkhost__item__int_text : host  ->  Prims.int  ->  Prims.string = (fun ( projectee  :  host ) -> (match (projectee) with
| {date_text = date_text; instant_text = instant_text; try_days = try_days; try_instant = try_instant; float_of_int = float_of_int; int_text = int_text; scale_of = scale_of; try_scaled = try_scaled; compare_float = compare_float} -> begin
     int_text
     end))


let __proj__Mkhost__item__scale_of : host  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.bool>  ->  FStar_Pervasives_Native.option<Prims.nat> = (fun ( projectee  :  host ) -> (match (projectee) with
| {date_text = date_text; instant_text = instant_text; try_days = try_days; try_instant = try_instant; float_of_int = float_of_int; int_text = int_text; scale_of = scale_of; try_scaled = try_scaled; compare_float = compare_float} -> begin
     scale_of
     end))


let __proj__Mkhost__item__try_scaled : host  ->  Prims.nat  ->  Prims.string  ->  FStar_Pervasives_Native.option<Prims.int> = (fun ( projectee  :  host ) -> (match (projectee) with
| {date_text = date_text; instant_text = instant_text; try_days = try_days; try_instant = try_instant; float_of_int = float_of_int; int_text = int_text; scale_of = scale_of; try_scaled = try_scaled; compare_float = compare_float} -> begin
     try_scaled
     end))


let __proj__Mkhost__item__compare_float : host  ->  Prims.string  ->  Prims.string  ->  Prims.int = (fun ( projectee  :  host ) -> (match (projectee) with
| {date_text = date_text; instant_text = instant_text; try_days = try_days; try_instant = try_instant; float_of_int = float_of_int; int_text = int_text; scale_of = scale_of; try_scaled = try_scaled; compare_float = compare_float} -> begin
     compare_float
     end))


let frac_at : FStar_Pervasives_Native.option<Prims.list<Prims.int>>  ->  Prims.nat  ->  Prims.int = (fun ( f  :  FStar_Pervasives_Native.option<Prims.list<Prims.int>> ) ( p  :  Prims.nat ) -> (match (f) with
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end
| FStar_Pervasives_Native.Some (fs) -> begin
     (at_or (Prims.parse_int "0") fs p)
     end))


let present : validity  ->  Prims.nat  ->  Prims.bool = (fun ( v  :  validity ) ( i  :  Prims.nat ) -> (match (v) with
| AllValid -> begin
     true
     end
| VMask (m) -> begin
     (bit m i)
     end))


let core_cell : host  ->  ccolumn  ->  Prims.nat  ->  Pipeline.cell = (fun ( h  :  host ) ( c  :  ccolumn ) ( i  :  Prims.nat ) -> (match (c.cdata) with
| DInts (xs, v) -> begin
      
if (present v i) then begin
     (match ((vnth xs i)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Int (x)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| DFloats (xs, v) -> begin
      
if (present v i) then begin
     (match ((vnth xs i)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Float (x)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| DBools (xs, v) -> begin
      
if (present v i) then begin
     (match ((vnth xs i)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Bool (x)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| DStrs (xs, v) -> begin
      
if (present v i) then begin
     (match ((vnth xs i)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Str (x)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| DDates (xs, v) -> begin
      
if (present v i) then begin
     (match ((vnth xs i)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Date ((h.date_text x))
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| DStamps (u, xs, f, v) -> begin
      
if (present v i) then begin
     (match ((vnth xs i)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Timestamp ((h.instant_text u x (frac_at f i)))
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| DDecimals (xs, v) -> begin
      
if (present v i) then begin
     (match ((vnth xs i)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Decimal (x)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end))


let core_cells : host  ->  ccolumn  ->  Prims.list<Pipeline.cell> = (fun ( h  :  host ) ( c  :  ccolumn ) -> (lmap (core_cell h c) (upto (clength c))))

type vec =
| VInts of Prims.list<Prims.int> * Prims.list<Prims.bool>
| VFloats of Prims.list<Prims.string> * Prims.list<Prims.bool>
| VBools of Prims.list<Prims.bool> * Prims.list<Prims.bool>
| VStrs of ftype * Prims.list<Prims.string> * Prims.list<Prims.bool>
| VDecs of Prims.list<Prims.int> * Prims.nat * Prims.list<Prims.string> * Prims.list<Prims.bool>
| VDates of Prims.list<Prims.int> * Prims.list<Prims.bool>
| VStamps of time_unit * Prims.list<Prims.int> * FStar_Pervasives_Native.option<Prims.list<Prims.int>> * Prims.list<Prims.bool>
| VCells of Prims.list<Pipeline.cell>


let uu___is_VInts : vec  ->  Prims.bool = (fun ( projectee  :  vec ) -> (match (projectee) with
| VInts (vals, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VInts__item__vals : vec  ->  Prims.list<Prims.int> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VInts (vals, mask) -> begin
     vals
     end))


let __proj__VInts__item__mask : vec  ->  Prims.list<Prims.bool> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VInts (vals, mask) -> begin
     mask
     end))


let uu___is_VFloats : vec  ->  Prims.bool = (fun ( projectee  :  vec ) -> (match (projectee) with
| VFloats (vals, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VFloats__item__vals : vec  ->  Prims.list<Prims.string> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VFloats (vals, mask) -> begin
     vals
     end))


let __proj__VFloats__item__mask : vec  ->  Prims.list<Prims.bool> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VFloats (vals, mask) -> begin
     mask
     end))


let uu___is_VBools : vec  ->  Prims.bool = (fun ( projectee  :  vec ) -> (match (projectee) with
| VBools (vals, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VBools__item__vals : vec  ->  Prims.list<Prims.bool> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VBools (vals, mask) -> begin
     vals
     end))


let __proj__VBools__item__mask : vec  ->  Prims.list<Prims.bool> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VBools (vals, mask) -> begin
     mask
     end))


let uu___is_VStrs : vec  ->  Prims.bool = (fun ( projectee  :  vec ) -> (match (projectee) with
| VStrs (ty, vals, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VStrs__item__ty : vec  ->  ftype = (fun ( projectee  :  vec ) -> (match (projectee) with
| VStrs (ty, vals, mask) -> begin
     ty
     end))


let __proj__VStrs__item__vals : vec  ->  Prims.list<Prims.string> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VStrs (ty, vals, mask) -> begin
     vals
     end))


let __proj__VStrs__item__mask : vec  ->  Prims.list<Prims.bool> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VStrs (ty, vals, mask) -> begin
     mask
     end))


let uu___is_VDecs : vec  ->  Prims.bool = (fun ( projectee  :  vec ) -> (match (projectee) with
| VDecs (scaled, scale, texts, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VDecs__item__scaled : vec  ->  Prims.list<Prims.int> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VDecs (scaled, scale, texts, mask) -> begin
     scaled
     end))


let __proj__VDecs__item__scale : vec  ->  Prims.nat = (fun ( projectee  :  vec ) -> (match (projectee) with
| VDecs (scaled, scale, texts, mask) -> begin
     scale
     end))


let __proj__VDecs__item__texts : vec  ->  Prims.list<Prims.string> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VDecs (scaled, scale, texts, mask) -> begin
     texts
     end))


let __proj__VDecs__item__mask : vec  ->  Prims.list<Prims.bool> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VDecs (scaled, scale, texts, mask) -> begin
     mask
     end))


let uu___is_VDates : vec  ->  Prims.bool = (fun ( projectee  :  vec ) -> (match (projectee) with
| VDates (days, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VDates__item__days : vec  ->  Prims.list<Prims.int> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VDates (days, mask) -> begin
     days
     end))


let __proj__VDates__item__mask : vec  ->  Prims.list<Prims.bool> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VDates (days, mask) -> begin
     mask
     end))


let uu___is_VStamps : vec  ->  Prims.bool = (fun ( projectee  :  vec ) -> (match (projectee) with
| VStamps (u, seconds, fraction, mask) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VStamps__item__u : vec  ->  time_unit = (fun ( projectee  :  vec ) -> (match (projectee) with
| VStamps (u, seconds, fraction, mask) -> begin
     u
     end))


let __proj__VStamps__item__seconds : vec  ->  Prims.list<Prims.int> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VStamps (u, seconds, fraction, mask) -> begin
     seconds
     end))


let __proj__VStamps__item__fraction : vec  ->  FStar_Pervasives_Native.option<Prims.list<Prims.int>> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VStamps (u, seconds, fraction, mask) -> begin
     fraction
     end))


let __proj__VStamps__item__mask : vec  ->  Prims.list<Prims.bool> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VStamps (u, seconds, fraction, mask) -> begin
     mask
     end))


let uu___is_VCells : vec  ->  Prims.bool = (fun ( projectee  :  vec ) -> (match (projectee) with
| VCells (cells) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__VCells__item__cells : vec  ->  Prims.list<Pipeline.cell> = (fun ( projectee  :  vec ) -> (match (projectee) with
| VCells (cells) -> begin
     cells
     end))


let vlen : vec  ->  Prims.nat = (fun ( v  :  vec ) -> (match (v) with
| VInts (a, uu___) -> begin
     (Pipeline.len a)
     end
| VFloats (a, uu___) -> begin
     (Pipeline.len a)
     end
| VBools (a, uu___) -> begin
     (Pipeline.len a)
     end
| VStrs (uu___, a, uu___1) -> begin
     (Pipeline.len a)
     end
| VDecs (a, uu___, uu___1, uu___2) -> begin
     (Pipeline.len a)
     end
| VDates (a, uu___) -> begin
     (Pipeline.len a)
     end
| VStamps (uu___, a, uu___1, uu___2) -> begin
     (Pipeline.len a)
     end
| VCells (a) -> begin
     (Pipeline.len a)
     end))


let str_cell : ftype  ->  Prims.string  ->  Pipeline.cell = (fun ( ty  :  ftype ) ( s  :  Prims.string ) -> (match (ty) with
| TDate -> begin
     Pipeline.Date (s)
     end
| TStamp (uu___) -> begin
     Pipeline.Timestamp (s)
     end
| uu___ -> begin
     Pipeline.Str (s)
     end))


let cell_at : host  ->  vec  ->  Prims.nat  ->  Pipeline.cell = (fun ( h  :  host ) ( v  :  vec ) ( p  :  Prims.nat ) -> (match (v) with
| VInts (a, m) -> begin
      
if (bit m p) then begin
     (match ((vnth a p)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Int (x)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| VFloats (a, m) -> begin
      
if (bit m p) then begin
     (match ((vnth a p)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Float (x)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| VBools (a, m) -> begin
      
if (bit m p) then begin
     (match ((vnth a p)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Bool (x)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| VStrs (ty, a, m) -> begin
      
if (bit m p) then begin
     (match ((vnth a p)) with
| FStar_Pervasives_Native.Some (x) -> begin
     (str_cell ty x)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| VDecs (uu___, uu___1, t, m) -> begin
      
if (bit m p) then begin
     (match ((vnth t p)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Decimal (x)
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| VDates (a, m) -> begin
      
if (bit m p) then begin
     (match ((vnth a p)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Date ((h.date_text x))
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| VStamps (u, s, f, m) -> begin
      
if (bit m p) then begin
     (match ((vnth s p)) with
| FStar_Pervasives_Native.Some (x) -> begin
     Pipeline.Timestamp ((h.instant_text u x (frac_at f p)))
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end else begin
     Pipeline.Null
     end
     end
| VCells (a) -> begin
     (match ((vnth a p)) with
| FStar_Pervasives_Native.Some (c) -> begin
     c
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Null
     end)
     end))


let vec_wf : vec  ->  Prims.bool = (fun ( v  :  vec ) -> (match (v) with
| VInts (a, m) -> begin
     (Prims.op_Equals (Pipeline.len m) (Pipeline.len a))
     end
| VFloats (a, m) -> begin
     (Prims.op_Equals (Pipeline.len m) (Pipeline.len a))
     end
| VBools (a, m) -> begin
     (Prims.op_Equals (Pipeline.len m) (Pipeline.len a))
     end
| VStrs (uu___, a, m) -> begin
     (Prims.op_Equals (Pipeline.len m) (Pipeline.len a))
     end
| VDecs (a, uu___, t, m) -> begin
     ((Prims.op_Equals (Pipeline.len m) (Pipeline.len a)) && (Prims.op_Equals (Pipeline.len t) (Pipeline.len a)))
     end
| VDates (a, m) -> begin
     (Prims.op_Equals (Pipeline.len m) (Pipeline.len a))
     end
| VStamps (uu___, a, uu___1, m) -> begin
     (Prims.op_Equals (Pipeline.len m) (Pipeline.len a))
     end
| VCells (uu___) -> begin
     true
     end))


let always : Prims.nat  ->  Prims.bool = (fun ( uu___  :  Prims.nat ) -> true)


let all_true : Prims.nat  ->  Prims.list<Prims.bool> = (fun ( n  :  Prims.nat ) -> (lmap always (upto n)))


let mask_of : Prims.nat  ->  validity  ->  Prims.list<Prims.bool> = (fun ( n  :  Prims.nat ) ( v  :  validity ) -> (match (v) with
| AllValid -> begin
     (all_true n)
     end
| VMask (m) -> begin
      
if (Prims.op_Equals (Pipeline.len m) n) then begin
     m
     end else begin
     (lmap (bit m) (upto n))
     end
     end))


let fill_at : host  ->  Prims.nat  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.bool>  ->  Prims.nat  ->  Prims.int = (fun ( h  :  host ) ( s  :  Prims.nat ) ( texts  :  Prims.list<Prims.string> ) ( mask  :  Prims.list<Prims.bool> ) ( i  :  Prims.nat ) ->  
if (bit mask i) then begin
     (match ((h.try_scaled s (at_or "" texts i))) with
| FStar_Pervasives_Native.Some (k) -> begin
     k
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end)
     end else begin
     (Prims.parse_int "0")
     end)


let fill : host  ->  Prims.nat  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.bool>  ->  Prims.list<Prims.int> = (fun ( h  :  host ) ( s  :  Prims.nat ) ( texts  :  Prims.list<Prims.string> ) ( mask  :  Prims.list<Prims.bool> ) -> (lmap (fill_at h s texts mask) (upto (Pipeline.len texts))))


let dec_boxed : Prims.list<Prims.string>  ->  Prims.list<Prims.bool>  ->  Prims.nat  ->  Pipeline.cell = (fun ( texts  :  Prims.list<Prims.string> ) ( mask  :  Prims.list<Prims.bool> ) ( i  :  Prims.nat ) ->  
if (bit mask i) then begin
     Pipeline.Decimal ((at_or "" texts i))
     end else begin
     Pipeline.Null
     end)


let of_decimal_texts : host  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.bool>  ->  vec = (fun ( h  :  host ) ( texts  :  Prims.list<Prims.string> ) ( mask  :  Prims.list<Prims.bool> ) -> (match ((h.scale_of texts mask)) with
| FStar_Pervasives_Native.Some (s) -> begin
     VDecs ((fill h s texts mask), s, texts, mask)
     end
| FStar_Pervasives_Native.None -> begin
     VCells ((lmap (dec_boxed texts mask) (upto (Pipeline.len texts))))
     end))


let admits : host  ->  ftype  ->  Pipeline.cell  ->  Prims.bool = (fun ( h  :  host ) ( ty  :  ftype ) ( c  :  Pipeline.cell ) -> (match (((ty), (c))) with
| (uu___, Pipeline.Null) -> begin
     true
     end
| (TInt, Pipeline.Int (uu___)) -> begin
     true
     end
| (TFloat, Pipeline.Float (uu___)) -> begin
     true
     end
| (TFloat, Pipeline.Int (uu___)) -> begin
     true
     end
| (TBool, Pipeline.Bool (uu___)) -> begin
     true
     end
| (TString, Pipeline.Str (uu___)) -> begin
     true
     end
| (TDate, Pipeline.Date (s)) -> begin
     (match ((h.try_days s)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| (TStamp (u), Pipeline.Timestamp (s)) -> begin
     (match ((h.try_instant u s)) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| (TDecimal, Pipeline.Decimal (uu___)) -> begin
     true
     end
| (TDecimal, Pipeline.Int (uu___)) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let is_present : Pipeline.cell  ->  Prims.bool = (fun ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Null -> begin
     false
     end
| uu___ -> begin
     true
     end))


let int_of : Pipeline.cell  ->  Prims.int = (fun ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Int (v) -> begin
     v
     end
| uu___ -> begin
     (Prims.parse_int "0")
     end))


let float_of : host  ->  Pipeline.cell  ->  Prims.string = (fun ( h  :  host ) ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Float (v) -> begin
     v
     end
| Pipeline.Int (v) -> begin
     (h.float_of_int v)
     end
| uu___ -> begin
     "0"
     end))


let bool_of : Pipeline.cell  ->  Prims.bool = (fun ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Bool (v) -> begin
     v
     end
| uu___ -> begin
     false
     end))


let str_of : Pipeline.cell  ->  Prims.string = (fun ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Str (s) -> begin
     s
     end
| uu___ -> begin
     ""
     end))


let dec_of : host  ->  Pipeline.cell  ->  Prims.string = (fun ( h  :  host ) ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Decimal (s) -> begin
     s
     end
| Pipeline.Int (v) -> begin
     (h.int_text v)
     end
| uu___ -> begin
     ""
     end))


let day_of : host  ->  Pipeline.cell  ->  Prims.int = (fun ( h  :  host ) ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Date (s) -> begin
     (match ((h.try_days s)) with
| FStar_Pervasives_Native.Some (d) -> begin
     d
     end
| FStar_Pervasives_Native.None -> begin
     (Prims.parse_int "0")
     end)
     end
| uu___ -> begin
     (Prims.parse_int "0")
     end))


let instant_of : host  ->  time_unit  ->  Pipeline.cell  ->  (Prims.int * Prims.int) = (fun ( h  :  host ) ( u  :  time_unit ) ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Timestamp (s) -> begin
     (match ((h.try_instant u s)) with
| FStar_Pervasives_Native.Some (p) -> begin
     p
     end
| FStar_Pervasives_Native.None -> begin
     (((Prims.parse_int "0")), ((Prims.parse_int "0")))
     end)
     end
| uu___ -> begin
     (((Prims.parse_int "0")), ((Prims.parse_int "0")))
     end))


let cell_i : Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Pipeline.cell = (fun ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (at_or Pipeline.Null cells i))


let pk_present : Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Prims.bool = (fun ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (is_present (cell_i cells i)))


let pk_int : Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Prims.int = (fun ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (int_of (cell_i cells i)))


let pk_float : host  ->  Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Prims.string = (fun ( h  :  host ) ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (float_of h (cell_i cells i)))


let pk_bool : Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Prims.bool = (fun ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (bool_of (cell_i cells i)))


let pk_str : Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Prims.string = (fun ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (str_of (cell_i cells i)))


let pk_day : host  ->  Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Prims.int = (fun ( h  :  host ) ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (day_of h (cell_i cells i)))


let pk_sec : host  ->  time_unit  ->  Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Prims.int = (fun ( h  :  host ) ( u  :  time_unit ) ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (FStar_Pervasives_Native.fst (instant_of h u (cell_i cells i))))


let pk_frac : host  ->  time_unit  ->  Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Prims.int = (fun ( h  :  host ) ( u  :  time_unit ) ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (FStar_Pervasives_Native.snd (instant_of h u (cell_i cells i))))


let pk_dec : host  ->  Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Prims.string = (fun ( h  :  host ) ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (dec_of h (cell_i cells i)))


let pack_mask : Prims.list<Pipeline.cell>  ->  Prims.list<Prims.bool> = (fun ( cells  :  Prims.list<Pipeline.cell> ) -> (lmap (pk_present cells) (upto (Pipeline.len cells))))


let pack_texts : host  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Prims.string> = (fun ( h  :  host ) ( cells  :  Prims.list<Pipeline.cell> ) -> (lmap (pk_dec h cells) (upto (Pipeline.len cells))))


let widen_cell : host  ->  ftype  ->  Pipeline.cell  ->  Pipeline.cell = (fun ( h  :  host ) ( ty  :  ftype ) ( c  :  Pipeline.cell ) -> (match (((ty), (c))) with
| (TFloat, Pipeline.Int (v)) -> begin
     Pipeline.Float ((h.float_of_int v))
     end
| (TDecimal, Pipeline.Int (v)) -> begin
     Pipeline.Decimal ((h.int_text v))
     end
| uu___ -> begin
     c
     end))


let pk_boxed : host  ->  ftype  ->  Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Pipeline.cell = (fun ( h  :  host ) ( ty  :  ftype ) ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) -> (widen_cell h ty (cell_i cells i)))


let pack_boxed : host  ->  ftype  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell> = (fun ( h  :  host ) ( ty  :  ftype ) ( cells  :  Prims.list<Pipeline.cell> ) -> (lmap (pk_boxed h ty cells) (upto (Pipeline.len cells))))


let pack : host  ->  ftype  ->  Prims.list<Pipeline.cell>  ->  vec = (fun ( h  :  host ) ( ty  :  ftype ) ( cells  :  Prims.list<Pipeline.cell> ) -> (

let n = (Pipeline.len cells)
in (

let mask = (pack_mask cells)
in  
if (not ((lfor_all (admits h ty) cells))) then begin
     VCells ((pack_boxed h ty cells))
     end else begin
     (match (ty) with
| TInt -> begin
     VInts ((lmap (pk_int cells) (upto n)), mask)
     end
| TFloat -> begin
     VFloats ((lmap (pk_float h cells) (upto n)), mask)
     end
| TBool -> begin
     VBools ((lmap (pk_bool cells) (upto n)), mask)
     end
| TString -> begin
     VStrs (TString, (lmap (pk_str cells) (upto n)), mask)
     end
| TDate -> begin
     VDates ((lmap (pk_day h cells) (upto n)), mask)
     end
| TStamp (u) -> begin
     VStamps (u, (lmap (pk_sec h u cells) (upto n)),  
if (Prims.op_Equals u Seconds) then begin
     FStar_Pervasives_Native.None
     end else begin
     FStar_Pervasives_Native.Some ((lmap (pk_frac h u cells) (upto n)))
     end, mask)
     end
| TDecimal -> begin
     (

let texts = (pack_texts h cells)
in (match ((h.scale_of texts mask)) with
| FStar_Pervasives_Native.Some (s) -> begin
     VDecs ((fill h s texts mask), s, texts, mask)
     end
| FStar_Pervasives_Native.None -> begin
     VCells ((pack_boxed h ty cells))
     end))
     end)
     end)))


let pad : Prims.nat  ->  Prims.list<Pipeline.cell>  ->  Prims.list<Pipeline.cell> = (fun ( n  :  Prims.nat ) ( cells  :  Prims.list<Pipeline.cell> ) -> (lmap (cell_i cells) (upto n)))


let of_column : host  ->  Prims.nat  ->  ftype  ->  FStar_Pervasives_Native.option<ccolumn>  ->  vec = (fun ( h  :  host ) ( n  :  Prims.nat ) ( ty  :  ftype ) ( column  :  FStar_Pervasives_Native.option<ccolumn> ) -> (match (column) with
| FStar_Pervasives_Native.None -> begin
     (pack h ty (pad n []))
     end
| FStar_Pervasives_Native.Some (c) -> begin
      
if ((Prims.op_Less_Greater (ctype c) ty) || (Prims.op_Less_Greater (clength c) n)) then begin
     (

let a = (core_cells h c)
in  
if (Prims.op_Equals (Pipeline.len a) n) then begin
     (pack h ty a)
     end else begin
     (pack h ty (pad n a))
     end)
     end else begin
     (match (c.cdata) with
| DInts (v, va) -> begin
     VInts (v, (mask_of n va))
     end
| DFloats (v, va) -> begin
     VFloats (v, (mask_of n va))
     end
| DBools (v, va) -> begin
     VBools (v, (mask_of n va))
     end
| DStrs (v, va) -> begin
     VStrs (TString, v, (mask_of n va))
     end
| DDates (v, va) -> begin
     VDates (v, (mask_of n va))
     end
| DStamps (u, s, f, va) -> begin
     VStamps (u, s, f, (mask_of n va))
     end
| DDecimals (v, va) -> begin
     (of_decimal_texts h v (mask_of n va))
     end)
     end
     end))

type dframe = {fcols : Prims.list<(Prims.string * ftype)>; vecs : Prims.list<vec>; sel : FStar_Pervasives_Native.option<Prims.list<Prims.nat>>; count : Prims.nat}


let __proj__Mkdframe__item__fcols : dframe  ->  Prims.list<(Prims.string * ftype)> = (fun ( projectee  :  dframe ) -> (match (projectee) with
| {fcols = fcols; vecs = vecs; sel = sel; count = count} -> begin
     fcols
     end))


let __proj__Mkdframe__item__vecs : dframe  ->  Prims.list<vec> = (fun ( projectee  :  dframe ) -> (match (projectee) with
| {fcols = fcols; vecs = vecs; sel = sel; count = count} -> begin
     vecs
     end))


let __proj__Mkdframe__item__sel : dframe  ->  FStar_Pervasives_Native.option<Prims.list<Prims.nat>> = (fun ( projectee  :  dframe ) -> (match (projectee) with
| {fcols = fcols; vecs = vecs; sel = sel; count = count} -> begin
     sel
     end))


let __proj__Mkdframe__item__count : dframe  ->  Prims.nat = (fun ( projectee  :  dframe ) -> (match (projectee) with
| {fcols = fcols; vecs = vecs; sel = sel; count = count} -> begin
     count
     end))


let erase_field : (Prims.string * ftype)  ->  (Prims.string * Pipeline.column_type) = (fun ( p  :  (Prims.string * ftype) ) -> (((FStar_Pervasives_Native.fst p)), ((erase (FStar_Pervasives_Native.snd p)))))


let erase_schema : Prims.list<(Prims.string * ftype)>  ->  Pipeline.schema = (fun ( cols  :  Prims.list<(Prims.string * ftype)> ) -> (lmap erase_field cols))


let rows : dframe  ->  Prims.nat = (fun ( f  :  dframe ) -> (match (f.sel) with
| FStar_Pervasives_Native.Some (s) -> begin
     (Pipeline.len s)
     end
| FStar_Pervasives_Native.None -> begin
     f.count
     end))


let physical : dframe  ->  Prims.list<Prims.nat> = (fun ( f  :  dframe ) -> (match (f.sel) with
| FStar_Pervasives_Native.Some (s) -> begin
     s
     end
| FStar_Pervasives_Native.None -> begin
     (upto f.count)
     end))


let select1 : dframe  ->  Prims.list<Prims.nat>  ->  dframe = (fun ( f  :  dframe ) ( s  :  Prims.list<Prims.nat> ) -> {fcols = f.fcols; vecs = f.vecs; sel = FStar_Pervasives_Native.Some (s); count = f.count})


let vec_ok : Prims.nat  ->  vec  ->  Prims.bool = (fun ( n  :  Prims.nat ) ( v  :  vec ) -> ((Prims.op_Equals (vlen v) n) && (vec_wf v)))


let in_range : Prims.nat  ->  Prims.nat  ->  Prims.bool = (fun ( n  :  Prims.nat ) ( p  :  Prims.nat ) -> (p < n))


let dwf : dframe  ->  Prims.bool = (fun ( f  :  dframe ) -> (((Prims.op_Equals (Pipeline.len f.vecs) (Pipeline.len f.fcols)) && (lfor_all (vec_ok f.count) f.vecs)) && (match (f.sel) with
| FStar_Pervasives_Native.None -> begin
     true
     end
| FStar_Pervasives_Native.Some (s) -> begin
     (lfor_all (in_range f.count) s)
     end)))


let read_at : host  ->  Prims.nat  ->  vec  ->  Pipeline.cell = (fun ( h  :  host ) ( p  :  Prims.nat ) ( v  :  vec ) -> (cell_at h v p))


let row_at : host  ->  dframe  ->  Prims.nat  ->  Prims.list<Pipeline.cell> = (fun ( h  :  host ) ( f  :  dframe ) ( p  :  Prims.nat ) -> (lmap (read_at h p) f.vecs))


let rec rows_at : host  ->  dframe  ->  Prims.list<Prims.nat>  ->  Prims.list<Prims.list<Pipeline.cell>> = (fun ( h  :  host ) ( f  :  dframe ) ( ps  :  Prims.list<Prims.nat> ) -> (match (ps) with
| [] -> begin
     []
     end
| (p)::t -> begin
     ((row_at h f p))::(rows_at h f t)
     end))


let rows_of : host  ->  dframe  ->  Prims.list<Prims.list<Pipeline.cell>> = (fun ( h  :  host ) ( f  :  dframe ) -> (rows_at h f (physical f)))


let logical : host  ->  dframe  ->  Pipeline.wframe = (fun ( h  :  host ) ( f  :  dframe ) -> {Pipeline.cols = (erase_schema f.fcols); Pipeline.rows = (rows_of h f)})


let row_count : ctable  ->  Prims.nat = (fun ( t  :  ctable ) -> (match (t.tcolumns) with
| (c)::uu___ -> begin
     (clength c)
     end
| [] -> begin
     (Prims.parse_int "0")
     end))


let rec try_column : Prims.string  ->  Prims.list<ccolumn>  ->  FStar_Pervasives_Native.option<ccolumn> = (fun ( name  :  Prims.string ) ( cs  :  Prims.list<ccolumn> ) -> (match (cs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (c)::t -> begin
      
if (Prims.op_Equals c.cname name) then begin
     FStar_Pervasives_Native.Some (c)
     end else begin
     (try_column name t)
     end
     end))


let column_vec : host  ->  Prims.nat  ->  Prims.list<ccolumn>  ->  (Prims.string * ftype)  ->  vec = (fun ( h  :  host ) ( n  :  Prims.nat ) ( cs  :  Prims.list<ccolumn> ) ( fld  :  (Prims.string * ftype) ) -> (of_column h n (FStar_Pervasives_Native.snd fld) (try_column (FStar_Pervasives_Native.fst fld) cs)))


let of_table : host  ->  ctable  ->  dframe = (fun ( h  :  host ) ( t  :  ctable ) -> (

let n = (row_count t)
in {fcols = t.tschema; vecs = (lmap (column_vec h n t.tcolumns) t.tschema); sel = FStar_Pervasives_Native.None; count = n}))


let cell_of_row : Prims.nat  ->  Prims.list<Pipeline.cell>  ->  Pipeline.cell = (fun ( j  :  Prims.nat ) ( r  :  Prims.list<Pipeline.cell> ) -> (at_or Pipeline.Null r j))


let column_of : Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.nat  ->  Prims.list<Pipeline.cell> = (fun ( rs  :  Prims.list<Prims.list<Pipeline.cell>> ) ( j  :  Prims.nat ) -> (lmap (cell_of_row j) rs))


let rec lmapi_from = (fun ( k  :  Prims.nat ) ( g  :  Prims.nat  ->  'a  ->  'b ) ( l  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
     ((g k x))::(lmapi_from (k + (Prims.parse_int "1")) g t)
     end))


let lmapi = (fun ( g  :  Prims.nat  ->  'a  ->  'b ) ( l  :  Prims.list<'a> ) -> (lmapi_from (Prims.parse_int "0") g l))


let row_vec : host  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Prims.nat  ->  (Prims.string * ftype)  ->  vec = (fun ( h  :  host ) ( rs  :  Prims.list<Prims.list<Pipeline.cell>> ) ( ci  :  Prims.nat ) ( fld  :  (Prims.string * ftype) ) -> (pack h (FStar_Pervasives_Native.snd fld) (column_of rs ci)))


let of_rows : host  ->  Prims.list<(Prims.string * ftype)>  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  dframe = (fun ( h  :  host ) ( cols  :  Prims.list<(Prims.string * ftype)> ) ( rs  :  Prims.list<Prims.list<Pipeline.cell>> ) -> {fcols = cols; vecs = (lmapi (row_vec h rs) cols); sel = FStar_Pervasives_Native.None; count = (Pipeline.len rs)})


let gather : vec  ->  Prims.list<Prims.nat>  ->  vec = (fun ( v  :  vec ) ( phys  :  Prims.list<Prims.nat> ) -> (

let mk = (fun ( m  :  Prims.list<Prims.bool> ) -> (lmap (bit m) phys))
in (match (v) with
| VInts (a, m) -> begin
     VInts ((lmap (at_or (Prims.parse_int "0") a) phys), (mk m))
     end
| VFloats (a, m) -> begin
     VFloats ((lmap (at_or "0" a) phys), (mk m))
     end
| VBools (a, m) -> begin
     VBools ((lmap (at_or false a) phys), (mk m))
     end
| VStrs (ty, a, m) -> begin
     VStrs (ty, (lmap (at_or "" a) phys), (mk m))
     end
| VDecs (a, s, t, m) -> begin
     VDecs ((lmap (at_or (Prims.parse_int "0") a) phys), s, (lmap (at_or "" t) phys), (mk m))
     end
| VDates (a, m) -> begin
     VDates ((lmap (at_or (Prims.parse_int "0") a) phys), (mk m))
     end
| VStamps (u, s, f, m) -> begin
     VStamps (u, (lmap (at_or (Prims.parse_int "0") s) phys), (match (f) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (fs) -> begin
     FStar_Pervasives_Native.Some ((lmap (at_or (Prims.parse_int "0") fs) phys))
     end), (mk m))
     end
| VCells (a) -> begin
     VCells ((lmap (at_or Pipeline.Null a) phys))
     end)))


let is_true : Prims.bool  ->  Prims.bool = (fun ( b  :  Prims.bool ) -> b)


let validity_of : Prims.list<Prims.bool>  ->  validity = (fun ( m  :  Prims.list<Prims.bool> ) ->  
if (lfor_all is_true m) then begin
     AllValid
     end else begin
     VMask (m)
     end)


let to_column : (Prims.string * ftype)  ->  vec  ->  FStar_Pervasives_Native.option<ccolumn> = (fun ( fld  :  (Prims.string * ftype) ) ( v  :  vec ) -> (

let name = (FStar_Pervasives_Native.fst fld)
in (match (v) with
| VInts (a, m) -> begin
     FStar_Pervasives_Native.Some ({cname = name; cdata = DInts (a, (validity_of m))})
     end
| VFloats (a, m) -> begin
     FStar_Pervasives_Native.Some ({cname = name; cdata = DFloats (a, (validity_of m))})
     end
| VBools (a, m) -> begin
     FStar_Pervasives_Native.Some ({cname = name; cdata = DBools (a, (validity_of m))})
     end
| VStrs (TString, a, m) -> begin
     FStar_Pervasives_Native.Some ({cname = name; cdata = DStrs (a, (validity_of m))})
     end
| VStrs (uu___, uu___1, uu___2) -> begin
     FStar_Pervasives_Native.None
     end
| VDates (a, m) -> begin
     FStar_Pervasives_Native.Some ({cname = name; cdata = DDates (a, (validity_of m))})
     end
| VStamps (u, s, fr, m) -> begin
     FStar_Pervasives_Native.Some ({cname = name; cdata = DStamps (u, s, fr, (validity_of m))})
     end
| VDecs (uu___, uu___1, t, m) -> begin
     FStar_Pervasives_Native.Some ({cname = name; cdata = DDecimals (t, (validity_of m))})
     end
| VCells (uu___) -> begin
     FStar_Pervasives_Native.None
     end)))


let rec all_some = (fun ( l  :  Prims.list<FStar_Pervasives_Native.option<'a>> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.Some ([])
     end
| (FStar_Pervasives_Native.Some (x))::t -> begin
     (match ((all_some t)) with
| FStar_Pervasives_Native.Some (r) -> begin
     FStar_Pervasives_Native.Some ((x)::r)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
| (FStar_Pervasives_Native.None)::uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let rec zip_cols : Prims.list<(Prims.string * ftype)>  ->  Prims.list<vec>  ->  Prims.list<FStar_Pervasives_Native.option<ccolumn>> = (fun ( fs  :  Prims.list<(Prims.string * ftype)> ) ( vs  :  Prims.list<vec> ) -> (match (((fs), (vs))) with
| ((fld)::ft, (v)::vt) -> begin
     ((to_column fld v))::(zip_cols ft vt)
     end
| uu___ -> begin
     []
     end))


let gather_by : Prims.list<Prims.nat>  ->  vec  ->  vec = (fun ( s  :  Prims.list<Prims.nat> ) ( v  :  vec ) -> (gather v s))


let to_table : dframe  ->  FStar_Pervasives_Native.option<ctable> = (fun ( f  :  dframe ) -> (

let dense = (match (f.sel) with
| FStar_Pervasives_Native.None -> begin
     f.vecs
     end
| FStar_Pervasives_Native.Some (s) -> begin
     (lmap (gather_by s) f.vecs)
     end)
in (match ((all_some (zip_cols f.fcols dense))) with
| FStar_Pervasives_Native.Some (cs) -> begin
     FStar_Pervasives_Native.Some ({tschema = f.fcols; tcolumns = cs})
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)))

type prepared = {psource : FStar_Pervasives_Native.option<ctable>; pframe : dframe}


let __proj__Mkprepared__item__psource : prepared  ->  FStar_Pervasives_Native.option<ctable> = (fun ( projectee  :  prepared ) -> (match (projectee) with
| {psource = psource; pframe = pframe} -> begin
     psource
     end))


let __proj__Mkprepared__item__pframe : prepared  ->  dframe = (fun ( projectee  :  prepared ) -> (match (projectee) with
| {psource = psource; pframe = pframe} -> begin
     pframe
     end))


let prepare : host  ->  ctable  ->  prepared = (fun ( h  :  host ) ( t  :  ctable ) -> {psource = FStar_Pervasives_Native.Some (t); pframe = (of_table h t)})


let of_frame : dframe  ->  prepared = (fun ( f  :  dframe ) -> (

let dense =  
if (Prims.op_Equals (Pipeline.len f.vecs) (Prims.parse_int "0")) then begin
     {fcols = f.fcols; vecs = []; sel = FStar_Pervasives_Native.None; count = (Prims.parse_int "0")}
     end else begin
     (match (f.sel) with
| FStar_Pervasives_Native.None -> begin
     f
     end
| FStar_Pervasives_Native.Some (s) -> begin
     {fcols = f.fcols; vecs = (lmap (gather_by s) f.vecs); sel = FStar_Pervasives_Native.None; count = (Pipeline.len s)}
     end)
     end
in {psource = FStar_Pervasives_Native.None; pframe = dense}))


let rec bad_row : Prims.nat  ->  Prims.nat  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  FStar_Pervasives_Native.option<(Prims.nat * Prims.nat)> = (fun ( w  :  Prims.nat ) ( k  :  Prims.nat ) ( rs  :  Prims.list<Prims.list<Pipeline.cell>> ) -> (match (rs) with
| [] -> begin
     FStar_Pervasives_Native.None
     end
| (r)::t -> begin
      
if (Prims.op_Less_Greater (Pipeline.len r) w) then begin
     FStar_Pervasives_Native.Some (((k), ((Pipeline.len r))))
     end else begin
     (bad_row w (k + (Prims.parse_int "1")) t)
     end
     end))


let prepare_rows : host  ->  Prims.list<(Prims.string * ftype)>  ->  Prims.list<Prims.list<Pipeline.cell>>  ->  Pipeline.outcome<prepared, Pipeline.eval_error> = (fun ( h  :  host ) ( cols  :  Prims.list<(Prims.string * ftype)> ) ( rs  :  Prims.list<Prims.list<Pipeline.cell>> ) -> (match ((bad_row (Pipeline.len cols) (Prims.parse_int "0") rs)) with
| FStar_Pervasives_Native.Some (i, got) -> begin
     Pipeline.Error (Pipeline.TypeError ((Prims.strcat "source row " (Prims.strcat (h.int_text i) (Prims.strcat ": " (Prims.strcat (h.int_text got) (Prims.strcat " cells, schema width " (h.int_text (Pipeline.len cols)))))))))
     end
| FStar_Pervasives_Native.None -> begin
     Pipeline.Ok ((of_frame (of_rows h cols rs)))
     end))


let prepared_table : prepared  ->  FStar_Pervasives_Native.option<ctable> = (fun ( p  :  prepared ) -> (match (p.psource) with
| FStar_Pervasives_Native.Some (t) -> begin
     FStar_Pervasives_Native.Some (t)
     end
| FStar_Pervasives_Native.None -> begin
     (to_table p.pframe)
     end))

type cmp_op =
| CLt
| CLe
| CGt
| CGe
| CEq
| CNe


let uu___is_CLt : cmp_op  ->  Prims.bool = (fun ( projectee  :  cmp_op ) -> (match (projectee) with
| CLt -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLe : cmp_op  ->  Prims.bool = (fun ( projectee  :  cmp_op ) -> (match (projectee) with
| CLe -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CGt : cmp_op  ->  Prims.bool = (fun ( projectee  :  cmp_op ) -> (match (projectee) with
| CGt -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CGe : cmp_op  ->  Prims.bool = (fun ( projectee  :  cmp_op ) -> (match (projectee) with
| CGe -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CEq : cmp_op  ->  Prims.bool = (fun ( projectee  :  cmp_op ) -> (match (projectee) with
| CEq -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CNe : cmp_op  ->  Prims.bool = (fun ( projectee  :  cmp_op ) -> (match (projectee) with
| CNe -> begin
     true
     end
| uu___ -> begin
     false
     end))


let holds : cmp_op  ->  Prims.int  ->  Prims.bool = (fun ( op  :  cmp_op ) ( c  :  Prims.int ) -> (match (op) with
| CLt -> begin
     (c < (Prims.parse_int "0"))
     end
| CLe -> begin
     (c <= (Prims.parse_int "0"))
     end
| CGt -> begin
     (c > (Prims.parse_int "0"))
     end
| CGe -> begin
     (c >= (Prims.parse_int "0"))
     end
| CEq -> begin
     (Prims.op_Equals c (Prims.parse_int "0"))
     end
| CNe -> begin
     (Prims.op_Less_Greater c (Prims.parse_int "0"))
     end))


let flip : cmp_op  ->  cmp_op = (fun ( op  :  cmp_op ) -> (match (op) with
| CLt -> begin
     CGt
     end
| CLe -> begin
     CGe
     end
| CGt -> begin
     CLt
     end
| CGe -> begin
     CLe
     end
| CEq -> begin
     CEq
     end
| CNe -> begin
     CNe
     end))


let int_cmp : Prims.int  ->  Prims.int  ->  Prims.int = (fun ( a  :  Prims.int ) ( b  :  Prims.int ) ->  
if (a < b) then begin
     (Prims.parse_int "-1")
     end else begin
      
if (a > b) then begin
     (Prims.parse_int "1")
     end else begin
     (Prims.parse_int "0")
     end
     end)


let cmp_int_at : cmp_op  ->  Prims.list<Prims.int>  ->  Prims.list<Prims.bool>  ->  Prims.int  ->  Prims.nat  ->  Prims.bool = (fun ( op  :  cmp_op ) ( vals  :  Prims.list<Prims.int> ) ( mask  :  Prims.list<Prims.bool> ) ( k  :  Prims.int ) ( p  :  Prims.nat ) -> ((bit mask p) && (holds op (int_cmp (at_or (Prims.parse_int "0") vals p) k))))


let cmp_ints : cmp_op  ->  Prims.list<Prims.int>  ->  Prims.list<Prims.bool>  ->  Prims.int  ->  Prims.nat  ->  Prims.list<Prims.bool> = (fun ( op  :  cmp_op ) ( vals  :  Prims.list<Prims.int> ) ( mask  :  Prims.list<Prims.bool> ) ( k  :  Prims.int ) ( count  :  Prims.nat ) -> (lmap (cmp_int_at op vals mask k) (upto count)))


let cmp_float_at : host  ->  cmp_op  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.bool>  ->  Prims.string  ->  Prims.nat  ->  Prims.bool = (fun ( h  :  host ) ( op  :  cmp_op ) ( vals  :  Prims.list<Prims.string> ) ( mask  :  Prims.list<Prims.bool> ) ( k  :  Prims.string ) ( p  :  Prims.nat ) -> ((bit mask p) && (holds op (h.compare_float (at_or "0" vals p) k))))


let cmp_floats : host  ->  cmp_op  ->  Prims.list<Prims.string>  ->  Prims.list<Prims.bool>  ->  Prims.string  ->  Prims.nat  ->  Prims.list<Prims.bool> = (fun ( h  :  host ) ( op  :  cmp_op ) ( vals  :  Prims.list<Prims.string> ) ( mask  :  Prims.list<Prims.bool> ) ( k  :  Prims.string ) ( count  :  Prims.nat ) -> (lmap (cmp_float_at h op vals mask k) (upto count)))


let band : Prims.bool  ->  Prims.bool  ->  Prims.bool = (fun ( x  :  Prims.bool ) ( y  :  Prims.bool ) -> (x && y))


let bor : Prims.bool  ->  Prims.bool  ->  Prims.bool = (fun ( x  :  Prims.bool ) ( y  :  Prims.bool ) -> (x || y))


let and_bits : Prims.list<Prims.bool>  ->  Prims.list<Prims.bool>  ->  Prims.list<Prims.bool> = (fun ( a  :  Prims.list<Prims.bool> ) ( b  :  Prims.list<Prims.bool> ) -> (lmap2 band a b))


let or_bits : Prims.list<Prims.bool>  ->  Prims.list<Prims.bool>  ->  Prims.list<Prims.bool> = (fun ( a  :  Prims.list<Prims.bool> ) ( b  :  Prims.list<Prims.bool> ) -> (lmap2 bor a b))


let is_set : Prims.list<Prims.bool>  ->  Prims.nat  ->  Prims.bool = (fun ( bits  :  Prims.list<Prims.bool> ) ( p  :  Prims.nat ) -> (bit bits p))


let selection : Prims.list<Prims.bool>  ->  Prims.list<Prims.nat> = (fun ( bits  :  Prims.list<Prims.bool> ) -> (lfilter (is_set bits) (upto (Pipeline.len bits))))


let cmp_op_of : Pipeline.bin_op  ->  FStar_Pervasives_Native.option<cmp_op> = (fun ( op  :  Pipeline.bin_op ) -> (match (op) with
| Pipeline.Lt -> begin
     FStar_Pervasives_Native.Some (CLt)
     end
| Pipeline.Le -> begin
     FStar_Pervasives_Native.Some (CLe)
     end
| Pipeline.Gt -> begin
     FStar_Pervasives_Native.Some (CGt)
     end
| Pipeline.Ge -> begin
     FStar_Pervasives_Native.Some (CGe)
     end
| Pipeline.Eq -> begin
     FStar_Pervasives_Native.Some (CEq)
     end
| Pipeline.Ne -> begin
     FStar_Pervasives_Native.Some (CNe)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let col_of : dframe  ->  Pipeline.col_expr  ->  FStar_Pervasives_Native.option<Prims.nat> = (fun ( f  :  dframe ) ( x  :  Pipeline.col_expr ) -> (match (x) with
| Pipeline.Col (name) -> begin
     (match ((Pipeline.index_of name (erase_schema f.fcols))) with
| FStar_Pervasives_Native.Some (i) -> begin
     FStar_Pervasives_Native.Some (i)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let const_of : Pipeline.param_env  ->  Pipeline.col_expr  ->  FStar_Pervasives_Native.option<Pipeline.cell> = (fun ( env  :  Pipeline.param_env ) ( x  :  Pipeline.col_expr ) -> (match (x) with
| Pipeline.Lit (c) -> begin
     FStar_Pervasives_Native.Some (c)
     end
| Pipeline.Param (name) -> begin
     (Pipeline.assoc name env)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let max_exact : Prims.int = (Prims.parse_int "9007199254740991")


let rec pow10 : Prims.nat  ->  Prims.int = (fun ( s  :  Prims.nat ) ->  
if (Prims.op_Equals s (Prims.parse_int "0")) then begin
     (Prims.parse_int "1")
     end else begin
     ((Prims.parse_int "10") * (pow10 (s - (Prims.parse_int "1"))))
     end)


let abs_int : Prims.int  ->  Prims.int = (fun ( x  :  Prims.int ) ->  
if (x < (Prims.parse_int "0")) then begin
     ((Prims.parse_int "0") - x)
     end else begin
     x
     end)


let leaf : host  ->  dframe  ->  cmp_op  ->  Prims.nat  ->  Pipeline.cell  ->  FStar_Pervasives_Native.option<Prims.list<Prims.bool>> = (fun ( h  :  host ) ( f  :  dframe ) ( op  :  cmp_op ) ( i  :  Prims.nat ) ( c  :  Pipeline.cell ) -> (match ((((vnth f.vecs i)), (c))) with
| (FStar_Pervasives_Native.Some (VInts (vals, mask)), Pipeline.Int (x)) -> begin
     FStar_Pervasives_Native.Some ((cmp_ints op vals mask x f.count))
     end
| (FStar_Pervasives_Native.Some (VFloats (vals, mask)), Pipeline.Float (x)) -> begin
     FStar_Pervasives_Native.Some ((cmp_floats h op vals mask x f.count))
     end
| (FStar_Pervasives_Native.Some (VFloats (vals, mask)), Pipeline.Int (x)) -> begin
     FStar_Pervasives_Native.Some ((cmp_floats h op vals mask (h.float_of_int x) f.count))
     end
| (FStar_Pervasives_Native.Some (VDecs (vals, s, uu___, mask)), Pipeline.Decimal (x)) -> begin
     (match ((h.try_scaled s x)) with
| FStar_Pervasives_Native.Some (kx) -> begin
     FStar_Pervasives_Native.Some ((cmp_ints op vals mask kx f.count))
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end)
     end
| (FStar_Pervasives_Native.Some (VDecs (vals, s, uu___, mask)), Pipeline.Int (x)) -> begin
     (

let kx = (x * (pow10 s))
in  
if ((abs_int kx) <= max_exact) then begin
     FStar_Pervasives_Native.Some ((cmp_ints op vals mask kx f.count))
     end else begin
     FStar_Pervasives_Native.None
     end)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let rec filter_bits : host  ->  Pipeline.param_env  ->  dframe  ->  Pipeline.col_expr  ->  FStar_Pervasives_Native.option<Prims.list<Prims.bool>> = (fun ( h  :  host ) ( env  :  Pipeline.param_env ) ( f  :  dframe ) ( x  :  Pipeline.col_expr ) -> (match (x) with
| Pipeline.Binary (Pipeline.And, a, b) -> begin
     (match ((((filter_bits h env f a)), ((filter_bits h env f b)))) with
| (FStar_Pervasives_Native.Some (p), FStar_Pervasives_Native.Some (q)) -> begin
     FStar_Pervasives_Native.Some ((and_bits p q))
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end)
     end
| Pipeline.Binary (Pipeline.Or, a, b) -> begin
     (match ((((filter_bits h env f a)), ((filter_bits h env f b)))) with
| (FStar_Pervasives_Native.Some (p), FStar_Pervasives_Native.Some (q)) -> begin
     FStar_Pervasives_Native.Some ((or_bits p q))
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end)
     end
| Pipeline.Binary (op, a, b) -> begin
     (match ((cmp_op_of op)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (o) -> begin
     (match ((((col_of f a)), ((const_of env b)))) with
| (FStar_Pervasives_Native.Some (i), FStar_Pervasives_Native.Some (c)) -> begin
     (leaf h f o i c)
     end
| uu___ -> begin
     (match ((((const_of env a)), ((col_of f b)))) with
| (FStar_Pervasives_Native.Some (c), FStar_Pervasives_Native.Some (i)) -> begin
     (leaf h f (flip o) i c)
     end
| uu___1 -> begin
     FStar_Pervasives_Native.None
     end)
     end)
     end)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let filter_kernel : host  ->  Pipeline.param_env  ->  dframe  ->  Pipeline.col_expr  ->  FStar_Pervasives_Native.option<dframe> = (fun ( h  :  host ) ( env  :  Pipeline.param_env ) ( f  :  dframe ) ( pred  :  Pipeline.col_expr ) -> (match ((filter_bits h env f pred)) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.Some (bits) -> begin
     (match (f.sel) with
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.Some ((select1 f (selection bits)))
     end
| FStar_Pervasives_Native.Some (s) -> begin
     FStar_Pervasives_Native.Some ((select1 f (lfilter (is_set bits) s)))
     end)
     end))


let packs : host  ->  ftype  ->  Prims.list<Pipeline.cell>  ->  Prims.bool = (fun ( h  :  host ) ( ty  :  ftype ) ( cells  :  Prims.list<Pipeline.cell> ) -> ((lfor_all (admits h ty) cells) && (match (ty) with
| TDecimal -> begin
     (match ((h.scale_of (pack_texts h cells) (pack_mask cells))) with
| FStar_Pervasives_Native.Some (v) -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end
| uu___ -> begin
     true
     end)))


let norm_cell : host  ->  ftype  ->  Pipeline.cell  ->  Pipeline.cell = (fun ( h  :  host ) ( ty  :  ftype ) ( c  :  Pipeline.cell ) -> (match (((ty), (c))) with
| (TDate, Pipeline.Date (uu___)) -> begin
     Pipeline.Date ((h.date_text (day_of h c)))
     end
| (TStamp (u), Pipeline.Timestamp (uu___)) -> begin
     Pipeline.Timestamp ((h.instant_text u (FStar_Pervasives_Native.fst (instant_of h u c)) ( 
if (Prims.op_Equals u Seconds) then begin
     (Prims.parse_int "0")
     end else begin
     (FStar_Pervasives_Native.snd (instant_of h u c))
     end)))
     end
| uu___ -> begin
     (widen_cell h ty c)
     end))


let pack_cell : host  ->  ftype  ->  Prims.list<Pipeline.cell>  ->  Prims.nat  ->  Pipeline.cell = (fun ( h  :  host ) ( ty  :  ftype ) ( cells  :  Prims.list<Pipeline.cell> ) ( i  :  Prims.nat ) ->  
if (packs h ty cells) then begin
     (norm_cell h ty (at_or Pipeline.Null cells i))
     end else begin
     (widen_cell h ty (at_or Pipeline.Null cells i))
     end)


let column_read : host  ->  Prims.nat  ->  ftype  ->  FStar_Pervasives_Native.option<ccolumn>  ->  Prims.nat  ->  Pipeline.cell = (fun ( h  :  host ) ( n  :  Prims.nat ) ( ty  :  ftype ) ( column  :  FStar_Pervasives_Native.option<ccolumn> ) ( i  :  Prims.nat ) -> (match (column) with
| FStar_Pervasives_Native.Some (c) -> begin
      
if ((Prims.op_Equals (ctype c) ty) && (Prims.op_Equals (clength c) n)) then begin
     (core_cell h c i)
     end else begin
     (pack_cell h ty (pad n (core_cells h c)) i)
     end
     end
| FStar_Pervasives_Native.None -> begin
     (pack_cell h ty (pad n []) i)
     end))


let column_validity : ccolumn  ->  validity = (fun ( c  :  ccolumn ) -> (match (c.cdata) with
| DInts (uu___, v) -> begin
     v
     end
| DFloats (uu___, v) -> begin
     v
     end
| DBools (uu___, v) -> begin
     v
     end
| DStrs (uu___, v) -> begin
     v
     end
| DDates (uu___, v) -> begin
     v
     end
| DStamps (uu___, uu___1, uu___2, v) -> begin
     v
     end
| DDecimals (uu___, v) -> begin
     v
     end))


let cell_ij : host  ->  dframe  ->  Prims.nat  ->  Prims.nat  ->  FStar_Pervasives_Native.option<Pipeline.cell> = (fun ( h  :  host ) ( f  :  dframe ) ( i  :  Prims.nat ) ( j  :  Prims.nat ) -> (match ((vnth (rows_of h f) i)) with
| FStar_Pervasives_Native.Some (r) -> begin
     (vnth r j)
     end
| FStar_Pervasives_Native.None -> begin
     FStar_Pervasives_Native.None
     end))


let typed_in : ctable  ->  (Prims.string * ftype)  ->  Prims.bool = (fun ( t  :  ctable ) ( fld  :  (Prims.string * ftype) ) -> (match ((try_column (FStar_Pervasives_Native.fst fld) t.tcolumns)) with
| FStar_Pervasives_Native.Some (c) -> begin
     ((Prims.op_Equals (ctype c) (FStar_Pervasives_Native.snd fld)) && (Prims.op_Equals (clength c) (row_count t)))
     end
| FStar_Pervasives_Native.None -> begin
     false
     end))


let table_typed : ctable  ->  Prims.bool = (fun ( t  :  ctable ) -> (lfor_all (typed_in t) t.tschema))


let is_normal : host  ->  ftype  ->  Pipeline.cell  ->  Prims.bool = (fun ( h  :  host ) ( ty  :  ftype ) ( c  :  Pipeline.cell ) -> (Prims.op_Equals (norm_cell h ty c) c))


let col_exact : host  ->  ftype  ->  Prims.list<Pipeline.cell>  ->  Prims.bool = (fun ( h  :  host ) ( ty  :  ftype ) ( cells  :  Prims.list<Pipeline.cell> ) -> ((packs h ty cells) && (lfor_all (is_normal h ty) cells)))


let bool_or_null : Pipeline.cell  ->  Prims.bool = (fun ( c  :  Pipeline.cell ) -> (match (c) with
| Pipeline.Bool (uu___) -> begin
     true
     end
| Pipeline.Null -> begin
     true
     end
| uu___ -> begin
     false
     end))


let kleene_and : Pipeline.cell  ->  Pipeline.cell  ->  Pipeline.cell = (fun ( a  :  Pipeline.cell ) ( b  :  Pipeline.cell ) -> (match (((a), (b))) with
| (Pipeline.Bool (false), uu___) -> begin
     Pipeline.Bool (false)
     end
| (uu___, Pipeline.Bool (false)) -> begin
     Pipeline.Bool (false)
     end
| (Pipeline.Bool (true), Pipeline.Bool (true)) -> begin
     Pipeline.Bool (true)
     end
| uu___ -> begin
     Pipeline.Null
     end))


let kleene_or : Pipeline.cell  ->  Pipeline.cell  ->  Pipeline.cell = (fun ( a  :  Pipeline.cell ) ( b  :  Pipeline.cell ) -> (match (((a), (b))) with
| (Pipeline.Bool (true), uu___) -> begin
     Pipeline.Bool (true)
     end
| (uu___, Pipeline.Bool (true)) -> begin
     Pipeline.Bool (true)
     end
| (Pipeline.Bool (false), Pipeline.Bool (false)) -> begin
     Pipeline.Bool (false)
     end
| uu___ -> begin
     Pipeline.Null
     end))




