module Fuaran.Compute.Tests.DataFrameTests

open Expecto
open Fuaran.Core
open Fuaran.Compute

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
              let asc = run [ Transform.sortBy [ "salary", Asc ] ] |> okTable

              Expect.equal
                  (cellsOf "salary" asc)
                  [ Int 90; Int 90; Int 100; Int 120; Null ]
                  "asc, nulls last, stable ties"

              let desc = run [ Transform.sortBy [ "salary", Desc ] ] |> okTable
              Expect.equal (cellsOf "salary" desc) [ Int 120; Int 100; Int 90; Int 90; Null ] "desc, nulls still last"

          testCase "Distinct dedupes whole rows, first occurrence wins"
          <| fun _ ->
              let t = run [ Project [ "dept", "dept" ]; Distinct ] |> okTable

              Expect.equal (cellsOf "dept" t) [ Str "eng"; Str "sales" ] "distinct depts in order"

          testCase "Limit with offset"
          <| fun _ ->
              let t = run [ Transform.limit 2 1 ] |> okTable
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
                  run [ Transform.limit 1 0; Union(Embedded(run [ Transform.limit 1 4 ] |> okTable)) ]
                  |> okTable

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
                    Transform.sortBy [ "dept", Asc ]
                    Distinct
                    Transform.limit 10 0
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
              | Ok [ Sort [ (Slot.Lit "revenue", Desc); (Slot.Lit "name", Asc) ] as p ] ->
                  let canonical =
                      DataFrameCodec.encodePipeline [ Transform.sortBy [ "revenue", Desc; "name", Asc ] ]

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
              | Ok [ Limit(Slot.Lit 10, Slot.Lit 0) ] -> ()
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

          // ---- `0.28.0` (D48) — a column-naming member is spelled out: `columns` / `column` ----
          //
          // Three claims per member, each able to go red on its own: the canonical BYTES (a revert
          // of the encoder fails this line), the pre-rename spelling still DECODING to the same
          // tree and normalising to the canonical bytes on re-encode, and both spellings together
          // being REFUSED by the existing ambiguity error rather than silently preferring one.

          testCase "0.28.0 — project emits `columns`; `cols` decodes to the same tree and normalises"
          <| fun _ ->
              let pipeline = [ Project [ "name", "who"; "salary", "pay" ] ]
              let canonical = DataFrameCodec.encodePipeline pipeline

              Expect.equal
                  canonical
                  "[{\"$type\":\"project\",\"columns\":[{\"a\":\"name\",\"b\":\"who\"},{\"a\":\"salary\",\"b\":\"pay\"}]}]"
                  "the canonical member is `columns`"

              let legacy =
                  """[{"$type":"project","cols":[{"a":"name","b":"who"},{"a":"salary","b":"pay"}]}]"""

              match DataFrameCodec.decodePipeline legacy, DataFrameCodec.decodePipeline canonical with
              | Ok viaAlias, Ok viaCanonical ->
                  Expect.equal viaAlias pipeline "the `cols` alias decodes to the same tree"
                  Expect.equal viaCanonical pipeline "and so does `columns`"

                  Expect.equal
                      (DataFrameCodec.encodePipeline viaAlias)
                      canonical
                      "an aliased document re-encodes as `columns`"
              | other -> failtestf "expected both spellings to decode, got %A" other

          testCase "0.28.0 — project carrying BOTH `columns` and `cols` is refused as ambiguous"
          <| fun _ ->
              match
                  DataFrameCodec.decodePipeline
                      """[{"$type":"project","columns":[{"a":"x","b":"x"}],"cols":[{"a":"y","b":"y"}]}]"""
              with
              | Error(MalformedShape d) ->
                  Expect.stringContains d "\"columns\" (canonical)" "names `columns` as the canonical spelling"
                  Expect.stringContains d "\"cols\" (alias)" "names `cols` as the alias"
                  Expect.stringContains d "not both" "names the ambiguity"
              | other -> failtestf "expected the ambiguity refusal, got %A" other

          testCase "0.28.0 — a sort key emits `column`; `col` decodes to the same tree and normalises"
          <| fun _ ->
              let pipeline = [ Transform.sortBy [ "total", Desc ] ]
              let canonical = DataFrameCodec.encodePipeline pipeline

              Expect.equal
                  canonical
                  "[{\"$type\":\"sort\",\"by\":[{\"column\":\"total\",\"dir\":\"desc\"}]}]"
                  "the canonical member is `column`"

              match DataFrameCodec.decodePipeline """[{"$type":"sort","by":[{"col":"total","dir":"desc"}]}]""" with
              | Ok viaAlias ->
                  Expect.equal viaAlias pipeline "the pre-rename `col` spelling decodes to the same tree"
                  Expect.equal (DataFrameCodec.encodePipeline viaAlias) canonical "and re-encodes as `column`"
              | other -> failtestf "expected the `col` spelling to decode, got %A" other

          testCase "0.28.0 — a sort key carrying BOTH `column` and `col` is refused as ambiguous"
          <| fun _ ->
              match
                  DataFrameCodec.decodePipeline """[{"$type":"sort","by":[{"column":"a","col":"b","dir":"asc"}]}]"""
              with
              | Error(MalformedShape d) ->
                  Expect.stringContains d "\"column\" (canonical)" "names `column` as the canonical spelling"
                  Expect.stringContains d "\"col\" (alias)" "names `col` as the alias"
                  Expect.stringContains d "not both" "names the ambiguity"
              | other -> failtestf "expected the ambiguity refusal, got %A" other

          testCase "0.28.0 — a window's frame ordering emits `column`, and `col` still decodes"
          <| fun _ ->
              let pipeline =
                  [ Window
                        { PartitionBy = [ "dept" ]
                          OrderBy = [ "name", Asc ]
                          Fn = Rank
                          Of = "salary"
                          As = "rk" } ]

              let canonical = DataFrameCodec.encodePipeline pipeline

              Expect.stringContains
                  canonical
                  "\"orderBy\":[{\"column\":\"name\",\"dir\":\"asc\"}]"
                  "the frame ordering names `column`"

              match
                  DataFrameCodec.decodePipeline
                      """[{"$type":"window","partitionBy":["dept"],"orderBy":[{"col":"name","dir":"asc"}],"fn":"rank","of":"salary","as":"rk"}]"""
              with
              | Ok viaAlias ->
                  Expect.equal viaAlias pipeline "the pre-rename `col` spelling decodes to the same tree"
                  Expect.equal (DataFrameCodec.encodePipeline viaAlias) canonical "and re-encodes as `column`"
              | other -> failtestf "expected the `col` spelling to decode, got %A" other

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
              | Ok [ Sort [ (Slot.Lit "revenue", Desc); (Slot.Lit "name", Asc) ] ] -> ()
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
              // a generator of (table, pipeline) samples over a fixed schema. Phase 223 — `strata`
              // says how many pipeline shapes are drawn from: the first five are well-formed, and the
              // sixth filters on a column the table does not carry, so the reference REFUSES it in
              // every table. `gen` draws all six, so the Error/Error parity arm the family now guards
              // is a stratum of the generator; `safeGen` is the old five-shape draw, kept as the
              // refusal-free generator the must-fail case below runs.
              let genOver (strata: int) (rng: ConfRng.T) =
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

                  let stepKind, r2 = pick strata r1

                  let pipeline =
                      match stepKind with
                      | 0 -> [ Filter(Binary(Gt, Col "v", Lit(Int 0))) ]
                      | 1 -> [ Transform.sortBy [ "v", Asc ]; Distinct ]
                      | 2 -> [ Derive("w", Binary(Add, Col "v", Lit(Int 1))) ]
                      | 3 -> [ GroupBy([ "g" ], [ { Name = "s"; Fn = Sum; Of = "v" } ]) ]
                      | 4 -> [ Transform.limit 2 0 ]
                      | _ -> [ Filter(Binary(Gt, Col "nope", Lit(Int 0))) ]

                  (table, pipeline), r2

              let gen = genOver 6
              let safeGen = genOver 5

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

              // Phase 223 — the must-fail case: over only well-formed pipelines every subject law
              // passes, and the refused-pipeline guard is the one line that says the Error/Error arm
              // was never compared.
              Expect.equal
                  (Conformance.transformLaws DataFrame.evalPipeline safeGen 7 200
                   |> List.filter (fun r -> not r.Passed)
                   |> List.map (fun r -> r.Law))
                  [ SampleAdequacy.lawPrefix "Conformance.transformLaws"
                    + "the sample reached every refused pipeline the laws distinguish" ]
                  "a refusal-free generator turns transformLaws red on exactly the refused-pipeline guard"

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
              Expect.equal (List.length results) 5 "four param laws and the decimal guard reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "paramLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.paramLaws 7714 200) results "same seed ⇒ identical report"

          // ---- Phase 112 — the static output-schema walk ----
          // The walk mirrors the evaluator's schema semantics without evaluating anything, so every
          // case below states the derived answer AND checks it against the schema the reference
          // evaluator actually produced. `Conformance.schemaWalkLaws` does the same over generated
          // pipelines; these pin the individual verbs and the two verdicts by hand.

          testCase "Phase 112 — an empty pipeline is the input schema, closed and fully typed"
          <| fun _ ->
              let k = SchemaWalk.ofPipeline people.Schema []
              Expect.isTrue (SchemaWalk.isClosed k) "nothing happened, so nothing was lost"
              Expect.equal (SchemaWalk.names k) [ "dept"; "name"; "salary"; "bonus" ] "the input's own columns"
              Expect.equal (SchemaWalk.typeOf "salary" k) (Some IntType) "declared types carry through"
              Expect.equal (SchemaWalk.reason k) None "a closed set has nothing to explain"

          testCase "Phase 112 — the row-set verbs and the set ops leave the schema alone"
          <| fun _ ->
              let unchanged =
                  [ Filter(Binary(Gt, Col "salary", Lit(Int 95)))
                    Transform.sortBy [ "salary", Asc ]
                    Distinct
                    Transform.limit 2 1
                    Union(Embedded people)
                    Intersect(Embedded people)
                    Except(Ref "anything") ]

              for step in unchanged do
                  let k = SchemaWalk.ofPipeline people.Schema [ step ]
                  Expect.isTrue (SchemaWalk.isClosed k) (sprintf "%A drops rows, not columns" step)

                  Expect.equal
                      (SchemaWalk.names k)
                      [ "dept"; "name"; "salary"; "bonus" ]
                      (sprintf "%A leaves the column set alone" step)

          testCase "Phase 112 — Derive names the column, and (Phase 338) states the type its expression decides"
          <| fun _ ->
              let added =
                  SchemaWalk.ofPipeline people.Schema [ Derive("raise", Binary(Add, Col "salary", Lit(Int 10))) ]

              Expect.equal
                  (SchemaWalk.names added)
                  [ "dept"; "name"; "salary"; "bonus"; "raise" ]
                  "the derived name is appended"

              Expect.equal
                  (SchemaWalk.typeOf "raise" added)
                  (Some IntType)
                  "the typer decides an int add, so the walk states it"

              Expect.equal
                  (SchemaWalk.typeOf "p" (SchemaWalk.ofPipeline people.Schema [ Derive("p", Param "k") ]))
                  None
                  "a param's cell is the arguments', so the walk cannot state it"

              Expect.isTrue (SchemaWalk.has "raise" added) "the column is KNOWN to exist, and so is its type"

              // Onto an existing name the evaluator retypes IN PLACE, position kept.
              let inPlace = SchemaWalk.ofPipeline people.Schema [ Derive("salary", Lit(Int 1)) ]

              Expect.equal
                  (SchemaWalk.names inPlace)
                  [ "dept"; "name"; "salary"; "bonus" ]
                  "an existing name is retyped, not appended"

              Expect.equal (SchemaWalk.typeOf "salary" inPlace) (Some IntType) "and retyped to the literal's type"

              let evaluated =
                  run [ Derive("raise", Binary(Add, Col "salary", Lit(Int 10))) ] |> okTable

              Expect.equal
                  (evaluated.Schema |> List.map fst)
                  (SchemaWalk.names added)
                  "the evaluator agrees on the names"

          testCase "Phase 112 — Project and GroupBy CLOSE the set; the aggregate types are derived"
          <| fun _ ->
              let grouped =
                  [ GroupBy(
                        [ "dept" ],
                        [ { Name = "tot"
                            Fn = Sum
                            Of = "salary" }
                          { Name = "n"; Fn = Count; Of = "name" }
                          { Name = "avg"
                            Fn = Mean
                            Of = "bonus" } ]
                    ) ]

              let k = SchemaWalk.ofPipeline people.Schema grouped
              Expect.isTrue (SchemaWalk.isClosed k) "a group-by output is exactly keys + aggregates"
              Expect.equal (SchemaWalk.names k) [ "dept"; "tot"; "n"; "avg" ] "keys then aggregates, in order"
              Expect.equal (SchemaWalk.typeOf "dept" k) (Some StringType) "a key keeps its type"
              Expect.equal (SchemaWalk.typeOf "tot" k) (Some IntType) "Sum keeps the source type"
              Expect.equal (SchemaWalk.typeOf "n" k) (Some IntType) "Count is Int over any source"
              Expect.equal (SchemaWalk.typeOf "avg" k) (Some FloatType) "Mean is Float over any source"
              Expect.isFalse (SchemaWalk.has "salary" k) "and a closed set makes that absence a FACT"

              Expect.equal
                  ((run grouped |> okTable).Schema)
                  [ "dept", StringType; "tot", IntType; "n", IntType; "avg", FloatType ]
                  "the evaluator produces exactly that schema"

              let projected =
                  SchemaWalk.ofPipeline people.Schema [ Project [ "name", "who"; "salary", "pay" ] ]

              Expect.equal (SchemaWalk.names projected) [ "who"; "pay" ] "renamed, in the listed order"
              Expect.equal (SchemaWalk.typeOf "pay" projected) (Some IntType) "under the source column's type"

          testCase "Phase 112 — Window APPENDS, even when its As collides: the name lands twice"
          <| fun _ ->
              let collide =
                  Window
                      { PartitionBy = [ "dept" ]
                        OrderBy = [ "salary", Asc ]
                        Fn = RowNumber
                        Of = "salary"
                        As = "salary" }

              let k = SchemaWalk.ofPipeline people.Schema [ collide ]

              Expect.equal
                  (SchemaWalk.names k)
                  [ "dept"; "name"; "salary"; "bonus"; "salary" ]
                  "the evaluator appends unconditionally, so the walk must too"

              Expect.equal
                  ((run [ collide ] |> okTable).Schema |> List.map fst)
                  (SchemaWalk.names k)
                  "and the evaluated schema really does carry it twice"

              // The three window type families, each pinned to the evaluator's own rule.
              let windowed fn ofCol =
                  SchemaWalk.ofPipeline
                      people.Schema
                      [ Window
                            { PartitionBy = []
                              OrderBy = [ "salary", Asc ]
                              Fn = fn
                              Of = ofCol
                              As = "w" } ]
                  |> SchemaWalk.typeOf "w"

              Expect.equal (windowed CompetitionRank "salary") (Some IntType) "the ranking family is Int"
              Expect.equal (windowed RollingSum "salary") (Some FloatType) "the accumulating family is Float"
              Expect.equal (windowed CumulMax "bonus") (Some FloatType) "the running extremes keep the source type"
              Expect.equal (windowed Lag "dept") (Some StringType) "and so does the shifting pair"

          testCase "Phase 112 — Unpivot closes to idVars + (variable, value)"
          <| fun _ ->
              let melt = [ Unpivot([ "dept"; "name" ], [ "salary" ]) ]
              let k = SchemaWalk.ofPipeline people.Schema melt
              Expect.isTrue (SchemaWalk.isClosed k) "the melted shape is fully determined"
              Expect.equal (SchemaWalk.names k) [ "dept"; "name"; "variable"; "value" ] "idVars then the melted pair"
              Expect.equal (SchemaWalk.typeOf "variable" k) (Some StringType) "the variable column is the column NAME"
              Expect.equal (SchemaWalk.typeOf "value" k) (Some IntType) "the value column takes the first value var"

              Expect.equal
                  ((run melt |> okTable).Schema)
                  [ "dept", StringType
                    "name", StringType
                    "variable", StringType
                    "value", IntType ]
                  "the evaluator agrees, types included"

          testCase "Phase 112 — Pivot OPENS the set, and only Closed supports a negative verdict"
          <| fun _ ->
              let pivot =
                  Pivot
                      { Index = [ "dept" ]
                        On = "name"
                        Values = "salary"
                        Agg = Sum }

              let k = SchemaWalk.ofPipeline people.Schema [ pivot ]
              Expect.isFalse (SchemaWalk.isClosed k) "the value columns are named by the data"
              Expect.equal (SchemaWalk.names k) [ "dept" ] "the index columns, and only those"

              Expect.stringContains
                  (SchemaWalk.reason k |> Option.defaultValue "")
                  "named by the data"
                  "the reason says what cost the walk its certainty"

              let evaluated = (run [ pivot ] |> okTable).Schema |> List.map fst

              Expect.stringContains
                  (String.concat "," evaluated)
                  "ana"
                  "the evaluated output really does carry a per-value column"

              Expect.isFalse
                  (SchemaWalk.has "ana" k)
                  "the walk cannot SEE it — which on an open set means 'not visible', never 'absent'"

              // A Project after an open step closes it again: the output is exactly what is listed.
              let closedAgain =
                  SchemaWalk.ofPipeline people.Schema [ pivot; Project [ "dept", "d" ] ]

              Expect.isTrue (SchemaWalk.isClosed closedAgain) "Project closes however open its input was"
              Expect.equal (SchemaWalk.names closedAgain) [ "d" ] "exactly the listed columns"

          testCase "Phase 112 — a Ref right-hand source: declared keeps it closed, undeclared opens it"
          <| fun _ ->
              let hr: Schema = [ "name", StringType; "grade", IntType ]
              let join = Join(Ref "hr", [ "name", "name" ], Inner)

              let declared =
                  SchemaWalk.ofPipelineFrom
                      (SchemaWalk.ofMap (Map.ofList [ "hr", hr ]))
                      (SchemaWalk.ofSchema people.Schema)
                      [ join ]

              Expect.isTrue (SchemaWalk.isClosed declared) "a declared source schema keeps the walk certain"

              Expect.equal
                  (SchemaWalk.names declared)
                  [ "dept"; "name"; "salary"; "bonus"; "name_right"; "grade" ]
                  "left columns, then the right's — a collision taking the evaluator's _right suffix"

              Expect.equal (SchemaWalk.typeOf "grade" declared) (Some IntType) "right column types carry through"

              let undeclared = SchemaWalk.ofPipeline people.Schema [ join ]
              Expect.isFalse (SchemaWalk.isClosed undeclared) "an undeclared Ref is honestly unknown"

              Expect.equal
                  (SchemaWalk.names undeclared)
                  [ "dept"; "name"; "salary"; "bonus" ]
                  "the left is still known — the walk lost the right, not everything"

              Expect.stringContains
                  (SchemaWalk.reason undeclared |> Option.defaultValue "")
                  "no declared schema"
                  "and it names the source it could not resolve"

          testCase "Phase 112 — a filtering join keeps the left schema and reaches no right source"
          <| fun _ ->
              for how in [ Semi; Anti ] do
                  let k =
                      SchemaWalk.ofPipeline people.Schema [ Join(Ref "never-declared", [ "name", "k" ], how) ]

                  Expect.isTrue
                      (SchemaWalk.isClosed k)
                      (sprintf "%A contributes no right columns, so an unknown right costs nothing" how)

                  Expect.equal
                      (SchemaWalk.names k)
                      [ "dept"; "name"; "salary"; "bonus" ]
                      (sprintf "%A is the left schema, unchanged" how)

          testCase "Phase 112 — an OPEN left contributes no right columns at all"
          <| fun _ ->
              let right =
                  tbl [ "k", StringType; "v", IntType ] [ col "k" StringType [ Str "eng" ]; col "v" IntType [ Int 1 ] ]

              let k =
                  SchemaWalk.ofPipeline
                      people.Schema
                      [ Pivot
                            { Index = [ "dept" ]
                              On = "name"
                              Values = "salary"
                              Agg = Sum }
                        Join(Embedded right, [ "dept", "k" ], Inner) ]

              Expect.equal
                  (SchemaWalk.names k)
                  [ "dept" ]
                  "while a left name is invisible, every right name is undecidable between x and x_right"

              Expect.stringContains
                  (SchemaWalk.reason k |> Option.defaultValue "")
                  "right-hand output names depend on the left"
                  "and the reason says so rather than silently dropping them"

          testCase "schemaWalkLaws certify the walk against the evaluator's schema (Phase 112)"
          <| fun _ ->
              let results = Conformance.schemaWalkLaws 1121 300
              // Phase 321: and the decimal step's adequacy guard.
              Expect.equal (List.length results) 5 "closed-exact, open-sound, types, non-vacuity, decimal"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "schemaWalkLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.schemaWalkLaws 1121 300) results "same seed ⇒ identical report"

          // ---- Phase 101 — the closed-set asymmetries ----

          testCase "Phase 101 — Intersect/Except are multiset ops on the full row; null matches null"
          <| fun _ ->
              let left =
                  tbl
                      [ "k", IntType; "v", StringType ]
                      [ col "k" IntType [ Int 1; Int 2; Int 2; Int 3; Null ]
                        col "v" StringType [ Str "a"; Str "b"; Str "b"; Str "c"; Str "z" ] ]

              let right =
                  tbl
                      [ "k", IntType; "v", StringType ]
                      [ col "k" IntType [ Int 2; Int 3; Null ]
                        col "v" StringType [ Str "b"; Str "x"; Str "z" ] ]

              let inter = DataFrame.evalPipeline [ Intersect(Embedded right) ] left |> okTable

              Expect.equal (cellsOf "k" inter) [ Int 2; Int 2; Null ] "duplicates survive; Null matches Null"
              Expect.equal (cellsOf "v" inter) [ Str "b"; Str "b"; Str "z" ] "the whole row is the key"

              let exc = DataFrame.evalPipeline [ Except(Embedded right) ] left |> okTable
              Expect.equal (cellsOf "k" exc) [ Int 1; Int 3 ] "rows in A not in B, left order preserved"

              // `· Distinct` recovers the SQL set forms, exactly as it does for Union.
              let interSet =
                  DataFrame.evalPipeline [ Intersect(Embedded right); Distinct ] left |> okTable

              Expect.equal (cellsOf "k" interSet) [ Int 2; Null ] "Intersect · Distinct = SQL INTERSECT"

              // An `Int 1` is not a `Float 1.0` — the same type-tagged token Distinct dedups on.
              let ints = tbl [ "k", IntType ] [ col "k" IntType [ Int 1 ] ]
              let floats = tbl [ "k", FloatType ] [ col "k" FloatType [ Float 1.0 ] ]

              Expect.equal
                  (DataFrame.evalPipeline [ Intersect(Embedded floats) ] ints
                   |> okTable
                   |> cellsOf "k")
                  []
                  "Int 1 and Float 1.0 are two values"

              match DataFrame.evalPipeline [ Except(Embedded people) ] left with
              | Error(JoinError m) -> Expect.stringContains m "except" "the verb names itself"
              | other -> failtestf "expected a JoinError on mismatched columns, got %A" other

          testCase "Phase 101 — Semi/Anti keep the left schema, once per row; Semi is NOT Left+filter"
          <| fun _ ->
              // `eng` appears twice on the right, so a `Left` join fans the eng rows out.
              let depts =
                  tbl [ "dept", StringType ] [ col "dept" StringType [ Str "eng"; Str "eng"; Str "hr" ] ]

              let semi = run [ Join(Embedded depts, [ "dept", "dept" ], Semi) ] |> okTable
              Expect.equal (List.map fst semi.Schema) [ "dept"; "name"; "salary"; "bonus" ] "left schema only"
              Expect.equal (cellsOf "name" semi) [ Str "ana"; Str "bob"; Str "el" ] "each matching left row ONCE"

              let anti = run [ Join(Embedded depts, [ "dept", "dept" ], Anti) ] |> okTable
              Expect.equal (cellsOf "name" anti) [ Str "cy"; Str "dee" ] "the unmatched left rows"

              // Anti IS expressible the long way — Left + IsNull on the (suffixed) right key.
              let antiIdiom =
                  run
                      [ Join(Embedded depts, [ "dept", "dept" ], Left)
                        Filter(IsNull(Col "dept_right")) ]
                  |> okTable

              Expect.equal (cellsOf "name" antiIdiom) (cellsOf "name" anti) "Anti agrees with the Left+IsNull idiom"

              // Semi is NOT: the fan-out is already baked in and no filter undoes it.
              let semiIdiom =
                  run
                      [ Join(Embedded depts, [ "dept", "dept" ], Left)
                        Filter(Not(IsNull(Col "dept_right"))) ]
                  |> okTable

              Expect.equal (List.length (cellsOf "name" semiIdiom)) 6 "Left+filter duplicates per right match"
              Expect.notEqual (cellsOf "name" semiIdiom) (cellsOf "name" semi) "…so it is not a spelling of Semi"

          testCase "Phase 101 — CountDistinct counts distinct present values, host-deterministically"
          <| fun _ ->
              let t =
                  run
                      [ GroupBy(
                            [ "dept" ],
                            [ { Name = "n"
                                Fn = Count
                                Of = "salary" }
                              { Name = "d"
                                Fn = CountDistinct
                                Of = "salary" } ]
                        ) ]
                  |> okTable

              Expect.equal (cellsOf "n" t) [ Int 2; Int 2 ] "Count is unchanged"
              Expect.equal (cellsOf "d" t) [ Int 2; Int 1 ] "sales has 90 twice"

              let distinctOver (table: Table) (column: string) =
                  DataFrame.evalPipeline
                      [ GroupBy(
                            [],
                            [ { Name = "d"
                                Fn = CountDistinct
                                Of = column } ]
                        ) ]
                      table
                  |> okTable
                  |> cellsOf "d"

              // nulls are skipped, exactly as Count skips them
              let allNull = tbl [ "x", IntType ] [ col "x" IntType [ Null; Null ] ]
              Expect.equal (distinctOver allNull "x") [ Int 0 ] "all-null counts 0"

              // NaN collapses to one value and -0.0/0.0 coincide — the canonical token, not host equality
              let nan = System.Double.NaN

              let odd =
                  tbl [ "f", FloatType ] [ col "f" FloatType [ Float nan; Float nan; Float -0.0; Float 0.0 ] ]

              Expect.equal (distinctOver odd "f") [ Int 2 ] "NaN is one value; -0.0 and 0.0 are one value"

          testCase "Phase 101 — DenseRank equals Rank; CompetitionRank is the gapped SQL RANK()"
          <| fun _ ->
              let scores =
                  tbl [ "s", IntType ] [ col "s" IntType [ Int 10; Int 10; Int 20; Int 30 ] ]

              let rankWith fn =
                  DataFrame.evalPipeline
                      [ Window
                            { PartitionBy = []
                              OrderBy = [ "s", Asc ]
                              Fn = fn
                              Of = "s"
                              As = "r" } ]
                      scores
                  |> okTable
                  |> cellsOf "r"

              Expect.equal (rankWith Rank) [ Int 1; Int 1; Int 2; Int 3 ] "the legacy Rank is DENSE"
              Expect.equal (rankWith DenseRank) (rankWith Rank) "DenseRank is the explicit spelling"

              Expect.equal
                  (rankWith CompetitionRank)
                  [ Int 1; Int 1; Int 3; Int 4 ]
                  "CompetitionRank skips by the tied block's size"

          testCase "Phase 101 — NTile distributes evenly, big buckets first; n < 1 is a named error"
          <| fun _ ->
              let five =
                  tbl [ "s", IntType ] [ col "s" IntType [ Int 1; Int 2; Int 3; Int 4; Int 5 ] ]

              let ntileSpec n =
                  Window
                      { PartitionBy = []
                        OrderBy = [ "s", Asc ]
                        Fn = NTile n
                        Of = "s"
                        As = "b" }

              let ntile n =
                  DataFrame.evalPipeline [ ntileSpec n ] five |> okTable |> cellsOf "b"

              Expect.equal (ntile 2) [ Int 1; Int 1; Int 1; Int 2; Int 2 ] "5 rows / 2 buckets = 3 then 2"
              Expect.equal (ntile 5) [ Int 1; Int 2; Int 3; Int 4; Int 5 ] "one row each"
              Expect.equal (ntile 7) [ Int 1; Int 2; Int 3; Int 4; Int 5 ] "more buckets than rows leaves a tail empty"

              match DataFrame.evalPipeline [ ntileSpec 0 ] five with
              | Error(TypeError m) -> Expect.stringContains m "ntile" "the error names the fn"
              | other -> failtestf "expected a TypeError for 0 buckets, got %A" other

          testCase "Phase 101 — CumulMax/CumulMin carry nulls forward; RollingSum shares the window"
          <| fun _ ->
              let v =
                  tbl [ "v", IntType ] [ col "v" IntType [ Null; Int 3; Int 1; Int 5; Null; Int 2 ] ]

              let windowed fn =
                  DataFrame.evalPipeline
                      [ Window
                            { PartitionBy = []
                              OrderBy = []
                              Fn = fn
                              Of = "v"
                              As = "w" } ]
                      v
                  |> okTable

              let maxT = windowed CumulMax
              Expect.equal (cellsOf "w" maxT) [ Null; Int 3; Int 3; Int 5; Int 5; Int 5 ] "running max, nulls carried"

              Expect.equal
                  (maxT.Schema |> List.tryFind (fun (n, _) -> n = "w"))
                  (Some("w", IntType))
                  "the running extreme keeps the source type"

              Expect.equal (windowed CumulMin |> cellsOf "w") [ Null; Int 3; Int 1; Int 1; Int 1; Int 1 ] "running min"

              Expect.equal
                  (windowed RollingSum |> cellsOf "w")
                  [ Null; Float 3.0; Float 4.0; Float 9.0; Float 6.0; Float 7.0 ]
                  "trailing 3-window total over present values"

              Expect.equal
                  (windowed RollingMean |> cellsOf "w" |> List.item 3)
                  (Float 3.0)
                  "RollingMean is unchanged — the same window"

          testCase "Phase 101 — Sqrt is Null below zero; Least/Greatest propagate null; IndexOf is 0-based"
          <| fun _ ->
              let one = tbl [ "x", IntType ] [ col "x" IntType [ Int 0 ] ]

              let scalar e =
                  DataFrame.evalPipeline [ Derive("r", e) ] one |> okTable |> cellsOf "r"

              Expect.equal (scalar (ApplyFn(Sqrt, [ Lit(Float 9.0) ]))) [ Float 3.0 ] "sqrt 9 = 3"
              Expect.equal (scalar (ApplyFn(Sqrt, [ Lit(Int 2) ]))) [ Float(sqrt 2.0) ] "int promotes to float"
              Expect.equal (scalar (ApplyFn(Sqrt, [ Lit(Int -4) ]))) [ Null ] "a negative root is Null, never NaN"
              Expect.equal (scalar (ApplyFn(Sqrt, [ Lit Null ]))) [ Null ] "null propagates"

              match DataFrame.evalPipeline [ Derive("r", ApplyFn(Sqrt, [ Lit(Str "x") ])) ] one with
              | Error(TypeError _) -> ()
              | other -> failtestf "expected a TypeError for sqrt of a string, got %A" other

              let clamped fn =
                  run [ Derive("r", ApplyFn(fn, [ Col "salary"; Lit(Int 95) ])) ]
                  |> okTable
                  |> cellsOf "r"

              Expect.equal (clamped Greatest) [ Int 100; Int 120; Int 95; Int 95; Null ] "greatest is a floor"
              Expect.equal (clamped Least) [ Int 95; Int 95; Int 90; Int 90; Null ] "least is a ceiling"

              Expect.equal (scalar (ApplyFn(Greatest, [ Lit(Int 1); Lit(Int 9); Lit(Int 4) ]))) [ Int 9 ] "variadic"

              Expect.equal
                  (run [ Derive("r", ApplyFn(IndexOf, [ Col "name"; Lit(Str "e") ])) ]
                   |> okTable
                   |> cellsOf "r")
                  [ Int -1; Int -1; Int -1; Int 1; Int 0 ]
                  "0-based, -1 when absent"

              Expect.equal (scalar (ApplyFn(IndexOf, [ Lit(Str "abc"); Lit(Str "") ]))) [ Int 0 ] "an empty needle is 0"

              // The composition the 0-based pin exists for.
              Expect.equal
                  (scalar (
                      ApplyFn(
                          Substr,
                          [ Lit(Str "key=value")
                            ApplyFn(IndexOf, [ Lit(Str "key=value"); Lit(Str "=") ])
                            Lit(Int 1) ]
                      )
                  ))
                  [ Str "=" ]
                  "IndexOf feeds Substr directly"

          testCase "Phase 101 — every new wire form round-trips; existing wire is byte-unchanged"
          <| fun _ ->
              let p =
                  [ Intersect(Embedded people)
                    Except(Embedded people)
                    Join(Embedded people, [ "dept", "dept" ], Semi)
                    Join(Embedded people, [ "dept", "dept" ], Anti)
                    GroupBy(
                        [ "dept" ],
                        [ { Name = "d"
                            Fn = CountDistinct
                            Of = "salary" } ]
                    )
                    Window
                        { PartitionBy = []
                          OrderBy = [ "salary", Asc ]
                          Fn = DenseRank
                          Of = "salary"
                          As = "a" }
                    Window
                        { PartitionBy = []
                          OrderBy = [ "salary", Asc ]
                          Fn = CompetitionRank
                          Of = "salary"
                          As = "b" }
                    Window
                        { PartitionBy = []
                          OrderBy = [ "salary", Asc ]
                          Fn = NTile 4
                          Of = "salary"
                          As = "c" }
                    Window
                        { PartitionBy = []
                          OrderBy = []
                          Fn = CumulMax
                          Of = "salary"
                          As = "d" }
                    Window
                        { PartitionBy = []
                          OrderBy = []
                          Fn = CumulMin
                          Of = "salary"
                          As = "e" }
                    Window
                        { PartitionBy = []
                          OrderBy = []
                          Fn = RollingSum
                          Of = "salary"
                          As = "f" }
                    Derive("g", ApplyFn(Sqrt, [ Col "bonus" ]))
                    Derive("h", ApplyFn(Least, [ Col "salary"; Lit(Int 1) ]))
                    Derive("i", ApplyFn(Greatest, [ Col "salary"; Lit(Int 1) ]))
                    Derive("j", ApplyFn(IndexOf, [ Col "name"; Lit(Str "e") ])) ]

              let bytes = DataFrameCodec.encodePipeline p

              match DataFrameCodec.decodePipeline bytes with
              | Error e -> failtestf "round-trip decode failed: %s" (ColumnCodec.errorString e)
              | Ok p2 ->
                  Expect.equal p2 p "tree-identical"
                  Expect.equal (DataFrameCodec.encodePipeline p2) bytes "byte-identical"

              // ADDITIVE PROOF — a non-NTile window step carries no "n" key, so its wire is exactly
              // what the pre-Phase-101 encoder produced, save for the `0.28.0` frame-ordering member
              // rename (`col` → `column`, D48) that is not this test's subject.
              Expect.equal
                  (DataFrameCodec.encodePipeline
                      [ Window
                            { PartitionBy = [ "dept" ]
                              OrderBy = [ "salary", Desc ]
                              Fn = Rank
                              Of = "salary"
                              As = "rk" } ])
                  "[{\"$type\":\"window\",\"as\":\"rk\",\"fn\":\"rank\",\"of\":\"salary\",\"orderBy\":[{\"column\":\"salary\",\"dir\":\"desc\"}],\"partitionBy\":[\"dept\"]}]"
                  "a non-ntile window step still carries no \"n\" key"

              Expect.stringContains
                  (DataFrameCodec.encodePipeline
                      [ Window
                            { PartitionBy = []
                              OrderBy = []
                              Fn = NTile 4
                              Of = "salary"
                              As = "b" } ])
                  "\"n\":4"
                  "…and the bucket count appears only for ntile"

          testCase "Phase 101 — the decode rosters enumerate the new vocabulary; ntile needs its n"
          <| fun _ ->
              let roster json =
                  match DataFrameCodec.decodePipeline json with
                  | Error(UnknownType(_, expected)) -> expected
                  | other -> failtestf "expected an UnknownType, got %A" other

              let steps = roster """[{"$type":"frobnicate"}]"""
              Expect.contains steps "intersect" "the step roster gained intersect"
              Expect.contains steps "except" "the step roster gained except"

              let joins =
                  roster """[{"$type":"join","source":{"schema":[],"columns":[]},"on":[],"how":"frob"}]"""

              Expect.contains joins "semi" "the join roster gained semi"
              Expect.contains joins "anti" "the join roster gained anti"

              let windows =
                  roster """[{"$type":"window","partitionBy":[],"orderBy":[],"fn":"frob","of":"x","as":"y"}]"""

              Expect.contains windows "ntile" "the window roster gained ntile"
              Expect.contains windows "denseRank" "the window roster gained denseRank"
              Expect.contains windows "competitionRank" "the window roster gained competitionRank"
              Expect.contains windows "rollingSum" "the window roster gained rollingSum"

              let aggs =
                  roster """[{"$type":"groupBy","keys":[],"aggs":[{"name":"a","fn":"frob","of":"x"}]}]"""

              Expect.contains aggs "countDistinct" "the aggregate roster gained countDistinct"

              let scalars =
                  roster """[{"$type":"filter","pred":{"$type":"apply","fn":"frob","args":[]}}]"""

              Expect.contains scalars "sqrt" "the scalar roster gained sqrt"
              Expect.contains scalars "indexOf" "the scalar roster gained indexOf"

              match
                  DataFrameCodec.decodePipeline
                      """[{"$type":"window","partitionBy":[],"orderBy":[],"fn":"ntile","of":"x","as":"y"}]"""
              with
              | Error(MissingField "n") -> ()
              | other -> failtestf "expected MissingField n, got %A" other

          testCase "Phase 101 — Except is a full-row step, so incremental reuse must not fire"
          <| fun _ ->
              let mk (c: Cell list) =
                  tbl
                      [ "a", IntType; "b", IntType; "c", IntType ]
                      [ col "a" IntType [ Int 1; Int 2 ]
                        col "b" IntType [ Int 0; Int 0 ]
                        col "c" IntType c ]

              let blocked =
                  tbl
                      [ "a", IntType; "b", IntType; "c", IntType ]
                      [ col "a" IntType [ Int 1 ]
                        col "b" IntType [ Int 0 ]
                        col "c" IntType [ Int 9 ] ]

              let pipeline = [ Except(Embedded blocked); Project [ "a", "a" ] ]
              let oldSrc = mk [ Int 9; Int 9 ]
              let newSrc = mk [ Int 7; Int 9 ]

              let prior = DataFrame.evalPipeline pipeline oldSrc |> okTable
              Expect.equal (cellsOf "a" prior) [ Int 2 ] "row 1 is excepted while c = 9"

              // `c` is neither read by the pipeline nor present in the prior output — the exact shape
              // the reuse short-circuit fires on. It must NOT fire here.
              let viaIncr =
                  DataFrame.evalFrom prior (ColumnValuesChanged "c") pipeline newSrc |> okTable

              let viaFull = DataFrame.evalPipeline pipeline newSrc |> okTable
              Expect.equal (cellsOf "a" viaFull) [ Int 1; Int 2 ] "changing c un-blocks row 1"
              Expect.equal (cellsOf "a" viaIncr) (cellsOf "a" viaFull) "incremental agrees with full"
              Expect.notEqual (cellsOf "a" viaIncr) (cellsOf "a" prior) "the prior result was NOT reused" ]

