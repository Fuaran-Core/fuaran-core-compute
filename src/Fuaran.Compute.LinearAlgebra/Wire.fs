namespace Fuaran.Compute.LinearAlgebra

open Fuaran.Core

/// The wire form of a `Matrix` and a `QRAccumulator` through `Fuaran.Core.Wire` (Phase 591), so a
/// primed fit survives a restart and continues to the same bits. Every float travels as its IEEE-754
/// binary64 bit pattern, sixteen lowercase hex digits, the floats of one member concatenated in one
/// string — never as decimal text, so nothing is rounded on the way through. A NaN is written as
/// `7ff8000000000000` on every host and read back as the host's own NaN, so the document a state
/// encodes to is the same bytes on every host. Each document is canonical JSON
/// (`Rendering.CrossHost`), its member set closed:
///
///   `{"$type":"matrix","bits":…,"cols":c,"rows":r}` — the elements in column-major order;
///   `{"$type":"qrAccumulator","cols":p,"count":n,"qty":…,"r":…,"rss":…}` — `r` the upper triangle
///   of `R` row by row (`R[0,0..p−1]`, then `R[1,1..p−1]`, …), `qty` the `p` entries of `Qᵀy`.
///
/// A decoded accumulator is refused unless it is one `QRAccumulator` could have built: every
/// diagonal entry of `R` and the residual sum of squares without the sign bit (a NaN aside).
[<RequireQualifiedAccess>]
module LinAlgWire =

    let private digits = "0123456789abcdef"

    /// The bit pattern written for any NaN.
    let private nanBits: int64 = 0x7ff8000000000000L

    let private writeBits (sb: System.Text.StringBuilder) (x: float) : unit =
        let b =
            if System.Double.IsNaN x then
                nanBits
            else
                System.BitConverter.DoubleToInt64Bits x

        for k in 0..15 do
            sb.Append(digits[int ((b >>> (4 * (15 - k))) &&& 0xFL)]) |> ignore

    let private bitsText (count: int) (at: int -> float) : string =
        let sb = System.Text.StringBuilder(16 * count)

        for k in 0 .. count - 1 do
            writeBits sb (at k)

        sb.ToString()

    let private hexDigit (c: char) : int =
        if c >= '0' && c <= '9' then int c - int '0'
        elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
        else -1

    /// The `count` floats of a bits text, or why it is not one.
    let private floatsOf (count: int) (text: string) : Result<float[], RootDecodeCode * string * string> =
        if int64 text.Length <> 16L * int64 count then
            Error(
                RootDecodeCode.OutOfRange,
                sprintf "%d floats as %d hex digits" count (16 * count),
                sprintf "%d characters" text.Length
            )
        else
            let out = Array.zeroCreate<float> count
            let mutable bad = -1
            let mutable k = 0

            while bad < 0 && k < count do
                let mutable b = 0L
                let mutable d = 0

                while bad < 0 && d < 16 do
                    let h = hexDigit text[16 * k + d]

                    if h < 0 then
                        bad <- 16 * k + d
                    else
                        b <- (b <<< 4) ||| int64 h

                    d <- d + 1

                let x = System.BitConverter.Int64BitsToDouble b
                out[k] <- if System.Double.IsNaN x then System.Double.NaN else x
                k <- k + 1

            if bad >= 0 then
                Error(
                    RootDecodeCode.OutOfRange,
                    "lowercase hex digits",
                    sprintf "the character at %d is '%c'" bad text[bad]
                )
            else
                Ok out

    let private bitsField (name: string) (count: int) : Decoder<float[]> =
        Decoder.field name (Decoder.str |> Decoder.andThen (floatsOf count))

    let private nonNegativeInt (name: string) : Decoder<int> =
        Decoder.field
            name
            (Decoder.int
             |> Decoder.andThen (fun v ->
                 if v < 0 then
                     Error(RootDecodeCode.OutOfRange, "a count of zero or more", string v)
                 else
                     Ok v))

    let private typeIs (tag: string) : Decoder<unit> =
        Decoder.field
            "$type"
            (Decoder.str
             |> Decoder.andThen (fun t ->
                 if t = tag then
                     Ok()
                 else
                     Error(RootDecodeCode.UnknownTag, tag, t)))

    /// Below `2³¹` floats in one member, so no length computed from a decoded count overflows.
    let private bounded (rows: int) (cols: int64) : Result<unit, RootDecodeCode * string * string> =
        if int64 rows * cols > int64 System.Int32.MaxValue / 16L then
            Error(RootDecodeCode.LimitExceeded, "a matrix below 2^27 elements", sprintf "%d by %d" rows cols)
        else
            Ok()

    // ---- the matrix ----------------------------------------------------------------------------

    /// The matrix as a wire value.
    let matrixToJson (m: Matrix) : JVal =
        let data = Matrix.toColumnMajor m

        Canonical.typed
            "matrix"
            [ "bits", JStr(bitsText data.Length (fun k -> data[k]))
              "cols", JInt m.Cols
              "rows", JInt m.Rows ]

    /// The matrix a wire value holds, or the typed refusal: a member outside the closed set, a wrong
    /// `$type`, a negative or oversized shape, or a `bits` member that is not `rows · cols` floats.
    let matrixOfJson (value: JVal) : Result<Matrix, DecodeError> =
        Decoder.closed
            [ "$type"; "bits"; "cols"; "rows" ]
            (typeIs "matrix"
             |> Decoder.bind (fun () ->
                 Decoder.pair (nonNegativeInt "rows") (nonNegativeInt "cols") (fun r c -> r, c)
                 |> Decoder.andThen (fun (r, c) -> bounded r (int64 c) |> Result.map (fun () -> r, c))
                 |> Decoder.bind (fun (r, c) ->
                     bitsField "bits" (r * c)
                     |> Decoder.map (fun data -> Matrix.init r c (fun i j -> data[j * r + i])))))
            value

    /// The canonical text of a matrix: the same bytes on every host.
    let encodeMatrix (m: Matrix) : string =
        Canonical.write Rendering.CrossHost (matrixToJson m)

    /// The matrix canonical text holds.
    let decodeMatrix (text: string) : Result<Matrix, DecodeError> =
        Canonical.read text |> Result.bind matrixOfJson

    // ---- the accumulator -----------------------------------------------------------------------

    /// The accumulator as a wire value.
    let accumulatorToJson (acc: QRAccumulator) : JVal =
        let p = acc.Cols
        let w = p + 1

        let triangle =
            [| for i in 0 .. p - 1 do
                   for j in i .. p - 1 -> acc.State[i * w + j] |]

        Canonical.typed
            "qrAccumulator"
            [ "cols", JInt p
              "count", JInt acc.Count
              "qty", JStr(bitsText p (fun j -> acc.State[j * w + p]))
              "r", JStr(bitsText triangle.Length (fun k -> triangle[k]))
              "rss", JStr(bitsText 1 (fun _ -> acc.RawRss)) ]

    let private signBit (x: float) : bool =
        not (System.Double.IsNaN x) && (x < 0.0 || (x = 0.0 && 1.0 / x < 0.0))

    /// The accumulator a wire value holds, or the typed refusal: the matrix's refusals, plus a diagonal
    /// entry of `R` or the residual sum of squares carrying the sign bit, which no accumulator builds.
    let accumulatorOfJson (value: JVal) : Result<QRAccumulator, DecodeError> =
        Decoder.closed
            [ "$type"; "cols"; "count"; "qty"; "r"; "rss" ]
            (typeIs "qrAccumulator"
             |> Decoder.bind (fun () ->
                 Decoder.pair (nonNegativeInt "cols") (nonNegativeInt "count") (fun p n -> p, n)
                 |> Decoder.andThen (fun (p, n) -> bounded p (int64 p + 1L) |> Result.map (fun () -> p, n))
                 |> Decoder.bind (fun (p, n) ->
                     let w = p + 1

                     Decoder.pair (bitsField "r" (p * w / 2)) (bitsField "qty" p) (fun t q -> t, q)
                     |> Decoder.bind (fun (triangle, q) ->
                         Decoder.field
                             "rss"
                             (Decoder.str
                              |> Decoder.andThen (floatsOf 1)
                              |> Decoder.andThen (fun rss ->
                                  if signBit rss[0] then
                                      Error(
                                          RootDecodeCode.OutOfRange,
                                          "a residual sum of squares without the sign bit",
                                          "a negative value"
                                      )
                                  else
                                      Ok rss[0]))
                         |> Decoder.andThen (fun rss ->
                             let state = Array.zeroCreate<float> (p * w)
                             let mutable k = 0
                             let mutable badDiagonal = -1

                             for i in 0 .. p - 1 do
                                 for j in i .. p - 1 do
                                     state[i * w + j] <- triangle[k]

                                     if i = j && badDiagonal < 0 && signBit triangle[k] then
                                         badDiagonal <- i

                                     k <- k + 1

                                 state[i * w + p] <- q[i]

                             if badDiagonal >= 0 then
                                 Error(
                                     RootDecodeCode.OutOfRange,
                                     "a diagonal of R without the sign bit",
                                     sprintf "R[%d,%d] is negative" badDiagonal badDiagonal
                                 )
                             else
                                 Ok(QRAccumulator(p, n, state, rss)))))))
            value

    /// The canonical text of an accumulator: the same bytes on every host.
    let encodeAccumulator (acc: QRAccumulator) : string =
        Canonical.write Rendering.CrossHost (accumulatorToJson acc)

    /// The accumulator canonical text holds.
    let decodeAccumulator (text: string) : Result<QRAccumulator, DecodeError> =
        Canonical.read text |> Result.bind accumulatorOfJson
