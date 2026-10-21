module Limits

let max_node_depth : Prims.nat = (Prims.parse_int "24")


let max_json_depth : Prims.nat = (Prims.parse_int "256")


let max_string_length : Prims.nat = (Prims.parse_int "1048576")


let max_array_length : Prims.nat = (Prims.parse_int "100000")


let max_total_nodes : Prims.nat = (Prims.parse_int "100000")


let max_document_bytes : Prims.nat = (Prims.parse_int "33554432")


let max_expr_nodes : Prims.nat = (Prims.parse_int "512")


let max_skeleton_rows : Prims.nat = (Prims.parse_int "10000")


let twins : Prims.list<Twin.twin> = ({Twin.tname = "node-depth-is-24"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals max_node_depth (Prims.parse_int "24")))})::({Twin.tname = "json-depth-is-256"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals max_json_depth (Prims.parse_int "256")))})::({Twin.tname = "document-bytes-is-32-mib"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals max_document_bytes (Prims.parse_int "33554432")))})::[]




