namespace Fuaran.Compute.Tests

// ============================================================================
//  Phase 593 — the elementary-function law vectors: authored hard inputs,
//  each answer computed by calling Fuaran.Compute.Elementary, rendered through
//  the substrate's one corpus writer (`CorpusFile`, Phase 543's route) into
//  `conformance/laws/elementary-laws.json`. Phase 630 moved them here from
//  the linear-algebra vectors with the module, row for row: every vector's
//  line is the line it was (DECISIONS.md D74).
//
//  The inputs are the reduction boundaries, both edges of the computed range,
//  subnormal results and inputs, the near-one band of ln, the exact shortcuts
//  of pow, and every row of D43's special-value table. Every float is written
//  as its IEEE-754 binary64 bit pattern, so a host compares its answer byte
//  for byte.
//
//  Fable-clean: the node leg (LinearAlgebraNode/) compiles this file and
//  renders the same document under JavaScript, and the suite holds the two
//  renderings and the committed file equal.
// ============================================================================

open System
open Fuaran.Core
open Fuaran.Compute.Elementary

module ElementaryVectors =

    /// The file this family is written to under `laws/`.
    let fileName = "elementary-laws.json"

    /// One authored case: an elementary function over named scalar operands.
    type Case =
        { Id: string
          Op: string
          Input: (string * float) list }

    /// A float's cell: its bit pattern as sixteen lowercase hex digits, or `nan` for any NaN — the one
    /// NaN every function answers has a host-specific bit pattern (.NET's `Double.NaN` sets the sign
    /// bit, JavaScript's does not), so the pattern of a NaN is not compared.
    let hex (x: float) : string =
        if Double.IsNaN x then
            "nan"
        else
            let b = BitConverter.DoubleToInt64Bits x
            let digits = "0123456789abcdef"

            Array.init 16 (fun k -> string digits[int ((b >>> (4 * (15 - k))) &&& 0xFL)])
            |> String.concat ""

    let private scalar (c: Case) (name: string) : float =
        match c.Input |> List.tryFind (fun (n, _) -> n = name) with
        | Some(_, x) -> x
        | None -> failwithf "elementary vector %s has no operand %s" c.Id name

    let private tableName (s: SpecialValue) : string =
        match s with
        | SpecialValue.NaN -> "nan"
        | SpecialValue.Value _ -> "value"
        | SpecialValue.Undefined -> "undefined"
        | SpecialValue.Compute -> "compute"

    /// What D43's table says of the case's input and what the function answers: `(table, answer)`.
    let private elementary (c: Case) : SpecialValue * float =
        match c.Op with
        | "exp" ->
            let x = scalar c "x"
            Elementary.expTable x, Elementary.exp x
        | "ln" ->
            let x = scalar c "x"
            Elementary.lnTable x, Elementary.ln x
        | "log10" ->
            let x = scalar c "x"
            Elementary.lnTable x, Elementary.log10 x
        | "pow" ->
            let x, y = scalar c "x", scalar c "y"
            Elementary.powTable x y, Elementary.pow x y
        | other -> failwithf "elementary vector %s: %s is not an elementary function" c.Id other

    let private elementaryCell (c: Case) : CorpusCell =
        let table, answer = elementary c

        CorpusCell.Row
            [ "verdict", CorpusFile.str "ok"
              "value", CorpusFile.str (hex answer)
              "table", CorpusFile.str (tableName table) ]

    /// The float whose bit pattern is these sixteen hex digits.
    let ofHex (h: string) : float =
        let mutable b = 0L

        for ch in h do
            let d =
                if ch >= '0' && ch <= '9' then
                    int ch - int '0'
                else
                    int ch - int 'a' + 10

            b <- (b <<< 4) ||| int64 d

        BitConverter.Int64BitsToDouble b

    let private ordered (x: float) : int64 =
        let b = BitConverter.DoubleToInt64Bits x
        if b < 0L then Int64.MinValue - b else b

    /// The distance between two floats in units in the last place: the difference of their ordered
    /// bit patterns (so an infinity and the largest finite double are one apart).
    let ulpsApart (a: float) (b: float) : int64 = abs (ordered a - ordered b)

    /// The reference answer is within this many units in the last place of the package's, on every
    /// vector that carries one (DECISIONS.md D52: the faithful-pinned build is within one).
    let referenceUlps = 1

    /// The correctly rounded answer for every vector the routine computes, as its bit pattern: the
    /// true value to eighty decimal digits (Python's `decimal`: `exp`, `ln`, `log10`, and `pow` as
    /// `exp(y · ln x)`) rounded once to the nearest double. A vector the table answers carries none.
    let references: (string * string) list =
        [ "exp-one", "4005bf0a8b145769"
          "exp-minus-one", "3fd78b56362cef38"
          "exp-at-half-ln2", "3ff6a09e667f3bcc"
          "exp-above-half-ln2", "3ff6a09e667f3bcd"
          "exp-three-halves-ln2", "4006a09e667f3bcc"
          "exp-overflow-edge", "7fefffffffffff2a"
          "exp-underflow-edge", "0000000000000000"
          "exp-subnormal-result", "0000000000000001"
          "exp-just-below-min-normal", "000ff15b469edf89"
          "exp-just-above-min-normal", "00119e98b83de7a3"
          "exp-tiny", "3ff000000006df38"
          "exp-below-two-to-minus-28", "3ff0000000ce288f"
          "exp-at-two-to-minus-28", "3ff0000001000000"
          "exp-negative-tiny", "3feffffffea86712"
          "exp-zero", "3ff0000000000000"
          "exp-negative-zero", "3ff0000000000000"
          "exp-ln-two-to-128", "47efffffdffffff5"
          "exp-minus-700", "00d14f2b0fb9307f"
          "exp-fifty-and-half", "447cf65319c5751a"
          "ln-two", "3fe62e42fefa39ef"
          "ln-half", "bfe62e42fefa39ef"
          "ln-ten", "40026bb1bbb55516"
          "ln-three", "3ff193ea7aad030b"
          "ln-e", "3ff0000000000000"
          "ln-just-above-one", "3cafffffffffffff"
          "ln-just-below-one", "bca0000000000000"
          "ln-one-plus-two-to-minus-20", "3eafffff00000aab"
          "ln-one-plus-below-two-to-minus-20", "3eaffffeffe00aad"
          "ln-one-minus-two-to-minus-20", "beb0000080000555"
          "ln-at-root-two", "3fd62e42fefa39f0"
          "ln-above-root-two", "3fd62e42fefa39f3"
          "ln-max", "40862e42fefa39ef"
          "ln-min-normal", "c086232bdd7abcd2"
          "ln-max-subnormal", "c086232bdd7abcd2"
          "ln-min-subnormal", "c0874385446d71c3"
          "ln-1e300", "4085963447f87fb5"
          "ln-1e-300", "c085963447f87fb5"
          "ln-tenth", "c0026bb1bbb55515"
          "ln-seven", "3fff2272ae325a57"
          "log10-ten", "3ff0000000000000"
          "log10-hundred", "4000000000000000"
          "log10-thousand", "4008000000000000"
          "log10-1e22", "4036000000000000"
          "log10-1e23", "4037000000000000"
          "log10-thousandth", "c008000000000000"
          "log10-two", "3fd34413509f79ff"
          "log10-half", "bfd34413509f79ff"
          "log10-1e-300", "c072c00000000000"
          "log10-min-subnormal", "c07434e6420f4374"
          "log10-max", "40734413509f79ff"
          "log10-just-above-one", "3c9bcb7b1526e50d"
          "log10-seven", "3feb0b0b0b78cc3f"
          "pow-root-shortcut", "3ff6a09e667f3bcd"
          "pow-square-shortcut", "4059000000000000"
          "pow-reciprocal-shortcut", "3fb999999999999a"
          "pow-identity-shortcut", "7e37e43c8800759c"
          "pow-two-ten-and-half", "4096a09e667f3bcd"
          "pow-ten-tiny-exponent", "3ff00000009e3b81"
          "pow-near-one-huge-exponent", "3ff3fa60615291ee"
          "pow-below-one-huge-negative-exponent", "400847c089d2b399"
          "pow-near-overflow", "7fe6a09e667f3bcd"
          "pow-subnormal-result", "0000000000000001"
          "pow-exact-min-subnormal", "0000000000000001"
          "pow-two-cubed", "4020000000000000"
          "pow-two-to-1023", "7fe0000000000000"
          "pow-generic", "4104ac21590dc051"
          "pow-negative-exponent", "3914c4e977ba1f74"
          "pow-three-seven-and-half", "40ad97fd7fdd4adf"
          "pow-subnormal-base-root", "1eb67e93ddbc0e73"
          "pow-overflow", "7ff0000000000000"
          "pow-underflow", "0000000000000000"
          "pow-two-to-1024", "7ff0000000000000"
          "pow-half-to-minus-1024", "7ff0000000000000"
          "pow-seven-tiny-exponent", "3ff0000000000000"
          "pow-small-base-sixty", "01a56e1fc2f8f376"
          "pow-tenth-minus-300", "7e37e43c8800752c"
          "pow-negative-base-odd", "c020000000000000"
          "pow-negative-base-even", "4010000000000000"
          "pow-negative-base-huge-even-exponent", "7ff0000000000000" ]

    /// The reference cells of an elementary case that carries one: the correctly rounded bits and the
    /// package's distance from them in units in the last place.
    let private referenceCells (c: Case) : (string * CorpusCell) list =
        match references |> List.tryFind (fun (id, _) -> id = c.Id) with
        | None -> []
        | Some(_, h) ->
            let _, answer = elementary c

            [ "reference", CorpusFile.str h
              "ulps", CorpusFile.num (int (ulpsApart answer (ofHex h))) ]

    /// The reference answer for one case, by calling the package.
    let expected (c: Case) : CorpusCell = elementaryCell c

    let private tiny = Double.Epsilon
    let private inf = Double.PositiveInfinity
    let private nan = Double.NaN

    let private case (id: string) (op: string) (input: (string * float) list) : Case =
        { Id = id; Op = op; Input = input }

    /// Every authored case, in file order.
    let cases: Case list =
        [ case "exp-one" "exp" [ "x", 1.0 ]
          case "exp-minus-one" "exp" [ "x", -1.0 ]
          case "exp-at-half-ln2" "exp" [ "x", 0.34657359027997264 ]
          case "exp-above-half-ln2" "exp" [ "x", 0.3465735902799727 ]
          case "exp-three-halves-ln2" "exp" [ "x", 1.0397207708399179 ]
          case "exp-overflow-edge" "exp" [ "x", 709.782712893384 ]
          case "exp-above-overflow-edge" "exp" [ "x", 709.7827128933841 ]
          case "exp-underflow-edge" "exp" [ "x", -745.1332191019412 ]
          case "exp-below-underflow-edge" "exp" [ "x", -745.14 ]
          case "exp-subnormal-result" "exp" [ "x", -745.0 ]
          case "exp-just-below-min-normal" "exp" [ "x", -708.4 ]
          case "exp-just-above-min-normal" "exp" [ "x", -708.3 ]
          case "exp-tiny" "exp" [ "x", 1e-10 ]
          case "exp-below-two-to-minus-28" "exp" [ "x", 3e-09 ]
          case "exp-at-two-to-minus-28" "exp" [ "x", 3.725290298461914e-09 ]
          case "exp-negative-tiny" "exp" [ "x", -2.5e-09 ]
          case "exp-zero" "exp" [ "x", 0.0 ]
          case "exp-negative-zero" "exp" [ "x", -0.0 ]
          case "exp-ln-two-to-128" "exp" [ "x", 88.72283905206835 ]
          case "exp-minus-700" "exp" [ "x", -700.0 ]
          case "exp-fifty-and-half" "exp" [ "x", 50.5 ]
          case "exp-thousand" "exp" [ "x", 1000.0 ]
          case "exp-minus-thousand" "exp" [ "x", -1000.0 ]
          case "exp-infinity" "exp" [ "x", inf ]
          case "exp-negative-infinity" "exp" [ "x", -inf ]
          case "exp-nan" "exp" [ "x", nan ]
          case "ln-two" "ln" [ "x", 2.0 ]
          case "ln-half" "ln" [ "x", 0.5 ]
          case "ln-ten" "ln" [ "x", 10.0 ]
          case "ln-three" "ln" [ "x", 3.0 ]
          case "ln-e" "ln" [ "x", 2.718281828459045 ]
          case "ln-just-above-one" "ln" [ "x", 1.0000000000000002 ]
          case "ln-just-below-one" "ln" [ "x", 0.9999999999999999 ]
          case "ln-one-plus-two-to-minus-20" "ln" [ "x", 1.0000009536743164 ]
          case "ln-one-plus-below-two-to-minus-20" "ln" [ "x", 1.0000009536743162 ]
          case "ln-one-minus-two-to-minus-20" "ln" [ "x", 0.9999990463256836 ]
          case "ln-at-root-two" "ln" [ "x", 1.4142135623730951 ]
          case "ln-above-root-two" "ln" [ "x", 1.4142135623730954 ]
          case "ln-max" "ln" [ "x", Double.MaxValue ]
          case "ln-min-normal" "ln" [ "x", 2.2250738585072014e-308 ]
          case "ln-max-subnormal" "ln" [ "x", 2.225073858507201e-308 ]
          case "ln-min-subnormal" "ln" [ "x", tiny ]
          case "ln-1e300" "ln" [ "x", 1e+300 ]
          case "ln-1e-300" "ln" [ "x", 1e-300 ]
          case "ln-tenth" "ln" [ "x", 0.1 ]
          case "ln-seven" "ln" [ "x", 7.0 ]
          case "ln-zero" "ln" [ "x", 0.0 ]
          case "ln-negative-zero" "ln" [ "x", -0.0 ]
          case "ln-negative" "ln" [ "x", -1.0 ]
          case "ln-one" "ln" [ "x", 1.0 ]
          case "ln-infinity" "ln" [ "x", inf ]
          case "ln-nan" "ln" [ "x", nan ]
          case "log10-ten" "log10" [ "x", 10.0 ]
          case "log10-hundred" "log10" [ "x", 100.0 ]
          case "log10-thousand" "log10" [ "x", 1000.0 ]
          case "log10-1e22" "log10" [ "x", 1e+22 ]
          case "log10-1e23" "log10" [ "x", 1e+23 ]
          case "log10-thousandth" "log10" [ "x", 0.001 ]
          case "log10-two" "log10" [ "x", 2.0 ]
          case "log10-half" "log10" [ "x", 0.5 ]
          case "log10-1e-300" "log10" [ "x", 1e-300 ]
          case "log10-min-subnormal" "log10" [ "x", tiny ]
          case "log10-max" "log10" [ "x", Double.MaxValue ]
          case "log10-just-above-one" "log10" [ "x", 1.0000000000000002 ]
          case "log10-seven" "log10" [ "x", 7.0 ]
          case "log10-zero" "log10" [ "x", 0.0 ]
          case "log10-one" "log10" [ "x", 1.0 ]
          case "log10-nan" "log10" [ "x", nan ]
          case "pow-root-shortcut" "pow" [ "x", 2.0; "y", 0.5 ]
          case "pow-square-shortcut" "pow" [ "x", 10.0; "y", 2.0 ]
          case "pow-reciprocal-shortcut" "pow" [ "x", 10.0; "y", -1.0 ]
          case "pow-identity-shortcut" "pow" [ "x", 1e+300; "y", 1.0 ]
          case "pow-two-ten-and-half" "pow" [ "x", 2.0; "y", 10.5 ]
          case "pow-ten-tiny-exponent" "pow" [ "x", 10.0; "y", 1e-09 ]
          case "pow-near-one-huge-exponent" "pow" [ "x", 1.0000000000000002; "y", 1000000000000000.0 ]
          case "pow-below-one-huge-negative-exponent" "pow" [ "x", 0.9999999999999999; "y", -1e+16 ]
          case "pow-near-overflow" "pow" [ "x", 2.0; "y", 1023.5 ]
          case "pow-subnormal-result" "pow" [ "x", 2.0; "y", -1074.5 ]
          case "pow-exact-min-subnormal" "pow" [ "x", 0.5; "y", 1074.0 ]
          case "pow-two-cubed" "pow" [ "x", 2.0; "y", 3.0 ]
          case "pow-two-to-1023" "pow" [ "x", 2.0; "y", 1023.0 ]
          case "pow-generic" "pow" [ "x", 123.456; "y", 2.5 ]
          case "pow-negative-exponent" "pow" [ "x", 10000000000.0; "y", -3.3 ]
          case "pow-three-seven-and-half" "pow" [ "x", 3.0; "y", 7.5 ]
          case "pow-subnormal-base-root" "pow" [ "x", 1e-320; "y", 0.5 ]
          case "pow-overflow" "pow" [ "x", 1e+308; "y", 1.1 ]
          case "pow-underflow" "pow" [ "x", 1e-308; "y", 1.1 ]
          case "pow-two-to-1024" "pow" [ "x", 2.0; "y", 1024.0 ]
          case "pow-half-to-minus-1024" "pow" [ "x", 0.5; "y", -1024.0 ]
          case "pow-seven-tiny-exponent" "pow" [ "x", 7.0; "y", 1e-300 ]
          case "pow-small-base-sixty" "pow" [ "x", 1e-05; "y", 60.0 ]
          case "pow-tenth-minus-300" "pow" [ "x", 0.1; "y", -300.0 ]
          case "pow-negative-base-odd" "pow" [ "x", -2.0; "y", 3.0 ]
          case "pow-negative-base-even" "pow" [ "x", -2.0; "y", 2.0 ]
          case "pow-negative-base-non-integer" "pow" [ "x", -2.0; "y", 0.5 ]
          case "pow-negative-base-huge-even-exponent" "pow" [ "x", -1.5; "y", 2e+18 ]
          case "pow-zero-base-odd" "pow" [ "x", 0.0; "y", 3.0 ]
          case "pow-negative-zero-odd" "pow" [ "x", -0.0; "y", 3.0 ]
          case "pow-negative-zero-even" "pow" [ "x", -0.0; "y", 2.0 ]
          case "pow-zero-negative" "pow" [ "x", 0.0; "y", -1.0 ]
          case "pow-infinity-negative-exponent" "pow" [ "x", inf; "y", -2.0 ]
          case "pow-negative-infinity-odd" "pow" [ "x", -inf; "y", 3.0 ]
          case "pow-negative-infinity-non-integer" "pow" [ "x", -inf; "y", 2.5 ]
          case "pow-below-one-to-infinity" "pow" [ "x", 0.5; "y", inf ]
          case "pow-two-to-negative-infinity" "pow" [ "x", 2.0; "y", -inf ]
          case "pow-one-nan" "pow" [ "x", 1.0; "y", nan ]
          case "pow-nan-zero" "pow" [ "x", nan; "y", 0.0 ]
          case "pow-minus-one-infinity" "pow" [ "x", -1.0; "y", inf ]
          case "pow-anything-zero" "pow" [ "x", -7.5; "y", 0.0 ]
          case "pow-zero-zero" "pow" [ "x", 0.0; "y", 0.0 ] ]

    let private caseCell (c: Case) : CorpusCell =
        CorpusCell.Row(
            [ "id", CorpusFile.str c.Id
              "op", CorpusFile.str c.Op
              "input", CorpusCell.Row(c.Input |> List.map (fun (n, x) -> n, CorpusFile.str (hex x)))
              "expected", expected c ]
            @ referenceCells c
        )

    // ---- Phase 621 (DECISIONS.md D76): the generator's vectors ----

    /// One generator case: a seed and a stream index.
    type RngCase =
        { Id: string; Seed: int64; Stream: int }

    /// The generator's cases, in file order: the zero seed's first stream, a published seed's second
    /// stream, and the most negative seed at a stream index past the index's first 16-bit limb.
    let rngCases: RngCase list =
        [ { Id = "rng-seed-zero"
            Seed = 0L
            Stream = 0 }
          { Id = "rng-seed-1234567-stream-1"
            Seed = 1234567L
            Stream = 1 }
          { Id = "rng-seed-min-stream-65537"
            Seed = Int64.MinValue
            Stream = 65537 } ]

    /// A 64-bit draw as sixteen lowercase hex digits.
    let hex64 (v: uint64) : string =
        let digits = "0123456789abcdef"

        Array.init 16 (fun k -> string digits[int ((v >>> (4 * (15 - k))) &&& 0xFUL)])
        |> String.concat ""

    /// How many of a stream's first `count` draws are distinct: the shape of the probe that measured a
    /// collapsed product (Fuaran.Core's Phase 495: 1579 distinct draws on one host against 6 on another).
    let rngDistinct (seed: int64) (stream: int) (count: int) : int =
        let g = Rng.ofSeed seed stream
        List.init count (fun _ -> hex64 (Rng.next g)) |> List.distinct |> List.length

    /// What the stream answers: its first twenty draws, the next four as `uniform53`, the next four as
    /// `below 1000`, and how many of a fresh stream's first two thousand draws are distinct.
    let rngExpected (c: RngCase) : CorpusCell =
        let g = Rng.ofSeed c.Seed c.Stream
        let draws = List.init 20 (fun _ -> hex64 (Rng.next g))
        let uniforms = List.init 4 (fun _ -> hex (Rng.uniform53 g))
        let belows = List.init 4 (fun _ -> Rng.below g 1000)

        CorpusCell.Row
            [ "verdict", CorpusFile.str "ok"
              "draws", CorpusCell.List(draws |> List.map CorpusFile.str)
              "uniform53", CorpusCell.List(uniforms |> List.map CorpusFile.str)
              "below1000", CorpusCell.List(belows |> List.map CorpusFile.num)
              "distinct", CorpusFile.num (rngDistinct c.Seed c.Stream 2000) ]

    let private rngCell (c: RngCase) : CorpusCell =
        CorpusCell.Row
            [ "id", CorpusFile.str c.Id
              "op", CorpusFile.str "rng"
              "input", CorpusCell.Row [ "seed", CorpusFile.str (string c.Seed); "stream", CorpusFile.num c.Stream ]
              "expected", rngExpected c ]

    let private generator =
        "Phase 621 (DECISIONS.md D76): the vectors whose `op` is `rng` hold the pinned SplitMix64 generator "
        + "`Rng`. `seed` is a signed 64-bit integer in decimal and `stream` a stream index; each answers "
        + "`{ verdict: ok, draws, uniform53, below1000, distinct }` over ONE stream `Rng.ofSeed seed stream` "
        + "read in this order: `draws`, its first twenty 64-bit draws, each sixteen lowercase hex digits; "
        + "`uniform53`, the next four draws as floats in [0, 1), each its binary64 bit pattern; `below1000`, "
        + "the next four answers of `Rng.below 1000`; and `distinct`, how many of a FRESH stream's first two "
        + "thousand draws are distinct, which is two thousand on a host whose 64-bit product is exact."

    let private description =
        "The reference answers for Fuaran.Compute.Elementary over AUTHORED hard inputs (Phase 593; their own "
        + "file since Phase 630, every vector unchanged): the pinned elementary functions `exp`, `ln`, `log10` "
        + "and the float-exponent `pow` over `x` (and `y`) at the reduction boundaries, both edges of the "
        + "computed range, subnormal results and inputs, the near-one band of `ln`, the exact shortcuts of "
        + "`pow` and every row of D43's special-value table. A float is its IEEE-754 binary64 bit pattern as "
        + "sixteen lowercase hex digits, or `nan` for any NaN (whose pattern is the host's own canonical NaN "
        + "and is not compared). `op` names the function; each vector answers `{ verdict: ok, value, table }` "
        + "where `table` is what D43's special-value table said of the input first (`nan`, `value`, "
        + "`undefined`, `compute`) and `value` the function's bits (`nan` where the table said `nan` or "
        + "`undefined`), compared byte for byte; a vector the routine computed also carries `reference`, the "
        + "correctly rounded bits of the true value, and `ulps`, the package's distance from it in units in "
        + "the last place, which is at most one on every such vector (DECISIONS.md D52) and is itself part "
        + "of the document a host must reproduce."

    /// The family's document as the one corpus writer renders it: this module's head and vectors, then
    /// the head members and vectors the special functions add (Phase 631, `SpecialVectors.render`, the
    /// one caller, so that nothing renders the document without them).
    let renderWith (head: (string * CorpusCell) list) (rows: CorpusCell list) : string =
        CorpusFile.render
            { Head =
                [ "family", CorpusFile.str "elementary"
                  "description", CorpusFile.str description
                  "generator", CorpusFile.str generator ]
                @ head
              Items = "vectors"
              Rows = (cases |> List.map caseCell) @ (rngCases |> List.map rngCell) @ rows }
