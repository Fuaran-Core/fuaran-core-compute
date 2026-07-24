module Fuaran.Core.Tests.DataFrameTests

open Expecto
open Fuaran.Core

// ---- helpers ----

let private col name ty cells : Column = Column.create name ty cells
let private tbl schema columns : Table = { Schema = schema; Columns = columns }

/// A small employees table for the verb tests.
let private people: Table =
    tbl
        [ "dept", StringType
          "name", StringType
          "salary", IntType
          "bonus", FloatType ]
        [ col "dept" StringType [ Str "eng"; Str "eng"; Str "sales"; Str "sales"; Str "eng" ]
          col "name" StringType [ Str "ana"; Str "bob"; Str "cy"; Str "dee"; Str "el" ]
          col "salary" IntType [ Int 100; Int 120; Int 90; Int 90; Null ]
          col "bonus" FloatType [ Float 1.5; Null; Float 2.0; Float 2.5; Float 0.5 ] ]

let private run pipeline = DataFrame.evalPipeline pipeline people

let private okTable =
    function
    | Ok t -> t
    | Error e -> failtestf "eval failed: %s" (DataFrame.errorString e)

let private cellsOf name t =
    Table.tryColumn name t
    |> Option.map (fun c -> c.Cells)
    |> Option.defaultValue []

