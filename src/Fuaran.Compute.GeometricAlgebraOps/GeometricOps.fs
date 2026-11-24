namespace Fuaran.Compute.GeometricAlgebraOps

open System
open Fuaran.Core
open Fuaran.Compute.GeometricAlgebra

// ============================================================================
//  Phase 731 — geometric transforms ride the op stream (DECISIONS.md D85
//  item 9, D90). A state of named objects, seven parametric ops over it, a
//  total `apply` and `canApply`, an inverse, a canonical codec and a
//  `Fuaran.Core.OpStream` `StreamWitness`, in the shape `Fuaran.Compute.ColumnOps`
//  gives a table; and the compaction of a run, which nothing here calls.
//
//  Replay derives every object and every versor through the algebra's
//  published constructors and applies it by the algebra's published
//  `applyTo…`, and computes nothing of its own: no product, no trigonometric
//  value, no norm, and no float literal in a comparison. A refusal a
//  constructor gives is carried as it was given (D85 item 9). The inverse
//  negates one parameter or forms one reciprocal; the compaction adds
//  parameters. No coefficient is a member of an op or of the wire (D85 item 10).
//
//  Every function is written once over `Rules`, an internal record with one
//  member for each rule, and a public function passes `Rules.shipped`. The
//  suite calls the same function with ONE member changed and shows a
//  committed law vector fail, so a go-red witness is this code and never a
//  copy of it, as Phase 728's `Arithmetic` is for the objects.
//
//  FSharp.Core, the algebra, Fuaran.Core.Wire and Fuaran.Core.OpStream; Fable-clean.
// ============================================================================

/// How a caller's names are compared and spelled. The package invents neither (DECISIONS.md D85
/// item 9): the state is ordered by `Compare`, and the codec writes a name as the string `Encode`
/// answers and reads it back with `Decode`.
type Naming<'N> =
    {
        /// A total order on names: negative, zero or positive. Two names are one name when it answers
        /// zero. The state lists its objects in ascending order under it, on every host, so a
        /// comparison that answers the same on every host (an ordinal comparison of strings, say)
        /// gives a state that iterates the same on every host.
        Compare: 'N -> 'N -> int
        /// The name as the text the wire carries.
        Encode: 'N -> string
        /// The name the text spells, or why it spells none.
        Decode: string -> Result<'N, string>
    }

/// An object in DECISIONS.md D85 item 4's parametric form. A triple is a Euclidean point, a vector
/// or a direction, `(x, y, z)`. A direction or a normal is used as it is given: the algebra divides it
/// by its norm.
[<RequireQualifiedAccess>]
type Form =
    /// A point: `Point.up`.
    | Point of point: (float * float * float)
    /// The point pair through two points: `PointPair.through (Point.up first) (Point.up second)`.
    | PointPair of first: (float * float * float) * second: (float * float * float)
    /// A line through a point with a direction: `Line.ofPointDirection`.
    | Line of point: (float * float * float) * direction: (float * float * float)
    /// A circle from its centre, its radius and its normal: `Circle.ofCentreRadiusNormal`.
    | Circle of centre: (float * float * float) * radius: float * normal: (float * float * float)
    /// A plane from its normal and its distance from the origin along it: `Plane.ofNormalDistance`.
    | Plane of normal: (float * float * float) * distance: float
    /// A sphere from its centre and its radius: `Sphere.ofCentreRadius`.
    | Sphere of centre: (float * float * float) * radius: float

/// An object a state holds: one of the algebra's six typed blades, as its constructor or its last
/// transform left it. Nothing normalises it (DECISIONS.md D85 item 5).
[<RequireQualifiedAccess>]
type GeometricObject =
    | Point of Point
    | PointPair of PointPair
    | Line of Line
    | Circle of Circle
    | Plane of Plane
    | Sphere of Sphere

/// A geometric edit of a state of named objects: DECISIONS.md D85 item 9's seven ops, with its
/// parameters and no coefficient. Angles are radians, applied as given; a direction or a normal is
/// used as it is given, and the algebra divides it by its norm.
[<RequireQualifiedAccess>]
type GeometricOp<'N> =
    /// Add the object `form` builds, under a name not already present.
    | Add of name: 'N * form: Form
    /// Remove the object of this name.
    | Remove of name: 'N
    /// Rotate the named object by `angle` about the axis through `point` with `direction`,
    /// counter-clockwise seen from the direction's tip: `Motor.ofScrew point direction angle 0.0`.
    | Rotate of name: 'N * point: (float * float * float) * direction: (float * float * float) * angle: float
    /// Translate the named object by `vector`: `Translator.ofVector`.
    | Translate of name: 'N * vector: (float * float * float)
    /// Scale the named object by `factor` about `centre`: `Dilator.ofCentreFactor`.
    | Scale of name: 'N * centre: (float * float * float) * factor: float
    /// Reflect the named object in the plane with `normal` at `distance` from the origin along it:
    /// `Reflection.ofNormalDistance`.
    | Reflect of name: 'N * normal: (float * float * float) * distance: float
    /// The general rigid motion: by `angle` about the axis through `point` with `direction`, and by
    /// `distance` along it: `Motor.ofScrew`.
    | Screw of
        name: 'N *
        point: (float * float * float) *
        direction: (float * float * float) *
        angle: float *
        distance: float

