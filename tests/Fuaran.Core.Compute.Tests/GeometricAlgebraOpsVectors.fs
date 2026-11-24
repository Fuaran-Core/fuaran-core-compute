namespace Fuaran.Compute.Tests

// ============================================================================
//  Phase 731 — the op algebra's law vectors: every published function of
//  `Fuaran.Compute.GeometricAlgebraOps` over authored inputs, each answer
//  computed by calling the package and rendered through the substrate's one
//  corpus writer (`CorpusFile`) into
//  `conformance/laws/geometric-algebra-ops-laws.json` (DECISIONS.md D85
//  item 9, D90).
//
//  An input is written as ops in their canonical wire text, which carries
//  parameters and never a coefficient. An answer that is a state is written
//  with the coefficients of each object, each float its IEEE-754 binary64
//  bit pattern: a law vector is evidence, not a persisted stream (D85
//  item 10). Every function is evaluated through a record of the published
//  functions, so the suite evaluates the same cases through the shipped
//  functions with one internal rule changed and shows a vector fail.
//
//  Fable-clean: the node leg (LinearAlgebraNode/) compiles this file and
//  renders the same document under JavaScript, and the suite holds the two
//  renderings and the committed file equal.
// ============================================================================

open System
open Fuaran.Core
open Fuaran.Compute.GeometricAlgebra
open Fuaran.Compute.GeometricAlgebraOps

