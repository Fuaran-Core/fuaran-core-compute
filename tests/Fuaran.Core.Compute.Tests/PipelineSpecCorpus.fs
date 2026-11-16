module Fuaran.Compute.Tests.PipelineSpecCorpus

// ---------------------------------------------------------------------------
// Phase 604 (`DECISIONS.md` D163) — the pipeline wire specification's corpus, generated here and
// certified here.
//
// The specification is a separate repository (`fuaran-pipeline-specification`); its `wire-fixtures/`
// corpus is a BUILD INPUT of this repository's gate. This module does three things with it:
//
//   * EMITS it. The round-trip and hash vectors are the wire-surface harness's own documents
//     (`WireSurfaceTests.documentsOf`, one per case of every union the pipeline reaches), each realised
//     into a document the reader accepts, plus the declared pipelines of `PipelineHashSample`; the
//     reject and accept vectors are written below by hand, each with the refusal (or the reading) this
//     repository's reader gives it. A case added to `Transform` or `ColExpr` therefore lands in the
//     emission without anyone listing it, and the freshness test then reds until the specification's
//     corpus is regenerated in the same change-set (the forward-coupling rule, D163).
//   * RESOLVES it. `FUARAN_PIPELINE_SPEC` names a checkout explicitly; otherwise the checkout beside
//     this repository's MAIN working tree, under the directory name `Fuaran-Pipeline-Specification`
//     (the name is the interface, the specification's DECISIONS.md settles it). An absent corpus
//     FAILS, naming every path tried and the remedy. There is no skip: the gate certifies against it.
//   * CERTIFIES against it (PipelineSpecTests): every vector the manifest enumerates runs, the count
//     run equals the count enumerated, and a mutated document goes red.
//
//   dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-pipeline-corpus <specification root>
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Fuaran.Core
open Fuaran.Compute

/// The variable naming a checkout of the specification explicitly — the form CI uses.
[<Literal>]
let variable = "FUARAN_PIPELINE_SPEC"

/// The directory the specification is cloned under, beside this repository's main working tree.
[<Literal>]
let directoryName = "Fuaran-Pipeline-Specification"

[<Literal>]
let cloneUrl = "https://github.com/Fuaran-Core/fuaran-pipeline-specification.git"

/// The `specification` member the corpus's manifest declares.
[<Literal>]
let specificationId = "compute-pipeline-wire"

let remedy =
    String.concat
        "\n"
        [ sprintf
              "  Clone the specification beside this repository's main working tree:  git clone %s %s"
              cloneUrl
              directoryName
          sprintf "  or point %s at an existing checkout (the repository root, which holds wire-fixtures/)." variable
          "  The gate certifies against this corpus; it is a build input, and there is no skip." ]

/// Why `root` is not a checkout of the specification, or `None` when it is.
let fault (root: string) : string option =
    let manifest = Path.Combine(root, "wire-fixtures", "manifest.json")

    if not (File.Exists manifest) then
        Some(sprintf "no wire-fixtures/manifest.json under '%s'" root)
    else
        match Json.parse (File.ReadAllText manifest) with
        | Ok(JObj fields) when
            fields
            |> List.exists (fun (k, v) -> k = "specification" && v = JStr specificationId)
            ->
            None
        | _ -> Some(sprintf "'%s' does not declare \"specification\": \"%s\"" manifest specificationId)

/// The specification checkout: `FUARAN_PIPELINE_SPEC` when set, else the directory beside this
/// repository's main working tree. `Error` names every path tried and the remedy.
let resolveWith (variableValue: string option) (from: string) : Result<string, string> =
    match variableValue |> Option.filter (String.IsNullOrWhiteSpace >> not) with
    | Some root ->
        match fault root with
        | None -> Ok(Path.GetFullPath root)
        | Some why -> Error(sprintf "%s names '%s', which is not the specification: %s\n%s" variable root why remedy)
    | None ->
        match SiblingCorpus.mainWorkingTreeFrom from with
        | Error why ->
            Error(sprintf "the main working tree could not be read (%s), and %s is unset\n%s" why variable remedy)
        | Ok main ->
            let candidate =
                Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator main), directoryName)

            match fault candidate with
            | None -> Ok candidate
            | Some why ->
                Error(
                    sprintf
                        "the pipeline wire specification's corpus is ABSENT: %s unset, and beside the main working tree '%s': %s\n%s"
                        variable
                        main
                        why
                        remedy
                )