// ---- Phase 323 — the streamed GroupBy aggregates, held to `Column.aggregate` ----

/// Two cells equal to the bit: a float by its IEEE bits (so `-0.0` is not `0.0`, and a sum that
/// differs in its last bit is a different answer), every other cell structurally.
let private sameCell (a: Cell) (b: Cell) : bool =
    match a, b with
    | Float x, Float y -> System.BitConverter.DoubleToInt64Bits x = System.BitConverter.DoubleToInt64Bits y
    | _ -> a = b

/// One drawn case of the differential law: a column type, its cells, and a group per row.
type private AggCase =
    { Ty: ColumnType
      Cells: Cell[]
      Groups: int
      GroupOf: int[] }

let private allAggFns =
    [ Count; Sum; Mean; Min; Max; First; Last; Median; StdDev; CountDistinct ]

/// The cell pool of one column type, at one of three edges: ordinary values, values at the edge
/// of the range (int32's ends, floats near the top of the double range, a decimal past it), and
/// cells OUTSIDE the column's type (which `Column.aggregate` refuses by name).
let private drawCell (rng: System.Random) (ty: ColumnType) (edge: int) : Cell =
    let pick (xs: Cell[]) = xs[rng.Next xs.Length]

    match ty with
    | IntType ->
        match edge with
        | 1 ->
            pick
                [| Null
                   Int System.Int32.MaxValue
                   Int(System.Int32.MaxValue - 1)
                   Int System.Int32.MinValue
                   Int 1 |]
        | 2 -> pick [| Null; Int 3; Int -2; Float 1.5 |]
        | _ -> pick [| Null; Int 0; Int 1; Int -1; Int 7; Int 7; Int 42 |]
    | FloatType ->
        match edge with
        | 1 ->
            pick
                [| Null
                   Float 1.6e308
                   Float 1.7e308
                   Float -1.5e308
                   Float 1.0e308
                   Float 3.0 |]
        | 2 -> pick [| Null; Float nan; Float infinity; Float -infinity; Float 2.5; Int 2 |]
        | _ ->
            pick
                [| Null
                   Float 0.0
                   Float -0.0
                   Float 0.1
                   Float 0.2
                   Float 0.3
                   Float 1.0
                   Int 1
                   Float nan
                   Float -3.75
                   Float 1e-300 |]
    | DecimalType ->
        match edge with
        | 1 -> pick [| Null; Decimal("1" + System.String('0', 400)); Decimal "2.5"; Int 3 |]
        | 2 -> pick [| Null; Decimal "1.5"; Decimal "not a number"; Float 1.0 |]
        | _ ->
            pick
                [| Null
                   Decimal "1.50"
                   Decimal "1.5"
                   Decimal "-2"
                   Decimal "0.1"
                   Int 4
                   Decimal "0.10" |]
    | BoolType ->
        if edge = 2 then
            pick [| Null; Bool true; Int 1 |]
        else
            pick [| Null; Bool true; Bool false |]
    | DateType -> pick [| Null; Date "2026-01-02"; Date "2025-12-31"; Date "2026-01-02" |]
    | TimestampType -> pick [| Null; Timestamp "2026-01-02T03:04:05Z"; Timestamp "2026-01-02T03:04:04Z" |]
    | StringType ->
        if edge = 2 then
            pick [| Null; Str "b"; Int 3 |]
        else
            pick [| Null; Str "b"; Str "a"; Str ""; Str "B"; Str "a" |]

let private lawTypes =
    [| IntType
       FloatType
       DecimalType
       StringType
       BoolType
       DateType
       TimestampType |]

/// Case `seed`: a column of 0 to 23 cells over 1 to 4 groups, some of which may receive no row.
let private drawCase (seed: int) : AggCase =
    let rng = System.Random(seed)
    let ty = lawTypes[rng.Next lawTypes.Length]

    let edge =
        if rng.Next 4 = 0 then 1
        elif rng.Next 6 = 0 then 2
        else 0

    let n = if rng.Next 8 = 0 then rng.Next 2 else rng.Next 24
    let groups = 1 + rng.Next 4

    { Ty = ty
      Cells = Array.init n (fun _ -> drawCell rng ty edge)
      Groups = groups
      GroupOf = Array.init n (fun _ -> rng.Next groups) }

/// The case's members of group `g`, in row order — what `Column.aggregate` is handed.
let private membersOf (c: AggCase) (g: int) : Cell list =
    [ for i in 0 .. c.Cells.Length - 1 do
          if c.GroupOf[i] = g then
              yield c.Cells[i] ]

/// What one run of the law over `seeds` saw: the counterexamples, how many group-aggregates the
/// stream ANSWERED (as opposed to deferring), how many it deferred where `Column.aggregate` refused,
/// and how many it deferred where `Column.aggregate` answered.
type private LawRun =
    { Failures: string list
      Answered: int
      DeferredRefusals: int
      DeferredAnswers: int }

/// The differential law: every group-aggregate the stream answers is `Column.aggregate` over the
/// same members in the same order, to the bit; every case `Column.aggregate` refuses, the stream
/// defers. Each case is fed twice — over the typed carrier `Vec.pack` chooses, and over the boxed
/// carrier — so the typed loops and the cell-by-cell route are both held.
let private streamLaw (perturbation: DataFrame.GroupAgg.Perturbation) (seeds: int seq) : LawRun =
    let failures = ResizeArray<string>()
    let mutable answered = 0
    let mutable deferredRefusals = 0
    let mutable deferredAnswers = 0

    for seed in seeds do
        let c = drawCase seed
        let phys = Array.init c.Cells.Length id

        for v in [ Vec.pack c.Ty c.Cells; Cells(Array.copy c.Cells) ] do
            for fn in allAggFns do
                let s = DataFrame.GroupAgg.Stream(fn, c.Ty, v, c.Groups, perturbation)
                s.FeedAll(c.GroupOf, phys)

                for g in 0 .. c.Groups - 1 do
                    let expected = Column.aggregate fn (Column.create "" c.Ty (membersOf c g))

                    match s.TryCell g, expected with
                    | ValueSome got, Ok want ->
                        answered <- answered + 1

                        if not (sameCell got want) then
                            failures.Add(
                                sprintf
                                    "seed %d %A over %A, group %d: streamed %A, Column.aggregate %A"
                                    seed
                                    fn
                                    c.Ty
                                    g
                                    got
                                    want
                            )
                    | ValueSome got, Error e ->
                        failures.Add(
                            sprintf
                                "seed %d %A over %A, group %d: streamed %A where Column.aggregate refused %A"
                                seed
                                fn
                                c.Ty
                                g
                                got
                                e
                        )
                    | ValueNone, Error _ -> deferredRefusals <- deferredRefusals + 1
                    | ValueNone, Ok _ -> deferredAnswers <- deferredAnswers + 1

    { Failures = List.ofSeq failures
      Answered = answered
      DeferredRefusals = deferredRefusals
      DeferredAnswers = deferredAnswers }

/// The GroupBy the evaluator computes, against the one the law's oracle computes: groups by token
/// in first-appearance order, then each aggregate as `Column.aggregate` over the group's members
/// (through `DataFrame.aggregateCells`, the evaluator's own envelope), the first error in group
/// order then aggregate order. Over the WHOLE step — the key probe, the streams, the deferrals and
/// the typed output columns — not just the accumulators.
let private groupByOracle (t: Table) (keys: string list) (aggs: Agg list) : Result<(string * Cell list) list, string> =
    let colCells name =
        (Table.tryColumn name t |> Option.get).Cells |> List.toArray

    let tyOf name =
        t.Schema |> List.find (fun (n, _) -> n = name) |> snd

    let keyCols = keys |> List.map colCells
    let n = Table.rowCount t
    let order = ResizeArray<string list>()
    let firstRow = System.Collections.Generic.Dictionary<string list, int>()
    let members = System.Collections.Generic.Dictionary<string list, ResizeArray<int>>()

    for i in 0 .. n - 1 do
        let k = keyCols |> List.map (fun cs -> Cell.token cs[i])

        if not (members.ContainsKey k) then
            order.Add k
            firstRow[k] <- i
            members[k] <- ResizeArray()

        members[k].Add i

    let results =
        [ for k in order ->
              aggs
              |> List.map (fun a ->
                  let src = colCells a.Of
                  DataFrame.aggregateCells a.Fn (tyOf a.Of) [ for i in members[k] -> src[i] ]) ]

    let firstError =
        results
        |> List.tryPick (
            List.tryPick (fun r ->
                match r with
                | Error e -> Some e
                | Ok _ -> None)
        )

    match firstError with
    | Some e -> Error(DataFrame.errorString e)
    | None ->
        Ok(
            [ for kn, cs in List.zip keys keyCols -> kn, [ for k in order -> cs[firstRow[k]] ] ]
            @ [ for j, a in List.indexed aggs ->
                    a.Name,
                    [ for r in results ->
                          match r[j] with
                          | Ok c -> c
                          | Error _ -> Null ] ]
        )

[<Tests>]
let streamedAggregateLaws =
    testList
        "DataFrame — streamed GroupBy aggregates equal Column.aggregate (Phase 323)"
        [ testCase "every streamed group-aggregate is Column.aggregate over the same members, to the bit"
          <| fun _ ->
              let run = streamLaw DataFrame.GroupAgg.Exact (seq { 0..2999 })
              Expect.isEmpty run.Failures "the stream answers what Column.aggregate answers, or defers"
              // Not vacuous: the stream answers most of what it is asked, and the pool reaches the
              // refusals and the recomputations it must defer (an int Sum past int32, a float Sum
              // past the range over finite input, a cell outside the column's type, Phase 306's
              // finite Mean of an overflowed sum).
              Expect.isGreaterThan run.Answered 50_000 "the stream answers, rather than deferring everything"
              Expect.isGreaterThan run.DeferredRefusals 500 "the pool reaches the refusals"
              Expect.isGreaterThan run.DeferredAnswers 1_000 "the pool reaches the deferred answers"

          testCase "the law is red against each perturbed accumulator"
          <| fun _ ->
              for p in
                  [ DataFrame.GroupAgg.MaxKeepsFirstTie
                    DataFrame.GroupAgg.NaNFirst
                    DataFrame.GroupAgg.SumIgnoresOverflow
                    DataFrame.GroupAgg.CountCountsNulls ] do
                  let run = streamLaw p (seq { 0..2999 })
                  Expect.isNonEmpty run.Failures (sprintf "the law finds the perturbation %A" p)

          testCase "a float Sum the stream answers is the left fold from 0.0, and an overflow over finite input defers"
          <| fun _ ->
              let xs = [| Float 0.1; Float 0.2; Float 0.3; Float -0.0 |]

              let s =
                  DataFrame.GroupAgg.Stream(Sum, FloatType, Vec.pack FloatType xs, 1, DataFrame.GroupAgg.Exact)

              s.FeedAll([| 0; 0; 0; 0 |], [| 0; 1; 2; 3 |])

              match s.TryCell 0 with
              | ValueSome(Float f) ->
                  Expect.equal
                      (System.BitConverter.DoubleToInt64Bits f)
                      (System.BitConverter.DoubleToInt64Bits(((0.0 + 0.1) + 0.2) + 0.3 + -0.0))
                      "the left fold, to the bit"
              | other -> failtestf "a float sum expected, got %A" other

              let big = [| Float 1.7e308; Float 1.7e308 |]

              let o =
                  DataFrame.GroupAgg.Stream(Sum, FloatType, Vec.pack FloatType big, 1, DataFrame.GroupAgg.Exact)

              o.FeedAll([| 0; 0 |], [| 0; 1 |])
              Expect.equal (o.TryCell 0) ValueNone "an overflow over finite input is Column.aggregate's to refuse"

              match
                  DataFrame.evalPipeline
                      [ GroupBy([ "k" ], [ { Name = "s"; Fn = Sum; Of = "v" } ]) ]
                      (tbl
                          [ "k", StringType; "v", FloatType ]
                          [ col "k" StringType [ Str "a"; Str "a" ]
                            col "v" FloatType [ Float 1.7e308; Float 1.7e308 ] ])
              with
              | Error(OverflowError _) -> ()
              | other -> failtestf "the GroupBy refuses the overflowed sum by name, got %A" other

          testCase "the whole GroupBy step equals the oracle over drawn tables, keys and aggregates"
          <| fun _ ->
              let failures = ResizeArray<string>()
              let mutable errors = 0

              for seed in 0..1499 do
                  let c = drawCase seed
                  let rng = System.Random(seed + 7_000_000)
                  let n = c.Cells.Length
                  // Keys over three carriers — a string key and an int key (the unboxed probes) and a
                  // float key with a string (the cell-key probe), each with nulls.
                  let sk =
                      [ for i in 0 .. n - 1 ->
                            if rng.Next 7 = 0 then
                                Null
                            else
                                Str(string ("xyz"[c.GroupOf[i] % 3])) ]

                  let ik =
                      [ for i in 0 .. n - 1 -> if rng.Next 7 = 0 then Null else Int(c.GroupOf[i]) ]

                  let fk =
                      [ for i in 0 .. n - 1 ->
                            match rng.Next 5 with
                            | 0 -> Null
                            | 1 -> Float -0.0
                            | 2 -> Float 0.0
                            | 3 -> Float nan
                            | _ -> Float(float c.GroupOf[i]) ]

                  let t =
                      tbl
                          [ "sk", StringType; "ik", IntType; "fk", FloatType; "v", c.Ty ]
                          [ col "sk" StringType sk
                            col "ik" IntType ik
                            col "fk" FloatType fk
                            col "v" c.Ty (List.ofArray c.Cells) ]

                  let aggs =
                      allAggFns
                      |> List.filter (fun _ -> rng.Next 3 > 0)
                      |> List.mapi (fun j fn ->
                          { Name = sprintf "a%d" j
                            Fn = fn
                            Of = "v" })

                  for keys in [ [ "sk" ]; [ "ik" ]; [ "fk"; "sk" ] ] do
                      let expected = groupByOracle t keys aggs

                      let actual =
                          DataFrame.evalPipeline [ GroupBy(keys, aggs) ] t
                          |> Result.mapError DataFrame.errorString
                          |> Result.map (fun r -> r.Columns |> List.map (fun cl -> cl.Name, cl.Cells))

                      match expected, actual with
                      | Ok e, Ok a ->
                          let same =
                              List.length e = List.length a
                              && List.forall2
                                  (fun (n1, c1) (n2, c2) ->
                                      n1 = n2 && List.length c1 = List.length c2 && List.forall2 sameCell c1 c2)
                                  e
                                  a

                          if not same then
                              failures.Add(sprintf "seed %d keys %A: expected %A, got %A" seed keys e a)
                      | Error e, Error a ->
                          errors <- errors + 1

                          if e <> a then
                              failures.Add(sprintf "seed %d keys %A: expected error %s, got %s" seed keys e a)
                      | _ -> failures.Add(sprintf "seed %d keys %A: expected %A, got %A" seed keys expected actual)

              Expect.isEmpty
                  (List.ofSeq failures |> List.truncate 3)
                  (sprintf
                      "the GroupBy step is the oracle's, cell for cell: %s"
                      (String.concat " | " (Seq.truncate 3 failures)))

              Expect.isGreaterThan errors 100 "the drawn tables reach the refusals, and the first one is the oracle's" ]

