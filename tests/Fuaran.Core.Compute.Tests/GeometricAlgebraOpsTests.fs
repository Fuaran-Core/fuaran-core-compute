module Fuaran.Compute.Tests.GeometricAlgebraOpsTests

// ---------------------------------------------------------------------------
// Phase 731 — the suite over Fuaran.Compute.GeometricAlgebraOps (DECISIONS.md D85
// item 9, D90): the law family on .NET, the committed law vectors, the exactness
// table, the wrong ports held against every function, the source rules (no
// compaction reached from apply, the codec or the witness; no float literal in a
// comparison; no host mathematics call), and the bounds' other seeds.
//
// Every wrong port is the shipped function with ONE member of `Rules.shipped`
// changed, never a second implementation written for the test. No case reads the
// host's mathematics library, the clock or the file system for an expected value:
// the expected values are the committed vectors' and the laws' own.
// ---------------------------------------------------------------------------

open System
open System.Globalization
open System.IO
open System.Reflection
open System.Text.RegularExpressions
open Expecto
open Fuaran.Core
open Fuaran.Compute.GeometricAlgebra
open Fuaran.Compute.GeometricAlgebraOps

module V = GeometricAlgebraOpsVectors

let private committedPath () =
    Snapshots.repoFile (Path.Combine("conformance", "laws", V.fileName))

let private naming = V.naming
let private shipped: Rules<string> = Rules.shipped

/// The published functions, each written over `r`.
let private functionsOf (r: Rules<string>) : V.Functions =
    { Apply = Ops.applyWith r
      CanApply = Ops.canApplyWith r
      ApplyAll = Ops.applyAllWith r
      Invert = Ops.invertWith r
      Compact = Ops.compactWith r
      Encode = Ops.encodeWith r naming
      Decode = Ops.decode naming
      Witness = Ops.streamWitnessWith r naming
      TryFind = Ops.tryFindWith r }

let private cellText = ConformalTests.cellText

// ---- the wrong ports: `Rules.shipped` with one member changed ---------------------------------------

let private bitsOf (x: float) : int64 = BitConverter.DoubleToInt64Bits x

let private referenceOf (id: string) : float =
    ElementaryVectors.references
    |> List.find (fun (vector, _) -> vector = id)
    |> snd
    |> fun hex -> BitConverter.Int64BitsToDouble(Convert.ToInt64(hex, 16))

/// The algebra's arithmetic with the correctly rounded sine and cosine where the pinned pair is a unit
/// from them (Phase 725's law file).
let private correctlyRounded: Arithmetic =
    let correctSine = referenceOf "sin-not-correctly-rounded-below-quarter-pi"
    let correctCosine = referenceOf "cos-not-correctly-rounded-reduced"
    let pinned = Arithmetic.shipped

    { pinned with
        SinCos =
            fun a ->
                let struct (s, c) = pinned.SinCos a

                if bitsOf a = bitsOf GeometricAlgebraVectors.sineOffByOne then
                    struct (correctSine, c)
                elif bitsOf a = bitsOf GeometricAlgebraVectors.cosineOffByOne then
                    struct (s, correctCosine)
                else
                    struct (s, c) }

let private rigid (op: GeometricOp<string>) =
    match op with
    | GeometricOp.Rotate _
    | GeometricOp.Translate _
    | GeometricOp.Screw _ -> true
    | _ -> false

/// The fold that composes each run of adjacent rigid ops of one object into one motor and applies it once.
let private composing
    (r: Rules<string>)
    (ops: GeometricOp<string> list)
    (s: GeometricState<string>)
    : GeometricState<string> * (int * GeometricOpRefusal<string>) list =
    let motorOf (op: GeometricOp<string>) =
        match Ops.versorOf r op with
        | Some(Ok(ByMotor m)) -> Some m
        | Some(Ok(ByTranslator t)) -> Some(Motor.ofTranslator t)
        | _ -> None

    let rec go (index: int) (state: GeometricState<string>) refusals (rest: GeometricOp<string> list) =
        match rest with
        | [] -> state, List.rev refusals
        | op :: tail ->
            let name = Ops.nameOf op

            let run =
                op
                :: (tail
                    |> List.takeWhile (fun o -> rigid o && naming.Compare (Ops.nameOf o) name = 0))

            let motors = run |> List.map motorOf

            match r.Find naming name state.Entries with
            | Some at when rigid op && List.length run >= 2 && List.forall Option.isSome motors ->
                let composed =
                    motors |> List.map Option.get |> List.reduce (fun a b -> Motor.compose b a)

                let n, o = List.item at state.Entries

                let entries =
                    state.Entries
                    |> List.mapi (fun i e -> if i = at then n, Ops.transform (ByMotor composed) o else e)

                go
                    (index + List.length run)
                    (GeometricState(naming, entries))
                    refusals
                    (List.skip (List.length run) rest)
            | _ ->
                match Ops.applyWith r op state with
                | Ok next -> go (index + 1) next refusals tail
                | Error e -> go (index + 1) state ((index, e) :: refusals) tail

    go 0 s [] ops