let resolve () : Result<string, string> =
    resolveWith (Option.ofObj (Environment.GetEnvironmentVariable variable)) (Snapshots.repoFile "")

// ---- the vectors ------------------------------------------------------------

/// One vector of the corpus. `Text` is the document's exact bytes (UTF-8, no trailing newline).
type Vector =
    {
        Family: string
        Name: string
        Kind: string
        Text: string
        /// The union case a harness document reaches (`Transform.Filter`), for a round-trip or hash vector.
        Case: string option
        /// The refusal class a reject vector names, and how the corpus's own tooling detects it.
        Reject: (string * string) option
        /// Does this repository's reader give a reject vector the refusal its class names?
        Refused: (ColumnError -> bool) option
        Description: string
    }

    member v.Id = v.Family + "/" + v.Name
    member v.File = v.Family + "/" + v.Name + ".json"

let private sha256Hex (text: string) =
    SHA256.HashData(Encoding.UTF8.GetBytes text)
    |> Array.map (fun b -> b.ToString "x2")
    |> String.concat ""

let private decodeOrFail (what: string) (text: string) : Transform list =
    match DataFrameCodec.decodePipeline text with
    | Ok p -> p
    | Error e -> failwithf "%s does not decode: %A\n%s" what e text

let private hashOrFail (what: string) (p: Transform list) : string =
    match Pipeline.hash p with
    | Ok h -> h
    | Error e -> failwithf "%s has no pipeline hash: %s" what e

// A union case's corpus name, kebab-cased: `GroupBy` -> `group-by`, `KMeans` -> `kmeans`, `HC0` -> `hc0`.
let private kebab (s: string) =
    let sb = StringBuilder()

    s
    |> Seq.iteri (fun i ch ->
        if i > 0 && Char.IsUpper ch && (Char.IsLower s[i - 1] || Char.IsDigit s[i - 1]) then
            sb.Append '-' |> ignore

        sb.Append(Char.ToLowerInvariant ch) |> ignore)

    sb.ToString()

/// The unions whose cases are Fuaran.Core's column wire (an embedded source's schema and columns):
/// that wire is Core's, specified and certified by Core, and cited rather than restated here.
let private coreOwned = set [ "ColumnData"; "ColumnType"; "Validity"; "TimeUnit" ]

/// Which family a harness document's union belongs to.
let private familyOf (union: string) : string =
    match union with
    | "Transform" -> "step"
    | "ColExpr" -> "expr"
    | "Cell" -> "cell"
    | "DataSource" -> "source"
    | u when u.StartsWith "Slot<" -> "slot"
    | _ -> "token"

/// The vector's name within its family.
let private nameOf (union: string) (case: string) : string =
    match familyOf union with
    | "token" -> union.ToLowerInvariant() + "-" + kebab case
    | "slot" -> union.Substring(5, union.Length - 6).ToLowerInvariant() + "-" + kebab case
    | _ -> kebab case

/// The reflective builder writes the shortest value of each type — `"s"` for every string — which is
/// the right exemplar for a byte baseline and not always a value a reader accepts. Realising swaps in
/// the smallest value that IS one, where the member's meaning needs it: a unit text, a date, an
/// instant, a decimal.
let rec private realise (v: JVal) : JVal =
    match v with
    | JObj fields ->
        let tag =
            fields |> List.tryPick (fun (k, x) -> if k = "$type" then Some x else None)

        JObj(
            fields
            |> List.map (fun (k, x) ->
                match k, x, tag with
                | "unit", JStr "ss", _ -> k, JStr "m"
                | "value", JStr "s", Some(JStr "Date") -> k, JStr "2026-01-15"
                | "value", JStr "s", Some(JStr "Timestamp") -> k, JStr "2026-01-15T09:30:00Z"
                | "value", JStr "s", Some(JStr "Decimal") -> k, JStr "12.5"
                | _ -> k, realise x)
        )
    | JArr xs -> JArr(xs |> List.map realise)
    | x -> x

/// A family and a link the estimator fits are a PAIR: a document reaching one family (or one link)
/// with the builder's first link (or family) beside it may name a pair the reader refuses. The first
/// admitted partner, in declaration order, is taken instead.
let private families =
    [ "gaussian"
      "poisson"
      "gamma"
      "tweedie"
      "quasiPoisson"
      "binomial"
      "quasiBinomial"
      "inverseGaussian"
      "negativeBinomial" ]