[<Tests>]
let tests =
    testList
        "DataFrame"
        [ testCase "Filter keeps rows whose predicate is Bool true; null/false drop"
          <| fun _ ->
              let t = run [ Filter(Binary(Gt, Col "salary", Lit(Int 95))) ] |> okTable
              // salary > 95: 100,120 keep; 90,90 drop; null drops (null > 95 = null)
              Expect.equal (cellsOf "name" t) [ Str "ana"; Str "bob" ] "only the two >95 rows survive"

          testCase "Project keeps + renames columns in order"
          <| fun _ ->
              let t = run [ Project [ "name", "who"; "salary", "pay" ] ] |> okTable
              Expect.equal (Table.columnNames t) [ "who"; "pay" ] "projected/renamed columns"
              Expect.equal (cellsOf "pay" t) [ Int 100; Int 120; Int 90; Int 90; Null ] "values preserved"

          testCase "Derive adds a computed column; int+int stays int"
          <| fun _ ->
              let t = run [ Derive("raise", Binary(Add, Col "salary", Lit(Int 10))) ] |> okTable

              Expect.equal
                  (cellsOf "raise" t)
                  [ Int 110; Int 130; Int 100; Int 100; Null ]
                  "int arithmetic + null propagation"

          testCase "coercion: int + float promotes to float"
          <| fun _ ->
              let t = run [ Derive("tot", Binary(Add, Col "salary", Col "bonus")) ] |> okTable

              Expect.equal
                  (cellsOf "tot" t)
                  [ Float 101.5; Null; Float 92.0; Float 92.5; Null ]
                  "int+float ⇒ float, null propagates"

          testCase "Coalesce picks the first non-null"
          <| fun _ ->
              let t = run [ Derive("b2", Coalesce [ Col "bonus"; Lit(Float 0.0) ]) ] |> okTable
              Expect.equal (cellsOf "b2" t) [ Float 1.5; Float 0.0; Float 2.0; Float 2.5; Float 0.5 ] "null filled"

          testCase "Case evaluates the first true branch"
          <| fun _ ->
              let band =
                  Case([ Binary(Ge, Col "salary", Lit(Int 100)), Lit(Str "hi") ], Lit(Str "lo"))

              let t = run [ Derive("band", band) ] |> okTable

              Expect.equal
                  (cellsOf "band" t)
                  [ Str "hi"; Str "hi"; Str "lo"; Str "lo"; Str "lo" ]
                  "null salary → else (lo)"

          testCase "GroupBy with the aggregate suite, groups in first-appearance order"
          <| fun _ ->
              let t =
                  run
                      [ GroupBy(
                            [ "dept" ],
                            [ { Name = "n"
                                Fn = Count
                                Of = "salary" }
                              { Name = "total"
                                Fn = Sum
                                Of = "salary" }
                              { Name = "avg"
                                Fn = Mean
                                Of = "salary" }
                              { Name = "top"
                                Fn = Max
                                Of = "salary" } ]
                        ) ]
                  |> okTable

              Expect.equal (cellsOf "dept" t) [ Str "eng"; Str "sales" ] "groups in first-appearance order"
              Expect.equal (cellsOf "n" t) [ Int 2; Int 2 ] "Count counts non-null (eng has a null salary)"
              Expect.equal (cellsOf "total" t) [ Int 220; Int 180 ] "Sum keeps int type, skips null"
              Expect.equal (cellsOf "avg" t) [ Float 110.0; Float 90.0 ] "Mean is float over present values"
              Expect.equal (cellsOf "top" t) [ Int 120; Int 90 ] "Max keeps source type"

          testCase "Sort places nulls last regardless of direction; stable"
          <| fun _ ->
              let asc = run [ Sort [ "salary", Asc ] ] |> okTable

              Expect.equal
                  (cellsOf "salary" asc)
                  [ Int 90; Int 90; Int 100; Int 120; Null ]
                  "asc, nulls last, stable ties"

              let desc = run [ Sort [ "salary", Desc ] ] |> okTable
              Expect.equal (cellsOf "salary" desc) [ Int 120; Int 100; Int 90; Int 90; Null ] "desc, nulls still last"

          testCase "Distinct dedupes whole rows, first occurrence wins"
          <| fun _ ->
              let t = run [ Project [ "dept", "dept" ]; Distinct ] |> okTable

              Expect.equal (cellsOf "dept" t) [ Str "eng"; Str "sales" ] "distinct depts in order"

          testCase "Limit with offset"
          <| fun _ ->
              let t = run [ Limit(2, 1) ] |> okTable
              Expect.equal (cellsOf "name" t) [ Str "bob"; Str "cy" ] "skip 1, take 2"

          testCase "Window RowNumber partitions + orders"
          <| fun _ ->
              let spec =
                  { PartitionBy = [ "dept" ]
                    OrderBy = [ "name", Asc ]
                    Fn = RowNumber
                    Of = "name"
                    As = "rn" }

              let t = run [ Window spec ] |> okTable
              // rows stay in input order; rn numbers within dept by name order
              // eng: ana(1) bob(2) el(3); sales: cy(1) dee(2)
              Expect.equal
                  (cellsOf "rn" t)
                  [ Int 1; Int 2; Int 1; Int 2; Int 3 ]
                  "per-partition row numbers, input order restored"

          testCase "Window CumulSum is a running float total per partition"
          <| fun _ ->
              let spec =
                  { PartitionBy = [ "dept" ]
                    OrderBy = [ "name", Asc ]
                    Fn = CumulSum
                    Of = "salary"
                    As = "cs" }

              let t = run [ Window spec ] |> okTable
              // eng by name: ana100, bob120, el(null→0): 100,220,220 ; sales: cy90, dee90: 90,180
              Expect.equal
                  (cellsOf "cs" t)
                  [ Float 100.0; Float 220.0; Float 90.0; Float 180.0; Float 220.0 ]
                  "cumulative sum per partition"

          testCase "Join inner on a key"
          <| fun _ ->
              let deptInfo =
                  tbl
                      [ "dept", StringType; "region", StringType ]
                      [ col "dept" StringType [ Str "eng"; Str "sales" ]
                        col "region" StringType [ Str "north"; Str "south" ] ]

              let t = run [ Join(Embedded deptInfo, [ "dept", "dept" ], Inner) ] |> okTable

              Expect.equal
                  (cellsOf "region" t)
                  [ Str "north"; Str "north"; Str "south"; Str "south"; Str "north" ]
                  "region joined per dept"

              Expect.stringContains
                  (String.concat "," (Table.columnNames t))
                  "dept_right"
                  "colliding right key is suffixed"

          testCase "Union concatenates matching-schema rows"
          <| fun _ ->
              let t =
                  run [ Limit(1, 0); Union(Embedded(run [ Limit(1, 4) ] |> okTable)) ] |> okTable

              Expect.equal (cellsOf "name" t) [ Str "ana"; Str "el" ] "first row ∪ last row"

          testCase "Pivot spreads on-values into columns"
          <| fun _ ->
              let t =
                  run
                      [ Pivot
                            { Index = [ "dept" ]
                              On = "name"
                              Values = "salary"
                              Agg = Max } ]
                  |> okTable
              // columns = dept + each distinct name (sorted): ana,bob,cy,dee,el
              Expect.equal (Table.columnNames t) [ "dept"; "ana"; "bob"; "cy"; "dee"; "el" ] "one column per name"
              Expect.equal (cellsOf "ana" t) [ Int 100; Null ] "eng.ana=100, sales.ana=null"

          testCase "Unpivot melts value columns into (variable, value)"
          <| fun _ ->
              let small =
                  tbl
                      [ "id", IntType; "x", IntType; "y", IntType ]
                      [ col "id" IntType [ Int 1 ]
                        col "x" IntType [ Int 7 ]
                        col "y" IntType [ Int 8 ] ]

              let t = DataFrame.evalPipeline [ Unpivot([ "id" ], [ "x"; "y" ]) ] small |> okTable
              Expect.equal (Table.columnNames t) [ "id"; "variable"; "value" ] "melt shape"
              Expect.equal (cellsOf "variable" t) [ Str "x"; Str "y" ] "one row per value var"
              Expect.equal (cellsOf "value" t) [ Int 7; Int 8 ] "values melted"

          testCase "scalar functions: round half away from zero, upper, length, datePart"
          <| fun _ ->
              let s =
                  tbl
                      [ "f", FloatType; "name", StringType; "d", DateType ]
                      [ col "f" FloatType [ Float 2.5; Float -2.5 ]
                        col "name" StringType [ Str "ab"; Str "cde" ]
                        col "d" DateType [ Date "2026-06-22"; Date "1999-12-31" ] ]

              let t =
                  DataFrame.evalPipeline
                      [ Derive("r", ApplyFn(Round, [ Col "f" ]))
                        Derive("u", ApplyFn(Upper, [ Col "name" ]))
                        Derive("len", ApplyFn(Length, [ Col "name" ]))
                        Derive("yr", ApplyFn(DatePart, [ Lit(Str "year"); Col "d" ])) ]
                      s
                  |> okTable

              Expect.equal (cellsOf "r" t) [ Float 3.0; Float -3.0 ] "half away from zero (not banker's)"
              Expect.equal (cellsOf "u" t) [ Str "AB"; Str "CDE" ] "upper"
              Expect.equal (cellsOf "len" t) [ Int 2; Int 3 ] "length"
              Expect.equal (cellsOf "yr" t) [ Int 2026; Int 1999 ] "datePart year"

          testCase "float canonicalisation: derived floats encode via the Wire layout"
          <| fun _ ->
              let t = run [ Derive("d", Binary(Div, Col "salary", Lit(Int 3))) ] |> okTable
              let json = ColumnCodec.encode (Embedded t)
              Expect.stringContains json (Json.render (JFloat(100.0 / 3.0))) "100/3 renders canonically"

          testCase "unknown column is a named EvalError, not a throw"
          <| fun _ ->
              match DataFrame.evalPipeline [ Filter(Col "nope") ] people with
              | Error(UnknownColumn("nope", _)) -> ()
              | other -> failtestf "expected UnknownColumn, got %A" other

          // ---- wire codec ----

          testCase "every verb round-trips through the canonical pipeline codec"
          <| fun _ ->
              let pipeline =
                  [ Filter(
                        Binary(And, Binary(Gt, Col "salary", Lit(Int 50)), Not(Binary(Eq, Col "dept", Lit(Str "x"))))
                    )
                    Project [ "dept", "dept"; "salary", "salary" ]
                    Derive("c", Coalesce [ Col "salary"; Lit Null ])
                    GroupBy([ "dept" ], [ { Name = "s"; Fn = Sum; Of = "salary" } ])
                    Join(Embedded people, [ "dept", "dept" ], Left)
                    Window
                        { PartitionBy = [ "dept" ]
                          OrderBy = [ "s", Desc ]
                          Fn = Rank
                          Of = "s"
                          As = "rk" }
                    Pivot
                        { Index = [ "dept" ]
                          On = "s"
                          Values = "s"
                          Agg = Mean }
                    Unpivot([ "dept" ], [ "s" ])
                    Sort [ "dept", Asc ]
                    Distinct
                    Limit(10, 0)
                    Union(Embedded people)
                    Derive("cast", Cast(FloatType, ApplyFn(Substr, [ Lit(Str "hello"); Lit(Int 1); Lit(Int 3) ]))) ]

              let once = DataFrameCodec.encodePipeline pipeline

              match DataFrameCodec.decodePipeline once with
              | Error e -> failtestf "decode failed: %s" (ColumnCodec.errorString e)
              | Ok p2 ->
                  Expect.equal p2 pipeline "decode reproduces the pipeline"
                  Expect.equal (DataFrameCodec.encodePipeline p2) once "re-encode is byte-identical"

          // ---- Phase 89 — the flat filter-step coercion ----

          testCase "Phase 89 — flat param filter coerces to the canonical predicate + round-trips"
          <| fun _ ->
              let flat = """[{"$type":"filter","column":"variety","op":"eq","param":"variety"}]"""

              let canonical =
                  DataFrameCodec.encodePipeline [ Filter(Binary(Eq, Col "variety", Param "variety")) ]

              match DataFrameCodec.decodePipeline flat with
              | Error e -> failtestf "decode failed: %s" (ColumnCodec.errorString e)
              | Ok p ->
                  Expect.equal
                      p
                      [ Filter(Binary(Eq, Col "variety", Param "variety")) ]
                      "coerces to the nested predicate"

                  Expect.equal (DataFrameCodec.encodePipeline p) canonical "re-encodes to the canonical bytes"

          testCase "Phase 89 — flat value filter coerces (scalar literal right-hand side)"
          <| fun _ ->
              match DataFrameCodec.decodePipeline """[{"$type":"filter","column":"tonnes","op":"gt","value":4}]""" with
              | Ok [ Filter(Binary(Gt, Col "tonnes", Lit(Int 4))) ] -> ()
              | other -> failtestf "expected the coerced literal predicate, got %A" other

          testCase "Phase 89 — a flat op outside the binary roster rejects with the enumeration"
          <| fun _ ->
              // `contains` was the original probe here; it COERCES as of Phase 90 — `like` stays out.
              match
                  DataFrameCodec.decodePipeline """[{"$type":"filter","column":"desk","op":"like","param":"search"}]"""
              with
              | Error(UnknownType("like", expected)) ->
                  Expect.contains expected "eq" "roster is enumerated"
                  Expect.contains expected "contains" "the Phase-90 string ops joined the roster"
              | other -> failtestf "expected UnknownType like, got %A" other

          testCase "Phase 89 — both param AND value rejects didactically"
          <| fun _ ->
              match
                  DataFrameCodec.decodePipeline """[{"$type":"filter","column":"x","op":"eq","param":"p","value":1}]"""
              with
              | Error(MalformedShape d) -> Expect.stringContains d "exactly ONE" "names the choice"
              | other -> failtestf "expected MalformedShape, got %A" other

          testCase "Phase 89 — a filter step with neither pred nor the flat triple names both forms"
          <| fun _ ->
              match DataFrameCodec.decodePipeline """[{"$type":"filter"}]""" with
              | Error(MalformedShape d) ->
                  Expect.stringContains d "flat short form" "names the flat form"
                  Expect.stringContains d "pred" "names the canonical form"
              | other -> failtestf "expected MalformedShape, got %A" other

          // ---- Phase 90 — expression-algebra completion ----

          testCase "Phase 90 — Contains filters ordinally; the flat form coerces it"
          <| fun _ ->
              let t = run [ Filter(Binary(Contains, Col "name", Lit(Str "e"))) ] |> okTable
              Expect.equal (cellsOf "name" t) [ Str "dee"; Str "el" ] "substring match"

              match
                  DataFrameCodec.decodePipeline """[{"$type":"filter","column":"desk","op":"contains","param":"q"}]"""
              with
              | Ok [ Filter(Binary(Contains, Col "desk", Param "q")) ] -> ()
              | other -> failtestf "expected the coerced contains predicate, got %A" other

          testCase "Phase 90 — the case-insensitive search idiom (Lower both sides)"
          <| fun _ ->
              let pred =
                  Binary(Contains, ApplyFn(Lower, [ Col "name" ]), ApplyFn(Lower, [ Lit(Str "AN") ]))

              let t = run [ Filter pred ] |> okTable
              Expect.equal (cellsOf "name" t) [ Str "ana" ] "ANA matches an"

          testCase "Phase 90 — StartsWith / EndsWith + null propagation through string predicates"
          <| fun _ ->
              let t = run [ Filter(Binary(StartsWith, Col "name", Lit(Str "d"))) ] |> okTable
              Expect.equal (cellsOf "name" t) [ Str "dee" ] "startsWith"
              let t2 = run [ Filter(Binary(EndsWith, Col "name", Lit(Str "b"))) ] |> okTable
              Expect.equal (cellsOf "name" t2) [ Str "bob" ] "endsWith"
              let t3 = run [ Derive("x", Binary(Contains, Lit Null, Lit(Str "a"))) ] |> okTable
              Expect.equal (cellsOf "x" t3) [ Null; Null; Null; Null; Null ] "null operand propagates"

          testCase "Phase 90 — Concat stringifies non-null args; any null propagates"
          <| fun _ ->
              let e = ApplyFn(Concat, [ Col "name"; Lit(Str " #"); Col "salary" ])

              let t = run [ Derive("label", e) ] |> okTable

              Expect.equal
                  (cellsOf "label" t)
                  [ Str "ana #100"; Str "bob #120"; Str "cy #90"; Str "dee #90"; Null ]
                  "ints stringify like Cast StringType; el's null salary propagates"

          testCase "Phase 90 — Trim strips exactly the pinned ASCII set"
          <| fun _ ->
              let e = ApplyFn(Trim, [ Lit(Str "\t  padded\r\n ") ])
              let t = run [ Derive("x", e) ] |> okTable
              Expect.equal (List.head (cellsOf "x" t)) (Str "padded") "space/tab/CR/LF stripped"
              // U+00A0 (NBSP) is NOT in the pinned set — .NET IsWhiteSpace would strip it; we must not.
              let nb = run [ Derive("x", ApplyFn(Trim, [ Lit(Str "\u00A0x") ])) ] |> okTable
              Expect.equal (List.head (cellsOf "x" nb)) (Str "\u00A0x") "NBSP survives (parity pin)"

          testCase "Phase 90 — Replace is literal replace-all; empty find is the identity"
          <| fun _ ->
              let go find repl subj =
                  let e = ApplyFn(Replace, [ Lit(Str subj); Lit(Str find); Lit(Str repl) ])
                  run [ Derive("x", e) ] |> okTable |> cellsOf "x" |> List.head

              Expect.equal (go "a" "o" "banana") (Str "bonono") "replace-all"
              Expect.equal (go "" "o" "banana") (Str "banana") "empty find => unchanged (pinned)"

          testCase "Phase 90 — DateDiffDays is civil-day arithmetic (leap year pinned)"
          <| fun _ ->
              let go a b =
                  let e = ApplyFn(DateDiffDays, [ Lit(Str a); Lit(Str b) ])
                  run [ Derive("x", e) ] |> okTable |> cellsOf "x" |> List.head

              Expect.equal (go "2024-02-28" "2024-03-01") (Int 2) "2024 is a leap year"
              Expect.equal (go "2023-02-28" "2023-03-01") (Int 1) "2023 is not"
              Expect.equal (go "2026-07-18" "2026-07-01") (Int(-17)) "negative when `to` is earlier"
              Expect.equal (go "2026-07-18" "2026-07-18T14:30:00") (Int 0) "timestamp slices to its date"

              match run [ Derive("x", ApplyFn(DateDiffDays, [ Lit(Str "yesterday"); Lit(Str "2026-07-18") ])) ] with
              | Error(TypeError d) -> Expect.stringContains d "YYYY-MM-DD" "didactic parse reject"
              | other -> failtestf "expected TypeError, got %A" other

          testCase "Phase 90 — InList is SQL three-valued membership"
          <| fun _ ->
              let e = InList(Col "salary", [ Lit(Int 90); Lit Null ])
              let t = run [ Derive("m", e) ] |> okTable

              Expect.equal
                  (cellsOf "m" t)
                  [ Null; Null; Bool true; Bool true; Null ]
                  "match => true; no match past a null item => null; null subject => null"

              let plain = InList(Col "salary", [ Lit(Int 100); Lit(Int 120) ])
              let t2 = run [ Filter plain ] |> okTable
              Expect.equal (cellsOf "name" t2) [ Str "ana"; Str "bob" ] "filter keeps only true"

          testCase "Phase 90 — IsNull is total (never null) and filters the honest way"
          <| fun _ ->
              let t = run [ Derive("miss", IsNull(Col "bonus")) ] |> okTable

              Expect.equal
                  (cellsOf "miss" t)
                  [ Bool false; Bool true; Bool false; Bool false; Bool false ]
                  "always Bool"

              let t2 = run [ Filter(IsNull(Col "salary")) ] |> okTable
              Expect.equal (cellsOf "name" t2) [ Str "el" ] "the null-salary row"

          testCase "Phase 90 — the new wire forms round-trip byte-stably"
          <| fun _ ->
              let p =
                  [ Filter(Binary(Contains, ApplyFn(Lower, [ Col "name" ]), ApplyFn(Lower, [ Param "q" ])))
                    Filter(InList(Col "dept", [ Lit(Str "eng"); Lit(Str "ops") ]))
                    Filter(Not(IsNull(Col "bonus")))
                    Derive("label", ApplyFn(Concat, [ Col "name"; Lit(Str " ("); Col "dept"; Lit(Str ")") ]))
                    Derive("days", ApplyFn(DateDiffDays, [ Col "name"; Lit(Str "2026-07-18") ])) ]

              let bytes = DataFrameCodec.encodePipeline p

              match DataFrameCodec.decodePipeline bytes with
              | Error e -> failtestf "round-trip decode failed: %s" (ColumnCodec.errorString e)
              | Ok p2 ->
                  Expect.equal p2 p "tree-identical"
                  Expect.equal (DataFrameCodec.encodePipeline p2) bytes "byte-identical"

          testCase "Phase 90 — malformed in/isNull reject didactically; the expr roster names them"
          <| fun _ ->
              match
                  DataFrameCodec.decodePipeline
                      """[{"$type":"filter","pred":{"$type":"in","expr":{"$type":"col","name":"x"}}}]"""
              with
              | Error(MissingField "items") -> ()
              | other -> failtestf "expected MissingField items, got %A" other

              match DataFrameCodec.decodePipeline """[{"$type":"filter","pred":{"$type":"frob"}}]""" with
              | Error(UnknownType("frob", expected)) ->
                  Expect.contains expected "in" "roster gained in"
                  Expect.contains expected "isNull" "roster gained isNull"
              | other -> failtestf "expected UnknownType frob, got %A" other

          // ---- Phase 92 — pipeline-step field aliases (pilot-4 census) ----

          testCase "Phase 92 — sort accepts keys/column/descending aliases and normalises"
          <| fun _ ->
              let flat =
                  """[{"$type":"sort","keys":[{"column":"revenue","descending":true},{"column":"name","descending":false}]}]"""

              match DataFrameCodec.decodePipeline flat with
              | Ok [ Sort [ ("revenue", Desc); ("name", Asc) ] as p ] ->
                  let canonical =
                      DataFrameCodec.encodePipeline [ Sort [ "revenue", Desc; "name", Asc ] ]

                  Expect.equal (DataFrameCodec.encodePipeline [ p ]) canonical "re-encodes canonically"
              | other -> failtestf "expected the coerced sort, got %A" other

          testCase "Phase 92 — groupBy accepts by/aggregations/{column,op,as} + avg and normalises"
          <| fun _ ->
              let flat =
                  """[{"$type":"groupBy","by":["dept"],"aggregations":[{"column":"salary","op":"avg","as":"avgPay"}]}]"""

              match DataFrameCodec.decodePipeline flat with
              | Ok [ GroupBy([ "dept" ],
                             [ { Name = "avgPay"
                                 Fn = Mean
                                 Of = "salary" } ]) ] -> ()
              | other -> failtestf "expected the coerced groupBy, got %A" other

          testCase "Phase 92 — limit accepts count and defaults offset to 0"
          <| fun _ ->
              match DataFrameCodec.decodePipeline """[{"$type":"limit","count":10}]""" with
              | Ok [ Limit(10, 0) ] -> ()
              | other -> failtestf "expected Limit(10,0), got %A" other

          testCase "Phase 92 — both canonical and alias present rejects didactically"
          <| fun _ ->
              match DataFrameCodec.decodePipeline """[{"$type":"limit","n":5,"count":10}]""" with
              | Error(MalformedShape d) -> Expect.stringContains d "not both" "names the ambiguity"
              | other -> failtestf "expected MalformedShape, got %A" other

              match
                  DataFrameCodec.decodePipeline
                      """[{"$type":"sort","by":[{"col":"x","dir":"asc","descending":true}]}]"""
              with
              | Error(MalformedShape d) -> Expect.stringContains d "descending" "names the alias"
              | other -> failtestf "expected MalformedShape, got %A" other

          // ---- CumulSum rename (wire tag; legacy alias admitted) ----

          testCase "CumulSum — canonical tag is cumulSum; legacy cumSum coerces and normalises"
          <| fun _ ->
              let mk (tag: string) =
                  let template =
                      """[{"$type":"window","partitionBy":["dept"],"orderBy":[{"col":"name","dir":"asc"}],"fn":"FNTAG","of":"salary","as":"running"}]"""

                  template.Replace("FNTAG", tag)

              let canonical = mk "cumulSum"
              let legacy = mk "cumSum"

              match DataFrameCodec.decodePipeline canonical, DataFrameCodec.decodePipeline legacy with
              | Ok p1, Ok p2 ->
                  Expect.equal p1 p2 "legacy alias decodes to the same tree"

                  Expect.stringContains
                      (DataFrameCodec.encodePipeline p2)
                      "cumulSum"
                      "re-encode emits the canonical tag"
              | a, b -> failtestf "decode failed: %A" (a, b)

          // ---- Phase 91 — list-valued params (InParam) ----

          testCase "Phase 91 — in with param decodes to InParam; both items+param rejects"
          <| fun _ ->
              let src =
                  """[{"$type":"filter","pred":{"$type":"in","expr":{"$type":"col","name":"dept"},"param":"depts"}}]"""

              match DataFrameCodec.decodePipeline src with
              | Ok([ Filter(InParam(Col "dept", "depts")) ] as p) ->
                  match DataFrameCodec.decodePipeline (DataFrameCodec.encodePipeline p) with
                  | Ok p2 -> Expect.equal p2 p "round-trips"
                  | Error e -> failtestf "round-trip failed: %s" (ColumnCodec.errorString e)
              | other -> failtestf "expected InParam, got %A" other

              match
                  DataFrameCodec.decodePipeline
                      """[{"$type":"filter","pred":{"$type":"in","expr":{"$type":"col","name":"d"},"items":[],"param":"p"}}]"""
              with
              | Error(MalformedShape d) -> Expect.stringContains d "exactly ONE" "names the choice"
              | other -> failtestf "expected MalformedShape, got %A" other

          testCase "Phase 91 — substituteListParams binds the selection; unbound InParam is strict"
          <| fun _ ->
              let pipeline = [ Filter(InParam(Col "dept", "depts")) ]
              Expect.equal (Transform.paramsOf pipeline) [ "depts" ] "list params surface in paramsOf"

              let bound =
                  Transform.substituteListParams (Map.ofList [ "depts", [ Str "eng" ] ]) pipeline

              Expect.equal
                  (run bound |> okTable |> cellsOf "name")
                  [ Str "ana"; Str "bob"; Str "el" ]
                  "filters by the bound selection"

              match run pipeline with
              | Error(UnboundParam("depts", _)) -> ()
              | other -> failtestf "expected UnboundParam, got %A" other

          // ---- Phase 93 — the stretch-wave-2 alias wave ----

          testCase "Phase 93 — predicate aliases pred; expr-level contains + call/apply coerce"
          <| fun _ ->
              // The exact tier-a-055 shakedown shape: predicate + expr-level contains over
              // call/lower on both sides -> the canonical nested Binary(Contains, ...).
              let src =
                  """[{"$type":"filter","predicate":{"$type":"contains","expr":{"$type":"call","fn":"lower","args":[{"$type":"col","name":"name"}]},"other":{"$type":"call","fn":"lower","args":[{"$type":"param","name":"search"}]}}}]"""

              match DataFrameCodec.decodePipeline src with
              | Ok([ Filter(Binary(Contains, ApplyFn(Lower, [ Col "name" ]), ApplyFn(Lower, [ Param "search" ]))) ] as p) ->
                  let canonical = DataFrameCodec.encodePipeline p

                  match DataFrameCodec.decodePipeline canonical with
                  | Ok p2 -> Expect.equal p2 p "normalises + round-trips"
                  | Error e -> failtestf "round-trip failed: %s" (ColumnCodec.errorString e)
              | other -> failtestf "expected the coerced contains predicate, got %A" other

          testCase "Phase 93 — the tier-a-057 shape: predicate + in/param coerces to InParam"
          <| fun _ ->
              let src =
                  """[{"$type":"filter","predicate":{"$type":"in","expr":{"$type":"col","name":"category"},"param":"cats"}}]"""

              match DataFrameCodec.decodePipeline src with
              | Ok [ Filter(InParam(Col "category", "cats")) ] -> ()
              | other -> failtestf "expected InParam, got %A" other

          testCase "Phase 93 — sort-entry direction spelling + directionless default asc"
          <| fun _ ->
              match
                  DataFrameCodec.decodePipeline
                      """[{"$type":"sort","by":[{"column":"revenue","direction":"desc"},{"column":"name"}]}]"""
              with
              | Ok [ Sort [ ("revenue", Desc); ("name", Asc) ] ] -> ()
              | other -> failtestf "expected the coerced sort, got %A" other

          testCase "Phase 93 — both pred and predicate rejects; left+expr rejects"
          <| fun _ ->
              match
                  DataFrameCodec.decodePipeline
                      """[{"$type":"filter","pred":{"$type":"col","name":"x"},"predicate":{"$type":"col","name":"x"}}]"""
              with
              | Error(MalformedShape d) -> Expect.stringContains d "not both" "names the ambiguity"
              | other -> failtestf "expected MalformedShape, got %A" other

          // ---- Phase 94 — the pilot-5 lenient wave (flat logical/comparison spellings) ----

          testCase "Phase 94 — the pilot-5 shape: variadic or/exprs folds to nested Binary(Or)"
          <| fun _ ->
              // The exact gemini n=1 emission shape (tier-a-025/050/051): a flat OR node
              // with an exprs array over binary comparisons.
              let src =
                  """[{"$type":"filter","pred":{"$type":"or","exprs":[{"$type":"binary","op":"eq","left":{"$type":"col","name":"a"},"right":{"$type":"lit","cell":{"$type":"Int","value":1}}},{"$type":"binary","op":"eq","left":{"$type":"col","name":"a"},"right":{"$type":"lit","cell":{"$type":"Int","value":2}}},{"$type":"binary","op":"eq","left":{"$type":"col","name":"a"},"right":{"$type":"lit","cell":{"$type":"Int","value":3}}}]}}]"""

              match DataFrameCodec.decodePipeline src with
              | Ok([ Filter(Binary(Or, Binary(Or, Binary(Eq, _, _), Binary(Eq, _, _)), Binary(Eq, _, _))) ] as p) ->
                  let canonical = DataFrameCodec.encodePipeline p

                  match DataFrameCodec.decodePipeline canonical with
                  | Ok p2 -> Expect.equal p2 p "normalises + round-trips"
                  | Error e -> failtestf "round-trip failed: %s" (ColumnCodec.errorString e)
              | other -> failtestf "expected the left-folded Or tree, got %A" other

          testCase "Phase 94 — flat and with left/right; flat eq with expr/other aliases"
          <| fun _ ->
              let src =
                  """[{"$type":"filter","pred":{"$type":"and","left":{"$type":"eq","expr":{"$type":"col","name":"x"},"other":{"$type":"param","name":"p"}},"right":{"$type":"gt","left":{"$type":"col","name":"y"},"right":{"$type":"lit","cell":{"$type":"Int","value":0}}}}}]"""

              match DataFrameCodec.decodePipeline src with
              | Ok [ Filter(Binary(And, Binary(Eq, Col "x", Param "p"), Binary(Gt, Col "y", Lit(Int 0)))) ] -> ()
              | other -> failtestf "expected the coerced And/Eq/Gt tree, got %A" other

          testCase "Phase 94 — a single-element or/exprs collapses to the inner expr; empty rejects"
          <| fun _ ->
              match
                  DataFrameCodec.decodePipeline
                      """[{"$type":"filter","pred":{"$type":"or","exprs":[{"$type":"col","name":"flag"}]}}]"""
              with
              | Ok [ Filter(Col "flag") ] -> ()
              | other -> failtestf "expected the collapsed single expr, got %A" other

              match DataFrameCodec.decodePipeline """[{"$type":"filter","pred":{"$type":"or","exprs":[]}}]""" with
              | Error(MalformedShape d) -> Expect.stringContains d "non-empty" "names the constraint"
              | other -> failtestf "expected MalformedShape, got %A" other

          testCase "Phase 94 — flat scalar-fn spellings: fn-alias node + bare fn-name node (args/expr)"
          <| fun _ ->
              // The observed shapes: {"$type":"fn","fn":"lower","args":[…]} (opus@low,
              // tier-a-051) and {"$type":"lower","expr":…} (gemini, tier-a-055).
              let src =
                  """[{"$type":"filter","pred":{"$type":"binary","left":{"$type":"fn","fn":"lower","args":[{"$type":"col","name":"desk"}]},"op":"contains","right":{"$type":"lower","expr":{"$type":"param","name":"q"}}}}]"""

              match DataFrameCodec.decodePipeline src with
              | Ok([ Filter(Binary(Contains, ApplyFn(Lower, [ Col "desk" ]), ApplyFn(Lower, [ Param "q" ]))) ] as p) ->
                  let canonical = DataFrameCodec.encodePipeline p

                  match DataFrameCodec.decodePipeline canonical with
                  | Ok p2 -> Expect.equal p2 p "normalises + round-trips"
                  | Error e -> failtestf "round-trip failed: %s" (ColumnCodec.errorString e)
              | other -> failtestf "expected the coerced ApplyFn pair, got %A" other

          testCase "pipeline decode rejects an unknown step kind with the enumeration"
          <| fun _ ->
              match DataFrameCodec.decodePipeline """[{"$type":"frobnicate"}]""" with
              | Error(UnknownType("frobnicate", _)) -> ()
              | other -> failtestf "expected UnknownType, got %A" other

          // ---- transformLaws (cross-host parity contract) ----

          testCase "transformLaws certifies the reference against itself (and has teeth)"
          <| fun _ ->
              // a conservative generator of safe (table, pipeline) samples over a fixed schema
              let gen (rng: ConfRng.T) =
                  let pick n r = ConfRng.intBelow n r
                  let n, r1 = pick 4 rng
                  let rows = n + 1

                  let mkInt i =
                      if (i * 7) % 5 = 0 then Null else Int(i * 3 - 4)

                  let table =
                      tbl
                          [ "g", StringType; "v", IntType ]
                          [ col "g" StringType [ for i in 0 .. rows - 1 -> Str(if i % 2 = 0 then "a" else "b") ]
                            col "v" IntType [ for i in 0 .. rows - 1 -> mkInt i ] ]

                  let stepKind, r2 = pick 5 r1

                  let pipeline =
                      match stepKind with
                      | 0 -> [ Filter(Binary(Gt, Col "v", Lit(Int 0))) ]
                      | 1 -> [ Sort [ "v", Asc ]; Distinct ]
                      | 2 -> [ Derive("w", Binary(Add, Col "v", Lit(Int 1))) ]
                      | 3 -> [ GroupBy([ "g" ], [ { Name = "s"; Fn = Sum; Of = "v" } ]) ]
                      | _ -> [ Limit(2, 0) ]

                  (table, pipeline), r2

              match Conformance.transformLaws DataFrame.evalPipeline gen 7 200 with
              | results when results |> List.forall (fun r -> r.Passed) -> ()
              | results ->
                  let bad = results |> List.filter (fun r -> not r.Passed)
                  failtestf "reference self-parity failed: %A" bad

              // teeth: a deliberately-wrong evaluator (drops all rows) must fail parity
              let broken _ (input: Table) =
                  Ok
                      { input with
                          Columns = input.Columns |> List.map (fun c -> { c with Cells = [] }) }

              let teeth = Conformance.transformLaws broken gen 7 50
              Expect.isFalse (teeth |> List.forall (fun r -> r.Passed)) "a wrong evaluator is caught"

          // ---- the shared canonical `$type` discipline (Stage 1 unification) ----

          testCase "transform + ColExpr + Cell wire carries $type, keys Ordinal-sorted"
          <| fun _ ->
              let json = DataFrameCodec.encodePipeline [ Filter(Binary(Gt, Col "x", Lit(Int 5))) ]

              Expect.stringStarts json "[{\"$type\":\"filter\"" "$type is the canonical first key of a step"
              Expect.stringContains json "\"$type\":\"binary\"" "nested ColExpr carries $type"
              Expect.stringContains json "\"$type\":\"col\"" "Col carries $type"
              Expect.stringContains json "\"$type\":\"Int\"" "Cell literal carries $type"
              // no legacy `kind` discriminator survives the realignment
              Expect.isFalse (json.Contains "\"kind\":\"filter\"") "the legacy kind tag is gone"

          // ---- Phase 39: pinned integer-overflow & cast safety ----

          testCase "int Mul that overflows int32 is a named OverflowError, not a wrap"
          <| fun _ ->
              let big = tbl [ "a", IntType ] [ col "a" IntType [ Int 100000; Int 2000000000 ] ]

              match DataFrame.evalPipeline [ Derive("p", Binary(Mul, Col "a", Lit(Int 100000))) ] big with
              | Error(OverflowError _) -> ()
              | other -> failtestf "expected OverflowError, got %A" other

          testCase "int Add at the int32 boundary overflows with a name"
          <| fun _ ->
              let m = tbl [ "a", IntType ] [ col "a" IntType [ Int 2147483647 ] ]

              match DataFrame.evalPipeline [ Derive("p", Binary(Add, Col "a", Lit(Int 1))) ] m with
              | Error(OverflowError _) -> ()
              | other -> failtestf "expected OverflowError, got %A" other

          testCase "in-range int arithmetic is unchanged"
          <| fun _ ->
              let t = run [ Derive("r", Binary(Mul, Col "salary", Lit(Int 2))) ] |> okTable
              Expect.equal (cellsOf "r" t) [ Int 200; Int 240; Int 180; Int 180; Null ] "no overflow for small ints"

          testCase "Sum that overflows int32 is a named OverflowError"
          <| fun _ ->
              let big =
                  tbl
                      [ "g", StringType; "v", IntType ]
                      [ col "g" StringType [ Str "x"; Str "x"; Str "x" ]
                        col "v" IntType [ Int 2000000000; Int 2000000000; Int 2000000000 ] ]

              match DataFrame.evalPipeline [ GroupBy([ "g" ], [ { Name = "s"; Fn = Sum; Of = "v" } ]) ] big with
              | Error(OverflowError _) -> ()
              | other -> failtestf "expected Sum OverflowError, got %A" other

          testCase "Float→Int cast of NaN / Infinity / out-of-range is named, not undefined"
          <| fun _ ->
              let mk f =
                  tbl [ "f", FloatType ] [ col "f" FloatType [ Float f ] ]

              let castInt = [ Derive("i", Cast(IntType, Col "f")) ]

              match DataFrame.evalPipeline castInt (mk (0.0 / 0.0)) with
              | Error(TypeError _) -> ()
              | other -> failtestf "expected TypeError for NaN cast, got %A" other

              match DataFrame.evalPipeline castInt (mk System.Double.PositiveInfinity) with
              | Error(TypeError _) -> ()
              | other -> failtestf "expected TypeError for Infinity cast, got %A" other

              match DataFrame.evalPipeline castInt (mk 5.0e9) with
              | Error(OverflowError _) -> ()
              | other -> failtestf "expected OverflowError for out-of-range cast, got %A" other

          testCase "Float→Int cast of an in-range finite float truncates toward zero"
          <| fun _ ->
              let t =
                  DataFrame.evalPipeline
                      [ Derive("i", Cast(IntType, Col "f")) ]
                      (tbl [ "f", FloatType ] [ col "f" FloatType [ Float 3.9; Float -2.7 ] ])
                  |> okTable

              Expect.equal (cellsOf "i" t) [ Int 3; Int -2 ] "truncation toward zero, unchanged for in-range"

          // ---- Phase 41: canonical float group-key normalisation ----

          testCase "GroupBy collapses NaN keys into one group and -0.0/0.0 into one"
          <| fun _ ->
              let nan = 0.0 / 0.0

              let t =
                  tbl
                      [ "k", FloatType; "v", IntType ]
                      [ col "k" FloatType [ Float nan; Float nan; Float -0.0; Float 0.0 ]
                        col "v" IntType [ Int 1; Int 1; Int 1; Int 1 ] ]

              let g =
                  DataFrame.evalPipeline [ GroupBy([ "k" ], [ { Name = "n"; Fn = Count; Of = "v" } ]) ] t
                  |> okTable

              // two groups: the NaN bucket (2 rows) and the zero bucket (-0.0 and 0.0 coincide, 2 rows)
              Expect.equal (cellsOf "n" g) [ Int 2; Int 2 ] "NaN→one group, ±0.0→one group"

          testCase "Distinct collapses NaN and ±0.0 deterministically"
          <| fun _ ->
              let nan = 0.0 / 0.0

              let t =
                  tbl
                      [ "k", FloatType ]
                      [ col "k" FloatType [ Float nan; Float nan; Float 0.0; Float -0.0; Float 1.5 ] ]

              let d = DataFrame.evalPipeline [ Distinct ] t |> okTable
              // distinct rows: one NaN, one zero, one 1.5
              Expect.equal (List.length (cellsOf "k" d)) 3 "NaN dedups to one, ±0.0 dedups to one"

          // ---- Phase 34: incremental evaluation ----

          testCase "evalFrom reuses the prior result when a changed column is dropped + unread"
          <| fun _ ->
              let oldSrc =
                  tbl
                      [ "a", IntType; "c", IntType ]
                      [ col "a" IntType [ Int 1; Int 2 ]; col "c" IntType [ Int 9; Int 9 ] ]

              // pipeline drops c and never reads it
              let pipeline = [ Project [ "a", "a" ] ]
              let prior = DataFrame.evalPipeline pipeline oldSrc |> okTable

              // c's values change; everything else identical
              let newSrc =
                  tbl
                      [ "a", IntType; "c", IntType ]
                      [ col "a" IntType [ Int 1; Int 2 ]; col "c" IntType [ Int 100; Int 200 ] ]

              let incr = DataFrame.evalFrom prior (ColumnValuesChanged "c") pipeline newSrc
              Expect.equal incr (Ok prior) "irrelevant change reuses the prior result"
              Expect.equal incr (DataFrame.evalPipeline pipeline newSrc) "and equals a full recompute"

          testCase "evalFrom recomputes when the changed column is read or emitted"
          <| fun _ ->
              let oldSrc =
                  tbl
                      [ "a", IntType; "c", IntType ]
                      [ col "a" IntType [ Int 1; Int 2 ]; col "c" IntType [ Int 9; Int 9 ] ]

              let pipeline = [ Filter(Binary(Gt, Col "a", Lit(Int 0))) ] // keeps + reads a, passes c through
              let prior = DataFrame.evalPipeline pipeline oldSrc |> okTable

              let newSrc =
                  tbl
                      [ "a", IntType; "c", IntType ]
                      [ col "a" IntType [ Int 1; Int 2 ]; col "c" IntType [ Int 100; Int 200 ] ]

              // c is in the output (Filter preserves columns) → must recompute, not reuse
              let incr = DataFrame.evalFrom prior (ColumnValuesChanged "c") pipeline newSrc
              Expect.notEqual incr (Ok prior) "c is in the output ⇒ prior is stale"
              Expect.equal incr (DataFrame.evalPipeline pipeline newSrc) "and equals a full recompute"

          testCase "incrementalLaws certify evalFrom ≡ evalPipeline (change- + op-driven) (Phase 34)"
          <| fun _ ->
              let results = Conformance.incrementalLaws 4242 200
              Expect.equal (List.length results) 2 "change-driven + op-driven equivalence reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "incrementalLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.incrementalLaws 4242 200) results "same seed ⇒ identical report"

          // ---- Phase 77: ColExpr.Param + evaluation environment ----

          testCase "a bound Param evaluates to its Cell (filter driven by a runtime scalar)"
          <| fun _ ->
              let env = Map.ofList [ "threshold", Int 95 ]

              let t =
                  DataFrame.evalPipelineInEnv env [ Filter(Binary(Gt, Col "salary", Param "threshold")) ] people

              match t with
              | Ok t ->
                  Expect.equal (cellsOf "name" t) [ Str "ana"; Str "bob" ] "salary > $threshold (95) keeps ana,bob"
              | Error e -> failtestf "eval failed: %s" (DataFrame.errorString e)

          testCase "an unbound Param is UnboundParam naming the param + the bound set, not a throw"
          <| fun _ ->
              let env = Map.ofList [ "other", Int 1 ]

              match DataFrame.evalPipelineInEnv env [ Filter(Binary(Gt, Col "salary", Param "threshold")) ] people with
              | Error(UnboundParam("threshold", bound)) -> Expect.equal bound [ "other" ] "bound set enumerated"
              | other -> failtestf "expected UnboundParam, got %A" other

          testCase "param-free pipelines evaluate byte-identically through the env-less entry points"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "salary", Lit(Int 95))) ]
              let viaPlain = DataFrame.evalPipeline pipeline people
              let viaEmptyEnv = DataFrame.evalPipelineInEnv Map.empty pipeline people
              Expect.equal viaEmptyEnv viaPlain "empty-env eval == plain eval"

          testCase "substituting a bound Param with its Lit evaluates identically (env ≡ substitute)"
          <| fun _ ->
              let env = Map.ofList [ "t", Int 100 ]
              let pipeline = [ Filter(Binary(Ge, Col "salary", Param "t")) ]
              let viaEnv = DataFrame.evalPipelineInEnv env pipeline people
              let viaSubst = DataFrame.evalPipeline (Transform.substitute env pipeline) people
              Expect.equal viaEnv viaSubst "env resolution ≡ literal substitution"

          testCase "Transform.paramsOf derives every referenced param, deduped, stable order"
          <| fun _ ->
              let pipeline =
                  [ Filter(Binary(And, Binary(Gt, Col "salary", Param "lo"), Binary(Lt, Col "salary", Param "hi")))
                    Derive("d", Binary(Add, Col "bonus", Param "lo")) ] // "lo" reused → deduped

              Expect.equal (Transform.paramsOf pipeline) [ "lo"; "hi" ] "first-occurrence order, deduplicated"

          testCase "Param round-trips the pipeline codec with the canonical $type discipline"
          <| fun _ ->
              let pipeline = [ Filter(Binary(Gt, Col "salary", Param "threshold")) ]
              let once = DataFrameCodec.encodePipeline pipeline
              Expect.stringContains once "\"$type\":\"param\"" "Param carries $type=param"
              Expect.stringContains once "\"name\":\"threshold\"" "param name encoded"

              match DataFrameCodec.decodePipeline once with
              | Ok p2 ->
                  Expect.equal p2 pipeline "decode reproduces the param pipeline"
                  Expect.equal (DataFrameCodec.encodePipeline p2) once "re-encode byte-identical"
              | Error e -> failtestf "decode failed: %s" (ColumnCodec.errorString e)

          testCase "corpus fixture: a filter comparing a col against a param round-trips; param-free is byte-stable"
          <| fun _ ->
              // the additive fixture — a `filter` step whose predicate compares a `col` against a `param`
              let paramPipeline = [ Filter(Binary(Gt, Col "salary", Param "threshold")) ]
              // the param-free companion — proves the codec is byte-unchanged for pre-Phase-77 pipelines
              let plainPipeline = [ Filter(Binary(Gt, Col "salary", Lit(Int 95))) ]

              let cases: Corpus.Case list =
                  [ { Name = "filter-col-vs-param"
                      Kind = Corpus.RoundTrip
                      Json = DataFrameCodec.encodePipeline paramPipeline
                      Tag = "param" }
                    { Name = "filter-col-vs-lit (byte-stable)"
                      Kind = Corpus.RoundTrip
                      Json = DataFrameCodec.encodePipeline plainPipeline
                      Tag = "param-free" }
                    { Name = "unknown-expr-kind"
                      Kind = Corpus.Reject
                      Json = """[{"$type":"filter","pred":{"$type":"frobnicate"}}]"""
                      Tag = "reject" } ]

              let outcomes = Corpus.runCorpus DataFrameCodec.pipelineCodec cases
              Expect.all outcomes (fun o -> o.Passed) "every corpus case passes"

              match Corpus.coverageGate [ "param"; "param-free"; "reject" ] cases with
              | Ok() -> ()
              | Error m -> failtest m

              // the param-free fixture's wire is exactly what the pre-Phase-77 encoder produced
              Expect.equal
                  (DataFrameCodec.encodePipeline plainPipeline)
                  "[{\"$type\":\"filter\",\"pred\":{\"$type\":\"binary\",\"left\":{\"$type\":\"col\",\"name\":\"salary\"},\"op\":\"gt\",\"right\":{\"$type\":\"lit\",\"cell\":{\"$type\":\"Int\",\"value\":95}}}}]"
                  "param-free pipeline wire is byte-unchanged (additive proof)"

          testCase "paramLaws certify substitution + unbound defect + paramsOf completeness + codec (Phase 77)"
          <| fun _ ->
              let results = Conformance.paramLaws 7714 200
              Expect.equal (List.length results) 4 "four param laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "paramLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.paramLaws 7714 200) results "same seed ⇒ identical report" ]
