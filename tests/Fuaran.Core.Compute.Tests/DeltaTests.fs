module Fuaran.Core.Tests.DeltaTests

open Expecto
open Fuaran.Core

// ---------------------------------------------------------------------------
//  Phase 98 — the typed delta representation for the column layer.
//
//  The algebra laws are pinned EXHAUSTIVELY rather than sampled: the change
//  space has four elements and the delta corpus below is small, so every triple
//  is checked rather than a random handful. A seeded generator would test fewer
//  cases and would have to be trusted to reach the awkward ones — and the
//  awkward ones here (add-then-remove, remove-then-add, cross-scheme) are
//  precisely where an associativity claim is easy to get wrong.
// ---------------------------------------------------------------------------

let private ok =
    function
    | Ok v -> v
    | Error e -> failtestf "expected Ok, got Error %A" e

let private scheme = "column:id"

let private allChanges = [ RowAdded; RowChanged; RowRemoved; RowTransient ]

/// A representative corpus: the top element, the quiet delta, key-addressed row sets covering every
/// change, a column-invalidation-only delta, an ordinal-scheme delta, and a foreign-scheme delta.
let private corpus: TableDelta list =
    [ FullRefresh
      Delta.empty scheme
      Delta.ofRows scheme [ ByKey "a", RowAdded ]
      Delta.ofRows scheme [ ByKey "a", RowRemoved ]
      Delta.ofRows scheme [ ByKey "a", RowChanged; ByKey "b", RowAdded ]
      Delta.ofRows scheme [ ByKey "b", RowRemoved; ByKey "c", RowTransient ]
      Delta.ofColumns scheme [ "amount" ]
      Delta.ofRows RowIdentity.ordinalScheme [ ByOrdinal 0, RowChanged ]
      Delta.ofRows "column:other" [ ByKey "a", RowAdded ] ]

// ---- tables for the diff leg ----

let private table (ids: Cell list) (amounts: Cell list) : Table =
    { Schema = [ "id", StringType; "amount", IntType ]
      Columns = [ Column.create "id" StringType ids; Column.create "amount" IntType amounts ] }

let private idWitness = RowIdentity.byColumn "id"

