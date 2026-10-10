module Fuaran.Compute.Tests.ColumnOpsTests

open Expecto
open Fuaran.Core
open Fuaran.Compute
open Fuaran.Compute.Tests

// ---- fixtures ----

let private baseTable: Table =
    { Schema = [ Field.create "a" IntType; Field.create "b" IntType ]
      Columns =
        [ KitColumn.create "a" IntType [ Int 1; Int 2; Int 3 ]
          KitColumn.create "b" IntType [ Int 4; Int 5; Int 6 ] ] }

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
                  (Table.tryColumn "a" t |> Option.map (fun c -> (Column.toCells c)))
                  (Some [ Int 1; Int 99; Int 3 ])
                  "cell set"

          testCase "InsertColumn / RemoveColumn add and drop a column"
          <| fun _ ->
              let added =
                  ok (ColumnOps.apply (InsertColumn(1, KitColumn.create "m" IntType [ Int 7; Int 8; Int 9 ])) baseTable)

              Expect.equal (Table.columnNames added) [ "a"; "m"; "b" ] "inserted at index 1"
              let dropped = ok (ColumnOps.apply (RemoveColumn "a") added)
              Expect.equal (Table.columnNames dropped) [ "m"; "b" ] "removed a"

          testCase "AppendRows extends every column (missing cell -> Null)"
          <| fun _ ->
              let t = ok (ColumnOps.apply (AppendRows [ [ "a", Int 10 ] ]) baseTable)
              Expect.equal (Table.rowCount t) 4 "one row appended"

              Expect.equal
                  (Table.tryColumn "a" t |> Option.map (fun c -> List.last (Column.toCells c)))
                  (Some(Int 10))
                  "a got 10"

              Expect.equal
                  (Table.tryColumn "b" t |> Option.map (fun c -> List.last (Column.toCells c)))
                  (Some Null)
                  "b got Null"

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
              match
                  ColumnOps.apply (InsertColumn(0, KitColumn.create "a" IntType [ Int 0; Int 0; Int 0 ])) baseTable
              with
              | Error(DuplicateColumn "a") -> ()
              | other -> failtestf "expected DuplicateColumn, got %A" other

              match ColumnOps.apply (InsertColumn(0, KitColumn.create "z" IntType [ Int 0 ])) baseTable with
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
              check (SetColumn(KitColumn.create "b" IntType [ Int 0; Int 0; Int 0 ]))
              check (InsertColumn(1, KitColumn.create "m" IntType [ Int 7; Int 8; Int 9 ]))
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
              let dup = InsertColumn(0, KitColumn.create "a" IntType [ Int 9; Int 9; Int 9 ])
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
                    SetColumn(KitColumn.create "b" IntType [ Int 0 ]), ColumnLengthMismatch("b", 3, 1)
                    InsertColumn(0, KitColumn.create "z" IntType [ Int 0 ]), ColumnLengthMismatch("z", 3, 1)
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
                    SetColumn(KitColumn.create "b" IntType [ Int 0; Int 0; Int 0 ])
                    InsertColumn(1, KitColumn.create "m" IntType [ Int 7; Int 8; Int 9 ])
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

              // Phase 321 added the second: the decimal cells the kit's roll writes.
              Expect.equal (List.length adequacy) 2 "the family emits two adequacy laws"

              for a in adequacy do
                  Expect.isTrue a.Passed (sprintf "and the shipped generator reaches the population: %s" a.Law)

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
                  { Schema = [ Field.create "x" StringType; Field.create "y" IntType ]
                    Columns =
                      [ KitColumn.create "x" StringType [ Str "p"; Str "q" ]
                        KitColumn.create "y" IntType [ Int 1; Int 2 ] ] }

              let ops = ColumnOps.toOps baseTable after
              Expect.equal (ColumnOps.applyAll ops baseTable) (Ok after) "rebuild reconstructs a different-shape table"

          // ---- codec ----
          testCase "every op round-trips through the wire codec"
          <| fun _ ->
              let ops =
                  [ SetCell("a", 1, Int 9)
                    SetColumn(KitColumn.create "b" IntType [ Int 0; Int 0; Int 0 ])
                    InsertColumn(0, KitColumn.create "m" IntType [ Null; Int 2; Int 3 ])
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
                  8
                  "totality + equivalence + inversion + inverse-only-for-applicable + verify + replay + two adequacy guards (Phase 321's decimal cell the second)"

              if results |> List.exists (fun r -> not r.Passed) then
                  let fails =
                      results
                      |> List.filter (fun r -> not r.Passed)
                      |> List.map (fun r -> sprintf "%s — %A" r.Law r.Counterexample)

                  failtestf "columnarOpLaws failed:\n%s" (String.concat "\n" fails)

              Expect.equal (Conformance.columnarOpLaws 4242 200) results "same seed ⇒ identical report" ]