let private links =
    [ "identity"
      "log"
      "logit"
      "probit"
      "cloglog"
      "inverse"
      "inverseSquare"
      "sqrt" ]

let rec private setFitMember (memberName: string) (value: string) (v: JVal) : JVal =
    match v with
    | JObj fields when
        fields |> List.exists (fun (k, _) -> k = "family")
        && fields |> List.exists (fun (k, _) -> k = "link")
        ->
        JObj(
            fields
            |> List.map (fun (k, x) ->
                if k = memberName then
                    k, JStr value
                else
                    k, setFitMember memberName value x)
        )
    | JObj fields -> JObj(fields |> List.map (fun (k, x) -> k, setFitMember memberName value x))
    | JArr xs -> JArr(xs |> List.map (setFitMember memberName value))
    | x -> x

let private admittedPartner (union: string) (doc: JVal) : JVal =
    let decodes (d: JVal) =
        DataFrameCodec.decodePipeline (Canonical.write Rendering.CrossHost d)
        |> Result.isOk

    if decodes doc then
        doc
    else
        let candidates =
            match union with
            | "FitFamily" -> links |> List.map (fun l -> setFitMember "link" l doc)
            | "FitLink" -> families |> List.map (fun f -> setFitMember "family" f doc)
            | _ -> []

        candidates |> List.tryFind decodes |> Option.defaultValue doc

/// The harness's per-case documents, realised: one hash vector per case of every union the pipeline
/// root reaches, outside the unions Core owns.
let harnessVectors () : Vector list =
    let root =
        WireSurfaceTests.roots
        |> List.find (fun r ->
            match r with
            | WireSurfaceTests.Derived(_, "pipeline", _, _) -> true
            | _ -> false)

    let _, cases = WireSurfaceTests.documentsOf root

    let docs = WireSurfaceTests.documentsOf root |> fst |> Map.ofList

    [ for name, _, case, _ in cases do
          // `pipeline / Slot<Cell>.Lit`: the union's readable name is the text between the label and the case.
          let reached = name.Substring(name.IndexOf " / " + 3)
          let union = reached.Substring(0, reached.Length - case.Length - 1)

          if not (coreOwned.Contains union) then
              let emitted = docs[name]

              let realised =
                  match Json.parse emitted with
                  | Ok v -> realise v |> admittedPartner union
                  | Error e -> failwithf "harness document %s is not JSON: %A" name e

              let text =
                  decodeOrFail name (Canonical.write Rendering.CrossHost realised)
                  |> DataFrameCodec.encodePipeline

              yield
                  { Family = familyOf union
                    Name = nameOf union case
                    Kind = "hash"
                    Text = text
                    Case = Some(union + "." + case)
                    Reject = None
                    Refused = None
                    Description =
                      sprintf
                          "The wire-surface harness's document for %s.%s: the shortest pipeline reaching that case, every optional member present."
                          union
                          case } ]

/// The `cast` target types (Fuaran.Core's column type tags), one vector each: the harness reaches
/// those tags through an embedded source's schema, which is Core's wire, so the pipeline's own use of
/// them is pinned here.
let castVectors () : Vector list =
    [ for ty in ColumnType.all do
          let text = DataFrameCodec.encodePipeline [ Derive("y", Cast(ty, Col "x")) ]

          yield
              { Family = "token"
                Name = "cast-type-" + (ColumnType.tag ty).Replace('_', '-')
                Kind = "hash"
                Text = text
                Case = Some("ColExpr.Cast")
                Reject = None
                Refused = None
                Description = sprintf "A `cast` to the column type `%s`, by its tag." (ColumnType.tag ty) } ]

/// The declared pipelines (PipelineHashSample) that have canonical bytes: the worked examples the
/// text cites, and the ones the node leg reproduces.
let sampleVectors () : Vector list =
    [ for id, p in PipelineHashSample.pipelines do
          match Pipeline.canonicalText p with
          | Ok text ->
              yield
                  { Family = "pipeline"
                    Name = id
                    Kind = "hash"
                    Text = text
                    Case = None
                    Reject = None
                    Refused = None
                    Description = sprintf "A declared pipeline (`%s`): see the specification's worked examples." id }
          | Error _ -> () ]