[<Tests>]
let tests =
    testList
        "Delta"
        [

          // ================= the algebra =================

          testCase "composeChange is associative over EVERY triple (4^3 = 64)"
          <| fun _ ->
              for a in allChanges do
                  for b in allChanges do
                      for c in allChanges do
                          Expect.equal
                              (Delta.composeChange (Delta.composeChange a b) c)
                              (Delta.composeChange a (Delta.composeChange b c))
                              (sprintf "associativity at (%A, %A, %A)" a b c)

          testCase "the pinned per-row composition table"
          <| fun _ ->
              // Added then Removed is absent at BOTH ends — that is what `RowTransient` names, and
              // why it exists: no other case has that (before, after) shape.
              Expect.equal (Delta.composeChange RowAdded RowRemoved) RowTransient "added ∘ removed"
              // Removed then Added is present at both ends with (possibly) different content.
              Expect.equal (Delta.composeChange RowRemoved RowAdded) RowChanged "removed ∘ added"
              Expect.equal (Delta.composeChange RowAdded RowChanged) RowAdded "added ∘ changed"
              Expect.equal (Delta.composeChange RowChanged RowRemoved) RowRemoved "changed ∘ removed"
              Expect.equal (Delta.composeChange RowChanged RowChanged) RowChanged "changed ∘ changed"
              Expect.equal (Delta.composeChange RowTransient RowAdded) RowAdded "transient ∘ added"

          testCase "compose is associative over every triple of the corpus"
          <| fun _ ->
              for a in corpus do
                  for b in corpus do
                      for c in corpus do
                          Expect.equal
                              (Delta.compose (Delta.compose a b) c)
                              (Delta.compose a (Delta.compose b c))
                              (sprintf "associativity at (%A, %A, %A)" a b c)

          testCase "full refresh absorbs on both sides"
          <| fun _ ->
              for d in corpus do
                  Expect.equal (Delta.compose FullRefresh d) FullRefresh "left-absorbing"
                  Expect.equal (Delta.compose d FullRefresh) FullRefresh "right-absorbing"

          testCase "the quiet delta is a two-sided identity within its scheme"
          <| fun _ ->
              let sameScheme =
                  corpus
                  |> List.filter (fun d ->
                      match d with
                      | RowSet r -> r.Scheme = scheme
                      | FullRefresh -> false)

              Expect.isNonEmpty sameScheme "the corpus carries same-scheme deltas"

              for d in sameScheme do
                  Expect.equal (Delta.compose (Delta.empty scheme) d) d "left identity"
                  Expect.equal (Delta.compose d (Delta.empty scheme)) d "right identity"

          testCase "composing across identity schemes degrades to the honest top, and composeChecked refuses"
          <| fun _ ->
              let a = Delta.ofRows scheme [ ByKey "a", RowAdded ]
              let b = Delta.ofRows "column:other" [ ByKey "a", RowRemoved ]

              Expect.equal (Delta.compose a b) FullRefresh "cross-scheme compose is FullRefresh"

              match Delta.composeChecked a b with
              | Error(SchemeMismatch(l, r)) ->
                  Expect.equal l scheme "names the left scheme"
                  Expect.equal r "column:other" "names the right scheme"
              | other -> failtestf "expected SchemeMismatch, got %A" other

          testCase "compose merges row sets and unions column invalidation"
          <| fun _ ->
              let a =
                  RowSet
                      { Scheme = scheme
                        Rows = [ ByKey "a", RowAdded; ByKey "b", RowChanged ]
                        InvalidatedColumns = [ "amount" ] }

              let b =
                  RowSet
                      { Scheme = scheme
                        Rows = [ ByKey "b", RowRemoved; ByKey "c", RowAdded ]
                        InvalidatedColumns = [ "amount"; "total" ] }

              match Delta.compose a b with
              | RowSet r ->
                  Expect.equal
                      r.Rows
                      [ ByKey "a", RowAdded; ByKey "b", RowRemoved; ByKey "c", RowAdded ]
                      "rows merged in canonical order"

                  Expect.equal r.InvalidatedColumns [ "amount"; "total" ] "columns unioned, deduplicated, sorted"
              | other -> failtestf "expected a RowSet, got %A" other

          testCase "composeAll folds earliest-first"
          <| fun _ ->
              let steps =
                  [ Delta.ofRows scheme [ ByKey "a", RowAdded ]
                    Delta.ofRows scheme [ ByKey "a", RowChanged ]
                    Delta.ofRows scheme [ ByKey "a", RowRemoved ] ]

              Expect.equal
                  (Delta.composeAll scheme steps)
                  (Delta.ofRows scheme [ ByKey "a", RowTransient ])
                  "add → change → remove is transient"

          testCase "normalise puts rows and columns in canonical order (keys before ordinals)"
          <| fun _ ->
              let messy =
                  RowSet
                      { Scheme = scheme
                        Rows = [ ByKey "c", RowAdded; ByKey "a", RowRemoved ]
                        InvalidatedColumns = [ "z"; "a" ] }

              match Delta.normalise messy with
              | RowSet r ->
                  Expect.equal (r.Rows |> List.map fst) [ ByKey "a"; ByKey "c" ] "rows ordinally sorted"
                  Expect.equal r.InvalidatedColumns [ "a"; "z" ] "columns ordinally sorted"
              | other -> failtestf "expected a RowSet, got %A" other

          // ================= totality / validation =================

          testCase "a duplicate row is a named defect, and the delta is refused WHOLE"
          <| fun _ ->
              let d = Delta.ofRows scheme [ ByKey "a", RowAdded; ByKey "a", RowRemoved ]

              match Delta.validate d with
              | Error(DuplicateRow r) -> Expect.equal r "k:a" "names the offending row token"
              | other -> failtestf "expected DuplicateRow, got %A" other

              // and nothing composes out of it — no partial application
              match Delta.composeChecked d (Delta.empty scheme) with
              | Error(DuplicateRow _) -> ()
              | other -> failtestf "expected the defect to propagate, got %A" other

          testCase "ordinals are refused in an identity-bearing scheme, and keys in the ordinal scheme"
          <| fun _ ->
              match Delta.validate (Delta.ofRows scheme [ ByOrdinal 2, RowChanged ]) with
              | Error(MixedAddressing(s, r)) ->
                  Expect.equal s scheme "names the scheme"
                  Expect.equal r "o:2" "names the ref"
              | other -> failtestf "expected MixedAddressing, got %A" other

              match Delta.validate (Delta.ofRows RowIdentity.ordinalScheme [ ByKey "a", RowChanged ]) with
              | Error(MixedAddressing _) -> ()
              | other -> failtestf "expected MixedAddressing the other way, got %A" other

          testCase "the remaining shape defects are named"
          <| fun _ ->
              Expect.equal (Delta.validate (Delta.empty "")) (Error EmptyScheme) "unnamed scheme"

              Expect.equal
                  (Delta.validate (Delta.ofRows scheme [ ByKey "", RowAdded ]))
                  (Error EmptyRowKey)
                  "empty row key"

              Expect.equal
                  (Delta.validate (Delta.ofRows RowIdentity.ordinalScheme [ ByOrdinal -1, RowAdded ]))
                  (Error(NegativeOrdinal -1))
                  "negative ordinal"

              Expect.equal (Delta.validate (Delta.ofColumns scheme [ "" ])) (Error EmptyColumnName) "empty column name"

              Expect.equal
                  (Delta.validate (Delta.ofColumns scheme [ "a"; "a" ]))
                  (Error(DuplicateInvalidatedColumn "a"))
                  "duplicate invalidated column"

          testCase "defects enumerates every fault, and a well-formed delta has none"
          <| fun _ ->
              let bad =
                  RowSet
                      { Scheme = ""
                        Rows = [ ByOrdinal -1, RowAdded; ByOrdinal -1, RowRemoved ]
                        InvalidatedColumns = [ "x"; "x" ] }

              let found = Delta.defects bad
              Expect.isGreaterThan (List.length found) 3 "several distinct defects reported at once"
              Expect.contains found EmptyScheme "scheme fault present"
              Expect.contains found (DuplicateRow "o:-1") "duplicate fault present"

              for d in corpus do
                  Expect.equal (Delta.defects d) [] (sprintf "corpus member is well-formed: %A" d)

          testCase "every defect renders a stable non-empty string"
          <| fun _ ->
              let every =
                  [ EmptyScheme
                    EmptyRowKey
                    NegativeOrdinal -1
                    DuplicateRow "k:a"
                    MixedAddressing(scheme, "o:1")
                    EmptyColumnName
                    DuplicateInvalidatedColumn "a"
                    SchemeMismatch("a", "b")
                    MissingIdentity(scheme, 3)
                    DuplicateIdentity(scheme, "s:a") ]

              for e in every do
                  Expect.isGreaterThan (Delta.defectString e).Length 0 (sprintf "renders %A" e)

          // ================= wire =================

          testCase "the pinned canonical bytes"
          <| fun _ ->
              Expect.equal (DeltaCodec.encode FullRefresh) """{"$type":"fullRefresh"}""" "full refresh"

              Expect.equal
                  (DeltaCodec.encode (Delta.empty scheme))
                  """{"$type":"rowSet","columns":[],"rows":[],"scheme":"column:id"}"""
                  "the quiet delta"

              Expect.equal
                  (DeltaCodec.encode (
                      RowSet
                          { Scheme = scheme
                            Rows = [ ByKey "b", RowRemoved; ByKey "a", RowAdded ]
                            InvalidatedColumns = [ "amount" ] }
                  ))
                  """{"$type":"rowSet","columns":["amount"],"rows":[{"$type":"added","key":"a"},{"$type":"removed","key":"b"}],"scheme":"column:id"}"""
                  "a key-addressed row set, canonically ordered on encode"

              Expect.equal
                  (DeltaCodec.encode (
                      Delta.ofRows RowIdentity.ordinalScheme [ ByOrdinal 2, RowChanged; ByOrdinal 0, RowTransient ]
                  ))
                  """{"$type":"rowSet","columns":[],"rows":[{"$type":"transient","ordinal":0},{"$type":"changed","ordinal":2}],"scheme":"ordinal"}"""
                  "an ordinal-addressed row set"

          testCase "every corpus member round-trips, and re-encodes byte-identically"
          <| fun _ ->
              for d in corpus do
                  let once = DeltaCodec.encode d
                  let back = ok (DeltaCodec.decode once)
                  Expect.equal back (Delta.normalise d) (sprintf "decode reproduces %A" d)
                  Expect.equal (DeltaCodec.encode back) once "re-encode is byte-identical"

          testCase "encode is order-insensitive — two spellings of one delta are the same bytes"
          <| fun _ ->
              let a =
                  RowSet
                      { Scheme = scheme
                        Rows = [ ByKey "a", RowAdded; ByKey "b", RowRemoved ]
                        InvalidatedColumns = [ "x"; "y" ] }

              let b =
                  RowSet
                      { Scheme = scheme
                        Rows = [ ByKey "b", RowRemoved; ByKey "a", RowAdded ]
                        InvalidatedColumns = [ "y"; "x" ] }

              Expect.equal (DeltaCodec.encode a) (DeltaCodec.encode b) "canonical bytes either way"

          testCase "decode refuses malformed wire with the six-code envelope"
          <| fun _ ->
              match DeltaCodec.decode "not json at all" with
              | Error(NotJson _) -> ()
              | other -> failtestf "expected NotJson, got %A" other

              match DeltaCodec.decode """{"$type":"partialRefresh"}""" with
              | Error(UnknownType(got, expected)) ->
                  Expect.equal got "partialRefresh" "names the tag"
                  Expect.equal expected [ "fullRefresh"; "rowSet" ] "enumerates the alternatives"
              | other -> failtestf "expected UnknownType, got %A" other

              match DeltaCodec.decode """{"$type":"rowSet","rows":[],"columns":[]}""" with
              | Error(MissingField "scheme") -> ()
              | other -> failtestf "expected MissingField scheme, got %A" other

              match
                  DeltaCodec.decode
                      """{"$type":"rowSet","scheme":"column:id","columns":[],"rows":[{"$type":"moved","key":"a"}]}"""
              with
              | Error(UnknownType(got, expected)) ->
                  Expect.equal got "moved" "names the change tag"
                  Expect.equal expected [ "added"; "changed"; "removed"; "transient" ] "enumerates the change set"
              | other -> failtestf "expected UnknownType for the change tag, got %A" other

              match
                  DeltaCodec.decode
                      """{"$type":"rowSet","scheme":"column:id","columns":[],"rows":[{"$type":"added","key":"a","ordinal":1}]}"""
              with
              | Error(MalformedShape _) -> ()
              | other -> failtestf "expected MalformedShape for key+ordinal, got %A" other

              match
                  DeltaCodec.decode
                      """{"$type":"rowSet","scheme":"column:id","columns":[],"rows":[{"$type":"added"}]}"""
              with
              | Error(MissingField "key") -> ()
              | other -> failtestf "expected MissingField key, got %A" other

          testCase "decode refuses a structurally-valid but INCONSISTENT delta, naming the defect"
          <| fun _ ->
              // Decodes as JSON, decodes as a row set, and is not a delta: the same row twice.
              let dup =
                  """{"$type":"rowSet","scheme":"column:id","columns":[],"rows":[{"$type":"added","key":"a"},{"$type":"removed","key":"a"}]}"""

              match DeltaCodec.decode dup with
              | Error(Malformed detail) -> Expect.stringContains detail "k:a" "the envelope names the offending row"
              | other -> failtestf "expected Malformed, got %A" other

              // An ordinal ref under an identity scheme — the addressing rule, enforced on the wire too.
              let mixed =
                  """{"$type":"rowSet","scheme":"column:id","columns":[],"rows":[{"$type":"added","ordinal":0}]}"""

              match DeltaCodec.decode mixed with
              | Error(Malformed detail) ->
                  Expect.stringContains detail "ordinals only where no identity exists" "explains the rule"
              | other -> failtestf "expected Malformed, got %A" other

          // ================= the Change bridge =================

          testCase "ofChange lifts only what Change can locate; the rest is the honest top"
          <| fun _ ->
              Expect.equal
                  (Delta.ofChange scheme (ColumnValuesChanged "amount"))
                  (Delta.ofColumns scheme [ "amount" ])
                  "a column-values change is column invalidation"

              Expect.equal (Delta.ofChange scheme RowsAppended) FullRefresh "an uncounted append is FullRefresh"

              Expect.equal
                  (Delta.ofChange
                      scheme
                      (SchemaChanged
                          { Added = []
                            Removed = []
                            Retyped = []
                            Reordered = false }))
                  FullRefresh
                  "a schema change is FullRefresh"

              Expect.equal (Delta.ofChange scheme FullChange) FullRefresh "a full change is FullRefresh"

          testCase "toChange projects conservatively, and says None for a quiet delta"
          <| fun _ ->
              Expect.equal (Delta.toChange (Delta.empty scheme)) None "quiet has no Change spelling"

              Expect.equal
                  (Delta.toChange (Delta.ofColumns scheme [ "amount" ]))
                  (Some(ColumnValuesChanged "amount"))
                  "one invalidated column projects exactly"

              Expect.equal
                  (Delta.toChange (Delta.ofColumns scheme [ "a"; "b" ]))
                  (Some FullChange)
                  "two columns exceed what Change can say — recompute"

              Expect.equal
                  (Delta.toChange (Delta.ofRows scheme [ ByKey "a", RowAdded ]))
                  (Some FullChange)
                  "a row-level delta exceeds what Change can say — recompute"

              Expect.equal (Delta.toChange FullRefresh) (Some FullChange) "the top projects to FullChange"

          // ================= diff, through the reference witness =================

          testCase "diff by identity classifies added / changed / removed and stays silent on the rest"
          <| fun _ ->
              let before = table [ Str "a"; Str "b"; Str "c" ] [ Int 1; Int 2; Int 3 ]

              let after = table [ Str "a"; Str "b"; Str "d" ] [ Int 1; Int 20; Int 4 ]

              match ok (Delta.diff idWitness before after) with
              | RowSet r ->
                  Expect.equal r.Scheme "column:id" "the witness names the scheme"

                  Expect.equal
                      r.Rows
                      [ ByKey "s:b", RowChanged; ByKey "s:c", RowRemoved; ByKey "s:d", RowAdded ]
                      "a unchanged and therefore unmentioned; b changed; c removed; d added"
              | other -> failtestf "expected a RowSet, got %A" other

          testCase "diff of a table with itself is quiet"
          <| fun _ ->
              let t = table [ Str "a"; Str "b" ] [ Int 1; Int 2 ]
              let d = ok (Delta.diff idWitness t t)
              Expect.isTrue (Delta.isQuiet d) "nothing changed"
              Expect.equal (Delta.toChange d) None "and it projects to no Change"

          testCase "a schema difference is FullRefresh, not a row-addressed lie"
          <| fun _ ->
              let before = table [ Str "a" ] [ Int 1 ]

              let after =
                  { Schema = [ "id", StringType ]
                    Columns = [ Column.create "id" StringType [ Str "a" ] ] }

              Expect.equal (ok (Delta.diff idWitness before after)) FullRefresh "column set moved"
              Expect.equal (Delta.diffByOrdinal before after) FullRefresh "same for the ordinal diff"

          testCase "a row the witness cannot key, or keys twice, refuses the whole diff"
          <| fun _ ->
              let before = table [ Str "a" ] [ Int 1 ]
              let nullKey = table [ Null ] [ Int 1 ]

              match Delta.diff idWitness before nullKey with
              | Error(MissingIdentity(s, i)) ->
                  Expect.equal s "column:id" "names the scheme"
                  Expect.equal i 0 "names the row"
              | other -> failtestf "expected MissingIdentity, got %A" other

              let dupKey = table [ Str "a"; Str "a" ] [ Int 1; Int 2 ]

              match Delta.diff idWitness before dupKey with
              | Error(DuplicateIdentity(_, k)) -> Expect.equal k "s:a" "names the repeated identity"
              | other -> failtestf "expected DuplicateIdentity, got %A" other

          testCase "row content is compared by the PINNED canonical token, so -0.0 and 0.0 agree"
          <| fun _ ->
              let mk (v: float) : Table =
                  { Schema = [ "id", StringType; "v", FloatType ]
                    Columns =
                      [ Column.create "id" StringType [ Str "a" ]
                        Column.create "v" FloatType [ Float v ] ] }

              let d = ok (Delta.diff idWitness (mk 0.0) (mk -0.0))
              Expect.isTrue (Delta.isQuiet d) "-0.0 and 0.0 are the same value in the columnar model"

          testCase "a composite key witness addresses rows by the tuple"
          <| fun _ ->
              let w = RowIdentity.byColumns [ "id"; "amount" ]
              let before = table [ Str "a" ] [ Int 1 ]
              let after = table [ Str "a" ] [ Int 2 ]

              match ok (Delta.diff w before after) with
              | RowSet r ->
                  Expect.equal r.Scheme "columns:id,amount" "the composite scheme is named"
                  // The composite key includes `amount`, so changing it is a DIFFERENT row: the old
                  // key is removed and the new one added. That is the honest consequence of keying on
                  // a mutable column, not a defect — and it is why `byColumn "id"` reports the same
                  // edit as `RowChanged`.
                  Expect.equal (r.Rows |> List.map snd) [ RowRemoved; RowAdded ] "the key itself moved"
              | other -> failtestf "expected a RowSet, got %A" other

          testCase "the ordinal diff is positional, under the reserved scheme"
          <| fun _ ->
              let before = table [ Str "a"; Str "b"; Str "c" ] [ Int 1; Int 2; Int 3 ]
              let after = table [ Str "a"; Str "B" ] [ Int 1; Int 2 ]

              match Delta.diffByOrdinal before after with
              | RowSet r ->
                  Expect.equal r.Scheme RowIdentity.ordinalScheme "the reserved scheme"
                  Expect.equal r.Rows [ ByOrdinal 1, RowChanged; ByOrdinal 2, RowRemoved ] "positional classification"
                  Expect.equal (Delta.defects (RowSet r)) [] "and it is well-formed"
              | other -> failtestf "expected a RowSet, got %A" other

          testCase "composing successive diffs is SOUND relative to the direct diff"
          <| fun _ ->
              // Composition may over-report (a row changed and changed back is still `RowChanged` in
              // the composite while the direct diff is silent) — it must never UNDER-report, and the
              // rows it does classify must agree with the direct answer.
              let t0 = table [ Str "a"; Str "b" ] [ Int 1; Int 2 ]
              let t1 = table [ Str "a"; Str "c" ] [ Int 10; Int 3 ]
              let t2 = table [ Str "a"; Str "c" ] [ Int 10; Int 30 ]

              let composed =
                  Delta.compose (ok (Delta.diff idWitness t0 t1)) (ok (Delta.diff idWitness t1 t2))

              let direct = ok (Delta.diff idWitness t0 t2)

              let asMap d =
                  match Delta.tryRowSet d with
                  | Some r -> Map.ofList (r.Rows |> List.map (fun (ref, c) -> Delta.refToken ref, c))
                  | None -> failtest "expected a row set"

              let composedMap = asMap composed
              let directMap = asMap direct

              for KeyValue(k, c) in directMap do
                  match Map.tryFind k composedMap with
                  | Some c' -> Expect.equal c' c (sprintf "%s classified the same way" k)
                  | None -> failtestf "the composite under-reported row %s" k

          // ================= resolve =================

          testCase "resolve turns present rows into indexes, and refuses a foreign scheme"
          <| fun _ ->
              let t = table [ Str "a"; Str "b"; Str "c" ] [ Int 1; Int 2; Int 3 ]

              let d =
                  Delta.ofRows "column:id" [ ByKey "s:a", RowChanged; ByKey "s:c", RowAdded; ByKey "s:z", RowRemoved ]

              Expect.equal (ok (Delta.resolve idWitness t d)) (Some [ 0; 2 ]) "present rows only, sorted"

              Expect.equal (ok (Delta.resolve idWitness t FullRefresh)) None "the top resolves to 'all rows'"

              match Delta.resolve idWitness t (Delta.ofRows "column:other" [ ByKey "s:a", RowChanged ]) with
              | Error(SchemeMismatch _) -> ()
              | other -> failtestf "expected SchemeMismatch, got %A" other

          testCase "resolve handles the ordinal scheme without needing the witness's keys"
          <| fun _ ->
              let t = table [ Str "a"; Str "b" ] [ Int 1; Int 2 ]

              let d =
                  Delta.ofRows RowIdentity.ordinalScheme [ ByOrdinal 1, RowChanged; ByOrdinal 5, RowAdded ]

              Expect.equal (ok (Delta.resolve idWitness t d)) (Some [ 1 ]) "out-of-range ordinals drop out"

          // ================= projections =================

          testCase "the projections do not confuse 'no rows' with 'every row'"
          <| fun _ ->
              Expect.isTrue (Delta.isFullRefresh FullRefresh) "isFullRefresh"
              Expect.isFalse (Delta.isQuiet FullRefresh) "the top is never quiet"
              Expect.equal (Delta.tryRowSet FullRefresh) None "no row set to read"
              Expect.isTrue (Delta.isQuiet (Delta.empty scheme)) "the quiet delta is quiet"

              let d =
                  Delta.ofRows scheme [ ByKey "a", RowAdded; ByKey "b", RowChanged; ByKey "c", RowAdded ]

              Expect.equal (Delta.rowsWith RowAdded d) [ ByKey "a"; ByKey "c" ] "rowsWith filters in canonical order"
              Expect.equal (Delta.rowsWith RowRemoved d) [] "and reports nothing for an absent change" ]