// ---------------------------------------------------------------------------
//  Phase 268 — the ops over a prepared VERSION agree with the ops over a table.
//
//  `applyPrepared` / `canApplyPrepared` / `invertPrepared` / `deltaOfPrepared`
//  are held to `apply` / `canApply` / `invert` / `deltaOf` op by op — verdict
//  and value — over a small table and over one wide enough to span several
//  chunks, so the chunk arithmetic (a cell in the third chunk, an append that
//  crosses a chunk boundary, a column edit that moves cells in two chunks) is
//  exercised. That agreement is what ties the prepared forms to the proved
//  model: the oracle certifies `apply` clause for clause, and these hold the
//  prepared forms to `apply`.
// ---------------------------------------------------------------------------

let private wide (n: int) : Table =
    { Schema =
        [ Field.create "id" IntType
          Field.create "s" StringType
          Field.create "f" FloatType
          Field.create "b" BoolType ]
      Columns =
        [ KitColumn.create "id" IntType [ for i in 0 .. n - 1 -> Int i ]
          KitColumn.create
              "s"
              StringType
              [ for i in 0 .. n - 1 -> (if i % 11 = 0 then Null else Str("v" + string (i % 13))) ]
          KitColumn.create "f" FloatType [ for i in 0 .. n - 1 -> Float(float i * 0.5) ]
          KitColumn.create "b" BoolType [ for i in 0 .. n - 1 -> Bool(i % 2 = 0) ] ] }

/// Ops over `t`, accepted and refused alike — one per clause and one per rejection.
let private opsOver (t: Table) : ColumnOp list =
    let n = Table.rowCount t
    let mid = n / 2
    let cells (f: int -> Cell) = [ for i in 0 .. n - 1 -> f i ]

    [ SetCell("id", mid, Int -1)
      SetCell("s", mid, Null)
      SetCell("s", 0, Str "edited")
      SetCell("f", n - 1, Float 1.25)
      SetCell("id", mid, Str "wrong")
      SetCell("id", n, Int 0)
      SetCell("id", -1, Int 0)
      SetCell("nope", 0, Int 0)
      SetColumn(
          KitColumn.create
              "f"
              FloatType
              (cells (fun i ->
                  if i = 3 || i = mid then
                      Float -1.0
                  else
                      Float(float i * 0.5)))
      )
      SetColumn(KitColumn.create "f" IntType (cells (fun i -> Int i)))
      SetColumn(KitColumn.create "f" FloatType (cells (fun i -> if i = 1 then Str "x" else Float 0.0)))
      SetColumn(KitColumn.create "f" FloatType [ Float 1.0 ])
      SetColumn(KitColumn.create "nope" FloatType (cells (fun _ -> Float 0.0)))
      InsertColumn(1, KitColumn.create "g" IntType (cells (fun i -> Int(i * 2))))
      InsertColumn(99, KitColumn.create "g" IntType (cells (fun i -> Int(i * 2))))
      InsertColumn(-5, KitColumn.create "g" IntType (cells (fun i -> Int(i * 2))))
      InsertColumn(0, KitColumn.create "id" IntType (cells (fun i -> Int i)))
      InsertColumn(0, KitColumn.create "g" IntType [ Int 1 ])
      InsertColumn(0, KitColumn.create "g" IntType (cells (fun _ -> Str "no")))
      RemoveColumn "s"
      RemoveColumn "id"
      RemoveColumn "nope"
      AppendRows
          [ [ "id", Int n; "s", Str "new" ]
            [ "id", Int(n + 1); "f", Float 2.0; "b", Bool false ] ]
      AppendRows [ for i in 0..1030 -> [ "id", Int(n + i); "f", Float(float i) ] ]
      AppendRows [ [ "id", Int n; "nope", Int 1 ] ]
      AppendRows [ [ "id", Str "wrong" ] ]
      AppendRows []
      ApplyTransform [ Filter(Binary(Ge, Col "id", Lit(Int 3))) ]
      ApplyTransform [ Derive("h", Binary(Mul, Col "f", Lit(Float 2.0))) ]
      ApplyTransform [ Filter(Col "nope") ] ]