let private halting
    (r: Rules<string>)
    (ops: GeometricOp<string> list)
    (s: GeometricState<string>)
    : GeometricState<string> * (int * GeometricOpRefusal<string>) list =
    let rec go (index: int) state =
        function
        | [] -> state, []
        | op :: rest ->
            match Ops.applyWith r op state with
            | Ok next -> go (index + 1) next rest
            | Error e -> state, [ index, e ]

    go 0 s ops

let private normalised (o: GeometricObject) : GeometricObject =
    let orKeep (r: Result<GeometricObject, GeometryRefusal>) = r |> Result.defaultValue o

    match o with
    | GeometricObject.Point x -> Point.normalise x |> Result.map GeometricObject.Point |> orKeep
    | GeometricObject.PointPair x -> PointPair.normalise x |> Result.map GeometricObject.PointPair |> orKeep
    | GeometricObject.Line x -> Line.normalise x |> Result.map GeometricObject.Line |> orKeep
    | GeometricObject.Circle x -> Circle.normalise x |> Result.map GeometricObject.Circle |> orKeep
    | GeometricObject.Plane x -> Plane.normalise x |> Result.map GeometricObject.Plane |> orKeep
    | GeometricObject.Sphere x -> Sphere.normalise x |> Result.map GeometricObject.Sphere |> orKeep

let private namesOnly (_: Rules<string>) (op: GeometricOp<string>) (s: GeometricState<string>) =
    let name = Ops.nameOf op
    let present = (Ops.find naming name s.Entries).IsSome

    match op with
    | GeometricOp.Add _ ->
        if present then
            Error(GeometricOpRefusal.NameAlreadyPresent name)
        else
            Ok()
    | _ ->
        if present then
            Ok()
        else
            Error(GeometricOpRefusal.UnknownName name)

/// Each wrong port: its letter in the record's list, what it does, and the rules.
let internal wrongPorts: (string * Rules<string>) list =
    [ "(a) a replay that applies the ops in reverse order",
      { shipped with
          Fold = fun r ops s -> Ops.fold r (List.rev ops) s }
      "(b) a refused op that empties the state",
      { shipped with
          Refused = fun s _ -> Ok(GeometricState(s.Naming, [])) }
      "(b) a replay that halts at the first refused op", { shipped with Fold = halting }
      "(c) a versor applied to the first object in order, not to the named one",
      { shipped with
          Find = fun n name entries -> Ops.find n name entries |> Option.map (fun _ -> 0) }
      "(d) a normalisation after each transform", { shipped with After = normalised }
      "(e) a run of rigid ops of one object composed first and applied once", { shipped with Fold = composing }
      "(f) a rotation about the origin, not about the axis's point",
      { shipped with
          RotateVersor = fun _ d a -> Rotor.ofAxisAngle d a |> Result.map Motor.ofRotor }
      "(g) a screw with the translation first and the rotation after",
      { shipped with
          ScrewVersor =
              fun p d a x ->
                  match Motor.ofScrew p d a 0.0, Motor.ofScrew p d 0.0 x with
                  | Ok rotation, Ok translation -> Ok(Motor.compose rotation translation)
                  | Error e, _
                  | _, Error e -> Error e }
      "(h) a scale about the origin, not about its centre",
      { shipped with
          ScaleVersor = fun _ f -> Dilator.ofFactor f }
      "(i) a rotation's inverse that negates the direction, not the angle",
      { shipped with
          Inverse =
              function
              | GeometricOp.Rotate(n, p, (x, y, z), a) -> Some(GeometricOp.Rotate(n, p, (-x, -y, -z), a))
              | op -> Ops.inverse op }
      "(i) a screw's inverse that negates the angle and not the distance",
      { shipped with
          Inverse =
              function
              | GeometricOp.Screw(n, p, d, a, x) -> Some(GeometricOp.Screw(n, p, d, -a, x))
              | op -> Ops.inverse op }
      "(i) a scale's inverse by the factor, not by its reciprocal",
      { shipped with
          Inverse =
              function
              | GeometricOp.Scale _ as op -> Some op
              | op -> Ops.inverse op }
      "(i) a translation's inverse that negates two components of three",
      { shipped with
          Inverse =
              function
              | GeometricOp.Translate(n, (x, y, z)) -> Some(GeometricOp.Translate(n, (-x, -y, z)))
              | op -> Ops.inverse op }
      "(j) a float written by the shortest round-trip decimal formatter",
      { shipped with
          FloatText = fun x -> x.ToString("R", CultureInfo.InvariantCulture) }
      "(k) a state iterated in the order its objects were added",
      { shipped with
          Place = fun _ n o entries -> entries @ [ n, o ] }
      "(l) the chain hashed over the op's declared-order rendering",
      { shipped with
          ChainText = fun r n op -> Canonical.write Rendering.Declared (Ops.encodeJson r n op) }
      "(m) an angle read as degrees",
      { shipped with
          Angle = fun a -> a * 3.141592653589793 / 180.0 }
      "(n) a canApply that reads the name and no parameter", { shipped with Check = namesOnly }
      "the correctly rounded sine and cosine in a rotation",
      { shipped with
          RotateVersor = fun p d a -> Motion.motorOfScrew correctlyRounded p d a 0.0 }
      "the correctly rounded sine and cosine in a screw",
      { shipped with
          ScrewVersor = Motion.motorOfScrew correctlyRounded }
      "a compaction that merges rotations of one object about different axes",
      { shipped with
          Merge =
              fun n a b ->
                  match a, b with
                  | GeometricOp.Rotate(name, p, d, a1), GeometricOp.Rotate(other, _, _, a2) when
                      n.Compare name other = 0
                      ->
                      Some(GeometricOp.Rotate(name, p, d, a1 + a2))
                  | _ -> Ops.merge n a b }
      "a NaN admitted to a state", { shipped with Holds = fun _ -> true }
      "an inverse answered without asking whether the state after the op accepts it",
      { shipped with
          Undoes = fun _ _ _ _ -> Ok() } ]

