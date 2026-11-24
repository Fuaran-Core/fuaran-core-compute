namespace Fuaran.Compute.GeometricAlgebraOps

open System
open Fuaran.Core
open Fuaran.Compute.GeometricAlgebra

/// `GeometricAlgebraOpsLaws` (Phase 731, DECISIONS.md D90): the laws the op algebra is held to, on
/// whichever host runs them. A state is compared BIT FOR BIT: the same names in the same order, the
/// same kind of object under each, and the sixty-four bits of each of its coefficients. No law compares
/// floats with `=`.
///
/// The exact laws: a recorded stream replays to the same bytes; a truncated stream replays to exactly
/// the state its prefix produced; `canApply` answers `Ok` exactly when `apply` does; a refused op
/// leaves the state the same value; each refusal is the one its op names; the codec's round trip is
/// the op bit for bit and malformed text is refused by name; an `Add` undone by its inverse is the
/// state it was. The bounded laws, each an identity of exact arithmetic that binary64 holds to
/// rounding: each transform undone by its inverse, and each compaction against the ops it replaced.
/// A bound is the largest error over ONE stated sample, drawn from `boundSampleSeed`, in units in the
/// last place of the reference coefficient and absolute where the reference is zero (DECISIONS.md D85
/// item 7), with no margin, because the answers are pinned bits and every host measures the same ones.
/// A bounded law draws nothing from the seed it is run at.
///
/// Every input is drawn by a pinned generator whose every step is an exact float operation (Park and
/// Miller's minimal standard), so a seed draws the same ops on every host and the laws run under Fable
/// over the ops they run over on .NET. A law answers `Ok ()` when it held at every iteration, else the
/// first failure with its iteration. Names are strings, compared ordinally.
[<RequireQualifiedAccess>]
module GeometricAlgebraOpsLaws =

    let private bits (x: float) : int64 = BitConverter.DoubleToInt64Bits x

    /// The names, compared ordinally and spelled as they are.
    let internal naming: Naming<string> =
        { Compare = fun a b -> String.CompareOrdinal(a, b)
          Encode = id
          Decode = fun s -> if s.Length = 0 then Error "an empty name" else Ok s }

    let private kindOf (o: GeometricObject) : int =
        match o with
        | GeometricObject.Point _ -> 0
        | GeometricObject.PointPair _ -> 1
        | GeometricObject.Line _ -> 2
        | GeometricObject.Circle _ -> 3
        | GeometricObject.Plane _ -> 4
        | GeometricObject.Sphere _ -> 5

    /// A state as its bits: each name, its kind and the bits of its coefficients, in the state's order.
    let internal stateBits (s: GeometricState<'N>) : ('N * int * int64[]) list =
        GeometricState.toList s
        |> List.map (fun (n, o) -> n, kindOf o, Ops.values o |> Array.map bits)

    let internal sameState (a: GeometricState<string>) (b: GeometricState<string>) : bool = stateBits a = stateBits b

    type private Draw(seed: int) =
        let mutable state =
            ((float seed % 2147483646.0) + 2147483646.0) % 2147483646.0 + 1.0

        member _.Next() : float =
            state <- (state * 48271.0) % 2147483647.0
            state

        /// An integer in `lo .. hi`.
        member this.Int (lo: int) (hi: int) : int =
            lo + int (this.Next() % float (hi - lo + 1))

        /// A multiple of a quarter in `[−8, 8]`, a zero of either sign among them.
        member this.Quarter() : float =
            let k = this.Int -32 32

            if k = 0 then
                (if this.Int 0 1 = 0 then 0.0 else -0.0)
            else
                float k / 4.0

        member this.Triple() : float * float * float =
            this.Quarter(), this.Quarter(), this.Quarter()

        /// A direction: a triple of small integers, one of them not zero.
        member this.Direction() : float * float * float =
            let x = float (this.Int -4 4)
            let y = float (this.Int -4 4)
            let z = float (this.Int 1 4)

            match this.Int 0 2 with
            | 0 -> x, y, z
            | 1 -> z, x, y
            | _ -> y, z, x

        /// An angle in `(−3, 3)` radians, of full precision.
        member this.Angle() : float =
            (this.Next() / 1073741824.0 - 1.0) * 3.0

        /// A factor in `(0.5, 4.5)`, of full precision.
        member this.Factor() : float = 0.5 + this.Next() / 536870912.0

        member this.Radius() : float = float (this.Int 1 16) / 4.0

    let private formOf (kind: int) (d: Draw) : Form =
        match kind with
        | 0 -> Form.Point(d.Triple())
        | 1 -> Form.PointPair(d.Triple(), d.Triple())
        | 2 -> Form.Line(d.Triple(), d.Direction())
        | 3 -> Form.Circle(d.Triple(), d.Radius(), d.Direction())
        | 4 -> Form.Plane(d.Direction(), d.Quarter())
        | _ -> Form.Sphere(d.Triple(), d.Radius())

    /// A transform of `name`: 0 rotate, 1 translate, 2 scale, 3 reflect, 4 screw.
    let private transformOf (kind: int) (name: string) (d: Draw) : GeometricOp<string> =
        match kind with
        | 0 -> GeometricOp.Rotate(name, d.Triple(), d.Direction(), d.Angle())
        | 1 -> GeometricOp.Translate(name, d.Triple())
        | 2 -> GeometricOp.Scale(name, d.Triple(), d.Factor())
        | 3 -> GeometricOp.Reflect(name, d.Direction(), d.Quarter())
        | _ -> GeometricOp.Screw(name, d.Triple(), d.Direction(), d.Angle(), d.Quarter())

    let private names = [| "a"; "b"; "c" |]

    /// An op of a drawn history: an `Add`, a `Remove` or a transform of one of three names, one in
    /// eight with a parameter the algebra refuses.
    let private opOf (d: Draw) : GeometricOp<string> =
        let name = names[d.Int 0 2]

        let op =
            match d.Int 0 9 with
            | 0
            | 1 -> GeometricOp.Add(name, formOf (d.Int 0 5) d)
            | 2 -> GeometricOp.Remove name
            | _ -> transformOf (d.Int 0 4) name d

        if d.Int 0 7 <> 0 then
            op
        else
            match op with
            | GeometricOp.Add(n, _) -> GeometricOp.Add(n, Form.Sphere((0.0, 0.0, 0.0), -1.0))
            | GeometricOp.Rotate(n, p, _, a) -> GeometricOp.Rotate(n, p, (0.0, 0.0, 0.0), a)
            | GeometricOp.Scale(n, c, _) -> GeometricOp.Scale(n, c, 0.0)
            | GeometricOp.Screw(n, p, u, _, x) -> GeometricOp.Screw(n, p, u, Double.NaN, x)
            | GeometricOp.Translate(n, (x, y, _)) -> GeometricOp.Translate(n, (x, y, Double.PositiveInfinity))
            | other -> other

    let private history (d: Draw) : GeometricOp<string> list =
        List.init (d.Int 1 12) (fun _ -> opOf d)

    let private check (held: bool) (why: unit -> string) : Result<unit, string> = if held then Ok() else Error(why ())

    let private law (seed: int) (count: int) (body: Draw -> Result<unit, string>) : Result<unit, string> =
        let draw = Draw seed
        let mutable verdict = Ok()
        let mutable i = 0

        while Result.isOk verdict && i < count do
            match body draw with
            | Ok() -> ()
            | Error why -> verdict <- Error(sprintf "iteration %d: %s" i why)

            i <- i + 1

        verdict

    let private stream =
        OpStream.canonical OpStream.defaultHash (GeometricOps.streamWitness naming)

    let private actor = Actor.ofLegacyString "laws"
    let private empty = GeometricState.empty naming

    /// Append each op of a history that applies; the records and the state after each record.
    let private record (ops: GeometricOp<string> list) =
        ops
        |> List.fold
            (fun (state, records, after) op ->
                match OpStream.append stream actor op state records with
                | Ok(state', records') -> state', records', after @ [ state' ]
                | Error _ -> state, records, after)
            (empty, OpStream.empty, [])

    // ---- the exact laws -------------------------------------------------------------------------

    let private replaysToTheSameBytes (d: Draw) =
        let ops = history d
        let state, records, _ = record ops
        let applied, _ = GeometricOps.applyAll ops empty

        // Each record's op written to its text and read back, as a stream persisted and reopened is.
        let readBack (r: OpRecord<GeometricOp<string>>) =
            GeometricOps.decode naming (GeometricOps.encode naming r.Op)
            |> Result.map (fun op ->
                { Seq = r.Seq
                  Actor = r.Actor
                  Op = op
                  PrevHash = r.PrevHash
                  Hash = r.Hash })

        let read = records |> List.map readBack

        match
            read
            |> List.tryPick (fun r ->
                match r with
                | Error e -> Some e
                | Ok _ -> None)
        with
        | Some e -> Error("a recorded op did not read back: " + e)
        | None ->
            let read = read |> List.choose Result.toOption

            match OpStream.replay (GeometricOps.streamWitness naming) empty read with
            | Error(i, _) -> Error(sprintf "the read-back stream refused its record %d" i)
            | Ok replayed ->
                check
                    (OpStream.verifyChain stream read
                     && sameState replayed state
                     && sameState applied state)
                    (fun () -> "the stream read back is not the state it recorded, or not the state applyAll reaches")

    let private truncatedReplaysToItsPrefix (d: Draw) =
        let ops = history d
        let _, records, after = record ops

        [ 1 .. List.length records ]
        |> List.tryPick (fun k ->
            match OpStream.replay (GeometricOps.streamWitness naming) empty (List.truncate k records) with
            | Ok s when sameState s (List.item (k - 1) after) -> None
            | _ -> Some k)
        |> function
            | None -> Ok()
            | Some k -> Error(sprintf "the first %d records do not replay to the state they produced" k)

    let private canApplyAgreesWithApply (d: Draw) =
        let ops = history d

        ops
        |> List.fold
            (fun (state, verdict) op ->
                match verdict with
                | Error _ -> state, verdict
                | Ok() ->
                    let applied = GeometricOps.apply op state
                    let checkedFirst = GeometricOps.canApply op state

                    let agree =
                        match applied, checkedFirst with
                        | Ok _, Ok() -> true
                        | Error a, Error b -> a = b
                        | _ -> false

                    (match applied with
                     | Ok s -> s
                     | Error _ -> state),
                    check agree (fun () -> sprintf "canApply and apply disagree on %A" op))
            (empty, Ok())
        |> snd

    let private refusedLeavesTheSameValue (d: Draw) =
        let ops = history d
        let state, _ = GeometricOps.applyAll ops empty
        let op = opOf d

        match GeometricOps.apply op state with
        | Ok _ -> Ok()
        | Error _ ->
            let after, refusals = GeometricOps.applyAll [ op ] state

            check (obj.ReferenceEquals(after, state) && List.length refusals = 1) (fun () ->
                "a refused op answered another state")

    /// Each refusal the op names, from a state holding `a`.
    let internal refusalCases: (GeometricOp<string> * GeometricOpRefusal<string>) list =
        let origin = 0.0, 0.0, 0.0
        let up = 0.0, 0.0, 1.0

        [ GeometricOp.Rotate("z", origin, up, 1.0), GeometricOpRefusal.UnknownName "z"
          GeometricOp.Remove "z", GeometricOpRefusal.UnknownName "z"
          GeometricOp.Add("a", Form.Point origin), GeometricOpRefusal.NameAlreadyPresent "a"
          GeometricOp.Add("b", Form.Point(Double.NaN, 0.0, 0.0)),
          GeometricOpRefusal.Geometry GeometryRefusal.NonFiniteCoordinate
          GeometricOp.Add("b", Form.Line(origin, origin)), GeometricOpRefusal.Geometry GeometryRefusal.ZeroDirection
          GeometricOp.Add("b", Form.Sphere(origin, -1.0)), GeometricOpRefusal.Geometry GeometryRefusal.NegativeRadius
          GeometricOp.Add("b", Form.Circle(origin, -0.5, up)),
          GeometricOpRefusal.Geometry GeometryRefusal.NegativeRadius
          GeometricOp.Rotate("a", origin, origin, 1.0), GeometricOpRefusal.Geometry GeometryRefusal.ZeroDirection
          GeometricOp.Rotate("a", origin, up, Double.PositiveInfinity),
          GeometricOpRefusal.Geometry GeometryRefusal.NonFiniteAngle
          GeometricOp.Screw("a", origin, up, 1.0, Double.NaN),
          GeometricOpRefusal.Geometry GeometryRefusal.NonFiniteDistance
          GeometricOp.Translate("a", (0.0, Double.NegativeInfinity, 0.0)),
          GeometricOpRefusal.Geometry GeometryRefusal.NonFiniteTranslation
          GeometricOp.Scale("a", origin, 0.0), GeometricOpRefusal.Geometry GeometryRefusal.NonPositiveFactor
          GeometricOp.Scale("a", origin, -2.0), GeometricOpRefusal.Geometry GeometryRefusal.NonPositiveFactor
          GeometricOp.Reflect("a", origin, 0.0), GeometricOpRefusal.Geometry GeometryRefusal.ZeroDirection
          // The name is read before the parameters, and the parameters in the order the op lists them.
          GeometricOp.Rotate("z", origin, origin, Double.NaN), GeometricOpRefusal.UnknownName "z"
          GeometricOp.Screw("a", (Double.NaN, 0.0, 0.0), origin, Double.NaN, Double.NaN),
          GeometricOpRefusal.Geometry GeometryRefusal.NonFiniteCoordinate
          GeometricOp.Screw("a", origin, origin, Double.NaN, Double.NaN),
          GeometricOpRefusal.Geometry GeometryRefusal.ZeroDirection
          GeometricOp.Scale("a", (0.0, Double.NaN, 0.0), -1.0),
          GeometricOpRefusal.Geometry GeometryRefusal.NonFiniteCoordinate ]

    let private eachRefusalByName (_: Draw) =
        match GeometricOps.apply (GeometricOp.Add("a", Form.Point(1.0, 2.0, 3.0))) empty with
        | Error _ -> Error "the state holding a was refused"
        | Ok state ->
            refusalCases
            |> List.tryFind (fun (op, expected) ->
                match GeometricOps.apply op state, GeometricOps.canApply op state with
                | Error got, Error got2 -> got <> expected || got2 <> expected
                | _ -> true)
            |> function
                | None -> Ok()
                | Some(op, expected) -> Error(sprintf "%A is not refused as %A" op expected)

    let private opBits (op: GeometricOp<string>) : string * int64 list =
        let t (x: float, y: float, z: float) = [ bits x; bits y; bits z ]

        let formBits (f: Form) =
            match f with
            | Form.Point p -> 0L :: t p
            | Form.PointPair(p, q) -> 1L :: t p @ t q
            | Form.Line(p, u) -> 2L :: t p @ t u
            | Form.Circle(c, r, n) -> 3L :: t c @ [ bits r ] @ t n
            | Form.Plane(n, x) -> 4L :: t n @ [ bits x ]
            | Form.Sphere(c, r) -> 5L :: t c @ [ bits r ]

        Ops.nameOf op,
        match op with
        | GeometricOp.Add(_, f) -> 10L :: formBits f
        | GeometricOp.Remove _ -> [ 11L ]
        | GeometricOp.Rotate(_, p, u, a) -> 12L :: t p @ t u @ [ bits a ]
        | GeometricOp.Translate(_, v) -> 13L :: t v
        | GeometricOp.Scale(_, c, f) -> 14L :: t c @ [ bits f ]
        | GeometricOp.Reflect(_, n, x) -> 15L :: t n @ [ bits x ]
        | GeometricOp.Screw(_, p, u, a, x) -> 16L :: t p @ t u @ [ bits a; bits x ]

    /// Texts no op is: each refused by the decoder, by name.
    let internal malformed: (string * string) list =
        [ "{", "not json"
          "[]", "expected an object"
          "{\"name\":\"a\"}", "$type: missing"
          "{\"$type\":\"spin\",\"name\":\"a\"}", "unknown op: spin"
          "{\"$type\":\"remove\"}", "members:"
          "{\"$type\":\"remove\",\"name\":\"a\",\"extra\":\"\"}", "members:"
          "{\"$type\":\"remove\",\"name\":\"\"}", "name: "
          "{\"$type\":\"translate\",\"name\":\"a\",\"vector\":\"00\"}", "vector: "
          "{\"$type\":\"translate\",\"name\":\"a\",\"vector\":\"3FF000000000000000000000000000000000000000000000\"}",
          "vector: "
          "{\"$type\":\"rotate\",\"angle\":1.5,\"direction\":\"000000000000000000000000000000000000000000000000\",\"name\":\"a\",\"point\":\"000000000000000000000000000000000000000000000000\"}",
          "angle: "
          "{\"$type\":\"add\",\"form\":{\"$type\":\"cone\"},\"name\":\"a\"}", "form: unknown form: cone" ]

    let private roundTrip (d: Draw) =
        let ops =
            history d
            |> List.filter (fun op -> not ((opBits op |> snd) |> List.exists (fun b -> b = bits Double.NaN)))

        let failed =
            ops
            |> List.tryFind (fun op ->
                match GeometricOps.decode naming (GeometricOps.encode naming op) with
                | Ok back -> opBits back <> opBits op
                | Error _ -> true)

        match failed with
        | Some op -> Error(sprintf "%A does not read back bit for bit" op)
        | None ->
            malformed
            |> List.tryFind (fun (text, name) ->
                match GeometricOps.decode naming text with
                | Ok _ -> true
                | Error e -> not (e.StartsWith name))
            |> function
                | None -> Ok()
                | Some(text, name) -> Error(sprintf "%s is not refused as %s" text name)

    let private addUndoneByItsInverse (d: Draw) =
        let state, _ = GeometricOps.applyAll (history d) empty
        let op = GeometricOp.Add(names[d.Int 0 2], formOf (d.Int 0 5) d)

        match GeometricOps.apply op state, GeometricOps.invert op state with
        | Ok after, Ok inverse ->
            match GeometricOps.apply inverse after with
            | Ok back -> check (sameState back state) (fun () -> "an Add undone is not the state it was")
            | Error _ -> Error "the inverse of an Add was refused"
        | Error _, Error _ -> Ok()
        | _ -> Error "invert and apply disagree on an Add"

    // ---- the bounded laws -----------------------------------------------------------------------

    /// The last place of a non-zero double, found by exact doubling and halving.
    let private lastPlace (x: float) : float =
        let magnitude = if x < 0.0 then -x else x
        let mutable power = 1.0

        while power > magnitude do
            power <- power / 2.0

        while power * 2.0 <= magnitude do
            power <- power * 2.0

        power / 4503599627370496.0

    /// The largest errors over the pairs given so far (D85 item 7, D90): in units in the last place of
    /// the reference coefficient where it is not zero, absolute where it is, and, beside them, in units
    /// in the last place of the largest magnitude among the reference object's coefficients. The first
    /// is D85 item 7's form and is dominated wherever a reference coefficient is a rounding residue of a
    /// zero; the third says how far the object moved at its own scale. A NaN is an error past any bound.
    type private Worst() =
        let mutable ulps = 0.0
        let mutable absolute = 0.0
        let mutable scaled = 0.0

        member _.Add(got: float, reference: float, scale: float) : unit =
            let error = if got < reference then reference - got else got - reference

            if Double.IsNaN error then
                ulps <- Double.PositiveInfinity
                scaled <- Double.PositiveInfinity
            else
                if reference = 0.0 then
                    (if error > absolute then
                         absolute <- error)
                else
                    let u = error / lastPlace reference

                    if u > ulps then
                        ulps <- u

                let s = if scale = 0.0 then error else error / lastPlace scale

                if s > scaled then
                    scaled <- s

        member this.States(got: GeometricState<string>, reference: GeometricState<string>) : Result<unit, string> =
            let g = GeometricState.toList got
            let r = GeometricState.toList reference

            if List.map fst g <> List.map fst r then
                Error "the two states hold other names"
            else
                List.iter2
                    (fun (_, a) (_, b) ->
                        let reached = Ops.values b
                        let scale = reached |> Array.map (fun x -> if x < 0.0 then -x else x) |> Array.max
                        Array.iter2 (fun x y -> this.Add(x, y, scale)) (Ops.values a) reached)
                    g
                    r

                Ok()

        member _.Answer: float * float * float = ulps, absolute, scaled

    /// The seed every bounded law's sample is drawn from.
    let internal boundSampleSeed = 731

    /// How many objects a bounded law's sample transforms: two hundred of each kind.
    let internal boundSampleSize = 1200

    let private measure
        (seed: int)
        (each: Draw -> GeometricState<string> -> Result<GeometricState<string> * GeometricState<string>, string>)
        =
        let d = Draw seed
        let worst = Worst()
        let mutable failure = None
        let mutable i = 0

        while failure.IsNone && i < boundSampleSize do
            let added = GeometricOps.apply (GeometricOp.Add("a", formOf (i % 6) d)) empty

            match
                added
                |> Result.mapError (fun _ -> "a drawn object was refused")
                |> Result.bind (each d)
            with
            | Ok(got, reference) ->
                match worst.States(got, reference) with
                | Ok() -> ()
                | Error e -> failure <- Some e
            | Error e -> failure <- Some(sprintf "sample %d: %s" i e)

            i <- i + 1

        match failure with
        | Some e -> Error e
        | None -> Ok worst.Answer

    let private undoneBy (op: GeometricOp<string>) (s0: GeometricState<string>) =
        match GeometricOps.apply op s0, GeometricOps.invert op s0 with
        | Ok s1, Ok inverse ->
            match GeometricOps.apply inverse s1 with
            | Ok s2 -> Ok(s2, s0)
            | Error _ -> Error "an inverse was refused"
        | _ -> Error "a drawn transform was refused"

    /// The inverse of each transform: the state after the op and its inverse, against the state before.
    let internal inverseSampleAt (kind: int) (seed: int) : Result<float * float * float, string> =
        measure seed (fun d s0 -> undoneBy (transformOf kind "a" d) s0)

    let private applied (ops: GeometricOp<string> list) (s0: GeometricState<string>) =
        match GeometricOps.applyAll ops s0 with
        | s, [] -> Ok s
        | _ -> Error "a drawn run was refused"

    /// A compaction: the state its one op reaches, against the state the run reaches.
    let private compacted (run: GeometricOp<string> list) (s0: GeometricState<string>) =
        match GeometricOps.compact run s0 with
        | [ one ] ->
            applied [ one ] s0
            |> Result.bind (fun got -> applied run s0 |> Result.map (fun reference -> got, reference))
        | ops -> Error(sprintf "a run of %d compacted to %d ops" (List.length run) (List.length ops))

    /// A run of two to four translations.
    let internal translationRunSampleAt (seed: int) : Result<float * float * float, string> =
        measure seed (fun d s0 ->
            compacted (List.init (d.Int 2 4) (fun _ -> GeometricOp.Translate("a", d.Triple()))) s0)

    /// A run of two to four rotations and screws about one axis.
    let internal axisRunSampleAt (seed: int) : Result<float * float * float, string> =
        measure seed (fun d s0 ->
            let p = d.Triple()
            let u = d.Direction()

            let run =
                List.init (d.Int 2 4) (fun _ ->
                    if d.Int 0 1 = 0 then
                        GeometricOp.Rotate("a", p, u, d.Angle())
                    else
                        GeometricOp.Screw("a", p, u, d.Angle(), d.Quarter()))

            compacted run s0)

    /// The bounds of the bounded laws: for each, the largest errors measured over its stated sample —
    /// in units in the last place of the reference coefficient, absolute where the reference is zero,
    /// and in units in the last place of the reference object's largest coefficient — with no margin,
    /// because the answers are pinned bits and every host measures the same ones (DECISIONS.md D90).
    /// Each figure is that sample's and no other's.
    let internal bounds: (string * (int -> Result<float * float * float, string>) * (float * float * float)) list =
        [ "a rotation undone by its inverse is the state it was, within the stated bound",
          inverseSampleAt 0,
          (31525197391593470.0, 7.216449660063518e-15, 65.5)
          "a translation undone by its inverse is the state it was, within the stated bound",
          inverseSampleAt 1,
          (13510798882111488.0, 4.440892098500626e-16, 58.0625)
          "a scale undone by its inverse is the state it was, within the stated bound",
          inverseSampleAt 2,
          (640.0, 2.842170943040401e-14, 50.0)
          "a reflection undone by itself is the state it was, within the stated bound",
          inverseSampleAt 3,
          (4503599627370496.0, 7.105427357601002e-15, 105.5)
          "a screw undone by its inverse is the state it was, within the stated bound",
          inverseSampleAt 4,
          (1280.0, 1.2434497875801753e-14, 135.5)
          "a run of translations compacts to one op that reaches its state, within the stated bound",
          translationRunSampleAt,
          (6755399441055744.0, 0.0, 32.0)
          "a run of rotations and screws about one axis compacts to one op that reaches its state, within the stated bound",
          axisRunSampleAt,
          (7808.0, 0.0, 71.0) ]

    let private verdicts: Lazy<(string * Result<unit, string>) list> =
        lazy
            (bounds
             |> List.map (fun (name, sample, (boundUlps, boundAbsolute, boundScaled)) ->
                 name,
                 match sample boundSampleSeed with
                 | Error why -> Error why
                 | Ok(ulps, absolute, scaled) ->
                     check (ulps <= boundUlps && absolute <= boundAbsolute && scaled <= boundScaled) (fun () ->
                         sprintf
                             "the largest errors are %g units in the last place, %g absolute and %g at the object's scale, past the stated bound"
                             ulps
                             absolute
                             scaled)))

    let private bounded (name: string) (_: Draw) : Result<unit, string> =
        verdicts.Value |> List.find (fun (n, _) -> n = name) |> snd

    /// The laws, by name, each run at `count` iterations drawn from `seed`.
    let laws (seed: int) (count: int) : (string * Result<unit, string>) list =
        [ "a recorded stream replays to the same bytes", replaysToTheSameBytes
          "a truncated stream replays to exactly the state its prefix produced", truncatedReplaysToItsPrefix
          "canApply answers Ok exactly when apply does, with the same refusal", canApplyAgreesWithApply
          "a refused op leaves the state the same value", refusedLeavesTheSameValue
          "each refusal is the one its op names, the name first and then the parameters in order", eachRefusalByName
          "the codec's round trip is the op bit for bit, and malformed text is refused by name", roundTrip
          "an Add undone by its inverse is the state it was, bit for bit", addUndoneByItsInverse ]
        @ [ for name, _, _ in bounds -> name, bounded name ]
        |> List.map (fun (name, body) -> name, law seed count body)