let private idw = RowIdentity.byColumn "id"

[<Tests>]
let preparedTests =
    testList
        "ColumnOps.prepared"
        [ testCase "toTable (prepare t) is t itself"
          <| fun _ ->
              let t = wide 10

              Expect.isTrue
                  (obj.ReferenceEquals(DataFrame.toTable (DataFrame.prepare t), t))
                  "the consumer's own object"

          testCase
              "applyPrepared agrees with apply on every op, accepted or refused, over a small and a many-chunk table"
          <| fun _ ->
              for t in [ baseTable; wide 7; wide 2_600 ] do
                  let p = DataFrame.prepare t

                  for op in opsOver t do
                      Expect.equal
                          (ColumnOps.applyPrepared op p |> Result.map DataFrame.toTable)
                          (ColumnOps.apply op t)
                          (sprintf "applyPrepared = apply for %A over %d rows" op (Table.rowCount t))

                      Expect.equal
                          (ColumnOps.canApplyPrepared op p)
                          (ColumnOps.canApply op t)
                          (sprintf "canApplyPrepared = canApply for %A" op)

          testCase "invertPrepared agrees with invert, and the inverse undoes the edit on the version"
          <| fun _ ->
              for t in [ baseTable; wide 2_600 ] do
                  let p = DataFrame.prepare t

                  for op in opsOver t do
                      Expect.equal
                          (ColumnOps.invertPrepared op p)
                          (ColumnOps.invert op t)
                          (sprintf "invertPrepared = invert for %A" op)

                      match ColumnOps.invertPrepared op p, ColumnOps.applyPrepared op p with
                      | Ok inv, Ok p' ->
                          Expect.equal
                              (ColumnOps.applyPrepared inv p' |> Result.map DataFrame.toTable)
                              (Ok t)
                              (sprintf "the inverse of %A restores the table" op)
                      | _ -> ()

          testCase "deltaOfPrepared agrees with deltaOf on every op"
          <| fun _ ->
              for t in [ wide 7; wide 2_600 ] do
                  let p = DataFrame.prepare t

                  for op in opsOver t do
                      Expect.equal
                          (ColumnOps.deltaOfPrepared idw p op)
                          (ColumnOps.deltaOf idw t op)
                          (sprintf "deltaOfPrepared = deltaOf for %A" op)

              // A cell edit that moves the KEY reads as a removal and an addition, on one row.
              let t = wide 2_600
              let p = DataFrame.prepare t
              let op = SetCell("id", 2_000, Int 9_999)
              Expect.equal (ColumnOps.deltaOfPrepared idw p op) (ColumnOps.deltaOf idw t op) "a key edit"

              Expect.equal
                  (Delta.rowsWith RowRemoved (ColumnOps.deltaOfPrepared idw p op))
                  [ ByKey(DataFrame.cellToken (Int 2_000)) ]
                  "the old key removed"

          testCase "every prior version stays reachable, and a chain of edits reads as applyAll"
          <| fun _ ->
              let t = wide 2_600
              let p0 = DataFrame.prepare t

              let ops =
                  [ SetCell("f", 1_500, Float 0.0)
                    AppendRows [ [ "id", Int 2_600; "s", Str "z" ] ]
                    SetColumn(KitColumn.create "b" BoolType [ for i in 0..2_600 -> Bool(i % 3 = 0) ])
                    InsertColumn(2, KitColumn.create "g" IntType [ for i in 0..2_600 -> Int i ])
                    SetCell("g", 2_600, Int -7)
                    RemoveColumn "s" ]

              let versions = ops |> List.scan (fun p op -> ok (ColumnOps.applyPrepared op p)) p0

              Expect.equal
                  (Ok(DataFrame.toTable (List.last versions)))
                  (ColumnOps.applyAll ops t)
                  "the last version is applyAll's table"

              Expect.isTrue (obj.ReferenceEquals(DataFrame.toTable p0, t)) "the first version is untouched"

              versions
              |> List.pairwise
              |> List.iteri (fun i (before, after) ->
                  Expect.equal
                      (Ok(DataFrame.toTable after))
                      (ColumnOps.apply (List.item i ops) (DataFrame.toTable before))
                      (sprintf "version %d is apply over version %d" (i + 1) i)) ]