let private notJson (kind: JsonErrorKind option) (e: ColumnError) =
    match e, kind with
    | NotJson j, Some k -> j.Kind = k
    | NotJson j, None -> j.Kind <> DuplicateMember && j.Kind <> NullNotRepresentable
    | _ -> false

let private shapeSays (needle: string) (e: ColumnError) =
    match e with
    | MalformedShape m -> m.Contains(needle: string)
    | _ -> false

let private litRefused (e: ColumnError) =
    match e with
    | TypeMismatch("lit", _, "value") -> true
    | _ -> false

let private reject family name cls detect (refused: ColumnError -> bool) text description =
    { Family = family
      Name = name
      Kind = "reject"
      Text = text
      Case = None
      Reject = Some(cls, detect)
      Refused = Some refused
      Description = description }

/// The reject vectors, each with the refusal this repository's reader gives it.
let rejectVectors () : Vector list =
    [ reject
          "pipeline"
          "reject-not-json"
          "not-json"
          "rule"
          (notJson None)
          """[{"$type":"distinct"}"""
          "A document that is not JSON: the closing bracket is missing."
      reject
          "pipeline"
          "reject-not-a-list"
          "not-a-pipeline"
          "schema"
          (shapeSays "expected a JSON array of transform steps")
          """{"$type":"distinct"}"""
          "A single step where a pipeline (a list of steps) is expected."
      reject
          "pipeline"
          "reject-null-member"
          "null-member"
          "schema"
          (notJson (Some NullNotRepresentable))
          """[{"$type":"limit","n":null,"offset":0}]"""
          "A member whose value is null. Absence is structural: a member is omitted, never null."
      reject
          "pipeline"
          "reject-repeated-member"
          "repeated-member"
          "rule"
          (notJson (Some DuplicateMember))
          """[{"$type":"limit","n":1,"n":2,"offset":0}]"""
          "One member named twice in one object: no ordering makes it deterministic."
      reject
          "step"
          "reject-unknown-step"
          "unknown-tag"
          "schema"
          (shapeSays "unknown tag 'explode'")
          """[{"$type":"explode","column":"items"}]"""
          "A step whose `$type` is not a registered step kind."
      reject
          "step"
          "reject-missing-type"
          "missing-member"
          "schema"
          (shapeSays "has no \"$type\"")
          """[{"pred":{"$type":"col","name":"active"}}]"""
          "A step with no `$type` discriminator."
      reject
          "step"
          "reject-missing-member"
          "missing-member"
          "schema"
          (fun e ->
              match e with
              | MissingField "expr" -> true
              | _ -> shapeSays "\"expr\"" e)
          """[{"$type":"derive","name":"total"}]"""
          "A `derive` step without its required `expr` member."
      reject
          "step"
          "reject-wrong-type"
          "wrong-type"
          "schema"
          (shapeSays "expected string")
          """[{"$type":"derive","expr":{"$type":"col","name":"a"},"name":7}]"""
          "A `derive` step whose `name` is a number where a string is required."
      reject
          "step"
          "reject-int-out-of-range"
          "wrong-type"
          "schema"
          (shapeSays "expected int")
          """[{"$type":"limit","n":3000000000,"offset":0}]"""
          "A 32-bit integer member holding a token outside the signed 32-bit range."
      reject
          "step"
          "reject-int-fraction"
          "wrong-type"
          "schema"
          (shapeSays "expected int")
          """[{"$type":"limit","n":1.5,"offset":0}]"""
          "A 32-bit integer member holding a token with a fraction."
      reject
          "step"
          "reject-fit-pair"
          "invalid-value"
          "rule"
          (shapeSays "the binomial family is not fitted under the identity link")
          """[{"$type":"fit","control":{"maxIter":25,"tol":1E-08},"family":"binomial","groupBy":[],"intercept":true,"link":"identity","output":"coefficients","predictors":[{"expr":{"$type":"col","name":"dose"},"name":"dose"}],"response":"survived"}]"""
          "A `fit` step naming a family and a link that are not an admitted pair."
      reject
          "expr"
          "reject-unknown-expr"
          "unknown-tag"
          "schema"
          (shapeSays "unknown tag 'magic'")
          """[{"$type":"filter","pred":{"$type":"magic"}}]"""
          "An expression whose `$type` is not a registered expression kind."
      reject
          "expr"
          "reject-unit-text"
          "invalid-value"
          "rule"
          (shapeSays "is not a unit")
          """[{"$type":"derive","expr":{"$type":"convert","expr":{"$type":"col","name":"d"},"unit":"furlongs"},"name":"e"}]"""
          "A `convert` expression whose `unit` is not unit text."
      reject
          "cell"
          "reject-date-text"
          "invalid-value"
          "schema"
          litRefused
          """[{"$type":"filter","pred":{"$type":"lit","cell":{"$type":"Date","value":"15/01/2026"}}}]"""
          "A `Date` cell whose value is not the date's canonical text."
      reject
          "cell"
          "reject-date-calendar"
          "invalid-value"
          "rule"
          litRefused
          """[{"$type":"filter","pred":{"$type":"lit","cell":{"$type":"Date","value":"2026-02-30"}}}]"""
          "A `Date` cell of the right shape naming a day the calendar does not have."
      reject
          "cell"
          "reject-timestamp-text"
          "invalid-value"
          "schema"
          litRefused
          """[{"$type":"filter","pred":{"$type":"lit","cell":{"$type":"Timestamp","value":"2026-01-15 09:30:00"}}}]"""
          "A `Timestamp` cell whose value is not the instant's canonical text."
      reject
          "cell"
          "reject-timestamp-trailing-zero"
          "invalid-value"
          "schema"
          litRefused
          """[{"$type":"filter","pred":{"$type":"lit","cell":{"$type":"Timestamp","value":"2026-01-15T09:30:00.250Z"}}}]"""
          "A `Timestamp` cell whose fraction ends in a zero: one instant has one text."
      reject
          "cell"
          "reject-decimal-text"
          "invalid-value"
          "schema"
          litRefused
          """[{"$type":"filter","pred":{"$type":"lit","cell":{"$type":"Decimal","value":"1.5e3"}}}]"""
          "A `Decimal` cell whose value carries an exponent."
      reject
          "cell"
          "reject-non-finite"
          "wrong-type"
          "schema"
          litRefused
          """[{"$type":"filter","pred":{"$type":"lit","cell":{"$type":"Float","value":"NaN"}}}]"""
          "A `Float` cell carrying a quoted non-finite token. No pipeline document carries a non-finite float."
      reject
          "cell"
          "reject-unknown-cell"
          "unknown-tag"
          "schema"
          (shapeSays "unknown tag 'Money'")
          """[{"$type":"filter","pred":{"$type":"lit","cell":{"$type":"Money","value":"1"}}}]"""
          "A cell whose `$type` is not a registered cell kind."
      reject
          "slot"
          "reject-seed-number"
          "wrong-type"
          "schema"
          (shapeSays "a seed is a signed 64-bit integer")
          """[{"$type":"kmeans","features":[{"expr":{"$type":"col","name":"x"},"name":"x"}],"groupBy":[],"k":2,"maxIterations":10,"output":"assignments","restarts":1,"seed":7,"silhouette":false,"standardise":true}]"""
          "A seed written as a JSON number. A seed is a 64-bit integer and travels as its decimal digits in a string."
      reject
          "slot"
          "reject-seed-leading-zero"
          "invalid-value"
          "schema"
          (shapeSays "a seed is a signed 64-bit integer")
          """[{"$type":"kmeans","features":[{"expr":{"$type":"col","name":"x"},"name":"x"}],"groupBy":[],"k":2,"maxIterations":10,"output":"assignments","restarts":1,"seed":"007","silhouette":false,"standardise":true}]"""
          "A seed whose digits carry a leading zero: one integer has one text."
      reject
          "slot"
          "reject-seed-out-of-range"
          "invalid-value"
          "rule"
          (shapeSays "a seed is a signed 64-bit integer")
          """[{"$type":"kmeans","features":[{"expr":{"$type":"col","name":"x"},"name":"x"}],"groupBy":[],"k":2,"maxIterations":10,"output":"assignments","restarts":1,"seed":"9223372036854775808","silhouette":false,"standardise":true}]"""
          "A seed one past the signed 64-bit range."
      reject
          "slot"
          "reject-param-name"
          "wrong-type"
          "schema"
          (shapeSays "\"$param\" must be a JSON string")
          """[{"$type":"limit","n":{"$param":7},"offset":0}]"""
          "A parameter slot whose `$param` is not a string."
      reject
          "token"
          "reject-join-kind"
          "unknown-tag"
          "schema"
          (shapeSays "unknown tag 'sideways'")
          """[{"$type":"join","how":"sideways","on":[{"a":"k","b":"k"}],"source":{"ref":"lookup","schema":[]}}]"""
          "A join whose `how` is not a registered join kind."
      reject
          "token"
          "reject-aggregate"
          "unknown-tag"
          "schema"
          (shapeSays "unknown tag 'mode'")
          """[{"$type":"groupBy","aggs":[{"fn":"mode","name":"m","of":"x"}],"keys":["k"]}]"""
          "An aggregate whose `fn` is not a registered aggregate function." ]

