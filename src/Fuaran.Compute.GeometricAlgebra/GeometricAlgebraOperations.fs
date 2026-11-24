namespace Fuaran.Compute.GeometricAlgebra

/// Every public operation of this package with its exactness class (DECISIONS.md D43, D85 item 7), as
/// data a schema walk reads through. Every operation is `Pinned`: no host mathematics function is
/// called on any path, and no product is fused, so each answers the same bits on every conformant
/// host. What stands behind the class differs by row:
///
///   * `init`, `ofScalar`, `basis`, `coefficient`, `toArray`, `grade`, `negate`, `reverse`,
///     `gradeInvolution`, `dual` and `undual` are copies and exact negation: exact on every input.
///   * `add`, `subtract` and `scale` are one `+`, `−` or `×` for each coefficient: correctly rounded,
///     coefficient by coefficient.
///   * `geometricProduct`, `outerProduct`, `leftContraction`, `scalarProduct` and `squaredNorm` are
///     D85 item 3's fold: exact whenever every product and every partial sum is representable, and
///     otherwise the fold's rounding, the same bits on every host.
///   * Of the objects' functions (Phase 728, D87): `toMultivector`, `ofMultivector`, a point's and a
///     sphere's `weight`, a line's `direction` and `moment` and a plane's `normal` and `distance` are
///     copies and exact negation. A point's `coordinates` and the `normalise` of a point and of a
///     sphere are one `÷` for each coefficient: correctly rounded. `Blade.join`, `Blade.meet`, every
///     `through`, `meet`, `meetPlane` and `meetSphere`, `Circle.plane`, `Point.up`, the centres, the
///     squared radii, `Blade.inverse`, `Blade.meetWithin`, the nearest points, a sphere from
///     parameters, and the three statistics with their tests are item 3's folds, and `÷` where D85
///     item 4 or 6 writes one. The other weights, the other normalisations, an object built from a
///     direction or a normal, a point pair's `points`, the `kind` of a blade and `Line.meet` are
///     those and `√`, which is correctly rounded.
///   * Of the versors' functions (Phase 729, D88): `toMultivector`, `ofMultivector`, `Motor.ofRotor`
///     and `Motor.ofTranslator` are copies. `Translator.ofVector` is one exact halving and a negation
///     for each component. Every `applyTo…`, every `compose` and `Motor.ofRotorTranslator` are item
///     3's folds. Every `toMatrix` is those and `÷`. Every `normalise`, the two dilators and
///     `Motor.ofMatrix` are those and `√`. `Rotor.ofAxisAngle`, `Motor.ofScrew`, `Motor.ofLine`, the
///     two `exp` and the two `interpolate` are those and `Fuaran.Compute.Elementary`'s pinned
///     `sinCos`, asked once; the two `log` and the two `interpolate` are those and its pinned `atan2`,
///     asked once. They answer the same bits on every host and are as accurate as D84 measured the
///     two pinned functions, which is not correct rounding.
///
/// The suite holds the table to the package's public surface both ways. The module is named for its
/// package, because two opened modules named `Operations` shadow each other member by member
/// (DECISIONS.md D74 item 2).
[<RequireQualifiedAccess>]
module GeometricAlgebraOperations =

    /// How exact an operation's bytes are across hosts — D43's vocabulary. Declared inside this
    /// module, so `open Fuaran.Compute.GeometricAlgebra` brings no type named `Exactness` into a
    /// caller's scope.
    [<RequireQualifiedAccess>]
    type Exactness =
        | Pinned
        | HostRounded

    /// `(operation, class)`, the operation spelled `<Module>.<function>`, in surface order.
    let exactness: (string * Exactness) list =
        [ "Multivector.init", Exactness.Pinned
          "Multivector.ofScalar", Exactness.Pinned
          "Multivector.basis", Exactness.Pinned
          "Multivector.coefficient", Exactness.Pinned
          "Multivector.toArray", Exactness.Pinned
          "Multivector.grade", Exactness.Pinned
          "Multivector.add", Exactness.Pinned
          "Multivector.subtract", Exactness.Pinned
          "Multivector.scale", Exactness.Pinned
          "Multivector.negate", Exactness.Pinned
          "Multivector.reverse", Exactness.Pinned
          "Multivector.gradeInvolution", Exactness.Pinned
          "Multivector.dual", Exactness.Pinned
          "Multivector.undual", Exactness.Pinned
          "Multivector.geometricProduct", Exactness.Pinned
          "Multivector.outerProduct", Exactness.Pinned
          "Multivector.leftContraction", Exactness.Pinned
          "Multivector.scalarProduct", Exactness.Pinned
          "Multivector.squaredNorm", Exactness.Pinned
          "Blade.join", Exactness.Pinned
          "Blade.meet", Exactness.Pinned
          "Blade.inverse", Exactness.Pinned
          "Blade.meetWithin", Exactness.Pinned
          "Point.up", Exactness.Pinned
          "Point.coordinates", Exactness.Pinned
          "Point.weight", Exactness.Pinned
          "Point.normalise", Exactness.Pinned
          "Point.toMultivector", Exactness.Pinned
          "Point.ofMultivector", Exactness.Pinned
          "PointPair.through", Exactness.Pinned
          "PointPair.weight", Exactness.Pinned
          "PointPair.normalise", Exactness.Pinned
          "PointPair.centre", Exactness.Pinned
          "PointPair.squaredRadius", Exactness.Pinned
          "PointPair.points", Exactness.Pinned
          "PointPair.kind", Exactness.Pinned
          "PointPair.toMultivector", Exactness.Pinned
          "PointPair.ofMultivector", Exactness.Pinned
          "Line.through", Exactness.Pinned
          "Line.ofPointDirection", Exactness.Pinned
          "Line.weight", Exactness.Pinned
          "Line.normalise", Exactness.Pinned
          "Line.direction", Exactness.Pinned
          "Line.moment", Exactness.Pinned
          "Line.nearestPoint", Exactness.Pinned
          "Line.kind", Exactness.Pinned
          "Line.meet", Exactness.Pinned
          "Line.meetPlane", Exactness.Pinned
          "Line.meetSphere", Exactness.Pinned
          "Line.toMultivector", Exactness.Pinned
          "Line.ofMultivector", Exactness.Pinned
          "Circle.through", Exactness.Pinned
          "Circle.ofCentreRadiusNormal", Exactness.Pinned
          "Circle.weight", Exactness.Pinned
          "Circle.normalise", Exactness.Pinned
          "Circle.centre", Exactness.Pinned
          "Circle.squaredRadius", Exactness.Pinned
          "Circle.kind", Exactness.Pinned
          "Circle.plane", Exactness.Pinned
          "Circle.power", Exactness.Pinned
          "Circle.side", Exactness.Pinned
          "Circle.toMultivector", Exactness.Pinned
          "Circle.ofMultivector", Exactness.Pinned
          "Plane.through", Exactness.Pinned
          "Plane.ofNormalDistance", Exactness.Pinned
          "Plane.weight", Exactness.Pinned
          "Plane.normalise", Exactness.Pinned
          "Plane.normal", Exactness.Pinned
          "Plane.distance", Exactness.Pinned
          "Plane.nearestPoint", Exactness.Pinned
          "Plane.kind", Exactness.Pinned
          "Plane.meet", Exactness.Pinned
          "Plane.signedDistance", Exactness.Pinned
          "Plane.side", Exactness.Pinned
          "Plane.toMultivector", Exactness.Pinned
          "Plane.ofMultivector", Exactness.Pinned
          "Sphere.through", Exactness.Pinned
          "Sphere.ofCentreRadius", Exactness.Pinned
          "Sphere.weight", Exactness.Pinned
          "Sphere.normalise", Exactness.Pinned
          "Sphere.centre", Exactness.Pinned
          "Sphere.squaredRadius", Exactness.Pinned
          "Sphere.kind", Exactness.Pinned
          "Sphere.meet", Exactness.Pinned
          "Sphere.meetPlane", Exactness.Pinned
          "Sphere.power", Exactness.Pinned
          "Sphere.side", Exactness.Pinned
          "Sphere.toMultivector", Exactness.Pinned
          "Sphere.ofMultivector", Exactness.Pinned
          "Rotor.ofAxisAngle", Exactness.Pinned
          "Rotor.exp", Exactness.Pinned
          "Rotor.log", Exactness.Pinned
          "Rotor.interpolate", Exactness.Pinned
          "Rotor.compose", Exactness.Pinned
          "Rotor.applyToPoint", Exactness.Pinned
          "Rotor.applyToPointPair", Exactness.Pinned
          "Rotor.applyToLine", Exactness.Pinned
          "Rotor.applyToCircle", Exactness.Pinned
          "Rotor.applyToPlane", Exactness.Pinned
          "Rotor.applyToSphere", Exactness.Pinned
          "Rotor.toMatrix", Exactness.Pinned
          "Rotor.normalise", Exactness.Pinned
          "Rotor.toMultivector", Exactness.Pinned
          "Rotor.ofMultivector", Exactness.Pinned
          "Translator.ofVector", Exactness.Pinned
          "Translator.compose", Exactness.Pinned
          "Translator.applyToPoint", Exactness.Pinned
          "Translator.applyToPointPair", Exactness.Pinned
          "Translator.applyToLine", Exactness.Pinned
          "Translator.applyToCircle", Exactness.Pinned
          "Translator.applyToPlane", Exactness.Pinned
          "Translator.applyToSphere", Exactness.Pinned
          "Translator.toMatrix", Exactness.Pinned
          "Translator.normalise", Exactness.Pinned
          "Translator.toMultivector", Exactness.Pinned
          "Translator.ofMultivector", Exactness.Pinned
          "Motor.ofRotorTranslator", Exactness.Pinned
          "Motor.ofRotor", Exactness.Pinned
          "Motor.ofTranslator", Exactness.Pinned
          "Motor.ofScrew", Exactness.Pinned
          "Motor.ofLine", Exactness.Pinned
          "Motor.exp", Exactness.Pinned
          "Motor.log", Exactness.Pinned
          "Motor.interpolate", Exactness.Pinned
          "Motor.compose", Exactness.Pinned
          "Motor.applyToPoint", Exactness.Pinned
          "Motor.applyToPointPair", Exactness.Pinned
          "Motor.applyToLine", Exactness.Pinned
          "Motor.applyToCircle", Exactness.Pinned
          "Motor.applyToPlane", Exactness.Pinned
          "Motor.applyToSphere", Exactness.Pinned
          "Motor.toMatrix", Exactness.Pinned
          "Motor.ofMatrix", Exactness.Pinned
          "Motor.normalise", Exactness.Pinned
          "Motor.toMultivector", Exactness.Pinned
          "Motor.ofMultivector", Exactness.Pinned
          "Dilator.ofFactor", Exactness.Pinned
          "Dilator.ofCentreFactor", Exactness.Pinned
          "Dilator.compose", Exactness.Pinned
          "Dilator.applyToPoint", Exactness.Pinned
          "Dilator.applyToPointPair", Exactness.Pinned
          "Dilator.applyToLine", Exactness.Pinned
          "Dilator.applyToCircle", Exactness.Pinned
          "Dilator.applyToPlane", Exactness.Pinned
          "Dilator.applyToSphere", Exactness.Pinned
          "Dilator.toMatrix", Exactness.Pinned
          "Dilator.normalise", Exactness.Pinned
          "Dilator.toMultivector", Exactness.Pinned
          "Dilator.ofMultivector", Exactness.Pinned
          "Reflection.ofNormalDistance", Exactness.Pinned
          "Reflection.compose", Exactness.Pinned
          "Reflection.applyToPoint", Exactness.Pinned
          "Reflection.applyToPointPair", Exactness.Pinned
          "Reflection.applyToLine", Exactness.Pinned
          "Reflection.applyToCircle", Exactness.Pinned
          "Reflection.applyToPlane", Exactness.Pinned
          "Reflection.applyToSphere", Exactness.Pinned
          "Reflection.normalise", Exactness.Pinned
          "Reflection.toMultivector", Exactness.Pinned
          "Reflection.ofMultivector", Exactness.Pinned ]

    /// The class of one operation, or `None` for a name this package does not publish.
    let exactnessOf (operation: string) : Exactness option =
        exactness |> List.tryFind (fun (o, _) -> o = operation) |> Option.map snd