/// Why a wrong port is UNABLE to change a function: the port, the reason, and the functions. Held both
/// ways by the drawn search: a function listed here answers the shipped answer on every drawn case, and
/// a function not listed fails a committed vector.
let private unable: (string * string * string list) list =
    // Which member each function reads, from the source: `apply` reads Refused, Find, Place, After, Angle,
    // the five versors and Holds; `canApply` reads Check, which reads `apply`; `applyAll` reads Fold, which
    // reads `apply`; `invert` reads Check, Inverse and Undoes, which reads `apply` and Check; `compact`
    // reads `apply` and Merge; `encode` reads FloatText; `decode` reads no member; `streamWitness` reads
    // `apply` and ChainText, which reads FloatText; `GeometricState.tryFind` reads Find. A function that
    // does not read a member cannot be changed by it: that is an argument from the source, and the drawn
    // search confirms it on its sample.
    let notReading (what: string) = sprintf "does not read %s" what

    /// A function that answers a verdict or the ops it was given, never an object: an argument where the
    /// port cannot move a verdict, stated by `why`.
    let verdictOnly (why: string) =
        "answers apply's verdict or the ops it was given, never an object, and " + why

    /// The same kind of function, for a port that changes the object a transform builds. Since an object
    /// that holds a NaN is refused (DECISIONS.md D90 item 5), such a port COULD move a verdict; this
    /// rests on the drawn search alone, which found no case.
    let searchOnly (why: string) =
        "answers apply's verdict or the ops it was given, never an object; a search, not an argument: the port changes the object a transform builds, which could move a NaN verdict, and no drawn case, narrow or wide, does so ("
        + why
        + ")"

    let allBut (functions: string list) =
        [ "GeometricOps.apply"
          "GeometricOps.canApply"
          "GeometricOps.applyAll"
          "GeometricOps.invert"
          "GeometricOps.compact"
          "GeometricOps.encode"
          "GeometricOps.decode"
          "GeometricOps.streamWitness"
          "GeometricState.tryFind" ]
        |> List.filter (fun f -> not (List.contains f functions))

    [ for port in
          [ "(a) a replay that applies the ops in reverse order"
            "(b) a replay that halts at the first refused op"
            "(e) a run of rigid ops of one object composed first and applied once" ] do
          yield port, notReading "Fold: only applyAll does", allBut [ "GeometricOps.applyAll" ]

      yield
          "(b) a refused op that empties the state",
          notReading "Refused",
          [ "GeometricOps.encode"; "GeometricOps.decode"; "GeometricState.tryFind" ]

      yield
          "(c) a versor applied to the first object in order, not to the named one",
          notReading "Find",
          [ "GeometricOps.encode"; "GeometricOps.decode" ]

      yield
          "(d) a normalisation after each transform",
          searchOnly "a normalisation divides by a weight once",
          [ "GeometricOps.canApply"; "GeometricOps.compact" ]

      yield
          "(d) a normalisation after each transform",
          notReading "After",
          [ "GeometricOps.encode"; "GeometricOps.decode"; "GeometricState.tryFind" ]

      for port in
          [ "the correctly rounded sine and cosine in a rotation"
            "the correctly rounded sine and cosine in a screw" ] do
          yield
              port,
              searchOnly "a sine and a cosine refuse nothing, and move a coefficient by a unit in the last place",
              [ "GeometricOps.canApply"; "GeometricOps.invert"; "GeometricOps.compact" ]

      yield
          "(g) a screw with the translation first and the rotation after",
          searchOnly
              "its two constructors refuse the point, the direction, the angle and the distance by the cases the shipped one does, in the same order",
          [ "GeometricOps.canApply"; "GeometricOps.invert"; "GeometricOps.compact" ]

      for port in
          [ "(f) a rotation about the origin, not about the axis's point"
            "(g) a screw with the translation first and the rotation after"
            "(h) a scale about the origin, not about its centre"
            "the correctly rounded sine and cosine in a rotation"
            "the correctly rounded sine and cosine in a screw" ] do
          yield port, notReading "a versor", [ "GeometricOps.encode"; "GeometricOps.decode"; "GeometricState.tryFind" ]

      yield
          "(h) a scale about the origin, not about its centre",
          searchOnly "a scale is never merged, so the compaction answers it as it was given",
          [ "GeometricOps.compact" ]

      for port in
          [ "(i) a rotation's inverse that negates the direction, not the angle"
            "(i) a screw's inverse that negates the angle and not the distance"
            "(i) a scale's inverse by the factor, not by its reciprocal"
            "(i) a translation's inverse that negates two components of three" ] do
          yield port, notReading "Inverse: only invert does", allBut [ "GeometricOps.invert" ]

      yield
          "(j) a float written by the shortest round-trip decimal formatter",
          notReading "FloatText: an answer that is an op is written by the published encoder",
          allBut [ "GeometricOps.encode"; "GeometricOps.streamWitness" ]

      yield
          "(k) a state iterated in the order its objects were added",
          verdictOnly "every name is found wherever it is placed, so every verdict is the shipped one",
          [ "GeometricOps.canApply"
            "GeometricOps.invert"
            "GeometricOps.compact"
            "GeometricState.tryFind" ]

      yield
          "(k) a state iterated in the order its objects were added",
          notReading "Place",
          [ "GeometricOps.encode"; "GeometricOps.decode" ]

      yield
          "(l) the chain hashed over the op's declared-order rendering",
          notReading "ChainText: only the witness does",
          allBut [ "GeometricOps.streamWitness" ]

      yield
          "(m) an angle read as degrees",
          notReading "Angle",
          [ "GeometricOps.encode"; "GeometricOps.decode"; "GeometricState.tryFind" ]

      yield
          "(n) a canApply that reads the name and no parameter",
          notReading "Check: only canApply and invert do",
          allBut [ "GeometricOps.canApply"; "GeometricOps.invert" ]

      yield
          "a compaction that merges rotations of one object about different axes",
          notReading "Merge: only compact does",
          allBut [ "GeometricOps.compact" ]

      yield
          "a NaN admitted to a state",
          notReading "Holds",
          [ "GeometricOps.encode"; "GeometricOps.decode"; "GeometricState.tryFind" ]

      yield
          "an inverse answered without asking whether the state after the op accepts it",
          notReading "Undoes: only invert does",
          allBut [ "GeometricOps.invert" ] ]

