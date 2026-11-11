namespace Fuaran.Compute.Regression

open Fuaran.Core
open Fuaran.Compute.LinearAlgebra

/// The wire form of a `QRAccumulator` through `Fuaran.Core.Wire` (Phase 591), so a primed fit survives a
/// restart and continues to the same bits. Its floats are spelled as `LinAlgWire` spells a matrix's:
/// each the IEEE-754 binary64 bit pattern, sixteen lowercase hex digits, the floats of one member
/// concatenated in one string, a NaN written as `7ff8000000000000` on every host. The document is
/// canonical JSON (`Rendering.CrossHost`), its member set closed:
///
///   `{"$type":"qrAccumulator","cols":p,"count":n,"qty":…,"r":…,"rss":…}` — `r` the upper triangle
///   of `R` row by row (`R[0,0..p−1]`, then `R[1,1..p−1]`, …), `qty` the `p` entries of `Qᵀy`.
///
/// A decoded accumulator is refused unless it is one `QRAccumulator` could have built: every
/// diagonal entry of `R` and the residual sum of squares without the sign bit (a NaN aside). The codec
/// left `LinAlgWire` with the accumulator in Phase 632 (DECISIONS.md D75); its bytes did not move.
[<RequireQualifiedAccess>]
module RegressionWire =

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
              "qty", JStr(LinAlgWire.bitsText p (fun j -> acc.State[j * w + p]))
              "r", JStr(LinAlgWire.bitsText triangle.Length (fun k -> triangle[k]))
              "rss", JStr(LinAlgWire.bitsText 1 (fun _ -> acc.RawRss)) ]

    let private signBit (x: float) : bool =
        not (System.Double.IsNaN x) && (x < 0.0 || (x = 0.0 && 1.0 / x < 0.0))

    /// The accumulator a wire value holds, or the typed refusal: the matrix's refusals, plus a diagonal
    /// entry of `R` or the residual sum of squares carrying the sign bit, which no accumulator builds.
    let accumulatorOfJson (value: JVal) : Result<QRAccumulator, DecodeError> =
        Decoder.closed
            [ "$type"; "cols"; "count"; "qty"; "r"; "rss" ]
            (LinAlgWire.typeIs "qrAccumulator"
             |> Decoder.bind (fun () ->
                 Decoder.pair (LinAlgWire.nonNegativeInt "cols") (LinAlgWire.nonNegativeInt "count") (fun p n -> p, n)
                 |> Decoder.andThen (fun (p, n) -> LinAlgWire.bounded p (int64 p + 1L) |> Result.map (fun () -> p, n))
                 |> Decoder.bind (fun (p, n) ->
                     let w = p + 1

                     Decoder.pair (LinAlgWire.bitsField "r" (p * w / 2)) (LinAlgWire.bitsField "qty" p) (fun t q ->
                         t, q)
                     |> Decoder.bind (fun (triangle, q) ->
                         Decoder.field
                             "rss"
                             (Decoder.str
                              |> Decoder.andThen (LinAlgWire.floatsOf 1)
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