let private accept family name text description =
    { Family = family
      Name = name
      Kind = "accept"
      Text = text
      Case = None
      Reject = None
      Refused = None
      Description = description }

/// The accept vectors: non-canonical spellings every reader must read, and read as the canonical
/// document the manifest records.
let acceptVectors () : Vector list =
    [ accept
          "pipeline"
          "accept-member-order"
          """[{"name":"net","expr":{"right":{"name":"discount","$type":"col"},"op":"sub","left":{"name":"amount","$type":"col"},"$type":"binary"},"$type":"derive"}]"""
          "Members in an order other than the canonical one. A reader matches members by name; the canonical document, and so the hash, are the sorted one's."
      accept
          "pipeline"
          "accept-whitespace"
          "[\n  { \"$type\": \"filter\",\n    \"pred\": { \"$type\": \"col\", \"name\": \"active\" } },\n  { \"$type\": \"distinct\" }\n]"
          "Insignificant whitespace between tokens. A reader ignores it; the canonical document carries none."
      accept
          "pipeline"
          "accept-unknown-member"
          """[{"$type":"distinct","note":"added by a later version"}]"""
          "A member this version does not define. A reader ignores it, which is what makes adding a member additive."
      accept
          "step"
          "accept-string-escapes"
          """[{"$type":"derive","expr":{"$type":"lit","cell":{"$type":"Str","value":"a\/b\ncé"}},"name":"t"}]"""
          "A string spelled with escapes the canonical form does not use (`\\/`, `\\n`, `\\u00e9`). The value is the same; the canonical text writes `/` and `é` literally and the line feed as `\\u000a`."
      accept
          "cell"
          "accept-decimal-text"
          """[{"$type":"filter","pred":{"$type":"lit","cell":{"$type":"Decimal","value":"012.50"}}}]"""
          "Decimal text with a leading and a trailing zero. A reader reads the number it names; the canonical text is `12.5`."
      accept
          "cell"
          "accept-decimal-integer-token"
          """[{"$type":"filter","pred":{"$type":"lit","cell":{"$type":"Decimal","value":7}}}]"""
          "A decimal written as an integer token, which is exact. The canonical text is the string `\"7\"`."
      accept
          "expr"
          "accept-unit-spelling"
          """[{"$type":"derive","expr":{"$type":"convert","expr":{"$type":"col","name":"force"},"unit":"m.kg/(s.s)"},"name":"newtons"}]"""
          "A unit text the grammar parses that is not the unit's canonical text. A reader reads the unit; the canonical text is `kg.m/s2`."
      accept
          "step"
          "accept-std-errors-classical"
          """[{"$type":"fit","control":{"maxIter":25,"tol":1E-08},"family":"gaussian","groupBy":[],"intercept":true,"link":"identity","output":"coefficients","predictors":[{"expr":{"$type":"col","name":"price"},"name":"price"}],"response":"sales","stdErrors":"classical"}]"""
          "A fit naming classical standard errors, which the canonical form says by omitting `stdErrors`."
      accept
          "source"
          "accept-ref-without-schema"
          """[{"$type":"union","source":{"ref":"archive"}}]"""
          "A source by reference without the empty `schema` member the canonical form writes."
      accept
          "cell"
          "accept-float-layout"
          """[{"$type":"filter","pred":{"$type":"lit","cell":{"$type":"Float","value":1.0e-5}}}]"""
          "A float token in a layout other than the canonical one. The canonical layout of the same double is `1E-05`." ]