[<Tests>]
let nowTests =
    testList
        "DataFrame.Now"
        [ testCase "nowLaws certify the pinned clock: determinism, one reading per grain, strict refusal (Phase 125)"
          <| fun _ ->
              let results = Conformance.nowLaws 1250 150
              Expect.equal (List.length results) 5 "five now laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "nowLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.nowLaws 1250 150) results "same seed ⇒ identical report"

          testCase "Now round-trips through the canonical wire on both grains"
          <| fun _ ->
              for grain, tag in [ NowGrain.Date, "date"; NowGrain.Timestamp, "timestamp" ] do
                  let pipeline = [ Derive("t", Now grain) ]
                  let json = DataFrameCodec.encodePipeline pipeline

                  Expect.stringContains json ("{\"$type\":\"now\",\"grain\":\"" + tag + "\"}") "the canonical now form"

                  match DataFrameCodec.decodePipeline json with
                  | Ok back -> Expect.equal back pipeline "decode ∘ encode is the identity on a Now"
                  | Error e -> failtestf "decode failed for grain %s: %A" tag e

          testCase "an unknown grain is refused and ENUMERATES the alternatives (GP5)"
          <| fun _ ->
              match
                  DataFrameCodec.decodePipeline
                      """[{"$type":"derive","name":"t","expr":{"$type":"now","grain":"fortnight"}}]"""
              with
              | Error(UnknownType("fortnight", alts)) -> Expect.equal alts [ "date"; "timestamp" ] "both grains named"
              | other -> failtestf "expected an enumerating UnknownType, got %A" other

          testCase "usesNow reads clock dependence off the pipeline, and a param-only one is not clock-dependent"
          <| fun _ ->
              Expect.isTrue
                  (Transform.usesNow [ Filter(Binary(Gt, Col "d", Now NowGrain.Date)) ])
                  "a Now nested inside an expression is found"

              Expect.isFalse
                  (Transform.usesNow [ Filter(Binary(Gt, Col "d", Param "asOf")) ])
                  "the hand-threaded param idiom is NOT clock-dependent — that is the point of the ask"

          testCase "substituteNow leaves no Now behind, so what evaluates is literals"
          <| fun _ ->
              let clock: ClockWitness =
                  fun g ->
                      match g with
                      | NowGrain.Date -> Date "2026-09-13"
                      | NowGrain.Timestamp -> Timestamp "2026-09-13T00:00:00Z"

              let pinned =
                  Transform.substituteNow clock [ Derive("a", Now NowGrain.Date); Derive("b", Now NowGrain.Timestamp) ]

              Expect.isFalse (Transform.usesNow pinned) "no Now survives the substitution"

              Expect.equal
                  pinned
                  [ Derive("a", Lit(Date "2026-09-13"))
                    Derive("b", Lit(Timestamp "2026-09-13T00:00:00Z")) ]
                  "each Now became the witness's own literal, at its own grain" ]

[<Tests>]
let slotTests =
    testList
        "DataFrame.Slot"
        [ testCase "slotParamLaws certify a slot param resolves exactly as an expression param (Phase 125)"
          <| fun _ ->
              let results = Conformance.slotParamLaws 12500 120
              Expect.equal (List.length results) 6 "six slot laws reported"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "slotParamLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.slotParamLaws 12500 120) results "same seed ⇒ identical report"

          // The adoption claim, checked rather than asserted: the 0.23.0 slot widening costs a
          // consumer that binds nothing NOTHING on the wire — a literal slot still encodes as the
          // bare value, never as an object. `0.28.0` then renamed the key's MEMBER (`col` →
          // `column`, D48), which moves these bytes for a reason that is not the widening; the
          // property the widening promised is the SHAPE of the value, and it is unchanged.
          testCase "a literal-only Sort/Limit slot still encodes as the bare value"
          <| fun _ ->
              Expect.equal
                  (DataFrameCodec.encodePipeline [ Transform.sortBy [ "total", Desc ]; Transform.limit 10 5 ])
                  "[{\"$type\":\"sort\",\"by\":[{\"column\":\"total\",\"dir\":\"desc\"}]},{\"$type\":\"limit\",\"n\":10,\"offset\":5}]"
                  "a literal slot is the bare value it always was"

          testCase "a param slot round-trips through the canonical wire"
          <| fun _ ->
              let pipeline =
                  [ Sort [ Slot.Param "orderBy", Asc ]; Limit(Slot.Param "take", Slot.Lit 0) ]

              let json = DataFrameCodec.encodePipeline pipeline
              Expect.stringContains json "{\"$param\":\"orderBy\"}" "the param slot's own wire shape"
              Expect.stringContains json "\"n\":{\"$param\":\"take\"}" "a param at a count slot"
              Expect.stringContains json "\"offset\":0" "the literal half stays bare"

              match DataFrameCodec.decodePipeline json with
              | Ok back -> Expect.equal back pipeline "decode ∘ encode is the identity over slots"
              | Error e -> failtestf "decode failed: %A" e

          testCase "a malformed param slot is refused, not read as a literal"
          <| fun _ ->
              match DataFrameCodec.decodePipeline """[{"$type":"limit","n":{"$param":7}}]""" with
              | Error(MalformedShape m) -> Expect.stringContains m "$param" "the refusal names the member"
              | other -> failtestf "expected a MalformedShape, got %A" other

              match DataFrameCodec.decodePipeline """[{"$type":"limit","n":{"count":7}}]""" with
              | Error(MalformedShape m) -> Expect.stringContains m "limit n" "the refusal names the slot"
              | other -> failtestf "expected a MalformedShape, got %A" other

          // The static walk's honesty, which is the one place the widening could have introduced a
          // WRONG answer rather than a refused one: `readColumns` cannot see a param sort column, so
          // `evalFrom` must not reuse a prior result on the strength of it.
          testCase "evalFrom declines the reuse while a slot param stands"
          <| fun _ ->
              let table =
                  { Schema = [ "a", IntType; "z", IntType ]
                    Columns =
                      [ Column.create "a" IntType [ Int 2; Int 1 ]
                        Column.create "z" IntType [ Int 9; Int 8 ] ] }

              let pipeline = [ Sort [ Slot.Param "orderBy", Asc ] ]

              // `z` is not a column any LITERAL key names, so the pre-0.23.0 reasoning would have
              // reused the prior table. The param could BE "z", so the reuse is not available.
              match DataFrame.evalFrom table (ColumnValuesChanged "z") pipeline table with
              | Error(UnboundParam("orderBy", _)) -> ()
              | other -> failtestf "expected the declined reuse to reach the evaluator and refuse, got %A" other

          testCase "Incremental.plan declines a slot-param sort BY NAME rather than guessing"
          <| fun _ ->
              match (Incremental.plan [ Sort [ Slot.Param "orderBy", Asc ] ]).Strategy with
              | ReferenceOnly(UnresolvedSlotParam("sort", "orderBy")) -> ()
              | other -> failtestf "expected a named UnresolvedSlotParam fall-back, got %A" other

              match (Incremental.plan [ Transform.sortBy [ "a", Asc ] ]).Strategy with
              | ReferenceOnly r -> failtestf "a literal sort must still be planned, got a fall-back: %A" r
              | _ -> () ]

// ---- Phase 264: the join, pivot and window answers the pre-phase evaluator gave ----
//
// Every expected table below was captured by RUNNING the nested-loop join, the per-pair pivot scan
// and the list-skipping rolling window at the commit before Phase 264 replaced them, over fixtures
// built to discriminate: duplicate keys on both sides, a null key on each side, an `Int` key that
// `cellEq` matches to a `Float` one (and `0` to `-0.0`), `NaN` keys (which `cellEq` matches to each
// other), right rows no left row matches, a pivot with a null on-value, a repeated index key and two
// numerically-equal on-values, and rolling windows whose leading run is null and whose sums are
// order-sensitive in the last bit. Equality with THOSE answers is the acceptance, not a reading of
// the semantics.

/// A cell rendered exactly: a float by its round-trip text, so `-0.0`, `NaN` and the last bit of a
/// sum are visible to the comparison (structural equality on `Float nan` is false, and `%A` rounds).
let private exactCell (c: Cell) : string =
    match c with
    | Float f -> "f:" + f.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
    | other -> sprintf "%A" other

let private expectOracle (label: string) (expected: (string * Cell list) list) (actual: Table) =
    Expect.equal
        (actual.Columns |> List.map (fun c -> c.Name, c.Cells |> List.map exactCell))
        (expected |> List.map (fun (n, cs) -> n, cs |> List.map exactCell))
        label

let private strs xs = xs |> List.map Str

let private joinLeft =
    tbl
        [ "k", IntType; "lv", StringType ]
        [ col "k" IntType [ Int 1; Int 2; Null; Int 1; Int 4; Int 0 ]
          col "lv" StringType (strs [ "a"; "b"; "c"; "d"; "e"; "f" ]) ]

let private joinRight =
    tbl
        [ "k", FloatType; "rv", StringType ]
        [ col "k" FloatType [ Float 1.0; Float 3.0; Null; Float 1.0; Float 2.0; Float -0.0 ]
          col "rv" StringType (strs [ "x"; "y"; "z"; "w"; "v"; "u" ]) ]

let private join2Left =
    tbl
        [ "s", StringType; "f", FloatType; "lv", StringType ]
        [ col "s" StringType [ Str "p"; Str "p"; Str "q"; Null; Str "p" ]
          col "f" FloatType [ Float nan; Float 1.5; Float nan; Float 1.5; Float infinity ]
          col "lv" StringType (strs [ "a"; "b"; "c"; "d"; "e" ]) ]

let private join2Right =
    tbl
        [ "s", StringType; "f", FloatType; "rv", StringType ]
        [ col "s" StringType [ Str "p"; Str "q"; Str "p"; Str "p"; Str "p" ]
          col "f" FloatType [ Float nan; Float nan; Float 1.5; Float 2.0; Float infinity ]
          col "rv" StringType (strs [ "x"; "y"; "z"; "w"; "v" ]) ]

let private pivotSrc =
    tbl
        [ "g", StringType; "h", IntType; "o", StringType; "v", FloatType ]
        [ col "g" StringType [ Str "a"; Str "b"; Str "a"; Str "a"; Str "b"; Null; Str "a"; Str "b" ]
          col "h" IntType [ Int 1; Int 1; Int 1; Int 2; Int 1; Int 1; Int 1; Int 1 ]
          col "o" StringType [ Str "x"; Str "y"; Str "x"; Null; Str "x"; Str "y"; Str "z"; Str "y" ]
          col
              "v"
              FloatType
              [ Float 0.1
                Float 2.0
                Float 0.2
                Float 4.0
                Float 5.0
                Float 6.0
                Float 0.3
                Null ] ]

let private pivotNumericOn =
    tbl
        [ "g", StringType; "o", FloatType; "v", IntType ]
        [ col "g" StringType [ Str "a"; Str "a"; Str "b"; Str "a"; Str "b"; Str "a" ]
          col "o" FloatType [ Int 1; Float 1.0; Int 2; Float nan; Float nan; Null ]
          col "v" IntType [ Int 10; Int 20; Int 30; Int 40; Int 50; Int 60 ] ]

let private windowSrc =
    tbl
        [ "p", StringType; "ord", IntType; "v", FloatType ]
        [ col "p" StringType (strs [ "A"; "B"; "A"; "A"; "B"; "A"; "B"; "A"; "A"; "A" ])
          col "ord" IntType ([ 5; 1; 3; 1; 2; 2; 3; 4; 6; 7 ] |> List.map Int)
          col
              "v"
              FloatType
              [ Float 1e16
                Null
                Float 0.1
                Null
                Int 3
                Null
                Float 0.7
                Float 0.2
                Float -1e16
                Float 1.0 ] ]

/// The pre-phase answer for `join1 Inner`.
let private oJoin1Inner: (string * Cell list) list =
    [ "k", [ Int 1; Int 1; Int 2; Int 1; Int 1; Int 0 ]
      "lv", [ Str "a"; Str "a"; Str "b"; Str "d"; Str "d"; Str "f" ]
      "k_right", [ Float 1.0; Float 1.0; Float 2.0; Float 1.0; Float 1.0; Float -0.0 ]
      "rv", [ Str "x"; Str "w"; Str "v"; Str "x"; Str "w"; Str "u" ] ]

/// The pre-phase answer for `join1 Left`.
let private oJoin1Left: (string * Cell list) list =
    [ "k", [ Int 1; Int 1; Int 2; Null; Int 1; Int 1; Int 4; Int 0 ]
      "lv", [ Str "a"; Str "a"; Str "b"; Str "c"; Str "d"; Str "d"; Str "e"; Str "f" ]
      "k_right",
      [ Float 1.0
        Float 1.0
        Float 2.0
        Null
        Float 1.0
        Float 1.0
        Null
        Float -0.0 ]
      "rv", [ Str "x"; Str "w"; Str "v"; Null; Str "x"; Str "w"; Null; Str "u" ] ]

/// The pre-phase answer for `join1 Right`.
let private oJoin1Right: (string * Cell list) list =
    [ "k", [ Int 1; Int 1; Int 2; Int 1; Int 1; Int 0; Null; Null ]
      "lv", [ Str "a"; Str "a"; Str "b"; Str "d"; Str "d"; Str "f"; Null; Null ]
      "k_right",
      [ Float 1.0
        Float 1.0
        Float 2.0
        Float 1.0
        Float 1.0
        Float -0.0
        Float 3.0
        Null ]
      "rv", [ Str "x"; Str "w"; Str "v"; Str "x"; Str "w"; Str "u"; Str "y"; Str "z" ] ]

/// The pre-phase answer for `join1 Outer`.
let private oJoin1Outer: (string * Cell list) list =
    [ "k", [ Int 1; Int 1; Int 2; Null; Int 1; Int 1; Int 4; Int 0; Null; Null ]
      "lv",
      [ Str "a"
        Str "a"
        Str "b"
        Str "c"
        Str "d"
        Str "d"
        Str "e"
        Str "f"
        Null
        Null ]
      "k_right",
      [ Float 1.0
        Float 1.0
        Float 2.0
        Null
        Float 1.0
        Float 1.0
        Null
        Float -0.0
        Float 3.0
        Null ]
      "rv",
      [ Str "x"
        Str "w"
        Str "v"
        Null
        Str "x"
        Str "w"
        Null
        Str "u"
        Str "y"
        Str "z" ] ]

/// The pre-phase answer for `join1 Semi`.
let private oJoin1Semi: (string * Cell list) list =
    [ "k", [ Int 1; Int 2; Int 1; Int 0 ]
      "lv", [ Str "a"; Str "b"; Str "d"; Str "f" ] ]

/// The pre-phase answer for `join1 Anti`.
let private oJoin1Anti: (string * Cell list) list =
    [ "k", [ Null; Int 4 ]; "lv", [ Str "c"; Str "e" ] ]

/// The pre-phase answer for `join2 Inner`.
let private oJoin2Inner: (string * Cell list) list =
    [ "s", [ Str "p"; Str "p"; Str "q"; Str "p" ]
      "f", [ Float nan; Float 1.5; Float nan; Float infinity ]
      "lv", [ Str "a"; Str "b"; Str "c"; Str "e" ]
      "s_right", [ Str "p"; Str "p"; Str "q"; Str "p" ]
      "f_right", [ Float nan; Float 1.5; Float nan; Float infinity ]
      "rv", [ Str "x"; Str "z"; Str "y"; Str "v" ] ]

/// The pre-phase answer for `join2 Left`.
let private oJoin2Left: (string * Cell list) list =
    [ "s", [ Str "p"; Str "p"; Str "q"; Null; Str "p" ]
      "f", [ Float nan; Float 1.5; Float nan; Float 1.5; Float infinity ]
      "lv", [ Str "a"; Str "b"; Str "c"; Str "d"; Str "e" ]
      "s_right", [ Str "p"; Str "p"; Str "q"; Null; Str "p" ]
      "f_right", [ Float nan; Float 1.5; Float nan; Null; Float infinity ]
      "rv", [ Str "x"; Str "z"; Str "y"; Null; Str "v" ] ]

/// The pre-phase answer for `join2 Right`.
let private oJoin2Right: (string * Cell list) list =
    [ "s", [ Str "p"; Str "p"; Str "q"; Str "p"; Null ]
      "f", [ Float nan; Float 1.5; Float nan; Float infinity; Null ]
      "lv", [ Str "a"; Str "b"; Str "c"; Str "e"; Null ]
      "s_right", [ Str "p"; Str "p"; Str "q"; Str "p"; Str "p" ]
      "f_right", [ Float nan; Float 1.5; Float nan; Float infinity; Float 2.0 ]
      "rv", [ Str "x"; Str "z"; Str "y"; Str "v"; Str "w" ] ]

/// The pre-phase answer for `join2 Outer`.
let private oJoin2Outer: (string * Cell list) list =
    [ "s", [ Str "p"; Str "p"; Str "q"; Null; Str "p"; Null ]
      "f", [ Float nan; Float 1.5; Float nan; Float 1.5; Float infinity; Null ]
      "lv", [ Str "a"; Str "b"; Str "c"; Str "d"; Str "e"; Null ]
      "s_right", [ Str "p"; Str "p"; Str "q"; Null; Str "p"; Str "p" ]
      "f_right", [ Float nan; Float 1.5; Float nan; Null; Float infinity; Float 2.0 ]
      "rv", [ Str "x"; Str "z"; Str "y"; Null; Str "v"; Str "w" ] ]

/// The pre-phase answer for `join2 Semi`.
let private oJoin2Semi: (string * Cell list) list =
    [ "s", [ Str "p"; Str "p"; Str "q"; Str "p" ]
      "f", [ Float nan; Float 1.5; Float nan; Float infinity ]
      "lv", [ Str "a"; Str "b"; Str "c"; Str "e" ] ]

/// The pre-phase answer for `join2 Anti`.
let private oJoin2Anti: (string * Cell list) list =
    [ "s", [ Null ]; "f", [ Float 1.5 ]; "lv", [ Str "d" ] ]

/// The pre-phase answer for `pivot2 Sum`.
let private oPivot2Sum: (string * Cell list) list =
    [ "g", [ Str "a"; Str "b"; Str "a"; Null ]
      "h", [ Int 1; Int 1; Int 2; Int 1 ]
      "x", [ Float 0.30000000000000004; Float 5.0; Null; Null ]
      "y", [ Null; Float 2.0; Null; Float 6.0 ]
      "z", [ Float 0.3; Null; Null; Null ] ]

/// The pre-phase answer for `pivot2 Mean`.
let private oPivot2Mean: (string * Cell list) list =
    [ "g", [ Str "a"; Str "b"; Str "a"; Null ]
      "h", [ Int 1; Int 1; Int 2; Int 1 ]
      "x", [ Float 0.15000000000000002; Float 5.0; Null; Null ]
      "y", [ Null; Float 2.0; Null; Float 6.0 ]
      "z", [ Float 0.3; Null; Null; Null ] ]

/// The pre-phase answer for `pivot2 First`.
let private oPivot2First: (string * Cell list) list =
    [ "g", [ Str "a"; Str "b"; Str "a"; Null ]
      "h", [ Int 1; Int 1; Int 2; Int 1 ]
      "x", [ Float 0.1; Float 5.0; Null; Null ]
      "y", [ Null; Float 2.0; Null; Float 6.0 ]
      "z", [ Float 0.3; Null; Null; Null ] ]

/// The pre-phase answer for `pivot2 Last`.
let private oPivot2Last: (string * Cell list) list =
    [ "g", [ Str "a"; Str "b"; Str "a"; Null ]
      "h", [ Int 1; Int 1; Int 2; Int 1 ]
      "x", [ Float 0.2; Float 5.0; Null; Null ]
      "y", [ Null; Null; Null; Float 6.0 ]
      "z", [ Float 0.3; Null; Null; Null ] ]

/// The pre-phase answer for `pivot2 Count`.
let private oPivot2Count: (string * Cell list) list =
    [ "g", [ Str "a"; Str "b"; Str "a"; Null ]
      "h", [ Int 1; Int 1; Int 2; Int 1 ]
      "x", [ Int 2; Int 1; Int 0; Int 0 ]
      "y", [ Int 0; Int 1; Int 0; Int 1 ]
      "z", [ Int 1; Int 0; Int 0; Int 0 ] ]

/// The pre-phase answer for `pivot2 Min`.
let private oPivot2Min: (string * Cell list) list =
    [ "g", [ Str "a"; Str "b"; Str "a"; Null ]
      "h", [ Int 1; Int 1; Int 2; Int 1 ]
      "x", [ Float 0.1; Float 5.0; Null; Null ]
      "y", [ Null; Float 2.0; Null; Float 6.0 ]
      "z", [ Float 0.3; Null; Null; Null ] ]

/// The pre-phase answer for `pivot1 Sum`.
let private oPivot1Sum: (string * Cell list) list =
    [ "g", [ Str "a"; Str "b"; Null ]
      "x", [ Float 0.30000000000000004; Float 5.0; Null ]
      "y", [ Null; Float 2.0; Float 6.0 ]
      "z", [ Float 0.3; Null; Null ] ]

/// The pre-phase answer for `pivotNum Sum`.
let private oPivotnumSum: (string * Cell list) list =
    [ "g", [ Str "a"; Str "b" ]
      "\"NaN\"", [ Int 40; Int 50 ]
      "\"NaN\"", [ Int 40; Int 50 ]
      "1", [ Int 30; Null ]
      "1", [ Int 30; Null ]
      "2", [ Null; Int 30 ] ]

/// The pre-phase answer for `window RollingSum`.
let private oWindowRollingsum: (string * Cell list) list =
    [ "p",
      [ Str "A"
        Str "B"
        Str "A"
        Str "A"
        Str "B"
        Str "A"
        Str "B"
        Str "A"
        Str "A"
        Str "A" ]
      "ord", [ Int 5; Int 1; Int 3; Int 1; Int 2; Int 2; Int 3; Int 4; Int 6; Int 7 ]
      "v",
      [ Float 10000000000000000.0
        Null
        Float 0.1
        Null
        Int 3
        Null
        Float 0.7
        Float 0.2
        Float -10000000000000000.0
        Float 1.0 ]
      "w",
      [ Float 10000000000000000.0
        Null
        Float 0.1
        Null
        Float 3.0
        Null
        Float 3.7
        Float 0.30000000000000004
        Float 0.0
        Float 1.0 ] ]

/// The pre-phase answer for `window RollingMean`.
let private oWindowRollingmean: (string * Cell list) list =
    [ "p",
      [ Str "A"
        Str "B"
        Str "A"
        Str "A"
        Str "B"
        Str "A"
        Str "B"
        Str "A"
        Str "A"
        Str "A" ]
      "ord", [ Int 5; Int 1; Int 3; Int 1; Int 2; Int 2; Int 3; Int 4; Int 6; Int 7 ]
      "v",
      [ Float 10000000000000000.0
        Null
        Float 0.1
        Null
        Int 3
        Null
        Float 0.7
        Float 0.2
        Float -10000000000000000.0
        Float 1.0 ]
      "w",
      [ Float 3333333333333333.5
        Null
        Float 0.1
        Null
        Float 3.0
        Null
        Float 1.85
        Float 0.15000000000000002
        Float 0.0
        Float 0.3333333333333333 ] ]

/// The pre-phase answer for `window RowNumber`.
let private oWindowRownumber: (string * Cell list) list =
    [ "p",
      [ Str "A"
        Str "B"
        Str "A"
        Str "A"
        Str "B"
        Str "A"
        Str "B"
        Str "A"
        Str "A"
        Str "A" ]
      "ord", [ Int 5; Int 1; Int 3; Int 1; Int 2; Int 2; Int 3; Int 4; Int 6; Int 7 ]
      "v",
      [ Float 10000000000000000.0
        Null
        Float 0.1
        Null
        Int 3
        Null
        Float 0.7
        Float 0.2
        Float -10000000000000000.0
        Float 1.0 ]
      "w", [ Int 5; Int 1; Int 3; Int 1; Int 2; Int 2; Int 3; Int 4; Int 6; Int 7 ] ]

/// The pre-phase answer for `window CumulSum`.
let private oWindowCumulsum: (string * Cell list) list =
    [ "p",
      [ Str "A"
        Str "B"
        Str "A"
        Str "A"
        Str "B"
        Str "A"
        Str "B"
        Str "A"
        Str "A"
        Str "A" ]
      "ord", [ Int 5; Int 1; Int 3; Int 1; Int 2; Int 2; Int 3; Int 4; Int 6; Int 7 ]
      "v",
      [ Float 10000000000000000.0
        Null
        Float 0.1
        Null
        Int 3
        Null
        Float 0.7
        Float 0.2
        Float -10000000000000000.0
        Float 1.0 ]
      "w",
      [ Float 10000000000000000.0
        Float 0.0
        Float 0.1
        Float 0.0
        Float 3.0
        Float 0.0
        Float 3.7
        Float 0.30000000000000004
        Float 0.0
        Float 1.0 ] ]