// ---- the functions and the search -----------------------------------------------------------------

let private published =
    [ "GeometricOps.apply"
      "GeometricOps.canApply"
      "GeometricOps.applyAll"
      "GeometricOps.invert"
      "GeometricOps.compact"
      "GeometricOps.encode"
      "GeometricOps.decode"
      "GeometricOps.streamWitness"
      "GeometricState.tryFind" ]

let private shippedFunctions = functionsOf shipped

let private answer (r: Rules<string>) (c: V.Case) : string =
    cellText (V.answerWith (functionsOf r) c)

let private shippedAnswers: Lazy<Map<string, string>> =
    lazy
        (V.cases
         |> List.map (fun c -> c.Id, cellText (V.answerWith shippedFunctions c))
         |> Map.ofList)

/// The functions a port fails a committed vector of, and the vector ids.
let private redUnder (r: Rules<string>) : Map<string, string list> =
    V.cases
    |> List.filter (fun c -> answer r c <> shippedAnswers.Value[c.Id])
    |> List.groupBy (fun c -> c.Op)
    |> List.map (fun (op, cs) -> op, cs |> List.map (fun c -> c.Id))
    |> Map.ofList

/// How many indices the search draws at.
let private searchSize = 400

let private drawnSample: Lazy<V.Case[]> =
    lazy
        ([| for index in 0 .. searchSize - 1 do
                yield! V.searched index
                yield! V.searchedWide index |])

let private drawnShipped: Lazy<string[]> =
    lazy (drawnSample.Value |> Array.Parallel.map (answer shipped))

/// For each function, the first drawn case a port changes.
let private changedUnder (r: Rules<string>) : Map<string, string> =
    Array.zip drawnSample.Value drawnShipped.Value
    |> Array.Parallel.choose (fun (c, s) -> if answer r c <> s then Some(c.Op, c.Id) else None)
    |> Array.groupBy fst
    |> Array.map (fun (op, cs) -> op, snd cs[0])
    |> Map.ofArray

// ---- the source -----------------------------------------------------------------------------------

let private sourceDir () =
    Snapshots.repoFile (Path.Combine("src", "Fuaran.Compute.GeometricAlgebraOps"))

let private source (file: string) =
    File.ReadAllText(Path.Combine(sourceDir (), file))

/// The operation modules: every source of the package but the law family.
let private operationModules =
    [ "GeometricOps.fs"; "GeometricAlgebraOpsOperations.fs" ]

/// The public functions of the package's operation modules, spelled `<Module>.<function>`.
let private publishedOperations () : Set<string> =
    let assembly = typeof<GeometricOpRefusal<string>>.Assembly

    assembly.GetExportedTypes()
    |> Array.filter (fun t -> t.IsAbstract && t.IsSealed && not t.IsNested)
    |> Array.collect (fun t ->
        let name =
            if t.Name.EndsWith "Module" then
                t.Name.Substring(0, t.Name.Length - 6)
            else
                t.Name

        if name = "GeometricOps" || name = "GeometricState" then
            t.GetMethods(BindingFlags.Public ||| BindingFlags.Static)
            |> Array.filter (fun m -> not m.IsSpecialName)
            |> Array.map (fun m -> name + "." + m.Name)
        else
            [||])
    |> Set.ofArray

