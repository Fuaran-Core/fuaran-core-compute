module Limits

let max_node_depth : Prims.nat = (Prims.parse_int "24")


let max_json_depth : Prims.nat = (Prims.parse_int "256")


let max_string_length : Prims.nat = (Prims.parse_int "1048576")


let max_array_length : Prims.nat = (Prims.parse_int "100000")


let max_total_nodes : Prims.nat = (Prims.parse_int "100000")


let max_document_bytes : Prims.nat = (Prims.parse_int "33554432")


let max_expr_nodes : Prims.nat = (Prims.parse_int "512")


let max_skeleton_rows : Prims.nat = (Prims.parse_int "10000")

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


let twins : Prims.list<twin> = ({tname = "node-depth-is-24"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals max_node_depth (Prims.parse_int "24")))})::({tname = "json-depth-is-256"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals max_json_depth (Prims.parse_int "256")))})::({tname = "document-bytes-is-32-mib"; tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals max_document_bytes (Prims.parse_int "33554432")))})::[]