[<Tests>]
let phase264OracleTests =
    testList
        "DataFrame.Phase264Oracle"
        [ testCase "Phase 264 — the hash join reproduces the nested loop: duplicates, nulls, Int/Float and -0.0 keys"
          <| fun _ ->
              let join how =
                  DataFrame.evalPipeline [ Join(Embedded joinRight, [ "k", "k" ], how) ] joinLeft
                  |> okTable

              expectOracle "inner" oJoin1Inner (join Inner)
              expectOracle "left" oJoin1Left (join Left)

              expectOracle
                  "right: unmatched right rows after every left-side row, in right order"
                  oJoin1Right
                  (join Right)

              expectOracle "outer" oJoin1Outer (join Outer)
              expectOracle "semi" oJoin1Semi (join Semi)
              expectOracle "anti" oJoin1Anti (join Anti)

          testCase "Phase 264 — the hash join reproduces the nested loop on a two-column key with NaN and a null"
          <| fun _ ->
              let join how =
                  DataFrame.evalPipeline [ Join(Embedded join2Right, [ "s", "s"; "f", "f" ], how) ] join2Left
                  |> okTable

              expectOracle "inner" oJoin2Inner (join Inner)
              expectOracle "left" oJoin2Left (join Left)
              expectOracle "right" oJoin2Right (join Right)
              expectOracle "outer" oJoin2Outer (join Outer)
              expectOracle "semi" oJoin2Semi (join Semi)
              expectOracle "anti" oJoin2Anti (join Anti)

          testCase "Phase 264 — the one-pass pivot reproduces the per-pair scan: null on-value, repeated index key"
          <| fun _ ->
              let pivot index agg =
                  DataFrame.evalPipeline
                      [ Pivot
                            { Index = index
                              On = "o"
                              Values = "v"
                              Agg = agg } ]
                      pivotSrc
                  |> okTable

              expectOracle "sum, cells aggregated in frame order" oPivot2Sum (pivot [ "g"; "h" ] Sum)
              expectOracle "mean" oPivot2Mean (pivot [ "g"; "h" ] Mean)
              expectOracle "first" oPivot2First (pivot [ "g"; "h" ] First)
              expectOracle "last" oPivot2Last (pivot [ "g"; "h" ] Last)
              expectOracle "count: an absent pair aggregates the empty list" oPivot2Count (pivot [ "g"; "h" ] Count)
              expectOracle "min" oPivot2Min (pivot [ "g"; "h" ] Min)
              expectOracle "one index column" oPivot1Sum (pivot [ "g" ] Sum)

          testCase "Phase 264 — the one-pass pivot keeps the cellEq matching: Int 1 and Float 1.0, NaN and NaN"
          <| fun _ ->
              DataFrame.evalPipeline
                  [ Pivot
                        { Index = [ "g" ]
                          On = "o"
                          Values = "v"
                          Agg = Sum } ]
                  pivotNumericOn
              |> okTable
              |> expectOracle "numerically-equal on-values each collect every cellEq match" oPivotnumSum

          testCase "Phase 264 — rolling windows and the scatter restore reproduce the pre-phase bits"
          <| fun _ ->
              let window fn =
                  DataFrame.evalPipeline
                      [ Window
                            { PartitionBy = [ "p" ]
                              OrderBy = [ "ord", Asc ]
                              Fn = fn
                              Of = "v"
                              As = "w" } ]
                      windowSrc
                  |> okTable

              expectOracle "rolling sum: a leading null run, order-sensitive sums" oWindowRollingsum (window RollingSum)
              expectOracle "rolling mean" oWindowRollingmean (window RollingMean)

              expectOracle
                  "row number: input order restored across interleaved partitions"
                  oWindowRownumber
                  (window RowNumber)

              expectOracle "cumulative sum is untouched" oWindowCumulsum (window CumulSum) ]

// ---- Phase 265 — token equality under the partitioning verbs ----
//
// `GroupBy`, `Distinct`, `Intersect`/`Except`, `Pivot`'s index groups, `Window`'s partitions and the
// seam's maintained grouping partition through one internal comparer rather than through minted
// token strings. The law: two cells land in ONE group exactly when their `cellToken`s are the same
// string. It is asserted through each verb's public behaviour over a pair of one-cell rows, so it
// holds the comparer's equality AND its hash — a hash table whose hash split two equal cells would
// report two groups — and it holds the wiring: a verb keyed on the join's `cellEq` relation instead
// would merge `Int 1` with `Float 1.0` and split two nulls, and fail here.

/// Cells drawn to reach every case of the token: both zeroes, two `NaN` bit patterns, both
/// infinities, `Int` against `Float` of equal value, adjacent floats, equal strings under the three
/// string tags, strings that spell another tag's token, the `int32` extremes, and `Null`.
let private tokenCasePool: Cell list =
    [ Int 0
      Int 1
      Int -1
      Int System.Int32.MaxValue
      Int System.Int32.MinValue
      Float 0.0
      Float -0.0
      Float 1.0
      Float -1.0
      Float 0.1
      Float(0.1 + 0.2)
      Float 0.3
      Float nan
      Float(System.BitConverter.Int64BitsToDouble 0x7ff8000000000001L)
      Float infinity
      Float -infinity
      Float System.Double.Epsilon
      Float System.Double.MaxValue
      Float 1.0000000000000002
      Float 1e16
      Float(1e16 + 2.0)
      Float 2147483647.0
      Bool true
      Bool false
      Str ""
      Str "a"
      Str "1"
      Str "i:1"
      Str "NaN"
      Str "2026-01-01"
      Date "2026-01-01"
      Date "a"
      Timestamp "2026-01-01"
      Timestamp "a"
      Null ]

/// A seeded draw of further cells, crowded into small ranges so that equal tokens recur: integers,
/// floats that are sometimes integral and sometimes a negative zero, and short strings under all
/// three string tags.
let private drawnCells (seed: int) (count: int) : Cell list =
    let mutable state = uint32 seed

    let next (bound: int) =
        state <- state * 1664525u + 1013904223u
        int ((state >>> 8) % uint32 bound)

    [ for _ in 1..count ->
          match next 7 with
          | 0 -> Int(next 5 - 2)
          | 1 -> Float(float (next 5 - 2))
          | 2 -> Float(float (next 9 - 4) / 4.0)
          | 3 -> Str(string (char (int 'a' + next 3)))
          | 4 -> Date(string (char (int 'a' + next 3)))
          | 5 -> Timestamp(string (char (int 'a' + next 3)))
          | _ -> if next 2 = 0 then Null else Float -0.0 ]

/// One partition count per verb, for the two-row frame `[a; b]` (or `a` against `b` for the set
/// operations): 1 when the verb put the two cells together, 2 when it kept them apart.
let private partitionsBy (a: Cell) (b: Cell) : (string * int) list =
    let pair: Table =
        tbl
            [ "id", StringType; "k", StringType; "o", StringType; "v", IntType ]
            [ col "id" StringType [ Str "r0"; Str "r1" ]
              col "k" StringType [ a; b ]
              col "o" StringType [ Str "x"; Str "x" ]
              col "v" IntType [ Int 1; Int 2 ] ]

    let one (c: Cell) : Table =
        tbl [ "k", StringType ] [ col "k" StringType [ c ] ]

    let rows (r: Result<Table, EvalError>) = r |> okTable |> Table.rowCount
    let countGroups = GroupBy([ "k" ], [ { Name = "n"; Fn = Count; Of = "v" } ])

    let windowTogether =
        let t =
            DataFrame.evalPipeline
                [ Window
                      { PartitionBy = [ "k" ]
                        OrderBy = [ "v", Asc ]
                        Fn = RowNumber
                        Of = "v"
                        As = "rn" } ]
                pair
            |> okTable

        // the second row numbers 2 exactly when it shares the first row's partition
        if cellsOf "rn" t = [ Int 1; Int 2 ] then 1 else 2

    let seamGroups =
        match Incremental.primeOn (RowIdentity.byColumn "id") [ countGroups ] pair with
        | Ok state -> Table.rowCount (Incremental.result state)
        | Error e -> failtestf "prime failed: %s" (DataFrame.errorString e)

    [ "GroupBy", rows (DataFrame.evalPipeline [ countGroups ] pair)
      "Distinct", rows (DataFrame.evalPipeline [ Project [ "k", "k" ]; Distinct ] pair)
      "Intersect", 2 - rows (DataFrame.evalPipeline [ Intersect(Embedded(one b)) ] (one a))
      "Except", 1 + rows (DataFrame.evalPipeline [ Except(Embedded(one b)) ] (one a))
      "Pivot",
      rows (
          DataFrame.evalPipeline
              [ Pivot
                    { Index = [ "k" ]
                      On = "o"
                      Values = "v"
                      Agg = Sum } ]
              pair
      )
      "Window", windowTogether
      "Incremental GroupBy", seamGroups ]

[<Tests>]
let tokenEqualityTests =
    testList
        "DataFrame.TokenEquality"
        [ testCase "the pool reaches every case the law must cover"
          <| fun _ ->
              // The pool is the law's teeth; this pins that it still has them: an Int and a Float of
              // equal value with different tokens, two zeroes with one token, two NaNs with one token,
              // one string under three tags with three tokens, and a string spelling a token.
              let tok = DataFrame.cellToken
              Expect.notEqual (tok (Int 1)) (tok (Float 1.0)) "Int 1 and Float 1.0 are two tokens"
              Expect.equal (tok (Float 0.0)) (tok (Float -0.0)) "the two zeroes are one token"

              Expect.equal
                  (tok (Float nan))
                  (tok (Float(System.BitConverter.Int64BitsToDouble 0x7ff8000000000001L)))
                  "two NaN bit patterns are one token"

              Expect.equal
                  (List.distinct [ tok (Str "a"); tok (Date "a"); tok (Timestamp "a") ]
                   |> List.length)
                  3
                  "one string under three tags is three tokens"

              Expect.notEqual (tok (Str "i:1")) (tok (Int 1)) "a string spelling a token is not that token"

          testCase "every partitioning verb puts two cells together exactly when their tokens are equal"
          <| fun _ ->
              let pool = tokenCasePool @ drawnCells 265 25
              let mutable checkedPairs = 0
              let mutable together = 0

              for a in pool do
                  for b in pool do
                      let expected =
                          if DataFrame.cellToken a = DataFrame.cellToken b then
                              1
                          else
                              2

                      if expected = 1 then
                          together <- together + 1

                      for verb, got in partitionsBy a b do
                          if got <> expected then
                              failtestf
                                  "%s partitioned %A and %A into %d group(s); their tokens say %d"
                                  verb
                                  a
                                  b
                                  got
                                  expected

                      checkedPairs <- checkedPairs + 1

              // vacuity: the pairs that must merge include more than each cell with itself
              Expect.isGreaterThan together (List.length pool) "some DISTINCT cells share a token"
              Expect.equal checkedPairs (List.length pool * List.length pool) "every pair was checked"

          testCase "a multi-column key partitions exactly as the row token does"
          <| fun _ ->
              // Two key columns, so the comparer's cell-by-cell walk and its combined hash are what
              // decide; a row whose cells match position by position is one group, and no other.
              let cells =
                  drawnCells 2651 24 @ [ Int 1; Float 1.0; Null; Float nan; Str "a"; Date "a" ]

              let n = List.length cells
              let mutable state = 7u

              let next () =
                  state <- state * 1664525u + 1013904223u
                  int ((state >>> 8) % uint32 n)

              for _ in 1..600 do
                  let a1, a2, b1, b2 = cells[next ()], cells[next ()], cells[next ()], cells[next ()]

                  let expected =
                      if DataFrame.rowTokenString [ a1; a2 ] = DataFrame.rowTokenString [ b1; b2 ] then
                          1
                      else
                          2

                  let t =
                      tbl
                          [ "k1", StringType; "k2", StringType; "v", IntType ]
                          [ col "k1" StringType [ a1; b1 ]
                            col "k2" StringType [ a2; b2 ]
                            col "v" IntType [ Int 1; Int 2 ] ]

                  let grouped =
                      DataFrame.evalPipeline [ GroupBy([ "k1"; "k2" ], [ { Name = "n"; Fn = Count; Of = "v" } ]) ] t
                      |> okTable
                      |> Table.rowCount

                  let distinct =
                      DataFrame.evalPipeline [ Project [ "k1", "k1"; "k2", "k2" ]; Distinct ] t
                      |> okTable
                      |> Table.rowCount

                  Expect.equal grouped expected (sprintf "GroupBy over [%A; %A] and [%A; %A]" a1 a2 b1 b2)
                  Expect.equal distinct expected (sprintf "Distinct over [%A; %A] and [%A; %A]" a1 a2 b1 b2)

          testCase "the comparer itself: equal exactly when the tokens are, and equal cells hash alike"
          <| fun _ ->
              // The verbs above observe the comparer only through a hash table, which never calls
              // `Equals` on two rows whose hashes differ — so an `Equals` that ignored a cell would
              // hide behind the hash. This case reads the comparer directly. It is internal to the
              // package (a hash table's comparer is not a consumer surface), so it is reached by
              // reflection, and a missing member fails the case rather than skipping it.
              let flags =
                  System.Reflection.BindingFlags.Static
                  ||| System.Reflection.BindingFlags.Public
                  ||| System.Reflection.BindingFlags.NonPublic

              let cellKey =
                  match typeof<JoinKind>.Assembly.GetType "Fuaran.Compute.DataFrame+CellKey" with
                  | null -> failtest "the internal CellKey module was not found"
                  | t -> t

              let memberOf (name: string) =
                  match cellKey.GetMethod(name, flags) with
                  | null -> failtestf "CellKey.%s was not found" name
                  | m -> m

              let equalsM = memberOf "equals"
              let hashM = memberOf "hashCell"

              let rowComparer =
                  match cellKey.GetProperty("row", flags) with
                  | null -> failtest "CellKey.row was not found"
                  | p -> p.GetValue null :?> System.Collections.Generic.IEqualityComparer<Cell[]>

              let equals (a: Cell) (b: Cell) =
                  equalsM.Invoke(null, [| box a; box b |]) :?> bool

              let hashOf (c: Cell) = hashM.Invoke(null, [| box c |]) :?> int

              let pool = tokenCasePool @ drawnCells 2652 25

              for a in pool do
                  for b in pool do
                      let tokensEqual = DataFrame.cellToken a = DataFrame.cellToken b

                      if equals a b <> tokensEqual then
                          failtestf "equals %A %A = %b, but the tokens say %b" a b (not tokensEqual) tokensEqual

                      if tokensEqual && hashOf a <> hashOf b then
                          failtestf "%A and %A are equal but hash apart" a b

              // Rows: every pair over a small cell set, lengths one to three, including rows that
              // agree on a prefix and differ after it — the case a short-circuiting walk gets wrong.
              let small =
                  [ Int 1; Float 1.0; Float -0.0; Float 0.0; Float nan; Null; Str "a"; Date "a" ]

              let rows =
                  [ for x in small -> [| x |] ]
                  @ [ for x in small do
                          for y in small -> [| x; y |] ]
                  @ [ for y in small -> [| Int 1; Str "a"; y |] ]

              for r1 in rows do
                  for r2 in rows do
                      let tokensEqual =
                          DataFrame.rowTokenString (List.ofArray r1) = DataFrame.rowTokenString (List.ofArray r2)

                      if rowComparer.Equals(r1, r2) <> tokensEqual then
                          failtestf "row Equals %A %A disagrees with the row tokens (%b)" r1 r2 tokensEqual

                      if tokensEqual && rowComparer.GetHashCode r1 <> rowComparer.GetHashCode r2 then
                          failtestf "rows %A and %A are equal but hash apart" r1 r2

          testCase "groups come out in first-appearance order with the first row's key cells"
          <| fun _ ->
              // -0.0 opens the zero group and 0.0 joins it; the output carries the OPENER's cell.
              let t =
                  tbl
                      [ "k", FloatType; "v", IntType ]
                      [ col "k" FloatType [ Float -0.0; Float nan; Float 0.0; Float 2.0; Float nan ]
                        col "v" IntType [ Int 1; Int 2; Int 3; Int 4; Int 5 ] ]

              let out =
                  DataFrame.evalPipeline [ GroupBy([ "k" ], [ { Name = "s"; Fn = Sum; Of = "v" } ]) ] t
                  |> okTable

              let keys = cellsOf "k" out |> List.map DataFrame.cellToken
              Expect.equal keys (List.map DataFrame.cellToken [ Float -0.0; Float nan; Float 2.0 ]) "opener order"
              Expect.equal (cellsOf "s" out) [ Int 4; Int 7; Int 4 ] "members in frame order"

              // the opener's cell itself, not a canonical representative: its sign survives
              match cellsOf "k" out with
              | Float z :: _ -> Expect.isTrue (System.Double.IsNegative z) "the -0.0 that opened the group"
              | other -> failtestf "unexpected key cells %A" other ]

// ---- Phase 266: the compiled expression form and the static typer ------------------------------
//
// `evalFilter` and `evalDerive` compile a step's expression once into a closure tree and run it
// per row; `evalExprInRow` stays the reference. The law below holds the two together over generated
// (schema, expression, row) triples, cell for cell and error for error, and guards its own sample:
// every typed kernel and the boxed fall-back must be reached, present cells and nulls and errors
// must all occur, or the green proves less than it says.

/// The schema every generated expression is typed against: two columns per family, so a binary
/// node can draw both operands from one family and reach the family's kernel.
let private typedSchema: Schema =
    [ "i", IntType
      "j", IntType
      "f", FloatType
      "g", FloatType
      "s", StringType
      "t", StringType
      "d", DateType
      "e", DateType
      "ts", TimestampType
      "tt", TimestampType
      "b", BoolType
      "c", BoolType
      // Phase 277 — the exact decimal, which no typed kernel carries: every node over it is boxed.
      "m", DecimalType
      "n", DecimalType ]

let private typedEnv: Map<string, Cell> =
    Map.ofList [ "p", Int 7; "q", Str "ab"; "pf", Float 2.5; "pn", Null ]

/// A cell of any family, for a literal or a cell that disagrees with its column's declared type.
let private anyCell (rng: System.Random) : Cell =
    match rng.Next 12 with
    | 0 -> Null
    | 1 -> Int(rng.Next(-5, 6))
    | 2 ->
        Int(
            if rng.Next 2 = 0 then
                System.Int32.MaxValue
            else
                System.Int32.MinValue
        )
    | 3 -> Float(float (rng.Next(-20, 21)) / 4.0)
    | 4 -> Float(if rng.Next 2 = 0 then nan else infinity)
    | 5 -> Float 0.0
    | 6 -> Str [ ""; "ab"; "abc"; "b"; "2024-01-05"; "12"; "x" ].[rng.Next 7]
    | 7 -> Date [ "2024-01-05"; "2024-02-29"; "1999-12-31" ].[rng.Next 3]
    | 8 -> Timestamp [ "2024-01-05T10:00:00Z"; "2024-01-05T09:59:59Z" ].[rng.Next 2]
    | 9 -> Bool(rng.Next 2 = 0)
    | 10 -> Int 0
    | 11 when rng.Next 2 = 0 -> Decimal [ "0"; "1.5"; "-0.25"; "12"; "0.1" ].[rng.Next 5]
    | _ -> Str "ab"

/// A cell conforming to a column type.
let private conformingCell (rng: System.Random) (ty: ColumnType) : Cell =
    match ty with
    | IntType ->
        Int(
            if rng.Next 10 = 0 then
                System.Int32.MaxValue
            else
                rng.Next(-5, 6)
        )
    | FloatType ->
        match rng.Next 8 with
        | 0 -> Float nan
        | 1 -> Float -0.0
        | 2 -> Float infinity
        | _ -> Float(float (rng.Next(-20, 21)) / 4.0)
    | StringType -> Str [ ""; "ab"; "abc"; "b"; "2024-01-05"; "12" ].[rng.Next 6]
    | DateType -> Date [ "2024-01-05"; "2024-02-29"; "1999-12-31" ].[rng.Next 3]
    | TimestampType -> Timestamp [ "2024-01-05T10:00:00Z"; "2024-01-05T09:59:59Z" ].[rng.Next 2]
    | BoolType -> Bool(rng.Next 2 = 0)
    | DecimalType -> Decimal [ "0"; "1.5"; "-0.25"; "12"; "0.1"; "-3"; "100.005" ].[rng.Next 7]

/// A row over `typedSchema`: mostly conforming cells, some nulls, and now and then a cell that
/// disagrees with its column — the case a typed kernel must hand to the reference arm.
let private typedRow (rng: System.Random) : Cell[] =
    typedSchema
    |> List.map (fun (_, ty) ->
        match rng.Next 20 with
        | 0 -> anyCell rng
        | 1
        | 2
        | 3 -> Null
        | _ -> conformingCell rng ty)
    |> List.toArray

let private colsOfType (ty: ColumnType) : string list =
    typedSchema |> List.filter (fun (_, t) -> t = ty) |> List.map fst