/// The eight other seeds' maxima of each bounded law (DECISIONS.md D90), as `a bounded law's other seeds` measures them.
let private recordedOtherSeeds: string list =
    [ "a rotation undone by its inverse is the state it was, within the stated bound\t1\t11776\t2.1871393585115584E-14\t100"
      "a rotation undone by its inverse is the state it was, within the stated bound\t2\t2048\t1.7763568394002505E-14\t154"
      "a rotation undone by its inverse is the state it was, within the stated bound\t3\t22517998136852480\t1.5987211554602254E-14\t98"
      "a rotation undone by its inverse is the state it was, within the stated bound\t590\t768\t2.4868995751603507E-14\t122"
      "a rotation undone by its inverse is the state it was, within the stated bound\t727\t15762598695796736\t3.885780586188048E-15\t284"
      "a rotation undone by its inverse is the state it was, within the stated bound\t729\t7104\t3.1780134079895106E-15\t94"
      "a rotation undone by its inverse is the state it was, within the stated bound\t1000\t4096\t8.881784197001252E-15\t176"
      "a rotation undone by its inverse is the state it was, within the stated bound\t4242\t14336\t8.881784197001252E-15\t114"
      "a translation undone by its inverse is the state it was, within the stated bound\t1\t4503599627370496\t8.881784197001252E-16\t26.5"
      "a translation undone by its inverse is the state it was, within the stated bound\t2\t49539595901075460\t8.881784197001252E-16\t36"
      "a translation undone by its inverse is the state it was, within the stated bound\t3\t1024\t8.881784197001252E-16\t19.75"
      "a translation undone by its inverse is the state it was, within the stated bound\t590\t2144\t1.9984014443252818E-15\t89"
      "a translation undone by its inverse is the state it was, within the stated bound\t727\t13510798882111488\t2.220446049250313E-16\t21"
      "a translation undone by its inverse is the state it was, within the stated bound\t729\t6755399441055744\t1.0658141036401503E-14\t16"
      "a translation undone by its inverse is the state it was, within the stated bound\t1000\t4503599627370496\t2.220446049250313E-16\t29"
      "a translation undone by its inverse is the state it was, within the stated bound\t4242\t4503599627370496\t4.440892098500626E-16\t20"
      "a scale undone by its inverse is the state it was, within the stated bound\t1\t4503599627370496\t7.105427357601002E-15\t26"
      "a scale undone by its inverse is the state it was, within the stated bound\t2\t10240\t1.3766765505351941E-14\t36"
      "a scale undone by its inverse is the state it was, within the stated bound\t3\t40532396646334460\t6.217248937900877E-15\t29"
      "a scale undone by its inverse is the state it was, within the stated bound\t590\t4503599627370496\t2.6645352591003757E-15\t44"
      "a scale undone by its inverse is the state it was, within the stated bound\t727\t3553621580972032\t3.552713678800501E-15\t40"
      "a scale undone by its inverse is the state it was, within the stated bound\t729\t9570149208162304\t4.218847493575595E-14\t53.5"
      "a scale undone by its inverse is the state it was, within the stated bound\t1000\t2462906046218240\t6.217248937900877E-15\t59"
      "a scale undone by its inverse is the state it was, within the stated bound\t4242\t1.125899906842624E+17\t4.440892098500626E-15\t44"
      "a reflection undone by itself is the state it was, within the stated bound\t1\t752\t3.552713678800501E-15\t51"
      "a reflection undone by itself is the state it was, within the stated bound\t2\t512\t1.0658141036401503E-14\t49"
      "a reflection undone by itself is the state it was, within the stated bound\t3\t562949953421312\t1.7763568394002505E-15\t64"
      "a reflection undone by itself is the state it was, within the stated bound\t590\t29273397577908224\t3.552713678800501E-15\t80"
      "a reflection undone by itself is the state it was, within the stated bound\t727\t1024\t1.7763568394002505E-15\t96"
      "a reflection undone by itself is the state it was, within the stated bound\t729\t9728\t1.4210854715202004E-14\t48"
      "a reflection undone by itself is the state it was, within the stated bound\t1000\t22517998136852480\t7.105427357601002E-15\t63"
      "a reflection undone by itself is the state it was, within the stated bound\t4242\t3072\t7.105427357601002E-15\t71"
      "a screw undone by its inverse is the state it was, within the stated bound\t1\t1340\t2.4868995751603507E-14\t134"
      "a screw undone by its inverse is the state it was, within the stated bound\t2\t4503599627370496\t2.4868995751603507E-14\t64"
      "a screw undone by its inverse is the state it was, within the stated bound\t3\t20266198323167230\t9.769962616701378E-15\t64"
      "a screw undone by its inverse is the state it was, within the stated bound\t590\t2560\t3.9968028886505635E-15\t121"
      "a screw undone by its inverse is the state it was, within the stated bound\t727\t1920\t1.1546319456101628E-14\t120"
      "a screw undone by its inverse is the state it was, within the stated bound\t729\t27021597764222976\t1.509903313490213E-14\t181"
      "a screw undone by its inverse is the state it was, within the stated bound\t1000\t1888\t8.548717289613705E-15\t602.5"
      "a screw undone by its inverse is the state it was, within the stated bound\t4242\t27021597764222976\t1.1546319456101628E-14\t76"
      "a run of translations compacts to one op that reaches its state, within the stated bound\t1\t6755399441055744\t8.881784197001252E-16\t60"
      "a run of translations compacts to one op that reaches its state, within the stated bound\t2\t22517998136852480\t1.1102230246251565E-16\t24"
      "a run of translations compacts to one op that reaches its state, within the stated bound\t3\t6755399441055744\t1.6653345369377348E-16\t47"
      "a run of translations compacts to one op that reaches its state, within the stated bound\t590\t4503599627370496\t4.440892098500626E-16\t108"
      "a run of translations compacts to one op that reaches its state, within the stated bound\t727\t13510798882111488\t0\t48"
      "a run of translations compacts to one op that reaches its state, within the stated bound\t729\t22517998136852480\t0\t61"
      "a run of translations compacts to one op that reaches its state, within the stated bound\t1000\t4503599627370496\t0\t28"
      "a run of translations compacts to one op that reaches its state, within the stated bound\t4242\t2251799813685248\t0\t16"
      "a run of rotations and screws about one axis compacts to one op that reaches its state, within the stated bound\t1\t6656\t0\t72"
      "a run of rotations and screws about one axis compacts to one op that reaches its state, within the stated bound\t2\t29243826285682932\t0\t141"
      "a run of rotations and screws about one axis compacts to one op that reaches its state, within the stated bound\t3\t8896\t0\t64"
      "a run of rotations and screws about one axis compacts to one op that reaches its state, within the stated bound\t590\t65536\t2.220446049250313E-16\t149"
      "a run of rotations and screws about one axis compacts to one op that reaches its state, within the stated bound\t727\t4096\t0\t76"
      "a run of rotations and screws about one axis compacts to one op that reaches its state, within the stated bound\t729\t4608\t0\t51"
      "a run of rotations and screws about one axis compacts to one op that reaches its state, within the stated bound\t1000\t3377699720527872\t0\t345"
      "a run of rotations and screws about one axis compacts to one op that reaches its state, within the stated bound\t4242\t11264\t0\t164" ]