module GeometricAlgebraOpsVectors =

    /// The file this family is written to under `laws/`.
    let fileName = "geometric-algebra-ops-laws.json"

    /// The names of every vector: strings, compared ordinally and spelled as they are.
    let naming: Naming<string> =
        { Compare = fun a b -> String.CompareOrdinal(a, b)
          Encode = id
          Decode = fun s -> if s.Length = 0 then Error "an empty name" else Ok s }

    type private Op = GeometricOp<string>
    type private State = GeometricState<string>
    type private Refusal = GeometricOpRefusal<string>

    /// The package's published functions, as one record: `published` is the package's; the suite
    /// builds another from the same functions with one rule changed.
    type Functions =
        { Apply:
            GeometricOp<string> -> GeometricState<string> -> Result<GeometricState<string>, GeometricOpRefusal<string>>
          CanApply: GeometricOp<string> -> GeometricState<string> -> Result<unit, GeometricOpRefusal<string>>
          ApplyAll:
              GeometricOp<string> list
                  -> GeometricState<string>
                  -> GeometricState<string> * (int * GeometricOpRefusal<string>) list
          Invert:
              GeometricOp<string> -> GeometricState<string> -> Result<GeometricOp<string>, GeometricOpRefusal<string>>
          Compact: GeometricOp<string> list -> GeometricState<string> -> GeometricOp<string> list
          Encode: GeometricOp<string> -> string
          Decode: string -> Result<GeometricOp<string>, string>
          Witness: StreamWitness<GeometricOp<string>, GeometricState<string>, GeometricOpRefusal<string>>
          TryFind: string -> GeometricState<string> -> GeometricObject option }

    let published: Functions =
        { Apply = GeometricOps.apply
          CanApply = GeometricOps.canApply
          ApplyAll = GeometricOps.applyAll
          Invert = GeometricOps.invert
          Compact = GeometricOps.compact
          Encode = GeometricOps.encode naming
          Decode = GeometricOps.decode naming
          Witness = GeometricOps.streamWitness naming
          TryFind = GeometricState.tryFind }

    /// One argument of a case.
    type Argument =
        | OneOp of GeometricOp<string>
        | OpList of GeometricOp<string> list
        | Text of string
        | Name of string

    /// One authored case: a published function over a state the `start` ops build, and its arguments.
    type Case =
        { Id: string
          Op: string
          Start: GeometricOp<string> list
          Input: (string * Argument) list }

    // ---- the parameters -------------------------------------------------------------------------

    /// The arguments at which Phase 725's law file records the pinned sine and the pinned cosine a unit
    /// in the last place from the correctly rounded value, doubled: a rotation by one of these angles
    /// asks `sinCos` at the half of it.
    let sineAngle = 2.0 * GeometricAlgebraVectors.sineOffByOne
    let cosineAngle = 2.0 * GeometricAlgebraVectors.cosineOffByOne

    let private forms: (string * Form) list =
        [ "point", Form.Point(1.0, 2.0, 3.0)
          "pair", Form.PointPair((1.0, 0.0, 0.0), (0.0, 2.0, -1.0))
          "line", Form.Line((1.0, 2.0, 3.0), (1.0, -1.0, 2.0))
          "circle", Form.Circle((0.0, 1.0, 2.0), 1.5, (1.0, 1.0, 1.0))
          "plane", Form.Plane((0.0, 3.0, 4.0), 2.0)
          "sphere", Form.Sphere((1.0, -1.0, 2.0), 2.5) ]

    let private transforms (name: string) : (string * Op) list =
        [ "rotate", GeometricOp.Rotate(name, (1.0, -2.0, 0.5), (0.0, 1.0, 1.0), 0.75)
          "translate", GeometricOp.Translate(name, (0.5, -1.25, 2.0))
          "scale", GeometricOp.Scale(name, (1.0, 1.0, -1.0), 2.5)
          "reflect", GeometricOp.Reflect(name, (1.0, 2.0, 2.0), 0.5)
          "screw", GeometricOp.Screw(name, (1.0, 0.0, -1.0), (1.0, 1.0, 0.0), -1.25, 0.75) ]

    /// A state of two objects, the target `b` added first and `a`, which orders before it, second: an
    /// op that reaches another name's object, or a state iterated in the order of addition, answers
    /// another state.
    let private beside (form: Form) : Op list =
        [ GeometricOp.Add("b", form)
          GeometricOp.Add("a", Form.Point(9.0, -9.0, 0.25)) ]

    let private case (id: string) (op: string) (start: Op list) (input: (string * Argument) list) : Case =
        { Id = id
          Op = op
          Start = start
          Input = input }

    let private origin = 0.0, 0.0, 0.0
    let private up = 0.0, 0.0, 1.0

    /// Each refusal from a state holding `b`, and its id.
    let private refused: (string * Op) list =
        [ "unknown-name", GeometricOp.Rotate("z", origin, up, 1.0)
          "remove-unknown-name", GeometricOp.Remove "z"
          "name-already-present", GeometricOp.Add("b", Form.Point origin)
          "non-finite-coordinate", GeometricOp.Add("c", Form.Point(Double.NaN, 0.0, 0.0))
          "zero-direction-of-a-line", GeometricOp.Add("c", Form.Line(origin, origin))
          "negative-radius-of-a-sphere", GeometricOp.Add("c", Form.Sphere(origin, -1.0))
          "negative-radius-of-a-circle", GeometricOp.Add("c", Form.Circle(origin, -0.5, up))
          "zero-direction-of-a-rotation", GeometricOp.Rotate("b", origin, origin, 1.0)
          "non-finite-angle", GeometricOp.Rotate("b", origin, up, Double.PositiveInfinity)
          "non-finite-distance", GeometricOp.Screw("b", origin, up, 1.0, Double.NaN)
          "non-finite-translation", GeometricOp.Translate("b", (0.0, Double.NegativeInfinity, 0.0))
          "zero-factor", GeometricOp.Scale("b", origin, 0.0)
          "negative-factor", GeometricOp.Scale("b", origin, -2.0)
          "negative-zero-factor", GeometricOp.Scale("b", origin, -0.0)
          "zero-normal", GeometricOp.Reflect("b", origin, 0.0)
          "the-name-before-the-parameters", GeometricOp.Rotate("z", origin, origin, Double.NaN)
          "the-point-before-the-direction", GeometricOp.Screw("b", (Double.NaN, 0.0, 0.0), origin, Double.NaN, 0.0)
          "the-direction-before-the-angle", GeometricOp.Screw("b", origin, origin, Double.NaN, Double.NaN)
          "the-angle-before-the-distance", GeometricOp.Screw("b", origin, up, Double.NaN, Double.NaN)
          "the-centre-before-the-factor", GeometricOp.Scale("b", (0.0, Double.NaN, 0.0), -1.0)
          "non-finite-point-of-a-rotation", GeometricOp.Rotate("b", (Double.PositiveInfinity, 0.0, 0.0), up, 1.0) ]

    let private holdingB = [ GeometricOp.Add("b", Form.Point(1.0, 2.0, 3.0)) ]

    /// A rotation by the largest double: an angle the algebra accepts, and one a port that converts it
    /// from degrees overflows.
    let private largestAngle =
        GeometricOp.Rotate("b", (1.0, -2.0, 0.5), (0.0, 1.0, 1.0), Double.MaxValue)

    /// A rotation and a screw at the angles where the pinned sine and cosine are not correctly rounded.
    let private trigonometric (name: string) : (string * Op) list =
        [ "rotate-where-the-sine-is-not-correctly-rounded",
          GeometricOp.Rotate(name, (1.0, -2.0, 0.5), (0.0, 1.0, 1.0), sineAngle)
          "rotate-where-the-cosine-is-not-correctly-rounded",
          GeometricOp.Rotate(name, (1.0, -2.0, 0.5), (0.0, 1.0, 1.0), cosineAngle)
          "screw-where-the-sine-is-not-correctly-rounded",
          GeometricOp.Screw(name, (1.0, 0.0, -1.0), (1.0, 1.0, 0.0), sineAngle, 0.75)
          "screw-where-the-cosine-is-not-correctly-rounded",
          GeometricOp.Screw(name, (1.0, 0.0, -1.0), (1.0, 1.0, 0.0), cosineAngle, 0.75) ]

    /// The name an op edits.
    let private nameOf (op: Op) : string =
        match op with
        | GeometricOp.Add(n, _)
        | GeometricOp.Remove n
        | GeometricOp.Rotate(n, _, _, _)
        | GeometricOp.Translate(n, _)
        | GeometricOp.Scale(n, _, _)
        | GeometricOp.Reflect(n, _, _)
        | GeometricOp.Screw(n, _, _, _, _) -> n

    /// Texts no op is, each with the start of the refusal the decoder names.
    let malformed: (string * string) list =
        [ "{", "not json"
          "[]", "expected an object"
          "{\"name\":\"a\"}", "$type: missing"
          "{\"$type\":\"spin\",\"name\":\"a\"}", "unknown op: spin"
          "{\"$type\":\"remove\"}", "members:"
          "{\"$type\":\"remove\",\"extra\":\"\",\"name\":\"a\"}", "members:"
          "{\"$type\":\"remove\",\"name\":\"\"}", "name: "
          "{\"$type\":\"remove\",\"name\":7}", "name: "
          "{\"$type\":\"translate\",\"name\":\"a\",\"vector\":\"00\"}", "vector: "
          "{\"$type\":\"translate\",\"name\":\"a\",\"vector\":\"3FF000000000000000000000000000000000000000000000\"}",
          "vector: "
          "{\"$type\":\"rotate\",\"angle\":1.5,\"direction\":\"000000000000000000000000000000000000000000000000\",\"name\":\"a\",\"point\":\"000000000000000000000000000000000000000000000000\"}",
          "angle: "
          "{\"$type\":\"add\",\"form\":{\"$type\":\"cone\"},\"name\":\"a\"}", "form: unknown form: cone"
          "{\"$type\":\"add\",\"form\":{\"$type\":\"point\"},\"name\":\"a\"}", "form: members:" ]

    /// The drawn cases the suite's search chose as witnesses: the index it drew each at, and its id.
    let chosen: (int * string) list = [ 2, "drawn-2-compact"; 6, "drawn-6-compact" ]

    /// The deep drawn cases the suite's search chose as witnesses (Phase 731's third round).
    let chosenDeep: (int * string) list =
        [ 38, "drawn-deep-38-compact"
          220, "drawn-deep-220-canApply"
          220, "drawn-deep-220-compact" ]

    /// The wide drawn cases the suite's search chose as witnesses (Phase 731's second round).
    let chosenWide: (int * string) list =
        [ 0, "drawn-wide-0-streamWitness"
          9, "drawn-wide-9-compact"
          13, "drawn-wide-13-canApply"
          13, "drawn-wide-13-invert"
          13, "drawn-wide-13-compact" ]

    let private geometryName (g: GeometryRefusal) : string =
        match g with
        | GeometryRefusal.NonFiniteCoordinate -> "NonFiniteCoordinate"
        | GeometryRefusal.ZeroDirection -> "ZeroDirection"
        | GeometryRefusal.NonFiniteDirection -> "NonFiniteDirection"
        | GeometryRefusal.NegativeRadius -> "NegativeRadius"
        | GeometryRefusal.NonFiniteRadius -> "NonFiniteRadius"
        | GeometryRefusal.NonFiniteDistance -> "NonFiniteDistance"
        | GeometryRefusal.ZeroDivisor -> "ZeroDivisor"
        | GeometryRefusal.NonFiniteDivisor -> "NonFiniteDivisor"
        | GeometryRefusal.InvalidThreshold -> "InvalidThreshold"
        | GeometryRefusal.NotANumber -> "NotANumber"
        | GeometryRefusal.NonFiniteAngle -> "NonFiniteAngle"
        | GeometryRefusal.NonFiniteTranslation -> "NonFiniteTranslation"
        | GeometryRefusal.NonFiniteFactor -> "NonFiniteFactor"
        | GeometryRefusal.NonPositiveFactor -> "NonPositiveFactor"
        | GeometryRefusal.NonFiniteBivector -> "NonFiniteBivector"
        | GeometryRefusal.NonFiniteParameter -> "NonFiniteParameter"
        | GeometryRefusal.NegativeSquaredNorm -> "NegativeSquaredNorm"
        | GeometryRefusal.NoRotationPlane -> "NoRotationPlane"

    // ---- the cases ------------------------------------------------------------------------------

    let private applyCases: Case list =
        [ for formName, form in forms do
              yield case ("apply-add-" + formName) "GeometricOps.apply" [] [ "op", OneOp(GeometricOp.Add("b", form)) ]

              for opName, op in transforms "b" do
                  yield case ("apply-" + opName + "-" + formName) "GeometricOps.apply" (beside form) [ "op", OneOp op ]

          yield
              case
                  "apply-remove"
                  "GeometricOps.apply"
                  (beside (Form.Point origin))
                  [ "op", OneOp(GeometricOp.Remove "b") ]
          yield case "apply-a-rotation-by-the-largest-angle" "GeometricOps.apply" holdingB [ "op", OneOp largestAngle ]
          yield
              case
                  "apply-add-a-name-that-orders-first"
                  "GeometricOps.apply"
                  holdingB
                  [ "op", OneOp(GeometricOp.Add("a", snd forms[4])) ]

          for id, op in refused do
              yield case ("apply-refused-" + id) "GeometricOps.apply" holdingB [ "op", OneOp op ]

          for id, op in trigonometric "b" do
              yield case ("apply-" + id) "GeometricOps.apply" (beside (snd forms[2])) [ "op", OneOp op ] ]

    let private canApplyCases: Case list =
        [ yield
              case
                  "canApply-a-transform"
                  "GeometricOps.canApply"
                  holdingB
                  [ "op", OneOp(transforms "b" |> List.head |> snd) ]
          yield
              case
                  "canApply-an-add"
                  "GeometricOps.canApply"
                  holdingB
                  [ "op", OneOp(GeometricOp.Add("c", snd forms[5])) ]
          yield
              case
                  "canApply-a-rotation-by-the-largest-angle"
                  "GeometricOps.canApply"
                  holdingB
                  [ "op", OneOp largestAngle ]

          for id, op in refused do
              yield case ("canApply-refused-" + id) "GeometricOps.canApply" holdingB [ "op", OneOp op ] ]

    /// Histories: each kind of object transformed by each op, a refusal in the middle, names added out of
    /// order, a run of rigid ops of one object, and the angles where the pinned pair is not correctly
    /// rounded.
    let private applyAllCases: Case list =
        let everyOp (name: string) = transforms name |> List.map snd

        [ for formName, form in forms do
              yield
                  case
                      ("applyAll-every-transform-of-a-" + formName)
                      "GeometricOps.applyAll"
                      []
                      [ "ops", OpList(beside form @ everyOp "b") ]

          yield
              case
                  "applyAll-a-refusal-in-the-middle"
                  "GeometricOps.applyAll"
                  []
                  [ "ops",
                    OpList(
                        beside (snd forms[0])
                        @ [ GeometricOp.Scale("b", origin, -1.0)
                            GeometricOp.Rotate("z", origin, up, 1.0) ]
                        @ everyOp "b"
                    ) ]
          yield
              case
                  "applyAll-names-added-out-of-order"
                  "GeometricOps.applyAll"
                  []
                  [ "ops",
                    OpList(
                        [ GeometricOp.Add("c", snd forms[4])
                          GeometricOp.Add("a", snd forms[0])
                          GeometricOp.Add("b", snd forms[5])
                          GeometricOp.Remove "a"
                          GeometricOp.Add("a", snd forms[1]) ]
                    ) ]
          yield
              case
                  "applyAll-a-run-of-rigid-ops-of-one-object"
                  "GeometricOps.applyAll"
                  []
                  [ "ops",
                    OpList(
                        beside (snd forms[3])
                        @ [ GeometricOp.Rotate("b", (1.0, -2.0, 0.5), (0.0, 1.0, 1.0), 0.75)
                            GeometricOp.Translate("b", (0.5, -1.25, 2.0))
                            GeometricOp.Screw("b", (1.0, 0.0, -1.0), (1.0, 1.0, 0.0), -1.25, 0.75)
                            GeometricOp.Rotate("b", (0.0, 0.0, 0.0), (2.0, -1.0, 3.0), 2.5) ]
                    ) ]

          for id, op in trigonometric "b" do
              yield case ("applyAll-" + id) "GeometricOps.applyAll" [] [ "ops", OpList(beside (snd forms[0]) @ [ op ]) ] ]

    let private invertCases: Case list =
        [ for opName, op in transforms "b" do
              yield case ("invert-" + opName) "GeometricOps.invert" (beside (snd forms[0])) [ "op", OneOp op ]

          yield case "invert-add" "GeometricOps.invert" holdingB [ "op", OneOp(GeometricOp.Add("c", snd forms[2])) ]
          yield case "invert-remove" "GeometricOps.invert" holdingB [ "op", OneOp(GeometricOp.Remove "b") ]
          yield
              case "invert-a-rotation-by-the-largest-angle" "GeometricOps.invert" holdingB [ "op", OneOp largestAngle ]
          yield
              case
                  "invert-a-negative-zero-angle"
                  "GeometricOps.invert"
                  holdingB
                  [ "op", OneOp(GeometricOp.Rotate("b", origin, up, -0.0)) ]
          yield
              case
                  "invert-a-factor-whose-reciprocal-rounds"
                  "GeometricOps.invert"
                  holdingB
                  [ "op", OneOp(GeometricOp.Scale("b", origin, 3.0)) ]

          for id, op in refused do
              yield case ("invert-refused-" + id) "GeometricOps.invert" holdingB [ "op", OneOp op ] ]

    let private compactCases: Case list =
        let p, u = (1.0, -2.0, 0.5), (0.0, 1.0, 1.0)

        let c (id: string) (ops: Op list) =
            case ("compact-" + id) "GeometricOps.compact" (beside (snd forms[0])) [ "ops", OpList ops ]

        [ c
              "translations"
              [ GeometricOp.Translate("b", (0.1, 0.2, 0.3))
                GeometricOp.Translate("b", (0.7, -0.25, 1.0)) ]
          c
              "three-translations"
              [ GeometricOp.Translate("b", (0.1, 0.2, 0.3))
                GeometricOp.Translate("b", (0.7, -0.25, 1.0))
                GeometricOp.Translate("b", (-0.3, 0.6, 1e-17)) ]
          c "rotations-about-one-axis" [ GeometricOp.Rotate("b", p, u, 0.75); GeometricOp.Rotate("b", p, u, 0.1) ]
          c
              "a-rotation-and-screws-about-one-axis"
              [ GeometricOp.Rotate("b", p, u, 0.75)
                GeometricOp.Screw("b", p, u, -0.5, 0.25)
                GeometricOp.Screw("b", p, u, 1.5, 0.3) ]
          c
              "rotations-about-two-axes"
              [ GeometricOp.Rotate("b", p, u, 0.75)
                GeometricOp.Rotate("b", p, (1.0, 0.0, 0.0), 0.1) ]
          c
              "rotations-about-one-line-given-by-two-points"
              [ GeometricOp.Rotate("b", p, u, 0.75)
                GeometricOp.Rotate("b", (1.0, -1.0, 1.5), u, 0.1) ]
          c
              "a-rotation-and-a-translation"
              [ GeometricOp.Rotate("b", p, u, 0.75)
                GeometricOp.Translate("b", (0.1, 0.2, 0.3)) ]
          c
              "a-scale-ends-a-run"
              [ GeometricOp.Translate("b", (0.1, 0.2, 0.3))
                GeometricOp.Scale("b", origin, 2.0)
                GeometricOp.Translate("b", (0.7, -0.25, 1.0)) ]
          c
              "a-reflection-ends-a-run"
              [ GeometricOp.Translate("b", (0.1, 0.2, 0.3))
                GeometricOp.Reflect("b", up, 0.0)
                GeometricOp.Translate("b", (0.7, -0.25, 1.0)) ]
          c
              "another-object-ends-a-run"
              [ GeometricOp.Translate("b", (0.1, 0.2, 0.3))
                GeometricOp.Translate("a", (0.7, -0.25, 1.0))
                GeometricOp.Translate("b", (0.7, -0.25, 1.0)) ]
          c
              "a-refused-op-ends-a-run"
              [ GeometricOp.Translate("b", (0.1, 0.2, 0.3))
                GeometricOp.Translate("b", (0.0, Double.NaN, 0.0))
                GeometricOp.Translate("b", (0.7, -0.25, 1.0)) ]
          c
              "a-sum-that-overflows-is-not-formed"
              [ GeometricOp.Translate("b", (Double.MaxValue, 0.0, 0.0))
                GeometricOp.Translate("b", (Double.MaxValue, 0.0, 0.0)) ]
          c "a-run-of-one" [ GeometricOp.Rotate("b", p, u, -0.0) ]
          c "nothing" [] ]

    let private codecOps: (string * Op) list =
        [ for formName, form in forms -> "add-" + formName, GeometricOp.Add("b", form) ]
        @ transforms "b"
        @ [ "remove", GeometricOp.Remove "b"
            "negative-zeros", GeometricOp.Rotate("b", (-0.0, 0.0, -0.0), (0.0, -0.0, 1.0), -0.0)
            "a-subnormal-and-the-largest",
            GeometricOp.Screw("b", (5e-324, -5e-324, Double.MaxValue), up, -Double.MaxValue, 1e-310)
            "a-nan", GeometricOp.Translate("b", (Double.NaN, 0.0, 0.0))
            "the-infinities", GeometricOp.Reflect("b", (Double.PositiveInfinity, 0.0, 0.0), Double.NegativeInfinity)
            "a-name-to-escape", GeometricOp.Remove "quote \" and tab \t" ]

    let private encodeCases: Case list =
        [ for id, op in codecOps -> case ("encode-" + id) "GeometricOps.encode" [] [ "op", OneOp op ] ]

    let private decodeCases: Case list =
        [ for id, op in codecOps ->
              case ("decode-" + id) "GeometricOps.decode" [] [ "text", Text(GeometricOps.encode naming op) ] ]
        @ [ for i, (text, _) in List.indexed malformed ->
                case (sprintf "decode-malformed-%d" i) "GeometricOps.decode" [] [ "text", Text text ] ]

    let private witnessCases: Case list =
        [ for formName, form in forms do
              yield
                  case
                      ("streamWitness-every-transform-of-a-" + formName)
                      "GeometricOps.streamWitness"
                      []
                      [ "ops", OpList(beside form @ (transforms "b" |> List.map snd)) ]

          yield
              case
                  "streamWitness-a-refusal-is-not-chained"
                  "GeometricOps.streamWitness"
                  []
                  [ "ops",
                    OpList(
                        holdingB
                        @ [ GeometricOp.Scale("b", origin, -1.0); GeometricOp.Translate("b", up) ]
                    ) ]

          for id, op in trigonometric "b" do
              yield
                  case
                      ("streamWitness-" + id)
                      "GeometricOps.streamWitness"
                      []
                      [ "ops", OpList(beside (snd forms[0]) @ [ op ]) ] ]

    let private tryFindCases: Case list =
        [ case "tryFind-the-second-in-order" "GeometricState.tryFind" (beside (snd forms[3])) [ "name", Name "b" ]
          case "tryFind-the-first-in-order" "GeometricState.tryFind" (beside (snd forms[3])) [ "name", Name "a" ]
          case "tryFind-none" "GeometricState.tryFind" (beside (snd forms[3])) [ "name", Name "z" ] ]

    /// Every authored case, in document order.
    let authored: Case list =
        applyCases
        @ canApplyCases
        @ applyAllCases
        @ invertCases
        @ compactCases
        @ encodeCases
        @ decodeCases
        @ witnessCases
        @ tryFindCases

    // ---- the drawn cases: what the suite's search draws -------------------------------------------

    /// A pinned generator (Park and Miller's minimal standard, every step exact), so an index draws the
    /// same cases on every host. A WIDE draw (Phase 731's second round) also draws coordinates of an
    /// overflowing scale and factors and radii that are subnormal or whose reciprocal overflows; a narrow
    /// draw is the stream it always was.
    type private Draw(seed: int, wide: bool) =
        let mutable state =
            ((float seed % 2147483646.0) + 2147483646.0) % 2147483646.0 + 1.0

        member _.Next() : float =
            state <- (state * 48271.0) % 2147483647.0
            state

        member this.Int (lo: int) (hi: int) : int =
            lo + int (this.Next() % float (hi - lo + 1))

        /// A coordinate: a multiple of a quarter, a zero of either sign, one whose square underflows to a
        /// zero, one whose square is subnormal, one of full precision, or one that is not finite.
        member this.Number() : float =
            match this.Int 0 10 with
            | 10 -> [| Double.NaN; Double.PositiveInfinity; Double.NegativeInfinity |][this.Int 0 2]
            | 0 -> 0.0
            | 1 -> -0.0
            | 2 -> (if this.Int 0 1 = 0 then 1e-170 else -3e-200)
            | 3 -> (if this.Int 0 1 = 0 then 1e-160 else -2e-155)
            | 4 when wide -> [| 1e150; -1e200; 1e300; -Double.MaxValue |][this.Int 0 3]
            | 4
            | 5 -> (this.Next() / 1073741824.0 - 1.0) * 4.0
            | _ -> float (this.Int -32 32) / 4.0

        member this.Triple() : float * float * float =
            this.Number(), this.Number(), this.Number()

        member this.Direction() : float * float * float =
            match this.Int 0 5 with
            | 0 -> 0.0, -0.0, float (this.Int 1 3)
            | 1 -> 1e-170, 0.0, 0.0
            | _ -> float (this.Int -3 3), float (this.Int -3 3), float (this.Int 1 3)

        member this.Angle() : float =
            match this.Int 0 6 with
            | 0 -> -0.0
            | 6 -> Double.MaxValue
            | 1 -> sineAngle
            | 2 -> cosineAngle
            | _ -> (this.Next() / 1073741824.0 - 1.0) * 3.0

        member this.Positive() : float =
            if wide && this.Int 0 3 = 0 then
                [| 1e-310; 5e-324; 1e-308; 1e300 |][this.Int 0 3]
            else
                0.25 + this.Next() / 536870912.0

        /// A coordinate of up to about `4e160`: a draw in `(−4, 4)` times ten, a drawn number of times
        /// from zero to 160, each a rounded product (Phase 731's third round).
        member this.Big() : float =
            let mutable x = (this.Next() / 1073741824.0 - 1.0) * 4.0

            // The count is drawn once, before the loop: a loop bound that calls the generator is read on every
            // pass under Fable and once on .NET.
            let times = this.Int 0 160

            for _ in 1..times do
                x <- x * 10.0

            x

        /// `10^k` for a drawn `k` in `[−30, 30]`, by rounded products or quotients of ten.
        member this.PowerOfTen() : float =
            let k = this.Int -30 30
            let mutable x = 1.0

            for _ in 1 .. (if k < 0 then -k else k) do
                x <- if k > 0 then x * 10.0 else x / 10.0

            x

    let private drawnForm (d: Draw) : Form =
        match d.Int 0 5 with
        | 0 -> Form.Point(d.Triple())
        | 1 -> Form.PointPair(d.Triple(), d.Triple())
        | 2 -> Form.Line(d.Triple(), d.Direction())
        | 3 -> Form.Circle(d.Triple(), d.Positive(), d.Direction())
        | 4 -> Form.Plane(d.Direction(), d.Number())
        | _ -> Form.Sphere(d.Triple(), d.Positive())

    let private drawnOp (d: Draw) : Op =
        let name = [| "a"; "b"; "c" |][d.Int 0 2]

        match d.Int 0 9 with
        | 0 -> GeometricOp.Add(name, drawnForm d)
        | 1 -> GeometricOp.Remove name
        | 2
        | 3 -> GeometricOp.Rotate(name, d.Triple(), d.Direction(), d.Angle())
        | 4 -> GeometricOp.Translate(name, d.Triple())
        | 5 -> GeometricOp.Scale(name, d.Triple(), (if d.Int 0 7 = 0 then -d.Positive() else d.Positive()))
        | 6 -> GeometricOp.Reflect(name, d.Direction(), d.Number())
        | _ -> GeometricOp.Screw(name, d.Triple(), d.Direction(), d.Angle(), d.Number())

    let private searchedIn (wide: bool) (index: int) : Case list =
        let d = Draw(731 * 1000 + index, wide)
        let start = [ GeometricOp.Add("a", drawnForm d); GeometricOp.Add("b", drawnForm d) ]
        let op = drawnOp d
        let ops = List.init (d.Int 1 6) (fun _ -> drawnOp d)
        let p, u = d.Triple(), d.Direction()

        let run =
            List.init (d.Int 2 4) (fun _ ->
                match d.Int 0 2 with
                | 0 -> GeometricOp.Translate("b", d.Triple())
                | 1 -> GeometricOp.Rotate("b", p, u, d.Angle())
                | _ -> GeometricOp.Screw("b", p, u, d.Angle(), d.Number()))

        let id (f: string) =
            sprintf "drawn-%s%d-%s" (if wide then "wide-" else "") index f

        [ case (id "apply") "GeometricOps.apply" start [ "op", OneOp op ]
          case (id "canApply") "GeometricOps.canApply" start [ "op", OneOp op ]
          case (id "applyAll") "GeometricOps.applyAll" start [ "ops", OpList ops ]
          case (id "invert") "GeometricOps.invert" start [ "op", OneOp op ]
          case (id "compact") "GeometricOps.compact" start [ "ops", OpList(run @ ops) ]
          case (id "encode") "GeometricOps.encode" [] [ "op", OneOp op ]
          case (id "decode") "GeometricOps.decode" [] [ "text", Text(GeometricOps.encode naming op) ]
          case (id "streamWitness") "GeometricOps.streamWitness" start [ "ops", OpList ops ]
          case (id "tryFind") "GeometricState.tryFind" start [ "name", Name(nameOf op) ] ]

    /// The cases the suite's search draws at `index`: one of each published function that computes.
    let searched (index: int) : Case list = searchedIn false index

    /// The wide cases the suite's search draws at `index`, the same shape over wide draws.
    let searchedWide (index: int) : Case list = searchedIn true index

    /// The deep cases the suite's search draws at `index` (Phase 731's third round): one object at an
    /// overflowing scale, a HISTORY of a transform of it drawn at that scale (a scale by `10^k`, a rotation,
    /// a translation, a reflection or a screw), then two or more small translations and a rotation about
    /// one axis. A port that changes the object the first transform builds can tip whether a later op is
    /// refused `NotANumber`, and so what a later function answers: `compact`, `applyAll` and
    /// `streamWitness` over the whole list, and `canApply` and `invert` of the next op over the state the
    /// history reaches through the functions under test.
    let searchedDeep (index: int) : Case list =
        let d = Draw(733 * 1000 + index, false)
        let big () = d.Big(), d.Big(), d.Big()
        let small () = d.Number(), d.Number(), d.Number()

        let start =
            [ GeometricOp.Add(
                  "b",
                  match d.Int 0 2 with
                  | 0 -> Form.Point(big ())
                  | 1 -> Form.PointPair(big (), big ())
                  | _ -> Form.Sphere(big (), d.Positive())
              ) ]

        let first =
            match d.Int 0 4 with
            | 0 -> GeometricOp.Scale("b", big (), d.PowerOfTen())
            | 1 -> GeometricOp.Rotate("b", big (), d.Direction(), d.Angle())
            | 2 -> GeometricOp.Translate("b", big ())
            | 3 -> GeometricOp.Reflect("b", d.Direction(), d.Big())
            | _ -> GeometricOp.Screw("b", big (), d.Direction(), d.Angle(), d.Big())

        let p, u = small (), d.Direction()

        let later =
            List.init (d.Int 2 3) (fun _ -> GeometricOp.Translate("b", small ()))
            @ [ GeometricOp.Rotate("b", p, u, d.Angle())
                GeometricOp.Rotate("b", p, u, d.Angle()) ]

        let ops = first :: later
        let id (f: string) = sprintf "drawn-deep-%d-%s" index f

        [ case (id "compact") "GeometricOps.compact" start [ "ops", OpList ops ]
          case (id "applyAll") "GeometricOps.applyAll" start [ "ops", OpList ops ]
          case (id "streamWitness") "GeometricOps.streamWitness" start [ "ops", OpList ops ]
          case (id "canApply") "GeometricOps.canApply" start [ "history", OpList [ first ]; "op", OneOp later.Head ]
          case (id "invert") "GeometricOps.invert" start [ "history", OpList [ first ]; "op", OneOp later.Head ] ]

    // ---- the answers and the document -------------------------------------------------------------

    let private floatCell = GeometricAlgebraVectors.floatCell

    let private objectCell (o: GeometricObject) : CorpusCell =
        let kind, values =
            match o with
            | GeometricObject.Point x -> "point", GeometricAlgebraVectors.pointCell x
            | GeometricObject.PointPair x -> "pointPair", GeometricAlgebraVectors.pairCell x
            | GeometricObject.Line x -> "line", GeometricAlgebraVectors.lineCell x
            | GeometricObject.Circle x -> "circle", GeometricAlgebraVectors.circleCell x
            | GeometricObject.Plane x -> "plane", GeometricAlgebraVectors.planeCell x
            | GeometricObject.Sphere x -> "sphere", GeometricAlgebraVectors.sphereCell x

        CorpusCell.Row [ "kind", CorpusFile.str kind; "coefficients", values ]

    let private stateCell (s: State) : CorpusCell =
        CorpusCell.List
            [ for n, o in GeometricState.toList s -> CorpusCell.Row [ "name", CorpusFile.str n; "object", objectCell o ] ]

    /// A refusal's name: its case, and the algebra's case it carries.
    let rec refusalName (r: Refusal) : string =
        match r with
        | GeometricOpRefusal.UnknownName n -> "UnknownName " + n
        | GeometricOpRefusal.NameAlreadyPresent n -> "NameAlreadyPresent " + n
        | GeometricOpRefusal.Geometry g -> "Geometry " + geometryName g
        | GeometricOpRefusal.NotANumber n -> "NotANumber " + n
        | GeometricOpRefusal.NotInvertible op -> "NotInvertible " + op
        | GeometricOpRefusal.InverseRefused inner -> "InverseRefused " + refusalName inner

    let private refusedCell (r: Refusal) : CorpusCell =
        CorpusCell.Row [ "refused", CorpusFile.str (refusalName r) ]

    let private opCell (op: Op) : CorpusCell =
        CorpusFile.str (GeometricOps.encode naming op)

    let private argument (c: Case) (name: string) : Argument =
        c.Input |> List.find (fun (n, _) -> n = name) |> snd

    let private oneOp (c: Case) =
        match argument c "op" with
        | OneOp op -> op
        | _ -> failwithf "%s: no op" c.Id

    let private ops (c: Case) =
        match argument c "ops" with
        | OpList xs -> xs
        | _ -> failwithf "%s: no ops" c.Id

    /// A case's answer through the functions `f`, from the start state the published `applyAll` builds,
    /// and then, where the case has a `history`, each of its ops applied in turn by `f.Apply`, a refused one
    /// leaving the state as it was.
    let answerWith (f: Functions) (c: Case) : CorpusCell =
        let initial = fst (published.ApplyAll c.Start (GeometricState.empty naming))

        let start =
            match c.Input |> List.tryFind (fun (n, _) -> n = "history") with
            | Some(_, OpList history) ->
                history
                |> List.fold
                    (fun s op ->
                        match f.Apply op s with
                        | Ok next -> next
                        | Error _ -> s)
                    initial
            | _ -> initial

        match c.Op with
        | "GeometricOps.apply" ->
            match f.Apply (oneOp c) start with
            | Ok s -> stateCell s
            | Error r -> refusedCell r
        | "GeometricOps.canApply" ->
            match f.CanApply (oneOp c) start with
            | Ok() -> CorpusFile.str "ok"
            | Error r -> refusedCell r
        | "GeometricOps.applyAll" ->
            let s, refusals = f.ApplyAll (ops c) start

            CorpusCell.Row
                [ "state", stateCell s
                  "refused",
                  CorpusCell.List
                      [ for i, r in refusals ->
                            CorpusCell.Row [ "index", CorpusFile.num i; "refusal", CorpusFile.str (refusalName r) ] ] ]
        | "GeometricOps.invert" ->
            match f.Invert (oneOp c) start with
            | Ok op -> opCell op
            | Error r -> refusedCell r
        | "GeometricOps.compact" -> CorpusCell.List [ for op in f.Compact (ops c) start -> opCell op ]
        | "GeometricOps.encode" -> CorpusFile.str (f.Encode(oneOp c))
        | "GeometricOps.decode" ->
            match argument c "text" with
            | Text t ->
                match f.Decode t with
                | Ok op -> opCell op
                | Error e -> CorpusCell.Row [ "refused", CorpusFile.str e ]
            | _ -> failwithf "%s: no text" c.Id
        | "GeometricOps.streamWitness" ->
            let stream = OpStream.canonical OpStream.defaultHash f.Witness
            let actor = Actor.ofLegacyString "vectors"

            let state, records =
                ops c
                |> List.fold
                    (fun (s, records) op ->
                        match OpStream.append stream actor op s records with
                        | Ok(s', records') -> s', records'
                        | Error _ -> s, records)
                    (start, OpStream.empty)

            let replayed =
                match OpStream.replay f.Witness start records with
                | Ok s -> stateCell s
                | Error(i, r) ->
                    CorpusCell.Row [ "halted", CorpusFile.num i; "refusal", CorpusFile.str (refusalName r) ]

            CorpusCell.Row
                [ "hashes", CorpusCell.List [ for r in records -> CorpusFile.str r.Hash ]
                  "state", stateCell state
                  "replayed", replayed ]
        | "GeometricState.tryFind" ->
            match argument c "name" with
            | Name n ->
                match f.TryFind n start with
                | Some o -> objectCell o
                | None -> CorpusFile.str "none"
            | _ -> failwithf "%s: no name" c.Id
        | other -> failwithf "%s: no function %s" c.Id other

    let private argumentCell (a: Argument) : CorpusCell =
        match a with
        | OneOp op -> opCell op
        | OpList xs -> CorpusCell.List [ for op in xs -> opCell op ]
        | Text t -> CorpusFile.str t
        | Name n -> CorpusFile.str n

    let private caseCell (c: Case) : CorpusCell =
        CorpusCell.Row
            [ "id", CorpusFile.str c.Id
              "op", CorpusFile.str c.Op
              "start", CorpusCell.List [ for op in c.Start -> opCell op ]
              "input", CorpusCell.Row [ for name, a in c.Input -> name, argumentCell a ]
              "expected", answerWith published c ]

    /// Phase 731's second round (DECISIONS.md D90 items 5 and 8): an op whose object would hold a NaN is
    /// refused, one for each op that reaches it, from a point whose squared length has overflowed (an
    /// infinity, which is a value); and an inverse the state after the op would refuse is not answered,
    /// one for each op whose inverse reaches it.
    let secondRound: Case list =
        let farPoint = [ GeometricOp.Add("b", Form.Point(1e200, 0.0, 0.0)) ]
        let near = [ GeometricOp.Add("b", Form.Point(1.0, 2.0, 3.0)) ]
        let originPoint = [ GeometricOp.Add("b", Form.Point(0.0, 0.0, 0.0)) ]
        let up = 0.0, 0.0, 1.0

        let nan =
            [ "add-a-pair-through-a-far-point",
              [],
              GeometricOp.Add("b", Form.PointPair((1e200, 0.0, 0.0), (0.0, 1.0, 0.0)))
              "translate-a-far-point", farPoint, GeometricOp.Translate("b", (1.0, 0.0, 0.0))
              "rotate-a-far-point", farPoint, GeometricOp.Rotate("b", origin, up, 1.0)
              "scale-a-far-point", farPoint, GeometricOp.Scale("b", (1.0, 0.0, 0.0), 2.0)
              "reflect-a-far-point", farPoint, GeometricOp.Reflect("b", (1.0, 0.0, 0.0), 0.0)
              "screw-a-far-point", farPoint, GeometricOp.Screw("b", origin, up, 1.0, 1.0) ]

        let inverses =
            [ "scale-whose-reciprocal-overflows", originPoint, GeometricOp.Scale("b", origin, 1e-310)
              "scale-whose-inverse-holds-a-nan",
              [ GeometricOp.Add("b", Form.Point(1.0, 0.0, 0.0)) ],
              GeometricOp.Scale("b", origin, 1e-308)
              "translate-far", near, GeometricOp.Translate("b", (1e300, 0.0, 0.0))
              "rotate-about-a-far-axis", near, GeometricOp.Rotate("b", (1e300, 0.0, 0.0), up, 1.0)
              "reflect-in-a-far-plane", near, GeometricOp.Reflect("b", (1.0, 0.0, 0.0), 1e300)
              "screw-far-along-its-axis", near, GeometricOp.Screw("b", origin, up, 1.0, 1e300) ]

        [ for id, start, op in nan do
              yield case ("apply-not-a-number-" + id) "GeometricOps.apply" start [ "op", OneOp op ]
              yield case ("canApply-not-a-number-" + id) "GeometricOps.canApply" start [ "op", OneOp op ]
              yield case ("applyAll-not-a-number-" + id) "GeometricOps.applyAll" start [ "ops", OpList [ op ] ]

          for id, start, op in inverses do
              yield case ("invert-refused-inverse-" + id) "GeometricOps.invert" start [ "op", OneOp op ]

          // A translation undone by its inverse: not exact on a point of tenths, where `0.1 + 0.7 − 0.7` is
          // not `0.1`; exact in value on a point of quarters, where a `-0.0` coordinate comes back `+0.0`.
          yield
              case
                  "applyAll-a-translation-undone-on-a-point-of-tenths"
                  "GeometricOps.applyAll"
                  [ GeometricOp.Add("b", Form.Point(0.1, 0.0, 0.0)) ]
                  [ "ops",
                    OpList
                        [ GeometricOp.Translate("b", (0.7, 0.0, 0.0))
                          GeometricOp.Translate("b", (-0.7, -0.0, -0.0)) ] ]
          yield
              case
                  "applyAll-a-translation-undone-on-a-point-of-quarters"
                  "GeometricOps.applyAll"
                  [ GeometricOp.Add("b", Form.Point(-0.0, 2.5, 5.25)) ]
                  [ "ops",
                    OpList
                        [ GeometricOp.Translate("b", (4.25, -0.0, -0.5))
                          GeometricOp.Translate("b", (-4.25, 0.0, 0.5)) ] ]

          // Phase 731's third round: quarters too large for every product of the sandwich to be a double.
          // A point pair of quarters near 1e5, translated and back, is not the pair it was in value.
          yield
              case
                  "applyAll-a-translation-undone-on-a-pair-of-large-quarters"
                  "GeometricOps.applyAll"
                  [ GeometricOp.Add(
                        "b",
                        Form.PointPair((-76508.0, 95112.0, -25822.75), (-69405.75, 92833.25, -80931.25))
                    ) ]
                  [ "ops",
                    OpList
                        [ GeometricOp.Translate("b", (-86017.25, -85842.5, -3598.0))
                          GeometricOp.Translate("b", (86017.25, 85842.5, 3598.0)) ] ] ]

    /// Every case, in document order: the authored ones, and the drawn ones the suite's search chose as
    /// witnesses.
    let cases: Case list =
        authored
        @ [ for index, id in chosen -> searched index |> List.find (fun c -> c.Id = id) ]
        @ secondRound
        @ [ for index, id in chosenWide -> searchedWide index |> List.find (fun c -> c.Id = id) ]
        @ [ for index, id in chosenDeep -> searchedDeep index |> List.find (fun c -> c.Id = id) ]

    let private description =
        "The reference answers for Fuaran.Compute.GeometricAlgebraOps over AUTHORED inputs (Phase 731, "
        + "DECISIONS.md D85 item 9 and D90): geometric transforms as a parametric op algebra over a state of "
        + "named objects. `op` names a published function of the module `GeometricOps` or `GeometricState`. "
        + "`start` is the ops `applyAll` runs from the empty state to make the state the function is given, "
        + "and every op is written as its canonical wire text: `{\"$type\": …}` with its members sorted, the "
        + "name as it is, and every float its IEEE-754 binary64 bit pattern in sixteen lowercase hex digits, "
        + "a triple's three concatenated, a NaN `7ff8000000000000`. Names are strings compared ordinally. A "
        + "state is the list of its objects in ascending order of name, each its kind and the coefficients "
        + "its type carries in index order (the blade order of geometric-algebra-laws.json), each float its "
        + "bit pattern: a vector is evidence, and no stream carries a coefficient. A refused answer is `{ "
        + "refused: <the case> }`, a constructor's refusal carried as `Geometry <the algebra's case>`. "
        + "`applyAll` answers the state and each refused op's index; `compact` the ops it answers; "
        + "`streamWitness` the hash of each record an `OpStream.append` with the canonical config and the "
        + "default hash chained, the state it reached, and the state `OpStream.replay` reaches from the "
        + "records. An object or a versor is built by Fuaran.Compute.GeometricAlgebra's published "
        + "constructor and applied by its published `applyTo…`, and nothing is normalised. A rotation is "
        + "`Motor.ofScrew point direction angle 0.0`. A vector whose id says `not correctly rounded` "
        + "rotates by twice an argument where the pinned `sinCos` is not the correctly rounded answer."

    /// The family's document as the one corpus writer renders it.
    let render () : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "geometricAlgebraOps"
                  "description", CorpusFile.str description ]
              Items = "vectors"
              Rows = cases |> List.map caseCell }