let private pick (rng: System.Random) (xs: 'a list) : 'a = List.item (rng.Next(List.length xs)) xs

let private allBinOps: BinOp list =
    [ Add
      Sub
      Mul
      Div
      Mod
      Eq
      Ne
      Lt
      Le
      Gt
      Ge
      And
      Or
      Contains
      StartsWith
      EndsWith ]

let private allFns: ScalarFn list =
    [ Abs
      Round
      Floor
      Ceil
      Length
      Lower
      Upper
      Substr
      DatePart
      Concat
      Trim
      Replace
      DateDiffDays
      Sqrt
      Least
      Greatest
      IndexOf ]

let private allTypes: ColumnType list =
    [ IntType
      FloatType
      StringType
      DateType
      TimestampType
      BoolType
      DecimalType ]

/// A generated expression of bounded depth. Binary nodes draw their operands from one family two
/// times in three, so the typed kernels are reached often; the rest of the draws mix families and
/// node kinds freely, so the boxed arm, the error paths and the mixed typings are reached too.
let rec private genExpr (rng: System.Random) (depth: int) : ColExpr =
    let leaf () =
        match rng.Next 10 with
        | 0 -> Lit(anyCell rng)
        | 1 -> Param(pick rng [ "p"; "q"; "pf"; "pn"; "unbound" ])
        | 2 when rng.Next 6 = 0 -> Now NowGrain.Date
        | _ -> Col(pick rng (List.map fst typedSchema))

    let familyCol (ty: ColumnType) = Col(pick rng (colsOfType ty))

    if depth <= 0 then
        leaf ()
    else
        let sub () = genExpr rng (depth - 1)

        match rng.Next 16 with
        | 0
        | 1
        | 2
        | 3
        | 4 ->
            let op = pick rng allBinOps

            if rng.Next 3 = 0 then
                Binary(op, sub (), sub ())
            else
                let family =
                    match op with
                    | Add
                    | Sub
                    | Mul
                    | Mod
                    | Div -> pick rng [ IntType; IntType; FloatType; DecimalType ]
                    | Eq
                    | Ne
                    | Lt
                    | Le
                    | Gt
                    | Ge -> pick rng allTypes
                    | And
                    | Or -> BoolType
                    | Contains
                    | StartsWith
                    | EndsWith -> StringType

                let operand () =
                    match rng.Next 6 with
                    | 0 -> sub ()
                    | 1 -> Lit(conformingCell rng family)
                    | 2 when family = IntType -> familyCol FloatType
                    | 3 when family = DecimalType -> familyCol IntType
                    | _ -> familyCol family

                Binary(op, operand (), operand ())
        | 5 -> Not(sub ())
        | 6 -> Coalesce(List.init (rng.Next 4) (fun _ -> sub ()))
        | 7 -> Case(List.init (1 + rng.Next 2) (fun _ -> sub (), sub ()), sub ())
        | 8 -> Cast(pick rng allTypes, sub ())
        | 9
        | 10 -> ApplyFn(pick rng allFns, List.init (rng.Next 4) (fun _ -> sub ()))
        | 11 -> InList(sub (), List.init (rng.Next 4) (fun _ -> sub ()))
        | 12 -> IsNull(sub ())
        | 13 -> InParam(sub (), pick rng [ "p"; "unbound" ])
        // Phase 277 — the two rounding nodes, at a literal scale, an out-of-range one, and a
        // param scale bound to an int and to a string.
        | _ ->
            let rounding =
                { Scale = pick rng [ Slot.Lit 0; Slot.Lit 2; Slot.Lit 1001; Slot.Param "p"; Slot.Param "q" ]
                  Mode =
                    pick
                        rng
                        [ RoundingMode.HalfEven
                          RoundingMode.HalfUp
                          RoundingMode.HalfDown
                          RoundingMode.Up
                          RoundingMode.Down
                          RoundingMode.Ceiling
                          RoundingMode.Floor ] }

            let operand () =
                if rng.Next 2 = 0 then
                    sub ()
                else
                    familyCol (pick rng [ DecimalType; IntType; FloatType ])

            if rng.Next 2 = 0 then
                Quotient(operand (), operand (), rounding)
            else
                Rounded(operand (), rounding)

/// Two evaluation outcomes agree when both are the same error, or both are cells with one token
/// (`NaN` is not equal to itself structurally, and is one token).
let private sameOutcome (a: Result<Cell, EvalError>) (b: Result<Cell, EvalError>) : bool =
    match a, b with
    | Ok x, Ok y -> DataFrame.cellToken x = DataFrame.cellToken y
    | Error e1, Error e2 -> e1 = e2
    | _ -> false

let private oneRowTable (row: Cell[]) : Table =
    { Schema = typedSchema
      Columns = typedSchema |> List.mapi (fun i (name, ty) -> col name ty [ row[i] ]) }

/// The compiled evaluation of one (expression, row), through the internal entry the steps use:
/// the row as a one-row frame (Phase 267 — a conforming column unpacks typed and reaches the
/// vector path; a column holding a disagreeing cell stays boxed and reaches the cell path), the
/// expression compiled over it, and the tree run at its one physical row.
let private compiledOutcome (e: ColExpr) (row: Cell[]) : Result<Cell, EvalError> * DataFrame.Kernel list =
    let frame = Frame.ofTable (oneRowTable row)

    let compiled =
        DataFrame.compileExpr frame (DataFrame.resolveExpr typedEnv typedSchema e)

    DataFrame.runCompiled compiled 0, compiled.Kernels

let private referenceOutcome (e: ColExpr) (row: Cell[]) : Result<Cell, EvalError> =
    DataFrame.evalExprInRow typedEnv typedSchema (List.ofArray row) e

/// The kernel a binary node over two columns of a type compiles to, with conforming cells.
let private kernelCases: (DataFrame.Kernel * BinOp * ColumnType) list =
    [ DataFrame.IntArith, Add, IntType
      DataFrame.IntArith, Mod, IntType
      DataFrame.FloatArith, Mul, FloatType
      DataFrame.FloatArith, Div, IntType
      DataFrame.NumCompare, Lt, FloatType
      DataFrame.NumCompare, Ge, IntType
      DataFrame.OrdinalCompare, Gt, StringType
      DataFrame.OrdinalCompare, Eq, DateType
      DataFrame.OrdinalCompare, Le, TimestampType
      DataFrame.StrPredicate, Contains, StringType
      DataFrame.Logical, And, BoolType
      DataFrame.Logical, Or, BoolType ]

[<Tests>]
let compiledExprLaws =
    testList
        "DataFrame.CompiledExpr"
        [ testCase "the compiled form equals evalExprInRow cell for cell and error for error over generated triples"
          <| fun _ ->
              let rng = System.Random 266
              let kernelHits = System.Collections.Generic.Dictionary<DataFrame.Kernel, int>()
              let mutable present = 0
              let mutable nulls = 0
              let mutable errors = 0
              let mutable checkedTriples = 0

              for _ in 1..4000 do
                  let e = genExpr rng (rng.Next 5)
                  let row = typedRow rng
                  let expected = referenceOutcome e row
                  let actual, kernels = compiledOutcome e row

                  if not (sameOutcome expected actual) then
                      failtestf "compiled %A differs from the reference %A on expr=%A row=%A" actual expected e row

                  // The step loops are the compiled form's real callers: a one-row `Derive` must
                  // produce the same cell or the same error, and a one-row `Filter` must keep the
                  // row exactly when the reference says `Bool true`.
                  let derived =
                      DataFrame.evalPipelineInEnv typedEnv [ Derive("out", e) ] (oneRowTable row)
                      |> Result.map (fun t -> List.head (cellsOf "out" t))

                  // Phase 338: an expression whose own arms put a float beside a decimal is refused
                  // by name as a DERIVE on every frame, whatever this row's cell is.
                  if DataFrame.mixesFloatDecimal typedSchema e then
                      if derived <> Error(DataFrame.floatBesideDecimal "out") then
                          failtestf "Derive %A was not refused statically on expr=%A row=%A" derived e row
                  elif not (sameOutcome expected derived) then
                      failtestf "Derive %A differs from the reference %A on expr=%A row=%A" derived expected e row

                  let filtered =
                      DataFrame.evalPipelineInEnv typedEnv [ Filter e ] (oneRowTable row)
                      |> Result.map Table.rowCount

                  let expectedRows =
                      expected
                      |> Result.map (fun c ->
                          match c with
                          | Bool true -> 1
                          | _ -> 0)

                  if filtered <> expectedRows then
                      failtestf
                          "Filter kept %A rows, the reference says %A, on expr=%A row=%A"
                          filtered
                          expectedRows
                          e
                          row

                  checkedTriples <- checkedTriples + 1

                  for k in kernels do
                      kernelHits[k] <-
                          (match kernelHits.TryGetValue k with
                           | true, n -> n + 1
                           | _ -> 1)

                  match expected with
                  | Ok Null -> nulls <- nulls + 1
                  | Ok _ -> present <- present + 1
                  | Error _ -> errors <- errors + 1

              // Adequacy: the boxed fall-back and every typed kernel compiled into the sample, and the
              // three outcome classes all occurred — a law over a sample missing any of them is a
              // green that proves less than it claims.
              let hits (k: DataFrame.Kernel) =
                  match kernelHits.TryGetValue k with
                  | true, n -> n
                  | _ -> 0

              for k in
                  [ DataFrame.IntArith
                    DataFrame.FloatArith
                    DataFrame.NumCompare
                    DataFrame.OrdinalCompare
                    DataFrame.StrPredicate
                    DataFrame.Logical
                    DataFrame.Boxed ] do
                  Expect.isGreaterThan (hits k) 30 (sprintf "the sample reached kernel %A" k)

              Expect.isGreaterThan checkedTriples 3999 "every triple was checked"
              Expect.isGreaterThan present 400 "present cells occurred"
              Expect.isGreaterThan nulls 200 "nulls occurred"
              Expect.isGreaterThan errors 200 "errors occurred"

          testCase
              "each typed kernel's fast path, its null propagation and its mistyped-cell fall-back match the reference"
          <| fun _ ->
              let rng = System.Random 2660

              for kernel, op, ty in kernelCases do
                  let cols = colsOfType ty
                  let a, b = List.head cols, List.item 1 cols
                  let ai = typedSchema |> List.findIndex (fun (n, _) -> n = a)
                  let bi = typedSchema |> List.findIndex (fun (n, _) -> n = b)
                  let e = Binary(op, Col a, Col b)

                  let check (label: string) (row: Cell[]) (classify: Result<Cell, EvalError> -> bool) =
                      let expected = referenceOutcome e row
                      let actual, kernels = compiledOutcome e row
                      Expect.equal kernels [ kernel ] (sprintf "%A over %s compiles to its kernel" op label)

                      if not (sameOutcome expected actual) then
                          failtestf "%A %s: compiled %A, reference %A, row %A" op label actual expected row

                      Expect.isTrue (classify expected) (sprintf "%A %s reached the outcome it targets" op label)

                  for _ in 1..40 do
                      // conforming cells on both sides: the fast path, answering a present cell
                      let row = typedRow rng
                      row[ai] <- conformingCell rng ty
                      row[bi] <- conformingCell rng ty

                      check "fast path" row (fun r ->
                          match r with
                          | Ok Null -> op = Div || op = Mod // a zero divisor answers null
                          | Ok _ -> true
                          | Error _ -> ty = IntType && (op = Add || op = Mul)) // int32 overflow

                      // a null operand: null propagates (the logical pair three-valued)
                      let row = typedRow rng
                      row[ai] <- Null
                      row[bi] <- conformingCell rng ty
                      check "null operand" row (fun _ -> true)

                      // a cell disagreeing with its column: the kernel hands the pair to the arm
                      let row = typedRow rng
                      row[ai] <- Str "not what the column declares"
                      row[bi] <- conformingCell rng ty
                      check "mistyped cell" row (fun _ -> true)

          testCase
              "the first error in row order is the one a Derive reports, and the first in a row the one a row reports"
          <| fun _ ->
              let t =
                  tbl
                      [ "x", IntType; "s", StringType ]
                      [ col "x" IntType [ Int 1; Int System.Int32.MaxValue; Null; Int 3 ]
                        col "s" StringType [ Str "a"; Str "b"; Str "c"; Str "d" ] ]

              // row 2 overflows; row 4 would too, and row 3 is null: row 2's error is the answer.
              match DataFrame.evalPipeline [ Derive("y", Binary(Add, Col "x", Lit(Int 1))) ] t with
              | Error(OverflowError d) -> Expect.stringContains d "add overflowed" "row 2's overflow"
              | other -> failtestf "expected the overflow, got %A" other

              // Left operand first: the arithmetic type error, not the cast's, though both fail.
              let e = Binary(Add, Binary(Add, Col "s", Lit(Int 1)), Cast(IntType, Lit(Str "zz")))

              match DataFrame.evalPipeline [ Derive("y", e) ] t with
              | Error(TypeError d) -> Expect.equal d "arithmetic on a non-numeric operand" "the left error"
              | other -> failtestf "expected the left operand's error, got %A" other

              // A filter stops at the erroring row as well, whatever rows follow.
              match DataFrame.evalPipeline [ Filter(Binary(Gt, Col "x", Lit(Str "1"))) ] t with
              | Error(TypeError _) -> ()
              | other -> failtestf "expected a comparison error, got %A" other

          testCase "a compiled tree is reusable across rows: an error on one row leaves the next row's answer intact"
          <| fun _ ->
              let e = Binary(Add, Col "i", Lit(Int 1))
              let bad = typedRow (System.Random 1)
              bad[0] <- Str "x"
              let good = typedRow (System.Random 2)
              good[0] <- Int 41

              // One frame holding both rows (Phase 267): the mistyped cell keeps column `i` boxed,
              // so the tree takes the cell path at both physical rows and the slot must reset
              // between them.
              let frame =
                  Frame.ofTable
                      { Schema = typedSchema
                        Columns = typedSchema |> List.mapi (fun i (name, ty) -> col name ty [ bad[i]; good[i] ]) }

              let compiled =
                  DataFrame.compileExpr frame (DataFrame.resolveExpr typedEnv typedSchema e)

              Expect.isError (DataFrame.runCompiled compiled 0) "the bad row errors"
              Expect.equal (DataFrame.runCompiled compiled 1) (Ok(Int 42)) "the good row answers"
              Expect.isError (DataFrame.runCompiled compiled 0) "and errors again"

          testCase "a 129-node integer expression compiles to a chain of integer kernels and equals the reference"
          <| fun _ ->
              // Sixteen levels of alternating addition and subtraction over two int columns — the
              // deep shape the scaling suite times — over rows with nulls and one mistyped cell.
              let rec nest n e =
                  if n = 0 then
                      e
                  else
                      nest
                          (n - 1)
                          (Binary(
                              Sub,
                              Binary(Add, e, Binary(Add, Col "j", Lit(Int 2))),
                              Binary(Add, Col "j", Lit(Int 1))
                          ))

              let e = nest 16 (Col "i")
              let rng = System.Random 129

              for _ in 1..200 do
                  let row = typedRow rng
                  let expected = referenceOutcome e row
                  let actual, kernels = compiledOutcome e row
                  Expect.equal (List.length kernels) 64 "sixty-four binary nodes"
                  Expect.isTrue (kernels |> List.forall ((=) DataFrame.IntArith)) "every node is the integer kernel"

                  if not (sameOutcome expected actual) then
                      failtestf "compiled %A differs from the reference %A on row %A" actual expected row

          testCase "a Derive and a Filter over many rows run as loops: 50,000 rows evaluate"
          <| fun _ ->
              let n = 50_000

              let t = tbl [ "x", IntType ] [ col "x" IntType [ for k in 1..n -> Int k ] ]

              let out =
                  DataFrame.evalPipeline
                      [ Derive("y", Binary(Mul, Col "x", Lit(Int 2)))
                        Filter(Binary(Gt, Col "y", Lit(Int n))) ]
                      t
                  |> okTable

              Expect.equal (Table.rowCount out) (n / 2) "half the rows pass" ]

// ---- Phase 266: the typer ----------------------------------------------------------------------

[<Tests>]
let exprTypingTests =
    testList
        "DataFrame.Typing"
        [ testCase "the typer decides each family from the schema and declines what the schema does not decide"
          <| fun _ ->
              let cases: (string * ColExpr * ColumnType option) list =
                  [ "int + int", Binary(Add, Col "i", Col "j"), Some IntType
                    "int + float", Binary(Add, Col "i", Col "f"), Some FloatType
                    "int / int", Binary(Div, Col "i", Col "j"), Some FloatType
                    "float mod", Binary(Mod, Col "f", Col "j"), None
                    "str + int", Binary(Add, Col "s", Col "i"), None
                    "int < float", Binary(Lt, Col "i", Col "f"), Some BoolType
                    "str = str", Binary(Eq, Col "s", Col "t"), Some BoolType
                    "str = date", Binary(Eq, Col "s", Col "d"), None
                    "contains", Binary(Contains, Col "s", Lit(Str "a")), Some BoolType
                    "and", Binary(And, Col "b", Col "c"), Some BoolType
                    "and null literal", Binary(And, Col "b", Lit Null), Some BoolType
                    "not bool", Not(Col "b"), Some BoolType
                    "not int", Not(Col "i"), None
                    "null literal", Lit Null, None
                    "null + int", Binary(Add, Lit Null, Col "i"), None
                    "param", Param "p", None
                    "now", Now NowGrain.Date, None
                    "unknown column", Col "nope", None
                    "coalesce same", Coalesce [ Col "i"; Lit(Int 0) ], Some IntType
                    "coalesce mixed", Coalesce [ Col "i"; Col "f" ], None
                    "case null else", Case([ Col "b", Col "s" ], Lit Null), Some StringType
                    "case mixed arms", Case([ Col "b", Col "s" ], Col "i"), None
                    "cast from string", Cast(IntType, Col "s"), Some IntType
                    "in list", InList(Col "i", [ Lit(Int 1) ]), Some BoolType
                    "is null", IsNull(Col "nope"), Some BoolType
                    "concat", ApplyFn(Concat, [ Col "s"; Col "i" ]), Some StringType
                    "length", ApplyFn(Length, [ Col "s" ]), Some IntType
                    "abs int", ApplyFn(Abs, [ Col "i" ]), Some IntType
                    "abs float", ApplyFn(Abs, [ Col "f" ]), Some FloatType
                    "abs str", ApplyFn(Abs, [ Col "s" ]), None
                    "round", ApplyFn(Round, [ Col "i" ]), Some FloatType
                    "least mixed", ApplyFn(Least, [ Col "i"; Col "f" ]), None
                    "greatest ints", ApplyFn(Greatest, [ Col "i"; Col "j" ]), Some IntType
                    "wrong arity", ApplyFn(Length, [ Col "s"; Col "t" ]), None
                    "date diff", ApplyFn(DateDiffDays, [ Col "d"; Col "e" ]), Some IntType ]

              for label, e, expected in cases do
                  Expect.equal (DataFrame.typeOf typedSchema e) expected label

          testCase "a Derive's column type is the typer's wherever it decides (Phase 338)"
          <| fun _ ->
              let cases: (string * ColExpr * ColumnType option) list =
                  [ "strings", ApplyFn(Concat, [ Col "s"; Lit(Str "!") ]), Some StringType
                    "null literal", Lit Null, Some StringType
                    "a name the schema lacks", Col "nope", None
                    "ints", Binary(Add, Col "i", Lit(Int 1)), Some IntType
                    "bools", Binary(Gt, Col "i", Lit(Int 1)), Some BoolType
                    "cast to string", Cast(StringType, Col "i"), Some StringType
                    "cast to int", Cast(IntType, Col "s"), Some IntType
                    "an int beside a float, which the exact typer keeps apart",
                    Case([ Binary(Gt, Col "i", Lit(Int 1)), Lit(Int 1) ], Lit(Float 2.5)),
                    None
                    "param", Param "p", None ]

              for label, e, expected in cases do
                  Expect.equal (DataFrame.derivedColumnType typedSchema e) expected label

          testCase
              "SchemaWalk types every decided Derive, keeps None where the cells decide, and agrees with evaluation on empty and full frames"
          <| fun _ ->
              let walk (pipeline: Transform list) =
                  SchemaWalk.ofPipeline people.Schema pipeline

              Expect.equal
                  (SchemaWalk.typeOf "tag" (walk [ Derive("tag", ApplyFn(Upper, [ Col "dept" ])) ]))
                  (Some StringType)
                  "an upper-cased string column is typed"

              Expect.equal
                  (SchemaWalk.typeOf "raise" (walk [ Derive("raise", Binary(Add, Col "salary", Lit(Int 10))) ]))
                  (Some IntType)
                  "an integer derive is an int on every frame, an empty one included (Phase 338)"

              Expect.equal
                  (SchemaWalk.typeOf "salary" (walk [ Derive("salary", Cast(StringType, Col "salary")) ]))
                  (Some StringType)
                  "a retype in place through a string cast is typed"

              Expect.equal
                  (SchemaWalk.typeOf "n" (walk [ Derive("n", Lit Null) ]))
                  (Some StringType)
                  "an all-null derive is String, as the evaluator's fall-back makes it"

              // Over an OPEN knowledge the walk cannot see `dept`, so a derive OF it is undecided —
              // while an `Upper` of it is still a string on every row it answers at all, whatever
              // `dept` turns out to be: the typing is a claim about present values, never about
              // which rows error.
              let openKnowledge = SchemaKnowledge.AtLeast([], "declared nothing")

              let overOpen (expr: ColExpr) =
                  SchemaWalk.ofTransform SchemaWalk.noSources openKnowledge (Derive("u", expr))
                  |> SchemaWalk.typeOf "u"

              Expect.equal (overOpen (Col "dept")) None "a column the walk cannot see leaves the derive undecided"

              Expect.equal
                  (overOpen (ApplyFn(Upper, [ Col "dept" ])))
                  (Some StringType)
                  "a string function of it is a string whenever it is a value"

              // The claim holds where it is made: an emptied frame and a full one give the walk's type.
              for e in [ ApplyFn(Upper, [ Col "dept" ]); Binary(Add, Col "salary", Lit(Int 10)) ] do
                  for pipeline in [ [ Filter(Lit(Bool false)); Derive("tag", e) ]; [ Derive("tag", e) ] ] do
                      let evaluated = run pipeline |> okTable
                      let actual = evaluated.Schema |> List.find (fun (n, _) -> n = "tag") |> snd

                      Expect.equal (SchemaWalk.typeOf "tag" (walk pipeline)) (Some actual) "walk and evaluator agree" ]

// ---------------------------------------------------------------------------
//  Phase 267 — the dense columnar frame and the prepared source.
//
//  The cells every verb produces are held to the row form's by the transform
//  law vectors (byte-identical across the phase) and by every family above;
//  what is held HERE is what the representation adds: the well-formedness
//  invariant after every step of every generated pipeline, the `Table`
//  boundary's padding rule, and the one new public surface — a source prepared
//  once answers every pipeline as the reference does, and is not consumed by
//  answering.
// ---------------------------------------------------------------------------

/// A table's answer as tokens, for equality: `Float nan` is not structurally equal to itself, and
/// is one token (`sameOutcome` above makes the same choice for one cell).
let private tokenised (r: Result<Table, EvalError>) : Result<(string * string * string list) list, EvalError> =
    r
    |> Result.map (fun t ->
        t.Columns
        |> List.map (fun c -> c.Name, ColumnType.tag c.Type, c.Cells |> List.map DataFrame.cellToken))

/// A table of `n` rows over `typedSchema`, drawn from `typedRow`; with `mistype` set, one integer
/// cell is a string, so the column unpacks BOXED and the pipeline exercises the cell path.
let private frameTable (rng: System.Random) (n: int) (mistype: bool) : Table =
    let rows = Array.init n (fun _ -> typedRow rng)

    if mistype && n > 0 then
        rows[rng.Next n][0] <- Str "mistyped"

    { Schema = typedSchema
      Columns =
        typedSchema
        |> List.mapi (fun ci (name, ty) -> col name ty [ for r in rows -> r[ci] ]) }

/// A right-hand table for the two-table verbs: the same schema, its own rows.
let private frameOther (rng: System.Random) : Table = frameTable rng (rng.Next 5) false

/// One drawn step, reaching every verb of the algebra.
let private genStep (rng: System.Random) : Transform =
    let name () = pick rng (typedSchema |> List.map fst)
    let intCol () = pick rng (colsOfType IntType)

    match rng.Next 16 with
    | 0 -> Filter(genExpr rng 2)
    | 1 ->
        Derive(
            (if rng.Next 2 = 0 then
                 name ()
             else
                 "d" + string (rng.Next 3)),
            genExpr rng 2
        )
    | 2 -> Project([ for _ in 0 .. rng.Next 4 -> let n = name () in n, (if rng.Next 3 = 0 then n + "_p" else n) ])
    | 3 ->
        GroupBy(
            [ name () ],
            [ { Name = "agg"
                Fn = pick rng [ Sum; Count; Min; Max; Mean; First; CountDistinct ]
                Of = name () } ]
        )
    | 4 -> Sort([ for _ in 0 .. rng.Next 2 -> Slot.Lit(name ()), pick rng [ Asc; Desc ] ])
    | 5 -> Distinct
    | 6 -> Limit(Slot.Lit(rng.Next 6), Slot.Lit(rng.Next 3))
    | 7 ->
        Window
            { Fn =
                pick
                    rng
                    [ RowNumber
                      Rank
                      DenseRank
                      CompetitionRank
                      NTile 2
                      Lag
                      Lead
                      CumulSum
                      CumulMax
                      CumulMin
                      RollingMean
                      RollingSum ]
              Of = name ()
              PartitionBy = [ for _ in 0 .. rng.Next 2 -> name () ]
              OrderBy = [ name (), Asc ]
              As = (if rng.Next 3 = 0 then name () else "w") }
    | 8 ->
        Pivot
            { Index = [ name () ]
              On = name ()
              Values = intCol ()
              Agg = pick rng [ Sum; Count; Max ] }
    | 9 -> Unpivot([ name () ], [ for _ in 0 .. rng.Next 2 -> name () ])
    | 10 -> Join(Embedded(frameOther rng), [ name (), name () ], pick rng [ Inner; Left; Right; Outer; Semi; Anti ])
    | 11 -> Union(Embedded(frameOther rng))
    | 12 -> Intersect(Embedded(frameOther rng))
    | 13 -> Except(Embedded(frameOther rng))
    | _ -> Filter(Binary(Gt, Col(intCol ()), Lit(Int(rng.Next 10 - 5))))

let private genPipeline (rng: System.Random) : Transform list =
    [ for _ in 0 .. rng.Next 4 -> genStep rng ]

/// The generated sample: (table, pipeline) pairs, a third of the tables carrying a mistyped cell.
let private frameSample (seed: int) (count: int) : (Table * Transform list) list =
    let rng = System.Random seed
    [ for i in 1..count -> frameTable rng (rng.Next 7) (i % 3 = 0), genPipeline rng ]

[<Tests>]
let frameTests =
    testList
        "Frame"
        [ // Phase 326: the JavaScript host indexes the frame's arrays through `Raw` — unchecked only
          // where a loop proves the index, checked everywhere else with the .NET runtime's own
          // refusal. This pins the refusals a malformed frame meets on THIS host, and that the
          // shared messages are the runtime's; the node leg's parity probe holds the JavaScript host
          // to the same lines byte for byte (the Phase 326 results file).
          testCase "a malformed frame is refused alike on both hosts, with the runtime's messages"
          <| fun _ ->
              let message (f: unit -> unit) : string =
                  try
                      f ()
                      "answered"
                  with e ->
                      e.Message

              let indexMessage = message (fun () -> ([||]: int[])[0] |> ignore)

              let spanMessage =
                  message (fun () -> System.ReadOnlySpan<bool>([||], 0, 1).Length |> ignore)

              Expect.equal
                  (message (fun () -> Raw.outOfRange () |> ignore))
                  indexMessage
                  "the index refusal is the runtime's"

              Expect.equal
                  (message (fun () -> Raw.outOfSpan () |> ignore))
                  spanMessage
                  "the span refusal is the runtime's"

              let t =
                  tbl
                      [ "k", StringType; "v", IntType; "f", FloatType ]
                      [ col "k" StringType [ Str "a"; Str "b"; Str "a" ]
                        col "v" IntType [ Int 1; Int 2; Int 3 ]
                        col "f" FloatType [ Float 0.5; Float 1.5; Float 2.5 ] ]

              let good = Frame.ofTable t
              // A selection naming a row past the vectors; every vector a row short of the frame.
              let badSel = Frame.select good [| 0; 7; 1 |]

              let shortVecs =
                  { good with
                      Vecs = good.Vecs |> Array.map (fun v -> Vec.slice v 0 2) }

              Expect.isFalse (Frame.wellFormed badSel) "the selection is malformed"
              Expect.isFalse (Frame.wellFormed shortVecs) "the vectors are malformed"

              let step (frame: Frame) (s: Transform) () =
                  match DataFrame.evalStep DataFrame.noResolve Map.empty frame s with
                  | Ok f -> Frame.toTable f |> ignore
                  | Error e -> failwith (DataFrame.errorString e)

              let reading =
                  [ "derive", Derive("w", Binary(Add, Col "v", Lit(Int 1)))
                    "sort", Transform.sortBy [ "v", Desc ]
                    "groupBy", GroupBy([ "k" ], [ { Name = "s"; Fn = Sum; Of = "v" } ])
                    "distinct", Distinct
                    "join", Join(Embedded t, [ "v", "v" ], Inner)
                    "window",
                    Window
                        { PartitionBy = [ "k" ]
                          OrderBy = [ "v", Asc ]
                          Fn = CumulSum
                          Of = "v"
                          As = "cs" }
                    "pivot",
                    Pivot
                        { Index = [ "k" ]
                          On = "v"
                          Values = "f"
                          Agg = Sum } ]

              Expect.equal
                  (message (fun () -> Frame.toTable badSel |> ignore))
                  indexMessage
                  "toTable over the selection"

              Expect.equal (message (step badSel (Transform.limit 2 0))) indexMessage "limit over the selection"

              for name, s in reading do
                  Expect.equal (message (step badSel s)) indexMessage (name + " over the selection")
                  Expect.equal (message (step shortVecs s)) indexMessage (name + " over the short vectors")

              // The filter kernel takes a span over the mask before its loop, so a short mask is
              // refused as a span is.
              Expect.equal
                  (message (step shortVecs (Filter(Binary(Gt, Col "v", Lit(Int 1))))))
                  spanMessage
                  "filter over the short vectors"

              // Phase 333: a filter over a selection naming a row past the vectors is refused BY NAME,
              // on the comparison kernels' path (a typed comparison with a constant) and on the
              // compiled one alike. The kernels read the entry as an unset bit and DROPPED it: the
              // filter answered the two rows it could see. The message is plain concatenation, so
              // node prints the same line (the Phase 333 results file).
              for path, predicate in
                  [ "kernel", Binary(Gt, Col "v", Lit(Int 0))
                    "compiled", Binary(Ge, Col "k", Lit(Str "a")) ] do
                  match DataFrame.evalStep DataFrame.noResolve Map.empty badSel (Filter predicate) with
                  | Error e ->
                      Expect.equal
                          (DataFrame.errorString e)
                          "type error: the frame's selection names row 7, past its 3-row vectors"
                          (path + ": a filter over the selection is refused by name")
                  | Ok f ->
                      failtestf "%s: a filter over a selection past the vectors answered %A" path (Frame.toTable f)

          // Phase 326: the JavaScript host's slot table stands where `Dictionary` stands on .NET for
          // the token slots, the row hasher and the order codes. Held here to `Dictionary` over keys
          // whose hash collides on purpose (probing and growth both exercised), and to the caller's
          // numbering: slot `s` is the `s`-th new key met, whatever the hashes — the first-seen
          // order groups are emitted in.
          testCase "the open slot table holds what a Dictionary holds, numbered in first-seen order"
          <| fun _ ->
              let rng = System.Random 326
              let table = DataFrame.OpenSlots<int>((fun k -> k % 7), (fun a b -> a = b))
              let reference = System.Collections.Generic.Dictionary<int, int>()
              let met = ResizeArray<int>()

              for _ in 1..5000 do
                  let k = rng.Next(-2000, 2000)
                  let found = table.Find k

                  match reference.TryGetValue k with
                  | true, s -> Expect.equal found s "a held key finds its slot"
                  | _ ->
                      Expect.equal found -1 "an absent key finds nothing"
                      let s = reference.Count
                      reference[k] <- s
                      table.Add(k, s)
                      met.Add k

              Expect.equal table.Count reference.Count "as many keys as the dictionary"

              for s in 0 .. met.Count - 1 do
                  Expect.equal (table.Find met[s]) s "slot s is the s-th key met"

              // Rows of cells under the token relation, as the JavaScript `CellKey.slots` keys them.
              let rows =
                  DataFrame.OpenSlots<Cell[]>(
                      (fun cells -> DataFrame.CellKey.row.GetHashCode cells),
                      (fun a b -> DataFrame.CellKey.row.Equals(a, b))
                  )

              let keys =
                  [ [| Int 1; Str "a" |]
                    [| Float 1.0; Str "a" |]
                    [| Float -0.0; Null |]
                    [| Float nan; Null |] ]

              keys |> List.iteri (fun s k -> rows.Add(k, s))
              Expect.equal (rows.Find [| Int 1; Str "a" |]) 0 "an int key is its own"
              Expect.equal (rows.Find [| Float 1.0; Str "a" |]) 1 "a float key is not the int's"
              Expect.equal (rows.Find [| Float 0.0; Null |]) 2 "-0.0 and 0.0 are one token"
              Expect.equal (rows.Find [| Float nan; Null |]) 3 "every NaN is one token"
              Expect.equal (rows.Find [| Int 2; Str "a" |]) -1 "an unmet row finds nothing"

          testCase "every step of every generated pipeline leaves the frame well-formed, and the fold is the reference"
          <| fun _ ->
              // Every vector the frame's row count, every selection entry inside it — after each
              // step, whichever family the step belongs to, over frames a `Filter` emptied, a
              // `Sort` permuted and a `Derive` widened as much as over fresh ones.
              let mutable steps = 0
              let mutable emptied = 0

              for table, pipeline in frameSample 267 400 do
                  let mutable frame = Frame.ofTable table
                  Expect.isTrue (Frame.wellFormed frame) "the unpacked table is well-formed"
                  let mutable failed = false

                  for step in pipeline do
                      if not failed then
                          match DataFrame.evalStep DataFrame.noResolve Map.empty frame step with
                          | Ok next ->
                              steps <- steps + 1

                              if Frame.rows next = 0 then
                                  emptied <- emptied + 1

                              Expect.isTrue (Frame.wellFormed next) (sprintf "well-formed after %A" step)
                              frame <- next
                          | Error _ -> failed <- true

                  if not failed then
                      Expect.equal
                          (tokenised (Ok(Frame.toTable frame)))
                          (tokenised (DataFrame.evalPipeline pipeline table))
                          "the stepwise fold, packed back, is the reference answer"

              // Adequacy: the sample reached the invariant often, and on emptied frames.
              Expect.isGreaterThan steps 200 "the sample took steps"
              Expect.isGreaterThan emptied 10 "the sample reached emptied frames"

          testCase
              "a source prepared once answers every pipeline as the reference does, and answering does not consume it"
          <| fun _ ->
              // The one new public surface: `prepare` pays the boundary, `evalPrepared` evaluates
              // over it. Three pipelines in turn over ONE prepared source, the first of them
              // twice, so a verb that wrote into a shared vector — a `Derive` upserting in place, a
              // `Sort` permuting — would be caught by the second answer disagreeing with the first.
              let rng = System.Random 2670

              for _ in 1..120 do
                  let table = frameTable rng (rng.Next 7) (rng.Next 3 = 0)
                  let prepared = DataFrame.prepare table
                  let pipelines = [ genPipeline rng; genPipeline rng; genPipeline rng ]

                  let answers =
                      [ for p in pipelines @ [ List.head pipelines ] ->
                            p, DataFrame.evalPrepared DataFrame.noResolve Map.empty p prepared ]

                  for p, answer in answers do
                      Expect.equal
                          (tokenised answer)
                          (tokenised (DataFrame.evalPipelineWithInEnv DataFrame.noResolve Map.empty p table))
                          "evalPrepared = the reference"

                  Expect.equal
                      (tokenised (snd (List.last answers)))
                      (tokenised (snd (List.head answers)))
                      "the first pipeline answers the same twice"

          testCase
              "the boundary pads a short column and an absent one with Null and cuts a long one, as the row form did"
          <| fun _ ->
              // `Column.cell` was total and answered `Null` past the end; the frame's unpack is the
              // same rule paid once per column.
              let t: Table =
                  { Schema = [ "a", IntType; "b", IntType; "c", StringType ]
                    Columns =
                      [ col "a" IntType [ Int 1; Int 2; Int 3 ]
                        col "b" IntType [ Int 9 ]
                        col "zzz" IntType [ Int 4; Int 5; Int 6; Int 7 ] ] }

              let expected: Table =
                  { Schema = t.Schema
                    Columns =
                      [ col "a" IntType [ Int 1; Int 2; Int 3 ]
                        col "b" IntType [ Int 9; Null; Null ]
                        col "c" StringType [ Null; Null; Null ] ] }

              Expect.equal (Frame.toTable (Frame.ofTable t)) expected "padded, cut and absent-as-null"
              Expect.equal (DataFrame.evalPipeline [] t) (Ok expected) "and the empty pipeline says the same"

          // ---- Phase 327: the boundary packed from the list, its laws over every column type, and interned cells ----

          testCase
              "the boundary packs a fitting column straight from its list into the vector pack answers, and a ragged or out-of-type one falls back by name"
          <| fun _ ->
              // `Vec.packList` must answer exactly the vector `Vec.pack` answers over the same cells
              // where it answers at all, and `ValueNone` - sending `Frame.ofTable` to
              // `Frame.unpackFallback` - in every other case. The sample is guarded: every column
              // type reaches the direct path, and the ragged, out-of-type and null-carrier cases
              // all reach the fall-back.
              let rng = System.Random 3270
              let mutable direct = Set.empty
              let mutable ragged = 0
              let mutable outOfType = 0

              for _ in 1..600 do
                  let ty = pick rng allTypes
                  let n = rng.Next 9

                  let cells =
                      [ for _ in 1..n ->
                            match rng.Next 6 with
                            | 0 -> Null
                            | _ -> conformingCell rng ty ]

                  match rng.Next 4 with
                  | 0 ->
                      // Ragged: the list one cell short of the row count, or one long.
                      let declared = if rng.Next 2 = 0 then n + 1 else max 0 (n - 1)

                      if declared <> n then
                          Expect.equal (Vec.packList ty declared cells) ValueNone "a ragged column falls back"
                          ragged <- ragged + 1
                  | 1 when n > 0 ->
                      // One cell out of the column's type (a `Str` in an int column, else an `Int`).
                      let wrong = if ty = IntType then Str "mistyped" else Int 1
                      let k = rng.Next n
                      let mistyped = cells |> List.mapi (fun i c -> if i = k then wrong else c)
                      Expect.equal (Vec.packList ty n mistyped) ValueNone "an out-of-type cell falls back"

                      Expect.equal (Vec.declaredType (Vec.pack ty (List.toArray mistyped))) None "and pack boxes it too"

                      outOfType <- outOfType + 1
                  | _ ->
                      match Vec.packList ty n cells with
                      | ValueSome v ->
                          let expected = Vec.pack ty (List.toArray cells)
                          direct <- Set.add (ColumnType.tag ty) direct
                          Expect.equal (Vec.declaredType v) (Vec.declaredType expected) "the same vector kind"
                          Expect.equal (Vec.length v) n "the column's length"

                          Expect.equal
                              [ for p in 0 .. n - 1 -> DataFrame.cellToken (Vec.cellAt v p) ]
                              [ for p in 0 .. n - 1 -> DataFrame.cellToken (Vec.cellAt expected p) ]
                              "every cell reads back as pack's does"

                          match v, expected with
                          | Decs(a, s, _, m), Decs(a', s', _, m') ->
                              Expect.equal (s, a, m) (s', a', m') "the same scale and scaled integers"
                          | Decs _, _
                          | _, Decs _ -> failtest "one path packed a decimal vector and the other did not"
                          | _ -> ()
                      | ValueNone -> failtestf "a fitting %A column fell back: %A" ty cells

              // A string-family cell carrying a null string is not the typed carrier's to hold.
              Expect.equal (Vec.packList StringType 1 [ Str null ]) ValueNone "a null carrier falls back"

              Expect.equal direct (allTypes |> List.map ColumnType.tag |> Set.ofList) "every column type packs directly"

              Expect.isGreaterThan ragged 50 "the sample reached ragged columns"
              Expect.isGreaterThan outOfType 50 "the sample reached out-of-type cells"

          testCase
              "the boundary laws hold over every column type and a ragged column: Table in, Table out and the round trip"
          <| fun _ ->
              // Table in: every column unpacks to the padded (or cut) cells, typed exactly where
              // every present cell is of its declared type, and keeps its own list as the origin
              // exactly where the list is the table's length. Table out: a frame's table, read back
              // through the vectors with no origin to hand back, is the padded table, through a
              // selection too. Round trip: `toTable (ofTable t)` is the padded table. The sample is
              // guarded: every column type unpacks typed, and ragged and boxed columns both occur.
              let rng = System.Random 3271
              let mutable typed = Set.empty
              let mutable ragged = 0
              let mutable boxed = 0
              let mutable keptOrigin = 0

              for i in 1..300 do
                  let n = rng.Next 7
                  let t = frameTable rng n (i % 3 = 0)

                  // Every fourth table has one column cut short, and every fifth one column too long.
                  let t =
                      if n > 0 && (i % 4 = 0 || i % 5 = 0) then
                          let k = rng.Next t.Columns.Length

                          { t with
                              Columns =
                                  t.Columns
                                  |> List.mapi (fun ci c ->
                                      if ci <> k then
                                          c
                                      elif i % 4 = 0 then
                                          Column.create c.Name c.Type (List.truncate (n - 1) c.Cells)
                                      else
                                          Column.create c.Name c.Type (c.Cells @ [ List.head c.Cells ])) }
                      else
                          t

                  let n = Table.rowCount t
                  let frame = Frame.ofTable t
                  Expect.isTrue (Frame.wellFormed frame) "the unpacked table is well-formed"

                  let paddedCells (name: string) : Cell list =
                      let cells =
                          match Table.tryColumn name t with
                          | Some c -> c.Cells
                          | None -> []

                      [ for p in 0 .. n - 1 -> List.tryItem p cells |> Option.defaultValue Null ]

                  t.Schema
                  |> List.iteri (fun ci (name, ty) ->
                      let expected = paddedCells name
                      let v = frame.Vecs[ci]

                      Expect.equal
                          [ for p in 0 .. n - 1 -> DataFrame.cellToken (Vec.cellAt v p) ]
                          (expected |> List.map DataFrame.cellToken)
                          "Table in: the padded cells"

                      let fits =
                          expected
                          |> List.forall (fun c ->
                              match c, ty with
                              | Null, _
                              | Int _, IntType
                              | Float _, FloatType
                              | Bool _, BoolType
                              | Str _, StringType
                              | Date _, DateType
                              | Timestamp _, TimestampType
                              | Decimal _, DecimalType -> true
                              | _ -> false)

                      Expect.equal
                          (Vec.declaredType v)
                          (if fits then Some ty else None)
                          "Table in: typed exactly where it fits"

                      if fits then
                          typed <- Set.add (ColumnType.tag ty) typed
                      else
                          boxed <- boxed + 1

                      match Table.tryColumn name t with
                      | Some c when List.length c.Cells = n ->
                          Expect.isTrue
                              (match frame.Origins[ci] with
                               | Some o -> obj.ReferenceEquals(o, c.Cells)
                               | None -> false)
                              "Table in: a column of the table's length keeps its own list"

                          keptOrigin <- keptOrigin + 1
                      | Some _ ->
                          Expect.isNone frame.Origins[ci] "Table in: a ragged column keeps no origin"
                          ragged <- ragged + 1
                      | None -> Expect.isNone frame.Origins[ci] "Table in: an absent column keeps no origin")

                  let padded: Table =
                      { Schema = t.Schema
                        Columns =
                          t.Schema
                          |> List.map (fun (name, ty) -> Column.create name ty (paddedCells name)) }

                  Expect.equal (tokenised (Ok(Frame.toTable frame))) (tokenised (Ok padded)) "round trip"

                  let unOriginated =
                      { frame with
                          Origins = Array.create frame.Vecs.Length None }

                  Expect.equal (tokenised (Ok(Frame.toTable unOriginated))) (tokenised (Ok padded)) "Table out"

                  let reversed = Frame.select frame (Array.init n (fun p -> n - 1 - p))

                  let paddedReversed =
                      { padded with
                          Columns =
                              padded.Columns
                              |> List.map (fun c -> Column.create c.Name c.Type (List.rev c.Cells)) }

                  Expect.equal
                      (tokenised (Ok(Frame.toTable reversed)))
                      (tokenised (Ok paddedReversed))
                      "Table out through a selection"

              Expect.equal typed (allTypes |> List.map ColumnType.tag |> Set.ofList) "every column type unpacks typed"

              Expect.isGreaterThan ragged 50 "the sample reached ragged columns"
              Expect.isGreaterThan boxed 50 "the sample reached boxed columns"
              Expect.isGreaterThan keptOrigin 500 "the sample kept origins"

          testCase
              "an interned cell is the fresh cell under every comparison the evaluator makes, and only equal cells are shared"
          <| fun _ ->
              // `cellAt` hands back one shared `Bool` per value and one shared `Int` per value in
              // `[Lo, Hi]`. Interning is sound only if nothing tells the shared cell from a fresh
              // one: structural equality, `Cell.compare`, the hash, `cellToken` and the grouping
              // comparer `CellKey` all agree, over the range, its edges and past them.
              let ints =
                  [ InternedCells.Lo - 3 .. InternedCells.Hi + 3 ]
                  @ [ System.Int32.MinValue; System.Int32.MaxValue ]

              let read (v: Vec) = Vec.cellAt v 0
              let mutable shared = 0

              let same (a: Cell) (fresh: Cell) =
                  Expect.equal a fresh "structurally equal"
                  Expect.equal (Cell.compare a fresh) (Some 0) "Cell.compare"
                  Expect.equal (hash a) (hash fresh) "hash"
                  Expect.equal (DataFrame.cellToken a) (DataFrame.cellToken fresh) "cellToken"
                  Expect.isTrue (DataFrame.CellKey.equals a fresh) "CellKey.equals"
                  Expect.equal (DataFrame.CellKey.hashCell a) (DataFrame.CellKey.hashCell fresh) "CellKey.hashCell"

              for v in ints do
                  let a = read (Ints([| v |], [| true |]))
                  let b = read (Ints([| v |], [| true |]))
                  same a (Int v)

                  let inRange = v >= InternedCells.Lo && v <= InternedCells.Hi

                  Expect.equal (obj.ReferenceEquals(a, b)) inRange (sprintf "%d is shared exactly inside the range" v)

                  if inRange then
                      shared <- shared + 1

                  // A shared cell never equals a different value.
                  Expect.notEqual a (Int(v ^^^ 1)) "not equal to a neighbour"

              for b in [ true; false ] do
                  let a = read (Bools([| b |], [| true |]))
                  same a (Bool b)
                  Expect.isTrue (obj.ReferenceEquals(a, read (Bools([| b |], [| true |])))) "a bool is shared"
                  Expect.notEqual a (Bool(not b)) "not equal to the other bool"

              Expect.equal shared (InternedCells.Hi - InternedCells.Lo + 1) "the whole range is shared"
              Expect.equal (read (Ints([| 5 |], [| false |]))) Null "an absent row reads Null"

              // And through the boundary out: a frame's table hands back the shared cells.
              let t =
                  tbl
                      [ "i", IntType; "b", BoolType ]
                      [ col "i" IntType [ Int 1; Int 2 ]; col "b" BoolType [ Bool true; Bool true ] ]

              let f = Frame.ofTable t

              let out = Frame.toTable { f with Origins = Array.create 2 None }

              match out.Columns with
              | [ i; b ] ->
                  Expect.equal i.Cells [ Int 1; Int 2 ] "ints read back"
                  Expect.isTrue (obj.ReferenceEquals(List.item 0 b.Cells, List.item 1 b.Cells)) "one shared true"
              | _ -> failtest "two columns"

          testCase
              "the typed path types a derived column from its cells: all null is String, an upsert replaces in place, a window appends"
          <| fun _ ->
              let t: Table =
                  { Schema = [ "i", IntType; "j", IntType ]
                    Columns = [ col "i" IntType [ Int 1; Int 2 ]; col "j" IntType [ Null; Null ] ] }

              let schemaOf (pipeline: Transform list) =
                  match DataFrame.evalPipeline pipeline t with
                  | Ok out -> out.Schema
                  | Error e -> failtestf "unexpected error %A" e

              Expect.equal
                  (schemaOf [ Derive("d", Binary(Add, Col "i", Col "j")) ])
                  [ "i", IntType; "j", IntType; "d", IntType ]
                  "an integer kernel that answers null on every row is still an int column (Phase 338)"

              Expect.equal
                  (schemaOf [ Derive("d", Binary(Add, Col "i", Lit(Int 1))) ])
                  [ "i", IntType; "j", IntType; "d", IntType ]
                  "one present cell and it is Int"

              Expect.equal
                  (schemaOf [ Derive("i", Cast(FloatType, Col "i")) ])
                  [ "i", FloatType; "j", IntType ]
                  "a Derive over an existing name replaces it in place"

              Expect.equal
                  (schemaOf
                      [ Window
                            { Fn = RowNumber
                              Of = "i"
                              PartitionBy = []
                              OrderBy = [ "i", Asc ]
                              As = "i" } ])
                  [ "i", IntType; "j", IntType; "i", IntType ]
                  "a Window appends its column even where the name exists" ]

// ---------------------------------------------------------------------------
//  Phase 270 — the kernel pair. The evaluator runs its row-local verbs through
//  a kernel set chosen when the package is compiled: the portable member under
//  Fable, the native member (vector compares, the thread pool) on .NET. The
//  portable member compiles on .NET too, so both run here, on one host, and
//  are held equal — kernel by kernel, over the transform law vectors, over the
//  generated sample, and over frames many morsels long.
// ---------------------------------------------------------------------------

/// A pipeline evaluated through one member of the kernel pair: the public driver's own fold over a
/// prepared source, with that member in place of the host's.
let private evalWith
    (k: KernelSet)
    (env: Map<string, Cell>)
    (pipeline: Transform list)
    (t: Table)
    : Result<Table, EvalError> =
    DataFrame.evalPreparedCountedWith k DataFrame.noResolve env pipeline (DataFrame.prepare t)
    |> Result.map fst

/// An answer as the bytes the parity contract compares: the canonical wire string of a table, and
/// the refusal's text — the pair must refuse with the SAME error, the first one in row order.
let private wireOf (r: Result<Table, EvalError>) : string =
    match r with
    | Ok t -> "ok " + ColumnCodec.encode (Embedded t)
    | Error e -> "error " + DataFrame.errorString e

let private allCmpOps = [ CLt; CLe; CGt; CGe; CEq; CNe ]

/// The per-row definition every comparison kernel answers: present, and the pinned ordering holds.
let private cmpDefinition (op: CmpOp) (count: int) (present: int -> bool) (cmp: int -> int) : bool[] =
    Array.init count (fun p -> present p && Kernels.holds op (cmp p))

/// A bitmap read back one row at a time, with every bit past `count` required clear.
let private bitsOf (bits: uint32[]) (count: int) : bool[] =
    Expect.equal bits.Length (Kernels.words count) "one word per 32 rows"

    for p in count .. bits.Length * 32 - 1 do
        Expect.isFalse (Kernels.isSet bits p) (sprintf "bit %d past the %d rows is clear" p count)

    Array.init count (Kernels.isSet bits)

/// A predicate the comparison kernels answer: `And` / `Or` over comparisons of a typed numeric
/// column with a constant — a literal or a bound param — on either side.
let rec private kernelPred (rng: System.Random) (depth: int) : ColExpr =
    if depth <= 0 || rng.Next 3 = 0 then
        let column, constant =
            match rng.Next 5 with
            | 0 -> pick rng (colsOfType IntType), Lit(conformingCell rng IntType)
            | 1 -> pick rng (colsOfType IntType), Param "p"
            | 2 -> pick rng (colsOfType FloatType), Lit(conformingCell rng FloatType)
            | 3 -> pick rng (colsOfType FloatType), Param "pf"
            | _ -> pick rng (colsOfType FloatType), Lit(Int(rng.Next(-5, 6)))

        let op = pick rng [ Lt; Le; Gt; Ge; Eq; Ne ]

        if rng.Next 2 = 0 then
            Binary(op, Col column, constant)
        else
            Binary(op, constant, Col column)
    else
        Binary(pick rng [ And; Or ], kernelPred rng (depth - 1), kernelPred rng (depth - 1))

/// A table over `typedSchema` whose every cell conforms to its column or is null, so every column
/// unpacks TYPED and the kernels are reachable.
let private conformingTable (rng: System.Random) (n: int) : Table =
    { Schema = typedSchema
      Columns =
        typedSchema
        |> List.map (fun (name, ty) ->
            col name ty [ for _ in 1..n -> if rng.Next 8 = 0 then Null else conformingCell rng ty ]) }

/// The float aggregates a reassociated reduction would change the last bit of.
let private floatAggs (over: string) : Agg list =
    [ { Name = "sum"; Fn = Sum; Of = over }
      { Name = "mean"; Fn = Mean; Of = over }
      { Name = "sd"; Fn = StdDev; Of = over } ]

/// A member of a JSON object, for reading the law vector file.
let private jsonMember (name: string) (el: JVal) : JVal option =
    match el with
    | JObj ms -> ms |> List.tryPick (fun (k, v) -> if k = name then Some v else None)
    | _ -> None

/// A string member of a JSON object.
let private jsonText (name: string) (el: JVal) : string option =
    match jsonMember name el with
    | Some(JStr s) -> Some s
    | _ -> None

/// Phase 342 — an evaluation whose result stays prepared: `DataFrame.evalToPrepared` held to
/// `evalPrepared` and to the chain through the `Table` boundary.
[<Tests>]
let preparedResultTests =
    testList
        "an evaluation kept prepared (Phase 342)"
        [ testCase
              "an evaluation kept prepared answers evalPrepared's table and errors over the transform vectors' sample, and serves as the next pipeline's source (the law the node harness runs too)"
          <| fun _ ->
              let failures, compared = PreparedResultLaw.check ()
              Expect.isEmpty failures (sprintf "the prepared-result law: %A" (List.truncate 5 failures))
              let n = TransformVectorSample.iterations
              // Every vector, then every vector's kept result fed every pipeline twice over.
              Expect.isGreaterThan compared (n * 10) "the law compared the sample, not a handful"

          testCase
              "over the committed transform law vectors, toTable of the kept result is the vector's answer and a refusal is evalPrepared's refusal"
          <| fun _ ->
              let path =
                  System.IO.Path.Combine(OwnedConformance.root (), "laws", "transform-laws.json")

              let doc =
                  match Json.parse (System.IO.File.ReadAllText path) with
                  | Ok d -> d
                  | Error m -> failtestf "the vector file did not parse: %s" m

              let vectors =
                  match jsonMember "vectors" doc with
                  | Some(JArr items) -> items |> List.filter (fun v -> jsonText "case" v = Some "evalPipeline")
                  | _ -> failtest "the vector file carries no vectors"

              Expect.equal vectors.Length TransformVectorSample.iterations "one evalPipeline vector per iteration"
              let mutable refused = 0

              for v in vectors do
                  let id = jsonText "id" v |> Option.defaultValue "?"
                  let input = jsonMember "input" v |> Option.get
                  let expected = jsonMember "expected" v |> Option.get

                  let pipeline =
                      match DataFrameCodec.decodePipeline (jsonText "pipeline" input |> Option.get) with
                      | Ok p -> p
                      | Error e -> failtestf "%s: the pipeline did not decode (%s)" id (ColumnCodec.errorString e)

                  let source =
                      match ColumnCodec.decode (jsonText "source" input |> Option.get) with
                      | Ok(Embedded t) -> DataFrame.prepare t
                      | other -> failtestf "%s: the source is not an embedded table (%A)" id other

                  let reference = DataFrame.evalPrepared DataFrame.noResolve Map.empty pipeline source
                  let kept = DataFrame.evalToPrepared DataFrame.noResolve Map.empty pipeline source

                  match jsonText "verdict" expected, reference, kept with
                  | Some "ok", Ok _, Ok p ->
                      Expect.equal
                          (ColumnCodec.encode (Embedded(DataFrame.toTable p)))
                          (jsonText "table" expected |> Option.get)
                          (sprintf "%s: toTable of the kept result is the vector's answer, byte for byte" id)
                  | Some "error", Error a, Error b ->
                      refused <- refused + 1
                      Expect.equal b a (sprintf "%s: the kept evaluation refuses with evalPrepared's error" id)
                  | verdict, r, k ->
                      failtestf "%s: the vector says %A; evalPrepared %A; evalToPrepared %A" id verdict r k

              Expect.isGreaterThan refused 0 "the sample carries refusals, so the error clause was exercised"

          testCase
              "a chain of three pipelines kept prepared between hops answers what the chain through the Table boundary answers, and a kept result takes column ops as a prepared table does"
          <| fun _ ->
              let rng = System.Random 3420

              let ev p src =
                  DataFrame.evalPrepared DataFrame.noResolve typedEnv p src

              let keep p src =
                  DataFrame.evalToPrepared DataFrame.noResolve typedEnv p src

              let mutable chains = 0
              let mutable selected = 0

              for _ in 1..300 do
                  let table = frameTable rng (rng.Next 9) (rng.Next 3 = 0)
                  let p1, p2, p3 = genPipeline rng, genPipeline rng, genPipeline rng

                  // Through the boundary at every hop: Table out, Table in.
                  let viaTables =
                      DataFrame.evalPipelineWithInEnv DataFrame.noResolve typedEnv p1 table
                      |> Result.bind (DataFrame.evalPipelineWithInEnv DataFrame.noResolve typedEnv p2)
                      |> Result.bind (DataFrame.evalPipelineWithInEnv DataFrame.noResolve typedEnv p3)

                  // Kept prepared between hops: the boundary once in, once out.
                  let viaPrepared =
                      keep p1 (DataFrame.prepare table)
                      |> Result.bind (keep p2)
                      |> Result.bind (ev p3)

                  Expect.equal (tokenised viaPrepared) (tokenised viaTables) (sprintf "%A > %A > %A" p1 p2 p3)
                  chains <- chains + 1

                  match keep p1 (DataFrame.prepare table) with
                  | Ok kept ->
                      if
                          p1
                          |> List.exists (function
                              | Filter _
                              | Sort _
                              | Limit _ -> true
                              | _ -> false)
                      then
                          selected <- selected + 1

                      let back = DataFrame.toTable kept
                      Expect.isTrue (obj.ReferenceEquals(back, DataFrame.toTable kept)) "the table is built once"

                      match back.Schema with
                      | (name, _) :: _ when Table.rowCount back > 0 ->
                          let op = SetCell(name, 0, Null)

                          // Compared as wire: the generated cells carry NaN, which no structural
                          // equality holds equal to itself.
                          let wire (p: Prepared) =
                              ColumnCodec.encode (Embedded(DataFrame.toTable p))

                          Expect.equal
                              (ColumnOps.applyPrepared op kept |> Result.map wire)
                              (ColumnOps.applyPrepared op (DataFrame.prepare back) |> Result.map wire)
                              "a column op over the kept result is the op over its table prepared"
                      | _ -> ()
                  | Error _ -> ()

              Expect.equal chains 300 "every chain compared"
              Expect.isGreaterThan selected 30 "kept results whose last steps left a selection were reached"

          testCase "a kept result over no columns stands for the empty table, as its table prepared afresh does"
          <| fun _ ->
              let t: Table =
                  { Schema = [ "a", IntType ]
                    Columns = [ col "a" IntType [ Int 1; Int 2; Int 3 ] ] }

              let none = [ Project [] ]
              let follow = [ Derive("x", Lit(Int 1)) ]

              match DataFrame.evalToPrepared DataFrame.noResolve Map.empty none (DataFrame.prepare t) with
              | Ok kept ->
                  let back = DataFrame.toTable kept

                  Expect.equal
                      (tokenised (DataFrame.evalPrepared DataFrame.noResolve Map.empty follow kept))
                      (tokenised (DataFrame.evalPipeline follow back))
                      "a follow-on over the kept result answers as over its table"
              | Error e ->
                  Expect.isTrue (Result.isError (DataFrame.evalPipeline none t)) (sprintf "refused alike: %A" e) ]

[<Tests>]
let kernelTests =
    testList
        "Kernels"
        [ testCase "each kernel of the pair answers as the other, and both as the per-row definition"
          <| fun _ ->
              let rng = System.Random 270
              let ints = [| System.Int32.MinValue; System.Int32.MaxValue; -1; 0; 1; 7 |]
              let floats = [| nan; -0.0; 0.0; infinity; -infinity; 1.5; -2.25; 7.0 |]

              let anyInt () =
                  if rng.Next 2 = 0 then
                      ints[rng.Next ints.Length]
                  else
                      rng.Next(-8, 9)

              let anyFloat () =
                  if rng.Next 2 = 0 then
                      floats[rng.Next floats.Length]
                  else
                      float (rng.Next(-8, 9)) / 2.0

              for trial in 1..400 do
                  let count = pick rng [ 0; 1; 15; 16; 17; 31; 32; 33; 63; 64; 65; rng.Next 3000 ]
                  let mask = Array.init count (fun _ -> rng.Next 5 <> 0)
                  let iv = Array.init count (fun _ -> anyInt ())
                  let fv = Array.init count (fun _ -> anyFloat ())
                  let ik = anyInt ()
                  let fk = anyFloat ()

                  for op in allCmpOps do
                      let pi = Kernels.portable.CmpInts op iv mask ik count
                      let ni = Kernels.native.CmpInts op iv mask ik count
                      Expect.equal ni pi (sprintf "trial %d: int %A %d over %d rows" trial op ik count)

                      Expect.equal
                          (bitsOf pi count)
                          (cmpDefinition op count (fun p -> mask[p]) (fun p -> compare iv[p] ik))
                          "the int kernel is the per-row definition"

                      let pf = Kernels.portable.CmpFloats op fv mask fk count
                      let nf = Kernels.native.CmpFloats op fv mask fk count
                      Expect.equal nf pf (sprintf "trial %d: float %A %g over %d rows" trial op fk count)

                      Expect.equal
                          (bitsOf pf count)
                          (cmpDefinition op count (fun p -> mask[p]) (fun p ->
                              // Phase 321: the definition is the SUBSTRATE's order (NaN above
                              // every value, -0.0 = 0.0), not the host's `compare`.
                              Cell.compare (Float fv[p]) (Float fk) |> Option.get))
                          "the float kernel is the per-row definition, NaN and -0.0 included"

                  let a = Kernels.portable.CmpInts CGe iv mask ik count
                  let b = Kernels.portable.CmpFloats CLt fv mask fk count
                  Expect.equal (Kernels.native.And a b) (Kernels.portable.And a b) "And agrees"
                  Expect.equal (Kernels.native.Or a b) (Kernels.portable.Or a b) "Or agrees"

                  Expect.equal
                      (bitsOf (Kernels.portable.And a b) count)
                      (Array.map2 (&&) (bitsOf a count) (bitsOf b count))
                      "And is both"

                  Expect.equal
                      (bitsOf (Kernels.portable.Or a b) count)
                      (Array.map2 (||) (bitsOf a count) (bitsOf b count))
                      "Or is either"

                  let expected =
                      [| for p in 0 .. count - 1 do
                             if Kernels.isSet a p then
                                 p |]

                  Expect.equal
                      (Kernels.portable.Selection a)
                      expected
                      "the portable selection is the set rows, ascending"

                  Expect.equal (Kernels.native.Selection a) expected "the native selection is the set rows, ascending"

          testCase "the morsel runner runs every morsel once, and the portable member stops at the first failure"
          <| fun _ ->
              for m in [ 0; 1; 2; 7 ] do
                  for k in [ Kernels.portable; Kernels.native ] do
                      let ran: int[] = Array.zeroCreate m

                      k.RunMorsels m (fun j ->
                          System.Threading.Interlocked.Increment(&ran[j]) |> ignore
                          true)

                      Expect.equal ran (Array.create m 1) (sprintf "%d morsels, each run once" m)

              let ran: int[] = Array.zeroCreate 5

              Kernels.portable.RunMorsels 5 (fun j ->
                  ran[j] <- 1
                  j <> 2)

              Expect.equal ran [| 1; 1; 1; 0; 0 |] "the portable member stops after the morsel that failed"
              Expect.equal (Kernels.morselCount 0) 0 "no rows, no morsels"
              Expect.equal (Kernels.morselCount Kernels.MorselRows) 1 "a full morsel is one"
              Expect.equal (Kernels.morselCount (Kernels.MorselRows + 1)) 2 "one row more is two"

              Expect.equal
                  (Kernels.morselEnd (Kernels.MorselRows + 1) 1)
                  (Kernels.MorselRows + 1)
                  "the last morsel is short"

          testCase "the transform law vectors answer byte-identically through both members of the pair"
          <| fun _ ->
              let path =
                  System.IO.Path.Combine(OwnedConformance.root (), "laws", "transform-laws.json")

              let doc =
                  match Json.parse (System.IO.File.ReadAllText path) with
                  | Ok d -> d
                  | Error m -> failtestf "the vector file did not parse: %s" m

              // Phase 321: the evalPipeline vectors; the columnOp and delta kinds run no pipeline.
              let vectors =
                  match jsonMember "vectors" doc with
                  | Some(JArr items) -> items |> List.filter (fun v -> jsonText "case" v = Some "evalPipeline")
                  | _ -> failtest "the vector file carries no vectors"

              Expect.isGreaterThan vectors.Length 0 "there are vectors to run"

              for v in vectors do
                  let id = jsonText "id" v |> Option.defaultValue "?"
                  let input = jsonMember "input" v |> Option.get
                  let expected = jsonMember "expected" v |> Option.get

                  let pipeline =
                      match DataFrameCodec.decodePipeline (jsonText "pipeline" input |> Option.get) with
                      | Ok p -> p
                      | Error e -> failtestf "%s: the pipeline did not decode (%s)" id (ColumnCodec.errorString e)

                  let table =
                      match ColumnCodec.decode (jsonText "source" input |> Option.get) with
                      | Ok(Embedded t) -> t
                      | other -> failtestf "%s: the source is not an embedded table (%A)" id other

                  let portable = evalWith Kernels.portable Map.empty pipeline table
                  let native = evalWith Kernels.native Map.empty pipeline table
                  Expect.equal (wireOf native) (wireOf portable) (sprintf "%s: the two members agree" id)

                  match jsonText "verdict" expected, portable with
                  | Some "ok", Ok t ->
                      Expect.equal
                          (ColumnCodec.encode (Embedded t))
                          (jsonText "table" expected |> Option.get)
                          (sprintf "%s: the answer is the vector's, byte for byte" id)
                  | Some "error", Error _ -> ()
                  | verdict, r -> failtestf "%s: the vector says %A, the pair answered %A" id verdict r

          testCase "a generated sample over the whole algebra answers byte-identically through both members"
          <| fun _ ->
              let mutable compared = 0

              for table, pipeline in frameSample 270 400 do
                  let portable = evalWith Kernels.portable typedEnv pipeline table
                  let native = evalWith Kernels.native typedEnv pipeline table
                  Expect.equal (wireOf native) (wireOf portable) (sprintf "the members agree over %A" pipeline)

                  Expect.equal
                      (wireOf native)
                      (wireOf (DataFrame.evalPipelineInEnv typedEnv pipeline table))
                      "and the host is the native member"

                  compared <- compared + 1

              Expect.equal compared 400 "every draw compared"

          testCase
              "over frames many morsels long, Filter, Derive and the float aggregates answer byte-identically through both members"
          <| fun _ ->
              // Frames of two to three morsels, so the native member runs morsels on the thread
              // pool and the comparison kernels read whole vectors; the float `Sum` / `Mean` /
              // `StdDev` over what the steps kept are the reductions a reassociation would move.
              let rng = System.Random 2700
              let mutable kernelPaths = 0

              for trial in 1..4 do
                  let n = Kernels.MorselRows * (2 + rng.Next 2) + rng.Next 100
                  let table = conformingTable rng n
                  let frame = Frame.ofTable table
                  let pred = kernelPred rng 3

                  if
                      DataFrame.filterBits Kernels.host frame (DataFrame.resolveExpr typedEnv frame.Cols pred)
                      |> Option.isSome
                  then
                      kernelPaths <- kernelPaths + 1

                  let key = pick rng [ "b"; "s"; "d" ]

                  let pipelines =
                      [ [ Filter pred; GroupBy([ key ], floatAggs "f") ]
                        [ Filter(genExpr rng 2); GroupBy([ key ], floatAggs "g") ]
                        [ Derive("x", genExpr rng 2)
                          Filter(kernelPred rng 2)
                          GroupBy([ key ], floatAggs "f") ]
                        [ Transform.sortBy [ "g", Desc ]
                          Filter pred
                          Derive("y", Binary(Mul, Col "f", Col "g")) ] ]

                  for pipeline in pipelines do
                      let portable = evalWith Kernels.portable typedEnv pipeline table
                      let native = evalWith Kernels.native typedEnv pipeline table

                      Expect.equal
                          (wireOf native)
                          (wireOf portable)
                          (sprintf "trial %d: the members agree over %d rows and %A" trial n pipeline)

              Expect.equal kernelPaths 4 "every drawn kernel predicate took the comparison kernels"

          testCase "the first error in row order answers, whichever morsel meets it"
          <| fun _ ->
              // Two rows overflow `i + j`, one in the second morsel and one in the third, each with
              // its own message; the earlier one is the answer, as a sequential walk gives it.
              let n = Kernels.MorselRows * 3 + 10
              let early = Kernels.MorselRows + 100
              let late = Kernels.MorselRows * 2 + 5

              let table =
                  tbl
                      [ "i", IntType; "j", IntType ]
                      [ col
                            "i"
                            IntType
                            [ for p in 0 .. n - 1 -> Int(if p = early || p = late then System.Int32.MaxValue else 0) ]
                        col
                            "j"
                            IntType
                            [ for p in 0 .. n - 1 ->
                                  Int(
                                      if p = early then 1
                                      elif p = late then 2
                                      else p % 7
                                  ) ] ]

              let sum = Binary(Add, Col "i", Col "j")

              for pipeline in [ [ Derive("s", sum) ]; [ Filter(Binary(Gt, sum, Lit(Int 0))) ] ] do
                  let portable = evalWith Kernels.portable Map.empty pipeline table
                  let native = evalWith Kernels.native Map.empty pipeline table
                  Expect.equal (wireOf native) (wireOf portable) "the members agree"

                  match portable with
                  | Error e ->
                      Expect.stringContains (DataFrame.errorString e) "2147483648" "the earlier row's overflow answers"
                  | Ok _ -> failtest "an overflowing row refuses the step"

          testCase "a float Sum over a kept selection is the left-to-right fold of the kept rows, on both members"
          <| fun _ ->
              // Values whose sum depends on the order it is taken in: a reassociated reduction —
              // per-morsel partial sums, a vector accumulator — lands on a different last bit.
              let rng = System.Random 27
              let n = Kernels.MorselRows * 3 + 77
              let iv = Array.init n (fun _ -> rng.Next(-5, 6))

              let fv =
                  Array.init n (fun _ -> float (rng.Next 1000) * 0.1 + 1e-7 * float (rng.Next 1000))

              let table =
                  tbl
                      [ "i", IntType; "f", FloatType ]
                      [ col "i" IntType [ for v in iv -> Int v ]
                        col "f" FloatType [ for v in fv -> Float v ] ]

              let pipeline =
                  [ Filter(Binary(Ge, Col "i", Lit(Int 0)))
                    Derive("one", Lit(Int 1))
                    GroupBy([ "one" ], [ { Name = "sum"; Fn = Sum; Of = "f" } ]) ]

              let mutable fold = 0.0

              for p in 0 .. n - 1 do
                  if iv[p] >= 0 then
                      fold <- fold + fv[p]

              // The teeth: the same rows summed per morsel and the partials then added land elsewhere,
              // so a reassociating kernel could not pass the assertion below by accident.
              let partials =
                  [ for j in 0 .. Kernels.morselCount n - 1 ->
                        let mutable s = 0.0

                        for p in Kernels.morselStart j .. Kernels.morselEnd n j - 1 do
                            if iv[p] >= 0 then
                                s <- s + fv[p]

                        s ]

              Expect.notEqual
                  (System.BitConverter.DoubleToInt64Bits(List.sum partials))
                  (System.BitConverter.DoubleToInt64Bits fold)
                  "the sample is order-sensitive"

              for k in [ Kernels.portable; Kernels.native ] do
                  match evalWith k Map.empty pipeline table with
                  | Ok t ->
                      match cellsOf "sum" t with
                      | [ Float s ] ->
                          Expect.equal
                              (System.BitConverter.DoubleToInt64Bits s)
                              (System.BitConverter.DoubleToInt64Bits fold)
                              "the sum is the sequential fold, to the last bit"
                      | other -> failtestf "one sum expected, got %A" other
                  | Error e -> failtestf "evaluation failed: %s" (DataFrame.errorString e) ]

// ---------------------------------------------------------------------------
//  Phase 327 — the in-place refresh over a sort or a limit. A one-cell edit
//  leaves every row at its slot, so the refresh builds its frame without
//  re-counting the unchanged columns and reads the row-local steps ahead of the
//  first sort or limit in place; every step after it takes the general reading,
//  because a sort reorders the frame and a limit lets a row the delta did not
//  name enter or leave its window. Held here against the reference over chains
//  of in-place edits, on the shapes where that distinction decides the answer.
// ---------------------------------------------------------------------------

[<Tests>]
let inPlaceOrderingTests =
    testList
        "Incremental — in place over a sort or a limit (Phase 327)"
        [ testCase
              "chains of one-cell edits refresh to the reference answer over sorts, limits and the steps after them"
          <| fun _ ->
              let idw = RowIdentity.byColumn "id"

              let ok r =
                  match r with
                  | Ok v -> v
                  | Error e -> failtestf "unexpected error %A" e

              let pipelines: Transform list list =
                  [ [ Filter(Binary(Ge, Col "a", Lit(Int 0)))
                      Transform.sortBy [ "a", Desc ]
                      Transform.limit 3 0 ]
                    [ Filter(Binary(Ge, Col "a", Lit(Int 0)))
                      Transform.sortBy [ "a", Desc ]
                      Transform.limit 3 0
                      Derive("x", Binary(Mul, Col "b", Lit(Int 2))) ]
                    [ Filter(Binary(Ge, Col "a", Lit(Int 0)))
                      Transform.limit 4 1
                      Derive("x", Binary(Add, Col "a", Col "b"))
                      Filter(Binary(Gt, Col "x", Lit(Int 3))) ]
                    [ Derive("x", Binary(Mul, Col "a", Lit(Int 3)))
                      Transform.sortBy [ "b", Asc; "x", Desc ]
                      Filter(Binary(Ne, Col "b", Lit(Int 2)))
                      Transform.limit 5 0 ]
                    [ Transform.sortBy [ "a", Asc ]
                      Derive("x", Binary(Sub, Col "a", Col "b"))
                      Transform.limit 2 2 ] ]

              let rng = System.Random 3272
              let mutable ticks = 0

              for p in pipelines do
                  for _ in 1..40 do
                      let n = 1 + rng.Next 12

                      let mk (a: int[]) (b: int[]) : Table =
                          { Schema = [ "id", IntType; "a", IntType; "b", IntType ]
                            Columns =
                              [ col "id" IntType [ for i in 0 .. n - 1 -> Int i ]
                                col "a" IntType [ for v in a -> if v = 99 then Null else Int v ]
                                col "b" IntType [ for v in b -> Int v ] ] }

                      let a = Array.init n (fun _ -> if rng.Next 8 = 0 then 99 else rng.Next(-3, 6))
                      let b = Array.init n (fun _ -> rng.Next 4)
                      let mutable prior = mk a b
                      let mutable state = ok (Incremental.primeOn idw p prior)

                      for _ in 1..6 do
                          // One cell of `a` or `b` edited, every other column's list kept by reference:
                          // the delta finds every row in place.
                          let i = rng.Next n
                          let edited = if rng.Next 2 = 0 then "a" else "b"

                          let value =
                              if edited = "a" && rng.Next 6 = 0 then
                                  Null
                              else
                                  Int(rng.Next(-3, 6))

                          let next =
                              { prior with
                                  Columns =
                                      prior.Columns
                                      |> List.map (fun c ->
                                          if c.Name <> edited then
                                              c
                                          else
                                              Column.create
                                                  c.Name
                                                  c.Type
                                                  (c.Cells |> List.mapi (fun j x -> if j = i then value else x))) }

                          let delta = ok (Delta.diff idw prior next)
                          state <- ok (Incremental.refreshOn idw p state delta next)
                          ticks <- ticks + 1

                          Expect.equal
                              (tokenised (Ok(Incremental.result state)))
                              (tokenised (DataFrame.evalPipeline p next))
                              (sprintf "%A after editing %s at row %d" p edited i)

                          prior <- next

              Expect.isGreaterThan ticks 1000 "the sample refreshed" ]

// ---- Phase 324: window and sort under order codes ----

/// One cell of a column of `ty` for the ordering laws: duplicates, nulls, `NaN`, both zeros and the
/// infinities among the floats, two spellings of one decimal, ordinal-only string distinctions, and
/// int values either in a narrow band or at int32's ends (`wide`), whose span no offset code covers.
/// In a `dirty` column one cell in eight is outside the type, so the column packs boxed and takes the
/// comparator path.
let private orderCell (rng: System.Random) (ty: ColumnType) (wide: bool) (dirty: bool) : Cell =
    let pick (xs: Cell[]) = xs[rng.Next xs.Length]

    if dirty && rng.Next 8 = 0 then
        pick [| Int 1; Str "x"; Float 2.0; Bool true |]
    else
        match ty with
        | IntType when wide ->
            pick
                [| Null
                   Int System.Int32.MaxValue
                   Int System.Int32.MinValue
                   Int 0
                   Int 5
                   Int 5
                   Int -7 |]
        | IntType -> if rng.Next 6 = 0 then Null else Int(rng.Next(-3, 4))
        | FloatType ->
            pick
                [| Null
                   Float 0.0
                   Float -0.0
                   Float nan
                   Float infinity
                   Float -infinity
                   Float 1.5
                   Float 1.5
                   Float -2.25
                   Float 1e-300 |]
        | DecimalType ->
            pick
                [| Null
                   Decimal "1.50"
                   Decimal "1.5"
                   Decimal "-2"
                   Decimal "0.10"
                   Decimal "3" |]
        | BoolType -> pick [| Null; Bool true; Bool false |]
        | DateType -> pick [| Null; Date "2026-01-02"; Date "2025-12-31"; Date "2026-01-02" |]
        | _ -> pick [| Null; Str "b"; Str "a"; Str ""; Str "B"; Str "ab"; Str "a" |]

let private orderTypes =
    [| IntType; FloatType; DecimalType; StringType; BoolType; DateType |]

/// Case `seed` of the ordering laws: a table of 0 to 40 rows over five columns (the fifth an int
/// column whose values may span int32), read through a selection that drops and permutes rows.
let private orderCase (seed: int) : Frame =
    let rng = System.Random seed
    let n = if rng.Next 10 = 0 then rng.Next 3 else rng.Next 41
    let types = Array.init 4 (fun _ -> orderTypes[rng.Next orderTypes.Length])
    let wide = rng.Next 2 = 0

    let schema = [ for j in 0..3 -> sprintf "c%d" j, types[j] ] @ [ "w", IntType ]

    let columns =
        [ for j in 0..3 ->
              let dirty = rng.Next 6 = 0
              Column.create (sprintf "c%d" j) types[j] [ for _ in 1..n -> orderCell rng types[j] false dirty ] ]
        @ [ Column.create "w" IntType [ for _ in 1..n -> orderCell rng IntType wide false ] ]

    let f = Frame.ofTable { Schema = schema; Columns = columns }
    // A selection: some physical rows dropped, the rest shuffled.
    let kept = [| 0 .. n - 1 |] |> Array.filter (fun _ -> rng.Next 5 <> 0)

    for i in kept.Length - 1 .. -1 .. 1 do
        let j = rng.Next(i + 1)
        let t = kept[i]
        kept[i] <- kept[j]
        kept[j] <- t

    if rng.Next 3 = 0 then f else Frame.select f kept

/// One to three sort keys over the case's columns, each in a random direction.
let private orderKeysOf (rng: System.Random) : (int * SortDir) list =
    [ for _ in 1 .. 1 + rng.Next 3 -> rng.Next 5, (if rng.Next 2 = 0 then Asc else Desc) ]

/// The reference order: the stable `List.sortWith` under the pinned row comparator the evaluator
/// sorted with before Phase 324, over the frame's rows in logical order.
let private referenceOrder (rows: Cell[][]) (keys: (int * SortDir) list) : int list =
    List.sortWith (fun a b -> DataFrame.compareResolved keys rows[a] rows[b]) [ 0 .. rows.Length - 1 ]

/// The ordering law over `seeds` under `perturbation`: wherever every key is coded, the coded
/// permutation — packed, and compared as a code tuple — is the reference order, and two rows share
/// their codes exactly when the reference comparator calls them equal. Returns the failures and how
/// many cases were coded and how many fell to the comparator.
let private orderingLaw (perturbation: DataFrame.Ordering.Perturbation) (seeds: int seq) =
    let failures = ResizeArray<string>()
    let mutable coded = 0
    let mutable boxed = 0
    let mutable packed = 0

    for seed in seeds do
        let f = orderCase seed
        let rng = System.Random(seed + 7)
        let keys = orderKeysOf rng
        let rows = Frame.rowsOf f
        let phys = Frame.physical f
        let n = rows.Length
        let expected = referenceOrder rows keys
        let keyVecs = keys |> List.map (fun (ci, dir) -> f.Vecs[ci], dir) |> List.toArray

        match DataFrame.Ordering.codesAll keyVecs phys with
        | ValueNone -> boxed <- boxed + 1
        | ValueSome codes ->
            coded <- coded + 1
            let o = DataFrame.Ordering.build perturbation null 0 codes n

            if not (isNull o.Packed) then
                packed <- packed + 1

            for label, order in [ "packed", o; "tuple", { o with Packed = null } ] do
                let got = DataFrame.Ordering.permutation order |> List.ofArray

                if got <> expected then
                    failures.Add(sprintf "seed %d keys %A (%s): coded %A, reference %A" seed keys label got expected)

            for a in 0 .. n - 1 do
                for b in 0 .. n - 1 do
                    let same = DataFrame.Ordering.sameCodes codes a b
                    let tied = DataFrame.compareResolved keys rows[a] rows[b] = 0

                    if same <> tied then
                        failures.Add(
                            sprintf "seed %d keys %A: rows %d and %d share codes %b, tie %b" seed keys a b same tied
                        )

    List.ofSeq failures, coded, boxed, packed

/// `compareCells` as the reference window read it (Phase 277's decimal arm included).
let private refCompareCells (a: Cell) (b: Cell) : int option =
    let num c =
        match c with
        | Int i -> float i
        | Float x -> x
        | _ -> nan

    match a, b with
    | (Int _ | Float _), (Int _ | Float _) -> Some(Kernels.compareFloat (num a) (num b))
    | Decimal _, (Decimal _ | Int _)
    | Int _, Decimal _ -> Cell.compare a b
    | Bool x, Bool y -> Some(compare x y)
    | Str x, Str y
    | Date x, Date y
    | Timestamp x, Timestamp y -> Some(System.String.CompareOrdinal(x, y))
    | _ -> None

/// The reference `Window` column — the row-list algorithm the evaluator ran before Phase 324,
/// kept here as the oracle: each partition's rows gathered by `CellKey`, sorted with the stable
/// `List.sortWith` under `compareResolved`, the function computed over the ordered list, and the
/// outputs scattered back to the rows' positions.
let private referenceWindow (cols: Schema) (rows: Cell[][]) (spec: WindowSpec) : Cell[] =
    let idx name =
        cols |> List.tryFindIndex (fun (c, _) -> c = name)

    let sourceIsDecimal =
        cols |> List.exists (fun (c, ty) -> c = spec.Of && ty = DecimalType)

    let partIdx = spec.PartitionBy |> List.choose idx |> List.toArray
    let orderKeys = DataFrame.resolveSortKeys cols spec.OrderBy
    let slots = DataFrame.CellKey.slots ()
    let probe: Cell[] = Array.zeroCreate partIdx.Length
    let parts = ResizeArray<ResizeArray<int * Cell[]>>()

    rows
    |> Array.iteri (fun i row ->
        for j in 0 .. partIdx.Length - 1 do
            probe[j] <- row[partIdx[j]]

        match slots.TryGetValue probe with
        | true, p -> parts[p].Add((i, row))
        | _ ->
            slots[Array.copy probe] <- parts.Count
            parts.Add(ResizeArray [ i, row ]))

    let ofIdx = idx spec.Of

    let valueAt (row: Cell[]) =
        match ofIdx with
        | Some i -> row[i]
        | None -> Null

    let asNum c =
        match c with
        | Int i -> Some(float i)
        | Float x -> Some x
        | _ -> None

    let decimalText c =
        match c with
        | Decimal s -> Some s
        | Int i -> Some(string i)
        | _ -> None

    let out = Array.create rows.Length Null

    for members in parts do
        let ordered =
            List.ofSeq members
            |> List.sortWith (fun (_, a) (_, b) -> DataFrame.compareResolved orderKeys a b)
            |> Array.ofList

        let len = ordered.Length
        let vals = ordered |> Array.map (snd >> valueAt)

        let tiedWithPrev k =
            k > 0
            && DataFrame.compareResolved orderKeys (snd ordered[k - 1]) (snd ordered[k]) = 0

        let outs: Cell[] = Array.create len Null

        match spec.Fn with
        | RowNumber ->
            for k in 0 .. len - 1 do
                outs[k] <- Int(k + 1)
        | Rank
        | DenseRank ->
            let mutable r = 0

            for k in 0 .. len - 1 do
                if not (tiedWithPrev k) then
                    r <- r + 1

                outs[k] <- Int r
        | CompetitionRank ->
            let mutable r = 0

            for k in 0 .. len - 1 do
                if not (tiedWithPrev k) then
                    r <- k + 1

                outs[k] <- Int r
        | NTile b ->
            let small = len / b
            let big = len % b
            let bigRows = big * (small + 1)

            for k in 0 .. len - 1 do
                outs[k] <-
                    if k < bigRows then
                        Int(k / (small + 1) + 1)
                    else
                        Int(big + (k - bigRows) / small + 1)
        | Lag ->
            for k in 1 .. len - 1 do
                outs[k] <- vals[k - 1]
        | Lead ->
            for k in 0 .. len - 2 do
                outs[k] <- vals[k + 1]
        | CumulSum when sourceIsDecimal ->
            let mutable acc = DecimalText.zero

            for k in 0 .. len - 1 do
                match decimalText vals[k] with
                | Some x -> acc <- DecimalText.add acc x |> Option.defaultValue acc
                | None -> ()

                outs[k] <- Decimal acc
        | CumulSum ->
            let mutable acc = 0.0

            for k in 0 .. len - 1 do
                match asNum vals[k] with
                | Some x -> acc <- acc + x
                | None -> ()

                outs[k] <- Float acc
        | RollingMean
        | RollingSum ->
            for k in 0 .. len - 1 do
                if sourceIsDecimal && spec.Fn = RollingSum then
                    let mutable sum = DecimalText.zero
                    let mutable count = 0

                    for j in max 0 (k - 2) .. k do
                        match decimalText vals[j] with
                        | Some x ->
                            sum <- DecimalText.add sum x |> Option.defaultValue sum
                            count <- count + 1
                        | None -> ()

                    outs[k] <- if count = 0 then Null else Decimal sum
                else
                    let mutable sum = 0.0
                    let mutable count = 0

                    for j in max 0 (k - 2) .. k do
                        let x =
                            match vals[j] with
                            | Decimal t -> DecimalText.tryToFloat t
                            | c -> asNum c

                        match x with
                        | Some x ->
                            sum <- sum + x
                            count <- count + 1
                        | None -> ()

                    outs[k] <-
                        if count = 0 then Null
                        elif spec.Fn = RollingSum then Float sum
                        else Float(sum / float count)
        | CumulMax
        | CumulMin ->
            let mutable acc = Null

            for k in 0 .. len - 1 do
                let v = vals[k]

                acc <-
                    match acc, v with
                    | _, Null -> acc
                    | Null, _ -> v
                    | _ ->
                        match refCompareCells acc v with
                        | Some c -> if (spec.Fn = CumulMin) = (c <= 0) then acc else v
                        | None -> acc

                outs[k] <- acc

        for k in 0 .. len - 1 do
            out[fst ordered[k]] <- outs[k]

    out

let private windowFns =
    [| RowNumber
       Rank
       DenseRank
       CompetitionRank
       NTile 1
       NTile 3
       Lag
       Lead
       CumulSum
       CumulMax
       CumulMin
       RollingMean
       RollingSum |]

/// The window law over `seeds` under `perturbation`: the frame path's column (and, under `Exact`,
/// the public row form's) is the reference window's, cell for cell and bit for bit.
let private windowLaw (perturbation: DataFrame.Ordering.Perturbation) (seeds: int seq) =
    let failures = ResizeArray<string>()
    let mutable cases = 0

    for seed in seeds do
        let f = orderCase seed
        let rng = System.Random(seed + 11)
        let name j = fst (List.item j f.Cols)

        let spec =
            { PartitionBy = [ for _ in 1 .. rng.Next 3 -> name (rng.Next 5) ]
              OrderBy =
                orderKeysOf rng
                |> List.map (fun (ci, dir) -> name ci, dir)
                |> List.truncate (rng.Next 3)
              Fn = windowFns[rng.Next windowFns.Length]
              Of = name (rng.Next 5)
              As = "w" }

        let rows = Frame.rowsOf f
        let expected = referenceWindow f.Cols rows spec
        let phys = Frame.physical f

        match DataFrame.windowColumnOf perturbation f.Cols (fun ci -> f.Vecs[ci]) phys spec with
        | Error e -> failures.Add(sprintf "seed %d %A: refused %A" seed spec e)
        | Ok(_, column) ->
            cases <- cases + 1
            let got = Array.init rows.Length (DataFrame.windowCellAt column)

            if not (Array.forall2 sameCell got expected) then
                failures.Add(sprintf "seed %d %A: frame path %A, reference %A" seed spec got expected)

            if perturbation = DataFrame.Ordering.Exact then
                match DataFrame.windowStep f.Cols (rows |> Array.map List.ofArray |> List.ofArray) spec with
                | Error e -> failures.Add(sprintf "seed %d %A: the row form refused %A" seed spec e)
                | Ok(_, outRows) ->
                    let col = outRows |> List.map List.last |> Array.ofList

                    if not (Array.forall2 sameCell col expected) then
                        failures.Add(sprintf "seed %d %A: row form %A, reference %A" seed spec col expected)

    List.ofSeq failures, cases

/// Sort the logical rows of a one-column table under `dir` and read the column back.
let private sortedColumn (ty: ColumnType) (cells: Cell list) (dir: SortDir) : Cell list =
    DataFrame.evalPipeline
        [ Transform.sortBy [ "x", dir ] ]
        { Schema = [ "x", ty ]
          Columns = [ col "x" ty cells ] }
    |> okTable
    |> cellsOf "x"

[<Tests>]
let orderCodeTests =
    testList
        "DataFrame — window and sort under order codes (Phase 324)"
        [ testCase
              "the coded order is the stable sort's, packed and as a tuple, and codes tie exactly where the comparator does"
          <| fun _ ->
              let failures, coded, boxed, packed =
                  orderingLaw DataFrame.Ordering.Exact (seq { 0..2999 })

              Expect.isEmpty failures "the coded order is the reference order"
              // Not vacuous: most cases are coded, some keep the comparator (a cell outside its type),
              // and the packed path is the common one.
              Expect.isGreaterThan coded 1500 "the law codes most cases"
              Expect.isGreaterThan boxed 100 "the law reaches the comparator path"
              Expect.isGreaterThan packed 1500 "the law reaches the packed path"

          testCase "the ordering law is red against a reversed tie-break"
          <| fun _ ->
              let failures, _, _, _ =
                  orderingLaw DataFrame.Ordering.TieBreakReversed (seq { 0..2999 })

              Expect.isNonEmpty failures "the law finds the reversed tie-break"

          testCase "every window function over the frame path and the row form is the reference window, bit for bit"
          <| fun _ ->
              let failures, cases = windowLaw DataFrame.Ordering.Exact (seq { 0..2999 })
              Expect.isEmpty failures "the window is the reference window"
              Expect.isGreaterThan cases 2900 "the law answers"

          testCase "the window law is red against a reversed tie-break"
          <| fun _ ->
              let failures, _ = windowLaw DataFrame.Ordering.TieBreakReversed (seq { 0..2999 })
              Expect.isNonEmpty failures "the law finds the reversed tie-break"

          testCase "sort, sort > limit and the coded order agree over the generated cases"
          <| fun _ ->
              // `evalSort` and the top-n (planned from a sort then a limit) through the evaluator, over
              // every coded case, against the reference order read through the selection.
              let mutable checkedCases = 0

              for seed in 0..999 do
                  let f = orderCase seed
                  let rng = System.Random(seed + 7)
                  let keys = orderKeysOf rng
                  let rows = Frame.rowsOf f
                  // Compared as tokens: a `NaN` cell is not structurally equal to itself.
                  let tokens (row: Cell[]) =
                      row |> Array.map Cell.token |> Array.toList

                  let expected = referenceOrder rows keys |> List.map (fun i -> tokens rows[i])
                  let by = keys |> List.map (fun (ci, dir) -> fst (List.item ci f.Cols), dir)
                  let t = Frame.toTable f
                  let keyVecs = keys |> List.map (fun (ci, dir) -> f.Vecs[ci], dir) |> List.toArray

                  match DataFrame.Ordering.codesAll keyVecs (Frame.physical f) with
                  | ValueNone -> ()
                  | ValueSome _ ->
                      checkedCases <- checkedCases + 1

                      let rowsOf (t: Table) =
                          Frame.rowsOf (Frame.ofTable t) |> Array.toList |> List.map tokens

                      let sorted = DataFrame.evalPipeline [ Transform.sortBy by ] t |> okTable
                      Expect.equal (rowsOf sorted) expected (sprintf "seed %d sort" seed)
                      let n = rng.Next 5
                      let off = rng.Next 3

                      let top =
                          DataFrame.evalPipeline [ Transform.sortBy by; Transform.limit n off ] t
                          |> okTable

                      Expect.equal
                          (rowsOf top)
                          (expected |> List.skip (min off expected.Length) |> List.truncate n)
                          (sprintf "seed %d sort > limit %d %d" seed n off)

              Expect.isGreaterThan checkedCases 400 "the sample reaches coded sorts"

          testCase "nulls sort last in both directions; NaN is the greatest float, so first descending"
          <| fun _ ->
              let xs =
                  [ Float 1.0
                    Null
                    Float nan
                    Float -0.0
                    Float 0.0
                    Float -infinity
                    Null
                    Float nan ]

              Expect.equal
                  (sortedColumn FloatType xs Asc |> List.map Cell.token)
                  ([ Float -infinity
                     Float -0.0
                     Float 0.0
                     Float 1.0
                     Float nan
                     Float nan
                     Null
                     Null ]
                   |> List.map Cell.token)
                  "ascending: -0.0 and 0.0 tie and keep their order; NaN after every value; nulls last"

              Expect.equal
                  (sortedColumn FloatType xs Desc |> List.map Cell.token)
                  ([ Float nan
                     Float nan
                     Float 1.0
                     Float -0.0
                     Float 0.0
                     Float -infinity
                     Null
                     Null ]
                   |> List.map Cell.token)
                  "descending: NaN first; the tied zeros still in frame order; nulls still last"

              Expect.equal
                  (sortedColumn StringType [ Str "b"; Null; Str "B"; Str "a"; Str "" ] Desc)
                  [ Str "b"; Str "a"; Str "B"; Str ""; Null ]
                  "strings ordinal, descending, null last"

              Expect.equal
                  (sortedColumn IntType [ Int System.Int32.MaxValue; Null; Int System.Int32.MinValue; Int 0 ] Desc)
                  [ Int System.Int32.MaxValue; Int 0; Int System.Int32.MinValue; Null ]
                  "an int key spanning int32 is ranked, not offset"

          testCase "a multi-key window over duplicate keys numbers ties in frame order, per partition"
          <| fun _ ->
              let t =
                  tbl
                      [ "p", StringType; "a", IntType; "b", StringType ]
                      [ col "p" StringType [ Str "x"; Str "y"; Str "x"; Str "x"; Null; Str "x"; Null ]
                        col "a" IntType [ Int 1; Int 1; Int 2; Int 1; Int 3; Null; Int 3 ]
                        col "b" StringType [ Str "q"; Str "q"; Str "q"; Str "q"; Null; Str "r"; Null ] ]

              let rows =
                  DataFrame.evalPipeline
                      [ Window
                            { PartitionBy = [ "p" ]
                              OrderBy = [ "a", Desc; "b", Asc ]
                              Fn = RowNumber
                              Of = "a"
                              As = "rn" } ]
                      t
                  |> okTable
                  |> cellsOf "rn"

              // Partition x: rows 0, 2, 3, 5 — a desc puts 2 first, then the tied 0 and 3 in frame
              // order, then 5 (a null `a`, last whatever the direction). Partition y: row 1. The null
              // partition: rows 4 and 6, tied on every key, in frame order.
              Expect.equal rows [ Int 2; Int 1; Int 1; Int 3; Int 1; Int 4; Int 2 ] "row numbers" ]

// ---- Phase 325: the typed row hasher, held to the two relations row by row ----
//
// Every keyed verb now hashes through one typed row hasher (`RowHash`): codes read from the typed
// vectors, a boxed column through its cells. These laws hold each verb to an ORACLE written row by
// row over the table's cells with the public statement of its relation — `joinKeysMatch` (`cellEq`
// pairwise) for the join and the pivot's on-value match, `rowTokenString` (the token) for
// `Distinct`, `Intersect`, `Except`, `GroupBy` and the pivot's index groups — over key columns drawn
// so that the vectors are of every kind (int, float, string, date, decimal, and boxed columns mixing
// `Int` with `Float` or one string under two tags), holding `Int 1` beside `Float 1.0`, both zeroes,
// `NaN` and `Null`, on one side or two. The relations differ on purpose (`Int 1` matches `Float 1.0`
// in a join and is a different value to `Distinct`), so a hasher that swapped them — or that keyed
// `Int 1` apart from `Float 1.0` in the join — fails here.

/// The key column kinds the draw mixes: each a declared type and the cells it may hold. The first
/// six pack typed; the last three hold cells outside one carrier and stay boxed. The far-apart ints
/// meet a side whose ints are close together, so a value outside a direct table's range is probed.
let private rowHashKinds: (string * ColumnType * Cell[]) list =
    [ "int", IntType, [| Int -1; Int 0; Int 1; Int 2; Null |]
      "int, far apart", IntType, [| Int System.Int32.MinValue; Int 1; Int System.Int32.MaxValue; Null |]
      "float", FloatType, [| Float -1.0; Float 0.0; Float -0.0; Float 1.0; Float 1.5; Float nan; Null |]
      "string", StringType, [| Str "a"; Str "b"; Str "1"; Null |]
      "date", DateType, [| Date "a"; Date "b"; Null |]
      "decimal", DecimalType, [| Decimal "1.50"; Decimal "1.5"; Decimal "2"; Null |]
      "int+float", FloatType, [| Int 1; Float 1.0; Int 0; Float -0.0; Float nan; Float 2.0; Null |]
      "string+date", StringType, [| Str "a"; Date "a"; Str "b"; Null |]
      "number+decimal", DecimalType, [| Int 1; Float 1.0; Decimal "1"; Decimal "1.0"; Null |] ]

/// A seeded draw: `next bound` in `[0, bound)`.
let private rowHashRng (seed: int) : int -> int =
    let mutable state = uint32 seed

    fun bound ->
        state <- state * 1664525u + 1013904223u
        int ((state >>> 8) % uint32 bound)

/// A table of `rows` rows: key columns `k0 .. k(n-1)` of the given kinds, and a payload `v` (the
/// row's ordinal plus `offset`, so every output row says which input row it came from).
let private rowHashTable
    (next: int -> int)
    (kinds: (string * ColumnType * Cell[]) list)
    (rows: int)
    (offset: int)
    : Table =
    let keys =
        kinds
        |> List.mapi (fun j (_, ty, pool) ->
            let name = "k" + string j
            name, ty, [ for _ in 1..rows -> pool[next pool.Length] ])

    { Schema = (keys |> List.map (fun (n, ty, _) -> n, ty)) @ [ "v", IntType ]
      Columns =
        (keys |> List.map (fun (n, ty, cells) -> col n ty cells))
        @ [ col "v" IntType [ for i in 0 .. rows - 1 -> Int(i + offset) ] ] }

/// A table's rows, cell by cell, in schema order.
let private rowHashRows (t: Table) : Cell list list =
    let cols = t.Schema |> List.map (fun (n, _) -> cellsOf n t)
    let n = Table.rowCount t
    [ for i in 0 .. n - 1 -> cols |> List.map (fun c -> List.item i c) ]

/// The table `rows` builds under `schema`.
let private rowHashOf (schema: Schema) (rows: Cell list list) : Table =
    tbl schema (schema |> List.mapi (fun j (n, ty) -> col n ty (rows |> List.map (List.item j))))

/// A table rendered exactly, column by column (`exactCell`).
let private rowHashRender (t: Table) =
    t.Columns |> List.map (fun c -> c.Name, c.Cells |> List.map exactCell)

/// The join a nested loop computes: `cellEq` pairwise on the key (`joinKeysMatch`), left order,
/// each left row's matches in right order, right-only rows after every left-side row.
let private oracleJoin (left: Table) (right: Table) (on: (string * string) list) (how: JoinKind) : Table =
    let li =
        on
        |> List.map (fun (l, _) -> left.Schema |> List.findIndex (fun (n, _) -> n = l))

    let ri =
        on
        |> List.map (fun (_, r) -> right.Schema |> List.findIndex (fun (n, _) -> n = r))

    let lrows = rowHashRows left
    let rrows = rowHashRows right

    let keyOf (idx: int list) (row: Cell list) =
        idx |> List.map (fun i -> List.item i row)

    let matches (l: Cell list) (r: Cell list) =
        DataFrame.joinKeysMatch (keyOf li l) (keyOf ri r)

    match how with
    | Semi
    | Anti ->
        lrows
        |> List.filter (fun l -> (rrows |> List.exists (matches l)) = (how = Semi))
        |> rowHashOf left.Schema
    | Inner
    | Left
    | Right
    | Outer ->
        let leftNames = left.Schema |> List.map fst |> Set.ofList

        let outRight =
            right.Schema
            |> List.map (fun (n, ty) -> (if Set.contains n leftNames then n + "_right" else n), ty)

        let lnulls = left.Schema |> List.map (fun _ -> Null)
        let rnulls = right.Schema |> List.map (fun _ -> Null)

        let fromLeft =
            [ for l in lrows do
                  let ms = rrows |> List.filter (matches l)

                  if List.isEmpty ms then
                      if how = Left || how = Outer then
                          yield l @ rnulls
                  else
                      for r in ms -> l @ r ]

        let rightOnly =
            if how = Right || how = Outer then
                [ for r in rrows do
                      if not (lrows |> List.exists (fun l -> matches l r)) then
                          yield lnulls @ r ]
            else
                []

        rowHashOf (left.Schema @ outRight) (fromLeft @ rightOnly)

/// `Intersect` / `Except` row by row: the left rows whose token is (or is not) among the right's.
let private oracleSetOp (keep: bool) (left: Table) (right: Table) : Table =
    let rightTokens =
        rowHashRows right |> List.map DataFrame.rowTokenString |> Set.ofList

    rowHashRows left
    |> List.filter (fun r -> Set.contains (DataFrame.rowTokenString r) rightTokens = keep)
    |> rowHashOf left.Schema

/// `Distinct` row by row: each token's first row.
let private oracleDistinct (t: Table) : Table =
    rowHashRows t
    |> List.fold
        (fun (seen, acc) r ->
            let tok = DataFrame.rowTokenString r

            if Set.contains tok seen then
                seen, acc
            else
                Set.add tok seen, r :: acc)
        (Set.empty, [])
    |> snd
    |> List.rev
    |> rowHashOf t.Schema

/// Rows grouped by the token of the cells at `idx`, groups in first-appearance order, each with its
/// first row's key cells and its rows in frame order.
let private oracleGroups (idx: int list) (rows: Cell list list) : (Cell list * Cell list list) list =
    let keyOf (r: Cell list) =
        idx |> List.map (fun i -> List.item i r)

    rows
    |> List.fold
        (fun (order: string list, groups: Map<string, Cell list * Cell list list>) r ->
            let tok = DataFrame.rowTokenString (keyOf r)

            match Map.tryFind tok groups with
            | Some(k, rs) -> order, Map.add tok (k, rs @ [ r ]) groups
            | None -> order @ [ tok ], Map.add tok (keyOf r, [ r ]) groups)
        ([], Map.empty)
    |> fun (order, groups) -> order |> List.map (fun t -> Map.find t groups)

/// `GroupBy keys` with a `Count` and a `Sum` of `v`, row by row.
let private oracleGroupBy (t: Table) (keys: string list) : Table =
    let idx =
        keys |> List.map (fun k -> t.Schema |> List.findIndex (fun (n, _) -> n = k))

    let vi = t.Schema |> List.findIndex (fun (n, _) -> n = "v")

    let agg fn cells =
        DataFrame.aggregateCells fn IntType cells
        |> Result.defaultWith (fun _ -> failtest "agg")

    oracleGroups idx (rowHashRows t)
    |> List.map (fun (k, rs) ->
        let vs = rs |> List.map (List.item vi)
        k @ [ agg Count vs; agg Sum vs ])
    |> rowHashOf (
        (keys
         |> List.map (fun k -> k, t.Schema |> List.find (fun (n, _) -> n = k) |> snd))
        @ [ "n", DataFrame.aggregateType Count IntType
            "s", DataFrame.aggregateType Sum IntType ]
    )

/// The per-pair pivot scan: index groups by token, on-values the distinct present cells sorted by
/// `cellString`, and each (group, on-value) the `Sum` of the group's `v` over the rows whose on cell
/// `cellEq`-matches the on-value.
let private oraclePivot (t: Table) (index: string list) (on: string) : Table =
    let pos name =
        t.Schema |> List.findIndex (fun (n, _) -> n = name)

    let idx = index |> List.map pos
    let oi = pos on
    let vi = pos "v"
    let rows = rowHashRows t

    let onValues =
        rows
        |> List.map (List.item oi)
        |> List.filter (fun c -> not (Cell.isNull c))
        |> List.distinct
        |> List.sortBy DataFrame.cellString

    let agg cells =
        DataFrame.aggregateCells Sum IntType cells
        |> Result.defaultWith (fun _ -> failtest "agg")

    oracleGroups idx rows
    |> List.map (fun (k, rs) ->
        k
        @ (onValues
           |> List.map (fun ov ->
               rs
               |> List.filter (fun r -> DataFrame.joinKeysMatch [ List.item oi r ] [ ov ])
               |> List.map (List.item vi)
               |> agg)))
    |> rowHashOf (
        (index
         |> List.map (fun n -> n, t.Schema |> List.find (fun (c, _) -> c = n) |> snd))
        @ (onValues
           |> List.map (fun ov -> DataFrame.cellString ov, DataFrame.aggregateType Sum IntType))
    )

/// The draws every law below runs: seeded kinds for one or two key columns on each side, sizes
/// from 0 to 24 rows, so empty sides, all-null keys and heavy duplication all occur.
let private rowHashCases (seed: int) (count: int) =
    let next = rowHashRng seed
    let kinds = List.toArray rowHashKinds

    [ for c in 1..count ->
          let width = 1 + next 2
          let lk = List.init width (fun _ -> kinds[next kinds.Length])
          let rk = List.init width (fun _ -> kinds[next kinds.Length])
          c, lk, rk, rowHashTable next lk (next 25) 0, rowHashTable next rk (next 25) 1000 ]

let private kindNames (ks: (string * ColumnType * Cell[]) list) =
    ks |> List.map (fun (n, _, _) -> n) |> String.concat ","

[<Tests>]
let typedRowHashTests =
    testList
        "DataFrame.TypedRowHash"
        [ testCase "every join kind equals the nested loop over cellEq, across key kinds, NaN, -0.0 and Null"
          <| fun _ ->
              for c, lk, rk, left, right in rowHashCases 3251 400 do
                  let on = lk |> List.mapi (fun j _ -> "k" + string j, "k" + string j)

                  for how in [ Inner; Left; Right; Outer; Semi; Anti ] do
                      let actual =
                          DataFrame.evalPipeline [ Join(Embedded right, on, how) ] left |> okTable

                      Expect.equal
                          (rowHashRender actual)
                          (rowHashRender (oracleJoin left right on how))
                          (sprintf
                              "case %d, %A join, left keys [%s], right keys [%s]"
                              c
                              how
                              (kindNames lk)
                              (kindNames rk))

          testCase "a join keys Int 1 to Float 1.0 and NaN to NaN, and no Null to anything"
          <| fun _ ->
              // The relation's edges in one fixture, typed on both sides (an int vector against a
              // float vector) and boxed on one: a hasher that keys the int arm apart from the float
              // arm drops the first two matches.
              let left =
                  tbl
                      [ "k", IntType; "v", IntType ]
                      [ col "k" IntType [ Int 1; Int 0; Null; Int 2 ]
                        col "v" IntType [ Int 1; Int 2; Int 3; Int 4 ] ]

              let right =
                  tbl
                      [ "k", FloatType; "w", IntType ]
                      [ col "k" FloatType [ Float 1.0; Float -0.0; Null; Float nan ]
                        col "w" IntType [ Int 10; Int 20; Int 30; Int 40 ] ]

              let inner =
                  DataFrame.evalPipeline [ Join(Embedded right, [ "k", "k" ], Inner) ] left
                  |> okTable

              Expect.equal
                  (cellsOf "v" inner)
                  [ Int 1; Int 2 ]
                  "Int 1 meets Float 1.0, Int 0 meets -0.0, Null meets nothing"

              Expect.equal (cellsOf "w" inner) [ Int 10; Int 20 ] "the right rows matched"

              let nanSide =
                  tbl
                      [ "k", FloatType; "v", IntType ]
                      [ col "k" FloatType [ Float nan; Null ]; col "v" IntType [ Int 1; Int 2 ] ]

              let nanJoin =
                  DataFrame.evalPipeline [ Join(Embedded right, [ "k", "k" ], Inner) ] nanSide
                  |> okTable

              Expect.equal (cellsOf "w" nanJoin) [ Int 40 ] "NaN meets NaN; Null meets nothing, not even Null"

          testCase "Intersect, Except and Distinct equal the token relation row by row"
          <| fun _ ->
              for c, lk, _, left, right0 in rowHashCases 3252 400 do
                  // The set operations need the left's column names: the other side is drawn over the
                  // SAME key kinds half the time and over others the rest, renamed to match.
                  let right =
                      { right0 with
                          Schema =
                              right0.Schema
                              |> List.mapi (fun j (_, ty) -> (List.item j left.Schema |> fst), ty)
                          Columns =
                              right0.Columns
                              |> List.mapi (fun j cl ->
                                  { cl with
                                      Name = List.item j left.Schema |> fst }) }

                  if List.length right.Schema = List.length left.Schema then
                      // Overlap: the left's own rows, appended to the right, so membership is reached.
                      let right =
                          rowHashOf right.Schema (rowHashRows right @ (rowHashRows left |> List.truncate 5))

                      for keep, verb in [ true, Intersect(Embedded right); false, Except(Embedded right) ] do
                          let actual = DataFrame.evalPipeline [ verb ] left |> okTable

                          Expect.equal
                              (rowHashRender actual)
                              (rowHashRender (oracleSetOp keep left right))
                              (sprintf
                                  "case %d, %s, keys [%s]"
                                  c
                                  (if keep then "intersect" else "except")
                                  (kindNames lk))

                  let keysOnly =
                      Project(
                          left.Schema
                          |> List.filter (fun (n, _) -> n <> "v")
                          |> List.map (fun (n, _) -> n, n)
                      )

                  let projected = DataFrame.evalPipeline [ keysOnly ] left |> okTable

                  Expect.equal
                      (rowHashRender (DataFrame.evalPipeline [ Distinct ] projected |> okTable))
                      (rowHashRender (oracleDistinct projected))
                      (sprintf "case %d, distinct, keys [%s]" c (kindNames lk))

          testCase "GroupBy and Pivot partition by the token and match on-values by cellEq, row by row"
          <| fun _ ->
              for c, lk, rk, left, right in rowHashCases 3253 300 do
                  let keys = lk |> List.mapi (fun j _ -> "k" + string j)

                  Expect.equal
                      (rowHashRender (
                          DataFrame.evalPipeline
                              [ GroupBy(
                                    keys,
                                    [ { Name = "n"; Fn = Count; Of = "v" }; { Name = "s"; Fn = Sum; Of = "v" } ]
                                ) ]
                              left
                          |> okTable
                      ))
                      (rowHashRender (oracleGroupBy left keys))
                      (sprintf "case %d, group by [%s]" c (kindNames lk))

                  // The pivot: the left's keys as the index, a column of the right's first kind as
                  // the on column (so on-values mix kinds the index does not).
                  let onCol = (List.head right.Columns).Cells |> List.truncate (Table.rowCount left)

                  let onCells = onCol @ List.replicate (Table.rowCount left - List.length onCol) Null

                  let _, onTy, _ = List.head rk

                  let pivotSrc =
                      { left with
                          Schema = left.Schema @ [ "o", onTy ]
                          Columns = left.Columns @ [ col "o" onTy onCells ] }

                  Expect.equal
                      (rowHashRender (
                          DataFrame.evalPipeline
                              [ Pivot
                                    { Index = keys
                                      On = "o"
                                      Values = "v"
                                      Agg = Sum } ]
                              pivotSrc
                          |> okTable
                      ))
                      (rowHashRender (oraclePivot pivotSrc keys "o"))
                      (sprintf "case %d, pivot index [%s] on %s" c (kindNames lk) (kindNames [ List.head rk ])) ]