[<Tests>]
let tests =
    testList
        "Geometric algebra ops"
        [

          testCase "the laws hold on .NET"
          <| fun () ->
              let failures =
                  GeometricAlgebraOpsLaws.laws 590 200
                  |> List.choose (fun (name, verdict) ->
                      match verdict with
                      | Ok() -> None
                      | Error why -> Some(name + ": " + why))

              Expect.isEmpty failures "every law of the op algebra holds"
              Expect.equal (GeometricAlgebraOpsLaws.laws 590 1 |> List.length) 17 "seventeen laws are run"

          testCase "every law of the family holds at eight other seeds"
          <| fun () ->
              let failures =
                  [ for seed in [ 1; 2; 3; 727; 729; 731; 1000; 4242 ] do
                        for name, verdict in GeometricAlgebraOpsLaws.laws seed 200 do
                            match verdict with
                            | Ok() -> ()
                            | Error why -> yield sprintf "seed %d, %s: %s" seed name why ]

              Expect.isEmpty failures "every law holds at every seed"

          testCase "an encoded op carries parameters and no coefficient"
          <| fun () ->
              // D85 item 10: what the wire carries is parametric. Every member of every encoded op and form
              // is a parameter of D85 item 9's list, and every float member is one parameter (sixteen hex
              // digits) or one triple (forty-eight): never a coefficient list.
              let parameters =
                  set
                      [ "$type"
                        "name"
                        "form"
                        "point"
                        "first"
                        "second"
                        "direction"
                        "centre"
                        "radius"
                        "normal"
                        "distance"
                        "angle"
                        "vector"
                        "factor" ]

              let rec members (v: JVal) : (string * JVal) list =
                  match v with
                  | JObj fields -> fields @ (fields |> List.collect (fun (_, x) -> members x))
                  | _ -> []

              let texts =
                  [ for c in V.authored do
                        match c.Op, c.Input with
                        | "GeometricOps.encode", [ _, V.OneOp op ] -> yield GeometricOps.encode naming op
                        | _ -> () ]

              Expect.isGreaterThan (List.length texts) 15 "every op and every form is encoded"

              for text in texts do
                  match Canonical.read text with
                  | Error e -> failtestf "%s is not JSON: %A" text e
                  | Ok v ->
                      for key, value in members v do
                          Expect.isTrue (parameters.Contains key) (sprintf "%s is a parameter of %s" key text)

                          match key, value with
                          | ("$type" | "name"), _
                          | "form", _ -> ()
                          | ("radius" | "distance" | "angle" | "factor"), JStr s -> Expect.equal s.Length 16 key
                          | _, JStr s -> Expect.equal s.Length 48 key
                          | _ -> failtestf "%s of %s is not a bits string" key text

          testCase "the committed law vectors are the package's answers"
          <| fun () ->
              let committed = File.ReadAllText(committedPath ())

              match CorpusFile.firstDifference committed (V.render ()) with
              | None -> ()
              | Some d ->
                  failtestf
                      "%s is stale at line %d:\n  committed: %A\n  rendered:  %A\n  Remedy: dotnet run --project tests/Fuaran.Core.Compute.Tests -- --emit-laws"
                      V.fileName
                      d.Line
                      d.Committed
                      d.Rendered

          testCase "the functions over the shipped rules are the published functions"
          <| fun () ->
              for c in V.cases do
                  Expect.equal
                      (cellText (V.answerWith shippedFunctions c))
                      (cellText (V.answerWith V.published c))
                      (sprintf "%s answers the same through the rules record" c.Id)

          testCase
              "GeometricAlgebraOpsOperations.exactness classifies exactly the published operations, every one Pinned"
          <| fun () ->
              let declared = GeometricAlgebraOpsOperations.exactness |> List.map fst
              Expect.equal (List.distinct declared) declared "no operation is classified twice"
              Expect.equal (Set.ofList declared) (publishedOperations ()) "the table is the published operation set"

              Expect.isTrue
                  (GeometricAlgebraOpsOperations.exactness
                   |> List.forall (fun (_, e) -> e = GeometricAlgebraOpsOperations.Exactness.Pinned))
                  "every operation is Pinned"

              Expect.equal
                  (GeometricAlgebraOpsOperations.exactnessOf "GeometricOps.apply")
                  (Some GeometricAlgebraOpsOperations.Exactness.Pinned)
                  "a lookup"

              Expect.equal (GeometricAlgebraOpsOperations.exactnessOf "GeometricOps.merge") None "no such operation"

              // Every function that computes has a committed vector.
              let vectored = V.cases |> List.map (fun c -> c.Op) |> Set.ofList
              Expect.equal vectored (Set.ofList published) "every function that computes has a vector"

          testCase "each wrong port fails a vector of a function, or a drawn search finds it unable to change it"
          <| fun () ->
              // The counts DECISIONS.md D90 states, read from the tree.
              Expect.equal
                  drawnSample.Value.Length
                  7200
                  "the drawn sample: 400 indices, narrow and wide, one case of each function"

              Expect.equal (List.length wrongPorts) 23 "twenty-three wrong ports"
              Expect.equal (List.length published) 9 "nine functions that compute"
              Expect.equal (List.length V.cases) 240 "240 committed vectors"

              let findings =
                  [ for name, port in wrongPorts do
                        let red = redUnder port |> Map.toList |> List.map fst |> Set.ofList

                        let declared =
                            unable
                            |> List.filter (fun (p, _, _) -> p = name)
                            |> List.collect (fun (_, _, functions) -> functions)

                        if List.distinct declared <> declared then
                            yield sprintf "REPEATED\t%s" name

                        if Set.isEmpty red then
                            yield sprintf "VACUOUS\t%s fails no committed vector at all" name

                        for f in Set.difference (Set.difference (Set.ofList published) red) (Set.ofList declared) do
                            yield sprintf "UNLISTED\t%s\t%s" name f

                        for f in Set.intersect red (Set.ofList declared) do
                            yield sprintf "LISTED-BUT-RED\t%s\t%s" name f

                        for KeyValue(f, id) in changedUnder port do
                            if not (red.Contains f) then
                                yield sprintf "MISSING\t%s\t%s\t%s" name f id ]

              if not (List.isEmpty findings) then
                  failtestf "the wrong ports and the vectors disagree:\n%s" (String.concat "\n" findings)

          testCase "every transform applies to every kind of object"
          <| fun () ->
              let transforms =
                  [ GeometricOp.Rotate("b", (1.0, 0.0, 0.0), (0.0, 0.0, 1.0), 1.0)
                    GeometricOp.Translate("b", (1.0, 2.0, 3.0))
                    GeometricOp.Scale("b", (1.0, 0.0, 0.0), 2.0)
                    GeometricOp.Reflect("b", (0.0, 1.0, 0.0), 1.0)
                    GeometricOp.Screw("b", (1.0, 0.0, 0.0), (0.0, 0.0, 1.0), 1.0, 2.0) ]

              let forms =
                  [ Form.Point(1.0, 2.0, 3.0)
                    Form.PointPair((1.0, 0.0, 0.0), (0.0, 1.0, 0.0))
                    Form.Line((0.0, 0.0, 0.0), (1.0, 0.0, 0.0))
                    Form.Circle((0.0, 0.0, 0.0), 1.0, (0.0, 0.0, 1.0))
                    Form.Plane((0.0, 0.0, 1.0), 1.0)
                    Form.Sphere((0.0, 0.0, 0.0), 1.0) ]

              for form in forms do
                  for op in transforms do
                      let s, refusals =
                          GeometricOps.applyAll [ GeometricOp.Add("b", form); op ] (GeometricState.empty naming)

                      Expect.isEmpty refusals (sprintf "%A applies to %A" op form)
                      Expect.equal (GeometricState.count s) 1 "one object"

          testCase "a rotation is the screw of no distance, which is (T_p R) rev(T_p) bit for bit"
          <| fun () ->
              // D90's reading of D85 item 4 for `Rotate`, held on the authored axes and angles: the published
              // screw at a distance of zero against the product D85 item 4 writes inside the screw.
              for p, d, a in
                  [ (1.0, -2.0, 0.5), (0.0, 1.0, 1.0), 0.75
                    (1.0, 0.0, -1.0), (1.0, 1.0, 0.0), V.sineAngle
                    (3.0, 0.25, -7.5), (2.0, -1.0, 3.0), V.cosineAngle
                    (-0.0, 0.0, 0.0), (0.0, 0.0, 1.0), -0.0 ] do
                  match Motor.ofScrew p d a 0.0, Translator.ofVector p, Rotor.ofAxisAngle d a with
                  | Ok screw, Ok toAxis, Ok rotor ->
                      let gp = Multivector.geometricProduct
                      let t = Translator.toMultivector toAxis

                      let product =
                          gp (gp t (Rotor.toMultivector rotor)) (Multivector.reverse t)
                          |> Motor.ofMultivector

                      Expect.equal
                          (Motor.toMultivector screw |> Multivector.toArray |> Array.map bitsOf)
                          (Motor.toMultivector product |> Multivector.toArray |> Array.map bitsOf)
                          (sprintf "the rotation about %A through %A by %g" d p a)
                  | _ -> failtest "a constructor refused an authored axis"

          testCase "nothing in apply, the codec or the witness calls the compaction"
          <| fun () ->
              // The compaction is the last section of `Ops`, after `apply`, the codec and the witness: F#
              // reads a file top to bottom, so nothing above it can name it. This holds the section's place
              // and that no member of the record but `Merge` names it.
              let text = ConformalTests.codeOf (source "GeometricOps.fs")
              let compaction = text.IndexOf "let private sameBits"
              let witness = text.IndexOf "let streamWitnessWith"
              Expect.isGreaterThan compaction witness "the compaction follows the witness"
              let before = text.Substring(0, compaction)

              for name in [ "compactWith"; "merge"; ".Merge" ] do
                  Expect.isFalse (before.Contains name) (sprintf "nothing before the compaction names %s" name)

              let published = text.Substring(text.IndexOf "module GeometricOps")

              for name in
                  [ "let apply"
                    "let canApply"
                    "let applyAll"
                    "let encode"
                    "let decode"
                    "let streamWitness" ] do
                  let at = published.IndexOf name
                  let next = published.IndexOf("let ", at + 4)
                  let body = published.Substring(at, next - at)
                  Expect.isFalse (body.Contains "compact") (sprintf "%s does not call the compaction" name)

          testCase "the op package's operation modules hold no float literal in a comparison"
          <| fun () ->
              // ConformalTests' census reads the algebra package's modules only, so the rule (D85 item 6, read
              // with D90) is held here over this package's: every float literal is accounted for, and none
              // is in a comparison.
              let found =
                  [ for file in operationModules do
                        for literal in ConformalTests.floatLiterals (source file) -> file, literal ]
                  |> List.countBy id
                  |> List.sort

              Expect.equal
                  found
                  [ ("GeometricOps.fs", "0.0"), 1
                    ("GeometricOps.fs", "1.0"), 1
                    ("GeometricOps.fs", "NaN"), 1 ]
                  "the rotation's distance of zero, the inverse's reciprocal and the NaN a decoded NaN pattern reads as"

              // None is in a comparison: no comparison operator stands beside a float literal.
              for file in operationModules do
                  let code = ConformalTests.codeOf (source file)

                  Expect.isFalse
                      (Regex.IsMatch(code, @"(?:[<>=]=?|<>)\s*-?\d+\.\d|\d+\.\d+\s*(?:[<>=]=?|<>)"))
                      (sprintf "%s compares nothing with a float literal" file)

          testCase "the package calls no host mathematics function"
          <| fun () ->
              for file in
                  [ "GeometricOps.fs"
                    "GeometricAlgebraOpsOperations.fs"
                    "GeometricAlgebraOpsLaws.fs" ] do
                  let code = ConformalTests.codeOf (source file)

                  Expect.isFalse
                      (Regex.IsMatch(
                          code,
                          @"\b(?:Math|MathF|Double)\.(?:Sin|Cos|Tan|Atan|Atan2|Exp|Log|Pow|Sqrt|FusedMultiplyAdd)\b|\b(?:sin|cos|tan|atan|atan2|exp|log|sqrt|pown)\s"
                      ))
                      (sprintf "%s calls no host mathematics function" file)

          testCase "a bounded law's other seeds measure the figures D90 records"
          <| fun () ->
              let measured =
                  [ for name, sample, _ in GeometricAlgebraOpsLaws.bounds do
                        for seed in [ 1; 2; 3; 590; 727; 729; 1000; 4242 ] ->
                            match sample seed with
                            | Ok(ulps, absolute, scaled) ->
                                let r (x: float) =
                                    x.ToString("R", CultureInfo.InvariantCulture)

                                sprintf "%s\t%d\t%s\t%s\t%s" name seed (r ulps) (r absolute) (r scaled)
                            | Error e -> sprintf "%s\t%d\t%s" name seed e ]

              Expect.equal measured recordedOtherSeeds "the eight other seeds' maxima" ]