/// Why an op was refused — named, carried as data, and never a throw. The state is the one it was.
[<RequireQualifiedAccess>]
type GeometricOpRefusal<'N> =
    /// No object has this name.
    | UnknownName of name: 'N
    /// An object already has this name.
    | NameAlreadyPresent of name: 'N
    /// The algebra's constructor refused a parameter, and this is its refusal as it gave it: a
    /// non-finite parameter, a zero-length direction, a factor that is not positive, a negative
    /// radius, each by its own case.
    | Geometry of refusal: GeometryRefusal
    /// The object the op would leave under this name holds a NaN in a coefficient: an overflow inside
    /// the algebra's product (an infinity times a zero, or two infinities of opposite sign summed).
    /// A state never holds a NaN; an infinity is a value and stays (DECISIONS.md D90 item 5).
    | NotANumber of name: 'N
    /// The op has no inverse (`Remove`: the object it removed is not a parameter of it).
    | NotInvertible of op: string
    /// The op's inverse is the refused op this carries: the state after the op would refuse it (a scale
    /// by a factor whose reciprocal overflows, say), so no inverse is answered (DECISIONS.md D90 item 8).
    | InverseRefused of refusal: GeometricOpRefusal<'N>

/// A set of named objects, in ascending order of name under the caller's `Naming.Compare`. Immutable:
/// every op answers a new state or the one it was given.
[<Sealed>]
type GeometricState<'N> internal (naming: Naming<'N>, entries: ('N * GeometricObject) list) =
    member internal _.Naming = naming
    member internal _.Entries = entries

/// A versor the op derives, applied by the algebra's `applyTo…` of the object's kind.
type internal Versor =
    | ByMotor of Motor
    | ByTranslator of Translator
    | ByDilator of Dilator
    | ByReflection of Reflection

/// The rules this package's functions are written in, one member for each. `Rules.shipped` is the
/// record as DECISIONS.md D85 and D90 write it; the suite changes one member to show a vector fail.
type internal Rules<'N> =
    {
        /// How `applyAll` runs a list of ops: each in turn, in the order given, a refused one leaving
        /// the state as it was and noted with its index, and the next one applied after it.
        Fold:
            Rules<'N>
                -> GeometricOp<'N> list
                -> GeometricState<'N>
                -> GeometricState<'N> * (int * GeometricOpRefusal<'N>) list
        /// What a refusal answers: the refusal, so the caller keeps the state it had.
        Refused: GeometricState<'N> -> GeometricOpRefusal<'N> -> Result<GeometricState<'N>, GeometricOpRefusal<'N>>
        /// Where the object of a name is: the entry whose name compares equal to it.
        Find: Naming<'N> -> 'N -> ('N * GeometricObject) list -> int option
        /// Where a new object goes: before the first entry whose name compares above it.
        Place: Naming<'N> -> 'N -> GeometricObject -> ('N * GeometricObject) list -> ('N * GeometricObject) list
        /// What follows a transform: nothing (D85 item 5).
        After: GeometricObject -> GeometricObject
        /// An op's angle as the versor is given it: radians, as recorded.
        Angle: float -> float
        /// The rotation about the axis through a point: the screw of no distance.
        RotateVersor: float * float * float -> float * float * float -> float -> Result<Motor, GeometryRefusal>
        /// The screw: `Motor.ofScrew`.
        ScrewVersor: float * float * float -> float * float * float -> float -> float -> Result<Motor, GeometryRefusal>
        /// The dilation about a centre: `Dilator.ofCentreFactor`.
        ScaleVersor: float * float * float -> float -> Result<Dilator, GeometryRefusal>
        /// The translation: `Translator.ofVector`.
        TranslateVersor: float * float * float -> Result<Translator, GeometryRefusal>
        /// The reflection: `Reflection.ofNormalDistance`.
        ReflectVersor: float * float * float -> float -> Result<Reflection, GeometryRefusal>
        /// `canApply`: `apply`, its state discarded.
        Check: Rules<'N> -> GeometricOp<'N> -> GeometricState<'N> -> Result<unit, GeometricOpRefusal<'N>>
        /// The op that undoes an applicable op, or `None` for one that has none.
        Inverse: GeometricOp<'N> -> GeometricOp<'N> option
        /// The one op two adjacent rigid ops of one object compact to, or `None`.
        Merge: Naming<'N> -> GeometricOp<'N> -> GeometricOp<'N> -> GeometricOp<'N> option
        /// A float as the wire writes it: its bit pattern, sixteen lowercase hex digits.
        FloatText: float -> string
        /// The bytes the op-stream chains an op by: its canonical text.
        ChainText: Rules<'N> -> Naming<'N> -> GeometricOp<'N> -> string
        /// Whether an object may stand in a state: no coefficient is a NaN.
        Holds: GeometricObject -> bool
        /// Whether an inverse is answered: the state after the op accepts it, as `canApply` says.
        Undoes:
            Rules<'N>
                -> GeometricOp<'N>
                -> GeometricState<'N>
                -> GeometricOp<'N>
                -> Result<unit, GeometricOpRefusal<'N>>
    }

/// The op algebra, written once over `Rules`.
module internal Ops =

    // ---- names and entries ----------------------------------------------------------------------

    let find (naming: Naming<'N>) (name: 'N) (entries: ('N * GeometricObject) list) : int option =
        entries |> List.tryFindIndex (fun (n, _) -> naming.Compare n name = 0)

    let place
        (naming: Naming<'N>)
        (name: 'N)
        (o: GeometricObject)
        (entries: ('N * GeometricObject) list)
        : ('N * GeometricObject) list =
        let before, after =
            entries |> List.partition (fun (n, _) -> naming.Compare n name < 0)

        before @ [ name, o ] @ after

    let private replaceAt (index: int) (o: GeometricObject) (entries: ('N * GeometricObject) list) =
        entries |> List.mapi (fun i (n, x) -> if i = index then n, o else n, x)

    let private removeAt (index: int) (entries: ('N * GeometricObject) list) =
        entries
        |> List.indexed
        |> List.filter (fun (i, _) -> i <> index)
        |> List.map snd

    // ---- the objects and the versors ------------------------------------------------------------

    /// The object a form builds, through the algebra's constructors, its parameters in the form's order.
    let build (form: Form) : Result<GeometricObject, GeometryRefusal> =
        match form with
        | Form.Point p -> Point.up p |> Result.map GeometricObject.Point
        | Form.PointPair(p, q) ->
            Point.up p
            |> Result.bind (fun first ->
                Point.up q
                |> Result.map (fun second -> GeometricObject.PointPair(PointPair.through first second)))
        | Form.Line(p, d) -> Line.ofPointDirection p d |> Result.map GeometricObject.Line
        | Form.Circle(c, radius, normal) ->
            Circle.ofCentreRadiusNormal c radius normal |> Result.map GeometricObject.Circle
        | Form.Plane(normal, distance) -> Plane.ofNormalDistance normal distance |> Result.map GeometricObject.Plane
        | Form.Sphere(c, radius) -> Sphere.ofCentreRadius c radius |> Result.map GeometricObject.Sphere

    /// The versor a transform derives, or the constructor's refusal. `None` for `Add` and `Remove`.
    let versorOf (r: Rules<'N>) (op: GeometricOp<'N>) : Result<Versor, GeometryRefusal> option =
        match op with
        | GeometricOp.Add _
        | GeometricOp.Remove _ -> None
        | GeometricOp.Rotate(_, p, d, angle) -> Some(r.RotateVersor p d (r.Angle angle) |> Result.map ByMotor)
        | GeometricOp.Screw(_, p, d, angle, distance) ->
            Some(r.ScrewVersor p d (r.Angle angle) distance |> Result.map ByMotor)
        | GeometricOp.Translate(_, t) -> Some(r.TranslateVersor t |> Result.map ByTranslator)
        | GeometricOp.Scale(_, c, factor) -> Some(r.ScaleVersor c factor |> Result.map ByDilator)
        | GeometricOp.Reflect(_, normal, distance) -> Some(r.ReflectVersor normal distance |> Result.map ByReflection)

    /// A versor applied to an object by the algebra's `applyTo…` of the object's kind.
    let transform (v: Versor) (o: GeometricObject) : GeometricObject =
        match v, o with
        | ByMotor m, GeometricObject.Point x -> GeometricObject.Point(Motor.applyToPoint m x)
        | ByMotor m, GeometricObject.PointPair x -> GeometricObject.PointPair(Motor.applyToPointPair m x)
        | ByMotor m, GeometricObject.Line x -> GeometricObject.Line(Motor.applyToLine m x)
        | ByMotor m, GeometricObject.Circle x -> GeometricObject.Circle(Motor.applyToCircle m x)
        | ByMotor m, GeometricObject.Plane x -> GeometricObject.Plane(Motor.applyToPlane m x)
        | ByMotor m, GeometricObject.Sphere x -> GeometricObject.Sphere(Motor.applyToSphere m x)
        | ByTranslator t, GeometricObject.Point x -> GeometricObject.Point(Translator.applyToPoint t x)
        | ByTranslator t, GeometricObject.PointPair x -> GeometricObject.PointPair(Translator.applyToPointPair t x)
        | ByTranslator t, GeometricObject.Line x -> GeometricObject.Line(Translator.applyToLine t x)
        | ByTranslator t, GeometricObject.Circle x -> GeometricObject.Circle(Translator.applyToCircle t x)
        | ByTranslator t, GeometricObject.Plane x -> GeometricObject.Plane(Translator.applyToPlane t x)
        | ByTranslator t, GeometricObject.Sphere x -> GeometricObject.Sphere(Translator.applyToSphere t x)
        | ByDilator s, GeometricObject.Point x -> GeometricObject.Point(Dilator.applyToPoint s x)
        | ByDilator s, GeometricObject.PointPair x -> GeometricObject.PointPair(Dilator.applyToPointPair s x)
        | ByDilator s, GeometricObject.Line x -> GeometricObject.Line(Dilator.applyToLine s x)
        | ByDilator s, GeometricObject.Circle x -> GeometricObject.Circle(Dilator.applyToCircle s x)
        | ByDilator s, GeometricObject.Plane x -> GeometricObject.Plane(Dilator.applyToPlane s x)
        | ByDilator s, GeometricObject.Sphere x -> GeometricObject.Sphere(Dilator.applyToSphere s x)
        | ByReflection f, GeometricObject.Point x -> GeometricObject.Point(Reflection.applyToPoint f x)
        | ByReflection f, GeometricObject.PointPair x -> GeometricObject.PointPair(Reflection.applyToPointPair f x)
        | ByReflection f, GeometricObject.Line x -> GeometricObject.Line(Reflection.applyToLine f x)
        | ByReflection f, GeometricObject.Circle x -> GeometricObject.Circle(Reflection.applyToCircle f x)
        | ByReflection f, GeometricObject.Plane x -> GeometricObject.Plane(Reflection.applyToPlane f x)
        | ByReflection f, GeometricObject.Sphere x -> GeometricObject.Sphere(Reflection.applyToSphere f x)

    /// An object's thirty-two coefficients, by the algebra's widening: a copy, for the laws and the suite.
    let values (o: GeometricObject) : float[] =
        match o with
        | GeometricObject.Point x -> Point.toMultivector x
        | GeometricObject.PointPair x -> PointPair.toMultivector x
        | GeometricObject.Line x -> Line.toMultivector x
        | GeometricObject.Circle x -> Circle.toMultivector x
        | GeometricObject.Plane x -> Plane.toMultivector x
        | GeometricObject.Sphere x -> Sphere.toMultivector x
        |> Multivector.toArray

    let nameOf (op: GeometricOp<'N>) : 'N =
        match op with
        | GeometricOp.Add(n, _)
        | GeometricOp.Remove n
        | GeometricOp.Rotate(n, _, _, _)
        | GeometricOp.Translate(n, _)
        | GeometricOp.Scale(n, _, _)
        | GeometricOp.Reflect(n, _, _)
        | GeometricOp.Screw(n, _, _, _, _) -> n

    /// The op's name on the wire: its `$type`.
    let tagOf (op: GeometricOp<'N>) : string =
        match op with
        | GeometricOp.Add _ -> "add"
        | GeometricOp.Remove _ -> "remove"
        | GeometricOp.Rotate _ -> "rotate"
        | GeometricOp.Translate _ -> "translate"
        | GeometricOp.Scale _ -> "scale"
        | GeometricOp.Reflect _ -> "reflect"
        | GeometricOp.Screw _ -> "screw"

    // ---- apply ----------------------------------------------------------------------------------

    /// Whether an object may stand in a state: no coefficient is a NaN. An infinity is a value.
    let holds (o: GeometricObject) : bool =
        values o |> Array.forall (fun x -> not (Double.IsNaN x))

    /// One op applied to a state: the name first, then the parameters through the algebra's
    /// constructor in the order the op lists them, then the object it leaves, which holds no NaN; any
    /// refusal through `Refused`.
    let applyWith
        (r: Rules<'N>)
        (op: GeometricOp<'N>)
        (s: GeometricState<'N>)
        : Result<GeometricState<'N>, GeometricOpRefusal<'N>> =
        let naming = s.Naming
        let entries = s.Entries
        let name = nameOf op

        // The state with `o` under the name, or the refusal of an object that holds a NaN.
        let leaving (o: GeometricObject) (into: GeometricObject -> ('N * GeometricObject) list) =
            if r.Holds o then
                Ok(GeometricState(naming, into o))
            else
                r.Refused s (GeometricOpRefusal.NotANumber name)

        match op, r.Find naming name entries with
        | GeometricOp.Add _, Some _ -> r.Refused s (GeometricOpRefusal.NameAlreadyPresent name)
        | GeometricOp.Add(_, form), None ->
            match build form with
            | Error g -> r.Refused s (GeometricOpRefusal.Geometry g)
            | Ok o -> leaving o (fun o -> r.Place naming name o entries)
        | _, None -> r.Refused s (GeometricOpRefusal.UnknownName name)
        | GeometricOp.Remove _, Some index -> Ok(GeometricState(naming, removeAt index entries))
        | _, Some index ->
            match versorOf r op with
            | None -> Ok s // unreachable: `Add` and `Remove` are answered above
            | Some(Error g) -> r.Refused s (GeometricOpRefusal.Geometry g)
            | Some(Ok v) ->
                let _, o = List.item index entries
                leaving (r.After(transform v o)) (fun o -> replaceAt index o entries)

    let tryFindWith (r: Rules<'N>) (name: 'N) (s: GeometricState<'N>) : GeometricObject option =
        r.Find s.Naming name s.Entries
        |> Option.map (fun index -> snd (List.item index s.Entries))

    let canApplyWith
        (r: Rules<'N>)
        (op: GeometricOp<'N>)
        (s: GeometricState<'N>)
        : Result<unit, GeometricOpRefusal<'N>> =
        r.Check r op s

    let applyAllWith
        (r: Rules<'N>)
        (ops: GeometricOp<'N> list)
        (s: GeometricState<'N>)
        : GeometricState<'N> * (int * GeometricOpRefusal<'N>) list =
        r.Fold r ops s

    /// Each op in turn, in the order given; a refused one leaves the state as it was and is noted.
    let fold
        (r: Rules<'N>)
        (ops: GeometricOp<'N> list)
        (s: GeometricState<'N>)
        : GeometricState<'N> * (int * GeometricOpRefusal<'N>) list =
        let rec go (index: int) (state: GeometricState<'N>) (refusals: (int * GeometricOpRefusal<'N>) list) =
            function
            | [] -> state, List.rev refusals
            | op :: rest ->
                match applyWith r op state with
                | Ok next -> go (index + 1) next refusals rest
                | Error e -> go (index + 1) state ((index, e) :: refusals) rest

        go 0 s [] ops

    // ---- the inverse ----------------------------------------------------------------------------

    /// The inverse of each op (DECISIONS.md D90): an `Add`'s is the `Remove` of its name; a rotation's
    /// and a screw's negate the angle, and a screw's its distance too; a translation's negates the
    /// vector; a scale's takes the reciprocal of the factor; a reflection is its own. A `Remove` has
    /// none.
    let inverse (op: GeometricOp<'N>) : GeometricOp<'N> option =
        match op with
        | GeometricOp.Add(n, _) -> Some(GeometricOp.Remove n)
        | GeometricOp.Remove _ -> None
        | GeometricOp.Rotate(n, p, d, angle) -> Some(GeometricOp.Rotate(n, p, d, -angle))
        | GeometricOp.Screw(n, p, d, angle, distance) -> Some(GeometricOp.Screw(n, p, d, -angle, -distance))
        | GeometricOp.Translate(n, (x, y, z)) -> Some(GeometricOp.Translate(n, (-x, -y, -z)))
        | GeometricOp.Scale(n, c, factor) -> Some(GeometricOp.Scale(n, c, 1.0 / factor))
        | GeometricOp.Reflect _ -> Some op

    /// Whether the state after `op` accepts its inverse: `canApply` over that state. An op that does not
    /// apply has no state after it, and its own refusal is the answer.
    let undoes
        (r: Rules<'N>)
        (op: GeometricOp<'N>)
        (s: GeometricState<'N>)
        (inv: GeometricOp<'N>)
        : Result<unit, GeometricOpRefusal<'N>> =
        applyWith r op s
        |> Result.bind (fun after -> r.Check r inv after |> Result.mapError GeometricOpRefusal.InverseRefused)

    /// The inverse of an op applied to the PRE-state, guarded twice: an op the state would refuse has no
    /// inverse, and its refusal is the answer; and an inverse the state after the op would refuse is not
    /// answered, its refusal carried in `InverseRefused`.
    let invertWith
        (r: Rules<'N>)
        (op: GeometricOp<'N>)
        (s: GeometricState<'N>)
        : Result<GeometricOp<'N>, GeometricOpRefusal<'N>> =
        match op with
        // No inverse at any state: answered before the guard.
        | GeometricOp.Remove _ -> Error(GeometricOpRefusal.NotInvertible "Remove")
        | _ ->
            match r.Check r op s with
            | Error e -> Error e
            | Ok() ->
                match r.Inverse op with
                | Some inv -> r.Undoes r op s inv |> Result.map (fun () -> inv)
                | None -> Error(GeometricOpRefusal.NotInvertible(tagOf op))

    // ---- the codec ------------------------------------------------------------------------------

    let private digits = "0123456789abcdef"

    /// The bit pattern written for every NaN, whatever its sign and payload (DECISIONS.md D51 item 7).
    let private nanBits: int64 = 0x7ff8000000000000L

    /// A float as its IEEE-754 binary64 bit pattern, sixteen lowercase hex digits, a NaN as
    /// `7ff8000000000000` (DECISIONS.md D51 item 7, `LinAlgWire`'s rule).
    let bitsText (x: float) : string =
        let b =
            if Double.IsNaN x then
                nanBits
            else
                BitConverter.DoubleToInt64Bits x

        String(Array.init 16 (fun k -> digits[int ((b >>> (4 * (15 - k))) &&& 0xFL)]))

    let private hexDigit (c: char) : int =
        if c >= '0' && c <= '9' then int c - int '0'
        elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
        else -1

    /// The `count` floats of a bits text, a NaN pattern read as the host's NaN.
    let floatsOf (count: int) (text: string) : Result<float[], string> =
        if text.Length <> 16 * count then
            Error(sprintf "expected %d lowercase hex digits, found %d characters" (16 * count) text.Length)
        else
            match text |> Seq.tryFindIndex (fun c -> hexDigit c < 0) with
            | Some at -> Error(sprintf "expected lowercase hex digits, found '%c' at %d" text[at] at)
            | None ->
                Ok(
                    Array.init count (fun k ->
                        let mutable b = 0L

                        for d in 0..15 do
                            b <- (b <<< 4) ||| int64 (hexDigit text[16 * k + d])

                        let x = BitConverter.Int64BitsToDouble b
                        if Double.IsNaN x then Double.NaN else x)
                )

    let private triple (r: Rules<'N>) (x: float, y: float, z: float) : JVal =
        JStr(r.FloatText x + r.FloatText y + r.FloatText z)

    let private one (r: Rules<'N>) (x: float) : JVal = JStr(r.FloatText x)

    let private formJson (r: Rules<'N>) (form: Form) : JVal =
        match form with
        | Form.Point p -> Canonical.typed "point" [ "point", triple r p ]
        | Form.PointPair(p, q) -> Canonical.typed "pointPair" [ "first", triple r p; "second", triple r q ]
        | Form.Line(p, d) -> Canonical.typed "line" [ "point", triple r p; "direction", triple r d ]
        | Form.Circle(c, radius, normal) ->
            Canonical.typed "circle" [ "centre", triple r c; "radius", one r radius; "normal", triple r normal ]
        | Form.Plane(normal, distance) ->
            Canonical.typed "plane" [ "normal", triple r normal; "distance", one r distance ]
        | Form.Sphere(c, radius) -> Canonical.typed "sphere" [ "centre", triple r c; "radius", one r radius ]

    let encodeJson (r: Rules<'N>) (naming: Naming<'N>) (op: GeometricOp<'N>) : JVal =
        let name n = "name", JStr(naming.Encode n)

        match op with
        | GeometricOp.Add(n, form) -> Canonical.typed "add" [ name n; "form", formJson r form ]
        | GeometricOp.Remove n -> Canonical.typed "remove" [ name n ]
        | GeometricOp.Rotate(n, p, d, angle) ->
            Canonical.typed "rotate" [ name n; "point", triple r p; "direction", triple r d; "angle", one r angle ]
        | GeometricOp.Translate(n, t) -> Canonical.typed "translate" [ name n; "vector", triple r t ]
        | GeometricOp.Scale(n, c, factor) ->
            Canonical.typed "scale" [ name n; "centre", triple r c; "factor", one r factor ]
        | GeometricOp.Reflect(n, normal, distance) ->
            Canonical.typed "reflect" [ name n; "normal", triple r normal; "distance", one r distance ]
        | GeometricOp.Screw(n, p, d, angle, distance) ->
            Canonical.typed
                "screw"
                [ name n
                  "point", triple r p
                  "direction", triple r d
                  "angle", one r angle
                  "distance", one r distance ]

    /// The canonical text of an op: `Rendering.CrossHost`, its members sorted, every float its bit pattern.
    let encodeWith (r: Rules<'N>) (naming: Naming<'N>) (op: GeometricOp<'N>) : string =
        Canonical.write Rendering.CrossHost (encodeJson r naming op)

    /// The members of an object, refused unless they are exactly `expected` and `$type`.
    let private members (expected: string list) (fields: (string * JVal) list) : Result<Map<string, JVal>, string> =
        let found =
            fields |> List.map fst |> List.filter (fun k -> k <> "$type") |> List.sort

        let wanted = List.sort expected

        if found <> wanted then
            Error(sprintf "members: expected %s, found %s" (String.concat ", " wanted) (String.concat ", " found))
        else
            Ok(Map.ofList fields)

    let private text (key: string) (m: Map<string, JVal>) : Result<string, string> =
        match Map.find key m with
        | JStr s -> Ok s
        | _ -> Error(key + ": expected a string")

    let private floatAt (key: string) (m: Map<string, JVal>) : Result<float, string> =
        text key m
        |> Result.bind (fun s -> floatsOf 1 s |> Result.mapError (fun e -> key + ": " + e))
        |> Result.map (fun xs -> xs[0])

    let private tripleAt (key: string) (m: Map<string, JVal>) : Result<float * float * float, string> =
        text key m
        |> Result.bind (fun s -> floatsOf 3 s |> Result.mapError (fun e -> key + ": " + e))
        |> Result.map (fun xs -> xs[0], xs[1], xs[2])

    let private typedObject (el: JVal) : Result<string * (string * JVal) list, string> =
        match el with
        | JObj fields ->
            match fields |> List.tryFind (fun (k, _) -> k = "$type") with
            | Some(_, JStr tag) -> Ok(tag, fields)
            | Some _ -> Error "$type: expected a string"
            | None -> Error "$type: missing"
        | _ -> Error "expected an object"

    let private formOf (el: JVal) : Result<Form, string> =
        typedObject el
        |> Result.mapError (fun e -> "form: " + e)
        |> Result.bind (fun (tag, fields) ->
            let read (expected: string list) (make: Map<string, JVal> -> Result<Form, string>) =
                members expected fields
                |> Result.bind make
                |> Result.mapError (fun e -> "form: " + e)

            match tag with
            | "point" -> read [ "point" ] (fun m -> tripleAt "point" m |> Result.map Form.Point)
            | "pointPair" ->
                read [ "first"; "second" ] (fun m ->
                    tripleAt "first" m
                    |> Result.bind (fun p -> tripleAt "second" m |> Result.map (fun q -> Form.PointPair(p, q))))
            | "line" ->
                read [ "point"; "direction" ] (fun m ->
                    tripleAt "point" m
                    |> Result.bind (fun p -> tripleAt "direction" m |> Result.map (fun d -> Form.Line(p, d))))
            | "circle" ->
                read [ "centre"; "radius"; "normal" ] (fun m ->
                    tripleAt "centre" m
                    |> Result.bind (fun c ->
                        floatAt "radius" m
                        |> Result.bind (fun radius ->
                            tripleAt "normal" m |> Result.map (fun normal -> Form.Circle(c, radius, normal)))))
            | "plane" ->
                read [ "normal"; "distance" ] (fun m ->
                    tripleAt "normal" m
                    |> Result.bind (fun normal ->
                        floatAt "distance" m
                        |> Result.map (fun distance -> Form.Plane(normal, distance))))
            | "sphere" ->
                read [ "centre"; "radius" ] (fun m ->
                    tripleAt "centre" m
                    |> Result.bind (fun c -> floatAt "radius" m |> Result.map (fun radius -> Form.Sphere(c, radius))))
            | other -> Error("form: unknown form: " + other))

    let decodeJson (naming: Naming<'N>) (el: JVal) : Result<GeometricOp<'N>, string> =
        typedObject el
        |> Result.bind (fun (tag, fields) ->
            let read (expected: string list) (make: 'N -> Map<string, JVal> -> Result<GeometricOp<'N>, string>) =
                members ("name" :: expected) fields
                |> Result.bind (fun m ->
                    text "name" m
                    |> Result.bind (fun s -> naming.Decode s |> Result.mapError (fun e -> "name: " + e))
                    |> Result.bind (fun n -> make n m))

            match tag with
            | "add" ->
                read [ "form" ] (fun n m -> formOf (Map.find "form" m) |> Result.map (fun f -> GeometricOp.Add(n, f)))
            | "remove" -> read [] (fun n _ -> Ok(GeometricOp.Remove n))
            | "rotate" ->
                read [ "point"; "direction"; "angle" ] (fun n m ->
                    tripleAt "point" m
                    |> Result.bind (fun p ->
                        tripleAt "direction" m
                        |> Result.bind (fun d ->
                            floatAt "angle" m
                            |> Result.map (fun angle -> GeometricOp.Rotate(n, p, d, angle)))))
            | "translate" ->
                read [ "vector" ] (fun n m -> tripleAt "vector" m |> Result.map (fun t -> GeometricOp.Translate(n, t)))
            | "scale" ->
                read [ "centre"; "factor" ] (fun n m ->
                    tripleAt "centre" m
                    |> Result.bind (fun c ->
                        floatAt "factor" m |> Result.map (fun factor -> GeometricOp.Scale(n, c, factor))))
            | "reflect" ->
                read [ "normal"; "distance" ] (fun n m ->
                    tripleAt "normal" m
                    |> Result.bind (fun normal ->
                        floatAt "distance" m
                        |> Result.map (fun distance -> GeometricOp.Reflect(n, normal, distance))))
            | "screw" ->
                read [ "point"; "direction"; "angle"; "distance" ] (fun n m ->
                    tripleAt "point" m
                    |> Result.bind (fun p ->
                        tripleAt "direction" m
                        |> Result.bind (fun d ->
                            floatAt "angle" m
                            |> Result.bind (fun angle ->
                                floatAt "distance" m
                                |> Result.map (fun distance -> GeometricOp.Screw(n, p, d, angle, distance))))))
            | other -> Error("unknown op: " + other))

    /// The op a text spells, or why it spells none: `not json: …`, or the member that failed.
    let decode (naming: Naming<'N>) (s: string) : Result<GeometricOp<'N>, string> =
        match Canonical.read s with
        | Error e -> Error("not json: " + DecodeError.describe e)
        | Ok el -> decodeJson naming el

    // ---- the stream witness ---------------------------------------------------------------------

    let streamWitnessWith
        (r: Rules<'N>)
        (naming: Naming<'N>)
        : StreamWitness<GeometricOp<'N>, GeometricState<'N>, GeometricOpRefusal<'N>> =
        { Apply = applyWith r
          Encode = r.ChainText r naming
          Decode = decode naming }

    // ---- the compaction: called by the caller, and by nothing above ------------------------------

    let private sameBits (a: float) (b: float) : bool =
        BitConverter.DoubleToInt64Bits a = BitConverter.DoubleToInt64Bits b

    let private sameTriple (a1: float, a2: float, a3: float) (b1: float, b2: float, b3: float) : bool =
        sameBits a1 b1 && sameBits a2 b2 && sameBits a3 b3

    /// Two adjacent rigid ops of one object as one op: two translations as the translation by the sum
    /// of their vectors; two rotations or screws about one axis, given by the same point and the same
    /// direction bit for bit, as the screw by the sum of their angles and distances, a rotation when
    /// neither has a distance. Each sum is one rounded addition, the earlier op's parameter first.
    let merge (naming: Naming<'N>) (a: GeometricOp<'N>) (b: GeometricOp<'N>) : GeometricOp<'N> option =
        if naming.Compare (nameOf a) (nameOf b) <> 0 then
            None
        else
            match a, b with
            | GeometricOp.Translate(n, (x1, y1, z1)), GeometricOp.Translate(_, (x2, y2, z2)) ->
                Some(GeometricOp.Translate(n, (x1 + x2, y1 + y2, z1 + z2)))
            | GeometricOp.Rotate(n, p, d, a1), GeometricOp.Rotate(_, q, e, a2) when sameTriple p q && sameTriple d e ->
                Some(GeometricOp.Rotate(n, p, d, a1 + a2))
            | GeometricOp.Rotate(n, p, d, a1), GeometricOp.Screw(_, q, e, a2, d2) when sameTriple p q && sameTriple d e ->
                Some(GeometricOp.Screw(n, p, d, a1 + a2, d2))
            | GeometricOp.Screw(n, p, d, a1, d1), GeometricOp.Rotate(_, q, e, a2) when sameTriple p q && sameTriple d e ->
                Some(GeometricOp.Screw(n, p, d, a1 + a2, d1))
            | GeometricOp.Screw(n, p, d, a1, d1), GeometricOp.Screw(_, q, e, a2, d2) when
                sameTriple p q && sameTriple d e
                ->
                Some(GeometricOp.Screw(n, p, d, a1 + a2, d1 + d2))
            | _ -> None

    let private rigid (op: GeometricOp<'N>) : bool =
        match op with
        | GeometricOp.Rotate _
        | GeometricOp.Translate _
        | GeometricOp.Screw _ -> true
        | _ -> false

    /// A list of ops with each run of adjacent rigid ops of one object answered as one op, read over
    /// the state they start from. An op ends a run when it is not a rotation, a translation or a
    /// screw (a scale, a reflection, an `Add`, a `Remove`), names another object, is refused, or does
    /// not merge (`merge`); and a merged op that the run's starting state would refuse (a sum that
    /// overflowed, say) is not formed, and the run ends there. Everything else is kept as given.
    let compactWith (r: Rules<'N>) (ops: GeometricOp<'N> list) (s: GeometricState<'N>) : GeometricOp<'N> list =
        let flush (pending: (GeometricOp<'N> * GeometricState<'N>) option) (acc: GeometricOp<'N> list) =
            match pending with
            | Some(op, _) -> op :: acc
            | None -> acc

        let rec go
            (state: GeometricState<'N>)
            (pending: (GeometricOp<'N> * GeometricState<'N>) option)
            (acc: GeometricOp<'N> list)
            =
            function
            | [] -> List.rev (flush pending acc)
            | op :: rest ->
                match applyWith r op state with
                | Error _ -> go state None (op :: flush pending acc) rest
                | Ok next ->
                    let merged =
                        match pending with
                        | Some(run, start) when rigid op ->
                            match r.Merge state.Naming run op with
                            | Some m when Result.isOk (applyWith r m start) -> Some(m, start)
                            | _ -> None
                        | _ -> None

                    match merged with
                    | Some p -> go next (Some p) acc rest
                    | None ->
                        let acc' = flush pending acc

                        if rigid op then
                            go next (Some(op, state)) acc' rest
                        else
                            go next None (op :: acc') rest

        go s None [] ops

/// The rules as DECISIONS.md D85 and D90 write them.
module internal Rules =

    let shipped<'N> : Rules<'N> =
        { Fold = Ops.fold
          Refused = fun _ e -> Error e
          Find = Ops.find
          Place = Ops.place
          After = id
          Angle = id
          RotateVersor = fun p d angle -> Motor.ofScrew p d angle 0.0
          ScrewVersor = Motor.ofScrew
          ScaleVersor = Dilator.ofCentreFactor
          TranslateVersor = Translator.ofVector
          ReflectVersor = Reflection.ofNormalDistance
          Check = fun r op s -> Ops.applyWith r op s |> Result.map ignore
          Inverse = Ops.inverse
          Merge = Ops.merge
          FloatText = Ops.bitsText
          ChainText = Ops.encodeWith
          Holds = Ops.holds
          Undoes = Ops.undoes }

/// A state of named objects (DECISIONS.md D90).
[<RequireQualifiedAccess>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module GeometricState =

    /// The state that holds nothing, its names compared and spelled by `naming`.
    let empty (naming: Naming<'N>) : GeometricState<'N> = GeometricState(naming, [])

    /// Every name and its object, in ascending order of name under the state's `Naming.Compare`.
    let toList (s: GeometricState<'N>) : ('N * GeometricObject) list = s.Entries

    /// The object of this name, if one has it.
    let tryFind (name: 'N) (s: GeometricState<'N>) : GeometricObject option = Ops.tryFindWith Rules.shipped name s

    /// How many objects the state holds.
    let count (s: GeometricState<'N>) : int = List.length s.Entries

/// The op algebra over a state of named objects (DECISIONS.md D85 item 9, D90): `apply`, `canApply`,
/// `applyAll`, `invert`, the codec and the `Fuaran.Core.OpStream` witness; and `compact`, which the
/// caller calls and nothing here does.
[<RequireQualifiedAccess>]
module GeometricOps =

    /// The state after `op`, or its refusal by name; total, and never a throw. The name is read first,
    /// then the parameters, through the algebra's constructor, in the order the op lists them; the
    /// object and the versor are the constructor's, applied by the algebra's `applyTo…`, and nothing
    /// is normalised. An object that would hold a NaN is refused, `NotANumber` with its name: a state
    /// never holds a NaN, and an infinity is a value (DECISIONS.md D90 item 5). A refused op leaves the
    /// state the caller holds as it was.
    let apply (op: GeometricOp<'N>) (s: GeometricState<'N>) : Result<GeometricState<'N>, GeometricOpRefusal<'N>> =
        Ops.applyWith Rules.shipped op s

    /// `Ok ()` exactly when `apply` answers `Ok`, with its refusal otherwise: `apply`, its state discarded.
    let canApply (op: GeometricOp<'N>) (s: GeometricState<'N>) : Result<unit, GeometricOpRefusal<'N>> =
        Ops.canApplyWith Rules.shipped op s

    /// Each op in turn, in the order given: the state at the end, and each refused op's index and
    /// refusal. A refused op leaves the state as it was, and the next op is applied after it.
    let applyAll
        (ops: GeometricOp<'N> list)
        (s: GeometricState<'N>)
        : GeometricState<'N> * (int * GeometricOpRefusal<'N>) list =
        Ops.applyAllWith Rules.shipped ops s

    /// The op that undoes `op` applied to the PRE-state `s`, guarded twice: an op `s` refuses has no
    /// inverse, and its refusal is the answer; and an inverse the state after `op` would refuse is not
    /// answered, `InverseRefused` carrying that refusal (a scale whose reciprocal overflows, or any
    /// transform whose result, undone, would hold a NaN: DECISIONS.md D90 item 8). `Add`'s is the `Remove` of its name, exact. A
    /// `Rotate`'s negates the angle and a `Screw`'s the angle and the distance; a `Translate`'s negates
    /// the vector; a `Scale`'s is the scale about the same centre by `1.0 / factor`, one rounded
    /// division; a `Reflect` is its own. `Remove` is `NotInvertible`: the object it removed is not
    /// one of its parameters, and undo of a `Remove` is truncate and replay. No transform's inverse is
    /// exact in general: each undoes its op in exact arithmetic, and in binary64 the state comes back to
    /// rounding (a translation of quarters is exact in value, a `-0.0` coming back `+0.0`). There is
    /// no bound on the error. `GeometricAlgebraOpsLaws` PINS the largest error over one stated sample of
    /// 1,200 objects at seed 731, with no margin, and other samples exceed it (DECISIONS.md D90 item 10
    /// records nine seeds). On that sample, at the object's scale, in units in the last place of its
    /// largest coefficient: `Rotate` 65.5, `Translate` 58.0625, `Scale` 50, `Reflect` 105.5, `Screw`
    /// 135.5; another seed's rotation sample reaches 284 and a screw's 602.5. Coefficient by coefficient,
    /// D85 item 7's form, the figure reaches 3.2e16 units in the last place where the original
    /// coefficient is a rounding residue of a zero: no statement of accuracy near zero.
    let invert (op: GeometricOp<'N>) (s: GeometricState<'N>) : Result<GeometricOp<'N>, GeometricOpRefusal<'N>> =
        Ops.invertWith Rules.shipped op s

    /// The canonical text of an op (`Rendering.CrossHost`): `{"$type":…}` with its members sorted, the
    /// name as `naming.Encode` spells it, and every float its IEEE-754 bit pattern in sixteen lowercase
    /// hex digits, a triple's three concatenated (DECISIONS.md D51 item 7's rule). A NaN is written
    /// `7ff8000000000000`; an infinity is written as its pattern, and `apply` refuses it.
    let encode (naming: Naming<'N>) (op: GeometricOp<'N>) : string = Ops.encodeWith Rules.shipped naming op

    /// The op a text spells, or why it spells none: `not json: …`, `unknown op: …`, `members: …`, a
    /// member that is not its count of lowercase hex digits, or `name: …` with the caller's reason.
    /// `decode (encode op)` is `op` bit for bit, `-0.0` included; a NaN reads back as `Double.NaN`.
    let decode (naming: Naming<'N>) (text: string) : Result<GeometricOp<'N>, string> = Ops.decode naming text

    /// The `Fuaran.Core.OpStream` `StreamWitness`: `apply`, `encode` and `decode`. With it
    /// `OpStream.append`, `verifyChain` and `replay` chain, verify and replay a history of transforms.
    let streamWitness
        (naming: Naming<'N>)
        : StreamWitness<GeometricOp<'N>, GeometricState<'N>, GeometricOpRefusal<'N>> =
        Ops.streamWitnessWith Rules.shipped naming

    /// `ops` with each run of adjacent rigid ops of one object answered as one op, read over the state
    /// `s` they start from. Two translations merge into the translation by the sum of their vectors;
    /// rotations and screws about one axis, given by the same point and direction bit for bit, into
    /// the screw by the sum of their angles and distances, a rotation when none has a distance. A
    /// scale, a reflection, an `Add`, a `Remove`, an op on another object, a refused op, and a rigid op
    /// that does not merge each end a run; a run of rotations about different axes is not merged (the
    /// screw's parameters read back from a composed motor are not ruled: DECISIONS.md D90). Nothing in
    /// `apply`, the codec or the witness calls this. Applying the answer reaches the state the ops reach
    /// to rounding, and there is no bound on the error. `GeometricAlgebraOpsLaws` PINS the largest error
    /// over one stated sample of 1,200 runs at seed 731, with no margin, and other samples exceed it
    /// (DECISIONS.md D90 item 10 records nine seeds): on that sample, at the object's scale, in units in
    /// the last place of its largest coefficient, translations 32 and rotations and screws about one axis
    /// 71, which another seed's sample reaches 345 for; coefficient by coefficient, D85 item 7's form,
    /// 6.8e15 and 7808 where a coefficient is a rounding residue of a zero.
    let compact (ops: GeometricOp<'N> list) (s: GeometricState<'N>) : GeometricOp<'N> list =
        Ops.compactWith Rules.shipped ops s
