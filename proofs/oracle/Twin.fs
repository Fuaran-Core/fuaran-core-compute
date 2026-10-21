module Twin
type twin = {tname : Prims.string; tholds : unit  ->  Prims.bool}


let __proj__Mktwin__item__tname : twin  ->  Prims.string = (fun ( projectee  :  twin ) -> (match (projectee) with
| {tname = tname; tholds = tholds} -> begin
     tname
     end))


let __proj__Mktwin__item__tholds : twin  ->  unit  ->  Prims.bool = (fun ( projectee  :  twin ) -> (match (projectee) with
| {tname = tname; tholds = tholds} -> begin
     tholds
     end))


let rec twins_hold : Prims.list<twin>  ->  Prims.bool = (fun ( l  :  Prims.list<twin> ) -> (match (l) with
| [] -> begin
     true
     end
| (t)::r -> begin
     ((t.tholds ()) && (twins_hold r))
     end))


let twin_true : twin = {tname = "true"; tholds = (fun ( uu___  :  unit ) -> true)}


let twin_false : twin = {tname = "false"; tholds = (fun ( uu___  :  unit ) -> false)}


let twins : Prims.list<twin> = ({tname = "twins-hold-of-no-twins"; tholds = (fun ( uu___  :  unit ) -> (twins_hold []))})::({tname = "twins-hold-of-holding-twins"; tholds = (fun ( uu___  :  unit ) -> (twins_hold ((twin_true)::(twin_true)::[])))})::({tname = "twins-hold-refuses-one-failing-twin"; tholds = (fun ( uu___  :  unit ) -> (not ((twins_hold ((twin_true)::(twin_false)::[])))))})::[]