/// Every vector, in the manifest's order: family by family, and by name within a family.
let familyOrder = [ "pipeline"; "step"; "expr"; "cell"; "slot"; "token"; "source" ]

let vectors () : Vector list =
    let all =
        sampleVectors ()
        @ harnessVectors ()
        @ castVectors ()
        @ rejectVectors ()
        @ acceptVectors ()

    let dup = all |> List.countBy _.Id |> List.filter (fun (_, n) -> n > 1)

    if not dup.IsEmpty then
        failwithf "two vectors share an id: %A" dup

    all |> List.sortBy (fun v -> List.findIndex ((=) v.Family) familyOrder, v.Name)

// ---- the manifest ------------------------------------------------------------

/// A JSON value written for a reader: two-space indent, members in the order given, an array of
/// scalars on one line. Deterministic, so the emission is a fixed point.
let rec private pretty (indent: string) (v: JVal) : string =
    let inner = indent + "  "

    let scalar (x: JVal) =
        match x with
        | JObj _
        | JArr _ -> false
        | _ -> true

    match v with
    | JObj [] -> "{}"
    | JObj fields ->
        let members =
            fields
            |> List.map (fun (k, x) -> inner + Canonical.write Rendering.CrossHost (JStr k) + ": " + pretty inner x)

        "{\n" + String.concat ",\n" members + "\n" + indent + "}"
    | JArr [] -> "[]"
    | JArr xs when xs |> List.forall scalar -> "[ " + (xs |> List.map (pretty inner) |> String.concat ", ") + " ]"
    | JArr xs ->
        "[\n"
        + (xs |> List.map (fun x -> inner + pretty inner x) |> String.concat ",\n")
        + "\n"
        + indent
        + "]"
    | x -> Canonical.write Rendering.CrossHost x

