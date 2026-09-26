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

              match ColumnOps.invert (ApplyTransform [ Distinct ]) baseTable with
              | Error(NotInvertible "ApplyTransform") -> ()
              | other -> failtestf "expected NotInvertible, got %A" other

              // Unconditionally, and that is why both answer BEFORE the `canApply` guard: an
              // `AppendRows` the table refuses still has no inverse, and saying so must not depend
              // on running the op.
              match ColumnOps.invert (AppendRows [ [ "nope", Int 1 ] ]) baseTable with
              | Error(NotInvertible "AppendRows") -> ()
              | other -> failtestf "expected NotInvertible for a refused AppendRows, got %A" other

          // ---- Phase 181: invert is guarded by canApply ----
          testCase "a REFUSED insert has no inverse — its rejection is returned, not a live remove"
          <| fun _ ->
              // The Phase 176 finding, closed. Before Phase 181 this answered `Ok(RemoveColumn "a")`
              // — a remove that SUCCEEDS at the pre-state and takes the column that was already
              // there, so an undo stack recording `invert op pre` beside every op it attempted lost
              // a column the refused insert never touched.
              let dup = InsertColumn(0, Column.create "a" IntType [ Int 9; Int 9; Int 9 ])
              Expect.equal (ColumnOps.apply dup baseTable) (Error(DuplicateColumn "a")) "the insert is refused"
              Expect.equal (ColumnOps.invert dup baseTable) (Error(DuplicateColumn "a")) "and so is its inverse"

          testCase "the guard is the refusing rejection, on every invertible clause"
          <| fun _ ->
              // Not only `InsertColumn`. `SetCell` and `SetColumn` read the pre-state for the column
              // and the row but never for the VALUE, so a wrong-typed cell or a wrong-length column
              // — both of which `apply` refuses — had an inverse too.
              let cases =
                  [ SetCell("a", 0, Str "x"), CellTypeMismatch("a", "int", "string")
                    SetCell("nope", 0, Int 1), NoSuchColumn("nope", [ "a"; "b" ])
                    SetCell("a", 9, Int 1), RowOutOfRange(9, 3)
                    SetColumn(Column.create "b" IntType [ Int 0 ]), ColumnLengthMismatch("b", 3, 1)
                    InsertColumn(0, Column.create "z" IntType [ Int 0 ]), ColumnLengthMismatch("z", 3, 1)
                    RemoveColumn "nope", NoSuchColumn("nope", [ "a"; "b" ]) ]

              for op, rejection in cases do
                  Expect.equal (ColumnOps.apply op baseTable) (Error rejection) (sprintf "apply refuses %A" op)

                  Expect.equal
                      (ColumnOps.invert op baseTable)
                      (Error rejection)
                      (sprintf "invert refuses %A with the SAME rejection" op)

          testCase "the guard does not narrow the accepted cases — an applicable op still inverts"
          <| fun _ ->
              // The other direction, so a guard that refused everything could not pass: every op
              // `apply` accepts still yields its inverse.
              let accepted =
                  [ SetCell("a", 2, Int 42)
                    SetColumn(Column.create "b" IntType [ Int 0; Int 0; Int 0 ])
                    InsertColumn(1, Column.create "m" IntType [ Int 7; Int 8; Int 9 ])
                    RemoveColumn "a" ]

              for op in accepted do
                  Expect.isTrue (Result.isOk (ColumnOps.apply op baseTable)) (sprintf "apply accepts %A" op)
                  Expect.isTrue (Result.isOk (ColumnOps.invert op baseTable)) (sprintf "invert answers for %A" op)

          testCase "the inverse-only-for-applicable law goes RED on the pre-Phase-181 clause"
          <| fun _ ->
              // The go-red, through the kit's own injectable seam: hand `columnarOpLawsWith` the
              // clause as it stood — `InsertColumn` answering `RemoveColumn col.Name` without
              // reading the pre-state — and the law must lose. A law that cannot go red on the
              // code it was written against certifies nothing.
              let preFixInvert (op: ColumnOp) (t: Table) : Result<ColumnOp, ColumnRejection> =
                  match op with
                  | InsertColumn(_, col) -> Ok(RemoveColumn col.Name)
                  | _ -> ColumnOps.invert op t

              let lawNamed name (rs: LawResult list) = rs |> List.find (fun r -> r.Law = name)

              let law = "columnar inverse exists only for an applicable op"

              let shipped =
                  Conformance.columnarOpLawsWith ColumnOps.invert Conformance.columnarOpStreamGen 4242 200

              let preFix =
                  Conformance.columnarOpLawsWith preFixInvert Conformance.columnarOpStreamGen 4242 200

              Expect.isTrue (lawNamed law shipped).Passed "the shipped invert satisfies the law"
              Expect.isFalse (lawNamed law preFix).Passed "the pre-181 clause does NOT"

              match (lawNamed law preFix).Counterexample with
              | Some c ->
                  Expect.stringContains c "REFUSED" "the counterexample says the op was refused"
                  Expect.stringContains c "still has an inverse" "and that it had an inverse anyway"
              | None -> failtest "a failing law must carry its counterexample"

              // and the SAME injection leaves every other law green — the seam is narrow, so a red
              // here is about the guard and not about the family
              for r in preFix do
                  if r.Law <> law then
                      Expect.isTrue r.Passed (sprintf "%s is unaffected by the injection" r.Law)

          testCase "the refusal population is guarded — the law cannot be certified vacuously"
          <| fun _ ->
              // Phase 121's guard, on this family. The law above is about refused ops, so a run
              // that refuses no invertible op certifies nothing by it; the family says that rather
              // than reporting a hollow green.
              let adequacy =
                  Conformance.columnarOpLaws 4242 200
                  |> List.filter (fun r -> r.Law.StartsWith "sample adequacy")

              Expect.equal (List.length adequacy) 1 "the family emits exactly one adequacy law"
              Expect.isTrue (List.head adequacy).Passed "and the shipped generator reaches the population"

              // its teeth: a generator that never refuses an invertible op must fail it
              let hollow =
                  SampleAdequacy.reached
                      "columnarOpLaws"
                      "invert's refusal population"
                      4242
                      [ "refused invertible op", 0 ]

              Expect.isFalse hollow.Passed "a run refusing no invertible op is not adequate for the law"

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
                    ApplyTransform [ Transform.sortBy [ "a", Asc ]; Distinct ] ]

              for op in ops do
                  match ColumnOps.decode (ColumnOps.encode op) with
                  | Ok op2 -> Expect.equal op2 op (sprintf "round-trip %A" op)
                  | Error m -> failtestf "decode failed for %A: %s" op m

          // ---- conformance law ----
          testCase "columnarOpLaws certify totality + canApply + invert + chain + replay (Phase 31)"
          <| fun _ ->
              let results = Conformance.columnarOpLaws 4242 200

              Expect.equal
                  (List.length results)
                  7
                  "totality + equivalence + inversion + inverse-only-for-applicable + verify + replay + adequacy"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "columnarOpLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.columnarOpLaws 4242 200) results "same seed ⇒ identical report" ]
