module Fuaran.Core.Tests.ColumnOpsTests

open Expecto
open Fuaran.Core

// ---- fixtures ----

let private baseTable: Table =
    { Schema = [ "a", IntType; "b", IntType ]
      Columns =
        [ Column.create "a" IntType [ Int 1; Int 2; Int 3 ]
          Column.create "b" IntType [ Int 4; Int 5; Int 6 ] ] }

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

[<Tests>]
let tests =
    testList
        "ColumnOps"
        [ // ---- apply happy paths ----
          testCase "SetCell replaces one cell"
          <| fun _ ->
              let t = ok (ColumnOps.apply (SetCell("a", 1, Int 99)) baseTable)

              Expect.equal
                  (Table.tryColumn "a" t |> Option.map (fun c -> c.Cells))
                  (Some [ Int 1; Int 99; Int 3 ])
                  "cell set"

          testCase "InsertColumn / RemoveColumn add and drop a column"
          <| fun _ ->
              let added =
                  ok (ColumnOps.apply (InsertColumn(1, Column.create "m" IntType [ Int 7; Int 8; Int 9 ])) baseTable)

              Expect.equal (Table.columnNames added) [ "a"; "m"; "b" ] "inserted at index 1"
              let dropped = ok (ColumnOps.apply (RemoveColumn "a") added)
              Expect.equal (Table.columnNames dropped) [ "m"; "b" ] "removed a"

          testCase "AppendRows extends every column (missing cell -> Null)"
          <| fun _ ->
              let t = ok (ColumnOps.apply (AppendRows [ [ "a", Int 10 ] ]) baseTable)
              Expect.equal (Table.rowCount t) 4 "one row appended"
              Expect.equal (Table.tryColumn "a" t |> Option.map (fun c -> List.last c.Cells)) (Some(Int 10)) "a got 10"
              Expect.equal (Table.tryColumn "b" t |> Option.map (fun c -> List.last c.Cells)) (Some Null) "b got Null"

          testCase "ApplyTransform applies a DataFrame pipeline as one op"
          <| fun _ ->
              let t =
                  ok (ColumnOps.apply (ApplyTransform [ Filter(Binary(Gt, Col "a", Lit(Int 1))) ]) baseTable)

              Expect.equal (Table.rowCount t) 2 "rows with a>1 kept"

          // ---- rejections ----
          testCase "SetCell on a missing column / out-of-range row is a named rejection"
          <| fun _ ->
              match ColumnOps.apply (SetCell("nope", 0, Int 1)) baseTable with
              | Error(NoSuchColumn("nope", _)) -> ()
              | other -> failtestf "expected NoSuchColumn, got %A" other

              match ColumnOps.apply (SetCell("a", 9, Int 1)) baseTable with
              | Error(RowOutOfRange(9, 3)) -> ()
              | other -> failtestf "expected RowOutOfRange, got %A" other

          testCase "SetCell with a wrong-typed value is a CellTypeMismatch"
          <| fun _ ->
              match ColumnOps.apply (SetCell("a", 0, Str "x")) baseTable with
              | Error(CellTypeMismatch("a", "int", "string")) -> ()
              | other -> failtestf "expected CellTypeMismatch, got %A" other

          testCase "InsertColumn with a duplicate name / wrong length is rejected"
          <| fun _ ->
              match ColumnOps.apply (InsertColumn(0, Column.create "a" IntType [ Int 0; Int 0; Int 0 ])) baseTable with
              | Error(DuplicateColumn "a") -> ()
              | other -> failtestf "expected DuplicateColumn, got %A" other

              match ColumnOps.apply (InsertColumn(0, Column.create "z" IntType [ Int 0 ])) baseTable with
              | Error(ColumnLengthMismatch("z", 3, 1)) -> ()
              | other -> failtestf "expected ColumnLengthMismatch, got %A" other

          // ---- canApply ≡ apply ----
          testCase "canApply agrees with apply (accept and reject)"
          <| fun _ ->
              Expect.equal (ColumnOps.canApply (SetCell("a", 0, Int 5)) baseTable) (Ok()) "valid op accepted"

              match ColumnOps.canApply (RemoveColumn "nope") baseTable with
              | Error(NoSuchColumn _) -> ()
              | other -> failtestf "expected NoSuchColumn from canApply, got %A" other

          // ---- invert ----
          testCase "apply ∘ invert = identity for the structural ops"
          <| fun _ ->
              let check op =
                  let post = ok (ColumnOps.apply op baseTable)
                  let inv = ok (ColumnOps.invert op baseTable)
                  Expect.equal (ColumnOps.apply inv post) (Ok baseTable) (sprintf "invert restores %A" op)

              check (SetCell("a", 2, Int 42))
              check (SetColumn(Column.create "b" IntType [ Int 0; Int 0; Int 0 ]))
              check (InsertColumn(1, Column.create "m" IntType [ Int 7; Int 8; Int 9 ]))
              check (RemoveColumn "a")

          testCase "AppendRows / ApplyTransform are NotInvertible"
          <| fun _ ->
              match ColumnOps.invert (AppendRows [ [ "a", Int 1 ] ]) baseTable with
              | Error(NotInvertible "AppendRows") -> ()
              | other -> failtestf "expected NotInvertible, got %A" other

          // ---- Diff ----
          testCase "Diff.toOps reconstructs after from before (same-schema cell change)"
          <| fun _ ->
              let after = ok (ColumnOps.apply (SetCell("a", 0, Int 100)) baseTable)
              let ops = ColumnOps.toOps baseTable after
              Expect.equal (ColumnOps.applyAll ops baseTable) (Ok after) "applyAll(toOps) = after"

          testCase "Diff.toOps reconstructs after from before (schema rebuild)"
          <| fun _ ->
              let after =
                  { Schema = [ "x", StringType; "y", IntType ]
                    Columns =
                      [ Column.create "x" StringType [ Str "p"; Str "q" ]
                        Column.create "y" IntType [ Int 1; Int 2 ] ] }

              let ops = ColumnOps.toOps baseTable after
              Expect.equal (ColumnOps.applyAll ops baseTable) (Ok after) "rebuild reconstructs a different-shape table"

          // ---- codec ----
          testCase "every op round-trips through the wire codec"
          <| fun _ ->
              let ops =
                  [ SetCell("a", 1, Int 9)
                    SetColumn(Column.create "b" IntType [ Int 0; Int 0; Int 0 ])
                    InsertColumn(0, Column.create "m" IntType [ Null; Int 2; Int 3 ])
                    RemoveColumn "a"
                    AppendRows [ [ "a", Int 1; "b", Null ] ]
                    ApplyTransform [ Sort [ "a", Asc ]; Distinct ] ]

              for op in ops do
                  match ColumnOps.decode (ColumnOps.encode op) with
                  | Ok op2 -> Expect.equal op2 op (sprintf "round-trip %A" op)
                  | Error m -> failtestf "decode failed for %A: %s" op m

          // ---- conformance law ----
          testCase "columnarOpLaws certify totality + canApply + invert + chain + replay (Phase 31)"
          <| fun _ ->
              let results = Conformance.columnarOpLaws 4242 200
              Expect.equal (List.length results) 5 "totality + equivalence + inversion + verify + replay"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "columnarOpLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.columnarOpLaws 4242 200) results "same seed ⇒ identical report" ]