/// The manifest entry of one vector. A hash vector records its digest; an accept vector the canonical
/// document it is read as and that document's digest; a reject vector its class and detection.
let private entry (v: Vector) : JVal =
    let digestOf (text: string) =
        hashOrFail v.Id (decodeOrFail v.Id text)

    let common =
        [ "id", JStr v.Id
          "family", JStr v.Family
          "kind", JStr v.Kind
          "file", JStr v.File
          "sha256", JStr(sha256Hex v.Text) ]

    let specific =
        match v.Kind with
        | "hash" ->
            [ yield "digest", JStr(digestOf v.Text)
              match v.Case with
              | Some c -> yield "case", JStr c
              | None -> () ]
        | "accept" ->
            let canonical = DataFrameCodec.encodePipeline (decodeOrFail v.Id v.Text)

            [ "canonical", JStr canonical; "digest", JStr(digestOf canonical) ]
        | _ ->
            match v.Reject with
            | Some(cls, detect) -> [ "reject", JStr cls; "detect", JStr detect ]
            | None -> []

    JObj(common @ specific @ [ "description", JStr v.Description ])

/// The manifest's text.
let manifestText (vs: Vector list) : string =
    let present =
        familyOrder |> List.filter (fun f -> vs |> List.exists (fun v -> v.Family = f))

    pretty
        ""
        (JObj
            [ "specification", JStr specificationId
              "formatVersion", JInt 1
              "hashAlgorithm", JStr Pipeline.hashAlgorithm
              "families", JArr(present |> List.map JStr)
              // Phase 603's tree form: a family this specification intends to add (convention 12).
              "reservedFamilies", JArr [ JStr "tree" ]
              "schemas", JObj [ "pipeline", JStr "../schemas/v1/pipeline.schema.json" ]
              "generator",
              JStr "dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-pipeline-corpus <specification root>"
              "vectors", JArr(vs |> List.map entry) ])
    + "\n"

/// Every file the emission writes under `wire-fixtures/`, by its path relative to that directory.
let emission () : (string * string) list =
    let vs = vectors ()
    ("manifest.json", manifestText vs) :: (vs |> List.map (fun v -> v.File, v.Text))

/// Write the emission into `<root>/wire-fixtures/`, removing any vector file it no longer emits.
let write (root: string) : string list =
    let fixtures = Path.Combine(root, "wire-fixtures")
    let files = emission ()

    for family in familyOrder do
        let dir = Path.Combine(fixtures, family)

        if Directory.Exists dir then
            for f in Directory.GetFiles(dir, "*.json") do
                File.Delete f

    for rel, text in files do
        let path = Path.Combine(fixtures, rel)
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        File.WriteAllBytes(path, UTF8Encoding(false).GetBytes text)

    files |> List.map fst
